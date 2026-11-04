using System;
using Godot;

namespace Wildlife;

/// <summary>
/// Behaviour state machines. Each behaviour is a small function over shared verbs (wander,
/// graze, flee, hunt, steer); the species' temperament decides how they react to you, which
/// is where the surprises come from: a skittish giant runs, an aggressive herd hunts as one.
/// The brain only writes intentions into the creature; Attacks, Locomotion and the rig act
/// on them.
/// </summary>
public static class Brain
{
    public static void Update(WildSim w, Creature c, float dt)
    {
        c.StateTime += dt;
        c.Think -= dt;
        c.PathAge += dt;
        c.FacePoint = null;
        c.LookAt = null;
        c.WantGraze = 0f;
        c.WantCrouch = 0f;
        c.WantHidden = 0f;
        if (c.Attacking)
        {
            // Mid-attack the attack owns the body; just keep looking at the target.
            c.LookAt = c.AttackTarget?.Position;
            return;
        }

        ITarget near = w.NearestTarget(c.Position, out float dist);
        if (near != null && dist < c.Stats.Perception)
        {
            c.LookAt = near.Position + Vector3.Up * 1.4f;
            c.Alert = 1f;
        }
        else c.Alert = 0f;

        switch (c.Species.Behaviour)
        {
            case Behaviour.Grazer: Grazer(w, c, near, dist, dt); break;
            case Behaviour.Herd: HerdMember(w, c, near, dist, dt); break;
            case Behaviour.Predator: Predator(w, c, near, dist, dt); break;
            case Behaviour.Ambusher: Ambusher(w, c, near, dist, dt); break;
            case Behaviour.Scavenger: Scavenger(w, c, near, dist, dt); break;
            case Behaviour.Swarm: SwarmMind(w, c, near, dist, dt); break;
            case Behaviour.TerritorialGiant: Territorial(w, c, near, dist, dt); break;
            default: Oddity(w, c, near, dist, dt); break;
        }

        // Fliers land to eat and to rest, and drop low to bite.
        if (c.Species.Rig == RigKind.Winged)
        {
            bool low = c.State is CState.Chase or CState.Eat || (c.State == CState.Graze && c.Species.Behaviour != Behaviour.Scavenger);
            c.Altitude = c.State == CState.Eat || c.State == CState.Graze ? 0f : c.State == CState.Chase ? 1.4f : low ? 1.4f : CruiseAltitude(c);
        }
        else if (c.Species.Rig == RigKind.GasBag) c.Altitude = c.State == CState.Chase ? 1.8f : CruiseAltitude(c);
    }

    static float CruiseAltitude(Creature c) => c.Species.Rig switch
    {
        RigKind.GasBag => 6f + (c.Id % 5) * 1.3f,
        RigKind.Swarm => 1.8f,
        _ => 9f + (c.Id % 4) * 2f,
    };

    // ---- Behaviours -------------------------------------------------------------------

    static void Grazer(WildSim w, Creature c, ITarget near, float dist, float dt)
    {
        if (ShouldFlee(w, c, near, dist)) { Flee(w, c, near.Position); return; }
        if (Provoked(w, c) is { } attacker) { Hunt(w, c, attacker, dist); return; }
        if (c.Stats.Hunts && Huntable(c, near, dist)) { Hunt(w, c, near, dist); return; }
        WanderAndGraze(w, c, 18f, dt);
    }

    static void HerdMember(WildSim w, Creature c, ITarget near, float dist, float dt)
    {
        Herd h = c.Herd;
        if (h == null) { Grazer(w, c, near, dist, dt); return; }
        UpdateHerd(w, h, dt);

        if (ShouldFlee(w, c, near, dist)) h.Alarm(near.Position, w.Time);
        if (Provoked(w, c) is { } attacker && c.Stats.Retaliates) h.Hunting = attacker;
        if (c.Stats.Hunts && Huntable(c, near, dist)) h.Hunting = near; // a herd that hunts
        if (h.Hunting != null)
        {
            float d = WildSim.Flat(h.Hunting.Position - c.Position).Length();
            if (!h.Hunting.Attackable || d > c.Stats.Perception * 1.6f) h.Hunting = null;
            else { Hunt(w, c, h.Hunting, d); return; }
        }

        // Everyone heads for their own slot around the herd's shared goal, keeping apart from
        // neighbours and never straying far from the middle: boids with a destination.
        float n = Math.Max(1, h.Members.Count);
        float angle = c.HerdSlot * 2.39996f;
        float r = h.Spread * MathF.Sqrt((c.HerdSlot + 0.5f) / n);
        Vector3 slot = h.Goal + new Vector3(MathF.Cos(angle) * r, 0f, MathF.Sin(angle) * r);
        Vector3 toSlot = WildSim.Flat(slot - c.Position);
        float speed = h.Alarmed ? c.Stats.RunSpeed : toSlot.Length() > 2.5f ? c.Stats.WalkSpeed : 0f;

        Vector3 steer = speed > 0f ? SteerDir(w, c, slot) * speed : Vector3.Zero;
        Vector3 separation = Vector3.Zero;
        foreach (Creature m in h.Members)
        {
            if (m == c || m.Dead) continue;
            Vector3 away = WildSim.Flat(c.Position - m.Position);
            float d = away.Length(), keep = (c.Radius + m.Radius) * 1.3f;
            if (d < keep && d > 0.001f) separation += away / d * (keep - d) / keep;
        }
        Vector3 toCenter = WildSim.Flat(h.Center - c.Position);
        Vector3 cohesion = toCenter.Length() > h.Spread * 1.4f ? toCenter.Normalized() * c.Stats.WalkSpeed * 0.8f : Vector3.Zero;
        c.Desired = steer + separation * c.Stats.WalkSpeed * 1.5f + cohesion;
        if (h.Alarmed) c.Desired += WildSim.Flat(h.Velocity) * 0.3f; // alignment: run with the herd

        bool still = c.Desired.Length() < 0.3f;
        c.SetState(h.Alarmed ? CState.Flee : still ? CState.Graze : CState.Wander);
        if (still)
        {
            c.Desired = Vector3.Zero;
            c.WantGraze = Mathf.PosMod(c.Age * 0.13f + c.HerdSlot * 0.37f, 1f) < 0.6f ? 1f : 0f;
        }
    }

    /// <summary>Herd-wide state, updated once per step by whichever member gets there first.</summary>
    static void UpdateHerd(WildSim w, Herd h, float dt)
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
            Vector3 away = WildSim.Flat(h.Center - h.ThreatPos);
            away = away.LengthSquared() > 0.01f ? away.Normalized() : Vector3.Right;
            h.Goal = w.Terrain.Clamp(h.Center + away * 20f, 20f);
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
        if (h.GoalTimer <= 0f)
        {
            var rng = h.Members[0].Rng;
            h.GoalTimer = rng.Range(14f, 28f);
            Vector3 g = h.Home + new Vector3(rng.Range(-25f, 25f), 0f, rng.Range(-25f, 25f));
            h.Goal = w.FreeSpot(g, rng);
        }
    }

    static void Predator(WildSim w, Creature c, ITarget near, float dist, float dt)
    {
        if (c.Health < c.Stats.MaxHealth * 0.25f && near != null) { Flee(w, c, near.Position); return; }
        if (Provoked(w, c) is { } attacker) { Hunt(w, c, attacker, dist); return; }

        bool prey = Huntable(c, near, dist * (w.Clock.IsNight ? 0.85f : 1f));
        switch (c.State)
        {
            case CState.Stalk:
                if (!prey) { c.SetState(CState.Wander); break; }
                // A shy hunter breaks off when you turn and stare it down.
                if (c.Species.Temperament == Temperament.Skittish && Watched(near, c, 0.85f) && dist < 18f)
                {
                    Flee(w, c, near.Position);
                    break;
                }
                c.WantCrouch = 1f;
                Steer(w, c, near.Position, c.Stats.WalkSpeed * 0.55f);
                c.FacePoint = near.Position;
                if (dist < Math.Max(8f, c.Stats.AttackRange * 3f) || c.StateTime > 10f) c.SetState(CState.Chase);
                break;
            case CState.Chase:
                Hunt(w, c, near, dist);
                break;
            default:
                if (prey) c.SetState(CState.Stalk);
                else WanderAndGraze(w, c, 35f, dt);
                break;
        }
    }

    static void Ambusher(WildSim w, Creature c, ITarget near, float dist, float dt)
    {
        if (Provoked(w, c) is { } attacker)
        {
            if (c.Stats.Retaliates) Hunt(w, c, attacker, dist);
            else Flee(w, c, attacker.Position);
            return;
        }

        bool night = w.Clock.IsNight;
        switch (c.State)
        {
            case CState.Chase:
                // After the strike it gives chase briefly, then slinks back to its spot.
                if (c.StateTime > 8f || near == null || !near.Attackable || dist > c.Stats.Perception) c.SetState(CState.ReturnHome);
                else Hunt(w, c, near, dist);
                break;
            case CState.ReturnHome:
                if (WildSim.Flat(c.Home - c.Position).Length() < 1.5f) c.SetState(night ? CState.Lurk : CState.Hidden);
                else Steer(w, c, c.Home, c.Stats.WalkSpeed);
                break;
            case CState.Lurk:
                c.WantHidden = 1f;
                c.Desired = Vector3.Zero;
                if (!night) c.SetState(CState.Hidden);
                else if (near != null && near.Attackable && dist < c.Stats.AmbushRadius)
                {
                    // Erupts out of hiding: the eruption is the start of its telegraphed lunge.
                    c.SetState(CState.Chase);
                    Hunt(w, c, near, dist);
                }
                break;
            default: // Hidden by day: buried, curled or flattened, and it stays that way
                c.SetState(CState.Hidden);
                c.WantHidden = 1f;
                c.Desired = Vector3.Zero;
                if (night) c.SetState(CState.Lurk);
                break;
        }
    }

    static void Scavenger(WildSim w, Creature c, ITarget near, float dist, float dt)
    {
        if (ShouldFlee(w, c, near, dist)) { Flee(w, c, near.Position); return; }
        if (Provoked(w, c) is { } attacker) { Hunt(w, c, attacker, dist); return; }
        if (c.Stats.Hunts && Huntable(c, near, dist)) { Hunt(w, c, near, dist); return; }

        // Look for carrion now and then, not every frame: 200 creatures times every corpse adds up.
        if (c.Think <= 0f)
        {
            c.Think = 1f;
            Creature corpse = w.NearestCorpse(c.Position, c.Stats.Perception * 1.5f);
            if (corpse != null)
            {
                c.Goal = corpse.Position;
                c.HasGoal = true;
                if (c.State is not (CState.Scavenge or CState.Eat)) c.SetState(CState.Scavenge);
                c.HasGoal = true;
            }
            else if (c.State is CState.Scavenge or CState.Eat) c.SetState(CState.Wander);
        }

        if (c.State is CState.Scavenge or CState.Eat)
        {
            Creature corpse = w.NearestCorpse(c.Goal, 2f);
            if (corpse == null) { c.SetState(CState.Wander); return; }
            float d = WildSim.Flat(corpse.Position - c.Position).Length();
            if (d < c.Radius + corpse.Radius + 0.6f)
            {
                c.SetState(CState.Eat);
                c.Desired = Vector3.Zero;
                c.WantGraze = 1f;
                c.FacePoint = corpse.Position;
                corpse.MeatLeft -= dt * 0.4f * MathF.Max(0.5f, c.Species.Body.Size);
            }
            else
            {
                c.State = CState.Scavenge;
                Steer(w, c, corpse.Position, c.Stats.WalkSpeed * 1.3f);
            }
            return;
        }
        WanderAndGraze(w, c, 30f, dt);
    }

    static void SwarmMind(WildSim w, Creature c, ITarget near, float dist, float dt)
    {
        if (ShouldFlee(w, c, near, dist)) { Flee(w, c, near.Position); return; }
        if (Provoked(w, c) is { } attacker) { Hunt(w, c, attacker, dist); return; }
        if (c.Stats.Hunts && Huntable(c, near, dist)) { Hunt(w, c, near, dist); return; }
        WanderAndGraze(w, c, 25f, dt);
    }

    static void Territorial(WildSim w, Creature c, ITarget near, float dist, float dt)
    {
        float territory = c.Stats.Territory;
        bool intruder = near != null && WildSim.Flat(near.Position - c.Home).Length() < territory;

        // The surprise: some giants are terrified of you.
        if (c.Species.Temperament == Temperament.Skittish && near != null && (intruder || dist < c.Stats.FleeRadius * near.ThreatScale))
        {
            Flee(w, c, near.Position);
            return;
        }
        if (Provoked(w, c) is { } attacker && c.Stats.Retaliates) { Hunt(w, c, attacker, dist); return; }

        switch (c.State)
        {
            case CState.Warn:
                // Rears, roars and stamps before it ever charges: you get to back off.
                c.Desired = Vector3.Zero;
                c.FacePoint = near?.Position;
                c.Rear = Math.Clamp(c.StateTime / 1.5f, 0f, 1f) * (c.StateTime < 1.8f ? 1f : 0f);
                if (!intruder || near == null || !near.Attackable) c.SetState(CState.ReturnHome);
                else if (c.StateTime > 2.2f) c.SetState(CState.Chase);
                break;
            case CState.Chase:
                if (near == null || !near.Attackable || WildSim.Flat(near.Position - c.Home).Length() > territory * 1.5f)
                    c.SetState(CState.ReturnHome);
                else Hunt(w, c, near, dist);
                break;
            case CState.ReturnHome:
                if (WildSim.Flat(c.Home - c.Position).Length() < 3f) c.SetState(CState.Wander);
                else Steer(w, c, c.Home, c.Stats.WalkSpeed);
                break;
            default:
                if (intruder && near.Attackable && c.Stats.Damage > 0f)
                {
                    c.SetState(CState.Warn);
                    w.RaiseWarned(c);
                }
                else WanderAndGraze(w, c, territory * 0.6f, dt);
                break;
        }
    }

    static void Oddity(WildSim w, Creature c, ITarget near, float dist, float dt)
    {
        if (Provoked(w, c) is { } attacker)
        {
            if (c.Stats.Retaliates && c.Stats.Damage > 0f) Hunt(w, c, attacker, dist);
            else Flee(w, c, attacker.Position);
            return;
        }

        Species s = c.Species;
        if (s.Rooted)
        {
            // Walking plants put down roots by day and wander by night; they curl up if you come close.
            if (near != null && dist < 5f)
            {
                c.SetState(CState.Freeze);
                c.WantCrouch = 1f;
                c.Desired = Vector3.Zero;
            }
            else if (!w.Clock.IsNight)
            {
                c.SetState(CState.Root);
                c.WantCrouch = 0.7f;
                c.Desired = Vector3.Zero;
            }
            else WanderAndGraze(w, c, 15f, dt);
            return;
        }

        if (s.Tumbles)
        {
            // Living boulders only move while nobody is looking at them.
            if (near != null && Watched(near, c, 0.72f) && dist < 70f)
            {
                c.SetState(CState.Freeze);
                c.Desired = Vector3.Zero;
            }
            else if (near != null && dist < 45f && dist > 4f)
            {
                c.SetState(CState.Follow);
                Steer(w, c, near.Position, c.Stats.WalkSpeed);
            }
            else if (near != null && dist <= 4f)
            {
                c.SetState(CState.Idle);
                c.Desired = Vector3.Zero;
            }
            else WanderAndGraze(w, c, 15f, dt);
            return;
        }

        if (ShouldFlee(w, c, near, dist)) { Flee(w, c, near.Position); return; }
        if (s.Rig == RigKind.GasBag) { WanderAndGraze(w, c, 40f, dt); return; }

        // Everything else is curious: it trails you at a polite distance and stares.
        if (near != null && dist < c.Stats.Perception)
        {
            c.SetState(CState.Follow);
            c.FacePoint = near.Position;
            if (dist > 7f) Steer(w, c, near.Position, c.Stats.WalkSpeed);
            else if (dist < 4f) c.Desired = WildSim.Flat(c.Position - near.Position).Normalized() * c.Stats.WalkSpeed * 0.5f;
            else c.Desired = Vector3.Zero;
            return;
        }
        WanderAndGraze(w, c, 20f, dt);
    }

    // ---- Shared verbs -----------------------------------------------------------------

    static bool ShouldFlee(WildSim w, Creature c, ITarget near, float dist)
    {
        if (near == null) return false;
        float radius = c.Stats.FleeRadius * near.ThreatScale;
        if (c.State == CState.Flee && dist < radius * 1.8f && c.StateTime < 8f) return true;
        return dist < radius;
    }

    static ITarget Provoked(WildSim w, Creature c) =>
        c.ProvokedBy != null && w.Time - c.ProvokedAt < 12f && c.ProvokedBy.Attackable && c.Stats.Retaliates ? c.ProvokedBy : null;

    static bool Huntable(Creature c, ITarget t, float dist) => t != null && t.Attackable && dist < c.Stats.Perception && c.Stats.Damage > 0f;

    /// <summary>True when the target is looking roughly at the creature.</summary>
    static bool Watched(ITarget t, Creature c, float cone)
    {
        Vector3 to = c.Position - t.Position;
        float len = to.Length();
        return len > 0.01f && t.Forward.Dot(to / len) > cone;
    }

    static void Flee(WildSim w, Creature c, Vector3 from)
    {
        c.SetState(CState.Flee);
        Vector3 away = WildSim.Flat(c.Position - from);
        away = away.LengthSquared() > 0.01f ? away.Normalized() : c.Forward;
        // Near the edge of the map, run along it rather than into it.
        Vector3 inward = WildSim.Flat(-c.Position).Normalized();
        if (!w.Terrain.Inside(c.Position + away * 15f, 10f)) away = (away + inward * 1.5f).Normalized();
        Steer(w, c, c.Position + away * 12f, c.Stats.RunSpeed);
    }

    /// <summary>Close in and attack. The attack itself always opens with the telegraph.</summary>
    static void Hunt(WildSim w, Creature c, ITarget t, float dist)
    {
        if (t == null || !t.Attackable) { c.SetState(CState.Wander); return; }
        dist = WildSim.Flat(t.Position - c.Position).Length();
        if (c.State != CState.Chase) c.SetState(CState.Chase);
        c.FacePoint = t.Position;
        if (dist <= c.Stats.AttackRange)
        {
            c.Desired = Vector3.Zero;
            Attacks.Start(w, c, t);
        }
        else Steer(w, c, t.Position, c.Stats.RunSpeed);
    }

    static void WanderAndGraze(WildSim w, Creature c, float radius, float dt)
    {
        if (c.State == CState.Graze || c.State == CState.Idle)
        {
            c.Desired = Vector3.Zero;
            c.WantGraze = c.Species.Flies && c.Species.Rig != RigKind.Winged ? 0f : 1f;
            if (c.StateTime > c.Rng.Range(3f, 8f)) c.SetState(CState.Wander);
            return;
        }
        if (c.State != CState.Wander) c.SetState(CState.Wander);
        if (!c.HasGoal)
        {
            Vector3 g = c.Home + new Vector3(c.Rng.Range(-radius, radius), 0f, c.Rng.Range(-radius, radius));
            c.Goal = w.FreeSpot(g, c.Rng);
            c.HasGoal = true;
        }
        float d = WildSim.Flat(c.Goal - c.Position).Length();
        bool flier = c.Species.Rig is RigKind.Winged or RigKind.GasBag || (c.Species.Rig == RigKind.Swarm && c.Species.Flies);
        if (d < (flier ? 4f : 1.5f) || c.StateTime > 25f)
        {
            if (c.Species.Rig == RigKind.Winged) c.HasGoal = false; // fliers keep circling
            else c.SetState(CState.Graze);
            return;
        }
        Steer(w, c, c.Goal, c.Stats.WalkSpeed);
    }

    static void Steer(WildSim w, Creature c, Vector3 goal, float speed)
    {
        Vector3 dir = SteerDir(w, c, goal);
        float d = WildSim.Flat(goal - c.Position).Length();
        c.Desired = dir * speed * Math.Clamp(d / 1.5f, 0.25f, 1f);
    }

    /// <summary>
    /// Straight at the goal when the line is clear on the nav grid; otherwise along an A* path,
    /// re-planned only when the goal has moved or the path is stale.
    /// </summary>
    static Vector3 SteerDir(WildSim w, Creature c, Vector3 goal)
    {
        Vector3 to = WildSim.Flat(goal - c.Position);
        bool flies = c.Species.Flies && !c.Landed;
        if (flies || w.Nav.LineClear(c.Position, goal))
        {
            c.Path.Clear();
            return to.LengthSquared() > 1e-4f ? to.Normalized() : Vector3.Zero;
        }
        bool stale = c.Path.Count == 0 || c.PathIndex >= c.Path.Count || c.PathGoal.DistanceTo(goal) > 4f || c.PathAge > 4f;
        if (stale && w.TakePathBudget())
        {
            w.Nav.FindPath(c.Position, goal, c.Path);
            c.PathIndex = 0;
            c.PathGoal = goal;
            c.PathAge = 0f;
        }
        while (c.PathIndex < c.Path.Count && WildSim.Flat(c.Path[c.PathIndex] - c.Position).Length() < 1.6f) c.PathIndex++;
        Vector3 next = c.PathIndex < c.Path.Count ? c.Path[c.PathIndex] : goal;
        Vector3 dir = WildSim.Flat(next - c.Position);
        return dir.LengthSquared() > 1e-4f ? dir.Normalized() : Vector3.Zero;
    }
}
