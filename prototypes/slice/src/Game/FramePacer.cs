using System;
using System.Diagnostics;
using Godot;

namespace Slice.Game;

/// <summary>
/// Even frames. With vsync on and no frame cap asked for, a frame is drawn every whole number of the screen's
/// refreshes: every refresh while a frame's work fits in one, else every second, third or fourth (a 240 Hz screen
/// at 240, 120, 80 or 60 frames a second, never under 50). Frames that take one refresh and then two (a 240 Hz
/// screen at 150 a second) stay up unevenly, and the picture judders however smoothly it moves.
/// A frame's work is the more of what the main thread spent on it and what the GPU spent drawing it, without the waits.
/// </summary>
public partial class FramePacer : Node
{
    /// <summary>The frame cap the settings ask for (0: none, so paced).</summary>
    public int Asked;

    /// <summary>Refreshes a frame stays up while paced (1 when not).</summary>
    public int Refreshes { get; private set; } = 1;

    /// <summary>The work of the slowest tenth of recent frames, ms.</summary>
    public double Work { get; private set; }

    readonly double[] _work = new double[120];
    readonly double[] _sorted = new double[120];
    int _count, _since;
    double _fitFor, _last;
    Rid _viewport;

    public override void _Ready()
    {
        _viewport = GetViewport().GetViewportRid();
        RenderingServer.ViewportSetMeasureRenderTime(_viewport, true);
    }

    public override void _EnterTree()
    {
        GetTree().PhysicsFrame += Begin;
        GetTree().ProcessFrame += Begin;
        RenderingServer.FramePreDraw += Drawing;
    }

    public override void _ExitTree()
    {
        GetTree().PhysicsFrame -= Begin;
        GetTree().ProcessFrame -= Begin;
        RenderingServer.FramePreDraw -= Drawing;
    }

    /// <summary>The main thread's time from a frame's first tick or step to its draw (no waiting in it: that comes after the draw), ms.</summary>
    double _scripts;
    long _begun;

    void Begin()
    {
        if (_begun == 0) _begun = Stopwatch.GetTimestamp();
    }

    void Drawing()
    {
        if (_begun != 0) _scripts = (Stopwatch.GetTimestamp() - _begun) * 1000.0 / Stopwatch.Frequency;
        _begun = 0;
    }

    public override void _Process(double delta)
    {
        double hz = DisplayServer.ScreenGetRefreshRate(DisplayServer.WindowGetCurrentScreen());
        bool paced = Asked == 0 && hz > 1 && DisplayServer.WindowGetVsyncMode() == DisplayServer.VSyncMode.Enabled;
        if (!paced)
        {
            Refreshes = 1;
            Engine.MaxFps = Asked;
            return;
        }
        // The last frame's main thread (its ticks and scripts up to the draw, then the renderer's own CPU) against the GPU's last frame drawn.
        double cpu = _scripts + RenderingServer.GetFrameSetupTimeCpu() + RenderingServer.ViewportGetMeasuredRenderTimeCpu(_viewport);
        double work = Math.Max(cpu, RenderingServer.ViewportGetMeasuredRenderTimeGpu(_viewport));
        // Two frames at a time: the frames queued for the screen take up one frame's unevenness (a tick's frame then one without).
        _work[_count++ % _work.Length] = (work + _last) * 0.5;
        _last = work;
        _since++;
        int n = Math.Min(_count, _work.Length);
        if (n < 30) return;
        Array.Copy(_work, _sorted, n);
        Array.Sort(_sorted, 0, n);
        Work = _sorted[(int)(n * 0.9)];
        double refresh = 1000.0 / hz;
        int most = Math.Max(1, (int)(hz / 50.0));
        int need = 1;
        while (need < most && Work > need * refresh - 0.5) need++;
        if (need > Refreshes && _since >= 30) Set(need);
        else if (need < Refreshes && Work < (Refreshes - 1) * refresh - 1.0)
        {
            // Back to more frames only once they have fitted for a while.
            _fitFor += delta;
            if (_fitFor > 2.0) Set(Refreshes - 1);
        }
        else _fitFor = 0;
        Engine.MaxFps = Refreshes == 1 ? 0 : (int)Math.Ceiling(hz / Refreshes);
    }

    void Set(int refreshes)
    {
        Refreshes = refreshes;
        _since = 0;
        _fitFor = 0;
    }
}
