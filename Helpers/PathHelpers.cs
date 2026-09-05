using System.IO;
using System.Linq;

namespace WorkActivityPanel.Helpers;

/// <summary>
/// Filesystem lookup helpers shared by services that probe several well-known
/// install locations for an executable or config file.
/// </summary>
public static class PathHelpers
{
    /// <summary>Returns the first path in <paramref name="candidates"/> that exists on disk, or null if none do.</summary>
    public static string? FindFirstExisting(params string[] candidates) => candidates.FirstOrDefault(File.Exists);
}
