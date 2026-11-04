using System;
using Godot;

namespace Wildlife;

/// <summary>
/// Moves creatures over the terrain with simple steering: accelerate towards the velocity
/// the brain wants, slide around rocks and trunks, stay on the height function. One mover
/// per kind of body: walkers and crawlers, hoppers, fliers, drifting gas bags. Lunges move
/// along a straight line from where the creature launched to where it committed.
/// </summary>
public static class Locomotion
{
    const float Gravity = 18f; // snappy hops read better than real ones

    public static void Move(WildSim w, Creature c, float dt)
    {
        if (c.Dead)
        {
            Fall(w, c, dt);
            return;
        }
        if (c.Phase == AttackPhase.Lunge)
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
            case RigKind.Winged: Fly(w, c, dt); break;
            case RigKind.GasBag: Drift(w, c, dt); break;
            case RigKind.Swarm when c.Species.Flies: Hover(w, c, dt); break;
            default: Ground(w, c, dt); break;
        }
    }

    static void Ground(WildSim w, Creature c, float dt)
    {
        Vector3 v = WildSim.Flat(c.Velocity);
        float accel = c.Stats.RunSpeed * 3f + 2f;
        v = v.MoveToward(WildSim.Flat(c.Desired), accel * dt);
        Vector3 p = c.Position + v * dt;
        Avoid(w, c, ref p, ref v);
        p = Bound(w, p, ref v);
        p.Y = w.Terrain.Height(p.X, p.Z);
        c.Velocity = v;
        c.Position = p;
        Turn(c, v, dt);
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

    /// <summary>Fliers hold a height above ground, bank into turns and land to eat or rest.</summary>
    static void Fly(WildSim w, Creature c, float dt)
    {
        float ground = w.Terrain.Height(c.Position.X, c.Position.Z);
        if (c.Landed)
        {
            if (c.Altitude > 0.5f)
            {
                c.Landed = false;
                c.Velocity = new Vector3(c.Velocity.X, 4f, c.Velocity.Z);
            }
            else
            {
                Vector3 vg = WildSim.Flat(c.Velocity).MoveToward(WildSim.Flat(c.Desired) * 0.3f, 4f * dt);
                Vector3 pg = Bound(w, c.Position + vg * dt, ref vg);
                pg.Y = w.Terrain.Height(pg.X, pg.Z);
                c.Velocity = vg;
                c.Position = pg;
                Turn(c, vg, dt);
                if (c.FacePoint is { } face) Turn(c, WildSim.Flat(face - c.Position), dt);
                return;
            }
        }

        Vector3 flat = WildSim.Flat(c.Velocity);
        Vector3 want = WildSim.Flat(c.Desired);
        if (want.Length() < c.Stats.WalkSpeed * 0.4f && c.Altitude > 0.5f)
            want = c.Forward * c.Stats.WalkSpeed * 0.6f + c.Forward.Cross(Vector3.Up) * 1.2f; // idle: lazy circles
        flat = flat.MoveToward(want, 6f * dt);
        float targetY = ground + c.Altitude;
        float vy = Math.Clamp((targetY - c.Position.Y) * 1.2f, -5f, 3.5f);
        float vertical = Mathf.MoveToward(c.Velocity.Y, vy, 8f * dt);
        Vector3 p = c.Position + (flat + Vector3.Up * vertical) * dt;
        p = Bound(w, p, ref flat);
        float g = w.Terrain.Height(p.X, p.Z);
        if (p.Y < g + 0.1f) p.Y = g + 0.1f;
        c.Velocity = flat + Vector3.Up * vertical;
        c.Position = p;
        Turn(c, flat, dt);
        if (c.Altitude <= 0.5f && p.Y - g < 0.35f)
        {
            c.Landed = true;
            c.Position = new Vector3(p.X, g, p.Z);
            c.Velocity = Vector3.Zero;
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
        else if (c.Species.Flies) p.Y = Mathf.Lerp(c.LungeFrom.Y, ground + 1f, eased);
        else p.Y = ground;
        c.Velocity = (c.LungeTo - c.LungeFrom) / MathF.Max(0.01f, c.PhaseLength);
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

    /// <summary>Push out of rocks and trunks, and stop pushing into them.</summary>
    static void Avoid(WildSim w, Creature c, ref Vector3 p, ref Vector3 v)
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

    static Vector3 Bound(WildSim w, Vector3 p, ref Vector3 v)
    {
        float limit = w.Terrain.HalfSize - 6f;
        if (MathF.Abs(p.X) > limit) { p.X = Math.Clamp(p.X, -limit, limit); v.X = 0f; }
        if (MathF.Abs(p.Z) > limit) { p.Z = Math.Clamp(p.Z, -limit, limit); v.Z = 0f; }
        return p;
    }

    /// <summary>Turn towards the way it is moving, or towards what it faces when standing still.</summary>
    static void Turn(Creature c, Vector3 v, float dt)
    {
        float rate = Math.Clamp(5f / MathF.Max(0.6f, c.Species.Body.Size), 0.5f, 6f);
        Vector3 dir = v;
        if (dir.Length() < 0.15f && c.FacePoint is { } face) dir = WildSim.Flat(face - c.Position);
        if (dir.Length() < 0.05f) return;
        c.Yaw = Mathf.RotateToward(c.Yaw, MathF.Atan2(-dir.X, -dir.Z), rate * dt);
    }
}
