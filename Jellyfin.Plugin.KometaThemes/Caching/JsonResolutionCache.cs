using System;
using System.Collections.Generic;
using System.IO;
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
/// Thread-safe via SemaphoreSlim with debounced flush to disk.
/// </summary>
public sealed class JsonResolutionCache : IResolutionCache, IDisposable
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _cachePath;
    private readonly ILogger<JsonResolutionCache> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly Timer _flushTimer;

    private Dictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private bool _dirty;
    private long _hits;
    private long _misses;

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

        // Flush to disk every 30 seconds if dirty
        _flushTimer = new Timer(FlushTimerCallback, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    /// <inheritdoc />
    public bool TryGet(string key, out Anime[]? result)
    {
        _semaphore.Wait();
        try
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                var config = Plugin.Instance?.Configuration;

                // Clamp to at least one unit. A configured 0 made every lookup expire, evict and
                // mark the cache dirty, so the whole file was reserialized every 30s and the API
                // was hit at full rate on every sync.
                var positiveTtl = TimeSpan.FromDays(Math.Max(1, config?.PositiveCacheTtlDays ?? 7));
                var negativeTtl = TimeSpan.FromHours(Math.Max(1, config?.NegativeCacheTtlHours ?? 24));

                var ttl = entry.IsNegative ? negativeTtl : positiveTtl;

                if (DateTime.UtcNow - entry.Timestamp < ttl)
                {
                    Interlocked.Increment(ref _hits);
                    result = entry.IsNegative ? null : entry.Anime;
                    return true;
                }

                // Expired, remove
                _cache.Remove(key);
                Volatile.Write(ref _dirty, true);
            }

            Interlocked.Increment(ref _misses);
            result = null;
            return false;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public void SetPositive(string key, Anime[] anime)
    {
        _semaphore.Wait();
        try
        {
            _cache[key] = new CacheEntry
            {
                Anime = anime,
                IsNegative = false,
                Timestamp = DateTime.UtcNow
            };
            Volatile.Write(ref _dirty, true);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public void SetNegative(string key)
    {
        _semaphore.Wait();
        try
        {
            _cache[key] = new CacheEntry
            {
                Anime = null,
                IsNegative = true,
                Timestamp = DateTime.UtcNow
            };
            Volatile.Write(ref _dirty, true);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        _semaphore.Wait();
        try
        {
            _cache.Clear();
            Volatile.Write(ref _dirty, true);
            _logger.LogInformation("Resolution cache cleared");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public CacheStats GetStats()
    {
        _semaphore.Wait();
        try
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
        finally
        {
            _semaphore.Release();
        }
    }

    private void LoadFromDisk()
    {
        _semaphore.Wait();
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

            _cache.Clear();
            if (entries != null)
            {
                foreach (var kvp in entries)
                {
                    _cache[kvp.Key] = kvp.Value;
                }
            }

            _logger.LogInformation("Loaded {Count} entries from resolution cache", _cache.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load resolution cache from disk");
        }
        finally
        {
            _semaphore.Release();
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
        // Volatile: writers set _dirty under the semaphore, but this pre-check runs outside it,
        // so a plain read could observe a stale false and skip the flush entirely.
        if (!Volatile.Read(ref _dirty))
        {
            return;
        }

        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!Volatile.Read(ref _dirty))
            {
                return;
            }

            var json = JsonSerializer.Serialize(_cache, _jsonOptions);

            // Write to a sibling temp file and rename it over the target. WriteAllTextAsync
            // truncates the real file before streaming into it, so a crash mid-flush left a prefix
            // of valid JSON on disk — permanently unparseable, silently loaded as an empty cache,
            // and then overwritten by the next flush.
            var tempPath = _cachePath + ".tmp";
            await File.WriteAllTextAsync(tempPath, json).ConfigureAwait(false);
            File.Move(tempPath, _cachePath, overwrite: true);
            Volatile.Write(ref _dirty, false);
            _logger.LogDebug("Flushed {Count} cache entries to disk", _cache.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to flush resolution cache to disk");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Disposes the cache, flushing remaining data to disk.
    /// </summary>
    public void Dispose()
    {
        _flushTimer.Dispose();
        try
        {
            FlushToDiskAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cache flush failed during dispose");
        }

        _semaphore.Dispose();
    }

    private sealed class CacheEntry
    {
        public Anime[]? Anime { get; set; }

        public bool IsNegative { get; set; }

        public DateTime Timestamp { get; set; }
    }
}
