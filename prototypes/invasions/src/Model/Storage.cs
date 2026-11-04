using System;
using System.Collections.Generic;
using System.Linq;
using Invasions.Director;

namespace Invasions.Model;

/// <summary>One container's share of the network: the item units physically inside it.</summary>
public sealed class StorageContainer
{
    public int BlockId { get; init; }
    public int Capacity { get; init; }
    public SortedDictionary<string, int> Items { get; } = new(StringComparer.Ordinal);
    public int Total => Items.Values.Sum();
    public int Free => Capacity - Total;
}

/// <summary>
/// A structure's storage: one shared inventory across all its containers. Players see and use the
/// merged contents from any container. Physically each item unit sits in one container (new goods go
/// to whichever has most room), so wrecking a container spills exactly what was inside it.
/// </summary>
public sealed class StorageNetwork
{
    readonly List<StorageContainer> _containers = new();

    public IReadOnlyList<StorageContainer> Containers => _containers;
    public int Capacity => _containers.Sum(c => c.Capacity);
    public int Total => _containers.Sum(c => c.Total);
    public int Free => Capacity - Total;

    public void AddContainer(int blockId, int capacity) =>
        _containers.Add(new StorageContainer { BlockId = blockId, Capacity = Math.Max(0, capacity) });

    /// <summary>The shared inventory: every container's items merged.</summary>
    public SortedDictionary<string, int> Contents()
    {
        var merged = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (StorageContainer c in _containers)
            foreach (var (item, count) in c.Items)
                merged[item] = merged.GetValueOrDefault(item) + count;
        return merged;
    }

    /// <summary>Adds up to <paramref name="count"/> units and returns how many fitted.</summary>
    public int Add(string item, int count)
    {
        int added = 0;
        while (added < count)
        {
            StorageContainer roomiest = null;
            foreach (StorageContainer c in _containers)
                if (c.Free > 0 && (roomiest == null || c.Free > roomiest.Free)) roomiest = c;
            if (roomiest == null) break;

            // Fill the roomiest container until it is level with the next roomiest, keeping them balanced.
            int nextFree = _containers.Where(c => c != roomiest).Select(c => c.Free).DefaultIfEmpty(0).Max();
            int chunk = Math.Min(count - added, Math.Max(1, roomiest.Free - nextFree + 1));
            chunk = Math.Min(chunk, roomiest.Free);
            roomiest.Items[item] = roomiest.Items.GetValueOrDefault(item) + chunk;
            added += chunk;
        }
        return added;
    }

    /// <summary>Removes a wrecked container from the network and hands back everything that was in it.</summary>
    public SortedDictionary<string, int> RemoveContainer(int blockId)
    {
        StorageContainer c = _containers.FirstOrDefault(x => x.BlockId == blockId);
        if (c == null) return new SortedDictionary<string, int>(StringComparer.Ordinal);
        _containers.Remove(c);
        return new SortedDictionary<string, int>(c.Items, StringComparer.Ordinal);
    }
}

/// <summary>A crate of goods spilled from a wrecked container. It never despawns; it only goes away once emptied.</summary>
public sealed class SpillCrate
{
    public int Id { get; init; }
    public GroundPos Position { get; init; }
    public int FromBlockId { get; init; }
    public SortedDictionary<string, int> Items { get; } = new(StringComparer.Ordinal);
    public int Total => Items.Values.Sum();
}

/// <summary>
/// Where every item in the base lives: in the storage network or in a spill crate. Nothing else can
/// hold or remove items. Invaders never take any, and wrecking a container moves its contents into a
/// crate in one step, so the total never changes.
/// </summary>
public sealed class LootLedger
{
    public StorageNetwork Storage { get; } = new();
    public IReadOnlyList<SpillCrate> Crates => _crates;

    readonly List<SpillCrate> _crates = new();
    int _nextCrateId = 1;

    public int CrateTotal => _crates.Sum(c => c.Total);
    public int TotalItems => Storage.Total + CrateTotal;

    /// <summary>Every item the base owns, in storage or in crates.</summary>
    public SortedDictionary<string, int> AllItems()
    {
        SortedDictionary<string, int> all = Storage.Contents();
        foreach (SpillCrate crate in _crates)
            foreach (var (item, count) in crate.Items)
                all[item] = all.GetValueOrDefault(item) + count;
        return all;
    }

    /// <summary>Moves a wrecked container's contents into a new crate where it stood. Null if it was empty.</summary>
    public SpillCrate Spill(int containerBlockId, GroundPos where)
    {
        SortedDictionary<string, int> items = Storage.RemoveContainer(containerBlockId);
        if (items.Values.Sum() == 0) return null;
        var crate = new SpillCrate { Id = _nextCrateId++, Position = where, FromBlockId = containerBlockId };
        foreach (var (item, count) in items) crate.Items[item] = count;
        _crates.Add(crate);
        return crate;
    }

    /// <summary>
    /// Moves as much of a crate as fits back into storage. The crate stays, with whatever didn't fit,
    /// until it is empty. Returns the units moved.
    /// </summary>
    public int Recover(SpillCrate crate)
    {
        int moved = 0;
        foreach (string item in crate.Items.Keys.ToList())
        {
            int fitted = Storage.Add(item, crate.Items[item]);
            moved += fitted;
            crate.Items[item] -= fitted;
            if (crate.Items[item] == 0) crate.Items.Remove(item);
        }
        if (crate.Total == 0) _crates.Remove(crate);
        return moved;
    }
}
