using System;

namespace Jellyfin.Plugin.KometaThemes.Sync;

/// <summary>
/// Tracks live KometaThemes sync progress independently from Jellyfin scheduled-task progress.
/// </summary>
public sealed class SyncStatusTracker
{
    private readonly object _gate = new();
    private SyncStatusResponse _status = SyncStatusResponse.Idle();

    /// <summary>
    /// Gets the current status snapshot.
    /// </summary>
    /// <returns>Current sync status.</returns>
    public SyncStatusResponse GetStatus()
    {
        lock (_gate)
        {
            return _status;
        }
    }

    /// <summary>
    /// Starts a new status session.
    /// </summary>
    /// <param name="totalItems">Total candidate item count.</param>
    public void Start(int totalItems)
    {
        Update("scan", totalItems, 0, 0, 0, 0, 0, null, false);
    }

    /// <summary>
    /// Marks the current session as finished without success, so the UI does not keep showing a
    /// stale in-progress snapshot.
    /// </summary>
    /// <param name="phase">Terminal phase name, e.g. <c>cancelled</c> or <c>failed</c>.</param>
    /// <param name="message">Display message.</param>
    public void Finish(string phase, string message)
    {
        lock (_gate)
        {
            var previous = _status;
            _status = previous with
            {
                Phase = phase,
                Message = message,
                ProgressPercent = 100,
                UpdatedUtc = DateTime.UtcNow,
                IsFinished = true
            };
        }
    }

    /// <summary>
    /// Updates the status snapshot.
    /// </summary>
    /// <param name="phase">Current phase.</param>
    /// <param name="totalItems">Total candidate item count.</param>
    /// <param name="processedItems">Processed item count.</param>
    /// <param name="resolvedItems">Resolved item count.</param>
    /// <param name="downloadedItems">Downloaded item count.</param>
    /// <param name="skippedItems">Skipped item count.</param>
    /// <param name="failedItems">Failed item count.</param>
    /// <param name="message">Optional display message.</param>
    /// <param name="isFinished">Whether the sync is finished.</param>
    public void Update(
        string phase,
        int totalItems,
        int processedItems,
        int resolvedItems,
        int downloadedItems,
        int skippedItems,
        int failedItems = 0,
        string? message = null,
        bool isFinished = false)
    {
        lock (_gate)
        {
            _status = new SyncStatusResponse(
                phase,
                totalItems,
                processedItems,
                resolvedItems,
                downloadedItems,
                skippedItems,
                failedItems,
                CalculateProgress(phase, totalItems, processedItems, resolvedItems, isFinished),
                message ?? string.Empty,
                DateTime.UtcNow,
                isFinished);
        }
    }

    /// <summary>
    /// Maps the current phase onto an overall percentage.
    /// </summary>
    /// <remarks>
    /// The download phase is driven by <paramref name="processedItems"/> rather than by the number of
    /// items that actually downloaded something. Keying it off downloads made the bar stall at 70%
    /// for any run where most items were already satisfied, because nothing incremented.
    /// </remarks>
    private static double CalculateProgress(
        string phase,
        int totalItems,
        int processedItems,
        int resolvedItems,
        bool isFinished)
    {
        if (isFinished)
        {
            return 100;
        }

        if (totalItems <= 0)
        {
            return 0;
        }

        var progress = phase switch
        {
            "scan" => 5,
            "filter" => 10,
            "resolve" => 10 + (resolvedItems * 60.0 / totalItems),
            "download" => 70 + (processedItems * 30.0 / totalItems),
            "failed" => 100,
            "cancelled" => 100,
            _ => processedItems * 100.0 / totalItems
        };

        return Math.Round(Math.Min(99, Math.Max(0, progress)), 1);
    }
}
