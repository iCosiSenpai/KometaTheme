using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.KometaThemes.Models;
using Microsoft.Extensions.Logging;

#pragma warning disable CA1001, CA1002, CA3003, CA1869, SA1611, SA1615, CS1591

namespace Jellyfin.Plugin.KometaThemes;

/// <summary>
/// Tracks downloaded themes per item via JSON files stored alongside the media.
/// </summary>
public class DownloadTracker
{
    private const string TrackerFileName = "_kometa_themes.json";
    private const string ThemeMusicDirectory = "theme-music";
    private const string ThemeVideoDirectory = "backdrops";

    /// <summary>
    /// Depth limit for the playlist walk. Enough for <c>library/Genre/Show/Season 01</c> without
    /// turning a large library into an unbounded recursive scan.
    /// </summary>
    private const int MaxPlaylistScanDepth = 4;

    private readonly ILogger<DownloadTracker> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public DownloadTracker(ILogger<DownloadTracker> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets the tracker file path for an item.
    /// </summary>
    public static string GetTrackerPath(string containingFolderPath)
        => Path.Combine(containingFolderPath, TrackerFileName);

    /// <summary>
    /// Loads download records for an item (thread-safe).
    /// </summary>
    public async Task<List<DownloadRecord>> LoadAsync(string containingFolderPath)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            return await LoadUnlockedAsync(containingFolderPath).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Loads download records for an item (caller must hold lock).
    /// </summary>
    private async Task<List<DownloadRecord>> LoadUnlockedAsync(string containingFolderPath)
    {
        var path = GetTrackerPath(containingFolderPath);
        try
        {
            if (!File.Exists(path))
            {
                return new List<DownloadRecord>();
            }

            var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
            return JsonSerializer.Deserialize<List<DownloadRecord>>(json) ?? new List<DownloadRecord>();
        }
        catch (JsonException ex)
        {
            // A corrupt tracker used to be silently treated as "no records", which then let the
            // next write replace it with an empty list — turning a recoverable read error into
            // permanent loss of the provenance data that pruning depends on. Preserve the bad file
            // so the original can still be inspected or salvaged.
            _logger.LogError(ex, "Download tracker at {Path} is corrupt; quarantining it", path);
            QuarantineCorruptFile(path);
            return new List<DownloadRecord>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load download tracker for {Path}", containingFolderPath);
            return new List<DownloadRecord>();
        }
    }

    private void QuarantineCorruptFile(string path)
    {
        try
        {
            var backup = path + ".corrupt";
            File.Move(path, backup, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to quarantine corrupt tracker {Path}", path);
        }
    }

    /// <summary>
    /// Saves download records for an item (caller must hold lock).
    /// </summary>
    /// <remarks>
    /// Writes to a sibling temp file and renames it over the target. <c>File.WriteAllTextAsync</c>
    /// truncates the real file first and then streams into it, so a crash, a container restart or a
    /// full disk mid-write left a prefix of valid JSON on disk — permanently unparseable, and taking
    /// the provenance data that prune and playlist generation rely on with it.
    /// </remarks>
    private async Task SaveUnlockedAsync(string containingFolderPath, List<DownloadRecord> records)
    {
        var path = GetTrackerPath(containingFolderPath);
        var tempPath = path + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(tempPath, json).ConfigureAwait(false);
            File.Move(tempPath, path, overwrite: true);
            _logger.LogDebug("Saved {Count} download records for {Path}", records.Count, containingFolderPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save download tracker for {Path}", containingFolderPath);
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (Exception cleanupEx)
            {
                _logger.LogDebug(cleanupEx, "Failed to remove temp tracker {Path}", tempPath);
            }
        }
    }

    /// <summary>
    /// Saves download records for an item (thread-safe).
    /// </summary>
    public async Task SaveAsync(string containingFolderPath, List<DownloadRecord> records)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            await SaveUnlockedAsync(containingFolderPath, records).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Adds a download record and saves (atomic read-modify-write).
    /// </summary>
    public async Task AddRecordAsync(string containingFolderPath, DownloadRecord record)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var records = await LoadUnlockedAsync(containingFolderPath).ConfigureAwait(false);

            // Dedupe on the file name as well as the theme id: imported themes share a synthetic
            // theme id space, so the id alone is not enough to identify the same target file.
            records.RemoveAll(r =>
                (r.Source == record.Source && r.ThemeId == record.ThemeId && r.Source == ThemeSource.AnimeThemes) ||
                string.Equals(r.FileName, record.FileName, StringComparison.OrdinalIgnoreCase));
            records.Add(record);
            await SaveUnlockedAsync(containingFolderPath, records).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Removes download records for specific files and saves (atomic read-modify-write).
    /// </summary>
    public async Task RemoveRecordsAsync(string containingFolderPath, IEnumerable<string> fileNames)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var records = await LoadUnlockedAsync(containingFolderPath).ConfigureAwait(false);
            var nameSet = fileNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
            records.RemoveAll(r => nameSet.Contains(r.FileName));
            await SaveUnlockedAsync(containingFolderPath, records).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Gets all downloaded theme audio files across all tracked items under a root path,
    /// for playlist generation.
    /// </summary>
    /// <remarks>
    /// The previous version enumerated exactly one directory level below the root and rebuilt every
    /// path as <c>&lt;dir&gt;/theme-music/&lt;file&gt;</c>. Nested layouts
    /// (<c>/anime/Genre/Show/</c>), per-season folders and every video theme therefore never made it
    /// into the playlist — video records resolved to a nonexistent path under <c>theme-music</c> and
    /// were dropped by the existence check.
    /// </remarks>
    public async Task<List<string>> GetAllThemeFilesAsync(string rootPath)
    {
        var allFiles = new List<string>();
        try
        {
            if (!Directory.Exists(rootPath))
            {
                return allFiles;
            }

            foreach (var dir in EnumerateTrackedDirectories(rootPath, MaxPlaylistScanDepth))
            {
                var records = await LoadAsync(dir).ConfigureAwait(false);
                foreach (var record in records)
                {
                    if (string.IsNullOrWhiteSpace(record.FileName))
                    {
                        continue;
                    }

                    // Playlists are audio-only; a webm theme video is not playable in an M3U
                    // of theme songs.
                    if (!record.FileName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var directory = string.IsNullOrWhiteSpace(record.Directory) ? ThemeMusicDirectory : record.Directory;
                    var fullPath = Path.Combine(dir, directory, record.FileName);
                    if (File.Exists(fullPath))
                    {
                        allFiles.Add(fullPath);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to collect theme files from {Path}", rootPath);
        }

        return allFiles;
    }

    /// <summary>
    /// Walks up to <paramref name="maxDepth"/> levels below <paramref name="rootPath"/> and yields
    /// every directory that holds a tracker file.
    /// </summary>
    private IEnumerable<string> EnumerateTrackedDirectories(string rootPath, int maxDepth)
    {
        var pending = new Queue<(string Path, int Depth)>();
        pending.Enqueue((rootPath, 0));

        while (pending.Count > 0)
        {
            var (current, depth) = pending.Dequeue();

            if (File.Exists(GetTrackerPath(current)))
            {
                yield return current;
            }

            if (depth >= maxDepth)
            {
                continue;
            }

            string[] children;
            try
            {
                children = Directory.GetDirectories(current);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Skipping unreadable directory {Path}", current);
                continue;
            }

            foreach (var child in children)
            {
                var name = Path.GetFileName(child);

                // Never descend into the theme folders themselves.
                if (string.Equals(name, ThemeMusicDirectory, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, ThemeVideoDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                pending.Enqueue((child, depth + 1));
            }
        }
    }
}
