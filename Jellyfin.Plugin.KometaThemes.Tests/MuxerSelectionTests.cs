using Jellyfin.Plugin.KometaThemes;
using Jellyfin.Plugin.KometaThemes.Models;
using Xunit;

namespace Jellyfin.Plugin.KometaThemes.Tests;

/// <summary>
/// The encoder writes to a sibling file carrying a <c>.kt-part</c> suffix so a failed run can never
/// leave a half-written theme in place, which means ffmpeg cannot infer the container from the file
/// name and the muxer has to be stated explicitly. Getting it wrong writes a webm stream into an mp4
/// container or vice versa.
/// </summary>
public class MuxerSelectionTests
{
    [Theory]
    [InlineData("/media/Show/backdrops/OP1__50.webm", "webm")]
    [InlineData("/media/Show/backdrops/OP1__50.WEBM", "webm")]
    [InlineData("/media/Show/backdrops/OP1__50.mp4", "mp4")]
    [InlineData("/media/Show/backdrops/OP1__50.MP4", "mp4")]
    public void MuxerFor_Video_FollowsTheTargetContainer(string path, string expected)
        => Assert.Equal(expected, AnimeThemesDownloader.MuxerFor(MediaType.Video, path));

    [Theory]
    [InlineData("/media/Show/theme-music/OP1__50.mp3")]
    [InlineData("/media/Show/theme-music/OP1__50.webm")]
    public void MuxerFor_Audio_IsAlwaysMp3(string path)
        => Assert.Equal("mp3", AnimeThemesDownloader.MuxerFor(MediaType.Audio, path));

    [Fact]
    public void MuxerFor_UnknownVideoContainer_FallsBackToWebm()
    {
        // Only the containers the import path is willing to produce should ever reach here, but the
        // fallback must be a real muxer rather than whatever the extension happened to say.
        Assert.Equal("webm", AnimeThemesDownloader.MuxerFor(MediaType.Video, "/media/Show/backdrops/OP1__50.mkv"));
        Assert.Equal("webm", AnimeThemesDownloader.MuxerFor(MediaType.Video, "/media/Show/backdrops/OP1__50"));
    }
}
