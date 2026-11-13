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
public sealed partial class Vessel
{
    /// <summary>The zone it is in (the planet's surface, or its orbit): it changes only at a zone handover.</summary>
    public SurfaceZone Zone { get; internal set; }
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
    public bool PowerOn = true, Home, AutoTransmit;

    bool _lightsOn = true;

    /// <summary>
    /// Are the lights on? True while any light block is lit; setting it switches every light block on
    /// or off (a light's Off flag, so its glow and its real light follow), and a single light switched
    /// by hand (<see cref="ToggleLight"/>) moves it with them.
    /// </summary>
    public bool LightsOn
    {
        get => _lightsOn;
        set
        {
            _lightsOn = value;
            SetDecoLights(value);
            bool changed = false;
            foreach (var (cell, block) in Structure.Blocks().ToList())
            {
                Block b = block;
                if (!Structure.TypeOf(b).IsLight || b.Off == !value) continue;
                b.Flags ^= BlockFlags.Off;
                Structure.Set(cell, b);
                changed = true;
            }
            if (changed) ApplyEdits();
        }
    }

    /// <summary>Gives one light block (or, with no cell, every light block) new colour and animation settings; <paramref name="change"/> maps what a light has now to what it should have.</summary>
    public int StyleLights(Int3? cell, System.Func<LightSettings, LightSettings> change)
    {
        int n = 0;
        foreach (var (at, block) in Structure.Blocks().ToList())
        {
            if (!Structure.TypeOf(block).IsLight || (cell != null && at != cell.Value)) continue;
            if (Structure.SetLight(at, change(Structure.LightAt(at)))) n++;
        }
        if (n > 0) ApplyEdits();
        return n;
    }

    /// <summary>E on a light block: switches that one light. False if the cell is not a light.</summary>
    public bool ToggleLight(Int3 cell)
    {
        if (!Structure.TryGet(cell, out Block b) || !Structure.TypeOf(b).IsLight) return false;
        Building.Toggle(Structure, cell);
        ApplyEdits();
        _lightsOn = Structure.Blocks().Any(x => Structure.TypeOf(x.Item2).IsLight && !x.Item2.Off);
        return true;
    }

    /// <summary>A wreck: adrift in space, or lying on a planet with no core yet (fixed like a base, but nobody's base till claimed).</summary>
    public bool IsWreck => Body.Kind == BodyKind.Wreck || GroundWreck && Structure.Core == null;

    /// <summary>
    /// The start's bunker: an ordinary structure of ordinary blocks, flagged only so it is named
    /// and marked as the bunker. Build onto it, make it part of your first base, or take it apart
    /// for its materials (every block refunds its bill).
    /// </summary>
    public bool IsRuin { get; set; }
    /// <summary>A structure anchored in Base Mode: it never moves (shared physics' fixed body). One switched to Base Mode in flight is anchored only once it has come to rest.</summary>
    public bool IsBase => Body.Kind == BodyKind.Base;
    public HandlingStats Stats => Flight?.Stats;
    public string Name => Structure.Name;

    /// <summary>The chair in use: where its sitter rests (structure frame, metres), or null if it has none.</summary>
    public N.Vector3? Seat { get; private set; }

    /// <summary>The seated eye in that chair (structure frame, metres), from the chair's data.</summary>
    public N.Vector3 Eye { get; private set; }

    /// <summary>The chair's console top, middle of its edge on the sitter's side (structure frame, metres): the cockpit screen stands there.</summary>
    public N.Vector3 ConsoleEdge { get; private set; }

    /// <summary>How far the chair reaches out from its seat across the floor, m: getting up puts you past it.</summary>
    public float ChairReach { get; private set; }

    /// <summary>How far the seat stands above the floor its sitter's feet rest on, m (the figure's legs reach down to it).</summary>
    public float SeatDrop { get; private set; }

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
        Roles.PilotChanged += FollowPilot;
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
    public bool Powered => PowerOn && HasGenerator;

    bool _hasGenerator;
    int _generatorVersion = -1;

    /// <summary>A generator aboard, looked for again only when the structure has changed (its view asks a few times a second).</summary>
    bool HasGenerator
    {
        get
        {
            if (_generatorVersion != Structure.Version)
            {
                _generatorVersion = Structure.Version;
                _hasGenerator = Structure.Blocks().Any(b => Structure.TypeOf(b.block).IsGenerator);
            }
            return _hasGenerator;
        }
    }

    /// <summary>Runs its machines for a tick (when powered; the time scale is the tests' marked speed-up).</summary>
    public void TickMachines(double dt)
    {
        if (!Powered) return;
        foreach (Constructor c in Machines.Values) c.Tick(dt);
    }

    // ---- Spawning ---------------------------------------------------------------------------

    /// <summary>A structure as a vessel: one kinematic hull swept through the world, landed (asleep) or flying.</summary>
    /// <param name="drawLater">Its look is drawn over the next frames, within their budget (a big structure set down whole).</param>
    public static Vessel Spawn(SurfaceZone zone, Structure s, N.Vector3 at, float heading, bool landed, bool drawLater = false)
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
        body.Controller = vessel.NewController();

        var hull = new CharacterBody3D { Name = s.Name.Replace(" ", "") };
        zone.AddChild(hull);
        vessel.Binding = zone.Host.Bind(body, new HullBinding(hull));
        vessel.HullShapes = new StructureShapes(hull);
        vessel.Furnish(hull, drawLater);
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
    public static Vessel SpawnBase(SurfaceZone zone, Structure s, N.Vector3 at, float heading, bool drawLater = false)
    {
        s.Mode = CoreMode.Base;
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
        vessel.Furnish(node, drawLater);
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

    void Furnish(CollisionObject3D owner, bool drawLater = false)
    {
        List<ChunkColliders> colliders = StructureBridge.Colliders(Structure);
        // A loose body (a wreck, cut off and free) collides in every part, fittings too: a lamp cut off a wall must lie on the ground.
        List<ChunkColliders> hull = Body.Motion == GalHack.Physics.Motion.Free ? StructureBridge.Colliders(Structure, null, fittings: true) : colliders;
        if (drawLater) foreach (ChunkColliders c in hull) _pendingShapes.Enqueue((HullShapes, c));
        else HullShapes.Apply(hull);
        View = new StructureMesh { Name = "View", BeforeDraw = Hits.Flush, PowerSource = () => Powered };
        owner.AddChild(View);
        View.Setup(Structure, drawLater);
        Fx = new VesselFx { Name = "Fx" };
        owner.AddChild(Fx);
        Fx.Setup(this);
        Cabin = CabinWorld.Create("CabinWorld");
        owner.AddChild(Cabin);
        if (drawLater) foreach (ChunkColliders c in colliders) _pendingShapes.Enqueue((Cabin.Shapes, c));
        else Cabin.Shapes.Apply(colliders);
        if (Body.Core != null) Cabin.SetGravity(Body.Core);
        Structure.DirtyChunks.Clear();
    }

    /// <summary>Collision still to build, a chunk a tick, hull first then cabin (a structure set down whole builds it over a few ticks, inside the frame's budget).</summary>
    readonly Queue<(StructureShapes shapes, ChunkColliders chunk)> _pendingShapes = new();

    /// <summary>Collision chunks still to build.</summary>
    public int PendingShapes => _pendingShapes.Count;

    /// <summary>Builds the next chunk of collision still to build (each tick), or all of it now.</summary>
    public void BuildPendingShapes(bool all = false)
    {
        while (_pendingShapes.TryDequeue(out var next))
        {
            next.shapes.Apply(new[] { next.chunk });
            if (!all) break;
        }
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
        BuildPendingShapes(all: true); // never an older chunk's collision over a newer one
        var dirty = Structure.DirtyChunks.ToList();
        Structure.DirtyChunks.Clear();
        List<ChunkColliders> colliders = StructureBridge.Colliders(Structure, dirty);
        HullShapes.Apply(Body.Motion == GalHack.Physics.Motion.Free ? StructureBridge.Colliders(Structure, dirty, fittings: true) : colliders);
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
                Body.DragBox = StructureBridge.DragBox(Structure);
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
    /// Landing legs and hover plates belong to the vessel's controller: when they change (a leg
    /// built on, or the core's mode switched), it gets a new one, standing where it stood if parked.
    /// </summary>
    void RefreshLegs()
    {
        if (Spec == null) return;
        string key = LegsKey();
        if (key == _legsKey) return;
        _legsKey = key;
        VesselController old = Controller;
        Body.Controller = NewController();
        if (old?.Frozen ?? true)
        {
            N.Quaternion rotation = Body.Rotation;
            Controller.PlaceLanded(Body, Body.Position, MathX.Heading(rotation));
            Body.Rotation = rotation;
            // It stood on gear that's gone now (a pad shot off): no longer attached, it drops onto its hull.
            Controller.Gear?.Probe(Zone.Physics, Body);
            if (old?.Gear?.Attached == true && Controller.Gear?.Attached != true) Controller.Wake(Body);
        }
        else Controller.LocalSpin = old.LocalSpin; // in flight it carries on as it was: speed, attitude and spin
    }

    /// <summary>The body rules for its legs and hover plates as built now; the plates push along the command chair's down.</summary>
    VesselController NewController()
    {
        var c = new VesselController(Flight, Spec.HoverPoints, Spec.Legs);
        if (c.Hover != null) c.Hover.Down = ChairDown();
        return c;
    }

    /// <summary>The command chair's down in the structure's frame (its first chair, as the seat is), or the structure's own down.</summary>
    N.Vector3 ChairDown()
    {
        foreach (var (_, b) in Structure.Blocks())
            if (IsChair(Structure.TypeOf(b))) return N.Vector3.Normalize(Orientations.Matrix(b.Rot).Apply(-N.Vector3.UnitY));
        return -N.Vector3.UnitY;
    }

    void SitAt(Int3 anchor, in Block chair)
    {
        Seat = StructureBridge.SeatPoint(Structure, anchor, chair);
        Eye = StructureBridge.EyePoint(Structure, anchor, chair);
        BlockType type = Structure.TypeOf(chair);
        Part desk = DeskOf(type);
        // No console of its own: the chair's front edge at the seat's height.
        N.Vector3 edge = desk == null ? type.Seat with { Z = -0.5f } : new(0.5f * (desk.Min.X + desk.Max.X), desk.Max.Y, desk.Max.Z);
        ConsoleEdge = Structure.PointOf(anchor, chair, edge) * Structure.Grid.CellSize;
        Int3 size = Footprint.WorldSize(type, chair.Rot);
        ChairReach = MathF.Max(size.X, size.Z) * Structure.Grid.CellSize;
        SeatDrop = FloorUnder(Structure.TypeOf(chair), Structure.Grid.CellSize);
    }

    /// <summary>A chair's console: the dark housing its lit symbol screen sits on, or null.</summary>
    static Part DeskOf(BlockType t)
    {
        Part screen = t.Parts.FirstOrDefault(p => p.Style == PartStyle.Glow && p.Finish is { Symbol: not DeviceSymbol.None });
        if (screen == null) return null;
        const float e = 0.01f;
        return t.Parts.FirstOrDefault(p => p.Style == PartStyle.Dark && N.Vector3.Max(p.Min, screen.Min) is var lo
            && N.Vector3.Min(p.Max, screen.Max) is var hi && lo.X <= hi.X + e && lo.Y <= hi.Y + e && lo.Z <= hi.Z + e);
    }

    /// <summary>Where a seated person's feet come down, m ahead of the seat point: past the seat's front edge, short of a console.</summary>
    const float FeetAhead = 0.4f;

    /// <summary>
    /// How far a chair's seat stands above what its sitter's feet rest on, m: the highest of its own
    /// parts under the feet and below the seat (a cockpit's floor), or the bottom of its box (an open
    /// chair stands on the block under it). The chair faces -z in its own frame.
    /// </summary>
    static float FloorUnder(BlockType t, float cell)
    {
        N.Vector3 size = t.Size.ToVector3() * cell, seat = t.Seat;
        float z = seat.Z - FeetAhead / size.Z, floor = -0.5f;
        foreach (Part p in t.Parts)
            if (!p.ClosedOnly && p.Max.Y < seat.Y - 0.02f && p.Min.X <= seat.X && p.Max.X >= seat.X && p.Min.Z <= z && p.Max.Z >= z)
                floor = MathF.Max(floor, p.Max.Y);
        return (seat.Y - floor) * size.Y;
    }

    string LegsKey() => Spec == null ? "" : string.Join(";", Spec.Legs.Select(l => l.Hip.ToString())) + "|" + string.Join(";", Spec.HoverPoints);

    void RefreshSeat()
    {
        if (_chair is Int3 taken && Structure.TryGetBlock(taken, out Int3 anchor, out Block held) && IsChair(Structure.TypeOf(held)))
        {
            SitAt(anchor, held);
            return;
        }
        foreach (var (p, b) in Structure.Blocks())
            if (IsChair(Structure.TypeOf(b)))
            {
                SitAt(p, b);
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
            DragBox = StructureBridge.DragBox(piece), // loose: the wind meets its real shape as it lies
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
        HullShapes.Apply(StructureBridge.Colliders(Structure, null, fittings: true)); // free now: its fittings collide too
        foreach (Node child in hull.Hull.GetChildren())
            if (child is not CollisionShape3D) child.Reparent(rigid, false);
        var free = Zone.Host.Rebind(Body, new FreeBinding(rigid, Body));
        Binding = free;
        Body.DragBox = StructureBridge.DragBox(Structure);
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
