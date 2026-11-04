using Godot;
using Invasions.Model;

namespace Invasions.World;

/// <summary>
/// The scene side of a <see cref="SpillCrate"/>: a hazard-striped crate with a beacon, sitting where
/// the wrecked container stood. It never despawns; it only goes once a player has emptied it back
/// into storage. It can't be shot or destroyed.
/// </summary>
public partial class SpillCrateNode : StaticBody3D
{
    public SpillCrate Crate;
    Label3D _label;
    MeshInstance3D _beacon;
    float _t;

    public override void _Ready()
    {
        CollisionLayer = Layers.Loot;
        CollisionMask = 0;
        AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1.3f, 1.0f, 1.3f) }, Position = new Vector3(0, 0.5f, 0) });
        Palette.Box(this, new Vector3(0, 0.5f, 0), new Vector3(1.3f, 1.0f, 1.3f), Palette.Textured(Palette.Hazard, new Vector3(3f, 2f, 1f)));
        Palette.Box(this, new Vector3(0, 1.02f, 0), new Vector3(1.36f, 0.08f, 1.36f), Palette.Mat(Palette.Black));
        Palette.Box(this, new Vector3(0, 0.04f, 0), new Vector3(1.36f, 0.08f, 1.36f), Palette.Mat(Palette.Black));
        _beacon = Palette.Box(this, new Vector3(0, 1.18f, 0), new Vector3(0.22f, 0.22f, 0.22f), Palette.Glow(Palette.Acid, 4f));
        _label = new Label3D
        {
            Font = Palette.Font,
            FontSize = 40,
            PixelSize = 0.006f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Modulate = Palette.Yellow,
            OutlineModulate = Palette.Black,
            OutlineSize = 10,
            Position = new Vector3(0, 1.75f, 0),
            NoDepthTest = false,
        };
        AddChild(_label);
        Refresh();
    }

    public void Refresh() => _label.Text = $"SPILL CRATE · {Crate.Total} ITEMS";

    public override void _Process(double delta)
    {
        _t += (float)delta;
        _beacon.Visible = Mathf.PosMod(_t, 1.2f) < 0.7f;
    }
}
