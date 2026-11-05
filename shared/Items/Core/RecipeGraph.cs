using System;
using System.Collections.Generic;
using System.Linq;

namespace GalHack.Items;

/// <summary>
/// Expanding a bill down the tree: how many runs of which recipe, in build order, and what is
/// left over. Runs are whole, so making 2 copper wire runs the 4-wire recipe once and leaves 2
/// as a by-product; by-products always go back to storage, never away.
/// </summary>
public sealed class Expansion
{
    /// <summary>What the expansion bottoms out in: raw ore, unique finds, or whatever the leaf rule stopped at.</summary>
    public Bill Leaves { get; } = new();
    /// <summary>Recipes to run, inputs before the things made from them.</summary>
    public List<(Recipe Recipe, int Runs)> Steps { get; } = new();
    /// <summary>Whole-run rounding surplus: made but not needed.</summary>
    public Bill Byproducts { get; } = new();
    /// <summary>Recipe seconds of every step on a speed-1 machine.</summary>
    public double Seconds { get; internal set; }
    /// <summary>The highest recipe tier any step needs.</summary>
    public int MaxTier { get; internal set; }
    public int Runs => Steps.Sum(s => s.Runs);
}

/// <summary>
/// The recipe tree as a graph: each item's level (raw is 0, everything else one above its
/// deepest input), the build order, cycle detection, and expanding a bill into ore.
/// </summary>
public sealed class RecipeGraph
{
    readonly ItemCatalogue _c;
    readonly int[] _level;
    readonly List<ItemDef> _buildOrder;
    readonly Dictionary<string, IReadOnlyDictionary<string, double>> _rawCost = new();
    readonly Dictionary<string, int> _tierNeeded = new();

    internal RecipeGraph(ItemCatalogue c)
    {
        _c = c;
        _level = new int[c.Items.Count];
        Array.Fill(_level, -1);
        foreach (ItemDef item in c.Items) LevelOf(item);
        _buildOrder = c.Items.OrderBy(i => _level[i.Index]).ThenBy(i => i.Index).ToList();
    }

    /// <summary>0 for raw and unique items, otherwise one more than the deepest input.</summary>
    public int Level(ItemDef item) => _level[item.Index];

    /// <summary>Every item, inputs before anything made from them.</summary>
    public IReadOnlyList<ItemDef> BuildOrder => _buildOrder;

    int LevelOf(ItemDef item)
    {
        if (_level[item.Index] >= 0) return _level[item.Index];
        int level = 0;
        if (item.Recipe != null)
            foreach (var (input, _) in item.Recipe.Inputs) level = Math.Max(level, LevelOf(input) + 1);
        return _level[item.Index] = level;
    }

    /// <summary>A recipe cycle as a path of ids (first = last), or null if the tree has none.</summary>
    public static List<string> FindCycle(IReadOnlyList<ItemDef> items)
    {
        var state = new Dictionary<ItemDef, int>(); // 1 on the stack, 2 done
        var stack = new List<ItemDef>();
        List<string> found = null;

        bool Visit(ItemDef item)
        {
            state[item] = 1;
            stack.Add(item);
            if (item.Recipe != null)
                foreach (var (input, _) in item.Recipe.Inputs)
                {
                    if (state.TryGetValue(input, out int s) && s == 1)
                    {
                        int from = stack.IndexOf(input);
                        found = stack.Skip(from).Select(i => i.Id).Append(input.Id).ToList();
                        return true;
                    }
                    if (s == 0 && Visit(input)) return true;
                }
            stack.RemoveAt(stack.Count - 1);
            state[item] = 2;
            return false;
        }

        foreach (ItemDef item in items)
            if (!state.ContainsKey(item) && Visit(item)) return found;
        return null;
    }

    /// <summary>
    /// Raw and unique items in one unit, as fractions (a wire is a quarter of a copper bar). The
    /// exact ore value, used by the conservation tests and the price of a whole vessel.
    /// </summary>
    public IReadOnlyDictionary<string, double> RawCost(ItemDef item)
    {
        if (_rawCost.TryGetValue(item.Id, out var cached)) return cached;
        var cost = new Dictionary<string, double>(StringComparer.Ordinal);
        if (item.Recipe == null) cost[item.Id] = 1;
        else
            foreach (var (input, n) in item.Recipe.Inputs)
                foreach (var (raw, amount) in RawCost(input))
                    cost[raw] = cost.GetValueOrDefault(raw) + amount * n / item.Recipe.Count;
        _rawCost[item.Id] = cost;
        return cost;
    }

    /// <summary>The fractional raw content of a bill.</summary>
    public Dictionary<string, double> RawCost(Bill bill)
    {
        var total = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (id, n) in bill)
            foreach (var (raw, amount) in RawCost(_c.Item(id)))
                total[raw] = total.GetValueOrDefault(raw) + amount * n;
        return total;
    }

    /// <summary>The highest recipe tier anywhere under an item: the least constructor that can make it from scratch.</summary>
    public int TierNeeded(ItemDef item)
    {
        if (item.Recipe == null) return 0;
        if (_tierNeeded.TryGetValue(item.Id, out int t)) return t;
        int tier = item.Recipe.Tier;
        foreach (var (input, _) in item.Recipe.Inputs) tier = Math.Max(tier, TierNeeded(input));
        return _tierNeeded[item.Id] = tier;
    }

    /// <summary>
    /// True if an item can be made from gathered materials alone (mined, dug or harvested). If
    /// not, <paramref name="blockers"/> lists the unique finds in the way.
    /// </summary>
    public bool FromGathered(ItemDef item, out List<string> blockers)
    {
        blockers = RawCost(item).Keys.Where(id => !_c.Item(id).IsGathered).ToList();
        return blockers.Count == 0;
    }

    /// <summary>
    /// Expands a bill: every item that isn't a leaf is made by whole runs of its recipe, with the
    /// needs of every consumer summed before an item is rounded up to whole runs (so ten blocks
    /// each wanting half a copper bar cost five bars, not ten).
    /// </summary>
    public Expansion Expand(Bill need, Func<ItemDef, bool> isLeaf = null)
    {
        isLeaf ??= i => i.Recipe == null;
        var result = new Expansion();
        var wanted = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var (id, n) in need) wanted[id] = wanted.GetValueOrDefault(id) + n;

        // Highest level first: everything that consumes an item is settled before the item is.
        var steps = new List<(Recipe, int)>();
        for (int i = _buildOrder.Count - 1; i >= 0; i--)
        {
            ItemDef item = _buildOrder[i];
            if (!wanted.TryGetValue(item.Id, out long n) || n <= 0) continue;
            if (item.Recipe == null || isLeaf(item))
            {
                result.Leaves.Add(item.Id, checked((int)n));
                continue;
            }
            Recipe r = item.Recipe;
            int runs = r.RunsFor(checked((int)n));
            int surplus = runs * r.Count - (int)n;
            if (surplus > 0) result.Byproducts.Add(item.Id, surplus);
            foreach (var (input, per) in r.Inputs)
                wanted[input.Id] = wanted.GetValueOrDefault(input.Id) + (long)per * runs;
            steps.Add((r, runs));
            result.Seconds += r.Seconds * runs;
            result.MaxTier = Math.Max(result.MaxTier, r.Tier);
        }
        steps.Reverse();
        result.Steps.AddRange(steps);
        return result;
    }
}
