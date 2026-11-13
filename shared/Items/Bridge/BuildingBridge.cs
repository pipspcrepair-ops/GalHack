using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using GalHack.Structures;

namespace GalHack.Items.Bridge;

/// <summary>
/// The adapter between building's structure model (prototypes/building/src/Structures) and the
/// item catalogue. Building keeps its own files: its catalogue is loaded from where it lives, with
/// any extra block files appended, and every block is priced through shared/Items/data/blocks.json
/// by its id. Pure translation, no rules.
/// </summary>
public static class BuildingBridge
{
    /// <summary>
    /// Loads building's catalogue from its data folder, appending extra component files (the
    /// production machines and guns in building-parts.json, the sandbox's vessel parts) and extra
    /// material files (precursor armour). Nothing in building's folder is copied or changed.
    /// </summary>
    public static Catalogue LoadCatalogue(string buildingData, IEnumerable<string> extraComponents = null, IEnumerable<string> extraMaterials = null)
    {
        var components = (extraComponents ?? Array.Empty<string>()).ToList();
        var materials = (extraMaterials ?? Array.Empty<string>()).ToList();
        return Catalogue.Load(name =>
        {
            string text = File.ReadAllText(Path.Combine(buildingData, name));
            List<string> extra = name == Catalogue.ComponentsFile ? components : name == Catalogue.MaterialsFile ? materials : null;
            if (extra == null || extra.Count == 0) return text;
            string key = name == Catalogue.ComponentsFile ? "components" : "materials";
            JsonNode root = JsonNode.Parse(text, documentOptions: DataJson.Options);
            JsonArray list = root[key].AsArray();
            foreach (string file in extra)
                foreach (JsonNode c in JsonNode.Parse(File.ReadAllText(file), documentOptions: DataJson.Options)[key].AsArray().ToList())
                    list.Add(c.DeepClone());
            return root.ToJsonString();
        });
    }

    /// <summary>The block id a type is priced and carried as: a variant family's head (every door is a door).</summary>
    public static string ItemBlockOf(BlockType t) => (t.Head ?? t).Id;

    /// <summary>A structure's blocks as a build list, by item: a door family counts its doors' units.</summary>
    public static List<BlockCount> BlocksOf(Structure s) =>
        s.Blocks().GroupBy(x => ItemBlockOf(s.TypeOf(x.block)))
            .Select(g => new BlockCount(g.Key, g.Sum(x => s.TypeOf(x.block).Units)))
            .OrderBy(b => b.BlockId, StringComparer.Ordinal).ToList();

    /// <summary>The block item a placed block is made from (any shape of an armour block, any size of a door), or null.</summary>
    public static ItemDef ItemOf(ItemCatalogue items, Structure s, in Block b) => items.BlockItem(ItemBlockOf(s.TypeOf(b)));

    /// <summary>What placing a block uses, and taking it down gives back: one of its item, or a door's units of the door item.</summary>
    public static Bill ItemsOf(ItemCatalogue items, Structure s, in Block b) =>
        ItemOf(items, s, b) is ItemDef item ? Bill.Of((item.Id, s.TypeOf(b).Units)) : new Bill();

    /// <summary>
    /// Links the block items to building's blocks: named after them ("Iron plated block", "Thruster
    /// T1"), and each bill told how many cells its block fills (for its assembly time and XP).
    /// </summary>
    public static void NameBlockItems(ItemCatalogue items, Catalogue blocks)
    {
        items.NameBlockItems(id => blocks.TryGetType(id, out BlockType t) ? (t.IsArmor ? $"{t.Name} block" : t.Name) : null);
        foreach (BlockBill bill in items.Blocks.All)
            if (bill.Family != BlockFamily.Deco && blocks.TryGetType(bill.BlockId, out BlockType t)) bill.Cells = t.Cells;
    }

    /// <summary>The bill of one placed block. Every shape of an armour block shares its cube's bill; a door is its units of the door's.</summary>
    public static Bill BillOf(ItemCatalogue items, Structure s, in Block b)
    {
        BlockType t = s.TypeOf(b);
        Bill one = items.Blocks.For(ItemBlockOf(t));
        return t.Units == 1 ? one : new Bill().Add(one, t.Units);
    }

    /// <summary>Seconds the blueprint factory takes to fit one placed block, and a deconstructor to take it apart.</summary>
    public static double AssemblySeconds(ItemCatalogue items, Structure s, in Block b) =>
        items.Blocks.TryGet(ItemBlockOf(s.TypeOf(b)), out BlockBill bill) ? bill.AssemblySeconds * s.TypeOf(b).Units : 0.4;

    /// <summary>True if the block has a bill (creative can place anything; survival only what it can price).</summary>
    public static bool Priced(ItemCatalogue items, Structure s, in Block b) => items.Blocks.TryGet(ItemBlockOf(s.TypeOf(b)), out _);

    /// <summary>The ammo the structure's guns fire, in a stable order: what its constructors restock.</summary>
    public static List<string> AmmoInUse(ItemCatalogue items, Structure s) =>
        s.Blocks().Select(x => items.Blocks.AmmoFor(s.TypeOf(x.block).Id)).Where(a => a != null)
            .Distinct().OrderBy(a => a, StringComparer.Ordinal).ToList();

    /// <summary>The structure's shared storage capacity: every container's, capped.</summary>
    public static double StorageCapacity(Structure s) => StructureStats.Compute(s).StorageCapacity;

    /// <summary>Production machines on a structure, with where they sit.</summary>
    public static IEnumerable<(Int3 Cell, MachineDef Machine)> MachinesOn(ItemCatalogue items, Structure s)
    {
        foreach (var (p, b) in s.Blocks())
        {
            MachineDef m = items.Machines.ForBlock(s.TypeOf(b).Id);
            if (m != null) yield return (p, m);
        }
    }
}
