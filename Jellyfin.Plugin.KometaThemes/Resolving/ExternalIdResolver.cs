using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Jellyfin.Plugin.KometaThemes.Api;
using Jellyfin.Plugin.KometaThemes.Caching;
using Jellyfin.Plugin.KometaThemes.Configuration;
using Jellyfin.Plugin.KometaThemes.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KometaThemes.Resolving;

/// <summary>
/// Resolver that looks up anime by external provider IDs (AniDB, MAL, AniList, etc.).
/// Parametrized by site name to support all providers with the same code.
/// </summary>
public class ExternalIdResolver
{
    private readonly AnimeThemesApi _api;
    private readonly IResolutionCache _cache;
    private readonly ILogger<ExternalIdResolver> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExternalIdResolver"/> class.
    /// </summary>
    /// <param name="api">AnimeThemes API instance.</param>
    /// <param name="cache">Resolution cache.</param>
    /// <param name="logger">Logger.</param>
    public ExternalIdResolver(AnimeThemesApi api, IResolutionCache cache, ILogger<ExternalIdResolver> logger)
    {
        _api = api;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Resolves items using external IDs for the given site.
    /// </summary>
    /// <param name="site">The site name (e.g. "AniDB", "MyAnimeList").</param>
    /// <param name="jellyfinProviderId">The Jellyfin provider ID key.</param>
    /// <param name="items">Items to resolve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of resolved items.</returns>
    public async IAsyncEnumerable<ItemWithAnime> ResolveAsync(
        string site,
        string jellyfinProviderId,
        BaseItem[] items,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Group items by their external ID for this provider
        var itemsById = new Dictionary<string, List<BaseItem>>(StringComparer.OrdinalIgnoreCase);
        var cachedResults = new List<(BaseItem Item, Anime[] Anime)>();

        foreach (var item in items)
        {
            if (!item.TryGetProviderId(jellyfinProviderId, out var externalId) || string.IsNullOrEmpty(externalId))
            {
                continue;
            }

            var cacheKey = $"{site}:{externalId}";

            if (_cache.TryGet(cacheKey, out var cached))
            {
                if (cached != null && cached.Length > 0)
                {
                    cachedResults.Add((item, cached));
                }

                // Skip API call for this ID (even for negative cache)
                continue;
            }

            if (!itemsById.TryGetValue(externalId, out var list))
            {
                list = [];
                itemsById[externalId] = list;
            }

            list.Add(item);
        }

        // Yield cached results first
        foreach (var (item, anime) in cachedResults)
        {
            yield return new ItemWithAnime(item, new ReadOnlyCollection<Anime>(anime));
        }

        // Batch fetch uncached IDs from API
        if (itemsById.Count > 0)
        {
            _logger.LogInformation("Resolving {Count} uncached IDs via {Site}", itemsById.Count, site);

            var result = await _api.FindByExternalIdsAsync(site, itemsById.Keys, cancellationToken).ConfigureAwait(false);

            foreach (var (externalId, animeArray) in result)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var cacheKey = $"{site}:{externalId}";

                // A match with no downloadable media is not a useful match. Caching it positively
                // marked the item resolved, cleared it from the failed-items store and then
                // downloaded nothing — so it silently disappeared from the dashboard and was not
                // retried until the positive TTL expired days later. This guard existed but was
                // never wired up to a caller.
                if (animeArray.Length > 0 && AnimeThemeAvailability.HasAnyUsableTheme(animeArray))
                {
                    _cache.SetPositive(cacheKey, animeArray);

                    if (itemsById.TryGetValue(externalId, out var matchedItems))
                    {
                        foreach (var item in matchedItems)
                        {
                            yield return new ItemWithAnime(item, new ReadOnlyCollection<Anime>(animeArray));
                        }
                    }
                }
                else
                {
                    if (animeArray.Length > 0)
                    {
                        _logger.LogInformation(
                            "{Site}:{ExternalId} matched {Count} anime but none have downloadable themes",
                            site,
                            externalId,
                            animeArray.Length);
                    }

                    _cache.SetNegative(cacheKey);
                }
            }
        }
    }
}
