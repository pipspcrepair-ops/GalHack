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
/// <item>Low and mid tech build with part cubes, thin walls and posts: facades in panels set back between pilasters, a
/// string course at every floor and a cornice under the roof's edge; walls with more behind them coved and panelled;
/// joists across the ceilings with beams under them; thin walls between rooms; posts in big halls; timbered mines.</item>
/// <item>High tech chamfers: every storey of a facade with a sloped foot and a chamfered head, its corners turned with
/// corner slopes; every room's walls coved at the ceiling and cut back at the floor, the inner corners turned the same;
/// ceilings folded in V-bays between ribs; round columns in big halls.</item>
/// <item>Every roof's edge and wall top is sloped (a coping, or a chamfer), its corners turned.</item>
/// </list>
/// </summary>
public static class Shaping
{
    /// <summary>Whether a place is built in the high-tech manner: chamfers and corners, not panels and posts.</summary>
    public static bool High(PoiInstance inst) =>
        (inst.Tech ?? inst.Layout.Tech ?? TechLevel.Mid) == TechLevel.High || (inst.Layout.Family.Style?.StartsWith("alien", System.StringComparison.Ordinal) ?? false);

    /// <summary>Off for a measurement against the plain cubes (tests).</summary>
    public static bool Enabled = true;

    public static void Apply(PoiInstance inst)
    {
        if (!Enabled) return;
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
        // Nothing against it: each cell beside it, and over and under each open cell beside it, is air, rock, building or
        // litter on the floor (a lamp, a door, a pane, a piece or a sign by it keeps it as it is).
        bool Calm(Int3 q)
        {
            Block n = g.Get(q);
            return n.IsAir || BlockInfo.IsFinish(n.Type)
                || n.Role is Role.Wall or Role.Floor or Role.Roof or Role.Decor or Role.Rock && n.Type is BlockType.Cube or BlockType.Ramp or BlockType.Corner or BlockType.InvCorner or BlockType.Lattice && !BlockInfo.IsEmissive(n.Material)
                || n.Role == Role.Debris && n.Type is BlockType.Scatter or BlockType.Scorch or BlockType.Ash or BlockType.Puddle or BlockType.Plant or BlockType.Remains or BlockType.Rubble;
        }
        bool Quiet(Int3 c) => Dirs.All.All(d => Calm(c + d.Vec()) && (!d.IsHorizontal() || !Air(c + d.Vec()) || Calm(c + d.Vec() + Int3.Up) && Calm(c + d.Vec() + Int3.Down)));
        // The facing a corner piece takes for its raised (or, inner, its low) corner between two ways.
        static int M(int v, int m) => ((v % m) + m) % m;
        static Dir CornerFacing(Dir a, Dir b) => Dirs.Horizontal.First(f => f == a && f.RotateCw(3) == b || f == b && f.RotateCw(3) == a);

        var lamps = inst.Lights.Select(l => l.Cell).Concat(inst.Growth.Select(s => s.Cell)).ToHashSet(); // (and where trees may root)
        var roots = inst.Growth.Select(s => s.Cell).ToHashSet();
        // (Over a root the sky stays open: nothing is added in its column.)
        var rootColumns = roots.Select(r => (r.X, r.Z)).ToHashSet();
        // Where anyone walks: a top on the way (a tank's rim to its ladder, a roof to its hatch) is never shaped, nor any top
        // at all where the way in can't be stood on to find them.
        NavGrid nav = inst.Nav();
        var walked = nav.Standable(layout.EntranceCell) ? nav.Reachable(layout.EntranceCell) : new HashSet<Int3>();
        bool NearDoor(Int3 q)
        {
            for (int y = -3; y <= 0; y++) for (int z = -2; z <= 2; z++) for (int x = -2; x <= 2; x++)
                if (g.Get(q + new Int3(x, y, z)).Type is BlockType.Door or BlockType.SealedDoor) return true;
            return false;
        }
        var shaped = new Dictionary<Int3, Block>();
        var coves = new Dictionary<Int3, (Dir Into, bool Top)>();
        void Put(Int3 c, Block b, BlockType t, Dir f) => shaped[c] = b with { Type = t, Facing = f };

        foreach (var (c, b) in g.Blocks().ToList())
        {
            if (!Plain(b) || !Dirs.All.Any(d => Air(c + d.Vec()) && !Rock(c + d.Vec())) || !Quiet(c)) continue;
            var outs = Dirs.Horizontal.Where(d => Outside(c + d.Vec())).ToList();
            var ins = Dirs.Horizontal.Where(d => Room(c + d.Vec())).ToList();
            bool roomUp = Room(c + Int3.Up), roomDown = Room(c + Int3.Down);
            bool square = outs.Count == 2 && outs[0] != outs[1].Opposite();
            // Only under building or rock, or a roof's or a wall's top edge: a top anything stands on, or can, stays whole.
            if (!Solid(c + Int3.Up) && !(outs.Count > 0 && Outside(c + Int3.Up) && walked.Count > 0) || walked.Contains(c + Int3.Up) || roots.Contains(c + Int3.Up)) continue;
            if (outs.Count > 0 && (c.Y >= 0 || space))
            {
                if (ins.Any(d => outs.Contains(d.Opposite()) == false) || roomDown) continue;
                if (Outside(c + Int3.Up))
                {
                    // A roof's edge or a wall's top: a coping or a chamfer sloping up from the edge, its corners turned.
                    if (ins.Count > 0 || Outside(c + Int3.Down)) continue;
                    if (outs.Count == 1) Put(c, b, BlockType.Ramp, outs[0].Opposite());
                    else if (square) Put(c, b, BlockType.Corner, CornerFacing(outs[0].Opposite(), outs[1].Opposite()));
                    continue;
                }
                if (outs.Count > 2 || outs.Count == 2 && !square) continue;
                Dir d = outs[0];
                // A facade cell (not a top), and a floor's slab meeting the facade there: the courses of each storey.
                bool Face(Int3 q) => BlockInfo.IsFullCube(g.Get(q).Type) && Outside(q + d.Vec()) && !Outside(q + Int3.Up);
                // (A storey's slab where one meets the facade, and every sixth course up a tall blank wall.)
                bool Slab(Int3 q) => Solid(q - d.Vec()) && Room(q - d.Vec() + Int3.Up) || high && M(q.Y, 6) == 5;
                if (Slab(c)) continue;
                bool foot = !Face(c + Int3.Down) || Slab(c + Int3.Down), head = !Face(c + Int3.Up) || Slab(c + Int3.Up);
                if (high)
                {
                    if (foot == head || foot && Outside(c + Int3.Down)) continue;
                    if (square) Put(c, b, foot ? BlockType.Corner : BlockType.ChamferCorner, CornerFacing(outs[0].Opposite(), outs[1].Opposite()));
                    else Put(c, b, foot ? BlockType.Ramp : BlockType.Chamfer, d.Opposite());
                }
                else if (!Face(c + Int3.Up) && Face(c + Int3.Down)) // a cornice under the roof's edge
                    Put(c, b, square ? BlockType.ChamferCorner : BlockType.Chamfer, square ? CornerFacing(outs[0].Opposite(), outs[1].Opposite()) : d.Opposite());
                else if (!square && M(d is Dir.North or Dir.South ? c.X : c.Z, 8) != 0)
                    Put(c, b, BlockType.Half, d.Opposite()); // a panel set back between pilasters, its back the room's wall
                continue;
            }
            if (outs.Count > 0) continue;
            if ((roomDown || ins.Count == 0 && Outside(c + Int3.Down) && c.Y >= 0) && g.Get(c + Int3.Up).Role != Role.Wall)
            {
                // A ceiling or a soffit (not under a wall: a wall stands on a whole block): low tech lays joists across it,
                // every fourth row; high tech folds it in V-bays between ribs, two hung slopes to a bay.
                if (high && M(c.X, 4) != 0 && M(c.Z, 4) is 1 or 2) Put(c, b, BlockType.Chamfer, M(c.Z, 4) == 1 ? Dir.North : Dir.South); // a folded bay between ribs
                else if (!high && M(c.X, 4) != 0) Put(c, b, BlockType.Half, Dir.Up);
                // Under a low-tech room's joist a beam shows, over a room at least 3 m clear, clear of its lamps and of the head of any door near it.
                else if (!high && roomDown && Enumerable.Range(1, 3).All(k => Air(c + Int3.Down * k)) && !lamps.Contains(c + Int3.Down) && !NearDoor(c + Int3.Down)
                    && !walked.Contains(c + Int3.Down) && !walked.Contains(c + Int3.Down * 2) && !rootColumns.Contains((c.X, c.Z)))
                    shaped[c + Int3.Down] = b with { Type = BlockType.Beam, Facing = Dir.North, Role = Role.Decor };
                continue;
            }
            if (ins.Count == 2 && ins[0] == ins[1].Opposite() && !roomUp && !roomDown && b.Role == Role.Wall)
            {
                Put(c, b, BlockType.Partition, ins[0]); // a thin wall between two rooms
                continue;
            }
            if (ins.Count == 1 && !roomUp && !roomDown)
            {
                // A room's wall with more behind it: coved at the ceiling, high tech cutting it back at the floor too, and
                // set back in panels between pilasters between.
                Dir d = ins[0];
                if (!Solid(c - d.Vec())) continue;
                bool top = !Room(c + d.Vec() + Int3.Up) && Solid(c + Int3.Up), foot = !Room(c + d.Vec() + Int3.Down) && Solid(c + Int3.Down) && high;
                if (top == foot)
                {
                    if (M(d is Dir.North or Dir.South ? c.X : c.Z, 8) != 0) Put(c, b, BlockType.Half, d.Opposite());
                    continue;
                }
                Put(c, b, top ? BlockType.Ramp : BlockType.Chamfer, d.Opposite());
                coves[c] = (d, top);
                continue;
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

        // Posts (or round columns) on a 6 m grid in a big hall, floor to ceiling, each with 2 m of clear floor all round it.
        foreach (PlacedModule m in layout.Modules)
        {
            if (m.Kind is not (ModuleKind.Room or ModuleKind.Hall)) continue;
            var floor = m.Cells.Where(c => c.Y == m.Min.Y && Room(c) && Solid(c + Int3.Down)).ToHashSet();
            if (floor.Count < 100) continue;
            foreach (Int3 f in floor)
            {
                if ((f.X - m.Min.X) % 6 != 3 || (f.Z - m.Min.Z) % 6 != 3) continue;
                int h = 0;
                while (h < 12 && Air(f + Int3.Up * h) && Enumerable.Range(-2, 5).All(x => Enumerable.Range(-2, 5).All(z => floor.Contains(f with { X = f.X + x, Z = f.Z + z }) && Air(f + new Int3(x, h, z))))) h++;
                Block over = g.Get(f + Int3.Up * h);
                if (h < 2 || !Plain(over) || rootColumns.Contains((f.X, f.Z))) continue;
                for (int y = 0; y < h; y++) shaped[f + Int3.Up * y] = over with { Type = high ? BlockType.Column : BlockType.Pillar, Role = Role.Decor, Facing = Dir.Up };
            }
        }

        // A mine is timbered: every third metre along a drift wide enough, a post against each wall, floor to roof, and a
        // cap beam across between them under the roof.
        if (layout.Family.Tags.Contains("mine") || layout.Family.Tags.Contains("ore"))
            foreach (PlacedModule m in layout.Modules.Where(m => m.Kind == ModuleKind.Corridor && m.Max.Y < 0))
                foreach (Int3 p in m.Cells.Where(c => c.Y == m.Min.Y))
                    foreach (Dir d in new[] { Dir.East, Dir.South })
                    {
                        // From the wall on one side across to the other, at a set's spacing along the drift.
                        if (M(d == Dir.East ? p.Z : p.X, 3) != 0 || !Solid(p - d.Vec()) || !Room(p) || !Solid(p + Int3.Down)) continue;
                        int w = 0;
                        while (w < 9 && Room(p + d.Vec() * w) && Solid(p + d.Vec() * w + Int3.Down)) w++;
                        if (w < 4 || w > 8 || !Solid(p + d.Vec() * w)) continue;
                        int h = 0;
                        while (h < 8 && Enumerable.Range(0, w).All(k => Air(p + d.Vec() * k + Int3.Up * h) && !shaped.ContainsKey(p + d.Vec() * k + Int3.Up * h))) h++;
                        if (h < 3 || Enumerable.Range(0, w).Any(k => !Solid(p + d.Vec() * k + Int3.Up * h) || rootColumns.Contains((p.X + d.Vec().X * k, p.Z + d.Vec().Z * k)))) continue;
                        var timber = new Block(BlockType.Pillar, Material.Timber, new Rgb(0x6a, 0x4c, 0x30), Role.Decor, Dir.Up);
                        for (int y = 0; y < h; y++) { shaped[p + Int3.Up * y] = timber; shaped[p + d.Vec() * (w - 1) + Int3.Up * y] = timber; }
                        for (int k = 1; k < w - 1; k++)
                            if (!walked.Contains(p + d.Vec() * k + Int3.Up * (h - 1)) && !walked.Contains(p + d.Vec() * k + Int3.Up * (h - 2)))
                                shaped[p + d.Vec() * k + Int3.Up * (h - 1)] = timber with { Type = BlockType.Beam, Facing = d };
                    }

        foreach (var (c, b) in shaped) g.Set(c, b);
    }
}
