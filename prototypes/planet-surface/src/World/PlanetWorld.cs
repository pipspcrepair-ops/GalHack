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
    public SkySystem Sky { get; private set; }
    public readonly WorldLook Look;

    /// <summary>Every plant on this world, rolled from its seed, climate, air and star: scatter, ruins, cover and weeds all read it.</summary>
    public readonly PlanetFlora Flora;

    /// <summary>
    /// The world's profile, rolled first from its seed: whether it was ever inhabited, its old
    /// population, starport and trade, and two tech levels (low, mid or high): before the collapse
    /// (the old world: ruins, POI families, infrastructure, wrecked traffic, decorations) and now
    /// (the living: settlements and faction places, their gear, defences and animals).
    /// </summary>
    public readonly WorldProfile Profile;

    /// <summary>How deep in the galaxy this world lies, 0 on the rim to 1 at the core, from its star's real place (-1 where the galaxy isn't compiled in).</summary>
    public float Depth { get; private set; } = -1f;

    /// <summary>What this world rolled, layer by layer (null for a world nobody ever lived on): its places are laid out from it.</summary>
    public PoiGenerator.Gen.WorldRoll Roll { get; private set; }

    /// <summary>What this world rolled, lived on or not (null only where no layers are compiled in): the same roll as <see cref="Roll"/> where there is one.</summary>
    public PoiGenerator.Gen.WorldRoll Layers { get; private set; }

    /// <summary>The POI library the world was built from (its dressing table among it); null where none was given.</summary>
    public PoiGenerator.Gen.PoiLibrary Library { get; private set; }

    /// <summary>
    /// What the rest of the world reads off its layers: its plants, creatures, paint and sound, and
    /// the hooks others read (<see cref="PoiGenerator.Gen.WorldReading.Age"/> and <see cref="PoiGenerator.Gen.WorldReading.Severity"/>
    /// for decay, <see cref="PoiGenerator.Gen.WorldReading.Storm"/> and <see cref="PoiGenerator.Gen.WorldReading.Dry"/> for the weather).
    /// </summary>
    public readonly PoiGenerator.Gen.WorldReading Reading;

    /// <summary>
    /// Every blast on this world, one record each (<see cref="PoiGenerator.Gen.Strike"/>): a blast site's
    /// own, and where a war's strikes came down on its targets. The craters in the ground, the decay of
    /// the places round them, the dead ash ground, the thinned plants and the map all read these, so
    /// they always agree. Empty on a world no blast touched.
    /// </summary>
    public readonly IReadOnlyList<PoiGenerator.Gen.Strike> Blasts;

    /// <summary>What the war's blasts left in the ground here, RAD (a nuclear crater and its ring; 0 elsewhere): added to the world's background by the suit.</summary>
    public float RadiationAt(double x, double z)
    {
        float rad = 0f;
        foreach (PoiGenerator.Gen.Strike b in Blasts)
        {
            if (b.Rad <= 0f) continue;
            double dx = Mathx.WrapDelta(x - b.X, Map.WidthMeters), dz = Mathx.WrapDelta(z - b.Z, Map.HeightMeters);
            if (Math.Abs(dx) > b.Dead || Math.Abs(dz) > b.Dead) continue;
            rad = MathF.Max(rad, b.RadiationAt((float)Math.Sqrt(dx * dx + dz * dz)));
        }
        return rad;
    }

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
            if (Flora.AnyLowGrowth() is not Rgb low) return null;
            Rgb c = low.Saturate(0.8f) * 0.85f;
            return _weed = new PoiGenerator.Gen.Rgb(Rgb.Byte(c.R), Rgb.Byte(c.G), Rgb.Byte(c.B));
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

    PlanetWorld(PlanetSpec spec, WorldProfile profile, WorldMap map, LandingSite landing, IReadOnlyList<PoiSite> pois, object around, PlanetTerrainSource source, double ms, PoiGenerator.Gen.WorldReading reading,
        IReadOnlyList<PoiGenerator.Gen.Strike> blasts)
    {
        Spec = spec;
        Profile = profile;
        Reading = reading ?? PoiGenerator.Gen.WorldReading.Neutral;
        _around = around;
        Map = map;
        Landing = landing;
        Pois = pois;
        Blasts = blasts ?? Array.Empty<PoiGenerator.Gen.Strike>();
        Source = source;
        Terrain = new PlanetTerrain(source);
        Sky = SkySystem.For(spec);
        // What grows here: the type's, as its layers have shaped it (overgrown, dead, swollen, fungal, burned, sparse, stranger).
        var r = Reading;
        Flora = PlanetFlora.For(map, Sky, mood: new GalHack.Plants.FloraMood(r.Overgrown, r.Dead, r.Swollen, r.Fungal, r.Burnt, r.Sparse), lean: r.Alien);
        Palette = new GroundPalette(map, Flora);
        // Every blast's dead zone shows in the ground's colour, fading back to the land's own, and nothing grows
        // in it (the scatter and the cover read the same zones); its crater shows on the map as in the ground.
        // An old shell hole's dead ground has all grown back: only its bowl is left.
        foreach (PoiGenerator.Gen.Strike b in Blasts)
        {
            if (b.Faded > 0f) Palette.DeadZones.Add((b.X, b.Z, b.Dead, b.Faded));
            if (b.Bowl) Palette.Craters.Add((b.X, b.Z, b.Crater, b.Depth, b.Rim));
        }
        Palette.IndexBlasts();
        // The land comes back through the ruins as the planet's own flora, and their rubble is its rock.
        // As each place is built: its rubble the land's rock, its weeds the land's, and the trees through it (RuinGrowth).
        Palette.Planted = new System.Collections.Concurrent.ConcurrentDictionary<long, PlantedFlora>();
        foreach (PoiSite p in pois)
        {
            PoiSite site = p;
            site.Fit.Add(inst =>
            {
                RuinGrowth.Plant(site, inst, source, Palette, map.WidthMeters, map.HeightMeters);
                PoiGenerator.Gen.Growth.Earthen(inst, RubbleRock, Weed);
            });
        }
        Look = WorldLook.For(map, Sky);
        BuildMs = ms;
    }

    /// <summary>Generates the planet, finds where to land, places and seats its POIs. Runs on a worker.</summary>
    public static PlanetWorld Build(PlanetSpec spec, PoiCatalog catalog)
    {
        var watch = Stopwatch.StartNew();
        // From here until the game says its sleeper stands, a place built because something read it unasked is counted (PoiSite.Loading).
        PoiSite.Loading = true;
        PoiSite.UnaskedLoadBuilds = 0;
        PoiGenerator.Gen.WorldForce force = PoiGenerator.Gen.WorldForce.Current;
        WorldProfile profile = force.Apply(ProfileFor(spec, PoiSiting.StartWorld, force));
        // Its star first: where it lies in the galaxy is part of what the world rolls from.
        object chart = null;
        float depth = -1f;
        Chart(spec, force.Depth, ref chart, ref depth);
        // Only a start world (or one a depth was asked for) has its real place yet: any other is found
        // a rim star for its sky, so its zone is still rolled by the odds rather than read off that star.
        float rollDepth = PoiSiting.StartWorld || force.Depth != null ? depth : -1f;
        double Lap() { double t = watch.Elapsed.TotalMilliseconds; return t; }
        WorldMap map = PlanetGenerator.Generate(spec);
        double tMap = Lap();
        LandingSite landing = Planets.Gen.Landing.Find(map, map.WidthMeters * 0.5, map.HeightMeters * 0.5);
        var bare = new PlanetTerrainSource(map);
        PoiGenerator.Gen.WorldRoll roll = catalog == null ? null : PoiSiting.RollOf(catalog.Library, map, profile, depth: rollDepth);
        PoiGenerator.Gen.WorldRoll layers = roll ?? (catalog == null ? null : PoiSiting.LayersOf(catalog.Library, map, profile, depth: rollDepth));
        if (roll != null) profile = profile with { Barred = roll.Barred };
        List<PoiSite> pois = catalog == null ? new List<PoiSite>() : PoiSiting.Place(catalog, map, bare, landing, profile: profile, depth: rollDepth);
        double tPlaces = Lap();
        var stamps = new List<TerrainStamp>();
        var anchors = new List<TerrainDigging.Terrain.PoiSite>();
        var seated = new List<PoiSite>(pois);
        foreach (PoiSite p in pois)
        {
            stamps.AddRange(p.Stamps);   // the pad, then whatever grounds it (bowl, terraces, approach cut, tilt, furrow)
            anchors.Add(new TerrainDigging.Terrain.PoiSite(p.Name, p.Footprint));
        }
        // The world round the places: planned on the bare land, its stamps laid after the places' own,
        // then finished on the final ground (see PlanetWorld.Infra.cs; nothing when that isn't compiled in).
        // A war's strikes are planned there too, on its targets, with the craters they punched.
        object around = null;
        List<PoiGenerator.Gen.Strike> blasts = PoiSiting.SiteBlasts(pois);
        PlanAround(spec, catalog, map, bare, pois, profile, landing, stamps, roll, blasts, ref around);
        // A ground burst in a town's middle took the places in its bowl: nothing holds the ground for them now.
        if (pois.Count != seated.Count || !seated.TrueForAll(pois.Contains))
        {
            var standing = new HashSet<PoiSite>(pois);
            var kept = new List<TerrainDigging.Terrain.PoiSite>();
            for (int i = 0; i < anchors.Count; i++) if (standing.Contains(seated[i])) kept.Add(anchors[i]);
            anchors = kept;
        }
        // Every place inside a blast's reach takes it, the city's buildings with the rest.
        double tAround = Lap();
        PoiSiting.Blast(pois, blasts, map.WidthMeters, map.HeightMeters);
        var source = new PlanetTerrainSource(map, stamps, anchors);
        FinishAround(spec, source, pois, around);
        // What stands off a place's footprint goes by the real ground, now that it's known.
        foreach (PoiSite p in pois)
        {
            PoiSite site = p;
            site.Fit.Add(inst => PoiGenerator.Gen.StateTransforms.Settle(inst, (x, z) => source.SurfaceHeight(Mathx.WrapX(site.Origin.X + x + 0.5, source.Width), Mathx.Wrap(site.Origin.Z + z + 0.5, source.Height)) - site.Origin.Y, 3));
        }
        double tEnd = Lap();
        LastTimes = $"map {tMap / 1000:F1} s, places {(tPlaces - tMap) / 1000:F1} s (plan {PoiSiting.LastPlanMs / 1000:F1}, layouts {PoiSiting.LastLayoutMs / 1000:F1}, seats {PoiSiting.LastSeatMs / 1000:F1} s), around {(tAround - tPlaces) / 1000:F1} s [{LastAroundTimes}], finish {(tEnd - tAround) / 1000:F1} s";
        var world = new PlanetWorld(spec, profile, map, landing, pois, around, source, watch.Elapsed.TotalMilliseconds, PoiGenerator.Gen.WorldReading.Of(layers), blasts) { Depth = depth, Roll = roll, Layers = layers, Library = catalog?.Library };
        world.TakeChart(chart);
        world.ChartStars();
        return world;
    }

    /// <summary>Finds this world in the galaxy, off the main thread with the rest of the world (nothing when that isn't compiled in).</summary>
    public void ChartStars() => FindHomeStar();

    partial void FindHomeStar();

    /// <summary>This world's star in the galaxy, found before anything is placed (near a depth asked for, or out at an arm's tip), and how deep it lies; nothing when that isn't compiled in.</summary>
    static partial void Chart(PlanetSpec spec, float? wanted, ref object chart, ref float depth);
    partial void TakeChart(object chart);

    /// <param name="blasts">the world's blasts so far (its blast sites'); a war's strikes are added to it</param>
    static partial void PlanAround(PlanetSpec spec, PoiCatalog catalog, WorldMap map, PlanetTerrainSource bare, List<PoiSite> pois, WorldProfile profile, LandingSite landing, List<TerrainStamp> stamps,
        PoiGenerator.Gen.WorldRoll roll, List<PoiGenerator.Gen.Strike> blasts, ref object around);
    static partial void FinishAround(PlanetSpec spec, PlanetTerrainSource source, List<PoiSite> pois, object around);

    /// <summary>
    /// A planet's world profile: rolled from its seed and kind, the first thing generated. Start
    /// worlds were always inhabited (their survivors live among the old world's ruins). The same
    /// spec always rolls the same profile.
    /// </summary>
    /// <summary>Where the last world's build time went, phase by phase (tests print it).</summary>
    public static string LastTimes = "";

    /// <summary>Where the last world's infrastructure time went, s (tests print it; empty where the infrastructure isn't compiled in).</summary>
    public static string LastAroundTimes = "";

    public static WorldProfile ProfileFor(PlanetSpec spec, bool startWorld = false, PoiGenerator.Gen.WorldForce force = null) =>
        WorldProfile.Roll(Planets.Gen.Hash.Of(spec.Seed, 0x50F1), spec.Archetype.ToString(), startWorld, force?.Population, force?.PopulationNow);

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

    /// <summary>
    /// The sea mask for the water shader: metres below sea level per world-map cell (negative on land),
    /// one texel a cell, so the shader's filtered read is the very bilinear blend the terrain and the
    /// physics take of the same cells. The sea is drawn exactly where it is, deeper than
    /// <see cref="PlanetTerrainSource.SeaMaskDepth"/>: the ground is raised just clear of it everywhere
    /// else, and no edge of it hangs over a dip the mask left out. Half floats, never bytes: a byte's
    /// step was half a metre of depth, the size of the sea's own margin.
    /// </summary>
    public ImageTexture SeaTexture()
    {
        var bytes = new byte[Map.Count * 2];
        for (int k = 0; k < Map.Count; k++)
            BitConverter.TryWriteBytes(new Span<byte>(bytes, k * 2, 2), (Half)Math.Clamp(-Map.Elevation[k], -60000f, 60000f));
        return ImageTexture.CreateFromImage(Image.CreateFromData(Map.Width, Map.Height, false, Image.Format.Rh, bytes));
    }

    /// <summary>What the water shader reads at a point: the sea mask's texels blended as its linear filter blends them (tests).</summary>
    public static float SeaMaskAt(Image mask, double x, double z, double planetWidth, double planetHeight)
    {
        int w = mask.GetWidth(), h = mask.GetHeight();
        double gx = x / planetWidth * w - 0.5, gz = z / planetHeight * h - 0.5;
        int i0 = (int)Math.Floor(gx), j0 = (int)Math.Floor(gz);
        float tx = (float)(gx - i0), tz = (float)(gz - j0);
        float At(int i, int j) => mask.GetPixel(((i % w) + w) % w, ((j % h) + h) % h).R;
        return Mathf.Lerp(Mathf.Lerp(At(i0, j0), At(i0 + 1, j0), tx), Mathf.Lerp(At(i0, j0 + 1), At(i0 + 1, j0 + 1), tx), tz);
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
