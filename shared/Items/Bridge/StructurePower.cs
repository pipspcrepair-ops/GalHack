using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GalHack.Structures;

namespace GalHack.Items.Bridge;

/// <summary>What a device is doing: the measures its draw follows (power.json "draw"). Each reads 0 to 1.</summary>
public enum PowerState { Throttle, Turning, Hovering, Lit, Working, Seated, On, Idle, Tracking, Firing, Up, Recharging, Engaged }

/// <summary>A drive's jump fuel: the item it burns from core storage, and how many per <see cref="Unit"/> (AU or LY).</summary>
public sealed record DriveFuel(string Item, double Per, string Unit);

/// <summary>
/// shared/Items/data/power.json: the energy in a unit of fuel, the core's built-in capacitor, what makes each
/// function draw, the drives' jump fuels and the shields' crystal. Generator fuel is every item with fuel units,
/// burnt in <see cref="Fuels"/> order.
/// </summary>
public sealed class PowerRules
{
    public const string FileName = "power.json";

    public double FuelUnitMJ { get; private init; }
    public double CoreMJ { get; private init; }

    /// <summary>By block function: each state it draws in, and the share of the block's power it draws there.</summary>
    public IReadOnlyDictionary<string, (PowerState State, double Share)[]> Draw { get; private init; }

    /// <summary>By block function: the drive's jump fuel.</summary>
    public IReadOnlyDictionary<string, DriveFuel> Drives { get; private init; }

    /// <summary>Fold crystal a shield burns per hit point it recharges (phase 2 wires shields in).</summary>
    public double ShieldFoldPerHp { get; private init; }

    /// <summary>
    /// Generator fuels in the order a generator takes them from core storage: the least efficient first (fewest
    /// units per storage unit, then per item). The bulkiest fuel goes first, which frees the most room in the hold
    /// for each unit burnt, and the dense fuel is kept for when it counts.
    /// </summary>
    public IReadOnlyList<ItemDef> Fuels { get; private init; }

    public static PowerRules LoadDirectory(string dir, ItemCatalogue items) => Parse(File.ReadAllText(Path.Combine(dir, FileName)), items);

    public static PowerRules Parse(string json, ItemCatalogue items)
    {
        JsonElement root = DataJson.Parse(json, "galhack.power");
        var draw = new Dictionary<string, (PowerState, double)[]>(StringComparer.Ordinal);
        foreach (JsonProperty f in root.GetProperty("draw").EnumerateObject())
            draw[f.Name] = f.Value.EnumerateObject().Select(p => (Enum.TryParse(p.Name, true, out PowerState s) ? s
                : throw new InvalidDataException($"power.json: '{f.Name}' draws in an unknown state '{p.Name}'"), p.Value.GetDouble())).ToArray();
        var drives = new Dictionary<string, DriveFuel>(StringComparer.Ordinal);
        foreach (JsonProperty d in root.GetProperty("drives").EnumerateObject())
        {
            string fuel = DataJson.Str(d.Value, "fuel");
            if (!items.Has(fuel)) throw new InvalidDataException($"power.json: drive '{d.Name}' burns an unknown item '{fuel}'");
            drives[d.Name] = new DriveFuel(fuel, DataJson.Num(d.Value, "per"), DataJson.Str(d.Value, "unit"));
        }
        return new PowerRules
        {
            FuelUnitMJ = DataJson.Num(root, "fuelUnitMJ"),
            CoreMJ = DataJson.Num(root, "coreMJ"),
            Draw = draw,
            Drives = drives,
            ShieldFoldPerHp = DataJson.Num(root.GetProperty("shield"), "foldPerHp"),
            Fuels = items.Items.Where(i => i.FuelUnits > 0).OrderBy(i => i.FuelUnits / i.VolumeSu).ThenBy(i => i.FuelUnits).ToList(),
        };
    }
}

/// <summary>
/// One structure's live power (design section 13): a capacitor charge, generators that keep it charged by burning
/// fuel from core storage up to their output, and every device drawing from it by what it is doing right now. The
/// generators fill what the capacitor lacks, so the burn follows the load and an idle structure burns nothing.
/// Short of power, every device gets the same share (<see cref="Supply"/>); with the capacitor empty and no fuel,
/// it is nothing, and everything stops. Whoever holds the structure says what its devices are doing.
/// </summary>
public sealed class StructurePower
{
    readonly PowerRules _rules;
    readonly Structure _s;
    readonly SharedStorage _storage;
    readonly List<Load> _loads = new();
    readonly HashSet<string> _functions = new(StringComparer.Ordinal);
    int _version = -1;
    double _extra;

    readonly record struct Load(Int3 Cell, Block Block, double Mw, (PowerState State, double Share)[] Rule);

    public StructurePower(PowerRules rules, Structure structure, SharedStorage storage)
    {
        _rules = rules;
        _s = structure;
        _storage = storage;
    }

    public PowerRules Rules => _rules;

    /// <summary>The capacitor's charge, MJ.</summary>
    public double ChargeMJ { get; set; }

    /// <summary>The capacitor: the one built into the core (none without a core).</summary>
    public double CapacityMJ => _s.Core != null ? _rules.CoreMJ : 0;

    /// <summary>What the generators can give, MW.</summary>
    public double GeneratorMW { get { Refresh(); return _generators; } }
    double _generators;

    /// <summary>Last tick: what the devices asked for, and what the generators gave, MW.</summary>
    public double DrawMW { get; private set; }
    public double OutputMW { get; private set; }

    /// <summary>Last tick: the draw against what the generators can give (over 1, the capacitor drains; infinite with a draw and no generator).</summary>
    public double Load => DrawMW <= 1e-12 ? 0 : GeneratorMW > 0 ? DrawMW / GeneratorMW : double.PositiveInfinity;

    /// <summary>Last tick: the share of the draw that was met (1 with nothing drawn and power to give, 0 with none).</summary>
    public double Supply { get; private set; }

    /// <summary>The fuel item a generator is burning now, or null, and the units left in it.</summary>
    public string Burning { get; private set; }
    public double BurningUnits { get; set; }

    /// <summary>Generator fuel left: in core storage, and what is left of the item burning.</summary>
    public double FuelUnits
    {
        get
        {
            double n = BurningUnits;
            foreach (ItemDef f in _rules.Fuels) n += (double)f.FuelUnits * _storage.Count(f.Id);
            return n;
        }
    }

    /// <summary>How long the fuel and the charge last at the last tick's draw, s (infinite when nothing draws).</summary>
    public double SecondsLeft => DrawMW <= 1e-9 ? double.PositiveInfinity
        : ((GeneratorMW > 0 ? FuelUnits * _rules.FuelUnitMJ : 0) + ChargeMJ) / DrawMW;

    /// <summary>Power drawn from outside its blocks this tick (a climate node standing by it), MW.</summary>
    public void AddLoad(double mw) => _extra += mw;

    /// <summary>A drive's reach on the jump fuel in core storage (AU or LY, per its rule), or null if none is fitted.</summary>
    public (double Reach, string Unit)? Range(string function)
    {
        Refresh();
        if (!_functions.Contains(function) || !_rules.Drives.TryGetValue(function, out DriveFuel d)) return null;
        return (_storage.Count(d.Item) / d.Per, d.Unit);
    }

    /// <summary>
    /// One tick. <paramref name="on"/>: the structure's power switch (off, nothing draws and nothing is made).
    /// <paramref name="free"/>: Creative's rule, generators run without burning fuel. <paramref name="doing"/> says
    /// what a device is doing, 0 to 1, in a state its function draws in.
    /// </summary>
    public void Tick(double dt, bool on, bool free, Func<PowerState, Int3, Block, double> doing)
    {
        Refresh();
        double demand = 0;
        if (on)
        {
            foreach (Load l in _loads)
            {
                double share = 0;
                foreach (var (state, part) in l.Rule)
                    if (part > 0) share += part * Math.Clamp(doing(state, l.Cell, l.Block), 0, 1);
                demand += l.Mw * share;
            }
            demand += _extra;
        }
        _extra = 0;
        double cap = CapacityMJ, need = demand * dt, made = 0;
        ChargeMJ = Math.Min(ChargeMJ, cap);
        // The generators make what the draw takes and the capacitor lacks, up to their output.
        if (on && _generators > 0)
        {
            double want = Math.Min(_generators * dt, need + cap - ChargeMJ);
            made = free ? want : Burn(want);
        }
        double given = Math.Min(need, ChargeMJ + made);
        ChargeMJ = Math.Min(cap, ChargeMJ + made - given);
        DrawMW = demand;
        OutputMW = dt > 0 ? made / dt : 0;
        bool source = on && (ChargeMJ > 1e-9 || _generators > 0 && (free || BurningUnits > 1e-9 || HasFuel()));
        Supply = need > 1e-12 ? given / need : source ? 1 : 0;
    }

    bool HasFuel()
    {
        foreach (ItemDef f in _rules.Fuels)
            if (_storage.Count(f.Id) > 0) return true;
        return false;
    }

    /// <summary>Burns fuel for so much energy, MJ, taking whole items from core storage as it goes; returns what it got.</summary>
    double Burn(double mj)
    {
        double units = mj / _rules.FuelUnitMJ, got = 0;
        while (units - got > 1e-12)
        {
            if (BurningUnits <= 1e-12)
            {
                ItemDef next = null;
                foreach (ItemDef f in _rules.Fuels)
                    if (_storage.Count(f.Id) > 0) { next = f; break; }
                if (next == null || _storage.Remove(next.Id, 1) == 0) { Burning = null; break; }
                BurningUnits += next.FuelUnits;
                Burning = next.Id;
            }
            double take = Math.Min(BurningUnits, units - got);
            BurningUnits -= take;
            got += take;
        }
        return got * _rules.FuelUnitMJ;
    }

    /// <summary>The devices that draw (a rule for their function and a power to draw), and the generators' output, again when the build changes.</summary>
    void Refresh()
    {
        if (_version == _s.Version) return;
        _version = _s.Version;
        _loads.Clear();
        _functions.Clear();
        _generators = 0;
        foreach (var (cell, b) in _s.Blocks())
        {
            BlockType t = _s.TypeOf(b);
            _functions.Add(t.Function);
            if (t.Power > 0) _generators += t.Power;
            else if (t.Power < 0 && _rules.Draw.TryGetValue(t.Function, out var rule)) _loads.Add(new Load(cell, b, -t.Power, rule));
        }
    }
}
