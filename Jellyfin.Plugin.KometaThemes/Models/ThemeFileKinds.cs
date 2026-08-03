using System;
using System.IO;
using System.Linq;

// CA3003 (file path injection) is suppressed here for the same reason the rest of the plugin
// suppresses it: every directory reaching these helpers is Jellyfin's own
// BaseItem.ContainingFolderPath joined with one of the two fixed theme-directory constants above.
// The taint analyser only sees that an item id came from a route and cannot follow that the id is
// resolved to a library item before its path is used. File *names* are separately validated by the
// callers, which reject anything Path.GetFileName would alter.
#pragma warning disable CA3003

namespace Jellyfin.Plugin.KometaThemes.Models;

/// <summary>
/// The file layout every part of the plugin agrees on: which extensions count as theme audio or
/// theme video, and which directory a given theme file belongs in.
/// </summary>
/// <remarks>
/// This used to be spelled out as a literal <c>"*.webm"</c> in eight different places, which was
/// correct only while animethemes.moe was the sole source. Imported themes keep the container the
/// extractor produced — an mp4 stream copy instead of a multi-minute VP9 re-encode — so every one of
/// those globs had to learn about a second extension, and the theme-link repair service in
/// particular would otherwise have silently failed to register mp4 themes with Jellyfin.
/// </remarks>
public static class ThemeFileKinds
{
    /// <summary>
    /// Folder holding theme songs, relative to the item folder. Jellyfin's own convention.
    /// </summary>
    public const string AudioDirectory = "theme-music";

    /// <summary>
    /// Folder holding theme videos, relative to the item folder. Jellyfin's own convention.
    /// </summary>
    public const string VideoDirectory = "backdrops";

    /// <summary>
    /// Root-level theme song, a copy of one of the tracked audio files.
    /// </summary>
    public const string RootAudioFileName = "theme.mp3";

    /// <summary>
    /// Extensions recognised as theme audio.
    /// </summary>
    public static readonly string[] AudioExtensions = [".mp3"];

    /// <summary>
    /// Extensions recognised as theme video. Both are containers Jellyfin resolves and that ffmpeg
    /// can mux into with a stream copy.
    /// </summary>
    public static readonly string[] VideoExtensions = [".webm", ".mp4"];

    /// <summary>
    /// Glob patterns for enumerating theme files of a given kind.
    /// </summary>
    /// <param name="audio">Whether to describe audio rather than video.</param>
    /// <returns>The search patterns to enumerate with.</returns>
    public static string[] SearchPatterns(bool audio)
        => (audio ? AudioExtensions : VideoExtensions).Select(extension => "*" + extension).ToArray();

    /// <summary>
    /// Enumerates theme files of a given kind in a directory, across every recognised extension.
    /// </summary>
    /// <param name="directory">Directory to enumerate.</param>
    /// <param name="audio">Whether to enumerate audio rather than video.</param>
    /// <returns>Matching file paths, or empty when the directory does not exist.</returns>
    public static string[] EnumerateFiles(string directory, bool audio)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return SearchPatterns(audio)
            .SelectMany(pattern => Directory.GetFiles(directory, pattern))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Returns true when the file name has a recognised theme video extension.
    /// </summary>
    /// <param name="fileName">File name to test.</param>
    /// <returns>Whether this is a theme video.</returns>
    public static bool IsVideoFile(string? fileName)
        => fileName != null && VideoExtensions.Any(extension => fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Returns true when the file name has a recognised theme audio extension.
    /// </summary>
    /// <param name="fileName">File name to test.</param>
    /// <returns>Whether this is a theme song.</returns>
    public static bool IsAudioFile(string? fileName)
        => fileName != null && AudioExtensions.Any(extension => fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Infers the theme directory for a file name, for tracker records written before the directory
    /// was recorded explicitly.
    /// </summary>
    /// <param name="fileName">File name to classify.</param>
    /// <returns>The directory the file belongs in.</returns>
    public static string DirectoryForFileName(string? fileName)
        => IsVideoFile(fileName) ? VideoDirectory : AudioDirectory;

    /// <summary>
    /// Checks that a directory name is one of the two theme directories, so a tampered or corrupt
    /// tracker record cannot redirect a delete elsewhere.
    /// </summary>
    /// <param name="directory">Directory name to validate.</param>
    /// <returns>Whether the name is a theme directory.</returns>
    public static bool IsThemeDirectory(string? directory)
        => string.Equals(directory, AudioDirectory, StringComparison.Ordinal)
        || string.Equals(directory, VideoDirectory, StringComparison.Ordinal);
}
