using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.KometaThemes.Api;
using Jellyfin.Plugin.KometaThemes.Configuration;
using Jellyfin.Plugin.KometaThemes.Models;
using Jellyfin.Plugin.KometaThemes.Sync;
using MediaBrowser.Controller.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KometaThemes.Resolving;

/// <summary>
/// Composite resolver that chains external ID resolvers in priority order
/// with an optional title search fallback.
/// </summary>
public class CompositeResolver : IAnimeResolver
{
    private readonly ExternalIdResolver _externalIdResolver;
    private readonly TitleSearchResolver _titleSearchResolver;
    private readonly AnimeThemesApi _api;
    private readonly FailedItemsStore _failedItems;
    private readonly ILogger<CompositeResolver> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompositeResolver"/> class.
    /// </summary>
    /// <param name="externalIdResolver">External ID resolver.</param>
    /// <param name="titleSearchResolver">Title search resolver.</param>
    /// <param name="api">AnimeThemes API client.</param>
    /// <param name="failedItems">Failed items store for tracking unresolved items.</param>
    /// <param name="logger">Logger.</param>
    public CompositeResolver(
        ExternalIdResolver externalIdResolver,
        TitleSearchResolver titleSearchResolver,
        AnimeThemesApi api,
        FailedItemsStore failedItems,
        ILogger<CompositeResolver> logger)
    {
        _externalIdResolver = externalIdResolver;
        _titleSearchResolver = titleSearchResolver;
        _api = api;
        _failedItems = failedItems;
        _logger = logger;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ItemWithAnime> ResolveItemsAsync(
        BaseItem[] items,
        PluginConfiguration config,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Build skipped item lookup for fast filtering (normalize to lowercase N-format)
        var skippedIds = new HashSet<string>(
            (config.SkippedItems ?? Enumerable.Empty<SkippedItemEntry>()).Select(s => NormalizeId(s.ItemId)),
            StringComparer.OrdinalIgnoreCase);

        // Filter to only Series and Movies, excluding skipped items
        var eligible = items
            .Where(it => (it.GetBaseItemKind() == BaseItemKind.Series || it.GetBaseItemKind() == BaseItemKind.Movie)
                         && !skippedIds.Contains(NormalizeId(it.Id.ToString())))
            .ToArray();

        if (eligible.Length == 0)
        {
            yield break;
        }

        // Track which items have been resolved
        var resolved = new HashSet<Guid>();

        // Manual bindings take precedence over automatic resolution.
        // Normalize keys so D/GUID-N formats and casing differences all match.
        // Built by hand rather than with ToDictionary: NormalizeId collapses the GUID "D" and "N"
        // formats and casing onto one key, while the write side de-duplicates with plain string
        // equality — so two bindings for the same item stored in different formats both survive in
        // the config and produced a duplicate key here, throwing before a single item resolved.
        // Last entry wins, matching the upsert semantics the controllers intend.
        var manualBindings = new Dictionary<string, ManualBindingEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var binding in config.ManualBindings)
        {
            var key = NormalizeId(binding.ItemId);
            if (key.Length == 0)
            {
                continue;
            }

            if (manualBindings.ContainsKey(key))
            {
                _logger.LogWarning("Duplicate manual binding for item {ItemId}; using the most recent entry", binding.ItemId);
            }

            manualBindings[key] = binding;
        }

        foreach (var item in eligible)
        {
            if (manualBindings.TryGetValue(NormalizeId(item.Id.ToString()), out var binding))
            {
                cancellationToken.ThrowIfCancellationRequested();

                _logger.LogInformation(
                    "[{Id}] Resolving via manual binding to AnimeThemes ID {AnimeId} ({Name})",
                    item.Id,
                    binding.AnimeId,
                    binding.AnimeName);

                var anime = await _api.GetAnimeByIdAsync(binding.AnimeId, cancellationToken).ConfigureAwait(false);
                if (anime != null)
                {
                    resolved.Add(item.Id);
                    _failedItems.RemoveIfUnresolved(item.Id);
                    yield return new ItemWithAnime(item, new System.Collections.ObjectModel.ReadOnlyCollection<Anime>(new[] { anime }));
                }
                else
                {
                    _logger.LogWarning(
                        "[{Id}] Manual binding to AnimeThemes ID {AnimeId} could not be resolved; falling back to automatic resolution",
                        item.Id,
                        binding.AnimeId);
                }
            }
        }

        // Get provider priority from config
        var priority = Sites.NormalizeProviderPriority(config.ProviderPriority);

        // Try each provider in priority order
        foreach (var site in priority)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Only pass unresolved items to the next resolver
            var unresolved = eligible.Where(it => !resolved.Contains(it.Id)).ToArray();
            if (unresolved.Length == 0)
            {
                break;
            }

            // Map site to Jellyfin provider ID
            var jellyfinProviderId = site;
            if (!Sites.ProviderToSite.ContainsKey(jellyfinProviderId))
            {
                _logger.LogWarning("Unknown provider site: {Site}, skipping", site);
                continue;
            }

            _logger.LogInformation(
                "Trying {Site} resolver for {Count} unresolved items",
                site,
                unresolved.Length);

#pragma warning disable CA2007
            await foreach (var result in _externalIdResolver.ResolveAsync(
                               Sites.ProviderToSite[jellyfinProviderId],
                               jellyfinProviderId,
                               unresolved,
                               cancellationToken))
#pragma warning restore CA2007
            {
                resolved.Add(result.Item.Id);
                _failedItems.RemoveIfUnresolved(result.Item.Id);
                yield return result;
            }
        }

        // Title fallback for remaining unresolved items
        if (config.EnableTitleFallback)
        {
            var unresolvedForTitle = eligible.Where(it => !resolved.Contains(it.Id)).ToArray();
            if (unresolvedForTitle.Length > 0)
            {
                // Log AnimeClick items specifically so Italian users know what's happening
                var animeClickCount = unresolvedForTitle.Count(it =>
                    it.ProviderIds.ContainsKey("AnimeClick"));
                if (animeClickCount > 0)
                {
                    _logger.LogInformation(
                        "{Count} items have only AnimeClick IDs — resolving via title search",
                        animeClickCount);
                }

                _logger.LogInformation(
                    "Attempting title fallback for {Count} unresolved items",
                    unresolvedForTitle.Length);

#pragma warning disable CA2007
                await foreach (var result in _titleSearchResolver.ResolveAsync(
                                   unresolvedForTitle,
                                   config.TitleMatchThreshold,
                                   cancellationToken))
#pragma warning restore CA2007
                {
                    resolved.Add(result.Item.Id);
                    _failedItems.RemoveIfUnresolved(result.Item.Id);
                    yield return result;
                }
            }
        }

        var totalUnresolved = 0;
        foreach (var item in eligible)
        {
            if (!resolved.Contains(item.Id))
            {
                totalUnresolved++;
                _failedItems.Record(item, FailedItemReason.Unresolved, null);
            }
        }

        if (totalUnresolved > 0)
        {
            _logger.LogInformation("{Count} items could not be resolved by any provider", totalUnresolved);
        }
    }

    private static string NormalizeId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return string.Empty;
        }

        // Try to parse as GUID, then return consistent N format (no hyphens, lowercase)
        if (Guid.TryParse(id, out var guid))
        {
            return guid.ToString("N").ToUpperInvariant();
        }

        // Fallback: strip hyphens and uppercase
        return id.Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
    }
}
