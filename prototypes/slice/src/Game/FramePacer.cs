using System;
using Godot;

namespace Slice.Game;

/// <summary>
/// Even frames. With vsync on and no frame cap asked for, a frame is drawn every whole number of the screen's
/// refreshes: every refresh while a frame's work fits in one, else every second, third or fourth (a 240 Hz screen
/// at 240, 120, 80 or 60 frames a second, never under 50). Frames that take one refresh and then two (a 240 Hz
/// screen at 150 a second) stay up unevenly, and the picture judders however smoothly it moves.
/// A frame's work is what the main thread spent on it and what the GPU spent drawing it, without the waits.
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
    double _fitFor;
    ulong _ticks;
    Rid _viewport;

    public override void _Ready()
    {
        ProcessPriority = int.MaxValue; // after everything else this frame's scripts did
        _viewport = GetViewport().GetViewportRid();
        RenderingServer.ViewportSetMeasureRenderTime(_viewport, true);
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
        // This frame's main thread (its ticks, its scripts, the renderer's own CPU) against the GPU's last frame drawn.
        ulong ticks = Engine.GetPhysicsFrames();
        double cpu = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0
                   + Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0 * (ticks - _ticks)
                   + RenderingServer.GetFrameSetupTimeCpu() + RenderingServer.ViewportGetMeasuredRenderTimeCpu(_viewport);
        _ticks = ticks;
        double work = Math.Max(cpu, RenderingServer.ViewportGetMeasuredRenderTimeGpu(_viewport));
        _work[_count++ % _work.Length] = work;
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
