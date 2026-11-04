using System.Collections.Generic;
using System.Linq;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;
using N = System.Numerics;
using HandlingStats = ShipHandling.Handling.HandlingStats;
using Structure = GalHack.Structures.Structure;

namespace PhysicsSandbox.World;

/// <summary>
/// A building structure as a body: a vessel flown by the handling model, or a free wreck. One
/// body, one compound collider built per chunk (see <see cref="StructureCollision"/>), the
/// structure's own mesh, and its cabin world for anyone aboard. Edits go through
/// <see cref="ApplyEdits"/>, which rebuilds only dirty chunks and keeps mass, centre of mass and
/// the four numbers in step with the blocks.
/// </summary>
public sealed class Ship
{
    public readonly SandboxWorld World;
    public readonly Structure Structure;
    public readonly Body Body;
    public readonly InteriorMap Interior;
    public readonly StructureField Field;

    public StructureVessel Spec { get; }
    public HandlingFlight Flight { get; }
    public VesselController Vessel => Body.Controller as VesselController;

    public BodyBinding Binding { get; private set; }
    public Node3D Node => Binding.Node;
    public StructureShapes HullShapes { get; private set; }
    public StructureView View { get; private set; }
    public CabinWorld Cabin { get; private set; }
    public GearView Gear { get; private set; }
    public ShipFx Fx { get; private set; }

    /// <summary>The seat, structure frame (metres); the pilot's eye sits above it.</summary>
    public N.Vector3 Seat { get; private set; }

    public bool IsWreck => Body.Kind == BodyKind.Wreck;

    Ship(SandboxWorld world, Structure structure, Body body)
    {
        World = world;
        Structure = structure;
        Body = body;
        Interior = new InteriorMap(structure);
        Field = new StructureField(body, structure);
        if (body.Kind == BodyKind.Vessel)
        {
            Spec = new StructureVessel(structure, world.Handling);
            Flight = new HandlingFlight(Spec, world.Handling);
        }
        Seat = StructureBridge.SeatOf(structure);
    }

    // ---- Spawning -------------------------------------------------------------------------

    /// <summary>A structure from a blueprint, flown as a vessel: one kinematic hull swept through the world.</summary>
    public static Ship SpawnVessel(SandboxWorld world, Structure s, N.Vector3 at, float heading, bool landed)
    {
        MassProperties mass = StructureBridge.Mass(s);
        Body body = world.Physics.Register(new BodySpec
        {
            Kind = BodyKind.Vessel,
            Motion = GalHack.Physics.Motion.Kinematic,
            Label = s.Name,
            Mass = mass.Mass,
            CenterOfMass = mass.CenterOfMass,
            DragArea = StructureBridge.DragArea(s),
            Shape = StructureBridge.ShapeOf(s),
            Position = at,
            Rotation = MathX.Yaw(heading),
        });
        body.Core = new CoreGravity { On = s.GravityOn, Strength = world.Physics.Zone.Strength };
        var ship = new Ship(world, s, body);
        var controller = new VesselController(ship.Flight, ship.Spec.HoverPoints, ship.Spec.Legs);
        body.Controller = controller;

        var hull = new CharacterBody3D { Name = s.Name.Replace(" ", "") };
        world.AddChild(hull);
        ship.Binding = world.Host.Bind(body, new HullBinding(hull));
        ship.HullShapes = new StructureShapes(hull);
        ship.Furnish(hull);

        if (landed) controller.PlaceLanded(body, at, heading);
        else controller.Place(body, at, heading);
        ship.Binding.Teleport();
        ship.Register();
        return ship;
    }

    /// <summary>A section severed off another structure: a free rigid body with the parent's motion.</summary>
    static Ship SpawnWreck(SandboxWorld world, Structure s, Body body)
    {
        var ship = new Ship(world, s, body);
        var rigid = new RigidBody3D { Name = s.Name.Replace(" ", ""), CollisionLayer = Layers.Hull, CollisionMask = Layers.WreckMask };
        world.AddChild(rigid);
        ship.HullShapes = new StructureShapes(rigid);
        ship.Furnish(rigid);
        ship.Binding = world.Host.Bind(body, new FreeBinding(rigid, body));
        ship.Binding.Teleport();
        ship.Register();
        return ship;
    }

    void Furnish(CollisionObject3D owner)
    {
        List<ChunkColliders> colliders = StructureBridge.Colliders(Structure);
        HullShapes.Apply(colliders);
        if (World.Visuals)
        {
            View = new StructureView { Name = "View" };
            owner.AddChild(View);
            View.Setup(Structure);
            if (Spec != null && Spec.LandingGear.Count > 0)
            {
                Gear = new GearView { Name = "Gear" };
                owner.AddChild(Gear);
                Gear.Setup(this);
            }
            if (Spec != null)
            {
                Fx = new ShipFx { Name = "Fx" };
                owner.AddChild(Fx);
                Fx.Setup(this);
            }
            if (Interior.Count > 0) owner.AddChild(CabinLight());
        }
        Cabin = CabinWorld.Create("CabinWorld");
        owner.AddChild(Cabin);
        Cabin.Shapes.Apply(colliders);
        if (Body.Core != null) Cabin.SetGravity(Body.Core);
        Structure.DirtyChunks.Clear();
    }

    /// <summary>A warm light in the middle of the interior, so the cabin reads from inside.</summary>
    OmniLight3D CabinLight()
    {
        N.Vector3 middle = N.Vector3.Zero;
        int n = 0;
        var (min, max) = Structure.Bounds();
        for (int x = min.X; x <= max.X; x++)
            for (int y = min.Y; y <= max.Y; y++)
                for (int z = min.Z; z <= max.Z; z++)
                    if (Interior.IsInterior(new Int3(x, y, z)))
                    {
                        middle += new N.Vector3(x, y, z);
                        n++;
                    }
        middle = middle / n * Structure.Grid.CellSize;
        return new OmniLight3D
        {
            Name = "CabinLight",
            Position = (middle + N.Vector3.UnitY * 0.6f).ToGodot(),
            OmniRange = 7f,
            LightEnergy = 1.8f,
            LightColor = new Color("ffe2b0"),
        };
    }

    void Register()
    {
        World.Physics.StuckFields.Add(Field);
        World.Weather.Track(Body);
        World.Ships.Add(this);
    }

    // ---- Edits ------------------------------------------------------------------------------

    /// <summary>
    /// After blocks changed: rebuild the dirty chunks' colliders (hull and cabin) and meshes,
    /// refresh mass and centre of mass, and tell the world so anything near is woken and checked
    /// for being stuck inside the new blocks.
    /// </summary>
    public void ApplyEdits()
    {
        if (Structure.DirtyChunks.Count == 0) return;
        var dirty = Structure.DirtyChunks.ToList();
        Structure.DirtyChunks.Clear();
        List<ChunkColliders> colliders = StructureBridge.Colliders(Structure, dirty);
        HullShapes.Apply(colliders);
        Cabin.Shapes.Apply(colliders);
        View?.Rebuild(dirty);

        if (Structure.Count > 0)
        {
            MassProperties mass = StructureBridge.Mass(Structure);
            Body.Mass = mass.Mass;
            Body.CenterOfMass = mass.CenterOfMass;
            Body.Shape = StructureBridge.ShapeOf(Structure);
            if (Binding is FreeBinding free)
            {
                free.Rigid.Mass = mass.Mass;
                free.Rigid.CenterOfMass = mass.CenterOfMass.ToGodot();
            }
        }
        World.Physics.NotifyChanged(RegionOf(dirty));
    }

    /// <summary>The world-space box around some of the structure's chunks.</summary>
    public Region RegionOf(IEnumerable<Int3> chunks)
    {
        float cell = Structure.Grid.CellSize, size = 16 * cell;
        N.Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        foreach (Int3 c in chunks)
            for (int i = 0; i < 8; i++)
            {
                var corner = new N.Vector3(c.X + (i & 1), c.Y + ((i >> 1) & 1), c.Z + ((i >> 2) & 1)) * size - new N.Vector3(cell * 0.5f);
                N.Vector3 w = Body.ToParent(corner);
                min = N.Vector3.Min(min, w);
                max = N.Vector3.Max(max, w);
            }
        return new Region(min, max);
    }

    /// <summary>
    /// A blast on the structure (cell units): blocks lose hit points, destroyed ones are gone,
    /// and every section cut off from the core becomes a wreck body that carries on with the
    /// ship's own motion, then is pushed away by the blast.
    /// </summary>
    public (DamageReport report, List<Ship> wrecks) Blast(N.Vector3 cellCentre, float radius, float amount, float push)
    {
        DamageReport report = Damage.Blast(Structure, cellCentre, radius, DamageType.Explosive, amount);
        var wrecks = new List<Ship>();
        foreach (Structure piece in report.Wrecks)
        {
            Ship wreck = Sever(piece);
            wrecks.Add(wreck);
            if (push > 0f)
            {
                // The blast throws the section away from where it was cut, and up, so it tumbles.
                N.Vector3 at = Body.ToParent(cellCentre * Structure.Grid.CellSize);
                N.Vector3 away = MathX.SafeNormalize(wreck.Body.CenterOfMassPosition - at, N.Vector3.UnitY);
                N.Vector3 throwDir = N.Vector3.Normalize(away + N.Vector3.UnitY * 0.8f);
                World.Physics.AddImpulseAt(wreck.Body, throwDir * push * wreck.Body.Mass, at, ForceKind.Contact);
            }
        }
        ApplyEdits();
        return (report, wrecks);
    }

    /// <summary>
    /// A shot through particular blocks (a pylon, a pod's mounting): they are destroyed, and
    /// every section that loses its path to the core breaks away as a wreck. The wrecks are
    /// returned with the destroyed blocks, so tests can account for every kilogram.
    /// </summary>
    public (List<(Int3 pos, Block block)> destroyed, List<Ship> wrecks) ShootThrough(IEnumerable<Int3> cells, float push = 0f)
    {
        var report = new DamageReport();
        foreach (Int3 p in cells) Damage.Hit(Structure, p, DamageType.Kinetic, 1e6f, report);
        var wrecks = new List<Ship>();
        foreach (Structure piece in Connectivity.SplitOff(Structure))
        {
            Ship wreck = Sever(piece);
            wrecks.Add(wreck);
            if (push > 0f)
            {
                N.Vector3 cut = N.Vector3.Zero;
                foreach (var (p, _) in report.Destroyed) cut += StructureBridge.CellCenter(Structure, p);
                // The blast throws the section away from the cut and up, from the cut itself, so
                // it flips over as it goes.
                N.Vector3 at = Body.ToParent(cut / System.Math.Max(1, report.Destroyed.Count));
                N.Vector3 away = MathX.SafeNormalize(wreck.Body.CenterOfMassPosition - at, N.Vector3.UnitY);
                N.Vector3 throwDir = N.Vector3.Normalize(away + N.Vector3.UnitY * 0.8f);
                World.Physics.AddImpulseAt(wreck.Body, throwDir * push * wreck.Body.Mass, at, ForceKind.Contact);
            }
        }
        ApplyEdits();
        return (report.Destroyed, wrecks);
    }

    /// <summary>The cells that mount the port engine pod to the hull: shoot these and the pod comes off.</summary>
    public static readonly Int3[] PortPodMount =
    {
        new(-4, 1, 6), new(-4, 2, 6), new(-4, 3, 6), new(-3, 1, 6), new(-3, 2, 6), new(-3, 3, 6),
    };

    /// <summary>
    /// Makes a body for a section that has just been split off this structure (it has the same
    /// grid coordinates): mass and momentum move with the blocks, nothing is lost.
    /// </summary>
    public Ship Sever(Structure piece)
    {
        MassProperties remaining = StructureBridge.Mass(Structure);
        MassProperties mass = StructureBridge.Mass(piece);
        Body wreckBody = World.Physics.Sever(Body, remaining.Mass, remaining.CenterOfMass, new BodySpec
        {
            Label = piece.Name,
            Mass = mass.Mass,
            CenterOfMass = mass.CenterOfMass,
            DragArea = StructureBridge.DragArea(piece),
            Shape = StructureBridge.ShapeOf(piece),
        });
        piece.DirtyChunks.Clear();
        Ship wreck = SpawnWreck(World, piece, wreckBody);
        // A moment ago the two were one rigid body, touching along every cut edge: let them part
        // without the engine shoving them apart (or the hull "ramming" its own section).
        if (Binding.Collider is PhysicsBody3D hull && wreck.Binding.Collider is PhysicsBody3D piece3d) World.Separate(hull, piece3d, 0.5f);
        return wreck;
    }

    /// <summary>
    /// No hull points left: the vessel loses power and becomes a free wreck the engine tumbles.
    /// Its shapes, mesh and cabin (with anyone in it) move to a rigid body; nothing is rebuilt.
    /// </summary>
    public void BecomeWreck()
    {
        if (Binding is not HullBinding hull) return;
        var rigid = new RigidBody3D { Name = hull.Hull.Name + "Wreck", CollisionLayer = Layers.Hull, CollisionMask = Layers.WreckMask };
        World.AddChild(rigid);
        rigid.GlobalTransform = hull.Hull.GlobalTransform;
        HullShapes.MoveTo(rigid);
        // Everything else rides along unchanged: the mesh, the legs, the lights, the cabin world
        // (with anyone in it).
        foreach (Node child in hull.Hull.GetChildren())
            if (child is not CollisionShape3D) child.Reparent(rigid, false);
        var free = World.Host.Rebind(Body, new FreeBinding(rigid, Body));
        Binding = free;
        free.Teleport();
        hull.Hull.GetParent().RemoveChild(hull.Hull); // out of the physics space now, not at the end of the frame
        hull.Hull.QueueFree();
    }

    // ---- Helpers --------------------------------------------------------------------------

    /// <summary>The structure-frame point of a world point.</summary>
    public N.Vector3 ToLocal(N.Vector3 world) => Body.ToLocal(world);

    public HandlingStats Stats => Flight?.Stats;

    /// <summary>Total linear momentum of every block, each moving with the body's rigid motion. For the severing test.</summary>
    public static N.Vector3 BlockMomentum(Structure s, Body body, out float mass)
    {
        double m = 0;
        N.Vector3 p = N.Vector3.Zero;
        foreach (var (cell, b) in s.Blocks())
        {
            float bm = Units.Kilograms(s.StatsOf(b).Mass);
            m += bm;
            p += bm * body.PointVelocity(body.ToParent(StructureBridge.CellCenter(s, cell)));
        }
        mass = (float)m;
        return p;
    }
}
