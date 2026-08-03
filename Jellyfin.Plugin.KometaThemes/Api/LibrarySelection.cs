using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.KometaThemes.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

#pragma warning disable SA1513, SA1508

namespace Jellyfin.Plugin.KometaThemes.Api;

/// <summary>
/// Shared helpers for selecting Jellyfin library items from plugin include/exclude settings.
/// </summary>
internal static class LibrarySelection
{
    /// <summary>
    /// Gets all series and movies from libraries matched by the plugin configuration.
    /// </summary>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="configuration">Plugin configuration.</param>
    /// <returns>Eligible Jellyfin library items.</returns>
    public static IEnumerable<BaseItem> GetEligibleItems(
        ILibraryManager libraryManager,
        PluginConfiguration configuration)
    {
        var isIncluded = LibraryPatternMatcher.Create(configuration.LibraryPattern);

        var libraryIds = libraryManager.GetVirtualFolders()
            .Where(lib => isIncluded(lib.Name))
            .Select(lib => Guid.TryParse(lib.ItemId, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToArray();

        var skipped = configuration.GetSkippedItemsDictionary();

        return libraryManager.GetItemList(new InternalItemsQuery
        {
            AncestorIds = libraryIds,
            IncludeItemTypes = new[] { BaseItemKind.Series, BaseItemKind.Movie },
            IsVirtualItem = false,
            Recursive = true
        }).Where(item => !skipped.ContainsKey(item.Id.ToString()));
    }

    /// <summary>
    /// Returns a localized "not eligible" error message based on the plugin's UiLanguage.
    /// </summary>
    /// <param name="config">Plugin configuration (for UiLanguage).</param>
    /// <returns>Localized error string.</returns>
    public static string GetNotEligibleErrorMessage(PluginConfiguration config)
    {
        var lang = (config?.UiLanguage ?? "en").Trim().ToLowerInvariant();
        if (lang.StartsWith("it", StringComparison.Ordinal))
        {
            return "L'elemento non appartiene a una libreria che corrisponde al Library Pattern configurato (es. 'Anime').";
        }
        return "Item is not in a library matching the configured Library Pattern (e.g. 'Anime').";
    }

    /// <summary>
    /// Determines if a specific item is eligible for KometaThemes UI and processing.
    /// Checks if the item belongs to a library matching the LibraryPattern and is not skipped.
    /// Uses parent walk + fallback query matching GetEligibleItems logic for robustness.
    /// </summary>
    /// <param name="item">The item to check.</param>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="configuration">Plugin configuration.</param>
    /// <returns>True if eligible.</returns>
    public static bool IsItemEligible(
        BaseItem item,
        ILibraryManager libraryManager,
        PluginConfiguration configuration)
    {
        if (item == null)
        {
            return false;
        }

        if (item.GetBaseItemKind() != BaseItemKind.Series &&
            item.GetBaseItemKind() != BaseItemKind.Movie)
        {
            return false;
        }

        var isIncluded = LibraryPatternMatcher.Create(configuration.LibraryPattern);

        var skipped = configuration.GetSkippedItemsDictionary();
        if (skipped.ContainsKey(item.Id.ToString()))
        {
            return false;
        }

        // Primary: walk parents to find CollectionFolder (fast path)
        var parents = item.GetParents();
        foreach (var parent in parents)
        {
            if (parent is CollectionFolder folder && isIncluded(folder.Name))
            {
                return true;
            }
        }

        // Fallback: check top parent
        var topParent = item.GetTopParent();
        if (topParent is CollectionFolder topFolder && isIncluded(topFolder.Name))
        {
            return true;
        }

        // Robust fallback: use the same AncestorIds logic as GetEligibleItems
        // This ensures consistency even if parent chain doesn't expose CollectionFolder directly.
        var libraryIds = libraryManager.GetVirtualFolders()
            .Where(lib => isIncluded(lib.Name))
            .Select(lib => Guid.TryParse(lib.ItemId, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToArray();

        if (libraryIds.Length == 0)
        {
            return false;
        }

        var queryResults = libraryManager.GetItemList(new InternalItemsQuery
        {
            ItemIds = new[] { item.Id },
            AncestorIds = libraryIds,
            IncludeItemTypes = new[] { item.GetBaseItemKind() },
            Limit = 1
        });

        return queryResults.Any();
    }

}
