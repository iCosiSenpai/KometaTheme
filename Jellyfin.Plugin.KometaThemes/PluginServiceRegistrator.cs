using System;
using System.Net.Http.Headers;
using Jellyfin.Plugin.KometaThemes.Api;
using Jellyfin.Plugin.KometaThemes.Caching;
using Jellyfin.Plugin.KometaThemes.Http;
using Jellyfin.Plugin.KometaThemes.Resolving;
using Jellyfin.Plugin.KometaThemes.Sync;
using Jellyfin.Plugin.KometaThemes.Web;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.KometaThemes;

/// <inheritdoc />
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        // Cache (singleton — shared across all syncs)
        serviceCollection.AddSingleton<IResolutionCache, JsonResolutionCache>();

        // API clients
        serviceCollection.AddSingleton<AnimeThemesApi>();
        serviceCollection.AddSingleton<AniListMetadataClient>();

        // Resolvers
        serviceCollection.AddSingleton<ExternalIdResolver>();
        serviceCollection.AddSingleton<TitleSearchResolver>();
        serviceCollection.AddSingleton<IAnimeResolver, CompositeResolver>();

        // Season detection & theme grouping
        serviceCollection.AddSingleton<SeasonDetector>();
        serviceCollection.AddSingleton<ThemeGrouper>();

        // Sync status tracking
        serviceCollection.AddSingleton<SyncStatusTracker>();
        serviceCollection.AddSingleton<DownloadMetrics>();
        serviceCollection.AddSingleton<FailedItemsStore>();
        serviceCollection.AddSingleton<SyncThemesRunner>();

        // Download tracking
        serviceCollection.AddSingleton<DownloadTracker>();

        // Playlist manager
        serviceCollection.AddSingleton<PlaylistManager>();

        // Downloader
        serviceCollection.AddSingleton<AnimeThemesDownloader>();

        // Theme link repair (Jellyfin 10.11.x ThemeMediaResolver workaround)
        serviceCollection.AddSingleton<ThemeLinkRepairService>();

        // YouTube theme import (external yt-dlp extractor)
        serviceCollection.AddSingleton<YouTube.YouTubeImportService>();

        // Library event handler for real-time sync on new items
        serviceCollection.AddSingleton<ItemRemovedHandler>();
        serviceCollection.AddSingleton<LibrarySyncHandler>();

        // Auto-inject the ♪ item button into the web client via File Transformation
        serviceCollection.AddHostedService<WebButtonInjectionRegistrar>();

        // HTTP handlers
        serviceCollection.AddTransient<PollyResilienceHandler>();
        serviceCollection.AddTransient<RateLimitingHandler>();

        var productHeader = new ProductInfoHeaderValue(
            "jf-plugin-kometathemes",
            applicationHost.ApplicationVersionString);

        serviceCollection
            .AddHttpClient("AnimeThemes", c =>
            {
                c.BaseAddress = new Uri("https://api.animethemes.moe");
                c.DefaultRequestHeaders.UserAgent.Add(productHeader);
            })
            .AddHttpMessageHandler<RateLimitingHandler>()
            .AddHttpMessageHandler<PollyResilienceHandler>();

        // CDN client for actual theme downloads (no rate limiting needed, but resilience is).
        // HttpClient.Timeout covers the streamed body read, not just the response headers, so the
        // previous 30s value aborted any theme video that took longer than that to transfer — the
        // failure was then swallowed and retried on every subsequent sync, forever. The per-attempt
        // budget lives in PollyResilienceHandler instead.
        serviceCollection
            .AddHttpClient("AnimeThemesCDN", c =>
            {
                c.DefaultRequestHeaders.UserAgent.Add(productHeader);
                c.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
            })
            .AddHttpMessageHandler<PollyResilienceHandler>();

        // AniList GraphQL client
        serviceCollection
            .AddHttpClient("AniList", c =>
            {
                c.BaseAddress = new Uri("https://graphql.anilist.co");
                c.DefaultRequestHeaders.UserAgent.Add(productHeader);
            })
            .AddHttpMessageHandler<PollyResilienceHandler>();
    }
}
