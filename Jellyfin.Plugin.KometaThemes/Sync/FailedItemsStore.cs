using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.KometaThemes.Models;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KometaThemes.Sync;

/// <summary>
/// Persistent store of items that failed to resolve or download, so the
/// dashboard can surface them with retry/blacklist actions.
/// Thread-safe via SemaphoreSlim with debounced flush to disk.
/// </summary>
public sealed class FailedItemsStore : IDisposable
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _storePath;
    private readonly ILogger<FailedItemsStore> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly Timer _flushTimer;
    private readonly Dictionary<string, FailedItemEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    private bool _dirty;

    /// <summary>
    /// Initializes a new instance of the <see cref="FailedItemsStore"/> class.
    /// </summary>
    /// <param name="applicationPaths">Application paths for finding the plugin data directory.</param>
    /// <param name="logger">Logger instance.</param>
    public FailedItemsStore(IApplicationPaths applicationPaths, ILogger<FailedItemsStore> logger)
    {
        _logger = logger;

        var pluginDir = Path.Combine(applicationPaths.PluginConfigurationsPath, "KometaThemes");
        Directory.CreateDirectory(pluginDir);
        _storePath = Path.Combine(pluginDir, "failed-items.json");

        LoadFromDisk();

        _flushTimer = new Timer(FlushTimerCallback, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// Gets the number of tracked failed items.
    /// </summary>
    public int Count
    {
        get
        {
            _semaphore.Wait();
            try
            {
                return _entries.Count;
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }

    /// <summary>
    /// Records a failure for an item, bumping the attempt counter if it already exists.
    /// </summary>
    /// <param name="item">The library item that failed.</param>
    /// <param name="reason">Why the item failed.</param>
    /// <param name="error">Optional error message of this attempt.</param>
    public void Record(BaseItem item, FailedItemReason reason, string? error)
    {
        var key = NormalizeId(item.Id.ToString());
        if (key.Length == 0)
        {
            return;
        }

        _semaphore.Wait();
        try
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                existing.Name = item.Name ?? existing.Name;
                existing.Reason = reason;
                existing.Error = error;
                existing.LastAttemptUtc = DateTime.UtcNow;
                existing.Attempts++;
            }
            else
            {
                _entries[key] = new FailedItemEntry
                {
                    ItemId = item.Id.ToString(),
                    Name = item.Name ?? string.Empty,
                    Type = item.GetBaseItemKind().ToString(),
                    ProductionYear = item.ProductionYear,
                    Reason = reason,
                    Error = error,
                    LastAttemptUtc = DateTime.UtcNow,
                    Attempts = 1
                };
            }

            Volatile.Write(ref _dirty, true);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Removes an item from the failed list (after a later success, a dismiss, or a blacklist).
    /// No-op when the item is not tracked.
    /// </summary>
    /// <param name="itemId">The Jellyfin item ID.</param>
    /// <returns>True when an entry was removed.</returns>
    public bool Remove(Guid itemId)
    {
        return Remove(itemId.ToString());
    }

    /// <summary>
    /// Removes an item from the failed list by its string ID.
    /// </summary>
    /// <param name="itemId">The Jellyfin item ID.</param>
    /// <returns>True when an entry was removed.</returns>
    public bool Remove(string itemId)
    {
        var key = NormalizeId(itemId);
        if (key.Length == 0)
        {
            return false;
        }

        _semaphore.Wait();
        try
        {
            if (_entries.Remove(key))
            {
                Volatile.Write(ref _dirty, true);
                return true;
            }

            return false;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Removes an item only when its current reason is <see cref="FailedItemReason.Unresolved"/>.
    /// Used when a resolver succeeds but the download outcome is still unknown.
    /// </summary>
    /// <param name="itemId">The Jellyfin item ID.</param>
    public void RemoveIfUnresolved(Guid itemId)
    {
        var key = NormalizeId(itemId.ToString());
        if (key.Length == 0)
        {
            return;
        }

        _semaphore.Wait();
        try
        {
            if (_entries.TryGetValue(key, out var entry) && entry.Reason == FailedItemReason.Unresolved)
            {
                _entries.Remove(key);
                Volatile.Write(ref _dirty, true);
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Gets all failed items, newest attempt first.
    /// </summary>
    /// <returns>Snapshot list of failed item entries.</returns>
    public IReadOnlyList<FailedItemEntry> GetAll()
    {
        _semaphore.Wait();
        try
        {
            return _entries.Values
                .OrderByDescending(e => e.LastAttemptUtc)
                .ToArray();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Clears all failed items.
    /// </summary>
    /// <returns>The number of removed entries.</returns>
    public int Clear()
    {
        _semaphore.Wait();
        try
        {
            var count = _entries.Count;
            if (count > 0)
            {
                _entries.Clear();
                Volatile.Write(ref _dirty, true);
            }

            return count;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Disposes the store, flushing remaining data to disk.
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
            _logger.LogError(ex, "Failed items store flush failed during dispose");
        }

        _semaphore.Dispose();
    }

    private static string NormalizeId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return string.Empty;
        }

        if (Guid.TryParse(id, out var guid))
        {
            return guid.ToString("N").ToUpperInvariant();
        }

        return id.Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
    }

    private void LoadFromDisk()
    {
        _semaphore.Wait();
        try
        {
            if (!File.Exists(_storePath))
            {
                return;
            }

            var json = File.ReadAllText(_storePath);
            List<FailedItemEntry>? entries;
            try
            {
                entries = JsonSerializer.Deserialize<List<FailedItemEntry>>(json);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed items store at {Path} is corrupt; quarantining it", _storePath);
                try
                {
                    File.Move(_storePath, _storePath + ".corrupt", overwrite: true);
                }
                catch (Exception moveEx)
                {
                    _logger.LogWarning(moveEx, "Failed to quarantine corrupt failed items store");
                }

                entries = null;
            }

            _entries.Clear();
            if (entries != null)
            {
                foreach (var entry in entries)
                {
                    var key = NormalizeId(entry.ItemId);
                    if (key.Length > 0)
                    {
                        _entries[key] = entry;
                    }
                }
            }

            _logger.LogInformation("Loaded {Count} entries from failed items store", _entries.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load failed items store from disk");
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
            _logger.LogError(ex, "Failed items store flush timer failed");
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

            var json = JsonSerializer.Serialize(_entries.Values.ToList(), _jsonOptions);

            // Temp file plus rename: WriteAllTextAsync truncates the target before streaming into
            // it, so a crash mid-flush left unparseable JSON that was then silently loaded as an
            // empty store, erasing the entire Unresolved list.
            var tempPath = _storePath + ".tmp";
            await File.WriteAllTextAsync(tempPath, json).ConfigureAwait(false);
            File.Move(tempPath, _storePath, overwrite: true);
            Volatile.Write(ref _dirty, false);
            _logger.LogDebug("Flushed {Count} failed item entries to disk", _entries.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to flush failed items store to disk");
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
