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

    /// <summary>
    /// Past this many, it collects whatever the frame: well under the runtime's own young budget (48 MB on the owner's machine),
    /// which would otherwise collect first, at any moment, and pause 12 to 36 ms (a firefight's breakages).
    /// </summary>
    public long MostBytes = 16L * 1024 * 1024;

    /// <summary>The room left in this frame for work that can wait, ms (none given: always room).</summary>
    public Func<double> Room;

    /// <summary>A collection's pause a megabyte allocated since the last, ms (eased): what this frame's would take.</summary>
    public double PausePerMb { get; private set; } = 0.1;

    long _last;
    int _count;

    int _fullAtDone;
    long _done = -1;
    double _runningSince;

    /// <summary>
    /// A full (generation 2) collection started and not yet over: more started than had been when the last one finished.
    /// (After 2 s it counts as over whatever the count says: two finishing between looks would otherwise hold it for good.)
    /// </summary>
    bool FullRunning()
    {
        long done = Math.Max(GC.GetGCMemoryInfo(GCKind.Background).Index, GC.GetGCMemoryInfo(GCKind.FullBlocking).Index);
        int started = GC.CollectionCount(2);
        double now = Time.GetTicksMsec() / 1000.0;
        if (done != _done)
        {
            _done = done;
            _fullAtDone = started;
        }
        if (started <= _fullAtDone) { _runningSince = 0; return false; }
        if (_runningSince == 0) _runningSince = now;
        if (now - _runningSince < 2.0) return true;
        _fullAtDone = started;
        _runningSince = 0;
        return false;
    }

    public override void _Process(double delta)
    {
        // Counted from the last collection, the runtime's own too.
        if (GC.CollectionCount(0) != _count) (_count, _last) = (GC.CollectionCount(0), GC.GetTotalAllocatedBytes(false));
        long since = GC.GetTotalAllocatedBytes(false) - _last;
        if (since < LeastBytes) return;
        double mb = since / 1048576.0;
        if (since < MostBytes && Room != null && mb * PausePerMb > Room()) return;
        // Never while a full collection runs in the background: a forced collection then waits for it, 50 to 100 ms on the
        // main thread with only a few of them counted as pause (the city's unexplained hitches, caught after this node).
        if (FullRunning()) return;
        TimeSpan before = GC.GetTotalPauseDuration();
        GC.Collect(0, GCCollectionMode.Forced, blocking: true, compacting: false);
        PausePerMb += ((GC.GetTotalPauseDuration() - before).TotalMilliseconds / mb - PausePerMb) * 0.3;
        _count = GC.CollectionCount(0);
        _last = GC.GetTotalAllocatedBytes(false);
        Collected?.Invoke(mb, (GC.GetTotalPauseDuration() - before).TotalMilliseconds);
    }

    public Action<double, double> Collected;
}
