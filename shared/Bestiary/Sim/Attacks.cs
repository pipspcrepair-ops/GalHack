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
/// - A blow lands only when the striking part meets the victim's body (<see cref="Strikes"/>):
///   the jaws or head, a stomping foot, a swept tail, a fist. Miss that and nothing is dealt, however
///   close the pose looked. Damage is dealt in full; nothing is capped.
/// - The tell is short and readable (a posture snap, a lowered head, a lifted foot, a sound; 0.4 to
///   0.8 s by weight), the strike fast and committed: jaws open in the lunge and snap shut on contact,
///   or at its end.
/// - Each species attacks in its behaviour's signature style: a pounce crouches low, a charge
///   lowers its head and paws, a giant drops its head to come down on you. The style changes the
///   look and sound only; the timing rules are the same for all.
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

    /// <summary>The same for one target: someone with a gun (rounds and a clear line, <see cref="Arming"/>) shoots from as far as it reaches; a horned giant charges from far off, a heavy tail swipes at someone behind.</summary>
    public static float Range(WildSim w, Creature c, ITarget t)
    {
        float shoot = Arming.ShootReach(c, t), near = Strikes.Reach(w, c, t, Range(c));
        return shoot > 0f ? MathF.Max(shoot, near) : near;
    }

    /// <summary>Whether this attack is a shot: it carries a gun that reaches, and the target isn't already within its blow.</summary>
    static bool Shooting(Creature c, ITarget t) => t != null && Arming.ShootReach(c, t) > 0f && WildSim.Flat(t.Position - c.Position).Length() > Range(c) + 0.3f;

    /// <summary>How far a lunge's pose throws its head forward, on top of its jaws at rest.</summary>
    public static float Stretch(Creature c) => c.Species.Rig == RigKind.Legged ? c.Species.Body.Size * 0.3f : c.Species.Body.Size * 0.08f;

    /// <summary>
    /// Which way from its aim point a lunge ends: back the way it came, unless that is behind the
    /// victim, when it swings round toward their front (not the opener, which comes straight).
    /// </summary>
    public static Vector3 Approach(Creature c, ITarget t, Vector3 from, Vector3 aim)
    {
        Vector3 side = WildSim.Flat(from - aim);
        side = side.LengthSquared() > 1e-4f ? side.Normalized() : -c.Forward;
        Vector3 front = WildSim.Flat(t.Forward);
        if (t is CreatureTarget || front.LengthSquared() < 1e-4f) return side;
        front = front.Normalized();
        float angle = MathF.Atan2(front.Cross(side).Y, front.Dot(side));
        if (MathF.Abs(angle) <= Front || c.FirstAttack) return side; // the telegraphed opener comes straight, so stepping aside dodges it
        return side.Rotated(Vector3.Up, -MathF.Sign(angle) * MathF.Min(MathF.Abs(angle) - Front, Swing));
    }

    /// <summary>Begins an attack on a target in reach. Returns false if it can't attack now.</summary>
    public static bool Start(WildSim w, Creature c, ITarget target)
    {
        // (Nor from a body not drawn: whatever hurts you is seen to.)
        if (c.Dead || c.Attacking || !c.Shown || target == null || !target.Attackable || c.Stats.Damage <= 0f) return false;
        CombatRules r = w.Combat;
        bool first = c.MustTelegraph(target, w.Time, r);
        float windup = c.Species.Windup(r, first);
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
        c.Move = Strikes.Choose(w, c, target);
        Strikes.Local(c, target.Position, out _, out float side);
        c.StrikeLimb = side < 0f ? -1 : 1;
        c.StrikeYaw = c.Yaw;
        c.StrikeAt = target.Position + Vector3.Up;
        c.StrikeLanded = false;
        c.StrikerWasOn = false;
        c.Swipe = 0f;
        w.RaiseTelegraph(new AttackEvent { Creature = c, Target = target, First = first, Windup = c.WindupLength, Time = w.Time });
        return true;
    }

    public static void Cancel(Creature c)
    {
        c.Phase = AttackPhase.None;
        c.ThinkEvery = 0f;
        c.Rear = 0f;
        c.Brace = 0f;
        c.Gape = 0f;
        c.Swipe = 0f;
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
            c.Gape = Mathf.MoveToward(c.Gape, 0f, dt * 4f);
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
                if (c.Move == AttackMove.Swipe) Sweep(c, -0.3f * Math.Clamp(c.PhaseTime / c.PhaseLength * 2f, 0f, 1f)); // it turns a little away: the tail cocks
                CommitIfDue(w, c, t);
                if (c.PhaseTime >= c.PhaseLength)
                {
                    if (Arming.Fire != null && Shooting(c, t))
                    {
                        // A gun, not a lunge: it fires from where it stands, and waits as its weapon makes it.
                        float wait = Arming.Fire(w, c, t);
                        c.RememberAttack(t, w.Time);
                        c.Phase = AttackPhase.Recover;
                        c.PhaseTime = 0f;
                        c.PhaseLength = MathF.Max(0.15f, wait);
                        c.Rear = 0f;
                        c.Lunge = 0f;
                    }
                    else if (Spitting.Wants(c))
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
                c.Lunge = c.Move is AttackMove.Bite or AttackMove.Charge ? 1f : 0f;
                c.Desired = Vector3.Zero;
                c.LookAt = c.StrikeAt;
                float k = Math.Clamp(c.PhaseTime / c.PhaseLength, 0f, 1f);
                if (c.Move == AttackMove.Swipe) Sweep(c, Mathf.Lerp(-0.3f, 1.25f, k * k * (3f - 2f * k)));
                if (c.Move == AttackMove.Charge && c.Rig.Steps != c.StrikeSteps)
                {
                    c.StrikeSteps = c.Rig.Steps; // the ground pounds under a heavy charge
                    Strikes.Quake(w, c, c.Position, false, false, w.Combat.Strikes.QuakeFootfall);
                }
                float gap = float.NaN;
                if (c.Space != null || c.Species.Rig == RigKind.Swarm)
                {
                    if (c.PhaseTime >= c.PhaseLength) Land(w, c, t, LandingHit(w, c, t), gap);
                }
                else if (Strikes.Touch(w, c, t, out gap)) Land(w, c, t, true, gap);
                else if (c.Move is AttackMove.Bite or AttackMove.Charge && (c.LungeTo - c.LungeFrom).Dot(t.Position - c.Position) > 0f && Strikes.Bumped(c, t))
                    c.LungeFrom = c.LungeTo = c.Position; // its body met them first: it stops there (nothing passes through anyone), and its head still comes on
                if (c.Phase != AttackPhase.Lunge || c.Space != null || c.Species.Rig == RigKind.Swarm) break;
                if (c.Move == AttackMove.Charge && c.PhaseTime < c.PhaseLength * 0.9f && c.LungeTo == c.Position && c.LungeFrom != c.LungeTo && c.PhaseTime > 0.05f)
                {
                    // Ran into something solid: it stops hard and stands dazed.
                    Strikes.Quake(w, c, c.Position, false, false);
                    Land(w, c, t, false, gap);
                    c.PhaseLength = w.Combat.Strikes.ChargeDazed;
                    c.Rear = 0.5f;
                }
                else if (c.PhaseTime >= c.PhaseLength + r.FollowThrough) Land(w, c, t, false, gap);
                break;

            case AttackPhase.Recover:
                c.Lunge = Mathf.MoveToward(c.Lunge, 0f, dt * 3f);
                c.Swipe = Mathf.MoveToward(c.Swipe, 0f, dt * 2f);
                if (c.PhaseTime > 0.15f) c.Gape = Mathf.MoveToward(c.Gape, 0f, dt * 4f);
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
    /// What the wind-up looks like, by move and style; k runs 0 to 1 over it. A tell is a snap
    /// into a posture, held, not a slow lift: it's there by a third of the way. The brain clears
    /// the pose intentions every step before this runs, so they hold only while winding up.
    /// </summary>
    static void WindupPose(Creature c, float k)
    {
        float snap = MathF.Min(1f, k * 3f);
        switch (c.Move)
        {
            case AttackMove.Charge:
                c.Brace = snap; // head down, a foot pawing, a snort
                return;
            case AttackMove.Stomp:
                c.Rear = 0.35f * snap; // the forequarters come up off the stomping foot
                return;
            case AttackMove.Swipe:
                return;
        }
        if (Mouthed(c)) c.Gape = c.Committed ? 1f : 0.3f * snap; // lips back, then wide as it commits
        switch (c.Species.Attack.Style)
        {
            case AttackStyle.Pounce:
                // Down low and coiled.
                c.WantCrouch = snap;
                c.Rear = 0.15f * snap;
                break;
            case AttackStyle.Charge:
            case AttackStyle.Rear:
                // The head drops to lead the blow: a giant brings it down on you, never up and back.
                c.Brace = snap;
                c.WantCrouch = 0.4f * snap;
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
                // A quick coil: drawn in and low, ready to throw itself.
                c.Rear = 0.2f * snap;
                c.WantCrouch = 0.5f * snap;
                break;
        }
    }

    /// <summary>Whether it bites with a mouth of some kind (jaws, beak, pincers, a round maw), not only a head.</summary>
    static bool Mouthed(Creature c) => c.Species.Part("head") != "none" && c.Species.Figure == null;

    /// <summary>A swipe's sweep, -1..1 and past: the body turns into it and the tail goes round to the victim's side.</summary>
    static void Sweep(Creature c, float amount)
    {
        c.Swipe = amount * c.StrikeLimb;
        c.Yaw = c.StrikeYaw + 0.55f * c.Swipe;
        c.FacePoint = c.Position + Geo.Forward(c.Yaw, 0f) * 10f;
    }

    /// <summary>Launches at the committed aim point, stopping with the jaws on it.</summary>
    static void Launch(WildSim w, Creature c)
    {
        // A Husk goes for the head as often as not; otherwise it swings an arm (always, just after someone threw a hold off).
        c.Seizing = c.Species.Attack.Style == AttackStyle.Seize && Seizing.Free(w, c.AttackTarget) && Seizing.CanReach(w, c, c.AttackTarget) && c.Rng.Chance(w.Data.Husks.SeizeShare);
        if (c.Space != null)
        {
            Crawl.Launch(w, c);
            c.StrikerWasOn = false;
            if (Mouthed(c)) c.Gape = 1f;
            return;
        }
        CombatRules r = w.Combat;
        AttackDef a = c.Species.Attack;
        Vector3 aim = c.AimPoint;
        c.Phase = AttackPhase.Lunge;
        c.PhaseTime = 0f;
        c.Rear = 0f;
        c.Brace = 0f;
        c.LungeFrom = c.LungeTo = c.Position;
        c.StrikeSteps = c.Rig.Steps;
        // Where on the victim the striking part goes: the nearest of their body to it (a giant's head comes down to the chest, a small hunter's goes for the legs).
        Vector4 part = c.Rig.Striker(c);
        // ...at their skin, on the side it comes from, so it drives up to them and not through.
        Vector3 back = WildSim.Flat(c.Position - aim);
        c.StrikeAt = aim + Vector3.Up * Math.Clamp(part.Y - aim.Y, 0.5f, c.AttackTarget.Crouched ? 0.9f : 1.3f)
                     + (back.LengthSquared() > 1e-4f && c.AttackTarget is not CreatureTarget ? back.Normalized() * BodyRadius : Vector3.Zero);
        switch (c.Move)
        {
            case AttackMove.Stomp:
                // It stands and brings the lifted foot down where they are.
                c.StrikePoint = c.StrikeAt = new Vector3(aim.X, w.Terrain.Height(aim.X, aim.Z), aim.Z);
                c.PhaseLength = r.Strikes.StompDown;
                return;
            case AttackMove.Swipe:
                c.PhaseLength = r.Strikes.SwipeTime;
                return;
            case AttackMove.Charge:
            {
                // Straight at them and on past, horns down, as fast as its weight lets it; they can step out of the line.
                Vector3 run = WildSim.Flat(aim - c.Position);
                float d0 = run.Length();
                Vector3 line = d0 > 0.01f ? run / d0 : c.Forward;
                float carry = d0 + c.Species.Body.Size * r.Strikes.ChargeOvershoot;
                c.LungeTo = c.Position + line * carry;
                c.StrikePoint = aim;
                c.Yaw = MathF.Atan2(-line.X, -line.Z);
                c.PhaseLength = carry / MathF.Max(2f, c.Stats.RunSpeed * r.Strikes.ChargeSpeed);
                c.Gape = 0.4f;
                return;
            }
        }
        if (Mouthed(c)) c.Gape = 1f; // jaws wide for the bite
        // It drives its jaws into the aim point, so they meet whoever stands there; contact stops it (a body
        // that strikes with whatever part is nearest can go deeper: that part meets them first).
        float stop = c.Rig.Reach(c, c.StrikeAt.Y) + (c.AttackTarget is CreatureTarget ? 0f : BodyRadius) - (c.Species.Rig == RigKind.Legged ? 0.65f : 1f);
        Vector3 go = WildSim.Flat(aim + Approach(c, c.AttackTarget, c.Position, aim) * stop - c.Position);
        float d = go.Length();
        Vector3 along = d > 0.01f ? go / d : c.Forward;
        float travel = MathF.Min(d, c.Stats.AttackRange + (a.Reach > 0f ? a.Reach : r.LungeReach));
        c.LungeFrom = c.Position;
        c.LungeTo = c.Position + along * travel;
        Vector3 at = WildSim.Flat(aim - c.LungeTo);
        Vector3 dir = at.LengthSquared() > 1e-4f ? at.Normalized() : c.Forward;
        c.StrikePoint = c.LungeTo + dir * Math.Clamp(stop, 0f, at.Length()); // someone already inside its reach is bitten where they stand
        c.Yaw = MathF.Atan2(-dir.X, -dir.Z);
        c.PhaseLength = MathF.Max(a.Lunge > 0f ? a.Lunge : r.LungeSeconds, travel / (c.Species.Flight == FlightKind.Hover ? MostLungeSpeed : MathF.Min(MostLungeSpeed, c.Species.Body.LegLength * 3.5f + 2f)));
    }

    /// <summary>
    /// Judged where the lunge ends, for what has no one striking part: a swarm stings wherever its
    /// cloud comes down, and in a cave or a place the lunge is a flight through the open (the rigs
    /// there don't all pose on the place's floor yet, so their drawn parts can't be the judge).
    /// </summary>
    static bool LandingHit(WildSim w, Creature c, ITarget t)
    {
        Vector3 end = c.StrikePoint;
        float rise = end.Y - t.Position.Y, near = w.Combat.ContactRadius + c.Radius * 0.4f;
        if (c.Space == null) return t.Attackable && WildSim.Flat(t.Position - end).Length() <= near && t.Position.Y - w.Terrain.Height(end.X, end.Z) < 2.2f;
        return t.Attackable
               && WildSim.Flat(t.Position - end).Length() <= near
               && rise > -0.8f && rise < 2.3f
               && SpaceMath.Clear(c.Space, end, t.Position + Vector3.Up * (BodyHeight * 0.5f)); // never through a wall, floor or ceiling
    }

    /// <summary>The strike is over: it met the victim (the moment its part touched them) or it didn't, and the jaws snap shut either way.</summary>
    static void Land(WildSim w, Creature c, ITarget t, bool hit, float gap)
    {
        CombatRules r = w.Combat;
        c.StrikeLanded = hit;
        if (Mouthed(c) && c.Move is AttackMove.Bite or AttackMove.Charge) c.Gape = -1f;
        if (c.Move == AttackMove.Stomp)
        {
            Vector4 foot = c.Rig.Striker(c);
            Strikes.Quake(w, c, new Vector3(foot.X, 0f, foot.Z), true, hit); // hit or miss, the ground takes it: a thud, a shake, dust
        }
        // A seize needs room where it lands, as it did when it set off; without it, it's a blow.
        if (hit && c.Seizing && Seizing.Free(w, t) && Seizing.CanReach(w, c, t))
        {
            // It has them by the head.
            Seizing.Begin(w, c, t);
            return;
        }
        Deliver(w, c, t, hit, gap);
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
        // A charge that missed has to pull up and come round, slowly for a big body: an opening.
        c.PhaseLength = r.AttackCooldown + (c.Move == AttackMove.Charge && !hit ? c.Species.Body.Size * 0.25f : 0f);
    }

    /// <summary>What its blow deals: its hand weapon's (or bare hands') where it's a person or machine that carries one, else its body's own.</summary>
    public static float BlowOf(Creature c) => Arming.BlowDamage?.Invoke(c) is float b && b >= 0f ? b : c.Stats.Damage;

    /// <summary>A blow lands or doesn't: the damage, and everyone told. What the body does next is its own business.</summary>
    internal static void Deliver(WildSim w, Creature c, ITarget t, bool hit, float gap = float.NaN)
    {
        float dealt = 0f, blow = BlowOf(c) * Strikes.DamageShare(w, c);
        if (hit)
        {
            // Another creature takes its wounds through the sim (it may die and be eaten).
            if (t is CreatureTarget prey)
            {
                float before = prey.Creature.Health;
                w.Damage(prey.Creature, blow, c.AsTarget);
                dealt = before - MathF.Max(0f, prey.Creature.Health);
            }
            else dealt = t.Health.Apply(blow);
        }
        c.RememberAttack(t, w.Time);
        w.RaiseAttacked(new AttackEvent
        {
            Creature = c, Target = t, First = c.FirstAttack, Windup = c.WindupLength, Hit = hit, Damage = dealt, Time = w.Time,
            Move = c.Move, Gap = gap, Push = hit ? Strikes.Push(w, c, t) : Vector3.Zero,
        });
    }
}
