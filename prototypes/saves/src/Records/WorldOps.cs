using System;
using System.Collections.Generic;
using GalHack.Persistence;

namespace GalHack.Records;

/// <summary>
/// Everything a player can do to saved things, as edits to the record table. Each one changes
/// all the records it touches together, so the next commit saves all of it or none of it: items
/// moved between two containers are in one or the other, never both, never neither.
/// The game, the soak test and the kill-test worker all use these same functions.
/// </summary>
public static class WorldOps
{
    /// <summary>The items a record holds, or null if it can't hold any.</summary>
    public static List<ItemStack> StacksOf(SaveRecord r) => r.Type switch
    {
        RecordTypes.Container => GameRecords.Read<ContainerData>(r).Stacks,
        RecordTypes.Player => GameRecords.Read<PlayerData>(r).Inventory,
        RecordTypes.Drop => GameRecords.Read<DropData>(r).Stacks,
        _ => null,
    };

    public static bool HoldsItems(SaveRecord r) => r.Type is RecordTypes.Container or RecordTypes.Player or RecordTypes.Drop;

    /// <summary>Moves up to <paramref name="count"/> of an item from one holder to another. Returns how many moved.</summary>
    public static int MoveItems(RecordTable t, string fromId, string toId, string item, int count)
    {
        if (fromId == toId) return 0;
        SaveRecord from = t.Get(fromId), to = t.Get(toId);
        int moved = 0;
        SaveRecord emptier = EditStacks(from, s => moved = Stacks.Take(s, item, count));
        if (moved == 0) return 0;
        t.Put(emptier with { Touched = true });
        t.Put(EditStacks(to, s => Stacks.Add(s, item, moved)) with { Touched = true });
        RemoveIfEmptyDrop(t, fromId);
        return moved;
    }

    /// <summary>
    /// A loose stack with nothing left in it is gone, in the same commit that emptied it. Only
    /// drops: an empty crate is still a crate.
    /// </summary>
    static void RemoveIfEmptyDrop(RecordTable t, string id)
    {
        if (t.TryGet(id, out SaveRecord r) && r.Type == RecordTypes.Drop && r.Items == 0) t.Remove(id);
    }

    /// <summary>Drops items from a holder as a new loose stack. Returns how many were dropped.</summary>
    public static int Drop(RecordTable t, string fromId, string item, int count, string dropId, string zoneId, string shipId, double[] position)
    {
        // A reused id would replace a record that exists: never let a new thing overwrite an old one.
        if (t.Contains(dropId)) throw new InvalidOperationException($"there is already a record {dropId}");
        SaveRecord from = t.Get(fromId);
        int taken = 0;
        SaveRecord emptier = EditStacks(from, s => taken = Stacks.Take(s, item, count));
        if (taken == 0) return 0;
        var drop = new DropData { Position = position, Stacks = { new ItemStack { Item = item, Count = taken } } };
        t.Put(emptier with { Touched = true });
        t.Put(GameRecords.Make(dropId, RecordTypes.Drop, zoneId, shipId, from.Owner, drop, touched: true));
        RemoveIfEmptyDrop(t, fromId);
        return taken;
    }

    /// <summary>Picks up a loose stack: all of it goes into the player's inventory and the empty drop is removed.</summary>
    public static int PickUp(RecordTable t, string dropId, string playerId)
    {
        SaveRecord drop = t.Get(dropId), player = t.Get(playerId);
        List<ItemStack> loose = GameRecords.Read<DropData>(drop).Stacks;
        t.Put(EditStacks(player, s => { foreach (ItemStack st in loose) Stacks.Add(s, st.Item, st.Count); }) with { Touched = true });
        t.Put(EditStacks(drop, s => s.Clear()) with { Touched = true });
        t.Remove(dropId); // empty now, so the table allows it
        return Stacks.Total(loose);
    }

    /// <summary>
    /// Puts a thing somewhere: a new zone (and ship, if aboard) and a position there. For a zone
    /// crossing this is the one write that matters: the record's single zone field changes.
    /// </summary>
    public static void Place(RecordTable t, string id, string zoneId, string shipId, double[] position, double? yaw = null, double[] rotation = null)
    {
        SaveRecord r = t.Get(id);
        object data;
        switch (r.Type)
        {
            case RecordTypes.Player:
                var p = GameRecords.Read<PlayerData>(r);
                p.Position = position;
                if (yaw is double py) p.Yaw = py;
                data = p;
                break;
            case RecordTypes.Ship:
                var s = GameRecords.Read<ShipData>(r);
                s.Position = position;
                if (rotation != null) s.Rotation = rotation;
                data = s;
                break;
            case RecordTypes.Container:
                var c = GameRecords.Read<ContainerData>(r);
                c.Position = position;
                if (yaw is double cy) c.Yaw = cy;
                data = c;
                break;
            case RecordTypes.Drop:
                var d = GameRecords.Read<DropData>(r);
                d.Position = position;
                data = d;
                break;
            default:
                throw new InvalidOperationException($"{id} is a {r.Type}, which can't be moved");
        }
        t.Put(GameRecords.With(r, data) with { ZoneId = zoneId, ShipId = shipId, Touched = true });
    }

    /// <summary>Moves one block, within a structure or to another one. False if the target cell is taken.</summary>
    public static bool MoveBlock(RecordTable t, string fromId, int index, string toId, int[] cell)
    {
        SaveRecord from = t.Get(fromId);
        var fd = GameRecords.Read<StructureData>(from);
        if (index < 0 || index >= fd.Blocks.Count) return false;
        int paint = fd.Blocks[index][3];
        if (fromId == toId)
        {
            if (fd.IndexOf(cell[0], cell[1], cell[2]) >= 0) return false;
            fd.Blocks[index] = new[] { cell[0], cell[1], cell[2], paint };
            t.Put(GameRecords.With(from, fd) with { Touched = true });
            return true;
        }
        SaveRecord to = t.Get(toId);
        var td = GameRecords.Read<StructureData>(to);
        if (td.IndexOf(cell[0], cell[1], cell[2]) >= 0) return false;
        fd.Blocks.RemoveAt(index);
        td.Blocks.Add(new[] { cell[0], cell[1], cell[2], paint });
        t.Put(GameRecords.With(from, fd) with { Touched = true });
        t.Put(GameRecords.With(to, td) with { Touched = true });
        return true;
    }

    /// <summary>Builds a block out of one hull plate from the player's inventory. False if the cell is taken or there is no plate.</summary>
    public static bool PlaceBlock(RecordTable t, string structureId, int[] cell, int paint, string playerId)
    {
        SaveRecord structure = t.Get(structureId);
        var sd = GameRecords.Read<StructureData>(structure);
        if (sd.IndexOf(cell[0], cell[1], cell[2]) >= 0) return false;
        int taken = 0;
        SaveRecord player = EditStacks(t.Get(playerId), s => taken = Stacks.Take(s, ItemKinds.Plate, 1));
        if (taken == 0) return false;
        sd.Blocks.Add(new[] { cell[0], cell[1], cell[2], paint });
        t.Put(player with { Touched = true });
        t.Put(GameRecords.With(structure, sd) with { Touched = true });
        t.Declare(items: -1, blocks: +1); // a plate became a block, on purpose
        return true;
    }

    /// <summary>Takes a block apart: it goes back into the player's inventory as a hull plate.</summary>
    public static bool RemoveBlock(RecordTable t, string structureId, int index, string playerId)
    {
        SaveRecord structure = t.Get(structureId);
        var sd = GameRecords.Read<StructureData>(structure);
        if (index < 0 || index >= sd.Blocks.Count) return false;
        sd.Blocks.RemoveAt(index);
        t.Put(GameRecords.With(structure, sd) with { Touched = true });
        t.Put(EditStacks(t.Get(playerId), s => Stacks.Add(s, ItemKinds.Plate, 1)) with { Touched = true });
        t.Declare(items: +1, blocks: -1);
        return true;
    }

    /// <summary>
    /// Switches a structure's core between base and capital-ship mode. Only the mode field changes:
    /// the same record, id, zone and blocks, and every crate on or in it is its own record and is
    /// not touched, so a switch can't drop or move a single block or item. False if already in that mode.
    /// </summary>
    public static bool SwitchCoreMode(RecordTable t, string structureId, string mode)
    {
        if (Array.IndexOf(CoreModes.All, mode) < 0) throw new ArgumentException($"unknown core mode '{mode}'");
        SaveRecord r = t.Get(structureId);
        if (r.Type != RecordTypes.Structure) throw new InvalidOperationException($"{structureId} is a {r.Type}, not a structure with a core");
        var data = GameRecords.Read<StructureData>(r);
        if (data.Mode == mode) return false;
        data.Mode = mode;
        t.Put(GameRecords.With(r, data) with { Touched = true });
        return true;
    }

    /// <summary>Adds a scorch mark to a zone's terrain record, creating the record if it has none yet.</summary>
    public static void Scorch(RecordTable t, string terrainId, string zoneId, double x, double z, double radius)
    {
        TerrainData data = t.TryGet(terrainId, out SaveRecord r) ? GameRecords.Read<TerrainData>(r) : new TerrainData();
        data.Scorches.Add(new[] { Math.Round(x, 2), Math.Round(z, 2), radius });
        t.Put(r == null
            ? GameRecords.Make(terrainId, RecordTypes.Terrain, zoneId, null, null, data, touched: true)
            : GameRecords.With(r, data) with { Touched = true });
    }

    static SaveRecord EditStacks(SaveRecord r, Action<List<ItemStack>> edit)
    {
        switch (r.Type)
        {
            case RecordTypes.Container:
                var c = GameRecords.Read<ContainerData>(r);
                edit(c.Stacks);
                return GameRecords.With(r, c);
            case RecordTypes.Player:
                var p = GameRecords.Read<PlayerData>(r);
                edit(p.Inventory);
                return GameRecords.With(r, p);
            case RecordTypes.Drop:
                var d = GameRecords.Read<DropData>(r);
                edit(d.Stacks);
                return GameRecords.With(r, d);
            default:
                throw new InvalidOperationException($"{r.Id} is a {r.Type}, which can't hold items");
        }
    }
}
