using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Terrain;
using Planets.Gen;
using PoiGenerator.Gen;
using PoiSite = PlanetSurface.Poi.PoiSite;

namespace PlanetSurface.Infra;

/// <summary>
/// Where a start begins (design doc start rule, the owner's correction): at the edge of an old
/// world's ruined town, near the landing site and away from any living settlement. Abandoned houses
/// line a few streets, a broken main street carries rusted cars, grass and shrubs are taking it all
/// back, and a place stands on the horizon. The slice's start-site search starts from
/// <see cref="Edge"/>, the town's outer edge facing the place in view.
/// </summary>
public sealed class StartTown
{
    public double CenterX, CenterZ;
    /// <summary>The main street's direction, a unit vector on a map axis, toward the place in view.</summary>
    public double AlongX, AlongZ;
    /// <summary>Which side of the main street (+1 or -1 across) the back street runs on.</summary>
    public int BackSide = 1;
    /// <summary>A point on open ground just past the town's last houses, on the side facing <see cref="Landmark"/>.</summary>
    public double EdgeX, EdgeZ;
    /// <summary>Which way from the edge the place in view lies (radians: the slice's heading convention, atan2(-dx, -dz)).</summary>
    public float Heading;
    /// <summary>The place on the horizon (a ruin, a wreck: nothing anyone holds), 300 to 1,200 m from the edge.</summary>
    public PoiSite Landmark;
    public readonly List<InfraRoute> Streets = new();
    public readonly List<FlavourRuin> Houses = new();
    /// <summary>What runs under the streets: sewers, a storm drain, a service gallery, a metro station (null if none could be planned).</summary>
    public UnderTown Under;

    /// <summary>How far the town keeps from any living settlement, metres (the bunker sits at its edge).</summary>
    public const double NoSettlement = 1500;
    /// <summary>The grid: the main street's half-length, the spacing of its cross streets and their reach to each side, metres. Rough country gets a smaller town.</summary>
    public double MainHalf = 130, Block = 65, Arm = 85;
    static readonly (double Main, double Block, double Arm)[] Sizes = { (130, 65, 85), (100, 50, 65), (78, 39, 52) };

    /// <summary>
    /// Plans a ruined town near the landing site on a start world: a flat, dry spot 200 m to 1.2 km out
    /// with a ruin or wreck 420 m to 1.15 km beyond it, a grid of streets on the map's axes (joined to
    /// the network by a spur), houses along them, a stalled jam on the main street. Null if no spot fits.
    /// </summary>
    public static StartTown Plan(InfraNetwork net, PoiLibrary lib, WorldMap map, PlanetTerrainSource bare, IReadOnlyList<PoiSite> pois, WorldProfile profile,
        LandingSite landing, ulong seed, List<FlavourRuin> ruins)
    {
        if (profile == null || !profile.Inhabited || lib == null) return null;
        double w = map.WidthMeters, h = map.HeightMeters;
        var rng = new PoiGenerator.Gen.Rng(PoiGenerator.Gen.Hash.Combine(seed, 0x57A27UL));
        double Dist(double ax, double az, double bx, double bz) => Math.Sqrt(Math.Pow(Mathx.WrapDelta(ax - bx, w), 2) + Math.Pow(Mathx.WrapDelta(az - bz, h), 2));
        var rails = net.Routes.Where(rt => rt.Kind == RouteKind.Rail).ToList();
        // The spot: flat, dry, clear of places, with a place in view beyond it and no settlement near.
        // The biggest town whose ground is gentle; failing that, the gentlest of the sizes.
        double bestScore = double.MaxValue, bestRank = double.MaxValue, bx = 0, bz = 0, mainHalf = 0, block = 0, arm = 0;
        PoiSite bestMark = null;
        foreach (var size in Sizes)
        {
        if (bestMark != null && bestScore <= 36) break;
        double reach = size.Main, step = reach / 5;
        for (double r = 200; r <= 1200; r += 50)
        for (int k = 0; k < 16; k++)
        {
            double a = k * Math.PI / 8 + r * 0.01;
            double cx = Mathx.WrapX(landing.X + Math.Cos(a) * r, w), cz = Mathx.Wrap(landing.Y + Math.Sin(a) * r, h);
            if (pois.Any(p => p.Family.Tags.Contains("settlement") && Dist(p.Center.X, p.Center.Z, cx, cz) < NoSettlement + 300)) continue;
            if (pois.Any(p => Dist(p.Center.X, p.Center.Z, cx, cz) < Math.Max(p.Stamp.ReachX, p.Stamp.ReachZ) + 150)) continue;
            // Not across a railway: its houses would have no room either side of the line.
            if (rails.Any(rt => Enumerable.Range(0, rt.Count).Any(i => Dist(rt.X[i], rt.Z[i], cx, cz) < reach * 1.5 + 40))) continue;
            // A deep place for choice: the first place a start walks to is one worth going down into.
            PoiSite mark = pois.Where(p => p.State != PoiState.Active && !p.Family.Tags.Contains("settlement"))
                .Select(p => (p, d: Dist(p.Center.X, p.Center.Z, cx, cz))).Where(t => t.d > 420 && t.d < 1150).OrderBy(t => !t.p.Pending && Deep(t.p.Layout) ? 0 : 1).ThenBy(t => t.d).Select(t => t.p).FirstOrDefault();
            if (mark == null) continue;
            float lo = float.MaxValue, hi = float.MinValue;
            bool wet = false;
            for (double dz = -reach; dz <= reach + 0.1; dz += step)
            for (double dx = -reach; dx <= reach + 0.1; dx += step)
            {
                GroundSample g = bare.Sampler.Sample(cx + dx, cz + dz, 8);
                if (g.InRiver || g.Height < 2f) wet = true;
                lo = Math.Min(lo, g.Height);
                hi = Math.Max(hi, g.Height);
            }
            if (wet) continue;
            double score = (hi - lo) + r * 0.004;
            // Rougher ground is worth it for a deep place in view: a start should have somewhere to go down into.
            double rank = score + (!mark.Pending && Deep(mark.Layout) ? 0 : 25);
            if (rank < bestRank) { bestRank = rank; bestScore = score; bx = cx; bz = cz; bestMark = mark; (mainHalf, block, arm) = size; }
        }
        }
        if (bestMark == null || bestScore > 85) return null;
        var town = new StartTown { Landmark = bestMark, MainHalf = mainHalf, Block = block, Arm = arm };

        // The streets: a grid on the map's axes, as a town is surveyed and its sewers laid: a main street
        // toward the place in view, three cross streets, a back street along their ends on one side.
        double mx = Mathx.WrapDelta(bestMark.Center.X - bx, w), mz = Mathx.WrapDelta(bestMark.Center.Z - bz, h);
        bool alongX = Math.Abs(mx) >= Math.Abs(mz);
        double ux = alongX ? Math.Sign(mx) : 0, uz = alongX ? 0 : Math.Sign(mz), sx = -uz, sz = ux;
        bx = Math.Floor(bx) + 0.5;
        bz = Math.Floor(bz) + 0.5;
        (town.CenterX, town.CenterZ, town.AlongX, town.AlongZ) = (bx, bz, ux, uz);
        RouteKind kind = RouteKind.Road;
        (double X, double Z) At(double along, double across) => (bx + ux * along + sx * across, bz + uz * along + sz * across);
        void Lay(params (double X, double Z)[] points)
        {
            InfraRoute s = net.Street(points.ToList(), kind);
            if (s != null) town.Streets.Add(s);
        }
        Lay(At(-mainHalf, 0), At(mainHalf, 0));
        int backSide = rng.Chance(0.5f) ? 1 : -1;
        // On the back street's side the outer cross streets and the back street are one street round two
        // rounded corners; the rest meet the main street square, at junctions with rounded kerbs.
        for (int c = -1; c <= 1; c++)
            foreach (int side in new[] { -1, 1 })
                if (side != backSide || c == 0) Lay(At(c * block, 0), At(c * block, side * arm));
        Lay(At(-block, 0), At(-block, backSide * arm), At(block, backSide * arm), At(block, 0));
        town.BackSide = backSide;
        // Joined to the old network: a spur from the town's far end to the nearest road.
        net.Spur(At(-mainHalf, 0), kind, 2500);
        // A century on: the streets uncared for.
        foreach (InfraRoute s in town.Streets) { s.Kept = false; s.Wear = 0.92f; }

        // What runs under them. Its stairs and passages come up through lots the houses leave free.
        town.Under = UnderTown.Reserve(town, profile, PoiGenerator.Gen.Hash.Combine(seed, 0x5E3EUL));
        var reserved = town.Under?.Reserved ?? new List<(int A0, int A1, int U0, int U1)>();

        // Shops along the main street, houses along the rest, set back from the kerb, both sides, a shed now and then.
        FamilyDef house = lib.Families.FirstOrDefault(f => f.Id == "flavour_house"), shed = lib.Families.FirstOrDefault(f => f.Id == "flavour_shed"), shop = lib.Families.FirstOrDefault(f => f.Id == "flavour_shop");
        if (house != null)
        {
            InfraRoute main = town.Streets.FirstOrDefault();
            foreach (InfraRoute s in town.Streets)
                for (int i = 4; i + 4 < s.Count; i += 9)
                    foreach (int side in new[] { -1, 1 })
                    {
                        double dx = s.X[i + 1] - s.X[i - 1], dz = s.Z[i + 1] - s.Z[i - 1], len = Math.Sqrt(dx * dx + dz * dz);
                        if (len < 1e-6) continue;
                        double nx = -dz / len * side, nz = dx / len * side;
                        Dir front = FlavourRuins.Facing(-nx, -nz);
                        FamilyDef type = s == main && shop != null && rng.Chance(0.75f) ? shop : shed != null && rng.Chance(0.1f) ? shed : house;
                        // One people to the town: its houses share its culture (a strange people's town is all strange), each put together afresh.
                        Layout lot = type == house
                            ? FlavourRuins.LayoutFor(lib, type, FlavourRuins.VariantFor(type, profile.Before, rng), front, profile.Before, rng.Range(0, FlavourRuins.HouseSalts),
                                new HomeHints(FlavourRuins.CultureAt(net.RegionPlan, map, s.X[i], s.Z[i]), FlavourRuins.ClimateAt(map, s.X[i], s.Z[i]), rng.Float()))
                            : FlavourRuins.LayoutFor(lib, type, FlavourRuins.VariantFor(type, profile.Before, rng), front, profile.Before, rng.Range(0, FlavourRuins.Salts));
                        if (lot == null) continue;
                        // The street runs on a map axis, so the lot lies square to it: half the lot off the kerb.
                        double half = Math.Abs(dz) > Math.Abs(dx) ? lot.Occupied.Size.X * 0.5 : lot.Occupied.Size.Z * 0.5;
                        double clear = s.HalfWidth + half + 2;
                        double lotX = s.X[i] + nx * (clear + 2), lotZ = s.Z[i] + nz * (clear + 2);
                        double la = Mathx.WrapDelta(lotX - bx, w) * ux + Mathx.WrapDelta(lotZ - bz, h) * uz, lu = Mathx.WrapDelta(lotX - bx, w) * sx + Mathx.WrapDelta(lotZ - bz, h) * sz;
                        double reach = Math.Max(lot.Occupied.Size.X, lot.Occupied.Size.Z) * 0.5 + 2;
                        if (reserved.Any(k => la + reach > k.A0 && la - reach < k.A1 && lu + reach > k.U0 && lu - reach < k.U1)) continue;
                        FlavourRuin r = FlavourRuins.Try(lib, type, lot, lotX, lotZ, bare, net, pois, ruins, w, h, rng,
                            spacing: 0, maxCut: 8f, clearance: clear);
                        if (r == null) continue;
                        r.Route = s;
                        r.State = PoiState.Ruined;
                        FlavourRuins.Connect(r, s, 1.6f, GalHack.Terrain.StampSurface.Gravel, bare, w, h);
                        ruins.Add(r);
                        town.Houses.Add(r);
                    }
        }
        town.Dungeon = FirstDungeon(lib, net, pois, bx, bz, w, h, PoiGenerator.Gen.Hash.Combine(seed, 0xD06UL));
        return town;
    }

    /// <summary>How far from the town the first dungeon may lie, metres.</summary>
    public const double DungeonReach = 2200;

    /// <summary>A deep place within reach of the start with someone or something in it, the first a start can walk down into (null if no deep place stands that near).</summary>
    public PoiSite Dungeon;

    /// <summary>A deep place: enough rooms to be somewhere you go into, two or more of them below ground, or five storeys or more of them (a tower is deep the other way).</summary>
    public static bool Deep(Layout l) => Rooms(l) >= 8 && (Below(l) >= 2 || Levels(l) >= 5);

    static int Rooms(Layout l) => l.Modules.Count(m => m.Kind is not (ModuleKind.Outdoor or ModuleKind.Stair));
    static int Below(Layout l) => l.Modules.Count(m => m.Kind != ModuleKind.Outdoor && (m.Storey < 0 || m.Underground));
    static int Levels(Layout l) => l.Modules.Where(m => m.Kind != ModuleKind.Outdoor).Select(m => m.Storey).Distinct().Count();

    /// <summary>Someone or something lives there: the Bloom, a roster, the occupancy layer's people or beasts, or a faction at home.</summary>
    public static bool Lived(PoiInstance inst) =>
        inst.State == PoiState.Infested || inst.Enemies.Count > 0 || inst.Occupancy is { Main: not Occupant.Empty }
        || inst.State == PoiState.Active && inst.Family.Faction != Faction.None;

    /// <summary>The same, read from what the world rolled for a place not yet built (its roster's own enemies aside).</summary>
    static bool Lived(PoiSite p) =>
        p.State == PoiState.Infested || p.Occupancy is { Main: not Occupant.Empty } || p.State == PoiState.Active && p.Family.Faction != Faction.None;

    /// <summary>
    /// The nearest deep place to the town that someone lives in; failing one within reach, the nearest deep place
    /// that stands empty is given people from the world's own mix (never empty), so every start has a dungeon to walk to.
    /// </summary>
    static PoiSite FirstDungeon(PoiLibrary lib, InfraNetwork net, IReadOnlyList<PoiSite> pois, double x, double z, double w, double h, ulong seed)
    {
        double Dist(PoiSite p) => Math.Sqrt(Math.Pow(Mathx.WrapDelta(p.Center.X - x, w), 2) + Math.Pow(Mathx.WrapDelta(p.Center.Z - z, h), 2));
        // Below ground first, then up a tower; failing both, the biggest place in reach that you can go into.
        var deep = pois.Where(p => !p.Pending && Deep(p.Layout) && Dist(p) <= DungeonReach).OrderBy(p => Below(p.Layout) >= 2 ? 0 : 1).ThenBy(Dist).ToList();
        if (deep.FirstOrDefault(Lived) is { } lived) return lived;
        PoiSite empty = deep.FirstOrDefault(p => OccupancyLayer.Applies(p.Family, p.State))
            ?? pois.Where(p => !p.Pending && Dist(p) <= DungeonReach && Rooms(p.Layout) >= 6 && OccupancyLayer.Applies(p.Family, p.State)).OrderByDescending(p => Rooms(p.Layout)).FirstOrDefault();
        if (empty == null) return null;
        var mix = (net.RegionPlan?.Roll.Occupancy ?? new Dictionary<string, float>()).Where(kv => kv.Key != "EMP" && kv.Value > 0f).ToDictionary(kv => kv.Key, kv => kv.Value);
        OccupancyRoll occ = OccupancyLayer.Roll(mix, empty.Family, seed);
        if (occ.Main == Occupant.Empty) occ = new OccupancyRoll(Occupant.Nesters);
        var given = occ with { Tags = net.RegionPlan?.Roll.Tags ?? new List<string>() };
        empty.Occupancy = given;
        empty.Fit.Add(inst => OccupancyLayer.Apply(lib, inst, given, new PoiGenerator.Gen.Rng(seed)));
        return empty;
    }

    /// <summary>
    /// Finds the town's edge on the ground as the town and its roads left it: past the last houses on
    /// the side toward the place in view, on open ground a person can walk out from (the slice's rule:
    /// three of eight ways open for 60 m, no rise over 1.2 m in 2 m).
    /// </summary>
    public void FindEdge(PlanetTerrainSource ground)
    {
        double w = ground.Width, h = ground.Height;
        double bx = CenterX, bz = CenterZ, ux = AlongX, uz = AlongZ, sx = -uz, sz = ux;
        double ex = bx + ux * (MainHalf + 40), ez = bz + uz * (MainHalf + 40);
        float At(double x, double z) => ground.SurfaceHeight(Mathx.WrapX(x, w), Mathx.Wrap(z, h));
        bool Walkable(double px, double pz)
        {
            int ways = 0;
            for (int d = 0; d < 8; d++)
            {
                double ang = d * Math.PI / 4;
                float prev = At(px, pz);
                bool ok = prev > 1.5f;
                for (double t = 2; t <= 60 && ok; t += 2)
                {
                    double qx = px + Math.Cos(ang) * t, qz = pz + Math.Sin(ang) * t;
                    float g = At(qx, qz);
                    ok = Math.Abs(g - prev) <= 1.0f && g > 1.5f && !ground.Sampler.Sample(Mathx.WrapX(qx, w), Mathx.Wrap(qz, h), 2).InRiver;
                    prev = g;
                }
                if (ok) ways++;
            }
            return ways >= 3;
        }
        bool found = false;
        // Toward the place in view first, then anywhere round the town's edge.
        for (double along = MainHalf + 30; along <= MainHalf + 190 && !found; along += 20)
            foreach (double across in new[] { 0.0, 30.0, -30.0, 60.0, -60.0, 100.0, -100.0 })
            {
                double tx = bx + ux * along + sx * across, tz = bz + uz * along + sz * across;
                if (!Walkable(tx, tz)) continue;
                (ex, ez, found) = (tx, tz, true);
                break;
            }
        for (int k = 0; k < 24 && !found; k++)
        {
            double a = k * Math.PI / 12, tx = bx + Math.Cos(a) * (MainHalf + 60), tz = bz + Math.Sin(a) * (MainHalf + 60);
            if (Walkable(tx, tz)) (ex, ez, found) = (tx, tz, true);
        }
        EdgeX = Mathx.WrapX(ex, w);
        EdgeZ = Mathx.Wrap(ez, h);
        // Faces the place in view from the edge, wherever on the town's rim that turned out.
        double fx = Mathx.WrapDelta(Landmark.Center.X - EdgeX, w), fz = Mathx.WrapDelta(Landmark.Center.Z - EdgeZ, h);
        Heading = MathF.Atan2((float)-fx, (float)-fz);
    }
}
