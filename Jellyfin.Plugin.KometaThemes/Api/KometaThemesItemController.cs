using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.KometaThemes.Configuration;
using Jellyfin.Plugin.KometaThemes.Models;
using Jellyfin.Plugin.KometaThemes.Sync;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;

#pragma warning disable CA2007, CA3003, SA1611, SA1615, CS1591

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KometaThemes.Api;

/// <summary>
/// REST API for per-item theme management.
/// </summary>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("Plugins/KometaThemes/Items")]
[Produces(MediaTypeNames.Application.Json)]
public class KometaThemesItemController : ControllerBase
{
    private const string ThemeMusicDirectory = "theme-music";
    private const string ThemeVideoDirectory = "backdrops";

    private readonly ILibraryManager _libraryManager;
    private readonly AnimeThemesDownloader _downloader;
    private readonly DownloadTracker _downloadTracker;
    private readonly ThemeLinkRepairService _linkRepair;
    private readonly FailedItemsStore _failedItems;
    private readonly ILogger<KometaThemesItemController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="KometaThemesItemController"/> class.
    /// </summary>
    public KometaThemesItemController(
        ILibraryManager libraryManager,
        AnimeThemesDownloader downloader,
        DownloadTracker downloadTracker,
        ThemeLinkRepairService linkRepair,
        FailedItemsStore failedItems,
        ILogger<KometaThemesItemController> logger)
    {
        _libraryManager = libraryManager;
        _downloader = downloader;
        _downloadTracker = downloadTracker;
        _linkRepair = linkRepair;
        _failedItems = failedItems;
        _logger = logger;
    }

    private bool EnsureEligible(BaseItem? item, out ActionResult? errorResult)
    {
        errorResult = null;
        if (item == null)
        {
            errorResult = NotFound(new { error = "Item not found" });
            return false;
        }

        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        if (!LibrarySelection.IsItemEligible(item, _libraryManager, config))
        {
            var errorMsg = LibrarySelection.GetNotEligibleErrorMessage(config);
            errorResult = BadRequest(new { error = errorMsg });
            return false;
        }

        return true;
    }

    /// <summary>
    /// Checks if the given item is eligible for KometaThemes (i.e. belongs to a library matching LibraryPattern and not blacklisted).
    /// Used by the web UI to decide whether to show the ♪ button and enable full features.
    /// </summary>
    [HttpGet("{itemId}/eligible")]
    public ActionResult GetItemEligibility([FromRoute, Required] Guid itemId)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item == null)
        {
            return NotFound(new { eligible = false, reason = "Item not found" });
        }

        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        bool eligible = LibrarySelection.IsItemEligible(item, _libraryManager, config);

        string? reason = null;
        if (!eligible)
        {
            var lang = (config?.UiLanguage ?? "en").Trim().ToLowerInvariant();
            reason = lang.StartsWith("it", StringComparison.Ordinal)
                ? "Questo elemento non appartiene a una libreria che corrisponde al Library Pattern configurato."
                : "This item does not belong to a library matching the configured Library Pattern.";
        }

        return Ok(new
        {
            eligible,
            reason
        });
    }

    /// <summary>
    /// Repairs the linking between the item and its theme files (Jellyfin 10.11.x workaround).
    /// </summary>
    [HttpPost("{itemId}/repair")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> RepairItemThemeLinks([FromRoute, Required] Guid itemId)
    {
        var item = _libraryManager.GetItemById(itemId);
        ActionResult? err;
        if (!EnsureEligible(item, out err))
        {
            return err!;
        }

        try
        {
            var result = await _linkRepair.RepairAsync(item!, CancellationToken.None);
            return Ok(new
            {
                songsOnDisk = result.SongsOnDisk,
                videosOnDisk = result.VideosOnDisk,
                repaired = result.Repaired,
                notScanned = result.NotScanned,
                registeredSongs = result.RegisteredSongs,
                registeredVideos = result.RegisteredVideos
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error repairing theme links for item {Id}", itemId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Gets the manual binding for a specific item, if any.
    /// </summary>
    [HttpGet("{itemId}/binding")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult GetItemBinding([FromRoute, Required] Guid itemId)
    {
        var item = _libraryManager.GetItemById(itemId);
        ActionResult? err;
        if (!EnsureEligible(item, out err))
        {
            return err!;
        }

        var configuration = Plugin.Instance?.Configuration;
        if (configuration == null)
        {
            return NotFound(new { error = "Plugin configuration not available" });
        }

        var itemIdString = itemId.ToString();
        var binding = configuration.ManualBindings
            .FirstOrDefault(b => string.Equals(b.ItemId, itemIdString, StringComparison.OrdinalIgnoreCase));

        if (binding == null)
        {
            return Ok(new { hasBinding = false });
        }

        return Ok(new
        {
            hasBinding = true,
            binding.AnimeId,
            binding.AnimeName,
            binding.Slug,
            binding.BoundAt,
            binding.Source
        });
    }

    /// <summary>
    /// Triggers a theme sync for a specific item. With <paramref name="force"/> the
    /// already-satisfied check is bypassed (used by the Unresolved tab retry button).
    /// </summary>
    [HttpPost("{itemId}/sync")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> SyncItemThemes([FromRoute, Required] Guid itemId, [FromQuery] bool force = false)
    {
        var item = _libraryManager.GetItemById(itemId);
        ActionResult? err;
        if (!EnsureEligible(item, out err))
        {
            return err!;
        }

        // Always work on a clone. Never mutate the live singleton config (avoids races with scheduled tasks, concurrent ops, persistence).
        var baseConfig = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var configuration = SyncThemesRunner.CloneConfiguration(baseConfig);
        bool effectiveForce = force || configuration.ForceSync;

        if (!force && !_downloader.ShouldUpdate(item!, configuration, effectiveForce) && !effectiveForce)
        {
            return Ok(new { message = "Item already has themes and ForceSync is off", downloaded = false });
        }

        try
        {
            var resolvedItems = new List<ItemWithAnime>();
            await foreach (var resolved in _downloader.ResolveItems(new[] { item! }, configuration, CancellationToken.None))
            {
                resolvedItems.Add(resolved);
            }

            foreach (var resolved in resolvedItems)
            {
                foreach (var anime in resolved.Anime)
                {
                    await _downloader.HandleAsync(resolved.Item, anime, configuration, CancellationToken.None, effectiveForce);
                }
            }

            if (resolvedItems.Count > 0)
            {
                _failedItems.Remove(item!.Id);
            }

            return Ok(new { message = "Sync completed successfully", downloaded = resolvedItems.Count > 0 });
        }
        catch (Exception ex)
        {
            _failedItems.Record(item!, FailedItemReason.DownloadFailed, ex.Message);
            _logger.LogError(ex, "Error syncing themes for item {Id}", itemId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Resolves themes for an item without downloading anything. Useful for preview/dry-run.
    /// </summary>
    [HttpPost("{itemId}/preview")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> PreviewItemThemes([FromRoute, Required] Guid itemId)
    {
        var item = _libraryManager.GetItemById(itemId);
        ActionResult? err;
        if (!EnsureEligible(item, out err))
        {
            return err!;
        }

        // Clone only; preview always forces to show candidates without side effects on live config.
        var baseConfig = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var configuration = SyncThemesRunner.CloneConfiguration(baseConfig);

        try
        {
            var resolvedItems = new List<ItemWithAnime>();
            await foreach (var resolved in _downloader.ResolveItems(new[] { item! }, configuration, CancellationToken.None))
            {
                resolvedItems.Add(resolved);
            }

            var summary = new List<object>();
            foreach (var resolved in resolvedItems)
            {
                foreach (var anime in resolved.Anime)
                {
                    var themeCount = anime.Themes?.Count ?? 0;
                    var videoCount = anime.Themes?
                        .SelectMany(t => t.Entries ?? new System.Collections.ObjectModel.Collection<AnimeThemeEntry>())
                        .Sum(e => e.Videos?.Count ?? 0) ?? 0;
                    summary.Add(new
                    {
                        animeId = anime.Id,
                        animeName = anime.Name,
                        animeSlug = anime.Slug,
                        themes = themeCount,
                        videos = videoCount
                    });
                }
            }

            return Ok(new { preview = true, candidates = summary });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error previewing themes for item {Id}", itemId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Gets the list of downloaded themes for an item.
    /// </summary>
    [HttpGet("{itemId}/themes")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> GetItemThemes([FromRoute, Required] Guid itemId)
    {
        var item = _libraryManager.GetItemById(itemId);
        ActionResult? err;
        if (!EnsureEligible(item, out err))
        {
            return err!;
        }

        var records = await _downloadTracker.LoadAsync(item!.ContainingFolderPath);
        var result = records.Select(r => new
        {
            r.ThemeId,
            Type = r.Type.ToString(),
            r.Sequence,
            r.Slug,
            r.FileName,
            r.SeasonNumber,
            r.DownloadedAt,
            Exists = System.IO.File.Exists(Path.Combine(item.ContainingFolderPath, "theme-music", r.FileName)) ||
                     System.IO.File.Exists(Path.Combine(item.ContainingFolderPath, "backdrops", r.FileName))
        });

        return Ok(result);
    }

    /// <summary>
    /// Deletes downloaded themes for an item. When <paramref name="fileName"/> is provided,
    /// only that file is removed; otherwise all tracked themes are deleted.
    /// </summary>
    /// <remarks>
    /// Two guards matter here. First the eligibility gate, which every other mutating action on this
    /// controller applies but this one used to skip — without it the endpoint operated on any library
    /// item, including ones outside the configured library pattern. Second, the requested file name
    /// is now matched against the tracker before anything is deleted: previously any bare name was
    /// deleted from both <c>theme-music/</c> and <c>backdrops/</c>, and since <c>backdrops/</c> is
    /// Jellyfin's artwork folder, a request naming an artwork file destroyed user fanart this plugin
    /// never created.
    /// </remarks>
    [HttpDelete("{itemId}/themes")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> DeleteItemThemes([FromRoute, Required] Guid itemId, [FromQuery] string? fileName = null)
    {
        var item = _libraryManager.GetItemById(itemId);
        ActionResult? err;
        if (!EnsureEligible(item, out err))
        {
            return err!;
        }

        try
        {
            var records = (await _downloadTracker.LoadAsync(item!.ContainingFolderPath)).ToList();

            if (!string.IsNullOrWhiteSpace(fileName))
            {
                // Guard against path traversal: only bare file names are accepted.
                if (!string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
                {
                    return BadRequest(new { error = "Invalid file name" });
                }

                var target = records.FirstOrDefault(r => string.Equals(r.FileName, fileName, StringComparison.OrdinalIgnoreCase));
                if (target == null)
                {
                    // Refuse to delete anything this plugin did not record.
                    _logger.LogWarning("Refusing to delete untracked file {FileName} for item {Id}", fileName, itemId);
                    return NotFound(new { error = "That file is not a theme tracked by KometaThemes." });
                }

                var deletedSingle = DeleteThemeFile(item, target);
                var remaining = records.Where(r => !string.Equals(r.FileName, fileName, StringComparison.OrdinalIgnoreCase)).ToList();
                await _downloadTracker.SaveAsync(item.ContainingFolderPath, remaining);
                return Ok(new { message = $"Deleted {deletedSingle} theme files", deleted = deletedSingle });
            }

            var deleted = records.Sum(record => DeleteThemeFile(item, record));
            await _downloadTracker.SaveAsync(item.ContainingFolderPath, new List<DownloadRecord>());

            return Ok(new { message = $"Deleted {deleted} theme files", deleted });
        }
        catch (Exception ex)
        {
            // Without this, a null ContainingFolderPath or a mid-loop IO error escaped as a 500 and
            // left the tracker file inconsistent with what was actually on disk.
            _logger.LogError(ex, "Error deleting themes for item {Id}", itemId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to delete theme files." });
        }
    }

    private int DeleteThemeFile(BaseItem item, DownloadRecord record)
    {
        var fileName = Path.GetFileName(record.FileName);
        if (string.IsNullOrWhiteSpace(fileName) || !string.Equals(fileName, record.FileName, StringComparison.Ordinal))
        {
            _logger.LogWarning("Ignoring tracker record with a path-like file name: {Name}", record.FileName);
            return 0;
        }

        // Prefer the recorded directory; records written before it existed are inferred from the
        // extension, which is what those records always were in practice.
        var directories = string.IsNullOrWhiteSpace(record.Directory)
            ? new[] { fileName.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) ? ThemeVideoDirectory : ThemeMusicDirectory }
            : new[] { record.Directory };

        var deleted = 0;
        foreach (var directory in directories)
        {
            if (!string.Equals(directory, ThemeMusicDirectory, StringComparison.Ordinal) &&
                !string.Equals(directory, ThemeVideoDirectory, StringComparison.Ordinal))
            {
                continue;
            }

            var path = Path.Combine(item.ContainingFolderPath, directory, fileName);
            try
            {
                if (System.IO.File.Exists(path))
                {
                    System.IO.File.Delete(path);
                    deleted++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete theme file {Path}", path);
            }
        }

        return deleted;
    }
}
