namespace Jellyfin.Plugin.KometaThemes.Models;

/// <summary>
/// Result of a single theme file download.
/// </summary>
public enum DownloadOutcome
{
    /// <summary>
    /// The file already existed on disk and was left untouched.
    /// </summary>
    Skipped = 0,

    /// <summary>
    /// The file was downloaded and written successfully.
    /// </summary>
    Downloaded = 1,

    /// <summary>
    /// The download or the transcode failed. Any partial output has been removed.
    /// </summary>
    Failed = 2
}
