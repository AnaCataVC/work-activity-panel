using System;
using System.Collections.Generic;
using System.IO;

namespace WorkActivityPanel.Models;

/// <summary>
/// Metadata for a local file discovered during folder scanning.
/// </summary>
public class LocalFileMetadata
{
    private string? _hashKey;

    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime LastModified { get; set; }

    /// <summary>
    /// Key identifying this file in the incremental-sync hash index. Falls back to the
    /// absolute path, which is what single-source syncs have always used, so existing
    /// indexes stay valid. Sources that write under a destination prefix qualify the key
    /// with it, so one local file synced to two destinations is tracked once per
    /// destination instead of the second one being skipped as unchanged.
    /// </summary>
    public string HashKey
    {
        get => string.IsNullOrEmpty(_hashKey) ? FilePath : _hashKey;
        set => _hashKey = value;
    }
}

/// <summary>
/// Rules and criteria for filtering files during folder scanning.
/// </summary>
public class SyncFilterOptions
{
    public HashSet<string> IncludedExtensions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ExcludedExtensions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ExcludedFolders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public long MaxFileSizeBytes { get; set; } = 50L * 1024 * 1024; // 50 MB default

    public static SyncFilterOptions Create(
        string? includedExtensions,
        string? excludedExtensions,
        string? excludedFolders,
        long maxFileSizeMb)
    {
        var options = new SyncFilterOptions
        {
            MaxFileSizeBytes = Math.Max(1, maxFileSizeMb) * 1024L * 1024L
        };

        if (!string.IsNullOrWhiteSpace(includedExtensions))
        {
            foreach (var ext in includedExtensions.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var normalized = ext.Trim();
                if (!normalized.StartsWith('.')) normalized = "." + normalized;
                options.IncludedExtensions.Add(normalized);
            }
        }

        if (!string.IsNullOrWhiteSpace(excludedExtensions))
        {
            foreach (var ext in excludedExtensions.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var normalized = ext.Trim();
                if (!normalized.StartsWith('.')) normalized = "." + normalized;
                options.ExcludedExtensions.Add(normalized);
            }
        }

        if (!string.IsNullOrWhiteSpace(excludedFolders))
        {
            foreach (var folder in excludedFolders.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                options.ExcludedFolders.Add(folder.Trim());
            }
        }

        return options;
    }

    public bool IsFolderExcluded(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName)) return false;
        return ExcludedFolders.Contains(folderName);
    }

    public bool ShouldIncludeFile(FileInfo fileInfo, out string? skipReason)
    {
        skipReason = null;
        var ext = fileInfo.Extension;

        // 1. Excluded extensions
        if (ExcludedExtensions.Contains(ext))
        {
            skipReason = $"Excluded extension ({ext})";
            return false;
        }

        // 2. Whitelist check
        if (IncludedExtensions.Count > 0 && !IncludedExtensions.Contains(ext))
        {
            skipReason = $"Extension not in whitelist ({ext})";
            return false;
        }

        // 3. Size limit
        if (MaxFileSizeBytes > 0 && fileInfo.Length > MaxFileSizeBytes)
        {
            var sizeMb = fileInfo.Length / (1024.0 * 1024.0);
            var limitMb = MaxFileSizeBytes / (1024.0 * 1024.0);
            skipReason = $"Exceeds maximum size ({sizeMb:F1} MB > {limitMb:F1} MB)";
            return false;
        }

        return true;
    }
}

/// <summary>
/// Real-time progress report during synchronization.
/// </summary>
public class SyncProgressReport
{
    public int TotalFiles { get; set; }
    public int ProcessedFiles { get; set; }
    public string CurrentFileName { get; set; } = string.Empty;
    public int UploadedCount { get; set; }
    public int SkippedCount { get; set; }
    public int ErrorCount { get; set; }
    public string StatusMessage { get; set; } = string.Empty;
    public double Percentage => TotalFiles == 0 ? 0 : Math.Round((double)ProcessedFiles / TotalFiles * 100, 1);
}

/// <summary>
/// Final summary of a completed synchronization run.
/// </summary>
public class SyncResultSummary
{
    public int TotalScanned { get; set; }
    public int Uploaded { get; set; }
    public int Skipped { get; set; }
    public int Errors { get; set; }
    public bool Success => Errors == 0;
    public string Message { get; set; } = string.Empty;
}
