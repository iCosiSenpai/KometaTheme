using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.KometaThemes.YouTube;

/// <summary>
/// Whether the external YouTube extractor is installed and usable.
/// </summary>
/// <param name="Available">Whether an executable was found.</param>
/// <param name="ExecutablePath">Where it was found, for display in the settings UI.</param>
/// <param name="Error">Why it is unusable, when it is not available.</param>
public sealed record YouTubeAvailability(
    [property: JsonPropertyName("available")] bool Available,
    [property: JsonPropertyName("executablePath")] string ExecutablePath,
    [property: JsonPropertyName("error")] string Error);
