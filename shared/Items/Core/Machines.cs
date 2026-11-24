using System;
using System.Collections.Generic;
using System.Linq;

namespace GalHack.Items;

public enum MachineKind { Constructor, Deconstructor, BlueprintFactory }

/// <summary>One production machine: a constructor tier, the deconstructor or the blueprint factory.</summary>
public sealed class MachineDef
{
    public string Id { get; init; }
    public string Name { get; init; }
    /// <summary>The short code on its screen's header, like FAB-1.</summary>
    public string Tag { get; init; }
    public MachineKind Kind { get; init; }
    /// <summary>Constructors: the highest recipe tier it knows.</summary>
    public int Tier { get; init; }
    /// <summary>How fast it runs recipe seconds (1 = the small constructor).</summary>
    public double Speed { get; init; } = 1;
    /// <summary>The building block that is this machine, if it is a block.</summary>
    public string BlockId { get; init; }
    /// <summary>The item that is this machine, if it is carried.</summary>
    public string ItemId { get; init; }
    public bool Portable { get; init; }

    public override string ToString() => Id;
}

/// <summary>One line of a build list: so many of a block (in its item's units: a door family counts its doors' units).</summary>
public readonly record struct BlockCount(string BlockId, int Count);

/// <summary>Pacing reference used by the tests and the starter-vessel estimate.</summary>
public sealed class ReferenceRates
{
    public double GatherOrePerMinute { get; init; } = 45;
    public int OreUnitsPerCell { get; init; } = 12;
    public double PlaceSecondsPerBlock { get; init; } = 1.2;
    public IReadOnlyList<BlockCount> Foothold { get; init; } = Array.Empty<BlockCount>();
    public Bill StarterFuel { get; init; } = new();
}

/// <summary>Every machine, plus the deconstructor's return rate and the pacing reference.</summary>
public sealed class MachineCatalogue
{
    readonly List<MachineDef> _all = new();
    readonly Dictionary<string, MachineDef> _byId = new();
    readonly Dictionary<string, MachineDef> _byBlock = new();

    public IReadOnlyList<MachineDef> All => _all;
    public IEnumerable<MachineDef> Constructors => _all.Where(m => m.Kind == MachineKind.Constructor);
    public MachineDef Deconstructor { get; private set; }
    public MachineDef Factory { get; private set; }

    /// <summary>Fraction of what an item or block was made of that the deconstructor gives back.</summary>
    public double DeconstructorReturn { get; internal set; } = 0.5;
    /// <summary>Breaking an item down takes this fraction of its recipe's seconds.</summary>
    public double DeconstructorSecondsFactor { get; internal set; } = 0.5;
    public IReadOnlyList<string> TierNames { get; internal set; } = new[] { "survival", "small", "large", "advanced" };
    public ReferenceRates Reference { get; internal set; } = new();

    public MachineDef Get(string id) => _byId.TryGetValue(id, out MachineDef m) ? m : throw new KeyNotFoundException($"unknown machine '{id}'");
    public bool TryGet(string id, out MachineDef m) => _byId.TryGetValue(id, out m);

    /// <summary>The machine a building block is, or null.</summary>
    public MachineDef ForBlock(string blockId) => blockId != null && _byBlock.TryGetValue(blockId, out MachineDef m) ? m : null;

    public string TierName(int tier) => tier >= 0 && tier < TierNames.Count ? TierNames[tier] : $"tier {tier}";

    /// <summary>The slowest constructor that knows a tier, for "needs a large constructor" messages.</summary>
    public MachineDef FirstConstructorFor(int tier) =>
        Constructors.Where(c => c.Tier >= tier).OrderBy(c => c.Tier).ThenBy(c => c.Speed).FirstOrDefault();

    internal void Add(MachineDef m)
    {
        if (_byId.ContainsKey(m.Id)) throw new FormatException($"duplicate machine id '{m.Id}'");
        _all.Add(m);
        _byId[m.Id] = m;
        if (m.BlockId != null) _byBlock[m.BlockId] = m;
        if (m.Kind == MachineKind.Deconstructor) Deconstructor = m;
        if (m.Kind == MachineKind.BlueprintFactory) Factory = m;
    }
}
