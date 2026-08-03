using System.Collections.Generic;
using System.Collections.ObjectModel;
using Jellyfin.Plugin.KometaThemes.Models;
using Jellyfin.Plugin.KometaThemes.Resolving;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.KometaThemes.Tests;

/// <summary>
/// The group ordinal is a position over the episode ranges animethemes.moe reports, not a season
/// number the source states. Comparing it to a Jellyfin season number is only valid when those
/// ranges partition the run cleanly, and the previous code both assumed that and fell back to the
/// first group whenever nothing matched — so a later season silently received season 1's themes and
/// the wrong assignment was written into the tracker as fact.
/// </summary>
public class ThemeGrouperTests
{
    private static ThemeGrouper CreateGrouper()
        => new(NullLogger<ThemeGrouper>.Instance);

    private static SeasonGroup Group(int seasonNumber, int? start, int? end)
        => new(seasonNumber, start, end, new Collection<FlattenedTheme>());

    [Fact]
    public void FormsSeasonPartition_SingleGroup_IsTrusted()
    {
        // One group covers the whole run; there is nothing to mis-assign.
        Assert.True(ThemeGrouper.FormsSeasonPartition(new List<SeasonGroup> { Group(1, 1, 24) }));
    }

    [Fact]
    public void FormsSeasonPartition_CleanAscendingCours_IsTrusted()
    {
        var groups = new List<SeasonGroup> { Group(1, 1, 12), Group(2, 13, 24) };
        Assert.True(ThemeGrouper.FormsSeasonPartition(groups));
    }

    [Fact]
    public void FormsSeasonPartition_OverlappingWholeRunRange_IsNotTrusted()
    {
        // The real shape that caused the bug: per-cour ranges plus a whole-run range. The ordinals
        // mean nothing here, and matching Jellyfin's season 2 gave the single episode-12 theme.
        var groups = new List<SeasonGroup> { Group(1, 1, 11), Group(2, 12, 12), Group(3, 1, 24) };
        Assert.False(ThemeGrouper.FormsSeasonPartition(groups));
    }

    [Fact]
    public void FormsSeasonPartition_MissingEpisodeNumbers_IsNotTrusted()
    {
        var groups = new List<SeasonGroup> { Group(1, null, null), Group(2, 13, 24) };
        Assert.False(ThemeGrouper.FormsSeasonPartition(groups));
    }

    [Fact]
    public void FormsSeasonPartition_UnclassifiedGroupIsIgnored()
    {
        var groups = new List<SeasonGroup>
        {
            Group(1, 1, 12),
            Group(2, 13, 24),
            Group(SeasonGroup.UnclassifiedSeason, null, null)
        };

        Assert.True(ThemeGrouper.FormsSeasonPartition(groups));
    }

    [Fact]
    public void FormsSeasonPartition_OnlyUnclassified_IsNotTrusted()
    {
        var groups = new List<SeasonGroup> { Group(SeasonGroup.UnclassifiedSeason, null, null) };
        Assert.False(ThemeGrouper.FormsSeasonPartition(groups));
    }

    [Fact]
    public void FindMatchingGroup_CleanPartition_MatchesTheRequestedSeason()
    {
        var grouper = CreateGrouper();
        var groups = new List<SeasonGroup> { Group(1, 1, 12), Group(2, 13, 24) };

        var match = grouper.FindMatchingGroup(groups, 2);

        Assert.NotNull(match);
        Assert.Equal(2, match!.SeasonNumber);
    }

    [Fact]
    public void FindMatchingGroup_OverlappingRanges_ReturnsNullRatherThanTheWrongSeason()
    {
        var grouper = CreateGrouper();
        var groups = new List<SeasonGroup> { Group(1, 1, 11), Group(2, 12, 12), Group(3, 1, 24) };

        // Null is the honest answer, and the caller reads it as "consider every theme".
        Assert.Null(grouper.FindMatchingGroup(groups, 2));
    }

    [Fact]
    public void FindMatchingGroup_UnknownSeason_ReturnsNullRatherThanTheFirstGroup()
    {
        var grouper = CreateGrouper();
        var groups = new List<SeasonGroup> { Group(1, 1, 12), Group(2, 13, 24) };

        Assert.Null(grouper.FindMatchingGroup(groups, 7));
    }

    [Fact]
    public void FindMatchingGroup_NoSeasonDetected_ReturnsNull()
    {
        var grouper = CreateGrouper();
        var groups = new List<SeasonGroup> { Group(1, 1, 12) };

        Assert.Null(grouper.FindMatchingGroup(groups, null));
    }

    [Fact]
    public void FindMatchingGroup_NeverReturnsTheUnclassifiedGroup()
    {
        var grouper = CreateGrouper();
        var groups = new List<SeasonGroup>
        {
            Group(1, 1, 12),
            Group(SeasonGroup.UnclassifiedSeason, null, null)
        };

        // Season 0 is the sentinel, not a real season anyone can ask for.
        Assert.Null(grouper.FindMatchingGroup(groups, SeasonGroup.UnclassifiedSeason));
    }

    [Fact]
    public void FindMatchingGroup_EmptyGroups_ReturnsNull()
        => Assert.Null(CreateGrouper().FindMatchingGroup(new List<SeasonGroup>(), 1));
}
