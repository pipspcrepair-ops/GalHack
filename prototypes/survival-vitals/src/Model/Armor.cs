using System;
using System.Collections.Generic;

namespace SurvivalVitals.Model;

/// <summary>
/// What a suit or booster does to the body. Additive fields add up; multipliers multiply.
/// Field meanings are documented at the top of armor.json.
/// </summary>
public sealed class Modifiers
{
    public double Armor { get; set; }
    public double ColdProtection { get; set; }
    public double HeatProtection { get; set; }
    public double RadiationShielding { get; set; }
    public double OxygenCapacity { get; set; }
    public double JetpackSeconds { get; set; }
    public double OxygenUse { get; set; } = 1;
    public double Hunger { get; set; } = 1;
    public double StaminaUse { get; set; } = 1;
    public double Speed { get; set; } = 1;
    public double FallDamage { get; set; } = 1;

    /// <summary>Armor never stops everything: some of every hit gets through.</summary>
    public const double MaxArmor = 0.8;

    public void Add(Modifiers o, double effect = 1)
    {
        Armor = Math.Min(MaxArmor, Armor + o.Armor * effect);
        ColdProtection += o.ColdProtection * effect;
        HeatProtection += o.HeatProtection * effect;
        // Shields stack like filters in a row, so no stack ever blocks all radiation.
        RadiationShielding = 1 - (1 - RadiationShielding) * (1 - o.RadiationShielding * effect);
        OxygenCapacity += o.OxygenCapacity * effect;
        JetpackSeconds += o.JetpackSeconds * effect;
        OxygenUse *= 1 + (o.OxygenUse - 1) * effect;
        Hunger *= 1 + (o.Hunger - 1) * effect;
        StaminaUse *= 1 + (o.StaminaUse - 1) * effect;
        Speed *= 1 + (o.Speed - 1) * effect;
        FallDamage *= 1 + (o.FallDamage - 1) * effect;
    }
}

/// <summary>One suit the player owns, worn or carried. Durability runs from 1 (new) to 0.</summary>
public sealed class SuitItem
{
    public string Kind;
    public double Durability = 1;

    public SuitItem Copy() => new() { Kind = Kind, Durability = Durability };
}

/// <summary>
/// What the player wears: at most one suit, and as many boosters as it has slots. A light suit
/// is the minimum for a helmet, suit oxygen and the jetpack.
/// </summary>
public sealed class Loadout
{
    public SuitItem Suit;
    public readonly List<string> Boosters = new();
    public bool HelmetOn;

    public bool HasSuit => Suit != null;

    public int Slots(ArmorCatalog catalog) => Suit == null ? 0 : catalog.Suit(Suit.Kind).Slots;

    public Modifiers Total(ArmorCatalog catalog)
    {
        var total = new Modifiers();
        if (Suit == null) return total;
        double effect = Suit.Durability <= 0 ? catalog.BrokenSuitEffect : 1;
        total.Add(catalog.Suit(Suit.Kind).Modifiers, effect);
        foreach (string id in Boosters) total.Add(catalog.Booster(id).Modifiers);
        return total;
    }

    public double OxygenCapacity(ArmorCatalog catalog, OxygenRules rules) =>
        HasSuit ? rules.Max + Total(catalog).OxygenCapacity : 0;
}
