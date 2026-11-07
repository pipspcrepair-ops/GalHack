using System;
using Godot;

namespace Wildlife;

/// <summary>
/// How creatures attack. Every attack is wind-up, lunge, recover:
///
/// - The wind-up is the telegraph. The creature stops, faces you and rears (the rig shows it,
///   the view plays a growl). A creature's first attack on a player uses its species' long
///   telegraph from data; later ones use the short normal wind-up.
/// - Part way through the wind-up it commits to where you are standing. The lunge then flies
///   at that spot, not at you, so stepping aside after the commit dodges it.
/// - Contact is judged where the lunge lands. Damage is dealt in full; nothing is capped.
/// - Each species attacks in its behaviour's signature style: a pounce crouches low, a charge
///   lowers its head and paws, a giant rears. The style changes the look and sound only; the
///   timing rules are the same for all.
/// - It bites with its head, not from inside you: the body stops so the jaws meet yours at your
///   skin (<see cref="BodyRadius"/>), however long its neck, and nothing passes through you
///   (<see cref="Locomotion.KeepOff"/>). A lunge from your blind side swings round, as far as it can,
///   to land in front of you, so you see what bit you.
/// - A glider can't stop to do any of this: the same wind-up, commit and strike happen on the wing,
///   as a swoop (<see cref="Swoop"/>). A hoverer stops and stings from the hover, the ordinary way.
/// - Pack members take turns: one may wind up at a time, and each commit comes at least a
///   telegraph after the last, so a pack never lands several lunges at once.
/// </summary>
public static class Attacks
{
    /// <summary>A person's body, as far as jaws and limbs go: how wide from the middle, how tall.</summary>
    public const float BodyRadius = 0.35f, BodyHeight = 1.9f;
    /// <summary>How fast a lunge covers ground at most, m/s; short legs throw a body less far.</summary>
    const float MostLungeSpeed = 8f;
    /// <summary>The most a bite from behind slides round the victim, and within what angle of their front it needn't.</summary>
    static readonly float Swing = Mathf.DegToRad(55f), Front = Mathf.DegToRad(75f);

    /// <summary>From its middle to the tip of its jaws, along the way it faces, by its rig's own head and neck.</summary>
    public static float Jaws(Creature c)
    {
        float best = c.Radius * 0.5f;
        Rig rig = c.Rig;
        if (rig == null) return best;
        if (c.JawsAtRest > 0f) return c.JawsAtRest;
        Vector3 f = c.Space != null ? WildSim.Flat(-c.Facing.Z) : c.Forward;
        Vector3 from = c.Space != null ? Crawl.Center(c) : c.Position;
        for (int i = 0; i < rig.HitCount; i++)
        {
            Vector4 s = rig.Hits[i];
            best = MathF.Max(best, WildSim.Flat(new Vector3(s.X, s.Y, s.Z) - from).Dot(f) + s.W);
        }
        // Once, standing: reared for a bite its head is drawn back, and that isn't how far it reaches.
        if (!c.Attacking && c.Rear < 0.05f && c.Age > 0.1f) c.JawsAtRest = best;
        return best;
    }

    /// <summary>How close it has to be to start a bite that will reach: no nearer than its jaws let it.</summary>
    public static float Range(Creature c) => Spitting.Can(c) ? Spitting.ReachOf(c.Species) : MathF.Max(c.Stats.AttackRange, Jaws(c) + Stretch(c) + BodyRadius + 0.2f);

    /// <summary>How far a lunge's pose throws its head forward, on top of its jaws at rest.</summary>
    public static float Stretch(Creature c) => c.Species.Rig == RigKind.Legged ? c.Species.Body.Size * 0.3f : c.Species.Body.Size * 0.08f;

    /// <summary>
    /// Which way from its aim point a lunge ends: back the way it came, unless that is behind the
    /// victim, when it swings round toward their front.
    /// </summary>
    public static Vector3 Approach(Creature c, ITarget t, Vector3 from, Vector3 aim)
    {
        Vector3 side = WildSim.Flat(from - aim);
        side = side.LengthSquared() > 1e-4f ? side.Normalized() : -c.Forward;
        Vector3 front = WildSim.Flat(t.Forward);
        if (t is CreatureTarget || front.LengthSquared() < 1e-4f) return side;
        front = front.Normalized();
        float angle = MathF.Atan2(front.Cross(side).Y, front.Dot(side));
        if (MathF.Abs(angle) <= Front) return side;
        return side.Rotated(Vector3.Up, -MathF.Sign(angle) * MathF.Min(MathF.Abs(angle) - Front, Swing));
    }

    /// <summary>Begins an attack on a target in reach. Returns false if it can't attack now.</summary>
    public static bool Start(WildSim w, Creature c, ITarget target)
    {
        if (c.Dead || c.Attacking || target == null || !target.Attackable || c.Stats.Damage <= 0f) return false;
        CombatRules r = w.Combat;
        bool first = c.MustTelegraph(target, w.Time, r);
        float windup = first ? c.Species.TelegraphWindup(r) : r.NormalWindup;
        if (c.Species.Flight == FlightKind.Glide) windup = MathF.Max(windup, Swoop.LeastTell(c.Species));
        if (c.Pack is { } pack)
        {
            // Wait for the pack's turn: nobody else winding up, and this commit a full
            // telegraph after the previous one.
            float commitAt = w.Time + r.CommitFraction * windup;
            if (pack.Busy(c) || commitAt < pack.NextCommitAt) return false;
            pack.NextCommitAt = commitAt + c.Species.TelegraphWindup(r) + Reaction.Hesitation(c); // the next waits its turn, and a beat more
        }
        c.Phase = AttackPhase.Windup;
        c.PhaseTime = 0f;
        c.PhaseLength = c.WindupLength = windup;
        c.FirstAttack = first;
        c.Committed = false;
        c.AttackTarget = target;
        c.AimPoint = target.Position;
        w.RaiseTelegraph(new AttackEvent { Creature = c, Target = target, First = first, Windup = c.WindupLength, Time = w.Time });
        return true;
    }

    public static void Cancel(Creature c)
    {
        c.Phase = AttackPhase.None;
        c.ThinkEvery = 0f;
        c.Rear = 0f;
        c.Brace = 0f;
        c.AirAimOn = false;
    }

    /// <summary>Part way through a wind-up it locks onto where you stand, and says so.</summary>
    internal static void CommitIfDue(WildSim w, Creature c, ITarget t)
    {
        if (c.Committed || c.PhaseTime < w.Combat.CommitFraction * c.PhaseLength) return;
        c.Committed = true;
        c.AimPoint = t.Position;
        w.RaiseCommitted(new AttackEvent { Creature = c, Target = t, First = c.FirstAttack, Windup = c.WindupLength, Time = w.Time });
    }

    public static void Update(WildSim w, Creature c, float dt)
    {
        if (c.Phase == AttackPhase.None)
        {
            if (c.State != CState.Warn) c.Rear = Mathf.MoveToward(c.Rear, 0f, dt * 3f); // a giant's warning display rears too
            c.Lunge = Mathf.MoveToward(c.Lunge, 0f, dt * 4f);
            c.Brace = Mathf.MoveToward(c.Brace, 0f, dt * 3f);
            return;
        }

        CombatRules r = w.Combat;
        ITarget t = c.AttackTarget;
        c.PhaseTime += dt;
        if (c.Phase == AttackPhase.Hold)
        {
            Seizing.Hold(w, c, dt);
            return;
        }
        if (c.Species.Flight == FlightKind.Glide && c.Space == null)
        {
            Swoop.Update(w, c, t, dt);
            return;
        }
        switch (c.Phase)
        {
            case AttackPhase.Windup:
                if (!t.Attackable && !c.Committed)
                {
                    Cancel(c); // they climbed into the hovercraft: nothing to bite
                    return;
                }
                c.Desired = Vector3.Zero;
                if (!c.Committed) c.AimPoint = t.Position;
                c.FacePoint = c.AimPoint;
                WindupPose(c, Math.Clamp(c.PhaseTime / c.PhaseLength, 0f, 1f));
                CommitIfDue(w, c, t);
                if (c.PhaseTime >= c.PhaseLength)
                {
                    if (Spitting.Wants(c))
                    {
                        // A throat, not teeth: it lets go from where it stands.
                        Spitting.Launch(w, c);
                        c.Phase = AttackPhase.Recover;
                        c.PhaseTime = 0f;
                        c.PhaseLength = r.AttackCooldown * 1.6f;
                        c.Rear = 0f;
                    }
                    else Launch(w, c);
                }
                break;

            case AttackPhase.Lunge:
                c.Lunge = 1f;
                c.Desired = Vector3.Zero;
                if (c.PhaseTime >= c.PhaseLength) Land(w, c, t);
                break;

            case AttackPhase.Recover:
                c.Lunge = Mathf.MoveToward(c.Lunge, 0f, dt * 3f);
                c.Desired = c.PhaseTime < 0.5f ? -c.Forward * c.Stats.WalkSpeed * 0.6f : Vector3.Zero;
                if (c.PhaseTime >= c.PhaseLength)
                {
                    c.Phase = AttackPhase.None;
                    c.ThinkEvery = 0f; // back to its own mind at once
                }
                break;
        }
    }

    /// <summary>
    /// What the wind-up looks like, by style; k runs 0 to 1 over it. The brain clears the pose
    /// intentions every step before this runs, so they hold only while winding up.
    /// </summary>
    static void WindupPose(Creature c, float k)
    {
        switch (c.Species.Attack.Style)
        {
            case AttackStyle.Pounce:
                // Down low and coiled, a slight lift of the head at the very end.
                c.WantCrouch = 1f;
                c.Rear = 0.25f * k * k;
                break;
            case AttackStyle.Charge:
                c.Brace = MathF.Min(1f, k * 1.6f);
                c.Rear = 0f;
                break;
            case AttackStyle.Drop:
                // Flattens to the ceiling, legs bunching, head swinging down at you.
                c.WantCrouch = 1f;
                c.Rear = 0.5f * k;
                break;
            case AttackStyle.Seize:
                // Arms coming up for the head, the worms already reaching.
                c.Rear = 0.6f * k;
                c.Stir = 1f;
                break;
            default:
                c.Rear = k;
                break;
        }
    }

    /// <summary>Launches at the committed aim point, stopping with the jaws on it.</summary>
    static void Launch(WildSim w, Creature c)
    {
        // A Husk goes for the head as often as not; otherwise it swings an arm.
        c.Seizing = c.Species.Attack.Style == AttackStyle.Seize && Seizing.CanReach(w, c) && c.Rng.Chance(w.Data.Husks.SeizeShare);
        if (c.Space != null)
        {
            Crawl.Launch(w, c);
            return;
        }
        CombatRules r = w.Combat;
        AttackDef a = c.Species.Attack;
        Vector3 aim = c.AimPoint;
        // It stops with its jaws on the aim point, at the skin of whoever stands there.
        float stop = Jaws(c) + Stretch(c) + (c.AttackTarget is CreatureTarget ? 0f : BodyRadius);
        Vector3 go = WildSim.Flat(aim + Approach(c, c.AttackTarget, c.Position, aim) * stop - c.Position);
        float d = go.Length();
        Vector3 along = d > 0.01f ? go / d : c.Forward;
        float travel = MathF.Min(d, c.Stats.AttackRange + (a.Reach > 0f ? a.Reach : r.LungeReach));
        c.LungeFrom = c.Position;
        c.LungeTo = c.Position + along * travel;
        Vector3 at = WildSim.Flat(aim - c.LungeTo);
        Vector3 dir = at.LengthSquared() > 1e-4f ? at.Normalized() : c.Forward;
        c.StrikePoint = c.LungeTo + dir * MathF.Min(stop, at.Length()); // someone already inside its reach is bitten where they stand
        c.Yaw = MathF.Atan2(-dir.X, -dir.Z);
        c.Phase = AttackPhase.Lunge;
        c.PhaseTime = 0f;
        c.PhaseLength = MathF.Max(a.Lunge > 0f ? a.Lunge : r.LungeSeconds, travel / (c.Species.Flight == FlightKind.Hover ? MostLungeSpeed : MathF.Min(MostLungeSpeed, c.Species.Body.LegLength * 3.5f + 2f)));
        c.Rear = 0f;
        c.Brace = 0f;
    }

    static void Land(WildSim w, Creature c, ITarget t)
    {
        CombatRules r = w.Combat;
        bool hit;
        if (c.Space != null)
        {
            // In a cave or a place the lunge is a flight through the open: judged where it ends.
            Vector3 end = c.StrikePoint;
            float rise = end.Y - t.Position.Y;
            hit = t.Attackable
                  && WildSim.Flat(t.Position - end).Length() <= r.ContactRadius + c.Radius * 0.4f
                  && rise > -0.8f && rise < 2.3f;
        }
        else
        {
            Vector3 strike = c.StrikePoint;
            float groundAtStrike = w.Terrain.Height(strike.X, strike.Z);
            Vector3 off = t.Position - strike;
            hit = t.Attackable
                  && WildSim.Flat(off).Length() <= r.ContactRadius + c.Radius * 0.4f
                  && t.Position.Y - groundAtStrike < 2.2f; // a jump at the right moment clears a low lunge
        }
        if (hit && c.Seizing)
        {
            // It has them by the head.
            Seizing.Begin(w, c, t);
            return;
        }
        Deliver(w, c, t, hit);
        if (c.Species.Flight == FlightKind.Hover) c.Velocity = Vector3.Zero; // a sting stops dead at the skin: what carried it in doesn't carry it through
        if (c.Space != null)
        {
            Crawl.Release(w, c);
            // A drop has spent itself on what it hit: it falls the rest of the way straight.
            if (c.Species.Attack.Style == AttackStyle.Drop)
            {
                c.Velocity = new Vector3(0f, c.Velocity.Y, 0f);
                c.Dropping = true;
            }
        }
        c.Phase = AttackPhase.Recover;
        c.PhaseTime = 0f;
        c.PhaseLength = r.AttackCooldown;
    }

    /// <summary>A blow lands or doesn't: the damage, and everyone told. What the body does next is its own business.</summary>
    internal static void Deliver(WildSim w, Creature c, ITarget t, bool hit)
    {
        float dealt = 0f;
        if (hit)
        {
            // Another creature takes its wounds through the sim (it may die and be eaten).
            if (t is CreatureTarget prey)
            {
                float before = prey.Creature.Health;
                w.Damage(prey.Creature, c.Stats.Damage, c.AsTarget);
                dealt = before - MathF.Max(0f, prey.Creature.Health);
            }
            else dealt = t.Health.Apply(c.Stats.Damage);
        }
        c.RememberAttack(t, w.Time);
        w.RaiseAttacked(new AttackEvent
        {
            Creature = c, Target = t, First = c.FirstAttack, Windup = c.WindupLength, Hit = hit, Damage = dealt, Time = w.Time,
        });
    }
}
