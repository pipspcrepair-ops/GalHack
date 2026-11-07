using System;
using System.Collections.Generic;
using System.Linq;
using DiegeticUi.Theme;
using Godot;
using Slice.Game;
using Slice.Items;
using Slice.Vessels;
using Palette = DiegeticUi.Theme.Palette;

namespace Slice.Ui;

/// <summary>The Tab menu's tabs.</summary>
public enum MenuTab { You, Tech, Stats, Core, Map }

/// <summary>
/// The suit's terminal, on Tab: one full-screen menu in the diegetic UI's look (screen glass,
/// scanlines, bold 1970s-paperback colours), styled as the suit's own tech but laid over the view
/// rather than built into the world. The game doesn't pause: you can walk with it open. Tabs:
///
///   YOU    your bag and toolbar, equipment round a paperdoll of you, your vitals and level, and
///          the suit fabricator; E on a container opens it here beside your bag
///   TECH   the tech tree, by tab, built from every item's and block's unlock
///   STATS  what you have done since the crash
///   CORE   the structure or device you are in or next to: its manufacturing, storage, settings and numbers
///   MAP    what is around you
///
/// Every section is its own panel (<see cref="IMenuSection"/>), so any of them can stand alone on
/// an in-world screen.
/// </summary>
public partial class TabMenu : CanvasLayer
{
    public Game.Game Game;
    public MenuRoot Root { get; private set; }
    public bool IsOpen => Root?.Visible == true;
    public MenuTab Tab
    {
        get => Root.Tab;
        set => Root.Tab = value;
    }

    /// <summary>Opened, closed, tab changes, drops, clicks: the Tab menu's own sounds play on these.</summary>
    public event Action<string> Sounded;

    public override void _Ready()
    {
        Layer = 3;
        Root = new MenuRoot { Name = "Root", Game = Game, Visible = false };
        AddChild(Root);
        Root.Sounded += s => Sounded?.Invoke(s);
        Root.Gui.Clock = () => Game.Stats.Seconds;
    }

    public void Toggle()
    {
        if (IsOpen) Close();
        else Open(Root.Tab);
    }

    public void Open(MenuTab tab)
    {
        Root.Tab = tab;
        Root.Visible = true;
        Sounded?.Invoke("open");
    }

    public void OpenInventory() => Open(MenuTab.You);

    /// <summary>Opens the map at a level.</summary>
    public void OpenMap(MapLevel level)
    {
        Root.Map.Levels.Level = level;
        Open(MenuTab.Map);
    }

    /// <summary>The Core tab on a particular structure.</summary>
    public void OpenCore(object v, int machine = 0)
    {
        Root.Core.Pick(v, machine);
        Open(MenuTab.Core);
    }

    public void Close()
    {
        if (!IsOpen) return;
        Root.Visible = false;
        Root.Gui.DragId = null;
        Game.CloseContainer();
        Sounded?.Invoke("close");
    }

    /// <summary>The game's mouse (tests, and anything scripted) as one input pass: the pointer is in the menu's 1600 x 900 canvas.</summary>
    public void Input(Controls c)
    {
        if (Game.Interactive) return; // real mouse input reaches the panel itself
        Root.Feed(c.Mouse, c.PrimaryPressed, c.PrimaryReleased, c.Primary, c.SecondaryPressed, c.IsHeld(Godot.Key.Shift), c.IsHeld(Godot.Key.Ctrl), c.SecondaryReleased, c.Secondary, c.Wheel);
    }

    /// <summary>The / key: the first filter box on the page takes the keyboard.</summary>
    public void FocusFilter()
    {
        string box = Root.Gui.LastFirstBox;
        if (box != null && box.EndsWith(":filter")) ItemViews.Focused = box[..^":filter".Length];
    }

    /// <summary>A screen point to the menu's canvas.</summary>
    public Vector2 ToVirtual(Vector2 screen) => Root == null ? screen : Root.ToDesign(screen - Root.GlobalPosition);

    /// <summary>A widget's rectangle by id (design units), from the last input pass.</summary>
    public Rect2? RectOf(string id) => Root.RectOf(id);
}

/// <summary>The menu's own panel: the header with its tabs and your level, and the current tab's sections.</summary>
public partial class MenuRoot : MenuPanel
{
    static Palette C => UiTheme.Colors;
    public Game.Game Game;
    public MenuTab Tab = MenuTab.You;
    public override Vector2 DesignSize => new(Gui.Width, Gui.Height);

    /// <summary>Redrawn when what it shows changes: the tab, the bag, an open container, progress, and a 4 Hz tick for live vitals and queues.</summary>
    protected override string Signature() => Game?.Pack == null ? "" :
        $"{Tab}|{Game.Pack.Storage.Version}|{Game.Pack.Grid.Version}|{Game.Pack.Selected}|{Game.Pack.Bar}|{Game.Opened?.Grid.Storage.Version}|{Game.Progress.Xp}|{Game.Progress.Points}|{(int)(Game.Stats.Seconds * 4)}|{ItemViews.Focused}|{(ItemViews.Focused != null ? ItemViews.Get(ItemViews.Focused).Text : "")}|{(Tab == MenuTab.Map ? Map.Levels.Signature : Tab == MenuTab.Tech ? Tech.Signature : Tab == MenuTab.Stats ? Stats.Signature : "")}";

    public YouSection You;
    public TechSection Tech;
    public StatsSection Stats;
    public CoreSection Core;
    public MapSection Map;

    public override void _Ready()
    {
        base._Ready();
        You = new YouSection(Game);
        Tech = new TechSection(Game);
        Stats = new StatsSection(Game);
        Core = new CoreSection(Game);
        Map = new MapSection(Game);
    }

    protected override void Build(Gui g, Rect2 area)
    {
        if (Game?.Survivor == null) return;
        // The glass: dark, with the theme's scanline stripes at the top.
        g.Fill(area, new Color(C.Glass, 0.93f));
        var header = new Rect2(area.Position, new Vector2(area.Size.X, 74));
        g.Fill(header, C.Accent);
        if (g.Drawing) g.Canvas.Stripes(new Rect2(header.Position + new Vector2(header.Size.X - 300, 0), new Vector2(300, header.Size.Y)), C.Accent, C.Accent2, 16);
        g.Text("SUIT TERMINAL", header.Position + new Vector2(24, 14), C.Glass, 30, UiTheme.Display);

        string[] names = { "YOU", "TECH", "STATS", "CORE", "MAP" };
        for (int i = 0; i < names.Length; i++)
        {
            var r = new Rect2(header.Position + new Vector2(420 + i * 150, 16), new Vector2(140, 44));
            if (g.Tab($"tab:{names[i].ToLowerInvariant()}", r, names[i], Tab == (MenuTab)i, C.Accent2)) Tab = (MenuTab)i;
        }
        Progress p = Game.Progress;
        var (into, span) = p.ToNext;
        g.Text($"LEVEL {p.Level}", header.Position + new Vector2(1180, 8), C.Glass, 20, UiTheme.Display);
        g.Text($"{p.Points} PTS", header.Position + new Vector2(1180, 40), C.Glass, 18, UiTheme.Mono);
        g.Bar(new Rect2(header.Position + new Vector2(1270, 46), new Vector2(150, 14)), into, span, C.Glass, 10);
        g.Text(Game.Mode == GalHack.Items.GameMode.Creative ? "CREATIVE" : "SURVIVAL", header.Position + new Vector2(1440, 26), C.Glass, 16, UiTheme.Label);

        var body = new Rect2(area.Position + new Vector2(20, 90), new Vector2(area.Size.X - 40, area.Size.Y - 110));
        switch (Tab)
        {
            case MenuTab.You: You.Build(g, body); break;
            case MenuTab.Tech: Tech.Build(g, body); break;
            case MenuTab.Stats: Stats.Build(g, body); break;
            case MenuTab.Core: Core.Build(g, body); break;
            case MenuTab.Map: Map.Build(g, body); break;
        }
        g.TextIn("TAB close · ESC close · the world keeps running", new Rect2(area.Position + new Vector2(20, area.Size.Y - 24), new Vector2(area.Size.X - 40, 20)), C.Dim, 14, HorizontalAlignment.Right);
        // Something dragged out of the window is set down on the ground in front of you.
        if (g.DroppedOutside(body.Grow(4), out object dropped) && dropped is SlotRef s) Game.Drop(s.Grid, s.Index);
    }
}


/// <summary>A block family being dragged from the list onto a toolbar (its item id).</summary>
public sealed record BlockRef(string BlockId);
/// <summary>
/// A slot being dragged: which grid and which slot, how many of its stack (all by default; the
/// right button takes half, Ctrl + right one, the wheel changes it), and, when the slot was part of
/// a selection, the other selected slots that go with it.
/// </summary>
public sealed class SlotRef : IDragAmount
{
    public readonly ItemGrid Grid;
    public readonly int Index;
    public int Max { get; }
    int _amount = int.MaxValue;

    /// <summary>The selected slots dragged together (this one included), or null for a single stack.</summary>
    public IReadOnlyList<int> Group;

    public SlotRef(ItemGrid grid, int index, int max = int.MaxValue)
    {
        Grid = grid;
        Index = index;
        Max = max;
    }

    public int Amount { get => _amount; set => _amount = value; }

    public void Begin(bool ctrl) => _amount = ctrl ? 1 : Math.Max(1, (Max == int.MaxValue ? 2 : Max) / 2 + (Max % 2));

    public void Step(int by)
    {
        int cur = _amount == int.MaxValue ? Max : _amount;
        _amount = Math.Clamp(cur + by, 1, Math.Max(1, Max));
        if (_amount == Max) _amount = int.MaxValue;
    }

    public string Shown => Group != null && Group.Count > 1 ? $"x{Group.Count}" : _amount == int.MaxValue ? null : $"{_amount}";
}

