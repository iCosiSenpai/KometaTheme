using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.KometaThemes.Api;
using Jellyfin.Plugin.KometaThemes.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

#pragma warning disable SA1611, SA1615, CS1591

namespace Jellyfin.Plugin.KometaThemes;

/// <summary>
/// Manages a global "Anime Themes" playlist aggregating all downloaded theme songs.
/// Creates an M3U playlist file in the Jellyfin data directory.
/// </summary>
public class PlaylistManager
{
    private readonly ILibraryManager _libraryManager;
    private readonly DownloadTracker _downloadTracker;
    private readonly IApplicationPaths _appPaths;
    private readonly ILogger<PlaylistManager> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaylistManager"/> class.
    /// </summary>
    public PlaylistManager(
        ILibraryManager libraryManager,
        DownloadTracker downloadTracker,
        IApplicationPaths appPaths,
        ILogger<PlaylistManager> logger)
    {
        _libraryManager = libraryManager;
        _downloadTracker = downloadTracker;
        _appPaths = appPaths;
        _logger = logger;
    }

    /// <summary>
    /// Refreshes the global playlist with all downloaded themes.
    /// Scans library root paths for theme files tracked by DownloadTracker.
    /// </summary>
    public async Task RefreshPlaylistAsync(string playlistName, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Refreshing global playlist: {Name}", playlistName);

            var allFiles = new List<string>();

            var configuration = Plugin.Instance?.Configuration ?? new PluginConfiguration();
            var isIncluded = Configuration.LibraryPatternMatcher.Create(configuration.LibraryPattern);

            var libraries = _libraryManager.GetVirtualFolders()
                .Where(lib => isIncluded(lib.Name))
                .ToList();

            foreach (var library in libraries)
            {
                if (!string.IsNullOrWhiteSpace(library.Locations?.FirstOrDefault()))
                {
                    var rootPath = library.Locations[0];
                    var themeFiles = await _downloadTracker.GetAllThemeFilesAsync(rootPath).ConfigureAwait(false);
                    allFiles.AddRange(themeFiles);
                    _logger.LogDebug("Found {Count} theme files in library {Name}", themeFiles.Count, library.Name);
                }
            }

            _logger.LogInformation("Playlist refresh complete: {Count} total theme files found", allFiles.Count);

            var playlistPath = Path.Combine(
                _appPaths.DataPath,
                "playlists",
                $"{SanitizeFileName(playlistName)}.m3u");

            if (allFiles.Count > 0)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(playlistPath)!);

                // Temp file plus rename, so a reader never sees a half-written playlist.
                var tempPath = playlistPath + ".tmp";
                await File.WriteAllLinesAsync(tempPath, allFiles, cancellationToken).ConfigureAwait(false);
                File.Move(tempPath, playlistPath, overwrite: true);
                _logger.LogInformation("Playlist saved to: {Path}", playlistPath);
            }
            else if (File.Exists(playlistPath))
            {
                // No themes left anywhere. Leaving the previous file in place used to present a
                // playlist of paths that no longer exist.
                File.Delete(playlistPath);
                _logger.LogInformation("No theme files found; removed stale playlist {Path}", playlistPath);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh playlist");
        }
    }

    /// <summary>
    /// Collects all theme files across all libraries without writing a playlist file.
    /// </summary>
    /// <param name="libraryNamePattern">Optional library name pattern filter (case-insensitive contains).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of absolute file paths to downloaded theme files.</returns>
    public async Task<List<string>> CollectAllThemeFilesAsync(string? libraryNamePattern = null, CancellationToken cancellationToken = default)
    {
        var allFiles = new List<string>();
        try
        {
            var libraries = _libraryManager.GetVirtualFolders();
            foreach (var library in libraries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!string.IsNullOrWhiteSpace(libraryNamePattern) &&
                    library.Name.IndexOf(libraryNamePattern, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var location = library.Locations?.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(location))
                {
                    continue;
                }

                var themeFiles = await _downloadTracker.GetAllThemeFilesAsync(location).ConfigureAwait(false);
                allFiles.AddRange(themeFiles);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to collect theme files");
        }

        return allFiles;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries)).Trim('_');
    }
}
