using System;
using System.Collections.Generic;
using System.Linq;

namespace GalHack.Items;

/// <summary>
/// The one shared storage of a structure: every container on it adds capacity to this single
/// inventory, and every machine takes its inputs from it and puts its products back (design
/// section 18). Capacity is in storage units; an item's volume says how much one unit takes.
///
/// <see cref="Version"/> goes up on every change, so a screen knows when to redraw and a queue
/// when to replan, without comparing contents.
/// </summary>
public sealed class SharedStorage
{
    const double Epsilon = 1e-9;

    readonly ItemCatalogue _items;
    readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);
    double _capacity;

    public string Name { get; set; }
    public int Version { get; private set; }
    public double UsedSu { get; private set; }
    public double MassKg { get; private set; }

    /// <summary>Total storage units. Infinite for a spill crate.</summary>
    public double CapacitySu
    {
        get => _capacity;
        set
        {
            if (value == _capacity) return;
            _capacity = value;
            Version++;
        }
    }

    public double FreeSu => Math.Max(0, _capacity - UsedSu);
    /// <summary>More inside than there is room for (a container was taken away).</summary>
    public bool OverCapacity => UsedSu > _capacity + Epsilon;
    public ItemCatalogue Catalogue => _items;

    public SharedStorage(ItemCatalogue items, string name, double capacitySu)
    {
        _items = items;
        Name = name;
        _capacity = capacitySu;
    }

    public static SharedStorage Unbounded(ItemCatalogue items, string name) => new(items, name, double.PositiveInfinity);

    public int Count(string id) => _counts.TryGetValue(id, out int n) ? n : 0;
    public int this[string id] => Count(id);
    public bool IsEmpty => _counts.Count == 0;

    /// <summary>Everything inside, as a bill (a copy).</summary>
    public Bill Contents => new(_counts);

    /// <summary>Stacks for a screen: by category, then rarity, then name.</summary>
    public IEnumerable<(ItemDef Item, int Count)> Stacks =>
        _counts.Select(kv => (Item: _items.Item(kv.Key), Count: kv.Value))
            .OrderBy(s => s.Item.Category).ThenBy(s => s.Item.Rarity).ThenBy(s => s.Item.Name, StringComparer.Ordinal);

    /// <summary>How many of an item still fit.</summary>
    public int RoomFor(string id)
    {
        if (double.IsPositiveInfinity(_capacity)) return int.MaxValue;
        double v = _items.Item(id).VolumeSu;
        return (int)Math.Min(int.MaxValue, Math.Floor((FreeSu + Epsilon) / v));
    }

    /// <summary>True if every unit of a bill fits at once.</summary>
    public bool Fits(Bill bill) => double.IsPositiveInfinity(_capacity) || _items.VolumeOf(bill) <= FreeSu + Epsilon;

    /// <summary>Adds as many as fit and returns how many went in.</summary>
    public int Add(string id, int count)
    {
        int added = Math.Min(count, RoomFor(id));
        if (added <= 0) return 0;
        Change(id, added);
        return added;
    }

    /// <summary>Adds all of a bill that fits and returns what didn't (never lost: the caller spills it).</summary>
    public Bill AddAll(Bill bill)
    {
        var left = new Bill();
        foreach (var (id, n) in bill)
        {
            int added = Add(id, n);
            if (added < n) left.Add(id, n - added);
        }
        return left;
    }

    /// <summary>All or nothing.</summary>
    public bool TryAdd(Bill bill)
    {
        if (!Fits(bill)) return false;
        foreach (var (id, n) in bill) Change(id, n);
        return true;
    }

    public bool Has(Bill bill) => bill.All(kv => Count(kv.Key) >= kv.Value);

    /// <summary>What a bill needs beyond what's here.</summary>
    public Bill Missing(Bill bill)
    {
        var missing = new Bill();
        foreach (var (id, n) in bill)
            if (Count(id) < n) missing.Add(id, n - Count(id));
        return missing;
    }

    /// <summary>Takes a whole bill, or nothing if any of it is short.</summary>
    public bool TryTake(Bill bill)
    {
        if (!Has(bill)) return false;
        foreach (var (id, n) in bill) Change(id, -n);
        return true;
    }

    /// <summary>Removes up to <paramref name="count"/> and returns how many came out.</summary>
    public int Remove(string id, int count)
    {
        int removed = Math.Min(count, Count(id));
        if (removed > 0) Change(id, -removed);
        return removed;
    }

    /// <summary>
    /// Takes out whatever no longer fits after the capacity shrank, newest-looking (largest
    /// volume) first so few stacks are split. The caller spills it to a crate.
    /// </summary>
    public Bill TakeOverflow()
    {
        var overflow = new Bill();
        foreach (var (item, _) in Stacks.OrderByDescending(s => s.Item.VolumeSu).ToList())
        {
            if (!OverCapacity) break;
            double excess = UsedSu - _capacity;
            int n = Math.Min(Count(item.Id), (int)Math.Ceiling(excess / item.VolumeSu - Epsilon));
            if (n <= 0) continue;
            Change(item.Id, -n);
            overflow.Add(item.Id, n);
        }
        return overflow;
    }

    void Change(string id, int delta)
    {
        ItemDef item = _items.Item(id);
        int n = Count(id) + delta;
        if (n < 0) throw new InvalidOperationException($"{Name}: {id} would go below zero");
        if (n == 0) _counts.Remove(id);
        else _counts[id] = n;
        UsedSu += item.VolumeSu * delta;
        MassKg += item.MassKg * delta;
        if (_counts.Count == 0) UsedSu = MassKg = 0; // no drift from summing fractions
        Version++;
    }
}
