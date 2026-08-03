namespace Jellyfin.Plugin.KometaThemes.YouTube;

/// <summary>
/// Result of fetching a YouTube video to a local temporary file.
/// </summary>
/// <param name="FilePath">Absolute path to the downloaded media file, or empty on failure.</param>
/// <param name="Title">Video title reported by the extractor, when available.</param>
/// <param name="DurationSeconds">Video duration in seconds, when reported.</param>
/// <param name="Error">Human-readable failure reason, or empty on success.</param>
public sealed record YouTubeDownloadResult(
    string FilePath,
    string Title,
    int DurationSeconds,
    string Error)
{
    /// <summary>
    /// Gets a value indicating whether the download succeeded.
    /// </summary>
    public bool Success => Error.Length == 0 && FilePath.Length > 0;

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    /// <param name="error">Failure reason.</param>
    /// <returns>A failed result.</returns>
    public static YouTubeDownloadResult Failure(string error)
        => new(string.Empty, string.Empty, 0, error);
}
