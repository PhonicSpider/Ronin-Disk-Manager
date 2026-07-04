using RoninDiskManager.Engine;
using RoninDiskManager.Models;
using Xunit;

namespace RoninDiskManager.Tests;

public class AnalysisTests
{
    // ── Small tree builders ──────────────────────────────────────────────────

    private static DiskNode File(string name, long size, DateTime? modified = null) => new()
    {
        Name = name,
        FullPath = @"C:\" + name,
        IsDirectory = false,
        SizeBytes = size,
        LastWriteUtc = modified ?? default
    };

    private static DiskNode Dir(string name, params DiskNode[] children)
    {
        var d = new DiskNode { Name = name, FullPath = @"C:\" + name, IsDirectory = true };
        foreach (var c in children) { c.Parent = d; d.Children.Add(c); }
        return d;
    }

    // ── Extension breakdown ──────────────────────────────────────────────────

    [Fact]
    public void ExtensionBreakdown_GroupsAndSortsBySize()
    {
        var root = Dir("root",
            File("a.log", 100),
            File("b.log", 200),
            File("c.txt", 50),
            File("readme", 10));

        var stats = DiskAnalysis.ExtensionBreakdown(root);

        Assert.Equal(".log", stats[0].Extension);       // largest total first
        Assert.Equal(2, stats[0].FileCount);
        Assert.Equal(300, stats[0].TotalBytes);
        Assert.Contains(stats, s => s.Extension == ".txt" && s.FileCount == 1 && s.TotalBytes == 50);
        Assert.Contains(stats, s => s.Extension == "(no extension)" && s.FileCount == 1);
    }

    [Fact]
    public void ExtensionBreakdown_IsCaseInsensitive()
    {
        var root = Dir("root", File("A.LOG", 100), File("b.log", 100));
        var stats = DiskAnalysis.ExtensionBreakdown(root);
        Assert.Single(stats);
        Assert.Equal(2, stats[0].FileCount);
    }

    // ── Empty folders ────────────────────────────────────────────────────────

    [Fact]
    public void FindEmptyFolders_ReturnsOnlyLeafEmptyDirectories()
    {
        var root = Dir("root",
            Dir("empty1"),
            Dir("hasFile", File("x.bin", 10)),
            Dir("empty2"));

        var empties = DiskAnalysis.FindEmptyFolders(root);

        Assert.Contains(empties, n => n.Name == "empty1");
        Assert.Contains(empties, n => n.Name == "empty2");
        Assert.DoesNotContain(empties, n => n.Name == "hasFile");
        Assert.DoesNotContain(empties, n => n.Name == "root");
    }

    [Fact]
    public void EnumerateFiles_ReturnsEveryFileNotDirectory()
    {
        var root = Dir("root",
            File("a", 1),
            Dir("sub", File("b", 2), File("c", 3)));

        var files = DiskAnalysis.EnumerateFiles(root).ToList();
        Assert.Equal(3, files.Count);
        Assert.All(files, f => Assert.False(f.IsDirectory));
    }

    // ── Duplicate pre-filter ─────────────────────────────────────────────────

    [Fact]
    public void GroupBySizeCandidates_KeepsOnlyMultiFileSizeGroups()
    {
        var files = new[]
        {
            File("a", 100), File("b", 100),   // pair
            File("c", 200),                    // unique size
            File("d", 300), File("e", 300), File("f", 300), // triple
            File("empty", 0), File("empty2", 0)             // zero-size excluded
        };

        var groups = DuplicateFinder.GroupBySizeCandidates(files);

        Assert.Equal(2, groups.Count);
        Assert.Contains(groups, g => g.Count == 2 && g[0].SizeBytes == 100);
        Assert.Contains(groups, g => g.Count == 3 && g[0].SizeBytes == 300);
    }
}
