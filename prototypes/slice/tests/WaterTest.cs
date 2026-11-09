using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Items;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using Slice.Construction;
using Slice.Game;
using Slice.Surface;
using Slice.Vessels;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>
/// Water: the pump that stands in open water and fills its tank (or a core in its radius), the recipes that take
/// water, ice melting to it, and the first water a new player gets on a fresh start world.
///
/// Run: --headless --fixed-fps 60 --path prototypes/slice res://tests/WaterTest.tscn
/// </summary>
public partial class WaterTest : Node
{
    const int WatchdogMinutes = 15;
    Game.Game _game;
    Playthrough _play;
    Driver _drive;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();

    public override void _Ready()
    {
        var thread = new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(WatchdogMinutes));
            Console.Error.WriteLine($"FAIL  the run took over {WatchdogMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true };
        thread.Start();
        ProcessPhysicsPriority = 100;
        _run = Scenario().GetEnumerator();
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
        if (ok) _passes++;
        else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
    }

    SurfaceZone Zone => _game.Zone;
    Slice.Player.Survivor Me => _game.Survivor;

    IEnumerable<int> Scenario()
    {
        _game = new Game.Game { Interactive = false, SkipIntro = false, World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Tested) };
        AddChild(_game);
        _play = new Playthrough(_game);
        _drive = _play.Drive;
        while (_game.Phase != Phase.Playing) yield return 0;
        Zone.Weather.Bridge.Calm = true;
        Zone.Weather.Sim.Snap(WeatherKind.Clear);
        Zone.Wildlife.Frozen = true;
        foreach (int x in _drive.UntilPlaying()) yield return x;
        foreach (int x in _play.WakeAndLeave((_, _) => { })) yield return x;
        foreach (int x in _drive.ClimbOut(_game.Bunker)) yield return x;
        Recipes();
        foreach (int x in Site()) yield return x;
        foreach (int x in FirstWater()) yield return x;
        foreach (int x in Funnel()) yield return x;
    }

    /// <summary>What a recipe takes, by item.</summary>
    Bill Takes(string id)
    {
        var b = new Bill();
        foreach (var (item, n) in Zone.Data.Items.Item(id).Recipe.Inputs) b.Add(item.Id, n);
        return b;
    }

    void Recipes()
    {
        var items = Zone.Data.Items;
        Check(items.TryGet("water", out _) && items.TryGet(Device.PumpItem, out _), "water and the water pump are items");
        Check(Takes("o2-bottle")["water"] == 2 && Takes("o2-bottle")["ice"] == 0, "the O2 bottle takes water, no ice");
        Check(Takes("polymer")["water"] == 1 && Takes("polymer")["ice"] == 0, "polymer takes water, no ice");
        Check(Takes("concrete-mix")["ice"] == 0 && Takes("concrete-mix")["water"] == 0 && Takes("concrete-mix")["stone"] == 3, "concrete mix takes only stone (3: Empyrion's cement is 2 stone dust)");
        Check(Takes("water")["ice"] == 1 && items.Item("water").Recipe.Tier == 0, "ice turns into water at the suit fabricator (tier 0)");
        Check(items.Item(Device.PumpItem).Recipe.Tier == 0, "the water pump is a tier 0 recipe: made at the suit fabricator");
    }

    /// <summary>Makes things at a constructor, through the real queue, and says how long it took in game seconds.</summary>
    double Make(GalHack.Items.Production.Constructor fab, SharedStorage store, string id, int n, Bill have)
    {
        store.AddAll(have);
        var order = fab.Order(id, n);
        double t = 0;
        while (fab.Busy && t < 4000) { fab.Tick(1); t += 1; }
        Check(order.Status == GalHack.Items.Production.OrderStatus.Queued, $"order {n} {id}: {order.Status}");
        return t;
    }

    Vector3 _water, _dry;

    IEnumerable<int> Site()
    {
        GD.Print("-- where a pump may stand --");
        Vector3 near = Me.WorldFeet;
        _dry = near;
        Check(Device.PumpSite(Zone, _dry) is "NEEDS OPEN WATER", $"dry ground is no place for a pump: \"{Device.PumpSite(Zone, _dry)}\"");
        bool shallow = false, deep = false;
        for (float r = 5f; r < 600f && !(shallow && deep); r += 3f)
        for (int i = 0; i < 48; i++)
        {
            float a = i * MathF.Tau / 48f, x = near.X + MathF.Cos(a) * r, z = near.Z + MathF.Sin(a) * r;
            if (Zone.Water.IsLava || !Zone.Water.LiquidAt(x, z, out float surface, out float ground)) continue;
            float depth = surface - ground;
            var at = new Vector3(x, ground, z);
            if (!shallow && depth is > 0.4f and < 1.0f)
            {
                shallow = true;
                _water = at;
                Check(Device.PumpSite(Zone, at) == null, $"a pump may stand in open water at wading depth ({depth:F2} m, {r:F0} m from the start)");
            }
            if (!deep && depth > 3f)
            {
                deep = true;
                Check(Device.PumpSite(Zone, at) is "TOO DEEP", $"and not in deep water: \"{Device.PumpSite(Zone, at)}\"");
            }
        }
        Check(shallow, "the start has wading water within 600 m");
        yield break;
    }

    IEnumerable<int> FirstWater()
    {
        GD.Print("-- the first water of a new player --");
        var items = Zone.Data.Items;
        // The suit makes the survival constructor from bare ore; set down, it makes the pump (tier 0) from the ore chain.
        Bill leaves0 = items.Graph.Expand(new Bill().Add("survival-constructor", 1)).Leaves;
        double suit = Make(_game.Fabricator, _game.Pack.Storage, "survival-constructor", 1, leaves0);
        Check(_game.Pack["survival-constructor"] == 1, $"the suit fabricator makes the survival constructor from {items.Describe(leaves0)} in {suit:F0} s");
        _game.Pack.Storage.Remove("survival-constructor", 1);
        Device bench = Device.Place(Zone, items.Machines.Get("survival"), "survival-constructor", Me.WorldFeet + Vector3.Right * 2f, 0f);
        Bill leaves = items.Graph.Expand(new Bill().Add(Device.PumpItem, 1)).Leaves;
        GD.Print($"  a pump takes {items.Describe(leaves)} from the ground");
        double craft = Make(bench.Constructor, bench.Storage, Device.PumpItem, 1, leaves);
        Check(bench.Storage.Count(Device.PumpItem) == 1, $"the survival constructor makes the pump in {craft:F0} s");
        bench.Storage.Remove(Device.PumpItem, 1);

        Device pump = Device.PlacePump(Zone, _water, 0f);
        Check(pump.Water == 0 && pump.Storage.Count("water") == 0, "set down, the pump's tank is empty");
        pump.Tick(300);
        Check(pump.Water == 0, "with no fuel it pumps nothing");
        pump.Storage.Add("biomass", 1);
        var rules = Zone.Data.Rules.WaterPump;
        pump.Tick(rules.SecondsPerWater - 1);
        Check(pump.Water == 0, $"after {rules.SecondsPerWater - 1:F0} s of pumping the first unit is not there yet");
        pump.Tick(2);
        Check(pump.Water == 1, $"at {rules.SecondsPerWater:F0} s there is one unit of water");
        pump.Tick(rules.SecondsPerWater);
        Check(pump.Water == 2, $"two units at {2 * rules.SecondsPerWater:F0} s");
        int taken = pump.Storage.Remove("water", 2);
        Check(taken == 2, "the water comes out of the pump's tank");
        bench.Storage.Add("water", taken);
        // The bottle: 2 water and an iron bar.
        double bottle = Make(bench.Constructor, bench.Storage, "o2-bottle", 1, new Bill().Add("iron-bar", 1));
        Check(bench.Storage.Count("o2-bottle") >= 1, $"two units of pumped water make an O2 bottle in {bottle:F0} s");
        GD.Print($"  FIRST WATER: constructor {suit:F0} s, pump {craft:F0} s, one biomass in it, {2 * rules.SecondsPerWater:F0} s of pumping for two units, {bottle:F0} s to make the bottle: about {suit + craft + 2 * rules.SecondsPerWater + bottle:F0} s from ore in the bag");

        // Ice melts, and polymer and concrete craft.
        Make(bench.Constructor, bench.Storage, "water", 3, new Bill().Add("ice", 3));
        Check(bench.Storage.Count("water") >= 3, $"3 ice make water: {bench.Storage.Count("water")}");
        Make(bench.Constructor, bench.Storage, "polymer", 2, new Bill().Add("carbon-ore", 2));
        Check(bench.Storage.Count("polymer") >= 2, $"polymer crafts from water: {bench.Storage.Count("polymer")}");
        Make(bench.Constructor, bench.Storage, "concrete-mix", 2, new Bill().Add("stone", 3));
        Check(bench.Storage.Count("concrete-mix") >= 2 && bench.Storage.Count("ice") == 0, $"concrete mix crafts from stone alone: {bench.Storage.Count("concrete-mix")}");

        // The tank stops at its capacity and the burner spends nothing then.
        pump.Storage.Add("biomass", 4);
        pump.Storage.Remove("water", pump.Water);
        pump.Storage.Add("water", rules.Capacity);
        int fuel = pump.Storage.Count("biomass");
        pump.Tick(5000);
        Check(pump.Water == rules.Capacity && pump.Storage.Count("biomass") <= fuel, $"a full tank ({pump.Water} / {rules.Capacity}) holds and pumps no more");
        yield break;
    }

    IEnumerable<int> Funnel()
    {
        GD.Print("-- the funnel to a core --");
        var rules = Zone.Data.Rules.WaterPump;
        Vector3 site = _play.FlatSpot(_game.Start, 70f, 0.6f, 0.4f, span: 14f);
        foreach (int x in _drive.WalkTo(site + new Vector3(6, 0, 0), 3f, 60f)) yield return x;
        _game.Pack.Add("big-core", 1);
        foreach (int x in _play.ToToolbar("big-core", 4)) yield return x;
        foreach (int x in _play.CloseMenu()) yield return x;
        var existing = new HashSet<Vessel>(Zone.Vessels);
        foreach (int x in _play.PlaceOnGround(4, site)) yield return x;
        Vessel core = Zone.Vessels.FirstOrDefault(v => !existing.Contains(v) && v.IsBase && !v.IsRuin && !v.IsWreck);
        Check(core != null, "a base core stands for the funnel");
        if (core == null) yield break;

        if (core.Storage.CapacitySu < 100) core.Storage.CapacitySu = 100; // STAGING: a bare core holds nothing; a container block gives a base room
        Device near = Device.PlacePump(Zone, core.Body.Position.ToGodot() + new Vector3(8, 0, 0), 0f);
        Device far = Device.PlacePump(Zone, core.Body.Position.ToGodot() + new Vector3(400, 0, 0), 0f);
        Check(near.CoresInRadius().Contains(core) && far.CoresInRadius().Count == 0, $"the base is in the near pump's radius and not the far one's ({near.CoresInRadius().Count} cores, {far.CoresInRadius().Count} cores)");
        near.Storage.Add("biomass", 1);
        far.Storage.Add("biomass", 1);

        near.Tick(rules.SecondsPerWater * 2 + 1);
        Check(core.Storage.Count("water") == 0 && near.Water == 2, "with TARGET set to NONE the water stays in the pump's own tank");
        near.Storage.Remove("water", near.Water);
        near.CycleTarget();
        Check(near.Target == core && near.LiveTarget == core, "TARGET steps to the core in radius");
        near.Storage.Add("biomass", 3);
        far.Storage.Add("biomass", 3);
        int before = core.Storage.Count("water");
        near.Tick(rules.SecondsPerWater * 2 + 1);
        Check(core.Storage.Count("water") - before == 2 && near.Water == 0, $"pointed at the core, its water lands in the core's storage: +{core.Storage.Count("water") - before}, tank {near.Water}");

        double capacity = core.Storage.CapacitySu;
        core.Storage.CapacitySu = core.Storage.UsedSu; // full
        near.Tick(rules.SecondsPerWater * 2 + 1);
        Check(near.Water == 2, $"the core's storage full, the pump keeps its water in its own tank ({near.Water})");
        core.Storage.CapacitySu = capacity;

        far.Target = core;
        far.Tick(rules.SecondsPerWater * 2 + 1);
        Check(far.LiveTarget == null && far.Water == 2 && core.Storage.Count("water") == before + 2, $"out of radius the pump keeps its water ({far.Water} in its tank)");
        near.CycleTarget();
        Check(near.Target == null, "TARGET steps on to NONE");
        yield break;
    }
}
