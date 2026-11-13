using System;
using System.Collections.Generic;
using System.Linq;
using CapitalBase.Core;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using GalHack.Structures.Presentation;
using Godot;
using PhysicsSandbox.Bridges;
using ShipHandling.Handling;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;

namespace CapitalBase.World;

/// <summary>What a craft is doing, as its console shows it.</summary>
public enum CraftState
{
    /// <summary>Flown by the handling model (or holding station with nobody at the controls).</summary>
    Flying,

    /// <summary>Settled on its gear (or its own blocks) and not simulating, until thrust wakes it.</summary>
    Landed,

    /// <summary>Base mode: locked where it stands. It can't fly and no wind moves it.</summary>
    Anchored,

    /// <summary>Capital mode, but still keyed into the ground it was built into: lift-off cuts it free.</summary>
    Keyed,

    /// <summary>Parked on a host structure: rides with it, sheltered from the wind.</summary>
    Docked,
}

/// <summary>
/// One of building's structures as a body in the shared physics world: one kinematic hull swept
/// by the handling model (physics-sandbox's bridges), its per-chunk colliders, building's own
/// block mesher for its look, landing legs, exhaust and its shared storage.
///
/// The same object is a capital ship or a base: the core's mode decides whether the handling
/// model has any thrust, and a <see cref="Pinned"/> controller stands in for the vessel controller
/// while it is anchored or parked. No block ever changes when it switches.
/// </summary>
public sealed class Craft : ILiftHull
{
    Body ILiftHull.Body => Body;
    HandlingFlight ILiftHull.Flight => Flight;
    void ILiftHull.LetGoOfGround() { if (Pin is { Docked: false }) Release(); }

    public readonly CapitalWorld World;

    /// <summary>The save record's id: lower case, digits and dashes.</summary>
    public readonly string Id;
    public readonly Structure Structure;
    public readonly Body Body;
    public readonly StructureVessel Spec;
    public readonly HandlingFlight Flight;
    public readonly VesselController Vessel;
    public readonly StructureField Field;
    public readonly HullBinding Binding;
    public readonly StructureShapes Shapes;
    public readonly Cargo Cargo = new();

    public Pinned Pin { get; private set; }

    /// <summary>A harvesting vessel's switch: farmed items go straight to Home while in range.</summary>
    public bool AutoTransmit;

    /// <summary>The last pass of its transmitter (harvesters only).</summary>
    public LinkReport Link;

    CraftView _view;
    int _statsVersion = -1;
    StructureStats _stats;
    float _restingFor, _heldFor, _lastY;

    public Craft(CapitalWorld world, string id, Structure structure, N.Vector3 position, N.Quaternion rotation)
    {
        World = world;
        Id = id;
        Structure = structure;
        MassProperties mass = StructureBridge.Mass(structure);
        Body = world.Physics.Register(new BodySpec
        {
            Kind = BodyKind.Vessel,
            Motion = GalHack.Physics.Motion.Kinematic,
            Label = structure.Name,
            Mass = mass.Mass,
            CenterOfMass = mass.CenterOfMass,
            DragArea = StructureBridge.DragArea(structure),
            Shape = StructureBridge.ShapeOf(structure),
            Position = position,
            Rotation = rotation,
        });
        Body.Core = new CoreGravity { On = structure.GravityOn, Strength = world.Physics.Zone.Strength };
        Spec = new StructureVessel(structure, world.Handling);
        Flight = new HandlingFlight(Spec, world.Handling) { Input = PilotInput.LetGo };
        Vessel = new VesselController(Flight, Spec.HoverPoints, Spec.Legs);
        Body.Controller = Vessel;
        Field = new StructureField(Body, structure);

        var hull = new CharacterBody3D { Name = Id };
        world.AddChild(hull);
        Binding = world.Host.Bind(Body, new HullBinding(hull));
        Shapes = new StructureShapes(hull);
        Shapes.Apply(StructureBridge.Colliders(structure));
        if (world.Visuals)
        {
            _view = new CraftView { Name = "View" };
            hull.AddChild(_view);
            _view.Setup(this);
        }
        structure.DirtyChunks.Clear();
        world.Physics.StuckFields.Add(Field);
        Vessel.Place(Body, position, 0f);
        Body.Rotation = rotation;
        Binding.Teleport();
    }

    public CharacterBody3D Hull => Binding.Hull;
    public bool IsBase => Structure.Mode == CoreMode.Base;
    public string Name => Structure.Name;

    public CraftState State
    {
        get
        {
            if (Pin != null) return Pin.Docked ? CraftState.Docked : IsBase ? CraftState.Anchored : CraftState.Keyed;
            return Vessel.Frozen || Vessel.Settling ? CraftState.Landed : CraftState.Flying;
        }
    }

    // ---- Stats -----------------------------------------------------------------------------

    /// <summary>Building's stats (mass, power, storage, life support), recomputed when the blocks or cargo change.</summary>
    public StructureStats Stats
    {
        get
        {
            if (_stats == null || _statsVersion != Structure.Version)
            {
                _stats = StructureStats.Compute(Structure);
                _statsVersion = Structure.Version;
            }
            return _stats;
        }
    }

    public double StorageCapacity => Stats.StorageCapacity;
    public double FreeSu => Cargo.FreeSu(World.Rules.Catalog, StorageCapacity);

    /// <summary>Mass in tonnes, cargo included.</summary>
    public double Tonnes => Stats.Mass;

    /// <summary>
    /// Cargo changed: building's storage reads it as SU (for mass), and its stats and the handling
    /// numbers refresh on the structure's version. Bump the version without dirtying any chunk, so
    /// nothing is remeshed for it.
    /// </summary>
    public void CargoChanged()
    {
        Structure.CargoSu = Cargo.UsedSu(World.Rules.Catalog);
        if (Structure.Core is not GalHack.Structures.Int3 core) return;
        var dirty = new HashSet<GalHack.Structures.Int3>(Structure.DirtyChunks);
        Structure.MarkDirty(core);
        Structure.DirtyChunks.Clear();
        Structure.DirtyChunks.UnionWith(dirty);
        RefreshMass();
    }

    void RefreshMass()
    {
        if (Structure.Count == 0) return;
        MassProperties mass = StructureBridge.Mass(Structure);
        Body.Mass = mass.Mass;
        Body.CenterOfMass = mass.CenterOfMass;
        Body.Shape = StructureBridge.ShapeOf(Structure);
        Body.DragArea = StructureBridge.DragArea(Structure);
    }

    // ---- Edits -----------------------------------------------------------------------------

    /// <summary>After blocks changed: rebuild only the dirty chunks' colliders and meshes, and the mass.</summary>
    public void ApplyEdits()
    {
        if (Structure.DirtyChunks.Count == 0) return;
        var dirty = Structure.DirtyChunks.ToList();
        Structure.DirtyChunks.Clear();
        Shapes.Apply(StructureBridge.Colliders(Structure, dirty));
        _view?.Rebuild(dirty);
        RefreshMass();
        World.Physics.NotifyChanged(Region.Around(Body.CenterOfMassPosition, 1f));
    }

    /// <summary>A cell's box in the world, as the cut-free planner and the build tool see it.</summary>
    public BlockBox BoxOf(GalHack.Structures.Int3 cell)
    {
        float size = Structure.Grid.CellSize;
        N.Vector3 local = StructureBridge.CellCenter(Structure, cell);
        return new BlockBox(Body.ToParent(local), Body.Rotation, new N.Vector3(size * 0.5f), local.Length());
    }

    /// <summary>Every block's box (components and slopes count as whole cells).</summary>
    public IEnumerable<BlockBox> Boxes()
    {
        foreach (var (p, _) in Structure.Cells()) yield return BoxOf(p);
    }

    /// <summary>How far the hull is tilted from level, radians.</summary>
    public float Tilt => MathX.Angle(N.Vector3.Transform(N.Vector3.UnitY, Body.Rotation), N.Vector3.UnitY);

    // ---- Pinning ---------------------------------------------------------------------------

    /// <summary>Locks the craft where it is (a base, or a hull keyed into the ground).</summary>
    public void Anchor() => SetPin(Pinned.Anchor(Body, Placed));

    public void AnchorAt(N.Vector3 position, N.Quaternion rotation) => SetPin(Pinned.AnchorAt(position, rotation, Placed));

    /// <summary>Parks on a host: from now on it rides the host exactly.</summary>
    public void DockOn(Craft host) => SetPin(Pinned.Dock(Body, host.Body, Placed));

    public void DockAt(Craft host, N.Vector3 local, N.Quaternion localRotation) => SetPin(Pinned.DockAt(host.Body, local, localRotation, Placed));

    void SetPin(Pinned pin)
    {
        Pin = pin;
        Body.Controller = pin;
        pin.Hold(Body);
        _restingFor = 0f;
    }

    /// <summary>
    /// Hands the craft back to its vessel controller, landed where it stands: it stays put until
    /// thrust (or a knock) wakes it, exactly like a ship that has just settled.
    /// </summary>
    public void Release()
    {
        if (Pin == null) return;
        N.Vector3 velocity = Body.Velocity;
        Pin = null;
        Body.Controller = Vessel;
        PlaceLanded(Body.Position, Body.Rotation);
        Body.Velocity = velocity;
    }

    /// <summary>Settled at a pose, gear down, not simulating.</summary>
    public void PlaceLanded(N.Vector3 position, N.Quaternion rotation)
    {
        Vessel.PlaceLanded(Body, position, MathX.Heading(rotation));
        Body.Rotation = rotation;
        Binding.Teleport();
    }

    /// <summary>Flying at a pose and velocity (a saved flight, or a test's start).</summary>
    public void PlaceFlying(N.Vector3 position, N.Quaternion rotation, N.Vector3 velocity)
    {
        Vessel.Place(Body, position, MathX.Heading(rotation));
        Body.Rotation = rotation;
        Body.Velocity = velocity;
        Flight.State.Velocity = velocity.ToGodot();
        Binding.Teleport();
    }

    /// <summary>The pinned pose was just applied: put the node there (interpolated, not snapped).</summary>
    void Placed(Body body) => Hull.GlobalTransform = GodotMath.TransformOf(body);

    /// <summary>
    /// How far the lowest block bottoms are above the ground under them, m (by the terrain's own
    /// cells). Measured only while flying slowly near the ground; infinite otherwise.
    /// </summary>
    public float HullClearance { get; private set; } = float.PositiveInfinity;

    /// <summary>Within this of the ground, a hull counts as touching it (the gear's own rule for a foot).</summary>
    public const float TouchClearance = 0.25f;

    /// <summary>
    /// After each physics tick: a ship that has come down onto its own blocks rather than its gear
    /// (a foundation it was given as a base reaches lower than its legs) settles there too, by the
    /// gear's rule: slow, nobody on the stick, a hand's width off the ground for half a second.
    /// So it can land anywhere, and then become a base there.
    /// </summary>
    public bool CheckRestingOnHull(float dt)
    {
        bool near = Pin == null && !Vessel.Frozen && !Vessel.Settling && !Body.Asleep
                    && Body.Velocity.Length() < 6f && (Vessel.Gear == null || Vessel.Gear.FootAltitude < 6f);
        HullClearance = near ? MeasureClearance() : float.PositiveInfinity;

        // Asked to come down near the ground and not moving: something under the hull holds it.
        bool held = near && HullClearance < 1f && Body.Velocity.Y < -0.2f && MathF.Abs(Body.Position.Y - _lastY) < 0.1f * MathF.Abs(Body.Velocity.Y) * dt;
        _heldFor = held ? _heldFor + dt : 0f;
        _lastY = Body.Position.Y;
        if (_heldFor > 0.3f) HullClearance = 0f;

        bool idle = !Flight.Seated || !Flight.AnyThrust;
        bool resting = near && idle && (Body.OnFloor || HullClearance < TouchClearance)
                       && Body.Velocity.Length() < World.PhysicsRules.Gear.LandSpeed && !(Vessel.Gear is { Contact: true });
        _restingFor = resting ? _restingFor + dt : 0f;
        if (_restingFor < 0.5f) return false;
        _restingFor = 0f;
        Flight.Halt();
        N.Vector3 down = float.IsFinite(HullClearance) ? N.Vector3.UnitY * MathF.Max(0f, HullClearance - 0.02f) : N.Vector3.Zero;
        PlaceLanded(Body.Position - down, Body.Rotation);
        return true;
    }

    /// <summary>
    /// The smallest gap under the hull's lowest faces, by the terrain's density field (the same
    /// zero surface its mesh and colliders are drawn from), looked for a metre down.
    /// </summary>
    float MeasureClearance()
    {
        const float Step = 0.05f, Reach = 1f;
        float half = Structure.Grid.CellSize * 0.5f, inset = half * 0.95f, best = float.PositiveInfinity;
        foreach (var (p, b) in Structure.Cells())
        {
            if (!Structure.IsSolid(b) || Structure.Has(new GalHack.Structures.Int3(p.X, p.Y - 1, p.Z))) continue;
            N.Vector3 centre = StructureBridge.CellCenter(Structure, p) - N.Vector3.UnitY * half;
            // The middle and the four corners of the face: a hull can rest on a corner on a slope.
            for (int k = 0; k < 5; k++)
            {
                N.Vector3 at = k == 0 ? centre : centre + new N.Vector3((k & 1) == 0 ? -inset : inset, 0f, k < 3 ? -inset : inset);
                N.Vector3 bottom = Body.ToParent(at);
                for (float d = 0f; d < MathF.Min(best, Reach); d += Step)
                    if (World.Ground.Store.SampleDensity(bottom - N.Vector3.UnitY * d) > 0f)
                    {
                        best = d;
                        break;
                    }
            }
        }
        return best;
    }

    // ---- Presentation ---------------------------------------------------------------------

    public void RefreshView() => _view?.Refresh();

    /// <summary>Takes the craft out of the world (only loading a save does this: it is rebuilt from its record).</summary>
    public void Discard()
    {
        World.Physics.StuckFields.Remove(Field);
        World.Physics.Unregister(Body);
        Hull.GetParent()?.RemoveChild(Hull);
        Hull.QueueFree();
    }

    public override string ToString() => $"{Name} ({Id})";
}

/// <summary>
/// A craft's look: building's block mesher per 16³ chunk (paint, patterns, stencils and grime),
/// its lights, its landing legs at their extension and an exhaust flame on every thruster that is
/// pushing. Presentation only.
/// </summary>
public partial class CraftView : Node3D
{
    Craft _craft;
    readonly Dictionary<GalHack.Structures.Int3, MeshInstance3D> _chunks = new();
    readonly List<(MeshInstance3D strut, MeshInstance3D foot)> _legs = new();
    readonly List<(MeshInstance3D flame, N.Vector3 push, N.Vector3 exhaust)> _flames = new();
    readonly List<OmniLight3D> _lights = new();
    static StandardMaterial3D _dark, _pad, _flame;

    public void Setup(Craft craft)
    {
        _craft = craft;
        _dark ??= new StandardMaterial3D { AlbedoColor = new Color("2b2d42"), Roughness = 0.5f, Metallic = 0.4f };
        _pad ??= new StandardMaterial3D { AlbedoColor = new Color("f2a516"), Roughness = 0.7f };
        _flame ??= new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.62f, 0.25f, 0.85f),
            EmissionEnabled = true,
            Emission = new Color(1f, 0.55f, 0.18f),
            EmissionEnergyMultiplier = 4f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        foreach (GalHack.Structures.Int3 c in craft.Structure.Chunks.Keys) RebuildChunk(c);
        RebuildFittings();
        Refresh();
    }

    public void Rebuild(IEnumerable<GalHack.Structures.Int3> chunks)
    {
        foreach (GalHack.Structures.Int3 c in chunks) RebuildChunk(c);
        RebuildFittings();
    }

    void RebuildChunk(GalHack.Structures.Int3 chunk)
    {
        if (!_chunks.TryGetValue(chunk, out MeshInstance3D mi))
        {
            mi = new MeshInstance3D { Name = $"Chunk {chunk.X} {chunk.Y} {chunk.Z}" };
            AddChild(mi);
            _chunks[chunk] = mi;
        }
        mi.Mesh = _craft.Structure.Chunks.ContainsKey(chunk) ? ChunkMesher.ToMesh(ChunkMesher.Build(_craft.Structure, chunk)) : null;
    }

    /// <summary>Legs, flames and lights follow the blocks.</summary>
    void RebuildFittings()
    {
        foreach (var (strut, foot) in _legs) { strut.QueueFree(); foot.QueueFree(); }
        foreach (var (f, _, _) in _flames) f.QueueFree();
        foreach (OmniLight3D l in _lights) l.QueueFree();
        _legs.Clear();
        _flames.Clear();
        _lights.Clear();

        Structure s = _craft.Structure;
        float cell = s.Grid.CellSize;
        LandingGear gear = _craft.Vessel.Gear;
        float width = cell * 0.09f;
        for (int i = 0; i < (gear?.Count ?? 0); i++)
        {
            var strut = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = width, BottomRadius = width, Height = 1f }, MaterialOverride = _dark };
            var foot = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = width * 2.4f, BottomRadius = width * 3.2f, Height = cell * 0.08f }, MaterialOverride = _pad };
            AddChild(strut);
            AddChild(foot);
            _legs.Add((strut, foot));
        }

        foreach (var (p, b) in s.Blocks())
        {
            BlockType t = s.TypeOf(b);
            N.Vector3 centre = StructureBridge.BlockCenter(s, p, b);
            if (t.IsThruster)
            {
                N.Vector3 push = Orientations.ToWorld(b.Rot, Dir.NegZ).Normal();
                var flame = new MeshInstance3D
                {
                    Mesh = new CylinderMesh { TopRadius = cell * 0.3f, BottomRadius = cell * 0.04f, Height = 1f, RadialSegments = 10 },
                    MaterialOverride = _flame,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    Visible = false,
                };
                // The flame hangs out of the exhaust face, opposite the push.
                AddChild(flame);
                _flames.Add((flame, push, centre - push * (cell * 0.5f)));
            }
            else if (t.IsLight && !b.Off && _lights.Count < 8)
            {
                var light = new OmniLight3D { Position = centre.ToGodot(), LightColor = new Color("ffe2b0"), LightEnergy = 1.4f, OmniRange = cell * 5f };
                AddChild(light);
                _lights.Add(light);
            }
        }
    }

    /// <summary>Legs at their extension (down to the ground under them when it's within reach); flames by thrust.</summary>
    public void Refresh()
    {
        if (_craft == null) return;
        LandingGear gear = _craft.Vessel.Gear;
        Body body = _craft.Body;
        for (int i = 0; i < _legs.Count && gear != null; i++)
        {
            GearLeg leg = gear.Leg(i);
            float reach = Mathf.Max(0.05f, leg.Length * gear.Extension);
            if (gear.Extension > 0.99f && gear.FootFound[i])
            {
                float drop = body.ToParent(leg.Hip).Y - gear.FootGround[i].Y;
                if (drop > 0f && drop < leg.Length + 1f) reach = Mathf.Max(0.05f, drop); // the foot telescopes onto its own ground
            }
            Vector3 hip = leg.Hip.ToGodot();
            var (strut, foot) = _legs[i];
            strut.Position = hip + Vector3.Down * (reach * 0.5f);
            strut.Scale = new Vector3(1f, reach, 1f);
            foot.Position = hip + Vector3.Down * reach;
            strut.Visible = foot.Visible = gear.Extension > 0.05f;
        }

        // Thrust as the ship feels it, in its own frame: each engine burns by its share.
        bool burning = !body.Asleep && _craft.Pin == null;
        N.Vector3 thrust = burning ? MathX.Unrotate(body.Rotation, body.LastForce(ForceKind.Thrust)) / body.Mass : N.Vector3.Zero;
        float cell = _craft.Structure.Grid.CellSize;
        foreach (var (flame, push, exhaust) in _flames)
        {
            float share = Mathf.Clamp(N.Vector3.Dot(thrust, push) / 6f, 0f, 1f);
            flame.Visible = share > 0.03f;
            if (!flame.Visible) continue;
            float length = cell * (0.6f + 2.2f * share);
            flame.Basis = Along(push.ToGodot(), length);
            flame.Position = (exhaust - push * (length * 0.5f)).ToGodot();
        }
    }

    /// <summary>A basis whose Y axis points along <paramref name="y"/>, stretched to a length (the flame's wide end toward the hull).</summary>
    static Basis Along(Vector3 y, float length)
    {
        Vector3 x = Mathf.Abs(y.X) < 0.9f ? Vector3.Right : Vector3.Forward;
        x = (x - y * x.Dot(y)).Normalized();
        return new Basis(x, y * length, x.Cross(y));
    }
}
