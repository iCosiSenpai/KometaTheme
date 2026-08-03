using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.KometaThemes.Api;
using Jellyfin.Plugin.KometaThemes.Configuration;
using Jellyfin.Plugin.KometaThemes.Models;
using Jellyfin.Plugin.KometaThemes.Resolving;
using Jellyfin.Plugin.KometaThemes.Sync;
using Jellyfin.Plugin.KometaThemes.YouTube;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using VideoModel = Jellyfin.Plugin.KometaThemes.Models.Video;

#pragma warning disable SA1611, SA1615, CS1591, CA3003

namespace Jellyfin.Plugin.KometaThemes.Search;

[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("Plugins/KometaThemes")]
public sealed class KometaThemesSearchController : ControllerBase
{
    private const string ThemeMusicDirectory = "theme-music";
    private const string ThemeVideoDirectory = "backdrops";
    private const int MaxThemeNameLength = 80;
    private const int MaxSearchResults = 8;
    private const int MinimumConfidentScore = 62;
    private const int StrongMatchScore = 78;
    private const int ExactMatchScore = 100;
    private const int ManualDownloadParallelism = 2;

    /// <summary>
    /// Upper bound for a user-supplied OP/ED sequence number.
    /// </summary>
    private const int MaxThemeSequence = 99;

    private static readonly HashSet<string> SearchNoiseWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a",
        "an",
        "and",
        "cour",
        "episode",
        "movie",
        "no",
        "ova",
        "part",
        "season",
        "special",
        "the",
        "tv"
    };

    private static readonly HashSet<string> ReservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    private readonly AnimeThemesApi _api;
    private readonly AnimeThemesDownloader _downloader;
    private readonly ILibraryManager _libraryManager;
    private readonly FailedItemsStore _failedItems;
    private readonly YouTubeImportService _youTube;
    private readonly ThemeLinkRepairService _linkRepair;
    private readonly ILogger<KometaThemesSearchController> _logger;

    public KometaThemesSearchController(
        AnimeThemesApi api,
        AnimeThemesDownloader downloader,
        ILibraryManager libraryManager,
        FailedItemsStore failedItems,
        YouTubeImportService youTube,
        ThemeLinkRepairService linkRepair,
        ILogger<KometaThemesSearchController> logger)
    {
        _api = api;
        _downloader = downloader;
        _libraryManager = libraryManager;
        _failedItems = failedItems;
        _youTube = youTube;
        _linkRepair = linkRepair;
        _logger = logger;
    }

    [HttpGet("Search")]
    public async Task<IActionResult> Search([FromQuery] string title, [FromQuery] int? year = null, [FromQuery] string? itemId = null)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return BadRequest(new { error = "Title is required." });
        }

        var queryTitle = title.Trim();
        var item = TryGetSearchItem(itemId);
        if (item != null)
        {
            var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
            if (!LibrarySelection.IsItemEligible(item, _libraryManager, config))
            {
                _logger.LogWarning("Theme Finder search rejected for non-eligible item {ItemId}", itemId);
                var errorMsg = LibrarySelection.GetNotEligibleErrorMessage();
                return BadRequest(new { error = errorMsg });
            }
        }

        var trustedKeys = BuildTrustedSearchKeys(queryTitle, item);
        var results = await _api.SearchByTitleAsync(queryTitle, year, HttpContext.RequestAborted).ConfigureAwait(false);
        var scoredResults = results
            .Select(result => ScoreSearchCandidate(result, trustedKeys, year))
            .OrderByDescending(result => result.Score)
            .ThenByDescending(result => result.YearMatches)
            .ThenBy(result => result.TitleLengthDelta)
            .ToArray();
        var filteredResults = scoredResults
            .Where(result => result.Score >= MinimumConfidentScore)
            .Take(MaxSearchResults)
            .ToArray();
        var retriedTitle = string.Empty;

        if (filteredResults.Length == 0 &&
            item != null &&
            ShouldRetryWithItemName(queryTitle, item.Name))
        {
            var retryTitle = CleanTitle(item.Name);
            if (!string.IsNullOrWhiteSpace(retryTitle))
            {
                retriedTitle = retryTitle;
                results = await _api.SearchByTitleAsync(retryTitle, year, HttpContext.RequestAborted).ConfigureAwait(false);
                scoredResults = results
                    .Select(result => ScoreSearchCandidate(result, trustedKeys, year))
                    .OrderByDescending(result => result.Score)
                    .ThenByDescending(result => result.YearMatches)
                    .ThenBy(result => result.TitleLengthDelta)
                    .ToArray();
                filteredResults = scoredResults
                    .Where(result => result.Score >= MinimumConfidentScore)
                    .Take(MaxSearchResults)
                    .ToArray();
            }
        }

        var broadResults = filteredResults.Length == 0
            ? scoredResults.Take(MaxSearchResults).ToArray()
            : [];
        filteredResults = await HydrateSearchCandidatesAsync(filteredResults, HttpContext.RequestAborted).ConfigureAwait(false);
        broadResults = await HydrateSearchCandidatesAsync(broadResults, HttpContext.RequestAborted).ConfigureAwait(false);

        _logger.LogInformation(
            "Theme Finder search title='{Title}' year={Year} itemId={ItemId} trustedKeys={TrustedKeys} raw={RawCount} filtered={FilteredCount}",
            queryTitle,
            year,
            itemId,
            string.Join(" | ", trustedKeys.Select(key => key.Value)),
            results.Length,
            filteredResults.Length);

        foreach (var candidate in scoredResults.Take(5))
        {
            _logger.LogInformation(
                "Theme Finder candidate id={Id} name='{Name}' slug='{Slug}' year={Year} score={Score} confidence={Confidence}",
                candidate.Anime.Id,
                candidate.Anime.Name,
                candidate.Anime.Slug,
                candidate.Anime.Year,
                candidate.Score,
                GetConfidence(candidate.Score));
        }

        return Ok(new
        {
            results = filteredResults.Select(candidate => ToSearchResult(candidate, broad: false)),
            broadResults = broadResults.Select(candidate => ToSearchResult(candidate, broad: true)),
            rawCount = results.Length,
            filteredCount = filteredResults.Length,
            trustedKeys = trustedKeys.Select(key => key.Value),
            retriedTitle
        });
    }

    [HttpGet("Anime/{id}/themes")]
    public async Task<IActionResult> GetAnimeThemes(int id, [FromQuery] string itemId, [FromQuery] string? slug = null)
    {
        if (!Guid.TryParse(itemId, out var gid))
        {
            return BadRequest(new { error = "Invalid itemId." });
        }

        var anime = !string.IsNullOrWhiteSpace(slug)
            ? await _api.GetAnimeBySlugAsync(slug, HttpContext.RequestAborted).ConfigureAwait(false)
            : await _api.GetAnimeByIdAsync(id, HttpContext.RequestAborted).ConfigureAwait(false);
        if (anime == null)
        {
            return NotFound(new { error = "Anime not found." });
        }

        var item = _libraryManager.GetItemById(gid);
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (item != null)
        {
            foreach (var s in item.GetThemeSongs())
            {
                if (s.Path != null)
                {
                    existing.Add(System.IO.Path.GetFileName(s.Path));
                }
            }

            foreach (var v in item.GetThemeVideos())
            {
                if (v.Path != null)
                {
                    existing.Add(System.IO.Path.GetFileName(v.Path));
                }
            }
        }

        var audioVolume = (int)Math.Round(Plugin.Instance?.Configuration?.AudioSettings.Volume * 100 ?? 50);
        var videoVolume = (int)Math.Round(Plugin.Instance?.Configuration?.VideoSettings.Volume * 100 ?? 50);
        var audioSuffix = $"__{audioVolume}";
        var videoSuffix = $"__{videoVolume}";

        var themes = new List<ThemeRow>();
        var seenThemeRows = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var theme in anime.Themes ?? new Collection<AnimeTheme>())
        {
            foreach (var entry in theme.Entries ?? new Collection<AnimeThemeEntry>())
            {
                var entryVideos = entry.Videos;
                if (entryVideos == null)
                {
                    continue;
                }

                foreach (var video in entryVideos)
                {
                    if (video.Audio == null)
                    {
                        continue;
                    }

                    var mediaKey = string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $"{theme.Id}:{entry.Id}:{video.Id}:{video.Audio.Id}");
                    if (!seenThemeRows.Add(mediaKey))
                    {
                        continue;
                    }

                    var slugValue = theme.Slug ?? "theme";
                    var label = BuildThemeLabel(theme);
                    var title = SlugToTitle(slugValue);
                    var name = BuildThemeDownloadName(label, title);
                    var audioFile = $"{name}{audioSuffix}.mp3";
                    var videoFile = $"{name}{videoSuffix}.webm";
                    var audioOk = existing.Any(e => e.Contains(audioFile, StringComparison.OrdinalIgnoreCase));
                    var videoOk = existing.Any(e => e.Contains(videoFile, StringComparison.OrdinalIgnoreCase));

                    themes.Add(new ThemeRow(
                        Type: theme.Type.ToString(),
                        Sequence: theme.Sequence,
                        Label: label,
                        Slug: slugValue,
                        Title: title,
                        Episodes: entry.Episodes ?? string.Empty,
                        Version: entry.Version?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                        Source: video.Source?.ToString() ?? string.Empty,
                        Resolution: video.Resolution?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                        Creditless: video.Creditless,
                        Overlap: video.Overlap.ToString(),
                        VideoBasename: video.Basename,
                        EntryId: entry.Id,
                        VideoId: video.Id,
                        AudioUrl: video.Audio.Link,
                        VideoUrl: video.Link,
                        AudioDownloaded: audioOk,
                        VideoDownloaded: videoOk));
                }
            }
        }

        var seasonGroups = GroupThemesIntoSeasons(themes);

        return Ok(new
        {
            anime = new
            {
                id = anime.Id,
                name = anime.Name,
                slug = anime.Slug,
                year = anime.Year,
                season = anime.Season ?? string.Empty,
                mediaFormat = anime.MediaFormat ?? string.Empty,
                imageUrl = GetImageUrl(anime, large: false),
                largeImageUrl = GetImageUrl(anime, large: true),
                synopsis = anime.Synopsis ?? string.Empty
            },
            audioVolume,
            videoVolume,
            themes,
            seasonGroups
        });
    }

    [HttpPost("Items/{itemId}/download")]
    public async Task<IActionResult> DownloadThemes(string itemId, [FromBody] DownloadRequest request, CancellationToken ct)
    {
        if (request?.Urls == null || request.Urls.Count == 0)
        {
            _logger.LogWarning("Theme Finder download rejected for itemId={ItemId}: no theme URLs were supplied.", itemId);
            return BadRequest(new { error = "No themes specified." });
        }

        const int MaxManualDownloadBatch = 64;
        if (request.Urls.Count > MaxManualDownloadBatch)
        {
            _logger.LogWarning("Theme Finder download rejected for itemId={ItemId}: batch too large ({Count} > {Max}).", itemId, request.Urls.Count, MaxManualDownloadBatch);
            return BadRequest(new { error = $"Too many themes requested (max {MaxManualDownloadBatch})." });
        }

        if (!Guid.TryParse(itemId, out var gid))
        {
            return BadRequest(new { error = "Invalid itemId." });
        }

        var item = _libraryManager.GetItemById(gid);
        if (item == null)
        {
            return NotFound(new { error = "Item not found." });
        }

        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        if (!LibrarySelection.IsItemEligible(item, _libraryManager, config))
        {
            _logger.LogWarning("Theme Finder action rejected for non-eligible item {ItemId}", itemId);
            var errorMsg = LibrarySelection.GetNotEligibleErrorMessage();
            return BadRequest(new { error = errorMsg });
        }

        if (string.IsNullOrWhiteSpace(item.ContainingFolderPath))
        {
            return BadRequest(new { error = "Selected item has no writable media folder." });
        }

        using var requestGate = new SemaphoreSlim(ManualDownloadParallelism, ManualDownloadParallelism);

        var validationError = ValidateDownloadRequest(request, item, out var downloadItems);
        if (validationError != null)
        {
            _logger.LogWarning(
                "Theme Finder download rejected for itemId={ItemId}: invalid selection payload with {Count} requested themes.",
                itemId,
                request.Urls.Count);
            return BadRequest(validationError);
        }

        // Downloads are serialized here per request; the shared TranscodeGate inside the downloader
        // is what bounds ffmpeg across all requests.
        var results = new ConcurrentBag<object>();
        var tasks = downloadItems.Select(async theme =>
        {
            await requestGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var outcome = await _downloader.DownloadSingle(theme.MediaType, theme.Url, item, theme.RelativePath, theme.Volume, ct).ConfigureAwait(false);
                results.Add(new
                {
                    url = theme.Url,
                    name = theme.ThemeName,
                    success = outcome != DownloadOutcome.Failed,
                    skipped = outcome == DownloadOutcome.Skipped
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to download {Url}", theme.Url);
                results.Add(new { url = theme.Url, name = theme.ThemeName, success = false, error = ex.Message });
            }
            finally
            {
                requestGate.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);

        // Persist the manual binding so automatic syncs never re-resolve this item.
        if (request.AnimeId.HasValue)
        {
            SaveManualBinding(item, request.AnimeId.Value, request.AnimeName, request.AnimeSlug);
            _failedItems.Remove(item.Id);
        }

        try
        {
            await item.RefreshMetadata(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh metadata");
        }

        return Ok(new { results });
    }

    /// <summary>
    /// Reports whether YouTube import is enabled and which extractor backend will be used.
    /// </summary>
    /// <returns>Availability details for the Theme Finder UI.</returns>
    [HttpGet("YouTube/status")]
    public IActionResult GetYouTubeStatus()
    {
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var availability = _youTube.GetAvailability();

        return Ok(new
        {
            enabled = config.EnableYouTubeImport,
            available = availability.Available,
            backend = availability.Backend,
            executablePath = availability.Available ? availability.ExecutablePath : string.Empty,
            error = availability.Error
        });
    }

    /// <summary>
    /// Imports a theme for an item from a YouTube link.
    /// </summary>
    /// <remarks>
    /// The link is reduced to a canonical 11-character video ID and rebuilt server-side, so the
    /// user-supplied string never reaches the extractor's argument list. The produced files are
    /// recorded with <see cref="ThemeSource.YouTube"/> so a later sync — which can only ever
    /// recompute the expected file set from animethemes.moe — does not classify them as orphans and
    /// delete them.
    /// </remarks>
    /// <param name="itemId">Target library item.</param>
    /// <param name="request">Import parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Per-file results.</returns>
    [HttpPost("Items/{itemId}/youtube")]
    public async Task<IActionResult> ImportFromYouTube(string itemId, [FromBody] YouTubeImportRequest? request, CancellationToken ct)
    {
        if (request == null)
        {
            return BadRequest(new { error = "A YouTube link is required." });
        }

        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        if (!config.EnableYouTubeImport)
        {
            return BadRequest(new { error = "YouTube import is disabled. Enable it in the KometaThemes settings first." });
        }

        if (!Enum.IsDefined(request.ThemeType) || !Enum.IsDefined(request.Format))
        {
            return BadRequest(new { error = "Invalid themeType or format." });
        }

        if (!YouTubeUrlParser.TryGetVideoId(request.Url, out var videoId))
        {
            return BadRequest(new { error = "That does not look like a YouTube link. Paste a youtube.com or youtu.be video URL." });
        }

        var sequence = Math.Clamp(request.Sequence, 1, MaxThemeSequence);

        if (!Guid.TryParse(itemId, out var gid))
        {
            return BadRequest(new { error = "Invalid itemId." });
        }

        var item = _libraryManager.GetItemById(gid);
        if (item == null)
        {
            return NotFound(new { error = "Item not found." });
        }

        if (!LibrarySelection.IsItemEligible(item, _libraryManager, config))
        {
            _logger.LogWarning("YouTube import rejected for non-eligible item {ItemId}", itemId);
            return BadRequest(new { error = LibrarySelection.GetNotEligibleErrorMessage() });
        }

        if (string.IsNullOrWhiteSpace(item.ContainingFolderPath))
        {
            return BadRequest(new { error = "Selected item has no writable media folder." });
        }

        var availability = _youTube.GetAvailability();
        if (!availability.Available)
        {
            return BadRequest(new { error = availability.Error });
        }

        var wantsAudio = request.Format is ThemeImportFormat.Audio or ThemeImportFormat.Both;
        var wantsVideo = request.Format is ThemeImportFormat.Video or ThemeImportFormat.Both;

        // Audio-only needs no video stream; anything that also wants a video theme must fetch the
        // muxed file once and derive both outputs from it.
        var download = await _youTube.DownloadAsync(videoId, audioOnly: !wantsVideo, ct).ConfigureAwait(false);
        if (!download.Success)
        {
            return BadRequest(new { error = download.Error });
        }

        try
        {
            var displayTitle = !string.IsNullOrWhiteSpace(request.Title) ? request.Title : download.Title;
            var results = new List<object>();

            if (wantsAudio)
            {
                results.Add(await ImportOneAsync(
                    MediaType.Audio,
                    download.FilePath,
                    item,
                    request,
                    sequence,
                    displayTitle,
                    videoId,
                    config.AudioSettings.Volume,
                    ct).ConfigureAwait(false));
            }

            if (wantsVideo)
            {
                results.Add(await ImportOneAsync(
                    MediaType.Video,
                    download.FilePath,
                    item,
                    request,
                    sequence,
                    displayTitle,
                    videoId,
                    config.VideoSettings.Volume,
                    ct).ConfigureAwait(false));
            }

            try
            {
                await item.RefreshMetadata(ct).ConfigureAwait(false);
                await _linkRepair.RepairAsync(item, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // The files are on disk either way; only the Jellyfin-side linking is best-effort.
                _logger.LogWarning(ex, "Failed to refresh metadata after YouTube import for {ItemId}", itemId);
            }

            return Ok(new { title = displayTitle, videoId, results });
        }
        finally
        {
            _youTube.CleanupDownload(download.FilePath);
        }
    }

    private async Task<object> ImportOneAsync(
        MediaType mediaType,
        string sourceFile,
        BaseItem item,
        YouTubeImportRequest request,
        int sequence,
        string displayTitle,
        string videoId,
        double volume,
        CancellationToken ct)
    {
        var themeName = BuildImportedThemeName(request.ThemeType, sequence, displayTitle);

        // Carry the source container into the target name so the write is a stream copy.
        var sourceExtension = Path.GetExtension(sourceFile);
        if (!TryBuildRelativePath(item, mediaType, themeName, out var relativePath, volume, sourceExtension))
        {
            return new
            {
                mediaType = mediaType.ToString().ToLowerInvariant(),
                success = false,
                error = "Could not build a valid file name for this theme."
            };
        }

        var record = new DownloadRecord
        {
            // Synthetic negative id, kept out of the animethemes.moe id space so an imported theme
            // can never evict a real theme's tracker record (or be evicted by one).
            ThemeId = -Math.Abs(HashCode.Combine(videoId, mediaType, sequence, request.ThemeType)),
            Type = request.ThemeType,
            Sequence = sequence,
            Slug = videoId,
            SeasonNumber = Math.Max(0, request.SeasonNumber),
            DownloadedAt = DateTime.UtcNow,
            ItemId = item.Id,
            Source = ThemeSource.YouTube,
            Directory = mediaType == MediaType.Audio ? ThemeMusicDirectory : ThemeVideoDirectory,
            SourceUrl = YouTubeUrlParser.BuildWatchUrl(videoId)
        };

        try
        {
            var outcome = await _downloader.ImportLocalFileAsync(mediaType, sourceFile, item, relativePath, volume, record, ct).ConfigureAwait(false);
            return new
            {
                mediaType = mediaType.ToString().ToLowerInvariant(),
                fileName = Path.GetFileName(relativePath),
                success = outcome != DownloadOutcome.Failed,
                skipped = outcome == DownloadOutcome.Skipped
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "YouTube import failed for {ItemId} ({MediaType})", item.Id, mediaType);
            return new
            {
                mediaType = mediaType.ToString().ToLowerInvariant(),
                success = false,
                error = ex.Message
            };
        }
    }

    /// <summary>
    /// Builds the display name for an imported theme, matching the sync path's
    /// <c>OP1 - Title</c> convention.
    /// </summary>
    private static string BuildImportedThemeName(ThemeType themeType, int sequence, string title)
    {
        var prefix = string.Create(
            CultureInfo.InvariantCulture,
            $"{(themeType == ThemeType.OP ? "OP" : "ED")}{sequence}");

        var cleanTitle = SanitizeThemeName(title);

        // SanitizeThemeName returns the "theme" placeholder for an empty input; in that case the
        // prefix alone is a better name than "OP1 - theme".
        if (string.IsNullOrWhiteSpace(title) || string.Equals(cleanTitle, "theme", StringComparison.Ordinal))
        {
            return prefix;
        }

        var combined = prefix + " - " + cleanTitle;
        return combined.Length > MaxThemeNameLength ? combined[..MaxThemeNameLength].TrimEnd() : combined;
    }

    private static void SaveManualBinding(BaseItem item, int animeId, string animeName, string animeSlug)
    {
        var itemId = item.Id.ToString();
        Plugin.MutateConfiguration(config =>
        {
            var existing = config.ManualBindings.FirstOrDefault(b =>
                string.Equals(b.ItemId, itemId, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                config.ManualBindings.Remove(existing);
            }

            config.ManualBindings.Add(new Models.ManualBindingEntry
            {
                ItemId = itemId,
                AnimeId = animeId,
                AnimeName = animeName ?? item.Name,
                Slug = animeSlug,
                BoundAt = DateTime.UtcNow,
                Source = "ThemeFinder"
            });

            config.TrimManualBindings();
        });
    }

    [HttpGet("Items/{itemId}/info")]
    public IActionResult GetItemInfo(string itemId)
    {
        if (!Guid.TryParse(itemId, out var gid))
        {
            return BadRequest(new { error = "Invalid itemId" });
        }

        var item = _libraryManager.GetItemById(gid);
        if (item == null)
        {
            return NotFound(new { error = "Item not found" });
        }

        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        if (!LibrarySelection.IsItemEligible(item, _libraryManager, config))
        {
            var errorMsg = LibrarySelection.GetNotEligibleErrorMessage();
            return BadRequest(new { error = errorMsg });
        }

        var songsOnDisk = 0;
        var videosOnDisk = 0;
        if (!string.IsNullOrWhiteSpace(item.ContainingFolderPath))
        {
            var musicDir = System.IO.Path.Combine(item.ContainingFolderPath, "theme-music");
            var videoDir = System.IO.Path.Combine(item.ContainingFolderPath, "backdrops");
            songsOnDisk = System.IO.Directory.Exists(musicDir) ? System.IO.Directory.GetFiles(musicDir, "*.mp3").Length : 0;
            if (System.IO.File.Exists(System.IO.Path.Combine(item.ContainingFolderPath, "theme.mp3")))
            {
                songsOnDisk++;
            }

            videosOnDisk = ThemeFileKinds.EnumerateFiles(videoDir, audio: false).Length;
        }

        return Ok(new
        {
            id = item.Id.ToString(),
            name = item.Name,
            originalTitle = item.OriginalTitle ?? string.Empty,
            productionYear = item.ProductionYear,
            type = item.GetBaseItemKind().ToString(),
            directoryPath = item.ContainingFolderPath ?? string.Empty,
            filePath = item.Path ?? string.Empty,
            overview = item.Overview ?? string.Empty,
            themeStatus = new
            {
                songsOnDisk,
                videosOnDisk,
                registeredSongs = item.GetThemeSongs().Count,
                registeredVideos = item.GetThemeVideos().Count
            }
        });
    }

    private async Task<ScoredSearchResult[]> HydrateSearchCandidatesAsync(
        ScoredSearchResult[] candidates,
        CancellationToken cancellationToken)
    {
        var hydrated = new List<ScoredSearchResult>(candidates.Length);
        foreach (var candidate in candidates)
        {
            var detail = !string.IsNullOrWhiteSpace(candidate.Anime.Slug)
                ? await _api.GetAnimeBySlugAsync(candidate.Anime.Slug, cancellationToken).ConfigureAwait(false)
                : null;
            hydrated.Add(detail == null ? candidate : candidate with { Anime = detail });
        }

        return hydrated.ToArray();
    }

    private static string? GetImageUrl(Anime anime, bool large)
    {
        if (anime.Images == null || anime.Images.Count == 0)
        {
            return null;
        }

        var preferredFacet = large ? "Large Cover" : "Small Cover";
        return anime.Images
            .FirstOrDefault(image => string.Equals(image.Facet, preferredFacet, StringComparison.OrdinalIgnoreCase))
            ?.Link ??
            anime.Images.FirstOrDefault(image => !string.IsNullOrWhiteSpace(image.Link))?.Link;
    }

    private static string BuildThemeLabel(AnimeTheme theme)
    {
        var slug = theme.Slug ?? string.Empty;
        if (slug.Length >= 2 &&
            string.Equals(slug[..2], theme.Type.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return slug.ToUpperInvariant();
        }

        return theme.Sequence.HasValue
            ? string.Create(CultureInfo.InvariantCulture, $"{theme.Type}{theme.Sequence.Value}")
            : theme.Type.ToString();
    }

    private static string BuildThemeDownloadName(string label, string title)
    {
        if (string.IsNullOrWhiteSpace(title) ||
            string.Equals(label, title, StringComparison.OrdinalIgnoreCase))
        {
            return label;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{label} - {title}");
    }

    private static string SlugToTitle(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return string.Empty;
        }

        var words = slug.Split('-');
        var result = new List<string>();
        foreach (var word in words)
        {
            if (word.Length == 0)
            {
                continue;
            }

            if (word.Length == 1)
            {
                result.Add(word.ToUpperInvariant());
            }
            else
            {
                result.Add(char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant());
            }
        }

        return string.Join(" ", result);
    }

    private static List<object> GroupThemesIntoSeasons(List<ThemeRow> themeList)
    {
        var groups = new List<object>();

        var themesWithRanges = themeList
            .Select(t => (Theme: t, Ranges: EpisodeRangeParser.Parse(t.Episodes)))
            .ToList();

        var allRanges = themesWithRanges
            .SelectMany(t => t.Ranges)
            .Select(r => (r.StartEpisode, r.EndEpisode))
            .Distinct()
            .OrderBy(r => r.StartEpisode)
            .ToList();

        if (allRanges.Count == 0)
        {
            return groups;
        }

        var seasonNumber = 1;
        var usedRanges = new HashSet<(int, int)>();

        foreach (var range in allRanges)
        {
            if (usedRanges.Contains(range))
            {
                continue;
            }

            usedRanges.Add(range);

            var matching = themesWithRanges
                .Where(t => t.Ranges.Any(r => r.StartEpisode == range.StartEpisode && r.EndEpisode == range.EndEpisode))
                .Select(t => (object)t.Theme) // keep previous response shape (ThemeRow serializes with correct names)
                .ToList();

            if (matching.Count > 0)
            {
                var opCount = matching.Count(t => ((ThemeRow)t).Type == "OP");
                var edCount = matching.Count(t => ((ThemeRow)t).Type == "ED");
                groups.Add(new
                {
                    seasonNumber,
                    startEpisode = (int?)range.StartEpisode,
                    endEpisode = (int?)range.EndEpisode,
                    opCount,
                    edCount,
                    themes = matching
                });
                seasonNumber++;
            }
        }

        var unmatched = themesWithRanges
            .Where(t => t.Ranges.Count == 0)
            .Select(t => (object)t.Theme)
            .ToList();

        if (unmatched.Count > 0)
        {
            var opCount = unmatched.Count(t => ((ThemeRow)t).Type == "OP");
            var edCount = unmatched.Count(t => ((ThemeRow)t).Type == "ED");
            groups.Add(new
            {
                seasonNumber,
                startEpisode = (int?)null,
                endEpisode = (int?)null,
                opCount,
                edCount,
                themes = unmatched
            });
        }

        return groups;
    }

    private static bool ShouldRetryWithItemName(string queryTitle, string? itemName)
    {
        return IsMostlyNonLatin(queryTitle) &&
            !string.IsNullOrWhiteSpace(itemName) &&
            !string.Equals(NormalizeSearchText(queryTitle), NormalizeSearchText(itemName), StringComparison.Ordinal);
    }

    private static bool IsMostlyNonLatin(string value)
    {
        var letters = 0;
        var latin = 0;
        foreach (var character in value)
        {
            if (!char.IsLetter(character))
            {
                continue;
            }

            letters++;
            if ((character >= 'A' && character <= 'Z') || (character >= 'a' && character <= 'z'))
            {
                latin++;
            }
        }

        return letters > 0 && latin * 2 < letters;
    }

    private static string CleanTitle(string title)
    {
        return string.IsNullOrWhiteSpace(title) ? string.Empty : title.Trim();
    }

    private BaseItem? TryGetSearchItem(string? itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return null;
        }

        if (!Guid.TryParse(itemId, out var gid))
        {
            _logger.LogWarning("Theme Finder search ignored invalid itemId '{ItemId}'", itemId);
            return null;
        }

        return _libraryManager.GetItemById(gid);
    }

    private static SearchKey[] BuildTrustedSearchKeys(string queryTitle, BaseItem? item)
    {
        var keys = new List<SearchKey>();
        AddTrustedSearchKey(keys, queryTitle);

        if (item != null)
        {
            AddTrustedSearchKey(keys, item.Name);
            AddTrustedSearchKey(keys, item.OriginalTitle);
            AddTrustedSearchKey(keys, GetProviderValue(item, "TvdbSlug"));
            AddTrustedSearchKey(keys, ExtractAnimeClickSlug(GetProviderValue(item, "AnimeClick")));
        }

        return keys.ToArray();
    }

    private static string? GetProviderValue(BaseItem item, string key)
    {
        return item.ProviderIds != null && item.ProviderIds.TryGetValue(key, out var value) ? value : null;
    }

    private static string? ExtractAnimeClickSlug(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        var lastSlash = trimmed.LastIndexOf('/');
        if (lastSlash >= 0 && lastSlash < trimmed.Length - 1)
        {
            trimmed = trimmed[(lastSlash + 1)..];
        }

        var index = 0;
        while (index < trimmed.Length && char.IsDigit(trimmed[index]))
        {
            index++;
        }

        while (index < trimmed.Length && (trimmed[index] == '-' || trimmed[index] == '_' || trimmed[index] == '.'))
        {
            index++;
        }

        return index < trimmed.Length ? trimmed[index..] : trimmed;
    }

    private static void AddTrustedSearchKey(List<SearchKey> keys, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var normalized = NormalizeSearchText(value);
        if (normalized.Length == 0 || keys.Any(key => key.Normalized == normalized))
        {
            return;
        }

        keys.Add(new SearchKey(value.Trim(), normalized, TokenizeSearchText(value)));
    }

    private static ScoredSearchResult ScoreSearchCandidate(Anime anime, SearchKey[] trustedKeys, int? year)
    {
        var candidateName = NormalizeSearchText(anime.Name);
        var candidateSlug = NormalizeSearchText(anime.Slug);
        var candidateTokens = TokenizeSearchText(string.Create(CultureInfo.InvariantCulture, $"{anime.Name} {anime.Slug}"));
        var bestScore = 0;
        var bestLengthDelta = int.MaxValue;

        foreach (var key in trustedKeys)
        {
            var score = 0;
            if (candidateName == key.Normalized || candidateSlug == key.Normalized)
            {
                score = ExactMatchScore;
            }
            else
            {
                var coverage = CalculateTokenCoverage(key.Tokens, candidateTokens);
                var orderedCoverage = CalculateOrderedTokenCoverage(key.Tokens, candidateTokens);
                if (coverage >= 0.95)
                {
                    score = 86;
                }
                else if (coverage >= 0.80 && orderedCoverage >= 0.60)
                {
                    score = 74;
                }
                else if (coverage >= 0.65 && orderedCoverage >= 0.50)
                {
                    score = 62;
                }
                else if (key.Normalized.Length >= 8 && (candidateName.Contains(key.Normalized, StringComparison.Ordinal) || candidateSlug.Contains(key.Normalized, StringComparison.Ordinal)))
                {
                    score = 68;
                }
            }

            if (year.HasValue && anime.Year.HasValue)
            {
                var yearDelta = Math.Abs(anime.Year.Value - year.Value);
                if (yearDelta == 0)
                {
                    score += 8;
                }
                else if (yearDelta == 1)
                {
                    score += 3;
                }
                else if (yearDelta > 3)
                {
                    score -= 8;
                }
            }

            var lengthDelta = Math.Abs(candidateName.Length - key.Normalized.Length);
            if (score > bestScore || (score == bestScore && lengthDelta < bestLengthDelta))
            {
                bestScore = score;
                bestLengthDelta = lengthDelta;
            }
        }

        return new ScoredSearchResult(anime, Math.Max(0, bestScore), year.HasValue && anime.Year == year.Value, bestLengthDelta);
    }

    private static object ToSearchResult(ScoredSearchResult candidate, bool broad)
    {
        return new
        {
            id = candidate.Anime.Id,
            name = candidate.Anime.Name,
            slug = candidate.Anime.Slug,
            year = candidate.Anime.Year,
            season = candidate.Anime.Season ?? string.Empty,
            mediaFormat = candidate.Anime.MediaFormat ?? string.Empty,
            imageUrl = GetImageUrl(candidate.Anime, large: false),
            largeImageUrl = GetImageUrl(candidate.Anime, large: true),
            score = broad ? 0 : candidate.Score,
            confidence = broad ? "Weak" : GetConfidence(candidate.Score),
            broad
        };
    }

    private static string GetConfidence(int score)
    {
        if (score >= ExactMatchScore)
        {
            return "Exact";
        }

        if (score >= StrongMatchScore)
        {
            return "Strong";
        }

        return "Weak";
    }

    private static string NormalizeSearchText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    private static string[] TokenizeSearchText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var normalized = value.Normalize(NormalizationForm.FormD);
        var tokens = new List<string>();
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                continue;
            }

            AddToken(tokens, builder);
        }

        AddToken(tokens, builder);
        return tokens.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static void AddToken(List<string> tokens, StringBuilder builder)
    {
        if (builder.Length == 0)
        {
            return;
        }

        var token = builder.ToString();
        builder.Clear();
        if (token.Length > 1 && !SearchNoiseWords.Contains(token))
        {
            tokens.Add(token);
        }
    }

    private static double CalculateTokenCoverage(string[] expectedTokens, string[] candidateTokens)
    {
        if (expectedTokens.Length == 0)
        {
            return 0;
        }

        var candidateSet = new HashSet<string>(candidateTokens, StringComparer.Ordinal);
        var matches = expectedTokens.Count(candidateSet.Contains);
        return (double)matches / expectedTokens.Length;
    }

    private static double CalculateOrderedTokenCoverage(string[] expectedTokens, string[] candidateTokens)
    {
        if (expectedTokens.Length == 0)
        {
            return 0;
        }

        var expectedIndex = 0;
        foreach (var candidateToken in candidateTokens)
        {
            if (expectedIndex < expectedTokens.Length && string.Equals(candidateToken, expectedTokens[expectedIndex], StringComparison.Ordinal))
            {
                expectedIndex++;
            }
        }

        return (double)expectedIndex / expectedTokens.Length;
    }

    private static object? ValidateDownloadRequest(DownloadRequest request, BaseItem item, out List<ValidatedDownloadItem> downloadItems)
    {
        downloadItems = new List<ValidatedDownloadItem>();

        var config = Plugin.Instance?.Configuration;
        var audioVolume = config?.AudioSettings.Volume ?? 0.5;
        var videoVolume = config?.VideoSettings.Volume ?? 0.5;

        for (var i = 0; i < request.Urls.Count; i++)
        {
            var theme = request.Urls[i];
            if (theme == null)
            {
                return new { error = "Invalid theme selection.", index = i };
            }

            if (!TryParseManualMediaType(theme.MediaType, out var mediaType))
            {
                return new { error = "Invalid mediaType. Use audio or video.", index = i };
            }

            if (!TryCreateAnimeThemesUri(theme.Url, out var uri))
            {
                return new { error = "Invalid URL. Only absolute HTTPS animethemes.moe URLs are allowed.", index = i };
            }

            var themeVolume = mediaType == MediaType.Audio ? audioVolume : videoVolume;
            if (!TryBuildRelativePath(item, mediaType, theme.ThemeName, out var relativePath, themeVolume))
            {
                return new { error = "Invalid themeName.", index = i };
            }

            downloadItems.Add(new ValidatedDownloadItem(
                uri.AbsoluteUri,
                mediaType,
                relativePath,
                SanitizeThemeName(theme.ThemeName),
                themeVolume));
        }

        return null;
    }

    private static bool TryParseManualMediaType(string? value, out MediaType mediaType)
    {
        if (string.Equals(value, "audio", StringComparison.OrdinalIgnoreCase))
        {
            mediaType = MediaType.Audio;
            return true;
        }

        if (string.Equals(value, "video", StringComparison.OrdinalIgnoreCase))
        {
            mediaType = MediaType.Video;
            return true;
        }

        mediaType = MediaType.Audio;
        return false;
    }

    private static bool TryCreateAnimeThemesUri(string? value, out Uri uri)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out uri!) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !IsAllowedAnimeThemesHost(uri.Host))
        {
            return false;
        }

        return string.IsNullOrEmpty(uri.UserInfo);
    }

    private static bool IsAllowedAnimeThemesHost(string host)
    {
        return string.Equals(host, "animethemes.moe", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".animethemes.moe", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds a theme path relative to the item folder, constrained to the theme directories.
    /// </summary>
    /// <param name="item">Owning library item.</param>
    /// <param name="mediaType">Audio or video theme.</param>
    /// <param name="themeName">Display name to derive the file name from.</param>
    /// <param name="relativePath">The resulting path, relative to the item folder.</param>
    /// <param name="volume">Volume baked into the file name.</param>
    /// <param name="videoExtension">
    /// Video container to use, including the dot. Imported themes carry the container of whatever
    /// stream the extractor returned, so the file can be written with a stream copy instead of a
    /// re-encode. Sync downloads always pass null and get <c>.webm</c>, which is what
    /// animethemes.moe serves.
    /// </param>
    /// <returns>Whether a valid, contained path could be built.</returns>
    private static bool TryBuildRelativePath(
        BaseItem item,
        MediaType mediaType,
        string themeName,
        out string relativePath,
        double volume,
        string? videoExtension = null)
    {
        relativePath = string.Empty;
        var safeName = SanitizeThemeName(themeName);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            return false;
        }

        var isVideo = mediaType == MediaType.Video;
        var directory = isVideo ? ThemeVideoDirectory : ThemeMusicDirectory;
        var extension = isVideo ? NormalizeVideoExtension(videoExtension) : ".mp3";
        var suffix = $"__{(int)Math.Round(Math.Clamp(volume, 0.0, 1.0) * 100)}";
        var filename = safeName + suffix + extension;

        var baseDirectory = Path.GetFullPath(Path.Combine(item.ContainingFolderPath, directory));
        var targetPath = Path.GetFullPath(Path.Combine(baseDirectory, filename));
        var expectedPrefix = baseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (!targetPath.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        relativePath = Path.Combine(directory, filename);
        return true;
    }

    /// <summary>
    /// Constrains the theme video container to one Jellyfin resolves and ffmpeg can mux into with a
    /// stream copy. Anything unrecognised falls back to webm.
    /// </summary>
    private static string NormalizeVideoExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return ".webm";
        }

        var normalized = extension.StartsWith('.') ? extension : "." + extension;
        return normalized.ToLowerInvariant() switch
        {
            ".mp4" or ".m4v" => ".mp4",
            _ => ".webm"
        };
    }

    private static string SanitizeThemeName(string? themeName)
    {
        var invalidChars = Path.GetInvalidFileNameChars().ToHashSet();
        var chars = new List<char>();
        var previousWasSpace = false;

        foreach (var c in themeName ?? string.Empty)
        {
            var isInvalid = invalidChars.Contains(c) || c == '/' || c == '\\' || char.IsControl(c);
            var next = isInvalid ? ' ' : c;

            if (char.IsWhiteSpace(next))
            {
                if (!previousWasSpace && chars.Count > 0)
                {
                    chars.Add(' ');
                    previousWasSpace = true;
                }

                continue;
            }

            chars.Add(next);
            previousWasSpace = false;
        }

        var sanitized = new string(chars.ToArray()).Trim().Trim('.');
        if (sanitized.Length > MaxThemeNameLength)
        {
            sanitized = sanitized[..MaxThemeNameLength].Trim().Trim('.');
        }

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return "theme";
        }

        if (ReservedWindowsNames.Contains(sanitized))
        {
            sanitized = "_" + sanitized;
        }

        return sanitized;
    }

    private sealed record SearchKey(
        string Value,
        string Normalized,
        string[] Tokens);

    private sealed record ScoredSearchResult(
        Anime Anime,
        int Score,
        bool YearMatches,
        int TitleLengthDelta);

    private sealed record ValidatedDownloadItem(
        string Url,
        MediaType MediaType,
        string RelativePath,
        string ThemeName,
        double Volume);

    // Strongly-typed row for theme data passed to UI + grouping.
    // JsonPropertyName keeps the wire shape identical to the previous anonymous objects.
    private sealed record ThemeRow(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("sequence")] int? Sequence,
        [property: JsonPropertyName("label")] string Label,
        [property: JsonPropertyName("slug")] string Slug,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("episodes")] string Episodes,
        [property: JsonPropertyName("version")] string? Version,
        [property: JsonPropertyName("source")] string Source,
        [property: JsonPropertyName("resolution")] string? Resolution,
        [property: JsonPropertyName("creditless")] bool Creditless,
        [property: JsonPropertyName("overlap")] string Overlap,
        [property: JsonPropertyName("videoBasename")] string? VideoBasename,
        [property: JsonPropertyName("entryId")] int EntryId,
        [property: JsonPropertyName("videoId")] int VideoId,
        [property: JsonPropertyName("audioUrl")] string AudioUrl,
        [property: JsonPropertyName("videoUrl")] string VideoUrl,
        [property: JsonPropertyName("audioDownloaded")] bool AudioDownloaded,
        [property: JsonPropertyName("videoDownloaded")] bool VideoDownloaded
    );
}
