#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// What changes every spawn: loot, who is waiting, the props that tell a story, and the logs.
/// </summary>
public static class Dressing
{
    public static void Apply(PoiLibrary lib, PoiInstance inst, Rng rng)
    {
        Story(inst, rng.Fork("story"));
        Loot(lib, inst, rng.Fork("loot"));
        Enemies(lib, inst, rng.Fork("enemies"));
        foreach (var t in inst.Terminals)
        {
            (t.Text, t.MidSentence) = Logs.Write(lib, inst, rng.Fork("log" + t.Cell));
        }
    }

    // ---- loot -------------------------------------------------------------------------

    /// <summary>Rolls and tier odds (tiers 1 to 4) per rarity, for side caches and vault caches.</summary>
    static (int rolls, float[] tiers) Table(Rarity r, bool vault) => (r, vault) switch
    {
        (Rarity.Common, false) => (2, new[] { 0.7f, 0.3f, 0f, 0f }),
        (Rarity.Common, true) => (3, new[] { 0.35f, 0.5f, 0.15f, 0f }),
        (Rarity.Uncommon, false) => (3, new[] { 0.4f, 0.5f, 0.1f, 0f }),
        (Rarity.Uncommon, true) => (4, new[] { 0.1f, 0.5f, 0.4f, 0f }),
        (Rarity.Rare, false) => (3, new[] { 0.1f, 0.5f, 0.4f, 0f }),
        _ => (5, new[] { 0f, 0.25f, 0.5f, 0.25f }),
    };

    static void Loot(PoiLibrary lib, PoiInstance inst, Rng rng)
    {
        foreach (var cache in inst.Caches)
        {
            var (rolls, odds) = Table(inst.Rarity, cache.InVault);
            if (!cache.InVault)
            {
                // Somebody got to the side rooms first; the vault is always sealed.
                if (inst.State == PoiState.Abandoned && rng.Chance(0.35f)) { cache.PickedOver = true; rolls = 1; }
                if (inst.State == PoiState.Ruined && rng.Chance(0.3f)) rolls--;
            }
            for (int i = 0; i < rolls; i++) cache.Items.Add(Roll(lib, odds, rng, inst.Rarity == Rarity.Rare));
            // Rare vaults have a real chance at a unique material on top.
            if (cache.InVault && inst.Rarity == Rarity.Rare && rng.Chance(0.5f))
                cache.Items.Add(Roll(lib, new[] { 0f, 0f, 0f, 1f }, rng, true));
        }
    }

    static LootItem Roll(PoiLibrary lib, float[] odds, Rng rng, bool uniquesAllowed)
    {
        int tier = rng.PickWeighted(odds) + 1;
        var pool = lib.Loot.Where(i => i.Tier == tier && (uniquesAllowed || !i.Unique)).ToList();
        if (pool.Count == 0) pool = lib.Loot.Where(i => i.Tier == 1).ToList();
        return rng.Pick(pool);
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
        var fauna = lib.Roster("fauna");

        // Spawn spots: the authored slots that are still clear, then any floor on the path.
        var spots = layout.Slots.Where(s => s.Kind == SlotKind.Spawn && nav.Standable(s.Cell)).Select(s => (s.Cell, s.Module)).ToList();
        rng.Shuffle(spots);
        foreach (int mi in layout.CriticalPath)
        {
            var floor = StateTransforms.FloorCells(inst, layout.Modules[mi], nav);
            if (floor.Count > 0) spots.Add((rng.Pick(floor), mi));
        }
        var used = new HashSet<Int3>();
        (Int3 Cell, int Module)? Next()
        {
            foreach (var s in spots)
                if (used.Add(s.Cell)) return s;
            return null;
        }
        void Spawn(EnemyDef def, EnemyKind kind, string name, bool defender, (Int3 Cell, int Module)? at = null)
        {
            var spot = at ?? Next();
            if (spot is { } s) inst.Enemies.Add(new EnemySpawn(s.Cell, name, kind, def, s.Module, defender));
        }

        int count = inst.Rarity switch { Rarity.Common => rng.Between(3, 5), Rarity.Uncommon => rng.Between(5, 8), _ => rng.Between(8, 12) };
        var bossRoom = layout.BossModule >= 0 ? layout.BossModule : -1;
        (Int3, int)? bossSpot = null;
        if (bossRoom >= 0)
        {
            var slot = layout.Slots.FirstOrDefault(s => s.Module == bossRoom && s.Kind == SlotKind.Boss && nav.Standable(s.Cell));
            Int3 c = slot?.Cell ?? Fixtures.FreeCell(inst, bossRoom, rng);
            bossSpot = (c, bossRoom);
            used.Add(c);
        }
        var vaultFloor = StateTransforms.FloorCells(inst, layout.Vault, nav).Where(c => !used.Contains(c)).ToList();

        switch (inst.State)
        {
            case PoiState.Active:
                for (int i = 0; i < count; i++) { var d = rng.Pick(guards); Spawn(d, EnemyKind.Guard, d.Name, true); }
                // The vault is guarded to match the rarity.
                for (int i = 0; i <= (int)inst.Rarity && vaultFloor.Count > 0; i++)
                {
                    var d = rng.Pick(guards);
                    Spawn(d, EnemyKind.Guard, d.Name, true, (rng.Pick(vaultFloor), layout.VaultModule));
                }
                // Emplacements on the roofs, drones over the way in.
                foreach (int k in layout.KeyModules.Take(1 + (int)inst.Rarity))
                    if (turrets.Count > 0 && StateTransforms.TopOf(inst, layout.Modules[k]) is Int3 top)
                    {
                        var d = rng.Pick(turrets);
                        Spawn(d, EnemyKind.Turret, d.Name, true, (top, k));
                    }
                for (int i = 0; i < (int)inst.Rarity && hovers.Count > 0; i++)
                {
                    var d = rng.Pick(hovers);
                    Spawn(d, EnemyKind.Hover, d.Name, true, (layout.EntranceCell + layout.EntranceFacing.Vec() * (3 + i * 2) + Int3.Up * 4, 0));
                }
                if (bossSpot != null && boss != null) Spawn(boss, EnemyKind.Boss, boss.Name, true, bossSpot);
                break;

            case PoiState.Abandoned:
                // Nobody on duty. Things have moved in.
                for (int i = rng.Between(0, 3); i > 0; i--) { var d = rng.Pick(fauna); Spawn(d, EnemyKind.Fauna, d.Name, false); }
                if (bossSpot != null) { var d = rng.Pick(fauna); Spawn(d, EnemyKind.Fauna, "Alpha " + d.Name, false, bossSpot); }
                break;

            case PoiState.Infested:
                var natives = bloom.Where(e => e.Kind == EnemyKind.Hollow).ToList();
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
                        var near = inst.Growths.Select(gc => gc + Int3.Up).Where(c => nav.Standable(c) && !used.Contains(c)).ToList();
                        Spawn(d, EnemyKind.Hollow, d.Name, false, near.Count > 0 ? (rng.Pick(near), -1) : null);
                    }
                }
                var heart = bloom.FirstOrDefault(e => e.Kind == EnemyKind.Boss);
                if (bossSpot != null && heart != null) Spawn(heart, EnemyKind.Boss, heart.Name, false, bossSpot);
                break;

            case PoiState.Ruined:
                for (int i = rng.Between(1, 4); i > 0; i--) { var d = rng.Pick(fauna); Spawn(d, EnemyKind.Fauna, d.Name, false); }
                if (rng.Chance(0.3f)) { var d = rng.Pick(bloom.Where(e => e.Kind == EnemyKind.Hollow).ToList()); Spawn(d, EnemyKind.Hollow, d.Name, false); }
                // Precursor guardians outlast everything; elsewhere something big lives in the rubble.
                if (bossSpot != null)
                {
                    if (inst.Family.Roster == "precursor" && boss != null) Spawn(boss, EnemyKind.Boss, boss.Name, true, bossSpot);
                    else { var d = rng.Pick(fauna); Spawn(d, EnemyKind.Fauna, "Alpha " + d.Name, false, bossSpot); }
                }
                break;
        }
    }

    // ---- story ------------------------------------------------------------------------

    static readonly Rgb Blood = new(0x5a, 0x10, 0x12);
    static readonly Rgb Soot = new(0x1a, 0x14, 0x12);

    /// <summary>Small scenes that hint at what happened. Nothing here explains it.</summary>
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

        void Add(string kind, Int3 c, string note) => inst.Story.Add(new StoryProp(kind, c, note));

        // An abandoned meal, still on the table.
        var meal = props.FirstOrDefault(s => layout.Modules[s.Module].Kind is ModuleKind.Room or ModuleKind.Hall);
        if (meal != null)
        {
            props.Remove(meal);
            g.Set(meal.Cell, new Block(quiet ? BlockType.Meal : BlockType.Table, Material.Timber, wood, Role.Story, Dir.North, (byte)rng.Range(0, 3)));
            if (quiet) Add("abandoned meal", meal.Cell, "Plates for four, half eaten. The chairs were pushed back in a hurry.");
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

        // A scorched barricade in a room on the path.
        var keyRooms = layout.KeyModules.Where(k => k != layout.VaultModule && layout.Modules[k].Kind is ModuleKind.Room or ModuleKind.Hall).ToList();
        if (keyRooms.Count > 0 && rng.Chance(0.8f))
        {
            var m = layout.Modules[rng.Pick(keyRooms)];
            var floor = StateTransforms.FloorCells(inst, m, nav);
            var line = floor.Where(c => !inst.Protected[c]).OrderBy(_ => rng.Float()).Take(rng.Between(2, 4)).ToList();
            foreach (Int3 c in line) g.Set(c, new Block(BlockType.Barricade, Material.Timber, wood.Lerp(Soot, 0.6f), Role.Story, Dirs.Horizontal[rng.Range(0, 4)]));
            foreach (Int3 c in floor.Where(c => g.Get(c).IsAir).OrderBy(_ => rng.Float()).Take(5))
                g.Set(c, new Block(BlockType.Scorch, Material.Ash, Soot, Role.Story));
            foreach (Int3 c in m.Cells)
            {
                ref Block w = ref g.At(c);
                if (!w.IsAir && w.Role == Role.Wall && rng.Chance(0.35f)) w.Color = w.Color.Lerp(Soot, 0.65f);
            }
            if (line.Count > 0) Add("scorched barricade", line[0], "Bunks and crates stacked against the door, burned from this side.");
        }

        // Someone sat down by a terminal and never got up.
        foreach (var t in inst.Terminals)
        {
            if (!rng.Chance(0.6f)) continue;
            if (nav.AccessCell(t.Cell) is Int3 c && g.Get(c).IsAir)
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

        // A last stand by the core.
        if (inst.Rarity != Rarity.Common || rng.Chance(0.4f))
        {
            var around = Dirs.Horizontal.Select(d => inst.CoreCell + d.Vec()).Where(c => g.Get(c).IsAir && !inst.Protected[c] && nav.Standable(c)).ToList();
            if (around.Count > 0)
            {
                var c = rng.Pick(around);
                g.Set(c, new Block(BlockType.Remains, Material.Cloth, new Rgb(0x7a, 0x70, 0x60), Role.Story, Dirs.Horizontal[rng.Range(0, 4)], 1));
                Add("last stand", c, "They held the core room. The core is still here.");
            }
        }
    }
}

/// <summary>Terminal logs from short templates. Most of them stop mid-sentence.</summary>
public static class Logs
{
    public static (string Text, bool MidSentence) Write(PoiLibrary lib, PoiInstance inst, Rng rng)
    {
        // Lines come from the roster's section (a faction id, or "precursor" for the ruins);
        // headers and names from who wrote them: humans, Vharn, Kessari or the Precursors.
        string section = inst.Family.Roster;
        string group = section switch
        {
            "vharn" or "kessari" or "precursor" => section,
            _ => "human",
        };
        var names = Names(lib, group);

        string Fill(string line)
        {
            return line
                .Replace("{name}", rng.Pick(names)).Replace("{name2}", rng.Pick(names))
                .Replace("{thing}", lib.LogSections.TryGetValue("things", out var things) ? rng.Pick(things) : "the hum")
                .Replace("{n4}", rng.Range(100, 9999).ToString("0000", CultureInfo.InvariantCulture))
                .Replace("{n}", rng.Between(2, 9).ToString(CultureInfo.InvariantCulture))
                .Replace("{day}", rng.Between(12, 380).ToString(CultureInfo.InvariantCulture))
                .Replace("{pct}", rng.Between(31, 88).ToString(CultureInfo.InvariantCulture))
                .Replace("{code}", $"{(char)('A' + rng.Range(0, 26))}{(char)('A' + rng.Range(0, 26))}-{rng.Between(10, 99)}");
        }

        var lines = new List<string>();
        string header = Pick(lib, "header." + group, rng) ?? "LOG";
        if (Pick(lib, section, rng) is string f) lines.Add(f);
        if (rng.Chance(0.5f) && Pick(lib, "any", rng) is string a) lines.Add(a);
        if (Pick(lib, inst.State.ToString().ToLowerInvariant(), rng) is string s) lines.Add(s);
        for (int i = 0; i < lines.Count; i++) lines[i] = Fill(lines[i]);
        // {thing} can contain {n}; fill twice.
        for (int i = 0; i < lines.Count; i++) lines[i] = Fill(lines[i]);

        // Most logs stop partway through their last sentence.
        if (lines.Count > 0 && rng.Chance(0.6f))
        {
            string[] words = lines[^1].Split(' ');
            if (words.Length > 3)
            {
                int keep = rng.Between(Math.Max(2, words.Length * 3 / 10), Math.Max(2, words.Length * 8 / 10));
                lines[^1] = string.Join(' ', words.Take(keep)).TrimEnd('.', ',', '!', '?', ';', ':');
            }
        }
        string text = Fill(header) + "\n\n" + string.Join("\n", lines);
        return (text, EndsMidSentence(text));
    }

    public static bool EndsMidSentence(string text)
    {
        string t = text.TrimEnd();
        return t.Length > 0 && !".!?\"".Contains(t[^1]);
    }

    static string? Pick(PoiLibrary lib, string section, Rng rng) =>
        lib.LogSections.TryGetValue(section, out var lines) && lines.Count > 0 ? rng.Pick(lines) : null;

    static List<string> Names(PoiLibrary lib, string group) =>
        lib.LogSections.TryGetValue("names." + group, out var lines)
            ? lines.SelectMany(l => l.Split(',')).Select(n => n.Trim()).Where(n => n.Length > 0).ToList()
            : new List<string> { "Unknown" };
}
