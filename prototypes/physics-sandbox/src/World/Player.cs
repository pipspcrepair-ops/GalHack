using System;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using PhysicsSandbox.Bridges;
using Weather.Forces;
using N = System.Numerics;

namespace PhysicsSandbox.World;

/// <summary>
/// The player: a registered body like any other (100 kg, CdA 0.9 m², a suit with a jetpack and
/// mag boots), walked by the shared <see cref="WalkController"/> and moved by a character
/// controller. Landing on (or, in the air, touching) a ship's hull puts it in that ship's cabin
/// world, the shared riding mechanism: inside, down is the core's; out on the hull, the boots hold
/// it to the face underfoot. It lets go of a ship by the shared rules (a jump, or out of contact),
/// sits in a cockpit seat to fly, and is hurt through weather's health, with the fling's floor.
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

    /// <summary>View heading and pitch, radians, within the body's walk frame (its up, wherever that points).</summary>
    public float Yaw, Pitch;

    /// <summary>
    /// The walk frame the camera uses, in the body's frame: it eases toward the body's walk frame,
    /// so crossing a hull edge or an airlock rolls the view smoothly instead of snapping it.
    /// </summary>
    public N.Quaternion ViewFrame = N.Quaternion.Identity;

    /// <summary>The ship whose seat the player sits in, or null.</summary>
    public Ship Seated { get; private set; }

    /// <summary>The ship whose cabin world the player is in (inside it or out on its hull), or null.</summary>
    public Ship Aboard => Body.Frame == null ? null : World.ShipOf(Body.Frame);

    /// <summary>The ship whose outer hull the player is on, or null.</summary>
    public Ship OnHull => Body.Frame != null && Body.OnOuterHull ? Aboard : null;

    public float HealthShare => Health.Current / Health.Max;

    /// <summary>
    /// Head under water: the vitals hook. The suit breathes its own oxygen while this is set (the
    /// sandbox has no oxygen to run down; its HUD shows the flag).
    /// </summary>
    public bool Underwater => Body.Water.Underwater;

    /// <summary>Raised when the player joins or lets go of a ship, or passes through its airlock.</summary>
    public event Action<string> Moved;

    Body _letGoOf;
    float _rejoinBlocked;
    bool _wasOutside;

    Player(SandboxWorld world, Body body, CharacterBinding binding)
    {
        World = world;
        Body = body;
        Binding = binding;
        Health = new Health(body.Integrity) { RegenDelay = 8f, RegenPerSecond = body.Integrity * 0.02f };
        body.DamageSink = new HealthSink(Health, world.Weather);
        body.Controller = Legs;
    }

    /// <summary>What other people (and third-person cameras) see, always drawn in the main world.</summary>
    public Node3D View { get; private set; }

    /// <summary>
    /// The body's pose in the world, from interpolated transforms: the cabin's frame times the
    /// cabin pose while aboard, standing along the (eased) view up.
    /// </summary>
    public Transform3D WorldTransformInterpolated()
    {
        if (Seated != null)
            return Seated.Node.GetGlobalTransformInterpolated() * new Transform3D(new Basis(Vector3.Up, Yaw), Seated.Seat.ToGodot());
        var pose = new Transform3D(MathX.ThenLocal(ViewFrame, MathX.Yaw(Yaw)).ToBasis(), Node.GetGlobalTransformInterpolated().Origin);
        Ship aboard = Aboard;
        return aboard == null ? pose : aboard.Node.GetGlobalTransformInterpolated() * pose;
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
        PhysicsRules.RidingData rules = World.Rules.Riding;
        _rejoinBlocked -= dt;

        if (Seated != null)
        {
            Body.Position = Seated.Seat + N.Vector3.UnitY * 0.05f;
            Body.Velocity = N.Vector3.Zero;
            Binding.Teleport();
            return;
        }

        if (Body.Frame == null)
        {
            if (Node.GetParent() != World) MoveNodeTo(World, inCabin: false); // a rescue took it out of a cabin
            // Landing on a hull, or touching one in the air, puts you in that ship's world. Leaning
            // on a parked ship from the ground doesn't: its cabin world has no ground to stand on.
            if (Binding.TouchedHull is Body hull && (Binding.TouchedHullUnderfoot || !Body.OnFloor)
                && !(hull == _letGoOf && _rejoinBlocked > 0f) && World.ShipOf(hull) is Ship ship)
                Join(ship, Binding.TouchedHullNormal);
        }
        else
        {
            Ship ship = Aboard;
            Body.OnOuterHull = !ship.Interior.Contains(Body.Position + Body.Up * 0.9f);
            float distance = ship.DistanceFromHull(Body.Position, rules.LetGoDistance + 0.5f);
            if (Riding.LetGo(rules, Body, Legs.Jumped, Binding.Touching, distance, dt))
            {
                _letGoOf = ship.Body;
                _rejoinBlocked = rules.RejoinAfter;
                Leave();
            }
            else if (Body.OnOuterHull != _wasOutside)
                Moved?.Invoke(Body.OnOuterHull ? $"out on {ship.Structure.Name}'s hull" : $"inside {ship.Structure.Name}");
        }
        _wasOutside = Body.Frame != null && Body.OnOuterHull;

        // Mag boots hold you to the face underfoot out on a hull; anywhere without gravity you float,
        // and in deep water you swim (no floor to snap to either way).
        Binding.MagBoots = _wasOutside && rules.MagBoots;
        Legs.Floating = !Binding.MagBoots && World.Physics.GravityAt(Body).LengthSquared() < 1e-6f;
        Binding.Floating = Legs.Floating || Legs.Swimming;
        Legs.Yaw = Yaw;
        Legs.Pitch = Pitch;

        // The camera's frame eases after the body's: edges and airlocks roll the view smoothly.
        ViewFrame = N.Quaternion.Normalize(N.Quaternion.Slerp(ViewFrame, Body.Rotation, 1f - MathF.Exp(-rules.ViewEase * dt)));
    }

    // ---- Changing frames ------------------------------------------------------------------

    /// <summary>
    /// Into a ship's cabin world (landing on its hull, or placed aboard): world position, velocity
    /// and orientation are kept. On the outer hull in mag boots, down is at once the face touched.
    /// </summary>
    public void Join(Ship ship, N.Vector3? worldNormal = null)
    {
        N.Quaternion view = WorldView();
        Riding.EnterCabin(Body, ship.Body);
        ViewFrame = N.Quaternion.Normalize(N.Quaternion.Conjugate(ship.Body.Rotation) * view);
        MoveNodeTo(ship.Cabin, inCabin: true);
        Body.OnOuterHull = !ship.Interior.Contains(Body.Position + Body.Up * 0.9f);
        if (Body.OnOuterHull && World.Rules.Riding.MagBoots && worldNormal is N.Vector3 n)
            Riding.SetUp(Body, Riding.DirectionToLocal(ship.Body, n));
        _wasOutside = Body.OnOuterHull;
        Binding.MagBoots = _wasOutside && World.Rules.Riding.MagBoots;
        Binding.Teleport();
        Moved?.Invoke(Body.OnOuterHull ? $"on {ship.Structure.Name}'s hull" : $"aboard {ship.Structure.Name}");
    }

    /// <summary>Lets go of the ship: back to the zone's world with the ship's velocity there plus your own.</summary>
    public void Leave()
    {
        Ship ship = Aboard;
        N.Quaternion view = WorldView();
        Riding.LeaveCabin(Body);
        ViewFrame = view;
        MoveNodeTo(World, inCabin: false);
        _wasOutside = false;
        Binding.MagBoots = false;
        Binding.Teleport();
        Moved?.Invoke($"let go of {ship?.Structure.Name}");
    }

    void MoveNodeTo(Node parent, bool inCabin)
    {
        if (Node.GetParent() != parent) Node.Reparent(parent, false);
        Node.CollisionLayer = inCabin ? Layers.Interior : Layers.Player;
        Node.CollisionMask = inCabin ? Layers.CabinMask : Layers.PlayerMask;
    }

    /// <summary>The view frame in the world.</summary>
    N.Quaternion WorldView() => Body.Frame == null ? ViewFrame : N.Quaternion.Normalize(Body.Frame.Rotation * ViewFrame);

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
        MoveNodeTo(ship.Cabin, inCabin: true);
        Body.Position = ship.Seat + new N.Vector3(0f, 0.1f, 1.2f);
        Binding.Teleport();
    }

    /// <summary>
    /// Puts the player in a ship's cabin world at a ship-local spot, at rest relative to the ship:
    /// upright, or standing on a hull face with the given up (ship-local). Tests and screenshots.
    /// </summary>
    public void PlaceAboard(Ship ship, N.Vector3 local, N.Vector3? up = null)
    {
        if (Seated != null) StandUp();
        if (Aboard != ship)
        {
            if (Body.Frame != null) Leave();
            Body.Position = ship.Body.ToParent(local);
            Body.Velocity = ship.Body.PointVelocity(Body.Position);
            Join(ship);
        }
        Body.Position = local;
        Body.Velocity = N.Vector3.Zero;
        Body.Rotation = N.Quaternion.Identity;
        Body.Up = N.Vector3.UnitY;
        if (up is N.Vector3 face) Riding.SetUp(Body, face);
        ViewFrame = Body.Rotation;
        Body.OnOuterHull = !ship.Interior.Contains(local + Body.Up * 0.9f);
        _wasOutside = Body.OnOuterHull;
        Binding.MagBoots = _wasOutside && World.Rules.Riding.MagBoots;
        Binding.Teleport();
    }

    /// <summary>Puts the player in the zone's world (landing on a hull joins it), upright. Tests and screenshots.</summary>
    public void PlaceOutside(N.Vector3 feet, N.Vector3 velocity = default)
    {
        if (Seated != null) StandUp();
        if (Body.Frame != null) Leave();
        Body.Position = feet;
        Body.Velocity = velocity;
        Body.Rotation = N.Quaternion.Identity;
        Body.Up = N.Vector3.UnitY;
        ViewFrame = N.Quaternion.Identity;
        Body.OnFloor = false;
        _letGoOf = null;
        Binding.Teleport();
    }

    // ---- View --------------------------------------------------------------------------

    /// <summary>The look direction in the world.</summary>
    public N.Vector3 WorldLook()
    {
        N.Quaternion look = MathX.ThenLocal(MathX.ThenLocal(WorldView(), MathX.Yaw(Yaw)), MathX.AxisAngle(N.Vector3.UnitX, Pitch));
        return MathX.Forward(look);
    }
}
