using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.KometaThemes.Api;
using Jellyfin.Plugin.KometaThemes.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.KometaThemes;

/// <summary>
/// The main plugin.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Serializes read-modify-write access to the shared configuration object.
    /// </summary>
    private static readonly object ConfigurationWriteLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    /// <param name="syncHandler">The <see cref="LibrarySyncHandler"/> — injected to force construction so it subscribes to library events.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, LibrarySyncHandler syncHandler)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        MigrateMutedVideoDefault();
        NormalizeProviderPriorityConfig();
        Configuration.NormalizeBounds();
    }

    /// <inheritdoc />
    public override string Name => "KometaThemes";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("48c98707-45d1-43ac-94b8-f74d875ad29c");

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <summary>
    /// Applies a change to the plugin configuration and persists it, under a lock shared by every
    /// writer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="BasePlugin{T}.Configuration"/> is a single mutable object shared by every request,
    /// and the skipped-items and manual-bindings properties are plain <c>Collection&lt;T&gt;</c>.
    /// Callers used to do an unsynchronized find-remove-add and then call
    /// <c>SaveConfiguration</c> themselves. Two concurrent dashboard actions
    /// could therefore interleave and lose one of the two entries, and
    /// <c>XmlSerializer</c> walking a collection while another thread mutated it throws
    /// <c>Collection was modified</c> part-way through writing the file. The base class locks the
    /// file write but nothing protected the object graph.
    /// </para>
    /// <para>
    /// Mutations must not block: this lock is held across the synchronous config write, so callers
    /// should do their bookkeeping inside the delegate and any I/O outside it.
    /// </para>
    /// </remarks>
    /// <param name="mutate">The change to apply. Runs while the lock is held.</param>
    /// <returns>Whether the change was applied. False only when the plugin is not initialised.</returns>
    public static bool MutateConfiguration(Action<PluginConfiguration> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        var plugin = Instance;
        if (plugin == null)
        {
            return false;
        }

        lock (ConfigurationWriteLock)
        {
            mutate(plugin.Configuration);
            plugin.SaveConfiguration();
        }

        return true;
    }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        var ns = GetType().Namespace;
        return
        [
            // Pages (cache-busting is handled by the in-page bootstrap via ?v=, not by duplicate names)
            new PluginPageInfo { Name = this.Name, EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", ns) },
            new PluginPageInfo { Name = "KometaThemesSearch", EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Web.SearchPage.html", ns) },
            new PluginPageInfo { Name = "KometaThemesItem", EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.itemPage.html", ns), EnableInMainMenu = true, DisplayName = "KometaThemes" },

            // Shared assets, served through /web/configurationpage?name=...
            new PluginPageInfo { Name = "KometaThemesCss", EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Web.assets.kometa.css", ns) },
            new PluginPageInfo { Name = "KometaThemesIconPng", EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Web.assets.kometathemes-icon.png", ns) },
            new PluginPageInfo { Name = "KometaThemesLoaderJs", EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Web.assets.kometa-loader.js", ns) },
            new PluginPageInfo { Name = "KometaThemesCoreJs", EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Web.assets.kometa-core.js", ns) },
            new PluginPageInfo { Name = "KometaThemesA11yJs", EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Web.assets.kometa-a11y.js", ns) },
            new PluginPageInfo { Name = "KometaThemesConfigJs", EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Web.assets.config.js", ns) },
            new PluginPageInfo { Name = "KometaThemesSearchJs", EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Web.assets.search.js", ns) },
            new PluginPageInfo { Name = "KometaThemesItemJs", EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Web.assets.item.js", ns) }
        ];
    }

    /// <summary>
    /// Repairs and seeds the provider priority list once at load. Earlier builds seeded
    /// defaults in the configuration constructor, which — combined with XmlSerializer
    /// appending to (never clearing) collection properties on deserialize — made the
    /// saved list grow by five entries on every load/save cycle. This dedupes any such
    /// accumulation back to the canonical order and seeds the defaults on a fresh install.
    /// </summary>
    private void NormalizeProviderPriorityConfig()
    {
        var configuration = Configuration;
        var normalized = Sites.NormalizeProviderPriority(configuration.ProviderPriority);

        if (configuration.ProviderPriority.Count == normalized.Count
            && configuration.ProviderPriority.SequenceEqual(normalized, StringComparer.Ordinal))
        {
            return;
        }

        configuration.ProviderPriority = normalized;
        SaveConfiguration();
    }

    private void MigrateMutedVideoDefault()
    {
        var configuration = Configuration;
        if (configuration.VideoVolumeDefaultMigrated)
        {
            return;
        }

        var changed = false;
        if (configuration.VideoSettings?.Volume <= 0.01)
        {
            configuration.VideoSettings.Volume = 0.5;
            changed = true;
        }

        if (configuration.MovieSettings?.VideoSettings?.Volume <= 0.01)
        {
            configuration.MovieSettings.VideoSettings.Volume = 0.5;
            changed = true;
        }

        configuration.VideoVolumeDefaultMigrated = true;
        if (changed)
        {
            SaveConfiguration();
        }
    }
}
