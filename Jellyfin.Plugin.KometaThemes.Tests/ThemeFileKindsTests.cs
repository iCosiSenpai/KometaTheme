using System;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.KometaThemes.Models;
using Xunit;

namespace Jellyfin.Plugin.KometaThemes.Tests;

/// <summary>
/// Theme video files are no longer always webm: an import keeps the container the extractor produced
/// so the file can be written with a stream copy instead of a multi-minute re-encode. Every place
/// that enumerates or classifies theme files has to agree on that set, and the theme-link repair
/// service in particular would silently never register an mp4 theme with Jellyfin if it did not.
/// </summary>
public sealed class ThemeFileKindsTests : IDisposable
{
    private readonly string _root;

    public ThemeFileKindsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "kt-kinds-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
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

    [Theory]
    [InlineData("OP1 - Tank__50.webm", true)]
    [InlineData("OP1 - Tank__50.mp4", true)]
    [InlineData("OP1 - Tank__50.WEBM", true)]
    [InlineData("OP1 - Tank__50.MP4", true)]
    [InlineData("OP1 - Tank__50.mp3", false)]
    [InlineData("poster.jpg", false)]
    [InlineData(null, false)]
    public void IsVideoFile_RecognisesBothContainers(string? fileName, bool expected)
        => Assert.Equal(expected, ThemeFileKinds.IsVideoFile(fileName));

    [Theory]
    [InlineData("OP1__50.mp3", "theme-music")]
    [InlineData("OP1__50.webm", "backdrops")]
    [InlineData("OP1__50.mp4", "backdrops")]
    public void DirectoryForFileName_InfersFromExtension(string fileName, string expected)
        => Assert.Equal(expected, ThemeFileKinds.DirectoryForFileName(fileName));

    [Theory]
    [InlineData("theme-music", true)]
    [InlineData("backdrops", true)]
    [InlineData("..", false)]
    [InlineData("metadata", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsThemeDirectory_RejectsAnythingElse(string? directory, bool expected)
        => Assert.Equal(expected, ThemeFileKinds.IsThemeDirectory(directory));

    [Fact]
    public void EnumerateFiles_FindsWebmAndMp4ButNotAudio()
    {
        File.WriteAllText(Path.Combine(_root, "OP1__50.webm"), "v");
        File.WriteAllText(Path.Combine(_root, "OP2__50.mp4"), "v");
        File.WriteAllText(Path.Combine(_root, "OP3__50.mp3"), "a");
        File.WriteAllText(Path.Combine(_root, "fanart.jpg"), "i");

        var videos = ThemeFileKinds.EnumerateFiles(_root, audio: false)
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "OP1__50.webm", "OP2__50.mp4" }, videos);
    }

    [Fact]
    public void EnumerateFiles_AudioIgnoresVideoContainers()
    {
        File.WriteAllText(Path.Combine(_root, "OP1__50.webm"), "v");
        File.WriteAllText(Path.Combine(_root, "OP1__50.mp3"), "a");

        var audio = ThemeFileKinds.EnumerateFiles(_root, audio: true)
            .Select(Path.GetFileName)
            .ToArray();

        Assert.Equal(new[] { "OP1__50.mp3" }, audio);
    }

    [Fact]
    public void EnumerateFiles_MissingDirectory_ReturnsEmpty()
        => Assert.Empty(ThemeFileKinds.EnumerateFiles(Path.Combine(_root, "nope"), audio: false));

    [Fact]
    public void SearchPatterns_CoverEveryDeclaredExtension()
    {
        Assert.Equal(ThemeFileKinds.VideoExtensions.Length, ThemeFileKinds.SearchPatterns(audio: false).Length);
        Assert.Equal(ThemeFileKinds.AudioExtensions.Length, ThemeFileKinds.SearchPatterns(audio: true).Length);
        Assert.All(ThemeFileKinds.SearchPatterns(audio: false), pattern => Assert.StartsWith("*.", pattern, StringComparison.Ordinal));
    }
}
