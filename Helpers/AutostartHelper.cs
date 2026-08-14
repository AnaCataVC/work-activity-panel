using Microsoft.Win32;

namespace WorkActivityPanel.Helpers;

/// <summary>
/// Helper class for managing application autostart on Windows.
/// </summary>
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
            key?.SetValue(AppName, $"\"{processPath}\"");
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
