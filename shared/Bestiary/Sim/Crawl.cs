using System;
using Godot;

namespace Wildlife;

/// <summary>
/// Moving on the surfaces of a cave or a place (an <see cref="ISolidSpace"/>), for creatures
/// that live inside one. The body's centre rides a surface held its clearance off the solid,
/// found by Newton steps along the density's gradient, so it can never sink into rock:
///
/// - Turning a corner onto a wall or a ceiling needs no special case. Walking into a wall pulls
///   the body onto the wall's surface; the heading is carried over the bend by the smallest
///   rotation that takes the old surface normal onto the new one (parallel transport), so
///   "forward into the wall" becomes "up the wall", and up the wall into the ceiling becomes
///   "along the ceiling". Round an outside edge the body swings round it at its clearance.
/// - Floor walkers (anything that doesn't climb) refuse a surface steeper than their limit:
///   they stop at a wall and slide along it.
/// - Small substeps keep a fast creature from skipping through a thin wall.
/// - Something that lets go (a drop, a leap, a death on the ceiling) falls under gravity until
///   it lands on whatever is below. If it ever loses every surface (it never should), it is put
///   back where it last stood firmly and <see cref="WildSim"/> counts a rescue.
/// </summary>
public static class Crawl
{
    /// <summary>How far a species' body rides off the surface it holds.</summary>
    public static float ClearanceFor(Species s) =>
        // A climber rides its own half-thickness off the face it holds (a big insect is no flatter than a small one).
        s.Climbs ? Math.Clamp(s.Body.Height * 0.55f, 0.1f, 0.6f) :Math.Clamp(s.Stats.Radius * 0.8f, 0.3f, 0.7f);

    /// <summary>The body's centre: the surface point lifted by the clearance along the surface normal.</summary>
    public static Vector3 Center(Creature c) => c.Position + c.Facing.Y * c.Clearance;

    /// <summary>Puts a creature on the surface nearest a point.</summary>
    public static void Place(WildSim w, Creature c, Vector3 near, Vector3? up)
    {
        ISolidSpace s = c.Species.Climbs ? c.Space : c.Space.Bare;
        float clr = c.Clearance;
        Vector3 p = near, n = Vector3.Up;
        // A walker is set down clear of the furniture: beside it, never in it.
        if (!c.Species.Climbs && c.Space.Parts is { } parts) near = parts.PushOut(near, SpaceNav.PartRadius(c.Species), SpaceNav.PartHeight(c.Species));
        if (!c.Species.Climbs || up == null)
        {
            float? floor = SpaceMath.FloorBelow(s, near.X, near.Y + 0.5f, near.Z, 8f);
            p = new Vector3(near.X, (floor ?? near.Y) + clr, near.Z);
        }
        else
        {
            Vector3 dir = -up.Value.Normalized();
            float t = SpaceMath.Ray(s, near, dir, 8f);
            p = near + dir * MathF.Max(0f, t - clr);
        }
        if (SpaceMath.Project(s, ref p, clr, out Vector3 normal)) n = normal;
        Vector3 f = c.Forward;
        c.Facing = SpaceMath.Frame(f, n);
        c.Position = p - n * clr;
        c.LastFirm = c.Position;
        c.LastFirmNormal = n;
        c.Falling = false;
        c.Velocity = Vector3.Zero;
        SyncYaw(c);
    }

    public static void Move(WildSim w, Creature c, float dt)
    {
        if (c.InWalls) return; // the brain moves it through the rock; nothing to draw or collide
        if (c.Phase == AttackPhase.Lunge)
        {
            Lunge(w, c);
            return;
        }
        if (c.Dead && !c.Falling && c.Facing.Y.Y < 0.6f) Release(w, c); // the dead let go of walls and ceilings
        if (c.Falling)
        {
            Fall(w, c, dt);
            return;
        }
        if (c.Dead)
        {
            c.Velocity = Vector3.Zero;
            c.CrawlSpeed = 0f;
            return;
        }
        Surface(w, c, dt);
    }

    /// <summary>Walking (or crawling) along the surface it holds, towards what the brain wants.</summary>
    static void Surface(WildSim w, Creature c, float dt)
    {
        if (!c.Species.Climbs)
        {
            Walk(w, c, dt);
            return;
        }
        ISolidSpace s = c.Space;
        CrawlRules r = w.Data.Deep.Crawl;
        float clr = c.Clearance;
        bool climbs = c.Species.Climbs;
        // Walkers stand upright on floors; only climbers lie against the surface they hold.
        Vector3 n = climbs ? c.Facing.Y : Vector3.Up, f = -c.Facing.Z;
        if (!climbs)
        {
            f.Y = 0f;
            f = f.LengthSquared() > 1e-4f ? f.Normalized() : c.Forward;
        }
        Vector3 center = c.Position + n * clr, start = center;

        // What the brain wants, laid flat on the surface.
        Vector3 want = c.Desired - n * c.Desired.Dot(n);
        float wantSpeed = want.Length();
        float turn = Heft.TurnRate(c, c.CrawlSpeed) * dt;
        // Wanting to go straight out from the surface (the next stretch of the way lies across an
        // inside corner): keep crawling the way it faces, into the corner and round onto the next face.
        float raw = c.Desired.Length();
        if (c.Species.Climbs && raw > 0.05f && wantSpeed < raw * 0.35f)
        {
            want = -c.Facing.Z * raw * 0.8f;
            wantSpeed = raw * 0.8f;
        }
        Vector3 aimDir = wantSpeed > 0.05f ? want / wantSpeed : Vector3.Zero;
        if (aimDir == Vector3.Zero && c.FacePoint is { } face)
        {
            Vector3 to = face - center;
            to -= n * to.Dot(n);
            if (to.LengthSquared() > 0.01f) aimDir = to.Normalized();
        }
        if (aimDir != Vector3.Zero) f = TurnToward(f, aimDir, n, turn);
        // Crawlers walk the way they face: slow while still turning to the way they want to go.
        float align = aimDir == Vector3.Zero ? 0f : MathF.Max(0f, f.Dot(aimDir));
        float target = wantSpeed * (0.25f + 0.75f * align);
        c.CrawlSpeed = Mathf.MoveToward(c.CrawlSpeed, target, (target > c.CrawlSpeed ? Heft.Accel(c) : Heft.Brake(c)) * dt);

        float distance = c.CrawlSpeed * dt;
        int steps = Math.Max(1, (int)MathF.Ceiling(distance / r.Substep));
        float ds = distance / steps;
        for (int i = 0; i < steps && ds > 1e-5f; i++)
        {
            bool moved = StepOnce(s, ref center, ref n, ref f, ds, clr, climbs, r.FloorUp);
            // A wall in a walker's way: slide along it instead.
            if (!moved && !climbs) moved = Slide(s, ref center, ref n, ref f, ds, clr, r.FloorUp);
            if (!climbs)
            {
                n = Vector3.Up;
                f.Y = 0f;
                f = f.LengthSquared() > 1e-4f ? f.Normalized() : c.Forward;
            }
            if (!moved)
            {
                c.CrawlSpeed *= 0.5f;
                break;
            }
        }

        // Holding on: still on a surface? If the ground fell away (dug out from under it), fall.
        float d = SpaceMath.Distance(s, center, out Vector3 now);
        if (now == Vector3.Zero || d < -clr - 0.45f)
        {
            Release(w, c);
            c.Position = center - n * clr;
            return;
        }

        c.Facing = SpaceMath.Frame(f, n);
        c.Velocity = (center - start) / MathF.Max(1e-4f, dt);
        if (climbs) c.Position = center - n * clr;
        else
        {
            // A walker's ground point is the floor straight under it.
            float? floor = SpaceMath.FloorBelow(s, center.X, center.Y, center.Z, clr + 1.2f);
            c.Position = new Vector3(center.X, floor ?? center.Y - clr, center.Z);
        }
        if (n.Y > 0.6f || climbs)
        {
            c.LastFirm = c.Position;
            c.LastFirmNormal = n;
        }
        SyncYaw(c);
    }

    /// <summary>
    /// A floor walker in a cave or a place: level steps across the floor, following its height
    /// (up small steps, never off a ledge it can't climb back up), and held off walls by its
    /// body's clearance, sliding along them. Simpler than crawling, and it can't wedge in a corner.
    /// </summary>
    static void Walk(WildSim w, Creature c, float dt)
    {
        // A walker stands on the floors and stairs and is held off the walls; the place's parts
        // (furniture, clutter) it goes round or under, its body never in one (see Parts).
        ISolidSpace s = c.Space.Bare;
        PartSet parts = c.Space.Parts;
        float pr = SpaceNav.PartRadius(c.Species), ph = SpaceNav.PartHeight(c.Species);
        CrawlRules r = w.Data.Deep.Crawl;
        float clr = c.Clearance;
        Vector3 f = -c.Facing.Z;
        f.Y = 0f;
        f = f.LengthSquared() > 1e-4f ? f.Normalized() : c.Forward;
        Vector3 feet = c.Position, start = feet;

        Vector3 want = new(c.Desired.X, 0f, c.Desired.Z);
        float wantSpeed = want.Length();
        float turn = Heft.TurnRate(c, c.CrawlSpeed) * dt;
        Vector3 aimDir = wantSpeed > 0.05f ? want / wantSpeed : Vector3.Zero;
        if (aimDir == Vector3.Zero && c.FacePoint is { } face)
        {
            Vector3 to = face - feet;
            to.Y = 0f;
            if (to.LengthSquared() > 0.01f) aimDir = to.Normalized();
        }
        if (aimDir != Vector3.Zero) f = TurnToward(f, aimDir, Vector3.Up, turn);
        float align = aimDir == Vector3.Zero ? 0f : MathF.Max(0f, f.Dot(aimDir));
        float aimed = wantSpeed * (0.25f + 0.75f * align);
        c.CrawlSpeed = Mathf.MoveToward(c.CrawlSpeed, aimed, (aimed > c.CrawlSpeed ? Heft.Accel(c) : Heft.Brake(c)) * dt);

        float distance = c.CrawlSpeed * dt;
        int steps = Math.Max(1, (int)MathF.Ceiling(distance / r.Substep));
        float ds = distance / steps;
        for (int i = 0; i < steps && ds > 1e-5f; i++)
        {
            Vector3 next = feet + f * ds;
            float? floor = SpaceMath.FloorBelow(s, next.X, feet.Y + 0.45f, next.Z, 1.4f);
            // No floor (a hole, a shaft), a drop it couldn't climb back, or a step too tall: not that way.
            if (floor is not float fy || fy - feet.Y > 0.4f || feet.Y - fy > 1.2f)
            {
                c.CrawlSpeed *= 0.5f;
                break;
            }
            next.Y = fy;
            next = HoldOffWalls(s, next, clr);
            if (parts != null)
            {
                // Off the furniture sideways (sliding round a leg or a corner); where that can't be done, no further.
                next = HoldOffWalls(s, parts.PushOut(next, pr, ph), clr);
                if (!parts.Fits(next, pr, ph) && parts.Gap(next, pr, ph) < parts.Gap(feet, pr, ph))
                {
                    c.CrawlSpeed *= 0.5f;
                    break;
                }
            }
            feet = next;
        }
        // Whatever moved it (a landing, a lunge), it ends clear of the walls and the furniture, on the floor.
        feet = HoldOffWalls(s, feet, clr);
        if (parts != null && !parts.Fits(feet, pr, ph))
        {
            feet = HoldOffWalls(s, parts.PushOut(feet, pr, ph), clr);
            // Held between a wall and a piece with no room for it: where it stood, never in the piece.
            if (!parts.Fits(feet, pr, ph) && parts.Gap(start, pr, ph) > parts.Gap(feet, pr, ph)) feet = start;
        }
        if (SpaceMath.FloorBelow(s, feet.X, feet.Y + 0.45f, feet.Z, 1.4f) is float y) feet.Y = y;
        else
        {
            Release(w, c);
            return;
        }
        c.Facing = SpaceMath.Frame(f, Vector3.Up);
        c.Velocity = (feet - start) / MathF.Max(1e-4f, dt);
        c.Position = feet;
        c.LastFirm = feet;
        c.LastFirmNormal = Vector3.Up;
        SyncYaw(c);
    }

    /// <summary>Pushes a walker's body (its centre a clearance over its feet) out from any wall closer than its clearance, sideways only.</summary>
    static Vector3 HoldOffWalls(ISolidSpace s, Vector3 feet, float clr)
    {
        for (int k = 0; k < 3; k++)
        {
            Vector3 body = feet + Vector3.Up * (clr + 0.12f);
            float d = SpaceMath.Distance(s, body, out Vector3 n);
            if (n == Vector3.Zero || d < -clr) break;
            Vector3 side = new(n.X, 0f, n.Z);
            if (side.LengthSquared() < 1e-4f) break; // the only thing near is the floor or the ceiling
            if (n.Y > 0.3f) break; // under the body, not beside it: a ramp or a flight it stands on, the crest at its head
            side = side.Normalized();
            feet += side * MathF.Min(0.3f, d + clr + 0.005f);
        }
        return feet;
    }

    /// <summary>One small move along the surface; false if the way is barred (a wall for a walker, a gap, a jump).</summary>
    static bool StepOnce(ISolidSpace s, ref Vector3 center, ref Vector3 n, ref Vector3 f, float ds, float clr, bool climbs, float floorUp)
    {
        Vector3 next = center + f * ds;
        if (!SpaceMath.Project(s, ref next, clr, out Vector3 n1))
        {
            // Off an outside edge with nothing in reach: swing down round it.
            next = center + f * ds - n * ds * 1.5f;
            if (!SpaceMath.Project(s, ref next, clr, out n1)) return false;
        }
        // The projection may not jump to another surface across a gap.
        if ((next - center).Length() > ds * 3f + 0.05f) return false;
        if (!climbs && n1.Y < floorUp) return false;
        f = SpaceMath.Transport(f, n, n1);
        f -= n1 * f.Dot(n1);
        if (f.LengthSquared() < 1e-6f) f = SpaceMath.Frame(-n, n1) * Vector3.Forward;
        f = f.Normalized();
        center = next;
        n = n1;
        return true;
    }

    /// <summary>A walker against a wall: move along the wall instead of into it.</summary>
    static bool Slide(ISolidSpace s, ref Vector3 center, ref Vector3 n, ref Vector3 f, float ds, float clr, float floorUp)
    {
        Vector3 probe = center + f * (clr + 0.15f);
        Vector3 wall = SpaceMath.Normal(s, probe);
        wall.Y = 0f;
        if (wall.LengthSquared() < 1e-4f) return false;
        wall = wall.Normalized();
        Vector3 along = f - wall * f.Dot(wall);
        along.Y = 0f;
        if (along.LengthSquared() < 0.04f) return false;
        Vector3 f2 = along.Normalized();
        Vector3 c2 = center, n2 = n;
        if (!StepOnce(s, ref c2, ref n2, ref f2, ds, clr, false, floorUp)) return false;
        center = c2;
        n = n2;
        return true;
    }

    /// <summary>Rotates heading f towards dir about the surface normal by at most maxAngle.</summary>
    static Vector3 TurnToward(Vector3 f, Vector3 dir, Vector3 n, float maxAngle)
    {
        float angle = MathF.Atan2(f.Cross(dir).Dot(n), f.Dot(dir));
        angle = Math.Clamp(angle, -maxAngle, maxAngle);
        return f.Rotated(n.Normalized(), angle).Normalized();
    }

    /// <summary>Lets go of the surface: it falls from here.</summary>
    public static void Release(WildSim w, Creature c)
    {
        if (c.Falling) return;
        c.Falling = true;
        c.CrawlSpeed = 0f;
        if (c.Phase != AttackPhase.Lunge) c.Velocity *= 0.3f;
        // Pushes off whatever it held, so it doesn't catch the same wall again.
        c.Velocity += c.Facing.Y * 0.6f;
    }

    /// <summary>Falling: gravity until it lands on whatever is below, landing on its feet.</summary>
    static void Fall(WildSim w, Creature c, float dt)
    {
        // A walker comes down to the floor (it never stands on the furniture; it is moved off it when it lands).
        ISolidSpace s = c.Species.Climbs ? c.Space : c.Space.Bare;
        float clr = c.Clearance, g = w.Data.Deep.Crawl.Gravity;
        Vector3 n = c.Facing.Y;
        Vector3 center = c.Position + n * clr;
        Vector3 v = c.Velocity + Vector3.Down * g * dt;
        v = v.LimitLength(25f);
        Vector3 next = center + v * dt;
        // Turn right way up while falling, like a cat.
        Vector3 up = SpaceMath.TurnToward(n, Vector3.Up, MathF.Min(1f, dt * 8f));
        float d = SpaceMath.Distance(s, next, out Vector3 normal);
        bool needFloor = c.Dead || !c.Species.Climbs || c.Dropping;
        if (d > -clr && normal != Vector3.Zero)
        {
            if (needFloor && normal.Y < w.Data.Deep.Crawl.FloorUp)
            {
                // A body (or a walker) against a wall slides off it, down to the floor.
                SpaceMath.Project(s, ref next, clr, out _);
                v -= normal * MathF.Min(0f, v.Dot(normal));
                v += normal * 1.5f * dt * 10f;
            }
            else
            {
                // Landed: on its feet on whatever it hit (a climber may catch a wall).
                SpaceMath.Project(s, ref next, clr, out Vector3 landed);
                if (landed == Vector3.Zero) landed = normal;
                c.Falling = false;
                c.Dropping = false;
                c.Velocity = Vector3.Zero;
                c.CrawlSpeed = 0f;
                c.Facing = SpaceMath.Frame(-c.Facing.Z, landed);
                c.Position = next - landed * clr;
                SyncYaw(c);
                return;
            }
        }
        c.Velocity = v;
        c.Facing = SpaceMath.Frame(-c.Facing.Z, up);
        c.Position = next - c.Facing.Y * clr;
        // It should always land: if it has fallen out of everything, put it back.
        if (c.LastFirm.Y - c.Position.Y > 40f || !Geo.Finite(c.Position))
        {
            w.RaiseRescued(c);
            c.Position = c.LastFirm;
            c.Facing = SpaceMath.Frame(-c.Facing.Z, c.LastFirmNormal);
            c.Velocity = Vector3.Zero;
            c.Falling = false;
        }
    }

    /// <summary>
    /// The lunge of a creature in a space, from where it held on to the committed aim: a drop
    /// from a ceiling falls (accelerating), a leap from a floor or a wall flies straight.
    /// </summary>
    public static void Launch(WildSim w, Creature c)
    {
        CombatRules r = w.Combat;
        ISolidSpace s = c.Space;
        AttackDef a = c.Species.Attack;
        Vector3 n = c.Facing.Y;
        Vector3 from = c.Position + n * c.Clearance;
        bool prey = c.AttackTarget is CreatureTarget;
        Vector3 aim = c.AimPoint + Vector3.Up * (prey ? 0.15f : 0.8f);
        bool drop = a.Style == AttackStyle.Drop && n.Y < -0.3f;
        // It stops with its jaws at the skin of whoever stands there, from their front if it can.
        float stop = prey ? c.Radius * 0.3f : Attacks.Jaws(c) + Attacks.Stretch(c) + Attacks.BodyRadius;
        Vector3 to = aim + (drop ? Vector3.Zero : Attacks.Approach(c, c.AttackTarget, from, aim) * stop) - from;
        float d = to.Length();
        Vector3 dir = d > 0.01f ? to / d : -c.Facing.Z;
        float reach = c.Stats.AttackRange + (a.Reach > 0f ? a.Reach : r.LungeReach);
        if (drop)
        {
            // A drop falls: as far down as it must, but only a short way across.
            Vector3 flat = new(to.X, 0f, to.Z);
            float across = flat.Length(), maxAcross = MathF.Max(1.5f, c.Species.StrikeRadius + 0.5f);
            if (across > maxAcross)
            {
                to = flat / across * maxAcross + Vector3.Up * to.Y;
                d = to.Length();
                dir = to / d;
            }
            reach = 8f;
        }
        float travel = Math.Clamp(drop ? d - stop : d, 0f, reach);
        // Keep the flight in the open: stop short of rock in the way.
        float free = SpaceMath.Ray(s, from + dir * 0.05f, dir, travel);
        travel = MathF.Min(travel, MathF.Max(0f, free - c.Clearance * 0.5f));
        c.LungeFrom = from;
        c.LungeTo = from + dir * travel;
        Vector3 onto = aim - c.LungeTo;
        c.StrikePoint = c.LungeTo + (onto.LengthSquared() > 1e-4f ? onto.Normalized() : dir) * MathF.Min(stop, onto.Length());
        c.Phase = AttackPhase.Lunge;
        c.PhaseTime = 0f;
        float fallHeight = MathF.Max(0.3f, from.Y - c.LungeTo.Y);
        c.PhaseLength = drop ? Math.Clamp(MathF.Sqrt(2f * fallHeight / w.Data.Deep.Crawl.Gravity), 0.3f, 0.9f) : a.Lunge > 0f ? a.Lunge : r.LungeSeconds;
        c.Rear = 0f;
        c.Brace = 0f;
        c.Falling = false;
        if (MathF.Abs(dir.Y) < 0.95f) c.Yaw = MathF.Atan2(-dir.X, -dir.Z);
    }

    static void Lunge(WildSim w, Creature c)
    {
        float t = Math.Clamp(c.PhaseTime / MathF.Max(0.01f, c.PhaseLength), 0f, 1f);
        bool drop = c.Species.Attack.Style == AttackStyle.Drop && c.LungeFrom.Y - c.LungeTo.Y > 0.3f;
        float eased = drop ? t * t : 1f - (1f - t) * (1f - t);
        Vector3 center = c.LungeFrom.Lerp(c.LungeTo, eased);
        // A straight flight can skim a bulge in the rock: it rides over it.
        float into = c.Space.Density(center) + 0.03f;
        if (into > 0f) center += SpaceMath.Normal(c.Space, center) * into;
        Vector3 travel = c.LungeTo - c.LungeFrom;
        // In flight it turns to face where it is going, feet towards the ground.
        Vector3 up = SpaceMath.TurnToward(c.Facing.Y, Vector3.Up, MathF.Min(1f, t * 1.5f));
        Vector3 f = travel.LengthSquared() > 1e-4f ? travel.Normalized() : -c.Facing.Z;
        c.Facing = SpaceMath.Frame(f, up);
        c.Velocity = travel / MathF.Max(0.01f, c.PhaseLength);
        c.Position = center - c.Facing.Y * c.Clearance;
        SyncYaw(c);
    }

    /// <summary>Its yaw follows its heading, for anything that reads a yaw (rigs, the hovercraft).</summary>
    static void SyncYaw(Creature c)
    {
        Vector3 f = -c.Facing.Z;
        if (MathF.Abs(f.Y) < 0.95f) c.Yaw = MathF.Atan2(-f.X, -f.Z);
    }

    /// <summary>Whether a body centre is clear of solid (the test of "never inside the rock").</summary>
    public static bool BodyClear(ISolidSpace s, Vector3 p) => s.Density(p) < 0f;
}
