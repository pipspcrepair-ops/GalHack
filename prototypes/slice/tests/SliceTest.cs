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

    /// <summary>A run that hasn't finished in this long has hung: it fails rather than sitting there.</summary>
    const int WatchdogMinutes = 12;

    public override void _Ready()
    {
        StartWatchdog();
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
        _game = new Game.Game { Interactive = false, SkipIntro = true, World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Tested) };
        AddChild(_game);
        _drive = new Driver(_game);
        foreach (int x in _drive.UntilPlaying()) yield return x;
        SurfaceZone zone = _game.Zone;
        var severe = zone.Weather.Sim.Timeline.Where(seg => seg.Start < SurfaceWeather.CalmOpening && zone.Data.Weather.State(seg.Kind).Severe).ToList();
        Check(severe.Count == 0, $"the weather opens calm: no severe weather forecast for the first {SurfaceWeather.CalmOpening / 60:F0} minutes ({string.Join(", ", zone.Weather.Sim.Timeline.Take(4).Select(seg => seg.Kind))} â€¦)");
        // The planet's weather is held calm and its creatures still for the scripted run, so it
        // can't be blown off course; each gets its own check below.
        zone.Weather.Bridge.Calm = true;
        zone.Weather.Sim.Snap(WeatherKind.Clear);
        zone.Wildlife.Frozen = true;
        zone.Physics.Rescued += _rescues.Add;
        zone.Physics.Rescued += r => GD.Print($"  (rescued {r.Body.Label} from inside {r.Inside}: {r.From} to {r.To}; frame {_game.Survivor.Body.Frame?.Label} outer {_game.Survivor.Body.OnOuterHull} at {_clock.Elapsed.TotalSeconds:F1})");
        zone.Physics.Impact += _impacts.Add;
        zone.Physics.Impact += i =>
        {
            if (i.Body == _game.Survivor.Body && i.Damage > 0f)
                GD.Print($"  (the castaway hit {(i.Other?.Label ?? i.Surface.ToString())} at {i.Speed:F1} m/s: {i.Damage:F4} damage)");
        };
        zone.Physics.Landed += _ => _landed++;
        zone.Physics.LiftedOff += _ => _liftedOff++;
        _lastHealth = _game.Suit.Vitals.Health;

        foreach (int x in new Playthrough(_game).MakeTool()) yield return x;
        Check(_game.Pack.Hand == Slice.Items.HandKind.Tool, "the survival tool, made free in the suit fabricator, is in hand");
        foreach (int x in Arrival()) yield return x;
        foreach (int x in Locker()) yield return x;
        if (OS.GetCmdlineUserArgs().Contains("--locker-only")) yield break;
        if (!OS.GetCmdlineUserArgs().Contains("--quick"))
        {
            foreach (int x in Gather()) yield return x;
            foreach (int x in CutPlant()) yield return x;
            foreach (int x in OreRock()) yield return x;
            foreach (int x in HoverBike()) yield return x;
            foreach (int x in Chipping()) yield return x;
        }
        // SYSTEMS TEST SHORTCUT: this test checks building, flying and creatures, so it hands over
        // the parts the Skiff's bill needs and opens the tech tree. The real-player test
        // (PlayerTest) crafts every part and earns every unlock by playing.
        foreach (var (item, n) in _game.Builder.TotalCost(_game.StarterPlan)) _game.Pack.Add(item, n);
        _game.Progress.Creative = true;
        GD.Print($"  (systems test: handed over the Skiff's parts, {_game.Zone.Data.Items.Describe(_game.Builder.TotalCost(_game.StarterPlan), 6)}, and opened the tech tree)");
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
        WorldChoice world = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Tested);
        var spec = world.Spec();
        WorldChoice f1 = WorldChoice.Fresh(), f2 = WorldChoice.Fresh();
        Check(f1.Seed != f2.Seed && WorldChoice.FromArgs(new[] { "--seed", "42", "--planet", "ice" }) == new WorldChoice(42, Planets.Gen.Archetype.Ice, 0)
              && WorldChoice.FromArgs(new[] { "--seed=42" }).Seed == 42 && WorldChoice.FromArgs(System.Array.Empty<string>()) == null,
            $"seeds: a new game rolls a fresh one ({f1.Seed}, then {f2.Seed}); --seed and --planet pin both; this run: {world}");
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
            $"the starter vessel can lift off: lift margin {sa.LiftMargin:F1} kN = {sa.UpwardThrust:F1} up âˆ’ {sa.Weight:F1} weight ({sa.SpareLift:F1} t spare)");
        GD.Print($"  starter: {plan.Count} blocks, {sa.Mass:F2} t, accel f/b/up/side {sa.Accel(Direction.Forward):F1}/{sa.Accel(Direction.Back):F1}/{sa.Accel(Direction.Up):F1}/{sa.Accel(Direction.Right):F1} m/sÂ², " +
                 $"turn {sa.TurnRate:F0}Â°/s, top {sa.TopSpeed:F0} m/s, lift margin {sa.LiftMargin:F0} kN");
    }

    static HandlingStats Stats(Structure s)
    {
        GameData data = GameData.Shared;
        return HandlingModel.Compute(new StructureVessel(s, data.Handling), new FlightConditions(data.Rules.Planet.Gravity, true), data.Handling);
    }

    // ==== Crash-landed ==============================================================================

    IEnumerable<int> Arrival()
    {
        GD.Print("-- the bunker --");
        SurfaceZone zone = _game.Zone;
        Slice.Start.Bunker b = _game.Bunker;
        Vector3 landing = zone.LandingSite;
        float under = zone.GroundAt(b.RoomCentre.X, b.RoomCentre.Z).Y - b.RoomCentre.Y;
        Check(b.Ruin.IsRuin && b.Blocked(zone) == 0 && under > 4f && b.RoomCentre.Y > 1.5f,
            $"the cryo bunker is dug in at the start site: {under:F1} m under the ground, above the sea, {new Vector2(b.Outside.X - landing.X, b.Outside.Z - landing.Z).Length():F0} m from the generator's landing site, {b.Steps} steps up");
        foreach (int x in _drive.Wait(30)) yield return x;
        Survivor s = _game.Survivor;
        Check(s.Body.OnFloor && s.WorldFeet.DistanceTo(b.CryoStand) < 0.8f && !Inside(s.Body) && b.Mine.Open,
            $"the castaway wakes standing in their cryopod (the systems test opens it), {b.Dead.Count} dead sleepers beside it");
        Check(_game.Suit.Vitals.Health == _game.Suit.Sim.Data.Vitals.Health.Max && _game.Suit.Last.Exposure.Temperature is > 2 and < 30
              && _game.Suit.Last.Exposure.OxygenPercent >= 16,
            $"the start site is survivable: {_game.Suit.Last.Exposure.Temperature:F1} °C, {_game.Suit.Last.Exposure.OxygenPercent:F0}% oxygen, {_game.Suit.Last.Exposure.Place}");
        // Few by design, and fewer on a harsher world: from half a species' natural groups down by how livable it is.
        int most = (int)MathF.Ceiling(zone.Data.Rules.Wildlife.MostPerPatch * zone.Wildlife.Livable);
        Check((zone.Wildlife.Sim.Creatures.Count > 0 || zone.Wildlife.Livable < 0.5f) && zone.Wildlife.Sim.Creatures.Count <= most + 8 && zone.Wildlife.Fauna.Species.Count >= 6,
            $"the planet's own wildlife lives around the start, thinly: {zone.Wildlife.Sim.Creatures.Count} creatures of {zone.Wildlife.Fauna.Species.Count} species, at most {most} on a world {zone.Wildlife.Livable:P0} livable");
    }

    IEnumerable<int> Locker()
    {
        GD.Print("-- the bunker's locker, and the way out --");
        Slice.Start.Bunker b = _game.Bunker;
        foreach (int x in _drive.WalkTo(b.LockerFront, 0.4f, 15f)) yield return x;
        foreach (int x in _drive.LookAt(b.Locker, 6)) yield return x;
        foreach (int x in _drive.Tap(Key.E)) yield return x;
        foreach (int x in _drive.Wait(2)) yield return x;
        Check(_game.Opened?.Loot == b.MineLocker.Loot && _game.Menu.IsOpen, $"E at locker {b.MineLocker.Number} opens it beside the bag; the other three stay sealed ({b.Lockers.Count(l => l.Sealed)})");
        foreach (int x in _drive.ClickWidget("box:take-all")) yield return x;
        foreach (int x in _drive.Tap(Key.Tab)) yield return x;
        Dictionary<string, int> want = _game.Zone.Data.Rules.Materials.StartKit;
        Check(want.All(kv => _game.Pack[kv.Key] == kv.Value) && b.MineLocker.Loot.Storage.IsEmpty && _game.Suit.Kit.CountFood("ration-pack") > 0 && !_game.Menu.IsOpen,
            $"TAKE ALL empties the locker into the bag: {string.Join(", ", want.Select(kv => $"{_game.Pack[kv.Key]} {kv.Key}"))}, and the rations");
        foreach (int x in _drive.ClimbOut(b)) yield return x;
        Check(_drive.OnSurface && _game.Survivor.Body.OnFloor && !_rescues.Any(r => r.Body == _game.Survivor.Body),
            $"up the stairwell's {b.Steps} steps to the surface on foot, never rescued");
    }

    // ==== Gathering =================================================================================

    IEnumerable<int> Gather()
    {
        GD.Print("-- gathering --");
        // Some of each common ore, a deposit each: what digging gives is the systems check here.
        var need = new Dictionary<string, int> { ["iron-ore"] = 60, ["copper-ore"] = 24, ["silicon-ore"] = 24, ["carbon-ore"] = 24 };
        var tried = new HashSet<Vector3>();
        bool first = true;
        foreach (DepositKind kind in DepositFinder.Kinds)
        {
            if (_game.Tool.OreOf(kind) is not GalHack.Items.ItemDef oreItem || !need.ContainsKey(oreItem.Id)) continue;
            string material = oreItem.Id;
            for (int deposit = 0; deposit < 4 && _game.Pack[material] < need.GetValueOrDefault(material); deposit++)
            {
                Vector3? found = NearestUntried(kind, tried);
                if (found is not Vector3 ore) break;
                tried.Add(ore);
                int before = _game.Pack[material], gatheredBefore = _game.Tool.Gathered.GetValueOrDefault(material), tellBefore = _game.Tool.TellOre;
                Vector3 start = _game.Survivor.WorldFeet;
                foreach (int x in _drive.WalkTo(ore, 3.5f)) yield return x;
                float walked = new Vector2(_game.Survivor.WorldFeet.X - start.X, _game.Survivor.WorldFeet.Z - start.Z).Length();
                int cells = 0;
                foreach (int x in DigOut(ore, need.GetValueOrDefault(material), material, c => cells += c)) yield return x;
                int got = _game.Pack[material] - before;
                int yield1 = _game.Zone.Data.Rules.Materials.OrePerCell, tellOre = _game.Tool.TellOre - tellBefore; // the rock over a deposit gives a little first
                GD.Print($"  {material}: walked {walked:F0} m to a deposit, dug {cells} ore cells for {got} {material}");
                // The first deposit that gives anything (one against the bunker's own walls may not; the check below this loop wants them all).
                if (first && got > 0)
                {
                    first = false;
                    // Gathering chips and ore trickles in as the beam works (a cell gives its yield over its durability), so the
                    // pack holds what the beam earned, whole items as the shares add up, not a whole cell's yield per pulse.
                    Check(got > 0 && got >= tellOre && _game.Tool.Gathered[material] - gatheredBefore == got,
                        $"digging ore fills the pack as it chips: {got} {material} in the pack, {tellOre} off the rock over it, about {got / (float)yield1:F1} cells' worth ({yield1} a cell)");
                    Check(tellOre % Player.SurvivalTool.TellOrePerPulse == 0 && tellOre <= Player.SurvivalTool.TellPulses * Player.SurvivalTool.TellOrePerPulse,
                        $"an ore rock gives {Player.SurvivalTool.TellOrePerPulse} a pulse for {Player.SurvivalTool.TellPulses} pulses at most ({tellOre}), then the tool digs through");
                    Check(_game.Zone.Planet.Terrain.Store.RemovedCount > 0 && !Inside(_game.Survivor.Body),
                        $"the dig is real terrain, removed and logged: {_game.Zone.Planet.Terrain.Store.RemovedCount} cells so far, the castaway still in the open");
                }
            }
        }
        var shortOf = need.Where(kv => _game.Pack[kv.Key] < kv.Value).ToList();
        if (shortOf.Count > 0)
            GD.Print($"  (the bag: {_game.Pack.Storage.UsedSu:F0} SU used: {_game.Zone.Data.Items.Describe(_game.Pack.Storage.Contents, 8)}; gathered {string.Join(", ", _game.Tool.Gathered.Select(kv => $"{kv.Value} {kv.Key}"))}; feet {_game.Survivor.WorldFeet}, {_game.Zone.GroundAt(_game.Survivor.WorldFeet.X, _game.Survivor.WorldFeet.Z).Y - _game.Survivor.WorldFeet.Y:F1} m under the ground)");
        Check(shortOf.Count == 0,
            $"the nearest deposits give every common ore: {string.Join(", ", need.Select(kv => $"{_game.Pack[kv.Key]}/{kv.Value} {kv.Key}"))}");
    }

    /// <summary>A rock over a deposit stops the ray and the walker, and the tool breaks it down: a little ore a pulse, then it digs on through.</summary>
    /// <summary>The hover bike: set down from the toolbar, F to ride, fast and hovering above the ground, over water as if it were ground, F to get off, E to pick it up.</summary>
    IEnumerable<int> HoverBike()
    {
        GD.Print("-- the hover bike --");
        var surf = _game.Zone;
        Vector3 spot = default;
        for (int i = 0; i < 80; i++)
        {
            float a = i * 0.7f, r = 14f + i * 2.5f;
            var p = _game.Survivor.WorldFeet + new Vector3(MathF.Cos(a) * r, 0f, MathF.Sin(a) * r);
            float h = surf.GroundAt(p.X, p.Z).Y, spread = 0f;
            for (int k = 0; k < 8; k++)
                spread = MathF.Max(spread, MathF.Abs(surf.GroundAt(p.X + MathF.Cos(k * 0.785f) * 12f, p.Z + MathF.Sin(k * 0.785f) * 12f).Y - h));
            if (h > 1f && spread < 0.5f) { spot = surf.GroundAt(p.X, p.Z); break; }
        }
        if (spot == default) spot = surf.GroundAt(_game.Survivor.WorldFeet.X + 20f, _game.Survivor.WorldFeet.Z);
        _game.Survivor.PlaceOutside((spot + Vector3.Up * 0.2f).ToNumerics());
        foreach (int x in _drive.UntilSettled()) yield return x;
        // A clear run ahead, or the first trunk stops the speed check: as a rider picks a line.
        Vector3 chest = _game.Survivor.WorldFeet + Vector3.Up * 1.4f;
        for (int k = 0; k < 16; k++)
        {
            Vector3 way = new Vector3(0f, 0f, -1f).Rotated(Vector3.Up, k * Mathf.Tau / 16f);
            if (_game.Bikes.Blocked(chest, way, 32f)) continue;
            foreach (int x in _drive.LookAt(chest + way * 10f, 3)) yield return x;
            break;
        }
        _game.Pack.Storage.Add("hover-bike", 1);
        _game.Pack.Grid[1].Item = "hover-bike";
        _game.Pack.Grid[1].Count = 1;
        _game.Pack.Grid.Reconcile();
        foreach (int x in new Playthrough(_game).Select(1)) yield return x;
        Check(_game.Pack.Hand == Slice.Items.HandKind.Vehicle, "the hover bike on the toolbar is in hand as a vehicle");
        foreach (int x in _drive.LookAt(_game.Survivor.WorldFeet + new Vector3(0f, -1f, 0f) + new Vector3(0f, 0f, -2.5f).Rotated(Vector3.Up, _game.Survivor.Yaw), 6)) yield return x;
        foreach (int x in _drive.Click()) yield return x;
        foreach (int x in _drive.Wait(20)) yield return x;
        Check(_game.Bikes.All.Count == 1 && _game.Pack["hover-bike"] == 0, "clicking sets the hover bike down and takes it from the pack");
        Slice.Vehicles.HoverBike bike = _game.Bikes.All.Count > 0 ? _game.Bikes.All[0] : null;
        if (bike == null) yield break;
        float ground = surf.GroundAt(bike.Position.X, bike.Position.Z).Y;
        Check(MathF.Abs(bike.Position.Y - ground - Slice.Vehicles.HoverBike.Hover) < 0.3f, $"it hovers above the ground ({bike.Position.Y - ground:F2} m up)");
        foreach (int x in _drive.Tap(Key.F)) yield return x;
        Check(_game.Survivor.OnBike == bike, "F gets on the hover bike");
        Vector3 from = bike.Position;
        float top = 0f;
        _game.Controls.Hold(Key.W);
        for (int i = 0; i < 100; i++) { yield return 0; top = MathF.Max(top, bike.Speed); }
        _game.Controls.Hold(Key.W, false);
        float ran = new Vector2(bike.Position.X - from.X, bike.Position.Z - from.Z).Length();
        Check(top > 9f && ran > 8f, $"the hover bike is fast: {top:F1} m/s, {ran:F0} m in under two seconds (a walker sprints at {_game.Survivor.Legs.SprintSpeed:F1})");
        Check(_game.Survivor.WorldEye().DistanceTo(bike.Position) < 2f, "the rider goes with the bike");
        foreach (int x in _drive.Wait(90)) yield return x;
        foreach (int x in _drive.Tap(Key.F)) yield return x;
        Check(_game.Survivor.OnBike == null && _game.Survivor.WorldFeet.DistanceTo(bike.Position) < 4f && _game.Survivor.Body.Asleep == false, "F again gets off, beside the bike");
        foreach (int x in _drive.LookAt(bike.Seat, 6)) yield return x;
        foreach (int x in _drive.Tap(Key.E)) yield return x;
        Check(_game.Bikes.All.Count == 0 && _game.Pack["hover-bike"] == 1, "E at the bike picks it up again");

        // Over water: any lake or sea within reach, found in the terrain's own height field.
        var source = _game.Zone.Planet.Source;
        Vector3? lake = null;
        for (int ring = 1; ring <= 30 && lake == null; ring++)
            for (int k = 0; k < 24 && lake == null; k++)
            {
                float a = k * MathF.Tau / 24f, r = ring * 25f;
                float wx = _game.Survivor.WorldFeet.X + MathF.Cos(a) * r, wz = _game.Survivor.WorldFeet.Z + MathF.Sin(a) * r;
                bool deep = true;
                for (int d = 0; d < 8 && deep; d++)
                    deep = source.SurfaceHeight(wx + MathF.Cos(d * 0.785f) * 40f, wz + MathF.Sin(d * 0.785f) * 40f) < source.WaterLevel - 1f;
                if (deep) lake = new Vector3(wx, source.WaterLevel, wz);
            }
        if (lake is not Vector3 water) { GD.Print("  (no lake within 750 m of the castaway on this world: the water ride is not tried)"); yield break; }
        Slice.Vehicles.HoverBike boat = _game.Bikes.Place(water, 0f);
        boat.Ridden = true;
        boat.Throttle = 1f;
        float lowest = float.MaxValue, highest = float.MinValue;
        Vector3 start = boat.Position;
        for (int i = 0; i < 150; i++)
        {
            yield return 0;
            lowest = MathF.Min(lowest, boat.Position.Y);
            highest = MathF.Max(highest, boat.Position.Y);
        }
        float skimmed = new Vector2(boat.Position.X - start.X, boat.Position.Z - start.Z).Length();
        Check(boat.OnWater && lowest > source.WaterLevel + 0.4f && highest < source.WaterLevel + 1.6f && skimmed > 15f,
            $"over a lake the hover bike skims it as ground: {skimmed:F0} m, riding {lowest - source.WaterLevel:F2} to {highest - source.WaterLevel:F2} m above the water");
        _game.Bikes.Remove(boat);
    }

    /// <summary>Gathering chips: a pulse wears the ground without taking it, sustained work sinks it a cell at a time, and the better drills do it faster.</summary>
    IEnumerable<int> Chipping()
    {
        GD.Print("-- chipping --");
        foreach (int x in new Playthrough(_game).Tool(Slice.Player.ToolMode.Gather)) yield return x;
        var store = _game.Zone.Planet.Terrain.Store;
        Vector3 feet = _game.Survivor.WorldFeet;
        Vector3 fwd = new Vector3(0f, 0f, -1f).Rotated(Vector3.Up, _game.Survivor.Yaw);
        Vector3 Ground(float along, float across)
        {
            Vector3 q = feet + fwd * along + fwd.Rotated(Vector3.Up, Mathf.Pi / 2f) * across;
            return _game.Zone.GroundAt(q.X, q.Z);
        }
        Vector3 a = Ground(2.6f, -3f), b = Ground(2.6f, 3f);
        int before = store.RemovedCount;
        foreach (int x in _drive.HoldPrimary(a, 2)) yield return x;
        Check(store.RemovedCount == before && _game.Tool.ChippedCells > 0, $"one pulse of the beam chips the ground without taking a cell ({store.RemovedCount - before} cells gone, {_game.Tool.ChippedCells} worn)");
        // Soil goes in a second, bare rock takes a few: how long depends on what this world has underfoot.
        long held = 0;
        for (int i = 0; i < 6 && store.RemovedCount == before; i++, held++)
            foreach (int x in _drive.HoldPrimary(a, 60)) yield return x;
        int mid = store.RemovedCount - before;
        foreach (int x in _drive.HoldPrimary(a, 200)) yield return x;
        int late = store.RemovedCount - before;
        Check(mid > 0 && late > mid, $"sustained work sinks the ground a cell at a time: {mid} cells after {held} s, {late} after four more (the store holds whole cells, so it steps in stages)");

        // The better drill, on the other spot, in the same time.
        _game.Pack.Storage.Add("plasma-drill", 1);
        _game.Pack.Grid[2].Item = "plasma-drill";
        _game.Pack.Grid[2].Count = 1;
        _game.Pack.Grid.Reconcile();
        foreach (int x in new Playthrough(_game).Select(2)) yield return x;
        Check(_game.Pack.Hand == Slice.Items.HandKind.Tool, "the plasma drill is a hand tool");
        int plain = store.RemovedCount;
        foreach (int x in _drive.HoldPrimary(b, 60)) yield return x;
        int fast = store.RemovedCount - plain;
        Check(fast > mid * 1.4f, $"the plasma drill gathers faster: {fast} cells in a second against {mid} for the survival tool");
        foreach (int x in new Playthrough(_game).Select(0)) yield return x;
    }

    IEnumerable<int> OreRock()
    {
        GD.Print("-- an ore rock --");
        SurfaceZone zone = _game.Zone;
        Vector3 from = _game.Survivor.WorldFeet;
        var source = zone.Planet.Source;
        var rocks = zone.Planet.Map.Deposits.InRect(from.X - 400, from.Z - 400, from.X + 400, from.Z + 400)
            .Where(d => PlanetSurface.Infra.InfraLayer.TellOf(d) != GalHack.Deco.DecoTellKind.Fracture && _game.Tool.OreOf(d.Kind) != null
                && source.StampWeight(Mathx.WrapX(d.X, zone.Planet.Width), Mathx.Wrap(d.Y, zone.Planet.Height)) <= 0f
                && !source.WaterSurfaceAt(Mathx.WrapX(d.X, zone.Planet.Width), Mathx.Wrap(d.Y, zone.Planet.Height), out _))
            .Select(d => (d, at: zone.Planet.NearestCopy(new Vector3((float)d.X, from.Y, (float)d.Y), from)))
            .OrderBy(r => r.at.DistanceTo(from)).Take(6).ToList();
        if (_game.Pack.Selected != 0) foreach (int x in _drive.Tap(Key.Key1)) yield return x;
        if (_game.Tool.Mode != Slice.Player.ToolMode.Gather) foreach (int x in _drive.Tap(Key.Q)) yield return x;
        foreach (var (d, at) in rocks)
        {
            foreach (int x in _drive.WalkTo(at, 2.4f, 40f)) yield return x;
            // The rock's solid boulders lie round the deposit's middle: look over it till the tool is on one (a small pile has none).
            float radius = GalHack.Terrain.PlanetTerrainSource.OreRadius(d);
            Vector3? aim = null;
            var spots = new List<Vector2> { Vector2.Zero };
            for (int a = 0; a < 8; a++)
                foreach (float r in new[] { 0.5f, 1f })
                    spots.Add(Vector2.FromAngle(a * MathF.PI / 4f) * radius * r);
            foreach (Vector2 spot in spots)
            {
                Vector3 look = zone.GroundAt(at.X + spot.X, at.Z + spot.Y) + Vector3.Up * 0.35f;
                foreach (int x in _drive.LookAt(look, 3)) yield return x;
                foreach (int x in _drive.Wait(1)) yield return x;
                if (_game.Tool.OnTell) { aim = look; break; }
            }
            if (aim == null) continue;
            // The rock the tool is really on: deposits overlap, and digging for ore may have worked it already.
            Deposit rock = _game.Tool.Tell.Value;
            string ore = _game.Tool.OreOf(rock.Kind).Id;
            int left = _game.Tool.TellLeft(rock), before = _game.Tool.TellOre, pack = _game.Pack[ore], all = left * Slice.Player.SurvivalTool.TellOrePerPulse;
            foreach (int x in _drive.HoldPrimary(aim, 600, () => _game.Tool.TellOre - before >= all)) yield return x;
            foreach (int x in _drive.Wait(2)) yield return x;
            bool gone = _game.Tool.Tell is not Deposit still || still.X != rock.X || still.Y != rock.Y;
            Check(_game.Tool.TellOre - before == all && _game.Pack[ore] - pack == all && gone,
                $"an ore rock breaks down under the tool: {_game.Pack[ore] - pack} of {all} {ore} over the {left} of {Slice.Player.SurvivalTool.TellPulses} pulses it had left, then it digs on through (still on it: {!gone})");
            yield break;
        }
        GD.Print($"  (no ore rock to aim at among the {rocks.Count} nearest deposits)");
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
            // Near enough the surface for the survival tool (it can't cut deep rock).
            if (zone.GroundAt(at.X, at.Z).Y - at.Y > 5f) continue;
            // Not one the bunker was dug through (what is left of it is behind the bunker's walls).
            if (_game.Bunker.WayOut.Append(_game.Bunker.RoomCentre).Any(w => w.DistanceTo(at) < 10f)) continue;
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
                // Walking up to it counts as nothing dug: ore out of reach (deep, or behind the
                // bunker's walls) is given up on after a while.
                foreach (int x in _drive.WalkTo(t, 1.2f, 6f)) yield return x;
                idle++;
                continue;
            }
            int before = _game.Tool.Gathered.GetValueOrDefault(material);
            foreach (int x in _drive.HoldPrimary(t, 2)) yield return x;
            for (int i = 0; i < 10; i++) yield return 0;
            int got = _game.Tool.Gathered.GetValueOrDefault(material) - before;
            cellsDug(got / Math.Max(1, _game.Zone.Data.Rules.Materials.OrePerCell));
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
        foreach (int x in _drive.Wait(1)) yield return x;
        // A tree if there is one near (its trunk is solid), any plant otherwise; the next one if a
        // bank or a cliff keeps it out of the tool's reach.
        var tried = new HashSet<long>();
        Vector3 p = default;
        long id = 0;
        float reach = _game.Zone.Data.Rules.Tool.CutReach - 0.5f;
        // What was in hand before the walk: the walk digs its own way past what is in it, and the tool
        // (in gather mode, trigger held) can take the plant on the way (that once left +0 against a
        // baseline read after it), so the baseline comes first.
        double organics = _game.Suit.Kit.Organics;
        int biomass = _game.Pack["biomass"];
        for (int attempt = 0; attempt < 4; attempt++)
        {
            var found = NearestPlant(_game.Survivor.WorldFeet, 200f, trees: true, tried) ?? NearestPlant(_game.Survivor.WorldFeet, 200f, trees: false, tried);
            if (found is not var (root, plant)) break;
            tried.Add(plant);
            (p, id) = (root, plant);
            foreach (int x in _drive.WalkTo(p, 1.8f, 40f)) yield return x;
            // In reach, and not up a bank over the castaway's head, where the tool can't be aimed at it.
            bool InReach() => _game.Survivor.WorldEye().DistanceTo(p + Vector3.Up * 0.9f) <= reach && p.Y < _game.Survivor.WorldEye().Y;
            if (!InReach())
                foreach (int x in _drive.WalkTo(p, 0.9f, 15f)) yield return x;
            if (InReach()) break;
        }
        if (id == 0)
        {
            Fail("no plant found near the castaway to cut");
            yield break;
        }
        bool tree = _game.Zone.Streamer.SolidsNear(p, 3f).Any(s => s.solid.Id == id);
        bool trunkBefore = TrunkCollider(p, id);
        bool takenEnRoute = _game.Zone.Planet.Felled.Contains(id);
        bool cutThisFrame = false, colliderGoneThatFrame = false;
        if (_game.Pack.Selected != 0) foreach (int x in _drive.Tap(Key.Key1)) yield return x;
        if (_game.Tool.Mode != Slice.Player.ToolMode.Gather) foreach (int x in _drive.Tap(Key.Q)) yield return x;
        // Gathering chips: a plant takes a pulse or two, a tree a few seconds, each pulse paying a share.
        for (int i = 0; i < 1500 && !cutThisFrame && !takenEnRoute; i++)
        {
            _drive.Face(p + Vector3.Up * 0.9f);
            _game.Controls.Primary = true;
            yield return 0;
            if (_game.Zone.Planet.Felled.Contains(id))
            {
                cutThisFrame = true;
                colliderGoneThatFrame = !TrunkCollider(p, id);
            }
        }
        _game.Controls.Primary = false;
        if (!cutThisFrame && !takenEnRoute)
            GD.Print($"  (the cut: plant {id} at {p}, {(tree ? "a tree" : "a plant")}; eye {_game.Survivor.WorldEye()} {_game.Survivor.WorldEye().DistanceTo(p + Vector3.Up * 0.9f):F1} m from it; hand {_game.Pack.Hand}, mode {_game.Tool.Mode}, on a block {_game.Tool.OnBlock})");
        foreach (int x in _drive.Wait(90)) yield return x;
        Check(_game.Zone.Planet.Felled.Contains(id) && _game.Suit.Kit.Organics > organics && _game.Pack["biomass"] > biomass && _game.Tool.PlantsCut >= 1,
            $"cutting a plant gives organics (+{_game.Suit.Kit.Organics - organics:F1} kg) and biomass (+{_game.Pack["biomass"] - biomass})");
        bool gone = !_game.Zone.Streamer.FloraNear(p, 0.3f).Any();
        Check(gone && _game.Zone.Planet.Felled.Contains(id), "the cut plant is gone from the ground, and on the planet's felled list (kept through streaming and saves)");
        if (tree && takenEnRoute) GD.Print("  (the walk's own digging took this tree on the way: its collider is checked in the next plant)");
        else if (tree)
        {
            Check(trunkBefore && cutThisFrame && colliderGoneThatFrame && !TrunkCollider(p, id),
                $"the tree's trunk was solid, and its collider went in the frame it was cut (before {trunkBefore}, gone that frame {colliderGoneThatFrame})");
            Check(_game.Tool.TreesFelled >= 1 && _game.Tool.Trees != null && _game.Tool.Trees.Stumps >= 1,
                $"the felled tree leaves a stump ({_game.Tool.Trees?.Stumps ?? 0}) and falls over");
        }
        else GD.Print("  (no tree near: the plant cut was not a solid one)");
    }

    /// <summary>The plant nearest a point within a radius, from the streamer's flora scatter: trees only (solid trunks), or any.</summary>
    (Vector3 root, long id)? NearestPlant(Vector3 from, float radius, bool trees, HashSet<long> skip = null)
    {
        (Vector3, long)? best = null;
        float bestD = radius;
        var solids = trees ? _game.Zone.Streamer.SolidsNear(from, radius).Where(s => s.solid.IsTrunk && s.solid.Fellable).Select(s => s.solid.Id).ToHashSet() : null;
        foreach (var (_, root, _, id) in _game.Zone.Streamer.FloraNear(from, radius))
        {
            if ((solids != null && !solids.Contains(id)) || (skip?.Contains(id) ?? false)) continue;
            float d = new Vector2(root.X - from.X, root.Z - from.Z).Length();
            // On ground you can walk to: above the water, and not up or down a cliff from here.
            if (d < bestD && root.Y > 0.6f && MathF.Abs(root.Y - from.Y) < 3f) { bestD = d; best = (root, id); }
        }
        return best;
    }

    /// <summary>Whether a ray across a plant's root a metre up hits that plant's trunk collider.</summary>
    bool TrunkCollider(Vector3 root, long id)
    {
        var space = _game.Zone.GetWorld3D().DirectSpaceState;
        Vector3 at = root + Vector3.Up * 1f;
        foreach (Vector3 across in new[] { Vector3.Right, Vector3.Back })
        {
            // A felled tree's shape is disabled, so the ray passes; anything else it hits isn't this tree.
            var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(at - across * 1.5f, at + across * 1.5f, PlanetSurface.World.Layers.Flora));
            if (hit.Count > 0 && _game.Zone.Streamer.SolidHit((Rid)hit["rid"], (int)hit["shape"], out var solid) && solid.Id == id) return true;
        }
        return false;
    }

    // ==== Building ==================================================================================

    /// <summary>What blows and falls have cost, apart from what the climate wears away.</summary>
    double BodyHarm() => _game.Suit.Lost.GetValueOrDefault(DamageKind.Hit) + _game.Suit.Lost.GetValueOrDefault(DamageKind.Fall);

    /// <summary>A flat spot this far off in country as mild as can be found, without a thought for how the world lies.</summary>
    Vector3 MildSpot(Vector3 from, float distance)
    {
        Suit suit = _game.Suit;
        Band mild = suit.Sim.Data.Vitals.Temperature.AmbientComfort; // the air the body feels, so counting what the suit handles
        double low = mild.Low - suit.Sim.Mods.ColdProtection, high = mild.High + suit.Sim.Mods.HeatProtection;
        Vector3 best = from;
        double bestOff = double.MaxValue;
        for (int i = 0; i < 10 && bestOff > 0; i++)
        {
            Vector3 p = FlatSpot(from, distance, 0.8f + i * 0.63f);
            double t = suit.Exposure(new Pos(p.X, p.Y, p.Z), suit.Sim.Time).Temperature;
            double off = Math.Max(0, Math.Max(low - t, t - high));
            if (off < bestOff) (best, bestOff) = (p, off);
        }
        return best;
    }

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
        GalHack.Items.Bill cost = _game.Builder.TotalCost(plan);
        GalHack.Items.Bill packBefore = _game.Pack.Storage.Contents;
        _game.Builder.Project();
        Vector3 pad = new Playthrough(_game).FlatSpot(_game.Start, 16f, span: 10f); // room for you to stand off it and see it
        Vector3 stand = pad + (pad - _game.Start).Normalized() * 7f;
        foreach (int x in _drive.WalkTo(new Vector3(stand.X, 0, stand.Z), 1.5f)) yield return x;
        foreach (int x in _drive.LookAt(pad, 6)) yield return x;
        // The ground's collision may still be coming in where you look: the ghost shows once it can go down.
        for (int i = 0; i < 300 && !_game.Builder.PlanShown; i++)
            foreach (int x in _drive.LookAt(pad, 1)) yield return x;
        foreach (int x in _drive.Click()) yield return x;
        Vessel skiff = _game.Builder.Underway;
        Check(skiff != null && skiff.Structure.Count == 1 && skiff.Structure.Core != null && skiff.Controller.Frozen,
            $"a click sets the blueprint down: its core is placed and paid for, a new vessel standing on the ground (the tool says: {_game.Builder.Verdict})");
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
        bool paid = cost.All(kv => packBefore[kv.Key] - _game.Pack[kv.Key] == kv.Value)
                    && cost.All(kv => _game.Builder.Spent.GetValueOrDefault(kv.Key) == kv.Value);
        Check(paid, $"every block was paid for from the pack: {string.Join(", ", cost.Select(kv => $"{packBefore[kv.Key] - _game.Pack[kv.Key]}/{kv.Value} {kv.Key}"))}");

        HandlingStats built = skiff.Flight.Stats ?? Stats(skiff.Structure), expected = Stats(plan);
        Check(MathF.Abs(skiff.Body.Mass - StructureBridge.Mass(plan).Mass) < 0.5f && MathF.Abs(built.Mass - expected.Mass) < 1e-3f,
            $"the vessel's body weighs what its blocks do: {skiff.Body.Mass:F0} kg");
        Check(MathF.Abs(built.LiftMargin - expected.LiftMargin) < 1e-2f && built.CanLiftOff
              && MathF.Abs(built.Accel(Direction.Forward) - expected.Accel(Direction.Forward)) < 1e-3f && built.TurnRate == expected.TurnRate,
            $"its four numbers are the blueprint's: {built.Accel(Direction.Forward):F1} m/sÂ² forward, {built.TurnRate:F0}Â°/s, {built.TopSpeed:F0} m/s, lift margin {built.LiftMargin:+0} kN");
        Check(skiff.Controller.Gear != null && skiff.Controller.Gear.Count == 3, "its three landing legs are its controller's");
        Check(!_rescues.Any(r => r.Body == skiff.Body), "building never pushed the vessel out of the ground");
    }

    IEnumerable<int> BuildCreative()
    {
        GD.Print("-- the same build in Creative --");
        _game.Mode = GalHack.Items.GameMode.Creative;
        GalHack.Items.Bill pack = null; // taken at the click: the walk to the pad can dig its way and fill the bag
        var spent = new Dictionary<string, int>(_game.Builder.Spent);
        double organics = _game.Suit.Kit.Organics;
        Vessel twin = null;
        // Another pad if the first won't take it (a slope, a place's pad, something in the way).
        for (int attempt = 0; attempt < 3 && twin == null; attempt++)
        {
            Vector3 pad = FlatSpot(_game.Start, 30f + attempt * 15f, 2.5f + attempt * 1.3f);
            Vector3 stand = pad + (pad - _game.Start).Normalized() * 7f;
            foreach (int x in _drive.WalkTo(new Vector3(stand.X, 0, stand.Z), 1.5f)) yield return x;
            pack = _game.Pack.Storage.Contents;
            _game.Builder.Project(); // the CORE tab's projector: blueprints are never on the toolbar
            foreach (int x in _drive.LookAt(pad, 6)) yield return x;
            foreach (int x in _drive.Click()) yield return x;
            twin = _game.Builder.Underway;
            if (twin == null) GD.Print($"  (the blueprint at {pad}: {_game.Builder.Verdict})");
        }
        foreach (int x in _drive.HoldPrimary(null, 2400, () => _game.Builder.Underway == null)) yield return x;
        Check(twin != null && twin.Structure.Count == _game.StarterPlan.Count,
            $"Creative builds the same blueprint: {twin?.Structure.Count} blocks");
        Check(_game.Pack.Storage.Contents.Equals(pack)
              && _game.Builder.Spent.All(kv => spent.GetValueOrDefault(kv.Key) == kv.Value) && _game.Suit.Kit.Organics == organics,
            $"the same build in Creative consumes nothing: the pack and the spend are unchanged (pack {pack} now {_game.Pack.Storage.Contents}; organics {organics:F2} now {_game.Suit.Kit.Organics:F2}; spent now {string.Join(", ", _game.Builder.Spent.Where(kv => spent.GetValueOrDefault(kv.Key) != kv.Value).Select(kv => $"{kv.Key} {spent.GetValueOrDefault(kv.Key)}->{kv.Value}"))})");
        _game.Mode = GalHack.Items.GameMode.Survival;
        foreach (int x in _drive.Tap(Key.Key1)) yield return x;
    }

    // ==== Flying ====================================================================================

    IEnumerable<int> Fly()
    {
        GD.Print("-- flying the starter vessel 2 km and landing --");
        Vessel v = _skiff;
        if (v == null) { Fail("no vessel to fly"); yield break; }
        // The full run reaches the flight after the calm opening, and wind now pushes small
        // vessels: this check is about flying, so it flies in clear air.
        if (_game.Zone.Weather.Sim is { } sky && sky.Dominant != WeatherKind.Clear)
        {
            GD.Print($"  (the sky was {sky.Dominant}: cleared for the flight)");
            sky.Snap(WeatherKind.Clear);
        }
        // The Creative twin may stand between its pad and the Skiff, on legs low enough to wedge you under: this check is about flying.
        GD.Print("  (TEST: the castaway is set down beside the Skiff, clear of the Creative twin)");
        Vector3 beside = v.Body.Position.ToGodot() + new Vector3(6f, 0f, 6f);
        _game.Survivor.PlaceOutside(_game.Zone.GroundAt(beside.X, beside.Z).ToNumerics() + N.Vector3.UnitY * 0.3f);
        foreach (int x in _drive.UntilSettled()) yield return x;
        foreach (int x in _drive.Board(v)) yield return x;
        Check(_game.Survivor.Seated == v, $"F at the seat boards the {v.Name}");
        if (_game.Survivor.Seated != v) yield break;
        double harmBefore = BodyHarm();
        float damageBefore = v.Body.Damage; // a castaway running into it on the ground isn't the flight's doing
        Vector3 start = v.Body.Position.ToGodot();
        Vector3 target = MildSpot(start, 2150f); // a planet has cold and hot country: this check is about flying
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
        float bestAgl = 0f, top = 0f, flown = 0f;
        Vector3 was = start;
        // Cruise toward the target at height, steering with the mouse stick.
        int ticks = 0;
        while (ticks++ < 60 * 120)
        {
            Vector3 p = v.Body.Position.ToGodot();
            // Along the way, not from end to end: the world wraps, and on a small one 2 km comes round the seam.
            float stride = new Vector2(p.X - was.X, p.Z - was.Z).Length();
            if (stride < 50f) flown += stride;
            was = p;
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
            // Feet down: let go, and it settles. Pushing on would only skid it along a slope.
            bool down = v.Controller.Gear.Contact;
            _game.Controls.Hold(Key.Ctrl, !down && vy > wanted + 0.5f);
            _game.Controls.Hold(Key.Space, !down && vy < wanted - 0.5f);
            yield return 0;
        }
        _game.Controls.ReleaseAll();
        foreach (int x in _drive.Wait(60)) yield return x;

        Check(flown >= 2000f, $"it flew {flown:F0} m across the planet, top speed {top:F0} m/s, up to {bestAgl:F0} m above the ground");
        float worst = _impacts.Where(i => i.Body == v.Body).Select(i => i.Speed).DefaultIfEmpty(0f).Max();
        Check(v.Controller.Frozen && _landed > lands && v.Controller.Gear.Deployed && v.Body.Damage == damageBefore && worst < 8f,
            $"it landed safely on its gear: landed and settled, no damage (hardest contact {worst:F1} m/s)");
        float tilt = MathF.Acos(Math.Clamp(MathX.Up(v.Body.Rotation).Y, -1f, 1f)) * 180f / MathF.PI;
        Check(tilt <= 12.5f && !Inside(v.Body), $"it stands on its feet ({tilt:F1}Â° of tilt), not in the ground");
        Check(BodyHarm() <= harmBefore, "the pilot came through unhurt");
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
        if (v.Controller == null)
        {
            Fail($"the Skiff has no flight controller to replay with (its body: {v.Body.Kind}, {v.Body.Motion}, controller {v.Body.Controller?.GetType().Name ?? "none"})");
            yield break;
        }
        N.Vector3 at = v.Body.Position;
        N.Quaternion rot = v.Body.Rotation;
        var runs = new List<List<N.Vector3>>();
        // Both hops in the same still air: the weather moves on with time, and isn't an input.
        var weather = _game.Zone.Physics.ForceProviders.ToList();
        _game.Zone.Physics.ForceProviders.Clear();
        for (int run = 0; run < 2; run++)
        {
            if (v.Controller == null)
            {
                Fail($"the Skiff lost its flight controller in hop {run} (its body: {v.Body.Kind}, {v.Body.Motion}, {v.Structure.Count} blocks, damage {v.Body.Damage:F2}, at {v.Body.Position})");
                break;
            }
            v.Controller.PlaceLanded(v.Body, at, MathX.Heading(rot));
            v.Body.Rotation = rot;
            v.Flight.State = FlightState.At(rot.ToBasis());
            v.Binding.Teleport();
            for (int i = 0; i < 3; i++) yield return 0;
            var track = new List<N.Vector3>();
            for (int i = 0; i < 240; i++)
            {
                // Up, a little way forward with a turn, and down again under power onto the same ground.
                _game.Controls.Hold(Key.Space, i < 70);
                _game.Controls.Hold(Key.W, i is > 40 and < 90);
                _game.Controls.Hold(Key.Ctrl, i is > 150 and < 200);
                _game.Controls.Look = new Vector2(i is > 50 and < 80 ? 3f : 0f, 0f);
                yield return 0;
                track.Add(v.Body.Position);
            }
            _game.Controls.ReleaseAll();
            runs.Add(track);
        }
        _game.Zone.Physics.ForceProviders.AddRange(weather);
        float diff = runs[0].Zip(runs[1], (a, b) => N.Vector3.Distance(a, b)).Max();
        Check(diff == 0f && N.Vector3.Distance(runs[0][^1], at) > 3f,
            $"determinism: the same inputs fly the same 4 s hop twice, in still air (max difference {diff} m over {runs[0].Count} ticks)");
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
        // Out of the water, if it landed on a shore: nothing that hunts on land comes in after you.
        PlanetSurface.World.PlanetWorld planet = _game.Zone.Planet;
        bool Wet(Vector3 p) => planet.Source.WaterSurfaceAt(Planets.Gen.Mathx.WrapX(p.X, planet.Width), Planets.Gen.Mathx.Wrap(p.Z, planet.Height), out _) || _game.Zone.GroundAt(p.X, p.Z).Y < 1f;
        for (int k = 0; k < 96 && Wet(clear); k++)
            clear += new Vector3(MathF.Cos(k * 0.7f), 0f, MathF.Sin(k * 0.7f)) * (2f + k * 0.5f);
        _game.Survivor.PlaceOutside(_game.Zone.GroundAt(clear.X, clear.Z).ToNumerics() + N.Vector3.UnitY * 0.3f);
        // The wildlife follows the castaway: a patch of the planet's own animals is baked around them.
        float Away() { Vector3 l = w.PatchLocal(_game.Survivor.WorldFeet); return new Vector2(l.X, l.Z).Length(); }
        for (int i = 0; i < 600 && Away() > w.Sim.Terrain.HalfSize * 0.5f; i++) yield return 0;
        w.FinishFollowing();
        foreach (int x in _drive.Wait(30)) yield return x;
        Check(w.Patches >= 2 && Away() < w.Sim.Terrain.HalfSize * 0.5f,
            $"the wildlife followed the castaway 2 km: a new patch of {w.Sim.Creatures.Count} creatures around them");
        Vector3 feet = _game.Survivor.WorldFeet;
        // On dry ground with a dry way to the castaway: creatures keep out of the sea and the rivers,
        // and the flight can end on a low shore. The way whose lowest ground is highest, out past where it can land.
        Vector3 local = w.PatchLocal(feet), toward = Vector3.Right;
        float driest = float.MinValue;
        for (int d = 0; d < 16; d++)
        {
            var dir = new Vector3(MathF.Cos(d * MathF.Tau / 16), 0f, MathF.Sin(d * MathF.Tau / 16));
            float lowest = float.MaxValue;
            for (float r = 2f; r <= 26f; r += 2f) lowest = MathF.Min(lowest, w.Sim.Terrain.Height(local.X + dir.X * r, local.Z + dir.Z * r));
            if (lowest > driest) (driest, toward) = (lowest, dir);
        }
        List<Creature> group = w.Sim.SpawnGroup(hunter, w.PatchLocal(feet + toward * 14f), hunter.Behaviour == Behaviour.PackHunter ? 3 : 1);
        foreach (Creature g in group)
        {
            g.ProvokedBy = w.Player;
            g.ProvokedAt = w.Sim.Time;
        }
        w.Frozen = false;
        double threatAt = -1, hurtAt = -1;
        double health = _game.Suit.Vitals.Health, bitten = BodyHarm();
        for (int i = 0; i < 60 * 25 && hurtAt < 0; i++)
        {
            if (threatAt < 0 && _game.Visor.ThreatShowing) threatAt = i;
            if (BodyHarm() > bitten) hurtAt = i; // the climate can wear at you all the while: only a blow counts
            yield return 0;
        }
        w.Frozen = true;
        Check(threatAt >= 0 && (hurtAt < 0 || threatAt < hurtAt),
            hurtAt < 0 ? $"a {hunter.CommonName} wound up to attack and the visor warned (THREAT) {(threatAt >= 0 ? "" : "NOT ")}â€” it never landed a bite"
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

    /// <summary>A thread of its own, so even a stuck main thread can't keep the run going for ever.</summary>
    static void StartWatchdog()
    {
        var thread = new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(WatchdogMinutes));
            Console.Error.WriteLine($"FAIL  the run took over {WatchdogMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true, Name = "SliceTest watchdog" };
        thread.Start();
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
