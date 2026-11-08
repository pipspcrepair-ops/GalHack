using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Items.Bridge;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;
using ShipHandling.Handling;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;

namespace Slice.Vessels;

/// <summary>
/// What a mode needs fitted, for the console's warning. A warning is all it is: nothing refuses a
/// mode, and a flying ship switched to a mode it isn't fitted for simply loses lift.
/// </summary>
public static class ModeFit
{
    public static string Name(CoreMode mode) => mode switch
    {
        CoreMode.Base => "BASE MODE",
        CoreMode.Capital => "CAPITAL MODE",
        CoreMode.Hover => "HOVER MODE",
        _ => "VESSEL MODE",
    };

    /// <summary>The modes a structure's core has: Base and Capital on a Big Core, Hover and Vessel on a Small Core.</summary>
    public static CoreMode[] ModesOf(Structure s) =>
        BuildingBridge.IsSmall(s) ? new[] { CoreMode.Hover, CoreMode.Vessel } : new[] { CoreMode.Base, CoreMode.Capital };

    /// <summary>What's missing for a mode to hold a structure up and move it, or nothing. Base Mode needs nothing.</summary>
    public static List<string> Missing(Structure s, CoreMode mode, Tunables tunables, float gravity)
    {
        var missing = new List<string>();
        if (mode == CoreMode.Base) return missing;
        var spec = new StructureVessel(s, tunables, mode);
        HandlingStats stats = HandlingModel.Compute(spec, new FlightConditions(gravity, true), tunables);
        bool powered = StructureStats.Compute(s).Powered;
        if (!s.Blocks().Any(b => s.TypeOf(b.block).IsCommandChair)) missing.Add("command chair");
        if (mode == CoreMode.Hover)
        {
            if (spec.HoverEngines.Count == 0) missing.Add("hover plates");
            else if (!stats.CanLiftOff) missing.Add($"{-stats.LiftMargin:F0} kN more hover lift");
        }
        if (spec.Thrusters.Count == 0) missing.Add("thrusters");
        else if (!powered) missing.Add("generator");
        else if (mode != CoreMode.Hover && !stats.CanLiftOff) missing.Add($"{-stats.LiftMargin:F0} kN more upward thrust");
        return missing;
    }

    /// <summary>The console's line for what's missing, or null when nothing is.</summary>
    public static string Warning(CoreMode mode, List<string> missing) =>
        missing.Count == 0 ? null : $"{Name(mode)} NEEDS: {string.Join(", ", missing).ToUpperInvariant()}";
}

/// <summary>
/// A core's mode on the body in the world. One value changes and no block does: a Big Core's
/// structure anchors (Base Mode: a fixed body, walked on in the zone's world, nothing moves it) or
/// flies (Capital Mode: the same hull as any vessel, on the flight model); a Small Core's craft rides
/// its hover plates or flies on its thrusters. Nothing refuses a switch. A flying Big Core set to
/// Base Mode cuts its engines, falls until it lands and anchors there; in space it comes to rest and
/// anchors as a station.
/// </summary>
public sealed partial class Vessel
{
    /// <summary>What a mode lacks on this structure, here (empty: nothing).</summary>
    public List<string> MissingFor(CoreMode mode) => ModeFit.Missing(Structure, mode, Zone.Data.Handling, Zone.Physics.Zone.Strength);

    /// <summary>The console's warning for the mode it is in, or null: worked out again only when the build or the mode changes.</summary>
    public string ModeWarning
    {
        get
        {
            var key = (Structure.Version, Structure.Mode);
            if (key != _warned)
            {
                _warned = key;
                _warning = ModeFit.Warning(Structure.Mode, MissingFor(Structure.Mode));
            }
            return _warning;
        }
    }

    (int, CoreMode) _warned = (-1, CoreMode.Base);
    string _warning;

    /// <summary>In Base Mode but not anchored yet: still falling or coming to rest.</summary>
    public bool Unanchored => Structure.Mode == CoreMode.Base && !IsBase;

    /// <summary>Base Mode's cells of ground are still held off its faces: let go once it has lifted away.</summary>
    N.Vector3? _liftedFrom;

    /// <summary>The metres a hull rises off its footings before the ground it held is let go.</summary>
    const float LetGoAfter = 1f;

    N.Vector3? _lies;
    int _refused;

    /// <summary>Changes the core's mode, whatever the structure is doing. Every block stays exactly as it was.</summary>
    public void SwitchMode(CoreMode mode)
    {
        if (Structure.Core == null || Structure.Mode == mode) return;
        // The look doesn't change, so nothing is remeshed and nothing nearby is woken.
        var dirty = new HashSet<Int3>(Structure.DirtyChunks);
        Building.SetCoreMode(Structure, mode);
        Structure.DirtyChunks.Clear();
        Structure.DirtyChunks.UnionWith(dirty);
        if (IsBase && mode != CoreMode.Base) TakeFlight();
        if (Spec != null) Body.Class = StructureBridge.ClassOf(Spec);
        RefreshLegs();
        TickMode();
    }

    /// <summary>Each tick: the next chunk of collision still to build, if any; a Big Core in Base Mode anchors once it has come to rest, and a hull that has left its footings lets go of them.</summary>
    public void TickMode()
    {
        BuildPendingShapes();
        // A claimed wreck lies where it came down until it lifts off: nothing settles it on legs it hasn't got.
        if (_lies is N.Vector3 lies)
        {
            if (Lifting != null) _lies = null;
            else if (Flight != null && Flight.Input.Seated && Flight.Input.Move.Y > 0.1f && _refused == 0)
            {
                // Its pilot asks it up: the ground it lies in is cut along the straight climb and it rises, or the console says why not.
                CapitalBase.Core.CutPlan plan = LiftOff();
                if (!plan.Ok) { Controller?.Say(plan.Refusal); _refused = 600; }
            }
            else
            {
                if (_refused > 0) _refused--;
                Body.Position = lies;
                Body.Velocity = N.Vector3.Zero;
            }
        }
        TickLift(1f / 60f);
        if (_liftedFrom is N.Vector3 from && N.Vector3.DistanceSquared(Body.Position, from) > LetGoAfter * LetGoAfter)
        {
            _liftedFrom = null;
            Zone.Edits?.ReleaseStructures(Body.Id);
        }
        if (!Unanchored || Controller == null) return;
        bool afloat = Zone.Physics.GravityAt(Body).LengthSquared() < 1e-6f;
        if (Controller.Frozen || afloat && Body.Velocity.Length() < 0.2f)
        {
            Anchor();
            return;
        }
        // Nobody flies a falling base: no autopilot holds its course. In space the assist brings it to rest.
        if (Flight.Input.Seated == false) Flight.Input = PilotInput.LetGo;
    }

    /// <summary>A fixed structure becomes a hull on the flight model, landed where it stands until its pilot lifts it off.</summary>
    void TakeFlight()
    {
        if (Binding is FreeBinding) return;
        if (Binding is FixedBinding anchored)
        {
            var hull = new CharacterBody3D { Name = anchored.Static.Name };
            MoveNodes(anchored.Static, hull);
            Binding = Zone.Host.Rebind(Body, new HullBinding(hull));
            _liftedFrom = Body.Position;
        }
        Body.Kind = BodyKind.Vessel;
        Body.Motion = GalHack.Physics.Motion.Kinematic;
        Body.DragArea = StructureBridge.DragArea(Structure);
        Spec = new StructureVessel(Structure, Zone.Data.Handling);
        Flight = new HandlingFlight(Spec, Zone.Data.Handling);
        _legsKey = LegsKey();
        N.Quaternion rotation = Body.Rotation;
        N.Vector3 position = Body.Position;
        Body.Controller = new VesselController(Flight, Spec.HoverPoints, Spec.Legs);
        Controller.PlaceLanded(Body, Body.Position, MathX.Heading(rotation));
        Body.Rotation = rotation;
        // A claimed wreck stays where it lies (in the ground it came down in, if it did) until it lifts off.
        if (Derelict != null) Body.Position = (_lies = position).Value;
        Binding.Teleport();
        FollowPilot();
        Fx.Setup(this);
    }

    /// <summary>A hull at rest becomes a fixed structure where it stands: no more physics, and the ground keeps off its faces.</summary>
    void Anchor()
    {
        if (Binding is not HullBinding hull) return;
        Zone.Physics.Unpark(Body);
        var node = new StaticBody3D { Name = hull.Hull.Name };
        MoveNodes(hull.Hull, node);
        Body.Kind = BodyKind.Base;
        Body.Motion = GalHack.Physics.Motion.Fixed;
        Body.Controller = null;
        Body.Velocity = N.Vector3.Zero;
        Body.AngularVelocity = N.Vector3.Zero;
        Body.Asleep = false;
        Binding = Zone.Host.Rebind(Body, new FixedBinding(node));
        Binding.Teleport();
        Spec = null;
        Flight = null;
        _liftedFrom = null;
        ClearGround();
        Fx.Setup(this);
    }

    /// <summary>Moves the hull's shapes, looks and cabin (with anyone in it) onto a new node, and frees the old one.</summary>
    void MoveNodes(CollisionObject3D from, CollisionObject3D to)
    {
        Zone.AddChild(to);
        to.GlobalTransform = from.GlobalTransform;
        HullShapes.MoveTo(to);
        foreach (Node child in from.GetChildren())
            if (child is not CollisionShape3D) child.Reparent(to, false);
        from.GetParent().RemoveChild(from);
        from.QueueFree();
    }

    /// <summary>Every block anchored at or under the ground meets it flush (terrain-digging's rule 4).</summary>
    public void ClearGround()
    {
        if (Zone.Edits == null) return;
        var parts = GroundParts().SelectMany(g => g).ToList();
        if (parts.Count > 0) Zone.Edits.ClearForStructure(parts); // one edit for them all
    }

    /// <summary>The blocks at and under the ground, grouped by the 16 m column of ground they stand in (so clearing can go a column a tick).</summary>
    public List<List<(TerrainDigging.Terrain.VolumeKey, TerrainDigging.Terrain.StructureVolume)>> GroundParts()
    {
        float cell = Structure.Grid.CellSize;
        var groups = new SortedDictionary<(int, int), List<(TerrainDigging.Terrain.VolumeKey, TerrainDigging.Terrain.StructureVolume)>>();
        foreach (var (p, _) in Structure.Blocks())
        {
            Vector3 at = ToWorld(StructureBridge.CellCenter(Structure, p));
            if (at.Y - cell > Zone.GroundAt(at.X, at.Z).Y) continue;
            var key = ((int)MathF.Floor(at.X / 16f), (int)MathF.Floor(at.Z / 16f));
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = new();
            list.Add((GroundKey(p), VolumeOf(p)));
        }
        return groups.Values.ToList();
    }

    /// <summary>
    /// A block of a base reaching into the ground: the cells whose centres lie inside it are cleared
    /// and its volume registered, so the ground meets its faces flush (terrain-digging's rule 4).
    /// </summary>
    public void ClearGround(Int3 cell) => Zone.Edits?.ClearForStructure(GroundKey(cell), VolumeOf(cell));

    TerrainDigging.Terrain.StructureVolume VolumeOf(Int3 cell) =>
        new(Body.ToParent(StructureBridge.CellCenter(Structure, cell)), Body.Rotation, new N.Vector3(Structure.Grid.CellSize));

    /// <summary>A block's hold on the ground.</summary>
    public TerrainDigging.Terrain.VolumeKey GroundKey(Int3 cell) => new(Body.Id, cell.X, cell.Y, cell.Z);
}
