namespace WorkActivityPanel.Models;

/// <summary>
/// Settings for GitHub CLI account management and schedule-based account switching.
/// </summary>
public class GitHubSettings
{
    /// <summary>
    /// The username designated as the work GitHub account (e.g. "CataVillalobosC").
    /// </summary>
    public string? WorkAccount { get; set; }

    /// <summary>
    /// The username designated as the personal/default GitHub account (e.g. "AnaCataVC").
    /// </summary>
    public string? PersonalAccount { get; set; }

    /// <summary>
    /// Whether to automatically switch to the work account when the work schedule starts.
    /// </summary>
    public bool AutoSwitchOnWorkStart { get; set; } = true;

    /// <summary>
    /// Whether to automatically switch to the personal account when the work schedule ends.
    /// </summary>
    public bool AutoSwitchOnWorkEnd { get; set; } = true;
}
