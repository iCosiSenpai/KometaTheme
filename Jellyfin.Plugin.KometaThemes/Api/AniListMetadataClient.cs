using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KometaThemes.Api;

/// <summary>
/// Minimal AniList GraphQL client used to enrich title candidates.
/// </summary>
public sealed class AniListMetadataClient
{
    private readonly HttpClient _client;
    private readonly ILogger<AniListMetadataClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AniListMetadataClient"/> class.
    /// </summary>
    /// <param name="clientFactory">HTTP client factory.</param>
    /// <param name="logger">Logger.</param>
    public AniListMetadataClient(IHttpClientFactory clientFactory, ILogger<AniListMetadataClient> logger)
    {
        _client = clientFactory.CreateClient("AniList");
        _logger = logger;
    }

    /// <summary>
    /// Gets AniList title metadata for a Jellyfin item when AniList or MAL IDs are available.
    /// </summary>
    /// <param name="item">Jellyfin library item.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>AniList metadata or null.</returns>
    public async Task<AniListMedia?> GetMediaForItemAsync(BaseItem item, CancellationToken cancellationToken)
    {
        try
        {
            if (item.ProviderIds.TryGetValue("AniList", out var aniListId) &&
                int.TryParse(aniListId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                return await GetMediaAsync("id", id, cancellationToken).ConfigureAwait(false);
            }

            if (item.ProviderIds.TryGetValue("MyAnimeList", out var malId) &&
                int.TryParse(malId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idMal))
            {
                return await GetMediaAsync("idMal", idMal, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogDebug(ex, "AniList enrichment failed for {Name}", item.Name);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(ex, "AniList enrichment timed out for {Name}", item.Name);
        }

        return null;
    }

    private async Task<AniListMedia?> GetMediaAsync(string idField, int id, CancellationToken cancellationToken)
    {
        var query = idField == "idMal"
            ? "query ($idMal: Int) { Media(idMal: $idMal, type: ANIME) { id idMal title { romaji english native userPreferred } synonyms } }"
            : "query ($id: Int) { Media(id: $id, type: ANIME) { id idMal title { romaji english native userPreferred } synonyms } }";

        var request = new AniListGraphQlRequest(query, new Dictionary<string, int> { [idField] = id });
        using var response = await _client.PostAsJsonAsync(string.Empty, request, cancellationToken).ConfigureAwait(false);

        if ((int)response.StatusCode == 429)
        {
            _logger.LogDebug("AniList rate limited title enrichment for {Field}={Id}", idField, id);
            return null;
        }

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<AniListGraphQlResponse>(cancellationToken).ConfigureAwait(false);

        // GraphQL reports failures as HTTP 200 with an errors array, so a malformed query, an unknown
        // id or an in-body rate limit all used to surface as a silent null with nothing logged.
        if (payload?.Errors is { Count: > 0 })
        {
            _logger.LogDebug(
                "AniList returned {Count} GraphQL error(s) for {Field}={Id}: {Message}",
                payload.Errors.Count,
                idField,
                id,
                payload.Errors[0].Message);
            return null;
        }

        return payload?.Data?.Media;
    }

    private sealed record AniListGraphQlRequest(
        [property: JsonPropertyName("query")] string Query,
        [property: JsonPropertyName("variables")] IReadOnlyDictionary<string, int> Variables);

    private sealed record AniListGraphQlResponse(
        [property: JsonPropertyName("data")] AniListGraphQlData? Data,
        [property: JsonPropertyName("errors")] IReadOnlyList<AniListGraphQlError>? Errors);

    private sealed record AniListGraphQlError(
        [property: JsonPropertyName("message")] string? Message);

    private sealed record AniListGraphQlData(
        [property: JsonPropertyName("Media")] AniListMedia? Media);
}
