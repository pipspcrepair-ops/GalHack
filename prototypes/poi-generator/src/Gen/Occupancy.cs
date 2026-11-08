#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>Who is in a place: a layer of its own, separate from its layout (docs/world-themes.md, Occupancy).</summary>
public enum Occupant : byte { Empty, Nesters, Turned, Scavengers, Squatters, Gang, Machines, Warped, Prowlers, Faction, Cyborgs, Robots, Cult, Keepers, Sleepers, Guardians, Mimics }

/// <summary>How two groups share a mixed place.</summary>
public enum Sharing : byte { Alone, Floors, FrontLine, Layers }

/// <summary>
/// One place's occupancy: who, perhaps a second group and how they share it, perhaps only the trace of
/// someone long gone. Rolled once per place from its seed, so an empty place stays empty on a revisit.
/// </summary>
public sealed record OccupancyRoll(Occupant Main, Occupant? Second = null, Sharing How = Sharing.Alone, Occupant? Trace = null)
{
    /// <summary>The world theme's tags: what its people left behind, whoever is in the place now.</summary>
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    public override string ToString() => Second is { } s ? $"{Main} and {s} ({How})" : Trace is { } t ? $"{Main}, with traces of {t}" : Main.ToString();
}

/// <summary>
/// The one way people come into a place, so what makes them can change without touching what they
/// leave behind. Today's people are the townsfolk and the rosters; the bestiary takes this over.
/// </summary>
public interface ISurvivors
{
    /// <summary>People who are nobody's enemy, settled at these spots.</summary>
    void Settle(PoiInstance inst, IReadOnlyList<Int3> spots, int module, int count, bool lookout);
    /// <summary>Those who'd fight for a place, by the roster it draws on.</summary>
    IReadOnlyList<EnemyDef> Fighters(PoiLibrary lib, string roster);
}

/// <summary>The people there are for now: townsfolk who keep to their spots, and the rosters.</summary>
public sealed class Townspeople : ISurvivors
{
    public void Settle(PoiInstance inst, IReadOnlyList<Int3> free, int module, int count, bool lookout)
    {
        Townsfolk town = inst.Townsfolk ??= new Townsfolk();
        int first = town.Spots.Count;
        town.Spots.Add(new ActivitySpot("home", free[0], module));
        town.Spots.Add(new ActivitySpot("chat", free[1], module));
        town.Spots.Add(new ActivitySpot("work", free[2], module));
        town.Links.Add((first, first + 1));
        town.Links.Add((first + 1, first + 2));
        town.Links.Add((first, first + 2));
        for (int i = 0; i < count; i++)
        {
            int spot = first + i % 3;
            town.People.Add(i == 0 && lookout ? new CitizenSpawn("guard", "patrol", free[spot - first], spot)
                : i % 2 == 0 ? new CitizenSpawn("worker", "steady", free[spot - first], spot) : new CitizenSpawn("wanderer", "amble", free[spot - first], spot));
        }
        town.AreaCenter = new Int3((int)town.Spots.Average(s => s.Cell.X), 0, (int)town.Spots.Average(s => s.Cell.Z));
        town.AreaRadius = MathF.Min(64f, town.Spots.Max(s => s.Cell.FlatDistance(town.AreaCenter)) + 10f);
    }

    public IReadOnlyList<EnemyDef> Fighters(PoiLibrary lib, string roster) => lib.Roster(roster);
}

/// <summary>
/// The same layout, different people. Works on any place: it clears who the state put there, then fills
/// the rooms with what this occupant leaves: nests, bodies, camp fires, barricades, bunks. It never
/// touches a protected cell, so the way to the loot stays open, and it can't change the plan.
/// </summary>
public static class OccupancyLayer
{
    /// <summary>Who makes the people. One place to swap them.</summary>
    public static ISurvivors Survivors = new Townspeople();

    static Occupant? Code(string code) => code switch
    {
        "EMP" => Occupant.Empty, "NST" => Occupant.Nesters, "HSK" => Occupant.Turned, "SCV" => Occupant.Scavengers, "SQT" => Occupant.Squatters,
        "GNG" => Occupant.Gang, "MCH" => Occupant.Machines, "WRP" or "HOL" => Occupant.Warped, "MUT" => Occupant.Prowlers,
        "FAC" => Occupant.Faction, "CYB" => Occupant.Cyborgs, "BOT" => Occupant.Robots, "CLT" => Occupant.Cult,
        "KPR" => Occupant.Keepers, "SLP" => Occupant.Sleepers, "GRD" => Occupant.Guardians, "MIM" => Occupant.Mimics, _ => null,
    };

    /// <summary>Whether a place takes the layer: the living and the Bloom keep who they have, wrecks and alien sites are their own stories.</summary>
    public static bool Applies(FamilyDef f, PoiState state) =>
        state is PoiState.Abandoned or PoiState.Ruined && f.Faction == Faction.None && !f.Tags.Contains("settlement") && !WorldProfile.Unbuilt(f);

    /// <summary>Rolls a place's occupancy from a world's mix. Pairs that can't share a map never come up; a family's own leanings shift the odds.</summary>
    public static OccupancyRoll Roll(IReadOnlyDictionary<string, float> mix, FamilyDef family, ulong seed)
    {
        var rng = new Rng(Hash.Combine(seed, 0x0CC0FAUL));
        var keys = mix.Keys.Where(k => Code(k) != null).OrderBy(k => k, StringComparer.Ordinal).ToList();
        if (keys.Count == 0) return new OccupancyRoll(Occupant.Empty);
        var weights = keys.Select(k =>
        {
            float w = mix[k];
            if (family.Tags.Contains("farm") && k == "SQT") w *= 2f;
            if (family.Tags.Contains("industry") && k == "MCH") w *= 1.5f;
            return w;
        }).ToList();
        Occupant Next() => Code(keys[Math.Max(0, rng.PickWeighted(weights))])!.Value;
        Occupant main = Next();
        // About one place in five holds two groups at once.
        if (main != Occupant.Empty && rng.Chance(0.2f))
        {
            Occupant second = Next();
            if (second != main && second != Occupant.Empty)
                return new OccupancyRoll(main, second, rng.Chance(0.6f) ? Sharing.Floors : Sharing.FrontLine);
        }
        // An empty place may keep the trace of someone long gone: a story, no spawn.
        if (main == Occupant.Empty && rng.Chance(0.35f))
        {
            Occupant trace = Next();
            if (trace != Occupant.Empty) return new OccupancyRoll(main, null, Sharing.Layers, trace);
        }
        return new OccupancyRoll(main);
    }

    static readonly Rgb Ember = new(0xff, 0x8a, 0x3a);

    sealed class Ctx
    {
        public PoiLibrary Lib = null!;
        public PoiInstance Inst = null!;
        public NavGrid Nav = null!;
        public Rng R = null!;
        public int Placed;

        public void Put(Int3 c, BlockType t, Material m, Rgb color, Role role = Role.Story, Dir facing = Dir.North)
        {
            var g = Inst.Blocks;
            if (!g.Get(c).IsAir && g.Get(c).Type != BlockType.Plate || Inst.Protected[c]) return;
            // Nothing heavy stands on the way through a door.
            if (BlockInfo.Collides(t) && Inst.Layout.Doorways.Any(d => d.Cells.Any(x => Math.Abs(x.X - c.X) + Math.Abs(x.Z - c.Z) <= 2 && Math.Abs(x.Y - c.Y) <= 1))) return;
            g.Set(c, new Block(t, m, color, role, facing));
            Placed++;
        }

        public void Enemy(EnemyDef def, EnemyKind kind, Int3 cell, int module, int tier = 1) =>
            Inst.Enemies.Add(new EnemySpawn(cell, def.Name, kind, def, module, false) { Tier = tier });
    }

    /// <summary>Who's in a place, in place of who the state put there. Runs last, on the place as decay left it.</summary>
    public static void Apply(PoiLibrary lib, PoiInstance inst, OccupancyRoll occ, Rng rng)
    {
        inst.Occupancy = occ;
        inst.Enemies.Clear();
        var x = new Ctx { Lib = lib, Inst = inst, Nav = inst.Nav(), R = rng };
        var rooms = inst.Layout.Modules.Where(m => m.Kind is ModuleKind.Room or ModuleKind.Hall or ModuleKind.Vault or ModuleKind.Cave or ModuleKind.Corridor && m.Shape.Occupied.Count > 40).ToList();
        if (rooms.Count == 0) rooms = inst.Layout.Modules.ToList();
        List<PlacedModule> mine = rooms, theirs = rooms;
        if (occ.Second != null)
        {
            // Floors: one group above, one below. A front line: the near rooms against the far.
            var order = occ.How == Sharing.Floors
                ? rooms.OrderByDescending(m => m.Min.Y).ThenBy(m => m.Index).ToList()
                : rooms.OrderBy(m => m.Cells.First().Manhattan(inst.Layout.EntranceCell)).ToList();
            int half = Math.Max(1, order.Count / 2);
            mine = order.Take(half).ToList();
            theirs = order.Skip(half).DefaultIfEmpty(order[0]).ToList();
            Line(x, order[Math.Min(half, order.Count - 1)]);
        }
        Fill(x, occ.Main, mine, full: true);
        if (occ.Second is { } s) Fill(x, s, theirs, full: true);
        if (occ.Trace is { } t) Fill(x, t, rooms, full: false);
        Left(x, occ.Tags, rooms);
    }

    /// <summary>What a world's last days left in its rooms, keyed on its theme's tags: luggage on a queue world, dinner still on the tables in the hush, shift boards at the works.</summary>
    static void Left(Ctx x, IReadOnlyList<string> tags, List<PlacedModule> rooms)
    {
        if (tags.Contains("queue_slips"))
            foreach (PlacedModule m in Settled(x, rooms, 4))
                if (Patch(x, m, 4f) is { } p)
                    foreach (Int3 c in p.Near.Where(_ => x.R.Chance(0.35f)))
                        x.Put(c, BlockType.Crate, Material.Cloth, x.R.Pick(new[] { new Rgb(0x6a, 0x4a, 0x38), new Rgb(0x3e, 0x4a, 0x5a), new Rgb(0x7a, 0x70, 0x52), new Rgb(0x5c, 0x3a, 0x3a) }));
        if (tags.Contains("set_tables"))
            foreach (PlacedModule m in Settled(x, rooms, 4))
                if (Patch(x, m, 3f) is { } p)
                    foreach (Int3 c in p.Near.Where(c => (c.X + c.Z) % 3 == 0 && x.R.Chance(0.5f)))
                        x.Put(c, BlockType.Meal, Material.Ceramic, new Rgb(0xb4, 0xac, 0x98));
        if (tags.Contains("shift_boards"))
            foreach (PlacedModule m in Settled(x, rooms, 3))
                foreach (Int3 c in Floor(x, m).Where(c => Dirs.Horizontal.Any(d => BlockInfo.Collides(x.Inst.Blocks.Get(c + d.Vec()).Type))).Take(10))
                    x.Put(c, BlockType.Locker, Material.Steel, new Rgb(0x4e, 0x5a, 0x5e));
    }

    static void Fill(Ctx x, Occupant who, List<PlacedModule> rooms, bool full)
    {
        switch (who)
        {
            case Occupant.Nesters: Nests(x, rooms, full); break;
            case Occupant.Turned: Turned(x, rooms, full); break;
            case Occupant.Scavengers: Camp(x, rooms, full); break;
            case Occupant.Squatters: Lived(x, rooms, full); break;
            case Occupant.Gang: Held(x, rooms, full); break;
            case Occupant.Machines: Machines(x, rooms, full); break;
            case Occupant.Warped: Wrong(x, rooms, full); break;
            case Occupant.Prowlers: Den(x, rooms, full); break;
            // Those who hold a place by force leave what a gang does; machines of any kind keep it as machines do.
            case Occupant.Faction: Held(x, rooms, full); break;
            case Occupant.Cyborgs or Occupant.Robots: Machines(x, rooms, full); break;
            // People who live by a rule: lived in, kept up, and at peace with you.
            case Occupant.Cult: Lived(x, rooms, full); break;
            // The rarer ones each leave something of their own, and nothing names it.
            case Occupant.Keepers: Kept(x, rooms, full); break;
            case Occupant.Sleepers: Sleepers(x, rooms, full); break;
            case Occupant.Guardians: Guarded(x, rooms, full); break;
            case Occupant.Mimics: Copies(x, rooms, full); break;
        }
    }

    /// <summary>
    /// Sleepers, still asleep: in pods where the world had them (cold light over glass in rows), in
    /// beds in a ward where it didn't, and now and then on their feet, a crowd standing in rows all
    /// facing one way. Nobody stirs, and nothing harms you.
    /// </summary>
    static void Sleepers(Ctx x, List<PlacedModule> rooms, bool full)
    {
        Rgb frame = new(0x62, 0x60, 0x5c), body = new(0x6a, 0x5e, 0x54), glass = new(0x9a, 0xb8, 0xc8), cold = new(0x9a, 0xd0, 0xff), sheet = new(0xc4, 0xc0, 0xb4);
        IReadOnlyList<string> tags = x.Inst.Occupancy?.Tags ?? Array.Empty<string>();
        // How they sleep: the world's own leaning first, then what its people could build.
        int how = tags.Contains("pod_rows") ? 0 : tags.Contains("sleep_wards") ? 1 : x.R.Chance(0.3f) ? 2 : x.Inst.Tech is TechLevel.Low ? 1 : 0;
        Dir facing = Dirs.Horizontal[x.R.Range(0, 4)];
        foreach (PlacedModule m in Settled(x, rooms, full ? 3 : 1))
        {
            var rows = Floor(x, m).Where(c => c.X % 3 == 0 && c.Z % 2 == 0).Take(16).ToList();
            foreach (Int3 c in rows)
                switch (how)
                {
                    case 0:
                        x.Put(c, BlockType.Tank, Material.Glass, glass, Role.Story, facing);
                        break;
                    case 1:
                        x.Put(c, BlockType.Bed, Material.Cloth, sheet, Role.Story, facing);
                        x.Put(c + facing.Vec(), BlockType.Remains, Material.Cloth, body, Role.Story, facing);
                        break;
                    default:
                        x.Put(c, BlockType.Statue, Material.Cloth, body.Scale(x.R.Float(0.85f, 1.15f)), Role.Story, facing);
                        break;
                }
            if (rows.Count > 0)
            {
                x.Inst.Markers.Add(new Marker("sleepers", rows[0], how switch { 0 => "pods", 1 => "beds", _ => "standing" }));
                if (how == 0) x.Inst.Lights.Add(new LightSpot { Cell = rows[rows.Count / 2] + Int3.Up * 2, Color = cold, Mode = LightMode.Steady, Energy = 0.7f, Range = 9f, Module = m.Index });
            }
            if (how == 1)
                foreach (Int3 c in Floor(x, m).Where(c => c.X % 6 == 3 && c.Z % 4 == 1).Take(3)) x.Put(c, BlockType.Bunk, Material.Steel, frame);
        }
    }

    /// <summary>
    /// Keepers on a duty that outlived its reason: the rooms on their rounds swept clean in a ruin, a
    /// post by the way in with a bench and one lamp still kept burning, a row of identical tags along
    /// the wall, a runner laid down the middle. The keepers are at their posts, and walk their rounds.
    /// </summary>
    static void Kept(Ctx x, List<PlacedModule> rooms, bool full)
    {
        Rgb wood = new(0x6a, 0x56, 0x40), brass = new(0xb0, 0x90, 0x4a), runner = new(0x7a, 0x34, 0x2a), lamp = new(0xff, 0xd8, 0x9a);
        bool first = true;
        foreach (PlacedModule m in Settled(x, rooms, full ? 3 : 1))
        {
            foreach (Int3 c in m.Cells.Where(c => x.Inst.Blocks.Get(c).Role == Role.Debris && !BlockInfo.Collides(x.Inst.Blocks.Get(c).Type)).ToList())
                x.Inst.Blocks.Set(c, Block.Air);
            var floor = Floor(x, m);
            if (floor.Count < 8) continue;
            Int3 post = floor.OrderBy(c => c.Manhattan(x.Inst.Layout.EntranceCell)).First();
            x.Put(post, BlockType.Bench, Material.Timber, wood);
            x.Inst.Lights.Add(new LightSpot { Cell = post + Int3.Up * 2, Color = lamp, Mode = LightMode.Steady, Energy = 1.2f, Range = 9f, Module = m.Index });
            x.Inst.Markers.Add(new Marker("keeper_post", post));
            // The roster: the same tag, again and again, at one height along one wall.
            int back = floor.Min(f => f.Z);
            foreach (Int3 c in floor.Where(c => c.Z == back && BlockInfo.Collides(x.Inst.Blocks.Get(c + Dir.North.Vec()).Type)).Take(10))
                x.Put(c + Int3.Up, BlockType.Sign, Material.Bronze, brass, Role.Story, Dir.South);
            // A runner down the room, straight and kept.
            Int3 mid = floor[floor.Count / 2];
            foreach (Int3 c in floor.Where(c => c.X == mid.X && c != post)) x.Put(c, BlockType.Plate, Material.Cloth, runner);
            if (full) People(x, post, floor, m.Index, Mood.Peaceful, first ? 2 : 1, lookout: first);
            first = false;
        }
    }

    /// <summary>
    /// Guardians of a far older place: a ring of dark stone posts round the deepest room, something
    /// grown of crystal at its heart in a slow pale light, and marks nobody can read on the walls. The
    /// guardians stand on the ring.
    /// </summary>
    static void Guarded(Ctx x, List<PlacedModule> rooms, bool full)
    {
        Rgb basalt = new(0x2e, 0x2c, 0x30), glyph = new(0x8a, 0xd8, 0xd0), heart = new(0xb8, 0xe8, 0xff);
        var frames = Survivors.Fighters(x.Lib, "oldworld").Where(e => e.Kind == EnemyKind.Guard).ToList();
        bool first = true;
        foreach (PlacedModule m in Settled(x, rooms, full ? 2 : 1))
        {
            var floor = Floor(x, m);
            if (floor.Count < 12) continue;
            float cx = (float)floor.Average(c => c.X), cz = (float)floor.Average(c => c.Z);
            Int3 mid = floor.OrderBy(c => MathF.Abs(c.X - cx) + MathF.Abs(c.Z - cz)).First();
            float ring = first ? 3f : 2f;
            foreach (Int3 c in floor.Where(c => MathF.Abs(c.FlatDistance(mid) - ring) < 0.5f && (c.X + c.Z) % 2 == 0).ToList())
                x.Put(c, BlockType.Pillar, Material.Basalt, basalt);
            x.Put(mid, BlockType.Crystal, Material.Crystal, heart);
            x.Inst.Lights.Add(new LightSpot { Cell = mid + Int3.Up * 2, Color = heart, Mode = LightMode.Pulse, Energy = 1f, Range = 10f, Module = m.Index });
            foreach (Int3 c in floor.Where(c => Dirs.Horizontal.Any(d => BlockInfo.Collides(x.Inst.Blocks.Get(c + d.Vec()).Type)) && Hash.Cell(c, 0x61) % 5 == 0).Take(8).ToList())
            {
                Dir wall = Dirs.Horizontal.First(d => BlockInfo.Collides(x.Inst.Blocks.Get(c + d.Vec()).Type));
                x.Put(c + Int3.Up, BlockType.Sign, Material.Glyph, glyph, Role.Story, wall.Opposite());
            }
            x.Inst.Markers.Add(new Marker("guardian_ring", mid));
            if (full && frames.Count > 0)
                foreach (Int3 c in floor.Where(c => MathF.Abs(c.FlatDistance(mid) - ring - 1f) < 0.5f).OrderBy(c => Hash.Cell(c, 0x67)).Take(first ? 2 : 1).ToList())
                    x.Enemy(x.R.Pick(frames), EnemyKind.Guard, c, m.Index, 2);
            first = false;
        }
    }

    static readonly HashSet<BlockType> Furniture = new() { BlockType.Table, BlockType.Bench, BlockType.Bed, BlockType.Bunk, BlockType.Locker, BlockType.Shelf, BlockType.Crate, BlockType.Terminal, BlockType.Tank, BlockType.Statue };

    /// <summary>
    /// Mimics and copies: one thing in the place that can't be (a copy of a piece of its furniture,
    /// stood the right way up against the ceiling over the original), and people who are all the same
    /// person, going about it peacefully. Nothing else is wrong.
    /// </summary>
    static void Copies(Ctx x, List<PlacedModule> rooms, bool full)
    {
        BlockGrid g = x.Inst.Blocks;
        // The one wrong object: the first piece of furniture with room above it up to a ceiling.
        foreach (PlacedModule m in Settled(x, rooms, rooms.Count))
        {
            Int3? found = null, top = null;
            foreach (Int3 c in m.Cells)
            {
                if (!Furniture.Contains(g.Get(c).Type)) continue;
                Int3 up = c + Int3.Up;
                int k = 0;
                for (; k < 12 && g.InBounds(up + Int3.Up) && g.Get(up + Int3.Up).IsAir; k++) up += Int3.Up;
                if (k < 2 || !g.InBounds(up + Int3.Up) || !BlockInfo.Collides(g.Get(up + Int3.Up).Type)) continue;
                found = c;
                top = up;
                break;
            }
            if (found is not Int3 at || top is not Int3 under) continue;
            g.Set(under, g.Get(at));
            x.Inst.Markers.Add(new Marker("wrong_object", under));
            break;
        }
        if (!full) return;
        foreach (PlacedModule m in Settled(x, rooms, 1))
            if (Patch(x, m, 4f) is { } p)
            {
                People(x, p.Mid, Floor(x, m), m.Index, Mood.Peaceful, x.R.Between(3, 5), lookout: false);
                if (x.Inst.Townsfolk != null)
                {
                    x.Inst.Townsfolk.Copies = true;
                    x.Inst.Markers.Add(new Marker("copies", p.Mid));
                }
            }
    }

    static List<Int3> Floor(Ctx x, PlacedModule m) => StateTransforms.FloorCells(x.Inst, m, x.Nav).Where(c => !x.Inst.Protected[c]).ToList();

    /// <summary>A few rooms, the biggest and deepest first: where a group would settle.</summary>
    static List<PlacedModule> Settled(Ctx x, List<PlacedModule> rooms, int n) =>
        rooms.OrderByDescending(m => m.Index == x.Inst.Layout.VaultModule ? 1 : 0).ThenByDescending(m => m.Shape.Occupied.Count * (x.R.Float() + 0.4f)).Take(n).ToList();

    static (Int3 Mid, List<Int3> Near)? Patch(Ctx x, PlacedModule m, float radius)
    {
        var floor = Floor(x, m);
        if (floor.Count < 8) return null;
        Int3 mid = floor[x.R.Range(0, floor.Count)];
        return (mid, floor.Where(c => c.FlatDistance(mid) <= radius && Math.Abs(c.Y - mid.Y) <= 1).ToList());
    }

    /// <summary>Egg clusters, resin heaps, webbing from the ceilings, gnawed bones; a brood room deepest in. The burrowers themselves.</summary>
    static void Nests(Ctx x, List<PlacedModule> rooms, bool full)
    {
        Rgb resin = new(0x6e, 0x5a, 0x3c), egg = new(0xb4, 0xa4, 0x7c), bone = new(0xd0, 0xc8, 0xb4);
        var fauna = x.Lib.Roster("fauna");
        int n = full ? Math.Clamp(rooms.Count / 2, 2, 8) : 2;
        bool first = true;
        foreach (PlacedModule m in Settled(x, rooms, n))
        {
            if (Patch(x, m, first ? 7f : 4f) is not { } p) continue;
            foreach (Int3 c in p.Near)
            {
                float d = c.FlatDistance(p.Mid);
                if (x.R.Chance(0.55f - d * 0.05f)) x.Put(c, BlockType.Rubble, Material.Resin, resin.Lerp(new Rgb(0x3e, 0x34, 0x24), x.R.Float()));
                else if (x.R.Chance(first ? 0.35f : 0.18f)) x.Put(c, BlockType.Pod, Material.Chitin, egg.Scale(x.R.Float(0.8f, 1.1f)));
                else if (x.R.Chance(0.1f)) x.Put(c, BlockType.Remains, Material.Bone, bone);
            }
            Webs(x, m, p.Near, resin.Lerp(bone, 0.5f));
            if (full && fauna.Count > 0)
                for (int e = 0, count = first ? x.R.Between(3, 6) : x.R.Between(1, 3); e < count && p.Near.Count > 0; e++)
                    x.Enemy(x.R.Pick(fauna), EnemyKind.Fauna, x.R.Pick(p.Near), m.Index, first ? 2 : 1);
            first = false;
        }
        // Whatever made the nests has been chewing at the way in.
        if (full)
            foreach (PlacedModule m in rooms.Take(12))
                foreach (Int3 c in Floor(x, m).Where(_ => x.R.Chance(0.012f)))
                    x.Put(c, BlockType.Remains, Material.Bone, bone);
    }

    /// <summary>Webbing strung down from what's overhead, on real anchors: it hangs from a block or isn't there.</summary>
    static void Webs(Ctx x, PlacedModule m, List<Int3> near, Rgb colour)
    {
        foreach (Int3 c in near.Where(_ => x.R.Chance(0.2f)))
        {
            Int3 up = c;
            for (int k = 0; k < 16 && x.Inst.Blocks.Get(up + Int3.Up).IsAir; k++) up += Int3.Up;
            if (!BlockInfo.Collides(x.Inst.Blocks.Get(up + Int3.Up).Type) || up.Y - c.Y < 2) continue;
            for (int k = 0, len = x.R.Between(1, 3); k < len; k++) x.Put(up + Int3.Down * k, BlockType.Tendril, Material.Resin, colour);
        }
    }

    /// <summary>Slumped bodies in their rows, a stand made and lost, barricades broken from the far side.</summary>
    static void Turned(Ctx x, List<PlacedModule> rooms, bool full)
    {
        Rgb body = new(0x5a, 0x52, 0x46), scorch = new(0x20, 0x1c, 0x1a), timber = new(0x5c, 0x4a, 0x3a);
        var husk = new EnemyDef("Turned", EnemyKind.Corrupted, ShooterClass.None, "claws");
        foreach (PlacedModule m in Settled(x, rooms, full ? Math.Clamp(rooms.Count / 2, 2, 7) : 2))
        {
            var floor = Floor(x, m);
            if (floor.Count < 8) continue;
            int bodies = Math.Min(14, floor.Count / 14 + 2);
            foreach (Int3 c in floor.OrderBy(_ => x.R.Float()).Take(bodies))
            {
                x.Put(c, BlockType.Remains, Material.Cloth, body);
                if (full && x.R.Chance(0.3f)) x.Enemy(husk, EnemyKind.Corrupted, c, m.Index);
            }
            if (Patch(x, m, 3f) is { } p)
                foreach (Int3 c in p.Near.Where(_ => x.R.Chance(0.3f)))
                    x.Put(c, BlockType.Barricade, Material.Timber, timber, Role.Story, Dirs.Horizontal[x.R.Range(0, 4)]);
            foreach (Int3 c in floor.Where(_ => x.R.Chance(0.02f))) x.Put(c, BlockType.Scorch, Material.Ash, scorch);
        }
    }

    /// <summary>A fire in the biggest room, bedrolls round it, cans and a stash; shelves picked over; a few people, in and out.</summary>
    static void Camp(Ctx x, List<PlacedModule> rooms, bool full)
    {
        Rgb cloth = new(0x5a, 0x5a, 0x4a), ash = new(0x54, 0x50, 0x4c), can = new(0x8a, 0x8a, 0x80);
        foreach (PlacedModule m in Settled(x, rooms, full ? 2 : 1))
        {
            if (Patch(x, m, 4f) is not { } p) continue;
            x.Put(p.Mid, BlockType.Ash, Material.Ash, ash);
            x.Put(p.Mid + Int3.Up, BlockType.Scorch, Material.Ash, ash);
            foreach (Int3 c in p.Near.Where(c => c != p.Mid))
            {
                float d = c.FlatDistance(p.Mid);
                if (d < 1.5f) x.Put(c, BlockType.Scorch, Material.Ash, new Rgb(0x28, 0x24, 0x22));
                else if (d < 3.5f && x.R.Chance(0.22f)) x.Put(c, BlockType.Plate, Material.Cloth, cloth.Scale(x.R.Float(0.8f, 1.2f)), Role.Story);
                else if (x.R.Chance(0.14f)) x.Put(c, BlockType.Scatter, Material.Scrap, can);
                else if (d > 3f && x.R.Chance(0.06f)) x.Put(c, BlockType.Crate, Material.Timber, new Rgb(0x6a, 0x58, 0x44));
            }
            if (!full) continue;
            x.Inst.Lights.Add(new LightSpot { Cell = p.Mid + Int3.Up, Color = Ember, Mode = LightMode.Flicker, Energy = 1.6f, Range = 10f, Module = m.Index });
            x.Inst.Markers.Add(new Marker("campfire", p.Mid));
            // Picking the place over, not holding it: they watch you and leave you be, unless you start it.
            People(x, p.Mid, p.Near, m.Index, Mood.Wary, x.R.Between(2, 4), lookout: true);
        }
        // They've been through the side rooms.
        foreach (LootCache c in x.Inst.Caches.Where(c => !c.InVault && !c.Secret))
            if (full && !c.PickedOver && c.Items.Count > 1) { c.PickedOver = true; c.Items.RemoveRange(1, c.Items.Count - 1); }
    }

    /// <summary>
    /// People who aren't anyone's enemy: a few spots round where they settled (a bed, somewhere to talk,
    /// something to do) and the people who use them, as a town's are. They are no spawn of the place's
    /// enemies: nothing here fights you unless you start it, and wildlife keeps out of their ground as
    /// it does a town's. The wariest group in a place sets its mood.
    /// </summary>
    static void People(Ctx x, Int3 mid, List<Int3> near, int module, Mood mood, int count, bool lookout)
    {
        var free = near.Where(c => x.Inst.Blocks.Get(c) is { IsAir: true } or { Type: BlockType.Plate } && x.Nav.Standable(c) && c.FlatDistance(mid) > 1.5f).OrderBy(c => Hash.Cell(c, 211)).ToList();
        if (free.Count < 3) return;
        Survivors.Settle(x.Inst, free, module, count, lookout);
        if (mood > x.Inst.Mood) x.Inst.Mood = mood;
        x.Inst.Friendly = true;
    }

    /// <summary>A place someone lives in: bunks in a row, a stove and its pipe, water barrels, a garden in troughs. Nobody's hostile.</summary>
    static void Lived(Ctx x, List<PlacedModule> rooms, bool full)
    {
        Rgb wood = new(0x64, 0x50, 0x3c), blanket = new(0x5e, 0x5a, 0x46), metal = new(0x62, 0x60, 0x5c);
        foreach (PlacedModule m in Settled(x, rooms, full ? 2 : 1))
        {
            var floor = Floor(x, m);
            if (floor.Count < 12) continue;
            // Along whatever wall the room has, tidily.
            var wall = floor.Where(c => Dirs.Horizontal.Any(d => BlockInfo.Collides(x.Inst.Blocks.Get(c + d.Vec()).Type))).OrderBy(c => c.X).ThenBy(c => c.Z).Take(32).ToList();
            int t = 0;
            foreach (Int3 c in wall)
            {
                switch (t++ % 8)
                {
                    case 0: case 2: x.Put(c, BlockType.Bunk, Material.Timber, wood); break;
                    case 4: x.Put(c, BlockType.Tank, Material.Steel, metal); break;
                    case 5: x.Put(c, BlockType.Bench, Material.Timber, wood); break;
                    case 6: x.Put(c, BlockType.Bed, Material.Cloth, blanket); break;
                }
            }
            if (Patch(x, m, 2.5f) is { } p)
            {
                foreach (Int3 c in p.Near.Where(_ => x.R.Chance(0.5f)))
                    x.Put(c, BlockType.Plant, Material.Plant, new Rgb(0x56, 0x72, 0x3c));
                if (full) People(x, p.Mid, floor, m.Index, Mood.Peaceful, x.R.Between(2, 3), lookout: false);
            }
        }
    }

    /// <summary>Barricades at the doors, spikes and trophies, a boss and a loot pile: somebody holds this, and doesn't want you here.</summary>
    static void Held(Ctx x, List<PlacedModule> rooms, bool full)
    {
        Rgb scrap = new(0x5e, 0x4a, 0x3c), rust = new(0x70, 0x48, 0x34), bone = new(0xd0, 0xc8, 0xb4), paint = new(0x8a, 0x2c, 0x24);
        var gang = x.Lib.Roster("raiders");
        var guards = gang.Where(e => e.Kind == EnemyKind.Guard).ToList();
        foreach (PlacedModule m in Settled(x, rooms, full ? Math.Clamp(rooms.Count / 2, 2, 6) : 2))
        {
            var floor = Floor(x, m);
            if (floor.Count < 8) continue;
            // A line of barricades across the room, leaving the way through.
            var (mid, near) = Patch(x, m, 7f) ?? (floor[0], floor);
            bool alongX = x.R.Chance(0.5f);
            foreach (Int3 c in near.Where(c => alongX ? Math.Abs(c.Z - mid.Z) <= 0 : Math.Abs(c.X - mid.X) <= 0))
                x.Put(c, BlockType.Barricade, Material.Scrap, scrap, Role.Story, alongX ? Dir.North : Dir.East);
            foreach (Int3 c in near.Where(_ => x.R.Chance(0.05f))) x.Put(c, BlockType.Spike, Material.Rust, rust);
            foreach (Int3 c in near.Where(_ => x.R.Chance(0.04f))) x.Put(c, BlockType.Remains, Material.Bone, bone);
            foreach (Int3 c in near.Where(_ => x.R.Chance(0.03f))) x.Put(c, BlockType.Crate, Material.Scrap, paint);
            if (full && guards.Count > 0)
                for (int e = 0, n = x.R.Between(2, 4); e < n; e++)
                    x.Enemy(x.R.Pick(guards), EnemyKind.Guard, x.R.Pick(floor), m.Index, 2);
        }
        if (full && gang.FirstOrDefault(e => e.Kind == EnemyKind.Boss) is { } boss)
        {
            PlacedModule vault = x.Inst.Layout.Vault;
            var floor = Floor(x, vault);
            if (floor.Count > 0) x.Enemy(boss, EnemyKind.Boss, x.R.Pick(floor), vault.Index, 3);
        }
    }

    /// <summary>The old security still walking its rounds: sentries, the floors swept where they pass, a few lights still on.</summary>
    static void Machines(Ctx x, List<PlacedModule> rooms, bool full)
    {
        var frames = Survivors.Fighters(x.Lib, "oldworld");
        var guards = frames.Where(e => e.Kind == EnemyKind.Guard).ToList();
        foreach (PlacedModule m in Settled(x, rooms, full ? Math.Clamp(rooms.Count / 2, 2, 6) : 1))
        {
            // Spotless floors beside the decay.
            foreach (Int3 c in m.Cells.Where(c => x.Inst.Blocks.Get(c).Role == Role.Debris && !BlockInfo.Collides(x.Inst.Blocks.Get(c).Type)).ToList())
                x.Inst.Blocks.Set(c, Block.Air);
            var floor = Floor(x, m);
            if (floor.Count == 0) continue;
            if (full)
            {
                x.Inst.Lights.Add(new LightSpot { Cell = m.Cells.First() + Int3.Up * 2, Color = new Rgb(0xd8, 0xe8, 0xff), Mode = LightMode.Steady, Energy = 1.4f, Range = 12f, Module = m.Index });
                if (guards.Count > 0)
                    for (int e = 0, n = x.R.Between(1, 3); e < n; e++)
                        x.Enemy(x.R.Pick(guards), EnemyKind.Guard, x.R.Pick(floor), m.Index, 2);
            }
        }
    }

    /// <summary>
    /// Husk-warped: things set out in patterns nobody would make, in a room with nobody in it, and
    /// everything that can face a way turned to face the same one. Now and then one of the turned
    /// stands in the middle of it.
    /// </summary>
    static void Wrong(Ctx x, List<PlacedModule> rooms, bool full)
    {
        var husk = new EnemyDef("Turned", EnemyKind.Corrupted, ShooterClass.None, "claws");
        Rgb pale = new(0xb8, 0xb4, 0xa8), stain = new(0x2a, 0x1e, 0x2e);
        Dir one = Dirs.Horizontal[x.R.Range(0, 4)];
        foreach (PlacedModule m in Settled(x, rooms, full ? 3 : 1))
        {
            if (Patch(x, m, 5f) is not { } p) continue;
            foreach (Int3 c in p.Near)
            {
                float d = c.FlatDistance(p.Mid);
                if (Math.Abs(d - 3f) < 0.5f && (c.X + c.Z) % 2 == 0) x.Put(c, BlockType.Scatter, Material.Bone, pale);
                else if (d < 1f) x.Put(c, BlockType.Scorch, Material.Ash, stain);
            }
            foreach (Int3 c in m.Cells)
            {
                ref Block b = ref x.Inst.Blocks.At(c);
                if (Furniture.Contains(b.Type) && !x.Inst.Protected[c]) b.Facing = one;
            }
            x.Inst.Markers.Add(new Marker("warped", p.Mid));
            if (full && x.R.Chance(0.35f)) x.Enemy(husk, EnemyKind.Corrupted, p.Mid, m.Index, 2);
        }
    }

    /// <summary>Claw marks, a blood trail back to a den of torn stuff, one big thing roaming.</summary>
    static void Den(Ctx x, List<PlacedModule> rooms, bool full)
    {
        Rgb blood = new(0x4a, 0x18, 0x14), torn = new(0x5a, 0x50, 0x44);
        var fauna = x.Lib.Roster("fauna");
        foreach (PlacedModule m in Settled(x, rooms, full ? 2 : 1))
        {
            var floor = Floor(x, m);
            if (floor.Count < 8) continue;
            foreach (Int3 c in floor.Where(_ => x.R.Chance(0.05f))) x.Put(c, BlockType.Puddle, Material.Flesh, blood);
            if (Patch(x, m, 3f) is { } p)
                foreach (Int3 c in p.Near.Where(_ => x.R.Chance(0.5f))) x.Put(c, BlockType.Rubble, Material.Cloth, torn);
            if (full && fauna.Count > 0) x.Enemy(x.R.Pick(fauna), EnemyKind.Fauna, floor[x.R.Range(0, floor.Count)], m.Index, 3);
        }
    }

    /// <summary>Where two groups meet: a line of sandbags and spent shells, a score kept on the wall.</summary>
    static void Line(Ctx x, PlacedModule m)
    {
        if (Patch(x, m, 5f) is not { } p) return;
        Rgb sand = new(0x7a, 0x70, 0x5a), shell = new(0x9a, 0x84, 0x48);
        foreach (Int3 c in p.Near.Where(c => Math.Abs(c.X - p.Mid.X) == 0)) x.Put(c, BlockType.Barricade, Material.Cloth, sand, Role.Story, Dir.East);
        foreach (Int3 c in p.Near.Where(_ => x.R.Chance(0.1f))) x.Put(c, BlockType.Scatter, Material.Scrap, shell);
    }
}
