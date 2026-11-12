using System;
using System.Collections.Generic;
using System.Diagnostics;
using CapitalBase.Core;
using CapitalBase.World;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using Slice.Surface;
using PhysicsSandbox.Bridges;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;
using TInt3 = TerrainDigging.Terrain.Int3;

namespace Slice.Vessels;

/// <summary>
/// A wreck claimed where it lies, and lifted free of the ground it came down in: capital-base's clean lift-off
/// (design, "Clean liftoff"). The ground the hull is embedded in is cut along the straight climb (CutFree), it
/// crumbles away, and once the terrain's colliders match the cut the hull climbs straight up through it at the
/// climb speed (LiftOff), then the pilot has it.
/// </summary>
public sealed partial class Vessel : ILiftHull
{
    /// <summary>
    /// The design's reference limits for a lift-off: up to 100 m of climb and 20,000 cells of ground cut, never bedrock.
    /// The clearance round each block is half a metre: with capital-base's 0.75 m every hull lying in the ground loses two
    /// layers of ground under its whole keel, and a capital ship's keel alone is past the limit.
    /// </summary>
    public static readonly CapitalRules.LiftoffData LiftRules = new() { MaxClimb = 100f, MaxCells = 20000, Margin = 0.5f, MinClimb = 3f, ClearBand = 3f, ClimbSpeed = 3f };

    /// <summary>The lift-off under way, or the last one.</summary>
    public LiftOff Lifting { get; private set; }

    Body ILiftHull.Body => Body;
    HandlingFlight ILiftHull.Flight => Flight;
    void ILiftHull.LetGoOfGround() => Zone.Edits?.ReleaseStructures(Body.Id);

    /// <summary>Came down on a planet as a wreck (<see cref="SpawnGroundWreck"/>): a wreck until a core claims it.</summary>
    public bool GroundWreck { get; private set; }

    /// <summary>
    /// A wreck lying where it came down on a planet: one fixed body, as a base is (nothing moves it, it walks like a
    /// base, the ground meets it), turned as it lies, with no core.
    /// </summary>
    public static Vessel SpawnGroundWreck(SurfaceZone zone, Structure s, N.Vector3 at, N.Quaternion rotation)
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
            Integrity = HullPoints(zone, mass.Mass),
            Shape = StructureBridge.ShapeOf(s),
            Position = at,
            Rotation = rotation,
        });
        var vessel = new Vessel(zone, s, body) { GroundWreck = true };
        var node = new StaticBody3D { Name = s.Name.Replace(" ", "").Replace("(", "").Replace(")", "") };
        zone.AddChild(node);
        vessel.Binding = zone.Host.Bind(body, new FixedBinding(node));
        vessel.HullShapes = new StructureShapes(node);
        vessel.Furnish(node, drawLater: true);
        vessel.Binding.Teleport();
        vessel.Register();
        return vessel;
    }

    /// <summary>
    /// A core set into a wreck with no core claims it (design: "Any wreck with no core ... can be claimed by
    /// placing a core in it"): the core goes in the cell, the wreck is yours. Returns why not, or null.
    /// </summary>
    public string ClaimWith(Int3 cell)
    {
        if (Structure.Core != null) return $"{Name} · HAS A CORE";
        Block core = Building.Make(Structure, Zone.Data.Catalogue.Type("core"));
        PlaceError e = Building.Place(Structure, cell, core, null, Building.DefaultOwner);
        if (e != PlaceError.None) return $"CORE WON'T SEAT · {e}";
        Structure.Owner = Building.DefaultOwner;
        ApplyEdits();
        Claimed(Zone.Physics.Zone.Strength);
        return null;
    }

    /// <summary>
    /// Cuts the hull free and starts it climbing. The plan is capital-base's: every terrain cell within the margin of
    /// a block anywhere along the straight climb is cut first (bedrock refuses); the hull then rises through the
    /// cut. Returns the plan (its refusal says why it can't go).
    /// </summary>
    public CutPlan LiftOff()
    {
        var clock = Stopwatch.StartNew();
        float cell = Structure.Grid.CellSize;
        // Each block's lever arm is from the hull's middle, which is what it would turn about as it levels.
        var centres = new List<N.Vector3>();
        foreach (var (p, _) in Structure.Cells()) centres.Add(Body.ToParent(StructureBridge.CellCenter(Structure, p)));
        N.Vector3 middle = N.Vector3.Zero;
        foreach (N.Vector3 c in centres) middle += c;
        middle /= Math.Max(1, centres.Count);
        var boxes = new List<BlockBox>(centres.Count);
        foreach (N.Vector3 at in centres) boxes.Add(new BlockBox(at, Body.Rotation, new N.Vector3(cell * 0.5f), (at - middle).Length()));
        float tilt = MathX.Angle(N.Vector3.Transform(N.Vector3.UnitY, Body.Rotation), N.Vector3.UnitY);
        var store = Zone.Planet.Terrain.Store;
        CutPlan plan = CutFree.Plan(boxes, tilt, LiftRules, c => store.Check(c, TerrainDigging.Terrain.EditKind.Tool));
        if (!plan.Ok) return plan;
        GalHack.Terrain.PlanetEdit edit = Zone.Edits.DigCells(plan.Cells);
        Crumble(edit);
        Lifting = new LiftOff(this, plan, edit, clock.Elapsed.TotalMilliseconds);
        Body.Asleep = false;
        return plan;
    }

    /// <summary>The ground falls away from the hull in grit and dust as it is cut.</summary>
    void Crumble(GalHack.Terrain.PlanetEdit edit)
    {
        if (Zone.Dust == null) return;
        int every = Math.Max(1, edit.Removed.Count / 60);
        for (int i = 0; i < edit.Removed.Count; i += every)
        {
            TInt3 c = edit.Removed[i].Cell;
            Zone.Dust.Puff(new Vector3(c.X + 0.5f, c.Y + 0.5f, c.Z + 0.5f), new Color(0.36f, 0.31f, 0.26f, 0.8f), 1.6f);
        }
    }

    /// <summary>Each tick while it climbs: capital-base's lift-off steps it against the surface's ground.</summary>
    void TickLift(float dt)
    {
        if (Lifting == null || Lifting.Phase == LiftOffPhase.Done || Flight == null) return;
        Lifting.Step(_ground ??= new SurfaceGround(Zone), dt);
    }

    SurfaceGround _ground;

    /// <summary>The surface as the ground a hull lifts off: the terrain still rebuilding round the cut, the climb speed.</summary>
    sealed class SurfaceGround : ILiftGround
    {
        readonly SurfaceZone _zone;
        public SurfaceGround(SurfaceZone zone) => _zone = zone;
        int _ticks;
        /// <summary>The terrain streamer's queue, settling after the cut; it never empties while the land streams round a moving eye, so a second and a half at most.</summary>
        public int Pending => ++_ticks > 90 ? 0 : _zone.Streamer?.Pending ?? 0;
        public float ClimbSpeed => LiftRules.ClimbSpeed;
        public float TopSpeed(ILiftHull hull) => hull.Flight.Stats?.TopSpeed is float top && top > 0f ? top : 100f;
        public void Settle(ILiftHull hull) { }
    }
}
