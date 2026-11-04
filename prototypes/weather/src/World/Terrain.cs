using System;
using Godot;

namespace Weather;

/// <summary>
/// Rolling plains with a few hills: a pure height function, plus the mesh and collider built
/// from it. The function is plain maths (no engine noise object), so the weather model can ask
/// for ground heights cheaply every tick.
/// </summary>
public static class Terrain
{
    public const float Size = 800f;     // the shaped area; flat skirt beyond
    public const float Spacing = 4f;

    /// <summary>The lone spire stands on the first hill.</summary>
    public static readonly Vector2 SpireSpot = new(-60f, -170f);

    /// <summary>The flattened pad where the vehicles park and the player starts.</summary>
    public static readonly Vector2 PadCenter = new(0f, -6f);
    const float PadRadius = 22f;

    static readonly (Vector2 at, float height, float radius)[] Hills =
    {
        (SpireSpot, 24f, 52f),
        (new Vector2(150f, -70f), 15f, 65f),
        (new Vector2(-190f, 70f), 28f, 90f),
        (new Vector2(95f, 210f), 12f, 60f),
        (new Vector2(-150f, -280f), 18f, 70f),
        (new Vector2(260f, -230f), 22f, 80f),
    };

    public static float Height(float x, float z)
    {
        float h = 3.2f * Fbm(x * 0.0065f, z * 0.0065f, 3) + 0.5f * Fbm(x * 0.04f + 17f, z * 0.04f - 5f, 2);
        foreach (var (at, height, radius) in Hills)
        {
            float dx = x - at.X, dz = z - at.Y;
            h += height * MathF.Exp(-(dx * dx + dz * dz) / (radius * radius));
        }

        // Fade to sea level at the edges so the skirt meets it, and flatten the parking pad.
        float edge = MathF.Max(MathF.Abs(x), MathF.Abs(z));
        h *= 1f - SmoothStep(Size * 0.38f, Size * 0.495f, edge);
        float pad = new Vector2(x - PadCenter.X, z - PadCenter.Y).Length();
        return Mathf.Lerp(PadHeight, h, SmoothStep(PadRadius * 0.6f, PadRadius * 1.6f, pad));
    }

    static readonly float PadHeight = RawPadHeight();

    static float RawPadHeight() => 3.2f * Fbm(PadCenter.X * 0.0065f, PadCenter.Y * 0.0065f, 3);

    public static Vector3 Normal(float x, float z)
    {
        const float e = 0.5f;
        float dx = Height(x + e, z) - Height(x - e, z);
        float dz = Height(x, z + e) - Height(x, z - e);
        return new Vector3(-dx, 2f * e, -dz).Normalized();
    }

    public static Vector3 OnGround(float x, float z, float lift = 0f) => new(x, Height(x, z) + lift, z);

    // ---- Building ---------------------------------------------------------------------

    /// <summary>The terrain as a static body, with a mesh coloured per vertex and a matching trimesh collider.</summary>
    public static StaticBody3D Build(Material material)
    {
        int n = (int)(Size / Spacing) + 1;
        var vertices = new Vector3[n * n];
        var normals = new Vector3[n * n];
        var colors = new Color[n * n];
        float half = Size / 2f;

        for (int iz = 0; iz < n; iz++)
        for (int ix = 0; ix < n; ix++)
        {
            float x = -half + ix * Spacing, z = -half + iz * Spacing;
            int i = iz * n + ix;
            vertices[i] = new Vector3(x, Height(x, z), z);
            normals[i] = Normal(x, z);
            colors[i] = GroundColor(x, z, vertices[i].Y, normals[i].Y);
        }

        var indices = new int[(n - 1) * (n - 1) * 6];
        int k = 0;
        for (int iz = 0; iz < n - 1; iz++)
        for (int ix = 0; ix < n - 1; ix++)
        {
            int a = iz * n + ix, b = a + 1, c = a + n, d = c + 1;
            indices[k++] = a; indices[k++] = b; indices[k++] = c;
            indices[k++] = b; indices[k++] = d; indices[k++] = c;
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        var faces = new Vector3[indices.Length];
        for (int i = 0; i < indices.Length; i++) faces[i] = vertices[indices[i]];

        var body = new StaticBody3D { Name = "Terrain", CollisionLayer = Layers.World, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D { Shape = new ConcavePolygonShape3D { Data = faces } });
        body.AddChild(new MeshInstance3D { Name = "Mesh", Mesh = mesh, MaterialOverride = material });
        body.AddChild(BuildSkirt(material));
        return body;
    }

    /// <summary>A flat ring out to the horizon, so the fog has something to swallow.</summary>
    static MeshInstance3D BuildSkirt(Material material)
    {
        float inner = Size / 2f, outer = 9000f;
        Color near = GroundColor(inner, inner, 0f, 1f), far = new Color("8c7a45");
        var vertices = new Vector3[16];
        var colors = new Color[16];
        var normals = new Vector3[16];
        Vector2[] corners = { new(-1, -1), new(1, -1), new(1, 1), new(-1, 1) };
        for (int i = 0; i < 4; i++)
        {
            vertices[i] = new Vector3(corners[i].X * inner, 0f, corners[i].Y * inner);
            vertices[i + 4] = new Vector3(corners[i].X * outer, -0.5f, corners[i].Y * outer);
            colors[i] = near;
            colors[i + 4] = far;
        }
        Array.Fill(normals, Vector3.Up);
        var indices = new System.Collections.Generic.List<int>();
        for (int i = 0; i < 4; i++)
        {
            int a = i, b = (i + 1) % 4, c = i + 4, d = (i + 1) % 4 + 4;
            indices.AddRange(new[] { a, c, b, b, c, d });
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices[..8];
        arrays[(int)Mesh.ArrayType.Normal] = normals[..8];
        arrays[(int)Mesh.ArrayType.Color] = colors[..8];
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return new MeshInstance3D { Name = "Skirt", Mesh = mesh, MaterialOverride = material };
    }

    /// <summary>
    /// Golden grassland with greener hollows, ochre hilltops and rust-red patches: an alien
    /// prairie in a 1970s paperback palette.
    /// </summary>
    public static Color GroundColor(float x, float z, float height, float up)
    {
        var grass = new Color("b49a3c");
        var lush = new Color("6f8a34");
        var ochre = new Color("c9893a");
        var rust = new Color("a8452a");
        var rock = new Color("6e5a4e");

        float wet = Math.Clamp(-height * 0.25f + 0.5f + Fbm(x * 0.012f + 40f, z * 0.012f, 2) * 0.6f, 0f, 1f);
        Color c = grass.Lerp(lush, wet * 0.8f);
        c = c.Lerp(ochre, Math.Clamp((height - 9f) / 14f, 0f, 1f));
        float patch = SmoothStep(0.35f, 0.6f, Fbm(x * 0.009f - 13f, z * 0.009f + 7f, 2));
        c = c.Lerp(rust, patch * 0.55f);
        c = c.Lerp(rock, SmoothStep(0.86f, 0.7f, up));
        return c;
    }

    // ---- Noise ------------------------------------------------------------------------

    static float Fbm(float x, float z, int octaves)
    {
        float sum = 0f, amp = 1f, norm = 0f;
        for (int o = 0; o < octaves; o++)
        {
            sum += amp * Value2D(x, z);
            norm += amp;
            amp *= 0.5f;
            x = x * 2.03f + 11.7f;
            z = z * 2.03f - 7.3f;
        }
        return sum / norm;
    }

    /// <summary>2D value noise in [-1, 1].</summary>
    static float Value2D(float x, float z)
    {
        float fx = MathF.Floor(x), fz = MathF.Floor(z);
        int ix = (int)fx, iz = (int)fz;
        float tx = x - fx, tz = z - fz;
        tx = tx * tx * (3f - 2f * tx);
        tz = tz * tz * (3f - 2f * tz);
        float a = Hash(ix, iz), b = Hash(ix + 1, iz), c = Hash(ix, iz + 1), d = Hash(ix + 1, iz + 1);
        return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
    }

    static float Hash(int x, int z)
    {
        uint h = (uint)x * 0x8DA6B343u ^ (uint)z * 0xD8163841u ^ 0x5bd1e995u;
        h ^= h >> 13;
        h *= 0x5bd1e995u;
        h ^= h >> 15;
        return (h & 0xFFFF) / 32767.5f - 1f;
    }

    public static float SmoothStep(float from, float to, float x)
    {
        float t = Math.Clamp((x - from) / (to - from), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}

/// <summary>Physics layers.</summary>
public static class Layers
{
    public const uint World = 1u << 0;
    public const uint Vehicles = 1u << 1;
    public const uint Player = 1u << 2;
}
