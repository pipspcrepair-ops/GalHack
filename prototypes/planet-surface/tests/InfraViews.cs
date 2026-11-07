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
        // The views of a ruined town and what runs under it want a start world, which has one.
        // at: a place in the world by its coordinates: infra:at:<archetype>:<seed>:<x>:<z>:<metres back>:<metres up>[:<bearing degrees>[:start]].
        if (what == "at")
        {
            double px = double.Parse(a[4]), pz = double.Parse(a[5]);
            float back = float.Parse(a[6]), up = float.Parse(a[7]), bearing = a.Length > 8 ? float.Parse(a[8]) : 30f;
            if (a.Length > 9) PlanetSurface.Poi.PoiSiting.StartWorld = true;
            return new(PlanetSpec.Roll(seed, arch, 0), m =>
            {
                var w = m.World;
                double vx = px + Mathf.Sin(Mathf.DegToRad(bearing)) * back, vz = pz + Mathf.Cos(Mathf.DegToRad(bearing)) * back;
                float target = w.GroundHeight(Mathx.WrapX(px, w.Width), Mathx.Wrap(pz, w.Height));
                float eye = Math.Max(target + up, w.GroundHeight(Mathx.WrapX(vx, w.Width), Mathx.Wrap(vz, w.Height)) + 3f);
                return Look(w, vx, vz, eye, px, pz, target);
            }, 0.4, WeatherKind.Clear);
        }
        if (what == "pylon") PylonBearing = a.Length > 4 ? float.Parse(a[4]) : 20f;
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

    static float PylonBearing = 20f;

    static (Vector3, float, float) Frame(Main m, string what)
    {
        var w = m.World;
        InfraNetwork net = w.Infra;
        // The standing pylon on the steepest ground (the feet's spread), from the side so the slope shows.
        if (what == "pylon" && net.Power != null)
        {
            Func<double, double, float> gr = (x, z) => w.Source.SurfaceHeight(Mathx.WrapX(x, w.Width), Mathx.Wrap(z, w.Height));
            var p = net.Power.Poles.Where(q => q.Pylon && !q.Fallen).OrderByDescending(q => { var g = q.FeetXZ().Select(f => gr(f.X, f.Z)).ToList(); return g.Max() - g.Min(); }).FirstOrDefault();
            if (p != null)
            {
                var g = p.FeetXZ().Select(f => gr(f.X, f.Z)).ToList();
                GD.Print($"STEEPEST PYLON at {p.X:F0},{p.Z:F0}: feet spread {g.Max() - g.Min():F2} m, height {p.Height}");
                double vx = p.X + Math.Sin(Mathf.DegToRad(PylonBearing)) * 40, vz = p.Z + Math.Cos(Mathf.DegToRad(PylonBearing)) * 40;
                return Look(w, vx, vz, Math.Max(gr(vx, vz) + 3f, p.Ground + 4f), p.X, p.Z, p.Ground + 8f);
            }
        }
        double lx = w.Landing.X, lz = w.Landing.Y;
        double Away(double x, double z) => Math.Sqrt(Math.Pow(Mathx.WrapDelta(x - lx, w.Width), 2) + Math.Pow(Mathx.WrapDelta(z - lz, w.Height), 2));
        // A cave mouth, from out in front of it.
        if (what is "cave" or "cliff" or "gully" or "flank")
        {
            var mouths = w.Source.CaveMouths(lx - 1500, lz - 1500, lx + 1500, lz + 1500).Where(c => what == "cave" || c.Kind == what).OrderBy(c => Away(c.X, c.Z)).ToList();
            if (mouths.Count > 0)
            {
                GalHack.Terrain.CaveMouth c = mouths[0];
                double vx = c.X - c.DirX * 28 + c.DirZ * 9, vz = c.Z - c.DirZ * 28 - c.DirX * 9;
                return Look(w, vx, vz, Math.Max(c.Y + 7f, w.GroundHeight(Mathx.WrapX(vx, w.Width), Mathx.Wrap(vz, w.Height)) + 3f), c.X + c.DirX * 3, c.Z + c.DirZ * 3, c.Y + 1f);
            }
        }
        // A close look at a vehicle in a jam.
        if (what == "wreck-close" && net.Wrecks.Any(wk => wk.Site is "evacuation-jam" or "pile-up"))
        {
            WreckSite site = net.Wrecks.Where(wk => wk.Site is "evacuation-jam" or "pile-up").OrderBy(wk => Away(wk.X, wk.Z)).First();
            GalHack.Deco.DecoPlan plan = PlanetSurface.Infra.InfraDeco.DressSite(site.Site, site.Seed, site.Tech);
            var car = plan.Placements.First(p => p.Piece is "sedan" or "hatchback" or "pickup" or "van" or "old-truck");
            var (cx, cz) = site.World(car.X, car.Z);
            double vx = cx + site.DirZ * 5.5 + site.DirX * 2.5, vz = cz - site.DirX * 5.5 + site.DirZ * 2.5;
            float gy = w.GroundHeight(Mathx.WrapX(cx, w.Width), Mathx.Wrap(cz, w.Height));
            return Look(w, vx, vz, gy + 1.6f, cx, cz, gy + 0.6f);
        }
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
        if (what is "house" or "farmstead" or "shed" or "wayside" or "shop")
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
        // Water beside what's built on the land: a road crossing a river, a place's pad beside water.
        if (what == "crossing")
        {
            var (cross, crossI, crossAway) = ((InfraRoute)null, 0, double.MaxValue);
            foreach (InfraRoute r in net.Routes.Where(r => r.Kind != RouteKind.Rail))
                for (int i = 2; i + 2 < r.Count; i++)
                    if (w.Source.Sampler.Sample(Mathx.WrapX(r.X[i], w.Width), Mathx.Wrap(r.Z[i], w.Height), 2).InRiver && Away(r.X[i], r.Z[i]) < crossAway)
                        (cross, crossI, crossAway) = (r, i, Away(r.X[i], r.Z[i]));
            if (cross != null)
            {
                // From the side of the road and a little above it, along the river.
                double dx = cross.X[crossI + 2] - cross.X[crossI - 2], dz = cross.Z[crossI + 2] - cross.Z[crossI - 2], len = Math.Max(1e-6, Math.Sqrt(dx * dx + dz * dz));
                double vx = cross.X[crossI] - dz / len * 70, vz = cross.Z[crossI] + dx / len * 70;
                return Look(w, vx, vz, cross.Y[crossI] + 16f, cross.X[crossI], cross.Z[crossI], cross.Y[crossI]);
            }
        }
        if (what == "padwater")
        {
            double Water(PlanetSurface.Poi.PoiSite p)
            {
                double near = double.MaxValue;
                double reach = Math.Max(p.Stamp.ReachX, p.Stamp.ReachZ) + 30;
                for (int k = 0; k < 16; k++)
                {
                    double a = k * Math.PI / 8;
                    if (w.Source.WaterSurfaceAt(Mathx.WrapX(p.Center.X + Math.Cos(a) * reach, w.Width), Mathx.Wrap(p.Center.Z + Math.Sin(a) * reach, w.Height), out _)) near = Math.Min(near, k);
                }
                return near;
            }
            var site = w.Pois.Where(p => !p.Family.Space && Water(p) < 16).OrderBy(p => Away(p.Center.X, p.Center.Z)).FirstOrDefault();
            if (site != null)
            {
                double a = Water(site) * Math.PI / 8, reach = Math.Max(site.Stamp.ReachX, site.Stamp.ReachZ) + 80;
                double vx = site.Center.X + Math.Cos(a) * reach, vz = site.Center.Z + Math.Sin(a) * reach;
                return Look(w, vx, vz, site.Stamp.PadY + 24f, site.Center.X, site.Center.Z, site.Stamp.PadY);
            }
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
        // A natural cave mouth near the landing: the generator's own caves, from close by or 300 m off.
        if (what is "cave" or "cavefar")
        {
            for (double r = 60; r < 4000; r += 7)
            {
                double a = r * 0.11, x = lx + Math.Cos(a) * r, z = lz + Math.Sin(a) * r;
                double cx = Mathx.WrapX(x, w.Width), cz = Mathx.Wrap(z, w.Height);
                float g = w.GroundHeight(cx, cz);
                if (g < 3f || w.Source.StampWeight(cx, cz) > 0f) continue;
                // Open a couple of metres under the surface, and more below that: a mouth, not a pit in a wall.
                if (w.Source.CellDensity((int)cx, (int)g - 2, (int)cz) >= 0f || w.Source.CellDensity((int)cx, (int)g - 5, (int)cz) >= 0f) continue;
                double back = what == "cave" ? 15 : 300;
                foreach (int k in Enumerable.Range(0, 8))
                {
                    double b = k * Math.PI / 4, vx = cx + Math.Cos(b) * back, vz = cz + Math.Sin(b) * back;
                    float eye = w.GroundHeight(Mathx.WrapX(vx, w.Width), Mathx.Wrap(vz, w.Height));
                    // From a spot about level with it or above, so the mouth faces the camera.
                    if (eye < g - 2f || (what == "cave" && eye > g + 6f)) continue;
                    return Look(w, vx, vz, eye + (what == "cave" ? 2.5f : 30f), cx, cz, g - 2f);
                }
            }
        }
        // A tunnel mouth in the steepest slope near the landing: from the road, or from 300 m off.
        if (what is "portal" or "portalfar")
        {
            var mouths = net.Structures.Where(st => st.Kind == RoadSpan.Tunnel).SelectMany(st => new[] { (st, st.First, Math.Min(st.Route.Count - 1, st.First + 2)), (st, st.Last, Math.Max(0, st.Last - 2)) })
                .Select(m =>
                {
                    InfraRoute r = m.Item1.Route;
                    double dx = r.X[m.Item3] - r.X[m.Item2], dz = r.Z[m.Item3] - r.Z[m.Item2], len = Math.Max(1e-6, Math.Sqrt(dx * dx + dz * dz));
                    float rise = w.GroundHeight(Mathx.WrapX(r.X[m.Item2] + dx / len * 40, w.Width), Mathx.Wrap(r.Z[m.Item2] + dz / len * 40, w.Height)) - r.Y[m.Item2];
                    return (r, at: m.Item2, ix: dx / len, iz: dz / len, rise);
                }).OrderByDescending(m => m.rise - Away(m.r.X[m.at], m.r.Z[m.at]) * 0.002).ToList();
            if (mouths.Count > 0)
            {
                var (r, at, ix, iz, _) = mouths[0];
                float deck = r.Y[at];
                // Stand on the road itself, whichever way it comes.
                int from = Math.Clamp(at + (r.Span[Math.Min(at, r.Span.Length - 1)] == RoadSpan.Tunnel ? -7 : 7), 0, r.Count - 1);
                if (what == "portal") return Look(w, r.X[from], r.Z[from], r.Y[from] + 3.2f, r.X[at], r.Z[at], deck + 3.5f);
                double vx = r.X[at] - ix * 260 - iz * 150, vz = r.Z[at] - iz * 260 + ix * 150;
                float eye = Math.Max(deck + 40f, w.GroundHeight(Mathx.WrapX(vx, w.Width), Mathx.Wrap(vz, w.Height)) + 12f);
                return Look(w, vx, vz, eye, r.X[at], r.Z[at], deck + 6f);
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
