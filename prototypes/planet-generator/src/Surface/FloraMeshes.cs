using System;
using System.Collections.Generic;
using Godot;
using Planets.Gen;
using Planets.Look;

namespace Planets.Surface;

/// <summary>
/// Low-poly scatter meshes built in code: flat-shaded, a few dozen triangles each. Vertex colour
/// alpha marks the parts the per-instance tint applies to (canopies, caps, crystals), so trunks and
/// stalks keep their own colour whatever the biome tints the rest.
/// </summary>
static class FloraMeshes
{
    static readonly Color Bark = new(0.36f, 0.26f, 0.2f, 0f);
    static readonly Color Stalk = new(0.93f, 0.88f, 0.78f, 0f);
    static readonly Color Tinted = new(0.8f, 0.8f, 0.8f, 1f); // the shader multiplies by the tint, and 0.8 leaves it as it is

    public static Dictionary<Flora, Mesh> BuildAll()
    {
        var meshes = new Dictionary<Flora, Mesh>();
        foreach (Flora kind in Enum.GetValues(typeof(Flora))) meshes[kind] = Build(kind);
        return meshes;
    }

    /// <summary>Kinds big enough to matter at 300+ m; small ones only appear on the nearest chunks.</summary>
    public static bool IsLarge(Flora kind) => kind is Flora.Conifer or Flora.Broadleaf or Flora.JungleTree
        or Flora.Palm or Flora.GiantMushroom or Flora.Cactus or Flora.IceShard;

    /// <summary>What can grow up through a ruin: the kinds that stand as a tree does.</summary>
    public static bool IsTree(Flora kind) => kind is Flora.Conifer or Flora.Broadleaf or Flora.JungleTree or Flora.Palm or Flora.GiantMushroom or Flora.Cactus;

    /// <summary>
    /// The room a tree's mesh takes up at scale 1, as bands of height (m above its foot) and how far
    /// out from the trunk each reaches: what must be air for it to fit between a ruin's walls.
    /// </summary>
    public static (float From, float To, float Radius)[] Reach(Flora kind) => kind switch
    {
        Flora.Conifer => new[] { (0f, 1.8f, 0.4f), (1.8f, 3.5f, 2.3f), (3.5f, 5.5f, 1.75f), (5.5f, 9.1f, 1.15f) },
        Flora.Broadleaf => new[] { (0f, 3f, 0.45f), (3f, 8.6f, 3.3f) },
        Flora.JungleTree => new[] { (0f, 8.7f, 0.5f), (8.7f, 14.4f, 5.6f) },
        Flora.Palm => new[] { (0f, 5.8f, 0.9f), (5.8f, 8.4f, 4.4f) },
        Flora.GiantMushroom => new[] { (0f, 5.6f, 0.65f), (5.6f, 8f, 4.5f) },
        Flora.Cactus => new[] { (0f, 3.9f, 1f) },
        _ => new[] { (0f, 1f, 1f) },
    };

    /// <summary>Size multiplier per kind: alien crystals tower, everything else is roughly life-size.</summary>
    public static float BaseScale(Flora kind) => kind switch
    {
        Flora.Crystal => 1.9f,
        Flora.IceShard => 1.3f,
        Flora.Rock or Flora.LavaRock => 1.3f,
        _ => 1f,
    };

    /// <summary>Steepest ground (as the up component of its normal) each kind will grow on.</summary>
    public static float MinUp(Flora kind) => kind switch
    {
        Flora.Rock or Flora.LavaRock or Flora.Crystal or Flora.IceShard => 0.45f,
        Flora.Shrub or Flora.Mushroom => 0.75f,
        _ => 0.85f,
    };

    static Mesh Build(Flora kind)
    {
        var b = new Builder();
        switch (kind)
        {
            case Flora.Conifer:
                b.Frustum(Vector3.Zero, Vector3.Up, 0.32f, 0.22f, 2.4f, 5, Bark);
                b.Frustum(new Vector3(0, 1.8f, 0), Vector3.Up, 2.3f, 0f, 4.2f, 7, Tinted);
                b.Frustum(new Vector3(0, 3.9f, 0), Vector3.Up, 1.75f, 0f, 3.6f, 7, Tinted);
                b.Frustum(new Vector3(0, 5.9f, 0), Vector3.Up, 1.15f, 0f, 3.1f, 6, Tinted);
                break;
            case Flora.Broadleaf:
                b.Frustum(Vector3.Zero, Vector3.Up, 0.42f, 0.26f, 3.8f, 6, Bark);
                b.Blob(new Vector3(0, 5.6f, 0), new Vector3(3.1f, 2.5f, 3.1f), Tinted, 11);
                b.Blob(new Vector3(1.3f, 6.9f, 0.6f), new Vector3(1.9f, 1.6f, 1.9f), Tinted, 12);
                break;
            case Flora.JungleTree:
                b.Frustum(Vector3.Zero, Vector3.Up, 0.5f, 0.3f, 11.5f, 6, Bark);
                b.Blob(new Vector3(0, 12.2f, 0), new Vector3(5.2f, 2.1f, 5.2f), Tinted, 21);
                b.Blob(new Vector3(-2.2f, 10.8f, 1.6f), new Vector3(3f, 1.5f, 3f), Tinted, 22);
                break;
            case Flora.Palm:
                b.Frustum(Vector3.Zero, new Vector3(0.12f, 1f, 0f), 0.3f, 0.2f, 7.4f, 6, Bark);
                Vector3 top = new Vector3(0.12f, 1f, 0f).Normalized() * 7.4f;
                for (int i = 0; i < 7; i++)
                {
                    float a = i * Mathf.Tau / 7f + 0.3f;
                    var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    var side = new Vector3(-dir.Z, 0, dir.X) * 0.55f;
                    Vector3 mid = top + dir * 2.2f + Vector3.Up * 0.5f, tip = top + dir * 4.3f + Vector3.Down * 1.4f;
                    b.Quad(top + side * 0.3f, mid + side, tip, top - side * 0.3f, Tinted, doubleSided: true);
                    b.Quad(top - side * 0.3f, tip, mid - side, top + side * 0.3f, Tinted, doubleSided: true);
                }
                break;
            case Flora.Shrub:
                b.Blob(new Vector3(0, 0.45f, 0), new Vector3(1f, 0.75f, 1f), Tinted, 31);
                break;
            case Flora.Rock:
                b.Blob(new Vector3(0, 0.25f, 0), new Vector3(1.2f, 0.75f, 0.95f), Tinted, 41, 0.28f);
                break;
            case Flora.LavaRock:
                b.Blob(new Vector3(0, 0.4f, 0), new Vector3(1.4f, 1.1f, 1.2f), Tinted, 51, 0.4f);
                break;
            case Flora.Cactus:
                b.Frustum(Vector3.Zero, Vector3.Up, 0.38f, 0.3f, 3.6f, 7, Tinted);
                b.Frustum(new Vector3(0, 3.6f, 0), Vector3.Up, 0.3f, 0f, 0.3f, 7, Tinted);
                b.Frustum(new Vector3(0.25f, 1.3f, 0), new Vector3(1f, 0.25f, 0), 0.2f, 0.2f, 0.6f, 6, Tinted);
                b.Frustum(new Vector3(0.78f, 1.4f, 0), Vector3.Up, 0.22f, 0.18f, 1.5f, 6, Tinted);
                b.Frustum(new Vector3(-0.25f, 2f, 0), new Vector3(-1f, 0.25f, 0), 0.18f, 0.18f, 0.5f, 6, Tinted);
                b.Frustum(new Vector3(-0.7f, 2.1f, 0), Vector3.Up, 0.19f, 0.16f, 1.1f, 6, Tinted);
                break;
            case Flora.Crystal:
                Shard(b, Vector3.Zero, new Vector3(0.05f, 1f, 0.1f), 0.42f, 3.2f);
                Shard(b, new Vector3(0.5f, 0, 0.2f), new Vector3(0.6f, 1f, 0.15f), 0.28f, 1.9f);
                Shard(b, new Vector3(-0.35f, 0, -0.35f), new Vector3(-0.5f, 1f, -0.45f), 0.25f, 1.5f);
                break;
            case Flora.IceShard:
                Shard(b, Vector3.Zero, new Vector3(0.25f, 1f, -0.1f), 0.7f, 5.5f);
                Shard(b, new Vector3(0.8f, 0, 0.4f), new Vector3(0.7f, 1f, 0.3f), 0.4f, 2.6f);
                break;
            case Flora.Mushroom:
                b.Frustum(Vector3.Zero, Vector3.Up, 0.14f, 0.1f, 0.65f, 6, Stalk);
                b.Blob(new Vector3(0, 0.7f, 0), new Vector3(0.55f, 0.28f, 0.55f), Tinted, 61);
                break;
            case Flora.GiantMushroom:
                b.Frustum(Vector3.Zero, new Vector3(0.08f, 1f, 0f), 0.6f, 0.38f, 6.5f, 7, Stalk);
                b.Frustum(new Vector3(0.5f, 6.1f, 0), Vector3.Up, 3.9f, 1.2f, 1.7f, 9, Tinted);
                b.Frustum(new Vector3(0.5f, 6.1f, 0), Vector3.Down, 3.9f, 0.5f, 0.35f, 9, Stalk);
                break;
        }
        return b.Commit();
    }

    /// <summary>
    /// A resource deposit: a squat grey rock with a cluster of shards in the material's colour.
    /// The rock takes a hint of the tint so a half-buried shallow deposit still reads.
    /// </summary>
    public static Mesh Ore()
    {
        var b = new Builder();
        b.Blob(new Vector3(0, 0.3f, 0), new Vector3(1.3f, 0.8f, 1.1f), new Color(0.45f, 0.43f, 0.42f, 0.2f), 71, 0.3f);
        Shard(b, new Vector3(0.1f, 0.5f, 0f), new Vector3(0.1f, 1f, 0.05f), 0.3f, 1.8f);
        Shard(b, new Vector3(0.6f, 0.3f, 0.3f), new Vector3(0.7f, 1f, 0.3f), 0.22f, 1.2f);
        Shard(b, new Vector3(-0.5f, 0.3f, 0.2f), new Vector3(-0.6f, 1f, 0.2f), 0.2f, 1.1f);
        Shard(b, new Vector3(-0.1f, 0.3f, -0.6f), new Vector3(-0.1f, 1f, -0.7f), 0.18f, 0.9f);
        Shard(b, new Vector3(0.4f, 0.3f, -0.4f), new Vector3(0.5f, 1f, -0.5f), 0.16f, 0.8f);
        return b.Commit();
    }

    /// <summary>A hexagonal crystal with a pointed tip, leaning along an axis.</summary>
    static void Shard(Builder b, Vector3 at, Vector3 axis, float radius, float length)
    {
        axis = axis.Normalized();
        b.Frustum(at, axis, radius, radius * 0.92f, length * 0.75f, 6, Tinted);
        b.Frustum(at + axis * length * 0.75f, axis, radius * 0.92f, 0f, length * 0.25f, 6, Tinted);
    }

    /// <summary>
    /// Collects flat-shaded triangles. Triangles are given counter-clockwise as seen from outside
    /// (and checked against an outward hint), then emitted clockwise, which is Godot's front face.
    /// </summary>
    sealed class Builder
    {
        readonly SurfaceTool _st = new();

        public Builder()
        {
            _st.Begin(Mesh.PrimitiveType.Triangles);
            _st.SetSmoothGroup(uint.MaxValue); // flat shading: every face gets its own normal
        }

        public Mesh Commit()
        {
            _st.GenerateNormals();
            return _st.Commit();
        }

        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 outward, Color color)
        {
            if ((b - a).Cross(c - a).Dot(outward) < 0) (b, c) = (c, b);
            _st.SetColor(color);
            _st.AddVertex(a);
            _st.SetColor(color);
            _st.AddVertex(c);
            _st.SetColor(color);
            _st.AddVertex(b);
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color, bool doubleSided = false)
        {
            Vector3 n = (b - a).Cross(c - a);
            Tri(a, b, c, n, color);
            Tri(a, c, d, n, color);
            if (!doubleSided) return;
            Tri(a, c, b, -n, color);
            Tri(a, d, c, -n, color);
        }

        /// <summary>A cone or truncated cone around an axis; r1 = 0 makes a point.</summary>
        public void Frustum(Vector3 at, Vector3 axis, float r0, float r1, float height, int sides, Color color)
        {
            axis = axis.Normalized();
            Vector3 u = axis.Cross(Mathf.Abs(axis.Y) < 0.9f ? Vector3.Up : Vector3.Right).Normalized();
            Vector3 v = axis.Cross(u);
            Vector3 top = at + axis * height;
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.Tau / sides, a1 = (i + 1) * Mathf.Tau / sides;
                Vector3 d0 = u * Mathf.Cos(a0) + v * Mathf.Sin(a0), d1 = u * Mathf.Cos(a1) + v * Mathf.Sin(a1);
                Vector3 b0 = at + d0 * r0, b1 = at + d1 * r0;
                Vector3 out_ = (d0 + d1).Normalized();
                if (r1 <= 0f) Tri(b0, b1, top, out_ + axis * 0.2f, color);
                else
                {
                    Vector3 t0 = top + d0 * r1, t1 = top + d1 * r1;
                    Tri(b0, b1, t1, out_, color);
                    Tri(b0, t1, t0, out_, color);
                    Tri(top, t0, t1, axis, color);
                }
                Tri(at, b1, b0, -axis, color);
            }
        }

        /// <summary>A lumpy low-poly blob: an icosahedron, scaled and nudged per vertex.</summary>
        public void Blob(Vector3 centre, Vector3 radii, Color color, ulong seed, float jitter = 0.15f)
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            Vector3[] p =
            {
                new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0),
                new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t),
                new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
            };
            int[] f =
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            var rng = new Rng(seed);
            var verts = new Vector3[p.Length];
            for (int i = 0; i < p.Length; i++)
            {
                Vector3 d = p[i].Normalized() * (1f + rng.Range(-jitter, jitter));
                verts[i] = centre + new Vector3(d.X * radii.X, d.Y * radii.Y, d.Z * radii.Z);
            }
            for (int i = 0; i < f.Length; i += 3)
            {
                Vector3 a = verts[f[i]], b = verts[f[i + 1]], c = verts[f[i + 2]];
                Tri(a, b, c, (a + b + c) / 3f - centre, color);
            }
        }
    }
}
