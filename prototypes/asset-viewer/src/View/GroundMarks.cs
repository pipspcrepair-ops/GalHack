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

    /// <summary>Lays the line and its ticks <paramref name="reach"/> either way of the subject at <paramref name="at"/> facing <paramref name="heading"/>, with the ground slid by <paramref name="shift"/>; null hides them (no ground to read). <paramref name="unit"/> is the size of a tick, m.</summary>
    public void Lay(Vector3? shift, Vector3 at, float heading, float reach, float unit)
    {
        Visible = shift.HasValue;
        if (shift is not Vector3 s) return;
        // The line runs the way the subject faces, through its feet; ticks are at whole half metres of ground along it.
        Position = new Vector3(at.X, 0f, at.Z);
        Rotation = new Vector3(0f, heading, 0f);
        Vector3 back = new(MathF.Sin(heading), 0f, MathF.Cos(heading));
        float along = (at.X - s.X) * back.X + (at.Z - s.Z) * back.Z;
        float to = MathF.Min(reach, Capacity * Spacing * 0.49f), from = -to;
        int k0 = (int)MathF.Ceiling((from + along) / Spacing), k1 = (int)MathF.Floor((to + along) / Spacing);
        int n = Math.Clamp(k1 - k0 + 1, 0, Capacity);
        for (int i = 0; i < n; i++)
        {
            int k = k0 + i;
            bool metre = k % 2 == 0;
            float h = metre ? unit * 2f : unit;
            _ticks.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(new Vector3(unit * 4f, h, unit * 0.3f)), new Vector3(0f, s.Y + h * 0.5f, k * Spacing - along)));
            _ticks.SetInstanceColor(i, metre ? new Color(1f, 0.95f, 0.5f) : new Color(0.08f, 0.08f, 0.08f));
        }
        _ticks.VisibleInstanceCount = n;
        _line.Scale = new Vector3(unit * 0.4f, unit * 0.25f, to - from);
        _line.Position = new Vector3(0f, s.Y + unit * 0.12f, (from + to) * 0.5f);
    }
}
