using Jellyfin.Plugin.KometaThemes.Resolving;
using Xunit;

namespace Jellyfin.Plugin.KometaThemes.Tests;

/// <summary>
/// "season", "part" and "cour" are stripped as noise words before token scoring, which made
/// "Attack on Titan Season 2" and "Attack on Titan" produce identical token sets and therefore a
/// confident match onto the wrong season. The ordinal is the discriminator that was being discarded,
/// so it is now extracted and compared separately.
/// </summary>
public class SeasonOrdinalTests
{
    [Theory]
    [InlineData("Attack on Titan Season 2", 2)]
    [InlineData("attack on titan season 2", 2)]
    [InlineData("Log Horizon Season 3", 3)]
    [InlineData("Some Show Part 2", 2)]
    [InlineData("Some Show Cour 2", 2)]
    [InlineData("Qualcosa Stagione 4", 4)]
    [InlineData("Show Series 2", 2)]
    [InlineData("Show Season2", 2)]
    [InlineData("Show Season  3", 3)]
    public void ExtractSeasonOrdinal_ReadsExplicitDigits(string title, int expected)
    {
        Assert.Equal(expected, TitleSearchResolver.ExtractSeasonOrdinal(title));
    }

    [Theory]
    [InlineData("Show Season II", 2)]
    [InlineData("Show Season IV", 4)]
    [InlineData("Show Part III", 3)]
    [InlineData("Show Season X", 10)]
    public void ExtractSeasonOrdinal_ReadsRomanNumerals(string title, int expected)
    {
        Assert.Equal(expected, TitleSearchResolver.ExtractSeasonOrdinal(title));
    }

    [Theory]
    [InlineData("Attack on Titan")]
    [InlineData("Cowboy Bebop")]
    [InlineData("Mob Psycho 100")]
    [InlineData("86")]
    [InlineData("District 9")]
    [InlineData("Fullmetal Alchemist Brotherhood")]
    [InlineData("")]
    [InlineData(null)]
    public void ExtractSeasonOrdinal_ReturnsNull_WithoutAnExplicitMarker(string? title)
    {
        // A bare trailing number is part of the title far more often than it is a season, so only
        // an explicit season/part/cour/series marker counts.
        Assert.Null(TitleSearchResolver.ExtractSeasonOrdinal(title));
    }
}
