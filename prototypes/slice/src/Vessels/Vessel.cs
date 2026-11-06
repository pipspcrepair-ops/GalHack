using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Items;
using GalHack.Items.Bridge;
using GalHack.Items.Production;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;
using Slice.Items;
using Slice.Surface;
using N = System.Numerics;
using HandlingStats = ShipHandling.Handling.HandlingStats;
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

    /// <summary>Where damage lands on its blocks, and how they're repaired.</summary>
    public VesselDamage Hits { get; }

    /// <summary>The cockpit is gone: whoever sat in it gets up, while <see cref="Seat"/> still says where it was.</summary>
    public event Action SeatLost;

    /// <summary>
    /// The structure's one shared storage (shared/Items): every container on it adds room, every
    /// machine on it takes its inputs from it and puts its products back, and building on it can
    /// pay from it. <see cref="StorageGrid"/> is how it shows in the inventory view.
    /// </summary>
    public SharedStorage Storage { get; private set; }
    public ItemGrid StorageGrid { get; private set; }

    /// <summary>Its production machines (constructors), one per machine block, by cell.</summary>
    public readonly SortedDictionary<Int3, Constructor> Machines = new(Comparer<Int3>.Create((a, b) => (a.X, a.Y, a.Z).CompareTo((b.X, b.Y, b.Z))));

    /// <summary>
    /// A vessel being built holds where its core was set up (its build stand) until someone first
    /// flies it: edits wake nearby bodies, and this keeps it from dropping to the ground meanwhile.
    /// </summary>
    public N.Vector3? Stand;

    /// <summary>Holds a vessel on its stand (called every tick until it is first flown).</summary>
    public void HoldOnStand()
    {
        if (Stand is not N.Vector3 at || Controller is not VesselController c) return;
        if (c.Frozen && N.Vector3.DistanceSquared(Body.Position, at) < 1e-6f) return;
        c.PlaceLanded(Body, at, MathX.Heading(Body.Rotation));
        Binding.Teleport();
    }

    /// <summary>Settings the Core tab changes: power to its devices, its lights, Home, auto-transmit.</summary>
    public bool PowerOn = true, LightsOn = true, Home, AutoTransmit;

    public bool IsWreck => Body.Kind == BodyKind.Wreck;

    /// <summary>
    /// The start's bunker: an ordinary structure of ordinary blocks, flagged only so it is named
    /// and marked as the bunker. Build onto it, make it part of your first base, or take it apart
    /// for its materials (every block refunds its bill).
    /// </summary>
    public bool IsRuin { get; set; }
    /// <summary>A structure in base mode, anchored where it was placed: never moves (shared physics' fixed body).</summary>
    public bool IsBase => Body.Kind == BodyKind.Base;
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
        Hits = new VesselDamage(this);
        body.DamageSink = Hits;
        if (body.Kind == BodyKind.Vessel)
        {
            Spec = new StructureVessel(structure, zone.Data.Handling);
            Flight = new HandlingFlight(Spec, zone.Data.Handling);
        }
        Storage = new SharedStorage(zone.Data.Items, structure.Name, BuildingBridge.StorageCapacity(structure));
        StorageGrid = new ItemGrid(Storage, 8, 6, growable: true);
        RefreshSeat();
        RefreshMachines();
    }

    /// <summary>A constructor for every machine block on it (BuildingBridge.MachinesOn), each working from its shared storage. Kept across edits.</summary>
    void RefreshMachines()
    {
        var present = new HashSet<Int3>();
        foreach (var (cell, def) in BuildingBridge.MachinesOn(Zone.Data.Items, Structure))
        {
            if (def.Kind != MachineKind.Constructor) continue;
            present.Add(cell);
            if (!Machines.ContainsKey(cell)) Machines[cell] = new Constructor(Zone.Data.Items, def, Storage, Zone.WorldRules);
        }
        foreach (Int3 gone in Machines.Keys.Where(c => !present.Contains(c)).ToList())
        {
            // A machine taken down: its run in progress and tray go back to storage (nothing lost).
            Constructor c = Machines[gone];
            foreach (Job j in c.Orders.ToList()) c.Cancel(j);
            Storage.AddAll(c.Tray);
            Machines.Remove(gone);
        }
    }

    /// <summary>Power to its machines: a generator on it, and its power switched on (a structure with no generator has none).</summary>
    public bool Powered => PowerOn && Structure.Blocks().Any(b => Structure.TypeOf(b.block).IsGenerator);

    /// <summary>Runs its machines for a tick (when powered; the time scale is the tests' marked speed-up).</summary>
    public void TickMachines(double dt)
    {
        if (!Powered) return;
        foreach (Constructor c in Machines.Values) c.Tick(dt);
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

    /// <summary>
    /// A structure in base mode set down on the ground: one fixed body (shared physics' base:
    /// nothing ever moves it, no gravity, wind or rescue), walked on in the zone's own world.
    /// </summary>
    public static Vessel SpawnBase(SurfaceZone zone, Structure s, N.Vector3 at, float heading)
    {
        MassProperties mass = MassOf(s);
        Body body = zone.Physics.Register(new BodySpec
        {
            Kind = BodyKind.Base,
            Motion = GalHack.Physics.Motion.Fixed,
            Label = s.Name,
            Mass = mass.Mass,
            CenterOfMass = mass.CenterOfMass,
            DragArea = 1f,
            Integrity = HullPoints(zone, mass.Mass), // a base's blocks are as tough as a ship's
            Shape = StructureBridge.ShapeOf(s),
            Position = at,
            Rotation = MathX.Yaw(heading),
        });
        body.Core = new CoreGravity { On = s.GravityOn, Strength = zone.Physics.Zone.Strength };
        var vessel = new Vessel(zone, s, body);
        var node = new StaticBody3D { Name = s.Name.Replace(" ", "") };
        zone.AddChild(node);
        vessel.Binding = zone.Host.Bind(body, new FixedBinding(node));
        vessel.HullShapes = new StructureShapes(node);
        vessel.Furnish(node);
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

    /// <summary>Hull points for this much structure, whatever it is: blocks are blocks on a ship, a base or a wreck.</summary>
    static float HullPoints(SurfaceZone zone, float massKg) => Impacts.Integrity(BodyKind.Vessel, massKg, zone.Physics.Rules.Integrity);

    static MassProperties MassOf(Structure s) =>
        s.Count > 0 ? StructureBridge.Mass(s) : new MassProperties(100f, N.Vector3.Zero);

    void Furnish(CollisionObject3D owner)
    {
        List<ChunkColliders> colliders = StructureBridge.Colliders(Structure);
        HullShapes.Apply(colliders);
        View = new StructureMesh { Name = "View", BeforeDraw = Hits.Flush };
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
        Zone.Weather.Happened += Hits.OnWeather;
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
            // Hull points follow what was built (a vessel starts as one core), and don't shrink as it's shot away.
            Body.Integrity = MathF.Max(Body.Integrity, HullPoints(Zone, mass.Mass));
            if (Binding is FreeBinding free)
            {
                free.Rigid.Mass = mass.Mass;
                free.Rigid.CenterOfMass = mass.CenterOfMass.ToGodot();
            }
        }
        RefreshSeat();
        RefreshLegs();
        RefreshMachines();
        Fx.Setup(this);
        Zone.Physics.NotifyChanged(RegionOf(dirty));
    }

    /// <summary>
    /// A landed vessel rests on its lowest point (its legs' feet, or its lowest block) on the
    /// highest ground under it: so one being built stands on the ground as it grows, instead of
    /// hanging where its finished legs will one day reach.
    /// </summary>
    public void Settle()
    {
        if (Controller is not VesselController c || !c.Frozen || IsBase || Structure.Count == 0) return;
        float feet = StructureBridge.ShapeOf(Structure).FeetBelowOrigin;
        if (Spec != null)
            foreach (ShipHandling.Handling.GearSpec g in Spec.LandingGear) feet = MathF.Max(feet, -g.Position.Y + g.Length);
        var (min, max) = Structure.Bounds();
        float cell = Structure.Grid.CellSize, top = float.MinValue;
        Basis basis = new(Body.Rotation.ToGodot());
        for (int i = 0; i < 9; i++)
        {
            var corner = new Vector3(i % 3 == 0 ? min.X : i % 3 == 1 ? (min.X + max.X) * 0.5f : max.X, 0, i / 3 == 0 ? min.Z : i / 3 == 1 ? (min.Z + max.Z) * 0.5f : max.Z) * cell;
            Vector3 p = Body.Position.ToGodot() + basis * corner;
            top = MathF.Max(top, Zone.GroundAt(p.X, p.Z).Y);
        }
        float y = top + feet + 0.02f;
        if (MathF.Abs(y - Body.Position.Y) < 0.01f) return;
        c.PlaceLanded(Body, new N.Vector3(Body.Position.X, y, Body.Position.Z), MathX.Heading(Body.Rotation));
        Binding.Teleport();
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
        foreach (var (p, b) in Structure.Blocks())
            if (Structure.TypeOf(b).IsCockpit)
            {
                Seat = StructureBridge.CellCenter(Structure, p);
                return;
            }
        if (Seat != null) SeatLost?.Invoke();
        Seat = null;
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

    /// <summary>The structure's stats now (mass, power, CPU, storage), building's own numbers.</summary>
    public StructureStats StructureStats => StructureStats.Compute(Structure);

    /// <summary>A structure-local point in the world.</summary>
    public Vector3 ToWorld(N.Vector3 local) => Body.ToParent(local).ToGodot();
}
