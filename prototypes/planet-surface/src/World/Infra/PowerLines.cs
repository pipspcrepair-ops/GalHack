using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GalHack.Terrain;
using Planets.Gen;
using PoiGenerator.Gen;
using PoiSite = PlanetSurface.Poi.PoiSite;

namespace PlanetSurface.Infra;

/// <summary>What a node of the grid is: a pole or pylon on a line, a substation where a town takes its power off the high-voltage line, or a plant the grid starts from.</summary>
public enum PowerRole { Line, Substation, Plant }

/// <summary>A power pole or pylon: where it stands, which way its crossarm faces, and whether it still stands.</summary>
public sealed class PowerPole
{
    public double X, Z;
    public float Ground;
    /// <summary>Across the line (unit, horizontal): the crossarm lies along it.</summary>
    public float SideX, SideZ;
    public bool Pylon;
    /// <summary>Fallen over (lying on the ground toward <see cref="SideX"/>, <see cref="SideZ"/> rotated a quarter): its wires hang from the poles either side.</summary>
    public bool Fallen;
    /// <summary>A pylon's height: 30 to 50 m, as tall as its spans need.</summary>
    public float Tall = 36f;
    /// <summary>A pole or pylon on a line; or a substation (its yard: <see cref="Ground"/> is its slab's top); or a plant, which is a place of its own and draws nothing here.</summary>
    public PowerRole Role;
    /// <summary>A plant's place, or the town a substation serves (null for a cluster of houses that is no place).</summary>
    public PoiSite Site;
    public float Height => Role == PowerRole.Substation ? Substation.High : Pylon ? Tall : 9f;

    /// <summary>A pylon's four feet on the ground plan (world x, z): the corners of its base.</summary>
    public (double X, double Z)[] FeetXZ()
    {
        float bh = Height * 0.13f;
        float[] sa = { 1, 1, -1, -1 }, sb = { 1, -1, -1, 1 };
        var f = new (double X, double Z)[4];
        for (int i = 0; i < 4; i++)
            f[i] = (X + (SideX * sa[i] + -SideZ * sb[i]) * bh, Z + (SideZ * sa[i] + SideX * sb[i]) * bh);
        return f;
    }

    /// <summary>The anchor of wire <paramref name="k"/> (of <see cref="PowerLines.Wires"/>), in world metres (x, z as the pole's; any copy).</summary>
    public Vector3 Anchor(int k, int wires)
    {
        if (Role == PowerRole.Substation)
        {
            // The high-voltage line lands on the tall gantry at one end of the yard; the town's lines leave from the low one at the other.
            bool high = wires > 3;
            float a = high ? Substation.HighAt : Substation.LowAt, c = (k - (wires - 1) * 0.5f) * (high ? 1.7f : 1.1f);
            float sy = Ground + (high ? Substation.High : Substation.Low) - 0.6f;
            return new Vector3((float)X - SideZ * a + SideX * c, sy, (float)Z + SideX * a + SideZ * c);
        }
        float across = (k - (wires - 1) * 0.5f) * (Pylon ? 4.2f : 0.85f);
        float y = Ground + Height - (Pylon ? 2f + (k % 2) * 4f : 0.25f);
        return new Vector3((float)X + SideX * across, y, (float)Z + SideZ * across);
    }

    /// <summary>Where a pole's service drops leave it: a bracket under its crossarm, on the side toward the house.</summary>
    public Vector3 DropAnchor(double towardX, double towardZ)
    {
        double dx = towardX - X, dz = towardZ - Z, l = Math.Max(1e-6, Math.Sqrt(dx * dx + dz * dz));
        return new Vector3((float)(X + dx / l * 0.2), Ground + Height - 1.5f, (float)(Z + dz / l * 0.2));
    }
}

/// <summary>A substation's yard: its size along and across, and its gantries' heights and places along it, metres.</summary>
public static class Substation
{
    public const float HalfAlong = 8f, HalfAcross = 5.5f, High = 9f, Low = 6.5f, HighAt = 5.5f, LowAt = -6f;
}

/// <summary>How a service drop hangs: strung from pole to wall, hanging from the pole or from the house when it broke, or lying on the ground between them with both ends gone.</summary>
public enum DropState { Strung, FromPole, FromHouse, Lying }

/// <summary>One house's service drop: a single thin wire from the nearest pole of its town's lines to the fixing on its wall.</summary>
public sealed class ServiceDrop
{
    public int Pole;
    public FlavourRuin House;
    /// <summary>The fixing on the house's wall, world metres; and the wall block it is fixed to (world cell).</summary>
    public Vector3 Fixing;
    public (int X, int Y, int Z) Cell;
    public DropState State;
}

/// <summary>One wire between two poles, or hanging from one when the other has fallen or the wire has snapped.</summary>
public sealed class PowerWire
{
    public int A, B;     // pole indices; B is where a hanging wire would have gone
    public int K;        // which wire on the crossarm
    public bool Spanned = true;
    public float Slack = 0.025f;
}

/// <summary>
/// Power lines, only where they made sense (design doc "Infrastructure"): wooden poles along the roads
/// to the places that needed power (towns, bases, mines and works on worlds that had grids), and on
/// modern and high-tech worlds a grid of steel pylons, 30 to 50 m tall, striding from place to place on
/// long straight legs of their own, whatever the roads do. Every wire is anchored: strung between two
/// standing poles with its sag, or, where a pole has fallen or a wire snapped on a neglected line,
/// hanging from the pole that still holds it down to the ground. Poles stand beside the road bed,
/// never on a deck, a bridge or a place's pad. Planned once the terrain (with its stamps) is final,
/// so every pole stands on the ground as it is.
/// </summary>
public sealed class PowerLines
{
    public readonly List<PowerPole> Poles = new();
    public readonly List<PowerWire> Wires = new();
    /// <summary>Wires per crossarm: two on a pole line, six on a pylon line.</summary>
    public int WiresOn(PowerPole p) => p.Pylon ? 6 : 3;
    /// <summary>For tests: the route each run of poles follows.</summary>
    public readonly List<(InfraRoute Route, int FirstPole, int LastPole)> Runs = new();

    public static PowerLines Plan(InfraNetwork net, PlanetTerrainSource source, ulong seed)
    {
        var lines = new PowerLines();
        WorldProfile profile = net.Profile;
        if (profile == null || !profile.Inhabited) return lines;
        var rng = new PoiGenerator.Gen.Rng(PoiGenerator.Gen.Hash.Combine(seed, 0x90E7UL));
        // A world's plan says who had power: nobody, only the port, or its towns, from the plants that fed them.
        string style = net.RegionPlan?.Roll.Power;
        if (style == "none") return lines;
        // A world with a grid: plants feed substations at the edges of towns by pylon lines, poles carry it
        // from each substation along the streets and roads, and every house has its own drop from a pole.
        if (profile.Before != TechLevel.Low && style != "port_only")
        {
            lines.Grid(net, source, rng);
            return lines;
        }
        foreach (InfraRoute r in net.Routes)
        {
            // Along roads and highways (railways carry their own catenary or none); a frontier world
            // strung poles only to mines and works.
            if (r.Kind is RouteKind.Track or RouteKind.Rail) continue;
            bool industrial(PoiSite p) => p != null && (p.Family.Tags.Contains("mine") || p.Family.Tags.Contains("industry"));
            if (profile.Before == TechLevel.Low && !industrial(r.From) && !industrial(r.To)) continue;
            if (r.Length < 120 || r.Kind == RouteKind.Highway && profile.Before != TechLevel.Low) continue;
            if (style == "port_only" && !(r.From?.Primary == true || r.To?.Primary == true)) continue;
            lines.Lay(r, source, net, false, rng);
        }
        return lines;
    }

    /// <summary>The pylons' legs, as polylines of pylons (for tests).</summary>
    public readonly List<(int FirstPole, int LastPole)> Legs = new();

    const float PylonSpan = 300f, PylonFarthest = 440f;
    static readonly float[] PylonHeights = { 30f, 36f, 42f, 50f };

    /// <summary>The grid's built connections, node to node (whether or not their wires still hang): what tests walk from a house back to a plant.</summary>
    public readonly List<(int A, int B)> Links = new();
    /// <summary>Every powered house's service drop.</summary>
    public readonly List<ServiceDrop> Drops = new();
    /// <summary>Houses that would have had power but no line could reach (tests: there should be none).</summary>
    public readonly List<FlavourRuin> Unserved = new();
    /// <summary>Why each unserved house went without (tests).</summary>
    public readonly Dictionary<string, int> UnservedWhy = new();

    void Without(FlavourRuin house, string why)
    {
        Unserved.Add(house);
        UnservedWhy[why] = UnservedWhy.GetValueOrDefault(why) + 1;
    }

    /// <summary>Whether a small place would have had power on a world with a grid: a house, a shop, a farmstead or a wayside stop; not a shed.</summary>
    public static bool Powered(FlavourRuin r) => !r.Family.Tags.Contains("shed");

    /// <summary>How far a service drop reaches from its pole to the wall, m; farther, a spur of poles comes nearer first.</summary>
    public const float DropReach = 38f;
    const float PoleSpan = 38f;

    int Add(PowerPole p)
    {
        Poles.Add(p);
        return Poles.Count - 1;
    }

    /// <summary>
    /// The grid, as the owner set it out: tall pylon lines carry high voltage from the plants (any place that
    /// made power; the main port where none stands) to a substation at the edge of each town, city and works;
    /// from each substation, poles carry it along the streets and roads; and from the nearest pole, one thin
    /// drop runs to a fixing on the wall of every house that would have had power. A house past the town's
    /// lines gets a spur of poles of its own across the land.
    /// </summary>
    void Grid(InfraNetwork net, PlanetTerrainSource source, PoiGenerator.Gen.Rng rng)
    {
        double w = net.Width, h = net.Height;
        Func<double, double, float> ground = (x, z) => source.SurfaceHeight(Mathx.WrapX(x, w), Mathx.Wrap(z, h));
        double Dist(double ax, double az, double bx, double bz) => Math.Sqrt(Math.Pow(Mathx.WrapDelta(ax - bx, w), 2) + Math.Pow(Mathx.WrapDelta(az - bz, h), 2));
        float wear = net.Routes.Count == 0 ? 0.8f : net.Routes.Average(r => r.Wear);
        bool kept = net.Routes.Any(r => r.Kept);

        // ---- the plants.
        var plants = net.Places.Where(p => p.Family.Tags.Contains("power") && p.Family.Id != "substation" && InfraNetwork.Served(p)).ToList();
        if (plants.Count == 0 && net.Places.Where(InfraNetwork.Served).OrderByDescending(InfraNetwork.Rank).FirstOrDefault() is PoiSite main) plants.Add(main);
        if (plants.Count == 0) return;
        var plantNodes = plants.Select(p => Add(new PowerPole { X = p.Center.X, Z = p.Center.Z, Ground = ground(p.Center.X, p.Center.Z), Role = PowerRole.Plant, Site = p })).ToList();

        // ---- the towns: clusters of houses (a street, the start town, a hamlet), then the places people lived and worked in.
        var houses = net.Ruins.Where(Powered).ToList();
        var towns = new List<(double X, double Z, double Radius, PoiSite Site, List<FlavourRuin> Houses)>();
        var left = new List<FlavourRuin>(houses);
        while (left.Count > 0)
        {
            var group = new List<FlavourRuin> { left[0] };
            left.RemoveAt(0);
            for (int i = 0; i < group.Count; i++)
                for (int j = left.Count - 1; j >= 0; j--)
                    if (Dist(group[i].Center.X, group[i].Center.Z, left[j].Center.X, left[j].Center.Z) < 120) { group.Add(left[j]); left.RemoveAt(j); }
            if (group.Count < 3) continue;
            double ox = group[0].Center.X, oz = group[0].Center.Z, sx = 0, sz = 0;
            foreach (FlavourRuin r in group) { sx += Mathx.WrapDelta(r.Center.X - ox, w); sz += Mathx.WrapDelta(r.Center.Z - oz, h); }
            double gx = ox + sx / group.Count, gz = oz + sz / group.Count;
            towns.Add((gx, gz, group.Max(r => Dist(r.Center.X, r.Center.Z, gx, gz)) + 20, null, group));
        }
        if (net.City is CityPlan city) towns.Add((city.CenterX, city.CenterZ, city.Radius, null, new List<FlavourRuin>()));
        foreach (PoiSite p in net.Places.Where(p => InfraNetwork.Rank(p) >= 2 && InfraNetwork.Served(p) && !plants.Contains(p)))
        {
            double reach = Math.Max(p.Stamp.ReachX, p.Stamp.ReachZ);
            if (towns.Any(t => Dist(t.X, t.Z, p.Center.X, p.Center.Z) < t.Radius + reach + 150)) continue;
            towns.Add((p.Center.X, p.Center.Z, reach, p, new List<FlavourRuin>()));
        }
        if (towns.Count == 0 && houses.Count > 0) towns.Add((houses[0].Center.X, houses[0].Center.Z, 30, null, new List<FlavourRuin>()));

        // ---- a substation at the edge of each, on the side toward the nearest plant.
        var subs = new List<int>();
        var townOf = new Dictionary<int, (double X, double Z, double Radius, PoiSite Site, List<FlavourRuin> Houses)>();
        foreach (var t in towns)
        {
            PoiSite near = plants.OrderBy(p => Dist(p.Center.X, p.Center.Z, t.X, t.Z)).First();
            double dx = Mathx.WrapDelta(near.Center.X - t.X, w), dz = Mathx.WrapDelta(near.Center.Z - t.Z, h), dl = Math.Sqrt(dx * dx + dz * dz);
            if (dl < 1) (dx, dz, dl) = (1, 0, 1);
            if (Yard(net, source, ground, t.X, t.Z, t.Radius, dx / dl, dz / dl) is not PowerPole yard) continue;
            yard.Site = t.Site;
            int s = Add(yard);
            subs.Add(s);
            townOf[s] = t;
        }

        // ---- the high-voltage lines: the shortest lines that join every substation to a plant (plants and the
        // substations already fed count as joined), each its own straight stride of pylons.
        var joined = new List<int>(plantNodes);
        var waiting = new List<int>(subs);
        while (waiting.Count > 0)
        {
            (int From, int To) next = default;
            double best = double.MaxValue;
            foreach (int a in joined)
                foreach (int b in waiting)
                {
                    double d = Dist(Poles[a].X, Poles[a].Z, Poles[b].X, Poles[b].Z);
                    if (d < best) { best = d; next = (a, b); }
                }
            waiting.Remove(next.To);
            if (Line(next.From, next.To, net, source, rng)) joined.Add(next.To);
        }

        // ---- the town lines: poles from each substation along its streets and the roads out of it.
        foreach (int s in subs)
        {
            var t = townOf[s];
            var routes = t.Houses.Select(r => r.Route).Where(r => r != null && r.Kind is RouteKind.Road or RouteKind.Highway).Distinct().ToList();
            if (t.Site != null)
                routes.AddRange(net.Routes.Where(r => (r.From == t.Site || r.To == t.Site) && r.Kind == RouteKind.Road && r.Length >= 120 && !routes.Contains(r)));
            foreach (InfraRoute r in routes)
            {
                // A street's own length; a road out of a town as far as the next place, or 1.5 km.
                int from = 0, to = r.Count - 1;
                if (t.Site == null || (r.From != t.Site && r.To != t.Site))
                {
                    from = -1;
                    for (int i = 0; i < r.Count; i++)
                        if (Dist(r.X[i], r.Z[i], t.X, t.Z) < t.Radius + 60) { if (from < 0) from = i; to = i; }
                    if (from < 0) continue;
                }
                else if (r.Length > 1500)
                {
                    int keep = (int)(1500 / InfraNetwork.Spacing);
                    if (r.From == t.Site) to = Math.Min(r.Count - 1, keep);
                    else from = Math.Max(0, r.Count - 1 - keep);
                }
                Lay(r, source, net, false, rng, from, to);
            }
        }

        // ---- every stretch of poles fed: a spur from the nearest pole already fed (or a substation) to any stretch that broke off.
        var fed = Fed();
        var stranded = new HashSet<int>();
        for (int round = 0; round < 200; round++)
        {
            int loose = -1, from = -1;
            double best = double.MaxValue;
            for (int i = 0; i < Poles.Count; i++)
            {
                if (fed[i] || stranded.Contains(i) || Poles[i].Role != PowerRole.Line || Poles[i].Pylon) continue;
                foreach (int j in Feeders(fed))
                {
                    double d = Dist(Poles[i].X, Poles[i].Z, Poles[j].X, Poles[j].Z);
                    if (d < best) { best = d; loose = i; from = j; }
                }
            }
            if (loose < 0) break;
            int end = Spur(from, Poles[loose].X, Poles[loose].Z, PoleSpan, net, source, ground, rng, wear, kept);
            if (end >= 0 && Dist(Poles[end].X, Poles[end].Z, Poles[loose].X, Poles[loose].Z) <= PoleSpan + 6 && Clears(Poles[end], Poles[loose], ground, 2.5f)) String(end, loose, rng, wear, kept);
            fed = Fed();
            // A stretch nothing could reach stands unfed (its houses take spurs of their own): not tried again.
            if (!fed[loose]) stranded.UnionWith(Stretch(loose));
        }

        // ---- the drops: from the nearest fed pole to each house's wall; past a drop's reach, a spur comes nearer first.
        foreach (FlavourRuin house in houses)
        {
            var (hx, hz) = house.Center;
            int pole = -1;
            double best = double.MaxValue;
            for (int i = 0; i < Poles.Count; i++)
            {
                PowerPole p = Poles[i];
                if (!fed[i] || p.Role != PowerRole.Line || p.Pylon) continue;
                double d = Dist(p.X, p.Z, hx, hz);
                if (d < best) { best = d; pole = i; }
            }
            double halfDiag = Math.Sqrt(Math.Pow(house.Stamp.MaxX - house.Stamp.MinX, 2) + Math.Pow(house.Stamp.MaxZ - house.Stamp.MinZ, 2)) * 0.5;
            if (pole < 0 || best > DropReach + halfDiag * 0.6)
            {
                int from = -1;
                double nearest = double.MaxValue;
                foreach (int j in Feeders(fed))
                {
                    double d = Dist(Poles[j].X, Poles[j].Z, hx, hz);
                    if (d < nearest) { nearest = d; from = j; }
                }
                int end = from < 0 ? -1 : Spur(from, hx, hz, halfDiag + 14, net, source, ground, rng, wear, kept);
                // (A spur that stopped short still stands: the poles it laid are fed.)
                fed = Fed();
                if (end < 0 || Poles[end].Role != PowerRole.Line) { Without(house, from < 0 ? "nothing fed to come from" : end < 0 ? $"no spur: {_spurWhy}" : "the spur ended at a substation"); continue; }
                pole = end;
            }
            PowerPole pp = Poles[pole];
            if (!Fixing(house, pp.X, pp.Z, w, h, out Vector3 at, out var cell)) { Without(house, "no wall to fix to"); continue; }
            Vector3 anchor = pp.DropAnchor(at.X, at.Z);
            if (Vector3.Distance(anchor with { Y = 0 }, at with { Y = 0 }) > DropReach + 8) { Without(house, "the pole too far from the wall"); continue; }
            // What still holds it: a ruined house's walls can't be trusted to keep the fixing, so its drop hangs from the pole.
            bool broken = house.State == PoiState.Ruined || house.Blast > 0.02f;
            DropState state = pp.Fallen ? (broken ? DropState.Lying : DropState.FromHouse)
                : broken ? DropState.FromPole
                : !kept && rng.Chance(0.15f * wear) ? (rng.Chance(0.5f) ? DropState.FromPole : DropState.FromHouse) : DropState.Strung;
            // A strung drop clears the ground all the way; one that would not is down.
            if (state == DropState.Strung && GalHack.Deco.DecoCables.Span(anchor, at, DropSlack, 12).Any(q => q.Y < ground(q.X, q.Z) + 1.2f)) state = DropState.FromHouse;
            Drops.Add(new ServiceDrop { Pole = pole, House = house, Fixing = at, Cell = cell, State = state });
        }
    }

    /// <summary>The poles linked to one, over the grid's links.</summary>
    HashSet<int> Stretch(int start)
    {
        var seen = new HashSet<int> { start };
        var stack = new Stack<int>();
        stack.Push(start);
        while (stack.Count > 0)
        {
            int i = stack.Pop();
            foreach (var (a, b) in Links)
            {
                int j = a == i ? b : b == i ? a : -1;
                if (j >= 0 && seen.Add(j)) stack.Push(j);
            }
        }
        return seen;
    }

    string _spurWhy = "";

    /// <summary>A drop's sag: a thin wire strung loosely.</summary>
    public const float DropSlack = 0.035f;

    /// <summary>Which nodes are fed: reached from a plant over the grid's links, a town's poles only through its substation.</summary>
    public bool[] Fed()
    {
        var adj = new List<int>[Poles.Count];
        for (int i = 0; i < adj.Length; i++) adj[i] = new List<int>();
        foreach (var (a, b) in Links) { adj[a].Add(b); adj[b].Add(a); }
        var fed = new bool[Poles.Count];
        var queue = new Queue<int>();
        for (int i = 0; i < Poles.Count; i++)
            if (Poles[i].Role == PowerRole.Plant) { fed[i] = true; queue.Enqueue(i); }
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            bool high = Poles[i].Role == PowerRole.Plant || Poles[i].Pylon;
            foreach (int j in adj[i])
            {
                if (fed[j]) continue;
                // High voltage steps down only at a substation: a pylon never feeds a pole directly.
                bool low = Poles[j].Role == PowerRole.Line && !Poles[j].Pylon;
                if (high && low) continue;
                fed[j] = true;
                queue.Enqueue(j);
            }
        }
        return fed;
    }

    /// <summary>What a spur can leave from: a fed substation or a fed pole.</summary>
    IEnumerable<int> Feeders(bool[] fed)
    {
        for (int i = 0; i < Poles.Count; i++)
            if (fed[i] && (Poles[i].Role == PowerRole.Substation || Poles[i].Role == PowerRole.Line && !Poles[i].Pylon)) yield return i;
    }

    /// <summary>
    /// A substation's yard by a town: on open ground just past its edge toward the line coming in (or round to
    /// the side if that is taken), clear of every road, place and pad, the slab level enough to lay. Its long
    /// axis points at the line: the tall gantry at that end.
    /// </summary>
    static PowerPole Yard(InfraNetwork net, PlanetTerrainSource source, Func<double, double, float> ground, double cx, double cz, double radius, double ux, double uz)
    {
        double w = net.Width, h = net.Height;
        foreach (double d in Enumerable.Range(0, 16).Select(i => radius + 25 + i * 15))
            foreach (double deg in new[] { 0.0, 25, -25, 50, -50, 80, -80, 115, -115, 150, -150, 180 })
            {
                double a = deg * Math.PI / 180, vx = ux * Math.Cos(a) - uz * Math.Sin(a), vz = ux * Math.Sin(a) + uz * Math.Cos(a);
                double x = cx + vx * d, z = cz + vz * d;
                if (!net.OffRoad(x, z, Math.Sqrt(Substation.HalfAlong * Substation.HalfAlong + Substation.HalfAcross * Substation.HalfAcross) + 3)) continue;
                float lo = float.MaxValue, hi = float.MinValue;
                bool ok = true;
                for (int i = -1; i <= 1 && ok; i++)
                    for (int j = -1; j <= 1 && ok; j++)
                    {
                        double px = x + vx * (Substation.HalfAlong + 2) * i - vz * (Substation.HalfAcross + 2) * j, pz = z + vz * (Substation.HalfAlong + 2) * i + vx * (Substation.HalfAcross + 2) * j;
                        double qx = Mathx.WrapX(px, w), qz = Mathx.Wrap(pz, h);
                        if (source.StampWeight(qx, qz) > 0f || source.WaterSurfaceAt(qx, qz, out _)) ok = false;
                        float g = ground(px, pz);
                        lo = Math.Min(lo, g);
                        hi = Math.Max(hi, g);
                    }
                if (!ok || hi - lo > 1.6f) continue;
                return new PowerPole { X = Mathx.WrapX(x, w), Z = Mathx.Wrap(z, h), Ground = hi + 0.15f, SideX = (float)vz, SideZ = (float)-vx, Role = PowerRole.Substation };
            }
        return null;
    }

    /// <summary>A pole may stand here: dry ground, off every road's bed, and off any place's footprint and apron (the outer blend round a yard is the land's own, cut and filled).</summary>
    static bool PoleSpot(InfraNetwork net, PlanetTerrainSource source, double x, double z)
    {
        double cx = Mathx.WrapX(x, net.Width), cz = Mathx.Wrap(z, net.Height);
        return source.Built(cx, cz, out _) < 0.5f && !source.WaterSurfaceAt(cx, cz, out _) && net.OffRoad(x, z, 1.5);
    }

    /// <summary>Strings a pole line's wires between two nodes and links them: strung, or hanging from whichever still stands.</summary>
    void String(int prev, int idx, PoiGenerator.Gen.Rng rng, float wear, bool kept)
    {
        PowerPole a = Poles[prev], pole = Poles[idx];
        Links.Add((prev, idx));
        int wires = WiresOn(a);
        for (int k = 0; k < wires; k++)
        {
            if (!a.Fallen && !pole.Fallen)
            {
                bool snapped = !kept && rng.Chance(0.06f * wear);
                Wires.Add(snapped ? new PowerWire { A = rng.Chance(0.5f) || pole.Role == PowerRole.Substation ? prev : idx, B = 0, K = k, Spanned = false }.Finish(prev, idx)
                    : new PowerWire { A = prev, B = idx, K = k, Slack = a.Pylon ? 0.02f : 0.03f });
            }
            else if (!a.Fallen) Wires.Add(new PowerWire { A = prev, B = idx, K = k, Spanned = false });
            else if (!pole.Fallen && pole.Role != PowerRole.Substation) Wires.Add(new PowerWire { A = idx, B = prev, K = k, Spanned = false });
        }
    }

    /// <summary>
    /// A spur of poles from a node straight toward a point, stopping <paramref name="shortOf"/> from it: a pole
    /// every span or nearer, each on a spot that takes one (nudged aside off a road or a pad), each span's wires
    /// clear of the ground. The last pole's index, or -1 if the land would not take it.
    /// </summary>
    int Spur(int from, double tx, double tz, double shortOf, InfraNetwork net, PlanetTerrainSource source, Func<double, double, float> ground, PoiGenerator.Gen.Rng rng, float wear, bool kept)
    {
        double w = net.Width, h = net.Height;
        int prev = from;
        for (int step = 0; step < 400; step++)
        {
            PowerPole last = Poles[prev];
            double dx = Mathx.WrapDelta(tx - last.X, w), dz = Mathx.WrapDelta(tz - last.Z, h), dl = Math.Sqrt(dx * dx + dz * dz);
            if (dl <= shortOf + 0.5) return prev;
            double ux = dx / dl, uz = dz / dl;
            double go = Math.Min(PoleSpan, dl - shortOf);
            // Close enough already for what it serves: a pole on a line stops here.
            if (go < 8 && last.Role == PowerRole.Line) return prev;
            PowerPole pole = null;
            bool spotted = false;
            foreach (double back in new[] { 0.0, 6, 12, 18, 24 })
            {
                double t = go - back;
                if (t < Math.Min(8, go) - 1e-6) break;
                foreach (double o in new[] { 0.0, 3, -3, 6, -6, 10, -10, 15, -15, 21, -21 })
                {
                    double x = last.X + ux * t - uz * o, z = last.Z + uz * t + ux * o;
                    if (!PoleSpot(net, source, x, z)) continue;
                    spotted = true;
                    var cand = new PowerPole { X = Mathx.WrapX(x, w), Z = Mathx.Wrap(z, h), Ground = ground(x, z), SideX = (float)-uz, SideZ = (float)ux };
                    // Unwrapped next to the last for the clearance test.
                    var test = new PowerPole { X = x, Z = z, Ground = cand.Ground, SideX = cand.SideX, SideZ = cand.SideZ };
                    if (!Clears(last, test, ground, 2.5f)) continue;
                    pole = cand;
                    break;
                }
                if (pole != null) break;
            }
            if (pole == null)
            {
                _spurWhy = spotted ? "wires would not clear the ground" : step == 0 ? "no spot by where it leaves" : "no spot along the way";
                return -1;
            }
            pole.Fallen = !kept && rng.Chance(0.06f * wear);
            int idx = Add(pole);
            String(prev, idx, rng, wear, kept);
            prev = idx;
        }
        return -1;
    }

    /// <summary>
    /// The wall fixing for a house's drop from a pole at (tx, tz): on the face the house gives (<see cref="FlavourRuin.FixingSide"/>)
    /// or else the face toward the pole, the outer wall nearest that face's middle, at the house's fixing height above the wall's
    /// foot. World metres, just proud of the wall, and the wall block's world cell. False if the house has no wall there tall enough.
    /// </summary>
    public static bool Fixing(FlavourRuin house, double tx, double tz, double w, double h, out Vector3 at, out (int X, int Y, int Z) cell)
    {
        at = default;
        cell = default;
        Layout l = house.Layout;
        BlockGrid g = l.Blocks;
        var (cx, cz) = house.Center;
        Dir side = house.FixingSide ?? FlavourRuins.Facing(Mathx.WrapDelta(tx - cx, w), Mathx.WrapDelta(tz - cz, h));
        PoiGenerator.Gen.Int3 v = side.Vec();
        bool acrossX = v.X == 0;
        int aMin = acrossX ? g.Min.X : g.Min.Z, aMax = acrossX ? g.Max.X : g.Max.Z;
        int sOut = acrossX ? (v.Z > 0 ? g.Max.Z : g.Min.Z) : (v.X > 0 ? g.Max.X : g.Min.X), sIn = acrossX ? (v.Z > 0 ? g.Min.Z : g.Max.Z) : (v.X > 0 ? g.Min.X : g.Max.X);
        int sStep = sOut < sIn ? 1 : -1, mid = (aMin + aMax) / 2;
        bool Wall(int x, int y, int z) { Block b = g.Get(new PoiGenerator.Gen.Int3(x, y, z)); return !b.IsAir && b.Role == Role.Wall && BlockInfo.IsFullCube(b.Type); }
        for (int o = 0; o <= (aMax - aMin) / 2 + 1; o++)
            foreach (int sign in new[] { 1, -1 })
            {
                int a = mid + o * sign;
                if (a < aMin || a > aMax || (o == 0 && sign < 0)) continue;
                for (int s = sOut; s != sIn + sStep; s += sStep)
                {
                    int x = acrossX ? a : s, z = acrossX ? s : a;
                    // The column's wall: its lowest wall block, and how high it stands unbroken from there.
                    int foot = int.MinValue;
                    for (int y = g.Min.Y; y <= g.Max.Y && foot == int.MinValue; y++) if (Wall(x, y, z)) foot = y;
                    if (foot == int.MinValue) continue;
                    int top = foot;
                    while (top + 1 <= g.Max.Y && Wall(x, top + 1, z)) top++;
                    // Anything less than a storey of wall is a fence or a low wall round the yard: look further in.
                    if (top - foot < 2) continue;
                    // Nothing outside it in front of the fixing (a porch roof, a lean-to): the drop must reach it.
                    int y0 = Math.Min(foot + Math.Max(2, (int)house.FixingHeight), top);
                    bool clear = true;
                    for (int k = s - sStep; k != sOut - sStep && clear; k -= sStep)
                        for (int y = y0 - 1; y <= y0 + 1 && clear; y++)
                            if (!g.Get(new PoiGenerator.Gen.Int3(acrossX ? a : k, y, acrossX ? k : a)).IsAir) clear = false;
                    if (!clear) break;
                    cell = (house.OriginX + x, house.OriginY + y0, house.OriginZ + z);
                    at = new Vector3(house.OriginX + x + 0.5f + v.X * 0.56f, house.OriginY + y0 + 0.5f, house.OriginZ + z + 0.5f + v.Z * 0.56f);
                    return true;
                }
            }
        return false;
    }

    /// <summary>A drop's path in world metres: strung with its sag, or hanging from whichever end still holds it and lying on the ground toward the other, or lying all the way.</summary>
    public List<Vector3> DropPath(ServiceDrop d, Func<double, double, float> ground)
    {
        PowerPole p = Poles[d.Pole];
        const float Radius = 0.012f;
        Vector3 pole = p.DropAnchor(d.Fixing.X, d.Fixing.Z);
        float run = Vector3.Distance(pole with { Y = 0 }, d.Fixing with { Y = 0 });
        List<Vector3> Hang(Vector3 from, Vector3 to, float tail)
        {
            var pts = GalHack.Deco.DecoCables.Drop(from, ground(from.X, from.Z), to - from, tail, Radius);
            for (int i = 2; i < pts.Count; i++) pts[i] = pts[i] with { Y = ground(pts[i].X, pts[i].Z) + Radius };
            return pts;
        }
        switch (d.State)
        {
            case DropState.Strung: return GalHack.Deco.DecoCables.Span(pole, d.Fixing, DropSlack, 12);
            case DropState.FromPole: return Hang(pole, d.Fixing, Math.Min(run * 0.6f, 7f));
            case DropState.FromHouse: return Hang(d.Fixing, pole, Math.Min(run * 0.6f, 7f));
            default:
            {
                var pts = new List<Vector3>();
                Vector3 a = new((float)p.X, 0, (float)p.Z), b = d.Fixing with { Y = 0 };
                int n = Math.Max(2, (int)(run / 0.5f));
                for (int i = 0; i <= n; i++)
                {
                    Vector3 q = Vector3.Lerp(a, b, i / (float)n);
                    pts.Add(q with { Y = ground(q.X, q.Z) + Radius });
                }
                return pts;
            }
        }
    }

    /// <summary>
    /// One high-voltage line from one node to another (a plant or a substation): out from a plant's place, or
    /// from beside a substation's tall gantry, straight across the land, or by one bend where the land asks for
    /// it, with pylons spaced evenly from end to end. True if it reached, every span strung.
    /// </summary>
    bool Line(int from, int to, InfraNetwork net, PlanetTerrainSource source, PoiGenerator.Gen.Rng rng)
    {
        PowerPole na = Poles[from], nb = Poles[to];
        Func<double, double, float> ground = (x, z) => source.SurfaceHeight(Mathx.WrapX(x, net.Width), Mathx.Wrap(z, net.Height));
        double ax = na.X, az = na.Z;
        double dx = Mathx.WrapDelta(nb.X - ax, net.Width), dz = Mathx.WrapDelta(nb.Z - az, net.Height), len = Math.Sqrt(dx * dx + dz * dz);
        double ReachOf(PowerPole p) => p.Site != null && p.Role == PowerRole.Plant ? Math.Max(p.Site.Stamp.ReachX, p.Site.Stamp.ReachZ) + 30 : 60;
        double reachA = ReachOf(na), reachB = ReachOf(nb);
        // A substation by its plant: the plant's own switchyard feeds it.
        if (len < reachA + reachB + 60)
        {
            Links.Add((from, to));
            return true;
        }
        double ux = dx / len, uz = dz / len;
        (double X, double Z) start = (ax + ux * reachA, az + uz * reachA), end = (ax + ux * (len - reachB), az + uz * (len - reachB));
        bool Takes(double x, double z)
        {
            double cx = Mathx.WrapX(x, net.Width), cz = Mathx.Wrap(z, net.Height);
            if (source.StampWeight(cx, cz) > 0.05f || source.WaterSurfaceAt(cx, cz, out _)) return false;
            // A pylon's whole footing stands clear of every road and railway, shoulders and junctions too.
            // (A 50 m pylon's feet stand 9.2 m from its middle: the margin covers the tallest.)
            if (!net.OffRoad(x, z, 11)) return false;
            float g = ground(x, z);
            return MathF.Abs(ground(x + 8, z) - g) < 7f && MathF.Abs(ground(x, z + 8) - g) < 7f;
        }
        int Bad(params (double X, double Z)[] pts)
        {
            int bad = 0;
            for (int i = 0; i + 1 < pts.Length; i++)
            {
                double l = Math.Sqrt(Math.Pow(pts[i + 1].X - pts[i].X, 2) + Math.Pow(pts[i + 1].Z - pts[i].Z, 2));
                for (double t = 0; t < l; t += 100)
                    if (!Takes(pts[i].X + (pts[i + 1].X - pts[i].X) * t / l, pts[i].Z + (pts[i + 1].Z - pts[i].Z) * t / l)) bad++;
            }
            return bad;
        }
        var way = new List<(double X, double Z)> { start, end };
        int least = Bad(start, end) + 2;
        double span = len - reachA - reachB;
        foreach (double along in new[] { 0.35, 0.65 })
            foreach (double off in new[] { 0.14, -0.14, 0.25, -0.25 })
            {
                (double X, double Z) bend = (start.X + (end.X - start.X) * along - uz * off * span, start.Z + (end.Z - start.Z) * along + ux * off * span);
                int bad = Bad(start, bend, end);
                if (bad < least) { least = bad; way = new List<(double X, double Z)> { start, bend, end }; }
            }
        int first = Poles.Count, prev = -1;
        PowerPole last = null;
        bool whole = true;
        for (int leg = 0; leg + 1 < way.Count; leg++)
        {
            var (px, pz) = way[leg];
            var (qx, qz) = way[leg + 1];
            double l = Math.Sqrt((qx - px) * (qx - px) + (qz - pz) * (qz - pz));
            double vx = (qx - px) / l, vz = (qz - pz) / l;
            // Evenly from end to end: the line's last pylon stands at its end, by the substation or the plant.
            int spans = Math.Max(1, (int)Math.Ceiling(l / PylonSpan));
            for (int i = leg == 0 ? 0 : 1; i <= spans; i++)
            {
                double t = l * i / spans;
                // At the spot due, or the nearest that takes a pylon along or to the side of it.
                PowerPole pole = null;
                // The first pass wants ground flat under all four feet; the second takes a steeper spot (the legs reach down to the ground under each foot), never a cliff.
                for (int pass = 0; pass < 3 && pole == null; pass++)
                    foreach (double o in pass < 2 ? new[] { 0.0, 24, -24 } : new[] { 48.0, -48, 80, -80 })
                        foreach (double s in new[] { 0.0, -40, 40, -80, 80, -120, 120 })
                        {
                            double x = px + vx * (t + s) - vz * o, z = pz + vz * (t + s) + vx * o;
                            if (pole != null || last != null && (x - last.X) * (x - last.X) + (z - last.Z) * (z - last.Z) > PylonFarthest * PylonFarthest || !Takes(x, z)) continue;
                            var cand = new PowerPole { X = x, Z = z, Ground = ground(x, z), SideX = (float)-vz, SideZ = (float)vx, Pylon = true };
                            float lo = float.MaxValue, hi = float.MinValue;
                            foreach (var f in cand.FeetXZ()) { float g = ground(f.X, f.Z); lo = Math.Min(lo, g); hi = Math.Max(hi, g); }
                            if (hi - lo > (pass == 0 ? 3.5f : 8f)) continue;
                            pole = cand;
                        }
                if (pole == null) { whole = false; last = null; prev = -1; continue; }
                bool strung = false;
                foreach (float tall in PylonHeights)
                {
                    pole.Tall = tall;
                    if (last == null || (strung = Clears(last, pole, ground, 8f))) break;
                }
                if (last != null && !strung) { whole = false; last = null; prev = -1; }
                pole.Fallen = rng.Chance(0.04f);
                Poles.Add(pole);
                int idx = Poles.Count - 1;
                if (prev >= 0) Pylons(prev, idx, rng);
                else if (idx == first) Links.Add((from, idx));
                prev = idx;
                last = pole;
            }
        }
        if (Poles.Count > first) Legs.Add((first, Poles.Count - 1));
        if (!whole || prev < 0) return false;
        // Into the far end: a plant's own switchyard, or down onto a substation's tall gantry.
        if (nb.Role == PowerRole.Substation)
        {
            if (!Clears(Poles[prev], nb, ground, 3f)) return false;
            Pylons(prev, to, rng);
        }
        else Links.Add((prev, to));
        // A line that starts at a substation lands its first span there too.
        if (na.Role == PowerRole.Substation)
        {
            Links.Remove((from, first));
            if (!Clears(Poles[first], na, ground, 3f)) return false;
            Pylons(first, from, rng);
        }
        return true;
    }

    /// <summary>Strings a pylon span's six wires (from the pylon, when the other end is a substation's gantry) and links the two.</summary>
    void Pylons(int prev, int idx, PoiGenerator.Gen.Rng rng)
    {
        Links.Add((prev, idx));
        PowerPole p0 = Poles[prev], pole = Poles[idx];
        for (int k = 0; k < 6; k++)
        {
            if (!p0.Fallen && !pole.Fallen)
            {
                bool snapped = rng.Chance(0.03f);
                Wires.Add(snapped ? new PowerWire { A = p0.Pylon && (rng.Chance(0.5f) || !pole.Pylon) ? prev : idx, B = 0, K = k, Spanned = false }.Finish(prev, idx)
                    : new PowerWire { A = prev, B = idx, K = k, Slack = 0.02f });
            }
            else if (!p0.Fallen) Wires.Add(new PowerWire { A = prev, B = idx, K = k, Spanned = false });
            else if (!pole.Fallen && pole.Pylon) Wires.Add(new PowerWire { A = idx, B = prev, K = k, Spanned = false });
        }
    }

    /// <summary>
    /// Lays a run of poles beside one route, on one side, and strings its wires. Each pole goes as far
    /// on from the last as its spacing allows while every wire between them still clears the ground
    /// (a hump in the land brings the next pole closer).
    /// </summary>
    void Lay(InfraRoute r, PlanetTerrainSource source, InfraNetwork net, bool pylons, PoiGenerator.Gen.Rng rng, int from = 0, int to = -1)
    {
        if (to < 0) to = r.Count - 1;
        float spacing = pylons ? 110f : 42f, offset = r.HalfWidth + (pylons ? 18f : 3.5f), clearance = pylons ? 7f : 2.5f;
        int side = rng.Chance(0.5f) ? 1 : -1;
        int first = Poles.Count;
        Func<double, double, float> ground = (x, z) => source.SurfaceHeight(Mathx.WrapX(x, net.Width), Mathx.Wrap(z, net.Height));
        // Candidate spots beside the route, one per point: none beside a bridge or tunnel, on a pad, a road bed or in water.
        var spots = new List<PowerPole>();
        for (int i = Math.Max(0, from); i + 1 < r.Count && i <= to; i++)
        {
            double sx = r.X[i + 1] - r.X[i], sz = r.Z[i + 1] - r.Z[i], len = Math.Sqrt(sx * sx + sz * sz);
            if (len < 1e-6) { spots.Add(null); continue; }
            bool ok = r.Span[i] == RoadSpan.Ground && (i == 0 || r.Span[i - 1] == RoadSpan.Ground);
            double nx = -sz / len * side, nz = sx / len * side;
            double px = r.X[i] + nx * offset, pz = r.Z[i] + nz * offset;
            double cx = Mathx.WrapX(px, net.Width), cz = Mathx.Wrap(pz, net.Height);
            if (ok && !PoleSpot(net, source, px, pz)) ok = false;
            spots.Add(ok ? new PowerPole { X = px, Z = pz, Ground = ground(px, pz), SideX = (float)nx, SideZ = (float)nz, Pylon = pylons } : null);
        }
        int prev = -1;
        PowerPole last = null;
        int at = (int)(spacing / 2 / InfraNetwork.Spacing);
        while (at < spots.Count)
        {
            if (spots[at] == null) { at++; last = null; prev = -1; continue; }
            if (last != null)
            {
                // The farthest spot within the spacing whose wires clear the ground; nearer if a hump is in the way.
                int lastAt = at;
                int reach = (int)(spacing / InfraNetwork.Spacing);
                int pick = -1;
                for (int k = Math.Min(spots.Count - 1, lastAt + reach - 1); k >= lastAt + Math.Max(2, reach / 5); k--)
                {
                    if (spots[k] == null) continue;
                    if (Clears(last, spots[k], ground, clearance)) { pick = k; break; }
                }
                if (pick < 0) { last = null; prev = -1; at = lastAt + Math.Max(2, reach / 5); continue; }
                at = pick;
            }
            PowerPole pole = spots[at];
            pole.Fallen = !r.Kept && rng.Chance(pylons ? 0.04f : 0.09f * r.Wear);
            Poles.Add(pole);
            int idx = Poles.Count - 1;
            if (prev >= 0)
            {
                Links.Add((prev, idx));
                PowerPole a = Poles[prev];
                int wires = WiresOn(pole);
                for (int k = 0; k < wires; k++)
                {
                    // Both standing: strung, though now and then a neglected wire has snapped and hangs from one end.
                    if (!a.Fallen && !pole.Fallen)
                    {
                        bool snapped = !r.Kept && rng.Chance(0.06f * r.Wear);
                        Wires.Add(snapped ? new PowerWire { A = rng.Chance(0.5f) ? prev : idx, B = 0, K = k, Spanned = false }.Finish(prev, idx)
                            : new PowerWire { A = prev, B = idx, K = k, Slack = pylons ? 0.02f : 0.03f });
                    }
                    // One fallen: the wires hang from the one still standing.
                    else if (!a.Fallen) Wires.Add(new PowerWire { A = prev, B = idx, K = k, Spanned = false });
                    else if (!pole.Fallen) Wires.Add(new PowerWire { A = idx, B = prev, K = k, Spanned = false });
                }
            }
            prev = idx;
            last = pole;
            at++;
        }
        if (Poles.Count > first) Runs.Add((r, first, Poles.Count - 1));
    }

    /// <summary>Whether every wire strung between two poles clears the ground by a margin all along its sag.</summary>
    bool Clears(PowerPole a, PowerPole b, Func<double, double, float> ground, float clearance)
    {
        int n = WiresOn(a);
        for (int k = 0; k < n; k++)
            foreach (Vector3 p in GalHack.Deco.DecoCables.Span(a.Anchor(k, n), b.Anchor(k, n), a.Pylon ? 0.02f : 0.03f, 16))
                if (p.Y < ground(p.X, p.Z) + clearance) return false;
        return true;
    }

    /// <summary>
    /// A wire's path in world metres: its catenary between two anchors, or hanging straight down from the
    /// one that holds it and then lying on the ground (draped over it) toward where it used to go.
    /// </summary>
    public List<Vector3> Path(PowerWire w, Func<double, double, float> ground)
    {
        PowerPole pa = Poles[w.A], pb = Poles[w.B];
        int n = WiresOn(pa);
        Vector3 a = pa.Anchor(w.K, n);
        if (w.Spanned) return GalHack.Deco.DecoCables.Span(a, pb.Anchor(w.K, n), w.Slack, 16);
        float radius = pa.Pylon ? 0.04f : 0.022f;
        var pts = GalHack.Deco.DecoCables.Drop(a, ground(a.X, a.Z), new Vector3((float)(pb.X - pa.X), 0, (float)(pb.Z - pa.Z)), 2.5f, radius);
        for (int i = 2; i < pts.Count; i++) pts[i] = pts[i] with { Y = ground(pts[i].X, pts[i].Z) + radius };
        return pts;
    }
}

static class PowerWireExtensions
{
    /// <summary>A snapped wire hangs from the pole it kept toward the other.</summary>
    public static PowerWire Finish(this PowerWire w, int a, int b)
    {
        w.B = w.A == a ? b : a;
        return w;
    }
}
