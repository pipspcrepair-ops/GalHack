using System.Collections.Generic;
using System.Linq;

namespace GalHack.Items;

/// <summary>
/// The design rules the catalogue is held to, beyond what loading already refuses. Each broken
/// rule is one readable line; an empty list means the data is sound.
/// </summary>
public static class Validation
{
    public static List<string> Validate(this ItemCatalogue c)
    {
        var problems = new List<string>();
        int topTier = c.Machines.Constructors.Max(m => m.Tier);

        foreach (ItemDef item in c.Items)
        {
            bool found = item.Category == ItemCategory.Unique;
            if (item.IsRaw || found)
            {
                if (item.Recipe != null) problems.Add($"{item.Id}: a {item.Category} item can't have a recipe");
                if (found && item.Source != ItemSource.Found) problems.Add($"{item.Id}: unique finds come from exploring, never from mining");
                if (found && item.Rarity != Rarity.Unique) problems.Add($"{item.Id}: a unique find must be of unique rarity");
                if (item.IsRaw && !item.IsGathered) problems.Add($"{item.Id}: raw items are mined, dug or harvested");
                if (item.IsRaw && item.Rarity == Rarity.Unique) problems.Add($"{item.Id}: unique rarity can't be mined");
                continue;
            }
            if (item.Recipe == null)
            {
                problems.Add($"{item.Id}: a {item.Category} item needs a recipe");
                continue;
            }
            if (item.Source != ItemSource.Crafted) problems.Add($"{item.Id}: a crafted item has no source");

            Recipe r = item.Recipe;
            if (r.Tier < 0 || r.Tier > topTier) problems.Add($"{item.Id}: tier {r.Tier} has no constructor");
            foreach (var (input, _) in r.Inputs)
            {
                if (input.Rarity > item.Rarity)
                    problems.Add($"{item.Id}: {Rarities.Name(item.Rarity)} but made from {Rarities.Name(input.Rarity)} {input.Id}");
                if (item.Category == ItemCategory.Refined && !input.IsRaw)
                    problems.Add($"{item.Id}: refined materials are made from raw ore only, not {input.Id}");
                if (item.Category is ItemCategory.Part or ItemCategory.Component && input.IsRaw)
                    problems.Add($"{item.Id}: parts and components take refined materials, not raw {input.Id}");
            }
            if (item.IsAmmo)
            {
                if (r.Count != 1) problems.Add($"{item.Id}: ammo recipes make one box a run, so a restock lands on its threshold");
                if (item.Rounds <= 0) problems.Add($"{item.Id}: ammo needs a round count");
            }
        }

        foreach (BlockBill b in c.Blocks.All)
            foreach (bool small in new[] { true, false })
            {
                if (!b.On(small)) continue;
                Bill bill = b.For(small);
                string grid = small ? "small" : "large";
                if (bill.IsEmpty) { problems.Add($"block {b.BlockId}: empty {grid} bill"); continue; }
                bool needsUnique = bill.Ids.Any(id => !c.Graph.FromGathered(c.Item(id), out _));
                if (b.UniqueOnly && !needsUnique) problems.Add($"block {b.BlockId}: marked unique-only but its {grid} bill needs no unique find");
                if (!b.UniqueOnly && needsUnique) problems.Add($"block {b.BlockId}: its {grid} bill needs a unique find but isn't marked unique-only");
                // Deco may use raw harvested and dug goods (wood and fabric from biomass, sandbags of stone).
                if (b.Family != BlockFamily.Deco)
                    foreach (string id in bill.Ids)
                        if (c.Item(id).IsRaw) problems.Add($"block {b.BlockId}: blocks take refined materials and parts, not raw {id}");
            }

        // Blocks are craftable items (Empyrion's way): every block family on every grid it goes on
        // is an item with a recipe, a stack, a mass, a volume and a tech-tree node.
        foreach (BlockBill b in c.Blocks.All)
            foreach (bool small in new[] { true, false })
            {
                if (!b.On(small)) continue;
                string grid = small ? "small" : "large";
                ItemDef item = b.ItemFor(small);
                if (item == null) { problems.Add($"block {b.BlockId}: no {grid} block item"); continue; }
                if (item.Recipe == null) problems.Add($"{item.Id}: a block item needs a recipe");
                else if (!item.Recipe.InputsFor(1).Equals(b.For(small))) problems.Add($"{item.Id}: its recipe isn't its block's bill");
                if (item.StackSize <= 0) problems.Add($"{item.Id}: no stack size");
                if (item.MassKg <= 0 || item.VolumeSu <= 0) problems.Add($"{item.Id}: no mass or volume");
                if (item.Unlock == null) problems.Add($"{item.Id}: no tech-tree node");
                if (item.Places.Count == 0) problems.Add($"{item.Id}: it goes on no class of structure");
                foreach (StructureClass sc in item.Places)
                    if (StructureClasses.IsSmallGrid(sc) != small) problems.Add($"{item.Id}: a {grid}-grid block on a {StructureClasses.Name(sc)}");
            }
        // The foothold base's blocks come out of the carried survival constructor.
        foreach (BlockCount f in c.Machines.Reference.Foothold)
            if (c.Blocks.ItemFor(f.BlockId, f.Small) is ItemDef fi && fi.Recipe.Tier > 0)
                problems.Add($"{fi.Id}: the foothold needs it, but only a {c.Machines.TierName(fi.Recipe.Tier)} constructor makes it");

        // The ladder climbs itself: each machine is built from recipes a lower tier already knows.
        foreach (MachineDef m in c.Machines.All)
        {
            int limit = m.Kind == MachineKind.Constructor ? m.Tier - 1 : topTier - 1;
            if (m.Kind == MachineKind.Constructor && m.Tier == 0) limit = 0;
            Bill bill = m.ItemId != null ? c.Item(m.ItemId).Recipe?.InputsFor(1) ?? new Bill()
                : c.Blocks.For(m.BlockId, smallGrid: false).Add(c.Blocks.For(m.BlockId, smallGrid: true));
            foreach (string id in bill.Ids)
            {
                int need = c.Graph.TierNeeded(c.Item(id));
                if (need > limit) problems.Add($"{m.Id}: needs {id}, which takes a {c.Machines.TierName(need)} constructor");
            }
        }
        return problems;
    }
}
