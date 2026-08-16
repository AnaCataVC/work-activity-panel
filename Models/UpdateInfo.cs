using System;

namespace WorkActivityPanel.Models;

/// <summary>
/// Model containing information about application updates discovered via GitHub Releases.
/// </summary>
public class UpdateInfo
{
    public bool IsUpdateAvailable { get; set; }
    public string CurrentVersion { get; set; } = string.Empty;
    public string LatestVersion { get; set; } = string.Empty;
    public string ReleaseTitle { get; set; } = string.Empty;
    public string ReleaseNotes { get; set; } = string.Empty;
    public string ReleaseHtmlUrl { get; set; } = string.Empty;
    public string? DownloadUrl { get; set; }
    public string? InstallerFileName { get; set; }
    public long InstallerSizeBytes { get; set; }
    public string? ErrorMessage { get; set; }

    public bool IsSuccess => string.IsNullOrEmpty(ErrorMessage);
}
