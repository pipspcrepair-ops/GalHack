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

    public static bool IsSmall(Structure s) => s.Grid.Size == GridSize.Small;

    /// <summary>A structure's blocks as a build list (by block id and grid).</summary>
    public static List<BlockCount> BlocksOf(Structure s) =>
        s.Blocks().GroupBy(x => s.TypeOf(x.block).Id)
            .Select(g => new BlockCount(g.Key, IsSmall(s), g.Count()))
            .OrderBy(b => b.BlockId, StringComparer.Ordinal).ToList();

    /// <summary>The bill of one placed block. Every shape of a material shares its cube's bill.</summary>
    public static Bill BillOf(ItemCatalogue items, Structure s, in Block b) => items.Blocks.For(s.TypeOf(b).Id, IsSmall(s));

    /// <summary>True if the block has a bill (creative can place anything; survival only what it can price).</summary>
    public static bool Priced(ItemCatalogue items, Structure s, in Block b) => items.Blocks.TryGet(s.TypeOf(b).Id, out _);

    /// <summary>The ammo the structure's guns fire, in a stable order: what its constructors restock.</summary>
    public static List<string> AmmoInUse(ItemCatalogue items, Structure s) =>
        s.Blocks().Select(x => items.Blocks.AmmoFor(s.TypeOf(x.block).Id)).Where(a => a != null)
            .Distinct().OrderBy(a => a, StringComparer.Ordinal).ToList();

    /// <summary>The structure's shared storage capacity: every container's, capped per grid.</summary>
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
