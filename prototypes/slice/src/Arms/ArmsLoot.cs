using System;
using System.Collections.Generic;
using GalHack.Arms;
using GalHack.Items;
using Wildlife;

namespace Slice.Arms;

/// <summary>
/// Weapons in a place's caches, by place and rarity, matched to the world's era (the same weapon
/// items people carry and the player makes): a sidearm in a house drawer now and then; long guns in
/// police, military and fortified places; launchers deep in a bunker's vault or a hidden stash. Each
/// comes with an item or two of its ammo. Nothing is placed here that the rows don't have.
/// </summary>
public static class ArmsLoot
{
    static readonly string[] Armed = { "military", "fort", "police", "bunker", "barracks", "armory", "armoury", "garrison", "outpost", "security", "prison", "checkpoint", "gang", "raider" };
    static readonly string[] Homes = { "house", "farmstead", "settlement", "civilian", "farm", "home", "shop", "wayside" };

    /// <summary>Weapons (with ammo) put in one cache: the place's tags, the cache's tier, whether it's deep (a vault, a hidden stash), the era (0 low, 1 mid, 2 high), and a hash for the rolls.</summary>
    public static void Add(Bill bill, IReadOnlyCollection<string> tags, int tier, bool deep, int era, uint hash, Armoury table, ItemCatalogue items)
    {
        if (table == null || items == null) return;
        var rng = Rng.For(hash, "arms-loot");
        bool armed = Any(tags, Armed), home = Any(tags, Homes);
        string item = null;
        if (deep && era >= 1 && rng.Chance(0.25f + 0.1f * tier)) item = rng.Chance(0.5f) ? "break-launcher" : "shoulder-tube";
        else if (armed && rng.Chance(0.3f + 0.08f * tier)) item = Weapons.ItemOf(LongGun(era, rng));
        else if (home && rng.Chance(0.1f + 0.04f * tier)) item = Weapons.ItemOf(Sidearm(era, rng));
        else if (rng.Chance(0.04f + 0.03f * tier)) item = Weapons.ItemOf(Weapons.ForEra(era, era, rng));
        if (item == null || item == Weapons.Fists || !items.TryGet(item, out ItemDef def)) return;
        // Rare things only in the better caches.
        if (def.Rarity >= GalHack.Items.Rarity.Rare && tier < 2 && !deep) return;
        WeaponDef w = table.OfItem(item);
        if (w == null) return;
        bill.Add(item, 1);
        if (w.Ammo != "" && w.Ammo != w.Item && items.Has(w.Ammo)) bill.Add(w.Ammo, rng.Int(1, 2 + tier));
    }

    static string LongGun(int era, Rng rng) => era switch
    {
        <= 0 => rng.Chance(0.5f) ? "musket" : "crossbow",
        1 => rng.Chance(0.65f) ? "rifle" : "shotgun",
        _ => rng.Chance(0.6f) ? "laser_rifle" : "rifle",
    };

    static string Sidearm(int era, Rng rng) => era switch
    {
        <= 0 => rng.Chance(0.5f) ? "machete" : "bow",
        1 => rng.Chance(0.7f) ? "pistol" : "shotgun",
        _ => rng.Chance(0.6f) ? "energy_pistol" : "pistol",
    };

    static bool Any(IReadOnlyCollection<string> tags, string[] words)
    {
        if (tags == null) return false;
        foreach (string t in tags)
            foreach (string w in words)
                if (t == w) return true;
        return false;
    }
}
