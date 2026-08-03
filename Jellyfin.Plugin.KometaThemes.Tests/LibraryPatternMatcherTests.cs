using System.Diagnostics;
using Jellyfin.Plugin.KometaThemes.Configuration;
using Xunit;

namespace Jellyfin.Plugin.KometaThemes.Tests;

/// <summary>
/// The library pattern is a free-text setting compiled into a regex and then evaluated once per
/// library name inside a scheduled task. It used to be compiled in four places with no match
/// timeout, so a pattern with nested quantifiers — easy to type by accident — could back-track for an
/// unbounded time, and an outright invalid pattern threw out of whichever call site got there first.
/// </summary>
public class LibraryPatternMatcherTests
{
    [Theory]
    [InlineData("Anime", "Anime", true)]
    [InlineData("Anime", "My Anime Shows", true)]
    [InlineData("Anime", "anime", true)]
    [InlineData("Anime", "Movies", false)]
    [InlineData("Anime|Cartoons", "Cartoons", true)]
    [InlineData("^Anime$", "Anime", true)]
    [InlineData("^Anime$", "Anime Movies", false)]
    public void Create_MatchesAsARegex(string pattern, string name, bool expected)
    {
        var matcher = LibraryPatternMatcher.Create(pattern);
        Assert.Equal(expected, matcher(name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyPattern_FallsBackToTheDefault(string? pattern)
    {
        var matcher = LibraryPatternMatcher.Create(pattern);
        Assert.True(matcher("Anime"));
        Assert.False(matcher("Documentaries"));
    }

    [Fact]
    public void Create_InvalidRegex_DegradesToASubstringMatch()
    {
        // An unbalanced group is not a valid expression. Treating it as literal text is what a user
        // typing a plain library name expects, and it must not throw.
        var matcher = LibraryPatternMatcher.Create("Anime (Kids");

        Assert.True(matcher("Anime (Kids"));
        Assert.True(matcher("My Anime (Kids Section"));
        Assert.False(matcher("Anime"));
    }

    [Fact]
    public void Create_NullName_IsNeverIncluded()
    {
        Assert.False(LibraryPatternMatcher.Create("Anime")(null));
        Assert.False(LibraryPatternMatcher.Create("Anime (Kids")(null));
    }

    [Fact]
    public void Create_CatastrophicBacktracking_IsBoundedByTheMatchTimeout()
    {
        // The classic ReDoS shape. Without a timeout this does not finish in any useful time; with
        // one it must return promptly and, since the result is unknown, must not include the library.
        var matcher = LibraryPatternMatcher.Create("^(a+)+$");
        var name = new string('a', 40) + "!";

        var stopwatch = Stopwatch.StartNew();
        var included = matcher(name);
        stopwatch.Stop();

        Assert.False(included);
        Assert.True(
            stopwatch.Elapsed.TotalSeconds < 5,
            $"Match should be bounded by the timeout but took {stopwatch.Elapsed.TotalSeconds:0.0}s");
    }
}
