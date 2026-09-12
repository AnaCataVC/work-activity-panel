using System.Runtime.Versioning;
using Microsoft.Win32;

namespace WorkActivityPanel.Helpers;

/// <summary>
/// Helper class for managing application autostart on Windows.
/// </summary>
[SupportedOSPlatform("windows")]
public static class AutostartHelper
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "WorkActivityPanel";

    /// <summary>
    /// Checks if autostart is currently enabled for this application.
    /// </summary>
    public static bool IsAutostartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(AppName) != null;
        }
        catch
        {
            return false;
        }
    }

    public const string AutostartArgument = "--autostart";

    /// <summary>
    /// Builds the standard command line string for registry autostart registration.
    /// </summary>
    public static string BuildAutostartCommandLine(string executablePath) => $"\"{executablePath}\" {AutostartArgument}";

    /// <summary>
    /// Checks whether the supplied command line arguments contain the autostart flag.
    /// </summary>
    public static bool HasAutostartArgument(IEnumerable<string>? args) =>
        args?.Any(a => string.Equals(a, AutostartArgument, StringComparison.OrdinalIgnoreCase)) ?? false;

    /// <summary>
    /// Ensures that if autostart is active in the registry, it includes the required <see cref="AutostartArgument"/>
    /// and points to the current process executable path.
    /// </summary>
    public static void EnsureAutostartSynced()
    {
        try
        {
            var processPath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(processPath)) return;

            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            var existingValue = key?.GetValue(AppName) as string;
            if (!string.IsNullOrEmpty(existingValue))
            {
                var expectedValue = BuildAutostartCommandLine(processPath);
                if (!string.Equals(existingValue.Trim(), expectedValue.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    key?.SetValue(AppName, expectedValue);
                }
            }
        }
        catch
        {
            // Handle or log error
        }
    }

    /// <summary>
    /// Enables autostart by adding the application to the startup registry.
    /// </summary>
    public static void EnableAutostart()
    {
        try
        {
            var processPath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(processPath)) return;

            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            key?.SetValue(AppName, BuildAutostartCommandLine(processPath));
        }
        catch
        {
            // Handle or log error
        }
    }

    /// <summary>
    /// Disables autostart by removing the application from the startup registry.
    /// </summary>
    public static void DisableAutostart()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            key?.DeleteValue(AppName, false);
        }
        catch
        {
            // Handle or log error
        }
    }

    /// <summary>
    /// Sets the autostart state.
    /// </summary>
    public static void SetAutostart(bool enabled)
    {
        if (enabled) EnableAutostart();
        else DisableAutostart();
    }
}
