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
/// Fetches YouTube media to a local temporary file, either with the managed extractor bundled with
/// the plugin or with an external <c>yt-dlp</c> binary.
/// </summary>
/// <remarks>
/// <para>
/// The bundled extractor is what makes the feature work on a stock Jellyfin install: nothing has to
/// be installed by hand, which is how comparable Jellyfin plugins behave. Its assemblies travel
/// inside the plugin package.
/// </para>
/// <para>
/// When <c>yt-dlp</c> is present it is used instead, because it is updated continuously against
/// YouTube's changes by its own maintainers, whereas the bundled extractor is pinned and only moves
/// when this plugin is released. So the default works out of the box, and installing yt-dlp buys
/// resilience without any configuration.
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
    /// Output template asking yt-dlp for a one-line JSON object with only the fields used here.
    /// </summary>
    private const string MetadataTemplate = "%(.{id,title,duration,ext})j";

    /// <summary>
    /// Video format preference. Ordered so that an already-muxed single file wins, then a webm
    /// pairing, then anything at or below 1080p. Whatever container comes back is carried through to
    /// the theme file, so no branch of this list forces a re-encode.
    /// </summary>
    private const string VideoFormatSelector =
        "best[ext=webm][height<=1080]/" +
        "best[ext=mp4][height<=1080]/" +
        "bestvideo[ext=webm][height<=1080]+bestaudio[ext=webm]/" +
        "bestvideo[ext=mp4][height<=1080]+bestaudio[ext=m4a]/" +
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
    private readonly ManagedYouTubeExtractor _managed;

    /// <summary>
    /// Initializes a new instance of the <see cref="YouTubeImportService"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="applicationPaths">Server paths, used for the scratch directory.</param>
    /// <param name="managed">The extractor bundled with the plugin.</param>
    public YouTubeImportService(
        ILogger<YouTubeImportService> logger,
        IApplicationPaths applicationPaths,
        ManagedYouTubeExtractor managed)
    {
        _logger = logger;
        _applicationPaths = applicationPaths;
        _managed = managed;
    }

    /// <summary>
    /// Reports which backend will be used, and whether an import can be attempted.
    /// </summary>
    /// <returns>Availability details for the UI.</returns>
    public YouTubeAvailability GetAvailability()
    {
        var configured = Plugin.Instance?.Configuration?.YtDlpPath;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            // An explicitly configured path that does not exist is a mistake worth reporting rather
            // than silently working around, since the administrator asked for that binary.
            return File.Exists(configured)
                ? new YouTubeAvailability(true, YouTubeAvailability.YtDlpBackend, configured, string.Empty)
                : new YouTubeAvailability(
                    false,
                    YouTubeAvailability.YtDlpBackend,
                    configured,
                    "The configured yt-dlp path does not exist.");
        }

        var resolved = ResolveExecutable();
        return resolved != null
            ? new YouTubeAvailability(true, YouTubeAvailability.YtDlpBackend, resolved, string.Empty)
            : new YouTubeAvailability(true, YouTubeAvailability.BundledBackend, string.Empty, string.Empty);
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
            if (!availability.UsesYtDlp)
            {
                var managedResult = await _managed.DownloadAsync(
                    videoId,
                    audioOnly,
                    workDirectory,
                    MaxDurationSeconds,
                    (long)MaxFileSizeMegabytes * 1024 * 1024,
                    cancellationToken).ConfigureAwait(false);

                if (!managedResult.Success)
                {
                    TryCleanup(workDirectory);
                }

                return managedResult;
            }

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

                // A compact JSON object of just the fields needed. The old --print-json emitted the
                // full info dict — around 600 KB for a single video, almost all of it the format
                // list — and is a deprecated alias. This variant is one line and stays correctly
                // escaped for titles containing quotes or non-ASCII text.
                "--no-simulate",
                "-O", MetadataTemplate,

                "--socket-timeout", "30",
                "--retries", "3",
                "--max-filesize", MaxFileSizeArgument(),

                // Plural: --match-filter is a deprecated alias.
                "--match-filters", string.Create(CultureInfo.InvariantCulture, $"duration < {MaxDurationSeconds}"),
                "-f", audioOnly ? AudioFormatSelector : VideoFormatSelector,
                "-o", outputTemplate
            };

            if (!audioOnly)
            {
                // Prefer a single already-muxed stream so no merge step is needed at all, and let
                // the container be whatever the chosen format uses. Forcing webm here used to mean
                // an H.264 source had to be re-encoded to VP9 before it could be written, which
                // measured at roughly 4x realtime on a 4-core machine — minutes of pegged CPU for
                // one theme. The container is now carried through and the stream simply copied.
                arguments.Add("--merge-output-format");
                arguments.Add("webm/mp4");
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
        // -O emits one JSON object per selected video. Scan for it rather than assuming a line
        // position, since yt-dlp may also print unrelated notices.
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
