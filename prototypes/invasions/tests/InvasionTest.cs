using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Invasions.Combat;
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
    FireControlData _fire;
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
            _fire = GameData.FireControl();
            // --only-soak skips straight to the navigation soak (for tuning it).
            bool onlySoak = OS.GetCmdlineUserArgs().Contains("--only-soak");
            if (!onlySoak) DirectorAndModelTests();
            await SceneTests(onlySoak);
        }
        catch (Exception e)
        {
            Fail($"test crashed: {e}");
        }

        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks)" : $"{_failures} CHECK(S) FAILED, {_passes} passed");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    void DirectorAndModelTests()
    {
        {
            Section("Director");
            NoInvasionWithoutPlayers();
            WarningsAtExactTimes();
            SirenFromOneMinuteToEnd();
            ForceGrowsWithValueAndPlayers();
            NearestEnemyBaseIsTheSource();
            DestroyedSourceStopsItsInvasions();
            SameSeedReplaysSameInvasion();

            Section("Factions");
            FactionDataIsComplete();
            NearestHostileFactionSendsTheInvasion();
            ExchangeAndFreeholdersNeverInvade();
            FactionTurnedFriendlyStopsInvading();
            StandingRespectsEachFactionsNature();
            ShipRaidsComeFromHostileFactions();
            EachFactionComposesItsOwnForce();
            KessariPlanStraightThroughWalls();

            Section("Base model");
            LootTotalSurvivesWreckedContainers();
            CoreLossTakesBaseOfflineAndBack();
            CrewDeathsSilenceGunsAndStopVehicles();
            PlannerBreachesWallToCoreRoom();

            Section("Siren");
            SirenWailsThenSoundsAllClear();

            Section("Fire control");
            FirstShotsAtPlayerAlwaysMiss();
            AccuracyReachesBaselineByTwoSeconds();
            LineOfSightBreakResetsTheRamp();
            BaselineComesFromShooterTargetAndWeapon();
            DamageIsNeverScaled();
        }
    }

    // ---- Director ---------------------------------------------------------------------

    InvasionDirector NewDirector(ulong seed = 7, bool shipRaids = true, bool garrison = true)
    {
        ServerSettings server = _server.Clone();
        server.ShipRaids = shipRaids;
        var d = new InvasionDirector(_data, server, seed);
        d.AddPlayerBase("home", "Homestead", Home);
        d.SetBaseStats("home", new BaseStats(4000, 90, 2));
        if (garrison) d.AddEnemyBase("okkar", "Garrison Okkar-9", "vharn", EnemyBaseKind.Garrison, GarrisonPos);
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
        FactionData vharn = _data.Faction("vharn");
        ForcePlan small = ForceComposer.Compose(new BaseStats(300, 24, 0), 1, SourceKind.Garrison, vharn, _data, rng);
        ForcePlan huge = ForceComposer.Compose(new BaseStats(400000, 3000, 24), 4, SourceKind.Fortress, vharn, _data, rng);
        Check(small.Tier == "squad" && huge.Tier == "army",
            $"a small outpost faces a squad ({small.Troops}), a fortress an army ({huge.Troops} {huge.Tier})");

        bool seated = new[] { small, huge }.All(f =>
            f.Vehicles.Sum(v => v.Troops.Count) == f.Troops &&
            f.Vehicles.All(v => v.Crew == 1 + v.Gunners && v.Troops.Count <= vharn.Vehicles[v.Type].Capacity) &&
            f.Vehicles.All(v => v.Troops.Count < 2 || v.Troops.Contains("breacher")));
        Check(seated, "every troop has a seat, every vehicle a driver and gunners, every squad a breacher");
    }

    void NearestEnemyBaseIsTheSource()
    {
        var d = NewDirector(garrison: false);
        d.AddEnemyBase("ghul", "Fortress Ghul", "vharn", EnemyBaseKind.Fortress, new GroundPos(-900, -1000));      // 1345 m
        d.AddEnemyBase("okkar", "Garrison Okkar-9", "vharn", EnemyBaseKind.Garrison, GarrisonPos);                 // 601 m
        d.AddEnemyBase("sere", "Outpost Sere", "vharn", EnemyBaseKind.Outpost, new GroundPos(300, 200));          // 361 m
        d.AddEnemyBase("far", "Outpost Vey", "vharn", EnemyBaseKind.Outpost, new GroundPos(2500, 0));            // out of range
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

    // ---- Fire control -----------------------------------------------------------------

    FireControl Gun(string weapon) => new(_fire, _fire.Weapon(weapon));

    /// <summary>Fires a weapon at its own rate from <paramref name="start"/> and returns each shot's solution.</summary>
    List<ShotSolution> Burst(FireControl fc, object target, TargetClass cls, bool isPlayer, int shots, double start = 0)
    {
        var list = new List<ShotSolution>();
        for (int i = 0; i < shots; i++) list.Add(fc.NextShot(target, cls, isPlayer, start + i * fc.Weapon.Interval));
        return list;
    }

    void FirstShotsAtPlayerAlwaysMiss()
    {
        int n = _fire.Warning.Shots;
        var player = new object();
        var rifle = Burst(Gun("rifle"), player, TargetClass.OnFoot, true, 10);
        Check(rifle.Take(n).All(x => x.Warning) && !rifle.Skip(n).Any(x => x.Warning),
            $"a rifle's first {n} shots at a player are warning shots, then it aims ({string.Join("", rifle.Select(x => x.Warning ? 'W' : 'a'))})");
        var minigun = Burst(Gun("minigun"), player, TargetClass.OnFoot, true, 12);
        int warned = minigun.TakeWhile(x => x.Warning).Count();
        Check(warned >= n && warned * _fire.Weapon("minigun").Interval >= _fire.Warning.Seconds && !minigun.Skip(warned).Any(x => x.Warning),
            $"a minigun keeps warning for the first {_fire.Warning.Seconds} s ({warned} shots)");
        var turretVsTroop = Burst(Gun("base_cannon"), new object(), TargetClass.OnFoot, false, 3);
        Check(turretVsTroop.All(x => !x.Warning), "fire at non-players gets no warning shots");
    }

    void AccuracyReachesBaselineByTwoSeconds()
    {
        var fc = Gun("rifle");
        var player = new object();
        double baseline = _fire.BaselineSpread(fc.Weapon, TargetClass.OnFoot);
        var shots = Burst(fc, player, TargetClass.OnFoot, true, 4); // the 4th is the first aimed shot
        double warningEnded = 3 * fc.Weapon.Interval;
        double first = shots[3].SpreadDegrees;
        double half = fc.NextShot(player, TargetClass.OnFoot, true, warningEnded + _fire.Ramp.Seconds / 2).SpreadDegrees;
        double settled = fc.NextShot(player, TargetClass.OnFoot, true, warningEnded + _fire.Ramp.Seconds).SpreadDegrees;
        Check(Math.Abs(first - _fire.Ramp.StartSpreadDegrees) < 1e-9 && half < first && half > baseline && Math.Abs(settled - baseline) < 1e-9,
            $"spread ramps {first:F2}° → {half:F2}° → baseline {settled:F2}° over {_fire.Ramp.Seconds} s after the warning");
    }

    void LineOfSightBreakResetsTheRamp()
    {
        var fc = Gun("rifle");
        var player = new object();
        Burst(fc, player, TargetClass.OnFoot, true, 12);
        double settled = fc.NextShot(player, TargetClass.OnFoot, true, 20).SpreadDegrees;
        fc.LostSight();
        var again = Burst(fc, player, TargetClass.OnFoot, true, 4, start: 25);
        Check(Math.Abs(settled - _fire.BaselineSpread(fc.Weapon, TargetClass.OnFoot)) < 1e-9 && again.Take(_fire.Warning.Shots).All(x => x.Warning) &&
              Math.Abs(again[3].SpreadDegrees - _fire.Ramp.StartSpreadDegrees) < 1e-9,
            "breaking line of sight resets it: warning shots again, then the ramp from wide");
    }

    void BaselineComesFromShooterTargetAndWeapon()
    {
        double G(string grade) => _fire.Grades[grade];
        double B(string weapon, TargetClass t) => _fire.BaselineSpread(_fire.Weapon(weapon), t);
        Check(B("rifle", TargetClass.OnFoot) == G("good") && B("rifle", TargetClass.Hover) == G("fair") &&
              B("rifle", TargetClass.SmallVessel) == G("poor") && B("rifle", TargetClass.Capital) == G("very_poor"),
            "infantry small arms: good on foot, fair hover, poor small vessel, very poor capital");
        Check(B("autocannon", TargetClass.OnFoot) == G("fair") && B("autocannon", TargetClass.Hover) == G("good") &&
              B("autocannon", TargetClass.SmallVessel) == G("good") && B("autocannon", TargetClass.Capital) == G("good"),
            "hover and small-vessel guns: fair on foot, good against everything else");
        Check(B("base_cannon", TargetClass.OnFoot) == G("very_poor") && B("base_cannon", TargetClass.Hover) == G("poor") &&
              B("base_cannon", TargetClass.SmallVessel) == G("fair") && B("base_cannon", TargetClass.Capital) == G("very_good"),
            "capital and base turrets: very poor on foot, poor hover, fair small vessel, very good capital");
        Check(B("minigun", TargetClass.OnFoot) > B("rifle", TargetClass.OnFoot) && B("rail", TargetClass.Hover) < B("autocannon", TargetClass.Hover) &&
              B("laser", TargetClass.Hover) < B("autocannon", TargetClass.Hover) &&
              B("artillery", TargetClass.Capital) < B("artillery", TargetClass.OnFoot) / 3,
            "minigun sprays, rail and laser are precise, artillery is only good against big targets");
        WeaponData rocket = _fire.Weapon("rocket_pod"), capital = _fire.Weapon("capital_gun"), cannon = _fire.Weapon("autocannon");
        Check(rocket.TurnRateDegrees > 0 && rocket.TurnRateDegrees < 45 && capital.ProjectileSpeed is >= 250 and <= 350 &&
              Math.Abs(cannon.ProjectileSpeed - 600) < 1 && _fire.Weapon("base_cannon").ProjectileSpeed < cannon.ProjectileSpeed,
            $"rockets track slowly ({rocket.TurnRateDegrees}°/s); capital rounds fly {capital.ProjectileSpeed} m/s against {cannon.ProjectileSpeed} m/s for small-craft guns");
    }

    void SirenWailsThenSoundsAllClear()
    {
        var synth = new Audio.SirenSynth(8000);
        var l = new float[80];
        var r = new float[80];
        var speeds = new List<double>();
        synth.Alert();
        for (int i = 0; i < 1500; i++) // 15 s in 10 ms steps
        {
            synth.Render(l, r);
            speeds.Add(synth.RotorSpeed);
        }
        var wail = speeds.Skip(500).ToList(); // after the first wind-up
        int turns = wail.Zip(wail.Skip(1), (a, b) => b > a).Zip(wail.Skip(1).Zip(wail.Skip(2), (a, b) => b > a), (x, y) => x != y).Count(c => c);
        Check(wail.Max() > 0.9 && wail.Min() > 0.3 && wail.Min() < 0.6 && turns >= 3 && turns <= 6,
            $"the alert winds up and down: rotor {wail.Min():F2}–{wail.Max():F2} of full speed, {turns} turns in 10 s (about 4.6 s a cycle)");

        synth.AllClear(6);
        var steady = new List<double>();
        for (int i = 0; i < 1800; i++)
        {
            synth.Render(l, r);
            if (i is > 600 and < 750) steady.Add(synth.RotorSpeed); // well into the held note
        }
        Check(steady.Max() - steady.Min() < 0.02 && steady.Min() > 0.95 && synth.Current == Audio.SirenSynth.Mode.Off && synth.RotorSpeed < 0.02,
            $"the all-clear holds one steady note, then the rotor runs down to silence ({synth.RotorSpeed:F3})");
    }

    void DamageIsNeverScaled()
    {
        var hp = new PlayerHealth();
        double taken = hp.TakeHit(80);
        Check(taken == 80 && hp.Current == 20, $"an 80-damage hit takes 80 ({taken}), with no cap");
        double low = hp.TakeHit(15);
        Check(low == 15 && hp.Current == 5, "low health gets no grace: 15 more takes 15");
        var fresh = new PlayerHealth();
        fresh.TakeHit(_fire.Weapon("capital_gun").Damage);
        Check(!fresh.Alive, "a capital gun round vaporises a player on foot");
    }

    // ---- Scene ------------------------------------------------------------------------

    Main _world;

    async Task SceneTests(bool onlySoak)
    {
        // Run the scene faster than real time: same 1/60 s physics steps, as many per second as the CPU allows.
        Engine.PhysicsTicksPerSecond = 60 * 24;
        Engine.TimeScale = 24;
        Engine.MaxPhysicsStepsPerFrame = 96;
        try
        {
            if (!onlySoak) await SceneAndFactionTests();

            Section("Navigation soak");
            int runs = 50;
            string only = null;
            foreach (string arg in OS.GetCmdlineUserArgs())
            {
                if (arg.StartsWith("--soak=")) runs = int.Parse(arg["--soak=".Length..]);
                else if (arg.StartsWith("--soak-faction=")) only = arg["--soak-faction=".Length..];
                else if (arg.StartsWith("--soak-first=")) _soakFirst = int.Parse(arg["--soak-first=".Length..]);
            }
            foreach (string faction in InvadingFactions.Where(f => only == null || f == only)) await Soak(runs, faction);
        }
        finally
        {
            Engine.TimeScale = 1;
            Engine.PhysicsTicksPerSecond = 60;
            if (_world != null) _world.QueueFree();
            _world = null;
            await PhysicsFrames(3); // let the last world go before quitting
        }
    }

    async Task SceneAndFactionTests()
    {
        Section("Scene");
        await CrouchingShrinksTheCapsule();
        await CrewDeathsInALiveApc();
        await InvadersBreachIntoTheCoreRoom();
        await WarningRoundsNeverHitAndRoundsTakeTime();
        await FullInvasionInFastTime();
        await DestroyingTheGarrisonStopsItsInvasions();

        Section("Faction rosters and tactics");
        foreach (string faction in InvadingFactions) await FactionRosterAndTactics(faction);
    }

    async Task<Main> NewWorld()
    {
        if (_world != null)
        {
            _world.QueueFree();
            await PhysicsFrames(2);
        }
        _world = new Main();
        AddChild(_world);
        await PhysicsFrames(2);
        return _world;
    }

    async Task PhysicsFrames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    async Task Run(double seconds) => await PhysicsFrames((int)Math.Round(seconds * 60));

    async Task<bool> RunUntil(Func<bool> condition, double maxSeconds)
    {
        for (double t = 0; t < maxSeconds; t += 0.25)
        {
            if (condition()) return true;
            await PhysicsFrames(15);
        }
        return condition();
    }

    async Task CrouchingShrinksTheCapsule()
    {
        Main m = await NewWorld();
        Player p = m.Player;
        float stand = p.CapsuleHeight, standEye = p.EyeHeight;
        Input.ActionPress(Controls.Crouch);
        await Run(0.5);
        float crouch = p.CapsuleHeight, crouchEye = p.EyeHeight;
        Input.ActionRelease(Controls.Crouch);
        await Run(0.5);
        Check(crouch < stand - 0.5f && crouchEye < standEye - 0.4f && Math.Abs(p.CapsuleHeight - stand) < 1e-4f,
            $"holding Ctrl shrinks the capsule {stand:F2} → {crouch:F2} m and lowers the eye; releasing stands back up");
    }

    async Task CrewDeathsInALiveApc()
    {
        Main m = await NewWorld();
        m.Combat.TurretsEnabled = false;
        m.Player.Invulnerable = true;
        m.Player.Teleport(new Vector3(0, 0.1f, -45f), 0f, 0f); // outside the north wall, in the open
        var plan = new VehiclePlan { Index = 0, Faction = "vharn", Type = "apc", Body = "apc", Label = "APC", Speed = 17, Guns = new List<string> { "autocannon", "rocket_pod" } };
        var at = new Vector3(0, 0, -120f);
        var route = new VehicleRoute(at, at, m.Combat.BaseCentre, Vector3.Forward, 520f);
        var apc = (CrewedVehicle)m.Controller.Factory.SpawnVehicle(plan, route);
        await Run(5);
        int[] before = apc.ShotsFired.ToArray();
        Check(before[0] > 0 && before[1] > 0, $"a crewed APC's guns both fire at a player in the open ({before[0]} cannon, {before[1]} rockets)");

        apc.Crew.Damage(1, 1000); // the cannon's gunner
        await Run(0.5);
        int[] silenced = apc.ShotsFired.ToArray();
        await Run(5);
        int[] after = apc.ShotsFired.ToArray();
        Check(after[0] == silenced[0] && after[1] > silenced[1],
            $"killing the cannon's gunner silences the cannon ({silenced[0]} → {after[0]}) while the rockets keep firing ({silenced[1]} → {after[1]})");

        apc.Depart();
        await Run(3);
        float moving = apc.Speed;
        apc.Crew.Damage(0, 1000); // the driver
        await Run(3);
        Vector3 stopped = apc.GlobalPosition;
        await Run(1);
        Check(moving > 2f && apc.Speed < 0.01f && apc.GlobalPosition.DistanceTo(stopped) < 0.01f && apc.IsStranded,
            $"killing the driver stops the APC ({moving:F1} m/s → {apc.Speed:F2} m/s)");

        // Troops aboard a carrier whose driver is killed on the way in climb out of the wreck and fight on.
        var loaded = new VehiclePlan { Index = 1, Faction = "vharn", Type = "apc", Body = "apc", Label = "APC", Speed = 17, Guns = new List<string> { "autocannon", "rocket_pod" } };
        loaded.Troops.AddRange(new[] { "breacher", "rifleman", "rifleman", "heavy" });
        var far = new Vector3(60f, 0, -400f);
        var carrier = (CrewedVehicle)m.Controller.Factory.SpawnVehicle(loaded, new VehicleRoute(far, new Vector3(60f, 0, -150f), m.Combat.BaseCentre, Vector3.Forward, 520f));
        int out0 = 0;
        carrier.TroopDismounted += (_, _, _) => out0++;
        await Run(3);
        carrier.Crew.Damage(0, 1000);
        bool emptied = await RunUntil(() => carrier.TroopsAboard == 0, 20);
        Check(emptied && out0 == 4 && carrier.IsStranded,
            $"troops aboard a carrier whose driver dies climb out of the wreck ({out0} of 4) instead of sitting in it forever");
    }

    async Task InvadersBreachIntoTheCoreRoom()
    {
        Main m = await NewWorld();
        m.Combat.TurretsEnabled = false;
        m.Player.Invulnerable = true;
        m.Player.Teleport(new Vector3(0, 0.1f, 260f), 0f, 0f);
        BaseGrid grid = m.Model.Grid;
        var squad = new List<PlaceholderSoldier>();
        string[] roles = { "breacher", "rifleman", "rifleman" };
        for (int i = 0; i < roles.Length; i++)
        {
            Vector3 at = m.Combat.CellCentre(grid.FromLayout(10 + i, -10)) + Vector3.Up * 0.1f;
            var troop = (PlaceholderSoldier)m.Controller.Factory.SpawnTroop("vharn", roles[i], at, Vector3.Back);
            troop.Assault();
            squad.Add(troop);
        }
        int wallsBefore = grid.Alive.Count(b => b.Kind == BlockKind.Wall);
        bool inside = await RunUntil(() => squad.Any(t => IsInstanceValid(t) && t.InCoreRoom), 120);
        int wallsBroken = wallsBefore - grid.Alive.Count(b => b.Kind == BlockKind.Wall);
        Check(inside && wallsBroken > 0,
            $"a squad outside the north wall blasts through {wallsBroken} wall block(s) and reaches the core room");
    }

    async Task WarningRoundsNeverHitAndRoundsTakeTime()
    {
        Main m = await NewWorld();
        m.Combat.TurretsEnabled = false;
        Player p = m.Player;
        p.Teleport(new Vector3(0, 0.1f, 70f), 0f, 0f); // south of the base, in the open
        double firstHurt = double.NaN;
        p.Hurt += _ => { if (double.IsNaN(firstHurt)) firstHurt = m.Combat.Time; };
        var shooter = (PlaceholderSoldier)m.Controller.Factory.SpawnTroop("vharn", "rifleman", new Vector3(4f, 0.1f, 100f), Vector3.Forward);
        shooter.Assault();
        int warnings = _fire.Warning.Shots;
        double firstAimed = double.NaN;
        for (int i = 0; i < 400 && (double.IsNaN(firstAimed) || m.Combat.Time < firstAimed + 1.0); i++)
        {
            await PhysicsFrames(1);
            if (double.IsNaN(firstAimed) && m.Combat.RoundsFired > warnings) firstAimed = m.Combat.Time;
        }
        Check(m.Combat.WarningRoundsFired >= warnings && m.Combat.WarningRoundsThatHitTheirTarget == 0 &&
              (double.IsNaN(firstHurt) || firstHurt >= firstAimed),
            $"a soldier's first {warnings} rounds at the player all miss: {m.Combat.WarningRoundsFired} warning rounds, " +
            $"{m.Combat.WarningRoundsThatHitTheirTarget} hits, first wound {(double.IsNaN(firstHurt) ? "none" : $"{firstHurt - firstAimed:+0.00;-0.00} s after the first aimed shot")}");

        // A capital gun round from 150 m: it takes its time, then it is not softened at all.
        Main w = await NewWorld();
        Player q = w.Player;
        q.Teleport(new Vector3(0, 0.1f, 60f), 0f, 0f);
        WeaponData capital = _fire.Weapon("capital_gun");
        Vector3 from = q.AimPoint + new Vector3(0, 0, 150f);
        w.Combat.Fire(from, q.AimPoint, capital, 0f, Layers.InvaderShots, Team.Invaders, Palette.TracerVharn);
        double fired = w.Combat.Time, expected = 150.0 / capital.ProjectileSpeed;
        await PhysicsFrames(2);
        bool aliveAfterLaunch = q.Health.Alive;
        for (int i = 0; i < 180 && q.Health.Alive; i++) await PhysicsFrames(1);
        double flight = w.Combat.Time - fired;
        Check(aliveAfterLaunch && !q.Health.Alive && Math.Abs(flight - expected) < 0.06 && q.Health.Current == 0,
            $"a capital round flies {capital.ProjectileSpeed} m/s (150 m in {flight:F2} s, expected {expected:F2}) and vaporises a player on foot");
    }

    async Task FullInvasionInFastTime()
    {
        Main m = await NewWorld();
        m.Player.Invulnerable = true;
        m.Director.FastTime = true;
        var items = m.Model.Loot.AllItems();
        int total = m.Model.Loot.TotalItems;

        Check(m.Controller.InvadeNow() == "", "I starts an invasion with the player home");
        bool arrived = await RunUntil(() => m.Controller.Current?.Phase == InvasionPhase.Assault, 30);
        var warnings = m.Controller.Heard.Where(e => e.Kind == DirectorEventKind.Warning).Select(e => e.Level).ToList();
        Check(arrived && warnings.SequenceEqual(new[] { 1, 2, 3 }) && m.Base.Sirens.On && m.Base.Sirens.AudioMode == Audio.SirenSynth.Mode.Alert,
            $"in fast time the three warnings come in order and the siren wails ({string.Join(",", warnings)})");
        await Run(30);
        Check(m.Base.Consoles.Count > 0 && m.Base.Consoles.All(c => c.Text.Contains("UNDER ATTACK")),
            "the console screens show the attack");
        Check(m.Combat.Vehicles.Count > 0 && m.Combat.Troops.Any(t => t.IsAlive), $"crewed vehicles arrive and set {m.Combat.Troops.Count} troops down");

        // An invasion that wrecks containers: everything they held lands in crates that stay.
        foreach (Block c in m.Model.Grid.Alive.Where(b => b.Kind == BlockKind.Container).Take(2).ToList()) m.Model.Damage(c, 1e6);
        await Run(1);
        int inCrates = m.Base.CrateNodes.Sum(n => n.Crate.Total);
        Check(m.Base.CrateNodes.Count() == 2 && m.Model.Loot.Storage.Total + inCrates == total && m.Model.Loot.AllItems().SequenceEqual(items),
            $"two wrecked containers leave two spill crates; storage {m.Model.Loot.Storage.Total} + crates {inCrates} = {total}, item for item");

        var survivors = m.Model.Grid.Alive.Where(b => b.Kind != BlockKind.Core).Select(b => b.Id).ToHashSet();
        m.Model.Damage(m.Model.Grid.Core, 1e6);
        await Run(0.25);
        var onFoot = m.Combat.Troops.Where(t => t.IsAlive && !t.IsGone).ToList();
        Check(!m.Model.Online && m.Base.Turrets.All(t => !t.Powered) && m.Controller.Current?.Phase == InvasionPhase.Withdrawing &&
              onFoot.All(t => t.State == TroopState.Withdrawing),
            $"with the core down the base is offline, the turrets are dead and all {onFoot.Count} troops on foot withdraw");
        Check(m.Base.Sirens.On, "the siren keeps sounding while they withdraw");

        bool ended = await RunUntil(() => m.Controller.Current == null, 240);
        Invasion last = m.Director.Base(InvasionController.HomeId).History.Last();
        Check(ended && last.Outcome == InvasionOutcome.CoreDestroyed && !m.Base.Sirens.On && m.Base.Sirens.AudioMode == Audio.SirenSynth.Mode.AllClear,
            "once the last invader is gone the invasion ends, the alert stops and the all-clear sounds");
        Check(m.Model.Loot.TotalItems == total && m.Base.CrateNodes.Count() == 2, "after the invasion every item is still there, crates included");

        Block core = m.Model.PlaceNewCore();
        await Run(0.25);
        var alive = m.Model.Grid.Alive.Select(b => b.Id).ToHashSet();
        Check(core != null && m.Model.Online && m.Director.Base(InvasionController.HomeId).CoreAlive && alive.SetEquals(survivors.Append(core.Id)),
            $"P places a new core: the base is back online with the {survivors.Count} blocks that survived");
    }

    async Task DestroyingTheGarrisonStopsItsInvasions()
    {
        Main m = await NewWorld();
        PlayerBase home = m.Director.Base(InvasionController.HomeId);
        Check(m.Director.NearestSourceBase(home)?.Id == m.Garrison.Id, "the Vharn garrison 600 m north-east, the nearest hostile base, is the source");
        Check(m.Controller.KnockOutSource() == "", "K knocks out the current source");
        await Run(0.25);
        Check(m.Garrison.Destroyed && m.Director.Enemy(m.Garrison.Id).State == EnemyBaseState.Destroyed &&
              m.Director.NearestSourceBase(home)?.Faction == "reavers",
            $"the garrison is down, so the next nearest hostile base takes over: {m.Controller.SourceSummary()}");
        while (m.Director.NearestSourceBase(home) is EnemyBase next) m.Controller.Node(next).DestroyCore();
        await Run(0.25);
        m.Controller.InvadeNow();
        Check(home.Active?.Source.Kind == SourceKind.Ship && home.Active.Faction is "vharn" or "reavers",
            $"with every hostile base in range knocked out, the next invasion comes by ship: {home.Active?.Source}");
    }

    // ---- Navigation soak ---------------------------------------------------------------

    const double StuckLimit = 3.0, EngageLimit = 40.0, SoakSeconds = 70.0;
    int _soakFirst = 1;

    sealed class SoakStats
    {
        public string Faction = "";
        public int Runs, Troops, StuckOverLimit, Late, Clutter;
        public double WorstStuck, WorstEngage;
        public string WorstStuckWhere = "";
    }

    sealed class Tracked
    {
        public PlaceholderSoldier Soldier;
        public Vector3 Anchor;
        public double AnchorTime, Spawned, StuckMax;
        public double? Engaged;
        public bool CountedStuck;
    }

    async Task Soak(int runs, string faction)
    {
        var stats = new SoakStats { Faction = faction };
        ulong started = Time.GetTicksMsec();
        int repaths = InvaderNavigator.Repaths, sidesteps = InvaderNavigator.Sidesteps, unsticks = InvaderNavigator.Unsticks;
        for (int seed = _soakFirst; seed < _soakFirst + runs; seed++) await SoakOne(seed, faction, stats);
        double seconds = (Time.GetTicksMsec() - started) / 1000.0;
        string name = _data.Faction(faction).Name;
        GD.Print($"SOAK  {name}: {stats.Runs} seeded invasions, {stats.Troops} invaders, {stats.Clutter} obstacles, {seconds:F0} s real time");
        GD.Print($"SOAK  {name}: worst stuck {stats.WorstStuck:F2} s ({stats.WorstStuckWhere}); stuck over {StuckLimit} s: {stats.StuckOverLimit}; " +
                 $"slowest to reach the core room or a breach: {stats.WorstEngage:F1} s; late or never: {stats.Late}");
        GD.Print($"SOAK  {name}: recoveries: {InvaderNavigator.Repaths - repaths} re-paths, {InvaderNavigator.Sidesteps - sidesteps} side-steps, " +
                 $"{InvaderNavigator.Unsticks - unsticks} hops to a free cell");
        Check(stats.StuckOverLimit == 0, $"{name}: no invader stays stuck for more than {StuckLimit} s across {stats.Runs} cluttered invasions (worst {stats.WorstStuck:F2} s)");
        Check(stats.Late == 0, $"{name}: every invader reaches the core room or a breach within {EngageLimit} s (slowest {stats.WorstEngage:F1} s, late {stats.Late})");
    }

    async Task SoakOne(int seed, string faction, SoakStats stats)
    {
        Main m = await NewWorld();
        var rng = new RandomNumberGenerator { Seed = (ulong)(seed * 7919 + 17) };
        m.Combat.TurretsEnabled = false;
        m.Player.Invulnerable = true;
        m.Player.Teleport(new Vector3(0, 0.1f, 260f), 0, 0); // home, but well out of the fight

        float bearing = rng.RandfRange(0f, 360f);
        GroundPos dir = GroundPos.FromBearing(bearing, 1);
        var outward = new Vector3((float)dir.X, 0, (float)dir.Z);
        var right = new Vector3(-outward.Z, 0, outward.X);
        float distance = rng.RandfRange(55f, 75f);
        stats.Clutter += Clutter.Scatter(m, rng, outward, distance);

        ForcePlan plan = ForceComposer.Compose(new BaseStats(rng.RandfRange(1500f, 9000f), 90, 2), rng.RandiRange(1, 3),
            SourceKind.Garrison, _data.Faction(faction), _data, new DeterministicRng((ulong)seed));
        var tracked = new List<Tracked>();
        double now = 0;
        foreach (VehiclePlan v in plan.Vehicles)
        {
            Vector3 drop = outward * distance + right * (float)v.Offset * 0.6f;
            // A big squad (a Bloom swarm) is set down over a wider patch, as it would pour out over time.
            float half = 2.5f * Mathf.Max(1f, Mathf.Sqrt(v.Troops.Count / 8f));
            foreach (string role in v.Troops)
            {
                Vector3 at = drop + new Vector3(rng.RandfRange(-half, half), 0.1f, rng.RandfRange(-half, half));
                IInvaderTroop troop = m.Controller.Factory.SpawnTroop(faction, role, at, -outward);
                troop.Assault();
                tracked.Add(new Tracked { Soldier = (PlaceholderSoldier)troop, Anchor = at, AnchorTime = 0, Spawned = 0 });
            }
        }
        stats.Runs++;
        stats.Troops += tracked.Count;

        const int sampleTicks = 15; // 0.25 s
        for (int tick = 0; now < SoakSeconds; tick += sampleTicks)
        {
            await PhysicsFrames(sampleTicks);
            now += sampleTicks / 60.0;
            bool anyActive = false;
            foreach (Tracked t in tracked)
            {
                PlaceholderSoldier s = t.Soldier;
                if (!IsInstanceValid(s) || !s.IsAlive || s.IsGone || s.State != TroopState.Assaulting) continue;
                anyActive = true;
                Vector3 p = s.GlobalPosition;
                bool engaged = s.AtBreach || s.InCoreRoom;
                if (engaged) t.Engaged ??= now - t.Spawned;
                if (engaged || p.DistanceTo(t.Anchor) >= 0.5f)
                {
                    t.Anchor = p;
                    t.AnchorTime = now;
                }
                double stuck = now - t.AnchorTime;
                t.StuckMax = Math.Max(t.StuckMax, stuck);
                if (stuck > stats.WorstStuck)
                {
                    stats.WorstStuck = stuck;
                    stats.WorstStuckWhere = $"seed {seed}, {s.Role} at {m.Model.Grid.ToLayout(m.Combat.CellAt(p))}";
                }
                if (stuck > StuckLimit && !t.CountedStuck)
                {
                    t.CountedStuck = true;
                    stats.StuckOverLimit++;
                    GD.Print($"  stuck: {faction} seed {seed} at {now:F1} s: {s.Describe()}");
                }
            }
            bool coreDown = !m.Model.Online;
            bool allInside = tracked.All(t => !IsInstanceValid(t.Soldier) || !t.Soldier.IsAlive || t.Soldier.InCoreRoom);
            if (!anyActive || coreDown || allInside) break;
        }

        foreach (Tracked t in tracked)
        {
            // Late: engaged after the limit, or still not engaged when the limit passed. (A run can end
            // sooner, when the squad takes the core; anyone still on the way then wasn't late.)
            double reach = t.Engaged ?? (now >= EngageLimit ? double.PositiveInfinity : 0);
            if (reach > EngageLimit)
            {
                stats.Late++;
                PlaceholderSoldier s = t.Soldier;
                string where = IsInstanceValid(s) ? $"{s.Role} {s.State} at {m.Model.Grid.ToLayout(m.Combat.CellAt(s.GlobalPosition))}, {s.GlobalPosition.DistanceTo(m.Combat.BaseCentre):F0} m out" : "freed";
                GD.Print($"  late: {faction} seed {seed}, {where}, run ended at {now:F1} s (core {(m.Model.Online ? "up" : "down")})");
            }
            if (t.Engaged is double e) stats.WorstEngage = Math.Max(stats.WorstEngage, e);
        }
        if (seed % 10 == 0) GD.Print($"  soak {faction} seed {seed}: worst stuck so far {stats.WorstStuck:F2} s, stuck {stats.StuckOverLimit}, late {stats.Late}");
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
