using System.Collections.Generic;
using System.Linq;

namespace GalHack.Records;

/// <summary>The record types this prototype saves.</summary>
public static class RecordTypes
{
    public const string Player = "player";
    public const string Ship = "ship";
    public const string Structure = "structure";
    public const string Container = "container";
    public const string Drop = "drop";
    public const string Terrain = "terrain";
}

/// <summary>
/// Zone ids. Everything is zoned: system space, planet and moon orbits, surfaces, deep-space
/// sites, and every ship's cabin, which travels with its ship.
/// </summary>
public static class ZoneIds
{
    public const string Surface = "surface/dune";
    public const string Orbit = "orbit/dune";

    public static string Cabin(string shipId) => "cabin/" + shipId;
    public static bool IsCabin(string zoneId) => zoneId.StartsWith("cabin/", System.StringComparison.Ordinal);
    public static string ShipOfCabin(string zoneId) => IsCabin(zoneId) ? zoneId["cabin/".Length..] : null;
}

/// <summary>Item kinds. Plates are what blocks are built from.</summary>
public static class ItemKinds
{
    public const string Ore = "ore", Ice = "ice", Plate = "plate", Fuel = "fuel";
    public static readonly string[] All = { Ore, Ice, Plate, Fuel };
}

public sealed class ItemStack
{
    public string Item { get; set; }
    public int Count { get; set; }
}

public sealed class PlayerData
{
    public string Name { get; set; }
    /// <summary>In the zone, or ship-local when aboard (the cabin zone's coordinates).</summary>
    public double[] Position { get; set; } = new double[3];
    public double Yaw { get; set; }
    public List<ItemStack> Inventory { get; set; } = new();
}

public sealed class ShipData
{
    public string Name { get; set; }
    public double[] Position { get; set; } = new double[3];
    /// <summary>A quaternion: x, y, z, w.</summary>
    public double[] Rotation { get; set; } = { 0, 0, 0, 1 };
}

/// <summary>A container, payload version 2: stacks of items. Version 1 kept one name per item.</summary>
public sealed class ContainerData
{
    public string Label { get; set; }
    public double[] Position { get; set; } = new double[3];
    public double Yaw { get; set; }
    public List<ItemStack> Stacks { get; set; } = new();
}

/// <summary>Items lying loose, dropped by a player or spilled from somewhere.</summary>
public sealed class DropData
{
    public double[] Position { get; set; } = new double[3];
    public List<ItemStack> Stacks { get; set; } = new();
}

/// <summary>
/// What a structure's core is set to. Bases and capital ships share every material and block,
/// and a core can switch between the two at any time.
/// </summary>
public static class CoreModes
{
    public const string Base = "base";
    public const string CapitalShip = "capital-ship";
    public static readonly string[] All = { Base, CapitalShip };
}

/// <summary>A block structure on a 1 m grid. Each block is x, y, z (cells from the origin) and a paint index.</summary>
public sealed class StructureData
{
    public string Name { get; set; }

    /// <summary>
    /// The core's mode: one plain field, so switching is a single write. The record keeps its type
    /// ("structure"), id, zone, blocks and everything else; nothing is copied, converted or moved.
    /// </summary>
    public string Mode { get; set; } = CoreModes.Base;

    public double[] Origin { get; set; } = new double[3];
    public List<int[]> Blocks { get; set; } = new();

    public int IndexOf(int x, int y, int z) => Blocks.FindIndex(b => b[0] == x && b[1] == y && b[2] == z);
}

/// <summary>A zone's terrain edits. Here: scorch marks where ships landed (x, z, radius).</summary>
public sealed class TerrainData
{
    public List<double[]> Scorches { get; set; } = new();
}

public static class Stacks
{
    public static int Total(IEnumerable<ItemStack> stacks) => stacks.Sum(s => s.Count);

    public static int CountOf(IEnumerable<ItemStack> stacks, string item) => stacks.Where(s => s.Item == item).Sum(s => s.Count);

    public static void Add(List<ItemStack> stacks, string item, int count)
    {
        if (count <= 0) return;
        ItemStack stack = stacks.Find(s => s.Item == item);
        if (stack != null) stack.Count += count;
        else stacks.Add(new ItemStack { Item = item, Count = count });
    }

    /// <summary>Takes up to <paramref name="count"/> of an item; returns how many it took.</summary>
    public static int Take(List<ItemStack> stacks, string item, int count)
    {
        ItemStack stack = stacks.Find(s => s.Item == item);
        if (stack == null || count <= 0) return 0;
        int taken = System.Math.Min(count, stack.Count);
        stack.Count -= taken;
        if (stack.Count == 0) stacks.Remove(stack);
        return taken;
    }

    public static string Describe(IEnumerable<ItemStack> stacks) =>
        stacks.Any() ? string.Join(", ", stacks.Select(s => $"{s.Item} {s.Count}")) : "empty";
}
