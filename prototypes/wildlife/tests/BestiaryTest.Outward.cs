using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Wildlife;

public partial class BestiaryTest
{
    /// <summary>
    /// Nothing is drawn inside out: every part kind (each shape, turned, stretched and mirrored) and the parts of many
    /// generated animals and machines, built as they are drawn, wind outward (clockwise from outside, the engine's front)
    /// with their normals pointing out. Measured on the built mesh, prim by prim.
    /// </summary>
    void NothingIsInsideOut()
    {
        // Every shape on its own, in many poses, including mirrored and squashed ones.
        var rng = new Random(Seed().GetHashCode());
        int shapeBad = 0, shapeTris = 0;
        var kinds = new Dictionary<string, int>();
        foreach (Shape shape in Enum.GetValues<Shape>())
        for (int k = 0; k < 24; k++)
        {
            Prim p = SamplePrim(shape, k, rng);
            (int bad, int tris, string why) = Outward(p);
            shapeBad += bad; shapeTris += tris;
            if (bad > 0) kinds[$"{shape}{(k >= 12 ? " mirrored/stretched" : "")} {why}"] = bad;
        }
        foreach (var kv in kinds.Take(12)) GD.Print($"    {kv.Value,5} inside-out faces: {kv.Key}");
        Check(shapeBad == 0, $"every shape, turned, stretched and mirrored, faces outward: {shapeBad} inside-out of {shapeTris} faces");

        var bodies = new List<(string Name, IReadOnlyList<Piece> Pieces)>();
        foreach (float t in new[] { -25f, 10f, 20f, 45f })
        {
            var life = Bestiary.Roll(_data, World(t, alien: t > 15f ? 1f : 0.25f));
            foreach (Species s in life.Fauna.Species) bodies.Add(($"{s.CommonName} ({s.Rig})", Rig.Create(s, null).Layout.Pieces));
        }
        foreach (Species s in Bestiary.Roll(_data, World(-60f, vacuum: true)).Fauna.Species) bodies.Add(($"{s.CommonName} ({s.Rig})", Rig.Create(s, null).Layout.Pieces));
        for (int era = 0; era < 3; era++)
        for (int n = 0; n < 6; n++)
        {
            Species r = Robots.Roll(World(20f), n, era);
            bodies.Add(($"robot {era}/{n} ({r.Rig})", Rig.Create(r, null).Layout.Pieces));
        }
        int bad2 = 0, tris2 = 0, species = 0;
        var worst = new Dictionary<string, int>();
        var byPiece = new Dictionary<string, int>();
        int shown = 0;
        foreach (var (name, pieces) in bodies)
        {
            species++;
            foreach (Piece piece in pieces)
            foreach (Prim prim in piece.Prims)
            {
                (int bad, int tris, string why) = Outward(prim);
                bad2 += bad; tris2 += tris;
                if (bad == 0) continue;
                worst[name] = worst.GetValueOrDefault(name) + bad;
                string key = $"{piece.Name} {prim.Shape} {why}";
                if (shown++ < 12) GD.Print($"      e.g. {name}: {piece.Name} {prim.Shape} size {prim.Size} sides {prim.Sides} bad {bad}");
                byPiece[key] = byPiece.GetValueOrDefault(key) + bad;
            }
        }
        foreach (var kv in byPiece.OrderByDescending(k => k.Value).Take(14)) GD.Print($"    {kv.Value,5} inside-out faces in {kv.Key}");
        foreach (var kv in worst.OrderByDescending(k => k.Value).Take(6)) GD.Print($"    {kv.Value,5} inside-out faces in a {kv.Key}");
        Check(bad2 == 0, $"{species} generated animals and machines face outward everywhere: {bad2} inside-out of {tris2} faces in {worst.Count} bodies");
    }

    static Prim SamplePrim(Shape shape, int k, Random r)
    {
        float F(float a, float b) => a + (float)r.NextDouble() * (b - a);
        var axis = new Vector3(F(-1, 1), F(-1, 1), F(-1, 1)).Normalized();
        Basis b = new Basis(axis, F(0, 6.28f));
        if (k >= 12) b = b.Scaled(new Vector3(k % 3 == 0 ? -1f : 1f, k % 3 == 1 ? -1f : 1f, k % 3 == 2 ? -1f : F(0.4f, 2.5f))).Scaled(new Vector3(F(0.5f, 2f), F(0.5f, 2f), F(0.5f, 2f)));
        var prim = new Prim
        {
            Shape = shape,
            Size = new Vector3(F(0.2f, 1f), F(0.3f, 1.5f), F(0.2f, 1f)),
            Local = new Transform3D(b, new Vector3(F(-1, 1), F(-1, 1), F(-1, 1))),
            Color = Colors.Gray, Color2 = Colors.White, Sides = k % 2 == 0 ? 0 : 6 + k % 5,
        };
        if (shape == Shape.Loft)
        {
            prim.Loft = LoftProfile.Smooth(-0.5f, 0.5f, new[] { (0f, 0.1f, 0.1f, 0f), (0.5f, F(0.15f, 0.4f), F(0.1f, 0.3f), F(0, 0.1f)), (1f, 0.08f, 0.08f, 0f) }, 8 + k % 6);
        }
        return prim;
    }

    /// <summary>Inside-out faces of one prim's built mesh, its faces, and what was wrong.</summary>
    static (int Bad, int Tris, string Why) Outward(Prim prim)
    {
        var one = new Piece("p");
        one.Prims.Add(prim);
        MeshBuilder.Prepared m = MeshBuilder.Plain(one);
        int tris = m.I.Length / 3;
        // The engine's front is clockwise from outside, so the outward side is cross(c - a, b - a).
        var face = new Vector3[tris];
        var centre = new Vector3[tris];
        for (int t = 0; t < tris; t++)
        {
            Vector3 a = m.V[m.I[t * 3]], b = m.V[m.I[t * 3 + 1]], c = m.V[m.I[t * 3 + 2]];
            face[t] = (c - a).Cross(b - a);
            centre[t] = (a + b + c) / 3f;
        }
        int badNormal = 0, badWinding = 0;
        for (int t = 0; t < tris; t++)
        {
            float area = face[t].Length();
            if (area < 1e-7f) continue;
            Vector3 n = face[t] / area;
            Vector3 sn = m.N[m.I[t * 3]] + m.N[m.I[t * 3 + 1]] + m.N[m.I[t * 3 + 2]];
            if (sn.Dot(n) < -1e-4f) badNormal++;
            // From just outside the face, a ray along its outward side leaves the closed shape: an even count of crossings.
            Vector3 o = centre[t] + n * 1e-3f;
            // Three rays, tilted a little along the face's own edges: a ray through a shared edge can miscount, so most must agree.
            Vector3 ea = (m.V[m.I[t * 3 + 1]] - m.V[m.I[t * 3]]).Normalized(), eb = (m.V[m.I[t * 3 + 2]] - m.V[m.I[t * 3]]).Normalized();
            int odd = 0;
            foreach (Vector3 d in new[] { n, (n + ea * 0.3f).Normalized(), (n + eb * 0.3f).Normalized() })
            {
                int hits = 0;
                for (int u = 0; u < tris; u++)
                {
                    if (u == t) continue;
                    if (Hit(o, d, m.V[m.I[u * 3]], m.V[m.I[u * 3 + 1]], m.V[m.I[u * 3 + 2]])) hits++;
                }
                if (hits % 2 == 1) odd++;
            }
            if (odd >= 2) badWinding++;
        }
        // A shape with an open side has no inside to measure by: only its normals count then.
        bool closed = badWinding < tris * 0.9f || badWinding == 0;
        int bad = Math.Max(badNormal, closed ? badWinding : 0);
        string why = badNormal > 0 && badWinding > 0 ? "winding and normals" : badNormal > 0 ? "normals" : "winding";
        return (bad, tris, why);
    }

    static bool Hit(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 e1 = b - a, e2 = c - a, p = d.Cross(e2);
        float det = e1.Dot(p);
        if (MathF.Abs(det) < 1e-10f) return false;
        float inv = 1f / det;
        Vector3 s = o - a;
        float u = s.Dot(p) * inv;
        if (u < 0f || u > 1f) return false;
        Vector3 q = s.Cross(e1);
        float v = d.Dot(q) * inv;
        if (v < 0f || u + v > 1f) return false;
        return e2.Dot(q) * inv > 1e-5f;
    }
}
