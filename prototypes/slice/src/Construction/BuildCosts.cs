using System.Collections.Generic;
using System.Linq;
using GalHack.Structures;
using Inventory = GalHack.Structures.Inventory;
using Structure = GalHack.Structures.Structure;

namespace Slice.Construction;

/// <summary>Survival pays for every block; Creative builds anything for free. A per-world setting.</summary>
public enum BuildMode { Survival, Creative }

/// <summary>
/// What building costs and where the materials come from. The one place the build mode is
/// checked: the build tool and the tests only ever ask this. The slice's placeholder is
/// <see cref="CatalogueCosts"/>; shared/Items will replace it behind the same interface.
/// </summary>
public interface IBuildCosts
{
    BuildMode Mode { get; set; }

    /// <summary>What one block costs to place (nothing in Creative).</summary>
    IReadOnlyDictionary<string, int> CostOf(Structure s, in Block block);

    /// <summary>Materials still missing for this block from these stores (none in Creative).</summary>
    IReadOnlyDictionary<string, int> Missing(Structure s, in Block block, IReadOnlyList<Inventory> stores);

    /// <summary>Takes the block's cost from the stores in order (the pack first, then shared storage). False, taking nothing, if short.</summary>
    bool TryPay(Structure s, in Block block, IReadOnlyList<Inventory> stores);

    /// <summary>A block taken down: its materials go back into the first store (nothing in Creative, which never took any).</summary>
    void Refund(Structure s, in Block block, Inventory into);
}

/// <summary>
/// PLACEHOLDER costs until materials and manufacturing are designed: the building catalogue's
/// own recipe numbers (smallCost for small-grid blocks: iron, copper, silicon, carbon), paid
/// straight from what the player carries or the structure stores.
/// </summary>
public sealed class CatalogueCosts : IBuildCosts
{
    static readonly IReadOnlyDictionary<string, int> Nothing = new Dictionary<string, int>();

    public BuildMode Mode { get; set; } = BuildMode.Survival;

    public IReadOnlyDictionary<string, int> CostOf(Structure s, in Block block) =>
        Mode == BuildMode.Creative ? Nothing : s.Catalogue.CostOf(block, s.Grid);

    public IReadOnlyDictionary<string, int> Missing(Structure s, in Block block, IReadOnlyList<Inventory> stores)
    {
        var missing = new Dictionary<string, int>();
        foreach (var (item, n) in CostOf(s, block))
        {
            int have = stores.Sum(st => st[item]);
            if (have < n) missing[item] = n - have;
        }
        return missing;
    }

    public bool TryPay(Structure s, in Block block, IReadOnlyList<Inventory> stores)
    {
        IReadOnlyDictionary<string, int> cost = CostOf(s, block);
        if (Missing(s, block, stores).Count > 0) return false;
        foreach (var (item, n) in cost)
        {
            int left = n;
            foreach (Inventory st in stores)
            {
                int take = System.Math.Min(left, st[item]);
                if (take <= 0) continue;
                st.Items[item] = st[item] - take;
                left -= take;
                if (left == 0) break;
            }
        }
        return true;
    }

    public void Refund(Structure s, in Block block, Inventory into)
    {
        IReadOnlyDictionary<string, int> cost = CostOf(s, block);
        if (cost.Count > 0) into.Add(cost.ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}
