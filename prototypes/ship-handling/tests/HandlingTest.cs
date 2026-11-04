using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipHandling.Handling;
using ShipHandling.Physics;

namespace ShipHandling;

/// <summary>
/// Headless tests of the handling model and the physics module. Run with:
///   .\tools\test.ps1 -Project prototypes/ship-handling -Scene res://tests/HandlingTest.tscn
/// The pure rules are checked first, with no engine involved. Then a real planet surface is
/// built and vessels are driven over it through the physics module, several physics ticks per
/// frame where nothing else moves. Exits 0 if every check passes, 1 if any fails.
/// </summary>
public partial class HandlingTest : Node
{
    const float Dt = 1f / 60f;

    Tunables _t;
    FlightPhysics _physics;
    Vessel _rover, _fighter, _capital;
    VesselLoadout _roverBuild, _fighterBuild, _capitalBuild;
    readonly List<ImpactInfo> _impacts = new();
    int _landed, _liftedOff;
    int _failures;
    IEnumerator<int> _run;
    int _wait = 3;

    /// <summary>Physics ticks per frame. Scenarios where vessels touch each other run at one.</summary>
    int _substeps = 8;

    public override void _Ready()
    {
        _t = Main.LoadTunables();
        GD.Print("-- handling model --");
        PureChecks();

        AddChild(new PlanetSurface { Name = "Surface", Visuals = false });
        _physics = new FlightPhysics(_t) { AutoStep = false };
        AddChild(_physics);
        _physics.Impact += _impacts.Add;
        _physics.Landed += _ => _landed++;
        _physics.LiftedOff += _ => _liftedOff++;

        _roverBuild = VesselDesigns.Rover(_t);
        _fighterBuild = VesselDesigns.Fighter(_t);
        _capitalBuild = VesselDesigns.Capital(_t);
        _rover = _physics.Spawn(_roverBuild, Vector3.Zero, 0f);
        _fighter = _physics.Spawn(_fighterBuild, Vector3.Zero, 0f);
        _capital = _physics.Spawn(_capitalBuild, Vector3.Zero, 0f);
        ParkAll();
        _run = Scenarios().GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_wait-- > 0) return; // let the static world settle into the physics space first
        for (int i = 0; i < _substeps; i++)
        {
            if (!_run.MoveNext())
            {
                GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} CHECK(S) FAILED");
                GetTree().Quit(_failures == 0 ? 0 : 1);
                SetPhysicsProcess(false);
                return;
            }
            _physics.Step(Dt);
        }
    }

    // ==== The pure handling model ==========================================================

    void PureChecks()
    {
        var air = new FlightConditions(9.8f, true);
        var space = new FlightConditions(9.8f, false);
        VesselLoadout fighter = VesselDesigns.Fighter(_t), rover = VesselDesigns.Rover(_t), capital = VesselDesigns.Capital(_t);
        HandlingStats f = HandlingModel.Compute(fighter, air, _t);

        // Acceleration is thrust / mass per direction; double the mass and it halves; top speed stays.
        bool perAxis = true;
        foreach (var v in new[] { fighter, rover, capital })
        {
            HandlingStats s = HandlingModel.Compute(v, air, _t);
            for (int d = 0; d < 6; d++)
            {
                float thrust = v.Thrusters.Where(x => x.Facing.Dot(HandlingModel.Directions[d]) > 0.99f).Sum(x => x.Thrust);
                perAxis &= Mathf.IsEqualApprox(s.Accel((Direction)d), thrust / s.Mass);
            }
        }
        Check(perAxis, $"acceleration is thrust / mass on all six axes for all three vessels (fighter fwd {f.Accel(Direction.Forward):F2} m/s² = {f.Thrust[0]:F0} kN / {f.Mass:F1} t)");

        VesselLoadout heavy = fighter.Clone();
        heavy.AddCargo(f.Mass);
        HandlingStats h = HandlingModel.Compute(heavy, air, _t);
        bool halved = Enumerable.Range(0, 6).All(d => Mathf.IsEqualApprox(h.Accel((Direction)d), f.Accel((Direction)d) / 2f));
        Check(halved && h.TopSpeed == f.TopSpeed,
            $"doubling the mass ({f.Mass:F1} -> {h.Mass:F1} t) halves every acceleration (fwd {f.Accel(Direction.Forward):F2} -> {h.Accel(Direction.Forward):F2}) and leaves top speed at {h.TopSpeed:F0} m/s");

        float simulated = SimulateAccel(fighter, air, Vector3.Forward, boost: false);
        Check(Mathf.Abs(simulated - f.Accel(Direction.Forward)) < 0.01f * f.Accel(Direction.Forward),
            $"in flight the fighter really accelerates at its number ({simulated:F2} m/s² from rest)");

        // Turning: gyro torque / mass, with a floor for the heavy.
        Check(Mathf.IsEqualApprox(f.TurnRate, f.GyroTorque / f.Mass) && !f.TurnAtFloor,
            $"turn rate is gyro torque / mass ({f.GyroTorque:F0} / {f.Mass:F1} = {f.TurnRate:F1}°/s)");
        HandlingStats c = HandlingModel.Compute(capital, air, _t);
        Check(c.RawTurnRate < _t.Turning.Floor && Mathf.IsEqualApprox(c.TurnRate, _t.Turning.Floor) && c.TurnAtFloor,
            $"the {c.Mass:F0} t capital ship gets the floor: {c.TurnRate:F0}°/s instead of {c.RawTurnRate:F1}°/s");
        float spin = SimulateTurn(capital, air);
        Check(Mathf.Abs(spin - c.TurnRate) < 0.01f, $"and really yaws at {spin:F2}°/s with the stick held over");

        // Lopsided: identical numbers, zero spin.
        VesselLoadout lopsided = fighter.Clone();
        lopsided.MakeLopsided();
        HandlingStats l = HandlingModel.Compute(lopsided, air, _t);
        bool same = Enumerable.Range(0, 6).All(d => l.Thrust[d] == f.Thrust[d]) && l.TurnRate == f.TurnRate && l.LiftMargin == f.LiftMargin;
        var state = FlightState.At(Basis.Identity);
        for (int i = 0; i < 180; i++)
            FlightModel.Step(ref state, PilotInput.Hands(new Vector3(1f, 1f, -1f), boost: i == 0), l, air, _t, Dt);
        Check(same && state.AngularVelocity == Vector3.Zero && state.Orientation.IsEqualApprox(Basis.Identity),
            "a lopsided build (every thruster on the left flank) has the same numbers and 3 s of full thrust adds zero spin");

        // Class top speeds.
        var classes = new (VesselClass cls, float airTop, float spaceTop)[]
        {
            (VesselClass.Hover, 50f, 50f), (VesselClass.Small, 70f, 130f), (VesselClass.Capital, 60f, 100f),
        };
        bool tops = classes.All(x => _t.Class(x.cls).TopSpeedAir == x.airTop && _t.Class(x.cls).TopSpeedSpace == x.spaceTop);
        bool lower = _t.Class(VesselClass.Small).TopSpeedAir < _t.Class(VesselClass.Small).TopSpeedSpace
                     && _t.Class(VesselClass.Capital).TopSpeedAir < _t.Class(VesselClass.Capital).TopSpeedSpace;
        Check(tops && lower, "class top speeds: hover 50, small 70 air / 130 space, capital 60 air / 100 space; air below space");
        float airMax = SimulateTopSpeed(fighter, air, boost: true), spaceMax = SimulateTopSpeed(fighter, space, boost: true);
        Check(airMax <= 70f + 1e-3f && airMax > 69.9f && spaceMax <= 130f + 1e-3f && spaceMax > 129.9f,
            $"full throttle and boost reach but never pass top speed ({airMax:F2} m/s in air, {spaceMax:F2} in space)");

        // Flight assist: let go and it stops in the expected time, with no overshoot.
        foreach (var v in new[] { fighter, rover, capital })
            CheckStop(v, air);

        // Boost.
        float boosted = SimulateAccel(fighter, air, Vector3.Forward, boost: true);
        Check(Mathf.Abs(boosted / simulated - _t.Boost.Multiplier) < 0.01f,
            $"boost multiplies acceleration by {boosted / simulated:F3} ({simulated:F2} -> {boosted:F2} m/s²)");
        CheckBoostCooldown(f, air);

        // Lift margin per gravity.
        bool margins = true;
        foreach (float g in new[] { 0f, 3.7f, 9.8f, 24f })
        {
            HandlingStats s = HandlingModel.Compute(fighter, new FlightConditions(g, true), _t);
            margins &= Mathf.IsEqualApprox(s.LiftMargin, s.Thrust[(int)Direction.Up] - s.Mass * g);
        }
        HandlingStats r = HandlingModel.Compute(rover, air, _t);
        margins &= Mathf.IsEqualApprox(r.LiftMargin, rover.HoverEngines.Sum(e => e.Lift) - r.Mass * 9.8f);
        Check(margins, $"lift margin is upward thrust - weight at 0, 3.7, 9.8 and 24 m/s² (fighter +{f.LiftMargin:F0} kN here, rover +{r.LiftMargin:F0} kN of hover lift)");

        VesselLoadout loaded = fighter.Clone();
        loaded.AddCargo(50f);
        HandlingStats heavyHere = HandlingModel.Compute(loaded, air, _t);
        HandlingStats heavyMoon = HandlingModel.Compute(loaded, new FlightConditions(3.7f, true), _t);
        Check(!heavyHere.CanLiftOff && heavyHere.LiftWarning?.Contains("TOO HEAVY") == true && heavyMoon.CanLiftOff,
            $"with 50 t of cargo the console warns before anyone tries: \"{heavyHere.LiftWarning}\" (on a 3.7 g world it could still lift off)");

        // Too heavy in the air: it sinks at the deficit, it can't hold altitude.
        var sink = FlightState.At(Basis.Identity);
        for (int i = 0; i < 60; i++) FlightModel.Step(ref sink, PilotInput.Hands(Vector3.Up), heavyHere, air, _t, Dt);
        float expectedSink = -heavyHere.LiftMargin / heavyHere.Mass;
        Check(Mathf.Abs(-sink.Velocity.Y - expectedSink) < 0.05f,
            $"an overloaded ship can't climb: full up for 1 s and it sinks at {-sink.Velocity.Y:F2} m/s (deficit {expectedSink:F2} m/s²)");

        // Autopilot and cruise.
        var hold = FlightState.At(Basis.Identity);
        hold.Velocity = new Vector3(3f, 0f, -40f);
        for (int i = 0; i < 180; i++) FlightModel.Step(ref hold, PilotInput.Autopilot, f, air, _t, Dt);
        var cruise = FlightState.At(Basis.Identity);
        cruise.Velocity = new Vector3(0f, 0f, -45f);
        FlightModel.ToggleCruise(ref cruise);
        for (int i = 0; i < 180; i++) FlightModel.Step(ref cruise, PilotInput.LetGo, f, air, _t, Dt);
        Check(hold.Velocity.IsEqualApprox(new Vector3(3f, 0f, -40f)) && Mathf.Abs(cruise.Velocity.Z + 45f) < 1e-3f,
            "with the pilot out of the seat the autopilot holds course; cruise holds 45 m/s hands off");

        // A first ship should be forgiving: bank it and let go, and the wings come back level.
        var banked = FlightState.At(new Basis(Vector3.Back, Mathf.DegToRad(40f)));
        for (int i = 0; i < 180; i++) FlightModel.Step(ref banked, PilotInput.LetGo, f, air, _t, Dt);
        float bank = Mathf.RadToDeg(Mathf.Asin(Mathf.Abs(banked.Orientation.X.Y)));
        Check(bank < 3f, $"horizon assist: a fighter left at 40° of bank is back to {bank:F1}° within 3 s");

        CheckJink(fighter, f, air);
    }

    /// <summary>Average acceleration over the first 0.25 s from rest along one direction.</summary>
    float SimulateAccel(IVesselStructure v, FlightConditions where, Vector3 dir, bool boost)
    {
        HandlingStats s = HandlingModel.Compute(v, where, _t);
        var state = FlightState.At(Basis.Identity);
        for (int i = 0; i < 15; i++) FlightModel.Step(ref state, PilotInput.Hands(dir, boost: boost), s, where, _t, Dt);
        return state.Velocity.Length() / (15 * Dt);
    }

    float SimulateTurn(IVesselStructure v, FlightConditions where)
    {
        HandlingStats s = HandlingModel.Compute(v, where, _t);
        var state = FlightState.At(Basis.Identity);
        for (int i = 0; i < 120; i++) FlightModel.Step(ref state, PilotInput.Hands(Vector3.Zero, new Vector3(0f, 1f, 0f)), s, where, _t, Dt);
        return Mathf.RadToDeg(state.AngularVelocity.Y);
    }

    float SimulateTopSpeed(IVesselStructure v, FlightConditions where, bool boost)
    {
        HandlingStats s = HandlingModel.Compute(v, where, _t);
        var state = FlightState.At(Basis.Identity);
        float max = 0f;
        for (int i = 0; i < 60 * 20; i++)
        {
            FlightModel.Step(ref state, PilotInput.Hands(Vector3.Forward, boost: boost), s, where, _t, Dt);
            max = Mathf.Max(max, state.Velocity.Length());
        }
        return max;
    }

    /// <summary>
    /// From top speed, let go. Assist brakes at max(reverse thrust / mass, floor) and eases out
    /// the last part, so the expected time is the linear part plus the exponential tail.
    /// </summary>
    void CheckStop(VesselLoadout v, FlightConditions where)
    {
        HandlingStats s = HandlingModel.Compute(v, where, _t);
        float v0 = s.TopSpeed;
        float brake = Mathf.Max(s.Accel(Direction.Back), _t.Assist.MinBrake);
        float knee = brake / _t.Assist.EaseRate;
        float expected = (v0 - knee) / brake + Mathf.Log(knee / _t.Assist.StopBelow) / _t.Assist.EaseRate;

        var state = FlightState.At(Basis.Identity);
        state.Velocity = new Vector3(0f, 0f, -v0);
        float time = 0f, last = v0;
        bool overshoot = false, monotonic = true;
        while (state.Velocity.Z != 0f && time < 60f)
        {
            FlightModel.Step(ref state, PilotInput.LetGo, s, where, _t, Dt);
            time += Dt;
            float forward = -state.Velocity.Z;
            overshoot |= forward < 0f;
            monotonic &= forward <= last + 1e-5f;
            last = forward;
        }
        Check(time <= expected + 2f * Dt && !overshoot && monotonic,
            $"{v.Name}: let go at {v0:F0} m/s and assist stops it in {time:F2} s (expected {expected:F2} s at {brake:F1} m/s²), no overshoot");
    }

    void CheckBoostCooldown(HandlingStats s, FlightConditions where)
    {
        var state = FlightState.At(Basis.Identity);
        float max = 0f;
        int boostedTicks = 0, ticks = (int)((_t.Boost.Duration + 0.5f) / Dt);
        for (int i = 0; i < ticks; i++)
        {
            FlightModel.Step(ref state, PilotInput.Hands(Vector3.Forward, boost: true), s, where, _t, Dt);
            if (state.Boosting) boostedTicks++;
            max = Mathf.Max(max, state.Velocity.Length());
        }
        bool burstEnded = !state.Boosting && boostedTicks <= (int)(_t.Boost.Duration / Dt) + 1;
        FlightModel.Step(ref state, PilotInput.Hands(Vector3.Forward, boost: true), s, where, _t, Dt);
        bool refused = !state.Boosting; // held through the cooldown: nothing
        for (int i = 0; i < (int)(_t.Boost.Cooldown / Dt) + 2; i++)
            FlightModel.Step(ref state, PilotInput.Hands(Vector3.Forward), s, where, _t, Dt);
        FlightModel.Step(ref state, PilotInput.Hands(Vector3.Forward, boost: true), s, where, _t, Dt);
        Check(burstEnded && refused && state.Boosting && max <= s.TopSpeed + 1e-3f,
            $"boost lasts {_t.Boost.Duration} s, is refused during its {_t.Boost.Cooldown} s cooldown, fires again after it, and never passes top speed");
    }

    /// <summary>
    /// Combat is dodging: a small vessel must strafe more than its own length in the time a
    /// capital gun's 300 m/s shot takes to cross 1000 m. Checked from 70 m/s forward flight.
    /// </summary>
    void CheckJink(VesselLoadout v, HandlingStats s, FlightConditions where)
    {
        float length = v.Blocks.Max(b => b.Center.Z + b.Size.Z / 2f) - v.Blocks.Min(b => b.Center.Z - b.Size.Z / 2f);
        float Dodge(float seconds, bool boost)
        {
            var state = FlightState.At(Basis.Identity);
            state.Velocity = new Vector3(0f, 0f, -s.TopSpeed);
            float x = 0f;
            for (int i = 0; i < Mathf.RoundToInt(seconds / Dt); i++)
            {
                FlightModel.Step(ref state, PilotInput.Hands(Vector3.Right, boost: boost), s, where, _t, Dt);
                x += state.Velocity.X * Dt;
            }
            return x;
        }
        float flight = 1000f / 300f, worst = 800f / 350f;
        float plain = Dodge(flight, false), boosted = Dodge(flight, true), tight = Dodge(worst, false);
        Check(plain > length && tight > length,
            $"jink: strafing, the {length:F1} m fighter moves {plain:F0} m sideways ({boosted:F0} m with boost) while a 300 m/s shot crosses 1000 m ({flight:F2} s); {tight:F0} m against 350 m/s at 800 m ({worst:F2} s)");
    }

    // ==== The physics module ===============================================================

    IEnumerable<int> Scenarios()
    {
        GD.Print("-- physics module --");
        foreach (var x in HoverBumps()) yield return x;
        foreach (var x in HoverStep(lookAhead: false)) yield return x;
        foreach (var x in HoverStep(lookAhead: true)) yield return x;
        foreach (var x in HoverLake()) yield return x;
        foreach (var x in HoverJump()) yield return x;
        foreach (var x in HoverSlopes()) yield return x;
        foreach (var x in GearAndLanding()) yield return x;
        foreach (var x in LowSpeedBump()) yield return x;
        foreach (var x in HighSpeedImpacts()) yield return x;
        foreach (var x in Ramming()) yield return x;
        foreach (var x in LopsidedInFlight()) yield return x;
        foreach (var x in Determinism()) yield return x;
        foreach (var x in Performance()) yield return x;
    }

    void ParkAll()
    {
        _rover.PlaceLanded(new Vector3(3000f, 1.5f, 3000f), 0f);
        _fighter.PlaceLanded(new Vector3(3100f, 2.4f, 3000f), 0f);
        _capital.PlaceLanded(new Vector3(3300f, 11f, 3000f), 0f);
        foreach (var v in new[] { _rover, _fighter, _capital })
        {
            v.Input = PilotInput.Autopilot;
            v.ResetDamage();
        }
        _impacts.Clear();
        _substeps = 8;
    }

    /// <summary>Mean height of the hover engines above the true ground or water under them.</summary>
    static float TrueClearance(Vessel v)
    {
        float sum = 0f;
        foreach (var e in v.Structure.HoverEngines)
        {
            Vector3 p = v.GlobalTransform * e.Position;
            sum += p.Y - PlanetSurface.SurfaceAt(p.X, p.Z);
        }
        return sum / v.Structure.HoverEngines.Count;
    }

    IEnumerable<int> HoverBumps()
    {
        ParkAll();
        float ride = _rover.Stats.RideHeight;
        _rover.Place(new Vector3(70f, PlanetSurface.GroundAt(70f, PlanetSurface.BumpLaneZ) + ride, PlanetSurface.BumpLaneZ), -90f);
        _rover.State.Velocity = new Vector3(30f, 0f, 0f);
        float worst = 0f, speed = 0f, lift = 0f;
        int samples = 0;
        while (_rover.GlobalPosition.X < 320f && samples < 2000)
        {
            _rover.Input = PilotInput.Hands(Vector3.Forward * 0.6f); // 30 m/s
            yield return 0;
            if (_rover.GlobalPosition.X < 100f) continue; // past the strip's edge, at speed
            worst = Mathf.Max(worst, Mathf.Abs(TrueClearance(_rover) - ride));
            lift = Mathf.Max(lift, _rover.Hover.LookAheadLift);
            speed += _rover.Speed;
            samples++;
        }
        Check(samples > 100 && worst <= 0.25f && _impacts.Count == 0,
            $"hover rover at {speed / Mathf.Max(samples, 1):F0} m/s over ±{PlanetSurface.BumpHeight} m bumps holds ride height within ±{worst:F2} m (limit ±0.25; look-ahead lifted it at most {lift:F2} m)");
    }

    IEnumerable<int> HoverStep(bool lookAhead)
    {
        ParkAll();
        var (stepX, height) = PlanetSurface.Steps[1];
        float ride = _rover.Stats.RideHeight;
        _rover.Hover.LookAhead = lookAhead;
        _rover.Place(new Vector3(stepX - 35f, ride, PlanetSurface.StepLaneZ), -90f);
        _rover.State.Velocity = new Vector3(15f, 0f, 0f);
        float slowest = float.MaxValue;
        for (int i = 0; i < 240; i++)
        {
            _rover.Input = PilotInput.Hands(Vector3.Forward * 0.3f); // 15 m/s
            yield return 0;
            slowest = Mathf.Min(slowest, _rover.Speed);
        }
        _rover.Hover.LookAhead = true;
        bool past = _rover.GlobalPosition.X > stepX + PlanetSurface.StepLength + 10f;
        bool snagged = _impacts.Count > 0 || slowest < 12f || !past;
        if (lookAhead)
            Check(!snagged && _rover.Damage == 0f,
                $"with look-ahead the rover rises early and rides over a {height} m rock step at 15 m/s (slowest {slowest:F1} m/s, no contact)");
        else
            Check(snagged, $"without look-ahead the same {height} m step snags it ({_impacts.Count} contact(s), slowest {slowest:F1} m/s): the look-ahead is what clears it");
    }

    IEnumerable<int> HoverLake()
    {
        ParkAll();
        float ride = _rover.Stats.RideHeight;
        Vector2 c = PlanetSurface.LakeCenter;
        _rover.Place(new Vector3(c.X + PlanetSurface.LakeRadius + 25f, PlanetSurface.GroundAt(c.X + PlanetSurface.LakeRadius + 25f, c.Y) + ride, c.Y), 90f);
        float low = float.MaxValue, high = float.MinValue;
        int over = 0;
        for (int i = 0; i < 60 * 9; i++)
        {
            _rover.Input = PilotInput.Hands(Vector3.Forward * 0.6f);
            yield return 0;
            float fromCenter = new Vector2(_rover.GlobalPosition.X, _rover.GlobalPosition.Z).DistanceTo(c);
            if (fromCenter > PlanetSurface.LakeRadius - 15f) continue; // deep water only
            float above = _rover.GlobalPosition.Y - PlanetSurface.WaterLevel;
            low = Mathf.Min(low, above);
            high = Mathf.Max(high, above);
            over++;
        }
        Check(over > 120 && low >= ride - 0.25f && high <= ride + 0.25f,
            $"across the lake at 30 m/s it rides {low:F2}-{high:F2} m above the water for {over * Dt:F1} s: full height, never sinking");
    }

    IEnumerable<int> HoverJump()
    {
        ParkAll();
        float ride = _rover.Stats.RideHeight;
        _rover.Place(new Vector3(-20f, ride, 0f), -90f);
        _rover.State.Velocity = new Vector3(15f, 0f, 0f);
        for (int i = 0; i < 60; i++)
        {
            _rover.Input = PilotInput.Hands(Vector3.Forward * 0.3f);
            yield return 0;
        }
        // Hop, and take a knock as it leaves the ground (as if it clipped a rock).
        _rover.Input = PilotInput.Hands(Vector3.Forward * 0.3f);
        _rover.Input.Jump = true;
        yield return 0;
        _rover.State.Orientation = new Basis(Vector3.Right, 0.3f) * new Basis(Vector3.Back, 0.35f) * _rover.State.Orientation;
        float tilt0 = Mathf.RadToDeg(_rover.State.Orientation.Y.AngleTo(Vector3.Up)), peak = 0f;
        for (int i = 0; i < 60; i++)
        {
            _rover.Input = PilotInput.Hands(Vector3.Forward * 0.3f);
            yield return 0;
            peak = Mathf.Max(peak, _rover.Hover.Clearance);
        }
        float tilt = Mathf.RadToDeg(_rover.GlobalBasis.Y.AngleTo(Vector3.Up));
        Check(peak > ride + 1f && tilt < 3f,
            $"after a jump ({peak - ride:F1} m up) and a {tilt0:F0}° knock the rover is level again within 1 s ({tilt:F1}°)");
    }

    IEnumerable<int> HoverSlopes()
    {
        foreach (var (z, degrees, climbs) in new[] { (PlanetSurface.SteepRampZ, PlanetSurface.SteepRampDegrees, false), (PlanetSurface.GentleRampZ, PlanetSurface.GentleRampDegrees, true) })
        {
            ParkAll();
            float ride = _rover.Stats.RideHeight;
            _rover.Place(new Vector3(PlanetSurface.RampStartX - 40f, ride, z), -90f);
            bool refused = false;
            float top = 0f;
            for (int i = 0; i < 60 * 6; i++)
            {
                _rover.Input = PilotInput.Hands(Vector3.Forward * 0.4f);
                yield return 0;
                refused |= _rover.Hover.SlopeRefused;
                top = Mathf.Max(top, _rover.GlobalPosition.Y);
            }
            if (climbs)
                Check(top > PlanetSurface.RampTop + ride - 0.3f && !refused,
                    $"it climbs a {degrees}° ramp onto the mesa ({top - ride:F1} m up)");
            else
                Check(refused && top < ride + 1f && _rover.GlobalPosition.X < PlanetSurface.RampStartX + 1f,
                    $"it refuses a {degrees}° ramp (limit {_t.Hover.MaxSlope}°): stops at its foot, highest point {top - ride:F2} m up");
        }
    }

    IEnumerable<int> GearAndLanding()
    {
        ParkAll();
        _fighter.Place(new Vector3(0f, 32f, 0f), 0f);
        bool startedUp = !_fighter.Gear.Down;
        int i = 0, landedBefore = _landed;
        float deployedAt = float.NaN, touchdown = 0f;
        while (!_fighter.Frozen && i++ < 60 * 20)
        {
            // Ease down at 5 m/s until the feet touch, then hands off.
            _fighter.Input = _fighter.Gear.Contact ? PilotInput.LetGo : PilotInput.Hands(Vector3.Down * 0.07f);
            if (_fighter.Gear.Contact && touchdown == 0f) touchdown = -_fighter.State.Velocity.Y;
            yield return 0;
            if (float.IsNaN(deployedAt) && _fighter.Gear.Down) deployedAt = _fighter.Gear.FootAltitude;
        }
        Check(startedUp && !float.IsNaN(deployedAt) && !_fighter.Gear.Manual,
            $"landing gear deploys by itself on the way down ({deployedAt:F1} m above the ground, below {_t.Gear.DeployAltitude} m and slower than {_t.Gear.DeploySpeed} m/s)");

        long ticks = _fighter.SimulatedTicks;
        Vector3 at = _fighter.GlobalPosition;
        for (int k = 0; k < 60; k++)
        {
            _fighter.Input = PilotInput.LetGo;
            yield return 0;
        }
        Check(_fighter.Frozen && _landed == landedBefore + 1 && _fighter.SimulatedTicks == ticks && _fighter.GlobalPosition == at && _fighter.Damage == 0f,
            $"touching down at {touchdown:F1} m/s it settles, raises Landed and stops simulating (no ticks, not a millimetre, no damage)");

        int lifted = _liftedOff;
        for (int k = 0; k < 60; k++)
        {
            _fighter.Input = PilotInput.Hands(Vector3.Up);
            yield return 0;
        }
        Check(!_fighter.Frozen && _liftedOff == lifted + 1 && _fighter.GlobalPosition.Y > at.Y + 2f,
            $"thrust wakes it: LiftedOff, and 1 s later it is {_fighter.GlobalPosition.Y - at.Y:F1} m up");

        // Land again, load it past its lift, and try to take off.
        while (!_fighter.Frozen && i++ < 60 * 40)
        {
            _fighter.Input = _fighter.Gear.Contact ? PilotInput.LetGo : PilotInput.Hands(Vector3.Down * 0.07f);
            yield return 0;
        }
        _fighterBuild.AddCargo(50f);
        lifted = _liftedOff;
        Vector3 parked = _fighter.GlobalPosition;
        for (int k = 0; k < 60; k++)
        {
            _fighter.Input = PilotInput.Hands(Vector3.Up);
            yield return 0;
        }
        Check(_fighter.Frozen && _liftedOff == lifted && _fighter.GlobalPosition == parked && _fighter.ConsoleMessage?.Contains("TOO HEAVY") == true,
            $"with 50 t aboard (lift margin {_fighter.Stats.LiftMargin:F0} kN) full thrust can't lift it, and the console says \"{_fighter.ConsoleMessage}\"");
        _fighterBuild.AddCargo(-50f);
    }

    IEnumerable<int> LowSpeedBump()
    {
        ParkAll();
        Vector3 wall = PlanetSurface.WallCenter;
        float face = wall.Z + PlanetSurface.WallSize.Z / 2f;
        _fighter.Place(new Vector3(wall.X - 15f, 10f, face + 12f), 0f);
        _fighter.State.Velocity = new Vector3(4f, 0f, -4f); // 5.7 m/s, 45° into the wall
        float startX = _fighter.GlobalPosition.X;
        for (int i = 0; i < 60 * 6; i++)
        {
            _fighter.Input = PilotInput.Autopilot; // holds course: whatever the contact leaves it
            yield return 0;
        }
        ImpactInfo hit = _impacts.FirstOrDefault();
        Check(hit != null && hit.Damage == 0f && _fighter.Damage == 0f && hit.CraterRadius == 0f
              && _fighter.State.Velocity.X > 3.5f && _fighter.GlobalPosition.X - startX > 15f,
            $"a {hit?.Speed ?? 0f:F1} m/s nudge into the wall does no damage; it glances off and slides along at {_fighter.State.Velocity.X:F1} m/s");
    }

    IEnumerable<int> HighSpeedImpacts()
    {
        var results = new List<(string what, float mass, float speed, ImpactInfo hit)>();
        foreach (var (vessel, speed) in new[] { (_fighter, 40f), (_fighter, 60f), (_capital, 40f) })
        {
            ParkAll();
            int craters = _physics.Craters.Count;
            vessel.Place(new Vector3(0f, 45f, 20f), 0f);
            vessel.State.Velocity = new Vector3(0f, -speed, 0f);
            for (int i = 0; i < 90; i++)
            {
                vessel.Input = PilotInput.Autopilot;
                yield return 0;
            }
            ImpactInfo hit = _impacts.FirstOrDefault(x => x.HitGround);
            results.Add((vessel.Structure.Name, vessel.Stats.Mass, speed, hit));
            if (hit == null || _physics.Craters.Count != craters + 1)
            {
                Fail($"{vessel.Structure.Name} at {speed} m/s: no ground impact or no crater recorded");
                yield break;
            }
        }

        bool formula = results.All(r => Mathf.Abs(r.hit.Damage - ImpactRules.Damage(r.mass, r.hit.Speed, _t)) < 1e-3f * r.hit.Damage
                                        && Mathf.Abs(r.hit.Speed - r.speed) < 0.5f);
        var (_, fm, _, slow) = results[0];
        var (_, _, _, fast) = results[1];
        var (_, cm, _, big) = results[2];
        float speedRatio = fast.Damage / slow.Damage, expectedSpeedRatio = Mathf.Pow((60f - 8f) / (40f - 8f), 2f);
        float massRatio = big.Damage / slow.Damage;
        Check(formula && Mathf.Abs(speedRatio - expectedSpeedRatio) < 0.05f * expectedSpeedRatio && Mathf.Abs(massRatio - cm / fm) < 0.02f * cm / fm,
            $"impact damage = mass × (speed − {_t.Contacts.SafeSpeed})² × {_t.Contacts.DamageScale}: fighter {slow.Damage:F1} at 40 m/s, {fast.Damage:F1} at 60 m/s (×{speedRatio:F2}); capital {big.Damage:F0} at 40 m/s (×{massRatio:F1} = mass ratio)");
        float cap = _t.Contacts.CraterMaxRadius;
        Check(slow.CraterRadius > 0f && fast.CraterRadius > slow.CraterRadius && big.CraterRadius == cap,
            $"high-speed ground hits blast craters ({slow.CraterRadius:F1} m, {fast.CraterRadius:F1} m), and the capital ship's is capped at {cap} m");
    }

    IEnumerable<int> Ramming()
    {
        ParkAll();
        _substeps = 1;
        _capital.PlaceLanded(new Vector3(0f, 11f, -60f), 0f);
        _fighter.Place(new Vector3(0f, 10f, 10f), 0f);
        _fighter.State.Velocity = new Vector3(0f, 0f, -30f);
        float shove = 0f;
        bool resettled = false;
        int landed = _landed;
        for (int i = 0; i < 240; i++)
        {
            _fighter.Input = PilotInput.Autopilot;
            yield return 0;
            if (!_capital.Frozen) shove = Mathf.Max(shove, _capital.Speed);
            resettled |= shove > 0f && _capital.Frozen;
        }
        ImpactInfo ram = _impacts.FirstOrDefault(x => x.Other != null);
        float mu = ImpactRules.ReducedMass(_fighter.Stats.Mass, _capital.Stats.Mass);
        Check(ram != null && _fighter.Damage > 0f && Mathf.IsEqualApprox(_fighter.Damage, _capital.Damage)
              && Mathf.Abs(ram.Damage - ImpactRules.Damage(mu, ram.Speed, _t)) < 1e-3f,
            $"ramming the parked capital at {ram?.Speed ?? 0f:F0} m/s hurts both sides: {_fighter.Damage:F1} damage each, {_fighter.Damage / _fighter.HullPoints * 100f:F0}% of the fighter's hull and {_capital.Damage / _capital.HullPoints * 100f:F1}% of the capital's");
        Check(shove > 0f && resettled && _landed == landed + 1,
            $"the rammed capital is knocked loose ({shove:F2} m/s), then settles on its gear again");
    }

    IEnumerable<int> LopsidedInFlight()
    {
        ParkAll();
        _fighterBuild.MakeLopsided();
        _fighter.Place(new Vector3(0f, 80f, 0f), 0f);
        Basis start = _fighter.GlobalBasis;
        for (int i = 0; i < 180; i++)
        {
            _fighter.Input = PilotInput.Hands(new Vector3(0.5f, 0.3f, -1f), boost: i == 0);
            yield return 0;
        }
        Check(_fighter.State.AngularVelocity == Vector3.Zero && _fighter.GlobalBasis.IsEqualApprox(start) && _fighter.Speed > 40f,
            $"flown lopsided through the physics module for 3 s at up to {_fighter.Speed:F0} m/s, it never turns a hair");
        _fighterBuild.CopyFrom(VesselDesigns.Fighter(_t));
    }

    IEnumerable<int> Determinism()
    {
        var runs = new List<List<Vector3>>();
        for (int run = 0; run < 2; run++)
        {
            ParkAll();
            _fighter.Place(new Vector3(-60f, 25f, 120f), 30f);
            _rover.Place(new Vector3(80f, PlanetSurface.GroundAt(80f, PlanetSurface.BumpLaneZ) + 1.5f, PlanetSurface.BumpLaneZ), -90f);
            var path = new List<Vector3>();
            for (int i = 0; i < 360; i++)
            {
                float t = i * Dt;
                _fighter.Input = PilotInput.Hands(new Vector3(Mathf.Sin(t), 0.2f, -1f), new Vector3(0.3f * Mathf.Sin(2f * t), 0.5f, Mathf.Cos(t)), boost: i == 90);
                _rover.Input = PilotInput.Hands(new Vector3(0.3f * Mathf.Sin(t * 3f), 0f, -1f), new Vector3(0f, 0.2f * Mathf.Sin(t), 0f));
                _rover.Input.Jump = i == 200;
                yield return 0;
                path.Add(_fighter.GlobalPosition);
                path.Add(_rover.GlobalPosition);
                path.Add(_fighter.GlobalBasis.Z);
            }
            runs.Add(path);
        }
        float worst = runs[0].Zip(runs[1], (a, b) => a.DistanceTo(b)).Max();
        Check(worst == 0f, $"the same inputs give the same trajectory: fighter and rover flown twice for 6 s match exactly (max difference {worst} m)");
    }

    IEnumerable<int> Performance()
    {
        ParkAll();
        _substeps = 1;
        var crowd = new List<Vessel>();
        for (int i = 0; i < 20; i++)
        {
            int kind = i % 3;
            VesselLoadout build = VesselDesigns.Create(kind, _t);
            Vector3 at = kind switch
            {
                0 => new Vector3(70f + i * 12f, PlanetSurface.GroundAt(70f + i * 12f, PlanetSurface.BumpLaneZ + 10f) + 1.5f, PlanetSurface.BumpLaneZ + 10f),
                1 => new Vector3(-200f + i * 25f, 60f, 200f),
                _ => new Vector3(-250f + i * 40f, 150f, -100f),
            };
            Vessel v = _physics.Spawn(build, at, -90f);
            crowd.Add(v);
        }
        for (int i = 0; i < 30; i++)
        {
            foreach (var v in crowd) v.Input = PilotInput.Hands(Vector3.Forward * 0.5f, new Vector3(0f, 0.3f, 0f));
            yield return 0;
        }
        double total = 0, worst = 0;
        const int ticks = 180;
        for (int i = 0; i < ticks; i++)
        {
            foreach (var v in crowd) v.Input = PilotInput.Hands(Vector3.Forward * 0.5f, new Vector3(0f, 0.3f, 0f));
            yield return 0;
            total += _physics.LastStepMs;
            worst = Math.Max(worst, _physics.LastStepMs);
        }
        GD.Print($"INFO  20 moving vessels (7 hover rovers, 7 fighters, 6 capital ships) + 3 parked: {total / ticks:F3} ms per physics tick on average, worst {worst:F3} ms");
        foreach (var v in crowd) _physics.Remove(v);
    }

    // ==== Reporting ========================================================================

    void Check(bool ok, string what)
    {
        if (ok) GD.Print($"PASS  {what}");
        else Fail(what);
    }

    void Fail(string what)
    {
        _failures++;
        GD.PrintErr($"FAIL  {what}");
    }
}
