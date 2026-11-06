using System;
using System.Collections.Generic;
using DiegeticUi.Theme;
using Godot;

namespace Slice.Ui;

/// <summary>
/// One section of the Tab menu (the bag, a constructor's manufacturing, a structure's settings,
/// the tech tree...). It lays itself out inside whatever rectangle it is given, so the same
/// section can fill a tab, share a tab with others, or stand alone on an in-world screen
/// (<see cref="SectionPanel"/>).
/// </summary>
public interface IMenuSection
{
    /// <summary>The size it is laid out for best (design units); it fits itself to any other.</summary>
    Vector2 PreferredSize { get; }

    /// <summary>Lays out (and, on the draw pass, draws) inside <paramref name="area"/>.</summary>
    void Build(Gui g, Rect2 area);
}

/// <summary>
/// A self-contained UI panel: a <see cref="Control"/> that sizes to its container, scales its
/// design canvas uniformly to fit, draws its sections, and takes mouse input the normal way
/// (<see cref="_GuiInput"/>), or from a script through <see cref="Feed"/> (tests). It assumes
/// nothing about being full-screen or on the HUD, so it can be shown on an in-world screen.
/// </summary>
public abstract partial class MenuPanel : Control
{
    public readonly Gui Gui = new();

    /// <summary>The canvas it is laid out in, design units.</summary>
    public abstract Vector2 DesignSize { get; }

    /// <summary>Lays out the whole panel.</summary>
    protected abstract void Build(Gui g, Rect2 area);

    /// <summary>A sound for what the last input did, if any ("click", "tab", "drop").</summary>
    public event Action<string> Sounded;

    bool _held;

    /// <summary>True: every pointer move is an input pass too (a panel on an in-world screen, whose widgets a look must find before any click).</summary>
    protected virtual bool LaysOutOnHover => false;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    /// <summary>Design units to this control's pixels: a uniform fit, centred.</summary>
    public Transform2D Fit
    {
        get
        {
            Vector2 size = Size.X < 10 || Size.Y < 10 ? DesignSize : Size;
            float s = Mathf.Min(size.X / DesignSize.X, size.Y / DesignSize.Y);
            Vector2 offset = (size - DesignSize * s) * 0.5f;
            return new Transform2D(0f, new Vector2(s, s), 0f, offset);
        }
    }

    public Vector2 ToDesign(Vector2 local) => Fit.AffineInverse() * local;

    public override void _Draw()
    {
        if (Size.X < 10 || Size.Y < 10) return;
        DrawSetTransformMatrix(Fit);
        Gui.BeginDraw(this);
        Build(Gui, new Rect2(Vector2.Zero, DesignSize));
        Gui.DrawTip(new Rect2(Vector2.Zero, DesignSize));
        Gui.DrawDrag();
        Gui.Canvas = null;
        DrawSetTransformMatrix(Transform2D.Identity);
    }

    /// <summary>A short text that changes whenever the panel would look different: it redraws only then (idle screens cost nothing).</summary>
    protected virtual string Signature() => "";

    string _shown;

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree()) return;
        // Straight on a canvas layer (the Tab menu), it fills the view; in a container or on a screen, the container sizes it.
        if (GetParent() is not Control && Size != GetViewportRect().Size)
        {
            Position = Vector2.Zero;
            Size = GetViewportRect().Size;
        }
        string now = $"{Signature()}|{Gui.Mouse.X:F0},{Gui.Mouse.Y:F0}|{Gui.DragId}";
        if (now == _shown) return;
        _shown = now;
        QueueRedraw();
    }

    /// <summary>The pointer left: nothing stays lit under where it was.</summary>
    public override void _Notification(int what)
    {
        if (what != NotificationMouseExit) return;
        Gui.Mouse = new Vector2(-1e5f, -1e5f);
        QueueRedraw();
    }

    /// <summary>Real mouse input: buttons and motion, in this control's pixels.</summary>
    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } b:
                _held = b.Pressed;
                Feed(ToDesign(b.Position), pressed: b.Pressed, released: !b.Pressed, held: b.Pressed, right: false, shift: b.ShiftPressed);
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } r:
                Feed(ToDesign(r.Position), false, false, _held, right: true, shift: r.ShiftPressed);
                AcceptEvent();
                break;
            case InputEventMouseMotion m:
                Gui.Mouse = ToDesign(m.Position);
                if (LaysOutOnHover) Feed(Gui.Mouse, false, false, _held, false, m.ShiftPressed);
                break;
        }
    }

    /// <summary>One input pass with the pointer at a design-space point (the game's mouse, or a test's).</summary>
    public void Feed(Vector2 mouse, bool pressed, bool released, bool held, bool right, bool shift)
    {
        Gui.BeginInput(mouse, pressed, released, held, right, shift);
        Build(Gui, new Rect2(Vector2.Zero, DesignSize));
        Gui.EndInput();
        if (Gui.Sound != null) Sounded?.Invoke(Gui.Sound);
    }

    /// <summary>A widget's rectangle by id, from the last input pass (design units), or null.</summary>
    public Rect2? RectOf(string id) => Gui.Rects.TryGetValue(id, out Rect2 r) ? r : null;

    public IEnumerable<string> WidgetIds => Gui.Rects.Keys;
}

/// <summary>Any one section as a panel of its own (for an in-world screen, or a test).</summary>
public partial class SectionPanel : MenuPanel
{
    public IMenuSection Section;
    public override Vector2 DesignSize => Section?.PreferredSize ?? new Vector2(800, 600);
    protected override bool LaysOutOnHover => true;

    /// <summary>What the section shows changes: set by whoever hosts it (a few times a second is plenty).</summary>
    public Func<string> State;
    protected override string Signature() => State?.Invoke() ?? "";

    protected override void Build(Gui g, Rect2 area)
    {
        g.Fill(area, UiTheme.Colors.Glass);
        Section?.Build(g, area);
    }
}
