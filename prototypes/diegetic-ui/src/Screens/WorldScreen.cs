using System;
using System.Collections.Generic;
using DiegeticUi.Theme;
using Godot;

namespace DiegeticUi.Screens;

/// <summary>A SubViewport that knows which world screen it belongs to, so widgets can find it.</summary>
public partial class ScreenViewport : SubViewport
{
    public WorldScreen Screen { get; init; }
}

/// <summary>
/// A screen that belongs to an object in the world: a 2D page drawn into a SubViewport and
/// shown on a flat panel through the scanline-and-glow surface shader.
///
/// Use it by looking at it and pressing E (see <see cref="Interactor"/>). While it is in use,
/// mouse events arrive as world-space rays; they are intersected with the panel's plane,
/// turned into UV and then viewport pixels, and pushed into the SubViewport as ordinary mouse
/// events, so stock Controls and our own widgets work unchanged.
///
/// Drawing is the expensive part on a low-end PC, so the SubViewport is normally switched off
/// (UpdateMode.Disabled). It redraws once when its content changes and it can be seen
/// (<see cref="ScreenManager"/> decides, within a per-frame budget). The cursor, scanlines and
/// flicker live in the surface shader, so moving the mouse or time passing costs no redraw.
///
/// Local space: the panel lies in the XY plane facing +Z, centred on the origin.
/// </summary>
public partial class WorldScreen : Node3D, IFocusTarget
{
    /// <summary>Physics layer of the invisible slab used to pick screens by looking at them.</summary>
    public const uint PickLayer = 1u << 1;

    /// <summary>Every screen in the tree, for the manager. Plain list: there are only ever a handful.</summary>
    public static readonly List<WorldScreen> All = new();

    public Vector2I Pixels { get; init; } = new(1280, 720);
    public Vector2 SizeMeters { get; init; } = new(1.28f, 0.72f);
    public string Prompt { get; set; } = "SCREEN";
    /// <summary>False for screens you never walk up to (the wrist): no pick slab.</summary>
    public bool Pickable { get; init; } = true;
    public bool MovesCamera { get; init; } = true;
    /// <summary>A chunky dark frame around the glass, in metres. 0 for none.</summary>
    public float Bezel { get; init; } = 0.045f;
    /// <summary>How much of the view the panel fills when framed for use.</summary>
    public float FocusFill { get; init; } = 0.8f;
    /// <summary>Content polling rate, which caps how often a busy page (a progress bar) redraws.</summary>
    public double RefreshHz { get; set; } = 12;
    /// <summary>Beyond this the text can't be read anyway, so changes wait until you come closer.</summary>
    public float ReadableDistance { get; set; } = 22f;

    public ScreenViewport Viewport { get; private set; }
    public MeshInstance3D Surface { get; private set; }
    public Control Content { get; private set; }

    public bool InUse { get; private set; }
    public bool Dirty { get; private set; } = true;
    public bool EverDrawn { get; private set; }
    /// <summary>How many times the SubViewport has been asked to draw. Tests read this.</summary>
    public int Redraws { get; private set; }
    public Vector2 CursorPixel { get; private set; } = new(-100, -100);

    ShaderMaterial _material;
    bool _renderQueued;
    double _nextPoll;
    Vector2 _lastPointer;
    MouseButtonMask _held;

    public override void _EnterTree() => All.Add(this);
    public override void _ExitTree() => All.Remove(this);

    public override void _Ready()
    {
        Viewport = new ScreenViewport
        {
            Name = "Viewport",
            Screen = this,
            Size = Pixels,
            Disable3D = true,
            TransparentBg = false,
            Msaa2D = Godot.Viewport.Msaa.Msaa2X,
            HandleInputLocally = true,
            GuiEmbedSubwindows = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            CanvasItemDefaultTextureFilter = Godot.Viewport.DefaultCanvasItemTextureFilter.Linear,
        };
        AddChild(Viewport);

        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/screen_surface.gdshader") };
        _material.SetShaderParameter("content", Viewport.GetTexture());
        _material.SetShaderParameter("pixels", (Vector2)Pixels);
        _material.SetShaderParameter("cursor_on", 0f);
        Surface = new MeshInstance3D { Name = "Surface", Mesh = Quad(SizeMeters), MaterialOverride = _material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(Surface);

        if (Bezel > 0)
        {
            var frame = new MeshInstance3D
            {
                Name = "Bezel",
                Mesh = new BoxMesh { Size = new Vector3(SizeMeters.X + Bezel * 2, SizeMeters.Y + Bezel * 2, 0.05f) },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("15171c"), Roughness = 0.45f, Metallic = 0.3f },
                Position = new Vector3(0, 0, -0.027f),
            };
            AddChild(frame);
        }

        if (Pickable)
        {
            // Only for "what am I looking at". The exact hit point comes from the plane maths
            // below, so collider margins can never cost cursor accuracy.
            var pick = new StaticBody3D { Name = "Pick", CollisionLayer = PickLayer, CollisionMask = 0 };
            pick.AddChild(new CollisionShape3D
            {
                Shape = new BoxShape3D { Size = new Vector3(SizeMeters.X, SizeMeters.Y, 0.02f) },
                Position = new Vector3(0, 0, -0.01f),
            });
            pick.SetMeta("focus_target", this);
            AddChild(pick);
        }
    }

    /// <summary>Puts a page on the screen. Its size is set to the screen's pixels.</summary>
    public void SetContent(Control content)
    {
        if (Content != null) Viewport.RemoveChild(Content);
        Content = content;
        content.Theme = UiTheme.GodotTheme;
        content.Size = Pixels;
        Viewport.AddChild(content);
        MarkDirty();
    }

    public void MarkDirty() => Dirty = true;

    /// <summary>Marks the screen showing this node (if any) for a redraw. Widgets call it on any visual change.</summary>
    public static void MarkDirtyFor(Node node)
    {
        if (node.IsInsideTree() && node.GetViewport() is ScreenViewport { Screen: { } screen }) screen.MarkDirty();
    }

    /// <summary>0..1: a red pulse around the glass during an alert. Lives in the shader, so it costs no redraw.</summary>
    public void SetAlert(float amount) => _material.SetShaderParameter("alert", amount);

    // ---- Redraw policy (driven by ScreenManager) -----------------------------------------

    /// <summary>Polls the page at <see cref="RefreshHz"/>; a page that changed marks the screen dirty.</summary>
    internal void Poll(double now)
    {
        if (now < _nextPoll) return;
        _nextPoll = now + 1.0 / RefreshHz;
        if (Content is IScreenPage page && page.Refresh()) MarkDirty();
    }

    /// <summary>When it last drew, in seconds (the manager's clock).</summary>
    public double LastRender { get; private set; } = double.NegativeInfinity;

    /// <summary>Asks the SubViewport to draw exactly once, at the end of this frame.</summary>
    internal void Render(double now)
    {
        LastRender = now;
        Viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        _renderQueued = true;
        Dirty = false;
        EverDrawn = true;
        Redraws++;
    }

    /// <summary>Switches drawing back off after a one-off draw.</summary>
    internal void Rest()
    {
        if (!_renderQueued) return;
        Viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
        _renderQueued = false;
    }

    /// <summary>
    /// Could this camera read the screen right now? In the tree and visible, near enough,
    /// facing it, and inside the view frustum (grown by a margin, so a screen coming into view
    /// is already drawn when it gets there).
    /// </summary>
    public bool IsSeenBy(Camera3D camera, float margin = 0.4f)
    {
        if (camera == null || !IsVisibleInTree()) return false;
        Vector3 toCamera = camera.GlobalPosition - GlobalPosition;
        if (toCamera.Length() > ReadableDistance) return false;
        if (toCamera.Dot(GlobalBasis.Z) <= 0) return false; // looking at its back

        Span<Vector3> corners = stackalloc Vector3[4];
        Corners(corners, margin);
        foreach (Plane plane in camera.GetFrustum())
        {
            bool allOutside = true;
            foreach (Vector3 c in corners)
                if (!plane.IsPointOver(c)) { allOutside = false; break; }
            if (allOutside) return false;
        }
        return true;
    }

    void Corners(Span<Vector3> corners, float margin = 0)
    {
        float hx = SizeMeters.X / 2 + margin, hy = SizeMeters.Y / 2 + margin;
        Transform3D t = GlobalTransform;
        corners[0] = t * new Vector3(-hx, hy, 0);
        corners[1] = t * new Vector3(hx, hy, 0);
        corners[2] = t * new Vector3(hx, -hy, 0);
        corners[3] = t * new Vector3(-hx, -hy, 0);
    }

    // ---- Ray to pixel ----------------------------------------------------------------------

    /// <summary>
    /// Intersects a world-space ray with the panel and returns the viewport pixel it hits.
    /// With <paramref name="clamp"/>, a ray just off the edge lands on the nearest edge pixel
    /// (so a drag that strays off the glass keeps going); without, it misses.
    /// </summary>
    public bool RayToPixel(Vector3 origin, Vector3 direction, out Vector2 pixel, bool clamp = false)
    {
        pixel = default;
        Transform3D toLocal = GlobalTransform.AffineInverse();
        Vector3 o = toLocal * origin;
        Vector3 d = toLocal.Basis * direction;
        if (Mathf.Abs(d.Z) < 1e-7f) return false;   // parallel to the glass
        float t = -o.Z / d.Z;
        if (t <= 0) return false;                     // the panel is behind the ray
        Vector3 p = o + d * t;
        var uv = new Vector2(p.X / SizeMeters.X + 0.5f, 0.5f - p.Y / SizeMeters.Y);
        bool inside = uv.X >= 0 && uv.X <= 1 && uv.Y >= 0 && uv.Y <= 1;
        if (!inside && !clamp) return false;
        uv = uv.Clamp(Vector2.Zero, Vector2.One);
        pixel = uv * (Vector2)Pixels;
        return true;
    }

    /// <summary>The world-space point on the panel at a viewport pixel. The inverse of <see cref="RayToPixel"/>.</summary>
    public Vector3 PixelToWorld(Vector2 pixel)
    {
        Vector2 uv = pixel / (Vector2)Pixels;
        return GlobalTransform * new Vector3((uv.X - 0.5f) * SizeMeters.X, (0.5f - uv.Y) * SizeMeters.Y, 0);
    }

    // ---- IFocusTarget ----------------------------------------------------------------------

    /// <summary>Square on to the panel, just far enough back for it to fill <see cref="FocusFill"/> of the view.</summary>
    public Transform3D FocusView(Camera3D camera)
    {
        float tanHalf = Mathf.Tan(Mathf.DegToRad(camera.Fov) / 2);
        Vector2 view = camera.GetViewport().GetVisibleRect().Size;
        float aspect = view.Y > 0 ? view.X / view.Y : 16f / 9f;
        float fitHeight = SizeMeters.Y / 2 / (tanHalf * FocusFill);
        float fitWidth = SizeMeters.X / 2 / (tanHalf * aspect * FocusFill);
        return GlobalTransform * new Transform3D(Basis.Identity, new Vector3(0, 0, Mathf.Max(fitHeight, fitWidth)));
    }

    public Rect2 ScreenRect(Camera3D camera)
    {
        Span<Vector3> corners = stackalloc Vector3[4];
        Corners(corners);
        var rect = new Rect2();
        for (int i = 0; i < 4; i++)
        {
            if (camera.IsPositionBehind(corners[i])) return camera.GetViewport().GetVisibleRect();
            Vector2 p = camera.UnprojectPosition(corners[i]);
            rect = i == 0 ? new Rect2(p, Vector2.Zero) : rect.Expand(p);
        }
        return rect;
    }

    public void SetInUse(bool inUse)
    {
        if (InUse == inUse) return;
        InUse = inUse;
        _material.SetShaderParameter("cursor_on", inUse ? 1f : 0f);
        if (inUse) Viewport.NotifyMouseEntered();
        else
        {
            // Let go of anything still held, so a widget is never left mid-drag.
            if (_held.HasFlag(MouseButtonMask.Left))
                Viewport.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = _lastPointer, GlobalPosition = _lastPointer }, true);
            _held = 0;
            Viewport.NotifyMouseExited();
        }
        MarkDirty();
    }

    public void Point(Vector3 origin, Vector3 direction, InputEvent e)
    {
        if (!RayToPixel(origin, direction, out Vector2 pixel, clamp: true)) return;
        CursorPixel = pixel;
        _material.SetShaderParameter("cursor", pixel);

        InputEvent local = null;
        if (e is InputEventMouseMotion)
        {
            local = new InputEventMouseMotion
            {
                Position = pixel, GlobalPosition = pixel, Relative = pixel - _lastPointer, ButtonMask = _held,
            };
        }
        else if (e is InputEventMouseButton button)
        {
            MouseButtonMask bit = button.ButtonIndex switch
            {
                MouseButton.Left => MouseButtonMask.Left,
                MouseButton.Right => MouseButtonMask.Right,
                MouseButton.Middle => MouseButtonMask.Middle,
                _ => 0,
            };
            _held = button.Pressed ? _held | bit : _held & ~bit;
            local = new InputEventMouseButton
            {
                Position = pixel, GlobalPosition = pixel, ButtonIndex = button.ButtonIndex,
                Pressed = button.Pressed, ButtonMask = _held, DoubleClick = button.DoubleClick, Factor = button.Factor,
            };
            MarkDirty(); // presses nearly always change something
        }
        _lastPointer = pixel;
        if (local != null) Viewport.PushInput(local, true);
    }

    /// <summary>A quad facing +Z with UV (0,0) at the top left, wound clockwise for Godot's front face.</summary>
    static ArrayMesh Quad(Vector2 size)
    {
        float hx = size.X / 2, hy = size.Y / 2;
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = new[] { new Vector3(-hx, hy, 0), new Vector3(hx, hy, 0), new Vector3(hx, -hy, 0), new Vector3(-hx, -hy, 0) };
        arrays[(int)Mesh.ArrayType.Normal] = new[] { Vector3.Back, Vector3.Back, Vector3.Back, Vector3.Back };
        arrays[(int)Mesh.ArrayType.TexUV] = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        arrays[(int)Mesh.ArrayType.Index] = new[] { 0, 1, 2, 0, 2, 3 };
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}
