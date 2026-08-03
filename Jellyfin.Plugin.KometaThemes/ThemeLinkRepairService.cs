using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

#pragma warning disable CA3003 // paths come from the library item's ContainingFolderPath, not user input

namespace Jellyfin.Plugin.KometaThemes;

/// <summary>
/// Repairs the linking between an item and its theme media files.
/// On Jellyfin 10.11.x the ThemeMediaResolver may assign theme items to the
/// CollectionFolder instead of the individual Series/Movie, leaving the owner's
/// <see cref="BaseItem.ExtraIds"/> empty so themes never play. This service links
/// the scanned Audio/Video items under theme-music/ and backdrops/ directly to the
/// owner, independent of the broken resolver.
/// </summary>
public class ThemeLinkRepairService
{
    private const string ThemeMusicDirectory = "theme-music";
    private const string ThemeVideoDirectory = "backdrops";
    private const string RootThemeFileName = "theme.mp3";

    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<ThemeLinkRepairService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThemeLinkRepairService"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="logger">The logger.</param>
    public ThemeLinkRepairService(ILibraryManager libraryManager, ILogger<ThemeLinkRepairService> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <summary>
    /// Repairs theme links for the given item.
    /// </summary>
    /// <param name="owner">The series/movie that owns the theme files.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A summary of the repair operation.</returns>
    public async Task<ThemeLinkRepairResult> RepairAsync(BaseItem owner, CancellationToken cancellationToken)
    {
        var result = new ThemeLinkRepairResult();
        if (string.IsNullOrWhiteSpace(owner.ContainingFolderPath))
        {
            return result;
        }

        var songFiles = EnumerateThemeFiles(owner.ContainingFolderPath, audio: true);
        var videoFiles = EnumerateThemeFiles(owner.ContainingFolderPath, audio: false);
        result.SongsOnDisk = songFiles.Count;
        result.VideosOnDisk = videoFiles.Count;

        var linkedIds = new List<Guid>();
        foreach (var (path, extraType) in songFiles.Select(p => (p, ExtraType.ThemeSong))
                     .Concat(videoFiles.Select(p => (p, ExtraType.ThemeVideo))))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var extra = _libraryManager.FindByPath(path, false);
            if (extra is null)
            {
                result.NotScanned++;
                continue;
            }

            var dirty = false;
            if (extra.ExtraType != extraType)
            {
                extra.ExtraType = extraType;
                dirty = true;
            }

            if (!extra.OwnerId.Equals(owner.Id))
            {
                extra.OwnerId = owner.Id;
                dirty = true;
            }

            if (!extra.ParentId.Equals(Guid.Empty))
            {
                extra.ParentId = Guid.Empty;
                dirty = true;
            }

            if (dirty)
            {
                await extra.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
                result.Repaired++;
            }

            linkedIds.Add(extra.Id);
        }

        // Keep unrelated extras (trailers and so on) and this run's theme files, but drop stale
        // theme extras whose file no longer exists. The previous union-only approach meant every
        // volume change or force sync — which renames theme files — left the old GUIDs behind
        // forever, so ExtraIds only ever grew.
        var currentThemeIds = linkedIds.ToHashSet();
        var existingIds = owner.ExtraIds ?? [];
        var keptIds = new List<Guid>();
        var droppedIds = 0;

        foreach (var existingId in existingIds)
        {
            if (currentThemeIds.Contains(existingId))
            {
                continue;
            }

            var existingExtra = _libraryManager.GetItemById(existingId);

            // Unknown to the library, or a theme extra whose file is gone: stop referencing it.
            var isStaleTheme = existingExtra == null
                || existingExtra.ExtraType == ExtraType.ThemeSong
                || existingExtra.ExtraType == ExtraType.ThemeVideo;

            if (isStaleTheme)
            {
                droppedIds++;
                continue;
            }

            keptIds.Add(existingId);
        }

        var extraIds = keptIds.Concat(linkedIds).Distinct().ToArray();
        if (!extraIds.SequenceEqual(existingIds))
        {
            owner.ExtraIds = extraIds;
            await owner.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
            result.OwnerUpdated = true;
            result.StaleLinksRemoved = droppedIds;
        }

        result.RegisteredSongs = owner.GetThemeSongs().Count;
        result.RegisteredVideos = owner.GetThemeVideos().Count;

        if (result.Repaired > 0 || result.OwnerUpdated)
        {
            _logger.LogInformation(
                "[{Id}] Theme link repair: {Repaired} extras fixed, {Songs} songs / {Videos} videos registered ({NotScanned} files not in library yet)",
                owner.Id,
                result.Repaired,
                result.RegisteredSongs,
                result.RegisteredVideos,
                result.NotScanned);
        }

        return result;
    }

    private static List<string> EnumerateThemeFiles(string containingFolderPath, bool audio)
    {
        var files = new List<string>();
        var directory = Path.Combine(containingFolderPath, audio ? ThemeMusicDirectory : ThemeVideoDirectory);
        if (Directory.Exists(directory))
        {
            // Every recognised container, not just webm: an imported mp4 theme would otherwise
            // never be linked to its owner and would silently never play.
            files.AddRange(Models.ThemeFileKinds.EnumerateFiles(directory, audio));
        }

        if (audio)
        {
            var rootTheme = Path.Combine(containingFolderPath, RootThemeFileName);
            if (File.Exists(rootTheme))
            {
                files.Add(rootTheme);
            }
        }

        return files;
    }
}
