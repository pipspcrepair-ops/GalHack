using System;
using System.Collections.Generic;
using System.Linq;

namespace DiegeticUi.Logic;

public sealed class Stack
{
    public ItemDef Item { get; }
    public int Count { get; internal set; }
    public double MassKg => Item.MassKg * Count;
    public double VolumeL => Item.VolumeL * Count;

    internal Stack(ItemDef item, int count)
    {
        Item = item;
        Count = count;
    }
}

/// <summary>
/// A container with a hard volume cap (a pocket is only so big) and a soft mass limit (heavy
/// just slows you down). Used for the suit, the ship's hold and the constructor's storage.
///
/// <see cref="Version"/> goes up on every change, so a screen showing it knows when to redraw
/// without comparing contents.
/// </summary>
public sealed class Inventory
{
    const double Epsilon = 1e-9;

    public string Name { get; }
    public double VolumeCapL { get; }
    public double SoftMassKg { get; }
    public int Version { get; private set; }

    readonly List<Stack> _stacks = new();

    public Inventory(string name, double volumeCapL, double softMassKg = double.PositiveInfinity)
    {
        Name = name;
        VolumeCapL = volumeCapL;
        SoftMassKg = softMassKg;
    }

    public IReadOnlyList<Stack> Stacks => _stacks;
    public double TotalMassKg => _stacks.Sum(s => s.MassKg);
    public double TotalVolumeL => _stacks.Sum(s => s.VolumeL);
    public double FreeVolumeL => Math.Max(0, VolumeCapL - TotalVolumeL);
    public bool Overweight => TotalMassKg > SoftMassKg + Epsilon;

    public int Count(string id) => _stacks.FirstOrDefault(s => s.Item.Id == id)?.Count ?? 0;

    /// <summary>How many of an item would still fit by volume.</summary>
    public int RoomFor(ItemDef item) =>
        item.VolumeL <= 0 ? int.MaxValue : (int)Math.Floor((FreeVolumeL + Epsilon) / item.VolumeL);

    /// <summary>Adds as many as fit and returns how many went in. Never exceeds the volume cap.</summary>
    public int Add(ItemDef item, int count)
    {
        int added = Math.Min(count, RoomFor(item));
        if (added <= 0) return 0;
        Stack stack = _stacks.FirstOrDefault(s => s.Item == item);
        if (stack == null) _stacks.Add(new Stack(item, added));
        else stack.Count += added;
        Version++;
        return added;
    }

    /// <summary>All or nothing: adds every unit, or none if they don't all fit.</summary>
    public bool TryAdd(ItemDef item, int count)
    {
        if (RoomFor(item) < count) return false;
        Add(item, count);
        return true;
    }

    /// <summary>Removes up to <paramref name="count"/> and returns how many were removed.</summary>
    public int Remove(string id, int count)
    {
        Stack stack = _stacks.FirstOrDefault(s => s.Item.Id == id);
        if (stack == null || count <= 0) return 0;
        int removed = Math.Min(count, stack.Count);
        stack.Count -= removed;
        if (stack.Count == 0) _stacks.Remove(stack);
        Version++;
        return removed;
    }
}

/// <summary>
/// What the suit is wearing: one armor slot and two booster slots. The union of their
/// protections is what keeps the vitals out of the red in a hazard.
/// </summary>
public sealed class SuitLoadout
{
    public const int BoosterSlots = 2;

    public ItemDef Armor { get; private set; }
    public ItemDef[] Boosters { get; } = new ItemDef[BoosterSlots];
    public int Version { get; private set; }

    public Hazard Protection
    {
        get
        {
            Hazard p = Armor?.Protects ?? Hazard.None;
            foreach (ItemDef b in Boosters) p |= b?.Protects ?? Hazard.None;
            return p;
        }
    }

    /// <summary>Which worn item covers a hazard, or null. The wrist shows this per hazard.</summary>
    public ItemDef CoveredBy(Hazard hazard)
    {
        if (Armor != null && Armor.Protects.HasFlag(hazard)) return Armor;
        return Boosters.FirstOrDefault(b => b != null && b.Protects.HasFlag(hazard));
    }

    public bool Equip(ItemDef item, int boosterSlot = 0)
    {
        switch (item.Kind)
        {
            case ItemKind.Armor: Armor = item; break;
            case ItemKind.Booster when boosterSlot is >= 0 and < BoosterSlots: Boosters[boosterSlot] = item; break;
            default: return false;
        }
        Version++;
        return true;
    }

    public void Unequip(ItemKind kind, int boosterSlot = 0)
    {
        if (kind == ItemKind.Armor) Armor = null;
        else if (kind == ItemKind.Booster) Boosters[boosterSlot] = null;
        Version++;
    }
}
