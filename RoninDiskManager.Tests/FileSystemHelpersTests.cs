using RoninDiskManager.Engine;
using Xunit;

namespace RoninDiskManager.Tests;

public class FileSystemHelpersTests
{
    // ── MatchesQuery: substring ──────────────────────────────────────────────

    [Theory]
    [InlineData("Readme.txt", "readme", true)]   // case-insensitive
    [InlineData("Readme.txt", "ME.T",   true)]   // mid-string
    [InlineData("Readme.txt", "xyz",     false)]
    [InlineData("config.json", "json",   true)]
    public void MatchesQuery_Substring(string name, string query, bool expected)
        => Assert.Equal(expected, FileSystemHelpers.MatchesQuery(name, query, isWildcard: false));

    // ── MatchesQuery: wildcard ───────────────────────────────────────────────

    [Theory]
    [InlineData("photo.jpg", "*.jpg", true)]
    [InlineData("photo.JPG", "*.jpg", true)]     // ignoreCase
    [InlineData("photo.png", "*.jpg", false)]
    [InlineData("save01.sav", "save0?.sav", true)]
    [InlineData("save12.sav", "save0?.sav", false)]
    public void MatchesQuery_Wildcard(string name, string query, bool expected)
        => Assert.Equal(expected, FileSystemHelpers.MatchesQuery(name, query, isWildcard: true));

    [Theory]
    [InlineData("a.jpg", "*.jpg", true)]         // overload auto-detects wildcard
    [InlineData("report2025", "2025", true)]     // overload treats plain string as substring
    public void MatchesQuery_AutoDetectsWildcard(string name, string query, bool expected)
        => Assert.Equal(expected, FileSystemHelpers.MatchesQuery(name, query));

    [Fact]
    public void MatchesQuery_EmptyNameIsNeverAMatch()
        => Assert.False(FileSystemHelpers.MatchesQuery("", "anything"));

    // ── Long-path detection ──────────────────────────────────────────────────

    [Fact]
    public void ExceedsLegacyMaxPath_TrueAtOrAboveLimit()
    {
        Assert.True(FileSystemHelpers.ExceedsLegacyMaxPath(new string('x', 260)));
        Assert.True(FileSystemHelpers.ExceedsLegacyMaxPath(new string('x', 400)));
    }

    [Fact]
    public void ExceedsLegacyMaxPath_FalseBelowLimit()
    {
        Assert.False(FileSystemHelpers.ExceedsLegacyMaxPath(@"C:\short\path.txt"));
        Assert.False(FileSystemHelpers.ExceedsLegacyMaxPath(""));
    }

    // ── Extended-length prefixing ────────────────────────────────────────────

    [Fact]
    public void ToExtendedPath_PrefixesRootedLocalPath()
        => Assert.Equal(@"\\?\C:\a\b", FileSystemHelpers.ToExtendedPath(@"C:\a\b"));

    [Fact]
    public void ToExtendedPath_LeavesAlreadyPrefixedUnchanged()
        => Assert.Equal(@"\\?\C:\a", FileSystemHelpers.ToExtendedPath(@"\\?\C:\a"));

    [Fact]
    public void ToExtendedPath_LeavesUncUnchanged()
        => Assert.Equal(@"\\server\share\f", FileSystemHelpers.ToExtendedPath(@"\\server\share\f"));

    [Fact]
    public void ToExtendedPath_LeavesRelativeUnchanged()
        => Assert.Equal(@"sub\file.txt", FileSystemHelpers.ToExtendedPath(@"sub\file.txt"));

    // ── Byte formatting ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "2 KB")]            // rounds
    [InlineData(1_048_576, "1 MB")]
    [InlineData(1_073_741_824, "1.0 GB")]
    [InlineData(3_221_225_472, "3.0 GB")]
    public void FormatBytes_ProducesExpectedString(long bytes, string expected)
        => Assert.Equal(expected, FileSystemHelpers.FormatBytes(bytes));
}
