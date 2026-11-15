using System;
using Godot;

namespace Slice.Game;

/// <summary>
/// Keeps the garbage collector's pauses short and out of the way while the world streams. Left alone, .NET lets tens
/// of megabytes of young objects pile up on this kind of machine and then stops every thread to copy the half still
/// alive: 40 to 60 ms, a visible hitch every couple of seconds on a road. So the young objects are collected before
/// that, in a frame with room left for the pause (<see cref="Room"/>, the frame pacer's), or at once once far past it.
/// A collection's pause is what it copies, not how long it waited, so collecting less often costs less in all.
/// </summary>
public partial class GcPacer : Node
{
    /// <summary>Allocated this many bytes (every thread) since the last collection: the next frame with room collects.</summary>
    public long EveryBytes = 24L * 1024 * 1024;

    /// <summary>Past this many, it collects whatever the frame.</summary>
    public long MostBytes = 64L * 1024 * 1024;

    /// <summary>The room left in this frame for work that can wait, ms (none given: always room).</summary>
    public Func<double> Room;

    /// <summary>The last collections' pause, ms (eased).</summary>
    public double PauseMs { get; private set; } = 2.0;

    long _last;

    public override void _Process(double delta)
    {
        long since = GC.GetTotalAllocatedBytes(false) - _last;
        if (since < EveryBytes) return;
        if (since < MostBytes && Room != null && Room() < PauseMs) return;
        TimeSpan before = GC.GetTotalPauseDuration();
        GC.Collect(0, GCCollectionMode.Forced, blocking: true, compacting: false);
        PauseMs += ((GC.GetTotalPauseDuration() - before).TotalMilliseconds - PauseMs) * 0.3;
        _last = GC.GetTotalAllocatedBytes(false);
    }
}
