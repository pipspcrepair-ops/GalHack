using System;
using Godot;

namespace Wildlife;

/// <summary>
/// What covers a body, put on the rig's pieces after they're built (like <see cref="Tack"/> and
/// <see cref="Mutation"/>), so it works on any rig and never touches how it moves: shag and fleece
/// hang off the body, the neck and the top of the legs; scales ride the back; thin skin and chitin
/// are the surface itself. The ears go on here too: big ears are what good hearing looks like, and
/// how a hot world's bodies lose heat.
/// </summary>
public static class Coats
{
    public static RigLayout Decorate(RigLayout layout, Species s)
    {
        if (s.Rig is RigKind.Swarm or RigKind.Humanoid || layout.Pieces.Count == 0) return layout;
        var rng = Rng.For(s.Seed, "coat");
        var k = new Paint(s);
        Piece body = layout.Pieces.Find(p => p.Name == "body");
        Piece head = layout.Pieces.Find(p => p.Name == "head");
        float w = MathF.Max(0.06f, MathF.Min(s.Body.Width, s.Body.Height));
        switch (s.Coat)
        {
            case Coat.Fur:
                if (body != null) Shag(body, k, rng, w * 0.34f, (int)Math.Clamp(46f * s.Body.Size, 30f, 80f));
                foreach (Piece p in layout.Pieces)
                {
                    if (p.Name.StartsWith("neck")) Shag(p, k, rng, w * 0.24f, 8);
                    else if (p.Name.Length > 3 && p.Name.StartsWith("leg") && p.Name.EndsWith("u")) Shag(p, k, rng, w * 0.22f, 7);
                }
                break;
            case Coat.Wool:
                if (body != null) Fleece(body, k, rng, w * 0.19f, (int)Math.Clamp(80f * s.Body.Size, 50f, 130f));
                foreach (Piece p in layout.Pieces)
                {
                    if (p.Name.StartsWith("neck")) Fleece(p, k, rng, w * 0.13f, 10);
                    else if (p.Name.Length > 3 && p.Name.StartsWith("leg") && p.Name.EndsWith("u")) Fleece(p, k, rng, w * 0.11f, 9);
                }
                break;
            case Coat.Blubber:
                if (body != null) Rolls(body, s, k);
                break;
            case Coat.Scale:
                if (body != null) Scutes(body, k, rng, w, s);
                break;
            case Coat.ThinSkin:
                Surface(layout, Mat.Membrane);
                break;
            case Coat.Chitin:
            case Coat.Shell:
                Surface(layout, Mat.Carapace);
                break;
        }
        if (s.EarSize > 0.05f && head != null) Ears(head, s, k);
        return layout;
    }

    static void Surface(RigLayout layout, float material)
    {
        foreach (Piece p in layout.Pieces)
        {
            if (p.Blur) continue;
            for (int i = 0; i < p.Prims.Count; i++)
            {
                Prim m = p.Prims[i];
                if (m.Material != Mat.Skin) continue;
                m.Material = material;
                p.Prims[i] = m;
            }
        }
    }

    static Prim First(Piece p)
    {
        foreach (Prim m in p.Prims)
            if (m.Shape == Shape.Loft) return m;
        return p.Prims.Count > 0 ? p.Prims[0] : default;
    }

    /// <summary>Long hair: tufts lying back and hanging down, longest under the belly.</summary>
    static void Shag(Piece p, Paint k, Rng rng, float length, int count)
    {
        if (p.Prims.Count == 0) return;
        Prim m = First(p);
        for (int i = 0; i < count; i++)
        {
            var (at, n, _) = Mutation.SurfacePoint(m, rng);
            Vector3 lie = (n * 0.45f + Vector3.Down * 0.55f + Vector3.Back * rng.Range(0.1f, 0.6f)).Normalized();
            float len = length * rng.Range(0.7f, 1.3f);
            Color c = k.Base.Darkened(rng.Range(0f, 0.22f)).Lerp(k.Belly, rng.Range(0f, 0.25f));
            p.Add(Shape.Cone, new Vector3(len * 0.62f, len, 0f), at + lie * len * 0.3f, c, 0f, 0f, Geo.AlongY(lie), 4);
        }
    }

    /// <summary>A fleece: fat soft clumps lying on the skin, close enough to hide it.</summary>
    static void Fleece(Piece p, Paint k, Rng rng, float size, int count)
    {
        if (p.Prims.Count == 0) return;
        Prim m = First(p);
        for (int i = 0; i < count; i++)
        {
            var (at, n, _) = Mutation.SurfacePoint(m, rng);
            float r = size * rng.Range(0.75f, 1.25f);
            Color c = k.Base.Darkened(rng.Range(0f, 0.12f));
            p.Add(Shape.Sphere, new Vector3(r, r * 0.55f, r), at - n * r * 0.05f, c, 0.3f, 0f, Geo.AlongY(n), 5);
        }
    }

    /// <summary>Folds of fat at the shoulder and the flank.</summary>
    static void Rolls(Piece body, Species s, Paint k)
    {
        float L = s.Body.Size, W = s.Body.Width, H = s.Body.Height;
        foreach (float z in new[] { -0.2f, 0.06f, 0.26f })
            body.Add(Shape.Sphere, new Vector3(W * 0.98f, H * 0.82f, L * 0.16f), new Vector3(0f, -H * 0.12f, z * L), k.Base.Darkened(0.05f), 0.4f, 0f, null, 12);
    }

    /// <summary>Overlapping plates down the spine and a scatter over the flanks.</summary>
    static void Scutes(Piece body, Paint k, Rng rng, float w, Species s)
    {
        Prim m = First(body);
        float L = s.Body.Size;
        LoftProfile shape = m.Shape == Shape.Loft ? m.Loft : null;
        int ridge = (int)Math.Clamp(L * 14f, 8f, 18f);
        for (int i = 0; i < ridge; i++)
        {
            float z = Mathf.Lerp(-0.42f, 0.42f, i / (float)(ridge - 1)) * L;
            float top = shape != null ? Parts.Top(shape, z) : s.Body.Height * 0.5f;
            float r = w * 0.09f * rng.Range(0.8f, 1.2f);
            body.Add(Shape.Sphere, new Vector3(r * 2.2f, r * 1.2f, r * 3f), m.Local * new Vector3(0f, top, z), k.Accent.Lerp(k.Base, 0.5f), 0.2f, 0f, null, 6, Mat.Carapace);
        }
        for (int i = 0; i < 26; i++)
        {
            var (at, n, _) = Mutation.SurfacePoint(m, rng);
            float r = w * 0.1f * rng.Range(0.7f, 1.2f);
            body.Add(Shape.Sphere, new Vector3(r * 2f, r * 0.45f, r * 2.3f), at, k.Base.Darkened(rng.Range(0.04f, 0.16f)), 0.3f, 0f, Geo.AlongY(n), 5, Mat.Carapace);
        }
    }

    /// <summary>Two ears standing off the skull, as big as its hearing needs: thin enough to take the light.</summary>
    static void Ears(Piece head, Species s, Paint k)
    {
        float hs = s.Body.HeadSize, e = 0.25f + 0.85f * Math.Clamp(s.EarSize, 0f, 1f);
        for (int side = -1; side <= 1; side += 2)
        {
            var root = new Vector3(side * hs * 0.34f, hs * 0.26f, hs * 0.12f);
            var dir = new Vector3(side * 0.72f, 0.62f + 0.2f * e, 0.3f).Normalized();
            float len = hs * 0.9f * e;
            head.Add(Shape.Sphere, new Vector3(len * 0.62f, len, len * 0.14f), root + dir * len * 0.42f, k.Base.Lerp(new Color(0.55f, 0.32f, 0.3f), 0.35f), 0.3f, 0f, Geo.AlongY(dir, new Vector3(side, 0f, 0.4f)), 9, Mat.Membrane);
        }
    }
}
