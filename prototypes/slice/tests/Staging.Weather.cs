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

    /// <summary>Rain seen from just above the nearest water, or (--indoors=1) from the bunker's room, looking at its stairwell's open top.</summary>
    IEnumerable<int> RainShot()
    {
        _game.Zone.Sky.Paused = true;
        _game.Zone.Sky.TimeOfDay = Num("time", 0.42);
        _game.Zone.Weather.Sim.Snap(Num("heavy", 1) != 0 ? WeatherKind.Monsoon : WeatherKind.Rain);
        _game.Visor.Visible = false;
        _game.Hands.Visible = false;
        if (Num("indoors", 0) != 0)
        {
            Vector3 room = _game.Bunker.RoomCentre + Vector3.Up * 1.5f, way = _game.Bunker.Outside + Vector3.Up * 2f;
            _game.Survivor.PlaceOutside(_game.Bunker.RoomCentre.ToNumerics());
            foreach (int x in _drive.UntilSettled()) yield return x;
            _game.FixedCamera = new Transform3D(Basis.LookingAt(way - room, Vector3.Up), room);
        }
        else if (ClimateTest.DeepWater(_game.Zone, _game.Survivor.WorldFeet, 2f, 4000f) is Vector3 water)
        {
            Vector3 stand = water + new Vector3(0f, 1.1f, 0f);
            _game.Survivor.PlaceOutside((stand + Vector3.Up * 3f).ToNumerics());
            foreach (int x in _drive.UntilSettled()) yield return x;
            Vector3 eye = water + Vector3.Up * 2.4f + Vector3.Right * 3f;
            _game.FixedCamera = new Transform3D(Basis.LookingAt(new Vector3(0.8f, -0.22f, 0.5f).Normalized(), Vector3.Up), eye);
        }
        foreach (int x in _drive.Wait(150)) yield return x;
    }

    /// <summary>Desert wind: a dust storm from a hilltop.</summary>
    IEnumerable<int> DustShot()
    {
        _game.Zone.Sky.Paused = true;
        _game.Zone.Sky.TimeOfDay = Num("time", 0.42);
        _game.Zone.Weather.Sim.Snap(WeatherKind.DustStorm);
        _game.Visor.Visible = false;
        _game.Hands.Visible = false;
        foreach (int x in _drive.UntilSettled()) yield return x;
        Vector3 eye = _game.Survivor.WorldEye() + Vector3.Up * 2f;
        _game.FixedCamera = new Transform3D(Basis.LookingAt(new Vector3(1f, -0.08f, 0.2f).Normalized(), Vector3.Up), eye);
        foreach (int x in _drive.Wait(150)) yield return x;
    }

    /// <summary>A dig's dust: a handful of puffs, seen a moment after.</summary>
    IEnumerable<int> PuffShot()
    {
        _game.Zone.Sky.Paused = true;
        _game.Zone.Sky.TimeOfDay = Num("time", 0.42);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        _game.Visor.Visible = false;
        _game.Hands.Visible = false;
        foreach (int x in _drive.UntilSettled()) yield return x;
        Vector3 at = _game.Zone.GroundAt(_game.Survivor.WorldFeet.X + 5f, _game.Survivor.WorldFeet.Z);
        Vector3 eye = at + new Vector3(-4f, 1.6f, 1.5f);
        _game.FixedCamera = new Transform3D(Basis.LookingAt(at + Vector3.Up * 0.5f - eye, Vector3.Up), eye);
        for (int i = 0; i < 12; i++)
        {
            _game.Zone.Dust.Puff(at + Vector3.Up * 0.3f, new Color(0.55f, 0.5f, 0.42f, 1f), 0.9f);
            foreach (int x in _drive.Wait(4)) yield return x;
        }
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
        Vector3 eye = hill + Vector3.Up * (float)Num("rise", 14);
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
