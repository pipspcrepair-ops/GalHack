using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GalHack.Terrain;
using Planets.Gen;
using PoiGenerator.Gen;
using PoiSite = PlanetSurface.Poi.PoiSite;

namespace PlanetSurface.Infra;

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
    public float Height => Pylon ? Tall : 9f;

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
        float across = (k - (wires - 1) * 0.5f) * (Pylon ? 4.2f : 0.85f);
        float y = Ground + Height - (Pylon ? 2f + (k % 2) * 4f : 0.25f);
        return new Vector3((float)X + SideX * across, y, (float)Z + SideZ * across);
    }
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
        if (net.RegionPlan != null)
        {
            foreach (PlannedLink l in net.RegionPlan.Links.Where(l => l.Kind == "power"))
                if (net.RegionPlan.Places[l.A].Seated is PoiSite a && net.RegionPlan.Places[l.B].Seated is PoiSite b) lines.Line(a, b, net, source, rng);
        }
        else if (profile.Before != TechLevel.Low) lines.Grid(net, source, rng);
        return lines;
    }

    /// <summary>The pylons' legs, as polylines of pylons (for tests).</summary>
    public readonly List<(int FirstPole, int LastPole)> Legs = new();

    const float PylonSpan = 300f, PylonFarthest = 440f;
    static readonly float[] PylonHeights = { 30f, 36f, 42f, 50f };

    /// <summary>The places that needed power, joined by the shortest lines that reach them all.</summary>
    void Grid(InfraNetwork net, PlanetTerrainSource source, PoiGenerator.Gen.Rng rng)
    {
        var nodes = net.Places.Where(p => InfraNetwork.Rank(p) >= 2).OrderByDescending(InfraNetwork.Rank).Take(9).ToList();
        if (nodes.Count < 2) return;
        double Dist(PoiSite a, PoiSite b)
        {
            double dx = Mathx.WrapDelta(a.Center.X - b.Center.X, net.Width), dz = Mathx.WrapDelta(a.Center.Z - b.Center.Z, net.Height);
            return Math.Sqrt(dx * dx + dz * dz);
        }
        var joined = new List<PoiSite> { nodes[0] };
        var left = nodes.Skip(1).ToList();
        while (left.Count > 0)
        {
            (PoiSite From, PoiSite To) next = default;
            double best = double.MaxValue;
            foreach (PoiSite a in joined)
                foreach (PoiSite b in left)
                    if (Dist(a, b) < best) { best = Dist(a, b); next = (a, b); }
            left.Remove(next.To);
            joined.Add(next.To);
            if (best < 7000) Line(next.From, next.To, net, source, rng);
        }
    }

    /// <summary>One line between two places: out from each, straight across the land, or by one bend where the land asks for it.</summary>
    void Line(PoiSite a, PoiSite b, InfraNetwork net, PlanetTerrainSource source, PoiGenerator.Gen.Rng rng)
    {
        Func<double, double, float> ground = (x, z) => source.SurfaceHeight(Mathx.WrapX(x, net.Width), Mathx.Wrap(z, net.Height));
        double ax = a.Center.X, az = a.Center.Z;
        double dx = Mathx.WrapDelta(b.Center.X - ax, net.Width), dz = Mathx.WrapDelta(b.Center.Z - az, net.Height), len = Math.Sqrt(dx * dx + dz * dz);
        if (len < 400) return;
        double ux = dx / len, uz = dz / len;
        double reachA = Math.Max(a.Stamp.ReachX, a.Stamp.ReachZ) + 30, reachB = Math.Max(b.Stamp.ReachX, b.Stamp.ReachZ) + 30;
        (double X, double Z) start = (ax + ux * reachA, az + uz * reachA), end = (ax + ux * (len - reachB), az + uz * (len - reachB));
        // Spots along a stretch, and how many of them won't take a pylon: pick the straight, or the bend that does better.
        bool Takes(double x, double z)
        {
            double cx = Mathx.WrapX(x, net.Width), cz = Mathx.Wrap(z, net.Height);
            if (source.StampWeight(cx, cz) > 0.05f || source.WaterSurfaceAt(cx, cz, out _)) return false;
            // A pylon's whole footing stands clear of every road and railway, shoulders and junctions too.
            // (A 50 m pylon's feet stand 9.2 m from its middle: the margin covers the tallest.)
            if (!net.OffRoad(x, z, 11)) return false;
            float h = ground(x, z);
            return MathF.Abs(ground(x + 8, z) - h) < 7f && MathF.Abs(ground(x, z + 8) - h) < 7f;
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
        foreach (double along in new[] { 0.35, 0.65 })
            foreach (double off in new[] { 0.14, -0.14, 0.25, -0.25 })
            {
                (double X, double Z) bend = (start.X + (end.X - start.X) * along - uz * off * len, start.Z + (end.Z - start.Z) * along + ux * off * len);
                int bad = Bad(start, bend, end);
                if (bad < least) { least = bad; way = new List<(double X, double Z)> { start, bend, end }; }
            }
        int first = Poles.Count, prev = -1;
        PowerPole last = null;
        for (int leg = 0; leg + 1 < way.Count; leg++)
        {
            var (px, pz) = way[leg];
            var (qx, qz) = way[leg + 1];
            double l = Math.Sqrt((qx - px) * (qx - px) + (qz - pz) * (qz - pz));
            double vx = (qx - px) / l, vz = (qz - pz) / l;
            double t = leg == 0 ? 0 : Math.Min(PylonSpan * 0.5, l * 0.5);
            while (t < l)
            {
                // At the spot due, or the nearest that takes a pylon along or to the side of it.
                PowerPole pole = null;
                // The first pass wants ground flat under all four feet; the second takes a steeper spot (the legs reach down to the ground under each foot), never a cliff.
                for (int pass = 0; pass < 2 && pole == null; pass++)
                    foreach (double o in new[] { 0.0, 24, -24 })
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
                if (pole == null) { t += PylonSpan * 0.5; if (last != null && t > 0) { last = null; prev = -1; } continue; }
                bool strung = false;
                foreach (float tall in PylonHeights)
                {
                    pole.Tall = tall;
                    if (last == null || (strung = Clears(last, pole, ground, 8f))) break;
                }
                if (last != null && !strung) { last = null; prev = -1; }
                pole.Fallen = rng.Chance(0.04f);
                Poles.Add(pole);
                int idx = Poles.Count - 1;
                if (prev >= 0)
                    for (int k = 0; k < WiresOn(pole); k++)
                    {
                        PowerPole p0 = Poles[prev];
                        if (!p0.Fallen && !pole.Fallen)
                        {
                            bool snapped = rng.Chance(0.03f);
                            Wires.Add(snapped ? new PowerWire { A = rng.Chance(0.5f) ? prev : idx, B = 0, K = k, Spanned = false }.Finish(prev, idx)
                                : new PowerWire { A = prev, B = idx, K = k, Slack = 0.02f });
                        }
                        else if (!p0.Fallen) Wires.Add(new PowerWire { A = prev, B = idx, K = k, Spanned = false });
                        else if (!pole.Fallen) Wires.Add(new PowerWire { A = idx, B = prev, K = k, Spanned = false });
                    }
                prev = idx;
                last = pole;
                t += PylonSpan * (0.9 + 0.2 * rng.Float());
            }
        }
        if (Poles.Count > first) Legs.Add((first, Poles.Count - 1));
    }

    /// <summary>
    /// Lays a run of poles beside one route, on one side, and strings its wires. Each pole goes as far
    /// on from the last as its spacing allows while every wire between them still clears the ground
    /// (a hump in the land brings the next pole closer).
    /// </summary>
    void Lay(InfraRoute r, PlanetTerrainSource source, InfraNetwork net, bool pylons, PoiGenerator.Gen.Rng rng)
    {
        float spacing = pylons ? 110f : 42f, offset = r.HalfWidth + (pylons ? 18f : 3.5f), clearance = pylons ? 7f : 2.5f;
        int side = rng.Chance(0.5f) ? 1 : -1;
        int first = Poles.Count;
        Func<double, double, float> ground = (x, z) => source.SurfaceHeight(Mathx.WrapX(x, net.Width), Mathx.Wrap(z, net.Height));
        // Candidate spots beside the route, one per point: none beside a bridge or tunnel, on a pad, a road bed or in water.
        var spots = new List<PowerPole>();
        for (int i = 0; i + 1 < r.Count; i++)
        {
            double sx = r.X[i + 1] - r.X[i], sz = r.Z[i + 1] - r.Z[i], len = Math.Sqrt(sx * sx + sz * sz);
            if (len < 1e-6) continue;
            bool ok = r.Span[i] == RoadSpan.Ground && (i == 0 || r.Span[i - 1] == RoadSpan.Ground);
            double nx = -sz / len * side, nz = sx / len * side;
            double px = r.X[i] + nx * offset, pz = r.Z[i] + nz * offset;
            double cx = Mathx.WrapX(px, net.Width), cz = Mathx.Wrap(pz, net.Height);
            if (ok && (source.StampWeight(cx, cz) > 0.05f || source.WaterSurfaceAt(cx, cz, out _) || !net.OffRoad(px, pz, 1.5))) ok = false;
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
