using Godot;

namespace Coop;

/// <summary>
/// The visible body of a player: a suited capsule with a visor, and a name tag for other
/// players. It always lives in the zone's world (cabin worlds are never drawn), so it is placed
/// at frame × feet, where the frame is the ship's transform for someone aboard.
/// </summary>
public partial class AvatarView : Node3D
{
    readonly Color _color;
    readonly string _label;
    Label3D _tag;

    public AvatarView(Color color, string label)
    {
        _color = color;
        _label = label;
    }

    public AvatarView() : this(ShipLayout.Yellow, null) { }

    public string Label
    {
        set { if (_tag != null) _tag.Text = value; }
    }

    public override void _Ready()
    {
        AddChild(new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = 0.35f, Height = 1.8f },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = _color, Roughness = 0.6f },
            Position = new Vector3(0, 0.9f, 0),
        });
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.46f, 0.2f, 0.14f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("1b263b"), Metallic = 0.6f, Roughness = 0.1f },
            Position = new Vector3(0, 1.55f, -0.3f),
        });
        // A backpack, so you can tell which way someone faces from behind.
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.5f, 0.6f, 0.22f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = ShipLayout.Dark, Roughness = 0.7f },
            Position = new Vector3(0, 1.15f, 0.36f),
        });
        if (_label != null)
        {
            _tag = new Label3D
            {
                Text = _label,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                FontSize = 48,
                PixelSize = 0.006f,
                OutlineSize = 10,
                Modulate = new Color("ffe8c2"),
                Position = new Vector3(0, 2.25f, 0),
                NoDepthTest = false,
            };
            AddChild(_tag);
        }
    }

    /// <summary>Places the body: feet in the frame's coordinates, facing yaw, squashed when crouching.</summary>
    public void Place(Transform3D frame, Vector3 feet, float yaw, float pitch, float squash)
    {
        var basis = new Basis(Vector3.Up, yaw) * Basis.FromScale(new Vector3(1f, Mathf.Clamp(squash, 0.4f, 1f), 1f));
        GlobalTransform = frame * new Transform3D(basis, feet);
    }
}
