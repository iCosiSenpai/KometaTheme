using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Jellyfin.Plugin.KometaThemes.Models;
using MediaBrowser.Model.Plugins;

#pragma warning disable CA2227, CS1591

namespace Jellyfin.Plugin.KometaThemes.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Largest number of entries kept in the skip list and the manual-binding list.
    /// </summary>
    /// <remarks>
    /// Both lists live inside the single plugin configuration XML file, are rewritten in full on
    /// every save, and had no cap and no pruning of entries whose item no longer exists. The
    /// bindings listing endpoint also does a library lookup per entry on every GET. A cap keeps the
    /// config file, the save cost and that listing bounded; the oldest entries are dropped first.
    /// </remarks>
    public const int MaxPersistedListEntries = 2000;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        // set default options here
        DegreeOfParallelism = 1;
        ForceSync = false;

        AudioSettings = new MediaTypeConfiguration()
        {
            FetchType = FetchType.Single,
            IgnoreOverlapping = true,
            IgnoreThemesWithCredits = false,
            IgnoreOPs = false,
            IgnoreEDs = false,
            Volume = 0.5,
        };

        VideoSettings = new MediaTypeConfiguration()
        {
            FetchType = FetchType.Single,
            IgnoreOverlapping = true,
            IgnoreThemesWithCredits = true,
            IgnoreOPs = false,
            IgnoreEDs = false,
            Volume = 0.5,
        };

        MovieSettings = new CollectionTypeConfiguration();

        // KometaThemes extensions
        UiLanguage = "en";
        // NOTE: do NOT seed ProviderPriority here. XmlSerializer appends to a
        // pre-populated collection on deserialize (it never clears it), so seeding
        // defaults in the constructor makes the saved list grow by 5 every load/save
        // cycle. Defaults are seeded — and any accumulated duplicates repaired — once
        // at load time by Plugin.NormalizeProviderPriorityConfig().
        EnableTitleFallback = true;
        TitleMatchThreshold = 0.80;
        RateLimitPerMinute = 60;
        PositiveCacheTtlDays = 7;
        NegativeCacheTtlHours = 24;

        // Library filtering
        LibraryPattern = "Anime";

        // Sync scheduling
        SyncIntervalHours = 6;

        // Dry-run mode
        DryRunMode = false;

        // Download settings
        DownloadTimeoutSeconds = 60;

        // Multi-season / multi-theme settings
        SeasonDetectionMode = "Auto";
        MaxThemesPerSeason = 5;

        // Playlist
        EnablePlaylist = false;
        PlaylistName = "Anime Themes";

        // Fallback mode
        MissingThemeFallbackMode = MissingThemeFallbackMode.None;

        VideoVolumeDefaultMigrated = false;
    }

    /// <summary>
    /// Gets or sets the degree of parallelism.
    /// </summary>
    public int DegreeOfParallelism { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the sync should enforce conformity.
    /// </summary>
    public bool ForceSync { get; set; }

    /// <summary>
    /// Gets or sets the audio settings.
    /// </summary>
    public MediaTypeConfiguration AudioSettings { get; set; }

    /// <summary>
    /// Gets or sets the video settings.
    /// </summary>
    public MediaTypeConfiguration VideoSettings { get; set; }

    /// <summary>
    /// Gets or sets the download settings for the movie type.
    /// </summary>
    public CollectionTypeConfiguration MovieSettings { get; set; }

    // ── KometaThemes Extensions ──

    /// <summary>
    /// Gets or sets the plugin UI language.
    /// </summary>
    public string UiLanguage { get; set; }

#pragma warning disable CA2227
    /// <summary>
    /// Gets or sets the provider priority order for external ID resolution.
    /// </summary>
    public Collection<string> ProviderPriority { get; set; } = new();
#pragma warning restore CA2227

    /// <summary>
    /// Gets or sets a value indicating whether to enable title-based fallback search.
    /// </summary>
    public bool EnableTitleFallback { get; set; }

    /// <summary>
    /// Gets or sets the minimum Levenshtein similarity threshold for title matching (0.0-1.0).
    /// </summary>
    public double TitleMatchThreshold { get; set; }

    /// <summary>
    /// Gets or sets the rate limit in requests per minute for the AnimeThemes API.
    /// </summary>
    public int RateLimitPerMinute { get; set; }

    /// <summary>
    /// Gets or sets the TTL in days for positive cache entries.
    /// </summary>
    public int PositiveCacheTtlDays { get; set; }

    /// <summary>
    /// Gets or sets the TTL in hours for negative cache entries.
    /// </summary>
    public int NegativeCacheTtlHours { get; set; }

    /// <summary>
    /// Gets or sets the library name pattern for selecting which libraries to sync.
    /// Only libraries whose name contains this pattern are included. Default: "Anime".
    /// </summary>
    public string LibraryPattern { get; set; }

    /// <summary>
    /// Gets or sets the sync interval in hours. Default: 6, range: 1-168.
    /// </summary>
    public int SyncIntervalHours { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether dry-run mode is enabled.
    /// When enabled, themes are resolved and logged but not downloaded.
    /// </summary>
    public bool DryRunMode { get; set; }

    /// <summary>
    /// Gets or sets the ffmpeg download timeout in seconds. Default: 60, range: 15-300.
    /// </summary>
    public int DownloadTimeoutSeconds { get; set; }

    /// <summary>
    /// Gets or sets the season detection mode (ByName, ByEpisodeRange, Auto).
    /// </summary>
    public string SeasonDetectionMode { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of themes to download per season.
    /// </summary>
    public int MaxThemesPerSeason { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to automatically create/update a global playlist.
    /// </summary>
    public bool EnablePlaylist { get; set; }

    /// <summary>
    /// Gets or sets the name of the global playlist.
    /// </summary>
    public string PlaylistName { get; set; }

    /// <summary>
    /// Gets or sets the library root path used to enumerate theme files for M3U export.
    /// </summary>
    public string? PlaylistExportRoot { get; set; }

    /// <summary>
    /// Gets or sets the fallback download mode when an item has no existing themes.
    /// </summary>
    public MissingThemeFallbackMode MissingThemeFallbackMode { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the old muted video default has been migrated.
    /// </summary>
    public bool VideoVolumeDefaultMigrated { get; set; }

    /// <summary>
    /// Gets or sets the full path to the <c>yt-dlp</c> executable used for YouTube theme imports.
    /// Leave empty to auto-detect it from the usual install locations and <c>PATH</c>.
    /// </summary>
    public string? YtDlpPath { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether importing themes from YouTube links is allowed.
    /// Disabled by default: it needs an external <c>yt-dlp</c> binary, and downloading from
    /// YouTube may not be permitted in every jurisdiction or for every video.
    /// </summary>
    public bool EnableYouTubeImport { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the new item auto-sync is enabled.
    /// </summary>
    public bool AutoSyncOnItemAdded { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether orphaned theme files should be removed when an item is deleted from the library.
    /// </summary>
    public bool CleanupThemesOnItemRemoved { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to send an admin notification when a sync downloads new themes.
    /// </summary>
    public bool NotifyOnSyncComplete { get; set; }

    /// <summary>
    /// Gets or sets the last successful full-sync timestamp (UTC).
    /// </summary>
    public DateTime? LastFullSyncUtc { get; set; }

    /// <summary>
    /// Gets or sets the last sync summary (e.g. "123 items, 87 downloaded, 2 failed").
    /// </summary>
    public string? LastSyncSummary { get; set; }

    /// <summary>
    /// Gets the list of permanently skipped items.
    /// </summary>
#pragma warning disable CA2227
    public Collection<SkippedItemEntry> SkippedItems { get; } = new();
#pragma warning restore CA2227

    /// <summary>
    /// Gets the list of manual item-to-anime bindings.
    /// </summary>
#pragma warning disable CA2227
    public Collection<ManualBindingEntry> ManualBindings { get; } = new();
#pragma warning restore CA2227

    /// <summary>
    /// Gets a dictionary of skipped item IDs for fast lookup.
    /// </summary>
    /// <returns>Dictionary keyed by item ID.</returns>
    public Dictionary<string, SkippedItemEntry> GetSkippedItemsDictionary()
    {
        // Built by hand: ToDictionary throws on a duplicate ItemId, and nothing guarantees these
        // lists are unique — the controllers de-duplicate with plain string equality, so the same
        // item stored under two GUID formats appears twice.
        var result = new Dictionary<string, SkippedItemEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in SkippedItems)
        {
            if (!string.IsNullOrEmpty(entry.ItemId))
            {
                result[entry.ItemId] = entry;
            }
        }

        return result;
    }

    /// <summary>
    /// Gets a dictionary of manual bindings keyed by item ID.
    /// </summary>
    /// <returns>Dictionary keyed by item ID.</returns>
    public Dictionary<string, ManualBindingEntry> GetManualBindingsDictionary()
    {
        var result = new Dictionary<string, ManualBindingEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in ManualBindings)
        {
            if (!string.IsNullOrEmpty(entry.ItemId))
            {
                result[entry.ItemId] = entry;
            }
        }

        return result;
    }

    /// <summary>
    /// Drops the oldest skip-list entries once the list exceeds <see cref="MaxPersistedListEntries"/>.
    /// </summary>
    public void TrimSkippedItems()
    {
        if (SkippedItems.Count <= MaxPersistedListEntries)
        {
            return;
        }

        var keep = SkippedItems
            .OrderByDescending(entry => entry.SkippedUtc)
            .Take(MaxPersistedListEntries)
            .ToList();

        SkippedItems.Clear();
        foreach (var entry in keep)
        {
            SkippedItems.Add(entry);
        }
    }

    /// <summary>
    /// Drops the oldest manual bindings once the list exceeds <see cref="MaxPersistedListEntries"/>.
    /// </summary>
    public void TrimManualBindings()
    {
        if (ManualBindings.Count <= MaxPersistedListEntries)
        {
            return;
        }

        var keep = ManualBindings
            .OrderByDescending(entry => entry.BoundAt)
            .Take(MaxPersistedListEntries)
            .ToList();

        ManualBindings.Clear();
        foreach (var entry in keep)
        {
            ManualBindings.Add(entry);
        }
    }

    /// <summary>
    /// Removes skip-list and binding entries whose item is no longer present in the library.
    /// </summary>
    /// <param name="itemExists">Predicate telling whether an item ID still resolves.</param>
    /// <returns>How many entries were dropped.</returns>
    public int PruneMissingItems(Func<string, bool> itemExists)
    {
        ArgumentNullException.ThrowIfNull(itemExists);

        var removed = 0;

        foreach (var entry in SkippedItems.Where(entry => !itemExists(entry.ItemId)).ToList())
        {
            SkippedItems.Remove(entry);
            removed++;
        }

        foreach (var entry in ManualBindings.Where(entry => !itemExists(entry.ItemId)).ToList())
        {
            ManualBindings.Remove(entry);
            removed++;
        }

        return removed;
    }

    /// <summary>
    /// Clamps configuration values to documented safe ranges. Called at load time.
    /// </summary>
    public void NormalizeBounds()
    {
        // Sync interval 1-168 hours
        SyncIntervalHours = Math.Clamp(SyncIntervalHours, 1, 168);

        // Download timeout 15-300 seconds
        DownloadTimeoutSeconds = Math.Clamp(DownloadTimeoutSeconds, 15, 300);

        // Max themes per season 1-50
        MaxThemesPerSeason = Math.Clamp(MaxThemesPerSeason, 1, 50);

        // Degree of parallelism 1-8
        DegreeOfParallelism = Math.Clamp(DegreeOfParallelism, 1, 8);

        // Rate limit: the same 1-90 window the HTTP handler enforces. This used to allow up to 300,
        // so the dashboard advertised rates the handler then silently capped.
        RateLimitPerMinute = Math.Clamp(
            RateLimitPerMinute,
            Http.RateLimitingHandler.MinRatePerMinute,
            Http.RateLimitingHandler.MaxRatePerMinute);

        // TTLs: the floor is 1, not 0. A zero TTL made every cache lookup expire on read, which
        // evicted the entry, marked the cache dirty and reserialized the whole file every 30s while
        // never producing a single hit.
        PositiveCacheTtlDays = Math.Clamp(PositiveCacheTtlDays, 1, 365);
        NegativeCacheTtlHours = Math.Clamp(NegativeCacheTtlHours, 1, 24 * 30);

        // Title threshold 0.5 - 1.0
        TitleMatchThreshold = Math.Clamp(TitleMatchThreshold, 0.5, 1.0);

        // Volume 0.0 - 1.0 for audio/video settings
        if (AudioSettings != null)
        {
            AudioSettings.Volume = Math.Clamp(AudioSettings.Volume, 0.0, 1.0);
        }

        if (VideoSettings != null)
        {
            VideoSettings.Volume = Math.Clamp(VideoSettings.Volume, 0.0, 1.0);
        }

        if (MovieSettings?.VideoSettings != null)
        {
            MovieSettings.VideoSettings.Volume = Math.Clamp(MovieSettings.VideoSettings.Volume, 0.0, 1.0);
        }
    }
}
