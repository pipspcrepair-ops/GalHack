using System;
using Godot;

namespace DiegeticUi.Screens;

/// <summary>
/// Point and click on in-world screens just by looking at them. While the middle of your view
/// rests on a screen within reach, a cursor sits on its glass at exactly that point: it follows
/// your aim, whatever is under it lights up, and the mouse buttons and wheel act there. Look
/// away, step out of reach or go round the back, and the cursor is gone.
///
/// The screen does the real work (ray to pixel, the pushed mouse events, the cursor in the glass
/// shader). This only decides which screen is under your aim, keeps that screen's cursor on the
/// aim point every frame, sizes it so it reads the same on your view at any distance, and
/// forwards the buttons. Mouse movement keeps turning your head.
///
/// Use: add one beside the camera, call <see cref="HandleInput"/> from _UnhandledInput before
/// your own mouse handling, and set <see cref="Enabled"/> false while something else owns the
/// mouse (a screen framed with E, a full-screen menu).
/// </summary>
public partial class ScreenPointer : Node
{
    public Camera3D Camera { get; set; }

    /// <summary>How far from your eyes a screen can be used, in metres.</summary>
    public float Reach { get; set; } = 2.8f;

    /// <summary>Layers the screens' pick slabs are on.</summary>
    public uint PickMask { get; set; } = WorldScreen.DefaultPickLayer;

    /// <summary>Layers that block the aim (walls, machines), besides the screens.</summary>
    public uint BlockMask { get; set; } = 1;

    /// <summary>The cursor's height on your view, in window pixels, at any distance.</summary>
    public float CursorPixels { get; set; } = 26;

    /// <summary>The screen your aim is on, or null.</summary>
    public WorldScreen Target { get; private set; }

    public bool CursorVisible => Target != null;

    /// <summary>Raised when the cursor moves onto a screen, or off one (null).</summary>
    public event Action<WorldScreen> TargetChanged;

    bool _enabled = true, _fresh;
    float _sizeBefore;

    /// <summary>False hands the mouse to something else: the cursor goes at once.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            if (!value) Release();
        }
    }

    // After whatever places the camera (the walker's look, a camera ease), before ScreenManager
    // decides what draws, so a hover lights up in the same frame. Set ProcessPriority to change it.
    public ScreenPointer() => ProcessPriority = 500;

    public override void _Process(double delta) => Update();

    public override void _ExitTree() => Release();

    /// <summary>
    /// Re-aims from where the camera is now. Runs every frame; call it yourself straight after
    /// moving the camera by hand (a test, a scripted view) to see the result without a frame's wait.
    /// </summary>
    public void Update()
    {
        if (!_enabled || Camera == null || !Camera.IsInsideTree())
        {
            Release();
            return;
        }
        (Vector3 origin, Vector3 direction) = AimRay();
        WorldScreen aimed = Pick(origin, direction);
        if (aimed != Target)
        {
            Release();
            if (aimed != null) Acquire(aimed);
        }
        // The pick slab is the glass's own size, so the hit is on the glass; clamping only keeps a
        // ray grazing in through the slab's edge on the edge pixel instead of flickering off and on.
        if (Target == null || !Target.RayToPixel(origin, direction, out Vector2 pixel, clamp: true)) return;
        Target.CursorSize = CursorSizeOn(Target, origin, pixel);
        // Only tell the page when the cursor really moved: a still aim pushes nothing.
        if (_fresh || pixel.DistanceSquaredTo(Target.CursorPixel) > 0.01f)
        {
            _fresh = false;
            Target.Point(origin, direction, new InputEventMouseMotion());
        }
    }

    /// <summary>
    /// Sends a mouse button or wheel to the screen under your aim, at the cursor. Returns true if
    /// it was used, so the game doesn't also act on it (place a block, fire).
    /// </summary>
    public bool HandleInput(InputEvent e)
    {
        if (Target == null || e is not InputEventMouseButton) return false;
        (Vector3 origin, Vector3 direction) = AimRay();
        Target.Point(origin, direction, e);
        return true;
    }

    /// <summary>Takes the cursor off the screen it is on, if any.</summary>
    public void Release()
    {
        if (Target == null) return;
        WorldScreen was = Target;
        Target = null;
        if (IsInstanceValid(was))
        {
            was.SetInUse(false);
            was.CursorSize = _sizeBefore;
        }
        TargetChanged?.Invoke(null);
    }

    void Acquire(WorldScreen screen)
    {
        Target = screen;
        _sizeBefore = screen.CursorSize;
        _fresh = true;
        screen.SetInUse(true);
        TargetChanged?.Invoke(screen);
    }

    /// <summary>
    /// The ray through the middle of the view as it is drawn this frame: the interpolated camera,
    /// so under physics interpolation the cursor sits exactly under the crosshair.
    /// </summary>
    (Vector3 origin, Vector3 direction) AimRay()
    {
        Transform3D view = Camera.GetGlobalTransformInterpolated();
        return (view.Origin, -view.Basis.Z.Normalized());
    }

    /// <summary>The screen whose glass the aim meets first, within reach and from the front, or null.</summary>
    WorldScreen Pick(Vector3 origin, Vector3 direction)
    {
        var query = PhysicsRayQueryParameters3D.Create(origin, origin + direction * Reach, PickMask | BlockMask);
        Godot.Collections.Dictionary hit = Camera.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0 || hit["collider"].AsGodotObject() is not Node collider || !collider.HasMeta("focus_target")) return null;
        if (collider.GetMeta("focus_target").AsGodotObject() is not WorldScreen screen || !screen.IsVisibleInTree()) return null;
        return (origin - screen.GlobalPosition).Dot(screen.GlobalBasis.Z) > 0 ? screen : null;
    }

    /// <summary>The arrow's height on the page that makes it <see cref="CursorPixels"/> tall on your view.</summary>
    float CursorSizeOn(WorldScreen screen, Vector3 eye, Vector2 pixel)
    {
        float viewHeight = Camera.GetViewport().GetVisibleRect().Size.Y;
        if (viewHeight <= 0) return screen.CursorSize;
        float distance = eye.DistanceTo(screen.PixelToWorld(pixel));
        float metresPerViewPixel = 2 * distance * Mathf.Tan(Mathf.DegToRad(Camera.Fov) / 2) / viewHeight;
        float pagePixelsPerMetre = screen.Pixels.Y / screen.SizeMeters.Y;
        return Mathf.Clamp(CursorPixels * metresPerViewPixel * pagePixelsPerMetre, 16, 160);
    }
}
