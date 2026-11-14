using System.Collections.Generic;
using GalHack.Structures;
using Godot;
using Structure = GalHack.Structures.Structure;

namespace AssetViewer.Providers.Parts;

/// <summary>
/// A flame out of each thruster block, drawn as the slice's vessels draw theirs (VesselFx): a cone from the
/// nozzle, its length set by how hard the engine works, 0.3 m to 3 m. The exhaust leaves a thruster's
/// back (+Z) and the push goes the other way (StructureBridge); only the effort is played here.
/// </summary>
public partial class ThrusterFx : Node3D
{
    static readonly Color Flame = new("ffb347");
    static StandardMaterial3D _material;

    readonly List<(MeshInstance3D flame, Vector3 nozzle, Vector3 exhaust)> _flames = new();
    public float Effort;

    public void Setup(Structure s)
    {
        _material ??= new StandardMaterial3D
        {
            AlbedoColor = new Color(Flame, 0.85f), EmissionEnabled = true, Emission = Flame, EmissionEnergyMultiplier = 3f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        float cell = s.Grid.CellSize;
        foreach (var (pos, b) in s.Blocks())
        {
            if (!s.TypeOf(b).IsThruster) continue;
            System.Numerics.Vector3 n = Orientations.ToWorld(b.Rot, Dir.NegZ).Normal();
            Vector3 push = new(n.X, n.Y, n.Z), exhaust = -push;
            var flame = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = cell * 0.32f, BottomRadius = 0.02f, Height = 1f, RadialSegments = 10, Rings = 1 },
                MaterialOverride = _material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Basis = push.Dot(Vector3.Up) < -0.999f ? new Basis(Vector3.Right, Mathf.Pi) : new Basis(new Quaternion(Vector3.Up, push)),
                Visible = false,
            };
            AddChild(flame);
            _flames.Add((flame, new Vector3(pos.X, pos.Y, pos.Z) * cell + exhaust * (cell * 0.5f), exhaust));
        }
    }

    public int Count => _flames.Count;

    public override void _Process(double delta)
    {
        foreach (var (flame, nozzle, exhaust) in _flames)
        {
            float length = Effort > 0.03f ? 0.3f + 2.7f * Effort : 0f;
            flame.Visible = length > 0.05f;
            flame.Scale = new Vector3(1f, Mathf.Max(length, 0.01f), 1f);
            flame.Position = nozzle + exhaust * (length * 0.5f);
        }
    }

    /// <summary>Adds the flames to a structure's asset and the thrust animations that drive them.</summary>
    public static ThrusterFx Add(StructureAsset a)
    {
        var fx = new ThrusterFx { Name = "Thrusters" };
        fx.Setup(a.Structure);
        a.Holder.AddChild(fx);
        if (fx.Count == 0) return fx;
        float t = 0f;
        a.AddAnimation("Thrust", () => t = 0f, dt =>
        {
            t += dt;
            fx.Effort = 0.5f + 0.5f * Mathf.Sin(t * 1.6f - 1.2f);
        }, () => fx.Effort = 0f);
        a.AddAnimation("Full burn", () => fx.Effort = 1f, dt => { }, () => fx.Effort = 0f);
        a.AddAnimation("Idle", () => fx.Effort = 0f, dt => { });
        return fx;
    }
}
