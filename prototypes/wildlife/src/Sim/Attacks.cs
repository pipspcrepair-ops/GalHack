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
/// - Pack members take turns: one may wind up at a time, and each commit comes at least a
///   telegraph after the last, so a pack never lands several lunges at once.
/// </summary>
public static class Attacks
{
    /// <summary>Begins an attack on a target in reach. Returns false if it can't attack now.</summary>
    public static bool Start(WildSim w, Creature c, ITarget target)
    {
        if (c.Dead || c.Attacking || target == null || !target.Attackable || c.Stats.Damage <= 0f) return false;
        CombatRules r = w.Combat;
        bool first = c.MustTelegraph(target, w.Time, r);
        float windup = first ? c.Species.TelegraphWindup(r) : r.NormalWindup;
        if (c.Pack is { } pack)
        {
            // Wait for the pack's turn: nobody else winding up, and this commit a full
            // telegraph after the previous one.
            float commitAt = w.Time + r.CommitFraction * windup;
            if (pack.Busy(c) || commitAt < pack.NextCommitAt) return false;
            pack.NextCommitAt = commitAt + c.Species.TelegraphWindup(r);
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
        c.Rear = 0f;
        c.Brace = 0f;
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
                if (!c.Committed && c.PhaseTime >= r.CommitFraction * c.PhaseLength)
                {
                    c.Committed = true;
                    c.AimPoint = t.Position;
                    w.RaiseCommitted(new AttackEvent { Creature = c, Target = t, First = c.FirstAttack, Windup = c.WindupLength, Time = w.Time });
                }
                if (c.PhaseTime >= c.PhaseLength) Launch(w, c);
                break;

            case AttackPhase.Lunge:
                c.Lunge = 1f;
                c.Desired = Vector3.Zero;
                if (c.PhaseTime >= c.PhaseLength) Land(w, c, t);
                break;

            case AttackPhase.Recover:
                c.Lunge = Mathf.MoveToward(c.Lunge, 0f, dt * 3f);
                c.Desired = c.PhaseTime < 0.5f ? -c.Forward * c.Stats.WalkSpeed * 0.6f : Vector3.Zero;
                if (c.PhaseTime >= c.PhaseLength) c.Phase = AttackPhase.None;
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
            default:
                c.Rear = k;
                break;
        }
    }

    /// <summary>Launches at the committed aim point, stopping with the jaws on it.</summary>
    static void Launch(WildSim w, Creature c)
    {
        if (c.Space != null)
        {
            Crawl.Launch(w, c);
            return;
        }
        CombatRules r = w.Combat;
        Vector3 to = WildSim.Flat(c.AimPoint - c.Position);
        float d = to.Length();
        Vector3 dir = d > 0.01f ? to / d : c.Forward;
        AttackDef a = c.Species.Attack;
        float travel = Math.Clamp(d - c.Radius * 0.5f, 0f, c.Stats.AttackRange + (a.Reach > 0f ? a.Reach : r.LungeReach));
        c.LungeFrom = c.Position;
        c.LungeTo = c.Position + dir * travel;
        c.Yaw = MathF.Atan2(-dir.X, -dir.Z);
        c.Phase = AttackPhase.Lunge;
        c.PhaseTime = 0f;
        c.PhaseLength = a.Lunge > 0f ? a.Lunge : r.LungeSeconds;
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
            Vector3 d3 = c.LungeTo - c.LungeFrom;
            Vector3 along = d3.LengthSquared() > 1e-4f ? d3.Normalized() : -c.Facing.Z;
            Vector3 end = c.LungeTo + along * c.Radius * 0.3f;
            float rise = end.Y - t.Position.Y;
            hit = t.Attackable
                  && WildSim.Flat(t.Position - end).Length() <= r.ContactRadius + c.Radius * 0.4f
                  && rise > -0.8f && rise < 2.3f;
        }
        else
        {
            Vector3 dir = WildSim.Flat(c.LungeTo - c.LungeFrom);
            dir = dir.LengthSquared() > 1e-4f ? dir.Normalized() : c.Forward;
            Vector3 strike = c.LungeTo + dir * c.Radius * 0.5f;
            float groundAtStrike = w.Terrain.Height(strike.X, strike.Z);
            Vector3 off = t.Position - strike;
            hit = t.Attackable
                  && WildSim.Flat(off).Length() <= r.ContactRadius + c.Radius * 0.4f
                  && t.Position.Y - groundAtStrike < 2.2f; // a jump at the right moment clears a low lunge
        }
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
        if (c.Space != null) Crawl.Release(w, c);
        c.RememberAttack(t, w.Time);
        w.RaiseAttacked(new AttackEvent
        {
            Creature = c, Target = t, First = c.FirstAttack, Windup = c.WindupLength, Hit = hit, Damage = dealt, Time = w.Time,
        });
        c.Phase = AttackPhase.Recover;
        c.PhaseTime = 0f;
        c.PhaseLength = r.AttackCooldown;
    }
}
