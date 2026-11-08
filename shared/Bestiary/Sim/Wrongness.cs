using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>
/// The Husks are a little psionic, and more so together: near them things go wrong. How strongly,
/// at a point, is one number from 0 to 1: every Husk within reach counts (the nearer the more),
/// every worm mass for its size, a cluster for more than the sum of it, and a den for how far
/// gone it is (<see cref="WildSim.DenSeverity"/>). Faint near a few, strong in a deep den. The
/// game reads it for what you notice (the compass drifting, shadows falling a little wrong, sound
/// dying off); the sim for what animals do (they won't go near a Husk, <see cref="Shunned"/>).
/// Nothing names it.
/// </summary>
public static class Wrongness
{
    /// <summary>How strongly the wrongness is felt at a point of this sim, 0..1.</summary>
    public static float At(WildSim w, Vector3 p)
    {
        HuskRules r = w.Data.Husks;
        float reach = r.WrongReach, sum = 0f;
        foreach (Creature c in w.Creatures)
        {
            if (c.Removed || c.Dead || !c.Species.Husk) continue;
            float d = (c.Position - p).Length();
            if (d >= reach) continue;
            float k = 1f - d / reach;
            sum += r.WrongPerHusk * k * k;
        }
        foreach (WormMass m in w.Masses)
        {
            if (m.Dead) continue;
            float d = (m.Position - p).Length();
            if (d >= reach) continue;
            float k = 1f - d / reach;
            sum += r.WrongPerMass * m.Size * k * k;
        }
        if (sum <= 0f) return 0f;
        // Together they are more than the sum of them; a deep den more again.
        float felt = MathF.Pow(sum, r.WrongCluster) * (1f + r.WrongDen * w.DenSeverity);
        return 1f - MathF.Exp(-felt);
    }

    /// <summary>
    /// Keeps an animal off the Husks: whatever it wants, nothing carries it nearer one than the
    /// shun distance, and inside it, it is pushed back out. A wander goal near one is given up.
    /// </summary>
    public static void Shunned(WildSim w, Creature c)
    {
        float keep = w.Data.Husks.AnimalShun;
        foreach (Vector3 h in w.HuskSpots)
        {
            Vector3 from = WildSim.Flat(c.Position - h);
            float d = from.Length();
            if (d >= keep + 4f) continue;
            Vector3 outward = d > 0.01f ? from / d : c.Forward;
            float inward = -c.Desired.Dot(outward);
            if (inward > 0f) c.Desired += outward * inward;
            if (d < keep) c.Desired += outward * MathF.Max(c.Stats.WalkSpeed, 1f) * Math.Clamp((keep - d) / 4f, 0f, 1.5f);
            if (c.HasGoal && WildSim.Flat(c.Goal - h).Length() < keep) c.HasGoal = false;
        }
    }

    /// <summary>Whether this creature keeps off the Husks: an animal of the open (not a person, a machine, a Husk or a town's own).</summary>
    public static bool Shuns(Creature c) =>
        c.Species.Figure == null && !c.Species.Mechanical && !c.Species.Husk && c.Species.Domestic == DomesticKind.None && c.Space == null;

    /// <summary>Where the sim's Husks stand now (and any a game adds from elsewhere, its dens), refreshed now and then.</summary>
    internal static void Refresh(WildSim w)
    {
        w.HuskSpots.Clear();
        foreach (Creature c in w.Creatures)
            if (!c.Removed && !c.Dead && c.Species.Husk) w.HuskSpots.Add(c.Position);
        w.HuskSpots.AddRange(w.WrongPlaces);
    }
}
