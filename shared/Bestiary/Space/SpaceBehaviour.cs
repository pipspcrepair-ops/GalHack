using System;
using Godot;

namespace Wildlife;

/// <summary>
/// What space creatures do. Three jobs, by behaviour:
/// - Shoals (drifting grazers, belt rays, sky mantas): drift together round a slowly wandering
///   goal, graze, scatter as one from anything that comes too close, and turn on whatever hurt
///   them if they are bold enough.
/// - Hull leeches (ambushers): crawl over a derelict's hull, freeze and watch anything that comes
///   near, rear off the hull and strike at whatever comes within reach, then drift back to the
///   spot they left and latch on again.
/// - The leviathan (a territorial giant): cruises a wide loop round its home, warns intruders off
///   with a display, then turns and charges anyone who stays.
/// The brain only writes intentions; <see cref="SpaceAttacks"/> and <see cref="SpaceMotion"/> act on them.
/// </summary>
public static class SpaceBrain
{
    public static void Update(SpaceSim w, Creature c, float dt)
    {
        c.StateTime += dt;
        c.FacePoint = null;
        c.LookAt = null;
        c.WantGraze = 0f;
        if (c.Attacking)
        {
            c.LookAt = c.AttackTarget?.Position;
            return;
        }
        ITarget near = w.NearestTarget(c.Position, out float dist);
        c.Alert = near != null && dist < c.Stats.Perception ? 1f : 0f;
        if (c.Alert > 0f) c.LookAt = near.Position;

        switch (c.Species.Behaviour)
        {
            case Behaviour.Ambusher: Leech(w, c, near, dist, dt); break;
            case Behaviour.TerritorialGiant: Leviathan(w, c, near, dt); break;
            default: Shoal(w, c, near, dist, dt); break;
        }
    }

    /// <summary>How close a target must be for this creature to start its attack.</summary>
    public static float StrikeRange(Creature c, CombatRules r) => c.Stats.AttackRange + Reach(c, r) * 0.6f;

    public static float Reach(Creature c, CombatRules r) => c.Species.Attack.Reach > 0f ? c.Species.Attack.Reach : r.LungeReach;

    // ---- Shoals -------------------------------------------------------------------------

    static void Shoal(SpaceSim w, Creature c, ITarget near, float dist, float dt)
    {
        Herd h = c.Herd;
        if (h != null) UpdateShoal(w, h, dt);
        ITarget provoker = Provoked(w, c);
        if (provoker != null && c.Stats.Retaliates) { Hunt(w, c, provoker); return; }
        if (c.Stats.Hunts && near != null && near.Attackable && dist < c.Stats.Perception) { Hunt(w, c, near); return; }
        bool threatened = near != null && dist < c.Stats.FleeRadius * near.ThreatScale;
        if (threatened || provoker != null)
        {
            Vector3 from = provoker?.Position ?? near.Position;
            if (h != null) h.Alarm(from, w.Time);
            else
            {
                c.SetState(CState.Flee);
                c.Desired = Away(c.Position, from, c.Forward) * c.Stats.RunSpeed;
                return;
            }
        }

        Vector3 goal = h?.Goal ?? c.Home;
        float spread = h?.Spread ?? c.Species.Body.Size;
        int n = Math.Max(1, h?.Members.Count ?? 1);
        Vector3 slot = goal + SpaceSim.SlotOffset(c.HerdSlot, n) * spread;
        Vector3 toSlot = slot - c.Position;
        bool alarmed = h is { Alarmed: true };
        float speed = alarmed ? c.Stats.RunSpeed : toSlot.Length() > spread * 0.25f + 1f ? c.Stats.WalkSpeed : 0f;
        Vector3 steer = speed > 0f && toSlot.LengthSquared() > 1e-4f ? toSlot.Normalized() * speed : Vector3.Zero;

        Vector3 separation = Vector3.Zero;
        if (h != null)
            foreach (Creature m in h.Members)
            {
                if (m == c || m.Dead || m.Removed) continue;
                Vector3 away = c.Position - m.Position;
                float d = away.Length(), keep = (c.Radius + m.Radius) * 1.6f;
                if (d < keep && d > 1e-3f) separation += away / d * (keep - d) / keep;
            }
        c.Desired = steer + separation * c.Stats.WalkSpeed * 1.5f;
        if (alarmed) c.Desired += h.Velocity * 0.3f;

        bool still = c.Desired.Length() < 0.3f;
        c.SetState(alarmed ? CState.Flee : still ? CState.Graze : CState.Wander);
        if (still)
        {
            c.WantGraze = Mathf.PosMod(c.Age * 0.11f + c.HerdSlot * 0.37f, 1f) < 0.55f ? 1f : 0f;
            if (NearestRock(w, c.Position, 30f) is { } rock) c.FacePoint = rock.Center; // belt rays graze the rock beside them
        }
    }

    /// <summary>Shoal-wide state, once per step: the middle, the alarm and the wandering goal.</summary>
    static void UpdateShoal(SpaceSim w, Herd h, float dt)
    {
        if (h.UpdatedFrame == w.Frame) return;
        h.UpdatedFrame = w.Frame;
        Vector3 sum = Vector3.Zero, vel = Vector3.Zero;
        int alive = 0;
        foreach (Creature m in h.Members)
        {
            if (m.Dead || m.Removed) continue;
            sum += m.Position;
            vel += m.Velocity;
            alive++;
        }
        if (alive == 0) return;
        h.Center = sum / alive;
        h.Velocity = vel / alive;
        Species s = h.Members[0].Species;

        if (h.Alarmed)
        {
            ITarget near = w.NearestTarget(h.Center, out float d);
            if (near != null && d < s.Stats.FleeRadius * 1.6f * near.ThreatScale)
            {
                h.ThreatPos = near.Position;
                h.AlarmedAt = w.Time;
            }
            h.Goal = h.Center + Away(h.Center, h.ThreatPos, Vector3.Forward) * (h.Spread + 30f);
            if (w.Time - h.AlarmedAt > 6f)
            {
                h.Alarmed = false;
                h.Home = h.Center; // settle where they ended up
                h.Goal = h.Center;
                h.GoalTimer = 8f;
            }
            return;
        }

        h.GoalTimer -= dt;
        if (h.GoalTimer > 0f) return;
        var rng = h.Members[0].Rng;
        h.GoalTimer = rng.Range(14f, 30f);
        // Belt rays move from rock to rock and graze beside them; the rest drift about home.
        if (s.Habitat == Habitat.AsteroidField && NearestRock(w, h.Home + RandomIn(rng, 60f), 160f) is { } rock)
        {
            Vector3 side = RandomIn(rng, 1f);
            side = side.LengthSquared() > 1e-3f ? side.Normalized() : Vector3.Up;
            h.Goal = rock.Center + side * (rock.Radius + h.Spread + s.Body.Size);
        }
        else h.Goal = h.Home + RandomIn(rng, 50f) * new Vector3(1f, 0.5f, 1f);
    }

    // ---- Hull leeches -----------------------------------------------------------------

    static void Leech(SpaceSim w, Creature c, ITarget near, float dist, float dt)
    {
        ITarget provoker = Provoked(w, c);
        if (c.Clinging)
        {
            if (near != null && near.Attackable && dist <= StrikeRange(c, w.Combat))
            {
                c.FacePoint = near.Position;
                SpaceAttacks.Start(w, c, near);
                return;
            }
            if (provoker != null && c.Stats.Retaliates)
            {
                c.ClingHull = null; // lets go to go after whatever hurt it
                Hunt(w, c, provoker);
                return;
            }
            if (near != null && dist < c.Stats.Perception)
            {
                // Something is coming: freeze flat, head turned towards it.
                c.SetState(CState.Lurk);
                c.FacePoint = near.Position;
                c.Desired = Vector3.Zero;
                return;
            }
            // Crawl about the spot, feeding on the hull.
            c.SetState(CState.Wander);
            if (!c.HasGoal || (c.Goal - c.Position).Length() < 0.4f || c.StateTime > 8f)
            {
                c.Goal = c.ClingHull.ClampToFace(c.ClingFace, c.Anchor + RandomIn(c.Rng, 3f), c.Radius);
                c.HasGoal = true;
                c.StateTime = 0f;
            }
            Vector3 to = c.Goal - c.Position;
            c.Desired = to.Length() > 0.3f ? to.Normalized() * c.Stats.WalkSpeed : Vector3.Zero;
            c.WantGraze = c.Desired == Vector3.Zero ? 1f : 0f;
            return;
        }

        // Adrift after a strike: go after whatever hurt it, else back to its spot and latch on.
        if (provoker != null && c.Stats.Retaliates && (provoker.Position - c.Position).Length() < c.Stats.Perception * 1.5f)
        {
            Hunt(w, c, provoker);
            return;
        }
        c.SetState(CState.ReturnHome);
        if (c.HomeHull == null) { c.Desired = Vector3.Zero; return; }
        Vector3 home = c.HomeHull.ClampToFace(c.HomeFace, c.Anchor, c.Radius);
        Vector3 back = home - c.Position;
        float d = back.Length();
        if (d < 0.35f)
        {
            SpaceSim.Cling(c, c.HomeHull, c.HomeFace);
            c.SetState(CState.Wander);
            return;
        }
        c.Desired = back / d * MathF.Min(c.Stats.RunSpeed * 0.5f, d * 2f);
    }

    // ---- The leviathan ----------------------------------------------------------------

    static void Leviathan(SpaceSim w, Creature c, ITarget near, float dt)
    {
        float territory = c.Stats.Territory;
        bool intruder = near != null && near.Attackable && (near.Position - c.Home).Length() < territory;
        ITarget provoker = Provoked(w, c);
        if (provoker != null && c.State != CState.Chase) c.SetState(CState.Chase);

        switch (c.State)
        {
            case CState.Warn:
                // The display: it slows, turns to face you and rears, glowing, for a few seconds.
                c.Desired = c.Velocity * 0.9f * MathF.Max(0f, 1f - c.StateTime);
                if (near != null) c.FacePoint = near.Position;
                c.Rear = MathF.Min(1f, c.StateTime / 1.2f);
                if (!intruder) c.SetState(CState.ReturnHome);
                else if (c.StateTime > 3.5f) c.SetState(CState.Chase);
                break;
            case CState.Chase:
            {
                ITarget prey = provoker ?? near;
                bool keep = prey != null && prey.Attackable && ((prey.Position - c.Home).Length() < territory * 1.5f || provoker != null);
                if (!keep) c.SetState(CState.ReturnHome);
                else Hunt(w, c, prey);
                break;
            }
            case CState.ReturnHome:
                if ((c.Home - c.Position).Length() < territory * 0.6f) c.SetState(CState.Wander);
                else c.Desired = (c.Home - c.Position).Normalized() * c.Stats.WalkSpeed;
                break;
            default:
            {
                // Cruising: a slow loop round home, swinging up and down a little.
                c.SetState(CState.Wander);
                float r = territory * 0.55f, a = c.Age * c.Stats.WalkSpeed / r + c.Id;
                Vector3 next = c.Home + new Vector3(MathF.Cos(a) * r, MathF.Sin(a * 0.5f) * r * 0.15f, MathF.Sin(a) * r);
                Vector3 to = next - c.Position;
                c.Desired = to.LengthSquared() > 1f ? to.Normalized() * c.Stats.WalkSpeed : Vector3.Zero;
                if (intruder)
                {
                    c.SetState(CState.Warn);
                    w.RaiseWarned(c);
                }
                break;
            }
        }
    }

    // ---- Shared verbs -----------------------------------------------------------------

    static void Hunt(SpaceSim w, Creature c, ITarget t)
    {
        if (t == null || !t.Attackable) { c.SetState(CState.Wander); return; }
        Vector3 to = t.Position - c.Position;
        float d = to.Length();
        if (c.State != CState.Chase) c.SetState(CState.Chase);
        c.FacePoint = t.Position;
        if (d <= StrikeRange(c, w.Combat))
        {
            c.Desired = Vector3.Zero;
            SpaceAttacks.Start(w, c, t);
        }
        else c.Desired = to / d * c.Stats.RunSpeed;
    }

    static ITarget Provoked(SpaceSim w, Creature c) =>
        c.ProvokedBy != null && w.Time - c.ProvokedAt < 12f && c.ProvokedBy.Attackable ? c.ProvokedBy : null;

    static Asteroid NearestRock(SpaceSim w, Vector3 p, float within)
    {
        Asteroid best = null;
        float bestD = within;
        foreach (Asteroid a in w.Asteroids)
        {
            float d = (a.Center - p).Length() - a.Radius;
            if (d < bestD) { bestD = d; best = a; }
        }
        return best;
    }

    static Vector3 Away(Vector3 from, Vector3 threat, Vector3 fallback)
    {
        Vector3 d = from - threat;
        return d.LengthSquared() > 1e-4f ? d.Normalized() : fallback;
    }

    static Vector3 RandomIn(Rng rng, float radius) => new Vector3(rng.Range(-1f, 1f), rng.Range(-1f, 1f), rng.Range(-1f, 1f)) * radius;
}

/// <summary>
/// Space attacks, the planet rules in three dimensions: wind-up (the telegraph), commit, lunge
/// at the committed spot, recover. The first attack on a target uses the species' long
/// telegraph; later ones the short normal wind-up. Contact is judged where the lunge lands, and
/// damage is dealt in full. A clinging leech lets go of the hull when it lunges.
/// </summary>
public static class SpaceAttacks
{
    public static bool Start(SpaceSim w, Creature c, ITarget target)
    {
        if (c.Dead || c.Attacking || target == null || !target.Attackable || c.Stats.Damage <= 0f) return false;
        CombatRules r = w.Combat;
        bool first = c.MustTelegraph(target, w.Time, r);
        c.Phase = AttackPhase.Windup;
        c.PhaseTime = 0f;
        c.PhaseLength = c.WindupLength = first ? c.Species.TelegraphWindup(r) : r.NormalWindup;
        c.FirstAttack = first;
        c.Committed = false;
        c.AttackTarget = target;
        c.AimPoint = target.Position;
        w.RaiseTelegraph(new AttackEvent { Creature = c, Target = target, First = first, Windup = c.WindupLength, Time = w.Time });
        return true;
    }

    public static void Update(SpaceSim w, Creature c, float dt)
    {
        if (c.Phase == AttackPhase.None)
        {
            if (c.State != CState.Warn) c.Rear = Mathf.MoveToward(c.Rear, 0f, dt * 2f);
            c.Lunge = Mathf.MoveToward(c.Lunge, 0f, dt * 3f);
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
                    c.Phase = AttackPhase.None;
                    return;
                }
                c.Desired = Vector3.Zero;
                if (!c.Committed) c.AimPoint = t.Position;
                c.FacePoint = c.AimPoint;
                float k = Math.Clamp(c.PhaseTime / c.PhaseLength, 0f, 1f);
                if (c.Species.Attack.Style == AttackStyle.Charge) c.Brace = MathF.Min(1f, k * 1.6f);
                else c.Rear = k;
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
                c.Desired = c.PhaseTime < 0.5f ? -c.Forward3() * c.Stats.WalkSpeed * 0.6f : Vector3.Zero;
                if (c.PhaseTime >= c.PhaseLength) c.Phase = AttackPhase.None;
                break;
        }
    }

    static void Launch(SpaceSim w, Creature c)
    {
        CombatRules r = w.Combat;
        Vector3 to = c.AimPoint - c.Position;
        float d = to.Length();
        Vector3 dir = d > 0.01f ? to / d : c.Forward3();
        float travel = Math.Clamp(d - c.Radius * 0.5f, 0f, c.Stats.AttackRange + SpaceBrain.Reach(c, r));
        c.ClingHull = null; // a leech lets go of the hull to strike
        c.LungeFrom = c.Position;
        c.LungeTo = c.Position + dir * travel;
        c.Facing = SpaceSim.Look(dir, c.Facing.Y);
        c.Phase = AttackPhase.Lunge;
        c.PhaseTime = 0f;
        c.PhaseLength = c.Species.Attack.Lunge > 0f ? c.Species.Attack.Lunge : r.LungeSeconds;
        c.Rear = c.Brace = 0f;
    }

    static void Land(SpaceSim w, Creature c, ITarget t)
    {
        CombatRules r = w.Combat;
        Vector3 dir = c.LungeTo - c.LungeFrom;
        dir = dir.LengthSquared() > 1e-4f ? dir.Normalized() : c.Forward3();
        Vector3 strike = c.LungeTo + dir * c.Radius * 0.5f;
        bool hit = t.Attackable && (t.Position - strike).Length() <= r.ContactRadius + c.Radius * 0.4f;
        float dealt = hit ? t.Health.Apply(c.Stats.Damage) : 0f;
        c.RememberAttack(t, w.Time);
        w.RaiseAttacked(new AttackEvent { Creature = c, Target = t, First = c.FirstAttack, Windup = c.WindupLength, Hit = hit, Damage = dealt, Time = w.Time });
        c.Phase = AttackPhase.Recover;
        c.PhaseTime = 0f;
        c.PhaseLength = r.AttackCooldown;
    }
}

/// <summary>
/// Moves space creatures: there is no ground and no gravity. Swimmers accelerate towards what
/// the brain wants and turn their whole body to face their way, banking into turns; they slide
/// off rocks and hulls. Clingers crawl over the face of the hull they hold. Lunges fly straight
/// from where the creature launched to where it committed. The dead drift and slowly tumble.
/// </summary>
public static class SpaceMotion
{
    public static void Move(SpaceSim w, Creature c, float dt)
    {
        if (c.Dead)
        {
            c.Velocity *= MathF.Exp(-0.4f * dt);
            c.Position += c.Velocity * dt;
            c.Facing = c.Facing.Rotated(new Vector3(0.3f, 1f, 0.2f).Normalized(), 0.12f * dt).Orthonormalized();
            return;
        }
        if (c.Phase == AttackPhase.Lunge)
        {
            float t = Math.Clamp(c.PhaseTime / c.PhaseLength, 0f, 1f);
            c.Position = c.LungeFrom.Lerp(c.LungeTo, 1f - (1f - t) * (1f - t));
            c.Velocity = (c.LungeTo - c.LungeFrom) / MathF.Max(0.01f, c.PhaseLength);
            return;
        }
        if (c.Clinging) Crawl(c, dt);
        else Swim(w, c, dt);
        Vector3 f = c.Forward3();
        c.Yaw = MathF.Atan2(-f.X, -f.Z);
    }

    static void Crawl(Creature c, float dt)
    {
        Hull hull = c.ClingHull;
        Vector3 n = hull.Normal(c.ClingFace);
        Vector3 v = c.Desired - n * c.Desired.Dot(n);
        c.Velocity = v;
        c.Position = hull.ClampToFace(c.ClingFace, c.Position + v * dt, c.Radius);
        Vector3 want = v.LengthSquared() > 0.0025f ? v : c.FacePoint is { } face ? face - c.Position : -c.Facing.Z;
        want -= n * want.Dot(n);
        if (want.LengthSquared() < 1e-6f) want = -c.Facing.Z - n * (-c.Facing.Z).Dot(n);
        if (want.LengthSquared() < 1e-6f) return;
        Turn(c, SpaceSim.Look(want, n), 3f, dt);
    }

    static void Swim(SpaceSim w, Creature c, float dt)
    {
        Species s = c.Species;
        float accel = s.Rig == RigKind.Clinger ? s.Stats.RunSpeed * 2f : s.Swim switch
        {
            SwimStyle.Pulse => 1.2f + s.Stats.RunSpeed * 0.5f,
            SwimStyle.Undulate => s.Stats.RunSpeed * 0.35f,
            _ => s.Stats.RunSpeed * 0.8f,
        };
        Vector3 v = c.Velocity.MoveToward(c.Desired, accel * dt);
        Vector3 p = c.Position + v * dt;

        foreach (Asteroid a in w.Asteroids)
        {
            Vector3 off = p - a.Center;
            float d = off.Length(), min = a.Radius + c.Radius;
            if (d >= min || d < 1e-3f) continue;
            Vector3 nrm = off / d;
            p = a.Center + nrm * min;
            float into = v.Dot(nrm);
            if (into < 0f) v -= nrm * into;
        }
        // Swimmers slide off hulls; a leech heading home is allowed right up to its own.
        foreach (Hull h in w.Hulls)
        {
            if (s.Rig == RigKind.Clinger && h == c.HomeHull) continue;
            if (h.SignedDistance(p) >= c.Radius) continue;
            p = h.PushOut(p, c.Radius, out Vector3 nrm);
            float into = v.Dot(nrm);
            if (into < 0f) v -= nrm * into;
        }
        if (p.Length() > w.BoundsRadius) v -= p.Normalized() * accel * dt * 2f;
        c.Velocity = v;
        c.Position = p;

        // Face the way it swims, or what it is watching when it hangs still; bank into turns.
        Vector3 fwd = c.Forward3();
        Vector3 want = v.Length() > 0.4f ? v : c.FacePoint is { } face && (face - p).LengthSquared() > 0.01f ? face - p : fwd;
        Vector3 up = Vector3.Up;
        if (s.Swim == SwimStyle.Flap)
        {
            Vector3 turn = want.Normalized() - fwd;
            up = (Vector3.Up + turn * 2.5f).Normalized();
        }
        float rate = Math.Clamp(2.4f / MathF.Sqrt(MathF.Max(1f, s.Body.Size / 3f)), 0.25f, 3f);
        Turn(c, SpaceSim.Look(want, up), rate, dt);
    }

    static void Turn(Creature c, Basis target, float rate, float dt)
    {
        Quaternion from = c.Facing.GetRotationQuaternion(), to = target.GetRotationQuaternion();
        c.Facing = new Basis(from.Slerp(to, 1f - MathF.Exp(-rate * dt)).Normalized());
    }

    /// <summary>The way a zero-g creature faces, in three dimensions.</summary>
    public static Vector3 Forward3(this Creature c) => -c.Facing.Z;
}
