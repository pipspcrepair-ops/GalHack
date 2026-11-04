using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SurvivalVitals.Model;

/// <summary>A food item carries the time it was made; its nourishment comes from its age.</summary>
public sealed class FoodItem
{
    public string Kind;
    public double MadeAt;
}

/// <summary>A packed-up survival replicator. Its fuel goes wherever it goes.</summary>
public sealed class ReplicatorKit
{
    public double Fuel;
}

/// <summary>
/// What the player carries (or a backpack or storage crate holds). Organics are kilograms of
/// biomass for replicators; everything else is counted. The survival tool is never in here:
/// it belongs to the player, so no drop rule can ever take it.
/// </summary>
public sealed class Inventory
{
    public double Organics;
    public int Ore, SmallBottles, LargeBottles, Medkits;
    public readonly List<FoodItem> Food = new();
    public readonly List<SuitItem> Suits = new();
    public readonly List<string> Boosters = new();
    public readonly List<ReplicatorKit> Kits = new();

    public bool IsEmpty =>
        Organics <= 0 && Ore == 0 && SmallBottles == 0 && LargeBottles == 0 && Medkits == 0
        && Food.Count == 0 && Suits.Count == 0 && Boosters.Count == 0 && Kits.Count == 0;

    /// <summary>Number of separate things, for messages ("12 items").</summary>
    public int ItemCount =>
        (Organics > 0 ? 1 : 0) + Ore + SmallBottles + LargeBottles + Medkits
        + Food.Count + Suits.Count + Boosters.Count + Kits.Count;

    public int CountFood(string kind) => Food.Count(f => f.Kind == kind);

    /// <summary>Moves everything from another inventory into this one, leaving it empty.</summary>
    public void Absorb(Inventory other)
    {
        Organics += other.Organics;
        Ore += other.Ore;
        SmallBottles += other.SmallBottles;
        LargeBottles += other.LargeBottles;
        Medkits += other.Medkits;
        Food.AddRange(other.Food);
        Suits.AddRange(other.Suits);
        Boosters.AddRange(other.Boosters);
        Kits.AddRange(other.Kits);
        other.Clear();
    }

    /// <summary>Moves everything into a new inventory (a backpack, say) and returns it.</summary>
    public Inventory TakeAll()
    {
        var bag = new Inventory();
        bag.Absorb(this);
        return bag;
    }

    public void Clear()
    {
        Organics = 0;
        Ore = SmallBottles = LargeBottles = Medkits = 0;
        Food.Clear();
        Suits.Clear();
        Boosters.Clear();
        Kits.Clear();
    }

    /// <summary>
    /// Takes the item of this kind closest to spoiling, so the player never has to sort food by
    /// date. Items made at the same time are interchangeable.
    /// </summary>
    public FoodItem TakeOldestFood(string kind)
    {
        FoodItem oldest = null;
        foreach (FoodItem f in Food)
            if (f.Kind == kind && (oldest == null || f.MadeAt < oldest.MadeAt)) oldest = f;
        if (oldest != null) Food.Remove(oldest);
        return oldest;
    }

    /// <summary>A stable text form of the contents, for tests and determinism checks.</summary>
    public string Fingerprint()
    {
        var sb = new StringBuilder();
        sb.Append(FormattableString(Organics)).Append('|').Append(Ore).Append('|').Append(SmallBottles).Append('|')
          .Append(LargeBottles).Append('|').Append(Medkits).Append('|');
        foreach (FoodItem f in Food.OrderBy(f => f.Kind).ThenBy(f => f.MadeAt)) sb.Append(f.Kind).Append('@').Append(FormattableString(f.MadeAt)).Append(',');
        sb.Append('|');
        foreach (SuitItem s in Suits.OrderBy(s => s.Kind)) sb.Append(s.Kind).Append(':').Append(FormattableString(s.Durability)).Append(',');
        sb.Append('|').Append(string.Join(",", Boosters.OrderBy(b => b))).Append('|');
        foreach (ReplicatorKit k in Kits) sb.Append(FormattableString(k.Fuel)).Append(',');
        return sb.ToString();
    }

    static string FormattableString(double v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
}
