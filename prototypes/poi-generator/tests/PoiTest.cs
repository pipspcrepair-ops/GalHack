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
        // Its own time limit: a run that hangs quits by itself and says so (nobody has to end it by hand).
        // A wall-clock limit on a thread of its own, for a run whose loop has hung.
        new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(35));
            Console.Error.WriteLine("PoiTest took over 35 minutes of wall-clock time: watchdog");
            System.Environment.Exit(2);
        }) { IsBackground = true }.Start();
        GetTree().CreateTimer(1800.0, processAlways: true, processInPhysics: false, ignoreTimeScale: true).Timeout += () =>
        {
            GD.PrintErr("PoiTest timed out after 30 minutes");
            GalHack.Testing.Backstop.Quit(this, 2);
        };
        string only = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--only="))?["--only=".Length..];
        var checks = new (string Name, Action Run)[]
        {
            ("Grand", GrandPlaces), ("FactionRolls", FactionRolls), ("CityLots", CityLots), ("Roofs", Roofs), ("Lights", LightMoods), ("Merged", MergedFaces), ("Layers", WorldLayers), ("Occupancy", Occupancy), ("Regions", Regions), ("Data", Data), ("Facades", Facades), ("Items", Items), ("Determinism", Determinism), ("KitValidation", KitValidation),
            ("KitAuthored", KitAuthored), ("ArchAuthored", ArchAuthored), ("Thousands", Thousands), ("Corridors", Corridors),
            ("Sense", Sense), ("Variety", Variety), ("Depths", Depths), ("DepthsSheet", DepthsSheet), ("Underground", UndergroundNet), ("Cores", Cores), ("Lights", LightsApi), ("StateTransforms", StateTransforms),
            ("LootByRarity", LootByRarity), ("LootByDepth", LootByDepth), ("Carved", Carved), ("Hidden", Hidden),
            ("Breaches", Breaches), ("NoLocks", NoLocks), ("PlacementRules", PlacementRules), ("NoRepeatsInSystem", NoRepeatsInSystem),
            ("SeenWeighting", SeenWeighting), ("RegrowthPolicy", RegrowthPolicy), ("RevealDistances", RevealDistances),
            ("WorldPlacement", WorldPlacementCheck), ("WorldProfiles", WorldProfiles), ("Ports", PortsAndPeople), ("StartWorlds", StartWorlds), ("Tethers", Tethers), ("Stamps", Stamps), ("Geometry", Geometry), ("RequiredRooms", RequiredRooms), ("Ages", Ages), ("FactionMix", FactionMix), ("Townsfolk", TownsfolkCheck), ("Blasts", Blasts), ("StartSystems", StartSystems), ("PlainRuins", PlainRuins), ("Houses", Houses), ("Doors", Doors), ("DoorwaySolids", DoorwaySolids), ("Surfaces", Surfaces), ("Rails", Rails), ("Decay", DecayPass), ("Coplanar", CoplanarFaces), ("Panes", Panes), ("Landmarks", Landmarks), ("ShooterTags", ShooterTags), ("Hulks", Hulks), ("Eras", Eras), ("Fallbacks", Fallbacks), ("Standard", Standard), ("References", ReferenceRanges), ("Patterns", PatternChecklist), ("Coherent", Coherent), ("Objects", Objects), ("Ornament", OrnamentCheck), ("Roads", Roads), ("RoadCost", RoadCost), ("PlaceCost", PlaceCost), ("Fresh", Fresh), ("Scale", Scale), ("Yards", Yards), ("TypeSizes", TypeSizes), ("TypesBuilt", TypesBuilt), ("HeldPlaces", HeldPlaces), ("Performance", Performance),
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
            Finish();
            return;
        }
        // The walks run on physics ticks, so they go last; see _PhysicsProcess.
        if (only == null || only.Split(',').Contains("Walk")) NextWalk();
        else Finish();
    }

    // ---- the large places --------------------------------------------------------------------

    IEnumerable<FamilyDef> Large => _lib.Families.Where(f => f.Tags.Contains("grand"));

    /// <summary>
    /// The large places on fresh seeds: a layout per tech variant passes the layout checks, each spawned
    /// in every state passes validation, they keep to the 2 m lattice and are far bigger than the old
    /// shoebox. Fewer seeds than the mass runs, because each is a whole works.
    /// </summary>
    void GrandPlaces()
    {
        var sw = Stopwatch.StartNew();
        int built = 0, spawned = 0, bad = 0;
        var problems = new List<string>();
        ulong fresh = (ulong)Random.Shared.NextInt64();
        // What a large plan got wrong, all of it at once: a big place is too slow to fix one complaint at a time.
        int told = 0;
        Architect.Rejected = (layout, why) =>
        {
            if (told++ > 0) return;
            foreach (string w in why) GD.Print("      rejected: " + w);
            var nav = new NavGrid(layout.Blocks, layout.Occupied, layout.Extras.Lifts, layout.Extras.InSpace);
            var reach = nav.Reachable(layout.EntranceCell);
            foreach (var m in layout.Modules)
                if (!m.Cells.Any(reach.Contains)) GD.Print($"      unreachable {m.Purpose} storey {m.Storey} {m.Min} to {m.Max}");
        };
        foreach (FamilyDef f in Large)
            foreach (VariantDef v in f.Variants.Concat(f.Variants))
            {
                ulong seed = Hash.Combine(fresh, Hash.Of(f.Id), (ulong)Random.Shared.NextInt64());
                Layout layout;
                var t0 = sw.ElapsedMilliseconds;
                // Every way round: a place is turned to face what it served.
                try { layout = PoiFactory.BuildLayout(_lib, f, v.Index, f.Rarity, seed, new LayoutHints(Dirs.Horizontal[Random.Shared.Next(4)])); }
                catch (Exception e) { bad++; problems.Add($"{f.Id} {v.Name} seed {seed}: {e.Message}"); continue; }
                built++;
                Int3 size = layout.Blocks.Size;
                long t1 = sw.ElapsedMilliseconds;
                GD.Print($"      {f.Id} '{v.Name}' {size.X} x {size.Z} x {size.Y} m, {layout.Modules.Count} rooms, {layout.Blocks.CountBlocks()} cells, layout {t1 - t0} ms, {layout.Attempts} attempts");
                if (Math.Max(size.X, size.Z) < 100 && !f.Tags.Contains("city")) { bad++; problems.Add($"{f.Id} is only {size.X} by {size.Z} m"); }
                foreach (PoiState s in Enum.GetValues<PoiState>())
                {
                    if (f.StateWeights[(int)s] <= 0f) continue;
                    long t2 = sw.ElapsedMilliseconds;
                    PoiInstance inst = PoiFactory.Spawn(_lib, layout, s, seed + (ulong)s);
                    spawned++;
                    foreach (string p in Validator.Check(inst).Take(3)) { bad++; problems.Add($"{f.Id} '{v.Name}' seed {seed} {s}: {p}"); }
                    GD.Print($"        {s}: {sw.ElapsedMilliseconds - t2} ms, {inst.Enemies.Count} enemies, removed {inst.RemovedFraction:P0}");
                }
            }
        Architect.Rejected = null;
        foreach (string p in problems.Take(12)) GD.PrintErr("      " + p);
        Check(bad == 0 && built > 0, $"large places: {built} layouts and {spawned} spawns pass the layout checks and validation, in {sw.ElapsedMilliseconds} ms");
    }

    /// <summary>Lights keep a place's feel: dying places mostly burn with a few flickering or failed, hostile ones pulse red, high-tech ones paint some light on as strips.</summary>
    void LightMoods()
    {
        ulong seed = (ulong)Random.Shared.NextInt64();
        int steady = 0, flicker = 0, dead = 0, red = 0, hostile = 0, strips = 0, unstyled = 0;
        foreach (FamilyDef f in _lib.Families.Where(f => !f.Space && f.Faction == Faction.None))
        {
            Layout layout = PoiFactory.BuildLayout(_lib, f, 0, f.Rarity, seed);
            if (f.StateWeights[(int)PoiState.Abandoned] > 0f)
                foreach (LightSpot l in PoiFactory.Spawn(_lib, layout, PoiState.Abandoned, seed + 1).Lights.Where(l => l.Mode is not (LightMode.Bloom or LightMode.Beacon)))
                {
                    if (l.Mode == LightMode.Dead) dead++;
                    else if (l.Style is not { } st) unstyled++;
                    else if (st.Anim == GalHack.Structures.LightAnim.Flicker) flicker++;
                    else steady++;
                }
            if (f.StateWeights[(int)PoiState.Infested] > 0f)
            {
                var inf = PoiFactory.Spawn(_lib, layout, PoiState.Infested, seed + 2);
                foreach (LightSpot l in inf.Lights.Where(l => l.Style != null)) { hostile++; if (l.Style!.Value.Anim == GalHack.Structures.LightAnim.Pulse && l.Style.Value.Color == 0xff3322) red++; }
            }
            if (f.Techs.Contains("high") || f.Techs.Count == 0)
                strips += PoiFactory.Spawn(_lib, layout, PoiState.Active, seed + 3, new WorldProfile { Before = TechLevel.High, Now = TechLevel.High }).LitFaces.Count;
        }
        int lit = steady + flicker + dead;
        Check(lit > 50 && unstyled == 0 && steady > flicker && flicker > 0 && dead > 0 && dead < lit / 4,
            $"dying places: of {lit} lamps {steady} still burn, {flicker} flicker and {dead} have failed; {unstyled} without a style");
        Check(hostile > 0 && red == hostile, $"hostile places pulse red: {red} of {hostile} lamps");
        Check(strips > 0, $"high-tech places paint light on as strips: {strips} glowing faces");
    }

    /// <summary>Every faction place lays itself out on any roll: every family, many fresh seeds, every turn and rarity, with no failure at all.</summary>
    void FactionRolls()
    {
        var families = _lib.Families.Where(f => f.Site is { } s && _lib.Sites[s].Parts.Count > 0 && _lib.Sites[s].Parts[0].Kind == "grand" && f.Faction != Faction.None || f.Site?.StartsWith("faction_") == true).Distinct().ToList();
        ulong fresh = (ulong)Random.Shared.NextInt64();
        int rolls = 0, failed = 0;
        var why = new Dictionary<string, int>();
        foreach (FamilyDef f in families)
            for (int k = 0; k < (f.Id == "vharn_outpost" ? 1500 : 120); k++)
            {
                ulong seed = Hash.Combine(fresh, Hash.Of(f.Id), (ulong)k);
                // Each pick from its own bits, so every turn meets every variant, tech and rarity.
                var hints = new LayoutHints(Dirs.Horizontal[(int)(seed % 4)], (TechLevel)(seed / 4 % 3));
                int variant = (int)(seed / 12 % (ulong)f.Variants.Count);
                Rarity rarity = seed / 97 % 2 == 0 ? f.Rarity : (Rarity)(seed / 194 % 5);
                rolls++;
                try { PoiFactory.BuildLayout(_lib, f, variant, rarity, seed, hints); }
                catch (Exception e)
                {
                    failed++;
                    string key = $"{f.Id}: {e.Message.Split("last: ").Last().Split(" @ ")[0]}";
                    why[key] = why.GetValueOrDefault(key) + 1;
                }
            }
        foreach (var (k, n) in why.OrderByDescending(w => w.Value).Take(8)) GD.Print($"      {n} x {k}");
        Check(failed == 0 && rolls > 0, $"faction places lay out on every roll: {rolls - failed} of {rolls} over {families.Count} families ({failed} failed, {100.0 * failed / Math.Max(1, rolls):F1}%)");

        // The crash's own case: data asking for a plan the code doesn't know (a build older than its data). The
        // world's door must leave the place out and say why, never throw.
        FamilyDef hive = families.First(f => f.Id == "vharn_outpost");
        SitePart part = _lib.Sites[hive.Site!].Parts[0];
        string what = part.Arg("what");
        Layout? none = null;
        string? reason = null;
        bool threw = false;
        part.Args["what"] = "no_such_plan";
        try { none = PoiFactory.TryLayout(_lib, hive, 0, hive.Rarity, fresh, null, out reason); }
        catch (Exception) { threw = true; }
        finally { part.Args["what"] = what; }
        Check(!threw && none == null && reason != null, $"a place that can't lay out is left out, not thrown: {(threw ? "threw" : reason ?? "laid out")}");
    }

    /// <summary>
    /// Every city lot the planner can ask for lays out: each kind at the lot sizes, storeys, styles, falls,
    /// leans, topples, hillside steps and turns it rolls, with no failure; each sets its gate on its front lot
    /// line, and nothing it builds above ground strays past the lot.
    /// </summary>
    void CityLots()
    {
        ulong fresh = (ulong)Random.Shared.NextInt64();
        int rolls = 0, failed = 0, stray = 0, gates = 0;
        var why = new Dictionary<string, int>();
        var rng = new Rng(fresh);
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        foreach (string kind in new[] { "tower", "midrise", "terrace", "house", "works", "civic", "station", "police", "fire", "school", "university" })
        {
            FamilyDef f = _lib.Families.First(x => x.Id == "city_" + kind);
            for (int k = 0; k < 40; k++)
            {
                float fall = rng.Float();
                int Step() => rng.Chance(0.4f) ? rng.Range(1, 6) : 0;
                var lot = kind switch
                {
                    "tower" => new CityLot(kind, rng.Range(17, 19), 17, rng.Range(2, 31), rng.Range(0, 1000), fall, Lean: rng.Chance(0.3f), Toppled: rng.Chance(0.2f)),
                    "midrise" => new CityLot(kind, rng.Range(12, 16), 17, rng.Range(2, 9), rng.Range(0, 1000), fall, Lean: rng.Chance(0.2f)),
                    "terrace" => new CityLot(kind, new[] { 9, 10, 13, 14, 17 }[rng.Range(0, 5)], 17, rng.Range(1, 4), rng.Range(0, 1000), fall, Back: Step(), Left: Step(), Right: Step(), EndRight: rng.Chance(0.3f)),
                    "house" => new CityLot(kind, rng.Range(10, 13), 17, rng.Range(1, 3), rng.Range(0, 1000), fall, Back: Step(), Left: Step(), Right: Step(), EndRight: rng.Chance(0.3f)),
                    "works" => new CityLot(kind, rng.Range(17, 21), 17, 1, rng.Range(0, 1000), fall),
                    "police" or "fire" => new CityLot(kind, rng.Range(15, 21), 17, 2, rng.Range(0, 1000), fall, Back: Step(), Left: Step(), Right: Step(), EndRight: rng.Chance(0.3f)),
                    _ => new CityLot(kind, 34, 34, 1, rng.Range(0, 1000), fall),
                };
                rolls++;
                Dir front = Dirs.Horizontal[k % 4];
                try
                {
                    Layout layout = PoiFactory.BuildLayout(_lib, f, k % f.Variants.Count, f.Rarity, rng.NextU64(), new LayoutHints(front, (TechLevel)(k % 3), lot));
                    // The lot in the place's own cells: u along the front, v back from it, turned to its front (Plot.Cell).
                    int w = 2 * lot.W, d = 2 * lot.D;
                    (int U, int V) Frame(Int3 c) => front switch { Dir.North => (c.X, c.Z), Dir.South => (-1 - c.X, -1 - c.Z), Dir.East => (c.Z, -1 - c.X), _ => (-1 - c.Z, c.X) };
                    bool In(Int3 c) => Frame(c) is var (u, v) && u >= 0 && u < w && v >= 0 && v < d;
                    if (layout.Blocks.Blocks().Any(b => b.Item1.Y >= 0 && !In(b.Item1))) stray++;
                    if (In(layout.Extras.Gate) && Frame(layout.Extras.Gate).V == 0) gates++;
                }
                catch (Exception e)
                {
                    failed++;
                    string key = $"{kind} {lot.W}x{lot.D}x{lot.Storeys}: {e.Message.Split("last: ").Last().Split(" @ ")[0]}";
                    why[key] = why.GetValueOrDefault(key) + 1;
                }
            }
        }
        foreach (var (k, n) in why.OrderByDescending(w => w.Value).Take(8)) GD.Print($"      {n} x {k}");
        GD.Print($"      {rolls} city lots in {System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0} ms (fresh seed {fresh})");
        Check(failed == 0, $"city lots lay out on every roll: {rolls - failed} of {rolls} ({failed} failed)");
        Check(stray == 0, $"nothing a city lot builds above ground stands past its lot lines ({stray} of {rolls - failed} strayed)");
        Check(gates == rolls - failed, $"every city lot's gate is on its front lot line ({gates} of {rolls - failed})");
    }

    /// <summary>A roof's angles are slopes: no family stacks cubes up a pitch, three courses or more, under the sky.</summary>
    void Roofs()
    {
        ulong seed = (ulong)Random.Shared.NextInt64();
        var bad = new Dictionary<string, int>();
        foreach (FamilyDef f in _lib.Families.Where(f => !f.Space))
            for (int v = 0; v < f.Variants.Count; v++)
            {
                BlockGrid g = PoiFactory.BuildLayout(_lib, f, v, f.Rarity, seed).Blocks;
                var top = new Dictionary<(int, int), (int Y, Block B)>();
                foreach (var (c, b) in g.Blocks())
                    if (BlockInfo.Collides(b.Type) && (!top.TryGetValue((c.X, c.Z), out var t) || c.Y > t.Y)) top[(c.X, c.Z)] = (c.Y, b);
                bool Step(int x, int z, int y) => top.TryGetValue((x, z), out var t) && t.Y == y && t.B.Type == BlockType.Cube && t.B.Role == Role.Roof;
                int steps = 0;
                foreach (var ((x, z), t) in top)
                    foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                        if (t.Y > 0 && Step(x, z, t.Y) && Step(x + dx, z + dz, t.Y + 1) && Step(x + 2 * dx, z + 2 * dz, t.Y + 2)) steps++;
                if (steps > 0) bad[f.Id] = bad.GetValueOrDefault(f.Id) + steps;
            }
        Check(bad.Count == 0, $"no stepped roofs in any family{(bad.Count > 0 ? ": " + string.Join(", ", bad.OrderByDescending(b => b.Value).Take(8).Select(b => $"{b.Key} {b.Value}")) : "")}");
    }

    // ---- worlds from layers ------------------------------------------------------------------

    /// <summary>The layers are data: they parse, a world rolls one of each it can, the same seed the same roll, and they combine rather than replace.</summary>
    void WorldLayers()
    {
        var kinds = _lib.Layers.GroupBy(l => l.Kind).ToDictionary(g => g.Key, g => g.Count());
        Check(kinds.GetValueOrDefault(LayerKind.Theme) == 44 && kinds.GetValueOrDefault(LayerKind.Type) == 39 && kinds.GetValueOrDefault(LayerKind.Fall) == 82 && kinds.GetValueOrDefault(LayerKind.Dress) == 189,
            $"every table of the layers doc loads: {string.Join(", ", kinds.Select(k => $"{k.Value} {k.Key}"))}");
        string[] worlds = { "Temperate", "Arid", "Ice", "Lava", "Alien" };
        var themes = new Dictionary<string, int>();
        var combos = new HashSet<string>();
        int rolls = 0, same = 0, plain = 0, falling = 0, roughStarts = 0, starts = 0, lowHusks = 0, lows = 0, openFoul = 0, foul = 0;
        var fams = new HashSet<string>();
        ulong baseSeed = (ulong)Random.Shared.NextInt64();
        for (ulong i = 0; i < 2000; i++)
        {
            ulong seed = Hash.Combine(baseSeed, i);
            string w = worlds[i % 5];
            bool start = i % 7 == 0;
            WorldProfile profile = WorldProfile.Roll(seed, w, start);
            if (!profile.Inhabited) continue;
            WorldRoll a = WorldRoll.Roll(_lib, seed, w, profile, start)!, b = WorldRoll.Roll(_lib, seed, w, profile, start)!;
            rolls++;
            if (a.ToString() == b.ToString()) same++;
            themes[a.Theme.Id] = themes.GetValueOrDefault(a.Theme.Id) + 1;
            combos.Add(a.ToString());
            fams.UnionWith(a.Poi.Keys);
            if (a.Theme.Blank) plain++;
            if (a.Fact("phase") == "falling") falling++;
            if (start) { starts++; if (a.Rolled.Any(l => l.Is.Contains("rough")) || a.Occupancy.ContainsKey("FAC")) roughStarts++; }
            if (a.Fact("pre") == "L") { lows++; if (a.Occupancy.ContainsKey("HSK")) lowHusks++; }
            if (a.Fact("air0") is "foul" or "none")
            {
                foul++;
                profile = profile with { Barred = a.Barred };
                if (profile.Fits(_lib.Family("old_farm"), PoiState.Ruined)) openFoul++;
            }
        }
        Check(same == rolls, $"the same seed rolls the same world ({same} of {rolls})");
        Check(themes.Count >= 25 && themes.Values.Max() < rolls / 2, $"themes come up by how they fit: {themes.Count} of them, the top {themes.OrderByDescending(t => t.Value).First()}");
        Check(combos.Count == rolls, $"no two worlds the same: {combos.Count} in {rolls} rolls");
        Check(plain > rolls / 5 && plain < rolls * 2 / 5, $"about three settled worlds in ten have no mood at all: {plain * 100 / rolls}%");
        Check(falling > 0 && falling < rolls / 12, $"mid-collapse worlds stay rare: {falling * 100f / rolls:F1}%");
        Check(roughStarts == 0 && starts > 0, $"start worlds roll nothing rough and no faction holding ground ({roughStarts} of {starts})");
        Check(lowHusks == 0 && lows > 0, $"husks need mid or high tech: none on {lows} low-tech worlds");
        Check(openFoul == 0 && foul > 0, $"air filters families: a farm stands on none of {foul} worlds built under bad air ({openFoul} did)");

        // The first themes to build: each old world's parts hold together on a roll, and show in what it adds up to.
        WorldProfile mid = new() { Before = TechLevel.Mid, Now = TechLevel.Low, Starport = Starport.C, Current = PopulationNow.Survivors };
        foreach (var (id, family) in new[] { ("long_queue", "evacuation_camp"), ("road_warrens", "scrap_fort"), ("glass_plains", "blast_crater"), ("fever_jungle", "biolab"), ("hush_suburbs", ""), ("quarantine", "hospital"), ("last_train", ""), ("salt_and_bone", ""), ("sentry_world", ""), ("folded_town", "") })
        {
            WorldLayer old = _lib.Layers.First(l => l.Kind == LayerKind.Old && l.Id == id);
            var given = new WorldRoll.Given();
            var kindOf = Enum.GetValues<LayerKind>().ToDictionary(k => k.ToString().ToLowerInvariant());
            foreach (var part in old.Parts.Select(p => p.Split('.')).Where(p => kindOf.ContainsKey(p[0])).GroupBy(p => kindOf[p[0]]))
                given.Set(part.Key, part.Select(p => p[1]).ToArray());
            WorldRoll r = WorldRoll.Roll(_lib, Hash.Combine(baseSeed, Hash.Of(id)), "Temperate", mid, false, given)!;
            bool holds = old.Parts.Where(p => !p.StartsWith("scar.") && !p.StartsWith("pre.") && !p.StartsWith("post.") && !p.StartsWith("unsettled.")).All(p => r.Rolled.Any(l => $"{l.Kind.ToString().ToLowerInvariant()}.{l.Id}" == p));
            var table = r.Poi.Keys.Concat(r.Hub.Keys).Concat(r.Industry.Keys).Concat(r.Farm.Keys).ToHashSet();
            Check(holds && (family == "" || table.Contains(family)), $"{id}: its parts make a world, theme {r.Theme.Id}, {string.Join(", ", r.Occupancy.OrderByDescending(o => o.Value).Take(4).Select(o => $"{o.Key} {o.Value:F0}"))}, places {string.Join(" ", table.Take(6))}");
        }

        // Layers add up: a presence changes who's in places, and leaves the rest of the mix as it was.
        ulong one = Hash.Combine(baseSeed, 77);
        WorldRoll quiet = WorldRoll.Roll(_lib, one, "Temperate", mid, false, new WorldRoll.Given().Set(LayerKind.Presence, "hermits").Set(LayerKind.Theme, "long_queue"))!;
        WorldRoll gangs = WorldRoll.Roll(_lib, one, "Temperate", mid, false, new WorldRoll.Given().Set(LayerKind.Presence, "hermits", "gangs").Set(LayerKind.Theme, "long_queue"))!;
        Check(gangs.Occupancy.GetValueOrDefault("GNG") > quiet.Occupancy.GetValueOrDefault("GNG"), $"layers add up: a gang presence takes gangs from {quiet.Occupancy.GetValueOrDefault("GNG")} to {gangs.Occupancy.GetValueOrDefault("GNG")}");
    }

    /// <summary>The same layout, different people: what each occupant leaves is theirs, nobody's plan changes, and it all still validates.</summary>
    /// <summary>The merged drawing and collision of a large place are the same surfaces as the plain ones, in far fewer faces.</summary>
    void MergedFaces()
    {
        ulong seed = (ulong)Random.Shared.NextInt64();
        foreach (string id in new[] { "sewage_works", "old_farm", "works_complex" })
        {
            FamilyDef f = _lib.Family(id);
            PoiInstance inst = PoiFactory.Spawn(_lib, PoiFactory.BuildLayout(_lib, f, 0, f.Rarity, seed), PoiState.Ruined, seed + 1);
            BlockGrid g = inst.Blocks;
            MeshOptions Options(bool merged) => new()
            {
                ChunkSize = 32, Doors = false, Collision = true, Merged = merged, Drawn = true,
                RockMin = new Int3(1, 0, 1), RockMax = new Int3(0, 0, 0), RockBottom = g.Min.Y, GroundShow = MeshOptions.AllGround,
            };
            static double Area(List<ChunkMesh> meshes)
            {
                double total = 0;
                foreach (ChunkMesh m in meshes)
                    for (int i = 0; i + 8 < m.Collision.Count; i += 9)
                    {
                        var a = new V3(m.Collision[i], m.Collision[i + 1], m.Collision[i + 2]);
                        var e = new V3(m.Collision[i + 3], m.Collision[i + 4], m.Collision[i + 5]) - a;
                        var h = new V3(m.Collision[i + 6], m.Collision[i + 7], m.Collision[i + 8]) - a;
                        V3 c = V3.Cross(e, h);
                        total += Math.Sqrt(V3.Dot(c, c)) * 0.5;
                    }
                return total;
            }
            List<ChunkMesh> plain = Mesher.Build(g, inst.Layout.Occupied, Options(false)), merged = Mesher.Build(g, inst.Layout.Occupied, Options(true));
            double a0 = Area(plain), a1 = Area(merged);
            int q0 = plain.Sum(m => m.Quads), q1 = merged.Sum(m => m.Quads);
            int t0 = plain.Sum(m => m.Collision.Count), t1 = merged.Sum(m => m.Collision.Count);
            Check(Math.Abs(a0 - a1) < a0 * 1e-4, $"{id}: what you touch from further off is what you touch up close: {a1:F0} m² of collision merged, {a0:F0} plain");
            // (Collision is merged into the fewest rectangles in both: every chunk's colliding faces are, near or far. So the
            // merged mesh's collision is only held to no more than the plain one's, give or take a few split rectangles.)
            Check(q1 * 3 < q0 && t1 <= t0 + t0 / 500 + 9, $"{id}: merged, {q1} faces for {q0}, {t1 / 9} collision triangles for {t0 / 9}");
            ChunkMesh far = Mesher.Coarse(g, inst.Layout.Occupied);
            Check(far.Quads > 0 && far.Quads * 20 < q0, $"{id}: the far mesh is {far.Quads} faces");
        }
    }

    void Occupancy()
    {
        ulong seed = (ulong)Random.Shared.NextInt64();
        FamilyDef sewage = _lib.Family("sewage_works"), hospital = _lib.Family("hospital");
        int bad = 0;
        string first = "";
        foreach (FamilyDef f in new[] { sewage, hospital })
        {
            Layout layout = PoiFactory.BuildLayout(_lib, f, 0, f.Rarity, seed);
            var counts = new Dictionary<Occupant, Dictionary<BlockType, int>>();
            var enemies = new Dictionary<Occupant, int>();
            var people = new Dictionary<Occupant, (int Count, Mood Mood)>();
            var walls = new HashSet<int>();
            foreach (Occupant who in Enum.GetValues<Occupant>())
            {
                var inst = PoiFactory.Spawn(_lib, layout, PoiState.Abandoned, seed + 3, null, new OccupancyRoll(who));
                foreach (string p in Validator.Check(inst).Take(2)) { bad++; if (first.Length == 0) first = $" ({f.Id}, {who}: {p})"; }
                counts[who] = inst.Blocks.Blocks().GroupBy(b => b.Block.Type).ToDictionary(g => g.Key, g => g.Count());
                enemies[who] = inst.Enemies.Count;
                people[who] = (inst.Townsfolk?.People.Count ?? 0, inst.Mood);
                walls.Add(inst.Blocks.Blocks().Count(b => b.Block.Role is Role.Wall or Role.Roof));
            }
            int Of(Occupant who, BlockType t) => counts[who].GetValueOrDefault(t);
            Check(enemies[Occupant.Empty] == 0, $"{f.Id}: an empty place is empty");
            Check(Of(Occupant.Nesters, BlockType.Pod) == 0 && enemies[Occupant.Nesters] > 0, $"{f.Id}: nesters build no nests and leave {enemies[Occupant.Nesters]} creatures");
            Check(Of(Occupant.Scavengers, BlockType.Ash) > Of(Occupant.Empty, BlockType.Ash) && enemies[Occupant.Scavengers] == 0 && people[Occupant.Scavengers] is { Count: >= 2, Mood: Mood.Wary }, $"{f.Id}: scavengers leave a camp fire and are no one's enemies: {people[Occupant.Scavengers].Count} people, {people[Occupant.Scavengers].Mood}");
            Check(enemies[Occupant.Squatters] == 0 && people[Occupant.Squatters].Mood == Mood.Peaceful, $"{f.Id}: squatters are at home and at peace: {people[Occupant.Squatters].Count} people");
            Check(Of(Occupant.Gang, BlockType.Barricade) > Of(Occupant.Empty, BlockType.Barricade) + 3 && enemies[Occupant.Gang] > 0, $"{f.Id}: a gang leaves {Of(Occupant.Gang, BlockType.Barricade)} barricades and {enemies[Occupant.Gang]} of its own");
            Check(Of(Occupant.Turned, BlockType.Remains) > Of(Occupant.Empty, BlockType.Remains) + 3, $"{f.Id}: the turned leave {Of(Occupant.Turned, BlockType.Remains)} bodies");
            Check(Of(Occupant.Squatters, BlockType.Bunk) > 0, $"{f.Id}: squatters leave {Of(Occupant.Squatters, BlockType.Bunk)} bunks");
            Check(walls.Count == 1, $"{f.Id}: the layout is the same whoever is in it ({walls.Count} wall counts)");
            var both = PoiFactory.Spawn(_lib, layout, PoiState.Abandoned, seed + 3, null, new OccupancyRoll(Occupant.Gang, Occupant.Nesters, Sharing.Floors));
            foreach (string p in Validator.Check(both).Take(2)) { bad++; if (first.Length == 0) first = $" ({f.Id} mixed: {p})"; }
            Check(both.Enemies.Any(e => e.Kind == EnemyKind.Fauna) && both.Enemies.Any(e => e.Kind == EnemyKind.Guard), $"{f.Id}: a mixed place holds both groups, {both.Occupancy}");
        }
        Check(bad == 0, $"every occupant leaves every place walkable{first}");
        var mix = new Dictionary<string, float> { ["EMP"] = 60, ["SCV"] = 30, ["NST"] = 10 };
        int empty = 0, n = 4000, two = 0, traces = 0;
        for (ulong i = 0; i < (ulong)n; i++)
        {
            OccupancyRoll r = OccupancyLayer.Roll(mix, sewage, Hash.Combine(seed, i));
            if (r.Main == Occupant.Empty) empty++;
            if (r.Second != null) two++;
            if (r.Trace != null) traces++;
        }
        Check(Math.Abs(empty / (float)n - 0.6f) < 0.05f, $"a theme's mix sets the odds: {empty * 100 / n}% empty of a mix of 60");
        Check(two > 0 && traces > 0, $"some places hold two groups ({two}), some only the trace of one ({traces})");
    }

    /// <summary>Plain land with a sea down one side and a slope across it: enough to read a plan's reasons.</summary>
    sealed class TestLand : ILand
    {
        readonly ulong _seed;
        public TestLand(ulong seed) => _seed = seed;
        public double Width => 16000;
        public double Depth => 8000;
        public float Height(double x, double z) => 30f + (float)x * 0.006f + PoiGenerator.Gen.Noise.Fbm(_seed, (float)x, (float)z, 0.0015f) * 16f;
        public bool Wet(double x, double z) => x < 900;
        public bool Rich(double x, double z) => PoiGenerator.Gen.Noise.Fbm(_seed + 1, (float)x, (float)z, 0.003f) > 0.6f;
    }

    /// <summary>Plans on a test land: deterministic, every place with its reason, things where their reason says, and most of the land left alone.</summary>
    void Regions()
    {
        int plans = 0, places = 0, reasons = 0, spaced = 0, wet = 0, sewage = 0, downhill = 0, rails = 0, unDepot = 0, powers = 0, differ = 0, breaks = 0, same = 0;
        float wild = 0f;
        ulong baseSeed = (ulong)Random.Shared.NextInt64();
        for (ulong i = 0; i < 24; i++)
        {
            ulong seed = Hash.Combine(baseSeed, i);
            WorldProfile profile = WorldProfile.Roll(seed, "Temperate");
            if (!profile.Inhabited) continue;
            WorldRoll roll = WorldRoll.Roll(_lib, seed, "Temperate", profile, false)!;
            var land = new TestLand(seed);
            RegionPlan plan = RegionPlan.Make(_lib, land, roll, profile, seed, (8000, 4000), false);
            RegionPlan again = RegionPlan.Make(_lib, land, roll, profile, seed, (8000, 4000), false);
            plans++;
            if (plan.Places.Select(p => (p.Family, p.X, p.Z)).SequenceEqual(again.Places.Select(p => (p.Family, p.X, p.Z)))) same++;
            foreach (PlannedPlace p in plan.Places)
            {
                places++;
                if (string.IsNullOrEmpty(p.Why) || string.IsNullOrEmpty(p.Role)) reasons++;
                if (land.Wet(p.X, p.Z)) wet++;
                if (p.Breaks) breaks++;
                foreach (PlannedPlace o in plan.Places)
                    if (o.Index > p.Index && plan.Dist(o.X, o.Z, p.X, p.Z) < 150) spaced++;
                if (p.Family == "sewage_works" && p.Serves >= 0)
                {
                    sewage++;
                    if (land.Height(p.X, p.Z) <= land.Height(plan.Places[p.Serves].X, plan.Places[p.Serves].Z) + 1f) downhill++;
                }
            }
            foreach (PlannedLink l in plan.Links)
            {
                if (l.Kind == "rail")
                {
                    rails++;
                    if (plan.Places[l.A].Role != "depot" && plan.Places[l.B].Role != "depot") unDepot++;
                }
                if (l.Kind == "power")
                {
                    powers++;
                    if (plan.Places[l.A].Role != "plant") reasons++;
                }
            }
            int cells = 0, open = 0;
            for (double z = 100; z < 8000; z += 400)
                for (double x = 1000; x < 16000; x += 400)
                {
                    cells++;
                    if (plan.RegionAt(x, z) == null) open++;
                }
            wild += open / (float)cells;
        }
        Check(same == plans && plans > 8, $"{plans} plans, each the same again from its seed ({same})");
        Check(reasons == 0 && places > plans * 4, $"{places} planned places, every one with its reason ({reasons} not)");
        Check(wet == 0 && spaced == 0, $"places stand on land, apart ({wet} wet, {spaced} crowded)");
        Check(sewage > 0 && downhill >= sewage * 0.8f, $"sewage works run downhill of their towns: {downhill} of {sewage}");
        Check(unDepot == 0, $"{rails} rail links, each ending at a depot ({unDepot} not)");
        Check(powers > 0 || plans == 0, $"{powers} plants feed their towns");
        Check(breaks > 0, $"{breaks} of {places} places break from the theme");
        Check(wild / plans > 0.4f, $"most of the land is left empty: {wild / plans:P0}");
        _ = differ;
    }

    void Finish()
    {
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} CHECK(S) FAILED");
        GalHack.Testing.Backstop.Quit(this, _failures == 0 ? 0 : 1);
    }

    /// <summary>The planned places the mass runs walk through in their hundreds. The large places are whole works and have their own check (<see cref="GrandPlaces"/>).</summary>
    IEnumerable<FamilyDef> Arch => _lib.Families.Where(f => f.Generator != Generator.Kit && !f.Tags.Contains("grand"));
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
                           "every room, storey and cache reachable, the deepest point reachable, nothing floating, no overlaps, cores only where held");
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
    /// The networks under a town, on invented ground of three kinds at each tech level (the planet
    /// surface's tests do the same on real towns). Walked by the same grid walker as every place: every
    /// way in, every room and every stretch of tunnel reachable from the first way in; no open cell
    /// against bare rock; the right things at the right tech level.
    /// </summary>
    void UndergroundNet()
    {
        int plans = 0, badHoles = 0, holes = 0, ways = 0, rooms = 0, columns = 0, unreached = 0, leaks = 0, wrongTech = 0, outfalls = 0, stations = 0, stubs = 0, spaces = 0, unlinked = 0;
        var notes = new List<string>();
        var grounds = new (string Name, Func<int, int, float> F)[]
        {
            ("flat", (_, _) => 40.5f),
            ("tilted", (x, z) => 60f - (x - 1000) * 0.06f - (z - 1000) * 0.03f),
            ("rolling", (x, z) => 50f + 7f * MathF.Sin(x / 47f) * MathF.Cos(z / 61f) - (x - 1000) * 0.03f),
        };
        foreach (TechLevel tech in new[] { TechLevel.Low, TechLevel.Mid, TechLevel.High })
            foreach (var (name, ground) in grounds)
                foreach (var (ax, az, back) in new[] { (1, 0, 1), (-1, 0, -1), (0, 1, -1), (0, -1, 1) })
                {
                    var site = new UnderSite { X = 1000, Z = 1000, AlongX = ax, AlongZ = az, BackSide = back, Tech = tech, Seed = (ulong)(plans + 3), Ground = ground };
                    UnderPlan plan = Underground.Plan(site);
                    UnderBuilt built = Underground.Build(plan);
                    plans++;
                    string where = $"{tech} {name} ({ax},{az})";
                    if (plan.HasMetro != UnderPlan.Metro(tech)) { wrongTech++; notes.Add("metro on " + where); }
                    // Brick sewers on a low-tech world: none of the higher levels' poured concrete or composite.
                    if (tech == TechLevel.Low && built.Blocks.Values.Any(b => b.Type == BlockType.Cube && b.Role == Role.Wall && b.Material is PoiGenerator.Gen.Material.Concrete or PoiGenerator.Gen.Material.Alloy)) { wrongTech++; notes.Add("concrete on " + where); }
                    stations += plan.Stations.Count;
                    // A couple of manholes a town, all off the carriageway (a road's half width is 4 m).
                    var mh = plan.Shafts.Where(k => k.Network == UnderKind.Sewer && k.ToSurface).ToList();
                    holes += mh.Count;
                    if (mh.Count is < 2 or > 3) { badHoles++; notes.Add($"{mh.Count} manholes on {where}"); }
                    foreach (UnderShaft k in mh)
                        for (int dx = -1; dx <= 2; dx++)
                            for (int dz = -1; dz <= 2; dz++)
                            {
                                int rx = k.Foot.X + dx - site.X, rz = k.Foot.Z + dz - site.Z;
                                int along = rx * ax + rz * az, across = -rx * az + rz * ax;
                                bool inRoad = Math.Abs(across) <= 4 && Math.Abs(along) <= site.MainHalf + 4;
                                for (int c = -1; c <= 1; c++) inRoad |= Math.Abs(along - c * site.Block) <= 4 && Math.Abs(across) <= site.Arm + 4;
                                inRoad |= Math.Abs(across - back * site.Arm) <= 4 && Math.Abs(along) <= site.Block + 4;
                                if (inRoad) { badHoles++; notes.Add($"manhole in the road at {k.Foot} on {where}"); dx = dz = 9; }
                            }
                    outfalls += plan.Outfalls.Count;
                    if (plan.Outfalls.Count == 0) stubs++;
                    var view = new UnderView(built);
                    var nav = new NavGrid(view, view.Mask());
                    HashSet<Int3> reach = nav.Reachable(view.Local(built.WaysIn[0]));
                    foreach (Int3 w in built.WaysIn)
                    {
                        ways++;
                        if (!reach.Contains(view.Local(w))) { unreached++; notes.Add($"way in {w} on {where}"); }
                    }
                    bool Reached(int x, int y, int z, int hx, int hz, int hw)
                    {
                        for (int k = -hw; k <= hw; k++)
                            for (int v = -1; v <= 1; v++)
                                if (reach.Contains(view.Local(new Int3(x + hx * k, y + v, z + hz * k)))) return true;
                        return false;
                    }
                    foreach (UnderRoom r in plan.Rooms)
                    {
                        rooms++;
                        Int3 c = new((r.Min.X + r.Max.X) / 2, r.Min.Y, (r.Min.Z + r.Max.Z) / 2);
                        if (!Reached(c.X, c.Y, c.Z, 1, 0, 3)) { unreached++; notes.Add($"{r.Kind} {r.Min} on {where}"); }
                    }
                    foreach (UnderRun r in plan.Runs)
                    {
                        // The far end of a run that stops in a fall of rock is the fall of rock.
                        int end = r.Length - (plan.Collapses.Any(k => k.X == r.Column(r.Length - 1).X && k.Z == r.Column(r.Length - 1).Z) ? 6 : 0);
                        for (int i = 0; i < end; i += 3)
                        {
                            columns++;
                            var (x, z) = r.Column(i);
                            if (!Reached(x, r.Floors[i], z, Math.Abs(r.DirZ), Math.Abs(r.DirX), r.HalfWidth)) { unreached++; notes.Add($"{r.Kind} column {i}/{r.Length} at {x},{z} on {where}"); break; }
                        }
                    }
                    // Sealed: nothing open opens straight onto the rock (the terrain would show through).
                    foreach (Int3 p in built.Open)
                        foreach (Dir d in Dirs.All)
                        {
                            Int3 n = p + d.Vec();
                            if (!built.Open.Contains(n) && !built.Daylight.Contains(n) && !built.Blocks.ContainsKey(n)) { leaks++; break; }
                        }
                    spaces += built.Spaces.Count;
                    unlinked += built.Spaces.Count(sp => sp.Links.Count == 0);
                    // Determinism: the plan and its blocks are a pure function of the site.
                    if (plans % 11 == 0 && Underground.Build(Underground.Plan(site)).Blocks.Count != built.Blocks.Count) { unreached++; notes.Add("not deterministic on " + where); }
                }
        Check(unreached == 0 && leaks == 0,
            $"under {plans} towns: {ways} ways in, {rooms} chambers and {columns} sampled tunnel stretches all walked to from one way in ({unreached} not), {leaks} open cells against bare rock{(notes.Count > 0 ? "; " + string.Join("; ", notes.Take(4)) : "")}");
        Check(wrongTech == 0 && stations == plans * 2 / 3 && outfalls > 0,
            $"the networks fit their worlds: {stations} metro stations and none on a low-tech world ({wrongTech} wrong), {outfalls} outfall culverts, {stubs} drains ending in a fall of rock where the ground never drops");
        Check(badHoles == 0, $"{holes} manholes under {plans} towns, two or three a town, none in a carriageway ({badHoles} wrong)");
        Check(spaces > 0 && unlinked == 0, $"{spaces} spaces for creatures, all dark, each linked to its neighbours ({unlinked} alone)");
    }

    /// <summary>
    /// Starports, exodus worlds and who lives here now. Classes follow people and tech: A and B take many
    /// people and modern or high tech, a low-tech world has D, E or none. A world has one primary port
    /// of its class, its secondaries lesser than it; an exodus world has a crowd of scrappy ports round
    /// the primary. How many live here now caps the living places, and what they can be.
    /// </summary>
    void PortsAndPeople()
    {
        var classes = new Dictionary<Starport, int>();
        int rolled = 0, inhabited = 0, wrongClass = 0, exoduses = 0, wrongExodus = 0, emptyExodus = 0, manySecondary = 0;
        var levels = new Dictionary<PopulationNow, int>();
        string[] kinds = { "temperate", "arid", "ice", "lava", "alien" };
        for (ulong s = 0; s < 4000; s++)
        {
            WorldProfile w = WorldProfile.Roll(Hash.Combine(0x70F7UL, s), kinds[s % 5], s % 9 == 0);
            rolled++;
            if (!w.Inhabited) { if (w.Starport != Starport.None) wrongClass++; continue; }
            inhabited++;
            classes[w.Starport] = classes.GetValueOrDefault(w.Starport) + 1;
            levels[w.Current] = levels.GetValueOrDefault(w.Current) + 1;
            if (w.Starport == Starport.A && (w.Population < 8 || w.Before != TechLevel.High)) wrongClass++;
            if (w.Starport == Starport.B && (w.Population < 6 || w.Before == TechLevel.Low)) wrongClass++;
            if (w.Before == TechLevel.Low && w.Starport > Starport.D) wrongClass++;
            if (w.SecondaryPorts >= 3) { manySecondary++; if (w.Starport != Starport.A) wrongClass++; }
            if (w.Exodus)
            {
                exoduses++;
                if (w.Population < 6 || w.Starport < Starport.D || w.ScrappyPorts < 6) wrongExodus++;
                if (w.Current <= PopulationNow.Survivors) emptyExodus++;
            }
        }
        float share = exoduses / (float)inhabited;
        Check(wrongClass == 0 && Enum.GetValues<Starport>().All(c => classes.GetValueOrDefault(c) > 0),
            $"starport classes follow people and tech over {inhabited} inhabited worlds: {string.Join(", ", Enum.GetValues<Starport>().Select(c => $"{c} {classes.GetValueOrDefault(c)}"))}; {manySecondary} with many secondary ports, all class A ({wrongClass} wrong)");
        Check(wrongExodus == 0 && share > 0.08f && share < 0.25f && emptyExodus > exoduses * 0.6f,
            $"exodus worlds: {exoduses} of {inhabited} inhabited ({share:P0}), all crowded once with a port to leave from ({wrongExodus} wrong), {emptyExodus} of them empty or nearly now");
        Check(Enum.GetValues<PopulationNow>().All(l => levels.GetValueOrDefault(l) > 0),
            $"who lives here now: {string.Join(", ", Enum.GetValues<PopulationNow>().Select(l => $"{l} {levels.GetValueOrDefault(l)}"))}");

        int worlds = 0, noPrimary = 0, twoPrimaries = 0, wrongPrimary = 0, secondaries = 0, wrongSecondary = 0, exodusWorlds = 0, scrappy = 0, thin = 0, camps = 0, far = 0;
        int overCap = 0, livingTooBig = 0, emptyWorlds = 0, emptyLiving = 0, tested = 0;
        var theme = _lib.Themes.First(t => t.Id == "temperate");
        for (ulong s = 0; tested < 80 && s < 4000; s++)
        {
            WorldProfile w = WorldProfile.Roll(Hash.Combine(0x70F7UL, s), kinds[s % 2], false);
            // Enough exodus and empty worlds among them to mean something.
            if (!w.Inhabited || (!w.Exodus && s % 5 != 0)) continue;
            tested++;
            var map = PlanetMap.GenerateWorld(Hash.Combine(0x5EEDUL, s), 120, 60, 80f, "ports");
            var r = WorldPlacement.Place(_lib, map, theme, Hash.Combine(0x9A1EUL, s), profile: w);
            worlds++;
            var primaries = r.Pois.Where(p => p.Primary).ToList();
            if (w.Starport != Starport.None && primaries.Count == 0) noPrimary++;
            if (primaries.Count > 1) twoPrimaries++;
            foreach (var p in primaries) if (p.Port != w.Starport) wrongPrimary++;
            foreach (var p in r.Pois.Where(p => p.Port != null && !p.Primary))
            {
                secondaries++;
                if (p.Port >= w.Starport) wrongSecondary++;
            }
            int living = r.Pois.Count(p => WorldProfile.Living(p.Family, p.State) && !WorldProfile.Unbuilt(p.Family));
            if (living > w.LivingCap) overCap++;
            foreach (var p in r.Pois.Where(p => WorldProfile.Living(p.Family, p.State) && !WorldProfile.Unbuilt(p.Family)))
                if (!w.LivingFits(p.Family)) livingTooBig++;
            if (w.Current == PopulationNow.None) { emptyWorlds++; emptyLiving += living; }
            if (w.Exodus)
            {
                exodusWorlds++;
                var main = primaries.FirstOrDefault();
                var mine = r.Pois.Where(p => p.Family.Tags.Contains("scrappy") || p.Family.Tags.Contains("exodus")).ToList();
                scrappy += mine.Count;
                camps += r.Pois.Count(p => p.Family.Tags.Contains("evacuation"));
                if (mine.Count < Math.Min(w.ScrappyPorts, 6)) thin++;
                // Crowded round the main port: nearer it than the world's other places, on the whole.
                if (main != null && mine.Count > 0)
                {
                    float near = mine.Average(p => WorldPlacement.TorusDist(map, p.CellX, p.CellY, main.CellX, main.CellY));
                    float all = r.Pois.Average(p => WorldPlacement.TorusDist(map, p.CellX, p.CellY, main.CellX, main.CellY));
                    if (near > all * 1.1f) far++;
                }
            }
        }
        Check(noPrimary == 0 && twoPrimaries == 0 && wrongPrimary == 0, $"one main starport a world, of the world's class, over {worlds} worlds ({noPrimary} without, {twoPrimaries} with two, {wrongPrimary} of the wrong class)");
        Check(wrongSecondary == 0, $"{secondaries} secondary ports, all of a lesser class ({wrongSecondary} not)");
        Check(exodusWorlds > 5 && thin == 0 && far == 0 && camps >= exodusWorlds, $"exodus worlds crowd round their main port: {exodusWorlds} worlds, {scrappy} scrappy ports and {camps} evacuation camps ({thin} worlds thin, {far} not gathered round the port)");
        Check(overCap == 0 && livingTooBig == 0 && emptyLiving == 0 && emptyWorlds > 0,
            $"living places follow who lives here now: none on {emptyWorlds} empty worlds ({emptyLiving} found), {overCap} worlds over their cap, {livingTooBig} too big for their people");
        var plain = OrbitPlacement.Place(_lib, 77, false, Starport.B);
        var great = OrbitPlacement.Place(_lib, 77, false, Starport.A);
        Check(!plain.Any(o => o.Site.Port != Starport.None) && great.Count(o => o.Site.Port == Starport.A) >= 2, $"a class A port's orbital works hang over its world ({great.Count(o => o.Site.Port == Starport.A)}), and over no lesser port's");
    }

    /// <summary>
    /// Sameness, three ways. Thresholds: within a type, no two of 20 seeds may have the same plan,
    /// and no coarse topology (shape, circulation, junctions, dead ends, room count in threes, storey
    /// by storey) may be shared by more than a quarter of them. Within a building, no two storeys
    /// may share a plan. Between types that should never walk alike (a prison and a hospital, a tower
    /// and a bunker...), their mixes of footprint and circulation overlap by less than half.
    /// Flavour ruins (a house, a shed by the road) are filler, not places: they repeat by design, so
    /// they're held only to a looser rule, no plan shared by more than half their seeds.
    /// </summary>
    void Variety()
    {
        int types = 0, layouts = 0, repeats = 0, storeyRepeats = 0, buildings = 0, fewestPatterns = int.MaxValue;
        float worstFiller = 0f;
        var repeated = new List<string>();
        string repeatedStorey = "";
        string worstFillerType = "";
        float worstPattern = 0f;
        string worstPatternType = "", fewestType = "";
        float worstShare = 0f;
        string worstType = "";
        var grammar = new Dictionary<string, Dictionary<string, int>>();
        foreach (var f in Arch)
        {
            types++;
            var full = new List<string>();
            var coarse = new List<string>();
            var mix = grammar[f.Id] = new Dictionary<string, int>();
            var patterns = new Dictionary<string, int>();
            for (int i = 0; i < 20; i++)
            {
                var l = PoiFactory.BuildLayout(_lib, f, 0, Rarity.Uncommon, Hash.Combine(0x7A21EUL, (ulong)i, Hash.Of(f.Id)));
                layouts++;
                full.Add(Signature(l) + "#" + string.Join("|", l.Extras.Plans.Select(p => p.Signature)));
                coarse.Add(string.Join("|", l.Extras.Plans.OrderBy(p => p.Building).ThenBy(p => p.Storey).Select(p => Topology(p.Signature))));
                foreach (var p in l.Extras.Plans) mix[Grammar(p.Signature)] = mix.GetValueOrDefault(Grammar(p.Signature)) + 1;
                foreach (var p in l.Extras.Plans.Where(p => p.Signature != "hut")) patterns[p.Signature.Split('/')[1]] = patterns.GetValueOrDefault(p.Signature.Split('/')[1]) + 1;
                foreach (var b in l.Extras.Plans.GroupBy(p => p.Building))
                {
                    buildings++;
                    var sigs = b.Where(p => p.Signature != "hut").Select(p => p.Signature).ToList();
                    if (sigs.Count != sigs.Distinct().Count()) repeatedStorey = f.Id;
                    storeyRepeats += sigs.Count - sigs.Distinct().Count();
                }
            }
            // Small improvised places repeat by design, as flavour ruins do.
            if (Marker(f)) continue;
            // (So are the era's own stand-ins for them: a caravan or road camp where a low-tech world has no launch field.)
            static bool Filler(FamilyDef f) => f.Tags.Contains("flavour") || f.Tags.Contains("scrappy") || f.Tags.Contains("evacuation");
            if (Filler(f) || _lib.Families.Any(o => o.Instead.Contains(f.Id) && Filler(o)))
            {
                float same = full.GroupBy(c => c).Max(g => g.Count()) / (float)full.Count;
                if (same > worstFiller) { worstFiller = same; worstFillerType = f.Id; }
                continue;
            }
            if (full.Count != full.Distinct().Count()) repeated.Add(f.Id);
            repeats += full.Count - full.Distinct().Count();
            float share = coarse.GroupBy(c => c).Max(g => g.Count()) / (float)coarse.Count;
            if (share > worstShare && f.Generator == Generator.Building) { worstShare = share; worstType = f.Id; }
            // Circulation shape: no one pattern walks most storeys of a type, and a type with a broad grammar uses it.
            if (f.Generator == Generator.Building && patterns.Count > 0 && _lib.Programs[f.Program!].Plans.Count >= 4)
            {
                float top = patterns.Values.Max() / (float)patterns.Values.Sum();
                if (top > worstPattern) { worstPattern = top; worstPatternType = f.Id; }
                if (patterns.Count < fewestPatterns) { fewestPatterns = patterns.Count; fewestType = f.Id; }
            }
        }
        Check(repeats == 0 && worstShare <= 0.15f,
            $"seeds differ within a type: {layouts} layouts of {types} types, {repeats} identical plans{(repeated.Count > 0 ? " (" + string.Join(", ", repeated) + ")" : "")}; the commonest coarse topology covers {worstShare:P0} of a type's seeds ({worstType}; at most 15%)");
        Check(worstFiller <= 0.5f,
            $"flavour ruins (filler) still vary: the commonest exact plan covers {worstFiller:P0} of a kind's seeds ({worstFillerType}; at most 50%)");
        Check(worstPattern <= 0.45f && fewestPatterns >= 3,
            $"circulation varies within a type: no pattern walks more than {worstPattern:P0} of a type's storeys ({worstPatternType}; at most 45%), and every type with a broad grammar uses at least {fewestPatterns} of its patterns ({fewestType})");
        Check(storeyRepeats == 0, $"every storey is its own: {buildings} buildings, {storeyRepeats} storeys sharing a plan with another storey of the same building{(repeatedStorey.Length > 0 ? " (" + repeatedStorey + ")" : "")}");

        // Types that must never walk alike: footprint and circulation mixes overlap by less than half.
        float Overlap(string a, string b)
        {
            var A = grammar[a];
            var B = grammar[b];
            float ta = A.Values.Sum(), tb = B.Values.Sum();
            return A.Keys.Union(B.Keys).Sum(k => Math.Min(A.GetValueOrDefault(k) / ta, B.GetValueOrDefault(k) / tb));
        }
        // Hospitals and bunkers are drawn on the 2 m grid by plans of their own now (Civic.cs): these are the program-built ones.
        var pairs = new[] { ("prison_block", "biolab"), ("ominous_tower", "research_base"), ("cryo_vault", "research_base"), ("command_bunker", "biolab"),
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

    /// <summary>A marker, not a place (a warning post at the edge of a blast's hot ground): no rooms to ruin, nothing to infest, no landmark to see from afar.</summary>
    static bool Marker(FamilyDef f) => f.Tags.Contains("warning");

        const float RuinShare = 0.15f;

    void StateTransforms()
    {
        bool abandonedQuiet = true, infestedBloom = true, ruinedOk = true;
        float minRemoved = 1f;
        string ruinFirst = "";
        foreach (var f in _lib.Families.Where(f => !Marker(f)))
        {
            int v = 0;
            var ab = PoiFactory.Generate(_lib, f, v, Rarity.Rare, PoiState.Abandoned, 3);
            abandonedQuiet &= !ab.Enemies.Any(e => e.Defender || e.Kind is EnemyKind.Guard or EnemyKind.Turret or EnemyKind.Hover);
            var inf = PoiFactory.Generate(_lib, f, v, Rarity.Common, PoiState.Infested, 3);
            int growths = inf.Blocks.Blocks().Count(b => b.Block.Type == BlockType.Growth);
            infestedBloom &= growths > 0 && inf.Enemies.Any(e => e.Kind is EnemyKind.Hollow or EnemyKind.Corrupted);
            var ru = PoiFactory.Generate(_lib, f, v, Rarity.Common, PoiState.Ruined, 3);
            var ruProblems = Validator.Check(ru);
            // A crashed hulk is a real vessel of the player's blocks, damaged in its own plan (CapitalHull.Damage) and kept
            // worth claiming (design: derelicts that can be repaired and claimed); the place's own blocks are only its yard.
            bool hulk = CapitalHull.Of(ru.Layout) != null;
            bool lost = hulk || ru.RemovedFraction >= RuinShare;
            if ((!lost || ruProblems.Count > 0) && ruinFirst.Length == 0) ruinFirst = $" (first: {f.Id} lost {ru.RemovedFraction:P1}{(ruProblems.Count > 0 ? ", " + ruProblems[0] : "")})";
            ruinedOk &= lost && ruProblems.Count == 0;
            if (!hulk) minRemoved = Math.Min(minRemoved, ru.RemovedFraction);
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
        // Buried buildings that roll a breach, and the places below ground whose years can bring one.
        var below = new HashSet<string>(Below.Select(f => f.Id));
        foreach (var f in Arch.Where(f => f.Program != null && _lib.Programs[f.Program].Breach > 0 || below.Contains(f.Id)))
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

    /// <summary>The owner's rule: no locked doors, no key cards. Every door of a fresh place is an ordinary door, and every room is on foot from the way in.</summary>
    void NoLocks()
    {
        int places = 0, doors = 0, odd = 0, shut = 0;
        var notes = new List<string>();
        ulong fresh = (ulong)Random.Shared.NextInt64();
        foreach (var f in Arch)
            for (int i = 0; i < 6; i++)
            {
                var p = PoiFactory.Generate(_lib, f, i, Rarity.Rare, (PoiState)(i % 4), (ulong)Random.Shared.NextInt64(), Hash.Combine(fresh, (ulong)i, Hash.Of(f.Id)));
                places++;
                var layout = p.Layout;
                var reach = p.Nav().Reachable(layout.EntranceCell);
                foreach (var d in layout.Doorways)
                {
                    if (d.Cells.Count == 0 || d.ModuleB < 0) continue;
                    doors++;
                    Block leaf = p.Blocks.Get(d.Cells[0]);
                    if (leaf.Type == BlockType.Door && leaf.Variant is not (DoorState.Closed or DoorState.Jammed or DoorState.Open or DoorState.Blast)) { odd++; if (notes.Count < 4) notes.Add($"{f.Id}: door variant {leaf.Variant}"); }
                    if (!layout.Modules[d.ModuleB].Cells.Any(reach.Contains) && !layout.Extras.Secrets.Any(s => s.Module == d.ModuleB))
                    {
                        shut++;
                        if (notes.Count < 4) notes.Add($"{f.Id}: room {layout.Modules[d.ModuleB]} is not on foot from the way in");
                    }
                }
            }
        foreach (string n in notes) GD.PrintErr("      " + n);
        Check(odd == 0 && shut == 0, $"no locks: {doors} doors in {places} fresh places, every one an ordinary door ({odd} odd) and every room on foot from the way in ({shut} cut off)");
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
    /// The world profile decides what stands where: old-world places at the pre-collapse tech level,
    /// living places (settlements, a faction's places it holds) at the post-collapse level, nothing
    /// old on a world never inhabited, spaceport ruins only where the old starport was big enough and
    /// sized by it. The same seed rolls the same profile. "New on old": a faction's high-tech place
    /// on a world of modern ruins is built at the high level, and knows it stands on older ruins;
    /// the living keep the animals of their own level.
    /// </summary>
    void WorldProfiles()
    {
        int worlds = 0, places = 0, oldWrong = 0, livingWrong = 0, neverOld = 0, portWrong = 0, variantWrong = 0, ports = 0, same = 0;
        string first = "";
        var theme = _lib.Themes.First(t => t.Id == "temperate");
        for (ulong s = 0; s < 40; s++)
        {
            WorldProfile w = WorldProfile.Roll(Hash.Combine(0x9F0FUL, s), s % 3 == 0 ? "alien" : s % 3 == 1 ? "temperate" : "arid", s % 7 == 0);
            if (WorldProfile.Roll(Hash.Combine(0x9F0FUL, s), s % 3 == 0 ? "alien" : s % 3 == 1 ? "temperate" : "arid", s % 7 == 0) == w) same++;
            var map = PlanetMap.GenerateWorld(Hash.Combine(0x5EEDUL, s), 120, 60, 80f, "profile");
            var r = WorldPlacement.Place(_lib, map, s % 7 == 0 ? theme.AsStart() : theme, Hash.Combine(0x9A1EUL, s), profile: w);
            worlds++;
            foreach (var p in r.Pois)
            {
                places++;
                FamilyDef f = p.Family;
                bool living = WorldProfile.Living(f, p.State);
                string level = WorldProfile.Tag(w.LevelFor(f, p.State));
                if (WorldProfile.Unbuilt(f)) continue;
                if (!living && !w.Inhabited) { neverOld++; if (first.Length == 0) first = $" (first: old {f.Id} on a world never inhabited)"; }
                if (f.Techs.Count > 0 && !f.Techs.Contains(level))
                {
                    if (living) livingWrong++; else oldWrong++;
                    if (first.Length == 0) first = $" (first: {f.Id} {p.State} at {level}, belongs to {string.Join("/", f.Techs)})";
                }
                if (p.Variant.Tech.Length > 0 && p.Variant.Tech != level && f.Variants.Any(v => v.Tech == level)) variantWrong++;
                if (f.Tier == PoiTier.Spaceport && !living)
                {
                    ports++;
                    if (w.Starport == Starport.None) portWrong++;
                    Starport wanted = p.Port ?? w.Starport;
                    int bestGap = f.Variants.Where(v => v.Port != null).Min(v => Math.Abs((int)v.Port!.Value - (int)wanted));
                    if (p.Variant.Port != null && Math.Abs((int)p.Variant.Port.Value - (int)wanted) != bestGap) portWrong++;
                }
            }
        }
        Check(same == worlds, $"the same seed rolls the same world profile ({same} of {worlds})");
        Check(oldWrong == 0 && livingWrong == 0 && neverOld == 0 && variantWrong == 0,
            $"content keeps to the right tech level over {worlds} worlds and {places} places: old-world places at the pre-collapse level ({oldWrong} wrong), living ones at the post-collapse level ({livingWrong} wrong), layouts for their level ({variantWrong} wrong), no old-world place on a world never inhabited ({neverOld}){first}");
        Check(portWrong == 0 && ports > 0, $"spaceport ruins only where there was a starport, laid out for their class: {ports} placed, {portWrong} wrong");

        // New on old: a faction's high-tech place on a world of modern ruins.
        var newOnOld = new WorldProfile { Inhabited = true, Population = 7, Starport = Starport.C, Trade = Trade.Industrial, Before = TechLevel.Mid, Now = TechLevel.High };
        FamilyDef spire = _lib.Family("kessari_spire");
        var inst = PoiFactory.Generate(_lib, spire, 0, Rarity.Uncommon, PoiState.Active, 21, world: newOnOld);
        var ruin = PoiFactory.Generate(_lib, _lib.Family("ominous_tower"), 0, Rarity.Uncommon, PoiState.Abandoned, 21, world: newOnOld);
        bool spireFits = newOnOld.Fits(spire, PoiState.Active) && !newOnOld.Fits(spire, PoiState.Ruined);
        Check(inst.NewOnOld && inst.Tech == TechLevel.High && !ruin.NewOnOld && ruin.Tech == TechLevel.Mid && spireFits,
            $"new on old: a Kessari spire held now is built at the high level on a world of mid-level ruins (new on old {inst.NewOnOld}, {inst.Tech}); a tower of the old world there is mid ({ruin.Tech}); the spire could stand only as a living place there ({spireFits})");

        // The living keep the animals of their own level: working beasts on a low-tech world, machines on a high one.
        FamilyDef hamlet = _lib.Family("survivor_hamlet");
        var low = PoiFactory.Generate(_lib, hamlet, 0, Rarity.Common, PoiState.Active, 33, world: newOnOld with { Before = TechLevel.High, Now = TechLevel.Low });
        var high = PoiFactory.Generate(_lib, hamlet, 0, Rarity.Common, PoiState.Active, 33, world: newOnOld with { Before = TechLevel.Low, Now = TechLevel.High });
        int lowWorking = low.Townsfolk?.Animals.Count(a => a.Kind == "working") ?? -1, highWorking = high.Townsfolk?.Animals.Count(a => a.Kind == "working") ?? -1;
        Check(lowWorking > 0 && highWorking == 0, $"the living keep animals of their own level: a hamlet of low-tech survivors among high-tech ruins keeps working beasts ({lowWorking}), one of high-tech people among low-tech ruins none ({highWorking})");
    }

    /// <summary>
    /// A century or two after the fall: old-world places hold no fresh food outside sealed, frozen
    /// or airless rooms; places worked today are new; trees grow through old ruins and unsheltered
    /// timber has fallen in. Factories and farms pick the layout for the world's tech level.
    /// </summary>
    void Ages()
    {
        int oldCaches = 0, rottenFood = 0, sealedFood = 0, trees = 0, blockTrees = 0, badSpots = 0, strayBloom = 0, ruins = 0, timberStanding = 0;
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
                trees += p.Growth.Count;
                badSpots += p.Growth.Count(s => !Growth.Rootable(p, s.Cell) || p.Protected[s.Cell]);
                // No trees of blocks: the planet plants its own flora on the spots.
                blockTrees += p.Blocks.Blocks().Count(b => b.Block.Type == BlockType.Pillar && b.Block.Material == Gen.Material.Timber && b.Block.Role == Role.Decor && b.Block.Color == new Rgb(0x4a, 0x3a, 0x2a) && p.Blocks.Get(b.Cell + Int3.Up * 3).Type == BlockType.Pillar);
                if (p.State != PoiState.Infested) strayBloom += p.Blocks.Blocks().Count(b => b.Block.Material == Gen.Material.Bloom);
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
        Check(rottenFood == 0 && farm.Age == Age.New && trees > 0 && blockTrees == 0 && badSpots == 0 && strayBloom == 0 && techMismatch == 0 && techPlaced > 0,
            $"long after the fall: {ruins} old-world places, no food in {oldCaches} of their unsealed caches ({sealedFood} sealed stores kept some), " +
            $"{trees} spots left for trees ({badSpots} bad), {blockTrees} block trees, {strayBloom} Bloom blocks outside infested places, {timberStanding} unsheltered timber roof blocks still up; a worked freeholder farm is new ({farm.Age}); " +
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

    /// <summary>
    /// No stray geometry: every vertex of every place's mesh lies inside its block grid (a metre of
    /// slack), and no triangle edge is longer than a chunk's diagonal. This is what catches a
    /// "wire" across the landscape: a stroke or strand thrown out of its cell.
    /// </summary>
    void Geometry()
    {
        int places = 0, stray = 0, triangles = 0;
        string first = "";
        foreach (var f in _lib.Families)
            foreach (PoiState s in Enum.GetValues<PoiState>())
            {
                var p = PoiFactory.Generate(_lib, f, 0, Rarity.Uncommon, s, 13);
                places++;
                var g = p.Blocks;
                var chunks = Mesher.Build(g, p.Layout.Occupied, new MeshOptions { RockMin = new Int3(1, 0, 1), RockMax = new Int3(0, 0, 0), RockBottom = g.Min.Y });
                float lo = -1f, maxEdge = 16f * 1.5f;
                foreach (var c in chunks)
                    foreach (var buf in c.Surfaces)
                        for (int t = 0; t + 2 < buf.Indices.Count; t += 3)
                        {
                            triangles++;
                            bool bad = false;
                            for (int e = 0; e < 3 && !bad; e++)
                            {
                                int a = buf.Indices[t + e] * 3, b = buf.Indices[t + (e + 1) % 3] * 3;
                                float x = buf.Positions[a], y = buf.Positions[a + 1], z = buf.Positions[a + 2];
                                bad |= float.IsNaN(x) || x < g.Min.X + lo || y < g.Min.Y + lo - 64 || z < g.Min.Z + lo
                                       || x > g.Min.X + g.Size.X + 1 || y > g.Min.Y + g.Size.Y + 1 || z > g.Min.Z + g.Size.Z + 1;
                                float dx = x - buf.Positions[b], dy = y - buf.Positions[b + 1], dz = z - buf.Positions[b + 2];
                                bad |= dx * dx + dy * dy + dz * dz > maxEdge * maxEdge;
                            }
                            if (bad) { stray++; if (first.Length == 0) first = $" (first: {f.Id} {s} near {buf.Positions[buf.Indices[t] * 3]:F1}, {buf.Positions[buf.Indices[t] * 3 + 1]:F1}, {buf.Positions[buf.Indices[t] * 3 + 2]:F1})"; }
                        }
            }
        Check(stray == 0, $"no stray geometry: {triangles} triangles in {places} places, every vertex inside its place and no edge longer than {16 * 1.5f:F0} m ({stray} stray){first}");
    }

    /// <summary>
    /// Grounding stamps beyond the flat pad: a crater bowl sinks below the pad and meets it at the
    /// rim, terraces step down a slope in level treads with walkable risers and only ever fill, an
    /// approach cut keeps an easy grade, a tilt rises away from the way in, a furrow ploughs out
    /// behind; each the same across a seam; and in a list the later stamp wins where it applies fully.
    /// </summary>
    void Stamps()
    {
        const double W = 4096, H = 2048;
        float Flat(double x, double z) => 10f;
        float Slope(double x, double z) => 10f - (float)x * 0.3f;
        var bowl = new GalHack.Terrain.TerrainStamp("bowl", 80, 80, 120, 120, 10, 0f, 16f, W, H) { Shape = GalHack.Terrain.StampShape.Bowl, Radius = 20f, Depth = 6f };
        float centre = bowl.Apply(10f, 100, 100), rim = bowl.Apply(10f, 120, 100), far = bowl.Apply(14f, 200, 100);
        var terraces = new GalHack.Terrain.TerrainStamp("terraces", 0, -10, 20, 10, 10, 0f, 0f, W, H) { Shape = GalHack.Terrain.StampShape.Terraces, DirX = 1, DirZ = 0, Length = 36f, Width = 24f, Step = 1.5f };
        float worst = 0f, prev = terraces.Apply(Slope(20, 0), 20, 0);
        bool fillsOnly = true;
        for (double x = 21; x < 70; x += 1)
        {
            float h = terraces.Apply(Slope(x, 0), x, 0);
            worst = MathF.Max(worst, MathF.Abs(h - prev));
            fillsOnly &= h >= Slope(x, 0) - 1e-3f;
            prev = h;
        }
        var cut = new GalHack.Terrain.TerrainStamp("cut", 0, -2, 4, 2, 10, 0f, 0f, W, H) { Shape = GalHack.Terrain.StampShape.Cut, DirX = 1, Length = 30f, Width = 7f, Grade = 0.4f };
        float cutWorst = 0f; prev = cut.Apply(20f, 4, 0);
        for (double x = 5; x < 34; x++) { float h = cut.Apply(20f, x, 0); cutWorst = MathF.Max(cutWorst, MathF.Abs(h - prev)); prev = h; }
        var tilt = new GalHack.Terrain.TerrainStamp("tilt", 0, 0, 40, 20, 10, 4f, 20f, W, H) { Shape = GalHack.Terrain.StampShape.Tilt, DirX = 0, DirZ = 1, Grade = 0.2f };
        float front = tilt.Apply(Flat(20, 1), 20, 1), back = tilt.Apply(Flat(20, 19), 20, 19);
        var furrow = new GalHack.Terrain.TerrainStamp("furrow", 0, 0, 20, 20, 10, 0f, 0f, W, H) { Shape = GalHack.Terrain.StampShape.Furrow, DirX = -1, Length = 60f, Width = 12f, Depth = 3f };
        float gouge = furrow.Apply(10f, -8, 10);
        // Across a seam: a bowl straddling x = 0 gives the same ground at x and x + the planet's width.
        var seam = new GalHack.Terrain.TerrainStamp("seam", -20, 100, 20, 140, 10, 0f, 16f, W, H) { Shape = GalHack.Terrain.StampShape.Bowl, Radius = 18f, Depth = 6f };
        float a = seam.Apply(10f, 5, 120), b = seam.Apply(10f, 5 + W, 120);
        // Composed: a pad and then a cut over it: the cut wins in its strip.
        var pad = new GalHack.Terrain.TerrainStamp("pad", 0, -10, 20, 10, 10, 4f, 12f, W, H);
        float composed = GalHack.Terrain.TerrainStamp.Compose(new[] { pad, cut }, 20f, 20, 0), cutAlone = cut.Apply(pad.Apply(20f, 20, 0), 20, 0);
        Check(Math.Abs(centre - 4f) < 0.01f && Math.Abs(rim - 10f) < 0.01f && Math.Abs(far - 14f) < 0.01f && worst <= 1.05f && fillsOnly && cutWorst <= 0.45f
              && back > front + 2f && gouge < 10f - 2f && Math.Abs(a - b) < 1e-4f && Math.Abs(composed - cutAlone) < 1e-4f,
            $"grounding stamps: a crater bowl {10 - centre:F1} m deep meeting the pad at its rim; terraces only fill, steepest step {worst:F2} m a metre; an approach cut at {cutWorst:F2} a metre; " +
            $"a tilt rising {back - front:F1} m across a ruin; a furrow {10 - gouge:F1} m deep; the same across a seam; and composed, the later stamp wins");
    }

    /// <summary>Cables, wires and tendrils always hang from something: never in mid-air.</summary>
    void Tethers()
    {
        int places = 0, hanging = 0, loose = 0, longRuns = 0, buried = 0, sideways = 0;
        foreach (var f in _lib.Families)
            foreach (PoiState s in Enum.GetValues<PoiState>())
            {
                var p = PoiFactory.Generate(_lib, f, 0, Rarity.Uncommon, s, 31);
                places++;
                hanging += p.Blocks.Blocks().Count(b => Structure.Hangs(b.Block.Type));
                loose += Structure.Untethered(p.Blocks).Count;
            }
        // No spans: a hanging run is one straight drop under its anchor, never longer than a tall hall,
        // never in the ground, and never a strand strung sideways from one cell to the next.
        foreach (var f in _lib.Families)
        {
            var p = PoiFactory.Generate(_lib, f, 0, Rarity.Uncommon, PoiState.Infested, 31);
            var g = p.Blocks;
            foreach (var (c, b) in g.Blocks())
            {
                if (!Structure.Hangs(b.Type)) continue;
                if (c.Y < 0 && !p.Layout.Occupied[c]) buried++;
                if (Structure.Hangs(g.Get(c + Int3.Up).Type)) continue;   // count each run once, from its top
                int run = 0;
                while (Structure.Hangs(g.Get(c + Int3.Down * run).Type)) run++;
                if (run > 16) longRuns++;
                bool side = Dirs.Horizontal.Any(d => Structure.Hangs(g.Get(c + d.Vec()).Type) && !BlockInfo.Collides(g.Get(c + d.Vec() + Int3.Up).Type) && !Structure.Hangs(g.Get(c + d.Vec() + Int3.Up).Type));
                if (side && BlockInfo.Collides(g.Get(c + Int3.Up).Type) == false) sideways++;
            }
        }
        Check(hanging > 0 && loose == 0 && longRuns == 0 && buried == 0 && sideways == 0,
            $"cables and tendrils: {hanging} hanging things in {places} places, every one hangs straight down from an anchor above it; {loose} in mid-air, {longRuns} runs over 16 m, {buried} in the ground, {sideways} strung sideways");
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

    /// <summary>
    /// The roadside ruins, on fresh seeds, turned every way, at every tech level: a house is house-sized
    /// against a person (10 to 16 m wide, 4 m a storey, the standard 2 m door), has a sloped roof and a door and
    /// never digs into the ground; decay leaves nothing hanging, no block trees, weeds only on
    /// something solid; and every face that isn't against a block is drawn, buried or not, tops of broken
    /// walls included, on these and on the places proper.
    /// </summary>
    void PlainRuins()
    {
        int spawns = 0, floating = 0, hovering = 0, trees = 0, missing = 0, faces = 0, wrongSize = 0, flatRoofs = 0, noDoor = 0, belowGround = 0, stands = 0;
        string first = "", faceFirst = "";
        void Note(string s) { if (first.Length == 0) first = " (first: " + s + ")"; }
        var rand = new Random();
        foreach (var f in _lib.Families.Where(f => f.Tags.Contains("flavour") && f.Id.StartsWith("flavour_")))
            foreach (var v in f.Variants)
                foreach (TechLevel tech in Enum.GetValues<TechLevel>())
                    foreach (Dir front in Dirs.Horizontal)
                    {
                        var layout = PoiFactory.BuildLayout(_lib, f, v.Index, Rarity.Common, (ulong)rand.NextInt64(), new LayoutHints(front, tech));
                        if (layout.Blocks.Blocks().Any(b => b.Cell.Y < 0)) { belowGround++; Note($"{v.Name} goes below ground"); }
                        // A house's buildings, its garage among them, at house size: a terrace row runs long.
                        foreach (PlainBox b in layout.Extras.Plain.Where(_ => f.Id == "flavour_house"))
                        {
                            int across = Math.Min(b.Max.X - b.Min.X, b.Max.Z - b.Min.Z) - 3, along = Math.Max(b.Max.X - b.Min.X, b.Max.Z - b.Min.Z) - 3;
                            if (across < 6 || along > 64 || (b.Eave + 1) % 4 != 0) { wrongSize++; Note($"{v.Name}: {across} by {along} m, eaves {b.Eave}"); }
                        }
                        foreach (Doorway d in layout.Doorways)
                        {
                            Int3 lo = d.Cells.Aggregate(Int3.Min), hi = d.Cells.Aggregate(Int3.Max);
                            // Every building's doors are the one standard door (a large block against a 1.8 m person): a house's, a shop's, an inn's, a shed's.
                            if (hi.Y - lo.Y + 1 != DoorSize.Tall) { wrongSize++; Note($"{v.Name}: a door {hi.Y - lo.Y + 1} m tall"); }
                        }
                        if (layout.Doorways.Count == 0 && f.Id is not ("flavour_shed" or "flavour_farmstead" or "flavour_warning")) { noDoor++; Note($"{v.Name} has no door"); }
                        // A house's roof is the form its family gave it: a flat one is flat, any other is slopes.
                        bool flatByRule = f.Id == "flavour_house" && layout.Extras.Homes.All(x => x.Roof is "flat" or "turf");
                        if (f.Id is "flavour_house" or "flavour_farmstead" or "flavour_shed" && v.Tech != "high" && !flatByRule && !layout.Blocks.Blocks().Any(b => b.Block.Type == BlockType.Ramp && b.Block.Role == Role.Roof)) { flatRoofs++; Note($"{v.Name} has no sloped roof"); }
                        foreach (PoiState state in new[] { PoiState.Ruined, PoiState.Abandoned })
                        {
                            PoiInstance p = PoiFactory.Spawn(_lib, layout, state, (ulong)rand.NextInt64());
                            if (state == PoiState.Ruined && rand.Next(4) == 0) Gen.StateTransforms.Blast(p, 1f, 0f, 0.3f + (float)rand.NextDouble() * 0.6f, new Rng((ulong)rand.NextInt64()));
                            spawns++;
                            var g = p.Blocks;
                            var nav = p.Nav();
                            // Nothing joined to the ground only through weeds and litter: flood the solid blocks from the ground.
                            var held = new HashSet<Int3>();
                            var queue = new Queue<Int3>();
                            foreach (var (c, b) in g.Blocks())
                                if (BlockInfo.Collides(b.Type) && nav.IsRock(c + Int3.Down) && held.Add(c)) queue.Enqueue(c);
                            while (queue.Count > 0)
                            {
                                Int3 c = queue.Dequeue();
                                foreach (Dir d in Dirs.All)
                                {
                                    Int3 q = c + d.Vec();
                                    if (BlockInfo.Collides(g.Get(q).Type) && held.Add(q)) queue.Enqueue(q);
                                }
                            }
                            foreach (var (c, b) in g.Blocks())
                            {
                                bool solid = BlockInfo.Collides(b.Type);
                                if (solid && !held.Contains(c)) { floating++; Note($"{v.Name} {state}: {b} floats at {c}"); }
                                if (!solid && b.Type is BlockType.Plant or BlockType.Ash or BlockType.Scatter or BlockType.Plate && !nav.IsRock(c + Int3.Down) && !BlockInfo.Collides(g.Get(c + Int3.Down).Type)) { hovering++; Note($"{v.Name} {state}: {b} hangs at {c}"); }
                                if (b.Type == BlockType.Rubble && !nav.IsRock(c + Int3.Down) && !BlockInfo.Supports(g.Get(c + Int3.Down).Type)) { hovering++; Note($"{v.Name} {state}: rubble hangs at {c}"); }
                                if (b.Type == BlockType.Pillar && b.Material == Gen.Material.Timber && b.Role == Role.Decor && b.Color == new Rgb(0x4a, 0x3a, 0x2a) && !nav.IsRock(c + Int3.Down) == false && g.Get(c + Int3.Up * 3).Type == BlockType.Pillar) { trees++; Note($"{v.Name}: a block tree"); }
                            }
                            stands += Faces(g, layout.Occupied, ref faces, ref missing, ref faceFirst);
                        }
                    }
        foreach (var f in Arch.Where(f => !f.Tags.Contains("flavour")).OrderBy(_ => rand.Next()).Take(12))
            foreach (PoiState state in new[] { PoiState.Ruined, PoiState.Abandoned })
            {
                PoiInstance p = PoiFactory.Generate(_lib, f, 0, Rarity.Common, state, (ulong)rand.NextInt64());
                stands += Faces(p.Blocks, p.Layout.Occupied, ref faces, ref missing, ref faceFirst);
            }
        // The places proper: what lies loose rests on what is under it, whatever decay took away.
        int loose = 0, unheld = 0;
        foreach (var f in Arch.Where(f => !f.Tags.Contains("flavour")).OrderBy(_ => rand.Next()).Take(16))
            foreach (PoiState state in new[] { PoiState.Ruined, PoiState.Abandoned, PoiState.Infested })
            {
                PoiInstance p = PoiFactory.Generate(_lib, f, 0, Rarity.Common, state, (ulong)rand.NextInt64());
                var nav = p.Nav();
                foreach (var (c, b) in p.Blocks.Blocks())
                {
                    if (p.Layout.Extras.InSpace || b.Role is Role.Loot or Role.Core or Role.Terminal or Role.Growth or Role.Decor) continue;
                    if (b.Type is not (BlockType.Crate or BlockType.Tank or BlockType.Rubble or BlockType.Plant or BlockType.Ash or BlockType.Scatter or BlockType.Barricade or BlockType.Bench)) continue;
                    loose++;
                    if (!nav.Solid(c + Int3.Down)) { unheld++; Note($"{f.Id} {state}: {b} hangs at {c}"); }
                }
            }
        Check(unheld == 0 && loose > 500, $"drums, crates, rubble and weeds rest on something in the places proper: {loose} loose blocks, {unheld} hanging{first}");
        Check(floating == 0 && hovering == 0 && trees == 0 && spawns > 200, $"nothing floats in a plain ruin: {spawns} spawns, flattened or not; {floating} blocks off the ground, {hovering} weeds or rubble on nothing, {trees} block trees{first}");
        Check(missing == 0 && faces > 10000, $"every face is drawn: {faces} open faces of {stands} structures, {missing} missing, buried ones too{faceFirst}");
        Check(wrongSize == 0 && flatRoofs == 0 && noDoor == 0 && belowGround == 0, $"plain ruins are real size and built as a player builds: {wrongSize} wrong sizes, {flatRoofs} without a sloped roof, {noDoor} without a door, {belowGround} dug into the ground{first}");
        // Road paint stops short of whatever stands on the road.
        int paint = 0, onto = 0;
        var hazard = new Rgb(0xe0, 0xa8, 0x00);
        foreach (var f in Arch.Where(f => f.Site != null && _lib.Sites[f.Site].Parts.Any(x => x.Kind == "road") && !_lib.Sites[f.Site].Parts.Any(x => x.Kind is "pad" or "runway" or "rocket" or "tanks")))
        {
            var g = PoiFactory.BuildLayout(_lib, f, 0, Rarity.Common, (ulong)rand.NextInt64()).Blocks;
            foreach (var (c, b) in g.Blocks())
            {
                if (b.Color != hazard || c.Y != -1 || b.Material != Gen.Material.Concrete) continue;
                paint++;
                for (int k = -3; k <= 3; k++)
                    if (k != 0 && (BlockInfo.Collides(g.Get(c + Int3.Up + new Int3(k, 0, 0)).Type) && g.Get(c + new Int3(k > 0 ? 1 : -1, 0, 0)).Color == hazard
                        || BlockInfo.Collides(g.Get(c + Int3.Up + new Int3(0, 0, k)).Type) && g.Get(c + new Int3(0, 0, k > 0 ? 1 : -1)).Color == hazard)) { onto++; break; }
            }
        }
        var white = new Rgb(0xe8, 0xe4, 0xd8);
        foreach (var f in Arch.Where(f => f.Site != null && _lib.Sites[f.Site].Parts.Any(x => x.Kind == "runway")))
        {
            var g = PoiFactory.BuildLayout(_lib, f, 0, Rarity.Common, (ulong)rand.NextInt64()).Blocks;
            foreach (var (c, b) in g.Blocks())
            {
                if (b.Color != white || c.Y != -1 || b.Material != Gen.Material.Concrete) continue;
                paint++;
                if (BlockInfo.Collides(g.Get(c + Int3.Up).Type)) onto++;
            }
        }
        Check(onto == 0, $"road and runway markings stop before a structure: {paint} painted cells, {onto} running on to a wall");
    }

    /// <summary>Holds a structure's open faces against what the planet's mesher draws: each cube face not against a full block must be there, buried or not (the ground can be dug off it).</summary>
    static int Faces(BlockGrid g, CellMask occ, ref int faces, ref int missing, ref string first)
    {
        var drawn = new HashSet<(int, int, int)>();
        MeshOptions options = MeshOptions.OnPlanet(g.Min);
        options.Collision = false;
        foreach (ChunkMesh chunk in Mesher.Build(g, occ, options))
            foreach (MeshBuffer s in chunk.Surfaces)
                for (int i = 0; i + 2 < s.Indices.Count; i += 3)
                {
                    // Half a unit quad spans the face: the middle of its box is the middle of the face (doubled, to stay whole).
                    int a = s.Indices[i] * 3, b = s.Indices[i + 1] * 3, c = s.Indices[i + 2] * 3;
                    int Mid(int k) => (int)MathF.Round(MathF.Min(s.Positions[a + k], MathF.Min(s.Positions[b + k], s.Positions[c + k])) + MathF.Max(s.Positions[a + k], MathF.Max(s.Positions[b + k], s.Positions[c + k])));
                    drawn.Add((Mid(0), Mid(1), Mid(2)));
                }
        foreach (var (p, b) in g.Blocks())
        {
            if (!BlockInfo.IsFullCube(b.Type) || BlockInfo.IsTransparent(b.Material) || b.Material == Gen.Material.Glyph) continue;
            foreach (Dir d in Dirs.All)
            {
                Int3 n = p + d.Vec();
                var nb = g.Get(n);
                if (BlockInfo.IsFullCube(nb.Type) && !BlockInfo.IsTransparent(nb.Material)) continue;
                faces++;
                Int3 v = d.Vec();
                if (!drawn.Contains((p.X * 2 + 1 + v.X, p.Y * 2 + 1 + v.Y, p.Z * 2 + 1 + v.Z)))
                {
                    missing++;
                    if (first.Length == 0) first = $" (first: {b} at {p}, its {d} face)";
                }
            }
        }
        return 1;
    }

    /// <summary>From afar: each family's landmark stands as high as its purpose wants, and something lights it.</summary>
    void Landmarks()
    {
        var bad = new List<string>();
        foreach (var f in _lib.Families.Where(f => !Marker(f)))
            foreach (var s in new[] { PoiState.Abandoned, PoiState.Ruined })
            {
                var p = PoiFactory.Generate(_lib, f, 0, Rarity.Uncommon, s, 21);
                int top = p.Blocks.Blocks().Where(b => b.Cell.Y >= 0 && BlockInfo.Collides(b.Block.Type)).Select(b => b.Cell.Y + 1).DefaultIfEmpty(0).Max();
                // A crashed hulk stands as the vessel it is (its 2 m blocks set down in the place's frame), not in the place's own blocks.
                if (CapitalHull.Of(p.Layout) is { } hull)
                    foreach (var piece in hull.Pieces)
                        foreach (Int3 c in piece.Intact.Keys)
                            top = Math.Max(top, (int)MathF.Floor(piece.Origin.Y + System.Numerics.Vector3.Transform(new System.Numerics.Vector3(c.X, c.Y, c.Z) * 2f, piece.Rotation).Y + 1f));
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
    // along the corridors, down every stair,
    // riding lifts and climbing ladders. Proves the meshes, collision, doors, lifts and grid agree.
    readonly (string Family, int Variant, Rarity Rarity, PoiState State, bool Lifts, string Goal)[] _walks =
    {
        ("airfield", 0, Rarity.Rare, PoiState.Abandoned, true, "deep"),
        ("research_base", 0, Rarity.Uncommon, PoiState.Abandoned, true, "deep"),
        ("ominous_tower", 0, Rarity.Uncommon, PoiState.Ruined, false, "top+deep"),
        ("deep_bunker", 0, Rarity.Rare, PoiState.Infested, false, "deep"),
        ("old_mine", 0, Rarity.Common, PoiState.Abandoned, true, "deep"),
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
        // Held up a moment: duck, as a player ducks under a door jammed part open.
        _walker.AutoCrouch = _stuck > 45;
        if (++_stuck > 420)
        {
            Fail($"walking {WalkName(w)}: stuck at {at} on cell {_step}/{_route.Count} ({target}){(_back ? " on the way back" : "")}");
            // What the route crosses there: each cell's block, the block under it and over it, and whose room it is.
            var poi = _view.Poi;
            for (int k = Math.Max(0, _step - 3); k < Math.Min(_route.Count, _step + 4); k++)
            {
                var c = _route[k];
                GD.PrintErr($"      {(k == _step ? ">" : " ")} {c}: {poi.Blocks.Get(c).Type} over {poi.Blocks.Get(c + Gen.Int3.Down).Type}, under {poi.Blocks.Get(c + Gen.Int3.Up).Type}/{poi.Blocks.Get(c + Gen.Int3.Up * 2).Type}, {poi.Layout.Modules.FirstOrDefault(m => m.Cells.Contains(c))?.Purpose ?? "-"}");
            }
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
