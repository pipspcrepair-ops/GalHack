using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using GalHack.Terrain;
using Planets.Gen;
using PlanetSurface.Poi;
using PoiGenerator.Gen;
using TInt3 = TerrainDigging.Terrain.Int3;
using CellBox = TerrainDigging.Terrain.CellBox;
using Rng = PoiGenerator.Gen.Rng;

namespace PlanetSurface.Infra;

/// <summary>
/// One lot of a city, as the planner hands it to the place it builds (the interface with the roads side):
/// the street it fronts, the stretch of kerb it owns (its frontage) and the point on the street's centre line
/// its driveway or forecourt meets (its crossover). Everything past the lot line is the lot's own.
/// </summary>
public sealed class CityLotPlan
{
    public string Kind = "", District = "";
    public double X0, Z0, X1, Z1;
    public Dir Front;
    public InfraRoute Street;
    public (double X0, double Z0, double X1, double Z1) Frontage;
    public (double X, double Z) Crossover;
    public CityLot Lot;
    public PoiSite Site;
}

/// <summary>
/// A ruined city on a world whose layers built one (a city-covered world, a drowned, ash-buried or overgrown
/// city): put where the land takes it best, by a river bend or a coast, its grid on the map's axes (the 2 m
/// lattice the buildings stand on) with its avenues along the water. Districts by distance from the middle:
/// a downtown of towers 8 to 30 storeys with a plaza, a domed hall and a park block; a ring of mid-rise
/// blocks; terraces of houses on the edges and works on the far side from the water; parks along the water.
/// Its streets are the road network's own (laid through <see cref="InfraNetwork.Street"/>, so they share its
/// kerbs, junctions and rounded corners); where a street meets water the planner stops and leaves the
/// crossing to the roads side, its anchor in <see cref="BridgeAnchors"/>. How far it fell (the world's fall
/// layers and age) cuts towers down to stumps and opens their tops; on a world still lived in, one district
/// keeps its lights. Each building is a place of its own, so the place layer's distance tiers draw it: one
/// merged mesh far off, merged blocks nearer, its floors only when you're close.
/// </summary>
public sealed class CityPlan
{
    /// <summary>The layers that build a city.</summary>
    public static readonly string[] Layers = { "city_covered", "overgrown_city", "dead_city_ash", "drowned_city" };

    /// <summary>Tests and proof frames: plan a city on any lived-in world, whatever its layers.</summary>
    public static bool Force;

    public double CenterX, CenterZ, Radius;
    public const double Block = 84, Avenue = 5.5, Side = 4;
    public readonly List<InfraRoute> Streets = new();
    public readonly List<CityLotPlan> Lots = new();
    public readonly List<TerrainStamp> Stamps = new();
    public readonly List<PoiSite> Sites = new();
    /// <summary>Where a street ran into water: a bridge's landing, for the roads side to build or leave broken.</summary>
    public readonly List<(double X, double Z, double DirX, double DirZ)> BridgeAnchors = new();
    public float Fall;
    public double PlanMs;
    public string Why = "";

    public static bool Wanted(WorldRoll roll) => roll != null && (Force || roll.Rolled.Any(l => Layers.Contains(l.Id)));

    public static CityPlan Plan(InfraNetwork net, PoiLibrary lib, WorldMap map, PlanetTerrainSource bare, List<PoiSite> pois, WorldProfile profile,
        LandingSite landing, ulong seed)
    {
        WorldRoll roll = net?.RegionPlan?.Roll;
        if (lib == null || profile is not { Inhabited: true } || !Wanted(roll)) return null;
        var watch = Stopwatch.StartNew();
        double w = map.WidthMeters, h = map.HeightMeters;
        var rng = new Rng(PoiGenerator.Gen.Hash.Combine(seed, 0xC171UL));
        var city = new CityPlan();

        GroundSample G(double x, double z) => bare.Sampler.Sample(Mathx.WrapX(x, w), Mathx.Wrap(z, h), 2);
        bool Wet(GroundSample g) => g.InRiver || g.Height < 2.5f;
        double Dist(double ax, double az, double bx, double bz) => Math.Sqrt(Math.Pow(Mathx.WrapDelta(ax - bx, w), 2) + Math.Pow(Mathx.WrapDelta(az - bz, h), 2));

        // The spot: land enough for the middle, gentle, with water at the edge of it (a river bend, a coast) and
        // clear of the places already there and of the landing.
        double radius = 520 + 260 * rng.Float(), bestScore = double.MaxValue, cx = 0, cz = 0;
        for (int ring = 0; ring < 7; ring++)
            for (int a = 0; a < 40; a++)
            {
                double ang = a * Math.Tau / 40 + ring * 0.37, d = 1300 + ring * 650;
                double x = landing.X + Math.Cos(ang) * d, z = landing.Y + Math.Sin(ang) * d;
                if (pois.Any(p => Dist(p.Center.X, p.Center.Z, x, z) < radius * 0.8 + Math.Max(p.Stamp.ReachX, p.Stamp.ReachZ))) continue;
                if (net.Town is StartTown t && Dist(t.CenterX, t.CenterZ, x, z) < radius + 400) continue;
                int land = 0, n = 0, coast = 0, coastN = 0;
                double sum = 0, sq = 0;
                for (double sz = -radius; sz <= radius; sz += radius / 5)
                    for (double sx = -radius; sx <= radius; sx += radius / 5)
                    {
                        double r = Math.Sqrt(sx * sx + sz * sz);
                        if (r > radius * 1.35) continue;
                        GroundSample g = G(x + sx, z + sz);
                        if (r <= radius)
                        {
                            n++;
                            if (!Wet(g)) { land++; sum += g.Height; sq += g.Height * g.Height; }
                        }
                        else { coastN++; if (Wet(g)) coast++; }
                    }
                if (G(x, z) is var mid && Wet(mid) || land < n * 0.62) continue;
                double mean = sum / land, rough = Math.Sqrt(Math.Max(0, sq / land - mean * mean));
                double wetEdge = coastN == 0 ? 0 : coast / (double)coastN;
                // Water at the edge is the point: a city by a river or a bay. Too much and it's a peninsula of nothing.
                double score = rough * 1.6 + (1 - land / (double)n) * 40 - Math.Min(wetEdge, 0.45) * 60 + d * 0.002;
                if (score < bestScore) { bestScore = score; cx = x; cz = z; }
            }
        if (bestScore == double.MaxValue) { city.Why = "no land would take a city"; return city; }
        city.CenterX = cx = Math.Floor(Mathx.WrapX(cx, w) / 2) * 2;
        city.CenterZ = cz = Math.Floor(Mathx.Wrap(cz, h) / 2) * 2;
        city.Radius = radius;

        // How far it fell: the world's fall layers and their weight, the dead-ash city most of all.
        int falls = roll.All(LayerKind.Fall).Count();
        city.Fall = Math.Clamp(0.25f + 0.12f * falls + (roll.Rolled.Any(l => l.Id == "dead_city_ash") ? 0.3f : 0f) + (profile.Current == PopulationNow.None ? 0.15f : 0f), 0.2f, 0.95f);
        bool lived = profile.Current is PopulationNow.Settled or PopulationNow.Outposts;

        // The water's side: which way from the middle the most water lies; the avenues run along it.
        double wx = 0, wz = 0;
        for (int a = 0; a < 32; a++)
        {
            double ang = a * Math.Tau / 32;
            for (double r = radius * 0.5; r <= radius * 1.4; r += radius * 0.15)
                if (Wet(G(cx + Math.Cos(ang) * r, cz + Math.Sin(ang) * r))) { wx += Math.Cos(ang); wz += Math.Sin(ang); }
        }
        bool waterAlongX = Math.Abs(wz) >= Math.Abs(wx); // water north or south: the avenues run east-west

        // The street grid, a line every block, laid in runs wherever the ground takes a street.
        int lines = (int)Math.Ceiling(radius / Block) + 1;
        double Edge(double x, double z)
        {
            // A ragged edge: the city thins out on the hills, not at a circle.
            double ang = Math.Atan2(z - cz, x - cx);
            return radius * (0.82 + 0.18 * Math.Sin(ang * 3 + seed % 7) + 0.08 * Math.Sin(ang * 7 + seed % 5));
        }
        bool In(double x, double z) => Dist(x, z, cx, cz) <= Edge(x, z);
        bool Free(double ax, double az, double bx, double bz)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            for (double t = 0; t <= 1.0001; t += 0.125)
            {
                double x = ax + (bx - ax) * t, z = az + (bz - az) * t;
                GroundSample g = G(x, z);
                if (Wet(g) || !In(x, z)) return false;
                lo = Math.Min(lo, g.Height);
                hi = Math.Max(hi, g.Height);
            }
            return hi - lo <= Block * 0.12 + 3;
        }
        var laid = new HashSet<(int, int, bool)>(); // segment from grid point (a, b) along x (true) or z
        for (int pass = 0; pass < 2; pass++)
            for (int m = -lines; m <= lines; m++)
            {
                bool alongX = pass == 0;
                var run = new List<(double X, double Z)>();
                var segs = new List<(int, int, bool)>();
                void Flush()
                {
                    if (run.Count >= 2)
                    {
                        bool avenue = Math.Abs(m) % 3 == 0 && alongX == waterAlongX;
                        InfraRoute r = net.Street(run.Select(p => (Mathx.WrapX(p.X, w), Mathx.Wrap(p.Z, h))).ToList(), RouteKind.Road);
                        if (r != null)
                        {
                            r.HalfWidth = (float)(avenue ? Avenue : Side);
                            city.Streets.Add(r);
                            foreach (var s in segs) laid.Add(s);
                        }
                    }
                    run.Clear();
                    segs.Clear();
                }
                for (int n = -lines; n < lines; n++)
                {
                    (double X, double Z) a = alongX ? (cx + n * Block, cz + m * Block) : (cx + m * Block, cz + n * Block);
                    (double X, double Z) b = alongX ? (cx + (n + 1) * Block, cz + m * Block) : (cx + m * Block, cz + (n + 1) * Block);
                    if (Free(a.X, a.Z, b.X, b.Z))
                    {
                        if (run.Count == 0) run.Add(a);
                        run.Add(b);
                        segs.Add(alongX ? (n, m, true) : (m, n, false));
                    }
                    else
                    {
                        // A street that runs into water ends at a bridge's landing; the roads side builds the crossing.
                        if (run.Count >= 2 && Wet(G((a.X + b.X) / 2, (a.Z + b.Z) / 2)))
                            city.BridgeAnchors.Add((Mathx.WrapX(a.X, w), Mathx.Wrap(a.Z, h), alongX ? 1 : 0, alongX ? 0 : 1));
                        Flush();
                    }
                }
                Flush();
            }
        if (city.Streets.Count == 0) { city.Why = "no street would lie"; return city; }

        // The blocks, by district, and their lots along the streets that bound them.
        var blocks = new List<(int A, int B, double X, double Z, double R, string District)>();
        for (int b = -lines; b < lines; b++)
            for (int a = -lines; a < lines; a++)
            {
                double x0 = cx + a * Block, z0 = cz + b * Block, mx = x0 + Block / 2, mz = z0 + Block / 2;
                bool north = laid.Contains((a, b, true)), south = laid.Contains((a, b + 1, true)), west = laid.Contains((a, b, false)), east = laid.Contains((a + 1, b, false));
                if (!north && !south) continue;
                if (!In(mx, mz)) continue;
                double r = Dist(mx, mz, cx, cz) / radius;
                // Parks along the water: a block with water within a block's width is a park, its lots left to grass.
                bool shore = false;
                for (int k = 0; k < 8 && !shore; k++)
                    shore = Wet(G(mx + Math.Cos(k * Math.PI / 4) * Block * 0.9, mz + Math.Sin(k * Math.PI / 4) * Block * 0.9));
                double side = (mx - cx) * wx + (mz - cz) * wz;
                string district = shore ? "park" : r < 0.3 ? "downtown" : r < 0.58 ? "midrise" : side < 0 && r > 0.6 ? "works" : "homes";
                blocks.Add((a, b, x0, z0, r, district));
            }
        // The middle: the nearest downtown block is a plaza, the next a domed hall, one more a park.
        var core = blocks.Where(k => k.District == "downtown").OrderBy(k => k.R).ToList();
        var special = new Dictionary<(int, int), string>();
        if (core.Count > 0) special[(core[0].A, core[0].B)] = "plaza";
        if (core.Count > 1) special[(core[1].A, core[1].B)] = "civic";
        if (core.Count > 4) special[(core[4].A, core[4].B)] = "park";

        var families = lib.Families.Where(f => f.Tags.Contains("city")).ToDictionary(f => f.Id);
        TechLevel tech = profile.Before;
        foreach (var blk in blocks)
        {
            string district = special.GetValueOrDefault((blk.A, blk.B), blk.District);
            double x0 = blk.X, z0 = blk.Z, x1 = x0 + Block, z1 = z0 + Block;
            // The block's ground, as one pad: a paved plaza, a park of the land's own ground, or the lots' footing.
            var samples = new List<float>();
            for (double sz = z0 + 10; sz <= z1 - 10; sz += (Block - 20) / 3)
                for (double sx = x0 + 10; sx <= x1 - 10; sx += (Block - 20) / 3)
                    samples.Add(G(sx, sz).Height);
            samples.Sort();
            int pad = (int)MathF.Round(samples[samples.Count / 2]);
            if (samples[^1] - samples[0] > (district == "homes" ? 14 : 8)) continue;
            double inset = Avenue + 1;
            var stamp = new TerrainStamp($"city {district}", (int)(x0 + inset), (int)(z0 + inset), (int)(x1 - inset), (int)(z1 - inset), pad, 2f, 10f, w, h)
            {
                // Paved downtown and round the blocks; lawns and yards where people lived and worked, grass in the parks.
                Surface = district is "downtown" or "midrise" or "plaza" or "civic" ? StampSurface.Concrete : StampSurface.Natural, Wear = 0.5f + city.Fall * 0.5f,
            };
            city.Stamps.Add(stamp);
            if (district is "plaza" or "park") continue;

            // The lots: back to back, each row facing the street on its side, across the block from kerb to kerb.
            foreach (bool northRow in new[] { true, false })
            {
                bool streetHere = northRow ? laid.Contains((blk.A, blk.B, true)) : laid.Contains((blk.A, blk.B + 1, true));
                if (!streetHere) continue;
                double depth = (Block - 2 * inset) / 2 - 1, zs = northRow ? z0 : z1;
                double lz0 = northRow ? z0 + inset : z1 - inset - depth, lz1 = lz0 + depth;
                var (kind, widths) = district switch
                {
                    "downtown" => ("tower", new[] { 34.0, 36.0 }),
                    "midrise" => ("midrise", new[] { 24.0, 26.0, 30.0 }),
                    "works" => ("works", new[] { 34.0, 40.0 }),
                    "civic" => ("civic", new[] { 52.0 }),
                    _ => ("terrace", new[] { 14.0, 16.0, 18.0 }),
                };
                if (district == "civic" && !northRow) continue;
                double along = x0 + inset, end = x1 - inset;
                while (true)
                {
                    double lw = widths[rng.Range(0, widths.Length)];
                    if (along + lw > end + 0.01) break;
                    double lx0 = along, lx1 = along + lw;
                    along += lw + (kind == "terrace" ? 0 : 2);
                    // Downtown, a lot now and then is a mid-rise between the towers; on the edge, one is left empty.
                    string k = kind == "tower" && rng.Chance(0.25f) ? "midrise" : kind;
                    if (k == "terrace" && rng.Chance(0.12f)) continue;
                    var lot = new CityLotPlan
                    {
                        Kind = k, District = district, X0 = lx0, X1 = lx1, Z0 = lz0, Z1 = lz1, Front = northRow ? Dir.North : Dir.South,
                        Frontage = (lx0, zs, lx1, zs), Crossover = ((lx0 + lx1) / 2, zs),
                    };
                    lot.Lot = Roll(lot, blk.R, city.Fall, rng, district == "civic" ? 52 : depth);
                    city.Lots.Add(lot);
                }
            }
        }

        // Every building laid out side by side, then set on its block's pad.
        var made = new PoiInstance[city.Lots.Count];
        var fams = new FamilyDef[city.Lots.Count];
        Parallel.For(0, city.Lots.Count, i =>
        {
            CityLotPlan lot = city.Lots[i];
            if (!families.TryGetValue("city_" + lot.Kind, out FamilyDef f)) return;
            ulong s = PoiGenerator.Gen.Hash.Combine(seed, (ulong)i * 0x9E37UL + 0xC17UL);
            Layout layout = PoiFactory.TryLayout(lib, f, (int)(s % (ulong)f.Variants.Count), f.Rarity, s, new LayoutHints(lot.Front, tech, lot.Lot), out string why);
            if (layout == null) { Godot.GD.PushWarning($"city lot left empty: {why}"); return; }
            // A district still lived in keeps its lights; the rest stand dark, the most fallen ruined.
            bool lit = lived && lot.District == "midrise" && (PoiGenerator.Gen.Hash.Combine(seed, 0x117UL) % 2 == 0) == (lot.Crossover.X > cx);
            PoiState state = lit ? PoiState.Active : lot.Lot.Fall > 0.55f ? PoiState.Ruined : PoiState.Abandoned;
            made[i] = PoiFactory.Spawn(lib, layout, state, s + 1, profile);
            fams[i] = f;
        });
        for (int i = 0; i < city.Lots.Count; i++)
        {
            if (made[i] == null) continue;
            CityLotPlan lot = city.Lots[i];
            // Nothing stands on a road the network laid before the city (a highway through it): that lot stays empty.
            double mx = (lot.X0 + lot.X1) / 2, mz = (lot.Z0 + lot.Z1) / 2;
            if (net.Nearest(Mathx.WrapX(mx, w), Mathx.Wrap(mz, h), 60, out _, out double gap) is { } near && !city.Streets.Contains(near)
                && gap < near.HalfWidth + Math.Max(lot.X1 - lot.X0, lot.Z1 - lot.Z0) / 2 + 3) continue;
            lot.Street = net.Nearest(Mathx.WrapX(lot.Crossover.X, w), Mathx.Wrap(lot.Crossover.Z, h), 20, out _, out _);
            int pad = (int)MathF.Round(city.Stamps.FirstOrDefault(s => s.MinX <= mx && mx <= s.MaxX && s.MinZ <= mz && mz <= s.MaxZ)?.PadY ?? G(mx, mz).Height);
            lot.Site = Set(made[i], fams[i], lot, pad, w, h);
            if (lot.Site != null) city.Sites.Add(lot.Site);
        }
        city.PlanMs = watch.Elapsed.TotalMilliseconds;
        return city;
    }

    /// <summary>A lot's building: its size from the lot, its storeys from the district and the distance in, cut down by the fall.</summary>
    static CityLot Roll(CityLotPlan lot, double r, float fall, Rng rng, double depth)
    {
        int wBlocks = (int)((lot.X1 - lot.X0) / 2) - 1, dBlocks = (int)(depth / 2) - 3;
        int storeys = lot.Kind switch
        {
            "tower" => (int)Math.Round(8 + 22 * Math.Pow(Math.Clamp(1 - r / 0.32, 0, 1), 1.3) * (0.55 + 0.45 * rng.Float())),
            "midrise" => rng.Range(4, 9),
            "terrace" => rng.Range(2, 4),
            _ => 1,
        };
        // A few landmarks downtown stand well over the rest.
        if (lot.Kind == "tower" && r < 0.18 && rng.Chance(0.2f)) storeys = Math.Min(30, storeys + 8);
        float f = Math.Clamp(fall * (0.5f + rng.Float()), 0f, 1f);
        // Dead towers: the most fallen stand as stumps; the rest lose their tops.
        if (storeys > 3 && f > 0.6f) storeys = Math.Max(2, (int)(storeys * (rng.Chance(0.3f) ? 0.25f : 1f - (f - 0.6f))));
        // A house is shallow, its back garden behind it; a works shed or a block fills its lot.
        if (lot.Kind == "terrace") dBlocks = Math.Min(dBlocks, rng.Range(8, 11));
        return new CityLot(lot.Kind, wBlocks, dBlocks, Math.Max(1, storeys), rng.Range(0, 1000), f);
    }

    /// <summary>A building set square on its lot at its block's pad, its front to the street: a place of its own, drawn by distance like any.</summary>
    static PoiSite Set(PoiInstance inst, FamilyDef family, CityLotPlan lot, int pad, double w, double h)
    {
        CellMask occ = inst.Layout.Occupied;
        double halfX = occ.Size.X * 0.5, halfZ = occ.Size.Z * 0.5;
        double cx = (lot.X0 + lot.X1) / 2, cz = (lot.Z0 + lot.Z1) / 2;
        // On the 2 m lattice; the front pulled up to the lot's street edge.
        int ox = (int)Math.Floor((cx - (occ.Min.X + halfX)) / 2) * 2;
        int oz = lot.Front == Dir.North ? (int)Math.Floor((lot.Z0 - occ.Min.Z) / 2) * 2 : (int)Math.Floor((lot.Z1 - (occ.Min.Z + occ.Size.Z)) / 2) * 2;
        ox = (int)Mathx.WrapX(ox, w);
        oz = (int)Mathx.Wrap(oz, h);
        var origin = new TInt3(ox, pad, oz);
        int lx0 = occ.Min.X, lz0 = occ.Min.Z, lx1 = lx0 + occ.Size.X, lz1 = lz0 + occ.Size.Z;
        var footprint = new CellBox(new TInt3(ox + lx0, pad + occ.Min.Y, oz + lz0), new TInt3(ox + lx1, pad + Math.Max(occ.Min.Y + occ.Size.Y, inst.Blocks.Min.Y + inst.Blocks.Size.Y), oz + lz1));
        var stamp = new TerrainStamp(family.Name, ox + lx0, oz + lz0, ox + lx1, oz + lz1, pad, 0f, 0f, w, h) { Surface = StampSurface.Concrete, Wear = 0.7f };
        PoiGenerator.Gen.Int3 e = inst.Layout.EntranceCell;
        PoiGenerator.Gen.Int3 outward = inst.Layout.EntranceFacing.Vec();
        var entrance = new TInt3(ox + e.X, pad + e.Y, oz + e.Z);
        return new PoiSite
        {
            Name = $"{family.Name}: {lot.District} ({inst.State.ToString().ToLowerInvariant()})",
            Family = family, Instance = inst, Origin = origin, Footprint = footprint, Stamp = stamp, Recarve = () => { },
            Siting = "city", Entrance = entrance, Gates = new List<PoiGate> { new("entrance", entrance, outward.X, outward.Z) },
        };
    }
}
