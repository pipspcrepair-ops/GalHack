using System;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using PhysicsSandbox.Bridges;
using Weather.Forces;
using N = System.Numerics;

namespace PhysicsSandbox.World;

/// <summary>
/// The player: a registered body like any other (100 kg, CdA 0.9 m², a suit with a jetpack),
/// walked by the shared <see cref="WalkController"/> and moved by a character controller. It
/// boards a structure by walking into its interior (then it walks in the structure's cabin
/// world), stands on outer hulls (then the hull carries it), sits in a cockpit seat to fly,
/// and is hurt by impacts through weather's health, with the tornado fling's floor.
/// </summary>
public sealed class Player
{
    public const float Mass = 100f, Radius = 0.35f, Height = 1.8f, EyeHeight = 1.6f;

    public readonly SandboxWorld World;
    public readonly Body Body;
    public readonly CharacterBinding Binding;
    public readonly WalkController Legs = new();
    public readonly Health Health;

    public CharacterBody3D Node => Binding.Character;

    /// <summary>View heading and pitch, radians, in the frame the body is in (the world, or the cabin).</summary>
    public float Yaw, Pitch;

    /// <summary>The ship whose seat the player sits in, or null.</summary>
    public Ship Seated { get; private set; }

    /// <summary>The ship whose cabin the player is in, or null.</summary>
    public Ship Aboard => Body.Frame == null ? null : World.ShipOf(Body.Frame);

    /// <summary>The ship whose outer hull the player stands on, or null.</summary>
    public Ship OnHull => Body.Platform == null ? null : World.ShipOf(Body.Platform);

    public float HealthShare => Health.Current / Health.Max;

    /// <summary>Raised when the player boards or leaves a cabin, or stands on or steps off a hull.</summary>
    public event Action<string> Moved;

    Body _lastPlatform;

    Player(SandboxWorld world, Body body, CharacterBinding binding)
    {
        World = world;
        Body = body;
        Binding = binding;
        Health = new Health(body.Integrity) { RegenDelay = 8f, RegenPerSecond = body.Integrity * 0.02f };
        body.DamageSink = new HealthSink(Health, world.Weather);
        body.Controller = Legs;
        binding.Carried += turned => Yaw += turned; // the hull turned under us: so does our view
    }

    /// <summary>What other people (and third-person cameras) see, always drawn in the main world.</summary>
    public Node3D View { get; private set; }

    /// <summary>The body's pose in the world, from interpolated transforms: the cabin's frame times the cabin pose while aboard.</summary>
    public Transform3D WorldTransformInterpolated()
    {
        var yaw = new Basis(Vector3.Up, Yaw);
        if (Seated != null)
            return Seated.Node.GetGlobalTransformInterpolated() * new Transform3D(yaw, Seated.Seat.ToGodot());
        Vector3 feet = Node.GetGlobalTransformInterpolated().Origin;
        Ship aboard = Aboard;
        return aboard == null ? new Transform3D(yaw, feet) : aboard.Node.GetGlobalTransformInterpolated() * new Transform3D(yaw, feet);
    }

    void BuildView()
    {
        View = new Node3D { Name = "PlayerView", PhysicsInterpolationMode = Godot.Node.PhysicsInterpolationModeEnum.Off };
        View.AddChild(new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = Radius, Height = Height },
            Position = Vector3.Up * (Height / 2f),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = Palette.Yellow, Roughness = 0.6f },
        });
        View.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.46f, 0.2f, 0.14f) },
            Position = new Vector3(0f, 1.55f, -0.3f),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("1b263b"), Metallic = 0.6f, Roughness = 0.1f },
        });
        View.AddChild(new MeshInstance3D // backpack
        {
            Mesh = new BoxMesh { Size = new Vector3(0.5f, 0.6f, 0.22f) },
            Position = new Vector3(0f, 1.15f, 0.33f),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = Palette.Dark, Roughness = 0.7f },
        });
        World.AddChild(View);
    }

    public static Player Spawn(SandboxWorld world, N.Vector3 feet)
    {
        Body body = world.Physics.Register(new BodySpec
        {
            Kind = BodyKind.Player,
            Label = "Player",
            Mass = Mass,
            DragArea = 0.9f,
            Shape = new StuckShape(Height, Radius),
            Position = feet,
        });
        var c = new CharacterBody3D { Name = "Player", CollisionLayer = Layers.Player, CollisionMask = Layers.PlayerMask };
        c.AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = Radius, Height = Height }, Position = Vector3.Up * (Height / 2f) });
        world.AddChild(c);
        var player = new Player(world, body, world.Host.Bind(body, new CharacterBinding(c)));
        player.Binding.Teleport();
        world.Weather.Track(body);
        if (world.Visuals) player.BuildView();
        return player;
    }

    // ---- Every tick, after physics ----------------------------------------------------------

    public void AfterPhysics(float dt)
    {
        Health.Tick(dt);
        if (Body.OnFloor) World.Weather.Landed(Body); // a fling's protection ends when you touch down

        if (Seated != null)
        {
            Body.Position = Seated.Seat + N.Vector3.UnitY * 0.05f;
            Body.Velocity = N.Vector3.Zero;
            Binding.Teleport();
            return;
        }

        if (Body.Frame == null)
        {
            foreach (Ship ship in World.Ships)
            {
                if (ship.Interior.Count == 0) continue;
                N.Vector3 local = ship.ToLocal(Axis(Body.Position));
                if (!ship.Interior.ContainsDeeply(local, Radius)) continue;
                Board(ship);
                break;
            }
        }
        else
        {
            Ship ship = Aboard;
            if (ship == null || !ship.Interior.Contains(Axis(Body.Position))) Leave();
        }

        if (Body.Platform != _lastPlatform)
        {
            Moved?.Invoke(Body.Platform != null ? $"standing on {Body.Platform.Label}" : $"stepped off {_lastPlatform?.Label}");
            _lastPlatform = Body.Platform;
        }
        Binding.Floating = Body.Frame != null && Body.Frame.Core is { On: false };
        Legs.Floating = Binding.Floating;
        Legs.Yaw = Yaw;
        Legs.Pitch = Pitch;
    }

    static N.Vector3 Axis(N.Vector3 feet) => feet + N.Vector3.UnitY * 0.9f;

    // ---- Changing frames ------------------------------------------------------------------

    /// <summary>Walks into a ship's cabin world: position, velocity and view become ship-local.</summary>
    public void Board(Ship ship)
    {
        N.Vector3 look = WorldLook();
        Riding.EnterCabin(Body, ship.Body);
        Node.Reparent(ship.Cabin, false);
        Node.CollisionLayer = Layers.Interior;
        Node.CollisionMask = Layers.CabinMask;
        SetLook(Riding.DirectionToLocal(ship.Body, look));
        Binding.Teleport();
        Moved?.Invoke($"aboard {ship.Structure.Name}");
    }

    /// <summary>Steps out of the cabin into the world, keeping the ship's velocity at the door.</summary>
    public void Leave()
    {
        Ship ship = Aboard;
        N.Vector3 look = WorldLook();
        Riding.LeaveCabin(Body);
        Node.Reparent(World, false);
        Node.CollisionLayer = Layers.Player;
        Node.CollisionMask = Layers.PlayerMask;
        SetLook(look);
        Binding.Teleport();
        Moved?.Invoke($"left {ship?.Structure.Name}");
    }

    /// <summary>Sits in a ship's cockpit seat: the body stops simulating and the controls fly the ship.</summary>
    public bool Sit(Ship ship)
    {
        if (ship.Flight == null) return false;
        if (Aboard != ship) PlaceAboard(ship, ship.Seat);
        Seated = ship;
        Body.Asleep = true;
        Node.CollisionLayer = 0;
        Node.CollisionMask = 0;
        Yaw = 0f;
        Pitch = 0f;
        ship.Flight.Input = ShipHandling.Handling.PilotInput.LetGo;
        return true;
    }

    public void StandUp()
    {
        Ship ship = Seated;
        if (ship == null) return;
        Seated = null;
        ship.Flight.Input = ShipHandling.Handling.PilotInput.Autopilot; // nobody at the controls: it holds course
        Body.Asleep = false;
        Node.CollisionLayer = Layers.Interior;
        Node.CollisionMask = Layers.CabinMask;
        Body.Position = ship.Seat + new N.Vector3(0f, 0.1f, 1.2f);
        Binding.Teleport();
    }

    /// <summary>Puts the player in a ship's cabin at a ship-local spot, at rest relative to the ship. Tests and screenshots.</summary>
    public void PlaceAboard(Ship ship, N.Vector3 local)
    {
        if (Seated != null) StandUp();
        if (Aboard != ship)
        {
            if (Body.Frame != null) Leave();
            Riding.StepOff(Body);
            Body.Position = ship.Body.ToParent(local);
            Body.Velocity = ship.Body.PointVelocity(Body.Position);
            Board(ship);
        }
        Body.Position = local;
        Body.Velocity = N.Vector3.Zero;
        Binding.Teleport();
    }

    /// <summary>Puts the player in the world (or on a hull it lands on), at rest. Tests and screenshots.</summary>
    public void PlaceOutside(N.Vector3 feet, N.Vector3 velocity = default)
    {
        if (Seated != null) StandUp();
        if (Body.Frame != null) Leave();
        Riding.StepOff(Body);
        Body.Position = feet;
        Body.Velocity = velocity;
        Body.OnFloor = false;
        Binding.Teleport();
    }

    // ---- View --------------------------------------------------------------------------

    /// <summary>The look direction in the world.</summary>
    public N.Vector3 WorldLook()
    {
        N.Vector3 local = MathX.Rotate(MathX.ThenLocal(MathX.Yaw(Yaw), MathX.AxisAngle(N.Vector3.UnitX, Pitch)), -N.Vector3.UnitZ);
        return Body.Frame != null ? Riding.DirectionToWorld(Body.Frame, local) : local;
    }

    void SetLook(N.Vector3 forward)
    {
        forward = N.Vector3.Normalize(forward);
        Yaw = MathF.Atan2(-forward.X, -forward.Z);
        Pitch = MathF.Asin(Math.Clamp(forward.Y, -1f, 1f));
    }

    /// <summary>Where the eyes are, in the world: the cabin's frame times the cabin-local eye while aboard.</summary>
    public Transform3D EyeTransform()
    {
        var look = new Basis(Vector3.Up, Yaw) * new Basis(Vector3.Right, Pitch);
        N.Vector3 eye = Seated != null ? Seated.Seat + N.Vector3.UnitY * 0.95f : Body.Position + N.Vector3.UnitY * EyeHeight;
        var local = new Transform3D(look, eye.ToGodot());
        Body frame = Body.Frame ?? Seated?.Body;
        if (frame == null) return local;
        return World.ShipOf(frame).Node.GlobalTransform * local;
    }
}
