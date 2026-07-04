using System.IO;
using RoninDiskManager.Models;

namespace RoninDiskManager.Engine;

// ── Search Engine ─────────────────────────────────────────────────────────────
// Performs a system-wide file/folder search across all fixed NTFS (and other
// fixed) drives by recursively walking every directory with
// DirectoryInfo.EnumerateFileSystemInfos.  Wildcards (* and ?) in the query
// are translated into a standard .NET filename pattern match; a plain string
// (no wildcards) is treated as a substring match so partial names work.
//
// Each drive is walked on a separate Task (parallel).  UnauthorizedAccessException
// and other IO errors are silently skipped per-directory so the search degrades
// gracefully without administrator rights.  Progress messages and cancellation
// are supported throughout.
public sealed class SearchEngine
{
    // ── Public entry point ────────────────────────────────────────────────────

    /// <summary>
    /// Search all fixed drives for items whose name matches <paramref name="query"/>.
    /// Results are accumulated into the returned list; call sites should display
    /// the list (or an ObservableCollection wrapping it) in the UI.
    /// </summary>
    /// <param name="query">
    /// A filename pattern.  Supports * and ? wildcards.  A plain string without
    /// wildcards is matched as a case-insensitive substring.
    /// </param>
    /// <param name="progress">
    /// Optional progress sink — receives short status strings suitable for a
    /// one-line status bar (e.g. "Searching C:\Windows\System32…").
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>All matching <see cref="SearchResult"/> items found across drives.</returns>
    public async Task<List<SearchResult>> SearchAsync(
        string            query,
        IProgress<string>? progress = null,
        CancellationToken ct        = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        // Normalise query once
        bool isWildcard = query.Contains('*') || query.Contains('?');
        string normQuery = query.Trim();

        // Collect fixed drives (NTFS and others alike — skip network/optical/RAM)
        var drives = DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .ToList();

        if (drives.Count == 0)
            return [];

        // Launch one task per drive, then collect all results
        var driveTasks = drives.Select(drive =>
            Task.Run(() => SearchDrive(drive, normQuery, isWildcard, progress, ct), ct)
        ).ToList();

        var resultBuckets = await Task.WhenAll(driveTasks);
        return [.. resultBuckets.SelectMany(r => r)];
    }

    // ── Per-drive walk ────────────────────────────────────────────────────────

    private static List<SearchResult> SearchDrive(
        DriveInfo         drive,
        string            query,
        bool              isWildcard,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var hits = new List<SearchResult>();
        var stack = new Stack<DirectoryInfo>();

        try
        {
            stack.Push(new DirectoryInfo(drive.RootDirectory.FullName));
        }
        catch
        {
            return hits;
        }

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            var dir = stack.Pop();

            // Report progress for top-level directories to keep status meaningful
            //  without flooding the UI with every subdirectory
            if (dir.Parent?.Parent == null)   // depth 0 or 1
                progress?.Report($"Searching {dir.FullName}");

            // ── Enumerate entries in this directory ───────────────────────────
            IEnumerable<FileSystemInfo> entries;
            try
            {
                entries = dir.EnumerateFileSystemInfos("*", SearchOption.TopDirectoryOnly);
            }
            catch (UnauthorizedAccessException) { continue; }
            catch (IOException)                 { continue; }
            catch (Exception)                   { continue; }

            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();

                // Queue subdirectories for traversal, but never follow reparse
                // points (junctions / symlinks) — they can loop or double-count.
                if (entry is DirectoryInfo subDir && !FileSystemHelpers.IsReparsePoint(subDir))
                {
                    try { stack.Push(subDir); }
                    catch { /* skip inaccessible directories */ }
                }

                // ── Name matching ─────────────────────────────────────────────
                if (!FileSystemHelpers.MatchesQuery(entry.Name, query, isWildcard))
                    continue;

                // ── Build SearchResult ────────────────────────────────────────
                bool isDir    = entry is DirectoryInfo;
                long size     = 0;
                DateTime modified = DateTime.MinValue;

                try
                {
                    modified = entry.LastWriteTime;
                    if (!isDir && entry is FileInfo fi)
                        size = fi.Length;
                }
                catch { /* metadata unavailable — leave defaults */ }

                hits.Add(new SearchResult
                {
                    Name         = entry.Name,
                    FullPath     = entry.FullName,
                    DriveRoot    = drive.RootDirectory.FullName,
                    IsDirectory  = isDir,
                    SizeBytes    = size,
                    DateModified = modified,
                    FileType     = ResolveFileType(entry.Name, isDir)
                });
            }
        }

        return hits;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Derives a human-readable type string consistent with Windows Explorer:
    /// "File Folder", "PNG File", "File", etc.
    /// </summary>
    private static string ResolveFileType(string name, bool isDirectory)
    {
        if (isDirectory)
            return "File Folder";

        var ext = Path.GetExtension(name);
        return string.IsNullOrEmpty(ext)
            ? "File"
            : ext.TrimStart('.').ToUpperInvariant() + " File";
    }
}
