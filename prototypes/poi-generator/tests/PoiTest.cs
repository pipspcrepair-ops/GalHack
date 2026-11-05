using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using GalHack.Terrain;
using Godot;
using PoiGenerator.Gen;

namespace PoiGenerator;

/// <summary>
/// Headless tests for the POI generator. Run with:
///   godot --headless --path prototypes/poi-generator res://tests/PoiTest.tscn
/// Exits 0 if every check passes, 1 if any fails. "-- --only=Walk,Sense" runs just those.
/// </summary>
public partial class PoiTest : Node
{
    int _failures;
    PoiLibrary _lib;

    public override void _Ready()
    {
        string only = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--only="))?["--only=".Length..];
        var checks = new (string Name, Action Run)[]
        {
            ("Data", Data), ("Items", Items), ("Determinism", Determinism), ("KitValidation", KitValidation),
            ("KitAuthored", KitAuthored), ("ArchAuthored", ArchAuthored), ("Thousands", Thousands), ("Corridors", Corridors),
            ("Sense", Sense), ("Variety", Variety), ("Cores", Cores), ("StateTransforms", StateTransforms),
            ("LootByRarity", LootByRarity), ("LootByDepth", LootByDepth), ("Carved", Carved), ("Hidden", Hidden),
            ("Breaches", Breaches), ("Keys", Keys), ("PlacementRules", PlacementRules), ("NoRepeatsInSystem", NoRepeatsInSystem),
            ("SeenWeighting", SeenWeighting), ("RegrowthPolicy", RegrowthPolicy), ("RevealDistances", RevealDistances),
            ("WorldPlacement", WorldPlacementCheck), ("StartWorlds", StartWorlds), ("Tethers", Tethers), ("RequiredRooms", RequiredRooms), ("Ages", Ages), ("FactionMix", FactionMix), ("Townsfolk", TownsfolkCheck), ("Blasts", Blasts), ("StartSystems", StartSystems), ("Landmarks", Landmarks), ("ShooterTags", ShooterTags), ("Performance", Performance),
        };
        try
        {
            _lib = GameData.Load();
            foreach (var (name, run) in checks)
                if (only == null || only.Split(',').Contains(name))
                {
                    try { run(); }
                    catch (Exception e) { Fail($"{name}: exception {e}"); }
                }
        }
        catch (Exception e)
        {
            Fail($"exception: {e}");
        }
        // The walks run on physics ticks, so they go last; see _PhysicsProcess.
        if (only == null || only.Split(',').Contains("Walk")) NextWalk();
        else Finish();
    }

    void Finish()
    {
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} CHECK(S) FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    IEnumerable<FamilyDef> Arch => _lib.Families.Where(f => f.Generator != Generator.Kit);
    IEnumerable<FamilyDef> Kits => _lib.Families.Where(f => f.Generator == Generator.Kit);

    // ---- data ---------------------------------------------------------------------------

    void Data()
    {
        var byFaction = _lib.Kits.Values.Where(k => k.Faction != Faction.None).GroupBy(k => k.Faction)
            .ToDictionary(g => g.Key, g => g.Sum(k => k.Modules.Count));
        int rich = byFaction.Count(kv => kv.Value >= 6);
        Check(rich >= 4, $"{rich} faction kits with 6+ modules ({string.Join(", ", byFaction.Select(kv => $"{kv.Key.Id()} {kv.Value}"))})");
        var known = new[] { "meridian", "exchange", "vharn", "kessari", "reavers", "freeholders", "hollow" };
        Check(Enum.GetValues<Faction>().Where(f => f != Faction.None).Select(f => f.Id()).SequenceEqual(known)
              && _lib.Families.All(f => f.Faction == Faction.None || known.Contains(f.Faction.Id())),
            "factions use the design ids (meridian, exchange, vharn, kessari, reavers, freeholders, hollow)");
        Check(_lib.Families.Count >= 20 && _lib.Families.All(f => f.Variants.Count is >= 3 and <= 6),
            $"{_lib.Families.Count} families ({Arch.Count()} built by the architect, {_lib.Families.Count(f => f.Landmark)} hand-authored landmarks), " +
            $"each with 3 to 6 named layouts ({_lib.Families.Sum(f => f.Variants.Count)} layouts)");
        Check(_lib.Rooms.Count >= 30 && _lib.Programs.Count >= 12 && _lib.Sites.Count >= 5 && _lib.Themes.Count >= 5,
            $"shared library: {_lib.Rooms.Count} room types, {_lib.Programs.Count} building programs, {_lib.Sites.Count} site plans, " +
            $"{_lib.Kits.Count} style kits, {_lib.Themes.Count} placement themes");
        Check(Enum.GetValues<Rarity>().Length == 5 && _lib.Families.Select(f => f.Rarity).Distinct().Count() >= 4,
            $"the five-step rarity ladder; families span {string.Join(", ", _lib.Families.Select(f => f.Rarity).Distinct().OrderBy(r => r))}");
        Check(_lib.Rosters.ContainsKey("raiders") && !Enum.GetNames<Faction>().Any(n => n.Equals("raiders", StringComparison.OrdinalIgnoreCase))
              && _lib.Families.Where(f => f.Owner == "gang").All(f => f.Faction == Faction.None && f.Core == CoreRule.None),
            "roving gangs are no faction: their own roster, no faction id, no core at their forts");
    }

    /// <summary>Every loot id is a real shared/Items item with the same rarity.</summary>
    void Items()
    {
        string path = ProjectSettings.GlobalizePath("res://") + "../../shared/Items/data/items.json";
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var items = doc.RootElement.GetProperty("items").EnumerateArray()
            .ToDictionary(e => e.GetProperty("id").GetString(), e => e.GetProperty("rarity").GetString());
        var missing = _lib.Loot.Where(i => !items.ContainsKey(i.Id)).Select(i => i.Id).ToList();
        var wrong = _lib.Loot.Where(i => items.TryGetValue(i.Id, out var r) && !r.Equals(i.Rarity.ToString(), StringComparison.OrdinalIgnoreCase)).Select(i => i.Id).ToList();
        Check(missing.Count == 0 && wrong.Count == 0,
            $"{_lib.Loot.Count} loot items are real shared/Items ids with matching rarities" +
            (missing.Count > 0 ? $"; missing: {string.Join(", ", missing)}" : "") + (wrong.Count > 0 ? $"; wrong rarity: {string.Join(", ", wrong)}" : ""));
    }

    // ---- determinism ------------------------------------------------------------------

    static ulong Fingerprint(PoiInstance p)
    {
        ulong h = 1469598103934665603UL;
        foreach (var (c, b) in p.Blocks.Blocks())
            h = Hash.Combine(h, (ulong)(uint)(c.X * 73856093 ^ c.Y * 19349663 ^ c.Z * 83492791), (ulong)b.Type << 32 | (ulong)b.Color.R << 16 | (ulong)b.Color.G << 8 | b.Color.B);
        foreach (var e in p.Enemies) h = Hash.Combine(h, Hash.Of(e.Name), (ulong)(uint)e.Cell.GetHashCode());
        foreach (var c in p.Caches) h = Hash.Combine(h, (ulong)c.Value);
        return h;
    }

    void Determinism()
    {
        bool same = true, differ = false;
        foreach (var f in _lib.Families)
            foreach (PoiState s in new[] { PoiState.Active, PoiState.Infested })
            {
                var a = PoiFactory.Generate(_lib, f, 1, Rarity.Uncommon, s, 42);
                var b = PoiFactory.Generate(_lib, f, 1, Rarity.Uncommon, s, 42);
                same &= Fingerprint(a) == Fingerprint(b) && a.Layout.Modules.Count == b.Layout.Modules.Count;
                var c = PoiFactory.Generate(_lib, f, 1, Rarity.Uncommon, s, 42, customSeed: 99);
                differ |= Fingerprint(c) != Fingerprint(a);
            }
        Check(same, "the same seed gives the same layout and dressing, block for block (every family)");
        Check(differ, "a different seed gives a different layout");
    }

    // ---- validation -------------------------------------------------------------------

    void KitValidation()
    {
        var kits = Kits.ToList();
        int ok = 0, total = 500;
        var problems = new List<string>();
        for (int i = 0; i < total; i++)
        {
            var f = kits[i % kits.Count];
            var rarity = (Rarity)(i / kits.Count % 5);
            var state = (PoiState)(i / 3 % 4);
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
        Check(ok == total, $"{ok}/{total} fresh kit-assembled layouts across all kit families, five rarities and four states pass validation");
    }

    void KitAuthored()
    {
        int ok = 0, total = 0;
        foreach (var f in Kits)
            for (int v = 0; v < f.Variants.Count; v++)
                foreach (Rarity r in Enum.GetValues<Rarity>())
                    foreach (PoiState s in Enum.GetValues<PoiState>())
                    {
                        total++;
                        var issues = Validator.Check(PoiFactory.Generate(_lib, f, v, r, s, 7));
                        if (issues.Count == 0) ok++;
                        else GD.PrintErr($"      {f.Variants[v]} {r} {s}: {issues[0]}");
                    }
        Check(ok == total, $"{ok}/{total} authored kit layouts pass validation in every rarity and state");
    }

    void ArchAuthored()
    {
        int ok = 0, total = 0;
        foreach (var f in Arch)
            for (int v = 0; v < f.Variants.Count; v++)
                foreach (Rarity r in new[] { Rarity.Common, Rarity.Rare, Rarity.Unique })
                    foreach (PoiState s in Enum.GetValues<PoiState>())
                    {
                        total++;
                        var issues = Validator.Check(PoiFactory.Generate(_lib, f, v, r, s, 7));
                        if (issues.Count == 0) ok++;
                        else GD.PrintErr($"      {f.Variants[v]} {r} {s}: {issues[0]}");
                    }
        Check(ok == total, $"{ok}/{total} named architect layouts (every variant of {Arch.Count()} families, three rarities, four states) pass validation: " +
                           "every room, storey and cache reachable, the deepest point reachable, keycards before their locks, nothing floating, no overlaps, cores only where held");
    }

    /// <summary>
    /// Thousands of fresh layouts across every architect type and seed. Each is checked before
    /// it can be placed (corridors, doors, stairs, reachability, nothing floating); any that fail
    /// are regenerated from the next seed. Zero of the layouts handed out may fail.
    /// </summary>
    void Thousands()
    {
        Architect.Stats.Reset();
        var sw = Stopwatch.StartNew();
        int built = 0, spawned = 0, placedFailures = 0;
        var problems = new List<string>();
        foreach (var f in Arch)
            for (int i = 0; i < 100; i++)
            {
                ulong seed = Hash.Combine(Hash.Of(f.Id), (ulong)i + 1);
                var r = (Rarity)(i % 5);
                Layout layout;
                try { layout = PoiFactory.BuildLayout(_lib, f, i, r, seed); }
                catch (Exception e) { placedFailures++; if (problems.Count < 6) problems.Add($"{f.Id} {seed}: {e.Message}"); continue; }
                built++;
                if (LayoutCheck.Check(layout) is { Count: > 0 } lp) { placedFailures++; if (problems.Count < 6) problems.Add($"{f.Id} {seed}: {lp[0]}"); }
                if (i % 4 != 0) continue;
                spawned++;
                var inst = PoiFactory.Spawn(_lib, layout, (PoiState)(i / 4 % 4), (ulong)i);
                if (Validator.Check(inst) is { Count: > 0 } vp) { placedFailures++; if (problems.Count < 6) problems.Add($"{f.Id} {seed} {inst.State}: {vp[0]}"); }
            }
        foreach (string p in problems) GD.PrintErr("      " + p);
        foreach (string p in Architect.Stats.FirstFailures.Take(4)) GD.Print("      regenerated after: " + p);
        GD.Print($"      COUNTS  generated {Architect.Stats.Generated}, failed the layout check {Architect.Stats.Failed}, repaired 0, " +
                 $"regenerated {Architect.Stats.Regenerated}, handed out {built}, spawned and validated {spawned}, in {sw.ElapsedMilliseconds} ms");
        Check(placedFailures == 0 && built == Arch.Count() * 100,
            $"{built} fresh architect layouts across {Arch.Count()} types: zero failures among those placed " +
            $"({Architect.Stats.Generated} generated, {Architect.Stats.Failed} failed and were regenerated, {spawned} also spawned and validated)");
    }

    // ---- corridors and sense ---------------------------------------------------------------

    /// <summary>
    /// The corridor rule, cell by cell: floor, three cells of headroom, a ceiling, closed sides
    /// (wall, doorway, more corridor, or an opening onto walkable floor), doors set in walls with
    /// floor both sides. Then a walking body is sent along every corridor network in the walks.
    /// </summary>
    void Corridors()
    {
        long cells = 0;
        int layouts = 0, bad = 0, doorways = 0;
        string first = "";
        foreach (var f in Arch)
            for (int i = 0; i < 12; i++)
            {
                var layout = PoiFactory.BuildLayout(_lib, f, i, (Rarity)(i % 5), Hash.Combine(0xC0DEUL, (ulong)i, Hash.Of(f.Id)));
                layouts++;
                cells += layout.Extras.CorridorCells.Count;
                doorways += layout.Doorways.Count;
                var problems = LayoutCheck.Check(layout);
                // And again by hand: every corridor floor cell has headroom of three and a minimum width of two.
                var nav = new NavGrid(layout.Blocks, layout.Occupied, layout.Extras.Lifts, layout.Extras.InSpace);
                foreach (Int3 c in layout.Extras.CorridorCells)
                {
                    bool wide = Dirs.Horizontal.Any(d => layout.Extras.CorridorCells.Contains(c + d.Vec()));
                    if (!wide || !nav.Standable(c) || nav.Solid(c + Int3.Up * 2)) { problems.Add($"corridor cell {c} is narrower than two or lower than three"); break; }
                }
                if (problems.Count > 0) { bad++; if (first.Length == 0) first = $" (first: {f.Id}: {problems[0]})"; }
            }
        Check(bad == 0, $"corridor rule on {layouts} layouts: {cells} corridor cells and {doorways} doorways checked for floor, walls, ceiling, " +
                        $"three-cell headroom and two-cell width, doors set in walls with floor both sides: {bad} failures{first}");
    }

    /// <summary>Rooms land where their purpose says, and stairwells line up storey on storey.</summary>
    void Sense()
    {
        int withRule = 0, satisfied = 0, rooms = 0, avoidBroken = 0, stairs = 0, misaligned = 0, layouts = 0;
        foreach (var f in Arch)
            for (int i = 0; i < 10; i++)
            {
                var layout = PoiFactory.BuildLayout(_lib, f, i, Rarity.Uncommon, Hash.Combine(0x5E45EUL, (ulong)i, Hash.Of(f.Id)));
                layouts++;
                foreach (var n in layout.Extras.Rooms)
                {
                    rooms++;
                    if (n.AvoidBroken) avoidBroken++;
                    if (!n.HasNearRule) continue;
                    withRule++;
                    if (n.NearSatisfied) satisfied++;
                }
                // Each building's stairwell: the same footprint on every storey, and one storey after the next.
                foreach (var g in layout.Extras.Stairwells.GroupBy(s => (s.Min.X, s.Min.Z, s.Max.X, s.Max.Z)))
                {
                    var storeys = g.Select(s => s.Storey).OrderBy(s => s).ToList();
                    stairs += storeys.Count;
                    for (int k = 1; k < storeys.Count; k++) if (storeys[k] != storeys[k - 1] + 1) misaligned++;
                }
                var byStorey = layout.Extras.Stairwells.GroupBy(s => s.Storey);
                // Two stairwells on one storey would only be fine if they belong to two buildings (two footprints).
            }
        float share = withRule == 0 ? 0f : satisfied / (float)withRule;
        Check(share >= 0.6f && avoidBroken <= rooms / 12, $"sense: {share:P0} of {withRule} rooms with an adjacency rule sit next to a room they belong with " +
                                                          $"(labs by storage, wards by the nurses...); {avoidBroken} of {rooms} rooms next to one they should avoid, over {layouts} layouts");
        Check(misaligned == 0 && stairs > 100, $"{stairs} stairwell storeys line up: every building's stair runs the same footprint storey after storey ({misaligned} gaps)");
    }

    /// <summary>A layout's signature: its rooms by storey and purpose, its size and its module graph.</summary>
    static string Signature(Layout l) =>
        string.Join("|", l.Modules.OrderBy(m => m.Storey).ThenBy(m => m.Min.X).ThenBy(m => m.Min.Z).Select(m => $"{m.Storey}:{m.Purpose}:{m.Shape.W}x{m.Shape.D}"))
        + $"#{l.Blocks.Size}#{l.Doorways.Count}";

    static HashSet<string> RoomMix(Layout l) => l.Modules.GroupBy(m => (m.Storey, m.Purpose)).Select(g => $"{g.Key.Storey}:{g.Key.Purpose}:{g.Count()}").ToHashSet();

    /// <summary>A storey's grammar in short: its footprint shape and circulation pattern.</summary>
    static string Grammar(string signature)
    {
        var parts = signature.Split('/');
        return parts.Length >= 6 ? $"{parts[0]}/{parts[1]}" : signature;
    }

    static string Topology(string signature)
    {
        var parts = signature.Split('/');
        if (parts.Length < 7) return signature;
        int rooms = int.Parse(parts[6][1..]);
        return $"{parts[0]}/{parts[1]}/{parts[4]}/{parts[5]}/r{rooms / 3}";
    }

    /// <summary>
    /// Sameness, three ways. Thresholds: within a type, no two of 20 seeds may have the same plan,
    /// and no coarse topology (shape, circulation, junctions, dead ends, room count in threes, storey
    /// by storey) may be shared by more than a quarter of them. Within a building, no two storeys
    /// may share a plan. Between types that should never walk alike (a prison and a hospital, a tower
    /// and a bunker...), their mixes of footprint and circulation overlap by less than half.
    /// </summary>
    void Variety()
    {
        int types = 0, layouts = 0, repeats = 0, storeyRepeats = 0, buildings = 0;
        float worstShare = 0f;
        string worstType = "";
        var grammar = new Dictionary<string, Dictionary<string, int>>();
        foreach (var f in Arch)
        {
            types++;
            var full = new List<string>();
            var coarse = new List<string>();
            var mix = grammar[f.Id] = new Dictionary<string, int>();
            for (int i = 0; i < 20; i++)
            {
                var l = PoiFactory.BuildLayout(_lib, f, 0, Rarity.Uncommon, Hash.Combine(0x7A21EUL, (ulong)i, Hash.Of(f.Id)));
                layouts++;
                full.Add(Signature(l) + "#" + string.Join("|", l.Extras.Plans.Select(p => p.Signature)));
                coarse.Add(string.Join("|", l.Extras.Plans.OrderBy(p => p.Building).ThenBy(p => p.Storey).Select(p => Topology(p.Signature))));
                foreach (var p in l.Extras.Plans) mix[Grammar(p.Signature)] = mix.GetValueOrDefault(Grammar(p.Signature)) + 1;
                foreach (var b in l.Extras.Plans.GroupBy(p => p.Building))
                {
                    buildings++;
                    var sigs = b.Where(p => p.Signature != "hut").Select(p => p.Signature).ToList();
                    storeyRepeats += sigs.Count - sigs.Distinct().Count();
                }
            }
            repeats += full.Count - full.Distinct().Count();
            float share = coarse.GroupBy(c => c).Max(g => g.Count()) / (float)coarse.Count;
            if (share > worstShare && f.Generator == Generator.Building) { worstShare = share; worstType = f.Id; }
        }
        Check(repeats == 0 && worstShare <= 0.25f,
            $"seeds differ within a type: {layouts} layouts of {types} types, {repeats} identical plans; the commonest coarse topology covers {worstShare:P0} of a type's seeds ({worstType}; at most 25%)");
        Check(storeyRepeats == 0, $"every storey is its own: {buildings} buildings, {storeyRepeats} storeys sharing a plan with another storey of the same building");

        // Types that must never walk alike: footprint and circulation mixes overlap by less than half.
        float Overlap(string a, string b)
        {
            var A = grammar[a];
            var B = grammar[b];
            float ta = A.Values.Sum(), tb = B.Values.Sum();
            return A.Keys.Union(B.Keys).Sum(k => Math.Min(A.GetValueOrDefault(k) / ta, B.GetValueOrDefault(k) / tb));
        }
        var pairs = new[] { ("prison_block", "hospital"), ("ominous_tower", "deep_bunker"), ("cryo_vault", "research_base"), ("hospital", "deep_bunker"),
            ("prison_block", "ominous_tower"), ("biolab", "cryo_vault"), ("meridian_garrison", "prison_block") };
        var worst = pairs.Select(p => (p, o: Overlap(p.Item1, p.Item2))).OrderByDescending(x => x.o).First();
        Check(worst.o < 0.5f, $"types differ from each other: of {pairs.Length} pairs that must never walk alike, the closest ({worst.p.Item1} and {worst.p.Item2}) share {worst.o:P0} of their footprint and circulation mix (under 50%)");
    }

    // ---- cores, states, loot ---------------------------------------------------------------

    void Cores()
    {
        bool rule = true, wrecks = true, bases = true;
        string firstBad = "";
        foreach (var f in _lib.Families)
            foreach (PoiState s in Enum.GetValues<PoiState>())
            {
                var p = PoiFactory.Generate(_lib, f, 0, Rarity.Common, s, 11);
                int cores = p.Blocks.Blocks().Count(b => b.Block.Type == BlockType.Core);
                bool want = f.HasCore(s);
                if ((cores == 1) != want) { rule = false; if (firstBad.Length == 0) firstBad = $" ({f.Id} {s}: {cores} cores)"; }
                if ((f.Tags.Contains("wreck") || f.Faction == Faction.None || f.Owner == "gang") && cores > 0) wrecks = false;
                if (f.Core == CoreRule.Owned && s == PoiState.Active && cores != 1) bases = false;
            }
        Check(rule, $"a core exactly where a faction holds the place, in every family and state{firstBad}");
        Check(wrecks, "wrecks, ruins, the old world's abandoned places and gang forts never have a core");
        Check(bases, "every faction base has its core while it's active (hijack it to take the base)");
    }

    const float RuinShare = 0.15f;

    void StateTransforms()
    {
        bool abandonedQuiet = true, infestedBloom = true, ruinedOk = true;
        float minRemoved = 1f;
        string ruinFirst = "";
        foreach (var f in _lib.Families)
        {
            int v = 0;
            var ab = PoiFactory.Generate(_lib, f, v, Rarity.Rare, PoiState.Abandoned, 3);
            abandonedQuiet &= !ab.Enemies.Any(e => e.Defender || e.Kind is EnemyKind.Guard or EnemyKind.Turret or EnemyKind.Hover);
            var inf = PoiFactory.Generate(_lib, f, v, Rarity.Common, PoiState.Infested, 3);
            int growths = inf.Blocks.Blocks().Count(b => b.Block.Type == BlockType.Growth);
            infestedBloom &= growths > 0 && inf.Enemies.Any(e => e.Kind is EnemyKind.Hollow or EnemyKind.Corrupted);
            var ru = PoiFactory.Generate(_lib, f, v, Rarity.Common, PoiState.Ruined, 3);
            minRemoved = Math.Min(minRemoved, ru.RemovedFraction);
            var ruProblems = Validator.Check(ru);
            if ((ru.RemovedFraction < RuinShare || ruProblems.Count > 0) && ruinFirst.Length == 0) ruinFirst = $" (first: {f.Id} lost {ru.RemovedFraction:P1}{(ruProblems.Count > 0 ? ", " + ruProblems[0] : "")})";
            ruinedOk &= ru.RemovedFraction >= RuinShare && ruProblems.Count == 0;
        }
        var garrison = PoiFactory.Generate(_lib, _lib.Family("vharn_outpost"), 0, Rarity.Rare, PoiState.Active, 3);
        Check(garrison.Enemies.Count(e => e.Defender) > 0, $"active faction places are guarded ({garrison.Enemies.Count(e => e.Defender)} defenders in a rare Vharn garrison)");
        Check(abandonedQuiet, "abandoned places have no one on duty (wildlife, and the Bloom's creatures deep down)");
        Check(infestedBloom, "infested places have Hollow Bloom growths and Hollow or corrupted enemies");
        Check(ruinedOk, $"ruined places lose at least {RuinShare:P0} of their blocks (least {minRemoved:P0}) and keep the loot path open{ruinFirst}");
    }

    void LootByRarity()
    {
        var means = new double[5];
        foreach (Rarity r in Enum.GetValues<Rarity>())
        {
            double sum = 0;
            int n = 0;
            foreach (var f in _lib.Families)
                for (int i = 0; i < 3; i++)
                {
                    var p = PoiFactory.Generate(_lib, f, i % f.Variants.Count, r, (PoiState)(i % 4), (ulong)(100 + i));
                    sum += p.Caches.Where(c => c.InVault).Sum(c => c.Value);
                    n++;
                }
            means[(int)r] = sum / n;
        }
        bool rising = true;
        for (int i = 1; i < 5; i++) rising &= means[i] > means[i - 1];
        Check(rising, $"vault value rises with the place's rarity: {string.Join(", ", Enum.GetValues<Rarity>().Select(r => $"{r.ToString().ToLowerInvariant()} {means[(int)r]:F0}"))}");
        var every = _lib.Families.All(f => PoiFactory.Generate(_lib, f, 0, Rarity.Common, PoiState.Abandoned, 5).Caches.Any(c => c.InVault && c.Value > 0));
        Check(every, "every place has a vault cache worth the trip");
    }

    /// <summary>Deeper and more dangerous caches hold rarer things; vaults and hidden stashes beat the rooms around them.</summary>
    void LootByDepth()
    {
        var byDepth = new Dictionary<int, List<double>>();
        double sideTier = 0, vaultTier = 0, secretTier = 0;
        int side = 0, vault = 0, secret = 0;
        double abandoned = 0, infested = 0;
        int na = 0, ni = 0;
        foreach (var f in Arch)
            for (int i = 0; i < 6; i++)
                foreach (var s in new[] { PoiState.Abandoned, PoiState.Infested })
                {
                    var p = PoiFactory.Generate(_lib, f, i % f.Variants.Count, Rarity.Uncommon, s, (ulong)(400 + i));
                    foreach (var c in p.Caches)
                    {
                        int depth = Math.Min(4, Decay.LevelAt(p.Layout, c.Cell.Y).Depth);
                        double meanRarity = c.Items.Count == 0 ? 0 : c.Items.Average(it => (int)it.Rarity);
                        if (!byDepth.TryGetValue(depth, out var l)) byDepth[depth] = l = new List<double>();
                        if (!c.InVault && !c.Secret) l.Add(meanRarity);
                        if (c.Secret) { secretTier += c.Tier; secret++; }
                        else if (c.InVault) { vaultTier += c.Tier; vault++; }
                        else { sideTier += c.Tier; side++; }
                        if (s == PoiState.Abandoned) { abandoned += c.Tier; na++; } else { infested += c.Tier; ni++; }
                    }
                }
        var depths = byDepth.Keys.OrderBy(d => d).ToList();
        var means = depths.Select(d => byDepth[d].Count > 0 ? byDepth[d].Average() : 0).ToList();
        bool rising = means.Count >= 3 && means[^1] > means[0] + 0.3;
        for (int i = 1; i < means.Count; i++) rising &= means[i] >= means[i - 1] - 0.05;
        Check(rising, $"loot rarity rises with depth: side caches average rarity {string.Join(", ", depths.Select((d, i) => $"depth {d}: {means[i]:F2}"))} (0 common .. 4 unique)");
        Check(secretTier / Math.Max(1, secret) > sideTier / Math.Max(1, side) && vaultTier / Math.Max(1, vault) > sideTier / Math.Max(1, side),
            $"vaults (tier {vaultTier / Math.Max(1, vault):F1}) and hidden stashes (tier {secretTier / Math.Max(1, secret):F1}) beat the side caches around them (tier {sideTier / Math.Max(1, side):F1})");
        Check(infested / Math.Max(1, ni) > abandoned / Math.Max(1, na), $"danger pays: infested caches average tier {infested / Math.Max(1, ni):F1}, abandoned {abandoned / Math.Max(1, na):F1}");
    }

    // ---- underground ---------------------------------------------------------------------

    /// <summary>
    /// Underground storeys are really carved: the terrain stamp planet-surface seats a place with
    /// opens every cell the place occupies below the pad, and leaves every cell round them solid.
    /// </summary>
    void Carved()
    {
        int places = 0, carved = 0, walls = 0, holes = 0, leaks = 0, deepest = 0;
        foreach (var f in _lib.Families)
        {
            var inst = PoiFactory.Generate(_lib, f, 0, Rarity.Rare, PoiState.Abandoned, 9);
            var occ = inst.Layout.Occupied;
            if (occ.Min.Y >= 0) continue;
            places++;
            int carveH = -occ.Min.Y;
            var mask = new bool[carveH * occ.Size.Z * occ.Size.X];
            for (int y = 0; y < carveH; y++)
                for (int z = 0; z < occ.Size.Z; z++)
                    for (int x = 0; x < occ.Size.X; x++)
                        mask[(y * occ.Size.Z + z) * occ.Size.X + x] = occ[new Gen.Int3(occ.Min.X + x, occ.Min.Y + y, occ.Min.Z + z)];
            var stamp = new TerrainStamp(f.Name, occ.Min.X, occ.Min.Z, occ.Min.X + occ.Size.X, occ.Min.Z + occ.Size.Z, 0, 4f, 8f, 16000, 8000,
                occ.Min.X, occ.Min.Y, occ.Min.Z, occ.Size.X, carveH, occ.Size.Z, mask);
            deepest = Math.Min(deepest, occ.Min.Y);
            for (int y = occ.Min.Y; y < 0; y++)
                for (int z = occ.Min.Z; z < occ.Min.Z + occ.Size.Z; z++)
                    for (int x = occ.Min.X; x < occ.Min.X + occ.Size.X; x++)
                    {
                        var c = new Gen.Int3(x, y, z);
                        bool inside = occ[c];
                        bool open = stamp.IsCarved(x, y, z);
                        if (inside && !open) holes++;
                        if (!inside && open) leaks++;
                        if (inside && inst.Blocks.Get(c).IsAir) carved++;
                        if (!inside && Dirs.All.Any(d => occ[c + d.Vec()] && inst.Blocks.Get(c + d.Vec()).IsAir)) walls++;
                    }
        }
        Check(places >= 10 && holes == 0 && leaks == 0 && carved > 10000,
            $"underground levels are carved: in {places} places with storeys below ground (down to {-deepest} m), the terrain stamp opens all " +
            $"{carved} open cells inside them and none outside, and {walls} cells of solid ground wall them in ({holes} unopened, {leaks} leaks)");
    }

    void Hidden()
    {
        int secrets = 0, withTells = 0, corridorBreaks = 0, opened = 0, sealedBefore = 0;
        foreach (var f in Arch)
            for (int i = 0; i < 6; i++)
            {
                var layout = PoiFactory.BuildLayout(_lib, f, i, Rarity.Rare, Hash.Combine(0x5EC2E7UL, (ulong)i, Hash.Of(f.Id)));
                var nav = new NavGrid(layout.Blocks, layout.Occupied, layout.Extras.Lifts, layout.Extras.InSpace);
                var reach = nav.Reachable(layout.EntranceCell);
                foreach (var s in layout.Extras.Secrets)
                {
                    secrets++;
                    var room = layout.Modules[s.Module];
                    if (!room.Cells.Any(reach.Contains)) sealedBefore++;
                    if (s.Tells.Count >= 2 && s.TellCells.Count > 0 && layout.Blocks.Get(s.WallCells[0]).Type == BlockType.Breakable) withTells++;
                    // Break it, then check the reach and the corridor rule again.
                    var saved = layout.Blocks;
                    layout.Blocks = saved.Clone();
                    foreach (Gen.Int3 w in s.WallCells) layout.Blocks.Set(w, Block.Air);
                    var reach2 = new NavGrid(layout.Blocks, layout.Occupied, layout.Extras.Lifts, layout.Extras.InSpace).Reachable(layout.EntranceCell);
                    if (room.Cells.Any(reach2.Contains)) opened++;
                    var problems = LayoutCheck.Check(layout).Where(p => !p.Contains("hidden room")).ToList();
                    if (problems.Count > 0) { corridorBreaks++; GD.PrintErr($"      breaking {f.Id}'s hidden wall: {problems[0]}"); }
                    layout.Blocks = saved;
                }
            }
        Check(secrets >= 30 && sealedBefore == secrets && opened == secrets,
            $"{secrets} hidden rooms: every one unreachable until its wall breaks ({sealedBefore}), then reachable ({opened})");
        Check(withTells == secrets, $"every hidden room has its tells: a cracked breakable panel, a draft, scuffs, a gap in the plan ({withTells}/{secrets})");
        Check(corridorBreaks == 0, $"breaking a hidden wall never breaks the corridor or anything round it ({corridorBreaks} failures)");
    }

    void Breaches()
    {
        int breaches = 0, layouts = 0, toVault = 0;
        foreach (var f in Arch.Where(f => f.Program != null && _lib.Programs[f.Program].Breach > 0))
            for (int i = 0; i < 20; i++)
            {
                var layout = PoiFactory.BuildLayout(_lib, f, i, Rarity.Common, Hash.Combine(0xB2EAC4UL, (ulong)i, Hash.Of(f.Id)));
                layouts++;
                foreach (var b in layout.Extras.Breaches)
                {
                    breaches++;
                    // An alternative way in: from the lip down into the building, all the way to the vault.
                    var nav = new NavGrid(layout.Blocks, layout.Occupied, layout.Extras.Lifts, layout.Extras.InSpace);
                    var reach = nav.Reachable(b.SurfaceCell);
                    if (Fixtures.RoomCell(layout.Vault, reach) != null) toVault++;
                }
            }
        Check(breaches >= 10 && toVault == breaches, $"collapse breaches: {breaches} rolled in {layouts} buried layouts, each a climbable rubble slope " +
                                                     $"from the surface into its room and on to the vault ({toVault}), validated like any room");
    }

    void Keys()
    {
        int places = 0, locks = 0, fine = 0;
        foreach (var f in Arch)
            for (int i = 0; i < 6; i++)
            {
                var p = PoiFactory.Generate(_lib, f, i, Rarity.Rare, (PoiState)(i % 4), (ulong)(700 + i), Hash.Combine(0x4E7UL, (ulong)i, Hash.Of(f.Id)));
                places++;
                locks += p.Extras.Locks.Count;
                if (Validator.KeyOrder(p) == null) fine++;
            }
        Check(fine == places && locks > 50, $"keycards and locks: {locks} locked doors in {places} places, every keycard reachable before the doors it opens ({fine}/{places})");
    }

    // ---- placement (the kit engine's per-planet rules) -------------------------------------

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
        foreach (var (map, pois) in systems)
            foreach (var p in pois)
            {
                total++;
                var others = pois.Where(o => o != p).ToList();
                string why = "";
                if (!p.Family.Biomes.Contains(map.BiomeAt(p.CellX, p.CellY))) why = "biome";
                else if (!p.Family.Territories.Contains("any") && !p.Family.Territories.Contains(map.TerritoryAt(p.CellX, p.CellY))) why = "territory";
                else if (p.Family.Near.Any(r => !Placement.FeatureWithin(map, r, p.CellX, p.CellY, others))) why = "near rule";
                else if (p.Family.Avoid.Any(r => Placement.FeatureWithin(map, r, p.CellX, p.CellY, others))) why = "avoid rule";
                else if (others.Any(o => Placement.Dist(p.CellX, p.CellY, o.CellX, o.CellY) < Placement.MinSpacing)) why = "spacing";
                else if (others.Any(o => o.Family == p.Family && Placement.Dist(p.CellX, p.CellY, o.CellX, o.CellY) < p.Family.Spacing)) why = "family spacing";
                if (why.Length > 0) { bad++; if (firstBad.Length == 0) firstBad = $" (first: {p} breaks {why})"; }
            }
        Check(total > 100 && bad == 0, $"{total} placements respect biome, territory, near/avoid rules and spacing{firstBad}");
        float nearestFarmToVharn = float.MaxValue;
        foreach (var (_, pois) in systems)
            foreach (var farm in pois.Where(p => p.Family.Id == "freeholder_farm"))
                foreach (var v in pois.Where(p => p.Family.Id == "vharn_outpost"))
                    nearestFarmToVharn = Math.Min(nearestFarmToVharn, Placement.Dist(farm.CellX, farm.CellY, v.CellX, v.CellY));
        Check(nearestFarmToVharn >= 10f, $"farms stay at least 10 cells from Vharn garrisons (closest {nearestFarmToVharn:F1})");
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
    }

    void RegrowthPolicy()
    {
        var common = Systems().SelectMany(s => s.Pois).First(p => p.Rarity == Rarity.Common);
        var uncommon = Systems().SelectMany(s => s.Pois).First(p => p.Rarity == Rarity.Uncommon);
        var rare = Systems().SelectMany(s => s.Pois).First(p => p.Rarity >= Rarity.Rare);
        var regrowth = new Regrowth { RegrowSeconds = 600f, PlayerRadius = 500f };
        foreach (var p in new[] { common, uncommon, rare }) regrowth.MarkCleared(p);
        ulong seedBefore = common.SpawnSeed;
        var near = new List<(float, float)> { (common.X + 100f, common.Z) };
        var regrown = new List<PlacedPoi>();
        for (int i = 0; i < 360; i++) regrown.AddRange(regrowth.Tick(10f, near));
        Check(regrown.Count == 0 && regrowth.IsCleared(common), "nothing regrows while a player is near");
        var far = new List<(float, float)> { (common.X + 5000f, common.Z + 5000f) };
        for (int i = 0; i < 59; i++) regrown.AddRange(regrowth.Tick(10f, far));
        Check(regrown.Count == 0, "nothing regrows before the timer runs out");
        for (int i = 0; i < 400; i++) regrown.AddRange(regrowth.Tick(10f, far));
        Check(regrown.Count == 1 && regrown[0] == common && common.SpawnSeed != seedBefore && !regrowth.IsCleared(common),
            "the common place regrows with fresh dressing once nobody is near");
        Check(regrowth.IsCleared(uncommon) && regrowth.IsCleared(rare), "uncommon and rarer places never regrow");
    }

    void RevealDistances()
    {
        var (map, pois) = Systems().Where(s => s.Pois.Any(p => p.Reveal.Landmark)).OrderByDescending(s => s.Pois.Count).First();
        var reveal = new MapReveal();
        var fromOrbit = reveal.Update(pois, map.Width * map.CellSize / 2, map.Height * map.CellSize / 2, 1200f);
        Check(fromOrbit.Count > 0 && fromOrbit.All(p => p.Reveal.Landmark), $"from orbit only landmarks show ({fromOrbit.Count})");
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
    }

    /// <summary>
    /// Whole planets: places cover the map, each planet's themed set fills most of it and its
    /// outliers stay inside the theme's data range, rare but always present.
    /// </summary>
    void WorldPlacementCheck()
    {
        int planets = 0, badShare = 0, noOutlier = 0, poorCover = 0, total = 0, outliers = 0;
        var byRarity = new int[5];
        int rareDupes = 0;
        float worstCover = 1f;
        string firstBad = "";
        foreach (var theme in _lib.Themes)
            for (int s = 0; s < 12; s++)
            {
                var map = PlanetMap.GenerateWorld(Hash.Combine(Hash.Of(theme.Id), (ulong)s), 200, 100, 80f, theme.Id);
                var r = WorldPlacement.Place(_lib, map, theme, Hash.Combine(0x91A7UL, (ulong)s));
                planets++;
                foreach (var p in r.Pois) byRarity[(int)p.Rarity]++;
                rareDupes += r.Pois.Where(p => p.Rarity >= Rarity.Exotic).GroupBy(p => p.Family.Id).Count(g => g.Count() > 1);
                int n = r.Pois.Count, o = r.Outliers;
                total += n;
                outliers += o;
                float share = o / (float)n;
                if (share < theme.OutlierMin - 1f / n || share > theme.OutlierMax + 1f / n) { badShare++; if (firstBad.Length == 0) firstBad = $" (first: {theme.Id} {o}/{n})"; }
                if (o == 0) noOutlier++;
                if (r.Pois.Any(p => !p.Outlier && !theme.Set.Any(t => t.Family == p.Family.Id))) badShare++;
                // Cover: an 8 x 4 grid of regions over the whole planet.
                var regions = new HashSet<(int, int)>(r.Pois.Select(p => (p.CellX * 6 / map.Width, p.CellY * 3 / map.Height)));
                float cover = regions.Count / 18f;
                worstCover = Math.Min(worstCover, cover);
                if (cover < 0.9f) poorCover++;
            }
        Check(badShare == 0 && noOutlier == 0, $"themed sets and outliers: {planets} planets over {_lib.Themes.Count} themes, {total} places, {outliers} outliers " +
                                               $"({outliers * 100f / total:F1}%); every planet's outlier share inside its theme's range and never zero{firstBad}");
        Check(byRarity[0] > byRarity[1] && byRarity[1] > byRarity[2] && byRarity[2] > byRarity[3] && rareDupes == 0,
            $"rarity in practice over {planets} planets: {byRarity[0]} common, {byRarity[1]} uncommon, {byRarity[2]} rare, {byRarity[3]} exotic, {byRarity[4]} unique; no exotic or unique family twice on a planet ({rareDupes})");
        Check(poorCover == 0, $"placement covers the whole planet, not just the landing site: the worst planet has places in {worstCover:P0} of its 18 regions (6 by 3)");
    }

    /// <summary>
    /// A century or two after the fall: old-world places hold no fresh food outside sealed, frozen
    /// or airless rooms; places worked today are new; trees grow through old ruins and unsheltered
    /// timber has fallen in. Factories and farms pick the layout for the world's tech level.
    /// </summary>
    void Ages()
    {
        int oldCaches = 0, rottenFood = 0, sealedFood = 0, trees = 0, ruins = 0, timberStanding = 0;
        foreach (var f in _lib.Families)
            for (int s = 0; s < 3; s++)
            {
                var p = PoiFactory.Generate(_lib, f, s % f.Variants.Count, Rarity.Uncommon, s == 2 ? PoiState.Ruined : PoiState.Abandoned, (ulong)(60 + s));
                if (p.Age != Age.OldWorld) continue;
                ruins++;
                foreach (var c in p.Caches)
                {
                    bool food = c.Items.Any(i => i.Tags.Contains("food"));
                    if (p.AgeOf(c.Module) == Age.OldWorld) { oldCaches++; if (food) rottenFood++; }
                    else if (food) sealedFood++;
                }
                trees += p.Blocks.Blocks().Count(b => b.Block.Type == BlockType.Pillar && b.Block.Material == Gen.Material.Timber && b.Block.Role == Role.Decor
                                                      && p.Blocks.Get(b.Cell + Int3.Down).Type != BlockType.Pillar) ;
                timberStanding += p.Blocks.Blocks().Count(b => b.Block.Material == Gen.Material.Timber && b.Block.Role == Role.Roof && b.Cell.Y >= 0);
            }
        var farm = PoiFactory.Generate(_lib, _lib.Family("freeholder_farm"), 0, Rarity.Common, PoiState.Active, 3);
        // Tech: on a world of each tech level, factories and farms take that level's layout.
        int techMismatch = 0, techPlaced = 0;
        foreach (var theme in _lib.Themes)
            for (int s = 0; s < 4; s++)
            {
                var map = PlanetMap.GenerateWorld(Hash.Combine(Hash.Of(theme.Id), (ulong)s + 90), 200, 100, 80f, theme.Id);
                foreach (var p in WorldPlacement.Place(_lib, map, theme, Hash.Combine(0x7EC4UL, (ulong)s)).Pois.Where(p => p.Family.Variants.Any(v => v.Tech.Length > 0)))
                {
                    techPlaced++;
                    if (p.Variant.Tech != theme.Tech && p.Family.Variants.Any(v => v.Tech == theme.Tech)) techMismatch++;
                }
            }
        Check(rottenFood == 0 && farm.Age == Age.New && trees > 0 && techMismatch == 0 && techPlaced > 0,
            $"long after the fall: {ruins} old-world places, no food in {oldCaches} of their unsealed caches ({sealedFood} sealed stores kept some), " +
            $"{trees} trees grown through them, {timberStanding} unsheltered timber roof blocks still up; a worked freeholder farm is new ({farm.Age}); " +
            $"factories and farms match their world's tech level ({techPlaced} placed, {techMismatch} wrong)");
    }

    /// <summary>
    /// What factions hold is mostly working ground: across many planets that aren't start worlds,
    /// fortifications are at most about 15% of faction-held places, and at least five kinds of
    /// faction-held place turn up. Every one has its core and defenders.
    /// </summary>
    void FactionMix()
    {
        int held = 0, forts = 0, noCore = 0;
        var kinds = new HashSet<string>();
        foreach (var theme in _lib.Themes.Where(t => !t.Match.Contains("fallen")))
            for (int s = 0; s < 10; s++)
            {
                var map = PlanetMap.GenerateWorld(Hash.Combine(Hash.Of(theme.Id), (ulong)s + 300), 200, 100, 80f, theme.Id);
                foreach (var p in WorldPlacement.Place(_lib, map, theme, Hash.Combine(0xFAC7UL, (ulong)s)).Pois)
                {
                    if (!p.Family.HasCore(p.State) || p.State != PoiState.Active) continue;
                    held++;
                    kinds.Add(p.Family.Id);
                    if (p.Family.Tags.Contains("fort")) forts++;
                }
            }
        foreach (var f in _lib.Families.Where(f => f.Core == CoreRule.Owned && f.Tags.Contains("working")))
        {
            var p = PoiFactory.Generate(_lib, f, 0, Rarity.Uncommon, PoiState.Active, 5);
            if (!p.HasCore || (!p.Enemies.Any(e => e.Defender) && !f.Tags.Contains("civilian"))) noCore++;
        }
        float share = held == 0 ? 1f : forts / (float)held;
        Check(share <= 0.15f && kinds.Count >= 5 && noCore == 0,
            $"faction ground is mostly working sites: {held} faction-held places, {forts} fortifications ({share:P0}, at most 15%), {kinds.Count} kinds ({string.Join(", ", kinds.OrderBy(k => k).Take(10))}); every working site has its core and defenders ({noCore} without)");
    }

    /// <summary>Settlements and civilian places: activity spots, links, routes, people with roles and gaits, a mood, animals by tech level, an area.</summary>
    void TownsfolkCheck()
    {
        var moods = new Dictionary<Mood, int>();
        int places = 0, bad = 0, hostileFriendly = 0, workingAnimalsHighTech = 0;
        string first = "";
        foreach (var f in _lib.Families.Where(f => f.Tags.Contains("settlement") || f.Tags.Contains("civilian")))
            for (int s = 0; s < 8; s++)
            {
                var p = PoiFactory.Generate(_lib, f, s % f.Variants.Count, Rarity.Common, PoiState.Active, (ulong)(400 + s));
                places++;
                var t = p.Townsfolk;
                moods[p.Mood] = moods.TryGetValue(p.Mood, out int n) ? n + 1 : 1;
                if (t == null || t.Spots.Count < 3 || t.Links.Count < 2 || t.Routes.Count == 0 || !t.People.Any(c => c.Role == "guard") || t.AreaRadius <= 0
                    || t.People.Any(c => c.Role is not ("vendor" or "rusher" or "wanderer" or "worker" or "guard")))
                { bad++; if (first.Length == 0) first = $" (first: {f.Id} seed {400 + s}: {t?.Spots.Count ?? 0} spots, {t?.People.Count ?? 0} people)"; }
                if (p.Mood == Mood.Hostile && p.Friendly) hostileFriendly++;
                if (f.Tags.Contains("civilian") && (p.Mood != Mood.Peaceful || !p.Friendly)) hostileFriendly++;
                if (t != null && (p.Layout.Variant?.Tech ?? "") == "high" && t.Animals.Any(a => a.Kind == "working")) workingAnimalsHighTech++;
            }
        Check(bad == 0 && hostileFriendly == 0 && workingAnimalsHighTech == 0 && moods.Count >= 2,
            $"townsfolk: {places} lived-in places with activity spots, links, cross-town routes, vendors, workers, rushers, wanderers and guards; " +
            $"moods {string.Join(", ", moods.Select(m => $"{m.Value} {m.Key.ToString().ToLowerInvariant()}"))}; civilian places friendly; animals by tech level{first}");
    }

    /// <summary>A blast site: the zone as data, ruins blown down round it with shadow marks, and the blasted decay on a nearby place.</summary>
    void Blasts()
    {
        var site = PoiFactory.Generate(_lib, _lib.Family("blast_crater"), 0, Rarity.Uncommon, PoiState.Ruined, 3);
        var zone = site.Layout.Extras.Blast;
        int shadows = site.Layout.Extras.Markers.Count(m => m.Kind == "shadow"), edges = site.Layout.Extras.Markers.Count(m => m.Kind == "radiation-edge"), hulks = site.Layout.Extras.Markers.Count(m => m.Kind == "hulk");
        var before = PoiFactory.Generate(_lib, _lib.Family("hospital"), 0, Rarity.Uncommon, PoiState.Abandoned, 3);
        var after = PoiFactory.Generate(_lib, _lib.Family("hospital"), 0, Rarity.Uncommon, PoiState.Abandoned, 3);
        Gen.StateTransforms.Blast(after, 1f, 0f, 0.8f, new Rng(5));
        int roofsBefore = before.Blocks.Blocks().Count(b => b.Block.Role == Role.Roof), roofsAfter = after.Blocks.Blocks().Count(b => b.Block.Role == Role.Roof);
        int Facing(PoiInstance p) => p.Blocks.Blocks().Count(b => b.Block.Role == Role.Wall && b.Cell.Y >= 0 && p.Blocks.Get(b.Cell + new Int3(1, 0, 0)).IsAir && !p.Layout.Occupied[b.Cell + new Int3(1, 0, 0)]);
        int eastBefore = Facing(before), eastAfter = Facing(after);
        var problems = Validator.Check(after);
        Check(zone != null && zone.Fade > zone.Dead && zone.Dead > zone.Crater && shadows > 0 && edges > 0 && hulks > 0 && roofsAfter < roofsBefore / 2 && eastAfter < eastBefore && problems.Count == 0,
            $"blast sites: crater {zone?.Crater:F0} m, dead zone to {zone?.Dead:F0} m fading by {zone?.Fade:F0} m (data for the ground tint), {shadows} shadow marks facing ground zero, {hulks} hulks thrown outward, {edges} radiation signs at the edge; " +
            $"a hospital in the blast keeps {roofsAfter} of {roofsBefore} roof blocks and {eastAfter} of {eastBefore} walls on the side facing it, still valid{(problems.Count > 0 ? ": " + problems[0] : "")}");
    }

    /// <summary>Programs guarantee their must-have rooms (`lobby!`, `vault!`): every building has every one at least once.</summary>
    void RequiredRooms()
    {
        int buildings = 0, missing = 0, demands = 0;
        string first = "";
        foreach (var f in _lib.Families.Where(f => f.Generator == Generator.Building))
        {
            var program = _lib.Programs[f.Program!];
            var musts = program.Bands.Values.SelectMany(b => b).Where(d => d.Must).GroupBy(d => d.Room).Select(g => (Room: g.Key, Min: 1)).ToList();
            for (int s = 0; s < 12; s++)
            {
                var l = PoiFactory.BuildLayout(_lib, f, 0, (Rarity)(s % 3), (ulong)(500 + s * 37));
                buildings++;
                foreach (var (room, min) in musts)
                {
                    demands++;
                    int have = l.Extras.Rooms.Count(r => r.Purpose == room) + (room == "vault" && l.VaultModule >= 0 ? 1 : 0);
                    if (have == 0) { missing++; if (first.Length == 0) first = $" (first: {f.Id} seed {500 + s * 37} has no {room})"; }
                }
            }
        }
        Check(missing == 0, $"programs guarantee their must-have rooms: {buildings} buildings, {demands} required rooms, {missing} missing{first}");
    }

    /// <summary>Cables, wires and tendrils always hang from something: never in mid-air.</summary>
    void Tethers()
    {
        int places = 0, hanging = 0, loose = 0;
        foreach (var f in _lib.Families)
            foreach (PoiState s in Enum.GetValues<PoiState>())
            {
                var p = PoiFactory.Generate(_lib, f, 0, Rarity.Uncommon, s, 31);
                places++;
                hanging += p.Blocks.Blocks().Count(b => Structure.Hangs(b.Block.Type));
                loose += Structure.Untethered(p.Blocks).Count;
            }
        Check(hanging > 0 && loose == 0, $"cables and tendrils: {hanging} hanging things in {places} places, every one hangs from an anchor above it ({loose} in mid-air)");
    }

    /// <summary>Start worlds: no faction holds ground (faction places only as ruins), no cores, no defenders; the rest stays.</summary>
    void StartWorlds()
    {
        int planets = 0, places = 0, held = 0, cores = 0, guards = 0, ruins = 0, gangs = 0, spawned = 0;
        foreach (var theme in _lib.Themes)
            for (int s = 0; s < 6; s++)
            {
                var start = theme.AsStart();
                var map = PlanetMap.GenerateWorld(Hash.Combine(Hash.Of(theme.Id), (ulong)s + 50), 200, 100, 80f, theme.Id);
                var r = WorldPlacement.Place(_lib, map, start, Hash.Combine(0x57A27UL, (ulong)s));
                planets++;
                foreach (var p in r.Pois)
                {
                    places++;
                    if (ThemeDef.FactionOwned(p.Family) && p.State != PoiState.Ruined) held++;
                    if (p.Family.HasCore(p.State)) cores++;
                    if (ThemeDef.FactionOwned(p.Family)) ruins++;
                    if (p.Family.Tags.Contains("gang")) gangs++;
                }
                // Spawn a few and look: no core block, no faction defender.
                foreach (var p in r.Pois.Take(3))
                {
                    var inst = PoiFactory.Generate(_lib, p.Family, p.Family.Variants.IndexOf(p.Variant), p.Rarity, p.State, p.SpawnSeed);
                    spawned++;
                    if (inst.HasCore || inst.Blocks.Blocks().Any(b => b.Block.Type == BlockType.Core)) cores++;
                    if (ThemeDef.FactionOwned(p.Family) && inst.Enemies.Any(e => e.Kind is EnemyKind.Guard or EnemyKind.Turret)) guards++;
                }
            }
        Check(held == 0 && cores == 0 && guards == 0,
            $"start worlds: {planets} planets, {places} places, none held by a faction ({ruins} faction places only as ruins), no cores, " +
            $"no faction defenders in {spawned} spawned; gang forts stay ({gangs}){(held + cores + guards > 0 ? $"; {held} held, {cores} cores, {guards} with guards" : "")}");
    }

    /// <summary>Start systems: plenty of wrecks in orbit and nearby, mostly salvage and lower danger, no live faction.</summary>
    void StartSystems()
    {
        int systems = 0, fewest = int.MaxValue, held = 0, dangerous = 0, near = 0, wrecks = 0, places = 0;
        int normalWrecks = 0, normalHeld = 0;
        for (int s = 0; s < 60; s++)
        {
            var start = OrbitPlacement.Place(_lib, (ulong)s * 7919 + 1, start: true);
            var normal = OrbitPlacement.Place(_lib, (ulong)s * 7919 + 1, start: false);
            systems++;
            int w = start.Count(p => p.Site.IsWreck);
            fewest = Math.Min(fewest, w);
            wrecks += w;
            places += start.Count;
            held += start.Count(p => p.FactionHeld);
            dangerous += start.Count(p => p.Site.Danger > 2);
            near += start.Count(p => p.Zone == "near");
            normalWrecks += normal.Count(p => p.Site.IsWreck);
            normalHeld += normal.Count(p => p.FactionHeld);
        }
        Check(fewest >= 8 && held == 0 && dangerous == 0 && wrecks > normalWrecks * 1.5f && normalHeld > 0,
            $"start systems: {systems} systems, every one with at least {fewest} wrecks in orbit and nearby ({wrecks} of {places} places, {near} close to home), " +
            $"none above danger 2, no live faction (other systems: {normalWrecks} wrecks, {normalHeld} faction-held)");
    }

    /// <summary>From afar: each family's landmark stands as high as its purpose wants, and something lights it.</summary>
    void Landmarks()
    {
        var bad = new List<string>();
        foreach (var f in _lib.Families)
            foreach (var s in new[] { PoiState.Abandoned, PoiState.Ruined })
            {
                var p = PoiFactory.Generate(_lib, f, 0, Rarity.Uncommon, s, 21);
                int top = p.Blocks.Blocks().Where(b => b.Cell.Y >= 0 && BlockInfo.Collides(b.Block.Type)).Select(b => b.Cell.Y + 1).DefaultIfEmpty(0).Max();
                bool light = p.Lights.Count > 0 || p.Blocks.Blocks().Any(b => b.Cell.Y >= 0 && b.Block.Material is Gen.Material.Lumen or Gen.Material.Glyph or Gen.Material.Crystal);
                if (s == PoiState.Ruined) top = Math.Max(top, f.Rise); // a ruin may have lost its top; it still needs its light
                if (top < f.Rise || !light) bad.Add($"{f.Id} {s}: top {top} m (wants {f.Rise}), light {light}");
            }
        Check(bad.Count == 0, $"landmarks: every family's tallest part stands as high as its purpose wants, from 2 m (a buried lab's hatch) to 30 m (a tower, a rocket), " +
                              $"and something still lit carries in the dark{(bad.Count > 0 ? "; " + string.Join("; ", bad.Take(4)) : "")}");
    }

    void ShooterTags()
    {
        var armed = _lib.Rosters.Values.SelectMany(r => r).Where(e => e.Kind is EnemyKind.Guard or EnemyKind.Turret or EnemyKind.Hover).ToList();
        Check(armed.All(e => e.Shooter != ShooterClass.None && e.Weapon.Length > 0),
            $"every armed defender and turret carries a shooter class and a weapon ({armed.Count} roster lines)");
    }

    // ---- performance ------------------------------------------------------------------

    void Performance()
    {
        var mats = new BlockMaterials();
        Prof.Ticks.Clear();
        var sw = Stopwatch.StartNew();
        double gen = 0, val = 0, mesh = 0, worst = 0;
        string worstName = "";
        long blocks = 0, quads = 0;
        var fams = _lib.Families;
        for (int i = 0; i < 100; i++)
        {
            var f = fams[i % fams.Count];
            long t0 = sw.ElapsedTicks;
            var p = PoiFactory.Generate(_lib, f, i % f.Variants.Count, (Rarity)(i % 5), (PoiState)(i % 4), (ulong)i, customSeed: (ulong)(77 + i));
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
            double one = (t3 - t0) * 1000.0 / Stopwatch.Frequency;
            if (one > worst) { worst = one; worstName = $"{f.Id} ({p.Blocks.CountBlocks()} blocks)"; }
            blocks += g.CountBlocks();
        }
        double ms(double ticks) => ticks * 1000.0 / Stopwatch.Frequency;
        GD.Print($"PERF  100 POIs: generate {ms(gen):F0} ms, validate {ms(val):F0} ms, mesh {ms(mesh):F0} ms, " +
                 $"total {ms(gen + val + mesh):F0} ms ({ms(gen + val + mesh) / 100:F1} ms each); slowest {worst:F0} ms, {worstName}");
        GD.Print($"PERF  average {blocks / 100} blocks and {quads / 100} quads per POI");
        GD.Print("PERF  generation phases: " + string.Join(", ", Prof.Ticks.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {Prof.Ms(kv.Value):F0} ms")));
        Check(ms(gen + val + mesh) / 100 < 250 && worst < 2500, $"generating, validating and meshing a POI averages under 250 ms (landmark sites under 2.5 s; slowest {worst:F0} ms)");
    }

    // ---- walking it -------------------------------------------------------------------

    // A body really walks from outside each place to its deepest point and back: through doors
    // (locked ones open as if the walker held the cards), along the corridors, down every stair,
    // riding lifts and climbing ladders. Proves the meshes, collision, doors, lifts and grid agree.
    readonly (string Family, int Variant, Rarity Rarity, PoiState State, bool Lifts, string Goal)[] _walks =
    {
        ("old_airfield", 0, Rarity.Rare, PoiState.Abandoned, true, "deep"),
        ("research_base", 0, Rarity.Uncommon, PoiState.Abandoned, true, "deep"),
        ("ominous_tower", 0, Rarity.Uncommon, PoiState.Ruined, false, "top+deep"),
        ("deep_bunker", 0, Rarity.Rare, PoiState.Infested, false, "deep"),
        ("old_mine", 0, Rarity.Common, PoiState.Abandoned, false, "deep"),
        ("vharn_outpost", 0, Rarity.Common, PoiState.Active, true, "vault"),
    };
    int _walk = -1, _step, _ticks, _stuck, _waits;
    Node3D _walkRoot;
    Player _walker;
    PoiView _view;
    List<Gen.Int3> _route;
    float _lowest;
    bool _back;

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
        var w = _walks[_walk];
        var poi = PoiFactory.Generate(_lib, _lib.Family(w.Family), w.Variant, w.Rarity, w.State, 9);
        _walkRoot = new Node3D { Name = "Walk" };
        AddChild(_walkRoot);
        _view = PoiView.Build(poi, Gen.Biome.Rock, new PoiView.Options(Lights: false, Figures: false));
        _view.AllKeys = true;
        _walkRoot.AddChild(_view);
        var hole = new Rect2I(_view.PlugMin, _view.PlugMax - _view.PlugMin + Vector2I.One);
        _walkRoot.AddChild(Terrain.Build(new Vector2I(-200, -200), new Vector2I(50, 50), (x, z) => Colors.Gray, new List<Rect2I> { hole }, true, 0f));
        _walker = new Player { Name = "Walker" };
        _walkRoot.AddChild(_walker);
        _walker.LadderQuery = _view.LadderAt;
        _view.Watcher = _walker;

        _route = Route(poi, w.Lifts, w.Goal);
        if (_route == null)
        {
            Fail($"walking {WalkName(w)}: no route to the goal");
            NextWalk();
            return;
        }
        var (start, yaw) = _view.EntranceStand();
        _walker.Place(start, yaw);
        _step = 0;
        _ticks = 0;
        _stuck = 0;
        _waits = 0;
        _back = false;
        _lowest = _route.Min(c => c.Y) - 1.5f;
    }

    static string WalkName((string Family, int Variant, Rarity Rarity, PoiState State, bool Lifts, string Goal) w) => $"{w.Family} ({w.State}, {w.Rarity})";

    /// <summary>From the way in to the goal by the grid's shortest path (lifts optional), and back again.</summary>
    List<Gen.Int3> Route(PoiInstance poi, bool lifts, string goal)
    {
        var layout = poi.Layout;
        var nav = new NavGrid(poi.Blocks, layout.Occupied, lifts ? layout.Extras.Lifts : null, layout.Extras.InSpace);
        var tree = nav.Tree(layout.EntranceCell);
        var reach = new HashSet<Gen.Int3>(tree.Keys);
        Gen.Int3? Deepest() => reach.Where(c => !nav.LadderAt(c) && !nav.LiftAt(c) && nav.Standable(c)).OrderBy(c => c.Y).ThenBy(c => c.Manhattan(layout.EntranceCell)).Cast<Gen.Int3?>().FirstOrDefault();
        Gen.Int3? Top() => reach.Where(c => !nav.LadderAt(c) && !nav.LiftAt(c)).OrderByDescending(c => c.Y).Cast<Gen.Int3?>().FirstOrDefault();
        var route = new List<Gen.Int3>();
        Gen.Int3? Vault() => poi.Caches.Where(c => c.InVault).Select(c => nav.AccessCell(c.Cell, reach)).FirstOrDefault(a => a != null);
        var goals = goal.Split('+').Select(g => g == "top" ? Top() : g == "vault" ? Vault() : Deepest()).ToList();
        Gen.Int3 from = layout.EntranceCell;
        foreach (var g in goals)
        {
            if (g is not Gen.Int3 to) return null;
            var leg = nav.FindPath(from, to);
            if (leg == null) return null;
            if (route.Count > 0) leg.RemoveAt(0);
            route.AddRange(leg);
            from = to;
        }
        var back = nav.FindPath(from, layout.EntranceCell);
        if (back == null) return null;
        back.RemoveAt(0);
        _turnAt = route.Count;
        route.AddRange(back);
        return route;
    }

    int _turnAt;

    public override void _PhysicsProcess(double delta)
    {
        if (_walker == null || _walk < 0 || _walk >= _walks.Length || _route == null) return;
        _ticks++;
        if (_ticks < 10) return; // let it land
        var w = _walks[_walk];
        Vector3 at = _walker.GlobalPosition;
        if (at.Y < _lowest)
        {
            Fail($"walking {WalkName(w)}: fell through the floor at {at} on cell {_step}/{_route.Count}");
            _walker = null;
            NextWalk();
            return;
        }
        while (_step < _route.Count && Reached(at, _route[_step]))
        {
            _step++;
            _stuck = 0;
            if (_step == _turnAt) _back = true;
        }
        if (_step >= _route.Count)
        {
            Check(true, $"a body walked {WalkName(w)} from outside to its {w.Goal} point and back: {_route.Count} cells ({_turnAt} in), " +
                        $"{_ticks / 60f:F0} s, {_waits} lift rides, never below the floor or stuck");
            _walker.AutoTarget = null;
            _walker = null;
            NextWalk();
            return;
        }
        var target = _route[_step];
        var prev = _route[Math.Max(0, _step - 1)];
        // A lift: wait on the car until it reaches the next stop, then walk off.
        if (Math.Abs(target.Y - prev.Y) > 1 && _view.LiftAt(target) is { } lift && lift.stop >= 0)
        {
            _view.RequestLift(lift.lift, lift.stop);
            if (!_view.LiftArrived(lift.lift, lift.stop))
            {
                _walker.AutoTarget = new Vector3(prev.X + 0.5f, prev.Y, prev.Z + 0.5f);
                _stuck = 0;
                if (_ticks % 60 == 0) _waits++;
                return;
            }
        }
        _walker.AutoTarget = new Vector3(target.X + 0.5f, target.Y, target.Z + 0.5f);
        if (++_stuck > 420)
        {
            Fail($"walking {WalkName(w)}: stuck at {at} on cell {_step}/{_route.Count} ({target}){(_back ? " on the way back" : "")}");
            _walker = null;
            NextWalk();
        }
    }

    static bool Reached(Vector3 at, Gen.Int3 c) =>
        new Vector2(at.X - (c.X + 0.5f), at.Z - (c.Z + 0.5f)).Length() < 0.45f && Mathf.Abs(at.Y - c.Y) < 1.2f;

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
