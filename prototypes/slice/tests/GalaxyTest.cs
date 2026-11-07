using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Stars;
using Godot;
using Planets.Gen;
using PlanetSurface.Skies;
using Slice.Game;
using Slice.Orbit;
using Slice.Ui;
using N = System.Numerics;

namespace Slice.Tests;

/// <summary>
/// The galaxy, and the sky drawn from it, on fresh seeds every run: same seed, same galaxy; its size
/// and shape; regions, nebulae, double stars and satellites; the home star fits the world; every
/// star on the map is in the sky in the right direction; a jump shifts directions by true parallax;
/// the map's trail goes down and back up; and, climbing out through the real game, the sky's own
/// uniforms hold every star's direction still from the ground to space.
///
/// Run: .\tools\test.ps1 -Project prototypes/slice -Scene res://tests/GalaxyTest.tscn
/// </summary>
public partial class GalaxyTest : Node
{
    const int WatchdogMinutes = 12;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    Game.Game _game;
    IEnumerator<int> _climb;
    readonly Random _rng = new();

    public override void _Ready()
    {
        var thread = new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(WatchdogMinutes));
            System.Console.Error.WriteLine($"FAIL  the run took over {WatchdogMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true };
        thread.Start();
        try
        {
            ulong seed = (ulong)_rng.NextInt64();
            GD.Print($"galaxy seed {seed}");
            var made = Stopwatch.StartNew();
            long before = GC.GetTotalMemory(true);
            Galaxy g = Galaxy.Make(seed);
            double makeMs = made.Elapsed.TotalMilliseconds;
            long catalogue = GC.GetTotalMemory(true) - before;
            Sameness(seed, g);
            Shape(g);
            Regions(g);
            Nebulae(g);
            Names(g);
            Homes(g);
            int home = g.Home(StarKind.YellowDwarf, seed, false);
            made.Restart();
            before = GC.GetTotalMemory(true);
            SkyView view = SkyView.From(g, home);
            double bakeMs = made.Elapsed.TotalMilliseconds;
            long sky = GC.GetTotalMemory(true) - before;
            OnTheMap(g, view, home);
            Lookup(g, view);
            Jump(g, view, home);
            CoreGlow(g);
            FromInside(g);
            Frames();
            Trail();
            GD.Print($"catalogue: {g.Stars.Length} stars in {makeMs:F0} ms, {catalogue / 1024} KB; sky baked in {bakeMs:F0} ms, {sky / 1024} KB on the heap ({view.StarTexels.Length / 4} list entries)");
        }
        catch (Exception e) { Check(false, $"threw: {e}"); }
        _game = new Game.Game { Interactive = false, SkipIntro = true, World = WorldChoice.Fresh() };
        AddChild(_game);
        GD.Print($"world: seed {_game.World.Seed}, {_game.World.Name}");
        _climb = new Staging(_game, new Dictionary<string, string>()).Run("galaxy-climb").GetEnumerator();
        ProcessPhysicsPriority = 100;
    }

    // ---- The catalogue ---------------------------------------------------------------------------

    void Sameness(ulong seed, Galaxy g)
    {
        Galaxy again = Galaxy.Make(seed);
        Check(g.Stars.SequenceEqual(again.Stars), "same seed, same stars, in the same order");
        Check(g.Nebulae.Select(n => (n.Centre, n.Radius, n.Name)).SequenceEqual(again.Nebulae.Select(n => (n.Centre, n.Radius, n.Name))), "same seed, same nebulae");
        Check(g.SystemSeed(1234) == again.SystemSeed(1234) && g.Name(1234) == again.Name(1234), "a star's system seed and name are the same every time");
        Galaxy other = Galaxy.Make(seed + 1);
        Check(g.Stars[100].Position != other.Stars[100].Position, "another seed, another galaxy");
        var seeds = new HashSet<ulong>(g.Stars.Select(s => g.SystemSeed(s.Id)));
        Check(seeds.Count == g.Stars.Length, "every star has a system seed of its own");
    }

    void Shape(Galaxy g)
    {
        Check(g.Stars.Length is >= 20000 and <= 35000, $"{g.Stars.Length} stars, 20,000 to 35,000");
        Star[] disc = g.Stars.Where(s => s.Region != Region.Satellite).ToArray();
        float widest = disc.Max(s => new Vector2(s.Position.X, s.Position.Z).Length());
        Check(widest is > 450f and < 560f, $"a disc about 500 LY in radius: its furthest star {widest:F0} LY out");
        double rms = Math.Sqrt(disc.Average(s => (double)s.Position.Y * s.Position.Y));
        Check(rms is > 5 and < 60, $"and thin: {rms:F1} LY rms off its plane");
        int stacked = 0;
        foreach (Star s in g.Stars.Take(2000))
            if (g.Within(s.Position, 0.5f, s.Id).Count > 0) stacked++;
        Check(stacked == 0, "no two stars on top of each other");
        Check(g.Stars[0].Kind == StarKind.BlackHole && g.Stars[0].Position == N.Vector3.Zero, "a black hole at the centre");
        Check(g.Stars.Count(s => s.Companions > 0) * 100 / g.Stars.Length is > 8 and < 25, $"double stars: {g.Stars.Count(s => s.Companions > 0)}, triples {g.Stars.Count(s => s.Companions > 1)}");
        Check(g.Satellites.Length >= 3 && g.Satellites.All(s => s.Count > 100 && new Vector2(s.Centre.X, s.Centre.Z).Length() > 600f), $"{g.Satellites.Length} small satellite galaxies, off past the disc");
    }

    void Regions(Galaxy g)
    {
        foreach (Region r in Enum.GetValues<Region>())
            Check(g.Stars.Any(s => s.Region == r), $"{r}: {g.Stars.Count(s => s.Region == r)} stars");
        float Crowd(Region r)
        {
            Star[] all = g.Stars.Where(s => s.Region == r).ToArray();
            Star[] some = Enumerable.Range(0, 120).Select(i => all[i * all.Length / 120]).ToArray();
            return (float)some.Average(s => g.Within(s.Position, 25f, s.Id).Count);
        }
        Check(Crowd(Region.Core) > Crowd(Region.OuterArm) * 2f && Crowd(Region.Void) < Crowd(Region.InnerArm), $"the core crowded ({Crowd(Region.Core):F0} within 25 LY), the voids empty ({Crowd(Region.Void):F0}), the arms between ({Crowd(Region.InnerArm):F0})");
        double Old(Region r) => g.Stars.Where(s => s.Region == r).Average(s => s.Kind is StarKind.RedGiant or StarKind.WhiteDwarf ? 1.0 : 0.0);
        Check(Old(Region.Core) > Old(Region.OuterArm) * 2, "the region picks the classes: old stars in the core");
    }

    void Nebulae(Galaxy g)
    {
        Check(g.Nebulae.Length >= 30, $"{g.Nebulae.Length} nebulae");
        foreach (NebulaKind k in Enum.GetValues<NebulaKind>()) Check(g.Nebulae.Any(n => n.Kind == k), $"{k}: {g.Nebulae.Count(n => n.Kind == k)}");
        int empty = g.Nebulae.Count(n => g.Stars.Count(s => s.Nebula == n.Id) < 3);
        Check(empty == 0, $"every nebula has systems in it to go to ({empty} without)");
        int wrong = g.Stars.Count(s => g.NebulaAt(s.Position) != s.Nebula);
        Check(wrong == 0, $"a star's nebula is the nebula at its place ({wrong} wrong)");
        Nebula big = g.Nebulae.OrderByDescending(n => n.Radius).First(n => n.Kind == NebulaKind.Emission);
        Check(g.Stars.Where(s => s.Nebula == big.Id).All(s => N.Vector3.Distance(s.Position, big.Centre) <= big.Radius), "its members lie inside it");
        Check(g.Nebulae.Count(n => new Vector2(n.Centre.X, n.Centre.Z).Length() > Galaxy.RimFrom && n.Kind != NebulaKind.Dark) >= 3, "and some lie out on the rim");
    }

    void Names(Galaxy g)
    {
        var names = g.Stars.Select(s => g.Name(s.Id)).ToList();
        Check(names.Distinct().Count() == names.Count, $"every star has its own name: {names[5]}, {names[50]}, {names[500]}");
        Check(!names.Any(GalaxyNames.IsForbidden), "none is a real star or anything of Earth");
        Check(!g.Nebulae.Any(n => GalaxyNames.IsForbidden(n.Name)), $"nor any nebula: {g.Nebulae[0].Name}, {g.Nebulae[1].Name}");
    }

    void Homes(Galaxy g)
    {
        for (int i = 0; i < 40; i++)
        {
            var archetype = (Archetype)_rng.Next(Enum.GetValues<Archetype>().Length);
            PlanetSpec spec = PlanetSpec.Roll((ulong)_rng.NextInt64(), archetype, _rng.Next(PlanetSpec.VariantCount));
            SkySystem sky = SkySystem.For(spec);
            StarKind want = SkyRig.KindOf(sky.Star);
            int home = g.Home(want, spec.Seed, sky.Nebula);
            Star s = g.Stars[home];
            bool rim = new Vector2(s.Position.X, s.Position.Z).Length() >= Galaxy.RimFrom;
            if (s.Kind != want || !rim || g.Neighbours(home).Count == 0)
            {
                Check(false, $"a home for {spec}: {s.Kind} wanted {want}, {g.Neighbours(home).Count} neighbours, rim {rim}");
                return;
            }
            if (sky.Nebula && (s.Nebula < 0 || g.Nebulae[s.Nebula].Kind == NebulaKind.Dark) && g.Stars.Any(c => c.Kind == want && c.Nebula >= 0 && new Vector2(c.Position.X, c.Position.Z).Length() >= Galaxy.RimFrom && g.Neighbours(c.Id).Count >= 4 && g.Nebulae[c.Nebula].Kind != NebulaKind.Dark))
                Check(false, $"a nebula world's home is in a nebula ({spec})");
        }
        Check(true, "40 random worlds: each home is a real star of the world's own class, on the rim, with neighbours");
        foreach (StarKind k in new[] { StarKind.RedDwarf, StarKind.OrangeDwarf, StarKind.YellowDwarf, StarKind.WhiteStar, StarKind.BlueGiant })
            Check(g.Stars[g.Home(k, 77, false)].Kind == k, $"a {k} home exists");
    }

    // ---- The sky ---------------------------------------------------------------------------------

    void OnTheMap(Galaxy g, SkyView view, int home)
    {
        List<StarEntry> stars = Charts.Stars(g, view, home);
        Check(stars.Count(s => s.Home) == 1 && stars.Count >= 2, $"the map holds home and {stars.Count - 1} neighbours");
        Check(stars.Where(s => !s.Home).All(s => s.Light is > 0f and <= Galaxy.JumpT1), "each within a jump (30 LY)");
        Check(stars.Select(s => s.Name).SequenceEqual(Charts.Stars(g, view, home).Select(s => s.Name)), "the same every time");
        double worst = 0;
        int missing = 0;
        foreach (StarEntry s in stars.Where(s => !s.Home))
        {
            N.Vector3 truth = N.Vector3.Normalize(g.Stars[s.Id].Position - g.Stars[home].Position);
            worst = Math.Max(worst, Angle(truth, s.Direction));
            if (!view.Resolved[s.Id] || !view.Listed(SkyView.CellOf(s.Direction)).Contains(s.Id) || view.Shown[s.Id] < SkyView.ChartFloor) missing++;
        }
        Check(worst < 1e-3 && missing == 0, $"every star on the map is in the sky, in the right direction (worst {worst:E1} degrees, {missing} missing)");
        Check(view.Shown[home] == 0f && !view.Resolved[home], "your own star isn't among the night's stars");
    }

    void Lookup(Galaxy g, SkyView view)
    {
        int resolved = view.Resolved.Count(r => r);
        Check(resolved > g.Stars.Length * 2 / 5, $"{resolved} of {g.Stars.Length} stars are points of their own, the rest in the soft light of the crowded core and band");
        // Each face round trip, and a star found from just beside it, as a pixel would look it up.
        for (int i = 0; i < 2000; i++)
        {
            var d = N.Vector3.Normalize(new N.Vector3((float)_rng.NextDouble() * 2 - 1, (float)_rng.NextDouble() * 2 - 1, (float)_rng.NextDouble() * 2 - 1));
            var (f, s, t) = SkyView.Face(d);
            if (Angle(SkyView.Through(f, s, t), d) > 1e-2) { Check(false, "a cube face round trip"); return; }
        }
        int lost = 0;
        for (int id = 1; id < g.Stars.Length; id += 3)
        {
            if (!view.Resolved[id]) continue;
            N.Vector3 d = view.Direction[id];
            var off = N.Vector3.Normalize(d + N.Vector3.Normalize(N.Vector3.Cross(d, N.Vector3.UnitY + new N.Vector3(0.3f, 0f, 0.1f))) * 0.0009f);
            if (!view.Listed(SkyView.CellOf(off)).Contains(id)) lost++;
        }
        Check(lost == 0, $"a star's whole point is listed in every cell it touches ({lost} cut off at a cell edge)");
        // The band: most of the light of the sky lies along the disc's plane, because that is where the stars are.
        double along = 0, across = 0;
        for (int id = 0; id < g.Stars.Length; id++)
            if (MathF.Abs(view.Direction[id].Y) < 0.26f) along += view.Shown[id];
            else across += view.Shown[id];
        Check(along > across * 0.9, $"the galaxy's band is simply where the stars are: {along / (along + across):P0} of the light within 15 degrees of the plane (that band is 26% of the sky)");
        double near = g.Stars.Where(s => view.Shown[s.Id] > 0f && N.Vector3.Distance(s.Position, view.Eye) < 25f && s.Kind == StarKind.YellowDwarf).Select(s => (double)view.Shown[s.Id]).DefaultIfEmpty(1).Average();
        double far = g.Stars.Where(s => N.Vector3.Distance(s.Position, view.Eye) > 300f && s.Kind == StarKind.YellowDwarf).Select(s => (double)view.Shown[s.Id]).DefaultIfEmpty(0).Average();
        Check(near > far * 2, $"brightness comes from distance: yellow dwarfs near {near:F3}, far {far:F3}");
    }

    void Jump(Galaxy g, SkyView a, int home)
    {
        int to = g.Neighbours(home).Last();
        SkyView b = SkyView.From(g, to);
        N.Vector3 hop = g.Stars[to].Position - g.Stars[home].Position;
        double worst = 0, nearShift = 0, farShift = 0;
        int near = 0, far = 0;
        foreach (Star s in g.Stars)
        {
            if (s.Id == home || s.Id == to) continue;
            N.Vector3 truth = N.Vector3.Normalize(s.Position - g.Stars[to].Position);
            worst = Math.Max(worst, Angle(truth, b.Direction[s.Id]));
            double shift = Angle(a.Direction[s.Id], b.Direction[s.Id]);
            float d = N.Vector3.Distance(s.Position, g.Stars[home].Position);
            if (d < 60f) { nearShift += shift; near++; }
            else if (d > 400f) { farShift += shift; far++; }
        }
        Check(worst < 1e-3, $"after a jump of {hop.Length():F1} LY every star stands in its true direction from there (worst {worst:E1} degrees)");
        Check(nearShift / Math.Max(near, 1) > farShift / Math.Max(far, 1) * 4, $"constellations shift by parallax: stars within 60 LY {nearShift / Math.Max(near, 1):F1} degrees on average, past 400 LY {farShift / Math.Max(far, 1):F2}");
        N.Vector3 back = N.Vector3.Normalize(g.Stars[home].Position - g.Stars[to].Position);
        Check(Angle(b.Direction[home], back) < 1e-3 && b.Resolved[home], "the star you left is in the sky behind you, just where it should be");
        Check(!b.Resolved[to], "and the star you came to is the sun now, not a point in the sky");
        var charts = new Charts();
        charts.Visited.Add(home);
        var shown = charts.Shown(g);
        Check(shown.Count == g.Neighbours(home).Count + 1 && !shown.Contains(0), "the map shows where you have been and its neighbours, not the rest of the galaxy");
        charts.Visited.Add(to);
        Check(charts.Shown(g).Count > shown.Count, "and each jump reveals more");
    }

    /// <summary>From any system the core is a real feature of the sky: a dense glow in its true direction.</summary>
    void CoreGlow(Galaxy g)
    {
        float Light(SkyView v, N.Vector3 dir)
        {
            var (f, s, t) = SkyView.Face(N.Vector3.Normalize(dir));
            int x = Math.Clamp((int)((s * 0.5f + 0.5f) * SkyView.GlowSize), 0, SkyView.GlowSize - 1), y = Math.Clamp((int)((t * 0.5f + 0.5f) * SkyView.GlowSize), 0, SkyView.GlowSize - 1), k = (x + y * SkyView.GlowSize) * 3;
            return v.Glow[f][k] + v.Glow[f][k + 1] + v.Glow[f][k + 2];
        }
        for (int i = 0; i < 4; i++)
        {
            Star[] rim = g.Stars.Where(s => s.Region is Region.OuterArm or Region.Periphery && s.Nebula < 0).ToArray();
            Star from = rim[_rng.Next(rim.Length)];
            SkyView v = SkyView.From(g, from.Id);
            N.Vector3 core = N.Vector3.Normalize(-from.Position);
            N.Vector3 side = N.Vector3.Normalize(N.Vector3.Cross(core, N.Vector3.UnitY));
            float peak = 0f;
            for (int a = 0; a < 200; a++)
                peak = MathF.Max(peak, Light(v, core + new N.Vector3((float)_rng.NextDouble() - 0.5f, (float)_rng.NextDouble() - 0.5f, (float)_rng.NextDouble() - 0.5f) * 0.05f));
            // Along the band a quarter of the sky away: the arms, without the core.
            float band = 0f;
            for (int a = 0; a < 200; a++)
                band += Light(v, core + side * ((float)_rng.NextDouble() * 0.5f + 0.9f) * (a % 2 == 0 ? 1f : -1f)) / 200f;
            Check(peak > 0.15f && peak > band * 2f, $"seen from {g.Name(from.Id)}, {from.Position.Length():F0} LY out, the core glows in its true direction: {peak:F2} against {band:F2} along the band");
        }
    }

    void FromInside(Galaxy g)
    {
        Nebula n = g.Nebulae.OrderByDescending(x => x.Radius).First(x => x.Kind == NebulaKind.Emission);
        Star inside = g.Stars.Where(s => s.Nebula == n.Id).OrderBy(s => N.Vector3.Distance(s.Position, n.Centre)).First();
        SkyView within = SkyView.From(g, inside.Id);
        // Outside: from the nebula's own distance out along the plane it covers a patch, and the sky behind you is dark.
        N.Vector3 outward = N.Vector3.Normalize(n.Centre with { Y = 0f });
        SkyView away = SkyView.Bake(g, n.Centre + outward * (n.Radius * 3.2f));
        float Lit(SkyView v, N.Vector3 dir)
        {
            var (f, s, t) = SkyView.Face(N.Vector3.Normalize(dir));
            int x = Math.Clamp((int)((s * 0.5f + 0.5f) * SkyView.GlowSize), 0, SkyView.GlowSize - 1), y = Math.Clamp((int)((t * 0.5f + 0.5f) * SkyView.GlowSize), 0, SkyView.GlowSize - 1), k = (x + y * SkyView.GlowSize) * 3;
            return v.Glow[f][k] + v.Glow[f][k + 1] + v.Glow[f][k + 2];
        }
        var dirs = new List<N.Vector3>();
        for (int i = 0; i < 400; i++) dirs.Add(N.Vector3.Normalize(new N.Vector3((float)_rng.NextDouble() - 0.5f, (float)_rng.NextDouble() - 0.5f, (float)_rng.NextDouble() - 0.5f)));
        // Median, so the galaxy's band doesn't count.
        float Median(SkyView v) => dirs.Select(d => Lit(v, d)).OrderBy(x => x).ElementAt(dirs.Count / 2);
        float inMedian = Median(within), outMedian = Median(away);
        Check(inMedian > outMedian * 3f, $"from inside a nebula it fills the sky: typical direction {inMedian:F3}, from outside {outMedian:F3}");
        N.Vector3 toward = N.Vector3.Normalize(n.Centre - away.Eye);
        float patch = 0f;
        for (int i = 0; i < 40; i++)
            patch = MathF.Max(patch, Lit(away, toward + new N.Vector3((float)_rng.NextDouble() - 0.5f, (float)_rng.NextDouble() - 0.5f, (float)_rng.NextDouble() - 0.5f) * 0.3f));
        Check(patch > Lit(away, -toward) * 3f, $"from outside it is a patch in its real direction: toward it {patch:F3}, behind you {Lit(away, -toward):F3}");
        Nebula dark = g.Nebulae.OrderByDescending(x => x.Dust).First(x => x.Kind == NebulaKind.Dark);
        Check(g.Clear(dark.Centre - N.Vector3.UnitX * dark.Radius * 2f, dark.Centre + N.Vector3.UnitX * dark.Radius * 2f) < 0.97f || g.Clear(dark.Centre - N.Vector3.UnitZ * dark.Radius * 2f, dark.Centre + N.Vector3.UnitZ * dark.Radius * 2f) < 0.97f, "a dark nebula blocks the light of what lies behind it");
    }

    void Frames()
    {
        double worst = 0;
        for (int i = 0; i < 500; i++)
        {
            double w = 16000, h = 8000, x = _rng.NextDouble() * w, z = _rng.NextDouble() * h;
            N.Quaternion a = SkyFrame.Map(x, z, w, h), b = new GlobeMap(w, h, 2500).Frame(x, z);
            worst = Math.Max(worst, Angle(N.Vector3.Transform(N.Vector3.UnitX, a), N.Vector3.Transform(N.Vector3.UnitX, b)) + Angle(N.Vector3.Transform(N.Vector3.UnitY, a), N.Vector3.Transform(N.Vector3.UnitY, b)));
        }
        Check(worst < 0.01, $"the sky's frame on the map is the orbit's own globe frame (worst {worst:E1} degrees)");
        // At the equator a star past the zenith moves west, as the world turns east under it.
        N.Vector3 up = N.Vector3.UnitY;
        N.Quaternion place = SkyFrame.Map(4000, 4000, 16000, 8000);
        N.Vector3 star = N.Vector3.Transform(up, place);
        N.Vector3 Seen(double t) => N.Vector3.Transform(star, N.Quaternion.Conjugate(N.Quaternion.Normalize(SkyFrame.Spin(t) * place)));
        Check(Seen(0.05).X < -0.1f, $"stars set in the west: {Seen(0.05)} a little after they stood overhead");
    }

    void Trail()
    {
        var t = new MapTrail { You = 7 };
        t.Open(MapLevel.Local);
        Check(t.Star == 7, "the map starts where you are");
        Check(t.Up() && t.Up() && t.Up() && t.Up() && t.Level == MapLevel.Galaxy && !t.Up(), "up from the ground: world, high orbit, system, galaxy, and no further");
        t.OpenStar(99);
        Check(t.Level == MapLevel.System && t.Star == 99 && !t.CanGoDown && !t.Down(), "down to any star's system; an unvisited one goes no lower");
        Check(t.Crumbs("Vask").Select(c => c.name).SequenceEqual(new[] { "GALAXY", "VASK" }), "and the trail says so");
        t.Up();
        t.OpenStar(7);
        Check(t.Down() && t.Level == MapLevel.HighOrbit && t.Down() && t.Level == MapLevel.World && t.Down() && t.Level == MapLevel.Local && !t.Down(), "your own star goes all the way down to the ground under you");
        Check(t.Crumbs("Vask").Count == 5, "the trail from the ground names every level");
        t.Open(MapLevel.Galaxy);
        t.OpenStar(99);
        t.Open(MapLevel.World);
        Check(t.Star == 7, "a world on the map is always yours");
        var view = new GalaxyMapView { Focus = new N.Vector3(100, 5, -40), Distance = 80 };
        view.Project(view.Focus, 1000f, out N.Vector2 at, out float depth);
        Check(at.Length() < 1e-3f && Math.Abs(depth - 80f) < 1e-3f, "the 3D map turns round its focus");
        N.Vector3 above = view.Focus + N.Vector3.UnitY * 20f;
        view.Project(above, 1000f, out N.Vector2 high, out _);
        Check(high.Y < 0f, "a star over the plane stands above the plane on the screen");
        double yaw = view.Yaw;
        view.Turn(50f, 0f);
        N.Vector3 off = view.Focus + N.Vector3.UnitX * 20f;
        view.Project(off, 1000f, out N.Vector2 one, out _);
        view.Turn(100f, 0f);
        view.Project(off, 1000f, out N.Vector2 two, out _);
        Check(view.Yaw != yaw && N.Vector2.DistanceSquared(one, two) > 1f, "a drag turns the map");
        view.Turn(0f, 100000f);
        Check(view.Pitch <= 1.5, "and it stops short of tipping over the pole");
        view.Zoom(-100);
        Check(view.AtClosest, "the wheel closes in as far as a star");
        view.Zoom(100);
        Check(view.Distance == GalaxyMapView.Widest, "and out as far as the whole galaxy");
    }

    // ---- The climb, through the real game ----------------------------------------------------------

    readonly N.Vector3[] _watch = new N.Vector3[3];
    N.Vector3[] _last;
    double _worstStep;
    string _worstAt = "";
    bool _sawSurface, _sawUnder, _sawOver, _sawSpace, _picked;
    readonly Dictionary<string, double> _truth = new();

    public override void _PhysicsProcess(double delta)
    {
        if (_climb == null) return;
        bool more;
        try { more = _climb.MoveNext(); }
        catch (Exception e)
        {
            Check(false, $"the climb threw: {e}");
            more = false;
        }
        Watch();
        if (more) return;
        _climb = null;
        Finish();
    }

    /// <summary>What the sky's own materials say a catalogue direction looks like in the world, this tick.</summary>
    void Watch()
    {
        GalaxySky stars = _game.Surface?.Sky?.Stars;
        if (stars?.View == null || _game.Phase != Phase.Playing) return;
        if (!_picked)
        {
            _picked = true;
            for (int i = 0; i < 3; i++) _watch[i] = stars.View.Direction[stars.Galaxy.Neighbours(stars.Here)[i]];
        }
        var space = _game.Orbit?.Space;
        bool inSpace = space?.World?.Environment != null;
        var material = (inSpace ? space.Environment : _game.Surface.Sky.Environment).Sky.SkyMaterial as ShaderMaterial;
        Basis turn = (Basis)material.GetShaderParameter("star_turn");
        Basis sky = inSpace ? Basis.FromEuler(space.Environment.SkyRotation) : Basis.Identity;
        var now = new N.Vector3[3];
        for (int i = 0; i < 3; i++)
        {
            Vector3 w = sky * (turn.Inverse() * new Vector3(_watch[i].X, _watch[i].Y, _watch[i].Z));
            now[i] = new N.Vector3(w.X, w.Y, w.Z);
        }
        float y = _game.Survivor?.Seated?.Body.Position.Y ?? 0f;
        string where = inSpace ? "space" : _game.InOrbit ? "over the cap" : y > 300f ? "under the cap" : "the ground";
        if (where == "the ground") _sawSurface = true;
        else if (where == "under the cap") _sawUnder = true;
        else if (where == "over the cap") _sawOver = true;
        else _sawSpace = true;
        // Measured from the seat: getting there is staged, and walking a kilometre on so small a world turns the sky too.
        if (_last != null && _game.Survivor?.Seated != null)
        {
            double step = 0;
            for (int i = 0; i < 3; i++) step = Math.Max(step, Angle(now[i], _last[i]));
            if (step > _worstStep) (_worstStep, _worstAt) = (step, $"{where}, {y:F0} m");
        }
        if (_game.Orbit is { } orbit)
        {
            // The truth, from the one function the map and the visor use.
            Vector3 say = _game.Spaceflight.LookToward(stars.Galaxy.Neighbours(stars.Here)[0]);
            _truth[where] = Math.Max(_truth.GetValueOrDefault(where), inSpace || _game.InOrbit ? Angle(new N.Vector3(say.X, say.Y, say.Z), now[0]) : 0);
        }
        _last = now;
    }

    void Finish()
    {
        Check(_sawSurface && _sawUnder && _sawOver && _sawSpace, $"the climb went from the ground, under the cap and over it, to space ({_sawSurface}, {_sawUnder}, {_sawOver}, {_sawSpace})");
        Check(_worstStep < 0.5, $"no star jumps anywhere on the way: worst step in one tick {_worstStep:F3} degrees ({_worstAt})");
        foreach (var (where, angle) in _truth) Check(angle < 0.5, $"{where}: the sky's own uniforms agree with the map's direction to a star, worst {angle:F3} degrees");
        GalaxySky s = _game.Surface.Sky.Stars;
        Check(s.Galaxy.Stars[s.Home].Kind == SkyRig.KindOf(_game.Surface.Planet.Sky.Star), $"this world's home star is a {s.Galaxy.Stars[s.Home].Kind}, the class of its own sun; id {s.Home}, {s.Galaxy.Name(s.Home)}");
        GD.Print($"timings: galaxy made in {s.MakeMs:F0} ms and sky baked in {s.BakeMs:F0} ms on workers; {s.UploadMs:F1} ms on the main thread to upload {s.TextureBytes / 1024} KB");
        Check(s.UploadMs < 12.0, "the upload is no visible hitch: one texture a frame");
        Report();
    }

    void Report()
    {
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {_clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        Wildlife.Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    static double Angle(N.Vector3 a, N.Vector3 b) => Math.Atan2(N.Vector3.Cross(a, b).Length(), N.Vector3.Dot(a, b)) * 180.0 / Math.PI;

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
    }
}
