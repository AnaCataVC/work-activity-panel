using System.Diagnostics;
using Microsoft.Extensions.Logging;
using WorkActivityPanel.Helpers;
using WorkActivityPanel.Services.Interfaces;

namespace WorkActivityPanel.Services;

/// <summary>
/// Implementation of IAppLauncherService for launching and checking applications.
/// </summary>
public class AppLauncherService : IAppLauncherService
{
    private readonly ILogger<AppLauncherService> _logger;

    public AppLauncherService(ILogger<AppLauncherService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsSlackRunning()
    {
        return Process.GetProcessesByName("slack").Length > 0;
    }

    /// <inheritdoc />
    public bool IsGranolaRunning()
    {
        return Process.GetProcessesByName("Granola").Length > 0;
    }

    /// <inheritdoc />
    public void EnsureSlackRunning()
    {
        if (!IsSlackRunning())
        {
            _logger.LogInformation("Slack is not running. Launching Slack...");
            LaunchSlack();
        }
        else
        {
            _logger.LogInformation("Slack is already running.");
        }
    }

    /// <inheritdoc />
    public void EnsureGranolaRunning()
    {
        if (!IsGranolaRunning())
        {
            _logger.LogInformation("Granola is not running. Launching Granola...");
            LaunchGranola();
        }
        else
        {
            _logger.LogInformation("Granola is already running.");
        }
    }

    /// <inheritdoc />
    public void LaunchSlack()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "slack:",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch Slack.");
        }
    }

    /// <inheritdoc />
    public string? GetGranolaExecutablePath()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        return PathHelpers.FindFirstExisting(
            Path.Combine(localAppData, "Programs", "@granolaelectron", "Granola.exe"),
            Path.Combine(localAppData, "Programs", "Granola", "Granola.exe"),
            Path.Combine(localAppData, "Granola", "Granola.exe"),
            Path.Combine(programFiles, "Granola", "Granola.exe"),
            Path.Combine(programFilesX86, "Granola", "Granola.exe"));
    }

    /// <inheritdoc />
    public void LaunchGranola()
    {
        try
        {
            string? granolaPath = GetGranolaExecutablePath();

            if (!string.IsNullOrEmpty(granolaPath))
            {
                _logger.LogInformation("Launching Granola from: {Path}", granolaPath);
                Process.Start(new ProcessStartInfo
                {
                    FileName = granolaPath,
                    UseShellExecute = true
                });
                return;
            }

            // Fallback 1: Windows URI protocol scheme
            _logger.LogInformation("Granola binary not found in standard paths. Attempting to launch via URI scheme 'granola:'...");
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "granola:",
                    UseShellExecute = true
                });
                return;
            }
            catch (Exception uriEx)
            {
                _logger.LogWarning(uriEx, "Failed to launch Granola via URI scheme.");
            }

            // Fallback 2: Direct executable name in PATH
            _logger.LogInformation("Attempting fallback launch using executable name 'Granola.exe'...");
            Process.Start(new ProcessStartInfo
            {
                FileName = "Granola.exe",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch Granola.");
        }
    }

    /// <inheritdoc />
    public void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open URL {Url}.", url);
        }
    }
}
