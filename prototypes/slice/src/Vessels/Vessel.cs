using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;
using Slice.Surface;
using N = System.Numerics;
using HandlingStats = ShipHandling.Handling.HandlingStats;
using Inventory = GalHack.Structures.Inventory;
using Structure = GalHack.Structures.Structure;

namespace Slice.Vessels;

/// <summary>
/// A building structure as one body in the zone (the physics sandbox's Ship, on a planet):
/// a vessel flown by the handling model, a pod that never flies, or a free wreck. One body, one
/// compound collider built per chunk, building's own mesher for its look, and a cabin world for
/// anyone aboard. Blocks change through <see cref="ApplyEdits"/>, which rebuilds only dirty chunks
/// and keeps mass, centre of mass, landing legs and the four numbers in step with the blocks.
/// </summary>
public sealed class Vessel
{
    public readonly SurfaceZone Zone;
    public readonly Structure Structure;
    public readonly Body Body;
    public readonly InteriorMap Interior;
    public readonly StructureField Field;

    public StructureVessel Spec { get; private set; }
    public HandlingFlight Flight { get; private set; }
    public VesselController Controller => Body.Controller as VesselController;

    public BodyBinding Binding { get; private set; }
    public Node3D Node => Binding.Node;
    public StructureShapes HullShapes { get; private set; }
    public StructureMesh View { get; private set; }
    public CabinWorld Cabin { get; private set; }
    public VesselFx Fx { get; private set; }

    /// <summary>
    /// PLACEHOLDER shared storage for building materials, held in the structure's containers
    /// (one storage unit per unit of material, up to the structure's storage capacity).
    /// </summary>
    public readonly Inventory Cargo = new() { Creative = false };

    public bool IsWreck => Body.Kind == BodyKind.Wreck;
    public HandlingStats Stats => Flight?.Stats;
    public string Name => Structure.Name;

    /// <summary>The cockpit seat (structure frame, metres), or null if it has none.</summary>
    public N.Vector3? Seat { get; private set; }

    string _legsKey = "";

    Vessel(SurfaceZone zone, Structure structure, Body body)
    {
        Zone = zone;
        Structure = structure;
        Body = body;
        Interior = new InteriorMap(structure);
        Field = new StructureField(body, structure);
        if (body.Kind == BodyKind.Vessel)
        {
            Spec = new StructureVessel(structure, zone.Data.Handling);
            Flight = new HandlingFlight(Spec, zone.Data.Handling);
        }
        RefreshSeat();
    }

    // ---- Spawning ---------------------------------------------------------------------------

    /// <summary>A structure as a vessel: one kinematic hull swept through the world, landed (asleep) or flying.</summary>
    public static Vessel Spawn(SurfaceZone zone, Structure s, N.Vector3 at, float heading, bool landed)
    {
        MassProperties mass = MassOf(s);
        Body body = zone.Physics.Register(new BodySpec
        {
            Kind = BodyKind.Vessel,
            Motion = GalHack.Physics.Motion.Kinematic,
            Label = s.Name,
            Mass = mass.Mass,
            CenterOfMass = mass.CenterOfMass,
            DragArea = s.Count > 0 ? StructureBridge.DragArea(s) : 1f,
            Shape = s.Count > 0 ? StructureBridge.ShapeOf(s) : new StuckShape(0.5f, 0.25f, 0.25f),
            Position = at,
            Rotation = MathX.Yaw(heading),
        });
        body.Core = new CoreGravity { On = s.GravityOn, Strength = zone.Physics.Zone.Strength };
        var vessel = new Vessel(zone, s, body);
        vessel._legsKey = vessel.LegsKey();
        body.Controller = new VesselController(vessel.Flight, vessel.Spec.HoverPoints, vessel.Spec.Legs);

        var hull = new CharacterBody3D { Name = s.Name.Replace(" ", "") };
        zone.AddChild(hull);
        vessel.Binding = zone.Host.Bind(body, new HullBinding(hull));
        vessel.HullShapes = new StructureShapes(hull);
        vessel.Furnish(hull);
        if (landed) vessel.Controller.PlaceLanded(body, at, heading);
        else vessel.Controller.Place(body, at, heading);
        vessel.Binding.Teleport();
        vessel.Register();
        return vessel;
    }

    /// <summary>A section severed off another structure: a free rigid body with the parent's motion.</summary>
    static Vessel SpawnWreck(SurfaceZone zone, Structure s, Body body)
    {
        var wreck = new Vessel(zone, s, body);
        var rigid = new RigidBody3D { Name = s.Name.Replace(" ", ""), CollisionLayer = Layers.Hull, CollisionMask = Layers.WreckMask };
        zone.AddChild(rigid);
        wreck.HullShapes = new StructureShapes(rigid);
        wreck.Furnish(rigid);
        wreck.Binding = zone.Host.Bind(body, new FreeBinding(rigid, body));
        wreck.Binding.Teleport();
        wreck.Register();
        return wreck;
    }

    static MassProperties MassOf(Structure s) =>
        s.Count > 0 ? StructureBridge.Mass(s) : new MassProperties(100f, N.Vector3.Zero);

    void Furnish(CollisionObject3D owner)
    {
        List<ChunkColliders> colliders = StructureBridge.Colliders(Structure);
        HullShapes.Apply(colliders);
        View = new StructureMesh { Name = "View" };
        owner.AddChild(View);
        View.Setup(Structure);
        Fx = new VesselFx { Name = "Fx" };
        owner.AddChild(Fx);
        Fx.Setup(this);
        Cabin = CabinWorld.Create("CabinWorld");
        owner.AddChild(Cabin);
        Cabin.Shapes.Apply(colliders);
        if (Body.Core != null) Cabin.SetGravity(Body.Core);
        Structure.DirtyChunks.Clear();
    }

    void Register()
    {
        Zone.Physics.StuckFields.Add(Field);
        Zone.Weather.Bridge?.Track(Body);
        Zone.Track(this);
    }

    // ---- Edits ------------------------------------------------------------------------------

    /// <summary>
    /// After blocks changed: rebuild the dirty chunks' colliders (hull and cabin) and meshes,
    /// refresh mass and centre of mass, rebuild the landing legs if they changed, and tell the
    /// world, so anything near is woken and checked for being stuck inside the new blocks.
    /// </summary>
    public void ApplyEdits()
    {
        if (Structure.DirtyChunks.Count == 0) return;
        var dirty = Structure.DirtyChunks.ToList();
        Structure.DirtyChunks.Clear();
        List<ChunkColliders> colliders = StructureBridge.Colliders(Structure, dirty);
        HullShapes.Apply(colliders);
        Cabin.Shapes.Apply(colliders);
        View.Rebuild(dirty);

        if (Structure.Count > 0)
        {
            MassProperties mass = StructureBridge.Mass(Structure);
            Body.Mass = mass.Mass;
            Body.CenterOfMass = mass.CenterOfMass;
            Body.Shape = StructureBridge.ShapeOf(Structure);
            Body.DragArea = StructureBridge.DragArea(Structure);
            if (Binding is FreeBinding free)
            {
                free.Rigid.Mass = mass.Mass;
                free.Rigid.CenterOfMass = mass.CenterOfMass.ToGodot();
            }
        }
        RefreshSeat();
        RefreshLegs();
        Fx.Setup(this);
        Zone.Physics.NotifyChanged(RegionOf(dirty));
    }

    /// <summary>
    /// Landing legs and hover pads belong to the vessel's controller: when they change (a leg
    /// built on), a parked vessel gets a new controller standing where it stood.
    /// </summary>
    void RefreshLegs()
    {
        if (Spec == null) return;
        string key = LegsKey();
        if (key == _legsKey) return;
        _legsKey = key;
        bool landed = Controller?.Frozen ?? true;
        Body.Controller = new VesselController(Flight, Spec.HoverPoints, Spec.Legs);
        float heading = MathX.Heading(Body.Rotation);
        if (landed) Controller.PlaceLanded(Body, Body.Position, heading);
        else Controller.Place(Body, Body.Position, heading);
    }

    string LegsKey() => Spec == null ? "" : string.Join(";", Spec.Legs.Select(l => l.Hip.ToString())) + "|" + string.Join(";", Spec.HoverPoints);

    void RefreshSeat()
    {
        Seat = null;
        foreach (var (p, b) in Structure.Blocks())
            if (Structure.TypeOf(b).IsCockpit)
            {
                Seat = StructureBridge.CellCenter(Structure, p);
                return;
            }
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

    /// <summary>Makes a body for a section just split off this structure: mass and momentum move with the blocks.</summary>
    public Vessel Sever(Structure piece)
    {
        MassProperties remaining = MassOf(Structure);
        MassProperties mass = StructureBridge.Mass(piece);
        Body wreckBody = Zone.Physics.Sever(Body, remaining.Mass, remaining.CenterOfMass, new BodySpec
        {
            Label = piece.Name,
            Mass = mass.Mass,
            CenterOfMass = mass.CenterOfMass,
            DragArea = StructureBridge.DragArea(piece),
            Shape = StructureBridge.ShapeOf(piece),
        });
        piece.DirtyChunks.Clear();
        return SpawnWreck(Zone, piece, wreckBody);
    }

    /// <summary>
    /// No hull points left: the vessel loses power and becomes a free wreck the engine tumbles.
    /// Its shapes, mesh and cabin (with anyone in it) move to a rigid body; nothing is rebuilt.
    /// </summary>
    public void BecomeWreck()
    {
        if (Binding is not HullBinding hull) return;
        var rigid = new RigidBody3D { Name = hull.Hull.Name + "Wreck", CollisionLayer = Layers.Hull, CollisionMask = Layers.WreckMask };
        Zone.AddChild(rigid);
        rigid.GlobalTransform = hull.Hull.GlobalTransform;
        HullShapes.MoveTo(rigid);
        foreach (Node child in hull.Hull.GetChildren())
            if (child is not CollisionShape3D) child.Reparent(rigid, false);
        var free = Zone.Host.Rebind(Body, new FreeBinding(rigid, Body));
        Binding = free;
        free.Teleport();
        hull.Hull.GetParent().RemoveChild(hull.Hull);
        hull.Hull.QueueFree();
    }

    // ---- Helpers ----------------------------------------------------------------------------

    /// <summary>How far a structure-frame point is from the nearest solid block, m, looking no farther than <paramref name="max"/>.</summary>
    public float DistanceFromHull(N.Vector3 local, float max)
    {
        float cell = Structure.Grid.CellSize, best = max;
        var half = new N.Vector3(cell * 0.5f);
        Int3 c = StructureBridge.CellAt(Structure, local);
        int r = (int)System.MathF.Ceiling(max / cell);
        for (int z = -r; z <= r; z++)
            for (int y = -r; y <= r; y++)
                for (int x = -r; x <= r; x++)
                {
                    var p = new Int3(c.X + x, c.Y + y, c.Z + z);
                    if (!Structure.TryGet(p, out Block b) || !Structure.IsSolid(b)) continue;
                    N.Vector3 d = N.Vector3.Max(N.Vector3.Abs(local - StructureBridge.CellCenter(Structure, p)) - half, N.Vector3.Zero);
                    best = System.MathF.Min(best, d.Length());
                }
        return best;
    }

    /// <summary>
    /// Height above the ground under the vessel, m: the gear's probe where it has terrain
    /// collision to hit, otherwise the planet's own ground height there (the probe only sees
    /// collision, which streams in around whoever is in focus).
    /// </summary>
    public float AboveGround
    {
        get
        {
            Vector3 p = Body.Position.ToGodot();
            float natural = p.Y - Zone.GroundAt(p.X, p.Z).Y;
            float probe = Controller?.GroundDistance ?? float.PositiveInfinity;
            return float.IsFinite(probe) ? MathF.Min(probe, natural) : natural;
        }
    }

    /// <summary>The structure's frame in the world, interpolated for drawing.</summary>
    public Transform3D DrawTransform => Node.GetGlobalTransformInterpolated();

    /// <summary>Storage capacity for the placeholder materials, in units.</summary>
    public int CargoCapacity => (int)StructureStats.Compute(Structure).StorageCapacity;

    /// <summary>Stores up to <paramref name="n"/> units of a material; returns how many fitted.</summary>
    public int Store(string material, int n)
    {
        int room = CargoCapacity - Cargo.Total;
        int put = System.Math.Clamp(n, 0, System.Math.Max(0, room));
        if (put > 0) Cargo.Add(new Dictionary<string, int> { [material] = put });
        return put;
    }
}
