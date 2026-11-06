using System;

namespace Planets.Gen;

/// <summary>
/// The kinds of land a world's regions can be. See LANDFORMS.md for the catalogue: what each
/// looks like, where it occurs and which archetypes use it. Values are stable (saved and hashed),
/// so new landforms go on the end.
///
/// This file has no dependencies on the rest of the generator, so other prototypes (wildlife)
/// can compile it on its own.
/// </summary>
public enum Landform : byte
{
    Plains, Steppe, Hills, Mountains, Plateau, Mesas, Canyonlands, RiverValleys,
    DuneSea, RockyDesert, Badlands, Wetlands, LakeDistrict, Delta, IcePlain, Fjords,
    CoastalCliffs, Archipelago, VolcanicField, BasaltPlains, Karst, ShardFields, DomeFields, Ringlands,
}

/// <summary>How a landform plays: open ground to build, drive and fly low over; rolling; or rugged.</summary>
public enum Openness : byte { Open, Rolling, Rugged }

/// <summary>
/// The ground a landform gives animals, which shapes what lives there: herds and their hunters on
/// open ground, climbers and gliders on steep ground, burrowers in sand, waders in the wet.
/// </summary>
public enum LandformHabitat : byte { Open, Steep, Sand, Wet, Broken }

/// <summary>
/// The landforms at one spot and how much each counts, strongest first. Up to four take part at
/// a border; deep inside a region there is one, at full weight. Weights add up to 1.
/// </summary>
public struct LandformBlend
{
    public const int Max = 4;
    public Landform L0, L1, L2, L3;
    public float W0, W1, W2, W3;
    public int Count;

    public static LandformBlend Of(Landform landform) => new() { L0 = landform, W0 = 1f, Count = 1 };

    /// <summary>The landform that counts most here.</summary>
    public readonly Landform Dominant => L0;

    public readonly Landform LandformAt(int i) => i switch { 0 => L0, 1 => L1, 2 => L2, _ => L3 };
    public readonly float WeightAt(int i) => i switch { 0 => W0, 1 => W1, 2 => W2, _ => W3 };

    /// <summary>How much a landform counts here, 0 to 1.</summary>
    public readonly float Weight(Landform landform)
    {
        for (int i = 0; i < Count; i++)
            if (LandformAt(i) == landform) return WeightAt(i);
        return 0f;
    }

    public readonly bool Has(Landform landform) => Weight(landform) > 0f;

    /// <summary>Adds weight to a landform (merging with any already here). When full, the weakest entry gives way.</summary>
    public void Add(Landform landform, float weight)
    {
        if (weight <= 0f) return;
        for (int i = 0; i < Count; i++)
        {
            if (LandformAt(i) != landform) continue;
            Set(i, landform, WeightAt(i) + weight);
            return;
        }
        if (Count < Max)
        {
            Set(Count++, landform, weight);
            return;
        }
        int weakest = 0;
        for (int i = 1; i < Count; i++)
            if (WeightAt(i) < WeightAt(weakest)) weakest = i;
        if (WeightAt(weakest) < weight) Set(weakest, landform, weight);
    }

    /// <summary>Strongest first, weights adding up to 1.</summary>
    public void Normalize()
    {
        float sum = W0 + (Count > 1 ? W1 : 0f) + (Count > 2 ? W2 : 0f) + (Count > 3 ? W3 : 0f);
        if (sum <= 0f)
        {
            this = default;
            return;
        }
        for (int i = 0; i < Count; i++) Set(i, LandformAt(i), WeightAt(i) / sum);
        // Insertion sort, four entries at most.
        for (int i = 1; i < Count; i++)
        for (int j = i; j > 0 && WeightAt(j) > WeightAt(j - 1); j--)
        {
            Landform l = LandformAt(j);
            float w = WeightAt(j);
            Set(j, LandformAt(j - 1), WeightAt(j - 1));
            Set(j - 1, l, w);
        }
    }

    void Set(int i, Landform l, float w)
    {
        switch (i)
        {
            case 0: L0 = l; W0 = w; break;
            case 1: L1 = l; W1 = w; break;
            case 2: L2 = l; W2 = w; break;
            default: L3 = l; W3 = w; break;
        }
    }

    public override readonly string ToString()
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(LandformInfo.Name(LandformAt(i))).Append(' ').Append((WeightAt(i) * 100).ToString("F0")).Append('%');
        }
        return sb.ToString();
    }
}

/// <summary>Names and kinds of every landform.</summary>
public static class LandformInfo
{
    public static readonly Landform[] All = (Landform[])Enum.GetValues(typeof(Landform));
    public static int Count => All.Length;

    public static string Name(Landform l) => l switch
    {
        Landform.Plains => "plains",
        Landform.Steppe => "steppe",
        Landform.Hills => "hills",
        Landform.Mountains => "mountains",
        Landform.Plateau => "plateau",
        Landform.Mesas => "mesas",
        Landform.Canyonlands => "canyonlands",
        Landform.RiverValleys => "river valleys",
        Landform.DuneSea => "dune sea",
        Landform.RockyDesert => "rocky desert",
        Landform.Badlands => "badlands",
        Landform.Wetlands => "wetlands",
        Landform.LakeDistrict => "lake district",
        Landform.Delta => "delta",
        Landform.IcePlain => "ice plain",
        Landform.Fjords => "fjords",
        Landform.CoastalCliffs => "coastal cliffs",
        Landform.Archipelago => "archipelago",
        Landform.VolcanicField => "volcanic field",
        Landform.BasaltPlains => "basalt plains",
        Landform.Karst => "karst",
        Landform.ShardFields => "shard fields",
        Landform.DomeFields => "dome fields",
        Landform.Ringlands => "ringlands",
        _ => l.ToString(),
    };

    public static Openness Openness(Landform l) => l switch
    {
        Landform.Plains or Landform.Steppe or Landform.RockyDesert or Landform.IcePlain or Landform.BasaltPlains
            or Landform.Wetlands or Landform.Delta => Gen.Openness.Open,
        Landform.Hills or Landform.RiverValleys or Landform.LakeDistrict or Landform.DuneSea or Landform.DomeFields
            or Landform.Ringlands or Landform.Archipelago => Gen.Openness.Rolling,
        _ => Gen.Openness.Rugged,
    };

    public static LandformHabitat Habitat(Landform l) => l switch
    {
        Landform.Plains or Landform.Steppe or Landform.IcePlain or Landform.BasaltPlains or Landform.RockyDesert
            or Landform.Ringlands => LandformHabitat.Open,
        Landform.Mountains or Landform.Fjords or Landform.CoastalCliffs or Landform.Mesas or Landform.Canyonlands
            or Landform.Plateau or Landform.Karst or Landform.ShardFields => LandformHabitat.Steep,
        Landform.DuneSea or Landform.Badlands => LandformHabitat.Sand,
        Landform.Delta or Landform.Wetlands or Landform.LakeDistrict or Landform.RiverValleys or Landform.Archipelago => LandformHabitat.Wet,
        _ => LandformHabitat.Broken,
    };

    /// <summary>Landforms that live along a coast (and offshore) rather than filling an inland region.</summary>
    public static bool IsCoastal(Landform l) => l is Landform.Delta or Landform.Fjords or Landform.CoastalCliffs or Landform.Archipelago;
}
