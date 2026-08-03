using System.Text.Json.Serialization;
using Jellyfin.Plugin.KometaThemes.Models;

namespace Jellyfin.Plugin.KometaThemes.Api;

/// <summary>
/// Request body for importing a theme from a YouTube link.
/// </summary>
public sealed class YouTubeImportRequest
{
    /// <summary>
    /// Gets or sets the YouTube link (or bare video ID). Reduced to a canonical video ID
    /// server-side before it is used.
    /// </summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether this is an opening or an ending.
    /// </summary>
    [JsonPropertyName("themeType")]
    public ThemeType ThemeType { get; set; } = ThemeType.OP;

    /// <summary>
    /// Gets or sets which files to produce: audio, video, or both.
    /// </summary>
    [JsonPropertyName("format")]
    public ThemeImportFormat Format { get; set; } = ThemeImportFormat.Audio;

    /// <summary>
    /// Gets or sets the OP/ED sequence number, e.g. 2 for "OP2". Defaults to 1.
    /// </summary>
    [JsonPropertyName("sequence")]
    public int Sequence { get; set; } = 1;

    /// <summary>
    /// Gets or sets an optional display title. When empty, the title reported by the
    /// extractor is used.
    /// </summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the season number this theme belongs to, for bookkeeping. 0 means unset.
    /// </summary>
    [JsonPropertyName("seasonNumber")]
    public int SeasonNumber { get; set; }
}
