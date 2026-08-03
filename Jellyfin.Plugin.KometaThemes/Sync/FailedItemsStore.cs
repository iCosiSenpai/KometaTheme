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
using MediaBrowser.Controller.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KometaThemes.Sync;

/// <summary>
/// Persistent store of items that failed to resolve or download, so the
/// dashboard can surface them with retry/blacklist actions.
/// </summary>
/// <remarks>
/// Backed by a <see cref="ConcurrentDictionary{TKey, TValue}"/>. The previous version took a
/// blocking <c>SemaphoreSlim.Wait()</c> on every operation including reads, and entries were mutated
/// in place while <see cref="GetAll"/> handed the very same instances to a controller for
/// serialization — so a response could contain a half-updated entry. Updates now replace the entry
/// with a new object and readers get copies.
/// </remarks>
public sealed class FailedItemsStore : IDisposable
{
    /// <summary>
    /// Ceiling on tracked failures. Permanently unresolvable items accumulate one entry each and
    /// nothing ever aged them out, so the file and the dashboard list grew without bound.
    /// </summary>
    private const int MaxEntries = 5000;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _storePath;
    private readonly ILogger<FailedItemsStore> _logger;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly Timer _flushTimer;
    private readonly ConcurrentDictionary<string, FailedItemEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    private bool _dirty;
    private bool _disposed;

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
    public int Count => _entries.Count;

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

        // AddOrUpdate with a fresh instance rather than mutating the stored one, so a concurrent
        // GetAll can never observe an entry mid-update.
        _entries.AddOrUpdate(
            key,
            _ => new FailedItemEntry
            {
                ItemId = item.Id.ToString(),
                Name = item.Name ?? string.Empty,
                Type = item.GetBaseItemKind().ToString(),
                ProductionYear = item.ProductionYear,
                Reason = reason,
                Error = error,
                LastAttemptUtc = DateTime.UtcNow,
                Attempts = 1
            },
            (_, existing) => new FailedItemEntry
            {
                ItemId = existing.ItemId,
                Name = item.Name ?? existing.Name,
                Type = existing.Type,
                ProductionYear = existing.ProductionYear,
                Reason = reason,
                Error = error,
                LastAttemptUtc = DateTime.UtcNow,
                Attempts = existing.Attempts + 1
            });

        Volatile.Write(ref _dirty, true);
        EnforceCap();
    }

    /// <summary>
    /// Drops the least recently attempted entries once the store exceeds <see cref="MaxEntries"/>.
    /// </summary>
    private void EnforceCap()
    {
        if (_entries.Count <= MaxEntries)
        {
            return;
        }

        var excess = _entries.Count - MaxEntries;
        var oldest = _entries.ToArray()
            .OrderBy(pair => pair.Value.LastAttemptUtc)
            .Take(excess)
            .Select(pair => pair.Key);

        foreach (var staleKey in oldest)
        {
            _entries.TryRemove(staleKey, out _);
        }

        _logger.LogInformation("Failed-items store exceeded {Max} entries; dropped {Count} oldest", MaxEntries, excess);
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

        if (_entries.TryRemove(key, out _))
        {
            Volatile.Write(ref _dirty, true);
            return true;
        }

        return false;
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

        // Compare-and-remove: only drop the entry if it is still the Unresolved one we looked at,
        // so a failure recorded in between is not lost.
        if (_entries.TryGetValue(key, out var entry) &&
            entry.Reason == FailedItemReason.Unresolved &&
            _entries.TryRemove(new KeyValuePair<string, FailedItemEntry>(key, entry)))
        {
            Volatile.Write(ref _dirty, true);
        }
    }

    /// <summary>
    /// Gets all failed items, newest attempt first.
    /// </summary>
    /// <returns>Snapshot list of failed item entries.</returns>
    public IReadOnlyList<FailedItemEntry> GetAll()
    {
        // Copies, not the stored instances: the caller serializes these on a request thread while
        // a sync may be recording new failures.
        return _entries.Values
            .OrderByDescending(e => e.LastAttemptUtc)
            .Select(entry => new FailedItemEntry
            {
                ItemId = entry.ItemId,
                Name = entry.Name,
                Type = entry.Type,
                ProductionYear = entry.ProductionYear,
                Reason = entry.Reason,
                Error = entry.Error,
                LastAttemptUtc = entry.LastAttemptUtc,
                Attempts = entry.Attempts
            })
            .ToArray();
    }

    /// <summary>
    /// Clears all failed items.
    /// </summary>
    /// <returns>The number of removed entries.</returns>
    public int Clear()
    {
        var count = _entries.Count;
        if (count > 0)
        {
            _entries.Clear();
            Volatile.Write(ref _dirty, true);
        }

        return count;
    }

    /// <summary>
    /// Disposes the store, flushing remaining data to disk.
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
            _logger.LogError(ex, "Failed items store flush failed during dispose");
        }

        _fileLock.Dispose();
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
            _fileLock.Release();
        }
    }
}
