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
    public const float TurnRate = 1.7f; // rad/s at full lock
    /// <summary>The seated eye, above the underside and a little back, m.</summary>
    public static readonly Vector3 EyeAt = new(0f, 1.3f, 0.15f);

    /// <summary>What it weighs, kg, and its drag area, m² (a rider adds theirs): the wind and a tornado take it by these.</summary>
    public const float Weight = 220f, DragArea = 1.0f;

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

    /// <summary>The turn the last tick made, so the rider's view turns with the bike.</summary>
    public float LastTurn { get; private set; }

    public bool Grounded { get; private set; } = true;
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

    void Place()
    {
        Node.GlobalTransform = new Transform3D(
            new Basis(Vector3.Up, Yaw) * new Basis(Vector3.Right, _pitch) * new Basis(Vector3.Forward, _roll), Position);
    }

    /// <summary>One physics tick.</summary>
    public void Tick(float dt)
    {
        LastTurn = 0f;
        float throttle = Ridden ? Throttle : 0f, steer = Ridden ? Steer : 0f;
        bool boost = Ridden && Boost, hop = Ridden && Hop;

        // Heading: a turn takes more lock the faster it goes (a hover bike barely turns at a standstill).
        float turn = -steer * TurnRate * Mathf.Clamp(Speed / 7f, 0.4f, 1f) * dt;
        Yaw += turn;
        LastTurn = turn;

        // The weather's push first: drag by its area over its weight, a tornado's pull and fling.
        Velocity += new Vector3(_push.X, 0f, _push.Z) * dt + new Vector3(_kick.X, 0f, _kick.Z);
        _vy += _push.Y * dt + _kick.Y;
        bool tossed = _push.Y > 12f || _kick.Y > 0.5f;
        _push = _kick = Vector3.Zero;

        Vector3 fwd = Forward, right = Right;
        Vector3 flat = new(Velocity.X, 0f, Velocity.Z);
        float vf = flat.Dot(fwd), vs = flat.Dot(right);
        float max = boost ? BoostSpeed : MaxSpeed;
        if (throttle > 0f) vf = vf < max ? MathF.Min(max, vf + Accel * (boost ? 1.6f : 1f) * throttle * dt) : MathF.Max(max, vf - 12f * dt);
        else if (throttle < 0f) vf = MathF.Max(-ReverseSpeed, vf - (vf > 0f ? Brake : 10f) * -throttle * dt);
        else if (Grounded) vf *= MathF.Exp(-0.55f * dt);
        if (Grounded) vs *= MathF.Exp(-3.2f * dt); // it drifts a little through a turn, then grips (only on its cushion: in the air it flies as thrown)
        Vector3 move = fwd * vf + right * vs;

        // Ahead: a wall, a cliff or a trunk stops it (it slides along nothing: it just loses the speed).
        float step = move.Length() * dt;
        if (step > 1e-4f)
        {
            Vector3 dir = move / (step / dt);
            Vector3 probe = Position + dir * (step + 0.9f);
            float ahead = _owner.SurfaceAt(probe.X, probe.Z, Position.Y + 4f, out _);
            bool cliff = ahead > Position.Y - Hover + 0.8f && ahead > _owner.SurfaceAt(Position.X, Position.Z, Position.Y + 4f, out _) + 0.8f;
            bool wall = _owner.Blocked(Position + Vector3.Up * 0.5f, dir, step + 1.0f);
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
        float surface = _owner.SurfaceAt(Position.X, Position.Z, Position.Y + 4f, out bool water);
        OnWater = water;
        float target = surface + Hover;
        if (Position.Y > target + 0.35f || tossed) { _vy -= 12f * dt; Grounded = false; }
        else
        {
            _vy += (70f * (target - Position.Y) - 16f * _vy) * dt;
            Grounded = true;
        }
        if (hop && Grounded) { _vy = 7f; Grounded = false; }
        _vy = Mathf.Clamp(_vy, -40f, 22f);
        Position.Y += _vy * dt;
        if (Position.Y < surface + 0.35f) { Position.Y = surface + 0.35f; _vy = MathF.Max(0f, _vy); }
        Velocity.Y = _vy;

        // The body leans to the slope under it and into a turn.
        float h0 = _owner.SurfaceAt(Position.X + fwd.X * 1.0f, Position.Z + fwd.Z * 1.0f, Position.Y + 4f, out _);
        float h1 = _owner.SurfaceAt(Position.X - fwd.X * 1.0f, Position.Z - fwd.Z * 1.0f, Position.Y + 4f, out _);
        float k = 1f - MathF.Exp(-8f * dt);
        _pitch = Mathf.Lerp(_pitch, Grounded ? Mathf.Clamp(MathF.Atan2(h0 - h1, 2f), -0.5f, 0.5f) : Mathf.Clamp(_vy * 0.02f, -0.35f, 0.35f), k);
        _roll = Mathf.Lerp(_roll, Mathf.Clamp(-steer * Speed * 0.012f - vs * 0.02f, -0.45f, 0.45f), k);
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
        float surface = _owner.SurfaceAt(Position.X, Position.Z, Position.Y + 4f, out bool wet);
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
        // A base's hull or a place's roof under it.
        var query = PhysicsRayQueryParameters3D.Create(new Vector3(x, MathF.Max(from, ground) + 0.5f, z), new Vector3(x, ground - 0.5f, z), Layers.Hull);
        var hit = _zone.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count > 0) ground = MathF.Max(ground, ((Vector3)hit["position"]).Y);
        float sea = _zone.Planet.Source.WaterLevel;
        water = ground < sea;
        return water ? sea : ground;
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
