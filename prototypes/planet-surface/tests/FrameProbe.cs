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
/// <see cref="LastCpuMs"/> reads the same callbacks as the main thread's own CPU time (cycles it ran,
/// with the time it sat waiting for a core left out), so a machine busy with other work shows as a gap
/// between <see cref="LastMs"/> and it, not as a slow game.
/// </summary>
public sealed class FrameProbe
{
    readonly FrameProbeEdge _first, _last;
    long _processStart, _physicsStart, _lastEnd;
    double _physicsMs;
    ulong _processCycles, _physicsCycleStart, _physicsCycles;
    bool _inFrame;

    public double LastMs { get; private set; }

    /// <summary>The same frame's callbacks as CPU time on the main thread, ms (0 where the platform has no cycle counter).</summary>
    public double LastCpuMs { get; private set; }
    public double WorstMs { get; private set; }
    public double WorstGapMs { get; private set; }
    public int Frames { get; private set; }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern nint GetCurrentThread();

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern bool QueryThreadCycleTime(nint thread, out ulong cycles);

    /// <summary>Cycles the main thread counts in a millisecond (measured once against the wall clock), or 0 if not readable.</summary>
    static readonly double CyclesPerMs = Calibrate();

    static double Calibrate()
    {
        if (!System.OperatingSystem.IsWindows()) return 0;
        try
        {
            // The most cycles a millisecond: a spin that was kept waiting for a core counts fewer.
            double best = 0;
            for (int i = 0; i < 5; i++)
            {
                QueryThreadCycleTime(GetCurrentThread(), out ulong c0);
                long t0 = Stopwatch.GetTimestamp();
                double spin = 0;
                while (Stopwatch.GetElapsedTime(t0).TotalMilliseconds < 20) spin += 1;
                QueryThreadCycleTime(GetCurrentThread(), out ulong c1);
                double ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                if (spin > 0 && c1 > c0) best = System.Math.Max(best, (c1 - c0) / ms);
            }
            return best;
        }
        catch { return 0; }
    }

    static ulong Cycles() => CyclesPerMs > 0 && QueryThreadCycleTime(GetCurrentThread(), out ulong c) ? c : 0;

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
        ulong cycles = Cycles();
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
            if (start)
            {
                _physicsStart = now;
                _physicsCycleStart = cycles;
            }
            else
            {
                _physicsMs += Stopwatch.GetElapsedTime(_physicsStart, now).TotalMilliseconds;
                _physicsCycles += cycles - _physicsCycleStart;
            }
            return;
        }
        if (start)
        {
            _processStart = now;
            _processCycles = cycles;
            return;
        }
        // Physics ticks run before the process pass in Godot's frame, so they belong to this frame.
        LastMs = Stopwatch.GetElapsedTime(_processStart, now).TotalMilliseconds + _physicsMs;
        LastCpuMs = CyclesPerMs > 0 ? (cycles - _processCycles + _physicsCycles) / CyclesPerMs : 0;
        _physicsMs = 0;
        _physicsCycles = 0;
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
