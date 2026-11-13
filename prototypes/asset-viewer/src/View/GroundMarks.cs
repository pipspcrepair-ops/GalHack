using System;
using Godot;

namespace AssetViewer;

/// <summary>
/// A ground line with a tick every half metre, laid along the way the subject faces, in the ground's own frame: when the stage slides the
/// ground under a walker the ticks go with it, so how far a step carries reads against them. Whole metres are taller and brighter.
/// </summary>
public partial class GroundMarks : Node3D
{
    public const float Spacing = 0.5f;
    const int Capacity = 480;

    readonly MultiMesh _ticks;
    readonly MeshInstance3D _line;

    public GroundMarks()
    {
        var unlit = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true };
        _ticks = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = new BoxMesh { Size = Vector3.One }, InstanceCount = Capacity, VisibleInstanceCount = 0 };
        AddChild(new MultiMeshInstance3D { Name = "Ticks", Multimesh = _ticks, MaterialOverride = unlit, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        _line = new MeshInstance3D
        {
            Name = "Line", Mesh = new BoxMesh { Size = Vector3.One }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1f, 0.82f, 0.1f) },
        };
        AddChild(_line);
    }

    /// <summary>Lays the line and its ticks over Z from <paramref name="from"/> to <paramref name="to"/>, with the ground slid by <paramref name="shift"/>; null hides them (no ground to read). <paramref name="unit"/> is the size of a tick, m.</summary>
    public void Lay(Vector3? shift, float from, float to, float unit)
    {
        Visible = shift.HasValue;
        if (shift is not Vector3 s) return;
        to = MathF.Min(to, from + Capacity * Spacing * 0.98f);
        int k0 = (int)MathF.Ceiling((from - s.Z) / Spacing), k1 = (int)MathF.Floor((to - s.Z) / Spacing);
        int n = Math.Clamp(k1 - k0 + 1, 0, Capacity);
        for (int i = 0; i < n; i++)
        {
            int k = k0 + i;
            bool metre = k % 2 == 0;
            float h = metre ? unit * 2f : unit;
            _ticks.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(new Vector3(unit * 4f, h, unit * 0.3f)), new Vector3(0f, s.Y + h * 0.5f, k * Spacing + s.Z)));
            _ticks.SetInstanceColor(i, metre ? new Color(1f, 0.95f, 0.5f) : new Color(0.08f, 0.08f, 0.08f));
        }
        _ticks.VisibleInstanceCount = n;
        _line.Scale = new Vector3(unit * 0.4f, unit * 0.25f, to - from);
        _line.Position = new Vector3(0f, s.Y + unit * 0.12f, (from + to) * 0.5f);
    }
}
