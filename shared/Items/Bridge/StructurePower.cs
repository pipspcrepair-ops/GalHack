using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GalHack.Structures;

namespace GalHack.Items.Bridge;

/// <summary>What a device is doing: the measures its draw follows (power.json "draw"). Each reads 0 to 1.</summary>
public enum PowerState { Throttle, Turning, Hovering, Lit, Working, Seated, On, Idle, Tracking, Firing, Up, Recharging, Engaged }

/// <summary>A drive's jump fuel: the item it burns from core storage, how many per <see cref="Unit"/> (AU or LY), and the seconds it spools before it jumps.</summary>
public sealed record DriveFuel(string Item, double Per, string Unit, double Spool);

/// <summary>A shield generator's figures: the hit points it holds and how many it recharges a second at full power.</summary>
public readonly record struct ShieldBlock(double Hp, double Recharge);

/// <summary>
/// shared/Items/data/power.json: the energy in a unit of fuel, the core's built-in capacitor, what makes each
/// function draw, the drives' jump fuels, the shields and the solar panels. Generator fuel is every item with fuel units,
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

    /// <summary>Fold crystal a shield burns per hit point it recharges, the seconds after a hit before it recharges, and each shield block's figures by id.</summary>
    public double ShieldFoldPerHp { get; private init; }
    public double ShieldCooldown { get; private init; }

    /// <summary>The item a shield burns while it recharges (fold crystal).</summary>
    public string ShieldFuel { get; private init; }
    public IReadOnlyDictionary<string, ShieldBlock> Shields { get; private init; }

    /// <summary>The share of the sun a full cloud cover takes from the solar panels.</summary>
    public double SolarCloudCut { get; private init; }

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
            drives[d.Name] = new DriveFuel(fuel, DataJson.Num(d.Value, "per"), DataJson.Str(d.Value, "unit"), DataJson.Num(d.Value, "spool"));
        }
        JsonElement shield = root.GetProperty("shield");
        var shields = new Dictionary<string, ShieldBlock>(StringComparer.Ordinal);
        foreach (JsonProperty b in shield.GetProperty("blocks").EnumerateObject())
            shields[b.Name] = new ShieldBlock(DataJson.Num(b.Value, "hp"), DataJson.Num(b.Value, "recharge"));
        return new PowerRules
        {
            FuelUnitMJ = DataJson.Num(root, "fuelUnitMJ"),
            CoreMJ = DataJson.Num(root, "coreMJ"),
            Draw = draw,
            Drives = drives,
            ShieldFuel = items.Has(DataJson.Str(shield, "fuel")) ? DataJson.Str(shield, "fuel") : throw new InvalidDataException("power.json: the shield burns an unknown item"),
            ShieldFoldPerHp = DataJson.Num(shield, "foldPerHp"),
            ShieldCooldown = DataJson.Num(shield, "cooldown"),
            Shields = shields,
            SolarCloudCut = DataJson.Num(root.GetProperty("solar"), "cloudCut"),
            Fuels = items.Items.Where(i => i.FuelUnits > 0).OrderBy(i => i.FuelUnits / i.VolumeSu).ThenBy(i => i.FuelUnits).ToList(),
        };
    }
}

/// <summary>
/// One structure's live power (design section 13): a capacitor charge, solar panels and generators that keep it
/// charged (the sun first, then fuel burnt from core storage, up to their output), and every device drawing from it
/// by what it is doing right now. They fill what the capacitor lacks, so the burn follows the load and an idle
/// structure burns nothing. Its shield and its drives are in StructurePower.Shield.cs.
/// Short of power, every device gets the same share (<see cref="Supply"/>); with the capacitor empty and no fuel,
/// it is nothing, and everything stops. Whoever holds the structure says what its devices are doing.
/// </summary>
public sealed partial class StructurePower
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
    double _generators, _solar;

    /// <summary>What the solar panels give in full sun, MW, and now: by <see cref="Sun"/>, the share of full sun on them (the holder sets it).</summary>
    public double SolarMW { get { Refresh(); return _solar; } }
    public double SolarNowMW => SolarMW * Sun;
    public double Sun { get; set; }

    /// <summary>What it can give each tick without its capacitor, MW: its generators and the sun on its panels.</summary>
    public double SupplyMW => GeneratorMW + SolarNowMW;

    /// <summary>Last tick: what the devices asked for, and what the generators gave, MW.</summary>
    public double DrawMW { get; private set; }
    public double OutputMW { get; private set; }

    /// <summary>Last tick: the draw against what the generators and the sun can give (over 1, the capacitor drains; infinite with a draw and neither).</summary>
    public double Usage => DrawMW <= 1e-12 ? 0 : SupplyMW > 0 ? DrawMW / SupplyMW : double.PositiveInfinity;

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

    /// <summary>How long the fuel and the charge last at the last tick's draw, the sun's share met first, s (infinite when the sun covers it).</summary>
    public double SecondsLeft => DrawMW - SolarNowMW <= 1e-9 ? double.PositiveInfinity
        : ((GeneratorMW > 0 ? FuelUnits * _rules.FuelUnitMJ : 0) + ChargeMJ) / (DrawMW - SolarNowMW);

    /// <summary>Power drawn from outside its blocks this tick (a force field), MW.</summary>
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
        ShieldRecharging = on && RechargeDue();
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
        // The sun, then the generators, make what the draw takes and the capacitor lacks, up to their output.
        if (on) made = Math.Min(SolarNowMW * dt, need + cap - ChargeMJ);
        if (on && _generators > 0)
        {
            double want = Math.Min(_generators * dt, need + cap - ChargeMJ - made);
            made += free ? want : Burn(want);
        }
        double given = Math.Min(need, ChargeMJ + made);
        ChargeMJ = Math.Min(cap, ChargeMJ + made - given);
        DrawMW = demand;
        OutputMW = dt > 0 ? made / dt : 0;
        bool source = on && (ChargeMJ > 1e-9 || SolarNowMW > 1e-12 || _generators > 0 && (free || BurningUnits > 1e-9 || HasFuel()));
        Supply = need > 1e-12 ? given / need : source ? 1 : 0;
        TickShield(dt, on);
        TickDrive(dt, on);
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

    /// <summary>The devices that draw (a rule for their function and a power to draw), the generators' and panels' output and the shield's figures, again when the build changes.</summary>
    void Refresh()
    {
        if (_version == _s.Version) return;
        _version = _s.Version;
        _loads.Clear();
        _functions.Clear();
        _generators = _solar = ShieldMaxHp = _shieldRecharge = 0;
        foreach (var (cell, b) in _s.Blocks())
        {
            BlockType t = _s.TypeOf(b);
            _functions.Add(t.Function);
            if (_rules.Shields.TryGetValue(t.Id, out ShieldBlock sb)) (ShieldMaxHp, _shieldRecharge) = (ShieldMaxHp + sb.Hp, _shieldRecharge + sb.Recharge);
            if (t.Power > 0 && t.Function == "solar") _solar += t.Power;
            else if (t.Power > 0) _generators += t.Power;
            else if (t.Power < 0 && _rules.Draw.TryGetValue(t.Function, out var rule)) _loads.Add(new Load(cell, b, -t.Power, rule));
        }
    }
}
