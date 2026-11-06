using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GalHack.Terrain;
using Planets.Gen;
using PoiGenerator.Gen;
using TerrainDigging.Terrain;
using PBiome = PoiGenerator.Gen.Biome;
using GBiome = Planets.Gen.Biome;
using PInt3 = PoiGenerator.Gen.Int3;
using TInt3 = TerrainDigging.Terrain.Int3;
using PRng = PoiGenerator.Gen.Rng;
using GHash = Planets.Gen.Hash;

namespace PlanetSurface.Poi;

/// <summary>The POI generator's authored data (kits, families, rosters, loot, logs), read in place from its prototype.</summary>
public sealed class PoiCatalog
{
    public readonly PoiLibrary Library;

    PoiCatalog(PoiLibrary library) => Library = library;

    /// <summary>Loads every *.kit and *.txt under the POI generator's data folder.</summary>
    public static PoiCatalog Load(string dataDir)
    {
        var files = new List<(string, string)>();
        foreach (string path in Directory.EnumerateFiles(dataDir, "*", SearchOption.AllDirectories))
            if (path.EndsWith(".kit") || path.EndsWith(".txt"))
                files.Add((Path.GetFileName(path), File.ReadAllText(path)));
        return new PoiCatalog(PoiLibrary.Load(files));
    }
}

/// <summary>One point of interest placed on a planet and seated in its terrain.</summary>
public sealed class PoiSite
{
    public string Name = "";
    /// <summary>The world's main starport: the network's main node.</summary>
    public bool Primary;
    public FamilyDef Family;
    public PoiInstance Instance;

    /// <summary>World cell of the POI grid's origin: x and z canonical, y the pad surface.</summary>
    public TInt3 Origin;

    /// <summary>The cells the place occupies, above and below ground (anchored by the terrain).</summary>
    public CellBox Footprint;

    public TerrainStamp Stamp;

    /// <summary>Stamps that ground the place beyond its pad: a crater bowl, terraces, an approach cut, a tilt that half-buries a ruin, an impact furrow. Applied after the pad.</summary>
    public List<TerrainStamp> Extra = new();

    /// <summary>Every stamp this place lays, pad first.</summary>
    public IEnumerable<TerrainStamp> Stamps => Extra.Prepend(Stamp);

    /// <summary>A ruin sunk into rising ground (its back walls are meant to be in the earth; its rooms stay open).</summary>
    public bool HalfBuried;

    /// <summary>Where it was fitted: ridge, valley, hillside or flat, and how much ground was cut and filled.</summary>
    public string Siting = "";
    public float CutFill;

    /// <summary>The cell a visitor stands in outside the way in, in world cells.</summary>
    public TInt3 Entrance;

    public TInt3 World(PInt3 local) => new(Origin.X + local.X, Origin.Y + local.Y, Origin.Z + local.Z);

    // ---- what a road, rail or power network needs to join the place up (read-only) ----------

    /// <summary>Quarter turns about y the place was turned by when seated. Places sit as generated today (0); the field is here so a network never assumes it.</summary>
    public int Rotation;

    /// <summary>The middle of the footprint, in world metres (x, z canonical).</summary>
    public (double X, double Z) Center => ((Stamp.MinX + Stamp.MaxX) * 0.5, (Stamp.MinZ + Stamp.MaxZ) * 0.5);

    /// <summary>How much it matters to a network: city, spaceport, town, base, outpost, camp, site, wreck.</summary>
    public PoiTier Tier => Family.Tier;

    /// <summary>Held by a faction (its core is live, or its people are home): its own infrastructure works.</summary>
    public bool FactionHeld => Instance.HasCore || (Family.Faction != PoiGenerator.Gen.Faction.None && Instance.State == PoiState.Active);

    /// <summary>A blast site's ground zero in world metres (its radii are in <c>Instance.Layout.Extras.Blast</c>): for the dead-zone ground tint and the world work.</summary>
    public (double X, double Z)? GroundZero => Instance.Layout.Extras.Blast is { } z ? (Origin.X + z.Center.X + 0.5, Origin.Z + z.Center.Z + 0.5) : null;

    /// <summary>A settlement's or civilian place's townsfolk data (spots, links, routes, people, animals, area), in local cells; null elsewhere.</summary>
    public Townsfolk Townsfolk => Instance.Townsfolk;

    /// <summary>The ways in a road can end at: the main entrance, fence gates, collapse breaches. World cells and the way out.</summary>
    public IReadOnlyList<PoiGate> Gates = Array.Empty<PoiGate>();
}

/// <summary>A way into a place, for a network to connect to: where you stand outside it, and which way is out (a fence gate gives the axis through it; 0, 0 when any way is).</summary>
public readonly record struct PoiGate(string Kind, TInt3 Cell, int OutX, int OutZ);

/// <summary>
/// Places POIs on a planet with the POI generator's own placement rules, then fits each one to
/// the real terrain and seats it:
/// 1. A placement map of the whole planet: biomes from the planet's own, faction territories,
///    and rich ground where the deposit map has a signature resource.
/// 2. <see cref="WorldPlacement"/> scatters places evenly over all of it (the planet is a torus)
///    and picks each one from the planet's themed set by biome and territory, with a few outliers
///    that don't belong. The nearest half are seated first, then an even spread to the far side.
/// 3. Each place is then fitted within a short walk of its cell to ground that suits it, by its
///    family's siting: a garrison or an observatory on high ground with a view (ridge, peak), a
///    mine at the foot of a slope it can dig into (valley, hillside), an airfield on wide level
///    ground (open), a farm on the flattest (flat), a wreck wherever it came down (any). Cut and
///    fill are bounded everywhere; water, rivers and the seams are off limits.
/// 4. The pad: a <see cref="TerrainStamp"/> levels the footprint and an apron at the floor's
///    height (cutting high ground, filling low) and keeps rooms below ground open.
/// Deterministic from the planet seed.
/// </summary>
public static class PoiSiting
{
    public const double RegionSize = 3200;   // metres around the landing site (kept for callers; placement now covers the planet)
    const float CellSize = 40f;
    /// <summary>Placement cells over the whole planet, in metres.</summary>
    const float WorldCell = 160f;
    /// <summary>How many places a planet gets at most: spread over all of it, nearest the landing site first.</summary>
    public static int MaxSites = 14;
    /// <summary>Tests and screenshots: also place one of this family near the landing site.</summary>
    public static string ForceFamily;
    /// <summary>
    /// Start worlds: no faction holds ground (faction places only as ruins, no cores, no defenders;
    /// wrecks, abandoned and infested places, the Bloom, wildlife and gang camps stay). The slice sets
    /// this when it rolls a start world, before the world is built; <see cref="Place"/> can also be told directly.
    /// </summary>
    public static bool StartWorld;
    const float Apron = 4f;
    const float MaxCutFill = 16f;
    const double SearchRadius = 140;
    const double SeamMargin = 320;           // keeps POIs clear of the ghost band either side of a seam

    public static List<PoiSite> Place(PoiCatalog catalog, WorldMap map, PlanetTerrainSource bare, LandingSite landing, bool? startWorld = null, WorldProfile profile = null)
    {
        bool start = startWorld ?? StartWorld;
        PoiLibrary lib = catalog.Library;
        PlanetSpec spec = map.Spec;
        // The whole planet as the placement engine sees it, and the theme its kind of world has.
        int cellsX = Math.Max(8, (int)(map.WidthMeters / WorldCell)), cellsZ = Math.Max(4, (int)(map.HeightMeters / WorldCell));
        PlanetMap pmap = BuildPlacementMap(map, 0, 0, cellsX, cellsZ, WorldCell, GHash.Of(spec.Seed, 0x504F49));
        ThemeDef theme = ThemeParser.For(lib, spec.Archetype.ToString(), map.Def.VariantName.Replace(' ', '_'), start);
        var world = WorldPlacement.Place(lib, pmap, theme, GHash.Of(spec.Seed, 0x504C4143), profile: profile);
        var placed = world.Pois.Where(p => !p.Family.Space).ToList();
        // Spread over the whole planet, but the landing site's country gets its share first.
        double Away(PlacedPoi p) => Dist(p.X, p.Z, landing.X, landing.Y, map.WidthMeters, map.HeightMeters);
        var byDistance = placed.OrderBy(Away).ToList();
        int near = MaxSites / 2;
        var far = byDistance.Skip(near).ToList();
        int stride = Math.Max(1, far.Count / Math.Max(1, MaxSites - near));
        var spread = far.Where((_, i) => i % stride == 0).ToList();
        // Nearest half, then an even spread out to the far side, then the rest in case some can't be seated.
        var order = byDistance.Take(near).Concat(spread).Concat(far.Except(spread)).ToList();
        // The ports come first and don't count against the rest: the main one above all, an exodus world's crowd round it.
        var ports = placed.Where(IsPort).OrderByDescending(p => p.Primary).ToList();
        order = ports.Concat(order.Where(p => !IsPort(p))).ToList();
        static bool IsPort(PlacedPoi p) => p.Primary || p.Port != null || p.Family.Tags.Contains("scrappy") || p.Family.Tags.Contains("evacuation");
        int budget = MaxSites + ports.Count;
        var sites = new List<PoiSite>();
        if (ForceFamily != null && lib.Families.FirstOrDefault(f => f.Id == ForceFamily) is { } forced)
        {
            // A screenshot's subject: the first spot round the landing site, 700 m out, where it can be seated.
            PoiState state = start && ThemeDef.FactionOwned(forced) ? PoiState.Ruined : forced.Core == CoreRule.Owned || forced.Tags.Contains("settlement") ? PoiState.Active : PoiState.Abandoned;
            int forcedVariant = profile != null && forced.Variants.Count > 1 ? profile.Variant(forced, state, new PRng(7)) : 0;
            PoiInstance inst = PoiFactory.Generate(lib, forced, forcedVariant, forced.Rarity, state, 7, world: profile);
            for (int k = 0; k < 12 && sites.Count == 0; k++)
            {
                double a = k * Math.PI / 6;
                PoiSite site = Seat(inst, forced, bare, map, Mathx.WrapX(landing.X + Math.Cos(a) * 700, map.WidthMeters), Mathx.Wrap(landing.Y + Math.Sin(a) * 700, map.HeightMeters), landing, sites);
                if (site != null) sites.Add(site);
            }
        }
        foreach (PlacedPoi p in order)
        {
            if (sites.Count >= budget + (ForceFamily != null ? 1 : 0)) break;
            int variant = p.Family.Variants.IndexOf(p.Variant);
            ulong spawnSeed = p.SpawnSeed;
            if (start)
            {
                // A start world: any blast site lies far from the bunker, and the settlements near it are never hostile.
                if (p.Family.Tags.Contains("radiation") && Away(p) < 3000) continue;
                // No living settlement near the start: the bunker sits at the edge of a ruined town by the
                // landing site, and survivors live further out (never hostile in the near country).
                if (p.Family.Tags.Contains("settlement") && Away(p) < 3000) continue;
                if (p.Family.Tags.Contains("settlement") && Away(p) < 4000)
                    for (ulong k = 1; k < 32 && Dressing.MoodFor(spawnSeed) == Mood.Hostile; k++) spawnSeed = PoiGenerator.Gen.Hash.Combine(p.SpawnSeed, k);
            }
            PoiInstance inst = PoiFactory.Generate(lib, p.Family, variant, p.Rarity, p.State, spawnSeed, world: profile);
            PoiSite site = Seat(inst, p.Family, bare, map, Mathx.WrapX(p.X, map.WidthMeters), Mathx.Wrap(p.Z, map.HeightMeters), landing, sites);
            // The main port stands somewhere: if its spot won't take it, the next good ground round it. So do its crowd, nearer in.
            int retries = p.Primary ? 64 : IsPort(p) ? 12 : 0;
            for (int k = 0; site == null && k < retries; k++)
            {
                double a = k * 2.399963, r = 300 + (p.Primary ? 90 : 60) * k;
                site = Seat(inst, p.Family, bare, map, Mathx.WrapX(p.X + Math.Cos(a) * r, map.WidthMeters), Mathx.Wrap(p.Z + Math.Sin(a) * r, map.HeightMeters), landing, sites);
            }
            if (site == null) continue;
            site.Primary = p.Primary;
            sites.Add(site);
        }
        Blasts(sites, map);
        return sites;
    }

    /// <summary>
    /// Places inside a blast's radius take the blasted decay: roofs gone, the walls that faced
    /// ground zero stripped, interiors burnt, by how close they stand.
    /// </summary>
    static void Blasts(List<PoiSite> sites, WorldMap map)
    {
        foreach (PoiSite b in sites.Where(s => s.GroundZero != null).ToList())
        {
            var (gx, gz) = b.GroundZero!.Value;
            BlastZone zone = b.Instance.Layout.Extras.Blast!;
            foreach (PoiSite o in sites)
            {
                if (o == b) continue;
                double dx = Mathx.WrapDelta(o.Center.X - gx, map.WidthMeters), dz = Mathx.WrapDelta(o.Center.Z - gz, map.HeightMeters);
                float d = (float)Math.Sqrt(dx * dx + dz * dz), strength = zone.Strength(d);
                if (strength < 0.05f || d < 1f) continue;
                StateTransforms.Blast(o.Instance, (float)(-dx / d), (float)(-dz / d), strength, new PRng(PoiGenerator.Gen.Hash.Combine(o.Instance.SpawnSeed, 0xB1A57UL)));
            }
        }
    }

    /// <summary>The placement engine's view of the country: biome, territory and rich ground per 40 m cell.</summary>
    static PlanetMap BuildPlacementMap(WorldMap map, double x0, double z0, int width, int height, float cell, ulong seed)
    {
        int side = Math.Min(width, height);
        var pmap = new PlanetMap
        {
            Name = map.Spec.ToString(), Size = PlanetSize.Large, Width = width, Height = height, CellSize = cell,
            Biomes = new PBiome[width * height], Territory = new string[width * height], Deposit = new bool[width * height],
        };
        var rng = new PRng(seed);
        string[] factions = { "vharn", "meridian", "freeholders", "kessari" };
        var capitals = new List<(float x, float y, string f)>();
        foreach (string f in factions)
            for (int i = rng.Between(1, 2); i > 0; i--)
                capitals.Add((rng.Float(0.1f, 0.9f) * width, rng.Float(0.1f, 0.9f) * height, f));
        float reach = side * 0.32f;

        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            double wx = Mathx.WrapX(x0 + (x + 0.5) * cell, map.WidthMeters), wz = Mathx.Wrap(z0 + (y + 0.5) * cell, map.HeightMeters);
            int k = pmap.Index(x, y);
            pmap.Biomes[k] = ToPoiBiome((GBiome)map.Biome[map.CellAt(wx, wz)], map.Def.Archetype);
            var nearest = capitals.OrderBy(c => (c.x - x) * (c.x - x) + (c.y - y) * (c.y - y)).First();
            float d = MathF.Sqrt((nearest.x - x) * (nearest.x - x) + (nearest.y - y) * (nearest.y - y));
            pmap.Territory[k] = d < reach ? nearest.f : "wild";
            int signature = 0, all = 0;
            foreach (Deposit dep in map.Deposits.InRect(wx - cell / 2, wz - cell / 2, wx + cell / 2, wz + cell / 2))
            {
                if (Math.Abs(Mathx.WrapDelta(dep.X - wx, map.WidthMeters)) > cell / 2 || Math.Abs(Mathx.WrapDelta(dep.Y - wz, map.HeightMeters)) > cell / 2) continue;
                all++;
                if (dep.Kind >= DepositKind.Titanium) signature++;
            }
            pmap.Deposit[k] = signature > 0 || all >= 3;
        }
        return pmap;
    }

    /// <summary>The planet's biomes in the POI generator's six terms.</summary>
    public static PBiome ToPoiBiome(GBiome b, Archetype a) => b switch
    {
        GBiome.Grassland or GBiome.Meadow or GBiome.Forest or GBiome.PineForest or GBiome.Riverbank or GBiome.Jungle
            or GBiome.Rainforest or GBiome.Swamp or GBiome.CloudForest or GBiome.Scrub or GBiome.Oasis => PBiome.Steppe,
        GBiome.Tundra or GBiome.Cliff or GBiome.RedRock or GBiome.MesaTop or GBiome.CrystalPlain or GBiome.CrystalSpires or GBiome.Sinter or GBiome.Basalt => PBiome.Rock,
        GBiome.Snow or GBiome.Glacier or GBiome.Snowfield or GBiome.IceTundra or GBiome.BlueIce or GBiome.HotSpring or GBiome.FrozenSea => PBiome.Ice,
        GBiome.Ash or GBiome.LavaField or GBiome.Obsidian or GBiome.Sulfur => PBiome.Ash,
        GBiome.FungalForest or GBiome.SporeMarsh or GBiome.Mycelium or GBiome.GlowMoss => PBiome.Fungal,
        GBiome.Chalk => PBiome.Desert,
        _ => a == Archetype.Ice ? PBiome.Ice : PBiome.Desert,
    };

    /// <summary>What ground a family looks for, from its data (siting): shape follows purpose.</summary>
    static string Preference(FamilyDef family) => family.Siting is { Length: > 0 } s ? s : "any";

    static PoiSite Seat(PoiInstance inst, FamilyDef family, PlanetTerrainSource bare, WorldMap map, double wantX, double wantZ,
        LandingSite landing, List<PoiSite> others)
    {
        // Local extents: the occupied box (rooms above and below ground) on x and z.
        CellMask occupied = inst.Layout.Occupied;
        int lx0 = occupied.Min.X, lz0 = occupied.Min.Z, lx1 = occupied.Min.X + occupied.Size.X, lz1 = occupied.Min.Z + occupied.Size.Z;
        double halfX = (lx1 - lx0) * 0.5, halfZ = (lz1 - lz0) * 0.5;
        string want = Preference(family);
        double width = map.WidthMeters, height = map.HeightMeters;

        double bestScore = double.MaxValue, bestX = 0, bestZ = 0;
        int bestPad = 0;
        float bestCut = 0;
        string bestKind = "";
        var samples = new List<float>(64);
        for (double dz = -SearchRadius; dz <= SearchRadius; dz += 10)
        for (double dx = -SearchRadius; dx <= SearchRadius; dx += 10)
        {
            double cx = Mathx.WrapX(wantX + dx, width), cz = Mathx.Wrap(wantZ + dz, height);
            if (cx < SeamMargin || cx > width - SeamMargin || cz < SeamMargin || cz > height - SeamMargin) continue;
            // Keep the landing spot open, and keep places from crowding each other.
            if (Dist(cx, cz, landing.X, landing.Y, width, height) < 160) continue;
            bool crowded = false;
            foreach (PoiSite o in others)
                crowded |= Dist(cx, cz, (o.Stamp.MinX + o.Stamp.MaxX) * 0.5, (o.Stamp.MinZ + o.Stamp.MaxZ) * 0.5, width, height)
                           < Math.Max(140, Math.Max(o.Stamp.ReachX, o.Stamp.ReachZ) + Math.Max(halfX, halfZ) + Apron + 50); // stamps never pile up
            if (crowded) continue;

            // Ground across the footprint and apron.
            samples.Clear();
            bool wet = false;
            for (double sz = -halfZ - Apron; sz <= halfZ + Apron + 0.01; sz += (halfZ + Apron) / 3)
            for (double sx = -halfX - Apron; sx <= halfX + Apron + 0.01; sx += (halfX + Apron) / 3)
            {
                GroundSample g = bare.Sampler.Sample(cx + sx, cz + sz, 2);
                if (g.InRiver || g.Height < 2.5f) wet = true;
                samples.Add(g.Height);
            }
            if (wet) continue;
            samples.Sort();
            float median = samples[samples.Count / 2];
            int pad = (int)MathF.Round(median);
            float cut = MathF.Max(samples[^1] - pad, pad - samples[0]);
            if (cut > MaxCutFill) continue;

            // Prominence: higher than the ground 80 m around (a ridge) or lower (a valley).
            float ring = 0f;
            for (int a = 0; a < 8; a++)
                ring += bare.NaturalHeight(cx + Math.Cos(a * Math.PI / 4) * 80, cz + Math.Sin(a * Math.PI / 4) * 80);
            float prominence = pad - ring / 8f;

            double distance = Dist(cx, cz, wantX, wantZ, width, height);
            double score = want switch
            {
                "ridge" or "peak" => -prominence * 2.0 + cut * 1.5,                    // high ground with a view
                "hillside" => Math.Abs(cut - 8f) * 1.5 + Math.Abs(prominence) * 0.3,  // dug into a slope
                "open" => cut * 5.0 + Math.Abs(prominence) * 0.6,                     // runways and pads: wide, level, open
                "valley" => prominence * 2.0 + cut * 0.8 - Math.Min(cut, 8) * 0.6, // at the foot of a slope: some cut is the point
                "flat" => cut * 4.0,
                _ => cut * 2.0,
            };
            score += distance * 0.02;
            if (score < bestScore)
            {
                bestScore = score;
                bestX = cx;
                bestZ = cz;
                bestPad = pad;
                bestCut = cut;
                bestKind = prominence > 12 ? "ridge" : prominence < -12 ? "valley" : cut > 5 ? "hillside" : "flat";
            }
        }
        if (bestScore == double.MaxValue) return null;

        // Origin so the occupied box is centred on the chosen spot; local y 0 is the pad surface.
        int ox = (int)Math.Floor(bestX - (lx0 + halfX)), oz = (int)Math.Floor(bestZ - (lz0 + halfZ));
        var origin = new TInt3(ox, bestPad, oz);
        var footprint = new CellBox(
            new TInt3(ox + lx0, bestPad + occupied.Min.Y, oz + lz0),
            new TInt3(ox + lx1, bestPad + Math.Max(occupied.Min.Y + occupied.Size.Y, inst.Blocks.Min.Y + inst.Blocks.Size.Y), oz + lz1));

        // Half-buried ruins: the ground rises across the footprint away from the way in, so the
        // building sinks into it toward the back. Every room stays open (carved up to the tilt's top).
        PInt3 outward = inst.Layout.EntranceFacing.Vec();
        bool ruin = inst.State == PoiState.Ruined && family.Faction == PoiGenerator.Gen.Faction.None && family.Site == null;
        float tilt = ruin ? 0.08f + (PoiGenerator.Gen.Hash.Cell(new PInt3(origin.X, 0, origin.Z), 77) % 100) / 1000f : 0f;
        double depthAlong = Math.Abs(outward.X) * (lx1 - lx0) + Math.Abs(outward.Z) * (lz1 - lz0);
        int buryTop = ruin ? (int)Math.Ceiling(tilt * depthAlong) + 1 : 0;
        // Rooms below the pad (and the floor layer, which sits in the ground) stay open.
        int carveH = Math.Max(0, -occupied.Min.Y) + buryTop;
        bool[] carve = null;
        if (carveH > 0)
        {
            carve = new bool[carveH * occupied.Size.Z * occupied.Size.X];
            for (int y = 0; y < carveH; y++)
            for (int z = 0; z < occupied.Size.Z; z++)
            for (int x = 0; x < occupied.Size.X; x++)
                carve[(y * occupied.Size.Z + z) * occupied.Size.X + x] = occupied[new PInt3(occupied.Min.X + x, occupied.Min.Y + y, occupied.Min.Z + z)];
        }
        // Wide enough that cut and fill slopes stay walkable: the further the ground round the pad is
        // from the pad's height (a slope rising behind it, a drop below it), the longer the blend.
        // The blend has to take the whole difference between the pad and the ground at its outer
        // edge, so it grows until that difference fits at a walkable slope.
        float blend = Math.Clamp(bestCut * 2f + 8f, 10f, 120f);
        for (int pass = 0; pass < 4; pass++)
        {
            float edge = 0f;
            for (int k = 0; k < 48; k++)
                foreach (double f in new[] { 0.35, 0.7, 1.0 })
                {
                    double ang = k * Math.PI / 24;
                    double rx = Math.Cos(ang) * (halfX + Apron + blend * f), rz = Math.Sin(ang) * (halfZ + Apron + blend * f);
                    edge = MathF.Max(edge, MathF.Abs(bare.NaturalHeight(bestX + rx, bestZ + rz) - bestPad));
                }
            float wanted = Math.Clamp(edge * 1.8f, 10f, 120f);
            if (wanted <= blend) break;
            blend = wanted;
        }
        var stamp = new TerrainStamp(family.Name, ox + lx0, oz + lz0, ox + lx1, oz + lz1, bestPad, Apron, blend, width, height,
            ox + occupied.Min.X, bestPad + occupied.Min.Y, oz + occupied.Min.Z, occupied.Size.X, carveH, occupied.Size.Z, carve)
        {
            Surface = GroundFor(family, map.Def.Archetype), Wear = inst.Age switch { PoiGenerator.Gen.Age.New => 0.1f, PoiGenerator.Gen.Age.Preserved => 0.35f, _ => 0.85f },
        };

        // Grounding: shapes that sit the place in its land rather than on a flat pad.
        var extra = new List<TerrainStamp>();
        double fx0 = ox + lx0, fz0 = oz + lz0, fx1 = ox + lx1, fz1 = oz + lz1;
        if (ruin)
        {
            // The tilt hinges on the way in, not on the footprint's front edge: a wing can stand
            // forward of the door, and by the door the ground would already have risen over its sill.
            // The rise then starts a little past the way in, so the way in stays open.
            PInt3 door = inst.Layout.EntranceCell;
            int tx0 = outward.X < 0 ? Math.Max((int)fx0, ox + door.X + 1) : (int)fx0, tx1 = outward.X > 0 ? Math.Min((int)fx1, ox + door.X) : (int)fx1;
            int tz0 = outward.Z < 0 ? Math.Max((int)fz0, oz + door.Z + 1) : (int)fz0, tz1 = outward.Z > 0 ? Math.Min((int)fz1, oz + door.Z) : (int)fz1;
            double along = (ox + door.X + 0.5 - (tx0 + tx1) * 0.5) * -outward.X + (oz + door.Z + 0.5 - (tz0 + tz1) * 0.5) * -outward.Z;
            double halfAlong = Math.Abs(outward.X) * (tx1 - tx0) * 0.5 + Math.Abs(outward.Z) * (tz1 - tz0) * 0.5;
            extra.Add(new TerrainStamp(family.Name + " (half-buried)", tx0, tz0, tx1, tz1, bestPad, Apron, blend, width, height)
                { Shape = StampShape.Tilt, DirX = -outward.X, DirZ = -outward.Z, Grade = tilt, Shift = (float)Math.Max(0, along + halfAlong + 2.5) });
        }
        // A crater's bowl, so it shows in the far rings too (its rim is the place's own rubble).
        if (inst.Layout.Extras.Blast is { } blast)
        {
            int gx = ox + blast.Center.X, gz = oz + blast.Center.Z, rr = (int)Math.Ceiling(blast.Crater);
            extra.Add(new TerrainStamp(family.Name + " (crater)", gx - rr, gz - rr, gx + rr, gz + rr, bestPad, 0f, 16f, width, height)
                { Shape = StampShape.Bowl, Radius = blast.Crater, Depth = 6f, Rim = 0f });
        }
        // An impact gouge behind a wreck, ploughed out the way it came down.
        if (family.Tags.Contains("wreck"))
            extra.Add(new TerrainStamp(family.Name + " (furrow)", (int)fx0, (int)fz0, (int)fx1, (int)fz1, bestPad, 0f, 0f, width, height)
                { Shape = StampShape.Furrow, DirX = -outward.X, DirZ = -outward.Z, Length = 70f, Width = 14f, Depth = 3f });
        // Terraces stepping down the slope below a place on a hillside or in a valley.
        if (!ruin && bestKind is "hillside" or "valley" or "ridge")
        {
            (float dx, float dz) down = (0, 0);
            float lowest = float.MaxValue;
            foreach (var (dx, dz) in new[] { (1f, 0f), (-1f, 0f), (0f, 1f), (0f, -1f) })
            {
                float n = bare.NaturalHeight(bestX + dx * (halfX + Apron + 14), bestZ + dz * (halfZ + Apron + 14));
                if (n < lowest) { lowest = n; down = (dx, dz); }
            }
            float drop = bestPad - lowest;
            if (drop > 4f)
            {
                float across = Math.Abs(down.dz) * (float)(fx1 - fx0) + Math.Abs(down.dx) * (float)(fz1 - fz0);
                extra.Add(new TerrainStamp(family.Name + " (terraces)", (int)fx0, (int)fz0, (int)fx1, (int)fz1, bestPad, 0f, 0f, width, height)
                    { Shape = StampShape.Terraces, DirX = down.dx, DirZ = down.dz, Length = Math.Min(48f, drop / 1.5f * 6f), Width = across + 6f, Step = Math.Clamp(drop / 18f * 6f, 1f, 2.5f) });
            }
        }
        // An approach cut: a road bed out from the way in, into the hill or up onto a bank.
        {
            PInt3 ent = inst.Layout.EntranceCell;
            double ex = ox + ent.X + 0.5, ez = oz + ent.Z + 0.5;
            float ahead = bare.NaturalHeight(ex + outward.X * 24, ez + outward.Z * 24);
            float diff = ahead - bestPad;
            if (Math.Abs(diff) > 2.5f)
            {
                float len = Math.Clamp(Math.Abs(diff) / 0.35f, 10f, 40f);
                double fcx = (fx0 + fx1) * 0.5, fcz = (fz0 + fz1) * 0.5;
                float shift = (float)(-(ex - fcx) * outward.Z + (ez - fcz) * outward.X);
                extra.Add(new TerrainStamp(family.Name + " (approach)", (int)fx0, (int)fz0, (int)fx1, (int)fz1, bestPad, 0f, 0f, width, height)
                    { Shape = StampShape.Cut, DirX = outward.X, DirZ = outward.Z, Shift = shift, Length = len, Width = 7f, Grade = Math.Clamp(diff / len, -0.4f, 0.4f) });
            }
        }

        var gates = new List<PoiGate>();
        TInt3 W(PInt3 c) => new(ox + c.X, bestPad + c.Y, oz + c.Z);
        gates.Add(new PoiGate("entrance", W(inst.Layout.EntranceCell), outward.X, outward.Z));
        foreach (Marker m in inst.Markers.Where(m => m.Kind == "gate"))
            gates.Add(new PoiGate("gate", W(m.Cell), m.Note == "x" ? 1 : 0, m.Note == "z" ? 1 : 0));
        foreach (Breach b in inst.Layout.Extras.Breaches)
            gates.Add(new PoiGate("breach", W(b.SurfaceCell), 0, 0));
        PInt3 e = inst.Layout.EntranceCell;
        return new PoiSite
        {
            Name = $"{family.Name}: {inst.Layout.Name} ({inst.State.ToString().ToLowerInvariant()})",
            Family = family, Instance = inst, Origin = origin, Footprint = footprint, Stamp = stamp,
            Siting = bestKind, CutFill = bestCut, Entrance = new TInt3(ox + e.X, bestPad + e.Y, oz + e.Z), Gates = gates, Extra = extra, HalfBuried = ruin,
        };
    }

    /// <summary>
    /// What a place's ground is made of, by purpose: tarmac or concrete aprons where aircraft stood
    /// or a faction works, packed dirt for farms, settlements and gang camps, gravel for outposts,
    /// and the land's own ground for ruins and wrecks. A family can name its own (ground).
    /// </summary>
    public static StampSurface GroundFor(FamilyDef family, Archetype? world = null)
    {
        // On a desert world the land's own ground under a place is sand and trodden dirt, never a green patch.
        if (world == Archetype.Arid && GroundFor(family) == StampSurface.Natural) return StampSurface.Dirt;
        if (family.Ground is { Length: > 0 } g && Enum.TryParse(g, true, out StampSurface named)) return named;
        if (family.Tags.Contains("farm") || family.Tags.Contains("settlement") || family.Tags.Contains("gang")) return StampSurface.Dirt;
        if (family.Tags.Contains("alien") || family.Tags.Contains("wreck") || family.Tags.Contains("precursor")) return StampSurface.Natural;
        return family.Tier switch
        {
            PoiTier.Spaceport or PoiTier.City or PoiTier.Town or PoiTier.Base => StampSurface.Concrete,
            PoiTier.Outpost or PoiTier.Camp => StampSurface.Gravel,
            _ => StampSurface.Natural,
        };
    }

    static double Dist(double ax, double az, double bx, double bz, double w, double h)
    {
        double dx = Mathx.WrapDelta(ax - bx, w), dz = Mathx.WrapDelta(az - bz, h);
        return Math.Sqrt(dx * dx + dz * dz);
    }
}
