using System;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.KometaThemes.Sync;

/// <summary>
/// Process-wide cap on concurrent ffmpeg invocations started by this plugin.
/// </summary>
/// <remarks>
/// The Theme Finder used to cap parallelism with a <see cref="SemaphoreSlim"/> held in a field of the
/// controller, which ASP.NET Core creates per request. It therefore limited concurrency only within a
/// single request: N simultaneous download requests started 2N transcodes, with nothing bounding the
/// total. On a NAS that is enough to starve playback transcoding. A DI singleton gives every entry
/// point — sync, Theme Finder, YouTube import — one shared budget.
/// </remarks>
public sealed class TranscodeGate : IDisposable
{
    /// <summary>
    /// Concurrent transcodes allowed across the whole plugin.
    /// </summary>
    /// <remarks>
    /// Deliberately small. Each slot is an ffmpeg process, and Jellyfin needs headroom on the same
    /// box for playback.
    /// </remarks>
    public const int MaxConcurrentTranscodes = 3;

    /// <remarks>
    /// CA2213 suppressed: see <see cref="Dispose"/>. Disposing this at shutdown would make the
    /// release in an in-flight transcode's finally block throw.
    /// </remarks>
#pragma warning disable CA2213
    private readonly SemaphoreSlim _gate = new(MaxConcurrentTranscodes, MaxConcurrentTranscodes);
#pragma warning restore CA2213
    private bool _disposed;

    /// <summary>
    /// Gets the number of slots currently free.
    /// </summary>
    public int AvailableSlots => _gate.CurrentCount;

    /// <summary>
    /// Waits for a transcode slot.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A handle that releases the slot when disposed.</returns>
    public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Slot(this);
    }

    private void Release()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _gate.Release();
        }
        catch (ObjectDisposedException)
        {
            // Shutting down.
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Not disposing the semaphore: transcodes may still be holding slots at shutdown, and their
        // release would then throw from a finally block. It owns no unmanaged resources.
        GC.SuppressFinalize(this);
    }

    private sealed class Slot : IDisposable
    {
        private readonly TranscodeGate _owner;
        private bool _released;

        public Slot(TranscodeGate owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            _owner.Release();
        }
    }
}
