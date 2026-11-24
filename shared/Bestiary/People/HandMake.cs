using System;
using Godot;

namespace Wildlife;

/// <summary>How a hand is held: spread, closed on itself, or the first finger out.</summary>
public enum HandShape { Open, Fist, Point }

/// <summary>
/// A hand made by code: palm, four fingers and a thumb at a real adult's size (0.19 m long, 8.5 cm
/// wide, times the figure's height over 1.72, its <see cref="Figure.HandK"/> and a readability
/// factor, so a gesture reads at 20 to 30 m). In the hand's own space +Y runs along the fingers
/// from the wrist, +Z comes out of the palm and +X is toward a right hand's thumb (a left hand is
/// its mirror). Each arm has one piece per shape (hand0, fist0, point0, ...): the rig shows the
/// shape asked for and folds the others away.
/// </summary>
public static class HandMake
{
    /// <summary>Hands are drawn this much bigger than life: the design asks key gestures to read at 20 to 30 m.</summary>
    public const float Readable = 1.3f;

    static readonly string[] Names = { "hand", "fist", "point" };

    /// <summary>A hand piece's name: the shape's, then the arm (0 left, 1 right).</summary>
    public static string PieceName(HandShape shape, int side) => Names[(int)shape] + side;

    /// <summary>How big this arm's hand is: 1 is a 1.72 m person's, as drawn (gloves and a Husk's long arm run bigger).</summary>
    public static float Size(Figure f, int side) =>
        HumanBody.Scale(f) * f.HandK * Readable * (f.Wrongs.HasFlag(Wrong.Arm) && side == f.LongArm ? 1.2f : f.Dress is "furs" or "suit" ? 1.12f : 1f);

    // The palm, then where the fingers start: all in metres at size 1.
    const float Wide = 0.085f, Long = 0.095f, Thick = 0.028f;
    static readonly float[] FingerX = { 0.03f, 0.01f, -0.01f, -0.03f }, FingerLong = { 0.082f, 0.092f, 0.086f, 0.07f };

    /// <summary>Half the palm's thickness at size 1: how far a hand laid flat has its centre from what it lies on.</summary>
    public const float Half = Thick * 0.5f;

    /// <summary>The middle of a closed fist, where something gripped runs through (along the hand's X), at size 1.</summary>
    public static readonly Vector3 Fist = new(0f, 0.07f, 0.05f);

    /// <summary>A hand's box in its own space (a right hand, size 1): from, to.</summary>
    public static (Vector3 lo, Vector3 hi) Extent(HandShape shape) => shape switch
    {
        HandShape.Open => (new Vector3(-Wide * 0.5f, -0.01f, -0.017f), new Vector3(0.07f, 0.19f, 0.017f)),
        HandShape.Fist => (new Vector3(-Wide * 0.5f, -0.01f, -0.014f), new Vector3(Wide * 0.5f, 0.11f, 0.091f)),
        _ => (new Vector3(-Wide * 0.5f, -0.01f, -0.014f), new Vector3(Wide * 0.5f, 0.185f, 0.091f)),
    };

    /// <summary>A hand's box in its own space at this size (a left hand mirrored).</summary>
    public static (Vector3 lo, Vector3 hi) Box(HandShape shape, bool left, float size)
    {
        var (lo, hi) = Extent(shape);
        return (new Vector3(left ? -hi.X : lo.X, lo.Y, lo.Z) * size, new Vector3(left ? -lo.X : hi.X, hi.Y, hi.Z) * size);
    }

    /// <summary>The hand's eight corners and its middle in its own space, at this size: what the body is kept out of.</summary>
    public static void Corners(HandShape shape, bool left, float size, Vector3[] into)
    {
        var (lo, hi) = Box(shape, left, size);
        for (int k = 0; k < 8; k++) into[k] = new Vector3((k & 1) == 0 ? lo.X : hi.X, (k & 2) == 0 ? lo.Y : hi.Y, (k & 4) == 0 ? lo.Z : hi.Z);
        into[8] = (lo + hi) * 0.5f;
    }

    /// <summary>The three shapes of one hand as pieces (hand, fist, point), in the colours of its skin or glove and its lighter palm.</summary>
    public static void Build(RigLayout layout, Figure f, int side, Color skin, Color palm, float material, bool clawed)
    {
        float s = Size(f, side), m = side == 0 ? -1f : 1f;
        for (int shape = 0; shape < 3; shape++)
        {
            var p = new Piece(PieceName((HandShape)shape, side)) { Shadow = false };
            void Box(float w, float l, float t, float x, float y, float z, Color c, float turn = 0f) =>
                p.Add(Shape.Box, new Vector3(w, l, t) * s, new Vector3(x * m, y, z) * s, c, 0f, 0f, turn == 0f ? null : new Basis(Vector3.Back, turn * m), 0, material);
            void Claw(float x, float y, float z, Vector3 dir) =>
                p.Add(Shape.Cone, new Vector3(0.012f, 0.035f, 0f) * s, new Vector3(x * m, y, z) * s + dir * (0.0175f * s), new Color(0.18f, 0.15f, 0.12f), 0f, 0f, Geo.AlongY(dir), 5, Mat.Horn);
            Box(Wide, Long + 0.01f, Thick, 0f, (Long - 0.01f) * 0.5f, 0f, skin);
            Box(0.066f, 0.075f, 0.004f, 0f, 0.05f, Thick * 0.5f + 0.001f, palm); // the palm side shows light
            if (shape == (int)HandShape.Open)
            {
                for (int k = 0; k < 4; k++)
                {
                    Box(0.018f, FingerLong[k], 0.02f, FingerX[k], Long + FingerLong[k] * 0.5f, 0f, skin);
                    if (clawed) Claw(FingerX[k], Long + FingerLong[k], 0f, Vector3.Up);
                }
                Box(0.022f, 0.062f, 0.022f, 0.056f, 0.035f, 0f, skin, -0.55f);
            }
            else
            {
                // Fingers curled over the palm (all four, or three with the first out), the thumb across them.
                bool point = shape == (int)HandShape.Point;
                Box(point ? 0.0675f : Wide, 0.05f, 0.05f, point ? -0.009f : 0f, 0.085f, 0.048f, skin);
                Box(0.022f, 0.05f, 0.022f, 0.02f, 0.09f, 0.08f, skin, 1.2f);
                if (point)
                {
                    Box(0.018f, 0.09f, 0.02f, 0.03f, 0.14f, 0.008f, skin);
                    if (clawed) Claw(0.03f, 0.185f, 0.008f, Vector3.Up);
                }
            }
            layout.Add(p);
        }
    }
}
