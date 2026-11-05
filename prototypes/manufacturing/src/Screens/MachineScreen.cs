using Godot;

namespace Manufacturing.Screens;

/// <summary>What a player does to a screen they are looking at.</summary>
public enum ScreenKey { Up, Down, Left, Right, Tab, Act, ActMany, Cancel, Toggle, Less, More }

/// <summary>
/// A machine's page: a 1280 x 720 drawing that knows when it changed. Pages read the machine's
/// own state every poll and redraw only when its signature changes.
/// </summary>
public abstract partial class MachinePage : Control
{
    public const int W = 1280, H = 720;
    int _seen = int.MinValue;
    bool _focused;

    /// <summary>The line the HUD shows while looking at the screen.</summary>
    public abstract string Prompt { get; }

    /// <summary>A number that changes whenever anything on the page would.</summary>
    protected abstract int Signature();

    public bool Focused
    {
        get => _focused;
        set { if (_focused != value) { _focused = value; QueueRedraw(); _seen = int.MinValue; } }
    }

    /// <summary>Redraws if anything changed; true if it did.</summary>
    public bool Refresh()
    {
        int s = System.HashCode.Combine(Signature(), _focused);
        if (s == _seen) return false;
        _seen = s;
        QueueRedraw();
        return true;
    }

    /// <summary>Handles a key while focused; returns a line for the HUD, or null.</summary>
    public virtual string Handle(ScreenKey key) => null;

    protected static int Wrap(int i, int n) => n <= 0 ? 0 : (i % n + n) % n;
}

/// <summary>
/// A screen mounted on a machine: its page drawn into a SubViewport and shown on a panel through
/// diegetic-ui's glass shader. The SubViewport draws only when the page changed, polled ten times
/// a second, so idle screens cost nothing. Look at it from close by to use it.
/// </summary>
public partial class MachineScreen : Node3D
{
    /// <summary>Physics layer of the invisible slab used to pick screens by looking at them.</summary>
    public const uint PickLayer = 1u << 3;

    public MachinePage Page { get; init; }
    public Vector2 SizeMeters { get; init; } = new(1.6f, 0.9f);
    public SubViewport Viewport { get; private set; }
    public int Redraws { get; private set; }

    ShaderMaterial _shader;
    StandardMaterial3D _bezel;
    double _poll;

    public override void _Ready()
    {
        Viewport = new SubViewport
        {
            Size = new Vector2I(MachinePage.W, MachinePage.H),
            Disable3D = true,
            TransparentBg = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
            Msaa2D = Godot.Viewport.Msaa.Msaa2X,
            CanvasItemDefaultTextureFilter = Godot.Viewport.DefaultCanvasItemTextureFilter.Linear,
        };
        AddChild(Viewport);
        Page.Size = new Vector2(MachinePage.W, MachinePage.H);
        Viewport.AddChild(Page);

        Material material;
        Shader shader = Kit.SurfaceShader();
        if (shader != null)
        {
            _shader = new ShaderMaterial { Shader = shader };
            _shader.SetShaderParameter("content", Viewport.GetTexture());
            _shader.SetShaderParameter("pixels", new Vector2(MachinePage.W, MachinePage.H));
            _shader.SetShaderParameter("cursor_on", 0f);
            material = _shader;
        }
        else material = new StandardMaterial3D { AlbedoTexture = Viewport.GetTexture(), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        AddChild(new MeshInstance3D { Mesh = new QuadMesh { Size = SizeMeters }, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });

        _bezel = new StandardMaterial3D { AlbedoColor = new Color("15171c"), Roughness = 0.45f, Metallic = 0.3f };
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(SizeMeters.X + 0.09f, SizeMeters.Y + 0.09f, 0.05f) },
            MaterialOverride = _bezel,
            Position = new Vector3(0, 0, -0.027f),
        });

        var pick = new StaticBody3D { CollisionLayer = PickLayer, CollisionMask = 0 };
        pick.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(SizeMeters.X, SizeMeters.Y, 0.04f) } });
        pick.SetMeta("screen", this);
        AddChild(pick);
    }

    public bool Focused
    {
        get => Page.Focused;
        set
        {
            Page.Focused = value;
            _bezel.AlbedoColor = value ? new Color("ffb400") : new Color("15171c");
            _bezel.EmissionEnabled = value;
            _bezel.Emission = new Color("7a5200");
        }
    }

    public override void _Process(double delta)
    {
        _poll -= delta;
        if (_poll > 0) return;
        _poll = 0.1;
        if (Page.Refresh())
        {
            Viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
            Redraws++;
        }
    }
}
