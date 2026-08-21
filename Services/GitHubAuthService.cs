using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WorkActivityPanel.Helpers;
using WorkActivityPanel.Models;
using WorkActivityPanel.Services.Interfaces;

namespace WorkActivityPanel.Services;

/// <summary>
/// Service implementation for managing GitHub accounts and switching via GitHub CLI ('gh').
/// </summary>
public class GitHubAuthService : IGitHubAuthService
{
    private const string SettingsKey = "GitHubAccountSettings";
    private readonly ILogger<GitHubAuthService> _logger;
    private GitHubSettings _settings;

    public event EventHandler<string?>? ActiveAccountChanged;
    public event EventHandler? SettingsChanged;

    public GitHubSettings Settings => _settings;

    public GitHubAuthService(ILogger<GitHubAuthService> logger)
    {
        _logger = logger;
        _settings = LoadSettings();
    }

    public void UpdateSettings(GitHubSettings settings)
    {
        _settings = settings ?? new GitHubSettings();
        SaveSettings();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public async Task<GitHubAccountInfo> GetAccountsStatusAsync()
    {
        return await Task.Run(() =>
        {
            var info = new GitHubAccountInfo
            {
                WorkAccount = _settings.WorkAccount,
                PersonalAccount = _settings.PersonalAccount
            };

            string? ghPath = FindGhExecutable();
            info.IsGhInstalled = !string.IsNullOrEmpty(ghPath);

            // First check config file for fast instant load
            string? hostsFilePath = GetHostsFilePath();
            if (hostsFilePath != null && File.Exists(hostsFilePath))
            {
                try
                {
                    string content = File.ReadAllText(hostsFilePath);
                    var parsed = ParseHostsYaml(content);
                    info.ActiveAccount = parsed.ActiveAccount;
                    info.AvailableAccounts = parsed.AvailableAccounts;
                    info.StatusMessage = !string.IsNullOrEmpty(info.ActiveAccount)
                        ? $"Cuenta activa: {info.ActiveAccount}"
                        : "Cuentas detectadas en hosts.yml";

                    if (info.IsAuthenticated)
                    {
                        return info;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error reading GitHub hosts.yml file.");
                }
            }

            // If gh is installed but hosts.yml was not found or empty, query gh auth status CLI
            if (info.IsGhInstalled && !string.IsNullOrEmpty(ghPath))
            {
                try
                {
                    var cliInfo = QueryGhAuthStatus(ghPath);
                    if (!string.IsNullOrEmpty(cliInfo.ActiveAccount))
                    {
                        info.ActiveAccount = cliInfo.ActiveAccount;
                    }
                    if (cliInfo.AvailableAccounts.Count > 0)
                    {
                        info.AvailableAccounts = cliInfo.AvailableAccounts;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to query 'gh auth status'.");
                }
            }

            if (!info.IsGhInstalled)
            {
                info.StatusMessage = "GitHub CLI no detectado en el sistema.";
            }
            else if (!info.IsAuthenticated)
            {
                info.StatusMessage = "No hay cuentas activas en GitHub CLI.";
            }
            else
            {
                info.StatusMessage = $"Cuenta activa: {info.ActiveAccount}";
            }

            return info;
        });
    }

    /// <inheritdoc />
    public async Task<(bool Success, string Message)> SwitchAccountAsync(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return (false, "El nombre de usuario no puede estar vacío.");
        }

        string? ghPath = FindGhExecutable();
        if (string.IsNullOrEmpty(ghPath))
        {
            return (false, "GitHub CLI no está instalado en este equipo.");
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ghPath,
                Arguments = $"auth switch -u {username.Trim()} --hostname github.com",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            string stdout = await process.StandardOutput.ReadToEndAsync();
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                _logger.LogInformation("Successfully switched GitHub account to {Username}", username);
                ActiveAccountChanged?.Invoke(this, username.Trim());
                return (true, $"Cuenta activa cambiada a {username.Trim()}");
            }

            string err = !string.IsNullOrWhiteSpace(stderr) ? stderr.Trim() : stdout.Trim();
            _logger.LogWarning("Failed to switch GitHub account: {Error}", err);
            return (false, $"Error al cambiar cuenta: {err}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while executing 'gh auth switch'.");
            return (false, $"Error: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public GitHubAccountInfo ParseHostsYaml(string yamlContent)
    {
        var info = new GitHubAccountInfo
        {
            IsGhInstalled = true
        };

        if (string.IsNullOrWhiteSpace(yamlContent))
        {
            return info;
        }

        var lines = yamlContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        bool inUsersSection = false;
        var accounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            var trimmed = line.Trim();

            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#"))
            {
                continue;
            }

            // Match active user: "user: <username>" or "    user: <username>"
            var userMatch = Regex.Match(line, @"^\s*user:\s*([a-zA-Z0-9_-]+)\s*$", RegexOptions.IgnoreCase);
            if (userMatch.Success)
            {
                info.ActiveAccount = userMatch.Groups[1].Value.Trim();
                accounts.Add(info.ActiveAccount);
                inUsersSection = false;
                continue;
            }

            // Match start of users block: "users:"
            if (Regex.IsMatch(line, @"^\s*users:\s*$", RegexOptions.IgnoreCase))
            {
                inUsersSection = true;
                continue;
            }

            // If we are under users block, lines like "    AnaCataVC:" or "        AnaCataVC:"
            if (inUsersSection)
            {
                // If the indentation ended or another root/sub property started (not matching username:)
                if (!line.StartsWith(" ") && !line.StartsWith("\t"))
                {
                    inUsersSection = false;
                }
                else
                {
                    var accountMatch = Regex.Match(trimmed, @"^([a-zA-Z0-9_-]+):$");
                    if (accountMatch.Success)
                    {
                        var acctName = accountMatch.Groups[1].Value.Trim();
                        // Ignore standard keys if any
                        if (!string.Equals(acctName, "git_protocol", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(acctName, "oauth_token", StringComparison.OrdinalIgnoreCase))
                        {
                            accounts.Add(acctName);
                        }
                    }
                }
            }
        }

        info.AvailableAccounts = accounts.ToList();

        // If no active user was explicitly set, but there is one account, default to it
        if (string.IsNullOrEmpty(info.ActiveAccount) && info.AvailableAccounts.Count == 1)
        {
            info.ActiveAccount = info.AvailableAccounts[0];
        }

        return info;
    }

    private GitHubAccountInfo QueryGhAuthStatus(string ghPath)
    {
        var info = new GitHubAccountInfo { IsGhInstalled = true };

        var startInfo = new ProcessStartInfo
        {
            FileName = ghPath,
            Arguments = "auth status",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(3000);

        string output = stdout + Environment.NewLine + stderr;
        var accounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Pattern 1: Logged in to github.com account <username>
        var accountMatches = Regex.Matches(output, @"account\s+([a-zA-Z0-9_-]+)", RegexOptions.IgnoreCase);
        foreach (Match m in accountMatches)
        {
            if (m.Groups.Count > 1)
            {
                accounts.Add(m.Groups[1].Value.Trim());
            }
        }

        // Pattern 2: Active account check
        // Look for blocks where Active account: true follows an account name
        var lines = output.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        string? currentAccount = null;
        foreach (var line in lines)
        {
            var match = Regex.Match(line, @"account\s+([a-zA-Z0-9_-]+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                currentAccount = match.Groups[1].Value.Trim();
            }

            if (line.Contains("Active account: true", StringComparison.OrdinalIgnoreCase) && currentAccount != null)
            {
                info.ActiveAccount = currentAccount;
            }
        }

        info.AvailableAccounts = accounts.ToList();
        return info;
    }

    private static string? GetHostsFilePath()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        string[] candidateFiles =
        [
            Path.Combine(appData, "GitHub CLI", "hosts.yml"),
            Path.Combine(userProfile, ".config", "gh", "hosts.yml"),
            Path.Combine(localAppData, "GitHub CLI", "hosts.yml")
        ];

        return candidateFiles.FirstOrDefault(File.Exists);
    }

    private static string? FindGhExecutable()
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        string[] candidates =
        [
            Path.Combine(programFiles, "GitHub CLI", "gh.exe"),
            Path.Combine(localAppData, "Programs", "GitHub CLI", "gh.exe"),
            Path.Combine(programFilesX86, "GitHub CLI", "gh.exe")
        ];

        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        // Test if "gh" is in PATH by attempting a silent resolve
        return "gh";
    }

    private GitHubSettings LoadSettings()
    {
        try
        {
            var json = LocalSettingsHelper.Get(SettingsKey);
            if (!string.IsNullOrEmpty(json))
            {
                var settings = JsonSerializer.Deserialize<GitHubSettings>(json);
                if (settings != null) return settings;
            }
        }
        catch { }

        return new GitHubSettings();
    }

    private void SaveSettings()
    {
        try
        {
            var json = JsonSerializer.Serialize(_settings);
            LocalSettingsHelper.Set(SettingsKey, json);
        }
        catch { }
    }
}
