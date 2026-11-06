using System;
using System.Linq;
using GalHack.Terrain;
using Godot;
using Planets.Gen;
using PlanetSurface.Infra;
using Weather.Model;
using Archetype = Planets.Gen.Archetype;

namespace PlanetSurface;

/// <summary>
/// Screenshot views of the infrastructure: infra:&lt;what&gt;:&lt;archetype&gt;[:&lt;seed&gt;] finds the nearest
/// thing of its kind to the landing site and frames it. what: road, highway, track, rail, bridge,
/// viaduct (a railway bridge) or tunnel.
/// </summary>
public static class InfraViews
{
    public static Screenshot.ViewSpec Get(string name)
    {
        string[] a = name.Split(':');
        if (a.Length < 3) return null;
        var arch = Enum.Parse<Archetype>(a[2], true);
        ulong seed = a.Length > 3 ? ulong.Parse(a[3]) : 61;
        string what = a[1];
        return new(PlanetSpec.Roll(seed, arch, 0), m => Frame(m, what), 0.4, WeatherKind.Clear);
    }

    static (Vector3, float, float) Frame(Main m, string what)
    {
        var w = m.World;
        InfraNetwork net = w.Infra;
        double lx = w.Landing.X, lz = w.Landing.Y;
        double Away(double x, double z) => Math.Sqrt(Math.Pow(Mathx.WrapDelta(x - lx, w.Width), 2) + Math.Pow(Mathx.WrapDelta(z - lz, w.Height), 2));
        // A wreck site: evacuation-jam, pile-up, roadblock, car-park, derelict-train, derailed-train, airliner-crash.
        if (net.Wrecks.Any(wk => wk.Site == what))
        {
            WreckSite site = net.Wrecks.Where(wk => wk.Site == what).OrderBy(wk => Away(wk.X, wk.Z)).First();
            var (sx, sz) = site.World(site.LaneX - 2f, -18f);
            var (tx, tz) = site.World(site.Width / 2, site.Length * 0.55f);
            float eye = w.GroundHeight(Mathx.WrapX(sx, w.Width), Mathx.Wrap(sz, w.Height)) + (what == "airliner-crash" ? 14f : 6f);
            return Look(w, sx, sz, eye, tx, tz, w.GroundHeight(Mathx.WrapX(tx, w.Width), Mathx.Wrap(tz, w.Height)) + 1f);
        }
        // A structure: the nearest of its kind.
        if (what is "bridge" or "viaduct" or "tunnel")
        {
            RoadSpan kind = what == "tunnel" ? RoadSpan.Tunnel : RoadSpan.Bridge;
            var s = net.Structures.Where(st => st.Kind == kind && (what != "viaduct" || st.Route.Kind == RouteKind.Rail) && (what == "viaduct" || st.Route.Kind != RouteKind.Rail) && !st.Route.Maglev)
                .OrderBy(st => Away(st.Route.X[(st.First + st.Last) / 2], st.Route.Z[(st.First + st.Last) / 2])).FirstOrDefault();
            if (s != null)
            {
                InfraRoute r = s.Route;
                if (kind == RoadSpan.Tunnel)
                {
                    // On the road before the portal, looking at it.
                    int at = Math.Max(0, s.First - 12), to = s.First + 2;
                    return Look(w, r.X[at], r.Z[at], r.Y[at] + 3f, r.X[to], r.Z[to], r.Y[to] + 2f);
                }
                // From the side, at the deck's height, far enough back to see its length.
                int mid = (s.First + s.Last) / 2;
                double dx = r.X[Math.Min(r.Count - 1, mid + 1)] - r.X[Math.Max(0, mid - 1)], dz = r.Z[Math.Min(r.Count - 1, mid + 1)] - r.Z[Math.Max(0, mid - 1)];
                double len = Math.Max(1e-6, Math.Sqrt(dx * dx + dz * dz));
                double back = Math.Clamp(s.Length * 0.8, 60, 260);
                double cx = r.X[mid] - dz / len * back, cz = r.Z[mid] + dx / len * back;
                float eye = Math.Max(w.GroundHeight(Mathx.WrapX(cx, w.Width), Mathx.Wrap(cz, w.Height)) + 2f, r.Y[mid] - 2f);
                return Look(w, cx, cz, eye, r.X[mid], r.Z[mid], r.Y[mid] - 4f);
            }
        }
        // A route: the nearest ground stretch of its kind, looking along it from a little above the deck.
        RouteKind want = what switch { "highway" => RouteKind.Highway, "track" => RouteKind.Track, "rail" => RouteKind.Rail, _ => RouteKind.Road };
        InfraRoute best = null;
        int bestI = 0;
        double bestD = double.MaxValue;
        foreach (InfraRoute r in net.Routes.Where(r => r.Kind == want || what == "road" && r.Kind == RouteKind.Highway))
            for (int i = 10; i + 10 < r.Count; i += 5)
            {
                if (r.Span[i] != RoadSpan.Ground || r.Span[i + 8] != RoadSpan.Ground) continue;
                double d = Away(r.X[i], r.Z[i]);
                if (d < bestD) { bestD = d; best = r; bestI = i; }
            }
        if (best == null) return (new Vector3((float)lx, w.GroundHeight(lx, lz) + 20f, (float)lz), 0f, -8f);
        int ahead = Math.Min(best.Count - 1, bestI + 14);
        return Look(w, best.X[bestI], best.Z[bestI], best.Y[bestI] + 2.2f, best.X[ahead], best.Z[ahead], best.Y[ahead] + 1f);
    }

    static (Vector3, float, float) Look(World.PlanetWorld w, double x, double z, float y, double tx, double tz, float ty)
    {
        double cx = Mathx.WrapX(x, w.Width), cz = Mathx.Wrap(z, w.Height);
        double dx = Mathx.WrapDelta(tx - x, w.Width), dz = Mathx.WrapDelta(tz - z, w.Height);
        float heading = Mathf.RadToDeg(MathF.Atan2((float)dx, -(float)dz));
        float pitch = Mathf.RadToDeg(MathF.Atan2(ty - y, (float)Math.Sqrt(dx * dx + dz * dz)));
        return (new Vector3((float)cx, y, (float)cz), heading, pitch);
    }
}
