using System.Collections.Generic;
using Jellyfin.Plugin.KometaThemes.YouTube;
using YoutubeExplode.Common;
using YoutubeExplode.Videos.Streams;
using Xunit;

namespace Jellyfin.Plugin.KometaThemes.Tests;

/// <summary>
/// Covers the stream pairing used by the bundled YouTube extractor.
/// </summary>
/// <remarks>
/// This is the part most likely to regress silently: picking a mismatched pair still downloads
/// successfully and only fails later, in the ffmpeg stream copy, because a container cannot hold
/// the codec it was handed. These build manifests directly, so no network is involved.
/// </remarks>
public class ManagedYouTubeStreamSelectionTests
{
    [Fact]
    public void PrefersWebmWhenBothContainersAreComplete()
    {
        var manifest = new StreamManifest(new List<IStreamInfo>
        {
            Video("webm", "vp9", 1080),
            Audio("webm", "opus", 130_000),
            Video("mp4", "avc1", 1080),
            Audio("mp4", "mp4a", 130_000)
        });

        var pair = ManagedYouTubeExtractor.ChooseVideoPair(manifest, 1080);

        Assert.NotNull(pair);
        Assert.Equal("webm", pair!.Value.Container);
        Assert.Equal("vp9", pair.Value.Video.VideoCodec);
    }

    [Fact]
    public void FallsBackToMp4WhenWebmHasNoAudio()
    {
        // A real case: some videos expose VP9 video but only AAC audio.
        var manifest = new StreamManifest(new List<IStreamInfo>
        {
            Video("webm", "vp9", 1080),
            Video("mp4", "avc1", 720),
            Audio("mp4", "mp4a", 128_000)
        });

        var pair = ManagedYouTubeExtractor.ChooseVideoPair(manifest, 1080);

        Assert.NotNull(pair);
        Assert.Equal("mp4", pair!.Value.Container);
        Assert.Equal(720, pair.Value.Video.VideoQuality.MaxHeight);
    }

    [Fact]
    public void NeverPairsAcrossContainers()
    {
        // webm video with only mp4 audio: pairing these would download fine and then fail the
        // stream copy, because webm cannot carry AAC and mp4 cannot carry VP9 here.
        var manifest = new StreamManifest(new List<IStreamInfo>
        {
            Video("webm", "vp9", 1080),
            Audio("mp4", "mp4a", 128_000)
        });

        Assert.Null(ManagedYouTubeExtractor.ChooseVideoPair(manifest, 1080));
    }

    [Fact]
    public void RespectsTheHeightCeiling()
    {
        var manifest = new StreamManifest(new List<IStreamInfo>
        {
            Video("webm", "vp9", 2160),
            Video("webm", "vp9", 1440),
            Video("webm", "vp9", 1080),
            Video("webm", "vp9", 720),
            Audio("webm", "opus", 130_000)
        });

        var pair = ManagedYouTubeExtractor.ChooseVideoPair(manifest, 1080);

        Assert.NotNull(pair);
        Assert.Equal(1080, pair!.Value.Video.VideoQuality.MaxHeight);
    }

    [Fact]
    public void TakesTheHighestBitrateAudioWithinTheChosenContainer()
    {
        var manifest = new StreamManifest(new List<IStreamInfo>
        {
            Video("webm", "vp9", 1080),
            Audio("webm", "opus", 70_000),
            Audio("webm", "opus", 160_000),
            Audio("webm", "opus", 130_000)
        });

        var pair = ManagedYouTubeExtractor.ChooseVideoPair(manifest, 1080);

        Assert.NotNull(pair);
        Assert.Equal(160_000, pair!.Value.Audio.Bitrate.BitsPerSecond);
    }

    [Fact]
    public void ReturnsNullWhenThereIsNoVideoAtAll()
    {
        var manifest = new StreamManifest(new List<IStreamInfo>
        {
            Audio("webm", "opus", 130_000)
        });

        Assert.Null(ManagedYouTubeExtractor.ChooseVideoPair(manifest, 1080));
    }

    [Fact]
    public void IgnoresPreMuxedStreams()
    {
        // YouTube's only pre-muxed option is 360p. It must never be chosen over a separate pair,
        // which is the entire reason the extractor muxes at all.
        var manifest = new StreamManifest(new List<IStreamInfo>
        {
            new MuxedStreamInfo(
                "https://example.invalid/muxed",
                new Container("mp4"),
                new FileSize(11_000_000),
                new Bitrate(500_000),
                "mp4a",
                null,
                null,
                "avc1",
                new VideoQuality("360p", 360, 30),
                new Resolution(640, 360)),
            Video("webm", "vp9", 1080),
            Audio("webm", "opus", 130_000)
        });

        var pair = ManagedYouTubeExtractor.ChooseVideoPair(manifest, 1080);

        Assert.NotNull(pair);
        Assert.Equal(1080, pair!.Value.Video.VideoQuality.MaxHeight);
    }

    private static VideoOnlyStreamInfo Video(string container, string codec, int height) =>
        new(
            "https://example.invalid/video",
            new Container(container),
            new FileSize(20_000_000),
            new Bitrate(height * 2_000L),
            codec,
            new VideoQuality(height + "p", height, 30),
            new Resolution(height * 16 / 9, height));

    private static AudioOnlyStreamInfo Audio(string container, string codec, long bitrate) =>
        new(
            "https://example.invalid/audio",
            new Container(container),
            new FileSize(3_000_000),
            new Bitrate(bitrate),
            codec,
            null,
            null);
}
