using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WorkActivityPanel.Helpers;
using WorkActivityPanel.Models;
using WorkActivityPanel.Services.Interfaces;

namespace WorkActivityPanel.Services;

public class ClaudeMaintenanceService : IClaudeMaintenanceService
{
    private const string SettingsKey = "ClaudeMaintenanceSettings";

    // Matches "isArchived":false with or without whitespace around the colon.
    private static readonly Regex ArchivedFalseRegex = new(@"""isArchived""\s*:\s*false", RegexOptions.Compiled);

    // A live session rewrites its transcript continuously, so anything touched this recently is
    // in use no matter what the retention says. This guard is what makes deletion safe to run
    // while Claude is open.
    private static readonly TimeSpan ActiveSessionGrace = TimeSpan.FromHours(24);

    // "isArchived" sits in the object header, but a variable-length cwd precedes it. The window
    // must clear the longest cwd without reaching the transcript, which can quote the same literal.
    private const int SessionHeaderChars = 1000;

    private readonly string _transcriptsRoot;
    private readonly string _sessionsRoot;
    private readonly Func<bool> _isClaudeRunning;
    private ClaudeMaintenanceSettings _settings;

    public ClaudeMaintenanceSettings Settings => _settings;

    public event EventHandler<ClaudeMaintenanceReport>? ReportUpdated;

    public ClaudeMaintenanceService()
        : this(null, null)
    {
    }

    /// <param name="claudeRunningProbe">
    /// Overridable so the archive guard can be exercised by tests on a machine where Claude
    /// happens to be open or closed; production uses the real process lookup.
    /// </param>
    public ClaudeMaintenanceService(string? transcriptsRoot, string? sessionsRoot, Func<bool>? claudeRunningProbe = null)
    {
        _transcriptsRoot = transcriptsRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");

        _sessionsRoot = sessionsRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude", "claude-code-sessions");

        _isClaudeRunning = claudeRunningProbe ?? IsClaudeProcessRunning;
        _settings = LoadSettings();
    }

    public void UpdateSettings(ClaudeMaintenanceSettings settings)
    {
        _settings = settings ?? new ClaudeMaintenanceSettings();
        SaveSettings();
    }

    public async Task<ClaudeMaintenanceReport> ScanAsync(CancellationToken cancellationToken = default)
    {
        var report = await Task.Run(() =>
        {
            var result = new ClaudeMaintenanceReport
            {
                ClaudeIsRunning = _isClaudeRunning()
            };

            result.Transcripts = MeasureStore(
                "Transcripts de sesiones", _transcriptsRoot, "*.jsonl",
                _settings.TranscriptRetentionDays, cancellationToken, reclaimsDiskSpace: true);

            result.Sessions = MeasureStore(
                "Índice de sesiones del escritorio", _sessionsRoot, "*.json",
                _settings.SessionRetentionDays, cancellationToken, reclaimsDiskSpace: false);

            return result;
        }, cancellationToken);

        ReportUpdated?.Invoke(this, report);
        return report;
    }

    public async Task<ClaudeCleanupResult> DeleteStaleTranscriptsAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var result = new ClaudeCleanupResult();

            if (!Directory.Exists(_transcriptsRoot))
            {
                result.Skipped = true;
                result.Message = "No hay carpeta de transcripts que limpiar.";
                return result;
            }

            DateTime staleBefore = DateTime.Now.AddDays(-_settings.TranscriptRetentionDays);
            DateTime activeAfter = DateTime.Now - ActiveSessionGrace;

            foreach (var file in EnumerateFiles(_transcriptsRoot, "*.jsonl"))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (file.LastWriteTime >= staleBefore || file.LastWriteTime >= activeAfter)
                {
                    continue;
                }

                long size = file.Length;
                try
                {
                    file.Delete();
                    result.FilesProcessed++;
                    result.BytesFreed += size;
                }
                catch (Exception ex)
                {
                    result.Failures.Add($"{file.Name}: {ex.Message}");
                }
            }

            result.Message = result.FilesProcessed == 0
                ? (result.Failures.Count > 0
                    ? $"No se pudieron borrar archivos ({result.Failures.Count} bloqueados o en uso)."
                    : "No había transcripts fuera de la retención.")
                : (result.Failures.Count > 0
                    ? $"Se eliminaron {result.FilesProcessed} transcripts y se liberaron {result.BytesFreedDisplay} ({result.Failures.Count} bloqueados o en uso)."
                    : $"Se eliminaron {result.FilesProcessed} transcripts y se liberaron {result.BytesFreedDisplay}.");

            return result;
        }, cancellationToken);
    }

    public async Task<ClaudeCleanupResult> ArchiveStaleSessionsAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var result = new ClaudeCleanupResult();

            if (!Directory.Exists(_sessionsRoot))
            {
                result.Skipped = true;
                result.Message = "No hay índice de sesiones que archivar.";
                return result;
            }

            if (_isClaudeRunning())
            {
                result.Skipped = true;
                result.Message = "Claude está abierto. Cierra la aplicación antes de archivar: mantiene estas sesiones en memoria y sobrescribiría el cambio.";
                return result;
            }

            DateTime staleBefore = DateTime.Now.AddDays(-_settings.SessionRetentionDays);

            foreach (var file in EnumerateFiles(_sessionsRoot, "*.json"))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (file.LastWriteTime >= staleBefore)
                {
                    continue;
                }

                try
                {
                    if (TryMarkArchived(file.FullName))
                    {
                        result.FilesProcessed++;
                    }
                }
                catch (Exception ex)
                {
                    result.Failures.Add($"{file.Name}: {ex.Message}");
                }
            }

            // Archiving only flips a flag, so it frees no disk space; saying so keeps the result
            // from reading like a cleanup that recovered nothing.
            result.Message = result.FilesProcessed == 0
                ? (result.Failures.Count > 0
                    ? $"No se pudieron archivar sesiones ({result.Failures.Count} bloqueadas o en uso)."
                    : "No había sesiones fuera de la retención sin archivar.")
                : (result.Failures.Count > 0
                    ? $"Se archivaron {result.FilesProcessed} sesiones ({result.Failures.Count} bloqueadas o en uso). Salen de la lista; no libera espacio en disco."
                    : $"Se archivaron {result.FilesProcessed} sesiones. Salen de la lista; no libera espacio en disco.");

            return result;
        }, cancellationToken);
    }

    private ClaudeStoreReport MeasureStore(
        string displayName, string root, string pattern, int retentionDays, CancellationToken cancellationToken, bool reclaimsDiskSpace = true)
    {
        var store = new ClaudeStoreReport
        {
            DisplayName = displayName,
            Exists = Directory.Exists(root),
            ReclaimsDiskSpace = reclaimsDiskSpace
        };

        if (!store.Exists)
        {
            return store;
        }

        DateTime staleBefore = DateTime.Now.AddDays(-retentionDays);

        foreach (var file in EnumerateFiles(root, pattern))
        {
            cancellationToken.ThrowIfCancellationRequested();

            store.TotalFiles++;
            store.TotalBytes += file.Length;

            if (file.LastWriteTime < staleBefore)
            {
                store.StaleFiles++;
                store.StaleBytes += file.Length;
            }
        }

        return store;
    }

    /// <summary>
    /// Enumerates a store defensively: an unreadable subtree must not abort the whole scan.
    /// </summary>
    private static IEnumerable<FileInfo> EnumerateFiles(string root, string pattern)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        IEnumerator<FileInfo> enumerator;
        try
        {
            enumerator = new DirectoryInfo(root).EnumerateFiles(pattern, options).GetEnumerator();
        }
        catch (Exception)
        {
            yield break;
        }

        using (enumerator)
        {
            while (true)
            {
                FileInfo current;
                try
                {
                    if (!enumerator.MoveNext()) break;
                    current = enumerator.Current;
                }
                catch (Exception)
                {
                    break;
                }

                yield return current;
            }
        }
    }

    private static bool TryMarkArchived(string path)
    {
        DateTime originalLastWrite = File.GetLastWriteTime(path);
        string text = File.ReadAllText(path);
        int split = Math.Min(SessionHeaderChars, text.Length);
        string head = text.Substring(0, split);

        var match = ArchivedFalseRegex.Match(head);
        if (!match.Success)
        {
            return false; // Already archived, or an unexpected header shape: leave it alone.
        }

        head = head.Remove(match.Index, match.Length)
                   .Insert(match.Index, "\"isArchived\":true");

        string tempPath = path + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(tempPath, head + text.Substring(split));
            File.Move(tempPath, path, overwrite: true);
            File.SetLastWriteTime(path, originalLastWrite);
            return true;
        }
        catch (Exception)
        {
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch { }
            throw;
        }
    }

    private static bool IsClaudeProcessRunning()
    {
        try
        {
            var processes = Process.GetProcessesByName("claude");
            try
            {
                return processes.Length > 0;
            }
            finally
            {
                foreach (var p in processes)
                {
                    p.Dispose();
                }
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    private ClaudeMaintenanceSettings LoadSettings()
    {
        try
        {
            string? json = LocalSettingsHelper.Get(SettingsKey);
            if (!string.IsNullOrWhiteSpace(json))
            {
                var loaded = JsonSerializer.Deserialize<ClaudeMaintenanceSettings>(json);
                if (loaded != null)
                {
                    return loaded;
                }
            }
        }
        catch (Exception)
        {
            // Malformed settings must not stop the panel from starting.
        }

        return new ClaudeMaintenanceSettings();
    }

    private void SaveSettings()
    {
        try
        {
            LocalSettingsHelper.Set(SettingsKey, JsonSerializer.Serialize(_settings));
        }
        catch (Exception)
        {
            // Ignore persistence errors, consistent with the other services.
        }
    }
}
