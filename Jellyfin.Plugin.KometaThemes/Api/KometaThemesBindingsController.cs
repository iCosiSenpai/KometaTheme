using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Threading.Tasks;
using Jellyfin.Plugin.KometaThemes.Models;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

#pragma warning disable SA1611, SA1615, CS1591, CA3003

namespace Jellyfin.Plugin.KometaThemes.Api;

/// <summary>
/// REST API for managing manual item-to-anime bindings.
/// </summary>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("Plugins/KometaThemes/Bindings")]
[Produces(MediaTypeNames.Application.Json)]
public class KometaThemesBindingsController : ControllerBase
{
    private const string ThemeMusicDirectory = "theme-music";
    private const string ThemeVideoDirectory = "backdrops";
    private const string RootThemeSongFileName = "theme.mp3";

    private readonly ILibraryManager _libraryManager;
    private readonly DownloadTracker _downloadTracker;
    private readonly ILogger<KometaThemesBindingsController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="KometaThemesBindingsController"/> class.
    /// </summary>
    public KometaThemesBindingsController(
        ILibraryManager libraryManager,
        DownloadTracker downloadTracker,
        ILogger<KometaThemesBindingsController> logger)
    {
        _libraryManager = libraryManager;
        _downloadTracker = downloadTracker;
        _logger = logger;
    }

    /// <summary>
    /// Lists all manual bindings.
    /// </summary>
    [HttpGet]
    public ActionResult<IEnumerable<object>> GetBindings()
    {
        var configuration = Plugin.Instance?.Configuration;
        if (configuration == null)
        {
            return Ok(Enumerable.Empty<object>());
        }

        var bindings = configuration.ManualBindings
            .OrderByDescending(b => b.BoundAt)
            .Select(b =>
            {
                // Guid.Parse here turned one malformed persisted ItemId — hand-edited config, a
                // failed migration, a torn write — into a permanent 500 for the whole listing.
                var item = Guid.TryParse(b.ItemId, out var boundId) ? _libraryManager.GetItemById(boundId) : null;
                return new
                {
                    b.ItemId,
                    itemName = item?.Name ?? b.AnimeName,
                    itemType = item?.GetBaseItemKind().ToString(),
                    b.AnimeId,
                    b.AnimeName,
                    b.Slug,
                    b.BoundAt,
                    b.Source
                };
            });

        return Ok(bindings);
    }

    /// <summary>
    /// Creates or updates a manual binding for an item.
    /// </summary>
    [HttpPost("{itemId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult SaveBinding(
        [FromRoute, Required] Guid itemId,
        [FromBody, Required] SaveBindingRequest request)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item == null)
        {
            return NotFound(new { error = "Item not found" });
        }

        var plugin = Plugin.Instance;
        if (plugin == null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Plugin not available" });
        }

        UpsertBinding(item, request.AnimeId, request.AnimeName, request.Slug, request.Source ?? "Manual");

        _logger.LogInformation(
            "Manual binding saved for item {ItemId} ({Name}) to anime {AnimeId}",
            itemId,
            item.Name,
            request.AnimeId);

        return Ok(new { message = $"'{item.Name}' bound to '{request.AnimeName}'", binding = request });
    }

    /// <summary>
    /// Removes a manual binding. Optionally deletes the downloaded theme files.
    /// </summary>
    [HttpDelete("{itemId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> RemoveBinding(
        [FromRoute, Required] Guid itemId,
        [FromQuery] bool deleteFiles = false)
    {
        var plugin = Plugin.Instance;
        if (plugin == null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Plugin not available" });
        }

        var item = _libraryManager.GetItemById(itemId);
        var itemIdString = itemId.ToString();

        // Find and remove together, under the shared configuration lock, so a concurrent upsert
        // cannot slip between the two and be silently discarded.
        ManualBindingEntry? existing = null;
        Plugin.MutateConfiguration(configuration =>
        {
            existing = configuration.ManualBindings
                .FirstOrDefault(b => string.Equals(b.ItemId, itemIdString, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                configuration.ManualBindings.Remove(existing);
            }
        });

        if (existing == null)
        {
            return NotFound(new { error = "Binding not found" });
        }

        var deletedFiles = 0;
        if (deleteFiles && item != null && !string.IsNullOrWhiteSpace(item.ContainingFolderPath))
        {
            deletedFiles = await DeleteThemeFilesAsync(item).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Manual binding removed for item {ItemId}; deleteFiles={DeleteFiles}, deleted={Deleted}",
            itemId,
            deleteFiles,
            deletedFiles);

        return Ok(new
        {
            message = $"Binding removed for '{item?.Name ?? existing.AnimeName}'",
            deletedFiles
        });
    }

    /// <summary>
    /// Unlocks an item by removing its manual binding so automatic resolution can take over again.
    /// Existing files are preserved.
    /// </summary>
    [HttpPost("{itemId}/unlock")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult UnlockBinding([FromRoute, Required] Guid itemId)
    {
        var plugin = Plugin.Instance;
        if (plugin == null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Plugin not available" });
        }

        var item = _libraryManager.GetItemById(itemId);
        var itemIdString = itemId.ToString();
        ManualBindingEntry? existing = null;
        Plugin.MutateConfiguration(configuration =>
        {
            existing = configuration.ManualBindings
                .FirstOrDefault(b => string.Equals(b.ItemId, itemIdString, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                configuration.ManualBindings.Remove(existing);
            }
        });

        if (existing == null)
        {
            return NotFound(new { error = "Binding not found" });
        }

        _logger.LogInformation(
            "Manual binding unlocked for item {ItemId}; automatic resolution will apply on next sync",
            itemId);

        return Ok(new
        {
            message = $"Binding unlocked for '{item?.Name ?? existing.AnimeName}'. The next sync will try automatic resolution.",
            unlocked = true
        });
    }

    private static void UpsertBinding(
        BaseItem item,
        int animeId,
        string animeName,
        string slug,
        string source)
    {
        var itemId = item.Id.ToString();
        Plugin.MutateConfiguration(configuration =>
        {
            var existing = configuration.ManualBindings
                .FirstOrDefault(b => string.Equals(b.ItemId, itemId, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                configuration.ManualBindings.Remove(existing);
            }

            configuration.ManualBindings.Add(new ManualBindingEntry
            {
                ItemId = itemId,
                AnimeId = animeId,
                AnimeName = animeName,
                Slug = slug,
                BoundAt = DateTime.UtcNow,
                Source = source
            });

            configuration.TrimManualBindings();
        });
    }

    /// <summary>
    /// Deletes the theme files this plugin recorded for an item.
    /// </summary>
    /// <remarks>
    /// This used to enumerate <c>Directory.GetFiles</c> in <c>theme-music/</c> and <c>backdrops/</c>
    /// and delete everything, with no extension filter and no tracker cross-check. Since
    /// <c>backdrops/</c> is Jellyfin's artwork folder, unbinding an item with <c>deleteFiles=true</c>
    /// destroyed the user's fanart along with the themes, and in a flat library it also took every
    /// other item's themes with it. Deletion is now driven by the tracker, so only files this plugin
    /// actually wrote for this item are removed.
    /// </remarks>
    private async Task<int> DeleteThemeFilesAsync(BaseItem item)
    {
        if (string.IsNullOrWhiteSpace(item.ContainingFolderPath))
        {
            return 0;
        }

        var records = await _downloadTracker.LoadAsync(item.ContainingFolderPath).ConfigureAwait(false);
        var owned = records.Where(r => r.ItemId.Equals(item.Id) || r.ItemId.Equals(Guid.Empty)).ToList();

        var deleted = 0;
        var removedNames = new List<string>();

        foreach (var record in owned)
        {
            var fileName = Path.GetFileName(record.FileName);
            if (string.IsNullOrWhiteSpace(fileName) || !string.Equals(fileName, record.FileName, StringComparison.Ordinal))
            {
                continue;
            }

            var directory = string.IsNullOrWhiteSpace(record.Directory)
                ? Models.ThemeFileKinds.DirectoryForFileName(fileName)
                : record.Directory;

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
                    removedNames.Add(fileName);
                }
                else
                {
                    removedNames.Add(fileName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete theme file {Path}", path);
            }
        }

        // The root theme.mp3 is a copy of one of the tracked audio files.
        if (owned.Any(r => r.FileName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)))
        {
            var rootTheme = Path.Combine(item.ContainingFolderPath, RootThemeSongFileName);
            try
            {
                if (System.IO.File.Exists(rootTheme))
                {
                    System.IO.File.Delete(rootTheme);
                    deleted++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete root theme song {Path}", rootTheme);
            }
        }

        if (removedNames.Count > 0)
        {
            await _downloadTracker.RemoveRecordsAsync(item.ContainingFolderPath, removedNames).ConfigureAwait(false);
        }

        return deleted;
    }
}
