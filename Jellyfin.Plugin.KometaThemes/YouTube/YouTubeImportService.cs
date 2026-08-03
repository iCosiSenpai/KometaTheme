using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KometaThemes.YouTube;

/// <summary>
/// Fetches YouTube media to a local temporary file using an external <c>yt-dlp</c> binary.
/// </summary>
/// <remarks>
/// <para>
/// An external process rather than a managed library, for two reasons. First, the plugin's release
/// artifact is a single DLL with no side-by-side dependencies, so any NuGet package added here would
/// simply not be present at runtime. Second, YouTube's player changes frequently; yt-dlp is updated
/// continuously by its own maintainers, whereas a pinned managed extractor would break on the
/// server's schedule and require a plugin release to fix.
/// </para>
/// <para>
/// The trade-off is that yt-dlp must be installed in the Jellyfin environment. That is reported
/// explicitly through <see cref="GetAvailability"/> so the UI can say so instead of failing opaquely.
/// </para>
/// </remarks>
public sealed class YouTubeImportService
{
    /// <summary>
    /// Ceiling on the download, so a mistakenly pasted full-episode link cannot fill the disk.
    /// </summary>
    private const int MaxFileSizeMegabytes = 512;

    /// <summary>
    /// Themes are short. Rejecting long videos up front avoids a large pointless download.
    /// </summary>
    private const int MaxDurationSeconds = 1800;

    /// <summary>
    /// Overall budget for one extraction, which includes yt-dlp's own retries.
    /// </summary>
    private const int DownloadTimeoutSeconds = 600;

    private const int MaxErrorLength = 400;

    /// <summary>
    /// Prefers a VP9/webm video stream so the theme file can be produced by a stream copy rather
    /// than a re-encode, falling back through progressively looser choices.
    /// </summary>
    private const string VideoFormatSelector =
        "bestvideo[ext=webm][height<=1080]+bestaudio[ext=webm]/" +
        "bestvideo[vcodec^=vp9][height<=1080]+bestaudio/" +
        "best[ext=webm][height<=1080]/" +
        "bestvideo[height<=1080]+bestaudio/best";

    /// <summary>
    /// Audio-only selector. The stream is re-encoded to mp3 regardless, so the container does not
    /// matter here.
    /// </summary>
    private const string AudioFormatSelector = "bestaudio/best";

    /// <summary>
    /// Locations checked when no explicit path is configured. Covers the common
    /// distro/pip/linuxserver-mod install targets plus Windows.
    /// </summary>
    private static readonly string[] CandidatePaths =
    [
        "/usr/local/bin/yt-dlp",
        "/usr/bin/yt-dlp",
        "/bin/yt-dlp",
        "/opt/bin/yt-dlp",
        "/usr/local/bin/yt-dlp_linux",
        "/config/yt-dlp",
        "yt-dlp",
        "yt-dlp.exe"
    ];

    private readonly ILogger<YouTubeImportService> _logger;
    private readonly IApplicationPaths _applicationPaths;

    /// <summary>
    /// Initializes a new instance of the <see cref="YouTubeImportService"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="applicationPaths">Server paths, used for the scratch directory.</param>
    public YouTubeImportService(ILogger<YouTubeImportService> logger, IApplicationPaths applicationPaths)
    {
        _logger = logger;
        _applicationPaths = applicationPaths;
    }

    /// <summary>
    /// Reports whether the extractor is usable, and where it was found.
    /// </summary>
    /// <returns>Availability details for the UI.</returns>
    public YouTubeAvailability GetAvailability()
    {
        var configured = Plugin.Instance?.Configuration?.YtDlpPath;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return File.Exists(configured)
                ? new YouTubeAvailability(true, configured, string.Empty)
                : new YouTubeAvailability(false, configured, "The configured yt-dlp path does not exist.");
        }

        var resolved = ResolveExecutable();
        return resolved != null
            ? new YouTubeAvailability(true, resolved, string.Empty)
            : new YouTubeAvailability(
                false,
                string.Empty,
                "yt-dlp was not found. Install it in the Jellyfin environment, or set its full path in the plugin settings.");
    }

    /// <summary>
    /// Downloads a YouTube video to a temporary file.
    /// </summary>
    /// <param name="videoId">A video ID already validated by <see cref="YouTubeUrlParser"/>.</param>
    /// <param name="audioOnly">When true, fetch the best audio-only stream instead of a muxed file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The download result. The caller owns and must delete the produced file.</returns>
    public async Task<YouTubeDownloadResult> DownloadAsync(string videoId, bool audioOnly, CancellationToken cancellationToken)
    {
        if (!YouTubeUrlParser.IsValidVideoId(videoId))
        {
            // Defence in depth: this is validated at the endpoint, but the value ends up in an
            // argument list, so it is re-checked at the boundary that actually spawns the process.
            return YouTubeDownloadResult.Failure("Invalid YouTube video ID.");
        }

        var availability = GetAvailability();
        if (!availability.Available)
        {
            return YouTubeDownloadResult.Failure(availability.Error);
        }

        var workDirectory = Path.Combine(_applicationPaths.CachePath, "kometathemes-yt", Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(workDirectory);

        try
        {
            var outputTemplate = Path.Combine(workDirectory, "source.%(ext)s");
            var arguments = new List<string>
            {
                // Never read from stdin, never touch the user's config or archive files, and never
                // let a playlist URL expand into many downloads.
                "--ignore-config",
                "--no-playlist",
                "--no-progress",
                "--no-warnings",
                "--no-continue",
                "--no-part",
                "--restrict-filenames",
                "--print-json",
                "--socket-timeout", "30",
                "--retries", "3",
                "--max-filesize", MaxFileSizeArgument(),
                "--match-filter", string.Create(CultureInfo.InvariantCulture, $"duration < {MaxDurationSeconds}"),
                "-f", audioOnly ? AudioFormatSelector : VideoFormatSelector,
                "-o", outputTemplate
            };

            if (!audioOnly)
            {
                // Ask for a webm container so the video stream can usually be copied straight into
                // the theme file. YouTube serves VP9/Opus for essentially everything modern, but the
                // caller still has a re-encode fallback for the cases where it cannot.
                arguments.Add("--merge-output-format");
                arguments.Add("webm");
            }

            // "--" then the canonical URL this process builds itself: the user's string never
            // reaches the argument list, so it cannot be read as an option.
            arguments.Add("--");
            arguments.Add(YouTubeUrlParser.BuildWatchUrl(videoId));

            var (exitCode, stdout, stderr) = await RunProcessAsync(availability.ExecutablePath, arguments, cancellationToken).ConfigureAwait(false);

            if (exitCode != 0)
            {
                var reason = SummarizeError(stderr);
                _logger.LogWarning("yt-dlp exited with {Code} for video {VideoId}: {Error}", exitCode, videoId, reason);
                return YouTubeDownloadResult.Failure(reason);
            }

            var produced = Directory.GetFiles(workDirectory)
                .OrderByDescending(path => new FileInfo(path).Length)
                .FirstOrDefault();

            if (produced == null || new FileInfo(produced).Length == 0)
            {
                return YouTubeDownloadResult.Failure("The extractor reported success but produced no media file.");
            }

            var (title, duration) = ParseMetadata(stdout);
            _logger.LogInformation(
                "Fetched YouTube {VideoId} ({Title}, {Duration}s, {Bytes} bytes)",
                videoId,
                title,
                duration,
                new FileInfo(produced).Length);

            return new YouTubeDownloadResult(produced, title, duration, string.Empty);
        }
        catch (OperationCanceledException)
        {
            TryCleanup(workDirectory);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch YouTube video {VideoId}", videoId);
            TryCleanup(workDirectory);
            return YouTubeDownloadResult.Failure(ex.Message);
        }
    }

    /// <summary>
    /// Removes the scratch directory a download was staged in.
    /// </summary>
    /// <param name="filePath">A file path previously returned by <see cref="DownloadAsync"/>.</param>
    public void CleanupDownload(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        // Only ever remove directories inside our own scratch root.
        var scratchRoot = Path.GetFullPath(Path.Combine(_applicationPaths.CachePath, "kometathemes-yt"));
        var target = Path.GetFullPath(directory);
        if (!target.StartsWith(scratchRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            _logger.LogWarning("Refusing to clean up a path outside the scratch directory: {Path}", target);
            return;
        }

        TryCleanup(target);
    }

    private void TryCleanup(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up scratch directory {Path}", directory);
        }
    }

    private static string? ResolveExecutable()
    {
        foreach (var candidate in CandidatePaths)
        {
            if (candidate.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                continue;
            }

            // Bare name: search PATH.
            var pathVariable = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathVariable))
            {
                continue;
            }

            foreach (var directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    var full = Path.Combine(directory.Trim(), candidate);
                    if (File.Exists(full))
                    {
                        return full;
                    }
                }
                catch (ArgumentException)
                {
                    // Malformed PATH entry — skip it.
                }
            }
        }

        return null;
    }

    private async Task<(int ExitCode, string StdOut, string StdErr)> RunProcessAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false
        };

        // ArgumentList, never a joined command string: the arguments are escaped by the runtime,
        // so nothing here can be reinterpreted as shell syntax.
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.Start();

        // Drain both pipes concurrently; yt-dlp writes a full JSON blob to stdout and can fill the
        // buffer, which would deadlock a sequential read.
        var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(DownloadTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
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
                    _logger.LogWarning(ex, "Failed to kill yt-dlp");
                }

                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }

            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);

            if (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return (-1, string.Empty, string.Create(CultureInfo.InvariantCulture, $"yt-dlp timed out after {DownloadTimeoutSeconds}s."));
            }

            throw;
        }

        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        return (process.ExitCode, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false));
    }

    private static (string Title, int DurationSeconds) ParseMetadata(string stdout)
    {
        // --print-json emits one JSON object per line.
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] != '{')
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(trimmed);
                var root = document.RootElement;
                var title = root.TryGetProperty("title", out var titleElement) && titleElement.ValueKind == JsonValueKind.String
                    ? titleElement.GetString() ?? string.Empty
                    : string.Empty;

                var duration = 0;
                if (root.TryGetProperty("duration", out var durationElement))
                {
                    duration = durationElement.ValueKind switch
                    {
                        JsonValueKind.Number => (int)durationElement.GetDouble(),
                        JsonValueKind.String when double.TryParse(durationElement.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => (int)parsed,
                        _ => 0
                    };
                }

                return (title, duration);
            }
            catch (JsonException)
            {
                // Not the metadata line — keep looking.
            }
        }

        return (string.Empty, 0);
    }

    /// <summary>
    /// Reduces yt-dlp's stderr to a single actionable line.
    /// </summary>
    private static string SummarizeError(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
        {
            return "The extractor failed without reporting a reason.";
        }

        var lines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();

        var error = Array.Find(lines, line => line.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase));
        var message = error ?? lines[^1];

        return message.Length > MaxErrorLength ? message[..MaxErrorLength] + "…" : message;
    }

    private static string MaxFileSizeArgument()
        => string.Create(CultureInfo.InvariantCulture, $"{MaxFileSizeMegabytes}M");
}
