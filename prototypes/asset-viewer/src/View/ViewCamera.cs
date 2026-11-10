using System;
using Godot;

namespace AssetViewer;

/// <summary>
/// The viewer's camera, in two modes.
///
/// Orbit (default): it circles a target. Left-drag turns, right- or middle-drag pans, the wheel zooms,
/// W/S zoom, A/D and Q/E pan, the arrow keys turn, Shift is fast.
///
/// Free: a fly camera. Hold left or right mouse to look, W A S D to move, E/Space up and Q/Ctrl down,
/// the wheel sets the speed, Shift is fast.
///
/// The orbit's target sits in the middle of the free part of the window (between the two panels):
/// <see cref="ScreenShift"/> slides the picture sideways by the difference of the panel widths.
/// </summary>
public partial class ViewCamera : Camera3D
{
    public bool IsFree { get; private set; }

    // Orbit state.
    public Vector3 Target;
    public float Yaw = 0.6f, Pitch = -0.3f, Distance = 4f;
    public float MinDistance = 0.05f, MaxDistance = 20000f;
    /// <summary>Slow automatic drift of the yaw (radians a second), for things that can't spin themselves.</summary>
    public float Drift;

    // Free-camera state.
    public float FlySpeed = 4f;
    float _freeYaw, _freePitch;

    /// <summary>Fraction of the window width to slide the picture by (positive moves it right). Set by the viewer from its panels.</summary>
    public float ScreenShift;

    /// <summary>The gui control that has the keyboard, so typing in a comment box doesn't fly the camera.</summary>
    public Func<bool> KeysBlocked = () => false;
    /// <summary>True while a list has the keyboard, so the arrow keys move its selection instead of the camera.</summary>
    public Func<bool> ArrowsBlocked = () => false;
    /// <summary>True while the pointer is over the 3D area (not a panel).</summary>
    public Func<bool> PointerInView = () => true;

    bool _looking, _panning;

    public override void _Ready()
    {
        Fov = 70f;
        Near = 0.02f;
        Far = 60000f;
        Current = true;
    }

    // ---- poses ------------------------------------------------------------------------------------

    /// <summary>Frames a sphere: orbit mode, looking at its centre from a pleasant angle.</summary>
    public void Frame(Vector3 centre, float radius, float aspect = 1.6f, bool keepAngles = false)
    {
        SetFree(false);
        Target = centre;
        radius = Mathf.Max(radius, 0.1f);
        // Fit the sphere in both directions of the visible area, with a margin.
        float half = Mathf.DegToRad(Fov) * 0.5f;
        float halfWide = Mathf.Atan(Mathf.Tan(half) * aspect);
        Distance = radius / Mathf.Sin(Mathf.Min(half, halfWide)) * 1.45f;
        if (!keepAngles) { Yaw = 0.6f; Pitch = -0.28f; }
        ApplyOrbit();
    }

    public void SetFree(bool free)
    {
        if (free == IsFree) return;
        if (free)
        {
            // Carry the orbit pose over.
            _freeYaw = Yaw;
            _freePitch = Pitch;
            IsFree = true;
        }
        else
        {
            // Re-orbit the point straight ahead at the current distance from the target.
            Vector3 forward = -GlobalTransform.Basis.Z;
            float d = Mathf.Clamp(Position.DistanceTo(Target), MinDistance, MaxDistance);
            Target = Position + forward * d;
            Vector3 offset = Position - Target;
            Distance = Mathf.Max(offset.Length(), 1e-4f);
            Yaw = Mathf.Atan2(offset.X, offset.Z);
            Pitch = -Mathf.Asin(Mathf.Clamp(offset.Y / Distance, -1f, 1f));
            IsFree = false;
        }
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _looking = _panning = false;
    }

    /// <summary>Puts the free camera somewhere, looking at a point.</summary>
    public void PlaceFree(Vector3 from, Vector3 look)
    {
        IsFree = true;
        Position = from;
        Vector3 d = (look - from).Normalized();
        _freeYaw = Mathf.Atan2(-d.X, -d.Z);
        _freePitch = Mathf.Asin(Mathf.Clamp(d.Y, -1f, 1f));
        ApplyFree();
    }

    public float FreeYaw => _freeYaw;
    public float FreePitch => _freePitch;
    public void SetFreeAngles(float yaw, float pitch) { _freeYaw = yaw; _freePitch = pitch; ApplyFree(); }

    void ApplyOrbit()
    {
        Distance = Mathf.Clamp(Distance, MinDistance, MaxDistance);
        Pitch = Mathf.Clamp(Pitch, -1.5f, 1.5f);
        // Pitch is negative when looking down on the target from above.
        float elevation = -Pitch;
        Vector3 offset = Distance * new Vector3(Mathf.Cos(elevation) * Mathf.Sin(Yaw), Mathf.Sin(elevation), Mathf.Cos(elevation) * Mathf.Cos(Yaw));
        Basis b = Basis.LookingAt(-offset.Normalized(), Vector3.Up);
        Vector3 from = Target + offset;
        Vector3 right = b.X;
        float viewport = Mathf.Max(1f, GetViewport()?.GetVisibleRect().Size.X ?? 1f);
        float aspect = viewport / Mathf.Max(1f, GetViewport()?.GetVisibleRect().Size.Y ?? 1f);
        float widthAtTarget = 2f * Distance * Mathf.Tan(Mathf.DegToRad(Fov) * 0.5f) * aspect;
        GlobalTransform = new Transform3D(b, from - right * ScreenShift * widthAtTarget);
        Near = Mathf.Clamp(Distance * 0.01f, 0.01f, 5f);
    }

    void ApplyFree()
    {
        _freePitch = Mathf.Clamp(_freePitch, -1.55f, 1.55f);
        Rotation = new Vector3(_freePitch, _freeYaw, 0f);
        Near = 0.03f;
    }

    // ---- input ------------------------------------------------------------------------------------

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb)
        {
            if (mb.Pressed && !PointerInView()) return;
            switch (mb.ButtonIndex)
            {
                case MouseButton.Left:
                case MouseButton.Right:
                    if (IsFree)
                    {
                        _looking = mb.Pressed;
                        Input.MouseMode = _looking ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
                    }
                    else if (mb.ButtonIndex == MouseButton.Left) { _looking = mb.Pressed; }
                    else { _panning = mb.Pressed; }
                    break;
                case MouseButton.Middle:
                    _panning = mb.Pressed && !IsFree;
                    break;
                case MouseButton.WheelUp when mb.Pressed: Wheel(1); break;
                case MouseButton.WheelDown when mb.Pressed: Wheel(-1); break;
            }
            if (mb.Pressed) GetViewport().GuiReleaseFocus();
        }
        else if (e is InputEventMouseMotion mm)
        {
            if (IsFree && _looking && Input.MouseMode == Input.MouseModeEnum.Captured)
            {
                _freeYaw -= mm.Relative.X * 0.0028f;
                _freePitch -= mm.Relative.Y * 0.0028f;
                ApplyFree();
            }
            else if (!IsFree && _looking)
            {
                Yaw -= mm.Relative.X * 0.007f;
                Pitch -= mm.Relative.Y * 0.007f;
                ApplyOrbit();
            }
            else if (!IsFree && _panning) PanScreen(mm.Relative);
        }
    }

    void Wheel(int dir)
    {
        if (IsFree) FlySpeed = Mathf.Clamp(FlySpeed * (dir > 0 ? 1.25f : 0.8f), 0.05f, 4000f);
        else
        {
            Distance *= dir > 0 ? 0.88f : 1.14f;
            ApplyOrbit();
        }
    }

    void PanScreen(Vector2 relative)
    {
        float h = Mathf.Max(1f, GetViewport().GetVisibleRect().Size.Y);
        float perPixel = 2f * Distance * Mathf.Tan(Mathf.DegToRad(Fov) * 0.5f) / h;
        Target += (-GlobalTransform.Basis.X * relative.X + GlobalTransform.Basis.Y * relative.Y) * perPixel;
        ApplyOrbit();
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        bool blocked = KeysBlocked();
        bool shift = Input.IsKeyPressed(Key.Shift);
        if (IsFree)
        {
            if (!blocked)
            {
                Vector3 move = Vector3.Zero;
                Basis b = GlobalTransform.Basis;
                if (Input.IsKeyPressed(Key.W)) move -= b.Z;
                if (Input.IsKeyPressed(Key.S)) move += b.Z;
                if (Input.IsKeyPressed(Key.A)) move -= b.X;
                if (Input.IsKeyPressed(Key.D)) move += b.X;
                if (Input.IsKeyPressed(Key.E) || Input.IsKeyPressed(Key.Space)) move += Vector3.Up;
                if (Input.IsKeyPressed(Key.Q) || Input.IsKeyPressed(Key.Ctrl)) move -= Vector3.Up;
                if (move != Vector3.Zero) Position += move.Normalized() * FlySpeed * (shift ? 4f : 1f) * dt;
                bool arrows = !ArrowsBlocked();
                float turn = arrows ? (Input.IsKeyPressed(Key.Left) ? 1 : 0) - (Input.IsKeyPressed(Key.Right) ? 1 : 0) : 0;
                float tilt = arrows ? (Input.IsKeyPressed(Key.Up) ? 1 : 0) - (Input.IsKeyPressed(Key.Down) ? 1 : 0) : 0;
                if (turn != 0 || tilt != 0) { _freeYaw += turn * 1.4f * dt; _freePitch += tilt * 1.0f * dt; ApplyFree(); }
            }
            return;
        }

        bool moved = false;
        if (!blocked)
        {
            float fast = shift ? 3f : 1f;
            bool arrows = !ArrowsBlocked();
            float turn = arrows ? (Input.IsKeyPressed(Key.Left) ? 1 : 0) - (Input.IsKeyPressed(Key.Right) ? 1 : 0) : 0;
            float tilt = arrows ? (Input.IsKeyPressed(Key.Up) ? 1 : 0) - (Input.IsKeyPressed(Key.Down) ? 1 : 0) : 0;
            if (turn != 0 || tilt != 0) { Yaw += turn * 1.4f * dt * fast; Pitch += tilt * 1.0f * dt * fast; moved = true; }
            float zoom = (Input.IsKeyPressed(Key.S) ? 1 : 0) - (Input.IsKeyPressed(Key.W) ? 1 : 0);
            if (zoom != 0) { Distance *= Mathf.Pow(1.9f, zoom * dt * fast); moved = true; }
            float side = (Input.IsKeyPressed(Key.D) ? 1 : 0) - (Input.IsKeyPressed(Key.A) ? 1 : 0);
            float rise = (Input.IsKeyPressed(Key.E) ? 1 : 0) - (Input.IsKeyPressed(Key.Q) ? 1 : 0);
            if (side != 0 || rise != 0)
            {
                Target += (GlobalTransform.Basis.X * side + Vector3.Up * rise) * Distance * 0.6f * fast * dt;
                moved = true;
            }
        }
        if (Drift != 0f && !_looking && !_panning) { Yaw += Drift * dt; moved = true; }
        // The panels can resize the window, so keep the shift current.
        if (moved || ScreenShift != _lastShift || GetViewport().GetVisibleRect().Size != _lastSize) ApplyOrbit();
        _lastShift = ScreenShift;
        _lastSize = GetViewport().GetVisibleRect().Size;
    }

    float _lastShift;
    Vector2 _lastSize;

    /// <summary>Re-applies the orbit pose (after Target/Yaw/Pitch/Distance were set from outside).</summary>
    public void Refresh() { if (IsFree) ApplyFree(); else ApplyOrbit(); }

    public void ApplyFov(float degrees)
    {
        Fov = degrees;
        Refresh();
    }
}
