using System;
using System.Diagnostics;
using Godot;

namespace Slice.Game;

/// <summary>
/// Even frames. With vsync on and no frame cap asked for, a frame is drawn every whole number of the screen's
/// refreshes: every refresh while a frame's work fits in one, else every second, third or fourth (a 240 Hz screen
/// at 240, 120, 80 or 60 frames a second, never under 50). Frames that take one refresh and then two (a 240 Hz
/// screen at 150 a second) stay up unevenly, and the picture judders however smoothly it moves.
/// The pace is set by the work every frame must do: the main thread's (a frame with a physics tick in it, once the
/// pace puts one in most frames) and the GPU's, without the waits. What can wait (the streamer's uploads) is given the
/// room left in the frame (<see cref="Room"/>), so streaming neither sets the pace nor falls behind at a slower one.
/// </summary>
public partial class FramePacer : Node
{
    /// <summary>The frame cap the settings ask for (0: none, so paced).</summary>
    public int Asked;

    /// <summary>This frame's main-thread work that can wait for room, ms (the streamer's).</summary>
    public Func<double> Deferred;

    /// <summary>Refreshes a frame stays up while paced (1 when not).</summary>
    public int Refreshes { get; private set; } = 1;

    /// <summary>The work every frame must do (the slowest fifth of the last two seconds' frames), ms.</summary>
    public double Work { get; private set; }

    /// <summary>Room left in a frame for what can wait, ms (<see cref="Unpaced"/> when not paced).</summary>
    public double Room { get; private set; } = Unpaced;

    public const double Unpaced = 3.0, LeastRoom = 1.0, MostRoom = 4.0, Margin = 0.5;

    const int Kept = 480;
    static readonly bool Debugging = OS.HasEnvironment("PACER_DEBUG");
    readonly double[] _cpu = new double[Kept], _gpu = new double[Kept], _sorted = new double[Kept];
    int _frames;
    double _fitFor, _missFor;
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

    /// <summary>The slowest fifth's threshold of the last <paramref name="last"/> of <paramref name="count"/> samples kept in a ring.</summary>
    double P80(double[] ring, int count, int last)
    {
        int n = Math.Min(count, Math.Min(ring.Length, last));
        for (int i = 0; i < n; i++) _sorted[i] = ring[(count - 1 - i) % ring.Length];
        Array.Sort(_sorted, 0, n);
        return n == 0 ? 0 : _sorted[(int)(n * 0.8)];
    }

    public override void _Process(double delta)
    {
        double hz = DisplayServer.ScreenGetRefreshRate(DisplayServer.WindowGetCurrentScreen());
        bool paced = Asked == 0 && hz > 1 && DisplayServer.WindowGetVsyncMode() == DisplayServer.VSyncMode.Enabled;
        if (!paced)
        {
            Refreshes = 1;
            Room = Unpaced;
            Cap(Asked);
            return;
        }
        // The last frame's main thread (up to the draw, then the renderer's own CPU) less what could wait; the GPU's last frame.
        double cpu = _scripts + RenderingServer.GetFrameSetupTimeCpu() + RenderingServer.ViewportGetMeasuredRenderTimeCpu(_viewport);
        _cpu[_frames % Kept] = Math.Max(0, cpu - (Deferred?.Invoke() ?? 0));
        _gpu[_frames % Kept] = RenderingServer.ViewportGetMeasuredRenderTimeGpu(_viewport);
        _frames++;
        if (_frames < 30) return;
        int window = Math.Max(30, (int)(2.0 * hz / Refreshes));
        // The slowest fifth: at any pace of half the ticks' rate or more, the frames with a tick in them.
        double must = P80(_cpu, _frames, window), gpu = P80(_gpu, _frames, window);
        double refresh = 1000.0 / hz;
        bool Fits(int n, double margin) => Math.Max(must + LeastRoom, gpu) <= n * refresh - margin;
        int most = Math.Max(1, (int)(hz / 50.0));
        int need = 1;
        while (need < most && !Fits(need, Margin)) need++;
        // Fewer frames once they have missed for a second (not for a hitch, which the frames queued for the screen take up); more
        // again once they have fitted with room to spare for three seconds: every change of pace is itself a jolt.
        if (need > Refreshes)
        {
            _fitFor = 0;
            _missFor += delta;
            if (_missFor > 1.0) Set(need);
        }
        else if (need < Refreshes && Fits(Refreshes - 1, Margin + 0.5))
        {
            _missFor = 0;
            _fitFor += delta;
            if (_fitFor > 3.0) Set(Refreshes - 1);
        }
        else _fitFor = _missFor = 0;
        Work = Math.Max(must, gpu);
        if (Debugging && _frames % 240 == 0)
            GD.Print($"PACER {Refreshes} refreshes: cpu p80 {must:F1} ms (scripts {_scripts:F1}, setup {RenderingServer.GetFrameSetupTimeCpu():F1}, render {RenderingServer.ViewportGetMeasuredRenderTimeCpu(_viewport):F1}, deferred {Deferred?.Invoke() ?? 0:F1}), gpu p80 {gpu:F1} ms");
        Room = Math.Clamp(Refreshes * refresh - Margin - must, LeastRoom, MostRoom);
        Cap(Refreshes == 1 ? 0 : (int)Math.Ceiling(hz / Refreshes));
    }

    /// <summary>The engine's frame cap, set only when the pace changes it (a test may set its own meanwhile).</summary>
    void Cap(int fps)
    {
        if (fps == _capped) return;
        _capped = fps;
        Engine.MaxFps = fps;
    }

    int _capped = -1;

    void Set(int refreshes)
    {
        Refreshes = refreshes;
        _fitFor = _missFor = 0;
    }
}
