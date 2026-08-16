using System;
using System.Collections.Generic;
using System.Text.Json;
using WorkActivityPanel.Helpers;
using WorkActivityPanel.Models;
using Xunit;

namespace WorkActivityPanel.Tests;

public class LocalSettingsAndPersistenceTests
{
    [Fact]
    public void LocalSettingsHelper_SetAndGet_ReturnsPersistedValue()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), "test_settings_" + Guid.NewGuid() + ".json");
        LocalSettingsHelper.SettingsFilePath = tempFile;

        try
        {
            const string testKey = "Test_Persistence_Key";
            const string testValue = "Test_Value_12345";

            // Act
            LocalSettingsHelper.Set(testKey, testValue);
            var retrieved = LocalSettingsHelper.Get(testKey);

            // Assert
            Assert.Equal(testValue, retrieved);

            // Cleanup
            LocalSettingsHelper.Remove(testKey);
            Assert.Null(LocalSettingsHelper.Get(testKey));
        }
        finally
        {
            LocalSettingsHelper.ResetToDefaultPath();
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { }
            }
        }
    }

    [Fact]
    public void WorkSchedule_SerializationAndDeserialization_MaintainsFullState()
    {
        // Arrange
        var original = new WorkSchedule
        {
            StartTime = new TimeSpan(8, 30, 0),
            EndTime = new TimeSpan(17, 30, 0),
            WorkDays = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday },
            IsVacationMode = true
        };

        // Act
        string json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<WorkSchedule>(json);

        // Assert
        Assert.NotNull(restored);
        Assert.Equal(original.StartTime, restored.StartTime);
        Assert.Equal(original.EndTime, restored.EndTime);
        Assert.Equal(original.WorkDays.Count, restored.WorkDays.Count);
        Assert.Contains(DayOfWeek.Wednesday, restored.WorkDays);
        Assert.True(restored.IsVacationMode);
    }

    [Fact]
    public void DriveSyncSettings_SerializationAndDeserialization_PreservesAllFiltersAndFlags()
    {
        // Arrange
        var original = new DriveSyncSettings
        {
            LocalFolderPath = @"C:\Mock\WorkFolder",
            WebAppUrl = "https://script.google.com/macros/s/AKfycbz_test/exec",
            IncludedExtensions = ".pdf, .docx, .xlsx",
            ExcludedExtensions = ".tmp, .bak",
            ExcludedFolders = "node_modules, bin, obj",
            MaxFileSizeMb = 100,
            OnlyModifiedOrNew = true,
            AutoSyncOnWorkEnd = true,
            IsEnabled = true,
            LastSyncStatus = "Exitoso"
        };

        // Act
        string json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<DriveSyncSettings>(json);

        // Assert
        Assert.NotNull(restored);
        Assert.Equal(original.LocalFolderPath, restored.LocalFolderPath);
        Assert.Equal(original.WebAppUrl, restored.WebAppUrl);
        Assert.Equal(original.IncludedExtensions, restored.IncludedExtensions);
        Assert.Equal(original.ExcludedExtensions, restored.ExcludedExtensions);
        Assert.Equal(original.ExcludedFolders, restored.ExcludedFolders);
        Assert.Equal(100, restored.MaxFileSizeMb);
        Assert.True(restored.OnlyModifiedOrNew);
        Assert.True(restored.AutoSyncOnWorkEnd);
        Assert.Equal("Exitoso", restored.LastSyncStatus);
    }
}
