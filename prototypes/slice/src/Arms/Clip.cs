using System;
using GalHack.Arms;
using Godot;
using Wildlife;

namespace Slice.Arms;

/// <summary>
/// The weapon someone other than the player carries (a person, a machine's mount): its row, the
/// rounds in it and the ammo they have for it. They use it up as the player does: a magazine,
/// a reload from what they carry, and nothing once that's gone. A hand weapon needs none.
/// </summary>
public sealed class Clip
{
    public readonly WeaponDef Def;
    /// <summary>Rounds in the weapon, rounds loose from an opened item, ammo items still carried.</summary>
    public int Loaded, Loose, Items;
    /// <summary>Rounds one ammo item gives.</summary>
    public readonly int PerItem;
    /// <summary>Rounds fired so far.</summary>
    public int Fired;

    public Clip(WeaponDef def, int perItem, int items)
    {
        Def = def;
        PerItem = Math.Max(1, perItem);
        Items = Math.Max(0, items);
        Loaded = Strike ? 0 : def.Magazine;
    }

    /// <summary>A weapon's row and how many rounds an item of its ammo gives (the row's own number, else the item's).</summary>
    public static Clip Of(WeaponDef def, GalHack.Items.ItemCatalogue items, int ammoItems)
    {
        int per = def.PerItem > 0 ? def.PerItem : items != null && def.Ammo != "" && items.TryGet(def.Ammo, out var it) && it.Rounds > 0 ? it.Rounds : 1;
        return new Clip(def, per, ammoItems);
    }

    public bool Strike => Def.Delivery == Delivery.Strike;
    /// <summary>Nothing left to fire: not in the weapon, not loose, not in an item.</summary>
    public bool Spent => !Strike && Loaded <= 0 && Loose <= 0 && Items <= 0;
    /// <summary>Rounds in all.</summary>
    public int Rounds => Loaded + Loose + Items * PerItem;

    /// <summary>One round goes (true), or there's none in the weapon.</summary>
    public bool Shot()
    {
        if (Strike) return true;
        if (Loaded <= 0) return false;
        Loaded--;
        Fired++;
        return true;
    }

    /// <summary>Fills the weapon from what they carry; false if nothing went in.</summary>
    public bool Reload()
    {
        if (Strike) return true;
        int before = Loaded;
        while (Loaded < Def.Magazine)
        {
            if (Loose <= 0)
            {
                if (Items <= 0) break;
                Items--;
                Loose = PerItem;
            }
            int take = Math.Min(Loose, Def.Magazine - Loaded);
            Loose -= take;
            Loaded += take;
        }
        return Loaded > before;
    }

    /// <summary>Rounds a pull of the trigger lets go: a burst for an automatic, its own burst, else one.</summary>
    public int Burst(Wildlife.Rng rng) => Def.Trigger switch
    {
        Trigger.Auto => rng.Int(3, 5),
        Trigger.Burst => Math.Max(1, Def.Burst),
        _ => 1,
    };

    /// <summary>The ammo items left to drop with the weapon: those carried, and one for what's loose or loaded if it's half an item or more.</summary>
    public int ItemsLeft => Strike || Def.Ammo == "" ? 0 : Items + (Loose + Loaded >= Math.Max(1, PerItem / 2) ? 1 : 0);

    /// <summary>How far they'll shoot with it, m: as far as its shot carries, pellets kept close, never past what a person can aim at.</summary>
    public float Range => Strike ? Def.Reach : Def.Pellets != null ? 14f : Math.Clamp(Def.Speed * Def.Life * 0.7f, 8f, 42f);

    /// <summary>Gun frame (grip at the origin, barrel along -Z, up +Y) into a rig's prop frame (grip at the origin, along +Y, up +Z).</summary>
    public static readonly Basis GunInProp = new(Vector3.Right, MathF.PI * 0.5f);
}
