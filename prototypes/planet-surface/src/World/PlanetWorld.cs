using System;
using System.Collections.Generic;
using System.Diagnostics;
using GalHack.Terrain;
using Godot;
using Planets.Gen;
using Planets.Look;
using PlanetSurface.Poi;
using PlanetSurface.Skies;
using PlanetSurface.Terrain;
using TerrainDigging.Terrain;
using PoiSite = PlanetSurface.Poi.PoiSite;
using WorldProfile = PoiGenerator.Gen.WorldProfile;

namespace PlanetSurface.World;

/// <summary>
/// Everything about one planet that the surface needs, built off the main thread when you
/// arrive: the generator's world map, the POIs placed on it and the pads they sit on, the voxel
/// terrain source and store, the ground palette, and the sky its system gives it.
/// </summary>
public sealed partial class PlanetWorld
{
    public readonly PlanetSpec Spec;
    public readonly WorldMap Map;
    public readonly GroundPalette Palette;
    public readonly PlanetTerrainSource Source;
    public readonly PlanetTerrain Terrain;
    public readonly LandingSite Landing;
    public readonly IReadOnlyList<PoiSite> Pois;
    public readonly SkySystem Sky;
    public readonly WorldLook Look;
    /// <summary>
    /// The world's profile, rolled first from its seed: whether it was ever inhabited, its old
    /// population, starport and trade, and two tech levels (low, mid or high): before the collapse
    /// (the old world: ruins, POI families, infrastructure, wrecked traffic, decorations) and now
    /// (the living: settlements and faction places, their gear, defences and animals).
    /// </summary>
    public readonly WorldProfile Profile;
    /// <summary>
    /// The world round the places, when its code is compiled in (src/World/Infra: the infrastructure
    /// network, see PlanetWorld.Infra.cs). Projects that compile only this file build worlds without it.
    /// </summary>
    readonly object _around;
    public readonly double BuildMs;

    public double Width => Map.WidthMeters;

    /// <summary>This world's star and everything round it: the one source of truth for the sky, in every view.</summary>
    public StarSystem StarSystem
    {
        get
        {
            if (_system != null) return _system;
            ulong seed = 0;
            SystemSeedOf(ref seed);
            return _system = StarSystem.For(Spec, Sky, Width, seed);
        }
    }
    StarSystem _system;

    /// <summary>The seed the galaxy's catalogue has for this world's star, when that code is compiled in (see PlanetWorld.Galaxy.cs).</summary>
    partial void SystemSeedOf(ref ulong seed);
    public double Height => Map.HeightMeters;

    /// <summary>
    /// Trees felled on this planet, by scatter id (<see cref="Terrain.FarBuilder.ScatterId"/>): gone
    /// at every level of detail, through streaming, saves and loads, as terrain edits are. Broken ore
    /// tell rocks keep their (negative) ids here too.
    /// </summary>
    public readonly HashSet<long> Felled = new();

    /// <summary>Where the felled trees are saved: beside the terrain's save, written the same moment.</summary>
    public string FelledPath => SavePath + ".felled";

    /// <summary>The colour a place's rubble takes: the planet's own rock.</summary>
    public PoiGenerator.Gen.Rgb RubbleRock => new(Rgb.Byte(Palette.Rock.R), Rgb.Byte(Palette.Rock.G), Rgb.Byte(Palette.Rock.B));

    PoiGenerator.Gen.Rgb? _weed;
    bool _weedKnown;

    /// <summary>The colour weeds take in a place's cracks: the planet's own low growth, or null on a world where nothing grows.</summary>
    public PoiGenerator.Gen.Rgb? Weed
    {
        get
        {
            if (_weedKnown) return _weed;
            _weedKnown = true;
            foreach (byte b in new HashSet<byte>(Map.Biome))
                foreach (FloraRule r in BiomeLook.FloraOf((Biome)b))
                    if (r.Kind == Flora.Shrub)
                    {
                        Rgb c = r.Color.Saturate(0.6f) * 0.8f;
                        return _weed = new PoiGenerator.Gen.Rgb(Rgb.Byte(c.R), Rgb.Byte(c.G), Rgb.Byte(c.B));
                    }
            return null;
        }
    }

    /// <summary>The colour of what the tool knocks off a cell of ground at a point (frame coordinates): what that cell is made of, here.</summary>
    public Color ChipColor(CellMaterial material, Vector3 at)
    {
        double x = Mathx.WrapX(at.X, Width), z = Mathx.Wrap(at.Z, Height);
        var cell = new TerrainDigging.Terrain.Int3((int)Math.Floor(x), (int)Math.Floor(at.Y), (int)Math.Floor(z));
        return Palette.Chip(material, x, z, at.Y, material == CellMaterial.Ore ? Source.OreKindAt(cell) : DepositKind.Iron);
    }

    /// <summary>Grit off the land at a point: its rock, half clear.</summary>
    public Color Grit(Vector3 at)
    {
        Color c = ChipColor(CellMaterial.Rock, at);
        return new Color(c.R, c.G, c.B, 0.5f);
    }

    /// <summary>Writes the felled trees atomically (a temporary file renamed over the old one).</summary>
    public void SaveFelled()
    {
        var ids = new List<long>(Felled);
        ids.Sort();
        var text = new System.Text.StringBuilder("galhack.felled 1\n");
        foreach (long id in ids) text.Append(id).Append('\n');
        string tmp = FelledPath + ".tmp";
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FelledPath));
        System.IO.File.WriteAllText(tmp, text.ToString());
        System.IO.File.Move(tmp, FelledPath, overwrite: true);
    }

    /// <summary>Reads the felled trees back (none if there is no file). False if the file is unreadable: nothing changes.</summary>
    public bool LoadFelled()
    {
        if (!System.IO.File.Exists(FelledPath))
        {
            Felled.Clear();
            return true;
        }
        string[] lines = System.IO.File.ReadAllLines(FelledPath);
        if (lines.Length == 0 || lines[0] != "galhack.felled 1") return false;
        var ids = new HashSet<long>();
        for (int i = 1; i < lines.Length; i++)
        {
            if (lines[i].Length == 0) continue;
            if (!long.TryParse(lines[i], out long id)) return false;
            ids.Add(id);
        }
        Felled.Clear();
        Felled.UnionWith(ids);
        return true;
    }

    PlanetWorld(PlanetSpec spec, WorldProfile profile, WorldMap map, LandingSite landing, IReadOnlyList<PoiSite> pois, object around, PlanetTerrainSource source, double ms)
    {
        Spec = spec;
        Profile = profile;
        _around = around;
        Map = map;
        Landing = landing;
        Pois = pois;
        Source = source;
        Terrain = new PlanetTerrain(source);
        Palette = new GroundPalette(map);
        // Blast sites' dead zones show in the ground's colour, fading back to the land's own.
        foreach (PoiSite p in pois)
            if (p.GroundZero is (double gx, double gz) && p.Instance.Layout.Extras.Blast is { } zone)
                Palette.DeadZones.Add((gx, gz, zone.Dead * 3f, zone.Fade * 4f));
        // The land comes back through the ruins as the planet's own flora, and their rubble is its rock.
        Palette.Planted = RuinGrowth.Plan(pois, source, Palette, map.WidthMeters, map.HeightMeters);
        foreach (PoiSite p in pois) PoiGenerator.Gen.Growth.Earthen(p.Instance, RubbleRock, Weed);
        Sky = SkySystem.For(spec);
        Look = WorldLook.For(map, Sky);
        BuildMs = ms;
    }

    /// <summary>Generates the planet, finds where to land, places and seats its POIs. Runs on a worker.</summary>
    public static PlanetWorld Build(PlanetSpec spec, PoiCatalog catalog)
    {
        var watch = Stopwatch.StartNew();
        WorldProfile profile = ProfileFor(spec, PoiSiting.StartWorld);
        WorldMap map = PlanetGenerator.Generate(spec);
        LandingSite landing = Planets.Gen.Landing.Find(map, map.WidthMeters * 0.5, map.HeightMeters * 0.5);
        var bare = new PlanetTerrainSource(map);
        List<PoiSite> pois = catalog == null ? new List<PoiSite>() : PoiSiting.Place(catalog, map, bare, landing, profile: profile);
        var stamps = new List<TerrainStamp>();
        var anchors = new List<TerrainDigging.Terrain.PoiSite>();
        foreach (PoiSite p in pois)
        {
            stamps.AddRange(p.Stamps);   // the pad, then whatever grounds it (bowl, terraces, approach cut, tilt, furrow)
            anchors.Add(new TerrainDigging.Terrain.PoiSite(p.Name, p.Footprint));
        }
        // The world round the places: planned on the bare land, its stamps laid after the places' own,
        // then finished on the final ground (see PlanetWorld.Infra.cs; nothing when that isn't compiled in).
        object around = null;
        PlanAround(spec, catalog, map, bare, pois, profile, landing, stamps, ref around);
        var source = new PlanetTerrainSource(map, stamps, anchors);
        FinishAround(spec, source, pois, around);
        // What stands off a place's footprint goes by the real ground, now that it's known.
        foreach (PoiSite p in pois)
            PoiGenerator.Gen.StateTransforms.Settle(p.Instance, (x, z) => source.SurfaceHeight(Mathx.WrapX(p.Origin.X + x + 0.5, source.Width), Mathx.Wrap(p.Origin.Z + z + 0.5, source.Height)) - p.Origin.Y, 3);
        var world = new PlanetWorld(spec, profile, map, landing, pois, around, source, watch.Elapsed.TotalMilliseconds);
        world.ChartStars();
        return world;
    }

    /// <summary>Finds this world in the galaxy, off the main thread with the rest of the world (nothing when that isn't compiled in).</summary>
    public void ChartStars() => FindHomeStar();

    partial void FindHomeStar();

    static partial void PlanAround(PlanetSpec spec, PoiCatalog catalog, WorldMap map, PlanetTerrainSource bare, List<PoiSite> pois, WorldProfile profile, LandingSite landing, List<TerrainStamp> stamps, ref object around);
    static partial void FinishAround(PlanetSpec spec, PlanetTerrainSource source, List<PoiSite> pois, object around);

    /// <summary>
    /// A planet's world profile: rolled from its seed and kind, the first thing generated. Start
    /// worlds were always inhabited (their survivors live among the old world's ruins). The same
    /// spec always rolls the same profile.
    /// </summary>
    public static WorldProfile ProfileFor(PlanetSpec spec, bool startWorld = false) =>
        WorldProfile.Roll(Planets.Gen.Hash.Of(spec.Seed, 0x50F1), spec.Archetype.ToString(), startWorld);

    /// <summary>
    /// The folder planets' saves go in. Tests give each run a folder of its own: user:// is shared
    /// by every checkout of the repo on a machine (they share .tools, where it lives), so two test
    /// runs at once would otherwise load and delete each other's saves.
    /// </summary>
    public static string SaveDirectory = "user://planets";

    /// <summary>Where saves for this planet's edits live: one file per terrain generator id.</summary>
    public string SavePath
    {
        get
        {
            ulong h = 14695981039346656037UL;
            foreach (char c in Source.GeneratorId) h = (h ^ c) * 1099511628211UL;
            return ProjectSettings.GlobalizePath($"{SaveDirectory}/{Spec.Archetype}-{Spec.Seed}-{h:x16}.ghs");
        }
    }

    /// <summary>A canonical position (x, z in [0, size)) moved by whole planets to the copy nearest a frame position.</summary>
    public Vector3 NearestCopy(Vector3 canonical, Vector3 near) => new(
        (float)TerrainStreamer.NearestCopy(canonical.X, 0, near.X, Width),
        canonical.Y,
        (float)TerrainStreamer.NearestCopy(canonical.Z, 0, near.Z, Height));

    /// <summary>A frame position wrapped onto the planet.</summary>
    public Vector3 Canonical(Vector3 p) => new((float)Mathx.WrapX(p.X, Width), p.Y, (float)Mathx.Wrap(p.Z, Height));

    public float GroundHeight(double x, double z) => Source.SurfaceHeight(x, z);

    /// <summary>
    /// The water surface over a point's column (the sea, a river), or null on dry ground. Water is
    /// a volume: anything under that surface is in it. A lava world's seas are lava, not water, and
    /// aren't entered.
    /// </summary>
    public float? WaterSurfaceAt(Vector3 p) =>
        !Look.Lava && Source.WaterSurfaceAt(Mathx.WrapX(p.X, Width), Mathx.Wrap(p.Z, Height), out float surface) ? surface : null;

    /// <summary>A sea mask for the water shader: depth below sea level per world-map cell.</summary>
    public ImageTexture SeaTexture()
    {
        var bytes = new byte[Map.Count];
        for (int k = 0; k < Map.Count; k++) bytes[k] = Rgb.Byte(Math.Clamp(-Map.Elevation[k] / 120f, 0f, 1f));
        return ImageTexture.CreateFromImage(Image.CreateFromData(Map.Width, Map.Height, false, Image.Format.R8, bytes));
    }
}

/// <summary>
/// How a world's ground and liquids look, beyond the palette: where snow lies, what dust looks
/// like, what glows, and what fills its seas.
/// </summary>
public sealed record WorldLook
{
    public float SnowAltitude { get; init; } = 100000f;
    public float BaseSnow { get; init; }
    public Color Dust { get; init; } = new("9c7e5a");
    public Color Glow { get; init; } = new("ff5212");
    public Color Shallow { get; init; }
    public Color Deep { get; init; }
    public bool Lava { get; init; }
    public float LiquidGlow { get; init; }

    public static WorldLook For(WorldMap map, SkySystem sky)
    {
        ArchetypeDef def = map.Def;
        return (def.Archetype, def.Variant) switch
        {
            (Archetype.Temperate, 0) => new() { SnowAltitude = 380f, Shallow = new("2e7a80"), Deep = new("0c2c40") },
            (Archetype.Temperate, _) => new() { SnowAltitude = 500f, Shallow = new("2f7c6c"), Deep = new("0c3236") },
            (Archetype.Arid, _) => new() { SnowAltitude = 640f, Dust = new("b98a56"), Shallow = new("3a8c8a"), Deep = new("12384a") },
            (Archetype.Ice, 0) => new() { BaseSnow = 0.55f, SnowAltitude = 60f, Dust = new("c8ccd0"), Shallow = new("2c5a78"), Deep = new("0a1c34") },
            (Archetype.Ice, _) => new() { BaseSnow = 0.35f, SnowAltitude = 150f, Dust = new("c0c4c8"), Shallow = new("2a6a7c"), Deep = new("0a2236"), Glow = new("40fff0") },
            (Archetype.Lava, _) => new() { Dust = new("4a4442"), Shallow = new("8a1a0a"), Deep = new("ff6a18"), Lava = true, LiquidGlow = 2.2f },
            (Archetype.Alien, 0) => new() { SnowAltitude = 500f, Dust = new("a898b8"), Shallow = new("2a8a86"), Deep = new("0c2a3a"), Glow = new("c070ff"), LiquidGlow = 0.25f },
            _ => new() { SnowAltitude = 500f, Dust = new("8a8a5a"), Shallow = new("5a3a7a"), Deep = new("1a0c30"), Glow = new("30ffc0"), LiquidGlow = 0.25f },
        };
    }
}
