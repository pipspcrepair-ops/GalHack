using System;
using Godot;

namespace Wildlife;

/// <summary>A species' colours, worked out once for all its parts.</summary>
public sealed class Paint
{
    public readonly Color Base, Accent, Belly, Bone, Dark, Horn, EyeColor, Mouth, Glow, Veins;
    public readonly float EyeGlow, GlowStrength;
    public readonly bool Corrupted;
    /// <summary>No eyes: sensing pits where they would be (the deep fauna far down).</summary>
    public readonly bool Blind;

    public Paint(Species s)
    {
        Palette p = s.Colors;
        Base = p.Base;
        Accent = p.Accent;
        Belly = p.Belly;
        Bone = new Color(0.86f, 0.8f, 0.66f).Lerp(p.Base, 0.15f);
        Dark = p.Base.Darkened(0.6f);
        // Horn, claws and teeth: bone at the tip, darkening towards the root.
        Horn = new Color(0.32f, 0.27f, 0.22f).Lerp(p.Base.Darkened(0.4f), 0.3f);
        Mouth = new Color(0.32f, 0.06f, 0.07f);
        Glow = p.Glow;
        GlowStrength = p.GlowStrength;
        Corrupted = s.Corrupted;
        Blind = s.Blind;
        Veins = s.Corrupted ? p.Accent : p.Glow;
        // Small, dark, wet eyes with a warm glint read as an animal that is watching you; big
        // pale eyes with pupils read as a cartoon. Glowing species get lamp eyes instead.
        EyeGlow = p.GlowStrength > 0.15f ? 1f : 0f;
        EyeColor = EyeGlow > 0f ? p.Glow : new Color(0.16f, 0.1f, 0.03f);
        if (s.Corrupted)
        {
            Bone = new Color(0.82f, 0.78f, 0.76f);
            Horn = new Color(0.2f, 0.14f, 0.18f);
        }
    }
}

/// <summary>How a head is built: where the snout ends and the crown is, so horns sit right.</summary>
public readonly record struct HeadShape(float Length, float Crown, float Width);

/// <summary>
/// The part library: heads, horns, backs, tail tips and Bloom growths. Shapes are lofts and
/// rounded tapers, not stacked balls: heads have a cranium, a brow, a jaw and deep-set eyes;
/// horns and claws curve and thin to a point. Head space: origin at the neck joint, facing -Z,
/// hs the head size. Body space: centred, facing -Z, built round the body's own loft. Tail-tip
/// space: the last tail bone, running along +Y.
/// </summary>
public static class Parts
{
    // ---- Heads ------------------------------------------------------------------------

    public static HeadShape Head(Piece p, string kind, float hs, Paint k, Rng rng)
    {
        switch (kind)
        {
            case "jaws": return Jaws(p, hs, k, rng);
            case "skull": return Skull(p, hs, k);
            case "beak": return Beak(p, hs, k);
            case "mandibles": return Mandibles(p, hs, k);
            case "hammer": return Hammer(p, hs, k);
            case "maw": return Maw(p, hs, k);
            case "lantern": return Lantern(p, hs, k);
            case "dome": return Dome(p, hs, k);
            default: return Snout(p, hs, k);
        }
    }

    /// <summary>A cranium-and-muzzle loft: the base of most heads. Returns the snout tip z.</summary>
    static float Cranium(Piece p, float hs, Paint k, float length, float width, float height, float muzzle, float pattern = 0.7f)
    {
        float z0 = 0.18f * hs, z1 = -length * hs;
        var profile = LoftProfile.Smooth(z0, z1, new[]
        {
            (0f, 0.4f * width * hs, 0.38f * height * hs, 0.06f * hs),
            (0.35f, 0.44f * width * hs, 0.36f * height * hs, 0.08f * hs),
            (0.72f, 0.3f * width * muzzle * hs, 0.25f * height * hs, 0.01f * hs),
            (1f, 0.22f * width * muzzle * hs, 0.19f * height * hs, -0.03f * hs),
        }, 12, 0.22f);
        p.Loft(profile, k.Base, k.Belly, pattern, 0f, Mat.Skin, null, null, 12);
        return z1;
    }

    /// <summary>A lower jaw hinged under the cranium, dropped open by <paramref name="drop"/> radians.</summary>
    static void LowerJaw(Piece p, float hs, Paint k, float length, float width, float drop, float thickness = 1f)
    {
        var profile = LoftProfile.Smooth(0.05f * hs, -length * hs, new[]
        {
            (0f, 0.3f * width * hs, 0.12f * thickness * hs, 0f),
            (0.6f, 0.24f * width * hs, 0.09f * thickness * hs, 0f),
            (1f, 0.15f * width * hs, 0.07f * thickness * hs, 0.01f * hs),
        }, 10, 0.25f);
        var hinge = new Vector3(0f, -0.12f * hs, 0.02f * hs);
        p.Loft(profile, k.Belly.Lerp(k.Base, 0.4f), k.Belly, 0.3f, 0f, Mat.Skin, hinge, new Basis(Vector3.Right, -drop), 10);
    }

    /// <summary>Deep-set eyes under a heavy brow, one each side.</summary>
    static void Eyes(Piece p, Paint k, float hs, float x, float y, float z, float size, float brow = 1f)
    {
        for (int s = -1; s <= 1; s += 2)
        {
            var at = new Vector3(s * x, y, z) * hs;
            Eye(p, k, at, size * hs);
            if (brow > 0f)
                p.Add(Shape.Sphere, new Vector3(size * 2.1f, size * 0.8f, size * 2.2f) * hs * brow, at + new Vector3(-s * size * 0.15f, size * 0.55f, -size * 0.1f) * hs,
                    k.Base.Darkened(0.12f), 0.5f, 0f, new Basis(Vector3.Back, s * 0.25f));
        }
    }

    static HeadShape Snout(Piece p, float hs, Paint k)
    {
        float tip = Cranium(p, hs, k, 1.2f, 0.9f, 0.85f, 0.85f);
        LowerJaw(p, hs, k, 1.05f, 0.85f, 0.06f);
        for (int s = -1; s <= 1; s += 2)
        {
            p.Add(Shape.Sphere, new Vector3(0.07f, 0.05f, 0.08f) * hs, new Vector3(s * 0.09f * hs, 0.06f * hs, tip + 0.06f * hs), k.Dark, 0f, 0f, null, 0, Mat.Eye);
            // Ears: small and swept back, flat to the skull.
            var ear = new Basis(Vector3.Up, s * 0.5f) * new Basis(Vector3.Right, 0.6f);
            p.Add(Shape.Sphere, new Vector3(0.1f, 0.05f, 0.28f) * hs, new Vector3(s * 0.28f, 0.3f, 0.12f) * hs, k.Base.Darkened(0.1f), 0.5f, 0f, ear);
        }
        Eyes(p, k, hs, 0.36f, 0.17f, -0.36f, 0.09f);
        return new HeadShape(-tip / hs, 0.4f, 0.9f);
    }

    static HeadShape Jaws(Piece p, float hs, Paint k, Rng rng)
    {
        float tip = Cranium(p, hs, k, 1.45f, 0.82f, 0.72f, 0.72f);
        const float drop = 0.3f;
        LowerJaw(p, hs, k, 1.3f, 0.78f, drop, 1.1f);
        // The mouth: dark gums between the jaws, then rows of teeth on both.
        p.Add(Shape.Sphere, new Vector3(0.42f, 0.16f, 1.05f) * hs, new Vector3(0f, -0.16f, -0.62f) * hs, k.Mouth, 0f, 0f, Pitch(-drop * 50f));
        int teeth = 6;
        var jaw = new Basis(Vector3.Right, -drop);
        for (int i = 0; i < teeth; i++)
        {
            float t = (i + 0.5f) / teeth;
            float z = Mathf.Lerp(-0.25f, -1.32f, t) * hs, w = Mathf.Lerp(0.3f, 0.15f, t) * hs, len = (i == 1 ? 0.2f : 0.11f + rng.Range(0f, 0.04f)) * hs;
            for (int s = -1; s <= 1; s += 2)
            {
                var top = new Vector3(s * w, -0.08f * hs, z);
                p.Taper(top, top + new Vector3(0f, -len, -0.02f * hs), 0.03f * hs, 0.003f * hs, k.Bone, 0f, 0f, Mat.Horn, 6);
                Vector3 low = new Vector3(0f, -0.12f * hs, 0.02f * hs) + jaw * new Vector3(s * w * 0.92f, 0.08f * hs, z * 0.92f);
                p.Taper(low, low + jaw * new Vector3(0f, len * 0.8f, -0.02f * hs), 0.026f * hs, 0.003f * hs, k.Bone, 0f, 0f, Mat.Horn, 6);
            }
        }
        // Forward-set eyes under a hard brow: a hunter's face.
        Eyes(p, k, hs, 0.29f, 0.16f, -0.46f, 0.085f, 1.15f);
        return new HeadShape(-tip / hs, 0.34f, 0.82f);
    }

    static HeadShape Skull(Piece p, float hs, Paint k)
    {
        float tip = Cranium(p, hs, k, 1.1f, 1.0f, 0.9f, 0.95f);
        LowerJaw(p, hs, k, 1.12f, 1.0f, 0.04f, 1.4f);
        // A bony shield over the crown and down the face.
        var plate = LoftProfile.Smooth(0.2f * hs, -0.95f * hs, new[]
        {
            (0f, 0.46f * hs, 0.16f * hs, 0.28f * hs), (0.4f, 0.42f * hs, 0.14f * hs, 0.26f * hs), (1f, 0.18f * hs, 0.08f * hs, 0.12f * hs),
        }, 10, 0.2f);
        p.Loft(plate, k.Accent.Lerp(k.Bone, 0.25f), k.Accent.Darkened(0.3f), 0.2f, 0f, Mat.Carapace, null, null, 12);
        // Tusks curling up out of the underbite.
        for (int s = -1; s <= 1; s += 2)
            p.Horn(k.Bone, 0.07f * hs, Mat.Horn, new Vector3(s * 0.28f, -0.14f, -0.95f) * hs, new Vector3(s * 0.34f, 0.04f, -1.1f) * hs, new Vector3(s * 0.3f, 0.22f, -1.02f) * hs);
        Eyes(p, k, hs, 0.4f, 0.12f, -0.32f, 0.08f, 1.2f);
        return new HeadShape(-tip / hs, 0.44f, 1f);
    }

    static HeadShape Beak(Piece p, float hs, Paint k)
    {
        Cranium(p, hs, k, 0.75f, 0.82f, 0.85f, 0.95f);
        Color beak = k.Horn.Lerp(k.Bone, 0.35f);
        // Upper beak: a hooked blade. Lower beak: shorter, tucked inside it.
        p.Horn(beak, 0.24f * hs, Mat.Horn, new Vector3(0f, 0.02f, -0.55f) * hs, new Vector3(0f, 0.0f, -1.05f) * hs, new Vector3(0f, -0.1f, -1.38f) * hs, new Vector3(0f, -0.3f, -1.45f) * hs);
        p.Horn(beak.Darkened(0.2f), 0.17f * hs, Mat.Horn, new Vector3(0f, -0.14f, -0.5f) * hs, new Vector3(0f, -0.18f, -0.95f) * hs, new Vector3(0f, -0.22f, -1.2f) * hs);
        Eyes(p, k, hs, 0.33f, 0.16f, -0.32f, 0.1f, 1.1f);
        return new HeadShape(1.45f, 0.42f, 0.82f);
    }

    static HeadShape Mandibles(Piece p, float hs, Paint k)
    {
        // A smooth armoured capsule with two big compound eyes and a pair of curved pincers.
        var head = LoftProfile.Smooth(0.15f * hs, -0.85f * hs, new[]
        {
            (0f, 0.4f * hs, 0.32f * hs, 0.04f * hs), (0.45f, 0.48f * hs, 0.36f * hs, 0.06f * hs), (1f, 0.3f * hs, 0.2f * hs, -0.04f * hs),
        }, 10, 0.3f);
        p.Loft(head, k.Base, k.Belly, 0.5f, 0f, Mat.Carapace, null, null, 12);
        for (int s = -1; s <= 1; s += 2)
        {
            p.Add(Shape.Sphere, new Vector3(0.16f, 0.2f, 0.24f) * hs, new Vector3(s * 0.38f, 0.12f, -0.46f) * hs, k.EyeGlow > 0f ? k.EyeColor : k.EyeColor.Darkened(0.4f), 0f, k.EyeGlow * 0.5f, null, 10, Mat.Eye);
            p.Horn(k.Horn, 0.1f * hs, Mat.Horn, new Vector3(s * 0.22f, -0.12f, -0.72f) * hs, new Vector3(s * 0.4f, -0.16f, -1.02f) * hs,
                new Vector3(s * 0.32f, -0.17f, -1.3f) * hs, new Vector3(s * 0.08f, -0.16f, -1.42f) * hs);
            p.Taper(new Vector3(s * 0.1f, -0.2f, -0.75f) * hs, new Vector3(s * 0.12f, -0.42f, -0.92f) * hs, 0.035f * hs, 0.015f * hs, k.Dark, 0f, 0f, Mat.Carapace);
        }
        return new HeadShape(1.35f, 0.38f, 0.96f);
    }

    static HeadShape Hammer(Piece p, float hs, Paint k)
    {
        Cranium(p, hs, k, 0.7f, 0.7f, 0.7f, 0.9f);
        // The hammer: a flat armoured bar with the eyes out at its ends.
        var bar = LoftProfile.Smooth(-1.05f * hs, 1.05f * hs, new[]
        {
            (0f, 0.22f * hs, 0.14f * hs, 0f), (0.5f, 0.28f * hs, 0.17f * hs, 0.02f * hs), (1f, 0.22f * hs, 0.14f * hs, 0f),
        }, 12, 0.12f);
        p.Loft(bar, k.Base, k.Belly, 0.8f, 0f, Mat.Skin, new Vector3(0f, 0.03f, -0.62f) * hs, new Basis(Vector3.Up, MathF.PI * 0.5f), 12);
        for (int s = -1; s <= 1; s += 2) Eye(p, k, new Vector3(s * 1.04f, 0.05f, -0.62f) * hs, 0.13f * hs);
        p.Add(Shape.Sphere, new Vector3(0.5f, 0.08f, 0.16f) * hs, new Vector3(0, -0.13f, -0.78f) * hs, k.Mouth);
        return new HeadShape(0.85f, 0.34f, 2f);
    }

    static HeadShape Maw(Piece p, float hs, Paint k)
    {
        // No eyes: a muscular tube ending in a round mouth ringed with teeth.
        var tube = LoftProfile.Smooth(0.15f * hs, -1.05f * hs, new[]
        {
            (0f, 0.36f * hs, 0.36f * hs, 0f), (0.6f, 0.4f * hs, 0.4f * hs, 0f), (0.9f, 0.48f * hs, 0.48f * hs, 0f), (1f, 0.44f * hs, 0.44f * hs, 0f),
        }, 12, 0.08f);
        p.Loft(tube, k.Base, k.Belly, 0.8f, 0f, Mat.Skin, null, null, 14);
        p.Add(Shape.Sphere, new Vector3(0.8f, 0.8f, 0.12f) * hs, new Vector3(0f, 0f, -1.04f) * hs, k.Mouth);
        for (int ring = 0; ring < 2; ring++)
        {
            int n = ring == 0 ? 10 : 7;
            float r = ring == 0 ? 0.36f : 0.2f;
            for (int i = 0; i < n; i++)
            {
                float a = (i + ring * 0.5f) * MathF.Tau / n;
                var at = new Vector3(MathF.Cos(a) * r, MathF.Sin(a) * r, -1.06f + ring * 0.04f) * hs;
                p.Taper(at, at + new Vector3(-MathF.Cos(a) * 0.14f, -MathF.Sin(a) * 0.14f, -0.06f) * hs, 0.045f * hs, 0.004f * hs, k.Bone, 0f, 0f, Mat.Horn, 6);
            }
        }
        return new HeadShape(1.05f, 0.4f, 0.8f);
    }

    static HeadShape Lantern(Piece p, float hs, Paint k)
    {
        // An angler's head: a gaping lower jaw full of needles, and a lure hung out in front.
        Cranium(p, hs, k, 0.95f, 1.05f, 0.9f, 1.05f);
        LowerJaw(p, hs, k, 1.0f, 1.1f, 0.38f, 1.3f);
        p.Add(Shape.Sphere, new Vector3(0.62f, 0.22f, 0.75f) * hs, new Vector3(0f, -0.2f, -0.52f) * hs, k.Mouth, 0f, 0f, Pitch(-18f));
        var jaw = new Basis(Vector3.Right, -0.38f);
        for (int i = 0; i < 7; i++)
        {
            float x = (i - 3f) * 0.12f, z = -0.88f + MathF.Abs(x) * 0.6f;
            p.Taper(new Vector3(x, -0.06f, z) * hs, new Vector3(x * 1.1f, -0.3f, z - 0.02f) * hs, 0.025f * hs, 0.003f * hs, k.Bone, 0f, 0f, Mat.Horn, 6);
            Vector3 low = new Vector3(0f, -0.12f * hs, 0.02f * hs) + jaw * new Vector3(x * 1.15f * hs, 0.1f * hs, z * 1.1f * hs);
            p.Taper(low, low + jaw * new Vector3(0f, 0.17f * hs, -0.03f * hs), 0.022f * hs, 0.003f * hs, k.Bone, 0f, 0f, Mat.Horn, 6);
        }
        Vector3 l0 = new Vector3(0, 0.32f, -0.3f) * hs, l1 = new Vector3(0, 0.85f, -0.6f) * hs, l2 = new Vector3(0, 0.7f, -1.25f) * hs;
        p.Taper(l0, l1, 0.05f * hs, 0.035f * hs, k.Base);
        p.Taper(l1, l2, 0.035f * hs, 0.025f * hs, k.Base);
        p.Add(Shape.Sphere, Vector3.One * 0.24f * hs, l2 + Vector3.Down * 0.1f * hs, k.Glow, 0f, 1f);
        Eyes(p, k, hs, 0.42f, 0.2f, -0.38f, 0.07f, 1.2f);
        return new HeadShape(1.05f, 0.42f, 1.05f);
    }

    static HeadShape Dome(Piece p, float hs, Paint k)
    {
        // Faceless: a smooth armoured dome with one dark slit where a face should be.
        var dome = LoftProfile.Smooth(0.2f * hs, -1.2f * hs, new[]
        {
            (0f, 0.42f * hs, 0.4f * hs, 0.06f * hs), (0.4f, 0.46f * hs, 0.4f * hs, 0.08f * hs), (0.8f, 0.26f * hs, 0.24f * hs, -0.02f * hs), (1f, 0.14f * hs, 0.12f * hs, -0.08f * hs),
        }, 12, 0.2f);
        p.Loft(dome, k.Base.Lightened(0.06f), k.Belly, 0.3f, 0f, Mat.Carapace, null, null, 14);
        // A keel down the crown, and a dark mouth slit underneath where a face should be.
        p.Add(Shape.Sphere, new Vector3(0.08f, 0.2f, 1.1f) * hs, new Vector3(0, 0.42f, -0.35f) * hs, k.Base.Darkened(0.2f), 0.2f, 0f, Pitch(-6f), 10, Mat.Carapace);
        p.Add(Shape.Sphere, new Vector3(0.4f, 0.05f, 0.6f) * hs, new Vector3(0, -0.2f, -0.82f) * hs,
            k.EyeGlow > 0 ? k.Glow : k.Mouth, 0f, k.EyeGlow * 0.6f, Pitch(-8f), 0, Mat.Eye);
        return new HeadShape(1.0f, 0.48f, 1f);
    }

    // ---- Horns ------------------------------------------------------------------------

    public static void Horns(Piece p, string kind, float hs, Paint k, Rng rng, HeadShape head)
    {
        float tip = -head.Length;
        float crown = head.Crown;
        switch (kind)
        {
            case "pair":
                for (int s = -1; s <= 1; s += 2)
                    p.Horn(k.Bone.Lerp(k.Horn, 0.3f), 0.09f * hs, Mat.Horn, new Vector3(s * 0.22f, crown - 0.06f, -0.2f) * hs, new Vector3(s * 0.34f, crown + 0.24f, 0.05f) * hs,
                        new Vector3(s * 0.38f, crown + 0.36f, 0.4f) * hs, new Vector3(s * 0.32f, crown + 0.34f, 0.68f) * hs);
                break;
            case "ram":
                for (int s = -1; s <= 1; s += 2)
                {
                    var pts = new Vector3[7];
                    for (int i = 0; i < pts.Length; i++)
                    {
                        float a = i / (float)(pts.Length - 1) * MathF.PI * 1.55f, r = 0.32f * (1f - 0.35f * i / pts.Length);
                        pts[i] = new Vector3(s * (0.3f + 0.05f * i), crown - 0.05f + MathF.Sin(a) * r, -0.12f + (1f - MathF.Cos(a)) * r * 0.9f) * hs;
                    }
                    p.Horn(k.Horn.Lerp(k.Bone, 0.45f), 0.13f * hs, Mat.Horn, pts);
                }
                break;
            case "antlers":
                for (int s = -1; s <= 1; s += 2)
                {
                    Color c = k.Bone.Lerp(new Color(0.42f, 0.3f, 0.2f), 0.45f);
                    Vector3 a = new Vector3(s * 0.2f, crown - 0.04f, -0.12f) * hs;
                    Vector3 b = a + new Vector3(s * 0.32f, 0.5f, 0.15f) * hs;
                    Vector3 t = b + new Vector3(s * 0.2f, 0.5f, 0.18f) * hs;
                    p.Taper(a, b, 0.055f * hs, 0.042f * hs, c, 0f, 0f, Mat.Horn, 7);
                    p.Taper(b, t, 0.042f * hs, 0.012f * hs, c, 0f, 0f, Mat.Horn, 7);
                    Vector3 m = a + (b - a) * 0.55f;
                    p.Taper(m, m + new Vector3(s * 0.04f, 0.32f, -0.28f) * hs, 0.034f * hs, 0.01f * hs, c, 0f, 0f, Mat.Horn, 6);
                    p.Taper(b, b + new Vector3(s * 0.34f, 0.18f, -0.14f) * hs, 0.034f * hs, 0.01f * hs, c, 0f, 0f, Mat.Horn, 6);
                    p.Taper(b + (t - b) * 0.6f, b + (t - b) * 0.6f + new Vector3(s * 0.05f, 0.24f, 0.2f) * hs, 0.028f * hs, 0.008f * hs, c, 0f, 0f, Mat.Horn, 6);
                }
                break;
            case "spike":
                p.Horn(k.Bone.Lerp(k.Horn, 0.2f), 0.13f * hs, Mat.Horn, new Vector3(0, 0.12f, tip + 0.3f) * hs, new Vector3(0, 0.38f, tip + 0.12f) * hs, new Vector3(0, 0.7f, tip + 0.18f) * hs);
                break;
            case "brow":
                // Heavy horns that sweep forward over the face: a ram's weapon, aimed at you.
                for (int s = -1; s <= 1; s += 2)
                    p.Horn(k.Horn.Lerp(k.Bone, 0.5f), 0.12f * hs, Mat.Horn, new Vector3(s * 0.3f, crown - 0.08f, -0.25f) * hs, new Vector3(s * 0.58f, crown + 0.12f, -0.4f) * hs,
                        new Vector3(s * 0.66f, crown + 0.2f, -0.72f) * hs, new Vector3(s * 0.5f, crown + 0.16f, -1.0f) * hs);
                break;
            case "tusks":
                for (int s = -1; s <= 1; s += 2)
                {
                    float z = tip * 0.72f;
                    p.Horn(k.Bone, 0.075f * hs, Mat.Horn, new Vector3(s * 0.24f, -0.16f, z) * hs, new Vector3(s * 0.36f, -0.2f, z - 0.3f) * hs,
                        new Vector3(s * 0.4f, 0.02f, z - 0.5f) * hs, new Vector3(s * 0.32f, 0.2f, z - 0.46f) * hs);
                }
                break;
            case "antennae":
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 a = new Vector3(s * 0.15f, crown - 0.08f, -0.55f) * hs;
                    p.Horn(k.Dark, 0.022f * hs, Mat.Carapace, a, a + new Vector3(s * 0.2f, 0.4f, -0.35f) * hs, a + new Vector3(s * 0.45f, 0.7f, -0.45f) * hs,
                        a + new Vector3(s * 0.7f, 0.82f, -0.2f) * hs, a + new Vector3(s * 0.85f, 0.78f, 0.15f) * hs);
                }
                break;
            case "frill":
            {
                // A bony frill behind the head, webbed with skin, every rib ending in a spike.
                p.Add(Shape.Sphere, new Vector3(2.0f, 1.45f, 0.08f) * hs, new Vector3(0, crown - 0.02f, 0.12f) * hs, k.Accent, 1f, 0f, Pitch(28), 0, Mat.Membrane);
                for (int i = 0; i < 9; i++)
                {
                    float a = MathF.PI * (0.06f + 0.88f * i / 8f);
                    var root = new Vector3(0, crown - 0.15f, 0.08f) * hs;
                    var at = root + new Basis(Vector3.Right, Mathf.DegToRad(28)) * (new Vector3(MathF.Cos(a) * 0.95f, MathF.Sin(a) * 0.7f, 0f) * hs);
                    p.Taper(root, at, 0.03f * hs, 0.022f * hs, k.Bone, 0f, 0f, Mat.Horn, 5);
                    Vector3 dir = (at - root).Normalized();
                    p.Taper(at, at + dir * 0.22f * hs, 0.04f * hs, 0.004f * hs, k.Bone, 0f, 0f, Mat.Horn, 6);
                }
                break;
            }
            case "crown":
                for (int i = 0; i < 7; i++)
                {
                    float a = i * MathF.Tau / 7f;
                    var at = new Vector3(MathF.Cos(a) * 0.26f, crown - 0.04f, -0.3f + MathF.Sin(a) * 0.26f) * hs;
                    p.Horn(k.Accent.Lightened(0.1f), 0.07f * hs, Mat.Horn, at, at + new Vector3(MathF.Cos(a) * 0.12f, 0.24f, MathF.Sin(a) * 0.12f) * hs,
                        at + new Vector3(MathF.Cos(a) * 0.14f, 0.42f, MathF.Sin(a) * 0.2f + 0.08f) * hs);
                }
                break;
        }
    }

    // ---- Backs ------------------------------------------------------------------------

    /// <summary>Height of the body's top at z (body space), from its loft.</summary>
    public static float Top(LoftProfile body, float z)
    {
        if (z <= body.Z[0]) return body.Y[0] + body.H[0];
        for (int i = 0; i + 1 < body.Count; i++)
        {
            if (z > body.Z[i + 1]) continue;
            float t = (z - body.Z[i]) / MathF.Max(1e-5f, body.Z[i + 1] - body.Z[i]);
            return Mathf.Lerp(body.Y[i] + body.H[i], body.Y[i + 1] + body.H[i + 1], t);
        }
        return body.Y[^1] + body.H[^1];
    }

    /// <summary>Half-width of the body at z.</summary>
    public static float HalfWidth(LoftProfile body, float z)
    {
        for (int i = 0; i + 1 < body.Count; i++)
        {
            if (z > body.Z[i + 1]) continue;
            float t = Math.Clamp((z - body.Z[i]) / MathF.Max(1e-5f, body.Z[i + 1] - body.Z[i]), 0f, 1f);
            return Mathf.Lerp(body.W[i], body.W[i + 1], t);
        }
        return body.W[^1];
    }

    /// <summary>Height of an ellipsoid body's top at z, for bodies that are not lofts.</summary>
    public static float Top(float z, float H, float L)
    {
        float t = Math.Clamp(2f * z / L, -0.98f, 0.98f);
        return 0.5f * H * MathF.Sqrt(1f - t * t);
    }

    public static void Back(Piece p, string kind, float W, float H, float L, Paint k, Rng rng, LoftProfile body = null)
    {
        float TopAt(float z) => body != null ? Top(body, z) : Top(z, H, L);
        float WidthAt(float z) => body != null ? HalfWidth(body, z) : 0.5f * W * MathF.Sqrt(MathF.Max(0f, 1f - MathF.Pow(2f * z / L, 2f)));
        switch (kind)
        {
            case "plates":
            {
                // Overlapping armour plates down the back, each lapping over the one behind.
                int n = Math.Clamp((int)MathF.Round(L / (W * 0.4f)), 5, 9);
                for (int i = 0; i < n; i++)
                {
                    float z = Mathf.Lerp(-0.36f, 0.38f, i / (float)(n - 1)) * L;
                    float w = WidthAt(z) * 1.5f;
                    p.Add(Shape.Sphere, new Vector3(w, H * 0.34f, L / n * 1.6f), new Vector3(0, TopAt(z) - H * 0.15f, z), i % 2 == 0 ? k.Accent : k.Accent.Darkened(0.12f),
                        0.15f, 0f, Pitch(-12f), 14, Mat.Carapace);
                }
                break;
            }
            case "shell":
            {
                // A domed carapace over the back, with a rim and growth ridges across it.
                if (body != null)
                {
                    var shell = LoftProfile.Smooth(body.Z[0] * 0.92f, body.Z[^1] * 0.95f, new[]
                    {
                        (0f, WidthAt(body.Z[0] * 0.6f) * 1.12f, H * 0.42f, H * 0.16f),
                        (0.5f, WidthAt(0f) * 1.14f, H * 0.5f, H * 0.18f),
                        (1f, WidthAt(body.Z[^1] * 0.6f) * 1.1f, H * 0.4f, H * 0.12f),
                    }, 14, 0.24f);
                    p.Loft(shell, k.Accent.Lerp(k.Base, 0.3f), k.Accent.Darkened(0.45f), 0.6f, 0f, Mat.Carapace, null, null, 16);
                }
                else
                    p.Add(Shape.Sphere, new Vector3(W * 1.12f, H * 0.95f, L * 0.92f), new Vector3(0, H * 0.2f, L * 0.02f), k.Accent.Lerp(k.Base, 0.35f), 0.6f, 0f, null, 0, Mat.Carapace);
                for (int i = 0; i < 4; i++)
                {
                    float z = Mathf.Lerp(-0.26f, 0.3f, i / 3f) * L;
                    p.Add(Shape.Sphere, new Vector3(WidthAt(z) * 2.25f, H * 0.12f, L * 0.035f), new Vector3(0, TopAt(z) + H * 0.04f, z), k.Accent.Darkened(0.35f), 0f, 0f, null, 0, Mat.Carapace);
                }
                break;
            }
            case "spines":
                for (int i = 0; i < 9; i++)
                {
                    float z = Mathf.Lerp(-0.34f, 0.42f, i / 8f) * L;
                    float h = H * 0.62f * (1f - 0.55f * i / 8f) * (i % 2 == 0 ? 1f : 0.8f);
                    Vector3 a = new(0, TopAt(z) - H * 0.06f, z);
                    p.Taper(a, a + new Vector3(0, 1f, 0.55f).Normalized() * h, W * 0.07f, W * 0.006f, k.Bone.Lerp(k.Dark, 0.35f), 0f, 0f, Mat.Horn, 7);
                }
                break;
            case "ridge":
                // Two staggered rows of bony scutes down the spine.
                for (int i = 0; i < 12; i++)
                {
                    float z = Mathf.Lerp(-0.36f, 0.42f, i / 11f) * L;
                    float s = i % 2 == 0 ? -1f : 1f, h = H * 0.18f * (1f - 0.4f * MathF.Abs(z / L));
                    var at = new Vector3(s * W * 0.08f, TopAt(z) - h * 0.2f, z);
                    p.Taper(at, at + new Vector3(s * 0.15f, 1f, 0.35f).Normalized() * h, W * 0.07f, W * 0.015f, k.Accent.Lerp(k.Bone, 0.3f), 0f, 0f, Mat.Horn, 6);
                }
                break;
            case "sail":
            {
                float z0 = -0.3f * L, z1 = 0.36f * L;
                p.Add(Shape.Sphere, new Vector3(W * 0.05f, H * 1.25f, L * 0.72f), new Vector3(0, TopAt(0f) + H * 0.32f, L * 0.03f), k.Accent, 1f, 0f, null, 0, Mat.Membrane);
                for (int i = 0; i < 7; i++)
                {
                    float z = Mathf.Lerp(z0, z1, i / 6f);
                    float top = TopAt(0f) + H * 0.3f + H * 0.6f * MathF.Sqrt(Math.Max(0f, 1f - MathF.Pow((z - L * 0.03f) / (L * 0.36f), 2f)));
                    p.Taper(new Vector3(0, TopAt(z) - H * 0.05f, z), new Vector3(0, top, z + L * 0.04f), W * 0.035f, W * 0.012f, k.Bone, 0f, 0f, Mat.Horn, 6);
                }
                break;
            }
            case "crystals":
            {
                int n = rng.Int(6, 10);
                Color crystal = (k.GlowStrength > 0f ? k.Glow : k.Accent).Lerp(Colors.White, 0.25f);
                for (int i = 0; i < n; i++)
                {
                    float z = rng.Range(-0.32f, 0.36f) * L, x = rng.Range(-0.28f, 0.28f) * W;
                    float h = H * rng.Range(0.5f, 1.15f), d = W * rng.Range(0.12f, 0.22f);
                    var tilt = new Basis(Vector3.Back, rng.Range(-0.45f, 0.45f) - x / W) * new Basis(Vector3.Right, rng.Range(-0.35f, 0.25f));
                    Vector3 at = new(x, TopAt(z) - H * 0.12f, z);
                    p.Add(Shape.Cylinder, new Vector3(d, h * 0.6f, d), at + tilt * Vector3.Up * h * 0.3f, crystal, 0f, 0.85f, tilt, 6, Mat.Horn);
                    p.Add(Shape.Cone, new Vector3(d, h * 0.4f, 0f), at + tilt * Vector3.Up * h * 0.8f, crystal.Lightened(0.2f), 0f, 0.95f, tilt, 6, Mat.Horn);
                }
                break;
            }
            case "bubbles":
            {
                int n = rng.Int(6, 11);
                for (int i = 0; i < n; i++)
                {
                    float z = rng.Range(-0.35f, 0.35f) * L, x = rng.Range(-0.3f, 0.3f) * W, d = W * rng.Range(0.16f, 0.36f);
                    p.Add(Shape.Sphere, Vector3.One * d, new Vector3(x, TopAt(z) + d * 0.05f, z), k.Belly.Lerp(k.Glow, 0.35f), 0f, 0.45f, null, 0, Mat.Membrane);
                }
                break;
            }
            case "vents":
            {
                int n = rng.Int(3, 5);
                for (int i = 0; i < n; i++)
                {
                    float z = Mathf.Lerp(-0.25f, 0.3f, n == 1 ? 0.5f : i / (float)(n - 1)) * L, x = rng.Range(-0.2f, 0.2f) * W;
                    float h = H * rng.Range(0.3f, 0.6f), d = W * rng.Range(0.14f, 0.2f);
                    Vector3 at = new(x, TopAt(z) - h * 0.15f, z);
                    p.Add(Shape.Cylinder, new Vector3(d * 1.3f, h, d * 0.9f), at + Vector3.Up * h * 0.4f, k.Dark, 0.3f, 0f, null, 9, Mat.Carapace);
                    p.Add(Shape.Cylinder, new Vector3(d * 0.75f, h * 0.06f, d * 0.75f), at + Vector3.Up * h * 0.91f, k.Accent.Lightened(0.3f), 0f, 0.9f, null, 9);
                }
                break;
            }
            case "fronds":
            {
                int n = rng.Int(6, 9);
                for (int i = 0; i < n; i++)
                {
                    float a = i * MathF.Tau / n + rng.Range(-0.2f, 0.2f);
                    var leaf = new Basis(Vector3.Up, -a) * new Basis(Vector3.Right, -0.55f - rng.Range(0f, 0.3f));
                    p.Add(Shape.Sphere, new Vector3(W * 0.36f, H * 0.04f, W * 1.05f), new Vector3(0, H * 0.45f, 0) + leaf * new Vector3(0, 0, -W * 0.5f),
                        i % 2 == 0 ? k.Base : k.Base.Lerp(k.Accent, 0.4f), 1f, 0f, leaf, 0, Mat.Membrane);
                }
                // A closed bud of hooked petals at the heart, not a flower: it bites.
                for (int i = 0; i < 6; i++)
                {
                    float a = i * MathF.Tau / 6f;
                    var root = new Vector3(MathF.Cos(a) * W * 0.12f, H * 0.5f, MathF.Sin(a) * W * 0.12f);
                    p.Horn(k.Accent, W * 0.1f, Mat.Skin, root, root + new Vector3(MathF.Cos(a) * W * 0.12f, H * 0.28f, MathF.Sin(a) * W * 0.12f),
                        root + new Vector3(-MathF.Cos(a) * W * 0.02f, H * 0.42f, -MathF.Sin(a) * W * 0.02f));
                }
                break;
            }
        }
    }

    /// <summary>
    /// What a Hollow (the parasitic fungus) grows out of an animal: a few caps standing on short
    /// stalks out of its back and flanks, mould spreading in pale patches over the hide, and
    /// clusters of thin fruiting bodies with a knob at each tip, some faintly lit. All of it sits on
    /// the body's own surface; the animal underneath is still plainly the animal.
    /// </summary>
    public static void Corruption(Piece p, float W, float H, float L, Paint k, Rng rng, LoftProfile body = null)
    {
        float TopAt(float z) => body != null ? Top(body, z) : Top(z, H, L);
        Color mould = k.Veins, gill = k.Veins.Darkened(0.35f);
        // The caps: domes on stalks, tilted out from the surface, each its own size; one big one.
        Color cap = k.Bone.Lerp(new Color(0.62f, 0.42f, 0.24f), rng.Range(0.3f, 0.8f));
        int caps = rng.Int(3, 6);
        for (int i = 0; i < caps; i++)
        {
            float z = rng.Range(-0.38f, 0.32f) * L, side = rng.Range(-0.32f, 0.32f);
            float y = TopAt(z) * rng.Range(0.7f, 0.98f);
            var root = new Vector3(side * W, y, z);
            var up = new Vector3(side * 1.4f, 1f, rng.Range(-0.3f, 0.3f)).Normalized();
            float r = W * rng.Range(0.16f, 0.26f) * (i == 0 ? 1.6f : 1f), stalk = r * rng.Range(0.6f, 1.2f);
            Vector3 top = root + up * stalk;
            p.Taper(root, top, r * 0.28f, r * 0.22f, k.Bone.Lerp(mould, 0.4f), 0f, 0f, Mat.Membrane, 6);
            p.Add(Shape.Sphere, new Vector3(r * 2f, r * 0.9f, r * 2f), top + up * r * 0.18f, cap.Darkened(rng.Range(0f, 0.15f)), 0.2f, 0f, Geo.AlongY(up), 10, Mat.Membrane);
            p.Add(Shape.Sphere, new Vector3(r * 1.8f, r * 0.3f, r * 1.8f), top - up * r * 0.05f, gill, 0f, 0.25f, Geo.AlongY(up), 10, Mat.Membrane);
        }
        // Mould: flat pale patches lying on the hide.
        int patches = rng.Int(3, 5);
        for (int i = 0; i < patches; i++)
        {
            float z = rng.Range(-0.45f, 0.4f) * L, x = rng.Range(-0.3f, 0.3f) * W, d = W * rng.Range(0.25f, 0.45f);
            p.Add(Shape.Sphere, new Vector3(d, d * 0.18f, d * rng.Range(0.8f, 1.4f)), new Vector3(x, TopAt(z) * rng.Range(0.82f, 1f), z), mould.Lerp(k.Base, rng.Range(0.1f, 0.35f)), 0.6f, 0f, null, 10, Mat.Membrane);
        }
        // Fruiting bodies: a few clusters of thin stalks, a knob on each, the knobs faintly lit.
        int clusters = rng.Int(1, 3);
        for (int c = 0; c < clusters; c++)
        {
            float z = rng.Range(-0.3f, 0.3f) * L, x = rng.Range(-0.25f, 0.25f) * W;
            var at = new Vector3(x, TopAt(z) * 0.95f, z);
            int stalks = rng.Int(4, 7);
            for (int i = 0; i < stalks; i++)
            {
                var dir = new Vector3(rng.Range(-0.45f, 0.45f), 1f, rng.Range(-0.45f, 0.45f)).Normalized();
                float len = H * rng.Range(0.12f, 0.24f);
                Vector3 tip = at + dir * len;
                p.Taper(at, tip, W * 0.03f, W * 0.018f, k.Bone.Lerp(mould, 0.5f), 0f, 0f, Mat.Membrane, 5);
                p.Add(Shape.Sphere, Vector3.One * W * rng.Range(0.06f, 0.09f), tip, mould, 0f, 0.7f, null, 6, Mat.Membrane);
            }
        }
    }

    // ---- Tail tips --------------------------------------------------------------------

    public static void TailTip(Piece p, string kind, float segLen, float W, Paint k)
    {
        float y = segLen;
        switch (kind)
        {
            case "quills":
                for (int i = 0; i < 9; i++)
                {
                    float a = i * MathF.Tau / 9f, spread = 0.35f + 0.15f * (i % 3);
                    var root = new Vector3(0, y * 0.75f, 0);
                    var dir = new Vector3(MathF.Cos(a) * spread, 1f, MathF.Sin(a) * spread).Normalized();
                    p.Taper(root, root + dir * W * (0.9f + 0.25f * (i % 2)), W * 0.035f, W * 0.004f, i % 2 == 0 ? k.Bone : k.Accent, 0f, k.GlowStrength * 0.3f, Mat.Horn, 5);
                }
                break;
            case "club":
                p.Add(Shape.Sphere, new Vector3(W * 0.5f, W * 0.62f, W * 0.5f), new Vector3(0, y + W * 0.12f, 0), k.Bone.Lerp(k.Base, 0.45f), 0.3f, 0f, null, 0, Mat.Horn);
                for (int i = 0; i < 5; i++)
                {
                    float a = i * MathF.Tau / 5f;
                    Vector3 at = new(MathF.Cos(a) * W * 0.22f, y + W * 0.14f, MathF.Sin(a) * W * 0.22f);
                    p.Taper(at, at + new Vector3(MathF.Cos(a), 0.25f, MathF.Sin(a)) * W * 0.24f, W * 0.07f, W * 0.008f, k.Bone, 0f, 0f, Mat.Horn, 6);
                }
                break;
            case "fin":
            {
                // A ribbed fin: bony rays fanning from the tip with skin stretched between.
                p.Add(Shape.Sphere, new Vector3(0.06f, 1.5f, 1.0f) * W, new Vector3(0, y + W * 0.45f, W * 0.2f), k.Accent, 1f, k.GlowStrength * 0.3f, null, 0, Mat.Membrane);
                for (int i = 0; i < 5; i++)
                {
                    float a = Mathf.Lerp(-0.5f, 1.0f, i / 4f);
                    var root = new Vector3(0, y, 0);
                    p.Taper(root, root + new Vector3(0, MathF.Cos(a), MathF.Sin(a)) * W * 1.05f, W * 0.03f, W * 0.01f, k.Bone, 0f, 0f, Mat.Horn, 5);
                }
                break;
            }
            case "stinger":
                p.Add(Shape.Sphere, new Vector3(W * 0.36f, W * 0.5f, W * 0.36f), new Vector3(0, y + W * 0.05f, 0), k.Base.Lerp(k.Accent, 0.4f), 0.4f, 0f, null, 0, Mat.Carapace);
                p.Horn(k.Dark.Lerp(k.Horn, 0.4f), W * 0.12f, Mat.Horn, new Vector3(0, y + W * 0.2f, 0), new Vector3(0, y + W * 0.5f, -W * 0.08f), new Vector3(0, y + W * 0.7f, -W * 0.3f));
                if (k.GlowStrength > 0f) p.Add(Shape.Sphere, Vector3.One * W * 0.07f, new Vector3(0, y + W * 0.62f, -W * 0.2f), k.Glow, 0f, 1f);
                break;
            case "spiked":
                for (int i = 0; i < 4; i++)
                {
                    float a = (i % 2 == 0 ? 1f : -1f) * 0.9f, h = y * (0.35f + 0.25f * (i / 2));
                    var root = new Vector3(0, h, 0);
                    p.Taper(root, root + new Vector3(MathF.Sin(a), 0.35f, 0.4f).Normalized() * W * 0.75f, W * 0.08f, W * 0.006f, k.Bone, 0f, 0f, Mat.Horn, 6);
                }
                break;
        }
    }

    // ---- Small helpers ----------------------------------------------------------------

    /// <summary>An eye: a small wet dark ball with a glint, or a lamp for glowing species.</summary>
    public static void Eye(Piece p, Paint k, Vector3 at, float size, int side = 0)
    {
        if (k.Blind)
        {
            // Blind: a shallow pit of bare skin where an eye would be, and a ring of smaller ones.
            p.Add(Shape.Sphere, new Vector3(size * 0.9f, size * 0.5f, size * 0.9f), at, k.Base.Darkened(0.45f), 0f, 0f, null, 6, Mat.Skin);
            p.Add(Shape.Sphere, Vector3.One * size * 0.35f, at + new Vector3(0f, -size * 0.7f, -size * 0.6f), k.Base.Darkened(0.4f), 0f, 0f, null, 6, Mat.Skin);
            return;
        }
        if (k.EyeGlow > 0f)
        {
            // Glowing eyes are narrow slanted slits: a lamp-eyed animal reads as a cartoon.
            float slant = at.X > 0f ? -0.35f : at.X < 0f ? 0.35f : 0f;
            p.Add(Shape.Sphere, new Vector3(size * 1.1f, size * 0.34f, size * 0.85f), at, k.EyeColor, 0f, 0.45f, new Basis(Vector3.Back, slant), 8, Mat.Eye);
        }
        else p.Add(Shape.Sphere, Vector3.One * size, at, k.EyeColor, 0f, 0f, null, 8, Mat.Eye);
    }

    public static Basis Pitch(float degrees) => new(Vector3.Right, Mathf.DegToRad(degrees));
}
