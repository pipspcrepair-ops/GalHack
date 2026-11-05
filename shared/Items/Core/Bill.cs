using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace GalHack.Items;

/// <summary>
/// A list of items and counts: a block's cost, a recipe's inputs, a bill of materials, what's
/// missing. Kept sorted by id so two equal bills print and compare the same. Counts are never
/// zero or negative: an item at zero simply isn't in the bill.
/// </summary>
public sealed class Bill : IEnumerable<KeyValuePair<string, int>>, IEquatable<Bill>
{
    readonly SortedDictionary<string, int> _items = new(StringComparer.Ordinal);

    public Bill() { }

    public Bill(IEnumerable<KeyValuePair<string, int>> items)
    {
        foreach (var (id, n) in items) Add(id, n);
    }

    public static Bill Of(params (string id, int count)[] items)
    {
        var b = new Bill();
        foreach (var (id, n) in items) b.Add(id, n);
        return b;
    }

    public int this[string id] => _items.TryGetValue(id, out int n) ? n : 0;

    /// <summary>Distinct items.</summary>
    public int Kinds => _items.Count;
    public int Total => _items.Values.Sum();
    public bool IsEmpty => _items.Count == 0;
    public IEnumerable<string> Ids => _items.Keys;

    /// <summary>Adds (or, with a negative count, takes away down to zero) one item.</summary>
    public Bill Add(string id, int count)
    {
        if (count == 0) return this;
        int n = this[id] + count;
        if (n > 0) _items[id] = n;
        else _items.Remove(id);
        return this;
    }

    public Bill Add(Bill other, int times = 1)
    {
        foreach (var (id, n) in other._items) Add(id, n * times);
        return this;
    }

    public Bill Clone() => new(_items);

    public Bill Times(int factor)
    {
        var b = new Bill();
        if (factor > 0) foreach (var (id, n) in _items) b.Add(id, n * factor);
        return b;
    }

    /// <summary>What this bill has beyond <paramref name="other"/> (never negative).</summary>
    public Bill Minus(Bill other)
    {
        var b = new Bill();
        foreach (var (id, n) in _items)
        {
            int left = n - other[id];
            if (left > 0) b.Add(id, left);
        }
        return b;
    }

    /// <summary>True if this bill holds at least every count in <paramref name="other"/>.</summary>
    public bool Covers(Bill other) => other._items.All(kv => this[kv.Key] >= kv.Value);

    public bool Equals(Bill other) =>
        other != null && other._items.Count == _items.Count && _items.All(kv => other[kv.Key] == kv.Value);

    public override bool Equals(object obj) => obj is Bill b && Equals(b);

    public override int GetHashCode()
    {
        var h = new HashCode();
        foreach (var (id, n) in _items) { h.Add(id); h.Add(n); }
        return h.ToHashCode();
    }

    public IEnumerator<KeyValuePair<string, int>> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>"2 hull-plate, 1 servo", or "nothing".</summary>
    public override string ToString() => IsEmpty ? "nothing" : string.Join(", ", _items.Select(kv => $"{kv.Value} {kv.Key}"));
}
