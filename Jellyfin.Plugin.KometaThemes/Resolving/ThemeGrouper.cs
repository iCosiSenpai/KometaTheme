using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Jellyfin.Plugin.KometaThemes.Models;
using Microsoft.Extensions.Logging;

#pragma warning disable CA1002, SA1611, SA1615, CA1305, CS1591

namespace Jellyfin.Plugin.KometaThemes.Resolving;

public class ThemeGrouper
{
    private readonly ILogger<ThemeGrouper> _logger;

    public ThemeGrouper(ILogger<ThemeGrouper> logger)
    {
        _logger = logger;
    }

    public List<SeasonGroup> GroupThemesBySeason(IEnumerable<FlattenedTheme> themes)
    {
        var themeList = themes.ToList();
        var groups = new List<SeasonGroup>();

        var themesWithRanges = themeList
            .Select(t => (Theme: t, Ranges: EpisodeRangeParser.Parse(t.Entry.Episodes)))
            .ToList();

        var allRanges = themesWithRanges
            .SelectMany(t => t.Ranges)
            .Select(r => (r.StartEpisode, r.EndEpisode))
            .Distinct()
            .OrderBy(r => r.StartEpisode)
            .ToList();

        if (allRanges.Count == 0)
        {
            groups.Add(new SeasonGroup(1, null, null, new Collection<FlattenedTheme>(themeList)));
            _logger.LogDebug("No episode ranges found, all {Count} themes assigned to season 1", themeList.Count);
            return groups;
        }

        var seasonNumber = 1;
        var usedRanges = new HashSet<(int, int)>();

        foreach (var range in allRanges)
        {
            var key = (range.StartEpisode, range.EndEpisode);
            if (usedRanges.Contains(key))
            {
                continue;
            }

            usedRanges.Add(key);

            var matchingThemes = themesWithRanges
                .Where(t => t.Ranges.Any(r => r.StartEpisode == range.StartEpisode && r.EndEpisode == range.EndEpisode))
                .Select(t => t.Theme)
                .ToList();

            if (matchingThemes.Count > 0)
            {
                groups.Add(new SeasonGroup(seasonNumber, range.StartEpisode, range.EndEpisode, new Collection<FlattenedTheme>(matchingThemes)));
                _logger.LogDebug(
                    "Season {Num}: episodes {Start}-{End}, {Count} themes",
                    seasonNumber,
                    range.StartEpisode,
                    range.EndEpisode.ToString(),
                    matchingThemes.Count);
                seasonNumber++;
            }
        }

        var unmatched = themesWithRanges
            .Where(t => t.Ranges.Count == 0)
            .Select(t => t.Theme)
            .ToList();

        if (unmatched.Count > 0)
        {
            // These used to be appended as one more numbered season, which invented a season that
            // does not exist and shifted nothing else but did make the group count misleading. They
            // are now marked unclassified so season matching ignores them outright.
            groups.Add(new SeasonGroup(SeasonGroup.UnclassifiedSeason, null, null, new Collection<FlattenedTheme>(unmatched)));
            _logger.LogDebug("{Count} themes have no parsable episode range and were left unclassified", unmatched.Count);
        }

        return groups;
    }

    /// <summary>
    /// Decides whether the group ordinals can be treated as season numbers.
    /// </summary>
    /// <remarks>
    /// The ordinal is only meaningful when the episode ranges partition the series cleanly: sorted by
    /// start episode, every range must begin after the previous one ended. animethemes.moe also
    /// reports whole-run ranges alongside per-cour ones — an anime with <c>1-11</c>, <c>12</c> and
    /// <c>1-24</c> yields three groups whose ordinals mean nothing, and matching Jellyfin's season 2
    /// against that gave the single episode-12 theme.
    /// </remarks>
    /// <param name="groups">Groups produced by <see cref="GroupThemesBySeason"/>.</param>
    /// <returns>Whether ordinal-to-season matching is trustworthy for these groups.</returns>
    internal static bool FormsSeasonPartition(List<SeasonGroup> groups)
    {
        var seasonal = groups.Where(g => !g.IsUnclassified).ToList();
        if (seasonal.Count == 0)
        {
            return false;
        }

        // A single group covers the whole run; there is nothing to mis-assign.
        if (seasonal.Count == 1)
        {
            return true;
        }

        if (seasonal.Any(g => g.StartEpisode == null))
        {
            return false;
        }

        var ordered = seasonal.OrderBy(g => g.StartEpisode!.Value).ToList();
        for (var i = 1; i < ordered.Count; i++)
        {
            var previousEnd = ordered[i - 1].EndEpisode ?? ordered[i - 1].StartEpisode!.Value;
            if (ordered[i].StartEpisode!.Value <= previousEnd)
            {
                return false;
            }
        }

        // The ordinals must also already be in episode order, otherwise the numbering does not
        // correspond to the partition it is supposed to describe.
        return ordered.Select(g => g.SeasonNumber).SequenceEqual(seasonal.Select(g => g.SeasonNumber).Order());
    }

    /// <summary>
    /// Finds the group for a Jellyfin season number, or null when no confident match exists.
    /// </summary>
    /// <remarks>
    /// Returning null is deliberate and is what the caller wants: it falls back to considering every
    /// theme for the item. Previously this returned <c>groups[0]</c> whenever nothing matched, with
    /// only a debug line, so season 3 silently received season 1's themes and the wrong assignment
    /// was then written into the tracker as fact. Offering all themes is honest about the
    /// uncertainty; naming the wrong season is not.
    /// </remarks>
    /// <param name="groups">Groups produced by <see cref="GroupThemesBySeason"/>.</param>
    /// <param name="seasonNumber">The Jellyfin season number, if one was detected.</param>
    /// <returns>The matching group, or null.</returns>
    public SeasonGroup? FindMatchingGroup(List<SeasonGroup> groups, int? seasonNumber)
    {
        if (groups.Count == 0 || !seasonNumber.HasValue)
        {
            return null;
        }

        if (!FormsSeasonPartition(groups))
        {
            _logger.LogDebug(
                "Episode ranges do not form a clean season partition ({Count} groups); considering all themes for season {Num}",
                groups.Count,
                seasonNumber.Value);
            return null;
        }

        var match = groups.FirstOrDefault(g => !g.IsUnclassified && g.SeasonNumber == seasonNumber.Value);
        if (match != null)
        {
            _logger.LogDebug("Found exact season match: Season {Num}", seasonNumber.Value);
            return match;
        }

        _logger.LogDebug(
            "No group for season {Num} among {Count} groups; considering all themes",
            seasonNumber.Value,
            groups.Count);
        return null;
    }
}
