using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PoiGenerator.Gen;
using Material = PoiGenerator.Gen.Material;

namespace PoiGenerator;

public partial class PoiTest
{
    /// <summary>
    /// An object's shape comes from the object alone (the owner, 14 Nov: a tank against a shed lost its cap on that side).
    /// Every kind of yard object the sites write, one of it at a time on a fresh seed at low and high tech, is shaped alone in
    /// the open, against a wall and against another of its kind: its cells come out the same each time, and so do a
    /// building's with and without a tank or a container against its wall. A stacked container is shaped as the one under
    /// it. Nothing shows through: what the shaping opens never lets the open air reach a cell it couldn't reach plain.
    /// </summary>
    void Objects()
    {
        var rand = new Random();
        string[] notObjects = { "building", "below", "runway", "pad", "fields", "crater", "trench", "camp", "warning", "fence" };
        var kinds = _lib.Sites.Values.Where(s => !s.Space && s.Parts.Count > 0 && s.Parts[0].Kind is not ("plain" or "grand")).SelectMany(s => s.Parts)
            .Where(p => !notObjects.Contains(p.Kind)).GroupBy(p => p.Kind).Select(g => g.First()).OrderBy(p => p.Kind).ToList();
        FamilyDef fam = _lib.Families.First(f => f.Site != null && !f.Space && _lib.Sites[f.Site].Parts.Any(p => p.Kind == "tanks"));
        SitePart shed = _lib.Sites.Values.SelectMany(s => s.Parts).First(p => p.Kind == "building");
        bool roads = SiteRoads.Enabled;
        SiteRoads.Enabled = false; // (a road would route round what stands beside: only the objects are judged)
        int altered = 0, cases = 0, cells = 0, shaped = 0, diffs = 0, through = 0, failed = 0;
        var bad = new List<string>();
        try
        {
            foreach (TechLevel tech in new[] { TechLevel.Low, TechLevel.High })
            {
                foreach (SitePart kind in kinds.Append(shed))
                {
                    ulong seed = (ulong)rand.NextInt64();
                    SitePart one = Moved(kind, 0, 0, single: true);
                    var alone = Build(seed, tech, new[] { one }, null);
                    if (alone == null) { failed++; continue; }
                    var mine = alone.Value.Shaped.Layout.Objects.Keys.ToList();
                    if (mine.Count == 0) continue;
                    Int3 lo = mine.Aggregate(Int3.Min), hi = mine.Aggregate(Int3.Max);
                    var near = new List<(string, IEnumerable<SitePart>, Action<Canvas>?)>
                    {
                        // A plain wall standing against its east side, the whole height and more.
                        ("a wall", new[] { one }, (Action<Canvas>)(c =>
                        {
                            c.Begin();
                            for (int z = lo.Z - 2; z <= hi.Z + 2; z++)
                                for (int y = 0; y <= Math.Max(6, hi.Y + 2); y++)
                                    c.Set(new Int3(hi.X + 1, y, z), new Block(BlockType.Cube, Material.Concrete, new Rgb(0x8a, 0x88, 0x82), Role.Wall));
                        })),
                        ("another", new[] { one, Moved(one, one.X + hi.X - lo.X + 1, one.Z) }, null),
                    };
                    if (kind == shed)
                    {
                        // A tank and a container against the building's east wall.
                        SitePart tank = kinds.First(k => k.Kind == "tanks"), box = kinds.First(k => k.Kind == "containers");
                        near.Add(("a tank", new[] { one, Moved(tank, hi.X + 1 + tank.Int("r", 4), (lo.Z + hi.Z) / 2, single: true) }, null));
                        near.Add(("a container", new[] { one, Moved(box, hi.X + 1, (lo.Z + hi.Z) / 2 - 1, single: true) }, null));
                    }
                    foreach (var (what, parts, extra) in near)
                    {
                        var with = Build(seed, tech, parts, extra);
                        if (with == null) { failed++; continue; }
                        // (A neighbour that took cells of this one, a front step dug through its wall, changed the object: not judged.)
                        if (mine.Any(p => with.Value.Plain.Blocks.Get(p) is var w && alone.Value.Plain.Blocks.Get(p) is var o && (w.Type != o.Type || w.Material != o.Material))) { altered++; continue; }
                        cases++;
                        int d = 0;
                        foreach (Int3 p in mine)
                        {
                            Block a = alone.Value.Shaped.Blocks.Get(p), b = with.Value.Shaped.Blocks.Get(p);
                            if (a.Type != b.Type || a.Facing != b.Facing) d++;
                        }
                        cells += mine.Count;
                        shaped += mine.Count(p => alone.Value.Shaped.Blocks.Get(p).Type != alone.Value.Plain.Blocks.Get(p).Type);
                        diffs += d;
                        int t = ShowsThrough(with.Value.Plain, with.Value.Shaped);
                        through += t;
                        if ((d > 0 || t > 0) && bad.Count < 8) bad.Add($"{kind.Kind} {tech} against {what}: {d} cells shaped otherwise, {t} cells seen through");
                    }
                    // A pile: the container on top is shaped as the one under it.
                    if (kind.Kind == "containers" && mine.Any(p => p.Y >= 4) && !mine.Where(p => p.Y >= 4).Any(p => Shaping.Clear(alone.Value.Plain.Blocks.Get(p - Int3.Up * 4))))
                    {
                        var g = alone.Value.Shaped.Blocks;
                        int pd = mine.Where(p => p.Y >= 4).Count(p => mine.Contains(p - Int3.Up * 4) && !Shaping.Clear(g.Get(p - Int3.Up * 4)) && (g.Get(p).Type != g.Get(p - Int3.Up * 4).Type || g.Get(p).Facing != g.Get(p - Int3.Up * 4).Facing));
                        diffs += pd;
                        if (pd > 0 && bad.Count < 8) bad.Add($"stacked container {tech}: {pd} cells shaped otherwise than the one under it");
                    }
                }
            }
        }
        finally { SiteRoads.Enabled = roads; Shaping.Enabled = true; }
        GD.Print($"      objects: {kinds.Count} kinds and a building, {cases} cases, {cells} cells judged ({shaped} shaped alone), {diffs} shaped otherwise, {through} seen through, {failed} not built, {altered} cut into by the neighbour");
        foreach (string b in bad) GD.Print("      " + b);
        Check(cases > 0 && diffs == 0 && through == 0, "an object's shape comes from the object alone: alone, against a wall or against another, every cell is shaped the same; nothing shows through (Objects)");

        // One site of the given parts, spawned plain and shaped.
        (PoiInstance Plain, PoiInstance Shaped)? Build(ulong seed, TechLevel tech, IEnumerable<SitePart> parts, Action<Canvas>? extra)
        {
            try
            {
                var site = new SiteDef { Id = "objects", Style = _lib.Sites[fam.Site!].Style, EntranceX = -40, EntranceZ = 0, Parts = parts.ToList() };
                var canvas = new Canvas(_lib.Kits[fam.Style ?? site.Style]) { Tech = tech };
                Sites.Compose(_lib, canvas, site, fam, Rarity.Common, new Rng(seed), new LayoutHints(Dir.West, tech));
                extra?.Invoke(canvas);
                Layout layout = canvas.Finish(fam, fam.Variants[0], Rarity.Common, seed, "objects");
                layout.Tech = tech;
                PoiInstance Spawn(bool on)
                {
                    var inst = new PoiInstance { Layout = layout, Blocks = layout.Blocks.Clone(), State = PoiState.Active };
                    Shaping.Enabled = on;
                    Shaping.Apply(inst);
                    Shaping.Enabled = true;
                    return inst;
                }
                return (Spawn(false), Spawn(true));
            }
            catch (Exception e) { if (bad.Count < 8) bad.Add($"not built: {e.Message}"); return null; }
        }
    }

    /// <summary>A site part set at a cell, its ranges at their least, and (<paramref name="single"/>) one of it where it would make several.</summary>
    static SitePart Moved(SitePart p, int x, int z, bool single = false)
    {
        var m = new SitePart { Kind = p.Kind, X = x, Z = z, Rot = p.Rot };
        foreach (var (k, v) in p.Args)
        {
            int at = v.IndexOf("..", StringComparison.Ordinal);
            m.Args[k] = at < 0 ? v : v[..at]; // (a range at its least: one spawn's roll)
        }
        if (single)
            foreach (string k in new[] { "count", "rows", "cols" })
                if (m.Args.ContainsKey(k) || p.Kind is "tanks" or "containers" or "stacks" or "obelisks" or "vehicles") m.Args[k] = "1";
        return m;
    }

    /// <summary>
    /// How many open cells the air outside reaches through what the shaping opened that it didn't reach plain: the inside of
    /// a hollow, a room, a void a hull closes. Light crosses a face unless either side's block fills it (a full cube; a slope's
    /// back and foot, a hung slope's back and head, a set-back panel's back), and never across a thin wall's plate.
    /// </summary>
    static int ShowsThrough(PoiInstance plain, PoiInstance shaped)
    {
        var occ = plain.Layout.Occupied;
        Dictionary<Int3, Int3> Parents = null!;
        HashSet<Int3> Seen(BlockGrid g)
        {
            Int3 lo = g.Min, hi = g.Min + g.Size - new Int3(1, 1, 1);
            bool In(Int3 q) => q.X >= lo.X && q.Y >= lo.Y && q.Z >= lo.Z && q.X <= hi.X && q.Y <= hi.Y && q.Z <= hi.Z;
            bool Seals(Int3 q, Dir x)
            {
                Block b = g.Get(q);
                if (b.IsAir) return q.Y <= Assembler.GroundY && !occ[q];
                if (BlockInfo.IsTransparent(b.Material)) return false;
                Dir f = b.Facing;
                return b.Type switch
                {
                    BlockType.Cube or BlockType.Breakable => true,
                    BlockType.Ramp => x == Dir.Down || x == f,
                    BlockType.Corner => x == Dir.Down,
                    BlockType.InvCorner => x == Dir.Down || x == f || x == f.RotateCw(3),
                    BlockType.Chamfer => x == Dir.Up || x == f,
                    BlockType.ChamferCorner => x == Dir.Up,
                    BlockType.ChamferInvCorner => x == Dir.Up || x == f || x == f.RotateCw(3),
                    BlockType.Half => x == f,
                    _ => false,
                };
            }
            // (A thin wall's cell is two halves, one either side of its plate: side 1 toward its facing, 2 away.)
            bool Thin(Int3 q, out Dir f) { Block b = g.Get(q); f = b.Facing; return b.Type == BlockType.Partition; }
            var seen = new HashSet<(Int3, int)>();
            var par = new Dictionary<Int3, Int3>(); Int3 at = default;
            var todo = new Queue<(Int3, int)>();
            void Enter(Int3 n, Dir x, int from)
            {
                if (!In(n) || Seals(n, x.Opposite())) return;
                par.TryAdd(n, at); if (!Thin(n, out Dir f)) { if (seen.Add((n, 0))) todo.Enqueue((n, 0)); return; }
                var sides = x == f ? new[] { 2 } : x == f.Opposite() ? new[] { 1 } : from > 0 ? new[] { from } : new[] { 1, 2 };
                foreach (int s in sides) if (seen.Add((n, s))) todo.Enqueue((n, s));
            }
            for (int x = lo.X; x <= hi.X; x++) for (int z = lo.Z; z <= hi.Z; z++) Enter(new Int3(x, hi.Y, z), Dir.Down, 0);
            while (todo.Count > 0)
            {
                var (c, side) = todo.Dequeue(); at = c;
                bool thin = Thin(c, out Dir f);
                foreach (Dir d in Dirs.All)
                {
                    if (Seals(c, d) || thin && (d == f && side == 2 || d == f.Opposite() && side == 1)) continue;
                    // (Sideways between two shapes split the same way: only where their open halves meet.)
                    Dir? Opens(Int3 q, int s) { Block qb = g.Get(q); return qb.Type == BlockType.Partition ? (s == 1 ? qb.Facing : qb.Facing.Opposite()) : qb.Type is BlockType.Half or BlockType.Ramp or BlockType.Chamfer && qb.Facing.IsHorizontal() ? qb.Facing.Opposite() : null; }
                    if (!Thin(c + d.Vec(), out _) && Opens(c, side) is Dir oc && Opens(c + d.Vec(), 0) is Dir on && d != oc && d != oc.Opposite() && oc == on.Opposite()) continue;
                    // (Sideways into a thin wall: the half its own half, or a shape's open side, faces; air sees both.)
                    int hint = 0;
                    if (Thin(c + d.Vec(), out Dir g2) && d != g2 && d != g2.Opposite())
                        {
                            Block cb = g.Get(c);
                            Dir open = thin ? (side == 1 ? f : f.Opposite()) : cb.Facing.Opposite();
                            if ((thin || !cb.IsAir && cb.Facing.IsHorizontal()) && (open == g2 || open == g2.Opposite())) hint = open == g2 ? 1 : 2;
                        }
                    Enter(c + d.Vec(), d, hint);
                }
            }
            Parents = par; return seen.Select(s => s.Item1).ToHashSet();
        }
        var before = Seen(plain.Blocks);
        var news = Seen(shaped.Blocks).Where(q => !before.Contains(q) && plain.Blocks.Get(q).IsAir).ToList();
        // (Another object's room seen through its own doorway, across the gap two objects' shapes leave, is no hole: an object's
        // shapes may never open its own inside, a room or a hollow, nor a void no room owns.)
        var owner = new Dictionary<Int3, int>();
        foreach (PlacedModule m in shaped.Layout.Modules) foreach (Int3 q in m.Cells) owner[q] = m.Object;
        var nset = news.ToHashSet();
        bool Counts(Int3 q)
        {
            Int3 p = q;
            for (int k = 0; k < 4096 && nset.Contains(p) && Parents.ContainsKey(p); k++) p = Parents[p];
            return !owner.TryGetValue(q, out int o) || o == shaped.ObjectOf(p);
        }
        return news.Count(Counts);
    }
}