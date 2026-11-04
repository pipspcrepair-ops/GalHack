using System;
using System.Collections.Generic;
using System.Linq;

namespace Boarding;

/// <summary>Named stacks of items. Components removed whole from a ship land here too.</summary>
public sealed class Inventory
{
    public const string Keycard = "officer keycard";
    public const string Charge = "breaching charge";

    readonly Dictionary<string, int> _items = new();

    public event Action Changed;

    public int Count(string item) => _items.TryGetValue(item, out int n) ? n : 0;
    public bool Has(string item) => Count(item) > 0;

    public void Add(string item, int amount = 1)
    {
        if (amount <= 0) return;
        _items[item] = Count(item) + amount;
        Changed?.Invoke();
    }

    public bool Take(string item, int amount = 1)
    {
        if (Count(item) < amount) return false;
        _items[item] -= amount;
        if (_items[item] == 0) _items.Remove(item);
        Changed?.Invoke();
        return true;
    }

    public IEnumerable<(string item, int count)> Items =>
        _items.OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value));

    public override string ToString() =>
        _items.Count == 0 ? "empty" : string.Join(", ", Items.Select(i => i.count > 1 ? $"{i.item} x{i.count}" : i.item));
}
