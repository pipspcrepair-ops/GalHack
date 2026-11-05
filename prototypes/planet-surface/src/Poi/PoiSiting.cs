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
    public FamilyDef Family;
    public PoiInstance Instance;

    /// <summary>World cell of the POI grid's origin: x and z canonical, y the pad surface.</summary>
    public TInt3 Origin;

    /// <summary>The cells the place occupies, above and below ground (anchored by the terrain).</summary>
    public CellBox Footprint;

    public TerrainStamp Stamp;

    /// <summary>Where it was fitted: ridge, valley, hillside or flat, and how much ground was cut and filled.</summary>
    public string Siting = "";
    public float CutFill;

    /// <summary>The cell a visitor stands in outside the way in, in world cells.</summary>
    public TInt3 Entrance;

    public TInt3 World(PInt3 local) => new(Origin.X + local.X, Origin.Y + local.Y, Origin.Z + local.Z);
}

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
    const float Apron = 4f;
    const float MaxCutFill = 16f;
    const double SearchRadius = 140;
    const double SeamMargin = 320;           // keeps POIs clear of the ghost band either side of a seam

    public static List<PoiSite> Place(PoiCatalog catalog, WorldMap map, PlanetTerrainSource bare, LandingSite landing)
    {
        PoiLibrary lib = catalog.Library;
        PlanetSpec spec = map.Spec;
        // The whole planet as the placement engine sees it, and the theme its kind of world has.
        int cellsX = Math.Max(8, (int)(map.WidthMeters / WorldCell)), cellsZ = Math.Max(4, (int)(map.HeightMeters / WorldCell));
        PlanetMap pmap = BuildPlacementMap(map, 0, 0, cellsX, cellsZ, WorldCell, GHash.Of(spec.Seed, 0x504F49));
        ThemeDef theme = ThemeParser.For(lib, spec.Archetype.ToString(), map.Def.VariantName.Replace(' ', '_'));
        var world = WorldPlacement.Place(lib, pmap, theme, GHash.Of(spec.Seed, 0x504C4143));
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
        if (ForceFamily != null && lib.Families.FirstOrDefault(f => f.Id == ForceFamily) is { } forced)
            order.Insert(0, new PlacedPoi { Family = forced, Variant = forced.Variants[0], State = PoiState.Abandoned, SpawnSeed = 7, X = (float)landing.X + 700f, Z = (float)landing.Y + 300f });

        var sites = new List<PoiSite>();
        foreach (PlacedPoi p in order)
        {
            if (sites.Count >= MaxSites + (ForceFamily != null ? 1 : 0)) break;
            int variant = p.Family.Variants.IndexOf(p.Variant);
            PoiInstance inst = PoiFactory.Generate(lib, p.Family, variant, p.Rarity, p.State, p.SpawnSeed);
            PoiSite site = Seat(inst, p.Family, bare, map, Mathx.WrapX(p.X, map.WidthMeters), Mathx.Wrap(p.Z, map.HeightMeters), landing, sites);
            if (site != null) sites.Add(site);
        }
        return sites;
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
                crowded |= Dist(cx, cz, (o.Stamp.MinX + o.Stamp.MaxX) * 0.5, (o.Stamp.MinZ + o.Stamp.MaxZ) * 0.5, width, height) < 140;
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

        // Rooms below the pad (and the floor layer, which sits in the ground) stay open.
        int carveH = Math.Max(0, -occupied.Min.Y);
        bool[] carve = null;
        if (carveH > 0)
        {
            carve = new bool[carveH * occupied.Size.Z * occupied.Size.X];
            for (int y = 0; y < carveH; y++)
            for (int z = 0; z < occupied.Size.Z; z++)
            for (int x = 0; x < occupied.Size.X; x++)
                carve[(y * occupied.Size.Z + z) * occupied.Size.X + x] = occupied[new PInt3(occupied.Min.X + x, occupied.Min.Y + y, occupied.Min.Z + z)];
        }
        float blend = Math.Clamp(bestCut * 1.7f + 6f, 8f, 34f);
        var stamp = new TerrainStamp(family.Name, ox + lx0, oz + lz0, ox + lx1, oz + lz1, bestPad, Apron, blend, width, height,
            ox + occupied.Min.X, bestPad + occupied.Min.Y, oz + occupied.Min.Z, occupied.Size.X, carveH, occupied.Size.Z, carve);

        PInt3 e = inst.Layout.EntranceCell;
        return new PoiSite
        {
            Name = $"{family.Name}: {inst.Layout.Name} ({inst.State.ToString().ToLowerInvariant()})",
            Family = family, Instance = inst, Origin = origin, Footprint = footprint, Stamp = stamp,
            Siting = bestKind, CutFill = bestCut, Entrance = new TInt3(ox + e.X, bestPad + e.Y, oz + e.Z),
        };
    }

    static double Dist(double ax, double az, double bx, double bz, double w, double h)
    {
        double dx = Mathx.WrapDelta(ax - bx, w), dz = Mathx.WrapDelta(az - bz, h);
        return Math.Sqrt(dx * dx + dz * dz);
    }
}
