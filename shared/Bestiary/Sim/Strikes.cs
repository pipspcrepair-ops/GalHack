using System;
using Godot;

namespace Wildlife;

/// <summary>The ground shaking under a heavy blow: a stomp coming down (hit or miss), a charge's footfalls, a charge into something solid.</summary>
public struct QuakeEvent
{
    public Creature Creature;
    /// <summary>Where the foot came down, on the ground.</summary>
    public Vector3 Position;
    /// <summary>How hard, 0..1, and how far it is felt, m (both by the body's mass).</summary>
    public float Strength, Reach;
    /// <summary>A stomp (a deep thud and dust) rather than a footfall; whether it came down on someone.</summary>
    public bool Stomp, Hit;
    public float Time;
}

/// <summary>
/// What a strike is made with and whether it lands. Every body bites (or strikes with its head);
/// bodies whose parts suit it also stomp, charge or swipe its tail (<see cref="StrikeRules"/>,
/// read from the parts and proportions, never per species). The move is picked by where the
/// victim is: behind or beside a heavy tail, a swipe; close in front of or under a pillar-legged
/// giant, a stomp; far off in front of a horned one, a charge; otherwise a bite.
///
/// A blow lands only when the striking part (<see cref="Rig.Striker"/>: the head and jaws, a
/// foot, the tail's tip, a fist) actually meets the victim's body, judged along the way it moved
/// since the last step so a fast sweep can't pass through someone unseen. No contact, no damage.
/// </summary>
public static class Strikes
{
    /// <summary>A crouching person's height, m.</summary>
    public const float CrouchHeight = 1.2f;

    public static bool CanStomp(Species s, StrikeRules r) =>
        Walker(s) && s.Body.LegStyle == LegStyle.Upright && s.Body.Size >= r.StompSize && s.Body.LegThickness >= r.StompLegs * s.Body.LegLength;

    public static bool CanCharge(Species s, StrikeRules r) => Walker(s) && s.Body.Size >= r.ChargeSize && s.Part("horns") != "none";

    public static bool CanSwipe(Species s, StrikeRules r) =>
        Walker(s) && s.Body.Size >= r.SwipeSize && s.Body.TailLength > s.Body.Size * 0.08f
        && (Tipped(s, r) || s.Body.TailLength >= r.SwipeTail * s.Body.Size);

    /// <summary>A tail ending in a club or spikes: it hits harder.</summary>
    public static bool Tipped(Species s, StrikeRules r) => Array.IndexOf(r.SwipeTips, s.Part("tail")) >= 0;

    /// <summary>Four legs or more on the open ground, flesh not machine: what stomps, charges and swipes.</summary>
    static bool Walker(Species s) => s.Rig == RigKind.Legged && !s.Mechanical && !s.Flies && s.Body.Legs >= 4;

    /// <summary>From its middle to the end of its tail, and the victim's skin beyond.</summary>
    public static float SwipeReach(Species s) => s.Body.Size * 0.5f + s.Body.TailLength + Attacks.BodyRadius;

    /// <summary>The strike for a victim where they stand now.</summary>
    public static AttackMove Choose(WildSim w, Creature c, ITarget t)
    {
        StrikeRules r = w.Combat.Strikes;
        Species s = c.Species;
        if (c.Space != null) return AttackMove.Bite;
        Local(c, t.Position, out float ahead, out float side);
        float d = MathF.Sqrt(ahead * ahead + side * side), round = MathF.Atan2(MathF.Abs(side), ahead);
        Proportions b = s.Body;
        if (CanSwipe(s, r) && round > Mathf.DegToRad(r.SwipeAngle) && d <= SwipeReach(s)) return AttackMove.Swipe;
        if (CanStomp(s, r) && ahead > -b.Size * 0.25f && ahead < b.Size * 0.5f + b.LegLength * 0.9f && MathF.Abs(side) < b.Width * 0.5f + b.LegLength * 0.7f) return AttackMove.Stomp;
        if (CanCharge(s, r) && d >= r.ChargeFrom) return AttackMove.Charge;
        return AttackMove.Bite;
    }

    /// <summary>How close it must be to start on this victim: a charge from far off, a swipe at someone behind.</summary>
    public static float Reach(WildSim w, Creature c, ITarget t, float bite)
    {
        StrikeRules r = w.Combat.Strikes;
        if (c.Space != null) return bite;
        float d = WildSim.Flat(t.Position - c.Position).Length();
        if (CanCharge(c.Species, r) && d >= r.ChargeFrom && d <= r.ChargeRun) return r.ChargeRun;
        if (CanSwipe(c.Species, r))
        {
            Local(c, t.Position, out float ahead, out float side);
            if (MathF.Atan2(MathF.Abs(side), ahead) > Mathf.DegToRad(r.SwipeAngle)) return MathF.Max(bite, SwipeReach(c.Species));
        }
        return bite;
    }

    /// <summary>Where a point is from the creature: metres ahead of it and to its right.</summary>
    public static void Local(Creature c, Vector3 p, out float ahead, out float side)
    {
        Vector3 to = new Basis(Vector3.Up, c.Yaw).Inverse() * WildSim.Flat(p - c.Position);
        ahead = -to.Z;
        side = to.X;
    }

    /// <summary>The striking part this step, swept from where it was last step: whether it has met the victim, and how near it came (negative: into them).</summary>
    public static bool Touch(WildSim w, Creature c, ITarget t, out float gap)
    {
        Vector4 s = c.Rig.Striker(c);
        var now = new Vector3(s.X, s.Y, s.Z);
        Vector3 was = c.StrikerWasOn ? c.StrikerWas : now;
        c.StrikerWas = now;
        c.StrikerWasOn = true;
        gap = s.W > 0f ? Gap(was, now, s.W, t) : float.MaxValue;
        return t.Attackable && gap <= w.Combat.ContactSlack
               && (c.Space == null || SpaceMath.Clear(c.Space, now, t.Position + Vector3.Up * (Attacks.BodyHeight * 0.5f))); // never through a wall, floor or ceiling
    }

    /// <summary>How far a sphere of radius r moved from a to b passes from the victim's body (a person's capsule, or a creature's own hit spheres).</summary>
    public static float Gap(Vector3 a, Vector3 b, float r, ITarget t)
    {
        if (t is CreatureTarget { Creature.Rig: { } rig })
        {
            float best = float.MaxValue;
            for (int i = 0; i < rig.HitCount; i++)
            {
                Vector4 h = rig.Hits[i];
                var p = new Vector3(h.X, h.Y, h.Z);
                best = MathF.Min(best, SegmentGap(a, b, p, p) - r - h.W);
            }
            return best;
        }
        float tall = t.Crouched ? CrouchHeight : Attacks.BodyHeight;
        return SegmentGap(a, b, t.Position + Vector3.Up * Attacks.BodyRadius, t.Position + Vector3.Up * (tall - Attacks.BodyRadius)) - r - Attacks.BodyRadius;
    }

    /// <summary>The least distance between segments p0-p1 and q0-q1.</summary>
    public static float SegmentGap(Vector3 p0, Vector3 p1, Vector3 q0, Vector3 q1)
    {
        Vector3 d1 = p1 - p0, d2 = q1 - q0, r = p0 - q0;
        float a = d1.Dot(d1), e = d2.Dot(d2), f = d2.Dot(r);
        float s, u;
        if (a < 1e-8f && e < 1e-8f) return r.Length();
        if (a < 1e-8f) { s = 0f; u = Math.Clamp(f / e, 0f, 1f); }
        else
        {
            float c = d1.Dot(r);
            if (e < 1e-8f) { u = 0f; s = Math.Clamp(-c / a, 0f, 1f); }
            else
            {
                float bb = d1.Dot(d2), den = a * e - bb * bb;
                s = den > 1e-8f ? Math.Clamp((bb * f - c * e) / den, 0f, 1f) : 0f;
                u = (bb * s + f) / e;
                if (u < 0f) { u = 0f; s = Math.Clamp(-c / a, 0f, 1f); }
                else if (u > 1f) { u = 1f; s = Math.Clamp((bb - c) / a, 0f, 1f); }
            }
        }
        return (p0 + d1 * s - (q0 + d2 * u)).Length();
    }

    /// <summary>A landed blow's share of its damage, by the move.</summary>
    public static float DamageShare(WildSim w, Creature c)
    {
        StrikeRules r = w.Combat.Strikes;
        return c.Move switch
        {
            AttackMove.Charge => r.ChargeDamage,
            AttackMove.Stomp => r.StompDamage,
            AttackMove.Swipe => r.SwipeDamage * (Tipped(c.Species, r) ? r.SwipeTipped : 1f),
            _ => 1f,
        };
    }

    /// <summary>
    /// How a landed blow throws its victim, m/s, by the striker's mass: back from a bite, tossed
    /// up and away by a charge, flattened under a stomp, swept aside by a tail.
    /// </summary>
    public static Vector3 Push(WildSim w, Creature c, ITarget t)
    {
        StrikeRules r = w.Combat.Strikes;
        float k = MathF.Min(r.PushMost, r.Push * MathF.Cbrt(Heft.Mass(c.Species) / 80f));
        Vector3 away = WildSim.Flat(t.Position - c.Position);
        away = away.LengthSquared() > 1e-4f ? away.Normalized() : c.Forward;
        return c.Move switch
        {
            AttackMove.Charge => away * k * 1.3f + Vector3.Up * k * 0.4f,
            AttackMove.Stomp => away * k * 0.35f,
            AttackMove.Swipe => new Basis(Vector3.Up, c.Yaw) * Vector3.Right * (c.StrikeLimb * k),
            _ => away * k * 0.6f,
        };
    }

    /// <summary>The ground shaking under this body: a stomp's full weight, or a share for a footfall.</summary>
    public static void Quake(WildSim w, Creature c, Vector3 at, bool stomp, bool hit, float share = 1f)
    {
        StrikeRules r = w.Combat.Strikes;
        float tonnes = MathF.Cbrt(Heft.Mass(c.Species) / 1000f);
        w.RaiseQuake(new QuakeEvent
        {
            Creature = c, Position = new Vector3(at.X, w.Terrain.Height(at.X, at.Z), at.Z), Strength = MathF.Min(1f, r.Quake * tonnes * share),
            Reach = r.QuakeReach * tonnes * MathF.Sqrt(share), Stomp = stomp, Hit = hit, Time = w.Time,
        });
    }
}
