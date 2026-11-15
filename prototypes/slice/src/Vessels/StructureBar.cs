using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GalHack.Arms;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;
using BarKind = Slice.Game.SliceRules.BarKind;
using N = System.Numerics;

namespace Slice.Vessels;

/// <summary>What a slot's number does: switches it, picks it for the trigger, or runs it once.</summary>
public enum BarAct { Toggle, Select, Run }

/// <summary>A function on a structure's toolbar: its key (its kind's id, or "kind.block" for a slot per block type), its kind, and its block type for a slot per type.</summary>
public sealed record BarSlot(string Key, BarKind Kind, BlockType Type)
{
    public BarAct Act => StructureBar.ActOf(Kind);

    /// <summary>Its name on the slot: the kind's label, or its block's first word for a slot per type.</summary>
    public string Label => Kind.Label ?? Type?.Name.Split(' ')[0].ToUpperInvariant() ?? Kind.Id.ToUpperInvariant();
}

/// <summary>
/// A structure's toolbar in a command chair (design: Visor HUD layout; Empyrion's seated toolbar). Its slots fill from
/// the device catalogue (data/slice.json's structureBar): a kind shows once the structure has a block of one of its
/// functions, one slot for the kind or one per block type. Equipment fills from the left end and core systems from the
/// right; the pilot's arrangement (<see cref="Structure.Bar"/>) wins and travels with the structure and its blueprint.
/// A toggle switches its blocks' lamps, the core's gravity or one of the structure's systems (<see cref="Vessel.SystemOn"/>);
/// a gun slot is picked for the trigger, and its guns fire along their blocks' facing with core storage's ammo.
/// </summary>
public sealed class StructureBar
{
    public const int Slots = Slice.Items.PlayerInventory.ToolbarSlots;

    /// <summary>The systems the game itself reads by name: the core's gravity, life support, the fabricators and the shield.</summary>
    public const string Gravity = "gravity", LifeSupport = "life-support", Fabricators = "fabricators", Shields = "shields";

    readonly Vessel _v;
    List<BarSlot> _functions;
    readonly List<(Int3 At, BarSlot Slot)> _guns = new();
    readonly Dictionary<Int3, Gun> _loaded = new();
    /// <summary>Each gun block's seconds of firing left: a cadence after each shot, while it draws its power.</summary>
    readonly Dictionary<Int3, double> _firing = new();
    /// <summary>The switched kinds with any of their blocks on (a block switched changes the structure, so this follows).</summary>
    readonly HashSet<string> _lit = new(StringComparer.Ordinal);
    int _version = -1;

    /// <summary>The gun slots picked for the trigger, by key (the pilot's, not saved).</summary>
    public readonly HashSet<string> Picked = new(StringComparer.Ordinal);

    public StructureBar(Vessel v) => _v = v;

    IReadOnlyList<BarKind> Kinds => _v.Zone.Data.Rules.StructureBar.Kinds;

    public static BarAct ActOf(BarKind k) => k.Act switch
    {
        "toggle" => BarAct.Toggle,
        "select" => BarAct.Select,
        "run" => BarAct.Run,
        _ => throw new InvalidDataException($"structureBar kind '{k.Id}': act '{k.Act}' is not toggle, select or run"),
    };

    static bool Right(BarKind k) => k.End switch
    {
        "left" => false,
        "right" => true,
        _ => throw new InvalidDataException($"structureBar kind '{k.Id}': end '{k.End}' is not left or right"),
    };

    static bool Brings(BarKind k, BlockType t) => k.Functions.Contains(t.Function);

    /// <summary>The functions its blocks bring, in data order (worked out again only when the structure has changed).</summary>
    public IReadOnlyList<BarSlot> Functions
    {
        get
        {
            Structure s = _v.Structure;
            if (_version == s.Version) return _functions;
            _version = s.Version;
            var types = new HashSet<BlockType>();
            foreach (var (_, b) in s.Blocks()) types.Add(s.TypeOf(b));
            _functions = new List<BarSlot>();
            foreach (BarKind k in Kinds)
            {
                var brought = types.Where(t => Brings(k, t)).OrderBy(t => t.Index).ToList();
                if (brought.Count == 0) continue;
                if (k.PerType) _functions.AddRange(brought.Select(t => new BarSlot($"{k.Id}.{t.Id}", k, t)));
                else _functions.Add(new BarSlot(k.Id, k, null));
            }
            _guns.Clear();
            _lit.Clear();
            foreach (var (at, b) in s.Blocks())
            {
                BlockType t = s.TypeOf(b);
                if (_functions.FirstOrDefault(f => f.Act == BarAct.Select && f.Type == t) is BarSlot gun) _guns.Add((at, gun));
                if (t.Switches && !b.Off)
                    foreach (BarKind k in Kinds)
                        if (Brings(k, t)) _lit.Add(k.Id);
            }
            foreach (Int3 gone in _loaded.Keys.Where(c => !_guns.Any(g => g.At == c)).ToList()) _loaded.Remove(gone);
            Picked.RemoveWhere(k => _functions.All(f => f.Key != k));
            return _functions;
        }
    }

    /// <summary>The bar's slots, left to right (null: empty): the pilot's arrangement first, then the rest from their ends.</summary>
    public BarSlot[] Arranged()
    {
        var slots = new BarSlot[Slots];
        IReadOnlyList<BarSlot> functions = Functions;
        var placed = new HashSet<string>(StringComparer.Ordinal);
        string[] saved = _v.Structure.Bar;
        for (int i = 0; saved != null && i < Math.Min(saved.Length, Slots); i++)
            if (saved[i] != null && functions.FirstOrDefault(f => f.Key == saved[i]) is BarSlot f && placed.Add(f.Key)) slots[i] = f;
        foreach (BarSlot f in functions.Where(f => !Right(f.Kind) && !placed.Contains(f.Key)))
            if (Array.IndexOf(slots, null) is int free and >= 0) slots[free] = f;
        foreach (BarSlot f in functions.Where(f => Right(f.Kind) && !placed.Contains(f.Key)).Reverse())
            if (Array.LastIndexOf(slots, null) is int free and >= 0) slots[free] = f;
        return slots;
    }

    /// <summary>The pilot moves a slot's function to another slot (the two swap), and the arrangement is kept with the structure.</summary>
    public void Move(int from, int to)
    {
        if (from == to || from < 0 || to < 0 || from >= Slots || to >= Slots) return;
        string[] keys = Arranged().Select(f => f?.Key).ToArray();
        (keys[from], keys[to]) = (keys[to], keys[from]);
        _v.Structure.Bar = keys;
    }

    /// <summary>Is a slot on: a toggle switched on, a gun picked for the trigger? A run slot is never on.</summary>
    public bool IsOn(BarSlot f)
    {
        _ = Functions; // the switched blocks' state follows the structure
        return f.Act switch
        {
            BarAct.Select => Picked.Contains(f.Key),
            BarAct.Run => false,
            _ when f.Kind.Id == Gravity => _v.Structure.GravityOn,
            _ when Switched(f.Kind) => _lit.Contains(f.Kind.Id),
            _ => _v.SystemOn(f.Kind.Id),
        };
    }

    /// <summary>A kind whose blocks have switches of their own (spotlights, turrets): its state is their Off flags.</summary>
    bool Switched(BarKind k) => _v.Structure.Catalogue.Types.Any(t => t != null && t.Switches && Brings(k, t));

    /// <summary>A slot's number pressed: a toggle switches, a gun is picked or let go, a scanner scans.</summary>
    public void Press(Game.Game game, int slot)
    {
        if (slot < 0 || slot >= Slots || Arranged()[slot] is not BarSlot f) return;
        switch (f.Act)
        {
            case BarAct.Select:
                if (!Picked.Remove(f.Key)) Picked.Add(f.Key);
                return;
            case BarAct.Run:
                if (Powered(f.Kind)) game.Scan(_v); // the structure's scanners' scan, at their range
                return;
        }
        bool on = !IsOn(f);
        if (f.Kind.Id == Gravity) _v.SetGravity(on);
        else if (Switched(f.Kind)) _v.SwitchBlocks(t => Brings(f.Kind, t), on);
        else _v.SetSystem(f.Kind.Id, on);
    }

    /// <summary>Whether a kind of the bar's devices may work now: the structure has power to give (each draws its share of it as it works).</summary>
    public bool Powered(BarKind kind) => _v.Powered;

    /// <summary>Is the gun at this cell firing (a cadence after its last shot): it draws its power while it is.</summary>
    public bool Firing(Int3 at) => _firing.TryGetValue(at, out double left) && left > 0;

    /// <summary>The guns' firing runs down with time (the power tick calls this).</summary>
    public void Cool(double dt)
    {
        if (_firing.Count == 0) return;
        foreach (Int3 at in _firing.Keys.ToList())
            if ((_firing[at] -= dt) <= 0) _firing.Remove(at);
    }

    // ---- Guns -----------------------------------------------------------------------------------

    /// <summary>A gun block's magazine and cadence (shared/Arms' Gun), its ammo taken from core storage.</summary>
    Gun GunAt(Int3 at, WeaponDef def)
    {
        if (_loaded.TryGetValue(at, out Gun gun)) return gun;
        var items = _v.Zone.Data.Items;
        gun = new Gun(def)
        {
            Take = (id, n) => _v.Storage.Remove(id, n),
            Available = id => _v.Storage.Count(id),
            RoundsOf = id => items.TryGet(id, out var it) && it.Rounds > 0 ? it.Rounds : 1,
        };
        _loaded[at] = gun;
        return gun;
    }

    /// <summary>Rounds a gun slot has to fire: in its guns' magazines and in core storage.</summary>
    public int Rounds(BarSlot f)
    {
        if (f.Act != BarAct.Select || _v.Zone.Data.Mounts.OfItem(f.Type.Id) is not WeaponDef def) return 0;
        int loaded = _guns.Where(g => g.Slot == f && _loaded.ContainsKey(g.At)).Sum(g => _loaded[g.At].Loaded);
        var items = _v.Zone.Data.Items;
        return loaded + _v.Storage.Count(def.Ammo) * (items.TryGet(def.Ammo, out var ammo) && ammo.Rounds > 0 ? ammo.Rounds : 1);
    }

    /// <summary>
    /// One tick of the pilot's trigger: every gun of a picked slot fires along its block's facing from its muzzle, carried
    /// at the structure's own velocity, through shared/Arms' shots; the others only finish their reloads.
    /// </summary>
    public void Fire(Slice.Arms.ArmsHost arms, float dt, bool held, bool pressed)
    {
        _ = Functions;
        if (_guns.Count == 0) return;
        Structure s = _v.Structure;
        foreach (var (at, f) in _guns)
        {
            if (_v.Zone.Data.Mounts.OfItem(f.Type.Id) is not WeaponDef def || !s.TryGet(at, out Block b)) continue;
            bool on = Picked.Contains(f.Key) && Powered(f.Kind);
            Gun gun = GunAt(at, def);
            if (on && pressed && gun.Loaded == 0 && !gun.Reloading) gun.LoadNow(); // a gun aboard is kept loaded: it fires at once
            int shots = gun.Tick(dt, on && held, on && pressed, out _);
            if (shots <= 0) continue;
            _firing[at] = def.SecondsPerShot;
            float cell = s.Grid.CellSize;
            N.Vector3 muzzle = s.PointOf(at, b, new N.Vector3(0f, 0f, -0.6f)) * cell, back = s.PointOf(at, b, N.Vector3.Zero) * cell;
            Vector3 from = _v.ToWorld(muzzle), dir = (from - _v.ToWorld(back)).Normalized();
            for (int k = 0; k < shots; k++) arms.FireMounted(def, from, dir, _v.Body.Velocity.ToGodot(), _v);
        }
    }

    /// <summary>The muzzle of every gun on it, world (tests: where its shots leave from).</summary>
    public IEnumerable<Vector3> Muzzles()
    {
        _ = Functions;
        Structure s = _v.Structure;
        foreach (var (at, _) in _guns)
            if (s.TryGet(at, out Block b)) yield return _v.ToWorld(s.PointOf(at, b, new N.Vector3(0f, 0f, -0.6f)) * s.Grid.CellSize);
    }
}
