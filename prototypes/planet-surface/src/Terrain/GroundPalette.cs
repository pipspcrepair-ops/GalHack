using System.Collections.Generic;
using System;
using System.Linq;
using GalHack.Terrain;
using Planets.Gen;
using Planets.Look;
using TerrainDigging.Terrain;
using Godot;

namespace PlanetSurface.Terrain;

/// <summary>
/// The colours of the ground, shared by the voxel chunks near the player and the heightfield
/// rings beyond them so the two meet without a visible line. Soil takes its biome's colour,
/// blended across wandering biome borders exactly the way the planet generator blends its map;
/// rock, deep rock and bedrock have one colour per world; ore takes its deposit's colour.
///
/// Colours are grounded and readable (soil, sand, ash, snow, stone), each world keeping its own
/// character: red sandstone canyons, black basalt, violet crystal rock, pale mycelium flats.
/// Read-only after construction; mesh workers share it.
/// </summary>
public sealed class GroundPalette
{
    public readonly WorldMap Map;
    public readonly Rgb Rock, DeepRock, Bedrock, Seabed, RiverBed, Snow;
    /// <summary>The ground bared by wheels and feet: the world's rock weathered into clay, grit or sand (tracks and dirt roads).</summary>
    public readonly Rgb BareSoil;

    /// <summary>
    /// A blast's dead zone (ground zero in world metres, the dead radius and where it has faded back
    /// to the land's own colours): the ground goes ash-grey and lifeless toward the crater.
    /// </summary>
    public readonly List<(double X, double Z, float Dead, float Fade)> DeadZones = new();

    /// <summary>The blasts' craters (ground zero in world metres, radius, bowl depth and rim height, metres): the map draws them as the ground has them.</summary>
    public readonly List<(double X, double Z, float Radius, float Depth, float Rim)> Craters = new();

    /// <summary>
    /// Draws the blasts onto a map image of this world (RGB8, <paramref name="width"/> by <paramref name="height"/>
    /// over the whole map): the dead ground ash-grey as the land itself turns (<see cref="Natural"/>'s rule), and
    /// each crater's bowl and thrown rim lit from the map's north-west, as its relief is. Nothing on the sea.
    /// </summary>
    public void PaintBlasts(byte[] rgb, int width, int height)
    {
        if (DeadZones.Count == 0 && Craters.Count == 0) return;
        double mx = Map.WidthMeters / width, mz = Map.HeightMeters / height;
        var dead = new float[width * height];
        var shade = new float[width * height];
        void Each(double cx, double cz, double reach, Action<int, double, double> at)
        {
            int px0 = (int)Math.Floor((cx - reach) / mx), px1 = (int)Math.Ceiling((cx + reach) / mx);
            int py0 = Math.Max(0, (int)Math.Floor((cz - reach) / mz)), py1 = Math.Min(height - 1, (int)Math.Ceiling((cz + reach) / mz));
            for (int py = py0; py <= py1; py++)
                for (int px = px0; px <= px1; px++)
                {
                    int wx = ((px % width) + width) % width;
                    double x = (wx + 0.5) * mx, z = (py + 0.5) * mz;
                    if (Map.Has(Map.CellAt(x, z), CellFlags.Water)) continue;
                    at(py * width + wx, Mathx.WrapDelta(x - cx, Map.WidthMeters), z - cz);
                }
        }
        foreach (var (gx, gz, d0, d1) in DeadZones)
            Each(gx, gz, d1, (k, dx, dz) =>
            {
                float d = (float)Math.Sqrt(dx * dx + dz * dz);
                if (d < d1) dead[k] = Math.Max(dead[k], d <= d0 ? 1f : 1f - SmoothStep(0f, 1f, (d - d0) / (d1 - d0)));
            });
        // The bowl's slope (the stamp's own profile), lit as the map lights its relief.
        const float Relief = 2.6f;
        float lx = -1f, ly = -1f, lz = 1.1f, ll = MathF.Sqrt(lx * lx + ly * ly + lz * lz);
        (lx, ly, lz) = (lx / ll, ly / ll, lz / ll);
        foreach (var (gx, gz, r, depth, rim) in Craters)
            Each(gx, gz, r * 1.5, (k, dx, dz) =>
            {
                double d = Math.Sqrt(dx * dx + dz * dz);
                if (d >= r * 1.5 || d < 1e-3) return;
                // How the ground rises outward: up out of the bowl to the rim's crest, then down the rim's outer face.
                float slope = d <= r ? (depth + rim) * 2f * (float)d / (r * r) : -rim / (r * 0.5f);
                float gxs = slope * (float)(dx / d), gzs = slope * (float)(dz / d);
                float nx = -gxs * Relief, nz = -gzs * Relief, nl = MathF.Sqrt(nx * nx + nz * nz + 1f);
                float s = (nx * lx + nz * ly + lz) / nl;
                shade[k] = Math.Clamp(1f + (s - lz) * 1.5f, 0.5f, 1.35f) * (d <= r ? 0.82f : 1f);
            });
        for (int k = 0; k < dead.Length; k++)
        {
            if (dead[k] <= 0f && shade[k] == 0f) continue;
            int o = k * 3;
            float r = rgb[o] / 255f, g = rgb[o + 1] / 255f, b = rgb[o + 2] / 255f;
            if (dead[k] > 0f)
            {
                float l = r * 0.3f + g * 0.59f + b * 0.11f, t = dead[k] * 0.85f;
                (r, g, b) = (r + (l * 0.78f + 0.06f - r) * t, g + (l * 0.74f + 0.055f - g) * t, b + (l * 0.7f + 0.05f - b) * t);
            }
            if (shade[k] > 0f) (r, g, b) = (r * shade[k], g * shade[k], b * shade[k]);
            rgb[o] = (byte)Math.Clamp((int)MathF.Round(r * 255f), 0, 255);
            rgb[o + 1] = (byte)Math.Clamp((int)MathF.Round(g * 255f), 0, 255);
            rgb[o + 2] = (byte)Math.Clamp((int)MathF.Round(b * 255f), 0, 255);
        }
    }

    /// <summary>Flora set down on purpose (trees through ruins), by the scatter id each takes; null where there is none.</summary>
    public System.Collections.Concurrent.ConcurrentDictionary<long, PlantedFlora> Planted;

    /// <summary>The world's plants (shared/Flora, through its planet adapter). Null only for a palette made without a world's flora.</summary>
    public readonly PlanetFlora Flora;

    /// <summary>The dead zones and the craters by 128 m cell (indices into each list), once they are all in: a war's hundreds of shell holes cost a ground vertex the few near it, not all of them.</summary>
    int[][] _deadCells, _craterCells;
    int _cellsX, _cellsZ;
    const double BlastCell = 128;
    static readonly int[] NoBlasts = Array.Empty<int>();

    /// <summary>Indexes <see cref="DeadZones"/> and <see cref="Craters"/>; call once they are filled, before any worker reads the palette.</summary>
    public void IndexBlasts()
    {
        _cellsX = Math.Max(1, (int)Math.Ceiling(Map.WidthMeters / BlastCell));
        _cellsZ = Math.Max(1, (int)Math.Ceiling(Map.HeightMeters / BlastCell));
        int[][] Index(int count, Func<int, (double X, double Z, double Reach)> at)
        {
            var lists = new List<int>[_cellsX * _cellsZ];
            for (int i = 0; i < count; i++)
            {
                var (x, z, reach) = at(i);
                int x0 = (int)Math.Floor((x - reach) / BlastCell), x1 = (int)Math.Floor((x + reach) / BlastCell);
                int z0 = (int)Math.Floor((z - reach) / BlastCell), z1 = (int)Math.Floor((z + reach) / BlastCell);
                if (x1 - x0 >= _cellsX) (x0, x1) = (0, _cellsX - 1);
                if (z1 - z0 >= _cellsZ) (z0, z1) = (0, _cellsZ - 1);
                for (int cz = z0; cz <= z1; cz++)
                    for (int cx = x0; cx <= x1; cx++)
                    {
                        int k = ((cz % _cellsZ + _cellsZ) % _cellsZ) * _cellsX + (cx % _cellsX + _cellsX) % _cellsX;
                        (lists[k] ??= new List<int>()).Add(i);
                    }
            }
            return lists.Select(l => l?.ToArray() ?? NoBlasts).ToArray();
        }
        _deadCells = Index(DeadZones.Count, i => (DeadZones[i].X, DeadZones[i].Z, DeadZones[i].Fade));
        _craterCells = Index(Craters.Count, i => (Craters[i].X, Craters[i].Z, Craters[i].Radius * 1.6));
    }

    int[] Near(int[][] cells, double x, double z)
    {
        int cx = (int)Math.Floor(Mathx.WrapX(x, Map.WidthMeters) / BlastCell) % _cellsX, cz = (int)Math.Floor(Mathx.Wrap(z, Map.HeightMeters) / BlastCell) % _cellsZ;
        return cells[Math.Clamp(cz, 0, _cellsZ - 1) * _cellsX + Math.Clamp(cx, 0, _cellsX - 1)];
    }

    /// <summary>How dead the land is at a point: 1 inside a blast's dead zone, easing to 0 at its fade, 0 elsewhere.</summary>
    public float Deadness(double x, double z)
    {
        float dead = 0f;
        if (_deadCells != null)
        {
            foreach (int i in Near(_deadCells, x, z))
            {
                var (gx, gz, d0, d1) = DeadZones[i];
                double dx = Mathx.WrapDelta(x - gx, Map.WidthMeters), dz = Mathx.WrapDelta(z - gz, Map.HeightMeters);
                float d = (float)Math.Sqrt(dx * dx + dz * dz);
                if (d >= d1) continue;
                dead = Math.Max(dead, d <= d0 ? 1f : 1f - SmoothStep(0f, 1f, (d - d0) / (d1 - d0)));
            }
            return dead;
        }
        foreach (var (gx, gz, d0, d1) in DeadZones)
        {
            double dx = Mathx.WrapDelta(x - gx, Map.WidthMeters), dz = Mathx.WrapDelta(z - gz, Map.HeightMeters);
            float d = (float)Math.Sqrt(dx * dx + dz * dz);
            if (d >= d1) continue;
            dead = Math.Max(dead, d <= d0 ? 1f : 1f - SmoothStep(0f, 1f, (d - d0) / (d1 - d0)));
        }
        return dead;
    }

    /// <summary>
    /// Soil a blast has been over: burnt to ash in its dead zone (by <paramref name="ash"/> at most), and in a
    /// crater a dark fused floor, a little glassy, with the paler thrown spoil of its rim round it.
    /// </summary>
    Rgb Scorched(Rgb soil, double x, double z, float ash, out float dead)
    {
        dead = Deadness(x, z);
        if (dead <= 0f) return soil;
        float l = soil.R * 0.3f + soil.G * 0.59f + soil.B * 0.11f;
        Rgb c = Rgb.Lerp(soil, new Rgb(l * 0.78f + 0.06f, l * 0.74f + 0.055f, l * 0.7f + 0.05f), dead * ash);
        int[] near = _craterCells != null ? Near(_craterCells, x, z) : null;
        int count = near?.Length ?? Craters.Count;
        for (int k = 0; k < count; k++)
        {
            var (gx, gz, r, _, _) = Craters[near != null ? near[k] : k];
            double dx = Mathx.WrapDelta(x - gx, Map.WidthMeters), dz = Mathx.WrapDelta(z - gz, Map.HeightMeters);
            if (Math.Abs(dx) > r * 1.6 || Math.Abs(dz) > r * 1.6) continue;
            float d = (float)Math.Sqrt(dx * dx + dz * dz) / r;
            if (d < 1f) c = Rgb.Lerp(c, new Rgb(0.16f, 0.15f, 0.14f), 0.7f * (1f - d * d * d));
            else if (d < 1.5f) c = Rgb.Lerp(c, new Rgb(l * 0.6f + 0.22f, l * 0.58f + 0.2f, l * 0.55f + 0.18f), 0.45f * (1f - Math.Abs(d - 1.15f) / 0.35f));
        }
        return c;
    }

    /// <summary>How strongly rock faces show horizontal strata (0-1): canyons and mesas show them most.</summary>
    public readonly float Strata;

    readonly Rgb[] _cells;
    readonly float[] _glow;
    readonly Fractal _jitter, _tone;

    public GroundPalette(WorldMap map, PlanetFlora flora = null)
    {
        Map = map;
        Flora = flora;
        _jitter = new Fractal(Hash.Of(map.Spec.Seed, 0x6A6974), ColorMap.BorderWave, 4);
        _tone = new Fractal(Hash.Of(map.Spec.Seed, 0x746F6E), 700, 3);
        _cells = new Rgb[map.Count];
        _glow = new float[map.Count];
        GalHack.Plants.Tone? verdure = flora?.Book.Verdure;
        for (int k = 0; k < map.Count; k++)
        {
            var biome = (Biome)map.Biome[k];
            _cells[k] = Soil(biome);
            // Green country takes the green of whatever grows on it: russet under russet trees.
            if (verdure is GalHack.Plants.Tone v) _cells[k] = Rgb.Lerp(_cells[k], new Rgb(v.R, v.G, v.B), Verdant(biome));
            _glow[k] = biome switch { Biome.LavaField => 1f, Biome.HotSpring => 0.12f, Biome.GlowMoss => 0.1f, _ => 0f };
        }
        _cells = ColorMap.Soften(map, _cells);

        (Rock, Strata) = (map.Def.Archetype, map.Def.Variant) switch
        {
            (Archetype.Temperate, _) => (Rgb.Hex("726b61"), 0.25f),
            (Archetype.Arid, 0) => (Rgb.Hex("a2725a"), 0.55f),
            (Archetype.Arid, _) => (Rgb.Hex("9c5436"), 0.9f),
            (Archetype.Ice, _) => (Rgb.Hex("5d646e"), 0.3f),
            (Archetype.Lava, _) => (Rgb.Hex("2f2a2b"), 0.35f),
            (Archetype.Alien, 0) => (Rgb.Hex("6f6185"), 0.5f),
            _ => (Rgb.Hex("6b5a52"), 0.3f),
        };
        DeepRock = Rock * 0.72f;
        Bedrock = Rgb.Hex("26262b");
        Seabed = Rgb.Lerp(Rock, Rgb.Hex("b0a48a"), 0.45f) * 0.85f;
        RiverBed = Rgb.Lerp(Rock, Rgb.Hex("4a4034"), 0.6f);
        BareSoil = Rgb.Lerp(Rock, Rgb.Hex("7a5e44"), 0.45f);
        Snow = Rgb.Hex("eef2f6");
    }

    /// <summary>How much of a biome's topsoil is what grows on it.</summary>
    static float Verdant(Biome b) => b switch
    {
        Biome.Grassland or Biome.Meadow => 0.55f,
        Biome.Forest or Biome.Jungle or Biome.Rainforest or Biome.Riverbank or Biome.Oasis => 0.5f,
        Biome.PineForest or Biome.CloudForest => 0.45f,
        Biome.Swamp or Biome.SporeMarsh or Biome.GlowMoss => 0.35f,
        Biome.Tundra or Biome.Scrub or Biome.HotSpring => 0.22f,
        _ => 0f,
    };

    /// <summary>A grounded colour for each biome's topsoil, sand, ash or snow.</summary>
    public static Rgb Soil(Biome b) => b switch
    {
        Biome.Beach => Rgb.Hex("c8b386"),
        Biome.Grassland => Rgb.Hex("5b7838"),
        Biome.Meadow => Rgb.Hex("76863c"),
        Biome.Forest => Rgb.Hex("40592c"),
        Biome.PineForest => Rgb.Hex("3e4e34"),
        Biome.Tundra => Rgb.Hex("7d7a5c"),
        Biome.Snow => Rgb.Hex("e4e9ee"),
        Biome.Cliff => Rgb.Hex("6e675e"),
        Biome.Riverbank => Rgb.Hex("4d6c33"),
        Biome.Jungle => Rgb.Hex("3b6829"),
        Biome.Rainforest => Rgb.Hex("345d2b"),
        Biome.Swamp => Rgb.Hex("4f5a31"),
        Biome.CloudForest => Rgb.Hex("486848"),
        Biome.Dunes => Rgb.Hex("c99860"),
        Biome.Erg => Rgb.Hex("d6ad76"),
        Biome.SaltFlat => Rgb.Hex("dcd4c2"),
        Biome.Scrub => Rgb.Hex("998a56"),
        Biome.RedRock => Rgb.Hex("9c5638"),
        Biome.Badlands => Rgb.Hex("ad6e47"),
        Biome.MesaTop => Rgb.Hex("b78a5a"),
        Biome.Oasis => Rgb.Hex("55783a"),
        Biome.Glacier => Rgb.Hex("dde7ef"),
        Biome.Snowfield => Rgb.Hex("edf1f5"),
        Biome.IceTundra => Rgb.Hex("888e96"),
        Biome.BlueIce => Rgb.Hex("9dc1da"),
        Biome.HotSpring => Rgb.Hex("6aa59c"),
        Biome.Sinter => Rgb.Hex("bf8f5e"),
        Biome.Basalt => Rgb.Hex("3a3436"),
        Biome.Ash => Rgb.Hex("5b5553"),
        Biome.LavaField => Rgb.Hex("2e2220"),
        Biome.Obsidian => Rgb.Hex("201b24"),
        Biome.Sulfur => Rgb.Hex("c2ae4a"),
        Biome.CrystalPlain => Rgb.Hex("6c5c86"),
        Biome.CrystalSpires => Rgb.Hex("8a6ca6"),
        Biome.Chalk => Rgb.Hex("d2c6d0"),
        Biome.GlowMoss => Rgb.Hex("3a8676"),
        Biome.FungalForest => Rgb.Hex("874a3c"),
        Biome.SporeMarsh => Rgb.Hex("5e6a35"),
        Biome.Mycelium => Rgb.Hex("cbbfa4"),
        _ => Rgb.Hex("6a6a5a"),
    };

    /// <summary>Blended soil colour at a point (canonical metres) and how much it glows (lava fields, glow moss).</summary>
    public Rgb SoilAt(double x, double z, out float glow)
    {
        Jitter(x, z, out double gx, out double gz);
        Rgb c = default;
        float g = 0f, total = 0f;
        Around(gx, gz, ref c, ref g, ref total);
        if (total <= 0f) Around(x / Map.CellSize - 0.5, z / Map.CellSize - 0.5, ref c, ref g, ref total); // by the shore a border can wander right out over water
        if (total <= 0f)
        {
            glow = 0f;
            return Seabed;
        }
        glow = g / total;
        float tone = _tone.Fbm(Map.Model.Torus.At(x, z), 32);
        return c * ((1f / total) * (1f + 0.08f * tone));
    }

    /// <summary>The ground's colour seen from far off (canonical metres): its soil, bare rock where <paramref name="up"/> (the ground normal's y) says it's steep, and the seabed under water.</summary>
    public Rgb FarGround(double x, double z, float height, float up, out float bare)
    {
        Rgb c = SoilAt(x, z, out _);
        // A blast's dead ground and its crater, as the near ground has them (Natural), so the horizon and the globe show them too.
        if (DeadZones.Count > 0) c = Scorched(c, x, z, 0.85f, out _);
        bare = Math.Clamp((0.85f - up) * 4f, 0f, 1f);
        c = Rgb.Lerp(c, Rock, bare);
        if (height < 0f) c = Rgb.Lerp(c, Seabed, Math.Clamp(-height / 5f, 0f, 1f));
        return c;
    }

    /// <summary>The same read from the world map alone, its slope over <paramref name="across"/> metres: what the globe's sheet is painted with, so the far land is the colour of the land.</summary>
    public Rgb FarGround(double x, double z, float across)
    {
        double w = Map.WidthMeters, h = Map.HeightMeters;
        float At(double px, double pz) => Map.Sample(Map.Elevation, Mathx.WrapX(px, w), Mathx.Wrap(pz, h));
        float height = At(x, z);
        var normal = new Vector3(At(x - across, z) - At(x + across, z), 2f * across, At(x, z - across) - At(x, z + across)).Normalized();
        return FarGround(Mathx.WrapX(x, w), Mathx.Wrap(z, h), height, normal.Y, out _);
    }

    /// <summary>The biome at a point, with the same wandering borders as the colours.</summary>
    public Biome BiomeAt(double x, double z)
    {
        Jitter(x, z, out double gx, out double gz);
        return (Biome)Map.Biome[Map.Index((int)Math.Round(gx), (int)Math.Round(gz))];
    }

    /// <summary>
    /// The colour a ground vertex shows. <paramref name="rock"/> is how bare the ground is (0
    /// soil, 1 rock face): the shader adds strata and rock detail by it. <paramref name="flags"/>
    /// carries glow (x) and bedrock (y).
    /// </summary>
    public Color Vertex(CellMaterial material, double x, double z, float height, out Vector2 flags, DepositKind ore = DepositKind.Iron)
    {
        flags = Vector2.Zero;
        Rgb c;
        float rock = 0f;
        switch (material)
        {
            case CellMaterial.Ore:
            {
                // Ore shows in the rock as speckle and streaks of its colour, not a flat stain: a
                // texture over the rock, read by its pattern first, and only a faint glow.
                Rgb oreColour = DepositLook.Color(ore);
                float speck = SmoothStep(0.55f, 0.85f, ValueNoise(x * 1.7, z * 1.7 + height * 0.9, 41));
                float streak = MathF.Abs(ValueNoise(x * 0.45 + height * 0.3, z * 0.45, 43) - 0.5f) < 0.1f ? 1f : 0f;
                c = Rgb.Lerp(Rock, oreColour, 0.14f + 0.26f * speck + 0.18f * streak);
                flags.X = DepositLook.Glow(ore) * 0.12f;
                rock = 0.8f;
                // Where a body breaks the surface it shows through the soil in broken patches, never a slab of its own.
                if (Map.Sample(Map.Elevation, x, z) - height < 2.5f)
                {
                    float showing = SmoothStep(0.45f, 0.68f, ValueNoise(x * 0.33, z * 0.33, 53));
                    c = Rgb.Lerp(SoilAt(x, z, out _), c, showing * 0.8f);
                    rock *= showing;
                }
                break;
            }
            case CellMaterial.Bedrock:
                c = Bedrock;
                flags.Y = 1f;
                rock = 1f;
                break;
            case CellMaterial.DeepRock:
                c = DeepRock;
                rock = 1f;
                break;
            case CellMaterial.Rock:
                c = Rock;
                rock = 1f;
                break;
            default:
                c = SoilAt(x, z, out float glow);
                flags.X = glow;
                if (height < 0.6f) c = Rgb.Lerp(c, Seabed, Math.Clamp(0.6f - height, 0f, 1f)); // wet sand and silt at the waterline
                break;
        }
        return new Color(c.R, c.G, c.B, rock);
    }

    /// <summary>What a chip of a cell looks like: its own colour here, soil dulled by the stones in it, a little varied by the caller.</summary>
    public Color Chip(CellMaterial material, double x, double z, float height, DepositKind ore)
    {
        Color c = Vertex(material, x, z, height, out _, ore);
        Rgb rgb = new(c.R, c.G, c.B);
        if (material is CellMaterial.Ore or CellMaterial.Rock or CellMaterial.DeepRock or CellMaterial.Bedrock) return new Color(rgb.R, rgb.G, rgb.B);
        rgb = Rgb.Lerp(rgb, Rock, 0.35f);
        return new Color(rgb.R, rgb.G, rgb.B);
    }

    /// <summary>
    /// The colour of undug ground: soil over rock by <paramref name="cover"/> (0 bare rock, 1 all
    /// soil), continuous so the near voxels and the far rings blend the same way.
    /// </summary>
    public Color Natural(double x, double z, float height, float cover, out Vector2 flags)
    {
        Rgb soil = SoilAt(x, z, out float glow);
        // In a blast's dead zone the soil is burnt to ash, greyer and darker toward ground zero.
        if (DeadZones.Count > 0)
        {
            soil = Scorched(soil, x, z, 0.75f + 0.2f * ValueNoise(x * 0.05, z * 0.05, 47), out float dead);
            glow *= 1f - dead;
        }
        Rgb c = Rgb.Lerp(Rock, soil, cover);
        if (height < 0.6f) c = Rgb.Lerp(c, Seabed, Math.Clamp(0.6f - height, 0f, 1f));
        flags = new Vector2(glow * cover, 0f);
        return new Color(c.R, c.G, c.B, 1f - cover);
    }

    // ---- made ground ----------------------------------------------------------------------

    /// <summary>
    /// A stamped place's made ground over the land's colour. Concrete aprons in slabs, tarmac with
    /// its cracks, gravel or packed-dirt yards; or the land's own ground, broken up so a pad is never
    /// a uniform carpet. It shows on the pad and its apron and gives way early in the blend, along
    /// a ragged edge, never a hard rectangle. A century of weather (<paramref name="wear"/>) cracks it
    /// and drifts the land's own sand, soil and weeds over it in patches.
    /// </summary>
    public Color Made(Color land, StampSurface surface, float weight, float wear, double x, double z, float spacing = 1f)
    {
        // A lawn is levelled, not laid: the land's own ground shows.
        if (weight <= 0f || surface == StampSurface.Lawn) return land;
        // Detail finer than the mesh's own vertex spacing can't be drawn there, only aliased into a blotchy
        // mosaic: far off, each pattern gives way to its average tone, and joints and cracks to nothing.
        float Noise(double fx, double fz, double freq, int seed)
        {
            float fine = Math.Clamp((float)(freq * spacing - 0.2) / 0.25f, 0f, 1f);
            return fine >= 1f ? 0.5f : ValueNoise(fx * freq, fz * freq, seed) * (1f - fine) + 0.5f * fine;
        }
        bool sharp = spacing <= 1.5f;
        float edge = Noise(x, z, 0.23, 11) - 0.5f;
        float w = SmoothStep(0.5f + edge * 0.4f, 0.92f, weight);
        if (w <= 0f) return land;
        var landRgb = new Rgb(land.R, land.G, land.B);
        Rgb made;
        float hard = 1f;
        switch (surface)
        {
            case StampSurface.Natural:
                // The land's own ground, trodden and patchy: soil broken by stone and bare earth.
                made = Rgb.Lerp(landRgb, Rgb.Lerp(Rock, Seabed, 0.3f), 0.2f + 0.35f * Noise(x, z, 0.11, 23));
                hard = 0.4f;
                break;
            case StampSurface.Tarmac:
                made = new Rgb(0.21f, 0.21f, 0.22f) * (0.88f + 0.24f * Noise(x, z, 0.6, 5));
                break;
            case StampSurface.Gravel:
                made = new Rgb(0.5f, 0.47f, 0.42f) * (0.85f + 0.3f * Noise(x, z, 1.3, 7));
                break;
            case StampSurface.Dirt:
                made = Rgb.Lerp(new Rgb(0.43f, 0.34f, 0.25f), landRgb, 0.25f) * (0.85f + 0.3f * Noise(x, z, 0.4, 9));
                hard = 0.7f;
                break;
            default:
            {
                made = new Rgb(0.56f, 0.55f, 0.52f) * (0.9f + 0.18f * Noise(x, z, 0.5, 3));
                // Slab joints every six metres, where the mesh is fine enough to draw a line.
                double jx = x / 6 - Math.Floor(x / 6), jz = z / 6 - Math.Floor(z / 6);
                if (sharp && (jx < 0.04 || jz < 0.04)) made *= 0.8f;
                break;
            }
        }
        if (sharp && surface is StampSurface.Concrete or StampSurface.Tarmac)
        {
            // Cracks: thin dark lines along a ridge of noise, more of them the older it is.
            float ridge = MathF.Abs(ValueNoise(x * 0.19, z * 0.19, 17) - 0.5f);
            if (ridge < 0.015f + 0.035f * wear) made *= 0.55f;
        }
        // The weather takes it back in drifts: the land's own ground over it in patches.
        float drift = Noise(x, z, 0.07, 29);
        float over = SmoothStep(0.8f - 0.5f * wear, 1.05f, drift) * (0.3f + 0.7f * wear);
        made = Rgb.Lerp(made, landRgb, over);
        Rgb c = Rgb.Lerp(landRgb, made, w);
        return new Color(c.R, c.G, c.B, land.A + (1f - land.A) * w * hard * (1f - over));
    }

    static float SmoothStep(float a, float b, float v)
    {
        float t = Math.Clamp((v - a) / (b - a), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>Smooth value noise in [0, 1] on a unit lattice: cheap, seeded, the same everywhere it is asked.</summary>
    static float ValueNoise(double x, double z, int salt)
    {
        double fx = Math.Floor(x), fz = Math.Floor(z);
        float tx = (float)(x - fx), tz = (float)(z - fz);
        tx = tx * tx * (3 - 2 * tx);
        tz = tz * tz * (3 - 2 * tz);
        long ix = (long)fx, iz = (long)fz;
        float a = Lattice(ix, iz, salt), b = Lattice(ix + 1, iz, salt), c = Lattice(ix, iz + 1, salt), d = Lattice(ix + 1, iz + 1, salt);
        return (a + (b - a) * tx) * (1 - tz) + (c + (d - c) * tx) * tz;
    }

    static float Lattice(long x, long z, int salt)
    {
        ulong h = (ulong)(x * 73856093L ^ z * 19349663L ^ salt * 83492791L);
        h ^= h >> 33; h *= 0xff51afd7ed558ccdUL; h ^= h >> 33; h *= 0xc4ceb9fe1a85ec53UL; h ^= h >> 33;
        return (h & 0xFFFFFF) / (float)0xFFFFFF;
    }

    void Around(double gx, double gz, ref Rgb c, ref float g, ref float total)
    {
        int i0 = (int)Math.Floor(gx), j0 = (int)Math.Floor(gz);
        float tx = (float)(gx - i0), tz = (float)(gz - j0);
        Add(i0, j0, (1 - tx) * (1 - tz), ref c, ref g, ref total);
        Add(i0 + 1, j0, tx * (1 - tz), ref c, ref g, ref total);
        Add(i0, j0 + 1, (1 - tx) * tz, ref c, ref g, ref total);
        Add(i0 + 1, j0 + 1, tx * tz, ref c, ref g, ref total);
    }

    void Jitter(double x, double z, out double gx, out double gz)
    {
        P4 p = Map.Model.Torus.At(x, z);
        gx = x / Map.CellSize - 0.5 + _jitter.Fbm(p, 4) * ColorMap.BorderWander;
        gz = z / Map.CellSize - 0.5 + _jitter.Fbm(p.Swap, 4) * ColorMap.BorderWander;
    }

    void Add(int i, int j, float w, ref Rgb c, ref float g, ref float total)
    {
        int k = Map.Index(i, j);
        if (Map.Has(k, CellFlags.Water)) return;
        c += _cells[k] * w;
        g += _glow[k] * w;
        total += w;
    }
}
