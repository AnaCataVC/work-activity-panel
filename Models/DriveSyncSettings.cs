using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;

namespace WorkActivityPanel.Models;

/// <summary>
/// One local folder to synchronize and the name of the Drive subfolder it lands in.
/// Every source is a sibling of every other one at the destination: there is no main
/// folder, and no source nests inside another.
/// </summary>
public class SyncSource
{
    public string LocalFolderPath { get; set; } = string.Empty;

    /// <summary>Name of the destination subfolder in Drive.</summary>
    public string DestinationPrefix { get; set; } = string.Empty;

    /// <summary>
    /// Destination actually used when syncing. An empty name falls back to the local
    /// folder's own name, so a source can never end up writing loose into the Drive root
    /// and mixing its files with another source's.
    /// </summary>
    [JsonIgnore]
    public string EffectiveDestinationPrefix =>
        string.IsNullOrWhiteSpace(DestinationPrefix)
            ? new DirectoryInfo(LocalFolderPath.TrimEnd('\\', '/')).Name
            : DestinationPrefix.Replace('\\', '/').Trim('/');
}

/// <summary>
/// Settings for Google Drive synchronization.
/// </summary>
public class DriveSyncSettings
{
    public string WebAppUrl { get; set; } = string.Empty;

    /// <summary>
    /// Drive folder the Web App writes into, used only to open it from the panel. The
    /// bridge already knows where it uploads; this is the link a person can click.
    /// </summary>
    public string DriveFolderUrl { get; set; } = string.Empty;

    /// <summary>
    /// Single main folder of the layout that had one, kept so an existing configuration
    /// can be migrated into <see cref="Sources"/> on load and then cleared.
    /// </summary>
    [JsonPropertyName("LocalFolderPath")]
    public string LegacyMainFolderPath { get; set; } = string.Empty;

    /// <summary>
    /// Optional shared secret authentication token for Google Apps Script Web App.
    /// </summary>
    public string AuthToken { get; set; } = string.Empty;

    public string IncludedExtensions { get; set; } = string.Empty;
    public string ExcludedExtensions { get; set; } = ".tmp, .log, .exe, .bak, .zip";
    public string ExcludedFolders { get; set; } = "node_modules, .git, bin, obj, .vs, temp";
    public long MaxFileSizeMb { get; set; } = 20;
    public bool OnlyModifiedOrNew { get; set; } = true;
    public bool AutoSyncOnWorkEnd { get; set; } = true;
    public bool IsEnabled { get; set; } = true;
    public DateTime? LastSyncTime { get; set; }
    public string LastSyncStatus { get; set; } = "Nunca sincronizado";

    /// <summary>Every folder to synchronize. All of them land side by side in Drive.</summary>
    public List<SyncSource> Sources { get; set; } = new();

    /// <summary>
    /// Collect the agent instruction files (CLAUDE.md) scattered across the user profile
    /// that no git repository is tracking. Those are the ones nothing else backs up: a
    /// tracked one already lives in its repo's history.
    /// </summary>
    public bool SyncUnversionedClaudeMarkdown { get; set; }

    /// <summary>Destination subfolder for the files found by the sweep above.</summary>
    public string ClaudeMarkdownDestinationPrefix { get; set; } = "claude-md-unversioned";

    /// <summary>
    /// How deep under the user profile to look. The sweep walks directories, so an
    /// unbounded depth on a profile holding large data folders costs real time.
    /// </summary>
    public int ClaudeMarkdownScanDepth { get; set; } = 6;
}
