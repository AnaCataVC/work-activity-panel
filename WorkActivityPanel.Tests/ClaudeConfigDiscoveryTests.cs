using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WorkActivityPanel.Services;
using Xunit;

namespace WorkActivityPanel.Tests;

public class ClaudeConfigDiscoveryTests : IDisposable
{
    private readonly string _root;
    private readonly ClaudeConfigDiscovery _discovery = new();

    public ClaudeConfigDiscoveryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "WorkActivityPanel_ClaudeDiscovery_" + Guid.NewGuid());
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            try { Directory.Delete(_root, true); } catch { }
        }
    }

    private string WriteInstructionFile(params string[] relativeDirectories)
    {
        var directory = Path.Combine(new[] { _root }.Concat(relativeDirectories).ToArray());
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, ClaudeConfigDiscovery.InstructionFileName);
        File.WriteAllText(path, "# instructions");
        return path;
    }

    [Fact]
    public async Task FindsNestedFilesOutsideAnyRepository()
    {
        var atRoot = WriteInstructionFile();
        var nested = WriteInstructionFile("projects", "alpha");

        var found = await _discovery.FindUnversionedAsync(_root, maxDepth: 4);

        Assert.Contains(atRoot, found);
        Assert.Contains(nested, found);
    }

    [Fact]
    public async Task SkipsDependencyAndBuildDirectories()
    {
        WriteInstructionFile("node_modules");
        WriteInstructionFile("obj");
        var kept = WriteInstructionFile("docs");

        var found = await _discovery.FindUnversionedAsync(_root, maxDepth: 4);

        Assert.Equal(new[] { kept }, found);
    }

    [Fact]
    public async Task StopsAtTheDepthLimit()
    {
        WriteInstructionFile("one", "two", "three");

        var found = await _discovery.FindUnversionedAsync(_root, maxDepth: 2);

        Assert.Empty(found);
    }

    [Fact]
    public async Task ReturnsNothingWhenTheRootDoesNotExist()
    {
        var found = await _discovery.FindUnversionedAsync(Path.Combine(_root, "missing"), maxDepth: 3);

        Assert.Empty(found);
    }

    [Fact]
    public async Task TreatsFilesAsUnversionedWhenGitCannotBeRun()
    {
        var path = WriteInstructionFile("no-git-here");
        var discovery = new ClaudeConfigDiscovery("git-that-does-not-exist");

        var found = await discovery.FindUnversionedAsync(_root, maxDepth: 3);

        Assert.Contains(path, found);
    }

    [Fact]
    public async Task FindsClaudeFolderAndReferencesFiles()
    {
        var claudeRef = WriteCustomFile("# team", ".claude", "references", "team_roster.md");
        var directRef = WriteCustomFile("# geocoding ref", "geocoding", "references", "spec.md");
        var rootClaude = WriteInstructionFile();

        var found = await _discovery.FindUnversionedAsync(_root, maxDepth: 4);

        Assert.Contains(claudeRef, found);
        Assert.Contains(directRef, found);
        Assert.Contains(rootClaude, found);
    }

    [Fact]
    public async Task SkipsBackupAndSensitiveFiles()
    {
        var backupFile = WriteCustomFile("# backup", "_backup_claudemd_20260828", "CLAUDE.md");
        var sensitiveFile = WriteCustomFile("api_key=123", ".claude", "api_key.md");
        var sshKeyFile = WriteCustomFile("-----BEGIN RSA PRIVATE KEY-----\nMIIEowIBAAKCAQEA...\n-----END RSA PRIVATE KEY-----", "references", "id_rsa.md");
        var awsKeyFile = WriteCustomFile("export AWS_ACCESS_KEY_ID=AKIAIOSFODNN7EXAMPLE", "references", "aws_setup.md");
        var sandboxDoc = WriteCustomFile("Token cuenta de pruebas 95542: `343f05ff9c8ad7a2473500e2d33f61dc5b81cb40`", "geocoding", "references", "cuenta-pruebas-95542.md");
        var validFile = WriteCustomFile("# valid context", ".claude", "references", "architecture.md");

        var found = await _discovery.FindUnversionedAsync(_root, maxDepth: 4);

        Assert.DoesNotContain(backupFile, found);
        Assert.DoesNotContain(sensitiveFile, found);
        Assert.DoesNotContain(sshKeyFile, found);
        Assert.DoesNotContain(awsKeyFile, found);
        Assert.Contains(sandboxDoc, found);
        Assert.Contains(validFile, found);
    }

    private string WriteCustomFile(string content, params string[] relativeSegments)
    {
        var fullPath = Path.Combine(new[] { _root }.Concat(relativeSegments).ToArray());
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }
}
