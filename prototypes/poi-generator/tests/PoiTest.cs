using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using PoiGenerator.Gen;

namespace PoiGenerator;

/// <summary>
/// Headless tests for the POI generator. Run with:
///   godot --headless --path prototypes/poi-generator res://tests/PoiTest.tscn
/// Exits 0 if every check passes, 1 if any fails.
/// </summary>
public partial class PoiTest : Node
{
    int _failures;
    PoiLibrary _lib;

    public override void _Ready()
    {
        // "-- --only=Performance,StateTransforms" runs just those checks while iterating.
        string only = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--only="))?["--only=".Length..];
        var checks = new (string Name, Action Run)[]
        {
            ("Data", Data), ("Determinism", Determinism), ("Validation500", Validation500),
            ("AuthoredLayouts", AuthoredLayouts), ("StateTransforms", StateTransforms), ("LootByRarity", LootByRarity),
            ("PlacementRules", PlacementRules), ("NoRepeatsInSystem", NoRepeatsInSystem), ("SeenWeighting", SeenWeighting),
            ("RegrowthPolicy", RegrowthPolicy), ("RevealDistances", RevealDistances), ("TerminalLogs", TerminalLogs),
            ("ShooterTags", ShooterTags), ("Performance", Performance),
        };
        try
        {
            _lib = GameData.Load();
            foreach (var (name, run) in checks)
                if (only == null || only.Split(',').Contains(name)) run();
        }
        catch (Exception e)
        {
            Fail($"exception: {e}");
        }
        // The walk runs on physics ticks, so it goes last; see _PhysicsProcess.
        if (only == null || only.Split(',').Contains("Walk")) StartWalk();
        else Finish();
    }

    void Finish()
    {
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} CHECK(S) FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    // ---- walking it -------------------------------------------------------------------

    // A body really walks each place's critical path, from outside the way in to the vault:
    // through the doors, along the corridors, down every ramp. Proves the meshes, collision
    // and grid path agree.
    readonly (string family, int variant, PoiState state)[] _walks =
    {
        ("vharn_outpost", 0, PoiState.Active),
        ("abandoned_mine", 0, PoiState.Abandoned),
        ("research_lab", 3, PoiState.Ruined),
    };
    int _walk = -1, _step, _ticks, _stuck;
    Node3D _walkRoot;
    Player _walker;
    List<Vector3> _route;
    float _lowest;

    void StartWalk() => NextWalk();

    void NextWalk()
    {
        _walkRoot?.QueueFree();
        _walk++;
        if (_walk >= _walks.Length)
        {
            _walkRoot = null;
            Finish();
            return;
        }
        var (fam, v, state) = _walks[_walk];
        var poi = PoiFactory.Generate(_lib, _lib.Family(fam), v, Rarity.Common, state, 9);
        _walkRoot = new Node3D { Name = "Walk" };
        AddChild(_walkRoot);
        var view = PoiView.Build(poi, Biome.Rock, new PoiView.Options(Lights: false, Figures: false));
        _walkRoot.AddChild(view);
        var hole = new Rect2I(view.PlugMin, view.PlugMax - view.PlugMin + Vector2I.One);
        _walkRoot.AddChild(Terrain.Build(new Vector2I(-160, -160), new Vector2I(40, 40), (x, z) => Colors.Gray, new List<Rect2I> { hole }, true, 0f));
        _walker = new Player { Name = "Walker" };
        _walkRoot.AddChild(_walker);
        view.Watcher = _walker;

        // The route: outside the door, then every cell of the critical path, cell centres.
        _route = poi.CriticalPath.Select(c => new Vector3(c.X + 0.5f, c.Y, c.Z + 0.5f)).ToList();
        var (start, yaw) = view.EntranceStand();
        _walker.Place(start, yaw);
        _step = 0;
        _ticks = 0;
        _stuck = 0;
        _lowest = poi.CriticalPath.Min(c => c.Y) - 1.5f;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_walker == null || _walk < 0 || _walk >= _walks.Length) return;
        _ticks++;
        if (_ticks < 10) return; // let it land
        var (fam, v, state) = _walks[_walk];
        string name = $"{_lib.Family(fam).Variants[v]} ({state})";
        Vector3 at = _walker.GlobalPosition;

        if (at.Y < _lowest)
        {
            Fail($"walking {name}: fell through the floor at {at}");
            _walker = null;
            NextWalk();
            return;
        }
        // Skip ahead along the route as cells are reached; a cell counts when we're over it.
        while (_step < _route.Count && new Vector2(at.X - _route[_step].X, at.Z - _route[_step].Z).Length() < 0.45f && Mathf.Abs(at.Y - _route[_step].Y) < 1.2f)
        {
            _step++;
            _stuck = 0;
        }
        if (_step >= _route.Count)
        {
            Check(true, $"a body walked {name} from outside to the vault: {_route.Count} cells, {_ticks / 60f:F1} s, never below the floor");
            _walker.AutoTarget = null;
            _walker = null;
            NextWalk();
            return;
        }
        _walker.AutoTarget = _route[_step];
        if (++_stuck > 300)
        {
            Fail($"walking {name}: stuck at {at} on cell {_step}/{_route.Count} ({_route[_step]})");
            _walker = null;
            NextWalk();
        }
    }

    // ---- data -------------------------------------------------------------------------

    void Data()
    {
        // Factions by their design ids; Precursor ruins (faction none) don't count as one.
        var byFaction = _lib.Kits.Values.Where(k => k.Faction != Faction.None).GroupBy(k => k.Faction)
            .ToDictionary(g => g.Key, g => g.Sum(k => k.Modules.Count));
        int rich = byFaction.Count(kv => kv.Value >= 6);
        Check(rich >= 4, $"{rich} faction kits with 6+ modules ({string.Join(", ", byFaction.Select(kv => $"{kv.Key.Id()} {kv.Value}"))}), " +
                         $"plus Precursor ruins with {_lib.Kits.Values.Where(k => k.Faction == Faction.None).Sum(k => k.Modules.Count)}");
        var known = new[] { "meridian", "exchange", "vharn", "kessari", "reavers", "freeholders", "hollow" };
        Check(Enum.GetValues<Faction>().Where(f => f != Faction.None).Select(f => f.Id()).SequenceEqual(known)
              && _lib.Families.All(f => f.Faction == Faction.None || known.Contains(f.Faction.Id())),
            "factions use the design ids (meridian, exchange, vharn, kessari, reavers, freeholders, hollow)");
        Check(_lib.Families.Count >= 6 && _lib.Families.All(f => f.Variants.Count is >= 3 and <= 6),
            $"{_lib.Families.Count} families, each with 3 to 6 layouts ({_lib.Families.Sum(f => f.Variants.Count)} layouts)");
    }

    // ---- determinism ------------------------------------------------------------------

    static ulong Fingerprint(PoiInstance p)
    {
        ulong h = 1469598103934665603UL;
        foreach (var (c, b) in p.Blocks.Blocks())
            h = Hash.Combine(h, (ulong)(uint)(c.X * 73856093 ^ c.Y * 19349663 ^ c.Z * 83492791), (ulong)b.Type << 32 | (ulong)b.Color.R << 16 | (ulong)b.Color.G << 8 | b.Color.B);
        foreach (var e in p.Enemies) h = Hash.Combine(h, Hash.Of(e.Name), (ulong)(uint)e.Cell.GetHashCode());
        foreach (var c in p.Caches) h = Hash.Combine(h, (ulong)c.Value);
        foreach (var t in p.Terminals) h = Hash.Combine(h, Hash.Of(t.Text));
        return h;
    }

    void Determinism()
    {
        bool same = true, differ = false;
        foreach (var f in _lib.Families)
            foreach (PoiState s in Enum.GetValues<PoiState>())
            {
                var a = PoiFactory.Generate(_lib, f, 1, Rarity.Uncommon, s, 42);
                var b = PoiFactory.Generate(_lib, f, 1, Rarity.Uncommon, s, 42);
                same &= Fingerprint(a) == Fingerprint(b) && a.Layout.Modules.Count == b.Layout.Modules.Count;
                var c = PoiFactory.Generate(_lib, f, 1, Rarity.Uncommon, s, 42, customSeed: 99);
                differ |= Fingerprint(c) != Fingerprint(a);
            }
        Check(same, "the same seed gives the same layout and dressing, block for block");
        Check(differ, "a different seed gives a different layout");
    }

    // ---- validation -------------------------------------------------------------------

    void Validation500()
    {
        int ok = 0, total = 500;
        var states = Enum.GetValues<PoiState>();
        var problems = new List<string>();
        for (int i = 0; i < total; i++)
        {
            var f = _lib.Families[i % _lib.Families.Count];
            var rarity = (Rarity)(i / _lib.Families.Count % 3);
            var state = states[i / 3 % 4];
            ulong seed = Hash.Combine(0xBEEFUL, (ulong)i);
            try
            {
                var p = PoiFactory.Generate(_lib, f, i % f.Variants.Count, rarity, state, (ulong)i, customSeed: seed);
                var issues = Validator.Check(p);
                if (issues.Count == 0) ok++;
                else if (problems.Count < 8) problems.Add($"{f.Id} seed {seed} {rarity} {state}: {issues[0]}");
            }
            catch (Exception e)
            {
                if (problems.Count < 8) problems.Add($"{f.Id} seed {seed}: {e.Message}");
            }
        }
        foreach (string p in problems) GD.PrintErr("      " + p);
        Check(ok == total, $"{ok}/{total} fresh layouts across all families, rarities and states pass validation " +
                           "(A* reaches vault, core, caches, terminals and key rooms; nothing floats; no overlaps; core placed)");
    }

    void AuthoredLayouts()
    {
        int ok = 0, total = 0;
        foreach (var f in _lib.Families)
            for (int v = 0; v < f.Variants.Count; v++)
                foreach (Rarity r in Enum.GetValues<Rarity>())
                    foreach (PoiState s in Enum.GetValues<PoiState>())
                    {
                        total++;
                        var issues = Validator.Check(PoiFactory.Generate(_lib, f, v, r, s, 7));
                        if (issues.Count == 0) ok++;
                        else GD.PrintErr($"      {f.Variants[v]} {r} {s}: {issues[0]}");
                    }
        Check(ok == total, $"{ok}/{total} authored layouts pass validation in every rarity and state");
    }

    // ---- states -----------------------------------------------------------------------

    const float RuinShare = 0.15f;

    void StateTransforms()
    {
        bool abandonedQuiet = true, infestedBloom = true, ruinedOk = true;
        float minRemoved = 1f;
        foreach (var f in _lib.Families)
            for (int v = 0; v < f.Variants.Count; v++)
            {
                var ab = PoiFactory.Generate(_lib, f, v, Rarity.Rare, PoiState.Abandoned, 3);
                abandonedQuiet &= !ab.Enemies.Any(e => e.Defender || e.Kind is EnemyKind.Guard or EnemyKind.Turret or EnemyKind.Hover);

                var inf = PoiFactory.Generate(_lib, f, v, Rarity.Common, PoiState.Infested, 3);
                int growths = inf.Blocks.Blocks().Count(b => b.Block.Type == BlockType.Growth);
                infestedBloom &= growths > 0 && inf.Enemies.Any(e => e.Kind is EnemyKind.Hollow or EnemyKind.Corrupted);

                var ru = PoiFactory.Generate(_lib, f, v, Rarity.Common, PoiState.Ruined, 3);
                minRemoved = Math.Min(minRemoved, ru.RemovedFraction);
                ruinedOk &= ru.RemovedFraction >= RuinShare && Validator.Check(ru).Count == 0;
            }
        var active = PoiFactory.Generate(_lib, _lib.Families[0], 0, Rarity.Rare, PoiState.Active, 3);
        Check(active.Enemies.Count(e => e.Defender) > 0, $"active places are guarded ({active.Enemies.Count(e => e.Defender)} defenders in a rare {active.Family.Name})");
        Check(abandonedQuiet, "abandoned places have no active guards (only wildlife)");
        Check(infestedBloom, "infested places have Hollow Bloom growths and Hollow or corrupted enemies");
        Check(ruinedOk, $"ruined places lose at least {RuinShare:P0} of their blocks (least {minRemoved:P0}) and keep the loot path open");
    }

    // ---- loot -------------------------------------------------------------------------

    void LootByRarity()
    {
        var means = new double[3];
        foreach (Rarity r in Enum.GetValues<Rarity>())
        {
            double sum = 0;
            int n = 0;
            foreach (var f in _lib.Families)
                for (int i = 0; i < 12; i++)
                {
                    var p = PoiFactory.Generate(_lib, f, i % f.Variants.Count, r, (PoiState)(i % 4), (ulong)(100 + i));
                    sum += p.LootValue;
                    n++;
                }
            means[(int)r] = sum / n;
        }
        Check(means[0] < means[1] && means[1] < means[2],
            $"expected loot value rises with rarity: common {means[0]:F0}, uncommon {means[1]:F0}, rare {means[2]:F0}");
        var every = _lib.Families.All(f => PoiFactory.Generate(_lib, f, 0, Rarity.Common, PoiState.Abandoned, 5).Caches.Any(c => c.InVault && c.Value > 0));
        Check(every, "every place has a vault cache worth the trip");
    }

    // ---- placement --------------------------------------------------------------------

    List<(PlanetMap Map, List<PlacedPoi> Pois)> _systems;

    List<(PlanetMap Map, List<PlacedPoi> Pois)> Systems()
    {
        if (_systems != null) return _systems;
        _systems = new();
        var knowledge = new PlayerKnowledge();
        for (int s = 0; s < 24; s++)
            _systems.AddRange(Placement.PlaceSystem(_lib, (ulong)(5000 + s), new[] { PlanetSize.Small, PlanetSize.Medium, PlanetSize.Large }, knowledge, 0.5f));
        return _systems;
    }

    void PlacementRules()
    {
        var systems = Systems();
        int total = 0, bad = 0;
        string firstBad = "";
        var countsBySize = new Dictionary<PlanetSize, List<int>>();
        foreach (var (map, pois) in systems)
        {
            if (!countsBySize.TryGetValue(map.Size, out var l)) countsBySize[map.Size] = l = new List<int>();
            l.Add(pois.Count);
            for (int i = 0; i < pois.Count; i++)
            {
                var p = pois[i];
                total++;
                var others = pois.Where(o => o != p).ToList();
                string why = "";
                if (!p.Family.Biomes.Contains(map.BiomeAt(p.CellX, p.CellY))) why = "biome";
                else if (!p.Family.Territories.Contains("any") && !p.Family.Territories.Contains(map.TerritoryAt(p.CellX, p.CellY))) why = "territory";
                else if (p.Family.Near.Any(r => !Placement.FeatureWithin(map, r, p.CellX, p.CellY, others))) why = "near rule";
                else if (p.Family.Avoid.Any(r => Placement.FeatureWithin(map, r, p.CellX, p.CellY, others))) why = "avoid rule";
                else if (others.Any(o => Placement.Dist(p.CellX, p.CellY, o.CellX, o.CellY) < Placement.MinSpacing)) why = "spacing";
                else if (others.Any(o => o.Family == p.Family && Placement.Dist(p.CellX, p.CellY, o.CellX, o.CellY) < p.Family.Spacing)) why = "family spacing";
                if (why.Length > 0)
                {
                    bad++;
                    if (firstBad.Length == 0) firstBad = $" (first: {p} breaks {why})";
                }
            }
        }
        Check(total > 100 && bad == 0, $"{total} placements respect biome, territory, near/avoid rules and spacing{firstBad}");

        // Freeholder farms keep away from Vharn garrisons, and garrisons sit near rich deposits.
        float nearestFarmToVharn = float.MaxValue;
        bool vharnNearOre = true;
        foreach (var (map, pois) in systems)
        {
            foreach (var farm in pois.Where(p => p.Family.Id == "freeholder_farm"))
                foreach (var v in pois.Where(p => p.Family.Id == "vharn_outpost"))
                    nearestFarmToVharn = Math.Min(nearestFarmToVharn, Placement.Dist(farm.CellX, farm.CellY, v.CellX, v.CellY));
            foreach (var v in pois.Where(p => p.Family.Id == "vharn_outpost"))
                vharnNearOre &= Placement.FeatureWithin(map, new ProximityRule("deposits", 3), v.CellX, v.CellY, pois);
        }
        Check(nearestFarmToVharn >= 10f, $"farms stay at least 10 cells from Vharn garrisons (closest {nearestFarmToVharn:F1})");
        Check(vharnNearOre, "every Vharn garrison is within 3 cells of a rich deposit");

        double Avg(PlanetSize s) => countsBySize.TryGetValue(s, out var l) ? l.Average() : 0;
        Check(Avg(PlanetSize.Small) < Avg(PlanetSize.Medium) && Avg(PlanetSize.Medium) <= Avg(PlanetSize.Large),
            $"counts scale with planet size: small {Avg(PlanetSize.Small):F1}, medium {Avg(PlanetSize.Medium):F1}, large {Avg(PlanetSize.Large):F1}");
    }

    void NoRepeatsInSystem()
    {
        bool ok = true;
        int systems = 0, places = 0;
        var all = Systems();
        for (int s = 0; s < all.Count; s += 3)
        {
            var keys = all.Skip(s).Take(3).SelectMany(x => x.Pois).Select(p => p.LayoutKey).ToList();
            ok &= keys.Count == keys.Distinct().Count();
            systems++;
            places += keys.Count;
        }
        Check(ok, $"no layout repeats within a system ({systems} systems, {places} places)");
    }

    void SeenWeighting()
    {
        // Direct: one of six layouts already seen.
        var family = _lib.Families.OrderByDescending(f => f.Variants.Count).First();
        var knowledge = new PlayerKnowledge();
        knowledge.Seen.Add(family.Variants[0].Key);
        var rng = new Rng(77);
        var counts = new int[family.Variants.Count];
        const int draws = 6000;
        for (int i = 0; i < draws; i++) counts[Placement.ChooseVariant(family.Variants, knowledge, rng).Index]++;
        double seen = counts[0] / (double)draws, unseen = counts.Skip(1).Average() / draws;
        double expected = 1.0 / (1 + 3.0 * (family.Variants.Count - 1));
        Check(seen < unseen * 0.6 && Math.Abs(seen - expected) < 0.02,
            $"unseen layouts are chosen more often: seen {seen:P1} (expected {expected:P1}) vs unseen {unseen:P1} each, over {draws} draws");

        // Through the whole engine: with half the layouts seen, unseen ones fill most places.
        var half = new PlayerKnowledge();
        foreach (var f in _lib.Families)
            foreach (var v in f.Variants.Where(v => v.Index % 2 == 0)) half.Seen.Add(v.Key);
        int seenPlaced = 0, unseenPlaced = 0;
        for (int s = 0; s < 40; s++)
            foreach (var (_, pois) in Placement.PlaceSystem(_lib, (ulong)(9000 + s), new[] { PlanetSize.Medium }, half))
                foreach (var p in pois)
                    if (half.Seen.Contains(p.LayoutKey)) seenPlaced++;
                    else unseenPlaced++;
        Check(unseenPlaced > seenPlaced * 1.5,
            $"with half the layouts seen, placement picks unseen ones {unseenPlaced} times to {seenPlaced}");
    }

    // ---- regrowth and reveal ----------------------------------------------------------

    void RegrowthPolicy()
    {
        var (map, pois) = Systems().First(s => s.Pois.Select(p => p.Rarity).Distinct().Count() >= 2);
        var common = Systems().SelectMany(s => s.Pois).First(p => p.Rarity == Rarity.Common);
        var uncommon = Systems().SelectMany(s => s.Pois).First(p => p.Rarity == Rarity.Uncommon);
        var rare = Systems().SelectMany(s => s.Pois).First(p => p.Rarity == Rarity.Rare);
        var regrowth = new Regrowth { RegrowSeconds = 600f, PlayerRadius = 500f };
        foreach (var p in new[] { common, uncommon, rare }) regrowth.MarkCleared(p);
        ulong seedBefore = common.SpawnSeed;

        // A player camps next to the common place for an hour: nothing regrows.
        var near = new List<(float, float)> { (common.X + 100f, common.Z) };
        var regrown = new List<PlacedPoi>();
        for (int i = 0; i < 360; i++) regrown.AddRange(regrowth.Tick(10f, near));
        Check(regrown.Count == 0 && regrowth.IsCleared(common), "nothing regrows while a player is near");

        // The player leaves: the common place regrows after the timer, the others never do.
        var far = new List<(float, float)> { (common.X + 5000f, common.Z + 5000f) };
        for (int i = 0; i < 59; i++) regrown.AddRange(regrowth.Tick(10f, far));
        Check(regrown.Count == 0, "nothing regrows before the timer runs out");
        for (int i = 0; i < 400; i++) regrown.AddRange(regrowth.Tick(10f, far));
        Check(regrown.Count == 1 && regrown[0] == common && common.SpawnSeed != seedBefore && !regrowth.IsCleared(common),
            "the common place regrows with fresh dressing once nobody is near");
        Check(regrowth.IsCleared(uncommon) && regrowth.IsCleared(rare), "uncommon and rare places never regrow");
        _ = map;
        _ = pois;
    }

    void RevealDistances()
    {
        var (map, pois) = Systems().Where(s => s.Pois.Any(p => p.Reveal.Landmark)).OrderByDescending(s => s.Pois.Count).First();
        var reveal = new MapReveal();
        // Arrive in orbit: only landmarks show.
        var fromOrbit = reveal.Update(pois, map.Width * map.CellSize / 2, map.Height * map.CellSize / 2, 1200f);
        Check(fromOrbit.Count > 0 && fromOrbit.All(p => p.Reveal.Landmark), $"from orbit only landmarks show ({fromOrbit.Count})");

        // Fly low along a line: each place shows only once we are inside its reveal distance.
        bool honest = true;
        var target = pois.First(p => !p.Reveal.Landmark);
        for (float t = 0; t <= 1f; t += 0.002f)
        {
            float x = target.X - 1500f + 3000f * t, z = target.Z + 40f;
            foreach (var p in reveal.Update(pois, x, z, 30f))
            {
                float d = MathF.Sqrt((p.X - x) * (p.X - x) + (p.Z - z) * (p.Z - z) + 900f);
                honest &= p.Reveal.Landmark || d <= p.Reveal.Distance + 0.01f;
            }
        }
        Check(honest && reveal.IsRevealed(target), $"places appear only within their reveal distance ({target.Family.Name}: {target.Reveal.Distance} m)");
        var hidden = pois.Where(p => !p.Reveal.Landmark && !reveal.IsRevealed(p)).ToList();
        bool untouched = hidden.All(p => !MapReveal.CanSee(p, target.X, target.Z + 40f, 30f, out _));
        Check(untouched, $"places we never got close to stay hidden ({hidden.Count} still hidden)");
    }

    // ---- logs and tags ----------------------------------------------------------------

    void TerminalLogs()
    {
        var logs = new List<TerminalLog>();
        foreach (var f in _lib.Families)
            foreach (PoiState s in Enum.GetValues<PoiState>())
                for (int i = 0; i < 4; i++)
                    logs.AddRange(PoiFactory.Generate(_lib, f, i, Rarity.Common, s, (ulong)(300 + i)).Terminals);
        int cut = logs.Count(l => l.MidSentence);
        Check(logs.Count > 0 && logs.All(l => l.Text.Length > 20), $"{logs.Count} terminal logs generated");
        Check(cut > logs.Count / 4 && cut < logs.Count, $"{cut} of {logs.Count} logs stop mid-sentence");
        var sample = logs.First(l => l.MidSentence);
        GD.Print("      e.g. " + sample.Text.Replace("\n\n", " | ").Replace("\n", " "));
    }

    void ShooterTags()
    {
        var armed = _lib.Rosters.Values.SelectMany(r => r).Where(e => e.Kind is EnemyKind.Guard or EnemyKind.Turret or EnemyKind.Hover).ToList();
        Check(armed.All(e => e.Shooter != ShooterClass.None && e.Weapon.Length > 0),
            $"every armed defender and turret carries a shooter class and a weapon ({armed.Count} roster lines)");
        Check(_lib.Rosters.Values.SelectMany(r => r).Where(e => e.Kind == EnemyKind.Turret).All(e => e.Shooter is ShooterClass.Capital or ShooterClass.SmallVessel),
            "turrets are small-vessel or capital class");
    }

    // ---- performance ------------------------------------------------------------------

    void Performance()
    {
        var mats = new BlockMaterials();
        Prof.Ticks.Clear();
        var sw = Stopwatch.StartNew();
        double gen = 0, val = 0, mesh = 0;
        long blocks = 0, quads = 0;
        for (int i = 0; i < 100; i++)
        {
            var f = _lib.Families[i % _lib.Families.Count];
            long t0 = sw.ElapsedTicks;
            var p = PoiFactory.Generate(_lib, f, i % f.Variants.Count, (Rarity)(i % 3), (PoiState)(i % 4), (ulong)i, customSeed: (ulong)(77 + i));
            long t1 = sw.ElapsedTicks;
            using (Prof.Time("validate")) Validator.Check(p);
            long t2 = sw.ElapsedTicks;
            var g = p.Blocks;
            List<ChunkMesh> chunks;
            using (Prof.Time("mesh arrays")) chunks = Mesher.Build(g, p.Layout.Occupied, new MeshOptions { RockMin = g.Min, RockMax = g.Max, RockBottom = g.Min.Y });
            using (Prof.Time("godot meshes"))
                foreach (var c in chunks)
                {
                    MeshBuild.ToMesh(c, mats.BySurface);
                    if (c.Collision.Count > 0) MeshBuild.ToShape(c);
                    quads += c.Quads;
                }
            long t3 = sw.ElapsedTicks;
            gen += t1 - t0;
            val += t2 - t1;
            mesh += t3 - t2;
            blocks += g.CountBlocks();
        }
        double ms(double ticks) => ticks * 1000.0 / Stopwatch.Frequency;
        GD.Print($"PERF  100 POIs: generate {ms(gen):F0} ms, validate {ms(val):F0} ms, mesh {ms(mesh):F0} ms, " +
                 $"total {ms(gen + val + mesh):F0} ms ({ms(gen + val + mesh) / 100:F1} ms each)");
        GD.Print($"PERF  average {blocks / 100} blocks and {quads / 100} quads per POI");
        GD.Print("PERF  generation phases over the whole run: " +
                 string.Join(", ", Prof.Ticks.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {Prof.Ms(kv.Value):F0} ms")));
        Check(ms(gen + val + mesh) / 100 < 100, "generating, validating and meshing a POI takes under 100 ms");
    }

    void Check(bool ok, string what)
    {
        if (ok) GD.Print($"PASS  {what}");
        else Fail(what);
    }

    void Fail(string what)
    {
        _failures++;
        GD.PrintErr($"FAIL  {what}");
    }
}
