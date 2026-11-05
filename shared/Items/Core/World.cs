using System;
using System.Collections.Generic;
using System.Linq;

namespace GalHack.Items;

/// <summary>Building and manufacturing run in one of two modes, chosen per world or session.</summary>
public enum GameMode
{
    /// <summary>Placing a block costs its bill from shared storage and removing it refunds it; machines use real materials.</summary>
    Survival,
    /// <summary>Placing is free and unlimited, nothing is consumed or refunded, every block is available; machines run without material checks.</summary>
    Creative,
}

/// <summary>
/// The one per-world setting every system reads: block placement, the constructors, the
/// deconstructor and the blueprint factory all ask this, so the mode can never disagree between
/// them. Changing it mid-session is safe: nothing in flight is lost.
/// </summary>
public sealed class WorldRules
{
    GameMode _mode;

    public WorldRules(GameMode mode = GameMode.Survival) => _mode = mode;

    public GameMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value) return;
            _mode = value;
            Version++;
        }
    }

    public bool Creative => _mode == GameMode.Creative;
    public bool Survival => _mode == GameMode.Survival;
    public int Version { get; private set; }
}

/// <summary>
/// A crate that holds what a full storage couldn't take. It never despawns: it stays where it
/// was dropped until a player empties it, and only an empty crate is ever removed.
/// </summary>
public sealed class SpillCrate
{
    public int Id { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double Z { get; init; }
    public SharedStorage Contents { get; init; }
    public bool IsEmpty => Contents.IsEmpty;
}

/// <summary>Every spill crate in a world. Spills near an existing crate go into it rather than littering.</summary>
public sealed class CrateYard
{
    readonly ItemCatalogue _items;
    readonly List<SpillCrate> _crates = new();
    int _nextId = 1;

    public CrateYard(ItemCatalogue items) => _items = items;

    public IReadOnlyList<SpillCrate> Crates => _crates;
    public int Version { get; private set; }

    /// <summary>Everything held in every crate.</summary>
    public Bill Total
    {
        get
        {
            var all = new Bill();
            foreach (SpillCrate c in _crates) all.Add(c.Contents.Contents);
            return all;
        }
    }

    /// <summary>Drops items into the crate within <paramref name="mergeRadius"/> of a point, or a new crate there. Returns the crate, or null if there was nothing to spill.</summary>
    public SpillCrate Spill(Bill items, double x, double y, double z, double mergeRadius = 2.5)
    {
        if (items == null || items.IsEmpty) return null;
        SpillCrate crate = _crates
            .Where(c => Dist2(c, x, y, z) <= mergeRadius * mergeRadius)
            .OrderBy(c => Dist2(c, x, y, z))
            .FirstOrDefault();
        if (crate == null)
        {
            crate = new SpillCrate { Id = _nextId++, X = x, Y = y, Z = z, Contents = SharedStorage.Unbounded(_items, "Spill crate") };
            _crates.Add(crate);
        }
        crate.Contents.TryAdd(items);
        Version++;
        return crate;
    }

    /// <summary>Moves what fits from a crate into a storage. An emptied crate is removed; returns what moved.</summary>
    public Bill Collect(SpillCrate crate, SharedStorage into)
    {
        var moved = new Bill();
        foreach (var (item, n) in crate.Contents.Stacks.ToList())
        {
            int added = into.Add(item.Id, n);
            if (added == 0) continue;
            crate.Contents.Remove(item.Id, added);
            moved.Add(item.Id, added);
        }
        if (crate.IsEmpty) _crates.Remove(crate);
        Version++;
        return moved;
    }

    static double Dist2(SpillCrate c, double x, double y, double z) =>
        (c.X - x) * (c.X - x) + (c.Y - y) * (c.Y - y) + (c.Z - z) * (c.Z - z);
}
