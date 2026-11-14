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
/// (uÂ·width, vÂ·height)).
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
        BodiesWearTheirOwnWorlds();
        TheCloudsRepeatExactly();
        try { TheWeatherShows(); }
        catch (Exception e) { Check(false, $"globe weather: threw {e}"); }
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        GalHack.Testing.Backstop.Quit(this, _failures == 0 ? 0 : 1);
    }

    /// <summary>
    /// Godot's SphereMesh carries the globe's texture: a vertex with texture coordinate (u, v) shows
    /// the map point (uÂ·width, vÂ·height). The mapping must put that map point in the same direction
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
            $"globe: all {checkedCount} vertices of Godot's SphereMesh point where the mapping puts their texture's map point (worst {worst:F3}Â°)");

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
        for (float y = 1060f; y <= 1060f + c.Band; y += 40f)
        {
            float climb = (y - 1060f) / c.Band;
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
        Check(unfit == 0, $"globe: every start has a vacuum moon in reach of thrusters with the drive's materials, and hyperdrive materials and capital ship materials a warp away ({unfit} did not{(unfit > 0 ? ": " + unfitWorld : "")})");
        Check(unresolved == 0, $"globe: any point on a world resolves up to its orbit, its place in the system and its star ({unresolved} did not)");
    }

    /// <summary>
    /// What you see from afar is what you would land on: every world's type is what its zone makes
    /// it, a coarse map shows the land and seas of the full one, and the surfaces are cheap, and
    /// bounded whatever the system.
    /// </summary>
    void BodiesWearTheirOwnWorlds()
    {
        const double width = 16000;
        int bare = 0, unfit = 0, worlds = 0, most = 0;
        string unfitAt = "";
        var picked = new System.Collections.Generic.List<PlanetSurface.Skies.Orbiter>();
        foreach (var (key, _, _, _) in Slice.Game.WorldChoice.Types)
            for (int i = 0; i < 40; i++)
            {
                var world = Slice.Game.WorldChoice.Fresh(key);
                Planets.Gen.PlanetSpec spec = world.Spec();
                var s = PlanetSurface.Skies.StarSystem.For(spec, PlanetSurface.Skies.SkySystem.For(spec), width);
                most = Math.Max(most, s.Bodies.Count(b => b != s.Home && (b.Spec != null || b.Kind == PlanetSurface.Skies.OrbiterKind.GasGiant)));
                foreach (var b in s.Bodies)
                {
                    if (b == s.Home || b.Kind is not (PlanetSurface.Skies.OrbiterKind.Planet or PlanetSurface.Skies.OrbiterKind.Moon)) continue;
                    worlds++;
                    if (b.Spec == null) { bare++; continue; }
                    // A body too small to keep its air is bare rock (or ice, out in the cold) wherever it stands (StarSystem.Dress).
                    bool fits = b.Kind == PlanetSurface.Skies.OrbiterKind.Moon || b.Spec.Airless && b.Spec.Archetype is Planets.Gen.Archetype.Arid or Planets.Gen.Archetype.Ice || b.Spec.Archetype switch
                    {
                        Planets.Gen.Archetype.Lava => b.Zone == 1,
                        Planets.Gen.Archetype.Arid => b.Zone is 2 or 3,
                        Planets.Gen.Archetype.Temperate => b.Zone is 3 or 4 or 5,
                        Planets.Gen.Archetype.Alien => b.Zone is 3 or 4 or 5,
                        _ => b.Zone >= 5,
                    };
                    if (!fits) { unfit++; unfitAt = $"{b.Name}: {b.Spec.Archetype} in zone {b.Zone}"; }
                    if (i == 0 && picked.Count < 6 && b.Spec.WidthKm <= 16 && picked.TrueForAll(p => p.Spec.Archetype != b.Spec.Archetype)) picked.Add(b);
                }
            }
        Check(bare == 0 && unfit == 0, $"globe: all {worlds} worlds of fresh systems have the type their zone makes them ({bare} with none, {unfit} out of place{(unfit > 0 ? ": " + unfitAt : "")})");
        Check(most <= PlanetSurface.Skies.BodySurfaces.Layers, $"globe: {PlanetSurface.Skies.BodySurfaces.MemoryBytes / 1048576.0:F1} MB holds every surface: the most any system wanted was {most} of {PlanetSurface.Skies.BodySurfaces.Layers}");
        double worstMatch = 1, worstMs = 0, totalMs = 0;
        string matchAt = "";
        foreach (var b in picked)
        {
            var clock = Stopwatch.StartNew();
            PlanetSurface.Skies.BodySurfaces.Paint(b);
            double ms = clock.Elapsed.TotalMilliseconds;
            worstMs = Math.Max(worstMs, ms);
            totalMs += ms;
            Planets.Gen.WorldMap coarse = PlanetSurface.Skies.BodySurfaces.Coarse(b.Spec), full = Planets.Gen.PlanetGenerator.Generate(b.Spec);
            int same = 0, count = 0;
            for (int j = 0; j < coarse.Height; j += 3)
                for (int i = 0; i < coarse.Width; i += 3)
                {
                    int fx = (int)((i + 0.5) / coarse.Width * full.Width), fz = (int)((j + 0.5) / coarse.Height * full.Height);
                    count++;
                    if (coarse.Elevation[j * coarse.Width + i] > 0f == full.Elevation[fz * full.Width + fx] > 0f) same++;
                }
            if ((double)same / count < worstMatch) (worstMatch, matchAt) = ((double)same / count, $"{b.Spec.Archetype} on {b.Spec.WidthKm} km");
        }
        Check(picked.Count >= 3 && worstMatch > 0.85, $"globe: {picked.Count} coarse maps show the land and seas of the full ones: {worstMatch:P0} of points agree at worst ({matchAt})");
        Check(worstMs < 3000, $"globe: a surface takes {totalMs / Math.Max(picked.Count, 1):F0} ms on its own thread, {worstMs:F0} ms at worst");
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
            float climb = Mathf.Clamp((y - cap) / c.Band, 0f, 1f);
            float sheet = 1f / Mathf.Lerp(1f / c.FlatCurve, 1f / radius, climb), ring = Globe.RingSphere(sheet, radius, y - cap);
            worst = MathF.Min(worst, Globe.RingInner * ring / sheet);
            if (y >= cap + c.Band) trueOff = MathF.Max(trueOff, MathF.Abs(ring - radius));
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

    /// <summary>
    /// The globe's clouds are the live weather's, at the right places: on a fresh world a storm front
    /// and its cells, made by the weather itself, stand on the globe's weather layer where the weather
    /// has them (the front's edge where its deck begins, each cell's tower over the cell), the layer
    /// follows the front as it advances a minute on, and filling it costs a frame under a millisecond.
    /// </summary>
    void TheWeatherShows()
    {
        var rng = new Random();
        var data = Weather.Model.WeatherData.Parse(System.IO.File.ReadAllText(ProjectSettings.GlobalizePath("res://") + "../weather/data/weather.json"));
        var kinds = new[] { Planets.Gen.Archetype.Temperate, Planets.Gen.Archetype.Alien, Planets.Gen.Archetype.Arid };
        var spec = Planets.Gen.PlanetSpec.Roll((ulong)rng.NextInt64(), kinds[rng.Next(kinds.Length)], rng.Next(Planets.Gen.PlanetSpec.VariantCount), 16);
        Planets.Gen.WorldMap map = Planets.Gen.PlanetGenerator.Generate(spec);
        var size = new Vector2((float)map.WidthMeters, (float)map.HeightMeters);
        var sim = new Weather.Model.WeatherSim(data, Slice.Surface.SurfaceWeather.ArchetypeFor(spec), (ulong)rng.NextInt64(), Weather.Model.WeatherKind.Thunderstorm)
        {
            Land = Slice.Surface.SurfaceWeather.LandOf(map, data.Regions), WorldSize = size, MeanWarmth = map.Temperature.Average(),
        };
        var sky = PlanetSurface.Skies.SkySystem.For(spec);
        var observer = new Vector3((float)(rng.NextDouble() * size.X), 0f, (float)(rng.NextDouble() * size.Y));
        // No seam anywhere in what the globe is drawn from, before a front lays a line of its own across it:
        // in a weather that differs from place to place (fog pooling in damp low ground, the rest overcast).
        sim.Snap(Weather.Model.WeatherKind.Fog);
        var layer = new GlobeWeather(size);
        layer.FillAll(sim, sky);
        NothingChangesAcrossSeams(spec, map, layer);
        sim.Snap(Weather.Model.WeatherKind.Thunderstorm);
        Weather.Model.Front f = sim.StartFront(Weather.Model.FrontKind.Storm, observer, 1500f);
        layer.FillAll(sim, sky);

        // Across the front through the observer's stretch of it: where the layer's cloud begins, against the sky's own sums at full resolution.
        var heading = new Vector3(f.Heading.X, 0f, f.Heading.Y);
        float along = f.Along(observer);
        Vector3 edge = f.EdgeAt(along, observer);
        var cells = sim.Cells.Where(c => c.Strength * sim.Current.Cellular >= 0.04f).Take(4)
            .Select(c => new GlobeWeather.Cell(new Vector2(Wrap(c.Position.X, size.X), Wrap(c.Position.Z, size.Y)), c.Radius, c.Strength * sim.Current.Cellular)).ToList();
        float LayerEdge() => Crossing(t => Bilinear(layer, edge + heading * t).Extra);
        float TrueEdge() => Crossing(t => GlobeWeather.At(sim, sky, edge + heading * t, size, cells).Extra);
        float onLayer = LayerEdge(), truth = TrueEdge();
        Check(MathF.Abs(onLayer - truth) <= GlobeWeather.TexelMetres * 1.5f && truth is > -900f and < 800f,
            $"globe weather: the front's deck begins on the globe's layer {onLayer:F0} m from its edge, where the sky's own sums have it ({truth:F0} m; its edge where the weather has it, its deck {f.Depth / 1000f:F1} km deep, {f.Period / 1000f:F0} km long)");
        var behind = Bilinear(layer, edge - heading * MathF.Min(1600f, f.Depth * 0.45f));
        var ahead = Bilinear(layer, edge + heading * 1500f);
        Check(GlobeWeather.Coverage(0.5f, behind) > 0.15f && GlobeWeather.Coverage(0.5f, behind) - GlobeWeather.Coverage(0.5f, ahead) > 0.6f,
            $"globe weather: thick cloud behind the front's edge ({GlobeWeather.Coverage(0.5f, behind):F2}), open air ahead of it ({GlobeWeather.Coverage(0.5f, ahead):F2}), lightning {behind.Lightning:F1} a minute under it");

        // Each cell's tower stands over the cell.
        float worstCell = 0f;
        int shown = 0;
        foreach (GlobeWeather.Cell c in cells)
        {
            var (ci, cj) = layer.TexelOf(c.At.X, c.At.Y);
            float best = -1f;
            int reach = (int)MathF.Ceiling(c.Radius / GlobeWeather.TexelMetres);
            for (int dj = -reach; dj <= reach; dj++)
            for (int di = -reach; di <= reach; di++)
            {
                int i = ((ci + di) % layer.Width + layer.Width) % layer.Width, j = Math.Clamp(cj + dj, 0, layer.Height - 1);
                best = MathF.Max(best, layer.Read(i, j).Tower);
            }
            // The tower's top is flat over its core: the cell stands where it is at its highest.
            float top = Bilinear(layer, new Vector3(c.At.X, 0f, c.At.Y)).Tower;
            worstCell = MathF.Max(worstCell, 1f - top / MathF.Max(best, 1e-3f));
            if (best > 0.3f) shown++;
        }
        Check(cells.Count > 0 && shown == cells.Count && worstCell < 0.05f,
            $"globe weather: all {cells.Count} of the front's storm cells stand on the layer as towers ({shown} tall), each cell under its tower's top ({worstCell:P0} short of it at worst)");

        // A minute on: the front has advanced, and so has its deck on the globe.
        float seconds = MathF.Min(60f, 800f / f.Speed);
        for (int i = 0; i < (int)(seconds * 2f); i++) sim.Step(0.5f, observer);
        cells = sim.Cells.Where(c => c.Strength * sim.Current.Cellular >= 0.04f).Take(4)
            .Select(c => new GlobeWeather.Cell(new Vector2(Wrap(c.Position.X, size.X), Wrap(c.Position.Z, size.Y)), c.Radius, c.Strength * sim.Current.Cellular)).ToList();
        layer.FillAll(sim, sky);
        float later = LayerEdge(), moved = later - onLayer, expected = f.Speed * (int)(seconds * 2f) * 0.5f;
        Check(sim.Front == f && MathF.Abs(moved - expected) <= GlobeWeather.TexelMetres * 1.5f && MathF.Abs(later - TrueEdge()) <= GlobeWeather.TexelMetres * 1.5f,
            $"globe weather: {seconds:F0} s on the front's deck on the globe has moved {moved:F0} m the way it advances, as the front has ({expected:F0} m at {f.Speed:F1} m/s)");

        // Filling it a frame at a time stays inside its budget.
        var fresh = new GlobeWeather(size);
        var times = new System.Collections.Generic.List<double>();
        var clock = Stopwatch.StartNew();
        while (true)
        {
            var frame = Stopwatch.StartNew();
            bool done = fresh.Fill(sim, sky, GlobeWeather.BudgetMs);
            times.Add(frame.Elapsed.TotalMilliseconds);
            if (done || times.Count > 100000) break;
        }
        times.Sort();
        double typical = times[(int)(times.Count * 0.95)], worst = times[^1];
        int over = times.Count(t => t > 1.5);
        // A frame or two the machine itself stalls (another process, a collection) doesn't count against it.
        Check(typical < 1.0 && over <= 2 && times.Count * (1000.0 / 60.0) < 20000,
            $"globe weather: a pass over the whole map ({fresh.Width} x {fresh.Height} texels) takes {clock.Elapsed.TotalMilliseconds:F0} ms of work over {times.Count} frames: {typical:F2} ms in 95% of them, {over} over 1.5 ms (the worst {worst:F2} ms)");
    }

    /// <summary>
    /// The globe's maps have no seam: across the equator and round through the map's edge in
    /// longitude, the colour map, the paint's cloud map and the weather layer (no front on it, which
    /// is a line of its own) are the same kind of thing either side: a band's statistics (each
    /// channel's mean and spread) just over the equator against just under it, and just west of the
    /// map's edge against just east, differ no more than neighbouring bands anywhere else do.
    /// </summary>
    void NothingChangesAcrossSeams(Planets.Gen.PlanetSpec spec, Planets.Gen.WorldMap map, GlobeWeather layer)
    {
        GlobePaint paint = GlobePaint.Make(spec, map, OrbitRules.Shared.Globe);
        var weather = new byte[layer.Width * layer.Height * 4];
        Array.Copy(layer.Finished, weather, weather.Length);
        foreach (var (what, data, w, h, channels) in new (string, byte[], int, int, int)[]
        {
            ("colour map", paint.Albedo.GetData(), paint.Albedo.GetWidth(), paint.Albedo.GetHeight(), 3),
            ("cloud map", paint.Clouds.GetData(), paint.Clouds.GetWidth(), paint.Clouds.GetHeight(), 4),
            ("weather layer", weather, layer.Width, layer.Height, 4),
        })
        {
            // A band's mean and spread in each channel: rows [y0, y1) whole, or columns [x0, x1) whole (wrapping).
            double[] Stats(int x0, int x1, int y0, int y1)
            {
                var st = new double[channels * 2];
                int n = 0;
                for (int y = y0; y < y1; y++)
                    for (int xi = x0; xi < x1; xi++)
                    {
                        int x = ((xi % w) + w) % w;
                        for (int c = 0; c < channels; c++)
                        {
                            double v = data[(y * w + x) * 4 + c];
                            st[c * 2] += v;
                            st[c * 2 + 1] += v * v;
                        }
                        n++;
                    }
                for (int c = 0; c < channels; c++)
                {
                    double mean = st[c * 2] / n;
                    st[c * 2 + 1] = Math.Sqrt(Math.Max(st[c * 2 + 1] / n - mean * mean, 0));
                    st[c * 2] = mean;
                }
                return st;
            }
            double Apart(double[] a, double[] b) => a.Zip(b, (p, q) => Math.Abs(p - q)).Max();
            int bh = Math.Max(2, h / 16), bw = Math.Max(2, w / 16);
            // Every pair of neighbouring bands, row-wise and column-wise; the equator's and the map edge's among them.
            var rowPairs = Enumerable.Range(1, h / bh - 1).Select(k => Apart(Stats(0, w, k * bh - bh, k * bh), Stats(0, w, k * bh, k * bh + bh))).ToList();
            var colPairs = Enumerable.Range(1, w / bw - 1).Select(k => Apart(Stats(k * bw - bw, k * bw, 0, h), Stats(k * bw, k * bw + bw, 0, h))).ToList();
            double equator = Apart(Stats(0, w, h / 2 - bh, h / 2), Stats(0, w, h / 2, h / 2 + bh));
            double round = Apart(Stats(w - bw, w, 0, h), Stats(w, w + bw, 0, h));
            double rowMost = rowPairs.Max(), colMost = colPairs.Max();
            // And no step from one pixel to the next there: a seam is a step all along a row or a column.
            double Step(bool rows, int a, int b)
            {
                double sum = 0;
                int n = rows ? w : h;
                for (int i = 0; i < n; i++)
                    for (int c = 0; c < channels; c++)
                        sum += rows ? Math.Abs(data[(a * w + i) * 4 + c] - data[(b * w + i) * 4 + c]) : Math.Abs(data[(i * w + a) * 4 + c] - data[(i * w + b) * 4 + c]);
                return sum / (n * channels);
            }
            var rowSteps = Enumerable.Range(1, h - 1).Select(y => Step(true, y - 1, y)).OrderBy(d => d).ToList();
            var colSteps = Enumerable.Range(1, w - 1).Select(x => Step(false, x - 1, x)).OrderBy(d => d).ToList();
            double rowStep = Step(true, h / 2 - 1, h / 2), colStep = Step(false, w - 1, 0);
            double rowUsual = rowSteps[(int)(rowSteps.Count * 0.9)], colUsual = colSteps[(int)(colSteps.Count * 0.9)];
            Check(equator <= Math.Max(rowMost, 1.0) && round <= Math.Max(colMost * 1.25, 1.0) && rowStep <= Math.Max(rowUsual * 1.5, 1.0) && colStep <= Math.Max(colUsual * 1.5, 1.0),
                $"globe weather: the {what} ({w} x {h}) is the same either side of the equator (bands {bh} rows deep differ by {equator:F1} there, up to {rowMost:F1} between neighbours elsewhere; a step of {rowStep:F2} between its rows, {rowUsual:F2} usual) and either side of its edge round the globe ({round:F1} against up to {colMost:F1}; a step of {colStep:F2}, {colUsual:F2} usual)");
        }
    }

    /// <summary>
    /// The sheet's clouds are one field over the whole map: every noise it reads is read at a whole
    /// number of its tiles to the map's (the sky's noise tile divides the map both ways), so the field
    /// repeats exactly through the map's edge, which runs through the lookup at u = ±0.5, and from pole to pole.
    /// </summary>
    void TheCloudsRepeatExactly()
    {
        var scales = new System.Collections.Generic.List<double>();
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(Globe.CloudShader, @"\bq \* (?:vec2\(([0-9.]+), ([0-9.]+)\)|([0-9.]+))"))
            foreach (int g in new[] { 1, 2, 3 })
                if (m.Groups[g].Success) scales.Add(double.Parse(m.Groups[g].Value, System.Globalization.CultureInfo.InvariantCulture));
        var odd = scales.Where(k => Math.Abs(k - Math.Round(k)) > 1e-9).ToList();
        Check(scales.Count >= 8 && odd.Count == 0 && Globe.CloudShader.Contains("mod(c, cells)"),
            $"globe weather: all {scales.Count} of the clouds' noise reads are whole tiles to the map ({string.Join(", ", scales.Distinct())}), and the lightning's patches a whole number to it: nothing breaks where the map's edge runs round the globe{(odd.Count > 0 ? $" (not: {string.Join(", ", odd)})" : "")}");
    }

    /// <summary>Where along a line (m, from +2000 back to -1600) a value first turns positive, to a few metres.</summary>
    static float Crossing(Func<float, float> value)
    {
        float last = 2000f;
        for (float t = 2000f; t >= -1600f; t -= 20f)
        {
            if (value(t) > 0f)
            {
                float lo = t, hi = last;
                for (int k = 0; k < 12; k++)
                {
                    float mid = (lo + hi) * 0.5f;
                    if (value(mid) > 0f) lo = mid;
                    else hi = mid;
                }
                return (lo + hi) * 0.5f;
            }
            last = t;
        }
        return float.NaN;
    }

    /// <summary>The layer read between its texels, as the sheet's filtering reads it.</summary>
    static GlobeWeather.Texel Bilinear(GlobeWeather layer, Vector3 p)
    {
        float x = Wrap(p.X, layer.MapSize.X) / layer.MapSize.X * layer.Width - 0.5f, z = Wrap(p.Z, layer.MapSize.Y) / layer.MapSize.Y * layer.Height - 0.5f;
        int i0 = (int)MathF.Floor(x), j0 = (int)MathF.Floor(z);
        float fx = x - i0, fz = z - j0;
        GlobeWeather.Texel T(int i, int j) => layer.Read(((i % layer.Width) + layer.Width) % layer.Width, ((j % layer.Height) + layer.Height) % layer.Height);
        GlobeWeather.Texel a = T(i0, j0), b = T(i0 + 1, j0), c = T(i0, j0 + 1), d = T(i0 + 1, j0 + 1);
        float L(Func<GlobeWeather.Texel, float> f) => (f(a) * (1 - fx) + f(b) * fx) * (1 - fz) + (f(c) * (1 - fx) + f(d) * fx) * fz;
        return new GlobeWeather.Texel(L(t => t.Cover), L(t => t.Extra), L(t => t.Tower), L(t => t.Veil), L(t => t.Dust), L(t => t.Snow), L(t => t.Fog), L(t => t.Lightning));
    }

    static float Wrap(float x, float size) => x - size * MathF.Floor(x / size);

    static Vector3 G(PlanetSurface.Skies.D3 d) => new((float)d.X, (float)d.Y, (float)d.Z);

    static Vector3 G(N.Vector3 d) => new(d.X, d.Y, d.Z);

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
    }
}
