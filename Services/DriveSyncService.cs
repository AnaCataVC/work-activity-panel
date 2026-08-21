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

    private readonly HttpClient _httpClient;
    private readonly IScheduleService _scheduleService;
    private readonly ClaudeConfigDiscovery _claudeConfigDiscovery = new();
    private readonly object _hashLock = new();
    private Dictionary<string, string> _hashIndex = new(StringComparer.OrdinalIgnoreCase);

    private DriveSyncSettings _settings;
    private CancellationTokenSource? _activeCts;
    private bool _isSyncing;

    public DriveSyncSettings Settings => _settings;
    public bool IsSyncing => _isSyncing;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_settings.WebAppUrl)
                                && (EnumerateSources().Any() || _settings.SyncUnversionedClaudeMarkdown);

    public event EventHandler? SettingsChanged;
    public event EventHandler<SyncProgressReport>? SyncProgressChanged;
    public event EventHandler<SyncResultSummary>? SyncCompleted;

    public DriveSyncService(IScheduleService scheduleService)
    {
        _scheduleService = scheduleService;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _settings = LoadSettings();
        LoadHashIndex();

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
        _settings = settings;
        SaveSettings();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }


    public void CancelSync()
    {
        _activeCts?.Cancel();
    }

    public async Task<SyncResultSummary> RunSyncAsync(
        IProgress<SyncProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (_isSyncing)
        {
            return new SyncResultSummary
            {
                Message = "Ya hay una sincronización en curso."
            };
        }

        if (!IsConfigured)
        {
            return new SyncResultSummary
            {
                Message = "Configuración incompleta: Verifica la URL del Web App y la carpeta local."
            };
        }

        _isSyncing = true;
        _activeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _activeCts.Token;

        var summary = new SyncResultSummary();

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

                // 1. Check SHA-256 hash if incremental sync is enabled
                if (_settings.OnlyModifiedOrNew)
                {
                    string? previousHash = GetKnownHash(file.HashKey);
                    if (!string.IsNullOrEmpty(previousHash) && string.Equals(previousHash, file.Hash, StringComparison.OrdinalIgnoreCase))
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

                    await UploadSingleFileAsync(file.FilePath, _settings.WebAppUrl, file.RelativePath);
                    SaveKnownHash(file.HashKey, file.Hash);
                    summary.Uploaded++;
                }
                catch (Exception ex)
                {
                    summary.Errors++;
                    ReportProgress(progress, new SyncProgressReport
                    {
                        TotalFiles = summary.TotalScanned,
                        ProcessedFiles = processed,
                        CurrentFileName = file.FileName,
                        UploadedCount = summary.Uploaded,
                        SkippedCount = summary.Skipped,
                        ErrorCount = summary.Errors,
                        StatusMessage = $"Error al subir {file.FileName}: {ex.Message}"
                    });
                }
            }

            if (!token.IsCancellationRequested)
            {
                summary.Message = $"Sincronización completada: {summary.Uploaded} subidos, {summary.Skipped} sin cambios, {summary.Errors} errores.";
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
            _isSyncing = false;
            _activeCts?.Dispose();
            _activeCts = null;
            SyncCompleted?.Invoke(this, summary);
        }

        return summary;
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

        byte[] fileBytes = await File.ReadAllBytesAsync(filePath);
        string base64Data = Convert.ToBase64String(fileBytes);
        string fileName = Path.GetFileName(filePath);
        string mimeType = GetMimeType(fileName);

        string normalizedRelativePath = string.IsNullOrWhiteSpace(relativePath)
            ? fileName
            : relativePath.Replace('\\', '/').TrimStart('/');

        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("filename", fileName),
            new KeyValuePair<string, string>("relativePath", normalizedRelativePath),
            new KeyValuePair<string, string>("mimeType", mimeType),
            new KeyValuePair<string, string>("data", base64Data)
        });

        var response = await _httpClient.PostAsync(webAppUrl, content);
        response.EnsureSuccessStatusCode();

        var responseString = await response.Content.ReadAsStringAsync();

        try
        {
            var result = JsonSerializer.Deserialize<JsonElement>(responseString);
            if (result.TryGetProperty("status", out var status) && status.GetString() == "error")
            {
                string msg = result.TryGetProperty("message", out var m) ? m.GetString() ?? "Error desconocido" : "Error desconocido";
                throw new Exception($"Apps Script Error: {msg}");
            }

            return result.TryGetProperty("fileId", out var id) ? id.GetString() : null;
        }
        catch (JsonException)
        {
            string rawPreview = responseString.Length > 200 ? responseString[..200] + "..." : responseString;
            throw new Exception($"Respuesta inválida de Apps Script (no es JSON):\n{rawPreview}");
        }
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
                                LastModified = fileInfo.LastWriteTimeUtc,
                                Hash = ComputeSha256(file)
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
    /// The folder-to-destination mappings to synchronize: the main folder always comes
    /// first, followed by every additional source. The main folder is not replaced by the
    /// list — adding sources adds mappings, it never removes the one already configured.
    ///
    /// A source repeating the main folder with the same destination is dropped so it is not
    /// uploaded twice.
    /// </summary>
    private IEnumerable<SyncSource> EnumerateSources()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(_settings.LocalFolderPath))
        {
            seen.Add($"|{_settings.LocalFolderPath}");
            yield return new SyncSource
            {
                Name = "Carpeta principal",
                LocalFolderPath = _settings.LocalFolderPath,
                DestinationPrefix = string.Empty
            };
        }

        foreach (var source in _settings.Sources)
        {
            if (!source.IsEnabled || string.IsNullOrWhiteSpace(source.LocalFolderPath))
                continue;

            if (seen.Add($"{source.DestinationPrefix}|{source.LocalFolderPath}"))
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
        var collected = new List<LocalFileMetadata>();

        foreach (var source in EnumerateSources())
        {
            token.ThrowIfCancellationRequested();

            ReportProgress(progress, new SyncProgressReport
            {
                StatusMessage = $"Escaneando {(string.IsNullOrWhiteSpace(source.Name) ? source.LocalFolderPath : source.Name)}..."
            });

            var filters = SyncFilterOptions.Create(
                source.IncludedExtensions ?? _settings.IncludedExtensions,
                source.ExcludedExtensions ?? _settings.ExcludedExtensions,
                source.ExcludedFolders ?? _settings.ExcludedFolders,
                source.MaxFileSizeMb ?? _settings.MaxFileSizeMb);

            foreach (var file in ScanFolder(source.LocalFolderPath, filters))
            {
                file.RelativePath = CombineDestination(source.DestinationPrefix, file.RelativePath);
                file.HashKey = $"{source.DestinationPrefix}|{file.FilePath}";
                collected.Add(file);
            }
        }

        if (_settings.SyncUnversionedClaudeMarkdown)
        {
            ReportProgress(progress, new SyncProgressReport
            {
                StatusMessage = "Buscando CLAUDE.md sin versionar..."
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
                    // The whole relative path becomes the file name: a tree of files all called
                    // CLAUDE.md would otherwise collapse into one at the destination.
                    var flattenedName = Path.GetRelativePath(profileRoot, path)
                        .Replace(Path.DirectorySeparatorChar, '_')
                        .Replace(Path.AltDirectorySeparatorChar, '_');

                    collected.Add(new LocalFileMetadata
                    {
                        FilePath = path,
                        FileName = fileInfo.Name,
                        RelativePath = CombineDestination(_settings.ClaudeMarkdownDestinationPrefix, flattenedName),
                        FileSize = fileInfo.Length,
                        LastModified = fileInfo.LastWriteTimeUtc,
                        Hash = ComputeSha256(path),
                        HashKey = $"{_settings.ClaudeMarkdownDestinationPrefix}|{path}"
                    });
                }
                catch
                {
                    // Skip files that cannot be read or are locked
                }
            }
        }

        return collected;
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

    private string? GetKnownHash(string filePath)
    {
        lock (_hashLock)
        {
            return _hashIndex.TryGetValue(filePath, out var hash) ? hash : null;
        }
    }

    private void SaveKnownHash(string filePath, string hash)
    {
        lock (_hashLock)
        {
            _hashIndex[filePath] = hash;
            SaveHashIndex();
        }
    }

    private void LoadHashIndex()
    {
        lock (_hashLock)
        {
            try
            {
                if (File.Exists(HashIndexFile))
                {
                    var json = File.ReadAllText(HashIndexFile);
                    _hashIndex = JsonSerializer.Deserialize<Dictionary<string, string>>(json) 
                                 ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }
            }
            catch
            {
                _hashIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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

    private DriveSyncSettings LoadSettings()
    {
        try
        {
            var json = LocalSettingsHelper.Get(SettingsKey);
            if (!string.IsNullOrEmpty(json))
            {
                var settings = JsonSerializer.Deserialize<DriveSyncSettings>(json);
                if (settings != null) return settings;
            }
        }
        catch { }

        return new DriveSyncSettings();
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

    public void Dispose()
    {
        _scheduleService.WorkEnded -= OnWorkEnded;
        _activeCts?.Cancel();
        _activeCts?.Dispose();
        _httpClient.Dispose();
    }
}
