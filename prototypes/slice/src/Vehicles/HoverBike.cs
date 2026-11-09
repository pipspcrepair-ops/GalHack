using System;
using System.Collections.Generic;
using GalHack.Physics;
using GalHack.Terrain;
using Godot;
using PlanetSurface.Terrain;
using Slice.Surface;
using TerrainDigging.Terrain;
using N = System.Numerics;

namespace Slice.Vehicles;

/// <summary>
/// The hover bike: a small one-seat hover vehicle made in the suit fabricator from basic ore.
/// It is its own simple model, apart from the vessel code: no blocks, no cabin, no core. It rides a
/// little above whatever is under it (the drawn ground, a base's hull, or the water's surface, which
/// it treats as ground and skims across), climbs slopes on its cushion and stops at a wall, a cliff
/// or a trunk. Empyrion's hover bike is the reference for the feel (quick, light, a short hop on
/// Space, a boost) and for its role (cheap fast ground transport from early on).
/// </summary>
public sealed class HoverBike
{
    /// <summary>How high the underside rides above the surface, m.</summary>
    public const float Hover = 0.9f;
    public const float MaxSpeed = 26f, BoostSpeed = 38f, ReverseSpeed = 7f, Accel = 20f, Brake = 34f;
    /// <summary>How hard an empty bike stops, m/s²: a rider who steps off a moving bike finds it a step away, not gliding off down the hill.</summary>
    public const float ParkedBrake = 100f;
    public const float TurnRate = 1.7f; // rad/s at full lock
    /// <summary>How fast a ridden bike turns toward where its rider looks, rad/s at speed (slower at a standstill, as the keys).</summary>
    public const float AimRate = 3f;
    /// <summary>The seated eye, above the underside and a little back, m.</summary>
    public static readonly Vector3 EyeAt = new(0f, 1.3f, 0.15f);

    /// <summary>What it weighs, kg, and its drag area, m² (a rider adds theirs): the wind and a tornado take it by these.</summary>
    public const float Weight = 220f, DragArea = 1.0f;

    /// <summary>Left parked, it settles on its skids: it holds against a push up to this, m/s² (a gale), and slides past it.</summary>
    public const float ParkedHold = 3f;

    /// <summary>The weather's push this tick, m/s² (wind, a tornado's pull), and a fling's kick, m/s.</summary>
    Vector3 _push, _kick;

    /// <summary>The weather's force module it is registered with, if any (<see cref="HoverBikes.Tick"/>).</summary>
    internal Weather.Forces.ForceModule Weather;
    internal BikeBody Pushed;

    public readonly Node3D Node;

    /// <summary>The underside's centre in the zone's world.</summary>
    public Vector3 Position;
    public float Yaw;

    /// <summary>Metres per second over the ground, and the climb.</summary>
    public Vector3 Velocity;
    float _vy;

    /// <summary>The controls this tick (set by the game for a ridden bike; a parked bike gets none).</summary>
    public float Throttle, Steer;
    public bool Boost, Hop;

    /// <summary>Where the rider looks (the view's yaw), or NaN while they look around: a ridden bike turns toward it, so the mouse steers.</summary>
    public float Aim = float.NaN;

    /// <summary>The turn the last tick made, so the rider's view turns with the bike.</summary>
    public float LastTurn { get; private set; }

    public bool Grounded { get; private set; } = true;

    /// <summary>Its hull box met something this tick (a bank it scraped, a wall it slid along) and was pushed clear of it.</summary>
    public bool Touching { get; private set; }
    public bool OnWater { get; private set; }
    public bool Ridden;

    float _pitch, _roll, _spray;
    readonly HoverBikes _owner;

    public float Speed => new Vector2(Velocity.X, Velocity.Z).Length();
    public Vector3 Forward => new(-MathF.Sin(Yaw), 0f, -MathF.Cos(Yaw));
    public Vector3 Right => new(MathF.Cos(Yaw), 0f, -MathF.Sin(Yaw));

    /// <summary>The middle of the rider's seat, in the world.</summary>
    public Vector3 Seat => Position + Vector3.Up * 0.55f;

    internal HoverBike(HoverBikes owner, Vector3 at, float yaw)
    {
        _owner = owner;
        Position = at + Vector3.Up * Hover;
        Yaw = yaw;
        Node = BikeModel.Build();
        Node.Name = "HoverBike";
        owner.AddChild(Node);
        Place();
        Node.ResetPhysicsInterpolation();
    }

    void Place() => Node.GlobalTransform = new Transform3D(Attitude, Position);

    /// <summary>How it sits in the world: the weather's tumble, its heading, and its lean to the slope and into a turn.</summary>
    public Basis Attitude => _tumble * new Basis(Vector3.Up, Yaw) * new Basis(Vector3.Right, _pitch) * new Basis(Vector3.Forward, _roll);

    /// <summary>A hop's kick off the ground, m/s.</summary>
    public const float HopSpeed = 7f;

    /// <summary>
    /// The least height of any corner of its hull above what is solid under that corner (the ground, a base's
    /// hull, a place's blocks; not water), m: below zero, that far into it.
    /// </summary>
    public float Clearance()
    {
        Basis b = Attitude;
        float least = float.PositiveInfinity;
        for (int i = 0; i < 8; i++)
        {
            var c = new Vector3((i & 1) == 0 ? HullMin.X : HullMax.X, (i & 2) == 0 ? HullMin.Y : HullMax.Y, (i & 4) == 0 ? HullMin.Z : HullMax.Z);
            Vector3 p = Position + b * c;
            least = MathF.Min(least, p.Y - _owner.SolidAt(p.X, p.Z, p.Y + 0.8f));
        }
        return least;
    }

    /// <summary>How far its lowest corner is above its underside's centre as it sits now, m (below zero: under it).</summary>
    float Lowest()
    {
        Basis b = Attitude;
        float low = float.PositiveInfinity;
        for (int i = 0; i < 8; i++)
            low = MathF.Min(low, (b * new Vector3((i & 1) == 0 ? HullMin.X : HullMax.X, (i & 2) == 0 ? HullMin.Y : HullMax.Y, (i & 4) == 0 ? HullMin.Z : HullMax.Z)).Y);
        return low;
    }

    /// <summary>
    /// How the weather has turned it, on top of its heading and lean: a toss tips, rolls and flips it.
    /// Its plates right it only within their reach of the ground, and only so hard (<see cref="PlateLevel"/>).
    /// </summary>
    Basis _tumble = Basis.Identity;
    Vector3 _spin;

    /// <summary>The most angular acceleration its hover plates put against a tumble near the ground, rad/s².</summary>
    public const float PlateLevel = 8f;

    /// <summary>Came down on its side or its roof: no cushion and no drive while it lies there, and its plates roll it back onto them (it always tries to sit flat), then it rides again.</summary>
    public bool Toppled { get; private set; }

    /// <summary>How far it is tipped from upright, radians.</summary>
    public float Tilt => _tumble.Y.AngleTo(Vector3.Up);

    /// <summary>
    /// The push it takes to grip it, m/s²: past this (a tornado's pull) the weather turns it over. Short of it
    /// (a gale, or the drag of its own speed through still air, ~5 m/s² at full boost) its gyro and plates hold
    /// it near level: a hop at speed once flipped it over backwards, the drag of its own speed tipping it
    /// harder than its gyro could hold in the air.
    /// </summary>
    public const float GripPush = 15f;

    /// <summary>The hull's box about the underside's centre, m (pads to bars, nose to tail): none of it ever goes into the ground.</summary>
    public static readonly Vector3 HullMin = new(-0.7f, -0.05f, -1.3f), HullMax = new(0.7f, 1.0f, 1.2f);

    /// <summary>How far above its underside the ground under it is looked for (a roof higher than this is something it rides under).</summary>
    const float LookFrom = 1.5f;

    /// <summary>The rider sets it back on its plates.</summary>
    public void SetUpright()
    {
        Toppled = false;
        _tumble = Basis.Identity;
        _spin = Vector3.Zero;
        Place();
    }

    /// <summary>Laid as it would lie after an upset (on its side, its roof): the tilt, at rest. Past its plates' reach it lies toppled.</summary>
    public void Lay(Basis tilt)
    {
        _tumble = tilt.Orthonormalized();
        _spin = Vector3.Zero;
        _vy = 0f;
        Toppled = Tilt > 1.05f;
        Grounded = !Toppled;
        Place();
    }

    /// <summary>A throw (a tornado's fling, a crash): a change of velocity, m/s, and of spin, rad/s.</summary>
    public void Throw(Vector3 velocity, Vector3 spin)
    {
        _kick += velocity;
        _spin += spin;
    }

    /// <summary>
    /// The weather's turn on it: its push acts above its middle (stronger aloft, uneven), so a hard push
    /// tips it, and a vortex turns it round with it; its plates right it within their reach of the
    /// ground, up to their limit. A light bike in a tornado's grip tumbles.
    /// </summary>
    void Turn(Vector3 push, float above, bool gripped, float dt)
    {
        var flat = new Vector3(push.X, 0f, push.Z);
        Vector3 tip = Vector3.Up.Cross(flat) * 1.1f; // m/s² of push over its own small size
        // It always tries to be level in the world: its plates hard near the ground (or lying on it), its gyro more
        // gently in the air, so a tornado's grip still overpowers it and tumbles it, but a hop, a drop or a gale
        // never leaves it tilted: short of a grip the push leans it a few degrees at most.
        float most = above < Hover * 2.5f || Toppled ? PlateLevel : PlateLevel * 0.4f;
        // Only a vortex's grip turns it round about up with it. Any push used to (the drag of its own speed through
        // still air too), and that spin, folded into its heading below, swung its nose round on every ride.
        if (gripped) tip += Vector3.Up * flat.Length() * 0.25f;
        else tip = Vector3.Zero; // short of a grip the push only leans it a little in its own frame (Tick), never about a slanting axis that would swing its nose
        Vector3 axis = _tumble.Y.Cross(Vector3.Up);
        float tilt = Tilt;
        if (axis.LengthSquared() < 1e-6f && tilt > 1.5f) axis = Forward; // flat on its roof: it rolls back over its side
        Vector3 level = (axis.LengthSquared() > 1e-8f ? axis.Normalized() * (20f * tilt) : Vector3.Zero) - _spin * 4f;
        Vector3 alpha = tip + level.LimitLength(most);
        _spin = ((_spin + alpha * dt) * MathF.Exp(-0.3f * dt)).LimitLength(7f);
        float turn = _spin.Length() * dt;
        if (turn > 1e-6f) _tumble = new Basis(_spin.Normalized(), turn) * _tumble;
        _tumble = _tumble.Orthonormalized();
        // What the weather turned it about the up axis turns the bike itself, its heading, so the model never
        // drifts round from the way it drives: the tumble keeps only the tilt. (Upside down there is no telling.)
        Vector3 up = _tumble.Y.Normalized();
        if (up.Dot(Vector3.Up) > -0.99f)
        {
            var tipped = new Quaternion(Vector3.Up, up);
            Quaternion yawOnly = tipped.Inverse() * _tumble.GetRotationQuaternion();
            Yaw += 2f * MathF.Atan2(yawOnly.Y, yawOnly.W);
            _tumble = new Basis(tipped);
        }
    }

    /// <summary>One physics tick.</summary>
    public void Tick(float dt)
    {
        LastTurn = 0f;
        float throttle = Ridden ? Throttle : 0f, steer = Ridden ? Steer : 0f;
        bool boost = Ridden && Boost, hop = Ridden && Hop;

        // Heading: a turn takes more lock the faster it goes (a hover bike barely turns at a standstill).
        // It comes round toward where the rider looks first; the keys' turn after it also turns the view, so it keeps the gap.
        float grip = Mathf.Clamp(Speed / 7f, 0.4f, 1f);
        if (Ridden && !float.IsNaN(Aim))
        {
            float gap = Mathf.Wrap(Aim - Yaw, -MathF.PI, MathF.PI), most = AimRate * grip * dt;
            Yaw += Math.Clamp(gap, -most, most);
        }
        float turn = -steer * TurnRate * grip * dt;
        Yaw += turn;
        LastTurn = turn;

        // The weather's push first: drag by its area over its weight, a tornado's pull and fling. Parked on
        // its skids it holds against a gale; past that it slides, braking hard on them.
        var flatPush = new Vector3(_push.X, 0f, _push.Z);
        bool parked = !Ridden && Grounded;
        if (parked && flatPush.Length() <= ParkedHold) flatPush = Vector3.Zero;
        Velocity += flatPush * dt + new Vector3(_kick.X, 0f, _kick.Z);
        if (parked) Velocity = new Vector3(Velocity.X, 0f, Velocity.Z) * MathF.Exp(-6f * dt) + Vector3.Up * Velocity.Y;
        _vy += _push.Y * dt + _kick.Y;
        bool tossed = _push.Y > 12f || _kick.Y > 0.5f;
        Vector3 pushed = _push;
        _push = _kick = Vector3.Zero;
        float above = Position.Y - _owner.SurfaceAt(Position.X, Position.Z, Position.Y + LookFrom, out _);
        Turn(pushed, above, tossed || pushed.Length() > GripPush, dt);
        if (Toppled)
        {
            // On its side or its roof: no cushion, no drive. It lies on the ground, sliding to a stop, while its
            // plates roll it back onto them (Turn); upright again, it rides at once, its rider still in the seat.
            // (A toppled bike that only a dismount and remount set upright left its rider unable to drive it.)
            Velocity = new Vector3(Velocity.X, 0f, Velocity.Z) * MathF.Exp(-4f * dt);
            Position += Velocity * dt;
            _vy = 0f;
            Position.Y -= Clearance(); // its lowest corner on the ground: never in it
            float sea = _owner.SurfaceAt(Position.X, Position.Z, Position.Y + LookFrom, out bool afloat);
            if (afloat) Position.Y = MathF.Max(Position.Y, sea + 0.1f - Lowest()); // on water it floats where it lies
            if (Tilt < 0.35f)
            {
                Toppled = false;
                Grounded = true;
            }
            Place();
            return;
        }

        Vector3 fwd = Forward, right = Right;
        Vector3 flat = new(Velocity.X, 0f, Velocity.Z);
        float vf = flat.Dot(fwd), vs = flat.Dot(right);
        float max = boost ? BoostSpeed : MaxSpeed;
        if (throttle > 0f) vf = vf < max ? MathF.Min(max, vf + Accel * (boost ? 1.6f : 1f) * throttle * dt) : MathF.Max(max, vf - 12f * dt);
        else if (throttle < 0f) vf = MathF.Max(-ReverseSpeed, vf - (vf > 0f ? Brake : 10f) * -throttle * dt);
        // Only on its cushion do its pads drag: in the air it flies as thrown.
        else if (Grounded && !Ridden) vf = MathF.Abs(vf) <= ParkedBrake * dt ? 0f : vf - MathF.Sign(vf) * ParkedBrake * dt; // nobody on it: the pads drop and drag it to a stop
        else if (Grounded) vf *= MathF.Exp(-0.55f * dt);
        if (Grounded) vs *= MathF.Exp(-(Ridden ? 3.2f : 12f) * dt); // it drifts a little through a turn, then grips
        Vector3 move = fwd * vf + right * vs;

        // Ahead: a wall, a cliff or a trunk stops it (it slides along nothing: it just loses the speed).
        float step = move.Length() * dt;
        if (step > 1e-4f)
        {
            Vector3 dir = move / (step / dt);
            Vector3 probe = Position + dir * (step + 0.9f);
            float ahead = _owner.SurfaceAt(probe.X, probe.Z, Position.Y + LookFrom, out _);
            bool cliff = ahead > Position.Y - Hover + 0.8f && ahead > _owner.SurfaceAt(Position.X, Position.Z, Position.Y + LookFrom, out _) + 0.8f;
            // Rays from its middle out past its nose (or tail, or flank) the way it moves, low and high, middle and both
            // sides: one ray from the middle to a metre out let its nose and corners into a wall before it saw it.
            Vector3 side = Vector3.Up.Cross(dir).Normalized();
            float reach = MathF.Abs(dir.Dot(fwd)) * (dir.Dot(fwd) > 0f ? -HullMin.Z : HullMax.Z) + MathF.Abs(dir.Dot(right)) * HullMax.X;
            bool wall = false;
            foreach (float h in new[] { 0.3f, 0.8f })
                foreach (float s in new[] { -0.6f, 0f, 0.6f })
                    wall = wall || _owner.Blocked(Position + Vector3.Up * h + side * s, dir, reach + step + 0.15f);
            if (cliff || wall)
            {
                if (Speed > 6f && Ridden) _owner.Bumped?.Invoke(Position + dir);
                vf = -vf * 0.2f;
                vs = 0f;
                move = fwd * vf;
            }
        }
        Velocity = new Vector3(move.X, 0f, move.Z);
        Position += Velocity * dt;

        // Up and down: a spring to the cushion over the surface, free fall when well above it.
        float surface = _owner.SurfaceAt(Position.X, Position.Z, Position.Y + LookFrom, out bool water);
        OnWater = water;
        float target = surface + Hover;
        bool wasAirborne = !Grounded;
        if (Position.Y > target + 0.35f || tossed) { _vy -= 12f * dt; Grounded = false; }
        else
        {
            // Back down: tipped past its plates' reach, it lands on its side or roof and stays there.
            if (wasAirborne && Tilt > 1.05f) Toppled = true;
            _vy += (70f * (target - Position.Y) - 16f * _vy) * dt;
            Grounded = true;
        }
        // A hop: its plates kick straight off the ground along the seat's own up (away from the seat's down), not a flip.
        if (hop && Grounded)
        {
            Vector3 up = Attitude.Y;
            _vy = HopSpeed * up.Y;
            Velocity += new Vector3(up.X, 0f, up.Z) * HopSpeed;
            Grounded = false;
        }
        _vy = Mathf.Clamp(_vy, -40f, 22f);
        Position.Y += _vy * dt;
        if (Position.Y < surface + 0.35f) { Position.Y = surface + 0.35f; _vy = MathF.Max(0f, _vy); }

        // The body leans to the slope under it and into a turn; in the air (a hop, off a ledge) it holds itself
        // level, as a hover craft tries to, never nosing up or down with its climb.
        float h0 = _owner.SurfaceAt(Position.X + fwd.X * 1.0f, Position.Z + fwd.Z * 1.0f, Position.Y + LookFrom, out _);
        float h1 = _owner.SurfaceAt(Position.X - fwd.X * 1.0f, Position.Z - fwd.Z * 1.0f, Position.Y + LookFrom, out _);
        float k = 1f - MathF.Exp(-8f * dt);
        // A push short of a grip (a gale, the drag of its own speed) leans it a few degrees, nose up into a headwind, over
        // to leeward in a crosswind, about its own axes: a tumble about a slanting axis swung its nose off its heading.
        Vector3 lean = new Vector3(pushed.X, 0f, pushed.Z);
        float windPitch = Mathf.Clamp(-lean.Dot(fwd) * 0.012f, -0.06f, 0.06f), windRoll = Mathf.Clamp(lean.Dot(right) * 0.012f, -0.06f, 0.06f);
        _pitch = Mathf.Lerp(_pitch, (Grounded ? Mathf.Clamp(MathF.Atan2(h0 - h1, 2f), -0.5f, 0.5f) : 0f) + windPitch, k);
        _roll = Mathf.Lerp(_roll, Mathf.Clamp(-steer * Speed * 0.012f - vs * 0.02f, -0.45f, 0.45f) + windRoll, k);

        // Nothing of it goes into the ground (or a base's hull, or a place's blocks): whatever corner a crash, a
        // drop, a tilt or the ground dug from under it put in, it is pushed straight back out.
        float sunk = -Clearance();
        if (sunk > 0f)
        {
            Position.Y += sunk;
            _vy = MathF.Max(0f, _vy);
        }
        // And out of anything its hull box still overlaps (a wall it slid into side on, a block it was set down in), along the contact.
        Touching = false;
        for (int pass = 0; pass < 8; pass++)
        {
            Vector3 clear = _owner.PushOut(Attitude, Position);
            if (clear == Vector3.Zero) break;
            Touching = true;
            Position += clear;
            Vector3 n = clear.Normalized();
            float into = Velocity.Dot(n);
            if (into < 0f) Velocity -= n * into;
            if (n.Y > 0.3f) _vy = MathF.Max(0f, _vy);
        }
        Velocity.Y = _vy;
        Place();

        // Spray off the water, dust off the land.
        _spray -= dt;
        if (Grounded && Speed > 4f && _spray <= 0f)
        {
            _spray = 0.07f;
            Vector3 at = new(Position.X - fwd.X * 0.9f, surface + 0.1f, Position.Z - fwd.Z * 0.9f);
            _owner.Puff?.Invoke(at, water ? new Color(0.82f, 0.92f, 1f, 0.55f) : new Color(0.56f, 0.5f, 0.42f, 0.45f), 0.7f + Speed * 0.03f);
        }
    }

    /// <summary>The weather pushes it, N, for this tick (see <see cref="BikeBody"/>).</summary>
    public void Push(Vector3 force) => _push += force / Mass;

    /// <summary>An instant change of momentum, N·s: a tornado's fling.</summary>
    public void Kick(Vector3 impulse) => _kick += impulse / Mass;

    /// <summary>It and its rider, kg.</summary>
    public float Mass => Weight + (Ridden ? Slice.Player.Survivor.Mass : 0f);

    /// <summary>Straight down onto the ground, as a parked bike settles (and where it starts).</summary>
    public void Settle()
    {
        float surface = _owner.SurfaceAt(Position.X, Position.Z, Position.Y + LookFrom, out bool wet);
        OnWater = wet;
        Position.Y = surface + Hover;
        _vy = 0f;
        Place();
        Node.ResetPhysicsInterpolation();
    }

    public void Free() => Node.QueueFree();
}

/// <summary>The hover bikes standing in the zone, and the ground they ride on.</summary>
public sealed partial class HoverBikes : Node3D
{
    readonly SurfaceZone _zone;
    readonly List<HoverBike> _bikes = new();

    /// <summary>Spray and dust (position, colour, size), when something draws them.</summary>
    public Action<Vector3, Color, float> Puff;

    /// <summary>It struck something at speed (where).</summary>
    public Action<Vector3> Bumped;

    public IReadOnlyList<HoverBike> All => _bikes;

    public HoverBikes(SurfaceZone zone) { _zone = zone; Name = "HoverBikes"; }

    /// <summary>Sets a bike down on the ground at a point.</summary>
    public HoverBike Place(Vector3 at, float yaw)
    {
        var bike = new HoverBike(this, at, yaw);
        _bikes.Add(bike);
        bike.Settle();
        return bike;
    }

    public void Remove(HoverBike bike)
    {
        _bikes.Remove(bike);
        bike.Weather?.Unregister(bike.Pushed);
        bike.Free();
    }

    /// <summary>A tornado has caught a ridden bike: the rider is torn off it (and is caught on their own).</summary>
    public Action<HoverBike> TornOff;

    public void Tick(float dt)
    {
        // The weather reaches every bike as it does every other body: drag by mass, a tornado's grip and fling.
        Weather.Forces.ForceModule forces = _zone.Weather?.Bridge?.Forces;
        foreach (HoverBike b in _bikes)
        {
            if (b.Weather == forces) continue;
            b.Weather?.Unregister(b.Pushed);
            b.Pushed ??= new BikeBody(b, this);
            b.Weather = forces;
            forces?.Register(b.Pushed);
        }
        foreach (HoverBike b in _bikes) b.Tick(dt);
    }

    /// <summary>Is a point out of the weather (a place's roof, a deep hole): the zone's own shelter rule.</summary>
    internal bool Sheltered(Vector3 at) => _zone.Weather?.ShelteredAt(at) ?? false;

    /// <summary>The nearest bike whose seat is within reach of a point, and how far.</summary>
    public HoverBike Nearest(Vector3 from, float reach, out float distance)
    {
        HoverBike best = null;
        distance = reach;
        foreach (HoverBike b in _bikes)
        {
            if (b.Ridden) continue;
            float d = from.DistanceTo(b.Seat);
            if (d < distance) { best = b; distance = d; }
        }
        return best;
    }

    /// <summary>The bike the look ray points at (a sphere about its body), within reach.</summary>
    public HoverBike Aimed(Vector3 eye, Vector3 dir, float reach)
    {
        HoverBike best = null;
        float bestAlong = reach;
        foreach (HoverBike b in _bikes)
        {
            if (b.Ridden) continue;
            Vector3 to = b.Seat - eye;
            float along = to.Dot(dir);
            if (along < 0f || along > bestAlong) continue;
            if ((eye + dir * along - b.Seat).Length() > 1.1f) continue;
            best = b;
            bestAlong = along;
        }
        return best;
    }

    // ---- The ground ------------------------------------------------------------------------------

    /// <summary>
    /// The height a bike rides over at a column: the drawn ground (the voxel field's own zero
    /// surface, found by walking down from <paramref name="from"/>), a base's hull if one is higher,
    /// and the water's surface where the ground lies under the sea or a lake.
    /// </summary>
    public float SurfaceAt(float x, float z, float from, out bool water)
    {
        float ground = SolidAt(x, z, from);
        // Any water's surface over the column, the sea, a lake or a river, as swimming reads it (and a lava sea's
        // top too: a hover cushion rides any liquid), not just the sea level.
        PlanetSurface.World.PlanetWorld planet = _zone.Planet;
        water = planet.Source.WaterSurfaceAt(Planets.Gen.Mathx.WrapX(x, planet.Width), Planets.Gen.Mathx.Wrap(z, planet.Height), out float surface) && surface > ground;
        return water ? surface : ground;
    }

    /// <summary>The top of what is solid at a column, under <paramref name="from"/>: the drawn ground, a base's hull or a place's blocks, whichever is higher (water aside).</summary>
    public float SolidAt(float x, float z, float from)
    {
        PlanetTerrain terrain = _zone.Planet.Terrain;
        ChunkStore store = terrain.Store;
        float ground = float.NegativeInfinity;
        // Inside the ground already (a bank): climb out first.
        float y = from;
        for (int i = 0; i < 24 && store.SampleDensity(new N.Vector3(x, y, z)) > 0f; i++) y += 1f;
        float step = 0.5f, top = y;
        for (int i = 0; i < 80; i++)
        {
            float next = top - step;
            if (store.SampleDensity(new N.Vector3(x, next, z)) > 0f)
            {
                float lo = next, hi = top; // lo inside, hi outside
                for (int b = 0; b < 7; b++)
                {
                    float mid = (lo + hi) * 0.5f;
                    if (store.SampleDensity(new N.Vector3(x, mid, z)) > 0f) lo = mid; else hi = mid;
                }
                ground = (lo + hi) * 0.5f;
                break;
            }
            top = next;
        }
        if (float.IsNegativeInfinity(ground)) ground = from - 40f;
        // A base's hull, a place's floor or roof, or the ground's own collision under it (the hull layer alone let it ride through a place's blocks).
        // Faces seen from behind (the underside of a bridge or a roof over it, a block it is already in) are not ground under it.
        var query = PhysicsRayQueryParameters3D.Create(new Vector3(x, MathF.Max(from, ground) + 0.5f, z), new Vector3(x, ground - 0.5f, z), Layers.Hull | Layers.World);
        query.HitBackFaces = false;
        var hit = _zone.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count > 0) ground = MathF.Max(ground, ((Vector3)hit["position"]).Y);
        return ground;
    }

    PhysicsShapeQueryParameters3D _hullQuery;

    /// <summary>
    /// The way out of whatever a bike's hull box overlaps (the ground's collision, a place's blocks, a hull), by the engine's
    /// own contact query: the deepest contact, from the point on the box to the point on what it is in; nothing when
    /// clear (within a few millimetres). Pointing down while sunk past a face of the ground, it lifts instead.
    /// </summary>
    public Vector3 PushOut(Basis attitude, Vector3 position)
    {
        _hullQuery ??= new PhysicsShapeQueryParameters3D
        {
            Shape = new BoxShape3D { Size = HoverBike.HullMax - HoverBike.HullMin }, CollisionMask = Layers.World | Layers.Hull, Margin = 0f,
        };
        _hullQuery.Transform = new Transform3D(attitude, position + attitude * ((HoverBike.HullMin + HoverBike.HullMax) * 0.5f));
        Godot.Collections.Array<Vector3> points = _zone.GetWorld3D().DirectSpaceState.CollideShape(_hullQuery, 16);
        // Out the top first: sunk into a floor or a roof (set down in it, dropped on it) it comes up onto it, not out
        // through the side of the block it is in (a mesh of blocks offers a short way out sideways at every seam).
        Vector3 push = Vector3.Zero, up = Vector3.Zero;
        float deepest = 0.005f, deepestUp = 0.005f;
        for (int i = 0; i + 1 < points.Count; i += 2)
        {
            Vector3 way = points[i + 1] - points[i];
            float depth = way.Length();
            if (way.Y > 0.7f * depth && depth > deepestUp) { deepestUp = depth; up = way; }
            if (depth <= deepest) continue;
            deepest = depth;
            push = way;
        }
        if (up != Vector3.Zero) push = up;
        if (push == Vector3.Zero) return push;
        if (push.Y < -0.2f * push.Length()) push = Vector3.Up * MathF.Min(push.Length(), 0.5f);
        return push * 1.02f;
    }

    /// <summary>Is something solid in the way along a direction (a trunk, a rock, a wall)?</summary>
    public bool Blocked(Vector3 from, Vector3 dir, float length)
    {
        var query = PhysicsRayQueryParameters3D.Create(from, from + dir * length, Layers.World | Layers.Hull | Layers.Props | PlanetSurface.World.Layers.Flora);
        var hit = _zone.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0) return false;
        // A gentle slope is ridden up, not a wall.
        return ((Vector3)hit["normal"]).Y < 0.55f;
    }
}

/// <summary>The bike's look, built from primitives: a slate hull, a nose, a seat, bars and two glowing pads.</summary>
static class BikeModel
{
    public static Node3D Build()
    {
        var root = new Node3D();
        Material Paint(Color c, float metal = 0.4f, float rough = 0.5f) => new StandardMaterial3D { AlbedoColor = c, Metallic = metal, Roughness = rough };
        void Part(Mesh mesh, Vector3 at, Material m, Vector3? rot = null)
        {
            var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = m, Position = at };
            if (rot is Vector3 r) mi.Rotation = r;
            root.AddChild(mi);
        }
        Material shell = Paint(new Color(0.26f, 0.32f, 0.4f)), trim = Paint(new Color(0.95f, 0.55f, 0.12f), 0.2f, 0.6f), dark = Paint(new Color(0.1f, 0.11f, 0.13f), 0.2f, 0.8f);
        var glow = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.2f, 0.9f, 1f), EmissionEnabled = true, Emission = new Color(0.2f, 0.9f, 1f), EmissionEnergyMultiplier = 2.2f,
        };
        Part(new BoxMesh { Size = new Vector3(0.62f, 0.26f, 1.9f) }, new Vector3(0f, 0.22f, 0f), shell);
        Part(new BoxMesh { Size = new Vector3(0.5f, 0.18f, 0.6f) }, new Vector3(0f, 0.3f, -1.0f), shell, new Vector3(-0.28f, 0f, 0f));
        Part(new BoxMesh { Size = new Vector3(0.5f, 0.3f, 0.55f) }, new Vector3(0f, 0.5f, -0.45f), trim);
        Part(new BoxMesh { Size = new Vector3(0.36f, 0.12f, 0.75f) }, new Vector3(0f, 0.42f, 0.4f), dark);
        Part(new BoxMesh { Size = new Vector3(0.5f, 0.34f, 0.14f) }, new Vector3(0f, 0.62f, 0.85f), shell);
        Part(new BoxMesh { Size = new Vector3(0.06f, 0.4f, 0.06f) }, new Vector3(0f, 0.78f, -0.2f), dark);
        Part(new BoxMesh { Size = new Vector3(0.82f, 0.05f, 0.07f) }, new Vector3(0f, 0.98f, -0.2f), dark);
        Part(new BoxMesh { Size = new Vector3(0.05f, 0.4f, 0.55f) }, new Vector3(0f, 0.55f, 0.92f), trim);
        foreach (float side in new[] { -1f, 1f })
        {
            Part(new CylinderMesh { TopRadius = 0.17f, BottomRadius = 0.17f, Height = 1.5f }, new Vector3(side * 0.44f, 0.14f, 0.1f), shell, new Vector3(MathF.PI / 2f, 0f, 0f));
            Part(new CylinderMesh { TopRadius = 0.26f, BottomRadius = 0.26f, Height = 0.04f }, new Vector3(side * 0.44f, -0.02f, -0.35f), glow);
            Part(new CylinderMesh { TopRadius = 0.26f, BottomRadius = 0.26f, Height = 0.04f }, new Vector3(side * 0.44f, -0.02f, 0.55f), glow);
        }
        root.AddChild(new OmniLight3D { Position = new Vector3(0f, -0.25f, 0f), LightColor = new Color(0.3f, 0.9f, 1f), LightEnergy = 1.1f, OmniRange = 4.5f, ShadowEnabled = false });
        return root;
    }
}

/// <summary>
/// A hover bike as the weather's force module sees it: its weight (and its rider's), its drag area
/// and where it is. The wind and a tornado push it by the same rules as every other body; out of the
/// weather (a roof, a deep hole) nothing reaches it. A tornado that catches a ridden bike tears the
/// rider off, and each is thrown by its own mass.
/// </summary>
internal sealed class BikeBody : Weather.Forces.IForceBody
{
    readonly HoverBike _bike;
    readonly HoverBikes _owner;

    public BikeBody(HoverBike bike, HoverBikes owner)
    {
        _bike = bike;
        _owner = owner;
    }

    public float Mass => _bike.Mass;
    public float DragArea => _owner.Sheltered(_bike.Position) ? 0f : HoverBike.DragArea + (_bike.Ridden ? 0.9f : 0f);
    public Vector3 Position => _bike.Position + Vector3.Up * 0.5f;
    public Vector3 Velocity => _bike.Velocity;

    public void ApplyForce(Vector3 force)
    {
        if (_owner.Sheltered(_bike.Position)) return;
        if (_bike.Ridden && _bike.Weather?.PhaseOf(this) == Weather.Forces.VortexPhase.Caught) _owner.TornOff?.Invoke(_bike);
        _bike.Push(force);
    }

    public void ApplyImpulse(Vector3 impulse)
    {
        if (!_owner.Sheltered(_bike.Position)) _bike.Kick(impulse);
    }
}
