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
    private static readonly string DefaultSettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WorkActivityPanel",
        "Data");
    private static string? _customSettingsFilePath;
    private static readonly object LockObj = new();
    private static Dictionary<string, string> _cache = new();

    public static string SettingsFilePath
    {
        get => _customSettingsFilePath ?? Path.Combine(DefaultSettingsDir, "settings.json");
        set
        {
            lock (LockObj)
            {
                _customSettingsFilePath = value;
                _cache.Clear();
                Load();
            }
        }
    }

    public static void ResetToDefaultPath()
    {
        lock (LockObj)
        {
            _customSettingsFilePath = null;
            _cache.Clear();
            Load();
        }
    }

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
                var path = SettingsFilePath;
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
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
            var filePath = SettingsFilePath;
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            string json = JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        }
        catch
        {
            // Ignore file write errors
        }
    }
}
