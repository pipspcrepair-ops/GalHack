using System;
using System.Collections.Generic;
using System.Linq;

namespace CapitalBase.Core;

/// <summary>How much room each item takes, in building's storage units (SU). Unknown items take 1.</summary>
public sealed class ItemCatalog
{
    readonly Dictionary<string, double> _su;

    public ItemCatalog(Dictionary<string, double> su) => _su = su;

    public double SuOf(string item) => _su.TryGetValue(item, out double su) ? su : 1.0;

    /// <summary>The largest unit: a store with less free room than this may not take a whole one of everything.</summary>
    public double LargestUnit => _su.Count == 0 ? 1.0 : _su.Values.Max();
}

/// <summary>
/// One structure's shared storage: whole units of items, in stacks kept in ordinal order so every
/// transfer goes in the same order on every machine. Capacity lives with the structure (building's
/// containers), so it is passed in. Nothing here ever deletes an item: every move takes from one
/// cargo exactly what it adds to another.
///
/// Until shared/Items lands, this is the prototype's stand-in for item storage.
/// </summary>
public sealed class Cargo
{
    readonly SortedDictionary<string, int> _stacks = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, int> Stacks => _stacks;

    public int this[string item] => _stacks.TryGetValue(item, out int n) ? n : 0;

    /// <summary>Every unit of every item.</summary>
    public int Total
    {
        get
        {
            int n = 0;
            foreach (int c in _stacks.Values) n += c;
            return n;
        }
    }

    public double UsedSu(ItemCatalog items)
    {
        double su = 0;
        foreach (var (item, n) in _stacks) su += n * items.SuOf(item);
        return su;
    }

    public double FreeSu(ItemCatalog items, double capacity) => Math.Max(0.0, capacity - UsedSu(items));

    /// <summary>Puts units in, with no capacity check: callers decide what fits (see <see cref="Fits"/>).</summary>
    public void Add(string item, int n)
    {
        if (n < 0) throw new ArgumentOutOfRangeException(nameof(n));
        if (n == 0) return;
        _stacks[item] = this[item] + n;
    }

    public void Add(IEnumerable<KeyValuePair<string, int>> items)
    {
        foreach (var (item, n) in items) Add(item, n);
    }

    /// <summary>Takes units out. False (and nothing taken) if there aren't that many.</summary>
    public bool Take(string item, int n)
    {
        if (n < 0 || this[item] < n) return false;
        if (n == 0) return true;
        int left = this[item] - n;
        if (left == 0) _stacks.Remove(item);
        else _stacks[item] = left;
        return true;
    }

    public void Clear() => _stacks.Clear();

    /// <summary>How many whole units of an item fit into this much free room.</summary>
    public static int Fits(string item, ItemCatalog items, double freeSu)
    {
        double su = items.SuOf(item);
        return su <= 0 ? int.MaxValue : (int)Math.Floor(freeSu / su + 1e-9);
    }

    /// <summary>
    /// Moves whatever fits from one cargo into another with <paramref name="toCapacity"/> SU of
    /// room in all, item by item in ordinal order, whole units only. What doesn't fit stays where
    /// it was. Returns the units moved.
    /// </summary>
    public static int MoveWhatFits(Cargo from, Cargo to, double toCapacity, ItemCatalog items)
    {
        int moved = 0;
        foreach (string item in from._stacks.Keys.ToList())
        {
            double free = to.FreeSu(items, toCapacity);
            int n = Math.Min(from[item], Fits(item, items, free));
            if (n <= 0) continue;
            from.Take(item, n);
            to.Add(item, n);
            moved += n;
        }
        return moved;
    }

    /// <summary>
    /// Splits a fresh haul between two stores: as much as fits into the first (Home, when it is in
    /// range), the rest into the second (the vessel's own cargo). Returns what went to each; the
    /// caller made sure the two together have room for all of it (see <see cref="Room"/>).
    /// </summary>
    public static (int first, int second) Split(IEnumerable<KeyValuePair<string, int>> haul, Cargo first, double firstCapacity,
        Cargo second, ItemCatalog items)
    {
        int a = 0, b = 0;
        foreach (var (item, n) in haul.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            int there = first == null ? 0 : Math.Min(n, Fits(item, items, first.FreeSu(items, firstCapacity)));
            if (there > 0) first.Add(item, there);
            if (n - there > 0) second.Add(item, n - there);
            a += there;
            b += n - there;
        }
        return (a, b);
    }

    public Dictionary<string, int> ToDictionary() => new(_stacks, StringComparer.Ordinal);

    public override string ToString() =>
        _stacks.Count == 0 ? "empty" : string.Join(", ", _stacks.Select(kv => $"{kv.Key} {kv.Value}"));
}
