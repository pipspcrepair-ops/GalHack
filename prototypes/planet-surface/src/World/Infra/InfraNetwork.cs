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
/// about one link in ten added back as loops. Every link is found by one path engine over the real
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
    /// <summary>Links the spanning tree and loops asked for that no route could be found for (tests).</summary>
    public int Unrouted;
    /// <summary>Links between places on land the sea separates, which a world of this tech level never bridged (tests).</summary>
    public int SeaSeparated;
    /// <summary>Places the sea cuts off from every other: no route reaches them.</summary>
    public readonly List<PoiSite> Isolated = new();
    /// <summary>Power lines along the network (planned once the terrain is final: <see cref="PowerLines.Plan"/>).</summary>
    public PowerLines Power = new();

    /// <summary>The places the network joins (built by people: not wrecks, alien or Precursor sites).</summary>
    public readonly List<PoiSite> Places = new();

    public const float Spacing = 4f;

    double _w, _h;

    // ---- per kind ---------------------------------------------------------------------------

    /// <summary>The steepest a kind's deck climbs, metres per metre.</summary>
    public static float MaxGrade(RouteKind k) => k switch { RouteKind.Highway => 0.06f, RouteKind.Road => 0.09f, RouteKind.Track => 0.15f, _ => 0.03f };
    /// <summary>How far a deck may stand over the ground before it becomes a bridge.</summary>
    static float BridgeFill(RouteKind k) => k switch { RouteKind.Highway => 8f, RouteKind.Road => 7f, RouteKind.Track => 5f, _ => 8f };
    /// <summary>How deep a deck may cut before it becomes a tunnel (tracks never tunnel: they climb and wind).</summary>
    static float TunnelCut(RouteKind k) => k switch { RouteKind.Highway => 14f, RouteKind.Road => 13f, RouteKind.Track => float.MaxValue, _ => 11f };
    /// <summary>How far along the deck heights are averaged before the grade is held: highways and railways sweep, tracks follow the land.</summary>
    static float SmoothRun(RouteKind k) => k switch { RouteKind.Highway => 60f, RouteKind.Road => 32f, RouteKind.Track => 14f, _ => 100f };

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
    public static int Rank(PoiSite p) => p.Tier switch
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
        var router = new Router(map, bare, pois);
        var rng = new PoiGenerator.Gen.Rng(PoiGenerator.Gen.Hash.Combine(seed, 0x1AF7AUL));

        // The links: a spanning tree by importance, then about one in ten added back as loops.
        var places = net.Places;
        var links = new List<(int A, int B, double D)>();
        for (int a = 0; a < places.Count; a++)
        for (int b = a + 1; b < places.Count; b++)
            links.Add((a, b, net.Dist(places[a].Center, places[b].Center)));
        var tree = new List<(int A, int B, double D)>();
        var parent = Enumerable.Range(0, places.Count).ToArray();
        int Find(int i) => parent[i] == i ? i : parent[i] = Find(parent[i]);
        foreach (var l in links.OrderBy(l => l.D / (1 + 0.25 * Math.Min(Rank(places[l.A]), Rank(places[l.B])))))
        {
            int ra = Find(l.A), rb = Find(l.B);
            if (ra == rb) continue;
            parent[ra] = rb;
            tree.Add(l);
        }
        double longest = tree.Count > 0 ? tree.Max(l => l.D) : 0;
        int loops = places.Count >= 4 ? Math.Max(1, (int)Math.Round(places.Count * 0.1)) : 0;
        var extra = links.Where(l => !tree.Contains(l) && l.D < longest * 1.2).OrderBy(l => l.D).Take(loops).ToList();

        // The most important links first, so the lesser ones merge into them at junctions.
        var all = tree.Concat(extra)
            .Select(l => (l.A, l.B, l.D, Kind: KindFor(places[l.A], places[l.B], profile.Before)))
            .OrderBy(l => l.Kind).ThenBy(l => l.D).ToList();
        foreach (var l in all)
            if (!net.Link(router, places[l.A], places[l.B], l.Kind, rng))
            {
                // Over land, or across a sea its builders never bridged?
                if (router.SeaBetween(places[l.A].Center, places[l.B].Center)) net.SeaSeparated++;
                else net.Unrouted++;
            }
        foreach (PoiSite p in places)
            if (!net.Routes.Any(r => r.From == p || r.To == p)) net.Isolated.Add(p);

        // Railways where the old world ran them: a line through the places that mattered most.
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
                if (net.Link(router, railNodes[l.A], railNodes[l.B], RouteKind.Rail, rng)) laid++;
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
        RouteKind k = r >= 3 || Urban(a) && Urban(b) && tech >= TechLevel.Mid ? RouteKind.Highway : r >= 2 ? RouteKind.Road : RouteKind.Track;
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

    /// <summary>Routes one link: from a gate of one place to a gate of the other, merging into roads already laid.</summary>
    bool Link(Router router, PoiSite a, PoiSite b, RouteKind kind, PoiGenerator.Gen.Rng rng)
    {
        var ga = GateToward(a, b.Center);
        var gb = GateToward(b, a.Center);
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
            List<(double X, double Z)> path = Resample(Smooth(pts, run.From == 0 ? 2 : 0, run.To == cells.Count - 1 ? 2 : 0), Spacing);
            if (path.Count < 2) continue;
            // Junction ends land exactly on the route they join, at its deck height.
            if (run.JoinFrom >= 0 && Snap(Routes[run.JoinFrom], path[0], out var p0, out float y0)) { path[0] = p0; pins.Add((0, y0)); }
            if (run.JoinTo >= 0 && Snap(Routes[run.JoinTo], path[^1], out var p1, out float y1)) { path[^1] = p1; pins.Add((path.Count - 1, y1)); }
            if (run.From == 0) pins.Add((0, ga.Y));
            if (run.To == cells.Count - 1) pins.Add((path.Count - 1, gb.Y));
            // A railway crosses roads on the level: its deck meets theirs where they cross.
            if (kind == RouteKind.Rail)
                for (int k = 2; k < path.Count - 2; k += 1)
                {
                    int road = router.RouteAtPoint(path[k].X, path[k].Z, RouteKind.Road);
                    if (road >= 0 && Snap(Routes[road], path[k], out _, out float yr) && !pins.Any(p => Math.Abs(p.Index - k) < 6)) pins.Add((k, yr));
                }
            var route = Build(router, path, pins, kind, a, b, run.From == 0 ? ga.Index : -1, run.To == cells.Count - 1 ? gb.Index : -1, rng);
            if (route == null) continue;
            // A railway goes only where the land lets it lie mostly on the ground: if most of it would be
            // viaduct and tunnel, the old world never built it (a maglev rides its guideway anyway).
            if (kind == RouteKind.Rail && !route.Maglev && route.Span.Count(sp => sp != RoadSpan.Ground) > route.Span.Length * 0.3)
            {
                Structures.RemoveAll(st => st.Route == route);
                Routes.RemoveAt(Routes.Count - 1);
                RailDropped++;
                continue;
            }
            route.From = run.From == 0 ? a : null;
            route.To = run.To == cells.Count - 1 ? b : null;
            router.Mark(route);
            any = true;
        }
        return any;
    }

    /// <summary>The nearest point of a route to a point (the short way round the seams), moved to the copy nearest it, and the deck height there.</summary>
    bool Snap(InfraRoute r, (double X, double Z) near, out (double X, double Z) at, out float y)
    {
        double best = double.MaxValue;
        at = near;
        y = 0;
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
        }
        return best < 40 * 40;
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
        if (!maglev)
        {
            FollowLand(spans, deck, ground, water, fixedAt, RoadSpan.Bridge, MaxViaduct(kind, Profile.Before));
            FollowLand(spans, deck, ground, water, fixedAt, RoadSpan.Tunnel, MaxTunnel(kind, Profile.Before));
        }
        // Wherever the land was followed, the deck may be steep but always walkable: never more than 0.45 a stretch.
        HoldGrade(deck, fixedAt, floor, 0.45f * Spacing, 3);
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
            FromGate = gateA, ToGate = gateB,
        };
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
                    buckle: r.Kept || r.Kind == RouteKind.Rail || r.Surface is StampSurface.Dirt or StampSurface.Gravel ? 0f : 0.22f * r.Wear));
            }
        }
        return list;
    }

    /// <summary>The route nearest a world point within a distance, its point index and the distance (x, z wrap).</summary>
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
            foreach (PoiSite p in pois)
            {
                TerrainStamp t = p.Stamp;
                for (double z = t.MinZ - 4; z <= t.MaxZ + 4; z += Cell / 2)
                for (double x = t.MinX - 4; x <= t.MaxX + 4; x += Cell / 2)
                    _blocked[Index(x, z)] = true;
            }
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

        public int RouteAt(int k, RouteKind kind) => (kind == RouteKind.Rail ? _rail[k] : _road[k]) - 1;
        public int RouteAtPoint(double x, double z, RouteKind kind) => RouteAt(Index(x, z), kind);

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
            float grade = MaxGrade(kind);
            float bridge = kind switch { RouteKind.Highway => 5f, RouteKind.Road => 7f, RouteKind.Track => 10f, _ => 7f };
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
                    float c = r <= 1f ? len * (1f + 1.5f * r * r) : len * (2.5f + 30f * (r - 1f));
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
