using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GalHack.Items;

namespace Slice.Items;

/// <summary>The numbers behind levels and experience (data/progress.json).</summary>
public sealed class ProgressRules
{
    public int[] Levels { get; set; } = Array.Empty<int>();
    public int[] Points { get; set; } = Array.Empty<int>();
    public int PastCap { get; set; } = 1500;
    /// <summary>Unlock points a new character starts with.</summary>
    public int StartPoints { get; set; }
    public XpRules Xp { get; set; } = new();

    /// <summary>The level cap: level 1 plus one per entry in <see cref="Levels"/>.</summary>
    public int Cap => Levels.Length + 1;

    public sealed class XpRules
    {
        /// <summary>Scales every experience gain (0.05: a twentieth of the values listed here).</summary>
        public double Gain { get; set; } = 1;
        public int OreCell { get; set; } = 3;
        public int Plant { get; set; } = 2;
        public double CraftSecondsPerXp { get; set; } = 2;
        public int Deposit { get; set; } = 15;
        public int Species { get; set; } = 10;
        public float DepositRadius { get; set; } = 30;
    }

    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static ProgressRules Parse(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (doc.RootElement.GetProperty("schema").GetString() != "galhack.progress") throw new InvalidDataException("progress.json: wrong schema");
        var rules = doc.RootElement.Deserialize<ProgressRules>(Json);
        if (rules.Levels.Length != rules.Points.Length) throw new InvalidDataException("progress.json: one points entry per level");
        for (int i = 1; i < rules.Levels.Length; i++)
            if (rules.Levels[i] <= rules.Levels[i - 1]) throw new InvalidDataException("progress.json: levels must rise");
        return rules;
    }

    /// <summary>Experience to reach a level (1 is free).</summary>
    public int XpFor(int level) => level <= 1 ? 0 : Levels[Math.Min(level, Cap) - 2];
}

/// <summary>
/// The character's progress, in one serialisable place (phase 3's saves keep it whole): experience,
/// the level it gives, unlock points earned and spent, and every unlocked tech node.
/// </summary>
public sealed class ProgressState
{
    public double Xp { get; set; }
    public int PointsEarned { get; set; }
    public int PointsSpent { get; set; }
    public List<string> Unlocked { get; set; } = new();
    /// <summary>Deposits found and species met (exploring XP is paid once each).</summary>
    public List<string> Found { get; set; } = new();
}

/// <summary>One node of the tech tree: an item or a block that has an unlock.</summary>
public sealed record TechNode(string Id, string Name, Unlock Unlock, string ItemId, string BlockId)
{
    /// <summary>A sub-group on its tab (a deco piece's theme set), or null.</summary>
    public string Group { get; init; }
    public string Tab => Unlock.Tab;
    public int Level => Unlock.Level;
    public int Cost => Unlock.Cost;
}

/// <summary>Why a node can't be unlocked (or used) yet, or that it can.</summary>
public enum UnlockState { Unlocked, Affordable, NeedsLevel, NeedsPoints, NeedsParent }

/// <summary>
/// Levels, experience and the tech tree (design section on progression; Empyrion's shape: every
/// item and block carries its own node). Experience comes from playing: digging ore, cutting
/// plants, crafting, building, kills and finding things. Each level grants unlock points, which
/// buy nodes whose level and parent are reached. Nothing that has a node can be crafted or placed
/// until it is unlocked; in Creative everything is.
/// </summary>
public sealed class Progress
{
    public readonly ProgressRules Rules;
    public readonly ProgressState State;
    public readonly IReadOnlyList<TechNode> Nodes;
    readonly Dictionary<string, TechNode> _byId;
    readonly HashSet<string> _unlocked;
    readonly HashSet<string> _found;

    /// <summary>Everything is unlocked (Creative).</summary>
    public bool Creative;

    /// <summary>Experience earned: amount and why ("+3 XP ore").</summary>
    public event Action<double, string> Earned;

    /// <summary>A new level was reached, with the points it granted.</summary>
    public event Action<int, int> LevelledUp;

    /// <summary>A node was unlocked.</summary>
    public event Action<TechNode> Unlocked;

    /// <summary>A free node came with a new level: a recipe learnt.</summary>
    public event Action<TechNode> Learnt;

    /// <summary>The tabs: every structure block (one tree from the core), tools, weapons, the deco sets, and misc.</summary>
    public static readonly string[] Tabs = { "structures", "tools", "weapons", "deco", "misc" };

    public static string TabName(string tab) => tab.ToUpperInvariant();

    public Progress(ProgressRules rules, ItemCatalogue items, GalHack.Structures.Catalogue blocks, ProgressState state = null)
    {
        Rules = rules;
        State = state ?? new ProgressState { PointsEarned = rules.StartPoints };
        Nodes = Build(items, blocks);
        _byId = Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        _unlocked = new HashSet<string>(State.Unlocked.Select(items.Current), StringComparer.Ordinal);
        _found = new HashSet<string>(State.Found, StringComparer.Ordinal);
        // The starting essentials: every node at level 1 that costs nothing.
        GrantFree();
    }

    /// <summary>
    /// Nodes that cost nothing unlock themselves as soon as their level and parent are reached:
    /// the starting essentials at level 1, and later the free blocks under a node you bought.
    /// </summary>
    void GrantFree()
    {
        int level = Level;
        bool more = true;
        while (more)
        {
            more = false;
            foreach (TechNode n in Nodes)
                if (n.Cost == 0 && n.Level <= level && !_unlocked.Contains(n.Id) && ParentOk(n))
                {
                    Grant(n);
                    Learnt?.Invoke(n);
                    more = true;
                }
        }
    }

    static List<TechNode> Build(ItemCatalogue items, GalHack.Structures.Catalogue blocks)
    {
        // Blocks are items: a block's node is its item's (iron-plated), so crafting and the tree
        // agree. Deco pieces are block items too, grouped by their theme set on the deco tab.
        var nodes = new List<TechNode>();
        foreach (ItemDef i in items.Items)
            if (i.Unlock != null) nodes.Add(new TechNode(i.Id, i.Name, i.Unlock, i.Id, i.BlockId) { Group = i.Group });
        return nodes.OrderBy(n => Array.IndexOf(Tabs, n.Tab)).ThenBy(n => n.Level).ThenBy(n => n.Name, StringComparer.Ordinal).ToList();
    }

    public TechNode Node(string id) => id != null && _byId.TryGetValue(id, out TechNode n) ? n : null;
    /// <summary>A block's node, by the block its item places (a door family's head).</summary>
    public TechNode NodeOfBlock(string blockId) => Node(blockId);

    // ---- Levels --------------------------------------------------------------------------------

    public int Xp => (int)State.Xp;

    public int Level
    {
        get
        {
            int level = 1;
            while (level < Rules.Cap && State.Xp >= Rules.XpFor(level + 1)) level++;
            return level;
        }
    }

    public int Points => State.PointsEarned - State.PointsSpent;

    /// <summary>Experience into this level, and what the next one needs (past the cap: toward the next point).</summary>
    public (int into, int span) ToNext
    {
        get
        {
            int level = Level;
            if (level >= Rules.Cap)
            {
                int past = Xp - Rules.XpFor(Rules.Cap);
                return (past % Rules.PastCap, Rules.PastCap);
            }
            int from = Rules.XpFor(level), to = Rules.XpFor(level + 1);
            return (Xp - from, to - from);
        }
    }

    /// <summary>Earns experience, scaled by the gain (<paramref name="scaled"/> false: exactly this much); levels and points follow (and keep accruing past the cap).</summary>
    public void Earn(double xp, string why, bool scaled = true)
    {
        if (scaled) xp *= Rules.Xp.Gain;
        if (xp <= 0) return;
        int before = Level, pastBefore = PastCapPoints();
        State.Xp += xp;
        Earned?.Invoke(xp, why);
        int after = Level;
        for (int l = before + 1; l <= after; l++)
        {
            int points = Rules.Points[l - 2];
            State.PointsEarned += points;
            LevelledUp?.Invoke(l, points);
        }
        if (after > before) GrantFree();
        int extra = PastCapPoints() - pastBefore;
        if (extra > 0)
        {
            State.PointsEarned += extra;
            LevelledUp?.Invoke(after, extra);
        }
    }

    int PastCapPoints() => State.Xp <= Rules.XpFor(Rules.Cap) || Level < Rules.Cap ? 0 : (int)(State.Xp - Rules.XpFor(Rules.Cap)) / Rules.PastCap;

    /// <summary>Exploring experience, paid once per thing found (a deposit, a species).</summary>
    public bool Discover(string key, int xp, string why)
    {
        if (!_found.Add(key)) return false;
        State.Found.Add(key);
        Earn(xp, why);
        return true;
    }

    public bool HasFound(string key) => _found.Contains(key);

    // ---- The tree ------------------------------------------------------------------------------

    public bool IsUnlocked(string nodeId) => Creative || nodeId == null || _unlocked.Contains(nodeId) || Node(nodeId) == null;

    /// <summary>Can this item be made? (Items without a node always can.)</summary>
    public bool ItemAllowed(ItemDef item) => item.Unlock == null || IsUnlocked(item.Id);

    /// <summary>
    /// Is this block unlocked? (Blocks without a node always are.) Unlocks gate crafting, as in
    /// Empyrion: a block item you carry (looted, say) places whether or not it is.
    /// </summary>
    public bool BlockAllowed(string blockId) => Node(blockId) == null || IsUnlocked(blockId);

    bool ParentOk(TechNode n) => n.Unlock.Parent == null || Node(n.Unlock.Parent) == null || _unlocked.Contains(n.Unlock.Parent);

    public UnlockState StateOf(TechNode n)
    {
        if (IsUnlocked(n.Id)) return UnlockState.Unlocked;
        if (Level < n.Level) return UnlockState.NeedsLevel;
        if (!ParentOk(n)) return UnlockState.NeedsParent;
        if (Points < n.Cost) return UnlockState.NeedsPoints;
        return UnlockState.Affordable;
    }

    /// <summary>What a locked node still needs, for greyed-out rows: "LEVEL 5", "2 POINTS", "NEEDS HOVER PLATE T1".</summary>
    public string Needs(TechNode n) => StateOf(n) switch
    {
        UnlockState.Unlocked => "UNLOCKED",
        UnlockState.Affordable => $"UNLOCK: {n.Cost} PT{(n.Cost == 1 ? "" : "S")}",
        UnlockState.NeedsLevel => $"LEVEL {n.Level}",
        UnlockState.NeedsParent => $"NEEDS {Node(n.Unlock.Parent)?.Name.ToUpperInvariant()}",
        _ => $"{n.Cost} POINTS (HAVE {Points})",
    };

    /// <summary>Why something with a node can't be used yet ("locked: level 5 in the tech tree"), or null.</summary>
    public string LockReason(string nodeId)
    {
        TechNode n = Node(nodeId);
        if (n == null || IsUnlocked(nodeId)) return null;
        return StateOf(n) == UnlockState.NeedsLevel ? $"LOCKED · LV {n.Level}" : "LOCKED · [TAB] TECH";
    }

    /// <summary>Spends points on a node. False if it isn't affordable now.</summary>
    public bool Unlock(string nodeId)
    {
        TechNode n = Node(nodeId);
        if (n == null || StateOf(n) != UnlockState.Affordable) return false;
        State.PointsSpent += n.Cost;
        Grant(n);
        GrantFree();
        Unlocked?.Invoke(n);
        return true;
    }

    void Grant(TechNode n)
    {
        if (!_unlocked.Add(n.Id)) return;
        State.Unlocked.Add(n.Id);
    }
}
