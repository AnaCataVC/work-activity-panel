using System;
using System.Threading;
using System.Threading.Tasks;
using WorkActivityPanel.Models;

namespace WorkActivityPanel.Services.Interfaces;

/// <summary>
/// Service interface for Google Drive folder synchronization.
/// </summary>
public interface IDriveSyncService
{
    /// <summary>
    /// Current drive sync configuration settings.
    /// </summary>
    DriveSyncSettings Settings { get; }

    /// <summary>
    /// Indicates whether synchronization is configured with a valid WebApp URL and folder.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Indicates whether a synchronization process is currently running.
    /// </summary>
    bool IsSyncing { get; }

    /// <summary>
    /// Updates and persists the sync settings.
    /// </summary>
    void UpdateSettings(DriveSyncSettings settings);

    /// <summary>
    /// Runs a full synchronization job according to configured settings.
    /// </summary>
    Task<SyncResultSummary> RunSyncAsync(
        IProgress<SyncProgressReport>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels any active synchronization job.
    /// </summary>
    void CancelSync();

    /// <summary>
    /// Tests connectivity with the Google Apps Script Web App endpoint.
    /// </summary>
    Task<string?> TestConnectionAsync(string webAppUrl);

    /// <summary>
    /// Uploads a single file directly to Google Drive via the Web App endpoint.
    /// </summary>
    Task<string?> UploadSingleFileAsync(string filePath, string webAppUrl, string? relativePath = null);

    /// <summary>
    /// List of errors recorded in the most recent synchronization or retry run.
    /// </summary>
    IReadOnlyList<SyncErrorItem> LastSyncErrors { get; }

    /// <summary>
    /// Retries uploading only the files that previously failed.
    /// </summary>
    Task<SyncResultSummary> RetryFailedFilesAsync(
        IProgress<SyncProgressReport>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the last recorded sync errors.
    /// </summary>
    void ClearSyncErrors();

    /// <summary>
    /// Event triggered when sync settings are updated.
    /// </summary>
    event EventHandler? SettingsChanged;

    /// <summary>
    /// Event triggered when sync progress updates.
    /// </summary>
    event EventHandler<SyncProgressReport>? SyncProgressChanged;

    /// <summary>
    /// Event triggered when sync completes.
    /// </summary>
    event EventHandler<SyncResultSummary>? SyncCompleted;

    /// <summary>
    /// Event triggered when the list of sync errors changes.
    /// </summary>
    event EventHandler<IReadOnlyList<SyncErrorItem>>? SyncErrorsChanged;
}

