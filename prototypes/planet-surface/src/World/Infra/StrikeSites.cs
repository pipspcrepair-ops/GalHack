using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Terrain;
using Planets.Gen;
using PoiGenerator.Gen;
using PoiSite = PlanetSurface.Poi.PoiSite;

namespace PlanetSurface.Infra;

/// <summary>
/// A war's strikes on a planet (<see cref="StrikePlan"/>, data/strikes.txt), once its places, its city
/// and its roads are known: what was worth striking, where each blast came down, the crater a ground
/// burst punched beside its target (never under a place, on a road, in the city's streets or in the
/// water), and the bridges it dropped. The places round each blast take its decay and its dead ground
/// with every other blast (<see cref="PlanetSurface.Poi.PoiSiting.Blast"/>, the ground palette), so the
/// map's craters and the ruined places agree. Deterministic from the planet seed.
/// </summary>
public static class StrikeSites
{
    /// <summary>Craters keep clear of the band either side of a seam, as places do.</summary>
    const double SeamMargin = 320;

    /// <summary>On a start world no strike's flattened ring reaches within this of the landing (the bunker and the town by it), nor its faint fade the landing itself.</summary>
    public const double StartSpared = 1800;

    /// <summary>How far round a target the roads that lead to it are its approaches, metres past its edge.</summary>
    const double ApproachReach = 1600;

    /// <summary>The shallowest a crater's bowl is, as a share of its radius (on land too low for its own depth).</summary>
    const float ShallowestBowl = 0.08f;

    /// <summary>
    /// Plans the strikes of a world's war (none for a fall that dropped nothing), punches their craters and drops the
    /// bridges they hit. A ground burst that came down in a city or a town takes what stood in its bowl: those
    /// places (and their stamps) go from <paramref name="pois"/> and <paramref name="stamps"/>, and the roads through
    /// it are cut there.
    /// </summary>
    public static List<Strike> Plan(StrikeBook book, WorldRoll roll, InfraNetwork net, WorldMap map, PlanetTerrainSource bare, List<PoiSite> pois,
        WorldProfile profile, LandingSite landing, IReadOnlyList<FlavourRuin> ruins, bool start, ulong seed, List<TerrainStamp> stamps = null)
    {
        if (book == null || roll == null || profile is not { Inhabited: true } || !roll.All(LayerKind.Fall).Any(f => book.For(f.Id) != null)) return new List<Strike>();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        double w = map.WidthMeters, h = map.HeightMeters;
        List<StrikeTarget> targets = Targets(book, net, pois, profile.Before, w, h);
        var spare = new List<(double X, double Z, double R)>();
        if (start)
        {
            spare.Add((landing.X, landing.Y, StartSpared));
            if (net.Town is StartTown town) spare.Add((town.CenterX, town.CenterZ, StartSpared * 0.6));
        }
        var land = new Land(map, bare, net, pois, ruins, Approaches(net, targets, w, h));
        List<Strike> strikes = StrikePlan.Make(book, roll, profile.Before, targets, land, seed, spare);
        foreach (Strike s in strikes.Where(s => s.Bowl && s.Within)) Raze(s, net, pois, stamps, w, h);
        // A bowl punched into low land is shallower: its floor stays above the sea.
        foreach (Strike s in strikes.Where(s => s.Bowl)) s.Depth = MathF.Max(s.Crater * ShallowestBowl, MathF.Min(s.Depth, land.Floor(s) - 3f));
        foreach (Strike s in strikes.Where(s => s.Bowl))
        {
            TerrainStamp bowl = land.Bowl(s);
            net.Craters.Add(bowl);
            // Tracks and drives laid later keep off it too.
            net.KeepOff(bowl);
        }
        DropBridges(net, strikes, w, h, seed);
        net.Targets.AddRange(targets);
        net.StrikeMs = watch.Elapsed.TotalMilliseconds;
        return strikes;
    }

    /// <summary>
    /// What stood in a crater punched into the middle of a city or a town is gone: every place whose footprint
    /// reaches into its bowl and thrown rim (with its stamps, its city lot and its share of the city's own
    /// ground), and every road through it, cut at the hole.
    /// </summary>
    static void Raze(Strike s, InfraNetwork net, List<PoiSite> pois, List<TerrainStamp> stamps, double w, double h)
    {
        double reach = s.BowlReach;
        var gone = pois.Where(p => !p.Primary && p.GroundZero == null
                                   && Dist(p.Center.X, p.Center.Z, s.X, s.Z, w, h) - Math.Max(p.Stamp.MaxX - p.Stamp.MinX, p.Stamp.MaxZ - p.Stamp.MinZ) * 0.5 < reach).ToHashSet();
        foreach (PoiSite p in gone)
        {
            pois.Remove(p);
            net.Places.Remove(p);
            if (stamps != null) foreach (TerrainStamp t in p.Stamps) stamps.Remove(t);
        }
        net.Lots.RemoveAll(l => l.Site != null && gone.Contains(l.Site));
        if (net.City is { } city)
        {
            city.Sites.RemoveAll(gone.Contains);
            city.Lots.RemoveAll(l => l.Site != null && gone.Contains(l.Site));
            foreach (TerrainStamp t in city.Stamps.Where(t => Dist(t.CenterX, t.CenterZ, s.X, s.Z, w, h) < reach).ToList())
            {
                city.Stamps.Remove(t);
                stamps?.Remove(t);
            }
        }
        foreach (InfraRoute r in net.Routes)
        {
            if (gone.Contains(r.From)) (r.From, r.FromGate) = (null, -1);
            if (gone.Contains(r.To)) (r.To, r.ToGate) = (null, -1);
        }
        var pieces = net.Sever(s.X, s.Z, reach + 8);
        // The city's streets are what is left of them.
        if (net.City is { } c)
        {
            c.Streets.AddRange(pieces.Where(p => c.Streets.Contains(p.From)).Select(p => p.Piece));
            c.Streets.RemoveAll(r => r.Count < 2);
        }
    }

    /// <summary>Points every 40 m along the roads near the targets (not on their own ground): where a war's shelling walks its approaches.</summary>
    static List<(double X, double Z)> Approaches(InfraNetwork net, List<StrikeTarget> targets, double w, double h)
    {
        var points = new List<(double X, double Z)>();
        if (targets.Count == 0) return points;
        foreach (InfraRoute r in net.Routes.Where(r => r.Kind != RouteKind.Rail))
            for (int i = 0; i + 1 < r.Count; i += 10)
            {
                if (r.Span[i] != RoadSpan.Ground) continue;
                double x = Mathx.WrapX(r.X[i], w), z = Mathx.Wrap(r.Z[i], h);
                bool near = false;
                foreach (StrikeTarget t in targets)
                {
                    double d = Dist(t.X, t.Z, x, z, w, h);
                    if (d < t.Reach + 40) { near = false; break; }
                    near |= d < t.Reach + ApproachReach;
                }
                if (near) points.Add((x, z));
            }
        return points;
    }

    static double Dist(double ax, double az, double bx, double bz, double w, double h)
    {
        double dx = Mathx.WrapDelta(ax - bx, w), dz = Mathx.WrapDelta(az - bz, h);
        return Math.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>
    /// Everything a strike could aim at on this world, by its words (a place's tier, family, tags and the plan's
    /// role for it; the city; the bridges and junctions of the network) and data/strikes.txt's rules. The old
    /// capital is the city where there is one, else the first town's heart, else the main port.
    /// </summary>
    public static List<StrikeTarget> Targets(StrikeBook book, InfraNetwork net, IReadOnlyList<PoiSite> pois, TechLevel tech, double w, double h)
    {
        var targets = new List<StrikeTarget>();
        CityPlan city = net.City is { Sites.Count: > 0 } c ? c : null;
        var lots = city != null ? new HashSet<PoiSite>(city.Sites) : new HashSet<PoiSite>();
        object capital = (object)city ?? pois.Where(p => p.Planned?.Role == "hub").OrderBy(p => p.Planned.Index).FirstOrDefault() ?? pois.FirstOrDefault(p => p.Primary);
        foreach (PoiSite p in pois)
        {
            // A city's buildings are the city; a blast site is a blast already.
            if (lots.Contains(p) || p.GroundZero != null) continue;
            var words = new HashSet<string> { "tier." + p.Tier.ToString().ToLowerInvariant(), "id." + p.Family.Id };
            foreach (string t in p.Family.Tags) words.Add("tag." + t);
            if (p.Planned != null) words.Add("role." + p.Planned.Role);
            if (p.Primary) words.Add("primary");
            if (ReferenceEquals(p, capital)) words.Add("capital");
            float reach = (float)Math.Max(p.Stamp.MaxX - p.Stamp.MinX, p.Stamp.MaxZ - p.Stamp.MinZ) * 0.5f;
            if (book.Target(words, p.Center.X, p.Center.Z, reach, tech) is { } target) targets.Add(target);
        }
        if (city != null)
        {
            var words = new HashSet<string> { "plan.city" };
            if (ReferenceEquals(city, capital)) words.Add("capital");
            if (book.Target(words, city.CenterX, city.CenterZ, (float)city.Radius, tech) is { } target) targets.Add(target);
        }
        // The bridges, at their middles.
        foreach (InfraStructure s in net.Structures.Where(s => s.Kind == RoadSpan.Bridge && s.Last > s.First))
        {
            var words = new HashSet<string> { "infra.bridge", "route." + s.Route.Kind.ToString().ToLowerInvariant() };
            int mid = (s.First + s.Last) / 2;
            if (book.Target(words, Mathx.WrapX(s.Route.X[mid], w), Mathx.Wrap(s.Route.Z[mid], h), (float)s.Length * 0.5f, tech) is { } target) targets.Add(target);
        }
        // The junctions: where a road ends on another, once each, by the roads that meet there.
        var seen = new List<(double X, double Z)>();
        foreach (InfraRoute r in net.Routes.Where(r => r.Kind != RouteKind.Rail && r.Count > 1))
            foreach (var (end, i) in new[] { (r.From, 0), (r.To, r.Count - 1) })
            {
                if (end != null) continue;
                double x = Mathx.WrapX(r.X[i], w), z = Mathx.Wrap(r.Z[i], h);
                if (seen.Any(s => Dist(s.X, s.Z, x, z, w, h) < 80)) continue;
                InfraRoute on = net.Nearest(x, z, 30, out _, out _, new[] { r });
                if (on == null) continue;
                seen.Add((x, z));
                var words = new HashSet<string> { "infra.junction", "route." + r.Kind.ToString().ToLowerInvariant(), "route." + on.Kind.ToString().ToLowerInvariant() };
                if (book.Target(words, x, z, 20f, tech) is { } target) targets.Add(target);
            }
        return targets;
    }

    /// <summary>A bridge inside a blast's flattened ring loses its spans between piers, never its ends (as decay drops them).</summary>
    static void DropBridges(InfraNetwork net, List<Strike> strikes, double w, double h, ulong seed)
    {
        var rng = new PoiGenerator.Gen.Rng(PoiGenerator.Gen.Hash.Combine(seed, 0xB21D6EUL));
        const int SpanLen = 5;   // stretches between piers (20 m), as the network lays them
        foreach (InfraStructure s in net.Structures.Where(s => s.Kind == RoadSpan.Bridge))
        {
            float hit = 0f;
            for (int k = s.First; k <= s.Last && k < s.Route.Count; k++)
                foreach (Strike b in strikes)
                {
                    float d = (float)Dist(s.Route.X[k], s.Route.Z[k], b.X, b.Z, w, h);
                    if (d < b.Ruin) hit = Math.Max(hit, b.Strength(d));
                }
            if (hit <= 0f) continue;
            for (int k = s.First + SpanLen; k + SpanLen < s.Last; k += SpanLen)
                if (rng.Chance(0.35f + 0.65f * hit))
                    for (int m = k; m < k + SpanLen; m++) s.Fallen.Add(m);
        }
    }

    /// <summary>The planet's land as the strike planner sees it: the sea, dry ground, and where a crater will go.</summary>
    sealed class Land : IStrikeLand
    {
        readonly WorldMap _map;
        readonly PlanetTerrainSource _bare;
        readonly InfraNetwork _net;
        readonly IReadOnlyList<PoiSite> _pois;
        readonly IReadOnlyList<FlavourRuin> _ruins;

        public Land(WorldMap map, PlanetTerrainSource bare, InfraNetwork net, IReadOnlyList<PoiSite> pois, IReadOnlyList<FlavourRuin> ruins, List<(double X, double Z)> approaches)
        {
            (_map, _bare, _net, _pois, _ruins) = (map, bare, net, pois, ruins);
            Width = map.WidthMeters;
            Depth = map.HeightMeters;
            Approaches = approaches;
        }

        public double Width { get; }
        public double Depth { get; }
        public IReadOnlyList<(double X, double Z)> Approaches { get; }

        /// <summary>In a city's or a town's middle: dry, firm ground clear of the seams and the water, never on the main port, a blast site or the start town, whatever else stands on it.</summary>
        public bool Within(double x, double z, float radius, float depth)
        {
            x = Mathx.WrapX(x, Width);
            z = Mathx.Wrap(z, Depth);
            float reach = radius * 1.5f + Math.Clamp(radius * 0.6f, 3f, 36f);
            if (x < SeamMargin + reach || x > Width - SeamMargin - reach || z < SeamMargin + reach || z > Depth - SeamMargin - reach) return false;
            foreach (PoiSite p in _pois)
                if ((p.Primary || p.GroundZero != null) && Dist(p.Center.X, p.Center.Z, x, z, Width, Depth) < Math.Max(p.Stamp.ReachX, p.Stamp.ReachZ) + reach + 10) return false;
            if (_net.Town is StartTown town && Dist(town.CenterX, town.CenterZ, x, z, Width, Depth) < 450 + reach) return false;
            // Its bowl dry and its thrown rim off the sea (a river past its rim keeps its banks); not so steep the bowl turns into a terrace; its floor above the sea (on low land the bowl is shallower: Plan holds it there).
            var (pad, low, high, _) = Ground(x, z, radius, radius * 1.5f);
            for (double f = 0; f <= 1.5; f += 0.25)
                for (int k = 0, around = f == 0 ? 1 : (int)(f * 24); k < around; k++)
                {
                    double a = k * Math.Tau / around;
                    double sx = Mathx.WrapX(x + Math.Cos(a) * radius * f, Width), sz = Mathx.Wrap(z + Math.Sin(a) * radius * f, Depth);
                    GroundSample g = _bare.Sampler.Sample(sx, sz, 2);
                    if (Sea(sx, sz) || f <= 1.25 && (g.InRiver || g.Height < 2.5f)) return false;
                }
            if (Watered(x, z, radius * 1.25f)) return false;
            return high - low <= Math.Max(9f, radius * 0.45f) && MathF.Round(pad) - radius * ShallowestBowl > 3f;
        }

        /// <summary>A river or open water anywhere within a radius (a river's bed is narrower than any ring of samples): every map cell it covers, a river's banks included.</summary>
        bool Watered(double x, double z, float reach)
        {
            double step = _map.CellSize * 0.5;
            for (double dz = -reach; dz <= reach; dz += step)
                for (double dx = -reach; dx <= reach; dx += step)
                {
                    if (dx * dx + dz * dz > reach * reach) continue;
                    int k = _map.CellAt(Mathx.WrapX(x + dx, Width), Mathx.Wrap(z + dz, Depth));
                    if (_map.Has(k, CellFlags.River) || _map.Has(k, CellFlags.Water)) return true;
                }
            return false;
        }

        public bool Sea(double x, double z) => _map.Has(_map.CellAt(Mathx.WrapX(x, Width), Mathx.Wrap(z, Depth)), CellFlags.Water);

        public bool Dry(double x, double z)
        {
            GroundSample g = _bare.Sampler.Sample(Mathx.WrapX(x, Width), Mathx.Wrap(z, Depth), 2);
            return !g.InRiver && g.Height >= 2.5f && !Sea(x, z);
        }

        /// <summary>The ground a crater sits in: its middle's median height, and how far the land round it rises and falls.</summary>
        (float Pad, float Low, float High, bool Wet) Ground(double x, double z, float radius, float reach)
        {
            var inner = new List<float>();
            float low = float.MaxValue, high = float.MinValue;
            bool wet = false;
            // A shell hole is a few metres across: its middle, its rim and its edge tell enough.
            bool small = radius < 14f;
            foreach (double f in small ? new[] { 0.0, 1.0, reach / radius } : new[] { 0.0, 0.5, 1.0, 1.5, reach / radius })
            {
                int around = f == 0 ? 1 : small ? 6 : 10;
                for (int k = 0; k < around; k++)
                {
                    double a = k * Math.Tau / around;
                    double sx = Mathx.WrapX(x + Math.Cos(a) * radius * f, Width), sz = Mathx.Wrap(z + Math.Sin(a) * radius * f, Depth);
                    GroundSample g = _bare.Sampler.Sample(sx, sz, 2);
                    if (g.InRiver || g.Height < 2.5f || Sea(sx, sz)) wet = true;
                    low = MathF.Min(low, g.Height);
                    high = MathF.Max(high, g.Height);
                    if (f <= 1.0) inner.Add(g.Height);
                }
            }
            inner.Sort();
            return (inner[inner.Count / 2], low, high, wet);
        }

        public bool Crater(double x, double z, float radius, float depth)
        {
            x = Mathx.WrapX(x, Width);
            z = Mathx.Wrap(z, Depth);
            float reach = radius * 1.5f + Math.Clamp(radius * 0.6f, 3f, 36f);
            if (x < SeamMargin + reach || x > Width - SeamMargin - reach || z < SeamMargin + reach || z > Depth - SeamMargin - reach) return false;
            // Beside a place, never under it: clear of every pad and the ground it blends.
            foreach (PoiSite p in _pois)
                if (Dist(p.Center.X, p.Center.Z, x, z, Width, Depth) < Math.Max(p.Stamp.ReachX, p.Stamp.ReachZ) + reach + 10) return false;
            // Out of the city's streets and the start town's.
            if (_net.City is { } city && Dist(city.CenterX, city.CenterZ, x, z, Width, Depth) < city.Radius + reach + 40) return false;
            if (_net.Town is StartTown town && Dist(town.CenterX, town.CenterZ, x, z, Width, Depth) < 450 + reach) return false;
            foreach (FlavourRuin r in _ruins)
            {
                var (rx, rz) = r.Center;
                if (Dist(rx, rz, x, z, Width, Depth) < Math.Max(r.Stamp.MaxX - r.Stamp.MinX, r.Stamp.MaxZ - r.Stamp.MinZ) + reach) return false;
            }
            // Off every road's bed and banks.
            if (!_net.OffRoad(x, z, reach + 12)) return false;
            // Dry, firm ground: no river or sea in it, not so steep the bowl turns into a terrace, its floor above the water.
            var (pad, low, high, wet) = Ground(x, z, radius, reach);
            if (wet || high - low > Math.Max(9f, radius * 0.45f)) return false;
            // A big bowl's ring of samples can straddle a river: the map's own cells say.
            if (radius >= 14f && Watered(x, z, radius * 1.25f)) return false;
            return MathF.Round(pad) - depth > 3f;
        }

        /// <summary>The height a crater's bowl is sunk from: its middle's ground.</summary>
        public float Floor(Strike s) => MathF.Round(Ground(s.X, s.Z, s.Crater, s.BowlReach).Pad);

        /// <summary>A ground burst's crater in the terrain: its bowl, glassed and ash-grey by the dead ground round it, and its thrown rim. Nothing was built in it: what grows here grows back into it (a nuclear crater's dead ground keeps it bare).</summary>
        public TerrainStamp Bowl(Strike s)
        {
            float reach = s.BowlReach;
            var (pad, _, _, _) = Ground(s.X, s.Z, s.Crater, reach);
            int gx = (int)Math.Round(s.X), gz = (int)Math.Round(s.Z), rr = (int)Math.Ceiling(s.Crater);
            return new TerrainStamp($"strike crater ({(s.Target.Length > 0 ? s.Target : "open country")})", gx - rr, gz - rr, gx + rr, gz + rr, (int)MathF.Round(pad), 0f, s.BowlBlend, Width, Depth)
                { Shape = StampShape.Bowl, Radius = s.Crater, Depth = s.Depth, Rim = s.Rim, Wild = !s.Flatten };
        }
    }
}
