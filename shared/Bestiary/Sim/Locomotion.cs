using System;
using Godot;

namespace Wildlife;

/// <summary>
/// Moves creatures over the terrain with simple steering: accelerate towards the velocity
/// the brain wants, slide around rocks and trunks, stay on the height function. One mover
/// per kind of body: walkers and crawlers, hoppers, winged flyers (<see cref="Flight"/>), drifting
/// gas bags. Lunges move along a straight line from where the creature launched to where it committed.
/// </summary>
public static class Locomotion
{
    const float Gravity = 18f; // snappy hops read better than real ones

    public static void Move(WildSim w, Creature c, float dt)
    {
        // Creatures of caves and places move on that space's surfaces, not the terrain's heights.
        if (c.Space != null)
        {
            Crawl.Move(w, c, dt);
            return;
        }
        if (c.Thrown)
        {
            Tumble(w, c, dt);
            return;
        }
        if (c.Downed > 0f && !c.Dead)
        {
            GetUp(c, dt);
            return;
        }
        if (c.Dead)
        {
            Fall(w, c, dt);
            return;
        }
        if (c.Phase == AttackPhase.Lunge && c.Species.Flight != FlightKind.Glide)
        {
            Lunge(w, c);
            return;
        }
        if (w.Frozen && c.Species.Flies && !c.Landed)
        {
            // Lineup: fliers and floaters hold station at their height instead of circling off.
            float y = w.Terrain.Height(c.Position.X, c.Position.Z) + c.Altitude;
            c.Velocity = Vector3.Zero;
            c.Position = new Vector3(c.Position.X, Mathf.Lerp(c.Position.Y, y, 1f - MathF.Exp(-3f * dt)), c.Position.Z);
            return;
        }
        switch (c.Species.Rig)
        {
            case RigKind.Hopper: Hop(w, c, dt); break;
            case RigKind.Winged: Flight.Move(w, c, dt); break;
            case RigKind.GasBag: Drift(w, c, dt); break;
            case RigKind.Swarm when c.Species.Flies: Hover(w, c, dt); break;
            default: Ground(w, c, dt); break;
        }
    }

    /// <summary>
    /// Thrown: a plain ballistic arc to the ground, turning end over end as it goes, and the landing hurts
    /// by how fast it comes down. Landing hard and tipped over, it lies there on its side or back a while.
    /// </summary>
    static void Tumble(WildSim w, Creature c, float dt)
    {
        Vector3 v = c.Velocity + Vector3.Down * Gravity * 0.6f * dt;
        Vector3 p = w.Terrain.Clamp(c.Position + v * dt, 4f);
        float ground = w.Terrain.Height(p.X, p.Z);
        float turn = c.TumbleSpin.Length() * dt;
        if (turn > 1e-6f) c.Tumble = (new Quaternion(c.TumbleSpin.Normalized(), turn) * c.Tumble).Normalized();
        if (p.Y <= ground && v.Y < 0f)
        {
            p.Y = ground;
            c.Thrown = false;
            float hard = v.Length() - 8f;
            v = Vector3.Zero;
            if (hard > 0f && !c.Dead) w.Damage(c, hard * 3f, null);
            // Down on its side or its back if it came down tipped: it lies there a while; else on its feet.
            float tipped = (c.Tumble * Vector3.Up).AngleTo(Vector3.Up);
            if (tipped > 0.9f && hard > 0f)
            {
                c.Downed = 1.5f + MathF.Min(4f, hard * 0.25f);
                Vector3 side = new Vector3(c.Forward.X, 0f, c.Forward.Z);
                c.Tumble = new Quaternion(side.LengthSquared() > 1e-4f ? side.Normalized() : Vector3.Forward, tipped > 2.3f ? 2.6f : 1.45f);
            }
            else c.Tumble = Quaternion.Identity;
            c.TumbleSpin = Vector3.Zero;
        }
        c.Velocity = v;
        c.Position = p;
    }

    /// <summary>Downed by a hard landing: it lies still, then rolls back onto its feet over its last moments.</summary>
    static void GetUp(Creature c, float dt)
    {
        c.Downed = MathF.Max(0f, c.Downed - dt);
        c.Velocity = Vector3.Zero;
        if (c.Downed < 0.8f) c.Tumble = c.Tumble.Slerp(Quaternion.Identity, 1f - MathF.Exp(-5f * dt));
        if (c.Downed <= 0f) c.Tumble = Quaternion.Identity;
    }

    static void Ground(WildSim w, Creature c, float dt)
    {
        Vector3 v = WildSim.Flat(c.Velocity);
        Vector3 want = WildSim.Flat(c.Desired);
        if (c.Attacking) v = v.MoveToward(want, Heft.Brake(c) * 3f * dt); // an attack places the body itself
        else
        {
            // It goes where it faces: it swings toward what it wants, easing off while it does, and
            // speed comes and goes by its size. What it was already doing carries it wide of a turn.
            float speed = v.Length(), wantSpeed = want.Length();
            Turn(c, wantSpeed > 0.05f ? want : Vector3.Zero, dt, speed);
            Vector3 f = c.Forward;
            float align = wantSpeed > 0.05f ? MathF.Max(0f, f.Dot(want / wantSpeed)) : 0f;
            float target = wantSpeed * (0.2f + 0.8f * align);
            speed = Mathf.MoveToward(speed, target, (target > speed ? Heft.Accel(c) : Heft.Brake(c)) * dt);
            v = v.MoveToward(f * speed, Heft.Accel(c) * 2f * dt);
        }
        Vector3 p = c.Position + v * dt;
        Avoid(w, c, ref p, ref v);
        p = Bound(w, p, ref v);
        p.Y = w.Terrain.Height(p.X, p.Z);
        if (w.SolidAt != null) KeepOutOfSolid(w, c, ref p, ref v);
        c.Velocity = v;
        c.Position = p;
    }

    /// <summary>
    /// A place's walls are walls: a step that would take the body into one slides along it on
    /// whichever way is still open, or doesn't happen. (A body already inside, which never should
    /// be, is let walk out: the sim's scan puts it back in the open.)
    /// </summary>
    static void KeepOutOfSolid(WildSim w, Creature c, ref Vector3 p, ref Vector3 v)
    {
        if (!Interpenetration.SolidSurface(w, p, c.Species) || Interpenetration.SolidSurface(w, c.Position, c.Species)) return;
        var alongX = new Vector3(p.X, 0f, c.Position.Z);
        var alongZ = new Vector3(c.Position.X, 0f, p.Z);
        if (!Interpenetration.SolidSurface(w, alongX, c.Species)) { p = alongX; v.Z = 0f; }
        else if (!Interpenetration.SolidSurface(w, alongZ, c.Species)) { p = alongZ; v.X = 0f; }
        else { p = c.Position; v = Vector3.Zero; return; }
        p.Y = w.Terrain.Height(p.X, p.Z);
    }

    /// <summary>Hoppers stand still on the ground and cover distance in ballistic hops.</summary>
    static void Hop(WildSim w, Creature c, float dt)
    {
        float legs = c.Species.Body.LegLength;
        switch (c.Hop)
        {
            case HopPhase.Ground:
                c.Velocity = Vector3.Zero;
                c.HopCooldown -= dt;
                Vector3 want = WildSim.Flat(c.Desired);
                if (want.Length() > 0.2f)
                {
                    Turn(c, want, dt * 2f);
                    if (c.HopCooldown <= 0f) { c.Hop = HopPhase.Crouch; c.HopTime = 0f; }
                }
                else if (c.FacePoint is { } face) Turn(c, WildSim.Flat(face - c.Position), dt);
                break;
            case HopPhase.Crouch:
                c.HopTime += dt / 0.16f;
                if (c.HopTime >= 1f)
                {
                    bool fleeing = c.State == CState.Flee;
                    float height = c.Species.Tumbles ? c.Species.Body.Size * 0.3f : legs * (fleeing ? 1.1f : 0.8f);
                    float vy = MathF.Sqrt(2f * Gravity * height);
                    float air = 2f * vy / Gravity;
                    float maxDist = c.Species.Tumbles ? c.Species.Body.Size * 0.8f : legs * 3.2f;
                    Vector3 dir = WildSim.Flat(c.Desired);
                    float speed = MathF.Min(dir.Length() * 1.6f, maxDist / air);
                    dir = dir.LengthSquared() > 1e-4f ? dir.Normalized() : c.Forward;
                    c.Velocity = dir * speed + Vector3.Up * vy;
                    c.Hop = HopPhase.Air;
                    c.HopTime = 0f;
                }
                break;
            case HopPhase.Air:
                c.HopTime += dt;
                Vector3 v = c.Velocity;
                v.Y -= Gravity * dt;
                Vector3 p = c.Position + v * dt;
                Vector3 flat = WildSim.Flat(v);
                Avoid(w, c, ref p, ref flat);
                p = Bound(w, p, ref flat);
                v = new Vector3(flat.X, v.Y, flat.Z);
                float ground = w.Terrain.Height(p.X, p.Z);
                if (p.Y <= ground && v.Y < 0f)
                {
                    p.Y = ground;
                    v = Vector3.Zero;
                    c.Hop = HopPhase.Land;
                    c.HopTime = 0f;
                }
                c.Velocity = v;
                c.Position = p;
                break;
            case HopPhase.Land:
                c.HopTime += dt / 0.16f;
                c.Velocity = Vector3.Zero;
                c.Position = new Vector3(c.Position.X, w.Terrain.Height(c.Position.X, c.Position.Z), c.Position.Z);
                if (c.HopTime >= 1f)
                {
                    c.Hop = HopPhase.Ground;
                    c.HopCooldown = c.State == CState.Flee ? 0.05f : c.Species.Tumbles ? 0.9f : c.Rng.Range(0.15f, 0.5f);
                }
                break;
        }
    }

    /// <summary>Gas bags go where the wind takes them, nudged towards where they want to be.</summary>
    static void Drift(WildSim w, Creature c, float dt)
    {
        Vector3 want = WildSim.Flat(c.Desired) * 0.6f + w.Wind * 0.3f;
        if (WildSim.Flat(c.Position - c.Home).Length() > 60f) want += WildSim.Flat(c.Home - c.Position).Normalized() * 1.2f;
        Vector3 flat = WildSim.Flat(c.Velocity).MoveToward(want, 0.9f * dt);
        float ground = w.Terrain.Height(c.Position.X, c.Position.Z);
        float bob = MathF.Sin(c.Age * 0.6f + c.Id) * 0.6f;
        float vy = Math.Clamp((ground + c.Altitude + bob - c.Position.Y) * 0.6f, -2f, 2f);
        Vector3 p = c.Position + (flat + Vector3.Up * vy) * dt;
        p = Bound(w, p, ref flat);
        c.Velocity = flat + Vector3.Up * vy;
        c.Position = p;
        if (flat.Length() > 0.1f) c.Yaw = Mathf.RotateToward(c.Yaw, MathF.Atan2(-flat.X, -flat.Z), 0.3f * dt);
    }

    /// <summary>A flying swarm's centre: quick and low, like a gas bag that knows where it's going.</summary>
    static void Hover(WildSim w, Creature c, float dt)
    {
        Vector3 flat = WildSim.Flat(c.Velocity).MoveToward(WildSim.Flat(c.Desired), 8f * dt);
        float ground = w.Terrain.Height(c.Position.X, c.Position.Z);
        float vy = Math.Clamp((ground + c.Altitude - c.Position.Y) * 2f, -3f, 3f);
        Vector3 p = c.Position + (flat + Vector3.Up * vy) * dt;
        p = Bound(w, p, ref flat);
        c.Velocity = flat + Vector3.Up * vy;
        c.Position = p;
        Turn(c, flat, dt);
    }

    static void Lunge(WildSim w, Creature c)
    {
        float t = Math.Clamp(c.PhaseTime / c.PhaseLength, 0f, 1f);
        float eased = 1f - (1f - t) * (1f - t);
        Vector3 p = c.LungeFrom.Lerp(c.LungeTo, eased);
        float ground = w.Terrain.Height(p.X, p.Z);
        if (c.Species.Rig == RigKind.Hopper) p.Y = ground + MathF.Sin(t * MathF.PI) * c.Species.Body.LegLength * 0.6f;
        else if (c.Species.Flight == FlightKind.Hover) p.Y = Mathf.Lerp(c.LungeFrom.Y, c.AimPoint.Y + 1.3f, eased); // a dart at your chest, wherever you stand
        else if (c.Species.Flies) p.Y = Mathf.Lerp(c.LungeFrom.Y, ground + 1f, eased);
        else p.Y = ground;
        c.Velocity = (c.LungeTo - c.LungeFrom) / MathF.Max(0.01f, c.PhaseLength);
        // A lunge into a wall stops at the wall.
        if (!c.Species.Flies && Interpenetration.SolidSurface(w, p, c.Species) && !Interpenetration.SolidSurface(w, c.Position, c.Species))
        {
            c.LungeTo = c.Position;
            c.Velocity = Vector3.Zero;
            return;
        }
        c.Position = p;
        if (c.Species.Rig == RigKind.Hopper) c.Hop = t < 1f ? HopPhase.Air : HopPhase.Land;
    }

    /// <summary>The dead stop where they are; anything airborne falls.</summary>
    static void Fall(WildSim w, Creature c, float dt)
    {
        float ground = w.Terrain.Height(c.Position.X, c.Position.Z);
        if (c.Position.Y > ground + 0.01f)
        {
            float g = c.Species.Rig == RigKind.GasBag ? 2.5f : 12f;
            Vector3 v = c.Velocity * MathF.Max(0f, 1f - dt) + Vector3.Down * g * dt;
            Vector3 p = c.Position + v * dt;
            if (p.Y < ground) { p.Y = ground; v = Vector3.Zero; }
            c.Velocity = v;
            c.Position = p;
        }
        else
        {
            c.Velocity = Vector3.Zero;
            c.Position = new Vector3(c.Position.X, ground, c.Position.Z);
            c.Landed = true;
        }
    }

    /// <summary>
    /// Nothing passes through a person: if any part of a body (head, neck, torso, by the rig's own
    /// spheres) has come into theirs, the whole creature is put back out the way it came. A swarm is
    /// a cloud and doesn't count; a lunge plans its own stop.
    /// </summary>
    public static void KeepOff(WildSim w, Creature c)
    {
        Rig rig = c.Rig;
        bool glider = c.Species.Flight == FlightKind.Glide && !c.Landed; // it can't step aside: it lifts over you, and a dive is held to this too
        if (rig == null || c.Dead || c.InWalls || c.Falling || (c.Phase == AttackPhase.Lunge && !glider) || c.Phase == AttackPhase.Hold || c.Species.Rig == RigKind.Swarm) return;
        if (c.Space != null && c.Facing.Y.Y < -0.3f) return; // on a ceiling: over your head, whatever the headroom
        foreach (ITarget t in w.Targets)
        {
            if (!t.Attackable) continue;
            Vector3 feet = t.Position;
            float reach = rig.BoundsRadius + Attacks.BodyRadius;
            if ((rig.BoundsCenter - feet - Vector3.Up).LengthSquared() > (reach + 1f) * (reach + 1f)) continue;
            float worst = 0f;
            Vector3 push = Vector3.Zero;
            for (int i = 0; i < rig.HitCount; i++)
            {
                Vector4 s = rig.Hits[i];
                if (s.Y + s.W < feet.Y || s.Y - s.W > feet.Y + Attacks.BodyHeight) continue;
                Vector3 away = WildSim.Flat(new Vector3(s.X, s.Y, s.Z) - feet);
                float d = away.Length(), into = s.W + Attacks.BodyRadius - d;
                if (into <= worst) continue;
                worst = into;
                push = d > 1e-3f ? away / d : -c.Forward;
            }
            if (worst <= 0f) continue;
            if (glider)
            {
                c.Position += Vector3.Up * MathF.Min(worst, 0.2f);
                continue;
            }
            // No faster than it can move itself: a push is still walking, feet and all.
            Vector3 moved = c.Position + push * MathF.Min(worst, MathF.Max(c.Stats.RunSpeed * 1.2f, 3f) / 60f);
            if (c.Space != null)
            {
                // Along what it stands on, whichever way that faces.
                Vector3 center = moved + c.Facing.Y * c.Clearance;
                if (!SpaceMath.Project(c.Space, ref center, c.Clearance, out Vector3 n)) continue;
                if (c.Species.Climbs) moved = center - c.Facing.Y * c.Clearance;
                else if (c.Space.Density(center) > -0.02f) continue;
            }
            else
            {
                moved = Bound(w, moved, ref c.Velocity);
                if (!c.Species.Flies && c.Hop == HopPhase.Ground) moved.Y = w.Terrain.Height(moved.X, moved.Z);
                // Never shoved into a wall.
                if (Interpenetration.SolidSurface(w, moved, c.Species) && !c.Species.Flies) continue;
            }
            float closing = c.Velocity.Dot(push);
            if (closing < 0f) c.Velocity -= push * closing;
            c.CrawlSpeed *= 0.5f;
            c.Position = moved;
        }
    }

    /// <summary>Push out of rocks and trunks, and stop pushing into them.</summary>
    internal static void Avoid(WildSim w, Creature c, ref Vector3 p, ref Vector3 v)
    {
        var near = w.Nav.Near(p);
        if (near == null) return;
        foreach (Obstacle o in near)
        {
            if (!o.Active) continue;
            Vector3 away = WildSim.Flat(p - o.Position);
            float d = away.Length(), min = o.Radius + c.Radius * 0.5f;
            if (d >= min) continue;
            Vector3 n = d > 1e-3f ? away / d : Vector3.Right;
            p += n * (min - d);
            float into = v.Dot(n);
            if (into < 0f) v -= n * into;
        }
    }

    internal static Vector3 Bound(WildSim w, Vector3 p, ref Vector3 v)
    {
        float limit = w.Terrain.HalfSize - 6f;
        if (MathF.Abs(p.X) > limit) { p.X = Math.Clamp(p.X, -limit, limit); v.X = 0f; }
        if (MathF.Abs(p.Z) > limit) { p.Z = Math.Clamp(p.Z, -limit, limit); v.Z = 0f; }
        return p;
    }

    /// <summary>Turn towards the way it is moving, or towards what it faces when standing still.</summary>
    internal static void Turn(Creature c, Vector3 v, float dt, float speed = 0f)
    {
        float rate = Heft.TurnRate(c, speed);
        Vector3 dir = v;
        if (dir.Length() < 0.15f && c.FacePoint is { } face) dir = WildSim.Flat(face - c.Position);
        if (dir.Length() < 0.05f) return;
        c.Yaw = Mathf.RotateToward(c.Yaw, MathF.Atan2(-dir.X, -dir.Z), rate * dt);
    }
}
