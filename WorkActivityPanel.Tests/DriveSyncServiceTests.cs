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

    [Theory]
    [InlineData("", "notes.md", "notes.md")]
    [InlineData(null, "sub\\notes.md", "sub/notes.md")]
    [InlineData("claude-md", "notes.md", "claude-md/notes.md")]
    [InlineData("/claude-md/", "sub\\notes.md", "claude-md/sub/notes.md")]
    public void CombineDestination_ShouldProduceForwardSlashPaths(string? prefix, string relativePath, string expected)
    {
        Assert.Equal(expected, DriveSyncService.CombineDestination(prefix, relativePath));
    }

    [Theory]
    [InlineData(@"C:\Users\me\.claude\CLAUDE.md", "claude-md/.claude/CLAUDE.md", "CLAUDE.md")]
    [InlineData(@"C:\docs\notes.md", "sub/notes.md", "notes.md")]
    [InlineData(@"C:\docs\notes.md", "", "notes.md")]
    public void ResolveUploadName_ShouldUseTheDestinationSegment(string filePath, string relativePath, string expected)
    {
        // The bridge builds folders from the leading segments and names the file after the
        // last one, so the name sent must be that segment and not the local file name.
        Assert.Equal(expected, DriveSyncService.ResolveUploadName(filePath, relativePath));
    }

    [Fact]
    public void HashKey_ShouldFallBackToTheAbsolutePath()
    {
        // Existing sync_hashes.json indexes are keyed by absolute path; the fallback is what
        // keeps them valid instead of re-uploading everything.
        var file = new LocalFileMetadata { FilePath = @"C:\folder\file.md" };

        Assert.Equal(@"C:\folder\file.md", file.HashKey);

        file.HashKey = "prefix|C:\\folder\\file.md";
        Assert.Equal("prefix|C:\\folder\\file.md", file.HashKey);
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
            Sources = { new SyncSource { LocalFolderPath = _testDir, DestinationPrefix = "trabajo" } },
            WebAppUrl = "https://script.google.com/macros/s/test/exec",
            MaxFileSizeMb = 100
        };

        // Act
        _service.UpdateSettings(newSettings);

        // Assert
        Assert.Equal(_testDir, Assert.Single(_service.Settings.Sources).LocalFolderPath);
        Assert.Equal("https://script.google.com/macros/s/test/exec", _service.Settings.WebAppUrl);
        Assert.Equal(100, _service.Settings.MaxFileSizeMb);
        Assert.True(_service.IsConfigured);
    }

    [Theory]
    [InlineData(@"C:\Users\me\Documentos\Trabajo", "", "Trabajo")]
    [InlineData(@"C:\Users\me\Documentos\Trabajo\", "", "Trabajo")]
    [InlineData(@"C:\Users\me\Documentos\Trabajo", "/respaldo/", "respaldo")]
    public void EffectiveDestinationPrefix_ShouldNeverBeEmpty(string localPath, string prefix, string expected)
    {
        // An empty destination would drop that source's files loose in the Drive root,
        // where they mix with every other source's instead of staying side by side.
        var source = new SyncSource { LocalFolderPath = localPath, DestinationPrefix = prefix };

        Assert.Equal(expected, source.EffectiveDestinationPrefix);
    }

    [Fact]
    public void LoadSettings_ShouldMigrateTheLegacyMainFolderIntoASource()
    {
        var json = """
            {"LocalFolderPath":"C:\\Users\\me\\Documentos\\Trabajo","WebAppUrl":"https://script.google.com/macros/s/test/exec"}
            """;
        LocalSettingsHelper.Set("DriveSyncSettings", json);

        using var service = new DriveSyncService(_scheduleMock.Object);

        var migrated = Assert.Single(service.Settings.Sources);
        Assert.Equal(@"C:\Users\me\Documentos\Trabajo", migrated.LocalFolderPath);
        Assert.Equal("Trabajo", migrated.EffectiveDestinationPrefix);
        Assert.Empty(service.Settings.LegacyMainFolderPath);
    }

    [Fact]
    public void CategorizeError_ShouldIdentifyLockedFiles()
    {
        var ioEx = new IOException("The process cannot access the file because it is being used by another process.");
        var (category, message) = DriveSyncService.CategorizeError(ioEx, "C:\\test\\doc.docx");

        Assert.Equal("Archivo en uso / Bloqueado", category);
        Assert.Contains("abierto en otra aplicación", message);
    }

    [Fact]
    public void CategorizeError_ShouldIdentifyRateLimitsAndTimeouts()
    {
        var ex429 = new Exception("Service invoked too many times for one day");
        var (category1, _) = DriveSyncService.CategorizeError(ex429, "C:\\test\\data.csv");
        Assert.Equal("Límite de Google Apps Script", category1);

        var timeoutEx = new TimeoutException("The operation timed out.");
        var (category2, _) = DriveSyncService.CategorizeError(timeoutEx, "C:\\test\\data.csv");
        Assert.Equal("Tiempo de espera agotado", category2);
    }

    [Fact]
    public void ClearSyncErrors_ShouldEmptyLastSyncErrors()
    {
        _service.ClearSyncErrors();
        Assert.Empty(_service.LastSyncErrors);
    }

    [Fact]
    public async Task RetryFailedFiles_ShouldReturnEarly_WhenNoErrorsExist()
    {
        _service.UpdateSettings(new DriveSyncSettings
        {
            WebAppUrl = "https://script.google.com/test",
            Sources = { new SyncSource { LocalFolderPath = _testDir } }
        });
        _service.ClearSyncErrors();
        var summary = await _service.RetryFailedFilesAsync();

        Assert.Contains("No hay archivos con error", summary.Message);
        Assert.Equal(0, summary.TotalScanned);
    }
}


