#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// The shapes a place is built with (docs/place-patterns.md section 1.5, rule P6). The last pass on a spawned place,
/// after everything that sets pieces down, turns its plain cubes into the shaped blocks its builders used, by what each
/// cube is and what is round it. Only a cube with nothing against it is shaped (by a locker, a lamp, a door, a window or
/// a sign a wall stays plain), so no piece is left standing off a wall; and a shape keeps whole the face the room or
/// the next cell sees where that face was a wall, so nothing shows through it.
/// <list type="bullet">
/// <item>Low and mid tech build with part cubes, thin walls and posts: facades in panels set back between pilasters
/// over a plinth, a string course at every floor; coffered ceilings; thin walls between rooms; posts in big halls.</item>
/// <item>High tech chamfers: every storey of a facade with a sloped foot and a chamfered head, its corners turned with
/// corner slopes; every room's walls coved at the ceiling and cut back at the floor, the inner corners turned the same;
/// round columns in big halls; fewer coffers.</item>
/// <item>Every roof's edge and wall top is sloped (a coping, or a chamfer), its corners turned.</item>
/// </list>
/// </summary>
public static class Shaping
{
    /// <summary>Whether a place is built in the high-tech manner: chamfers and corners, not panels and posts.</summary>
    public static bool High(PoiInstance inst) =>
        (inst.Tech ?? inst.Layout.Tech ?? TechLevel.Mid) == TechLevel.High || (inst.Layout.Family.Style?.StartsWith("alien", System.StringComparison.Ordinal) ?? false);

    public static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> Why = new();
    static void W(string k) => Why.AddOrUpdate(k, 1, (_, v) => v + 1);
    public static void Apply(PoiInstance inst)
    {
        Layout layout = inst.Layout;
        BlockGrid g = inst.Blocks;
        CellMask occ = layout.Occupied;
        bool space = layout.Extras.InSpace, high = High(inst);
        var roofed = new Dictionary<Int3, bool>();

        bool Air(Int3 q) { Block b = g.Get(q); return b.IsAir || BlockInfo.IsFinish(b.Type); }
        bool Rock(Int3 q) => !space && q.Y <= Assembler.GroundY && !occ[q] && g.Get(q).IsAir;
        // A room's air: inside the place and under something (a yard or a courtyard is out of doors).
        bool Room(Int3 q)
        {
            if (!Air(q) || !occ[q]) return false;
            if (q.Y <= Assembler.GroundY && !space) return true;
            if (roofed.TryGetValue(q, out bool r)) return r;
            r = false;
            for (int k = 1; k <= 16 && !r; k++) r = BlockInfo.Collides(g.Get(q + Int3.Up * k).Type);
            return roofed[q] = r;
        }
        bool Outside(Int3 q) => Air(q) && !Rock(q) && !Room(q);
        bool Solid(Int3 q) => BlockInfo.IsFullCube(g.Get(q).Type) || Rock(q);
        static bool Plain(Block b) => b.Type == BlockType.Cube && b.Role is Role.Wall or Role.Floor or Role.Roof or Role.Decor
            && !BlockInfo.IsTransparent(b.Material) && !BlockInfo.IsEmissive(b.Material);
        // Nothing against it: every cell round it is air, rock, building or litter on the floor (a lamp, a door, a pane, a piece or a sign keeps it as it is).
        bool Quiet(Int3 c)
        {
            for (int y = -1; y <= 1; y++)
                for (int z = -1; z <= 1; z++)
                    for (int x = -1; x <= 1; x++)
                    {
                        Block n = g.Get(c + new Int3(x, y, z));
                        if (n.IsAir || BlockInfo.IsFinish(n.Type)) continue;
                        if (n.Role is Role.Wall or Role.Floor or Role.Roof or Role.Decor or Role.Rock && n.Type is BlockType.Cube or BlockType.Ramp or BlockType.Corner or BlockType.InvCorner or BlockType.Lattice
                            && !BlockInfo.IsEmissive(n.Material)) continue;
                        if (n.Role == Role.Debris && n.Type is BlockType.Scatter or BlockType.Scorch or BlockType.Ash or BlockType.Puddle or BlockType.Plant or BlockType.Remains or BlockType.Rubble) continue;
                        return false;
                    }
            return true;
        }
        // The facing a corner piece takes for its raised (or, inner, its low) corner between two ways.
        static Dir CornerFacing(Dir a, Dir b) => Dirs.Horizontal.First(f => f == a && f.RotateCw(3) == b || f == b && f.RotateCw(3) == a);

        var shaped = new Dictionary<Int3, Block>();
        var coves = new Dictionary<Int3, (Dir Into, bool Top)>();
        void Put(Int3 c, Block b, BlockType t, Dir f) => shaped[c] = b with { Type = t, Facing = f };

        foreach (var (c, b) in g.Blocks().ToList())
        {
            if (!Plain(b)) { W("notplain " + b.Type + " " + b.Role); continue; }
            if (!Quiet(c)) { W("busy"); continue; }
            if (false) W("cand " + (high ? "H " : "L ") + string.Join("", Dirs.Horizontal.Select(d => Outside(c + d.Vec()) ? "o" : Room(c + d.Vec()) ? "r" : Solid(c + d.Vec()) ? "s" : "x")) + (Outside(c + Int3.Up) ? " Uo" : Room(c + Int3.Up) ? " Ur" : " Us") + (Room(c + Int3.Down) ? " Dr" : Outside(c + Int3.Down) ? " Do" : " Ds"));
            var outs = Dirs.Horizontal.Where(d => Outside(c + d.Vec())).ToList();
            var ins = Dirs.Horizontal.Where(d => Room(c + d.Vec())).ToList();
            bool roomUp = Room(c + Int3.Up), roomDown = Room(c + Int3.Down);
            bool square = outs.Count == 2 && outs[0] != outs[1].Opposite();
            if (outs.Count > 0 && (c.Y >= 0 || space))
            {
                if (ins.Any(d => outs.Contains(d.Opposite()) == false) || roomDown) { W("fac skip ins/roomDown " + ins.Count + " " + roomDown); continue; }
                if (Outside(c + Int3.Up))
                {
                    // A roof's edge or a wall's top: a coping or a chamfer sloping up from the edge, its corners turned.
                    if (ins.Count > 0 || Outside(c + Int3.Down)) continue;
                    if (outs.Count == 1) Put(c, b, BlockType.Ramp, outs[0].Opposite());
                    else if (square) Put(c, b, BlockType.Corner, CornerFacing(outs[0].Opposite(), outs[1].Opposite()));
                    continue;
                }
                if (outs.Count > 2 || outs.Count == 2 && !square) { W("fac skip outs " + outs.Count + (Outside(c+Int3.Up) ? " up" : "")); continue; }
                Dir d = outs[0];
                // A facade cell (not a top), and a floor's slab meeting the facade there: the courses of each storey.
                bool Face(Int3 q) => BlockInfo.IsFullCube(g.Get(q).Type) && Outside(q + d.Vec()) && !Outside(q + Int3.Up);
                bool Slab(Int3 q) => Solid(q - d.Vec()) && Room(q - d.Vec() + Int3.Up);
                if (Slab(c)) { W("fac slab"); continue; }
                bool foot = !Face(c + Int3.Down) || Slab(c + Int3.Down), head = !Face(c + Int3.Up) || Slab(c + Int3.Up);
                if (high)
                {
                    if (foot == head || foot && Outside(c + Int3.Down)) continue;
                    if (square) Put(c, b, foot ? BlockType.Corner : BlockType.ChamferCorner, CornerFacing(outs[0].Opposite(), outs[1].Opposite()));
                    else Put(c, b, foot ? BlockType.Ramp : BlockType.Chamfer, d.Opposite());
                }
                else if (!square && Face(c + Int3.Down) && Face(c + Int3.Up) && (d is Dir.North or Dir.South ? c.X : c.Z) % 4 != 0)
                    Put(c, b, BlockType.Half, d.Opposite()); // a panel set back between pilasters over the plinth, its back the room's wall
                else W("fac low keep sq" + square + " dn" + Face(c + Int3.Down) + " up" + Face(c + Int3.Up) + " under " + g.Get(c+Int3.Down).Type + "/" + g.Get(c+Int3.Down).Role + " over " + g.Get(c+Int3.Up).Type+ "/" + g.Get(c+Int3.Up).Role);
                continue;
            }
            if (outs.Count > 0) continue;
            if (ins.Count == 2 && ins[0] == ins[1].Opposite() && !roomUp && !roomDown && b.Role == Role.Wall)
            {
                Put(c, b, BlockType.Partition, ins[0]); // a thin wall between two rooms
                continue;
            }
            if (ins.Count == 1 && !roomUp && !roomDown)
            {
                // A room's wall with more behind it: high tech coves it at the ceiling and cuts it back at the floor; low tech
                // sets it back in panels between pilasters over a skirting.
                Dir d = ins[0];
                if (!Solid(c - d.Vec())) continue;
                bool top = !Room(c + d.Vec() + Int3.Up) && Solid(c + Int3.Up), foot = !Room(c + d.Vec() + Int3.Down) && Solid(c + Int3.Down);
                if (!high || !top && !foot)
                {
                    if (!foot && (d is Dir.North or Dir.South ? c.X : c.Z) % 4 != 0) Put(c, b, BlockType.Half, d.Opposite());
                    continue;
                }
                Put(c, b, top ? BlockType.Ramp : BlockType.Chamfer, d.Opposite());
                coves[c] = (d, top);
                continue;
            }
            if (ins.Count == 0 && (roomDown || Outside(c + Int3.Down) && c.Y >= 0) && !roomUp && !Outside(c + Int3.Up) == !Air(c + Int3.Up))
            {
                // A ceiling or a soffit: low tech lays joists across it, every third row, high tech coffers it between beams.
                static int M(int v, int m) => ((v % m) + m) % m;
                if (high ? M(c.X, 4) is 1 or 2 && M(c.Z, 4) is 1 or 2 : M(c.X, 3) != 0) Put(c, b, BlockType.Half, Dir.Up);
            }
        }

        // A room's inner corners: where two coved walls meet, the corner between them turns with them.
        foreach (var (c, (into, top)) in coves)
            foreach (Dir along in new[] { into.RotateCw(1), into.RotateCw(3) })
            {
                Int3 e = c + along.Vec();
                if (shaped.ContainsKey(e) || !Plain(g.Get(e)) || !Quiet(e)) continue;
                if (!coves.TryGetValue(e + into.Vec(), out var other) || other.Into != along.Opposite() || other.Top != top) continue;
                Put(e, g.Get(e), top ? BlockType.InvCorner : BlockType.ChamferInvCorner, CornerFacing(into.Opposite(), along));
            }

        // Posts (or round columns) on a 6 m grid in a big hall, floor to ceiling, each with clear floor all round it.
        foreach (PlacedModule m in layout.Modules)
        {
            if (m.Kind is not (ModuleKind.Room or ModuleKind.Hall)) continue;
            var floor = m.Cells.Where(c => c.Y == m.Min.Y && Room(c) && Solid(c + Int3.Down)).ToHashSet();
            if (floor.Count < 100) continue;
            foreach (Int3 f in floor)
            {
                if ((f.X - m.Min.X) % 6 != 3 || (f.Z - m.Min.Z) % 6 != 3) continue;
                int h = 0;
                while (h < 12 && Air(f + Int3.Up * h) && Enumerable.Range(-1, 3).All(x => Enumerable.Range(-1, 3).All(z => floor.Contains(f with { X = f.X + x, Z = f.Z + z }) && Air(f + new Int3(x, h, z))))) h++;
                Block over = g.Get(f + Int3.Up * h);
                if (h < 2 || !Plain(over)) continue;
                for (int y = 0; y < h; y++) shaped[f + Int3.Up * y] = over with { Type = high ? BlockType.Column : BlockType.Pillar, Role = Role.Wall, Facing = Dir.Up };
            }
        }

        foreach (var (c, b) in shaped) g.Set(c, b);
        foreach (var (c, b) in g.Blocks().ToList())
            if (Plain(b) && Dirs.All.Any(d => Air(c + d.Vec()) && !Rock(c + d.Vec())))
            {
                string pat = string.Join("", Dirs.Horizontal.Select(d => Outside(c + d.Vec()) ? "o" : Room(c + d.Vec()) ? "r" : Solid(c + d.Vec()) ? "s" : "x"));
                string kind = Room(c + Int3.Up) ? "floor" : pat.Contains('o') ? "facade" : pat.Contains('r') ? "inner" : Outside(c + Int3.Up) ? (c.Y < 0 ? "ground" : "roof") : Room(c + Int3.Down) || Outside(c + Int3.Down) ? "ceiling" : "other " + pat;
                string why = "";
                if (!Quiet(c))
                    for (int y = -1; y <= 1 && why == ""; y++) for (int z = -1; z <= 1 && why == ""; z++) for (int x = -1; x <= 1 && why == ""; x++)
                    {
                        Block n = g.Get(c + new Int3(x, y, z));
                        if (n.IsAir || BlockInfo.IsFinish(n.Type)) continue;
                        if (n.Role is Role.Wall or Role.Floor or Role.Roof or Role.Decor or Role.Rock && n.Type is BlockType.Cube or BlockType.Ramp or BlockType.Corner or BlockType.InvCorner or BlockType.Lattice && !BlockInfo.IsEmissive(n.Material)) continue;
                        if (n.Role == Role.Debris && n.Type is BlockType.Scatter or BlockType.Scorch or BlockType.Ash or BlockType.Puddle or BlockType.Plant or BlockType.Remains or BlockType.Rubble) continue;
                        if (BlockInfo.IsShaped(n.Type) || BlockInfo.IsFullCube(n.Type) && n.Role is Role.Wall or Role.Floor or Role.Roof) continue;
                        why = $" by {n.Type}/{n.Role}";
                    }
                W("left " + (high ? "H " : "L ") + kind + why);
            }
        foreach (var (c, b) in g.Blocks()) if (b.Type == BlockType.Cube && !Plain(b) && BlockInfo.IsStructure(b.Role) && Dirs.All.Any(d => Air(c + d.Vec()) && !Rock(c + d.Vec()))) W("nonplain " + (high ? "H " : "L ") + b.Role + " " + b.Material);
    }
}
