namespace Jellyfin.Plugin.KometaThemes.Models;

/// <summary>
/// Where a downloaded theme file came from.
/// </summary>
/// <remarks>
/// This drives pruning. A sync only ever recomputes the set of expected file names from the
/// animethemes.moe graph, so any tracked file that is not in that set looks like an orphan and gets
/// deleted. Files imported from another source (a pasted YouTube link) can never appear in that set,
/// so without a provenance marker they would be tracked, classified as orphans and removed on the
/// very next sync. Only <see cref="AnimeThemes"/> records participate in prune and force-sync
/// cleanup.
/// </remarks>
public enum ThemeSource
{
    /// <summary>
    /// Downloaded from animethemes.moe, either by a sync or by the Theme Finder. Prunable.
    /// </summary>
    AnimeThemes = 0,

    /// <summary>
    /// Imported from a user-supplied YouTube link. Never pruned by a sync.
    /// </summary>
    YouTube = 1
}
