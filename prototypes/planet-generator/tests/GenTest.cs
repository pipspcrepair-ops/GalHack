using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using Planets.Gen;
using Planets.Look;

namespace Planets;

/// <summary>
/// Headless tests of the planet generator. Run with:
///   godot --headless --path prototypes/planet-generator res://tests/GenTest.tscn
/// Exits 0 if every check passes, 1 if any fails. Takes about half a minute.
/// </summary>
public partial class GenTest : Node
{
    int _failures;

    public override void _Ready()
    {
        var total = Stopwatch.StartNew();
        // -- --only=Relief,Distinctness runs just the named checks (handy while tuning).
        var only = new HashSet<string>();
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--only=")) only.UnionWith(arg["--only=".Length..].Split(','));
        void Run(string name, Action check) { if (only.Count == 0 || only.Contains(name)) check(); }
        Run("Determinism", Determinism);
        Run("SeamlessWrap", SeamlessWrap);
        Run("Poles", Poles);
        Run("LandFraction", LandFraction);
        Run("Rivers", Rivers);
        Run("RiversAcrossSeams", RiversAcrossSeams);
        Run("StarterMaterials", StarterMaterials);
        Run("Relief", ReliefCheck);
        Run("Performance", Performance);
        Run("Distinctness", Distinctness);
        GD.Print($"total test time {total.Elapsed.TotalSeconds:F1} s");
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} CHECK(S) FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    static IEnumerable<(Archetype a, int v)> Variants()
    {
        for (int a = 0; a < 5; a++)
        for (int v = 0; v < PlanetSpec.VariantCount; v++)
            yield return ((Archetype)a, v);
    }

    // ---- Checks -------------------------------------------------------------------------

    void Determinism()
    {
        var spec = new PlanetSpec(4242, Archetype.Arid, 1, Modifier.Cratered | Modifier.Mesas, 8);
        ulong first = PlanetGenerator.Generate(spec).ComputeHash();
        ulong second = PlanetGenerator.Generate(spec).ComputeHash();
        ulong other = PlanetGenerator.Generate(spec with { Seed = 4243 }).ComputeHash();
        Check(first == second, $"determinism: same spec, same map hash ({first:X16})");
        Check(first != other, "determinism: a different seed gives a different map");

        var sampler = new SurfaceSampler(PlanetGenerator.Generate(spec));
        var again = new SurfaceSampler(PlanetGenerator.Generate(spec));
        bool same = true;
        for (int k = 0; k < 500; k++)
        {
            double x = k * 15.7, y = 300 + k * 6.1;
            same &= sampler.HeightAt(x, y, 1) == again.HeightAt(x, y, 1);
        }
        Check(same, "determinism: fine terrain identical at 500 points");
    }

    /// <summary>
    /// The planet is a torus: both seams, and the corner where they meet, must be invisible on the
    /// world map and continuous in the fine terrain, heights and normals alike.
    /// </summary>
    void SeamlessWrap()
    {
        foreach (var (a, v) in Variants())
        {
            var map = PlanetGenerator.Generate(PlanetSpec.Roll(77, a, v, 8));
            var (ewSeam, ewMax, ewInner, ewWidest) = SeamStep(map, eastWest: true);
            var (nsSeam, nsMax, nsInner, nsWidest) = SeamStep(map, eastWest: false);

            // The fine terrain must be continuous at each seam: a real step would stay the same
            // size as the gap shrinks, a slope shrinks with it. Same for the normals.
            var sampler = new SurfaceSampler(map);
            double W = map.WidthMeters, H = map.HeightMeters;
            var (ewNear, ewNearer, ewTilt, ewTilter) = FineGap(sampler, k => (W, H * k / 400.0), (p, d) => (p.x - d, p.y), (p, d) => (d, p.y));
            var (nsNear, nsNearer, nsTilt, nsTilter) = FineGap(sampler, k => (W * k / 400.0, H), (p, d) => (p.x, p.y - d), (p, d) => (p.x, d));

            // All four corners at once: (W-d, H-d), (d, H-d), (W-d, d), (d, d) are one spot.
            double cornerNear = CornerSpread(sampler, W, H, 0.05), cornerNearer = CornerSpread(sampler, W, H, 0.005);

            bool mapOk = ewSeam <= ewWidest && ewMax <= ewInner && nsSeam <= nsWidest && nsMax <= nsInner;
            bool fineOk = ewNearer <= ewNear * 0.2 + 1e-4 && nsNearer <= nsNear * 0.2 + 1e-4 && cornerNearer <= cornerNear * 0.2 + 1e-4;
            bool normalsOk = ewTilter <= ewTilt * 0.3 + 1e-3 && nsTilter <= nsTilt * 0.3 + 1e-3;
            Check(mapOk && fineOk && normalsOk,
                $"seamless wrap {a}/{v}: map step east-west {ewSeam:F2} m (others up to {ewWidest:F2}), north-south {nsSeam:F2} m (others up to {nsWidest:F2}); " +
                $"fine gap 0.1 -> 0.01 m: east-west {ewNear:F4} -> {ewNearer:F5} m, north-south {nsNear:F4} -> {nsNearer:F5} m, " +
                $"four corners {cornerNear:F4} -> {cornerNearer:F5} m; normals across the seams 0.1 -> 0.01 m apart: east-west {ewTilt:F3} -> {ewTilter:F4} deg, " +
                $"north-south {nsTilt:F3} -> {nsTilter:F4} deg");
            if (!mapOk) GD.PrintErr($"      map seam: east-west max step {ewMax:F2} (inner max {ewInner:F2}), north-south {nsMax:F2} ({nsInner:F2})");
        }
    }

    /// <summary>Mean and max height step across the seam, and the max and widest mean over every other column (or row) pair.</summary>
    static (double seam, double seamMax, double innerMax, double widest) SeamStep(WorldMap map, bool eastWest)
    {
        int lines = eastWest ? map.Width : map.Height, along = eastWest ? map.Height : map.Width;
        var means = new List<double>();
        double seam = 0, seamMax = 0, innerMax = 0;
        for (int l = 0; l < lines; l++)
        {
            double sum = 0;
            for (int t = 0; t < along; t++)
            {
                int here = eastWest ? map.Index(l, t) : map.Index(t, l);
                int next = eastWest ? map.Index(l + 1, t) : map.Index(t, l + 1);
                double d = Math.Abs(map.Elevation[next] - map.Elevation[here]);
                sum += d;
                if (l == lines - 1) seamMax = Math.Max(seamMax, d);
                else innerMax = Math.Max(innerMax, d);
            }
            if (l == lines - 1) seam = sum / along;
            else means.Add(sum / along);
        }
        means.Sort();
        return (seam, seamMax, innerMax, means[^1]);
    }

    /// <summary>
    /// Mean height difference, and mean angle between the surface normals, across a seam at
    /// gaps of 0.1 and 0.01 m. Continuous ground shrinks both tenfold with the gap; a step or a
    /// crease at the seam would not shrink at all.
    /// </summary>
    static (double near, double nearer, double tilt, double tilter) FineGap(SurfaceSampler s, Func<int, (double x, double y)> seamPoint,
        Func<(double x, double y), double, (double, double)> before, Func<(double x, double y), double, (double, double)> after)
    {
        double near = 0, nearer = 0, tilt = 0, tilter = 0;
        for (int k = 0; k < 400; k++)
        {
            var p = seamPoint(k);
            var (ax, ay) = before(p, 0.05);
            var (bx, by) = after(p, 0.05);
            near += Math.Abs(s.HeightAt(ax, ay, 1) - s.HeightAt(bx, by, 1));
            (ax, ay) = before(p, 0.005);
            (bx, by) = after(p, 0.005);
            nearer += Math.Abs(s.HeightAt(ax, ay, 1) - s.HeightAt(bx, by, 1));
            if (k % 4 == 0)
            {
                var (cx, cy) = before(p, 0.05);
                var (dx, dy) = after(p, 0.05);
                tilt += Angle(Normal(s, cx, cy), Normal(s, dx, dy)) / 100;
                (cx, cy) = before(p, 0.005);
                (dx, dy) = after(p, 0.005);
                tilter += Angle(Normal(s, cx, cy), Normal(s, dx, dy)) / 100;
            }
        }
        return (near / 400, nearer / 400, tilt, tilter);
    }

    static (float x, float y, float z) Normal(SurfaceSampler s, double x, double y)
    {
        float gx = s.HeightAt(x + 0.5, y, 1) - s.HeightAt(x - 0.5, y, 1), gy = s.HeightAt(x, y + 0.5, 1) - s.HeightAt(x, y - 0.5, 1);
        float l = MathF.Sqrt(gx * gx + 1 + gy * gy);
        return (-gx / l, 1 / l, -gy / l);
    }

    static double Angle((float x, float y, float z) a, (float x, float y, float z) b) =>
        Math.Acos(Math.Clamp(a.x * b.x + a.y * b.y + a.z * b.z, -1f, 1f)) * 180 / Math.PI;

    static double CornerSpread(SurfaceSampler s, double w, double h, double d)
    {
        float a = s.HeightAt(w - d, h - d, 1), b = s.HeightAt(d, h - d, 1), c = s.HeightAt(w - d, d, 1), e = s.HeightAt(d, d, 1);
        return Math.Max(Math.Max(a, b), Math.Max(c, e)) - Math.Min(Math.Min(a, b), Math.Min(c, e));
    }

    /// <summary>
    /// No walls at the poles: they are cold country, not a barrier. The steepest ground in the polar
    /// band stays within what the rest of the planet has, the band is colder than the equator, and
    /// the cold carries straight across the north-south seam.
    /// </summary>
    void Poles()
    {
        foreach (var (a, v) in Variants())
        {
            var map = PlanetGenerator.Generate(PlanetSpec.Roll(31, a, v, 8));
            int band = Math.Max(2, map.Height / 16);
            var polar = new List<float>();
            var all = new List<float>();
            double polarT = 0, equatorT = 0, seamStep = 0;
            int polarN = 0, equatorN = 0;
            for (int j = 0; j < map.Height; j++)
            for (int i = 0; i < map.Width; i++)
            {
                int k = map.Index(i, j);
                bool isPolar = j < band || j >= map.Height - band;
                if (!map.Has(k, CellFlags.Water))
                {
                    all.Add(map.Slope[k]);
                    if (isPolar) polar.Add(map.Slope[k]);
                }
                if (isPolar) { polarT += map.Temperature[k]; polarN++; }
                if (Math.Abs(j - map.Height / 2) < band) { equatorT += map.Temperature[k]; equatorN++; }
            }
            for (int i = 0; i < map.Width; i++)
                seamStep += Math.Abs(map.Temperature[map.Index(i, 0)] - map.Temperature[map.Index(i, map.Height - 1)]) / map.Width;
            all.Sort();
            polar.Sort();
            float p99 = all.Count > 0 ? all[(int)(all.Count * 0.99f)] : 0f, max = all.Count > 0 ? all[^1] : 0f;
            float polar99 = polar.Count > 0 ? polar[(int)(polar.Count * 0.99f)] : 0f, polarMax = polar.Count > 0 ? polar[^1] : 0f;
            polarT /= Math.Max(1, polarN);
            equatorT /= Math.Max(1, equatorN);
            Check(polar99 <= p99 * 1.25f && polarMax <= max && polarT < equatorT && seamStep < 0.05,
                $"poles {a}/{v}: no wall (polar slopes 99th pct {Deg(polar99):F0} deg, max {Deg(polarMax):F0} deg; whole planet {Deg(p99):F0} / {Deg(max):F0} deg); " +
                $"polar band {polarT:F2} vs equator {equatorT:F2} temperature; step across the north-south seam {seamStep:F3}");
        }
    }

    static float Deg(float slope) => MathF.Atan(slope) * 180f / MathF.PI;

    /// <summary>Rivers ignore the seams: some cross the north-south seam, still draining downhill, and the fine terrain carves them there.</summary>
    void RiversAcrossSeams()
    {
        int crossings = 0, climbing = 0, carved = 0, probed = 0;
        foreach (var (a, v) in Variants())
        {
            for (int s = 0; s < 3 && crossings < 12; s++)
            {
                var map = PlanetGenerator.Generate(PlanetSpec.Roll((ulong)(600 + s * 11), a, v, 8));
                SurfaceSampler sampler = null;
                foreach (River r in map.Rivers)
                {
                    for (int k = 0; k + 1 < r.Count; k++)
                    {
                        double y0 = r.Y[k], y1 = r.Y[k + 1];
                        double seam = y0 < 0 || y1 < 0 ? 0 : y0 >= map.HeightMeters || y1 >= map.HeightMeters ? map.HeightMeters : -1;
                        if (seam < 0 || (y0 - seam) * (y1 - seam) > 0) continue;
                        crossings++;
                        if (r.Level[k + 1] > r.Level[k] + 1e-3f) climbing++;
                        // The channel under the crossing sits below the river's surface on both sides.
                        sampler ??= new SurfaceSampler(map);
                        double t = (seam - y0) / (y1 - y0);
                        double x = r.X[k] + (r.X[k + 1] - r.X[k]) * t;
                        // The channel meanders up to a few tens of metres off the polyline, so look along the seam for it.
                        for (int side = -1; side <= 1; side += 2)
                        {
                            probed++;
                            for (double dx = -60; dx <= 60; dx += 1)
                            {
                                GroundSample g = sampler.Sample(x + dx, seam + side * 0.5, 1);
                                if (!g.InRiver || g.Height >= g.RiverLevel) continue;
                                carved++;
                                break;
                            }
                        }
                        break;
                    }
                }
            }
        }
        Check(crossings > 0 && climbing == 0 && carved == probed,
            $"rivers across the north-south seam: {crossings} crossings, {climbing} climb downstream, channel carved on {carved} of {probed} sides probed");
    }

    void LandFraction()
    {
        foreach (var (a, v) in Variants())
        {
            var def = Archetypes.Get(a, v);
            float min = 1f, max = 0f;
            int outside = 0;
            for (int s = 0; s < 20; s++)
            {
                var map = PlanetGenerator.Generate(PlanetSpec.Roll((ulong)(1000 + s * 7919), a, v, 8));
                min = Math.Min(min, map.LandFraction);
                max = Math.Max(max, map.LandFraction);
                if (map.LandFraction < def.LandMin - 0.01f || map.LandFraction > def.LandMax + 0.01f) outside++;
            }
            Check(outside == 0,
                $"land fraction {a}/{def.VariantName}: {min:P0}-{max:P0} over 20 seeds, target {def.LandMin:P0}-{def.LandMax:P0}");
        }
    }

    void Rivers()
    {
        foreach (var (a, v) in Variants())
        {
            int rivers = 0, reachSea = 0, banks = 0, deadEnds = 0;
            for (int s = 0; s < 3; s++)
            {
                var map = PlanetGenerator.Generate(PlanetSpec.Roll((ulong)(500 + s), a, v, 16));
                rivers += map.Rivers.Count;
                for (int k = 0; k < map.Count; k++)
                {
                    if (map.Has(k, CellFlags.Riparian)) banks++;
                    if (!map.Has(k, CellFlags.River)) continue;
                    // Follow the river down: it must end in the sea, never in a pit.
                    int c = k, steps = 0;
                    while (c >= 0 && !map.Has(c, CellFlags.Water) && steps++ < map.Count) c = map.Downstream[c];
                    if (c < 0 || !map.Has(c, CellFlags.Water)) deadEnds++;
                }
                foreach (River r in map.Rivers)
                    if (map.Has(map.CellAt(Mathx.WrapX(r.X[^1], map.WidthMeters), r.Y[^1]), CellFlags.Water)) reachSea++;
            }
            Check(rivers > 0 && reachSea > 0 && deadEnds == 0 && banks > 0,
                $"rivers {a}/{v}: {rivers} rivers, {reachSea} reach the sea directly (the rest join one), " +
                $"{deadEnds} dead ends, {banks} riverbank cells");
        }
    }

    /// <summary>
    /// The early game must stay short: from any spot a player could start on (the landing site, or
    /// any walkable ground), every common material for a small vessel must be within 300 m, as a
    /// surface outcrop or a shallow deposit, on every archetype.
    /// </summary>
    void StarterMaterials()
    {
        const double Reach = 300;
        foreach (var (a, v) in Variants())
        {
            double worst = 0, worstLanding = 0;
            int deposits = 0, dry = 0, tooDeep = 0, unusable = 0;
            for (int s = 0; s < 4; s++)
            {
                var map = PlanetGenerator.Generate(PlanetSpec.Roll((ulong)(700 + s * 13), a, v, 16));
                DepositField field = map.Deposits;
                var sampler = new SurfaceSampler(map);
                foreach (Deposit d in field.All())
                {
                    deposits++;
                    if (d.Depth > DepositField.MaxShallowDepth) tooDeep++;
                    if (!DepositField.Usable(map, map.CellAt(d.X, d.Y))) unusable++;
                    if (sampler.HeightAt(d.X, d.Y, 2) > 0f) dry++;
                }

                // Landing sites near a few longitudes, as Enter would pick them from orbit.
                for (int l = 0; l < 4; l++)
                {
                    LandingSite site = Landing.Find(map, map.WidthMeters * l / 4, map.HeightMeters / 2);
                    for (int kind = 0; kind < DepositField.CommonKinds; kind++)
                        worstLanding = Math.Max(worstLanding, field.Nearest((DepositKind)kind, site.X, site.Y));
                }

                // And anywhere else a player could stand.
                var rng = new Rng((ulong)(s + 1) * 977);
                for (int p = 0, tries = 0; p < 300 && tries < 20000; tries++)
                {
                    double x = rng.NextFloat() * map.WidthMeters, y = rng.NextFloat() * map.HeightMeters;
                    if (!DepositField.Usable(map, map.CellAt(x, y))) continue;
                    p++;
                    for (int kind = 0; kind < DepositField.CommonKinds; kind++)
                        worst = Math.Max(worst, field.Nearest((DepositKind)kind, x, y));
                }
            }
            float dryShare = dry / (float)Math.Max(deposits, 1);
            Check(worst <= Reach && worstLanding <= Reach && tooDeep == 0 && unusable == 0 && dryShare > 0.97f,
                $"starter materials {a}/{v}: iron, copper, silicon, carbon and water all within {worst:F0} m of any walkable spot " +
                $"({worstLanding:F0} m from landing sites); {deposits / 4} deposits per planet, {dryShare:P1} on dry ground, none deeper than 3 m");
        }
    }

    /// <summary>
    /// Empyrion-style drama in numbers: mountain ranges hundreds of metres high within a 2 km
    /// window, real cliffs (ground steeper than 45° over 2 m), and gorges or canyons cut deep
    /// between walls. Each variant has its own floor, so a dune sea may be gentler than a canyon
    /// world but never a plain of bumps.
    /// </summary>
    void ReliefCheck()
    {
        foreach (var (a, v) in Variants())
        {
            var stats = new List<Relief.Stats>();
            for (int s = 0; s < 2; s++)
            {
                var map = PlanetGenerator.Generate(PlanetSpec.Roll((ulong)(3100 + s * 37), a, v, 16));
                var sampler = new SurfaceSampler(map);
                stats.Add(Relief.Measure(map, (x, y, spacing) => sampler.HeightAt(x, y, spacing), (ulong)s));
            }
            var r = new Relief.Stats(
                (stats[0].RangeMedian + stats[1].RangeMedian) / 2, Math.Max(stats[0].RangeMax, stats[1].RangeMax),
                (stats[0].Slope30 + stats[1].Slope30) / 2, (stats[0].Slope45 + stats[1].Slope45) / 2,
                (stats[0].Incision95 + stats[1].Incision95) / 2, Math.Max(stats[0].IncisionMax, stats[1].IncisionMax),
                (stats[0].Fine30 + stats[1].Fine30) / 2, (stats[0].Fine45 + stats[1].Fine45) / 2);
            ReliefFloor floor = ReliefFloors[(int)a * 2 + v];
            Check(r.RangeMedian >= floor.Range && r.Slope30 >= floor.Slope30 && r.Slope45 >= floor.Slope45 && r.Incision95 >= floor.Canyon,
                $"relief {a}/{Archetypes.Get(a, v).VariantName}: {r}  (floor: range {floor.Range} m, >30° {floor.Slope30:P0}, >45° {floor.Slope45:P1}, canyon {floor.Canyon} m)");
        }
    }

    readonly record struct ReliefFloor(float Range, float Slope30, float Slope45, float Canyon);

    /// <summary>Minimum relief per variant, in Variants() order.</summary>
    static readonly ReliefFloor[] ReliefFloors =
    {
        new(750, 0.40f, 0.25f, 50), new(400, 0.40f, 0.25f, 45),   // temperate meadow, rainforest
        new(200, 0.20f, 0.08f, 25), new(280, 0.22f, 0.17f, 90),   // arid dunes, canyon
        new(500, 0.42f, 0.28f, 55), new(330, 0.30f, 0.15f, 35),   // ice glacier, hot spring
        new(400, 0.28f, 0.14f, 33), new(320, 0.33f, 0.18f, 28),   // lava volcanic, lava ocean
        new(380, 0.36f, 0.23f, 50), new(270, 0.30f, 0.14f, 36),   // alien crystal, fungal
    };

    void Performance()
    {
        PlanetGenerator.Generate(PlanetSpec.Roll(1, Archetype.Temperate, 0, 8)); // warm the JIT
        foreach (var (a, v) in new[] { (Archetype.Temperate, 0), (Archetype.Lava, 0), (Archetype.Alien, 1) })
        {
            var watch = Stopwatch.StartNew();
            var map = PlanetGenerator.Generate(PlanetSpec.Roll(9001, a, v, 16));
            double ms = watch.Elapsed.TotalMilliseconds;
            Check(ms < 2000, $"performance: 16x8 km {a}/{v} map ({map.Width}x{map.Height} cells) in {ms:F0} ms");
        }
        var big = Stopwatch.StartNew();
        var huge = PlanetGenerator.Generate(PlanetSpec.Roll(9001, Archetype.Ice, 0, 64));
        GD.Print($"info  64x32 km map ({huge.Width}x{huge.Height} cells) in {big.Elapsed.TotalMilliseconds:F0} ms");

        var sampler = new SurfaceSampler(huge);
        float sink = 0;
        for (int k = 0; k < 81 * 81; k++) sink += sampler.HeightAt(30000 + k % 81 * 2.0, 2000 + k / 81 * 2.0, 2);
        var fine = Stopwatch.StartNew();
        for (int k = 0; k < 81 * 81; k++) sink += sampler.HeightAt(20000 + k % 81 * 2.0, 12000 + k / 81 * 2.0, 2);
        GD.Print($"info  one 160 m chunk of fine terrain (81x81 samples at 2 m) in {fine.Elapsed.TotalMilliseconds:F1} ms on one thread ({sink:F0})");
    }

    /// <summary>
    /// The blind test, automated: simple feature vectors from what a player sees (colour map) and
    /// the terrain, then a nearest-centroid classifier trained on some seeds and scored on others.
    /// </summary>
    void Distinctness()
    {
        const int train = 12, test = 10;
        var trainSet = new List<(float[] f, int label, int variant)>();
        var testSet = new List<(float[] f, int label)>();
        foreach (var (a, v) in Variants())
        {
            for (int s = 0; s < train + test; s++)
            {
                ulong seed = (ulong)(s < train ? 20000 + s * 31 : 90000 + s * 17);
                float[] f = Features(PlanetGenerator.Generate(PlanetSpec.Roll(seed, a, v, 8)));
                if (s < train) trainSet.Add((f, (int)a, v));
                else testSet.Add((f, (int)a));
            }
        }

        // Standardise every feature so none dominates by its units.
        int dims = trainSet[0].f.Length;
        var mean = new float[dims];
        var std = new float[dims];
        foreach (var t in trainSet) for (int d = 0; d < dims; d++) mean[d] += t.f[d] / trainSet.Count;
        foreach (var t in trainSet) for (int d = 0; d < dims; d++) std[d] += (t.f[d] - mean[d]) * (t.f[d] - mean[d]) / trainSet.Count;
        for (int d = 0; d < dims; d++) std[d] = MathF.Sqrt(std[d]) + 1e-6f;

        // One centroid per archetype variant; a match on either variant names the archetype.
        var centroids = new float[10][];
        var labels = new int[10];
        for (int c = 0; c < 10; c++) centroids[c] = new float[dims];
        foreach (var t in trainSet)
        {
            int c = t.label * 2 + t.variant;
            labels[c] = t.label;
            for (int d = 0; d < dims; d++) centroids[c][d] += (t.f[d] - mean[d]) / std[d] / train;
        }

        int correct = 0;
        var confusion = new int[5, 5];
        foreach (var t in testSet)
        {
            int best = 0;
            float bestDist = float.MaxValue;
            for (int c = 0; c < 10; c++)
            {
                float dist = 0;
                for (int d = 0; d < dims; d++)
                {
                    float z = (t.f[d] - mean[d]) / std[d] - centroids[c][d];
                    dist += z * z;
                }
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = c;
                }
            }
            confusion[t.label, labels[best]]++;
            if (labels[best] == t.label) correct++;
        }
        float accuracy = correct / (float)testSet.Count;
        var rows = new System.Text.StringBuilder("      confusion (rows = truth): T  A  I  L  X\n");
        for (int r = 0; r < 5; r++)
        {
            rows.Append($"      {Archetypes.Names[r],-10}               ");
            for (int c = 0; c < 5; c++) rows.Append($"{confusion[r, c],2} ");
            rows.Append('\n');
        }
        GD.Print(rows.ToString().TrimEnd());
        Check(accuracy >= 0.95f, $"distinctness: nearest-centroid classifier names the archetype of {accuracy:P0} of {testSet.Count} unseen planets");
    }

    static float[] Features(WorldMap map)
    {
        var look = ArchetypeLook.For(map.Spec);
        var colors = ColorMap.Build(map, look, 192);
        double lr = 0, lg = 0, lb = 0, wr = 0, wg = 0, wb = 0, luma2 = 0, luma = 0;
        int land = 0, liquid = 0;
        for (int p = 0; p < colors.Width * colors.Height; p++)
        {
            float r = colors.Albedo[p * 4] / 255f, g = colors.Albedo[p * 4 + 1] / 255f, b = colors.Albedo[p * 4 + 2] / 255f;
            if (colors.Albedo[p * 4 + 3] > 0) { wr += r; wg += g; wb += b; liquid++; }
            else
            {
                lr += r; lg += g; lb += b; land++;
                float l = 0.3f * r + 0.59f * g + 0.11f * b;
                luma += l;
                luma2 += l * l;
            }
        }
        land = Math.Max(land, 1);
        liquid = Math.Max(liquid, 1);
        double lumaMean = luma / land;

        double slope = 0, elev = 0, elev2 = 0;
        int cells = 0;
        for (int k = 0; k < map.Count; k++)
        {
            if (map.Flags[k] != 0 && !map.Has(k, CellFlags.Riparian) && !map.Has(k, CellFlags.Coast)) continue;
            slope += map.Slope[k];
            elev += map.Elevation[k];
            elev2 += map.Elevation[k] * map.Elevation[k];
            cells++;
        }
        cells = Math.Max(cells, 1);
        double elevMean = elev / cells;
        return new[]
        {
            (float)(lr / land), (float)(lg / land), (float)(lb / land),
            (float)(wr / liquid), (float)(wg / liquid), (float)(wb / liquid),
            map.LandFraction,
            (float)(slope / cells),
            (float)Math.Sqrt(Math.Max(0, elev2 / cells - elevMean * elevMean)) / 100f,
            (float)Math.Sqrt(Math.Max(0, luma2 / land - lumaMean * lumaMean)),
        };
    }

    // ---- Helpers ------------------------------------------------------------------------

    void Check(bool ok, string what)
    {
        if (ok) GD.Print($"PASS  {what}");
        else
        {
            _failures++;
            GD.PrintErr($"FAIL  {what}");
        }
    }
}
