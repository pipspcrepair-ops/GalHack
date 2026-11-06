using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>
/// Behaviour state machines. Each behaviour is a small function over shared verbs (wander,
/// graze, flee, hunt, steer); the species' temperament decides how they react to you, which
/// is where the surprises come from: a skittish giant runs, an aggressive herd hunts as one.
/// The brain only writes intentions into the creature; Attacks, Locomotion and the rig act
/// on them.
/// </summary>
public static partial class Brain
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
            if (c.Noticed != null) c.NoticedAt = w.Time;
            return;
        }

        // A settlement's own animals keep to their pens and their people.
        if (c.Species.Domestic != DomesticKind.None)
        {
            Domestic(w, c, dt);
            return;
        }

        // Caves and places have their own roles: lurkers, prowlers, ambushers, nesters, subsisters.
        if (c.Species.IsDeep)
        {
            Deep(w, c, dt);
            return;
        }

        // Thinking every time is what it can see and hear, never how far off someone is: what it hasn't
        // noticed isn't there. (A living boulder is the exception: it goes by being looked at.)
        ITarget near = w.NearestTarget(c.Position, out float dist);
        Senses.Surface(w, c, near, dist, dt);
        if (c.Noticed == null && !c.Species.Tumbles)
        {
            near = null;
            dist = float.MaxValue;
        }
        else if (near != null && c.Noticed != null) near = c.Noticed;

        // Outside its hours it goes to its rest site and stays there, unless something wakes it.
        if (w.ActivityCycles && c.Rest != null && Resting(w, c, near, dist, dt)) return;

        switch (c.Species.Behaviour)
        {
            case Behaviour.Grazer: Grazer(w, c, near, dist, dt); break;
            case Behaviour.Herd: HerdMember(w, c, near, dist, dt); break;
            case Behaviour.Predator: Predator(w, c, near, dist, dt); break;
            case Behaviour.PackHunter: PackHunter(w, c, near, dist, dt); break;
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

        // Wild things keep out of settlements: they steer round them, whatever they are doing.
        if (w.Settlements.Count > 0) AvoidTowns(w, c);
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

    // ---- Packs --------------------------------------------------------------------------

    /// <summary>
    /// A pack member. Roaming, the leader wanders and the rest keep station behind it. When
    /// any of them spots prey the pack stalks it together, crouched; close in, they spread
    /// round it (one in front, the rest out to the flanks and behind); then the one in front
    /// feints, darting in and snarling without biting, while the flankers close in and attack
    /// in turn (Attacks makes them take turns). They all break off together when the leader
    /// dies or too many are hurt, run, and regroup somewhere else.
    /// </summary>
    static void PackHunter(WildSim w, Creature c, ITarget near, float dist, float dt)
    {
        Pack p = c.Pack;
        if (p == null)
        {
            Predator(w, c, near, dist, dt);
            return;
        }
        PackRules r = w.Data.Fauna.Pack;
        UpdatePack(w, p, r, dt);
        ITarget prey = p.Prey;
        switch (p.Phase)
        {
            case PackPhase.Retreat:
                c.SetState(CState.Flee);
                Steer(w, c, p.RetreatGoal + FormationOffset(c, p.Leader, r.Spacing), c.Stats.RunSpeed);
                c.Desired += Separation(c, p, r.Spacing) * c.Stats.RunSpeed;
                break;

            case PackPhase.Stalk:
            {
                // Creep in together, low, all facing the prey.
                c.SetState(CState.Stalk);
                c.WantCrouch = 1f;
                c.FacePoint = prey.Position;
                Vector3 to = WildSim.Flat(prey.Position - p.Center);
                to = to.LengthSquared() > 0.01f ? to.Normalized() : c.Forward;
                Vector3 together = WildSim.Flat(p.Center - c.Position) * 0.3f;
                c.Desired = to * c.Stats.WalkSpeed * 0.7f + together.LimitLength(c.Stats.WalkSpeed) + Separation(c, p, r.Spacing) * c.Stats.WalkSpeed;
                break;
            }

            case PackPhase.Surround:
                c.SetState(CState.Chase);
                c.WantCrouch = 0.5f;
                c.FacePoint = prey.Position;
                CircleTo(w, c, prey.Position, SlotAngle(p, c), RingRadius(c, r) + (c == p.Feinter ? 1f : 0f), c.Stats.RunSpeed);
                c.Desired += Separation(c, p, r.Spacing) * c.Stats.WalkSpeed;
                break;

            case PackPhase.Engage:
                if (c == p.Feinter && Flankers(p) > 0) Feint(w, c, p, prey);
                else Flank(w, c, p, prey, r);
                break;

            default: // Roam: the leader wanders, the others follow it
                if (c == p.Leader) WanderAndGraze(w, c, 40f, dt);
                else Follow(w, c, p, r);
                break;
        }
    }

    /// <summary>Pack-wide decisions, made once per step by whichever member gets there first.</summary>
    static void UpdatePack(WildSim w, Pack p, PackRules r, float dt)
    {
        if (p.UpdatedFrame == w.Frame) return;
        p.UpdatedFrame = w.Frame;
        p.PhaseTime += dt;

        Vector3 sum = Vector3.Zero;
        int alive = 0, hurt = 0;
        foreach (Creature m in p.Members)
        {
            if (m.Dead || m.Removed || m.Health < m.Stats.MaxHealth * r.HurtBelow) hurt++;
            if (m.Dead || m.Removed) continue;
            sum += m.Position;
            alive++;
        }
        if (alive == 0) return;
        p.Center = sum / alive;

        bool leaderLost = p.Leader == null || p.Leader.Dead || p.Leader.Removed;
        if (leaderLost) p.Leader = p.Members.Find(m => m.Alive && !m.Removed);
        bool hunting = p.Phase is PackPhase.Stalk or PackPhase.Surround or PackPhase.Engage;
        if (hunting && (leaderLost || hurt >= MathF.Ceiling(p.Members.Count * r.RetreatShare)))
        {
            StartRetreat(w, p);
            return;
        }

        switch (p.Phase)
        {
            case PackPhase.Roam:
            {
                if (w.Time < p.RestUntil) break;
                // Hurt one of them and the whole pack turns on you at once.
                foreach (Creature m in p.Members)
                    if (m.Alive && Provoked(w, m) is { } attacker)
                    {
                        p.Prey = attacker;
                        AssignSlots(p, r);
                        p.SetPhase(PackPhase.Surround);
                        return;
                    }
                foreach (Creature m in p.Members)
                {
                    if (!m.Alive || m.Noticed is not { Attackable: true } t) continue;
                    p.Prey = t;
                    p.SetPhase(PackPhase.Stalk);
                    break;
                }
                break;
            }
            case PackPhase.Stalk:
                if (!PreyValid(p)) EndHunt(w, p, 4f);
                else if (WildSim.Flat(p.Prey.Position - p.Center).Length() < r.SurroundRadius)
                {
                    AssignSlots(p, r);
                    p.SetPhase(PackPhase.Surround);
                }
                break;
            case PackPhase.Surround:
                if (!PreyValid(p)) EndHunt(w, p, 4f);
                else if (InPlace(p, r) || p.PhaseTime > r.SurroundSeconds) p.SetPhase(PackPhase.Engage);
                break;
            case PackPhase.Engage:
                if (!PreyValid(p)) EndHunt(w, p, 6f);
                break;
            case PackPhase.Retreat:
                if (p.PhaseTime > r.RetreatSeconds)
                {
                    // Regroup where they ended up, and rest before hunting again.
                    p.Home = p.Center;
                    foreach (Creature m in p.Members) m.Home = p.Center;
                    EndHunt(w, p, r.RegroupSeconds);
                }
                break;
        }
    }

    static bool PreyValid(Pack p) =>
        p.Prey != null && p.Prey.Attackable && !(p.Prey.Health?.Downed ?? false)
        && WildSim.Flat(p.Prey.Position - p.Center).Length() < (p.Leader?.Stats.Perception ?? 40f) * 2f
        && Tracked(p);

    /// <summary>Whether any of the pack still has its prey, by sight, by ear or by memory, or was hurt by it.</summary>
    static bool Tracked(Pack p)
    {
        foreach (Creature m in p.Members)
            if (m.Alive && (m.Noticed == p.Prey || m.ProvokedBy == p.Prey || m.Attacking)) return true;
        return false;
    }

    static void EndHunt(WildSim w, Pack p, float rest)
    {
        p.Prey = null;
        p.Feinter = null;
        p.Slots.Clear();
        p.RestUntil = w.Time + rest;
        p.SetPhase(PackPhase.Roam);
    }

    static void StartRetreat(WildSim w, Pack p)
    {
        Vector3 from = p.Prey?.Position ?? p.Center + p.Leader.Forward * 5f;
        Vector3 away = WildSim.Flat(p.Center - from);
        away = away.LengthSquared() > 0.01f ? away.Normalized() : -p.Leader.Forward;
        p.RetreatGoal = w.Terrain.Clamp(p.Center + away * 35f, 20f);
        foreach (Creature m in p.Members)
            if (m.Alive && m.Phase == AttackPhase.Windup && !m.Committed) Attacks.Cancel(m);
        p.Prey = null;
        p.Feinter = null;
        p.Slots.Clear();
        p.SetPhase(PackPhase.Retreat);
    }

    /// <summary>
    /// Places round the prey: the member nearest the side the pack came from holds the front
    /// and feints; the others take the flanks first, then the rear quarters, then behind.
    /// </summary>
    static void AssignSlots(Pack p, PackRules r)
    {
        Vector3 prey = p.Prey.Position;
        p.FrontAngle = AngleAround(prey, p.Center);
        var free = p.Members.FindAll(m => m.Alive && !m.Removed);
        free.Sort((a, b) => MathF.Abs(Geo.WrapAngle(AngleAround(prey, a.Position) - p.FrontAngle)).CompareTo(MathF.Abs(Geo.WrapAngle(AngleAround(prey, b.Position) - p.FrontAngle))));
        p.Slots.Clear();
        p.Feinter = free[0];
        p.Slots[p.Feinter] = 0f;
        free.RemoveAt(0);
        float flank = Mathf.DegToRad(r.FlankAngle);
        float[] offsets = { flank, -flank, flank * 0.55f, -flank * 0.55f, MathF.PI, flank * 1.35f, -flank * 1.35f };
        foreach (float off in offsets)
        {
            if (free.Count == 0) break;
            // Whoever is already nearest that place takes it, so nobody runs round the whole ring.
            Creature best = null;
            float bestErr = float.MaxValue;
            foreach (Creature m in free)
            {
                float err = MathF.Abs(Geo.WrapAngle(AngleAround(prey, m.Position) - (p.FrontAngle + off)));
                if (err < bestErr) { bestErr = err; best = m; }
            }
            p.Slots[best] = off;
            free.Remove(best);
        }
        foreach (Creature m in free) p.Slots[m] = MathF.PI; // more than seven: the rest go behind
    }

    static bool InPlace(Pack p, PackRules r)
    {
        foreach (Creature m in p.Members)
        {
            if (!m.Alive || m.Removed) continue;
            float angle = AngleAround(p.Prey.Position, m.Position);
            float d = WildSim.Flat(m.Position - p.Prey.Position).Length();
            if (MathF.Abs(Geo.WrapAngle(angle - SlotAngle(p, m))) > 0.45f || MathF.Abs(d - RingRadius(m, r)) > 3f) return false;
        }
        return true;
    }

    static int Flankers(Pack p)
    {
        int n = 0;
        foreach (Creature m in p.Members) if (m != p.Feinter && m.Alive && !m.Removed) n++;
        return n;
    }

    static float SlotAngle(Pack p, Creature c) => p.FrontAngle + p.Slots.GetValueOrDefault(c, MathF.PI);

    static float RingRadius(Creature c, PackRules r) => r.RingRadius + c.Radius;

    /// <summary>The yaw angle of <paramref name="p"/> seen from <paramref name="center"/> (0 = +Z).</summary>
    static float AngleAround(Vector3 center, Vector3 p) => MathF.Atan2(p.X - center.X, p.Z - center.Z);

    static Vector3 AngleDir(float a) => new(MathF.Sin(a), 0f, MathF.Cos(a));

    /// <summary>Runs round a ring about a point towards a place on it, never across the middle.</summary>
    static void CircleTo(WildSim w, Creature c, Vector3 center, float angle, float radius, float speed)
    {
        float now = AngleAround(center, c.Position);
        float d = WildSim.Flat(c.Position - center).Length();
        // From far out, head straight for the place; near the ring, swing round it a step at a time.
        float next = d > radius + 6f ? angle : now + Math.Clamp(Geo.WrapAngle(angle - now), -0.6f, 0.6f);
        Steer(w, c, center + AngleDir(next) * radius, speed);
    }

    /// <summary>The front member's job: hold its place, dart in snarling, back off. No bite.</summary>
    static void Feint(WildSim w, Creature c, Pack p, ITarget prey)
    {
        float hold = c.Stats.AttackRange + 2.4f;
        float cycle = (c.Age + c.Id * 0.7f) % 2.6f;
        bool dart = cycle < 0.7f;
        if (dart && cycle < 0.05f) w.RaiseWarned(c); // the snarl
        c.SetState(dart ? CState.Warn : CState.Chase);
        c.FacePoint = prey.Position;
        c.Rear = dart ? MathF.Sin(cycle / 0.7f * MathF.PI) * 0.8f : 0f;
        float radius = dart ? c.Stats.AttackRange + 0.7f : hold;
        CircleTo(w, c, prey.Position, SlotAngle(p, c), radius, dart ? c.Stats.RunSpeed : c.Stats.WalkSpeed * 1.5f);
    }

    /// <summary>A flanker closes in from its own side and attacks when the pack lets it.</summary>
    static void Flank(WildSim w, Creature c, Pack p, ITarget prey, PackRules r)
    {
        c.SetState(CState.Chase);
        c.FacePoint = prey.Position;
        float d = WildSim.Flat(prey.Position - c.Position).Length();
        if (d <= c.Stats.AttackRange)
        {
            c.Desired = Vector3.Zero;
            Attacks.Start(w, c, prey); // waits its turn if another member is mid-attack
            return;
        }
        float angle = SlotAngle(p, c);
        if (d > RingRadius(c, r) + 3f) CircleTo(w, c, prey.Position, angle, RingRadius(c, r), c.Stats.RunSpeed);
        else Steer(w, c, prey.Position + AngleDir(angle) * c.Stats.AttackRange * 0.7f, c.Stats.RunSpeed);
        c.Desired += Separation(c, p, r.Spacing) * c.Stats.WalkSpeed;
    }

    /// <summary>Keeping station behind the leader in a loose wedge while the pack roams.</summary>
    static void Follow(WildSim w, Creature c, Pack p, PackRules r)
    {
        Creature lead = p.Leader;
        Vector3 goal = lead.Position + FormationOffset(c, lead, r.Spacing);
        float d = WildSim.Flat(goal - c.Position).Length();
        c.FacePoint = null;
        if (d < 1.2f)
        {
            c.SetState(CState.Idle);
            c.Desired = Separation(c, p, r.Spacing) * c.Stats.WalkSpeed;
            return;
        }
        c.SetState(CState.Wander);
        Steer(w, c, goal, d > 8f ? c.Stats.RunSpeed : c.Stats.WalkSpeed);
        c.Desired += Separation(c, p, r.Spacing) * c.Stats.WalkSpeed;
    }

    /// <summary>A member's place in the wedge behind the leader, alternating sides.</summary>
    static Vector3 FormationOffset(Creature c, Creature lead, float spacing)
    {
        if (c == lead || lead == null) return Vector3.Zero;
        int k = Math.Max(1, c.HerdSlot);
        float side = k % 2 == 1 ? -1f : 1f, row = (k + 1) / 2;
        return new Basis(Vector3.Up, lead.Yaw) * new Vector3(side * spacing * 0.8f * row, 0f, spacing * row);
    }

    static Vector3 Separation(Creature c, Pack p, float spacing)
    {
        Vector3 push = Vector3.Zero;
        foreach (Creature m in p.Members)
        {
            if (m == c || !m.Alive) continue;
            Vector3 away = WildSim.Flat(c.Position - m.Position);
            float d = away.Length(), keep = MathF.Max(spacing * 0.7f, (c.Radius + m.Radius) * 1.2f);
            if (d < keep && d > 0.001f) push += away / d * (keep - d) / keep;
        }
        return push;
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
        else Steer(w, c, Senses.Aim(c, t), c.Stats.RunSpeed);
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
