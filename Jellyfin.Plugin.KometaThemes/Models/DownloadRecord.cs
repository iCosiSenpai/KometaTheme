using System;

#pragma warning disable SA1516, CS1591

namespace Jellyfin.Plugin.KometaThemes.Models;

public sealed class DownloadRecord
{
    public int ThemeId { get; set; }
    public ThemeType Type { get; set; }
    public int Sequence { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public int SeasonNumber { get; set; }
    public DateTime DownloadedAt { get; set; } = DateTime.UtcNow;
    public Guid ItemId { get; set; }

    /// <summary>
    /// Gets or sets where this file came from. Defaults to <see cref="ThemeSource.AnimeThemes"/>,
    /// which is also what records written by older versions deserialize to.
    /// </summary>
    public ThemeSource Source { get; set; } = ThemeSource.AnimeThemes;

    /// <summary>
    /// Gets or sets the theme directory this file lives in, relative to the item folder
    /// (<c>theme-music</c> or <c>backdrops</c>). Older records omit it; an empty value means
    /// <c>theme-music</c>, which is what those records always were in practice.
    /// </summary>
    public string Directory { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the original source URL, for imported themes. Empty for sync downloads.
    /// </summary>
    public string SourceUrl { get; set; } = string.Empty;
}
