using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>
/// The parts of an alien folk, at the human body's sockets (docs/alien-peoples.md 1.2): ears,
/// face, eyes, crown, back, hands, tail. Each is a few primitives added to an existing piece, so a
/// body stays one skinned mesh; only a tail adds bones. Sizes are in the head radius R and the
/// body unit u, so every part scales with every body. Parts are tidy and symmetrical: mutation is
/// the lumpy one. A Husk keeps its folk's parts, greyed, and the worms come out of whatever
/// openings that face has (<see cref="Openings"/>).
/// </summary>
public static class FolkBody
{
    /// <summary>The openings a Husk's worms use: the mouth (where, how wide, which way it opens, how far along it runs), the nostrils, the ear holes.</summary>
    public struct Openings
    {
        public Vector3 Mouth, MouthAlong, Nose;
        public float Gape;
        public bool EarHoles;
    }

    static Folk Of(Figure f) => f.Folk ?? Plain;
    static readonly Folk Plain = new();

    /// <summary>How far the face stands forward of a human one's, in R (a respirator or a seize keeps that gap).</summary>
    public static float FaceLength(Figure f) => Of(f).Mouth switch
    {
        "muzzle" => 1.4f, "snout" => 0.75f + 1.3f * Of(f).MouthSize, "beak" => 0.85f + 0.85f * Of(f).MouthSize, "mandibles" => 1.4f, _ => 1.05f,
    };

    /// <summary>How big their parts are for their sex and age: one sex carries the big horns, children buds.</summary>
    static float Grown(Figure f)
    {
        Folk k = Of(f);
        float sex = k.Dimorphism > 1f ? (f.Sex == Sex.Male ? 1f : 0.65f) : 1f;
        return sex * (f.Stage == LifeStage.Child ? 0.5f : 1f);
    }

    /// <summary>Their own second colour: faded with age, grey on a Husk.</summary>
    static Color Mark(Figure f)
    {
        Color m = Of(f).Mark;
        if (f.Stage == LifeStage.Elder) m = m.Lerp(new Color(0.6f, 0.58f, 0.55f), 0.35f);
        if (f.Husk) m = m.Lerp(new Color(0.5f, 0.48f, 0.45f), 0.55f);
        return m;
    }

    /// <summary>Points: hands, ears and tail tip darker than the rest.</summary>
    public static Color Pointed(Figure f, Color c) => c.Darkened(0.45f * Of(f).Points);

    public static bool Chitin(Figure f) => Of(f).Covering == "chitin";
    public static float SkinMat(Figure f) => Chitin(f) ? Mat.Carapace : Mat.Skin;

    /// <summary>A flat ellipsoid from root along dir: an ear, a feather, a plate. Thick across side, wide across the third axis.</summary>
    static void Leaf(Piece p, Vector3 root, Vector3 dir, Vector3 side, float length, float width, float thick, Color c, float mat = Mat.Skin, int sides = 7, float glow = 0f)
    {
        dir = dir.Normalized();
        p.Add(Shape.Sphere, new Vector3(thick, length, width), root + dir * length * 0.45f, c, 0f, glow, Geo.AlongY(dir, side), sides, mat);
    }

    // ---- Ears ---------------------------------------------------------------------------------------

    /// <summary>One ear (s = -1 left, 1 right).</summary>
    public static void Ear(Piece p, Figure f, Vector3 c, float s, Color skin)
    {
        Folk k = Of(f);
        float R = f.HeadRadius, e = k.EarSize;
        bool husk = f.Husk;
        Color ear = Pointed(f, skin.Darkened(0.06f)), inner = skin.Lerp(Mark(f), 0.3f).Lightened(0.08f);
        Vector3 side = new(s, 0f, 0f);
        Vector3 root = c + new Vector3(s * R * 0.82f, R * 0.06f, R * 0.08f);
        // A Husk's ears hang: droop and tear.
        float droop = husk ? 0.6f : 0f;
        switch (k.Ears)
        {
            case "round":
                Leaf(p, c + new Vector3(s * R * 0.62f, R * 0.62f, R * 0.12f), new Vector3(s * 0.55f, 1f - droop, 0.1f), side, R * 0.62f * e, R * 0.56f * e, R * 0.14f, ear);
                break;
            case "leaf":
                Leaf(p, root, new Vector3(s * 0.75f, 0.85f - droop * 1.2f, 0.45f), side, R * 1.15f * e, R * 0.42f, R * 0.12f, ear);
                break;
            case "tall":
                Leaf(p, c + new Vector3(s * R * 0.5f, R * 0.72f, R * 0.12f), new Vector3(s * 0.22f, 1f - droop * 1.5f, 0.18f), side, R * 2.3f * e, R * 0.6f, R * 0.13f, ear);
                Leaf(p, c + new Vector3(s * R * 0.52f, R * 0.85f, R * 0.06f), new Vector3(s * 0.22f, 1f - droop * 1.5f, 0.18f), side, R * 1.8f * e, R * 0.36f, R * 0.06f, inner);
                break;
            case "fan":
                Leaf(p, root, new Vector3(s * 0.8f, 0.5f - droop, 0.2f), side, R * 1.5f * e, R * 1.35f * e, R * 0.09f, ear);
                Leaf(p, root + new Vector3(0f, 0f, -R * 0.04f), new Vector3(s * 0.8f, 0.5f - droop, 0.2f), side, R * 1.1f * e, R * 0.9f * e, R * 0.05f, inner);
                break;
            case "droop":
                Leaf(p, c + new Vector3(s * R * 0.78f, R * 0.32f, R * 0.12f), new Vector3(s * 0.3f, -1f, 0.15f), side, R * 1.2f * e, R * 0.55f, R * 0.12f, ear);
                break;
            case "fin":
                Leaf(p, root, new Vector3(s * 0.6f, 0.4f - droop, 0.7f), side, R * 0.95f * e, R * 0.8f * e, R * 0.07f, Mark(f), Mat.Membrane);
                for (int i = 0; i < 3; i++)
                {
                    Vector3 d = new Vector3(s * 0.6f, 0.75f - i * 0.4f - droop, 0.5f + i * 0.2f).Normalized();
                    p.Taper(root, root + d * R * 0.9f * e, R * 0.04f, R * 0.012f, skin.Darkened(0.15f), 0f, 0f, Mat.Horn, 5);
                }
                break;
            case "none":
                p.Add(Shape.Sphere, Vector3.One * R * 0.16f, root + new Vector3(s * R * 0.02f, 0f, 0f), skin.Darkened(0.45f), 0f, 0f, null, 5);
                break;
            default:
                p.Add(Shape.Sphere, new Vector3(R * 0.2f, R * 0.44f, R * 0.3f) * MathF.Max(0.6f, e * (k.Human ? 1f : 1f)), c + new Vector3(s * R * 0.86f, R * 0.02f, R * 0.08f), ear, 0f, 0f, null, 6);
                break;
        }
    }

    // ---- Face ---------------------------------------------------------------------------------------

    /// <summary>
    /// The face below the eyes: a human nose and mouth, a muzzle, a long snout with a lower jaw, a
    /// beak in two halves, or mandibles; tusks from the jaw. Returns where the worms come out.
    /// </summary>
    public static Openings Face(Piece p, Figure f, Vector3 c, Color skin)
    {
        Folk k = Of(f);
        float R = f.HeadRadius, L = k.MouthSize, drop = f.JawDrop;
        var o = new Openings { EarHoles = k.Ears != "none" || !(k.Crown == "antennae" || Chitin(f)) };
        Color nose = Pointed(f, skin.Darkened(0.12f));
        Color dark = new(0.06f, 0.04f, 0.04f);
        switch (k.Mouth)
        {
            case "muzzle":
                p.Add(Shape.Sphere, new Vector3(R * 0.78f, R * 0.52f, R * 0.66f) * L, c + new Vector3(0f, -R * 0.3f, -R * 0.86f), skin.Lightened(0.04f), 0f, 0f, null, 8, SkinMat(f));
                p.Add(Shape.Sphere, new Vector3(R * 0.3f, R * 0.2f, R * 0.2f), c + new Vector3(0f, -R * 0.12f, -R * (0.86f + 0.33f * L)), nose.Darkened(0.4f), 0f, 0f, null, 6);
                o.Mouth = c + new Vector3(0f, -R * (0.52f + drop * 0.4f), -R * (0.8f + 0.3f * L));
                o.Nose = c + new Vector3(0f, -R * 0.16f, -R * (0.9f + 0.33f * L));
                o.MouthAlong = Vector3.Right;
                o.Gape = R * 0.5f;
                if (!f.Husk) p.Add(Shape.Box, new Vector3(R * 0.5f, R * 0.05f, R * 0.04f), o.Mouth, skin.Darkened(0.45f));
                break;
            case "snout":
            {
                float len = 1.3f * L;
                Vector3 upper = c + new Vector3(0f, -R * 0.22f, -R * (0.72f + len * 0.5f));
                p.Add(Shape.Sphere, new Vector3(R * 0.62f, R * 0.5f, R * (len + 0.2f)), upper, skin, 0f, 0f, null, 8, SkinMat(f));
                // The lower jaw hinges at the jaw sphere: a Husk's hangs open.
                Basis hinge = new(Vector3.Right, -0.35f * drop - (f.Husk ? 0.15f : 0f));
                Vector3 pivot = c + new Vector3(0f, -R * 0.5f, -R * 0.5f);
                Vector3 jaw = pivot + hinge * new Vector3(0f, -R * 0.06f, -R * len * 0.5f);
                p.Add(Shape.Sphere, new Vector3(R * 0.5f, R * 0.28f, R * len * 0.92f), jaw, skin.Darkened(0.05f), 0f, 0f, hinge, 8, SkinMat(f));
                p.Add(Shape.Sphere, new Vector3(R * 0.34f, R * 0.24f, R * 0.18f), c + new Vector3(0f, -R * 0.1f, -R * (0.74f + len)), nose.Darkened(0.4f), 0f, 0f, null, 6);
                o.Mouth = (upper + jaw) * 0.5f + new Vector3(0f, -R * 0.08f, 0f);
                o.MouthAlong = Vector3.Forward;
                o.Gape = R * len * 0.8f;
                o.Nose = c + new Vector3(0f, -R * 0.12f, -R * (0.8f + len));
                if (!f.Husk) p.Add(Shape.Box, new Vector3(R * 0.06f, R * 0.04f, R * len * 0.8f), o.Mouth + new Vector3(0f, 0f, 0f), skin.Darkened(0.5f));
                break;
            }
            case "beak":
            {
                float len = 0.85f * L;
                Color horn = new Color(0.72f, 0.6f, 0.3f).Lerp(Mark(f), 0.35f).Lerp(new Color(0.5f, 0.48f, 0.44f), f.Husk ? 0.5f : 0f);
                // The upper half hooks over; a Husk's lower half swings down, the beak chipped.
                p.Horn(horn, R * 0.36f, Mat.Horn, c + new Vector3(0f, -R * 0.05f, -R * 0.78f), c + new Vector3(0f, -R * 0.12f, -R * (0.85f + len * 0.55f)),
                    c + new Vector3(0f, -R * 0.3f, -R * (0.85f + len * (f.Husk ? 0.85f : 1f))), c + new Vector3(0f, -R * 0.52f, -R * (0.8f + len * (f.Husk ? 0.85f : 1f))));
                float swing = f.Husk ? 0.55f + 0.5f * drop : 0f;
                Vector3 low0 = c + new Vector3(0f, -R * 0.42f, -R * 0.78f);
                Vector3 Low(float along, float y) => low0 + new Basis(Vector3.Right, -swing) * new Vector3(0f, -R * y, -R * len * along);
                p.Horn(horn.Darkened(0.12f), R * 0.25f, Mat.Horn, low0, Low(0.55f, 0.06f), Low(0.85f, 0.04f));
                for (int s = -1; s <= 1; s += 2) p.Add(Shape.Sphere, Vector3.One * R * 0.08f, c + new Vector3(s * R * 0.1f, -R * 0.02f, -R * (0.9f + len * 0.2f)), dark, 0f, 0f, null, 5);
                o.Mouth = (c + new Vector3(0f, -R * 0.3f, -R * (0.9f + len * 0.35f))).Lerp(Low(0.4f, 0f), 0.4f);
                o.MouthAlong = Vector3.Forward;
                o.Gape = R * len * 0.6f;
                o.Nose = c + new Vector3(0f, -R * 0.02f, -R * (0.92f + len * 0.2f));
                break;
            }
            case "mandibles":
            {
                Color plate = Mark(f).Darkened(0.35f).Lerp(skin, 0.3f);
                // Mandibles at the mouth's corners, curving in; a Husk's splay apart.
                float splay = f.Husk ? 1.7f : 1f;
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 a = c + new Vector3(s * R * 0.32f, -R * 0.5f, -R * 0.74f);
                    p.Horn(plate, R * 0.13f, Mat.Carapace, a, a + new Vector3(s * R * 0.24f * splay, -R * 0.2f, -R * 0.26f * L), a + new Vector3(s * R * 0.12f * splay, -R * 0.32f, -R * 0.56f * L),
                        a + new Vector3(-s * R * (0.16f - 0.18f * (splay - 1f)), -R * 0.3f, -R * 0.66f * L));
                    Vector3 palp = c + new Vector3(s * R * 0.14f, -R * 0.62f, -R * 0.82f);
                    p.Taper(palp, palp + new Vector3(s * R * 0.08f, -R * 0.42f, -R * 0.1f), R * 0.05f, R * 0.025f, plate.Lightened(0.1f), 0f, 0f, Mat.Carapace, 5);
                }
                p.Add(Shape.Sphere, new Vector3(R * 0.42f, R * 0.18f, R * 0.12f), c + new Vector3(0f, -R * 0.6f, -R * 0.84f), dark, 0f, 0f, null, 6);
                o.Mouth = c + new Vector3(0f, -R * 0.64f, -R * 0.9f);
                o.MouthAlong = Vector3.Right;
                o.Gape = R * 0.32f * splay;
                o.Nose = c + new Vector3(0f, -R * 0.3f, -R * 0.95f);
                break;
            }
            default:
                p.Taper(c + new Vector3(0f, R * 0.12f, -R * 0.92f), c + new Vector3(0f, -R * 0.2f, -R * 1.05f), R * 0.1f, R * 0.13f, skin.Darkened(0.04f), 0f, 0f, Mat.Skin, 6);
                o.Mouth = c + new Vector3(0f, -R * (0.62f + drop * 0.55f), -R * (0.86f - drop * 0.08f));
                o.MouthAlong = Vector3.Right;
                o.Gape = R * 0.42f;
                o.Nose = c + new Vector3(0f, -R * 0.18f, -R * 1.04f);
                break;
        }
        if (k.Tusks)
        {
            float fwd = k.Mouth == "snout" ? R * 0.5f : 0f;
            for (int s = -1; s <= 1; s += 2)
            {
                // A Husk's tilt out, one broken.
                float tilt = f.Husk ? 0.25f : 0f;
                bool broken = f.Husk && s > 0;
                Vector3 a = c + new Vector3(s * R * 0.3f, -R * 0.62f, -R * 0.74f - fwd);
                var pts = new List<Vector3> { a, a + new Vector3(s * R * (0.1f + tilt), R * 0.02f, -R * 0.22f) };
                if (!broken) pts.Add(a + new Vector3(s * R * (0.16f + tilt * 2f), R * 0.34f * Grown(f), -R * 0.36f));
                p.Horn(new Color(0.86f, 0.82f, 0.7f), R * 0.1f, Mat.Horn, pts.ToArray());
            }
        }
        return o;
    }

    // ---- Eyes ---------------------------------------------------------------------------------------

    /// <summary>Where the eyes are and how big, by the folk's plan: two, three, four, two compound, or one band.</summary>
    public static List<(Vector3 At, float Size, int Side)> EyeSpots(Figure f, Vector3 c)
    {
        Folk k = Of(f);
        float R = f.HeadRadius, e = k.EyeSize;
        var l = new List<(Vector3, float, int)>();
        float wide = k.Mouth is "snout" or "beak" ? 0.06f : 0f;
        switch (k.Eyes)
        {
            case "compound":
                for (int s = -1; s <= 1; s += 2) l.Add((c + new Vector3(s * R * 0.5f, R * 0.12f, -R * 0.66f), R * 0.56f * e, s));
                break;
            case "band":
                l.Add((c + new Vector3(0f, R * 0.14f, -R * 0.84f), R * 0.2f * e, 0));
                break;
            case "four":
                for (int s = -1; s <= 1; s += 2)
                {
                    l.Add((c + new Vector3(s * R * 0.36f, R * 0.26f, -R * 0.8f), R * 0.14f * e, s));
                    l.Add((c + new Vector3(s * R * 0.3f, R * 0.0f, -R * 0.84f), R * 0.11f * e, s));
                }
                break;
            default:
                for (int s = -1; s <= 1; s += 2) l.Add((c + new Vector3(s * R * (0.36f + wide), R * 0.14f, -R * (0.82f - wide)), R * 0.17f * e, s));
                if (k.Eyes == "three") l.Add((c + new Vector3(0f, R * 0.44f, -R * 0.86f), R * 0.14f * e, 0));
                break;
        }
        return l;
    }

    /// <summary>One eye of a folk: its own colour, a pupil; compound eyes a big faceted dome; a band a dark visor with bright dots.</summary>
    public static void Eye(Piece p, Figure f, Vector3 at, float size, int side)
    {
        Folk k = Of(f);
        Color colour = f.Husk ? new Color(0.08f, 0.07f, 0.06f) : k.EyeColour;
        float glow = !f.Husk && k.DarkVision > 0.5f ? 0.35f : 0f;
        switch (k.Eyes)
        {
            case "compound":
                p.Add(Shape.Sphere, new Vector3(size * 0.9f, size, size * 0.85f), at, colour.Lerp(new Color(0.08f, 0.06f, 0.08f), 0.5f), 0f, glow, null, 6, Mat.Eye);
                break;
            case "band":
                p.Add(Shape.Sphere, new Vector3(size * 7f, size * 1.2f, size * 1.4f), at, new Color(0.05f, 0.05f, 0.06f), 0f, 0f, null, 8, Mat.Eye);
                if (!f.Husk)
                    for (int i = -1; i <= 1; i++) p.Add(Shape.Sphere, Vector3.One * size * 0.7f, at + new Vector3(i * size * 2.2f, 0f, -size * 0.45f), k.EyeShine, 0f, 0.8f, null, 5, Mat.Eye);
                break;
            default:
                p.Add(Shape.Sphere, Vector3.One * size, at, colour, 0f, glow, null, 8, Mat.Eye);
                if (colour.V > 0.2f && !f.Husk) p.Add(Shape.Sphere, new Vector3(size * 0.35f, size * 0.6f, size * 0.2f), at + new Vector3(0f, 0f, -size * 0.42f), new Color(0.03f, 0.02f, 0.02f), 0f, 0f, null, 5, Mat.Eye);
                break;
        }
    }

    // ---- Crown --------------------------------------------------------------------------------------

    /// <summary>What stands on the head: horns, antlers, antennae (the animal part library's, sized to the head), or a crest, quills, a plume or a keel.</summary>
    public static void Crown(Piece p, Figure f, Vector3 c, Color skin)
    {
        Folk k = Of(f);
        if (k.Crown == "" || f.Has("suit_helmet")) return;
        float R = f.HeadRadius, size = k.CrownSize * Grown(f);
        Color mark = Mark(f);
        switch (k.Crown)
        {
            case "pair":
            case "ram":
            case "brow":
            case "antlers":
            case "crown":
            case "antennae":
            {
                // The animal part library, in head space at its own scale: rooted on top of the skull.
                float hs = 2f * R * size;
                var paint = new Paint(new Species { Colors = { Base = skin, Accent = mark, Belly = skin } });
                var part = new Piece("crown");
                Parts.Horns(part, k.Crown, hs, paint, new Rng((ulong)(f.Height * 1000f)), new HeadShape(0.55f, 0.48f, 1f));
                Vector3 origin = c + new Vector3(0f, R * 0.95f - 0.42f * hs, -R * 0.1f + 0.2f * hs);
                foreach (Prim prim in part.Prims)
                {
                    Prim q = prim;
                    // A Husk's antennae and horns hang slack about their roots.
                    if (f.Husk && k.Crown == "antennae") q.Local = new Transform3D(new Basis(Vector3.Right, -0.9f), Vector3.Zero) * q.Local;
                    q.Local = new Transform3D(q.Local.Basis, q.Local.Origin + origin);
                    if (f.Husk) { q.Color = q.Color.Lerp(new Color(0.45f, 0.42f, 0.4f), 0.35f); q.Glow = 0f; }
                    p.Prims.Add(q);
                }
                if (k.Crown == "antennae")
                    for (int s = -1; s <= 1; s += 2)
                        p.Add(Shape.Sphere, Vector3.One * R * 0.12f * size, origin + new Vector3(s * 0.85f, f.Husk ? 0.1f : 0.78f, f.Husk ? 0.6f : 0.15f) * hs, mark, 0f, f.Husk ? 0f : k.Glow ? 0.5f : 0f, null, 5);
                break;
            }
            case "knobs":
                for (int s = -1; s <= 1; s += 2)
                    p.Add(Shape.Cone, new Vector3(R * 0.22f, R * 0.4f * size, 0f), c + new Vector3(s * R * 0.4f, R * 0.95f, -R * 0.25f), new Color(0.6f, 0.55f, 0.46f), 0f, 0f, new Basis(Vector3.Back, -s * 0.35f), 6, Mat.Horn);
                break;
            case "crest":
                p.Add(Shape.Sphere, new Vector3(R * 0.34f, R * 1.0f * size, R * 1.7f), c + new Vector3(0f, R * 0.95f, R * 0.15f), mark, 0f, 0f, new Basis(Vector3.Right, 0.3f), 9, Chitin(f) ? Mat.Carapace : Mat.Horn);
                break;
            case "keel":
                p.Add(Shape.Sphere, new Vector3(R * 0.14f, R * 0.85f * size, R * 2.0f), c + new Vector3(0f, R * 1.0f, R * 0.12f), skin.Lerp(mark, 0.5f), 0f, 0f, null, 9, Mat.Horn);
                break;
            case "quills":
                for (int i = 0; i < 9; i++)
                {
                    float a = -0.35f + i * 0.28f;
                    Vector3 dir = new(MathF.Sin(i * 2.4f) * 0.25f, MathF.Cos(a), MathF.Sin(a) + 0.35f);
                    dir = dir.Normalized();
                    if (f.Husk) dir = (dir + Vector3.Back * 0.8f).Normalized();
                    p.Add(Shape.Cone, new Vector3(R * 0.13f, R * 0.7f * size, 0f), c + new Vector3(0f, R * 0.1f, 0f) + dir * R * 1.15f, i % 2 == 0 ? mark : skin.Lightened(0.15f), 0f, 0f, Geo.AlongY(dir), 5, Mat.Horn);
                }
                break;
            case "plume":
                for (int i = 0; i < 7; i++)
                {
                    float a = (i - 3) * 0.16f;
                    Vector3 dir = new Vector3(MathF.Sin(a), 0.85f, f.Husk ? 0.9f : 0.4f).Normalized();
                    Leaf(p, c + new Vector3(0f, R * 0.85f, R * 0.1f), dir, Vector3.Right, R * 1.25f * size * (1f - MathF.Abs(a)), R * 0.3f, R * 0.06f, i % 2 == 0 ? mark : mark.Lightened(0.15f), Mat.Skin, 5);
                }
                break;
        }
    }

    // ---- Covering at the collar, glow ----------------------------------------------------------------

    /// <summary>What shows of the covering on the head and neck: fur at the cheeks, feathers at the neck, scutes, a chitin brow; glowing dots.</summary>
    public static void Covering(Piece p, Figure f, Vector3 c, Color skin)
    {
        Folk k = Of(f);
        float R = f.HeadRadius, N = f.Neck;
        switch (k.Covering)
        {
            case "fur":
                for (int s = -1; s <= 1; s += 2)
                    for (int i = 0; i < 3; i++)
                    {
                        Vector3 dir = new Vector3(s * 0.7f, -0.3f - i * 0.25f, 0.6f).Normalized();
                        p.Add(Shape.Cone, new Vector3(R * 0.26f, R * 0.5f, 0f), c + new Vector3(s * R * 0.72f, -R * (0.35f + i * 0.2f), -R * 0.15f) + dir * R * 0.2f, skin.Lightened(0.1f), 0f, 0f, Geo.AlongY(dir), 5);
                    }
                break;
            case "feathers":
                for (int i = 0; i < 8; i++)
                {
                    float a = i / 8f * MathF.Tau;
                    Vector3 at = new(MathF.Cos(a) * R * 0.62f, N * 0.4f, MathF.Sin(a) * R * 0.62f);
                    Leaf(p, at, new Vector3(MathF.Cos(a) * 0.4f, -1f, MathF.Sin(a) * 0.4f), new Vector3(MathF.Cos(a), 0f, MathF.Sin(a)), R * 0.7f, R * 0.32f, R * 0.06f, i % 2 == 0 ? skin.Lightened(0.08f) : Mark(f), Mat.Skin, 5);
                }
                break;
            case "scales":
                for (int i = 0; i < 4; i++)
                    p.Add(Shape.Sphere, new Vector3(R * 0.26f, R * 0.1f, R * 0.32f), c + new Vector3(0f, R * (0.62f + i * 0.12f), -R * (0.72f - i * 0.36f)), skin.Darkened(0.15f), 0f, 0f, new Basis(Vector3.Right, -0.6f + i * 0.4f), 6, Mat.Horn);
                break;
            case "chitin":
                p.Add(Shape.Sphere, new Vector3(R * 1.5f, R * 0.32f, R * 0.5f), c + new Vector3(0f, R * 0.42f, -R * 0.72f), skin.Darkened(0.2f), 0f, 0f, new Basis(Vector3.Right, 0.3f), 8, Mat.Carapace);
                for (int s = -1; s <= 1; s += 2)
                    p.Add(Shape.Sphere, new Vector3(R * 0.5f, R * 0.6f, R * 0.9f), c + new Vector3(s * R * 0.62f, -R * 0.2f, -R * 0.3f), skin.Darkened(0.1f), 0f, 0f, null, 7, Mat.Carapace);
                break;
        }
        if (k.Glow && !f.Husk)
            for (int s = -1; s <= 1; s += 2)
                for (int i = 0; i < 3; i++)
                    p.Add(Shape.Sphere, Vector3.One * R * 0.09f, c + new Vector3(s * R * (0.62f + 0.06f * i), -R * (0.08f + 0.17f * i), -R * (0.66f - 0.1f * i)), k.EyeShine, 0f, 1f, null, 5);
    }

    // ---- Body ---------------------------------------------------------------------------------------

    /// <summary>On the back, over the clothes: spines, plates or a ridge (a sealed suit keeps them in).</summary>
    public static void Back(Piece p, Figure f)
    {
        Folk k = Of(f);
        if (k.Back == "" || f.Dress == "suit") return;
        float T = f.Torso, d = f.ChestD * f.Padding, W = f.ShoulderW * f.Padding, u = f.Height / 1.72f, g = Grown(f);
        Color plate = Chitin(f) ? f.Skin.Darkened(0.2f) : f.Skin.Lerp(Mark(f), 0.4f);
        switch (k.Back)
        {
            case "spines":
                for (int i = 0; i < 6; i++)
                {
                    float y = T * (0.98f - i * 0.13f);
                    Vector3 dir = new Vector3(0f, 0.5f, 1f).Normalized();
                    p.Add(Shape.Cone, new Vector3(0.05f, 0.2f * (1f - i * 0.1f) * g, 0f) * u, new Vector3(0f, y, d * 0.95f) + dir * 0.06f * u, plate, 0f, 0f, Geo.AlongY(dir), 5, Mat.Horn);
                }
                break;
            case "plates":
                for (int i = 0; i < 5; i++)
                    p.Add(Shape.Sphere, new Vector3(W * (1.6f - i * 0.18f), T * 0.2f, d * 0.55f), new Vector3(0f, T * (0.92f - i * 0.16f), d * 0.86f), plate.Lightened(0.04f * (i % 2)), 0f, 0f, new Basis(Vector3.Right, 0.35f), 8, Mat.Carapace);
                break;
            case "ridge":
                for (int i = 0; i < 7; i++)
                    for (int s = -1; s <= 1; s += 2)
                        p.Add(Shape.Box, new Vector3(0.05f, 0.05f, 0.03f) * u, new Vector3(s * 0.035f * u, T * (0.98f - i * 0.11f - (s > 0 ? 0.05f : 0f)), d * 1.0f), plate, 0f, 0f, new Basis(Vector3.Right, 0.5f), 0, Mat.Horn);
                break;
        }
    }

    /// <summary>Whether the hands end in claws (not under gloves).</summary>
    public static bool Clawed(Figure f) => Of(f).Claws && f.Dress is not ("suit" or "hazard" or "furs");

    /// <summary>Glowing dots up a bare forearm.</summary>
    public static void Glow(Piece forearm, Figure f, float fore, bool bare)
    {
        Folk k = Of(f);
        float u = f.Height / 1.72f;
        if (k.Glow && bare && !f.Husk)
            for (int i = 0; i < 3; i++)
                forearm.Add(Shape.Sphere, Vector3.One * 0.012f * u, new Vector3(0.028f * u, fore * (0.3f + 0.18f * i), -0.01f * u), k.EyeShine, 0f, 1f, null, 5);
    }

    // ---- Tail ---------------------------------------------------------------------------------------

    /// <summary>How many bones the tail has (0 for none): a stub is one, the rest three.</summary>
    public static int TailBones(Figure f) => Of(f).Tail switch { "" => 0, "stub" => 1, _ => 3 };

    /// <summary>A tail's length, m, for this body.</summary>
    public static float TailLength(Figure f) => Of(f).TailLength * f.Height / 1.72f * (f.Stage == LifeStage.Child ? 0.6f : 1f);

    /// <summary>A tail's radius at its root, m.</summary>
    public static float TailRoot(Figure f) => f.Height / 1.72f * Of(f).Tail switch { "stub" => 0.06f, "thick" => 0.075f, "bushy" => 0.05f, "fan" => 0.032f, _ => 0.032f };

    /// <summary>
    /// The tail's bones as pieces tail0, tail1, tail2 (each along +Y from its joint): a stub, a
    /// whip, a tuft at the end, a thick lizard's, a bushy one, a fan of feathers. A sealed suit
    /// gives it a sleeve.
    /// </summary>
    public static void Tail(RigLayout layout, Figure f)
    {
        Folk k = Of(f);
        int n = TailBones(f);
        if (n == 0) return;
        float len = TailLength(f), seg = len / n, r = TailRoot(f), u = f.Height / 1.72f;
        Color skin = f.Dress is "suit" or "hazard" ? f.Cloth : f.Skin, tip = Pointed(f, f.Skin.Lerp(Mark(f), 0.5f));
        for (int i = 0; i < n; i++)
        {
            var piece = new Piece($"tail{i}");
            float r0 = r * (1f - i / (float)(n + (k.Tail == "thick" ? 0.3f : 1f))), r1 = i == n - 1 ? r * (k.Tail is "whip" or "thick" ? 0.08f : 0.45f) : r * (1f - (i + 1f) / (n + (k.Tail == "thick" ? 0.3f : 1f)));
            if (k.Tail == "stub") piece.Add(Shape.Sphere, new Vector3(r * 2f, seg, r * 2f), Vector3.Up * seg * 0.45f, skin, 0f, 0f, null, 7);
            else piece.Taper(Vector3.Zero, Vector3.Up * seg, r0, r1, i == n - 1 && k.Tail == "whip" ? tip : skin, 0f, 0f, SkinMat(f), 6);
            if (k.Tail == "bushy")
                for (int j = 0; j < 4; j++)
                {
                    float a = j * 1.6f + i;
                    piece.Add(Shape.Sphere, Vector3.One * (r * 2.6f + 0.02f * u) * (i == n - 1 ? 1.15f : 1f), new Vector3(MathF.Cos(a) * r * 0.6f, seg * (0.2f + 0.22f * j), MathF.Sin(a) * r * 0.6f), j == 3 && i == n - 1 ? tip : skin.Lightened(0.05f * (j % 2)), 0f, 0f, null, 6);
                }
            if (i == n - 1)
            {
                if (k.Tail == "tufted")
                    for (int j = 0; j < 6; j++)
                    {
                        Vector3 dir = new Vector3(MathF.Cos(j * 1.05f) * 0.45f, 1f, MathF.Sin(j * 1.05f) * 0.45f).Normalized();
                        piece.Add(Shape.Cone, new Vector3(0.04f, 0.13f, 0f) * u, Vector3.Up * seg + dir * 0.05f * u, tip, 0f, 0f, Geo.AlongY(dir), 5);
                    }
                if (k.Tail == "fan")
                    for (int j = 0; j < 7; j++)
                    {
                        float a = (j - 3) * 0.26f;
                        Leaf(piece, Vector3.Up * seg * 0.6f, new Vector3(MathF.Sin(a), 1f, 0f), Vector3.Forward, 0.3f * u * (1f - 0.12f * MathF.Abs(j - 3)), 0.07f * u, 0.012f * u, j % 2 == 0 ? Mark(f) : f.Skin.Lightened(0.1f), Mat.Skin, 5);
                    }
                if (k.Tail == "thick" && k.Back == "spines") Parts.TailTip(piece, "spiked", seg, r * 2.2f, new Paint(new Species { Colors = { Base = skin, Accent = Mark(f), Belly = skin } }));
            }
            if (i == 0 && f.Dress != "rags") piece.Add(Shape.Cylinder, new Vector3(r * 2.6f, 0.025f * u, r * 2.6f), Vector3.Up * 0.01f, f.Trim.Darkened(0.2f), 0f, 0f, null, 7);
            piece.Shadow = k.Tail is "thick" or "bushy";
            layout.Add(piece);
        }
    }

    // ---- From a distance ----------------------------------------------------------------------------

    /// <summary>What of the folk reads at range on the one cheap mesh: the crown, big ears, the back and the tail.</summary>
    public static void Far(Piece p, Figure f, Color head)
    {
        Folk k = Of(f);
        float R = f.HeadRadius, u = f.Height / 1.72f, top = f.Height;
        if (k.Crown != "" && !f.Has("suit_helmet"))
        {
            float rise = k.CrownRise * R * Grown(f);
            bool pair = k.Crown is "pair" or "ram" or "brow" or "antlers" or "antennae" or "knobs";
            Color c = k.Crown is "crest" or "plume" or "crown" or "quills" ? Mark(f) : k.Crown == "antennae" ? f.Skin.Darkened(0.4f) : new Color(0.58f, 0.52f, 0.42f);
            if (pair)
                for (int s = -1; s <= 1; s += 2)
                    p.Add(Shape.Box, new Vector3(R * (k.Crown == "antlers" ? 0.6f : 0.25f), rise, R * 0.25f), new Vector3(s * R * (k.Crown == "antlers" ? 0.8f : 0.45f), top - R * 0.2f + rise * 0.5f, 0f), c, 0f, 0f, new Basis(Vector3.Back, -s * 0.3f));
            else p.Add(Shape.Box, new Vector3(R * 0.3f, rise, R * 1.6f), new Vector3(0f, top - R * 0.2f + rise * 0.5f, 0f), c);
        }
        if (k.Ears is "tall" or "fan" && !f.Has("suit_helmet"))
            for (int s = -1; s <= 1; s += 2)
                p.Add(Shape.Box, new Vector3(R * 0.2f, R * (k.Ears == "tall" ? 2.2f : 1.4f) * k.EarSize, R * 0.8f), new Vector3(s * R * (k.Ears == "tall" ? 0.55f : 1.3f), top - R * (k.Ears == "tall" ? -0.4f : 0.9f), 0f), head, 0f, 0f, new Basis(Vector3.Back, -s * (k.Ears == "tall" ? 0.2f : 0.9f)));
        if (k.Back != "" && f.Dress != "suit")
            p.Add(Shape.Box, new Vector3(f.ShoulderW * 1.6f, f.Torso * 0.7f, 0.08f * u), new Vector3(0f, f.LegLength + f.Torso * 0.6f, f.ChestD * f.Padding + 0.04f * u), f.Skin.Darkened(0.15f));
        float tail = TailLength(f);
        if (tail > 0.25f)
        {
            Vector3 dir = new Vector3(0f, -0.55f, 1f).Normalized();
            Vector3 root = new(0f, f.LegLength * 0.95f, f.ChestD * f.Padding * 0.8f);
            p.Add(Shape.Box, new Vector3(TailRoot(f) * 1.6f, tail, TailRoot(f) * 1.6f), root + dir * tail * 0.5f, f.Dress is "suit" or "hazard" ? f.Cloth : f.Skin, 0f, 0f, Geo.AlongY(dir));
        }
    }
}
