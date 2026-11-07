using System;
using System.Diagnostics;
using System.Linq;
using Godot;
using Slice.Orbit;
using N = System.Numerics;

namespace Slice.Tests;

/// <summary>
/// The sphere-to-map mapping (<see cref="GlobeMap"/>): its pure checks (round trips, descents at
/// eight points with both poles and the seam among them, the seam and a pole crossing), and checks
/// against the engine itself, so the globe the player sees and the map they land on agree: every
/// vertex of Godot's own SphereMesh points where the mapping says its texture coordinate is (the
/// globe's colour map is planet-generator's ColorMap, whose pixel (u, v) is the map point
/// (u·width, v·height)).
///
/// Run: .\tools\test.ps1 -Project prototypes/slice -Scene res://tests/GlobeTest.tscn
/// </summary>
public partial class GlobeTest : Node
{
    int _failures, _passes;

    public override void _Ready()
    {
        var clock = Stopwatch.StartNew();
        const double width = 16000, height = 8000;
        foreach (var (ok, text) in GlobeChecks.Run(width, height)) Check(ok, text);
        SphereMeshAgrees(width, height);
        SheetLooksUpTheMap(width, height);
        TheCap();
        TheSheetNeverWraps();
        OneSystem();
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    /// <summary>
    /// Godot's SphereMesh carries the globe's texture: a vertex with texture coordinate (u, v) shows
    /// the map point (u·width, v·height). The mapping must put that map point in the same direction
    /// as the vertex, or the place under a descending vessel isn't the place it lands on.
    /// </summary>
    void SphereMeshAgrees(double width, double height)
    {
        var globe = new GlobeMap(width, height, 1);
        var mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 48, Rings = 24 };
        Godot.Collections.Array arrays = mesh.GetMeshArrays();
        var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var uvs = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
        double worst = 0;
        int checkedCount = 0;
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 v = vertices[i];
            if (v.Length() < 0.5f) continue;
            Vector3 d = v.Normalized();
            // At a pole every longitude is the same point.
            N.Vector3 m = globe.Direction(uvs[i].X * width, uvs[i].Y * height);
            double angle = Math.Acos(Math.Clamp(d.Dot(new Vector3(m.X, m.Y, m.Z)), -1f, 1f)) * 180 / Math.PI;
            worst = Math.Max(worst, angle);
            checkedCount++;
        }
        Check(checkedCount > 1000 && worst < 0.5,
            $"globe: all {checkedCount} vertices of Godot's SphereMesh point where the mapping puts their texture's map point (worst {worst:F3}°)");

        // East is east: a quarter of the way round the map from x = 0 is the globe's +X side (u runs east).
        N.Vector3 quarter = globe.Direction(width / 4, height / 2);
        Check(quarter.X > 0.99f, $"globe: a quarter of the map east of the seam, on the equator, is the globe's +X side ({quarter.X:F3}, {quarter.Y:F3}, {quarter.Z:F3})");
        N.Vector3 north = globe.Direction(width / 3, 0);
        Check(north.Y > 0.999f, $"globe: the map's top row is the north pole ({north.Y:F4} up)");
    }

    /// <summary>The sheet's shader finds the map point of a direction by this same sum: it must be the mapping's own inverse.</summary>
    void SheetLooksUpTheMap(double width, double height)
    {
        var globe = new GlobeMap(width, height, width / Math.Tau);
        var rng = new Random();
        double worst = 0;
        for (int i = 0; i < 400; i++)
        {
            double x = rng.NextDouble() * width, z = (0.01 + 0.98 * rng.NextDouble()) * height;
            N.Vector3 d = globe.Direction(x, z);
            double u = Math.Atan2(d.X, d.Z) / Math.Tau, v = Math.Acos(Math.Clamp(d.Y, -1f, 1f)) / Math.PI;
            double dx = Math.Abs(u - Math.Round(u - x / width) - x / width) * width;
            worst = Math.Max(worst, Math.Max(dx, Math.Abs(v * height - z)));
        }
        Check(worst < 3, $"globe: the sheet's lookup of a direction is the map point it came from, within {worst:F2} m at 400 random places");
    }

    /// <summary>The cap: orbit starts at 1,060 m on every world, and the land stands under it on fresh worlds of every type, tall or not.</summary>
    void TheCap()
    {
        OrbitRules rules = OrbitRules.Shared;
        foreach (string air in Enum.GetNames<PlanetSurface.Skies.AirKind>())
            Check(rules.OrbitAltitude(air) == 1060f, $"globe: orbit starts at 1,060 m under {air} air ({rules.OrbitAltitude(air):F0} m), whatever the world's peaks");
        var worlds = new Slice.Game.WorldChoice[20];
        for (int i = 0; i < worlds.Length; i++) worlds[i] = Slice.Game.WorldChoice.Fresh(Slice.Game.WorldChoice.Types[i % Slice.Game.WorldChoice.Types.Count].Key);
        var highest = new float[worlds.Length];
        System.Threading.Tasks.Parallel.For(0, worlds.Length, i => highest[i] = Planets.Gen.PlanetGenerator.Generate(worlds[i].Spec()).Elevation.Max());
        int tallest = Array.IndexOf(highest, highest.Max());
        Check(highest.Max() < 1060f - 60f, $"globe: the land on {worlds.Length} fresh worlds stands well under the cap: the tallest peak {highest[tallest]:F0} m ({worlds[tallest]}), the average {highest.Average():F0} m");
    }

    /// <summary>
    /// The sheet is far wider than the globe low down. Past the far side a lookup that keeps going
    /// shows the globe over again, and everything at the far side smears into rings: its rim must
    /// stand short of that at every height, at the map's scale under you, and on the true sphere
    /// it must be the sphere.
    /// </summary>
    void TheSheetNeverWraps()
    {
        OrbitRules rules = OrbitRules.Shared;
        var c = rules.Crossing;
        float radius = 16000f / MathF.Tau, worst = 0f;
        for (float y = 1060f; y <= (float)c.TrueGlobeAt; y += 40f)
        {
            float climb = (y - 1060f) / ((float)c.TrueGlobeAt - 1060f);
            float rho = 1f / Mathf.Lerp(1f / (float)c.FlatCurve, 1f / radius, climb);
            worst = MathF.Max(worst, Globe.RimOf(rho, y) / Globe.ArcAt(MathF.PI * 0.99f, rho, radius));
        }
        Check(worst < 1f, $"globe: the sheet's rim stands short of the far side of the globe at every height ({worst * 100f:F0}% of the way there at worst)");
        float under = Globe.ArcAt(0.01f, 300000f, radius) / (0.01f * radius), round = Globe.ArcAt(1.5f, radius, radius) / (1.5f * radius);
        Check(MathF.Abs(under - 1f) < 0.01f && MathF.Abs(round - 1f) < 0.001f, $"globe: the sheet's lookup is at the map's scale under you ({under:F3}) and is the sphere itself on the sphere ({round:F3})");
    }

    /// <summary>
    /// One model of the system, on fresh worlds of every type: every body is a real thing with a place
    /// at every moment, seen in the same direction from the ground, just under the cap, just over it
    /// and high in space (but for the true parallax of the eye having moved), the world's type agrees
    /// with its orbit, and any point on a world resolves up to its star.
    /// </summary>
    void OneSystem()
    {
        const double width = 16000, radius = width / Math.Tau;
        var map = new GlobeMap(16000, 8000, radius);
        var rng = new Random();
        double worstFrame = 0, worstSight = 0, worstNoon = 0;
        int bodies = 0, fewest = int.MaxValue, misplaced = 0, moved = 0, moving = 0, unresolved = 0;
        string misplacedWorld = "", unfitWorld = "";
        int unfit = 0;
        int giants = 0, ringed = 0, moons = 0;
        for (int w = 0; w < 25; w++)
        {
            var world = Slice.Game.WorldChoice.Fresh(Slice.Game.WorldChoice.Types[w % Slice.Game.WorldChoice.Types.Count].Key);
            Planets.Gen.PlanetSpec spec = world.Spec();
            var sky = PlanetSurface.Skies.SkySystem.For(spec);
            var s = PlanetSurface.Skies.StarSystem.For(spec, sky, width);
            fewest = Math.Min(fewest, s.Bodies.Count(b => b.Kind is not (PlanetSurface.Skies.OrbiterKind.Belt or PlanetSurface.Skies.OrbiterKind.Star)));
            giants += s.Neighbour != null ? 1 : 0;
            ringed += s.Neighbour?.Rings == true ? 1 : 0;
            moons += s.Bodies.Count(b => b.Parent == s.Home);
            string fault = s.StartFault();
            if (fault != null) { unfit++; unfitWorld = $"{world}: {fault}"; }
            bool wrong = spec.Archetype switch
            {
                Planets.Gen.Archetype.Lava => s.Home.Zone != 1,
                Planets.Gen.Archetype.Ice => s.Home.Zone != 6,
                Planets.Gen.Archetype.Temperate => s.Home.Zone != 4,
                Planets.Gen.Archetype.Arid => s.Home.Zone is not (2 or 3),
                _ => s.Home.Zone is not (3 or 4 or 5),
            };
            if (wrong) { misplaced++; misplacedWorld = $"{world}: {s.Home.Zone}, {s.Home.Temperature:F0} K"; }

            double x = rng.NextDouble() * 16000, z = (0.05 + 0.9 * rng.NextDouble()) * 8000, days = rng.NextDouble() * 400;
            var (east, up, south) = PlanetSurface.Skies.StarSystem.FrameAt(map.Longitude(x), map.Colatitude(z));
            N.Quaternion frame = map.Frame(x, z);
            double Off(PlanetSurface.Skies.D3 a, N.Vector3 axis) => G(a).AngleTo(G(N.Vector3.Transform(axis, frame))) * 180 / Math.PI;
            worstFrame = Math.Max(worstFrame, Math.Max(Off(east, N.Vector3.UnitX), Math.Max(Off(up, N.Vector3.UnitY), Off(south, N.Vector3.UnitZ))));

            // At the same moment, in the globe's frame, from four heights over one point.
            foreach (PlanetSurface.Skies.Orbiter b in s.Bodies)
            {
                if (b.Kind == PlanetSurface.Skies.OrbiterKind.Belt || b == s.Home) continue;
                bodies++;
                PlanetSurface.Skies.Sighting ground = s.See(b, days, up * radius);
                foreach (double h in new[] { 1050.0, 1070.0, 5000.0 })
                {
                    PlanetSurface.Skies.Sighting there = s.See(b, days, up * (radius + h));
                    double between = G(ground.Direction).AngleTo(G(there.Direction)) * 180 / Math.PI;
                    worstSight = Math.Max(worstSight, between - Math.Asin(h / Math.Min(there.Distance, ground.Distance)) * 180 / Math.PI);
                }
                moving++;
                if ((s.Position(b, days) - s.Position(b, days + 1.0)).Length > 1.0 || b.Kind == PlanetSurface.Skies.OrbiterKind.Star) moved++;
            }

            // The clock tells solar time at the map's middle: at noon there the sun has no east in it.
            var (e2, u2, _) = PlanetSurface.Skies.StarSystem.FrameAt(Math.PI, Math.PI / 3);
            worstNoon = Math.Max(worstNoon, Math.Abs(s.ToStar(Math.Floor(days) + 0.5, u2 * radius).Dot(e2)));
            var at = s.Locate(1.0, 1.0, 500.0, days);
            if (s.Up(s.Home).Last() != s.Star || Math.Abs((at.InSystem - s.Position(s.Home, days)).Length - radius - 500.0) > 0.01) unresolved++;
        }
        Check(worstFrame < 0.01, $"globe: the sky's frame over a map point is the globe's own ({worstFrame:F4} degrees off at worst)");
        Check(worstSight < 0.02, $"globe: {bodies} bodies on fresh worlds stand in the same direction from the ground, under the cap, over it and in space, past the true parallax ({worstSight:F4} degrees at worst; {giants} worlds with a giant, {ringed} ringed, {moons} moons)");
        Check(giants > 0 && moons > 0, $"globe: the fresh worlds had giants and moons to check ({giants}, {moons})");
        Check(fewest >= 8, $"globe: every system has at least eight worlds, moons among them (the fewest {fewest})");
        Check(misplaced == 0, $"globe: a world's type agrees with its orbit round its star ({misplaced} did not{(misplaced > 0 ? ": " + misplacedWorld : "")})");
        Check(worstNoon < 0.002, $"globe: the clock's noon is the sun on the meridian at the map's middle ({worstNoon:F4} off)");
        Check(moved == moving, $"globe: everything moves along its orbit from one day to the next ({moved} of {moving})");
        Check(unfit == 0, $"globe: every start has a moon in reach of thrusters with warp fuel and the drive's materials, and hyperdrive fuel, hyperdrive materials and capital ship materials a warp away ({unfit} did not{(unfit > 0 ? ": " + unfitWorld : "")})");
        Check(unresolved == 0, $"globe: any point on a world resolves up to its orbit, its place in the system and its star ({unresolved} did not)");
    }

    static Vector3 G(PlanetSurface.Skies.D3 d) => new((float)d.X, (float)d.Y, (float)d.Z);

    static Vector3 G(N.Vector3 d) => new(d.X, d.Y, d.Z);

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
    }
}
