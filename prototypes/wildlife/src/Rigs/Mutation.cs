using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>
/// What a mutagenic world (fallout, an escaped biolab agent, the Hollow Bloom's touch) does to a
/// species' body, at a strength from 0 to 1. It decorates the rig's pieces after they are built,
/// so it works on every rig and never touches what makes the animation work:
/// - growths: a few tumorous lumps on the body, the head or a limb, sitting on the skin;
/// - asymmetry: one side's legs thicker than the other's (thickness only: legs keep their
///   length, so feet still plant), one side of the head bigger (a heavier horn, a wider eye);
/// - an occasional extra part: a cluster of extra eyes, a withered spare limb on the flank, or
///   tendrils that split at the tip;
/// - a patch of skin in a colour off the species' palette.
/// Same seed and strength, same mutation; strength 0 changes nothing at all. Everything stays
/// within limits (a few lumps, a third thicker at most, lumps a fraction of the body's girth),
/// so the animal still reads as a real, dangerous animal, not a broken model.
/// </summary>
public static class Mutation
{
    /// <summary>Most lumps a fully mutated body carries.</summary>
    public const int MaxGrowths = 5;
    /// <summary>The heavier side is at most this much thicker.</summary>
    public const float MaxAsymmetry = 1.32f;

    /// <summary>What was done, for the tests and the codex.</summary>
    public sealed class Report
    {
        public int Growths;
        public float Asymmetry = 1f;
        public string Extra = "";
        public bool Patch;
        public float AddedVolume, BodyVolume;
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Species, Report> Reports = new();

    public static Report Of(Species s) => Reports.TryGetValue(s, out Report r) ? r : new Report();

    public static RigLayout Decorate(RigLayout layout, Species s)
    {
        // Swarm members are specks: nothing to see on them.
        if (s.Mutation <= 0f || layout.Pieces.Count == 0 || s.Rig == RigKind.Swarm) return layout;
        float k = Math.Clamp(s.Mutation, 0f, 1f);
        var rng = Rng.For(s.Seed, "mutation", (long)MathF.Round(k * 1000f));
        var paint = new Paint(s);
        var report = new Report();
        Piece body = Find(layout, "body") ?? Largest(layout);
        Piece head = Find(layout, "head");
        report.BodyVolume = Volume(body);

        // Growths: on the body mostly, now and then the head or a limb.
        int growths = Math.Min(MaxGrowths, (int)MathF.Round(k * MaxGrowths * rng.Range(0.7f, 1.1f)));
        if (k > 0.05f) growths = Math.Max(1, growths);
        var limbs = layout.Pieces.FindAll(p => p.Name.StartsWith("leg") || p.Name.StartsWith("up") || p.Name.StartsWith("hind") || p.Name.StartsWith("seg"));
        for (int i = 0; i < growths; i++)
        {
            // Never so much growth that the body underneath stops reading: a fifth of its bulk at most.
            if (report.BodyVolume > 0f && report.AddedVolume > report.BodyVolume * 0.11f) break;
            float roll = rng.Float();
            Piece on = roll < 0.6f || (head == null && limbs.Count == 0) ? body : roll < 0.8f && head != null ? head : limbs.Count > 0 ? limbs[rng.Int(0, limbs.Count - 1)] : body;
            // Sized by its width, or by the body piece's bulk where the animal's width isn't that piece's (a gas bag's).
            float across = MathF.Min(s.Body.Width, report.BodyVolume > 0f ? MathF.Cbrt(report.BodyVolume) * 0.75f : s.Body.Width);
            report.AddedVolume += Growth(on, paint, rng, k, across * (on == body ? 1f : 0.6f));
            report.Growths++;
        }

        // Asymmetry: one side heavier.
        float side = rng.Chance(0.5f) ? -1f : 1f;
        float a = 1f + (MaxAsymmetry - 1f) * k * rng.Range(0.6f, 1f);
        report.Asymmetry = a;
        foreach (Piece p in layout.Pieces)
        {
            if (LegSide(p.Name) is int legSide && legSide != 0)
                Thicken(p, legSide == (int)side ? a : 1f / MathF.Sqrt(a));
        }
        if (head != null) Lopside(head, side, a);

        // An extra part, now and then (always, at full strength).
        if (k >= 0.3f && rng.Chance(0.35f + 0.65f * k))
        {
            var tendrils = layout.Pieces.FindAll(p => p.Name.StartsWith("tendril") || p.Name.StartsWith("whisker") || p.Name.StartsWith("feel"));
            int pick = rng.Int(0, 2);
            if (pick == 2 && tendrils.Count > 0)
            {
                foreach (Piece t in tendrils) if (LastInChain(layout, t)) Split(t, paint, rng);
                report.Extra = "split tendrils";
            }
            else if (pick == 1 && body != null)
            {
                report.AddedVolume += SpareLimb(body, paint, rng, -side, s);
                report.Extra = "a spare limb";
            }
            else if (head != null && !s.Blind)
            {
                EyeCluster(head, paint, rng, -side, s.Body.HeadSize);
                report.Extra = "an extra eye cluster";
            }
            else if (body != null)
            {
                report.AddedVolume += SpareLimb(body, paint, rng, -side, s);
                report.Extra = "a spare limb";
            }
        }

        // A patch of skin in a colour that isn't the species' own.
        if (k >= 0.15f && rng.Chance(0.4f + 0.6f * k) && body != null)
        {
            Patch(body, s, rng);
            report.Patch = true;
        }
        Reports.AddOrUpdate(s, report);
        return layout;
    }

    static Piece Find(RigLayout l, string name) => l.Pieces.Find(p => p.Name == name);

    static Piece Largest(RigLayout l)
    {
        Piece best = null;
        float v = -1f;
        foreach (Piece p in l.Pieces)
        {
            float pv = Volume(p);
            if (pv > v) { v = pv; best = p; }
        }
        return best;
    }

    static float Volume(Piece p)
    {
        if (p == null) return 0f;
        float v = 0f;
        foreach (Prim m in p.Prims)
        {
            if (m.Shape == Shape.Loft && m.Loft != null)
            {
                for (int i = 0; i + 1 < m.Loft.Count; i++) v += MathF.PI * m.Loft.W[i] * m.Loft.H[i] * MathF.Abs(m.Loft.Z[i + 1] - m.Loft.Z[i]);
            }
            else v += m.Size.X * MathF.Max(0.01f, m.Size.Y) * MathF.Max(0.01f, m.Size.Z) * 0.52f;
        }
        return v;
    }

    /// <summary>Which side a leg piece is on (legged walkers alternate left and right), if it is one.</summary>
    static int? LegSide(string name)
    {
        if (name.Length < 4 || !name.StartsWith("leg")) return null;
        int i = 3, n = 0;
        bool digits = false;
        while (i < name.Length && char.IsDigit(name[i])) { n = n * 10 + (name[i] - '0'); i++; digits = true; }
        if (!digits) return null;
        if (i < name.Length && name[i] == '_') return name.EndsWith("_-1") ? -1 : 1; // chain rigs name the side
        return n % 2 == 0 ? -1 : 1;
    }

    /// <summary>A point on a prim's surface and the outward normal there, in piece space; upper side favoured.</summary>
    static (Vector3 at, Vector3 n, float girth) SurfacePoint(Prim m, Rng rng)
    {
        float theta = rng.Range(0.15f, 0.85f) * MathF.PI * (rng.Chance(0.8f) ? 1f : -1f);
        switch (m.Shape)
        {
            case Shape.Loft when m.Loft != null && m.Loft.Count > 2:
            {
                int i = rng.Int(m.Loft.Count / 5, m.Loft.Count * 4 / 5);
                float w = m.Loft.W[i], h = m.Loft.H[i];
                var local = new Vector3(w * MathF.Cos(theta), m.Loft.Y[i] + h * MathF.Sin(theta), m.Loft.Z[i]);
                var n = new Vector3(MathF.Cos(theta) / MathF.Max(1e-3f, w), MathF.Sin(theta) / MathF.Max(1e-3f, h), 0f).Normalized();
                return (m.Local * local, (m.Local.Basis * n).Normalized(), MathF.Min(w, h));
            }
            case Shape.Taper:
            {
                float t = rng.Range(0.2f, 0.8f), r = Mathf.Lerp(m.Size.X, m.Size.Z, t) * 0.5f;
                var local = new Vector3(r * MathF.Cos(theta), t * m.Size.Y, r * MathF.Sin(theta));
                var n = new Vector3(MathF.Cos(theta), 0f, MathF.Sin(theta));
                return (m.Local * local, (m.Local.Basis * n).Normalized(), r);
            }
            default:
            {
                Vector3 half = m.Size * 0.5f;
                var dir = new Vector3(MathF.Cos(theta), MathF.Sin(theta) * 0.8f + 0.2f, rng.Range(-0.6f, 0.6f)).Normalized();
                var local = new Vector3(dir.X * half.X, dir.Y * half.Y, dir.Z * half.Z);
                return (m.Local * local, (m.Local.Basis * dir).Normalized(), MathF.Min(half.X, MathF.Min(half.Y, MathF.Max(0.01f, half.Z))));
            }
        }
    }

    static Prim Biggest(Piece p)
    {
        Prim best = p.Prims[0];
        float v = -1f;
        foreach (Prim m in p.Prims)
        {
            float pv = m.Shape == Shape.Loft ? 99f : m.Size.X * m.Size.Y * MathF.Max(0.01f, m.Size.Z);
            if (pv > v && m.Material != Mat.Eye) { v = pv; best = m; }
        }
        return best;
    }

    /// <summary>A tumorous lump on the skin: a main swelling and a smaller one beside it, in the skin's own colour gone mottled.</summary>
    static float Growth(Piece p, Paint k, Rng rng, float strength, float width)
    {
        if (p == null || p.Prims.Count == 0) return 0f;
        var (at, n, girth) = SurfacePoint(Biggest(p), rng);
        // Sized off the body (a fifth of its width or so), never more than the local girth allows.
        float r = MathF.Min(width * rng.Range(0.2f, 0.28f) * (0.6f + 0.4f * strength), girth * 0.8f);
        // Swollen tissue: paler and pinker than the hide, a little glossy where it is stretched.
        Color c = k.Base.Lerp(new Color(0.72f, 0.56f, 0.52f), 0.45f).Darkened(rng.Range(0f, 0.1f));
        var basis = Geo.AlongY(n);
        p.Add(Shape.Sphere, new Vector3(r * 2f, r * 1.5f, r * 1.8f), at + n * r * 0.3f, c, 0.2f, 0f, basis, 12, Mat.Membrane);
        float vol = r * r * r * 0.63f;
        int satellites = rng.Int(1, 3);
        for (int j = 0; j < satellites; j++)
        {
            float r2 = r * rng.Range(0.35f, 0.6f), a = rng.Range(0f, MathF.Tau);
            Vector3 off = basis * new Vector3(MathF.Cos(a) * r * 0.95f, 0f, MathF.Sin(a) * r * 0.95f);
            p.Add(Shape.Sphere, new Vector3(r2 * 2f, r2 * 1.6f, r2 * 2f), at + off + n * r2 * 0.25f, c.Darkened(0.06f + 0.04f * j), 0.2f, 0f, basis, 10, Mat.Membrane);
            vol += r2 * r2 * r2 * 0.8f;
        }
        return 4.19f * vol;
    }

    /// <summary>A thicker (or thinner) leg on one side: radii only, never length, so the foot still plants.</summary>
    static void Thicken(Piece p, float f)
    {
        for (int i = 0; i < p.Prims.Count; i++)
        {
            Prim m = p.Prims[i];
            if (m.Shape == Shape.Loft) continue;
            m.Size = m.Shape is Shape.Taper or Shape.Capsule or Shape.Cylinder or Shape.Cone ? new Vector3(m.Size.X * f, m.Size.Y, m.Size.Z * f) : m.Size * f;
            p.Prims[i] = m;
        }
    }

    /// <summary>One side of the head bigger than the other: a heavier horn, a wider eye, a bigger cheek.</summary>
    static void Lopside(Piece head, float side, float a)
    {
        for (int i = 0; i < head.Prims.Count; i++)
        {
            Prim m = head.Prims[i];
            float x = m.Local.Origin.X;
            if (MathF.Abs(x) < 0.01f || m.Shape == Shape.Loft) continue;
            float f = MathF.Sign(x) == MathF.Sign(side) ? a : 1f / MathF.Sqrt(a);
            m.Size *= f;
            m.Local = new Transform3D(m.Local.Basis, new Vector3(x * (0.5f + 0.5f * f), m.Local.Origin.Y, m.Local.Origin.Z));
            head.Prims[i] = m;
        }
    }

    /// <summary>A few small extra eyes clustered above the true eye on one side.</summary>
    static void EyeCluster(Piece head, Paint k, Rng rng, float side, float hs)
    {
        var center = new Vector3(side * 0.34f, 0.32f, -0.22f) * hs;
        int n = rng.Int(3, 4);
        for (int i = 0; i < n; i++)
        {
            Vector3 at = center + new Vector3(rng.Range(-0.08f, 0.08f), rng.Range(-0.06f, 0.06f), rng.Range(-0.08f, 0.08f)) * hs;
            Parts.Eye(head, k, at, hs * rng.Range(0.045f, 0.07f));
        }
        // The swollen skin they sit in.
        head.Add(Shape.Sphere, new Vector3(0.26f, 0.16f, 0.26f) * hs, center - new Vector3(0f, 0.03f, 0f) * hs, k.Base.Darkened(0.1f), 0.4f, 0f, null, 10);
    }

    /// <summary>A small, withered spare limb hanging from the flank: two joints and a claw, held tight to the body.</summary>
    static float SpareLimb(Piece body, Paint k, Rng rng, float side, Species s)
    {
        float W = MathF.Max(0.08f, s.Body.Width), L = MathF.Max(0.2f, s.Body.Size);
        var root = new Vector3(side * W * 0.42f, -s.Body.Height * 0.05f, rng.Range(-0.15f, 0.1f) * L);
        float len = MathF.Min(L * 0.3f, MathF.Max(0.15f, s.Body.LegLength * 0.45f));
        float r = MathF.Max(0.012f, W * 0.07f);
        Vector3 knee = root + new Vector3(side * len * 0.45f, -len * 0.35f, len * 0.15f);
        Vector3 tip = knee + new Vector3(side * len * 0.05f, -len * 0.5f, -len * 0.2f);
        body.Taper(root, knee, r, r * 0.8f, k.Base.Darkened(0.12f), 0.5f, 0f, Mat.Skin, 8);
        body.Taper(knee, tip, r * 0.8f, r * 0.5f, k.Base.Darkened(0.18f), 0.3f, 0f, Mat.Skin, 8);
        body.Horn(k.Horn, r * 0.5f, Mat.Horn, tip, tip + new Vector3(0f, -len * 0.12f, -len * 0.1f), tip + new Vector3(0f, -len * 0.14f, -len * 0.22f));
        return MathF.PI * r * r * len;
    }

    static bool LastInChain(RigLayout l, Piece p)
    {
        int cut = p.Name.LastIndexOf('_');
        if (cut < 0 || !int.TryParse(p.Name[(cut + 1)..], out int i)) return false;
        return Find(l, $"{p.Name[..cut]}_{i + 1}") == null;
    }

    /// <summary>A tendril's tip forks in two.</summary>
    static void Split(Piece t, Paint k, Rng rng)
    {
        if (t.Prims.Count == 0) return;
        Prim m = t.Prims[0];
        float len = MathF.Max(0.02f, m.Size.Y), r = MathF.Max(0.003f, m.Size.Z * 0.5f);
        Vector3 from = m.Local * new Vector3(0f, len * 0.55f, 0f);
        Vector3 dir = (m.Local.Basis * new Vector3(rng.Chance(0.5f) ? 0.6f : -0.6f, 1f, 0.2f)).Normalized();
        t.Taper(from, from + dir * len * 0.7f, r, r * 0.3f + 0.002f, m.Color, 0f, m.Glow);
    }

    /// <summary>A blotch of skin in a hue off the species' palette, lying on the body's surface.</summary>
    static void Patch(Piece body, Species s, Rng rng)
    {
        Prim m = Biggest(body);
        var (at, n, girth) = SurfacePoint(m, rng);
        s.Colors.Base.ToHsv(out float h, out float sat, out float v);
        float hue = h + rng.Range(0.3f, 0.5f) * (rng.Chance(0.5f) ? 1f : -1f);
        hue -= MathF.Floor(hue);
        Color c = Color.FromHsv(hue, Math.Clamp(sat * 0.8f + 0.12f, 0.15f, 0.55f), Math.Clamp(v * 1.05f, 0.25f, 0.7f));
        float r = MathF.Max(0.04f, MathF.Min(s.Body.Width * rng.Range(0.42f, 0.55f), girth * 1.3f));
        body.Add(Shape.Sphere, new Vector3(r * 2f, r * 0.35f, r * 1.7f), at - n * r * 0.06f, c, 0.5f, 0f, Geo.AlongY(n), 14);
    }
}
