using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.KometaThemes.Models;
using Microsoft.Extensions.Logging;
#pragma warning disable SA1611, SA1615, CS1591

namespace Jellyfin.Plugin.KometaThemes;

/// <summary>
/// Reacts to library item removals and optionally cleans up orphaned theme files.
/// Controlled by <see cref="Configuration.PluginConfiguration.CleanupThemesOnItemRemoved"/>.
/// </summary>
public sealed class ItemRemovedHandler : IDisposable
{
    private const string ThemeMusicDirectory = "theme-music";
    private const string ThemeVideoDirectory = "backdrops";
    private const string ThemeMusicFileName = "theme.mp3";

    private readonly ILogger<ItemRemovedHandler> _logger;
    private readonly DownloadTracker _downloadTracker;
    private readonly CancellationTokenSource _shutdown = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ItemRemovedHandler"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="downloadTracker">Tracker used to establish which files this plugin created.</param>
    public ItemRemovedHandler(ILogger<ItemRemovedHandler> logger, DownloadTracker downloadTracker)
    {
        _logger = logger;
        _downloadTracker = downloadTracker;
    }

    /// <summary>
    /// Handles a removed library item by deleting the theme files this plugin recorded for it.
    /// </summary>
    /// <param name="itemId">The removed item ID.</param>
    /// <param name="containingFolderPath">The folder the item lived in (may be null/empty).</param>
    public void HandleRemoved(Guid itemId, string? containingFolderPath)
    {
        if (_disposed)
        {
            return;
        }

        var config = Plugin.Instance?.Configuration;
        if (config?.CleanupThemesOnItemRemoved != true)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(containingFolderPath))
        {
            return;
        }

        _ = Task.Run(
            () => CleanupAsync(itemId, containingFolderPath, _shutdown.Token),
            CancellationToken.None);
    }

    /// <summary>
    /// Deletes only the files the tracker attributes to this item.
    /// </summary>
    /// <remarks>
    /// The previous implementation called <c>Directory.Delete(recursive: true)</c> on
    /// <c>theme-music/</c> and <c>backdrops/</c> under the removed item's containing folder. That is
    /// only correct when every item owns its own folder. In a flat library
    /// (<c>/movies/Film.mkv</c> → containing folder <c>/movies</c>) removing a single movie wiped the
    /// theme files of every movie in that library, and because <c>backdrops/</c> is Jellyfin's
    /// artwork folder it also destroyed user-supplied fanart that this plugin never created. Both
    /// losses are unrecoverable. Cleanup is now driven by the tracker: delete the recorded files for
    /// this item, drop their records, and only remove a directory once it is genuinely empty.
    /// </remarks>
    private async Task CleanupAsync(Guid itemId, string containingFolderPath, CancellationToken cancellationToken)
    {
        try
        {
            var records = await _downloadTracker.LoadAsync(containingFolderPath).ConfigureAwait(false);
            if (records.Count == 0)
            {
                _logger.LogDebug("[{Id}] No tracked theme files to clean up in {Path}", itemId, containingFolderPath);
                return;
            }

            var owned = records.Where(r => r.ItemId.Equals(itemId)).ToList();
            if (owned.Count == 0)
            {
                // The folder is shared with other items whose themes are still tracked; deleting
                // anything here would take their files with it.
                _logger.LogInformation(
                    "[{Id}] Folder {Path} holds {Count} theme records owned by other items — leaving it untouched",
                    itemId,
                    containingFolderPath,
                    records.Count);
                return;
            }

            var removedNames = new List<string>();
            foreach (var record in owned)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(record.FileName))
                {
                    continue;
                }

                // Guard against a tampered or corrupt tracker file steering the delete elsewhere.
                var fileName = Path.GetFileName(record.FileName);
                if (!string.Equals(fileName, record.FileName, StringComparison.Ordinal))
                {
                    _logger.LogWarning("[{Id}] Ignoring tracker record with a path-like file name: {Name}", itemId, record.FileName);
                    continue;
                }

                var directory = string.IsNullOrWhiteSpace(record.Directory)
                    ? (fileName.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) ? ThemeVideoDirectory : ThemeMusicDirectory)
                    : record.Directory;

                if (!string.Equals(directory, ThemeMusicDirectory, StringComparison.Ordinal) &&
                    !string.Equals(directory, ThemeVideoDirectory, StringComparison.Ordinal))
                {
                    continue;
                }

                if (TryDelete(Path.Combine(containingFolderPath, directory, fileName), itemId))
                {
                    removedNames.Add(fileName);
                }
            }

            // The root theme.mp3 is a copy of one of the tracked audio files, so it belongs to
            // this item exactly when the tracked audio files did.
            if (owned.Any(r => r.FileName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)))
            {
                TryDelete(Path.Combine(containingFolderPath, ThemeMusicFileName), itemId);
            }

            if (removedNames.Count > 0)
            {
                await _downloadTracker.RemoveRecordsAsync(containingFolderPath, removedNames).ConfigureAwait(false);
                _logger.LogInformation("[{Id}] Removed {Count} orphan theme files from {Path}", itemId, removedNames.Count, containingFolderPath);
            }

            TryRemoveIfEmpty(Path.Combine(containingFolderPath, ThemeMusicDirectory), itemId);
            TryRemoveIfEmpty(Path.Combine(containingFolderPath, ThemeVideoDirectory), itemId);

            // Only drop the tracker file once nothing is left in it.
            var remaining = await _downloadTracker.LoadAsync(containingFolderPath).ConfigureAwait(false);
            if (remaining.Count == 0)
            {
                TryDelete(DownloadTracker.GetTrackerPath(containingFolderPath), itemId);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("[{Id}] Theme cleanup cancelled during shutdown", itemId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[{Id}] Failed to cleanup orphan theme files in {Path}", itemId, containingFolderPath);
        }
    }

    private bool TryDelete(string path, Guid itemId)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            File.Delete(path);
            _logger.LogDebug("[{Id}] Deleted orphan theme file {Path}", itemId, path);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[{Id}] Failed to delete {Path}", itemId, path);
            return false;
        }
    }

    private void TryRemoveIfEmpty(string directory, Guid itemId)
    {
        try
        {
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
                _logger.LogDebug(
                    "[{Id}] Removed now-empty directory {Path}",
                    itemId.ToString("D", CultureInfo.InvariantCulture),
                    directory);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[{Id}] Failed to remove empty directory {Path}", itemId, directory);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _shutdown.Cancel();
        }
        catch (Exception)
        {
            // Nothing useful to do while tearing down.
        }

        _shutdown.Dispose();
    }
}
