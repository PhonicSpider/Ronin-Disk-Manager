using System.IO;
using RoninDiskManager.Models;

namespace RoninDiskManager.Engine;

// ── Disk analysis ───────────────────────────────────────────────────────────
// Pure tree walks over a completed scan: aggregate space by file extension, and
// find empty folders. Both are dependency-free and unit-tested.
public static class DiskAnalysis
{
    /// <summary>Aggregate size and count of one file extension.</summary>
    public sealed record ExtensionStat
    {
        public string Extension { get; init; } = string.Empty;
        public int    FileCount { get; init; }
        public long   TotalBytes { get; init; }
        public string SizeDisplay => FileSystemHelpers.FormatBytes(TotalBytes);
    }

    /// <summary>
    /// Groups every file in the tree by extension (lower-cased, with a friendly
    /// label for extension-less files) and returns the groups sorted by size.
    /// </summary>
    public static List<ExtensionStat> ExtensionBreakdown(DiskNode root)
    {
        var byExt = new Dictionary<string, (int Count, long Bytes)>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in EnumerateFiles(root))
        {
            var ext = Path.GetExtension(file.Name);
            var key = string.IsNullOrEmpty(ext) ? "(no extension)" : ext.ToLowerInvariant();
            var cur = byExt.TryGetValue(key, out var v) ? v : (0, 0L);
            byExt[key] = (cur.Item1 + 1, cur.Item2 + file.SizeBytes);
        }

        return byExt
            .Select(kv => new ExtensionStat { Extension = kv.Key, FileCount = kv.Value.Count, TotalBytes = kv.Value.Bytes })
            .OrderByDescending(s => s.TotalBytes)
            .ToList();
    }

    /// <summary>
    /// Returns directories that contain no items at all (leaf empty folders),
    /// sorted by path. Recycling these is safe: they hold no file data.
    /// </summary>
    public static List<DiskNode> FindEmptyFolders(DiskNode root)
    {
        var empties = new List<DiskNode>();
        var stack = new Stack<DiskNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n.IsDirectory)
            {
                if (n.Children.Count == 0)
                    empties.Add(n);
                else
                    foreach (var c in n.Children) stack.Push(c);
            }
        }
        return empties.OrderBy(n => n.FullPath, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Iteratively enumerates every file (non-directory) node in the tree.</summary>
    public static IEnumerable<DiskNode> EnumerateFiles(DiskNode root)
    {
        var stack = new Stack<DiskNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (!n.IsDirectory)
            {
                yield return n;
            }
            else
            {
                foreach (var c in n.Children) stack.Push(c);
            }
        }
    }
}
