using System;
using System.Collections.Generic;
using Godot;

namespace Weather;

/// <summary>
/// Grass and umbrella trees, drawn as two MultiMeshes so thousands of blades cost two draw
/// calls. Both sway in a vertex shader driven by the weather's global wind uniforms: no nodes
/// per blade, nothing to tick.
/// </summary>
public static class Foliage
{
    public const int GrassClumps = 22000;
    public const float GrassArea = 240f;

    /// <summary>Tree tops, for lightning to find.</summary>
    public static List<Vector3> TreeTops { get; } = new();

    public static Node3D Build(Shader shader, Texture2D cloudNoise, Vector3 toSun)
    {
        var root = new Node3D { Name = "Foliage" };
        var rng = new RandomNumberGenerator { Seed = 1979 };

        var grassMaterial = new ShaderMaterial { Shader = shader };
        grassMaterial.SetShaderParameter("cloud_noise", cloudNoise);
        grassMaterial.SetShaderParameter("to_sun", toSun);
        grassMaterial.SetShaderParameter("stiffness", 1.0f);
        grassMaterial.SetShaderParameter("flutter", 1.0f);
        grassMaterial.SetShaderParameter("height_scale", 0.8f);

        var grass = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = GrassClump(),
            InstanceCount = GrassClumps,
        };
        int placed = 0;
        for (int i = 0; i < GrassClumps * 3 && placed < GrassClumps; i++)
        {
            // Most of the grass is near the pad, where you start; the rest thins out across the plain.
            bool near = rng.Randf() < 0.6f;
            float half = near ? 55f : GrassArea / 2f;
            float x = rng.RandfRange(-half, half);
            float z = rng.RandfRange(-half, half) - (near ? 0f : 40f);
            if (Terrain.Normal(x, z).Y < 0.88f) continue;          // not on steep slopes
            if (InClearing(x, z)) continue;
            float scale = rng.RandfRange(0.7f, 1.3f);
            var basis = new Basis(Vector3.Up, rng.RandfRange(0f, MathF.Tau)).Scaled(new Vector3(scale, scale * rng.RandfRange(0.8f, 1.3f), scale));
            grass.SetInstanceTransform(placed, new Transform3D(basis, Terrain.OnGround(x, z, -0.05f)));
            Color ground = Terrain.GroundColor(x, z, Terrain.Height(x, z), 1f);
            grass.SetInstanceColor(placed, ground.Lightened(rng.RandfRange(0.05f, 0.25f)));
            placed++;
        }
        grass.VisibleInstanceCount = placed;
        root.AddChild(new MultiMeshInstance3D
        {
            Name = "Grass", Multimesh = grass, MaterialOverride = grassMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });

        var treeMaterial = (ShaderMaterial)grassMaterial.Duplicate();
        treeMaterial.SetShaderParameter("stiffness", 3.5f);
        treeMaterial.SetShaderParameter("flutter", 0.6f);
        treeMaterial.SetShaderParameter("height_scale", 1.4f);

        var spots = new List<(Vector3 at, float scale, float yaw, Color canopy)>();
        Color[] canopies = { new("1f7a74"), new("d9622b"), new("2f8f6f"), new("c2442b"), new("e0a030") };
        for (int i = 0; i < 2000 && spots.Count < 190; i++)
        {
            float x = rng.RandfRange(-370f, 370f), z = rng.RandfRange(-370f, 370f);
            // Trees grow in loose groves: keep a spot only where a slow noise says "grove".
            float grove = Mathf.Sin(x * 0.013f + 1.7f) * Mathf.Cos(z * 0.011f - 0.4f) + Mathf.Sin(x * 0.031f - z * 0.027f) * 0.4f;
            if (grove < 0.35f || InClearing(x, z) || Terrain.Normal(x, z).Y < 0.85f) continue;
            spots.Add((Terrain.OnGround(x, z, -0.2f), rng.RandfRange(0.8f, 1.5f), rng.RandfRange(0f, MathF.Tau),
                canopies[rng.RandiRange(0, canopies.Length - 1)]));
        }

        var trees = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = UmbrellaTree(),
            InstanceCount = spots.Count,
        };
        TreeTops.Clear();
        var colliders = new StaticBody3D { Name = "TreeTrunks", CollisionLayer = Layers.World, CollisionMask = 0 };
        for (int i = 0; i < spots.Count; i++)
        {
            var (at, scale, yaw, canopy) = spots[i];
            trees.SetInstanceTransform(i, new Transform3D(new Basis(Vector3.Up, yaw).Scaled(Vector3.One * scale), at));
            trees.SetInstanceColor(i, canopy);
            TreeTops.Add(at + Vector3.Up * TreeHeight * scale);
            colliders.AddChild(new CollisionShape3D
            {
                Shape = new CylinderShape3D { Radius = 0.35f * scale, Height = 6f * scale },
                Position = at + Vector3.Up * 3f * scale,
            });
        }
        root.AddChild(new MultiMeshInstance3D { Name = "Trees", Multimesh = trees, MaterialOverride = treeMaterial });
        root.AddChild(colliders);
        return root;
    }

    /// <summary>Keep the parking pad, the path to the spire and the spire's hilltop clear.</summary>
    static bool InClearing(float x, float z)
    {
        if (new Vector2(x - Terrain.PadCenter.X, z - Terrain.PadCenter.Y).Length() < 20f) return true;
        return new Vector2(x - Terrain.SpireSpot.X, z - Terrain.SpireSpot.Y).Length() < 14f;
    }

    public const float TreeHeight = 8.2f;

    /// <summary>Five thin tapered blades; UV.y is 0 at the root and 1 at the tip.</summary>
    static ArrayMesh GrassClump()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var rng = new RandomNumberGenerator { Seed = 7 };
        for (int b = 0; b < 5; b++)
        {
            float angle = b * 1.3f + rng.RandfRange(-0.3f, 0.3f);
            var side = new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * 0.035f;
            var lean = new Vector3(rng.RandfRange(-0.18f, 0.18f), 0f, rng.RandfRange(-0.18f, 0.18f));
            var offset = new Vector3(rng.RandfRange(-0.15f, 0.15f), 0f, rng.RandfRange(-0.15f, 0.15f));
            float height = rng.RandfRange(0.25f, 0.5f);
            // Normals point mostly up, so blades take the same light as the ground they grow from.
            var normal = (new Vector3(-MathF.Sin(angle), 0f, MathF.Cos(angle)) * 0.3f + Vector3.Up).Normalized();
            Vertex(st, offset - side, 0f, normal, 0.7f);
            Vertex(st, offset + side, 0f, normal, 0.7f);
            Vertex(st, offset + lean + Vector3.Up * height, 1f, normal, 1.12f);
        }
        return st.Commit();
    }

    static void Vertex(SurfaceTool st, Vector3 position, float tip, Vector3 normal, float shade)
    {
        st.SetColor(new Color(shade, shade, shade));
        st.SetUV(new Vector2(0.5f, tip));
        st.SetNormal(normal);
        st.AddVertex(position);
    }

    /// <summary>
    /// An alien acacia: a trunk that forks into three limbs, each holding up a flat, slightly
    /// domed pad of foliage, so the skyline reads at a glance. The trunk's vertex colour is
    /// dark; the canopy's is white, so the instance colour tints it.
    /// </summary>
    static ArrayMesh UmbrellaTree()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var bark = new Color(0.2f, 0.14f, 0.11f);
        var fork = new Vector3(0.2f, 3.2f, 0f);
        Limb(st, Vector3.Zero, fork, 0.42f, 0.28f, bark);

        var crowns = new[] { (new Vector3(-1.9f, 6.4f, 0.6f), 2.6f), (new Vector3(1.7f, 7.0f, -0.9f), 3.0f), (new Vector3(0.3f, 7.5f, 1.4f), 2.3f) };
        foreach (var (top, radius) in crowns)
        {
            Limb(st, fork, top - Vector3.Up * 0.3f, 0.24f, 0.1f, bark);
            Pad(st, top, radius);
        }
        return st.Commit();
    }

    /// <summary>A tapered seven-sided limb between two points.</summary>
    static void Limb(SurfaceTool st, Vector3 from, Vector3 to, float r0, float r1, Color color)
    {
        const int sides = 7;
        Vector3 axis = (to - from).Normalized();
        Vector3 u = axis.Cross(Vector3.Forward).Normalized();
        if (u.LengthSquared() < 0.01f) u = Vector3.Right;
        Vector3 v = axis.Cross(u);
        for (int i = 0; i < sides; i++)
        {
            float a0 = i * MathF.Tau / sides, a1 = (i + 1) * MathF.Tau / sides;
            Vector3 d0 = u * MathF.Cos(a0) + v * MathF.Sin(a0), d1 = u * MathF.Cos(a1) + v * MathF.Sin(a1);
            Vector3[] ring = { from + d0 * r0, from + d1 * r0, to + d1 * r1, from + d0 * r0, to + d1 * r1, to + d0 * r1 };
            Vector3[] normals = { d0, d1, d1, d0, d1, d0 };
            for (int k = 0; k < 6; k++)
            {
                st.SetColor(color);
                st.SetNormal(normals[k]);
                st.SetUV(new Vector2(0.5f, Mathf.Clamp(ring[k].Y / TreeHeight, 0f, 1f)));
                st.AddVertex(ring[k]);
            }
        }
    }

    /// <summary>A flat crown: domed on top, a darker shade underneath.</summary>
    static void Pad(SurfaceTool st, Vector3 center, float radius)
    {
        const int rim = 12;
        var top = new Color(1f, 1f, 1f);
        var under = new Color(0.5f, 0.5f, 0.55f);
        Vector3 crown = center + Vector3.Up * 0.75f, belly = center - Vector3.Up * 0.25f;
        for (int i = 0; i < rim; i++)
        {
            float a0 = i * MathF.Tau / rim, a1 = (i + 1) * MathF.Tau / rim;
            float w0 = radius * (1f + 0.12f * MathF.Sin(a0 * 3f)), w1 = radius * (1f + 0.12f * MathF.Sin(a1 * 3f));
            Vector3 e0 = center + new Vector3(MathF.Cos(a0) * w0, 0f, MathF.Sin(a0) * w0);
            Vector3 e1 = center + new Vector3(MathF.Cos(a1) * w1, 0f, MathF.Sin(a1) * w1);
            Vector3 n0 = ((e0 - center).Normalized() * 0.45f + Vector3.Up).Normalized();
            Vector3 n1 = ((e1 - center).Normalized() * 0.45f + Vector3.Up).Normalized();
            AddTri(st, (crown, Vector3.Up), (e1, n1), (e0, n0), top);
            AddTri(st, (belly, Vector3.Down), (e0, Vector3.Down), (e1, Vector3.Down), under);
        }
    }

    static void AddTri(SurfaceTool st, (Vector3 p, Vector3 n) a, (Vector3 p, Vector3 n) b, (Vector3 p, Vector3 n) c, Color color)
    {
        foreach (var (p, n) in new[] { a, b, c })
        {
            st.SetColor(color);
            st.SetNormal(n);
            st.SetUV(new Vector2(0.5f, Mathf.Clamp(p.Y / TreeHeight, 0f, 1f)));
            st.AddVertex(p);
        }
    }
}
