using System;
using System.Collections.Generic;
using System.Linq;
using DiegeticUi.Logic;
using DiegeticUi.Screens;
using DiegeticUi.Theme;
using Godot;

namespace DiegeticUi.Pages;

/// <summary>
/// The wrist-mounted suit computer. GEAR: the inventory on a clean grid with mass and volume
/// totals, the armor and booster slots, and a row of hazard lamps saying what the worn gear
/// covers. CODEX: one entry, read out by the suit's assistant (text for now).
/// </summary>
public partial class WristPage : Control, IScreenPage
{
    static Palette C => UiTheme.Colors;

    public const int Width = 720, Height = 450;
    const int Cols = 4, Rows = 3;
    const float TileW = 96, TileH = 84, TileGap = 8, GridX = 12, GridY = 70;

    public enum Tab { Gear, Codex }

    const string CodexTitle = "CORE GRAVITY";
    const string CodexText =
        "Your ship makes its own down. The core gravity generator under the bridge pulls everything " +
        "aboard toward the deck. Switch it off at the systems console and loose cargo floats free, " +
        "while your boots lock to the deck. It draws 34 kW: on a tight power budget, it is the first " +
        "thing to shed.";

    static readonly (Hazard Hazard, string Label)[] HazardLamps =
    {
        (Hazard.Cold, "COLD"), (Hazard.Heat, "HEAT"), (Hazard.Radiation, "RAD"), (Hazard.Toxic, "TOX"), (Hazard.Vacuum, "VAC"),
    };

    readonly Inventory _suit;
    readonly SuitLoadout _loadout;
    readonly Func<Hazard> _hazardsHere;
    readonly BlockButton _gearTab, _codexTab;

    public Tab Current { get; private set; } = Tab.Gear;
    int _hover = -1;
    (int, int, Hazard, Tab, int) _seen;
    double _typed;            // characters of the codex shown so far
    double _lastTick;

    public WristPage(Inventory suit, SuitLoadout loadout, Func<Hazard> hazardsHere)
    {
        _suit = suit;
        _loadout = loadout;
        _hazardsHere = hazardsHere;
        MouseFilter = MouseFilterEnum.Stop; // the grid's hover is handled here

        _gearTab = new BlockButton { Text = "GEAR", Colour = C.Accent, Position = new Vector2(8, 6), Size = new Vector2(140, 46) };
        _codexTab = new BlockButton { Text = "CODEX", Colour = C.Holo, Position = new Vector2(156, 6), Size = new Vector2(150, 46) };
        _gearTab.Pressed += () => Show(Tab.Gear);
        _codexTab.Pressed += () => Show(Tab.Codex);
        AddChild(_gearTab);
        AddChild(_codexTab);
        Show(Tab.Gear);
    }

    public void Show(Tab tab)
    {
        Current = tab;
        _gearTab.Selected = tab == Tab.Gear;
        _codexTab.Selected = tab == Tab.Codex;
        _gearTab.Changed();
        _codexTab.Changed();
        if (tab == Tab.Codex) _typed = 0;
        QueueRedraw();
        WorldScreen.MarkDirtyFor(this);
    }

    public bool Refresh()
    {
        double now = Time.GetTicksMsec() / 1000.0;
        double dt = Math.Min(now - _lastTick, 0.1);
        _lastTick = now;

        bool typing = Current == Tab.Codex && _typed < CodexText.Length;
        if (typing) _typed = Math.Min(CodexText.Length, _typed + dt * 70); // the assistant "speaks" at reading pace

        var seen = (_suit.Version, _loadout.Version, _hazardsHere(), Current, _hover);
        if (seen == _seen && !typing) return false;
        _seen = seen;
        QueueRedraw();
        return true;
    }

    static Rect2 TileRect(int i) =>
        new(GridX + i % Cols * (TileW + TileGap), GridY + i / Cols * (TileH + TileGap), TileW, TileH);

    public override void _GuiInput(InputEvent e)
    {
        if (e is not InputEventMouseMotion m || Current != Tab.Gear) return;
        int hover = -1;
        for (int i = 0; i < Cols * Rows; i++)
            if (TileRect(i).HasPoint(m.Position) && i < _suit.Stacks.Count) hover = i;
        if (hover != _hover)
        {
            _hover = hover;
            QueueRedraw();
            WorldScreen.MarkDirtyFor(this);
        }
    }

    public static Color KindColour(ItemKind kind) => kind switch
    {
        ItemKind.Ore => C.Accent2,
        ItemKind.Part => C.Good,
        ItemKind.Consumable => C.Holo,
        ItemKind.Armor or ItemKind.Booster => C.Accent,
        _ => C.Text,
    };

    public override void _Draw()
    {
        this.Block(new Rect2(Vector2.Zero, Size), C.Glass);
        this.Block(new Rect2(0, 0, Width, 58), C.Panel);
        this.Block(new Rect2(0, 58, Width, 4), C.Accent);

        if (Current == Tab.Gear) DrawGear();
        else DrawCodex();
    }

    void DrawGear()
    {
        // Top bar, right: the hovered item's numbers, or the suit's name.
        if (_hover >= 0 && _hover < _suit.Stacks.Count)
        {
            Stack s = _suit.Stacks[_hover];
            this.TextIn(UiTheme.Mono, 18, new Rect2(316, 0, Width - 330, 58),
                $"{s.Item.Name}  {s.Item.MassKg:0.#}KG {s.Item.VolumeL:0.#}L", C.Accent2, HorizontalAlignment.Right);
        }
        else this.TextIn(UiTheme.Display, 22, new Rect2(316, 0, Width - 330, 58), "VESTA", C.Accent, HorizontalAlignment.Right);

        for (int i = 0; i < Cols * Rows; i++)
        {
            Rect2 r = TileRect(i);
            if (i >= _suit.Stacks.Count)
            {
                this.Frame(r, C.Rule, 2);
                continue;
            }
            Stack s = _suit.Stacks[i];
            Color colour = KindColour(s.Item.Kind);
            this.Block(r, i == _hover ? C.Panel.Lightened(0.12f) : C.Panel);
            this.Block(new Rect2(r.Position, new Vector2(r.Size.X, 5)), colour);
            this.ItemGlyph(s.Item.Kind, r.Position + new Vector2(30, 34), 17, colour);
            this.Text(UiTheme.Mono, 24, new Vector2(r.Position.X, r.Position.Y + 14), s.Count.ToString(), C.Text, HorizontalAlignment.Right, r.Size.X - 8);
            this.Text(UiTheme.Label, 18, new Vector2(r.Position.X + 7, r.End.Y - 27), s.Item.Short, C.Text);
            if (i == _hover) this.Frame(r, C.Accent2, 3);
        }

        // Totals: the two numbers you pack against.
        Total(new Rect2(12, 346, 200, 96), "MASS", _suit.TotalMassKg, _suit.SoftMassKg, "KG");
        Total(new Rect2(222, 346, 200, 96), "VOLUME", _suit.TotalVolumeL, _suit.VolumeCapL, "L");

        // Worn gear and what it covers.
        float x = 446, w = Width - x - 10;
        this.Block(new Rect2(x - 12, 70, 3, 372), C.Rule);
        Slot(new Rect2(x, 70, w, 58), "ARMOR", _loadout.Armor);
        Slot(new Rect2(x, 134, w, 58), "BOOST 1", _loadout.Boosters[0]);
        Slot(new Rect2(x, 198, w, 58), "BOOST 2", _loadout.Boosters[1]);

        this.Caption(new Vector2(x, 276), "HAZARD COVER", C.Dim, 18);
        Hazard here = _hazardsHere();
        for (int i = 0; i < HazardLamps.Length; i++)
        {
            var (hazard, label) = HazardLamps[i];
            var centre = new Vector2(x + 24 + i * 52, 342);
            bool covered = _loadout.CoveredBy(hazard) != null;
            bool exposed = here.HasFlag(hazard) && !covered;
            if (exposed) this.DrawCircle(centre, 24, C.Danger);
            this.Lamp(centre, 16, C.Good, covered);
            this.Text(UiTheme.Label, 15, new Vector2(centre.X - 26, centre.Y + 26), label, exposed ? C.Danger : covered ? C.Good : C.Dim,
                HorizontalAlignment.Center, 52);
        }
        bool anyExposed = HazardLamps.Any(h => here.HasFlag(h.Hazard) && _loadout.CoveredBy(h.Hazard) == null);
        this.Text(UiTheme.Label, 17, new Vector2(x, 410), anyExposed ? "EXPOSED: GEAR UP" : "COVERED HERE", anyExposed ? C.Danger : C.Good);
    }

    void Total(Rect2 r, string caption, double value, double cap, string unit)
    {
        double load = value / cap;
        Color colour = UiTheme.LoadColor(load);
        this.Text(UiTheme.Label, 18, r.Position, caption, C.Dim);
        this.Text(UiTheme.Mono, 26, r.Position + new Vector2(0, 22), $"{value:0.#}/{cap:0}{unit}", colour);
        this.Segments(new Rect2(r.Position.X, r.Position.Y + 64, r.Size.X, 26), value, cap, colour, 0, 10, 4);
    }

    void Slot(Rect2 r, string label, ItemDef item)
    {
        this.Block(r, C.Panel);
        this.Text(UiTheme.Label, 15, r.Position + new Vector2(10, 4), label, C.Dim);
        if (item == null)
        {
            this.Text(UiTheme.Label, 20, r.Position + new Vector2(10, 26), "EMPTY", C.Rule.Lightened(0.3f));
            return;
        }
        this.ItemGlyph(item.Kind, r.End - new Vector2(28, 29), 16, KindColour(item.Kind));
        this.Text(UiTheme.Label, 20, r.Position + new Vector2(10, 26), item.Name, C.Text);
    }

    void DrawCodex()
    {
        this.TextIn(UiTheme.Label, 26, new Rect2(316, 0, Width - 330, 58), "CODEX 07", C.Holo, HorizontalAlignment.Right);

        // VESTA, the suit's assistant: a simple vector eye.
        var eye = new Vector2(100, 170);
        this.DrawCircle(eye, 70, C.Panel);
        this.DrawArc(eye, 62, 0, Mathf.Tau, 48, C.Holo, 4, true);
        this.DrawArc(eye, 46, -0.4f, Mathf.Pi + 0.4f, 32, C.Holo.Darkened(0.4f), 8, true);
        this.DrawCircle(eye, 24, C.Holo);
        this.DrawCircle(eye, 10, C.Glass);
        this.TextIn(UiTheme.Display, 26, new Rect2(0, 250, 200, 40), "VESTA", C.Text, HorizontalAlignment.Center);
        this.TextIn(UiTheme.Label, 16, new Rect2(0, 286, 200, 24), "SUIT ASSISTANT", C.Dim, HorizontalAlignment.Center);
        bool speaking = _typed < CodexText.Length;
        for (int i = 0; i < 7; i++)
        {
            float h = speaking ? 6 + 18 * Mathf.Abs(Mathf.Sin(i * 1.7f + (float)_typed * 0.6f)) : 6;
            this.Block(new Rect2(50 + i * 15, 350 - h / 2, 9, h), C.Holo);
        }

        float x = 216;
        this.Block(new Rect2(x - 12, 70, 3, 372), C.Rule);
        this.Text(UiTheme.Display, 28, new Vector2(x, 76), CodexTitle, C.Accent2);
        this.Text(UiTheme.Label, 16, new Vector2(x, 116), "SHIP SYSTEMS  -  READ BY VESTA", C.Dim);
        string shown = CodexText[..(int)_typed];
        this.DrawMultilineString(UiTheme.Body, new Vector2(x, 160 + UiTheme.Body.GetAscent(22)), shown,
            HorizontalAlignment.Left, Width - x - 16, 22, -1, C.Text);
    }
}
