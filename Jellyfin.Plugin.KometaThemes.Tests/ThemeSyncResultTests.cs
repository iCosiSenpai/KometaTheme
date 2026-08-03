using Jellyfin.Plugin.KometaThemes.Models;
using Xunit;

namespace Jellyfin.Plugin.KometaThemes.Tests;

/// <summary>
/// The sync runner decides whether an item stays on the Unresolved list purely from this result,
/// so the distinction between "nothing changed" and "something failed" has to survive aggregation.
/// </summary>
public class ThemeSyncResultTests
{
    [Fact]
    public void Default_IsAllZero_AndReportsNothing()
    {
        var result = default(ThemeSyncResult);
        Assert.False(result.ChangesMade);
        Assert.False(result.HasFailures);
        Assert.Equal(0, result.Downloaded);
        Assert.Equal(0, result.Failed);
        Assert.Equal(0, result.Skipped);
    }

    [Fact]
    public void SkippedOnly_IsNotAChange_AndNotAFailure()
    {
        // Every theme already on disk: the item is fine and must not be recorded as failed.
        var result = new ThemeSyncResult(0, 0, 3);
        Assert.False(result.ChangesMade);
        Assert.False(result.HasFailures);
    }

    [Fact]
    public void FailedOnly_IsAFailure_EvenThoughNothingChanged()
    {
        // This is the regression that mattered: a download failure returned "no change", so the
        // runner treated the item as clean and removed it from the failed-items store.
        var result = new ThemeSyncResult(0, 2, 0);
        Assert.False(result.ChangesMade);
        Assert.True(result.HasFailures);
    }

    [Fact]
    public void PartialSuccess_ReportsBothChangeAndFailure()
    {
        var result = new ThemeSyncResult(1, 1, 0);
        Assert.True(result.ChangesMade);
        Assert.True(result.HasFailures);
    }

    [Fact]
    public void Addition_SumsEachCounterIndependently()
    {
        var combined = new ThemeSyncResult(1, 2, 3) + new ThemeSyncResult(10, 20, 30);
        Assert.Equal(11, combined.Downloaded);
        Assert.Equal(22, combined.Failed);
        Assert.Equal(33, combined.Skipped);
    }

    [Fact]
    public void Addition_PreservesAFailureFromEitherSide()
    {
        // Audio and video are aggregated per item; a failure in one must not be masked by the other.
        Assert.True((new ThemeSyncResult(5, 0, 0) + new ThemeSyncResult(0, 1, 0)).HasFailures);
        Assert.True((new ThemeSyncResult(0, 1, 0) + new ThemeSyncResult(5, 0, 0)).HasFailures);
        Assert.False((new ThemeSyncResult(5, 0, 0) + new ThemeSyncResult(0, 0, 2)).HasFailures);
    }

    [Fact]
    public void Add_MatchesTheOperator()
    {
        var left = new ThemeSyncResult(1, 2, 3);
        var right = new ThemeSyncResult(4, 5, 6);
        Assert.Equal(left + right, ThemeSyncResult.Add(left, right));
    }
}
