using System;
using System.Collections.Generic;
using System.IO;

namespace WorkActivityPanel.Models;

/// <summary>
/// One local folder to synchronize, with the destination prefix it lands under and
/// optional filter overrides. Different folders need different rules: a documents
/// folder and a tool's configuration folder have almost nothing in common in terms of
/// what is worth uploading.
/// </summary>
public class SyncSource
{
    /// <summary>Label shown in the UI and in progress messages.</summary>
    public string Name { get; set; } = string.Empty;

    public string LocalFolderPath { get; set; } = string.Empty;

    /// <summary>
    /// Subfolder of the Drive destination this source writes into. Empty means the
    /// destination root, which is how a single-source setup has always behaved.
    /// </summary>
    public string DestinationPrefix { get; set; } = string.Empty;

    /// <summary>Null falls back to the global setting of the same name.</summary>
    public string? IncludedExtensions { get; set; }

    /// <summary>Null falls back to the global setting of the same name.</summary>
    public string? ExcludedExtensions { get; set; }

    /// <summary>Null falls back to the global setting of the same name.</summary>
    public string? ExcludedFolders { get; set; }

    /// <summary>Null falls back to the global setting of the same name.</summary>
    public long? MaxFileSizeMb { get; set; }

    public bool IsEnabled { get; set; } = true;
}

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

    /// <summary>
    /// Additional folder-to-destination mappings, synchronized alongside
    /// <see cref="LocalFolderPath"/> rather than instead of it: the main folder keeps its
    /// own mapping whether this list is empty or not.
    /// </summary>
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
