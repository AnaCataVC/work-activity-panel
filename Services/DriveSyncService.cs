using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
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

    /// <summary>
    /// Batch upload caps. Multiple files ride in a single JSON POST so the fixed per-call cost
    /// (Apps Script cold start + LockService acquisition, ~1.5-3s) is paid once per batch instead
    /// of once per file — this is what actually fixes "uploads are still slow" once the scan and
    /// folder-resolution fast-paths are already in place (see drive-sync-fast-path-hash-cache.md
    /// and drive-sync-appsscript-folder-id-cache.md). The byte cap keeps the encoded payload well
    /// under Google's undocumented ~25-30 MB HTTP body ceiling for Apps Script Web Apps (base64
    /// expands raw bytes by ~33%, so 9 MB raw becomes ~12 MB encoded — see
    /// drive-sync-adversarial-stress-test.md, vulnerability #3).
    /// </summary>
    private const int MaxBatchFileCount = 8;
    private const long MaxBatchRawBytes = 9L * 1024 * 1024;

    private readonly HttpClient _httpClient;
    private readonly IScheduleService _scheduleService;
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
                                && EnumerateSources().Any();

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
        bool forceFullSync = false,
        SyncSource? onlySource = null)
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

            var localFiles = await CollectFilesAsync(progress, token, onlySource);
            summary.TotalScanned = localFiles.Count;

            int processed = 0;
            var pendingUploads = new List<UploadCandidate>();

            // 1. Classification pass: resolve each file's hash lazily with the fast-path cache
            //    (no network calls here) and split into "skip" vs. "needs upload".
            //    IsMetadataConfirmed does the single FileInfo stat to check LastWriteTimeUtc + FileSize.
            //    GetKnownHash is a pure cache lookup (no disk I/O). This avoids duplicate stat calls.
            //    When metadata matches, the file is skipped with no disk read. When it does not
            //    (new file, size/mtime changed, legacy entry with ticks=0, or file < 1 KB),
            //    we fall through to ComputeSha256.
            foreach (var file in localFiles)
            {
                if (token.IsCancellationRequested)
                {
                    summary.Message = "Sincronización cancelada por el usuario.";
                    break;
                }

                processed++;

                bool skip = false;
                FileInfo? statFileInfo = null;

                if (!forceFullSync && _settings.OnlyModifiedOrNew)
                {
                    string? cachedHash = GetKnownHash(file.HashKey);

                    // IsMetadataConfirmed does the single authoritative FileInfo stat under _hashLock.
                    bool metadataConfirmed = cachedHash != null && IsMetadataConfirmed(file.HashKey, file.FilePath, file.FileSize);

                    if (metadataConfirmed)
                    {
                        // Fast-path: metadata identical, file unchanged, no disk read needed.
                        skip = true;
                    }
                    else
                    {
                        // Metadata changed or unavailable — compute the real hash from disk.
                        statFileInfo = new FileInfo(file.FilePath);
                        file.Hash = ComputeSha256(file.FilePath);

                        if (cachedHash != null &&
                            string.Equals(cachedHash, file.Hash, StringComparison.OrdinalIgnoreCase))
                        {
                            // Hash matches even though metadata mismatched (e.g. git checkout restored mtime,
                            // or xcopy preserved timestamp). Upgrade the cache entry with fresh metadata.
                            SaveKnownHash(file.HashKey, file.Hash, statFileInfo);
                            skip = true;
                        }
                    }
                }
                else
                {
                    // Full sync or incremental check disabled: compute hash for post-upload persistence.
                    statFileInfo = new FileInfo(file.FilePath);
                    file.Hash = ComputeSha256(file.FilePath);
                }

                if (skip)
                {
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

                pendingUploads.Add(new UploadCandidate(
                    file.FilePath, file.FileName, file.RelativePath, file.HashKey, file.Hash, statFileInfo!));
            }

            // 2. Upload pass: group pending files into batches so the fixed per-request cost of
            //    Apps Script (cold start + LockService acquisition, ~1.5-3s) is paid once per
            //    batch instead of once per file — see the MaxBatchFileCount/MaxBatchRawBytes docs above.
            if (!token.IsCancellationRequested)
            {
                foreach (var batch in BuildBatches(pendingUploads))
                {
                    if (token.IsCancellationRequested)
                    {
                        summary.Message = "Sincronización cancelada por el usuario.";
                        break;
                    }

                    ReportProgress(progress, new SyncProgressReport
                    {
                        TotalFiles = summary.TotalScanned,
                        ProcessedFiles = processed,
                        CurrentFileName = batch.Count == 1 ? batch[0].FileName : $"{batch.Count} archivos",
                        UploadedCount = summary.Uploaded,
                        SkippedCount = summary.Skipped,
                        ErrorCount = summary.Errors,
                        StatusMessage = batch.Count == 1
                            ? $"Subiendo: {batch[0].FileName}..."
                            : $"Subiendo lote de {batch.Count} archivos..."
                    });

                    await ProcessBatchAsync(batch, summary, currentRunErrors, token);

                    ReportProgress(progress, new SyncProgressReport
                    {
                        TotalFiles = summary.TotalScanned,
                        ProcessedFiles = processed,
                        CurrentFileName = batch[^1].FileName,
                        UploadedCount = summary.Uploaded,
                        SkippedCount = summary.Skipped,
                        ErrorCount = summary.Errors,
                        StatusMessage = $"Progreso: {summary.Uploaded} subidos, {summary.Errors} errores."
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
            var pendingRetries = new List<UploadCandidate>();

            foreach (var item in filesToRetry)
            {
                processed++;

                if (!File.Exists(item.FilePath))
                {
                    summary.Errors++;
                    var (category, friendlyMsg) = CategorizeError(
                        new FileNotFoundException("El archivo local ya no existe.", item.FilePath), item.FilePath);
                    item.ErrorMessage = friendlyMsg;
                    item.ErrorCategory = category;
                    item.Timestamp = DateTime.Now;
                    remainingErrors.Add(item);
                    summary.FailedFiles.Add(item);
                    continue;
                }

                pendingRetries.Add(new UploadCandidate(
                    item.FilePath, item.FileName, item.RelativePath, item.HashKey, item.Hash, new FileInfo(item.FilePath)));
            }

            var batches = BuildBatches(pendingRetries);
            for (int bi = 0; bi < batches.Count; bi++)
            {
                if (token.IsCancellationRequested)
                {
                    summary.Message = "Reintento cancelado por el usuario.";
                    for (int rem = bi; rem < batches.Count; rem++)
                    {
                        foreach (var candidate in batches[rem])
                        {
                            remainingErrors.Add(BuildErrorItem(candidate, "Cancelado", "Reintento cancelado por el usuario."));
                        }
                    }
                    break;
                }

                var batch = batches[bi];

                ReportProgress(progress, new SyncProgressReport
                {
                    TotalFiles = summary.TotalScanned,
                    ProcessedFiles = processed,
                    CurrentFileName = batch.Count == 1 ? batch[0].FileName : $"{batch.Count} archivos",
                    UploadedCount = summary.Uploaded,
                    SkippedCount = summary.Skipped,
                    ErrorCount = summary.Errors,
                    StatusMessage = batch.Count == 1
                        ? $"Reintentando: {batch[0].FileName}..."
                        : $"Reintentando lote de {batch.Count} archivos..."
                });

                await ProcessBatchAsync(batch, summary, remainingErrors, token);
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

    /// <summary>One file queued for upload, with its local stat already resolved (avoids a repeat FileInfo read at upload time).</summary>
    public sealed record UploadCandidate(string FilePath, string FileName, string RelativePath, string HashKey, string Hash, FileInfo Info);

    /// <summary>Per-file outcome inside a batch response, matched back to its <see cref="UploadCandidate"/> by array position.</summary>
    private sealed record BatchUploadResult(bool Success, string? FileId, string? ErrorMessage);

    /// <summary>
    /// Groups pending uploads into batches bounded by <see cref="MaxBatchFileCount"/> and
    /// <see cref="MaxBatchRawBytes"/>. A single file already over the byte cap still gets its
    /// own one-item batch instead of being dropped. Public (like <see cref="CombineDestination"/>
    /// and <see cref="ResolveUploadName"/>) purely so the batching boundary logic is unit-testable.
    /// </summary>
    public static List<List<UploadCandidate>> BuildBatches(List<UploadCandidate> candidates)
    {
        var batches = new List<List<UploadCandidate>>();
        var current = new List<UploadCandidate>();
        long currentBytes = 0;

        foreach (var candidate in candidates)
        {
            if (current.Count > 0 &&
                (current.Count >= MaxBatchFileCount || currentBytes + candidate.Info.Length > MaxBatchRawBytes))
            {
                batches.Add(current);
                current = new List<UploadCandidate>();
                currentBytes = 0;
            }

            current.Add(candidate);
            currentBytes += candidate.Info.Length;
        }

        if (current.Count > 0)
            batches.Add(current);

        return batches;
    }

    private static SyncErrorItem BuildErrorItem(UploadCandidate candidate, string category, string message) => new()
    {
        FileName = candidate.FileName,
        FilePath = candidate.FilePath,
        RelativePath = candidate.RelativePath,
        HashKey = candidate.HashKey,
        Hash = candidate.Hash,
        ErrorMessage = message,
        ErrorCategory = category,
        Timestamp = DateTime.Now
    };

    /// <summary>
    /// Uploads one batch, updates the hash cache and <paramref name="summary"/> counters for
    /// every file it contains, and appends failures to <paramref name="errorSink"/>. A whole-batch
    /// failure (auth rejected, lock timeout, retries exhausted) marks every file in the batch as
    /// failed with the same cause, mirroring what a single-file failure did before batching.
    /// </summary>
    private async Task ProcessBatchAsync(
        List<UploadCandidate> batch,
        SyncResultSummary summary,
        List<SyncErrorItem> errorSink,
        CancellationToken token)
    {
        try
        {
            var results = await UploadBatchAsync(batch, _settings.WebAppUrl);
            for (int i = 0; i < batch.Count; i++)
            {
                var candidate = batch[i];
                var result = results[i];

                if (result.Success)
                {
                    SaveKnownHash(candidate.HashKey, candidate.Hash, candidate.Info);
                    summary.Uploaded++;
                }
                else
                {
                    summary.Errors++;
                    var (category, friendlyMsg) = CategorizeError(new Exception(result.ErrorMessage ?? "Error desconocido"), candidate.FilePath);
                    var errorItem = BuildErrorItem(candidate, category, friendlyMsg);
                    errorSink.Add(errorItem);
                    summary.FailedFiles.Add(errorItem);
                }
            }
        }
        catch (Exception ex)
        {
            var (category, friendlyMsg) = CategorizeError(ex, batch.Count == 1 ? batch[0].FilePath : $"{batch.Count} archivos en lote");
            foreach (var candidate in batch)
            {
                summary.Errors++;
                var errorItem = BuildErrorItem(candidate, category, friendlyMsg);
                errorSink.Add(errorItem);
                summary.FailedFiles.Add(errorItem);
            }
        }

        // Throttle between batches to avoid Google Apps Script burst rate-limits. Swallow
        // cancellation here so it is handled by the caller's IsCancellationRequested check on
        // the next loop iteration instead of surfacing as an exception after work already done.
        try { await Task.Delay(300, token); } catch (OperationCanceledException) { }
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

        string normalizedRelativePath = string.IsNullOrWhiteSpace(relativePath)
            ? Path.GetFileName(filePath)
            : relativePath.Replace('\\', '/').TrimStart('/');

        var candidate = new UploadCandidate(filePath, Path.GetFileName(filePath), normalizedRelativePath, filePath, string.Empty, fileInfo);
        var results = await UploadBatchAsync(new List<UploadCandidate> { candidate }, webAppUrl);
        var result = results[0];

        if (!result.Success)
        {
            throw new Exception(result.ErrorMessage ?? "Error desconocido al subir el archivo.");
        }

        return result.FileId;
    }

    /// <summary>
    /// Uploads a whole batch of files in a single JSON POST. Multiple files per request amortize
    /// the fixed per-call cost of Apps Script (cold start + LockService acquisition) across all of
    /// them instead of paying it per file — the actual fix for uploads staying slow once the scan
    /// and folder-resolution fast-paths are already in place. The request body is capped by
    /// <see cref="MaxBatchRawBytes"/> well under Google's undocumented ~25-30 MB ceiling for Apps
    /// Script Web App payloads (see drive-sync-adversarial-stress-test.md).
    /// </summary>
    private async Task<List<BatchUploadResult>> UploadBatchAsync(List<UploadCandidate> batch, string webAppUrl)
    {
        await _uploadSemaphore.WaitAsync();
        try
        {
            var fileEntries = new List<object>(batch.Count);
            foreach (var candidate in batch)
            {
                byte[] fileBytes = await File.ReadAllBytesAsync(candidate.FilePath);
                string fileName = ResolveUploadName(candidate.FilePath, candidate.RelativePath);

                fileEntries.Add(new
                {
                    filename = fileName,
                    relativePath = candidate.RelativePath,
                    mimeType = GetMimeType(fileName),
                    data = Convert.ToBase64String(fileBytes)
                });
            }

            var payload = new Dictionary<string, object?> { ["files"] = fileEntries };
            if (!string.IsNullOrWhiteSpace(_settings.AuthToken))
            {
                payload["authToken"] = _settings.AuthToken.Trim();
            }

            string requestJson = JsonSerializer.Serialize(payload);

            int maxRetries = 2;
            int delayMs = 1500;

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                try
                {
                    var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
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

                    JsonElement result;
                    try
                    {
                        result = JsonSerializer.Deserialize<JsonElement>(responseString);
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

                        // Whole batch rejected before per-file processing (auth failure, lock timeout, malformed request).
                        throw new Exception($"Apps Script Error: {msg}");
                    }

                    var perFileResults = new List<BatchUploadResult>(batch.Count);
                    if (result.TryGetProperty("results", out var resultsArray) && resultsArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in resultsArray.EnumerateArray())
                        {
                            bool itemSuccess = item.TryGetProperty("status", out var itemStatus) && itemStatus.GetString() == "success";
                            string? fileId = item.TryGetProperty("fileId", out var fid) ? fid.GetString() : null;
                            string? errMsg = item.TryGetProperty("message", out var im) ? im.GetString() : null;
                            perFileResults.Add(new BatchUploadResult(itemSuccess, fileId, errMsg));
                        }
                    }

                    if (perFileResults.Count != batch.Count)
                    {
                        throw new Exception("Apps Script devolvió una cantidad de resultados distinta a la cantidad de archivos enviados. Actualiza el Code.gs desplegado (ver docs/google-setup-guide.md).");
                    }

                    return perFileResults;
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
    /// is dropped so it is not uploaded twice. When <paramref name="onlySource"/> is given,
    /// every other configured source is skipped so only that one folder gets synced.
    /// </summary>
    private IEnumerable<SyncSource> EnumerateSources(SyncSource? onlySource = null)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in _settings.Sources)
        {
            if (string.IsNullOrWhiteSpace(source.LocalFolderPath))
                continue;

            if (onlySource != null &&
                !string.Equals(source.LocalFolderPath, onlySource.LocalFolderPath, StringComparison.OrdinalIgnoreCase))
                continue;

            if (seen.Add($"{source.EffectiveDestinationPrefix}|{source.LocalFolderPath}"))
                yield return source;
        }
    }

    /// <summary>
    /// Scans the configured source folders and returns the files to upload with their
    /// destination paths already resolved. Restricted to <paramref name="onlySource"/> when given.
    /// </summary>
    private async Task<List<LocalFileMetadata>> CollectFilesAsync(
        IProgress<SyncProgressReport>? progress,
        CancellationToken token,
        SyncSource? onlySource = null)
    {
        return await Task.Run(() =>
        {
            var collected = new List<LocalFileMetadata>();

            var filters = SyncFilterOptions.Create(
                _settings.IncludedExtensions,
                _settings.ExcludedExtensions,
                _settings.ExcludedFolders,
                _settings.MaxFileSizeMb);

            foreach (var source in EnumerateSources(onlySource))
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

    /// <summary>Reads the whole file as text, or <c>null</c> if it does not exist or cannot be read.</summary>
    private static string? TryReadFileText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Serializes <paramref name="value"/> as compact JSON and writes it to <paramref name="path"/>, ignoring write failures.</summary>
    private static void SaveJsonFile<T>(string path, T value)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = false }));
        }
        catch
        {
            // Ignore file save errors
        }
    }

    private void LoadHashIndex()
    {
        lock (_hashLock)
        {
            var newIndex = new Dictionary<string, HashCacheEntry>(StringComparer.OrdinalIgnoreCase);
            var json = TryReadFileText(HashIndexFile);

            if (json != null)
            {
                try
                {
                    var doc = JsonSerializer.Deserialize<JsonElement>(json);

                    if (doc.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in doc.EnumerateObject())
                        {
                            if (prop.Value.ValueKind == JsonValueKind.String)
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
                            else if (prop.Value.ValueKind == JsonValueKind.Object)
                            {
                                // New format: {"key": {"Hash": "...", "LastWriteTimeUtcTicks": N, "FileSize": N}}
                                var entry = JsonSerializer.Deserialize<HashCacheEntry>(prop.Value.GetRawText());
                                if (entry != null)
                                    newIndex[prop.Name] = entry;
                            }
                        }
                    }
                }
                catch
                {
                    newIndex.Clear();
                }
            }

            _hashIndex = newIndex;
        }
    }

    private void SaveHashIndex()
    {
        SaveJsonFile(HashIndexFile, _hashIndex);
    }

    private void LoadSyncErrors()
    {
        lock (_errorLock)
        {
            var json = TryReadFileText(ErrorsFile);
            if (json == null) return;

            try
            {
                var list = JsonSerializer.Deserialize<List<SyncErrorItem>>(json);
                if (list != null)
                {
                    _lastSyncErrors.Clear();
                    _lastSyncErrors.AddRange(list);
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
        SaveJsonFile(ErrorsFile, _lastSyncErrors);
    }


    private DriveSyncSettings LoadSettings()
    {
        return MigrateLegacyMainFolder(LocalSettingsHelper.LoadJson<DriveSyncSettings>(SettingsKey));
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
        LocalSettingsHelper.SaveJson(SettingsKey, _settings);
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
