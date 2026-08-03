namespace Jellyfin.Plugin.KometaThemes.Models;

/// <summary>
/// Which media files to produce when importing a theme from an external link.
/// </summary>
public enum ThemeImportFormat
{
    /// <summary>
    /// Audio only, written to <c>theme-music/</c> as mp3.
    /// </summary>
    Audio = 0,

    /// <summary>
    /// Video only, written to <c>backdrops/</c> as webm.
    /// </summary>
    Video = 1,

    /// <summary>
    /// Both an audio and a video theme file from the same source.
    /// </summary>
    Both = 2
}
