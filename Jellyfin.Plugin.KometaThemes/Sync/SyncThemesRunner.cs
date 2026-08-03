using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.KometaThemes.Api;
using Jellyfin.Plugin.KometaThemes.Configuration;
using Jellyfin.Plugin.KometaThemes.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

#pragma warning disable SA1611, CS1591

namespace Jellyfin.Plugin.KometaThemes.Sync;

/// <summary>
/// Runs KometaThemes sync logic for scheduled tasks and manual preset runs.
/// </summary>
public sealed class SyncThemesRunner
{
    private const int ChunkSize = 100;

    private static int _isRunning;

    private readonly ILibraryManager _libraryManager;
    private readonly AnimeThemesDownloader _downloader;
    private readonly PlaylistManager _playlistManager;
    private readonly SyncStatusTracker _statusTracker;
    private readonly FailedItemsStore _failedItems;
    private readonly ILogger<SyncThemesRunner> _logger;

    public SyncThemesRunner(
        ILibraryManager libraryManager,
        AnimeThemesDownloader downloader,
        PlaylistManager playlistManager,
        SyncStatusTracker statusTracker,
        FailedItemsStore failedItems,
        ILogger<SyncThemesRunner> logger)
    {
        _libraryManager = libraryManager;
        _downloader = downloader;
        _playlistManager = playlistManager;
        _statusTracker = statusTracker;
        _failedItems = failedItems;
        _logger = logger;
    }

    public bool IsRunning => Volatile.Read(ref _isRunning) == 1;

    public bool TryStartPresetRun(SyncRunPreset preset)
    {
        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
        {
            return false;
        }

        var configuration = CloneConfiguration(Plugin.Instance?.Configuration ?? new PluginConfiguration());
        ApplyPreset(configuration, preset);

        _ = Task.Run(async () =>
        {
            try
            {
                await RunWithOwnedLockAsync(configuration, new Progress<double>(), $"preset:{preset}", CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "KometaThemes preset sync failed.");
            }
            finally
            {
                Volatile.Write(ref _isRunning, 0);
            }
        });

        return true;
    }

    /// <summary>
    /// Starts a normal (non-force) manual sync. This always performs an incremental
    /// sync (only unsatisfied items), ignoring the persistent ForceSync checkbox.
    /// This makes the "Sync now" button clearly distinct from "Force sync".
    /// </summary>
    /// <returns><c>true</c> if the sync was started; <c>false</c> if another sync is already running.</returns>
    public bool TryStartManualSync()
    {
        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
        {
            return false;
        }

        var configuration = CloneConfiguration(Plugin.Instance?.Configuration ?? new PluginConfiguration());
        configuration.ForceSync = false; // ensure incremental behavior

        _ = Task.Run(async () =>
        {
            try
            {
                await RunWithOwnedLockAsync(configuration, new Progress<double>(), "manual", CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "KometaThemes manual sync failed.");
            }
            finally
            {
                Volatile.Write(ref _isRunning, 0);
            }
        });

        return true;
    }

    /// <summary>
    /// Starts a forced full sync without persisting ForceSync to the configuration.
    /// This avoids the race condition where the scheduled task reads the config before
    /// the temporary ForceSync=true save has been applied.
    /// </summary>
    /// <returns><c>true</c> if the sync was started; <c>false</c> if another sync is already running.</returns>
    public bool TryStartForcedRun()
    {
        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
        {
            return false;
        }

        var configuration = CloneConfiguration(Plugin.Instance?.Configuration ?? new PluginConfiguration());
        configuration.ForceSync = true;

        _ = Task.Run(async () =>
        {
            try
            {
                await RunWithOwnedLockAsync(configuration, new Progress<double>(), "forced", CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "KometaThemes forced sync failed.");
            }
            finally
            {
                Volatile.Write(ref _isRunning, 0);
            }
        });

        return true;
    }

    public Task RunPresetAsync(SyncRunPreset preset, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var configuration = CloneConfiguration(Plugin.Instance?.Configuration ?? new PluginConfiguration());
        ApplyPreset(configuration, preset);
        return RunAsync(configuration, progress, $"preset:{preset}", cancellationToken);
    }

    public Task RunScheduledAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var configuration = CloneConfiguration(Plugin.Instance?.Configuration ?? new PluginConfiguration());
        return RunAsync(configuration, progress, "scheduled", cancellationToken);
    }

    /// <summary>
    /// Syncs themes for a single item. Used by the library event listener for new items.
    /// </summary>
    /// <param name="item">The library item to sync.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task SyncItemAsync(BaseItem item, CancellationToken cancellationToken)
    {
        var configuration = CloneConfiguration(Plugin.Instance?.Configuration ?? new PluginConfiguration());

        // Extra gate: skip items not in libraries matching LibraryPattern (non-anime)
        if (!LibrarySelection.IsItemEligible(item, _libraryManager, configuration))
        {
            _logger.LogInformation("Item {Name} ({Id}) is not in an eligible library (LibraryPattern), skipping sync", item.Name, item.Id);
            return;
        }

        if (!_downloader.ShouldUpdate(item, configuration, configuration.ForceSync) && !configuration.ForceSync)
        {
            _logger.LogInformation("Item {Name} ({Id}) already satisfied, skipping async theme sync", item.Name, item.Id);
            return;
        }

        try
        {
            var resolvedItems = new List<ItemWithAnime>();
            await foreach (var resolved in _downloader.ResolveItems(new[] { item }, configuration, cancellationToken).ConfigureAwait(false))
            {
                resolvedItems.Add(resolved);
            }

            if (resolvedItems.Count == 0)
            {
                _logger.LogInformation("Item {Name} ({Id}) could not be resolved by any provider", item.Name, item.Id);
                return;
            }

            foreach (var resolved in resolvedItems)
            {
                foreach (var anime in resolved.Anime)
                {
                    await _downloader.HandleAsync(resolved.Item, anime, configuration, cancellationToken, configuration.ForceSync).ConfigureAwait(false);
                }
            }

            _failedItems.Remove(item.Id);
        }
        catch (Exception ex)
        {
            _failedItems.Record(item, FailedItemReason.DownloadFailed, ex.Message);
            _logger.LogError(ex, "Error syncing themes for new item {Name} ({Id})", item.Name, item.Id);
        }
    }

    private async Task RunAsync(PluginConfiguration configuration, IProgress<double> progress, string runKind, CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
        {
            throw new InvalidOperationException("A KometaThemes sync is already running.");
        }

        try
        {
            await RunWithOwnedLockAsync(configuration, progress, runKind, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Without a terminal snapshot the dashboard kept polling a status that said
            // phase=download, isFinished=false forever, while _isRunning was already reset —
            // so the UI simultaneously reported "no sync running" and a frozen progress bar.
            _statusTracker.Finish("cancelled", "Sync cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            _statusTracker.Finish("failed", ex.Message);
            throw;
        }
        finally
        {
            Volatile.Write(ref _isRunning, 0);
        }
    }

    private async Task RunWithOwnedLockAsync(PluginConfiguration configuration, IProgress<double> progress, string runKind, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Starting KometaThemes sync ({RunKind}, ForceSync={ForceSync}, DryRun={DryRun})",
                runKind,
                configuration.ForceSync,
                configuration.DryRunMode);

            if (configuration.DryRunMode)
            {
                _logger.LogInformation("Dry-run mode enabled: themes will be resolved and logged but not downloaded.");
            }

            _statusTracker.Start(0);

            var allItems = LibrarySelection.GetEligibleItems(_libraryManager, configuration).ToArray();
            _statusTracker.Update("scan", allItems.Length, 0, 0, 0, 0, 0, $"Found {allItems.Length} items");

            _logger.LogInformation("Found {Count} total items to evaluate for theme updates", allItems.Length);

            if (allItems.Length == 0)
            {
                _logger.LogWarning("No eligible items found in libraries matching '{Pattern}'.", configuration.LibraryPattern);
                _statusTracker.Update("done", 0, 0, 0, 0, 0, 0, "No eligible items.", true);
                progress.Report(100);
                return;
            }

            var itemsToUpdate = allItems.Where(it => _downloader.ShouldUpdate(it, configuration)).ToArray();

            _logger.LogInformation("{Count} items need theme updates", itemsToUpdate.Length);
            _statusTracker.Update("filter", allItems.Length, 0, 0, 0, allItems.Length - itemsToUpdate.Length, 0, $"{itemsToUpdate.Length} need updates");

            if (itemsToUpdate.Length == 0)
            {
                _logger.LogInformation("All found items already have the requested themes.");
                _statusTracker.Update("done", allItems.Length, 0, 0, 0, 0, 0, "All items already satisfied.", true);
                progress.Report(100);
                return;
            }

            var totalChunks = (int)Math.Ceiling((double)itemsToUpdate.Length / ChunkSize);
            var processedItems = 0;
            var resolvedCount = 0;
            var downloadedCount = 0;
            var failedCount = 0;
            var skippedCount = allItems.Length - itemsToUpdate.Length;

            for (var chunkIndex = 0; chunkIndex < totalChunks; chunkIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var chunk = itemsToUpdate
                    .Skip(chunkIndex * ChunkSize)
                    .Take(ChunkSize)
                    .ToArray();

                _logger.LogInformation("Processing chunk {Chunk}/{Total} ({Count} items)", chunkIndex + 1, totalChunks, chunk.Length);

#pragma warning disable CA2007
                await foreach (var resolved in _downloader.ResolveItems(chunk, configuration, cancellationToken))
#pragma warning restore CA2007
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    resolvedCount++;
                    if (configuration.DryRunMode)
                    {
                        foreach (var anime in resolved.Anime)
                        {
                            _logger.LogInformation("[DRY-RUN] Would download themes for: {Name} (AnimeId={AniId}, Themes={Count})", resolved.Item.Name, anime.Id, anime.Themes?.Count ?? 0);
                        }

                        downloadedCount++;
                    }
                    else
                    {
                        var itemFailed = false;
                        foreach (var anime in resolved.Anime)
                        {
                            try
                            {
                                var outcome = await _downloader.HandleAsync(resolved.Item, anime, configuration, cancellationToken).ConfigureAwait(false);
                                if (outcome.ChangesMade)
                                {
                                    downloadedCount++;
                                }

                                if (outcome.HasFailures)
                                {
                                    // A failed download is a failure even though nothing threw:
                                    // recording it keeps the item on the dashboard's Unresolved
                                    // list instead of silently retrying it on every sync forever.
                                    itemFailed = true;
                                    _logger.LogWarning(
                                        "{Failed} theme file(s) failed to download for {Name}",
                                        outcome.Failed,
                                        resolved.Item.Name);
                                    _failedItems.Record(
                                        resolved.Item,
                                        FailedItemReason.DownloadFailed,
                                        string.Create(CultureInfo.InvariantCulture, $"{outcome.Failed} theme file(s) failed to download"));
                                }
                            }
                            catch (Exception ex) when (ex is not OperationCanceledException)
                            {
                                itemFailed = true;
                                _failedItems.Record(resolved.Item, FailedItemReason.DownloadFailed, ex.Message);
                                _logger.LogError(ex, "Error processing {Name}", resolved.Item.Name);
                            }
                        }

                        if (itemFailed)
                        {
                            failedCount++;
                        }
                        else
                        {
                            _failedItems.Remove(resolved.Item.Id);
                        }
                    }

                    processedItems++;
                    progress.Report(100.0 * processedItems / itemsToUpdate.Length);
                    _statusTracker.Update("download", allItems.Length, processedItems, resolvedCount, downloadedCount, skippedCount, failedCount);
                }
            }

            _logger.LogInformation("KometaThemes sync completed. Processed {Count} items.", processedItems);
            _statusTracker.Update("done", allItems.Length, processedItems, resolvedCount, downloadedCount, skippedCount, failedCount, $"Completed: {processedItems} processed, {downloadedCount} downloaded, {failedCount} failed", true);

            PersistLastSync(processedItems, downloadedCount, failedCount);

            if (configuration.NotifyOnSyncComplete && downloadedCount > 0)
            {
                _logger.LogInformation("NOTIFY: {Count} themes downloaded across {Processed} items", downloadedCount, processedItems);
            }

            if (configuration.EnablePlaylist)
            {
                var playlistName = configuration.PlaylistName ?? "Anime Themes";
                await _playlistManager.RefreshPlaylistAsync(playlistName, cancellationToken).ConfigureAwait(false);
            }

            progress.Report(100);
        }
        catch (OperationCanceledException)
        {
            _statusTracker.Finish("cancelled", "Sync cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            _statusTracker.Finish("failed", ex.Message);
            throw;
        }
    }

    private static void ApplyPreset(PluginConfiguration configuration, SyncRunPreset preset)
    {
        configuration.ForceSync = true;
        configuration.DryRunMode = false;
        configuration.AudioSettings.FetchType = FetchType.All;
        configuration.AudioSettings.Volume = 0.5;
        configuration.VideoSettings.Volume = 0.5;
        configuration.MovieSettings.AudioSettings.FetchType = FetchType.All;
        configuration.MovieSettings.AudioSettings.Volume = 0.5;
        configuration.MovieSettings.VideoSettings.Volume = 0.5;

        configuration.AudioSettings.IgnoreOPs = preset == SyncRunPreset.AllEDAudio;
        configuration.AudioSettings.IgnoreEDs = preset == SyncRunPreset.AllOPAudio;
        configuration.MovieSettings.AudioSettings.IgnoreOPs = configuration.AudioSettings.IgnoreOPs;
        configuration.MovieSettings.AudioSettings.IgnoreEDs = configuration.AudioSettings.IgnoreEDs;

        var includeVideo = preset == SyncRunPreset.AllOPEDAudioVideo;
        configuration.VideoSettings.FetchType = includeVideo ? FetchType.All : FetchType.None;
        configuration.MovieSettings.VideoSettings.FetchType = includeVideo ? FetchType.All : FetchType.None;
        configuration.VideoSettings.IgnoreOPs = false;
        configuration.VideoSettings.IgnoreEDs = false;
        configuration.MovieSettings.VideoSettings.IgnoreOPs = false;
        configuration.MovieSettings.VideoSettings.IgnoreEDs = false;

        if (preset == SyncRunPreset.AllOPEDAudio || preset == SyncRunPreset.AllOPEDAudioVideo)
        {
            configuration.AudioSettings.IgnoreOPs = false;
            configuration.AudioSettings.IgnoreEDs = false;
            configuration.MovieSettings.AudioSettings.IgnoreOPs = false;
            configuration.MovieSettings.AudioSettings.IgnoreEDs = false;
        }
    }

    internal static PluginConfiguration CloneConfiguration(PluginConfiguration source)
    {
        var clone = new PluginConfiguration
        {
            DegreeOfParallelism = source.DegreeOfParallelism,
            ForceSync = source.ForceSync,
            AudioSettings = CloneMediaType(source.AudioSettings),
            VideoSettings = CloneMediaType(source.VideoSettings),
            MovieSettings = CloneCollection(source.MovieSettings),
            UiLanguage = source.UiLanguage,
            ProviderPriority = Sites.NormalizeProviderPriority(source.ProviderPriority),
            EnableTitleFallback = source.EnableTitleFallback,
            TitleMatchThreshold = source.TitleMatchThreshold,
            RateLimitPerMinute = source.RateLimitPerMinute,
            PositiveCacheTtlDays = source.PositiveCacheTtlDays,
            NegativeCacheTtlHours = source.NegativeCacheTtlHours,
            LibraryPattern = source.LibraryPattern,
            SyncIntervalHours = source.SyncIntervalHours,
            DryRunMode = source.DryRunMode,
            DownloadTimeoutSeconds = source.DownloadTimeoutSeconds,
            SeasonDetectionMode = source.SeasonDetectionMode,
            MaxThemesPerSeason = source.MaxThemesPerSeason,
            EnablePlaylist = source.EnablePlaylist,
            PlaylistName = source.PlaylistName,
            VideoVolumeDefaultMigrated = source.VideoVolumeDefaultMigrated,
            MissingThemeFallbackMode = source.MissingThemeFallbackMode,
            AutoSyncOnItemAdded = source.AutoSyncOnItemAdded,
            CleanupThemesOnItemRemoved = source.CleanupThemesOnItemRemoved,
            NotifyOnSyncComplete = source.NotifyOnSyncComplete,
            LastFullSyncUtc = source.LastFullSyncUtc,
            LastSyncSummary = source.LastSyncSummary
        };

        foreach (var item in source.SkippedItems)
        {
            clone.SkippedItems.Add(new SkippedItemEntry
            {
                ItemId = item.ItemId,
                Name = item.Name,
                Type = item.Type,
                ProductionYear = item.ProductionYear,
                Reason = item.Reason,
                SkippedUtc = item.SkippedUtc
            });
        }

        return clone;
    }

    internal static CollectionTypeConfiguration CloneCollection(CollectionTypeConfiguration? source)
    {
        source ??= new CollectionTypeConfiguration();
        return new CollectionTypeConfiguration
        {
            AudioSettings = CloneMediaType(source.AudioSettings),
            VideoSettings = CloneMediaType(source.VideoSettings),
            MaxThemesPerSeason = source.MaxThemesPerSeason
        };
    }

    internal static MediaTypeConfiguration CloneMediaType(MediaTypeConfiguration? source)
    {
        source ??= new MediaTypeConfiguration();
        return new MediaTypeConfiguration
        {
            FetchType = source.FetchType,
            IgnoreOverlapping = source.IgnoreOverlapping,
            IgnoreEDs = source.IgnoreEDs,
            IgnoreOPs = source.IgnoreOPs,
            IgnoreThemesWithCredits = source.IgnoreThemesWithCredits,
            Volume = source.Volume
        };
    }

    private static void PersistLastSync(int processedItems, int downloadedCount, int failedCount)
    {
        var plugin = Plugin.Instance;
        if (plugin == null)
        {
            return;
        }

        try
        {
            plugin.Configuration.LastFullSyncUtc = DateTime.UtcNow;
            plugin.Configuration.LastSyncSummary = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0} processed, {1} downloaded, {2} failed",
                processedItems,
                downloadedCount,
                failedCount);
            plugin.SaveConfiguration();
        }
        catch (Exception ex)
        {
            System.Console.WriteLine("Failed to persist last sync metadata: " + ex.Message);
        }
    }
}
