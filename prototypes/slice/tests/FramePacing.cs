using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Godot;

namespace Slice.Tests;

/// <summary>
/// How evenly the picture moves, frame by frame (the owner's "consistent lowkey stuttery framerate", which a
/// count of slow frames cannot see). Runs after everything else in a frame (the game's camera placed): the
/// interval since the last frame against the display's refresh and the rate actually reached, physics ticks
/// per drawn frame, how evenly the camera moves and turns against its own pace, and the garbage collector.
/// </summary>
public partial class FramePacing : Node
{
    public Func<Camera3D> Camera;
    public bool On;

    readonly List<double> _dt = new(), _delta = new();
    readonly List<int> _ticks = new();
    readonly List<Vector3> _pos = new();
    readonly List<float> _turn = new();
    readonly List<long> _alloc = new();
    long _last, _allocWas;
    ulong _tickWas;
    int _gc0, _gc1, _gc2;
    TimeSpan _paused0;
    Basis? _basisWas;
    readonly Stopwatch _clock = new();

    /// <summary>The display's refresh interval, ms (the window's screen).</summary>
    public static double Refresh => 1000.0 / Math.Max(1.0, DisplayServer.ScreenGetRefreshRate(DisplayServer.WindowGetCurrentScreen()));

    public override void _Ready() => ProcessPriority = int.MaxValue;

    public static string Screens()
    {
        int n = DisplayServer.GetScreenCount();
        return string.Join("; ", Enumerable.Range(0, n).Select(i => string.Create(CultureInfo.InvariantCulture,
            $"screen {i}{(i == DisplayServer.GetPrimaryScreen() ? " (primary)" : "")}{(i == DisplayServer.WindowGetCurrentScreen() ? " (window)" : "")} {DisplayServer.ScreenGetSize(i)} at {DisplayServer.ScreenGetRefreshRate(i):F2} Hz")));
    }

    public void Start()
    {
        _dt.Clear(); _delta.Clear(); _ticks.Clear(); _pos.Clear(); _turn.Clear(); _alloc.Clear();
        _last = 0;
        _basisWas = null;
        (_gc0, _gc1, _gc2) = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
        _paused0 = GC.GetTotalPauseDuration();
        _clock.Restart();
        On = true;
    }

    public override void _Process(double delta)
    {
        long now = Stopwatch.GetTimestamp();
        ulong tick = Engine.GetPhysicsFrames();
        long alloc = GC.GetAllocatedBytesForCurrentThread();
        if (On && _last != 0 && Camera?.Invoke() is Camera3D cam)
        {
            _dt.Add((now - _last) * 1000.0 / Stopwatch.Frequency);
            _delta.Add(delta * 1000.0);
            _ticks.Add((int)(tick - _tickWas));
            _alloc.Add(alloc - _allocWas);
            Transform3D t = cam.GlobalTransform;
            _pos.Add(t.Origin);
            Basis b = t.Basis.Orthonormalized();
            _turn.Add(_basisWas is Basis was ? was.GetRotationQuaternion().AngleTo(b.GetRotationQuaternion()) : 0f);
            _basisWas = b;
        }
        else if (Camera?.Invoke() is Camera3D c) _basisWas = c.GlobalTransform.Basis.Orthonormalized();
        _last = now;
        _tickWas = tick;
        _allocWas = alloc;
    }

    /// <summary>What was measured since <see cref="Start"/>, worked out.</summary>
    public sealed record Result(int Frames, double Fps, double Refresh, double MeanMs, double SdMs, double OffRefresh, double OffPace,
        double P99, double Over, string Vblanks, string Ticks, double MoveSd, double MoveOff, double TurnSd, double TurnOff, double MoveOffGame, double TurnOffGame,
        double Gc0PerS, double Gc1PerS, double Gc2PerS, double GcMsPerS, double KbPerFrame)
    {
        public override string ToString() => string.Create(CultureInfo.InvariantCulture,
            $"{Frames} frames, {Fps:F1} fps against {1000 / Refresh:F1} Hz: interval mean {MeanMs:F2} ms, sd {SdMs:F2} ms, p99 {P99:F1} ms, " +
            $"over 16.7 ms {Over:F1}/min; more than 2 ms off the refresh interval {OffRefresh:F1}%, off the rate reached {OffPace:F1}%; " +
            $"refreshes a frame {Vblanks}; ticks a frame {Ticks}; camera step against its pace sd {MoveSd:F3}, over 25% off {MoveOff:F1}% ({MoveOffGame:F1}% by the engine's delta); " +
            $"turn sd {TurnSd:F3}, over 25% off {TurnOff:F1}% ({TurnOffGame:F1}% by the engine's delta); gc0 {Gc0PerS:F2}/s, gc1 {Gc1PerS:F2}/s, gc2 {Gc2PerS:F2}/s, paused {GcMsPerS:F1} ms/s; main thread {KbPerFrame:F0} KB a frame");
    }

    public Result Stop()
    {
        On = false;
        double seconds = _clock.Elapsed.TotalSeconds;
        int n = _dt.Count;
        if (n < 10) return null;
        double refresh = Refresh;
        double mean = _dt.Average();
        double sd = Math.Sqrt(_dt.Sum(d => (d - mean) * (d - mean)) / n);
        var sorted = _dt.OrderBy(d => d).ToList();
        double median = sorted[n / 2];
        // The rate reached: the frame's interval in whole refreshes (as it shows on screen), by its median.
        double pace = Math.Max(1, Math.Round(median / refresh)) * refresh;
        string Hist(IEnumerable<int> xs) => string.Join(" ", xs.GroupBy(x => x).OrderBy(g => g.Key).Select(g => string.Create(CultureInfo.InvariantCulture, $"{g.Key}:{g.Count() * 100.0 / n:F0}%")));
        // Camera evenness: each frame's step against the pace over the frames round it (a second's), scaled by the frame's own interval.
        (double Sd, double Off) Even(IReadOnlyList<double> step, List<double> clock)
        {
            var r = new List<double>();
            const int W = 30;
            for (int i = 1; i < n; i++)
            {
                int a = Math.Max(1, i - W), z = Math.Min(n - 1, i + W);
                double s = 0, t = 0;
                for (int k = a; k <= z; k++) { s += step[k]; t += clock[k]; }
                double want = s / t * clock[i];
                if (s / t < 1e-4) continue; // not moving
                r.Add(step[i] / want);
            }
            if (r.Count < 10) return (double.NaN, double.NaN);
            double m = r.Average();
            return (Math.Sqrt(r.Sum(x => (x - m) * (x - m)) / r.Count), r.Count(x => Math.Abs(x - 1) > 0.25) * 100.0 / r.Count);
        }
        var moves = new List<double> { 0 };
        for (int i = 1; i < n; i++) moves.Add(_pos[i].DistanceTo(_pos[i - 1]));
        var (moveSd, moveOff) = Even(moves, _dt);
        var turns = _turn.Select(x => (double)x).ToList();
        var (turnSd, turnOff) = Even(turns, _dt);
        var (_, moveOffGame) = Even(moves, _delta);
        var (_, turnOffGame) = Even(turns, _delta);
        return new Result(n, n / seconds, refresh, mean, sd, _dt.Count(d => Math.Abs(d - refresh) > 2) * 100.0 / n,
            _dt.Count(d => Math.Abs(d - pace) > 2) * 100.0 / n, sorted[Math.Min(n - 1, (int)(n * 0.99))], _dt.Count(d => d > 16.7) / (seconds / 60),
            Hist(_dt.Select(d => (int)Math.Max(1, Math.Round(d / refresh)))), Hist(_ticks), moveSd, moveOff, turnSd, turnOff, moveOffGame, turnOffGame,
            (GC.CollectionCount(0) - _gc0) / seconds, (GC.CollectionCount(1) - _gc1) / seconds, (GC.CollectionCount(2) - _gc2) / seconds, (GC.GetTotalPauseDuration() - _paused0).TotalMilliseconds / seconds,
            _alloc.Average() / 1024.0);
    }

    /// <summary>Every frame's numbers (interval ms, ticks, camera step m, turn rad, KB), for a closer look.</summary>
    public void Dump(string path)
    {
        using var f = new System.IO.StreamWriter(path);
        f.WriteLine("dt,delta,ticks,step,turn,kb");
        for (int i = 0; i < _dt.Count; i++)
            f.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{_dt[i]:F3},{_delta[i]:F3},{_ticks[i]},{(i == 0 ? 0 : _pos[i].DistanceTo(_pos[i - 1])):F4},{_turn[i]:F5},{_alloc[i] / 1024.0:F1}"));
    }
}
