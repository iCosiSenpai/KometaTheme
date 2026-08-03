using System;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.KometaThemes.Sync;

/// <summary>
/// Live KometaThemes sync status response.
/// </summary>
/// <param name="Phase">Current phase.</param>
/// <param name="TotalItems">Total candidate items.</param>
/// <param name="ProcessedItems">Processed items.</param>
/// <param name="ResolvedItems">Resolved items.</param>
/// <param name="DownloadedItems">Downloaded items.</param>
/// <param name="SkippedItems">Skipped items.</param>
/// <param name="FailedItems">Failed items.</param>
/// <param name="ProgressPercent">Progress percentage.</param>
/// <param name="Message">Display message.</param>
/// <param name="UpdatedUtc">UTC update time.</param>
/// <param name="IsFinished">Whether the latest sync has finished.</param>
public record SyncStatusResponse(
    [property: JsonPropertyName("phase")] string Phase,
    [property: JsonPropertyName("totalItems")] int TotalItems,
    [property: JsonPropertyName("processedItems")] int ProcessedItems,
    [property: JsonPropertyName("resolvedItems")] int ResolvedItems,
    [property: JsonPropertyName("downloadedItems")] int DownloadedItems,
    [property: JsonPropertyName("skippedItems")] int SkippedItems,
    [property: JsonPropertyName("failedItems")] int FailedItems,
    [property: JsonPropertyName("progressPercent")] double ProgressPercent,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("updatedUtc")] DateTime UpdatedUtc,
    [property: JsonPropertyName("isFinished")] bool IsFinished)
{
    /// <summary>
    /// Creates an idle status response.
    /// </summary>
    /// <returns>An idle status response.</returns>
    public static SyncStatusResponse Idle()
    {
        return new SyncStatusResponse("idle", 0, 0, 0, 0, 0, 0, 0, string.Empty, DateTime.UtcNow, true);
    }
}
