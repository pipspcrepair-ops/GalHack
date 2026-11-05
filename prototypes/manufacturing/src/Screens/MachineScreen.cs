using System;
using DiegeticUi.Screens;
using Godot;

namespace Manufacturing.Screens;

/// <summary>What a player does to a screen with the keyboard (the cursor is the main way; these are extras).</summary>
public enum ScreenKey { Up, Down, Left, Right, Tab, Act, ActMany, Cancel, Toggle, Less, More }

/// <summary>
/// A machine's page: a panel that paints itself (1280 x 720), knows when it changed, and what on
/// it can be clicked. Pages read the machine's own state every poll and redraw only when its
/// signature changes. While they paint, they register every clickable thing as a hot spot, so the
/// cursor always clicks exactly what the screen shows; whatever is under the cursor is painted lit.
/// Like any panel it brings no theme: the screen gives it diegetic-ui's.
/// </summary>
public abstract partial class MachinePage : Control, IScreenPage
{
    public const int W = 1280, H = 720;

    /// <summary>The clickable things as last painted.</summary>
    public HotSpots Spots { get; } = new();

    /// <summary>A line for the HUD after a click on the screen ("ORDERED 1 x SERVO").</summary>
    public event Action<string> Said;

    int _seen = int.MinValue;

    protected MachinePage() => MouseFilter = MouseFilterEnum.Stop;

    /// <summary>The hint line along the bottom of the glass while the cursor is on it.</summary>
    public abstract string Prompt { get; }

    /// <summary>A number that changes whenever anything on the page would.</summary>
    protected abstract int Signature();

    /// <summary>Paints the page, registering its hot spots with <see cref="Spot"/> as it goes.</summary>
    protected abstract void Paint();

    /// <summary>The wheel turned over the page: +1 down, -1 up.</summary>
    protected virtual string Wheel(int step) => Handle(step > 0 ? ScreenKey.Down : ScreenKey.Up);

    /// <summary>A click that landed on no hot spot.</summary>
    protected virtual string ClickedNothing(SpotClick click) => null;

    /// <summary>Handles a key while the cursor is on the screen; returns a line for the HUD, or null.</summary>
    public virtual string Handle(ScreenKey key) => null;

    /// <summary>A key pressed while the cursor is on the screen: handled, then redrawn at once. Returns a line for the HUD, or null.</summary>
    public string Press(ScreenKey key)
    {
        string line = Handle(key);
        Refresh();
        Changed();
        return line;
    }

    /// <summary>True while the player's cursor is on this page's screen.</summary>
    public bool Focused => GetViewport() is ScreenViewport { Screen.InUse: true };

    /// <summary>Redraws if anything changed; true if it did.</summary>
    public bool Refresh()
    {
        int s = HashCode.Combine(Signature(), Focused);
        if (s == _seen) return false;
        _seen = s;
        QueueRedraw();
        return true;
    }

    /// <summary>Registers a clickable area while painting; true if the cursor is on it (paint it lit).</summary>
    protected bool Spot(Rect2 rect, string key, Func<SpotClick, string> act = null) => Spots.Add(rect, key, act);

    public sealed override void _Draw()
    {
        Spots.Begin();
        Paint();
        if (Focused) this.Hints(W, H, Prompt);
    }

    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseMotion motion:
                if (Spots.Move(motion.Position)) Changed();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel:
                Report(Wheel(wheel.ButtonIndex == MouseButton.WheelDown ? 1 : -1));
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left or MouseButton.Right } button:
                var click = new SpotClick(button.ButtonIndex, button.ShiftPressed);
                string said = Spots.Press(button.Position, click, out bool hit);
                Report(hit ? said : ClickedNothing(click));
                break;
            default:
                return;
        }
        AcceptEvent();
    }

    public override void _Notification(int what)
    {
        // The cursor arriving or leaving shows or hides the hint line and any lit spot.
        if (what == NotificationMouseEnter) Changed();
        else if (what == NotificationMouseExit)
        {
            Spots.Move(null);
            Changed();
        }
    }

    /// <summary>After an action: tell the HUD, and redraw at once rather than at the next poll.</summary>
    void Report(string line)
    {
        if (line != null) Said?.Invoke(line);
        Refresh();
        Changed();
    }

    /// <summary>Redraws now: the page, and the screen showing it.</summary>
    protected void Changed()
    {
        QueueRedraw();
        WorldScreen.MarkDirtyFor(this);
    }

    protected static int Wrap(int i, int n) => n <= 0 ? 0 : (i % n + n) % n;

    /// <summary>The first row to show so that <paramref name="keep"/> stays in a window of <paramref name="rows"/>.</summary>
    protected static int ScrollTo(int top, int keep, int rows, int count) =>
        Math.Clamp(keep < top ? keep : keep >= top + rows ? keep - rows + 1 : top, 0, Math.Max(0, count - rows));
}

/// <summary>
/// A screen mounted on a machine: diegetic-ui's <see cref="WorldScreen"/> showing one machine's
/// page, with the glass shader read from diegetic-ui's folder, a bezel that lights while the
/// cursor is on it, and its own pick layer (building's layers own 1 to 3).
/// </summary>
public partial class MachineScreen : WorldScreen
{
    /// <summary>Physics layer of the invisible slab used to pick machine screens by looking at them.</summary>
    public const uint Layer = 1u << 3;

    public MachinePage Page { get; init; }

    public MachineScreen()
    {
        SizeMeters = new Vector2(1.6f, 0.9f);
        Pixels = new Vector2I(MachinePage.W, MachinePage.H);
        PickLayer = Layer;
        BezelLit = Kit.Accent2;
        SurfaceShader = Kit.SurfaceShader;
        RefreshHz = 10;
    }

    public override void _Ready()
    {
        base._Ready();
        SetContent(Page);
    }
}
