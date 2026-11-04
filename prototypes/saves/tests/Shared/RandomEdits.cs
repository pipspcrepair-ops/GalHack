using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Persistence;
using GalHack.Records;

namespace GalHack.Tests;

/// <summary>
/// Random player edits that never create or destroy anything: items move between crates,
/// players and loose stacks, crates move between zones and the ship's hold, blocks move between
/// structures, the ship crosses between surface and orbit, the player steps on and off. So the
/// item and block totals must stay exactly the same forever. Shared by the soak test and the
/// kill-test worker, so both hammer the same code the game uses.
/// </summary>
public static class RandomEdits
{
    public enum Kind { MoveItems, Drop, PickUp, MoveContainer, MoveBlock, CrossZone, StepAboard, SwitchCoreMode }

    public static Kind Apply(RecordTable t, Random rng, ref int nextId)
    {
        Kind kind = (Kind)rng.Next(8);
        switch (kind)
        {
            case Kind.MoveItems:
            {
                List<SaveRecord> holders = t.All.Where(WorldOps.HoldsItems).ToList();
                List<SaveRecord> full = holders.Where(h => h.Items > 0).ToList();
                if (full.Count == 0) break;
                SaveRecord from = Pick(full, rng), to = Pick(holders, rng);
                ItemStack stack = Pick(WorldOps.StacksOf(from), rng);
                WorldOps.MoveItems(t, from.Id, to.Id, stack.Item, 1 + rng.Next(10));
                break;
            }
            case Kind.Drop:
            {
                List<SaveRecord> full = t.All.Where(r => r.Type is RecordTypes.Container or RecordTypes.Player && r.Items > 0).ToList();
                if (full.Count == 0) break;
                SaveRecord from = Pick(full, rng);
                ItemStack stack = Pick(WorldOps.StacksOf(from), rng);
                string id;
                do id = $"drop-r{nextId++}"; while (t.Contains(id));
                WorldOps.Drop(t, from.Id, stack.Item, 1 + rng.Next(5), id, from.ZoneId, from.ShipId, RandomSpot(rng));
                break;
            }
            case Kind.PickUp:
            {
                List<SaveRecord> drops = t.All.Where(r => r.Type == RecordTypes.Drop).ToList();
                if (drops.Count == 0) break;
                WorldOps.PickUp(t, Pick(drops, rng).Id, StarterWorld.PlayerId);
                break;
            }
            case Kind.MoveContainer:
            {
                List<SaveRecord> crates = t.All.Where(r => r.Type == RecordTypes.Container).ToList();
                SaveRecord crate = Pick(crates, rng);
                var (zone, ship) = RandomPlace(t, rng);
                WorldOps.Place(t, crate.Id, zone, ship, RandomSpot(rng), yaw: Math.Round(rng.NextDouble() * 6, 3));
                break;
            }
            case Kind.MoveBlock:
            {
                List<SaveRecord> structures = t.All.Where(r => r.Type == RecordTypes.Structure).ToList();
                List<SaveRecord> built = structures.Where(s => s.Blocks > 0).ToList();
                if (built.Count == 0) break;
                SaveRecord from = Pick(built, rng), to = Pick(structures, rng);
                int[] cell = { rng.Next(-8, 8), rng.Next(0, 6), rng.Next(-8, 8) };
                WorldOps.MoveBlock(t, from.Id, rng.Next(from.Blocks), to.Id, cell); // false if the cell is taken: fine
                break;
            }
            case Kind.CrossZone:
            {
                SaveRecord ship = t.Get(StarterWorld.ShipId);
                string to = ship.ZoneId == ZoneIds.Surface ? ZoneIds.Orbit : ZoneIds.Surface;
                WorldOps.Place(t, ship.Id, to, null, RandomSpot(rng), rotation: new double[] { 0, 0, 0, 1 });
                break;
            }
            case Kind.StepAboard:
            {
                SaveRecord player = t.Get(StarterWorld.PlayerId);
                SaveRecord ship = t.Get(StarterWorld.ShipId);
                if (player.ShipId == null) WorldOps.Place(t, player.Id, ZoneIds.Cabin(ship.Id), ship.Id, RandomSpot(rng));
                else WorldOps.Place(t, player.Id, ship.ZoneId, null, RandomSpot(rng));
                break;
            }
            case Kind.SwitchCoreMode:
            {
                SaveRecord structure = Pick(t.All.Where(r => r.Type == RecordTypes.Structure).ToList(), rng);
                string mode = GameRecords.Read<StructureData>(structure).Mode == CoreModes.Base ? CoreModes.CapitalShip : CoreModes.Base;
                WorldOps.SwitchCoreMode(t, structure.Id, mode);
                break;
            }
        }
        return kind;
    }

    /// <summary>The surface, orbit, or the ship's hold.</summary>
    static (string Zone, string Ship) RandomPlace(RecordTable t, Random rng) => rng.Next(3) switch
    {
        0 => (ZoneIds.Surface, null),
        1 => (ZoneIds.Orbit, null),
        _ => (ZoneIds.Cabin(StarterWorld.ShipId), StarterWorld.ShipId),
    };

    static double[] RandomSpot(Random rng) =>
        new[] { Math.Round(rng.NextDouble() * 40 - 20, 3), 0, Math.Round(rng.NextDouble() * 40 - 20, 3) };

    static T Pick<T>(IReadOnlyList<T> list, Random rng) => list[rng.Next(list.Count)];
}

/// <summary>What must be true of the records after anything at all: crash, kill, reload, restore.</summary>
public static class Invariants
{
    /// <summary>Every rule broken, in words. Empty if all is well.</summary>
    public static List<string> Check(IEnumerable<SaveRecord> records, int items, int blocks)
    {
        var problems = new List<string>();
        var all = records.ToDictionary(r => r.Id, StringComparer.Ordinal);
        var counts = AuditCounts.Of(all.Values);
        if (counts.Items != items) problems.Add($"items: {counts.Items}, expected {items}");
        if (counts.Blocks != blocks) problems.Add($"blocks: {counts.Blocks}, expected {blocks}");

        foreach (SaveRecord r in all.Values)
        {
            // Exactly one place: the zone field, and a cabin zone only together with its ship.
            string cabinShip = ZoneIds.ShipOfCabin(r.ZoneId);
            if (cabinShip != r.ShipId) problems.Add($"{r.Id} is in zone {r.ZoneId} but aboard {r.ShipId ?? "nothing"}");
            if (r.ShipId != null && (!all.TryGetValue(r.ShipId, out SaveRecord ship) || ship.Type != RecordTypes.Ship))
                problems.Add($"{r.Id} is aboard {r.ShipId}, which is not a ship here");
            if (r.Type == RecordTypes.Ship && ZoneIds.IsCabin(r.ZoneId)) problems.Add($"ship {r.Id} is inside a cabin");
            if (r.Type == RecordTypes.Drop && r.Items == 0) problems.Add($"{r.Id} is an empty drop");
        }
        return problems;
    }
}
