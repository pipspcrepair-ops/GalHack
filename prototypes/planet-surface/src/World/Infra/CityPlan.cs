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
/// One lot of a city and its frontage: where the place meets the street. This is the interface with the roads
/// side, which reads it from <see cref="InfraNetwork.City"/> (<see cref="CityPlan.Lots"/>). Everything from the
/// lot line in is the place's own and built by it (lawn, drive, path, fences, car park, apron, building); from
/// the lot line out to the street (verge, footpath, kerb and the crossover itself) is the roads side's to draw.
///   - <see cref="Street"/>: the street the lot fronts; <see cref="Front"/>: which way its front looks (at it).
///   - <see cref="Frontage"/>: the lot line along that street, end to end.
///   - <see cref="Gate"/>: where its drive or path crosses the lot line; <see cref="DriveWidth"/> how wide it is
///     there (a footpath's width where there is no drive).
///   - <see cref="Crossover"/>: the point on the street's centre line square across from the gate, where a
///     crossover from the kerb to the gate meets the street.
/// World metres, canonical (inside the map), except the lot's box, which is continuous about the city's middle
/// (wrap it for the map).
/// </summary>
public sealed class CityLotPlan
{
    public string Kind = "", District = "";
    public double X0, Z0, X1, Z1;
    public Dir Front;
    public InfraRoute Street;
    public (double X0, double Z0, double X1, double Z1) Frontage;
    public (double X, double Z) Gate, Crossover;
    public float DriveWidth;
    /// <summary>The lot's ground, world y.</summary>
    public int Pad;
    /// <summary>A house's lot: the open ground from its front to its street's edge, metres (0: none known).</summary>
    public double FrontGap;
    public CityLot Lot;
    public PoiSite Site;
}

/// <summary>
/// A ruined city on a world whose layers built one (a city-covered world, glass towers, a drowned, ash-buried,
/// sinking or overgrown city), fitted to the world's own land. Its spot is dry ground by a river bend or a
/// coast. Its grid lies on the map's axes (the 2 m lattice the buildings stand on), avenues along the water,
/// and its streets are the road network's own (<see cref="InfraNetwork.Street"/>), joined into one street
/// round every corner the grid turns, so a corner is a rounded curve and a street ends only at a junction or
/// the city's edge. A highway runs along the bank with parks between it and the water; where a street runs
/// into water it stops at a bridge's landing, and where the far shore lies within reach the crossing is
/// kept for the roads side to build (<see cref="Bridges"/>). Districts by distance in: a downtown of towers 8
/// to 30 storeys round a plaza, a domed hall and a park block; a ring of mid-rise blocks; works on the far
/// side from the water; houses on the edges, terraced rows nearer in and up the slopes, each lot on its own
/// ground stepping up a hillside, detached houses thinning outward; a station at the edge. How far it fell
/// (the world's fall layers and its people) tears tops, leans towers over their lots and brings some down;
/// on a world still lived in, or one half-fallen, a district keeps its lights. Each building is a place of
/// its own built lot line to lot line, so the place layer draws it by distance: merged far off, its floors
/// only near.
/// </summary>
public sealed class CityPlan
{
    /// <summary>The layers that build a city, and say what kind of city it is.</summary>
    public static readonly string[] Layers = { "city_covered", "glass_tower", "overgrown_city", "dead_city_ash", "drowned_city", "sinkage" };

    /// <summary>A world this crowded before its fall had a city, whatever its layers (a power of ten: 6 is a million).</summary>
    public const int CrowdedFrom = 6;

    /// <summary>Tests and proof frames: plan a city on any lived-in world, whatever its layers; and, if set, how far it fell.</summary>
    public static bool Force;
    public static float? ForceFall;
    /// <summary>Proof frames: build its waterfronts as a world of this tech would.</summary>
    public static TechLevel? ForceTech;

    public double CenterX, CenterZ, Radius;
    /// <summary>Which way from the middle the most water lies (a unit vector; zero with no water near).</summary>
    public double WaterX, WaterZ;
    /// <summary>The grid's pitch, centre line to centre line, and how far a lot's line stands back from a street's centre line, metres.</summary>
    public const double Block = 84, Inset = 8;
    public const float Avenue = 5.5f, Side = 4f;
    /// <summary>How far back from the water a lot keeps, metres: no building at the very edge.</summary>
    public const double Setback = 12;
    public readonly List<InfraRoute> Streets = new();
    /// <summary>The highway along the bank (null where the city has no bank to run along).</summary>
    public InfraRoute Bank;
    public readonly List<CityLotPlan> Lots = new();
    public readonly List<TerrainStamp> Stamps = new();
    public readonly List<PoiSite> Sites = new();
    /// <summary>Where a street ran into water: a bridge's landing on this side, and the way it was heading.</summary>
    public readonly List<(double X, double Z, double DirX, double DirZ)> BridgeAnchors = new();
    /// <summary>Crossings the roads side can build: from a landing here straight over the water to the far shore, kept clear at both ends.</summary>
    public readonly List<(double AX, double AZ, double BX, double BZ)> Bridges = new();
    /// <summary>The station's track bed, end to end, for the railway to run into (null if the city has no station).</summary>
    public (double X0, double Z0, double X1, double Z1)? Tracks;
    /// <summary>How many blocks went to each district (tests).</summary>
    public readonly Dictionary<string, int> Districts = new();
    public float Fall;
    public double PlanMs;
    public string Why = "";

    public static bool Wanted(WorldRoll roll, WorldProfile profile) =>
        roll != null && (Force || profile?.Population >= CrowdedFrom || roll.Rolled.Any(l => Layers.Contains(l.Id)));

    public static CityPlan Plan(InfraNetwork net, PoiLibrary lib, WorldMap map, PlanetTerrainSource bare, List<PoiSite> pois, WorldProfile profile,
        LandingSite landing, ulong seed)
    {
        WorldRoll roll = net?.RegionPlan?.Roll;
        if (lib == null || profile is not { Inhabited: true } || !Wanted(roll, profile)) return null;
        var watch = Stopwatch.StartNew();
        double w = map.WidthMeters, h = map.HeightMeters;
        var rng = new Rng(PoiGenerator.Gen.Hash.Combine(seed, 0xC171UL));
        var city = new CityPlan();

        GroundSample G(double x, double z) => bare.Sampler.Sample(Mathx.WrapX(x, w), Mathx.Wrap(z, h), 2);
        bool Wet(GroundSample g) => g.InRiver || g.Height < 2.5f;
        bool WetAt(double x, double z) => Wet(G(x, z));
        double Dist(double ax, double az, double bx, double bz) => Math.Sqrt(Math.Pow(Mathx.WrapDelta(ax - bx, w), 2) + Math.Pow(Mathx.WrapDelta(az - bz, h), 2));

        // ---- the spot: land enough for the middle, gentle, water at its edge (a river bend, a coast), a river
        // through it better still; clear of the places already there and of the landing.
        double radius = 520 + 260 * rng.Float(), bestScore = double.MaxValue, cx = 0, cz = 0;
        for (int ring = 0; ring < 7; ring++)
            for (int a = 0; a < 40; a++)
            {
                double ang = a * Math.Tau / 40 + ring * 0.37, d = 1300 + ring * 650;
                double x = landing.X + Math.Cos(ang) * d, z = landing.Y + Math.Sin(ang) * d;
                if (pois.Any(p => Dist(p.Center.X, p.Center.Z, x, z) < radius * 0.8 + Math.Max(p.Stamp.ReachX, p.Stamp.ReachZ))) continue;
                if (net.Town is StartTown t && Dist(t.CenterX, t.CenterZ, x, z) < radius + 400) continue;
                int land = 0, n = 0, coast = 0, coastN = 0, river = 0;
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
                            else if (g.InRiver && r > radius * 0.25) river++;
                        }
                        else { coastN++; if (Wet(g)) coast++; }
                    }
                if (G(x, z) is var mid && Wet(mid) || land < n * 0.62) continue;
                // A city stands on firm ground: not on a beach or in the dunes.
                if (PlanetSurface.World.Footing.SandShare(map, x, z, radius * 0.7, radius * 0.7, 7) > 0.2f) continue;
                double mean = sum / land, rough = Math.Sqrt(Math.Max(0, sq / land - mean * mean));
                double wetEdge = coastN == 0 ? 0 : coast / (double)coastN;
                // Water at the edge is the point: a city by a river or a bay. Too much and it's a peninsula of nothing.
                double score = rough * 1.6 + (1 - land / (double)n) * 40 - Math.Min(wetEdge, 0.45) * 60 - Math.Min(river, 4) * 6 + d * 0.002;
                if (score < bestScore) { bestScore = score; cx = x; cz = z; }
            }
        if (bestScore == double.MaxValue) { city.Why = "no land would take a city"; return city; }
        city.CenterX = cx = Math.Floor(Mathx.WrapX(cx, w) / 2) * 2;
        city.CenterZ = cz = Math.Floor(Mathx.Wrap(cz, h) / 2) * 2;
        city.Radius = radius;

        // How far it fell: the world's fall layers and their weight, the dead-ash city most of all.
        int falls = roll.All(LayerKind.Fall).Count();
        city.Fall = ForceFall ?? Math.Clamp(0.25f + 0.12f * falls + (roll.Rolled.Any(l => l.Id == "dead_city_ash") ? 0.3f : 0f) + (profile.Current == PopulationNow.None ? 0.15f : 0f), 0.2f, 0.95f);
        bool lived = profile.Current is PopulationNow.Settled or PopulationNow.Outposts;

        // The water's side: which way from the middle the most water lies; the avenues run along it.
        double wx = 0, wz = 0;
        for (int a = 0; a < 32; a++)
        {
            double ang = a * Math.Tau / 32;
            for (double r = radius * 0.5; r <= radius * 1.4; r += radius * 0.15)
                if (WetAt(cx + Math.Cos(ang) * r, cz + Math.Sin(ang) * r)) { wx += Math.Cos(ang); wz += Math.Sin(ang); }
        }
        bool waterAlongX = Math.Abs(wz) >= Math.Abs(wx); // water north or south: the avenues run east-west
        double wl = Math.Sqrt(wx * wx + wz * wz);
        if (wl > 1e-6) (city.WaterX, city.WaterZ) = (wx / wl, wz / wl);

        // ---- the street grid: every stretch of grid line the ground takes, a block long.
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
            // Streets keep back from the water: the waterfront road on its wall runs between them and it.
            double len = Math.Max(1e-6, Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az))), px = -(bz - az) / len * 22, pz = (bx - ax) / len * 22;
            for (double t = 0; t <= 1.0001; t += 0.125)
            {
                double x = ax + (bx - ax) * t, z = az + (bz - az) * t;
                GroundSample g = G(x, z);
                if (Wet(g) || !In(x, z) || WetAt(x + px, z + pz) || WetAt(x - px, z - pz)) return false;
                lo = Math.Min(lo, g.Height);
                hi = Math.Max(hi, g.Height);
            }
            return hi - lo <= Block * 0.15 + 3;
        }
        (double X, double Z) Node(int a, int b) => (cx + a * Block, cz + b * Block);
        var laid = new HashSet<(int A, int B, bool X)>(); // the stretch from node (a, b) along x (true) or z
        for (int b = -lines; b <= lines; b++)
            for (int a = -lines; a <= lines; a++)
            {
                var p = Node(a, b);
                foreach (bool alongX in new[] { true, false })
                {
                    var q = alongX ? Node(a + 1, b) : Node(a, b + 1);
                    if (Free(p.X, p.Z, q.X, q.Z)) laid.Add((a, b, alongX));
                    // A street that runs into water ends at a bridge's landing; the roads side builds the crossing.
                    else if (In(p.X, p.Z) && !WetAt(p.X, p.Z) && WetAt((p.X + q.X) / 2, (p.Z + q.Z) / 2))
                        city.BridgeAnchors.Add((Mathx.WrapX(p.X, w), Mathx.Wrap(p.Z, h), alongX ? 1 : 0, alongX ? 0 : 1));
                }
            }
        // Lone stretches no street could reach are dropped.
        laid.RemoveWhere(s => Degree(laid, s.A, s.B) + Degree(laid, s.X ? s.A + 1 : s.A, s.X ? s.B : s.B + 1) <= 2);
        if (laid.Count == 0) { city.Why = "no street would lie"; return city; }

        // ---- the streets: the stretches joined into streets that run straight on through every crossing and
        // turn round every corner the grid turns, longest first, so a street that ends at another meets it.
        foreach (List<(int A, int B)> chain in Chains(laid).OrderByDescending(c => c.Count))
        {
            var points = chain.Select(n => { var (x, z) = Node(n.A, n.B); return (Mathx.WrapX(x, w), Mathx.Wrap(z, h)); }).ToList();
            bool alongX = chain[1].B == chain[0].B;
            int line = alongX ? chain[0].B : chain[0].A;
            bool avenue = Math.Abs(line) % 3 == 0 && alongX == waterAlongX;
            InfraRoute r = net.Street(points, RouteKind.Road, round: InfraNetwork.TownRadius(RouteKind.Road));
            if (r == null) continue;
            r.HalfWidth = avenue ? Avenue : Side;
            // Paved streets, cracking; only a city long fallen lets the grass back into them.
            r.Wear = 0.2f + city.Fall * 0.5f;
            city.Streets.Add(r);
        }
        if (city.Streets.Count == 0) { city.Why = "no street would lie"; return city; }

        // ---- the waterfronts: a highway along the main bank and promenades along the rest, each on a wall over the water.
        Waterfronts(city, net, cx, cz, radius, WetAt, (x, z) => G(x, z).Height, Dist, w, h, ForceTech ?? profile.Before, seed);
        // The city joined to the old network: from the far ends of its two longest avenues out to the nearest road.
        var own = new HashSet<InfraRoute>(city.Streets);
        foreach (Seawall s in city.Waterfront) own.Add(s.Road);
        foreach (InfraRoute a in city.Streets.Where(s => s.HalfWidth >= Avenue).OrderByDescending(s => s.Count).Take(2))
        {
            int far = Dist(a.X[0], a.Z[0], cx, cz) > Dist(a.X[^1], a.Z[^1], cx, cz) ? 0 : a.Count - 1;
            net.Spur((a.X[far], a.Z[far]), RouteKind.Road, 3000, own);
        }

        // ---- bridges: from a landing straight over the water to dry ground within reach on the far side.
        foreach (var (ax, az, dx, dz) in city.BridgeAnchors)
            foreach (double sgn in new[] { 1.0, -1.0 })
            {
                double ox = ax + dx * sgn * Block * 0.5, oz = az + dz * sgn * Block * 0.5;
                if (!WetAt(ox, oz)) continue;
                double far = 0;
                for (double t = Block * 0.5; t <= 1600; t += 8)
                    if (!WetAt(ax + dx * sgn * t, az + dz * sgn * t)) { far = t; break; }
                if (far >= 90 && city.Bridges.All(b => Dist(b.AX, b.AZ, ax, az) > Block * 2.5))
                    city.Bridges.Add((ax, az, Mathx.WrapX(ax + dx * sgn * (far + 12), w), Mathx.Wrap(az + dz * sgn * (far + 12), h)));
            }

        // ---- the blocks, by district.
        var blocks = new List<(int A, int B, double X, double Z, double R, string District)>();
        double side(double x, double z) => (x - cx) * wx + (z - cz) * wz;
        for (int b = -lines; b < lines; b++)
            for (int a = -lines; a < lines; a++)
            {
                double x0 = cx + a * Block, z0 = cz + b * Block, mx = x0 + Block / 2, mz = z0 + Block / 2;
                bool north = laid.Contains((a, b, true)), south = laid.Contains((a, b + 1, true));
                if (!north && !south || !In(mx, mz)) continue;
                double r = Dist(mx, mz, cx, cz) / radius;
                // Parks along the water: a block the water comes within half a block of is left to grass.
                bool shore = false;
                for (int k = 0; k < 8 && !shore; k++)
                    shore = WetAt(mx + Math.Cos(k * Math.PI / 4) * Block * 0.55, mz + Math.Sin(k * Math.PI / 4) * Block * 0.55);
                string district = shore ? "park" : r < 0.36 ? "downtown" : r < 0.6 ? "midrise" : side(mx, mz) < 0 && r > 0.62 ? "works" : "homes";
                // Towers, blocks of flats and works want firm ground: a block on sand is left open; houses, being light, may stand on it.
                if (district is not ("park" or "homes") && PlanetSurface.World.Footing.SandShare(map, mx, mz, Block * 0.45, Block * 0.45, 4) > 0.2f) district = "park";
                blocks.Add((a, b, x0, z0, r, district));
            }
        // What else stands on the ground: the network's roads not the city's own, and the other places.
        var other = new List<(double X, double Z, double Reach)>();
        foreach (InfraRoute r in net.Routes)
        {
            if (city.Streets.Contains(r)) continue;
            for (int i = 0; i < r.Count; i++)
                if (Dist(r.X[i], r.Z[i], cx, cz) < radius * 1.4) other.Add((cx + Mathx.WrapDelta(r.X[i] - cx, w), cz + Mathx.WrapDelta(r.Z[i] - cz, h), r.HalfWidth + 3));
        }
        foreach (var bridge in city.Bridges)
            for (int k = 0; k < 4; k++) other.Add((cx + Mathx.WrapDelta(bridge.AX - cx, w) - (bridge.BX - bridge.AX) / Math.Max(1, Dist(bridge.AX, bridge.AZ, bridge.BX, bridge.BZ)) * k * 12,
                cz + Mathx.WrapDelta(bridge.AZ - cz, h) - (bridge.BZ - bridge.AZ) / Math.Max(1, Dist(bridge.AX, bridge.AZ, bridge.BX, bridge.BZ)) * k * 12, 14));
        bool Clear(double x0, double z0, double x1, double z1)
        {
            // Nothing stands at the water's edge: a lot keeps back from it, behind the waterfront's wall.
            for (double x = x0 - Setback; x <= x1 + Setback + 0.01; x += 8)
                foreach (double z in new[] { z0 - Setback, (z0 + z1) / 2, z1 + Setback })
                    if (WetAt(x, z)) return false;
            for (double z = z0 - Setback; z <= z1 + Setback + 0.01; z += 8)
                foreach (double x in new[] { x0 - Setback, x1 + Setback })
                    if (WetAt(x, z)) return false;
            foreach (var (x, z, reach) in other)
                if (x > x0 - reach && x < x1 + reach && z > z0 - reach && z < z1 + reach) return false;
            foreach (PoiSite p in pois)
            {
                double px = cx + Mathx.WrapDelta(p.Center.X - cx, w), pz = cz + Mathx.WrapDelta(p.Center.Z - cz, h);
                if (Math.Abs(px - (x0 + x1) / 2) < (x1 - x0) / 2 + p.Stamp.ReachX + 4 && Math.Abs(pz - (z0 + z1) / 2) < (z1 - z0) / 2 + p.Stamp.ReachZ + 4) return false;
            }
            return true;
        }
        float Rise(double x0, double z0)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            for (double sz = z0 + 10; sz <= z0 + Block - 10; sz += (Block - 20) / 3)
                for (double sx = x0 + 10; sx <= x0 + Block - 10; sx += (Block - 20) / 3)
                {
                    float y = G(sx, sz).Height;
                    (lo, hi) = (Math.Min(lo, y), Math.Max(hi, y));
                }
            return hi - lo;
        }
        bool Takes((int A, int B, double X, double Z, double R, string District) k) => Rise(k.X, k.Z) <= 12 && Clear(k.X + Inset, k.Z + Inset, k.X + Block - Inset, k.Z + Block - Inset);

        // The middle: the nearest downtown block is a plaza, the next that will take it a domed hall, one more a park;
        // a station at the edge away from the water, on the first block there that will take it.
        var core = blocks.Where(k => k.District == "downtown").OrderBy(k => k.R).ToList();
        var special = new Dictionary<(int, int), string>();
        if (core.Count > 0) special[(core[0].A, core[0].B)] = "plaza";
        if (core.Skip(1).FirstOrDefault(Takes) is { District: not null } hall) special[(hall.A, hall.B)] = "civic";
        if (core.Count > 4) special.TryAdd((core[4].A, core[4].B), "park");
        var edge = blocks.Where(k => k.District is "homes" or "works" && k.R > 0.6 && laid.Contains((k.A, k.B, true))).OrderBy(k => side(k.X + Block / 2, k.Z + Block / 2)).ToList();
        if (edge.FirstOrDefault(Takes) is { District: not null } station) special[(station.A, station.B)] = "station";
        // Schools among the houses, about one to every eight blocks of them; a university only in a big city.
        var homeBlocks = blocks.Where(k => k.District == "homes" && !special.ContainsKey((k.A, k.B))).OrderBy(k => PoiGenerator.Gen.Hash.Combine(seed, (ulong)(k.A * 7919 + k.B * 104729 + 1 << 20))).ToList();
        foreach (var k in homeBlocks.Where(Takes).Take(Math.Max(homeBlocks.Count > 0 ? 1 : 0, homeBlocks.Count / 8))) special[(k.A, k.B)] = "school";
        if (radius >= 640 && blocks.Where(k => k.District == "midrise" && !special.ContainsKey((k.A, k.B))).OrderBy(k => k.R).Skip(1).FirstOrDefault(Takes) is { District: not null } campus)
            special[(campus.A, campus.B)] = "university";

        // ---- the lots: back to back, each row facing the street on its side, kerb line to kerb line.
        var families = lib.Families.Where(f => f.Tags.Contains("city")).ToDictionary(f => f.Id);
        TechLevel tech = profile.Before;
        foreach (var blk in blocks)
        {
            string district = special.GetValueOrDefault((blk.A, blk.B), blk.District);
            city.Districts[district] = city.Districts.GetValueOrDefault(district) + 1;
            double x0 = blk.X, z0 = blk.Z, x1 = x0 + Block, z1 = z0 + Block;
            var samples = new List<float>();
            for (double sz = z0 + 10; sz <= z1 - 10; sz += (Block - 20) / 3)
                for (double sx = x0 + 10; sx <= x1 - 10; sx += (Block - 20) / 3)
                    samples.Add(G(sx, sz).Height);
            samples.Sort();
            int blockPad = (int)MathF.Round(samples[samples.Count / 2]);
            float rise = samples[^1] - samples[0];
            // A block sits level with the streets round it: their decks along its sides, where it has them.
            var decks = new List<float>();
            foreach (double t in new[] { 0.25, 0.5, 0.75 })
                foreach (var (sx, sz) in new[] { (x0 + Block * t, z0), (x0 + Block * t, z1), (x0, z0 + Block * t), (x1, z0 + Block * t) })
                    if (NearestOf(city.Streets, Mathx.WrapX(sx, w), Mathx.Wrap(sz, h), 8, w, h, out int at) is { } s) decks.Add(s.Y[at]);
            if (decks.Count >= 3) blockPad = (int)MathF.Round(decks.Average());
            bool homes = district == "homes";
            // A city cuts and fills its blocks level, up to a point; houses step up steeper ground lot by lot.
            if (rise > (homes ? 26 : 12)) continue;
            // The block's ground as one pad, but where houses step up a slope lot by lot: paved downtown, grass in the parks.
            // (A park by the water keeps the land's own shape down to the waterfront's wall.)
            if (!homes && blk.District != "park")
                city.Stamps.Add(new TerrainStamp($"city {district}", (int)(x0 + Inset), (int)(z0 + Inset), (int)(x1 - Inset), (int)(z1 - Inset), blockPad, 2f, 10f, w, h)
                {
                    Surface = district is "park" or "school" or "university" ? StampSurface.Lawn : district is "works" or "station" ? StampSurface.Gravel : StampSurface.Concrete,
                    Wear = 0.5f + city.Fall * 0.5f, Wild = district == "park",
                });
            if (district is "plaza" or "park") continue;

            double depth = (Block - 2 * Inset) / 2;
            if (district is "civic" or "station" or "school" or "university")
            {
                // A whole block, facing the street on its north (or south) side.
                bool northRow = laid.Contains((blk.A, blk.B, true));
                AddLot(city, district, district, x0 + Inset, z0 + Inset, x1 - Inset, z1 - Inset, northRow ? Dir.North : Dir.South, blockPad, blk.R, rng, Clear);
                continue;
            }
            // Out on the edges the houses thin: a lot left empty more often the further out it is.
            double vacant = homes ? Math.Clamp((blk.R - 0.62) * 1.6, 0.04, 0.6) : 0;
            bool steep = rise > 8;
            foreach (bool northRow in new[] { true, false })
            {
                if (!laid.Contains(northRow ? (blk.A, blk.B, true) : (blk.A, blk.B + 1, true))) continue;
                double lz0 = northRow ? z0 + Inset : z1 - Inset - depth, lz1 = lz0 + depth;
                double along = x0 + Inset, end = x1 - Inset;
                var row = new List<CityLotPlan>();
                while (true)
                {
                    // Terraces nearer in and up the slopes; detached houses further out.
                    string kind = district switch
                    {
                        "downtown" => rng.Chance(0.22f) ? "midrise" : "tower",
                        "midrise" => "midrise",
                        "works" => "works",
                        _ => steep || blk.R < 0.72 ? "terrace" : "house",
                    };
                    double[] widths = kind switch
                    {
                        "tower" => new[] { 34.0, 36.0 },
                        "midrise" => new[] { 24.0, 26.0, 30.0 },
                        "works" => new[] { 34.0, 36.0, 40.0 },
                        "terrace" => new[] { 18.0, 26.0, 34.0 },
                        _ => new[] { 20.0, 22.0, 24.0 },
                    };
                    double lw = widths[rng.Range(0, widths.Length)];
                    if (along + lw > end + 0.01) lw = widths.Where(x => along + x <= end + 0.01).DefaultIfEmpty(0).Min();
                    if (lw <= 0) break;
                    double lx0 = along, lx1 = along + lw;
                    along += lw + (homes ? 0 : 2);
                    if (rng.Chance((float)vacant)) continue;
                    if (AddLot(city, district, kind, lx0, lz0, lx1, lz1, northRow ? Dir.North : Dir.South, blockPad, blk.R, rng, Clear) is { } lot) row.Add(lot);
                }
                // The last lot of a row (its right as the street sees it) fences its right side too.
                if (row.Count > 0)
                {
                    CityLotPlan last = northRow ? row.MaxBy(l => l.X0) : row.MinBy(l => l.X0);
                    last.Lot = last.Lot with { EndRight = true };
                }
            }
        }

        // ---- a police station and a fire station to each district: the wide lot nearest its middle, then the next.
        foreach (var group in city.Lots.Where(l => l.District is "downtown" or "midrise" or "homes" or "works").GroupBy(l => l.District))
        {
            double gx = group.Average(l => (l.X0 + l.X1) / 2), gz = group.Average(l => (l.Z0 + l.Z1) / 2);
            var wide = group.Where(l => l.X1 - l.X0 >= 30 && l.Kind is not ("civic" or "station")).OrderBy(l => Math.Abs((l.X0 + l.X1) / 2 - gx) + Math.Abs((l.Z0 + l.Z1) / 2 - gz)).Take(2).ToList();
            for (int i = 0; i < wide.Count; i++)
            {
                wide[i].Kind = i == 0 ? "police" : "fire";
                wide[i].Lot = wide[i].Lot with { Kind = wide[i].Kind, Storeys = 2, Lean = false, Toppled = false };
            }
        }

        // ---- each lot's ground: houses on their own street's deck, so a row steps up the hill; the rest on the
        // block's pad. Then where a neighbour's ground stands higher, a retaining wall on this side.
        foreach (CityLotPlan lot in city.Lots)
        {
            if (lot.District != "homes") continue;
            double gx = (lot.X0 + lot.X1) / 2, gz = lot.Front == Dir.North ? lot.Z0 - Inset : lot.Z1 + Inset;
            InfraRoute s = NearestOf(city.Streets, gx, gz, 20, w, h, out int at);
            lot.Pad = s != null ? (int)MathF.Round(s.Y[at]) : (int)MathF.Round(G(gx, gz).Height);
            lot.FrontGap = s != null ? Inset - s.HalfWidth : 0;
        }
        var byCell = new Dictionary<(long, long), CityLotPlan>();
        foreach (CityLotPlan lot in city.Lots)
            for (double x = lot.X0 + 1; x < lot.X1; x += 2)
                for (double z = lot.Z0 + 1; z < lot.Z1; z += 2) byCell[((long)Math.Floor(x / 2), (long)Math.Floor(z / 2))] = lot;
        int Higher(CityLotPlan lot, double x, double z) =>
            byCell.TryGetValue(((long)Math.Floor(x / 2), (long)Math.Floor(z / 2)), out CityLotPlan n) && n != lot ? Math.Clamp(n.Pad - lot.Pad, 0, 8) : 0;
        foreach (CityLotPlan lot in city.Lots.Where(l => l.District == "homes"))
        {
            double mx = (lot.X0 + lot.X1) / 2, mz = (lot.Z0 + lot.Z1) / 2;
            bool north = lot.Front == Dir.North;
            int back = Higher(lot, mx, north ? lot.Z1 + 1 : lot.Z0 - 1);
            // Left and right as the place sees them, standing in the lot facing its street.
            int east = Higher(lot, lot.X1 + 1, mz), west = Higher(lot, lot.X0 - 1, mz);
            lot.Lot = lot.Lot with { Back = back, Left = north ? west : east, Right = north ? east : west };
        }

        // ---- the homes: one culture to the whole city, in its weather; further out, better off. The house
        // rules (Homes.CityHouse) build every house and terrace from it, so no two neighbours are one model.
        {
            // The region's people (FlavourRuins.CultureAt), so the streets round the city are of its kind too.
            int culture = FlavourRuins.CultureAt(net?.RegionPlan, map, cx, cz);
            var climate = FlavourRuins.ClimateAt(map, cx, cz);
            foreach (CityLotPlan lot in city.Lots.Where(l => l.Kind is "house" or "terrace"))
            {
                double far = Math.Sqrt(Math.Pow(Mathx.WrapDelta((lot.X0 + lot.X1) / 2 - cx, w), 2) + Math.Pow(Mathx.WrapDelta((lot.Z0 + lot.Z1) / 2 - cz, h), 2)) / radius;
                lot.Lot = lot.Lot with { Home = new HomeHints(culture, climate, (float)Math.Clamp(0.2 + 0.7 * far, 0, 1)) };
            }
        }

        // ---- the buildings, laid out side by side, then set on their lots.
        var made = new PoiInstance[city.Lots.Count];
        var fams = new FamilyDef[city.Lots.Count];
        ulong lightSeed = PoiGenerator.Gen.Hash.Combine(seed, 0x117UL);
        // A world still lived in, or one only half fallen, keeps the lights on in a district or two.
        bool anyLit = lived || city.Fall is > 0.3f and < 0.62f;
        double litAngle = (lightSeed % 360) * Math.PI / 180;
        Parallel.For(0, city.Lots.Count, i =>
        {
            CityLotPlan lot = city.Lots[i];
            if (!families.TryGetValue("city_" + lot.Kind, out FamilyDef f)) return;
            ulong s = PoiGenerator.Gen.Hash.Combine(seed, (ulong)i * 0x9E37UL + 0xC17UL);
            Layout layout = PoiFactory.TryLayout(lib, f, (int)(s % (ulong)f.Variants.Count), f.Rarity, s, new LayoutHints(lot.Front, tech, lot.Lot), out string why);
            if (layout == null) { Godot.GD.PushWarning($"city lot left empty: {why}"); return; }
            double ang = Math.Atan2((lot.Z0 + lot.Z1) / 2 - cz, (lot.X0 + lot.X1) / 2 - cx);
            bool lit = anyLit && lot.District is "midrise" or "downtown" && Math.Abs(Math.IEEERemainder(ang - litAngle, Math.Tau)) < (lived ? 1.2 : 0.7);
            PoiState state = lit ? PoiState.Active : lot.Lot.Fall > 0.55f ? PoiState.Ruined : PoiState.Abandoned;
            // On a fallen world its services lean how it went (a police station to a gang or the stripped, a
            // school to a shelter, a campus to a cult), but only among who the world's theme and fall have.
            Dictionary<string, float> lean = lot.Kind switch
            {
                "police" => new() { ["GNG"] = 3f, ["FAC"] = 2f, ["SCV"] = 2f },
                "school" => new() { ["SQT"] = 3f },
                "university" => new() { ["CLT"] = 3f, ["GNG"] = 1.5f },
                _ => null,
            };
            OccupancyRoll occ = !OccupancyLayer.Applies(f, state) ? null : OccupancyLayer.Roll(roll.Occupancy, f, s + 1, lean, OccupancyLayer.Peopled(roll, profile) && OccupancyLayer.Livable(f, state)) with { Tags = roll.Tags };
            made[i] = PoiFactory.Spawn(lib, layout, state, s + 1, profile, occ);
            fams[i] = f;
        });
        // Houses on their pads lowest first, so where two lots meet the higher one's edge wins.
        foreach (int i in Enumerable.Range(0, city.Lots.Count).OrderBy(i => city.Lots[i].Pad))
        {
            if (made[i] == null) continue;
            CityLotPlan lot = city.Lots[i];
            lot.Site = Set(made[i], fams[i], lot, w, h);
            city.Sites.Add(lot.Site);
            // The frontage: the lot line along the street, the gate in it, the crossover square across on the street's centre line.
            bool alongX = lot.Front is Dir.North or Dir.South;
            double line = lot.Front switch { Dir.North => lot.Z0, Dir.South => lot.Z1, Dir.West => lot.X0, _ => lot.X1 };
            lot.Frontage = alongX ? (Mathx.WrapX(lot.X0, w), Mathx.Wrap(line, h), Mathx.WrapX(lot.X1, w), Mathx.Wrap(line, h))
                : (Mathx.WrapX(line, w), Mathx.Wrap(lot.Z0, h), Mathx.WrapX(line, w), Mathx.Wrap(lot.Z1, h));
            TInt3 g = lot.Site.World(made[i].Layout.Extras.Gate);
            (double X, double Z) gate = alongX ? (g.X + 0.5, line) : (line, g.Z + 0.5);
            lot.Gate = (Mathx.WrapX(gate.X, w), Mathx.Wrap(gate.Z, h));
            int out1 = lot.Front is Dir.North or Dir.West ? -1 : 1;
            (double X, double Z) cross = alongX ? (gate.X, line + out1 * Inset) : (line + out1 * Inset, gate.Z);
            lot.Street = NearestOf(city.Streets, cross.X, cross.Z, 12, w, h, out _);
            lot.Crossover = (Mathx.WrapX(cross.X, w), Mathx.Wrap(cross.Z, h));
            lot.DriveWidth = lot.Kind switch { "house" => 4f, "tower" or "midrise" or "police" => 6f, "works" => 16f, "fire" => 12f, "station" => 8f, "terrace" => 2f, _ => 4f };
            // The station's track bed lies along its lot between its platforms (CityBlocks.Station), 49 m back from the lot line.
            if (lot.Kind == "station")
            {
                double tz = lot.Front == Dir.North ? lot.Z0 + 49 : lot.Z1 - 49;
                city.Tracks = (Mathx.WrapX(lot.X0, w), Mathx.Wrap(tz, h), Mathx.WrapX(lot.X1, w), Mathx.Wrap(tz, h));
            }
        }
        net.Lots.AddRange(city.Lots.Where(l => l.Site != null));
        city.PlanMs = watch.Elapsed.TotalMilliseconds;
        return city;
    }

    /// <summary>A lot, if the ground there is clear: its building's size from the lot, its storeys from the district and the distance in, cut down by the fall.</summary>
    static CityLotPlan AddLot(CityPlan city, string district, string kind, double x0, double z0, double x1, double z1, Dir front, int pad, double r, Rng rng,
        Func<double, double, double, double, bool> clear)
    {
        if (!clear(x0, z0, x1, z1)) return null;
        var lot = new CityLotPlan { Kind = kind, District = district, X0 = x0, Z0 = z0, X1 = x1, Z1 = z1, Front = front, Pad = pad };
        int wBlocks = (int)Math.Round((x1 - x0) / 2), dBlocks = (int)Math.Round((z1 - z0) / 2);
        int storeys = kind switch
        {
            "tower" => (int)Math.Round(8 + 22 * Math.Pow(Math.Clamp(1 - r / 0.38, 0, 1), 0.8) * (0.4 + 0.6 * rng.Float())),
            "midrise" => rng.Range(4, 9),
            "terrace" => rng.Range(2, 4),
            "house" => rng.Range(1, 3),
            _ => 1,
        };
        // A few landmarks downtown stand well over the rest.
        if (kind == "tower" && r < 0.22 && rng.Chance(0.25f)) storeys = Math.Min(30, storeys + rng.Range(6, 11));
        float f = Math.Clamp(city.Fall * (0.5f + rng.Float()), 0f, 1f);
        // The most fallen towers came down onto their stumps; others lean over their lots; the rest lose their tops.
        bool toppled = storeys > 6 && f > 0.75f && rng.Chance(0.35f), lean = !toppled && storeys > 6 && f > 0.5f && rng.Chance(0.3f);
        if (toppled) storeys = Math.Max(2, storeys / 4);
        else if (storeys > 3 && f > 0.6f && !lean) storeys = Math.Max(2, (int)(storeys * (1f - (f - 0.6f))));
        lot.Lot = new CityLot(kind, wBlocks, dBlocks, Math.Max(1, storeys), rng.Range(0, 1 << 12), f, lean, toppled);
        city.Lots.Add(lot);
        return lot;
    }

    /// <summary>A building set square on its lot, its front to the street, on the 2 m lattice: a place of its own, drawn by distance like any. Its pad is the whole lot.</summary>
    static PoiSite Set(PoiInstance inst, FamilyDef family, CityLotPlan lot, double w, double h)
    {
        // The place's cell (0, 0, 0) is the corner of the lot at its front's left (Plot.Cell): turned, that cell lies just inside the corner.
        var (ox, oz) = lot.Front switch
        {
            Dir.North => (lot.X0, lot.Z0),
            Dir.South => (lot.X1, lot.Z1),
            Dir.East => (lot.X1, lot.Z0),
            _ => (lot.X0, lot.Z1),
        };
        int px = (int)Math.Round(ox / 2) * 2, pz = (int)Math.Round(oz / 2) * 2, pad = lot.Pad;
        int x0 = (int)Math.Round(lot.X0), z0 = (int)Math.Round(lot.Z0), x1 = (int)Math.Round(lot.X1), z1 = (int)Math.Round(lot.Z1);
        BlockGrid g = inst.Blocks;
        int top = pad + Math.Max(inst.Layout.Occupied.Min.Y + inst.Layout.Occupied.Size.Y, g.Min.Y + g.Size.Y);
        var footprint = new CellBox(new TInt3((int)Mathx.WrapX(x0, w), pad + Math.Min(0, inst.Layout.Occupied.Min.Y), (int)Mathx.Wrap(z0, h)),
            new TInt3((int)Mathx.WrapX(x0, w) + (x1 - x0), top, (int)Mathx.Wrap(z0, h) + (z1 - z0)));
        bool green = lot.Kind is "house" or "terrace" or "civic";
        var stamp = new TerrainStamp(family.Name, (int)Mathx.WrapX(x0, w), (int)Mathx.Wrap(z0, h), (int)Mathx.WrapX(x0, w) + (x1 - x0), (int)Mathx.Wrap(z0, h) + (z1 - z0), pad,
            0f, lot.District == "homes" ? 1.5f : 0f, w, h)
        {
            Surface = green ? StampSurface.Lawn : lot.Kind is "works" or "station" ? StampSurface.Gravel : StampSurface.Concrete,
            Wear = 0.4f + lot.Lot.Fall * 0.5f,
        };
        var origin = new TInt3((int)Mathx.WrapX(px, w), pad, (int)Mathx.Wrap(pz, h));
        PoiGenerator.Gen.Int3 e = inst.Layout.EntranceCell;
        PoiGenerator.Gen.Int3 outward = inst.Layout.EntranceFacing.Vec();
        var entrance = new TInt3(origin.X + e.X, pad + e.Y, origin.Z + e.Z);
        // A house's front garden and pavement: its lot's level out to a metre short of the street's edge (the street is at
        // that level there), so the land between never stands up against its front wall: cut into a hill, the 1.5 m the
        // lot eases back over left a ditch along the wall's foot and a bank up to the street.
        var extra = new List<TerrainStamp>();
        int front = (int)Math.Floor(lot.FrontGap - 0.5);
        if (lot.District == "homes" && front >= 1) extra.Add(Strip(lot, family.Name + " front", lot.Front, front, stamp, w, h));
        return new PoiSite
        {
            Extra = extra,
            Name = $"{family.Name}: {lot.District} ({inst.State.ToString().ToLowerInvariant()})",
            Family = family, Instance = inst, Origin = origin, Footprint = footprint, Stamp = stamp, Recarve = () => { },
            Siting = "city", Entrance = entrance, Gates = new List<PoiGate> { new("entrance", entrance, outward.X, outward.Z) },
        };
    }

    /// <summary>A strip of a lot's own level along one side of it, out to <paramref name="depth"/> metres, easing back to the land over 1.5 m.</summary>
    static TerrainStamp Strip(CityLotPlan lot, string name, Dir side, int depth, TerrainStamp pad, double w, double h, int? from = null, int? to = null)
    {
        int x0 = (int)Math.Round(lot.X0), z0 = (int)Math.Round(lot.Z0), x1 = (int)Math.Round(lot.X1), z1 = (int)Math.Round(lot.Z1);
        // Only part of the side's length, where given (world x along a north or south side, z along an east or west one).
        if (side is Dir.North or Dir.South) { x0 = from ?? x0; x1 = to ?? x1; } else { z0 = from ?? z0; z1 = to ?? z1; }
        var (fx0, fz0, fx1, fz1) = side switch
        {
            Dir.North => (x0, z0 - depth, x1, z0),
            Dir.South => (x0, z1, x1, z1 + depth),
            Dir.East => (x1, z0, x1 + depth, z1),
            _ => (x0 - depth, z0, x0, z1),
        };
        return new TerrainStamp(name, (int)Mathx.WrapX(fx0, w), (int)Mathx.Wrap(fz0, h), (int)Mathx.WrapX(fx0, w) + (fx1 - fx0), (int)Mathx.Wrap(fz0, h) + (fz1 - fz0), lot.Pad,
            0f, 1.5f, w, h) { Surface = pad.Surface, Wear = pad.Wear };
    }

    /// <summary>
    /// A house's sides with no house beside them once the city stands as it will (a row's end, an empty lot, one that
    /// came to nothing or a strike took): a yard's width of its level there too, added to its place's stamps and returned
    /// to lay. Cut into a hill, a row's end had the hill a metre off its end wall.
    /// </summary>
    public List<TerrainStamp> Yards(double w, double h)
    {
        // Built ground beside it: another place's pad, or a block's.
        var pads = Sites.Select(s => s.Stamp).Concat(Stamps).ToList();
        var yards = new List<TerrainStamp>();
        // Lowest first, as the houses' own pads, so where two yards meet the higher one's edge wins.
        foreach (CityLotPlan lot in Lots.Where(l => l.Site != null && l.District == "homes").OrderBy(l => l.Pad))
        {
            bool Open(double x, double z)
            {
                double wx = Mathx.WrapX(x, w), wz = Mathx.Wrap(z, h);
                return !pads.Any(t => t != lot.Site.Stamp && t.Weight(wx, wz) >= 0.99f);
            }
            int x0 = (int)Math.Round(lot.X0), z0 = (int)Math.Round(lot.Z0), x1 = (int)Math.Round(lot.X1), z1 = (int)Math.Round(lot.Z1);
            Dir back = lot.Front == Dir.North ? Dir.South : Dir.North;
            // Along each side but the front, a metre out, in 2 m steps: a yard over each run of open ground (lots
            // behind needn't line up with this one, so part of a back can be open and part built).
            foreach (Dir side in new[] { Dir.East, Dir.West, back })
            {
                bool alongX = side is Dir.North or Dir.South;
                int a0 = alongX ? x0 : z0, a1 = alongX ? x1 : z1;
                double outAt = side switch { Dir.East => x1 + 1, Dir.West => x0 - 1, Dir.South => z1 + 1, _ => z0 - 1 };
                int? run = null;
                for (int a = a0; a < a1 + 2; a += 2)
                {
                    bool open = a < a1 && (alongX ? Open(a + 1, outAt) : Open(outAt, a + 1));
                    if (open && run == null) run = a;
                    if (open || run == null) continue;
                    TerrainStamp yard = Strip(lot, lot.Site.Family.Name + " yard", side, 2, lot.Site.Stamp, w, h, run, Math.Min(a, a1));
                    lot.Site.Extra.Add(yard);
                    yards.Add(yard);
                    run = null;
                }
            }
        }
        return yards;
    }

    /// <summary>How many stretches meet at a grid node.</summary>
    static int Degree(HashSet<(int A, int B, bool X)> laid, int a, int b) =>
        (laid.Contains((a, b, true)) ? 1 : 0) + (laid.Contains((a - 1, b, true)) ? 1 : 0) + (laid.Contains((a, b, false)) ? 1 : 0) + (laid.Contains((a, b - 1, false)) ? 1 : 0);

    /// <summary>
    /// The grid's stretches as streets: at every node a street runs straight on where it can, and turns where
    /// the node is only a corner (two stretches square to each other), so it ends only where it meets another
    /// street side-on, or at the city's edge. Each street is its list of nodes.
    /// </summary>
    static List<List<(int A, int B)>> Chains(HashSet<(int A, int B, bool X)> laid)
    {
        // The stretches out of a node: east, west, south, north.
        (int A, int B, bool X)? Out(int a, int b, int dir) => dir switch
        {
            0 => laid.Contains((a, b, true)) ? (a, b, true) : null,
            1 => laid.Contains((a - 1, b, true)) ? (a - 1, b, true) : null,
            2 => laid.Contains((a, b, false)) ? (a, b, false) : null,
            _ => laid.Contains((a, b - 1, false)) ? (a, b - 1, false) : null,
        };
        (int A, int B) Other((int A, int B, bool X) s, (int A, int B) n) =>
            n == (s.A, s.B) ? (s.X ? (s.A + 1, s.B) : (s.A, s.B + 1)) : (s.A, s.B);
        int DirOf((int A, int B) from, (int A, int B) to) => to.A > from.A ? 0 : to.A < from.A ? 1 : to.B > from.B ? 2 : 3;
        // Arriving at a node heading one way, the stretch the street carries on along (null where it ends).
        (int A, int B, bool X)? Next((int A, int B) n, int heading)
        {
            if (Out(n.A, n.B, heading) is { } on) return on;
            int count = 0;
            for (int d = 0; d < 4; d++) if (Out(n.A, n.B, d) != null) count++;
            if (count != 2) return null; // a T or a crossing the street meets side-on, or an end
            for (int d = 0; d < 4; d++)
                if (d != (heading ^ 1) && Out(n.A, n.B, d) is { } turn) return turn; // a corner: round it
            return null;
        }
        var used = new HashSet<(int A, int B, bool X)>();
        var chains = new List<List<(int A, int B)>>();
        void Walk((int A, int B, bool X) first, (int A, int B) start)
        {
            var nodes = new List<(int A, int B)> { start };
            (int A, int B, bool X)? s = first;
            (int A, int B) at = start;
            while (s is { } cur && used.Add(cur))
            {
                var to = Other(cur, at);
                nodes.Add(to);
                s = Next(to, DirOf(at, to));
                at = to;
            }
            if (nodes.Count >= 2) chains.Add(nodes);
        }
        // From every end first (a node where a street stops), then whatever is left (closed loops).
        foreach (var s in laid.OrderBy(s => s.A).ThenBy(s => s.B).ThenBy(s => s.X))
            foreach (var n in new[] { (s.A, s.B), s.X ? (s.A + 1, s.B) : (s.A, s.B + 1) })
            {
                if (used.Contains(s)) break;
                var beyond = Other(s, n);
                if (Next(n, DirOf(beyond, n)) == null) Walk(s, n);
            }
        foreach (var s in laid.OrderBy(s => s.A).ThenBy(s => s.B).ThenBy(s => s.X))
            if (!used.Contains(s)) Walk(s, (s.A, s.B));
        // Straight runs through crossings were walked as one street; merge pieces that meet end to end on a line.
        return chains;
    }

    static InfraRoute NearestOf(List<InfraRoute> routes, double x, double z, double within, double w, double h, out int index)
    {
        index = -1;
        InfraRoute best = null;
        double bestD = within;
        foreach (InfraRoute r in routes)
            for (int i = 0; i < r.Count; i++)
            {
                double dx = Mathx.WrapDelta(r.X[i] - x, w), dz = Mathx.WrapDelta(r.Z[i] - z, h);
                if (Math.Abs(dx) > bestD || Math.Abs(dz) > bestD) continue;
                double d = Math.Sqrt(dx * dx + dz * dz);
                if (d < bestD) { bestD = d; best = r; index = i; }
            }
        return best;
    }

    /// <summary>
    /// A waterfront: a road along the water on top of a wall (the owner's rule: where the city meets the water it
    /// meets it with a retaining wall, never sloping shore or an overhang). The road's terrain stamp keeps no bank
    /// on the wall's side (<see cref="InfraRoute.WallSide"/>), so past the wall's line the land drops to the water;
    /// the infrastructure layer draws the wall from the road's line: a seawall with a parapet, a quay with
    /// bollards, a boulder revetment where the drop is tall, timber piling on a low-tech world; stairs down at
    /// intervals; cracked, slumped and breached on a fallen world, weed at the waterline.
    /// </summary>
    public sealed class Seawall
    {
        public InfraRoute Road;
        public TechLevel Tech;
        public float Fall;
        public ulong Seed;
        /// <summary>A quay where boats tied up (bollards along it), not a plain seawall: the main bank, near the middle.</summary>
        public bool Quay;
    }

    /// <summary>The city's waterfronts, the main bank's first (it carries <see cref="Bank"/>).</summary>
    public readonly List<Seawall> Waterfront = new();

    /// <summary>
    /// The waterfronts: from the middle out, the first water in each direction marks the bank; every unbroken
    /// stretch of it long enough, smoothed, is laid as a road a few metres back from the water on a wall, the
    /// longest a highway joined to the network at both ends, the rest promenades.
    /// </summary>
    static void Waterfronts(CityPlan city, InfraNetwork net, double cx, double cz, double radius, Func<double, double, bool> wet, Func<double, double, float> height,
        Func<double, double, double, double, double> dist, double w, double h, TechLevel tech, ulong seed)
    {
        const int Rays = 180;
        var bank = new double[Rays];
        for (int a = 0; a < Rays; a++)
        {
            double ang = a * Math.Tau / Rays, c = Math.Cos(ang), s = Math.Sin(ang);
            bank[a] = double.NaN;
            for (double r = radius * 0.35; r <= radius * 1.15; r += 4)
                if (wet(cx + c * r, cz + s * r)) { bank[a] = r; break; }
        }
        // The runs of rays that found one bank, round the circle, longest first: a jump between neighbours is another shore (an inlet, a far coast).
        bool Joined(int a)
        {
            int p = (a + Rays - 1) % Rays;
            return !double.IsNaN(bank[a]) && !double.IsNaN(bank[p]) && Math.Abs(bank[a] - bank[p]) <= 30;
        }
        var runs = new List<(int Start, int Len)>();
        for (int a = 0; a < Rays; a++)
        {
            if (double.IsNaN(bank[a]) || Joined(a)) continue;
            int len = 1;
            while (len < Rays && Joined((a + len) % Rays)) len++;
            if (len >= 10) runs.Add((a, len));
        }
        if (runs.Count == 0 && Enumerable.Range(0, Rays).All(Joined)) runs.Add((0, Rays - 1)); // water all round: a ring broken at one point
        foreach (var (start, length) in runs.OrderByDescending(r => r.Len))
        {
            bool main = city.Waterfront.Count == 0;
            RouteKind kind = main ? RouteKind.Highway : RouteKind.Road;
            float half = main ? InfraNetwork.HalfWidthOf(RouteKind.Highway) : Side;
            // The road's middle so far back from the first water that the wall's line stands a few metres short of it.
            double back = half + TerrainStamp.WallStep + 4;
            var pts = new List<(double X, double Z)>();
            for (int k = 2; k < length - 2; k++)
            {
                // The bank's distance eased over its neighbours, so a creek mouth doesn't kink the road.
                double sum = 0;
                int n = 0;
                for (int j = -3; j <= 3; j++)
                {
                    int i = k + j;
                    if (i < 0 || i >= length) continue;
                    sum += bank[(start + i) % Rays];
                    n++;
                }
                double r = sum / n - back, ang = (start + k) % Rays * Math.Tau / Rays;
                double x = cx + Math.Cos(ang) * r, z = cz + Math.Sin(ang) * r;
                if (wet(x, z) || wet(x + Math.Cos(ang) * (half + 1), z + Math.Sin(ang) * (half + 1))) { if (pts.Count >= 4) break; pts.Clear(); continue; }
                if (pts.Count > 0 && dist(pts[^1].X, pts[^1].Z, x, z) < 24) continue;
                pts.Add((x, z));
            }
            if (pts.Count < 4) continue;
            InfraRoute road = net.Street(pts.Select(p => (Mathx.WrapX(p.X, w), Mathx.Wrap(p.Z, h))).ToList(), kind, round: InfraNetwork.TownRadius(kind));
            if (road == null || road.Count < 6) continue;
            road.HalfWidth = half;
            road.Wear = 0.2f + city.Fall * 0.5f;
            // The wall on the side away from the middle, where the water is.
            int m = road.Count / 2;
            double dx = road.X[m + 1] - road.X[m - 1], dz = road.Z[m + 1] - road.Z[m - 1];
            double ox = Mathx.WrapDelta(road.X[m] - cx, w), oz = Mathx.WrapDelta(road.Z[m] - cz, h);
            road.WallSide = -dz * ox + dx * oz >= 0 ? 1 : -1;
            // The top stands over the land in front of the wall everywhere, filled behind, the grade held: the land drops away in front.
            float step = InfraNetwork.MaxGrade(kind) * InfraNetwork.Spacing;
            for (int i = 0; i < road.Count; i++)
            {
                int i0 = Math.Max(0, i - 1), i1 = Math.Min(road.Count - 1, i + 1);
                double ex = road.X[i1] - road.X[i0], ez = road.Z[i1] - road.Z[i0], el = Math.Max(1e-6, Math.Sqrt(ex * ex + ez * ez));
                double nx = -ez / el * road.WallSide, nz = ex / el * road.WallSide, reach = half + TerrainStamp.WallStep;
                float front = Math.Max(height(road.X[i] + nx * reach, road.Z[i] + nz * reach), height(road.X[i] + nx * (reach + 3), road.Z[i] + nz * (reach + 3)));
                road.Y[i] = Math.Max(road.Y[i], front + 0.8f);
            }
            for (int i = 1; i < road.Count; i++) road.Y[i] = Math.Max(road.Y[i], road.Y[i - 1] - step);
            for (int i = road.Count - 2; i >= 0; i--) road.Y[i] = Math.Max(road.Y[i], road.Y[i + 1] - step);
            city.Waterfront.Add(new Seawall { Road = road, Tech = tech, Fall = city.Fall, Seed = PoiGenerator.Gen.Hash.Combine(seed, (ulong)road.Id), Quay = main && tech != TechLevel.Low });
            if (main)
            {
                city.Bank = road;
                var own = new HashSet<InfraRoute>(city.Streets) { road };
                net.Spur(pts[0], RouteKind.Highway, 2500, own);
                net.Spur(pts[^1], RouteKind.Highway, 2500, own);
            }
            if (city.Waterfront.Count >= 4) break;
        }
    }
}
