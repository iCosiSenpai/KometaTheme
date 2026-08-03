using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.KometaThemes.Configuration;
using Jellyfin.Plugin.KometaThemes.Exceptions;
using Jellyfin.Plugin.KometaThemes.Models;
using Jellyfin.Plugin.KometaThemes.Resolving;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;
using MediaType = Jellyfin.Plugin.KometaThemes.Models.MediaType;

#pragma warning disable SA1611, SA1615, CA1305, CA3003, CS1591

namespace Jellyfin.Plugin.KometaThemes;

/// <summary>
/// Downloads anime theme songs and videos with multi-season support.
/// </summary>
public class AnimeThemesDownloader : IDisposable
{
    private const string ThemeMusicFileName = "theme.mp3";
    private const string ThemeMusicDirectory = "theme-music";
    private const string ThemeVideoDirectory = "backdrops";

    /// <summary>
    /// Suffix for in-progress encoder output. Never left behind on a successful run, and always
    /// removed on a failed one, so a partial file can never be mistaken for a finished theme.
    /// </summary>
    private const string PartialSuffix = ".kt-part";

    /// <summary>
    /// Budget for a video re-encode, which is CPU-bound and far slower than a stream copy.
    /// </summary>
    private const int ReencodeTimeoutSeconds = 1800;

    private readonly HttpClient _client;
    private readonly IAnimeResolver _resolver;
    private readonly ILogger<AnimeThemesDownloader> _logger;
    private readonly IMediaEncoder _mediaEncoder;
    private readonly SeasonDetector _seasonDetector;
    private readonly ThemeGrouper _themeGrouper;
    private readonly DownloadTracker _downloadTracker;
    private readonly Sync.DownloadMetrics _metrics;
    private readonly ThemeLinkRepairService _linkRepair;
    private readonly object _downloadGateLock = new();

    /// <remarks>
    /// CA2213 is suppressed deliberately. This gate is intentionally never disposed: overlapping
    /// syncs may still hold permits on it (including across a parallelism change, which swaps the
    /// instance), and a <see cref="SemaphoreSlim"/> whose <c>AvailableWaitHandle</c> was never
    /// accessed owns no unmanaged resources, so the GC can reclaim it safely. Disposing it is what
    /// caused the <c>ObjectDisposedException</c>/<c>SemaphoreFullException</c> bug this replaced.
    /// </remarks>
#pragma warning disable CA2213
    private SemaphoreSlim _downloadGate = new(2, 2);
#pragma warning restore CA2213
    private int _downloadGateDegree = 2;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnimeThemesDownloader"/> class.
    /// </summary>
    public AnimeThemesDownloader(
        IMediaEncoder mediaEncoder,
        IHttpClientFactory clientFactory,
        IAnimeResolver resolver,
        ILogger<AnimeThemesDownloader> logger,
        SeasonDetector seasonDetector,
        ThemeGrouper themeGrouper,
        DownloadTracker downloadTracker,
        Sync.DownloadMetrics metrics,
        ThemeLinkRepairService linkRepair)
    {
        _mediaEncoder = mediaEncoder;
        _resolver = resolver;
        _logger = logger;
        _seasonDetector = seasonDetector;
        _themeGrouper = themeGrouper;
        _downloadTracker = downloadTracker;
        _metrics = metrics;
        _linkRepair = linkRepair;
        _client = clientFactory.CreateClient("AnimeThemesCDN");
    }

    /// <summary>
    /// Returns the download gate sized to the configured parallelism, rebuilding it only when
    /// the configured degree actually changed.
    /// </summary>
    /// <remarks>
    /// This type is a DI singleton and syncs can overlap (scheduled run, per-item library-event
    /// sync and the Theme Finder all reach it), so the previous implementation — disposing and
    /// replacing the field on every <see cref="HandleAsync"/> — could throw
    /// <see cref="ObjectDisposedException"/> at an in-flight <c>WaitAsync</c>, or release a permit
    /// on a different instance than the one it waited on. The old instance is deliberately not
    /// disposed: callers may still hold permits on it, and a <see cref="SemaphoreSlim"/> whose
    /// <c>AvailableWaitHandle</c> was never touched owns no unmanaged resources, so letting the
    /// GC reclaim it is safe. Callers must capture the returned reference once and use that same
    /// reference for both the wait and the release.
    /// </remarks>
    private SemaphoreSlim GetDownloadGate(int degree)
    {
        var target = Math.Clamp(degree, 1, 8);
        if (Volatile.Read(ref _downloadGateDegree) == target)
        {
            return _downloadGate;
        }

        lock (_downloadGateLock)
        {
            if (_downloadGateDegree != target)
            {
                _downloadGate = new SemaphoreSlim(target, target);
                _downloadGateDegree = target;
            }

            return _downloadGate;
        }
    }

    /// <summary>
    /// Checks if this item should be processed.
    /// </summary>
    public bool ShouldUpdate(BaseItem item, PluginConfiguration configuration, bool? forceOverride = null)
    {
        if (item.GetBaseItemKind() != BaseItemKind.Series &&
            item.GetBaseItemKind() != BaseItemKind.Movie &&
            item.GetBaseItemKind() != BaseItemKind.Season)
        {
            return false;
        }

        bool force = forceOverride ?? configuration.ForceSync;
        return force || !IsSatisfied(item, configuration);
    }

    /// <summary>
    /// Resolves a list of BaseItems to a list of BaseItems with their corresponding anime object.
    /// </summary>
    public IAsyncEnumerable<ItemWithAnime> ResolveItems(BaseItem[] items, PluginConfiguration configuration, CancellationToken cancellationToken)
    {
        return _resolver.ResolveItemsAsync(items, configuration, cancellationToken);
    }

    /// <summary>
    /// Processes an item, downloading themes for all applicable seasons.
    /// </summary>
    /// <returns>A summary of how many theme files were downloaded, failed and skipped.</returns>
    public async ValueTask<ThemeSyncResult> HandleAsync(BaseItem item, Anime anime, PluginConfiguration configuration, CancellationToken cancellationToken, bool? forceOverride = null)
    {
        _logger.LogInformation("[{Id}] Processing themes for: {Name} (AnimeId={AniId})", item.Id, item.Name, anime.Id);

        var appliedConfiguration = ApplyFallbackMode(item, configuration);
        bool force = forceOverride ?? appliedConfiguration.ForceSync;

        var isMovie = item.GetBaseItemKind() == BaseItemKind.Movie;

        var result = default(ThemeSyncResult);

        if (isMovie)
        {
            var settings = appliedConfiguration.MovieSettings;
            result += await ProcessMediaType(MediaType.Video, anime, item, force, settings, null, appliedConfiguration, cancellationToken).ConfigureAwait(false);
            result += await ProcessMediaType(MediaType.Audio, anime, item, force, settings, null, appliedConfiguration, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            result = await ProcessSeasons(anime, item, appliedConfiguration, cancellationToken, force).ConfigureAwait(false);
        }

        if (result.ChangesMade || force)
        {
            _logger.LogInformation("[{Id}] Saving metadata after theme changes with full refresh", item.Id);
            CopyBestThemeToRoot(item);
            var options = new MetadataRefreshOptions(new MediaBrowser.Controller.Providers.DirectoryService(BaseItem.FileSystem))
            {
                MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
                ForceSave = true,
                ReplaceAllMetadata = true
            };
            await item.RefreshMetadata(options, cancellationToken).ConfigureAwait(false);

            // Jellyfin 10.11.x may misfile the refreshed theme items under the
            // CollectionFolder — relink them deterministically to this item.
            try
            {
                await _linkRepair.RepairAsync(item, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[{Id}] Theme link repair failed", item.Id);
            }

            return result;
        }

        _logger.LogInformation("[{Id}] Finished without changes", item.Id);
        return result;
    }

    /// <summary>
    /// Copies the first available theme-music/*.mp3 as theme.mp3 in the item root folder.
    /// This provides a guaranteed fallback that Jellyfin 10.11.x always recognizes,
    /// even when its ThemeMediaResolver misfiles items under CollectionFolder.
    /// </summary>
    private void CopyBestThemeToRoot(BaseItem item)
    {
        if (string.IsNullOrWhiteSpace(item.ContainingFolderPath))
        {
            return;
        }

        try
        {
            var musicDir = Path.Combine(item.ContainingFolderPath, ThemeMusicDirectory);
            var rootPath = Path.Combine(item.ContainingFolderPath, ThemeMusicFileName);

            if (!Directory.Exists(musicDir))
            {
                return;
            }

            var mp3Files = Directory.GetFiles(musicDir, "*.mp3");
            if (mp3Files.Length == 0)
            {
                return;
            }

            // Pick the first file and copy to theme.mp3
            var bestFile = mp3Files[0];
            File.Copy(bestFile, rootPath, overwrite: true);
            _logger.LogInformation("[{Id}] Copied {Src} → theme.mp3 for root-level theme song", item.Id, Path.GetFileName(bestFile));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[{Id}] Failed to copy theme.mp3 to root", item.Id);
        }
    }

    private async Task<ThemeSyncResult> ProcessSeasons(Anime anime, BaseItem item, PluginConfiguration configuration, CancellationToken cancellationToken, bool? forceOverride = null)
    {
        var result = default(ThemeSyncResult);
        var seasonNumber = _seasonDetector.DetectSeason(item, configuration.SeasonDetectionMode);
        bool force = forceOverride ?? configuration.ForceSync;

        if (item is Season season)
        {
            _logger.LogInformation("[{Id}] Processing Season item: {Name} (Season={S})", item.Id, season.Name, seasonNumber);
        }
        else
        {
            _logger.LogInformation("[{Id}] Processing Series item: {Name} (DetectedSeason={S})", item.Id, item.Name, seasonNumber);
        }

        CollectionTypeConfiguration BuildSettings() => new()
        {
            AudioSettings = configuration.AudioSettings,
            VideoSettings = configuration.VideoSettings,
            MaxThemesPerSeason = configuration.MaxThemesPerSeason
        };

        result += await ProcessMediaType(MediaType.Video, anime, item, force, BuildSettings(), seasonNumber, configuration, cancellationToken).ConfigureAwait(false);
        result += await ProcessMediaType(MediaType.Audio, anime, item, force, BuildSettings(), seasonNumber, configuration, cancellationToken).ConfigureAwait(false);

        if (item is Series series)
        {
            var childSeasons = series.Children.OfType<Season>().ToList();
            if (childSeasons.Count > 0)
            {
                _logger.LogInformation("[{Id}] Found {Count} child seasons to process", item.Id, childSeasons.Count);
                foreach (var childSeason in childSeasons)
                {
                    var childSeasonNumber = _seasonDetector.DetectSeason(childSeason, configuration.SeasonDetectionMode);
                    result += await ProcessMediaType(MediaType.Video, anime, childSeason, force, BuildSettings(), childSeasonNumber, configuration, cancellationToken).ConfigureAwait(false);
                    result += await ProcessMediaType(MediaType.Audio, anime, childSeason, force, BuildSettings(), childSeasonNumber, configuration, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        return result;
    }

    private async ValueTask<ThemeSyncResult> ProcessMediaType(
        MediaType type,
        Anime anime,
        BaseItem item,
        bool forceSync,
        CollectionTypeConfiguration configuration,
        int? seasonNumber,
        PluginConfiguration pluginConfiguration,
        CancellationToken cancellationToken = default)
    {
        var settings = type == MediaType.Audio ? configuration.AudioSettings : configuration.VideoSettings;

        if (settings.FetchType == FetchType.None)
        {
            return default;
        }

        var allThemes = GetBestThemes(anime, settings).DistinctBy(it => it.Theme.Id).ToList();

        List<FlattenedTheme> themesToDownload;

        if (seasonNumber.HasValue)
        {
            var groups = _themeGrouper.GroupThemesBySeason(allThemes);
            var matchingGroup = _themeGrouper.FindMatchingGroup(groups, seasonNumber.Value);
            themesToDownload = matchingGroup?.Themes.ToList() ?? allThemes;
            _logger.LogInformation("[{Id}] Season {S}: {Count} themes available", item.Id, seasonNumber.Value, themesToDownload.Count);
        }
        else
        {
            themesToDownload = allThemes;
        }

        themesToDownload = PickThemes(settings.FetchType, themesToDownload, configuration.MaxThemesPerSeason).ToList();

        var links = ExtractLinks(type, themesToDownload, settings, seasonNumber).ToArray();

        if (forceSync)
        {
            if (type == MediaType.Audio)
            {
                RemoveFile(item, ThemeMusicFileName);
            }

            // Remove existing targets so they are actually re-downloaded,
            // not skipped because File.Exists still matches the new name.
            foreach (var link in links)
            {
                RemoveFile(item, link.Filepath);
            }

            await CleanDirectoryAsync(item, type, links.Select(it => Path.GetFileName(it.Filepath)), cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await PruneOrphanedFilesAsync(item, type, links.Select(it => Path.GetFileName(it.Filepath)), cancellationToken).ConfigureAwait(false);
        }

        var downloaded = 0;
        var failed = 0;
        var skipped = 0;

        if (links.Length > 1)
        {
            // Capture the gate once: it must be the same instance for the wait and the release.
            var gate = GetDownloadGate(pluginConfiguration.DegreeOfParallelism);
            var tasks = links.Select(async link =>
            {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    return await Download(type, link.Url, item, link.Filepath, settings.Volume, link.Theme, seasonNumber, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    gate.Release();
                }
            });

            var outcomes = await Task.WhenAll(tasks).ConfigureAwait(false);
            foreach (var outcome in outcomes)
            {
                switch (outcome)
                {
                    case DownloadOutcome.Downloaded: downloaded++; break;
                    case DownloadOutcome.Failed: failed++; break;
                    default: skipped++; break;
                }
            }
        }
        else
        {
            foreach (var link in links)
            {
                switch (await Download(type, link.Url, item, link.Filepath, settings.Volume, link.Theme, seasonNumber, cancellationToken).ConfigureAwait(false))
                {
                    case DownloadOutcome.Downloaded: downloaded++; break;
                    case DownloadOutcome.Failed: failed++; break;
                    default: skipped++; break;
                }
            }
        }

        return new ThemeSyncResult(downloaded, failed, skipped);
    }

    private List<FlattenedTheme> PickThemes(FetchType fetchType, List<FlattenedTheme> themes, int? maxThemes = null)
    {
        var selected = fetchType switch
        {
            FetchType.None => new List<FlattenedTheme>(),
            FetchType.Single => themes.Take(1).ToList(),
            FetchType.All => themes.ToList(),
            FetchType.AllPerSeason => themes.ToList(),
            _ => throw new ArgumentOutOfRangeException($"Unknown fetch type: {fetchType}")
        };

        if (maxThemes.HasValue && maxThemes.Value > 0 && selected.Count > maxThemes.Value)
        {
            selected = selected.Take(maxThemes.Value).ToList();
        }

        return selected;
    }

    private IEnumerable<(string Url, string Filepath, FlattenedTheme Theme)> ExtractLinks(
        MediaType type,
        List<FlattenedTheme> themes,
        MediaTypeConfiguration settings,
        int? seasonNumber)
    {
        bool isAudio = type == MediaType.Audio;

        foreach (var theme in themes)
        {
            var link = isAudio ? theme.Audio.Link : theme.Video.Link;
            var fileName = BuildThemeFileName(theme, settings, type);
            var directory = isAudio ? ThemeMusicDirectory : ThemeVideoDirectory;
            var path = Path.Combine(directory, fileName);

            yield return (link, path, theme);
        }
    }

    private string BuildThemeFileName(FlattenedTheme theme, MediaTypeConfiguration settings, MediaType type)
    {
        var typeStr = theme.Theme.Type == ThemeType.OP ? "OP" : "ED";
        var seq = theme.Theme.Sequence?.ToString() ?? "0";
        var slug = theme.Theme.Slug ?? "theme";

        var name = SlugToTitle(slug);

        var volume = (int)(settings.Volume * 100);
        var ext = type == MediaType.Audio ? "mp3" : "webm";

        if (!string.IsNullOrWhiteSpace(name) && name.Length <= 60)
        {
            return $"{typeStr}{seq} - {name}__{volume}.{ext}";
        }

        return $"{typeStr}{seq}__{volume}.{ext}";
    }

    private static string SlugToTitle(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return string.Empty;
        }

        var words = slug.Split('-');
        var titleWords = new List<string>();
        foreach (var word in words)
        {
            if (word.Length == 0)
            {
                continue;
            }

            if (word.Length == 1)
            {
                titleWords.Add(word.ToUpperInvariant());
            }
            else
            {
                titleWords.Add(char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant());
            }
        }

        return string.Join(" ", titleWords);
    }

    private void RemoveFile(BaseItem series, string filename)
    {
        if (string.IsNullOrWhiteSpace(series.ContainingFolderPath))
        {
            // Virtual seasons (e.g. "Specials") have no folder of their own — routine, not a problem.
            _logger.LogDebug("[{Id}] Cannot remove file for item with null path: {Name}", series.Id, series.Name);
            return;
        }

        var path = Path.Combine(series.ContainingFolderPath, filename);
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[{Id}] Failed to delete file {Path}", series.Id, path);
        }
    }

    /// <summary>
    /// Removes theme files this plugin previously wrote that are no longer wanted.
    /// </summary>
    /// <remarks>
    /// This used to delete every <c>*.mp3</c>/<c>*.webm</c> in the theme folder that was not part of
    /// the current run, with no tracker check — so a force sync destroyed hand-placed theme files and
    /// anything the Theme Finder had downloaded under a different name. It now applies the same rule
    /// the non-force path already used: only touch files the tracker says we created, and never touch
    /// imported themes, which by definition can never appear in a sync's expected-name set.
    /// </remarks>
    private async Task CleanDirectoryAsync(BaseItem item, MediaType mediaType, IEnumerable<string> allowedNames, CancellationToken cancellationToken)
        => await RemoveUnwantedThemeFilesAsync(item, mediaType, allowedNames, cancellationToken).ConfigureAwait(false);

    private async Task PruneOrphanedFilesAsync(BaseItem item, MediaType mediaType, IEnumerable<string> allowedNames, CancellationToken cancellationToken)
        => await RemoveUnwantedThemeFilesAsync(item, mediaType, allowedNames, cancellationToken).ConfigureAwait(false);

    private async Task RemoveUnwantedThemeFilesAsync(BaseItem item, MediaType mediaType, IEnumerable<string> allowedNames, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(item.ContainingFolderPath))
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var directory = mediaType == MediaType.Audio ? ThemeMusicDirectory : ThemeVideoDirectory;
        var searchPattern = mediaType == MediaType.Audio ? "*.mp3" : "*.webm";
        var path = Path.Combine(item.ContainingFolderPath, directory);
        if (!Directory.Exists(path))
        {
            return;
        }

        var tracked = new Dictionary<string, DownloadRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in await _downloadTracker.LoadAsync(item.ContainingFolderPath).ConfigureAwait(false))
        {
            if (!string.IsNullOrEmpty(record.FileName))
            {
                tracked[record.FileName] = record;
            }
        }

        var allowedSet = allowedNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removed = new List<string>();

        foreach (var filepath in Directory.GetFiles(path, searchPattern))
        {
            var name = Path.GetFileName(filepath);
            if (allowedSet.Contains(name))
            {
                continue;
            }

            // Only prune files the tracker knows about — never delete files we did not create.
            if (!tracked.TryGetValue(name, out var record))
            {
                continue;
            }

            // Imported themes are not part of any sync's expected set, so they would look like
            // orphans forever. Leave them alone; the user removes them explicitly.
            if (record.Source != ThemeSource.AnimeThemes)
            {
                continue;
            }

            _logger.LogInformation("[{Id}] Removing obsolete theme: {Theme}", item.Id, filepath);
            try
            {
                File.Delete(filepath);
                removed.Add(name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[{Id}] Failed to delete obsolete theme {Path}", item.Id, filepath);
            }
        }

        if (removed.Count > 0)
        {
            await _downloadTracker.RemoveRecordsAsync(item.ContainingFolderPath, removed).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Downloads a single theme file. Public entry point for the manual theme picker.
    /// </summary>
    public ValueTask<DownloadOutcome> DownloadSingle(
        MediaType type,
        string url,
        BaseItem item,
        string relativePath,
        double volume = 1.0,
        CancellationToken cancellationToken = default)
        => Download(type, url, item, relativePath, volume, null, null, cancellationToken);

    /// <summary>
    /// Transcodes an already-downloaded local media file into a theme file for an item.
    /// Used by sources that need an external extractor (YouTube) rather than a direct CDN link.
    /// </summary>
    /// <param name="type">Audio or video theme.</param>
    /// <param name="sourceFile">Path to the local source media file.</param>
    /// <param name="item">The owning library item.</param>
    /// <param name="relativePath">Theme path relative to the item folder.</param>
    /// <param name="volume">Volume to bake into the output, 0.0-1.0.</param>
    /// <param name="record">Optional tracker record to persist on success.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The download outcome.</returns>
    public async ValueTask<DownloadOutcome> ImportLocalFileAsync(
        MediaType type,
        string sourceFile,
        BaseItem item,
        string relativePath,
        double volume,
        DownloadRecord? record,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(item.ContainingFolderPath))
        {
            return DownloadOutcome.Failed;
        }

        var path = Path.Combine(item.ContainingFolderPath, relativePath);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            try
            {
                await TranscodeToTargetAsync(type, sourceFile, path, volume, item, copyVideoStream: true, cancellationToken).ConfigureAwait(false);
            }
            catch (ConversionException) when (type == MediaType.Video)
            {
                // A stream copy only works when the source video codec is one the webm container
                // accepts. An imported source can be H.264/mp4, which ffmpeg refuses to copy into
                // webm, so fall back to a real encode rather than failing the import.
                _logger.LogInformation("[{Id}] Stream copy rejected for the imported video; re-encoding instead", item.Id);
                await TranscodeToTargetAsync(type, sourceFile, path, volume, item, copyVideoStream: false, cancellationToken).ConfigureAwait(false);
            }

            RemoveLegacyMutedVideoForTarget(type, path, volume, item);

            if (record != null)
            {
                record.FileName = Path.GetFileName(relativePath);
                await SafeAddTrackerRecordAsync(item, record, path).ConfigureAwait(false);
            }

            _metrics.RecordSuccess();
            return DownloadOutcome.Downloaded;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Id}] Import of local file into {Path} failed", item.Id, path);
            _metrics.RecordFailure();
            return DownloadOutcome.Failed;
        }
    }

    private async ValueTask<DownloadOutcome> Download(
        MediaType type,
        string url,
        BaseItem item,
        string relativePath,
        double volume = 1.0,
        FlattenedTheme? theme = null,
        int? seasonNumber = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(item.ContainingFolderPath))
        {
            _logger.LogDebug("[{Id}] Cannot download for item with null path: {Name}", item.Id, item.Name);
            return DownloadOutcome.Failed;
        }

        var path = Path.Combine(item.ContainingFolderPath, relativePath);
        if (IsUsableExistingTarget(path, item))
        {
            RemoveLegacyMutedVideoForTarget(type, path, volume, item);
            _metrics.RecordSkipped();
            return DownloadOutcome.Skipped;
        }

        var tempFile = Path.GetTempFileName();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            _logger.LogInformation("[{Id}] Downloading {Url} to {Path}", item.Id, url, path);
            using (var downloadStream = await _client.GetStreamAsync(url, cancellationToken).ConfigureAwait(false))
            using (var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await downloadStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
            }

            await TranscodeToTargetAsync(type, tempFile, path, volume, item, copyVideoStream: true, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("[{Id}] Successfully downloaded theme!", item.Id);
            RemoveLegacyMutedVideoForTarget(type, path, volume, item);

            if (theme != null)
            {
                await SafeAddTrackerRecordAsync(
                    item,
                    new DownloadRecord
                    {
                        ThemeId = theme.Theme.Id,
                        Type = theme.Theme.Type,
                        Sequence = theme.Theme.Sequence ?? 0,
                        Slug = theme.Theme.Slug ?? string.Empty,
                        FileName = Path.GetFileName(relativePath),
                        SeasonNumber = seasonNumber ?? 0,
                        DownloadedAt = DateTime.UtcNow,
                        ItemId = item.Id,
                        Source = ThemeSource.AnimeThemes,
                        Directory = type == MediaType.Audio ? ThemeMusicDirectory : ThemeVideoDirectory
                    },
                    path).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation is not a download failure: do not count it, do not blame the item.
            // Rethrow so the caller stops instead of grinding through the remaining links.
            throw;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "[{Id}] Download of {Url} failed", item.Id, url);
            _metrics.RecordFailure();
            return DownloadOutcome.Failed;
        }
        finally
        {
            TryDeleteFile(tempFile);
        }

        _metrics.RecordSuccess();
        return DownloadOutcome.Downloaded;
    }

    /// <summary>
    /// Runs ffmpeg over <paramref name="sourceFile"/> and publishes the result at
    /// <paramref name="targetPath"/> atomically.
    /// </summary>
    /// <remarks>
    /// ffmpeg used to be pointed straight at the final path. Any failure after it had created and
    /// begun filling that file — non-zero exit, the kill-on-timeout branch, cancellation, a full
    /// disk — left a truncated file behind, and only the temporary *input* was cleaned up. On the
    /// next sync the <c>File.Exists</c> short-circuit saw that stub, counted it as skipped and
    /// reported the item satisfied, so the broken theme was never repaired by any subsequent sync,
    /// force sync or prune. Encoding into a sibling <c>.kt-part</c> file and moving it into place
    /// only after a verified non-empty exit means a failed attempt leaves no trace.
    /// </remarks>
    private async Task TranscodeToTargetAsync(
        MediaType type,
        string sourceFile,
        string targetPath,
        double volume,
        BaseItem item,
        bool copyVideoStream,
        CancellationToken cancellationToken)
    {
        var partialPath = targetPath + PartialSuffix;
        TryDeleteFile(partialPath);

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    FileName = _mediaEncoder.EncoderPath,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    ErrorDialog = false,
                    ArgumentList = { "-nostdin", "-y", "-i", sourceFile }
                },
                EnableRaisingEvents = true
            };

            var arguments = process.StartInfo.ArgumentList;
            if (type == MediaType.Video)
            {
                if (copyVideoStream)
                {
                    arguments.Add("-c:v");
                    arguments.Add("copy");
                }
                else
                {
                    // VP9 so the result is a valid webm. "good" with a row-based thread split keeps
                    // a short theme clip within a sane wall time on a NAS-class CPU.
                    arguments.Add("-c:v");
                    arguments.Add("libvpx-vp9");
                    arguments.Add("-crf");
                    arguments.Add("33");
                    arguments.Add("-b:v");
                    arguments.Add("0");
                    arguments.Add("-deadline");
                    arguments.Add("good");
                    arguments.Add("-cpu-used");
                    arguments.Add("4");
                    arguments.Add("-row-mt");
                    arguments.Add("1");
                }
            }

            if (volume < 0.01 && type == MediaType.Video)
            {
                arguments.Add("-an");
            }
            else
            {
                arguments.Add("-filter:a");
                arguments.Add(string.Create(CultureInfo.InvariantCulture, $"volume={volume:0.00}"));
            }

            // ffmpeg picks the muxer from the extension, so keep the real one and only
            // append the partial marker after it.
            arguments.Add("-f");
            arguments.Add(type == MediaType.Audio ? "mp3" : "webm");
            arguments.Add(partialPath);

            process.Start();

            // A stream copy is I/O-bound and finishes in seconds, so the configured timeout is the
            // right budget. A re-encode is CPU-bound and can take many minutes for a theme-length
            // clip on NAS-class hardware, which would otherwise be killed as a spurious timeout.
            var timeoutSeconds = copyVideoStream || type == MediaType.Audio
                ? Math.Clamp(Plugin.Instance?.Configuration?.DownloadTimeoutSeconds ?? 60, 15, 300)
                : ReencodeTimeoutSeconds;
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            // Drain both pipes: an unread stdout can fill its buffer and deadlock ffmpeg
            // until the timeout kills it.
            var outputRead = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var errorRead = process.StandardError.ReadToEndAsync(CancellationToken.None);

            try
            {
                await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    var reason = timeoutCts.IsCancellationRequested ? "timed out" : "was cancelled";
                    _logger.LogWarning("[{Id}] ffmpeg {Reason}, killing process", item.Id, reason);
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch (Exception killEx)
                    {
                        _logger.LogWarning(killEx, "[{Id}] Failed to kill ffmpeg", item.Id);
                    }

                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                }

                await Task.WhenAll(outputRead, errorRead).ConfigureAwait(false);

                if (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    throw new ConversionException(
                        process.ExitCode,
                        string.Create(CultureInfo.InvariantCulture, $"ffmpeg timed out after {timeoutSeconds}s"));
                }

                throw;
            }

            await Task.WhenAll(outputRead, errorRead).ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                var commandInfo = $"Command line: {process.StartInfo.FileName} {string.Join(" ", arguments)}";
                throw new ConversionException(process.ExitCode, commandInfo + "\n" + await errorRead.ConfigureAwait(false));
            }

            var info = new FileInfo(partialPath);
            if (!info.Exists || info.Length == 0)
            {
                throw new ConversionException(0, "ffmpeg reported success but produced no output.");
            }

            File.Move(partialPath, targetPath, overwrite: true);
        }
        catch
        {
            TryDeleteFile(partialPath);
            throw;
        }
    }

    /// <summary>
    /// Treats a zero-byte target as absent so themes broken by an earlier interrupted
    /// transcode are re-downloaded instead of being skipped forever.
    /// </summary>
    private bool IsUsableExistingTarget(string path, BaseItem item)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            return false;
        }

        if (info.Length > 0)
        {
            return true;
        }

        _logger.LogWarning("[{Id}] Existing theme {Path} is empty — replacing it", item.Id, path);
        TryDeleteFile(path);
        return false;
    }

    private async Task SafeAddTrackerRecordAsync(BaseItem item, DownloadRecord record, string path)
    {
        try
        {
            await _downloadTracker.AddRecordAsync(item.ContainingFolderPath, record).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[{Id}] Failed to record download tracker entry for {Path}", item.Id, path);
        }
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete file {Path}", path);
        }
    }

    private bool IsSatisfied(BaseItem item, PluginConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(item.ContainingFolderPath))
        {
            return false; // virtual items are never satisfied
        }

        var isMovie = item.GetBaseItemKind() == BaseItemKind.Movie;
        var audioSettings = isMovie ? configuration.MovieSettings.AudioSettings : configuration.AudioSettings;
        var videoSettings = isMovie ? configuration.MovieSettings.VideoSettings : configuration.VideoSettings;

        var audioSatisfied = item.GetThemeSongs().Any() || audioSettings.FetchType == FetchType.None;
        var videoSatisfied = videoSettings.FetchType == FetchType.None ||
            item.GetThemeVideos().Any(video => IsUsableThemeVideo(video.Path, videoSettings.Volume));

        // Jellyfin 10.11.x bug: ThemeMediaResolver may assign themes to CollectionFolder
        // instead of the individual Series. Fall back to filesystem check so the plugin
        // does not attempt a redundant download on every sync cycle.
        if (!audioSatisfied && audioSettings.FetchType != FetchType.None)
        {
            var musicDir = Path.Combine(item.ContainingFolderPath, ThemeMusicDirectory);
            audioSatisfied = Directory.Exists(musicDir) && Directory.GetFiles(musicDir, "*.mp3").Length > 0;
        }

        if (!videoSatisfied && videoSettings.FetchType != FetchType.None)
        {
            var vidDir = Path.Combine(item.ContainingFolderPath, ThemeVideoDirectory);
            videoSatisfied = Directory.Exists(vidDir) && Directory.GetFiles(vidDir, "*.webm").Length > 0;
        }

        return audioSatisfied && videoSatisfied;
    }

    private void RemoveLegacyMutedVideoForTarget(MediaType type, string targetPath, double volume, BaseItem item)
    {
        if (type != MediaType.Video || volume < 0.01)
        {
            return;
        }

        var directory = Path.GetDirectoryName(targetPath);
        var fileName = Path.GetFileNameWithoutExtension(targetPath);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        var suffixIndex = fileName.LastIndexOf("__", StringComparison.Ordinal);
        if (suffixIndex < 0)
        {
            return;
        }

        var legacyPath = Path.Combine(directory, fileName[..suffixIndex] + "__0.webm");
        if (!string.Equals(legacyPath, targetPath, StringComparison.Ordinal) && File.Exists(legacyPath))
        {
            _logger.LogInformation("[{Id}] Removing legacy muted video theme: {Theme}", item.Id, legacyPath);
            try
            {
                File.Delete(legacyPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[{Id}] Failed to delete legacy muted video {Path}", item.Id, legacyPath);
            }
        }
    }

    /// <summary>
    /// Returns a modified configuration when the item has no themes and a fallback mode is set.
    /// </summary>
    private static PluginConfiguration ApplyFallbackMode(BaseItem item, PluginConfiguration configuration)
    {
        if (configuration.MissingThemeFallbackMode == MissingThemeFallbackMode.None)
        {
            return configuration;
        }

        if (string.IsNullOrWhiteSpace(item.ContainingFolderPath))
        {
            return configuration;
        }

        var hasAnyThemeFile = Directory.Exists(Path.Combine(item.ContainingFolderPath, ThemeMusicDirectory)) &&
                              Directory.GetFiles(Path.Combine(item.ContainingFolderPath, ThemeMusicDirectory), "*.mp3").Length > 0;
        var hasAnyVideoFile = Directory.Exists(Path.Combine(item.ContainingFolderPath, ThemeVideoDirectory)) &&
                              Directory.GetFiles(Path.Combine(item.ContainingFolderPath, ThemeVideoDirectory), "*.webm").Length > 0;

        if (hasAnyThemeFile && hasAnyVideoFile)
        {
            return configuration;
        }

        var clone = CloneConfigForFallback(configuration);
        var mode = configuration.MissingThemeFallbackMode;

        switch (mode)
        {
            case MissingThemeFallbackMode.AllOPs:
                clone.AudioSettings.FetchType = FetchType.All;
                clone.AudioSettings.IgnoreOPs = false;
                clone.AudioSettings.IgnoreEDs = true;
                clone.VideoSettings.FetchType = FetchType.None;
                clone.MovieSettings.AudioSettings.FetchType = FetchType.All;
                clone.MovieSettings.AudioSettings.IgnoreOPs = false;
                clone.MovieSettings.AudioSettings.IgnoreEDs = true;
                clone.MovieSettings.VideoSettings.FetchType = FetchType.None;
                break;

            case MissingThemeFallbackMode.AllEDs:
                clone.AudioSettings.FetchType = FetchType.All;
                clone.AudioSettings.IgnoreOPs = true;
                clone.AudioSettings.IgnoreEDs = false;
                clone.VideoSettings.FetchType = FetchType.None;
                clone.MovieSettings.AudioSettings.FetchType = FetchType.All;
                clone.MovieSettings.AudioSettings.IgnoreOPs = true;
                clone.MovieSettings.AudioSettings.IgnoreEDs = false;
                clone.MovieSettings.VideoSettings.FetchType = FetchType.None;
                break;

            case MissingThemeFallbackMode.AllVideos:
                clone.AudioSettings.FetchType = FetchType.None;
                clone.VideoSettings.FetchType = FetchType.All;
                clone.VideoSettings.IgnoreOPs = false;
                clone.VideoSettings.IgnoreEDs = false;
                clone.MovieSettings.AudioSettings.FetchType = FetchType.None;
                clone.MovieSettings.VideoSettings.FetchType = FetchType.All;
                clone.MovieSettings.VideoSettings.IgnoreOPs = false;
                clone.MovieSettings.VideoSettings.IgnoreEDs = false;
                break;

            case MissingThemeFallbackMode.AllOPsEDsVideos:
                clone.AudioSettings.FetchType = FetchType.All;
                clone.AudioSettings.IgnoreOPs = false;
                clone.AudioSettings.IgnoreEDs = false;
                clone.VideoSettings.FetchType = FetchType.All;
                clone.VideoSettings.IgnoreOPs = false;
                clone.VideoSettings.IgnoreEDs = false;
                clone.MovieSettings.AudioSettings.FetchType = FetchType.All;
                clone.MovieSettings.AudioSettings.IgnoreOPs = false;
                clone.MovieSettings.AudioSettings.IgnoreEDs = false;
                clone.MovieSettings.VideoSettings.FetchType = FetchType.All;
                clone.MovieSettings.VideoSettings.IgnoreOPs = false;
                clone.MovieSettings.VideoSettings.IgnoreEDs = false;
                break;
        }

        return clone;
    }

    private static PluginConfiguration CloneConfigForFallback(PluginConfiguration source)
    {
        return new PluginConfiguration
        {
            DegreeOfParallelism = source.DegreeOfParallelism,
            ForceSync = source.ForceSync,
            DryRunMode = source.DryRunMode,
            MaxThemesPerSeason = source.MaxThemesPerSeason,
            SeasonDetectionMode = source.SeasonDetectionMode,
            AudioSettings = new MediaTypeConfiguration
            {
                FetchType = source.AudioSettings.FetchType,
                IgnoreOverlapping = source.AudioSettings.IgnoreOverlapping,
                IgnoreEDs = source.AudioSettings.IgnoreEDs,
                IgnoreOPs = source.AudioSettings.IgnoreOPs,
                IgnoreThemesWithCredits = source.AudioSettings.IgnoreThemesWithCredits,
                Volume = source.AudioSettings.Volume
            },
            VideoSettings = new MediaTypeConfiguration
            {
                FetchType = source.VideoSettings.FetchType,
                IgnoreOverlapping = source.VideoSettings.IgnoreOverlapping,
                IgnoreEDs = source.VideoSettings.IgnoreEDs,
                IgnoreOPs = source.VideoSettings.IgnoreOPs,
                IgnoreThemesWithCredits = source.VideoSettings.IgnoreThemesWithCredits,
                Volume = source.VideoSettings.Volume
            },
            MovieSettings = new CollectionTypeConfiguration
            {
                AudioSettings = new MediaTypeConfiguration
                {
                    FetchType = source.MovieSettings.AudioSettings.FetchType,
                    IgnoreOverlapping = source.MovieSettings.AudioSettings.IgnoreOverlapping,
                    IgnoreEDs = source.MovieSettings.AudioSettings.IgnoreEDs,
                    IgnoreOPs = source.MovieSettings.AudioSettings.IgnoreOPs,
                    IgnoreThemesWithCredits = source.MovieSettings.AudioSettings.IgnoreThemesWithCredits,
                    Volume = source.MovieSettings.AudioSettings.Volume
                },
                VideoSettings = new MediaTypeConfiguration
                {
                    FetchType = source.MovieSettings.VideoSettings.FetchType,
                    IgnoreOverlapping = source.MovieSettings.VideoSettings.IgnoreOverlapping,
                    IgnoreEDs = source.MovieSettings.VideoSettings.IgnoreEDs,
                    IgnoreOPs = source.MovieSettings.VideoSettings.IgnoreOPs,
                    IgnoreThemesWithCredits = source.MovieSettings.VideoSettings.IgnoreThemesWithCredits,
                    Volume = source.MovieSettings.VideoSettings.Volume
                }
            },
            MissingThemeFallbackMode = source.MissingThemeFallbackMode
        };
    }

    private static bool IsUsableThemeVideo(string? path, double configuredVolume)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return configuredVolume < 0.01 ||
            !Path.GetFileName(path).EndsWith("__0.webm", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets themes roughly sorted by relevance and filtered as needed.
    /// </summary>
    private IEnumerable<FlattenedTheme> GetBestThemes(Anime anime, MediaTypeConfiguration settings)
    {
        return (anime.Themes ?? [])
            .Where(theme => theme?.Entries != null)
            .SelectMany(theme => theme.Entries
                .Where(entry => entry?.Videos != null)
                .SelectMany(entry => entry.Videos
                    .Where(video => video?.Audio != null &&
                                    !string.IsNullOrWhiteSpace(video.Link) &&
                                    !string.IsNullOrWhiteSpace(video.Audio.Link))
                    .Select(video => Wrap(theme, entry, video))))
            .OrderBy(Rate)
            .ThenBy(t => t.Theme.Sequence ?? int.MaxValue)
            .Where(it => !settings.IgnoreOverlapping || it.Video.Overlap == OverlapType.None)
            .Where(it => !settings.IgnoreThemesWithCredits || it.Video.Creditless)
            .Where(it => !settings.IgnoreEDs || it.Theme.Type != ThemeType.ED)
            .Where(it => !settings.IgnoreOPs || it.Theme.Type != ThemeType.OP);
    }

    private static FlattenedTheme Wrap(AnimeTheme theme, AnimeThemeEntry entry, Models.Video video)
    {
        return new FlattenedTheme(theme, entry, video, video.Audio);
    }

    private static double Rate(FlattenedTheme theme)
    {
        double score = 0;

        if (theme.Entry.Nsfw)
        {
            score += 10;
        }

        if (theme.Entry.Spoiler)
        {
            score += 50;
        }

        switch (theme.Video.Overlap)
        {
            case OverlapType.Over:
                score += 20;
                break;
            case OverlapType.Transition:
                score += 15;
                break;
        }

        switch (theme.Video.Source)
        {
            case VideoSource.LD:
            case VideoSource.VHS:
                score += 10;
                break;
            case VideoSource.WEB:
            case VideoSource.RAW:
                score += 5;
                break;
        }

        if (!theme.Video.Creditless)
        {
            score += 10;
        }

        return score;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;

            // The HttpClient comes from IHttpClientFactory, which owns the underlying handler;
            // disposing the client here is a no-op for the connection pool but keeps the
            // ownership story explicit.
            _client.Dispose();

            // _downloadGate is deliberately not disposed: downloads may still hold permits on it
            // during shutdown, and a SemaphoreSlim whose AvailableWaitHandle was never touched
            // holds no unmanaged resources.
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
