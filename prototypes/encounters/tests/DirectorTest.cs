using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Encounters.Director;
using Godot;
using static Encounters.Director.MathX;
using Vector3 = System.Numerics.Vector3;

namespace Encounters;

/// <summary>
/// Headless tests of the encounter director. Run with tools/test.ps1, or:
///   godot --headless --path prototypes/encounters res://tests/DirectorTest.tscn
/// The director is pure C#, so everything runs on a simulated clock as fast as the CPU allows.
/// Exits 0 if every check passes, 1 if any fails.
/// </summary>
public partial class DirectorTest : Node
{
    const string Orbit = "veyra-orbit", Deep = "drift-deep", Lanes = "outer-lanes", Moon = "kessa-moon-orbit", Surface = "veyra-surface";

    int _failures, _checks;
    string _configJson, _zonesJson, _deckJson;

    public override void _Ready()
    {
        _configJson = GameData.Read(GameData.ConfigPath);
        _zonesJson = GameData.Read(GameData.ZonesPath);
        _deckJson = GameData.Read(GameData.DeckPath);

        var tests = new (string Name, Action Run)[]
        {
            ("Spawn rules under churn: view cone, distance, budget, caps, no-repeat", SpawnRulesUnderChurn),
            ("Deck weights follow zone tags", DeckWeightsFollowTags),
            ("Vharn Dominion and Freeholders", VharnAndFreeholders),
            ("No-spawn circles and player bases", NoSpawnCircles),
            ("Leaving a zone cleans up", LeavingCleansUp),
            ("Cleanup never removes anything player-owned", PlayerOwnedSurvives),
            ("Determinism", SameSeedSameEvents),
            ("Zones per player, scaling by players", ZonesAndScaling),
            ("Signal leads", SignalLeads),
            ("Tick cost", TickCost),
            ("Gunnery hook (not wired: nothing shoots at players yet)", GunneryHook),
            ("Export", ExportFilter),
        };
        foreach (var (name, run) in tests)
        {
            GD.Print($"\n== {name}");
            try { run(); }
            catch (Exception e) { Fail($"{name} threw {e}"); }
        }
        GD.Print(_failures == 0 ? $"\nALL PASSED ({_checks} checks)" : $"\n{_failures} OF {_checks} CHECK(S) FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    // ---- Tests --------------------------------------------------------------------------

    /// <summary>
    /// Two hours of busy simulated play in four zones at once, with the spawn gap and event
    /// durations cut right down so thousands of events come and go. Every ship of every spawn is
    /// checked against every player in its zone.
    /// </summary>
    void SpawnRulesUnderChurn()
    {
        DirectorConfig cfg = ChurnConfig();
        var d = new EncounterDirector(cfg, Zones(), ChurnDeck(), seed: 7);
        var players = new List<Wanderer>
        {
            new(0, Orbit, new Vector3(0, 0, 0), 1), new(1, Orbit, new Vector3(800, 100, 400), 2),
            new(2, Deep, new Vector3(0, 0, 0), 3),
            new(3, Lanes, new Vector3(0, 0, 0), 4), new(4, Lanes, new Vector3(-1500, 0, 900), 5), new(5, Lanes, new Vector3(2500, -200, 0), 6),
            new(6, Moon, new Vector3(-1000, 0, -1000), 7),
        };

        int spawns = 0, ships = 0, tooClose = 0, inView = 0, inNoSpawn = 0, repeats = 0, peacefulInHostile = 0, freeholderShips = 0;
        float nearest = float.MaxValue, narrowest = 180f, shortestRepeat = float.MaxValue;
        var last = new Dictionary<(string, string), double>();
        int leads = 0, badLeads = 0;
        d.Spawned += (zi, enc) =>
        {
            spawns++;
            foreach (WorldEntity ship in enc.Ships)
            {
                ships++;
                if (zi.Zone.BlocksSpawn(ship.Position)) inNoSpawn++;
                if (enc.Def.Hostile && !d.Deck.Fights(ship.Faction)) peacefulInHostile++;
                if (ship.Faction == "freeholders") freeholderShips++;
                foreach (PlayerState p in zi.Players)
                {
                    float dist = Vector3.Distance(ship.Position, p.Position);
                    float angle = AngleDeg(ship.Position - p.Position, p.Forward);
                    nearest = MathF.Min(nearest, dist);
                    narrowest = MathF.Min(narrowest, angle);
                    if (dist < cfg.MinSpawnDistance) tooClose++;
                    if (angle <= cfg.ViewConeHalfAngleDeg) inView++;
                }
            }
            var key = (zi.Zone.Id, enc.Def.Id);
            if (last.TryGetValue(key, out double previous))
            {
                shortestRepeat = MathF.Min(shortestRepeat, (float)(enc.Start - previous));
                if (enc.Start - previous < cfg.NoRepeatWindow) repeats++;
            }
            last[key] = enc.Start;
        };
        d.LeadEmitted += lead =>
        {
            leads++;
            if (lead.AngleErrorDeg > cfg.Lead.AngleFuzzDeg + 0.01f || lead.DistanceError > cfg.Lead.DistanceFuzz + 1e-4f) badLeads++;
        };

        int overEvents = 0, overShips = 0, overCaps = 0, peakEvents = 0, peakShips = 0;
        Simulate(d, players, 7200, _ =>
        {
            foreach (ZoneInstance zi in d.Instances)
            {
                if (zi.Encounters.Count > zi.MaxEvents) overEvents++;
                if (zi.Ships.Count > zi.MaxShips) overShips++;
                peakEvents = Math.Max(peakEvents, zi.Encounters.Count);
                peakShips = Math.Max(peakShips, zi.Ships.Count);
                foreach (Encounter enc in zi.Encounters)
                    if (zi.CategoryCount(enc.Def.Category) > cfg.CategoryCap(enc.Def.Category)) overCaps++;
            }
        });

        int rejectedInView = d.Instances.Sum(zi => zi.Rejections[(int)SpawnVerdict.InView]);
        int rejectedClose = d.Instances.Sum(zi => zi.Rejections[(int)SpawnVerdict.TooClose]);
        GD.Print(Inv($"      {spawns} spawns, {ships} ships, 7 players in 4 zones over 2 h of simulated time"));
        GD.Print(Inv($"      candidate points turned down: {rejectedInView} in a view cone, {rejectedClose} too close"));
        Check(spawns >= 2000, Inv($"thousands of spawns sampled ({spawns} events, {ships} ships)"));
        Check(inView == 0, Inv($"no ship ever spawned inside a player's {cfg.ViewConeHalfAngleDeg:F0}° view cone (narrowest {narrowest:F1}° off a player's view)"));
        Check(tooClose == 0, Inv($"no ship ever spawned nearer than {cfg.MinSpawnDistance:F0} m to a player (nearest {nearest:F0} m)"));
        Check(inNoSpawn == 0, "no ship ever spawned inside a no-spawn circle or player base");
        Check(overEvents == 0 && overShips == 0, Inv($"the concurrent budget was never exceeded (peak {peakEvents} events, {peakShips} ships in a zone)"));
        Check(overCaps == 0, "no category cap was ever exceeded");
        Check(repeats == 0, Inv($"no event type repeated within {cfg.NoRepeatWindow:F0} s in a zone (shortest repeat {shortestRepeat:F1} s)"));
        Check(leads > 0 && badLeads == 0, Inv($"all {leads} leads from natural spawns were within the fuzz"));
        Check(freeholderShips > 0 && peacefulInHostile == 0,
            $"no Ledger Exchange or Freeholder ship ever spawned in a hostile event ({freeholderShips} Freeholder ships seen)");
    }

    void DeckWeightsFollowTags()
    {
        DirectorConfig cfg = Config();
        EventDeck deck = Deck();
        ZoneCatalog zones = Zones();

        // The danger grade sets the hostile share outright.
        var shares = new List<string>();
        bool exact = true, rising = true;
        float previous = -1f;
        for (int danger = 1; danger <= 5; danger++)
        {
            var zone = new ZoneDef { Id = "test", Kind = ZoneKind.Orbit, Danger = danger, Circles = { Poi("station", 0, -3000, 300) } };
            float hostile = DeckWeights.Evaluate(deck, zone, Vector3.Zero, cfg).Where(w => w.Def.Hostile).Sum(w => w.Share);
            exact &= MathF.Abs(hostile - cfg.HostileShare(danger)) < 1e-4f;
            rising &= hostile > previous;
            previous = hostile;
            shares.Add(Inv($"{danger}: {hostile:P0}"));
        }
        Check(exact && rising, $"the hostile share of the deck follows the danger grade ({string.Join(", ", shares)})");

        // And it shows in what actually spawns.
        float low = HostileFraction(danger: 1), high = HostileFraction(danger: 4);
        Check(high > low + 0.25f && MathF.Abs(low - cfg.HostileShare(1)) < 0.1f,
            Inv($"a high danger grade spawns more hostile events (danger 1: {low:P0} of spawns hostile, deck {cfg.HostileShare(1):P0}; danger 4: {high:P0}, deck {cfg.HostileShare(4):P0})"));

        // Faction territory gates and boosts faction events.
        ZoneDef deep = zones.Get(Deep), orbit = zones.Get(Orbit), lanes = zones.Get(Lanes);
        Vector3 kessariOnly = new(6000, 0, -4000), reaverOnly = new(-7500, 0, 3500), unclaimed = new(9000, 0, 9000);
        Check(Share(deck, deep, kessariOnly, "kessari-miners", cfg) > 0f && Share(deck, deep, reaverOnly, "kessari-miners", cfg) == 0f
              && Share(deck, deep, unclaimed, "kessari-miners", cfg) == 0f,
            "Kessari miners only inside Kessari territory");
        Check(Share(deck, orbit, Vector3.Zero, "patrol-vs-reavers", cfg) > 0f && Share(deck, lanes, Vector3.Zero, "patrol-vs-reavers", cfg) == 0f,
            "Meridian patrols inside Meridian territory, never in unclaimed space");
        float home = Raw(deck, deep, reaverOnly, "reaver-pack", cfg), away = Raw(deck, deep, kessariOnly, "reaver-pack", cfg);
        Check(MathF.Abs(home - away * cfg.Territory.HomeBoost) < 1e-3f, Inv($"Reaver packs spawn {cfg.Territory.HomeBoost}x as often in Reaver territory"));

        WeightedEvent clash = DeckWeights.Evaluate(deck, deep, Vector3.Zero, cfg).First(w => w.Def.Id == "border-clash");
        Check(clash.Share > 0f && clash.SideA == "kessari" && clash.SideB == "reavers" && Share(deck, deep, kessariOnly, "border-clash", cfg) == 0f,
            $"factions fight where their territories meet ({clash.SideA} vs {clash.SideB}), and only there");
        Check(DeckWeights.Evaluate(deck, lanes, Vector3.Zero, cfg).All(w => w.Share == 0f || w.Def.Territory is TerritoryRule.Any or TerritoryRule.Outside),
            "unclaimed space has no faction-territory events");

        // In a real run, faction events follow where the player is.
        int inside = CountSpawns(Deep, kessariOnly, "kessari-miners"), outside = CountSpawns(Deep, reaverOnly, "kessari-miners");
        Check(inside > 0 && outside == 0, $"a player in Kessari space meets Kessari miners ({inside} spawns), a player in Reaver space never does ({outside})");

        // Tag and POI gating.
        Check(zones.Zones.All(z => (Share(deck, z, Vector3.Zero, "crash-site", cfg) > 0f) == (z.Kind == ZoneKind.Surface)),
            "crash sites only on surfaces");
        Check(zones.Zones.All(z => (Share(deck, z, Vector3.Zero, "traders-dock", cfg) > 0f) == (z.FindPoi("station") != null)),
            "traders dock only where there is a station");
        Check(Share(deck, orbit, Vector3.Zero, "bloom-outbreak", cfg) == 0f && GateOf(deck, orbit, "bloom-outbreak", cfg) == Gate.Danger,
            "a Bloom outbreak needs danger 3+, so Veyra's danger-2 station is spared");
    }

    void NoSpawnCircles()
    {
        DirectorConfig cfg = ChurnConfig();
        // A no-spawn circle and a player base right across the spawn ring on both sides of the player.
        var zone = new ZoneDef
        {
            Id = "fenced", Kind = ZoneKind.Orbit, Danger = 2, Radius = 20000,
            Circles =
            {
                new ZoneCircle { Kind = CircleKind.NoSpawn, Name = "shear", X = 2800, Z = 0, R = 1500 },
                new ZoneCircle { Kind = CircleKind.Base, Name = "friends' base", X = -2800, Z = 0, R = 1200 },
                Poi("station", 0, 6000, 300),
            },
        };
        var d = new EncounterDirector(cfg, new ZoneCatalog { Zones = { zone } }, ChurnDeck(), seed: 21);
        int ships = 0, inside = 0;
        d.Spawned += (zi, enc) =>
        {
            foreach (WorldEntity ship in enc.Ships)
            {
                ships++;
                if (zone.BlocksSpawn(ship.Position)) inside++;
            }
        };
        var spinner = new Wanderer(0, "fenced", Vector3.Zero, 9) { Speed = 0f }; // stays put, looks around
        Simulate(d, new List<Wanderer> { spinner }, 3600);
        int rejected = d.Instance("fenced").Rejections[(int)SpawnVerdict.NoSpawnCircle];
        Check(inside == 0 && rejected > 50,
            $"no ship spawned inside a no-spawn circle or player base ({ships} ships; {rejected} candidate points turned down for it)");
    }

    void LeavingCleansUp()
    {
        var d = new EncounterDirector(Config(), Zones(), Deck(), seed: 11);
        d.SetPlayer(0, Orbit, Vector3.Zero, -Vector3.UnitZ);
        d.Tick();
        d.DebugSpawn(Orbit, "convoy", new Vector3(3000, 0, 1500), Vector3.Zero);
        Encounter freighter = d.DebugSpawn(Orbit, "freighter-pass", new Vector3(-2500, 200, 0), Vector3.Zero);
        Encounter fight = d.DebugSpawn(Orbit, "patrol-vs-reavers", new Vector3(0, 300, 3000), Vector3.Zero);

        // Fly over to the live freighter and touch it: remembered, but still its crew's.
        WorldEntity touched = freighter.Ships[0];
        d.SetPlayer(0, Orbit, touched.Position + new Vector3(0, 60, 0), -Vector3.UnitZ);
        TouchOutcome touch = d.Touch(0, touched.Id);
        Check(touch.Status == TouchStatus.Touched && touch.Kind == TouchKind.Touched && touched.Owner < 0
              && touched.IsLasting && d.Records.Contains(touched.Id) && !touched.Live,
            "touching a live freighter makes it a permanent record on the spot, but not the player's");

        // Back home to watch the fight until someone is shot down.
        for (int i = 0; i < 1200 && !fight.Ships.Any(s => s.Wrecked); i++)
        {
            d.SetPlayer(0, Orbit, Vector3.Zero, Vector3.UnitZ);
            d.Tick();
        }
        var wrecks = fight.Ships.Where(s => s.Wrecked).Select(s => s.Id).ToList();
        Check(wrecks.Count > 0 && wrecks.All(d.Records.Contains), Inv($"{wrecks.Count} ship(s) wrecked in sight were registered as salvage"));

        // A wreck's core is gone, so it can be claimed.
        WorldEntity salvage = fight.Ships.First(s => s.Wrecked);
        d.SetPlayer(0, Orbit, salvage.Position + new Vector3(0, 60, 0), Vector3.UnitZ);
        TouchOutcome claim = d.Touch(0, salvage.Id);
        d.SetPlayer(0, Orbit, Vector3.Zero, Vector3.UnitZ);
        Check(claim.Kind == TouchKind.Claimed && salvage.Owner == 0, "touching a wreck claims it as salvage: now the player's");

        ZoneInstance zi = d.Instance(Orbit);
        var untouched = zi.Ships.Select(s => s.Id).ToList();
        int lasting = d.Records.InZone(Orbit).Count;
        d.SetPlayer(0, Deep, Vector3.Zero, -Vector3.UnitZ); // the last player leaves Veyra

        Check(d.Instance(Orbit) == null, "the zone instance is gone once its last player leaves");
        Check(untouched.Count > 0 && untouched.All(id => !d.Records.Contains(id)),
            $"all {untouched.Count} untouched ships were cleaned up");
        Check(d.Records.Contains(touched.Id) && touched.Touch == TouchKind.Touched && touched.Owner < 0 && touched.ZoneId == Orbit,
            "the touched freighter stays, as a permanent record in Veyra, still not the player's");
        Check(d.Records.Contains(salvage.Id) && salvage.Owner == 0, "the claimed wreck stays, and is still the player's");
        Check(wrecks.All(d.Records.Contains) && d.Records.InZone(Orbit).Count == lasting,
            Inv($"the wrecks stay as salvage ({lasting} permanent records in Veyra before and after)"));

        d.SetPlayer(0, Orbit, Vector3.Zero, -Vector3.UnitZ);
        ZoneInstance again = d.Instance(Orbit);
        Check(again.Visit == 2 && again.Encounters.Count == 0 && d.Records.InZone(Orbit).Count == lasting,
            "coming back starts fresh events, and the records are still there");
        Check(d.Records.GuardTrips == 0, "the cleanup guard never had to step in");
    }

    void PlayerOwnedSurvives()
    {
        var d = new EncounterDirector(Config(), Zones(), Deck(), seed: 5);
        var removed = new HashSet<long>();
        d.Logged += e => { if (e.Kind == "despawn") removed.Add(e.Id); };
        var players = new List<Wanderer> { new(0, Orbit, Vector3.Zero, 31), new(1, Orbit, new Vector3(300, 0, 0), 32) };
        Simulate(d, players, 5);

        // Things players built, dropped or stored, over both zones...
        var owned = new List<(WorldEntity Entity, Vector3 Position, string Zone)>();
        var rng = new Rng(99);
        for (int i = 0; i < 24; i++)
        {
            string zone = i % 2 == 0 ? Orbit : Deep;
            WorldEntity e = d.RegisterPlayerOwned(i % 2, zone, i % 3 == 0 ? EntityKind.PlayerShip : EntityKind.PlayerCargo,
                "cargo-pod", "stash", rng.OnSphere() * rng.Range(500f, 6000f), Vector3.UnitZ);
            owned.Add((e, e.Position, zone));
        }
        // ...some dropped right in the middle of an event...
        Encounter fight = d.DebugSpawn(Orbit, "patrol-vs-reavers", new Vector3(0, 0, 2500), Vector3.Zero);
        foreach (WorldEntity ship in fight.Ships)
        {
            WorldEntity e = d.RegisterPlayerOwned(0, Orbit, EntityKind.PlayerCargo, "cargo-pod", "crate by a fight", ship.Position, Vector3.UnitZ);
            owned.Add((e, e.Position, Orbit));
        }
        // ...touching a live event ship doesn't make it theirs...
        WorldEntity marked = fight.Ships[2];
        d.SetPlayer(0, Orbit, marked.Position + Vector3.UnitY * 50f, Vector3.UnitZ);
        d.Touch(0, marked.Id);
        Check(marked.Touch == TouchKind.Touched && marked.Owner < 0, "touching a live ship mid-event marks it but doesn't make it the player's");
        // ...one taken mid-event with the debug claim, standing in for boarding and hijacking its core...
        WorldEntity claimed = fight.Ships[0];
        d.DebugClaim(0, claimed.Id);
        // ...and one marked owned by some future system that forgets to promote it. Only the guard stands in the way.
        WorldEntity sneaky = fight.Ships[1];
        sneaky.Owner = 1;

        // An hour of players hopping between zones, so both instances open and close over and over.
        for (int hop = 0; hop < 120; hop++)
        {
            Simulate(d, players, 30);
            players[hop % 2].Zone = players[hop % 2].Zone == Orbit ? Deep : Orbit;
        }

        int moved = owned.Count(o => !d.Records.Contains(o.Entity.Id) || o.Entity.Position != o.Position || o.Entity.ZoneId != o.Zone || o.Entity.Owner < 0);
        Check(moved == 0 && owned.All(o => !removed.Contains(o.Entity.Id)),
            $"all {owned.Count} player-owned things are still there, untouched, after 120 zone hops");
        Check(d.Records.Contains(claimed.Id) && claimed.Owner == 0 && !removed.Contains(claimed.Id),
            "the ship taken mid-event (debug claim, standing in for boarding) is still there, and still theirs");
        Check(d.Records.Contains(marked.Id) && marked.Owner < 0 && !removed.Contains(marked.Id), "the touched ship is still there, still not theirs");
        Check(d.Records.Contains(sneaky.Id) && !removed.Contains(sneaky.Id) && d.Records.GuardTrips == 1,
            "a ship marked owned without being promoted was kept by the cleanup guard, not removed");
    }

    void SameSeedSameEvents()
    {
        string a = Scenario(42), b = Scenario(42), c = Scenario(43);
        int lines = a.Count(ch => ch == '\n');
        Check(lines > 500 && a == b, $"the same seed and inputs give the same {lines} log lines (hash {StableHash.Of(a):x16})");
        Check(a != c, $"a different seed gives different events (hash {StableHash.Of(c):x16})");
    }

    void ZonesAndScaling()
    {
        var d = new EncounterDirector(Config(), Zones(), Deck(), seed: 3);
        var players = new List<Wanderer> { new(0, Orbit, Vector3.Zero, 41), new(1, Deep, Vector3.Zero, 42) };
        var spawns = new Dictionary<string, int> { [Orbit] = 0, [Deep] = 0 };
        int strays = 0, wrongLeads = 0, bothOpen = 0, ticks = 0;
        d.Spawned += (zi, enc) =>
        {
            spawns[zi.Zone.Id]++;
            PlayerState focus = d.Player(enc.FocusPlayer);
            if (enc.ZoneId != zi.Zone.Id || focus.ZoneId != zi.Zone.Id) strays++;
            foreach (WorldEntity ship in enc.Ships)
                if (ship.ZoneId != zi.Zone.Id || Vector3.Distance(ship.Position, focus.Position) > d.Config.MaxSpawnDistance + 1500f) strays++;
        };
        d.LeadEmitted += lead => { if (lead.ZoneId != d.Player(lead.PlayerId).ZoneId) wrongLeads++; };
        Simulate(d, players, 900, _ =>
        {
            ticks++;
            if (d.Instance(Orbit) != null && d.Instance(Deep) != null) bothOpen++;
        });
        Check(bothOpen == ticks && spawns[Orbit] >= 10 && spawns[Deep] >= 10 && strays == 0 && wrongLeads == 0,
            $"two players in two zones each get their own events (Veyra {spawns[Orbit]}, Drift {spawns[Deep]}), spawned around them, in their zone");

        var (one, oneAvg, oneShips) = Crowd(1);
        var (four, fourAvg, fourShips) = Crowd(4);
        Check(four > one * 1.3f && fourAvg > oneAvg + 0.5f && fourShips > oneShips,
            Inv($"more players in a zone get more events: 1 player {one} spawns, {oneAvg:F1} events and {oneShips:F1} ships at once; 4 players {four}, {fourAvg:F1} and {fourShips:F1}"));
    }

    void SignalLeads()
    {
        DirectorConfig cfg = Config();
        var d = new EncounterDirector(cfg, Zones(), Deck(), seed: 13);
        d.SetPlayer(0, Deep, Vector3.Zero, -Vector3.UnitZ);
        d.Tick();
        var leads = new List<SignalLead>();
        d.LeadEmitted += lead => { if (lead.Kind == "distress") leads.Add(lead); };
        var rng = new Rng(5);
        for (int i = 0; i < 1000; i++)
            d.DebugSpawn(Deep, "distress-call", rng.AroundHorizon(0.3f) * rng.Range(2600f, 5200f), Vector3.Zero);

        float maxAngle = leads.Max(l => l.AngleErrorDeg), meanAngle = leads.Average(l => l.AngleErrorDeg);
        float maxDist = leads.Max(l => l.DistanceError), meanDist = leads.Average(l => l.DistanceError);
        Check(leads.Count == 1000, $"every distress call emits a lead ({leads.Count})");
        Check(maxAngle <= cfg.Lead.AngleFuzzDeg + 0.01f && maxDist <= cfg.Lead.DistanceFuzz + 1e-4f,
            Inv($"lead direction within {cfg.Lead.AngleFuzzDeg}° (worst {maxAngle:F2}°) and distance within {cfg.Lead.DistanceFuzz:P0} (worst {maxDist:P1})"));
        Check(meanAngle > cfg.Lead.AngleFuzzDeg * 0.4f && meanDist > cfg.Lead.DistanceFuzz * 0.3f,
            Inv($"a lead is never an exact spot (mean error {meanAngle:F2}° and {meanDist:P1})"));

        SignalLead first = leads[0];
        WorldEntity target = d.Instance(Deep).FindShip(first.TargetId);
        bool resolved = false;
        d.Logged += e => { if (e.Kind == "lead-resolved" && e.Id == first.Id) resolved = true; };
        d.SetPlayer(0, Deep, target.Position + new Vector3(0, 0, 400), -Vector3.UnitZ);
        d.Tick();
        Check(resolved && d.Leads.All(l => l.Id != first.Id), "flying to within the resolve distance resolves the lead");
    }

    void TickCost()
    {
        var d = new EncounterDirector(Config(), Zones(), Deck(), seed: 9);
        var players = new List<Wanderer>
        {
            new(0, Orbit, Vector3.Zero, 51), new(1, Orbit, new Vector3(500, 0, 0), 52), new(2, Deep, Vector3.Zero, 53),
            new(3, Deep, new Vector3(0, 0, 900), 54), new(4, Lanes, Vector3.Zero, 55), new(5, Moon, Vector3.Zero, 56),
        };
        Simulate(d, players, 120); // warm up: JIT, first spawns
        d.Stats.Reset();
        long liveShips = 0;
        Simulate(d, players, 1800, _ => { foreach (ZoneInstance zi in d.Instances) liveShips += zi.Ships.Count; });
        double avgShips = (double)liveShips / d.Stats.Ticks;
        GD.Print(Inv($"      {d.Stats.Ticks} ticks, 6 players in 4 zones, {avgShips:F0} live ships on average"));
        GD.Print(Inv($"      director tick: average {d.Stats.AverageMicros:F1} us, max {d.Stats.MaxMicros:F1} us"));
        Check(d.Stats.AverageMicros < 100 && d.Stats.MaxMicros < 2000,
            Inv($"the director's tick stays tiny (average {d.Stats.AverageMicros:F1} us, max {d.Stats.MaxMicros:F1} us at {d.Config.TickHz} Hz)"));
    }

    void VharnAndFreeholders()
    {
        DirectorConfig cfg = Config();
        EventDeck deck = Deck();
        ZoneCatalog zones = Zones();
        ZoneDef deep = zones.Get(Deep), orbit = zones.Get(Orbit), surface = zones.Get(Surface);
        ZoneDef march = zones.Get("vharn-march"), karesh = zones.Get("karesh-surface");
        Vector3 vharnSpace = new(-8500, 0, -7500), holt = new(5200, 0, 3600);

        // The Vharn Dominion: the main enemy, hostile, and only in their own territory.
        var vharn = deck.Events.Where(e => e.Faction == "vharn").ToList();
        Check(vharn.Count >= 3 && vharn.All(e => e.Hostile && e.Territory == TerritoryRule.Inside),
            $"all {vharn.Count} Vharn cards are hostile and need Vharn territory");
        Check(Share(deck, deep, vharnSpace, "vharn-patrol", cfg) > 0f && Share(deck, deep, vharnSpace, "vharn-capital", cfg) > 0f
              && Share(deck, march, Vector3.Zero, "vharn-capital", cfg) > 0f
              && Share(deck, deep, Vector3.Zero, "vharn-patrol", cfg) == 0f && Share(deck, orbit, Vector3.Zero, "vharn-patrol", cfg) == 0f,
            "Vharn patrols and capital ships come only in Vharn space");
        Check(Share(deck, karesh, Vector3.Zero, "vharn-garrison", cfg) > 0f && Share(deck, surface, Vector3.Zero, "vharn-garrison", cfg) == 0f
              && Share(deck, march, Vector3.Zero, "vharn-garrison", cfg) == 0f,
            "Vharn garrison troops patrol only on Vharn worlds' surfaces");
        int inVharn = CountSpawns(Deep, vharnSpace, "vharn-patrol"), outVharn = CountSpawns(Deep, Vector3.Zero, "vharn-patrol");
        Check(inVharn > 0 && outVharn == 0, $"a player in Vharn space meets Vharn patrols ({inVharn} spawns), a player outside never does ({outVharn})");

        // The Freeholders: settlers round their outposts, who never attack.
        var free = deck.Events.Where(e => e.Ships.Any(g => (g.Faction != "" ? g.Faction : e.Faction) == "freeholders")).ToList();
        Check(free.Count >= 2 && free.All(e => !e.Hostile && e.Anchor == "outpost" && e.Territory == TerritoryRule.Inside),
            $"all {free.Count} Freeholder cards are peaceful, at their outposts, in their territory");
        Check(Share(deck, orbit, holt, "freeholder-haulers", cfg) > 0f && Share(deck, orbit, holt, "freeholder-miners", cfg) > 0f
              && Share(deck, orbit, Vector3.Zero, "freeholder-haulers", cfg) == 0f && Share(deck, deep, Vector3.Zero, "freeholder-miners", cfg) == 0f,
            "Freeholder haulers and mining skiffs work only round their outposts");
        int inHolt = CountSpawns(Orbit, holt, "freeholder-haulers") + CountSpawns(Orbit, holt, "freeholder-miners");
        int outHolt = CountSpawns(Orbit, Vector3.Zero, "freeholder-haulers") + CountSpawns(Orbit, Vector3.Zero, "freeholder-miners");
        Check(inHolt > 0 && outHolt == 0, $"a player at Holt outpost meets Freeholders ({inHolt} spawns), a player away from it never does ({outHolt})");

        var near = new List<string>();
        orbit.FactionsAt(holt, cfg.Territory.BorderMargin, near);
        Check(near.Contains("reavers") && near.Contains("freeholders") && Share(deck, orbit, holt, "border-clash", cfg) == 0f,
            "Reaver and Freeholder territory overlap at Holt, but Freeholders never take a side in a border clash");
        string hostileSettlers = _deckJson.Replace("\"faction\": \"freeholders\", \"territory\": \"inside\", \"hostile\": false",
            "\"faction\": \"freeholders\", \"territory\": \"inside\", \"hostile\": true");
        bool refused = false;
        try { EventDeck.Parse(hostileSettlers); }
        catch (FormatException) { refused = true; }
        Check(hostileSettlers != _deckJson && refused, "the deck refuses to load a card where Freeholders attack");
    }

    void ExportFilter()
    {
        string presets = FileAccess.GetFileAsString("res://export_presets.cfg");
        Check(presets.Contains("include_filter=\"*.json\""), "the export preset includes the data files (*.json)");
    }

    void GunneryHook()
    {
        GunneryConfig g = GameData.Gunnery();
        var track = new AimTrack();
        float Shot(double at) => Gunnery.HitChance(g, ShooterClass.VesselGun, TargetClass.SmallVessel, "cannon", 200f, track, at);
        bool warnings = Shot(0.0) == 0f && Shot(0.1) == 0f && Shot(0.2) == 0f && Shot(0.5) == 0f;
        float half = Shot(0.75 + g.RampSeconds * 0.5), full = Shot(0.75 + g.RampSeconds + 0.1);
        float baseline = g.Grades["good"];
        Check(warnings && MathF.Abs(half - baseline * 0.5f) < 0.01f && MathF.Abs(full - baseline) < 0.01f,
            Inv($"first shots are warning misses, then accuracy ramps to the baseline over {g.RampSeconds} s ({half:F2} halfway, {full:F2} after)"));
        track.LineOfSightBroken();
        Check(Shot(10.0) == 0f && Shot(10.1) == 0f, "breaking line of sight brings back the warning shots and the ramp");

        string[,] expected =
        {
            { "good", "fair", "poor", "veryPoor" },
            { "fair", "good", "good", "good" },
            { "veryPoor", "poor", "fair", "veryGood" },
        };
        bool table = true;
        for (int s = 0; s < 3; s++)
            for (int t = 0; t < 4; t++)
                table &= g.BaselineFor((ShooterClass)s, (TargetClass)t) == g.Grades[expected[s, t]];
        Check(table, "the shooter-against-target baseline table matches the design");

        float rail = g.Weapons["rail"].Accuracy, minigun = g.Weapons["minigun"].Accuracy, capital = g.Weapons["capitalCannon"].ProjectileSpeed;
        float Settled(TargetClass target, float speed)
        {
            var aim = new AimTrack();
            for (int i = 0; i < g.WarningShots.Shots; i++) Gunnery.HitChance(g, ShooterClass.CapitalTurret, target, "artillery", speed, aim, i * 0.1);
            return Gunnery.HitChance(g, ShooterClass.CapitalTurret, target, "artillery", speed, aim, 10);
        }
        float vsFighter = Settled(TargetClass.SmallVessel, 250f), vsCapital = Settled(TargetClass.Capital, 10f);
        Check(rail > 1f && minigun < 1f && capital is >= 250f and <= 350f && g.Weapons["cannon"].ProjectileSpeed >= 550f && vsCapital > vsFighter * 5f,
            Inv($"weapons modify it: rails x{rail}, miniguns x{minigun}, capital shells {capital:F0} m/s, artillery {vsCapital:F2} on a slow capital vs {vsFighter:F2} on a fighter"));
    }

    // ---- Scenarios ----------------------------------------------------------------------

    /// <summary>Fraction of hostile spawns in a busy run of a zone at the given danger grade.</summary>
    float HostileFraction(int danger)
    {
        var zone = new ZoneDef { Id = "danger" + danger, Kind = ZoneKind.Orbit, Danger = danger, Radius = 20000, Circles = { Poi("station", 0, -3000, 300) } };
        var d = new EncounterDirector(ChurnConfig(), new ZoneCatalog { Zones = { zone } }, ChurnDeck(), seed: 17);
        int hostile = 0, total = 0;
        d.Spawned += (_, enc) => { total++; if (enc.Def.Hostile) hostile++; };
        Simulate(d, new List<Wanderer> { new(0, zone.Id, Vector3.Zero, 61) }, 3600);
        return (float)hostile / Math.Max(1, total);
    }

    int CountSpawns(string zoneId, Vector3 at, string eventId)
    {
        var d = new EncounterDirector(ChurnConfig(), Zones(), ChurnDeck(), seed: 19);
        int count = 0;
        d.Spawned += (_, enc) => { if (enc.Def.Id == eventId) count++; };
        Simulate(d, new List<Wanderer> { new(0, zoneId, at, 71) { Speed = 0f } }, 3600);
        return count;
    }

    /// <summary>Spawns and average concurrent events and ships in Veyra orbit with a number of players flying about together.</summary>
    (int Spawns, float AvgEvents, float AvgShips) Crowd(int count)
    {
        var d = new EncounterDirector(Config(), Zones(), Deck(), seed: 23);
        var players = Enumerable.Range(0, count).Select(i => new Wanderer(i, Orbit, new Vector3(i * 300f, 0, 0), 81 + (ulong)i)).ToList();
        int spawns = 0;
        long events = 0, ships = 0, ticks = 0;
        d.Spawned += (_, _) => spawns++;
        Simulate(d, players, 3600, _ =>
        {
            ticks++;
            events += d.Instance(Orbit).Encounters.Count;
            ships += d.Instance(Orbit).Ships.Count;
        });
        return (spawns, (float)events / ticks, (float)ships / ticks);
    }

    /// <summary>A fixed script of players, zone hops and touches; everything the director does goes into the returned log.</summary>
    string Scenario(ulong seed)
    {
        var d = new EncounterDirector(Config(), Zones(), Deck(), seed);
        var log = new StringBuilder();
        d.Logged += e => log.AppendLine(e.ToString());
        d.Spawned += (_, enc) =>
        {
            foreach (WorldEntity s in enc.Ships)
                log.AppendLine(Inv($"  ship {s.Id} {s.Hull} {s.Faction} crew {s.Crew} at {s.Position.X:F2} {s.Position.Y:F2} {s.Position.Z:F2}"));
        };
        d.LeadEmitted += l => log.AppendLine(Inv($"  lead {l.Id} {l.Direction.X:F4} {l.Direction.Y:F4} {l.Direction.Z:F4} {l.Distance:F2}"));

        var players = new List<Wanderer> { new(0, Orbit, Vector3.Zero, 91), new(1, Orbit, new Vector3(400, 0, 0), 92), new(2, Deep, Vector3.Zero, 93) };
        for (int minute = 0; minute < 30; minute++)
        {
            Simulate(d, players, 60);
            if (minute == 10) players[1].Zone = Deep;
            if (minute == 20) players[0].Zone = Deep;
            if (minute == 25) players[0].Zone = Orbit;
            // Every other minute player 0 flies up to the first event ship in their zone and touches it.
            if (minute % 2 == 1 && d.Instance(players[0].Zone)?.Ships.FirstOrDefault() is { } ship)
            {
                d.SetPlayer(0, players[0].Zone, ship.Position + new Vector3(0, 80, 0), players[0].Forward);
                log.AppendLine($"  touch {ship.Id}: {d.Touch(0, ship.Id).Status}");
            }
        }
        return log.ToString();
    }

    // ---- Helpers ------------------------------------------------------------------------

    /// <summary>Runs the director tick by tick, moving the players before each tick as the game would.</summary>
    static void Simulate(EncounterDirector d, List<Wanderer> players, double seconds, Action<EncounterDirector> afterTick = null)
    {
        float dt = (float)d.Config.TickSeconds;
        int ticks = (int)Math.Round(seconds / dt);
        for (int t = 0; t < ticks; t++)
        {
            foreach (Wanderer w in players)
            {
                w.Step(dt);
                d.SetPlayer(w.Id, w.Zone, w.Position, w.Forward);
            }
            d.Tick();
            afterTick?.Invoke(d);
        }
    }

    DirectorConfig Config() => DirectorConfig.Parse(_configJson);
    ZoneCatalog Zones() => ZoneCatalog.Parse(_zonesJson);
    EventDeck Deck() => EventDeck.Parse(_deckJson);

    /// <summary>The real rules with the clock turned up: short gaps and cooldowns, so thousands of events come and go.</summary>
    DirectorConfig ChurnConfig()
    {
        DirectorConfig cfg = Config();
        cfg.SpawnGap = new[] { 0.3f, 1.2f };
        cfg.RetryDelay = 0.3f;
        cfg.NoRepeatWindow = 12f;
        foreach (string category in cfg.CategoryCooldowns.Keys.ToList()) cfg.CategoryCooldowns[category] *= 0.1f;
        return cfg;
    }

    /// <summary>The real deck with every event cut short.</summary>
    EventDeck ChurnDeck()
    {
        EventDeck deck = Deck();
        foreach (EventDef def in deck.Events) def.Duration = new[] { 5f, 12f };
        return deck;
    }

    static ZoneCircle Poi(string poi, float x, float z, float r) => new() { Kind = CircleKind.Poi, Poi = poi, Name = poi, X = x, Z = z, R = r };

    static float Share(EventDeck deck, ZoneDef zone, Vector3 at, string id, DirectorConfig cfg) =>
        DeckWeights.Evaluate(deck, zone, at, cfg).First(w => w.Def.Id == id).Share;

    static float Raw(EventDeck deck, ZoneDef zone, Vector3 at, string id, DirectorConfig cfg) =>
        DeckWeights.Evaluate(deck, zone, at, cfg).First(w => w.Def.Id == id).Raw;

    static Gate GateOf(EventDeck deck, ZoneDef zone, string id, DirectorConfig cfg) =>
        DeckWeights.Evaluate(deck, zone, Vector3.Zero, cfg).First(w => w.Def.Id == id).Gate;

    void Check(bool ok, string what)
    {
        _checks++;
        if (ok) GD.Print($"PASS  {what}");
        else Fail(what);
    }

    void Fail(string what)
    {
        _failures++;
        GD.PrintErr($"FAIL  {what}");
    }

    /// <summary>
    /// A scripted player: flies about at a steady speed, turning at random and looking where it
    /// goes, staying in the middle of its zone. Deterministic from its own seed.
    /// </summary>
    sealed class Wanderer
    {
        public readonly int Id;
        public string Zone;
        public Vector3 Position;
        public Vector3 Forward = -Vector3.UnitZ;
        public float Speed = 120f;
        readonly Rng _rng;
        Vector3 _turn;

        public Wanderer(int id, string zone, Vector3 position, ulong seed)
        {
            Id = id;
            Zone = zone;
            Position = position;
            _rng = new Rng(seed);
            Forward = _rng.AroundHorizon(0.4f);
        }

        public void Step(float dt)
        {
            if (_rng.Chance(dt * 0.4f)) _turn = _rng.OnSphere() * 0.9f;
            Forward = SafeNormalize(Forward + _turn * dt, Forward);
            if (Speed > 0f && Flat(Position).Length() > 5000f) // turn back towards the middle
                Forward = SafeNormalize(Vector3.Lerp(Forward, -SafeNormalize(Position, Forward), 0.05f), Forward);
            Position += Forward * Speed * dt;
            Position.Y = Math.Clamp(Position.Y, -1500f, 1500f);
        }
    }
}
