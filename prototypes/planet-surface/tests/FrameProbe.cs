using System.Diagnostics;
using Godot;

namespace PlanetSurface;

/// <summary>
/// Frame timing that Godot's monitors can't give. Those hold the slowest process pass and the
/// slowest physics pass of a whole second (possibly from different frames), and a reading can
/// be up to a second stale, so they blame a frame for whatever happened two seconds earlier.
/// This measures every frame:
/// - <see cref="WorstMs"/>: game callbacks on the main thread, from the first node's _Process to
///   the last one's, plus the physics ticks that ran earlier in the same frame;
/// - <see cref="WorstGapMs"/>: from the end of one frame's callbacks to the start of the next
///   one's, which holds the engine's own work (the physics step, deferred frees, the .NET frame
///   callback, rendering) and the wait for the frame cap. At a 60 fps cap it sits near 16.7 ms
///   minus the frame's work; an engine stall shows as a longer gap.
/// </summary>
public sealed class FrameProbe
{
    readonly FrameProbeEdge _first, _last;
    long _processStart, _physicsStart, _lastEnd;
    double _physicsMs;
    bool _inFrame;

    public double LastMs { get; private set; }
    public double WorstMs { get; private set; }
    public double WorstGapMs { get; private set; }
    public int Frames { get; private set; }

    public FrameProbe(Node parent)
    {
        _first = new FrameProbeEdge { Probe = this, IsStart = true, ProcessPriority = -1_000_000, ProcessPhysicsPriority = -1_000_000 };
        _last = new FrameProbeEdge { Probe = this, IsStart = false, ProcessPriority = 1_000_000, ProcessPhysicsPriority = 1_000_000 };
        parent.AddChild(_first);
        parent.AddChild(_last);
    }

    public void Reset()
    {
        WorstMs = 0;
        WorstGapMs = 0;
        Frames = 0;
    }

    public void Remove()
    {
        _first.QueueFree();
        _last.QueueFree();
    }

    internal void Mark(bool start, bool physics)
    {
        long now = Stopwatch.GetTimestamp();
        // The first callback of a frame (a physics tick, or the process pass if no tick is due).
        if (start && !_inFrame)
        {
            _inFrame = true;
            if (_lastEnd != 0)
            {
                double gap = Stopwatch.GetElapsedTime(_lastEnd, now).TotalMilliseconds;
                if (gap > WorstGapMs) WorstGapMs = gap;
            }
        }
        if (physics)
        {
            if (start) _physicsStart = now;
            else _physicsMs += Stopwatch.GetElapsedTime(_physicsStart, now).TotalMilliseconds;
            return;
        }
        if (start)
        {
            _processStart = now;
            return;
        }
        // Physics ticks run before the process pass in Godot's frame, so they belong to this frame.
        LastMs = Stopwatch.GetElapsedTime(_processStart, now).TotalMilliseconds + _physicsMs;
        _physicsMs = 0;
        _inFrame = false;
        _lastEnd = now;
        Frames++;
        if (LastMs > WorstMs) WorstMs = LastMs;
    }
}

/// <summary>One edge of a <see cref="FrameProbe"/>: runs first or last in every process and physics pass.</summary>
public partial class FrameProbeEdge : Node
{
    public FrameProbe Probe;
    public bool IsStart;

    public override void _Process(double delta) => Probe?.Mark(IsStart, physics: false);
    public override void _PhysicsProcess(double delta) => Probe?.Mark(IsStart, physics: true);
}
