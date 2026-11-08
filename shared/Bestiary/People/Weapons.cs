using System;

namespace Wildlife;

/// <summary>How a carried thing is held: the rig's hold for each.</summary>
public enum HoldKind { None, LongGun, Pistol, Spear, Bow, Blade }

/// <summary>
/// What people fight with, by the world's tech era: low worlds have spears, bows, crossbows,
/// clubs and machetes (and the rare old long gun), mid worlds rifles, shotguns and pistols, high
/// worlds energy weapons alongside them. A role that carries a weapon carries its era's.
/// </summary>
public static class Weapons
{
    public static HoldKind HoldOf(string prop) => prop switch
    {
        "rifle" or "shotgun" or "musket" or "crossbow" or "laser_rifle" => HoldKind.LongGun,
        "pistol" or "energy_pistol" => HoldKind.Pistol,
        "spear" => HoldKind.Spear,
        "bow" => HoldKind.Bow,
        "club" or "machete" => HoldKind.Blade,
        _ => HoldKind.None,
    };

    public static bool Armed(string prop) => HoldOf(prop) != HoldKind.None;
    /// <summary>Shoots: raised and held on a target rather than swung.</summary>
    public static bool Ranged(string prop) => HoldOf(prop) is HoldKind.LongGun or HoldKind.Pistol or HoldKind.Bow;
    /// <summary>A gun made by a mid or high tech world.</summary>
    public static bool Modern(string prop) => prop is "rifle" or "shotgun" or "pistol" or "laser_rifle" or "energy_pistol";
    public static bool Energy(string prop) => prop is "laser_rifle" or "energy_pistol";

    static readonly (string prop, float weight)[] Low = { ("spear", 0.3f), ("bow", 0.24f), ("crossbow", 0.14f), ("machete", 0.12f), ("club", 0.12f), ("musket", 0.08f) };
    static readonly (string prop, float weight)[] Mid = { ("rifle", 0.55f), ("shotgun", 0.25f), ("pistol", 0.2f) };
    static readonly (string prop, float weight)[] High = { ("laser_rifle", 0.35f), ("energy_pistol", 0.15f), ("rifle", 0.3f), ("shotgun", 0.1f), ("pistol", 0.1f) };

    /// <summary>
    /// The weapon someone carries in place of a role's weapon: their world's era now, and now and
    /// then a relic of a higher era before the fall (0 low, 1 mid, 2 high).
    /// </summary>
    public static string ForEra(int now, int before, Rng rng)
    {
        int era = now >= 1 && before > now && rng.Chance(0.05f) ? before : now; // (a low world keeps to its own: its old long gun is the musket)
        var table = era <= 0 ? Low : era == 1 ? Mid : High;
        float total = 0f;
        foreach (var t in table) total += t.weight;
        float r = rng.Range(0f, total);
        foreach (var t in table)
            if ((r -= t.weight) <= 0f) return t.prop;
        return table[^1].prop;
    }
}
