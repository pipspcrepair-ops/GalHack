using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using N = System.Numerics;

namespace PhysicsSandbox.World;

/// <summary>One kind of crate, from data/crates.json: a cube of this size, mass, drag area and density.</summary>
public sealed record CrateKind(float Size, float Mass, float DragArea, float Density);

/// <summary>
/// A crate: a free rigid body on the one force path. Wind and tornadoes push it by its mass and
/// drag area, water buoys it by its density (a supply crate floats, an ore crate sinks), impacts
/// damage it by the one formula, and it never disappears.
/// </summary>
public sealed class Crate
{
    public readonly Body Body;
    public readonly FreeBinding Binding;
    public readonly CrateKind Kind;
    public RigidBody3D Node => Binding.Rigid;

    Crate(Body body, FreeBinding binding, CrateKind kind)
    {
        Body = body;
        Binding = binding;
        Kind = kind;
    }

    /// <summary>A crate of a kind from data/crates.json ("supply" unless said otherwise).</summary>
    public static Crate Spawn(SandboxWorld world, N.Vector3 at, int colour, string kind = "supply")
    {
        CrateKind k = world.CrateKinds[kind];
        Body body = world.Physics.Register(new BodySpec
        {
            Kind = BodyKind.Crate,
            Motion = GalHack.Physics.Motion.Free,
            Label = kind == "supply" ? "Crate" : $"Crate ({kind})",
            Mass = k.Mass,
            DragArea = k.DragArea,
            Density = k.Density,
            Shape = StuckShape.Box(k.Size),
            Position = at,
        });
        float size = k.Size;
        var rigid = new RigidBody3D { Name = $"Crate{body.Id}", CollisionLayer = Layers.Props, CollisionMask = Layers.PropMask };
        rigid.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = Vector3.One * size } });
        if (world.Visuals)
        {
            Color[] liveries = { Palette.Orange, Palette.Yellow, Palette.Teal, Palette.Red };
            rigid.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = Vector3.One * size },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = kind == "supply" ? liveries[colour % liveries.Length] : Palette.DeepRock, Roughness = 0.8f },
            });
            rigid.AddChild(new MeshInstance3D // a dark band round the middle
            {
                Mesh = new BoxMesh { Size = new Vector3(size + 0.02f, 0.14f, size + 0.02f) },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = Palette.Dark, Roughness = 0.6f },
            });
        }
        world.AddChild(rigid);
        var binding = world.Host.Bind(body, new FreeBinding(rigid, body));
        binding.Teleport();
        world.Weather.Track(body);
        var crate = new Crate(body, binding, k);
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

    /// <param name="radius">its capsule: a beast of any size, a giant included</param>
    public static Creature Spawn(SandboxWorld world, N.Vector3 feet, float radius = 0.45f, float height = 1.4f)
    {
        Body body = world.Physics.Register(new BodySpec
        {
            Kind = BodyKind.Creature,
            Label = "Creature",
            Mass = 220f,
            DragArea = 1.2f,
            Shape = new StuckShape(height, radius),
            Position = feet,
        });
        var c = new CharacterBody3D { Name = $"Creature{body.Id}", CollisionLayer = Layers.Props, CollisionMask = Layers.PlayerMask };
        c.AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = radius, Height = height }, Position = Vector3.Up * (height / 2f) });
        if (world.Visuals)
            c.AddChild(new MeshInstance3D
            {
                Mesh = new CapsuleMesh { Radius = radius, Height = height },
                Position = Vector3.Up * (height / 2f),
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
