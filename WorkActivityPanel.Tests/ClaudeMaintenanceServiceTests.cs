using System;
using System.IO;
using System.Threading.Tasks;
using WorkActivityPanel.Helpers;
using WorkActivityPanel.Models;
using WorkActivityPanel.Services;
using Xunit;

namespace WorkActivityPanel.Tests;

public class ClaudeMaintenanceServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _transcriptsRoot;
    private readonly string _sessionsRoot;
    private readonly ClaudeMaintenanceService _service;

    public ClaudeMaintenanceServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "WorkActivityPanel_ClaudeMaintTests_" + Guid.NewGuid());
        _transcriptsRoot = Path.Combine(_testDir, "projects");
        _sessionsRoot = Path.Combine(_testDir, "claude-code-sessions");
        Directory.CreateDirectory(_transcriptsRoot);
        Directory.CreateDirectory(_sessionsRoot);

        LocalSettingsHelper.SettingsFilePath = Path.Combine(_testDir, "test_settings.json");

        // Claude closed by default, so the archive guard does not depend on what happens to be
        // running on the machine executing the suite.
        _service = new ClaudeMaintenanceService(_transcriptsRoot, _sessionsRoot, () => false);
    }

    public void Dispose()
    {
        LocalSettingsHelper.ResetToDefaultPath();
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, true); } catch { }
        }
    }

    private string WriteTranscript(string name, int ageInDays, string content = "{}")
    {
        // Transcripts live one directory below the root, mirroring the per-project layout.
        var projectDir = Path.Combine(_transcriptsRoot, "C--some-project");
        Directory.CreateDirectory(projectDir);

        var path = Path.Combine(projectDir, name);
        File.WriteAllText(path, content);
        File.SetLastWriteTime(path, DateTime.Now.AddDays(-ageInDays));
        return path;
    }

    private string WriteSession(string name, int ageInDays, bool archived)
    {
        var path = Path.Combine(_sessionsRoot, name);
        var flag = archived ? "true" : "false";
        File.WriteAllText(path, "{\"sessionId\":\"s1\",\"cwd\":\"D:\\\\work\",\"isArchived\":" + flag + ",\"transcript\":\"body\"}");
        File.SetLastWriteTime(path, DateTime.Now.AddDays(-ageInDays));
        return path;
    }

    [Fact]
    public async Task ScanAsync_SeparatesStaleFilesFromTheTotal()
    {
        _service.UpdateSettings(new ClaudeMaintenanceSettings { TranscriptRetentionDays = 30 });
        WriteTranscript("old.jsonl", ageInDays: 90);
        WriteTranscript("recent.jsonl", ageInDays: 1);

        var report = await _service.ScanAsync();

        Assert.Equal(2, report.Transcripts.TotalFiles);
        Assert.Equal(1, report.Transcripts.StaleFiles);
    }

    [Fact]
    public async Task ScanAsync_ReportsAMissingStoreInsteadOfThrowing()
    {
        // A machine without Claude Desktop installed still has to render the panel.
        var service = new ClaudeMaintenanceService(
            Path.Combine(_testDir, "does-not-exist"),
            Path.Combine(_testDir, "also-missing"));

        var report = await service.ScanAsync();

        Assert.False(report.Transcripts.Exists);
        Assert.Equal(0, report.Transcripts.TotalFiles);
    }

    [Fact]
    public async Task DeleteStaleTranscriptsAsync_KeepsFilesInsideTheRetention()
    {
        _service.UpdateSettings(new ClaudeMaintenanceSettings { TranscriptRetentionDays = 30 });
        var stale = WriteTranscript("old.jsonl", ageInDays: 90);
        var fresh = WriteTranscript("recent.jsonl", ageInDays: 2);

        var result = await _service.DeleteStaleTranscriptsAsync();

        Assert.Equal(1, result.FilesProcessed);
        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(fresh));
    }

    [Fact]
    public async Task DeleteStaleTranscriptsAsync_KeepsARecentlyWrittenFileEvenWithZeroRetention()
    {
        // A retention of zero would otherwise sweep the session the user is running right now.
        _service.UpdateSettings(new ClaudeMaintenanceSettings { TranscriptRetentionDays = 0 });
        var live = WriteTranscript("live.jsonl", ageInDays: 0);

        var result = await _service.DeleteStaleTranscriptsAsync();

        Assert.Equal(0, result.FilesProcessed);
        Assert.True(File.Exists(live));
    }

    [Fact]
    public async Task DeleteStaleTranscriptsAsync_ReportsFreedBytes()
    {
        _service.UpdateSettings(new ClaudeMaintenanceSettings { TranscriptRetentionDays = 7 });
        var content = new string('x', 2048);
        WriteTranscript("old.jsonl", ageInDays: 30, content: content);

        var result = await _service.DeleteStaleTranscriptsAsync();

        Assert.Equal(2048, result.BytesFreed);
    }

    [Fact]
    public async Task ArchiveStaleSessionsAsync_FlipsTheFlagOnlyOnStaleUnarchivedSessions()
    {
        _service.UpdateSettings(new ClaudeMaintenanceSettings { SessionRetentionDays = 7 });
        var stale = WriteSession("stale.json", ageInDays: 30, archived: false);
        var expectedLastWrite = File.GetLastWriteTime(stale);
        var fresh = WriteSession("fresh.json", ageInDays: 1, archived: false);

        var result = await _service.ArchiveStaleSessionsAsync();

        Assert.Equal(1, result.FilesProcessed);
        Assert.Contains("\"isArchived\":true", File.ReadAllText(stale));
        Assert.Contains("\"isArchived\":false", File.ReadAllText(fresh));
        Assert.Equal(expectedLastWrite, File.GetLastWriteTime(stale));
    }

    [Fact]
    public async Task ArchiveStaleSessionsAsync_LeavesAlreadyArchivedSessionsUntouched()
    {
        _service.UpdateSettings(new ClaudeMaintenanceSettings { SessionRetentionDays = 7 });
        WriteSession("already.json", ageInDays: 30, archived: true);

        var result = await _service.ArchiveStaleSessionsAsync();

        Assert.Equal(0, result.FilesProcessed);
    }

    [Fact]
    public async Task ArchiveStaleSessionsAsync_RefusesWhileClaudeIsRunning()
    {
        // Claude holds these sessions in memory and rewrites the files, so a flag flipped
        // behind its back is silently undone. Refusing is the only correct outcome.
        var service = new ClaudeMaintenanceService(_transcriptsRoot, _sessionsRoot, () => true);
        service.UpdateSettings(new ClaudeMaintenanceSettings { SessionRetentionDays = 7 });
        var stale = WriteSession("stale.json", ageInDays: 30, archived: false);

        var result = await service.ArchiveStaleSessionsAsync();

        Assert.True(result.Skipped);
        Assert.Equal(0, result.FilesProcessed);
        Assert.Contains("\"isArchived\":false", File.ReadAllText(stale));
    }

    [Fact]
    public async Task ArchiveStaleSessionsAsync_FlipsTheFlag_WhenJsonHasWhitespace()
    {
        _service.UpdateSettings(new ClaudeMaintenanceSettings { SessionRetentionDays = 7 });
        var path = Path.Combine(_sessionsRoot, "whitespace.json");
        File.WriteAllText(path, "{\"sessionId\":\"s1\", \"cwd\": \"D:\\\\work\", \"isArchived\": false, \"transcript\": \"body\"}");
        File.SetLastWriteTime(path, DateTime.Now.AddDays(-30));

        var result = await _service.ArchiveStaleSessionsAsync();

        Assert.Equal(1, result.FilesProcessed);
        Assert.Contains("\"isArchived\":true", File.ReadAllText(path));
    }

    [Fact]
    public void ClaudeStoreReport_Summary_AdaptsForSessionsVsTranscripts()
    {
        var transcriptsStore = new ClaudeStoreReport
        {
            DisplayName = "Transcripts",
            Exists = true,
            TotalFiles = 10,
            TotalBytes = 1024 * 1024,
            StaleFiles = 3,
            StaleBytes = 512 * 1024,
            ReclaimsDiskSpace = true
        };

        var sessionsStore = new ClaudeStoreReport
        {
            DisplayName = "Sesiones",
            Exists = true,
            TotalFiles = 10,
            TotalBytes = 1024 * 1024,
            StaleFiles = 3,
            StaleBytes = 512 * 1024,
            ReclaimsDiskSpace = false
        };

        Assert.Contains("recuperables", transcriptsStore.Summary);
        Assert.DoesNotContain("recuperables", sessionsStore.Summary);
        Assert.Contains("fuera de retención", sessionsStore.Summary);
    }

    [Theory]
    [InlineData(512L, "512 B")]
    [InlineData(2048L, "2,0 KB")]
    public void FormatBytes_ScalesTheUnit(long bytes, string expectedFragment)
    {
        // The separator depends on the machine culture; assert on the unit, not the glyph.
        var formatted = ClaudeStoreReport.FormatBytes(bytes);

        Assert.EndsWith(expectedFragment.Split(' ')[1], formatted);
    }
}
