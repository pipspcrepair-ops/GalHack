using System;
using DiegeticUi.Logic;
using Godot;

namespace DiegeticUi.Screens;

/// <summary>
/// Using things in the world. Look at a screen and press E: the view eases in to frame it, a
/// cursor appears on its surface and the mouse drives it. Esc eases back out. The world keeps
/// running throughout.
///
/// The OS mouse stays captured the whole time. While a screen is in use, mouse motion moves a
/// virtual cursor in window space, kept on the screen's projected rectangle; each mouse event
/// becomes a ray from the camera through that cursor, which the target turns into its own
/// input. A virtual cursor behaves the same on every platform, never escapes the window, and
/// works the same for a gamepad stick later.
/// </summary>
public partial class Interactor : Node
{
    public Camera3D Camera { get; set; }

    /// <summary>Where the camera is while walking. Read every frame.</summary>
    public Func<Transform3D> HeadView { get; set; }

    /// <summary>How far away you can use something from.</summary>
    public float Reach { get; set; } = 2.8f;

    /// <summary>Layers the things you can use are picked on.</summary>
    public uint PickMask { get; set; } = WorldScreen.DefaultPickLayer;

    /// <summary>Layers that block the look ray (walls), besides the pick layer.</summary>
    public uint BlockMask { get; set; } = 1;

    public FocusMachine<IFocusTarget> Focus { get; } = new();

    /// <summary>What the player is looking at and could use with E, or null.</summary>
    public IFocusTarget LookTarget { get; private set; }

    /// <summary>The virtual cursor, in window pixels. Meaningful only while <see cref="CursorVisible"/>.</summary>
    public Vector2 Cursor { get; private set; }

    public bool CursorVisible => Focus.CursorVisible;
    public bool LookLocked => Focus.LookLocked;

    public override void _Ready() => ProcessPriority = 10;

    public bool Enter(IFocusTarget target) => Focus.Enter(target);

    public bool Back()
    {
        IFocusTarget target = Focus.Target;
        bool wasUsing = Focus.State == FocusState.Using;
        if (!Focus.Back()) return false;
        if (wasUsing) target.SetInUse(false);
        return true;
    }

    /// <summary>Handles E, Esc and the mouse while a screen is involved. Returns true if it used the event.</summary>
    public bool HandleInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.E }:
                return Focus.State == FocusState.Free && LookTarget != null && Enter(LookTarget);

            case InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Escape }:
                return Back();

            case InputEventMouseMotion motion when Focus.State == FocusState.Using:
                MoveCursor(Cursor + motion.Relative);
                Point(e);
                return true;

            case InputEventMouseButton when Focus.State == FocusState.Using:
                Point(e);
                return true;

            case InputEventMouse when Focus.Busy:
                return true; // easing in or out: the mouse neither looks nor clicks
        }
        return false;
    }

    /// <summary>Puts the virtual cursor at a window position (kept on the target) and sends a move there.</summary>
    public void PointAt(Vector2 windowPosition)
    {
        if (Focus.State != FocusState.Using) return;
        MoveCursor(windowPosition);
        Point(new InputEventMouseMotion { Position = Cursor });
    }

    void MoveCursor(Vector2 to)
    {
        Rect2 rect = Focus.Target.ScreenRect(Camera);
        Cursor = to.Clamp(rect.Position, rect.End);
    }

    void Point(InputEvent e) =>
        Focus.Target.Point(Camera.ProjectRayOrigin(Cursor), Camera.ProjectRayNormal(Cursor), e);

    public override void _Process(double delta)
    {
        FocusState before = Focus.State;
        Focus.Tick(delta);
        IFocusTarget target = Focus.Target;

        if (before == FocusState.Entering && Focus.State == FocusState.Using)
        {
            target.SetInUse(true);
            Rect2 rect = target.ScreenRect(Camera);
            Cursor = rect.GetCenter();
            Point(new InputEventMouseMotion { Position = Cursor });
        }

        Transform3D head = HeadView();
        Camera.GlobalTransform = target is { MovesCamera: true }
            ? head.InterpolateWith(target.FocusView(Camera), (float)Focus.EasedBlend)
            : head;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Focus.State != FocusState.Free || Camera == null)
        {
            LookTarget = null;
            return;
        }
        Vector3 from = Camera.GlobalPosition;
        var query = PhysicsRayQueryParameters3D.Create(from, from - Camera.GlobalBasis.Z * Reach, PickMask | BlockMask);
        var hit = Camera.GetWorld3D().DirectSpaceState.IntersectRay(query);
        LookTarget = hit.Count > 0 && hit["collider"].AsGodotObject() is Node collider && collider.HasMeta("focus_target")
            ? collider.GetMeta("focus_target").AsGodotObject() as IFocusTarget
            : null;
    }
}
