using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.KometaThemes.Models;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KometaThemes.Caching;

/// <summary>
/// JSON file-based resolution cache with TTL support for positive and negative entries.
/// </summary>
/// <remarks>
/// <para>
/// Backed by a <see cref="ConcurrentDictionary{TKey, TValue}"/> holding immutable entries. The
/// previous implementation took a <see cref="SemaphoreSlim"/> with a blocking <c>Wait()</c> on every
/// lookup, from async call paths, so each cache read parked a thread-pool thread and every read was
/// serialized against every other. No reader concurrency was possible even in principle, because
/// <see cref="TryGet"/> itself mutated the dictionary to evict expired entries.
/// </para>
/// <para>
/// Entries are also capped and swept. Expiry used to be evaluated only when something looked up that
/// exact key again, so keys for renamed or deleted library items were never revisited and stayed in
/// memory and in the file forever — and each positive entry holds a whole <see cref="Anime"/> graph.
/// </para>
/// </remarks>
public sealed class JsonResolutionCache : IResolutionCache, IDisposable
{
    /// <summary>
    /// Hard ceiling on cached entries. Beyond this the oldest are dropped, so a very large library
    /// or a long-lived server cannot grow the file without bound.
    /// </summary>
    private const int MaxEntries = 20000;

    /// <summary>
    /// How often to sweep expired entries, independently of whether anyone looks them up.
    /// </summary>
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(30);

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        // Not indented: each positive entry carries a full Anime graph, and pretty-printing roughly
        // doubled a file that is rewritten in its entirety on every flush.
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _cachePath;
    private readonly ILogger<JsonResolutionCache> _logger;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly Timer _flushTimer;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

    private DateTime _lastSweepUtc = DateTime.UtcNow;
    private bool _dirty;
    private long _hits;
    private long _misses;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonResolutionCache"/> class.
    /// </summary>
    /// <param name="applicationPaths">Application paths for finding the plugin data directory.</param>
    /// <param name="logger">Logger instance.</param>
    public JsonResolutionCache(IApplicationPaths applicationPaths, ILogger<JsonResolutionCache> logger)
    {
        _logger = logger;

        var pluginDir = Path.Combine(applicationPaths.PluginConfigurationsPath, "KometaThemes");
        Directory.CreateDirectory(pluginDir);
        _cachePath = Path.Combine(pluginDir, "resolution-cache.json");

        LoadFromDisk();

        _flushTimer = new Timer(FlushTimerCallback, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// Gets the configured positive TTL, floored at one unit.
    /// </summary>
    private static TimeSpan PositiveTtl
        => TimeSpan.FromDays(Math.Max(1, Plugin.Instance?.Configuration?.PositiveCacheTtlDays ?? 7));

    /// <summary>
    /// Gets the configured negative TTL, floored at one unit.
    /// </summary>
    private static TimeSpan NegativeTtl
        => TimeSpan.FromHours(Math.Max(1, Plugin.Instance?.Configuration?.NegativeCacheTtlHours ?? 24));

    /// <inheritdoc />
    public bool TryGet(string key, out Anime[]? result)
    {
        MaybeSweep();

        if (_cache.TryGetValue(key, out var entry))
        {
            if (!entry.IsExpired(PositiveTtl, NegativeTtl))
            {
                Interlocked.Increment(ref _hits);
                result = entry.IsNegative ? null : entry.Anime;
                return true;
            }

            if (_cache.TryRemove(key, out _))
            {
                Volatile.Write(ref _dirty, true);
            }
        }

        Interlocked.Increment(ref _misses);
        result = null;
        return false;
    }

    /// <inheritdoc />
    public void SetPositive(string key, Anime[] anime)
    {
        _cache[key] = new CacheEntry
        {
            Anime = anime,
            IsNegative = false,
            Timestamp = DateTime.UtcNow
        };
        Volatile.Write(ref _dirty, true);
        EnforceCap();
    }

    /// <inheritdoc />
    public void SetNegative(string key)
    {
        _cache[key] = new CacheEntry
        {
            Anime = null,
            IsNegative = true,
            Timestamp = DateTime.UtcNow
        };
        Volatile.Write(ref _dirty, true);
        EnforceCap();
    }

    /// <inheritdoc />
    public void Clear()
    {
        _cache.Clear();
        Volatile.Write(ref _dirty, true);
        _logger.LogInformation("Resolution cache cleared");
    }

    /// <inheritdoc />
    public CacheStats GetStats()
    {
        var positive = 0;
        var negative = 0;

        foreach (var entry in _cache.Values)
        {
            if (entry.IsNegative)
            {
                negative++;
            }
            else
            {
                positive++;
            }
        }

        return new CacheStats(positive, negative, Interlocked.Read(ref _hits), Interlocked.Read(ref _misses));
    }

    /// <summary>
    /// Drops expired entries on a schedule, so entries nobody queries again do not live forever.
    /// </summary>
    private void MaybeSweep()
    {
        if (DateTime.UtcNow - _lastSweepUtc < SweepInterval)
        {
            return;
        }

        _lastSweepUtc = DateTime.UtcNow;

        var positiveTtl = PositiveTtl;
        var negativeTtl = NegativeTtl;
        var removed = 0;

        foreach (var pair in _cache.ToArray())
        {
            if (pair.Value.IsExpired(positiveTtl, negativeTtl) && _cache.TryRemove(pair.Key, out _))
            {
                removed++;
            }
        }

        if (removed > 0)
        {
            Volatile.Write(ref _dirty, true);
            _logger.LogDebug("Swept {Count} expired resolution cache entries", removed);
        }
    }

    /// <summary>
    /// Trims the oldest entries once the cache exceeds <see cref="MaxEntries"/>.
    /// </summary>
    private void EnforceCap()
    {
        if (_cache.Count <= MaxEntries)
        {
            return;
        }

        var excess = _cache.Count - MaxEntries;
        var oldest = _cache.ToArray()
            .OrderBy(pair => pair.Value.Timestamp)
            .Take(excess)
            .Select(pair => pair.Key);

        foreach (var key in oldest)
        {
            _cache.TryRemove(key, out _);
        }

        _logger.LogInformation("Resolution cache exceeded {Max} entries; dropped {Count} oldest", MaxEntries, excess);
    }

    private void LoadFromDisk()
    {
        try
        {
            if (!File.Exists(_cachePath))
            {
                return;
            }

            var json = File.ReadAllText(_cachePath);
            Dictionary<string, CacheEntry>? entries;
            try
            {
                entries = JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(json);
            }
            catch (JsonException ex)
            {
                // Preserve the bad file instead of letting the next flush silently overwrite it.
                _logger.LogError(ex, "Resolution cache at {Path} is corrupt; quarantining it", _cachePath);
                try
                {
                    File.Move(_cachePath, _cachePath + ".corrupt", overwrite: true);
                }
                catch (Exception moveEx)
                {
                    _logger.LogWarning(moveEx, "Failed to quarantine corrupt resolution cache");
                }

                entries = null;
            }

            if (entries == null)
            {
                return;
            }

            var positiveTtl = PositiveTtl;
            var negativeTtl = NegativeTtl;
            var skipped = 0;

            foreach (var kvp in entries)
            {
                // Drop entries that already expired while the server was down, rather than carrying
                // them forward until something happens to look them up.
                if (kvp.Value.IsExpired(positiveTtl, negativeTtl))
                {
                    skipped++;
                    continue;
                }

                _cache[kvp.Key] = kvp.Value;
            }

            if (skipped > 0)
            {
                Volatile.Write(ref _dirty, true);
            }

            EnforceCap();
            _logger.LogInformation(
                "Loaded {Count} entries from resolution cache ({Skipped} expired)",
                _cache.Count,
                skipped);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load resolution cache from disk");
        }
    }

    private async void FlushTimerCallback(object? state)
    {
        try
        {
            await FlushToDiskAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cache flush timer failed");
        }
    }

    private async Task FlushToDiskAsync()
    {
        // Volatile: writers set _dirty outside this method, so a plain read could observe a stale
        // false and skip the flush entirely.
        if (!Volatile.Read(ref _dirty))
        {
            return;
        }

        await _fileLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!Volatile.Read(ref _dirty))
            {
                return;
            }

            // Snapshot before serializing: the dictionary is live and a concurrent write would
            // otherwise be able to change it mid-serialization.
            var snapshot = _cache.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            var json = JsonSerializer.Serialize(snapshot, _jsonOptions);

            // Write to a sibling temp file and rename it over the target. WriteAllTextAsync
            // truncates the real file before streaming into it, so a crash mid-flush left a prefix
            // of valid JSON on disk — permanently unparseable, silently loaded as an empty cache,
            // and then overwritten by the next flush.
            var tempPath = _cachePath + ".tmp";
            await File.WriteAllTextAsync(tempPath, json).ConfigureAwait(false);
            File.Move(tempPath, _cachePath, overwrite: true);
            Volatile.Write(ref _dirty, false);
            _logger.LogDebug("Flushed {Count} cache entries to disk", snapshot.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to flush resolution cache to disk");
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>
    /// Disposes the cache, flushing remaining data to disk.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _flushTimer.Dispose();

        try
        {
            FlushToDiskAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cache flush failed during dispose");
        }

        _fileLock.Dispose();
    }

    private sealed class CacheEntry
    {
        public Anime[]? Anime { get; set; }

        public bool IsNegative { get; set; }

        public DateTime Timestamp { get; set; }

        public bool IsExpired(TimeSpan positiveTtl, TimeSpan negativeTtl)
            => DateTime.UtcNow - Timestamp >= (IsNegative ? negativeTtl : positiveTtl);
    }
}
