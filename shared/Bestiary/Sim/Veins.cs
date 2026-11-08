using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>One worm-flesh vein grown over a den's surfaces: its line, the surface's normal along it, how thick it is at its root.</summary>
public sealed class Vein
{
    public readonly List<Vector3> Points = new();
    public readonly List<Vector3> Normals = new();
    /// <summary>Across at the root, m: it thins to nothing at the tip.</summary>
    public float Width;
    public Color Color;
}

/// <summary>
/// The veins of worm flesh round an infestation: out from every mass across the floor, up the
/// walls and over onto the ceiling, thick and many near the mass and thinning to nothing further
/// out, and more, longer and thicker the worse the den is. A light den's faint one starts in a
/// corner with no mass at all. Each vein walks the place's own surfaces (<see cref="SpaceMath.Project"/>),
/// so it bends round every corner it meets; <see cref="VeinView"/> draws a whole place's in one mesh.
/// </summary>
public static class Veins
{
    /// <summary>How far a vein goes between the points it is drawn through, m.</summary>
    const float Step = 0.16f;
    /// <summary>The most a den grows (a severe den's are many; a mesh of them is still one draw).</summary>
    const int Most = 420;
    /// <summary>The thickest a vein is at its root, m, however big its mass: a vein, not a hose.</summary>
    public const float Widest = 0.055f;

    public static void Grow(List<Vein> into, ISolidSpace space, List<WormMass> masses, List<(Vector3 At, Vector3 Normal, float Spread)> roots, float severity, ulong seed)
    {
        var rng = Rng.For(seed, "veins");
        float sev = Math.Clamp(severity, 0f, 1f);
        foreach (WormMass m in masses)
        {
            int n = (int)MathF.Round(Mathf.Lerp(2f, 10f, sev) * (0.5f + 0.6f * m.Size));
            for (int i = 0; i < n && into.Count < Most; i++)
            {
                Vector3 dir = Tangent(new Vector3(rng.Range(-1f, 1f), rng.Range(-1f, 1f), rng.Range(-1f, 1f)), m.Normal);
                if (dir == Vector3.Zero) continue;
                float length = rng.Range(0.5f, 1f) * Mathf.Lerp(0.8f, 3.8f, sev) * (0.6f + 0.5f * m.Size);
                float width = MathF.Min(Widest, m.Size * Mathf.Lerp(0.04f, 0.07f, sev) * rng.Range(0.7f, 1.2f));
                Walk(into, space, m.Position + dir * m.Size * 0.35f + m.Normal * 0.05f, dir, length, width, Flesh(rng), rng, sev > 0.4f ? 2 : 1, sev);
            }
        }
        foreach (var (at, normal, spread) in roots)
        {
            int n = 1 + (spread > 0.35f ? 1 : 0);
            for (int i = 0; i < n && into.Count < Most; i++)
            {
                Vector3 dir = Tangent(new Vector3(rng.Range(-1f, 1f), 0f, rng.Range(-1f, 1f)), normal);
                if (dir == Vector3.Zero) continue;
                Walk(into, space, at + normal * 0.05f, dir, Mathf.Lerp(0.5f, 1.6f, spread) * rng.Range(0.7f, 1f), Mathf.Lerp(0.012f, 0.03f, spread), Flesh(rng), rng, 1, spread * 0.5f);
            }
        }
    }

    /// <summary>Pale flesh to the dark red of what lives inside.</summary>
    static Color Flesh(Rng rng) => new Color(0.62f, 0.36f, 0.36f).Lerp(new Color(0.34f, 0.1f, 0.13f), rng.Range(0f, 1f));

    /// <summary>A direction laid along a surface; zero if it points (nearly) straight into or out of it.</summary>
    static Vector3 Tangent(Vector3 d, Vector3 n)
    {
        Vector3 t = d - n * d.Dot(n);
        return t.LengthSquared() > 0.04f ? t.Normalized() : Vector3.Zero;
    }

    /// <summary>
    /// One vein: it wanders a little at each step, held on the surface; walking into a wall it
    /// turns up it (the way up becomes the way on), and over a ledge it rolls round onto the new
    /// face. Now and then a thinner branch splits off.
    /// </summary>
    static void Walk(List<Vein> into, ISolidSpace space, Vector3 from, Vector3 dir, float length, float width, Color color, Rng rng, int branches, float sev)
    {
        Vector3 p = from;
        if (!SpaceMath.Project(space, ref p, 0.008f, out Vector3 n)) return;
        dir = Tangent(dir, n);
        if (dir == Vector3.Zero) return;
        var v = new Vein { Width = width, Color = color };
        for (float went = 0f; went <= length; went += Step)
        {
            v.Points.Add(p);
            v.Normals.Add(n);
            dir = (dir + n.Cross(dir) * rng.Range(-0.4f, 0.4f)).Normalized();
            Vector3 q = p + dir * Step;
            if (!SpaceMath.Project(space, ref q, 0.008f, out Vector3 nq) || q.DistanceTo(p) > Step * 2.5f) break;
            Vector3 on = Tangent(dir, nq);
            if (on == Vector3.Zero) on = Tangent(n, nq);
            if (on == Vector3.Zero) break;
            p = q;
            n = nq;
            dir = on;
            if (branches > 0 && into.Count < Most && rng.Chance(0.05f + 0.07f * sev))
            {
                Vector3 side = n.Cross(dir) * (rng.Chance(0.5f) ? 1f : -1f);
                float left = 1f - went / length;
                Walk(into, space, p, (dir + side * rng.Range(0.6f, 1.4f)).Normalized(), (length - went) * rng.Range(0.4f, 0.75f), width * left * 0.6f, color.Lightened(0.06f), rng, branches - 1, sev);
            }
        }
        if (v.Points.Count >= 3) into.Add(v);
    }

    /// <summary>
    /// Every vein as one mesh: a low ridge lying on its surface (an edge either side, a raised
    /// spine down the middle), tapering from its root to nothing.
    /// </summary>
    public static ArrayMesh Build(IReadOnlyList<Vein> veins)
    {
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var cols = new List<Color>();
        var index = new List<int>();
        foreach (Vein v in veins)
        {
            int count = v.Points.Count, start = verts.Count;
            for (int i = 0; i < count; i++)
            {
                Vector3 p = v.Points[i], n = v.Normals[i];
                Vector3 along = (v.Points[Math.Min(count - 1, i + 1)] - v.Points[Math.Max(0, i - 1)]);
                Vector3 side = n.Cross(along);
                side = side.LengthSquared() > 1e-8f ? side.Normalized() : n.Cross(Vector3.Right).Normalized();
                float t = i / (count - 1f), w = v.Width * MathF.Pow(1f - t, 0.7f) + 0.002f;
                Vector3 lift = n * 0.006f;
                verts.Add(p + lift + side * w * 0.5f);
                verts.Add(p + lift + n * w * 0.38f);
                verts.Add(p + lift - side * w * 0.5f);
                norms.Add((n * 0.6f + side).Normalized());
                norms.Add(n);
                norms.Add((n * 0.6f - side).Normalized());
                cols.Add(v.Color.Darkened(0.25f));
                cols.Add(v.Color.Lightened(0.12f));
                cols.Add(v.Color.Darkened(0.25f));
                if (i == 0) continue;
                int a = start + (i - 1) * 3, b = start + i * 3;
                index.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1, a + 1, b + 1, a + 2, a + 2, b + 1, b + 2 });
            }
        }
        var mesh = new ArrayMesh();
        if (index.Count == 0) return mesh;
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = norms.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = cols.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = index.ToArray();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}
