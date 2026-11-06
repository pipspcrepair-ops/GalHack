using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Planets.Gen;

namespace Planets;

/// <summary>
/// The landform checks: worlds divided into regions of real landforms, placed where they make
/// sense, blended at their borders, seamless, varied from world to world, and each landform
/// measurably what it claims to be.
/// </summary>
public partial class GenTest
{
    /// <summary>A planet of a random archetype and variant, from the seed alone.</summary>
    static PlanetSpec RandomSpec(ulong seed, int widthKm)
    {
        var rng = new Rng(Hash.Of(seed, 0x535045));
        int a = rng.Next(5), v = rng.Next(PlanetSpec.VariantCount);
        return PlanetSpec.Roll(seed, (Archetype)a, v, widthKm);
    }

    static TerrainModel ModelOf(PlanetSpec spec) => new(spec, Archetypes.For(spec));

    static float ShareOf(TerrainModel model, Landform l)
    {
        foreach (var (lf, s) in model.Landforms.Present(0f)) if (lf == l) return s;
        return 0f;
    }

    /// <summary>The first world (by seed, over the variants that can roll it) where a landform holds at least a share of the land.</summary>
    static PlanetSpec FindWorld(Landform l, float minShare, Func<ArchetypeDef, bool> variantOk = null, int widthKm = 16)
    {
        PlanetSpec best = null;
        float bestShare = -1f;
        for (ulong seed = 1; seed < 500; seed++)
        for (int a = 0; a < 5; a++)
        for (int v = 0; v < PlanetSpec.VariantCount; v++)
        {
            ArchetypeDef def = Archetypes.Get((Archetype)a, v);
            if (!def.Landforms.Any(o => o.Landform == l) && Array.IndexOf(def.SingleLandforms, l) < 0) continue;
            if (variantOk != null && !variantOk(def)) continue;
            var spec = PlanetSpec.Roll(seed * 7 + (ulong)(a * 2 + v), (Archetype)a, v, widthKm);
            float share = ShareOf(ModelOf(spec), l);
            if (share > bestShare) { bestShare = share; best = spec; }
            if (share >= minShare) return spec;
        }
        return best;
    }

    void LandformDeterminism()
    {
        var spec = PlanetSpec.Roll(4243, Archetype.Temperate, 0, 16);
        TerrainModel a = ModelOf(spec), b = ModelOf(spec), c = ModelOf(spec with { Seed = 4244 });
        bool same = a.Landforms.ComputeHash() == b.Landforms.ComputeHash() && a.Mix.ToString() == b.Mix.ToString();
        bool differs = a.Landforms.ComputeHash() != c.Landforms.ComputeHash();
        int mismatched = 0;
        var rng = new Rng(99);
        for (int k = 0; k < 400; k++)
        {
            double x = rng.NextFloat() * a.Width, y = rng.NextFloat() * a.Height;
            LandformBlend p = a.Landforms.At(x, y), q = b.Landforms.At(x, y);
            if (p.Count != q.Count || p.Dominant != q.Dominant || p.W0 != q.W0) mismatched++;
        }
        Check(same && differs && mismatched == 0,
            $"landforms: same seed, same landforms (layer hash {a.Landforms.ComputeHash():X16}, mix {a.Mix}); 400 point queries identical ({mismatched} differ); another seed differs: {differs}");
    }

    /// <summary>The landform layer is seamless: blends match across both seams and at the corners, as the heights do.</summary>
    void LandformSeams()
    {
        double worst = 0, worstInner = 0;
        foreach (var (a, v) in Variants())
        {
            TerrainModel m = ModelOf(PlanetSpec.Roll(77, a, v, 8));
            LandformLayer layer = m.Landforms;
            double W = m.Width, H = m.Height;
            for (int k = 0; k < 400; k++)
            {
                double y = H * (k + 0.5) / 400, x = W * (k + 0.5) / 400;
                worst = Math.Max(worst, Distance(layer.At(W - 0.05, y), layer.At(0.05, y)));
                worst = Math.Max(worst, Distance(layer.At(x, H - 0.05), layer.At(x, 0.05)));
                worstInner = Math.Max(worstInner, Distance(layer.At(W * 0.37 - 0.05, y), layer.At(W * 0.37 + 0.05, y)));
            }
            worst = Math.Max(worst, Distance(layer.At(W - 0.05, H - 0.05), layer.At(0.05, 0.05)));
            worst = Math.Max(worst, Distance(layer.At(0.05, H - 0.05), layer.At(W - 0.05, 0.05)));
        }
        Check(worst < 0.01 && worst <= Math.Max(worstInner * 4, 0.002),
            $"landforms seamless: blend change 0.1 m across either seam or a corner at most {worst:F5} (across an inland line {worstInner:F5}), every variant");
    }

    static double Distance(LandformBlend a, LandformBlend b)
    {
        double d = 0;
        foreach (Landform l in LandformInfo.All) d += Math.Abs(a.Weight(l) - b.Weight(l));
        return d;
    }

    /// <summary>
    /// World variety: about one world in five is a single landform throughout, the rest combine
    /// 2-5, and every landform turns up somewhere.
    /// </summary>
    void LandformVariety()
    {
        var watch = Stopwatch.StartNew();
        const int Worlds = 200;
        int single = 0, inRange = 0, mixed = 0;
        var counts = new int[8];
        var seen = new int[LandformInfo.Count];
        var models = new TerrainModel[Worlds];
        Parallel.For(0, Worlds, s => models[s] = ModelOf(RandomSpec((ulong)(70000 + s * 131), 16)));
        foreach (TerrainModel m in models)
        {
            var present = m.Landforms.Present(0.004f);
            foreach (var (l, _) in present) seen[(int)l]++;
            counts[Math.Min(present.Count, 7)]++;
            if (present.Count == 1) single++;
            else
            {
                mixed++;
                if (present.Count is >= 2 and <= 5) inRange++;
            }
        }
        var missing = LandformInfo.All.Where(l => seen[(int)l] == 0).Select(LandformInfo.Name).ToList();
        string hist = string.Join(", ", Enumerable.Range(1, 6).Select(c => $"{c}: {counts[c]}"));
        string often = string.Join(", ", LandformInfo.All.OrderByDescending(l => seen[(int)l]).Select(l => $"{LandformInfo.Name(l)} {seen[(int)l]}"));
        float singleShare = single / (float)Worlds;
        Check(singleShare is >= 0.13f and <= 0.28f && inRange >= mixed * 0.97f && missing.Count == 0,
            $"world variety over {Worlds} seeds: {singleShare:P0} single-landform worlds, {inRange} of {mixed} mixed worlds have 2-5 landforms " +
            $"(landforms per world {hist}); every landform appears{(missing.Count > 0 ? $" EXCEPT {string.Join(", ", missing)}" : "")} ({watch.Elapsed.TotalMilliseconds:F0} ms)");
        GD.Print($"      worlds per landform: {often}");
    }

    /// <summary>
    /// Placed where it makes sense: deltas on coasts at river mouths (never inland), fjords only on
    /// cold coasts, deserts in the dry places (dry belts, interiors, rain shadows).
    /// </summary>
    void LandformPlacement()
    {
        // Deltas.
        int deltas = 0, inland = 0, withRiver = 0, worlds = 0;
        for (ulong seed = 1; seed < 400 && deltas < 14; seed++)
        {
            PlanetSpec spec = RandomSpec(seed * 31 + 5, 16);
            TerrainModel model = ModelOf(spec);
            if (model.Landforms.Deltas.Count == 0) continue;
            WorldMap map = PlanetGenerator.Generate(spec);
            worlds++;
            foreach (DeltaFan f in map.Landforms.Deltas)
            {
                deltas++;
                // The fan must reach the sea: water within its radius on the seaward side.
                int sea = 0, cells = 0;
                for (double r = 0; r <= f.Radius; r += 64)
                for (double t = -1.2; t <= 1.2; t += 0.1)
                {
                    double ca = Math.Cos(t), sa = Math.Sin(t);
                    double x = f.ApexX + (f.AxisX * ca - f.AxisY * sa) * r, y = f.ApexY + (f.AxisY * ca + f.AxisX * sa) * r;
                    int cell = map.CellAt(Mathx.WrapX(x, map.WidthMeters), Mathx.Wrap(y, map.HeightMeters));
                    cells++;
                    if (map.Has(cell, CellFlags.Water)) sea++;
                }
                // Coastal: the apex within a radius of the coast the delta was placed by, and the
                // fan reaching the sea (it builds new land out over the shallows, so the sea is at its rim).
                if (sea < cells * 0.02f || map.Landforms.CoastDistance(f.ApexX, f.ApexY) > f.Radius)
                {
                    inland++;
                    GD.Print($"      inland delta on {spec}: apex {f.ApexX:F0},{f.ApexY:F0}, radius {f.Radius:F0} m, {sea} of {cells} fan samples in the sea, apex {map.Landforms.CoastDistance(f.ApexX, f.ApexY):F0} m from the coast");
                }
                // And a real river should end in it.
                bool mouth = false;
                foreach (River rv in map.Rivers)
                {
                    if (rv.Count < 8 || !map.Has(map.CellAt(Mathx.WrapX(rv.X[^1], map.WidthMeters), Mathx.Wrap(rv.Y[^1], map.HeightMeters)), CellFlags.Water)) continue;
                    double dx = Mathx.WrapDelta(rv.X[^1] - f.ApexX, map.WidthMeters), dy = Mathx.WrapDelta(rv.Y[^1] - f.ApexY, map.HeightMeters);
                    if (dx * dx + dy * dy < Math.Pow(f.Radius * 1.3, 2)) { mouth = true; break; }
                }
                if (mouth) withRiver++;
            }
        }
        Check(deltas >= 6 && inland == 0 && withRiver >= deltas * 0.6f,
            $"deltas on coasts: {deltas} deltas on {worlds} worlds, {inland} inland, {withRiver} with a river ending in them");

        // Fjords: only on cold coasts.
        var fjordTemps = new List<float>();
        int fjordWorlds = 0;
        for (ulong seed = 1; seed < 400 && fjordWorlds < 8; seed++)
        {
            PlanetSpec spec = RandomSpec(seed * 17 + 3, 16);
            if (ShareOf(ModelOf(spec), Landform.Fjords) < 0.005f) continue;
            WorldMap map = PlanetGenerator.Generate(spec);
            fjordWorlds++;
            for (int k = 0; k < map.Count; k++)
                if (map.Landform[k] == (byte)Landform.Fjords) fjordTemps.Add(map.Temperature[k]);
        }
        fjordTemps.Sort();
        float fjord95 = fjordTemps.Count > 0 ? fjordTemps[(int)(fjordTemps.Count * 0.95f)] : 0f;
        Check(fjordWorlds >= 4 && fjord95 < 0.42f,
            $"fjords only on cold coasts: {fjordWorlds} worlds with fjords, temperature in fjord country 95th percentile {fjord95:F2} " +
            $"(median {(fjordTemps.Count > 0 ? fjordTemps[fjordTemps.Count / 2] : 0):F2}; a temperate equator is about 0.6)");

        // Deserts in the dry places. On a mixed-climate world the deserts (dune seas, rocky
        // deserts) take its dry belts, interiors and rain shadows. On an arid world everything is
        // desert country (mesas and canyons too), and there the dune seas take the driest of it.
        int desertWorlds = 0, drier = 0, aridWorlds = 0, aridDrier = 0;
        double desertDry = 0, otherDry = 0;
        for (ulong seed = 1; seed < 3000 && (desertWorlds < 14 || aridWorlds < 10); seed++)
        {
            PlanetSpec spec = RandomSpec(seed * 13 + 1, 16);
            bool arid = spec.Archetype == Archetype.Arid;
            if (arid ? aridWorlds >= 10 : desertWorlds >= 14) continue;
            TerrainModel model = ModelOf(spec);
            if (model.Mix.Single) continue;
            bool Desert(Landform l) => arid ? l == Landform.DuneSea : l is Landform.DuneSea or Landform.RockyDesert;
            if (!model.Mix.Landforms.Any(Desert)) continue;
            LandformLayer layer = model.Landforms;
            double dd = 0, od = 0;
            int dn = 0, on = 0;
            for (int j = 0; j < layer.Ny; j++)
            for (int i = 0; i < layer.Nx; i++)
            {
                if (layer.NodeCoast(i, j) <= 0) continue;
                LandformBlend b = layer.Node(i, j);
                b.Normalize();
                float dry = layer.Dryness(layer.NodeX(i), layer.NodeY(j));
                if (Desert(b.Dominant)) { dd += dry; dn++; }
                else { od += dry; on++; }
            }
            // The deserts must be a part of the land, not nearly all of it, for "drier than the
            // rest" to mean anything.
            float desertShare = dn / (float)Math.Max(1, dn + on);
            if (dn == 0 || on == 0 || desertShare is < 0.05f or > 0.65f) continue;
            bool isDrier = dd / dn > od / on;
            if (arid) { aridWorlds++; if (isDrier) aridDrier++; }
            else { desertWorlds++; if (isDrier) drier++; desertDry += dd / dn; otherDry += od / on; }
            if (!isDrier)
                GD.Print($"      not drier: {spec.Archetype}/{Archetypes.Get(spec.Archetype, spec.Variant).VariantName} seed {spec.Seed}: {(arid ? "dune sea" : "deserts")} {dd / dn:+0.00;-0.00} ({dn} nodes), rest {od / on:+0.00;-0.00} ({on}); {model.Mix}");
        }
        Check(desertWorlds >= 8 && drier >= desertWorlds * 0.8f && aridWorlds >= 6 && aridDrier >= aridWorlds * 0.8f,
            $"deserts in dry places: on {drier} of {desertWorlds} mixed-climate worlds with deserts, the deserts are drier than the rest of the land " +
            $"(dryness {desertDry / Math.Max(1, desertWorlds):+0.00;-0.00} against {otherDry / Math.Max(1, desertWorlds):+0.00;-0.00}); " +
            $"on {aridDrier} of {aridWorlds} arid worlds with a dune sea, the dune sea is the driest country");
    }

    readonly record struct Character(int Points, float Gentle, float Steep30, float Steep45, float Relief1Km, float Water);

    /// <summary>
    /// Slopes and relief where one landform holds (weight 0.85 and more), on one world. Inland
    /// landforms are measured inland only (the sea off their coast belongs to nobody), and relief
    /// windows only count when the whole kilometre is that landform.
    /// </summary>
    static Character Measure(WorldMap map, SurfaceSampler sampler, Landform l, int points = 360, int windows = 30)
    {
        var rng = new Rng(Hash.Of(map.Spec.Seed, (long)l, 0x434852));
        var spots = new List<(double x, double y)>();
        var whole = new List<(double x, double y)>();
        bool coastal = LandformInfo.IsCoastal(l);
        LandformLayer layer = map.Landforms;
        int water = 0, tried = 0;
        for (int t = 0; t < 400000 && spots.Count < points; t++)
        {
            double x = rng.NextFloat() * map.WidthMeters, y = rng.NextFloat() * map.HeightMeters;
            if (map.LandformsAt(x, y).Weight(l) < 0.85f) continue;
            if (!coastal && layer.CoastDistance(x, y) < 250f) continue;
            tried++;
            if (map.Sample(map.Elevation, x, y) < 0.5f) { water++; continue; }
            spots.Add((x, y));
            if (whole.Count < windows)
            {
                bool all = true;
                for (double dy = -500; dy <= 500 && all; dy += 125)
                for (double dx = -500; dx <= 500 && all; dx += 125)
                    all = map.LandformsAt(x + dx, y + dy).Weight(l) >= 0.85f && (coastal || layer.CoastDistance(x + dx, y + dy) > 0f);
                if (all) whole.Add((x, y));
            }
        }
        if (spots.Count == 0) return default;
        int gentle = 0, s30 = 0, s45 = 0;
        foreach (var (x, y) in spots)
        {
            float gx = (sampler.HeightAt(x + 4, y, 1) - sampler.HeightAt(x - 4, y, 1)) / 8f;
            float gy = (sampler.HeightAt(x, y + 4, 1) - sampler.HeightAt(x, y - 4, 1)) / 8f;
            float s = MathF.Sqrt(gx * gx + gy * gy);
            if (s < 0.0875f) gentle++;
            if (s > 0.57735f) s30++;
            if (s > 1f) s45++;
        }
        if (whole.Count == 0) whole = spots;
        var ranges = new float[Math.Min(windows, whole.Count)];
        Parallel.For(0, ranges.Length, w =>
        {
            var (cx, cy) = whole[w * whole.Count / ranges.Length];
            float lo = float.MaxValue, hi = float.MinValue;
            for (double dy = -500; dy <= 500; dy += 25)
            for (double dx = -500; dx <= 500; dx += 25)
            {
                float h = Math.Max(0f, sampler.HeightAt(cx + dx, cy + dy, 8));
                lo = Math.Min(lo, h);
                hi = Math.Max(hi, h);
            }
            ranges[w] = hi - lo;
        });
        Array.Sort(ranges);
        float n = spots.Count;
        return new Character(spots.Count, gentle / n, s30 / n, s45 / n, ranges[ranges.Length / 2], tried > 0 ? water / (float)tried : 0f);
    }

    /// <summary>Each landform measurably is what it claims: plains flat over kilometres, mountains high, dunes in periodic ridges, deltas threaded with channels.</summary>
    void LandformCharacter()
    {
        var watch = Stopwatch.StartNew();
        foreach (Landform l in LandformInfo.All)
        {
            float want = LandformInfo.IsCoastal(l) ? 0.02f : 0.12f;
            PlanetSpec spec = l == Landform.Mountains
                ? FindWorld(l, want, d => d.MountainHeight >= 300f)
                : FindWorld(l, want);
            WorldMap map = PlanetGenerator.Generate(spec);
            var sampler = new SurfaceSampler(map);
            Character c = Measure(map, sampler, l);
            string stats = $"{c.Points} points: {c.Gentle:P0} gentle (<5°), {c.Steep30:P0} over 30°, {c.Steep45:P0} over 45°, relief in 1 km {c.Relief1Km:F0} m (median), {c.Water:P0} under water";
            string where = $"{map.Spec.Archetype}/{map.Def.VariantName} seed {map.Spec.Seed}";
            bool ok;
            string claim;
            switch (l)
            {
                case Landform.Plains or Landform.Steppe or Landform.IcePlain:
                    ok = c.Gentle >= 0.8f && c.Relief1Km <= 40f;
                    claim = "flat to gently rolling: 80% gentle, relief in 1 km 40 m or less";
                    break;
                case Landform.BasaltPlains or Landform.RockyDesert:
                    ok = c.Gentle >= 0.65f && c.Relief1Km <= (l == Landform.RockyDesert ? 120f : 40f);
                    claim = "open: 65% gentle, low relief (bar the odd rock dome)";
                    break;
                case Landform.Wetlands:
                    ok = c.Gentle >= 0.85f && c.Relief1Km <= 15f && c.Water is >= 0.05f and <= 0.6f;
                    claim = "low, flat, waterlogged: 85% gentle, relief in 1 km 15 m or less, 5-60% under water";
                    break;
                case Landform.Mountains:
                    ok = c.Relief1Km >= 220f && c.Steep30 >= 0.25f;
                    claim = "high relief: 220 m in 1 km, a quarter over 30°";
                    break;
                case Landform.Hills or Landform.DomeFields:
                    ok = c.Relief1Km is >= 25f and <= 400f && c.Gentle < 0.8f;
                    claim = "rolling: 25-400 m in 1 km (foothills climb towards their ranges), not flat";
                    break;
                case Landform.Plateau or Landform.Mesas or Landform.Canyonlands:
                    ok = c.Steep45 >= 0.03f && c.Gentle >= 0.2f;
                    claim = "flat tops and cliffs: 3% over 45°, 20% gentle";
                    break;
                case Landform.Karst:
                    ok = c.Steep45 >= 0.05f && c.Gentle >= 0.4f;
                    claim = "towers out of a flat plain: 5% over 45°, 40% gentle";
                    break;
                case Landform.Badlands:
                    ok = c.Steep30 >= 0.12f;
                    claim = "dense gullies: 12% over 30°";
                    break;
                case Landform.ShardFields:
                    ok = c.Steep45 >= 0.06f;
                    claim = "spires: 6% over 45°";
                    break;
                case Landform.LakeDistrict:
                    ok = c.Water is >= 0.08f and <= 0.6f;
                    claim = "lakes: 8-60% under water";
                    break;
                case Landform.Fjords:
                    ok = c.Steep30 >= 0.15f && c.Water >= 0.04f;
                    claim = "steep walls over drowned troughs: 15% over 30°, water in the troughs";
                    break;
                case Landform.CoastalCliffs:
                    ok = CliffShare(map, sampler) >= 0.35f;
                    claim = $"cliffs at the shore: {CliffShare(map, sampler):P0} of its shore rises 15 m within 60 m (35%)";
                    break;
                case Landform.DuneSea:
                    var (lagCorr, halfCorr, height) = DunePeriodicity(map, sampler);
                    ok = lagCorr >= 0.35f && halfCorr < 0f && height >= 6f;
                    claim = $"dunes in periodic ridges along the wind: correlation {lagCorr:F2} one wavelength on, {halfCorr:F2} half a wavelength on, crests {height:F0} m (0.35, below 0, 6 m)";
                    break;
                case Landform.Delta:
                    var (channels, arcs) = DeltaChannels(map);
                    ok = arcs > 0 && channels >= 3f;
                    claim = $"a channel network: {channels:F1} channels crossed per arc across the fan on average, {arcs} arcs (3)";
                    break;
                case Landform.Archipelago:
                    ok = c.Water >= 0.2f;
                    claim = "islands in the sea: 20% under water";
                    break;
                case Landform.RiverValleys:
                    ok = c.Gentle >= 0.4f && c.Relief1Km >= 12f;
                    claim = "flat valley floors between bluffs: 40% gentle, 12 m relief";
                    break;
                case Landform.VolcanicField:
                    ok = c.Relief1Km >= 40f && c.Steep30 >= 0.03f;
                    claim = "cones: 40 m relief in 1 km, 3% over 30°";
                    break;
                case Landform.Ringlands:
                    ok = c.Relief1Km >= 10f && c.Gentle >= 0.4f;
                    claim = "ring ridges on open ground: 10 m relief, 40% gentle";
                    break;
                default:
                    ok = c.Points > 0;
                    claim = "present";
                    break;
            }
            Check(ok && c.Points >= 40, $"landform {LandformInfo.Name(l)} ({where}): {claim}; {stats}");
        }
        GD.Print($"info  landform character checks in {watch.Elapsed.TotalSeconds:F1} s");
    }

    /// <summary>Of the shore where coastal cliffs hold, the share that rises 15 m within 60 m of the water.</summary>
    static float CliffShare(WorldMap map, SurfaceSampler sampler)
    {
        int shore = 0, cliff = 0;
        for (int k = 0; k < map.Count; k++)
        {
            if (!map.Has(k, CellFlags.Coast)) continue;
            double x = map.CellX(k % map.Width), y = map.CellY(k / map.Width);
            if (map.LandformsAt(x, y).Weight(Landform.CoastalCliffs) < 0.7f) continue;
            shore++;
            float hi = 0f;
            for (double dy = -60; dy <= 60; dy += 10)
            for (double dx = -60; dx <= 60; dx += 10)
                hi = Math.Max(hi, sampler.HeightAt(x + dx, y + dy, 4));
            if (hi > 15f) cliff++;
        }
        return shore > 0 ? cliff / (float)shore : 0f;
    }

    /// <summary>Height profiles along the wind across a dune sea: correlation one dune wavelength apart and half of one, and crest height.</summary>
    static (float lag, float half, float height) DunePeriodicity(WorldMap map, SurfaceSampler sampler)
    {
        LandformLayer layer = map.Landforms;
        double wave = map.Def.DuneWavelength > 0 ? map.Def.DuneWavelength : 160;
        var rng = new Rng(Hash.Of(map.Spec.Seed, 0x44554E));
        double lagSum = 0, halfSum = 0, heightSum = 0;
        int profiles = 0;
        for (int t = 0; t < 40000 && profiles < 16; t++)
        {
            double x0 = rng.NextFloat() * map.WidthMeters, y0 = rng.NextFloat() * map.HeightMeters;
            const int N = 300;
            const double Step = 4;
            bool ok = true;
            for (int s = 0; s <= N && ok; s += 25)
                ok = layer.At(x0 + layer.WindX * s * Step, y0 + layer.WindY * s * Step).Weight(Landform.DuneSea) > 0.9f
                    && map.Sample(map.Elevation, x0 + layer.WindX * s * Step, y0 + layer.WindY * s * Step) > 4f;
            if (!ok) continue;
            var h = new float[N];
            for (int s = 0; s < N; s++) h[s] = sampler.HeightAt(x0 + layer.WindX * s * Step, y0 + layer.WindY * s * Step, 1);
            // Take out the broad trend so only the dunes are left.
            var d = new float[N];
            for (int s = 0; s < N; s++)
            {
                float mean = 0f;
                int c = 0;
                for (int q = Math.Max(0, s - 80); q < Math.Min(N, s + 80); q++) { mean += h[q]; c++; }
                d[s] = h[s] - mean / c;
            }
            int lag = (int)Math.Round(wave / Step), halfLag = lag / 2;
            float best = float.MinValue;
            for (int lg = (int)(lag * 0.8); lg <= (int)(lag * 1.25); lg++) best = Math.Max(best, Corr(d, lg));
            lagSum += best;
            halfSum += Corr(d, halfLag);
            float lo = float.MaxValue, hi = float.MinValue;
            for (int s = 0; s < N; s++) { lo = Math.Min(lo, d[s]); hi = Math.Max(hi, d[s]); }
            heightSum += hi - lo;
            profiles++;
        }
        return profiles == 0 ? (0f, 1f, 0f) : ((float)(lagSum / profiles), (float)(halfSum / profiles), (float)(heightSum / profiles));
    }

    static float Corr(float[] d, int lag)
    {
        double num = 0, a = 0, b = 0;
        for (int s = 0; s + lag < d.Length; s++)
        {
            num += d[s] * d[s + lag];
            a += d[s] * d[s];
            b += d[s + lag] * d[s + lag];
        }
        return a > 0 && b > 0 ? (float)(num / Math.Sqrt(a * b)) : 0f;
    }

    /// <summary>Channels crossed along arcs across each delta fan (land to water changes), on average per arc.</summary>
    static (float channels, int arcs) DeltaChannels(WorldMap map)
    {
        var sampler = new SurfaceSampler(map);
        double total = 0;
        int arcs = 0;
        foreach (DeltaFan f in map.Landforms.Deltas)
        {
            for (double frac = 0.45; frac <= 0.86; frac += 0.2)
            {
                double r = f.Radius * frac;
                bool? wasWet = null;
                int crossings = 0, land = 0;
                for (double t = -1.1; t <= 1.1; t += 2.0 / r)
                {
                    double ca = Math.Cos(t), sa = Math.Sin(t);
                    double x = f.ApexX + (f.AxisX * ca - f.AxisY * sa) * r, y = f.ApexY + (f.AxisY * ca + f.AxisX * sa) * r;
                    if (map.LandformsAt(x, y).Weight(Landform.Delta) < 0.6f) { wasWet = null; continue; }
                    bool wet = sampler.HeightAt(x, y, 2) < 0f;
                    if (!wet) land++;
                    if (wasWet == false && wet) crossings++;
                    wasWet = wet;
                }
                if (land < 20) continue;
                total += crossings;
                arcs++;
            }
        }
        return arcs > 0 ? ((float)(total / arcs), arcs) : (0f, 0);
    }

    /// <summary>Room to build, drive and fly low: a temperate world has at least a quarter of its land under a gentle slope.</summary>
    void FlatLand()
    {
        foreach (int v in new[] { 0, 1 })
        {
            var shares = new List<float>();
            for (ulong s = 0; s < 6; s++)
            {
                WorldMap map = PlanetGenerator.Generate(PlanetSpec.Roll(5200 + s * 41, Archetype.Temperate, v, 16));
                shares.Add(GentleShare(map, new SurfaceSampler(map)));
                GD.Print($"      seed {map.Spec.Seed}: {shares[^1]:P0} gentle; {string.Join(", ", map.Landforms.Present().Select(t => $"{LandformInfo.Name(t.landform)} {t.share:P0}"))}");
            }
            float mean = shares.Average(), min = shares.Min();
            Check(mean >= 0.3f && min >= 0.22f,
                $"flat land, temperate/{Archetypes.Get(Archetype.Temperate, v).VariantName}: {mean:P0} of the land under 5° on average over 6 worlds, " +
                $"least {min:P0} ({string.Join(", ", shares.Select(x => x.ToString("P0")))}); want about a quarter at least");
        }
    }

    /// <summary>Share of dry land with a slope under 5° across 8 m.</summary>
    static float GentleShare(WorldMap map, SurfaceSampler sampler, int points = 1500)
    {
        var rng = new Rng(Hash.Of(map.Spec.Seed, 0x464C4154));
        int gentle = 0, n = 0;
        for (int t = 0; t < points * 20 && n < points; t++)
        {
            double x = rng.NextFloat() * map.WidthMeters, y = rng.NextFloat() * map.HeightMeters;
            if (map.Sample(map.Elevation, x, y) < 0.3f) continue;
            if (sampler.HeightAt(x, y, 1) < 0.3f) continue;
            n++;
            float gx = (sampler.HeightAt(x + 4, y, 1) - sampler.HeightAt(x - 4, y, 1)) / 8f;
            float gy = (sampler.HeightAt(x, y + 4, 1) - sampler.HeightAt(x, y - 4, 1)) / 8f;
            if (MathF.Sqrt(gx * gx + gy * gy) < 0.0875f) gentle++;
        }
        return n > 0 ? gentle / (float)n : 0f;
    }

    /// <summary>A tuning aid (-- --only=Survey): every variant's mixes, landforms, land, rivers, timing and flat share.</summary>
    void Survey()
    {
        foreach (var (a, v) in Variants())
        {
            for (ulong s = 0; s < 3; s++)
            {
                WorldMap map = PlanetGenerator.Generate(PlanetSpec.Roll(3100 + s * 37, a, v, 16));
                var sampler = new SurfaceSampler(map);
                string present = string.Join(", ", map.Landforms.Present().Select(t => $"{LandformInfo.Name(t.landform)} {t.share:P0}"));
                GD.Print($"survey {a}/{map.Def.VariantName} seed {map.Spec.Seed}: land {map.LandFraction:P0} (target {map.LandTarget:P0}), {map.Rivers.Count} rivers, " +
                         $"{map.GenerationMs:F0} ms, gentle {GentleShare(map, sampler, 600):P0}; {present}");
            }
        }
    }
}

public partial class GenTest
{
    /// <summary>A tuning aid (-- --only=Debug --world=a:v:seed): per landform, land cells, gentle share and the worst relief window.</summary>
    void DebugWorld(string world)
    {
        string[] p = world.Split(':');
        var spec = PlanetSpec.Roll(ulong.Parse(p[2]), (Archetype)int.Parse(p[0]), int.Parse(p[1]), 16);
        WorldMap map = PlanetGenerator.Generate(spec);
        var sampler = new SurfaceSampler(map);
        GD.Print($"debug {spec}: mix {map.Model.Mix}; land {map.LandFraction:P0}");
        var cells = new int[LandformInfo.Count];
        for (int k = 0; k < map.Count; k++) if (!map.Has(k, CellFlags.Water)) cells[map.Landform[k]]++;
        var rng = new Rng(5);
        var gentle = new int[LandformInfo.Count];
        var counted = new int[LandformInfo.Count];
        for (int t = 0; t < 6000; t++)
        {
            double x = rng.NextFloat() * map.WidthMeters, y = rng.NextFloat() * map.HeightMeters;
            if (sampler.HeightAt(x, y, 1) < 0.3f) continue;
            int l = (int)map.LandformsAt(x, y).Dominant;
            counted[l]++;
            float gx = (sampler.HeightAt(x + 4, y, 1) - sampler.HeightAt(x - 4, y, 1)) / 8f;
            float gy = (sampler.HeightAt(x, y + 4, 1) - sampler.HeightAt(x, y - 4, 1)) / 8f;
            if (MathF.Sqrt(gx * gx + gy * gy) < 0.0875f) gentle[l]++;
        }
        foreach (Landform l in LandformInfo.All)
            if (counted[(int)l] > 0)
                GD.Print($"debug   {LandformInfo.Name(l),-15} {cells[(int)l] / (float)map.Count:P1} of map cells, {counted[(int)l]} points, {gentle[(int)l] / (float)counted[(int)l]:P0} gentle");
        // The first few plains-only windows: where are their lowest and highest points?
        int shown = 0;
        for (int t = 0; t < 200000 && shown < 4; t++)
        {
            double x = rng.NextFloat() * map.WidthMeters, y = rng.NextFloat() * map.HeightMeters;
            Landform dom = map.LandformsAt(x, y).Dominant;
            if (dom != Landform.Hills || map.LandformsAt(x, y).W0 < 0.95f || map.Landforms.CoastDistance(x, y) < 300) continue;
            float lo = float.MaxValue, hi = float.MinValue;
            double lx = 0, ly = 0, hx = 0, hy = 0;
            for (double dy = -500; dy <= 500; dy += 25)
            for (double dx = -500; dx <= 500; dx += 25)
            {
                float h = Math.Max(0f, sampler.HeightAt(x + dx, y + dy, 8));
                if (h < lo) { lo = h; lx = x + dx; ly = y + dy; }
                if (h > hi) { hi = h; hx = x + dx; hy = y + dy; }
            }
            GD.Print($"debug   hills window at {x:F0},{y:F0} (h {sampler.HeightAt(x, y, 8):F1}): low {lo:F1} at {lx:F0},{ly:F0} [{map.LandformsAt(lx, ly)}], high {hi:F1} at {hx:F0},{hy:F0} [{map.LandformsAt(hx, hy)}]");
            shown++;
        }
    }

    /// <summary>A tuning aid (-- --list=a:v:first:count): each seed's landforms, from the layer alone (fast).</summary>
    void ListWorlds(string what)
    {
        string[] p = what.Split(':');
        var a = (Archetype)int.Parse(p[0]);
        int v = int.Parse(p[1]);
        ulong first = ulong.Parse(p[2]), count = ulong.Parse(p[3]);
        for (ulong s = first; s < first + count; s++)
        {
            TerrainModel m = ModelOf(PlanetSpec.Roll(s, a, v, 16));
            GD.Print($"list {a}/{v} seed {s}: {string.Join(", ", m.Landforms.Present(0.004f).Select(t => $"{LandformInfo.Name(t.landform)} {t.share:P0}"))}; deltas {m.Landforms.Deltas.Count}");
        }
    }
}
