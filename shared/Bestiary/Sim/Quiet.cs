using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>What a small sound is: what made it, doing what.</summary>
public enum QuietKind { Footsteps, Skitter, Clatter, Murmur, Servo }

/// <summary>One small sound, where it came from, and how loud (dB, before distance).</summary>
public struct QuietSound
{
    public Creature Creature;
    public QuietKind Kind;
    public Vector3 At;
    public float Volume;
    public bool ThroughWall;
}

/// <summary>
/// Inside places, things keep quiet so they can surprise you: no idle calls, nothing constant.
/// What gives them away is the small sound of what they are doing, out of sight: feet, the
/// skitter of many legs, something knocked over, a murmur of voices, the tick of a machine. It
/// comes from the body (its weight sets how loud) and the act (only something moving makes any),
/// carries as hearing does, and comes through a wall much quieter. Nothing in sight makes one:
/// what you can see, you can see. Outdoors none of this runs.
/// </summary>
public static class Quiet
{
    /// <summary>How far a small sound carries in the open, and through a wall, m.</summary>
    public const float Reach = 24f, ThroughWall = 11f;
    /// <summary>How much quieter a wall makes it, dB.</summary>
    public const float WallLoss = 10f;

    /// <summary>The small sounds due this step from what lives in a place, for a listener.</summary>
    public static void Step(WildSim w, ITarget listener, float dt, List<QuietSound> into)
    {
        if (listener == null) return;
        Vector3 ear = listener.Position + Vector3.Up * 1.5f;
        foreach (Creature c in w.Creatures)
        {
            if (c.Removed || c.Dead || c.Space == null || c.InWalls) continue;
            float speed = WildSim.Flat(c.Velocity).Length();
            bool talking = c.Species.Figure is { Voice: not null } && !c.Species.Mechanical && Company(w, c);
            if (speed < 0.25f && !talking)
            {
                c.QuietIn = MathF.Max(c.QuietIn, 0.8f);
                continue;
            }
            if ((c.QuietIn -= dt) > 0f) continue;
            // Quicker feet are heard more often; a still group talks now and then.
            c.QuietIn = c.Rng.Range(2.5f, 6.5f) / MathF.Max(0.6f, MathF.Min(2.2f, speed)) + (speed < 0.25f ? c.Rng.Range(4f, 9f) : 0f);
            float d = (c.Position - listener.Position).Length();
            if (d > Reach) continue;
            Vector3 at = c.Position + Vector3.Up * MathF.Min(1.2f, c.Species.BodyHeight * 0.5f);
            bool clear = w.SightLine != null && !c.NoRays ? w.SightLine(at, ear) : SpaceMath.Clear(c.Space, at, ear, 0.05f);
            // In plain sight it gives nothing away it hasn't already.
            if (clear && d < Reach * 0.6f) continue;
            if (!clear && d > ThroughWall) continue;
            QuietKind kind = KindOf(c, speed, talking);
            float loud = Heft.Loudness(c.Species) - 13f + (kind == QuietKind.Clatter ? 4f : 0f) - (clear ? 0f : WallLoss);
            into.Add(new QuietSound { Creature = c, Kind = kind, At = at, Volume = loud, ThroughWall = !clear });
        }
    }

    static bool Company(WildSim w, Creature c)
    {
        foreach (Creature o in w.Creatures)
            if (o != c && !o.Dead && !o.Removed && o.Species.Figure != null && (o.Position - c.Position).LengthSquared() < 36f) return true;
        return false;
    }

    /// <summary>What sound its body makes doing what it's doing.</summary>
    static QuietKind KindOf(Creature c, float speed, bool talking)
    {
        Species s = c.Species;
        if (speed > 2.2f && c.Rng.Chance(0.15f)) return QuietKind.Clatter; // in a hurry, something goes over
        if (s.Mechanical) return QuietKind.Servo;
        if (talking && speed < 1f) return QuietKind.Murmur;
        if (s.Coat == Coat.Chitin || s.Rig is RigKind.Crawler or RigKind.Swarm or RigKind.Chain || s.Climbs) return QuietKind.Skitter;
        return QuietKind.Footsteps;
    }
}
