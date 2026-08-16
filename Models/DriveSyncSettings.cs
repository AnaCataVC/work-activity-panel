using System;
using System.IO;

namespace WorkActivityPanel.Models;

/// <summary>
/// Settings for Google Drive synchronization.
/// </summary>
public class DriveSyncSettings
{
    public string LocalFolderPath { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    public string WebAppUrl { get; set; } = string.Empty;
    public string IncludedExtensions { get; set; } = string.Empty;
    public string ExcludedExtensions { get; set; } = ".tmp, .log, .exe, .bak, .zip";
    public string ExcludedFolders { get; set; } = "node_modules, .git, bin, obj, .vs, temp";
    public long MaxFileSizeMb { get; set; } = 50;
    public bool OnlyModifiedOrNew { get; set; } = true;
    public bool AutoSyncOnWorkEnd { get; set; } = true;
    public bool IsEnabled { get; set; } = true;
    public DateTime? LastSyncTime { get; set; }
    public string LastSyncStatus { get; set; } = "Nunca sincronizado";
}
