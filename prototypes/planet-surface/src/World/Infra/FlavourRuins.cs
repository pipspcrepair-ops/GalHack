using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using GalHack.Terrain;
using Planets.Gen;
using PoiGenerator.Gen;
using PoiSite = PlanetSurface.Poi.PoiSite;
using PInt3 = PoiGenerator.Gen.Int3;

namespace PlanetSurface.Infra;

/// <summary>
/// A small ruin that isn't a place of its own: a house on a street, a farmstead out in the fields, a
/// shed, a shop, a fuel station on the highway. Its layout comes from one of a few small families
/// (flavour_*), drawn at the world's tech level and turned to face its road, shared by every ruin built
/// from it; each ruin spawns it with its own seed, so the century of decay differs.
/// </summary>
public sealed class FlavourRuin
{
    public FamilyDef Family;
    public Layout Layout;
    public PoiLibrary Library;
    /// <summary>World cell of the layout's origin (x, z on the planet, as laid; y the pad's surface).</summary>
    public int OriginX, OriginY, OriginZ;
    public PoiState State = PoiState.Ruined;
    public ulong SpawnSeed;
    public TerrainStamp Stamp;
    /// <summary>How hard a nearby blast flattened it (0 none), and from which way ground zero lies.</summary>
    public float Blast;
    public float BlastX, BlastZ;
    /// <summary>The big blast that graded it by distance (stumps by the rim, shells further out), or null.</summary>
    public Strike Flatten;
    /// <summary>The road it stands by (null for one out in the country).</summary>
    public InfraRoute Route;
    /// <summary>Its driveway, forecourt apron or farm track to that road (null where it has no road).</summary>
    public TerrainStamp Drive;
    /// <summary>A track of its own runs from its gate to the nearest road.</summary>
    public bool Tracked;
    /// <summary>
    /// Power (the interface with the roads side's lines, <see cref="PowerLines.Fixing"/>): where the house's service drop
    /// is fixed to its wall. The houses side sets these from the house it builds: which face of its outer walls takes the
    /// drop (null: the face toward the pole it comes from), and how high above that wall's foot, metres. The drop meets
    /// the outer wall nearest that face's middle with nothing in front of it.
    /// </summary>
    public Dir? FixingSide;
    public float FixingHeight = 3f;
    public (double X, double Z) Center => ((Stamp.MinX + Stamp.MaxX) * 0.5, (Stamp.MinZ + Stamp.MaxZ) * 0.5);

    /// <summary>The ruin as built and decayed (a pure function of its layout and seed): call off the main thread.</summary>
    public PoiInstance Spawn(WorldProfile world, PlanetTerrainSource ground = null)
    {
        PoiInstance inst = PoiFactory.Spawn(Library, Layout, State, SpawnSeed, world);
        if (Blast > 0.02f && Flatten is { } f)
        {
            double w = ground?.Width ?? double.MaxValue, h = ground?.Height ?? double.MaxValue;
            StateTransforms.Flattened(inst, (x, z) =>
            {
                double dx = Mathx.WrapDelta(OriginX + x + 0.5 - f.X, w), dz = Mathx.WrapDelta(OriginZ + z + 0.5 - f.Z, h);
                return f.Strength((float)Math.Sqrt(dx * dx + dz * dz));
            });
        }
        if (Blast > 0.02f) StateTransforms.Blast(inst, BlastX, BlastZ, Blast, new PoiGenerator.Gen.Rng(PoiGenerator.Gen.Hash.Combine(SpawnSeed, 0xB1A57UL)));
        if (ground != null)
            StateTransforms.Settle(inst, (x, z) => ground.SurfaceHeight(Mathx.WrapX(OriginX + x + 0.5, ground.Width), Mathx.Wrap(OriginZ + z + 0.5, ground.Height)) - OriginY, -1);
        return inst;
    }

    /// <summary>
    /// What the survival tool salvages from it, by what its blocks are made of: scrap metal, wiring,
    /// glass, plastics and rubble (an early source of raw and primary materials; each ruin runs out).
    /// </summary>
    public static Dictionary<string, int> Yields(PoiInstance inst)
    {
        var y = new Dictionary<string, int>();
        void Add(string k, int n) => y[k] = y.GetValueOrDefault(k) + n;
        foreach (var (_, b) in inst.Blocks.Blocks())
        {
            if (b.IsAir || b.Role == Role.Loot) continue;
            switch (b.Material)
            {
                case Material.Scrap or Material.Alloy or Material.Steel or Material.Rust or Material.Armour or Material.Bronze: Add("scrap metal", 1); break;
                case Material.Glass: Add("glass", 1); break;
                case Material.Lumen: Add("wiring", 1); break;
                default:
                    if (b.Role is Role.Light or Role.Prop) Add("wiring", 1);
                    else if (b.Role == Role.Decor) Add("plastics", 1);
                    else Add("rubble", 1);
                    break;
            }
        }
        // A block is a cubic metre: the tool takes a handful from each.
        foreach (var k in y.Keys.ToList()) y[k] = Math.Max(1, y[k] / 8);
        return y;
    }
}

/// <summary>
/// The old world's small places (design doc "Infrastructure"), not places but flavour, and few enough
/// to mean something: long empty stretches of road, then a reason to leave it. Houses stand together
/// along a street off a junction or on the way into a town, with shops at its road end; a fuel station
/// stands right on a highway or at a crossroads, open to the road, and is whatever its age kept (a coaching
/// inn, a petrol station, a charging depot, now and then something nobody here built); a farmstead lies out in the fields down its own track; a shed or a lone
/// house turns up now and then. Each faces its road and joins it by a driveway. On once-inhabited
/// worlds only, built at the world's pre-collapse tech level. Each gets a small pad of its own, laid
/// before the roads, and never stands on a road bed, a place's pad or another ruin. Round a blast site
/// they stand in its ruin ring, flattened outward from ground zero.
/// </summary>
public static class FlavourRuins
{
    static readonly ConcurrentDictionary<(string, int, Dir, TechLevel?, int, HomeHints), Layout> Layouts = new();

    /// <summary>
    /// How far a blast reaches into the land round its site, as a multiple of the site's own fade
    /// radius: the site holds its crater and inner ruin ring, the country round it the rest.
    /// </summary>
    public const float WorldReach = 4f;

    /// <summary>Different drawings of each plan a world may use, so a street isn't one house over and over.</summary>
    public const int Salts = 3;

    /// <summary>How many different houses one culture's street may draw from: each house is put together afresh by the house rules.</summary>
    public const int HouseSalts = 4096;

    /// <summary>The climate a place was built against, from the world map's temperature and humidity there.</summary>
    public static PoiGenerator.Gen.Climate ClimateAt(WorldMap map, double x, double z)
    {
        int cx = (int)Math.Floor(Mathx.WrapX(x, map.WidthMeters) / map.CellSize) % map.Width, cz = (int)Math.Floor(Mathx.Wrap(z, map.HeightMeters) / map.CellSize) % map.Height;
        int k = Math.Clamp(cz, 0, map.Height - 1) * map.Width + Math.Clamp(cx, 0, map.Width - 1);
        return HomeHints.ClimateOf(map.Temperature[k], map.Humidity[k]);
    }

    /// <summary>A house's culture where it stands, the region's (<see cref="CultureAt"/>); its purse is its own.</summary>
    static HomeHints Home(PoiGenerator.Gen.Rng rng, WorldMap map, RegionPlan plan, double x, double z) =>
        new(CultureAt(plan, map, x, z), ClimateAt(map, x, z), rng.Float());

    /// <summary>How far a stretch of open country reaches that one people built in, metres.</summary>
    public const double CountryPatch = 3200;

    /// <summary>
    /// The building culture where a house stands: one people to a town and everything it ran (its streets, its
    /// farmland, its works: the plan's region and the town it served), and out in the open country one to each
    /// stretch of land. A strange people's culture makes all its houses strange and a regular one none, so the two
    /// never share a street, a town or a stretch of country (design doc, "Infrastructure").
    /// </summary>
    public static int CultureAt(RegionPlan plan, WorldMap map, double x, double z)
    {
        x = Mathx.WrapX(x, map.WidthMeters);
        z = Mathx.Wrap(z, map.HeightMeters);
        if (plan?.RegionAt(x, z) is { } region)
            return (int)(PoiGenerator.Gen.Hash.Combine(map.Spec.Seed, 0xC0175UL, (ulong)(region.Town >= 0 ? region.Town : region.Index)) >> 33);
        long px = (long)Math.Floor(x / CountryPatch), pz = (long)Math.Floor(z / CountryPatch);
        return (int)(PoiGenerator.Gen.Hash.Combine(map.Spec.Seed, 0xC0A7UL, (ulong)(px * 73856093L ^ pz * 19349663L)) >> 33);
    }

    /// <summary>Forgets every layout drawn so far: a world's streets are no use to the next (a new game calls it between them).</summary>
    public static void Forget() => Layouts.Clear();

    /// <summary>Time spent laying out ruins' buildings, ms, since last reset (tests print it).</summary>
    public static double LayoutMs;
    readonly struct Timed : System.IDisposable
    {
        readonly long _t0;
        public Timed(long t0) => _t0 = t0;
        public void Dispose() => LayoutMs += System.Diagnostics.Stopwatch.GetElapsedTime(_t0).TotalMilliseconds;
    }

    /// <summary>A family's layout, turned to face a road, in the world's tech level's materials; null (with a warning) if none lays out, and the lot stays empty.</summary>
    public static Layout LayoutFor(PoiLibrary lib, FamilyDef f, int variant, Dir front = Dir.North, TechLevel? tech = null, int salt = 0, HomeHints home = null)
    {
        // Every street's houses are its own: the cache forgets old worlds' rather than growing without end.
        if (home != null && Layouts.Count > 3000) Layouts.Clear();
        return Layouts.GetOrAdd((f.Id, variant, front, tech, salt, home), _ =>
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            using var timed = new Timed(t0);
            Layout l = PoiFactory.TryLayout(lib, f, variant, Rarity.Common, f.Variants[variant % f.Variants.Count].Seed + (ulong)salt * 7919UL, new LayoutHints(front, tech, Home: home), out string why);
            if (l == null) Godot.GD.PushWarning($"lot left empty: {why}");
            return l;
        });
    }

    /// <summary>
    /// Which of a family's layouts a world of a tech level builds: its own, or any that fit all. A family's
    /// odd ones out (tech "unique": what the alien or Precursor road kept) turn up now and then.
    /// </summary>
    public static int VariantFor(FamilyDef f, TechLevel tech, PoiGenerator.Gen.Rng rng)
    {
        var odd = f.Variants.Where(v => v.Tech == "unique").ToList();
        if (odd.Count > 0 && rng.Chance(0.08f)) return rng.Pick(odd).Index;
        string tag = WorldProfile.Tag(tech);
        var fit = f.Variants.Where(v => v.Tech.Length == 0 || v.Tech == tag).ToList();
        return rng.Pick(fit.Count > 0 ? fit : f.Variants.Where(v => v.Tech != "unique").ToList()).Index;
    }

    /// <summary>The map axis facing nearest a direction.</summary>
    public static Dir Facing(double dx, double dz) => Math.Abs(dx) >= Math.Abs(dz) ? (dx > 0 ? Dir.East : Dir.West) : (dz > 0 ? Dir.South : Dir.North);

    /// <summary>A lot's width across its depth, and its depth from the road, for its turn.</summary>
    static (double Along, double Deep) LotSize(Layout l, Dir front) =>
        front is Dir.North or Dir.South ? (l.Occupied.Size.X, l.Occupied.Size.Z) : (l.Occupied.Size.Z, l.Occupied.Size.X);

    public static List<FlavourRuin> Plan(InfraNetwork net, PoiLibrary lib, WorldMap map, PlanetTerrainSource bare, IReadOnlyList<PoiSite> pois, WorldProfile profile, ulong seed,
        List<FlavourRuin> already = null)
    {
        var ruins = already ?? new List<FlavourRuin>();
        if (profile == null || !profile.Inhabited || lib == null) return ruins;
        var families = lib.Families.Where(f => f.Tags.Contains("flavour") && f.Id.StartsWith("flavour_") && (f.Techs.Count == 0 || f.Techs.Contains(WorldProfile.Tag(profile.Before))) && !f.Is.Any(profile.Barred.Contains)).ToList();
        if (families.Count == 0) return ruins;
        var rng = new PoiGenerator.Gen.Rng(PoiGenerator.Gen.Hash.Combine(seed, 0xF1A7UL));
        FamilyDef Pick(string kind) => families.FirstOrDefault(f => f.Tags.Contains(kind));
        double w = map.WidthMeters, h = map.HeightMeters;
        // On a planned world the small places stand where the plan's towns and farmland are, and the land between stays empty.
        RegionPlan plan = net.RegionPlan;
        bool farmland = plan != null ? plan.Regions.Any(r => r.Kind == RegionKind.Farmland) : profile.Trade == Trade.Agricultural;
        bool Lived(double x, double z) => plan == null || plan.RegionAt(Mathx.WrapX(x, map.WidthMeters), Mathx.Wrap(z, map.HeightMeters)) != null;
        // A street of houses runs a good way off its junction: its junction lies well inside the region.
        bool Heart(double x, double z) => plan == null || plan.RegionAt(Mathx.WrapX(x, map.WidthMeters), Mathx.Wrap(z, map.HeightMeters)) is { } home && plan.Dist(home.X, home.Z, Mathx.WrapX(x, map.WidthMeters), Mathx.Wrap(z, map.HeightMeters)) < home.Radius * 0.7;
        bool Farmed(double x, double z) => plan == null || plan.RegionAt(Mathx.WrapX(x, map.WidthMeters), Mathx.Wrap(z, map.HeightMeters))?.Kind == RegionKind.Farmland;
        double Dist(double ax, double az, double bx, double bz) => Math.Sqrt(Math.Pow(Mathx.WrapDelta(ax - bx, w), 2) + Math.Pow(Mathx.WrapDelta(az - bz, h), 2));
        // A house off on its own, or a farm's, is of the people of the region it stands in, in the weather there.
        Layout Lot(FamilyDef f, Dir front, double x = 0, double z = 0) => f.Tags.Contains("house")
            ? LayoutFor(lib, f, VariantFor(f, profile.Before, rng), front, profile.Before, rng.Range(0, HouseSalts), Home(rng, map, plan, x, z))
            : f.Tags.Contains("farmstead")
                ? LayoutFor(lib, f, VariantFor(f, profile.Before, rng), front, profile.Before, rng.Range(0, Salts), Home(rng, map, plan, x, z) with { Wealth = 0.5f })
                : LayoutFor(lib, f, VariantFor(f, profile.Before, rng), front, profile.Before, rng.Range(0, Salts));

        double roadKm = net.Routes.Where(r => r.Kind is RouteKind.Highway or RouteKind.Road).Sum(r => r.Count * InfraNetwork.Spacing) / 1000;
        double trackKm = net.Routes.Where(r => r.Kind == RouteKind.Track).Sum(r => r.Count * InfraNetwork.Spacing) / 1000;

        // Crossroads, and the last stretch of road into a town: where people lived.
        var hubs = new List<(InfraRoute R, int I, bool Junction)>();
        foreach (InfraRoute r in net.Routes.Where(r => r.Kind is RouteKind.Highway or RouteKind.Road && r.Count > 12))
        {
            if (r.From == null && Heart(r.X[0], r.Z[0])) hubs.Add((r, 0, true));
            if (r.To == null && Heart(r.X[^1], r.Z[^1])) hubs.Add((r, r.Count - 1, true));
            foreach (var (place, end, step) in new[] { (r.From, 0, 1), (r.To, r.Count - 1, -1) })
                if (place != null && (place.Tier is PoiTier.Town or PoiTier.City || place.Planned?.Role == "hub"))
                {
                    int i = end + step * (int)((Math.Max(place.Stamp.ReachX, place.Stamp.ReachZ) * 0.5 + 90) / InfraNetwork.Spacing);
                    if (i > 1 && i + 2 < r.Count) hubs.Add((r, i, false));
                }
        }
        rng.Shuffle(hubs);

        // Streets of houses off the hubs, a good way apart.
        int streets = plan != null ? plan.Roll.Streets * Math.Max(1, plan.Regions.Count(r => r.Kind == RegionKind.Town)) : Math.Clamp((int)(roadKm / 6), 2, 7);
        var built = new List<(double X, double Z)>();
        foreach (var (r, i, junction) in hubs)
        {
            if (built.Count >= streets) break;
            double px = r.X[i], pz = r.Z[i];
            if (built.Any(b => Dist(b.X, b.Z, px, pz) < (plan != null ? 420 : 1100))) continue;
            if (r.Span[Math.Min(i, r.Span.Length - 1)] != RoadSpan.Ground) continue;
            int length = 3 + rng.Range(0, 4);
            if (Street(px, pz, r, length, net, lib, bare, pois, ruins, w, h, rng, Pick("house"), Pick("shed"), Pick("shop"), profile.Before, CultureAt(plan, map, px, pz))) built.Add((px, pz));
        }

        // The stop each age kept on its roads (an inn, a petrol station, a charging depot): crossroads first, then wherever a long highway has gone without one.
        FamilyDef fuel = Pick("wayside");
        if (fuel != null)
        {
            int want = Math.Clamp((int)(roadKm / 7), 1, 6), got = 0;
            var stops = new List<(double X, double Z)>();
            bool Fuel(InfraRoute r, int i)
            {
                if (i < 2 || i + 2 >= r.Count || r.Span[i] != RoadSpan.Ground || r.Span[i - 1] != RoadSpan.Ground) return false;
                if (stops.Any(s => Dist(s.X, s.Z, r.X[i], r.Z[i]) < 1500)) return false;
                double dx = r.X[i + 2] - r.X[i - 2], dz = r.Z[i + 2] - r.Z[i - 2], len = Math.Sqrt(dx * dx + dz * dz);
                // A straight bit, near enough on a map axis for a lot to lie square to it.
                if (len < 1e-6 || Math.Max(Math.Abs(dx), Math.Abs(dz)) / len < 0.92) return false;
                foreach (int side in rng.Chance(0.5f) ? new[] { 1, -1 } : new[] { -1, 1 })
                {
                    double nx = Math.Abs(dx) > Math.Abs(dz) ? 0 : side, nz = Math.Abs(dx) > Math.Abs(dz) ? side : 0;
                    Dir front = Facing(-nx, -nz);
                    Layout layout = Lot(fuel, front);
                    if (layout == null) continue;
                    double deep = LotSize(layout, front).Deep * 0.5, back = deep + r.HalfWidth + 6;
                    FlavourRuin ruin = Try(lib, fuel, layout, r.X[i] + nx * back, r.Z[i] + nz * back, bare, net, pois, ruins, w, h, rng, spacing: 12, maxCut: 5f, clearance: back - 3);
                    // The forecourt lies level with the road it opens on.
                    if (ruin == null || Math.Abs(ruin.OriginY - r.Y[i]) > 1.5f || !Connect(ruin, r, layout.Variant?.Tech == "mid" || layout.Variant?.Tech == "high" ? 7f : 3f, ruin.Stamp.Surface == StampSurface.Natural ? StampSurface.Gravel : ruin.Stamp.Surface, bare, w, h)) continue;
                    ruin.Route = r;
                    ruins.Add(ruin);
                    stops.Add((r.X[i], r.Z[i]));
                    return true;
                }
                return false;
            }
            foreach (var (r, i, junction) in hubs.Where(k => k.Junction))
            {
                if (got >= want) break;
                int j = i == 0 ? 12 : i - 12; // a little way along from the junction itself
                if (j > 0 && j < r.Count && Fuel(r, j)) got++;
            }
            foreach (InfraRoute r in net.Routes.Where(r => r.Kind == RouteKind.Highway))
                for (int i = 40 + rng.Range(0, 60); i < r.Count && got < want; i += 25)
                    if (Fuel(r, i)) { got++; i += 300; }
            if (got < Math.Min(want, 2))
                foreach (InfraRoute r in net.Routes.Where(r => r.Kind == RouteKind.Road))
                    for (int i = 20; i < r.Count && got < 2; i += 25)
                        if (Fuel(r, i)) { got++; i += 300; }
        }

        // Farmsteads out in the fields, a track down to each from the road.
        FamilyDef farm = Pick("farmstead");
        var feeders = net.Routes.Where(r => r.Kind is RouteKind.Road or RouteKind.Track && r.Count > 20).ToList();
        if (farm != null && feeders.Count > 0)
        {
            int want = plan != null ? plan.Regions.Count(r => r.Kind == RegionKind.Farmland) * 3 : farmland ? Math.Clamp((int)((roadKm + trackKm) / 4), 4, 12) : Math.Clamp((int)((roadKm + trackKm) / 16), 1, 3);
            for (int tries = 0, got = 0; tries < want * 40 && got < want; tries++)
            {
                InfraRoute r = feeders[rng.Range(0, feeders.Count)];
                int i = rng.Range(2, r.Count - 2);
                if (r.Span[i] != RoadSpan.Ground) continue;
                double dx = r.X[i + 1] - r.X[i - 1], dz = r.Z[i + 1] - r.Z[i - 1], len = Math.Sqrt(dx * dx + dz * dz);
                if (len < 1e-6) continue;
                int side = rng.Chance(0.5f) ? 1 : -1;
                double nx = -dz / len * side, nz = dx / len * side, off = 70 + 130 * rng.Float();
                if (!Farmed(r.X[i] + nx * off, r.Z[i] + nz * off)) continue;
                Dir front = Facing(-nx, -nz);
                Layout layout = Lot(farm, front, r.X[i] + nx * off, r.Z[i] + nz * off);
                if (layout == null) continue;
                FlavourRuin ruin = Try(lib, farm, layout, r.X[i] + nx * off, r.Z[i] + nz * off, bare, net, pois, ruins, w, h, rng, spacing: 40);
                if (ruin == null || !Connect(ruin, r, 2.2f, profile.Before == TechLevel.Low ? StampSurface.Dirt : StampSurface.Gravel, bare, w, h)) continue;
                ruin.Route = r;
                ruins.Add(ruin);
                got++;
            }
        }

        // Now and then a shed or a lone house off a quiet road, with a long way between them.
        FamilyDef shed = Pick("shed"), house = Pick("house");
        foreach (InfraRoute r in net.Routes.Where(r => r.Kind is RouteKind.Road or RouteKind.Track))
        {
            double every = r.Kind == RouteKind.Track ? 1700 : 2200, next = every * (0.3 + rng.Float()), run = 0;
            for (int i = 2; i + 2 < r.Count; i++)
            {
                run += InfraNetwork.Spacing;
                if (run < next || r.Span[i] != RoadSpan.Ground || !Lived(r.X[i], r.Z[i])) continue;
                next = run + every * (0.6 + 0.8 * rng.Float());
                FamilyDef f = (rng.Chance(0.55f) ? shed : house) ?? house;
                if (f == null) continue;
                double dx = r.X[i + 1] - r.X[i - 1], dz = r.Z[i + 1] - r.Z[i - 1], len = Math.Sqrt(dx * dx + dz * dz);
                if (len < 1e-6) continue;
                int side = rng.Chance(0.5f) ? 1 : -1;
                double nx = -dz / len * side, nz = dx / len * side;
                Dir front = Facing(-nx, -nz);
                Layout layout = Lot(f, front, r.X[i], r.Z[i]);
                if (layout == null) continue;
                var size = layout.Occupied.Size;
                double back = Math.Sqrt(size.X * size.X + size.Z * size.Z) * 0.5 + 12 + rng.Float() * 16;
                FlavourRuin ruin = Try(lib, f, layout, r.X[i] + nx * back, r.Z[i] + nz * back, bare, net, pois, ruins, w, h, rng);
                if (ruin == null || !Connect(ruin, r, 1.6f, profile.Before == TechLevel.Low ? StampSurface.Dirt : StampSurface.Gravel, bare, w, h)) continue;
                ruin.Route = r;
                ruins.Add(ruin);
            }
        }

        // Enough to make a world with few roads look lived in once: lone houses and farms off in the country.
        int attempts = 800;
        for (int tries = 0; ruins.Count < 12 && tries < attempts && plan == null; tries++)
        {
            // Rough country takes a farmstead's yard badly: late on, a lone house on what ground there is.
            bool rough = tries > attempts / 2;
            FamilyDef f = Pick(!rough && (farmland || rng.Chance(0.5f)) ? "farmstead" : "house") ?? Pick("house");
            if (f == null) break;
            double lx = rng.Float() * w, lz = rng.Float() * h;
            Layout layout = Lot(f, Dirs.Horizontal[rng.Range(0, 4)], lx, lz);
            if (layout == null) continue;
            FlavourRuin ruin = Try(lib, f, layout, lx, lz, bare, net, pois, ruins, w, h, rng, maxCut: rough ? 8f : 4.5f);
            if (ruin != null) ruins.Add(ruin);
        }
        // A survivor town's landing clearing, at its edge, with a track in, where traders set down.
        if (lib.Families.FirstOrDefault(f => f.Id == "landing_clearing") is { } clearing)
            foreach (PoiSite p in pois.Where(p => p.Family.Id == "survivor_town" && p.Instance.State == PoiState.Active))
            {
                if (!rng.Chance(0.7f)) continue;
                Layout layout = LayoutFor(lib, clearing, rng.Range(0, clearing.Variants.Count));
                if (layout == null) continue;
                // Clear of the town's square reach whichever way round it lands.
                double start = rng.Float() * Math.PI * 2, reach = (Math.Max(p.Stamp.ReachX, p.Stamp.ReachZ) + 50) * 1.45;
                for (int k = 0; k < 36; k++)
                {
                    // Round the town, then further out: a clearing is flattened, so it takes rougher ground than a house.
                    double a = start + k * Math.PI / 6, far = reach + 40 * (k / 12);
                    double cx = p.Center.X + Math.Cos(a) * far, cz = p.Center.Z + Math.Sin(a) * far;
                    var ruin = Try(lib, clearing, layout, cx, cz, bare, net, pois, ruins, w, h, rng, roadGap: 4, maxCut: 12f);
                    if (ruin == null) continue;
                    ruin.State = PoiState.Abandoned;
                    ruins.Add(ruin);
                    var (rx, rz) = ruin.Center;
                    net.LandingSpots.Add(new LandingSpot(rx, rz, ruin.OriginY, 12f, p));
                    net.Spur((rx, rz), RouteKind.Track, 900);
                    break;
                }
            }
        // Round a blast: the ruin ring, every ruin in it flattened outward from ground zero.
        foreach (PoiSite p in pois)
        {
            if (p.GroundZero is not (double gx, double gz) || p.Instance.Layout.Extras.Blast is not BlastZone zone) continue;
            double inner = Math.Max(p.Stamp.ReachX, p.Stamp.ReachZ) + 24, outer = zone.Fade * (WorldReach - 1);
            if (outer <= inner) outer = inner + 120;
            for (int k = 0, placed = 0; k < 120 && placed < 12; k++)
            {
                double a = rng.Float() * Math.PI * 2, d = inner + (outer - inner) * rng.Float();
                FamilyDef f = Pick("house");
                if (f == null) break;
                Layout layout = Lot(f, Dirs.Horizontal[rng.Range(0, 4)], gx + Math.Cos(a) * d, gz + Math.Sin(a) * d);
                if (layout == null) continue;
                var ruin = Try(lib, f, layout, gx + Math.Cos(a) * d, gz + Math.Sin(a) * d, bare, net, pois, ruins, w, h, rng);
                if (ruin == null) continue;
                ruins.Add(ruin);
                placed++;
            }
        }
        // Round a blast that left the ground hot: warning posts at the edge of it, facing whoever comes in.
        if (Pick("warning") is { } warning)
            foreach (Strike b in net.Blasts.Where(b => b.Rad > 0f && !b.Naval))
            {
                double start = rng.Float() * Math.PI * 2;
                for (int k = 0, placed = 0; k < 16 && placed < 8; k++)
                {
                    double a = start + k * Math.PI / 8, d = b.Dead + 6;
                    double px = b.X + Math.Cos(a) * d, pz = b.Z + Math.Sin(a) * d;
                    Layout layout = LayoutFor(lib, warning, 0, Facing(Math.Cos(a), Math.Sin(a)));
                    if (layout == null) break;
                    var post = Try(lib, warning, layout, px, pz, bare, net, pois, ruins, w, h, rng, roadGap: 3, spacing: 4, maxCut: 3f, clearance: 4);
                    if (post == null) continue;
                    post.State = PoiState.Abandoned;
                    ruins.Add(post);
                    placed++;
                }
            }
        foreach (FlavourRuin ruin in ruins) Blasted(ruin, net.Blasts, w, h);        // Nobody built out of reach of a road: whatever came down without one, and hasn't a neighbour that has, gets a track.
        foreach (FlavourRuin ruin in ruins) net.KeepOff(ruin.Stamp);
        foreach (FlavourRuin ruin in ruins)
        {
            // A warning post stands where people come, not where a track was made to it.
            if (ruin.Route != null || ruin.Drive != null || ruin.Family.Tags.Contains("warning")) continue;
            var (rx, rz) = ruin.Center;
            if (ruins.Any(o => o != ruin && (o.Route != null || o.Drive != null) && Dist(o.Center.X, o.Center.Z, rx, rz) < 60)) continue;
            InfraRoute near = net.Nearest(rx, rz, 380, out _, out _);
            if (near != null && near.Kind != RouteKind.Rail && Connect(ruin, near, 1.8f, StampSurface.Dirt, bare, w, h)) { ruin.Route = near; continue; }
            // From a little way out of the lot's gate, and a drive of its own from the gate to that.
            var gate = ruin.Layout.Extras.Gate;
            double gx = ruin.OriginX + gate.X + 0.5, gz = ruin.OriginZ + gate.Z + 0.5, ox = gx - rx, oz = gz - rz, olen = Math.Max(1e-3, Math.Sqrt(ox * ox + oz * oz));
            ruin.Tracked = net.Spur((gx + ox / olen * 16, gz + oz / olen * 16), RouteKind.Track, 2400);
            if (ruin.Tracked) Connect(ruin, net.Routes[^1], 1.8f, StampSurface.Dirt, bare, w, h);
        }
        return ruins;
    }

    /// <summary>
    /// A street off a road at a point: laid straight along a map axis, the way out across the road,
    /// houses down both sides facing it, shops at its road end, a shed behind the odd one. It is laid
    /// only if the ground takes a fair few lots. False if it doesn't, in any of the ways it could run.
    /// </summary>
    static bool Street(double px, double pz, InfraRoute road, int pairs, InfraNetwork net, PoiLibrary lib, PlanetTerrainSource bare, IReadOnlyList<PoiSite> pois, List<FlavourRuin> ruins,
        double w, double h, PoiGenerator.Gen.Rng rng, FamilyDef house, FamilyDef shed, FamilyDef shop, TechLevel tech, int culture)
    {
        if (house == null) return false;
        const float StreetHalf = 4f;
        // The street's people: one culture, in the weather here, as well off as the street was.
        var home = new HomeHints(culture, ClimateAt(bare.Sampler.Map, px, pz), rng.Float());
        var ways = new List<(double X, double Z)> { (1, 0), (-1, 0), (0, 1), (0, -1) };
        rng.Shuffle(ways);
        foreach (var (ux, uz) in ways)
        {
            // Not back along the road it leaves.
            bool clear = true;
            for (double t = 24; t <= 60 && clear; t += 12) clear = net.Nearest(Mathx.WrapX(px + ux * t, w), Mathx.Wrap(pz + uz * t, h), 16, out _, out _) == null;
            if (!clear) continue;
            var lots = new List<FlavourRuin>(ruins);
            int before = lots.Count;
            double sx = -uz, sz = ux, far = 0;
            foreach (int side in new[] { -1, 1 })
            {
                double along = road.HalfWidth + 14;
                for (int k = 0; k < pairs; k++)
                {
                    bool shopFront = shop != null && k < Math.Max(1, pairs / 3);
                    FamilyDef f = shopFront ? shop : shed != null && rng.Chance(0.1f) ? shed : house;
                    Dir front = Facing(-sx * side, -sz * side);
                    // Every house on a street of one culture, each put together afresh, so no two neighbours are one model.
                    Layout layout = f == house ? LayoutFor(lib, f, VariantFor(f, tech, rng), front, tech, rng.Range(0, HouseSalts), home)
                        : LayoutFor(lib, f, VariantFor(f, tech, rng), front, tech, rng.Range(0, Salts));
                    if (layout == null) continue;
                    var (wide, deep) = LotSize(layout, front);
                    along += wide * 0.5;
                    double back = StreetHalf + deep * 0.5 + 6.5, cx = px + ux * along + sx * side * back, cz = pz + uz * along + sz * side * back;
                    FlavourRuin r = Try(lib, f, layout, cx, cz, bare, net, pois, lots, w, h, rng, spacing: 0, maxCut: 5f, clearance: back - 3);
                    // A lot sits at the street's level, not up a bank above it.
                    float kerb = bare.Sampler.Sample(Mathx.WrapX(px + ux * along, w), Mathx.Wrap(pz + uz * along, h), 2).Height;
                    if (r != null && Math.Abs(r.OriginY - kerb) <= 2.5f) lots.Add(r);
                    along += wide * 0.5 + 2;
                    far = Math.Max(far, along);
                }
            }
            if (lots.Count - before < Math.Max(3, pairs)) continue;
            InfraRoute street = net.Street(new List<(double X, double Z)> { (px, pz), (px + ux * (far + 6), pz + uz * (far + 6)) }, RouteKind.Road);
            if (street == null) continue;
            street.Kept = false;
            street.Wear = 0.92f;
            foreach (FlavourRuin r in lots.Skip(before))
            {
                r.Route = street;
                r.State = rng.Chance(0.85f) ? PoiState.Ruined : PoiState.Abandoned;
                if (Connect(r, street, 1.6f, StampSurface.Gravel, bare, w, h)) ruins.Add(r);
            }
            return true;
        }
        return false;
    }

    /// <summary>
    /// Joins a ruin's gate to the nearest point of a road: a stamp of its own, its deck following the land
    /// from the pad to the road's, at a grade you can drive. False if the ground between won't take one.
    /// </summary>
    public static bool Connect(FlavourRuin ruin, InfraRoute road, float halfWidth, StampSurface surface, PlanetTerrainSource bare, double w, double h)
    {
        var gate = ruin.Layout.Extras.Gate;
        double gx = ruin.OriginX + gate.X + 0.5, gz = ruin.OriginZ + gate.Z + 0.5;
        // Toward the road, from a little way inside the lot.
        int best = -1;
        double bestD = double.MaxValue;
        for (int i = 0; i < road.Count; i++)
        {
            double dx = Mathx.WrapDelta(road.X[i] - gx, w), dz = Mathx.WrapDelta(road.Z[i] - gz, h), d = dx * dx + dz * dz;
            if (d < bestD) { bestD = d; best = i; }
        }
        if (best < 0 || road.Span[Math.Min(best, road.Span.Length - 1)] != RoadSpan.Ground) return false;
        double ex = gx + Mathx.WrapDelta(road.X[best] - gx, w), ez = gz + Mathx.WrapDelta(road.Z[best] - gz, h), len = Math.Sqrt(bestD);
        if (len > 400) return false;
        int n = Math.Max(2, (int)Math.Ceiling(len / InfraNetwork.Spacing));
        double ux = len > 1e-6 ? (ex - gx) / len : 0, uz = len > 1e-6 ? (ez - gz) / len : 0;
        // A few metres into the lot, so the drive and the pad overlap and no strip of raw ground shows between them.
        const double Into = 4;
        var xs = new List<double> { gx - ux * Into };
        var zs = new List<double> { gz - uz * Into };
        var ys = new List<float> { ruin.OriginY };
        float y0 = ruin.OriginY, y1 = road.Y[best];
        var land = new float[n + 1];
        for (int i = 0; i <= n; i++)
        {
            double t = i / (double)n;
            GroundSample g = bare.Sampler.Sample(Mathx.WrapX(gx + (ex - gx) * t, w), Mathx.Wrap(gz + (ez - gz) * t, h), 2);
            if (g.InRiver || g.Height < 2f) return false;
            land[i] = g.Height;
        }
        for (int i = 0; i <= n; i++)
        {
            double t = i / (double)n;
            // The land smoothed, pinned to the pad and the road at its ends.
            float s = 0;
            int c = 0;
            for (int k = Math.Max(0, i - 3); k <= Math.Min(n, i + 3); k++) { s += land[k]; c++; }
            float ends = y0 + (y1 - y0) * (float)t, pin = (float)Math.Min(1.0, Math.Min(t, 1 - t) * len / 16);
            float y = ends + (s / c - ends) * pin;
            if (i > 0 && Math.Abs(y - ys[^1]) > InfraNetwork.Spacing * 0.3f + 0.2f) return false;
            xs.Add(gx + (ex - gx) * t);
            zs.Add(gz + (ez - gz) * t);
            ys.Add(y);
        }
        var spans = Enumerable.Repeat(RoadSpan.Ground, xs.Count - 1).ToArray();
        ruin.Drive = TerrainStamp.Road("drive", xs, zs, ys, spans, halfWidth, surface, 0.9f, w, h);
        return true;
    }

    /// <summary>Takes a ruin's share of any blast round it (a blast site's or a war's strike, one record each): the nearer ground zero, the harder.</summary>
    static void Blasted(FlavourRuin ruin, IReadOnlyList<Strike> blasts, double w, double h)
    {
        foreach (Strike b in blasts)
        {
            var (cx, cz) = ruin.Center;
            double dx = Mathx.WrapDelta(cx - b.X, w), dz = Mathx.WrapDelta(cz - b.Z, h);
            float d = (float)Math.Sqrt(dx * dx + dz * dz), s = b.Strength(d);
            if (s <= ruin.Blast) continue;
            ruin.Blast = s;
            ruin.Flatten = b.Flatten ? b : null;
            ruin.BlastX = d < 1f ? 0f : (float)(-dx / d);
            ruin.BlastZ = d < 1f ? 1f : (float)(-dz / d);
        }
    }

    /// <summary>
    /// A ruin's pad at a spot, if the ground there takes one: dry, not too steep, clear of roads, places
    /// and other ruins. <paramref name="clearance"/> replaces the corner-and-gap distance from any road's
    /// centre where the caller knows its lot lies square to the street.
    /// </summary>
    public static FlavourRuin Try(PoiLibrary lib, FamilyDef f, Layout layout, double cx, double cz, PlanetTerrainSource bare, InfraNetwork net, IReadOnlyList<PoiSite> pois,
        List<FlavourRuin> ruins, double w, double h, PoiGenerator.Gen.Rng rng, double roadGap = 14, double spacing = 18, float maxCut = 4.5f, double? clearance = null)
    {
        cx = Mathx.WrapX(cx, w);
        cz = Mathx.Wrap(cz, h);
        CellMask occ = layout.Occupied;
        double halfX = occ.Size.X * 0.5, halfZ = occ.Size.Z * 0.5;
        const float Apron = 2f;
        // On the world's 2 m lattice, as a player's blocks are; everything below is judged from there.
        int ox = (int)Math.Floor((cx - (occ.Min.X + halfX)) / 2) * 2, oz = (int)Math.Floor((cz - (occ.Min.Z + halfZ)) / 2) * 2;
        cx = ox + occ.Min.X + halfX;
        cz = oz + occ.Min.Z + halfZ;
        // Clear of every place's reach, every road's bed and banks, and other ruins.
        foreach (PoiSite p in pois)
        {
            double dx = Math.Abs(Mathx.WrapDelta(p.Center.X - cx, w)), dz = Math.Abs(Mathx.WrapDelta(p.Center.Z - cz, h));
            double reach = Math.Max(p.Stamp.ReachX, p.Stamp.ReachZ) + Math.Max(halfX, halfZ) + 12;
            if (dx < reach && dz < reach) return null;
        }
        // Nor in a crater: nothing stood where a strike punched one.
        foreach (TerrainStamp c in net.Craters)
        {
            double reach = c.ReachX + Math.Max(halfX, halfZ) + 6;
            if (Math.Abs(Mathx.WrapDelta(c.CenterX - cx, w)) < reach && Math.Abs(Mathx.WrapDelta(c.CenterZ - cz, h)) < reach) return null;
        }
        double corner = Math.Sqrt(halfX * halfX + halfZ * halfZ) + Apron;
        if (net.Nearest(cx, cz, corner + 30, out _, out double gap) != null && gap < (clearance ?? corner + roadGap)) return null;
        foreach (FlavourRuin o in ruins)
        {
            var (rx, rz) = o.Center;
            double ohx = (o.Stamp.MaxX - o.Stamp.MinX) * 0.5, ohz = (o.Stamp.MaxZ - o.Stamp.MinZ) * 0.5;
            if (Math.Abs(Mathx.WrapDelta(rx - cx, w)) < halfX + ohx + spacing && Math.Abs(Mathx.WrapDelta(rz - cz, h)) < halfZ + ohz + spacing) return null;
        }
        // The ground: dry, and level enough to cut and fill a small pad.
        var samples = new List<float>();
        for (double sz = -halfZ - Apron; sz <= halfZ + Apron + 0.01; sz += (halfZ + Apron) / 2)
        for (double sx = -halfX - Apron; sx <= halfX + Apron + 0.01; sx += (halfX + Apron) / 2)
        {
            GroundSample g = bare.Sampler.Sample(cx + sx, cz + sz, 2);
            if (g.InRiver || g.Height < 2f) return null;
            samples.Add(g.Height);
        }
        samples.Sort();
        int pad = (int)MathF.Round(samples[samples.Count / 2]);
        float cut = MathF.Max(samples[^1] - pad, pad - samples[0]);
        if (cut > maxCut) return null;
        // Every side of the pad, and the 2 m round it, keeps off a road's deck, whichever way the road runs.
        var edge = new List<(double X, double Z)>();
        for (double a = -halfX - 2; a <= halfX + 2.01; a += 6) { edge.Add((a, -halfZ - 2)); edge.Add((a, halfZ + 2)); }
        for (double a = -halfZ - 2; a <= halfZ + 2.01; a += 6) { edge.Add((-halfX - 2, a)); edge.Add((halfX + 2, a)); }
        foreach (var (ex, ez) in edge)
            {
                InfraRoute near = net.Nearest(Mathx.WrapX(cx + ex, w), Mathx.Wrap(cz + ez, h), 24, out _, out double off);
                if (near != null && off < near.HalfWidth + 2) return null;
            }
        int carveH = Math.Max(0, -occ.Min.Y);
        bool[] carve = null, walls = null;
        if (carveH > 0)
        {
            carve = new bool[carveH * occ.Size.Z * occ.Size.X];
            walls = new bool[carve.Length];
            for (int y = 0; y < carveH; y++)
            for (int z = 0; z < occ.Size.Z; z++)
            for (int x = 0; x < occ.Size.X; x++)
            {
                var cell = new PInt3(occ.Min.X + x, occ.Min.Y + y, occ.Min.Z + z);
                carve[(y * occ.Size.Z + z) * occ.Size.X + x] = Structure.Carved(layout.Blocks, occ, cell);
                walls[(y * occ.Size.Z + z) * occ.Size.X + x] = Structure.OuterWall(layout.Blocks, occ, cell);
            }
            if (!carve.Contains(true)) (carve, walls, carveH) = (null, null, 0);
        }
        float blend = Math.Clamp(cut * 2.4f + 6f, 6f, 24f);
        var stamp = new TerrainStamp(f.Name, ox + occ.Min.X, oz + occ.Min.Z, ox + occ.Min.X + occ.Size.X, oz + occ.Min.Z + occ.Size.Z, pad, Apron, blend, w, h,
            ox + occ.Min.X, pad + occ.Min.Y, oz + occ.Min.Z, occ.Size.X, carveH, occ.Size.Z, carve)
        {
            Walls = walls, Surface = f.Tags.Contains("wayside") ? (layout.Variant?.Tech switch { "low" => StampSurface.Dirt, "unique" => StampSurface.Natural, _ => StampSurface.Concrete }) : f.Tags.Contains("shop") ? StampSurface.Concrete : f.Tags.Contains("farmstead") || f.Tags.Contains("shed") ? StampSurface.Dirt : StampSurface.Natural, Wear = 0.9f,
        };
        return new FlavourRuin
        {
            Family = f, Layout = layout, Library = lib, OriginX = ox, OriginY = pad, OriginZ = oz, SpawnSeed = rng.NextU64(), Stamp = stamp,
            State = rng.Chance(0.75f) ? PoiState.Ruined : PoiState.Abandoned,
        };
    }
}
