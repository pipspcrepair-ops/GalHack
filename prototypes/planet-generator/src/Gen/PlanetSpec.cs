using System;
using System.Collections.Generic;
using System.Linq;

namespace Planets.Gen;

/// <summary>A world's kind. Barren: a body with no air at all (regolith, icy or metal, <see cref="Archetypes.BarrenVariant"/>), every airless moon and small world.</summary>
public enum Archetype { Temperate, Arid, Ice, Lava, Alien, Barren }

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

/// <summary>What a world is, for its size roll (<see cref="PlanetSpec.RollWidthKm"/>).</summary>
public enum WorldKind { Planet, Moon, Start }

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

    /// <summary>The width every start world has, km: gravity is measured against it.</summary>
    public const int StartWidthKm = 16;

    /// <summary>
    /// Surface gravity in g by size class, beside <see cref="Widths"/> (owner, 22 Nov: from size alone; the start
    /// size 1 g, the largest 5 g, moons as low as 0.2 g). The one table every fall and thrust reads.
    /// </summary>
    public static readonly float[] Gravities = { 0.2f, 1.0f, 2.2f, 5.0f };

    /// <summary>The chance a world of each size class has no air (owner, 22 Nov: the smallest about 95%, too light to hold it; the largest never).</summary>
    public static readonly float[] VacuumChances = { 0.95f, 0.6f, 0.25f, 0f };

    /// <summary>
    /// How thick a world's air is by size class, against a start world's (owner, 22 Nov: air density grows with size,
    /// thin on the smallest that keep any, always dense on the largest).
    /// </summary>
    public static readonly float[] AirThicknesses = { 0.5f, 1.0f, 1.4f, 2.0f };

    /// <summary>
    /// This world's air thickness, from its size alone: 0 in vacuum, then thin up to dense (<see cref="AirThicknesses"/>).
    /// The one value its sky and its globe's air shell read, beside its air kind (<c>SkySystem.Air</c>, <c>SkySystem.Thickness</c>).
    /// </summary>
    public float AirThickness => Airless ? 0f : AirThicknesses[SizeClass];

    /// <summary>A width's size class: its place in <see cref="Widths"/> and the tables beside it.</summary>
    public static int ClassOf(int widthKm) => Math.Max(0, Array.FindLastIndex(Widths, w => w <= widthKm));

    public int SizeClass => ClassOf(WidthKm);

    /// <summary>The gravity from which a world is dangerous (Empyrion's high-gravity worlds pull 25 or more against its usual 5 to 12): 32 km worlds and up.</summary>
    public const float DangerousGravity = 1.8f;

    /// <summary>Surface gravity against a start world's (1 g), from <see cref="Gravities"/>.</summary>
    public float GravityScale => Gravities[SizeClass];

    /// <summary>Whether a world of this width is a dangerous high-gravity one: they hold fold fuel and fold drive materials.</summary>
    public static bool Heavy(int widthKm) => Gravities[ClassOf(widthKm)] >= DangerousGravity;

    public bool HighGravity => Heavy(WidthKm);

    /// <summary>
    /// Whether natural impact craters stay on a kind of world: one with no air or water to wear them away.
    /// A barren body always keeps them; an ice or lava world only when it lost its air (a tidally heated moon,
    /// <see cref="Modifier.Cratered"/>). A desert is never airless (owner, 22 Nov: an airless moon is a grey
    /// cratered moonscape, never dunes), nor is a living world: air, weather and seas erode and fill old impacts.
    /// A war's blast craters are something else: they go on the war's targets (the POI generator's strikes).
    /// </summary>
    public static bool Craters(Archetype archetype) => archetype is Archetype.Barren or Archetype.Ice or Archetype.Lava;

    /// <summary>
    /// No air to speak of: a barren body, or an ice or lava world that kept its craters for want of air.
    /// The one rule the flora, the creatures and the slice all read, so they can't disagree.
    /// </summary>
    public bool Airless => Has(Modifier.Cratered) && Craters(Archetype);

    /// <summary>A planet of the given archetype with 1-2 modifiers rolled from the seed (craters only where the archetype keeps them).</summary>
    public static PlanetSpec Roll(ulong seed, Archetype archetype, int variant, int widthKm = 16) =>
        new(seed, archetype, variant, RollModifiers(seed, 0, archetype), widthKm);

    /// <summary>
    /// A world's width in km, rolled from its seed on Empyrion's planet classes 2-5 (5/40/50/5%):
    /// 8x4 km 5%, 16x8 km 40%, 32x16 km 50%, 64x32 km 5%. A moon rolls the same odds among the sizes
    /// smaller than what it orbits (owner, 22 Nov), its parent's width given (a giant's moons, any size):
    /// a 32 km world's are 8 km 1 in 9 and 16 km 8 in 9, and an 8 km world has none. A start world is
    /// always 16x8 km. Separate from <see cref="Roll"/>'s width, so a caller that names a size keeps it.
    /// </summary>
    public static int RollWidthKm(ulong seed, WorldKind kind = WorldKind.Planet, int parentWidthKm = int.MaxValue)
    {
        if (kind == WorldKind.Start) return StartWidthKm;
        int last = kind == WorldKind.Moon ? Array.FindLastIndex(Widths, w => w < parentWidthKm) : Widths.Length - 1;
        if (last < 0) throw new ArgumentException($"no world is small enough to be a moon of a {parentWidthKm} km one");
        float roll = new Rng(Hash.Of(seed, 0x53495A45)).NextFloat() * SizeOdds.Take(last + 1).Sum();
        for (int i = 0; i < last; i++)
            if ((roll -= SizeOdds[i]) < 0f) return Widths[i];
        return Widths[last];
    }

    static readonly float[] SizeOdds = { 0.05f, 0.4f, 0.5f, 0.05f };

    /// <summary>Whether a world of this width can have moons: something smaller than it must exist.</summary>
    public static bool HoldsMoons(int widthKm) => widthKm > Widths[0];

    /// <summary>A planet with its modifiers and its size both rolled from the seed.</summary>
    public static PlanetSpec RollSized(ulong seed, Archetype archetype, int variant, WorldKind kind = WorldKind.Planet) =>
        Roll(seed, archetype, variant, RollWidthKm(seed, kind));

    /// <summary>
    /// 1-2 distinct modifiers. Different rerolls of the same seed give different sets. Given the archetype,
    /// <see cref="Modifier.Cratered"/> only stays where natural craters do (<see cref="Craters"/>): on a
    /// living world the next of the rest takes its place, so every other world rolls as it always did.
    /// </summary>
    public static Modifier RollModifiers(ulong seed, int reroll, Archetype? archetype = null)
    {
        // A barren body is its craters: no mesas, canyons or seas (there was never weather or water to make them), no storm in its black sky.
        if (archetype == Archetype.Barren) return Modifier.Cratered;
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
        if ((result & Modifier.Cratered) != 0 && archetype is Archetype a && !Craters(a))
        {
            result &= ~Modifier.Cratered;
            result |= pool[rng.Next(pool.Count)];
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
