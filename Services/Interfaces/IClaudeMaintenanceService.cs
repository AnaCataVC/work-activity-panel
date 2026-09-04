using System;
using System.Threading;
using System.Threading.Tasks;
using WorkActivityPanel.Models;

namespace WorkActivityPanel.Services.Interfaces;

/// <summary>
/// Reports what the local Claude stores are costing on disk and performs the two cleanup
/// actions on demand. Every destructive operation is explicit: this service never runs on a
/// timer and never acts as a side effect of a scan.
/// </summary>
public interface IClaudeMaintenanceService
{
    ClaudeMaintenanceSettings Settings { get; }

    void UpdateSettings(ClaudeMaintenanceSettings settings);

    /// <summary>
    /// Measures both stores without modifying anything.
    /// </summary>
    Task<ClaudeMaintenanceReport> ScanAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently deletes transcripts older than the configured retention. Files touched in
    /// the last day are always kept, so a resumed session is never removed under the user.
    /// </summary>
    Task<ClaudeCleanupResult> DeleteStaleTranscriptsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Flags stale desktop sessions as archived, which hides them from the session list.
    /// This reclaims no disk space. Refused while Claude is running, because the application
    /// owns those files in memory and would overwrite the change.
    /// </summary>
    Task<ClaudeCleanupResult> ArchiveStaleSessionsAsync(CancellationToken cancellationToken = default);

    event EventHandler<ClaudeMaintenanceReport>? ReportUpdated;
}
