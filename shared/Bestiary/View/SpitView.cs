using System;
using Godot;

namespace Wildlife;

/// <summary>
/// What's in the air from their throats: a few wet blobs on one multimesh, each gob a blob and
/// the drops strung out behind it, in the colour of what it is.
/// </summary>
public partial class SpitView : Node3D
{
    const int MostGobs = 12, DropsEach = 5;
    MultiMeshInstance3D _blobs;

    public override void _Ready()
    {
        var mesh = new SphereMesh { Radius = 0.09f, Height = 0.18f, RadialSegments = 7, Rings = 4 };
        mesh.Material = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.25f };
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = mesh,
            InstanceCount = MostGobs * DropsEach,
            VisibleInstanceCount = 0,
        };
        _blobs = new MultiMeshInstance3D { Multimesh = mm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_blobs);
    }

    /// <summary>Draws what's in the air now (positions in the sim's frame: this node sits at its origin).</summary>
    public void Sync(WildSim sim)
    {
        if (_blobs == null) return;
        MultiMesh mm = _blobs.Multimesh;
        int n = 0;
        float t = Time.GetTicksMsec() / 1000f;
        foreach (Gob g in sim.Gobs)
        {
            if (n / DropsEach >= MostGobs) break;
            // A Husk's maggots: pale grubs strung out loose, each one writhing as it flies.
            bool maggots = g.From?.Species.Spit == SpitKind.Maggots;
            for (int i = 0; i < DropsEach; i++)
            {
                float back = i * (maggots ? 0.06f : 0.045f);
                float scale = 1f - i * 0.17f;
                Vector3 at = g.Position - g.Velocity * back + Vector3.Up * 4.5f * back * back;
                Basis b = Basis.Identity.Scaled(Vector3.One * scale);
                if (maggots)
                {
                    float w = t * (9f + i * 1.7f) + i * 2.1f;
                    at += new Vector3(MathF.Sin(w * 0.7f + i), MathF.Cos(w * 0.5f), MathF.Sin(w * 0.6f + i * 3f)) * 0.06f * i;
                    b = new Basis(new Vector3(MathF.Sin(i * 1.3f), 1f, MathF.Cos(i * 2.1f)).Normalized(), w) * Basis.FromScale(new Vector3(0.38f, 0.38f, 1.25f) * scale * 0.8f);
                }
                mm.SetInstanceTransform(n, new Transform3D(b, at));
                mm.SetInstanceColor(n, g.Colour);
                n++;
            }
        }
        if (mm.VisibleInstanceCount != n) mm.VisibleInstanceCount = n;
    }
}
