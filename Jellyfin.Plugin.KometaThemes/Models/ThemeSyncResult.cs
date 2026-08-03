namespace Jellyfin.Plugin.KometaThemes.Models;

/// <summary>
/// Outcome of processing one item's themes.
/// </summary>
/// <remarks>
/// The downloader used to return a bare <c>bool</c> meaning "something changed", which conflated
/// three different outcomes: a successful download, a no-op because the file already existed, and
/// an outright failure. Because a failed download returned <c>false</c> rather than throwing, the
/// sync runner treated the item as processed-without-error and *removed* it from the failed-items
/// store, so items whose downloads all failed disappeared from the dashboard and were silently
/// retried in full on every subsequent sync. Reporting failures explicitly keeps that bookkeeping
/// honest.
/// </remarks>
/// <param name="Downloaded">Number of theme files newly written to disk.</param>
/// <param name="Failed">Number of theme files that could not be written.</param>
/// <param name="Skipped">Number of theme files already present and left untouched.</param>
public readonly record struct ThemeSyncResult(int Downloaded, int Failed, int Skipped)
{
    /// <summary>
    /// Gets a value indicating whether anything was written to disk.
    /// </summary>
    public bool ChangesMade => Downloaded > 0;

    /// <summary>
    /// Gets a value indicating whether at least one theme file failed to download.
    /// </summary>
    public bool HasFailures => Failed > 0;

    /// <summary>
    /// Combines two results.
    /// </summary>
    /// <param name="left">First result.</param>
    /// <param name="right">Second result.</param>
    /// <returns>The summed result.</returns>
    public static ThemeSyncResult operator +(ThemeSyncResult left, ThemeSyncResult right)
        => new(left.Downloaded + right.Downloaded, left.Failed + right.Failed, left.Skipped + right.Skipped);

    /// <summary>
    /// Combines two results. Named alternative to the <c>+</c> operator.
    /// </summary>
    /// <param name="left">First result.</param>
    /// <param name="right">Second result.</param>
    /// <returns>The summed result.</returns>
    public static ThemeSyncResult Add(ThemeSyncResult left, ThemeSyncResult right) => left + right;
}
