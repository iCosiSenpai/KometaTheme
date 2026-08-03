using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.KometaThemes.Api;
using Jellyfin.Plugin.KometaThemes.Configuration;
using Jellyfin.Plugin.KometaThemes.Sync;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

#pragma warning disable SA1611, SA1615, CS1591

namespace Jellyfin.Plugin.KometaThemes;

/// <summary>
/// Reacts to library item additions and triggers theme sync for new anime.
/// Constructed eagerly via <see cref="Plugin"/> to subscribe to <see cref="ILibraryManager.ItemAdded"/>.
/// </summary>
public sealed class LibrarySyncHandler : IDisposable
{
    /// <summary>
    /// Cap on the pending set, so a full-library scan cannot grow it without bound.
    /// </summary>
    private const int MaxPendingItems = 5000;

    private static readonly TimeSpan DebounceWindow = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Upper bound on how long a batch may be deferred. The debounce window is reset by every new
    /// item, so during a large library scan the sync used to be pushed back indefinitely while the
    /// pending set grew without limit. Once the first queued item is this old the batch runs
    /// regardless of incoming activity.
    /// </summary>
    private static readonly TimeSpan MaxDeferral = TimeSpan.FromMinutes(5);

    private readonly ILibraryManager _libraryManager;
    private readonly SyncThemesRunner _runner;
    private readonly ILogger<LibrarySyncHandler> _logger;
    private readonly object _batchLock = new();
    private readonly HashSet<Guid> _pendingItems = new();
#pragma warning disable CA2213
    private readonly SemaphoreSlim _runGate = new(2, 2);
#pragma warning restore CA2213
    private readonly ItemRemovedHandler _itemRemovedHandler;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Timer _debounceTimer;
    private DateTime _firstQueuedUtc = DateTime.MinValue;
    private int _flushing;
    private bool _disposed;

    public LibrarySyncHandler(
        ILibraryManager libraryManager,
        SyncThemesRunner runner,
        ILogger<LibrarySyncHandler> logger,
        ItemRemovedHandler itemRemovedHandler)
    {
        _libraryManager = libraryManager;
        _runner = runner;
        _logger = logger;
        _itemRemovedHandler = itemRemovedHandler;

        // One timer for the lifetime of the handler, rescheduled with Change(). The previous
        // implementation disposed and replaced the field on every ItemAdded, from the library event
        // thread and without a lock, so concurrent events could leak a Timer or replace one whose
        // callback had already begun (Dispose does not stop a running callback) — producing two
        // concurrent flushes over the same pending set.
        _debounceTimer = new Timer(OnDebounceElapsed, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        _libraryManager.ItemAdded += OnItemAdded;
        _libraryManager.ItemRemoved += OnItemRemoved;
        _logger.LogInformation("LibrarySyncHandler started — watching for new items");
    }

    private void OnItemRemoved(object? sender, ItemChangeEventArgs e)
    {
        if (_disposed || e.Item == null)
        {
            return;
        }

        var item = e.Item;
        if (item.GetBaseItemKind() != BaseItemKind.Series && item.GetBaseItemKind() != BaseItemKind.Movie)
        {
            return;
        }

        _logger.LogInformation("Item removed: {Name} ({Id}) — checking cleanup policy", item.Name, item.Id);
        _itemRemovedHandler.HandleRemoved(item.Id, item.ContainingFolderPath);
    }

    private void OnItemAdded(object? sender, ItemChangeEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        if (Plugin.Instance?.Configuration?.AutoSyncOnItemAdded == false)
        {
            return;
        }

        var item = e.Item;
        if (item == null)
        {
            return;
        }

        var kind = item.GetBaseItemKind();
        if (kind != BaseItemKind.Series && kind != BaseItemKind.Movie)
        {
            return;
        }

        // Gate on library eligibility (LibraryPattern) so non-anime libraries are never queued
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        if (!LibrarySelection.IsItemEligible(item, _libraryManager, config))
        {
            return;
        }

        _logger.LogInformation("New {Type} detected: {Name} ({Id}) — queuing for theme sync", kind, item.Name, item.Id);

        bool runNow;
        lock (_batchLock)
        {
            if (_pendingItems.Count >= MaxPendingItems)
            {
                _logger.LogWarning("Pending theme-sync queue is full ({Max} items); flushing now", MaxPendingItems);
                runNow = true;
            }
            else
            {
                _pendingItems.Add(item.Id);
                if (_firstQueuedUtc == DateTime.MinValue)
                {
                    _firstQueuedUtc = DateTime.UtcNow;
                }

                runNow = DateTime.UtcNow - _firstQueuedUtc >= MaxDeferral;
            }

            try
            {
                _debounceTimer.Change(runNow ? TimeSpan.Zero : DebounceWindow, Timeout.InfiniteTimeSpan);
            }
            catch (ObjectDisposedException)
            {
                // Shutting down.
            }
        }
    }

    /// <summary>
    /// Timer entry point. Kept synchronous so no exception can escape as an unhandled
    /// <c>async void</c> fault on a pool thread.
    /// </summary>
    private void OnDebounceElapsed(object? state)
    {
        if (_disposed)
        {
            return;
        }

        // Never let two flushes overlap.
        if (Interlocked.CompareExchange(ref _flushing, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(
            async () =>
            {
                try
                {
                    await FlushPendingItems(_shutdown.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogDebug("Pending theme sync cancelled during shutdown");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled error while flushing pending theme syncs");
                }
                finally
                {
                    Volatile.Write(ref _flushing, 0);
                }
            },
            CancellationToken.None);
    }

    private async Task FlushPendingItems(CancellationToken cancellationToken)
    {
        Guid[] batch;
        lock (_batchLock)
        {
            batch = _pendingItems.ToArray();
            _pendingItems.Clear();
            _firstQueuedUtc = DateTime.MinValue;
        }

        if (batch.Length == 0)
        {
            return;
        }

        _logger.LogInformation("Processing {Count} newly added items for theme sync", batch.Length);

        foreach (var itemId in batch)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var item = _libraryManager.GetItemById(itemId);
                if (item == null)
                {
                    _logger.LogWarning("Item {Id} no longer exists, skipping", itemId);
                    continue;
                }

                await _runGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    // Pass the shutdown token so a server stop actually stops the work; this used
                    // to be CancellationToken.None, which kept downloads running through shutdown.
                    await _runner.SyncItemAsync(item, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    _runGate.Release();
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sync themes for new item {Id}", itemId);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _libraryManager.ItemAdded -= OnItemAdded;
        _libraryManager.ItemRemoved -= OnItemRemoved;

        try
        {
            _shutdown.Cancel();
        }
        catch (Exception)
        {
            // Nothing useful to do while tearing down.
        }

        _debounceTimer.Dispose();
        _shutdown.Dispose();

        // _runGate is intentionally not disposed: an in-flight flush would then throw
        // ObjectDisposedException from its finally-block Release(). It holds no unmanaged
        // resources here, so leaving it to the GC is safe.
        _logger.LogInformation("LibrarySyncHandler disposed");
    }
}
