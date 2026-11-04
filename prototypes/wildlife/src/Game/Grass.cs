using System;
using Godot;

namespace Wildlife;

/// <summary>
/// Grass as instanced tufts in square chunks. Each chunk is one draw call with a visibility
/// range, so only the meadow around you is drawn: the count is a single tunable for low-end
/// machines.
/// </summary>
public partial class Grass : Node3D
{
    public int TuftsPerChunk = 520;
    public float ChunkSize = 40f, Extent = 160f, DrawDistance = 95f;

    ShaderMaterial _material;

    public void Build(Terrain terrain, NavGrid nav)
    {
        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/grass.gdshader") };
        Mesh tuft = TuftMesh();
        var rng = Rng.For(Planet.LandSeed, "grass");
        int chunks = (int)(Extent * 2f / ChunkSize);
        for (int cz = 0; cz < chunks; cz++)
        for (int cx = 0; cx < chunks; cx++)
        {
            var origin = new Vector3(-Extent + (cx + 0.5f) * ChunkSize, 0f, -Extent + (cz + 0.5f) * ChunkSize);
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = tuft, InstanceCount = TuftsPerChunk };
            int used = 0;
            for (int i = 0; i < TuftsPerChunk; i++)
            {
                var p = origin + new Vector3(rng.Range(-0.5f, 0.5f) * ChunkSize, 0f, rng.Range(-0.5f, 0.5f) * ChunkSize);
                if (nav.Blocked(p) && rng.Chance(0.8f)) continue; // thin out over rocks and trunks
                float y = terrain.Height(p.X, p.Z);
                float s = rng.Range(0.7f, 1.35f);
                var basis = new Basis(Vector3.Up, rng.Range(0f, MathF.Tau)).Scaled(new Vector3(s, s * rng.Range(0.8f, 1.3f), s));
                mm.SetInstanceTransform(used++, new Transform3D(basis, new Vector3(p.X - origin.X, y, p.Z - origin.Z)));
            }
            mm.VisibleInstanceCount = used;
            AddChild(new MultiMeshInstance3D
            {
                Multimesh = mm,
                MaterialOverride = _material,
                Position = new Vector3(origin.X, 0f, origin.Z),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                VisibilityRangeEnd = DrawDistance,
                VisibilityRangeEndMargin = 15f,
                VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Disabled,
            });
        }
    }

    public void ApplyStyle(WorldStyle style)
    {
        _material.SetShaderParameter("base_color", style.GrassBase);
        _material.SetShaderParameter("tip_color", style.GrassTip);
        _material.SetShaderParameter("glow_color", style.GrassGlow);
        _material.SetShaderParameter("glow", style.GrassGlowStrength);
    }

    /// <summary>Three blades, each a single tapered triangle; UV.y runs 0 at the root to 1 at the tip.</summary>
    static Mesh TuftMesh()
    {
        var verts = new Vector3[9];
        var uvs = new Vector2[9];
        for (int b = 0; b < 3; b++)
        {
            float a = b * MathF.Tau / 3f + 0.3f;
            var dir = new Vector3(MathF.Cos(a), 0f, MathF.Sin(a));
            var side = new Vector3(-dir.Z, 0f, dir.X) * 0.045f;
            float h = 0.45f + 0.15f * b;
            verts[b * 3] = dir * 0.06f - side;
            verts[b * 3 + 1] = dir * 0.06f + side;
            verts[b * 3 + 2] = dir * 0.22f + Vector3.Up * h;
            uvs[b * 3] = new Vector2(0f, 0f);
            uvs[b * 3 + 1] = new Vector2(1f, 0f);
            uvs[b * 3 + 2] = new Vector2(0.5f, 1f);
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}
