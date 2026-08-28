using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WorkActivityPanel.Services;

/// <summary>
/// Finds agent instruction and context files (CLAUDE.md, .claude/**/*.md, references/*.md)
/// under the root path that no git repository is tracking.
///
/// Only untracked or ignored files are backed up: a tracked file already lives in its
/// repository's git history. Untracked ones exist on this machine only.
/// </summary>
public sealed class ClaudeConfigDiscovery
{
    public const string InstructionFileName = "CLAUDE.md";

    /// <summary>
    /// Directories never worth walking into: build output, dependencies, and caches.
    /// </summary>
    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", ".vs", "bin", "obj", "venv", ".venv", "__pycache__",
        "AppData", "dist", "build", ".obsidian", ".trash", ".idea"
    };

    /// <summary>
    /// Keywords in filenames that indicate secrets or credentials which must never be uploaded.
    /// </summary>
    private static readonly string[] SensitiveNameKeywords =
    {
        "id_rsa", "id_ed25519", "credentials", "auth_token", "api_key"
    };

    /// <summary>
    /// Regex signatures of high-risk live infrastructure secrets (SSH private keys, AWS access keys, GitHub PATs).
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex[] InfrastructureSecretPatterns =
    {
        new(@"-----BEGIN\s+[A-Z\s]+PRIVATE\s+KEY-----", System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase),
        new(@"\bAKIA[0-9A-Z]{16}\b", System.Text.RegularExpressions.RegexOptions.Compiled),
        new(@"\bghp_[A-Za-z0-9_]{36}\b", System.Text.RegularExpressions.RegexOptions.Compiled),
        new(@"\bxox[baprs]-[0-9a-zA-Z]{10,48}\b", System.Text.RegularExpressions.RegexOptions.Compiled)
    };

    private readonly string _gitExecutable;

    public ClaudeConfigDiscovery(string gitExecutable = "git")
    {
        _gitExecutable = gitExecutable;
    }

    /// <summary>
    /// Walks <paramref name="rootPath"/> up to <paramref name="maxDepth"/> levels and
    /// returns the absolute paths of instruction and reference files that git does not track.
    /// </summary>
    public async Task<List<string>> FindUnversionedAsync(
        string rootPath,
        int maxDepth,
        CancellationToken cancellationToken = default)
    {
        var found = new List<string>();
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            return found;

        var candidates = EnumerateCandidates(rootPath, Math.Max(1, maxDepth)).ToList();
        if (candidates.Count == 0)
            return found;

        // Group candidate files by their Git repository root (null if outside any Git repository)
        var repoRootCache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var repoGroups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var outsideRepoFiles = new List<string>();

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var directory = Path.GetDirectoryName(candidate);
            if (string.IsNullOrEmpty(directory))
            {
                outsideRepoFiles.Add(candidate);
                continue;
            }

            if (!repoRootCache.TryGetValue(directory, out var repoRoot))
            {
                repoRoot = await GetGitRepoRootAsync(directory, cancellationToken);
                repoRootCache[directory] = repoRoot;
            }

            if (string.IsNullOrEmpty(repoRoot))
            {
                outsideRepoFiles.Add(candidate);
            }
            else
            {
                if (!repoGroups.TryGetValue(repoRoot, out var list))
                {
                    list = new List<string>();
                    repoGroups[repoRoot] = list;
                }
                list.Add(candidate);
            }
        }

        // Candidates outside any git repository are always unversioned
        found.AddRange(outsideRepoFiles);

        // For candidates inside git repositories, batch query git ls-files
        foreach (var (repoRoot, fileList) in repoGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in fileList)
            {
                try
                {
                    var rel = Path.GetRelativePath(repoRoot, file);
                    relMap[rel] = file;
                }
                catch
                {
                    // If cross-drive or relative path error, treat as unversioned
                    found.Add(file);
                }
            }

            var trackedFiles = await GetTrackedFilesAsync(repoRoot, relMap.Keys, cancellationToken);

            foreach (var (relPath, fullPath) in relMap)
            {
                if (!trackedFiles.Contains(relPath))
                {
                    found.Add(fullPath);
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Breadth-first walk that yields every candidate instruction and reference file within the depth limit.
    /// </summary>
    public static IEnumerable<string> EnumerateCandidates(string rootPath, int maxDepth)
    {
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((rootPath, 0));
        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (queue.Count > 0)
        {
            var (currentDir, depth) = queue.Dequeue();

            // 1. Direct CLAUDE.md in current directory
            var mainInstruction = Path.Combine(currentDir, InstructionFileName);
            if (File.Exists(mainInstruction) && IsCandidateAllowed(mainInstruction) && yielded.Add(mainInstruction))
            {
                yield return mainInstruction;
            }

            // 2. Direct references/ directory under currentDir
            var directReferencesDir = Path.Combine(currentDir, "references");
            if (Directory.Exists(directReferencesDir))
            {
                foreach (var file in SafeEnumerateFiles(directReferencesDir, "*.md"))
                {
                    if (IsCandidateAllowed(file) && yielded.Add(file))
                        yield return file;
                }
            }

            // 3. .claude/ folder under currentDir (including .claude/references/ and subfolders)
            var claudeDir = Path.Combine(currentDir, ".claude");
            if (Directory.Exists(claudeDir))
            {
                foreach (var file in SafeEnumerateFilesRecursive(claudeDir, 3))
                {
                    if (IsCandidateAllowed(file) && yielded.Add(file))
                        yield return file;
                }
            }

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
                var dirInfo = new DirectoryInfo(subDirectory);
                if (dirInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    continue;

                var name = dirInfo.Name;
                if (!IsDirectorySkipped(name))
                    queue.Enqueue((subDirectory, depth + 1));
            }
        }
    }

    /// <summary>
    /// Checks whether a directory should be skipped during recursive traversal.
    /// </summary>
    public static bool IsDirectorySkipped(string dirName)
    {
        if (SkippedDirectories.Contains(dirName))
            return true;

        if (dirName.StartsWith("_backup_", StringComparison.OrdinalIgnoreCase) ||
            dirName.StartsWith("backup_", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>
    /// Validates that a file is a safe candidate for syncing (valid extension and not sensitive).
    /// </summary>
    public static bool IsCandidateAllowed(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        if (!string.Equals(ext, ".md", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(ext, ".txt", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fileName = Path.GetFileName(filePath);
        foreach (var keyword in SensitiveNameKeywords)
        {
            if (fileName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (HasInfrastructureSecret(filePath))
            return false;

        return true;
    }

    /// <summary>
    /// Checks the contents of a file for high-risk infrastructure secret signatures (SSH private keys, AWS keys, GitHub PATs).
    /// </summary>
    public static bool HasInfrastructureSecret(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return false;

            // Read up to 64 KB to efficiently scan without loading unbounded files
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var buffer = new char[65536];
            int read = reader.Read(buffer, 0, buffer.Length);
            if (read <= 0)
                return false;

            var content = new string(buffer, 0, read);
            foreach (var pattern in InfrastructureSecretPatterns)
            {
                if (pattern.IsMatch(content))
                    return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string dirPath, string searchPattern)
    {
        try
        {
            return Directory.GetFiles(dirPath, searchPattern, SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return Enumerable.Empty<string>();
        }
    }

    private static IEnumerable<string> SafeEnumerateFilesRecursive(string rootDir, int maxDepth)
    {
        var results = new List<string>();
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((rootDir, 0));

        while (queue.Count > 0)
        {
            var (current, depth) = queue.Dequeue();

            try
            {
                foreach (var file in Directory.GetFiles(current))
                {
                    results.Add(file);
                }
            }
            catch { }

            if (depth >= maxDepth)
                continue;

            try
            {
                foreach (var sub in Directory.GetDirectories(current))
                {
                    var dirInfo = new DirectoryInfo(sub);
                    if (!dirInfo.Attributes.HasFlag(FileAttributes.ReparsePoint) && !IsDirectorySkipped(dirInfo.Name))
                    {
                        queue.Enqueue((sub, depth + 1));
                    }
                }
            }
            catch { }
        }

        return results;
    }

    /// <summary>
    /// Discovers the Git repository top-level root directory for a given directory, if any.
    /// </summary>
    public async Task<string?> GetGitRepoRootAsync(string directory, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _gitExecutable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(directory);
        startInfo.ArgumentList.Add("rev-parse");
        startInfo.ArgumentList.Add("--show-toplevel");

        try
        {
            using var process = Process.Start(startInfo);
            if (process == null) return null;

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(cancellationToken);
            var stdout = await stdoutTask;
            await stderrTask;

            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
            {
                var repoRoot = stdout.Trim().Replace('/', Path.DirectorySeparatorChar);
                return Directory.Exists(repoRoot) ? repoRoot : Path.GetFullPath(repoRoot);
            }
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Executes batched `git ls-files` queries to determine which files are tracked by Git.
    /// </summary>
    public async Task<HashSet<string>> GetTrackedFilesAsync(
        string repoRoot,
        IEnumerable<string> relativeFilePaths,
        CancellationToken cancellationToken)
    {
        var tracked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fileList = relativeFilePaths.ToList();
        if (fileList.Count == 0)
            return tracked;

        const int batchSize = 50;
        for (int i = 0; i < fileList.Count; i += batchSize)
        {
            var batch = fileList.Skip(i).Take(batchSize).ToList();
            var startInfo = new ProcessStartInfo
            {
                FileName = _gitExecutable,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-C");
            startInfo.ArgumentList.Add(repoRoot);
            startInfo.ArgumentList.Add("ls-files");
            startInfo.ArgumentList.Add("--");
            foreach (var relPath in batch)
            {
                startInfo.ArgumentList.Add(relPath.Replace('\\', '/'));
            }

            try
            {
                using var process = Process.Start(startInfo);
                if (process == null) continue;

                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync(cancellationToken);
                var stdout = await stdoutTask;
                await stderrTask;

                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
                {
                    using var reader = new StringReader(stdout);
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            var normalized = line.Trim().Replace('/', Path.DirectorySeparatorChar);
                            tracked.Add(normalized);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // If git fails, treat files as untracked
            }
        }

        return tracked;
    }
}
