using System;
using System.Collections.Generic;
using System.Numerics;

namespace GalHack.Plants;

/// <summary>An sRGB colour, 0..1. The module's own, so its core needs neither the engine nor the planet generator.</summary>
public readonly record struct Tone(float R, float G, float B)
{
    public static Tone Hsv(float degrees, float s, float v)
    {
        float h = ((degrees % 360f) + 360f) % 360f / 60f, c = v * s, x = c * (1f - MathF.Abs(h % 2f - 1f)), m = v - c;
        (float r, float g, float b) = (int)h switch { 0 => (c, x, 0f), 1 => (x, c, 0f), 2 => (0f, c, x), 3 => (0f, x, c), 4 => (x, 0f, c), _ => (c, 0f, x) };
        return new Tone(r + m, g + m, b + m);
    }

    public static Tone Lerp(Tone a, Tone b, float t) => new(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);
    public static Tone operator *(Tone c, float k) => new(c.R * k, c.G * k, c.B * k);
    public float Luma => 0.299f * R + 0.587f * G + 0.114f * B;
    public float Distance(Tone o) => MathF.Sqrt((R - o.R) * (R - o.R) + (G - o.G) * (G - o.G) + (B - o.B) * (B - o.B));

    /// <summary>A mesh paint: its own colour, which the instance's tint never touches.</summary>
    internal Vector4 Own() => new(R, G, B, 0f);
}

/// <summary>A small deterministic random source (SplitMix64): world seeds never touch System.Random.</summary>
public sealed class Dice
{
    ulong _state;
    public Dice(ulong seed) { _state = seed; }

    public static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public static ulong Of(ulong seed, long a, long b = 0) => Mix(seed ^ Mix((ulong)a + 0x9E3779B97F4A7C15UL ^ Mix((ulong)b + 0x632BE59BD9B4E019UL)));

    public ulong Next()
    {
        _state += 0x9E3779B97F4A7C15UL;
        return Mix(_state);
    }

    public float Unit() => (Next() >> 40) * (1f / (1 << 24));
    public float Range(float min, float max) => min + (max - min) * Unit();
    public int Int(int min, int max) => min + (int)((Next() >> 33) % (ulong)(max - min + 1));
    public bool Chance(float p) => Unit() < p;
    public T Pick<T>(IReadOnlyList<T> list) => list[(int)((Next() >> 33) % (ulong)list.Count)];
}

/// <summary>What a plant is for. Every family is a starting point for many forms, on any world.</summary>
public enum PlantFamily
{
    // Stands, on the scatter grid.
    Conifer, Broadleaf, Jungle, Palm, Shrub, Cactus, Fungus, GiantFungus, Crystal, Ice,
    // Ground cover, drawn near the player.
    Grass, Forb, Twig, Reed, Moss, Pins,
    // Sown, for whatever farms a world has.
    Crop,
}

/// <summary>The body plans a genome can take.</summary>
public enum PlantForm
{
    Spire, Pagoda, Spindle, Stacked, Domed, Umbrella, Weeping, CloudPads, Bottle, Pole, Palm, FanPalm, SpikeTree, Antler, Lantern, Snag,
    Capped, Parasol, Trumpet, Organ,
    Candelabra, Barrel, Whips, Paddles,
    Bush, Fern, Thorn, Rosette,
    Shards,
    Tussock, Flowers, Twigs, Reeds, Lichen, PinStalks,
    GrainCrop, LeafCrop, VineCrop,
}

/// <summary>
/// What a world offers its plants. Warmth and Wetness run 0..1; Air 0 is vacuum, 1 Earth's, 2 thick;
/// Light is the sunlight's colour; Alien 0..1 pulls forms from the familiar to the strange; Life 0..1
/// is how much grows at all, and 0 means nothing does.
/// </summary>
public sealed record FloraClimate(ulong Seed, float Warmth, float Wetness, float Air, Tone Light, float Alien, float Life);

/// <summary>A kind of ground on a world (a biome, to the planet): its own climate, how much of the world it covers, and the families it wants.</summary>
public sealed record Habitat(int Id, float Warmth, float Wetness, float Share, IReadOnlyList<PlantFamily> Families);

/// <summary>A built mesh as plain arrays: three vertices a triangle, flat shaded. Colour alpha 1 takes the instance's tint.</summary>
public sealed class PlantMesh
{
    public Vector3[] Positions, Normals;
    public Vector4[] Colors;
    public int Triangles => Positions.Length / 3;

    /// <summary>Bytes of vertex data once on the graphics card (position, normal, colour).</summary>
    public int Bytes => Positions.Length * 20;
}

/// <summary>The numbers a plant's form is built from: what makes two plants of one form different.</summary>
public sealed class Genome
{
    public float Height, TrunkRadius, Stem, Lean, LeanAngle, Bend, Flare, Crown, Flat, Droop, Jag, Taper;
    public float Spread, Arch, FrondLength, FrondWidth, Ribs, Bloom;
    public int Layers, Forks, Lobes, Fronds, Sides, Clump;

    /// <summary>Its numbers side by side, each scaled to about 0..1, for measuring how far apart two plants are.</summary>
    public float[] Vector() => new[]
    {
        Height / 30f, TrunkRadius / 1.5f, Stem, Lean * 3f, Bend * 3f, Flare / 2f, Crown, Flat, Droop, Jag, Taper,
        Spread, Arch, FrondLength / 8f, FrondWidth / 2f, Ribs * 2f, Bloom, Layers / 8f, Forks / 6f, Lobes / 7f, Fronds / 14f,
    };
}

/// <summary>
/// One species of a world: its form and genome, its colours, a few bounded meshes, and everything
/// gameplay needs to treat it as data (trunk, foot, room it takes up).
/// </summary>
public sealed class Plant
{
    public int Index;
    public PlantFamily Family;
    public PlantForm Form;
    public Genome Genome;
    public float Warmth, Wetness;

    public Tone Leaf, Bark, Bloom;

    /// <summary>Whether it carries flowers.</summary>
    public bool Flowers;

    /// <summary>The near meshes (a few per species), and the one low-detail mesh for the distance. Cover has one near mesh and no far one.</summary>
    public PlantMesh[] Near;
    public PlantMesh Far;

    /// <summary>A trunk, for a collider. Off for what you walk through.</summary>
    public bool Solid;
    public float TrunkRadius, TrunkHeight;
    public Vector3 TrunkAxis = Vector3.UnitY;

    /// <summary>How far the base reaches from its root: what must stay in the ground on a slope.</summary>
    public float FootRadius;

    /// <summary>How tall it stands, and how far its crown reaches, m at scale 1.</summary>
    public float Height, CrownRadius;

    /// <summary>Bands of height (m above its foot) and how far out each reaches: the room it needs.</summary>
    public (float From, float To, float Radius)[] Reach;

    public bool Large => Family is PlantFamily.Conifer or PlantFamily.Broadleaf or PlantFamily.Jungle or PlantFamily.Palm or PlantFamily.Cactus or PlantFamily.GiantFungus or PlantFamily.Ice;
    public bool Tree => Family is PlantFamily.Conifer or PlantFamily.Broadleaf or PlantFamily.Jungle or PlantFamily.Palm or PlantFamily.Cactus or PlantFamily.GiantFungus;
    public bool Cover => Family is >= PlantFamily.Grass and <= PlantFamily.Pins;

    /// <summary>The mineral growths take their colour from the ground they stand on, not from a palette of leaves.</summary>
    public bool Mineral => Family is PlantFamily.Crystal or PlantFamily.Ice;
}
