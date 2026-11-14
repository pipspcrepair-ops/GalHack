using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Planets.Gen;

namespace PlanetSurface.Poi;

/// <summary>
/// Builds a world's places off the main thread, one at a time on a thread of its own:
/// whatever was asked for first (the place the sleeper is heading into), then the nearest unbuilt place to the
/// focus, out to <see cref="Range"/>. A world loads with only its places' layouts; this is where they are spawned
/// and fitted to the world (<see cref="PoiSite.Build"/>), and each comes out the same from its seed whenever it is.
/// </summary>
public sealed class PoiBuilds : IDisposable
{
    readonly IReadOnlyList<PoiSite> _sites;
    readonly double _width, _height;
    readonly ConcurrentQueue<PoiSite> _asked = new();
    readonly HashSet<PoiSite> _failed = new();
    readonly AutoResetEvent _wake = new(false);
    readonly Thread _thread;
    volatile bool _stop;
    double _fx, _fz;
    readonly object _focusLock = new();
    readonly Stopwatch _clock = Stopwatch.StartNew();

    /// <summary>How far out from the focus places are built before anyone asks, m: as far as places are drawn (the layer sets it to the camera's far plane).</summary>
    public float Range = 12000f;

    /// <summary>While this says so, nothing is built unasked: a game standing a place up (a frame at a time) asks for quiet, as building's allocations would pause it.</summary>
    public Func<bool> Hold;

    /// <summary>Tests: places the builder leaves until this lets them go (a frame-time run holds an airfield back until its flight in starts).</summary>
    public static Func<PoiSite, bool> Withhold;

    /// <summary>Places built here so far, and the time from the start until every place within range of the focus first stood built, ms (-1 until then).</summary>
    public int Built;
    public double AllInRangeMs = -1;

    /// <summary>Where the builder's time went, ms (tests): building, and held while a place stood up.</summary>
    public double BusyMs, HeldMs;

    public PoiBuilds(IReadOnlyList<PoiSite> sites, double width, double height, double focusX, double focusZ)
    {
        _sites = sites;
        _width = width;
        _height = height;
        (_fx, _fz) = (focusX, focusZ);
        // Normal priority, one thread: below normal, a busy machine starved it, and every garbage collection waited on it
        // to reach a safe point (a 138 ms pause in one frame). One thread of sixteen costs the frame nothing.
        _thread = new Thread(Run) { Name = "Place builds", IsBackground = true };
        _thread.Start();
    }

    /// <summary>Where the sleeper (or the camera) is: nearer places are built first.</summary>
    public void Focus(double x, double z)
    {
        lock (_focusLock) (_fx, _fz) = (x, z);
    }

    /// <summary>Wanted now: built next, ahead of everything else.</summary>
    public void Ask(PoiSite site)
    {
        if (site.Built != null) return;
        _asked.Enqueue(site);
        _wake.Set();
    }

    double Away(PoiSite s, double fx, double fz)
    {
        var (cx, cz) = s.Center;
        double dx = Mathx.WrapDelta(cx - fx, _width), dz = Mathx.WrapDelta(cz - fz, _height);
        return Math.Max(0, Math.Sqrt(dx * dx + dz * dz) - Math.Max(s.Stamp.ReachX, s.Stamp.ReachZ));
    }

    void Run()
    {
        while (!_stop)
        {
            PoiSite next = null;
            while (next == null && _asked.TryDequeue(out PoiSite a)) if (a.Built == null && !_failed.Contains(a)) next = a;
            if (next == null && Hold?.Invoke() == true)
            {
                double held = _clock.Elapsed.TotalMilliseconds;
                _wake.WaitOne(50);
                HeldMs += _clock.Elapsed.TotalMilliseconds - held;
                continue;
            }
            if (next == null)
            {
                double fx, fz;
                lock (_focusLock) (fx, fz) = (_fx, _fz);
                double best = Range;
                foreach (PoiSite s in _sites)
                {
                    if (s.Built != null || _failed.Contains(s) || Withhold?.Invoke(s) == true) continue;
                    double d = Away(s, fx, fz);
                    if (d < best) (best, next) = (d, s);
                }
                if (next == null)
                {
                    if (AllInRangeMs < 0) AllInRangeMs = _clock.Elapsed.TotalMilliseconds;
                    _wake.WaitOne(250);
                    continue;
                }
            }
            double began = _clock.Elapsed.TotalMilliseconds;
            try { next.Build(); Built++; BusyMs += _clock.Elapsed.TotalMilliseconds - began; }
            catch (Exception e) { Godot.GD.PushError($"could not build {next.Name}: {e.GetBaseException().Message}"); _failed.Add(next); }
        }
    }

    public void Dispose()
    {
        _stop = true;
        _wake.Set();
    }
}
