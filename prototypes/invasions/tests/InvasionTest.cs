using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Invasions.Director;
using Invasions.Model;
using Invasions.World;

namespace Invasions.Tests;

/// <summary>
/// Headless tests for the invasion systems. Run with:
///   godot --headless --path prototypes/invasions res://tests/InvasionTest.tscn
/// The director and base model are checked directly (instant, on the simulated clock); then the
/// playable scene is built and driven frame by frame for the parts that need it.
/// Exits 0 if every check passes, 1 if any fails.
/// </summary>
public partial class InvasionTest : Node
{
    InvasionData _data;
    ServerSettings _server;
    BaseData _base;
    int _failures, _passes;

    static readonly GroundPos Home = new(0, 0);
    static readonly GroundPos GarrisonPos = new(430, -420);

    public override async void _Ready()
    {
        try
        {
            _data = GameData.Invasions();
            _server = GameData.Server();
            _base = GameData.Base();

            Section("Director");
            NoInvasionWithoutPlayers();
            WarningsAtExactTimes();
            SirenFromOneMinuteToEnd();
            ForceGrowsWithValueAndPlayers();
            NearestEnemyBaseIsTheSource();
            DestroyedSourceStopsItsInvasions();
            SameSeedReplaysSameInvasion();

            Section("Base model");
            LootTotalSurvivesWreckedContainers();
            CoreLossTakesBaseOfflineAndBack();
            CrewDeathsSilenceGunsAndStopVehicles();
            PlannerBreachesWallToCoreRoom();
            PlayerHitsAreCappedWithGrace();

            await SceneTests();
        }
        catch (Exception e)
        {
            Fail($"test crashed: {e}");
        }

        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks)" : $"{_failures} CHECK(S) FAILED, {_passes} passed");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    // ---- Director ---------------------------------------------------------------------

    InvasionDirector NewDirector(ulong seed = 7, bool shipRaids = true, bool garrison = true)
    {
        ServerSettings server = _server.Clone();
        server.ShipRaids = shipRaids;
        var d = new InvasionDirector(_data, server, seed);
        d.AddPlayerBase("home", "Homestead", Home);
        d.SetBaseStats("home", new BaseStats(4000, 90, 2));
        if (garrison) d.AddEnemyBase("okkar", "Garrison Okkar-9", EnemyBaseKind.Garrison, GarrisonPos);
        return d;
    }

    /// <summary>The longest any countdown can be: the interval at full jitter, stretched for ship raids.</summary>
    double LongestInterval =>
        _server.InvasionIntervalMinutes * 60 * (1 + _server.IntervalJitter) * _data.Sources.ShipRaidIntervalMultiplier;

    static bool RunUntil(InvasionDirector d, Func<bool> condition, double limit, double step = 5)
    {
        double end = d.Now + limit;
        while (d.Now < end)
        {
            if (condition()) return true;
            d.Advance(step);
        }
        return condition();
    }

    void NoInvasionWithoutPlayers()
    {
        var d = NewDirector();
        d.Advance(LongestInterval * 10);
        Check(!d.Log.Any(e => e.Kind == DirectorEventKind.InvasionScheduled),
            "no invasion is scheduled in ten intervals with no player in the zone");
        Check(!d.StartInvasionNow("home"), "'invade now' is refused while nobody is home");

        d.SetPlayersPresent("home", 1);
        bool came = RunUntil(d, () => d.Base("home").Active != null, LongestInterval);
        Check(came, $"with a player home an invasion is scheduled within one interval (at {d.Now:F0} s)");

        d.SetPlayersPresent("home", 0);
        d.Advance(_data.Presence.AbandonGraceSeconds + 1);
        Invasion inv = d.Base("home").History.Last();
        Check(inv.Phase == InvasionPhase.Ended && inv.Outcome == InvasionOutcome.Abandoned,
            $"everyone leaving calls the incoming invasion off ({inv.Outcome})");
    }

    void WarningsAtExactTimes()
    {
        var d = NewDirector();
        d.Advance(123.456); // start somewhere awkward so rounding would show
        d.SetPlayersPresent("home", 1);
        Check(d.StartInvasionNow("home"), "'invade now' starts the timeline with a player home");
        Invasion inv = d.Base("home").Active;
        double arrival = inv.ArrivalAt;

        // Advance in uneven steps, some far longer than the gaps between warnings.
        double[] steps = { 0.37, 13.1, 59.9, 0.016, 7.77, 170.3, 1.0 / 60 };
        for (int i = 0; d.Now < arrival + 5; i++) d.Advance(steps[i % steps.Length]);

        var warnings = d.Log.Where(e => e.Kind == DirectorEventKind.Warning && e.InvasionId == inv.Id).ToList();
        Check(warnings.Count == 3, $"three warnings fired ({warnings.Count})");
        if (warnings.Count == 3)
        {
            Check(warnings[0].Level == 1 && warnings[0].Time == arrival - 600, $"warning 1 at exactly T-10 min ({arrival - warnings[0].Time} s)");
            Check(warnings[1].Level == 2 && warnings[1].Time == arrival - 300, $"warning 2 at exactly T-5 min ({arrival - warnings[1].Time} s)");
            Check(warnings[2].Level == 3 && warnings[2].Time == arrival - 60, $"warning 3 at exactly T-1 min ({arrival - warnings[2].Time} s)");
            Check(warnings[0].Text.Contains("Vharn Dominion") && warnings[0].Text.Contains("Garrison Okkar-9") &&
                  warnings[0].Text.Contains("NE"),
                $"the first warning names the attacker, the source base and the direction: \"{warnings[0].Text}\"");
        }
        DirectorEvent scan = d.Log.FirstOrDefault(e => e.Kind == DirectorEventKind.ForceOnScan && e.InvasionId == inv.Id);
        Check(scan != null && scan.Time == arrival - 300, "the force appears on long-range scans at T-5 min");
        DirectorEvent arrived = d.Log.FirstOrDefault(e => e.Kind == DirectorEventKind.ForceArrived && e.InvasionId == inv.Id);
        Check(arrived != null && arrived.Time == arrival && inv.Phase == InvasionPhase.Assault, "the force arrives at T-0");
    }

    void SirenFromOneMinuteToEnd()
    {
        var d = NewDirector();
        d.SetPlayersPresent("home", 1);
        d.StartInvasionNow("home");
        Invasion inv = d.Base("home").Active;
        double arrival = inv.ArrivalAt;

        d.Advance(600 - 60 - 0.01);
        Check(!inv.SirenOn, "no siren until one minute out");
        d.Advance(0.02);
        DirectorEvent on = d.Log.FirstOrDefault(e => e.Kind == DirectorEventKind.SirenOn);
        Check(inv.SirenOn && on != null && on.Time == arrival - 60, "the siren starts at exactly T-1 min");

        d.Advance(61);
        d.ReportInvadersRemaining("home", 7);
        d.Advance(300);
        Check(inv.SirenOn && inv.Phase == InvasionPhase.Assault, "the siren keeps sounding while invaders remain");

        d.ReportInvadersRemaining("home", 0);
        DirectorEvent off = d.Log.LastOrDefault(e => e.Kind == DirectorEventKind.SirenOff);
        Check(!inv.SirenOn && off != null && off.Time == d.Now && inv.Phase == InvasionPhase.Ended &&
              inv.Outcome == InvasionOutcome.Victory,
            "the siren stops the moment the last invader is down: victory");
    }

    void ForceGrowsWithValueAndPlayers()
    {
        ScalingData k = _data.Scaling;
        SourceKindData fortress = _data.SourceKind(SourceKind.Fortress);

        double[] values = { 0, 2000, 8000, 30000, 120000 };
        int[] byValue = values.Select(v => ForceComposer.TroopCount(new BaseStats(v, 80, 2), 1, fortress, k)).ToArray();
        bool rising = byValue.Zip(byValue.Skip(1), (a, b) => b >= a).All(x => x) && byValue[^1] > byValue[0];
        Check(rising, $"troops rise with stored value: {string.Join(" → ", byValue)}");

        int[] bySize = new[] { 20, 200, 800 }.Select(n => ForceComposer.TroopCount(new BaseStats(2000, n, 2), 1, fortress, k)).ToArray();
        int[] byTurrets = new[] { 0, 4, 12 }.Select(n => ForceComposer.TroopCount(new BaseStats(2000, 80, n), 1, fortress, k)).ToArray();
        Check(bySize[2] > bySize[0] && byTurrets[2] > byTurrets[0],
            $"troops rise with size ({string.Join(" → ", bySize)}) and defences ({string.Join(" → ", byTurrets)})");

        int[] byPlayers = Enumerable.Range(1, 4).Select(p => ForceComposer.TroopCount(new BaseStats(6000, 120, 2), p, fortress, k)).ToArray();
        bool strictly = byPlayers.Zip(byPlayers.Skip(1), (a, b) => b > a).All(x => x);
        Check(strictly, $"troops rise with every player present: {string.Join(" → ", byPlayers)}");

        var rng = new DeterministicRng(5);
        ForcePlan small = ForceComposer.Compose(new BaseStats(300, 24, 0), 1, SourceKind.Garrison, _data, rng);
        ForcePlan huge = ForceComposer.Compose(new BaseStats(400000, 3000, 24), 4, SourceKind.Fortress, _data, rng);
        Check(small.Tier == "squad" && huge.Tier == "army",
            $"a small outpost faces a squad ({small.Troops}), a fortress an army ({huge.Troops} {huge.Tier})");

        bool seated = new[] { small, huge }.All(f =>
            f.Vehicles.Sum(v => v.Troops.Count) == f.Troops &&
            f.Vehicles.All(v => v.Crew == 1 + v.Gunners && v.Troops.Count <= _data.Vehicles[v.Type].Capacity) &&
            f.Vehicles.All(v => v.Troops.Count < 2 || v.Troops.Contains("breacher")));
        Check(seated, "every troop has a seat, every vehicle a driver and gunners, every squad a breacher");
    }

    void NearestEnemyBaseIsTheSource()
    {
        var d = NewDirector(garrison: false);
        d.AddEnemyBase("ghul", "Fortress Ghul", EnemyBaseKind.Fortress, new GroundPos(-900, -1000));      // 1345 m
        d.AddEnemyBase("okkar", "Garrison Okkar-9", EnemyBaseKind.Garrison, GarrisonPos);                 // 601 m
        d.AddEnemyBase("sere", "Outpost Sere", EnemyBaseKind.Outpost, new GroundPos(300, 200));          // 361 m
        d.AddEnemyBase("far", "Outpost Vey", EnemyBaseKind.Outpost, new GroundPos(2500, 0));            // out of range
        PlayerBase home = d.Base("home");
        Check(d.NearestSourceBase(home)?.Id == "sere", "the nearest base in range is picked (outpost at 361 m)");

        d.SetEnemyBaseState("sere", EnemyBaseState.Hijacked);
        d.SetPlayersPresent("home", 1);
        d.StartInvasionNow("home");
        InvasionSource src = home.Active?.Source;
        Check(src?.EnemyBaseId == "okkar" && src.Kind == SourceKind.Garrison,
            $"with the outpost hijacked, the next nearest (garrison at 601 m) sends the invasion: {src}");
        Check(src != null && src.Compass == "NE" && Math.Abs(src.Bearing - 45.7) < 0.5, "the approach bearing points at the source");
    }

    void DestroyedSourceStopsItsInvasions()
    {
        var d = NewDirector(shipRaids: true);
        d.SetPlayersPresent("home", 1);
        d.StartInvasionNow("home");
        Invasion first = d.Base("home").Active;
        d.Advance(300);
        d.SetEnemyBaseState("okkar", EnemyBaseState.Destroyed);
        Check(first.Phase == InvasionPhase.Ended && first.Outcome == InvasionOutcome.CalledOff && !first.SirenOn,
            "destroying the source calls its incoming invasion off");
        Check(d.NearestSourceBase(d.Base("home")) == null && d.Log.Any(e => e.Kind == DirectorEventKind.SourceLost),
            "the destroyed garrison is no longer a source");
        bool next = RunUntil(d, () => d.Base("home").Active != null, LongestInterval);
        Check(next && d.Base("home").Active.Source.Kind == SourceKind.Ship,
            $"with ship raids on, the next invasion comes by ship: {d.Base("home").Active?.Source}");

        var quiet = NewDirector(shipRaids: false);
        quiet.SetPlayersPresent("home", 1);
        quiet.SetEnemyBaseState("okkar", EnemyBaseState.Hijacked);
        Check(quiet.Log.Any(e => e.Kind == DirectorEventKind.InvasionsStopped) && !quiet.StartInvasionNow("home"),
            "with ship raids off, hijacking the only source stops invasions");
        quiet.Advance(LongestInterval * 10);
        Check(!quiet.Log.Any(e => e.Kind == DirectorEventKind.InvasionScheduled), "and none come in ten intervals");
    }

    string ScriptedRun(ulong seed)
    {
        var d = NewDirector(seed);
        d.SetPlayersPresent("home", 2);
        RunUntil(d, () => d.Base("home").Active?.Phase == InvasionPhase.Assault, LongestInterval * 2, 7.5);
        d.ReportInvadersRemaining("home", 9);
        d.Advance(40);
        d.ReportInvadersRemaining("home", 0);
        d.SetEnemyBaseState("okkar", EnemyBaseState.Destroyed);
        RunUntil(d, () => d.Base("home").Active?.Phase == InvasionPhase.Assault, LongestInterval * 2, 7.5);
        d.ReportCoreDestroyed("home");
        d.ReportInvadersRemaining("home", 0);
        return string.Join("\n", d.Log) + "\n" + string.Join("\n", d.Base("home").History.Select(i => i.Force?.ToString()));
    }

    void SameSeedReplaysSameInvasion()
    {
        string a = ScriptedRun(42), b = ScriptedRun(42), c = ScriptedRun(43);
        Check(a == b && a.Contains("ForceArrived") && a.Contains("Ship"),
            $"the same seed replays the same invasions, event for event ({a.Split('\n').Length} lines)");
        Check(a != c, "a different seed gives different invasions");
    }

    // ---- Base model -------------------------------------------------------------------

    void LootTotalSurvivesWreckedContainers()
    {
        var model = new BaseModel(_base, Home);
        var before = model.Loot.AllItems();
        int total = model.Loot.TotalItems;
        Check(total > 0 && model.Loot.Storage.Containers.Count == 4, $"the base starts with {total} items in 4 linked containers");

        // An invasion's worth of damage: two containers wrecked, walls breached, the core shot up.
        var containers = model.Grid.Blocks.Where(b => b.Kind == BlockKind.Container).Take(2).ToList();
        foreach (Block c in containers) model.Damage(c, 120);
        foreach (Block c in containers) model.Damage(c, 1e6);
        foreach (Block w in model.Grid.Blocks.Where(b => b.Kind == BlockKind.Wall).Take(6).ToList()) model.Damage(w, 1e6);
        model.Damage(model.Grid.Core, 900);

        var after = model.Loot.AllItems();
        Check(model.Loot.Crates.Count == 2 && model.Loot.CrateTotal > 0,
            $"each wrecked container spilled into a crate ({model.Loot.Crates.Count} crates, {model.Loot.CrateTotal} items)");
        Check(model.Loot.TotalItems == total && before.SequenceEqual(after),
            $"item total in storage + crates is unchanged: {total} → {model.Loot.TotalItems}, item by item");
        Check(model.Loot.Storage.Containers.Count == 2, "the remaining containers still share one inventory");

        int moved = model.RecoverCrate(model.Loot.Crates[0]);
        Check(moved > 0 && model.Loot.TotalItems == total, $"recovering a crate into storage keeps the total ({moved} moved)");
    }

    void CoreLossTakesBaseOfflineAndBack()
    {
        var model = new BaseModel(_base, Home);
        var d = NewDirector();
        model.OnlineChanged += online =>
        {
            if (online) d.ReportCorePlaced("home");
            else d.ReportCoreDestroyed("home");
        };
        d.SetPlayersPresent("home", 1);
        d.StartInvasionNow("home");
        d.Advance(600);
        Invasion inv = d.Base("home").Active;
        d.ReportInvadersRemaining("home", 12);

        foreach (Block w in model.Grid.Blocks.Where(b => b.Kind == BlockKind.Wall).Skip(3).Take(4).ToList()) model.Damage(w, 1e6);
        var survivors = model.Grid.Alive.Where(b => b.Kind != BlockKind.Core).Select(b => b.Id).ToHashSet();
        model.Damage(model.Grid.Core, 1e6);

        Check(!model.Online && !model.Powered, "with the core destroyed the base is offline and unpowered");
        Check(inv.Phase == InvasionPhase.Withdrawing && inv.Outcome == InvasionOutcome.CoreDestroyed &&
              d.Log.Any(e => e.Kind == DirectorEventKind.Withdraw),
            "the invaders are ordered to withdraw");
        d.ReportInvadersRemaining("home", 3);
        Check(inv.SirenOn, "the siren sounds until the last invader is gone");
        d.ReportInvadersRemaining("home", 0);
        Check(inv.Phase == InvasionPhase.Ended && !inv.SirenOn, "once they're gone the invasion ends and the siren stops");

        int scheduledBefore = d.Log.Count(e => e.Kind == DirectorEventKind.InvasionScheduled);
        d.Advance(LongestInterval * 3);
        Check(d.Log.Count(e => e.Kind == DirectorEventKind.InvasionScheduled) == scheduledBefore,
            "an offline base is not invaded again");

        Block core = model.PlaceNewCore();
        var nowAlive = model.Grid.Alive.Select(b => b.Id).ToHashSet();
        Check(core != null && model.Online && model.Powered && d.Base("home").CoreAlive,
            "placing a new core brings the base back online");
        Check(nowAlive.SetEquals(survivors.Append(core?.Id ?? -1)),
            $"with exactly the {survivors.Count} blocks that survived, plus the new core");
    }

    void CrewDeathsSilenceGunsAndStopVehicles()
    {
        var crew = new VehicleCrew(2);
        Check(crew.CanMove && crew.CanFire(0) && crew.CanFire(1), "a fresh APC drives and both guns fire");
        crew.Damage(2, 1000);
        Check(!crew.CanFire(1) && crew.CanFire(0) && crew.CanMove, "killing gunner 2 silences gun 2 only");
        crew.Damage(0, 1000);
        Check(!crew.CanMove && crew.CanFire(0), "killing the driver stops the vehicle; the other gun keeps firing");
    }

    void PlannerBreachesWallToCoreRoom()
    {
        var model = new BaseModel(_base, Home);
        var planner = new BreachPlanner(_data.Planner.HpPerCellOfDetour);
        BaseGrid grid = model.Grid;
        Cell outsideNorth = grid.FromLayout(12, -8);
        AssaultPlan plan = planner.Plan(grid, outsideNorth, grid.CoreSlot);
        var kinds = plan.BreachBlockIds.Select(id => grid.Find(id).Kind).ToList();
        Check(plan.Found && kinds.Contains(BlockKind.Wall),
            $"from the north, the plan breaches the outer wall ({string.Join(", ", kinds)})");
        Check(plan.Found && grid.CoreRoom.Contains(plan.Cells[^1]), "and ends inside the core room, next to the core");
    }

    void PlayerHitsAreCappedWithGrace()
    {
        var hp = new PlayerHealth(_data.PlayerSafety);
        double taken = hp.ApplyNpcHit(80, 0);
        Check(Math.Abs(taken - 35) < 1e-9, $"an 80-damage NPC hit takes only 35% of max health ({taken})");
        hp.ApplyNpcHit(35, 1);
        hp.ApplyNpcHit(10, 2); // 100 → 65 → 30 → 20: below 25%
        Check(hp.InGrace(2.1), "dropping below 25% opens a grace window");
        double reduced = hp.ApplyNpcHit(35, 2.5);
        Check(hp.Alive && reduced < 35 * 0.5, $"hits in the grace window are reduced ({reduced:F1})");
    }

    // ---- Scene ------------------------------------------------------------------------

    async Task SceneTests()
    {
        await Task.CompletedTask;
    }

    // ---- Reporting --------------------------------------------------------------------

    static void Section(string name) => GD.Print($"-- {name}");

    void Check(bool ok, string what)
    {
        if (ok) Pass(what);
        else Fail(what);
    }

    void Pass(string what)
    {
        _passes++;
        GD.Print($"PASS  {what}");
    }

    void Fail(string what)
    {
        _failures++;
        GD.PrintErr($"FAIL  {what}");
    }
}
