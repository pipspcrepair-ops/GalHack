using System;
using System.Collections.Generic;
using System.Linq;
using DiegeticUi.Theme;
using GalHack.Items;
using GalHack.Items.Production;
using GalHack.Physics.Engine;
using GalHack.Structures;
using GalHack.Structures.Presentation;
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
            g.Caption("NO CORE IN RANGE", o, area.Size.X, C.Warn);
            return;
        }
        // The chooser.
        // Every core whose area you are in: pick which one to manage.
        int shown = Math.Min(near.Count, PerRow * MaxRows), rows = (shown + PerRow - 1) / PerRow;
        for (int i = 0; i < shown; i++)
        {
            string name = near[i] switch { Vessel v => v.Name, Device d => d.Label, _ => "?" };
            if (g.Tab($"core:pick:{i}", new Rect2(o + new Vector2(i % PerRow * 210, i / PerRow * 40), new Vector2(200, 36)), HudLayout.Fit(UiTheme.Label, 18, name.ToUpperInvariant(), 186), near[i] == _picked, C.Accent)) Pick(near[i]);
        }
        float top = 50 + (rows - 1) * 40;
        var body = new Rect2(o + new Vector2(0, top), new Vector2(area.Size.X, area.Size.Y - top));
        if (_picked is Device device)
        {
            _constructor.Build(g, new Rect2(body.Position, new Vector2(700, body.Size.Y)));
            DeviceStorage(g, new Rect2(body.Position + new Vector2(730, 0), new Vector2(area.End.X - body.Position.X - 730, body.Size.Y)), device);
            return;
        }
        var v2 = (Vessel)_picked;
        // Side by side: the structure's settings and numbers, its constructor and queue, its storage.
        _structure.Build(g, new Rect2(body.Position, new Vector2(400, body.Size.Y)));
        var machines = new Rect2(body.Position + new Vector2(420, 0), new Vector2(620, body.Size.Y));
        if (v2.Machines.Count == 0)
        {
            g.Caption("MANUFACTURING", machines.Position, machines.Size.X);
            g.Text("NONE", machines.Position + new Vector2(0, 40), C.Dim, 15);
        }
        else
        {
            for (int i = 0; i < v2.Machines.Count; i++)
            {
                Constructor c = v2.Machines.Values.ElementAt(i);
                if (g.Tab($"core:machine:{i}", new Rect2(machines.Position + new Vector2(i * 140, 0), new Vector2(130, 30)), c.Def.Tag, i == _machine, C.Accent2)) _machine = i;
            }
            if (!v2.Powered) g.Text(v2.PowerOn ? "NO POWER" : "POWER OFF", machines.Position + new Vector2(300, 6), C.Danger, 15);
            _constructor.Build(g, new Rect2(machines.Position + new Vector2(0, 40), new Vector2(machines.Size.X, machines.Size.Y - 40)));
        }
        var column = new Rect2(body.Position + new Vector2(1060, 0), new Vector2(area.End.X - body.Position.X - 1060, body.Size.Y));
        StorageColumn(g, column, v2.StorageGrid, v2.Name, () => _game.Open(new OpenContainer
        {
            Title = $"{v2.Name} storage", Grid = v2.StorageGrid, Vessel = v2, Where = () => v2.Body.Position.ToGodot(),
        }));
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
        // As many rows as the column has room for, above its buttons (and the blueprint button under them).
        float cellsTop = r.Position.Y + 66 + FilterBar.Height;
        int visRows = Math.Max(4, (int)((r.End.Y - 44 - 26 - 120 - cellsTop) / 56));
        var cells = new Rect2(new Vector2(r.Position.X, cellsTop), new Vector2(r.Size.X, visRows * 56 - 6));
        IReadOnlyList<int> shownSlots = view.Visible(grid);
        int rows = Math.Max(1, (shownSlots.Count + columns - 1) / columns);
        _storageScroll = Math.Clamp(_storageScroll, 0, Math.Max(0, rows - visRows));
        Slots.Grid(g, _game, "store", grid, 0, grid.Count, columns, cells, 50, _game.Pack.Grid, _storageScroll, view);
        float y = cells.End.Y + 6;
        g.Text($"{s.Stacks.Count()} KINDS{(view.Filtering ? $" · {shownSlots.Count} SHOWN" : "")}", new Vector2(r.Position.X, y), C.Dim, 13);
        if (rows > visRows)
        {
            if (g.Button("store:up", new Rect2(r.End.X - 146, y - 2, 70, 24), "UP", C.Accent2, _storageScroll > 0, size: 12)) _storageScroll--;
            if (g.Button("store:down", new Rect2(r.End.X - 72, y - 2, 70, 24), "DOWN", C.Accent2, _storageScroll + visRows < rows, size: 12)) _storageScroll++;
        }
        y += 26;
        var only = view.Filtering ? new HashSet<int>(shownSlots) : null;
        float half = (r.Size.X - 8) / 2;
        if (g.Button("storage:store-all", new Rect2(r.Position.X + half + 8, y, half, 34), "STORE BAG", C.Accent2, size: 14))
        {
            Bill moved = ItemGrid.MoveAll(_game.Pack.Grid, grid, false);
            if (moved.IsEmpty) _game.Say("NOTHING STORED", Tones.Caution);
        }
        if (g.Button("storage:store-raw", new Rect2(r.Position.X, y, half, 34), "STORE ORE + MATS", C.Accent2, size: 14))
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
            if (moved.IsEmpty) _game.Say("NOTHING STORED", Tones.Caution);
        }
        if (g.Button($"storage:take-made", new Rect2(r.Position.X, y + 42, r.Size.X, 34), "TAKE MADE ITEMS", C.Good, size: 14))
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
            if (moved.IsEmpty) _game.Say("NOTHING TAKEN", Tones.Caution);
        }
        if (g.Button($"storage:open", new Rect2(r.Position.X, y + 84, r.Size.X, 34), "OPEN BY BAG", C.Accent)) open();
    }
}

/// <summary>
/// A structure's settings and numbers: core mode, gravity, Home, auto-transmit, power, lights; mass,
/// power, CPU, storage, and a vessel's four numbers. The mode switch is here on the Tab menu's CORE
/// tab and nowhere else, in its warning stripes: it works at any time, even in flight, and nothing
/// refuses it. It warns about missing parts and leaves the choice to the player.
/// </summary>
public sealed class StructureSection : IMenuSection
{
    static Palette C => UiTheme.Colors;
    readonly Game.Game _game;
    readonly Func<Vessel> _vessel;
    readonly bool _modeSwitch;

    /// <param name="modeSwitch">Show the mode switch: true on the Tab menu only (a screen in the world just says the mode).</param>
    public StructureSection(Game.Game game, Func<Vessel> vessel, bool modeSwitch = true)
    {
        _game = game;
        _vessel = vessel;
        _modeSwitch = modeSwitch;
    }

    public Vector2 PreferredSize => new(500, 900);

    /// <summary>The switch, in its stripes: one button a mode, the one it is in lit, and what each mode still lacks. Returns the y below it.</summary>
    float ModeSwitch(Gui g, Vessel v, Vector2 at, float width)
    {
        const float Band = 14, Rows = 140;
        Structure s = v.Structure;
        var panel = new Rect2(at, new Vector2(width, Rows));
        g.Stripes(panel, Band, C.Warn);
        Rect2 inner = panel.Grow(-Band);
        g.Fill(inner, new Color(C.Glass, 0.92f));
        CoreMode[] modes = ModeFit.Modes;
        float each = (inner.Size.X - 6 * (modes.Length + 1)) / modes.Length;
        for (int i = 0; i < modes.Length; i++)
        {
            CoreMode m = modes[i];
            var r = new Rect2(inner.Position.X + 6 + i * (each + 6), inner.Position.Y + 6, each, 36);
            if (g.Button($"core:mode:{m.ToString().ToLowerInvariant()}", r, ModeFit.Name(m), C.Warn, lit: s.Mode == m, size: 16) && s.Mode != m) Switch(v, m);
        }
        float y = inner.Position.Y + 48;
        foreach (CoreMode m in modes)
        {
            string warning = ModeFit.Warning(m, v.MissingFor(m));
            string line = warning ?? $"{ModeFit.Name(m)}: READY";
            g.TextIn(HudLayout.Fit(UiTheme.Label, 12, line, inner.Size.X - 16), new Rect2(inner.Position.X + 8, y, inner.Size.X - 16, 16), warning == null ? C.Good : C.Warn, 12);
            y += 18;
        }
        return panel.End.Y + 8;
    }

    /// <summary>Whatever the structure is doing, it switches. The console says what that will mean.</summary>
    void Switch(Vessel v, CoreMode mode)
    {
        v.SwitchMode(mode);
        string warning = v.ModeWarning;
        // The panel shows the mode; only a problem with it is said.
        if (warning != null) _game.Say(warning, Tones.Caution);
    }

    /// <summary>Colour, mode and speed for the structure's lights: the one picked with Shift+E, else all of them. Quiet: three short rows.</summary>
    float LightStyle(Gui g, Vessel v, Vector2 at, float width)
    {
        bool any = v.Structure.Blocks().Any(b => v.Structure.TypeOf(b.block).IsLight);
        if (!any) return at.Y;
        Int3? one = _game.LightEdit is Int3 c && v.Structure.TryGet(c, out Block lb) && v.Structure.TypeOf(lb).IsLight ? c : null;
        LightSettings cur = one is Int3 cell ? v.Structure.LightAt(cell) : v.Structure.Blocks().Where(b => v.Structure.TypeOf(b.block).IsLight).Select(b => v.Structure.LightAt(b.pos)).First();
        float y = at.Y;
        if (g.Button("light:target", new Rect2(at.X, y, width, 24), one != null ? "ONE LIGHT" : "ALL LIGHTS", C.Dim, size: 12) && one != null) _game.LightEdit = null;
        y += 28;
        uint[] pal = LightSettings.Palette;
        float sw = (width - (pal.Length - 1) * 4) / pal.Length;
        for (int i = 0; i < pal.Length; i++)
            if (g.Button($"light:col:{i}", new Rect2(at.X + i * (sw + 4), y, sw, 26), "", ChunkMesher.Rgb(pal[i]), lit: cur.Color == pal[i], size: 12))
                v.StyleLights(one, s => s.WithColor(pal[i]));
        y += 32;
        LightAnim[] modes = Enum.GetValues<LightAnim>();
        float mw = (width - (modes.Length - 1) * 4) / modes.Length;
        for (int i = 0; i < modes.Length; i++)
        {
            LightAnim m = modes[i];
            if (g.Button($"light:mode:{i}", new Rect2(at.X + i * (mw + 4), y, mw, 28), m.ToString().ToUpperInvariant(), C.Accent2, lit: cur.Anim == m, size: 12))
                v.StyleLights(one, s => (m == LightAnim.Fade && s.With(m).Color2 == LightSettings.Steady(s.Color).Color2 ? s.With(m).WithColor2(pal[(Array.IndexOf(pal, s.Color) + 5) % pal.Length]) : s.With(m)).WithSeed(one is Int3 p ? (p.X * 7 + p.Y * 13 + p.Z * 29) : 0));
        }
        y += 34;
        for (int i = 0; i < LightSettings.Speeds.Length; i++)
        {
            float sp = LightSettings.Speeds[i];
            float spw = (width - 8) / 3;
            if (g.Button($"light:speed:{i}", new Rect2(at.X + i * (spw + 4), y, spw, 26), LightSettings.SpeedNames[i], C.Accent2, lit: MathF.Abs(cur.Speed - sp) < 0.01f, size: 12))
                v.StyleLights(one, s => s.WithSpeed(sp));
        }
        return y + 36;
    }

    public void Build(Gui g, Rect2 area)
    {
        Vessel v = _vessel();
        Vector2 o = area.Position;
        if (v == null) return;
        Structure s = v.Structure;
        g.Caption($"{v.Name.ToUpperInvariant()} · {BlueprintsSection.ModeLabel(s.Mode)}", o, area.Size.X);
        float y = o.Y + 36;

        // ---- Settings ---------------------------------------------------------------------------
        bool Toggle(string id, string label, bool on, bool enabled = true)
        {
            bool clicked = g.Button(id, new Rect2(o.X, y, area.Size.X, 32), $"{label}: {(on ? "ON" : "OFF")}", on ? C.Good : C.Accent2, enabled, lit: on, size: 15);
            y += 38;
            return clicked;
        }
        if (_modeSwitch) y = ModeSwitch(g, v, new Vector2(o.X, y), area.Size.X);
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
        y = LightStyle(g, v, new Vector2(o.X, y), area.Size.X);
        if (Toggle("core:home", "HOME", v.Home))
        {
            bool now = !v.Home;
            foreach (Vessel other in _game.Zone.Vessels) other.Home = false; // one Home at a time
            v.Home = now;
        }
        if (Toggle("core:transmit", "AUTO-TRANSMIT TO HOME", v.AutoTransmit)) v.AutoTransmit = !v.AutoTransmit;

        // ---- Numbers ----------------------------------------------------------------------------
        StructureStats st = v.StructureStats;
        y += 8;
        g.Caption("NUMBERS", new Vector2(o.X, y), area.Size.X, C.Holo);
        y += 34;
        void Row(string label, string value, Color colour)
        {
            g.Text(label, new Vector2(o.X, y), C.Dim, 14);
            g.TextIn(HudLayout.Fit(UiTheme.Mono, 15, value, area.Size.X - 150), new Rect2(o.X + 150, y - 1, area.Size.X - 150, 20), colour, 15, HorizontalAlignment.Left, UiTheme.Mono);
            y += 26;
        }
        Row("MASS", $"{st.Mass:F2} t · {st.Blocks} BLOCKS", C.Text);
        Row("POWER", st.Generators == 0 ? "NO GENERATOR" : $"{st.PowerOut:F2} MW OUT · {st.PowerUse:F2} MW USE", st.PowerOut >= st.PowerUse ? C.Good : C.Warn);
        Row("STORAGE", $"{v.Storage.UsedSu:F0} / {st.StorageCapacity:F0} SU", C.Text);
        if (v.Stats is HandlingStats h && !v.IsBase)
        {
            Row("ACCEL F / B", $"{h.Accel(Direction.Forward):F1} / {h.Accel(Direction.Back):F1} m/s²", C.Text);
            Row("ACCEL UP / SIDE", $"{h.Accel(Direction.Up):F1} / {h.Accel(Direction.Right):F1} m/s²", C.Text);
            Row("TURN", h.TurnCapped ? $"{h.TurnRate:F0} °/s · CAPPED" : $"{h.TurnRate:F0} °/s", C.Text);
            Row("TOP SPEED", $"{h.TopSpeed:F0} m/s", C.Text);
            // Too heavy says so in the row itself: a line of its own ran the page onto the alerts' row.
            Row("LIFT MARGIN", h.CanLiftOff ? $"{h.LiftMargin:+0;-0} kN · {h.SpareLift:F1} t SPARE" : $"{h.LiftMargin:+0;-0} kN · TOO HEAVY", h.CanLiftOff ? C.Good : C.Danger);
        }
    }
}
