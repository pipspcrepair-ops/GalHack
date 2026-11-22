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
        if (c.Space == null && !c.Falling && !(c.Species.Flies && !c.Landed) && Swim(w, c, dt)) return;
        if (Unfooted(w, c))
        {
            FreeFall(w, c, dt);
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

    /// <summary>How far over the ground under it a body on its feet may find itself before it falls, m.</summary>
    const float FootSlack = 0.25f;

    /// <summary>The most it steps down in a stride going down a slope, m: more, and it is over a drop.</summary>
    const float StrideDrop = 0.5f;

    /// <summary>
    /// What its feet stand on (patch frame): the ground under its middle, or where that is a hole narrower than it (a giant over a
    /// pit), the ground under three of its four feet, round it at its body's reach.
    /// </summary>
    public static float Footing(WildSim w, Creature c, float x, float z)
    {
        float r = c.Radius * 0.7f, h = w.Terrain.Height(x, z);
        float a = w.Terrain.Height(x + r, z), b = w.Terrain.Height(x - r, z), d = w.Terrain.Height(x, z + r), e = w.Terrain.Height(x, z - r);
        // (The second lowest of the four, the lowest that three of its feet stand on or over: of each pair's lower and higher.)
        float lowA = MathF.Min(a, b), highA = MathF.Max(a, b), lowB = MathF.Min(d, e), highB = MathF.Max(d, e);
        return MathF.Max(h, MathF.Min(MathF.Max(lowA, lowB), MathF.Min(highA, highB)));
    }

    /// <summary>The ground under it changed (dug, blasted): looked under now, asleep or not; with nothing under its feet it falls (and so is stepped till down).</summary>
    public static void LookUnder(WildSim w, Creature c)
    {
        if (c.Space == null && !c.Dead && !c.Thrown) Unfooted(w, c);
    }

    /// <summary>
    /// Nothing under its feet (the ground dug or blasted away from under it): anything that walks, crawls or stands, and a flier
    /// standing on the ground, falls. Its feet are on the ground after every step, so only the ground changing finds this.
    /// </summary>
    static bool Unfooted(WildSim w, Creature c)
    {
        if (c.Falling) return true;
        if (c.Species.Flies && !c.Landed || c.Species.Rig == RigKind.GasBag || c.Hop == HopPhase.Air) return false;
        if (c.Position.Y - Footing(w, c, c.Position.X, c.Position.Z) <= FootSlack) return false;
        c.Falling = true;
        c.Velocity = Vector3.Zero;
        return true;
    }

    /// <summary>Falling under the world's pull (<see cref="Drop"/>) to the ground under it, landing on its feet (<see cref="WildSim.Landed"/>).</summary>
    static void FreeFall(WildSim w, Creature c, float dt)
    {
        float y = c.Position.Y, speed = MathF.Max(0f, -c.Velocity.Y), ground = Footing(w, c, c.Position.X, c.Position.Z);
        // (Into water: down to where it floats, or to the bottom if it can stand there or sinks; the water takes the blow.)
        bool wet = w.WaterAt?.Invoke(c.Position.X, c.Position.Z) is float top && top > ground;
        if (wet && Shore.Swims(c.Species)) ground = MathF.Max(ground, w.WaterAt(c.Position.X, c.Position.Z).Value - c.Species.BodyHeight * w.SwimShare);
        bool down = Drop.Step(ref y, ref speed, ground, w.Gravity, dt);
        c.Position = new Vector3(c.Position.X, y, c.Position.Z);
        c.Velocity = new Vector3(0f, -speed, 0f);
        if (!down) return;
        c.Falling = false;
        c.Velocity = Vector3.Zero;
        w.Landed(c, wet ? 0f : speed);
    }

    /// <summary>Water no deeper than this (or than it wades) it goes about in as on land, at the edge to drink; deeper, it makes for the bank, m.</summary>
    const float Paddle = 0.3f;

    /// <summary>
    /// In water deeper than it paddles in (<see cref="Paddle"/>): a swimmer swims (its back at the surface) for the nearest bank it can climb out on and
    /// walks up out of it; what can't swim walks the bottom for it where its head is clear, and drowns where it isn't. Out of its
    /// depth with no bank in reach, it treads water and looks again. True while the water has it (and its own mind waits).
    /// </summary>
    static bool Swim(WildSim w, Creature c, float dt)
    {
        if (w.WaterAt?.Invoke(c.Position.X, c.Position.Z) is not float top || c.Position.Y > top + 0.05f) // (over it, it falls in first: Unfooted)
        {
            (c.Shoreward, c.ShorewardReach) = (null, 0f);
            return false;
        }
        float bottom = Footing(w, c, c.Position.X, c.Position.Z), depth = top - bottom, h = c.Species.BodyHeight;
        if (c.Shoreward == null && depth <= MathF.Min(h * w.WadeShare, Paddle)) return false; // (paddling at the edge, drinking: on its feet as on land)
        bool swims = Shore.Swims(c.Species), deep = depth > h * w.SwimShare;
        if (c.Shoreward == null && w.Time - c.ShorewardAt > 5f)
        {
            c.ShorewardAt = w.Time;
            c.ShorewardReach = c.ShorewardReach <= 0f ? Shore.Reach : MathF.Min(c.ShorewardReach * 2f, Shore.FarReach);
            // (Nothing it can step out onto even far off: a bank as high as its body, scrambled up.)
            float climb = c.ShorewardReach >= Shore.FarReach ? MathF.Max(BankClimb(c), c.Species.BodyHeight) : BankClimb(c);
            c.Shoreward = Shore.Nearest(c.Position, w.WaterAt, w.Terrain.Height, climb, c.ShorewardReach);
        }
        if (!swims && depth > h)
        {
            // Out of its depth and too heavy to swim: it sinks, and drowns.
            c.Position = c.Position with { Y = Mathf.MoveToward(c.Position.Y, bottom, 1.5f * dt) };
            c.Velocity = Vector3.Zero;
            w.Damage(c, c.Stats.MaxHealth * 0.12f * dt, null);
            return true;
        }
        Vector3 to = c.Shoreward is { } bank ? WildSim.Flat(bank - c.Position) : Vector3.Zero;
        float speed = to.LengthSquared() < 0.01f ? 0f : (swims && deep ? 0.6f : 0.4f) * c.Stats.WalkSpeed;
        Vector3 dir = speed > 0f ? to.Normalized() : c.Forward;
        Turn(c, dir, dt);
        Vector3 p = c.Position + c.Forward * speed * dt;
        p = Bound(w, p, ref dir);
        // Its back at the surface where it swims; on the bottom where it wades out (up the bank at a walk, never a leap).
        float floatAt = top - h * w.SwimShare, under = Footing(w, c, p.X, p.Z);
        p.Y = swims && deep ? Mathf.MoveToward(c.Position.Y, MathF.Max(floatAt, under), 2f * dt) : under;
        if (under - c.Position.Y > 1.5f * dt) p = c.Position with { Y = c.Position.Y + 1.5f * dt }; // (the bed or the bank rising steeper than a walk ahead: it clambers up where it is, never a hop)
        c.Velocity = c.Forward * speed;
        c.Position = p;
        if (w.WaterAt(p.X, p.Z) == null) c.Shoreward = null; // (out on the bank: its own way again)
        return true;
    }

    /// <summary>How high over the water a bank may stand for a creature to climb out onto it (its own legs' reach), m.</summary>
    static float BankClimb(Creature c) => MathF.Max(0.5f, c.Species.Body.LegLength * 0.8f);

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
        else if (c.Backing)
        {
            // Stepping back or aside with its eyes on something: it keeps facing it and doesn't turn to go.
            Turn(c, Vector3.Zero, dt);
            v = v.MoveToward(want, Heft.Accel(c) * dt);
        }
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
        // Never into another body: along its side instead.
        Vector3 held = w.Unpressed(c, c.Position, p);
        if (held != p) (p, v) = (held, WildSim.Flat(held - c.Position) / MathF.Max(dt, 1e-4f));
        p = Bound(w, p, ref v);
        // On its feet on the ground under them; a drop more than a stride down a slope it goes over, and falls (Unfooted).
        p.Y = Footing(w, c, p.X, p.Z);
        if (c.Position.Y - p.Y > StrideDrop) p.Y = c.Position.Y;
        KeepOffFaces(w, c, ref p, ref v);
        if (w.SolidAt != null) KeepOutOfSolid(w, c, ref p, ref v);
        // Nor turned off a face or out of the solid into another body: along it, or where it stood if that takes it up a face.
        if (w.Unpressed(c, c.Position, p) is var clear && clear != p) p = Face(w, c.Position, clear, Brain.GaitOf(c.Species)) ? c.Position : clear with { Y = Footing(w, c, clear.X, clear.Z) };
        c.Velocity = v;
        c.Position = p;
    }

    /// <summary>
    /// Ground its legs can't take is a wall to it (<see cref="Brain.GaitOf"/>): a step up a face steeper than its limit, or
    /// off an edge higher than it drops, goes along it instead, the nearest way round to the one it wanted that is still
    /// walkable (along the foot of a face as along a wall), or doesn't happen. (Its way goes round such ground; this is the
    /// body's own say, whatever pushed it.)
    /// </summary>
    static void KeepOffFaces(WildSim w, Creature c, ref Vector3 p, ref Vector3 v)
    {
        NavGrid.Gait g = Brain.GaitOf(c.Species);
        if (g.Any || !Face(w, c.Position, p, g)) return;
        Vector3 step = WildSim.Flat(p - c.Position);
        foreach (float a in Along)
        {
            float k = MathF.Cos(a);
            Vector3 q = c.Position + step.Rotated(Vector3.Up, a) * k;
            if (Face(w, c.Position, q, g)) continue;
            (p, v) = (q with { Y = w.Terrain.Height(q.X, q.Z) }, WildSim.Flat(v).Rotated(Vector3.Up, a) * k);
            return;
        }
        (p, v) = (c.Position, Vector3.Zero);
    }

    /// <summary>The turns off a step it tries, nearest first, radians.</summary>
    static readonly float[] Along = { 0.45f, -0.45f, 0.9f, -0.9f, 1.3f, -1.3f };

    /// <summary>
    /// Whether a step from one point to another puts its feet on ground steeper than a gait takes (up it, across it or along
    /// it): from ground it walks never; from a face it is on already, only across it (the step itself rising or falling no
    /// more than its legs take) or down off it by an edge it drops from (the ground three metres on no lower than it drops).
    /// </summary>
    public static bool Face(WildSim w, Vector3 a, Vector3 b, NavGrid.Gait g)
    {
        Vector3 d = WildSim.Flat(b - a);
        float run = d.Length();
        if (g.Any || run < 1e-4f) return false;
        Vector3 n = w.Terrain.Normal(b.X, b.Z);
        float steep = new Vector2(n.X, n.Z).Length() / MathF.Max(n.Y, 1e-3f);
        if (steep <= g.Slope) return false;
        // Never onto a face from ground it walks; one already on a face (a spawn, a landing) only across or down off it, by an edge it drops from.
        Vector3 m = w.Terrain.Normal(a.X, a.Z);
        if (new Vector2(m.X, m.Z).Length() <= g.Slope * m.Y) return true;
        float h = w.Terrain.Height(a.X, a.Z), hb = w.Terrain.Height(b.X, b.Z);
        if (MathF.Abs(hb - h) <= run * g.Slope) return false;
        d /= run;
        if (hb < h && h - w.Terrain.Height(a.X + d.X * 3f, a.Z + d.Z * 3f) > g.Drop) return true;
        return hb >= h;
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
        p.Y = Footing(w, c, p.X, p.Z);
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
        // A lunge into a wall, a trunk or a rock stops there.
        if (!c.Species.Flies && Interpenetration.SolidSurface(w, p, c.Species) && !Interpenetration.SolidSurface(w, c.Position, c.Species) || IntoTrees(w, c, p) || Face(w, c.Position, p, Brain.GaitOf(c.Species)))
        {
            c.LungeFrom = c.LungeTo = c.Position; // held where it met it, not drawn back along the way it came
            c.LungeBlocked = true;
            c.Velocity = Vector3.Zero;
            return;
        }
        // Nor through anyone but whom it strikes at (owner: melee, swings and charges never through an ally): it stops at them.
        if (!c.Species.Flies && w.IntoBody(c, p, c.AttackTarget))
        {
            c.LungeFrom = c.LungeTo = c.Position;
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
            float g = c.Species.Rig == RigKind.GasBag ? 2.5f : w.Gravity;
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
                // Nor a walker pushed into a piece of furniture (its body as it really stands).
                if (!c.Species.Climbs && c.Space.Parts is { } parts && !parts.Fits(moved, SpaceNav.PartRadius(c.Species), SpaceNav.PartHeight(c.Species))
                    && parts.Gap(moved, SpaceNav.PartRadius(c.Species), SpaceNav.PartHeight(c.Species)) < parts.Gap(c.Position, SpaceNav.PartRadius(c.Species), SpaceNav.PartHeight(c.Species))) continue;
            }
            else
            {
                moved = Bound(w, moved, ref c.Velocity);
                if (!c.Species.Flies && c.Hop == HopPhase.Ground) moved.Y = w.Terrain.Height(moved.X, moved.Z);
                // Never shoved into a wall, a trunk or a rock.
                if (Interpenetration.SolidSurface(w, moved, c.Species) && !c.Species.Flies) continue;
                if (IntoTrees(w, c, moved) || Face(w, c.Position, moved, Brain.GaitOf(c.Species))) continue;
            }
            float closing = c.Velocity.Dot(push);
            if (closing < 0f) c.Velocity -= push * closing;
            c.CrawlSpeed *= 0.5f;
            c.Position = moved;
        }
    }

    /// <summary>How wide a body is round its feet where trunks and rocks are concerned, m: a person's shoulders, an animal's flanks.</summary>
    public static float Footprint(Species s) => s.Figure != null ? 0.3f : MathF.Max(0.12f, s.Body.Width * 0.5f);

    /// <summary>How far a body standing at p would be into the nearest trunk or rock, m (0 or less: clear of them all).</summary>
    public static float IntoObstacles(WildSim w, Creature c, Vector3 p)
    {
        float r = Footprint(c.Species), worst = 0f;
        foreach (Obstacle o in w.Nav.Around(p, r))
            if (o.Active) worst = MathF.Max(worst, o.Radius + r - WildSim.Flat(p - o.Position).Length());
        return worst;
    }

    /// <summary>Whether a walker moved to p would be further into a trunk or a rock than it is now (a lunge, a shove or a jostle that may not happen).</summary>
    public static bool IntoTrees(WildSim w, Creature c, Vector3 p) =>
        !c.Species.Flies && IntoObstacles(w, c, p) > MathF.Max(0.01f, IntoObstacles(w, c, c.Position));

    /// <summary>
    /// Push out of rocks and trunks, and stop pushing into them: it slides round them. Squeezed between two closer
    /// than it is wide, it stays where it was rather than pass into either (a giant keeps out of a tight wood).
    /// </summary>
    internal static void Avoid(WildSim w, Creature c, ref Vector3 p, ref Vector3 v)
    {
        float r = Footprint(c.Species);
        var near = w.Nav.Around(p, r);
        if (near.Count == 0) return;
        for (int pass = 0; pass < 2; pass++)
            foreach (Obstacle o in near)
            {
                if (!o.Active) continue;
                Vector3 away = WildSim.Flat(p - o.Position);
                float d = away.Length(), min = o.Radius + r;
                if (d >= min) continue;
                Vector3 n = d > 1e-3f ? away / d : Vector3.Right;
                p += n * (min - d);
                float into = v.Dot(n), speed = v.Length();
                if (into >= 0f) continue;
                // Round it at the pace it came: what went into the trunk goes along its side (only dead on does it stop).
                v -= n * into;
                float along = v.Length();
                if (along > 1e-3f) v *= speed / along;
            }
        float now = IntoObstacles(w, c, p);
        if (now > 0.01f && now >= IntoObstacles(w, c, c.Position) - 1e-4f)
        {
            p = new Vector3(c.Position.X, p.Y, c.Position.Z);
            v = Vector3.Zero;
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
