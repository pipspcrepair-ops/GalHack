using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Terrain;
using Planets.Gen;
using PoiGenerator.Gen;
using PoiSite = PlanetSurface.Poi.PoiSite;

namespace PlanetSurface.Infra;

/// <summary>
/// A site of abandoned vehicles: an evacuation jam or a pile-up on a highway, a roadblock, a car
/// park by a town, a train standing derelict or derailed on its line, an airliner crashed in a
/// field. Placed in the deco library's site frame: its north-west corner at (X, Z), its length (+z
/// in the site) along <see cref="DirX"/>, <see cref="DirZ"/>, its lane on the road's centre line, so
/// the road stays passable.
/// </summary>
public sealed class WreckSite
{
    public string Site = "";
    public double X, Z;        // world metres of the site's north-west corner (any copy)
    public float Y;            // the ground's height at the lane's start
    public float DirX, DirZ;   // the site's +z (along the road), unit
    public float Width, Length, LaneX, Lane;
    public TechLevel Tech;
    public ulong Seed;
    public InfraRoute Route;   // the road or line it stands on (null for a field)
    /// <summary>Which of the route's points it covers (a train's own track replaces the line's there).</summary>
    public int First = -1, Last = -1;

    /// <summary>The x in the site's frame that lies on the route's centre line: the lane on a road, the track under a train.</summary>
    public static float AlignX(string site) => site switch
    {
        "derelict-train" => 12 / 2f + 1.5f,
        "derailed-train" => 9f,
        _ => Wrecks.Size(site).LaneX,
    };

    /// <summary>Degrees about +y that turn the site's +z onto its direction (Godot: +z rotated by yaw).</summary>
    public float Yaw => MathF.Atan2(DirX, DirZ) * 180f / MathF.PI;

    /// <summary>A point in the site's frame (x across, z along) in world metres.</summary>
    public (double X, double Z) World(float sx, float sz)
    {
        // The site's +x is its +z turned a quarter clockwise seen from above (east of south-running).
        float ax = DirZ, az = -DirX;
        return (X + ax * sx + DirX * sz, Z + az * sx + DirZ * sz);
    }
}

/// <summary>
/// Where the abandoned vehicles are (design doc "Infrastructure"): evacuation jams nose to tail on
/// highways out of cities, rusted pile-ups, roadblocks before the places soldiers held, car parks
/// by towns, derelict and derailed trains on straight stretches of line, an airliner down in a field
/// on a world that flew them. Every site is laid on a straight, nearly level stretch with its lane
/// down the road, so the way stays open; none on a place's pad. Deterministic from the planet seed.
/// </summary>
public static class Wrecks
{
    /// <summary>The site's width, length and lane (from the deco library's deco-arrangements.json), and where its lane runs across it.</summary>
    public static (float W, float L, float Lane, float LaneX) Size(string site) => site switch
    {
        "evacuation-jam" => (16, 60, 3.2f, 6f),
        "pile-up" => (16, 40, 3.2f, 6f),
        "roadblock" => (16, 30, 3.2f, 6f),
        "car-park" => (26, 30, 3.2f, 13f),
        "derelict-train" => (12, 60, 1.2f, 1.6f),
        "derailed-train" => (30, 60, 1.2f, 1.6f),
        "airliner-crash" => (44, 70, 3.0f, 3f),
        _ => (16, 40, 3.2f, 8f),
    };

    public static List<WreckSite> Plan(InfraNetwork net, PlanetTerrainSource source, IReadOnlyList<PoiSite> pois, ulong seed)
    {
        var sites = new List<WreckSite>();
        WorldProfile profile = net.Profile;
        if (profile == null || !profile.Inhabited) return sites;
        var rng = new PoiGenerator.Gen.Rng(PoiGenerator.Gen.Hash.Combine(seed, 0x3EC5UL));
        // A start town's main street: a stalled jam of rusted cars down it.
        if (net.Town?.Streets.FirstOrDefault() is InfraRoute main)
        {
            bool laid = false;
            foreach (string kind in new[] { "evacuation-jam", "pile-up", "roadblock" })
                for (int i = 4; i + 8 < main.Count && !laid; i += 2)
                    if (TryOnRoute(main, i, kind, source, net, rng, profile.Before, out WreckSite jam, steep: 0.12f, town: true)) { sites.Add(jam); laid = true; }
        }
        // A city's avenues: traffic stalled down the widest of them where it was left.
        if (net.City is { } city)
            foreach (InfraRoute avenue in city.Streets.Where(s => s.HalfWidth >= CityPlan.Avenue).OrderByDescending(s => s.Count).Take(4))
            {
                bool laid = false;
                foreach (string kind in new[] { "evacuation-jam", "pile-up" })
                    for (int i = 6; i + 10 < avenue.Count && !laid; i += 3)
                        if (TryOnRoute(avenue, i, kind, source, net, rng, profile.Before, out WreckSite jam, steep: 0.12f, town: true) && !Overlaps(sites, jam, net)) { sites.Add(jam); laid = true; }
            }
        // An exodus: every road to a port jammed, thicker the nearer the port.
        if (profile.Exodus || net.RegionPlan?.Roll.Road == "jammed") ExodusJams(net, source, sites, rng);
        foreach (InfraRoute r in net.Routes)
        {
            if (r.Kind == RouteKind.Track) continue;
            if (r.Kind == RouteKind.Rail && r.Maglev) continue;
            // How often: highways out of cities jam; roads now and then; lines carry a train every few kilometres.
            float every = r.Kind switch { RouteKind.Highway => 320f, RouteKind.Road => profile.Before == TechLevel.Low ? 1200f : 600f, _ => 2400f };
            double next = every * (0.3 + 0.7 * rng.Float());
            double run = 0;
            for (int i = 0; i + 1 < r.Count; i++)
            {
                run += InfraNetwork.Spacing;
                if (run < next) continue;
                string site = r.Kind switch
                {
                    RouteKind.Rail => rng.Chance(0.35f) ? "derailed-train" : "derelict-train",
                    RouteKind.Highway => rng.Chance(0.25f) ? "pile-up" : "evacuation-jam",
                    _ => NearHeld(r, i, net) ? "roadblock" : rng.Chance(0.5f) ? "pile-up" : "evacuation-jam",
                };
                if (TryOnRoute(r, i, site, source, net, rng, profile.Before, out WreckSite w) && !Overlaps(sites, w, net))
                {
                    sites.Add(w);
                    next = run + every * (0.6 + 0.8 * rng.Float());
                }
            }
        }
        // Car parks by towns and cities, beside the road where it leaves the gate.
        foreach (InfraRoute r in net.Routes.Where(r => r.Kind is RouteKind.Road or RouteKind.Highway && profile.Before != TechLevel.Low))
            foreach (PoiSite p in new[] { r.From, r.To })
            {
                if (p == null || p.Tier is not (PoiTier.Town or PoiTier.City or PoiTier.Spaceport) || !rng.Chance(0.6f)) continue;
                int i = p == r.From ? Math.Min(r.Count - 2, 14) : Math.Max(0, r.Count - 16);
                if (TryBeside(r, i, "car-park", source, net, rng, profile.Before, out WreckSite w) && !Overlaps(sites, w, net)) sites.Add(w);
            }
        // An airliner down in a field, on a world that flew them: open, level ground clear of roads and places.
        if (profile.Before != TechLevel.Low)
        {
            int want = 1 + (profile.Population >= 7 ? 1 : 0);
            for (int tries = 0; tries < 400 && want > 0; tries++)
            {
                double x = rng.Float() * net.Width, z = rng.Float() * net.Height;
                float yaw = rng.Float() * MathF.Tau;
                var w = Field("airliner-crash", x, z, MathF.Sin(yaw), MathF.Cos(yaw), source, rng, profile.Before);
                if (w == null || Overlaps(sites, w, net) || net.Nearest(x, z, 140, out _, out _) != null) continue;
                if (pois.Any(p => Math.Abs(Mathx.WrapDelta(p.Center.X - x, net.Width)) < 260 && Math.Abs(Mathx.WrapDelta(p.Center.Z - z, net.Height)) < 260)) continue;
                sites.Add(w);
                want--;
            }
        }
        return sites;
    }

    /// <summary>Within a short drive of a place soldiers held: roadblocks go up on the road in.</summary>
    static bool NearHeld(InfraRoute r, int i, InfraNetwork net) =>
        new[] { r.From, r.To }.Any(p => p != null && (p.FactionHeld || p.Family.Tags.Contains("military"))
            && Math.Sqrt(Math.Pow(Mathx.WrapDelta(p.Center.X - r.X[i], net.Width), 2) + Math.Pow(Mathx.WrapDelta(p.Center.Z - r.Z[i], net.Height), 2)) < 1200);

    /// <summary>A site laid on the route from point i: straight and nearly level for its length, all on the ground, off every pad.</summary>
    /// <summary>Whether people tried to leave from here: a starport of any class, a scrappy field.</summary>
    public static bool IsPort(PoiSite p) => p != null && (p.Tier == PoiTier.Spaceport || p.Family.Tags.Contains("scrappy") || p.Family.Tags.Contains("exodus"));

    /// <summary>
    /// The roads people tried to leave by: walking out from each port along every road that reaches it,
    /// a jam of abandoned vehicles every so often, the gap growing with the distance from the port.
    /// </summary>
    static void ExodusJams(InfraNetwork net, PlanetTerrainSource source, List<WreckSite> sites, PoiGenerator.Gen.Rng rng)
    {
        const double Reach = 2400;
        foreach (InfraRoute r in net.Routes)
        {
            if (r.Kind == RouteKind.Rail) continue;
            foreach (bool fromStart in new[] { true, false })
            {
                if (!IsPort(fromStart ? r.From : r.To)) continue;
                double next = 50, run = 0;
                for (int k = 2; k + 12 < r.Count && run < Reach; k++)
                {
                    run += InfraNetwork.Spacing;
                    if (run < next) continue;
                    int i = fromStart ? k : r.Count - 1 - k - 8;
                    if (i < 0 || !TryOnRoute(r, i, "evacuation-jam", source, net, rng, net.Profile.Before, out WreckSite jam) || Overlaps(sites, jam, net)) continue;
                    sites.Add(jam);
                    next = run + 70 + run * 0.45;
                }
            }
        }
    }

    static bool TryOnRoute(InfraRoute r, int i, string site, PlanetTerrainSource source, InfraNetwork net, PoiGenerator.Gen.Rng rng, TechLevel tech, out WreckSite w, float steep = 0.04f, bool town = false)
    {
        w = null;
        var (W, L, lane, laneX) = Size(site);
        int n = (int)Math.Ceiling(L / InfraNetwork.Spacing);
        if (i + n >= r.Count) return false;
        for (int k = i; k < i + n; k++) if (r.Span[k] != RoadSpan.Ground) return false;
        double dx = r.X[i + n] - r.X[i], dz = r.Z[i + n] - r.Z[i], len = Math.Sqrt(dx * dx + dz * dz);
        if (len < L * 0.97) return false;   // a bend
        if (Math.Abs(r.Y[i + n] - r.Y[i]) > L * steep) return false;
        float ux = (float)(dx / len), uz = (float)(dz / len);
        // The lane on the road's centre line (a train's track on the line's): the site's corner is that far to the side of it.
        float ax = uz, az = -ux, align = WreckSite.AlignX(site);
        w = new WreckSite
        {
            Site = site, X = r.X[i] - ax * align, Z = r.Z[i] - az * align, Y = r.Y[i], DirX = ux, DirZ = uz,
            Width = W, Length = L, Lane = lane, LaneX = laneX, Tech = tech, Seed = rng.NextU64(), Route = r, First = i, Last = i + n,
        };
        // A town street runs between its houses' lots: the cars stay on it, the lots are the street's own.
        return town || OffPads(w, source, net);
    }

    /// <summary>A site beside the route at point i (a car park off to one side, its lane parallel to the road).</summary>
    static bool TryBeside(InfraRoute r, int i, string site, PlanetTerrainSource source, InfraNetwork net, PoiGenerator.Gen.Rng rng, TechLevel tech, out WreckSite w)
    {
        w = null;
        var (W, L, lane, laneX) = Size(site);
        int j = Math.Min(r.Count - 1, i + 2);
        double dx = r.X[j] - r.X[i], dz = r.Z[j] - r.Z[i], len = Math.Sqrt(dx * dx + dz * dz);
        if (len < 1e-3) return false;
        float ux = (float)(dx / len), uz = (float)(dz / len), ax = uz, az = -ux;
        int side = rng.Chance(0.5f) ? 1 : -1;
        double off = r.HalfWidth + 3 + (side > 0 ? 0 : W);
        double cx = r.X[i] + ax * off * side, cz = r.Z[i] + az * off * side;
        w = Field(site, cx, cz, ux, uz, source, rng, tech);
        if (w == null) return false;
        w.Route = r;
        return true;
    }

    /// <summary>A site on open ground with its corner at a point: level enough (within 1.2 m over it), dry, off every pad and road bed.</summary>
    static WreckSite Field(string site, double x, double z, float ux, float uz, PlanetTerrainSource source, PoiGenerator.Gen.Rng rng, TechLevel tech)
    {
        var (W, L, lane, laneX) = Size(site);
        var w = new WreckSite { Site = site, X = x, Z = z, DirX = ux, DirZ = uz, Width = W, Length = L, Lane = lane, LaneX = laneX, Tech = tech, Seed = rng.NextU64() };
        float lo = float.MaxValue, hi = float.MinValue;
        for (float sz = 0; sz <= L; sz += L / 4)
        for (float sx = 0; sx <= W; sx += W / 4)
        {
            var (px, pz) = w.World(sx, sz);
            double cx = Mathx.WrapX(px, source.Width), cz = Mathx.Wrap(pz, source.Height);
            if (source.StampWeight(cx, cz) > 0f || source.WaterSurfaceAt(cx, cz, out _)) return null;
            float h = source.SurfaceHeight(cx, cz);
            lo = Math.Min(lo, h);
            hi = Math.Max(hi, h);
        }
        if (hi - lo > (site == "airliner-crash" ? 3.5f : 2f) || lo < 1f) return null;
        w.Y = lo;
        return w;
    }

    static bool OffPads(WreckSite w, PlanetTerrainSource source, InfraNetwork net)
    {
        for (float sz = 0; sz <= w.Length; sz += 4)
        for (float sx = 0; sx <= w.Width; sx += 4)
        {
            var (px, pz) = w.World(sx, sz);
            double cx = Mathx.WrapX(px, net.Width), cz = Mathx.Wrap(pz, net.Height);
            if (TerrainStamp.OnPad(source.Stamps, source.StampsAt(cx, cz), cx, cz) || source.WaterSurfaceAt(cx, cz, out _)) return false;
        }
        return true;
    }

    static bool Overlaps(List<WreckSite> sites, WreckSite w, InfraNetwork net)
    {
        var (mx, mz) = w.World(w.Width / 2, w.Length / 2);
        foreach (WreckSite o in sites)
        {
            var (ox, oz) = o.World(o.Width / 2, o.Length / 2);
            double dx = Mathx.WrapDelta(mx - ox, net.Width), dz = Mathx.WrapDelta(mz - oz, net.Height);
            if (Math.Sqrt(dx * dx + dz * dz) < (w.Length + o.Length) * 0.5 + 20) return true;
        }
        return false;
    }
}
