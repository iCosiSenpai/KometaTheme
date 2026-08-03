using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.MediaEncoding;
using Microsoft.Extensions.Logging;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace Jellyfin.Plugin.KometaThemes.YouTube;

/// <summary>
/// Fetches YouTube media using a managed extractor that travels with the plugin, so the feature
/// works on a stock Jellyfin install with nothing to install by hand.
/// </summary>
/// <remarks>
/// <para>
/// This is the default backend. <c>yt-dlp</c> remains supported and is preferred when present,
/// because it is updated continuously against YouTube's changes while a pinned managed extractor
/// only changes when this plugin is released. Neither backend is required for the plugin to run.
/// </para>
/// <para>
/// Video is fetched as a container-matched video-only plus audio-only pair and muxed with a stream
/// copy, rather than as one of YouTube's pre-muxed streams. Those exist but top out at 360p — a
/// single 360p stream was the only muxed option on every video measured — which is too low for a
/// backdrop. Muxing costs no re-encode: the codecs are copied as they are.
/// </para>
/// </remarks>
public sealed class ManagedYouTubeExtractor
{
    /// <summary>
    /// Container preference for the muxed result. Both hold the codecs YouTube serves without a
    /// re-encode, and both are containers Jellyfin plays. webm first because VP9 at a given quality
    /// is markedly smaller than the AVC equivalent.
    /// </summary>
    private static readonly string[] ContainerPreference = ["webm", "mp4"];

    private readonly ILogger<ManagedYouTubeExtractor> _logger;
    private readonly IHttpClientFactory _clientFactory;
    private readonly IMediaEncoder _mediaEncoder;
    private readonly Sync.TranscodeGate _transcodeGate;

    /// <summary>
    /// Initializes a new instance of the <see cref="ManagedYouTubeExtractor"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="clientFactory">HTTP client factory.</param>
    /// <param name="mediaEncoder">Jellyfin's media encoder, used to locate ffmpeg for the mux.</param>
    /// <param name="transcodeGate">Plugin-wide cap on concurrent ffmpeg invocations.</param>
    public ManagedYouTubeExtractor(
        ILogger<ManagedYouTubeExtractor> logger,
        IHttpClientFactory clientFactory,
        IMediaEncoder mediaEncoder,
        Sync.TranscodeGate transcodeGate)
    {
        _logger = logger;
        _clientFactory = clientFactory;
        _mediaEncoder = mediaEncoder;
        _transcodeGate = transcodeGate;
    }

    /// <summary>
    /// Downloads one video into <paramref name="workDirectory"/> as a single media file.
    /// </summary>
    /// <param name="videoId">A video ID already validated by <see cref="YouTubeUrlParser"/>.</param>
    /// <param name="audioOnly">When true, fetch only the best audio stream.</param>
    /// <param name="workDirectory">Scratch directory owned by the caller.</param>
    /// <param name="maxDurationSeconds">Reject videos longer than this.</param>
    /// <param name="maxBytes">Reject a stream selection larger than this.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The download result. The caller owns the produced file.</returns>
    public async Task<YouTubeDownloadResult> DownloadAsync(
        string videoId,
        bool audioOnly,
        string workDirectory,
        int maxDurationSeconds,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var http = _clientFactory.CreateClient("YouTube");
        var youtube = new YoutubeClient(http);

        var video = await youtube.Videos.GetAsync(videoId, cancellationToken).ConfigureAwait(false);
        var duration = (int)Math.Round(video.Duration?.TotalSeconds ?? 0d);

        if (duration > maxDurationSeconds)
        {
            return YouTubeDownloadResult.Failure(string.Create(
                CultureInfo.InvariantCulture,
                $"The video is {duration}s long; themes are limited to {maxDurationSeconds}s."));
        }

        var manifest = await youtube.Videos.Streams.GetManifestAsync(videoId, cancellationToken).ConfigureAwait(false);

        var produced = audioOnly
            ? await DownloadAudioAsync(youtube, manifest, workDirectory, maxBytes, cancellationToken).ConfigureAwait(false)
            : await DownloadVideoAsync(youtube, manifest, workDirectory, maxBytes, cancellationToken).ConfigureAwait(false);

        if (produced.Error.Length > 0)
        {
            return YouTubeDownloadResult.Failure(produced.Error);
        }

        _logger.LogInformation(
            "Fetched YouTube {VideoId} with the bundled extractor ({Title}, {Duration}s, {Bytes} bytes)",
            videoId,
            video.Title,
            duration,
            new FileInfo(produced.Path).Length);

        return new YouTubeDownloadResult(produced.Path, video.Title, duration, string.Empty);
    }

    /// <summary>
    /// Chooses a video-only and audio-only stream that share a container, so the two can be muxed
    /// without touching either codec.
    /// </summary>
    /// <param name="manifest">The stream manifest.</param>
    /// <param name="maxHeight">Ceiling on video height.</param>
    /// <returns>The chosen pair and its container, or null when no container has both.</returns>
    internal static (IVideoStreamInfo Video, IAudioStreamInfo Audio, string Container)? ChooseVideoPair(
        StreamManifest manifest,
        int maxHeight)
    {
        foreach (var container in ContainerPreference)
        {
            var video = manifest.GetVideoOnlyStreams()
                .Where(s => string.Equals(s.Container.Name, container, StringComparison.OrdinalIgnoreCase)
                            && s.VideoQuality.MaxHeight <= maxHeight)
                .OrderByDescending(s => s.VideoQuality.MaxHeight)
                .ThenByDescending(s => s.Bitrate.BitsPerSecond)
                .FirstOrDefault();

            var audio = manifest.GetAudioOnlyStreams()
                .Where(s => string.Equals(s.Container.Name, container, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(s => s.Bitrate.BitsPerSecond)
                .FirstOrDefault();

            if (video != null && audio != null)
            {
                return (video, audio, container);
            }
        }

        return null;
    }

    private async Task<(string Path, string Error)> DownloadAudioAsync(
        YoutubeClient youtube,
        StreamManifest manifest,
        string workDirectory,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var audio = manifest.GetAudioOnlyStreams()
            .OrderByDescending(s => s.Bitrate.BitsPerSecond)
            .FirstOrDefault();

        if (audio == null)
        {
            return (string.Empty, "The video has no audio stream.");
        }

        if (audio.Size.Bytes > maxBytes)
        {
            return (string.Empty, TooLarge(audio.Size.Bytes, maxBytes));
        }

        // The container does not matter: this is re-encoded to mp3 downstream, because that is what
        // Jellyfin theme songs are.
        var path = Path.Combine(workDirectory, "source." + audio.Container.Name);
        await youtube.Videos.Streams.DownloadAsync(audio, path, progress: null, cancellationToken).ConfigureAwait(false);
        return EnsureNonEmpty(path);
    }

    private async Task<(string Path, string Error)> DownloadVideoAsync(
        YoutubeClient youtube,
        StreamManifest manifest,
        string workDirectory,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var pair = ChooseVideoPair(manifest, 1080);
        if (pair == null)
        {
            return (string.Empty, "The video has no usable video and audio stream pair.");
        }

        var (videoStream, audioStream, container) = pair.Value;
        var total = videoStream.Size.Bytes + audioStream.Size.Bytes;
        if (total > maxBytes)
        {
            return (string.Empty, TooLarge(total, maxBytes));
        }

        var videoPath = Path.Combine(workDirectory, "video." + videoStream.Container.Name);
        var audioPath = Path.Combine(workDirectory, "audio." + audioStream.Container.Name);
        var outputPath = Path.Combine(workDirectory, "source." + container);

        await youtube.Videos.Streams.DownloadAsync(videoStream, videoPath, progress: null, cancellationToken).ConfigureAwait(false);
        await youtube.Videos.Streams.DownloadAsync(audioStream, audioPath, progress: null, cancellationToken).ConfigureAwait(false);

        var muxError = await MuxAsync(videoPath, audioPath, outputPath, container, cancellationToken).ConfigureAwait(false);
        if (muxError.Length > 0)
        {
            return (string.Empty, muxError);
        }

        // The pair is no longer needed and the caller's directory is scanned for the produced file.
        TryDelete(videoPath);
        TryDelete(audioPath);

        return EnsureNonEmpty(outputPath);
    }

    /// <summary>
    /// Joins the separate video and audio files into one container with a stream copy.
    /// </summary>
    private async Task<string> MuxAsync(
        string videoPath,
        string audioPath,
        string outputPath,
        string container,
        CancellationToken cancellationToken)
    {
        var encoderPath = _mediaEncoder.EncoderPath;
        if (string.IsNullOrWhiteSpace(encoderPath))
        {
            return "Jellyfin reported no ffmpeg path, so the video could not be assembled.";
        }

        // Counts against the same budget as every other ffmpeg run this plugin starts, even though
        // a stream copy is cheap, so a burst of imports cannot crowd out playback transcoding.
        using var slot = await _transcodeGate.AcquireAsync(cancellationToken).ConfigureAwait(false);

        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo(encoderPath)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                ErrorDialog = false,
                ArgumentList =
                {
                    "-nostdin", "-y",
                    "-i", videoPath,
                    "-i", audioPath,
                    "-c", "copy",
                    "-f", container,
                    outputPath
                }
            },
            EnableRaisingEvents = true
        };

        process.Start();

        // Drain both pipes: an unread buffer can fill and deadlock ffmpeg.
        var outputRead = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var errorRead = process.StandardError.ReadToEndAsync(CancellationToken.None);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to kill the muxing ffmpeg process");
                }
            }

            throw;
        }

        await Task.WhenAll(outputRead, errorRead).ConfigureAwait(false);

        if (process.ExitCode == 0)
        {
            return string.Empty;
        }

        var stderr = await errorRead.ConfigureAwait(false);
        _logger.LogWarning("ffmpeg exited with {Code} while assembling the video: {Error}", process.ExitCode, stderr);
        TryDelete(outputPath);
        return "The downloaded video and audio could not be assembled into one file.";
    }

    private static string TooLarge(long bytes, long maxBytes) => string.Create(
        CultureInfo.InvariantCulture,
        $"The selected streams total {bytes / (1024 * 1024)} MB, over the {maxBytes / (1024 * 1024)} MB limit for a theme.");

    private static (string Path, string Error) EnsureNonEmpty(string path)
        => File.Exists(path) && new FileInfo(path).Length > 0
            ? (path, string.Empty)
            : (string.Empty, "The extractor produced no media file.");

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // A leftover file in the caller's scratch directory is removed with it.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
