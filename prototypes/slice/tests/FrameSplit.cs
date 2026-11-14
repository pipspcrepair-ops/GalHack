using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Godot;

namespace Slice.Tests;

/// <summary>
/// Every frame's main thread in its engine-level stages, wall clock: the scripts of the physics ticks; the engine's own
/// physics after them (the rest of each tick's scene work, the Jolt step, the next tick's body callbacks); the process
/// scripts; the scene's flush after them (deferred calls, skeletons, frees, transform notices: to the renderer's start);
/// the renderer's whole draw (its own CPU measure, setup and viewport, given apart, and the rest of it: syncing what
/// changed, shaders, waiting on the card and the screen); and what is left (input, the next frame's start). Also each
/// frame's collector pause, what the main thread allocated, and the draw calls and objects drawn. A frame over
/// <see cref="HitchMs"/> is written down whole, with the systems that took the most of it (<see cref="SystemSplit"/>).
/// </summary>
public sealed partial class FrameSplit
{
    sealed partial class Edge : Node
    {
        public FrameSplit Split;
        public bool Start;
        public override void _Process(double delta) => Split.Mark(Start, false);
        public override void _PhysicsProcess(double delta) => Split.Mark(Start, true);
    }

    public readonly record struct Frame(double Total, double PhysicsScripts, double PhysicsEngine, double ProcessScripts, double Flush, double Draw, double Render,
        double Pause, long Bytes, int Ticks, int DrawCalls, int Objects, int ShadowDraws)
    {
        public double Rest => Math.Max(0, Total - PhysicsScripts - PhysicsEngine - ProcessScripts - Flush - Draw);
        public double DrawOther => Math.Max(0, Draw - Render);
    }

    public const double HitchMs = 100;
    public readonly List<Frame> Frames = new();
    public readonly List<string> Hitches = new();
    /// <summary>What the frame was doing, for a hitch's note (the route sets it).</summary>
    public Func<string> Doing = () => "";
    public SystemSplit Systems;
    readonly Rid _viewport;
    long _frameStart, _passStart, _physicsEnd, _processEnd, _preDraw, _alloc;
    double _physicsScripts, _physicsEngine, _processScripts, _flush, _draw;
    int _ticks;
    bool _open;
    TimeSpan _paused;
    readonly double[] _pipes = new double[4];
    static readonly Performance.Monitor[] Pipes = { Performance.Monitor.PipelineCompilationsDraw, Performance.Monitor.PipelineCompilationsSpecialization, Performance.Monitor.PipelineCompilationsSurface, Performance.Monitor.PipelineCompilationsMesh };

    public FrameSplit(Node root, Rid viewport)
    {
        _viewport = viewport;
        root.AddChild(new Edge { Split = this, Start = true, Name = "Frame split start", ProcessPriority = -3_000_000, ProcessPhysicsPriority = -3_000_000 });
        root.AddChild(new Edge { Split = this, Start = false, Name = "Frame split end", ProcessPriority = 3_000_000, ProcessPhysicsPriority = 3_000_000 });
        RenderingServer.FramePreDraw += () =>
        {
            _preDraw = Stopwatch.GetTimestamp();
            if (_processEnd != 0) _flush += Ms(_processEnd, _preDraw);
            _processEnd = 0;
        };
        RenderingServer.FramePostDraw += () =>
        {
            if (_preDraw != 0) _draw += Ms(_preDraw, Stopwatch.GetTimestamp());
            _preDraw = 0;
        };
    }

    public void Reset()
    {
        Frames.Clear();
        Hitches.Clear();
    }

    void Mark(bool start, bool physics)
    {
        long now = Stopwatch.GetTimestamp();
        if (start)
        {
            if (!_open) Close(now);
            if (_physicsEnd != 0) _physicsEngine += Ms(_physicsEnd, now);
            _physicsEnd = 0;
            _passStart = now;
            return;
        }
        if (physics)
        {
            _physicsScripts += Ms(_passStart, now);
            _physicsEnd = now;
            _ticks++;
            return;
        }
        _processScripts += Ms(_passStart, now);
        _processEnd = now;
        _open = false;
    }

    /// <summary>The frame just ended (the renderer has drawn it): its parts go in, and the next one starts.</summary>
    void Close(long now)
    {
        _open = true;
        TimeSpan paused = GC.GetTotalPauseDuration();
        long alloc = GC.GetAllocatedBytesForCurrentThread();
        if (_frameStart != 0)
        {
            double render = RenderingServer.ViewportGetMeasuredRenderTimeCpu(_viewport) + RenderingServer.GetFrameSetupTimeCpu();
            var f = new Frame(Ms(_frameStart, now), _physicsScripts, _physicsEngine, _processScripts, _flush, _draw, render, (paused - _paused).TotalMilliseconds, alloc - _alloc, _ticks,
                (int)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame), (int)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalObjectsInFrame),
                RenderingServer.ViewportGetRenderInfo(_viewport, RenderingServer.ViewportRenderInfoType.Shadow, RenderingServer.ViewportRenderInfo.DrawCallsInFrame));
            Frames.Add(f);
            if (f.Total > HitchMs)
            {
                string pipes = string.Join("/", Pipes.Select((m, i) => Performance.GetMonitor(m) - _pipes[i]));
                Hitches.Add(string.Create(CultureInfo.InvariantCulture, $"{f.Total:F0} ms at frame {Frames.Count} ({Parts(f)}; pipelines compiled (draw/specialization/surface/mesh) {pipes}; {Doing()}; systems: {Systems?.LastFrame() ?? "?"})"));
            }
        }
        for (int i = 0; i < Pipes.Length; i++) _pipes[i] = Performance.GetMonitor(Pipes[i]);
        _frameStart = now;
        _paused = paused;
        _alloc = alloc;
        _physicsScripts = _physicsEngine = _processScripts = _flush = _draw = 0;
        _ticks = 0;
    }

    static double Ms(long a, long b) => (b - a) * 1000.0 / Stopwatch.Frequency;

    static readonly (string Name, Func<Frame, double> Of)[] PartsOf =
    {
        ("physics scripts", f => f.PhysicsScripts), ("physics engine", f => f.PhysicsEngine), ("process scripts", f => f.ProcessScripts),
        ("scene flush", f => f.Flush), ("render cpu", f => f.Render), ("draw other", f => f.DrawOther), ("rest", f => f.Rest), ("gc pause", f => f.Pause),
    };

    static string Parts(Frame f) => string.Join(", ", PartsOf.Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.Name} {p.Of(f):F1}"))) + $", {f.Ticks} ticks";

    /// <summary>Means, 99th percentiles and the frames over budget by what was largest in them; the worst frame whole; the hitches.</summary>
    public string Report(double budget = 16.7)
    {
        if (Frames.Count == 0) return "(no frames)";
        static double P(IEnumerable<double> v, double q)
        {
            var s = v.OrderBy(x => x).ToList();
            return s[Math.Min(s.Count - 1, (int)(s.Count * q))];
        }
        var over = Frames.Where(f => f.Total > budget).ToList();
        string Blame(Frame f) => PartsOf.Take(7).OrderByDescending(p => p.Of(f)).First().Name;
        return string.Create(CultureInfo.InvariantCulture,
            $"{string.Join(", ", PartsOf.Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.Name} mean {Frames.Average(p.Of):F2} p99 {P(Frames.Select(p.Of), 0.99):F1}")))}; " +
            $"ticks a frame {Frames.Average(f => f.Ticks):F2}, {Frames.Average(f => f.Bytes) / 1024.0:F0} KB allocated a frame, draw calls mean {Frames.Average(f => f.DrawCalls):F0} (p99 {P(Frames.Select(f => (double)f.DrawCalls), 0.99):F0}), objects mean {Frames.Average(f => f.Objects):F0}, shadow draw calls mean {Frames.Average(f => f.ShadowDraws):F0}; " +
            $"{over.Count} frames over {budget} ms, largest part in them: {(over.Count == 0 ? "none" : string.Join(", ", over.GroupBy(Blame).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}")))}" +
            $"{(over.Count == 0 ? "" : $" (their means: {string.Join(", ", PartsOf.Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.Name} {over.Average(p.Of):F1}")))})")}; " +
            $"hitches over {HitchMs} ms: {Hitches.Count}");
    }
}
