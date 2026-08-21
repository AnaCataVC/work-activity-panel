using System.Collections.Generic;

namespace WorkActivityPanel.Models;

/// <summary>
/// Information about GitHub CLI authentication state and configured accounts.
/// </summary>
public class GitHubAccountInfo
{
    /// <summary>
    /// Indicates whether the GitHub CLI ('gh') is detected and accessible.
    /// </summary>
    public bool IsGhInstalled { get; set; }

    /// <summary>
    /// The currently active GitHub account username (e.g. "AnaCataVC").
    /// </summary>
    public string? ActiveAccount { get; set; }

    /// <summary>
    /// List of all logged-in GitHub accounts available in the environment.
    /// </summary>
    public List<string> AvailableAccounts { get; set; } = new();

    /// <summary>
    /// Status or diagnostic message.
    /// </summary>
    public string? StatusMessage { get; set; }

    /// <summary>
    /// The configured work GitHub account username.
    /// </summary>
    public string? WorkAccount { get; set; }

    /// <summary>
    /// The configured personal GitHub account username.
    /// </summary>
    public string? PersonalAccount { get; set; }

    /// <summary>
    /// True if an active account is configured and authenticated.
    /// </summary>
    public bool IsAuthenticated => IsGhInstalled && !string.IsNullOrEmpty(ActiveAccount);

    /// <summary>
    /// True if there are 2 or more accounts available to switch between.
    /// </summary>
    public bool HasMultipleAccounts => AvailableAccounts.Count > 1;

    /// <summary>
    /// True if the currently active account matches the configured work account.
    /// </summary>
    public bool IsActiveAccountWorkAccount =>
        !string.IsNullOrEmpty(ActiveAccount) &&
        !string.IsNullOrEmpty(WorkAccount) &&
        string.Equals(ActiveAccount, WorkAccount, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True if the currently active account matches the configured personal account.
    /// </summary>
    public bool IsActiveAccountPersonalAccount =>
        !string.IsNullOrEmpty(ActiveAccount) &&
        !string.IsNullOrEmpty(PersonalAccount) &&
        string.Equals(ActiveAccount, PersonalAccount, StringComparison.OrdinalIgnoreCase);
}
