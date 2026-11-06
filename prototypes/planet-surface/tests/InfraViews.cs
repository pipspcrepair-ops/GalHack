using System;
using System.Linq;
using GalHack.Terrain;
using Godot;
using Planets.Gen;
using PlanetSurface.Infra;
using PoiGenerator.Gen;
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
        bool town = what is "town" or "sewer" or "drain" or "metro" or "culvert" or "gallery";
        if (town) PlanetSurface.Poi.PoiSiting.StartWorld = true;
        Action<Main> torch = what is "sewer" or "drain" or "metro" or "gallery" ? Torch : null;
        return new(PlanetSpec.Roll(seed, arch, 0), m => Frame(m, what), 0.4, WeatherKind.Clear, BeforeCapture: torch);
    }

    /// <summary>What you'd have with you down there: a soft light at the eye (it's black underground without one).</summary>
    static void Torch(Main m) => m.GetViewport().GetCamera3D()?.AddChild(new OmniLight3D
    {
        LightColor = new Color("ffeeda"), LightEnergy = 5f, OmniRange = 48f, OmniAttenuation = 0.8f, ShadowEnabled = false, Position = new Vector3(0.2f, -0.2f, 0f),
    });

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
        // A flavour ruin: house, farmstead, shed or wayside, from 30 m off.
        if (what is "house" or "farmstead" or "shed" or "wayside")
        {
            FlavourRuin ruin = net.Ruins.Where(r => r.Family.Tags.Contains(what)).OrderBy(r => Away(r.Center.X, r.Center.Z)).FirstOrDefault();
            if (ruin != null)
            {
                var (rx, rz) = ruin.Center;
                double back = Math.Max(ruin.Stamp.MaxX - ruin.Stamp.MinX, ruin.Stamp.MaxZ - ruin.Stamp.MinZ) * 1.1 + 14;
                double vx = rx + back * 0.7, vz = rz + back * 0.7;
                return Look(w, vx, vz, w.GroundHeight(Mathx.WrapX(vx, w.Width), Mathx.Wrap(vz, w.Height)) + 4f, rx, rz, ruin.OriginY + 3f);
            }
        }
        // An ore outcrop: the nearest deposit breaking the surface, from a few metres off.
        if (what == "ore")
        {
            var d = w.Map.Deposits.InRect(0, 0, w.Width, w.Height).Where(dp => dp.Depth <= 0.01f && w.Source.StampWeight(dp.X, dp.Y) <= 0f)
                .OrderBy(dp => Away(dp.X, dp.Y)).FirstOrDefault();
            double vx = d.X + 5, vz = d.Y + 5;
            return Look(w, vx, vz, w.GroundHeight(Mathx.WrapX(vx, w.Width), Mathx.Wrap(vz, w.Height)) + 1.8f, d.X, d.Y, w.GroundHeight(d.X, d.Y) + 0.5f);
        }
        // A ruined town and what runs under it.
        if (w.StartTown is StartTown town)
        {
            UnderTown under = town.Under;
            if (what == "town")
            {
                // Standing at the edge, looking back down the main street into the town.
                return Look(w, town.EdgeX, town.EdgeZ, w.GroundHeight(town.EdgeX, town.EdgeZ) + 2.2f, town.CenterX, town.CenterZ, w.GroundHeight(Mathx.WrapX(town.CenterX, w.Width), Mathx.Wrap(town.CenterZ, w.Height)) + 3f);
            }
            if (under != null && what == "sewer" && under.Scheme.Rooms.FirstOrDefault(r => r.Kind == UnderSpaceKind.Junction && r.Network == UnderKind.Sewer) is UnderRoom room)
            {
                // A corner of a junction chamber, looking across it.
                return Look(w, room.Min.X + 1.5, room.Max.Z - 0.5, room.Min.Y + 2.6f, room.Max.X, room.Min.Z, room.Min.Y + 1f);
            }
            if (under != null && what == "drain" && under.Scheme.Runs.FirstOrDefault(r => r.Kind == UnderKind.Drain) is UnderRun run)
            {
                var (ax, az) = run.Column(run.Length / 2);
                var (bx, bz) = run.Column(run.Length / 2 + 20);
                return Look(w, ax + 0.5, az + 0.5, run.Floors[run.Length / 2] + 1.6f, bx + 0.5, bz + 0.5, run.Floors[run.Length / 2 + 20] + 1.2f);
            }
            if (under != null && what == "metro" && under.Scheme.Stations.FirstOrDefault() is UnderStation st)
            {
                // Down the platform from near one end.
                double ax = st.X - st.DirX * (st.HalfLength - 6), az = st.Z - st.DirZ * (st.HalfLength - 6), bx = st.X + st.DirX * 10, bz = st.Z + st.DirZ * 10;
                int sideX = Math.Abs(st.DirZ), sideZ = Math.Abs(st.DirX);
                return Look(w, ax + sideX * 1.5 + 0.5, az + sideZ * 1.5 + 0.5, st.Y + 2.8f, bx + 0.5, bz + 0.5, st.Y + 2.2f);
            }
            if (under != null && what == "culvert" && under.Scheme.Outfalls.FirstOrDefault() is UnderOutfall o)
            {
                // Out on the apron, looking back at the mouth.
                double vx = o.X + o.DirX * 13 + 0.5 + 2 * Math.Abs(o.DirZ), vz = o.Z + o.DirZ * 13 + 0.5 + 2 * Math.Abs(o.DirX);
                return Look(w, vx, vz, o.Y + 2.6f, o.X + 0.5, o.Z + 0.5, o.Y + 1.6f);
            }
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
