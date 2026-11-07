using System;
using Godot;

namespace Slice.Game;

/// <summary>
/// Keeps the garbage collector's pauses short while the world streams. Left alone, .NET lets tens of
/// megabytes of young objects pile up on this kind of machine and then stops every thread to copy the
/// half still alive: 40 to 60 ms, a visible hitch every couple of seconds on a road. Collecting the
/// young objects little and often copies little each time, which fits in a frame.
/// </summary>
public partial class GcPacer : Node
{
    /// <summary>Allocated this many bytes since the last collection: time for the next.</summary>
    public long EveryBytes = 6 * 1024 * 1024;

    long _last;

    public override void _Process(double delta)
    {
        long now = GC.GetTotalAllocatedBytes(false);
        if (now - _last < EveryBytes) return;
        _last = now;
        GC.Collect(0, GCCollectionMode.Forced, blocking: true, compacting: false);
    }
}
