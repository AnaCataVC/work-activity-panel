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
}
