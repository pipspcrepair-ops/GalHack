using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using N = System.Numerics;

namespace PhysicsSandbox.World;

/// <summary>
/// A supply crate: a free rigid body on the one force path. Wind and tornadoes push it by its
/// mass and drag area, impacts damage it by the one formula, and it never disappears.
/// </summary>
public sealed class Crate
{
    public const float Size = 0.8f, Mass = 60f, DragArea = 0.75f;

    public readonly Body Body;
    public readonly FreeBinding Binding;
    public RigidBody3D Node => Binding.Rigid;

    Crate(Body body, FreeBinding binding)
    {
        Body = body;
        Binding = binding;
    }

    public static Crate Spawn(SandboxWorld world, N.Vector3 at, int colour)
    {
        Body body = world.Physics.Register(new BodySpec
        {
            Kind = BodyKind.Crate,
            Motion = GalHack.Physics.Motion.Free,
            Label = "Crate",
            Mass = Mass,
            DragArea = DragArea,
            Shape = StuckShape.Box(Size),
            Position = at,
        });
        var rigid = new RigidBody3D { Name = $"Crate{body.Id}", CollisionLayer = Layers.Props, CollisionMask = Layers.PropMask };
        rigid.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = Vector3.One * Size } });
        if (world.Visuals)
        {
            Color[] liveries = { Palette.Orange, Palette.Yellow, Palette.Teal, Palette.Red };
            rigid.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = Vector3.One * Size },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = liveries[colour % liveries.Length], Roughness = 0.8f },
            });
            rigid.AddChild(new MeshInstance3D // a dark band round the middle
            {
                Mesh = new BoxMesh { Size = new Vector3(Size + 0.02f, 0.14f, Size + 0.02f) },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = Palette.Dark, Roughness = 0.6f },
            });
        }
        world.AddChild(rigid);
        var binding = world.Host.Bind(body, new FreeBinding(rigid, body));
        binding.Teleport();
        world.Weather.Track(body);
        var crate = new Crate(body, binding);
        world.Crates.Add(crate);
        return crate;
    }
}

/// <summary>
/// A creature as far as physics cares: a walking capsule with its own legs (a wander or a
/// test's commands). Registered with mass and drag like everything else.
/// </summary>
public sealed class Creature
{
    public readonly Body Body;
    public readonly CharacterBinding Binding;
    public readonly WalkController Legs = new() { WalkSpeed = 3f };

    Creature(Body body, CharacterBinding binding)
    {
        Body = body;
        Binding = binding;
    }

    public static Creature Spawn(SandboxWorld world, N.Vector3 feet)
    {
        Body body = world.Physics.Register(new BodySpec
        {
            Kind = BodyKind.Creature,
            Label = "Creature",
            Mass = 220f,
            DragArea = 1.2f,
            Shape = new StuckShape(1.4f, 0.45f),
            Position = feet,
        });
        var c = new CharacterBody3D { Name = $"Creature{body.Id}", CollisionLayer = Layers.Props, CollisionMask = Layers.PlayerMask };
        c.AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 0.45f, Height = 1.4f }, Position = Vector3.Up * 0.7f });
        if (world.Visuals)
            c.AddChild(new MeshInstance3D
            {
                Mesh = new CapsuleMesh { Radius = 0.45f, Height = 1.4f },
                Position = Vector3.Up * 0.7f,
                MaterialOverride = new StandardMaterial3D { AlbedoColor = Palette.DeepRock, Roughness = 0.9f },
            });
        world.AddChild(c);
        var creature = new Creature(body, world.Host.Bind(body, new CharacterBinding(c)));
        body.Controller = creature.Legs;
        creature.Binding.Teleport();
        world.Weather.Track(body);
        world.Creatures.Add(creature);
        return creature;
    }
}
