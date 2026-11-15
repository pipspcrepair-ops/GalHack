using System;
using Godot;

namespace Slice.Game;

/// <summary>
/// Keeps the garbage collector's pauses short and out of the way while the world streams. Left alone, .NET lets tens
/// of megabytes of young objects pile up on this kind of machine and then stops every thread to copy the half still
/// alive: 40 to 60 ms, a visible hitch every couple of seconds on a road. So the young objects are collected before
/// that, in the first frame with room left for its pause (<see cref="Room"/>, the frame pacer's), or at once once far past it.
/// A pause grows more slowly than the bytes since the last (6 MB about 2 ms, 64 MB about 6), so collecting as late as the room allows costs least.
/// </summary>
public partial class GcPacer : Node
{
    /// <summary>Allocated at least this many bytes (every thread) since the last collection: the first frame with room for its pause collects.</summary>
    public long LeastBytes = 8L * 1024 * 1024;

    /// <summary>Past this many, it collects whatever the frame.</summary>
    public long MostBytes = 64L * 1024 * 1024;

    /// <summary>The room left in this frame for work that can wait, ms (none given: always room).</summary>
    public Func<double> Room;

    /// <summary>A collection's pause a megabyte allocated since the last, ms (eased): what this frame's would take.</summary>
    public double PausePerMb { get; private set; } = 0.1;

    long _last;

    public override void _Process(double delta)
    {
        long since = GC.GetTotalAllocatedBytes(false) - _last;
        if (since < LeastBytes) return;
        double mb = since / 1048576.0;
        if (since < MostBytes && Room != null && mb * PausePerMb > Room()) return;
        TimeSpan before = GC.GetTotalPauseDuration();
        GC.Collect(0, GCCollectionMode.Forced, blocking: true, compacting: false);
        PausePerMb += ((GC.GetTotalPauseDuration() - before).TotalMilliseconds / mb - PausePerMb) * 0.3;
        _last = GC.GetTotalAllocatedBytes(false);
    }
}
