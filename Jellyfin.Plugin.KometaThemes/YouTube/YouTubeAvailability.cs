using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.KometaThemes.YouTube;

/// <summary>
/// Which extractor backend YouTube import will use, and whether it is usable.
/// </summary>
/// <remarks>
/// The managed extractor ships with the plugin, so import is available on a stock install and
/// <see cref="Available"/> is false only when something is actively misconfigured — a yt-dlp path
/// that was set by hand and does not exist.
/// </remarks>
/// <param name="Available">Whether an import can be attempted.</param>
/// <param name="Backend">Either <c>bundled</c> or <c>yt-dlp</c>.</param>
/// <param name="ExecutablePath">Where yt-dlp was found, when that is the backend in use.</param>
/// <param name="Error">Why import is unusable, when it is not available.</param>
public sealed record YouTubeAvailability(
    [property: JsonPropertyName("available")] bool Available,
    [property: JsonPropertyName("backend")] string Backend,
    [property: JsonPropertyName("executablePath")] string ExecutablePath,
    [property: JsonPropertyName("error")] string Error)
{
    /// <summary>
    /// Backend name for the extractor bundled with the plugin.
    /// </summary>
    public const string BundledBackend = "bundled";

    /// <summary>
    /// Backend name for an external yt-dlp binary.
    /// </summary>
    public const string YtDlpBackend = "yt-dlp";

    /// <summary>
    /// Gets a value indicating whether the external binary will be used.
    /// </summary>
    public bool UsesYtDlp => Backend == YtDlpBackend;
}
