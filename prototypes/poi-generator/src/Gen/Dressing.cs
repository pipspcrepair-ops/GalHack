#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// What changes every spawn: loot, who is waiting, and the props that tell a story. There are no
/// written logs: what happened is shown by the place itself.
/// </summary>
public static class Dressing
{
    public static void Apply(PoiLibrary lib, PoiInstance inst, Rng rng)
    {
        Story(inst, rng.Fork("story"));
        Loot(lib, inst, rng.Fork("loot"));
        Enemies(lib, inst, rng.Fork("enemies"));
        Townsfolk(lib, inst, rng.Fork("townsfolk"));
        foreach (var t in inst.Terminals) t.Text = TerminalText(inst, t);
    }

    static string TerminalText(PoiInstance inst, TerminalLog t)
    {
        bool power = inst.State == PoiState.Active;
        return power ? "DOOR CONTROL // ONLINE" : "NO POWER";
    }

    // ---- townsfolk --------------------------------------------------------------------

    /// <summary>A settlement's temper, from its spawn seed: mostly peaceful, some wary, a few hostile.</summary>
    public static Mood MoodFor(ulong spawnSeed)
    {
        uint h = (uint)(Hash.Combine(spawnSeed, 0x300DUL) % 10);
        return h < 5 ? Mood.Peaceful : h < 8 ? Mood.Wary : Mood.Hostile;
    }

    /// <summary>What townsfolk do in a room of this purpose.</summary>
    static string? SpotTag(string purpose) => purpose switch
    {
        "market-hall" or "shop" or "market" or "stall" => "stall",
        "office" or "workshop" or "control" or "galley" or "storage" or "warehouse" or "parts-store" or "loading-dock" or "plant" or "generator" => "work",
        "mess" or "canteen" or "lobby" or "farm-kitchen" => "sit",
        "quarters" or "apartment" or "shack" or "washroom" => "home",
        "greenhouse" or "hydroponic shed" or "grow-room" or "hydroponics" or "garden" => "garden",
        "yard" or "corridor" or "square" or "plaza" => "chat",
        _ => null,
    };

    /// <summary>
    /// People in a lived-in place (a settlement, or a faction's civilian place while it's in use):
    /// activity spots by what each room is for, walkable links to the nearest few, long routes for
    /// rushers, and a role and gait for each person. Settlements roll a mood; faction civilian
    /// places are friendly until someone starts shooting.
    /// </summary>
    static void Townsfolk(PoiLibrary lib, PoiInstance inst, Rng rng)
    {
        var f = inst.Family;
        bool settlement = f.Tags.Contains("settlement"), civilian = f.Tags.Contains("civilian");
        if ((!settlement && !civilian) || inst.State != PoiState.Active) return;
        inst.Mood = settlement ? MoodFor(inst.SpawnSeed) : Mood.Peaceful;
        // A settlement's people aren't a gang on duty: its guards are townsfolk (and enemies only if it's hostile).
        if (settlement) inst.Enemies.RemoveAll(e => e.Defender);
        inst.Friendly = inst.Mood != Mood.Hostile;
        var layout = inst.Layout;
        var nav = inst.Nav();
        var reach = nav.Reachable(layout.EntranceCell);
        var town = new Townsfolk();
        foreach (var m in layout.Modules)
        {
            string? tag = SpotTag(m.Purpose) ?? (m.Kind == ModuleKind.Corridor ? "chat" : m.Kind == ModuleKind.Outdoor && m.Index == 0 ? "chat" : null);
            if (tag == null) continue;
            var cells = m.Cells.Where(c => c.Y == m.Min.Y && reach.Contains(c) && g(c).IsAir).OrderBy(c => Hash.Cell(c, 151)).ToList();
            int n = Math.Min(cells.Count / 12 + 1, tag == "chat" ? 4 : 3);
            foreach (Int3 c in cells.Take(n)) town.Spots.Add(new ActivitySpot(tag, c, m.Index));
        }
        foreach (var mk in layout.Extras.Markers.Where(mk => mk.Kind is "stall" or "garden" or "home"))
            if (reach.Contains(mk.Cell)) town.Spots.Add(new ActivitySpot(mk.Kind, mk.Cell, -1));
        if (town.Spots.Count < 2) return;
        // Links to the nearest few; routes between the furthest pairs.
        float Dist(Int3 a, Int3 b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z)) + MathF.Abs(a.Y - b.Y) * 2f;
        for (int i = 0; i < town.Spots.Count; i++)
            foreach (int j in Enumerable.Range(0, town.Spots.Count).Where(j => j != i).OrderBy(j => Dist(town.Spots[i].Cell, town.Spots[j].Cell)).Take(3))
                if (!town.Links.Contains((Math.Min(i, j), Math.Max(i, j)))) town.Links.Add((Math.Min(i, j), Math.Max(i, j)));
        var pairs = from a in Enumerable.Range(0, town.Spots.Count) from b in Enumerable.Range(0, town.Spots.Count) where a < b select (a, b);
        town.Routes.AddRange(pairs.OrderByDescending(p => Dist(town.Spots[p.a].Cell, town.Spots[p.b].Cell)).Take(4));
        // People: a vendor per stall, workers at work and garden spots, guards at the way in, then rushers and wanderers.
        var raiders = lib.Roster("raiders").Where(e => e.Kind == EnemyKind.Guard).ToList();
        for (int i = 0; i < town.Spots.Count; i++)
        {
            var s = town.Spots[i];
            if (s.Tag == "stall") town.People.Add(new CitizenSpawn("vendor", "still", s.Cell, i));
            else if (s.Tag is "work" or "garden" && rng.Chance(0.6f)) town.People.Add(new CitizenSpawn("worker", "steady", s.Cell, i));
        }
        int guards = inst.Mood == Mood.Peaceful ? 2 : 4;
        var edge = town.Spots.Select((s, i) => (s, i)).OrderBy(t => Dist(t.s.Cell, layout.EntranceCell)).Take(guards).ToList();
        foreach (var (s, i) in edge)
        {
            town.People.Add(new CitizenSpawn("guard", "patrol", s.Cell, i));
            if (inst.Mood == Mood.Hostile && raiders.Count > 0)
                inst.Enemies.Add(new EnemySpawn(s.Cell, "Settlement guard", EnemyKind.Guard, raiders[rng.Range(0, raiders.Count)], s.Module, true));
        }
        int crowd = Math.Max(3, town.Spots.Count / 2);
        for (int k = 0; k < crowd; k++)
        {
            int i = rng.Range(0, town.Spots.Count);
            town.People.Add(rng.Chance(0.3f) ? new CitizenSpawn("rusher", "hurry", town.Spots[i].Cell, i) : new CitizenSpawn("wanderer", "amble", town.Spots[i].Cell, i));
        }
        // Animals by tech level: working beasts only where tech is low; stock where tech is low or
        // on any farm; elsewhere machines do that work. Pets anywhere. On a world, the living keep
        // the animals of its post-collapse level, whatever the ruins round them were.
        string tech = inst.World is { } world && WorldProfile.Living(f, inst.State) ? WorldProfile.Tag(world.Now)
            : layout.Variant?.Tech is { Length: > 0 } t ? t : settlement ? "low" : "mid";
        bool farm = f.Tags.Contains("farm");
        if (tech == "low" || farm)
        {
            foreach (var s in town.Spots.Where(s => s.Tag == "garden").Take(3)) town.Animals.Add(("stock", s.Cell));
            foreach (var mk in layout.Extras.Markers.Where(mk => mk.Kind == "pen").Take(2)) town.Animals.Add(("stock", mk.Cell));
        }
        if (tech == "low" && edge.Count > 0) town.Animals.Add(("working", edge[0].s.Cell));
        foreach (var s in town.Spots.Where(s => s.Tag == "home").Take(2)) town.Animals.Add(("pet", s.Cell));
        // The area wildlife keeps out of: a circle round every spot, with a margin.
        town.AreaCenter = new Int3((int)town.Spots.Average(s => s.Cell.X), 0, (int)town.Spots.Average(s => s.Cell.Z));
        town.AreaRadius = town.Spots.Max(s => Dist(s.Cell with { Y = 0 }, town.AreaCenter)) + 8f;
        // A wary or hostile settlement shows it at the way in: barricades and a warning sign.
        if (inst.Mood != Mood.Peaceful)
        {
            Int3 e = layout.EntranceCell;
            foreach (Dir d in Dirs.Horizontal)
            {
                Int3 c = e + d.Vec() * 2;
                if (!inst.Blocks.Get(c).IsAir || inst.Protected[c] || inst.Blocks.Get(c + Int3.Down).IsAir) continue;
                inst.Blocks.Set(c, new Block(BlockType.Barricade, Material.Scrap, new Rgb(0x5a, 0x4a, 0x3a), Role.Prop));
            }
            inst.Story.Add(new StoryProp(inst.Mood == Mood.Hostile ? "keep out" : "warning sign", e, inst.Mood == Mood.Hostile ? "TURN BACK. WE SHOOT." : "State your business at the gate."));
        }
        inst.Townsfolk = town;
        Block g(Int3 c) => inst.Blocks.Get(c);
    }

    // ---- loot -------------------------------------------------------------------------

    /// <summary>
    /// A cache's tier: the place's rarity, how deep and dangerous its storey is, and what kind of
    /// container it is (side cache, vault, hidden stash). Higher tiers roll rarer items.
    /// </summary>
    public static int CacheTier(PoiInstance inst, LootCache cache)
    {
        int tier = (int)inst.Rarity;
        var lvl = Decay.LevelAt(inst.Layout, cache.Cell.Y);
        tier += (int)MathF.Round(lvl.Wrongness * 2f);
        if (lvl.Depth >= 2) tier++;
        if (cache.InVault) tier += 1;
        if (cache.Secret) tier += 1 + (lvl.Depth >= 2 ? 1 : 0);
        if (inst.State == PoiState.Infested) tier += 1;   // danger pays
        return Math.Clamp(tier, 0, 6);
    }

    static void Loot(PoiLibrary lib, PoiInstance inst, Rng rng)
    {
        var tags = new HashSet<string>(inst.Family.Tags);
        foreach (var cache in inst.Caches)
        {
            cache.Tier = CacheTier(inst, cache);
            var roomTags = new HashSet<string>(tags);
            var m = inst.Layout.Modules[cache.Module];
            if (lib.Rooms.TryGetValue(m.Purpose, out var def)) roomTags.UnionWith(def.Tags);
            int rolls = cache.InVault ? 3 + Math.Min(3, (int)inst.Rarity) : cache.Secret ? 3 : 2;
            if (!cache.InVault && !cache.Secret)
            {
                // Somebody got to the side rooms first; vaults and hidden stashes stay sealed.
                if (inst.State == PoiState.Abandoned && rng.Chance(0.35f)) { cache.PickedOver = true; rolls = 1; }
                if (inst.State == PoiState.Ruined && rng.Chance(0.3f)) rolls--;
            }
            // Long after the fall, nothing edible survives outside a sealed, frozen or airless room.
            Age age = inst.AgeOf(cache.Module);
            for (int i = 0; i < rolls; i++) cache.Items.Add(Roll(lib, cache.Tier, roomTags, rng, uniqueOk: cache.Tier >= 4, age));
            // Rare and deeper vaults have a real chance at a unique find on top; hidden stashes sometimes too.
            if (cache.InVault && inst.Rarity >= Rarity.Rare && rng.Chance(0.5f)) cache.Items.Add(RollRarity(lib, Rarity.Unique, roomTags, rng, age));
            else if (cache.Secret && cache.Tier >= 4 && rng.Chance(0.25f)) cache.Items.Add(RollRarity(lib, Rarity.Unique, roomTags, rng, age));
        }
    }

    static readonly float[] Spread = { 0.12f, 0.26f, 0.38f, 0.19f, 0.05f }; // -2 .. +2 around the tier's centre

    static LootItem Roll(PoiLibrary lib, int tier, HashSet<string> tags, Rng rng, bool uniqueOk, Age age = Age.New)
    {
        // Tier 0 centres on common, tier 2 on uncommon, tier 4 on rare, tier 6 on exotic.
        int centre = Math.Clamp(tier / 2, 0, 3);
        int k = rng.PickWeighted(Spread) - 2;
        int r = Math.Clamp(centre + k, 0, uniqueOk ? 4 : 3);
        return RollRarity(lib, (Rarity)r, tags, rng, age);
    }

    static LootItem RollRarity(PoiLibrary lib, Rarity rarity, HashSet<string> tags, Rng rng, Age age = Age.New)
    {
        bool Keeps(LootItem i) => age != Age.OldWorld || !i.Tags.Contains("food");
        var pool = lib.Loot.Where(i => i.Rarity == rarity && Keeps(i)).ToList();
        if (pool.Count == 0) pool = lib.Loot.Where(i => i.Rarity == Rarity.Common && Keeps(i)).ToList();
        var weights = pool.Select(i => i.Tags.Any(tags.Contains) ? 3f : 1f).ToList();
        return pool[Math.Max(0, rng.PickWeighted(weights))];
    }

    // ---- enemies ----------------------------------------------------------------------

    static void Enemies(PoiLibrary lib, PoiInstance inst, Rng rng)
    {
        var layout = inst.Layout;
        var nav = inst.Nav();
        var roster = lib.Roster(inst.Family.Roster);
        var guards = roster.Where(e => e.Kind == EnemyKind.Guard).ToList();
        var turrets = roster.Where(e => e.Kind == EnemyKind.Turret).ToList();
        var hovers = roster.Where(e => e.Kind == EnemyKind.Hover).ToList();
        var boss = roster.FirstOrDefault(e => e.Kind == EnemyKind.Boss);
        var bloom = lib.Roster(Faction.Hollow.Id());
        var natives = bloom.Where(e => e.Kind == EnemyKind.Hollow).ToList();
        var fauna = lib.Roster("fauna");
        // Owned places have their faction on duty; the old world's places have their dormant security, awake while powered.
        bool owned = guards.Count > 0;

        // Spawn spots: the authored slots that are still clear, then any floor on the path.
        var spots = layout.Slots.Where(s => s.Kind == SlotKind.Spawn && nav.Standable(s.Cell) && inst.Blocks.Get(s.Cell).IsAir).Select(s => (s.Cell, s.Module)).ToList();
        rng.Shuffle(spots);
        foreach (int mi in layout.CriticalPath)
        {
            var floor = StateTransforms.FloorCells(inst, layout.Modules[mi], nav).Where(c => !nav.LadderAt(c) && !nav.LiftAt(c)).ToList();
            if (floor.Count > 0) spots.Add((rng.Pick(floor), mi));
        }
        var used = new HashSet<Int3>();
        (Int3 Cell, int Module)? Next(Func<(Int3 Cell, int Module), bool>? where = null)
        {
            foreach (var s in spots)
                if ((where == null || where(s)) && used.Add(s.Cell)) return s;
            return null;
        }
        int TierAt(Int3 c) => (int)inst.Rarity + Decay.LevelAt(layout, c.Y).Tier - (int)inst.Rarity + Decay.LevelAt(layout, c.Y).Depth / 2;
        void Spawn(EnemyDef def, EnemyKind kind, string name, bool defender, (Int3 Cell, int Module)? at = null)
        {
            var spot = at ?? Next();
            if (spot is { } s) inst.Enemies.Add(new EnemySpawn(s.Cell, name, kind, def, s.Module, defender) { Tier = TierAt(s.Cell) });
        }

        int count = inst.Rarity switch { Rarity.Common => rng.Between(3, 5), Rarity.Uncommon => rng.Between(5, 8), Rarity.Rare => rng.Between(8, 12), _ => rng.Between(10, 15) };
        count += layout.Extras.Levels.Count(l => l.Depth > 0);
        var bossRoom = layout.BossModule >= 0 ? layout.BossModule : -1;
        (Int3, int)? bossSpot = null;
        if (bossRoom >= 0)
        {
            var slot = layout.Slots.FirstOrDefault(s => s.Module == bossRoom && s.Kind == SlotKind.Boss && nav.Standable(s.Cell) && inst.Blocks.Get(s.Cell).IsAir);
            Int3 c = slot?.Cell ?? Fixtures.FreeCell(inst, bossRoom, rng);
            bossSpot = (c, bossRoom);
            used.Add(c);
        }
        var vaultFloor = StateTransforms.FloorCells(inst, layout.Vault, nav).Where(c => !used.Contains(c) && !nav.LadderAt(c) && !nav.LiftAt(c)).ToList();

        switch (inst.State)
        {
            case PoiState.Active when owned:
                for (int i = 0; i < count; i++) { var d = rng.Pick(guards); Spawn(d, EnemyKind.Guard, d.Name, true); }
                for (int i = 0; i <= Math.Min(2, (int)inst.Rarity) && vaultFloor.Count > 0; i++)
                {
                    var d = rng.Pick(guards);
                    Spawn(d, EnemyKind.Guard, d.Name, true, (rng.Pick(vaultFloor), layout.VaultModule));
                }
                Int3 side = layout.EntranceFacing.RotateCw(1).Vec();
                foreach (int s in new[] { -2, 3 })
                {
                    Int3 c = layout.EntranceCell + side * s;
                    if (!nav.Standable(c) || guards.Count == 0) continue;
                    var d = rng.Pick(guards);
                    Spawn(d, EnemyKind.Guard, d.Name, true, (c, layout.EntranceModule));
                }
                foreach (int k in layout.KeyModules.Take(1 + Math.Min(2, (int)inst.Rarity)))
                    if (turrets.Count > 0 && StateTransforms.TopOf(inst, layout.Modules[k]) is Int3 top)
                    {
                        var d = rng.Pick(turrets);
                        Spawn(d, EnemyKind.Turret, d.Name, true, (top, k));
                    }
                for (int i = 0; i < Math.Min(2, (int)inst.Rarity) && hovers.Count > 0; i++)
                {
                    var d = rng.Pick(hovers);
                    Spawn(d, EnemyKind.Hover, d.Name, true, (layout.EntranceCell + layout.EntranceFacing.Vec() * (3 + i * 2) + Int3.Up * 4, layout.EntranceModule));
                }
                if (bossSpot != null && boss != null) Spawn(boss, EnemyKind.Boss, boss.Name, true, bossSpot);
                break;

            case PoiState.Active:
            case PoiState.Abandoned:
                // Nobody on duty. Things have moved in, and below ground the Bloom's creatures.
                for (int i = rng.Between(1, 3); i > 0; i--) { var d = rng.Pick(fauna); Spawn(d, EnemyKind.Fauna, d.Name, false, Next(s => s.Cell.Y >= 0) ?? Next()); }
                foreach (var lvl in layout.Extras.Levels.Where(l => l.Wrongness > 0.35f))
                    for (int i = 0; i < 1 + (int)(lvl.Wrongness * 3); i++)
                    {
                        var d = rng.Pick(natives);
                        Spawn(d, EnemyKind.Hollow, d.Name, false, Next(s => s.Cell.Y >= lvl.WalkY && s.Cell.Y <= lvl.WalkY + 2));
                    }
                if (bossSpot != null)
                {
                    var deep = Decay.LevelAt(layout, bossSpot.Value.Item1.Y);
                    var heart = bloom.FirstOrDefault(e => e.Kind == EnemyKind.Boss);
                    if (deep.Wrongness > 0.6f && heart != null) Spawn(heart, EnemyKind.Boss, heart.Name, false, bossSpot);
                    else { var d = rng.Pick(fauna); Spawn(d, EnemyKind.Fauna, "Alpha " + d.Name, false, bossSpot); }
                }
                break;

            case PoiState.Infested:
                for (int i = 0; i < count; i++)
                {
                    if (i % 2 == 0 && guards.Count > 0)
                    {
                        var d = rng.Pick(guards);
                        Spawn(d, EnemyKind.Corrupted, "Bloom-ridden " + d.Name, false);
                    }
                    else
                    {
                        var d = rng.Pick(natives);
                        var near = inst.Growths.Select(gc => gc + Int3.Up).Where(c => nav.Standable(c) && !used.Contains(c) && layout.Occupied[c]).ToList();
                        Spawn(d, EnemyKind.Hollow, d.Name, false, near.Count > 0 ? (rng.Pick(near), -1) : null);
                    }
                }
                var bh = bloom.FirstOrDefault(e => e.Kind == EnemyKind.Boss);
                if (bossSpot != null && bh != null) Spawn(bh, EnemyKind.Boss, bh.Name, false, bossSpot);
                break;

            case PoiState.Ruined:
                for (int i = rng.Between(1, 4); i > 0; i--) { var d = rng.Pick(fauna); Spawn(d, EnemyKind.Fauna, d.Name, false); }
                if (rng.Chance(0.3f)) { var d = rng.Pick(natives); Spawn(d, EnemyKind.Hollow, d.Name, false); }
                if (bossSpot != null)
                {
                    if (inst.Family.Roster == "precursor" && boss != null) Spawn(boss, EnemyKind.Boss, boss.Name, true, bossSpot);
                    else { var d = rng.Pick(fauna); Spawn(d, EnemyKind.Fauna, "Alpha " + d.Name, false, bossSpot); }
                }
                break;
        }
        // Spawn markers the architect laid (gang camps, patrol starts) become tagged spawns for a later pass.
        foreach (var mk in layout.Extras.Markers.Where(m => m.Kind == "gang-spawn"))
        {
            var gang = lib.Roster("raiders");
            if (gang.Count == 0 || inst.State != PoiState.Active) continue; // only while the gang is home
            for (int i = 0; i < 3; i++)
            {
                var d = rng.Pick(gang.Where(e => e.Kind == EnemyKind.Guard).ToList());
                Int3 c = mk.Cell + new Int3(i % 2, 0, i / 2);
                if (nav.Standable(c)) inst.Enemies.Add(new EnemySpawn(c, d.Name, EnemyKind.Guard, d, layout.EntranceModule, false) { Tier = (int)inst.Rarity });
            }
        }
    }

    // ---- story ------------------------------------------------------------------------

    static readonly Rgb Blood = new(0x4a, 0x18, 0x14);
    static readonly Rgb Soot = new(0x1a, 0x14, 0x12);

    /// <summary>
    /// Frozen moments: things stopped suddenly (the meal left on the table, the barricade, the
    /// last stand) and time and the Bloom did the rest. Nothing here is written down.
    /// </summary>
    static void Story(PoiInstance inst, Rng rng)
    {
        var layout = inst.Layout;
        var g = inst.Blocks;
        var nav = inst.Nav();
        bool quiet = inst.State != PoiState.Active;
        var props = layout.Slots.Where(s => s.Kind == SlotKind.Prop && g.Get(s.Cell).IsAir && !inst.Protected[s.Cell]).ToList();
        rng.Shuffle(props);
        var kit = inst.Kit;
        Rgb wood = new(0x6a, 0x4e, 0x36);
        inst.Story.AddRange(layout.Extras.Story);

        void Add(string kind, Int3 c, string note) => inst.Story.Add(new StoryProp(kind, c, note));

        // A meal left on the table: they stopped mid-bite.
        foreach (var meal in props.Where(s => layout.Modules[s.Module].Kind is ModuleKind.Room or ModuleKind.Hall).Take(quiet ? 2 : 1).ToList())
        {
            props.Remove(meal);
            // Long after the fall: indoors the table and plates survive faded and dusty; out in the weather nothing is left on it.
            bool weathered = inst.AgeOf(meal.Module) == Age.OldWorld && inst.ExposureOf(meal.Module) != Exposure.Indoors;
            g.Set(meal.Cell, new Block(quiet && !weathered ? BlockType.Meal : BlockType.Table, Material.Timber, wood, Role.Story, Dir.North, (byte)rng.Range(0, 3)));
            if (quiet && !weathered) Add("abandoned meal", meal.Cell, inst.AgeOf(meal.Module) == Age.OldWorld
                ? "Plates still out, chairs pushed back in a hurry. Whatever was on them went to dust long ago."
                : "Plates still out, half eaten. The chairs were pushed back in a hurry.");
        }

        // Welded doors. Something has been at them from the other side.
        foreach (var cap in layout.Capped)
        {
            if (!rng.Chance(0.5f)) continue;
            var color = (kit.BySymbol('k') ?? kit.Palette[0]).Colors[0].Scale(0.6f);
            foreach (Int3 c in cap.Cells)
                g.Set(c, new Block(BlockType.SealedDoor, Material.Steel, color, Role.Story, cap.Facing.Opposite(), (byte)(quiet ? 1 : 0)));
            if (quiet) Add("sealed door", cap.Cells[0], "Welded shut from this side. The scratches are on the other.");
        }

        if (!quiet) return;

        // Scorched barricades in rooms on the path.
        var keyRooms = layout.KeyModules.Where(k => k != layout.VaultModule && layout.Modules[k].Kind is ModuleKind.Room or ModuleKind.Hall).ToList();
        for (int n = 0; n < 1 + Math.Min(2, layout.Extras.Levels.Count / 3) && keyRooms.Count > 0; n++)
        {
            if (!rng.Chance(0.8f)) continue;
            var m = layout.Modules[rng.Pick(keyRooms)];
            var floor = StateTransforms.FloorCells(inst, m, nav);
            var line = floor.Where(c => !inst.Protected[c]).OrderBy(_ => rng.Float()).Take(rng.Between(2, 4)).ToList();
            foreach (Int3 c in line) g.Set(c, new Block(BlockType.Barricade, Material.Timber, wood.Lerp(Soot, 0.6f), Role.Story, Dirs.Horizontal[rng.Range(0, 4)]));
            foreach (Int3 c in floor.Where(c => g.Get(c).IsAir).OrderBy(_ => rng.Float()).Take(5))
                g.Set(c, new Block(BlockType.Scorch, Material.Ash, Soot, Role.Story));
            foreach (Int3 c in m.Cells)
            {
                if (!g.InBounds(c)) continue;
                ref Block w = ref g.At(c);
                if (!w.IsAir && w.Role == Role.Wall && rng.Chance(0.35f)) w.Color = w.Color.Lerp(Soot, 0.65f);
            }
            if (line.Count > 0) Add("scorched barricade", line[0], "Bunks and crates stacked against the door, burned from this side.");
        }

        // Someone sat down by a terminal and never got up.
        foreach (var t in inst.Terminals)
        {
            if (!rng.Chance(0.6f)) continue;
            if (nav.AccessCell(t.Cell) is Int3 c && g.Get(c).IsAir && !inst.Protected[c])
            {
                g.Set(c, new Block(BlockType.Remains, Material.Cloth, new Rgb(0x8a, 0x84, 0x70), Role.Story, Dirs.Horizontal[rng.Range(0, 4)]));
                Add("remains", c, "A suit slumped against the console. The screen is still on.");
                break;
            }
        }

        // Drag marks toward somewhere deeper.
        if (inst.CriticalPath.Count > 12 && rng.Chance(inst.State == PoiState.Ruined ? 0.3f : 0.7f))
        {
            int start = rng.Range(inst.CriticalPath.Count / 3, inst.CriticalPath.Count - 6);
            for (int i = start; i < start + 5; i++)
            {
                Int3 c = inst.CriticalPath[i];
                if (g.Get(c).IsAir) g.Set(c, new Block(BlockType.Scorch, Material.Ash, Blood, Role.Story, Dir.North, 1));
            }
            Add("drag marks", inst.CriticalPath[start], "Something was dragged this way. Deeper in.");
        }

        // A last stand: by the core where there is one, else at the vault door.
        if (inst.Rarity != Rarity.Common || rng.Chance(0.4f))
        {
            Int3 at = inst.HasCore ? inst.CoreCell : inst.Caches.FirstOrDefault(c => c.InVault)?.Cell ?? layout.EntranceCell;
            var around = Dirs.Horizontal.Select(d => at + d.Vec()).Where(c => g.Get(c).IsAir && !inst.Protected[c] && nav.Standable(c)).ToList();
            if (around.Count > 0)
            {
                var c = rng.Pick(around);
                g.Set(c, new Block(BlockType.Remains, Material.Cloth, new Rgb(0x7a, 0x70, 0x60), Role.Story, Dirs.Horizontal[rng.Range(0, 4)], 1));
                Add("last stand", c, inst.HasCore ? "They held the core room. The core is still here." : "They held the vault door to the end.");
            }
        }
    }
}
