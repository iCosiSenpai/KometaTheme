using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net.Mime;
using System.Threading.Tasks;
using Jellyfin.Plugin.KometaThemes.Models;
using Jellyfin.Plugin.KometaThemes.Sync;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KometaThemes.Api;

/// <summary>
/// REST API for managing permanently skipped items from the settings page.
/// </summary>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("Plugins/KometaThemes/Skipped")]
[Produces(MediaTypeNames.Application.Json)]
public class KometaThemesSkippedController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;
    private readonly FailedItemsStore _failedItems;
    private readonly ILogger<KometaThemesSkippedController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="KometaThemesSkippedController"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager for resolving item metadata.</param>
    /// <param name="failedItems">Failed items store, cleared when an item is blacklisted.</param>
    /// <param name="logger">Logger instance.</param>
    public KometaThemesSkippedController(
        ILibraryManager libraryManager,
        FailedItemsStore failedItems,
        ILogger<KometaThemesSkippedController> logger)
    {
        _libraryManager = libraryManager;
        _failedItems = failedItems;
        _logger = logger;
    }

    /// <summary>
    /// Lists all permanently skipped items.
    /// </summary>
    /// <returns>A list of skipped item entries.</returns>
    [HttpGet("items")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult GetSkippedItems()
    {
        var configuration = Plugin.Instance?.Configuration;
        if (configuration == null)
        {
            return Ok(Array.Empty<object>());
        }

        // Return the entries directly: SkippedItemEntry carries camelCase
        // JsonPropertyName attributes the frontend relies on; an anonymous
        // projection would serialize PascalCase and break the table.
        return Ok(configuration.SkippedItems.ToList());
    }

    /// <summary>
    /// Adds an item to the skip list (blacklist) so it is never matched again.
    /// Idempotent: re-adding an already skipped item succeeds without duplicating it.
    /// </summary>
    /// <param name="itemId">The Jellyfin item ID to blacklist.</param>
    /// <param name="request">Optional request body with the skip reason.</param>
    /// <returns>A success message.</returns>
    [HttpPost("{itemId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult AddSkippedItem([FromRoute, Required] Guid itemId, [FromBody] SkipItemRequest? request = null)
    {
        var configuration = Plugin.Instance?.Configuration;
        if (configuration == null)
        {
            return NotFound(new { error = "Configuration not available" });
        }

        var item = _libraryManager.GetItemById(itemId);
        if (item == null)
        {
            return NotFound(new { error = "Item not found" });
        }

        var idString = itemId.ToString();
        var added = false;

        // The existence check and the insert run together under the shared configuration lock, so
        // two concurrent blacklist actions cannot both decide the entry is missing.
        Plugin.MutateConfiguration(config =>
        {
            var existing = config.SkippedItems
                .FirstOrDefault(s => Guid.TryParse(s.ItemId, out var g) && g == itemId);
            if (existing != null)
            {
                return;
            }

            config.SkippedItems.Add(new SkippedItemEntry
            {
                ItemId = idString,
                Name = item.Name ?? string.Empty,
                Type = item.GetBaseItemKind().ToString(),
                ProductionYear = item.ProductionYear,
                Reason = request?.Reason ?? string.Empty,
                SkippedUtc = DateTime.UtcNow
            });
            config.TrimSkippedItems();
            added = true;
        });

        if (added)
        {
            _logger.LogInformation("Blacklisted item {Id} ({Name})", idString, item.Name);
        }

        // A blacklisted item should no longer show up as failed/unresolved.
        _failedItems.Remove(itemId);

        return Ok(new { message = $"'{item.Name}' added to skip list" });
    }

    /// <summary>
    /// Removes a single item from the skip list.
    /// </summary>
    /// <param name="itemId">The Jellyfin item ID to remove from the skip list.</param>
    /// <returns>A success message or error.</returns>
    [HttpPost("{itemId}/remove")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult RemoveSkippedItem([FromRoute, Required] string itemId)
    {
        var configuration = Plugin.Instance?.Configuration;
        if (configuration == null)
        {
            return NotFound(new { error = "Configuration not available" });
        }

        SkippedItemEntry? entry = null;
        Plugin.MutateConfiguration(config =>
        {
            entry = config.SkippedItems
                .FirstOrDefault(s => string.Equals(s.ItemId, itemId, StringComparison.OrdinalIgnoreCase));
            if (entry != null)
            {
                config.SkippedItems.Remove(entry);
            }
        });

        if (entry == null)
        {
            return NotFound(new { error = "Skipped item not found" });
        }

        _logger.LogInformation("Removed skipped item {Id} ({Name})", entry.ItemId, entry.Name);

        return Ok(new { message = $"Removed '{entry.Name}' from skip list" });
    }

    /// <summary>
    /// Clears all skipped items.
    /// </summary>
    /// <returns>A success message with cleared count.</returns>
    [HttpPost("clear")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult ClearSkippedItems()
    {
        var configuration = Plugin.Instance?.Configuration;
        if (configuration == null)
        {
            return NotFound(new { error = "Configuration not available" });
        }

        var count = 0;
        Plugin.MutateConfiguration(config =>
        {
            count = config.SkippedItems.Count;
            config.SkippedItems.Clear();
        });

        _logger.LogInformation("Cleared {Count} skipped items", count);

        return Ok(new { message = $"Cleared {count} skipped items", removed = count });
    }

    /// <summary>
    /// Gets the total count of skipped items.
    /// </summary>
    /// <returns>The count of skipped items.</returns>
    [HttpGet("count")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult GetSkippedCount()
    {
        var count = Plugin.Instance?.Configuration?.SkippedItems.Count ?? 0;
        return Ok(new { count });
    }
}
