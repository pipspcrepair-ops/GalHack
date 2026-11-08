using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using ShipHandling.Handling;
using PhysicsSandbox.Bridges;
using Slice.Game;
using Slice.Surface;
using Slice.Vessels;
using Weather.Forces;
using Weather.Model;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;

namespace Slice.Tests;

/// <summary>
/// Wind and tornadoes on bodies, in the slice's real world. First a table of every kind of body,
/// from the lightest creature to a capital ship, against the one set of rules: the wind it starts
/// to slide at, the tornado that lifts it, and how far each shape throws it. Then for real, on the
/// ground: a hurricane slides a standing survivor but not a crouched one; a tornado tosses a landed
/// light craft, which lands with hull damage where it strikes, and leaves a hover tank where it is;
/// a fling's landing hurts in full, and firing the jetpack on the way down lands you slower; the
/// jetpack can't fight out of a strong core; creatures are thrown by their mass.
///
/// Run: --headless --fixed-fps 60 --quit-after 72000 --path prototypes/slice res://tests/WindTest.tscn
/// </summary>
public partial class WindTest : Node
{
    Game.Game _game;
    Driver _drive;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly List<ImpactInfo> _impacts = new();

    SurfaceZone Zone => _game.Zone;
    WeatherSim Sim => Zone.Weather.Sim;
    WeatherBridge Bridge => Zone.Weather.Bridge;
    Slice.Player.Survivor Me => _game.Survivor;

    public override void _Ready()
    {
        ProcessPhysicsPriority = 100;
        _run = Scenarios().GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        bool more;
        try { more = _run.MoveNext(); }
        catch (Exception e)
        {
            Check(false, $"scenario threw: {e}");
            more = false;
        }
        if (more) return;
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {_clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        SetPhysicsProcess(false);
        Wildlife.Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    void Check(bool ok, string what)
    {
        GD.Print($"{(ok ? "PASS" : "FAIL")}  {what}");
        if (ok) _passes++;
        else _failures++;
    }

    IEnumerable<int> Scenarios()
    {
        _game = new Game.Game { Interactive = false, SkipIntro = true, World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Tested) };
        AddChild(_game);
        _drive = new Driver(_game);
        foreach (int x in _drive.UntilPlaying()) yield return x;
        Bridge.Calm = true;
        Sim.FrontsOff = true;
        Sim.Snap(WeatherKind.Clear);
        Zone.Wildlife.Frozen = true;
        Zone.Physics.Impact += _impacts.Add;
        foreach (int x in _drive.ClimbOut(_game.Bunker)) yield return x;
        foreach (int x in _drive.Wait(30)) yield return x;
        _home = Me.WorldFeet;

        MassTable();
        foreach (int x in Footing()) yield return x;
        foreach (int x in TossedCraft()) yield return x;
        foreach (int x in FlingAndJetpack()) yield return x;
        foreach (int x in StrongCore()) yield return x;
        foreach (int x in Creatures()) yield return x;
        foreach (int x in TumblingCraft()) yield return x;
        foreach (int x in Flyer()) yield return x;
        foreach (int x in Trees()) yield return x;
    }

    Vector3 _home;

    /// <summary>Open ground this far east and north of where the survivor came out, at its height.</summary>
    Vector3 Spot(float east, float north)
    {
        Vector3 p = _home + new Vector3(east, 0f, -north);
        return Zone.GroundAt(p.X, p.Z);
    }

    /// <summary>Open flat ground near a spot (no woods to break the wind, nothing steep), or the spot itself.</summary>
    Vector3 OpenSpot(float east, float north)
    {
        Vector3 best = Spot(east, north);
        float leastWood = float.MaxValue;
        for (int k = 0; k < 2000; k++)
        {
            float a = k * 2.399f, r = MathF.Sqrt(k) * 15f;
            Vector3 p = Spot(east + MathF.Cos(a) * r, north + MathF.Sin(a) * r);
            Ground g = Sim.GroundAt(p);
            if (g.Water || g.Slope > 0.08f || Regions.Barred(Sim.Data.Regions, g) || _game.Sheltered(p) || _game.Zone.Water.LiquidAt(p.X, p.Z, out _)) continue;
            if (g.Forest < 0.05f && Regions.High(Sim.Data.Regions, g) < 0.05f) return p;
            if (g.Forest < leastWood) (leastWood, best) = (g.Forest, p); // the least wooded there is, on a world of woods
        }
        return best;
    }

    void Calm()
    {
        Bridge.Calm = true;
        Sim.ClearTornadoes();
        Sim.ClearCells();
        Sim.Snap(WeatherKind.Clear);
        _game.Controls.ReleaseAll();
    }

    void FullHealth() => _game.Suit.Vitals.Health = _game.Suit.Sim.Data.Vitals.Health.Max;

    /// <summary>A tornado of one shape right here, touched down, barely moving, heading off east.</summary>
    Tornado TornadoAt(Vector3 at, TornadoShape shape, float strengthLife = 240f)
    {
        TornadoShapeRules s = Sim.Data.Tornado.Shapes.First(x => x.Shape == shape);
        Tornado t = Sim.SpawnTornado(at, at + Vector3.Right * 500f, Sim.Data.Tornado.Shaped(s, 0.4f, strengthLife));
        t.Homing = false;
        t.Kept = true;
        t.SkipFormation();
        return t;
    }

    // ---- The table --------------------------------------------------------------------

    /// <summary>One kind of body, as the rules see it.</summary>
    record struct Kind(string Name, float Mass, float Area, string Holds);

    /// <summary>
    /// Every kind of body against the same rules, with the starters' real masses and drag areas:
    /// the wind it starts to slide at (feet: past the footing; a landed craft: past the push that
    /// knocks it loose; a hover bike floats, so any wind drifts it), whether each shape of tornado
    /// lifts it, and how far a cone and a wedge throw it (the force module's own run, on flat ground).
    /// </summary>
    void MassTable()
    {
        var kinds = new List<Kind>();
        var species = Zone.Wildlife.Fauna?.Species ?? new List<Wildlife.Species>();
        var walkers = species.Where(s => !s.Flies).OrderBy(Wildlife.Heft.Mass).ToList();
        if (walkers.Count > 0)
        {
            foreach (Wildlife.Species s in new[] { walkers[0], walkers[walkers.Count / 2], walkers[^1] }.Distinct())
                kinds.Add(new Kind($"creature: {s.Name}", Wildlife.Heft.Mass(s), CreatureArea(s), "feet"));
        }
        kinds.Add(new Kind("survivor, standing", Slice.Player.Survivor.Mass, Slice.Player.Survivor.StandDragArea, "feet"));
        kinds.Add(new Kind("survivor, crouched", Slice.Player.Survivor.Mass, Slice.Player.Survivor.CrouchDragArea, "feet"));
        kinds.Add(new Kind("hover bike, parked", Slice.Vehicles.HoverBike.Weight, Slice.Vehicles.HoverBike.DragArea, "skids"));
        kinds.Add(new Kind("hover bike and rider", Slice.Vehicles.HoverBike.Weight + Slice.Player.Survivor.Mass, Slice.Vehicles.HoverBike.DragArea + 0.9f, "ridden"));
        foreach (string file in Zone.Data.Rules.Build.Library)
        {
            Structure s = Zone.Data.Blueprint(file);
            if (s.Mode == CoreMode.Base) continue;
            StructureStats st = StructureStats.Compute(s);
            kinds.Add(new Kind($"{s.Name} ({(st.Mass >= 100 ? "Large" : "Small")})", (float)st.Mass * 1000f, StructureBridge.DragArea(s), "landed"));
        }
        kinds = kinds.OrderBy(k => k.Mass).ToList();

        float g = Zone.Physics.Zone.Strength, rho = ForceModule.AirDensity;
        var walk = new WalkController();
        string Slide(Kind k) => k.Holds switch
        {
            "feet" => $"{MathF.Sqrt(walk.Footing * k.Mass / (0.5f * rho * k.Area)):F0} m/s",
            "landed" => $"{MathF.Sqrt(VesselController.WeatherWakeShare * k.Mass * g / (0.5f * rho * k.Area)):F0} m/s",
            "skids" => $"{MathF.Sqrt(Slice.Vehicles.HoverBike.ParkedHold * k.Mass / (0.5f * rho * k.Area)):F0} m/s",
            _ => "rider holds",
        };
        GD.Print($"      {"body",-34} {"mass",10} {"drag mÂ²",8} {"slides at",10}  lifted by          thrown by a cone / a wedge");
        float lastCone = float.MaxValue, lastWedge = float.MaxValue;
        bool ordered = true, bigStays = true, lightFly = true;
        foreach (Kind k in kinds)
        {
            var lifts = Sim.Data.Tornado.Shapes.Where(s => Lifts(s, k.Mass)).Select(s => s.Shape.ToString().ToLowerInvariant()).ToList();
            var (coneUp, coneOff) = Throw(TornadoShape.Cone, k);
            var (wedgeUp, wedgeOff) = Throw(TornadoShape.Wedge, k);
            GD.Print($"      {k.Name,-34} {Mass(k.Mass),10} {k.Area,8:F1} {Slide(k),10}  {(lifts.Count == 0 ? "nothing" : string.Join(", ", lifts)),-17} " +
                $"{coneUp:F0} m up, {coneOff:F0} m off / {wedgeUp:F0} m up, {wedgeOff:F0} m off");
            // Under a shape's mass limit a body takes its full pull, and only its own drag tells them apart; past it, the heavier the less.
            if (k.Mass > Limit(TornadoShape.Cone)) { ordered &= coneOff <= lastCone * 1.15f + 2f; lastCone = coneOff; }
            if (k.Mass > Limit(TornadoShape.Wedge)) { ordered &= wedgeOff <= lastWedge * 1.15f + 2f; lastWedge = wedgeOff; }
            if (k.Mass >= 100000f) bigStays &= lifts.Count == 0 && wedgeUp < 0.5f && wedgeOff < 3f;
            if (k.Mass <= 400f) lightFly &= lifts.Count == Sim.Data.Tornado.Shapes.Count && coneUp > 20f;
        }
        Check(ordered, "past the lightest bodies (which all take a tornado's full pull) the throw falls with mass, creatures and craft alike, Small to Large");
        Check(lightFly, "every shape of tornado lifts the light bodies (creatures, people, the bike) and throws them far");
        Check(bigStays, "a hover tank or a capital ship stays put: no tornado lifts one, the strongest drags it a few metres at most");
    }

    float Limit(TornadoShape shape) => Sim.Data.Tornado.Shaped(Sim.Data.Tornado.Shapes.First(s => s.Shape == shape), 1f, 100f).LiftMassLimit;

    static string Mass(float kg) => kg >= 1000f ? $"{kg / 1000f:F1} t" : $"{kg:F0} kg";

    /// <summary>A creature's side-on drag area, as the wind takes it (SurfaceWeather's own rule).</summary>
    static float CreatureArea(Wildlife.Species s) =>
        SurfaceWeather.CreaturePush(s, 1f) * Wildlife.Heft.Mass(s) / (0.5f * ForceModule.AirDensity);

    /// <summary>Whether a tornado of this shape at full strength can carry a body of this mass up: its share of the updraft beats its weight.</summary>
    bool Lifts(TornadoShapeRules s, float mass)
    {
        TornadoRules r = Sim.Data.Tornado.Shaped(s, 1f, 100f);
        float share = ForceModule.MassShare(mass, r.LiftMassLimit);
        return share * (r.Response * r.LiftSpeed + ForceModule.Gravity) > ForceModule.Gravity * 1.05f;
    }

    /// <summary>The force module's own run of a body at the base of a tornado of one shape, on flat ground: how high, and how far off it ends up.</summary>
    (float up, float off) Throw(TornadoShape shape, Kind k)
    {
        var sim = new WeatherSim(Sim.Data, "temperate", (ulong)Random.Shared.NextInt64(), WeatherKind.Clear);
        var forces = new ForceModule();
        var body = new Point(k.Mass, k.Area) { Position = new Vector3(3f, 0f, 0f) };
        forces.Register(body);
        TornadoShapeRules s = Sim.Data.Tornado.Shapes.First(x => x.Shape == shape);
        Tornado t = sim.SpawnTornado(Vector3.Zero, Vector3.Right * 100f, Sim.Data.Tornado.Shaped(s, 0.01f, 300f));
        t.SkipFormation();
        float up = 0f;
        for (int i = 0; i < 60 * 25; i++)
        {
            sim.Step(1f / 60f, body.Position);
            Weather.Glue.Sync(sim, forces);
            forces.Wind = _ => Vector3.Zero;
            forces.Step(1f / 60f);
            body.Integrate(1f / 60f);
            up = MathF.Max(up, body.Position.Y);
        }
        return (up, new Vector2(body.Position.X - 3f, body.Position.Z).Length());
    }

    /// <summary>A point body on flat ground with friction, for the table.</summary>
    sealed class Point : IForceBody
    {
        public Point(float mass, float area) { Mass = mass; DragArea = area; }
        public float Mass { get; }
        public float DragArea { get; }
        public Vector3 Position { get; set; }
        public Vector3 Velocity { get; set; }
        Vector3 _force;
        public void ApplyForce(Vector3 force) => _force += force;
        public void ApplyImpulse(Vector3 impulse) => Velocity += impulse / Mass;

        public void Integrate(float dt)
        {
            Velocity += (_force / Mass + Vector3.Down * ForceModule.Gravity) * dt;
            _force = Vector3.Zero;
            Position += Velocity * dt;
            if (Position.Y >= 0f) return;
            Position = new Vector3(Position.X, 0f, Position.Z);
            Vector3 flat = new(Velocity.X, 0f, Velocity.Z);
            float slow = ForceModule.Gravity * 0.6f * dt; // sliding to a stop on the ground
            Velocity = flat.Length() <= slow ? Vector3.Zero : flat - flat.Normalized() * slow;
        }
    }

    // ---- Wind on feet -------------------------------------------------------------------

    /// <summary>A hurricane's gusts slide a survivor standing tall in the open; crouched to brace, they hold.</summary>
    IEnumerable<int> Footing()
    {
        Calm();
        Vector3 at = OpenSpot(0f, 60f);
        var drift = new float[2];
        var slips = new int[2];
        var gust = new float[2];
        for (int run = 0; run < 2; run++)
        {
            bool crouch = run == 1;
            Me.PlaceOutside((at + Vector3.Up * 0.1f).ToNumerics());
            foreach (int x in _drive.UntilSettled()) yield return x;
            Sim.Snap(WeatherKind.Hurricane);
            Bridge.Calm = false;
            _game.Controls.Hold(Key.Ctrl, crouch);
            Vector3 start = Me.WorldFeet;
            for (int i = 0; i < 60 * 12; i++)
            {
                yield return 0;
                if (Me.Legs.Slipping) slips[run]++;
                gust[run] = MathF.Max(gust[run], Sim.WindAt(Me.WorldFeet).Length());
            }
            drift[run] = new Vector2(Me.WorldFeet.X - start.X, Me.WorldFeet.Z - start.Z).Length();
            Calm();
            foreach (int x in _drive.Wait(20)) yield return x;
        }
        float slip = MathF.Sqrt(new WalkController().Footing * Slice.Player.Survivor.Mass / (0.5f * ForceModule.AirDensity * Slice.Player.Survivor.StandDragArea));
        Check(gust[0] < slip ? slips[0] == 0 : slips[0] > 0 && drift[0] > drift[1],
            $"a hurricane here ({Sim.Data.State(WeatherKind.Hurricane).WindSpeed:F0} m/s, gusting to {gust[0]:F0}; standing feet slip from {slip:F0}) {(slips[0] > 0 ? "slides" : "holds")} a survivor standing tall: {drift[0]:F1} m in 12 s, slipping {slips[0] / 60f:F1} s of it");
        Check(drift[1] <= drift[0] * 0.4f + 0.2f && slips[1] <= slips[0], $"crouched to brace, they hold far better: {drift[1]:F1} m, slipping {slips[1] / 60f:F1} s");
    }

    // ---- Craft --------------------------------------------------------------------------

    /// <summary>
    /// A tornado over a landed light craft: it is caught, its engines can't hold it, it is carried up
    /// and thrown, and it lands by the one impact rule, its blocks worn where it struck. The same
    /// strongest tornado over a landed hover tank leaves it where it stands.
    /// </summary>
    IEnumerable<int> TossedCraft()
    {
        Calm();
        Structure s = Zone.Data.Blueprint(Zone.Data.Rules.Build.StarterBlueprint);
        s.Name = "Tossed";
        Vector3 at = Spot(-120f, 90f);
        Vessel v = Vessel.Spawn(Zone, s, (at + Vector3.Up * 3f).ToNumerics(), 0f, landed: true);
        foreach (int x in _drive.Wait(90)) yield return x;
        Me.PlaceOutside((Spot(-120f, 30f) + Vector3.Up * 0.1f).ToNumerics());
        foreach (int x in _drive.Wait(10)) yield return x;
        float hp0 = v.Body.Damage;
        Bridge.Calm = false;
        TornadoAt(v.Body.WorldPosition.ToGodot() + new Vector3(2f, 0f, 0f), TornadoShape.Cone);
        _impacts.Clear();
        bool caught = false, tossed = false;
        float peak = 0f, ground = v.Body.WorldPosition.Y;
        ImpactInfo landing = null;
        for (int i = 0; i < 60 * 40 && landing == null; i++)
        {
            yield return 0;
            VortexPhase phase = Bridge.PhaseOf(v.Body);
            caught |= phase == VortexPhase.Caught;
            tossed |= v.Body.Tossed > 0f;
            peak = MathF.Max(peak, v.Body.WorldPosition.Y - ground);
            if (phase == VortexPhase.Released) Sim.ClearTornadoes();
            if (tossed && peak > 5f) landing = _impacts.FirstOrDefault(x => x.Body == v.Body && x.Damage > 0f); // its first hard landing
        }
        // The blocks it wore in that first landing (the damage lands a few ticks on), in the hull's own frame as it struck.
        N.Vector3 hitLocal = landing == null ? default : v.Body.ToLocal(landing.Point);
        N.Vector3 normalLocal = landing == null ? default : N.Vector3.Transform(landing.Normal, N.Quaternion.Inverse(v.Body.Rotation));
        for (int i = 0; i < 6; i++) yield return 0;
        var wornFirst = s.Blocks().Where(b => Building.IsDamaged(s, b.pos)).Select(b => StructureBridge.CellCenter(s, b.pos)).ToList();
        // Where it struck, and which way the ground faced, in the hull's own frame as it struck (it rolls and settles after).
        foreach (int x in _drive.Wait(30)) yield return x;
        Check(caught && tossed && peak > 5f, $"a tornado catches a landed {s.Name} ({v.Body.Mass / 1000f:F1} t): its engines can't hold it, and it is carried {peak:F0} m up");
        Check(landing != null && v.Body.Damage > hp0, $"it comes down by the one impact rule: {landing?.Speed:F1} m/s on {landing?.Surface}, {landing?.Damage:F2} hull damage");
        if (landing != null)
        {
            var worn = wornFirst;
            N.Vector3 middle = v.Body.CenterOfMass;
            int facing = worn.Count(p => N.Vector3.Dot(p - middle, normalLocal) < 0f); // on the side that met the ground
            float nearest = worn.Count == 0 ? float.MaxValue : worn.Min(p => N.Vector3.Distance(p, hitLocal));
            Check(worn.Count > 0 && nearest < 3f, $"and its blocks are worn where it struck: {facing} of {worn.Count} worn blocks on that side, the nearest {nearest:F1} m from the impact point");
        }
        Remove(v);
        Calm();

        Structure tank = Zone.Data.Blueprint("tank.gbp");
        Vector3 tat = Spot(120f, 100f);
        Vessel big = Vessel.Spawn(Zone, tank, (tat + Vector3.Up * 4f).ToNumerics(), 0f, landed: true);
        foreach (int x in _drive.Wait(90)) yield return x;
        N.Vector3 before = big.Body.WorldPosition;
        Bridge.Calm = false;
        TornadoAt(before.ToGodot() + new Vector3(2f, 0f, 0f), TornadoShape.Wedge);
        float rise = 0f;
        bool woke = false;
        for (int i = 0; i < 60 * 15; i++)
        {
            yield return 0;
            rise = MathF.Max(rise, big.Body.WorldPosition.Y - before.Y);
            woke |= !big.Body.Asleep;
        }
        float moved = N.Vector3.Distance(big.Body.WorldPosition, before);
        Check(rise < 0.5f && moved < 2f, $"the strongest tornado leaves a landed hover tank ({big.Body.Mass / 1000f:F0} t) where it stands: {moved:F2} m moved, {rise:F2} m lifted{(woke ? ", knocked awake" : ", never woken")}");
        Remove(big);
        Calm();
    }

    /// <summary>Takes a test's vessel out of the world, as a vessel whose last block is gone goes.</summary>
    void Remove(Vessel v)
    {
        Bridge.Untrack(v.Body);
        Zone.Physics.Unregister(v.Body);
        Zone.Physics.StuckFields.Remove(v.Field);
        Zone.Untrack(v);
        v.Node.QueueFree();
    }

    // ---- People -------------------------------------------------------------------------

    /// <summary>
    /// A survivor flung by a tornado lands by the one impact rule, with nothing held back; one who
    /// fires the jetpack on the way down lands slower and loses less, from the same tornado.
    /// </summary>
    IEnumerable<int> FlingAndJetpack()
    {
        var result = new (float speed, double lost, float peak, bool flung)[2];
        for (int attempt = 0; attempt < 8; attempt++)
        {
            int run = attempt % 2;
            bool jet = run == 1;
            Calm();
            Vector3 at = OpenSpot(-40f + attempt * 60f, 140f + run * 70f);
            Me.PlaceOutside((at + Vector3.Up * 0.1f).ToNumerics());
            foreach (int x in _drive.UntilSettled()) yield return x;
            FullHealth();
            for (int i = 0; i < 60 * 8 && Bridge.PhaseOf(Me.Body) != VortexPhase.Free; i++) yield return 0; // the last fling's grace has passed
            for (int i = 0; i < 60 * 8 && Me.Legs.JetpackFuel < Me.Legs.JetpackFuelMax; i++) yield return 0; // a full pack
            double health0 = _game.Suit.Vitals.Health;
            Bridge.Calm = false;
            TornadoAt(at + new Vector3(2f, 0f, 0f), TornadoShape.Cone);
            _impacts.Clear();
            bool released = false, landed = false;
            float peak = 0f, down = 0f;
            for (int i = 0; i < 60 * 40 && !landed; i++)
            {
                yield return 0;
                VortexPhase phase = Bridge.PhaseOf(Me.Body);
                if (!released && phase == VortexPhase.Released)
                {
                    released = true;
                    Sim.ClearTornadoes();
                }
                float height = Me.WorldFeet.Y - Zone.GroundAt(Me.WorldFeet.X, Me.WorldFeet.Z).Y;
                peak = MathF.Max(peak, height);
                // A survivor who reacts fires the pack once falling, low enough that the fuel lasts to the ground.
                if (jet && released) _game.Controls.Hold(Key.Space, Me.Body.WorldVelocity.Y < -4f && height < 30f);
                landed = released && Me.Body.OnFloor && peak > 5f;
                if (!Me.Body.OnFloor) down = -Me.Body.WorldVelocity.Y; // how fast it was coming down the tick before it touched
            }
            foreach (int x in _drive.Wait(10)) yield return x;
            bool wet = Me.Body.Water.Fraction > 0f;
            GD.Print($"      fling {(jet ? "firing the jetpack" : "doing nothing")}: {peak:F0} m up, came down at {down:F1} m/s, lost {health0 - _game.Suit.Vitals.Health:F0}{(wet ? ", in water: again" : "")}");
            if (!wet && landed) result[run] = (down, health0 - _game.Suit.Vitals.Health, peak, released);
            _game.Controls.ReleaseAll();
            if (_game.Suit.Dead) foreach (int x in Revive()) yield return x;
            if (result[0].flung && result[1].flung) break;
        }
        var (s0, l0, p0, f0) = result[0];
        var (s1, l1, p1, f1) = result[1];
        Check(f0 && p0 > 15f && l0 > 0 && s0 > 15.5f, $"a tornado flings the survivor {p0:F0} m up, and the landing at {s0:F1} m/s takes {l0:F0} health, in full");
        Check(f1 && p1 > 15f && s1 < s0 - 3f && l1 < l0, $"firing the jetpack on the way down lands them at {s1:F1} m/s instead, losing {l1:F0} (from {p1:F0} m up)");
    }

    IEnumerable<int> Revive()
    {
        _game.Controls.Tap(Key.R);
        foreach (int x in _drive.Wait(60)) yield return x;
        FullHealth();
    }

    /// <summary>
    /// Caught in a strong tornado's core, the jetpack fired outward the whole time does not break its
    /// grip: it lets go when it lets go. At the edge of a weak one near its end, the same burn breaks free.
    /// </summary>
    IEnumerable<int> StrongCore()
    {
        var held = new float[2];
        var free = new bool[2];
        for (int run = 0; run < 2; run++)
        {
            bool weak = run == 1;
            Calm();
            Vector3 at = OpenSpot(60f, 140f + run * 70f);
            Me.PlaceOutside((at + Vector3.Up * 0.1f).ToNumerics());
            foreach (int x in _drive.UntilSettled()) yield return x;
            FullHealth();
            for (int i = 0; i < 60 * 8 && Bridge.PhaseOf(Me.Body) != VortexPhase.Free; i++) yield return 0;
            Bridge.Calm = false;
            Tornado t = TornadoAt(at + new Vector3(weak ? 3f : 2f, 0f, 0f), weak ? TornadoShape.Rope : TornadoShape.Wedge);
            if (weak) { t.Kept = false; t.Dissipate(); } // near the end of its life: its grip fading
            float caughtFor = 0f, ground = at.Y;
            bool broke = false;
            for (int i = 0; i < 60 * 10; i++)
            {
                yield return 0;
                VortexPhase phase = Bridge.PhaseOf(Me.Body);
                if (phase == VortexPhase.Caught)
                {
                    caughtFor += 1f / 60f;
                    // Face away from the funnel and burn: up and out.
                    Vector3 away = Me.WorldFeet - t.Position;
                    away.Y = 0f;
                    _drive.Face(Me.WorldFeet + away.Normalized() * 10f);
                    _game.Controls.Hold(Key.W);
                    _game.Controls.Hold(Key.Space);
                }
                else if (caughtFor > 0f)
                {
                    // Let go by the tornado (at the top, at the end of its carry, or as it dies), or broken free past the edge of its grip?
                    float r = new Vector2(Me.WorldFeet.X - t.Position.X, Me.WorldFeet.Z - t.Position.Z).Length();
                    float edge = ForceModule.HoldRadius(Weather.Glue.ToVortex(t)) * 1.6f;
                    broke = r >= edge - 1.5f && t.Strength > 0.3f;
                    GD.Print($"      {(weak ? "weak rope" : "strong wedge")}: let go after {caughtFor:F1} s at strength {t.Strength:F2}, {Me.WorldFeet.Y - ground:F0} m up, {r:F0} m out (its grip ends at {edge:F0}){(broke ? ": broke free" : "")}");
                    break;
                }
            }
            _game.Controls.ReleaseAll();
            held[run] = caughtFor;
            free[run] = broke;
            Sim.ClearTornadoes();
            for (int i = 0; i < 60 * 15 && !Me.Body.OnFloor; i++) yield return 0;
            if (_game.Suit.Dead) foreach (int x in Revive()) yield return x;
        }
        Check(held[0] > 1f && !free[0], $"the jetpack burning up and out can't break a strong wedge's core: held {held[0]:F1} s, until it let go");
        Check(held[1] > 0f && free[1], $"in a weak rope near its end the same burn breaks free of its grip: after {held[1]:F1} s");
    }

    static float TiltOf(N.Quaternion q) => N.Vector3.Transform(N.Vector3.UnitY, q).ToGodot().AngleTo(Vector3.Up);

    /// <summary>
    /// A hovercraft and a hover bike, landed and parked: a hurricane's gale doesn't tip them (their plates
    /// hold them near the ground), but a tornado's grip beats their gyros and plates, and they tip, roll
    /// and flip in the air. Where they come down on their side or roof they stay, until someone rights them.
    /// </summary>
    IEnumerable<int> TumblingCraft()
    {
        Calm();
        Vector3 at = OpenSpot(-200f, 260f);
        Me.PlaceOutside((at - new Vector3(0f, 0f, -60f) + Vector3.Up * 0.1f).ToNumerics());
        foreach (int x in _drive.UntilSettled()) yield return x;
        Structure plan = Zone.Data.Blueprint("rover.gbp");
        Vessel rover = Vessel.Spawn(Zone, plan, (at + Vector3.Up * 3f).ToNumerics(), 0f, landed: true);
        Slice.Vehicles.HoverBike bike = _game.Bikes.Place(at + new Vector3(-6f, 0f, 3f), 0.4f);
        foreach (int x in _drive.Wait(90)) yield return x;

        // A hurricane first: nothing tips them on the ground.
        Sim.Snap(WeatherKind.Hurricane);
        Bridge.Calm = false;
        float galeTilt = 0f, galeBike = 0f;
        for (int i = 0; i < 60 * 6; i++)
        {
            yield return 0;
            galeTilt = MathF.Max(galeTilt, TiltOf(rover.Body.Rotation));
            galeBike = MathF.Max(galeBike, bike.Tilt);
        }
        Check(galeTilt < 0.15f && galeBike < 0.5f, $"a hurricane's gale doesn't tip a parked {plan.Name} or hover bike: at most {Mathf.RadToDeg(galeTilt):F0}° and {Mathf.RadToDeg(galeBike):F0}°");

        // Then a tornado over both.
        Sim.Snap(WeatherKind.Clouds);
        TornadoAt(at + new Vector3(-2f, 0f, 1f), TornadoShape.Cone);
        float roverTilt = 0f, bikeTilt = 0f, roverUp = 0f;
        bool tossed = false;
        for (int i = 0; i < 60 * 25; i++)
        {
            yield return 0;
            tossed |= rover.Body.Tossed > 0f;
            if (rover.Body.Tossed > 0f) roverTilt = MathF.Max(roverTilt, TiltOf(rover.Body.Rotation));
            roverUp = MathF.Max(roverUp, rover.Body.WorldPosition.Y - at.Y);
            bikeTilt = MathF.Max(bikeTilt, bike.Tilt);
            if (Bridge.PhaseOf(rover.Body) == VortexPhase.Released) Sim.ClearTornadoes();
            if (i > 60 * 8 && rover.Controller.Frozen && !bike.Toppled && bike.Grounded && Sim.Tornadoes.Count == 0) break;
        }
        Sim.ClearTornadoes();
        for (int i = 0; i < 60 * 10 && !(rover.Controller.Frozen && rover.Body.Tossed <= 0f); i++) yield return 0;
        float lies = TiltOf(rover.Body.Rotation);
        foreach (int x in _drive.Wait(60 * 4)) yield return x;
        float still = TiltOf(rover.Body.Rotation);
        Check(tossed && roverTilt > MathF.PI / 2f, $"a tornado tips the {plan.Name} over in the air ({roverUp:F0} m up): it turned {Mathf.RadToDeg(roverTilt):F0}° from upright, past what its gyros and plates hold");
        Check(MathF.Abs(still - lies) < 0.05f, $"it lies as it came down, {Mathf.RadToDeg(lies):F0}° from upright, and is still so 4 s on ({Mathf.RadToDeg(still):F0}°): its engines wait for a pilot");
        Check(bikeTilt > 1.2f, $"the hover bike flips too: {Mathf.RadToDeg(bikeTilt):F0}° from upright{(bike.Toppled ? ", and lies on its side until it is ridden" : ", and landed back on its plates")}");
        Remove(rover);
        _game.Bikes.Remove(bike);
        Calm();
    }

    /// <summary>
    /// A small flyer in the air, unpiloted: its gyros and thrusters hold it level and in place in a
    /// hurricane's gale; a tornado's grip beats them and it tumbles; once it is let go a pilot takes it
    /// back, if there is room under it.
    /// </summary>
    IEnumerable<int> Flyer()
    {
        Calm();
        Vector3 at = OpenSpot(220f, 260f);
        Me.PlaceOutside((at + new Vector3(0f, 0f, 60f) + Vector3.Up * 0.1f).ToNumerics());
        foreach (int x in _drive.UntilSettled()) yield return x;
        Structure plan = Zone.Data.Blueprint(Zone.Data.Rules.Build.StarterBlueprint);
        plan.Name = "Flyer";
        Vessel v = Vessel.Spawn(Zone, plan, (at + Vector3.Up * 14f).ToNumerics(), 0f, landed: false);
        v.Flight.Input = PilotInput.Autopilot;
        foreach (int x in _drive.Wait(60)) yield return x;
        Sim.Snap(WeatherKind.Hurricane);
        Bridge.Calm = false;
        float galeTilt = 0f;
        bool galeTossed = false;
        N.Vector3 start = v.Body.WorldPosition;
        for (int i = 0; i < 60 * 6; i++)
        {
            yield return 0;
            galeTilt = MathF.Max(galeTilt, TiltOf(v.Body.Rotation));
            galeTossed |= v.Body.Tossed > 0f;
        }
        float galeDrift = N.Vector3.Distance(v.Body.WorldPosition, start);
        Check(!galeTossed && galeTilt < 0.35f, $"in flight a small flyer's gyros and thrusters hold it through a hurricane's gale: {Mathf.RadToDeg(galeTilt):F0}° at most, {galeDrift:F1} m off station");

        Sim.Snap(WeatherKind.Clouds);
        Vector3 under = v.Body.WorldPosition.ToGodot();
        TornadoAt(Zone.GroundAt(under.X + 2f, under.Z), TornadoShape.Cone);
        float tilt = 0f;
        bool tossed = false, released = false, recovered = false, crashed = false;
        float recoverTilt = float.MaxValue;
        for (int i = 0; i < 60 * 25; i++)
        {
            yield return 0;
            tossed |= v.Body.Tossed > 0f;
            if (v.Body.Tossed > 0f) tilt = MathF.Max(tilt, TiltOf(v.Body.Rotation));
            if (!released && Bridge.PhaseOf(v.Body) == VortexPhase.Released)
            {
                released = true;
                Sim.ClearTornadoes();
                v.Flight.Input = PilotInput.Hands(new Vector3(0f, 1f, 0f)); // a pilot at the controls: climb
            }
            if (released && tossed && v.Body.Tossed <= 0f && !v.Controller.Frozen) recovered = true;
            if (recovered) recoverTilt = TiltOf(v.Body.Rotation);
            crashed |= _impacts.Any(x => x.Body == v.Body && x.Damage > 0f);
            if (recovered && v.Body.Tossed <= 0f && i % 60 == 0) break;
            if (v.Controller.Frozen) break;
        }
        v.Flight.Input = PilotInput.Autopilot;
        Check(tossed && tilt > MathF.PI / 2f, $"a tornado's grip beats the same flyer's gyros and thrusters: tossed, it turned {Mathf.RadToDeg(tilt):F0}° from upright");
        Check(recovered ? !v.Controller.Frozen : crashed, recovered
            ? $"let go, the pilot takes it back under power with room to spare ({Mathf.RadToDeg(recoverTilt):F0}° off level, to steer back)"
            : "let go too low to recover, it came down by the impact rule");
        Remove(v);
        Calm();
    }

    /// <summary>
    /// Trees in a tornado's path go by their size, as every body does: the light ones pulled up whole and
    /// thrown (a root hole left), the heavy ones snapped (a stump) or blown over by their root plate (a
    /// hole); they are gone for good from the world (the felled list), and only so many fly at once.
    /// </summary>
    IEnumerable<int> Trees()
    {
        Calm();
        // The nearest stand of trees on open ground.
        Vector3 stand = Vector3.Zero;
        int most = 0;
        for (int k = 0; k < 400; k++)
        {
            float a = k * 2.399f, r = 40f + k * 5f;
            Vector3 p = Spot(MathF.Cos(a) * r, MathF.Sin(a) * r);
            Me.PlaceOutside((p + Vector3.Up * 0.1f).ToNumerics());
            int n = Zone.Streamer.SolidsNear(p, 25f).Count(s => s.solid.Fellable);
            if (n > most) (most, stand) = (n, p);
            if (most >= 8) break;
            if (k % 20 == 19) foreach (int x in _drive.Wait(10)) yield return x; // let the ground stream in round the new spots
        }
        Me.PlaceOutside((stand + new Vector3(0f, 0f, 70f) + Vector3.Up * 0.1f).ToNumerics());
        foreach (int x in _drive.UntilSettled()) yield return x;
        var trees = Zone.Streamer.SolidsNear(stand, 25f).Where(s => s.solid.Fellable).Select(s => s.solid.Id).ToList();
        if (trees.Count == 0)
        {
            Check(false, "a stand of trees near the start to put a tornado through");
            yield break;
        }
        SurfaceWeather fx = Zone.Weather;
        int torn0 = fx.TreesTorn, thrown0 = fx.TreesThrown, snapped0 = fx.TreesSnapped, uprooted0 = fx.TreesUprooted;
        Bridge.Calm = false;
        Tornado t = TornadoAt(stand - new Vector3(40f, 0f, 0f), TornadoShape.Cone);
        t.Rules.Speed = 3f;
        int mostFlying = 0;
        for (int i = 0; i < 60 * 30; i++)
        {
            yield return 0;
            mostFlying = Math.Max(mostFlying, fx.Trees?.Flying ?? 0);
        }
        Sim.ClearTornadoes();
        int torn = fx.TreesTorn - torn0;
        var gone = trees.Where(id => Zone.Planet.Felled.Contains(id)).ToList();
        bool stillThere = Zone.Streamer.SolidsNear(stand, 40f).Any(s => gone.Contains(s.solid.Id));
        Check(torn > 0 && gone.Count > 0, $"a tornado through a stand of {trees.Count} trees tears out {gone.Count} of them ({torn} torn all told: {fx.TreesThrown - thrown0} pulled up and thrown, {fx.TreesSnapped - snapped0} snapped, {fx.TreesUprooted - uprooted0} blown over by the roots)");
        Check(!stillThere && (fx.Trees?.Stumps ?? 0) + (fx.Trees?.Holes ?? 0) > 0, $"they are gone from the world for good, leaving {fx.Trees?.Stumps ?? 0} stumps and {fx.Trees?.Holes ?? 0} root holes along its path");
        var (stood, flew) = fx.LastThrown;
        bool sameSize = fx.TreesThrown == thrown0 || (stood - flew).Length() < 0.05f * stood.Length() + 0.05f;
        Check(sameSize, fx.TreesThrown == thrown0 ? "(no tree was light enough to throw this time)"
            : $"a tree thrown flies as itself, full size: {stood.X:F1} × {stood.Y:F1} × {stood.Z:F1} m standing, {flew.X:F1} × {flew.Y:F1} × {flew.Z:F1} m in the air");
        Check(mostFlying <= SurfaceWeather.MostFlyingTrees, $"no more than {SurfaceWeather.MostFlyingTrees} trees are ever in the air at once ({mostFlying} at most)");
        Calm();
    }

    // ---- Creatures ----------------------------------------------------------------------

    /// <summary>Creatures of different weights in one tornado's reach: the light ones are thrown far, the heavy ones barely moved.</summary>
    IEnumerable<int> Creatures()
    {
        Calm();
        WildlifePatch patch = Zone.Wildlife;
        var species = (patch.Fauna?.Species ?? new List<Wildlife.Species>()).Where(s => !s.Flies && s.Rig != Wildlife.RigKind.Swarm).OrderBy(Wildlife.Heft.Mass).ToList();
        if (species.Count < 2 || patch.Sim == null)
        {
            Check(false, "this world has walking creatures of more than one kind to throw");
            yield break;
        }
        Vector3 at = Spot(0f, 220f);
        Me.PlaceOutside((Spot(0f, 150f) + Vector3.Up * 0.1f).ToNumerics());
        foreach (int x in _drive.Wait(30)) yield return x;
        patch.Frozen = false;
        var picks = new[] { species[0], species[species.Count / 2], species[^1] }.Distinct().ToList();
        Vector3 origin = patch.Origin;
        Wildlife.WildSim sim0 = patch.Sim; // if the patch moves on to a new sim, these creatures are left behind: stop following them
        var bodies = new List<(Wildlife.Species s, Wildlife.Creature c, Vector3 start)>();
        for (int k = 0; k < picks.Count; k++)
        {
            Vector3 p = at + new Vector3(MathF.Cos(k * 2.1f) * 4f, 0f, MathF.Sin(k * 2.1f) * 4f);
            Vector3 local = patch.PatchLocal(Zone.GroundAt(p.X, p.Z));
            Wildlife.Creature c = patch.Sim.Spawn(picks[k], local);
            bodies.Add((picks[k], c, c.Position + origin));
        }
        Bridge.Calm = false;
        TornadoAt(at, TornadoShape.Cone);
        var flew = new float[bodies.Count];
        var rose = new float[bodies.Count];
        var tumbled = new float[bodies.Count];
        bool downed = false, gotUp = false;
        for (int i = 0; i < 60 * 12; i++)
        {
            yield return 0;
            for (int k = 0; k < bodies.Count; k++)
            {
                Wildlife.Creature c = bodies[k].c;
                if (patch.Sim != sim0 || patch.Origin != origin) continue;
                Vector3 now = c.Position + origin;
                flew[k] = MathF.Max(flew[k], new Vector2(now.X - bodies[k].start.X, now.Z - bodies[k].start.Z).Length());
                rose[k] = MathF.Max(rose[k], now.Y - bodies[k].start.Y);
                tumbled[k] = MathF.Max(tumbled[k], (c.Tumble * Vector3.Up).AngleTo(Vector3.Up));
                downed |= c.Downed > 0f;
                gotUp |= downed && c.Downed <= 0f && !c.Thrown && c.Tumble.IsEqualApprox(Quaternion.Identity) && !c.Dead;
            }
        }
        for (int i = 0; i < 60 * 8 && downed && !gotUp; i++)
        {
            yield return 0;
            gotUp = bodies.Any(b => !b.c.Dead && b.c.Downed <= 0f && !b.c.Thrown && b.c.Tumble.IsEqualApprox(Quaternion.Identity) && rose[bodies.IndexOf(b)] > 3f);
        }
        Check(rose[0] < 3f || tumbled[0] > 1.2f, $"a thrown creature tumbles in the air, not level: the {bodies[0].s.Name} turned {Mathf.RadToDeg(tumbled[0]):F0}° from upright");
        Check(!downed || gotUp, $"one that lands hard on its side lies there, then gets up ({(downed ? "downed, and up again" : "none came down on its side this time")})");
        string row = string.Join(", ", bodies.Select((b, k) => $"{b.s.Name} {Mass(Wildlife.Heft.Mass(b.s))}: {rose[k]:F0} m up, {flew[k]:F0} m off"));
        bool ordered = true;
        for (int k = 1; k < bodies.Count; k++) ordered &= flew[k] <= flew[k - 1] * 1.15f + 2f;
        Check(ordered && (rose[0] > 3f || Wildlife.Heft.Mass(bodies[0].s) > 1500f), $"creatures in a tornado are thrown by their own mass: {row}");
        patch.Frozen = true;
        Calm();
    }
}
