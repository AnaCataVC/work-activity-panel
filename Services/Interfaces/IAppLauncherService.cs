namespace WorkActivityPanel.Services.Interfaces;

/// <summary>
/// Service for launching and monitoring required applications.
/// </summary>
public interface IAppLauncherService
{
    /// <summary>
    /// Checks if Slack is currently running.
    /// </summary>
    bool IsSlackRunning();

    /// <summary>
    /// Checks if Granola is currently running.
    /// </summary>
    bool IsGranolaRunning();

    /// <summary>
    /// Ensures Slack is running, launching it if necessary.
    /// </summary>
    void EnsureSlackRunning();

    /// <summary>
    /// Ensures Granola is running, launching it if necessary.
    /// </summary>
    void EnsureGranolaRunning();

    /// <summary>
    /// Launches Slack.
    /// </summary>
    void LaunchSlack();

    /// <summary>
    /// Launches Granola.
    /// </summary>
    void LaunchGranola();

    /// <summary>
    /// Resolves the absolute path to the Granola executable if installed on the system.
    /// </summary>
    string? GetGranolaExecutablePath();
}
