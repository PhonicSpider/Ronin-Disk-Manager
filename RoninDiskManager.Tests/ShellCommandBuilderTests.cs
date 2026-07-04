using RoninDiskManager.Engine;
using Xunit;
using Opts = RoninDiskManager.Engine.ShellCommandBuilder.FileOpOptions;

namespace RoninDiskManager.Tests;

public class ShellCommandBuilderTests
{
    // ── EscapePs ────────────────────────────────────────────────────────────

    [Fact]
    public void EscapePs_DoublesSingleQuotes()
    {
        Assert.Equal("O''Brien", ShellCommandBuilder.EscapePs("O'Brien"));
        Assert.Equal(@"C:\Games\it''s here", ShellCommandBuilder.EscapePs(@"C:\Games\it's here"));
    }

    [Fact]
    public void EscapePs_LeavesPlainPathUnchanged()
        => Assert.Equal(@"C:\a\b", ShellCommandBuilder.EscapePs(@"C:\a\b"));

    // ── Move ────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildMove_UsesPathAndDestination()
    {
        var cmd = ShellCommandBuilder.BuildMove(new Opts
        {
            SourcePath = @"C:\src\file.txt",
            Destination = @"D:\dst"
        });
        Assert.Equal(@"Move-Item -Path 'C:\src\file.txt' -Destination 'D:\dst'", cmd);
    }

    [Fact]
    public void BuildMove_LiteralPath_UsesLiteralPathParameter()
    {
        var cmd = ShellCommandBuilder.BuildMove(new Opts
        {
            SourcePath = @"C:\a[1].txt",
            Destination = @"D:\dst",
            LiteralPath = true
        });
        Assert.Contains("-LiteralPath 'C:\\a[1].txt'", cmd);
        Assert.DoesNotContain("-Path '", cmd);
    }

    [Fact]
    public void BuildMove_AppendsFlagsInOrder()
    {
        var cmd = ShellCommandBuilder.BuildMove(new Opts
        {
            SourcePath = @"C:\a",
            Destination = @"D:\b",
            Force = true,
            Verbose = true,
            WhatIf = true
        });
        Assert.Equal(@"Move-Item -Path 'C:\a' -Destination 'D:\b' -Force -Verbose -WhatIf", cmd);
    }

    [Fact]
    public void BuildMove_EscapesQuotesInPaths()
    {
        var cmd = ShellCommandBuilder.BuildMove(new Opts
        {
            SourcePath = @"C:\it's\a.txt",
            Destination = @"D:\o'dir"
        });
        Assert.Contains(@"'C:\it''s\a.txt'", cmd);
        Assert.Contains(@"'D:\o''dir'", cmd);
    }

    // ── Copy ────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildCopy_IncludesRecurseWhenSet()
    {
        var cmd = ShellCommandBuilder.BuildCopy(new Opts
        {
            SourcePath = @"C:\src",
            Destination = @"D:\dst",
            Recurse = true
        });
        Assert.Equal(@"Copy-Item -Path 'C:\src' -Destination 'D:\dst' -Recurse", cmd);
    }

    [Fact]
    public void BuildCopy_OmitsRecurseWhenNotSet()
    {
        var cmd = ShellCommandBuilder.BuildCopy(new Opts
        {
            SourcePath = @"C:\src\f.txt",
            Destination = @"D:\dst"
        });
        Assert.DoesNotContain("-Recurse", cmd);
    }

    // ── Delete ──────────────────────────────────────────────────────────────

    [Fact]
    public void BuildDelete_HasNoDestination()
    {
        var cmd = ShellCommandBuilder.BuildDelete(new Opts
        {
            SourcePath = @"C:\junk",
            Force = true,
            Recurse = true
        });
        Assert.Equal(@"Remove-Item -Path 'C:\junk' -Force -Recurse", cmd);
        Assert.DoesNotContain("-Destination", cmd);
    }

    [Fact]
    public void BuildDelete_WhatIfEnablesDryRun()
    {
        var cmd = ShellCommandBuilder.BuildDelete(new Opts { SourcePath = @"C:\x", WhatIf = true });
        Assert.Contains("-WhatIf", cmd);
    }

    // ── Filters (shared) ──────────────────────────────────────────────────────

    [Fact]
    public void Filters_AreAppendedWhenPresent()
    {
        var cmd = ShellCommandBuilder.BuildDelete(new Opts
        {
            SourcePath = @"C:\logs",
            Recurse = true,
            Filter = "*.log",
            Include = "*.txt, *.tmp",
            Exclude = "*.keep"
        });
        Assert.Contains("-Filter '*.log'", cmd);
        Assert.Contains("-Include *.txt, *.tmp", cmd);
        Assert.Contains("-Exclude *.keep", cmd);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("")]
    public void Filters_BlankValuesAreSkipped(string blank)
    {
        var cmd = ShellCommandBuilder.BuildMove(new Opts
        {
            SourcePath = @"C:\a",
            Destination = @"D:\b",
            Filter = blank,
            Include = blank,
            Exclude = blank
        });
        Assert.DoesNotContain("-Filter", cmd);
        Assert.DoesNotContain("-Include", cmd);
        Assert.DoesNotContain("-Exclude", cmd);
    }
}
