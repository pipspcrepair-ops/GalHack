using System;
using Godot;
using Slice.Items;

namespace Slice.Player;

/// <summary>
/// The first-person hands: a gloved hand holding the survival tool low on the right of the view,
/// its emitter lit by mode (teal to gather, red to shoot, amber to build), kicking back on each
/// shot and swaying a little as you walk. Shown when the tool, a block, a core or a device is in
/// hand, hidden in a seat or in third person. Procedural boxes and cylinders, no assets.
/// </summary>
public partial class Hands : Node3D
{
    public Game.Game Game;

    Node3D _rig;
    StandardMaterial3D _glow;
    int _shotsSeen;
    float _kick, _bob;

    /// <summary>The muzzle's offset in the camera's frame (matches <see cref="SurvivalTool.Muzzle"/>).</summary>
    public static readonly Vector3 MuzzleOffset = new(0.17f, -0.15f, -0.55f);

    public override void _Ready()
    {
        _rig = new Node3D { Name = "Rig", Scale = Vector3.One * 0.75f, Position = new Vector3(0.05f, -0.03f, 0f) };
        AddChild(_rig);
        var suit = new StandardMaterial3D { AlbedoColor = new Color("e85d04"), Roughness = 0.7f };
        var glove = new StandardMaterial3D { AlbedoColor = new Color("2b2d42"), Roughness = 0.8f };
        var metal = new StandardMaterial3D { AlbedoColor = new Color("ece3c8"), Roughness = 0.4f, Metallic = 0.4f };
        var dark = new StandardMaterial3D { AlbedoColor = new Color("1b263b"), Roughness = 0.6f };
        _glow = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color("2ec4b6") };
        // Forearm and glove.
        Add(new CylinderMesh { TopRadius = 0.045f, BottomRadius = 0.055f, Height = 0.32f }, suit, new Vector3(0.21f, -0.24f, -0.18f), new Vector3(-75, 8, 0));
        Add(new BoxMesh { Size = new Vector3(0.09f, 0.07f, 0.1f) }, glove, new Vector3(0.185f, -0.19f, -0.33f), Vector3.Zero);
        // The tool: body, grip, barrel, emitter ring.
        Add(new BoxMesh { Size = new Vector3(0.08f, 0.09f, 0.26f) }, metal, new Vector3(0.17f, -0.15f, -0.38f), Vector3.Zero);
        Add(new BoxMesh { Size = new Vector3(0.05f, 0.11f, 0.05f) }, dark, new Vector3(0.17f, -0.21f, -0.33f), new Vector3(15, 0, 0));
        Add(new CylinderMesh { TopRadius = 0.022f, BottomRadius = 0.026f, Height = 0.14f }, dark, new Vector3(0.17f, -0.145f, -0.55f + 0.07f), new Vector3(90, 0, 0));
        Add(new TorusMesh { InnerRadius = 0.022f, OuterRadius = 0.036f, Rings = 16, RingSegments = 6 }, _glow, new Vector3(0.17f, -0.145f, -0.545f), new Vector3(90, 0, 0));
        Add(new BoxMesh { Size = new Vector3(0.03f, 0.02f, 0.1f) }, _glow, new Vector3(0.17f, -0.1f, -0.38f), Vector3.Zero);
        foreach (Node n in _rig.GetChildren())
            if (n is MeshInstance3D m) m.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        Visible = false;
    }

    void Add(Mesh mesh, Material material, Vector3 at, Vector3 degrees) =>
        _rig.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = material, Position = at, RotationDegrees = degrees });

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        var g = Game;
        HandKind hand = g?.Pack?.Hand ?? HandKind.Empty;
        bool show = g?.Survivor != null && g.Survivor.Seated == null && !g.ThirdPerson && g.FixedCamera == null && !g.Suit.Dead
                    && hand is HandKind.Tool or HandKind.Block or HandKind.Core or HandKind.Device or HandKind.Blueprint;
        Visible = show;
        if (!show) return;
        _glow.AlbedoColor = hand != HandKind.Tool ? new Color("ffb703") : g.Tool.Mode == ToolMode.Shoot ? new Color("ef233c") : new Color("2ec4b6");
        if (g.Tool.Shots != _shotsSeen)
        {
            _shotsSeen = g.Tool.Shots;
            _kick = 1f;
        }
        _kick = Mathf.MoveToward(_kick, 0f, dt * 7f);
        float speed = g.Survivor.Body.OnFloor ? new Vector2(g.Survivor.Body.Velocity.X, g.Survivor.Body.Velocity.Z).Length() : 0f;
        _bob += dt * speed * 1.6f;
        float sway = MathF.Min(speed, 6f) / 6f;
        _rig.Position = new Vector3(0.05f + MathF.Sin(_bob) * 0.008f * sway, -0.03f + MathF.Abs(MathF.Cos(_bob)) * 0.01f * sway, _kick * 0.05f);
        _rig.RotationDegrees = new Vector3(_kick * 6f, 0f, 0f);
    }
}
