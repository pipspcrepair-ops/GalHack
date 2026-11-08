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
    /// <summary>
    /// A graded dirt road: a road nobody paved, its own ground scraped wide and level, the spoil left in a berm
    /// along each side. Every road of a world that never paved one, and haul roads out to mines, farms and
    /// works that only served those.
    /// </summary>
    public bool Graded;
    /// <summary>A waterfront road: the side its seawall stands on (+1 left of its way, -1 right, 0 none); the ground past the wall drops to the water.</summary>
    public int WallSide;
    /// <summary>The form of the junction at each end (None at a place's gate or a dead end).</summary>
    public JunctionForm StartJoin, EndJoin;
    /// <summary>A roundabout's ring: a closed street round its island.</summary>
    public bool Ring;
    /// <summary>
    /// Where it runs on at an end into a way of another class (a track into a graded road, a highway into a road):
    /// that way's half width there, 0 where it doesn't. Its painted width tapers to meet it (<see cref="SeamWidth"/>).
    /// </summary>
    public float StartMeetHw, EndMeetHw;
    /// <summary>And whether that way is of the dirt family or paved (its taper's length follows the two).</summary>
    public bool StartMeetSoil, EndMeetSoil;
    /// <summary>A street's turning bulb at an end: the tilt of its plane (rise per metre in x and z), following the land within a street's grades.</summary>
    public (float X, float Z) StartTilt, EndTilt;

    /// <summary>A way of the land's own ground (a track or a graded dirt road), not paved.</summary>
    public bool Soil => Kind == RouteKind.Track || Graded || Surface is StampSurface.Dirt or StampSurface.Gravel;

    public int Count => X.Length;
}

/// <summary>The forms a junction is built in (design doc "Infrastructure"): chosen from the ways that meet, their class, the angle, the ground and the world's tech.</summary>
public enum JunctionForm { None, RunOn, Tee, Crossroads, Roundabout, Slip, Fork, DeadEnd }

/// <summary>One way out of a junction: its route, whether it leaves along the route's points or back along them, and its heading (unit x, z).</summary>
public sealed record JunctionArm(InfraRoute Route, bool Forward, (double X, double Z) Dir)
{
    public double DirX => Dir.X;
    public double DirZ => Dir.Z;
}

/// <summary>A junction: where it is, the way the others join, every way out of it, and its form.</summary>
public sealed class InfraJunction
{
    public double X, Z;
    public float Y;
    public InfraRoute Through;
    public readonly List<JunctionArm> Arms = new();
    public JunctionForm Form;
    public string Why = "";
    /// <summary>A roundabout's ring (null for any other form), the radius of its centre line and its half width, m.</summary>
    public InfraRoute Ring;
    public double RingR = InfraNetwork.RingRadius;
    public float RingHw = 4f;
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
    /// <summary>A ruined city, on a world whose layers built one (null otherwise; see CityPlan).</summary>
    public CityPlan City;
    /// <summary>
    /// Lots that front a street, each with its frontage, gate and crossover (<see cref="CityLotPlan"/>): what a
    /// planner of places along streets hands the roads side, which draws the verges, footpaths, kerbs and
    /// crossovers from them. The city's lots, today.
    /// </summary>
    public readonly List<CityLotPlan> Lots = new();
    /// <summary>Flavour ruins along the roads and in the countryside (planned on the bare land: <see cref="FlavourRuins.Plan"/>).</summary>
    public List<FlavourRuin> Ruins = new();

    /// <summary>The made ground round the places (yards, aprons, car parks, footpaths, paths), for the terrain shader (<see cref="MadeGrounds"/>).</summary>
    public List<GalHack.Terrain.GroundPatch> Grounds = new();
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
    /// <summary>How high a bridge's deck stands at its ends at least, m: lower, the bank is built up to it (an embankment).</summary>
    public const float EmbankHeight = 3.5f;
    /// <summary>How high a span must stand to have fallen, m (lower, it would only have settled onto the land).</summary>
    public const float FallHeight = 6f;
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
    /// <summary>A graded road's half width: wider than a paved road of its class, for the grader's two passes and the drift at its edges.</summary>
    public const float GradedHalfWidth = 4.6f;

    /// <summary>A place out in the country that a haul road served: a mine, a farm or a works, not a town.</summary>
    static bool Rural(PoiSite p) => p != null && p.Tier is not (PoiTier.Town or PoiTier.City or PoiTier.Spaceport)
        && (p.Family.Tags.Contains("mine") || p.Family.Tags.Contains("farm") || p.Family.Tags.Contains("industry"));

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
        net.JoinNeighbours(router, rng);
        net.KeepClass();
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
        // The back street turns down at each end into the last side streets, round corners and not elbows.
        bool ring = Free(At(-half, arm), At(half, arm)) && Free(At(-half, 0), At(-half, arm)) && Free(At(half, 0), At(half, arm)), backed = false;
        if (ring && Street(new List<(double X, double Z)> { At(-half, 0), At(-half, arm), At(half, arm), At(half, 0) }, RouteKind.Road, round: TownRadius(RouteKind.Track)) is { } back)
        {
            back.HalfWidth = SideWidth;
            TownStreets++;
            backed = true;
        }
        else if (Free(At(-half, arm), At(half, arm)) && Street(new List<(double X, double Z)> { At(-half, arm), At(half, arm) }, RouteKind.Road) is { } straightBack)
        {
            ring = false;
            straightBack.HalfWidth = SideWidth;
            TownStreets++;
            backed = true;
        }
        else ring = false;
        // The side streets run from the main street to the back street; with no back street they would only run out into
        // the fields as dead ends with nothing along them, and aren't laid.
        for (int c = -blocks; c <= blocks && backed; c++)
            if ((!ring || Math.Abs(c) < blocks) && Free(At(c * Block, 0), At(c * Block, arm)) && Street(new List<(double X, double Z)> { At(c * Block, 0), At(c * Block, arm) }, RouteKind.Road) is { } side)
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
        // A way keeps its class along its whole length: the runs are laid first, and if any of them was too squeezed to
        // keep a paved road's curves, the whole link is built as the lesser way (never a short paved stretch between tracks).
        var laidRuns = new List<(List<(double X, double Z)> Path, List<(int Index, float Y)> Pins, (int From, int To, int JoinFrom, int JoinTo) Run, string Laid)>();
        bool narrowedAny = false;
        foreach (var run in runs)
        {
            if (run.To - run.From < 1 && run.JoinFrom >= 0 && run.JoinTo >= 0) continue;
            // A gate that already has its way out (an earlier link left by it, and this one's search just stepped onto
            // that): a second, short way out of the same gate would lie over the first, two decks meeting at nothing.
            bool Served(PoiSite p, int gate) => Routes.Any(q => q.From == p && q.FromGate == gate || q.To == p && q.ToGate == gate);
            if (run.From == 0 && run.JoinTo >= 0 && run.To - run.From < 12 && Served(a, ga.Index)
                || run.To == cells.Count - 1 && run.JoinFrom >= 0 && run.To - run.From < 12 && Served(b, gb.Index)) { any = true; continue; }
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
            bool narrowed = _narrowed;
            string laidAs = _laid;
            if (path.Count < 2) continue;
            // A stretch between two roads already laid that only runs along them (the search stepping off a road and
            // back on, or from one to another lying beside it) adds nothing but a second deck over theirs: left out.
            if (run.JoinFrom >= 0 && run.JoinTo >= 0 && path.All(q =>
                    Snap(Routes[run.JoinFrom], q, out var qa, out _) && Math.Sqrt(Math.Pow(qa.X - q.X, 2) + Math.Pow(qa.Z - q.Z, 2)) < Routes[run.JoinFrom].HalfWidth + 6
                    || Snap(Routes[run.JoinTo], q, out var qb, out _) && Math.Sqrt(Math.Pow(qb.X - q.X, 2) + Math.Pow(qb.Z - q.Z, 2)) < Routes[run.JoinTo].HalfWidth + 6))
            {
                any = true;
                continue;
            }
            // Junction ends land exactly on the route they join, at its deck height.
            // Where the link goes on along the road it meets, it merges into it in a sweeping curve; where it
            // only meets or crosses it, it meets it square.
            int ContinuesAlong(int join, int from, int step)
            {
                int n = 0;
                for (int k = from; k >= 0 && k < cells.Count && router.RouteAt(cells[k], kind) == join; k += step) n++;
                return n;
            }
            // (The line is eased first, its ends on the junctions, so the last stretch into each keeps the shape given it.)
            (double X, double Z) p0 = default, d0 = default, p1 = default, d1 = default;
            bool joinA = run.JoinFrom >= 0 && Snap(Routes[run.JoinFrom], path[0], out p0, out _, out d0);
            if (joinA) { path[0] = p0; ClearStart(path); }
            bool joinB = run.JoinTo >= 0 && Snap(Routes[run.JoinTo], path[^1], out p1, out _, out d1);
            if (joinB) { path[^1] = p1; ClearEnd(path); }
            if (kind is RouteKind.Road or RouteKind.Highway) path = Ease(router, path, LeastRadius(kind), 2, 2, out _);
            // Only a highway going on along a highway it meets leaves it by a slip road (merging in, a gore at the nose);
            // every other way meets the one it joins near square, a T, however it came up to it.
            if (joinA)
            {
                int along = ContinuesAlong(run.JoinFrom, run.From - 1, -1);
                if (kind == RouteKind.Highway && Routes[run.JoinFrom].Kind == RouteKind.Highway && along >= 3)
                {
                    var c = router.CellCentre(cells[Math.Max(0, run.From - Math.Min(along, 4))]);
                    double cx = p0.X + Mathx.WrapDelta(c.X - p0.X, _w), cz = p0.Z + Mathx.WrapDelta(c.Z - p0.Z, _h);
                    double sgn = Math.Sign(d0.X * (p0.X - cx) + d0.Z * (p0.Z - cz));
                    Merge(path, Routes[run.JoinFrom], (d0.X * sgn, d0.Z * sgn), LeastRadius(kind), true);
                }
                else if (kind != RouteKind.Rail)
                {
                    if (Approach(path, Routes[run.JoinFrom], true, out var pA, out var dA)) { path[0] = pA; (p0, d0) = (pA, dA); }
                    Square(path, Routes[run.JoinFrom], p0, d0, true, MouthRadius(kind, a, b));
                }
                ClearStart(path);
            }
            if (joinB)
            {
                int along = ContinuesAlong(run.JoinTo, run.To + 1, 1);
                if (kind == RouteKind.Highway && Routes[run.JoinTo].Kind == RouteKind.Highway && along >= 3)
                {
                    var c = router.CellCentre(cells[Math.Min(cells.Count - 1, run.To + Math.Min(along, 4))]);
                    double cx = p1.X + Mathx.WrapDelta(c.X - p1.X, _w), cz = p1.Z + Mathx.WrapDelta(c.Z - p1.Z, _h);
                    double sgn = Math.Sign(d1.X * (cx - p1.X) + d1.Z * (cz - p1.Z));
                    Merge(path, Routes[run.JoinTo], (d1.X * sgn, d1.Z * sgn), LeastRadius(kind), false);
                }
                else if (kind != RouteKind.Rail)
                {
                    if (Approach(path, Routes[run.JoinTo], false, out var pB, out var dB)) { path[^1] = pB; (p1, d1) = (pB, dB); }
                    Square(path, Routes[run.JoinTo], p1, d1, false, MouthRadius(kind, a, b));
                }
                ClearEnd(path);
            }
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
            narrowedAny |= narrowed;
            laidRuns.Add((path, pins, run, laidAs));
        }
        foreach (var (path, pins, run, laidAs) in laidRuns)
        {
            _laid = laidAs;
            var route = Build(router, path, pins, narrowedAny ? RouteKind.Track : kind, a, b, run.From == 0 ? ga.Index : -1, run.To == cells.Count - 1 ? gb.Index : -1, rng);
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

    /// <summary>Lanes laid between neighbouring places and from places no road reached (tests).</summary>
    public int NeighbourLanes, Spurs;

    /// <summary>Proof frames: lay no lanes between neighbours (--lanes=0, the network as it was).</summary>
    public static bool NoLanes;

    /// <summary>
    /// A cluster of places reads as one place: neighbours under a kilometre apart get a lane between their gates,
    /// a paved one on a world that paved its roads (a track on a frontier world), unless a road of the network
    /// already runs gate to gate between them; and a place no road reached gets a lane out to the nearest road.
    /// Each lane is laid like any other link (round bends, merging where it meets a road), at a lane's width.
    /// </summary>
    void JoinNeighbours(Router router, PoiGenerator.Gen.Rng rng)
    {
        if (NoLanes) return;
        RouteKind lane = Profile.Before == TechLevel.Low ? RouteKind.Track : RouteKind.Road;
        bool Direct(PoiSite a, PoiSite b) => Routes.Any(r => r.From == a && r.To == b || r.From == b && r.To == a);
        int Out(PoiSite p) => Routes.Count(r => r.From == p || r.To == p);
        var joins = new Dictionary<PoiSite, int>();
        var pairs = new List<(PoiSite A, PoiSite B, double Gap)>();
        for (int i = 0; i < Places.Count; i++)
        for (int j = i + 1; j < Places.Count; j++)
        {
            PoiSite a = Places[i], b = Places[j];
            if (a.Siting == "city" || b.Siting == "city") continue;
            double d = Dist(a.Center, b.Center);
            double gap = d - Math.Max(a.Stamp.ReachX, a.Stamp.ReachZ) - Math.Max(b.Stamp.ReachX, b.Stamp.ReachZ);
            if (d < 900 && gap < 450) pairs.Add((a, b, gap));
        }
        int direct = 0, failed = 0;
        foreach (var (a, b, _) in pairs.OrderBy(p => p.Gap))
        {
            if (Direct(a, b)) direct++;
            // Never a star: a place with roads enough out of it already gets no more.
            if (Direct(a, b) || joins.GetValueOrDefault(a) >= 2 || joins.GetValueOrDefault(b) >= 2 || Out(a) >= 4 || Out(b) >= 4) continue;
            int before = Routes.Count;
            if (!Link(router, a, b, lane, rng)) { failed++; continue; }
            for (int k = before; k < Routes.Count; k++) if (Routes[k].Kind == RouteKind.Road) Routes[k].HalfWidth = 2.6f;
            joins[a] = joins.GetValueOrDefault(a) + 1;
            joins[b] = joins.GetValueOrDefault(b) + 1;
            NeighbourLanes++;
        }
        // Anything still on its own: out from its way in to the nearest road within reach.
        foreach (PoiSite p in Places.Where(p => p.Siting != "city" && !Routes.Any(r => r.From == p || r.To == p)).ToList())
        {
            var g = GateToward(p, p.Center);
            int before = Routes.Count;
            if (!Spur((g.X + g.OutX * 3, g.Z + g.OutZ * 3), lane, 3000)) continue;
            for (int k = before; k < Routes.Count; k++)
            {
                if (Routes[k].Kind == RouteKind.Road) Routes[k].HalfWidth = 2.6f;
                Routes[k].From ??= p;
            }
            Spurs++;
        }
        double closest = double.MaxValue;
        for (int i = 0; i < Places.Count; i++)
            for (int j = i + 1; j < Places.Count; j++) closest = Math.Min(closest, Dist(Places[i].Center, Places[j].Center));
        LaneLog = $"{Places.Count} places, closest two {closest:F0} m apart, {pairs.Count} neighbour pairs ({direct} joined directly already, {failed} found no way)";
    }

    /// <summary>What joining the neighbours found (tests).</summary>
    public string LaneLog = "";

    /// <summary>Paved stretches laid between ways of bare ground at both ends, built as the lesser way instead (tests).</summary>
    public int Demoted;

    /// <summary>The ways an end of a route meets (its end lies on their decks), itself and railways aside.</summary>
    public List<InfraRoute> EndJoins(InfraRoute r, bool atStart)
    {
        var met = new List<InfraRoute>();
        if (r.Count < 2) return met;
        int e = atStart ? 0 : r.Count - 1;
        double x = Mathx.WrapX(r.X[e], _w), z = Mathx.Wrap(r.Z[e], _h);
        var skip = new List<InfraRoute> { r };
        for (int c = 0; c < 5; c++)
        {
            InfraRoute q = Nearest(x, z, 16, out _, out double qd, skip);
            if (q == null) break;
            skip.Add(q);
            if (q.Kind != RouteKind.Rail && qd <= q.HalfWidth + 1.5) met.Add(q);
        }
        return met;
    }

    /// <summary>
    /// A road keeps its class from one junction or place to the next: a paved road whose ends both meet only ways of bare
    /// ground (tracks, graded dirt roads), with no place at either, was never paved: a short paved stretch between dirt
    /// tracks. It is built as the better of the ways it meets (a graded road where one meets it, a track otherwise).
    /// </summary>
    void KeepClass()
    {
        for (bool changed = true; changed;)
        {
            changed = false;
            foreach (InfraRoute r in Routes)
            {
                if (r.Kind != RouteKind.Road || r.Soil || r.Laid.Length == 0 || r.Ring || r.Count < 2 || r.From != null || r.To != null) continue;
                List<InfraRoute> a = EndJoins(r, true), b = EndJoins(r, false);
                if (a.Count == 0 || b.Count == 0 || a.Concat(b).Any(q => !q.Soil)) continue;
                if (a.Concat(b).Any(q => q.Graded))
                {
                    r.Graded = true;
                    r.HalfWidth = GradedHalfWidth;
                    r.Surface = StampSurface.Dirt;
                }
                else
                {
                    r.Kind = RouteKind.Track;
                    r.HalfWidth = HalfWidthOf(RouteKind.Track);
                    r.Surface = SurfaceOf(RouteKind.Track, Profile.Before);
                }
                Demoted++;
                changed = true;
            }
        }
    }

    /// <summary>Dead-end streets cut back to their last lot, streets that served nothing taken out, and lots given up where a street's end was too steep for its turning bulb (tests).</summary>
    public int StreetsTrimmed, StreetsRemoved, LotsDropped;

    /// <summary>The most a turning bulb's level is tilted across the way its street runs (rise over run).</summary>
    public const float BulbCross = 0.05f;
    /// <summary>The most a turning bulb's edge may stand off the land round it, m: past that it would sit on a bank.</summary>
    public const float BulbBank = 1.2f;

    /// <summary>
    /// A street that just stops is a cul-de-sac for the lots along it, never a kerbed dead end out into a field: each such
    /// end is cut back to a turning bulb's room past its last lot's crossover, and a street with no lot along it at all and
    /// nothing else joining it is taken out. A bulb lies on the land as a gently tilted plane (the street's grade along it,
    /// the land's across it within <see cref="BulbCross"/>); where the land is too steep for that, the street ends earlier,
    /// on flatter ground, and the lots past that end are given up. Runs once the lots are placed, before the junctions.
    /// </summary>
    public void PruneDeadEnds(Func<double, double, float> bare)
    {
        // (Not a city's grid: its blocks are laid to its streets as they are.)
        foreach (InfraRoute r in Routes.Where(r => r.Kind == RouteKind.Road && r.Laid.Length == 0 && !r.Ring && !r.Soil && City?.Streets.Contains(r) != true).ToList())
            for (int pass = 0; pass < 2 && Routes.Contains(r); pass++)
            {
                bool atStart = pass == 0;
                if (r.Count < 2 || (atStart ? r.From : r.To) != null || EndJoins(r, atStart).Count > 0) continue;
                // The lots along it, by where their crossovers meet it.
                var lots = new List<(int Index, FlavourRuin Ruin)>();
                foreach (FlavourRuin ruin in Ruins.Where(q => q.Join?.Road == r)) lots.Add((ruin.Join.Index, ruin));
                foreach (CityLotPlan lot in Lots.Where(l => l.Street == r)) lots.Add((NearestIndex(r, lot.Crossover.X, lot.Crossover.Z), null));
                // Other ways joining it along its length (a side street, a lane out to a place): it reaches them whatever its lots
                // do, its bulb a turning bulb's room past the last; they are never given up.
                var joins = new List<int>();
                foreach (InfraRoute q in Routes)
                    foreach (bool qs in new[] { true, false })
                        if (q != r && q.Kind != RouteKind.Rail && q.Count >= 2 && EndJoins(q, qs).Contains(r))
                            joins.Add(NearestIndex(r, q.X[qs ? 0 : q.Count - 1], q.Z[qs ? 0 : q.Count - 1]));
                // (Its own other end meeting a way counts only as where it starts.)
                bool joined = joins.Count > 0 || EndJoins(r, !atStart).Count > 0;
                if (lots.Count == 0 && joins.Count == 0 && Ruins.Any(q => q.Route == r))
                {
                    BulbFits(r, atStart ? 0 : r.Count - 1, atStart, bare, out var t0);
                    if (atStart) r.StartTilt = t0; else r.EndTilt = t0;
                    continue;
                }
                if (lots.Count == 0 && joins.Count == 0)
                {
                    // Nothing along it: a street to nowhere, taken out (and anything leaning on it).
                    Town?.Streets.Remove(r);
                    City?.Streets.Remove(r);
                    Remove(r);
                    StreetsRemoved++;
                    break;
                }
                float margin = RoadCorners.BulbRadius(r) + 5f;
                // (The ways joining it are kept whatever: past the last of them, only the lots beyond may be given up.)
                int held = joins.Count == 0 ? -1 : atStart ? joins.Min() : joins.Max();
                foreach (int k in joins) lots.Add((k, null));
                while (lots.Count > 0)
                {
                    int last = atStart ? lots.Min(l => l.Index) : lots.Max(l => l.Index);
                    int extra = (int)Math.Ceiling(margin / Spacing);
                    int end = atStart ? Math.Max(0, last - extra) : Math.Min(r.Count - 1, last + extra);
                    if (last == held || BulbFits(r, end, atStart, bare, out _)) break;
                    // Too steep for a bulb there: the street ends short of this lot, which is given up.
                    foreach (var l in lots.Where(l => l.Index == last && l.Ruin != null).ToList())
                    {
                        Ruins.Remove(l.Ruin);
                        Town?.Houses.Remove(l.Ruin);
                        LotsDropped++;
                    }
                    lots.RemoveAll(l => l.Index == last);
                }
                if (lots.Count == 0)
                {
                    // No lot keeps a bulb on level enough ground: the street goes, with the lots that leaned on it.
                    foreach (FlavourRuin q in Ruins.Where(q => q.Route == r).ToList()) { Ruins.Remove(q); Town?.Houses.Remove(q); LotsDropped++; }
                    if (Lots.Any(l => l.Street == r)) continue;
                    Town?.Streets.Remove(r);
                    City?.Streets.Remove(r);
                    Remove(r);
                    StreetsRemoved++;
                    break;
                }
                int keepTo = atStart ? Math.Max(0, lots.Min(l => l.Index) - (int)Math.Ceiling(margin / Spacing)) : Math.Min(r.Count - 1, lots.Max(l => l.Index) + (int)Math.Ceiling(margin / Spacing));
                int count = atStart ? r.Count - keepTo : keepTo + 1;
                if (count < r.Count)
                {
                    Trim(r, count, !atStart);
                    foreach (FlavourRuin q in Ruins.Where(q => q.Join?.Road == r && atStart)) q.Join.Index -= keepTo;
                    StreetsTrimmed++;
                }
                BulbFits(r, atStart ? 0 : r.Count - 1, atStart, bare, out var tilt);
                if (atStart) r.StartTilt = tilt; else r.EndTilt = tilt;
            }
    }

    /// <summary>The point of a route nearest a world point.</summary>
    int NearestIndex(InfraRoute r, double x, double z)
    {
        int best = 0;
        double bd = double.MaxValue;
        for (int i = 0; i < r.Count; i++)
        {
            double d = Dist((r.X[i], r.Z[i]), (x, z));
            if (d < bd) { bd = d; best = i; }
        }
        return best;
    }

    /// <summary>
    /// Whether a street ending at point <paramref name="e"/> has ground for its turning bulb: a plane through the deck there,
    /// along the street at its grade and across it at the land's slope (no more than <see cref="BulbCross"/>), lies within
    /// <see cref="BulbBank"/> of the land all round the bulb's bed. Its tilt (rise per metre in x and z) either way.
    /// </summary>
    public bool BulbFits(InfraRoute r, int e, bool atStart, Func<double, double, float> bare, out (float X, float Z) tilt)
    {
        int o = atStart ? Math.Min(r.Count - 1, e + 3) : Math.Max(0, e - 3);
        double tx = r.X[e] - r.X[o], tz = r.Z[e] - r.Z[o], tl = Math.Max(1e-6, Math.Sqrt(tx * tx + tz * tz));
        tx /= tl; tz /= tl;
        double nx = -tz, nz = tx;
        float along = Math.Clamp((float)((r.Y[e] - r.Y[o]) / tl), -MaxGrade(RouteKind.Road), MaxGrade(RouteKind.Road));
        float rim = RoadCorners.BulbRadius(r) + (Kerbed(r) ? FootpathOut + 0.4f : 1.6f);
        // The land's slope across the street at its end, by least squares over the rim.
        double sxy = 0, sxx = 0;
        var pts = new List<(double A, double C, float G)>();
        for (int k = 0; k < 16; k++)
        {
            double ang = k * Math.Tau / 16, ca = Math.Cos(ang) * rim, cc = Math.Sin(ang) * rim;
            double x = r.X[e] + tx * ca + nx * cc, z = r.Z[e] + tz * ca + nz * cc;
            float g = bare(Mathx.WrapX(x, _w), Mathx.Wrap(z, _h));
            pts.Add((ca, cc, g));
            double rest = g - r.Y[e] - along * ca;
            sxy += rest * cc;
            sxx += cc * cc;
        }
        float cross = Math.Clamp((float)(sxy / Math.Max(sxx, 1e-6)), -BulbCross, BulbCross);
        tilt = ((float)(tx * along + nx * cross), (float)(tz * along + nz * cross));
        float worst = 0;
        foreach (var (a, c, g) in pts) worst = Math.Max(worst, Math.Abs(g - (r.Y[e] + along * (float)a + cross * (float)c)));
        return worst <= BulbBank;
    }

    /// <summary>Takes a route out of the network (a street that served nothing): its structures and cells with it, every later route's id one less.</summary>
    public void Remove(InfraRoute r)
    {
        int id = Routes.IndexOf(r);
        if (id < 0) return;
        Structures.RemoveAll(s => s.Route == r);
        Routes.RemoveAt(id);
        for (int i = id; i < Routes.Count; i++) Routes[i].Id = i;
        _router?.Renumber(id, r.Kind == RouteKind.Rail);
        foreach (PoiSite k in _mainStreets.Where(kv => kv.Value == r).Select(kv => kv.Key).ToList()) _mainStreets.Remove(k);
        _roadCells = null;
    }

    /// <summary>Cuts a route short, keeping <paramref name="count"/> points from its start (or from its end), its structures and cells to match.</summary>
    public void Trim(InfraRoute r, int count, bool keepStart)
    {
        int n = r.Count;
        if (count >= n || count < 2) return;
        int from = keepStart ? 0 : n - count;
        _router?.Unmark(r);
        r.X = r.X.Skip(from).Take(count).ToArray();
        r.Z = r.Z.Skip(from).Take(count).ToArray();
        r.Y = r.Y.Skip(from).Take(count).ToArray();
        r.Span = r.Span.Skip(from).Take(count - 1).ToArray();
        if (!keepStart) { r.From = null; r.FromGate = -1; }
        else { r.To = null; r.ToGate = -1; }
        Structures.RemoveAll(s => s.Route == r && (s.First < from || s.Last > from + count - 1));
        foreach (InfraStructure s in Structures.Where(s => s.Route == r)) { s.First -= from; s.Last -= from; }
        r.Length = 0;
        for (int i = 0; i + 1 < r.Count; i++) r.Length += Math.Sqrt(Math.Pow(r.X[i + 1] - r.X[i], 2) + Math.Pow(r.Z[i + 1] - r.Z[i], 2));
        _router?.Mark(r);
        _roadCells = null;
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
            if (kind != RouteKind.Rail && Approach(path, near, false, out var pa, out var da)) { path[^1] = pa; (p1, d1) = (pa, da); }
            if (Snap(near, path[^1], out _, out float ya)) y1 = ya;
            if (kind != RouteKind.Rail) Square(path, near, p1, d1, false, MouthRadius(kind, p, null));
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
        if (kind == RouteKind.Track) return StraightenFlats(router, smooth);
        if (kind is not (RouteKind.Highway or RouteKind.Road) || pts.Count < 4) return smooth;
        bool highway = kind == RouteKind.Highway;
        // Paved roads never hairpin: a crowded corner gives way only to its class's least radius, and a run
        // that can't keep it takes the next fit, or the land's own line smoothed below.
        double radius = highway ? 220 : 130, least = highway ? LeastRadius(RouteKind.Highway) : LeastRadius(RouteKind.Road);
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

    /// <summary>Ground steeper than this (rise over run) is a hill to a track: there it keeps the land's winding line, and may switch back.</summary>
    public const float TrackHill = 0.07f;

    /// <summary>
    /// A track follows the land: across flat ground it runs straight from where it must turn to where it must
    /// (whatever made it took the short way when nothing stopped it), and up a hill it keeps the land's winding
    /// line, the only place it may switch back. Runs of flat ground 60 m and longer become straights joined by
    /// easy curves; the first and last 24 m (the way out of a gate, into a junction) stay as they are.
    /// </summary>
    List<(double X, double Z)> StraightenFlats(Router router, List<(double X, double Z)> path)
    {
        int n = path.Count;
        const int Keep = 6, Least = 15;
        if (n < Keep * 2 + Least) return path;
        var flat = new bool[n];
        for (int i = 0; i < n; i++)
        {
            double x = Mathx.WrapX(path[i].X, _w), z = Mathx.Wrap(path[i].Z, _h);
            float c = router.Ground(x, z, 2, out bool wet, out _);
            float gx = router.Ground(Mathx.WrapX(x + 8, _w), z, 2, out _, out _) - c, gz = router.Ground(x, Mathx.Wrap(z + 8, _h), 2, out _, out _) - c;
            flat[i] = !wet && Math.Sqrt(gx * gx + gz * gz) / 8 < TrackHill;
        }
        var result = new List<(double X, double Z)>(n);
        bool changed = false;
        for (int k = 0; k < n;)
        {
            int j = k;
            while (j < n && flat[j] == flat[k]) j++;
            int a = Math.Max(k, Keep), b = Math.Min(j, n - Keep);
            if (flat[k] && b - a >= Least)
            {
                // The run's own ends and a point 12 m in from each: the straights leave the way the track came in.
                var land = new List<(double X, double Z)> { path[a] };
                for (int i = a + 3; i <= b - 4; i++) land.Add(path[i]);
                land.Add(path[b - 1]);
                List<(double X, double Z)> corners = Straights(router, land, 18, 60, 1, 1, 14);
                List<(double X, double Z)> sweep = Sweep(corners, 40, 10, Math.PI * 0.9, out _);
                if (sweep != null)
                {
                    List<(double X, double Z)> laid = Resample(sweep, Spacing);
                    if (!laid.Any(q => router.Blocked(q.X, q.Z)))
                    {
                        for (int i = k; i < a; i++) result.Add(path[i]);
                        result.AddRange(laid);
                        for (int i = b; i < j; i++) result.Add(path[i]);
                        changed = true;
                        k = j;
                        continue;
                    }
                }
            }
            for (int i = k; i < j; i++) result.Add(path[i]);
            k = j;
        }
        // Where a straight meets the land's line again, one more easing, the ends kept.
        return changed ? Resample(Smooth(result, Keep, Keep), Spacing) : path;
    }

    /// <summary>
    /// The curve a way swings round on to meet another square: a paved road's class's least radius, a graded dirt road's a
    /// town corner's (it is laid on the land's own ground, and comes in square however short its last stretch), a track's none.
    /// </summary>
    double MouthRadius(RouteKind kind, PoiSite a, PoiSite b) =>
        kind == RouteKind.Road && (Profile.Before == TechLevel.Low || Rural(a) && (b == null || Rural(b))) ? TownRadius(RouteKind.Road) : LeastRadius(kind);

    /// <summary>The tightest curve a paved road of a class takes; tracks may switch back as tight as they like.</summary>
    public static double LeastRadius(RouteKind k) => k switch { RouteKind.Highway => 130, RouteKind.Road => 80, _ => 0 };

    /// <summary>
    /// The most a paved road of a class turns over any 50 m outside a town's grid, degrees: its curves sweep
    /// (a turn of this over 50 m is a radius of about 1.3 times the least). Tracks and town streets aren't held to it.
    /// </summary>
    public static double MostTurn50(RouteKind k) => k switch { RouteKind.Highway => 30, RouteKind.Road => 45, _ => 180 };

    /// <summary>
    /// A paved road that had to follow the land's line, eased until no bend is tighter than its least
    /// radius: each point drawn toward the middle of its neighbours where the curve through them is too
    /// tight, never onto blocked ground, the fixed ends left alone.
    /// </summary>
    static List<(double X, double Z)> Ease(Router router, List<(double X, double Z)> p, double least, int keepStart, int keepEnd, out bool tight)
    {
        var pts = p.ToList();
        tight = false;
        // First straighten what the land's line left wavering: a broad average over the run takes out the
        // small back-and-forth a road following the ground picks up, so it runs straight between its bends.
        for (int pass = 0; pass < 3; pass++)
        {
            int lo = Math.Max(4, keepStart + 2), hi = pts.Count - Math.Max(4, keepEnd + 2);
            if (hi - lo < 12) break;
            const int Broad = 12;
            var next = pts.ToList();
            for (int i = lo; i < hi; i++)
            {
                double x = 0, z = 0, sw = 0;
                for (int k = -2 * Broad; k <= 2 * Broad; k++)
                {
                    int j = Math.Clamp(i + k, lo - 1, hi);
                    double g = Math.Exp(-0.5 * k * k / (Broad * Broad * 0.25));
                    x += pts[j].X * g;
                    z += pts[j].Z * g;
                    sw += g;
                }
                x /= sw;
                z /= sw;
                if (!router.Blocked(x, z) || router.Blocked(pts[i].X, pts[i].Z)) next[i] = (x, z);
            }
            pts = Resample(next, Spacing);
        }
        // Then open every bend still tighter than the class allows.
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
            // The whole run is smoothed each pass (a road that follows the land runs straight between its bends,
            // without wobbles), the tight stretches most.
            for (int i = lo; i < hi; i++)
            {
                int a = Math.Max(lo - 1, i - window), b = Math.Min(hi, i + window);
                double x = 0, z = 0, sw = 0, sigma = window * (mark[i] ? 0.5 : 0.3);
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
    public InfraRoute Street(List<(double X, double Z)> points, RouteKind kind, (int Gate, float Y)? gate = null, double round = 0)
    {
        if (_router == null || points.Count < 2) return null;
        var pts = new List<(double X, double Z)>(points);
        Unwrap(pts);
        List<(double X, double Z)> path = Resample(Round(pts, round > 0 ? round : TownRadius(kind)), Spacing);
        var pins = new List<(int Index, float Y)>();
        int a = _router.RouteAtPoint(path[0].X, path[0].Z, kind), b = _router.RouteAtPoint(path[^1].X, path[^1].Z, kind);
        if (a >= 0 && Snap(Routes[a], path[0], out var p0, out float y0)) { path[0] = p0; ClearStart(path); pins.Add((0, y0)); }
        if (b >= 0 && gate == null && Snap(Routes[b], path[^1], out var p1, out float y1)) { path[^1] = p1; ClearEnd(path); pins.Add((path.Count - 1, y1)); }
        if (gate is { } g) pins.Add((path.Count - 1, g.Y));
        InfraRoute route = Build(_router, path, pins, kind, null, null, -1, -1, _rng);
        if (route != null) _router.Mark(route);
        return route;
    }

    /// <summary>
    /// No hard elbow anywhere: where a road turns more than a town corner's curve over 8 m (out of a gate,
    /// into a junction, round a cell corner), the points there are relaxed toward their neighbours until it
    /// doesn't. Its two ends stay where they are; the number of points doesn't change.
    /// </summary>
    static void Unkink(List<(double X, double Z)> p)
    {
        const double Most = 50 * Math.PI / 180;
        for (int pass = 0; pass < 200; pass++)
        {
            bool any = false;
            for (int i = 2; i + 2 < p.Count; i++)
            {
                double a0 = Math.Atan2(p[i].Z - p[i - 2].Z, p[i].X - p[i - 2].X), a1 = Math.Atan2(p[i + 2].Z - p[i].Z, p[i + 2].X - p[i].X);
                if (Math.Abs(Math.IEEERemainder(a1 - a0, Math.PI * 2)) <= Most) continue;
                any = true;
                for (int j = Math.Max(1, i - 2); j <= Math.Min(p.Count - 2, i + 2); j++)
                    p[j] = ((p[j - 1].X + p[j + 1].X) * 0.5 * 0.6 + p[j].X * 0.4, (p[j - 1].Z + p[j + 1].Z) * 0.5 * 0.6 + p[j].Z * 0.4);
            }
            if (!any) break;
        }
    }

    /// <summary>A town street's corners, centre-line radius by class: tighter than a country road's, never an elbow.</summary>
    public static double TownRadius(RouteKind k) => k switch { RouteKind.Highway => 12, RouteKind.Road => 10, _ => 8 };

    /// <summary>Every corner of a polyline rounded into an arc of the radius (shorter where the legs are short).</summary>
    static List<(double X, double Z)> Round(List<(double X, double Z)> c, double radius)
    {
        if (c.Count < 3 || radius <= 0) return c;
        var o = new List<(double X, double Z)> { c[0] };
        for (int i = 1; i + 1 < c.Count; i++)
        {
            double ax = c[i].X - c[i - 1].X, az = c[i].Z - c[i - 1].Z, bx = c[i + 1].X - c[i].X, bz = c[i + 1].Z - c[i].Z;
            double la = Math.Sqrt(ax * ax + az * az), lb = Math.Sqrt(bx * bx + bz * bz);
            if (la < 1e-6 || lb < 1e-6) continue;
            ax /= la; az /= la; bx /= lb; bz /= lb;
            double turn = Math.Abs(Math.Atan2(ax * bz - az * bx, ax * bx + az * bz));
            if (turn < 1e-3) { o.Add(c[i]); continue; }
            double t = Math.Min(radius * Math.Tan(turn / 2), Math.Min(la, lb) * 0.45), r = t / Math.Tan(turn / 2);
            double sign = Math.Sign(ax * bz - az * bx);
            var (sx, sz) = (c[i].X - ax * t, c[i].Z - az * t);
            double cx = sx - az * r * sign, cz = sz + ax * r * sign;
            int steps = Math.Max(3, (int)(r * turn / 1.5));
            for (int k = 0; k <= steps; k++)
            {
                double ang = turn * k / steps * sign, rx = sx - cx, rz = sz - cz;
                o.Add((cx + rx * Math.Cos(ang) - rz * Math.Sin(ang), cz + rx * Math.Sin(ang) + rz * Math.Cos(ang)));
            }
        }
        o.Add(c[^1]);
        return o;
    }

    /// <summary>Later routes go round this pad.</summary>
    public void KeepOff(TerrainStamp pad) => _router?.Block(pad, 12);

    /// <summary>
    /// The land's way between two points as a track would take it (round water and places, up the easy side of a
    /// hill, along the roads where they go its way), continuous across the seams from <paramref name="from"/>; null
    /// on a world with no network or where there's no way. For what follows the land without being a road (a spur of poles).
    /// </summary>
    public List<(double X, double Z)> LandWay((double X, double Z) from, (double X, double Z) to)
    {
        // (As a frontier world's would: never over open sea, where nothing stands.)
        List<int> cells = _router?.Find(from, to, RouteKind.Track, TechLevel.Low);
        if (cells == null) return null;
        var pts = new List<(double X, double Z)> { from };
        foreach (int c in cells.Skip(1).Take(Math.Max(0, cells.Count - 2))) pts.Add(_router.CellCentre(c));
        pts.Add(to);
        Unwrap(pts);
        return Resample(Smooth(pts, 1, 1), Spacing);
    }

    /// <summary>A spur from a point to the nearest route already laid (within a reach, leaving out some), found by the path engine. False if none.</summary>
    public bool Spur((double X, double Z) from, RouteKind kind, double reach, ICollection<InfraRoute> except = null)
    {
        if (_router == null) return false;
        InfraRoute near = Nearest(from.X, from.Z, reach, out int at, out _, except);
        if (near == null) return false;
        List<int> cells = _router.Find(from, (near.X[at], near.Z[at]), kind, Profile.Before);
        if (cells == null || cells.Count < 2) return false;
        var pts = new List<(double X, double Z)> { from };
        foreach (int c in cells.Skip(1)) pts.Add(_router.CellCentre(c));
        pts.Add((near.X[at], near.Z[at]));
        Unwrap(pts);
        List<(double X, double Z)> path = Resample(Smooth(pts, 1, 0), Spacing);
        // A paved spur (a city's way out to the network) is a street of its class: no corner tighter than a town's.
        if (kind is RouteKind.Road or RouteKind.Highway) path = Ease(_router, path, TownRadius(kind) * 1.3, 1, 1, out _);
        var pins = new List<(int Index, float Y)>();
        // It meets the way it joins square, as every way does: never sliding in at a glancing angle as a long wedge.
        if (Snap(near, path[^1], out var p1, out float y1, out var d1))
        {
            path[^1] = p1;
            if (Approach(path, near, false, out var pa, out var da)) { path[^1] = pa; (p1, d1) = (pa, da); }
            if (Snap(near, path[^1], out _, out float ya)) y1 = ya;
            Square(path, near, p1, d1, false, kind is RouteKind.Road or RouteKind.Highway ? TownRadius(kind) * 1.3 : 0);
            ClearEnd(path);
            pins.Add((path.Count - 1, y1));
        }
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
    /// A road joining another meets it square: its last stretch curves smoothly round to leave the other's
    /// edge along the other's normal, over a length that grows with the other's width, so a junction is a
    /// clean T and not a long blunt wedge where one road slides into the other at a glancing angle. The
    /// curve (a cubic from the road's own heading to the normal) turns one way only: no wobble.
    /// </summary>
    static void Square(List<(double X, double Z)> path, InfraRoute joined, (double X, double Z) at, (double X, double Z) dir, bool atStart, double least = 0)
    {
        if (path.Count < 6) return;
        if (atStart) path.Reverse();
        double reach = Math.Clamp(joined.HalfWidth + 12, 14, 26), cut = reach * 1.6;
        var inner = path[Math.Max(0, path.Count - 7)];
        double nx = -dir.Z, nz = dir.X;
        if ((inner.X - at.X) * nx + (inner.Z - at.Z) * nz < 0) { nx = -nx; nz = -nz; }
        // Keep the road up to where the curve begins, and its heading there.
        int k = path.Count - 1;
        int Back(double c)
        {
            int i = path.Count - 1;
            while (i > 1 && (path[i].X - at.X) * (path[i].X - at.X) + (path[i].Z - at.Z) * (path[i].Z - at.Z) < c * c) i--;
            return i;
        }
        k = Back(cut);
        // A paved road swings round no tighter than its class allows: the more it has to turn to come in square, the
        // further back the curve begins (within reason: past that, as far back as the road runs).
        if (least > 0 && k >= 1)
        {
            double hx = path[k].X - path[k - 1].X, hz = path[k].Z - path[k - 1].Z, hl = Math.Max(1e-6, Math.Sqrt(hx * hx + hz * hz));
            double turn = Math.Acos(Math.Clamp(-(hx * nx + hz * nz) / hl, -1, 1));
            k = Back(Math.Clamp(least * turn * 0.75, cut, 110));
        }
        if (k < 1) { if (atStart) path.Reverse(); return; }
        var p0 = path[k];
        double tx = p0.X - path[k - 1].X, tz = p0.Z - path[k - 1].Z, tl = Math.Max(1e-6, Math.Sqrt(tx * tx + tz * tz));
        tx /= tl; tz /= tl;
        double l = Math.Sqrt((at.X - p0.X) * (at.X - p0.X) + (at.Z - p0.Z) * (at.Z - p0.Z));
        var c1 = (p0.X + tx * l * 0.4, p0.Z + tz * l * 0.4);
        var c2 = (at.X + nx * l * 0.45, at.Z + nz * l * 0.45);
        var keep = path.Take(k + 1).ToList();
        int steps = Math.Max(4, (int)(l / 2));
        for (int s = 1; s <= steps; s++)
        {
            double t = s / (double)steps, u = 1 - t;
            keep.Add((u * u * u * p0.X + 3 * u * u * t * c1.Item1 + 3 * u * t * t * c2.Item1 + t * t * t * at.X,
                u * u * u * p0.Z + 3 * u * u * t * c1.Item2 + 3 * u * t * t * c2.Item2 + t * t * t * at.Z));
        }
        var even = Resample(keep, Spacing);
        if (atStart) even.Reverse();
        path.Clear();
        path.AddRange(even);
    }

    /// <summary>
    /// A way that came up alongside the one it joins (cheap ground beside a road draws a search along it) leaves
    /// that run out: it joins where it first came near, so it can turn in square from out in the open instead of
    /// hooking in off a long thin wedge of grass. Drops the end of the path that lies within the other's deck and a
    /// margin, and ends it on the other's line nearest where it came near. False where nothing was dropped.
    /// </summary>
    bool Approach(List<(double X, double Z)> path, InfraRoute joined, bool atStart, out (double X, double Z) at, out (double X, double Z) dir)
    {
        at = atStart ? path[0] : path[^1];
        dir = (1, 0);
        if (path.Count < 8) return false;
        double margin = joined.HalfWidth + 14;
        int n = path.Count;
        (double X, double Z) P(int i) => atStart ? path[i] : path[n - 1 - i];
        double Off(int i) => Snap(joined, P(i), out var q, out _) ? Math.Sqrt(Math.Pow(q.X - P(i).X, 2) + Math.Pow(q.Z - P(i).Z, 2)) : double.MaxValue;
        int k = 1;
        while (k < n - 6 && Off(k) < margin) k++;
        // Nothing ran alongside: the end already comes in from the open.
        if (k <= 3) return false;
        var keep = P(k);
        if (!Snap(joined, keep, out var foot, out _, out dir)) return false;
        // Not across ground the way may not cross (a place's pad, a ruin's lot, water): there it keeps the line it found.
        for (double t = 0.1; t < 1; t += 0.1)
            if (_router != null && _router.Blocked(keep.X + (foot.X - keep.X) * t, keep.Z + (foot.Z - keep.Z) * t)) { dir = (1, 0); return false; }
        if (atStart) path.RemoveRange(0, k); else path.RemoveRange(n - k, k);
        var link = Resample(atStart ? new List<(double X, double Z)> { foot, keep } : new List<(double X, double Z)> { keep, foot }, Spacing);
        if (atStart) { path.RemoveAt(0); path.InsertRange(0, link); }
        else { path.RemoveAt(path.Count - 1); path.AddRange(link); }
        at = foot;
        return true;
    }

    /// <summary>
    /// A paved road that goes on along the road it meets merges into it the way a slip road does: its last
    /// stretch sweeps round, at no less than its class's radius, to run alongside and join it tangentially a
    /// little further along the way it is heading, so from the air the two read as one road curving, never
    /// an elbow. <paramref name="heading"/> is the way along the joined road the link goes on (at the start,
    /// the way it came).
    /// </summary>
    void Merge(List<(double X, double Z)> path, InfraRoute joined, (double X, double Z) heading, double least, bool atStart)
    {
        if (path.Count < 6) return;
        if (atStart) { path.Reverse(); heading = (-heading.X, -heading.Z); }
        var end = path[^1];
        // The merge lies on along the joined road the way the link goes (behind it at the start).
        var target = (end.X + heading.X * least * 0.8, end.Z + heading.Z * least * 0.8);
        if (!Snap(joined, target, out var m, out _, out var dm)) { if (atStart) path.Reverse(); return; }
        double sg = Math.Sign(dm.X * heading.X + dm.Z * heading.Z);
        var arrive = (dm.X * sg, dm.Z * sg);
        double cut = least * 1.6;
        int k = path.Count - 1;
        while (k > 1 && (path[k].X - m.X) * (path[k].X - m.X) + (path[k].Z - m.Z) * (path[k].Z - m.Z) < cut * cut) k--;
        if (k < 1) { if (atStart) path.Reverse(); return; }
        var p0 = path[k];
        double tx = p0.X - path[k - 1].X, tz = p0.Z - path[k - 1].Z, tl = Math.Max(1e-6, Math.Sqrt(tx * tx + tz * tz));
        tx /= tl; tz /= tl;
        double l = Math.Sqrt((m.X - p0.X) * (m.X - p0.X) + (m.Z - p0.Z) * (m.Z - p0.Z));
        var c1 = (p0.X + tx * l * 0.4, p0.Z + tz * l * 0.4);
        var c2 = (m.X - arrive.Item1 * l * 0.45, m.Z - arrive.Item2 * l * 0.45);
        var keep = path.Take(k + 1).ToList();
        int steps = Math.Max(6, (int)(l / 2));
        for (int i = 1; i <= steps; i++)
        {
            double t = i / (double)steps, u = 1 - t;
            keep.Add((u * u * u * p0.X + 3 * u * u * t * c1.Item1 + 3 * u * t * t * c2.Item1 + t * t * t * m.X,
                u * u * u * p0.Z + 3 * u * u * t * c1.Item2 + 3 * u * t * t * c2.Item2 + t * t * t * m.Z));
        }
        var even = Resample(keep, Spacing);
        if (atStart) even.Reverse();
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
        if (kind != RouteKind.Rail) Unkink(path);
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
        // A bridge reaches on until its deck stands an embankment's height over the ground (an abutment, the bank built
        // up to it: never a deck lying low over the land, nor one that falls onto it); a tunnel until its mouth is about
        // a bore high in the slope (a portal face).
        Extend(spans, RoadSpan.Bridge, i => deck[i] - ground[i] > EmbankHeight && !water[i] || water[i]);
        Extend(spans, RoadSpan.Tunnel, i => ground[i] - deck[i] > 6.5f);
        // Past what its builders could span or bore, a route follows the land instead: down into the
        // valley and up the far side, over the ridge, steep where it must be (its deck eases onto the
        // ground over a few stretches at each end of the run).
        if (!maglev && kind != RouteKind.Rail) // a train can't follow the land: a line that long in the air is dropped
        {
            FollowLand(spans, deck, ground, water, fixedAt, RoadSpan.Bridge, MaxViaduct(kind, Profile.Before));
            FollowLand(spans, deck, ground, water, fixedAt, RoadSpan.Tunnel, MaxTunnel(kind, Profile.Before));
            Gaps(spans, deck, ground, water, fixedAt, kind);
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
        // Never paved: every road on a world that paved none, and a haul road out to what only it served.
        bool graded = kind == RouteKind.Road && (Profile.Before == TechLevel.Low || Rural(a) && (b == null || Rural(b)));
        var route = new InfraRoute
        {
            Id = Routes.Count, Kind = kind, Tech = Profile.Before, HalfWidth = HalfWidthOf(kind), Surface = SurfaceOf(kind, Profile.Before),
            Kept = kept, Wear = kept ? 0.18f : 0.75f + rng.Float() * 0.2f, Maglev = maglev,
            X = path.Select(p => p.X).ToArray(), Z = path.Select(p => p.Z).ToArray(), Y = deck, Span = spans,
            FromGate = gateA, ToGate = gateB, Laid = _laid, Graded = graded,
        };
        if (graded)
        {
            route.HalfWidth = GradedHalfWidth;
            route.Surface = StampSurface.Dirt;
        }
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
                // (Only a span standing high: one a few metres up would only have settled onto the land, slabs on the ground.)
                int spanLen = 5;   // stretches between piers (20 m)
                for (int k = i + spanLen; k + spanLen < j; k += spanLen)
                    if (rng.Chance(0.12f + 0.25f * route.Wear * (route.Tech == TechLevel.Low ? 1.5f : 1f))
                        && Enumerable.Range(k, spanLen + 1).All(m => water[m] || deck[m] - ground[m] >= FallHeight))
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

    /// <summary>
    /// A bridge stands only where a way crosses a gap (water, a gully, a valley): a whole span from an abutment seated on one
    /// bank to one seated on the other, the way going on along the ground past both. A dry run whose ground only falls away
    /// along it (down a hillside to a valley floor) spans nothing, and one standing off the ground at an end has no bank to
    /// stand on: there the way follows the land instead. Nor does a bridge end at the route's own end (a junction, a gate),
    /// never a dead end: over dry ground the way follows the land, over water it crosses on a causeway at the bank's height.
    /// </summary>
    static void Gaps(RoadSpan[] spans, float[] deck, float[] ground, bool[] water, bool[] pinned, RouteKind kind)
    {
        for (int i = 0; i < spans.Length;)
        {
            if (spans[i] != RoadSpan.Bridge) { i++; continue; }
            int j = i;
            bool wet = false;
            while (j < spans.Length && spans[j] == RoadSpan.Bridge) { wet |= water[j] || water[j + 1]; j++; }
            // Its abutments stand at points i and j.
            bool atEnd = i < 2 || j > spans.Length - 2, whole;
            if (wet) whole = !atEnd;
            else
            {
                float low = float.MaxValue;
                for (int k = i + 1; k < j; k++) low = Math.Min(low, ground[k]);
                float banks = Math.Min(ground[i], ground[j]);
                bool seated = deck[i] - ground[i] <= EmbankHeight + 1.5f && deck[j] - ground[j] <= EmbankHeight + 1.5f;
                whole = !atEnd && seated && j - i >= 3 && banks - low >= BridgeFill(kind) * 0.75f;
            }
            if (!whole)
            {
                const int Ease = 5;
                for (int k = Math.Max(0, i - Ease); k <= Math.Min(deck.Length - 1, j + Ease); k++)
                {
                    if (pinned[k]) continue;
                    int inside = Math.Min(k - (i - Ease), (j + Ease) - k);
                    float w = Math.Clamp(inside / (float)Ease, 0f, 1f);
                    deck[k] += (ground[k] - deck[k]) * w * w * (3 - 2 * w);
                }
                for (int k = i; k < j; k++) spans[k] = RoadSpan.Ground;
            }
            i = j;
        }
    }

    /// <summary>Bridges a route's builders put up (tests): every one crosses a gap from bank to bank.</summary>
    public static bool WholeBridge(InfraStructure s, Func<double, double, float> bare, out string why)
    {
        InfraRoute r = s.Route;
        why = null;
        if (s.Kind != RoadSpan.Bridge || r.Maglev) return true;
        if (s.First < 2 || s.Last > r.Count - 3) { why = "ends at its way's end"; return false; }
        if (s.OverWater) return true;
        float gA = bare(r.X[s.First], r.Z[s.First]), gB = bare(r.X[s.Last], r.Z[s.Last]), low = float.MaxValue;
        for (int k = s.First + 1; k < s.Last; k++) low = Math.Min(low, bare(r.X[k], r.Z[k]));
        if (s.Last - s.First < 3) { why = "a fragment"; return false; }
        if (Math.Min(gA, gB) - low < BridgeFill(r.Kind) * 0.6f) { why = $"spans nothing (its banks {Math.Min(gA, gB) - low:F1} m over the ground under it)"; return false; }
        if (r.Y[s.First] - gA > EmbankHeight + 2.5f || r.Y[s.Last] - gB > EmbankHeight + 2.5f) { why = $"an end standing off its bank ({r.Y[s.First] - gA:F1} and {r.Y[s.Last] - gB:F1} m up)"; return false; }
        return true;
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
    /// <summary>The streets' lamps, verge trees, signal poles and sign posts (<see cref="StreetFurniture.Plan"/>).</summary>
    public List<StreetItem> Furniture = new();

    /// <summary>Every place two or more ways meet, and the form each was built in (<see cref="PlanJunctions"/>).</summary>
    public readonly List<InfraJunction> Joins = new();

    /// <summary>Proof frames and tests: build a roundabout wherever one could go.</summary>
    public static bool ForceRoundabouts;

    /// <summary>The least angle a branch leaves the way it joins at, degrees: sharper leaves a knife edge of grass between the decks.</summary>
    public const double LeastJoinAngle = 40;

    /// <summary>
    /// A slip road built as one: a paved way leaving a highway (or joining one) at a shallow angle, its lane
    /// tapering off with a gore at the nose (widened, rounded, hatched with chevrons). Any other shallow join is
    /// bent to meet square instead.
    /// </summary>
    public static bool GoreSlip(InfraJunction j) => j.Form is JunctionForm.Slip or JunctionForm.Tee or JunctionForm.Crossroads && j.Through.Kind == RouteKind.Highway && !j.Through.Soil
        && j.Arms.All(a => !a.Route.Soil);

    /// <summary>Every rounded kerb corner: at the junctions, where streets cross, round the rings and the turning bulbs (<see cref="RoadCorners"/>).</summary>
    public List<RoadCorner> Corners = new();
    /// <summary>Corners that couldn't be laid, and why, and where (tests).</summary>
    public readonly Dictionary<string, int> CornerMisses = new();
    public readonly List<(double X, double Z, string Why)> CornerMissedAt = new();

    /// <summary>Plans the rounded corners once every way and junction is laid.</summary>
    public void PlanCorners(Func<double, double, bool> onPlace = null)
    {
        CornerMisses.Clear();
        CornerMissedAt.Clear();
        Corners = RoadCorners.Plan(this, CornerMisses, onPlace, CornerMissedAt);
        // Each corner lies on the plane of the decks it joins (where the ways climb, its kerb climbs with them): through the
        // decks at its two tangent points and at the corner of its two kerb lines. (A bulb's lie on the bulb's own.)
        foreach (RoadCorner c in Corners)
        {
            if (c.Disc || c.TiltX != 0f || c.TiltZ != 0f || c.RouteA < 0 || c.RouteB < 0 || c.RouteA >= Routes.Count || c.RouteB >= Routes.Count) continue;
            double bx = c.AX + c.BX, bz = c.AZ + c.BZ, bl = Math.Sqrt(bx * bx + bz * bz), cosHalf = bl * 0.5;
            if (bl < 0.3 || cosHalf < 0.25) continue;
            (double X, double Z) ta = (c.X + c.AX * c.R, c.Z + c.AZ * c.R), tb = (c.X + c.BX * c.R, c.Z + c.BZ * c.R);
            (double X, double Z) k = (c.X + bx / bl * c.R / cosHalf, c.Z + bz / bl * c.R / cosHalf);
            InfraRoute ra = Routes[c.RouteA], rb = Routes[c.RouteB];
            if (!Snap(ra, ta, out _, out float ya) || !Snap(rb, tb, out _, out float yb) || !Snap(ra, k, out _, out float yk)) continue;
            // Y0 + gx dx + gz dz through the three, (dx, dz) from the corner's centre: Cramer's rule.
            double x1 = ta.X - c.X, z1 = ta.Z - c.Z, x2 = tb.X - c.X, z2 = tb.Z - c.Z, x3 = k.X - c.X, z3 = k.Z - c.Z;
            double d = (x2 * z3 - x3 * z2) - (x1 * z3 - x3 * z1) + (x1 * z2 - x2 * z1);
            if (Math.Abs(d) < 1e-3) continue;
            double dy = ya * (x2 * z3 - x3 * z2) - yb * (x1 * z3 - x3 * z1) + yk * (x1 * z2 - x2 * z1);
            double dgx = (yb * z3 - yk * z2) - (ya * z3 - yk * z1) + (ya * z2 - yb * z1);
            double dgz = (x2 * yk - x3 * yb) - (x1 * yk - x3 * ya) + (x1 * yb - x2 * ya);
            double gx = dgx / d, gz = dgz / d;
            // (Never steeper than the steepest way: a bad fit keeps the level it had.)
            if (Math.Abs(gx) > 0.16 || Math.Abs(gz) > 0.16) continue;
            c.Y = (float)(dy / d);
            c.TiltX = (float)gx;
            c.TiltZ = (float)gz;
        }
        RoadCorners.Settle(Corners, _w, _h);
    }

    /// <summary>Why junctions that might have been roundabouts weren't (tests and proof frames).</summary>
    public readonly Dictionary<string, int> RoundaboutMisses = new();

    /// <summary>
    /// The junctions, once every road, street and lane is laid: each end that joins another way, grouped where
    /// they meet, and given the form the roads that meet ask for (design doc "Infrastructure"). A way running
    /// on into another is one road; a branch leaving at a shallow angle leaves by a slip road, its lane tapering
    /// off with a chevron gore in the fork (a low-tech world's is a plain fork); three ways make a T and four a
    /// crossroads; and on a world that built them, where roads alike meet on level open ground out of town, a
    /// roundabout: a ring round a kerbed island the ways come into. Low-tech worlds build only the simple forms.
    /// </summary>
    public void PlanJunctions(Func<double, double, float> ground, Func<double, double, double, bool> clear, PoiGenerator.Gen.Rng rng)
    {
        Joins.Clear();
        if (Profile == null) return;
        bool low = Profile.Before == TechLevel.Low;
        bool roundabouts = !low && (ForceRoundabouts || rng.Chance(0.55f));
        var roads = Routes.Where(r => r.Kind != RouteKind.Rail && r.Count >= 4).ToList();
        foreach (InfraRoute r in roads)
            foreach (bool atStart in new[] { true, false })
            {
                // An end at a place's gate is no junction (one that runs on out of a town's main street is: the way meets it there).
                bool placeEnd = atStart ? r.From != null : r.To != null;
                if (placeEnd && (atStart ? r.FromGate : r.ToGate) >= 0) continue;
                int e = atStart ? 0 : r.Count - 1;
                double x = r.X[e], z = r.Z[e];
                // The way it joins: of the ways whose deck it reaches, one that runs on through the junction rather than
                // ending there too, then the greatest class, then the nearest (another branch ending at the same spot is
                // no through way: measured against it, a square T read as a knife edge).
                var reached = new List<(InfraRoute R, int I, double D)>();
                var skip = new List<InfraRoute> { r };
                InfraRoute first = null;
                for (int c = 0; c < 5; c++)
                {
                    InfraRoute q = Nearest(Mathx.WrapX(x, _w), Mathx.Wrap(z, _h), 16, out int qi, out double qd, skip.ToArray());
                    if (q == null) break;
                    first ??= q;
                    skip.Add(q);
                    if (q.Kind != RouteKind.Rail && qd <= q.HalfWidth + 1.5) reached.Add((q, qi, qd));
                }
                if (reached.Count == 0)
                {
                    // A town street that just stops ends in a turning bulb (not a junction: no other way meets it there).
                    if (first == null && !placeEnd && r.Kind == RouteKind.Road && r.Laid.Length == 0 && !r.Ring && !low)
                        if (atStart) r.StartJoin = JunctionForm.DeadEnd; else r.EndJoin = JunctionForm.DeadEnd;
                    continue;
                }
                static int Rank(InfraRoute q) => q.Kind == RouteKind.Highway ? 3 : q.Kind == RouteKind.Road && !q.Soil ? 2 : q.Kind == RouteKind.Road ? 1 : 0;
                var (o, idx, _) = reached.OrderByDescending(c => c.I >= 3 && c.I + 3 < c.R.Count ? 1 : 0).ThenByDescending(c => Rank(c.R)).ThenBy(c => c.D).First();
                InfraJunction j = Joins.FirstOrDefault(q => Dist((q.X, q.Z), (x, z)) < 18 && q.Arms.Any(a => reached.Any(c => c.R == a.Route)));
                if (j == null)
                {
                    j = new InfraJunction { X = Mathx.WrapX(o.X[idx], _w), Z = Mathx.Wrap(o.Z[idx], _h), Y = o.Y[idx], Through = o };
                    // The way it joins runs on either side of it, or ends there too.
                    if (idx >= 3) j.Arms.Add(new JunctionArm(o, false, Away(o, idx, -1)));
                    if (idx + 3 < o.Count) j.Arms.Add(new JunctionArm(o, true, Away(o, idx, 1)));
                    Joins.Add(j);
                }
                if (j.Arms.Any(a => a.Route == r && a.Forward == atStart)) continue;
                j.Arms.Add(new JunctionArm(r, atStart, Away(r, e, atStart ? 1 : -1)));
            }
        foreach (InfraJunction j in Joins)
        {
            var through = j.Arms.Where(a => a.Route == j.Through).ToList();
            var branches = j.Arms.Where(a => a.Route != j.Through).ToList();
            // How sharply each branch leaves the way it joins: a slip road leaves within 35 degrees of it.
            bool shallow = branches.Count > 0 && branches.All(b => through.Any(t => Math.Acos(Math.Clamp(b.DirX * t.DirX + b.DirZ * t.DirZ, -1, 1)) < 35 * Math.PI / 180));
            if (j.Arms.Count <= 2) { j.Form = JunctionForm.RunOn; j.Why = "a way running on into another"; }
            else if (shallow) { j.Form = low ? JunctionForm.Fork : JunctionForm.Slip; j.Why = low ? "a branch forking off" : "a branch leaving at a shallow angle"; }
            else if (j.Arms.Count == 3) { j.Form = JunctionForm.Tee; j.Why = "three ways"; }
            else { j.Form = JunctionForm.Crossroads; j.Why = $"{j.Arms.Count} ways"; }
            if (!roundabouts || j.Form is not (JunctionForm.Tee or JunctionForm.Crossroads)) continue;
            void Miss(string why) => RoundaboutMisses[why] = RoundaboutMisses.GetValueOrDefault(why) + 1;
            // A roundabout: paved roads alike out of town (no track or lane, no dirt road, no town's grid street), level open
            // ground, room for the ring.
            bool hwArm = j.Arms.Any(a => a.Route.Kind == RouteKind.Highway);
            // (A highway meets others round a big ring on a world that built them; interchanges are for later.)
            if (j.Arms.Any(a => a.Route.Kind is not (RouteKind.Road or RouteKind.Highway) || a.Route.Soil || a.Route.Ring || a.Route.HalfWidth < 3.5f || a.Route.Laid.Length == 0)) { Miss("not roads alike: " + string.Join("+", j.Arms.Select(a => a.Route.Graded ? "Graded" : a.Route.Laid.Length == 0 ? $"{a.Route.Kind}-street" : a.Route.Kind.ToString()).OrderBy(s => s))); continue; }
            // Only where enough of the better roads meet for good: each way out runs on paved a fair way, to a place or to more
            // paved road (never a short paved stub with tracks beyond it), and only on a highway or in or near a town.
            if (j.Arms.Any(a => !PavedOn(j, a))) { Miss("a short paved stub"); continue; }
            if (!hwArm && !NearSettlement(j.X, j.Z, 700)) { Miss("out of town"); continue; }
            double radius = hwArm ? 19 : RingRadius;
            bool level = true;
            for (int k = 0; k < 16 && level; k++)
            {
                double a = k * Math.Tau / 16, px = j.X + Math.Cos(a) * (radius + 6), pz = j.Z + Math.Sin(a) * (radius + 6);
                level = Math.Abs(ground(px, pz) - j.Y) < 3f;
            }
            if (!level) { Miss("not level"); continue; }
            if (!clear(j.X, j.Z, radius + 10)) { Miss("no room"); continue; }
            if (!(ForceRoundabouts || rng.Chance(0.75f))) { Miss("chance"); continue; }
            if (Joins.Any(q => q != j && q.Form == JunctionForm.Roundabout && Dist((q.X, q.Z), (j.X, j.Z)) < 200)) { Miss("another near"); continue; }
            // A ring is a circle round one junction: another junction on one of its ways close enough to run into it
            // keeps this one a plain junction (two joined in one ring drew a stretched pill).
            if (Joins.Any(q => q != j && q.Form != JunctionForm.RunOn && q.Arms.Any(a => j.Arms.Any(b => b.Route == a.Route)) && Dist((q.X, q.Z), (j.X, j.Z)) < radius + (hwArm ? 6 : 4) + 18)) { Miss("a junction near"); continue; }
            j.Form = JunctionForm.Roundabout;
            j.Why = "roads alike meeting on level open ground";
            j.RingR = radius;
            j.RingHw = hwArm ? 6f : 4f;
            j.Ring = Ring(j);
        }
        foreach (InfraJunction j in Joins)
            foreach (JunctionArm a in j.Arms.Where(a => a.Route != j.Through || j.Form == JunctionForm.Roundabout))
            {
                if (a.Route == j.Through) continue;
                if (a.Forward) a.Route.StartJoin = j.Form; else a.Route.EndJoin = j.Form;
            }
        // A paved way much wider than the one it meets (a highway coming to a town's street) narrows to it over a lane drop's
        // length before the junction, so the two meet at one width and their corners fit: never a wide deck poking into a
        // narrow street's junction as a wedge of another surface.
        foreach (InfraJunction j in Joins.Where(j => j.Form is JunctionForm.Tee or JunctionForm.Crossroads or JunctionForm.Slip or JunctionForm.Fork))
            foreach (JunctionArm a in j.Arms.Where(a => a.Route != j.Through))
            {
                InfraRoute b = a.Route;
                if (b.Soil || j.Through.Soil || b.HalfWidth <= j.Through.HalfWidth + 0.75f) continue;
                // (Its painted width eases to the mean of its own and this, which is the one it meets.)
                float meet = Math.Max(0.6f, 2 * j.Through.HalfWidth - b.HalfWidth);
                if (a.Forward) { b.StartMeetHw = meet; b.StartMeetSoil = false; }
                else { b.EndMeetHw = meet; b.EndMeetSoil = false; }
            }
        // Where a way runs on into one of another class, each tapers to meet the other: never a hard edge between them.
        foreach (InfraJunction j in Joins.Where(j => j.Form == JunctionForm.RunOn && j.Arms.Count == 2 && j.Arms[0].Route != j.Arms[1].Route))
        {
            JunctionArm a = j.Arms[0], b = j.Arms[1];
            if (!ClassChange(a.Route, b.Route)) continue;
            foreach (var (me, other) in new[] { (a, b), (b, a) })
                if (me.Forward) { me.Route.StartMeetHw = other.Route.HalfWidth; me.Route.StartMeetSoil = other.Route.Soil; }
                else { me.Route.EndMeetHw = other.Route.HalfWidth; me.Route.EndMeetSoil = other.Route.Soil; }
        }
    }

    /// <summary>The least a way out of a roundabout runs on paved before it may end at anything but a place or more paved road, m.</summary>
    public const double LeastArm = 150;

    /// <summary>Whether a way out of a junction runs on paved: a fair way, or to a place's gate, or into more paved road.</summary>
    public bool PavedOn(InfraJunction j, JunctionArm a)
    {
        InfraRoute r = a.Route;
        if (r.Soil || r.Count < 2) return false;
        int near = 0;
        double best = double.MaxValue;
        for (int i = 0; i < r.Count; i++)
        {
            double d = Dist((r.X[i], r.Z[i]), (j.X, j.Z));
            if (d < best) { best = d; near = i; }
        }
        double run = a.Forward ? (r.Count - 1 - near) * Spacing : near * Spacing;
        if (run >= LeastArm) return true;
        bool toEnd = a.Forward;
        if ((toEnd ? r.To : r.From) != null) return true;
        return EndJoins(r, !toEnd).Any(q => !q.Soil);
    }

    /// <summary>Whether a point lies in or near a town: a town, city or settlement's place, the start town or the city, within a reach of its edge.</summary>
    public bool NearSettlement(double x, double z, double reach)
    {
        foreach (PoiSite p in Places)
            if ((p.Tier is PoiTier.Town or PoiTier.City or PoiTier.Spaceport || p.Family.Tags.Contains("settlement"))
                && Dist(p.Center, (x, z)) < reach + Math.Max(p.Stamp.ReachX, p.Stamp.ReachZ)) return true;
        if (Town != null && Dist((Town.CenterX, Town.CenterZ), (x, z)) < reach + Town.MainHalf) return true;
        if (City != null && Dist((City.CenterX, City.CenterZ), (x, z)) < reach + City.Radius) return true;
        return false;
    }

    /// <summary>Whether two ways that meet are of different classes: kind, graded or not, or their widths.</summary>
    public static bool ClassChange(InfraRoute a, InfraRoute b) => a.Kind != b.Kind || a.Graded != b.Graded || a.Soil != b.Soil || Math.Abs(a.HalfWidth - b.HalfWidth) > 0.3f;

    /// <summary>
    /// How long a seam's taper runs, m: a few tens of metres within the dirt family, a lane drop's length between
    /// paved ways, shorter where dirt meets a paved road; and never shorter than sixteen times the difference in their widths
    /// (a lane drop's gentle slant).
    /// </summary>
    public static double SeamTaper(bool soil, bool otherSoil, float hw, float otherHw) => Math.Max(soil && otherSoil ? 30 : !soil && !otherSoil ? 60 : 20, 16 * Math.Abs(hw - otherHw));

    /// <summary>
    /// A way's painted half width <paramref name="along"/> metres from a seam where it runs on into a way of another
    /// class: the two meet at the mean of their widths and each eases back to its own over the taper (smoothly, no step).
    /// </summary>
    public static float SeamWidth(float own, float other, double along, double taper)
    {
        double t = Math.Clamp(along / taper, 0, 1);
        t = t * t * (3 - 2 * t);
        return (float)((own + other) * 0.5 + (own - (own + other) * 0.5) * t);
    }

    /// <summary>
    /// A highway's median: its carriageways divided by a strip of grass (most mid-tech worlds) or a concrete
    /// barrier (most high-tech ones), a grass one grown over where nobody has kept the road for a century.
    /// </summary>
    public int MedianOf(InfraRoute r)
    {
        if (r.Kind != RouteKind.Highway || Profile == null || Profile.Before == TechLevel.Low) return 0;
        float roll = (float)((PoiGenerator.Gen.Hash.Combine((ulong)r.Id, 0x3ED1A0UL) >> 11) / (double)(1UL << 53));
        bool barrier = roll < (Profile.Before == TechLevel.High ? 0.6f : 0.3f);
        return barrier ? 2 : !r.Kept && r.Wear > 0.8f ? 3 : 1;
    }

    /// <summary>
    /// A town street with kerbs, a grass verge and footpaths (the shader's FOOT_IN to FOOT_OUT past the kerb): a paved
    /// street of a town or city, not a lane, its bed laid level out past the footpath.
    /// </summary>
    public static bool Kerbed(InfraRoute r) => r.Laid.Length == 0 && r.Kind == RouteKind.Road && !r.Ring && r.HalfWidth >= 3.5f && !r.Soil;

    /// <summary>How far past a kerbed street's kerb its footpath ends, m (the shader's FOOT_OUT).</summary>
    public const float FootpathOut = 3.3f;

    /// <summary>A roundabout's ring: the radius of its centre line, m (its island's edge lies a road's half width inside).</summary>
    public const double RingRadius = 11.5;

    /// <summary>The way an arm leaves a junction from point <paramref name="i"/> of its route, a step at a time along it (unit, x and z).</summary>
    static (double X, double Z) Away(InfraRoute r, int i, int step)
    {
        int k = Math.Clamp(i + step * 3, 0, r.Count - 1);
        double dx = r.X[k] - r.X[i], dz = r.Z[k] - r.Z[i], l = Math.Max(1e-6, Math.Sqrt(dx * dx + dz * dz));
        return (dx / l, dz / l);
    }

    /// <summary>A roundabout's ring: a closed street round the junction at its deck's height, its island on its left (the shader's inside).</summary>
    InfraRoute Ring(InfraJunction j)
    {
        int n = (int)Math.Ceiling(Math.Tau * j.RingR / 3.0);
        var xs = new double[n + 1];
        var zs = new double[n + 1];
        for (int k = 0; k <= n; k++)
        {
            // Turning so the middle lies to the left of the way it runs: (dir x to) > 0 in the shader's sense.
            double a = -k * Math.Tau / n;
            xs[k] = j.X + Math.Cos(a) * j.RingR;
            zs[k] = j.Z + Math.Sin(a) * j.RingR;
        }
        double tx = xs[1] - xs[0], tz = zs[1] - zs[0], cx = j.X - xs[0], cz = j.Z - zs[0];
        if (tx * cz - tz * cx < 0) { Array.Reverse(xs); Array.Reverse(zs); }
        var ring = new InfraRoute
        {
            Id = Routes.Count, Kind = RouteKind.Road, Tech = Profile.Before, HalfWidth = j.RingHw, Surface = j.Through.Surface,
            Kept = j.Through.Kept, Wear = j.Through.Wear, X = xs, Z = zs, Y = Enumerable.Repeat(j.Y, n + 1).ToArray(),
            Span = Enumerable.Repeat(RoadSpan.Ground, n).ToArray(), Laid = "", Ring = true,
        };
        for (int i = 0; i < n; i++) ring.Length += Math.Sqrt(Math.Pow(xs[i + 1] - xs[i], 2) + Math.Pow(zs[i + 1] - zs[i], 2));
        Routes.Add(ring);
        return ring;
    }

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
                // (A stretch all bridge is stamped too, so the land under it is cut clear of its deck; not a maglev's
                // guideway, which rides high over the deck its route keeps along the ground.)
                if (!ground && r.Maglev) continue;
                list.Add(TerrainStamp.Road($"{(r.Ring ? "Ring" : r.Graded ? "Graded" : r.Kind.ToString())} {r.Id}{(r.Laid.Length == 0 ? " street" : "")}", new ArraySegment<double>(r.X, s, e - s + 1), new ArraySegment<double>(r.Z, s, e - s + 1),
                    new ArraySegment<float>(r.Y, s, e - s + 1), spans, r.HalfWidth, r.Surface, r.Wear, _w, _h,
                    boreHalfWidth: r.HalfWidth + 1.2f, boreHeight: r.Kind == RouteKind.Rail ? 7f : 6.5f,
                    buckle: r.Kept || r.Kind == RouteKind.Rail || r.Surface is StampSurface.Dirt or StampSurface.Gravel ? 0f : 0.22f * r.Wear,
                    mouth: new ArraySegment<float>(mouth, s, e - s + 1), wallSide: r.WallSide));
                list[^1].JoinStart = (int)r.StartJoin;
                list[^1].JoinEnd = (int)r.EndJoin;
                list[^1].Median = MedianOf(r);
                list[^1].Verge = Kerbed(r) ? FootpathOut + 0.4f : 0f;
                // A street that stops turns in a bulb: its bed a level disc round its end, out past the bulb's footpath.
                float cap = RoadCorners.BulbRadius(r) + (Kerbed(r) ? FootpathOut + 0.4f : 1.6f);
                if (s == 0 && r.StartJoin == JunctionForm.DeadEnd) { list[^1].CapStart = cap; list[^1].CapStartTilt = r.StartTilt; }
                if (e == r.Count - 1 && r.EndJoin == JunctionForm.DeadEnd) { list[^1].CapEnd = cap; list[^1].CapEndTilt = r.EndTilt; }
                list[^1].MeetStartHw = r.StartMeetHw;
                list[^1].MeetEndHw = r.EndMeetHw;
                list[^1].MeetStartTaper = (float)SeamTaper(r.Soil, r.StartMeetSoil, r.HalfWidth, r.StartMeetHw);
                list[^1].MeetEndTaper = (float)SeamTaper(r.Soil, r.EndMeetSoil, r.HalfWidth, r.EndMeetHw);
            }
        }
        // Each rounded corner's own level bed round its arc, at the deck's height, out past a street's footpath: the corner
        // is laid on the ground it rounds off, never on a bank (RoadField draws no road for it; it only shapes the ground).
        for (int k = 0; k < Corners.Count; k++)
        {
            RoadCorner c = Corners[k];
            if (c.Disc) continue;
            double a0 = Math.Atan2(c.AZ, c.AX), cab = c.AX * c.BZ - c.AZ * c.BX;
            double sweep = Math.Acos(Math.Clamp(c.AX * c.BX + c.AZ * c.BZ, -1, 1)) * Math.Sign(cab);
            int n = Math.Max(3, (int)Math.Ceiling(Math.Abs(sweep) * c.R / 1.5));
            var xs = new double[n + 1];
            var zs = new double[n + 1];
            for (int i = 0; i <= n; i++)
            {
                double ang = a0 + sweep * i / n;
                xs[i] = c.X + Math.Cos(ang) * c.R;
                zs[i] = c.Z + Math.Sin(ang) * c.R;
            }
            var bed = TerrainStamp.Road($"Corner {k}", xs, zs, Enumerable.Range(0, n + 1).Select(i => c.DeckAt(xs[i], zs[i], _w, _h)).ToArray(), Enumerable.Repeat(RoadSpan.Ground, n).ToArray(),
                0.5f, StampSurface.Natural, 0f, _w, _h);
            bed.Verge = Math.Min(c.R * 0.6f, c.Kerbed ? FootpathOut + 0.6f : 1.8f);
            list.Add(bed);
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

    /// <summary>
    /// Whether a spot stands off every deck as drawn: the roads' own (<see cref="OffRoad"/>), a junction's rounded corner
    /// and a street's turning bulb, by a margin: for what stands on the ground (a lamp, a sign, a signal pole).
    /// </summary>
    public bool OffDeck(double x, double z, double margin)
    {
        if (!OffRoad(x, z, margin)) return false;
        double cx = Mathx.WrapX(x, _w), cz = Mathx.Wrap(z, _h);
        foreach (RoadCorner c in Corners)
        {
            if (Math.Abs(Mathx.WrapDelta(c.X - cx, _w)) > c.Cut || Math.Abs(Mathx.WrapDelta(c.Z - cz, _h)) > c.Cut) continue;
            if (c.Edge(cx, cz, _w, _h) < margin) return false;
        }
        foreach (InfraRoute r in Routes)
            foreach (bool start in new[] { true, false })
            {
                if ((start ? r.StartJoin : r.EndJoin) != JunctionForm.DeadEnd) continue;
                int e = start ? 0 : r.Count - 1;
                if (Dist((r.X[e], r.Z[e]), (cx, cz)) < RoadCorners.BulbRadius(r) + margin) return false;
            }
        // A drive up to a roadside ruin is a deck too.
        foreach (FlavourRuin ruin in Ruins)
            if (ruin.Drive is { } d && d.NearestOnPath(cx, cz, out float dd, out _, out _) && dd < d.HalfWidth + margin) return false;
        return true;
    }

    /// <summary>
    /// Whether a spot stands off every deck (<see cref="OffDeck"/>) and off every kerbed street's whole level bed, its kerb,
    /// verge and footpath, round its corners and turning bulbs too, by a margin: where a pole may stand (never on a road,
    /// a bulb or a footpath).
    /// </summary>
    public bool OffBed(double x, double z, double margin)
    {
        if (!OffDeck(x, z, margin)) return false;
        double cx = Mathx.WrapX(x, _w), cz = Mathx.Wrap(z, _h);
        foreach (InfraRoute r in Routes)
        {
            if (!Kerbed(r) || r.Count < 2) continue;
            double reach = r.HalfWidth + FootpathOut + margin;
            for (int i = 0; i + 1 < r.Count; i++)
            {
                double ax = Mathx.WrapDelta(r.X[i] - cx, _w), az = Mathx.WrapDelta(r.Z[i] - cz, _h);
                if (Math.Abs(ax) > reach + 8 || Math.Abs(az) > reach + 8) continue;
                double ex = r.X[i + 1] - r.X[i], ez = r.Z[i + 1] - r.Z[i], l2 = ex * ex + ez * ez;
                double t = l2 < 1e-9 ? 0 : Math.Clamp(-(ax * ex + az * ez) / l2, 0, 1);
                double px = ax + ex * t, pz = az + ez * t;
                if (px * px + pz * pz < reach * reach) return false;
            }
            foreach (bool start in new[] { true, false })
                if ((start ? r.StartJoin : r.EndJoin) == JunctionForm.DeadEnd)
                {
                    int e = start ? 0 : r.Count - 1;
                    if (Dist((r.X[e], r.Z[e]), (cx, cz)) < RoadCorners.BulbRadius(r) + FootpathOut + margin) return false;
                }
        }
        foreach (RoadCorner c in Corners)
        {
            if (!c.Kerbed || Math.Abs(Mathx.WrapDelta(c.X - cx, _w)) > c.Cut + FootpathOut || Math.Abs(Mathx.WrapDelta(c.Z - cz, _h)) > c.Cut + FootpathOut) continue;
            if (c.Edge(cx, cz, _w, _h) < FootpathOut + margin) return false;
        }
        return true;
    }

    Dictionary<(int, int), List<(InfraRoute R, int I)>> _roadCells;
    int _roadPoints;
    double _roadReach;

    public InfraRoute Nearest(double x, double z, double within, out int index, out double distance, ICollection<InfraRoute> except = null)
    {
        index = -1;
        distance = double.MaxValue;
        InfraRoute best = null;
        foreach (InfraRoute r in Routes)
            for (int i = 0; i < r.Count && except?.Contains(r) != true; i++)
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

        /// <summary>Clears the cells a route was marked through (where no later route took them).</summary>
        public void Unmark(InfraRoute r)
        {
            int[] grid = r.Kind == RouteKind.Rail ? _rail : _road;
            for (int i = 0; i < r.Count; i++)
            {
                int k = Index(r.X[i], r.Z[i]);
                if (grid[k] == r.Id + 1) grid[k] = 0;
            }
        }

        /// <summary>A route taken out of the list at <paramref name="id"/>: its cells cleared, every later route's moved down one.</summary>
        public void Renumber(int id, bool rail)
        {
            foreach (int[] grid in new[] { _road, _rail })
                for (int k = 0; k < grid.Length; k++)
                {
                    if (grid[k] == id + 1 && (grid == _rail) == rail) grid[k] = 0;
                    else if (grid[k] > id + 1) grid[k]--;
                }
        }

        static readonly (int dx, int dz)[] Moves =
        {
            (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1),
            (2, 1), (2, -1), (-2, 1), (-2, -1), (1, 2), (1, -2), (-1, 2), (-1, -2),
        };

        /// <summary>The cells of the cheapest path between two points, or null.</summary>
        public List<int> Find((double X, double Z) from, (double X, double Z) to, RouteKind kind, TechLevel tech)
        {
            int start = Free(from, to), goal = Free(to, from);
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
            int start = Free(a, b), goal = Free(b, a);
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
        /// <summary>
        /// The open cell a search starts or ends at for a point: its own, or where it lies on a place's keep-off ground
        /// (a gate's way out), the nearest open cell to it, and of two as near the one toward the other end. (Taking the
        /// first open cell of each ring as scanned leaned every way out toward one corner of the map: a track left its
        /// gate aslant and turned back on itself on the flat.)
        /// </summary>
        int Free((double X, double Z) p, (double X, double Z)? toward = null)
        {
            int k = Index(p.X, p.Z);
            if (!_blocked[k]) return k;
            int best = -1;
            double least = double.MaxValue;
            for (int dz = -4; dz <= 4; dz++)
            for (int dx = -4; dx <= 4; dx++)
            {
                int m = Index(p.X + dx * Cell, p.Z + dz * Cell);
                if (_blocked[m]) continue;
                double d = dx * dx + dz * dz;
                if (toward is { } t)
                {
                    double tx = Mathx.WrapDelta(t.X - (p.X + dx * Cell), _w), tz = Mathx.WrapDelta(t.Z - (p.Z + dz * Cell), _h);
                    d += 1e-4 * Math.Sqrt(tx * tx + tz * tz) / Cell;
                }
                if (d < least) { least = d; best = m; }
            }
            return best;
        }
    }
}
