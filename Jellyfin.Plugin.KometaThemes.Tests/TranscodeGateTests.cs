using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.KometaThemes.Sync;
using Xunit;

namespace Jellyfin.Plugin.KometaThemes.Tests;

/// <summary>
/// The Theme Finder used to cap ffmpeg concurrency with a semaphore held in a controller field,
/// which ASP.NET Core creates per request — so it limited nothing across requests and N parallel
/// download requests started 2N transcodes. This gate is a DI singleton shared by sync, the Theme
/// Finder and YouTube import.
/// </summary>
public class TranscodeGateTests
{
    [Fact]
    public async Task AcquireAsync_AllowsUpToTheCapConcurrently()
    {
        using var gate = new TranscodeGate();

        var slots = new IDisposable[TranscodeGate.MaxConcurrentTranscodes];
        for (var i = 0; i < slots.Length; i++)
        {
            slots[i] = await gate.AcquireAsync(CancellationToken.None);
        }

        Assert.Equal(0, gate.AvailableSlots);

        foreach (var slot in slots)
        {
            slot.Dispose();
        }

        Assert.Equal(TranscodeGate.MaxConcurrentTranscodes, gate.AvailableSlots);
    }

    [Fact]
    public async Task AcquireAsync_BlocksBeyondTheCapUntilASlotIsReleased()
    {
        using var gate = new TranscodeGate();

        var held = new IDisposable[TranscodeGate.MaxConcurrentTranscodes];
        for (var i = 0; i < held.Length; i++)
        {
            held[i] = await gate.AcquireAsync(CancellationToken.None);
        }

        var queued = gate.AcquireAsync(CancellationToken.None);
        Assert.False(queued.IsCompleted, "The gate must not hand out more slots than the cap.");

        held[0].Dispose();

        var acquired = await queued;
        Assert.NotNull(acquired);
        acquired.Dispose();

        for (var i = 1; i < held.Length; i++)
        {
            held[i].Dispose();
        }
    }

    [Fact]
    public async Task Slot_IsIdempotent()
    {
        using var gate = new TranscodeGate();
        var slot = await gate.AcquireAsync(CancellationToken.None);

        slot.Dispose();
        slot.Dispose();

        // A double dispose must not hand back a permit twice and inflate the cap.
        Assert.Equal(TranscodeGate.MaxConcurrentTranscodes, gate.AvailableSlots);
    }

    [Fact]
    public async Task AcquireAsync_HonoursCancellation()
    {
        using var gate = new TranscodeGate();

        var held = await Task.WhenAll(Enumerable
            .Range(0, TranscodeGate.MaxConcurrentTranscodes)
            .Select(_ => gate.AcquireAsync(CancellationToken.None)));

        using var cts = new CancellationTokenSource();
        var queued = gate.AcquireAsync(cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);

        foreach (var slot in held)
        {
            slot.Dispose();
        }
    }

    [Fact]
    public async Task Release_AfterDispose_DoesNotThrow()
    {
        // Shutdown ordering: a transcode still holding a slot releases it from a finally block after
        // the gate itself has been disposed.
        var gate = new TranscodeGate();
        var slot = await gate.AcquireAsync(CancellationToken.None);

        gate.Dispose();
        slot.Dispose();
    }
}
