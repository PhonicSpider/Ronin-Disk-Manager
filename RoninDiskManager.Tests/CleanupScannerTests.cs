using RoninDiskManager.Engine;
using Xunit;

namespace RoninDiskManager.Tests;

public class CleanupScannerTests
{
    private static readonly DateTime Cutoff = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Old = Cutoff.AddDays(-10);
    private static readonly DateTime Recent = Cutoff.AddDays(10);

    [Fact]
    public void ShouldClean_MatchesPatternAndOldEnough()
        => Assert.True(CleanupScanner.ShouldClean("server.log", "*.log", Old, Cutoff));

    [Fact]
    public void ShouldClean_RejectsWrongPattern()
        => Assert.False(CleanupScanner.ShouldClean("server.txt", "*.log", Old, Cutoff));

    [Fact]
    public void ShouldClean_RejectsTooRecent()
        => Assert.False(CleanupScanner.ShouldClean("server.log", "*.log", Recent, Cutoff));

    [Fact]
    public void ShouldClean_EmptyPatternMatchesAnyNameButStillChecksAge()
    {
        Assert.True(CleanupScanner.ShouldClean("anything.dat", "", Old, Cutoff));
        Assert.False(CleanupScanner.ShouldClean("anything.dat", "", Recent, Cutoff));
    }

    [Fact]
    public void ShouldClean_SubstringPatternWorks()
        => Assert.True(CleanupScanner.ShouldClean("crash-dump-01.dmp", "crash", Old, Cutoff));
}
