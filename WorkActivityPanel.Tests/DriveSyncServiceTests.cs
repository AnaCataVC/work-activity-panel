using System;
using System.IO;
using System.Threading.Tasks;
using Moq;
using WorkActivityPanel.Helpers;
using WorkActivityPanel.Models;
using WorkActivityPanel.Services;
using WorkActivityPanel.Services.Interfaces;
using Xunit;

namespace WorkActivityPanel.Tests;

public class DriveSyncServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly Mock<IScheduleService> _scheduleMock;
    private readonly DriveSyncService _service;

    public DriveSyncServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "WorkActivityPanel_SyncTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testDir);

        LocalSettingsHelper.SettingsFilePath = Path.Combine(_testDir, "test_settings.json");

        _scheduleMock = new Mock<IScheduleService>();
        _scheduleMock.Setup(s => s.CurrentSchedule).Returns(new WorkSchedule());
        _scheduleMock.Setup(s => s.IsVacationMode).Returns(false);

        _service = new DriveSyncService(_scheduleMock.Object);
    }

    public void Dispose()
    {
        _service.Dispose();
        LocalSettingsHelper.ResetToDefaultPath();
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, true); } catch { }
        }
    }

    [Fact]
    public void ScanFolder_ShouldExcludeBlacklistedExtensions()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_testDir, "document.pdf"), "PDF data");
        File.WriteAllText(Path.Combine(_testDir, "app.log"), "Log info");
        File.WriteAllText(Path.Combine(_testDir, "temp.tmp"), "Temp cache");

        var filters = SyncFilterOptions.Create(
            includedExtensions: "",
            excludedExtensions: ".log, .tmp",
            excludedFolders: "",
            maxFileSizeMb: 50
        );

        // Act
        var results = _service.ScanFolder(_testDir, filters);

        // Assert
        Assert.Single(results);
        Assert.Equal("document.pdf", results[0].FileName);
    }

    [Fact]
    public void ScanFolder_ShouldOnlyIncludeWhitelistedExtensions_WhenConfigured()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_testDir, "report.docx"), "Docx content");
        File.WriteAllText(Path.Combine(_testDir, "sheet.xlsx"), "Excel content");
        File.WriteAllText(Path.Combine(_testDir, "notes.txt"), "Text content");

        var filters = SyncFilterOptions.Create(
            includedExtensions: ".docx, .xlsx",
            excludedExtensions: "",
            excludedFolders: "",
            maxFileSizeMb: 50
        );

        // Act
        var results = _service.ScanFolder(_testDir, filters);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.FileName == "report.docx");
        Assert.Contains(results, r => r.FileName == "sheet.xlsx");
        Assert.DoesNotContain(results, r => r.FileName == "notes.txt");
    }

    [Fact]
    public void ScanFolder_ShouldSkipIgnoredFoldersRecursively()
    {
        // Arrange
        var workFolder = Path.Combine(_testDir, "Work");
        var gitFolder = Path.Combine(_testDir, ".git");
        var nodeFolder = Path.Combine(_testDir, "node_modules");

        Directory.CreateDirectory(workFolder);
        Directory.CreateDirectory(gitFolder);
        Directory.CreateDirectory(nodeFolder);

        File.WriteAllText(Path.Combine(workFolder, "presentation.pptx"), "Presentation data");
        File.WriteAllText(Path.Combine(gitFolder, "config"), "git config");
        File.WriteAllText(Path.Combine(nodeFolder, "package.json"), "{}");

        var filters = SyncFilterOptions.Create(
            includedExtensions: "",
            excludedExtensions: "",
            excludedFolders: "node_modules, .git",
            maxFileSizeMb: 50
        );

        // Act
        var results = _service.ScanFolder(_testDir, filters);

        // Assert
        Assert.Single(results);
        Assert.Equal("presentation.pptx", results[0].FileName);
        Assert.Equal(Path.Combine("Work", "presentation.pptx"), results[0].RelativePath);
    }

    [Fact]
    public void ScanFolder_ShouldFilterOutFilesExceedingMaxFileSize()
    {
        // Arrange
        var smallFile = Path.Combine(_testDir, "small.txt");
        var largeFile = Path.Combine(_testDir, "large.bin");

        File.WriteAllBytes(smallFile, new byte[1024]); // 1 KB
        File.WriteAllBytes(largeFile, new byte[3 * 1024 * 1024]); // 3 MB

        var filters = SyncFilterOptions.Create(
            includedExtensions: "",
            excludedExtensions: "",
            excludedFolders: "",
            maxFileSizeMb: 2 // 2 MB limit
        );

        // Act
        var results = _service.ScanFolder(_testDir, filters);

        // Assert
        Assert.Single(results);
        Assert.Equal("small.txt", results[0].FileName);
    }

    [Fact]
    public void ComputeSha256_ShouldBeConsistentAndDeterministic()
    {
        // Arrange
        var filePath = Path.Combine(_testDir, "test_sha.txt");
        File.WriteAllText(filePath, "Deterministic test content for WorkActivityPanel sync");

        // Act
        var hash1 = _service.ComputeSha256(filePath);
        var hash2 = _service.ComputeSha256(filePath);

        // Assert
        Assert.NotEmpty(hash1);
        Assert.Equal(64, hash1.Length);
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void UpdateSettings_ShouldUpdateServiceSettings()
    {
        // Arrange
        var newSettings = new DriveSyncSettings
        {
            LocalFolderPath = _testDir,
            WebAppUrl = "https://script.google.com/macros/s/test/exec",
            MaxFileSizeMb = 100
        };

        // Act
        _service.UpdateSettings(newSettings);

        // Assert
        Assert.Equal(_testDir, _service.Settings.LocalFolderPath);
        Assert.Equal("https://script.google.com/macros/s/test/exec", _service.Settings.WebAppUrl);
        Assert.Equal(100, _service.Settings.MaxFileSizeMb);
        Assert.True(_service.IsConfigured);
    }
}
