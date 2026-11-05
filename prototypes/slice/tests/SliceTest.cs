using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using Planets.Gen;
using PhysicsSandbox.Bridges;
using PlanetSurface;
using PlanetSurface.World;
using ShipHandling.Handling;
using Slice.Construction;
using Slice.Game;
using Slice.Player;
using Slice.Surface;
using Slice.Vessels;
using SurvivalVitals.Model;
using Weather.Model;
using Wildlife;
using WeatherKind = Weather.Model.WeatherKind;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;
using Survivor = Slice.Player.Survivor;

namespace Slice.Tests;

/// <summary>
/// The slice played through, headless, by its own controls (tests/Driver): the pod comes down,
/// the castaway empties its locker, walks to the nearest deposits and digs them, cuts a plant,
/// builds the starter vessel from its blueprint block by block (paying for every one), builds it
/// again in Creative (paying nothing), boards it, takes off, flies 2 km and lands on its gear.
/// Survival runs the whole time and must warn before any harm. Run with:
///   .\tools\test.ps1 -Project prototypes/slice -Scene res://tests/SliceTest.tscn
/// (test-all adds --fixed-fps 60 from SliceTest.args.) Exits 0 if every check passes, 1 if not.
/// </summary>
public partial class SliceTest : Node
{
    Game.Game _game;
    Driver _drive;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly List<RescueInfo> _rescues = new();
    readonly List<ImpactInfo> _impacts = new();
    readonly List<string> _harmUnwarned = new();
    int _landed, _liftedOff;
    double _lastHealth;
    Vessel _skiff;
    const float Dt = 1f / 60f;

    public override void _Ready()
    {
        ProcessPhysicsPriority = 100; // checks after the game has stepped
        GD.Print("-- the planet, before anything moves --");
        PureChecks();
        _run = Scenarios().GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        bool more;
        try
        {
            more = _run.MoveNext();
            if (more && _game?.Suit != null) WatchVitals();
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
        // Wildlife's way out: sound stopped and drained, shared caches released, then quit.
        Wildlife.Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    IEnumerable<int> Scenarios()
    {
        _game = new Game.Game { Interactive = false, SkipIntro = true };
        AddChild(_game);
        _drive = new Driver(_game);
        foreach (int x in _drive.UntilPlaying()) yield return x;
        SurfaceZone zone = _game.Zone;
        var severe = zone.Weather.Sim.Timeline.Where(seg => seg.Start < SurfaceWeather.CalmOpening && zone.Data.Weather.State(seg.Kind).Severe).ToList();
        Check(severe.Count == 0, $"the weather opens calm: no severe weather forecast for the first {SurfaceWeather.CalmOpening / 60:F0} minutes ({string.Join(", ", zone.Weather.Sim.Timeline.Take(4).Select(seg => seg.Kind))} �)");
        // The planet's weather is held calm and its creatures still for the scripted run, so it
        // can't be blown off course; each gets its own check below.
        zone.Weather.Bridge.Calm = true;
        zone.Weather.Sim.Snap(WeatherKind.Clear);
        zone.Wildlife.Frozen = true;
        zone.Physics.Rescued += _rescues.Add;
        zone.Physics.Rescued += r => GD.Print($"  (rescued {r.Body.Label} from inside {r.Inside}: {r.From} to {r.To})");
        zone.Physics.Impact += _impacts.Add;
        zone.Physics.Impact += i =>
        {
            if (i.Body == _game.Survivor.Body && i.Damage > 0f)
                GD.Print($"  (the castaway hit {(i.Other?.Label ?? i.Surface.ToString())} at {i.Speed:F1} m/s: {i.Damage:F4} damage)");
        };
        zone.Physics.Landed += _ => _landed++;
        zone.Physics.LiftedOff += _ => _liftedOff++;
        _lastHealth = _game.Suit.Vitals.Health;

        foreach (int x in Arrival()) yield return x;
        foreach (int x in Locker()) yield return x;
        if (OS.GetCmdlineUserArgs().Contains("--quick"))
        {
            // Iterating on the later steps: hand over what gathering would have.
            foreach (var (item, n) in _game.Builder.TotalCost(_game.StarterPlan)) _game.Pack.Add(new Dictionary<string, int> { [item] = n });
        }
        else
        {
            foreach (int x in Gather()) yield return x;
            foreach (int x in CutPlant()) yield return x;
        }
        foreach (int x in Build()) yield return x;
        foreach (int x in BuildCreative()) yield return x;
        foreach (int x in Fly()) yield return x;
        foreach (int x in Replay()) yield return x;
        foreach (int x in Creatures()) yield return x;
        Finish();
    }

    // ==== Pure: the same seed gives the same start ================================================

    void PureChecks()
    {
        GameData data = GameData.Shared;
        SliceRules.PlanetRules p = data.Rules.Planet;
        var spec = PlanetSpec.Roll(p.Seed, Enum.Parse<Planets.Gen.Archetype>(p.Archetype), p.Variant);
        PlanetWorld a = PlanetWorld.Build(spec, null), b = PlanetWorld.Build(spec, null);
        Check(a.Landing == b.Landing && a.Map.ComputeHash() == b.Map.ComputeHash(),
            $"determinism: the starter planet generates identically twice (landing at {a.Landing.X:F0}, {a.Landing.Y:F0})");
        var fa = new FaunaGenerator(data.Wildlife.Fauna).Generate(spec.Seed, data.Rules.Wildlife.Alienness, false);
        var fb = new FaunaGenerator(data.Wildlife.Fauna).Generate(spec.Seed, data.Rules.Wildlife.Alienness, false);
        Check(fa.Species.Select(s => s.Name).SequenceEqual(fb.Species.Select(s => s.Name)) && fa.Species.Count >= 6,
            $"determinism: the planet's fauna rolls the same {fa.Species.Count} species twice ({string.Join(", ", fa.Species.Select(s => s.CommonName))})");

        Structure plan = data.Blueprint(data.Rules.Build.StarterBlueprint);
        Structure again = data.Blueprint(data.Rules.Build.StarterBlueprint);
        HandlingStats sa = Stats(plan), sb = Stats(again);
        Check(sa.Mass == sb.Mass && sa.LiftMargin == sb.LiftMargin && sa.TurnRate == sb.TurnRate,
            "determinism: the starter blueprint's four numbers come out the same twice");
        Check(sa.CanLiftOff && sa.LiftMargin > 0f && MathF.Abs(sa.LiftMargin - (sa.UpwardThrust - sa.Weight)) < 1e-3f,
            $"the starter vessel can lift off: lift margin {sa.LiftMargin:F1} kN = {sa.UpwardThrust:F1} up − {sa.Weight:F1} weight ({sa.SpareLift:F1} t spare)");
        GD.Print($"  starter: {plan.Count} blocks, {sa.Mass:F2} t, accel f/b/up/side {sa.Accel(Direction.Forward):F1}/{sa.Accel(Direction.Back):F1}/{sa.Accel(Direction.Up):F1}/{sa.Accel(Direction.Right):F1} m/s², " +
                 $"turn {sa.TurnRate:F0}°/s, top {sa.TopSpeed:F0} m/s, lift margin {sa.LiftMargin:F0} kN");
    }

    static HandlingStats Stats(Structure s)
    {
        GameData data = GameData.Shared;
        return HandlingModel.Compute(new StructureVessel(s, data.Handling), new FlightConditions(data.Rules.Planet.Gravity, true), data.Handling);
    }

    // ==== Crash-landed ==============================================================================

    IEnumerable<int> Arrival()
    {
        GD.Print("-- the pod --");
        SurfaceZone zone = _game.Zone;
        Vector3 landing = zone.LandingSite;
        Vector3 pod = _game.Pod.Body.Position.ToGodot();
        Check(new Vector2(pod.X - landing.X, pod.Z - landing.Z).Length() < 0.01f && _game.Pod.Controller.Frozen,
            $"the escape pod stands landed at the generator's landing site ({pod.X:F0}, {pod.Y:F1}, {pod.Z:F0})");
        foreach (int x in _drive.Wait(30)) yield return x;
        Survivor s = _game.Survivor;
        Check(s.Body.OnFloor && s.Body.Frame == null && s.WorldFeet.DistanceTo(pod) < 6f && !Inside(s.Body),
            $"the castaway spawns at the pod, standing on the ground {s.WorldFeet.DistanceTo(pod):F1} m from it");
        Check(_game.Suit.Vitals.Health == _game.Suit.Sim.Data.Vitals.Health.Max && _game.Suit.Last.Exposure.Temperature is > 2 and < 30
              && _game.Suit.Last.Exposure.OxygenPercent >= 16,
            $"the landing site is survivable: {_game.Suit.Last.Exposure.Temperature:F1} °C, {_game.Suit.Last.Exposure.OxygenPercent:F0}% oxygen, {_game.Suit.Last.Exposure.Place}");
        Check(zone.Wildlife.Sim.Creatures.Count > 20 && zone.Wildlife.Fauna.Species.Count >= 6,
            $"the planet's own wildlife lives around the landing site: {zone.Wildlife.Sim.Creatures.Count} creatures of {zone.Wildlife.Fauna.Species.Count} species");
    }

    IEnumerable<int> Locker()
    {
        GD.Print("-- the pod's locker --");
        Vessel pod = _game.Pod;
        Int3 locker = pod.Structure.Blocks().First(b => pod.Structure.TypeOf(b.block).IsContainer).pos;
        Vector3 at = pod.Body.ToParent(StructureBridge.CellCenter(pod.Structure, locker)).ToGodot();
        foreach (int x in _drive.WalkTo(at, 1.4f, 20f)) yield return x;
        foreach (int x in _drive.LookAt(at, 6)) yield return x;
        foreach (int x in _drive.Tap(Key.E)) yield return x;
        foreach (int x in _drive.Wait(2)) yield return x;
        Dictionary<string, int> want = _game.Zone.Data.Rules.Materials.PodLocker;
        Check(want.All(kv => _game.Pack[kv.Key] == kv.Value) && pod.Cargo.Total == 0 && _game.Suit.Kit.CountFood("ration-pack") > 0,
            $"E at the pod's locker takes what it holds: {string.Join(", ", want.Select(kv => $"{_game.Pack[kv.Key]} {kv.Key}"))}, and the rations");
    }

    // ==== Gathering =================================================================================

    IEnumerable<int> Gather()
    {
        GD.Print("-- gathering --");
        Structure plan = _game.StarterPlan;
        IReadOnlyDictionary<string, int> need = _game.Builder.TotalCost(plan);
        GD.Print($"  the starter needs {string.Join(", ", need.Select(kv => $"{kv.Value} {kv.Key}"))}");
        var tried = new HashSet<Vector3>();
        bool first = true;
        foreach (DepositKind kind in DepositFinder.Kinds)
        {
            string material = SurvivalTool.MaterialOf(kind);
            for (int deposit = 0; deposit < 4 && _game.Pack[material] < need.GetValueOrDefault(material); deposit++)
            {
                Vector3? found = NearestUntried(kind, tried);
                if (found is not Vector3 ore) break;
                tried.Add(ore);
                int before = _game.Pack[material], gatheredBefore = _game.Tool.Gathered.GetValueOrDefault(material);
                Vector3 start = _game.Survivor.WorldFeet;
                foreach (int x in _drive.WalkTo(ore, 3.5f)) yield return x;
                float walked = new Vector2(_game.Survivor.WorldFeet.X - start.X, _game.Survivor.WorldFeet.Z - start.Z).Length();
                int cells = 0;
                foreach (int x in DigOut(ore, need.GetValueOrDefault(material), material, c => cells += c)) yield return x;
                int got = _game.Pack[material] - before;
                int yield1 = _game.Zone.Data.Rules.Materials.OrePerCell[material];
                GD.Print($"  {material}: walked {walked:F0} m to a deposit, dug {cells} ore cells for {got} {material}");
                if (first)
                {
                    first = false;
                    Check(got > 0 && got == cells * yield1 && _game.Tool.Gathered[material] - gatheredBefore == got,
                        $"digging ore fills the pack: {cells} {material} ore cells dug, {got} {material} in the pack ({yield1} a cell)");
                    Check(_game.Zone.Planet.Terrain.Store.RemovedCount > 0 && !Inside(_game.Survivor.Body),
                        $"the dig is real terrain, removed and logged: {_game.Zone.Planet.Terrain.Store.RemovedCount} cells so far, the castaway still in the open");
                }
            }
        }
        var shortOf = need.Where(kv => _game.Pack[kv.Key] < kv.Value).ToList();
        Check(shortOf.Count == 0,
            $"the nearest deposits hold enough for the starter vessel: {string.Join(", ", need.Select(kv => $"{_game.Pack[kv.Key]}/{kv.Value} {kv.Key}"))}");
    }

    Vector3? NearestUntried(DepositKind kind, HashSet<Vector3> tried)
    {
        SurfaceZone zone = _game.Zone;
        Vector3 from = _game.Survivor.WorldFeet;
        Vector3? best = null;
        float bestD = 700f;
        foreach (Deposit d in zone.Planet.Map.Deposits.InRect(from.X - 700, from.Z - 700, from.X + 700, from.Z + 700))
        {
            if (d.Kind != kind) continue;
            zone.Planet.Source.OreCentre(d, out double ox, out double oy, out double oz);
            Vector3 at = zone.Planet.NearestCopy(new Vector3((float)ox, (float)oy, (float)oz), from);
            if (tried.Contains(at)) continue;
            // Deposits under water can't be dug from the shore.
            if (zone.GroundAt(at.X, at.Z).Y < 0.5f) continue;
            float dist = new Vector2(at.X - from.X, at.Z - from.Z).Length();
            if (dist < bestD) { bestD = dist; best = at; }
        }
        return best;
    }

    /// <summary>Digs at an ore body until it gives no more, stepping down into the pit as it deepens.</summary>
    IEnumerable<int> DigOut(Vector3 ore, int enough, string material, Action<int> cellsDug)
    {
        int idle = 0;
        var store = _game.Zone.Planet.Terrain.Store;
        for (int pulse = 0; pulse < 160 && idle < 14 && _game.Pack[material] < enough; pulse++)
        {
            // Aim at the nearest ore cell still in the ground within reach.
            Vector3 eye = _game.Survivor.WorldEye();
            Vector3? target = NearestOre(ore, eye);
            if (target is not Vector3 t)
            {
                idle = 99;
                break;
            }
            if (eye.DistanceTo(t) > 6.5f)
            {
                foreach (int x in _drive.WalkTo(t, 1.2f, 6f)) yield return x;
                continue;
            }
            int before = _game.Tool.Gathered.GetValueOrDefault(material);
            foreach (int x in _drive.HoldPrimary(t, 2)) yield return x;
            for (int i = 0; i < 10; i++) yield return 0;
            int got = _game.Tool.Gathered.GetValueOrDefault(material) - before;
            cellsDug(got / Math.Max(1, _game.Zone.Data.Rules.Materials.OrePerCell[material]));
            idle = got > 0 ? 0 : idle + 1;
        }
    }

    /// <summary>The nearest ore cell of this deposit's body that is still solid.</summary>
    Vector3? NearestOre(Vector3 centre, Vector3 eye)
    {
        var store = _game.Zone.Planet.Terrain.Store;
        Vector3? best = null;
        float bestD = float.MaxValue;
        int cx = (int)MathF.Floor(centre.X), cy = (int)MathF.Floor(centre.Y), cz = (int)MathF.Floor(centre.Z);
        for (int y = -4; y <= 4; y++)
        for (int z = -4; z <= 4; z++)
        for (int x = -4; x <= 4; x++)
        {
            var cell = new TerrainDigging.Terrain.Int3(cx + x, cy + y, cz + z);
            if (store.MaterialAt(cell) != TerrainDigging.Terrain.CellMaterial.Ore) continue;
            var p = new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);
            float d = p.DistanceTo(eye);
            if (d < bestD) { bestD = d; best = p; }
        }
        return best;
    }

    IEnumerable<int> CutPlant()
    {
        GD.Print("-- a plant --");
        Vector3 feet = _game.Survivor.WorldFeet;
        Vector3? plant = null;
        foreach (int x in _drive.Wait(1)) yield return x;
        plant = NearestPlant(feet, 200f);
        if (plant is not Vector3 p)
        {
            Fail("no plant found near the castaway to cut");
            yield break;
        }
        foreach (int x in _drive.WalkTo(p, 1.8f, 40f)) yield return x;
        double organics = _game.Suit.Kit.Organics;
        int carbon = _game.Pack["carbon"];
        foreach (int x in _drive.HoldPrimary(p + Vector3.Up * 0.9f, 3)) yield return x;
        foreach (int x in _drive.Wait(90)) yield return x;
        Check(_game.Suit.Kit.Organics > organics && _game.Pack["carbon"] > carbon && _game.Tool.CutPlants.Count >= 1,
            $"cutting a plant gives organics (+{_game.Suit.Kit.Organics - organics:F1} kg) and carbon (+{_game.Pack["carbon"] - carbon})");
        bool gone = !_game.Zone.Streamer.FloraNear(p, 0.3f).Any();
        Check(gone, "the cut plant is gone from the ground");
    }

    /// <summary>The plant nearest a point within a radius, from the streamer's flora scatter.</summary>
    Vector3? NearestPlant(Vector3 from, float radius)
    {
        Vector3? best = null;
        float bestD = radius;
        foreach (var (_, root, _) in _game.Zone.Streamer.FloraNear(from, radius))
        {
            if (_game.Tool.CutPlants.Contains(((int)MathF.Floor(root.X), (int)MathF.Floor(root.Z)))) continue;
            float d = new Vector2(root.X - from.X, root.Z - from.Z).Length();
            if (d < bestD && root.Y > 0.6f) { bestD = d; best = root; }
        }
        return best;
    }

    // ==== Building ==================================================================================

    /// <summary>A flat, dry spot for a vessel this far from a point.</summary>
    Vector3 FlatSpot(Vector3 near, float distance, float startAngle = 0f)
    {
        SurfaceZone zone = _game.Zone;
        for (int i = 0; i < 64; i++)
        {
            float a = startAngle + i * 0.61f, r = distance + (i / 16) * 6f;
            var p = new Vector3(near.X + MathF.Cos(a) * r, 0f, near.Z + MathF.Sin(a) * r);
            float h0 = zone.GroundAt(p.X, p.Z).Y, spread = 0f;
            for (int k = 0; k < 8; k++)
            {
                float b = k * MathF.Tau / 8f;
                spread = MathF.Max(spread, MathF.Abs(zone.GroundAt(p.X + MathF.Cos(b) * 4f, p.Z + MathF.Sin(b) * 4f).Y - h0));
            }
            if (h0 > 1f && spread < 0.35f) return zone.GroundAt(p.X, p.Z);
        }
        return zone.GroundAt(near.X + distance, near.Z);
    }

    IEnumerable<int> Build()
    {
        GD.Print("-- building the starter vessel, block by block --");
        Structure plan = _game.StarterPlan;
        IReadOnlyDictionary<string, int> cost = _game.Builder.TotalCost(plan);
        var packBefore = new Dictionary<string, int>(_game.Pack.Items);
        Vector3 pad = FlatSpot(_game.Pod.Body.Position.ToGodot(), 16f);
        Vector3 stand = pad + (pad - _game.Pod.Body.Position.ToGodot()).Normalized() * 7f;
        foreach (int x in _drive.WalkTo(new Vector3(stand.X, 0, stand.Z), 1.5f)) yield return x;
        foreach (int x in _drive.Tap(Key.Key4)) yield return x;
        foreach (int x in _drive.LookAt(pad, 6)) yield return x;
        foreach (int x in _drive.Click()) yield return x;
        Vessel skiff = _game.Builder.Underway;
        Check(skiff != null && skiff.Structure.Count == 1 && skiff.Structure.Core != null && skiff.Controller.Frozen,
            "a click sets the blueprint down: its core is placed and paid for, a new vessel standing on the ground");
        if (skiff == null) yield break;
        _skiff = skiff;
        int placed = 0, stalls = 0;
        bool sawHalf = false;
        var watch = Stopwatch.StartNew();
        while (_game.Builder.Underway == skiff && stalls < 240)
        {
            int before = skiff.Structure.Count;
            _drive.Face(skiff.Body.Position.ToGodot());
            _game.Controls.Primary = true;
            yield return 0;
            stalls = skiff.Structure.Count > before ? 0 : stalls + 1;
            placed = skiff.Structure.Count;
            sawHalf |= placed * 2 >= plan.Count && placed < plan.Count;
        }
        _game.Controls.Primary = false;
        foreach (int x in _drive.Wait(5)) yield return x;
        Check(skiff.Structure.Count == plan.Count && sawHalf,
            $"holding the button builds it piece by piece: {skiff.Structure.Count} of {plan.Count} blocks, one at a time ({watch.Elapsed.TotalSeconds:F1} s real)");
        bool same = plan.Blocks().All(b => skiff.Structure.TryGet(b.pos, out Block got) && got.Type == b.block.Type && got.Shape == b.block.Shape && got.Rot == b.block.Rot);
        Check(same, "every block is where the blueprint has it, of its type, shape and turn");
        bool paid = cost.All(kv => packBefore.GetValueOrDefault(kv.Key) - _game.Pack[kv.Key] == kv.Value)
                    && cost.All(kv => _game.Builder.Spent.GetValueOrDefault(kv.Key) == kv.Value);
        Check(paid, $"every block was paid for from the pack: {string.Join(", ", cost.Select(kv => $"{packBefore.GetValueOrDefault(kv.Key) - _game.Pack[kv.Key]}/{kv.Value} {kv.Key}"))}");

        HandlingStats built = skiff.Flight.Stats ?? Stats(skiff.Structure), expected = Stats(plan);
        Check(MathF.Abs(skiff.Body.Mass - StructureBridge.Mass(plan).Mass) < 0.5f && MathF.Abs(built.Mass - expected.Mass) < 1e-3f,
            $"the vessel's body weighs what its blocks do: {skiff.Body.Mass:F0} kg");
        Check(MathF.Abs(built.LiftMargin - expected.LiftMargin) < 1e-2f && built.CanLiftOff
              && MathF.Abs(built.Accel(Direction.Forward) - expected.Accel(Direction.Forward)) < 1e-3f && built.TurnRate == expected.TurnRate,
            $"its four numbers are the blueprint's: {built.Accel(Direction.Forward):F1} m/s² forward, {built.TurnRate:F0}°/s, {built.TopSpeed:F0} m/s, lift margin {built.LiftMargin:+0} kN");
        Check(skiff.Controller.Gear != null && skiff.Controller.Gear.Count == 3, "its three landing legs are its controller's");
        Check(!_rescues.Any(r => r.Body == skiff.Body), "building never pushed the vessel out of the ground");
    }

    IEnumerable<int> BuildCreative()
    {
        GD.Print("-- the same build in Creative --");
        _game.Costs.Mode = BuildMode.Creative;
        _game.BuildMode = BuildMode.Creative;
        var pack = new Dictionary<string, int>(_game.Pack.Items);
        var spent = new Dictionary<string, int>(_game.Builder.Spent);
        double organics = _game.Suit.Kit.Organics;
        Vector3 pad = FlatSpot(_game.Pod.Body.Position.ToGodot(), 30f, 2.5f);
        Vector3 stand = pad + (pad - _game.Pod.Body.Position.ToGodot()).Normalized() * 7f;
        foreach (int x in _drive.WalkTo(new Vector3(stand.X, 0, stand.Z), 1.5f)) yield return x;
        foreach (int x in _drive.Tap(Key.Key4)) yield return x;
        foreach (int x in _drive.LookAt(pad, 6)) yield return x;
        foreach (int x in _drive.Click()) yield return x;
        Vessel twin = _game.Builder.Underway;
        foreach (int x in _drive.HoldPrimary(null, 2400, () => _game.Builder.Underway == null)) yield return x;
        Check(twin != null && twin.Structure.Count == _game.StarterPlan.Count,
            $"Creative builds the same blueprint: {twin?.Structure.Count} blocks");
        Check(_game.Pack.Items.All(kv => pack.GetValueOrDefault(kv.Key) == kv.Value) && pack.All(kv => _game.Pack[kv.Key] == kv.Value)
              && _game.Builder.Spent.All(kv => spent.GetValueOrDefault(kv.Key) == kv.Value) && _game.Suit.Kit.Organics == organics,
            "the same build in Creative consumes nothing: the pack and the spend are unchanged");
        _game.Costs.Mode = BuildMode.Survival;
        _game.BuildMode = BuildMode.Survival;
        foreach (int x in _drive.Tap(Key.Key1)) yield return x;
    }

    // ==== Flying ====================================================================================

    IEnumerable<int> Fly()
    {
        GD.Print("-- flying the starter vessel 2 km and landing --");
        Vessel v = _skiff;
        if (v == null) { Fail("no vessel to fly"); yield break; }
        foreach (int x in _drive.Board(v)) yield return x;
        Check(_game.Survivor.Seated == v, $"F at the seat boards the {v.Name}");
        if (_game.Survivor.Seated != v) yield break;
        double healthBefore = _game.Suit.Vitals.Health;
        Vector3 start = v.Body.Position.ToGodot();
        Vector3 target = FlatSpot(start, 2150f, 0.8f);
        int lifts = _liftedOff, lands = _landed;
        _impacts.Clear();

        // Lift off.
        _game.Controls.Hold(Key.Space);
        for (int i = 0; i < 90; i++) yield return 0;
        Check(_liftedOff == lifts + 1 && !v.Controller.Frozen && v.Body.Position.Y > start.Y + 2f,
            $"Space lifts it off: {v.Body.Position.Y - start.Y:F1} m up after 1.5 s");

        var probe = new FrameProbe(this);
        var windows = new List<double>();
        double windowSum = 0;
        int windowFrames = 0;
        float bestAgl = 0f, top = 0f;
        // Cruise toward the target at height, steering with the mouse stick.
        int ticks = 0;
        while (ticks++ < 60 * 120)
        {
            Vector3 p = v.Body.Position.ToGodot();
            Vector3 to = target - p;
            float flat = new Vector2(to.X, to.Z).Length();
            if (flat < 120f) break;
            Steer(v, to, flat);
            float agl = v.AboveGround;
            _game.Controls.Hold(Key.Space, agl < 60f);
            _game.Controls.Hold(Key.Ctrl, agl > 110f);
            top = MathF.Max(top, v.Body.Velocity.Length());
            bestAgl = MathF.Max(bestAgl, agl);
            if (ticks > 240)
            {
                windowSum += probe.LastMs;
                if (++windowFrames == 300)
                {
                    windows.Add(windowSum / windowFrames);
                    windowSum = 0;
                    windowFrames = 0;
                }
            }
            yield return 0;
        }
        probe.Remove();
        _game.Controls.ReleaseAll();

        // Come down on the gear.
        int landing = 0;
        while (!v.Controller.Frozen && landing++ < 60 * 90)
        {
            Vector3 p = v.Body.Position.ToGodot();
            Vector3 to = target - p;
            float flat = new Vector2(to.X, to.Z).Length();
            Steer(v, to, flat);
            float agl = v.AboveGround, vy = v.Body.Velocity.Y;
            float wanted = agl > 25f ? -8f : agl > 6f ? -3f : -1.2f;
            // Creep on toward the flat spot while coming down.
            float ground = new Vector2(v.Body.Velocity.X, v.Body.Velocity.Z).Length();
            _game.Controls.Hold(Key.W, flat > 12f && ground < 6f && agl > 4f);
            _game.Controls.Hold(Key.Ctrl, vy > wanted + 0.5f);
            _game.Controls.Hold(Key.Space, vy < wanted - 0.5f);
            yield return 0;
        }
        _game.Controls.ReleaseAll();
        foreach (int x in _drive.Wait(60)) yield return x;

        Vector3 end = v.Body.Position.ToGodot();
        float flown = new Vector2(end.X - start.X, end.Z - start.Z).Length();
        Check(flown >= 2000f, $"it flew {flown:F0} m across the planet, top speed {top:F0} m/s, up to {bestAgl:F0} m above the ground");
        float worst = _impacts.Where(i => i.Body == v.Body).Select(i => i.Speed).DefaultIfEmpty(0f).Max();
        Check(v.Controller.Frozen && _landed > lands && v.Controller.Gear.Deployed && v.Body.Damage == 0f && worst < 8f,
            $"it landed safely on its gear: landed and settled, no damage (hardest contact {worst:F1} m/s)");
        float tilt = MathF.Acos(Math.Clamp(MathX.Up(v.Body.Rotation).Y, -1f, 1f)) * 180f / MathF.PI;
        Check(tilt <= 12.5f && !Inside(v.Body), $"it stands on its feet ({tilt:F1}° of tilt), not in the ground");
        Check(_game.Suit.Vitals.Health >= healthBefore, "the pilot came through unhurt");
        if (windows.Count > 0)
        {
            double best = windows.Min();
            Check(best < 8.0, $"flying, the game's main-thread cost per frame stays under 8 ms: best window {best:F2} ms (windows {string.Join(", ", windows.Select(w => w.ToString("F2")))}; worst frame {probe.WorstMs:F1} ms)");
        }
        else Fail("the flight was too short to time");
    }

    /// <summary>A simple pilot: point the nose at the target with the mouse stick, thrust when roughly on course.</summary>
    void Steer(Vessel v, Vector3 to, float flat)
    {
        N.Vector3 f = MathX.Forward(v.Body.Rotation);
        float heading = MathF.Atan2(-f.X, -f.Z), wanted = MathF.Atan2(-to.X, -to.Z);
        float err = Mathf.Wrap(wanted - heading, -MathF.PI, MathF.PI);
        // Stick x yaws right; the mouse moves the stick (it recentres by itself).
        _game.Controls.Look = new Vector2(Math.Clamp(-err * 12f, -4f, 4f), 0f);
        _game.Controls.Hold(Key.W, MathF.Abs(err) < 0.35f && flat > 150f);
    }

    /// <summary>The same inputs from the same landed state give the same hop, tick for tick.</summary>
    IEnumerable<int> Replay()
    {
        GD.Print("-- determinism: a hop flown twice --");
        Vessel v = _skiff;
        if (v == null || _game.Survivor.Seated != v) yield break;
        N.Vector3 at = v.Body.Position;
        N.Quaternion rot = v.Body.Rotation;
        var runs = new List<List<N.Vector3>>();
        for (int run = 0; run < 2; run++)
        {
            v.Controller.PlaceLanded(v.Body, at, MathX.Heading(rot));
            v.Body.Rotation = rot;
            v.Flight.State = FlightState.At(rot.ToBasis());
            v.Binding.Teleport();
            for (int i = 0; i < 3; i++) yield return 0;
            var track = new List<N.Vector3>();
            for (int i = 0; i < 240; i++)
            {
                _game.Controls.Hold(Key.Space, i < 100);
                _game.Controls.Hold(Key.W, i is > 60 and < 140);
                _game.Controls.Look = new Vector2(i is > 80 and < 120 ? 3f : 0f, 0f);
                yield return 0;
                track.Add(v.Body.Position);
            }
            _game.Controls.ReleaseAll();
            runs.Add(track);
        }
        float diff = runs[0].Zip(runs[1], (a, b) => N.Vector3.Distance(a, b)).Max();
        Check(diff == 0f && N.Vector3.Distance(runs[0][^1], at) > 3f,
            $"determinism: the same inputs fly the same 4 s hop twice (max difference {diff} m over {runs[0].Count} ticks)");
        // Set it down again where it was.
        v.Controller.PlaceLanded(v.Body, at, MathX.Heading(rot));
        v.Body.Rotation = rot;
        v.Binding.Teleport();
        foreach (int x in _drive.Wait(10)) yield return x;
        _game.Controls.Tap(Key.F);
        foreach (int x in _drive.Wait(10)) yield return x;
    }

    // ==== Creatures ===============================================================================

    /// <summary>A hunter comes for the castaway: the visor warns before its first bite lands.</summary>
    IEnumerable<int> Creatures()
    {
        GD.Print("-- a creature attack is warned before it lands --");
        WildlifePatch w = _game.Zone.Wildlife;
        Species hunter = w.Fauna.Species.FirstOrDefault(s => s.Behaviour == Behaviour.Predator && !s.Flies)
                         ?? w.Fauna.Species.FirstOrDefault(s => s.Behaviour == Behaviour.PackHunter)
                         ?? w.Fauna.Species.First(s => s.Attack != null);
        if (_game.Survivor.Seated != null) _game.Controls.Tap(Key.F);
        foreach (int x in _drive.Wait(3)) yield return x;
        Vector3 clear = _skiff != null ? _skiff.Body.Position.ToGodot() + new Vector3(6f, 0f, 6f) : _game.Survivor.WorldFeet;
        _game.Survivor.PlaceOutside(_game.Zone.GroundAt(clear.X, clear.Z).ToNumerics() + N.Vector3.UnitY * 0.3f);
        // The wildlife follows the castaway: a patch of the planet's own animals is baked around them.
        float Away() { Vector3 l = w.PatchLocal(_game.Survivor.WorldFeet); return new Vector2(l.X, l.Z).Length(); }
        for (int i = 0; i < 600 && Away() > w.Sim.Terrain.HalfSize * 0.5f; i++) yield return 0;
        w.FinishFollowing();
        foreach (int x in _drive.Wait(30)) yield return x;
        Check(w.Patches >= 2 && Away() < w.Sim.Terrain.HalfSize * 0.5f && w.Sim.Creatures.Count > 10,
            $"the wildlife followed the castaway 2 km: a new patch of {w.Sim.Creatures.Count} creatures around them");
        Vector3 feet = _game.Survivor.WorldFeet;
        List<Creature> group = w.Sim.SpawnGroup(hunter, w.PatchLocal(feet + new Vector3(14f, 0f, 0f)), hunter.Behaviour == Behaviour.PackHunter ? 3 : 1);
        foreach (Creature g in group)
        {
            g.ProvokedBy = w.Player;
            g.ProvokedAt = w.Sim.Time;
        }
        w.Frozen = false;
        double threatAt = -1, hurtAt = -1;
        double health = _game.Suit.Vitals.Health;
        for (int i = 0; i < 60 * 25 && hurtAt < 0; i++)
        {
            if (threatAt < 0 && _game.Visor.ThreatShowing) threatAt = i;
            if (_game.Suit.Vitals.Health < health) hurtAt = i;
            yield return 0;
        }
        w.Frozen = true;
        Check(threatAt >= 0 && (hurtAt < 0 || threatAt < hurtAt),
            hurtAt < 0 ? $"a {hunter.CommonName} wound up to attack and the visor warned (THREAT) {(threatAt >= 0 ? "" : "NOT ")}— it never landed a bite"
                       : $"a {hunter.CommonName}'s attack: THREAT on the visor {(hurtAt - threatAt) / 60.0:F1} s before its bite cost {health - _game.Suit.Vitals.Health:F0} health");
        foreach (Creature g in group) if (!g.Dead) w.Sim.Kill(g, w.Player);
    }

    // ==== Throughout ================================================================================

    /// <summary>Every tick: any health lost must follow a warning of its kind on an earlier tick.</summary>
    void WatchVitals()
    {
        Suit suit = _game.Suit;
        double now = suit.Vitals.Health;
        if (now < _lastHealth - 1e-6)
        {
            double t = suit.Sim.Time;
            Alert harm = suit.Last.Harm;
            bool warned = harm == Alert.None
                ? _game.Visor.ThreatShowing || suit.WarnedAt.TryGetValue(Alert.Fall, out double fall) && fall < t
                : Enum.GetValues<Alert>().Where(a => a != Alert.None && (harm & a) != 0).All(a => suit.WarnedAt.TryGetValue(a, out double at) && at < t);
            if (!warned) _harmUnwarned.Add($"{_lastHealth - now:F1} health at {t:F1} s ({harm})");
        }
        _lastHealth = now;
    }

    void Finish()
    {
        GD.Print("-- throughout --");
        Suit suit = _game.Suit;
        Check(_harmUnwarned.Count == 0, _harmUnwarned.Count == 0
            ? $"vitals stayed sane: every loss of health came after its warning ({suit.TotalLost:F0} health lost in all, {suit.Vitals.Health:F0} left)"
            : $"health was lost without a warning first: {string.Join("; ", _harmUnwarned.Take(5))}");
        Check(suit.Vitals.Health > suit.Sim.Data.Vitals.Health.Max * 0.5 && suit.Vitals.Food > 0,
            $"the castaway ends the run healthy: {suit.Vitals.Health:F0} health, food {suit.FoodShare * 100:F0}%");
        var inside = _game.Zone.Physics.Bodies.Where(Inside).ToList();
        Check(inside.Count == 0, $"no stuck bodies: nothing of {_game.Zone.Physics.Bodies.Count} bodies is inside the ground ({_rescues.Count} rescues along the way: {string.Join(", ", _rescues.Select(r => r.Body.Label).Distinct())})");
        Check(!_rescues.Any(r => r.Body.Kind == BodyKind.Vessel), "no vessel ever needed rescuing from the ground");
    }

    bool Inside(Body b)
    {
        if (b.Frame != null && !b.OnOuterHull) return false;
        PlanetGround ground = _game.Zone.Ground;
        return ground.IsInside(b, b.WorldPosition, b.Shape);
    }

    // ---- Reporting --------------------------------------------------------------------------

    void Check(bool ok, string what)
    {
        if (ok)
        {
            _passes++;
            GD.Print($"  ok    {what}");
        }
        else Fail(what);
    }

    void Fail(string what)
    {
        _failures++;
        GD.Print($"  FAIL  {what}");
    }
}
