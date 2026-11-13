using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;
using PhysicsSandbox.World;
using ShipHandling.Handling;
using TerrainDigging.Terrain;
using Weather.Forces;
using Weather.Model;
using N = System.Numerics;
using SInt3 = GalHack.Structures.Int3;
using Structure = GalHack.Structures.Structure;
using TInt3 = TerrainDigging.Terrain.Int3;

namespace PhysicsSandbox;

/// <summary>
/// Headless tests of the shared physics module, through the integration sandbox. Run with:
///   .\tools\test.ps1 -Project prototypes/physics-sandbox -Scene res://tests/PhysicsTest.tscn
/// (test-all passes --fixed-fps 60 from PhysicsTest.args, so it runs as fast as the machine can.)
///
/// The pure rules are checked first with no engine. Then two identical worlds in their own
/// physics spaces prove determinism, and one sandbox world runs every scenario for real: one
/// physics tick per frame, exactly as in play. Exits 0 if every check passes, 1 if any fails.
/// </summary>
public partial class PhysicsTest : Node
{
    const float Dt = 1f / 60f;

    SandboxWorld _w;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly List<ImpactInfo> _impacts = new();
    readonly List<CraterInfo> _craters = new();
    readonly List<RescueInfo> _rescues = new();
    int _landed;

    public override void _Ready()
    {
        ProcessPhysicsPriority = 100; // check after the physics host (-10) and the world's game logic (0)
        GD.Print("-- pure rules --");
        PureChecks();
        _run = Scenarios().GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        bool more;
        try
        {
            more = _run.MoveNext();
        }
        catch (Exception e)
        {
            Fail($"scenario threw: {e}");
            more = false;
        }
        if (more) return;
        GD.Print(_failures == 0
            ? $"ALL PASSED ({_passes} checks in {_clock.Elapsed.TotalSeconds:F1} s)"
            : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        SetPhysicsProcess(false);
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    IEnumerable<int> Scenarios()
    {
        GD.Print("-- determinism: two identical worlds, side by side --");
        foreach (int x in Determinism()) yield return x;

        GD.Print("-- the sandbox --");
        _w = new SandboxWorld { Visuals = false };
        AddChild(_w);
        _w.Physics.Impact += _impacts.Add;
        _w.Physics.Cratered += _craters.Add;
        _w.Physics.Rescued += _rescues.Add;
        _w.Physics.Landed += _ => _landed++;
        _w.Weather.Calm = true;
        for (int i = 0; i < 5; i++) yield return 0;

        StructureChecks();
        foreach (int x in MultiCellBlocks()) yield return x;
        foreach (int x in ForcePath()) yield return x;
        foreach (int x in NoAutoOrientation()) yield return x;
        foreach (int x in DamageAndCraters()) yield return x;
        foreach (int x in Severing()) yield return x;
        foreach (int x in HullRider()) yield return x;
        foreach (int x in LandingOnMovingHull()) yield return x;
        foreach (int x in CabinRider()) yield return x;
        foreach (int x in SpaceWalk()) yield return x;
        foreach (int x in Airlock()) yield return x;
        foreach (int x in HullWalls()) yield return x;
        foreach (int x in StepsEverywhere()) yield return x;
        foreach (int x in RoverTrench()) yield return x;
        foreach (int x in HoverHoles()) yield return x;
        foreach (int x in GearOnUnevenGround()) yield return x;
        foreach (int x in WindRules()) yield return x;
        foreach (int x in WaterVolume()) yield return x;
        foreach (int x in TornadoFling()) yield return x;
        foreach (int x in StuckSafety()) yield return x;
        foreach (int x in Performance()) yield return x;
    }

    // ==== Pure rules =============================================================================

    void PureChecks()
    {
        var rules = new PhysicsRules();

        // Rotation conventions: a world turn goes on the left, a local turn on the right.
        N.Quaternion pitchUp = MathX.AxisAngle(N.Vector3.UnitX, MathF.PI / 2f);
        N.Quaternion yawLeft = MathX.AxisAngle(N.Vector3.UnitY, MathF.PI / 2f);
        N.Vector3 worldFirst = MathX.Forward(MathX.ThenWorld(pitchUp, yawLeft));
        N.Vector3 localFirst = MathX.Forward(MathX.ThenLocal(yawLeft, pitchUp));
        Check(N.Vector3.Distance(worldFirst, N.Vector3.UnitY) < 1e-5f && N.Vector3.Distance(localFirst, N.Vector3.UnitY) < 1e-5f
              && MathF.Abs(MathX.Heading(yawLeft) - MathF.PI / 2f) < 1e-5f,
            "rotation conventions: world turns compose on the left, local on the right, heading reads the twist about up");

        // The one damage formula, everywhere, never capped.
        bool formula = true;
        float[] masses = { 60f, 100f, 3670f, 8797f, 970_000f };
        float[] speeds = { 0f, 8f, 8.5f, 20f, 70f, 300f, 2000f };
        foreach (float m in masses)
            foreach (float v in speeds)
            {
                float expected = m / 1000f * MathF.Pow(MathF.Max(0f, v - 8f), 2f) * 0.001f;
                formula &= MathF.Abs(Impacts.Damage(m, v, rules.Contacts) - expected) <= 1e-6f * MathF.Max(1f, expected);
            }
        float huge = Impacts.Damage(8797f, 2000f, rules.Contacts);
        float hull = Impacts.Integrity(BodyKind.Vessel, 8797f, rules.Integrity);
        Check(formula && Impacts.Damage(8797f, 8f, rules.Contacts) == 0f && huge > 500f * hull,
            $"impact damage is mass (t) × (speed − 8)² × 0.001 for every mass and speed, zero at 8 m/s and uncapped (2000 m/s does {huge:F0}, {huge / hull:F0}× the hull)");
        Check(MathF.Abs(Impacts.ReducedMass(8797f, 3670f) - 8797f * 3670f / (8797f + 3670f)) < 1e-2f
              && Impacts.ReducedMass(100f, float.PositiveInfinity) == 100f,
            "ramming uses the reduced mass m1·m2/(m1+m2); a fixed obstacle leaves the body's own mass");

        float small = Impacts.CraterRadius(8797f, 30f, rules.Craters), big = Impacts.CraterRadius(970_000f, 60f, rules.Craters);
        float uncapped = 2f * MathF.Cbrt(Impacts.EnergyMJ(970_000f, 60f));
        Check(Impacts.CraterRadius(8797f, 24f, rules.Craters) == 0f && small > 1f && small < 6f && big == 6f && uncapped > 20f
              && Impacts.CraterRadius(60f, 40f, rules.Craters) == 0f,
            $"craters: none below 25 m/s or under 1 m, 2·∛MJ above ({small:F2} m for the Corsair at 30 m/s), capped at the terrain's 6 m (a 970 t ship at 60 m/s would be {uncapped:F0} m)");

        // Integrity is per tonne, and a player lands on their legs: their own safe speed, the same
        // formula. Small hops, jetpack landings and drops up to about 12 m never hurt; only a
        // genuinely high fall does.
        float player = Impacts.Integrity(BodyKind.Player, 100f, rules.Integrity);
        float Fall(float metres) => Impacts.Damage(100f, MathF.Sqrt(2f * 9.8f * metres), rules.Contacts, BodyKind.Player) / player;
        float safe = rules.Contacts.PlayerSafeSpeed, safeDrop = safe * safe / (2f * 9.8f);
        float excess = MathF.Sqrt(2f * 9.8f * 30f) - safe;
        bool sameFormula = MathF.Abs(Impacts.Damage(100f, MathF.Sqrt(2f * 9.8f * 30f), rules.Contacts, BodyKind.Player) - 0.1f * excess * excess * 0.001f) < 1e-7f
                           && Impacts.Damage(8797f, 12f, rules.Contacts, BodyKind.Vessel) == Impacts.Damage(8797f, 12f, rules.Contacts);
        Check(MathF.Abs(hull - 8.797f * 4f) < 1e-4f && Fall(1.5f) == 0f && Fall(12f) == 0f && safeDrop > 11.9f && sameFormula
              && Fall(30f) > 0.08f && Fall(30f) < 0.18f && Fall(50f) > 0.3f && Fall(50f) < 0.5f && Fall(70f) < 1f && Fall(90f) > 1f,
            $"toughness is per tonne (the Corsair has {hull:F1} hull points), and a player lands on their legs: no damage up to {safe:0.#} m/s, " +
            $"a {safeDrop:F1} m drop; a 30 m fall costs {Fall(30f):P0} of their health, 50 m {Fall(50f):P0}, 70 m {Fall(70f):P0}; 90 m kills (the same formula, vessels still safe to {rules.Contacts.SafeSpeed:0.#} m/s)");

        // Greedy boxes cover exactly the solid cells.
        var rng = new Random(7);
        bool covered = true;
        for (int trial = 0; trial < 20; trial++)
        {
            var solid = new bool[16 * 16 * 16];
            for (int i = 0; i < solid.Length; i++) solid[i] = rng.NextDouble() < (trial % 2 == 0 ? 0.3 : 0.85);
            var boxes = StructureCollision.MergeBoxes(solid);
            var hit = new int[solid.Length];
            foreach (var (x0, y0, z0, x1, y1, z1) in boxes)
                for (int z = z0; z < z1; z++)
                    for (int y = y0; y < y1; y++)
                        for (int x = x0; x < x1; x++)
                            hit[x + 16 * (y + 16 * z)]++;
            for (int i = 0; i < solid.Length; i++) covered &= hit[i] == (solid[i] ? 1 : 0);
        }
        Check(covered, "the greedy box merge covers every solid cell exactly once and no empty one (20 random chunks)");

        // Riding conversions round-trip.
        var ship = new Body { Mass = 1000f, Position = new N.Vector3(5, 50, -3), Rotation = MathX.AxisAngle(N.Vector3.Normalize(new N.Vector3(1, 2, 3)), 0.7f),
            Velocity = new N.Vector3(20, -3, 4), AngularVelocity = new N.Vector3(0.2f, 0.5f, -0.3f) };
        var rider = new Body { Mass = 100f, Position = new N.Vector3(6, 52, -1), Velocity = new N.Vector3(1, 2, 3) };
        rider.Rotation = MathX.AxisAngle(N.Vector3.Normalize(new N.Vector3(-2, 1, 1)), 0.4f);
        N.Vector3 p0 = rider.WorldPosition, v0 = rider.WorldVelocity;
        N.Quaternion q0 = rider.WorldRotation;
        Riding.EnterCabin(rider, ship);
        N.Vector3 p1 = rider.WorldPosition, v1 = rider.WorldVelocity;
        N.Quaternion q1 = rider.WorldRotation;
        Riding.LeaveCabin(rider);
        Check(N.Vector3.Distance(p0, p1) < 1e-4f && N.Vector3.Distance(v0, v1) < 1e-4f && N.Vector3.Distance(v0, rider.Velocity) < 1e-4f
              && MathF.Abs(N.Quaternion.Dot(q0, q1)) > 0.99999f && MathF.Abs(N.Quaternion.Dot(q0, rider.Rotation)) > 0.99999f,
            "joining a ship's cabin world and letting go both keep world position, velocity (spin included) and orientation");

        // Letting go: a jump at once; out of contact only after the grace time; never inside.
        var riding = new PhysicsRules.RidingData();
        Riding.EnterCabin(rider, ship);
        rider.OnOuterHull = true;
        bool held = true;
        for (float t = 0f; t < riding.LetGoAfter - 0.05f; t += 1f / 60f) held &= !Riding.LetGo(riding, rider, false, false, 0.3f, 1f / 60f);
        bool touchedAgain = !Riding.LetGo(riding, rider, false, true, 0.1f, 1f / 60f) && Riding.OutOfContactFor(rider) == 0f;
        bool lost = false;
        for (float t = 0f; t < riding.LetGoAfter + 0.05f; t += 1f / 60f) lost |= Riding.LetGo(riding, rider, false, false, 0.3f, 1f / 60f);
        bool far = Riding.LetGo(riding, rider, false, true, riding.LetGoDistance + 0.1f, 1f / 60f);
        bool jump = Riding.LetGo(riding, rider, true, true, 0f, 1f / 60f);
        rider.OnOuterHull = false;
        bool inside = !Riding.LetGo(riding, rider, true, false, 9f, 10f);
        Check(held && touchedAgain && lost && far && jump && inside,
            $"letting go of a hull: a jump at once, {riding.LetGoAfter:0.0#} s out of contact or {riding.LetGoDistance:0.0#} m off it, never from inside");

        // Turning a walk frame onto a new up keeps the heading as near as it can.
        var walker = new Body { Rotation = MathX.Yaw(0.6f) };
        N.Vector3 forward = MathX.Forward(walker.Rotation);
        Riding.SetUp(walker, N.Vector3.UnitZ);
        N.Vector3 turnedUp = MathX.Rotate(walker.Rotation, N.Vector3.UnitY), turnedForward = MathX.Forward(walker.Rotation);
        N.Vector3 carried = N.Vector3.Normalize(MathX.Rotate(Riding.FromTo(N.Vector3.UnitY, N.Vector3.UnitZ), forward));
        Check(N.Vector3.Distance(turnedUp, N.Vector3.UnitZ) < 1e-4f && N.Vector3.Distance(walker.Up, N.Vector3.UnitZ) < 1e-4f
              && N.Vector3.Distance(turnedForward, carried) < 1e-3f,
            "turning onto a new up (a hull face) turns the walk frame by the smallest rotation, so forward stays forward");
    }

    void StructureChecks()
    {
        foreach (Ship s in new[] { _w.Corsair, _w.Rover })
        {
            StructureStats st = StructureStats.Compute(s.Structure);
            HandlingStats h = HandlingModel.Compute(s.Spec, new FlightConditions(9.8f, true), _w.Handling);
            bool massAgrees = MathF.Abs(s.Body.Mass - (float)st.Mass * 1000f) < 0.05f && MathF.Abs(h.Mass - (float)st.Mass) < 1e-4f;
            bool thrustAgrees = true;
            // Building's thrust per direction is the handling model's thrust per direction.
            (Direction d, Dir dir)[] map =
            {
                (Direction.Forward, Dir.NegZ), (Direction.Back, Dir.PosZ), (Direction.Up, Dir.PosY),
                (Direction.Down, Dir.NegY), (Direction.Left, Dir.NegX), (Direction.Right, Dir.PosX),
            };
            foreach (var (d, dir) in map) thrustAgrees &= MathF.Abs(h.Thrust[(int)d] - (float)st.Thrust[(int)dir]) < 1e-3f;
            Check(massAgrees && thrustAgrees,
                $"{s.Structure.Name} from its blueprint: body mass {s.Body.Mass:F0} kg = the blocks' {st.Mass:F3} t = the handling model's; " +
                $"thrust per direction is building's (fwd {h.Accel(Direction.Forward):F1} m/s², up {h.Accel(Direction.Up):F1}, turn {h.TurnRate:F0}°/s, lift {h.LiftMargin:+0;-0} kN)");
        }
        Ship c = _w.Corsair;
        Check(c.HullShapes.ShapeCount > 0 && c.HullShapes.ShapeCount < c.Structure.Count / 3 && c.Interior.Count > 300,
            $"the Corsair is one body of {c.HullShapes.ShapeCount} collider shapes for {c.Structure.Count} blocks, with {c.Interior.Count} interior cells to walk in");

        // One block edited: only its chunk's colliders are rebuilt (hull and cabin), never the whole body.
        Ship scratch = _w.SpawnVessel("corsair.gbp", new N.Vector3(90f, 150f, 90f), 0f, landed: false);
        int chunks = scratch.Structure.Chunks.Count, shapesBefore = scratch.HullShapes.ShapeCount;
        var cell = new SInt3(5, 3, 4);
        Building.Place(scratch.Structure, cell, Building.Make(scratch.Structure, scratch.Structure.Catalogue.Type("steel")));
        var watch = Stopwatch.StartNew();
        scratch.ApplyEdits();
        double ms = watch.Elapsed.TotalMilliseconds;
        Check(scratch.HullShapes.LastRebuilt == 1 && chunks >= 4 && scratch.HullShapes.ShapeCount >= shapesBefore,
            $"collider strategy: placing one block rebuilds the shapes of {scratch.HullShapes.LastRebuilt} of the structure's {chunks} chunks " +
            $"(hull and cabin together, mass and centre of mass refreshed) in {ms:F2} ms; the other chunks keep their shapes");
        _w.Remove(scratch);
    }

    // ==== Determinism ===========================================================================

    IEnumerable<int> Determinism()
    {
        var views = new SubViewport[2];
        var worlds = new SandboxWorld[2];
        for (int i = 0; i < 2; i++)
        {
            views[i] = new SubViewport { OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled, Size = new Vector2I(2, 2) };
            AddChild(views[i]);
            worlds[i] = new SandboxWorld { Visuals = false };
            views[i].AddChild(worlds[i]);
            worlds[i].Weather.Sim.Snap(WeatherKind.DustStorm);
        }
        for (int i = 0; i < 3; i++) yield return 0;

        double worst = 0;
        int ticks = 420, compared = 0;
        int craters = 0;
        worlds[0].Physics.Cratered += _ => craters++;
        for (int t = 0; t < ticks; t++)
        {
            foreach (SandboxWorld w in worlds) Script(w, t);
            yield return 0;
            IReadOnlyList<Body> a = worlds[0].Physics.Bodies, b = worlds[1].Physics.Bodies;
            if (a.Count != b.Count)
            {
                worst = double.PositiveInfinity;
                break;
            }
            for (int i = 0; i < a.Count; i++)
            {
                worst = Math.Max(worst, N.Vector3.Distance(a[i].Position, b[i].Position));
                worst = Math.Max(worst, N.Vector3.Distance(a[i].Velocity, b[i].Velocity));
                worst = Math.Max(worst, (a[i].Rotation - b[i].Rotation).Length());
                compared++;
            }
        }
        bool sameTerrain = worlds[0].Store.EditStateHash() == worlds[1].Store.EditStateHash();
        float corsairTravel = N.Vector3.Distance(worlds[0].Corsair.Body.Position, new N.Vector3(0, 24, 0));
        Check(worst == 0 && sameTerrain && craters > 0 && corsairTravel > 50f,
            $"the same inputs give identical trajectories: two worlds in their own physics spaces, 7 s of a dust storm, " +
            $"the Corsair flown and crashed ({craters} crater), the rover driven, crates flung by a tornado: " +
            $"{compared} body-ticks compared, max difference {worst} (positions, velocities, rotations), terrain edits identical");
        foreach (SubViewport v in views) v.QueueFree();
        yield return 0;
    }

    /// <summary>The scripted inputs both determinism worlds get, tick for tick.</summary>
    static void Script(SandboxWorld w, int t)
    {
        Ship c = w.Corsair, r = w.Rover;
        float s = t * Dt;
        if (t < 240) c.Flight.Input = PilotInput.Hands(new Vector3(0.4f * Mathf.Sin(s), t < 60 ? 1f : 0.2f, -0.5f), new Vector3(0.1f, 0.5f * Mathf.Sin(s * 0.7f), 0f), boost: t == 90);
        else c.Flight.Input = PilotInput.Autopilot;
        if (t == 240) c.Body.Velocity = new N.Vector3(4f, -60f, 0f); // thrown at the ground: a crash, a crater
        r.Flight.Input = PilotInput.Hands(new Vector3(0.2f * Mathf.Sin(s * 2f), 0f, -0.8f), new Vector3(0f, 0.25f * Mathf.Sin(s), 0f));
        r.Flight.Input.Jump = t == 150;
        if (t == 60)
        {
            Tornado tornado = w.Weather.Sim.SpawnTornado(new Vector3(14f, 0f, 9f), new Vector3(14f, 0f, 20f));
            tornado.SkipFormation();
        }
    }

    // ==== The one force path ======================================================================

    sealed class TestPush : IForceProvider
    {
        public readonly Dictionary<Body, N.Vector3> Forces = new();

        public void ApplyForces(PhysicsWorld world, float dt)
        {
            foreach (var (b, f) in Forces) world.AddForce(b, f, ForceKind.Other);
        }
    }

    IEnumerable<int> ForcePath()
    {
        _w.Weather.Calm = false;
        _w.Weather.Sim.Snap(WeatherKind.DustStorm);
        Ship vessel = _w.SpawnVessel("corsair.gbp", new N.Vector3(0, 140, -40), 0f, landed: false);
        Ship wreck = _w.SpawnVessel("sandflea.gbp", new N.Vector3(25, 140, -40), 0f, landed: false);
        _w.Physics.MakeWreck(wreck.Body);
        Crate crate = Crate.Spawn(_w, new N.Vector3(-20, 140, -40), 0);
        Creature creature = Creature.Spawn(_w, new N.Vector3(-35, 140, -40));
        _w.Player.PlaceOutside(new N.Vector3(40, 140, -40));
        vessel.Flight.Input = PilotInput.Hands(new Vector3(0.5f, 0.3f, -1f), new Vector3(0f, 0.4f, 0f));
        var push = new TestPush();
        var probe = new N.Vector3(600f, 0f, 0f);
        foreach (Body b in new[] { vessel.Body, wreck.Body, crate.Body, creature.Body, _w.Player.Body }) push.Forces[b] = probe;
        _w.Physics.ForceProviders.Add(push);
        for (int i = 0; i < 3; i++) yield return 0;

        var bodies = new (string name, Body body)[]
        {
            ("vessel", vessel.Body), ("wreck", wreck.Body), ("crate", crate.Body), ("creature", creature.Body), ("player", _w.Player.Body),
        };
        var worst = new double[bodies.Length];
        var last = new (N.Vector3 v, N.Vector3 f, N.Vector3 j)[bodies.Length];
        for (int i = 0; i < bodies.Length; i++) last[i] = (Tracked(bodies[i].body), bodies[i].body.LastTotalForce, bodies[i].body.LastImpulse);
        for (int t = 0; t < 40; t++)
        {
            yield return 0;
            for (int i = 0; i < bodies.Length; i++)
            {
                Body b = bodies[i].body;
                N.Vector3 v = Tracked(b);
                // Kinematic bodies are integrated in this tick; free bodies by the engine's step
                // that followed the last tick, from that tick's sums.
                var (pv, pf, pj) = last[i];
                N.Vector3 f = b.Motion == GalHack.Physics.Motion.Kinematic ? b.LastTotalForce : pf;
                N.Vector3 j = b.Motion == GalHack.Physics.Motion.Kinematic ? b.LastImpulse : pj;
                N.Vector3 expected = pv + f / b.Mass * Dt + j / b.Mass;
                worst[i] = Math.Max(worst[i], N.Vector3.Distance(v, expected) / Math.Max(1.0, expected.Length()));
                last[i] = (v, b.LastTotalForce, b.LastImpulse);
            }
        }
        string detail = string.Join(", ", bodies.Select((x, i) =>
            $"{x.name} {x.body.Mass:F0} kg: {probe.X / x.body.Mass:F3} m/s² from the 600 N probe (error {worst[i]:E1})"));
        Check(worst.All(e => e < 1e-4), $"every body type accelerates at ΣF/m through the one force path (gravity, weather, thrust, legs, a probe): {detail}");
        N.Vector3 weather = crate.Body.LastForce(ForceKind.Weather), thrust = vessel.Body.LastForce(ForceKind.Thrust);
        Check(weather.Length() > 1f && thrust.Length() > vessel.Body.Mass * 9f && crate.Body.LastForce(ForceKind.Gravity).Y < -crate.Body.Mass * 9.7f,
            $"and the sums really hold all the sources: dust-storm wind on the crate {weather.Length():F0} N, the Corsair's thrust {thrust.Length() / 1000f:F1} kN, gravity {crate.Body.LastForce(ForceKind.Gravity).Y:F0} N");

        _w.Physics.ForceProviders.Remove(push);
        _w.Remove(vessel);
        _w.Remove(wreck);
        _w.Remove(crate);
        _w.Remove(creature);
        _w.Weather.Calm = true;
        _w.Player.PlaceOutside(_w.Ground(1.5f, 8.5f) + N.Vector3.UnitY * 0.05f);
        yield return 0;
    }

    /// <summary>
    /// A flying vessel never orients itself: left banked or pitched, hands off or on the autopilot,
    /// in air or in space, it keeps that attitude. Flown through the whole shared flight path (the
    /// handling model, the vessel controller, thrust against gravity), all six at once.
    /// </summary>
    IEnumerable<int> NoAutoOrientation()
    {
        var attitudes = new (string name, N.Quaternion turn)[]
        {
            ("40° of bank", MathX.AxisAngle(N.Vector3.UnitZ, Units.Radians(40f))),
            ("30° nose up", MathX.AxisAngle(N.Vector3.UnitX, Units.Radians(30f))),
            ("25° nose down and 60° of bank", N.Quaternion.Normalize(MathX.AxisAngle(N.Vector3.UnitX, Units.Radians(-25f)) * MathX.AxisAngle(N.Vector3.UnitZ, Units.Radians(60f)))),
        };
        var flown = new List<(string what, Ship ship, N.Quaternion start)>();
        for (int i = 0; i < attitudes.Length * 2; i++)
        {
            bool space = i % 2 == 1;
            var (name, turn) = attitudes[i / 2];
            float altitude = space ? _w.Physics.Zone.Ceiling + 150f : 160f;
            Ship ship = _w.SpawnVessel("corsair.gbp", new N.Vector3(-60f + i * 24f, altitude, -70f), 0f, landed: false);
            ship.Body.Rotation = MathX.ThenLocal(ship.Body.Rotation, turn);
            ship.Body.Velocity = MathX.Forward(ship.Body.Rotation) * 20f; // flying on at 20 m/s
            ship.Binding.Teleport();
            // Half of them with hands off the stick, half with nobody in the seat (the autopilot holds course).
            ship.Flight.Input = i / 2 % 2 == 0 ? PilotInput.LetGo : PilotInput.Autopilot;
            flown.Add(($"{name} in {(space ? "space" : "air")}, {(ship.Flight.Input.Seated ? "hands off" : "autopilot")}", ship, ship.Body.Rotation));
        }
        float worst = 0f;
        string worstWhat = "";
        for (int t = 0; t < 600; t++)
        {
            yield return 0;
            foreach (var (what, ship, start) in flown)
            {
                float off = AngleBetween(ship.Body.Rotation, start);
                if (off < worst) continue;
                worst = off;
                worstWhat = what;
            }
        }
        bool flying = flown.All(f => !f.ship.Vessel.Frozen && !f.ship.IsWreck);
        Check(flying && worst < 0.01f,
            $"no auto-orientation through the shared flight path: six Corsairs left banked or pitched (40° of bank, 30° nose up, 25° down and 60° banked), " +
            $"in air and in space, hands off or on the autopilot, keep that attitude for 10 s: worst drift {worst:F4}° ({worstWhat})");
        foreach (var (_, ship, _) in flown) _w.Remove(ship);
        yield return 0;
    }

    /// <summary>
    /// The angle between two orientations, degrees, from the sine of the half angle (an arccosine of
    /// their dot product can't resolve hundredths of a degree in single precision).
    /// </summary>
    static float AngleBetween(N.Quaternion a, N.Quaternion b)
    {
        N.Quaternion d = N.Quaternion.Normalize(a * N.Quaternion.Conjugate(b));
        float sinHalf = MathF.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z);
        return Units.Degrees(2f * MathF.Asin(MathF.Min(1f, sinHalf)));
    }

    /// <summary>The velocity each integrator integrates: a kinematic body's origin, a free body's centre of mass.</summary>
    static N.Vector3 Tracked(Body b) => b.Motion == GalHack.Physics.Motion.Kinematic ? b.Velocity : b.PointVelocity(b.CenterOfMassPosition);

    // ==== Damage and craters =====================================================================

    IEnumerable<int> DamageAndCraters()
    {
        // A hard landing on the hull, gear still up: the formula exactly, from the body's own mass.
        N.Vector3 spot = _w.Ground(-55f, -12f);
        Ship drop = _w.SpawnVessel("corsair.gbp", spot + new N.Vector3(0, 9f, 0), 0.4f, landed: false);
        drop.Body.Velocity = new N.Vector3(0f, -23f, 0f);
        _impacts.Clear();
        for (int i = 0; i < 60 && !_impacts.Any(x => x.Body == drop.Body); i++) yield return 0;
        ImpactInfo hit = _impacts.FirstOrDefault(x => x.Body == drop.Body);
        float expected = hit == null ? -1f : Impacts.Damage(drop.Body.Mass, hit.Speed, _w.Rules.Contacts);
        Check(hit != null && hit.HitGround && MathF.Abs(hit.Damage - expected) < 1e-5f && MathF.Abs(drop.Body.Damage - SumDamage(drop.Body)) < 1e-5f && hit.Speed > 15f,
            $"a {hit?.Speed:F1} m/s belly landing does {hit?.Damage:F2} damage = {drop.Body.Mass / 1000f:F2} t × ({hit?.Speed:F1} − 8)² × 0.001, and the hull counts exactly that ({drop.Body.Damage / drop.Body.Integrity:P0} of it)");
        _w.Remove(drop);

        // A dive far past what the hull can take: damage is never capped, and the ground cratered.
        spot = _w.Ground(-60f, 30f);
        float before = spot.Y;
        int craterCount = _craters.Count;
        Ship dive = _w.SpawnVessel("corsair.gbp", spot + new N.Vector3(0, 40f, 0), 0f, landed: false);
        dive.Body.Velocity = new N.Vector3(0f, -100f, 0f);
        int logBefore = _w.Terrain.Log.CellCount;
        _impacts.Clear();
        for (int i = 0; i < 60 && _craters.Count == craterCount; i++) yield return 0;
        hit = _impacts.FirstOrDefault(x => x.Body == dive.Body && x.HitGround);
        CraterInfo crater = _craters.Count > craterCount ? _craters[^1] : null;
        float formula = hit == null ? 0f : Impacts.Damage(dive.Body.Mass, hit.Speed, _w.Rules.Contacts);
        float uncappedRadius = hit == null ? 0f : 2f * MathF.Cbrt(Impacts.EnergyMJ(dive.Body.Mass, hit.Speed));
        Check(hit != null && MathF.Abs(hit.Damage - formula) < 1e-4f && dive.Body.Damage >= formula - 1e-4f && dive.Body.Damage > 1.5f * dive.Body.Integrity,
            $"a {hit?.Speed:F0} m/s dive does {hit?.Damage:F1} damage, {dive.Body.Damage / dive.Body.Integrity:F1}× the hull: never capped at what it had left");

        bool logged = crater != null;
        int inLog = 0, inStore = 0;
        if (crater != null)
        {
            EditLog edits = _w.Store.ExportEdits();
            var c = TInt3.Floor(crater.Center);
            int r = (int)MathF.Ceiling(crater.Radius) + 1;
            for (int y = -r; y <= r; y++)
                for (int z = -r; z <= r; z++)
                    for (int x = -r; x <= r; x++)
                    {
                        var cell = new TInt3(c.X + x, c.Y + y, c.Z + z);
                        if (!_w.Store.IsRemoved(cell)) continue;
                        inStore++;
                        if (edits.Contains(cell) && _w.Terrain.Log.Contains(cell)) inLog++;
                    }
            logged = inLog == inStore && inStore >= crater.CellsRemoved && _w.Terrain.Log.CellCount - logBefore == crater.CellsRemoved;
        }
        Check(crater != null && crater.Radius <= _w.Rules.Craters.MaxRadius && crater.Radius <= ChunkStore.MaxCraterRadius
              && crater.CellsRemoved > 100 && crater.CellsRemoved <= ChunkStore.MaxCraterCells && logged && uncappedRadius > crater.Radius,
            $"it cut a real crater through the terrain's edit API: {crater?.CellsRemoved} cells (cap {ChunkStore.MaxCraterCells}), radius {crater?.Radius:F1} m " +
            $"(the formula says {uncappedRadius:F1}, capped at {ChunkStore.MaxCraterRadius:F0}), every cell in the store's export and the session's edit log");

        for (int i = 0; i < 90; i++) yield return 0;
        // Measured where it struck: the hull's lowest point, not its centre over the spot, makes the hole.
        N.Vector3 struck = crater?.Center ?? spot;
        before = _w.Ground(struck.X, struck.Z).Y;
        float floor = _w.GroundTop(struck.X, struck.Z);
        Check(dive.IsWreck && dive.Body.Motion == GalHack.Physics.Motion.Free && floor < before - 2f && !_w.Physics.Stuck.IsStuck(dive.Body, out _),
            $"with no hull points left it lost power and became a free wreck, now lying in its own hole (ground {before - floor:F1} m lower than before)");
        _w.Remove(dive);

        // Ramming: both sides take the same damage, from the reduced mass. (A hover rover would
        // hop over the parked ship: its look-ahead sees the hull as a rock to lift over.)
        Ship parked = _w.Corsair;
        Ship rammer = _w.SpawnVessel("corsair.gbp", parked.Body.Position + new N.Vector3(-22f, 0.3f, 0f), -MathF.PI / 2f, landed: false);
        rammer.Body.Velocity = new N.Vector3(20f, 0f, 0f); // unmanned: the autopilot holds course
        long parkedTicks = parked.Vessel.SimulatedTicks;
        float rammerBefore = rammer.Body.Damage, parkedBefore = parked.Body.Damage;
        _impacts.Clear();
        for (int i = 0; i < 90 && !_impacts.Any(x => x.Other != null); i++) yield return 0;
        ImpactInfo ram = _impacts.FirstOrDefault(x => x.Other != null);
        float mu = Impacts.ReducedMass(rammer.Body.Mass, parked.Body.Mass);
        float rammerTook = rammer.Body.Damage - rammerBefore, parkedTook = parked.Body.Damage - parkedBefore;
        Check(ram != null && MathF.Abs(ram.Damage - Impacts.Damage(mu, ram.Speed, _w.Rules.Contacts)) < 1e-5f
              && MathF.Abs(rammerTook - parkedTook) < 1e-5f && rammerTook > 0f,
            $"a Corsair rams the parked one at {ram?.Speed:F1} m/s: both take {rammerTook:F3} damage, from the reduced mass {mu:F0} kg");
        for (int i = 0; i < 30; i++) yield return 0;
        Check(parked.Vessel.SimulatedTicks > parkedTicks, $"and the rammed Corsair was knocked loose ({parked.Body.Velocity.Length():F1} m/s)");
        _w.Remove(rammer);
        for (int i = 0; i < 120; i++) yield return 0;

        // Falls: the player is hurt by the same formula (with their own safe speed), with no floor
        // at all. A 10 m drop lands them unhurt; 30 m costs a little.
        Player p = _w.Player;
        var drops = new List<(float metres, float speed, float lost)>();
        foreach (float metres in new[] { 10f, 30f })
        {
            p.Health.Set(p.Health.Max);
            p.PlaceOutside(_w.Ground(-40f, -25f) + N.Vector3.UnitY * metres);
            float healthBefore = p.Health.Current;
            float fastest = 0f;
            _impacts.Clear();
            for (int i = 0; i < 200 && !(p.Body.OnFloor && fastest > 1f); i++)
            {
                yield return 0;
                if (!p.Body.OnFloor) fastest = MathF.Max(fastest, -p.Body.Velocity.Y);
            }
            hit = _impacts.LastOrDefault(x => x.Body == p.Body);
            float lost = healthBefore - p.Health.Current;
            bool byFormula = MathF.Abs(lost - Impacts.Damage(100f, hit?.Speed ?? fastest, _w.Rules.Contacts, BodyKind.Player)) < 1e-6f;
            drops.Add((metres, fastest, byFormula ? lost / p.Health.Max : float.NaN));
        }
        Check(drops[0].lost == 0f && drops[0].speed > 12f && drops[1].lost > 0.02f && drops[1].lost < 0.2f,
            $"a 10 m drop lands the player at {drops[0].speed:F1} m/s unhurt; a 30 m fall at {drops[1].speed:F1} m/s costs {drops[1].lost:P0} of their health: " +
            $"the same formula with their own {_w.Rules.Contacts.PlayerSafeSpeed:0.#} m/s safe speed, no floor");
        p.Health.Set(p.Health.Max);
        p.PlaceOutside(_w.Ground(1.5f, 8.5f) + N.Vector3.UnitY * 0.05f);
        yield return 0;
    }

    float SumDamage(Body b) => _impacts.Where(x => x.Body == b || x.Other == b).Sum(x => x.Damage);

    // ==== Severing ==============================================================================

    IEnumerable<int> Severing()
    {
        Ship ship = _w.SpawnVessel("corsair.gbp", new N.Vector3(-20f, 95f, 30f), 0.3f, landed: false);
        ship.Body.Velocity = new N.Vector3(6f, 3f, -25f);
        for (int i = 0; i < 30; i++)
        {
            ship.Flight.Input = PilotInput.Hands(Vector3.Forward * 0.6f, new Vector3(0.2f, 0.7f, 0.3f)); // turning hard: plenty of spin
            yield return 0;
        }
        N.Vector3 p0 = Ship.BlockMomentum(ship.Structure, ship.Body, out float m0);
        float spin = ship.Body.AngularVelocity.Length();
        var (destroyed, wrecks) = ship.ShootThrough(Ship.PortPodMount);

        N.Vector3 p1 = Ship.BlockMomentum(ship.Structure, ship.Body, out float m1);
        foreach (Ship w in wrecks)
        {
            m1 += w.Body.Mass;
            p1 += w.Body.Momentum;
        }
        foreach (var (pos, block) in destroyed)
        {
            float bm = Units.Kilograms(ship.Structure.Catalogue.StatsFor(block).Mass);
            m1 += bm;
            p1 += bm * ship.Body.PointVelocity(ship.Body.ToParent(StructureBridge.CellCenter(ship.Structure, pos)));
        }
        Ship pod = wrecks.FirstOrDefault();
        MassProperties podMass = pod == null ? default : StructureBridge.Mass(pod.Structure);
        Check(wrecks.Count == 1 && pod.Body.Kind == BodyKind.Wreck && pod.Body.Motion == GalHack.Physics.Motion.Free
              && MathF.Abs(m0 - m1) < 0.01f && N.Vector3.Distance(p0, p1) < 1e-4f * p0.Length() && MathF.Abs(pod.Body.Mass - podMass.Mass) < 0.01f
              && MathF.Abs(ship.Body.Mass - StructureBridge.Mass(ship.Structure).Mass) < 0.01f,
            $"shooting through the pod's mounting severs it into a free wreck ({pod?.Structure.Count} blocks, {pod?.Body.Mass:F0} kg) " +
            $"while the ship turns at {spin:F2} rad/s: total mass {m0:F1} → {m1:F1} kg and momentum {p0.Length():F1} → {p1.Length():F1} kg·m/s " +
            $"(difference {N.Vector3.Distance(p0, p1):E1}) with the {destroyed.Count} shot-out blocks counted");

        // Two frames on, the engine has stepped the wreck once (its step follows each tick): its
        // velocity is the inherited one plus exactly that tick's forces (gravity and air drag) over its mass.
        N.Vector3 expected = pod == null ? default : pod.Body.PointVelocity(pod.Body.CenterOfMassPosition);
        yield return 0;
        N.Vector3 pushed = pod == null ? default : pod.Body.LastTotalForce / pod.Body.Mass * Dt;
        yield return 0;
        var rigid = (pod?.Binding as FreeBinding)?.Rigid;
        N.Vector3 actual = rigid?.LinearVelocity.ToNumerics() ?? default;
        N.Vector3 drift = actual - expected;
        Check(rigid != null && N.Vector3.Distance(drift, pushed) < 1e-3f,
            $"the engine's wreck body carries on with the momentum it inherited ({expected.Length():F1} m/s at its centre of mass): one engine step later it has changed by exactly gravity and drag over its mass ({drift.Length():F3} m/s, off by {N.Vector3.Distance(drift, pushed):E1})");

        // The second pod, shot with a blast behind it: thrown clear and tumbling.
        SInt3[] starboard = Ship.PortPodMount.Select(c => new SInt3(-c.X, c.Y, c.Z)).ToArray();
        var (_, more) = ship.ShootThrough(starboard, push: 6f);
        float tumble = 0f;
        for (int i = 0; i < 45; i++)
        {
            yield return 0;
            if (more.Count > 0) tumble = MathF.Max(tumble, (more[0].Binding as FreeBinding).Rigid.AngularVelocity.Length());
        }
        Check(more.Count == 1 && tumble > spin + 0.3f && ship.Stats.Accel(Direction.Forward) < 1f,
            $"the other pod, blasted off, tumbles away at up to {tumble:F1} rad/s; the ship's numbers follow its blocks (forward accel now {ship.Stats.Accel(Direction.Forward):F1} m/s²: both main engines gone)");
        foreach (Ship w in wrecks.Concat(more)) _w.Remove(w);
        _w.Remove(ship);
        yield return 0;
    }

    // ==== Riding: one mechanism, the cabin world, inside and out on the hull =====================

    /// <summary>
    /// A point on the Corsair in the metres it was written in: an old half-metre blueprint, it loads a cell for a cell
    /// on 1 m cells, so its points scale with it (the blueprints are replaced, not converted).
    /// </summary>
    static N.Vector3 Corsair(float x, float y, float z) => new N.Vector3(x, y, z) * (1f / 0.5f);

    IEnumerable<int> HullRider()
    {
        Ship ship = _w.SpawnVessel("corsair.gbp", new N.Vector3(0f, 70f, -30f), 0f, landed: false);
        ship.Flight.Input = PilotInput.LetGo;
        Player p = _w.Player;
        p.Yaw = 0f;
        p.Pitch = 0f;
        N.Vector3 standLocal = Corsair(-1.0f, 2.85f, -1.5f); // just over the roof, clear of the down jets and the fin
        p.PlaceOutside(ship.Body.ToParent(standLocal));
        for (int i = 0; i < 30; i++) yield return 0;
        bool joined = p.OnHull == ship && p.Body.OnFloor && p.Node.GetParent() == ship.Cabin;
        N.Vector3 startLocal = p.Body.Position;
        float worst = 0f, maxSpeed = 0f, turned = 0f;
        float startHeading = MathX.Heading(ship.Body.Rotation);
        bool stayed = true;
        for (int t = 0; t < 600; t++)
        {
            float s = t * Dt;
            Vector3 move = s < 3f ? new Vector3(0f, 0f, -1f) : s < 6f ? new Vector3(0.3f, 0f, -0.8f) : s < 8f ? new Vector3(0f, 0.8f, -0.6f) : Vector3.Zero;
            Vector3 turn = s < 3f ? Vector3.Zero : s < 6f ? new Vector3(0f, 0.6f, 0f) : s < 8f ? new Vector3(0.15f, -0.5f, 0f) : Vector3.Zero;
            ship.Flight.Input = PilotInput.Hands(move, turn, boost: t == 30);
            yield return 0;
            worst = MathF.Max(worst, N.Vector3.Distance(p.Body.Position, startLocal));
            maxSpeed = MathF.Max(maxSpeed, ship.Body.Velocity.Length());
            turned = MathF.Max(turned, MathF.Abs(MathX.WrapAngle(MathX.Heading(ship.Body.Rotation) - startHeading)));
            stayed &= p.OnHull == ship;
        }
        Check(joined && stayed && worst < 0.15f && maxSpeed > 40f && turned > 1f,
            $"a player who lands on a hull joins its cabin world, and stays put on the outer hull through a 10 s manoeuvre (boost to {maxSpeed:F0} m/s, " +
            $"a {Units.Degrees(turned):F0}° turn, a climb, a stop): never more than {worst * 100f:F1} cm from where they stood");
        N.Vector3 look = p.WorldLook();
        float lookHeading = MathF.Atan2(-look.X, -look.Z);
        Check(MathF.Abs(MathX.WrapAngle(lookHeading - MathX.Heading(ship.Body.Rotation))) < 0.05f,
            "and their view turned with the ship (it lives in the ship's frame)");

        // Weather reaches a rider out on the hull, bridged into the cabin world, against their
        // motion relative to the hull: flying through still air is no wind at all, a storm is.
        ship.Flight.Input = PilotInput.Hands(Vector3.Forward);
        for (int i = 0; i < 60; i++) yield return 0;
        float airspeed = ship.Body.Velocity.Length(), stillAir = p.Body.LastForce(ForceKind.Weather).Length();
        _w.Weather.Calm = false;
        _w.Weather.Sim.Snap(WeatherKind.DustStorm);
        for (int i = 0; i < 5; i++) yield return 0;
        N.Vector3 wind = _w.Weather.Sim.WindAt(p.Body.WorldPosition.ToGodot()).ToNumerics();
        N.Vector3 push = Riding.DirectionToWorld(ship.Body, p.Body.LastForce(ForceKind.Weather));
        bool stillOn = p.OnHull == ship;
        _w.Weather.Calm = true;
        Check(stillAir < 1f && stillOn && push.Length() > 10f && N.Vector3.Dot(push, wind) > 0.95f * push.Length() * wind.Length(),
            $"weather reaches the rider on the hull relative to it: flying at {airspeed:F0} m/s through still air pushes them {stillAir:F1} N; " +
            $"a {wind.Length():F0} m/s dust-storm wind pushes them {push.Length():F0} N along it, and they keep their feet");

        // A jump lets go at once, with the ship's velocity there plus the jump. Back over the map
        // first (the rider goes wherever the ship goes), so the jump lands on ground.
        ship.Vessel.Place(ship.Body, new N.Vector3(-70f, 75f, 40f), -MathF.PI / 2f);
        ship.Binding.Teleport();
        ship.Flight.Input = PilotInput.Hands(Vector3.Forward);
        for (int i = 0; i < 90; i++) yield return 0;
        bool onBefore = p.OnHull == ship;
        N.Vector3 up = Riding.DirectionToWorld(ship.Body, p.Body.Up);
        p.Legs.Jump = true;
        yield return 0;
        p.Legs.Jump = false;
        bool letGo = p.Body.Frame == null && p.Node.GetParent() == _w;
        N.Vector3 hullVelocity = ship.Body.PointVelocity(p.Body.Position);
        N.Vector3 relative = p.Body.WorldVelocity - hullVelocity;
        float along = N.Vector3.Dot(relative, up), across = (relative - up * along).Length();
        Check(onBefore && letGo && MathF.Abs(along - p.Legs.JumpSpeed) < 0.5f && across < 0.2f && hullVelocity.Length() > 20f,
            $"a jump lets go of the ship at once, and the player keeps the ship's {hullVelocity.Length():F0} m/s plus the jump " +
            $"({along:F2} m/s up off the hull, {across:F2} m/s astray)");
        for (int i = 0; i < 3; i++) yield return 0;
        _w.Remove(ship);
        p.PlaceOutside(_w.Ground(1.5f, 8.5f) + N.Vector3.UnitY * 0.05f);
        for (int i = 0; i < 5; i++) yield return 0;
    }

    IEnumerable<int> LandingOnMovingHull()
    {
        Ship ship = _w.SpawnVessel("corsair.gbp", new N.Vector3(-60f, 80f, 60f), MathF.PI / 2f, landed: false);
        ship.Flight.Input = PilotInput.Hands(Vector3.Forward * 0.6f);
        for (int i = 0; i < 120; i++) yield return 0;
        ship.Flight.Input = PilotInput.Autopilot; // holds its course and speed
        for (int i = 0; i < 10; i++) yield return 0;
        Player p = _w.Player;
        N.Vector3 drop = ship.Body.ToParent(Corsair(-1.0f, 3.6f, -1.5f));
        p.PlaceOutside(drop, ship.Body.PointVelocity(drop) - N.Vector3.UnitY * 1.5f);
        N.Vector3 last = p.Body.WorldVelocity, lastPos = p.Body.WorldPosition;
        float jolt = float.NaN, jump = float.NaN, slipBefore = 0f, slipAfter = 0f, fall = 0f, settled = float.NaN;
        int afterTicks = -1;
        for (int t = 0; t < 120 && afterTicks < 60; t++)
        {
            yield return 0;
            N.Vector3 v = p.Body.WorldVelocity, pos = p.Body.WorldPosition;
            N.Vector3 n = Riding.DirectionToWorld(ship.Body, N.Vector3.UnitY);
            if (afterTicks < 0 && p.OnHull == ship)
            {
                // The tick it joined: along the hull nothing changes; only the fall into the roof stops.
                N.Vector3 dv = v - last, slip = last - ship.Body.PointVelocity(pos);
                fall = -N.Vector3.Dot(slip, n);
                slipBefore = (slip - n * N.Vector3.Dot(slip, n)).Length();
                jolt = (dv - n * N.Vector3.Dot(dv, n)).Length();
                jump = N.Vector3.Distance(pos, lastPos + last * Dt);
                afterTicks = 0;
            }
            else if (afterTicks >= 0)
            {
                afterTicks++;
                N.Vector3 rel = v - ship.Body.PointVelocity(pos);
                if (afterTicks == 1) slipAfter = rel.Length();
                settled = rel.Length();
            }
            last = v;
            lastPos = pos;
        }
        float speed = ship.Body.Velocity.Length();
        Check(afterTicks >= 60 && p.OnHull == ship && jolt < 0.3f && jump < 0.1f && slipAfter < slipBefore + 0.3f && settled < 0.05f && speed > 15f,
            $"landing on a hull flying at {speed:F0} m/s joins it without a jolt: the world velocity along the hull changes by {jolt:F2} m/s and the position by " +
            $"{jump * 100f:F1} cm at the join (only the {fall:F1} m/s fall stops); sliding {slipBefore:F2} m/s on it as they land, {slipAfter:F2} m/s a tick later, " +
            $"then their feet hold ({settled:F3} m/s)");
        _w.Remove(ship);
        p.PlaceOutside(_w.Ground(1.5f, 8.5f) + N.Vector3.UnitY * 0.05f);
        for (int i = 0; i < 5; i++) yield return 0;
    }

    IEnumerable<int> CabinRider()
    {
        Ship ship = _w.SpawnVessel("corsair.gbp", new N.Vector3(30f, 90f, -20f), 0f, landed: false);
        Player p = _w.Player;
        p.PlaceAboard(ship, new N.Vector3(0f, 0.3f, -1f));
        ship.Flight.Input = PilotInput.Hands(Vector3.Forward * 0.5f);
        for (int i = 0; i < 40; i++) yield return 0;
        N.Vector3 start = p.Body.Position;
        float rolled = 0f, worst = 0f;
        bool aboard = true;
        int floorTicks = 0, ticks = 0;
        while (rolled < MathF.Tau && ticks < 600)
        {
            ship.Flight.Input = PilotInput.Hands(Vector3.Forward * 0.5f, new Vector3(0f, 0f, 1f));
            yield return 0;
            ticks++;
            rolled += MathF.Abs(ship.Vessel.LocalSpin.Z) * Dt;
            worst = MathF.Max(worst, N.Vector3.Distance(p.Body.Position, start));
            aboard &= p.Aboard == ship && !p.Body.OnOuterHull;
            if (p.Body.OnFloor) floorTicks++;
        }
        Check(aboard && rolled >= MathF.Tau && worst < 0.05f && floorTicks >= ticks - 2,
            $"a player in the cabin stays put through a barrel roll ({Units.Degrees(rolled):F0}° in {ticks * Dt:F1} s): on the floor, " +
            $"{worst * 100f:F2} cm from where they stood, because the cabin world never moves and the core's gravity points at the floor");

        // Walking in the cabin while it flies: the walk is in ship-local metres. Down the
        // starboard side, clear of the seat, toward the windscreen.
        ship.Flight.Input = PilotInput.Hands(Vector3.Forward);
        p.PlaceAboard(ship, new N.Vector3(1.0f, 0.3f, 0.5f));
        p.Yaw = 0f; // facing the windscreen
        p.Pitch = 0f;
        for (int i = 0; i < 10; i++) yield return 0;
        N.Vector3 from = p.Body.Position;
        N.Vector3 worldFrom = p.Body.WorldPosition;
        for (int i = 0; i < 30; i++)
        {
            p.Legs.Move = new N.Vector2(0f, -1f);
            yield return 0;
        }
        p.Legs.Move = N.Vector2.Zero;
        float walked = from.Z - p.Body.Position.Z, flown = N.Vector3.Distance(p.Body.WorldPosition, worldFrom);
        Check(p.Aboard == ship && walked > 2f && walked < 3f && flown > 15f,
            $"walking forward in the cabin for 0.5 s covers {walked:F1} m of deck while the ship carries them {flown:F0} m");

        // Core gravity is built in and toggleable, and costs no power. Off in the air, the planet's
        // own gravity reaches into the cabin (turned into the ship's frame); on, ship-down again.
        ship.Flight.Input = PilotInput.LetGo;
        ship.Body.Core.On = false;
        for (int i = 0; i < 30; i++) yield return 0;
        N.Vector3 planet = MathX.Unrotate(ship.Body.Rotation, _w.Physics.Zone.At(p.Body.WorldPosition));
        bool planetDown = N.Vector3.Distance(_w.Physics.GravityAt(p.Body), planet) < 1e-4f && planet.Length() > 9f && !p.Legs.Floating;
        ship.Body.Core.On = true;
        for (int i = 0; i < 60; i++) yield return 0;
        Check(planetDown && p.Body.OnFloor && N.Vector3.Distance(_w.Physics.GravityAt(p.Body), ship.Body.Core.Local) < 1e-4f,
            "core gravity toggles off (in the air, the planet's gravity reaches in) and on again (ship-down, on the floor)");
        p.PlaceOutside(_w.Ground(1.5f, 8.5f) + N.Vector3.UnitY * 0.05f);
        _w.Remove(ship);
        for (int i = 0; i < 5; i++) yield return 0;
    }

    /// <summary>What a walk on a hull saw.</summary>
    sealed class HullWalk
    {
        public bool Stayed = true;
        public float LongestOff, Rolled, LowestUp = 1f;
        public int Edges, Walls, Steps, Ticks, OffFloor;
    }

    /// <summary>
    /// Walks the player forward on a ship until <paramref name="done"/> says so (or time runs out),
    /// noting whether they ever left its cabin world and the longest they were out of contact.
    /// </summary>
    IEnumerable<int> WalkOn(Ship ship, HullWalk log, Func<Player, bool> done, int maxTicks, float speed = 1f, bool rolling = false, Player who = null)
    {
        Player p = who ?? _w.Player;
        int edges = p.Binding.EdgesCrossed, walls = p.Binding.WallsClimbed, steps = p.Binding.StepsTaken;
        for (int t = 0; t < maxTicks; t++)
        {
            if (rolling) ship.Flight.Input = PilotInput.Hands(Vector3.Zero, new Vector3(0f, 0f, 1f));
            p.Legs.Move = new N.Vector2(0f, -speed);
            yield return 0;
            log.Ticks++;
            log.Stayed &= ship == null ? p.Body.Frame == null : p.Aboard == ship;
            log.LongestOff = MathF.Max(log.LongestOff, Riding.OutOfContactFor(p.Body));
            log.LowestUp = MathF.Min(log.LowestUp, p.Body.Up.Y);
            if (!p.Body.OnFloor) log.OffFloor++;
            if (ship?.Vessel != null) log.Rolled += MathF.Abs(ship.Vessel.LocalSpin.Z) * Dt;
            if (done(p)) break;
        }
        p.Legs.Move = N.Vector2.Zero;
        log.Edges += p.Binding.EdgesCrossed - edges;
        log.Walls += p.Binding.WallsClimbed - walls;
        log.Steps += p.Binding.StepsTaken - steps;
    }

    /// <summary>A ship in orbit (100 m above the atmosphere), still, its core gravity on (or off).</summary>
    Ship InOrbit(string blueprint, N.Vector3 spot, bool coreGravity = true)
    {
        Ship ship = _w.SpawnVessel(blueprint, new N.Vector3(spot.X, _w.Physics.Zone.Ceiling + 100f + spot.Y, spot.Z), 0f, landed: false);
        ship.Flight.Input = PilotInput.LetGo;
        ship.Body.Core.On = coreGravity;
        ship.Cabin.SetGravity(ship.Body.Core);
        return ship;
    }

    void Still(Ship ship)
    {
        ship.Flight.Input = PilotInput.LetGo;
        ship.Vessel.Place(ship.Body, ship.Body.Position, 0f);
        ship.Binding.Teleport();
    }

    /// <summary>
    /// Out on a hull, down is the gravity the rider is in, as inside: in orbit, the ship's core
    /// gravity. It holds them to the roof whatever the ship does; nothing holds them to its sides.
    /// </summary>
    IEnumerable<int> SpaceWalk()
    {
        Ship ship = InOrbit("corsair.gbp", new N.Vector3(30f, 0f, -20f));
        Player p = _w.Player;
        p.PlaceAboard(ship, Corsair(-1.0f, 2.78f, -1.5f)); // on the roof
        for (int i = 0; i < 20; i++) yield return 0;
        float grace = _w.Rules.Riding.LetGoAfter;
        bool held = p.OnHull == ship && !p.Binding.MagBoots && p.Body.OnFloor && _w.Physics.Zone.At(ship.Body.Position) == N.Vector3.Zero
                    && N.Vector3.Distance(_w.Physics.GravityAt(p.Body), ship.Body.Core.Local) < 1e-4f;

        // A barrel roll in space, with someone on the roof.
        N.Vector3 start = p.Body.Position;
        float rolled = 0f, worst = 0f;
        bool stayed = true;
        int floorTicks = 0, ticks = 0;
        while (rolled < MathF.Tau && ticks < 600)
        {
            ship.Flight.Input = PilotInput.Hands(Vector3.Forward * 0.1f, new Vector3(0f, 0f, 1f));
            yield return 0;
            ticks++;
            rolled += MathF.Abs(ship.Vessel.LocalSpin.Z) * Dt;
            worst = MathF.Max(worst, N.Vector3.Distance(p.Body.Position, start));
            stayed &= p.OnHull == ship;
            if (p.Body.OnFloor) floorTicks++;
        }
        Check(held && stayed && rolled >= MathF.Tau && worst < 0.05f && floorTicks >= ticks - 2,
            $"in orbit, out on the hull down is the core's gravity: a player on the roof stays put through a barrel roll ({Units.Degrees(rolled):F0}°), " +
            $"{worst * 100f:F2} cm from where they stood, held toward the ship's floor at {ship.Body.Core.Strength:0.#} m/s²");

        // Walked off the roof to starboard, nothing turns them onto the side: they go down the
        // bevel, off the edge, fall ship-down and let go of the ship.
        Still(ship);
        p.PlaceAboard(ship, Corsair(0.9f, 2.78f, -1.5f));
        p.Yaw = -MathF.PI / 2f; // facing starboard
        p.Pitch = 0f;
        for (int i = 0; i < 20; i++) yield return 0;
        var off = new HullWalk();
        foreach (int x in WalkOn(ship, off, q => q.Body.Frame == null, 240)) yield return x;
        N.Vector3 fall = MathX.Unrotate(ship.Body.Rotation, p.Body.Velocity - ship.Body.PointVelocity(p.Body.Position));
        Check(p.Body.Frame == null && off.Edges == 0 && off.Walls == 0 && off.LowestUp > 0.999f && fall.Y < -1f,
            $"walked off the roof's edge, nothing carries them onto the hull's side: they fall off it ship-down ({-fall.Y:F1} m/s) " +
            $"and let go of the ship after {off.LongestOff:F2} s out of contact, never turned off ship-up");
        for (int i = 0; i < 5; i++) yield return 0;

        // Knocked off the roof by a bump that throws them clear for less than the grace time: still aboard.
        Still(ship);
        p.PlaceAboard(ship, Corsair(-1.0f, 2.78f, -1.5f));
        for (int i = 0; i < 10; i++) yield return 0;
        _w.Physics.AddImpulse(p.Body, N.Vector3.UnitY * p.Body.Mass * 1.6f, ForceKind.Other);
        float bumped = 0f;
        bool kept = true;
        for (int i = 0; i < 60; i++)
        {
            yield return 0;
            bumped = MathF.Max(bumped, Riding.OutOfContactFor(p.Body));
            kept &= p.OnHull == ship;
        }
        Check(kept && bumped > 0.15f && bumped < grace && p.Body.OnFloor,
            $"a bump that throws a rider off the hull for {bumped:F2} s (under the {grace:0.0#} s grace) doesn't let go of the ship: its gravity brings them back down");

        // Knocked off hard: they let go and drift off with their inertia.
        _w.Physics.AddImpulse(p.Body, N.Vector3.UnitY * p.Body.Mass * 6f, ForceKind.Other);
        int until = 0;
        while (p.Body.Frame != null && until++ < 60) yield return 0;
        bool knockedOff = p.Body.Frame == null;
        N.Vector3 v0 = p.Body.Velocity;
        float d0 = N.Vector3.Distance(p.Body.Position, ship.Body.Position);
        float drift = 0f;
        for (int i = 0; i < 60; i++)
        {
            yield return 0;
            drift = MathF.Max(drift, N.Vector3.Distance(p.Body.Velocity, v0));
        }
        float d1 = N.Vector3.Distance(p.Body.Position, ship.Body.Position);
        N.Vector3 away = v0 - ship.Body.Velocity;
        Check(knockedOff && drift < 1e-3f && d1 - d0 > 0.9f * away.Length() && away.Length() > 1f,
            $"knocked off hard, a rider lets go after {until * Dt:F2} s and drifts away with their inertia in space ({away.Length():F1} m/s, " +
            $"{d1 - d0:F1} m in 1 s, velocity unchanged to {drift:F4} m/s)");

        // A jump from the roof of a ship under way lets go at once and drifts away, with the
        // ship's velocity plus the jump, while the ship flies on.
        Still(ship);
        ship.Flight.Input = PilotInput.Hands(Vector3.Forward * 0.3f);
        p.PlaceAboard(ship, Corsair(-1.0f, 2.78f, -1.5f));
        for (int i = 0; i < 40; i++) yield return 0;
        bool onRoof = p.OnHull == ship && p.Body.OnFloor;
        N.Vector3 up = Riding.DirectionToWorld(ship.Body, p.Body.Up);
        p.Legs.Jump = true;
        yield return 0;
        p.Legs.Jump = false;
        bool jumped = p.Body.Frame == null;
        N.Vector3 rel = p.Body.Velocity - ship.Body.PointVelocity(p.Body.Position);
        N.Vector3 vj = p.Body.Velocity, shipThen = ship.Body.Velocity;
        float j0 = N.Vector3.Distance(p.Body.Position, ship.Body.Position), coast = 0f;
        for (int i = 0; i < 60; i++)
        {
            yield return 0;
            coast = MathF.Max(coast, N.Vector3.Distance(p.Body.Velocity, vj));
        }
        float j1 = N.Vector3.Distance(p.Body.Position, ship.Body.Position);
        Check(onRoof && jumped && N.Vector3.Dot(rel, up) > 0.9f * p.Legs.JumpSpeed && (rel - up * N.Vector3.Dot(rel, up)).Length() < 0.2f
              && coast < 1e-3f && j1 - j0 > 3f && shipThen.Length() > 5f,
            $"a jump from the roof lets go at once: the player keeps the ship's {shipThen.Length():F0} m/s plus the jump, {N.Vector3.Dot(rel, up):F1} m/s " +
            $"up off it, and drifts on with exactly that velocity ({j1 - j0:F1} m apart after 1 s)");
        _w.Remove(ship);
        p.PlaceOutside(_w.Ground(1.5f, 8.5f) + N.Vector3.UnitY * 0.05f);
        for (int i = 0; i < 5; i++) yield return 0;
    }

    IEnumerable<int> Airlock()
    {
        Ship ship = InOrbit("corsair.gbp", new N.Vector3(-30f, 0f, -40f));
        Player p = _w.Player;
        p.PlaceAboard(ship, Corsair(0f, 0.28f, 3.9f)); // on the porch behind the door
        p.Yaw = 0f; // facing the door
        p.Pitch = 0f;
        for (int i = 0; i < 10; i++) yield return 0;
        bool porch = p.OnHull == ship && !p.Binding.MagBoots && p.Body.OnFloor
                     && N.Vector3.Distance(_w.Physics.GravityAt(p.Body), ship.Body.Core.Local) < 1e-4f;
        var walk = new HullWalk();
        foreach (int x in WalkOn(ship, walk, q => q.Body.Position.Z < Corsair(0f, 0f, 1.5f).Z, 120)) yield return x;
        for (int i = 0; i < 20; i++) yield return 0;
        bool inside = p.Aboard == ship && !p.Body.OnOuterHull && p.Body.OnFloor
                      && N.Vector3.Distance(_w.Physics.GravityAt(p.Body), ship.Body.Core.Local) < 1e-4f;
        Check(porch && inside && walk.Stayed && walk.LongestOff < 0.05f && p.Body.Up.Y > 0.999f,
            "walking from the porch on the outer hull in through the door never leaves the ship's cabin world: the core's gravity outside and in");

        // Core off in orbit: they float inside, and floating out of the door nothing holds them to
        // the hull (there is no gravity out there either): they let go of the ship and drift off.
        ship.Body.Core.On = false;
        ship.Cabin.SetGravity(ship.Body.Core);
        for (int i = 0; i < 10; i++) yield return 0;
        bool floating = p.Legs.Floating && _w.Physics.GravityAt(p.Body) == N.Vector3.Zero;
        bool stayed = true;
        int ticks = 0;
        while (!p.Body.OnOuterHull && ticks++ < 180)
        {
            p.Legs.Move = new N.Vector2(0f, 0.5f); // backwards, out of the door
            yield return 0;
            stayed &= p.Aboard == ship;
        }
        p.Legs.Move = N.Vector2.Zero;
        bool outside = p.OnHull == ship;
        int drifting = 0;
        while (p.Body.Frame != null && drifting++ < 120) yield return 0;
        Check(floating && stayed && outside && p.Body.Frame == null && p.Legs.Floating,
            $"with core gravity off in orbit they float inside; floating out of the door nothing holds them to the hull: " +
            $"they let go of the ship {drifting * Dt:F2} s later and drift off, weightless");
        _w.Remove(ship);
        p.PlaceOutside(_w.Ground(1.5f, 8.5f) + N.Vector3.UnitY * 0.05f);
        for (int i = 0; i < 5; i++) yield return 0;
    }

    /// <summary>
    /// Out on a hull, down is the gravity the rider is in: a hull's walls and underside can't be
    /// walked on, and a low lip is still just a step. On the Deck Barge in orbit, under its core gravity.
    /// </summary>
    IEnumerable<int> HullWalls()
    {
        Ship barge = InOrbit("barge.gbp", new N.Vector3(-20f, 0f, 30f));
        Player p = _w.Player;
        float stepHeight = _w.Rules.Walking.StepHeight, grace = _w.Rules.Riding.LetGoAfter;

        // Into the superstructure's side, two blocks (1 m) tall: a wall, never a floor.
        p.PlaceAboard(barge, new N.Vector3(-0.25f, 0.28f, 3.0f));
        p.Yaw = 0f; // facing the superstructure
        p.Pitch = 0f;
        for (int i = 0; i < 10; i++) yield return 0;
        var wall = new HullWalk();
        foreach (int x in WalkOn(barge, wall, _ => false, 90)) yield return x; // 1.5 s pushing into it
        for (int i = 0; i < 10; i++) yield return 0;
        bool stopped = p.OnHull == barge && p.Body.OnFloor && p.Body.Up.Y > 0.999f && MathF.Abs(p.Body.Position.Y - 0.25f) < 0.05f
                       && p.Body.Position.Z > 0.75f && p.Body.Position.Z < 1.3f;
        Check(wall.Stayed && stopped && wall.Walls == 0 && wall.Edges == 0 && wall.LowestUp > 0.999f && wall.OffFloor == 0,
            $"in orbit under the barge's core gravity, walking 1.5 s into its superstructure's side (1 m, over the {stepHeight:0.0#} m step height) stops the player " +
            $"against it on the deck ({p.Body.Position.Z - 0.75f:F2} m off it): a hull's wall is never walked onto");

        // Off the deck's square edge: nothing carries them round onto its side or underside; they
        // drop off it, ship-down, and let go of the barge.
        p.PlaceAboard(barge, new N.Vector3(-2.5f, 0.28f, -2.5f));
        p.Yaw = MathF.PI / 2f; // facing port
        for (int i = 0; i < 10; i++) yield return 0;
        var edge = new HullWalk();
        foreach (int x in WalkOn(barge, edge, q => q.Body.Frame == null, 240)) yield return x;
        N.Vector3 fall = MathX.Unrotate(barge.Body.Rotation, p.Body.Velocity - barge.Body.PointVelocity(p.Body.Position));
        Check(p.Body.Frame == null && edge.Edges == 0 && edge.LowestUp > 0.999f && fall.Y < -1f && p.Body.Position.X < -4.25f,
            $"walked off the deck's square edge they drop past its side ship-down ({-fall.Y:F1} m/s) and let go of the barge: its underside can't be walked on");
        for (int i = 0; i < 5; i++) yield return 0;

        // A lip one block tall is a step: up onto it without a stop and down off the far side
        // (inboard, from the strip of deck outside it, so there is deck to come down on).
        p.PlaceAboard(barge, new N.Vector3(3.25f, 0.28f, -2.5f));
        p.Yaw = MathF.PI / 2f; // facing port, the starboard lip just ahead
        for (int i = 0; i < 10; i++) yield return 0;
        var lip = new HullWalk();
        foreach (int x in WalkOn(barge, lip, q => q.Body.Position.X < 1.2f, 120)) yield return x;
        int settle = 0;
        while (!p.Body.OnFloor && settle++ < 60) yield return 0;
        for (int i = 0; i < 10; i++) yield return 0;
        bool over = p.OnHull == barge && p.Body.OnFloor && p.Body.Up.Y > 0.999f && p.Body.Position.X < 1.9f && MathF.Abs(p.Body.Position.Y - 0.25f) < 0.05f;
        float drop = (lip.OffFloor + settle) * Dt;
        Check(lip.Stayed && over && lip.Steps >= 1 && lip.Walls == 0 && lip.LongestOff < grace && drop < 0.35f,
            $"out on the hull a lip one block ({barge.Structure.Grid.CellSize:0.0#} m) tall, under the {stepHeight:0.0#} m step height, is a step: " +
            $"the player walks up onto it without a stop and off the far side (a {drop:F2} s drop), never letting go of the hull");

        // A stair of three one-block steps, climbed without a pause.
        p.PlaceAboard(barge, new N.Vector3(-3.25f, 0.28f, 3.4f));
        p.Yaw = 0f; // facing up the stair
        for (int i = 0; i < 10; i++) yield return 0;
        var stair = new HullWalk();
        foreach (int x in WalkOn(barge, stair, q => q.Body.Position.Z < 0.9f, 180)) yield return x;
        for (int i = 0; i < 10; i++) yield return 0;
        bool top = p.OnHull == barge && p.Body.OnFloor && p.Body.Up.Y > 0.999f && MathF.Abs(p.Body.Position.Y - 1.75f) < 0.05f;
        Check(stair.Stayed && top && stair.Steps >= 3 && stair.Walls == 0 && stair.LongestOff < grace,
            $"and a stair of three one-block steps is walked straight up ({stair.Steps} steps, 1.5 m up in {stair.Ticks * Dt:F2} s), never letting go of the hull");
        _w.Remove(barge);
        p.PlaceOutside(_w.Ground(1.5f, 8.5f) + N.Vector3.UnitY * 0.05f);
        for (int i = 0; i < 5; i++) yield return 0;
    }

    /// <summary>Steps in the cabin under core gravity, and on terrain under the planet's.</summary>
    IEnumerable<int> StepsEverywhere()
    {
        float stepHeight = _w.Rules.Walking.StepHeight;

        // The same lip across the floor of the barge's deckhouse, under core gravity. (Not in the
        // Corsair: its cabin is 2 m tall, and over a 0.5 m lip there's no room to stand.)
        Ship barge = InOrbit("barge.gbp", new N.Vector3(-20f, 0f, 30f));
        Player p = _w.Player;
        p.PlaceAboard(barge, new N.Vector3(-0.25f, 0.28f, 5.2f));
        p.Yaw = MathF.PI; // facing aft, the lip ahead
        p.Pitch = 0f;
        for (int i = 0; i < 20; i++) yield return 0;
        bool indoors = p.Aboard == barge && !p.Body.OnOuterHull;
        var cabin = new HullWalk();
        foreach (int x in WalkOn(barge, cabin, r => r.Body.Position.Z > 6.6f, 120)) yield return x;
        int settle = 0;
        while (!p.Body.OnFloor && settle++ < 60) yield return 0;
        for (int i = 0; i < 5; i++) yield return 0;
        bool inside = indoors && p.Aboard == barge && !p.Body.OnOuterHull && p.Body.OnFloor && MathF.Abs(p.Body.Position.Y - 0.25f) < 0.05f
                      && N.Vector3.Distance(_w.Physics.GravityAt(p.Body), barge.Body.Core.Local) < 1e-4f;
        float drop = (cabin.OffFloor + settle) * Dt;
        Check(cabin.Stayed && inside && cabin.Steps >= 1 && drop < 0.35f,
            $"inside a cabin under core gravity, the same one-block lip is stepped up onto without a stop, and walked off the far side: " +
            $"over it in {cabin.Ticks * Dt:F2} s, then a {drop:F2} s drop to the floor beyond, as off any step under gravity");
        _w.Remove(barge);
        p.PlaceOutside(_w.Ground(1.5f, 8.5f) + N.Vector3.UnitY * 0.05f);
        for (int i = 0; i < 5; i++) yield return 0;

        // On terrain, in a fresh world (the landing field is level and nothing has been there).
        var view = new SubViewport { OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled, Size = new Vector2I(2, 2) };
        AddChild(view);
        var w = new SandboxWorld { Visuals = false };
        view.AddChild(w);
        w.Weather.Calm = true;
        for (int i = 0; i < 3; i++) yield return 0;
        Player q = w.Player;

        // A 0.5 m ledge on the world layer: a trimesh like the terrain's own collision, with the
        // sheer face the terrain's smooth mesh never makes, on the level field.
        N.Vector3 field = w.Ground(-12f, -10f);
        var ledge = new StaticBody3D { Name = "Ledge", CollisionLayer = Layers.World, CollisionMask = 0 };
        var box = new BoxMesh { Size = new Vector3(2f, 0.5f, 3f) };
        ledge.AddChild(new CollisionShape3D { Shape = box.CreateTrimeshShape() });
        w.AddChild(ledge);
        ledge.GlobalPosition = (field + new N.Vector3(-1f, 0.25f, 0f)).ToGodot(); // x -14..-12, z -11.5..-8.5
        q.PlaceOutside(field + new N.Vector3(1.5f, 0.05f, 0f));
        q.Yaw = MathF.PI / 2f; // facing west, at the ledge
        q.Pitch = 0f;
        for (int i = 0; i < 20; i++) yield return 0;
        var onLedge = new HullWalk();
        foreach (int x in WalkOn(null, onLedge, r => r.Body.Position.X < field.X - 1.2f, 120, who: q)) yield return x;
        for (int i = 0; i < 10; i++) yield return 0;
        float rise = q.Body.Position.Y - field.Y;
        Check(onLedge.Stayed && onLedge.Steps >= 1 && onLedge.OffFloor == 0 && q.Body.OnFloor && MathF.Abs(rise - 0.5f) < 0.05f,
            $"on terrain, walking into a sheer 0.5 m ledge steps the player up onto it ({rise:F2} m) without leaving the floor; nothing over {stepHeight:0.0#} m would be");

        // The steepest edge the terrain itself makes: one layer of cells dug out of the level
        // field leaves a 1 m drop over 2 m of smooth slope, walked up out of without a stop.
        var cells = new List<TInt3>();
        for (int x = 6; x < 11; x++)
            for (int z = -12; z < -8; z++)
                cells.Add(new TInt3(x, w.Store.TopSolid(x, z), z));
        w.Terrain.DigCells(cells);
        for (int i = 0; i < 3; i++) yield return 0;
        N.Vector3 pit = new(8.5f, w.GroundTop(8.5f, -10f), -10f);
        q.PlaceOutside(pit + N.Vector3.UnitY * 0.05f);
        q.Yaw = MathF.PI / 2f; // facing west, up the dug edge
        for (int i = 0; i < 20; i++) yield return 0;
        float floorY = q.Body.Position.Y;
        var dug = new HullWalk();
        float slowest = float.PositiveInfinity;
        foreach (int x in WalkOn(null, dug, r =>
        {
            if (r.Body.Position.X < 8f && r.Body.Position.X > 4.5f) slowest = MathF.Min(slowest, MathX.Flat(r.Body.Velocity).Length()); // once up to speed
            return r.Body.Position.X < 3.5f;
        }, 120, who: q)) yield return x;
        float climbed = q.Body.Position.Y - floorY;
        Check(dug.Stayed && dug.OffFloor == 0 && climbed > 0.9f && slowest > 3f,
            $"and the steepest edge the terrain itself makes (one dug layer: {climbed:F2} m up over 2 m of slope) is walked up out of without leaving the floor, never slower than {slowest:F1} m/s");
        view.GetParent().RemoveChild(view);
        view.QueueFree();
        yield return 0;
    }

    // ==== Hover rover over a trench, gear on dug ground ==========================================

    IEnumerable<int> RoverTrench()
    {
        Ship rover = _w.Rover;
        float laneZ = SandboxTerrain.LaneZ;
        rover.Vessel.Place(rover.Body, _w.Ground(-40f, laneZ) + N.Vector3.UnitY * 2.3f, -MathF.PI / 2f);
        rover.Binding.Teleport();
        for (int i = 0; i < 60; i++) yield return 0;
        int trench = 0;
        for (int dz = -7; dz <= 7; dz++)
            for (int x = 0; x < 2; x++)
                if (_w.GroundTop(x + 0.5f, laneZ + dz) < _w.Store.Source.SurfaceHeight(x + 0.5f, laneZ + dz) - 1.5f) trench++;
        _impacts.Clear();
        float lowest = float.PositiveInfinity;
        for (int t = 0; t < 300 && rover.Body.Position.X < 20f; t++)
        {
            rover.Flight.Input = PilotInput.Hands(new Vector3(0f, 0f, -0.5f));
            yield return 0;
            float x = rover.Body.Position.X;
            if (x > -4f && x < 6f) lowest = MathF.Min(lowest, rover.Vessel.Hover.Clearance);
        }
        rover.Flight.Input = PilotInput.LetGo;
        bool touched = _impacts.Any(i => i.Body == rover.Body && i.Other == null);
        Check(trench >= 25 && rover.Body.Position.X > 15f && !touched && lowest > 0.8f,
            $"the hover rover crosses the 2 m dug trench at {rover.Body.Velocity.Length():F0} m/s without touching it: its engines dip to {lowest:F2} m over the hole (ride height 1.5)");
        for (int i = 0; i < 90; i++) yield return 0;
    }

    /// <summary>What one drive into a hole and out of it saw.</summary>
    sealed class HoleRun
    {
        public string Name;
        public float Depth, Seconds, NoseDown, NoseUp, Bottom, Steepest, Slowest = float.PositiveInfinity;
        public bool Bottomed, Out;
        public int Contacts, Refused;
    }

    /// <summary>
    /// Hover vessels follow the ground's attitude: the rover drives at a crawl into a pit dug the
    /// survival tool's way and into the biggest crater a crash can cut, stops at the bottom, and
    /// drives on up and out over the far rim, its nose down going in and up coming out, never stuck.
    /// </summary>
    IEnumerable<int> HoverHoles()
    {
        Ship rover = _w.Rover;
        float laneZ = SandboxTerrain.LaneZ;
        rover.Vessel.Place(rover.Body, _w.Ground(-36f, laneZ) + N.Vector3.UnitY * 2.3f, -MathF.PI / 2f);
        rover.Binding.Teleport();
        rover.Flight.Input = PilotInput.LetGo;

        // A pit dug with the survival tool's 1.4 m balls: 10 m along the lane, 7 m across, two balls deep.
        N.Vector3 pit = _w.Ground(-18f, laneZ);
        for (float dx = -5f; dx <= 5.01f; dx += 1.25f)
            for (float dz = -3f; dz <= 3.01f; dz += 1.5f)
                for (int layer = 0; layer < 2; layer++)
                    _w.Terrain.Dig(TInt3.Floor(new N.Vector3(pit.X + dx, pit.Y - 0.5f - layer * 1.3f, pit.Z + dz)), 1.4f);

        // The biggest crater a crash cuts (the 6 m cap), its blast sphere where a crash puts it.
        N.Vector3 rim = _w.Ground(24f, laneZ);
        float radius = _w.Rules.Craters.MaxRadius;
        CraterResult crater = _w.Terrain.Crater(rim + N.Vector3.UnitY * radius * _w.Rules.Craters.Lift, radius);
        _w.Physics.NotifyChanged(crater.Changed);
        for (int i = 0; i < 3; i++) yield return 0;

        var runs = new List<HoleRun>();
        foreach (var (name, centre, from) in new[] { ("dug pit", pit, -36f), ("crater", rim, 8f) })
        {
            var run = new HoleRun { Name = name, Depth = centre.Y - _w.GroundTop(centre.X, centre.Z) };
            foreach (int x in ThroughHole(rover, centre, from, run)) yield return x;
            runs.Add(run);
            GD.Print($"INFO  {name}: {run.Depth:F1} m deep; in and out in {run.Seconds:F1} s, nose {run.NoseDown:F0}° down going in and {run.NoseUp:F0}° up coming out, " +
                     $"steepest grade ahead {run.Steepest:F0}°, slowest climbing {run.Slowest:F1} m/s, {run.Contacts} hull contacts, {run.Refused} ticks refused");
        }
        foreach (HoleRun run in runs)
            Check(run.Depth > 2f && run.Bottomed && run.Out && run.Slowest > 1f && run.NoseDown < -8f && run.NoseUp > 8f,
                $"the hover rover drives at a crawl into a {run.Name} {run.Depth:F1} m deep, stops at the bottom ({run.Bottom:F1} m down) and drives on up and out " +
                $"over the far rim in {run.Seconds:F1} s, following the ground: nose {-run.NoseDown:F0}° down going in, {run.NoseUp:F0}° up coming out, " +
                $"never slower than {run.Slowest:F1} m/s on the way out (steepest grade ahead {run.Steepest:F0}°, under the {_w.Rules.Hover.MaxSlope:0}° limit)");

        // A cliff is still refused: driven west from the basin at the bank, which rises 13 m in about 5 m.
        N.Vector3 basin = _w.Ground(68f, -30f);
        rover.Vessel.Place(rover.Body, basin + N.Vector3.UnitY * 2.3f, MathF.PI / 2f);
        rover.Binding.Teleport();
        int refused = 0;
        float highest = float.NegativeInfinity, west = float.PositiveInfinity;
        for (int t = 0; t < 360; t++)
        {
            rover.Flight.Input = PilotInput.Hands(new Vector3(0f, 0f, -0.15f));
            yield return 0;
            if (rover.Vessel.Hover.SlopeRefused) refused++;
            highest = MathF.Max(highest, rover.Vessel.Hover.GroundRef - basin.Y);
            west = MathF.Min(west, rover.Body.Position.X);
        }
        float bankTop = _w.Ground(44f, -30f).Y - basin.Y;
        Check(refused > 60 && highest < 0.5f * bankTop && west > 50f,
            $"and a cliff is still refused: driven at the {bankTop:F0} m bank from the basin, it climbs no more than {highest:F1} m of it and stops at its foot " +
            $"({refused * Dt:F1} s refused, never west of x {west:F0})");
        rover.Flight.Input = PilotInput.LetGo;
        for (int i = 0; i < 30; i++) yield return 0;
    }

    /// <summary>Drives the rover east along the lane from <paramref name="from"/> into a hole, stops it at the bottom, then drives it out.</summary>
    IEnumerable<int> ThroughHole(Ship rover, N.Vector3 centre, float from, HoleRun run)
    {
        rover.Vessel.Place(rover.Body, _w.Ground(from, centre.Z) + N.Vector3.UnitY * 2.3f, -MathF.PI / 2f);
        rover.Binding.Teleport();
        for (int i = 0; i < 40; i++)
        {
            rover.Flight.Input = PilotInput.LetGo;
            yield return 0;
        }
        _impacts.Clear();
        var crawl = PilotInput.Hands(new Vector3(0f, 0f, -0.15f));
        float Pitch() => Units.Degrees(MathF.Asin(Math.Clamp(MathX.Forward(rover.Body.Rotation).Y, -1f, 1f)));

        // In, until the hole's middle is under it, then hands off: it stops at the bottom.
        int ticks = 0;
        for (; ticks < 600 && rover.Body.Position.X < centre.X; ticks++)
        {
            rover.Flight.Input = crawl;
            yield return 0;
            run.NoseDown = MathF.Min(run.NoseDown, Pitch());
        }
        for (int i = 0; i < 60; i++)
        {
            rover.Flight.Input = PilotInput.LetGo;
            yield return 0;
            ticks++;
        }
        run.Bottom = centre.Y - rover.Vessel.Hover.GroundRef;
        run.Bottomed = run.Bottom > run.Depth * 0.6f && MathX.Flat(rover.Body.Velocity).Length() < 0.5f;

        // Out over the far rim, from a standstill at the bottom.
        int climbing = 0;
        for (; ticks < 1500 && rover.Body.Position.X < centre.X + 14f; ticks++, climbing++)
        {
            rover.Flight.Input = crawl;
            yield return 0;
            HoverController hover = rover.Vessel.Hover;
            run.NoseUp = MathF.Max(run.NoseUp, Pitch());
            run.Steepest = MathF.Max(run.Steepest, hover.SteepestAhead);
            if (hover.SlopeRefused) run.Refused++;
            if (climbing > 60) run.Slowest = MathF.Min(run.Slowest, MathX.Flat(rover.Body.Velocity).Length());
        }
        run.Out = rover.Body.Position.X >= centre.X + 14f;
        run.Seconds = ticks * Dt;
        run.Contacts = _impacts.Count(i => i.Body == rover.Body && i.Other == null);
    }

    IEnumerable<int> GearOnUnevenGround()
    {
        // Uneven ground near the field where every one of the Corsair's telescoping legs can reach
        // its own: the hull comes to rest on the lift jets hanging a metre below its pads, so each
        // pad's ground must lie within its 1.4 m reach of that, and no two pads' ground agree.
        var pads = new N.Vector2[] { new(0f, -8f), new(-3f, 5f), new(3f, 5f) };
        var jets = new N.Vector2[] { new(-3f, -6f), new(3f, -6f), new(-3f, 3f), new(3f, 3f) };
        float At(N.Vector2 c, N.Vector2 u) => _w.Ground(c.X + u.X, c.Y + u.Y).Y;
        var site = new N.Vector2(-42f, -8f);
        float best = float.MaxValue;
        for (float dx = -40f; dx <= 40f; dx += 2f)
            for (float dz = -40f; dz <= 40f; dz += 2f)
            {
                var c = new N.Vector2(-42f + dx, -8f + dz);
                float rest = jets.Max(u => At(c, u)), hip = rest + 1f;
                float low = pads.Min(u => At(c, u)), high = pads.Max(u => At(c, u));
                bool reach = hip - low < 1.3f && high < hip - 0.1f;
                float miss = MathF.Abs(high - low - 0.6f); // about 0.6 m between the pads' grounds
                if (reach && miss < best) (best, site) = (miss, c);
            }
        Ship ship = _w.SpawnVessel("corsair.gbp", _w.Ground(site.X, site.Y) + N.Vector3.UnitY * 9f, 0f, landed: false);
        int landed = _landed;
        LandingGear gear = ship.Vessel.Gear;
        int want = gear.Count;
        bool cameOut = false;
        for (int t = 0; t < 900 && _landed == landed; t++)
        {
            // Ease down at 3.5 m/s until every foot is on its ground (or the hull rests), then let go.
            bool down = Enumerable.Range(0, gear.Count).Count(gear.Touching) < want && !ship.Body.OnFloor;
            ship.Flight.Input = down ? PilotInput.Hands(new Vector3(0f, -0.05f, 0f)) : PilotInput.LetGo;
            yield return 0;
            cameOut |= gear.Deployed;
        }
        gear.Probe(_w.Physics, ship.Body);
        float worstFoot = 0f, shortest = float.MaxValue, longest = 0f;
        float minGround = float.MaxValue, maxGround = float.MinValue;
        int standing = 0;
        for (int i = 0; i < gear.Count; i++)
        {
            if (!gear.Touching(i)) continue;
            standing++;
            // Each telescoping leg reaches its own ground: its foot is on it, whatever the others stand on.
            float foot = ship.Body.ToParent(gear.Leg(i).Hip).Y - gear.Reach(i) - gear.FootGround[i].Y;
            worstFoot = MathF.Max(worstFoot, MathF.Abs(foot));
            shortest = MathF.Min(shortest, gear.Reach(i));
            longest = MathF.Max(longest, gear.Reach(i));
            minGround = MathF.Min(minGround, gear.FootGround[i].Y);
            maxGround = MathF.Max(maxGround, gear.FootGround[i].Y);
        }
        Check(_landed > landed && cameOut && ship.Vessel.Frozen && gear.Attached && standing >= 2 && worstFoot < 0.25f
              && maxGround - minGround > 0.3f && longest - shortest > 0.3f && !_w.Physics.Stuck.IsStuck(ship.Body, out _),
            $"landing gear on uneven ground: it comes out by itself, and {standing} of {gear.Count} telescoping legs reach their own ground, " +
            $"{maxGround - minGround:F1} m uneven, out {shortest:F2} to {longest:F2} m, every foot within {worstFoot * 100f:F0} cm of it; attached, it raises Landed and stops simulating");

        // And it goes in again by itself as the craft climbs away: there is no button.
        int climb = 0;
        for (; climb < 900 && gear.Extension > 0f; climb++)
        {
            ship.Flight.Input = PilotInput.Hands(new Vector3(0f, 1f, -1f));
            yield return 0;
        }
        Check(gear.Extension == 0f && !gear.Down && !gear.Contact,
            $"climbing away, the gear goes in by itself: in after {climb * Dt:F1} s, {gear.FootAltitude:F0} m up at {ship.Body.Velocity.Length():F0} m/s");
        _w.Remove(ship);
        yield return 0;
    }

    // ==== Wind: bases, parked vessels, capital ships ============================================

    /// <summary>
    /// The wind rules, in a hurricane, in a fresh world of its own (so nothing else on the map is
    /// blown about): a base never moves; a Corsair landed on it feels no wind and stays put on it
    /// (one landed on the bare ground beside it does feel the wind); a capital ship flying on its
    /// autopilot is pushed by its own mass, and the Corsair landed on its deck and a rover docked to
    /// it ride along without a push of their own; once the Corsair lifts off, the wind has it again.
    /// </summary>
    IEnumerable<int> WindRules()
    {
        var view = new SubViewport { OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled, Size = new Vector2I(2, 2) };
        AddChild(view);
        var w = new SandboxWorld { Visuals = false, Populate = false };
        view.AddChild(w);
        w.Weather.Calm = true;
        var lifted = new List<Body>();
        w.Physics.LiftedOff += lifted.Add;
        for (int i = 0; i < 3; i++) yield return 0;

        // A base on the landing field, a Corsair landed on it and another on the bare ground beside it.
        Ship pad = w.SpawnBase("pad.gbp", w.Ground(0f, 0f), 0f);
        Ship onPad = w.SpawnLandedAt("corsair.gbp", pad.Body.ToParent(new N.Vector3(0f, 1f, 0f)), 0f);
        Ship onGround = w.SpawnLanded("corsair.gbp", new N.Vector2(0f, -15f), 0f);

        // A capital ship flying on at 4 m/s with nobody at the controls (its autopilot holds course).
        Ship tender = w.SpawnVessel("tender.gbp", new N.Vector3(50f, 110f, 30f), 0f, landed: false);
        tender.Body.Velocity = MathX.Forward(tender.Body.Rotation) * 4f;
        tender.Flight.Input = PilotInput.Autopilot;
        for (int i = 0; i < 3; i++) yield return 0;
        // A Corsair landed on its deck, and a hover rover docked to it.
        Ship onDeck = w.SpawnLandedAt("corsair.gbp", tender.Body.ToParent(new N.Vector3(0f, 1f, 3f)), 0f);
        Ship docked = w.SpawnVessel("sandflea.gbp", tender.Body.ToParent(new N.Vector3(-3.5f, 4f, -7f)), 0f, landed: false);
        docked.Vessel.Place(docked.Body, tender.Body.ToParent(new N.Vector3(-3.5f, 1.05f + docked.Body.Shape.FeetBelowOrigin, -7f)), 0f);
        docked.Vessel.Dock(w.Physics, docked.Body, tender.Body);
        docked.Binding.Teleport();
        for (int i = 0; i < 5; i++) yield return 0;

        bool parked = onPad.Body.ParkedOn == pad.Body && onDeck.Body.ParkedOn == tender.Body && docked.Body.ParkedOn == tender.Body
                      && onGround.Body.ParkedOn == null;
        N.Vector3 padAt = pad.Body.Position;
        N.Quaternion padRot = pad.Body.Rotation;
        N.Vector3 onPadAt = onPad.Body.Position;
        N.Vector3 deckLocal = tender.Body.ToLocal(onDeck.Body.Position), dockLocal = tender.Body.ToLocal(docked.Body.Position);
        N.Quaternion deckTurn = N.Quaternion.Conjugate(tender.Body.Rotation) * onDeck.Body.Rotation;
        N.Vector3 tenderV0 = tender.Body.Velocity;

        w.Weather.Calm = false;
        w.Weather.Sim.Snap(WeatherKind.Hurricane);
        float sheltered = 0f, onGroundPush = 0f, padMoved = 0f, onPadMoved = 0f, deckSlip = 0f, deckTurned = 0f, dockSlip = 0f;
        float tenderPush = 0f, worstFormula = 0f;
        N.Vector3 integrated = N.Vector3.Zero;
        bool stayedParked = true;
        int ticks = 180;
        for (int t = 0; t < ticks; t++)
        {
            yield return 0;
            foreach (Body b in new[] { pad.Body, onPad.Body, onDeck.Body, docked.Body })
                sheltered = MathF.Max(sheltered, b.LastForce(ForceKind.Weather).Length());
            onGroundPush = MathF.Max(onGroundPush, onGround.Body.LastForce(ForceKind.Weather).Length());
            padMoved = MathF.Max(padMoved, N.Vector3.Distance(pad.Body.Position, padAt) + (pad.Body.Rotation == padRot ? 0f : 1f));
            onPadMoved = MathF.Max(onPadMoved, N.Vector3.Distance(onPad.Body.Position, onPadAt));
            deckSlip = MathF.Max(deckSlip, N.Vector3.Distance(tender.Body.ToLocal(onDeck.Body.Position), deckLocal));
            deckTurned = MathF.Max(deckTurned, AngleBetween(N.Quaternion.Conjugate(tender.Body.Rotation) * onDeck.Body.Rotation, deckTurn));
            dockSlip = MathF.Max(dockSlip, N.Vector3.Distance(tender.Body.ToLocal(docked.Body.Position), dockLocal));
            stayedParked &= onPad.Body.ParkedOn == pad.Body && onDeck.Body.ParkedOn == tender.Body && docked.Body.ParkedOn == tender.Body;

            // The capital ship's own push: wind drag on its drag area, ½ρ·CdA·|w|·w, and it alone changes its course.
            N.Vector3 push = tender.Body.LastForce(ForceKind.Weather);
            N.Vector3 wind = w.Weather.Sim.WindAt(tender.Body.Position.ToGodot()).ToNumerics();
            N.Vector3 drag = 0.5f * ForceModule.AirDensity * tender.Body.DragArea * wind.Length() * wind;
            worstFormula = MathF.Max(worstFormula, N.Vector3.Distance(push, drag) / MathF.Max(1f, drag.Length()));
            tenderPush = MathF.Max(tenderPush, push.Length());
            integrated += push / tender.Body.Mass * Dt;
        }
        N.Vector3 gained = tender.Body.Velocity - tenderV0;
        float pushedError = N.Vector3.Distance(gained, integrated) / MathF.Max(1e-3f, integrated.Length());
        Check(parked && stayedParked && sheltered == 0f && padMoved == 0f && onPadMoved < 1e-4f && onGroundPush > 1000f,
            $"a {w.Weather.Sim.WindAt(pad.Body.Position.ToGodot()).Length():F0} m/s hurricane never moves a base (0 N on it, not a hair in {ticks * Dt:F0} s), " +
            $"and a Corsair landed on it is parked there: no wind force at all, {onPadMoved * 1000f:F2} mm moved; the same Corsair landed on the bare ground beside it takes up to {onGroundPush / 1000f:F1} kN");
        Check(stayedParked && sheltered == 0f && deckSlip < 1e-3f && deckTurned < 0.01f && dockSlip < 1e-3f && gained.Length() > 0.5f,
            $"a Corsair landed on a capital ship's deck and a rover docked to it feel no wind of their own and ride along as the ship is pushed: " +
            $"{deckSlip * 1000f:F2} mm and {deckTurned:F4}° off their spot on the deck (rover {dockSlip * 1000f:F2} mm) while the ship goes from {tenderV0.Length():F1} to {tender.Body.Velocity.Length():F1} m/s");
        Check(tenderPush > 10_000f && worstFormula < 0.02f && pushedError < 0.01f,
            $"the capital ship itself is pushed, at its mass: up to {tenderPush / 1000f:F0} kN of ½ρ·CdA·|w|·w on its {tender.Body.DragArea:F0} m² (formula off by at most {worstFormula:P1}) " +
            $"is {tenderPush / tender.Body.Mass:F2} m/s² on its {tender.Body.Mass / 1000f:F0} t; its course changed by {gained.Length():F2} m/s, exactly the push over its mass (off by {pushedError:P2})");

        // A chain: a rover parked on the Corsair that is itself parked on the deck. It rides the ship
        // with the Corsair, and the ship's thrust carries all three (one way only: nothing loops back).
        Ship onCorsair = w.SpawnVessel("sandflea.gbp", onDeck.Body.Position + N.Vector3.UnitY * 8f, 0f, landed: false);
        float corsairTop = onDeck.Body.Shape.Height - onDeck.Body.Shape.FeetBelowOrigin;
        onCorsair.Vessel.Place(onCorsair.Body, onDeck.Body.ToParent(new N.Vector3(0f, corsairTop + onCorsair.Body.Shape.FeetBelowOrigin + 0.05f, 0f)), MathX.Heading(onDeck.Body.Rotation));
        onCorsair.Vessel.Dock(w.Physics, onCorsair.Body, onDeck.Body);
        onCorsair.Binding.Teleport();
        N.Vector3 chainLocal = onDeck.Body.ToLocal(onCorsair.Body.Position);
        bool loops = w.Physics.Park(tender.Body, onCorsair.Body) || w.Physics.Park(onDeck.Body, onCorsair.Body) || tender.Body.ParkedOn != null;
        float chainSlip = 0f;
        N.Vector3 tenderStart = tender.Body.Position;
        for (int t = 0; t < 60; t++)
        {
            yield return 0;
            chainSlip = MathF.Max(chainSlip, N.Vector3.Distance(onDeck.Body.ToLocal(onCorsair.Body.Position), chainLocal));
        }
        float load = onDeck.Body.Mass + docked.Body.Mass + onCorsair.Body.Mass;
        float statsMass = tender.Flight.Stats.Mass, own = HandlingModel.Mass(tender.Spec);
        Check(onCorsair.Body.ParkedOn == onDeck.Body && onDeck.Body.ParkedOn == tender.Body && chainSlip < 1e-3f && !loops
              && N.Vector3.Distance(tender.Body.Position, tenderStart) > 0.5f,
            $"chains carry down: a rover parked on the Corsair parked on the deck rides the ship with it ({chainSlip * 1000f:F2} mm off its spot as the ship goes " +
            $"{N.Vector3.Distance(tender.Body.Position, tenderStart):F1} m), and parking is one way: the ship can't park on what it carries");
        Check(MathF.Abs(tender.Body.Carried - load) < 1f && MathF.Abs(onDeck.Body.Carried - onCorsair.Body.Mass) < 1f
              && MathF.Abs(statsMass - own - Units.Tonnes(load)) < 0.01f,
            $"a parked craft's mass is its host's to move: the ship carries {Units.Tonnes(tender.Body.Carried):F1} t (the Corsair, the rover on it and the docked rover), " +
            $"and its handling model flies {statsMass:F1} t ({own:F1} t of its own)");

        // The Corsair on the deck lifts off: the moment it leaves its host the wind has it again, and
        // it takes the rover parked on it along.
        float before = onDeck.Body.LastForce(ForceKind.Weather).Length();
        float liftedPush = 0f, rose = 0f;
        for (int t = 0; t < 60; t++)
        {
            onDeck.Flight.Input = PilotInput.Hands(new Vector3(0f, 1f, 0f));
            yield return 0;
            liftedPush = onDeck.Body.LastForce(ForceKind.Weather).Length();
            rose = tender.Body.ToLocal(onDeck.Body.Position).Y - deckLocal.Y;
        }
        Check(before == 0f && lifted.Contains(onDeck.Body) && onDeck.Body.ParkedOn == null && liftedPush > 500f && rose > 1f,
            $"once the Corsair lifts off the deck ({rose:F1} m up off it) it is no longer parked, and the wind pushes it again: {liftedPush / 1000f:F1} kN");
        float carriedSlip = N.Vector3.Distance(onDeck.Body.ToLocal(onCorsair.Body.Position), chainLocal);
        Check(onCorsair.Body.ParkedOn == onDeck.Body && carriedSlip < 1e-3f && MathF.Abs(tender.Body.Carried - docked.Body.Mass) < 1f,
            $"moving under its own power unparked it from the ship, and it took the rover on it along ({carriedSlip * 1000f:F2} mm off its spot); the ship now carries {Units.Tonnes(tender.Body.Carried):F1} t");

        view.GetParent().RemoveChild(view);
        view.QueueFree();
        yield return 0;
    }

    // ==== Water: a volume, not a surface ========================================================

    /// <summary>
    /// The lake in the basin, through the shared force path: an ore crate sinks to the bed and a
    /// supply crate floats; the player walks in, swims across and out the far side, dives 10 m and
    /// surfaces; a Corsair that loses its power sinks and settles on the bed without tunnelling; a
    /// powered one is flown under water, slowly, and holds its depth hands off; the hover rover
    /// rides the surface; and the stuck guard never mistakes any of it for being stuck.
    /// </summary>
    IEnumerable<int> WaterVolume()
    {
        N.Vector2 lake = SandboxTerrain.LakeCenter;
        float level = SandboxTerrain.LakeLevel;
        var source = (SandboxTerrain)_w.Store.Source;
        var watched = new List<Body>();
        var streaks = new Dictionary<Body, int>();
        var tally = new StuckTally();
        int rescuesBefore = _rescues.Count;
        void Watching(string when)
        {
            Watch(watched, streaks, when, tally);
        }

        // Crates: an ore crate denser than water, a supply crate lighter, dropped in off to one side.
        Crate light = Crate.Spawn(_w, new N.Vector3(lake.X + 6f, level + 1f, lake.Y - 3f), 1);
        Crate heavy = Crate.Spawn(_w, new N.Vector3(lake.X + 6f, level + 1f, lake.Y + 3f), 2, "ore");
        watched.Add(light.Body);
        watched.Add(heavy.Body);

        // The player walks in from the southern beach, swims north across the lake and out on the far side.
        Player p = _w.Player;
        watched.Add(p.Body);
        float beachZ = lake.Y + SandboxTerrain.LakeRadiusZ * 1.18f, farZ = lake.Y - SandboxTerrain.LakeRadiusZ * 1.1f;
        p.PlaceOutside(_w.Ground(lake.X - 4f, beachZ) + N.Vector3.UnitY * 0.05f);
        p.Yaw = 0f; // north
        p.Pitch = 0f;
        for (int i = 0; i < 10; i++) yield return 0;
        bool swam = false, walkedOut = false, crateSettled = false;
        float deepest = 0f, swimSpeed = 0f;
        int calm = 0, ticks = 0;
        while (ticks++ < 3000 && !(walkedOut && crateSettled))
        {
            p.Legs.Move = walkedOut ? N.Vector2.Zero : new N.Vector2(0f, -1f);
            yield return 0;
            Watching("swimming across");
            WaterState wet = p.Body.Water;
            if (p.Legs.Swimming)
            {
                swam = true;
                deepest = MathF.Max(deepest, wet.Fraction);
                if (MathF.Abs(p.Body.Position.Z - lake.Y) < 6f) swimSpeed = MathF.Max(swimSpeed, MathX.Flat(p.Body.Velocity).Length());
            }
            walkedOut |= swam && p.Body.Position.Z < farZ && wet.Fraction == 0f && p.Body.OnFloor;
            bool still = light.Body.Velocity.Length() < 0.03f && heavy.Body.Velocity.Length() < 0.03f;
            calm = still ? calm + 1 : 0;
            crateSettled = calm > 30;
        }
        p.Legs.Move = N.Vector2.Zero;
        Check(swam && walkedOut && deepest < 1f && swimSpeed > 1.8f && swimSpeed < _w.Rules.Water.SwimSpeed + 0.2f,
            $"the player walks into the lake, swims across it ({swimSpeed:F1} m/s mid-lake, at most {deepest:P0} of them under: head above water) and walks out on the far shore " +
            $"({ticks * Dt:F0} s beach to beach)");

        float bed = source.SurfaceHeight(heavy.Body.Position.X, heavy.Body.Position.Z);
        float floatAt = light.Body.CenterOfMassPosition.Y, sunkAt = heavy.Body.CenterOfMassPosition.Y;
        float half = light.Kind.Size * 0.5f, share = light.Kind.Density / _w.Rules.Water.Density;
        float floatsAt = level - half + share * light.Kind.Size; // its centre, with that share of it under
        Check(crateSettled && MathF.Abs(floatAt - floatsAt) < 0.12f && MathF.Abs(light.Body.Water.Fraction - share) < 0.1f
              && MathF.Abs(sunkAt - (bed + half)) < 0.25f && heavy.Body.Water.Underwater,
            $"water buoys crates by their density in data: the {light.Kind.Density:F0} kg/m³ supply crate floats {light.Body.Water.Fraction:P0} under (it should be {share:P0}), its centre {floatAt - level:+0.00;-0.00} m from the surface; " +
            $"the {heavy.Kind.Density:F0} kg/m³ ore crate sinks {level - sunkAt:F1} m to the bed and rests on it ({sunkAt - bed:F2} m centre above the bed)");

        // Dive 10 m in the middle of the lake, then come back up.
        p.PlaceOutside(new N.Vector3(lake.X - 4f, level - 1.5f, lake.Y));
        for (int i = 0; i < 90; i++)
        {
            yield return 0;
            Watching("floating");
        }
        bool floating = p.Legs.Swimming && !p.Underwater && p.Body.Water.Fraction > 0.6f;
        float floatShare = p.Body.Water.Fraction;
        float feetDepth = 0f;
        bool underFlag = false;
        int down = 0;
        while (down++ < 900 && feetDepth < 10f)
        {
            p.Legs.Vertical = -1f;
            yield return 0;
            Watching("diving");
            feetDepth = MathF.Max(feetDepth, level - p.Body.Position.Y);
            underFlag |= p.Underwater;
        }
        bool stillUnder = p.Underwater;
        int up = 0;
        while (up++ < 900 && p.Underwater)
        {
            p.Legs.Vertical = 1f;
            yield return 0;
            Watching("surfacing");
        }
        p.Legs.Vertical = 0f;
        for (int i = 0; i < 30; i++)
        {
            yield return 0;
            Watching("surfaced");
        }
        Check(floating && feetDepth >= 10f && underFlag && stillUnder && !p.Underwater && p.Legs.Swimming,
            $"left alone the player floats with {floatShare:P0} of them under and their head out; Ctrl dives them {feetDepth:F1} m down in {down * Dt:F1} s " +
            $"(under water: the vitals flag says the suit is on its own oxygen) and Space brings them back up in {up * Dt:F1} s, head out again");

        // A Corsair over the lake loses its generator: no power, it falls in, sinks and settles on the bed.
        Ship sinker = _w.SpawnVessel("corsair.gbp", new N.Vector3(lake.X, level + 8f, lake.Y), 0.3f, landed: false);
        sinker.Flight.Input = PilotInput.Autopilot;
        watched.Add(sinker.Body);
        for (int i = 0; i < 20; i++) yield return 0;
        sinker.ShootThrough(new[] { new SInt3(-2, 0, 3) }); // the generator, set into the deck
        int landedBefore = _landed;
        float lowestKeel = float.PositiveInfinity, sinkSpeed = 0f;
        bool wentUnder = false;
        for (int t = 0; t < 2400 && _landed == landedBefore; t++)
        {
            yield return 0;
            Watching("sinking");
            Body b = sinker.Body;
            wentUnder |= b.Water.Underwater;
            if (b.Water.Underwater) sinkSpeed = MathF.Max(sinkSpeed, -b.Velocity.Y);
            float keel = b.Position.Y - b.Shape.FeetBelowOrigin;
            lowestKeel = MathF.Min(lowestKeel, keel - source.SurfaceHeight(b.Position.X, b.Position.Z));
        }
        float restsAt = sinker.Body.Position.Y - source.SurfaceHeight(sinker.Body.Position.X, sinker.Body.Position.Z);
        Check(!sinker.Stats.CanLiftOff && wentUnder && sinker.Vessel.Frozen && _landed > landedBefore && sinker.Body.Water.Underwater
              && lowestKeel > -0.3f && !_w.Physics.Stuck.IsStuck(sinker.Body, out _),
            $"a Corsair that loses its generator over the lake falls in and sinks ({sinkSpeed:F1} m/s at most under water, against buoyancy and heavy drag), " +
            $"settles on the bed {level - sinker.Body.Position.Y:F1} m down on its legs and lands, never through it (its keel never below the bed: lowest {lowestKeel:+0.00;-0.00} m)");
        _w.Remove(sinker);
        watched.Remove(sinker.Body);

        // A powered Corsair is flown down under the surface: it holds its depth hands off, and
        // full ahead it crawls through the drag.
        Ship diver = _w.SpawnVessel("corsair.gbp", new N.Vector3(lake.X, level + 6f, lake.Y), 0f, landed: false);
        watched.Add(diver.Body);
        float descent = 0f;
        int dived = 0;
        while (dived++ < 900 && diver.Body.Position.Y > level - 6f)
        {
            diver.Flight.Input = PilotInput.Hands(new Vector3(0f, -1f, 0f));
            yield return 0;
            Watching("flown under");
            if (diver.Body.Water.Underwater) descent = MathF.Max(descent, -diver.Body.Velocity.Y);
        }
        diver.Flight.Input = PilotInput.LetGo;
        for (int i = 0; i < 60; i++)
        {
            yield return 0;
            Watching("holding depth");
        }
        N.Vector3 held = diver.Body.Position;
        float drift = 0f;
        for (int i = 0; i < 120; i++)
        {
            yield return 0;
            Watching("holding depth");
            drift = MathF.Max(drift, N.Vector3.Distance(diver.Body.Position, held));
        }
        float crawl = 0f;
        for (int i = 0; i < 90; i++)
        {
            diver.Flight.Input = PilotInput.Hands(new Vector3(0f, 0f, -1f));
            yield return 0;
            Watching("flown under");
            crawl = MathF.Max(crawl, diver.Body.Velocity.Length());
        }
        diver.Flight.Input = PilotInput.LetGo;
        float top = diver.Stats.TopSpeed;
        Check(diver.Body.Water.Underwater && !diver.Vessel.Frozen && drift < 0.1f && crawl > 1f && crawl < 0.15f * top && descent > 0.5f,
            $"a powered Corsair is flown under the water: it goes down at up to {descent:F1} m/s, holds its depth hands off ({drift * 100f:F1} cm in 2 s: its engines carry its weight less its buoyancy), " +
            $"and full ahead the heavy drag holds it to {crawl:F1} m/s (top speed in air {top:F0})");
        _w.Remove(diver);
        watched.Remove(diver.Body);

        // The hover rover rides the surface, powered, as it rides the ground, and drives out up the
        // far shore. Its ride height is judged over open water: nearing a shore, the look-ahead
        // lifts it early for the bank, as for any rise.
        Ship skiff = _w.SpawnVessel("sandflea.gbp", new N.Vector3(lake.X + 4f, level + 2.3f, lake.Y + 12f), 0f, landed: false);
        watched.Add(skiff.Body);
        float ride = 0f, worstRide = 0f, wetHull = 0f, overWater = 0f;
        for (int t = 0; t < 300; t++)
        {
            skiff.Flight.Input = PilotInput.Hands(new Vector3(0f, 0f, -0.25f));
            yield return 0;
            Watching("hovering");
            ride = skiff.Stats.RideHeight;
            HoverController h = skiff.Vessel.Hover;
            wetHull = MathF.Max(wetHull, skiff.Body.Water.Fraction);
            bool openWater = SandboxTerrain.LakeReach(skiff.Body.Position.X, skiff.Body.Position.Z) < 0.6f && MathF.Abs(h.GroundRef - level) < 0.05f;
            if (t < 30 || !openWater) continue;
            overWater += Dt;
            worstRide = MathF.Max(worstRide, MathF.Abs(h.Clearance - ride));
        }
        float ashore = skiff.Vessel.Hover.GroundRef - level;
        bool outOfLake = SandboxTerrain.LakeReach(skiff.Body.Position.X, skiff.Body.Position.Z) > 1f && ashore > 0.5f;
        Check(overWater > 1f && worstRide < 0.25f && wetHull == 0f && outOfLake,
            $"the hover rover rides the lake's surface with its hover powered: {overWater:F1} s over open water, its engines {ride:F1} m above it (within {worstRide:F2} m), " +
            $"its hull never wet, and drives out up the far shore ({ashore:F1} m above the water)");
        _w.Remove(skiff);
        watched.Remove(skiff.Body);

        int wrongly = _rescues.Skip(rescuesBefore).Count(r => r.Body == p.Body || r.Body == light.Body || r.Body == heavy.Body || r.Body.Kind == BodyKind.Vessel);
        Check(tally.Failures == 0 && wrongly == 0,
            $"nothing in the water is taken for stuck: floating, sunk, diving, swimming, landed on the bed, flown under or hovering, {tally.SeenInside} ticks seen inside anything and {wrongly} rescues" +
            (tally.First != null ? $". First failure: {tally.First}" : ""));

        _w.Remove(light);
        _w.Remove(heavy);
        p.PlaceOutside(_w.Ground(1.5f, 8.5f) + N.Vector3.UnitY * 0.05f);
        for (int i = 0; i < 5; i++) yield return 0;
    }

    // ==== Tornado ===============================================================================

    /// <summary>What one run of the tornado scenario measured.</summary>
    sealed class FlingRun
    {
        public bool Caught, Released, Landed, Dead, CrateKept;
        public float Peak, LandingSpeed, Raw, Share, CratePeak, CrateDamage, NoFlingShare;
        public int Ticks;

        /// <summary>Every number, exactly, for comparing runs.</summary>
        public string Key =>
            $"{Caught} {Released} {Landed} {Dead} {CrateKept} {Ticks} {Peak:R} {LandingSpeed:R} {Raw:R} {Share:R} {CratePeak:R} {CrateDamage:R} {NoFlingShare:R}";
    }

    /// <summary>The player's health when the tornado catches them: the landing takes what it takes, with nothing held back.</summary>
    const float FlingStartShare = 0.6f;

    /// <summary>
    /// The tornado fling, run twice, each time in a fresh world of its own: its numbers depend on
    /// nothing that ran before (the weather's state, the tick count, what lies on the field), and
    /// the two runs must agree exactly.
    /// </summary>
    IEnumerable<int> TornadoFling()
    {
        var runs = new FlingRun[2];
        for (int r = 0; r < runs.Length; r++)
        {
            var view = new SubViewport { OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled, Size = new Vector2I(2, 2) };
            AddChild(view);
            var w = new SandboxWorld { Visuals = false };
            view.AddChild(w);
            w.Weather.Calm = true;
            for (int i = 0; i < 3; i++) yield return 0;
            runs[r] = new FlingRun();
            foreach (int x in Fling(w, runs[r])) yield return x;
            view.GetParent().RemoveChild(view); // out of the tree (and its physics space) at once
            view.QueueFree();
            yield return 0;
        }

        FlingRun a = runs[0];
        Check(a.Caught && a.Released && a.Landed && a.Raw > 0f && a.Share <= MathF.Max(0f, FlingStartShare - a.Raw) + 1e-4f,
            $"a tornado catches the player at {FlingStartShare:P0} health, carries them {a.Peak:F0} m up and flings them; the landing at {a.LandingSpeed:F0} m/s costs " +
            $"{a.Raw:P0} of their health in full, leaving {a.Share:P1}{(a.Dead ? " (dead)" : "")}: no floor");
        Check(a.CratePeak > 8f && a.CrateKept && a.CrateDamage >= 0f,
            $"the crate beside them is flung {a.CratePeak:F0} m up too, and is still there when it lands ({a.CrateDamage:P0} damage)");
        Check(MathF.Abs(a.NoFlingShare - a.Share) < 0.05f || a.NoFlingShare < FlingStartShare,
            $"the same fall without a fling hurts the same way, by the one impact rule ({a.NoFlingShare:P0} left)");
        Check(a.Key == runs[1].Key,
            $"two separate fresh worlds, one after the other, run the tornado scenario to identical numbers, bit for bit ({a.Ticks} ticks: " +
            $"{a.Peak:F2} m up, landing at {a.LandingSpeed:F3} m/s, {a.Raw:P2} of their health, the crate {a.CratePeak:F2} m up)");
    }

    /// <summary>One run of the tornado scenario in a world of its own, measured into <paramref name="run"/>.</summary>
    IEnumerable<int> Fling(SandboxWorld w, FlingRun run)
    {
        var impacts = new List<ImpactInfo>();
        w.Physics.Impact += impacts.Add;
        Player p = w.Player;
        var spot = w.Ground(-50f, 12f);
        p.PlaceOutside(spot + N.Vector3.UnitY * 0.05f);
        p.Health.Set(p.Health.Max * FlingStartShare);
        p.Health.RegenDelay = 1e6f; // no healing mid-test: the numbers stay readable
        Crate crate = Crate.Spawn(w, spot + new N.Vector3(3f, 0.45f, 0f), 2);
        w.Weather.Calm = false;
        w.Weather.Sim.Snap(WeatherKind.Clouds);
        for (int i = 0; i < 20; i++) yield return 0;
        // It touches down beside the player heading east, across the middle of the map, so the
        // carry and the fling stay inside the 256 m world.
        Tornado tornado = w.Weather.Sim.SpawnTornado(new Vector3(spot.X - 5f, spot.Y, spot.Z + 1f), new Vector3(spot.X + 60f, spot.Y, spot.Z + 1f));
        tornado.SkipFormation();
        impacts.Clear();

        float peak = spot.Y, cratePeak = spot.Y;
        for (int t = 0; t < 1500 && !run.Landed; t++)
        {
            yield return 0;
            run.Ticks++;
            VortexPhase phase = w.Weather.PhaseOf(p.Body);
            run.Caught |= phase == VortexPhase.Caught;
            if (run.Caught && phase == VortexPhase.Released && !run.Released)
            {
                run.Released = true;
                w.Weather.Sim.ClearTornadoes(); // gone once it has thrown you: no second catch
            }
            peak = MathF.Max(peak, p.Body.Position.Y);
            cratePeak = MathF.Max(cratePeak, crate.Body.Position.Y);
            run.Landed = run.Released && p.Body.OnFloor && p.Body.WorldVelocity.Length() < 1f;
        }
        ImpactInfo landing = impacts.Where(x => x.Body == p.Body && x.Point.Y < peak).OrderByDescending(x => x.Speed).FirstOrDefault();
        run.Peak = peak - spot.Y;
        run.LandingSpeed = landing?.Speed ?? 0f;
        run.Raw = landing == null ? 0f : Impacts.Damage(100f, landing.Speed, w.Rules.Contacts, BodyKind.Player) / p.Health.Max;
        run.Share = p.Health.Current / p.Health.Max;
        run.Dead = p.Health.Dead;
        run.CratePeak = cratePeak - spot.Y;
        run.CrateKept = w.Crates.Contains(crate);
        run.CrateDamage = crate.Body.Damage / crate.Body.Integrity;

        // The same fall without a fling has no floor at all.
        p.Health.Set(p.Health.Max * FlingStartShare);
        p.PlaceOutside(spot + N.Vector3.UnitY * (peak - spot.Y));
        for (int t = 0; t < 400 && !(p.Body.OnFloor && p.Body.Velocity.Length() < 1f); t++) yield return 0;
        run.NoFlingShare = p.Health.Current / p.Health.Max;
        w.Weather.Calm = true;
        w.Weather.Sim.ClearTornadoes();
    }

    // ==== Stuck-safety ==========================================================================

    IEnumerable<int> StuckSafety()
    {
        var rng = new Random(1234);
        Ship corsair = _w.Corsair;
        Player p = _w.Player;
        var creature = Creature.Spawn(_w, _w.Ground(10f, -10f) + N.Vector3.UnitY * 0.05f);
        var small = new List<Body> { p.Body, creature.Body };
        small.AddRange(_w.Crates.Select(c => c.Body));
        var counts = new int[6];
        int rescuesBefore = _w.Physics.Stuck.Rescues;
        int structureEdits = 0, terrainEdits = 0;
        var streaks = new Dictionary<Body, int>();
        var tally = new StuckTally();

        for (int op = 0; op < 1000; op++)
        {
            Body target = small[rng.Next(small.Count)];
            int kind = rng.Next(6);
            counts[kind]++;
            N.Vector3 near = new((float)(rng.NextDouble() * 60 - 30), 0, (float)(rng.NextDouble() * 60 - 30));
            switch (kind)
            {
                case 0: // dig under or beside a body
                {
                    N.Vector3 at = target.WorldPosition + new N.Vector3((float)rng.NextDouble() * 2 - 1, -(float)rng.NextDouble() * 1.5f, (float)rng.NextDouble() * 2 - 1);
                    if (_w.Terrain.Dig(TInt3.Floor(at), 1f + (float)rng.NextDouble()).Removed.Count > 0) terrainEdits++;
                    break;
                }
                case 1: // a crater near a body
                {
                    N.Vector3 at = target.WorldPosition + new N.Vector3((float)rng.NextDouble() * 6 - 3, 0f, (float)rng.NextDouble() * 6 - 3);
                    CraterResult r = _w.Terrain.Crater(at, 2f + 4f * (float)rng.NextDouble());
                    if (r.CellsRemoved > 0) { _w.Physics.NotifyChanged(r.Changed); terrainEdits++; }
                    break;
                }
                case 2: // a network correction or teleport puts a body into the ground
                {
                    N.Vector3 g = _w.Ground(near.X, near.Z);
                    Teleport(target, new N.Vector3(near.X, _w.GroundTop(near.X, near.Z) - (float)rng.NextDouble() * 3f, near.Z));
                    break;
                }
                case 3: // a block placed where a body stands, on the Corsair's flank
                {
                    if (PlaceBlockOn(corsair, target, rng)) structureEdits++;
                    break;
                }
                case 4: // a hard landing: thrown at the ground
                {
                    N.Vector3 g = new(near.X, _w.GroundTop(near.X, near.Z) + 4f, near.Z);
                    Teleport(target, g, new N.Vector3(0f, -(20f + 20f * (float)rng.NextDouble()), 0f));
                    break;
                }
                case 5: // a zone load: everything placed back, some of it inside the ground
                {
                    foreach (Body b in small)
                    {
                        float x = (float)(rng.NextDouble() * 60 - 30), z = (float)(rng.NextDouble() * 60 - 30);
                        Teleport(b, new N.Vector3(x, _w.GroundTop(x, z) - (float)rng.NextDouble() * 2f, z), notify: false);
                    }
                    _w.Physics.NotifyZoneLoaded();
                    break;
                }
            }
            // Watch every tick: a body may be seen inside for one tick (the guard needs two in a row
            // before it moves anyone), never for two. Nothing may ever fall out of the world.
            for (int tick = 0; tick < 2; tick++)
            {
                yield return 0;
                Watch(small.Append(_w.Rover.Body), streaks, $"op {op} (kind {kind})", tally);
            }
        }
        for (int tick = 0; tick < 3; tick++)
        {
            yield return 0;
            Watch(small.Append(_w.Rover.Body), streaks, "the end", tally);
        }
        bool clearAtEnd = small.All(b => !_w.Physics.Stuck.IsStuck(b, out _));
        int rescues = _w.Physics.Stuck.Rescues - rescuesBefore;
        Check(tally.Failures == 0 && clearAtEnd && rescues > 200,
            $"after 1000 random edits and impacts ({counts[0]} digs, {counts[1]} craters, {counts[2]} teleports into the ground, {counts[3]} blocks placed on bodies, " +
            $"{counts[4]} hard landings, {counts[5]} zone loads; {terrainEdits} terrain and {structureEdits} structure edits), nothing ends below the terrain or inside a structure: " +
            $"{rescues} rescues, every one within 2 frames (a body was seen inside on {tally.SeenInside} ticks, never two in a row), and none fell out of the world" +
            (tally.First != null ? $". First failure: {tally.First}" : ""));
        _w.Remove(creature);
        p.PlaceOutside(_w.Ground(1.5f, 8.5f) + N.Vector3.UnitY * 0.05f);
        for (int i = 0; i < 5; i++) yield return 0;
    }

    sealed class StuckTally
    {
        public int Failures, SeenInside;
        public string First;
    }

    /// <summary>One tick of watching: inside one tick is allowed (the guard waits for two), inside two ticks in a row is a failed rescue.</summary>
    void Watch(IEnumerable<Body> bodies, Dictionary<Body, int> streaks, string when, StuckTally tally)
    {
        foreach (Body b in bodies)
        {
            streaks.TryGetValue(b, out int streak);
            string why = null;
            if (_w.Physics.Stuck.IsStuck(b, out string inside))
            {
                tally.SeenInside++;
                if (++streak >= 2) why = $"inside {inside} for {streak} ticks";
            }
            else streak = 0;
            if (b.WorldPosition.Y < ChunkStore.WorldCells.Min.Y) why = "fell out of the world";
            streaks[b] = streak;
            if (why == null) continue;
            tally.Failures++;
            tally.First ??= $"{when}: {b} {why} at {b.WorldPosition}";
        }
    }

    void Teleport(Body b, N.Vector3 to, N.Vector3 velocity = default, bool notify = true)
    {
        if (b.Kind == BodyKind.Player) _w.Player.PlaceOutside(to, velocity);
        else
        {
            b.Position = to + N.Vector3.UnitY * b.Shape.FeetBelowOrigin;
            b.Velocity = velocity;
            b.AngularVelocity = N.Vector3.Zero;
            b.Asleep = false;
            _w.Host.BindingOf(b)?.Teleport();
        }
        if (notify) _w.Physics.NotifyChanged(Region.Around(to, 2f));
    }

    /// <summary>Puts a body in an open cell against the ship's hull, then builds a block into that cell.</summary>
    bool PlaceBlockOn(Ship ship, Body body, Random rng)
    {
        Structure s = ship.Structure;
        for (int tries = 0; tries < 40; tries++)
        {
            // An open cell just outside a side wall (x ±4), anywhere along the cabin.
            int side = rng.Next(2) == 0 ? -1 : 1;
            var cell = new SInt3(side * 5, rng.Next(1, 5), rng.Next(-9, 7));
            if (s.Has(cell) || !s.Has(new SInt3(side * 4, cell.Y, cell.Z))) continue;
            Block block = Building.Make(s, s.Catalogue.Type("steel"));
            if (Building.Check(s, cell, block) != PlaceError.None) continue;
            N.Vector3 centre = ship.Body.ToParent(StructureBridge.CellCenter(s, cell));
            Teleport(body, centre - N.Vector3.UnitY * 0.5f + N.Vector3.UnitY * (body.Kind == BodyKind.Crate ? 0.1f : 0f), notify: false);
            Building.Place(s, cell, block);
            ship.ApplyEdits();
            return true;
        }
        return false;
    }

    // ==== Performance ==========================================================================

    IEnumerable<int> Performance()
    {
        _w.Weather.Calm = false;
        _w.Weather.Sim.Snap(WeatherKind.DustStorm);
        var fleet = new List<Ship>(_w.Ships.Where(s => !s.IsWreck && s.Flight != null));
        for (int i = fleet.Count; i < 20; i++)
        {
            bool rover = i % 3 == 0;
            float x = -70f + (i % 6) * 26f, z = -60f + (i / 6) * 28f;
            fleet.Add(rover
                ? _w.SpawnVessel("sandflea.gbp", _w.Ground(x, z) + N.Vector3.UnitY * 2.3f, i * 0.7f, landed: false)
                : _w.SpawnVessel("corsair.gbp", new N.Vector3(x, 60f + i * 3f, z), i * 0.7f, landed: false));
        }
        for (int i = _w.Crates.Count; i < 50; i++)
        {
            float x = -60f + (i % 10) * 12f, z = -50f + (i / 10) * 22f;
            Crate.Spawn(_w, new N.Vector3(x, _w.GroundTop(x, z) + 0.5f + (i % 4) * 3f, z), i);
        }
        int vessels = _w.Ships.Count(s => !s.IsWreck && s.Flight != null), crates = _w.Crates.Count;
        for (int i = 0; i < 40; i++)
        {
            Drive(fleet, i);
            yield return 0;
        }

        // With --fixed-fps (test-all passes it) frames run back to back, so a frame's wall time is
        // one whole physics tick: the shared module, the Jolt step, every script, plus a headless
        // frame that draws nothing. Without it frames wait for real time, and only the module's own
        // step can be timed.
        //
        // Three separate windows, judged on the best one: a busy machine (another test run, a
        // build) slows some windows down, but a real slowdown slows every one of them. The budget
        // is unchanged. Each window also reports the main thread's own CPU time per tick, which a
        // busy machine doesn't inflate (the Jolt step's worker threads aren't in it).
        const int windows = 3, ticks = 100, gap = 10;
        var results = new List<PerfWindow>();
        for (int window = 0; window < windows; window++)
        {
            for (int i = 0; i < gap; i++)
            {
                Drive(fleet, i);
                yield return 0;
            }
            var frameTimes = new List<double>();
            double steps = 0, stepWorst = 0;
            double cpuFrom = ThreadCpuMs();
            long last = Stopwatch.GetTimestamp();
            for (int i = 0; i < ticks; i++)
            {
                Drive(fleet, i);
                yield return 0;
                long now = Stopwatch.GetTimestamp();
                frameTimes.Add(Stopwatch.GetElapsedTime(last, now).TotalMilliseconds);
                last = now;
                steps += _w.Host.LastStepMs;
                stepWorst = Math.Max(stepWorst, _w.Host.LastStepMs);
            }
            double cpu = (ThreadCpuMs() - cpuFrom) / ticks;
            frameTimes.Sort();
            results.Add(new PerfWindow(frameTimes.Average(), frameTimes[ticks / 2], frameTimes[^1], steps / ticks, stepWorst, cpu));
        }
        int moving = fleet.Count(s => s.Body.Velocity.Length() > 1f);
        bool fixedFps = results.Min(r => r.Median) < 12.0; // real-time frames are paced at 16.7 ms; back-to-back ones are far quicker
        PerfWindow best = fixedFps ? results.MinBy(r => r.Tick) : results.MinBy(r => r.Step);
        string all = string.Join(", ", results.Select(r => $"{r.Tick:F2}"));
        string cpus = string.Join(", ", results.Select(r => double.IsNaN(r.Cpu) ? "n/a" : $"{r.Cpu:F2}"));
        string what = $"performance: {vessels} vessels ({moving} moving) + {crates} crates in a dust storm";
        if (fixedFps)
            Check(vessels >= 20 && crates >= 50 && moving >= 18 && best.Tick < 2.0,
                $"{what}: a whole physics tick (shared module, Jolt step, scripts and an empty headless frame) takes {best.Tick:F2} ms on average " +
                $"in the best of three {ticks}-tick windows (budget 2 ms; windows {all} ms; best window's median {best.Median:F2}, worst {best.Worst:F2}); " +
                $"the shared module's own step {best.Step:F3} ms, worst {best.StepWorst:F3}; main-thread CPU per tick {cpus} ms (Windows counts thread time in 15.6 ms steps: ±{15.6 / ticks:F2})");
        else
            Check(vessels >= 20 && crates >= 50 && moving >= 18 && best.Step < 2.0,
                $"{what}: the shared module's step takes {best.Step:F3} ms on average in the best of three windows " +
                $"({string.Join(", ", results.Select(r => $"{r.Step:F3}"))} ms), worst {best.StepWorst:F3} ms " +
                "(run with --fixed-fps 60, as test-all does, to time the whole tick including the Jolt step)");
    }

    /// <summary>One timing window: wall-clock tick (average, median, worst), the module's step (average, worst) and main-thread CPU per tick, all ms.</summary>
    readonly record struct PerfWindow(double Tick, double Median, double Worst, double Step, double StepWorst, double Cpu);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern uint GetCurrentThreadId();

    /// <summary>The CPU time this thread has used so far, ms (Windows only; NaN elsewhere).</summary>
    static double ThreadCpuMs()
    {
        if (!OperatingSystem.IsWindows()) return double.NaN;
        uint id = GetCurrentThreadId();
        foreach (ProcessThread t in Process.GetCurrentProcess().Threads)
            if (t.Id == id) return t.TotalProcessorTime.TotalMilliseconds;
        return double.NaN;
    }

    static void Drive(List<Ship> fleet, int t)
    {
        for (int i = 0; i < fleet.Count; i++)
        {
            Ship s = fleet[i];
            if (s.Flight == null || s.IsWreck) continue;
            s.Flight.Input = PilotInput.Hands(new Vector3(0f, 0f, -0.3f), new Vector3(0f, i % 2 == 0 ? 0.6f : -0.6f, 0f)); // tight circles, so nobody leaves the map
        }
    }

    // ==== Reporting ============================================================================

    void Check(bool ok, string what)
    {
        if (ok)
        {
            _passes++;
            GD.Print($"PASS  {what}");
        }
        else Fail(what);
    }

    void Fail(string what)
    {
        _failures++;
        GD.PrintErr($"FAIL  {what}");
    }
}
