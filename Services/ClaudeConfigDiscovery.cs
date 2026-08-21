using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace WorkActivityPanel.Services;

/// <summary>
/// Finds the agent instruction files (CLAUDE.md) under the user profile that no git
/// repository is tracking.
///
/// Only the untracked ones are worth backing up: a tracked file already lives in its
/// repository's history, so copying it elsewhere just creates a second copy that will
/// silently disagree with the first one. The untracked ones exist on this machine only.
/// </summary>
public sealed class ClaudeConfigDiscovery
{
    public const string InstructionFileName = "CLAUDE.md";

    /// <summary>
    /// Directories never worth walking into: build output and dependency trees hold no
    /// hand-written instruction files and are the bulk of the walking cost.
    /// </summary>
    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", ".vs", "bin", "obj", "venv", ".venv", "__pycache__", "AppData"
    };

    private readonly string _gitExecutable;

    public ClaudeConfigDiscovery(string gitExecutable = "git")
    {
        _gitExecutable = gitExecutable;
    }

    /// <summary>
    /// Walks <paramref name="rootPath"/> up to <paramref name="maxDepth"/> levels and
    /// returns the absolute paths of the instruction files that git does not track.
    /// </summary>
    public async Task<List<string>> FindUnversionedAsync(
        string rootPath,
        int maxDepth,
        CancellationToken cancellationToken = default)
    {
        var found = new List<string>();
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            return found;

        foreach (var candidate in EnumerateCandidates(rootPath, Math.Max(1, maxDepth)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var directory = Path.GetDirectoryName(candidate);
            if (string.IsNullOrEmpty(directory))
                continue;

            if (!await IsTrackedByGitAsync(directory, candidate, cancellationToken))
                found.Add(candidate);
        }

        return found;
    }

    /// <summary>
    /// Breadth-first walk that yields every instruction file within the depth limit.
    /// Directories that cannot be read are skipped rather than aborting the walk: a
    /// single permission error must not cost the whole sweep.
    /// </summary>
    private static IEnumerable<string> EnumerateCandidates(string rootPath, int maxDepth)
    {
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((rootPath, 0));

        while (queue.Count > 0)
        {
            var (currentDir, depth) = queue.Dequeue();

            var candidate = Path.Combine(currentDir, InstructionFileName);
            if (File.Exists(candidate))
                yield return candidate;

            if (depth >= maxDepth)
                continue;

            string[] subDirectories;
            try
            {
                subDirectories = Directory.GetDirectories(currentDir);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var subDirectory in subDirectories)
            {
                var name = new DirectoryInfo(subDirectory).Name;
                if (!SkippedDirectories.Contains(name))
                    queue.Enqueue((subDirectory, depth + 1));
            }
        }
    }

    /// <summary>
    /// Asks git whether the file is tracked. The exit code is the answer: 0 means tracked,
    /// anything else means it is not, which also covers the common case of the directory
    /// not being a repository at all.
    ///
    /// When git cannot be run, the file is reported as untracked. Backing up a file that a
    /// repository already holds costs a duplicate; skipping one that nothing holds loses
    /// it, so the uncertain case errs toward keeping the data.
    /// </summary>
    private async Task<bool> IsTrackedByGitAsync(
        string directory,
        string filePath,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _gitExecutable,
            // The path is passed absolute on purpose: git resolves a relative one against
            // the directory given to -C, which silently reports every file as untracked.
            Arguments = $"-C \"{directory}\" ls-files --error-unmatch \"{filePath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process == null)
                return false;

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(stdout, stderr);

            return process.ExitCode == 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
