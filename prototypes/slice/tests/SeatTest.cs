using System;
using System.Collections.Generic;
using System.Diagnostics;
using GalHack.Structures;
using Godot;
using ShipHandling.Handling;
using Slice.Game;
using Slice.Vessels;

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
        grid small
        mode capital
        block 0 0 0 core
        block 1 0 0 steel
        block -1 0 0 steel
        block 1 1 0 cockpit
        block -1 1 0 cockpit rot=+y+x
        block 0 0 1 gyro
        """;

    public override void _Ready()
    {
        var clock = Stopwatch.StartNew();
        try
        {
            Chairs();
            Flying();
            Hover();
            Roles();
        }
        catch (Exception e) { Check(false, $"threw: {e}"); }
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        GalHack.Testing.Backstop.Quit(this, _failures == 0 ? 0 : 1);
    }

    static Structure Blocks()
    {
        Structure s = Blueprint.Load(Hull, GameData.Shared.Catalogue, out List<string> errors);
        if (s == null || errors.Count > 0) throw new InvalidOperationException(string.Join("; ", errors));
        return s;
    }

    void Chairs()
    {
        Structure s = Blocks();
        Basis ahead = VesselRoles.BasisOf(s, new Int3(1, 1, 0)), side = VesselRoles.BasisOf(s, new Int3(-1, 1, 0));
        Check((-ahead.Z).IsEqualApprox(Vector3.Forward), $"a chair set the default way faces the hull's forward ({-ahead.Z})");
        Check((-side.Z).IsEqualApprox(Vector3.Right), $"a chair turned to face +x faces the hull's right ({-side.Z})");
        Check(side.Y.IsEqualApprox(Vector3.Up) && Mathf.IsEqualApprox(side.Determinant(), 1f), "its up is still the hull's, and its frame is a true rotation");
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
        public VesselClass Class => VesselClass.Small;
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
        Basis ahead = VesselRoles.BasisOf(s, new Int3(1, 1, 0)), side = VesselRoles.BasisOf(s, new Int3(-1, 1, 0));

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
        turn.Seat = VesselRoles.BasisOf(s, new Int3(-1, 1, 0));
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
        var one = new Int3(1, 1, 0);
        var two = new Int3(-1, 1, 0);
        roles.Sat("castaway", one);
        Check(roles.Pilot == "castaway" && roles.PilotSeat == one, "alone, you hold the pilot role when you sit");
        roles.Sat("friend", two);
        Check(roles.Pilot == "castaway", "someone else sitting down doesn't take it");
        Check(!roles.Assign("stranger"), "the role can't go to someone who isn't in a seat");
        Check(roles.Assign("friend") && roles.Pilot == "friend" && roles.PilotSeat == two, "it can go to anyone who is, and the frame goes with it");
        roles.Stood("friend");
        Check(roles.Pilot == "castaway" && roles.PilotSeat == one, "when its holder gets up it passes to someone still seated");
        roles.Stood("castaway");
        Check(roles.Pilot == null && roles.PilotSeat == null && changes > 0, "with nobody seated it stands empty");
    }

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
    }
}
