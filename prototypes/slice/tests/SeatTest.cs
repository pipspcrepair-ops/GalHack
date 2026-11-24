using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;
using ShipHandling.Handling;
using Slice.Game;
using Slice.Vessels;
using N = System.Numerics;

namespace Slice.Tests;

/// <summary>
/// The pilot role (design: a vessel's heading is set by the seat of whoever holds it): a two-chair
/// hull with the chairs facing different ways, flown by the handling model itself. It goes forward
/// relative to whichever chair holds the role; handing the role over turns what counts as forward
/// at once, and the ship itself doesn't move; thrusters keep pushing along their own facing;
/// hover vessels keep a level, yaw-only frame from the seat. And the role as data: anyone in a
/// seat can receive it, nobody else can, and it passes on when its holder gets up.
///
/// Run: .\tools\test.ps1 -Project prototypes/slice -Scene res://tests/SeatTest.tscn
/// </summary>
public partial class SeatTest : Node
{
    int _failures, _passes;

    const string Hull = """
        schema galhack.blueprint
        version 1
        name "Two Seats"
        grid 1m
        mode capital
        block -4..-1 0 -2..2 base-block
        block 1..3 0 -2..2 base-block
        block 0 0 -2..-1 base-block
        block 0 0 1..2 base-block
        block 0 0 3 gyro-t1
        block 0 0 0 core
        block 2 1 -1 command-chair
        block -2 1 0 command-chair rot=+y+x
        """;

    /// <summary>The two chairs' anchors: one facing the hull's forward, one turned to face +x.</summary>
    static readonly Int3 One = new(2, 1, -1), Two = new(-2, 1, 0);

    public override void _Ready()
    {
        var clock = Stopwatch.StartNew();
        try
        {
            Chairs();
            GettingUp();
            BlueprintChairs();
            Flying();
            Hover();
            Roles();
        }
        catch (Exception e) { Check(false, $"threw: {e}"); }
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        GalHack.Testing.Backstop.Quit(this, _failures == 0 ? 0 : 1);
    }

    static Structure Blocks(string hull = Hull)
    {
        Structure s = Blueprint.Load(hull, GameData.Shared.Catalogue, out List<string> errors);
        if (s == null || errors.Count > 0) throw new InvalidOperationException(string.Join("; ", errors));
        return s;
    }

    void Chairs()
    {
        Structure s = Blocks();
        Basis ahead = VesselRoles.BasisOf(s, One), side = VesselRoles.BasisOf(s, Two);
        Check((-ahead.Z).IsEqualApprox(Vector3.Forward), $"a chair set the default way faces the hull's forward ({-ahead.Z})");
        Check((-side.Z).IsEqualApprox(Vector3.Right), $"a chair turned to face +x faces the hull's right ({-side.Z})");
        Check(side.Y.IsEqualApprox(Vector3.Up) && Mathf.IsEqualApprox(side.Determinant(), 1f), "its up is still the hull's, and its frame is a true rotation");
    }

    /// <summary>Getting up puts you straight behind the chair, whichever way it is turned: in the cell past its rear, clear of it, and at its sides only after that.</summary>
    void GettingUp()
    {
        Structure s = Blocks();
        foreach (Int3 chair in new[] { One, Two })
        {
            s.TryGetBlock(chair, out Int3 anchor, out Block block);
            N.Vector3[] spots = Vessel.StandSpotsOf(s, anchor, block);
            Basis facing = VesselRoles.BasisOf(s, chair);
            Vector3 back = facing.Z, right = facing.X;
            Check(spots.Length == 5, $"a chair at {chair} has five places to get up to: behind it twice, each side, in front ({spots.Length})");
            if (spots.Length != 5) continue;
            Vector3 Level(N.Vector3 o) => new Vector3(o.X, 0f, o.Z).Normalized();
            N.Vector3 seat = StructureBridge.SeatPoint(s, anchor, block), step = new(back.X, 0f, back.Z);
            Check(Level(spots[0]).IsEqualApprox(back), $"the first is straight behind it, which is {back} for the chair at {chair} ({Level(spots[0])})");
            Int3 stand = StructureBridge.CellAt(s, seat + spots[0]), last = StructureBridge.CellAt(s, seat + spots[0] - step * s.Grid.CellSize);
            List<Int3> cells = s.CellsOf(anchor, block).ToList();
            Check(!cells.Contains(stand) && cells.Contains(last), $"in the cell just past its rear, clear of the chair (standing in {stand}, one cell closer is {last})");
            Check(Vessel.CanStandAt(s, seat + spots[0] - N.Vector3.UnitY * SeatDrop), "and there is room to stand there");
            Check(Level(spots[1]).IsEqualApprox(back) && spots[1].Length() < spots[0].Length() && cells.Contains(StructureBridge.CellAt(s, seat + spots[1])), "the second is also straight behind it, but short of the chair's rear, in its own box");
            Check(Level(spots[2]).IsEqualApprox(right) && Level(spots[3]).IsEqualApprox(-right), "then its right and left");
            Check(Level(spots[4]).IsEqualApprox(-back), "and last, in front of it");
        }

        // Machinery right behind the chair (the pod's generator and lockers): you still get up behind it, in the room past its backrest, not at its side.
        Structure walled = Blocks(Hull + "\nblock 2 1 2 base-block\nblock 2 2 2 base-block");
        walled.TryGetBlock(One, out Int3 at, out Block chairBlock);
        N.Vector3[] options = Vessel.StandSpotsOf(walled, at, chairBlock);
        N.Vector3 sat = StructureBridge.SeatPoint(walled, at, chairBlock), feet = sat - N.Vector3.UnitY * SeatDrop;
        int pick = Array.FindIndex(options, o => Vessel.CanStandAt(walled, feet + o));
        Check(!Vessel.CanStandAt(walled, feet + options[0]), "with a block right behind a chair, the cell past its rear is no place to stand");
        Check(pick == 1, $"and it is the room just behind the backrest that is chosen, not a side (option {pick})");
        Check(options.Length == 5 && walled.CellsOf(at, chairBlock).Contains(StructureBridge.CellAt(walled, sat + options[pick])), "which is inside the chair's own box");
    }

    /// <summary>Where a seated person's feet are under the seat, m (a chair box's bottom: this chair's seat is half a metre up in it).</summary>
    const float SeatDrop = 0.5f;

    /// <summary>Every chair in the shipped blueprints has room to get up straight behind it (not at its side), so none is boxed in by what is built behind it.</summary>
    void BlueprintChairs()
    {
        int chairs = 0, boxedIn = 0;
        foreach (string path in System.IO.Directory.GetFiles(ProjectSettings.GlobalizePath("res://data/blueprints"), "*.gbp"))
        {
            Structure s = Blueprint.Load(System.IO.File.ReadAllText(path), GameData.Shared.Catalogue, out List<string> errors);
            if (s == null) { Check(false, $"{System.IO.Path.GetFileName(path)} loads ({string.Join("; ", errors)})"); continue; }
            foreach (var (pos, b) in s.Blocks().ToList())
            {
                if (!s.TypeOf(b).IsCommandChair) continue;
                s.TryGetBlock(pos, out Int3 anchor, out Block block);
                N.Vector3[] spots = Vessel.StandSpotsOf(s, anchor, block);
                N.Vector3 feet = StructureBridge.SeatPoint(s, anchor, block) - N.Vector3.UnitY * SeatDrop;
                int pick = Array.FindIndex(spots, o => Vessel.CanStandAt(s, feet + o));
                chairs++;
                if (pick is < 0 or > 1)
                {
                    boxedIn++;
                    GD.Print($"  {System.IO.Path.GetFileName(path)}: the chair at {pos} gets you up at option {pick}, not behind it");
                }
            }
        }
        Check(chairs > 5 && boxedIn == 0, $"every chair in the shipped blueprints lets you get up behind it ({chairs} chairs, {boxedIn} boxed in)");
    }

    /// <summary>A thruster on every face of a hull, each with its own strength, so a direction's thrust says which thruster pushed.</summary>
    static HandlingStats Stats()
    {
        var hull = new TestHull();
        return HandlingModel.Compute(hull, new FlightConditions(0f, false), GameData.Shared.Handling);
    }

    sealed class TestHull : IVesselStructure
    {
        public string Name => "Test hull";
        public bool HoverMode => false;
        public float HullMass => 10f;
        public float CargoMass => 0f;
        public IReadOnlyList<ThrusterSpec> Thrusters { get; } = new[]
        {
            new ThrusterSpec("small", Vector3.Zero, Vector3.Forward, 500f, 1f), new ThrusterSpec("small", Vector3.Zero, Vector3.Right, 200f, 1f),
            new ThrusterSpec("small", Vector3.Zero, Vector3.Back, 100f, 1f), new ThrusterSpec("small", Vector3.Zero, Vector3.Left, 100f, 1f),
        };
        public IReadOnlyList<GyroSpec> Gyros { get; } = new[] { new GyroSpec("small", Vector3.Zero, 800f, 1f) };
        public IReadOnlyList<HoverEngineSpec> HoverEngines { get; } = Array.Empty<HoverEngineSpec>();
        public IReadOnlyList<GearSpec> LandingGear { get; } = Array.Empty<GearSpec>();
        public IReadOnlyList<HullBlock> Blocks { get; } = Array.Empty<HullBlock>();
        public Vector3 Cockpit => Vector3.Zero;
        public int Revision => 0;
    }

    void Flying()
    {
        HandlingStats stats = Stats();
        Tunables t = GameData.Shared.Handling;
        var where = new FlightConditions(0f, false);
        Structure s = Blocks();
        Basis ahead = VesselRoles.BasisOf(s, One), side = VesselRoles.BasisOf(s, Two);

        FlightState a = FlightState.At(Basis.Identity), b = FlightState.At(Basis.Identity);
        a.Seat = ahead;
        b.Seat = side;
        for (int i = 0; i < 60; i++)
        {
            FlightModel.Step(ref a, PilotInput.Hands(new Vector3(0, 0, -1)), stats, where, t, 1f / 60f);
            FlightModel.Step(ref b, PilotInput.Hands(new Vector3(0, 0, -1)), stats, where, t, 1f / 60f);
        }
        Check(a.Velocity.Normalized().Dot(Vector3.Forward) > 0.99f, $"W from the forward chair flies the hull forward ({a.Velocity})");
        Check(b.Velocity.Normalized().Dot(Vector3.Right) > 0.99f, $"W from the sideways chair flies the hull to its right ({b.Velocity})");
        Check(a.Velocity.Length() > b.Velocity.Length() * 1.5f,
            $"each by the thrusters facing that way, along their own facing: {a.Velocity.Length():F1} m/s on the 500 kN jet, {b.Velocity.Length():F1} on the 200");

        // Yaw from the seat turns about the seat's up.
        FlightState turn = FlightState.At(Basis.Identity);
        turn.Seat = VesselRoles.BasisOf(s, Two);
        Vector3 before = -turn.Control.Z;
        for (int i = 0; i < 30; i++) FlightModel.Step(ref turn, PilotInput.Hands(Vector3.Zero, new Vector3(0, 1, 0)), stats, where, t, 1f / 60f);
        Vector3 after = -turn.Control.Z;
        Check(before.Dot(after) < 0.999f && Mathf.Abs(after.Y) < 1e-3f, $"yaw turns the seat's forward in its own plane ({before} to {after})");
        FlightState pitch = FlightState.At(Basis.Identity);
        pitch.Seat = side;
        for (int i = 0; i < 30; i++) FlightModel.Step(ref pitch, PilotInput.Hands(Vector3.Zero, new Vector3(1, 0, 0)), stats, where, t, 1f / 60f);
        Check((-pitch.Control.Z).Y > 0.05f, $"pitch up raises the seat's nose, whichever way it faces ({-pitch.Control.Z})");
        var cruise = FlightState.At(Basis.Identity);
        cruise.Seat = side;
        cruise.Velocity = Vector3.Right * 40f;
        FlightModel.ToggleCruise(ref cruise);
        Check(Mathf.IsEqualApprox(cruise.CruiseSpeed, 40f), $"cruise holds speed along the seat's forward ({cruise.CruiseSpeed:F1} m/s)");

        // Handing the role over mid-flight: forward moves, the ship doesn't.
        Basis orientation = a.Orientation;
        Vector3 velocity = a.Velocity, spin = a.AngularVelocity;
        a.Seat = side;
        Check(a.Orientation == orientation && a.Velocity == velocity && a.AngularVelocity == spin, "the pilot role handed over: the ship's attitude, velocity and spin don't change");
        for (int i = 0; i < 90; i++) FlightModel.Step(ref a, PilotInput.Hands(new Vector3(0, 0, -1)), stats, where, t, 1f / 60f);
        Check(a.Orientation.IsEqualApprox(orientation) && (a.Velocity - velocity).Normalized().Dot(Vector3.Right) > 0.5f,
            $"and W now drives toward the new seat's forward, the ship unturned ({a.Velocity})");
    }

    void Hover()
    {
        // A hover vessel's frame is level: the seat's forward with the tilt taken out, yawing only.
        var state = FlightState.At(new Basis(Vector3.Right, 0.3f));
        state.Seat = new Basis(Vector3.Up, -Mathf.Pi / 2f);
        Basis frame = FlightModel.Frame(state.Control, hover: true);
        Check(Mathf.Abs((-frame.Z).Y) < 1e-4f && frame.Y.IsEqualApprox(Vector3.Up), $"a hover frame from a seat is level ({-frame.Z})");
        Check((-frame.Z).Dot(Vector3.Right) > 0.9f, "and it points the seat's way");
        Basis zero = FlightModel.Frame(state.Orientation, hover: true);
        Check((-zero.Z).Dot(Vector3.Forward) > 0.9f, "where the hull's own forward would point elsewhere");
    }

    void Roles()
    {
        var roles = new VesselRoles();
        int changes = 0;
        roles.PilotChanged += () => changes++;
        var one = One;
        var two = Two;
        roles.Sat("sleeper", one);
        Check(roles.Pilot == "sleeper" && roles.PilotSeat == one, "alone, you hold the pilot role when you sit");
        roles.Sat("friend", two);
        Check(roles.Pilot == "sleeper", "someone else sitting down doesn't take it");
        Check(!roles.Assign("stranger"), "the role can't go to someone who isn't in a seat");
        Check(roles.Assign("friend") && roles.Pilot == "friend" && roles.PilotSeat == two, "it can go to anyone who is, and the frame goes with it");
        roles.Stood("friend");
        Check(roles.Pilot == "sleeper" && roles.PilotSeat == one, "when its holder gets up it passes to someone still seated");
        roles.Stood("sleeper");
        Check(roles.Pilot == null && roles.PilotSeat == null && changes > 0, "with nobody seated it stands empty");
    }

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
    }
}
