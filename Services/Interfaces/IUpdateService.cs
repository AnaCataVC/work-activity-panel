using System;
using System.Threading;
using System.Threading.Tasks;
using WorkActivityPanel.Models;

namespace WorkActivityPanel.Services.Interfaces;

/// <summary>
/// Service contract for checking and downloading updates from GitHub Releases.
/// </summary>
public interface IUpdateService
{
    /// <summary>
    /// Gets the current installed version of the application.
    /// </summary>
    string CurrentAppVersion { get; }

    /// <summary>
    /// Checks GitHub Releases for newer application versions.
    /// </summary>
    Task<UpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the update installer executable to a temporary location.
    /// </summary>
    Task<string> DownloadUpdateAsync(
        string downloadUrl,
        string? fileName = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Launches the downloaded installer executable to start the in-place upgrade.
    /// </summary>
    void LaunchInstaller(string installerPath);

    /// <summary>
    /// Downloads <paramref name="update"/>'s installer with progress reporting and launches it.
    /// </summary>
    /// <param name="onBeforeLaunch">Invoked right after the download completes, before the installer starts.</param>
    Task DownloadAndInstallAsync(UpdateInfo update, IProgress<double>? progress = null, Action? onBeforeLaunch = null);
}
