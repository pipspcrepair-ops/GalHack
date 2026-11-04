using System;
using Godot;

namespace Wildlife;

/// <summary>A species' colours, worked out once for all its parts.</summary>
public sealed class Paint
{
    public readonly Color Base, Accent, Belly, Bone, Dark, Sclera, Pupil, Glow, Veins;
    public readonly float EyeGlow, GlowStrength;
    public readonly bool Corrupted;

    public Paint(Species s)
    {
        Palette p = s.Colors;
        Base = p.Base;
        Accent = p.Accent;
        Belly = p.Belly;
        Bone = new Color(0.93f, 0.87f, 0.72f).Lerp(p.Base, 0.18f);
        Dark = p.Base.Darkened(0.65f);
        Glow = p.Glow;
        GlowStrength = p.GlowStrength;
        Corrupted = s.Corrupted;
        Veins = s.Corrupted ? p.Accent : p.Glow;
        // Glowing species get lamp eyes; the rest get pale eyes with dark pupils, which read
        // as alive and curious from a long way off.
        EyeGlow = p.GlowStrength > 0.15f ? 1f : 0f;
        Sclera = EyeGlow > 0f ? p.Glow : new Color(0.96f, 0.9f, 0.7f).Lerp(p.Accent, 0.15f);
        Pupil = new Color(0.04f, 0.03f, 0.05f);
        if (s.Corrupted) Bone = new Color(0.85f, 0.8f, 0.78f);
    }
}

/// <summary>
/// The part library: heads, horns, backs, tail tips and Bloom growths, each built from a
/// handful of primitives in a piece's local space. Head space: origin at the neck joint,
/// facing -Z. Body space: centred, facing -Z, the body an ellipsoid of width W, height H and
/// length L. Tail-tip space: the last tail bone, running along +Y.
/// </summary>
public static class Parts
{
    // ---- Heads ------------------------------------------------------------------------

    public static void Head(Piece p, string kind, float hs, Paint k, Rng rng)
    {
        switch (kind)
        {
            case "beak":
                p.Add(Shape.Sphere, new Vector3(0.75f, 0.75f, 0.8f) * hs, new Vector3(0, 0.05f, -0.4f) * hs, k.Base, 0.6f);
                p.Add(Shape.Cone, new Vector3(0.42f, 0.95f, 0) * hs, new Vector3(0, -0.02f, -1.15f) * hs, k.Bone.Lerp(k.Accent, 0.45f), 0f, 0f, Pitch(-90));
                p.Add(Shape.Cone, new Vector3(0.3f, 0.6f, 0) * hs, new Vector3(0, -0.2f, -0.95f) * hs, k.Bone.Lerp(k.Accent, 0.3f).Darkened(0.2f), 0f, 0f, Pitch(-100));
                Eyes(p, k, hs, 0.33f, 0.16f, -0.52f, 0.2f);
                break;
            case "mandibles":
                p.Add(Shape.Sphere, new Vector3(0.9f, 0.6f, 0.85f) * hs, new Vector3(0, 0, -0.42f) * hs, k.Base, 0.6f);
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 a = new Vector3(s * 0.26f, -0.12f, -0.75f) * hs;
                    Vector3 b = a + new Vector3(s * 0.05f, -0.05f, -0.45f) * hs;
                    Vector3 c = b + new Vector3(-s * 0.3f, 0f, -0.2f) * hs;
                    p.Rod(a, b, 0.14f * hs, k.Dark);
                    p.Rod(b, c, 0.1f * hs, k.Dark, 0, 0, cone: true);
                }
                for (int i = 0; i < 4; i++)
                    Eye(p, k, new Vector3((i - 1.5f) * 0.14f, 0.27f, -0.68f + MathF.Abs(i - 1.5f) * 0.06f) * hs, 0.11f * hs);
                break;
            case "hammer":
                p.Add(Shape.Sphere, new Vector3(0.5f, 0.5f, 0.6f) * hs, new Vector3(0, 0, -0.25f) * hs, k.Base, 0.6f);
                p.Add(Shape.Sphere, new Vector3(2.1f, 0.36f, 0.5f) * hs, new Vector3(0, 0.02f, -0.65f) * hs, k.Base, 1f);
                for (int s = -1; s <= 1; s += 2)
                    Eye(p, k, new Vector3(s * 1.02f, 0.05f, -0.65f) * hs, 0.26f * hs);
                p.Add(Shape.Sphere, new Vector3(0.5f, 0.12f, 0.2f) * hs, new Vector3(0, -0.16f, -0.82f) * hs, k.Pupil);
                break;
            case "eyestalks":
                p.Add(Shape.Sphere, new Vector3(0.75f, 0.6f, 0.8f) * hs, new Vector3(0, 0, -0.42f) * hs, k.Base, 0.6f);
                p.Add(Shape.Sphere, new Vector3(0.45f, 0.18f, 0.25f) * hs, new Vector3(0, -0.18f, -0.8f) * hs, k.Pupil);
                int stalks = rng.Int(2, 4);
                for (int i = 0; i < stalks; i++)
                {
                    float x = (i - (stalks - 1) * 0.5f) * 0.22f;
                    Vector3 a = new Vector3(x, 0.22f, -0.45f) * hs;
                    Vector3 b = a + new Vector3(x * 1.4f, 0.75f + 0.1f * (i % 2), -0.15f) * hs;
                    p.Rod(a, b, 0.08f * hs, k.Base.Lightened(0.1f));
                    Eye(p, k, b, 0.28f * hs);
                }
                break;
            case "lantern":
                p.Add(Shape.Sphere, new Vector3(0.8f, 0.65f, 0.85f) * hs, new Vector3(0, 0, -0.45f) * hs, k.Base, 0.6f);
                p.Add(Shape.Sphere, new Vector3(0.78f, 0.34f, 0.92f) * hs, new Vector3(0, -0.3f, -0.55f) * hs, k.Belly);
                for (int i = 0; i < 6; i++)
                {
                    float x = (i - 2.5f) * 0.12f;
                    p.Add(Shape.Cone, new Vector3(0.06f, 0.16f, 0) * hs, new Vector3(x, -0.12f, -0.92f + MathF.Abs(x) * 0.6f) * hs, k.Bone);
                }
                Vector3 l0 = new Vector3(0, 0.3f, -0.45f) * hs, l1 = l0 + new Vector3(0, 0.45f, -0.3f) * hs, l2 = l1 + new Vector3(0, -0.05f, -0.5f) * hs;
                p.Rod(l0, l1, 0.06f * hs, k.Base);
                p.Rod(l1, l2, 0.05f * hs, k.Base);
                p.Add(Shape.Sphere, Vector3.One * 0.3f * hs, l2 + Vector3.Down * 0.08f * hs, k.Glow, 0f, 1f);
                Eyes(p, k, hs, 0.3f, 0.12f, -0.72f, 0.12f);
                break;
            case "maw":
                p.Add(Shape.Cylinder, new Vector3(0.8f, 1.0f, 0.95f) * hs, new Vector3(0, 0, -0.5f) * hs, k.Base, 1f, 0f, Pitch(-90), 10);
                p.Add(Shape.Cylinder, new Vector3(0.82f, 0.05f, 0.82f) * hs, new Vector3(0, 0, -1.0f) * hs, new Color(0.35f, 0.03f, 0.08f), 0f, 0f, Pitch(-90), 10);
                for (int i = 0; i < 9; i++)
                {
                    float a = i * MathF.Tau / 9f;
                    Vector3 at = new Vector3(MathF.Cos(a) * 0.36f, MathF.Sin(a) * 0.36f, -1.0f) * hs;
                    Vector3 tip = at + new Vector3(-MathF.Cos(a) * 0.18f, -MathF.Sin(a) * 0.18f, -0.12f) * hs;
                    p.Rod(at, tip, 0.1f * hs, k.Bone, 0, 0, cone: true);
                }
                break;
            case "dome":
                p.Add(Shape.Sphere, new Vector3(1.0f, 0.9f, 1.15f) * hs, new Vector3(0, 0.05f, -0.5f) * hs, k.Base.Lightened(0.08f), 0.3f);
                p.Add(Shape.Box, new Vector3(0.6f, 0.05f, 0.08f) * hs, new Vector3(0, 0.05f, -1.06f) * hs,
                    k.EyeGlow > 0 ? k.Glow : k.Pupil, 0f, k.EyeGlow);
                break;
            default: // "snout", and each of a twin pair
                p.Add(Shape.Sphere, new Vector3(0.8f, 0.72f, 0.95f) * hs, new Vector3(0, 0.05f, -0.45f) * hs, k.Base, 0.6f);
                p.Add(Shape.Sphere, new Vector3(0.52f, 0.45f, 0.65f) * hs, new Vector3(0, -0.1f, -0.95f) * hs, k.Belly);
                p.Add(Shape.Sphere, new Vector3(0.22f, 0.14f, 0.12f) * hs, new Vector3(0, -0.02f, -1.27f) * hs, k.Pupil);
                Eyes(p, k, hs, 0.3f, 0.18f, -0.68f, 0.2f);
                for (int s = -1; s <= 1; s += 2)
                    p.Add(Shape.Cone, new Vector3(0.2f, 0.34f, 0) * hs, new Vector3(s * 0.26f, 0.42f, -0.28f) * hs, k.Base.Darkened(0.15f), 0.3f, 0f,
                        new Basis(Vector3.Back, -s * 0.45f));
                break;
        }
    }

    public static void Horns(Piece p, string kind, float hs, Paint k, Rng rng)
    {
        switch (kind)
        {
            case "pair":
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 a = new Vector3(s * 0.22f, 0.36f, -0.36f) * hs;
                    p.Rod(a, a + new Vector3(s * 0.32f, 0.62f, 0.3f) * hs, 0.18f * hs, k.Bone, 0, 0, cone: true);
                }
                break;
            case "ram":
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 a = new Vector3(s * 0.22f, 0.34f, -0.34f) * hs;
                    Vector3 b = a + new Vector3(s * 0.18f, 0.25f, 0.3f) * hs;
                    Vector3 c = b + new Vector3(s * 0.25f, -0.15f, 0.2f) * hs;
                    Vector3 d = c + new Vector3(s * 0.05f, -0.3f, -0.18f) * hs;
                    Color horn = k.Bone.Darkened(0.2f);
                    p.Rod(a, b, 0.18f * hs, horn);
                    p.Rod(b, c, 0.15f * hs, horn);
                    p.Rod(c, d, 0.11f * hs, horn, 0, 0, cone: true);
                }
                break;
            case "antlers":
                for (int s = -1; s <= 1; s += 2)
                {
                    Color c = k.Bone.Lerp(new Color(0.45f, 0.32f, 0.2f), 0.4f);
                    Vector3 a = new Vector3(s * 0.2f, 0.38f, -0.3f) * hs;
                    Vector3 b = a + new Vector3(s * 0.35f, 0.55f, 0.2f) * hs;
                    Vector3 t = b + new Vector3(s * 0.2f, 0.45f, 0.15f) * hs;
                    p.Rod(a, b, 0.08f * hs, c);
                    p.Rod(b, t, 0.06f * hs, c, 0, 0, cone: true);
                    p.Rod(Vector3Lerp(a, b, 0.5f), Vector3Lerp(a, b, 0.5f) + new Vector3(s * 0.05f, 0.35f, -0.3f) * hs, 0.05f * hs, c, 0, 0, cone: true);
                    p.Rod(b, b + new Vector3(s * 0.35f, 0.2f, -0.15f) * hs, 0.05f * hs, c, 0, 0, cone: true);
                }
                break;
            case "spike":
                p.Rod(new Vector3(0, 0.12f, -1.0f) * hs, new Vector3(0, 0.6f, -1.45f) * hs, 0.22f * hs, k.Bone, 0, 0, cone: true);
                break;
            case "antennae":
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 a = new Vector3(s * 0.15f, 0.32f, -0.6f) * hs;
                    Vector3 b = a + new Vector3(s * 0.4f, 1.1f, -0.5f) * hs;
                    p.Rod(a, b, 0.045f * hs, k.Dark);
                    p.Add(Shape.Sphere, Vector3.One * 0.13f * hs, b, k.EyeGlow > 0 ? k.Glow : k.Accent, 0f, k.EyeGlow);
                }
                break;
            case "frill":
                p.Add(Shape.Sphere, new Vector3(2.3f, 1.7f, 0.1f) * hs, new Vector3(0, 0.3f, -0.1f) * hs, k.Accent, 1f, 0f, Pitch(25));
                for (int i = 0; i < 7; i++)
                {
                    float a = MathF.PI * (0.08f + 0.84f * i / 6f);
                    Vector3 at = new Vector3(MathF.Cos(a) * 1.05f, 0.3f + MathF.Sin(a) * 0.75f, 0.05f) * hs;
                    p.Rod(at, at + new Vector3(MathF.Cos(a) * 0.3f, MathF.Sin(a) * 0.3f, 0.1f) * hs, 0.09f * hs, k.Bone, 0, 0, cone: true);
                }
                break;
            case "crown":
                for (int i = 0; i < 7; i++)
                {
                    float a = i * MathF.Tau / 7f;
                    Vector3 at = new Vector3(MathF.Cos(a) * 0.28f, 0.33f, -0.42f + MathF.Sin(a) * 0.28f) * hs;
                    p.Rod(at, at + new Vector3(MathF.Cos(a) * 0.15f, 0.42f, MathF.Sin(a) * 0.15f) * hs, 0.12f * hs,
                        k.Accent.Lightened(0.15f), 0, k.GlowStrength * 0.6f, cone: true);
                }
                break;
        }
    }

    // ---- Backs ------------------------------------------------------------------------

    /// <summary>Height of the body ellipsoid's top at z (body space).</summary>
    public static float Top(float z, float H, float L)
    {
        float t = Math.Clamp(2f * z / L, -0.98f, 0.98f);
        return 0.5f * H * MathF.Sqrt(1f - t * t);
    }

    public static void Back(Piece p, string kind, float W, float H, float L, Paint k, Rng rng)
    {
        switch (kind)
        {
            case "plates":
            {
                int n = Math.Clamp((int)MathF.Round(L / (W * 0.45f)), 4, 8);
                for (int i = 0; i < n; i++)
                {
                    float z = Mathf.Lerp(-0.38f, 0.38f, i / (float)(n - 1)) * L;
                    float taper = 1f - MathF.Abs(z / L) * 0.8f;
                    p.Add(Shape.Sphere, new Vector3(W * 0.98f * taper, H * 0.22f, L / n * 1.55f), new Vector3(0, Top(z, H, L) - H * 0.02f, z),
                        i % 2 == 0 ? k.Accent : k.Accent.Darkened(0.15f), 0.2f, 0f, Pitch(-z / L * 40f));
                }
                break;
            }
            case "shell":
                p.Add(Shape.Sphere, new Vector3(W * 1.14f, H * 0.95f, L * 0.92f), new Vector3(0, H * 0.2f, L * 0.02f), k.Accent.Lerp(k.Base, 0.35f), 1f);
                p.Add(Shape.Sphere, new Vector3(W * 1.2f, H * 0.14f, L * 0.97f), new Vector3(0, -H * 0.02f, L * 0.02f), k.Accent.Darkened(0.4f));
                break;
            case "spines":
                for (int i = 0; i < 8; i++)
                {
                    float z = Mathf.Lerp(-0.35f, 0.42f, i / 7f) * L;
                    float h = H * 0.6f * (1f - 0.55f * i / 7f);
                    Vector3 a = new(0, Top(z, H, L) - H * 0.05f, z);
                    p.Rod(a, a + new Vector3(0, 1f, 0.5f).Normalized() * h, W * 0.13f, k.Bone.Lerp(k.Dark, 0.3f), 0, 0, cone: true);
                }
                break;
            case "sail":
                p.Add(Shape.Sphere, new Vector3(W * 0.06f, H * 1.3f, L * 0.75f), new Vector3(0, H * 0.85f, L * 0.05f), k.Accent, 1f);
                for (int i = 0; i < 5; i++)
                {
                    float z = Mathf.Lerp(-0.25f, 0.33f, i / 4f) * L;
                    float top = H * 0.85f + H * 0.62f * MathF.Sqrt(Math.Max(0f, 1f - MathF.Pow((z - L * 0.05f) / (L * 0.375f), 2f)));
                    p.Rod(new Vector3(0, Top(z, H, L), z), new Vector3(0, top, z + L * 0.03f), W * 0.06f, k.Bone);
                }
                break;
            case "crystals":
            {
                int n = rng.Int(6, 10);
                Color crystal = (k.GlowStrength > 0f ? k.Glow : k.Accent).Lerp(Colors.White, 0.25f);
                for (int i = 0; i < n; i++)
                {
                    float z = rng.Range(-0.32f, 0.36f) * L, x = rng.Range(-0.28f, 0.28f) * W;
                    float h = H * rng.Range(0.5f, 1.15f), d = W * rng.Range(0.12f, 0.22f);
                    var tilt = new Basis(Vector3.Back, rng.Range(-0.45f, 0.45f) - x / W) * new Basis(Vector3.Right, rng.Range(-0.35f, 0.25f));
                    Vector3 at = new(x, Top(z, H, L) - H * 0.1f, z);
                    p.Add(Shape.Cylinder, new Vector3(d, h * 0.6f, d), at + tilt * Vector3.Up * h * 0.3f, crystal, 0f, 0.85f, tilt, 6);
                    p.Add(Shape.Cone, new Vector3(d, h * 0.4f, 0f), at + tilt * Vector3.Up * h * 0.8f, crystal.Lightened(0.2f), 0f, 0.95f, tilt, 6);
                }
                break;
            }
            case "bubbles":
            {
                int n = rng.Int(6, 11);
                for (int i = 0; i < n; i++)
                {
                    float z = rng.Range(-0.35f, 0.35f) * L, x = rng.Range(-0.3f, 0.3f) * W, d = W * rng.Range(0.18f, 0.42f);
                    p.Add(Shape.Sphere, Vector3.One * d, new Vector3(x, Top(z, H, L) + d * 0.15f, z), k.Belly.Lerp(k.Glow, 0.35f), 0f, 0.45f);
                }
                break;
            }
            case "vents":
            {
                int n = rng.Int(3, 5);
                for (int i = 0; i < n; i++)
                {
                    float z = Mathf.Lerp(-0.25f, 0.3f, n == 1 ? 0.5f : i / (float)(n - 1)) * L, x = rng.Range(-0.2f, 0.2f) * W;
                    float h = H * rng.Range(0.35f, 0.7f), d = W * rng.Range(0.14f, 0.2f);
                    Vector3 at = new(x, Top(z, H, L), z);
                    p.Add(Shape.Cylinder, new Vector3(d * 1.2f, h, d), at + Vector3.Up * h * 0.4f, k.Dark, 0.3f, 0f, null, 8);
                    p.Add(Shape.Cylinder, new Vector3(d * 0.8f, h * 0.06f, d * 0.8f), at + Vector3.Up * h * 0.91f, k.Accent.Lightened(0.3f), 0f, 0.9f, null, 8);
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
                    Vector3 at = new Vector3(MathF.Sin(-a), 0, MathF.Cos(-a)) * 0;
                    p.Add(Shape.Sphere, new Vector3(W * 0.36f, H * 0.05f, W * 1.05f), at + new Vector3(0, H * 0.45f, 0) + leaf * new Vector3(0, 0, -W * 0.5f),
                        i % 2 == 0 ? k.Base : k.Base.Lerp(k.Accent, 0.4f), 1f, 0f, leaf);
                }
                p.Add(Shape.Sphere, Vector3.One * W * 0.32f, new Vector3(0, H * 0.62f, 0), k.Accent, 0f, k.GlowStrength > 0 ? 0.8f : 0f);
                for (int i = 0; i < 5; i++)
                {
                    float a = i * MathF.Tau / 5f;
                    p.Add(Shape.Sphere, new Vector3(W * 0.2f, W * 0.08f, W * 0.2f), new Vector3(MathF.Cos(a) * W * 0.2f, H * 0.66f, MathF.Sin(a) * W * 0.2f),
                        k.Accent.Lightened(0.3f), 0f, k.GlowStrength * 0.5f);
                }
                break;
            }
        }
    }

    /// <summary>The Hollow Bloom's growths: glowing pustules, bone spurs and extra eyes.</summary>
    public static void Corruption(Piece p, float W, float H, float L, Paint k, Rng rng)
    {
        int pustules = rng.Int(4, 8);
        for (int i = 0; i < pustules; i++)
        {
            float z = rng.Range(-0.4f, 0.4f) * L, x = rng.Range(-0.35f, 0.35f) * W, d = W * rng.Range(0.1f, 0.22f);
            p.Add(Shape.Sphere, Vector3.One * d, new Vector3(x, Top(z, H, L) * rng.Range(0.6f, 1f), z), k.Veins, 0f, 1f);
        }
        for (int i = 0; i < 3; i++)
        {
            float z = rng.Range(-0.3f, 0.3f) * L;
            int s = i % 2 == 0 ? 1 : -1;
            Vector3 a = new(s * W * 0.4f, rng.Range(-0.1f, 0.25f) * H, z);
            p.Rod(a, a + new Vector3(s * 0.8f, rng.Range(0.2f, 0.8f), rng.Range(-0.4f, 0.4f)).Normalized() * H * 0.55f, W * 0.1f, k.Bone, 0, 0, cone: true);
        }
        int eyes = rng.Int(3, 6);
        for (int i = 0; i < eyes; i++)
            p.Add(Shape.Sphere, Vector3.One * W * 0.08f, new Vector3(W * 0.42f * (i % 2 == 0 ? 1 : -1), H * rng.Range(-0.05f, 0.25f), -L * rng.Range(0.1f, 0.38f)), k.Veins, 0f, 1f);
    }

    // ---- Tail tips --------------------------------------------------------------------

    public static void TailTip(Piece p, string kind, float segLen, float W, Paint k)
    {
        float y = segLen;
        switch (kind)
        {
            case "plume":
                for (int i = 0; i < 3; i++)
                    p.Add(Shape.Sphere, new Vector3(0.45f, 0.7f, 0.45f) * W, new Vector3((i - 1) * 0.18f * W, y + W * 0.3f, 0), k.Accent, 0f, k.GlowStrength * 0.4f);
                break;
            case "club":
                p.Add(Shape.Sphere, Vector3.One * W * 0.5f, new Vector3(0, y + W * 0.1f, 0), k.Bone.Lerp(k.Base, 0.4f), 0.4f);
                for (int i = 0; i < 4; i++)
                {
                    float a = i * MathF.Tau / 4f;
                    Vector3 at = new(MathF.Cos(a) * W * 0.22f, y + W * 0.1f, MathF.Sin(a) * W * 0.22f);
                    p.Rod(at, at + new Vector3(MathF.Cos(a), 0.2f, MathF.Sin(a)) * W * 0.22f, W * 0.1f, k.Bone, 0, 0, cone: true);
                }
                break;
            case "fan":
                p.Add(Shape.Sphere, new Vector3(1.5f, 1.1f, 0.07f) * W, new Vector3(0, y + W * 0.4f, 0), k.Accent, 1f, k.GlowStrength * 0.3f, new Basis(Vector3.Right, -1.2f));
                break;
            case "stinger":
                p.Rod(new Vector3(0, y - W * 0.05f, 0), new Vector3(0, y + W * 0.6f, -W * 0.1f), W * 0.2f, k.Dark, 0, 0, cone: true);
                if (k.GlowStrength > 0f) p.Add(Shape.Sphere, Vector3.One * W * 0.08f, new Vector3(0, y + W * 0.55f, -W * 0.09f), k.Glow, 0f, 1f);
                break;
        }
    }

    // ---- Small helpers ----------------------------------------------------------------

    static void Eyes(Piece p, Paint k, float hs, float x, float y, float z, float size)
    {
        for (int s = -1; s <= 1; s += 2) Eye(p, k, new Vector3(s * x, y, z) * hs, size * hs, s);
    }

    /// <summary>An eye: a lamp for glowing species, else a pale ball with a dark pupil looking out.</summary>
    public static void Eye(Piece p, Paint k, Vector3 at, float size, int side = 0)
    {
        p.Add(Shape.Sphere, Vector3.One * size, at, k.Sclera, 0f, k.EyeGlow);
        if (k.EyeGlow > 0f) return;
        Vector3 outward = new Vector3(side * 0.5f, 0.1f, -1f).Normalized();
        p.Add(Shape.Sphere, Vector3.One * size * 0.55f, at + outward * size * 0.3f, k.Pupil);
    }

    public static Basis Pitch(float degrees) => new(Vector3.Right, Mathf.DegToRad(degrees));

    static Vector3 Vector3Lerp(Vector3 a, Vector3 b, float t) => a + (b - a) * t;
}
