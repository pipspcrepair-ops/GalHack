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
/// The faction checks: who invades (the nearest base of any hostile faction, by standing), who never
/// does, what each faction sends and how it fights. The navigation soak runs once per invading faction.
/// </summary>
public partial class InvasionTest
{
    /// <summary>Every faction that can invade, in the order the scene tests and the soak run them.</summary>
    static readonly string[] InvadingFactions = { "vharn", "kessari", "meridian", "reavers", "hollow" };

    // ---- Director: who invades ----------------------------------------------------------

    /// <summary>A director with no enemy bases unless the test adds them, and a player home.</summary>
    InvasionDirector Bare(ulong seed = 11, bool shipRaids = false)
    {
        ServerSettings server = _server.Clone();
        server.ShipRaids = shipRaids;
        var d = new InvasionDirector(_data, server, seed);
        d.AddPlayerBase("home", "Homestead", Home);
        d.SetBaseStats("home", new BaseStats(4000, 90, 2));
        return d;
    }

    void FactionDataIsComplete()
    {
        var ids = _data.Factions.All.Select(f => f.Id).ToList();
        Check(ids.OrderBy(x => x).SequenceEqual(new[] { "exchange", "freeholders", "hollow", "kessari", "meridian", "reavers", "vharn" }),
            $"the seven design factions are all there, by their encounter ids: {string.Join(", ", ids)}");
        var missing = _data.Factions.Weapons().Where(w => !_fire.Weapons.ContainsKey(w)).ToList();
        Check(missing.Count == 0, $"every gun and trooper weapon the factions name is in fire_control.json{(missing.Count > 0 ? ": missing " + string.Join(", ", missing) : "")}");
        Check(!_data.Faction("exchange").Invades && !_data.Faction("freeholders").Invades &&
              InvadingFactions.All(f => _data.Faction(f).Invades),
            "the Ledger Exchange and the Freeholders never invade; the other five can");
    }

    void NearestHostileFactionSendsTheInvasion()
    {
        InvasionDirector d = Bare();
        d.AddEnemyBase("larkspur", "Larkspur Steading", "freeholders", EnemyBaseKind.Outpost, new GroundPos(0, -150));   // 150 m N
        d.AddEnemyBase("quay", "Ledger Quay 9", "exchange", EnemyBaseKind.Outpost, new GroundPos(200, 0));            // 200 m E
        d.AddEnemyBase("tannic", "Hold Tannic-3", "kessari", EnemyBaseKind.Garrison, new GroundPos(0, 300));          // 300 m S
        d.AddEnemyBase("vantage", "Vantage Post", "meridian", EnemyBaseKind.Garrison, new GroundPos(-600, 0));        // 600 m W
        d.AddEnemyBase("scrapjaw", "Scrapjaw Den", "reavers", EnemyBaseKind.Garrison, new GroundPos(0, -800));        // 800 m N
        d.AddEnemyBase("sere", "Infestation Sere", "hollow", EnemyBaseKind.Garrison, new GroundPos(700, 714));        // 1000 m SE
        d.AddEnemyBase("okkar", "Garrison Okkar-9", "vharn", EnemyBaseKind.Garrison, new GroundPos(1200, 0));        // 1200 m E
        PlayerBase home = d.Base("home");
        Check(d.NearestSourceBase(home)?.Id == "scrapjaw",
            $"of seven factions' bases, the nearest of a hostile faction sends the invasion: the Reaver den at 800 m, not the neutral or peaceful ones nearer ({d.NearestSourceBase(home)?.Name})");

        d.Wrong("meridian", 100, deliberate: true);
        d.SetPlayersPresent("home", 1);
        d.StartInvasionNow("home");
        InvasionSource src = home.Active?.Source;
        Check(src?.Faction == "meridian" && src.EnemyBaseId == "vantage" && src.Compass == "W",
            $"wronged on purpose, the Meridian Combine turns hostile and its post at 600 m is now nearest and sends the next: {src}");
        DirectorEvent warning = d.Log.FirstOrDefault(e => e.Kind == DirectorEventKind.Warning && e.InvasionId == home.Active?.Id);
        Check(warning != null && warning.Text.Contains("Meridian Combine") && warning.Text.Contains("Vantage Post"),
            $"the first warning names the attacking faction and its base: \"{warning?.Text}\"");

        d.SetEnemyBaseState("vantage", EnemyBaseState.Destroyed);
        Check(home.History.Last().Outcome == InvasionOutcome.CalledOff && d.NearestSourceBase(home)?.Id == "scrapjaw",
            "knocking out the Meridian post calls its assault off, and the Reavers are next");

        d.Wrong("kessari", 200, deliberate: false);
        Check(d.NearestSourceBase(home)?.Id == "scrapjaw" && !d.Standings.IsHostile("kessari"),
            $"an accident doesn't turn the Kessari (standing {d.Standings["kessari"]:0})");
        d.Wrong("kessari", 100, deliberate: true);
        d.Advance(1);
        d.StartInvasionNow("home");
        Check(home.Active?.Source.Faction == "kessari" && home.Active.Source.EnemyBaseId == "tannic",
            $"wronged on purpose, the Kessari hold at 300 m becomes the nearest hostile base and invades: {home.Active?.Source}");
    }

    void ExchangeAndFreeholdersNeverInvade()
    {
        InvasionDirector d = Bare();
        d.AddEnemyBase("larkspur", "Larkspur Steading", "freeholders", EnemyBaseKind.Garrison, new GroundPos(0, -150));
        d.AddEnemyBase("quay", "Ledger Quay 9", "exchange", EnemyBaseKind.Fortress, new GroundPos(200, 0));
        double before = d.Standings["exchange"];
        d.Wrong("exchange", 500, deliberate: false);
        d.Wrong("freeholders", 500, deliberate: false);
        Check(d.Standings["exchange"] == before && !d.Standings.IsHostile("freeholders"),
            "the Ledger Exchange and the Freeholders can't be made hostile by accident");

        d.Wrong("exchange", 500, deliberate: true);
        d.Wrong("freeholders", 500, deliberate: true);
        PlayerBase home = d.Base("home");
        Check(d.Standings.IsHostile("exchange") && d.Standings.IsHostile("freeholders") && d.NearestSourceBase(home) == null,
            "wronged on purpose they turn hostile, yet their bases at 150 and 200 m never become a source");
        // Ship raids off, and theirs the only bases in range.
        d.SetPlayersPresent("home", 1);
        d.Advance(LongestInterval * 10);
        Check(!d.HasSource(home) && !d.Log.Any(e => e.Kind == DirectorEventKind.InvasionScheduled) && !d.StartInvasionNow("home"),
            "with only hostile Exchange and Freeholder bases in range, no invasion comes in ten intervals");
    }

    void FactionTurnedFriendlyStopsInvading()
    {
        InvasionDirector d = Bare();
        d.AddEnemyBase("vantage", "Vantage Post", "meridian", EnemyBaseKind.Garrison, new GroundPos(-400, 0));
        d.AddEnemyBase("okkar", "Garrison Okkar-9", "vharn", EnemyBaseKind.Garrison, new GroundPos(900, 0));
        PlayerBase home = d.Base("home");
        d.Wrong("meridian", 100, deliberate: true);
        d.SetPlayersPresent("home", 1);
        d.StartInvasionNow("home");
        Invasion incoming = home.Active;
        d.Advance(200);
        d.Favour("meridian", 200);
        Check(incoming?.Faction == "meridian" && incoming.Phase == InvasionPhase.Ended && incoming.Outcome == InvasionOutcome.CalledOff &&
              d.Log.Any(e => e.Kind == DirectorEventKind.StandingChanged && e.Text.Contains("no longer")),
            $"the Meridian Combine turned friendly ({d.Standings["meridian"]:0}) calls off its incoming assault");
        bool next = RunUntil(d, () => home.Active != null, LongestInterval);
        Check(next && home.Active.Faction == "vharn",
            $"the next invasion comes from the next nearest hostile base, the Vharn at 900 m: {home.Active?.Source}");

        // An assault under way pulls out when its faction stands down.
        InvasionDirector a = Bare();
        a.AddEnemyBase("tannic", "Hold Tannic-3", "kessari", EnemyBaseKind.Garrison, new GroundPos(0, 300));
        a.Wrong("kessari", 100, deliberate: true);
        a.SetPlayersPresent("home", 1);
        a.StartInvasionNow("home");
        Invasion assault = a.Base("home").Active;
        a.Advance(601);
        a.ReportInvadersRemaining("home", 12);
        a.Favour("kessari", 200);
        Check(assault.Phase == InvasionPhase.Withdrawing && assault.Outcome == InvasionOutcome.CalledOff && assault.SirenOn,
            "a Kessari assault under way withdraws when the Kessari are made friends again; the siren sounds until they're gone");
        a.ReportInvadersRemaining("home", 0);
        a.Advance(LongestInterval * 6);
        Check(assault.Phase == InvasionPhase.Ended && !a.HasSource(a.Base("home")) &&
              a.Base("home").History.Count == 1,
            "and with no hostile faction left in reach and no ship raids, nothing more comes");
    }

    void StandingRespectsEachFactionsNature()
    {
        InvasionDirector d = Bare();
        d.Favour("vharn", 500);
        Check(d.Standings.Invades("vharn"), $"the Vharn Dominion can't be won over: at best {d.Standings["vharn"]:0}, still hostile");
        d.Favour("hollow", 500);
        Check(d.Standings["hollow"] == -100 && d.Standings.Invades("hollow"), "the Hollow Bloom is always hostile");
        d.Favour("reavers", 500);
        Check(d.Standings["reavers"] == 0 && !d.Standings.Invades("reavers") && d.Standings.Tier("reavers") == StandingTier.Neutral,
            "the Carrion Reavers can be paid off to a truce, never to friendship, and stop raiding");
        Check(!d.Standings.Invades("kessari") && !d.Standings.Invades("meridian"), "the Kessari and the Meridian Combine start out peaceable");
        d.Wrong("meridian", 40, deliberate: false);
        double halved = d.Standings["meridian"];
        d.Wrong("kessari", 40, deliberate: false);
        Check(Math.Abs(halved - (10 - 20)) < 1e-9 && d.Standings["kessari"] == 0,
            $"accidents count half against the Meridian Combine ({halved:0}) and not at all against the Kessari");
        d.Wrong("meridian", 60, deliberate: true);
        Check(d.Standings.Invades("meridian"), $"wronged on purpose, the Meridian Combine turns hostile ({d.Standings["meridian"]:0})");
    }

    void ShipRaidsComeFromHostileFactions()
    {
        InvasionDirector d = Bare(shipRaids: true);
        PlayerBase home = d.Base("home");
        d.Wrong("exchange", 500, deliberate: true);
        d.Wrong("freeholders", 500, deliberate: true);
        var count = new Dictionary<string, int>();
        for (int i = 1; i <= 400; i++)
        {
            string f = d.ChooseSource(home, new DeterministicRng((ulong)i)).Faction;
            count[f] = count.GetValueOrDefault(f) + 1;
        }
        string tally = string.Join(", ", count.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} {kv.Value}"));
        Check(count.Keys.All(f => f is "vharn" or "reavers") && count.GetValueOrDefault("vharn") > count.GetValueOrDefault("reavers") &&
              count.GetValueOrDefault("reavers") > 100,
            $"with no base in range, ship raids come from hostile factions with ships, by weight; never the Bloom, the Exchange or the Freeholders: {tally}");
        d.Wrong("kessari", 100, deliberate: true);
        bool kessari = Enumerable.Range(1, 200).Any(i => d.ChooseSource(home, new DeterministicRng((ulong)i)).Faction == "kessari");
        Check(kessari, "once the Kessari are hostile, some ship raids are theirs");
        d.SetStanding("kessari", 100);
        d.SetStanding("reavers", 100);
        bool onlyVharn = Enumerable.Range(1, 100).All(i => d.ChooseSource(home, new DeterministicRng((ulong)i)).Faction == "vharn");
        Check(onlyVharn, "with the Kessari befriended and the Reavers paid off, every ship raid is the Vharn's");
    }

    void EachFactionComposesItsOwnForce()
    {
        var stats = new BaseStats(6000, 120, 2);
        foreach (string id in InvadingFactions)
        {
            FactionData f = _data.Faction(id);
            var kinds = new List<SourceKind> { SourceKind.Outpost, SourceKind.Garrison, SourceKind.Fortress };
            if (f.CanRaidByShip) kinds.Add(SourceKind.Ship);
            var drawn = new HashSet<string>();
            bool ok = true;
            string why = "";
            foreach (SourceKind kind in kinds)
            {
                ForcePlan plan = ForceComposer.Compose(stats, 2, kind, f, _data, new DeterministicRng(3));
                var allowed = f.Carriers[InvasionData.Key(kind)].Keys.ToHashSet();
                foreach (VehiclePlan v in plan.Vehicles)
                {
                    VehicleTypeData type = f.Vehicles[v.Type];
                    if (!allowed.Contains(v.Type) || v.Faction != id || v.Body != type.Body || v.Crew != 1 + type.Guns.Count || v.Troops.Count > type.Capacity)
                    { ok = false; why = $"{kind} {v}"; }
                    if (v.Troops.Count >= 2 && !v.Troops.Contains(f.BreacherRole)) { ok = false; why = $"{kind} squad without a {f.BreacherRole}"; }
                    foreach (string r in v.Troops) drawn.Add(r);
                }
                if (plan.Faction != id || plan.Vehicles.Sum(v => v.Troops.Count) != plan.Troops) { ok = false; why = $"{kind} seats"; }
            }
            bool roles = drawn.All(r => f.Roles.TryGetValue(r, out RoleData rd) && rd.Share > 0) && drawn.Count >= 2;
            Check(ok && roles,
                $"{f.Name}: {string.Join(" and ", f.Vehicles.Values.Select(v => v.Label + "s").Distinct())} carry its own {string.Join(", ", drawn.OrderBy(r => r))}, every troop seated, every squad with a {f.BreacherRole} {why}");
        }
        int vharn = ForceComposer.Compose(stats, 1, SourceKind.Garrison, _data.Faction("vharn"), _data, new DeterministicRng(4)).Troops;
        ForcePlan swarm = ForceComposer.Compose(stats, 1, SourceKind.Garrison, _data.Faction("hollow"), _data, new DeterministicRng(4));
        Check(swarm.Troops > vharn && swarm.Vehicles.Count == 1 && swarm.Vehicles[0].Body == "growth",
            $"the Bloom swarms: {swarm.Troops} against the Vharn's {vharn} for the same base, all from one growth ({swarm.Tier})");
        ForcePlan raid = ForceComposer.Compose(stats, 1, SourceKind.Garrison, _data.Faction("reavers"), _data, new DeterministicRng(4));
        Check(raid.Vehicles.Any(v => v.Body == "hoverbike") && raid.Vehicles.All(v => v.DropDistance < _data.Approach.DropDistance),
            $"the Reavers come on hover bikes and gunships that set down close: {raid.VehicleSummary()}");
    }

    void KessariPlanStraightThroughWalls()
    {
        BaseGrid grid = new BaseModel(_base, Home).Grid;
        var vharn = new BreachPlanner(_data.Planner.HpPerCellOfDetour);
        var kessari = new BreachPlanner(_data.Faction("kessari").Tactics.HpPerCellOfDetour ?? 0);
        int vharnCells = 0, kessariCells = 0, vharnBreaches = 0, kessariBreaches = 0;
        foreach (Cell start in new[] { grid.FromLayout(-6, 6), grid.FromLayout(24, 6), grid.FromLayout(9, 20), grid.FromLayout(3, -6) })
        {
            AssaultPlan v = vharn.Plan(grid, start, grid.CoreSlot), k = kessari.Plan(grid, start, grid.CoreSlot);
            vharnCells += v.Cells.Count;
            kessariCells += k.Cells.Count;
            vharnBreaches += v.BreachBlockIds.Count;
            kessariBreaches += k.BreachBlockIds.Count;
        }
        Check(kessariCells < vharnCells && kessariBreaches >= vharnBreaches,
            $"the Kessari cut straight through: {kessariCells} cells and {kessariBreaches} breaches to the core from four sides, against the Vharn's {vharnCells} and {vharnBreaches}");
    }

    // ---- Scene: each faction's roster and tactics -----------------------------------------

    static readonly Dictionary<string, Type> Bodies = new()
    {
        ["apc"] = typeof(Apc), ["dropship"] = typeof(Dropship), ["strider"] = typeof(Strider), ["lifter"] = typeof(Lifter),
        ["hovertruck"] = typeof(HoverTruck), ["shuttle"] = typeof(Shuttle), ["hoverbike"] = typeof(HoverBike),
        ["gunship"] = typeof(Gunship), ["growth"] = typeof(Growth),
    };

    /// <summary>
    /// A fresh world where only <paramref name="faction"/> can invade: it is made hostile if it isn't,
    /// and every other faction's base is knocked out. The invasion is started and run to arrival.
    /// </summary>
    async Task<Main> InvadedBy(string faction)
    {
        Main m = await NewWorld();
        m.Combat.TurretsEnabled = false;
        m.Player.Invulnerable = true;
        if (!m.Director.Standings.Invades(faction)) m.Director.Wrong(faction, 200, deliberate: true);
        foreach (EnemyBaseNode b in m.EnemyBases.Where(b => b.Faction != faction)) b.DestroyCore();
        await Run(0.1);
        m.Controller.InvadeNow();
        m.Controller.SkipToArrival();
        Invasion inv = m.Controller.Current;
        if (inv != null)
        {
            // The player stays home (the presence rule), on the far side from the attack.
            GroundPos d = GroundPos.FromBearing(inv.Source.Bearing, 1);
            m.Player.Teleport(m.Combat.BaseCentre - new Vector3((float)d.X, 0, (float)d.Z) * 150f + Vector3.Up * 0.1f, 0f, 0f);
        }
        m.Controller.SpawnPendingNow();
        return m;
    }

    IEnumerable<PlaceholderSoldier> Soldiers(Main m) => m.Combat.Troops.OfType<PlaceholderSoldier>().Where(IsInstanceValid);

    async Task FactionRosterAndTactics(string faction)
    {
        FactionData f = _data.Faction(faction);
        Main m = await InvadedBy(faction);
        Invasion inv = m.Controller.Current;
        Check(inv?.Faction == faction && inv.Phase == InvasionPhase.Assault,
            $"{f.Name}: with its base the only hostile one in range, it invades: {inv?.Source}");
        if (inv == null) return;
        int total = m.Model.Loot.TotalItems;

        var vehicles = m.Combat.Vehicles.OfType<CrewedVehicle>().ToList();
        bool ownVehicles = vehicles.Count == inv.Force.Vehicles.Count && vehicles.All(v => v.Plan.Faction == faction && Bodies[v.Plan.Body] == v.GetType());
        bool landed = await RunUntil(() => Soldiers(m).Count() >= Math.Min(6, inv.Force.Troops), 60);
        var troops = Soldiers(m).ToList();
        bool ownTroops = troops.All(t => t.Faction == faction && f.Roles.ContainsKey(t.Role));
        Check(ownVehicles && landed && ownTroops,
            $"{f.Name}: its own {inv.Force.VehicleSummary()} ({string.Join(", ", vehicles.Select(v => v.GetType().Name).Distinct())}) set down " +
            $"its own troops ({string.Join(", ", troops.GroupBy(t => t.Role).Select(g => $"{g.Count()} {g.Key}"))})");

        switch (faction)
        {
            case "vharn":
            {
                bool charged = await RunUntil(() => Soldiers(m).Any(t => t.ChargesPlanted > 0), 90);
                Check(charged, "Vharn: breachers plant charges on the walls");
                break;
            }
            case "kessari":
            {
                bool cut = await RunUntil(() => Soldiers(m).Any(t => t.CutDamage > 100f), 90);
                PlaceholderSoldier cutter = Soldiers(m).OrderByDescending(t => t.CutDamage).FirstOrDefault();
                Check(cut && cutter?.Role == "cutter" && m.Combat.PlannerFor("kessari") != m.Combat.Planner,
                    $"Kessari: hovering cutters burn through the walls with beams ({cutter?.CutDamage:F0} damage cut), on a plan that goes straight through");
                break;
            }
            case "meridian":
            {
                Check(troops.All(t => t.ShieldLeft > 0f), "Meridian: every officer lands with a shield up");
                PlaceholderSoldier officer = troops.First(t => t.Role != f.BreacherRole);
                float hp = officer.Health;
                officer.TakeDamage(40f, officer.GlobalPosition, Team.Defenders);
                float afterSoak = officer.Health, shieldLeft = officer.ShieldLeft;
                officer.TakeDamage(50f, officer.GlobalPosition, Team.Defenders);
                Check(afterSoak == hp && Math.Abs(shieldLeft - (officer.Stats.Shield - 40)) < 1e-3 && Math.Abs(officer.Health - (hp - Math.Max(0f, 50f - shieldLeft))) < 1e-3,
                    $"Meridian: a shield soaks hits first and the rest goes straight through, never capped ({hp:0} hp: 40 → shield, then 50 → {officer.Health:0} hp)");
                bool stacked = await RunUntil(() => Soldiers(m).Any(t => t.Role != f.BreacherRole && t.LongestStackUp >= 3f) &&
                                                    Soldiers(m).Any(t => t.ChargesPlanted > 0), 90);
                Check(stacked, "Meridian: officers stack up at the wall holding fire while the breaching team sets its charges");
                bool recharged = await RunUntil(() => !IsInstanceValid(officer) || !officer.IsAlive || officer.ShieldLeft > 5f, 10);
                Check(recharged, "Meridian: a shield recharges once the hits stop");
                break;
            }
            case "reavers":
            {
                Check(troops.Where(t => t.Role != f.CrewRole).All(t => t.Goal?.Kind == BlockKind.Container),
                    "Reavers: raiders go for the storage containers, not the core");
                int containers = m.Model.Grid.Alive.Count(b => b.Kind == BlockKind.Container);
                bool smashed = await RunUntil(() => m.Model.Grid.Alive.Count(b => b.Kind == BlockKind.Container) < containers, 90);
                bool ran = await RunUntil(() => m.Controller.RaidOver, f.Tactics.RaidSeconds + 30);
                await Run(0.5);
                bool running = Soldiers(m).Where(t => t.IsAlive && !t.IsGone).All(t => t.State == TroopState.Withdrawing);
                Check(smashed && ran && running && m.Model.Online,
                    $"Reavers: they break the storage open, find the spill crates too heavy to lift, and run for their rides " +
                    $"({m.Model.Grid.Alive.Count(b => b.Kind == BlockKind.Container)} of {containers} containers left, core {(m.Model.Online ? "standing" : "down")})");
                bool gone = await RunUntil(() => m.Controller.Current == null, 150);
                Check(gone && m.Model.Loot.TotalItems == total,
                    $"Reavers: once they're gone, every item is still there ({m.Model.Loot.TotalItems} of {total}, {m.Model.Loot.Crates.Count} spill crates): they took nothing");
                break;
            }
            case "hollow":
            {
                var growth = vehicles.OfType<Growth>().Single();
                bool pouring = await RunUntil(() => growth.Unloaded >= 12, 30);
                bool burst = await RunUntil(() => Soldiers(m).Any(t => t.Burst), 90);
                Check(pouring && burst && growth.Spread > 1f,
                    $"Hollow Bloom: a growth breaks the surface and spreads ({growth.Spread:F0} m of tendrils), pouring out the swarm ({growth.Unloaded}), and bursters burst against the walls");
                // A second growth, killed at the heart while it is still pouring.
                var plan = new VehiclePlan { Index = 9, Faction = "hollow", Type = "growth", Body = "growth", Label = "growth", Guns = new List<string> { "spitter", "spitter" }, UnloadSeconds = 0.22 };
                for (int i = 0; i < 30; i++) plan.Troops.Add(i % 4 == 0 ? "corrupted" : "mutant");
                Vector3 drop = m.Combat.BaseCentre + new Vector3(0, 0, -110f);
                var second = (Growth)m.Controller.Factory.SpawnVehicle(plan, new VehicleRoute(drop, drop, m.Combat.BaseCentre, Vector3.Forward, 520f));
                await RunUntil(() => second.Unloaded >= 4, 20);
                int aboard = second.TroopsAboard;
                second.Crew.Damage(0, 1e6);
                await Run(0.2);
                Check(aboard > 0 && second.IsStranded && second.TroopsAboard == 0 && second.Crew.AliveCount == 0,
                    $"Hollow Bloom: kill the heart and the growth dies, its spitter pods and its {aboard} unborn with it");
                break;
            }
        }
        await Run(1);
        Check(m.Model.Loot.TotalItems == total, $"{f.Name}: no item was taken or destroyed ({m.Model.Loot.TotalItems} of {total})");
    }
}
