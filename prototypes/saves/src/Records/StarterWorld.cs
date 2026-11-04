using System;
using System.Collections.Generic;
using GalHack.Persistence;

namespace GalHack.Records;

/// <summary>
/// The world a new save starts with: a small base on the surface with crates and loose stacks
/// around it, a ship parked beside it with a crate and a shelf in its cabin and the player
/// standing aboard, and a derelict crate nobody has touched drifting in orbit.
/// </summary>
public static class StarterWorld
{
    public const string Owner = "p1";
    public const string PlayerId = "player-p1";
    public const string ShipId = "ship-1";
    public const string BaseId = "base-1";
    public const string ShelfId = "shelf-1";
    public const string CrateA = "crate-a", CrateB = "crate-b", CrateC = "crate-c";
    public const string DropOre = "drop-ore", DropPlate = "drop-plate";
    public const string Wreck = "wreck-1";
    public const string SurfaceTerrain = "terrain-surface";

    /// <summary>Where the player stands aboard on a new game (ship-local metres).</summary>
    public static readonly double[] SpawnAboard = { 0, 0.05, 4 };

    /// <summary>Paints for blocks, indexed by a block's fourth number.</summary>
    public const int Cream = 0, Orange = 1, Teal = 2, Red = 3;

    public static void Create(RecordTable t, int extraContainers = 0, int seed = 1)
    {
        AuditCounts before = AuditCounts.Of(t.All);
        Populate(t, extraContainers, seed);
        // Generating a world creates its items and blocks, so say so: the table's ledger refuses
        // to save anything that appears (or vanishes) without being declared.
        AuditCounts after = AuditCounts.Of(t.All);
        t.Declare(after.Items - before.Items, after.Blocks - before.Blocks);
    }

    static void Populate(RecordTable t, int extraContainers, int seed)
    {
        string cabin = ZoneIds.Cabin(ShipId);

        t.Put(GameRecords.Make(PlayerId, RecordTypes.Player, cabin, ShipId, Owner, new PlayerData
        {
            Name = "Pilot",
            Position = (double[])SpawnAboard.Clone(),
            Inventory = { Stack(ItemKinds.Plate, 20), Stack(ItemKinds.Ore, 10) },
        }));

        t.Put(GameRecords.Make(ShipId, RecordTypes.Ship, ZoneIds.Surface, null, Owner, new ShipData
        {
            Name = "Kestrel",
            Position = new[] { 0, 0.85, 0 },
        }));

        t.Put(GameRecords.Make(BaseId, RecordTypes.Structure, ZoneIds.Surface, null, Owner, new StructureData
        {
            Name = "Base",
            Mode = CoreModes.Base,
            Origin = new double[] { -3, 0, 24 },
            Blocks = HutBlocks(),
        }));

        // A shelf of cargo blocks in the ship's cabin: a structure aboard, which rides with the ship.
        t.Put(GameRecords.Make(ShelfId, RecordTypes.Structure, cabin, ShipId, Owner, new StructureData
        {
            Name = "Cargo shelf",
            Origin = new[] { -4.5, 0, 8 },
            Blocks = { new[] { 0, 0, 0, Teal }, new[] { 0, 0, 1, Teal }, new[] { 0, 1, 0, Orange }, new[] { 0, 1, 1, Teal } },
        }));

        t.Put(Crate(CrateA, ZoneIds.Surface, null, "Base stores", new[] { 0, 0, 26.5 }, Stack(ItemKinds.Ore, 30), Stack(ItemKinds.Ice, 12)));
        t.Put(Crate(CrateB, ZoneIds.Surface, null, "Supply crate", new[] { 4.5, 0, 18 }, Stack(ItemKinds.Fuel, 6), Stack(ItemKinds.Plate, 10)));
        t.Put(Crate(CrateC, cabin, ShipId, "Ship's hold", new[] { -2.2, 0, 7.5 }, Stack(ItemKinds.Fuel, 8), Stack(ItemKinds.Ice, 4)));

        t.Put(GameRecords.Make(DropOre, RecordTypes.Drop, ZoneIds.Surface, null, Owner,
            new DropData { Position = new[] { -2.5, 0, 19 }, Stacks = { Stack(ItemKinds.Ore, 5) } }, touched: true));
        t.Put(GameRecords.Make(DropPlate, RecordTypes.Drop, ZoneIds.Surface, null, Owner,
            new DropData { Position = new[] { 2, 0, 20.5 }, Stacks = { Stack(ItemKinds.Plate, 4) } }, touched: true));

        // World-generated and untouched: the only kind of thing a cleanup may ever remove.
        t.Put(GameRecords.Make(Wreck, RecordTypes.Container, ZoneIds.Orbit, null, null, new ContainerData
        {
            Label = "Derelict pod",
            Position = new double[] { 26, 612, -48 },
            Stacks = { Stack(ItemKinds.Ice, 7) },
        }));

        t.Put(GameRecords.Make(SurfaceTerrain, RecordTypes.Terrain, ZoneIds.Surface, null, null,
            new TerrainData { Scorches = { new[] { 0.0, 0, 9 } } }, touched: true));

        // More crates for the stress tests, spread around the base.
        var rng = new Random(seed);
        for (int i = 0; i < extraContainers; i++)
        {
            t.Put(Crate($"crate-x{i}", ZoneIds.Surface, null, $"Crate {i}",
                new[] { Math.Round(rng.NextDouble() * 40 - 20, 2), 0, Math.Round(30 + rng.NextDouble() * 20, 2) },
                Stack(ItemKinds.All[rng.Next(4)], 1 + rng.Next(30)), Stack(ItemKinds.All[rng.Next(4)], 1 + rng.Next(30))));
        }
    }

    public static ItemStack Stack(string item, int count) => new() { Item = item, Count = count };

    static SaveRecord Crate(string id, string zoneId, string shipId, string label, double[] position, params ItemStack[] stacks)
    {
        var data = new ContainerData { Label = label, Position = position };
        foreach (ItemStack s in stacks) Stacks.Add(data.Stacks, s.Item, s.Count);
        return GameRecords.Make(id, RecordTypes.Container, zoneId, shipId, Owner, data);
    }

    /// <summary>A 6 by 5 hut, two blocks high, open towards the ship, with beacons on its corners.</summary>
    static List<int[]> HutBlocks()
    {
        var blocks = new List<int[]>();
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 6; x++)
                for (int z = 0; z < 5; z++)
                {
                    bool wall = x == 0 || x == 5 || z == 0 || z == 4;
                    bool doorway = z == 0 && x is 2 or 3;
                    if (wall && !doorway) blocks.Add(new[] { x, y, z, y == 0 ? Cream : Orange });
                }
        foreach (var (x, z) in new[] { (0, 0), (5, 0), (0, 4), (5, 4) })
            blocks.Add(new[] { x, 2, z, Teal });
        return blocks;
    }
}
