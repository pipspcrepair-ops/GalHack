using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Terrain;
using Planets.Gen;
using PoiGenerator.Gen;
using PoiSite = PlanetSurface.Poi.PoiSite;
using PoiGate = PlanetSurface.Poi.PoiGate;

namespace PlanetSurface.Infra;

/// <summary>What a route is: a highway, a road, a track, or a railway (a maglev on high-tech worlds).</summary>
public enum RouteKind : byte { Highway, Road, Track, Rail }

/// <summary>
/// One stretch of road, track or railway: a smoothed path of points 4 m apart (x, z continuous
/// across the seams, from its start), each with its deck height, and per stretch whether it lies on
/// the ground, crosses a bridge or runs in a tunnel. Its ends are a place's gate or a junction with
/// another route.
/// </summary>
public sealed class InfraRoute
{
    public int Id;
    public RouteKind Kind;
    /// <summary>The tech level that built it (the world's pre-collapse level).</summary>
    public TechLevel Tech;
    public double[] X = Array.Empty<double>(), Z = Array.Empty<double>();
    public float[] Y = Array.Empty<float>();
    /// <summary>Per stretch (point i to i + 1).</summary>
    public RoadSpan[] Span = Array.Empty<RoadSpan>();
    /// <summary>Half the deck's width: carriageway and shoulders, or the rail bed.</summary>
    public float HalfWidth;
    /// <summary>How its line was found: surveyed in straights and arcs, or the land's line eased (tests).</summary>
    public string Laid = "";
    public StampSurface Surface;
    /// <summary>0 kept up, 1 a century of weather.</summary>
    public float Wear;
    /// <summary>Kept in repair: it serves a place a faction holds or people live in.</summary>
    public bool Kept;
    /// <summary>The places at its ends (null where it ends at a junction), and which of their gates.</summary>
    public PoiSite From, To;
    public int FromGate = -1, ToGate = -1;
    public double Length;
    /// <summary>A maglev: the whole line rides an elevated guideway.</summary>
    public bool Maglev;

    public int Count => X.Length;
}

/// <summary>A flattened clearing a vessel can set down on, and the town it serves.</summary>
public sealed record LandingSpot(double X, double Z, float Y, float Radius, PoiSite Town);

/// <summary>A bridge (or viaduct) or a tunnel: a run of a route's stretches.</summary>
public sealed class InfraStructure
{
    public InfraRoute Route;
    public int First, Last;     // point indices; the run covers stretches First .. Last - 1
    public RoadSpan Kind;
    /// <summary>Bridges: built of timber (low tech), steel and concrete (mid) or composite (high).</summary>
    public string Build = "";
    /// <summary>Bridges: the stretches whose deck has fallen (the decay of a century); tunnels: flooded.</summary>
    public HashSet<int> Fallen = new();
    public bool Flooded;
    public double Length;
    /// <summary>Bridges: some of it crosses water (a river, a lake, the sea).</summary>
    public bool OverWater;
}

/// <summary>
/// The once-inhabited world's infrastructure, planned before anything is built: highways between
/// cities and spaceports, roads to towns, mines and bunkers, tracks out to remote outposts, and
/// railways where the old world ran them. A spanning tree joins the places by importance, with
/// about one link in five added back as loops. Every link is found by one path engine over the real
/// terrain with a cost table per kind of route (gentle grades, water dearest, existing road cheap so
/// routes merge into junctions), then smoothed, given a deck that keeps to the kind's grade, and
/// split into ground, bridges over rivers and ravines, and tunnels through ridges. Each route is laid
/// in the terrain as road stamps (after the places' own), so it shows in the near voxels and the far
/// rings alike. Worlds that were never inhabited get none. Deterministic from the planet seed.
/// </summary>
public sealed class InfraNetwork
{
    public readonly List<InfraRoute> Routes = new();
    public readonly List<InfraStructure> Structures = new();
    public WorldProfile Profile;
    public double PlanMs, SearchMs;
    /// <summary>Railway links dropped because the land would have made them mostly viaduct and tunnel (tests).</summary>
    public int RailDropped;
    /// <summary>Why each was dropped (tests).</summary>
    public readonly List<string> RailDrops = new();
    void Drop(string why)
    {
        RailDropped++;
        RailDrops.Add(why);
    }
    /// <summary>Links the spanning tree and loops asked for that no route could be found for (tests).</summary>
    public int Unrouted;
    /// <summary>Links between places on land the sea separates, which a world of this tech level never bridged (tests).</summary>
    public int SeaSeparated;
    /// <summary>Places the sea cuts off from every other: no route reaches them.</summary>
    public readonly List<PoiSite> Isolated = new();
    /// <summary>Power lines along the network (planned once the terrain is final: <see cref="PowerLines.Plan"/>).</summary>
    public PowerLines Power = new();
    /// <summary>Sites of abandoned vehicles along the network and in the fields (planned once the terrain is final: <see cref="Wrecks.Plan"/>).</summary>
    public List<WreckSite> Wrecks = new();
    /// <summary>Where a trader can set down: the clearings at survivor towns' edges, for live traders to plug into later.</summary>
    public readonly List<LandingSpot> LandingSpots = new();
    /// <summary>A start world's ruined town by the landing site, where the start begins (null elsewhere).</summary>
    public StartTown Town;
    /// <summary>Flavour ruins along the roads and in the countryside (planned on the bare land: <see cref="FlavourRuins.Plan"/>).</summary>
    public List<FlavourRuin> Ruins = new();
    /// <summary>What the world's plan says each region was for (null on a world laid out without one).</summary>
    public RegionPlan RegionPlan;
    /// <summary>Links the plan asked for, kept with why, for tests: (from, to, why).</summary>
    public readonly List<(PoiSite A, PoiSite B, string Why)> PlannedRoutes = new();

    /// <summary>The places the network joins (built by people: not wrecks, alien or Precursor sites).</summary>
    public readonly List<PoiSite> Places = new();

    public const float Spacing = 4f;

    double _w, _h;
    Router _router;
    PoiGenerator.Gen.Rng _rng;

    // ---- per kind ---------------------------------------------------------------------------

    /// <summary>The steepest a kind's deck climbs, metres per metre.</summary>
    public static float MaxGrade(RouteKind k) => k switch { RouteKind.Highway => 0.06f, RouteKind.Road => 0.09f, RouteKind.Track => 0.15f, _ => 0.025f };
    /// <summary>The tightest curve a railway takes, metres: a train doesn't turn on a coin, so a line sweeps.</summary>
    public const double RailRadius = 260, RailRadiusLeast = 170;
    /// <summary>How far a deck may stand over the ground before it becomes a bridge.</summary>
    static float BridgeFill(RouteKind k) => k switch { RouteKind.Highway => 8f, RouteKind.Road => 7f, RouteKind.Track => 5f, _ => 8f };
    /// <summary>How deep a deck may cut before it becomes a tunnel (tracks never tunnel: they climb and wind).</summary>
    static float TunnelCut(RouteKind k) => k switch { RouteKind.Highway => 14f, RouteKind.Road => 13f, RouteKind.Track => float.MaxValue, _ => 11f };
    /// <summary>How far along the deck heights are averaged before the grade is held: highways and railways sweep, tracks follow the land.</summary>
    static float SmoothRun(RouteKind k) => k switch { RouteKind.Highway => 60f, RouteKind.Road => 32f, RouteKind.Track => 14f, _ => 320f };

    /// <summary>
    /// The longest dry bridge (a viaduct over a valley) a kind of route built at a tech level runs to:
    /// tracks and frontier roads go down and up the land instead, steep where they must be.
    /// </summary>
    public static float MaxViaduct(RouteKind k, TechLevel t) => (k, t) switch
    {
        (RouteKind.Track, _) => 24f,
        (RouteKind.Rail, TechLevel.Low) => 200f,
        (RouteKind.Rail, _) => 600f,
        (_, TechLevel.Low) => 40f,
        (RouteKind.Road, TechLevel.Mid) => 200f,
        (RouteKind.Road, _) => 400f,
        (_, TechLevel.Mid) => 400f,
        _ => 800f,
    };

    /// <summary>The longest tunnel a kind of route built at a tech level runs to: past that it climbs over the ridge.</summary>
    public static float MaxTunnel(RouteKind k, TechLevel t) => (k, t) switch
    {
        (RouteKind.Track, _) => 0f,
        (_, TechLevel.Low) => k == RouteKind.Rail ? 400f : 120f,
        (_, TechLevel.Mid) => 800f,
        _ => 1600f,
    };

    public static float HalfWidthOf(RouteKind k) => k switch { RouteKind.Highway => 7f, RouteKind.Road => 4f, RouteKind.Track => 2.2f, _ => 2.6f };

    static StampSurface SurfaceOf(RouteKind k, TechLevel t) => (k, t) switch
    {
        (RouteKind.Rail, _) => StampSurface.Gravel,
        (RouteKind.Track, _) => t == TechLevel.Low ? StampSurface.Dirt : StampSurface.Gravel,
        (_, TechLevel.Low) => k == RouteKind.Highway ? StampSurface.Gravel : StampSurface.Dirt,
        (RouteKind.Highway, TechLevel.High) => StampSurface.Concrete,
        _ => StampSurface.Tarmac,
    };

    /// <summary>How much a place matters to the network: 3 cities and spaceports, 2 towns, bases, mines and works, 1 the rest.</summary>
    public static int Rank(PoiSite p) => p.Primary ? 4 : p.Tier switch
    {
        PoiTier.City or PoiTier.Spaceport => 3,
        PoiTier.Town or PoiTier.Base => 2,
        _ => p.Family.Tags.Contains("mine") || p.Family.Tags.Contains("industry") || p.Family.Tags.Contains("farm") ? 2 : 1,
    };

    /// <summary>Places people built and reached by road: not wrecks that fell from the sky, nor alien or Precursor sites.</summary>
    public static bool Served(PoiSite p) => !WorldProfile.Unbuilt(p.Family);

    // ---- planning ---------------------------------------------------------------------------

    public static InfraNetwork Plan(WorldMap map, PlanetTerrainSource bare, IReadOnlyList<PoiSite> pois, WorldProfile profile, ulong seed)
    {
        var watch = Stopwatch.StartNew();
        var net = new InfraNetwork { Profile = profile, _w = map.WidthMeters, _h = map.HeightMeters };
        if (profile == null || !profile.Inhabited)
        {
            net.PlanMs = watch.Elapsed.TotalMilliseconds;
            return net;
        }
        net.Places.AddRange(pois.Where(Served));
        net.RegionPlan = pois.FirstOrDefault(p => p.Plan != null)?.Plan;
        var router = new Router(map, bare, pois);
        var rng = new PoiGenerator.Gen.Rng(PoiGenerator.Gen.Hash.Combine(seed, 0x1AF7AUL));
        net._router = router;
        net._rng = rng;

        // The links: a spanning tree by importance, then about one in five added back as loops.
        var places = net.Places;
        var links = new List<(int A, int B, double D)>();
        for (int a = 0; a < places.Count; a++)
        for (int b = a + 1; b < places.Count; b++)
            links.Add((a, b, net.Dist(places[a].Center, places[b].Center)));
        var tree = new List<(int A, int B, double D)>();
        var parent = Enumerable.Range(0, places.Count).ToArray();
        int Find(int i) => parent[i] == i ? i : parent[i] = Find(parent[i]);
        // The plan's own links come first and join what they join; anything still alone is reached by the plain tree below.
        var wanted = new List<(int A, int B, RouteKind Kind, string Why)>();
        var spine = new HashSet<(int A, int B)>();
        if (net.RegionPlan != null)
            foreach (PlannedLink l in net.RegionPlan.Links.Where(l => l.Kind is "highway" or "road" or "track"))
            {
                if (net.RegionPlan.Places[l.A].Seated is not PoiSite sa || net.RegionPlan.Places[l.B].Seated is not PoiSite sb) continue;
                int a = places.IndexOf(sa), b = places.IndexOf(sb);
                if (a < 0 || b < 0) continue;
                RouteKind kind = l.Kind switch { "highway" => RouteKind.Highway, "road" => RouteKind.Road, _ => RouteKind.Track };
                if (profile.Before == TechLevel.Low && kind == RouteKind.Highway) kind = RouteKind.Road;
                wanted.Add((a, b, kind, l.Why));
                parent[Find(a)] = Find(b);
                net.PlannedRoutes.Add((sa, sb, l.Why));
                if (net.RegionPlan.Places[l.A].Role is "hub" or "port" && net.RegionPlan.Places[l.B].Role is "hub" or "port" || kind == RouteKind.Highway)
                    spine.Add((a, b));
            }
        foreach (var l in links.OrderBy(l => l.D / (1 + 0.25 * Math.Min(Rank(places[l.A]), Rank(places[l.B])))))
        {
            int ra = Find(l.A), rb = Find(l.B);
            if (ra == rb) continue;
            parent[ra] = rb;
            tree.Add(l);
        }
        double longest = tree.Count > 0 ? tree.Max(l => l.D) : 0;
        int loops = net.RegionPlan != null ? 0 : places.Count >= 4 ? Math.Max(2, (int)Math.Round(places.Count * 0.22)) : 0;
        var extra = links.Where(l => !tree.Contains(l) && l.D < longest * 1.2).OrderBy(l => l.D).Take(loops).ToList();

        // The most important links first, so the lesser ones merge into them at junctions.
        var all = tree.Concat(extra)
            .Select(l => (l.A, l.B, l.D, Kind: net.RegionPlan != null ? RouteKind.Track : KindFor(places[l.A], places[l.B], profile.Before)))
            .Concat(wanted.Select(l => (l.A, l.B, D: net.Dist(places[l.A].Center, places[l.B].Center), l.Kind)))
            .OrderBy(l => l.Kind).ThenBy(l => l.D).ToList();
        // A town has its streets before any road comes to it.
        if (net.RegionPlan != null)
            foreach (PoiSite hub in places.Where(p => p.Planned?.Role == "hub").ToList())
                if (hub.Planned.Region >= 0 && net.RegionPlan.Regions[hub.Planned.Region].Kind is RegionKind.Town or RegionKind.Hamlet)
                    net.TownGrid(router, hub, net.RegionPlan.Regions[hub.Planned.Region].Kind == RegionKind.Town);
        // On a planned world only the spine goes place to place; everything else comes off what's laid (below).
        var branches = new List<(int A, int B, double D, RouteKind Kind)>();
        foreach (var l in all)
        {
            if (net.RegionPlan != null && !spine.Contains((l.A, l.B)))
            {
                branches.Add(l);
                continue;
            }
            if (!net.Link(router, places[l.A], places[l.B], l.Kind, rng))
            {
                // Over land, or across a sea its builders never bridged?
                if (router.SeaBetween(places[l.A].Center, places[l.B].Center)) net.SeaSeparated++;
                else net.Unrouted++;
            }
        }
        if (net.RegionPlan != null) net.Branches(router, branches, rng);
        foreach (PoiSite p in places)
            if (!net.Routes.Any(r => r.From == p || r.To == p)) net.Isolated.Add(p);

        // Railways where the old world ran them: a line through the places that mattered most.
        // On a planned world every line is one the plan asked for, and ends at a depot built for it.
        if (net.RegionPlan != null)
        {
            foreach (PlannedLink l in net.RegionPlan.Links.Where(l => l.Kind == "rail"))
                if (net.RegionPlan.Places[l.A].Seated is PoiSite sa && net.RegionPlan.Places[l.B].Seated is PoiSite sb && net.LinkRail(router, sa, sb, rng))
                    net.PlannedRoutes.Add((sa, sb, l.Why));
            net.PlanMs = watch.Elapsed.TotalMilliseconds;
            return net;
        }
        var railNodes = places.Where(p => Rank(p) >= 2).OrderByDescending(Rank).ThenBy(p => net.Dist(p.Center, places[0].Center)).Take(5).ToList();
        if (railNodes.Count >= 2 && profile.Population >= 3)
        {
            var railLinks = new List<(int A, int B, double D)>();
            for (int a = 0; a < railNodes.Count; a++)
            for (int b = a + 1; b < railNodes.Count; b++)
                railLinks.Add((a, b, net.Dist(railNodes[a].Center, railNodes[b].Center)));
            parent = Enumerable.Range(0, railNodes.Count).ToArray();
            int laid = 0;
            foreach (var l in railLinks.OrderBy(l => l.D))
            {
                int ra = Find(l.A), rb = Find(l.B);
                if (ra == rb || laid >= 3) continue;
                parent[ra] = rb;
                if (net.LinkRail(router, railNodes[l.A], railNodes[l.B], rng)) laid++;
            }
        }
        net.PlanMs = watch.Elapsed.TotalMilliseconds;
        return net;
    }

    static RouteKind KindFor(PoiSite a, PoiSite b, TechLevel tech)
    {
        int r = Math.Min(Rank(a), Rank(b));
        static bool Urban(PoiSite p) => p.Tier is PoiTier.Town or PoiTier.City or PoiTier.Spaceport;
        // Highways between cities and spaceports, and between towns on a world that built them.
        // Whatever matters on the world has a highway to its main starport.
        // On a world that built them, the main roads between anything that mattered were highways too.
        RouteKind k = r >= 3 || r >= 2 && tech >= TechLevel.Mid || Urban(a) && Urban(b) && tech >= TechLevel.Mid || a.Primary || b.Primary ? RouteKind.Highway : r >= 2 ? RouteKind.Road : RouteKind.Track;
        // Frontier worlds had no highways: their best roads were gravel.
        if (tech == TechLevel.Low && k == RouteKind.Highway) k = RouteKind.Road;
        return k;
    }

    double Dist((double X, double Z) a, (double X, double Z) b)
    {
        double dx = Mathx.WrapDelta(a.X - b.X, _w), dz = Mathx.WrapDelta(a.Z - b.Z, _h);
        return Math.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>The gate of a place a route toward a point leaves by, the point outside it, and the way out.</summary>
    (int Index, double X, double Z, float Y, double OutX, double OutZ) GateToward(PoiSite p, (double X, double Z) toward)
    {
        int best = -1;
        double bestScore = double.MaxValue;
        (double cx, double cz) = p.Center;
        for (int i = 0; i < p.Gates.Count; i++)
        {
            PoiGate g = p.Gates[i];
            if (g.Kind == "breach") continue;
            double score = Dist((g.Cell.X + 0.5, g.Cell.Z + 0.5), toward) - (g.Kind == "entrance" ? 25 : 0);
            if (score < bestScore) { bestScore = score; best = i; }
        }
        if (best < 0) best = 0;
        PoiGate gate = p.Gates[best];
        double gx = gate.Cell.X + 0.5, gz = gate.Cell.Z + 0.5;
        double ox = gate.OutX, oz = gate.OutZ;
        // A fence gate gives only its axis: out is the way away from the place's middle.
        double awayX = Mathx.WrapDelta(gx - cx, _w), awayZ = Mathx.WrapDelta(gz - cz, _h);
        if (ox * awayX + oz * awayZ < 0) { ox = -ox; oz = -oz; }
        if (ox == 0 && oz == 0)
        {
            double l = Math.Max(1e-6, Math.Sqrt(awayX * awayX + awayZ * awayZ));
            ox = awayX / l;
            oz = awayZ / l;
        }
        return (best, gx, gz, gate.Cell.Y, ox, oz);
    }

    /// <summary>The way a road leaves a place: a town's by the end of its main street, carrying straight on; anywhere else by a gate.</summary>
    (int Index, double X, double Z, float Y, double OutX, double OutZ) WayOut(PoiSite p, (double X, double Z) toward)
    {
        if (!_mainStreets.TryGetValue(p, out InfraRoute main)) return GateToward(p, toward);
        int n = main.Count - 1;
        bool far = Dist((main.X[n], main.Z[n]), toward) < Dist((main.X[0], main.Z[0]), toward);
        int end = far ? n : 0, back = far ? n - 1 : 1;
        double ox = main.X[end] - main.X[back], oz = main.Z[end] - main.Z[back], l = Math.Max(1e-6, Math.Sqrt(ox * ox + oz * oz));
        return (-1, main.X[end], main.Z[end], main.Y[end], ox / l, oz / l);
    }

    readonly Dictionary<PoiSite, InfraRoute> _mainStreets = new();

    /// <summary>Streets laid in the grids of the plan's towns (tests).</summary>
    public int TownStreets;

    /// <summary>
    /// A town's streets, surveyed on the map's axes before any road reaches it: a main street across the
    /// front of its heart, with the roads from outside running into its ends, side streets off it every
    /// block, and a back street closing them. Narrower than the main street, as a side street is. Where the
    /// ground won't take a street, it wasn't built.
    /// </summary>
    void TownGrid(Router router, PoiSite hub, bool town)
    {
        var g = GateToward(hub, hub.Center);
        bool outX = Math.Abs(g.OutX) >= Math.Abs(g.OutZ);
        double vx = outX ? Math.Sign(g.OutX) : 0, vz = outX ? 0 : Math.Sign(g.OutZ), ux = -vz, uz = vx;
        double ox = Math.Floor(g.X + vx * 34) + 0.5, oz = Math.Floor(g.Z + vz * 34) + 0.5;
        const double Block = 110;
        int blocks = town ? 3 : 1;
        double arm = town ? 220 : 110, half = blocks * Block;
        (double X, double Z) At(double a, double b) => (ox + ux * a + vx * b, oz + uz * a + vz * b);
        bool Free((double X, double Z) a, (double X, double Z) b)
        {
            double len = Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Z - a.Z, 2)), lo = double.MaxValue, hi = double.MinValue;
            for (double t = 0; t <= len; t += 8)
            {
                double x = a.X + (b.X - a.X) * t / Math.Max(len, 1), z = a.Z + (b.Z - a.Z) * t / Math.Max(len, 1);
                if (router.Blocked(x, z) || router.Wet(x, z)) return false;
                float y = router.Ground(Mathx.WrapX(x, _w), Mathx.Wrap(z, _h), 2, out _, out _);
                lo = Math.Min(lo, y);
                hi = Math.Max(hi, y);
            }
            return hi - lo <= len * 0.09 + 3;
        }
        if (!Free(At(-half, 0), At(half, 0))) return;
        InfraRoute main = Street(new List<(double X, double Z)> { At(-half, 0), At(half, 0) }, RouteKind.Road);
        if (main == null) return;
        _mainStreets[hub] = main;
        TownStreets++;
        const float SideWidth = 2.6f;
        if (Free(At(-half, arm), At(half, arm)) && Street(new List<(double X, double Z)> { At(-half, arm), At(half, arm) }, RouteKind.Road) is { } back)
        {
            back.HalfWidth = SideWidth;
            TownStreets++;
        }
        for (int c = -blocks; c <= blocks; c++)
            if (Free(At(c * Block, 0), At(c * Block, arm)) && Street(new List<(double X, double Z)> { At(c * Block, 0), At(c * Block, arm) }, RouteKind.Road) is { } side)
            {
                side.HalfWidth = SideWidth;
                TownStreets++;
            }
        // Its way in, straight off the main street to its gate.
        if (Street(new List<(double X, double Z)> { At(0, 0), (g.X, g.Z) }, RouteKind.Road, (g.Index, g.Y)) is { } drive)
        {
            drive.HalfWidth = SideWidth;
            drive.To = hub;
            drive.ToGate = g.Index;
        }
        else _mainStreets.Remove(hub);
    }

    /// <summary>Routes one link: from a gate of one place to a gate of the other, merging into roads already laid.</summary>
    bool Link(Router router, PoiSite a, PoiSite b, RouteKind kind, PoiGenerator.Gen.Rng rng)
    {
        var ga = WayOut(a, b.Center);
        var gb = WayOut(b, a.Center);
        // Leave each gate straight out for a few metres before the path engine takes over.
        (double X, double Z) outA = (ga.X + ga.OutX * 14, ga.Z + ga.OutZ * 14), outB = (gb.X + gb.OutX * 14, gb.Z + gb.OutZ * 14);
        var search = Stopwatch.StartNew();
        List<int> cells = router.Find(outA, outB, kind, Profile.Before);
        SearchMs += search.Elapsed.TotalMilliseconds;
        if (cells == null) return false;

        // Split the path where it runs along routes already laid: each new run joins them at a junction.
        var runs = new List<(int From, int To, int JoinFrom, int JoinTo)>();   // cell index range, and the route ids it joins at each end (-1 a gate)
        int i = 0;
        while (i < cells.Count)
        {
            if (router.RouteAt(cells[i], kind) >= 0 && i > 0 && i < cells.Count - 1) { i++; continue; }
            int start = i;
            while (i < cells.Count && (router.RouteAt(cells[i], kind) < 0 || i == 0 || i == cells.Count - 1)) i++;
            int end = i - 1;
            int joinFrom = start == 0 ? -1 : router.RouteAt(cells[start - 1], kind);
            int joinTo = end == cells.Count - 1 ? -1 : router.RouteAt(cells[end + 1], kind);
            // A run that touches an existing route reaches into it by one cell, where the junction is.
            runs.Add((start == 0 ? 0 : start - 1, end == cells.Count - 1 ? end : end + 1, joinFrom, joinTo));
        }
        bool any = false;
        foreach (var run in runs)
        {
            if (run.To - run.From < 1 && run.JoinFrom >= 0 && run.JoinTo >= 0) continue;
            var pts = new List<(double X, double Z)>();
            var pins = new List<(int Index, float Y)>();
            if (run.From == 0)
            {
                pts.Add((ga.X, ga.Z));
                pts.Add((ga.X + ga.OutX * 6, ga.Z + ga.OutZ * 6));
            }
            for (int k = run.From; k <= run.To; k++) pts.Add(router.CellCentre(cells[k]));
            if (run.To == cells.Count - 1)
            {
                pts.Add((gb.X + gb.OutX * 6, gb.Z + gb.OutZ * 6));
                pts.Add((gb.X, gb.Z));
            }
            Unwrap(pts);
            List<(double X, double Z)> path = Lay(router, pts, kind, run.From == 0 ? 2 : 0, run.To == cells.Count - 1 ? 2 : 0);
            if (path.Count < 2) continue;
            // Junction ends land exactly on the route they join, at its deck height.
            if (run.JoinFrom >= 0 && Snap(Routes[run.JoinFrom], path[0], out var p0, out float y0, out var d0)) { path[0] = p0; if (kind != RouteKind.Rail) Square(path, Routes[run.JoinFrom], p0, d0, true); ClearStart(path); }
            if (run.JoinTo >= 0 && Snap(Routes[run.JoinTo], path[^1], out var p1, out float y1, out var d1)) { path[^1] = p1; if (kind != RouteKind.Rail) Square(path, Routes[run.JoinTo], p1, d1, false); ClearEnd(path); }
            // Pinned once the ends are final: the junctions at the decks they meet.
            if (run.JoinFrom >= 0 && Snap(Routes[run.JoinFrom], path[0], out _, out float yj0)) pins.Add((0, yj0));
            if (run.JoinTo >= 0 && Snap(Routes[run.JoinTo], path[^1], out _, out float yj1)) pins.Add((path.Count - 1, yj1));
            if (run.From == 0) pins.Add((0, ga.Y));
            if (run.To == cells.Count - 1) pins.Add((path.Count - 1, gb.Y));
            // A railway crosses roads on the level: its deck meets theirs where they cross.
            if (kind == RouteKind.Rail)
                for (int k = 2; k < path.Count - 2; k += 1)
                {
                    int road = router.RouteAtPoint(path[k].X, path[k].Z, RouteKind.Road);
                    if (road < 0 || !Snap(Routes[road], path[k], out _, out float yr) || pins.Any(p => Math.Abs(p.Index - k) < 6)) continue;
                    // A road up on a bank or down in a cutting at this spot passes over or under: no level crossing.
                    if (Math.Abs(yr - router.Ground(Mathx.WrapX(path[k].X, _w), Mathx.Wrap(path[k].Z, _h), 2, out _, out _)) < 1.5f) pins.Add((k, yr));
                }
            var route = Build(router, path, pins, _narrowed ? RouteKind.Track : kind, a, b, run.From == 0 ? ga.Index : -1, run.To == cells.Count - 1 ? gb.Index : -1, rng);
            if (route == null) continue;
            // A railway goes only where the land lets it lie mostly on the ground: if most of it would be
            // viaduct and tunnel, the old world never built it (a maglev rides its guideway anyway).
            if (kind == RouteKind.Rail && !route.Maglev && route.Span.Count(sp => sp != RoadSpan.Ground) > route.Span.Length * 0.3)
            {
                Structures.RemoveAll(st => st.Route == route);
                Routes.RemoveAt(Routes.Count - 1);
                Drop("mostly in the air or the ground");
                continue;
            }
            route.From = run.From == 0 ? a : null;
            route.To = run.To == cells.Count - 1 ? b : null;
            router.Mark(route);
            any = true;
        }
        return any;
    }

    /// <summary>
    /// The network below the spine. Each place the plan wants joined comes off the nearest road of its
    /// own rank or better, and the nearest of them first, so the next one finds that road already there:
    /// roads off the highway, tracks off the roads, at junctions, instead of a star of lanes back to a
    /// town's gate. A place no road reaches goes the whole way to whatever it served.
    /// </summary>
    void Branches(Router router, List<(int A, int B, double D, RouteKind Kind)> wanted, PoiGenerator.Gen.Rng rng)
    {
        bool Joined(PoiSite p) => Routes.Any(r => r.From == p || r.To == p);
        var waiting = wanted.OrderBy(l => l.Kind).ToList();
        while (waiting.Count > 0)
        {
            RouteKind rank = waiting[0].Kind;
            int pick = 0;
            double best = double.MaxValue;
            for (int i = 0; i < waiting.Count && waiting[i].Kind == rank; i++)
            {
                double d = Math.Min(NearestRoad(Places[waiting[i].A].Center, rank, out _, out _), waiting[i].D);
                if (d < best) { best = d; pick = i; }
            }
            var l = waiting[pick];
            waiting.RemoveAt(pick);
            // The end that needs a way out: the plan's links run from a place to what it served.
            PoiSite p = Places[l.A], served = Places[l.B];
            if (Joined(p)) p = served;
            if (Joined(p)) continue;
            if (Branch(router, p, l.Kind, rng) || Link(router, Places[l.A], served, l.Kind, rng)) continue;
            if (router.SeaBetween(Places[l.A].Center, served.Center)) SeaSeparated++;
            else Unrouted++;
        }
        // Whatever the rest went past still gets its way out.
        foreach (PoiSite p in Places)
            if (!Joined(p)) Branch(router, p, RouteKind.Track, rng);
    }

    /// <summary>How far a point is from the nearest road of this rank or better, and where that is.</summary>
    double NearestRoad((double X, double Z) from, RouteKind rank, out InfraRoute route, out int index)
    {
        double best = double.MaxValue;
        route = null;
        index = -1;
        foreach (InfraRoute r in Routes)
        {
            if (r.Kind == RouteKind.Rail || r.Kind > rank) continue;
            for (int i = 0; i < r.Count; i += 3)
            {
                double dx = Mathx.WrapDelta(r.X[i] - from.X, _w), dz = Mathx.WrapDelta(r.Z[i] - from.Z, _h), d = dx * dx + dz * dz;
                if (d < best) { best = d; route = r; index = i; }
            }
        }
        return route == null ? double.MaxValue : Math.Sqrt(best);
    }

    /// <summary>A way out of a place's gate to the nearest road it may join, ending in a junction on it (or on the first such road it meets). False if there's none, or no way there.</summary>
    bool Branch(Router router, PoiSite p, RouteKind kind, PoiGenerator.Gen.Rng rng)
    {
        NearestRoad(p.Center, kind, out InfraRoute near, out int at);
        if (near == null) return false;
        (double X, double Z) target = (near.X[at], near.Z[at]);
        var g = GateToward(p, target);
        var search = Stopwatch.StartNew();
        List<int> cells = router.Find((g.X + g.OutX * 14, g.Z + g.OutZ * 14), target, kind, Profile.Before);
        SearchMs += search.Elapsed.TotalMilliseconds;
        if (cells == null || cells.Count < 2) return false;
        int end = cells.Count - 1;
        for (int i = 1; i < cells.Count; i++)
        {
            int id = router.RouteAt(cells[i], kind);
            if (id < 0 || Routes[id].Kind > kind) continue;
            near = Routes[id];
            end = i;
            break;
        }
        var pts = new List<(double X, double Z)> { (g.X, g.Z), (g.X + g.OutX * 6, g.Z + g.OutZ * 6) };
        for (int k = 0; k <= end; k++) pts.Add(router.CellCentre(cells[k]));
        Unwrap(pts);
        List<(double X, double Z)> path = Lay(router, pts, kind, 2, 0);
        if (path.Count < 2) return false;
        var pins = new List<(int Index, float Y)>();
        if (Snap(near, path[^1], out var p1, out float y1, out var d1))
        {
            path[^1] = p1;
            if (kind != RouteKind.Rail) Square(path, near, p1, d1, false);
            ClearEnd(path);
            pins.Add((path.Count - 1, y1));
        }
        pins.Add((0, g.Y));
        InfraRoute route = Build(router, path, pins, _narrowed ? RouteKind.Track : kind, p, null, g.Index, -1, rng);
        if (route == null) return false;
        route.From = p;
        route.To = null;
        router.Mark(route);
        Junctions++;
        return true;
    }

    /// <summary>Roads that end on another road (tests).</summary>
    public int Junctions;

    // ---- railways ------------------------------------------------------------------------------------

    /// <summary>
    /// Lays a railway between two places, a station at each end. It doesn't wind up a hill as a road does:
    /// the land's cheapest way is drawn out into long straights, joined by sweeping curves, on a deck that
    /// keeps its gentle grade whatever the land does, through cuttings and over embankments, and where
    /// that won't do on bridges and through tunnels. A line the land won't take was never built.
    /// </summary>
    bool LinkRail(Router router, PoiSite a, PoiSite b, PoiGenerator.Gen.Rng rng)
    {
        var ga = GateToward(a, b.Center);
        var gb = GateToward(b, a.Center);
        // The stations stand a little way out from each place's gate; the platform lies along the line, whichever way that goes.
        const double Platform = 34;
        (double X, double Z) oa = (ga.X + ga.OutX * Platform, ga.Z + ga.OutZ * Platform), ob = (gb.X + gb.OutX * Platform, gb.Z + gb.OutZ * Platform);
        var search = Stopwatch.StartNew();
        List<int> cells = router.Find(oa, ob, RouteKind.Rail, Profile.Before);
        SearchMs += search.Elapsed.TotalMilliseconds;
        if (cells == null || cells.Count < 4) { Drop("no way"); return false; }
        var land = new List<(double X, double Z)> { oa };
        foreach (int c in cells) land.Add(router.CellCentre(c));
        land.Add(ob);
        Unwrap(land);
        List<(double X, double Z)> corners = Straights(router, land, 70, 260);
        // A corner too sharp for the room it has goes, if the straight past it stays clear of every place.
        List<(double X, double Z)> sweep = Sweep(corners, RailRadius, RailRadiusLeast, Math.PI * 0.55, out int sharp);
        for (int tries = 0; sweep == null && tries < 24; tries++)
        {
            int cut = -1;
            foreach (int i in new[] { sharp, sharp - 1, sharp + 1 })
                if (i > 0 && i + 1 < corners.Count && Clear(router, corners[i - 1], corners[i + 1])) { cut = i; break; }
            if (cut < 0) break;
            corners.RemoveAt(cut);
            sweep = Sweep(corners, RailRadius, RailRadiusLeast, Math.PI * 0.55, out sharp);
        }
        if (sweep == null) { Drop("a corner too sharp"); return false; }
        List<(double X, double Z)> path = Resample(sweep, Spacing);
        if (CrossesItself(path)) { Drop("crosses itself"); return false; }
        foreach (var (x, z) in path.Skip(14).Take(Math.Max(0, path.Count - 28)))
            if (router.Blocked(x, z)) { Drop("through a place"); return false; }
        var pins = new List<(int Index, float Y)> { (0, ga.Y), (path.Count - 1, gb.Y) };
        for (int k = 8; k < path.Count - 8; k++)
        {
            int road = router.RouteAtPoint(path[k].X, path[k].Z, RouteKind.Road);
            if (road < 0 || !Snap(Routes[road], path[k], out _, out float yr) || pins.Any(p => Math.Abs(p.Index - k) < 20)) continue;
            if (Math.Abs(yr - router.Ground(Mathx.WrapX(path[k].X, _w), Mathx.Wrap(path[k].Z, _h), 2, out _, out _)) < 1.5f) pins.Add((k, yr));
        }
        InfraRoute route = Build(router, path, pins, RouteKind.Rail, a, b, ga.Index, gb.Index, rng);
        if (route == null) { Drop("no deck"); return false; }
        // Too much in the air or under the ground, or a run longer than its builders could manage, or too steep for a train: never built.
        string bad2 = !route.Maglev && route.Span.Count(sp => sp != RoadSpan.Ground) > route.Span.Length * 0.4 ? "mostly in the air or the ground"
            : !route.Maglev && Structures.Any(s => s.Route == route && s.Length > (s.Kind == RoadSpan.Bridge ? MaxViaduct(RouteKind.Rail, Profile.Before) : MaxTunnel(RouteKind.Rail, Profile.Before))) ? "a run too long to build" : null;
        for (int i = 0; i + 1 < route.Count && bad2 == null; i++) if (Math.Abs(route.Y[i + 1] - route.Y[i]) > Spacing * 0.04f) bad2 = "too steep";
        // Level crossings that pull the deck about give way first: the line crosses those roads on its own bank or in its own cutting.
        if (bad2 == "too steep" && pins.Count > 2)
        {
            Structures.RemoveAll(st => st.Route == route);
            Routes.RemoveAt(Routes.Count - 1);
            route = Build(router, path, pins.Where(p => p.Index == 0 || p.Index == path.Count - 1).ToList(), RouteKind.Rail, a, b, ga.Index, gb.Index, rng);
            bad2 = route.Span.Count(sp => sp != RoadSpan.Ground) > route.Span.Length * 0.4 && !route.Maglev ? "mostly in the air or the ground" : null;
            for (int i = 0; i + 1 < route.Count && bad2 == null; i++) if (Math.Abs(route.Y[i + 1] - route.Y[i]) > Spacing * 0.04f) bad2 = "too steep";
        }
        if (bad2 != null)
        {
            Structures.RemoveAll(st => st.Route == route);
            Routes.RemoveAt(Routes.Count - 1);
            Drop(bad2);
            return false;
        }
        route.From = a;
        route.To = b;
        router.Mark(route);
        return true;
    }

    /// <summary>The land's way between two points as long straights: as few corners as stay within a fair distance of it and clear of every place.</summary>
    List<(double X, double Z)> Straights(Router router, List<(double X, double Z)> pts, double Tolerance, double Shortest, int keepStart = 0, int keepEnd = 0, double margin = 40)
    {
        var keep = new SortedSet<int> { 0, pts.Count - 1 };
        for (int i = 1; i <= keepStart && i < pts.Count; i++) keep.Add(i);
        for (int i = 1; i <= keepEnd && pts.Count - 1 - i >= 0; i++) keep.Add(pts.Count - 1 - i);
        void Fit(int lo, int hi)
        {
            if (hi - lo < 2) return;
            var (ax, az) = pts[lo];
            var (bx, bz) = pts[hi];
            double ex = bx - ax, ez = bz - az, len = Math.Max(1e-6, Math.Sqrt(ex * ex + ez * ez));
            int worst = -1;
            double most = 0;
            for (int i = lo + 1; i < hi; i++)
            {
                double d = Math.Abs((pts[i].X - ax) * ez - (pts[i].Z - az) * ex) / len;
                if (d > most) { most = d; worst = i; }
            }
            if (most <= Tolerance && Clear(router, pts[lo], pts[hi], margin)) return;
            if (worst < 0) worst = (lo + hi) / 2;
            keep.Add(worst);
            Fit(lo, worst);
            Fit(worst, hi);
        }
        var fixedCorners = keep.ToList();
        for (int i = 0; i + 1 < fixedCorners.Count; i++) Fit(fixedCorners[i], fixedCorners[i + 1]);
        var corners = keep.ToList();
        // No short stretches between corners, where the land allows: each is a pair of curves with no straight between.
        for (bool merged = true; merged;)
        {
            merged = false;
            for (int i = 1; i + 1 < corners.Count; i++)
            {
                if (fixedCorners.Contains(corners[i])) continue;
                var (x0, z0) = pts[corners[i - 1]];
                var (x1, z1) = pts[corners[i]];
                if (Math.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0)) >= Shortest && i + 2 < corners.Count) continue;
                if (!Clear(router, pts[corners[i - 1]], pts[corners[i + 1]], margin)) continue;
                corners.RemoveAt(i);
                merged = true;
                break;
            }
        }
        return corners.Select(i => pts[i]).ToList();
    }

    /// <summary>Whether a straight line keeps off every place.</summary>
    static bool Clear(Router router, (double X, double Z) a, (double X, double Z) b, double margin = 40)
    {
        double len = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Z - a.Z) * (b.Z - a.Z));
        // Stations stand by their places: a line's ends may be close to one.
        for (double t = margin; t <= len - margin; t += 8)
        {
            double x = a.X + (b.X - a.X) * t / len, z = a.Z + (b.Z - a.Z) * t / len;
            if (router.Blocked(x, z) || router.Sea(x, z)) return false;
        }
        return true;
    }

    /// <summary>Joins straights with arcs of a radius, down to the least where corners crowd; null if a corner (<paramref name="sharp"/>) is too sharp for the room it has.</summary>
    static List<(double X, double Z)> Sweep(List<(double X, double Z)> c, double radius, double least, double sharpest, out int sharp)
    {
        int n = c.Count;
        sharp = -1;
        var tangent = new double[n];
        var turn = new double[n];
        for (int i = 1; i + 1 < n; i++)
        {
            double ax = c[i].X - c[i - 1].X, az = c[i].Z - c[i - 1].Z, bx = c[i + 1].X - c[i].X, bz = c[i + 1].Z - c[i].Z;
            turn[i] = Math.Abs(Math.Atan2(ax * bz - az * bx, ax * bx + az * bz));
            tangent[i] = radius * Math.Tan(turn[i] / 2);
        }
        // Where two curves want more than the straight between them, each gives way, down to the least radius.
        double Len(int i) => Math.Sqrt(Math.Pow(c[i + 1].X - c[i].X, 2) + Math.Pow(c[i + 1].Z - c[i].Z, 2));
        for (int i = 0; i + 1 < n; i++)
        {
            double need = tangent[i] + tangent[i + 1], have = Len(i) * (i == 0 || i + 2 == n ? 0.95 : 0.98);
            if (need <= have) continue;
            tangent[i] *= have / need;
            tangent[i + 1] *= have / need;
        }
        var path = new List<(double X, double Z)> { c[0] };
        for (int i = 1; i + 1 < n; i++)
        {
            if (turn[i] < 1e-4) continue;
            if (turn[i] > sharpest || tangent[i] / Math.Tan(turn[i] / 2) < least)
            {
                sharp = i;
                return null;
            }
            double l0 = Len(i - 1), l1 = Len(i);
            double ux = (c[i].X - c[i - 1].X) / l0, uz = (c[i].Z - c[i - 1].Z) / l0, vx = (c[i + 1].X - c[i].X) / l1, vz = (c[i + 1].Z - c[i].Z) / l1;
            double t = tangent[i], r = t / Math.Tan(turn[i] / 2);
            double sign = Math.Sign(ux * vz - uz * vx);
            var (sx, sz) = (c[i].X - ux * t, c[i].Z - uz * t);
            double cx = sx - uz * r * sign, cz = sz + ux * r * sign;
            int steps = Math.Max(2, (int)(r * turn[i] / 6));
            for (int k = 0; k <= steps; k++)
            {
                double ang = turn[i] * k / steps * sign, rx = sx - cx, rz = sz - cz;
                path.Add((cx + rx * Math.Cos(ang) - rz * Math.Sin(ang), cz + rx * Math.Sin(ang) + rz * Math.Cos(ang)));
            }
        }
        path.Add(c[^1]);
        return path;
    }

    /// <summary>
    /// The line a run takes, from the land's way. Roads are surveyed: long straights, corners only where
    /// the land or a place wants one, each a clean arc. A highway sweeps, a road turns tighter. A track
    /// wanders as tracks do. The fixed points at each end (the way out of a gate) stay.
    /// </summary>
    List<(double X, double Z)> Lay(Router router, List<(double X, double Z)> pts, RouteKind kind, int keepStart, int keepEnd)
    {
        _narrowed = false;
        List<(double X, double Z)> smooth = Resample(Smooth(pts, keepStart, keepEnd), Spacing);
        if (kind is not (RouteKind.Highway or RouteKind.Road) || pts.Count < 4) return smooth;
        bool highway = kind == RouteKind.Highway;
        // Paved roads never hairpin: a crowded corner gives way only to its class's least radius, and a run
        // that can't keep it takes the next fit, or the land's own line smoothed below.
        double radius = highway ? 150 : 60, least = highway ? LeastRadius(RouteKind.Highway) : LeastRadius(RouteKind.Road);
        // The straightest the land allows: closer to its way only where the long straights ran into something.
        foreach (double fit in new[] { 1.0, 0.5, 0.25 })
        {
            List<(double X, double Z)> corners = Straights(router, pts, (highway ? 90 : 60) * fit, (highway ? 320 : 200) * fit, Math.Max(0, keepStart - 1), Math.Max(0, keepEnd - 1), 14);
            List<(double X, double Z)> sweep = Sweep(corners, radius, least, Math.PI * 0.97, out int sharp);
            for (int tries = 0; sweep == null && tries < 16; tries++)
            {
                int cut = -1;
                foreach (int i in new[] { sharp, sharp - 1, sharp + 1 })
                    if (i > keepStart - 1 && i + keepEnd < corners.Count && i > 0 && i + 1 < corners.Count && Clear(router, corners[i - 1], corners[i + 1])) { cut = i; break; }
                if (cut < 0) break;
                corners.RemoveAt(cut);
                sweep = Sweep(corners, radius, least, Math.PI * 0.97, out sharp);
            }
            if (sweep == null) continue;
            List<(double X, double Z)> path = Resample(sweep, Spacing);
            if (path.Count < 2 || CrossesItself(path) || path.Skip(12).Take(Math.Max(0, path.Count - 24)).Any(q => router.Blocked(q.X, q.Z))) continue;
            Surveyed++;
            _laid = "surveyed";
            return path;
        }
        Wandered++;
        _laid = "eased";
        List<(double X, double Z)> eased = Ease(router, smooth, least, keepStart, keepEnd, out bool tight);
        // Squeezed between places too closely to keep a paved road's curves, it was only ever a lane.
        _narrowed = tight;
        return eased;
    }

    /// <summary>The tightest curve a paved road of a class takes; tracks may switch back as tight as they like.</summary>
    public static double LeastRadius(RouteKind k) => k switch { RouteKind.Highway => 90, RouteKind.Road => 35, _ => 0 };

    /// <summary>
    /// A paved road that had to follow the land's line, eased until no bend is tighter than its least
    /// radius: each point drawn toward the middle of its neighbours where the curve through them is too
    /// tight, never onto blocked ground, the fixed ends left alone.
    /// </summary>
    static List<(double X, double Z)> Ease(Router router, List<(double X, double Z)> p, double least, int keepStart, int keepEnd, out bool tight)
    {
        var pts = p.ToList();
        tight = false;
        const int Gap = 5;
        int window = Math.Max(3, (int)(least / Spacing));
        for (int pass = 0; pass < 400; pass++)
        {
            int lo = Math.Max(4, keepStart + 2), hi = pts.Count - Math.Max(4, keepEnd + 2);
            if (hi - lo <= 2 * Gap) break;
            // Where the curve through a point and its neighbours either side is too tight, and round it.
            var mark = new bool[pts.Count];
            bool any = false;
            for (int i = lo + Gap; i < hi - Gap; i++)
            {
                if (Radius(pts[i - Gap], pts[i], pts[i + Gap]) >= least * 1.02) continue;
                any = true;
                for (int k = Math.Max(lo, i - 2 * window); k < Math.Min(hi, i + 2 * window); k++) mark[k] = true;
            }
            if (!any) break;
            if (pass == 399) tight = true;
            var next = pts.ToList();
            // Late passes may edge into a place's keep-off margin, a few metres at most, rather than leave a hairpin.
            double pressed = pass > 300 ? 8 : pass > 160 ? 3 : 0;
            for (int i = lo; i < hi; i++)
            {
                if (!mark[i]) continue;
                int a = Math.Max(lo - 1, i - window), b = Math.Min(hi, i + window);
                double x = 0, z = 0, sw = 0, sigma = window * 0.5;
                for (int k = a; k <= b; k++)
                {
                    double g = Math.Exp(-0.5 * (k - i) * (k - i) / (sigma * sigma));
                    x += pts[k].X * g;
                    z += pts[k].Z * g;
                    sw += g;
                }
                x /= sw;
                z /= sw;
                // Onto a place's ground only where the road already runs on it (its way out of a gate); otherwise
                // as far toward the eased line as stays off it.
                if (!router.Blocked(x, z) || router.Blocked(pts[i].X, pts[i].Z) || Math.Abs(x - pts[i].X) + Math.Abs(z - pts[i].Z) < pressed
                    || pass > 360 && !router.OnPad(x, z)) { next[i] = (x, z); continue; }
                for (double f = 0.5; f > 0.05; f *= 0.5)
                {
                    double px = pts[i].X + (x - pts[i].X) * f, pz = pts[i].Z + (z - pts[i].Z) * f;
                    if (!router.Blocked(px, pz)) { next[i] = (px, pz); break; }
                }
            }
            pts = Resample(next, Spacing);
        }
        return pts;
    }

    /// <summary>The radius of the circle through three points.</summary>
    public static double Radius((double X, double Z) a, (double X, double Z) b, (double X, double Z) c)
    {
        double area = Math.Abs((b.X - a.X) * (c.Z - a.Z) - (b.Z - a.Z) * (c.X - a.X)) * 0.5;
        double ab = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Z - a.Z) * (b.Z - a.Z)), bc = Math.Sqrt((c.X - b.X) * (c.X - b.X) + (c.Z - b.Z) * (c.Z - b.Z)), ca = Math.Sqrt((a.X - c.X) * (a.X - c.X) + (a.Z - c.Z) * (a.Z - c.Z));
        return area < 1e-6 ? 1e9 : ab * bc * ca / (4 * area);
    }

    /// <summary>Roads laid out in straights, and those that had to follow the land's way as it is (tests).</summary>
    public int Surveyed, Wandered;
    /// <summary>The last run laid couldn't keep its class's curves: it is built as a track.</summary>
    bool _narrowed;
    string _laid = "";

    /// <summary>Whether a path runs over itself.</summary>
    static bool CrossesItself(List<(double X, double Z)> p)
    {
        for (int i = 0; i + 1 < p.Count; i += 4)
            for (int j = i + 12; j + 1 < p.Count; j += 4)
            {
                var (a, b, c, d) = (p[i], p[Math.Min(i + 4, p.Count - 1)], p[j], p[Math.Min(j + 4, p.Count - 1)]);
                double d1 = (b.X - a.X) * (c.Z - a.Z) - (b.Z - a.Z) * (c.X - a.X), d2 = (b.X - a.X) * (d.Z - a.Z) - (b.Z - a.Z) * (d.X - a.X);
                double d3 = (d.X - c.X) * (a.Z - c.Z) - (d.Z - c.Z) * (a.X - c.X), d4 = (d.X - c.X) * (b.Z - c.Z) - (d.Z - c.Z) * (b.X - c.X);
                if (d1 * d2 < 0 && d3 * d4 < 0) return true;
            }
        return false;
    }

    // ---- streets and spurs for other planners (the start town) ----------------------------------------

    /// <summary>
    /// A street laid along given points (continuous, the short way round the seams): smoothed and
    /// resampled, its deck held to the kind's grade over the ground, joined at its ends to any route
    /// already laid there. Null on a world with no network.
    /// </summary>
    public InfraRoute Street(List<(double X, double Z)> points, RouteKind kind, (int Gate, float Y)? gate = null)
    {
        if (_router == null || points.Count < 2) return null;
        var pts = new List<(double X, double Z)>(points);
        Unwrap(pts);
        List<(double X, double Z)> path = Resample(pts, Spacing);
        var pins = new List<(int Index, float Y)>();
        int a = _router.RouteAtPoint(path[0].X, path[0].Z, kind), b = _router.RouteAtPoint(path[^1].X, path[^1].Z, kind);
        if (a >= 0 && Snap(Routes[a], path[0], out var p0, out float y0)) { path[0] = p0; ClearStart(path); pins.Add((0, y0)); }
        if (b >= 0 && gate == null && Snap(Routes[b], path[^1], out var p1, out float y1)) { path[^1] = p1; ClearEnd(path); pins.Add((path.Count - 1, y1)); }
        if (gate is { } g) pins.Add((path.Count - 1, g.Y));
        InfraRoute route = Build(_router, path, pins, kind, null, null, -1, -1, _rng);
        if (route != null) _router.Mark(route);
        return route;
    }

    /// <summary>Later routes go round this pad.</summary>
    public void KeepOff(TerrainStamp pad) => _router?.Block(pad, 12);

    /// <summary>A spur from a point to the nearest route already laid (within a reach), found by the path engine. False if none.</summary>
    public bool Spur((double X, double Z) from, RouteKind kind, double reach)
    {
        if (_router == null) return false;
        InfraRoute near = Nearest(from.X, from.Z, reach, out int at, out _);
        if (near == null) return false;
        List<int> cells = _router.Find(from, (near.X[at], near.Z[at]), kind, Profile.Before);
        if (cells == null || cells.Count < 2) return false;
        var pts = new List<(double X, double Z)> { from };
        foreach (int c in cells.Skip(1)) pts.Add(_router.CellCentre(c));
        pts.Add((near.X[at], near.Z[at]));
        Unwrap(pts);
        List<(double X, double Z)> path = Resample(Smooth(pts, 1, 0), Spacing);
        var pins = new List<(int Index, float Y)>();
        if (Snap(near, path[^1], out var p1, out float y1)) { path[^1] = p1; ClearEnd(path); pins.Add((path.Count - 1, y1)); }
        InfraRoute route = Build(_router, path, pins, kind, null, null, -1, -1, _rng);
        if (route == null) return false;
        _router.Mark(route);
        return true;
    }

    /// <summary>
    /// A snapped end can land almost on the next point. Takes that point out: a stretch of centimetres
    /// between a pinned junction and the deck beside it would stand as a cliff.
    /// </summary>
    static void ClearStart(List<(double X, double Z)> path)
    {
        while (path.Count > 2 && Near(path[0], path[1])) path.RemoveAt(1);
    }

    static void ClearEnd(List<(double X, double Z)> path)
    {
        while (path.Count > 2 && Near(path[^1], path[^2])) path.RemoveAt(path.Count - 2);
    }

    static bool Near((double X, double Z) a, (double X, double Z) b) => Math.Abs(a.X - b.X) + Math.Abs(a.Z - b.Z) < Spacing * 0.6;

    /// <summary>The nearest point of a route to a point (the short way round the seams), moved to the copy nearest it, and the deck height there.</summary>
    bool Snap(InfraRoute r, (double X, double Z) near, out (double X, double Z) at, out float y) => Snap(r, near, out at, out y, out _);

    bool Snap(InfraRoute r, (double X, double Z) near, out (double X, double Z) at, out float y, out (double X, double Z) dir)
    {
        double best = double.MaxValue;
        at = near;
        y = 0;
        dir = (1, 0);
        for (int i = 0; i + 1 < r.Count; i++)
        {
            double ax = near.X + Mathx.WrapDelta(r.X[i] - near.X, _w), az = near.Z + Mathx.WrapDelta(r.Z[i] - near.Z, _h);
            double bx = ax + (r.X[i + 1] - r.X[i]), bz = az + (r.Z[i + 1] - r.Z[i]);
            double ex = bx - ax, ez = bz - az, len2 = ex * ex + ez * ez;
            double u = len2 > 1e-9 ? Math.Clamp(((near.X - ax) * ex + (near.Z - az) * ez) / len2, 0, 1) : 0;
            double qx = ax + ex * u, qz = az + ez * u, d = (qx - near.X) * (qx - near.X) + (qz - near.Z) * (qz - near.Z);
            if (d >= best) continue;
            best = d;
            at = (qx, qz);
            y = r.Y[i] + (r.Y[i + 1] - r.Y[i]) * (float)u;
            double el = Math.Sqrt(len2);
            if (el > 1e-9) dir = (ex / el, ez / el);
        }
        return best < 40 * 40;
    }

    /// <summary>
    /// A road joining another meets it square: its last stretch runs straight out from the other's edge,
    /// along the other's normal, for a length that grows with the other's width, so a junction is a clean
    /// T and not a long blunt wedge where one road slides into the other at a glancing angle.
    /// </summary>
    static void Square(List<(double X, double Z)> path, InfraRoute joined, (double X, double Z) at, (double X, double Z) dir, bool atStart)
    {
        if (path.Count < 4) return;
        double reach = Math.Clamp(joined.HalfWidth + 12, 14, 26);
        var inner = atStart ? path[Math.Min(path.Count - 1, 6)] : path[Math.Max(0, path.Count - 7)];
        double nx = -dir.Z, nz = dir.X;
        if ((inner.X - at.X) * nx + (inner.Z - at.Z) * nz < 0) { nx = -nx; nz = -nz; }
        var foot = (at.X + nx * reach, at.Z + nz * reach);
        var keep = new List<(double X, double Z)>();
        if (atStart)
        {
            keep.Add(at);
            keep.Add(foot);
            foreach (var q in path.Skip(1)) if ((q.X - at.X) * (q.X - at.X) + (q.Z - at.Z) * (q.Z - at.Z) > reach * reach * 2.2) keep.Add(q);
        }
        else
        {
            foreach (var q in path.Take(path.Count - 1)) if ((q.X - at.X) * (q.X - at.X) + (q.Z - at.Z) * (q.Z - at.Z) > reach * reach * 2.2) keep.Add(q);
            keep.Add(foot);
            keep.Add(at);
        }
        if (keep.Count < 3) return;
        var even = Resample(keep, Spacing);
        path.Clear();
        path.AddRange(even);
    }

    /// <summary>Makes a path continuous across the seams: each point moved by whole planets to the copy nearest the one before.</summary>
    void Unwrap(List<(double X, double Z)> pts)
    {
        for (int i = 1; i < pts.Count; i++)
            pts[i] = (pts[i - 1].X + Mathx.WrapDelta(pts[i].X - pts[i - 1].X, _w), pts[i - 1].Z + Mathx.WrapDelta(pts[i].Z - pts[i - 1].Z, _h));
    }

    /// <summary>Corner cutting (Chaikin), keeping the ends and the first and last few points (the straight run out of a gate).</summary>
    static List<(double X, double Z)> Smooth(List<(double X, double Z)> pts, int keepStart, int keepEnd)
    {
        var p = pts;
        for (int pass = 0; pass < 3; pass++)
        {
            if (p.Count < 3) break;
            var q = new List<(double X, double Z)>(p.Count * 2);
            int ks = pass == 0 ? keepStart : keepStart * 2, ke = pass == 0 ? keepEnd : keepEnd * 2;
            for (int i = 0; i < Math.Min(ks, p.Count); i++) q.Add(p[i]);
            if (q.Count == 0) q.Add(p[0]);
            for (int i = Math.Max(0, q.Count - 1); i + 1 < p.Count - Math.Max(0, ke - 1); i++)
            {
                var (ax, az) = p[i];
                var (bx, bz) = p[i + 1];
                q.Add((ax * 0.75 + bx * 0.25, az * 0.75 + bz * 0.25));
                q.Add((ax * 0.25 + bx * 0.75, az * 0.25 + bz * 0.75));
            }
            for (int i = Math.Max(p.Count - Math.Max(ke, 1), 0); i < p.Count; i++) q.Add(p[i]);
            p = q;
        }
        return p;
    }

    static List<(double X, double Z)> Resample(List<(double X, double Z)> pts, double step)
    {
        var o = new List<(double X, double Z)> { pts[0] };
        double carry = 0;
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            var (ax, az) = pts[i];
            var (bx, bz) = pts[i + 1];
            double len = Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
            if (len < 1e-6) continue;
            double t = step - carry;
            while (t <= len)
            {
                o.Add((ax + (bx - ax) * t / len, az + (bz - az) * t / len));
                t += step;
            }
            carry = len - (t - step);
        }
        var last = pts[^1];
        var (lx, lz) = o[^1];
        if (Math.Sqrt((last.X - lx) * (last.X - lx) + (last.Z - lz) * (last.Z - lz)) > step * 0.35) o.Add(last);
        else o[^1] = last;
        return o;
    }

    /// <summary>Gives a path its deck (smoothed ground held to the kind's grade, meeting the pins), its spans and its look.</summary>
    InfraRoute Build(Router router, List<(double X, double Z)> path, List<(int Index, float Y)> pins, RouteKind kind, PoiSite a, PoiSite b, int gateA, int gateB, PoiGenerator.Gen.Rng rng)
    {
        int n = path.Count;
        var ground = new float[n];
        var water = new bool[n];
        for (int i = 0; i < n; i++)
        {
            double px = Mathx.WrapX(path[i].X, _w), pz = Mathx.Wrap(path[i].Z, _h);
            ground[i] = router.Ground(px, pz, 2, out water[i], out float surface);
            // Over water (a river, a lake, the sea) the deck clears it.
            if (water[i]) ground[i] = Math.Max(ground[i], surface + (kind == RouteKind.Track ? 1.8f : 3f));
        }
        // Smoothed ground: the deck's first guess.
        int half = Math.Max(1, (int)(SmoothRun(kind) / Spacing / 2));
        var deck = new float[n];
        for (int i = 0; i < n; i++)
        {
            float sum = 0;
            int c = 0;
            for (int k = Math.Max(0, i - half); k <= Math.Min(n - 1, i + half); k++) { sum += ground[k]; c++; }
            deck[i] = sum / c;
        }
        // Meet the pins (gates and junctions) and hold the grade: the deck is never steeper than the kind
        // allows between neighbours, so round each pin it ramps to the pin's height and no further.
        pins = pins.GroupBy(p => p.Index).Select(g => g.First()).OrderBy(p => p.Index).ToList();
        float step = MaxGrade(kind) * Spacing;
        var fixedAt = new bool[n];
        foreach (var p in pins) { fixedAt[p.Index] = true; deck[p.Index] = p.Y; }
        // Over water the deck never drops below its clearance; the grade passes ramp up to it.
        var floor = new float[n];
        for (int i = 0; i < n; i++) floor[i] = water[i] ? ground[i] : float.MinValue;
        HoldGrade(deck, fixedAt, floor, step, 6);
        // A last light smoothing, so the deck has no kinks (never below a water clearance).
        var smooth = (float[])deck.Clone();
        for (int i = 1; i + 1 < n; i++) if (!fixedAt[i]) smooth[i] = Math.Max(floor[i], (deck[i - 1] + 2 * deck[i] + deck[i + 1]) * 0.25f);
        deck = smooth;

        // Spans: a bridge where the deck stands well over the ground or crosses water, a tunnel where it cuts deep into a ridge.
        bool maglev = kind == RouteKind.Rail && Profile.Before == TechLevel.High;
        var spans = new RoadSpan[n - 1];
        for (int i = 0; i + 1 < n; i++)
        {
            float g = (ground[i] + ground[i + 1]) * 0.5f, d = (deck[i] + deck[i + 1]) * 0.5f;
            if (maglev || water[i] || water[i + 1] || d - g > BridgeFill(kind)) spans[i] = RoadSpan.Bridge;
            else if (g - d > TunnelCut(kind)) spans[i] = RoadSpan.Tunnel;
        }
        // No tunnel shorter than three stretches (that is a cutting), no dry bridge shorter than three (that is an
        // embankment); bridges and tunnels reach one stretch on to firm ground at each end.
        Clean(spans, RoadSpan.Tunnel, 3);
        Clean(spans, RoadSpan.Bridge, 3, water);
        // A bridge reaches on until its deck meets the bank (an abutment, never a step); a tunnel until
        // its mouth is about a bore high in the slope (a portal face).
        Extend(spans, RoadSpan.Bridge, i => deck[i] - ground[i] > 1.2f && !water[i] || water[i]);
        Extend(spans, RoadSpan.Tunnel, i => ground[i] - deck[i] > 6.5f);
        // Past what its builders could span or bore, a route follows the land instead: down into the
        // valley and up the far side, over the ridge, steep where it must be (its deck eases onto the
        // ground over a few stretches at each end of the run).
        if (!maglev && kind != RouteKind.Rail) // a train can't follow the land: a line that long in the air is dropped
        {
            FollowLand(spans, deck, ground, water, fixedAt, RoadSpan.Bridge, MaxViaduct(kind, Profile.Before));
            FollowLand(spans, deck, ground, water, fixedAt, RoadSpan.Tunnel, MaxTunnel(kind, Profile.Before));
        }
        // A mouth stands square in the hill, not half out of its side: a tunnel starts where both sides of its
        // bore have ground over them, and short of that the line stays in the open, in a cutting.
        TrimMouths(spans, path, deck, router, HalfWidthOf(kind));
        Clean(spans, RoadSpan.Tunnel, 3);
        // Wherever the land was followed, the deck may be steep but always walkable: never more than 0.45 a stretch.
        if (kind != RouteKind.Rail) HoldGrade(deck, fixedAt, floor, 0.45f * Spacing, 3);
        // Never a bridge or tunnel at a gate: the first and last stretches lie on the ground.
        if (gateA >= 0 && spans.Length > 1) spans[0] = RoadSpan.Ground;
        if (gateB >= 0 && spans.Length > 1) spans[^1] = RoadSpan.Ground;
        if (maglev)
        {
            // A maglev rides its guideway the whole way, down to the ground only at the stations at its ends.
            for (int i = 0; i < spans.Length; i++) spans[i] = RoadSpan.Bridge;
            for (int i = 0; i < Math.Min(6, spans.Length); i++) spans[i] = RoadSpan.Ground;
            for (int i = Math.Max(0, spans.Length - 6); i < spans.Length; i++) spans[i] = RoadSpan.Ground;
        }

        bool kept = (a != null && (a.FactionHeld || a.Family.Tags.Contains("settlement") && a.Instance.State == PoiState.Active))
                    || (b != null && (b.FactionHeld || b.Family.Tags.Contains("settlement") && b.Instance.State == PoiState.Active));
        var route = new InfraRoute
        {
            Id = Routes.Count, Kind = kind, Tech = Profile.Before, HalfWidth = HalfWidthOf(kind), Surface = SurfaceOf(kind, Profile.Before),
            Kept = kept, Wear = kept ? 0.18f : 0.75f + rng.Float() * 0.2f, Maglev = maglev,
            X = path.Select(p => p.X).ToArray(), Z = path.Select(p => p.Z).ToArray(), Y = deck, Span = spans,
            FromGate = gateA, ToGate = gateB, Laid = _laid,
        };
        _laid = "";
        for (int i = 0; i + 1 < n; i++) route.Length += Math.Sqrt((route.X[i + 1] - route.X[i]) * (route.X[i + 1] - route.X[i]) + (route.Z[i + 1] - route.Z[i]) * (route.Z[i + 1] - route.Z[i]));
        Routes.Add(route);

        // Its bridges and tunnels, with what a century did to them.
        for (int i = 0; i < spans.Length;)
        {
            if (spans[i] == RoadSpan.Ground) { i++; continue; }
            int j = i;
            while (j < spans.Length && spans[j] == spans[i]) j++;
            var s = new InfraStructure
            {
                Route = route, First = i, Last = j, Kind = spans[i], Length = (j - i) * Spacing, OverWater = Enumerable.Range(i, j - i + 1).Any(k => water[k]),
                Build = route.Tech switch { TechLevel.Low => "timber", TechLevel.Mid => "steel", _ => "composite" },
            };
            if (s.Kind == RoadSpan.Bridge && !route.Kept && j - i >= 4)
            {
                // A long-neglected bridge has lost a span or two: never at its ends, always a whole span between piers.
                int spanLen = 5;   // stretches between piers (20 m)
                for (int k = i + spanLen; k + spanLen < j; k += spanLen)
                    if (rng.Chance(0.12f + 0.25f * route.Wear * (route.Tech == TechLevel.Low ? 1.5f : 1f)))
                        for (int m = k; m < k + spanLen; m++) s.Fallen.Add(m);
            }
            if (s.Kind == RoadSpan.Tunnel && !route.Kept)
            {
                // Water pools in a neglected tunnel whose middle lies lower than its mouths.
                float mid = deck[(i + j) / 2], mouths = Math.Min(deck[i], deck[j]);
                s.Flooded = mid < mouths - 0.4f || rng.Chance(0.15f);
            }
            Structures.Add(s);
            i = j;
        }
        return route;
    }

    /// <summary>Forward and backward passes holding each stretch to a climb of at most <paramref name="step"/>, pins fixed, never below a floor.</summary>
    static void HoldGrade(float[] deck, bool[] pinned, float[] floor, float step, int passes)
    {
        int n = deck.Length;
        for (int pass = 0; pass < passes; pass++)
        {
            for (int i = 1; i < n; i++) if (!pinned[i]) deck[i] = Math.Max(floor[i], Math.Clamp(deck[i], deck[i - 1] - step, deck[i - 1] + step));
            for (int i = n - 2; i >= 0; i--) if (!pinned[i]) deck[i] = Math.Max(floor[i], Math.Clamp(deck[i], deck[i + 1] - step, deck[i + 1] + step));
        }
    }

    /// <summary>Lays dry runs of a span kind longer than a limit on the ground: the deck follows the land there, easing in over five points.</summary>
    /// <summary>Pulls each tunnel's ends in to where the ground over both sides of the bore stands clear of it.</summary>
    void TrimMouths(RoadSpan[] spans, List<(double X, double Z)> path, float[] deck, Router router, float halfWidth)
    {
        float need = 8.5f;
        bool Covered(int i)
        {
            int a = Math.Max(0, i - 1), b = Math.Min(path.Count - 1, i + 1);
            double dx = path[b].X - path[a].X, dz = path[b].Z - path[a].Z, len = Math.Sqrt(dx * dx + dz * dz);
            if (len < 1e-6) return true;
            double nx = -dz / len, nz = dx / len, off = halfWidth + 5;
            foreach (int s in new[] { -1, 1 })
                if (router.Ground(Mathx.WrapX(path[i].X + nx * off * s, _w), Mathx.Wrap(path[i].Z + nz * off * s, _h), 4, out _, out _) - deck[i] < need) return false;
            return true;
        }
        for (int i = 0; i < spans.Length; i++)
        {
            if (spans[i] != RoadSpan.Tunnel) continue;
            int j = i;
            while (j + 1 < spans.Length && spans[j + 1] == RoadSpan.Tunnel) j++;
            int lo = i, hi = j;
            while (lo <= hi && !Covered(lo)) spans[lo++] = RoadSpan.Ground;
            while (hi >= lo && !Covered(hi + 1)) spans[hi--] = RoadSpan.Ground;
            i = j;
        }
        // A few stretches of daylight between two bores would be a slot through the hill: one tunnel, not two.
        const int Slot = 10;
        int last = -1;
        for (int i = 0; i < spans.Length; i++)
        {
            if (spans[i] != RoadSpan.Tunnel) continue;
            if (last >= 0 && i - last - 1 <= Slot && i - last > 1)
                for (int k = last + 1; k < i; k++)
                    if (spans[k] == RoadSpan.Ground) spans[k] = RoadSpan.Tunnel;
            last = i;
        }
    }

    static void FollowLand(RoadSpan[] spans, float[] deck, float[] ground, bool[] water, bool[] pinned, RoadSpan kind, float limit)
    {
        for (int i = 0; i < spans.Length;)
        {
            if (spans[i] != kind) { i++; continue; }
            int j = i;
            bool wet = false;
            while (j < spans.Length && spans[j] == kind) { wet |= water[j] || water[j + 1]; j++; }
            if ((j - i) * Spacing > limit && !wet)
            {
                const int Ease = 5;
                for (int k = Math.Max(0, i - Ease); k <= Math.Min(deck.Length - 1, j + Ease); k++)
                {
                    if (pinned[k]) continue;
                    int inside = Math.Min(k - (i - Ease), (j + Ease) - k);
                    float w = Math.Clamp(inside / (float)Ease, 0f, 1f);
                    w = w * w * (3 - 2 * w);
                    deck[k] += (ground[k] - deck[k]) * w;
                }
                for (int k = i; k < j; k++) spans[k] = RoadSpan.Ground;
            }
            i = j;
        }
    }

    /// <summary>Turns runs of a span kind shorter than a minimum back to ground (runs over water stay: a short bridge over a stream).</summary>
    static void Clean(RoadSpan[] spans, RoadSpan kind, int min, bool[] water = null)
    {
        for (int i = 0; i < spans.Length;)
        {
            if (spans[i] != kind) { i++; continue; }
            int j = i;
            bool wet = false;
            while (j < spans.Length && spans[j] == kind) { wet |= water != null && (water[j] || water[j + 1]); j++; }
            if (j - i < min && !wet) for (int k = i; k < j; k++) spans[k] = RoadSpan.Ground;
            i = j;
        }
    }

    /// <summary>
    /// Grows each run of a span kind outward over ground stretches while the point it reaches still
    /// needs it (at least one stretch, at most eight): bridges to where the deck meets the bank,
    /// tunnels to where the slope is about a bore high.
    /// </summary>
    static void Extend(RoadSpan[] spans, RoadSpan kind, Func<int, bool> needs)
    {
        var copy = (RoadSpan[])spans.Clone();
        for (int i = 0; i < copy.Length; i++)
        {
            if (copy[i] != kind) continue;
            if (i > 0 && copy[i - 1] == RoadSpan.Ground)
                for (int k = i - 1, n = 0; k >= 0 && spans[k] == RoadSpan.Ground && n < 8; k--, n++)
                {
                    spans[k] = kind;
                    if (!needs(k)) break;
                }
            if (i + 1 < copy.Length && copy[i + 1] == RoadSpan.Ground)
                for (int k = i + 1, n = 0; k < spans.Length && spans[k] == RoadSpan.Ground && n < 8; k++, n++)
                {
                    spans[k] = kind;
                    if (!needs(k + 1)) break;
                }
        }
    }

    // ---- into the terrain -------------------------------------------------------------------

    /// <summary>
    /// The network as terrain stamps, laid after the places' own: each route in stretches of about
    /// 100 m (each a road stamp), so a height query composes only the stretches near it. Maglevs touch
    /// the ground only at their stations; everything else is cut and filled to its deck.
    /// </summary>
    public List<TerrainStamp> Stamps()
    {
        var list = new List<TerrainStamp>();
        const int Chunk = 24;
        foreach (InfraRoute r in Routes)
        {
            if (r.Count < 2) continue;
            float[] mouth = MouthDistances(r);
            for (int s = 0; s + 1 < r.Count; s += Chunk)
            {
                int e = Math.Min(r.Count - 1, s + Chunk);
                var spans = new List<RoadSpan>();
                bool ground = false;
                for (int i = s; i < e; i++) { spans.Add(r.Span[i]); ground |= r.Span[i] != RoadSpan.Bridge; }
                if (!ground) continue;
                list.Add(TerrainStamp.Road($"{r.Kind} {r.Id}", new ArraySegment<double>(r.X, s, e - s + 1), new ArraySegment<double>(r.Z, s, e - s + 1),
                    new ArraySegment<float>(r.Y, s, e - s + 1), spans, r.HalfWidth, r.Surface, r.Wear, _w, _h,
                    boreHalfWidth: r.HalfWidth + 1.2f, boreHeight: r.Kind == RouteKind.Rail ? 7f : 6.5f,
                    buckle: r.Kept || r.Kind == RouteKind.Rail || r.Surface is StampSurface.Dirt or StampSurface.Gravel ? 0f : 0.22f * r.Wear,
                    mouth: new ArraySegment<float>(mouth, s, e - s + 1)));
            }
        }
        return list;
    }

    /// <summary>How far along a route each of its points lies from the nearest tunnel's mouth.</summary>
    static float[] MouthDistances(InfraRoute r)
    {
        var d = new float[r.Count];
        Array.Fill(d, 1e6f);
        for (int i = 0; i + 1 < r.Count; i++)
            if (r.Span[i] == RoadSpan.Tunnel) d[i] = d[i + 1] = 0f;
        for (int i = 1; i < r.Count; i++) d[i] = Math.Min(d[i], d[i - 1] + Spacing);
        for (int i = r.Count - 2; i >= 0; i--) d[i] = Math.Min(d[i], d[i + 1] + Spacing);
        return d;
    }

    /// <summary>The route nearest a world point within a distance, its point index and the distance (x, z wrap).</summary>
    /// <summary>
    /// Whether a spot is clear of every road, track and railway bed: its half width plus a margin from
    /// the line, so nothing stands on a road, its shoulder or a junction. Wires may cross over; this is
    /// for what stands on the ground.
    /// </summary>
    public bool OffRoad(double x, double z, double margin)
    {
        const double Cell = 16;
        int points = Routes.Sum(r => r.Count);
        if (_roadCells == null || _roadPoints != points)
        {
            _roadCells = new Dictionary<(int, int), List<(InfraRoute R, int I)>>();
            _roadPoints = points;
            foreach (InfraRoute r in Routes)
                for (int i = 0; i < r.Count; i++)
                {
                    var key = ((int)Math.Floor(Mathx.WrapX(r.X[i], _w) / Cell), (int)Math.Floor(Mathx.Wrap(r.Z[i], _h) / Cell));
                    if (!_roadCells.TryGetValue(key, out var list)) _roadCells[key] = list = new();
                    list.Add((r, i));
                }
            _roadReach = Routes.Count == 0 ? 0 : Routes.Max(r => r.HalfWidth);
        }
        double cx = Mathx.WrapX(x, _w), cz = Mathx.Wrap(z, _h);
        int span = (int)Math.Ceiling((_roadReach + margin + Spacing) / Cell);
        int gx = (int)Math.Floor(cx / Cell), gz = (int)Math.Floor(cz / Cell), nx = (int)Math.Ceiling(_w / Cell), nz = (int)Math.Ceiling(_h / Cell);
        for (int dz = -span; dz <= span; dz++)
            for (int dx = -span; dx <= span; dx++)
            {
                var key = (((gx + dx) % nx + nx) % nx, ((gz + dz) % nz + nz) % nz);
                if (!_roadCells.TryGetValue(key, out var list)) continue;
                foreach (var (r, i) in list)
                {
                    // The bed between this point and the next: the distance to the segment, not just to its points.
                    int j = Math.Min(i + 1, r.Count - 1);
                    double ax = Mathx.WrapDelta(r.X[i] - cx, _w), az = Mathx.WrapDelta(r.Z[i] - cz, _h);
                    double bx = ax + (r.X[j] - r.X[i]), bz = az + (r.Z[j] - r.Z[i]);
                    double sx = bx - ax, sz = bz - az, len2 = sx * sx + sz * sz;
                    double t = len2 < 1e-9 ? 0 : Math.Clamp(-(ax * sx + az * sz) / len2, 0, 1);
                    double px = ax + sx * t, pz = az + sz * t;
                    double reach = r.HalfWidth + margin;
                    if (px * px + pz * pz < reach * reach) return false;
                }
            }
        return true;
    }

    Dictionary<(int, int), List<(InfraRoute R, int I)>> _roadCells;
    int _roadPoints;
    double _roadReach;

    public InfraRoute Nearest(double x, double z, double within, out int index, out double distance)
    {
        index = -1;
        distance = double.MaxValue;
        InfraRoute best = null;
        foreach (InfraRoute r in Routes)
            for (int i = 0; i < r.Count; i++)
            {
                double dx = Mathx.WrapDelta(r.X[i] - x, _w), dz = Mathx.WrapDelta(r.Z[i] - z, _h);
                if (Math.Abs(dx) > within || Math.Abs(dz) > within) continue;
                double d = Math.Sqrt(dx * dx + dz * dz);
                if (d < distance && d <= within) { distance = d; index = i; best = r; }
            }
        return best;
    }

    public double Width => _w;
    public double Height => _h;

    // ---- the path engine ----------------------------------------------------------------------

    /// <summary>
    /// A* over 16 m cells of the whole planet (a torus), one cost table per kind of route: length,
    /// grade against the kind's limit (steeper than it costs dearly: cuttings, embankments and
    /// tunnels), water dearest (a bridge), the sea almost never, other places' pads not at all, and
    /// a route already laid cheap, so new routes join it rather than run beside it.
    /// </summary>
    sealed class Router
    {
        public const float Cell = 16f;
        public readonly PlanetTerrainSource Bare;
        readonly int _nx, _nz;
        readonly double _w, _h;
        readonly float[] _height;
        readonly byte[] _water;     // 0 unknown, 1 dry, 2 river or lake, 3 sea
        readonly bool[] _blocked;
        readonly int[] _road, _rail;   // route id + 1 laid through a cell
        readonly float[] _cost;
        readonly int[] _came;
        readonly bool[] _closed;
        readonly List<int> _touched = new();

        public Router(WorldMap map, PlanetTerrainSource bare, IReadOnlyList<PoiSite> pois)
        {
            Bare = bare;
            _w = map.WidthMeters;
            _h = map.HeightMeters;
            _nx = (int)Math.Ceiling(_w / Cell);
            _nz = (int)Math.Ceiling(_h / Cell);
            _height = new float[_nx * _nz];
            Array.Fill(_height, float.NaN);
            _water = new byte[_nx * _nz];
            _blocked = new bool[_nx * _nz];
            _road = new int[_nx * _nz];
            _rail = new int[_nx * _nz];
            _cost = new float[_nx * _nz];
            Array.Fill(_cost, float.PositiveInfinity);
            _came = new int[_nx * _nz];
            _closed = new bool[_nx * _nz];
            // Places' footprints and aprons are never driven through.
            foreach (PoiSite p in pois) Block(p.Stamp, 4);
        }

        readonly List<TerrainStamp> _pads = new();

        /// <summary>Whether a point lies on a place's own footprint (not just the margin kept round it).</summary>
        public bool OnPad(double x, double z) => _pads.Any(t => t.OnFootprint(Mathx.WrapX(x, _w), Mathx.Wrap(z, _h)));

        public void Block(TerrainStamp t, double margin)
        {
            _pads.Add(t);
            for (double z = t.MinZ - margin; z <= t.MaxZ + margin; z += Cell / 2)
            for (double x = t.MinX - margin; x <= t.MaxX + margin; x += Cell / 2)
                _blocked[Index(x, z)] = true;
        }

        int Index(double x, double z)
        {
            int i = (int)(Mathx.WrapX(x, _w) / Cell), j = (int)(Mathx.Wrap(z, _h) / Cell);
            return Math.Min(_nz - 1, j) * _nx + Math.Min(_nx - 1, i);
        }

        public (double X, double Z) CellCentre(int k) => ((k % _nx + 0.5) * Cell, (k / _nx + 0.5) * Cell);

        float HeightAt(int k)
        {
            if (!float.IsNaN(_height[k])) return _height[k];
            var (x, z) = CellCentre(k);
            float h = Ground(x, z, 16, out bool wet, out float surface);
            _height[k] = h;
            _water[k] = (byte)(!wet ? 1 : surface <= 0.01f && h < -1.5f ? 3 : 2);
            return h;
        }

        /// <summary>
        /// The bare ground's height at a point at a sample spacing (coarser is cheaper), and the water
        /// over it as <see cref="PlanetTerrainSource.WaterSurfaceAt"/> decides it: a river in its
        /// channel, or the sea where the map lies below sea level.
        /// </summary>
        public float Ground(double x, double z, double spacing, out bool wet, out float surface)
        {
            GroundSample g = Bare.Sampler.Sample(x, z, spacing);
            surface = 0f;
            wet = false;
            if (g.InRiver && g.RiverLevel - PlanetTerrainSource.RiverDrop > g.Height)
            {
                surface = g.RiverLevel - PlanetTerrainSource.RiverDrop;
                wet = true;
            }
            else if (g.Height < 0f && Bare.Map.Sample(Bare.Map.Elevation, x, z) < -PlanetTerrainSource.SeaMaskDepth) wet = true;
            return g.Height;
        }

        byte WaterAt(int k)
        {
            if (_water[k] == 0) HeightAt(k);
            return _water[k];
        }

        /// <summary>Whether a point lies on a place's footprint or apron.</summary>
        public bool Blocked(double x, double z) => _blocked[Index(x, z)];

        public int RouteAt(int k, RouteKind kind) => (kind == RouteKind.Rail ? _rail[k] : _road[k]) - 1;
        public int RouteAtPoint(double x, double z, RouteKind kind) => RouteAt(Index(x, z), kind);
        /// <summary>Open sea at a point.</summary>
        public bool Sea(double x, double z) => WaterAt(Index(x, z)) == 3;
        /// <summary>Any water at a point.</summary>
        public bool Wet(double x, double z) => WaterAt(Index(x, z)) >= 2;

        /// <summary>Marks the cells a laid route runs through, so later routes join it.</summary>
        public void Mark(InfraRoute r)
        {
            int[] grid = r.Kind == RouteKind.Rail ? _rail : _road;
            for (int i = 0; i < r.Count; i++) grid[Index(r.X[i], r.Z[i])] = r.Id + 1;
        }

        static readonly (int dx, int dz)[] Moves =
        {
            (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1),
            (2, 1), (2, -1), (-2, 1), (-2, -1), (1, 2), (1, -2), (-1, 2), (-1, -2),
        };

        /// <summary>The cells of the cheapest path between two points, or null.</summary>
        public List<int> Find((double X, double Z) from, (double X, double Z) to, RouteKind kind, TechLevel tech)
        {
            int start = Free(from), goal = Free(to);
            if (start < 0 || goal < 0) return null;
            foreach (int t in _touched) { _cost[t] = float.PositiveInfinity; _closed[t] = false; }
            _touched.Clear();
            var open = new PriorityQueue<int, float>();
            _cost[start] = 0;
            _came[start] = start;
            _touched.Add(start);
            open.Enqueue(start, 0);
            int gx = goal % _nx, gz = goal / _nx;
            // A railway's deck holds its own grade whatever the land does: it isn't asked to wind round every rise, only to go the easiest way.
            float grade = kind == RouteKind.Rail ? 0.09f : MaxGrade(kind);
            // A river is bridged where the road meets it, rather than followed for kilometres to a ford.
            float bridge = kind switch { RouteKind.Highway => 2f, RouteKind.Road => 3f, RouteKind.Track => 8f, _ => 5f };
            // Crossing open sea: a long causeway only where the old world had the means, never on a frontier world.
            float sea = tech == TechLevel.Low ? float.PositiveInfinity : tech == TechLevel.Mid ? 120f : 60f;
            int expanded = 0;
            while (open.Count > 0)
            {
                int k = open.Dequeue();
                if (k == goal) break;
                if (_closed[k]) continue;
                _closed[k] = true;
                if (++expanded > 600000) return null;
                float ck = _cost[k];
                int kx = k % _nx, kz = k / _nx;
                float hk = HeightAt(k);
                foreach (var (dx, dz) in Moves)
                {
                    int mx = ((kx + dx) % _nx + _nx) % _nx, mz = ((kz + dz) % _nz + _nz) % _nz;
                    int m = mz * _nx + mx;
                    if (_blocked[m] && m != goal) continue;
                    // A knight's move passes over the two cells beside it.
                    if (Math.Abs(dx) + Math.Abs(dz) == 3)
                    {
                        int ax = ((kx + Math.Sign(dx)) % _nx + _nx) % _nx, az = ((kz + Math.Sign(dz)) % _nz + _nz) % _nz;
                        if (_blocked[az * _nx + ax] || _blocked[(((kz + dz - Math.Sign(dz)) % _nz + _nz) % _nz) * _nx + (((kx + dx - Math.Sign(dx)) % _nx + _nx) % _nx)]) continue;
                    }
                    float len = Cell * MathF.Sqrt(dx * dx + dz * dz);
                    float hm = HeightAt(m);
                    float g = MathF.Abs(hm - hk) / len, r = g / grade;
                    // Within its grade a road or highway doesn't care about the slope, or it would wander round every rise; past it, it does.
                    float c = r <= 1f ? len * (1f + (kind is RouteKind.Highway or RouteKind.Road ? 0.25f : 1.5f) * r * r) : len * (kind is RouteKind.Highway or RouteKind.Road ? 1.25f + 30f * (r - 1f) : 2.5f + 30f * (r - 1f));
                    byte wtr = WaterAt(m);
                    if (wtr == 2) c += len * bridge;
                    else if (wtr == 3) c += len * sea;
                    if (float.IsPositiveInfinity(c)) continue;
                    if (RouteAt(m, kind) >= 0) c *= 0.3f;
                    else if (kind != RouteKind.Rail && _rail[m] > 0 || kind == RouteKind.Rail && _road[m] > 0) c += len * 2f;   // crossings, not running alongside
                    float nc = ck + c;
                    if (_cost[m] <= nc) continue;
                    if (float.IsPositiveInfinity(_cost[m])) _touched.Add(m);
                    _cost[m] = nc;
                    _came[m] = k;
                    double ddx = Math.Abs(mx - gx), ddz = Math.Abs(mz - gz);
                    ddx = Math.Min(ddx, _nx - ddx);
                    ddz = Math.Min(ddz, _nz - ddz);
                    // Headed for the goal: of two ways as good, the straighter.
                    open.Enqueue(m, nc + (float)(Math.Sqrt(ddx * ddx + ddz * ddz) * Cell * 1.5));
                }
            }
            if (float.IsPositiveInfinity(_cost[goal])) return null;
            var path = new List<int> { goal };
            while (path[^1] != start) path.Add(_came[path[^1]]);
            path.Reverse();
            return path;
        }

        /// <summary>Whether the sea parts two points: no way between them over land (rivers can be bridged).</summary>
        public bool SeaBetween((double X, double Z) a, (double X, double Z) b)
        {
            int start = Free(a), goal = Free(b);
            if (start < 0 || goal < 0) return false;
            var seen = new bool[_nx * _nz];
            var queue = new Queue<int>();
            queue.Enqueue(start);
            seen[start] = true;
            while (queue.Count > 0)
            {
                int k = queue.Dequeue();
                if (k == goal) return false;
                int kx = k % _nx, kz = k / _nx;
                foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int m = ((kz + dz) % _nz + _nz) % _nz * _nx + ((kx + dx) % _nx + _nx) % _nx;
                    if (seen[m] || (_blocked[m] && m != goal) || WaterAt(m) == 3) continue;
                    seen[m] = true;
                    queue.Enqueue(m);
                }
            }
            return true;
        }

        /// <summary>The cell of a point, or the nearest free one within a few cells.</summary>
        int Free((double X, double Z) p)
        {
            int k = Index(p.X, p.Z);
            if (!_blocked[k]) return k;
            for (int r = 1; r <= 4; r++)
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                int m = Index(p.X + dx * Cell, p.Z + dz * Cell);
                if (!_blocked[m]) return m;
            }
            return -1;
        }
    }
}
