using System.IO;

namespace RoninDiskManager.Engine;

// ── File system helpers ─────────────────────────────────────────────────────
// Small, dependency-free, unit-testable utilities shared by the scan and
// search engines: reparse-point detection (junction/symlink loop guard),
// filename matching, long-path detection, and byte formatting.
public static class FileSystemHelpers
{
    // Legacy MAX_PATH. The classic shell APIs (e.g. Recycle Bin) cannot handle
    // paths at or beyond this without the extended-length "\\?\" prefix.
    public const int LegacyMaxPath = 260;

    /// <summary>
    /// True when the entry is a reparse point (junction, symbolic link, or mount
    /// point). Scanners must not follow these: doing so can double-count storage
    /// or spin forever on a self-referential loop.
    /// </summary>
    public static bool IsReparsePoint(FileSystemInfo info)
    {
        try
        {
            return (info.Attributes & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            // If attributes can't be read, treat it as a reparse point so we skip it.
            return true;
        }
    }

    /// <summary>
    /// Returns true when <paramref name="name"/> matches the search
    /// <paramref name="query"/>. Wildcard queries (containing * or ?) use simple
    /// expression matching; plain queries match as a case-insensitive substring.
    /// </summary>
    public static bool MatchesQuery(string name, string query, bool isWildcard)
    {
        if (string.IsNullOrEmpty(name)) return false;

        if (isWildcard)
            return System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(
                query, name, ignoreCase: true);

        return name.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Convenience overload that derives the wildcard flag from the query.</summary>
    public static bool MatchesQuery(string name, string query)
        => MatchesQuery(name, query, query.Contains('*') || query.Contains('?'));

    /// <summary>True when a path is long enough to need the extended-length prefix.</summary>
    public static bool ExceedsLegacyMaxPath(string path)
        => !string.IsNullOrEmpty(path) && path.Length >= LegacyMaxPath;

    /// <summary>
    /// Adds the "\\?\" extended-length prefix to a rooted local path when needed,
    /// so enumeration and IO APIs can address paths beyond MAX_PATH. Paths that
    /// already carry a device prefix, or UNC/relative paths, are returned as-is.
    /// </summary>
    public static string ToExtendedPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path;
        if (path.StartsWith(@"\\", StringComparison.Ordinal))   return path; // UNC — leave alone
        if (!Path.IsPathRooted(path)) return path;
        return @"\\?\" + path;
    }

    /// <summary>Human-readable byte size (KB / MB / GB), matching the app's display style.</summary>
    public static string FormatBytes(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
        >= 1_048_576     => $"{bytes / 1_048_576.0:F0} MB",
        _                => $"{bytes / 1024.0:F0} KB"
    };
}
