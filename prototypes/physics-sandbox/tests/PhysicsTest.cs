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
        foreach (int x in ForcePath()) yield return x;
        foreach (int x in DamageAndCraters()) yield return x;
        foreach (int x in Severing()) yield return x;
        foreach (int x in HullRider()) yield return x;
        foreach (int x in CabinRider()) yield return x;
        foreach (int x in RoverTrench()) yield return x;
        foreach (int x in GearOnDugGround()) yield return x;
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

        // Integrity is per tonne; the player's makes a 30 m fall cost about half their health.
        float player = Impacts.Integrity(BodyKind.Player, 100f, rules.Integrity);
        float fall30 = Impacts.Damage(100f, MathF.Sqrt(2f * 9.8f * 30f), rules.Contacts);
        Check(MathF.Abs(hull - 8.797f * 4f) < 1e-4f && fall30 / player > 0.4f && fall30 / player < 0.6f,
            $"toughness is per tonne: the Corsair has {hull:F1} hull points; a 30 m fall costs a player {fall30 / player:P0} of their health");

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
        N.Vector3 p0 = rider.WorldPosition, v0 = rider.WorldVelocity;
        Riding.EnterCabin(rider, ship);
        N.Vector3 p1 = rider.WorldPosition, v1 = rider.WorldVelocity;
        Riding.LeaveCabin(rider);
        Riding.StandOn(rider, ship);
        N.Vector3 v2 = rider.WorldVelocity;
        Riding.StepOff(rider);
        Check(N.Vector3.Distance(p0, p1) < 1e-4f && N.Vector3.Distance(v0, v1) < 1e-4f && N.Vector3.Distance(v0, v2) < 1e-4f
              && N.Vector3.Distance(v0, rider.Velocity) < 1e-4f,
            "boarding a cabin, standing on a hull and stepping off all keep world position and velocity (spin included)");
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
        float floor = _w.GroundTop(spot.X, spot.Z);
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

        // A fall: the player is hurt by the same formula, with no floor at all.
        Player p = _w.Player;
        p.Health.Set(p.Health.Max);
        p.PlaceOutside(_w.Ground(-40f, -25f) + N.Vector3.UnitY * 30f);
        float healthBefore = p.Health.Current;
        _impacts.Clear();
        for (int i = 0; i < 200 && !_impacts.Any(x => x.Body == p.Body); i++) yield return 0;
        hit = _impacts.FirstOrDefault(x => x.Body == p.Body);
        float lost = healthBefore - p.Health.Current;
        Check(hit != null && MathF.Abs(lost - Impacts.Damage(100f, hit.Speed, _w.Rules.Contacts)) < 1e-6f,
            $"a 30 m fall lands the player at {hit?.Speed:F1} m/s and costs {lost / p.Health.Max:P0} of their health: the same formula, no floor");
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
            float bm = Units.Kilograms(ship.Structure.Catalogue.StatsFor(block, ship.Structure.Grid).Mass);
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

    // ==== Riding =================================================================================

    IEnumerable<int> HullRider()
    {
        Ship ship = _w.SpawnVessel("corsair.gbp", new N.Vector3(0f, 70f, -30f), 0f, landed: false);
        ship.Flight.Input = PilotInput.LetGo;
        Player p = _w.Player;
        N.Vector3 standLocal = new(-1.0f, 2.78f, -1.5f); // on the roof, clear of the down jets and the fin
        p.PlaceOutside(ship.Body.ToParent(standLocal));
        for (int i = 0; i < 30; i++) yield return 0;
        bool standing = p.Body.Platform == ship.Body && p.Body.OnFloor;
        N.Vector3 startLocal = ship.ToLocal(p.Body.Position);
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
            worst = MathF.Max(worst, N.Vector3.Distance(ship.ToLocal(p.Body.Position), startLocal));
            maxSpeed = MathF.Max(maxSpeed, ship.Body.Velocity.Length());
            turned = MathF.Max(turned, MathF.Abs(MathX.WrapAngle(MathX.Heading(ship.Body.Rotation) - startHeading)));
            stayed &= p.Body.Platform == ship.Body;
        }
        Check(standing && stayed && worst < 0.15f && maxSpeed > 40f && turned > 1f,
            $"a player standing on the outer hull stays put through a 10 s manoeuvre (boost to {maxSpeed:F0} m/s, a {Units.Degrees(turned):F0}° turn, a climb, a stop): " +
            $"carried by the hull, never more than {worst * 100f:F1} cm from where they stood");
        float viewTurn = MathX.WrapAngle(p.Yaw - 0f);
        Check(MathF.Abs(MathX.WrapAngle(viewTurn - MathX.WrapAngle(MathX.Heading(ship.Body.Rotation) - startHeading))) < 0.05f,
            "and their view turned with the ship");

        // Jumping off a moving hull keeps the hull's velocity. Back over the map first (the rider
        // is carried along with the ship), so the jump lands on ground rather than off the edge.
        ship.Vessel.Place(ship.Body, new N.Vector3(-70f, 75f, 40f), -MathF.PI / 2f);
        ship.Binding.Teleport();
        ship.Flight.Input = PilotInput.Hands(Vector3.Forward);
        for (int i = 0; i < 90; i++) yield return 0;
        N.Vector3 hullVelocity = ship.Body.PointVelocity(p.Body.Position);
        p.Legs.Jump = true;
        yield return 0;
        p.Legs.Jump = false;
        for (int i = 0; i < 3; i++) yield return 0;
        N.Vector3 flat = MathX.Flat(p.Body.WorldVelocity - hullVelocity);
        Check(p.Body.Platform == null && flat.Length() < 2f && hullVelocity.Length() > 20f,
            $"jumping off at {hullVelocity.Length():F0} m/s, the player keeps the hull's velocity (off by {flat.Length():F2} m/s sideways)");
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
            aboard &= p.Aboard == ship;
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

        // Core gravity is built in and toggleable, and costs no power.
        ship.Body.Core.On = false;
        for (int i = 0; i < 30; i++) yield return 0;
        bool floating = p.Legs.Floating && _w.Physics.GravityAt(p.Body) == N.Vector3.Zero;
        ship.Body.Core.On = true;
        for (int i = 0; i < 60; i++) yield return 0;
        Check(floating && p.Body.OnFloor && _w.Physics.GravityAt(p.Body).Y < -9f,
            "core gravity toggles off (the player floats in zero-g) and on again (back on the floor)");
        p.PlaceOutside(_w.Ground(1.5f, 8.5f) + N.Vector3.UnitY * 0.05f);
        _w.Remove(ship);
        for (int i = 0; i < 5; i++) yield return 0;
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

    IEnumerable<int> GearOnDugGround()
    {
        var site = new N.Vector2(-42f, -8f);
        // Dig a pit where the nose leg will come down and a scrape under the port rear leg, on
        // sloping ground, so no two feet agree.
        _w.Terrain.Dig(TInt3.Floor(_w.Ground(site.X, site.Y - 4f) - N.Vector3.UnitY * 0.2f), 1.2f);
        _w.Terrain.Dig(TInt3.Floor(_w.Ground(site.X - 1.5f, site.Y + 2.5f)), 0.6f);
        Ship ship = _w.SpawnVessel("corsair.gbp", _w.Ground(site.X, site.Y) + N.Vector3.UnitY * 9f, 0f, landed: false);
        int landed = _landed;
        for (int t = 0; t < 600 && _landed == landed; t++)
        {
            bool down = !ship.Vessel.Gear.Contact; // ease down at 3.5 m/s until the feet touch, then let go
            ship.Flight.Input = down ? PilotInput.Hands(new Vector3(0f, -0.05f, 0f)) : PilotInput.LetGo;
            yield return 0;
        }
        LandingGear gear = ship.Vessel.Gear;
        gear.Probe(_w.Physics, ship.Body);
        float worstFoot = 0f;
        float spread = 0f, minGround = float.MaxValue, maxGround = float.MinValue;
        for (int i = 0; i < gear.Count; i++)
        {
            if (!gear.FootFound[i]) continue;
            float foot = ship.Body.ToParent(gear.Leg(i).Hip).Y - gear.Leg(i).Length - gear.FootGround[i].Y;
            worstFoot = MathF.Max(worstFoot, MathF.Abs(foot));
            minGround = MathF.Min(minGround, gear.FootGround[i].Y);
            maxGround = MathF.Max(maxGround, gear.FootGround[i].Y);
        }
        spread = maxGround - minGround;
        float tilt = Units.Degrees(MathX.Angle(MathX.Up(ship.Body.Rotation), N.Vector3.UnitY));
        Check(_landed > landed && ship.Vessel.Frozen && gear.Deployed && tilt > 2f && tilt <= _w.Rules.Gear.MaxSettleTilt + 0.5f
              && worstFoot < 0.25f && spread > 0.3f && !_w.Physics.Stuck.IsStuck(ship.Body, out _),
            $"landing gear on dug ground: the legs drop by themselves, it settles tilted {tilt:F1}° to ground {spread:F1} m uneven under its feet, " +
            $"every foot within {worstFoot * 100f:F0} cm of the ground, raises Landed and stops simulating");
        _w.Remove(ship);
        yield return 0;
    }

    // ==== Tornado ===============================================================================

    IEnumerable<int> TornadoFling()
    {
        Player p = _w.Player;
        var spot = _w.Ground(-50f, 12f);
        p.PlaceOutside(spot + N.Vector3.UnitY * 0.05f);
        p.Health.Set(p.Health.Max * 0.4f);
        p.Health.RegenDelay = 1e6f; // no healing mid-test: the numbers stay readable
        Crate crate = Crate.Spawn(_w, spot + new N.Vector3(3f, 0.45f, 0f), 2);
        _w.Weather.Calm = false;
        _w.Weather.Sim.Snap(WeatherKind.Clouds);
        for (int i = 0; i < 20; i++) yield return 0;
        // It touches down beside the player heading east, across the middle of the map, so the
        // carry and the fling stay inside the 256 m world.
        Tornado tornado = _w.Weather.Sim.SpawnTornado(new Vector3(spot.X - 5f, spot.Y, spot.Z + 1f), new Vector3(spot.X + 60f, spot.Y, spot.Z + 1f));
        tornado.SkipFormation();
        _impacts.Clear();

        bool caught = false, released = false, landed = false;
        float floorSeen = 0f, peak = spot.Y, cratePeak = spot.Y;
        for (int t = 0; t < 1500 && !landed; t++)
        {
            yield return 0;
            VortexPhase phase = _w.Weather.PhaseOf(p.Body);
            caught |= phase == VortexPhase.Caught;
            if (caught && phase == VortexPhase.Released && !released)
            {
                released = true;
                _w.Weather.Sim.ClearTornadoes(); // gone once it has thrown you: no second catch
            }
            floorSeen = MathF.Max(floorSeen, _w.Weather.FlingFloor(p.Body));
            peak = MathF.Max(peak, p.Body.Position.Y);
            cratePeak = MathF.Max(cratePeak, crate.Body.Position.Y);
            landed = released && p.Body.OnFloor && p.Body.WorldVelocity.Length() < 1f;
        }
        ImpactInfo landing = _impacts.Where(x => x.Body == p.Body && x.Point.Y < peak).OrderByDescending(x => x.Speed).FirstOrDefault();
        float raw = landing == null ? 0f : Impacts.Damage(100f, landing.Speed, _w.Rules.Contacts);
        float share = p.Health.Current / p.Health.Max;
        Check(caught && released && landed && !p.Health.Dead && MathF.Abs(share - 0.15f) < 1e-5f && raw > 0.25f * p.Health.Max && floorSeen == 0.15f,
            $"a tornado catches the player at 40% health, carries them {peak - spot.Y:F0} m up and flings them; the landing at {landing?.Speed:F0} m/s would cost " +
            $"{raw / p.Health.Max:P0} of their health, and they survive at exactly {share:P2}: the fling floor, which belongs to the weather code");
        Check(cratePeak - spot.Y > 8f && _w.Crates.Contains(crate) && crate.Body.Damage >= 0f,
            $"the crate beside them is flung {cratePeak - spot.Y:F0} m up too, and is still there when it lands ({crate.Body.Damage / crate.Body.Integrity:P0} damage)");

        // The same fall without a fling has no floor at all.
        p.Health.Set(p.Health.Max * 0.4f);
        p.PlaceOutside(spot + N.Vector3.UnitY * (peak - spot.Y));
        for (int t = 0; t < 400 && !(p.Body.OnFloor && p.Body.Velocity.Length() < 1f); t++) yield return 0;
        Check(p.Health.Current / p.Health.Max < 0.15f,
            $"the same fall without a fling leaves them at {p.Health.Current / p.Health.Max:P0}: the 15% floor is the tornado's alone");

        _w.Weather.Calm = true;
        _w.Weather.Sim.ClearTornadoes();
        _w.Remove(crate);
        p.Health.RegenDelay = 8f;
        p.Health.Set(p.Health.Max);
        p.PlaceOutside(_w.Ground(1.5f, 8.5f) + N.Vector3.UnitY * 0.05f);
        for (int i = 0; i < 5; i++) yield return 0;
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
        double total = 0, worst = 0, frames = 0, frameWorst = 0;
        var frameTimes = new List<double>();
        const int ticks = 300;
        long last = Stopwatch.GetTimestamp();
        for (int i = 0; i < ticks; i++)
        {
            Drive(fleet, i);
            yield return 0;
            long now = Stopwatch.GetTimestamp();
            double frame = Stopwatch.GetElapsedTime(last, now).TotalMilliseconds;
            last = now;
            frameTimes.Add(frame);
            frames += frame;
            frameWorst = Math.Max(frameWorst, frame);
            total += _w.Host.LastStepMs;
            worst = Math.Max(worst, _w.Host.LastStepMs);
        }
        frameTimes.Sort();
        int moving = fleet.Count(s => s.Body.Velocity.Length() > 1f);
        double tick = frames / ticks, median = frameTimes[ticks / 2], step = total / ticks;
        bool fixedFps = median < 12.0; // real-time frames are paced at 16.7 ms; back-to-back ones are far quicker
        string what = $"performance: {vessels} vessels ({moving} moving) + {crates} crates in a dust storm";
        if (fixedFps)
            Check(vessels >= 20 && crates >= 50 && moving >= 18 && tick < 2.0,
                $"{what}: a whole physics tick (shared module, Jolt step, scripts and an empty headless frame) takes {tick:F2} ms on average " +
                $"(median {median:F2}, worst {frameWorst:F2}); the shared module's own step {step:F3} ms, worst {worst:F3} ms");
        else
            Check(vessels >= 20 && crates >= 50 && moving >= 18 && step < 2.0,
                $"{what}: the shared module's step takes {step:F3} ms on average, worst {worst:F3} ms " +
                "(run with --fixed-fps 60, as test-all does, to time the whole tick including the Jolt step)");
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
