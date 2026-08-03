using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Jellyfin.Plugin.KometaThemes.Models;

#pragma warning disable CA1002, CS1591

/// <summary>
/// A set of themes that appear to belong to the same season of an anime.
/// </summary>
/// <remarks>
/// <see cref="SeasonNumber"/> is a positional ordinal over the distinct episode ranges
/// animethemes.moe reports, not a season number the source actually states. It is only comparable
/// to a Jellyfin season number when the ranges form a clean ascending partition — see
/// <c>ThemeGrouper.FormsSeasonPartition</c>.
/// </remarks>
public sealed record SeasonGroup(
    int SeasonNumber,
    int? StartEpisode,
    int? EndEpisode,
    Collection<FlattenedTheme> Themes
)
{
    /// <summary>
    /// Sentinel <see cref="SeasonNumber"/> for themes whose episode range could not be parsed, so
    /// they belong to no identifiable season.
    /// </summary>
    public const int UnclassifiedSeason = 0;

    /// <summary>
    /// Gets a value indicating whether this group holds themes that could not be attributed to a season.
    /// </summary>
    public bool IsUnclassified => SeasonNumber == UnclassifiedSeason;
}

#pragma warning restore CA1002
