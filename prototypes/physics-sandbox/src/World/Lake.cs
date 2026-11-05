using GalHack.Physics;
using Godot;
using PhysicsSandbox.Bridges;

namespace PhysicsSandbox.World;

/// <summary>
/// The lake's surface: a flat oval at the lake's level, the shape of its shore. To physics it is
/// only a probe-only surface on the water layer, for hover engines to ride on; everything else
/// goes into the water below it (the shared module's <see cref="IWaterField"/>). To the eye it is
/// a glassy teal sheet, seen from above and from below.
/// </summary>
public static class Lake
{
    const int Segments = 72;

    public static void Build(Node3D parent, bool visuals)
    {
        // A fan of triangles: the centre, then the shore all the way round (a hair outside it, so
        // the ground's edge hides the sheet's).
        var verts = new Vector3[Segments * 3];
        var centre = new Vector3(SandboxTerrain.LakeCenter.X, SandboxTerrain.LakeLevel, SandboxTerrain.LakeCenter.Y);
        for (int i = 0; i < Segments; i++)
        {
            verts[i * 3] = centre;
            verts[i * 3 + 1] = Rim(centre, (i + 1) * Mathf.Tau / Segments);
            verts[i * 3 + 2] = Rim(centre, i * Mathf.Tau / Segments);
        }

        var surface = new StaticBody3D { Name = "LakeSurface", CollisionLayer = Layers.Water, CollisionMask = 0 };
        surface.AddChild(new CollisionShape3D { Shape = new ConcavePolygonShape3D { Data = verts, BackfaceCollision = true } });
        parent.AddChild(surface);
        if (!visuals) return;

        var mesh = new ArrayMesh();
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        var normals = new Vector3[verts.Length];
        for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.Up;
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        parent.AddChild(new MeshInstance3D
        {
            Name = "LakeWater",
            Mesh = mesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(Palette.Water, 0.78f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled, // the underside is the sky seen from under water
                Roughness = 0.08f,
                Metallic = 0.1f,
                MetallicSpecular = 0.7f,
            },
        });
    }

    static Vector3 Rim(Vector3 centre, float angle) => centre + new Vector3(
        Mathf.Cos(angle) * SandboxTerrain.LakeRadiusX * 1.01f, 0f, Mathf.Sin(angle) * SandboxTerrain.LakeRadiusZ * 1.01f);
}
