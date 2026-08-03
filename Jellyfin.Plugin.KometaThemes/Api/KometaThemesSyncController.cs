using System;
using System.Threading;
using Jellyfin.Plugin.KometaThemes.Sync;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.KometaThemes.Api;

/// <summary>
/// REST endpoints for live sync status.
/// </summary>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("Plugins/KometaThemes/Sync")]
public class KometaThemesSyncController : ControllerBase
{
    private readonly SyncStatusTracker _statusTracker;
    private readonly SyncThemesRunner _runner;

    /// <summary>
    /// Initializes a new instance of the <see cref="KometaThemesSyncController"/> class.
    /// </summary>
    /// <param name="statusTracker">Sync status tracker.</param>
    /// <param name="runner">Sync runner.</param>
    public KometaThemesSyncController(SyncStatusTracker statusTracker, SyncThemesRunner runner)
    {
        _statusTracker = statusTracker;
        _runner = runner;
    }

    /// <summary>
    /// Gets live sync status.
    /// </summary>
    /// <returns>Current sync status.</returns>
    [HttpGet("status")]
    public ActionResult<SyncStatusResponse> GetStatus()
    {
        return Ok(_statusTracker.GetStatus());
    }

    /// <summary>
    /// Starts a preset sync run.
    /// </summary>
    /// <param name="request">Preset request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Accepted status, or conflict if another sync is running.</returns>
    [HttpPost("run")]
    public IActionResult Run([FromBody] SyncRunRequest? request, CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return BadRequest(new { error = "A preset is required." });
        }

        // Any integer deserializes into an enum field, so an out-of-range value would otherwise
        // reach the runner and fall through its preset switch unnoticed.
        if (!Enum.IsDefined(request.Preset))
        {
            return BadRequest(new { error = "Unknown preset." });
        }

        if (!_runner.TryStartPresetRun(request.Preset))
        {
            return Conflict(new { error = "A KometaThemes sync is already running." });
        }

        return Accepted(new { message = "KometaThemes sync started.", preset = request.Preset.ToString() });
    }

    /// <summary>
    /// Starts a normal manual "Sync now" run. This is always incremental
    /// (only unsatisfied items according to current settings) and ignores
    /// the persistent ForceSync checkbox. This keeps "Sync now" and "Force sync"
    /// clearly distinct.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Accepted if started, Conflict if already running.</returns>
    [HttpPost("sync")]
    public IActionResult SyncNow(CancellationToken cancellationToken)
    {
        if (!_runner.TryStartManualSync())
        {
            return Conflict(new { error = "A KometaThemes sync is already running." });
        }

        return Accepted(new { message = "KometaThemes sync started." });
    }

    /// <summary>
    /// Starts a forced full sync run. Existing themes are removed and re-downloaded
    /// according to the current settings. This does not persist ForceSync to the config.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Accepted status, or conflict if another sync is running.</returns>
    [HttpPost("force")]
    public IActionResult Force(CancellationToken cancellationToken)
    {
        if (!_runner.TryStartForcedRun())
        {
            return Conflict(new { error = "A KometaThemes sync is already running." });
        }

        return Accepted(new { message = "KometaThemes forced sync started." });
    }
}
