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
        RingsCrossInOrder();
        OrbitsStandApart();
        RingsFitTheirSpheres();
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

    /// <summary>
    /// A ring stands on its sphere: same centre, its inner edge clear of the surface. For a world's
    /// ring that is the sphere you can see at every height through the crossing and the sheet's
    /// curve, and the true globe from the true globe's height; for a giant's, one mesh and the
    /// sky's shader, from the same radii.
    /// </summary>
    void RingsFitTheirSpheres()
    {
        OrbitRules.CrossingRules c = OrbitRules.Shared.Crossing;
        const float radius = 16000f / MathF.Tau, cap = 1060f;
        float worst = float.MaxValue, trueOff = 0f, under = 0f;
        for (float y = cap + Globe.RingFitsBy; y <= 12000f; y += 20f)
        {
            float climb = Mathf.Clamp((y - cap) / (c.TrueGlobeAt - cap), 0f, 1f);
            float sheet = 1f / Mathf.Lerp(1f / c.FlatCurve, 1f / radius, climb), ring = Globe.RingSphere(sheet, radius, y - cap);
            worst = MathF.Min(worst, Globe.RingInner * ring / sheet);
            if (y >= c.TrueGlobeAt) trueOff = MathF.Max(trueOff, MathF.Abs(ring - radius));
        }
        for (float y = 0f; y <= cap; y += 20f) under = MathF.Max(under, MathF.Abs(Globe.RingSphere(300000f, radius, y - cap) - radius));
        Check(worst > 1.4f, $"globe: from {cap + Globe.RingFitsBy:F0} m up a world's ring stands round the sheet you can see: its inner edge at {worst:F2} radii at the least, never inside the disc");
        Check(trueOff < 0.01f && under < 0.01f, $"globe: and it is the true globe's ring on the ground and in true orbit ({under:F2}, {trueOff:F2} m off)");
        foreach (var (inner, outer, what) in new[] { (Globe.RingInner, Globe.RingOuter, "a world's"), (SpaceSky.RingInner, SpaceSky.RingOuter, "a giant's") })
        {
            var vertices = Globe.RingMesh(inner, outer, 160).GetFaces();
            float least = vertices.Min(v => v.Length()), most = vertices.Max(v => v.Length());
            Vector3 middle = vertices.Aggregate(Vector3.Zero, (sum, v) => sum + v) / vertices.Length;
            Check(least > 1f && most <= outer * 1.001f && middle.Length() < 1e-3f, $"globe: {what} ring mesh is centred on its body ({middle.Length():F4} off) and clear of it: {least:F2} to {most:F2} radii");
        }
        string sky = Godot.FileAccess.GetFileAsString("res://../planet-surface/shaders/sky.gdshader");
        Check(sky.Contains($"smoothstep({SpaceSky.RingInner:F2}, ") && sky.Contains($"smoothstep(2.35, {SpaceSky.RingOuter:F2}, rr)"), "globe: the sky's shader and space's mesh draw a giant's ring from the same radii");
    }

    /// <summary>
    /// No two worlds share an orbit: on fresh worlds of every type, neighbours round the star stand a
    /// clear step apart (a giant that near is the one this world is a moon of), a moon holds no moons,
    /// moons of one parent keep apart, and no world sits in a belt but in the temperate band.
    /// </summary>
    void OrbitsStandApart()
    {
        const double width = 16000;
        double closest = double.MaxValue, closestMoons = double.MaxValue;
        string closestAt = "", moonAt = "";
        int moonsOfMoons = 0, inBelts = 0, asMoons = 0, cases = 0;
        foreach (string key in Slice.Game.WorldChoice.Types.Select(t => t.Key))
            for (int i = 0; i < 80; i++)
            {
                cases++;
                var world = Slice.Game.WorldChoice.Fresh(key);
                Planets.Gen.PlanetSpec spec = world.Spec();
                var s = PlanetSurface.Skies.StarSystem.For(spec, PlanetSurface.Skies.SkySystem.For(spec), width);
                asMoons += s.Home.Parent != s.Star ? 1 : 0;
                var round = s.Bodies.Where(b => b.Parent == s.Star && b.Kind != PlanetSurface.Skies.OrbiterKind.Belt).OrderBy(b => b.Habitable).ToList();
                for (int k = 1; k < round.Count; k++)
                {
                    double step = round[k].Habitable / round[k - 1].Habitable;
                    if (step < closest) (closest, closestAt) = (step, $"{round[k - 1].Name} and {round[k].Name} on {world}");
                }
                foreach (var b in s.Bodies)
                {
                    if (b.Kind == PlanetSurface.Skies.OrbiterKind.Moon && b.Parent.Kind == PlanetSurface.Skies.OrbiterKind.Moon) moonsOfMoons++;
                    if (b.Kind == PlanetSurface.Skies.OrbiterKind.Belt)
                        inBelts += round.Count(w => w.Habitable is < 0.95 or > 1.7 && Math.Abs(w.Orbit - b.Orbit) < b.BeltWidth * 0.5);
                    var moons = s.Under(b).Where(m => m.Kind == PlanetSurface.Skies.OrbiterKind.Moon && m != s.FuelMoon || (m == s.FuelMoon && m.Orbit != s.Home.Orbit)).OrderBy(m => m.Orbit).ToList();
                    for (int k = 1; k < moons.Count; k++)
                        if (moons[k].Orbit / moons[k - 1].Orbit < closestMoons) (closestMoons, moonAt) = (moons[k].Orbit / moons[k - 1].Orbit, $"{moons[k - 1].Name} and {moons[k].Name} on {world}");
                }
            }
        Check(closest >= PlanetSurface.Skies.StarSystem.LeastOrbitStep * 0.999, $"globe: no two worlds share an orbit round the star in {cases} fresh systems: the closest neighbours stand {closest:F2} times apart ({closestAt}); {asMoons} homes are a giant's moon");
        Check(closestMoons >= PlanetSurface.Skies.StarSystem.LeastMoonStep * 0.999, $"globe: moons of one parent keep apart, the closest {closestMoons:F2} times ({moonAt}), and only a start's sister moon shares its orbit");
        Check(moonsOfMoons == 0, $"globe: a moon holds no moons of its own ({moonsOfMoons} did)");
        Check(inBelts == 0, $"globe: no world sits in a belt outside the temperate band ({inBelts} did)");
    }

    /// <summary>
    /// The sky's order for a giant's rings, against the true solid: from random eyes on both sides of
    /// the plane and nearly in it, a ring seen over the disc must truly be nearer than the giant, and
    /// one drawn behind it truly further. Only the near arc crosses in front.
    /// </summary>
    void RingsCrossInOrder()
    {
        var rng = new Random();
        int wrong = 0, front = 0, behind = 0;
        for (int i = 0; i < 40000; i++)
        {
            // A giant one away, its radius small against that; a ring plane at any tilt to the view.
            var centre = new N.Vector3(0, 0, 1);
            float radius = 0.05f + 0.15f * (float)rng.NextDouble(), tilt = i % 4 == 0 ? (float)(rng.NextDouble() - 0.5) * 0.1f : (float)(rng.NextDouble() - 0.5) * 3f;
            var axis = N.Vector3.Normalize(new N.Vector3((float)(rng.NextDouble() - 0.5) * 0.5f, MathF.Cos(tilt), MathF.Sin(tilt)));
            var dir = N.Vector3.Normalize(centre + new N.Vector3((float)(rng.NextDouble() - 0.5), (float)(rng.NextDouble() - 0.5), 0) * 2f * radius);
            float t = N.Vector3.Dot(centre, axis) / N.Vector3.Dot(dir, axis);
            float rr = (dir * t - centre).Length() / radius;
            if (t <= 0f || rr is < 1.45f or > 2.42f) continue;
            // The ray into the solid sphere: where it first touches, if it does.
            float b = N.Vector3.Dot(dir, centre), past = b * b - 1f + radius * radius;
            if (past <= 0f) continue;
            float surface = b - MathF.Sqrt(past);
            bool drawnInFront = t < 1f, trulyNearer = t < surface;
            if (drawnInFront != trulyNearer) wrong++;
            if (drawnInFront) front++;
            else behind++;
        }
        Check(wrong == 0 && front > 100 && behind > 100, $"globe: over a giant's disc only the near arc is drawn in front, from both sides of the ring plane and nearly in it ({front} in front, {behind} behind, {wrong} wrong)");
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
