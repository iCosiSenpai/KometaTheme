using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.KometaThemes;
using Jellyfin.Plugin.KometaThemes.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.KometaThemes.Tests;

/// <summary>
/// The tracker file is what tells the plugin which files on disk are its own. Pruning, force-sync
/// cleanup, item-removal cleanup and playlist generation all read it, so its durability and its
/// provenance field are load-bearing.
/// </summary>
public sealed class DownloadTrackerTests : IDisposable
{
    private readonly string _root;
    private readonly DownloadTracker _tracker;

    public DownloadTrackerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "kt-tracker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _tracker = new DownloadTracker(NullLogger<DownloadTracker>.Instance);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Temp cleanup is best-effort.
        }
    }

    private static DownloadRecord Record(
        string fileName,
        ThemeSource source = ThemeSource.AnimeThemes,
        int themeId = 1,
        string directory = "theme-music")
        => new()
        {
            ThemeId = themeId,
            Type = ThemeType.OP,
            Sequence = 1,
            Slug = "slug",
            FileName = fileName,
            Source = source,
            Directory = directory,
            ItemId = Guid.NewGuid()
        };

    [Fact]
    public async Task LoadAsync_MissingFile_ReturnsEmpty()
    {
        var records = await _tracker.LoadAsync(_root);
        Assert.Empty(records);
    }

    [Fact]
    public async Task AddRecordAsync_RoundTrips()
    {
        await _tracker.AddRecordAsync(_root, Record("OP1 - Tank__50.mp3"));

        var records = await _tracker.LoadAsync(_root);
        var only = Assert.Single(records);
        Assert.Equal("OP1 - Tank__50.mp3", only.FileName);
        Assert.Equal(ThemeSource.AnimeThemes, only.Source);
        Assert.Equal("theme-music", only.Directory);
    }

    [Fact]
    public async Task AddRecordAsync_ReplacesAnEntryForTheSameFileName()
    {
        await _tracker.AddRecordAsync(_root, Record("OP1__50.mp3", themeId: 1));
        await _tracker.AddRecordAsync(_root, Record("OP1__50.mp3", themeId: 2));

        var records = await _tracker.LoadAsync(_root);
        var only = Assert.Single(records);
        Assert.Equal(2, only.ThemeId);
    }

    [Fact]
    public async Task AddRecordAsync_KeepsImportedAndSyncedEntriesSeparate()
    {
        // An imported theme carries a synthetic id and must not evict a real one, or vice versa.
        await _tracker.AddRecordAsync(_root, Record("OP1__50.mp3", ThemeSource.AnimeThemes, themeId: 42));
        await _tracker.AddRecordAsync(_root, Record("ED1__50.mp3", ThemeSource.YouTube, themeId: -42));

        var records = await _tracker.LoadAsync(_root);
        Assert.Equal(2, records.Count);
        Assert.Contains(records, r => r.Source == ThemeSource.AnimeThemes);
        Assert.Contains(records, r => r.Source == ThemeSource.YouTube);
    }

    [Fact]
    public async Task RemoveRecordsAsync_RemovesByFileNameCaseInsensitively()
    {
        await _tracker.AddRecordAsync(_root, Record("OP1__50.mp3", themeId: 1));
        await _tracker.AddRecordAsync(_root, Record("ED1__50.mp3", themeId: 2));

        await _tracker.RemoveRecordsAsync(_root, new[] { "op1__50.MP3" });

        var records = await _tracker.LoadAsync(_root);
        var only = Assert.Single(records);
        Assert.Equal("ED1__50.mp3", only.FileName);
    }

    [Fact]
    public async Task SaveAsync_LeavesNoTemporaryFileBehind()
    {
        // Writes go to a sibling temp file and are renamed into place, so a crash cannot leave a
        // truncated tracker. The temp file must not survive a successful write.
        await _tracker.SaveAsync(_root, new List<DownloadRecord> { Record("OP1__50.mp3") });

        Assert.True(File.Exists(DownloadTracker.GetTrackerPath(_root)));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    [Fact]
    public async Task LoadAsync_CorruptFile_IsQuarantinedRatherThanSilentlyOverwritten()
    {
        var path = DownloadTracker.GetTrackerPath(_root);
        await File.WriteAllTextAsync(path, "[{\"FileName\":\"truncated\"");

        var records = await _tracker.LoadAsync(_root);

        Assert.Empty(records);
        Assert.False(File.Exists(path));
        Assert.True(File.Exists(path + ".corrupt"));
    }

    [Fact]
    public async Task GetAllThemeFilesAsync_FindsAudioInNestedItemFolders()
    {
        // The previous collector only looked one directory below the library root, so nested
        // layouts and per-season folders never reached the playlist.
        var nested = Path.Combine(_root, "Genre", "Cowboy Bebop");
        var musicDirectory = Path.Combine(nested, "theme-music");
        Directory.CreateDirectory(musicDirectory);
        await File.WriteAllTextAsync(Path.Combine(musicDirectory, "OP1__50.mp3"), "audio");
        await _tracker.AddRecordAsync(nested, Record("OP1__50.mp3"));

        var files = await _tracker.GetAllThemeFilesAsync(_root);

        Assert.Single(files);
        Assert.EndsWith("OP1__50.mp3", files[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAllThemeFilesAsync_ExcludesVideoAndMissingFiles()
    {
        var musicDirectory = Path.Combine(_root, "theme-music");
        Directory.CreateDirectory(musicDirectory);
        await File.WriteAllTextAsync(Path.Combine(musicDirectory, "OP1__50.mp3"), "audio");

        await _tracker.AddRecordAsync(_root, Record("OP1__50.mp3", themeId: 1));
        // Recorded but never written to disk.
        await _tracker.AddRecordAsync(_root, Record("OP2__50.mp3", themeId: 2));
        // A theme video is not playable from an M3U of theme songs.
        await _tracker.AddRecordAsync(_root, Record("OP3__50.webm", themeId: 3, directory: "backdrops"));

        var files = await _tracker.GetAllThemeFilesAsync(_root);

        Assert.Single(files);
        Assert.EndsWith("OP1__50.mp3", files[0], StringComparison.Ordinal);
    }
}
