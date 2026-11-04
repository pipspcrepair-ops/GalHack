using System;
using System.Collections.Generic;
using System.Linq;
using DiegeticUi.Logic;
using DiegeticUi.Screens;
using DiegeticUi.Theme;
using Godot;

namespace DiegeticUi.Pages;

/// <summary>
/// The bridge's ship systems console: live power, CPU and mass; the shield switch and the
/// core-gravity lever, each previewing its cost on the bars while the cursor is on it; and the
/// station roster, where roles are dragged straight onto crew.
/// </summary>
public partial class ConsolePage : Control, IScreenPage
{
    static Palette C => UiTheme.Colors;

    public static Color RoleColour(Station s) => s switch
    {
        Station.Pilot => C.Accent,
        Station.Navigation => C.Good,
        Station.Gunnery => C.Danger,
        Station.Engineering => C.Accent2,
        _ => C.Holo,
    };

    public static string RoleName(Station s) => s switch
    {
        Station.Pilot => "PILOT",
        Station.Navigation => "NAV",
        Station.Gunnery => "GUN",
        Station.Engineering => "ENG",
        _ => "SCAN",
    };

    readonly ShipSystems _ship;
    readonly WarningTimeline _warnings;
    public StationRoster Roster { get; }

    readonly SegmentBar _power, _cpu, _mass;
    readonly RockerSwitch _shields;
    readonly Lever _gravity;
    readonly List<RoleChip> _chips = new();
    readonly List<CrewCard> _cards = new();
    readonly Control _overlay;

    int _seenShip = -1, _seenRoster = -1, _seenWarnings = -1;
    ShipSystem? _previewing;

    // The role being dragged, where from (null: the palette) and where the cursor is.
    public Station? Dragging { get; private set; }
    string _dragFrom;
    Vector2 _dragAt;
    public CrewCard DropTarget { get; private set; }
    string _message;
    double _messageUntil;

    public IReadOnlyList<CrewCard> Cards => _cards;
    public IReadOnlyList<RoleChip> Chips => _chips;
    public RockerSwitch ShieldSwitch => _shields;
    public Lever GravityLever => _gravity;

    public ConsolePage(ShipSystems ship, StationRoster roster, WarningTimeline warnings)
    {
        _ship = ship;
        Roster = roster;
        _warnings = warnings;
        MouseFilter = MouseFilterEnum.Ignore;

        _power = Add(new SegmentBar { Caption = "POWER", Unit = "KW" }, new Rect2(36, 160, 600, 92));
        _cpu = Add(new SegmentBar { Caption = "CPU", Unit = "CU" }, new Rect2(36, 268, 600, 92));
        _mass = Add(new SegmentBar { Caption = "MASS", Unit = "T", Format = "0.0" }, new Rect2(36, 376, 600, 92));

        var (sp, sc) = ShipSystems.Cost(ShipSystem.Shields);
        _shields = Add(new RockerSwitch { Caption = "SHIELDS", Detail = $"{sp:0} KW  {sc:0} CU" }, new Rect2(36, 492, 280, 200));
        _shields.Toggled += on => _ship.Set(ShipSystem.Shields, on);
        _shields.HoverChanged += h => Preview(h ? ShipSystem.Shields : null);

        var (gp, gc) = ShipSystems.Cost(ShipSystem.CoreGravity);
        _gravity = Add(new Lever { Caption = "CORE GRAVITY", Detail = $"{gp:0} KW  {gc:0} CU" }, new Rect2(356, 492, 280, 210));
        _gravity.Thrown += on => _ship.Set(ShipSystem.CoreGravity, on);
        _gravity.HoverChanged += h => Preview(h ? ShipSystem.CoreGravity : null);

        float x = 690;
        foreach (Station s in Enum.GetValues<Station>())
        {
            var chip = Add(new RoleChip { Page = this, Role = s }, new Rect2(x, 176, 104, 52));
            _chips.Add(chip);
            x += 114;
        }
        for (int i = 0; i < roster.Crew.Count; i++)
            _cards.Add(Add(new CrewCard { Page = this, Crew = roster.Crew[i] }, new Rect2(690, 254 + i * 140, 560, 126)));

        _overlay = new Control { MouseFilter = MouseFilterEnum.Ignore, Size = new Vector2(1280, 720) };
        _overlay.Draw += DrawOverlay;
        AddChild(_overlay);
    }

    T Add<T>(T widget, Rect2 rect) where T : Control
    {
        widget.Position = rect.Position;
        widget.Size = rect.Size;
        AddChild(widget);
        return widget;
    }

    void Preview(ShipSystem? system)
    {
        _previewing = system;
        UpdateBars();
    }

    void UpdateBars()
    {
        (double power, double cpu) delta = _previewing is { } s ? _ship.PreviewToggle(s) : (0, 0);
        _power.Set(_ship.PowerKw, ShipSystems.PowerCapKw, delta.power);
        _cpu.Set(_ship.CpuUsed, ShipSystems.CpuCap, delta.cpu);
        _mass.Set(_ship.MassT, ShipSystems.MaxMassT);
    }

    public bool Refresh()
    {
        bool changed = false;
        if (_ship.Version != _seenShip)
        {
            _seenShip = _ship.Version;
            _shields.SetOn(_ship.Shields);
            if (_gravity.On != _ship.CoreGravity) _gravity.SetOn(_ship.CoreGravity);
            UpdateBars();
            changed = true;
        }
        if (Roster.Version != _seenRoster)
        {
            _seenRoster = Roster.Version;
            if (Roster.LastRefusal != null) Say(Roster.LastRefusal);
            foreach (CrewCard card in _cards) card.Changed();
            changed = true;
        }
        if (_warnings.Version != _seenWarnings)
        {
            _seenWarnings = _warnings.Version;
            changed = true;
        }
        if (_message != null && Time.GetTicksMsec() / 1000.0 > _messageUntil)
        {
            _message = null;
            changed = true;
        }
        if (changed) QueueRedraw();
        return changed;
    }

    void Say(string message)
    {
        _message = message;
        _messageUntil = Time.GetTicksMsec() / 1000.0 + 4;
        QueueRedraw();
    }

    // ---- Dragging roles ----------------------------------------------------------------

    public void BeginDrag(Station role, string fromCrew, Vector2 at)
    {
        Dragging = role;
        _dragFrom = fromCrew;
        DragTo(at);
    }

    public void DragTo(Vector2 at)
    {
        if (Dragging == null) return;
        _dragAt = at;
        CrewCard over = _cards.FirstOrDefault(c => c.GetGlobalRect().HasPoint(at));
        if (over != DropTarget)
        {
            DropTarget?.Changed();
            DropTarget = over;
            over?.Changed();
        }
        _overlay.QueueRedraw();
        WorldScreen.MarkDirtyFor(this);
    }

    /// <summary>
    /// Drops the role. On a crew card: assign it. Dragged off a card onto nothing: take it
    /// away (the roster refuses for the pilot, and the console says why).
    /// </summary>
    public void Drop(Vector2 at)
    {
        if (Dragging is not { } role) return;
        DragTo(at);
        CrewCard target = DropTarget;
        if (target != null && target.Crew != _dragFrom)
        {
            // From the palette this adds a holder (gunnery splits); from another card it moves
            // the role. Single-holder roles move on assignment anyway; a shared one needs the
            // old holder taken off.
            Roster.Assign(role, target.Crew);
            if (_dragFrom != null && StationRoster.Shared(role)) Roster.Unassign(role, _dragFrom);
        }
        else if (target == null && _dragFrom != null) Roster.Unassign(role, _dragFrom);

        Dragging = null;
        _dragFrom = null;
        DropTarget?.Changed();
        DropTarget = null;
        _overlay.QueueRedraw();
        WorldScreen.MarkDirtyFor(this);
    }

    // ---- Drawing -----------------------------------------------------------------------

    public override void _Draw()
    {
        this.Block(new Rect2(Vector2.Zero, Size), C.Glass);
        this.Header(Size.X, "SHIP SYSTEMS", "HX-07");

        // The status strip under the header: an invasion countdown, a refusal, or all well.
        bool over = _ship.PowerKw > ShipSystems.PowerCapKw;
        if (_message != null) PageChrome.Strip(this, Size.X, _warnings, _message, C.Warn);
        else if (over) PageChrome.Strip(this, Size.X, _warnings, "POWER OVERDRAWN: SHED A SYSTEM", C.Danger);
        else PageChrome.Strip(this, Size.X, _warnings, "ALL SYSTEMS NOMINAL", C.Good);

        this.Block(new Rect2(660, 160, 4, 540), C.Rule);
        this.Caption(new Vector2(690, 150 - 4), "STATIONS  -  DRAG A ROLE ONTO CREW", C.Dim);
    }

    void DrawOverlay()
    {
        if (Dragging is not { } role) return;
        // The chip being carried, lifted with a shadow so it reads as "in hand".
        var rect = new Rect2(_dragAt - new Vector2(52, 26), new Vector2(104, 52));
        _overlay.DrawRect(new Rect2(rect.Position + new Vector2(8, 8), rect.Size), new Color(0, 0, 0, 0.5f));
        _overlay.DrawRect(rect, RoleColour(role));
        _overlay.TextIn(UiTheme.Heavy, UiTheme.SizeLabel, rect, RoleName(role), C.Glass, HorizontalAlignment.Center);
        _overlay.Frame(rect, C.Text, 3);
    }
}

/// <summary>A role in the palette at the top of the roster. Press and drag it onto a crew card.</summary>
public partial class RoleChip : ScreenWidget
{
    public ConsolePage Page { get; init; }
    public Station Role { get; init; }

    public override void _GuiInput(InputEvent e)
    {
        Vector2 global = GetGlobalRect().Position;
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } b)
        {
            if (b.Pressed) Page.BeginDrag(Role, null, global + b.Position);
            else Page.Drop(global + b.Position);
            AcceptEvent();
        }
        else if (e is InputEventMouseMotion m && Page.Dragging != null)
        {
            Page.DragTo(global + m.Position);
            AcceptEvent();
        }
    }

    public override void _Draw()
    {
        var rect = new Rect2(Vector2.Zero, Size);
        Color colour = ConsolePage.RoleColour(Role);
        this.Block(rect, Hovered ? colour.Lightened(0.2f) : colour);
        this.TextIn(UiTheme.Heavy, UiTheme.SizeLabel, rect, ConsolePage.RoleName(Role), C.Glass, HorizontalAlignment.Center);
    }
}

/// <summary>One crew member: their name and the roles they hold. Roles on the card can be dragged off again.</summary>
public partial class CrewCard : ScreenWidget
{
    public ConsolePage Page { get; init; }
    public string Crew { get; init; }

    /// <summary>The tag rectangles for this crew member's roles, in local coordinates.</summary>
    public List<(Station Role, Rect2 Rect)> Tags()
    {
        var tags = new List<(Station, Rect2)>();
        float x = 24;
        foreach (Station s in Page.Roster.RolesOf(Crew))
        {
            float w = s == Station.Gunnery ? 150 : 104;
            tags.Add((s, new Rect2(x, 64, w, 46)));
            x += w + 10;
        }
        return tags;
    }

    public override void _GuiInput(InputEvent e)
    {
        Vector2 global = GetGlobalRect().Position;
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } b)
        {
            if (b.Pressed)
            {
                foreach (var (role, rect) in Tags())
                    if (rect.HasPoint(b.Position)) Page.BeginDrag(role, Crew, global + b.Position);
            }
            else Page.Drop(global + b.Position);
            AcceptEvent();
        }
        else if (e is InputEventMouseMotion m && Page.Dragging != null)
        {
            Page.DragTo(global + m.Position);
            AcceptEvent();
        }
    }

    public override void _Draw()
    {
        var rect = new Rect2(Vector2.Zero, Size);
        bool target = Page.DropTarget == this;
        this.Block(rect, target ? C.Panel.Lightened(0.12f) : C.Panel);
        this.Block(new Rect2(0, 0, 10, Size.Y), Page.Roster.Holders(Station.Pilot).Contains(Crew) ? C.Accent : C.Rule);
        if (target) this.Frame(rect, C.Accent2, 5);

        this.Text(UiTheme.Label, 34, new Vector2(24, 8), Crew, C.Text);
        var tags = Tags();
        if (tags.Count == 0)
            this.Text(UiTheme.Label, UiTheme.SizeSmall, new Vector2(24, 76), "NO STATION  -  FREE TO ROAM", C.Dim);
        foreach (var (role, r) in tags)
        {
            this.Block(r, ConsolePage.RoleColour(role));
            string label = role == Station.Gunnery ? $"GUN {Page.Roster.TurretsFor(Crew)}/{Page.Roster.Turrets}" : ConsolePage.RoleName(role);
            this.TextIn(UiTheme.Heavy, UiTheme.SizeLabel - 2, r, label, C.Glass, HorizontalAlignment.Center);
        }
    }
}
