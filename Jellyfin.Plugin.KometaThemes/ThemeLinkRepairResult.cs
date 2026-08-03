namespace Jellyfin.Plugin.KometaThemes;

/// <summary>
/// Result of a theme link repair operation.
/// </summary>
public class ThemeLinkRepairResult
{
    /// <summary>
    /// Gets or sets the number of theme songs found on disk.
    /// </summary>
    public int SongsOnDisk { get; set; }

    /// <summary>
    /// Gets or sets the number of theme videos found on disk.
    /// </summary>
    public int VideosOnDisk { get; set; }

    /// <summary>
    /// Gets or sets the number of extras whose linking was fixed.
    /// </summary>
    public int Repaired { get; set; }

    /// <summary>
    /// Gets or sets the number of files on disk that have no library item yet (library scan needed).
    /// </summary>
    public int NotScanned { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the owner's ExtraIds were updated.
    /// </summary>
    public bool OwnerUpdated { get; set; }

    /// <summary>
    /// Gets or sets the number of dangling theme extra references removed from the owner.
    /// </summary>
    public int StaleLinksRemoved { get; set; }

    /// <summary>
    /// Gets or sets the number of theme songs Jellyfin reports for the item after repair.
    /// </summary>
    public int RegisteredSongs { get; set; }

    /// <summary>
    /// Gets or sets the number of theme videos Jellyfin reports for the item after repair.
    /// </summary>
    public int RegisteredVideos { get; set; }
}
