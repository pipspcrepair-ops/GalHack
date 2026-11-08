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
    /// <summary>A tornado under its storm tower: --dist metres away, looking up at its top at the cloud base (the shot is taken while it is forming and lowers).</summary>
    IEnumerable<int> FunnelShot()
    {
        _game.Zone.Sky.Paused = true;
        _game.Zone.Sky.TimeOfDay = Num("time", 0.42);
        _game.Visor.Visible = false;
        _game.Hands.Visible = false;
        var sim = _game.Zone.Weather.Sim;
        sim.Snap(Enum.Parse<WeatherKind>(_args.GetValueOrDefault("weather", "Tornado")));
        foreach (int x in _drive.UntilSettled()) yield return x;
        Vector3 eye = Clearing(_game.Survivor.WorldFeet);
        // Never inside terrain or a hull: at least 1.2 m clear of the ground, with a note when it had to move.
        float groundY = _game.Zone.GroundAt(eye.X, eye.Z).Y;
        eye.Y = MathF.Max(eye.Y, groundY) + (float)Num("up", 1.6);
        GD.Print($"(funnel camera stands at {eye}, {eye.Y - groundY:F1} m over the ground, moved out of the start bunker)");
        float dist = (float)Num("dist", 500);
        sim.ClearTornadoes();
        Tornado t = sim.SpawnTornado(eye + new Vector3(dist, 0f, 0f), eye);
        t.Homing = false;
        Hurry(6);
        string stage = _args.GetValueOrDefault("stage", "mature");
        for (int i = 0; i < 60 * 60 && (stage == "forming" ? t.Formation < 0.55f : !t.Touchdown); i++) yield return 0; // until it is down
        if (stage == "decay")
        {
            t.Dissipate();
            for (int i = 0; i < 60 * 60 && t.Strength > 0.45f; i++) yield return 0;
        }
        Hurry(1);
        GD.Print($"(funnel touchdown {t.Touchdown}, formation {t.Formation:F2})");
        foreach (int x in _drive.Wait(60)) yield return x;
        float pitch = (float)Num("lookup", 0.35);
        Vector3 look = new Vector3(1f, MathF.Tan(pitch) , 0f).Normalized();
        _game.FixedCamera = new Transform3D(Basis.LookingAt(look, Vector3.Up), eye);
        foreach (int x in _drive.Wait(60)) yield return x;
        if (Num("hide", 0) == 1) _game.Zone.Sky.Visible = false;
        if (Num("hide", 0) == 2) _game.Zone.Weather.Visible = false;
        if (Num("hide", 0) == 3) foreach (Node n in _game.Zone.Sky.GetChildren()) GD.Print($"SKYKID {n.Name} {n.GetType().Name} {(n as Node3D)?.Visible}");
        foreach (int x in _drive.Wait(5)) yield return x;
        GD.Print($"funnel at {t.Position}, formation {t.Formation:F2}");
        foreach (Tornado o in sim.Tornadoes) GD.Print($"(tornado {o.Id} {new Vector2(o.Position.X - eye.X, o.Position.Z - eye.Z).Length():F0} m from the camera, touchdown {o.Touchdown})");
        foreach (Node n in _game.GetTree().Root.FindChildren("*", "MeshInstance3D", true, false))
            if (n is MeshInstance3D m && m.IsVisibleInTree() && m.Mesh is BoxMesh or CylinderMesh && m.GetAabb().Size.Length() > 2f)
                GD.Print($"BOX {m.GetPath()} {m.Mesh.GetType().Name} rel {m.GlobalPosition - eye} size {m.GetAabb().Size * m.GlobalTransform.Basis.Scale}");
    }

    /// <summary>A storm cell from outside (--under=1: from right under it), looking at the strongest one. --weather picks the strength.</summary>
    IEnumerable<int> StormShot()
    {
        _game.Zone.Sky.Paused = true;
        _game.Zone.Sky.TimeOfDay = Num("time", 0.5);
        _game.Visor.Visible = false;
        _game.Hands.Visible = false;
        var sim = _game.Zone.Weather.Sim;
        sim.Snap(Enum.Parse<WeatherKind>(_args.GetValueOrDefault("weather", "Thunderstorm")));
        _game.Zone.Weather.Sim.ClearTornadoes();
        Hurry(6);
        for (int i = 0; i < 60 * 25; i++) yield return 0; // the cells grow
        Hurry(1);
        foreach (int x in _drive.UntilSettled()) yield return x;
        Vector3 start = Clearing(_game.Survivor.WorldFeet);
        StormCell cell = sim.Cells.OrderByDescending(c => c.Strength).FirstOrDefault();
        if (cell == null) { GD.Print("(no cell)"); yield break; }
        float far = Num("under", 0) == 1 ? 0f : (float)Num("dist", 3000);
        Vector3 toward = new Vector3(cell.Position.X - start.X, 0f, cell.Position.Z - start.Z);
        float away = toward.Length();
        Vector3 dir = toward.Normalized().Rotated(Vector3.Up, Mathf.DegToRad((float)Num("bearing", 0)));
        Vector3 eye = Num("under", 0) == 1 ? new Vector3(cell.Position.X, 0f, cell.Position.Z) + dir * 150f : new Vector3(cell.Position.X, 0f, cell.Position.Z) - dir * far;
        float groundY = _game.Zone.GroundAt(eye.X, eye.Z).Y;
        eye.Y = MathF.Max(MathF.Max(groundY, start.Y + 150f), 0f) + (float)Num("up", 40); // well clear of any hill
        GD.Print($"(storm: strength {cell.Strength:F2}, radius {cell.Radius:F0}, camera {(new Vector2(eye.X - cell.Position.X, eye.Z - cell.Position.Z)).Length():F0} m from it, {eye.Y - groundY:F0} m over the ground; start {away:F0} m from it)");
        Vector3 look = (dir * MathF.Cos((float)Num("lookup", 0.15)) + Vector3.Up * MathF.Sin((float)Num("lookup", 0.15)));
        _game.FixedCamera = new Transform3D(Basis.LookingAt(look, Vector3.Up), eye);
        foreach (int x in _drive.Wait(90)) yield return x;
    }

    /// <summary>
    /// A storm front coming in, from open ground --dist metres ahead of its edge, looking up the wind at its
    /// shelf. --sunback=1 brings it from the side away from the sun (a low sun at your back lights the land
    /// against it: --time picks the hour); --bolt=1 catches a strike out from under the shelf; --roll=0/1 forces its roll cloud.
    /// </summary>
    IEnumerable<int> FrontShot()
    {
        var sky = _game.Zone.Sky;
        sky.Paused = true;
        sky.TimeOfDay = Num("time", 0.42);
        _game.Visor.Visible = false;
        _game.Hands.Visible = false;
        var sim = _game.Zone.Weather.Sim;
        sim.Snap(WeatherKind.Thunderstorm);
        sim.ClearTornadoes();
        foreach (int x in _drive.UntilSettled()) yield return x;
        if (_args.ContainsKey("sunheight"))
        {
            // The morning hour with the sun this high: a low sun to light the land against the front.
            double best = 0.5, want = Num("sunheight", 0.12);
            for (double t = 0.5; t > 0.1; t -= 0.0005)
                if (Math.Abs(sky.SunAt(t).Y - want) < Math.Abs(sky.SunAt(best).Y - want)) best = t;
            sky.TimeOfDay = best;
            foreach (int x in _drive.Wait(5)) yield return x;
        }
        Vector2 wind = sim.Current.Wind;
        if (Num("sunback", 0) != 0) { Vector3 s = sky.ToSun; wind = new Vector2(s.X, s.Z).Normalized(); }
        Vector2 way = Front.Lattice(wind, sim.WorldSize, 5500f).heading;
        Vector3 toward = new Vector3(-way.X, 0f, -way.Y).Rotated(Vector3.Up, Mathf.DegToRad((float)Num("bearing", 0)));
        float up = (float)Num("up", 24); // over the trees
        Vector3 eye = Lookout(_game.Survivor.WorldFeet, toward, up);
        // The castaway stands there too: the weather happens around them, so the front's cells line up along their stretch of it.
        _game.Survivor.PlaceOutside((eye + Vector3.Up).ToNumerics());
        foreach (int x in _drive.UntilSettled()) yield return x;
        eye.Y = _game.Zone.GroundAt(eye.X, eye.Z).Y + up;
        bool? roll = Num("roll", -1) < 0 ? null : Num("roll", 0) != 0;
        Front f = sim.StartFront(FrontKind.Storm, eye, (float)Num("dist", 3000), wind, roll);
        float pitch = (float)Num("lookup", 0.1);
        _game.FixedCamera = new Transform3D(Basis.LookingAt(toward * MathF.Cos(pitch) + Vector3.Up * MathF.Sin(pitch), Vector3.Up), eye);
        float before = f.Ahead(eye);
        double started = sim.Time;
        _game.Zone.Sky.DebugView((int)Num("debug", 0));
        foreach (int x in _drive.Wait(90)) yield return x;
        if (Num("bolt", 0) != 0)
        {
            Vector3 under = f.EdgeAt(f.Along(eye) + (float)Num("boltside", 300), eye) - new Vector3(f.Heading.X, 0f, f.Heading.Y) * 450f;
            _game.Zone.Weather.StrikeAt(_game.Zone.GroundAt(under.X, under.Z));
            foreach (int x in _drive.Wait(2)) yield return x;
        }
        float moved = before - f.Ahead(eye), seconds = (float)(sim.Time - started);
        GD.Print($"front: {f.Ahead(eye):F0} m ahead of its edge, {f.Period / 1000f:F0} km long, copies {f.Spacing / 1000f:F1} km apart, heading {f.Heading} against wind {sim.Current.Wind.Normalized()}, roll cloud {f.Roll}; came {moved:F0} m in {seconds:F1} s ({moved / MathF.Max(seconds, 0.01f):F1} m/s, wind {sim.Current.WindSpeed:F1} m/s); " +
            $"{sim.Cells.Count(c => c.OnFront)} cells on it, storm over the camera {sim.CellCover(eye):F2}, rain here {_game.Zone.Weather.RainHere:F2}, sun y {sky.ToSun.Y:F2}");
    }

    /// <summary>
    /// Waterspouts over the nearest open water, from its shore: a fair day's under growing cumulus, or
    /// (--storm=1) a storm cell's. --wait is how long they grow first, seconds.
    /// </summary>
    IEnumerable<int> SpoutShot()
    {
        var sky = _game.Zone.Sky;
        sky.Paused = true;
        sky.TimeOfDay = Num("time", 0.42);
        _game.Visor.Visible = false;
        _game.Hands.Visible = false;
        var sim = _game.Zone.Weather.Sim;
        bool storm = Num("storm", 0) != 0;
        sim.FrontsOff = true;
        sim.ClearFront();
        sim.Snap(storm ? WeatherKind.Thunderstorm : WeatherKind.Clouds);
        sim.ClearTornadoes();
        sim.ClearCells();
        foreach (int x in _drive.UntilSettled()) yield return x;
        // A wide stretch of open water, and a place out on it 650 m from where they form: the camera floats there over the water.
        Vector3 water = Vector3.Zero, shore = Vector3.Zero;
        bool found = false;
        bool Open(Vector3 p) => sim.OpenWater != null && sim.OpenWater(p.X, p.Z);
        for (int i = 0; i < 600 && !found; i++)
        {
            float r = 150f + i * 15f;
            Vector3 w = _game.Survivor.WorldFeet + new Vector3(MathF.Cos(i * 0.61f) * r, 0f, MathF.Sin(i * 0.61f) * r);
            if (!Open(w)) continue;
            bool wide = true;
            for (int k = 0; k < 8 && wide; k++) wide = Open(w + new Vector3(MathF.Cos(k * 0.785f), 0f, MathF.Sin(k * 0.785f)) * 450f);
            if (!wide) continue;
            for (int k = 0; k < 12 && !found; k++)
            {
                Vector3 c = w + new Vector3(MathF.Cos(k * 0.52f), 0f, MathF.Sin(k * 0.52f)) * 650f;
                if (Open(c)) (water, shore, found) = (w, c, true);
            }
        }
        if (!found) { GD.Print("spout: no wide open water near the start: try another world"); yield break; }
        Vector3 land = Clearing(shore);
        _game.Survivor.PlaceOutside((land + Vector3.Up).ToNumerics());
        foreach (int x in _drive.UntilSettled()) yield return x;
        if (storm) sim.AddCell(water, 1f);
        IReadOnlyList<Tornado> spouts = sim.SpawnSpouts(water, storm);
        Hurry(6);
        float wait = (float)Num("wait", 40);
        for (int i = 0; i < 60 * 60 && spouts[0].Age < wait && sim.Tornadoes.Contains(spouts[0]); i++) yield return 0;
        Hurry(1);
        Vector3 eye = new Vector3(shore.X, sim.GroundHeight(shore.X, shore.Z) + (float)Num("up", 5), shore.Z);
        Vector3 to = water - eye;
        to.Y = 0f;
        float pitch = (float)Num("lookup", 0.22);
        _game.FixedCamera = new Transform3D(Basis.LookingAt(to.Normalized() * MathF.Cos(pitch) + Vector3.Up * MathF.Sin(pitch), Vector3.Up), eye);
        foreach (int x in _drive.Wait(60)) yield return x;
        foreach (Tornado t in spouts)
            GD.Print($"spout {t.Id}: {t.Kind}, age {t.Age:F0} s, reaches {t.Formation:F2} of the way down (stops at {t.Reach:F2}), touchdown {t.Touchdown}, " +
                $"{new Vector2(t.Position.X - eye.X, t.Position.Z - eye.Z).Length():F0} m off, over open water {sim.OpenWater(t.Position.X, t.Position.Z)}");
    }

    /// <summary>Prints the cirrus and stratus weights over 40 rolled world and weather pairs.</summary>
    IEnumerable<int> CirrusLog()
    {
        var rng = new Random();
        WeatherData data = _game.Zone.Data.Weather;
        string[] ids = { "temperate", "arid", "ice", "storm", "swamp", "ocean", "lava" };
        var kinds = new[] { WeatherKind.Clear, WeatherKind.Clear, WeatherKind.Clear, WeatherKind.Clouds, WeatherKind.Clouds, WeatherKind.Clouds, WeatherKind.Clouds, WeatherKind.Rain, WeatherKind.Rain, WeatherKind.Rain, WeatherKind.Fog, WeatherKind.Snow, WeatherKind.Thunderstorm, WeatherKind.DustStorm, WeatherKind.Monsoon };
        var cir = new List<float>();
        for (int i = 0; i < 40; i++)
        {
            var arch = (Planets.Gen.Archetype)rng.Next(5);
            var sky = PlanetSurface.Skies.SkySystem.For(Planets.Gen.PlanetSpec.Roll((ulong)rng.Next(), arch, 0));
            var sim = new WeatherSim(data, ids[rng.Next(ids.Length)], (ulong)rng.Next());
            WeatherKind k = kinds[rng.Next(kinds.Length)];
            sim.Snap(k);
            var a = PlanetSurface.Skies.Atmosphere.Compute(sky, sim.Current, 0.8f);
            cir.Add(a.Cirrus);
            GD.Print($"CIRRUS {i,2} {arch,-9} {sky.Air,-10} {k,-12} wind {sim.Current.WindSpeed,4:F1} temp {sim.Current.Temperature,5:F1} cover {a.CloudCover:F2} cirrus {a.Cirrus:F2} stratus {a.Stratus:F2}");
        }
        cir.Sort();
        GD.Print($"CIRRUS spread: <0.1 {cir.Count(c => c < 0.1f)}, 0.1-0.3 {cir.Count(c => c >= 0.1f && c < 0.3f)}, 0.3-0.6 {cir.Count(c => c >= 0.3f && c < 0.6f)}, >=0.6 {cir.Count(c => c >= 0.6f)}; median {cir[20]:F2}");
        yield return 0;
    }

    /// <summary>The sky at ground level: --weather, level (--lookup 0) and 30 degrees up, for the cloud ceiling.</summary>
    IEnumerable<int> SkyLevel()
    {
        _game.Zone.Sky.Paused = true;
        _game.Zone.Sky.TimeOfDay = Num("time", 0.5);
        _game.Visor.Visible = false;
        _game.Hands.Visible = false;
        _game.Zone.Weather.Sim.Snap(Enum.Parse<WeatherKind>(_args.GetValueOrDefault("weather", "Clouds")));
        foreach (int x in _drive.UntilSettled()) yield return x;
        Vector3 eye = Clearing(_game.Survivor.WorldFeet);
        float groundY = _game.Zone.GroundAt(eye.X, eye.Z).Y;
        eye.Y = MathF.Max(eye.Y, groundY) + (float)Num("up", 1.6);
        GD.Print($"(sky camera {eye.Y - groundY:F1} m over the ground, moved out of the start bunker)");
        foreach (float deg in new[] { 0f, 30f })
        {
            Vector3 look = new Vector3(1f, MathF.Tan(Mathf.DegToRad(deg)), 0f).Normalized();
            _game.FixedCamera = new Transform3D(Basis.LookingAt(look, Vector3.Up), eye);
            foreach (int x in _drive.Wait(40)) yield return x;
            Snap($"up{deg:0}");
        }
        // Horizon check: no hard horizontal cloud edge, nor a bright gap under a sheet, in the sky just above the land.
        Vector3 level = new Vector3(1f, MathF.Tan(Mathf.DegToRad(6f)), 0f).Normalized();
        foreach (float yaw in new[] { 0f, 90f, 180f, 270f })
        {
            Vector3 dir = level.Rotated(Vector3.Up, Mathf.DegToRad(yaw));
            _game.FixedCamera = new Transform3D(Basis.LookingAt(dir, Vector3.Up), eye);
            foreach (int x in _drive.Wait(30)) yield return x;
            Image img = _game.GetViewport().GetTexture().GetImage();
            int w = img.GetWidth(), h = img.GetHeight(), r0 = (int)(h * 0.12f), r1 = (int)(h * 0.5f);
            float worst = 0f; int worstRow = 0;
            float Row(int y) { float s = 0; for (int xx = 0; xx < w; xx += 8) { Color c = img.GetPixel(xx, y); s += 0.3f * c.R + 0.59f * c.G + 0.11f * c.B; } return s / (w / 8); }
            float prev = Row(r0);
            for (int y = r0 + 2; y < r1; y += 2) { float cur = Row(y); if (MathF.Abs(cur - prev) > worst) { worst = MathF.Abs(cur - prev); worstRow = y; } prev = cur; }
            GD.Print($"HORIZON CHECK yaw {yaw:0}: biggest step between sky rows {worst:F3} at row {worstRow} of {h} {(worst > 0.04f ? "FAIL" : "ok")}");
        }
    }

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
            Vector3 eye = water + Vector3.Up * (float)Num("eyeh", 2.4) + Vector3.Right * 3f;
            _game.FixedCamera = new Transform3D(Basis.LookingAt(new Vector3(0.8f, (float)Num("pitch", -0.22), 0.5f).Normalized(), Vector3.Up), eye);
        }
        foreach (int x in _drive.Wait(150)) yield return x;
        if (_args.TryGetValue("burst", out string burst))
            for (int i = 0; i < int.Parse(burst); i++)
            {
                foreach (int x in Shot(_args["out"].Replace(".png", $"-{i:D2}.png"))) yield return x;
                foreach (int x in _drive.Wait(4)) yield return x;
            }
        GD.Print($"rain shot: eye open {_game.Zone.Light.EyeOpen:F2}, sky at eye {_game.Zone.Weather.SkyAtEye:F2}, drops {_game.Zone.Weather.RainShown}, splashes {_game.Zone.Weather.SplashShown}");
    }

    /// <summary>
    /// The nearest open dry ground with a clear view one way: nothing of the land rises over the eye's
    /// line that way for a couple of kilometres, and no tree or place stands near.
    /// </summary>
    Vector3 Lookout(Vector3 near, Vector3 toward, float eyeUp = 1.8f)
    {
        toward = new Vector3(toward.X, 0f, toward.Z).Normalized();
        for (int i = 1; i < 400; i++)
        {
            float r = 18f + i * 6f;
            Vector3 g = _game.Zone.GroundAt(near.X + MathF.Cos(i * 0.39f) * r, near.Z + MathF.Sin(i * 0.39f) * r);
            if (g.Y < 1.5f || _game.Zone.Water.LiquidAt(g.X, g.Z, out _) || _game.Places?.PlaceAt(g) != null || _game.Zone.Streamer.SolidsNear(g, 20f).Any()) continue;
            bool open = true;
            for (float d = 6f; d < 2400f && open; d *= 1.35f)
            {
                Vector3 ahead = g + toward * d;
                if (_game.Zone.GroundAt(ahead.X, ahead.Z).Y > g.Y + eyeUp - 1.2f + d * 0.02f) open = false;
                if (d < 120f && _game.Zone.Streamer.SolidsNear(ahead, 8f).Any()) open = false;
            }
            if (open) return g;
        }
        GD.Print("(no open lookout that way: using the nearest clearing)");
        return Clearing(near);
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
        bool ridge = Num("ridge", 0) != 0, slope = Num("slope", 0) != 0;
        Vector3 hill = ridge ? _game.Survivor.WorldFeet : Num("clear", 0) != 0 ? Clearing(_game.Survivor.WorldFeet) : Hilltop(_game.Survivor.WorldFeet, 300f);
        _game.Survivor.PlaceOutside(hill.ToNumerics());
        foreach (int x in _drive.UntilSettled()) yield return x;
        sky.ClearCells();
        float ang = (float)Num("ang", 0) * Mathf.Pi / 180f;
        Vector3 toward = new Vector3(1f, 0f, 0.35f).Normalized().Rotated(Vector3.Up, ang);
        float off = (float)Num("off", 1500);
        float eyeUp = ridge ? 1.7f : Num("clear", 0) != 0 ? 2f : (float)Num("up", 16);
        if (ridge)
        {
            // Stand 350 m short of the highest hill about, on its lowest side, and put the cell a kilometre behind it.
            Vector3 top = Hilltop(_game.Survivor.WorldFeet, 900f);
            float low = -1f;
            for (int k = 0; k < 24; k++)
            {
                Vector3 dir = Vector3.Right.Rotated(Vector3.Up, k * Mathf.Tau / 24f);
                Vector3 foot = _game.Zone.GroundAt(top.X - dir.X * 350f, top.Z - dir.Z * 350f);
                if (foot.Y < 3f || _game.Zone.Water.LiquidAt(foot.X, foot.Z, out _) || _game.Zone.Streamer.SolidsNear(foot, 12f).Any()) continue;
                float rise = top.Y - foot.Y;
                if (rise > low) { low = rise; toward = dir; }
            }
            hill = _game.Zone.GroundAt(top.X - toward.X * 350f, top.Z - toward.Z * 350f);
            _game.Survivor.PlaceOutside(hill.ToNumerics());
            foreach (int x in _drive.UntilSettled()) yield return x;
            hill = _game.Zone.GroundAt(hill.X, hill.Z);
            off = 1350f;
            GD.Print($"staging: hill {low:F0} m high between camera and cell");
        }
        if (slope)
        {
            // Ridge: the direction with the tallest rise to a ridge 200 m or more out, the cell a kilometre past it.
            // Slope: the direction whose cell ground is steepest.
            float best = -1f;
            for (int k = 0; k < 24; k++)
            {
                Vector3 dir = Vector3.Right.Rotated(Vector3.Up, k * Mathf.Tau / 24f);
                float score;
                float at = off;
                if (ridge)
                {
                    float rise = 0f, where = 300f;
                    for (float d = 200f; d <= 800f; d += 25f)
                    {
                        float r = _game.Zone.GroundAt(hill.X + dir.X * d, hill.Z + dir.Z * d).Y - hill.Y - 1.7f;
                        if (r > rise) { rise = r; where = d; }
                    }
                    score = rise; at = where + 1000f;
                }
                else
                {
                    Vector3 c = hill + dir * off;
                    float s = 0f;
                    for (float d = -150f; d <= 150f; d += 50f) s += MathF.Abs(_game.Zone.GroundAt(c.X + dir.X * d, c.Z + dir.Z * d).Y - _game.Zone.GroundAt(c.X, c.Z).Y);
                    score = s;
                }
                if (score > best) { best = score; toward = dir; off = at; }
            }
            GD.Print($"staging: {(ridge ? "ridge rise" : "slope relief")} {best:F0} m, cell {off:F0} m out");
        }
        var cell = sky.AddCell(_game.Zone.GroundAt(hill.X + toward.X * off, hill.Z + toward.Z * off), 1f);
        Vector3 eye = hill + Vector3.Up * eyeUp;
        if (Num("mound", 0) != 0)
        {
            // A smooth green hill (a flattened ball) in front of the cell, or under it so the shaft stands on a slope:
            // the same depth test as terrain, with a known shape. Never in a shipped scene.
            bool under = Num("mound", 0) == 2;
            float dist = under ? off : 450f, wide = under ? 260f : 380f, tall = under ? 110f : 95f;
            Vector3 c = _game.Zone.GroundAt(hill.X + toward.X * dist, hill.Z + toward.Z * dist);
            if (under) c = new Vector3(c.X, c.Y - 20f, c.Z);
            var ball = new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 48, Rings = 24 },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.32f, 0.5f, 0.2f), Roughness = 1f },
                GlobalTransform = new Transform3D(Basis.Identity.Scaled(new Vector3(wide, tall, wide)), c),
            };
            _game.AddChild(ball);
        }
        _game.FixedCamera = new Transform3D(Basis.LookingAt(toward + Vector3.Up * (float)Num("pitch", 0.12), Vector3.Up), eye);
        foreach (int x in _drive.Wait(90)) yield return x;
        if (Num("bolt", 0) != 0)
        {
            // The next strike out of it, caught as it lands.
            for (int i = 0; i < 1800 && !sky.Events.Any(e => e.Kind == WeatherEventKind.LightningStrike); i++) yield return 0;
            _game.Zone.Weather.StrikeAt(_game.Zone.GroundAt(cell.Position.X - toward.Z * cell.Radius * 0.3f, cell.Position.Z + toward.X * cell.Radius * 0.3f));
            foreach (int x in _drive.Wait(1)) yield return x;
        }
        if (Num("compare", 0) != 0)
        {
            foreach (int x in Shot(_args["out"].Replace(".png", "-with.png"))) yield return x;
            _game.Zone.Weather.ShaftsHidden = true;
            foreach (int x in _drive.Wait(30)) yield return x;
            foreach (int x in Shot(_args["out"].Replace(".png", "-without.png"))) yield return x;
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

