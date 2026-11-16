using System;
using System.Collections.Generic;
using Godot;

namespace PlanetSurface.Fx;

/// <summary>
/// The one way a bolt of lightning is drawn, whoever throws it (a storm's strike, the Sleeper tool's
/// blast, an arc gun): each channel of it is one camera-facing strip whose pieces share their edges
/// at every point of its path (no quads stacked at the joints, nothing across the channel), narrowing
/// to a point at both ends. Its shader (shaders/bolt.gdshader) draws a thin bright core and a glow
/// that falls smoothly to nothing at the strip's edge (<see cref="Profile"/>), additively. The strip
/// keeps a few pixels wide however far off it is, and faces the eye at any angle.
/// </summary>
public static class Bolts
{
    /// <summary>One channel of a bolt: its jagged path, its width (metres, the glow's whole width) and how bright it is.</summary>
    public sealed record Channel(List<Vector3> Points, float Width, float Alpha);

    /// <summary>The hot core's half-width as a share of the strip's: the rest is glow.</summary>
    public const float CoreShare = 0.16f;

    static Shader _shader;

    /// <summary>A bolt material: a tint, how bright, and how strong the glow round the core is.</summary>
    public static ShaderMaterial Material(Color tint, float energy, float glow = 0.35f)
    {
        var m = new ShaderMaterial { Shader = _shader ??= GD.Load<Shader>("res://shaders/bolt.gdshader") };
        m.SetShaderParameter(Names.Of("tint"), tint);
        m.SetShaderParameter(Names.Of("energy"), energy);
        m.SetShaderParameter(Names.Of("glow"), glow);
        return m;
    }

    /// <summary>
    /// How bright the shader draws a strip at <paramref name="d"/> across it (0 the middle, 1 the edge), for
    /// a core of <paramref name="core"/> share and a glow of <paramref name="glow"/>: the same curve as the shader.
    /// </summary>
    public static float Profile(float d, float core = CoreShare, float glow = 0.35f)
    {
        float k = MathF.Exp(-(d * d) / MathF.Max(core * core, 1e-4f)), o = 1f - d;
        return k + glow * o * o * o * o;
    }

    /// <summary>A jagged path from a to b by midpoint displacement, <paramref name="rough"/> of the length at the first split (appended to <paramref name="path"/>, a first).</summary>
    public static void Jag(List<Vector3> path, Vector3 a, Vector3 b, float rough, int depth, Func<float> random)
    {
        path.Add(a);
        Split(path, a, b, a.DistanceTo(b) * rough, depth, random);
    }

    static void Split(List<Vector3> path, Vector3 a, Vector3 b, float disp, int depth, Func<float> random)
    {
        if (depth == 0)
        {
            path.Add(b);
            return;
        }
        Vector3 dir = b - a;
        dir = dir.LengthSquared() > 1e-10f ? dir.Normalized() : Vector3.Up;
        Vector3 u = dir.Cross(MathF.Abs(dir.Y) > 0.9f ? Vector3.Right : Vector3.Up).Normalized(), w = dir.Cross(u);
        float spin = random() * MathF.Tau;
        Vector3 mid = (a + b) * 0.5f + (u * MathF.Cos(spin) + w * MathF.Sin(spin)) * disp * (0.4f + 0.6f * random());
        Split(path, a, mid, disp * 0.5f, depth - 1, random);
        Split(path, mid, b, disp * 0.5f, depth - 1, random);
    }

    /// <summary>
    /// A channel's strip: at each point of its path a left and a right edge, shared by the pieces
    /// either side of it, so the strip runs unbroken; it narrows to a point at both ends, and stays at
    /// least a few pixels wide far off.
    /// </summary>
    public static (Vector3 left, Vector3 right)[] Strip(Channel c, Vector3 camera, float width)
    {
        List<Vector3> p = c.Points;
        var edges = new (Vector3, Vector3)[p.Count];
        Vector3 last = Vector3.Zero;
        for (int i = 0; i < p.Count; i++)
        {
            Vector3 along = p[Math.Min(i + 1, p.Count - 1)] - p[Math.Max(i - 1, 0)];
            Vector3 side = along.Cross(camera - p[i]);
            side = side.LengthSquared() > 1e-12f ? side.Normalized() : last;
            if (i > 0 && side.Dot(last) < 0f) side = -side; // never twisted over between two points
            last = side;
            float f = i / (float)(p.Count - 1);
            float taper = Mathf.Min(1f, Mathf.Min(f, 1f - f) * 12f) * Mathf.Lerp(1f, 0.6f, f);
            float scale = Mathf.Max(1f, camera.DistanceTo(p[i]) * 0.004f / Mathf.Max(c.Width, 0.01f)); // a few pixels wide far away
            Vector3 half = side * c.Width * width * scale * taper * 0.5f;
            edges[i] = (p[i] - half, p[i] + half);
        }
        return edges;
    }

    /// <summary>Draws the channels into a mesh as strips facing the camera, brightness (0..1) on each.</summary>
    public static void Draw(ImmediateMesh mesh, IReadOnlyList<Channel> channels, Vector3 camera, float width = 1f, float brightness = 1f, Color colour = default)
    {
        mesh.ClearSurfaces();
        if (channels.Count == 0) return;
        if (colour == default) colour = Colors.White;
        mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);
        foreach (Channel c in channels)
        {
            if (c.Points.Count < 2) continue;
            var e = Strip(c, camera, width);
            var col = new Color(colour.R, colour.G, colour.B, c.Alpha * brightness);
            void V(Vector3 p, float u)
            {
                mesh.SurfaceSetColor(col);
                mesh.SurfaceSetUV(new Vector2(u, CoreShare));
                mesh.SurfaceAddVertex(p);
            }
            for (int i = 0; i < e.Length - 1; i++)
            {
                V(e[i].left, 0); V(e[i].right, 1); V(e[i + 1].right, 1);
                V(e[i].left, 0); V(e[i + 1].right, 1); V(e[i + 1].left, 0);
            }
        }
        mesh.SurfaceEnd();
    }
}
