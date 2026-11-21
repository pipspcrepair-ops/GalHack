using System;
using Godot;

namespace Wildlife;

/// <summary>
/// Getting out of the water, for anyone in it on foot (a town's people, the land's creatures): the nearest bank low enough to
/// climb out on, and who can swim to it at all. The one rule for people and creatures alike.
/// </summary>
public static class Shore
{
    /// <summary>
    /// The nearest dry ground round a point in water that a body can climb out onto (no more than <paramref name="climb"/> over the
    /// water's surface where it meets it), looked for in rings out to <paramref name="reach"/>; null if there is none that near.
    /// <paramref name="water"/> is the surface over a point (null on dry ground), <paramref name="ground"/> the ground's top.
    /// </summary>
    public static Vector3? Nearest(Vector3 from, Func<float, float, float?> water, Func<float, float, float> ground, float climb, float reach)
    {
        float top = water(from.X, from.Z) ?? from.Y;
        for (float r = Step; r <= reach; r += Step)
        {
            int n = Math.Max(12, (int)(r * 1.5f));
            Vector3? best = null;
            float low = float.MaxValue;
            for (int k = 0; k < n; k++)
            {
                float a = k * MathF.Tau / n, x = from.X + MathF.Sin(a) * r, z = from.Z + MathF.Cos(a) * r;
                if (water(x, z) != null) continue;
                float rise = ground(x, z) - top;
                if (rise > climb || rise >= low) continue;
                (best, low) = (new Vector3(x, top + rise, z), rise);
            }
            if (best != null) return best;
        }
        return null;
    }

    /// <summary>The rings' spacing, m.</summary>
    const float Step = 2f;

    /// <summary>
    /// Whether a creature swims, by its body: anything that walks or crawls the land does, but what is too heavy for it, a machine, a
    /// plated or armoured body, or a giant; those wade where they can stand and drown where they can't.
    /// </summary>
    public static bool Swims(Species s) =>
        !s.Mechanical && s.Coat != Coat.Plating && s.Adaptation != Adaptation.Armoured && s.Plan != BodyPlan.Giant;
}
