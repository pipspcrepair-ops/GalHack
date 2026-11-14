using System;
using Godot;

namespace Wildlife;

/// <summary>
/// How a winged species stays up, decided by its body (<see cref="Species.Flight"/>). There are two
/// kinds and no third:
/// - Gliders (everything medium and large) fly: always forward along their heading, never below a
///   minimum airspeed, turning by banking round a turn circle that grows with size and weight. They
///   flap to climb and to gain speed and glide to cruise or come down. They land on a long glide
///   with a flare and a run-out, take off with a run, and attack by swooping (<see cref="Swoop"/>).
/// - Hoverers (small and light) beat their wings too fast to see, never stopping while they are in
///   the air. Only they may stop, hold, back up and dart.
/// The brain still only writes <see cref="Creature.Desired"/> and <see cref="Creature.Altitude"/>;
/// to a glider those are where it would like to go, never a licence to stop.
/// </summary>
public static class Flight
{
    /// <summary>Rays only near someone: nobody sees a flyer clip a building 90 m off, and the world's collision ends about there.</summary>
    const float ProbeRange = 90f;
    /// <summary>The steepest a glider comes down on an ordinary approach, as a rise over a run.</summary>
    const float ApproachSlope = 0.27f;

    public static void Move(WildSim w, Creature c, float dt)
    {
        if (c.Species.Flight == FlightKind.Hover) Hoverer(w, c, dt);
        else Glider(w, c, dt);
    }

    /// <summary>How far out a glider at this height has to start its approach to reach the ground by then, m.</summary>
    public static float ApproachFrom(Creature c, float height) => MathF.Max(8f, height / ApproachSlope);

    /// <summary>
    /// Sets a flyer's height from what it is doing. Gliders come down to eat, graze and hide, and
    /// keep their swoop's height while hunting; hoverers stay low and hold at your face to sting.
    /// </summary>
    public static void PickAltitude(WildSim w, Creature c, ITarget hunted)
    {
        if (c.Species.Flight == FlightKind.Hover)
        {
            float ground = w.Terrain.Height(c.Position.X, c.Position.Z);
            c.Altitude = c.State switch
            {
                CState.Eat or CState.Graze => 0.9f,
                CState.Chase when hunted != null => MathF.Max(1.1f, hunted.Position.Y + 1.5f - ground),
                _ => 2.2f + (c.Id % 4) * 0.7f,
            };
            return;
        }
        switch (c.State)
        {
            case CState.Eat or CState.Graze or CState.Hidden or CState.Lurk:
                c.Altitude = 0f;
                break;
            case CState.Scavenge when WildSim.Flat(c.Goal - c.Position).Length() < ApproachFrom(c, c.Position.Y - w.Terrain.Height(c.Position.X, c.Position.Z)):
                c.Altitude = 0f;
                break;
            case CState.Chase:
                break; // the swoop keeps its own height
            default:
                c.Altitude = 9f + (c.Id % 4) * 2f;
                break;
        }
    }

    // ---- Gliders ----------------------------------------------------------------------------

    static void Glider(WildSim w, Creature c, float dt)
    {
        if (c.Landed && OnTheGround(w, c, dt)) return;
        Species s = c.Species;
        Terrain land = w.Terrain;
        float least = Heft.MinAirspeed(s), cruise = Heft.CruiseAirspeed(c), most = Heft.MostAirspeed(c), radius = Heft.TurnRadius(s);
        float stand = Heft.StandHeight(s);
        float speed = MathF.Max(WildSim.Flat(c.Velocity).Length(), least);
        bool stoop = c.AirAimOn;
        if (stoop) most = MathF.Max(most, Heft.SwoopSpeed(c));

        Probe(w, c, c.Forward * speed + Vector3.Up * c.Velocity.Y, dt);
        stoop = c.AirAimOn; // the probe may have called it off

        // Which way: never sideways, only by turning its heading, and only as fast as its bank lets it.
        Vector3 from = c.Position;
        Vector3 want = stoop ? WildSim.Flat(c.AirAim - from) : WildSim.Flat(c.Desired);
        float wantSpeed = stoop ? most : want.Length();
        if (!land.Inside(from + c.Forward * (radius * 2.5f + speed), 8f))
        {
            want = -WildSim.Flat(from); // the edge of the patch: back in, well before it
            wantSpeed = cruise;
        }
        float rate = speed / radius;
        float wantRate;
        if (c.AvoidFor > 0f)
        {
            // A wall coming: it stands on a wingtip and hauls round, harder than it ever turns in open air.
            c.AvoidFor -= dt;
            rate *= 1.7f;
            wantRate = c.AvoidSide * rate;
            wantSpeed = least;
        }
        else if (want.LengthSquared() < 0.16f) wantRate = c.Altitude <= 0.5f ? 0f : (c.Id % 2 == 0 ? 1f : -1f) * rate * 0.55f; // nothing to do: lazy circles, or a straight run in to land
        else wantRate = Math.Clamp(Geo.WrapAngle(MathF.Atan2(-want.X, -want.Z) - c.Yaw) * 2.5f, -rate, rate);
        c.TurnRate = Mathf.MoveToward(c.TurnRate, wantRate, rate * 2.4f * dt); // rolling into a bank takes a moment
        c.TurnRate = Math.Clamp(c.TurnRate, -rate, rate);
        c.Yaw = Geo.WrapAngle(c.Yaw + c.TurnRate * dt);
        c.Bank = MathF.Atan(c.TurnRate * speed / 9.8f);

        // How high, by what the ground does ahead of it.
        float ground = land.Height(from.X, from.Z), y = from.Y;
        Vector3 ahead = c.Forward * speed;
        float h1 = land.Height(from.X + ahead.X * 0.5f, from.Z + ahead.Z * 0.5f), h2 = land.Height(from.X + ahead.X * 1.2f, from.Z + ahead.Z * 1.2f);
        float risingTo = MathF.Max(ground, MathF.Max(h1, h2));
        bool landing = c.Altitude <= 0.5f && !stoop && c.AvoidFor <= 0f;
        // Rock, a tree or the sea under the approach, or a slope rising to meet it: it holds off and flies on till it isn't.
        if (landing && (w.Nav.Blocked(from + ahead * 1.5f) || risingTo - ground > speed * 0.45f)) landing = false;

        float target = landing ? least * 1.1f : Math.Clamp(wantSpeed < 0.4f ? cruise : wantSpeed, least, most);
        float height = y - ground;
        float floor = risingTo + stand + (landing ? 0.05f : stoop ? 0.9f : MathF.Min(2.5f, height * 0.5f));
        bool pulling = y < floor && !landing;
        bool flap;
        float vyWant;
        if (stoop)
        {
            // The dive: wings back, straight down the line to the aim point.
            float away = WildSim.Flat(c.AirAim - from).Length();
            vyWant = Math.Clamp((c.AirAim.Y - y) / MathF.Max(0.2f, away / speed), -speed * 0.85f, 2f);
            flap = false;
        }
        else if (landing)
        {
            float flare = 1.2f + s.Body.Size;
            float sink = MathF.Min(speed * ApproachSlope, MathF.Max(0.5f, (height - stand) * 0.7f));
            vyWant = -sink;
            flap = height - stand < flare; // beating the air to brake, feet out
        }
        else
        {
            float aim = risingTo * 0.4f + ground * 0.6f + MathF.Max(c.Altitude, 3f + s.Body.Size);
            float err = aim - y;
            // A beat or two to climb back, then a long glide down to where it started: a band, or it would flicker.
            c.Climbing = c.Climbing ? err > -0.4f || speed < target - 0.4f : err > 1.8f || speed < target - 1f;
            flap = c.Climbing;
            float climb = Heft.ClimbRate(s) * (c.Phase == AttackPhase.Recover ? 1.5f : 1f);
            float sink = Heft.SinkRate(s);
            // A climb carries on through the top of the band, or the beating would never end.
            vyWant = flap ? Math.Clamp((err + 0.8f) * 1.2f, 0.5f, climb) : err < -1f ? Math.Clamp(err * 0.5f, -3.5f, -sink) : -sink;
        }
        if (pulling)
        {
            flap = !stoop || y < floor - 1f;
            vyWant = MathF.Max(vyWant, MathF.Min(Heft.ClimbRate(s) * 2.2f, (floor - y) * 3f));
        }
        if (c.AvoidFor > 0f && c.AvoidClimb)
        {
            flap = true;
            vyWant = MathF.Max(vyWant, Heft.ClimbRate(s) * 2f);
        }

        // Going up is always work; a flap once started is finished.
        c.FlapFor = flap ? 0.5f : MathF.Max(0f, c.FlapFor - dt);
        c.Flapping = !stoop && c.FlapFor > 0f;

        speed = Mathf.MoveToward(speed, target, MathF.Min(6f, Heft.Accel(c) * 1.2f) * dt);
        speed = MathF.Max(speed, least);
        float vy = Mathf.MoveToward(c.Velocity.Y, vyWant, (pulling || c.AvoidClimb ? 22f : stoop ? 14f : 8f) * dt);
        if (vy > 0.3f)
        {
            c.Flapping = true;
            c.FlapFor = MathF.Max(c.FlapFor, 0.2f);
        }

        Vector3 flat = c.Forward * speed;
        Vector3 p = from + (flat + Vector3.Up * vy) * dt;
        p = Locomotion.Bound(w, p, ref flat);
        float g = land.Height(p.X, p.Z);
        if (p.Y < g + stand)
        {
            if (landing)
            {
                c.Landed = true;
                c.LandedAt = w.Time;
                c.Flapping = false;
                c.FlapFor = 0f;
                c.TurnRate = 0f;
                c.Bank = 0f;
                c.Position = new Vector3(p.X, g, p.Z);
                c.Velocity = flat;
                return;
            }
            p.Y = g + stand;
            vy = MathF.Max(vy, 0f);
        }
        c.Velocity = flat + Vector3.Up * vy;
        c.Position = p;
    }

    /// <summary>
    /// On the ground: runs itself up to flying speed when it wants to be off, otherwise stands, walks
    /// a little, and lets a landing's run-out die away. Returns true while it stays down.
    /// </summary>
    static bool OnTheGround(WildSim w, Creature c, float dt)
    {
        Species s = c.Species;
        if (c.Altitude <= 0.5f)
        {
            c.TakingOff = false;
            c.Flapping = false;
            c.Bank = 0f;
            Vector3 vg = WildSim.Flat(c.Velocity).MoveToward(WildSim.Flat(c.Desired) * 0.3f, 4f * dt);
            Vector3 pg = Locomotion.Bound(w, c.Position + vg * dt, ref vg);
            pg.Y = w.Terrain.Height(pg.X, pg.Z);
            c.Velocity = vg;
            c.Position = pg;
            Locomotion.Turn(c, vg, dt);
            if (c.FacePoint is { } face) Locomotion.Turn(c, WildSim.Flat(face - c.Position), dt);
            return true;
        }

        // The take-off run: wings going, down the way it wants to go, until the air will carry it.
        c.TakingOff = true;
        c.Flapping = true;
        c.FlapFor = 0.6f;
        float least = Heft.MinAirspeed(s);
        float speed = Mathf.MoveToward(WildSim.Flat(c.Velocity).Length(), least * 1.05f, Math.Clamp(Heft.Accel(c) * 0.4f, 2.5f, 6f) * dt);
        Vector3 want = WildSim.Flat(c.Desired);
        if (want.LengthSquared() > 0.04f) Locomotion.Turn(c, want, dt);
        Vector3 v = c.Forward * speed;
        Vector3 p = c.Position + v * dt;
        Locomotion.Avoid(w, c, ref p, ref v);
        p = Locomotion.Bound(w, p, ref v);
        p.Y = w.Terrain.Height(p.X, p.Z);
        c.Velocity = v;
        c.Position = p;
        if (speed < least) return true;
        c.Landed = false;
        c.TakingOff = false;
        c.Position = new Vector3(p.X, p.Y + Heft.StandHeight(s), p.Z);
        c.Velocity = c.Forward * speed + Vector3.Up * Heft.ClimbRate(s);
        return true;
    }

    // ---- Hoverers ---------------------------------------------------------------------------

    static void Hoverer(WildSim w, Creature c, float dt)
    {
        Species s = c.Species;
        float stand = Heft.StandHeight(s);
        if (c.Landed)
        {
            if (c.Altitude <= 0.5f)
            {
                c.Flapping = false;
                c.Velocity = Vector3.Zero;
                c.Position = new Vector3(c.Position.X, w.Terrain.Height(c.Position.X, c.Position.Z), c.Position.Z);
                if (c.FacePoint is { } face) Locomotion.Turn(c, WildSim.Flat(face - c.Position), dt);
                return;
            }
            c.Landed = false;
            c.Position += Vector3.Up * stand;
            c.Velocity = Vector3.Up * 1.5f;
        }
        c.Flapping = true;

        Vector3 flat = WildSim.Flat(c.Velocity).MoveToward(WildSim.Flat(c.Desired), Heft.Accel(c) * 0.8f * dt);
        Probe(w, c, flat, dt);
        if (c.AvoidFor > 0f)
        {
            // Something ahead: it stops short and backs off, as nothing else in the air can.
            c.AvoidFor -= dt;
            float into = flat.Dot(c.AvoidFrom);
            if (into > 0f) flat -= c.AvoidFrom * (into + 1.5f);
        }

        float ground = w.Terrain.Height(c.Position.X, c.Position.Z);
        float bob = MathF.Sin(c.Age * 2.3f + c.Id) * 0.05f;
        float vy = c.Altitude <= 0.5f ? -1.4f : Math.Clamp((ground + c.Altitude + bob - c.Position.Y) * 3f, -3.5f, 3.5f);
        Vector3 p = c.Position + (flat + Vector3.Up * vy) * dt;
        p = Locomotion.Bound(w, p, ref flat);
        float g = w.Terrain.Height(p.X, p.Z);
        if (p.Y < g + stand)
        {
            if (c.Altitude <= 0.5f)
            {
                c.Landed = true;
                c.LandedAt = w.Time;
                c.Flapping = false;
                c.Position = new Vector3(p.X, g, p.Z);
                c.Velocity = Vector3.Zero;
                return;
            }
            p.Y = g + stand;
        }
        c.Velocity = flat + Vector3.Up * vy;
        c.Position = p;
        // Facing what it means to face, whichever way it is drifting: that is what lets it back up.
        Locomotion.Turn(c, c.FacePoint is { } look ? WildSim.Flat(look - p) : flat.Length() > 0.6f ? flat : Vector3.Zero, dt * 2f);
    }

    // ---- Keeping out of things --------------------------------------------------------------

    /// <summary>
    /// Now and then looks along its way for walls, trees and hulls (a dive looks often). Something
    /// ahead: a glider banks for the open side, climbing if neither side is, and breaks off any dive.
    /// </summary>
    static void Probe(WildSim w, Creature c, Vector3 velocity, float dt)
    {
        c.ProbeIn -= dt;
        if (c.ProbeIn > 0f) return;
        bool diving = c.Phase == AttackPhase.Lunge;
        c.ProbeIn = diving ? 0.08f : c.Attacking || c.AvoidFor > 0f ? 0.12f : 0.25f + (c.Id % 5) * 0.01f;
        Vector3 from = c.Position;
        // The ground is the height's to deal with, not this: it looks level, lifting over the land ahead, and
        // a dive looks down its own line as far as the aim point and no further (it goes on into the ground).
        Vector3 way = WildSim.Flat(velocity);
        float speed = velocity.Length();
        if (speed < 0.3f || way.LengthSquared() < 0.01f || !NearSomeone(w, c)) return;
        if (c.Altitude <= 0.5f && from.Y - w.Terrain.Height(from.X, from.Z) < 4f) return; // coming in to land: the ground is the point
        Vector3 dir = way.Normalized();
        float look = speed * (diving ? 0.9f : 1.3f) + (c.Species.Flight == FlightKind.Glide ? Heft.TurnRadius(c.Species) * 1.1f : 0.8f);
        bool ahead;
        if (diving)
        {
            Vector3 to = c.AirAim - from;
            ahead = Clear(w, c, from, from + to.LimitLength(look));
        }
        else ahead = Clear(w, c, from, Level(w, from, dir * look));
        // A dive only minds what is on its line; anything else also looks to either side, or it would shave a corner.
        bool leftNear = diving || Clear(w, c, from, Level(w, from, dir.Rotated(Vector3.Up, 0.5f) * look * 0.75f));
        bool rightNear = diving || Clear(w, c, from, Level(w, from, dir.Rotated(Vector3.Up, -0.5f) * look * 0.75f));
        if (ahead && leftNear && rightNear) return;
        if (diving) Swoop.BreakOff(w, c);
        c.AvoidFrom = WildSim.Flat(dir);
        if (leftNear != rightNear)
        {
            c.AvoidSide = leftNear ? 1f : -1f;
            c.AvoidClimb = false;
        }
        else
        {
            bool left = Clear(w, c, from, Level(w, from, dir.Rotated(Vector3.Up, 1.1f) * look));
            bool right = Clear(w, c, from, Level(w, from, dir.Rotated(Vector3.Up, -1.1f) * look));
            c.AvoidClimb = !left && !right;
            // Having picked a way round, it keeps to it rather than dithering at the wall.
            c.AvoidSide = left && !right ? 1f : right && !left ? -1f : c.AvoidFor > 0f ? c.AvoidSide : c.Id % 2 == 0 ? 1f : -1f;
        }
        c.AvoidFor = 0.9f;
    }

    /// <summary>A point out along the level, lifted clear of the ground if the land rises to it.</summary>
    static Vector3 Level(WildSim w, Vector3 from, Vector3 out_)
    {
        Vector3 to = from + out_;
        to.Y = MathF.Max(to.Y, w.Terrain.Height(to.X, to.Z) + 1.5f);
        return to;
    }

    static bool NearSomeone(WildSim w, Creature c)
    {
        foreach (ITarget t in w.Targets)
            if (WildSim.Flat(t.Position - c.Position).LengthSquared() < ProbeRange * ProbeRange) return true;
        return false;
    }

    /// <summary>Whether the air between two points is free: by the world's own collision, or low down by the nav grid's rocks and trunks where a sim has no collision.</summary>
    public static bool Clear(WildSim w, Creature c, Vector3 a, Vector3 b)
    {
        if (w.SightLine != null && !c.NoRays) return w.SightLine(a, b);
        return a.Y - w.Terrain.Height(a.X, a.Z) > 5f || w.Nav.LineClear(a, b);
    }
}

/// <summary>
/// A glider's attack: never from a hold. It circles or comes in at height; lines up on you and gives
/// its tell in flight (a cry, the climb of a wing-over, wings thrown up); commits to where you
/// stand, as every attack does; stoops down the line with its wings back; strikes in passing, over
/// your head, never through you; climbs away, comes round and goes again. Every number grows with
/// the body, so a big one comes slower, from further out, and swings wider between passes. The
/// wind-up, commit and strike are <see cref="Attacks"/>' own, so the events and rules are the same
/// as on the ground: the only thing different is that it never stops flying.
/// </summary>
public static class Swoop
{
    /// <summary>How long its tell runs at the least, s: a big one takes longer to get round to it.</summary>
    public static float LeastTell(Species s) => 1f + 0.3f * s.Body.Size;

    /// <summary>How high above you it comes in, m.</summary>
    public static float Height(Species s) => 7f + 1.6f * s.Body.Size;

    /// <summary>How far above your feet its middle passes: clear of your head by more than its own body and folded wings.</summary>
    public static float PassHeight(Species s) => Attacks.BodyHeight + MathF.Max(s.Body.Width, s.Body.Height) * 0.7f + s.Body.Size * 0.45f + 0.2f;

    /// <summary>How far out it starts its run, m: the dive's length, plus what it flies through the tell.</summary>
    public static float RunIn(Creature c) => (Height(c.Species) - PassHeight(c.Species)) * 2.25f + Heft.CruiseAirspeed(c) * LeastTell(c.Species) * 0.8f;

    /// <summary>The seconds between one pass and being ready to line up for the next.</summary>
    static float PassGap(Species s) => 2f + 1.5f * s.Body.Size;

    /// <summary>The hunting part of it, thought now and then: gets in position, and starts the attack when lined up.</summary>
    public static void Hunt(WildSim w, Creature c, ITarget t)
    {
        c.FacePoint = t.Position;
        Species s = c.Species;
        Vector3 to = WildSim.Flat(t.Position - c.Position);
        float d = to.Length(), near = RunIn(c), far = near + 10f;
        Vector3 toward = d > 0.01f ? to / d : c.Forward;
        float ground = w.Terrain.Height(c.Position.X, c.Position.Z);
        c.Altitude = MathF.Max(5f, t.Position.Y + Height(s) - ground);
        float up = c.Position.Y - t.Position.Y;
        if (c.Landed) { c.Desired = c.Forward * Heft.CruiseAirspeed(c); return; }

        bool ready = w.Time >= c.NextPass && d > near * 0.75f && d < far && c.Forward.Dot(toward) > 0.75f && up > Height(s) * 0.7f;
        if (ready)
        {
            Vector3 pass = new(t.Position.X, t.Position.Y + PassHeight(s), t.Position.Z);
            if (RunClear(w, c, c.Position, pass, toward) && Attacks.Start(w, c, t)) return;
        }
        // Too close or too soon: it flies on out, round to the side, and turns back when it has the room.
        if (d < near || w.Time < c.NextPass) c.Desired = (-toward + toward.Cross(Vector3.Up) * (c.Id % 2 == 0 ? 0.5f : -0.5f)).Normalized() * Heft.CruiseAirspeed(c);
        else c.Desired = toward * (d > far ? Heft.MostAirspeed(c) : Heft.CruiseAirspeed(c));
    }

    /// <summary>The attack in the air, in place of <see cref="Attacks"/>' standing wind-up, lunge and recover.</summary>
    public static void Update(WildSim w, Creature c, ITarget t, float dt)
    {
        Species s = c.Species;
        switch (c.Phase)
        {
            case AttackPhase.Windup:
                if (!t.Attackable && !c.Committed)
                {
                    Attacks.Cancel(c);
                    c.NextPass = w.Time + 1f;
                    return;
                }
                if (!c.Committed) c.AimPoint = t.Position;
                c.FacePoint = c.AimPoint;
                Vector3 to = WildSim.Flat(c.AimPoint - c.Position);
                c.Desired = (to.LengthSquared() > 0.01f ? to.Normalized() : c.Forward) * Heft.CruiseAirspeed(c);
                c.Rear = Math.Clamp(c.PhaseTime / c.PhaseLength, 0f, 1f);
                c.Altitude = MathF.Max(5f, c.AimPoint.Y + Height(s) + 1f - w.Terrain.Height(c.Position.X, c.Position.Z));
                Attacks.CommitIfDue(w, c, t);
                if (c.PhaseTime >= c.PhaseLength) Stoop(w, c);
                break;

            case AttackPhase.Lunge:
                c.Lunge = 1f;
                Vector3 left = WildSim.Flat(c.AirAim - c.Position);
                if (left.Dot(c.Forward) <= 0.2f || c.PhaseTime >= c.PhaseLength) Strike(w, c, t);
                break;

            case AttackPhase.Recover:
                c.Lunge = Mathf.MoveToward(c.Lunge, 0f, dt * 3f);
                c.Rear = 0f;
                c.Desired = c.Forward * Heft.MostAirspeed(c);
                c.Altitude = MathF.Max(5f, (t?.Position.Y ?? c.Position.Y) + Height(s) - w.Terrain.Height(c.Position.X, c.Position.Z));
                if (c.PhaseTime >= c.PhaseLength)
                {
                    c.Phase = AttackPhase.None;
                    c.ThinkEvery = 0f;
                }
                break;
        }
    }

    /// <summary>The tell is over: down the line, if the line is free.</summary>
    static void Stoop(WildSim w, Creature c)
    {
        Species s = c.Species;
        Vector3 pass = new(c.AimPoint.X, c.AimPoint.Y + PassHeight(s), c.AimPoint.Z);
        Vector3 to = WildSim.Flat(pass - c.Position);
        float d = to.Length();
        if (!RunClear(w, c, c.Position, pass, d > 0.01f ? to / d : c.Forward))
        {
            Attacks.Cancel(c);
            c.NextPass = w.Time + 1.5f;
            return;
        }
        c.AirAim = pass;
        c.AirAimOn = true;
        c.Phase = AttackPhase.Lunge;
        c.PhaseTime = 0f;
        c.PhaseLength = d / Heft.SwoopSpeed(c) + 1.2f;
        c.Rear = 0f;
    }

    /// <summary>It goes by: judged where it is, against wherever you are now.</summary>
    static void Strike(WildSim w, Creature c, ITarget t)
    {
        CombatRules r = w.Combat;
        Vector3 at = t.Position;
        float above = c.Position.Y - (at.Y + Attacks.BodyHeight);
        bool hit = t.Attackable && WildSim.Flat(at - c.Position).Length() <= r.ContactRadius + c.Radius * 0.4f && above < 1.6f + c.Species.Body.Size * 0.3f
            && (c.Space == null || SpaceMath.Clear(c.Space, c.Position, at + Vector3.Up * (Attacks.BodyHeight * 0.5f))); // never through a wall, floor or ceiling
        c.StrikePoint = c.Position;
        Attacks.Deliver(w, c, t, hit);
        Away(w, c);
    }

    /// <summary>Something in the way of the dive: it pulls out, and no blow is struck.</summary>
    public static void BreakOff(WildSim w, Creature c)
    {
        if (c.AttackTarget != null) Attacks.Deliver(w, c, c.AttackTarget, false);
        Away(w, c);
    }

    static void Away(WildSim w, Creature c)
    {
        c.AirAimOn = false;
        c.Phase = AttackPhase.Recover;
        c.PhaseTime = 0f;
        c.PhaseLength = 1.2f + 0.5f * c.Species.Body.Size;
        c.NextPass = w.Time + PassGap(c.Species);
    }

    /// <summary>Whether a dive down this line, and the climb out past its end, stays clear of the ground and everything on it.</summary>
    static bool RunClear(WildSim w, Creature c, Vector3 from, Vector3 pass, Vector3 along)
    {
        float stand = Heft.StandHeight(c.Species);
        for (int i = 1; i <= 5; i++)
        {
            Vector3 q = from.Lerp(pass, i / 5f);
            if (q.Y < w.Terrain.Height(q.X, q.Z) + stand + 0.4f) return false;
        }
        Vector3 past = pass + along * (Heft.TurnRadius(c.Species) + 4f) + Vector3.Up * 2f;
        if (past.Y < w.Terrain.Height(past.X, past.Z) + stand + 0.4f) return false;
        return w.SightLine == null || c.NoRays || (w.SightLine(from, pass) && w.SightLine(pass, past));
    }
}
