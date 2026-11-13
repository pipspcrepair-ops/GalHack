using GalHack.Physics.Engine;
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PlanetSurface.Poi;
using PoiGenerator.Gen;
using Slice.Game;
using Slice.Places;
using Slice.Surface;
using WeatherKind = Weather.Model.WeatherKind;
using TInt3 = TerrainDigging.Terrain.Int3;

namespace Slice.Tests;

/// <summary>
/// The dark proof: one run on one world takes every shot, in the game itself, at the hour the local
/// sun stands highest. A cave walked into from its mouth, torch off and on, the view back out, a
/// sealed room dark and then with a lamp, a dug tunnel, and the start bunker. --stem=PATH names the
/// files; each shot prints how bright it came out, so "dark" is a number as well as a picture.
/// </summary>
public sealed partial class Staging
{
    void DarkShot(string name)
    {
        string stem = _args.GetValueOrDefault("stem", "dark");
        string path = $"{stem}-{name}.png";
        Image image = _game.GetViewport().GetTexture().GetImage();
        Error e = image.SavePng(path);
        // The middle of the picture: the HUD is round its edges.
        float total = 0f, top = 0f;
        int n = 0, w = image.GetWidth(), h = image.GetHeight();
        for (int y = h / 4; y < h * 3 / 4; y += 6)
        for (int x = w / 4; x < w * 3 / 4; x += 6)
        {
            float v = image.GetPixel(x, y).Luminance;
            total += v;
            top = Mathf.Max(top, v);
            n++;
        }
        var l = _game.Zone.Light;
        GD.Print($"DARK {name}: {e}, mean {total / n:F4}, brightest {top:F3}, eye open {l.EyeOpen:F2}, light here {l.LightAt(_game.Camera.GlobalPosition):F3}, flood {l.LastFloodMs:F1} ms, upload {l.LastUploadMs:F2} ms, gpu {_gpuTotal / Mathf.Max(1, _gpuFrames):F2} ms, sun {_game.Zone.Sky.ToSun.Y:F2}");
    }

    /// <summary>The hour the sun stands highest (or lowest) over where you are.</summary>
    public static double HighSun(Game.Game game, bool highest = true)
    {
        var sky = game.Zone.Sky;
        return Enumerable.Range(0, 48).Select(i => i / 48.0).OrderBy(t => (highest ? -1f : 1f) * sky.SunAt(t).Y).First();
    }

    float _gpuTotal;
    int _gpuFrames;

    /// <summary>The GPU's time per frame, ms, averaged over a wait.</summary>
    IEnumerable<int> MeasureGpu(int ticks)
    {
        Rid view = _game.GetViewport().GetViewportRid();
        RenderingServer.ViewportSetMeasureRenderTime(view, true);
        _gpuTotal = 0f;
        _gpuFrames = 0;
        for (int i = 0; i < ticks; i++)
        {
            yield return 0;
            if (i < 10) continue; // the measuring takes a frame or two to start
            _gpuTotal += (float)RenderingServer.ViewportGetMeasuredRenderTimeGpu(view);
            _gpuFrames++;
        }
    }

    IEnumerable<int> DarkAt(Vector3 eye, Vector3 look)
    {
        _game.Survivor.PlaceOutside((eye + Vector3.Down * 1.5f).ToNumerics());
        _game.FixedCamera = new Transform3D(Basis.LookingAt(look - eye, Mathf.Abs((look - eye).Normalized().Y) > 0.95f ? Vector3.Forward : Vector3.Up), eye);
        foreach (int x in _drive.UntilSettled()) yield return x;
        foreach (int x in MeasureGpu(60)) yield return x;
    }

    /// <summary>A cave mouth near the start, by its passage's length, or null on a world with none near.</summary>
    public static (Vector3 Mouth, Vector3[] Line)? NearestCave(Game.Game game)
    {
        var planet = game.Zone.Planet;
        Vector3 from = game.Survivor.WorldFeet;
        var best = planet.Terrain.Source.CaveMouths(0, 0, planet.Width, planet.Height)
            .Where(m => m.Path != null && m.Path.Length >= 4)
            .OrderBy(m => planet.NearestCopy(new Vector3((float)m.X, 0f, (float)m.Z), from).DistanceTo(new Vector3(from.X, 0f, from.Z)))
            .FirstOrDefault();
        if (best == null) return null;
        Vector3 mouth = planet.NearestCopy(new Vector3((float)best.X, best.Y, (float)best.Z), from);
        if (mouth.DistanceTo(new Vector3(from.X, mouth.Y, from.Z)) > 2500f) return null; // a long way off: a walk, not a look
        return (mouth, best.Path.Select(p => mouth + new Vector3(p.X, p.Y, p.Z)).ToArray());
    }

    IEnumerable<int> Dark()
    {
        var zone = _game.Zone;
        zone.Weather.Sim.Snap(WeatherKind.Clear);
        zone.Sky.Paused = true;
        GD.Print($"DARK world {_game.World}");
        zone.Sky.TimeOfDay = HighSun(_game);
        string only = _args.GetValueOrDefault("only", "");

        if (only is "" or "cave" && NearestCave(_game) is { } cave)
        {
            Vector3[] line = cave.Line;
            Vector3 way = (line[0] - line[^1]) with { Y = 0f };
            way = way.Normalized();
            Vector3 Eye(int i) => line[i] + Vector3.Up * 1.2f;
            Vector3 outside = cave.Mouth + way * 9f;
            outside = outside with { Y = zone.GroundAt(outside.X, outside.Z).Y + 1.7f };
            foreach (int x in DarkAt(outside, Eye(line.Length / 3))) yield return x;
            zone.Sky.TimeOfDay = HighSun(_game);
            foreach (int x in _drive.Wait(30)) yield return x;
            DarkShot("cave-from-outside");
            int steps = line.Length - 1;
            foreach (int x in DarkAt(Eye(0), Eye(Math.Min(steps, 3)))) yield return x;
            DarkShot("cave-in-1");
            foreach (int x in DarkAt(Eye(steps / 3), Eye(Math.Min(steps, steps / 3 + 3)))) yield return x;
            DarkShot("cave-in-2");
            foreach (int x in DarkAt(Eye(steps * 2 / 3), Eye(Math.Min(steps, steps * 2 / 3 + 3)))) yield return x;
            DarkShot("cave-in-3");
            foreach (int x in DarkAt(Eye(steps - 1), Eye(steps))) yield return x;
            DarkShot("cave-deep-torch-off");
            _game.Torch.Visible = true;
            foreach (int x in _drive.Wait(10)) yield return x;
            DarkShot("cave-deep-torch-on");
            _game.Torch.Visible = false;
            foreach (int x in DarkAt(Eye(steps / 3), Eye(0))) yield return x;
            DarkShot("cave-looking-out");
            zone.Sky.TimeOfDay = HighSun(_game, highest: false);
            foreach (int x in _drive.Wait(30)) yield return x;
            foreach (int x in DarkAt(Eye(steps - 1), Eye(steps))) yield return x;
            DarkShot("cave-deep-midnight");
            zone.Sky.TimeOfDay = HighSun(_game);
        }
        else GD.Print("DARK no cave mouth on this world");

        // A sealed room in the nearest place that has one.
        _game.FixedCamera = null;
        foreach (PoiSite site in zone.Planet.Pois.Where(s => s != _game.Bunker.Poi && s.Instance.Blocks.Min.Y < -4).OrderBy(s => _game.Places.DistanceTo(s, _game.Survivor.WorldFeet)).Take(6))
        {
            foreach (int x in GoTo(site, Gate(site) + Vector3.Up * 0.1f)) yield return x;
            foreach (int x in _drive.Wait(30)) yield return x;
            Place p = _game.Places.PlaceOf(site);
            if (p == null) continue;
            // The field reaches 32 m up and down: look for it from the bottom of the place.
            Int3 deepest = p.Nav().Reachable(p.Inst.Layout.EntranceCell).OrderBy(c => c.Y).First();
            _game.Survivor.PlaceOutside(p.WorldOf(deepest, 0.2f).ToNumerics());
            foreach (int x in _drive.Wait(60)) yield return x;
            var cells = p.Nav().Reachable(p.Inst.Layout.EntranceCell).Select(c => (Cell: c, World: p.WorldOf(c, 1.6f)))
                .Where(c => zone.Light.Field != null && zone.Light.Field.Holds(Mathf.FloorToInt(c.World.X), Mathf.FloorToInt(c.World.Y), Mathf.FloorToInt(c.World.Z)))
                .ToList();
            var lit = cells.Where(c => zone.Light.Openness(c.World) > 0f).Select(c => c.World).ToList();
            cells = cells.Where(c => zone.Light.LightAt(c.World) < 0.002f && lit.All(l => l.DistanceSquaredTo(c.World) > 12f * 12f)).ToList();
            GD.Print($"DARK {site.Family.Id} ({site.Instance.State}) has {cells.Count} dark cells in reach of the field");
            if (cells.Count == 0) continue;
            var (cell, eye) = cells.OrderByDescending(c => FreeRun(p, c.Cell)).First();
            Vector3 dir = OpenWay(p, cell);
            GD.Print($"DARK sealed room in {site.Family.Id}, {cells.Count} dark cells");
            foreach (int x in DarkAt(eye, eye + dir * 4f)) yield return x;
            DarkShot("room-sealed-noon");
            var lamp = new OmniLight3D { Name = "ProofLamp", LightColor = new Color(1f, 0.85f, 0.65f), LightEnergy = 2.5f, OmniRange = 9f, ShadowEnabled = true };
            p.AddChild(lamp);
            lamp.GlobalPosition = eye + dir * 2.2f + Vector3.Up * 0.6f;
            foreach (int x in _drive.Wait(10)) yield return x;
            DarkShot("room-sealed-lamp");
            lamp.QueueFree();
            break;
        }

        if (only == "room") yield break;
        Vector3 open = _game.Bunker.Ahead(10f);
        open.Y = zone.GroundAt(open.X, open.Z).Y + 1.7f;
        foreach (int x in DarkAt(open, _game.Bunker.Ahead(90f) + Vector3.Up * 2f)) yield return x;
        DarkShot("open-noon");
        zone.Sky.TimeOfDay = HighSun(_game, highest: false);
        foreach (int x in DarkAt(open, _game.Bunker.Ahead(90f) + Vector3.Up * 2f)) yield return x;
        DarkShot("open-midnight");
        zone.Sky.TimeOfDay = HighSun(_game);

        _game.FixedCamera = null;

        // A tunnel dug into the ground, slanting down from where the land is level, looking along it.
        Vector3 start = _game.Bunker.Ahead(14f);
        start.Y = zone.GroundAt(start.X, start.Z).Y;
        Vector3 down = (_game.Bunker.Ahead(15f) - _game.Bunker.Ahead(14f)).Normalized() + Vector3.Down * 0.55f;
        down = down.Normalized();
        foreach (int x in DarkAt(start + Vector3.Up * 1.7f, start + down * 6f)) yield return x;
        for (int i = 0; i < 26; i++)
        {
            Vector3 c = start + down * (i * 0.9f);
            zone.Edits.Dig(new TInt3(Mathf.FloorToInt(c.X), Mathf.FloorToInt(c.Y), Mathf.FloorToInt(c.Z)), 2f);
            yield return 0;
        }
        foreach (int x in _drive.Wait(120)) yield return x;
        foreach (int x in DarkAt(start - down * 3f + Vector3.Up * 2.2f, start + down * 8f)) yield return x;
        DarkShot("tunnel-from-top");
        foreach (int x in DarkAt(start + down * 9f + Vector3.Down * 0.2f, start + down * 20f)) yield return x;
        DarkShot("tunnel-part-way");
        foreach (int x in DarkAt(start + down * 21f, start + down * 26f)) yield return x;
        DarkShot("tunnel-bottom");
        foreach (int x in DarkAt(start + down * 21f, start)) yield return x;
        DarkShot("tunnel-looking-up");

        // A vessel's own dark, with the field off as it is in orbit: the same sealed box, lit by its own bake or not at all.
        Vector3 shell = _game.Bunker.Ahead(34f);
        shell.Y = zone.GroundAt(shell.X, shell.Z).Y + 8f;
        var plan = new System.Text.StringBuilder("schema galhack.blueprint\nversion 1\nname \"Shell\"\ngrid 1m\nmode base\ngravity off\n");
        for (int y = 0; y <= 4; y++)
        for (int z = -3; z <= 3; z++)
        for (int bx = -3; bx <= 3; bx++)
            if (y == 0 || y == 4 || Math.Abs(bx) == 3 || Math.Abs(z) == 3) plan.Append($"block {bx} {y} {z} {(y == 2 && z == -3 && bx == 0 ? "glass-plated" : "iron-plated")}\n");
        plan.Append("block 2 1 2 core\n");
        var built = GalHack.Structures.Blueprint.Load(plan.ToString(), zone.Data.Catalogue, out List<string> planErrors);
        if (built == null) GD.Print($"DARK shell: {string.Join("; ", planErrors)}");
        else
        {
            var box = Slice.Vessels.Vessel.SpawnBase(zone, built, shell.ToNumerics(), 0f);
            Vector3 inside = box.ToWorld(new System.Numerics.Vector3(0f, 1.6f, 1.5f));
            foreach (int x in DarkAt(inside, box.ToWorld(new System.Numerics.Vector3(0f, 2f, -3f)))) yield return x;
            zone.Light.Active = false;
            foreach (int x in _drive.Wait(30)) yield return x;
            DarkShot("vessel-own-dark-field-off");
            zone.Light.Active = true;
            zone.Light.Touch();
            foreach (int x in _drive.Wait(30)) yield return x;
            DarkShot("vessel-own-dark-field-on");
        }
        _game.FixedCamera = null;

        // The start.
        Vector3 room = _game.Bunker.RoomCentre + Vector3.Up * 1.55f;
        foreach (int x in DarkAt(room, _game.Bunker.CryoStand + Vector3.Up * 1.2f)) yield return x;
        zone.Sky.TimeOfDay = HighSun(_game);
        foreach (int x in _drive.Wait(90)) yield return x;
        GD.Print($"DARK bunker lights: {string.Join(", ", SkyLit.Lights.Where(l => l.GlobalPosition.DistanceTo(room) < 12f).Select(l => $"{l.Name} {l.Visible} {l.LightEnergy:F1}"))}");
        DarkShot("bunker");
        foreach (int x in DarkAt(room, _game.Bunker.Door + Vector3.Up * 1.2f)) yield return x;
        DarkShot("bunker-to-door");
    }

    /// <summary>How far you can see from a cell, in the widest of the four ways.</summary>
    static int FreeRun(Place p, Int3 c) => new[] { new Int3(1, 0, 0), new Int3(-1, 0, 0), new Int3(0, 0, 1), new Int3(0, 0, -1) }.Max(d => Run(p, c, d));

    static int Run(Place p, Int3 c, Int3 d)
    {
        int n = 0;
        while (n < 24 && !BlockInfo.Collides(p.Inst.Blocks.Get(c + Int3.Up + d * (n + 1)).Type)) n++;
        return n;
    }

    static Vector3 OpenWay(Place p, Int3 c)
    {
        Int3 d = new[] { new Int3(1, 0, 0), new Int3(-1, 0, 0), new Int3(0, 0, 1), new Int3(0, 0, -1) }.OrderByDescending(v => Run(p, c, v)).First();
        return new Vector3(d.X, 0f, d.Z);
    }
}
