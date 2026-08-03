using System;
using System.Linq;
using Jellyfin.Plugin.KometaThemes.Configuration;
using Jellyfin.Plugin.KometaThemes.Models;
using Xunit;

namespace Jellyfin.Plugin.KometaThemes.Tests;

/// <summary>
/// Both persisted lists live inside the single configuration XML file, which is rewritten in full on
/// every save, and the bindings listing does a library lookup per entry on every GET. Neither list
/// had a cap or any pruning of entries whose item no longer exists.
/// </summary>
public class PluginConfigurationTests
{
    private static SkippedItemEntry Skipped(string id, DateTime when)
        => new() { ItemId = id, Name = id, SkippedUtc = when };

    private static ManualBindingEntry Binding(string id, DateTime when)
        => new() { ItemId = id, AnimeId = 1, AnimeName = id, BoundAt = when };

    [Fact]
    public void TrimSkippedItems_UnderTheCap_ChangesNothing()
    {
        var config = new PluginConfiguration();
        config.SkippedItems.Add(Skipped("a", DateTime.UtcNow));

        config.TrimSkippedItems();

        Assert.Single(config.SkippedItems);
    }

    [Fact]
    public void TrimSkippedItems_KeepsTheNewestUpToTheCap()
    {
        var config = new PluginConfiguration();
        var baseTime = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Oldest first, so a naive "take the first N" would keep exactly the wrong ones.
        for (var i = 0; i < PluginConfiguration.MaxPersistedListEntries + 25; i++)
        {
            config.SkippedItems.Add(Skipped("item-" + i, baseTime.AddMinutes(i)));
        }

        config.TrimSkippedItems();

        Assert.Equal(PluginConfiguration.MaxPersistedListEntries, config.SkippedItems.Count);
        Assert.DoesNotContain(config.SkippedItems, entry => entry.ItemId == "item-0");
        Assert.Contains(config.SkippedItems, entry => entry.ItemId == "item-" + (PluginConfiguration.MaxPersistedListEntries + 24));
    }

    [Fact]
    public void TrimManualBindings_KeepsTheNewestUpToTheCap()
    {
        var config = new PluginConfiguration();
        var baseTime = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < PluginConfiguration.MaxPersistedListEntries + 10; i++)
        {
            config.ManualBindings.Add(Binding("item-" + i, baseTime.AddMinutes(i)));
        }

        config.TrimManualBindings();

        Assert.Equal(PluginConfiguration.MaxPersistedListEntries, config.ManualBindings.Count);
        Assert.DoesNotContain(config.ManualBindings, entry => entry.ItemId == "item-0");
    }

    [Fact]
    public void PruneMissingItems_DropsEntriesWhoseItemIsGone()
    {
        var config = new PluginConfiguration();
        config.SkippedItems.Add(Skipped("present", DateTime.UtcNow));
        config.SkippedItems.Add(Skipped("gone", DateTime.UtcNow));
        config.ManualBindings.Add(Binding("present", DateTime.UtcNow));
        config.ManualBindings.Add(Binding("gone", DateTime.UtcNow));

        var removed = config.PruneMissingItems(id => id == "present");

        Assert.Equal(2, removed);
        Assert.Single(config.SkippedItems);
        Assert.Single(config.ManualBindings);
        Assert.Equal("present", config.SkippedItems[0].ItemId);
        Assert.Equal("present", config.ManualBindings[0].ItemId);
    }

    [Fact]
    public void GetSkippedItemsDictionary_SurvivesDuplicateIds()
    {
        // ToDictionary threw here, and nothing guarantees uniqueness: the controllers de-duplicate
        // with plain string equality, so the same item under two GUID formats appears twice.
        var config = new PluginConfiguration();
        config.SkippedItems.Add(Skipped("dupe", DateTime.UtcNow));
        config.SkippedItems.Add(Skipped("dupe", DateTime.UtcNow));

        var map = config.GetSkippedItemsDictionary();

        Assert.Single(map);
        Assert.True(map.ContainsKey("dupe"));
    }

    [Fact]
    public void GetManualBindingsDictionary_SurvivesDuplicateIds()
    {
        var config = new PluginConfiguration();
        config.ManualBindings.Add(Binding("dupe", DateTime.UtcNow));
        config.ManualBindings.Add(Binding("dupe", DateTime.UtcNow));

        Assert.Single(config.GetManualBindingsDictionary());
    }

    [Fact]
    public void NormalizeBounds_FloorsCacheTtlsAtOne()
    {
        // Zero made every lookup expire on read, which evicted the entry, marked the cache dirty and
        // reserialized the whole file every 30 seconds while never producing a single hit.
        var config = new PluginConfiguration
        {
            PositiveCacheTtlDays = 0,
            NegativeCacheTtlHours = 0
        };

        config.NormalizeBounds();

        Assert.Equal(1, config.PositiveCacheTtlDays);
        Assert.Equal(1, config.NegativeCacheTtlHours);
    }

    [Fact]
    public void NormalizeBounds_ClampsRateLimitToWhatTheHandlerEnforces()
    {
        // The dashboard used to accept up to 300 while the HTTP handler silently capped at 90.
        var config = new PluginConfiguration { RateLimitPerMinute = 300 };
        config.NormalizeBounds();
        Assert.Equal(Jellyfin.Plugin.KometaThemes.Http.RateLimitingHandler.MaxRatePerMinute, config.RateLimitPerMinute);

        config = new PluginConfiguration { RateLimitPerMinute = 0 };
        config.NormalizeBounds();
        Assert.Equal(Jellyfin.Plugin.KometaThemes.Http.RateLimitingHandler.MinRatePerMinute, config.RateLimitPerMinute);
    }

    [Fact]
    public void YouTubeImport_IsOptInAndUnconfiguredByDefault()
    {
        var config = new PluginConfiguration();
        Assert.False(config.EnableYouTubeImport);
        Assert.True(string.IsNullOrEmpty(config.YtDlpPath));
    }
}
