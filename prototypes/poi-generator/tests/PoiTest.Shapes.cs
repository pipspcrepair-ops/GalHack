using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PoiGenerator.Gen;

namespace PoiGenerator;

public partial class PoiTest
{
    /// <summary>
    /// The manner and kind a place's shape shares are judged by (data/references.txt "shapes"): high tech (and alien)
    /// chamfers, industry rounder; low and mid tech pitch the roofs of houses, sheds and farms, timber mines, and build
    /// the rest flat-roofed (a survivors' settlement's shacks and a farm's works among them).
    /// </summary>
    static string ShapeKind(PoiInstance inst)
    {
        var tags = inst.Layout.Family.Tags;
        bool Any(params string[] t) => t.Any(tags.Contains);
        if (Shaping.High(inst)) return Any("industry", "utility", "machine", "power") ? "high industry" : "high base";
        if (Any("mine", "ore")) return "low mine";
        return Any("house", "farm", "farmstead", "shed") && !Any("industry") ? "low pitched" : "low flat";
    }

    /// <summary>
    /// A place's shapes by class into <paramref name="by"/>, over the blocks it shows: its structure (and stairs) with a
    /// face to the air or to what doesn't fill its cell. Buried fill (the core of a thick wall, a slab under a slab) and
    /// the made ground (a yard's or a pad's paving, open only to the sky) are left out, as the games' own tallies leave
    /// out rock and terrain; so are lamps, as devices. Returns how many were counted.
    /// </summary>
    static long CountShapes(PoiInstance inst, long[] by)
    {
        BlockGrid g = inst.Blocks;
        var occ = inst.Layout.Occupied;
        bool space = inst.Layout.Extras.InSpace;
        bool Open(Int3 q)
        {
            Block n = g.Get(q);
            return n.IsAir ? space || q.Y > Assembler.GroundY || occ[q] : !BlockInfo.IsFullCube(n.Type);
        }
        long seen = 0;
        foreach (var (c, b) in g.Blocks())
        {
            if (!BlockInfo.IsStructure(b.Role) && b.Type != BlockType.Stair || b.Role == Role.Light || BlockInfo.IsFinish(b.Type)) continue;
            var open = Dirs.All.Where(d => Open(c + d.Vec())).ToList();
            if (open.Count == 0 || c.Y <= Assembler.GroundY && open.Count == 1 && open[0] == Dir.Up && !Enumerable.Range(1, 16).Any(k => BlockInfo.Collides(g.Get(c + Int3.Up * k).Type))) continue;
            by[(int)BlockInfo.ShapeOf(b)]++;
            seen++;
        }
        return seen;
    }

    /// <summary>
    /// Shapes are designed elements, never lone cells (the owner, 13 Nov: "in relation to adjacent tiles, a lot of it
    /// doesn't make sense"). Over fresh places of every kind at every tech level, each spawned shaped and plain from one
    /// layout and seed, what the shaping changed is judged against its neighbours: every run ends at a corner, an opening
    /// or a wall's end (never against the plain wall it could have gone on along); where two runs meet at a corner the
    /// corner piece joins them, and every corner piece has its two runs; a run keeps one profile; no panel or thin wall
    /// cuts an opening's frame; posts and beams stand on and under something. Counts are printed per kind.
    /// </summary>
    void Coherent()
    {
        var rand = new Random();
        string[] names = { "places", "shaped", "runs", "orphans", "loose ends", "corners missing", "corners alone", "profile changes", "frames cut", "unsupported" };
        var tally = new SortedDictionary<string, long[]>();
        foreach (FamilyDef f in _lib.Families.Where(f => !f.Tags.Contains("city") && !f.Space))
            foreach (TechLevel tech in f.Techs.Count > 0 ? f.Techs.Select(WorldProfile.Parse).Distinct() : Enum.GetValues<TechLevel>())
            {
                PoiInstance inst, plain;
                try
                {
                    Layout layout = PoiFactory.SpawnLayout(_lib, f, rand.Next(f.Variants.Count), f.Rarity, (ulong)rand.NextInt64(), new LayoutHints(Dirs.Horizontal[rand.Next(4)], tech), out _);
                    if (layout == null) continue;
                    ulong seed = (ulong)rand.NextInt64();
                    Shaping.Enabled = false;
                    plain = PoiFactory.Spawn(_lib, layout, PoiState.Abandoned, seed);
                    Shaping.Enabled = true;
                    inst = PoiFactory.Spawn(_lib, layout, PoiState.Abandoned, seed);
                }
                catch (Exception) { Shaping.Enabled = true; continue; }
                string kind = ShapeKind(inst);
                var t = tally.TryGetValue(kind, out var x) ? x : tally[kind] = new long[names.Length];
                t[0]++;
                AuditShapes(plain.Blocks, inst.Blocks, t);
            }
        foreach (var (kind, t) in tally)
            GD.Print($"      coherent {kind,-14} " + string.Join(", ", names.Select((n, i) => $"{t[i]} {n}")));
        Check(tally.Count > 0 && tally.Values.All(t => t.Skip(3).All(n => n == 0)),
            "every shape is part of a designed element: runs end at corners, openings or wall ends, corners are turned, profiles hold, frames are whole, posts and beams bear (Coherent)");
    }

    /// <summary>Tallies into <paramref name="t"/> (from index 1) how what the shaping changed sits against its neighbours.</summary>
    static void AuditShapes(BlockGrid before, BlockGrid g, long[] t)
    {
        var shaped = new HashSet<Int3>();
        foreach (var (c, b) in g.Blocks())
            if (b.Type != before.Get(c).Type && (BlockInfo.IsShaped(b.Type) || b.Type is BlockType.Ramp or BlockType.Corner or BlockType.InvCorner or BlockType.Pillar)) shaped.Add(c);
        t[1] += shaped.Count;
        bool Open(Int3 q) { Block b = g.Get(q); return !BlockInfo.Collides(b.Type) || BlockInfo.IsFinish(b.Type); }
        IEnumerable<Int3> Ring(Int3 c, Int3 a) =>
            from i in Enumerable.Range(-1, 3) from y in Enumerable.Range(-1, 3) where (i, y) != (0, 0) select c + a * i + Int3.Up * y;
        // A frame's cell: beside a way through the wall in its plane.
        bool InFrame(Int3 c, Dir d) => Ring(c, d.RotateCw(1).Vec()).Any(q => Shaping.Opening(g.Get(q)) || Open(q) && Open(q + d.Vec()) && Open(q - d.Vec()));
        static bool Run(BlockType ty) => ty is BlockType.Ramp or BlockType.Chamfer or BlockType.Half or BlockType.Partition;
        bool Same(Int3 q, Block b) => shaped.Contains(q) && g.Get(q).Type == b.Type && g.Get(q).Facing == b.Facing;
        // How a run along a, open to o, ends at n: 0 fine, 1 loose (the plain face goes on), 2 a corner left unturned, 3 a profile change.
        int End(Int3 n, Block b, Dir o, Dir a)
        {
            Block nb = g.Get(n);
            if (shaped.Contains(n))
            {
                if (Shaping.Turns(b.Type) is var (outer, inner) && (nb.Type == outer || nb.Type == inner)) return 0;
                return Run(nb.Type) && b.Type != BlockType.Partition && Open(n + o.Vec()) ? 3 : 0;
            }
            if (!BlockInfo.IsFullCube(nb.Type) || Shaping.Opening(nb)) return 0;
            if (b.Type == BlockType.Partition) return Open(n + b.Facing.Vec()) && Open(n - b.Facing.Vec()) && !InFrame(n, b.Facing) ? 1 : 0;
            if (!Open(n + o.Vec())) return 0;
            bool corner = o.IsHorizontal() && Open(n + a.Vec());
            if (b.Type == BlockType.Half) return corner || o.IsHorizontal() && InFrame(n, o) ? 0 : 1;
            return corner ? 2 : 1;
        }
        var bays = new Dictionary<Int3, bool>();
        foreach (Int3 c in shaped)
        {
            Block b = g.Get(c);
            if (b.Type is BlockType.Pillar or BlockType.Column or BlockType.Beam)
            {
                // A post stands on something and under something; a beam spans between supports.
                Int3 s = b.Type == BlockType.Beam ? b.Facing.Vec() : Int3.Up, end = c;
                if (Same(c - s, b)) continue;
                while (Same(end + s, b)) end += s;
                if (Open(c - s) || Open(end + s)) t[9]++;
                continue;
            }
            BlockType straight = b.Type is BlockType.Corner or BlockType.InvCorner ? BlockType.Ramp : BlockType.Chamfer;
            if (Shaping.Turns(straight) is var (po, pi) && (b.Type == po || b.Type == pi))
            {
                // Its two runs: an outer corner's along the faces from it, an inner one's from the cell behind both.
                Dir f = b.Facing, f3 = f.RotateCw(3);
                int k = b.Type == po ? 1 : -1;
                if (new[] { (c + f.Vec() * k, f3), (c + f3.Vec() * k, f) }.Any(arm => !Same(arm.Item1, b with { Type = straight, Facing = arm.Item2 }))) t[6]++;
                continue;
            }
            if (!Run(b.Type)) continue;
            if (!b.Facing.IsHorizontal())
            {
                // A ceiling's raised bay: whole from end to end one way or the other.
                foreach (Dir a in new[] { Dir.East, Dir.South })
                {
                    if (Same(c - a.Vec(), b)) continue;
                    var line = new List<Int3> { c };
                    while (Same(line[^1] + a.Vec(), b)) line.Add(line[^1] + a.Vec());
                    bool ok = End(c - a.Vec(), b, Dir.Down, a.Opposite()) == 0 && End(line[^1] + a.Vec(), b, Dir.Down, a) == 0;
                    foreach (Int3 q in line) bays[q] = bays.GetValueOrDefault(q) || ok;
                }
                continue;
            }
            Dir along = b.Facing.RotateCw(1);
            Dir side = b.Type == BlockType.Partition || Open(c + b.Facing.Opposite().Vec()) ? b.Facing.Opposite() : Dir.Down;
            if (Same(c - along.Vec(), b)) continue;
            Int3 last = c;
            int n = 1;
            while (Same(last + along.Vec(), b)) { last += along.Vec(); n++; }
            t[2]++;
            var ends = new[] { End(c - along.Vec(), b, side, along.Opposite()), End(last + along.Vec(), b, side, along) };
            foreach (int e in ends) if (e > 0) t[e == 1 ? 4 : e == 2 ? 5 : 7]++;
            if (n == 1 && ends.Any(e => e > 0)) t[3]++;
            // An inner corner left unturned: the run it meets comes off the cell beyond its end.
            if (side.IsHorizontal() && Shaping.Turns(b.Type) != null)
                foreach (Dir s in new[] { along, along.Opposite() })
                {
                    Int3 j = (s == along ? last : c) + s.Vec();
                    if (BlockInfo.IsFullCube(g.Get(j).Type) && Same(j + side.Vec(), b with { Facing = s })) t[5]++;
                }
        }
        t[4] += bays.Count(h => !h.Value);
        // No panel or thin wall cuts into an opening's frame.
        foreach (var (c, b) in g.Blocks())
            if (Shaping.Opening(b) && new[] { Dir.East.Vec(), Dir.South.Vec() }.Any(a => Ring(c, a).Any(q =>
                    shaped.Contains(q) && g.Get(q).Type is BlockType.Half or BlockType.Partition && g.Get(q).Facing.IsHorizontal())))
                t[8]++;
    }
}
