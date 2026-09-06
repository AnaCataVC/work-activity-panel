using System.Diagnostics;

namespace WorkActivityPanel.Helpers;

/// <summary>
/// Launches a file, URL, or URI scheme via the OS-registered shell handler.
/// </summary>
public static class ProcessLaunchHelper
{
    public static Process? ShellExecute(string fileName) =>
        Process.Start(new ProcessStartInfo { FileName = fileName, UseShellExecute = true });
}
