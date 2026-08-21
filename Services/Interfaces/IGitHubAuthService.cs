using System;
using System.Threading.Tasks;
using WorkActivityPanel.Models;

namespace WorkActivityPanel.Services.Interfaces;

/// <summary>
/// Service interface for detecting and switching GitHub accounts via GitHub CLI.
/// </summary>
public interface IGitHubAuthService
{
    /// <summary>
    /// Event triggered when the active GitHub account has changed.
    /// </summary>
    event EventHandler<string?>? ActiveAccountChanged;

    /// <summary>
    /// Event triggered when GitHub settings have changed.
    /// </summary>
    event EventHandler? SettingsChanged;

    /// <summary>
    /// Gets the current GitHub configuration settings.
    /// </summary>
    GitHubSettings Settings { get; }

    /// <summary>
    /// Updates and persists the GitHub configuration settings.
    /// </summary>
    void UpdateSettings(GitHubSettings settings);

    /// <summary>
    /// Retrieves current GitHub authentication status, active account, and available accounts.
    /// </summary>
    Task<GitHubAccountInfo> GetAccountsStatusAsync();

    /// <summary>
    /// Switches the active GitHub CLI account to the specified username.
    /// </summary>
    /// <param name="username">GitHub username to activate.</param>
    /// <returns>Tuple indicating success and a status message.</returns>
    Task<(bool Success, string Message)> SwitchAccountAsync(string username);

    /// <summary>
    /// Helper to parse GitHub hosts.yml file content directly.
    /// </summary>
    /// <param name="yamlContent">Raw content of hosts.yml.</param>
    /// <returns>Parsed GitHub account info.</returns>
    GitHubAccountInfo ParseHostsYaml(string yamlContent);
}
