using Jellyfin.Plugin.KometaThemes.YouTube;
using Xunit;

namespace Jellyfin.Plugin.KometaThemes.Tests;

/// <summary>
/// The import endpoint hands a video ID to an external process, so this parser is the security
/// boundary for that feature: it decides what the server will go and fetch. The rejection cases
/// matter as much as the acceptance ones.
/// </summary>
public class YouTubeUrlParserTests
{
    private const string ExpectedId = "dQw4w9WgXcQ";

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("http://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ")]
    [InlineData("youtu.be/dQw4w9WgXcQ")]
    [InlineData("www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://m.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://music.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/shorts/dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/embed/dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/live/dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/v/dQw4w9WgXcQ")]
    [InlineData("dQw4w9WgXcQ")]
    [InlineData("   https://www.youtube.com/watch?v=dQw4w9WgXcQ   ")]
    public void TryGetVideoId_Accepts_SupportedLinkShapes(string input)
    {
        Assert.True(YouTubeUrlParser.TryGetVideoId(input, out var id));
        Assert.Equal(ExpectedId, id);
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=42s")]
    [InlineData("https://www.youtube.com/watch?list=PLabcdef&v=dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ&list=PLabcdef&index=3")]
    public void TryGetVideoId_IgnoresExtraQueryParameters(string input)
    {
        // Playlist and timestamp parameters must not change which video is imported.
        Assert.True(YouTubeUrlParser.TryGetVideoId(input, out var id));
        Assert.Equal(ExpectedId, id);
    }

    [Theory]
    [InlineData("https://evil.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtube.com.evil.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://notyoutube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://user:pass@www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://127.0.0.1:8096/System/Info")]
    public void TryGetVideoId_Rejects_UntrustedOrUnsafeTargets(string input)
    {
        // An SSRF here would let an admin-authenticated request make the server fetch an
        // arbitrary address, so a non-YouTube host must never yield an ID.
        Assert.False(YouTubeUrlParser.TryGetVideoId(input, out var id));
        Assert.Equal(string.Empty, id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("https://www.youtube.com/watch?v=short")]
    [InlineData("https://www.youtube.com/watch?v=waytoolongtobeanid")]
    [InlineData("https://www.youtube.com/watch?v=has spaces!")]
    [InlineData("https://www.youtube.com/@somechannel")]
    [InlineData("https://www.youtube.com/playlist?list=PLabcdef")]
    [InlineData("not a url at all")]
    public void TryGetVideoId_Rejects_MalformedInput(string? input)
    {
        Assert.False(YouTubeUrlParser.TryGetVideoId(input, out var id));
        Assert.Equal(string.Empty, id);
    }

    [Fact]
    public void TryGetVideoId_Accepts_IdsBeginningWithDash()
    {
        // Real YouTube IDs can start with '-'. They must be accepted, and they are safe because
        // BuildWatchUrl embeds them inside an https:// URL that is passed after an "--" argument
        // terminator, so the process never sees an argument that begins with a dash.
        Assert.True(YouTubeUrlParser.TryGetVideoId("https://www.youtube.com/watch?v=-wtIMTCHWuI", out var id));
        Assert.Equal("-wtIMTCHWuI", id);
        Assert.StartsWith("https://", YouTubeUrlParser.BuildWatchUrl(id), StringComparison.Ordinal);
    }

    [Fact]
    public void BuildWatchUrl_ProducesCanonicalUrl()
    {
        Assert.Equal(
            "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
            YouTubeUrlParser.BuildWatchUrl(ExpectedId));
    }

    [Fact]
    public void BuildWatchUrl_Rejects_InvalidId()
    {
        // The canonical URL is what actually gets fetched, so it must never be built from
        // unvalidated input.
        Assert.Throws<ArgumentException>(() => YouTubeUrlParser.BuildWatchUrl("../../etc/passwd"));
        Assert.Throws<ArgumentException>(() => YouTubeUrlParser.BuildWatchUrl("short"));
        Assert.Throws<ArgumentException>(() => YouTubeUrlParser.BuildWatchUrl(string.Empty));
    }

    [Theory]
    [InlineData("dQw4w9WgXcQ", true)]
    [InlineData("-wtIMTCHWuI", true)]
    [InlineData("___________", true)]
    [InlineData("short", false)]
    [InlineData("twelvecharss", false)]
    [InlineData("has space x", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidVideoId_MatchesTheElevenCharacterAlphabet(string? candidate, bool expected)
    {
        Assert.Equal(expected, YouTubeUrlParser.IsValidVideoId(candidate));
    }
}
