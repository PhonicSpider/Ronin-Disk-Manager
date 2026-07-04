using System.IO;
using System.Security.Cryptography;
using RoninDiskManager.Models;

namespace RoninDiskManager.Engine;

// ── Duplicate finder ────────────────────────────────────────────────────────
// Finds byte-for-byte duplicate files in a scanned tree. Files are first grouped
// by size (a cheap, exact pre-filter — different sizes can never be equal), then
// only same-size candidates are hashed with SHA-256 to confirm equality.
public static class DuplicateFinder
{
    /// <summary>A set of files that are byte-for-byte identical.</summary>
    public sealed record DuplicateGroup
    {
        public long SizeBytes { get; init; }
        public string Hash { get; init; } = string.Empty;
        public List<DiskNode> Files { get; init; } = [];
        public int Count => Files.Count;
        /// <summary>Space reclaimable if all but one copy were removed.</summary>
        public long WastedBytes => SizeBytes * (Count - 1);
    }

    /// <summary>
    /// Pure pre-filter: groups files with size &gt; 0 by exact size and returns only
    /// the groups that have more than one file (the duplicate candidates).
    /// </summary>
    public static List<List<DiskNode>> GroupBySizeCandidates(IEnumerable<DiskNode> files)
        => files
            .Where(f => !f.IsDirectory && f.SizeBytes > 0)
            .GroupBy(f => f.SizeBytes)
            .Where(g => g.Count() > 1)
            .Select(g => g.ToList())
            .ToList();

    /// <summary>
    /// Finds confirmed duplicate groups under <paramref name="root"/>. Reports
    /// progress as files are hashed and honors cancellation. Files that cannot be
    /// read are skipped.
    /// </summary>
    public static Task<List<DuplicateGroup>> FindDuplicatesAsync(
        DiskNode root,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
        => Task.Run(() =>
        {
            var candidates = GroupBySizeCandidates(DiskAnalysis.EnumerateFiles(root));
            int total = candidates.Sum(g => g.Count);
            int hashed = 0;
            var groups = new List<DuplicateGroup>();

            foreach (var sizeGroup in candidates)
            {
                ct.ThrowIfCancellationRequested();

                var byHash = new Dictionary<string, List<DiskNode>>();
                foreach (var file in sizeGroup)
                {
                    ct.ThrowIfCancellationRequested();
                    var hash = TryHash(file.FullPath);
                    hashed++;
                    if (hash == null) continue;

                    if (!byHash.TryGetValue(hash, out var list))
                        byHash[hash] = list = [];
                    list.Add(file);

                    if (hashed % 200 == 0)
                        progress?.Report($"Hashing {hashed:N0} / {total:N0} candidate files");
                }

                foreach (var kv in byHash.Where(kv => kv.Value.Count > 1))
                    groups.Add(new DuplicateGroup
                    {
                        SizeBytes = kv.Value[0].SizeBytes,
                        Hash = kv.Key,
                        Files = kv.Value
                    });
            }

            return groups.OrderByDescending(g => g.WastedBytes).ToList();
        }, ct);

    private static string? TryHash(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch
        {
            return null; // locked / inaccessible files are skipped
        }
    }
}
