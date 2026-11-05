using System;
using System.Collections.Generic;
using System.Linq;

namespace GalHack.Items.Production;

/// <summary>Everything about building one blueprint here and now: its bill of materials, what's missing, how long.</summary>
public sealed class FactoryQuote
{
    public string Name { get; init; }
    public IReadOnlyList<BlockCount> Blocks { get; init; }
    public int BlockTotal => Blocks.Sum(b => b.Count);
    /// <summary>The blocks' own bills summed: the parts that end up in the structure.</summary>
    public Bill Parts { get; init; }
    /// <summary>The bill of materials it takes from storage: refined materials (and any unique finds), never ore.</summary>
    public Bill Materials { get; init; }
    /// <summary>Whole-run leftovers it puts back into storage when done (two spare wires, say).</summary>
    public Bill Byproducts { get; init; }
    /// <summary>What storage lacks right now. Empty in creative.</summary>
    public Bill Missing { get; init; }
    /// <summary>Seconds at this factory: making the parts plus fitting each block. Zero in creative.</summary>
    public double Seconds { get; init; }
    /// <summary>Blocks it has no bill for.</summary>
    public IReadOnlyList<string> Problems { get; init; }
    public bool CanBuild => Problems.Count == 0 && Missing.IsEmpty;
}

public enum FactoryStartStatus { Started, Busy, Missing, UnknownBlocks, Empty }

public readonly record struct FactoryStart(FactoryStartStatus Status, FactoryQuote Quote, string Message)
{
    public bool Ok => Status == FactoryStartStatus.Started;
}

/// <summary>
/// Builds a whole saved blueprint from shared storage (design section 11): it shows the bill of
/// materials, what's missing and the time, refuses with the missing list, and otherwise takes
/// the materials at once and makes the parts itself. It pays in refined materials, not ore: the
/// bill is every block's bill expanded down to refined materials, with needs summed before
/// rounding to whole runs and the rounding surplus handed back when it finishes.
///
/// In creative the bill is shown but nothing is taken and the structure appears at once.
/// </summary>
public sealed class BlueprintFactory
{
    readonly Bill _tray = new();

    public MachineDef Def { get; }
    public ItemCatalogue Items { get; }
    public SharedStorage Storage { get; }
    public WorldRules Rules { get; }
    public int Version { get; private set; }

    public FactoryQuote Current { get; private set; }
    /// <summary>0..1 through <see cref="Current"/>.</summary>
    public double Progress { get; private set; }
    public double SecondsLeft => Current == null ? 0 : (1 - Progress) * Current.Seconds;
    public FactoryQuote LastFinished { get; private set; }
    public Bill Tray => _tray.Clone();

    /// <summary>Raised when a blueprint is done: the world spawns its structure.</summary>
    public event Action<FactoryQuote> Finished;

    public BlueprintFactory(ItemCatalogue items, MachineDef def, SharedStorage storage, WorldRules rules)
    {
        if (def.Kind != MachineKind.BlueprintFactory) throw new ArgumentException($"{def.Id} is not a blueprint factory");
        Items = items;
        Def = def;
        Storage = storage;
        Rules = rules;
    }

    /// <summary>A leaf of the factory's bill: what it takes from storage rather than making.</summary>
    public static bool PaysIn(ItemDef item) => item.Category is ItemCategory.Refined or ItemCategory.Raw or ItemCategory.Unique;

    public FactoryQuote Quote(string name, IEnumerable<BlockCount> blocks)
    {
        var list = blocks.GroupBy(b => (b.BlockId, b.Small))
            .Select(g => new BlockCount(g.Key.BlockId, g.Key.Small, g.Sum(b => b.Count)))
            .OrderBy(b => b.BlockId, StringComparer.Ordinal).ThenBy(b => b.Small).ToList();
        var problems = list.Where(b => !Items.Blocks.TryGet(b.BlockId, out _)).Select(b => $"no bill for block '{b.BlockId}'").ToList();
        var known = list.Where(b => Items.Blocks.TryGet(b.BlockId, out _)).ToList();

        Bill parts = Items.Blocks.Total(known);
        Expansion e = Items.Graph.Expand(parts, PaysIn);
        double seconds = e.Seconds + known.Sum(b => Items.Blocks.AssemblySeconds(b.Small) * b.Count);
        return new FactoryQuote
        {
            Name = name,
            Blocks = list,
            Parts = parts,
            Materials = e.Leaves,
            Byproducts = e.Byproducts,
            Missing = Rules.Creative ? new Bill() : Storage.Missing(e.Leaves),
            Seconds = Rules.Creative ? 0 : seconds / Def.Speed,
            Problems = problems,
        };
    }

    /// <summary>Starts building, or refuses with what's missing. In creative it finishes at once.</summary>
    public FactoryStart Start(string name, IEnumerable<BlockCount> blocks)
    {
        if (Current != null) return new(FactoryStartStatus.Busy, null, $"busy with {Current.Name}");
        FactoryQuote q = Quote(name, blocks);
        if (q.BlockTotal == 0) return new(FactoryStartStatus.Empty, q, "the blueprint has no blocks");
        if (q.Problems.Count > 0) return new(FactoryStartStatus.UnknownBlocks, q, string.Join("; ", q.Problems));
        if (!q.Missing.IsEmpty) return new(FactoryStartStatus.Missing, q, "missing " + Items.Describe(q.Missing));

        if (Rules.Survival && !Storage.TryTake(q.Materials))
            return new(FactoryStartStatus.Missing, q, "missing " + Items.Describe(Storage.Missing(q.Materials)));
        Current = q;
        Progress = 0;
        Version++;
        if (Rules.Creative) Complete();
        return new(FactoryStartStatus.Started, q, null);
    }

    public void Tick(double dt)
    {
        FlushTray();
        if (Current == null) return;
        Progress = Current.Seconds <= 0 ? 1 : Math.Min(1, Progress + dt / Current.Seconds);
        Version++;
        if (Progress >= 1) Complete();
    }

    void Complete()
    {
        FactoryQuote done = Current;
        if (Rules.Survival) _tray.Add(Storage.AddAll(done.Byproducts));
        Current = null;
        Progress = 0;
        LastFinished = done;
        Version++;
        Finished?.Invoke(done);
    }

    void FlushTray()
    {
        if (_tray.IsEmpty) return;
        Bill left = Storage.AddAll(_tray);
        foreach (string id in _tray.Ids.ToList()) _tray.Add(id, -_tray[id]);
        _tray.Add(left);
        Version++;
    }

    public string Status => Current != null ? $"BUILDING {Current.Name.ToUpperInvariant()}" : "IDLE";
}
