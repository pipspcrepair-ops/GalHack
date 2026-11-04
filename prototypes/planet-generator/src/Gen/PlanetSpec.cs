using System;
using System.Collections.Generic;

namespace Planets.Gen;

public enum Archetype { Temperate, Arid, Ice, Lava, Alien }

/// <summary>
/// Planet modifiers. The first five change the terrain; the last three only change the look
/// (sky tint, clouds, night glow), so they never affect generation or the map hash.
/// </summary>
[Flags]
public enum Modifier
{
    None = 0,
    Archipelago = 1 << 0,
    Highlands = 1 << 1,
    Cratered = 1 << 2,
    Mesas = 1 << 3,
    Canyons = 1 << 4,
    RingedSky = 1 << 5,
    PerpetualStorm = 1 << 6,
    GlowingNights = 1 << 7,
}

/// <summary>Everything needed to regenerate a planet exactly. Sizes are in kilometres.</summary>
public sealed record PlanetSpec(ulong Seed, Archetype Archetype, int Variant, Modifier Modifiers, int WidthKm = 16)
{
    public const int VariantCount = 2;
    public static readonly int[] Widths = { 8, 16, 32, 64 };
    public const Modifier TerrainModifiers =
        Modifier.Archipelago | Modifier.Highlands | Modifier.Cratered | Modifier.Mesas | Modifier.Canyons;

    /// <summary>Maps are twice as wide as they are tall, so they wrap onto a sphere without stretching.</summary>
    public int HeightKm => WidthKm / 2;
    public double WidthMeters => WidthKm * 1000.0;
    public double HeightMeters => HeightKm * 1000.0;

    public bool Has(Modifier modifier) => (Modifiers & modifier) != 0;

    /// <summary>A planet of the given archetype with 1-2 modifiers rolled from the seed.</summary>
    public static PlanetSpec Roll(ulong seed, Archetype archetype, int variant, int widthKm = 16) =>
        new(seed, archetype, variant, RollModifiers(seed, 0), widthKm);

    /// <summary>1-2 distinct modifiers. Different rerolls of the same seed give different sets.</summary>
    public static Modifier RollModifiers(ulong seed, int reroll)
    {
        var rng = new Rng(Hash.Of(seed, 0x6D6F64, reroll));
        var all = (Modifier[])Enum.GetValues(typeof(Modifier));
        var pool = new List<Modifier>();
        foreach (Modifier m in all) if (m != Modifier.None) pool.Add(m);

        int count = rng.NextFloat() < 0.55f ? 1 : 2;
        Modifier result = Modifier.None;
        for (int i = 0; i < count; i++)
        {
            int pick = rng.Next(pool.Count);
            result |= pool[pick];
            pool.RemoveAt(pick);
        }
        return result;
    }

    public override string ToString()
    {
        var def = Archetypes.Get(Archetype, Variant);
        string mods = Modifiers == Modifier.None ? "none" : Modifiers.ToString();
        return $"{Archetype} / {def.VariantName}   seed {Seed}   {WidthKm}x{HeightKm} km   modifiers: {mods}";
    }
}
