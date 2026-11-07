using System;
using Godot;

namespace Wildlife;

/// <summary>
/// No group reacts in one instant. Each individual has its own nerve, set when it's born, and
/// reacts to an event after a delay of its own: quicker with more nerve, slower the further off
/// it was, slower if it only heard rather than saw, slower again for the young and the old, and a
/// little different every time. Until then it does what it was doing, or freezes, or turns its
/// head to look, so a startle ripples out through a herd or a crowd. Delays stay short: a fraction
/// of a second to about two. Machines are the exception: they act together, on the same tick,
/// with nothing in them to hesitate.
/// </summary>
public static class Reaction
{
    public const float Least = 0.08f, Most = 2.2f, Machine = 0.05f;

    /// <summary>How long this one takes to react to something this far off.</summary>
    public static float Delay(float nerve, float distance, bool saw, bool young, bool old, Rng rng)
    {
        float d = Mathf.Lerp(0.95f, 0.12f, Math.Clamp(nerve, 0f, 1f)) + MathF.Min(distance, 40f) / 40f * 0.6f + (saw ? 0f : 0.3f) + rng.Range(0f, 0.35f);
        if (young) d *= 1.5f;
        if (old) d *= 1.35f;
        return Math.Clamp(d, Least, Most);
    }

    /// <summary>A creature's delay: robots in sync, everything alive its own.</summary>
    public static float Delay(Creature c, float distance, bool saw)
    {
        if (c.Species.Mechanical) return Machine;
        Figure f = c.Species.Figure;
        return Delay(c.Nerve, distance, saw, c.Species.Adult != null || f?.Stage == LifeStage.Child, f?.Stage == LifeStage.Elder, c.Rng);
    }

    /// <summary>A beat of hesitation between one member of a pack or mob committing and the next: none for machines.</summary>
    public static float Hesitation(Creature c) => c.Species.Mechanical ? 0f : Mathf.Lerp(0.8f, 0.1f, c.Nerve) * c.Rng.Range(0.6f, 1.2f);
}