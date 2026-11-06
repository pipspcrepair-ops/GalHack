using System;
using System.Collections.Generic;
using GalHack.Physics.Engine;
using Godot;
using Weather.Model;

namespace Slice.Tests;

/// <summary>
/// Weather and sky views: what a monsoon costs a frame (printed as well as drawn), and a setting
/// sun at the horizon on a real world, looking into it or away from it, with the share of the
/// picture burnt out to white.
/// </summary>
public partial class Staging
{
    /// <summary>Stands in a monsoon and prints the frame's cost, with the rain, over a few hundred drawn frames.</summary>
    IEnumerable<int> MonsoonCost()
    {
        _game.Zone.Sky.Paused = true;
        _game.Zone.Sky.TimeOfDay = Num("time", 0.42);
        _game.Visor.Visible = false;
        _game.Hands.Visible = false;
        _game.Zone.Weather.Sim.Snap(WeatherKind.Monsoon);
        foreach (int x in _drive.UntilSettled()) yield return x;
        Vector3 eye = _game.Survivor.WorldEye();
        Vector3 look = eye + new Vector3(1f, -0.12f, 0.4f).Normalized() * 20f;
        _game.FixedCamera = new Transform3D(Basis.LookingAt(look - eye, Vector3.Up), eye);
        foreach (int x in _drive.Wait(90)) yield return x;
        var cost = new FrameCost(_game);
        foreach (int x in cost.Sample((int)Num("frames", 400))) yield return x;
        GD.Print($"monsoon frame: {cost}");
    }

    /// <summary>Under the nearest deep water, a few metres down, looking up at the surface (--up=0 looks across).</summary>
    IEnumerable<int> UnderwaterShot()
    {
        _game.Zone.Sky.Paused = true;
        _game.Zone.Sky.TimeOfDay = Num("time", 0.42);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        _game.Visor.Visible = _args.ContainsKey("hud");
        _game.Hands.Visible = false;
        if (ClimateTest.DeepWater(_game.Zone, _game.Survivor.WorldFeet, 6f, 4000f) is not Vector3 water)
        {
            GD.Print("no deep water on this world: try another seed");
            yield break;
        }
        _game.Survivor.PlaceOutside((water + Vector3.Up * 1f).ToNumerics());
        foreach (int x in _drive.UntilSettled()) yield return x;
        Vector3 eye = water + Vector3.Down * (float)Num("depth", 3.5);
        Vector3 dir = new Vector3(1f, (float)Num("up", 0.9), 0.3f).Normalized();
        _game.FixedCamera = new Transform3D(Basis.LookingAt(dir, Vector3.Up), eye);
        foreach (int x in _drive.Wait(30)) yield return x;
        GD.Print($"underwater: camera under {_game.Zone.Underwater.CameraUnder}, {_game.Zone.Underwater.Depth:F1} m down");
    }

    /// <summary>A setting (or rising) sun a few degrees over the horizon, from a hilltop: --facing=sun or away, --sunheight, --evening=0 for dawn.</summary>
    IEnumerable<int> Sunset()
    {
        var sky = _game.Zone.Sky;
        sky.Paused = true;
        bool evening = Num("evening", 1) != 0;
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        _game.Visor.Visible = false;
        _game.Hands.Visible = false;
        Vector3 hill = Hilltop(_game.Survivor.WorldFeet, 300f);
        _game.Survivor.PlaceOutside(hill.ToNumerics());
        foreach (int x in _drive.Wait(30)) yield return x;
        foreach (int x in _drive.UntilSettled()) yield return x;
        float height = (float)Num("sunheight", 0.04);
        double time = evening ? 0.75 : 0.25;
        // Down the sun's own path to the height asked for.
        double step = evening ? 0.0005 : -0.0005;
        double best = time;
        for (double t = evening ? 0.55 : 0.45; t > 0.05 && t < 0.95; t += step)
            if (MathF.Abs(sky.SunAt(t).Y - height) < MathF.Abs(sky.SunAt(best).Y - height)) best = t;
        sky.TimeOfDay = best;
        foreach (int x in _drive.Wait(20)) yield return x;
        Vector3 sun = sky.ToSun;
        var along = new Vector3(sun.X, 0f, sun.Z).Normalized();
        bool into = !(_args.TryGetValue("facing", out string f) && f == "away");
        Vector3 eye = hill + Vector3.Up * 1.7f;
        Vector3 dir = (into ? along : -along) + Vector3.Up * (float)Num("pitch", 0.05);
        _game.FixedCamera = new Transform3D(Basis.LookingAt(dir.Normalized(), Vector3.Up), eye);
        int drawn = Engine.GetFramesDrawn();
        while (Engine.GetFramesDrawn() < drawn + 12) yield return 0;
        Image shot = _game.GetViewport().GetTexture().GetImage();
        float[] l = Luminance(shot, out int w, out int h);
        int white = 0, bright = 0;
        foreach (float v in l)
        {
            if (v > 0.96f) white++;
            if (v > 0.8f) bright++;
        }
        GD.Print($"sun at y {sun.Y:F3} (time {best:F4}), {(into ? "looking into it" : "looking away")}, heading {WeatherSim.Bearing(Vector3.Zero, along * (into ? 1f : -1f))}: {100f * white / l.Length:F2}% burnt white, {100f * bright / l.Length:F2}% over 0.8");
    }
}

/// <summary>What frames cost: the engine's measured render time and the game's own, averaged over the frames sampled.</summary>
public sealed class FrameCost
{
    readonly Node _node;
    double _cpu, _gpu, _process, _physics;
    int _frames;

    public FrameCost(Node node) => _node = node;

    public double RenderCpuMs => _cpu / Math.Max(1, _frames);
    public double RenderGpuMs => _gpu / Math.Max(1, _frames);
    public double ProcessMs => _process / Math.Max(1, _frames) * 1000.0;
    public double PhysicsMs => _physics / Math.Max(1, _frames) * 1000.0;

    public IEnumerable<int> Sample(int frames)
    {
        Rid view = _node.GetViewport().GetViewportRid();
        RenderingServer.ViewportSetMeasureRenderTime(view, true);
        int last = Engine.GetFramesDrawn(), seen = 0, skip = 20;
        while (seen < frames)
        {
            yield return 0;
            int now = Engine.GetFramesDrawn();
            if (now == last) continue;
            last = now;
            if (skip-- > 0) continue; // measuring only just began
            _cpu += RenderingServer.ViewportGetMeasuredRenderTimeCpu(view) + RenderingServer.GetFrameSetupTimeCpu();
            _gpu += RenderingServer.ViewportGetMeasuredRenderTimeGpu(view);
            _process += Performance.GetMonitor(Performance.Monitor.TimeProcess);
            _physics += Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess);
            _frames++;
            seen++;
        }
        RenderingServer.ViewportSetMeasureRenderTime(view, false);
    }

    public override string ToString() => $"render cpu {RenderCpuMs:F2} ms, gpu {RenderGpuMs:F2} ms, process {ProcessMs:F2} ms, physics {PhysicsMs:F2} ms over {_frames} frames";
}
