using System;
using System.IO;
using WorkActivityPanel.Helpers;

namespace WorkActivityPanel.Tests;

/// <summary>
/// Redirects <see cref="LocalSettingsHelper"/> to a throwaway settings file for the scope's
/// lifetime, then restores the default path and deletes the throwaway file on dispose.
/// </summary>
internal sealed class TempSettingsFileScope : IDisposable
{
    public string FilePath { get; }

    public TempSettingsFileScope(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(Path.GetTempPath(), $"test_settings_{Guid.NewGuid():N}.json");
        LocalSettingsHelper.SettingsFilePath = FilePath;
    }

    public void Dispose()
    {
        LocalSettingsHelper.ResetToDefaultPath();
        if (File.Exists(FilePath))
        {
            try { File.Delete(FilePath); } catch { }
        }
    }
}
