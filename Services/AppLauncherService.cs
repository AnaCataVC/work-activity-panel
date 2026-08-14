using System.Diagnostics;
using Microsoft.Extensions.Logging;
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
    public void LaunchGranola()
    {
        try
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string granolaPath = Path.Combine(localAppData, "Granola", "Granola.exe");

            if (File.Exists(granolaPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = granolaPath,
                    UseShellExecute = true
                });
            }
            else
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "Granola.exe",
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch Granola.");
        }
    }
}
