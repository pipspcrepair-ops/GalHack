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
        Determinism();
        SeamlessWrap();
        PolarCaps();
        LandFraction();
        Rivers();
        StarterMaterials();
        Performance();
        Distinctness();
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

    void SeamlessWrap()
    {
        foreach (var (a, v) in Variants())
        {
            var map = PlanetGenerator.Generate(PlanetSpec.Roll(77, a, v, 8));

            // Mean step between each pair of neighbouring columns; the seam is just one more pair.
            // A single column can be unlucky (a canyon wall running along it), so the seam has to
            // sit inside the spread of the others rather than below their average.
            var pairs = new List<double>();
            double seam = 0, seamMax = 0, innerMax = 0;
            for (int i = 0; i < map.Width; i++)
            {
                double sum = 0;
                int rows = 0;
                for (int j = 0; j < map.Height; j++)
                {
                    if (map.Has(map.Index(i, j), CellFlags.Cap)) continue;
                    double d = Math.Abs(map.Elevation[map.Index(i + 1, j)] - map.Elevation[map.Index(i, j)]);
                    sum += d;
                    rows++;
                    if (i == map.Width - 1) seamMax = Math.Max(seamMax, d);
                    else innerMax = Math.Max(innerMax, d);
                }
                if (i == map.Width - 1) seam = sum / rows;
                else pairs.Add(sum / rows);
            }
            pairs.Sort();
            double median = pairs[pairs.Count / 2], widest = pairs[^1];

            // The fine terrain must be continuous at the seam: a real step would stay the same size
            // as the gap shrinks, a slope shrinks with it.
            var sampler = new SurfaceSampler(map);
            double near = 0, nearer = 0;
            for (int k = 0; k < 400; k++)
            {
                double y = map.HeightMeters * (0.15 + 0.7 * k / 400.0);
                near += Math.Abs(sampler.HeightAt(map.WidthMeters - 0.05, y, 1) - sampler.HeightAt(0.05, y, 1));
                nearer += Math.Abs(sampler.HeightAt(map.WidthMeters - 0.005, y, 1) - sampler.HeightAt(0.005, y, 1));
            }
            near /= 400;
            nearer /= 400;
            Check(seam <= widest && seamMax <= innerMax && nearer <= near * 0.2 + 1e-4,
                $"seamless wrap {a}/{v}: seam step {seam:F2} m, other column pairs {pairs[0]:F2}-{widest:F2} m (median {median:F2}); " +
                $"fine gap 0.1 m -> {near:F4} m, 0.01 m -> {nearer:F5} m");
        }
    }

    void PolarCaps()
    {
        foreach (var (a, v) in Variants())
        {
            var map = PlanetGenerator.Generate(PlanetSpec.Roll(31, a, v, 8));
            bool everyColumn = true;
            for (int i = 0; i < map.Width; i++)
                everyColumn &= map.Has(map.Index(i, 0), CellFlags.Cap) && map.Has(map.Index(i, map.Height - 1), CellFlags.Cap);

            // Ignore the flags: could anything walking up 45° slopes get from the equator to either edge?
            bool crossed = Walkable(map, maxSlope: 1f);
            var sampler = new SurfaceSampler(map);
            float steepest = SteepestCapClimb(map, sampler);
            Check(everyColumn && !crossed && steepest > 2f,
                $"polar caps {a}/{v}: walls on every column {everyColumn}, crossable {crossed}, " +
                $"cap wall slope at 2 m {MathF.Atan(steepest) * 180f / MathF.PI:F0} deg");
        }
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
        Check(accuracy >= 0.9f, $"distinctness: nearest-centroid classifier names the archetype of {accuracy:P0} of {testSet.Count} unseen planets");
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

    /// <summary>Flood from the middle row, stepping only between cells no steeper than maxSlope.</summary>
    static bool Walkable(WorldMap map, float maxSlope)
    {
        var seen = new bool[map.Count];
        var queue = new Queue<int>();
        int mid = map.Height / 2;
        for (int i = 0; i < map.Width; i++)
        {
            int k = map.Index(i, mid);
            seen[k] = true;
            queue.Enqueue(k);
        }
        float climb = maxSlope * map.CellSize;
        while (queue.Count > 0)
        {
            int c = queue.Dequeue();
            int ci = c % map.Width, cj = c / map.Width;
            if (cj == 0 || cj == map.Height - 1) return true;
            for (int d = 0; d < 4; d++)
            {
                int ni = ci + (d == 0 ? 1 : d == 1 ? -1 : 0), nj = cj + (d == 2 ? 1 : d == 3 ? -1 : 0);
                int nb = map.Index(ni, nj);
                if (seen[nb] || Math.Abs(map.Elevation[nb] - map.Elevation[c]) > climb) continue;
                seen[nb] = true;
                queue.Enqueue(nb);
            }
        }
        return false;
    }

    /// <summary>The smallest, over sampled columns, of the steepest 2 m step found walking into the north cap.</summary>
    static float SteepestCapClimb(WorldMap map, SurfaceSampler sampler)
    {
        float weakest = float.MaxValue;
        for (int c = 0; c < 16; c++)
        {
            double x = (c + 0.37) * map.WidthMeters / 16;
            float steepest = 0f;
            double start = map.Model.CapBand * 2;
            float prev = sampler.HeightAt(x, start, 2);
            for (double y = start - 2; y > 0; y -= 2)
            {
                float h = sampler.HeightAt(x, y, 2);
                steepest = Math.Max(steepest, (h - prev) / 2f);
                prev = h;
            }
            weakest = Math.Min(weakest, steepest);
        }
        return weakest;
    }

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
