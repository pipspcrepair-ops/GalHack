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
        if (what == "roads")
        {
            PlanetSurface.Poi.PoiSiting.StartWorld = true;
            return new(PlanetSpec.Roll(seed, arch, 0), m => default, 0.4, WeatherKind.Clear, Series: RoadSeries);
        }
        // The tracks and dirt roads, close and from the air; "ways-wet" the same after rain (clear sky, wet ground).
        if (what is "ways" or "ways-wet")
        {
            PlanetSurface.Poi.PoiSiting.StartWorld = true;
            return new(PlanetSpec.Roll(seed, arch, 0), m => default, 0.4, WeatherKind.Clear, Ground: what == "ways-wet" ? new GroundCover { Wetness = 0.75f } : null, Series: WaySeries);
        }
        // Where a track leaves a road (and a road with its markings): from standing height and from 30 m up.
        if (what == "junction")
        {
            PlanetSurface.Poi.PoiSiting.StartWorld = true;
            return new(PlanetSpec.Roll(seed, arch, 0), m => default, 0.4, WeatherKind.Clear, Series: JunctionSeries);
        }
        // The grid: a substation and its lines, a house's drop close, and a street of drops from above.
        if (what == "power")
        {
            PlanetSurface.Poi.PoiSiting.StartWorld = true;
            return new(PlanetSpec.Roll(seed, arch, 0), m => default, 0.4, WeatherKind.Clear, Series: PowerSeries);
        }
        if (what == "pylon") PylonBearing = a.Length > 4 ? float.Parse(a[4]) : 20f;
        bool town = what is "town" or "manhole" or "sewer" or "drain" or "metro" or "culvert" or "gallery";
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
            if (under != null && what == "manhole" && under.Scheme.Shafts.FirstOrDefault(k => k.Network == UnderKind.Sewer && k.ToSurface) is UnderShaft hole)
            {
                // From above and behind, over the footpath: the open manhole beside the street.
                double hx = hole.Foot.X + 1, hz = hole.Foot.Z + 1;
                return Look(w, hx - town.AlongX * 14 + town.AlongZ * 6, hz - town.AlongZ * 14 - town.AlongX * 6, hole.TopY + 14f, hx, hz, hole.TopY);
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

    /// <summary>
    /// infra:roads: the start town's street grid and a road bending across open country, each from the
    /// ground, from low flight and from about 400 m up: how roads read at every distance.
    /// </summary>
    static System.Collections.Generic.List<Screenshot.Shot> RoadSeries(Main m)
    {
        var w = m.World;
        var shots = new System.Collections.Generic.List<Screenshot.Shot>();
        float G(double x, double z) => w.GroundHeight(Mathx.WrapX(x, w.Width), Mathx.Wrap(z, w.Height));
        void Add(string name, double x, double z, float y, double tx, double tz, float ty)
        {
            var (p, h, pitch) = Look(w, x, z, Math.Max(y, G(x, z) + 1.7f), tx, tz, ty);
            shots.Add(new Screenshot.Shot(name, p, h, pitch));
        }
        double ox = w.Landing.X, oz = w.Landing.Y;
        if (w.StartTown is StartTown town)
        {
            ox = town.CenterX;
            oz = town.CenterZ;
            float c = G(town.CenterX, town.CenterZ);
            // On the main street near its end, looking down it into the town.
            double gx = town.CenterX - town.AlongX * (town.MainHalf - 12) + town.AlongZ * 1.5, gz = town.CenterZ - town.AlongZ * (town.MainHalf - 12) - town.AlongX * 1.5;
            Add("town-ground", gx, gz, G(gx, gz) + 1.8f, town.CenterX + town.AlongX * 30, town.CenterZ + town.AlongZ * 30, c + 0.5f);
            double bx = town.CenterX + (town.AlongX * 0.6 + town.AlongZ * 0.8) * 150, bz = town.CenterZ + (town.AlongZ * 0.6 - town.AlongX * 0.8) * 150;
            Add("town-low", bx, bz, c + 45f, town.CenterX, town.CenterZ, c);
            double hx = town.CenterX + (town.AlongX * 0.6 + town.AlongZ * 0.8) * 520, hz = town.CenterZ + (town.AlongZ * 0.6 - town.AlongX * 0.8) * 520;
            Add("town-high", hx, hz, c + 400f, town.CenterX, town.CenterZ, c);
            // A corner of the grid from above, where the back street turns into a cross street.
            double sx0 = -town.AlongZ, sz0 = town.AlongX;
            double kx = town.CenterX + town.AlongX * town.Block + sx0 * town.BackSide * town.Arm, kz = town.CenterZ + town.AlongZ * town.Block + sz0 * town.BackSide * town.Arm;
            Add("town-corner", kx - town.AlongX * 14 - sx0 * town.BackSide * 14, kz - town.AlongZ * 14 - sz0 * town.BackSide * 14, G(kx, kz) + 55f, kx, kz, G(kx, kz));
        }
        else GD.Print("ROADS: no start town on this world");
        // A ground stretch that bends and runs off the map's axes, a little way from the town.
        InfraRoute best = null;
        int at = 0;
        double bestScore = double.MaxValue;
        foreach (InfraRoute r in w.Infra.Routes.Where(r => r.Kind is RouteKind.Road or RouteKind.Highway or RouteKind.Track))
            for (int i = 40; i + 40 < r.Count; i += 4)
            {
                bool ground = true;
                for (int k = i - 40; k < i + 40 && ground; k++) ground = r.Span[k] == RoadSpan.Ground;
                if (!ground) continue;
                double d = Math.Sqrt(Math.Pow(Mathx.WrapDelta(r.X[i] - ox, w.Width), 2) + Math.Pow(Mathx.WrapDelta(r.Z[i] - oz, w.Height), 2));
                if (d < 400 || d > 4000) continue;
                double a0 = Math.Atan2(r.Z[i] - r.Z[i - 30], r.X[i] - r.X[i - 30]), a1 = Math.Atan2(r.Z[i + 30] - r.Z[i], r.X[i + 30] - r.X[i]);
                double bend = Math.Abs(Mathf.Wrap((float)(a1 - a0), -Mathf.Pi, Mathf.Pi)) * 180 / Math.PI;
                double diag = Math.Abs(((a0 + a1) * 0.5 * 180 / Math.PI % 90 + 90) % 90 - 45);
                double score = Math.Abs(bend - 35) + diag * 0.8 + d * 0.004 + (r.Kind == RouteKind.Track ? 15 : 0);
                if (score < bestScore) { bestScore = score; best = r; at = i; }
            }
        if (best != null)
        {
            GD.Print($"ROADS: country {best.Kind} {best.Id} ({best.Surface}, wear {best.Wear:F2}) at {best.X[at]:F0}, {best.Z[at]:F0}");
            double dx = best.X[at + 12] - best.X[at - 12], dz = best.Z[at + 12] - best.Z[at - 12], len = Math.Max(1e-6, Math.Sqrt(dx * dx + dz * dz));
            dx /= len; dz /= len;
            int from = at - 16;
            double sx = best.X[from] + dz * best.HalfWidth * 0.5, sz = best.Z[from] - dx * best.HalfWidth * 0.5;
            Add("road-ground", sx, sz, best.Y[from] + 1.8f, best.X[at + 14], best.Z[at + 14], best.Y[at + 14] + 0.5f);
            Add("road-low", best.X[at] - dx * 70 + dz * 55, best.Z[at] - dz * 70 - dx * 55, best.Y[at] + 40f, best.X[at], best.Z[at], best.Y[at]);
            Add("road-high", best.X[at] - dx * 380 + dz * 300, best.Z[at] - dz * 380 - dx * 300, best.Y[at] + 400f, best.X[at], best.Z[at], best.Y[at]);
        }
        else GD.Print("ROADS: no country road found");
        // A paved road out in the country, from its own deck: its joints, cracks, patches and holes up close.
        InfraRoute paved = null;
        int pavedAt = 0;
        double pavedAway = double.MaxValue;
        foreach (InfraRoute r in w.Infra.Routes.Where(r => r.Kind is RouteKind.Road or RouteKind.Highway && (w.StartTown == null || !w.StartTown.Streets.Contains(r))))
            for (int i = 20; i + 20 < r.Count; i += 4)
            {
                if (r.Span[i - 20] != RoadSpan.Ground || r.Span[i + 19] != RoadSpan.Ground) continue;
                double d = Math.Sqrt(Math.Pow(Mathx.WrapDelta(r.X[i] - ox, w.Width), 2) + Math.Pow(Mathx.WrapDelta(r.Z[i] - oz, w.Height), 2));
                if (d < 400 || d >= pavedAway) continue;
                // Out on its own, not by a place (whose pad would fill the view).
                if (w.Pois.Any(q => Math.Sqrt(Math.Pow(Mathx.WrapDelta(q.Center.X - r.X[i], w.Width), 2) + Math.Pow(Mathx.WrapDelta(q.Center.Z - r.Z[i], w.Height), 2)) < Math.Max(q.Stamp.ReachX, q.Stamp.ReachZ) + 80)) continue;
                pavedAway = d;
                paved = r;
                pavedAt = i;
            }
        if (paved != null)
        {
            GD.Print($"ROADS: paved {paved.Kind} {paved.Id} ({paved.Surface}, wear {paved.Wear:F2}) at {paved.X[pavedAt]:F0}, {paved.Z[pavedAt]:F0}");
            double dx = paved.X[pavedAt + 6] - paved.X[pavedAt - 6], dz = paved.Z[pavedAt + 6] - paved.Z[pavedAt - 6], len = Math.Max(1e-6, Math.Sqrt(dx * dx + dz * dz));
            double sx = paved.X[pavedAt] + dz / len * paved.HalfWidth * 0.4, sz = paved.Z[pavedAt] - dx / len * paved.HalfWidth * 0.4;
            Add("paved-ground", sx, sz, paved.Y[pavedAt] + 1.7f, paved.X[pavedAt + 10], paved.Z[pavedAt + 10], paved.Y[pavedAt + 10] - 0.6f);
        }
        // The tightest bend on a paved road near the landing, close up from above its inside.
        InfraRoute bent = null;
        int bentAt = 0;
        double bentTurn = 0;
        foreach (InfraRoute r in w.Infra.Routes.Where(r => r.Kind is RouteKind.Road or RouteKind.Highway && (w.StartTown == null || !w.StartTown.Streets.Contains(r))))
            for (int i = 12; i + 12 < r.Count; i += 2)
            {
                if (r.Span[i - 12] != RoadSpan.Ground || r.Span[i + 11] != RoadSpan.Ground) continue;
                double d = Math.Sqrt(Math.Pow(Mathx.WrapDelta(r.X[i] - w.Landing.X, w.Width), 2) + Math.Pow(Mathx.WrapDelta(r.Z[i] - w.Landing.Y, w.Height), 2));
                if (d > 3000) continue;
                double a0 = Math.Atan2(r.Z[i] - r.Z[i - 12], r.X[i] - r.X[i - 12]), a1 = Math.Atan2(r.Z[i + 12] - r.Z[i], r.X[i + 12] - r.X[i]);
                double turn = Math.Abs(Mathf.Wrap((float)(a1 - a0), -Mathf.Pi, Mathf.Pi));
                if (turn > bentTurn) { bentTurn = turn; bent = r; bentAt = i; }
            }
        if (bent != null)
        {
            GD.Print($"ROADS: tightest bend on {bent.Kind} {bent.Id}: {bentTurn * 180 / Math.PI:F0} degrees over 96 m");
            double mx = (bent.X[bentAt - 12] + bent.X[bentAt + 12]) * 0.5, mz = (bent.Z[bentAt - 12] + bent.Z[bentAt + 12]) * 0.5;
            double ix = mx - bent.X[bentAt], iz = mz - bent.Z[bentAt], il = Math.Max(1e-6, Math.Sqrt(ix * ix + iz * iz));
            Add("bend-close", bent.X[bentAt] + ix / il * 45, bent.Z[bentAt] + iz / il * 45, bent.Y[bentAt] + 22f, bent.X[bentAt], bent.Z[bentAt], bent.Y[bentAt]);
        }
        // A junction out in the country: two roads (not railways) meeting, the nearest to the landing.
        var grid = new System.Collections.Generic.Dictionary<(long, long), InfraRoute>();
        foreach (InfraRoute r in w.Infra.Routes.Where(r => r.Kind != RouteKind.Rail))
            for (int i = 0; i < r.Count; i++)
                grid.TryAdd(((long)Math.Floor(Mathx.WrapX(r.X[i], w.Width) / 4), (long)Math.Floor(Mathx.Wrap(r.Z[i], w.Height) / 4)), r);
        InfraRoute jr = null;
        int ji = 0;
        double joinAway = double.MaxValue;
        foreach (InfraRoute r in w.Infra.Routes.Where(r => r.Kind != RouteKind.Rail && (w.StartTown == null || !w.StartTown.Streets.Contains(r))))
            for (int i = 0; i + 1 < r.Count; i += 2)
            {
                if (r.Span[i] != RoadSpan.Ground) continue;
                if (!grid.TryGetValue(((long)Math.Floor(Mathx.WrapX(r.X[i], w.Width) / 4), (long)Math.Floor(Mathx.Wrap(r.Z[i], w.Height) / 4)), out InfraRoute o) || o == r) continue;
                double d = Math.Sqrt(Math.Pow(Mathx.WrapDelta(r.X[i] - w.Landing.X, w.Width), 2) + Math.Pow(Mathx.WrapDelta(r.Z[i] - w.Landing.Y, w.Height), 2));
                if (d < joinAway) { joinAway = d; jr = r; ji = i; }
            }
        if (jr != null)
        {
            GD.Print($"ROADS: junction of {jr.Kind} {jr.Id} at {jr.X[ji]:F0}, {jr.Z[ji]:F0}");
            Add("junction-low", jr.X[ji] + 60, jr.Z[ji] + 45, jr.Y[ji] + 40f, jr.X[ji], jr.Z[ji], jr.Y[ji]);
            Add("junction-close", jr.X[ji] + 10, jr.Z[ji] + 8, jr.Y[ji] + 38f, jr.X[ji], jr.Z[ji], jr.Y[ji]);
            Add("junction-high", jr.X[ji] + 330, jr.Z[ji] + 250, jr.Y[ji] + 400f, jr.X[ji], jr.Z[ji], jr.Y[ji]);
        }
        else GD.Print("ROADS: no junction found");
        return shots;
    }

    /// <summary>
    /// infra:ways: a track and a graded dirt road near the landing, each from beside it at eye height, from a few
    /// metres over its ruts, and from the air: the land's own ground, ruts and crown, berms, prints and wear.
    /// </summary>
    static System.Collections.Generic.List<Screenshot.Shot> WaySeries(Main m)
    {
        var w = m.World;
        var shots = new System.Collections.Generic.List<Screenshot.Shot>();
        float G(double x, double z) => w.GroundHeight(Mathx.WrapX(x, w.Width), Mathx.Wrap(z, w.Height));
        void Add(string name, double x, double z, float y, double tx, double tz, float ty)
        {
            var (p, h, pitch) = Look(w, x, z, Math.Max(y, G(x, z) + 1.7f), tx, tz, ty);
            shots.Add(new Screenshot.Shot(name, p, h, pitch));
        }
        foreach (var (name, pick) in new (string, Func<InfraRoute, bool>)[] { ("track", r => r.Kind == RouteKind.Track), ("graded", r => r.Graded), ("steep", r => r.Kind == RouteKind.Track || r.Graded) })
        {
            InfraRoute best = null;
            int at = 0;
            double bestScore = double.MaxValue;
            foreach (InfraRoute r in w.Infra.Routes.Where(pick))
                for (int i = 12; i + 12 < r.Count; i += 2)
                {
                    bool ground = true;
                    for (int k = i - 12; k < i + 12 && ground; k++) ground = r.Span[k] == RoadSpan.Ground;
                    if (!ground) continue;
                    double d = Math.Sqrt(Math.Pow(Mathx.WrapDelta(r.X[i] - w.Landing.X, w.Width), 2) + Math.Pow(Mathx.WrapDelta(r.Z[i] - w.Landing.Y, w.Height), 2));
                    // Out on its own: not by a place, whose yard would fill the view.
                    if (w.Pois.Any(q => Math.Sqrt(Math.Pow(Mathx.WrapDelta(q.Center.X - r.X[i], w.Width), 2) + Math.Pow(Mathx.WrapDelta(q.Center.Z - r.Z[i], w.Height), 2)) < Math.Max(q.Stamp.ReachX, q.Stamp.ReachZ) + 40)) continue;
                    double grade = Math.Abs(r.Y[i + 6] - r.Y[i - 6]) / 48;
                    double score = name == "steep" ? -grade * 20000 + d : d;
                    if (score < bestScore) { bestScore = score; best = r; at = i; }
                }
            if (best == null) { GD.Print($"WAYS: no {name} found"); continue; }
            GD.Print($"WAYS: {name} on {best.Kind} {best.Id} ({best.Surface}, graded {best.Graded}, wear {best.Wear:F2}, half width {best.HalfWidth:F1}) at {best.X[at]:F0}, {best.Z[at]:F0}, grade {Math.Abs(best.Y[at + 6] - best.Y[at - 6]) / 48:F2}");
            double dx = best.X[at + 4] - best.X[at - 4], dz = best.Z[at + 4] - best.Z[at - 4], len = Math.Max(1e-6, Math.Sqrt(dx * dx + dz * dz));
            dx /= len; dz /= len;
            int from = at - 6;
            double sx = best.X[from] + dz * (best.HalfWidth + 1.2), sz = best.Z[from] - dx * (best.HalfWidth + 1.2);
            Add($"{name}-ground", sx, sz, G(sx, sz) + 1.7f, best.X[at + 6], best.Z[at + 6], best.Y[at + 6]);
            Add($"{name}-close", best.X[at] - dx * 3 + dz * 1.5, best.Z[at] - dz * 3 - dx * 1.5, best.Y[at] + 3.2f, best.X[at] + dx * 3, best.Z[at] + dz * 3, best.Y[at]);
            if (name != "steep") Add($"{name}-high", best.X[at] - dx * 120 + dz * 90, best.Z[at] - dz * 120 - dx * 90, best.Y[at] + 160f, best.X[at], best.Z[at], best.Y[at]);
        }
        return shots;
    }

    /// <summary>The nearest track end that leaves a road on open ground, from standing height and 30 m up, and the road beside it with its markings.</summary>
    static System.Collections.Generic.List<Screenshot.Shot> JunctionSeries(Main m)
    {
        var w = m.World;
        var shots = new System.Collections.Generic.List<Screenshot.Shot>();
        var j = Junctions.Find(w).OrderBy(q => Math.Pow(Mathx.WrapDelta(q.X - w.Landing.X, w.Width), 2) + Math.Pow(Mathx.WrapDelta(q.Z - w.Landing.Y, w.Height), 2)).FirstOrDefault();
        if (j == null) { GD.Print("JUNCTION: none found"); return shots; }
        GD.Print($"JUNCTION: track meets {j.Road.Kind} at {j.X:F0}, {j.Z:F0}");
        float G(double x, double z) => w.GroundHeight(Mathx.WrapX(x, w.Width), Mathx.Wrap(z, w.Height));
        void Add(string name, double x, double z, float y, double tx, double tz)
        {
            var (p, h, pitch) = Look(w, x, z, Math.Max(y, G(x, z) + 1.7f), tx, tz, G(tx, tz));
            shots.Add(new Screenshot.Shot(name, p, h, pitch));
        }
        Add("junction-ground", j.X + j.DirX * 14, j.Z + j.DirZ * 14, 0f, j.X, j.Z);
        Add("junction-high", j.X + j.DirX * 20, j.Z + j.DirZ * 20, G(j.X, j.Z) + 30f, j.X, j.Z);
        Add("road-ground", j.X + j.RoadX * 6 + j.RoadZ * 8, j.Z + j.RoadZ * 6 - j.RoadX * 8, 0f, j.X + j.RoadX * 25, j.Z + j.RoadZ * 25);
        return shots;
    }

    /// <summary>infra:power: the nearest substation from the ground and from above, the nearest strung drop close, and its street from above.</summary>
    static System.Collections.Generic.List<Screenshot.Shot> PowerSeries(Main m)
    {
        var w = m.World;
        var shots = new System.Collections.Generic.List<Screenshot.Shot>();
        PowerLines power = w.Infra.Power;
        float G(double x, double z) => w.GroundHeight(Mathx.WrapX(x, w.Width), Mathx.Wrap(z, w.Height));
        double Away(double x, double z) => Math.Sqrt(Math.Pow(Mathx.WrapDelta(x - w.Landing.X, w.Width), 2) + Math.Pow(Mathx.WrapDelta(z - w.Landing.Y, w.Height), 2));
        void Add(string name, double x, double z, float y, double tx, double tz, float ty)
        {
            var (p, h, pitch) = Look(w, x, z, Math.Max(y, G(x, z) + 1.7f), tx, tz, ty);
            shots.Add(new Screenshot.Shot(name, p, h, pitch));
        }
        if (power == null) { GD.Print("POWER: no lines"); return shots; }
        GD.Print($"POWER: {power.Poles.Count(p => p.Role == PowerRole.Plant)} plants, {power.Poles.Count(p => p.Role == PowerRole.Substation)} substations, {power.Poles.Count(p => p.Pylon)} pylons, {power.Poles.Count(p => p.Role == PowerRole.Line && !p.Pylon)} poles, {power.Drops.Count} drops ({power.Drops.Count(d => d.State == DropState.Strung)} strung), {power.Unserved.Count} unserved");
        PowerPole sub = power.Poles.Where(p => p.Role == PowerRole.Substation).OrderBy(p => Away(p.X, p.Z)).FirstOrDefault();
        if (sub != null)
        {
            double ax = -sub.SideZ, az = sub.SideX;
            Add("substation", sub.X - ax * 26 + sub.SideX * 18, sub.Z - az * 26 + sub.SideZ * 18, sub.Ground + 4f, sub.X, sub.Z, sub.Ground + 3f);
            Add("substation-high", sub.X - ax * 90 + sub.SideX * 70, sub.Z - az * 90 + sub.SideZ * 70, sub.Ground + 70f, sub.X, sub.Z, sub.Ground);
        }
        ServiceDrop drop = power.Drops.Where(d => d.State == DropState.Strung).OrderBy(d => Away(d.Fixing.X, d.Fixing.Z)).FirstOrDefault()
            ?? power.Drops.OrderBy(d => Away(d.Fixing.X, d.Fixing.Z)).FirstOrDefault();
        if (drop != null)
        {
            PowerPole p = power.Poles[drop.Pole];
            double dx = drop.Fixing.X - p.X, dz = drop.Fixing.Z - p.Z, l = Math.Max(1, Math.Sqrt(dx * dx + dz * dz));
            double mx = (p.X + drop.Fixing.X) / 2, mz = (p.Z + drop.Fixing.Z) / 2;
            GD.Print($"POWER: drop {drop.State} to a {drop.House.Family.Id} ({drop.House.State}), {l:F0} m");
            Add("drop", mx + dz / l * 16 - dx / l * 4, mz - dx / l * 16 - dz / l * 4, G(mx, mz) + 2.5f, mx, mz, drop.Fixing.Y);
            Add("drops-high", mx + dz / l * 45, mz - dx / l * 45, G(mx, mz) + 40f, mx, mz, G(mx, mz));
        }
        return shots;
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
