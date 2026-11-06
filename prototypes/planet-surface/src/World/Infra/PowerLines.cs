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
    public float Height => Pylon ? 26f : 9f;

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
/// Power lines, only where they made sense (design doc "Infrastructure"): from the places that
/// needed power (towns, bases, mines and works on worlds that had grids) along the main roads and
/// railways between them, never strung across open country. Wooden poles along roads; steel pylons
/// along highways on modern and high-tech worlds. Every wire is anchored: strung between two
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
        foreach (InfraRoute r in net.Routes)
        {
            // Along roads and highways (railways carry their own catenary or none); a frontier world
            // strung poles only to mines and works.
            if (r.Kind is RouteKind.Track or RouteKind.Rail) continue;
            bool industrial(PoiSite p) => p != null && (p.Family.Tags.Contains("mine") || p.Family.Tags.Contains("industry"));
            if (profile.Before == TechLevel.Low && !industrial(r.From) && !industrial(r.To)) continue;
            if (r.Length < 120) continue;
            bool pylons = r.Kind == RouteKind.Highway && profile.Before != TechLevel.Low;
            lines.Lay(r, source, net, pylons, rng);
        }
        return lines;
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
            if (ok && (source.StampWeight(cx, cz) > 0.05f || source.WaterSurfaceAt(cx, cz, out _))) ok = false;
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
