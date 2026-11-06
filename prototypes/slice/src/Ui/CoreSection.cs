using System;
using System.Collections.Generic;
using System.Linq;
using DiegeticUi.Theme;
using GalHack.Items;
using GalHack.Items.Production;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using ShipHandling.Handling;
using Slice.Game;
using Slice.Items;
using Slice.Vessels;
using Palette = DiegeticUi.Theme.Palette;

namespace Slice.Ui;

/// <summary>
/// CORE: the systems of the core nearby, the structure you are in or next to (a base, a vessel,
/// the pod) or a device you stand by: its manufacturing (every constructor on it, with ammo
/// restock), its storage, its settings (core mode, gravity, Home, auto-transmit, power, lights)
/// and its numbers (power, CPU, mass; for a vessel the four handling numbers and lift margin).
/// With several near, the one you are in or the nearest is picked; the row of buttons switches.
/// </summary>
public sealed class CoreSection : IMenuSection
{
    static Palette C => UiTheme.Colors;
    readonly Game.Game _game;
    object _picked;
    int _machine;
    readonly ConstructorSection _constructor;
    int _storageScroll;
    readonly StructureSection _structure;

    /// <summary>How near a device must be to show here, m. A structure shows anywhere inside its own area (<see cref="Slice.Construction.StructureArea"/>), however large.</summary>
    public const float DeviceReach = 6f;

    /// <summary>The chooser's buttons a row, and rows.</summary>
    const int PerRow = 6, MaxRows = 3;

    public CoreSection(Game.Game game)
    {
        _game = game;
        _constructor = new ConstructorSection(game, () => CurrentMachine, "MANUFACTURING", "core");
        _structure = new StructureSection(game, () => _picked as Vessel);
    }

    public Vector2 PreferredSize => new(1560, 790);

    /// <summary>The cores near you: what you are in first, then nearest first.</summary>
    public List<object> Near()
    {
        var near = new List<(object thing, float d)>();
        Vector3 feet = _game.Survivor.WorldFeet;
        // A structure's panels work inside its area (bases, capital, small and hover vessels alike).
        var rules = _game.Zone.Data.Rules.Build.Area;
        foreach (Vessel v in _game.Zone.Vessels)
        {
            if (v.IsWreck || v.Structure.Core == null) continue;
            bool aboard = _game.Survivor.Aboard == v || _game.Survivor.Seated == v;
            if (aboard || Slice.Construction.StructureArea.Contains(v, feet, rules)) near.Add((v, aboard ? -1f : Slice.Construction.StructureArea.Distance(v, feet)));
        }
        foreach (Device dev in _game.Zone.Devices)
        {
            if (dev.Constructor == null) continue; // a climate node has a hopper, not a panel
            float d = dev.GlobalPosition.DistanceTo(feet);
            if (d <= DeviceReach) near.Add((dev, d));
        }
        return near.OrderBy(n => n.d).Select(n => n.thing).ToList();
    }

    /// <summary>The structure or device the panel is on.</summary>
    public object Picked => _picked;

    public void Pick(object thing, int index = 0)
    {
        _picked = thing;
        _machine = index;
    }

    Constructor CurrentMachine => _picked switch
    {
        Device d => d.Constructor,
        Vessel v when v.Machines.Count > 0 => v.Machines.Values.ElementAt(Math.Clamp(_machine, 0, v.Machines.Count - 1)),
        _ => null,
    };

    public void Build(Gui g, Rect2 area)
    {
        List<object> near = Near();
        if (_picked == null || !near.Contains(_picked)) _picked = near.FirstOrDefault();
        Vector2 o = area.Position;
        if (_picked == null)
        {
            g.Caption("NO CORE NEARBY", o, area.Size.X);
            g.Text("OUT OF RANGE: a structure's core, storage and constructors work anywhere inside its area; step into one.", o + new Vector2(0, 40), C.Warn, 16);
            g.Text("Craft a survival constructor in the suit fabricator (YOU), put it on the toolbar and set it down.", o + new Vector2(0, 66), C.Dim, 16);
            return;
        }
        // The chooser.
        // Every core whose area you are in: pick which one to manage.
        int shown = Math.Min(near.Count, PerRow * MaxRows), rows = (shown + PerRow - 1) / PerRow;
        for (int i = 0; i < shown; i++)
        {
            string name = near[i] switch { Vessel v => v.Name, Device d => d.Label, _ => "?" };
            if (g.Tab($"core:pick:{i}", new Rect2(o + new Vector2(i % PerRow * 210, i / PerRow * 40), new Vector2(200, 36)), name.ToUpperInvariant(), near[i] == _picked, C.Accent)) Pick(near[i]);
        }
        float top = 50 + (rows - 1) * 40;
        var body = new Rect2(o + new Vector2(0, top), new Vector2(area.Size.X, area.Size.Y - top));
        if (_picked is Device device)
        {
            _constructor.Build(g, new Rect2(body.Position, new Vector2(560, body.Size.Y)));
            DeviceStorage(g, new Rect2(body.Position + new Vector2(600, 0), new Vector2(560, body.Size.Y)), device);
            return;
        }
        var v2 = (Vessel)_picked;
        _structure.Build(g, new Rect2(body.Position, new Vector2(500, body.Size.Y)));
        var machines = new Rect2(body.Position + new Vector2(530, 0), new Vector2(540, body.Size.Y));
        if (v2.Machines.Count == 0)
        {
            g.Caption("MANUFACTURING", machines.Position, machines.Size.X);
            g.Text(v2.IsBase ? "No constructor on this base yet: craft a Small constructor (large) and place it." : "No constructor aboard.", machines.Position + new Vector2(0, 40), C.Dim, 15);
        }
        else
        {
            for (int i = 0; i < v2.Machines.Count; i++)
            {
                Constructor c = v2.Machines.Values.ElementAt(i);
                if (g.Tab($"core:machine:{i}", new Rect2(machines.Position + new Vector2(i * 140, 0), new Vector2(130, 30)), c.Def.Tag, i == _machine, C.Accent2)) _machine = i;
            }
            if (!v2.Powered) g.Text(v2.PowerOn ? "NO POWER: build a generator onto it" : "POWER OFF", machines.Position + new Vector2(300, 6), C.Danger, 15);
            _constructor.Build(g, new Rect2(machines.Position + new Vector2(0, 40), new Vector2(machines.Size.X, machines.Size.Y - 40)));
        }
        var column = new Rect2(body.Position + new Vector2(1100, 0), new Vector2(area.End.X - body.Position.X - 1100, body.Size.Y));
        StorageColumn(g, column, v2.StorageGrid, v2.Name, () => _game.Open(new OpenContainer
        {
            Title = $"{v2.Name} storage", Grid = v2.StorageGrid, Vessel = v2, Where = () => v2.Body.Position.ToGodot(),
        }));
        // Blueprints live here in the menu, never on the toolbar: building by hand is the game.
        if (g.Button("core:blueprint", new Rect2(column.Position.X, column.End.Y - 40, column.Size.X, 30), "BLUEPRINTS: PROJECT THE SKIFF", C.Holo, size: 13))
        {
            _game.Builder.Project();
            _game.Menu.Close();
        }
    }

    void DeviceStorage(Gui g, Rect2 r, Device d) =>
        StorageColumn(g, r, d.Grid, $"{d.Label} hopper", () => _game.Open(new OpenContainer { Title = $"{d.Label} hopper", Grid = d.Grid, Device = d, Where = () => d.GlobalPosition }));

    /// <summary>
    /// A storage's stacks through the shared filter bar (text, categories, sort), each draggable
    /// onto your bag or the structure toolbar, with quick moves to and from your bag (acting on
    /// what is shown while a filter is on) and a button to open it beside your bag.
    /// </summary>
    void StorageColumn(Gui g, Rect2 r, ItemGrid grid, string name, Action open)
    {
        g.Caption("STORAGE", r.Position, r.Size.X);
        grid.Reconcile();
        SharedStorage s = grid.Storage;
        g.Text($"{s.UsedSu:F0} / {s.CapacitySu:F0} SU · {s.MassKg:F0} kg", r.Position + new Vector2(0, 30), C.Text, 13, UiTheme.Mono);
        g.Bar(new Rect2(r.Position + new Vector2(0, 50), new Vector2(r.Size.X, 8)), s.UsedSu, Math.Max(1, s.CapacitySu), C.Accent2, 16);
        ItemView view = FilterBar.Build(g, "storage", new Rect2(r.Position + new Vector2(0, 64), new Vector2(r.Size.X, FilterBar.Height)));
        int columns = Math.Max(1, (int)((r.Size.X + 6) / 56));
        var cells = new Rect2(r.Position + new Vector2(0, 66 + FilterBar.Height), new Vector2(r.Size.X, 4 * 56 - 6));
        IReadOnlyList<int> shownSlots = view.Visible(grid);
        int rows = Math.Max(1, (shownSlots.Count + columns - 1) / columns);
        _storageScroll = Math.Clamp(_storageScroll, 0, Math.Max(0, rows - 4));
        Slots.Grid(g, _game, "store", grid, 0, grid.Count, columns, cells, 50, _game.Pack.Grid, _storageScroll, view);
        float y = cells.End.Y + 6;
        if (s.IsEmpty) g.Text("empty", new Vector2(r.Position.X, y), C.Dim, 13);
        g.Text($"{s.Stacks.Count()} kinds{(view.Filtering ? $" · {shownSlots.Count} stacks shown" : "")}", new Vector2(r.Position.X + 60, y), C.Dim, 13);
        if (rows > 4)
        {
            if (g.Button("store:up", new Rect2(r.End.X - 130, y - 2, 60, 24), "UP", C.Accent2, _storageScroll > 0, size: 12)) _storageScroll--;
            if (g.Button("store:down", new Rect2(r.End.X - 64, y - 2, 60, 24), "DOWN", C.Accent2, _storageScroll + 4 < rows, size: 12)) _storageScroll++;
        }
        y += 26;
        var only = view.Filtering ? new HashSet<int>(shownSlots) : null;
        float half = (r.Size.X - 8) / 2;
        if (g.Button("storage:store-all", new Rect2(r.Position.X + half + 8, y, half, 34), "STORE ALL MY BAG", C.Accent2, size: 14))
        {
            Bill moved = ItemGrid.MoveAll(_game.Pack.Grid, grid, false);
            _game.Say(moved.IsEmpty ? "Nothing in your bag to store (or no room; the toolbar stays with you)" : $"Stored {s.Catalogue.Describe(moved, 4)}", moved.IsEmpty ? Tones.Caution : Tones.Good);
        }
        if (g.Button("storage:store-raw", new Rect2(r.Position.X, y, half, 34), "STORE ORE, MATERIALS", C.Accent2, size: 14))
        {
            var moved = new Bill();
            PlayerInventory pack = _game.Pack;
            for (int i = pack.Grid.Toolbar; i < pack.Grid.Count; i++)
            {
                Slot slot = pack.Grid[i];
                if (slot.Item == null) continue;
                ItemDef item = s.Catalogue.Item(slot.Item);
                if (item.Category is not (ItemCategory.Raw or ItemCategory.Refined or ItemCategory.Part)) continue;
                string id = slot.Item;
                int n = ItemGrid.QuickMove(pack.Grid, i, grid);
                if (n > 0) moved.Add(id, n);
            }
            _game.Say(moved.IsEmpty ? "No ore or materials in your bag (or no room)" : $"Stored {s.Catalogue.Describe(moved, 4)}", moved.IsEmpty ? Tones.Caution : Tones.Good);
        }
        if (g.Button($"storage:take-made", new Rect2(r.Position.X, y + 42, r.Size.X, 34), "TAKE BLOCKS, CORES, DEVICES AND SUPPLIES", C.Good, size: 14))
        {
            var moved = new Bill();
            for (int i = 0; i < grid.Count; i++)
            {
                Slot slot = grid[i];
                if (slot.Item == null || (only != null && !only.Contains(i))) continue;
                ItemDef item = s.Catalogue.Item(slot.Item);
                if (item.Category is ItemCategory.Raw or ItemCategory.Refined or ItemCategory.Part) continue;
                string id = slot.Item;
                int n = ItemGrid.QuickMove(grid, i, _game.Pack.Grid);
                if (n > 0) moved.Add(id, n);
            }
            _game.Say(moved.IsEmpty ? "Nothing made here to take yet" : $"Took {s.Catalogue.Describe(moved, 4)}{FilterBar.Shown(view, only?.Count ?? 0)}", moved.IsEmpty ? Tones.Caution : Tones.Good);
        }
        if (g.Button($"storage:open", new Rect2(r.Position.X, y + 84, r.Size.X, 34), "OPEN BESIDE MY BAG", C.Accent)) open();
    }
}

/// <summary>A structure's settings and numbers: core mode, gravity, Home, auto-transmit, power, lights; mass, power, CPU, storage, and a vessel's four numbers.</summary>
public sealed class StructureSection : IMenuSection
{
    static Palette C => UiTheme.Colors;
    readonly Game.Game _game;
    readonly Func<Vessel> _vessel;

    public StructureSection(Game.Game game, Func<Vessel> vessel)
    {
        _game = game;
        _vessel = vessel;
    }

    public Vector2 PreferredSize => new(500, 740);

    public void Build(Gui g, Rect2 area)
    {
        Vessel v = _vessel();
        Vector2 o = area.Position;
        if (v == null) return;
        Structure s = v.Structure;
        bool large = s.Grid.Size == GridSize.Large;
        string kind = v.IsBase ? "BASE" : large ? "CAPITAL VESSEL" : "SMALL VESSEL";
        g.Caption($"{v.Name.ToUpperInvariant()} · {kind}", o, area.Size.X);
        float y = o.Y + 36;

        // ---- Settings ---------------------------------------------------------------------------
        bool Toggle(string id, string label, bool on, bool enabled = true)
        {
            bool clicked = g.Button(id, new Rect2(o.X, y, area.Size.X, 32), $"{label}: {(on ? "ON" : "OFF")}", on ? C.Good : C.Accent2, enabled, lit: on, size: 15);
            y += 38;
            return clicked;
        }
        if (large)
        {
            bool capitalOk = _game.Progress.BlockAllowed("thruster", small: false);
            if (g.Button("core:mode", new Rect2(o.X, y, area.Size.X, 32), $"CORE MODE: {(s.Mode == CoreMode.Base ? "BASE" : "CAPITAL")}", C.Accent, enabled: s.Mode == CoreMode.Capital || capitalOk, size: 15))
            {
                Building.ToggleCoreMode(s);
                _game.Say(s.Mode == CoreMode.Capital ? $"{v.Name}: capital mode (it stays anchored until it has the thrust to lift off)" : $"{v.Name}: base mode, anchored", Tones.Text);
            }
            if (!capitalOk && s.Mode == CoreMode.Base) g.Text("capital mode: " + _game.Progress.LockReason("thruster@large"), new Vector2(o.X + 8, y + 34), C.Warn, 11);
            y += 50;
        }
        // The pilot role: whoever holds it flies, and their seat is forward. It can go to anyone in a seat.
        if (v.Flight != null && v.Roles.Seated.Count > 0)
        {
            var seated = v.Roles.Seated.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
            string next = seated[(seated.IndexOf(v.Roles.Pilot ?? seated[0]) + 1) % seated.Count];
            if (g.Button("core:pilot", new Rect2(o.X, y, area.Size.X, 32), $"PILOT: {(v.Roles.Pilot ?? "nobody").ToUpperInvariant()}{(seated.Count > 1 ? " (HAND OVER)" : "")}", C.Accent2, enabled: seated.Count > 1, size: 15)) v.Roles.Assign(next);
            y += 38;
        }
        if (Toggle("core:gravity", "CORE GRAVITY", s.GravityOn))
        {
            s.GravityOn = !s.GravityOn;
            if (v.Body.Core != null)
            {
                v.Body.Core.On = s.GravityOn;
                v.Cabin.SetGravity(v.Body.Core);
            }
        }
        if (Toggle("core:power", "POWER", v.PowerOn)) v.PowerOn = !v.PowerOn;
        if (Toggle("core:lights", "LIGHTS", v.LightsOn)) v.LightsOn = !v.LightsOn;
        if (large && Toggle("core:home", "HOME", v.Home))
        {
            bool now = !v.Home;
            foreach (Vessel other in _game.Zone.Vessels) other.Home = false; // one Home at a time
            v.Home = now;
            if (now) _game.Say($"{v.Name} is Home: vessels with auto-transmit send their haul here", Tones.Good);
        }
        if (!large && Toggle("core:transmit", "AUTO-TRANSMIT TO HOME", v.AutoTransmit)) v.AutoTransmit = !v.AutoTransmit;

        // ---- Numbers ----------------------------------------------------------------------------
        StructureStats st = v.StructureStats;
        y += 8;
        g.Caption("NUMBERS", new Vector2(o.X, y), area.Size.X, C.Holo);
        y += 34;
        void Row(string label, string value, Color colour)
        {
            g.Text(label, new Vector2(o.X, y), C.Dim, 14);
            g.Text(value, new Vector2(o.X + 190, y - 1), colour, 16, UiTheme.Mono);
            y += 26;
        }
        Row("MASS", $"{st.Mass:F2} t ({st.Blocks} blocks)", C.Text);
        Row("POWER", st.Generators == 0 ? "no generator" : $"{st.PowerOut:F2} MW out, {st.PowerUse:F2} MW use", st.PowerOut >= st.PowerUse ? C.Good : C.Warn);
        Row("CPU", $"{st.CpuUsed:N0} / {st.CpuLimit:N0} (tier {st.Tier})", st.Efficiency >= 1 ? C.Text : C.Warn);
        Row("STORAGE", $"{v.Storage.UsedSu:F0} / {st.StorageCapacity:F0} SU", C.Text);
        if (v.Stats is HandlingStats h && !v.IsBase)
        {
            float cap = _game.Zone.Data.Rules.Build.SmallVesselMaxTurn;
            Row("ACCEL F / B", $"{h.Accel(Direction.Forward):F1} / {h.Accel(Direction.Back):F1} m/s²", C.Text);
            Row("ACCEL UP / SIDE", $"{h.Accel(Direction.Up):F1} / {h.Accel(Direction.Right):F1} m/s²", C.Text);
            Row("TURN", large || h.TurnRate <= cap ? $"{h.TurnRate:F0} °/s" : $"{cap:F0} °/s (capped from {h.TurnRate:F0})", C.Text);
            Row("TOP SPEED", $"{h.TopSpeed:F0} m/s", C.Text);
            Row("LIFT MARGIN", $"{h.LiftMargin:+0;-0} kN ({h.SpareLift:F1} t spare)", h.CanLiftOff ? C.Good : C.Danger);
            if (!h.CanLiftOff && !string.IsNullOrEmpty(h.LiftWarning)) { g.Text(h.LiftWarning, new Vector2(o.X, y), C.Warn, 12); y += 20; }
        }
    }
}
