using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WorkActivityPanel.Helpers;
using WorkActivityPanel.Models;
using WorkActivityPanel.Services.Interfaces;

namespace WorkActivityPanel.Services;

/// <summary>
/// Implementation of Google Drive synchronization service using Google Apps Script Web App bridge.
/// </summary>
public class DriveSyncService : IDriveSyncService, IDisposable
{
    private const string SettingsKey = "DriveSyncSettings";
    private static readonly string DataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WorkActivityPanel",
        "Data");
    private static readonly string HashIndexFile = Path.Combine(DataDirectory, "sync_hashes.json");
    private static readonly string ErrorsFile = Path.Combine(DataDirectory, "sync_errors.json");

    private readonly HttpClient _httpClient;
    private readonly IScheduleService _scheduleService;
    private readonly ClaudeConfigDiscovery _claudeConfigDiscovery = new();
    private readonly SemaphoreSlim _uploadSemaphore = new(1, 1);
    private readonly object _hashLock = new();
    private readonly object _errorLock = new();
    private readonly List<SyncErrorItem> _lastSyncErrors = new();
    private Dictionary<string, HashCacheEntry> _hashIndex = new(StringComparer.OrdinalIgnoreCase);

    private DriveSyncSettings _settings;
    private CancellationTokenSource? _activeCts;
    private readonly object _ctsLock = new();
    private int _isSyncing; // 0 = idle, 1 = syncing

    public DriveSyncSettings Settings => _settings;
    public bool IsSyncing => Volatile.Read(ref _isSyncing) == 1;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_settings.WebAppUrl)
                                && (EnumerateSources().Any() || _settings.SyncUnversionedClaudeMarkdown);

    public IReadOnlyList<SyncErrorItem> LastSyncErrors
    {
        get
        {
            lock (_errorLock)
            {
                return _lastSyncErrors.ToList().AsReadOnly();
            }
        }
    }

    public event EventHandler? SettingsChanged;
    public event EventHandler<SyncProgressReport>? SyncProgressChanged;
    public event EventHandler<SyncResultSummary>? SyncCompleted;
    public event EventHandler<IReadOnlyList<SyncErrorItem>>? SyncErrorsChanged;

    public DriveSyncService(IScheduleService scheduleService)
    {
        _scheduleService = scheduleService;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _settings = LoadSettings();
        LoadHashIndex();
        LoadSyncErrors();

        _scheduleService.WorkEnded += OnWorkEnded;
    }


    private void OnWorkEnded(object? sender, EventArgs e)
    {
        if (_settings.IsEnabled && _settings.AutoSyncOnWorkEnd && IsConfigured && !_scheduleService.IsVacationMode)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await RunSyncAsync();
                }
                catch
                {
                    // Ignore silent background auto-sync failure
                }
            });
        }
    }

    public void UpdateSettings(DriveSyncSettings settings)
    {
        // Invalidation Guard: If the Claude destination prefix changed, invalidate cached hashes for it
        if (!string.Equals(_settings.ClaudeMarkdownDestinationPrefix, settings.ClaudeMarkdownDestinationPrefix, StringComparison.OrdinalIgnoreCase))
        {
            InvalidateHashesByPrefix(_settings.ClaudeMarkdownDestinationPrefix);
        }

        // Invalidation Guard: If WebAppUrl changed, clear all hashes to force fresh sync to new target
        if (!string.Equals(_settings.WebAppUrl, settings.WebAppUrl, StringComparison.OrdinalIgnoreCase))
        {
            ClearHashIndex();
        }

        _settings = settings;
        SaveSettings();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearHashIndex()
    {
        lock (_hashLock)
        {
            _hashIndex.Clear();
            SaveHashIndex();
        }
    }

    private void InvalidateHashesByPrefix(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix)) return;

        lock (_hashLock)
        {
            var prefixNormalized = prefix.Replace('\\', '/').Trim('/');
            var keysToRemove = _hashIndex.Keys
                .Where(k => k.StartsWith(prefixNormalized + "/", StringComparison.OrdinalIgnoreCase) ||
                            k.StartsWith(prefixNormalized + "|", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var key in keysToRemove)
            {
                _hashIndex.Remove(key);
            }

            if (keysToRemove.Count > 0)
            {
                SaveHashIndex();
            }
        }
    }

    public void CancelSync()
    {
        lock (_ctsLock)
        {
            try
            {
                if (_activeCts != null && !_activeCts.IsCancellationRequested)
                {
                    _activeCts.Cancel();
                }
            }
            catch (ObjectDisposedException)
            {
                // Ignored safely if already disposed
            }
        }
    }

    public async Task<SyncResultSummary> RunSyncAsync(
        IProgress<SyncProgressReport>? progress = null,
        CancellationToken cancellationToken = default,
        bool forceFullSync = false)
    {
        if (Interlocked.CompareExchange(ref _isSyncing, 1, 0) != 0)
        {
            return new SyncResultSummary
            {
                Message = "Ya hay una sincronización en curso."
            };
        }

        if (!IsConfigured)
        {
            Interlocked.Exchange(ref _isSyncing, 0);
            return new SyncResultSummary
            {
                Message = "Configuración incompleta: Verifica la URL del Web App y la carpeta local."
            };
        }

        CancellationToken token;
        lock (_ctsLock)
        {
            _activeCts?.Dispose();
            _activeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            token = _activeCts.Token;
        }

        var summary = new SyncResultSummary();
        var currentRunErrors = new List<SyncErrorItem>();

        try
        {
            ReportProgress(progress, new SyncProgressReport
            {
                StatusMessage = "Escaneando archivos locales..."
            });

            var localFiles = await CollectFilesAsync(progress, token);
            summary.TotalScanned = localFiles.Count;

            int processed = 0;

            foreach (var file in localFiles)
            {
                if (token.IsCancellationRequested)
                {
                    summary.Message = "Sincronización cancelada por el usuario.";
                    break;
                }

                processed++;

                // 1. Resolve the file hash lazily with the fast-path cache.
                //    IsMetadataConfirmed does the single FileInfo stat to check LastWriteTimeUtc + FileSize.
                //    GetKnownHash is a pure cache lookup (no disk I/O). This avoids duplicate stat calls.
                //    When metadata matches, the file is skipped with no disk read. When it does not
                //    (new file, size/mtime changed, legacy entry with ticks=0, or file < 1 KB),
                //    we fall through to ComputeSha256.
                string currentHash;
                FileInfo? uploadFileInfo = null;
                if (!forceFullSync && _settings.OnlyModifiedOrNew)
                {
                    string? cachedHash = GetKnownHash(file.HashKey);

                    // IsMetadataConfirmed does the single authoritative FileInfo stat under _hashLock.
                    bool metadataConfirmed = cachedHash != null && IsMetadataConfirmed(file.HashKey, file.FilePath, file.FileSize);

                    if (metadataConfirmed)
                    {
                        // Fast-path: metadata identical, file unchanged, no disk read needed.
                        summary.Skipped++;
                        ReportProgress(progress, new SyncProgressReport
                        {
                            TotalFiles = summary.TotalScanned,
                            ProcessedFiles = processed,
                            CurrentFileName = file.FileName,
                            UploadedCount = summary.Uploaded,
                            SkippedCount = summary.Skipped,
                            ErrorCount = summary.Errors,
                            StatusMessage = $"Sin cambios: {file.FileName}"
                        });
                        continue;
                    }

                    // Metadata changed or unavailable — compute the real hash from disk.
                    uploadFileInfo = new FileInfo(file.FilePath);
                    currentHash = ComputeSha256(file.FilePath);
                    file.Hash = currentHash;

                    if (cachedHash != null &&
                        string.Equals(cachedHash, currentHash, StringComparison.OrdinalIgnoreCase))
                    {
                        // Hash matches even though metadata mismatched (e.g. git checkout restored mtime,
                        // or xcopy preserved timestamp). Upgrade the cache entry with fresh metadata.
                        SaveKnownHash(file.HashKey, currentHash, uploadFileInfo);
                        summary.Skipped++;
                        ReportProgress(progress, new SyncProgressReport
                        {
                            TotalFiles = summary.TotalScanned,
                            ProcessedFiles = processed,
                            CurrentFileName = file.FileName,
                            UploadedCount = summary.Uploaded,
                            SkippedCount = summary.Skipped,
                            ErrorCount = summary.Errors,
                            StatusMessage = $"Sin cambios: {file.FileName}"
                        });
                        continue;
                    }
                }
                else
                {
                    // Full sync or incremental check disabled: compute hash for post-upload persistence.
                    uploadFileInfo = new FileInfo(file.FilePath);
                    currentHash = ComputeSha256(file.FilePath);
                    file.Hash = currentHash;
                }

                // 2. Upload file to Google Drive
                try
                {
                    ReportProgress(progress, new SyncProgressReport
                    {
                        TotalFiles = summary.TotalScanned,
                        ProcessedFiles = processed,
                        CurrentFileName = file.FileName,
                        UploadedCount = summary.Uploaded,
                        SkippedCount = summary.Skipped,
                        ErrorCount = summary.Errors,
                        StatusMessage = $"Subiendo ({processed}/{summary.TotalScanned}): {file.FileName}..."
                    });

                    var fileId = await UploadSingleFileAsync(file.FilePath, _settings.WebAppUrl, file.RelativePath);
                    if (string.IsNullOrWhiteSpace(fileId))
                    {
                        throw new Exception("Google Apps Script no confirmó el identificador del archivo creado (fileId).");
                    }

                    SaveKnownHash(file.HashKey, file.Hash, uploadFileInfo);
                    summary.Uploaded++;

                    // Throttle between uploads to avoid Google Apps Script burst rate-limits
                    await Task.Delay(300, token);
                }
                catch (Exception ex)
                {
                    summary.Errors++;
                    var (category, friendlyMsg) = CategorizeError(ex, file.FilePath);
                    var errorItem = new SyncErrorItem
                    {
                        FileName = file.FileName,
                        FilePath = file.FilePath,
                        RelativePath = file.RelativePath,
                        HashKey = file.HashKey,
                        Hash = file.Hash,
                        ErrorMessage = friendlyMsg,
                        ErrorCategory = category,
                        Timestamp = DateTime.Now
                    };
                    currentRunErrors.Add(errorItem);
                    summary.FailedFiles.Add(errorItem);

                    ReportProgress(progress, new SyncProgressReport
                    {
                        TotalFiles = summary.TotalScanned,
                        ProcessedFiles = processed,
                        CurrentFileName = file.FileName,
                        UploadedCount = summary.Uploaded,
                        SkippedCount = summary.Skipped,
                        ErrorCount = summary.Errors,
                        StatusMessage = $"Error al subir {file.FileName}: {friendlyMsg}"
                    });
                }
            }

            lock (_errorLock)
            {
                _lastSyncErrors.Clear();
                _lastSyncErrors.AddRange(currentRunErrors);
                SaveSyncErrors();
            }
            SyncErrorsChanged?.Invoke(this, LastSyncErrors);

            if (!token.IsCancellationRequested)
            {
                // Housekeeping: Purge hashes of local files that were deleted to prevent index bloat
                PurgeOrphanHashes();

                summary.Message = summary.Success
                    ? $"Sincronización completada: {summary.Uploaded} subidos, {summary.Skipped} sin cambios."
                    : $"Sincronización completada con {summary.Errors} errores ({summary.Uploaded} subidos, {summary.Skipped} sin cambios).";

                _settings.LastSyncTime = DateTime.Now;
                _settings.LastSyncStatus = summary.Success
                    ? $"Al día ({DateTime.Now:HH:mm})"
                    : $"Completado con {summary.Errors} errores ({DateTime.Now:HH:mm})";
                SaveSettings();
            }
        }
        catch (Exception ex)
        {
            summary.Message = $"Error durante la sincronización: {ex.Message}";
        }
        finally
        {
            lock (_ctsLock)
            {
                _activeCts?.Dispose();
                _activeCts = null;
            }

            Interlocked.Exchange(ref _isSyncing, 0);
            SyncCompleted?.Invoke(this, summary);
        }

        return summary;
    }

    public async Task<SyncResultSummary> RetryFailedFilesAsync(
        IProgress<SyncProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _isSyncing, 1, 0) != 0)
        {
            return new SyncResultSummary
            {
                Message = "Ya hay una sincronización en curso."
            };
        }

        if (!IsConfigured)
        {
            Interlocked.Exchange(ref _isSyncing, 0);
            return new SyncResultSummary
            {
                Message = "Configuración incompleta: Verifica la URL del Web App y la carpeta local."
            };
        }

        List<SyncErrorItem> filesToRetry;
        lock (_errorLock)
        {
            filesToRetry = _lastSyncErrors.ToList();
        }

        if (filesToRetry.Count == 0)
        {
            Interlocked.Exchange(ref _isSyncing, 0);
            return new SyncResultSummary
            {
                Message = "No hay archivos con error pendientes de reintentar."
            };
        }

        CancellationToken token;
        lock (_ctsLock)
        {
            _activeCts?.Dispose();
            _activeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            token = _activeCts.Token;
        }

        var summary = new SyncResultSummary
        {
            TotalScanned = filesToRetry.Count
        };

        var remainingErrors = new List<SyncErrorItem>();
        int processed = 0;

        try
        {
            foreach (var item in filesToRetry)
            {
                if (token.IsCancellationRequested)
                {
                    summary.Message = "Reintento cancelado por el usuario.";
                    for (int i = processed; i < filesToRetry.Count; i++)
                    {
                        remainingErrors.Add(filesToRetry[i]);
                    }
                    break;
                }

                processed++;

                try
                {
                    ReportProgress(progress, new SyncProgressReport
                    {
                        TotalFiles = summary.TotalScanned,
                        ProcessedFiles = processed,
                        CurrentFileName = item.FileName,
                        UploadedCount = summary.Uploaded,
                        SkippedCount = summary.Skipped,
                        ErrorCount = summary.Errors,
                        StatusMessage = $"Reintentando ({processed}/{summary.TotalScanned}): {item.FileName}..."
                    });

                    if (!File.Exists(item.FilePath))
                    {
                        throw new FileNotFoundException("El archivo local ya no existe.", item.FilePath);
                    }

                    var fileId = await UploadSingleFileAsync(item.FilePath, _settings.WebAppUrl, item.RelativePath);
                    if (string.IsNullOrWhiteSpace(fileId))
                    {
                        throw new Exception("Google Apps Script no confirmó el identificador del archivo creado (fileId).");
                    }

                    if (!string.IsNullOrEmpty(item.HashKey) && !string.IsNullOrEmpty(item.Hash))
                    {
                        SaveKnownHash(item.HashKey, item.Hash);
                    }

                    summary.Uploaded++;

                    await Task.Delay(300, token);
                }
                catch (Exception ex)
                {
                    summary.Errors++;
                    var (category, friendlyMsg) = CategorizeError(ex, item.FilePath);
                    item.ErrorMessage = friendlyMsg;
                    item.ErrorCategory = category;
                    item.Timestamp = DateTime.Now;
                    remainingErrors.Add(item);
                    summary.FailedFiles.Add(item);

                    ReportProgress(progress, new SyncProgressReport
                    {
                        TotalFiles = summary.TotalScanned,
                        ProcessedFiles = processed,
                        CurrentFileName = item.FileName,
                        UploadedCount = summary.Uploaded,
                        SkippedCount = summary.Skipped,
                        ErrorCount = summary.Errors,
                        StatusMessage = $"Error al reintentar {item.FileName}: {friendlyMsg}"
                    });
                }
            }

            lock (_errorLock)
            {
                _lastSyncErrors.Clear();
                _lastSyncErrors.AddRange(remainingErrors);
                SaveSyncErrors();
            }
            SyncErrorsChanged?.Invoke(this, LastSyncErrors);

            summary.FailedFiles = remainingErrors;
            if (!token.IsCancellationRequested)
            {
                summary.Message = summary.Success
                    ? $"Reintento exitoso: Todos los {summary.Uploaded} archivos se subieron correctamente."
                    : $"Reintento completado: {summary.Uploaded} subidos, {summary.Errors} aún con error.";

                _settings.LastSyncTime = DateTime.Now;
                _settings.LastSyncStatus = summary.Success
                    ? $"Al día ({DateTime.Now:HH:mm})"
                    : $"Completado con {summary.Errors} errores ({DateTime.Now:HH:mm})";
                SaveSettings();
            }
        }
        catch (Exception ex)
        {
            summary.Message = $"Error durante el reintento: {ex.Message}";
        }
        finally
        {
            lock (_ctsLock)
            {
                _activeCts?.Dispose();
                _activeCts = null;
            }

            Interlocked.Exchange(ref _isSyncing, 0);
            SyncCompleted?.Invoke(this, summary);
        }

        return summary;
    }

    public void ClearSyncErrors()
    {
        lock (_errorLock)
        {
            _lastSyncErrors.Clear();
            SaveSyncErrors();
        }
        SyncErrorsChanged?.Invoke(this, LastSyncErrors);
    }


    public async Task<string?> TestConnectionAsync(string webAppUrl)
    {
        if (string.IsNullOrWhiteSpace(webAppUrl))
            throw new ArgumentException("La URL del Web App no puede estar vacía.");

        string tempFile = Path.Combine(Path.GetTempPath(), "test_drive_sync.txt");
        try
        {
            await File.WriteAllTextAsync(tempFile, $"Work Activity Panel - Prueba de conexión realizada el {DateTime.Now}");
            var fileId = await UploadSingleFileAsync(tempFile, webAppUrl, "test_drive_sync.txt");
            return fileId;
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { }
            }
        }
    }

    public async Task<string?> UploadSingleFileAsync(string filePath, string webAppUrl, string? relativePath = null)
    {
        if (string.IsNullOrWhiteSpace(webAppUrl))
            throw new ArgumentException("Web App URL no está configurada.");

        if (!File.Exists(filePath))
            throw new FileNotFoundException("El archivo local no existe.", filePath);

        var fileInfo = new FileInfo(filePath);
        long maxBytes = Math.Min(_settings.MaxFileSizeMb, 25) * 1024 * 1024;
        if (fileInfo.Length > maxBytes)
        {
            throw new InvalidOperationException($"El archivo ({fileInfo.Length / (1024.0 * 1024.0):F1} MB) supera el límite seguro de {_settings.MaxFileSizeMb} MB permitido para Google Apps Script.");
        }

        await _uploadSemaphore.WaitAsync();
        try
        {
            byte[] fileBytes = await File.ReadAllBytesAsync(filePath);
            string base64Data = Convert.ToBase64String(fileBytes);

            string normalizedRelativePath = string.IsNullOrWhiteSpace(relativePath)
                ? Path.GetFileName(filePath)
                : relativePath.Replace('\\', '/').TrimStart('/');

            string fileName = ResolveUploadName(filePath, normalizedRelativePath);
            string mimeType = GetMimeType(fileName);

            int maxRetries = 2;
            int delayMs = 1500;

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                try
                {
                    var postParams = new List<KeyValuePair<string, string>>
                    {
                        new("filename", fileName),
                        new("relativePath", normalizedRelativePath),
                        new("mimeType", mimeType),
                        new("data", base64Data)
                    };

                    if (!string.IsNullOrWhiteSpace(_settings.AuthToken))
                    {
                        postParams.Add(new("authToken", _settings.AuthToken.Trim()));
                    }

                    var content = new FormUrlEncodedContent(postParams);
                    var response = await _httpClient.PostAsync(webAppUrl, content);

                    // If rate limited or server overloaded (429, 503, 500), retry with backoff
                    if ((response.StatusCode == System.Net.HttpStatusCode.TooManyRequests ||
                         response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable ||
                         response.StatusCode == System.Net.HttpStatusCode.InternalServerError) && attempt < maxRetries)
                    {
                        await Task.Delay(delayMs + Random.Shared.Next(100, 500));
                        delayMs *= 2;
                        continue;
                    }

                    response.EnsureSuccessStatusCode();

                    var responseString = await response.Content.ReadAsStringAsync();

                    try
                    {
                        var result = JsonSerializer.Deserialize<JsonElement>(responseString);
                        if (result.TryGetProperty("status", out var status) && status.GetString() == "error")
                        {
                            string msg = result.TryGetProperty("message", out var m) ? m.GetString() ?? "Error desconocido" : "Error desconocido";

                            if ((msg.Contains("Service invoked too many times", StringComparison.OrdinalIgnoreCase) ||
                                 msg.Contains("rate limit", StringComparison.OrdinalIgnoreCase)) && attempt < maxRetries)
                            {
                                await Task.Delay(delayMs + Random.Shared.Next(100, 500));
                                delayMs *= 2;
                                continue;
                            }

                            throw new Exception($"Apps Script Error: {msg}");
                        }

                        return result.TryGetProperty("fileId", out var id) ? id.GetString() : null;
                    }
                    catch (JsonException)
                    {
                        if (attempt < maxRetries)
                        {
                            await Task.Delay(delayMs + Random.Shared.Next(100, 500));
                            delayMs *= 2;
                            continue;
                        }

                        string rawPreview = responseString.Length > 200 ? responseString[..200] + "..." : responseString;
                        throw new Exception($"Respuesta inválida de Apps Script (no es JSON):\n{rawPreview}");
                    }
                }
                catch (Exception ex) when (attempt < maxRetries && (ex is TaskCanceledException || ex is HttpRequestException))
                {
                    await Task.Delay(delayMs + Random.Shared.Next(100, 500));
                    delayMs *= 2;
                }
            }

            throw new Exception("Se agotaron los intentos de subida.");
        }
        finally
        {
            _uploadSemaphore.Release();
        }
    }

    public static (string Category, string FriendlyMessage) CategorizeError(Exception ex, string filePath)
    {
        if (ex is IOException ioEx && (ioEx.HResult == unchecked((int)0x80070020) || ioEx.Message.Contains("used by another process", StringComparison.OrdinalIgnoreCase)))
        {
            return ("Archivo en uso / Bloqueado", "El archivo está abierto en otra aplicación o bloqueado por Windows.");
        }

        if (ex is UnauthorizedAccessException)
        {
            return ("Permiso denegado", "Sin permisos de lectura para acceder a este archivo local.");
        }

        if (ex is TaskCanceledException || ex is TimeoutException)
        {
            return ("Tiempo de espera agotado", "La subida superó el límite de tiempo (30s) de Google Apps Script.");
        }

        var message = ex.Message;
        if (message.Contains("429") || message.Contains("Service invoked too many times", StringComparison.OrdinalIgnoreCase) || message.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
        {
            return ("Límite de Google Apps Script", "Google Apps Script ha superado la cuota de peticiones por minuto o día.");
        }

        if (message.Contains("503") || message.Contains("500") || message.Contains("504") || message.Contains("Respuesta inválida de Apps Script", StringComparison.OrdinalIgnoreCase))
        {
            return ("Error de servidor en Google", "El servidor de Google Apps Script falló o devolvió una respuesta no válida.");
        }

        if (message.Contains("Payload too large", StringComparison.OrdinalIgnoreCase) || message.Contains("OutOfMemory", StringComparison.OrdinalIgnoreCase))
        {
            return ("Archivo demasiado grande", "El archivo excede el tamaño máximo permitido para subir vía Base64.");
        }

        return ("Error de subida", message);
    }


    /// <summary>
    /// Name the file takes at the destination: the last segment of the relative path, not
    /// the local file name. The bridge creates the file with this name inside the folders it
    /// derives from the earlier segments, so any renaming a sweep applies to that segment
    /// only reaches the destination if the segment is what gets sent.
    /// </summary>
    public static string ResolveUploadName(string filePath, string normalizedRelativePath)
    {
        var lastSegment = normalizedRelativePath.Split('/')[^1];
        return string.IsNullOrWhiteSpace(lastSegment) ? Path.GetFileName(filePath) : lastSegment;
    }

    public List<LocalFileMetadata> ScanFolder(string rootFolderPath, SyncFilterOptions? filters = null)
    {
        var results = new List<LocalFileMetadata>();
        if (string.IsNullOrWhiteSpace(rootFolderPath) || !Directory.Exists(rootFolderPath))
            return results;

        filters ??= new SyncFilterOptions();
        var directoriesQueue = new Queue<string>();
        directoriesQueue.Enqueue(rootFolderPath);

        while (directoriesQueue.Count > 0)
        {
            var currentDir = directoriesQueue.Dequeue();

            try
            {
                var subDirs = Directory.GetDirectories(currentDir);
                foreach (var subDir in subDirs)
                {
                    var dirName = new DirectoryInfo(subDir).Name;
                    if (!filters.IsFolderExcluded(dirName))
                    {
                        directoriesQueue.Enqueue(subDir);
                    }
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (Exception) { }

            try
            {
                var files = Directory.GetFiles(currentDir);
                foreach (var file in files)
                {
                    try
                    {
                        var fileInfo = new FileInfo(file);
                        if (filters.ShouldIncludeFile(fileInfo, out _))
                        {
                            results.Add(new LocalFileMetadata
                            {
                                FilePath = file,
                                FileName = fileInfo.Name,
                                RelativePath = Path.GetRelativePath(rootFolderPath, file),
                                FileSize = fileInfo.Length,
                                // Hash is intentionally deferred: computed lazily in the sync loop
                                // via the fast-path cache to avoid reading every file on disk up-front.
                                Hash = string.Empty
                            });
                        }
                    }
                    catch (Exception) { }
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (Exception) { }
        }

        return results;
    }

    /// <summary>
    /// The folders to synchronize. A source repeating another one's folder and destination
    /// is dropped so it is not uploaded twice.
    /// </summary>
    private IEnumerable<SyncSource> EnumerateSources()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in _settings.Sources)
        {
            if (string.IsNullOrWhiteSpace(source.LocalFolderPath))
                continue;

            if (seen.Add($"{source.EffectiveDestinationPrefix}|{source.LocalFolderPath}"))
                yield return source;
        }
    }

    /// <summary>
    /// Scans every source plus, when enabled, the unversioned instruction-file sweep, and
    /// returns the files to upload with their destination paths already resolved.
    /// </summary>
    private async Task<List<LocalFileMetadata>> CollectFilesAsync(
        IProgress<SyncProgressReport>? progress,
        CancellationToken token)
    {
        return await Task.Run(async () =>
        {
            var collected = new List<LocalFileMetadata>();

            var filters = SyncFilterOptions.Create(
                _settings.IncludedExtensions,
                _settings.ExcludedExtensions,
                _settings.ExcludedFolders,
                _settings.MaxFileSizeMb);

            foreach (var source in EnumerateSources())
            {
                token.ThrowIfCancellationRequested();

                var prefix = source.EffectiveDestinationPrefix;

                ReportProgress(progress, new SyncProgressReport
                {
                    StatusMessage = $"Escaneando {prefix}..."
                });

                foreach (var file in ScanFolder(source.LocalFolderPath, filters))
                {
                    file.RelativePath = CombineDestination(prefix, file.RelativePath);
                    file.HashKey = $"{prefix}|{file.FilePath}";
                    collected.Add(file);
                }
            }

            if (_settings.SyncUnversionedClaudeMarkdown)
            {
                ReportProgress(progress, new SyncProgressReport
                {
                    StatusMessage = "Buscando CLAUDE.md y referencias sin versionar..."
                });

                var profileRoot = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var unversioned = await _claudeConfigDiscovery.FindUnversionedAsync(
                    profileRoot,
                    _settings.ClaudeMarkdownScanDepth,
                    token);

                foreach (var path in unversioned)
                {
                    try
                    {
                        if (!File.Exists(path))
                            continue;

                        var fileInfo = new FileInfo(path);
                        // The path under the user profile is kept as a path, so the bridge
                        // recreates the folder tree instead of dropping a tree of files all
                        // called CLAUDE.md into one folder, where they would overwrite each other.
                        var destination = CombineDestination(
                            _settings.ClaudeMarkdownDestinationPrefix,
                            Path.GetRelativePath(profileRoot, path));

                        collected.Add(new LocalFileMetadata
                        {
                            FilePath = path,
                            FileName = fileInfo.Name,
                            RelativePath = destination,
                            FileSize = fileInfo.Length,
                            // Hash is intentionally deferred: computed lazily in the sync loop
                            // via the fast-path cache to avoid reading every file on disk up-front.
                            Hash = string.Empty,
                            // The destination, not just its prefix, is part of the key: a file already
                            // uploaded under a different name has not been uploaded to where it belongs.
                            HashKey = $"{destination}|{path}"
                        });
                    }
                    catch
                    {
                        // Skip files that cannot be read or are locked
                    }
                }
            }

            return collected;
        }, token);
    }

    /// <summary>
    /// Joins a destination prefix and a relative path into the forward-slash path the
    /// Apps Script bridge expects. An empty prefix leaves the path untouched.
    /// </summary>
    public static string CombineDestination(string? prefix, string relativePath)
    {
        var normalized = (relativePath ?? string.Empty).Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrWhiteSpace(prefix))
            return normalized;

        return $"{prefix.Replace('\\', '/').Trim('/')}/{normalized}";
    }

    public string ComputeSha256(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var hashBytes = sha256.ComputeHash(stream);
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
    }

    private void ReportProgress(IProgress<SyncProgressReport>? progress, SyncProgressReport report)
    {
        progress?.Report(report);
        SyncProgressChanged?.Invoke(this, report);
    }

    /// <summary>
    /// Returns the raw cached hash string for the given key, or <c>null</c> if no entry exists.
    /// Does NOT validate file metadata — callers that need fast-path confirmation must use
    /// <see cref="IsMetadataConfirmed"/> separately. This separation keeps each method
    /// single-responsibility and avoids duplicate <see cref="FileInfo"/> disk reads.
    /// </summary>
    private string? GetKnownHash(string hashKey)
    {
        lock (_hashLock)
        {
            return _hashIndex.TryGetValue(hashKey, out var entry) ? entry.Hash : null;
        }
    }

    /// <summary>
    /// Returns <c>true</c> when the cached entry for <paramref name="hashKey"/> has valid
    /// metadata (<see cref="HashCacheEntry.LastWriteTimeUtcTicks"/> and
    /// <see cref="HashCacheEntry.FileSize"/> match the file on disk and the file is at
    /// least 1 KB), confirming the fast-path skip is safe.
    /// </summary>
    private bool IsMetadataConfirmed(string hashKey, string filePath, long scannedFileSize)
    {
        lock (_hashLock)
        {
            if (!_hashIndex.TryGetValue(hashKey, out var entry))
                return false;

            // Fast-path guard: entries with ticks=0 are legacy migrations not yet re-hashed
            if (entry.LastWriteTimeUtcTicks == 0L || entry.FileSize < 1024)
                return false;

            try
            {
                var fi = new FileInfo(filePath);
                return fi.Exists &&
                       fi.LastWriteTimeUtc.Ticks == entry.LastWriteTimeUtcTicks &&
                       fi.Length == entry.FileSize &&
                       scannedFileSize == entry.FileSize;
            }
            catch
            {
                return false;
            }
        }
    }

    private void SaveKnownHash(string hashKey, string hash, FileInfo? fileInfo = null)
    {
        lock (_hashLock)
        {
            _hashIndex[hashKey] = new HashCacheEntry
            {
                Hash = hash,
                LastWriteTimeUtcTicks = fileInfo != null ? fileInfo.LastWriteTimeUtc.Ticks : 0L,
                FileSize = fileInfo?.Length ?? 0L
            };
            SaveHashIndex();
        }
    }

    private void LoadHashIndex()
    {
        lock (_hashLock)
        {
            try
            {
                if (!File.Exists(HashIndexFile))
                    return;

                var json = File.ReadAllText(HashIndexFile);
                var doc = JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);

                var newIndex = new Dictionary<string, HashCacheEntry>(StringComparer.OrdinalIgnoreCase);

                if (doc.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    foreach (var prop in doc.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            // Legacy format: {"key": "sha256hash"} — migrate to HashCacheEntry with
                            // zero timestamps so the fast-path is bypassed until the file is re-hashed.
                            newIndex[prop.Name] = new HashCacheEntry
                            {
                                Hash = prop.Value.GetString() ?? string.Empty,
                                LastWriteTimeUtcTicks = 0L,
                                FileSize = 0L
                            };
                        }
                        else if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.Object)
                        {
                            // New format: {"key": {"Hash": "...", "LastWriteTimeUtcTicks": N, "FileSize": N}}
                            var entry = JsonSerializer.Deserialize<HashCacheEntry>(prop.Value.GetRawText());
                            if (entry != null)
                                newIndex[prop.Name] = entry;
                        }
                    }
                }

                _hashIndex = newIndex;
            }
            catch
            {
                _hashIndex = new Dictionary<string, HashCacheEntry>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    private void SaveHashIndex()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            var json = JsonSerializer.Serialize(_hashIndex, new JsonSerializerOptions { WriteIndented = false });
            File.WriteAllText(HashIndexFile, json);
        }
        catch
        {
            // Ignore index save errors
        }
    }

    private void LoadSyncErrors()
    {
        lock (_errorLock)
        {
            try
            {
                if (File.Exists(ErrorsFile))
                {
                    var json = File.ReadAllText(ErrorsFile);
                    var list = JsonSerializer.Deserialize<List<SyncErrorItem>>(json);
                    if (list != null)
                    {
                        _lastSyncErrors.Clear();
                        _lastSyncErrors.AddRange(list);
                    }
                }
            }
            catch
            {
                _lastSyncErrors.Clear();
            }
        }
    }

    private void SaveSyncErrors()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            var json = JsonSerializer.Serialize(_lastSyncErrors, new JsonSerializerOptions { WriteIndented = false });
            File.WriteAllText(ErrorsFile, json);
        }
        catch
        {
            // Ignore error log save errors
        }
    }


    private DriveSyncSettings LoadSettings()
    {
        try
        {
            var json = LocalSettingsHelper.Get(SettingsKey);
            if (!string.IsNullOrEmpty(json))
            {
                var settings = JsonSerializer.Deserialize<DriveSyncSettings>(json);
                if (settings != null) return MigrateLegacyMainFolder(settings);
            }
        }
        catch { }

        return new DriveSyncSettings();
    }

    /// <summary>
    /// Turns the main folder of the old layout into one more source, so a configuration
    /// saved before the destinations were flattened keeps being backed up. Its files move
    /// from the destination root into a subfolder, so they are uploaded once more.
    /// </summary>
    private static DriveSyncSettings MigrateLegacyMainFolder(DriveSyncSettings settings)
    {
        var legacyPath = settings.LegacyMainFolderPath;
        settings.LegacyMainFolderPath = string.Empty;

        if (string.IsNullOrWhiteSpace(legacyPath))
            return settings;

        bool alreadyPresent = settings.Sources.Any(s =>
            string.Equals(s.LocalFolderPath, legacyPath, StringComparison.OrdinalIgnoreCase));

        if (!alreadyPresent)
        {
            settings.Sources.Add(new SyncSource { LocalFolderPath = legacyPath });
        }

        return settings;
    }

    private void SaveSettings()
    {
        try
        {
            var json = JsonSerializer.Serialize(_settings);
            LocalSettingsHelper.Set(SettingsKey, json);
        }
        catch { }
    }

    private static string GetMimeType(string fileName)
    {
        string extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".txt" => "text/plain",
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".zip" => "application/zip",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".csv" => "text/csv",
            ".json" => "application/json",
            _ => "application/octet-stream",
        };
    }

    /// <summary>
    /// Purges hash entries from the index for local files that no longer exist on disk,
    /// preventing perpetual state drift and memory bloat.
    /// </summary>
    public void PurgeOrphanHashes()
    {
        lock (_hashLock)
        {
            var orphanKeys = new List<string>();
            foreach (var (key, _) in _hashIndex)
            {
                var pipeIndex = key.IndexOf('|');
                var localPath = pipeIndex >= 0 ? key[(pipeIndex + 1)..] : key;
                if (!File.Exists(localPath))
                {
                    orphanKeys.Add(key);
                }
            }

            foreach (var k in orphanKeys)
            {
                _hashIndex.Remove(k);
            }

            if (orphanKeys.Count > 0)
            {
                SaveHashIndex();
            }
        }
    }

    public void Dispose()
    {
        _scheduleService.WorkEnded -= OnWorkEnded;
        _activeCts?.Cancel();
        _activeCts?.Dispose();
        _httpClient.Dispose();
        _uploadSemaphore.Dispose();
    }
}
