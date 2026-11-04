using System;
using System.Linq;
using Invasions.Director;

namespace Invasions.Model;

/// <summary>
/// The player base's rules, without any scene: blocks take damage and break, wrecked containers spill
/// into crates, the core keeps the base online, the generator powers it. The scene's BaseView listens
/// to these events to draw it.
/// </summary>
public sealed class BaseModel
{
    public BaseData Data { get; }
    public GroundPos Centre { get; }
    public BaseGrid Grid { get; }
    public LootLedger Loot { get; } = new();

    /// <summary>A living core keeps the base online. Without one: no power, turrets off, doors dead.</summary>
    public bool Online => Grid.Core != null;

    /// <summary>Online and with a working generator.</summary>
    public bool Powered => Online && Grid.Blocks.Any(b => b.Kind == BlockKind.Generator && b.Alive);

    public event Action<Block> BlockDamaged;
    public event Action<Block> BlockDestroyed;
    public event Action<Block> BlockPlaced;
    public event Action<SpillCrate> CrateSpilled;
    public event Action<SpillCrate> CrateRecovered;
    public event Action<bool> OnlineChanged;

    public BaseModel(BaseData data, GroundPos centre)
    {
        Data = data;
        Centre = centre;
        Grid = BaseGrid.FromLayout(data, centre);
        foreach (Block b in Grid.Blocks.Where(b => b.Kind == BlockKind.Container))
            Loot.Storage.AddContainer(b.Id, b.Capacity);
        foreach (var (item, count) in data.StartingInventory.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            Loot.Storage.Add(item, count);
    }

    /// <summary>Applies damage. Returns true if this hit destroyed the block.</summary>
    public bool Damage(Block b, double amount)
    {
        if (b == null || !b.Alive || amount <= 0) return false;
        bool wasOnline = Online; // before the hit: a core at 0 hp no longer counts as online
        b.Hp = Math.Max(0, b.Hp - amount);
        BlockDamaged?.Invoke(b);
        if (b.Alive) return false;

        Grid.Remove(b);
        if (b.Kind == BlockKind.Container)
        {
            // Loot is never at risk: the container's share of the shared inventory lands in a crate right here.
            SpillCrate crate = Loot.Spill(b.Id, Grid.CellCentre(b.Cell));
            if (crate != null) CrateSpilled?.Invoke(crate);
        }
        BlockDestroyed?.Invoke(b);
        if (wasOnline && !Online) OnlineChanged?.Invoke(false);
        return true;
    }

    /// <summary>
    /// Places a new core in the core's slot when the base is offline. Every block that survived is
    /// still there; the base simply comes back online around them.
    /// </summary>
    public Block PlaceNewCore()
    {
        if (Online) return null;
        Block core = Grid.Place(BlockKind.Core, Grid.CoreSlot);
        if (core == null) return null;
        BlockPlaced?.Invoke(core);
        OnlineChanged?.Invoke(true);
        return core;
    }

    public int RecoverCrate(SpillCrate crate)
    {
        int moved = Loot.Recover(crate);
        if (moved > 0) CrateRecovered?.Invoke(crate);
        return moved;
    }

    public double StoredValue =>
        Loot.Storage.Contents().Sum(kv => kv.Value * (Data.Items.TryGetValue(kv.Key, out ItemSpec spec) ? spec.Value : 1));

    public int TurretsAlive => Grid.Blocks.Count(b => b.Kind == BlockKind.Turret && b.Alive);

    /// <summary>What the invasion scaling looks at.</summary>
    public BaseStats Stats() => new(StoredValue, Grid.AliveCount, TurretsAlive);

    public bool InCoreRoom(GroundPos p) => Grid.CoreRoom.Contains(Grid.CellAt(p));
}
