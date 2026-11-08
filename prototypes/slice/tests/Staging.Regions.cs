using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using PlanetSurface.Terrain;
using Slice.Surface;
using Slice.Vessels;
using Weather.Forces;
using Weather.Model;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;

namespace Slice.Tests;

/// <summary>
/// Regional weather and wind on bodies, seen: a storm front from high up as it crosses from one
/// region into the next; a tornado over open plains with the mountains beside it clear; and a
/// tornado walking down a row of bodies of every weight (a frame every few ticks, for a GIF).
/// </summary>
public partial class Staging
{
    WeatherSim WeatherNow => _game.Zone.Weather.Sim;

    /// <summary>A rough name for a stretch of land, to tell two regions apart.</summary>
    string RegionOf(Vector3 p)
    {
        Ground g = WeatherNow.GroundAt(p);
        RegionRules r = WeatherNow.Data.Regions;
        return g.Water ? "water" : Regions.High(r, g) > 0.5f ? "mountains" : g.Forest > 0.5f ? "forest"
            : g.Rugged > 0.6f || g.Slope > 0.15f ? "broken hills" : g.Humidity < 0.35f ? "dry plain" : "plain";
    }

    void Midday()
    {
        var sky = _game.Zone.Sky;
        sky.Paused = true;
        // Full daylight on this world: the hour its sun stands highest (--time picks one instead).
        double noon = 0.5;
        for (double t = 0.2; t < 0.8; t += 0.002) if (sky.SunAt(t).Y > sky.SunAt(noon).Y) noon = t;
        sky.TimeOfDay = Num("time", noon);
        _game.Visor.Visible = false;
        _game.Hands.Visible = false;
        _game.Alerts.Visible = false;
    }

    /// <summary>
    /// A storm front from --up metres high (600), --back metres (2500) downwind of where its edge
    /// crosses from one kind of land into another, looking up the wind at it: its shelf and deck over
    /// the one region, the fair sky still over the other.
    /// </summary>
    IEnumerable<int> RegionFront()
    {
        Midday();
        WeatherSim sim = WeatherNow;
        sim.FrontsOff = true;
        sim.Snap(WeatherKind.Clouds);
        sim.ClearTornadoes();
        foreach (int x in _drive.UntilSettled()) yield return x;
        sim.Force(WeatherKind.Thunderstorm); // the storm comes in on the front: fair ahead of it, its own weather behind
        Vector2 stormWind = sim.SegmentAt(sim.Time).To.Wind;
        Vector2 way = Front.Lattice(stormWind, sim.WorldSize, 5500f).heading;
        var down = new Vector3(way.X, 0f, way.Y);
        // A boundary between two regions across the front's path, near the start.
        Vector3 start = _game.Survivor.WorldFeet, boundary = start;
        string from = "", to = "";
        for (int k = 0; k < 400 && from == to; k++)
        {
            float a = k * 2.399f, r = 300f + k * 20f;
            Vector3 p = start + new Vector3(MathF.Cos(a), 0f, MathF.Sin(a)) * r;
            string here = RegionOf(p), ahead = RegionOf(p + down * 1500f);
            if (here != ahead && here != "water" && ahead != "water") (boundary, from, to) = (p + down * 750f, here, ahead);
        }
        GD.Print($"(front: crossing from {from} into {to})");
        float back = (float)Num("back", 2500), up = (float)Num("up", 600);
        Vector3 eye = boundary + down * back;
        eye.Y = MathF.Max(_game.Zone.GroundAt(eye.X, eye.Z).Y, 0f) + up;
        _game.Survivor.PlaceOutside((_game.Zone.GroundAt(boundary.X, boundary.Z) + Vector3.Up).ToNumerics());
        foreach (int x in _drive.UntilSettled()) yield return x;
        // Its edge just over the boundary, advancing from the first region into the second.
        Front f = sim.StartFront(FrontKind.Storm, boundary, (float)Num("dist", 200), stormWind);
        sim.FrontsOff = true;
        foreach (int x in _drive.Wait(60)) yield return x;
        float pitch = -(float)Num("down", 0.2);
        _game.FixedCamera = new Transform3D(Basis.LookingAt(-down * MathF.Cos(pitch) + Vector3.Up * MathF.Sin(pitch), Vector3.Up), eye);
        foreach (int x in _drive.Wait(90)) yield return x;
        GD.Print($"(front: edge {f.Ahead(boundary):F0} m from the boundary, camera {up:F0} m up, {back:F0} m off; under it {RegionOf(boundary - down * 1000f)}, ahead {RegionOf(eye)})");
        if (_args.TryGetValue("later", out string later))
        {
            foreach (int x in Shot(_args["out"].Replace(".png", "-a.png"))) yield return x;
            Hurry(8);
            for (int i = 0; i < 60 * int.Parse(later); i++) yield return 0;
            Hurry(1);
            foreach (int x in _drive.Wait(30)) yield return x;
            GD.Print($"(front: {later} s on, edge {f.Ahead(boundary):F0} m from the boundary)");
        }
    }

    /// <summary>
    /// A tornado over open plains, with mountains beside it standing clear: the nearest broad open
    /// ground a tornado may form over within a few kilometres of mountain country, a strong cell over
    /// it and its funnel down, seen from the plains --dist metres (1100) off with the mountains behind.
    /// </summary>
    IEnumerable<int> PlainsTornado()
    {
        Midday();
        WeatherSim sim = WeatherNow;
        sim.FrontsOff = true;
        sim.Snap(WeatherKind.Thunderstorm);
        sim.ClearTornadoes();
        sim.ClearCells();
        foreach (int x in _drive.UntilSettled()) yield return x;
        RegionRules rules = sim.Data.Regions;
        Vector3 start = _game.Survivor.WorldFeet, plain = start, peak = start;
        float best = float.MaxValue;
        for (int k = 0; k < 3000; k++)
        {
            float a = k * 2.399f, r = 200f + k * 4f;
            Vector3 p = start + new Vector3(MathF.Cos(a), 0f, MathF.Sin(a)) * r;
            if (Regions.TornadoGround(rules, sim.Land, p) < 0.5f) continue;
            for (int j = 0; j < 12; j++)
            {
                Vector3 m = p + new Vector3(MathF.Cos(j * 0.52f), 0f, MathF.Sin(j * 0.52f)) * 1600f;
                Ground g = sim.GroundAt(m);
                if (Regions.High(rules, g) < 0.6f || g.Height < sim.GroundAt(p).Height + 60f) continue;
                if (r < best) (best, plain, peak) = (r, p, m);
                break;
            }
            if (best < float.MaxValue) break;
        }
        if (best == float.MaxValue) { GD.Print("(plains tornado: no open plain beside mountains near the start: try another world)"); yield break; }
        Vector3 toMountain = new Vector3(peak.X - plain.X, 0f, peak.Z - plain.Z).Normalized();
        float dist = (float)Num("dist", 1100);
        Vector3 eye = plain - toMountain.Rotated(Vector3.Up, 0.35f) * dist;
        eye.Y = MathF.Max(_game.Zone.GroundAt(eye.X, eye.Z).Y, 0f) + (float)Num("up", 30);
        _game.Survivor.PlaceOutside((_game.Zone.GroundAt(eye.X, eye.Z) + Vector3.Up).ToNumerics());
        foreach (int x in _drive.UntilSettled()) yield return x;
        sim.AddCell(plain, 1f, first: true);
        var (rules2, shape) = sim.Roll(sim.Current.WindSpeed);
        rules2.Speed = 0.5f;
        Tornado t = sim.SpawnTornado(plain, plain + toMountain * 100f, (rules2, sim.Data.Tornado.Shapes.First(s => s.Shape == Enum.Parse<TornadoShape>(_args.GetValueOrDefault("shape", "Wedge")))));
        t.Homing = false;
        t.Kept = true;
        Hurry(6);
        for (int i = 0; i < 60 * 60 && !t.Touchdown; i++) yield return 0;
        Hurry(1);
        foreach (int x in _drive.Wait(120)) yield return x;
        Vector3 look = new Vector3(plain.X - eye.X, 0f, plain.Z - eye.Z).Normalized();
        float pitch = (float)Num("lookup", 0.12);
        _game.FixedCamera = new Transform3D(Basis.LookingAt(look * MathF.Cos(pitch) + Vector3.Up * MathF.Sin(pitch), Vector3.Up), eye);
        foreach (int x in _drive.Wait(60)) yield return x;
        int over = sim.Tornadoes.Count(o => o.Kind == TornadoKind.Tornado && Regions.High(rules, sim.GroundAt(o.Position)) > 0.3f);
        GD.Print($"(plains tornado: a {t.Shape} on {RegionOf(plain)} ({sim.GroundAt(plain).Height:F0} m), mountains {RegionOf(peak)} at {sim.GroundAt(peak).Height:F0} m 1.6 km beyond; {over} tornadoes over high ground; camera {dist:F0} m off)");
    }

    /// <summary>
    /// A tornado walking slowly down a row of bodies of every weight, side on from --dist metres (150):
    /// creatures light to heavy, the survivor, a hover bike, a landed Skiff and a landed hover tank.
    /// One frame every --every ticks (12) for --seconds (24), as --out with the frame's number.
    /// </summary>
    IEnumerable<int> WindBodies()
    {
        Midday();
        WeatherSim sim = WeatherNow;
        sim.FrontsOff = true;
        sim.Snap(WeatherKind.Thunderstorm); // a storm's cells overhead: the funnel hangs from one
        sim.ClearTornadoes();
        foreach (int x in _drive.UntilSettled()) yield return x;
        // A long stretch of open flat ground.
        RegionRules rules = sim.Data.Regions;
        Vector3 start = _game.Survivor.WorldFeet, row = start;
        var along = new Vector3(1f, 0f, 0f);
        bool woods = Num("trees", 0) == 1; // --trees=1: a stand of trees along the row instead of clear ground
        for (int k = 0; k < 2000; k++)
        {
            float a = k * 2.399f, r = 60f + k * 3f;
            Vector3 p = start + new Vector3(MathF.Cos(a), 0f, MathF.Sin(a)) * r;
            bool flat = true;
            for (int j = -3; j <= 3 && flat; j++)
            {
                Vector3 q = p + along * (j * 25f);
                Ground g = sim.GroundAt(q);
                flat = !g.Water && (woods || g.Forest < 0.05f) && g.Slope < 0.06f && !_game.Zone.Water.LiquidAt(q.X, q.Z, out _) && _game.Places?.PlaceAt(q) == null
                    && (woods || !_game.Zone.Streamer.SolidsNear(_game.Zone.GroundAt(q.X, q.Z), 14f).Any());
            }
            if (flat && (!woods || _game.Zone.Streamer.SolidsNear(_game.Zone.GroundAt(p.X, p.Z), 30f).Count(s => s.solid.Fellable) >= 6)) { row = p; break; }
        }
        Vector3 At(float x) => _game.Zone.GroundAt(row.X + x, row.Z);
        // The survivor stands in the row too: the weather happens round them, and their streamed ground holds everyone up.
        _game.Survivor.PlaceOutside((At(-15f) + Vector3.Up * 0.1f).ToNumerics());
        foreach (int x in _drive.UntilSettled()) yield return x;
        var life = _game.Zone.Wildlife;
        life.Frozen = false;
        var walkers = (life.Fauna?.Species ?? new List<Wildlife.Species>()).Where(s => !s.Flies && s.Rig != Wildlife.RigKind.Swarm).OrderBy(Wildlife.Heft.Mass).ToList();
        var labels = new List<string>();
        if (walkers.Count > 0)
            foreach (var (s, x) in new[] { (walkers[0], -50f), (walkers[walkers.Count / 2], -35f), (walkers[^1], -25f) })
            {
                life.Sim.Spawn(s, life.PatchLocal(At(x)));
                labels.Add($"{s.Name} {Wildlife.Heft.Mass(s):F0} kg at {x:F0} m");
            }
        _game.Bikes.Place(At(-5f), 0f);
        Structure skiff = _game.Zone.Data.Blueprint("rover.gbp");
        Vessel small = Vessel.Spawn(_game.Zone, skiff, (At(10f) + Vector3.Up * 3f).ToNumerics(), 0f, landed: true);
        Structure flyerPlan = _game.Zone.Data.Blueprint(_game.Zone.Data.Rules.Build.StarterBlueprint);
        Vessel flyer = Vessel.Spawn(_game.Zone, flyerPlan, (At(28f) + Vector3.Up * 12f).ToNumerics(), 0f, landed: false);
        Structure tankPlan = _game.Zone.Data.Blueprint("tank.gbp");
        Vessel tank = Vessel.Spawn(_game.Zone, tankPlan, (At(50f) + Vector3.Up * 4f).ToNumerics(), 0f, landed: true);
        foreach (int x in _drive.Wait(120)) yield return x;
        GD.Print($"(wind bodies: {string.Join(", ", labels)}, survivor at -15 m, bike at -5 m, {skiff.Name} at 10 m, {flyerPlan.Name} flying at 28 m, {tankPlan.Name} at 50 m)");
        float dist = (float)Num("dist", 150);
        Vector3 eye = At(0f) + new Vector3(0f, 0f, dist);
        eye.Y = MathF.Max(eye.Y, _game.Zone.GroundAt(eye.X, eye.Z).Y) + (float)Num("up", 28);
        _game.FixedCamera = new Transform3D(Basis.LookingAt((At(0f) + Vector3.Up * 18f - eye).Normalized(), Vector3.Up), eye);
        _game.Zone.Weather.Bridge.Calm = false;
        TornadoShapeRules cone = sim.Data.Tornado.Shapes.First(s => s.Shape == Enum.Parse<TornadoShape>(_args.GetValueOrDefault("shape", "Cone")));
        TornadoRules tr = sim.Data.Tornado.Shaped(cone, (float)Num("speed", 4), 300f);
        Tornado t = sim.SpawnTornado(At(-75f), At(500f), tr); // under a storm cell of its own: a funnel always hangs from a cloud
        t.Homing = false;
        t.Kept = true;
        t.SetHeading(0f);
        t.SkipFormation();
        int every = (int)Num("every", 12), frames = (int)(Num("seconds", 24) * 60 / every);
        string pattern = _args.GetValueOrDefault("out", "wind.png").Replace(".png", "-{0:D3}.png");
        for (int f = 0; f < frames; f++)
        {
            foreach (int x in Shot(string.Format(pattern, f))) yield return x;
            for (int i = 0; i < every; i++) yield return 0;
            if (f % 10 == 0)
                GD.Print($"(frame {f}: tornado at {t.Position.X - row.X:F0} m; hovercraft {small.Body.WorldPosition.Y - At(10f).Y:F1} m up, tilt {Mathf.RadToDeg(N.Vector3.Transform(N.Vector3.UnitY, small.Body.Rotation).ToGodot().AngleTo(Vector3.Up)):F0}°, damage {small.Body.Damage:F2}; flyer tilt {Mathf.RadToDeg(N.Vector3.Transform(N.Vector3.UnitY, flyer.Body.Rotation).ToGodot().AngleTo(Vector3.Up)):F0}°; tank moved {N.Vector3.Distance(tank.Body.WorldPosition, (At(50f) + Vector3.Up * 4f).ToNumerics()):F1} m; survivor {_game.Survivor.WorldFeet.Y - At(-15f).Y:F1} m up; trees torn {_game.Zone.Weather.TreesTorn})");
        }
    }

    /// <summary>
    /// A tornado through a stand of trees, from above the canopy --dist metres (90) off: trees pulled up whole
    /// and thrown as themselves, others snapped or blown over, stumps and root holes left along its path.
    /// A frame every --every ticks (12) for --seconds (20), as --out with the frame's number.
    /// </summary>
    IEnumerable<int> TreePath()
    {
        Midday();
        WeatherSim sim = WeatherNow;
        sim.FrontsOff = true;
        sim.Snap(Enum.Parse<WeatherKind>(_args.GetValueOrDefault("weather", "Thunderstorm")));
        sim.ClearTornadoes();
        foreach (int x in _drive.UntilSettled()) yield return x;
        Vector3 start = _game.Survivor.WorldFeet, stand = start;
        int most = 0;
        for (int k = 0; k < 300 && most < 12; k++)
        {
            float a = k * 2.399f, r = 40f + k * 6f;
            Vector3 p = _game.Zone.GroundAt(start.X + MathF.Cos(a) * r, start.Z + MathF.Sin(a) * r);
            if (_game.Zone.Water.LiquidAt(p.X, p.Z, out _) || _game.Places?.PlaceAt(p) != null) continue;
            int n = _game.Zone.Streamer.FloraNear(p, 25f).Count(s => s.Kind is Planets.Look.Flora.Conifer or Planets.Look.Flora.Broadleaf or Planets.Look.Flora.JungleTree or Planets.Look.Flora.Palm); // the scatter, near or far: no need to go there to look
            if (n > most) (most, stand) = (n, p);
        }
        _game.Survivor.PlaceOutside((stand + new Vector3(0f, 0f, 60f) + Vector3.Up * 0.1f).ToNumerics());
        foreach (int x in _drive.UntilSettled()) yield return x;
        GD.Print($"(tree path: {most} trees within 25 m of the stand)");
        float dist = (float)Num("dist", 130);
        Vector3 eye = stand + new Vector3(0f, 0f, dist);
        // Clear over the canopy: above the tallest plant between it and the stand, and then some.
        float canopy = 0f;
        foreach (TerrainStreamer.PlantSighting s in _game.Zone.Streamer.FloraNear(stand + new Vector3(0f, 0f, dist * 0.5f), dist * 0.6f))
            canopy = MathF.Max(canopy, s.Root.Y + (new Transform3D(s.Turn, Vector3.Zero) * (_game.Zone.Streamer.MeshOf(s)?.GetAabb() ?? new Aabb())).End.Y);
        eye.Y = MathF.Max(MathF.Max(eye.Y, _game.Zone.GroundAt(eye.X, eye.Z).Y), canopy) + (float)Num("up", 25);
        _game.FixedCamera = new Transform3D(Basis.LookingAt((stand + Vector3.Up * 10f - eye).Normalized(), Vector3.Up), eye);
        if (Num("still", 0) == 1)
        {
            // The stand as it is, no tornado: what the trees look like untouched.
            foreach (int x in _drive.Wait(60)) yield return x;
            yield break;
        }
        _game.Zone.Weather.Bridge.Calm = false;
        TornadoShapeRules cone = sim.Data.Tornado.Shapes.First(s => s.Shape == Enum.Parse<TornadoShape>(_args.GetValueOrDefault("shape", "Wedge")));
        Tornado t = sim.SpawnTornado(stand - new Vector3(45f, 0f, 0f), stand + new Vector3(500f, 0f, 0f), sim.Data.Tornado.Shaped(cone, (float)Num("speed", 4), 300f));
        t.Homing = false;
        t.Kept = true;
        t.SetHeading(0f);
        t.SkipFormation();
        int every = (int)Num("every", 12), frames = (int)(Num("seconds", 20) * 60 / every);
        string pattern = _args.GetValueOrDefault("out", "trees.png").Replace(".png", "-{0:D3}.png");
        SurfaceWeather fx = _game.Zone.Weather;
        for (int f = 0; f < frames; f++)
        {
            foreach (int x in Shot(string.Format(pattern, f))) yield return x;
            for (int i = 0; i < every; i++) yield return 0;
            if (f % 10 == 0)
                GD.Print($"(frame {f}: tornado at {t.Position.X - stand.X:F0} m; torn {fx.TreesTorn}: {fx.TreesThrown} thrown, {fx.TreesSnapped} snapped, {fx.TreesUprooted} blown over; flying {fx.Trees?.Flying ?? 0}; last thrown stood {fx.LastThrown.Standing} flew {fx.LastThrown.Flying})");
        }
    }

    /// <summary>
    /// A tornado storm coming, warned: on open land where tornado storms are themselves, the survivor looks
    /// upwind at the sky while the alert row (bottom left) shows the warning with its sign and its time.
    /// </summary>
    IEnumerable<int> TornadoWarning()
    {
        var sky = _game.Zone.Sky;
        sky.Paused = true;
        double noon = 0.5;
        for (double t = 0.2; t < 0.8; t += 0.002) if (sky.SunAt(t).Y > sky.SunAt(noon).Y) noon = t;
        sky.TimeOfDay = noon;
        WeatherSim sim = WeatherNow;
        sim.Snap(WeatherKind.Clouds);
        foreach (int x in _drive.UntilSettled()) yield return x;
        Vector3 at = _game.Survivor.WorldFeet;
        for (int k = 0; k < 600 && sim.LocalKind(WeatherKind.Tornado, at) != WeatherKind.Tornado; k++)
            at = _game.Survivor.WorldFeet + new Vector3(MathF.Cos(k * 2.399f), 0f, MathF.Sin(k * 2.399f)) * (150f + k * 12f);
        GD.Print($"(tornado warning: standing where tornado storms are themselves: {sim.LocalKind(WeatherKind.Tornado, at)})");
        _game.Survivor.PlaceOutside((at + Vector3.Up * 0.2f).ToNumerics());
        foreach (int x in _drive.UntilSettled()) yield return x;
        sim.Force(WeatherKind.Tornado);
        foreach (int x in _drive.Wait(120)) yield return x;
        var list = new List<WarningEntry>();
        sim.GetWarnings(list, _game.Survivor.WorldFeet);
        GD.Print($"(tornado warning: {string.Join("; ", list.Select(w => $"{w.Sign} {w.Text}"))})");
    }
}
