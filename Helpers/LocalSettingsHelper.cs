using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace WorkActivityPanel.Helpers;

/// <summary>
/// Thread-safe settings manager for unpackaged desktop applications.
/// Saves settings to %LocalAppData%\WorkActivityPanel\Data\settings.json.
/// </summary>
public static class LocalSettingsHelper
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WorkActivityPanel",
        "Data");
    private static readonly string SettingsFilePath = Path.Combine(SettingsDir, "settings.json");
    private static readonly object LockObj = new();
    private static Dictionary<string, string> _cache = new();

    static LocalSettingsHelper()
    {
        Load();
    }

    private static void Load()
    {
        lock (LockObj)
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    _cache = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
                }
            }
            catch
            {
                _cache = new();
            }
        }
    }

    public static string? Get(string key)
    {
        lock (LockObj)
        {
            return _cache.TryGetValue(key, out var val) ? val : null;
        }
    }

    public static void Set(string key, string value)
    {
        lock (LockObj)
        {
            _cache[key] = value;
            Save();
        }
    }

    public static void Remove(string key)
    {
        lock (LockObj)
        {
            if (_cache.Remove(key))
            {
                Save();
            }
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            string json = JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFilePath, json);
        }
        catch
        {
            // Ignore file write errors
        }
    }
}
