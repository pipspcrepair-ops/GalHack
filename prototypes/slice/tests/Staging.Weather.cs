using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics.Engine;
using GalHack.Structures;
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

    /// <summary>Rain seen from just above the nearest water, from the bunker's room looking at its stairwell's open top (--indoors=1), from inside a roofed steel room looking out of its doorway (--indoors=2), or from a shut one (--indoors=3).</summary>
    IEnumerable<int> RainShot()
    {
        _game.Zone.Sky.Paused = true;
        _game.Zone.Sky.TimeOfDay = Num("time", 0.42);
        _game.Zone.Weather.Sim.Snap(Num("heavy", 1) != 0 ? WeatherKind.Monsoon : WeatherKind.Rain);
        _game.Visor.Visible = false;
        _game.Hands.Visible = false;
        if (Num("indoors", 0) >= 2)
        {
            bool door = Num("indoors", 0) == 2;
            var text = new System.Text.StringBuilder("schema galhack.blueprint\nversion 1\nname \"Shelter\"\ngrid large\nmode base\ngravity off\n");
            for (int y = 0; y <= 3; y++)
            for (int z = -3; z <= 3; z++)
            for (int x = -3; x <= 3; x++)
            {
                if (y != 0 && y != 3 && Math.Abs(x) != 3 && Math.Abs(z) != 3) continue;
                if (door && z == 3 && Math.Abs(x) <= 1 && (y == 1 || y == 2)) continue;
                text.Append($"block {x} {y} {z} steel\n");
            }
            text.Append("block -2 1 -2 core\n");
            var shelter = Blueprint.Load(text.ToString(), _game.Zone.Data.Catalogue, out List<string> errors);
            if (shelter == null) GD.Print($"shelter blueprint: {string.Join("; ", errors)}");
            Vector3 site = Clearing(_game.Survivor.WorldFeet);
            var room = Slice.Vessels.Vessel.SpawnBase(_game.Zone, shelter, (site + Vector3.Up * 3f).ToNumerics(), 0f);
            Vector3 inside = room.ToWorld(new System.Numerics.Vector3(0f, 2.9f, -1.5f));
            Vector3 outside = room.ToWorld(new System.Numerics.Vector3(0f, 2.4f, 16f));
            // The castaway stands in a back corner, out of the picture.
            _game.Survivor.PlaceOutside(room.ToWorld(new System.Numerics.Vector3(3.5f, 2.9f, -3.5f)).ToNumerics());
            _game.Zone.Light.Touch();
            foreach (int x in _drive.UntilSettled()) yield return x;
            _game.FixedCamera = new Transform3D(Basis.LookingAt(outside - inside, Vector3.Up), inside);
            if (!door) _game.Torch.Visible = true; // or a shut room is only a black picture
        }
        else if (Num("indoors", 0) != 0)
        {
            Vector3 room = _game.Bunker.RoomCentre + Vector3.Up * 1.5f, way = _game.Bunker.Outside + Vector3.Up * 2f;
            _game.Survivor.PlaceOutside(_game.Bunker.RoomCentre.ToNumerics());
            foreach (int x in _drive.UntilSettled()) yield return x;
            _game.FixedCamera = new Transform3D(Basis.LookingAt(way - room, Vector3.Up), room);
        }
        else if (Num("ground", 0) != 0)
        {
            Vector3 open = Clearing(_game.Survivor.WorldFeet);
            _game.Survivor.PlaceOutside((open + Vector3.Up * 0.5f).ToNumerics());
            foreach (int x in _drive.UntilSettled()) yield return x;
            Vector3 at = _game.Zone.GroundAt(open.X + 4f, open.Z + 1f);
            Vector3 eye = _game.Zone.GroundAt(open.X + 1.5f, open.Z - 1f) + Vector3.Up * 1.1f;
            _game.FixedCamera = new Transform3D(Basis.LookingAt(at - eye, Vector3.Up), eye);
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
        GD.Print($"rain shot: eye open {_game.Zone.Light.EyeOpen:F2}, sky at eye {_game.Zone.Weather.SkyAtEye:F2}, drops {_game.Zone.Weather.RainShown}, splashes {_game.Zone.Weather.SplashShown}");
    }

    /// <summary>The nearest dry ground with no tree or place within a dozen metres or so of it.</summary>
    Vector3 Clearing(Vector3 near)
    {
        for (int i = 1; i < 240; i++)
        {
            float r = 18f + i * 3f;
            Vector3 g = _game.Zone.GroundAt(near.X + MathF.Cos(i * 0.39f) * r, near.Z + MathF.Sin(i * 0.39f) * r);
            if (g.Y > 1.5f && !_game.Zone.Water.LiquidAt(g.X, g.Z, out _) && _game.Places?.PlaceAt(g) == null && !_game.Zone.Streamer.SolidsNear(g, 16f).Any())
                return g;
        }
        return near;
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

    /// <summary>A storm cell a kilometre and a half off over open ground: its tower, its shaft, a bolt out of it.</summary>
    IEnumerable<int> StormCellShot()
    {
        var sky = _game.Zone.Weather.Sim;
        _game.Zone.Sky.Paused = true;
        _game.Zone.Sky.TimeOfDay = Num("time", 0.45);
        sky.Snap(_args.TryGetValue("kind", out string kind) ? Enum.Parse<WeatherKind>(kind) : WeatherKind.Thunderstorm);
        _game.Visor.Visible = false;
        _game.Hands.Visible = false;
        Vector3 hill = Hilltop(_game.Survivor.WorldFeet, 300f);
        _game.Survivor.PlaceOutside(hill.ToNumerics());
        foreach (int x in _drive.UntilSettled()) yield return x;
        sky.ClearCells();
        Vector3 toward = new Vector3(1f, 0f, 0.35f).Normalized();
        var cell = sky.AddCell(hill + toward * (float)Num("off", 1500), 1f);
        Vector3 eye = hill + Vector3.Up * 16f;
        _game.FixedCamera = new Transform3D(Basis.LookingAt(toward + Vector3.Up * (float)Num("pitch", 0.12), Vector3.Up), eye);
        foreach (int x in _drive.Wait(90)) yield return x;
        if (Num("bolt", 0) != 0)
        {
            // The next strike out of it, caught as it lands.
            for (int i = 0; i < 1800 && !sky.Events.Any(e => e.Kind == WeatherEventKind.LightningStrike); i++) yield return 0;
            _game.Zone.Weather.StrikeAt(_game.Zone.GroundAt(cell.Position.X - toward.Z * cell.Radius * 0.3f, cell.Position.Z + toward.X * cell.Radius * 0.3f));
            foreach (int x in _drive.Wait(1)) yield return x;
        }
        GD.Print($"storm cell: radius {cell.Radius:F0} m, {sky.Cells.Count} in the sky, rain here {_game.Zone.Weather.RainHere:F2} of {sky.Current.Rain:F2}, under it {sky.RainAt(cell.Position):F2}");
    }

    /// <summary>A clear or clouded sky for the cloud types: --kind=Clear, Clouds, Rain.</summary>
    IEnumerable<int> CloudTypes()
    {
        var sky = _game.Zone.Weather.Sim;
        _game.Zone.Sky.Paused = true;
        _game.Zone.Sky.TimeOfDay = Num("time", 0.42);
        sky.Snap(_args.TryGetValue("kind", out string kind) ? Enum.Parse<WeatherKind>(kind) : WeatherKind.Clouds);
        _game.Visor.Visible = false;
        _game.Hands.Visible = false;
        Vector3 hill = Hilltop(_game.Survivor.WorldFeet, 300f);
        _game.Survivor.PlaceOutside(hill.ToNumerics());
        foreach (int x in _drive.UntilSettled()) yield return x;
        Vector3 eye = hill + Vector3.Up * 16f;
        _game.FixedCamera = new Transform3D(Basis.LookingAt(new Vector3(1f, (float)Num("pitch", 0.35), 0.35f).Normalized(), Vector3.Up), eye);
        foreach (int x in _drive.Wait(60)) yield return x;
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
