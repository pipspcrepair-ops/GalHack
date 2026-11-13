#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// The shapes a place is built with (docs/place-patterns.md section 1.5, rule P6). The last pass on a spawned place,
/// after everything that sets pieces down, turns its plain cubes into the shaped blocks its builders used. It shapes by
/// element, never by cell: it finds the wall faces, their corners, openings and storey lines, the ceilings and the
/// walls between rooms, then lays each element whole or not at all, so every shape runs, turns, frames or repeats.
/// <list type="bullet">
/// <item>Runs: a line of a wall's face at one height, from corner to corner, opening or wall end, with one profile. A
/// roof's edge is coped (a slope up from the edge) and the course under it corniced (a hung chamfer); high tech
/// chamfers every storey line too; inside, a room's walls are coved at the ceiling and, high tech, cut back at the
/// floor. Where two runs of a profile meet at a corner, in or out, the corner piece joins them.</item>
/// <item>Low and mid tech set each storey's facade back in a panel between its storey lines, its corners standing as
/// quoins and every opening keeping its whole frame; a wall between two rooms is thin, its doorways framed whole.</item>
/// <item>Ceilings under more floor, room by room across their whole span: low tech raised in bays between joists, a
/// beam under each joist wall to wall; high tech folded in V-bays the long way. Posts (round columns, high tech) on a
/// grid in big halls; mines timbered in whole sets.</item>
/// </list>
/// A cell is shaped only if it is a plain cube with nothing set against it and no top anyone stands on; an element
/// with any such cell stays plain. A shape keeps whole the face a room or the next cell sees, so nothing shows through.
/// </summary>
public static class Shaping
{
    /// <summary>Whether a place is built in the high-tech manner: chamfers and corners, not panels and posts.</summary>
    public static bool High(PoiInstance inst) =>
        (inst.Tech ?? inst.Layout.Tech ?? TechLevel.Mid) == TechLevel.High || (inst.Layout.Family.Style?.StartsWith("alien", System.StringComparison.Ordinal) ?? false);

    /// <summary>Off for a measurement against the plain cubes (tests).</summary>
    public static bool Enabled = true;

    /// <summary>The corner pieces a run's profile turns with, outer and inner (none: it doesn't turn).</summary>
    public static (BlockType Outer, BlockType Inner)? Turns(BlockType profile) => profile switch
    {
        BlockType.Ramp => (BlockType.Corner, BlockType.InvCorner),
        BlockType.Chamfer => (BlockType.ChamferCorner, BlockType.ChamferInvCorner),
        _ => null,
    };

    /// <summary>The facing a corner piece takes for its raised (or, inner, its low) corner between two ways.</summary>
    public static Dir CornerFacing(Dir a, Dir b) => Dirs.Horizontal.First(f => f == a && f.RotateCw(3) == b || f == b && f.RotateCw(3) == a);

    /// <summary>Open space: air, a floor's finish, or litter lying on it (not what hangs from or juts out of something).</summary>
    public static bool Clear(Block b) => b.IsAir || BlockInfo.IsFinish(b.Type)
        || b.Role == Role.Debris && !BlockInfo.Collides(b.Type) && b.Type is not (BlockType.Cable or BlockType.Tendril or BlockType.Vine or BlockType.Rebar); // (what hangs or juts needs its hold)

    /// <summary>A way through a wall: a window, a door, glass.</summary>
    public static bool Opening(Block b) => b.Role == Role.Window || b.Type is BlockType.Door or BlockType.SealedDoor || BlockInfo.IsTransparent(b.Material) && b.Type is BlockType.Cube or BlockType.Pane;

    /// <summary>Furniture standing on the floor: a panel or a thin wall may stand behind it, as nothing hangs on it.</summary>
    public static bool Standing(BlockType t) => t is BlockType.Crate or BlockType.Locker or BlockType.Shelf or BlockType.Table or BlockType.Meal or BlockType.Bench
        or BlockType.Bed or BlockType.Bunk or BlockType.Cache or BlockType.Barricade or BlockType.Tank or BlockType.Machine or BlockType.Statue;

    /// <summary>Set against a wall: a lamp, a sign, a ladder, a post, a stair; its wall stays whole round it, as a mount.</summary>
    public static bool Fixture(Block b) => !Clear(b) && !BlockInfo.IsFullCube(b.Type) && !Opening(b) && !Standing(b.Type)
        && !(b.Role is Role.Wall or Role.Floor or Role.Roof or Role.Decor or Role.Rock && b.Type is BlockType.Ramp or BlockType.Corner or BlockType.InvCorner or BlockType.Lattice);

    /// <summary>A wall's cube that is more than wall: a light, glass, a weak section; it is framed like an opening.</summary>
    public static bool Feature(Block b) => BlockInfo.IsFullCube(b.Type) && (b.Type != BlockType.Cube || b.Role == Role.Light || BlockInfo.IsEmissive(b.Material) || BlockInfo.IsTransparent(b.Material));

    public static void Apply(PoiInstance inst)
    {
        if (!Enabled) return;
        Layout layout = inst.Layout;
        BlockGrid g = inst.Blocks;
        CellMask occ = layout.Occupied;
        bool space = layout.Extras.InSpace, high = High(inst);
        var roofed = new Dictionary<Int3, bool>();

        bool Air(Int3 q) => Clear(g.Get(q));
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
        bool Wall(Int3 q) { Block b = g.Get(q); return BlockInfo.IsFullCube(b.Type) && !Opening(b); }
        static bool Plain(Block b) => b.Type == BlockType.Cube && b.Role is Role.Wall or Role.Floor or Role.Roof or Role.Decor
            && !BlockInfo.IsTransparent(b.Material) && !BlockInfo.IsEmissive(b.Material);
        // Nothing against it: each cell beside it, and over and under each open cell beside it, is air, rock, building or
        // litter on the floor (a lamp, a door, a pane, a piece or a sign by it keeps it as it is).
        bool Calm(Int3 q)
        {
            Block n = g.Get(q);
            return Clear(n)
                || n.Role is Role.Wall or Role.Floor or Role.Roof or Role.Decor or Role.Rock && n.Type is BlockType.Cube or BlockType.Ramp or BlockType.Corner or BlockType.InvCorner or BlockType.Lattice && !BlockInfo.IsEmissive(n.Material)
                || n.Role == Role.Debris && n.Type is BlockType.Scatter or BlockType.Scorch or BlockType.Ash or BlockType.Puddle or BlockType.Plant or BlockType.Remains or BlockType.Rubble;
        }
        bool Quiet(Int3 c) => Dirs.All.All(d => Calm(c + d.Vec()) && (!d.IsHorizontal() || !Air(c + d.Vec()) || Calm(c + d.Vec() + Int3.Up) && Calm(c + d.Vec() + Int3.Down)));
        // (A panel or a thin wall may stand behind furniture on the floor: nothing hangs on it.)
        bool Stands(Int3 q) => Standing(g.Get(q).Type) && Solid(q + Int3.Down);
        // (Nor what is behind a set-back panel, whose back face stays whole.)
        bool Behind(Int3 c, Dir? back = null) => Dirs.All.All(d => d == back || Calm(c + d.Vec()) || d.IsHorizontal() && Stands(c + d.Vec()))
            && Dirs.Horizontal.All(d => d == back || !Air(c + d.Vec()) || (Calm(c + d.Vec() + Int3.Up) || Stands(c + d.Vec() + Int3.Up)) && (Calm(c + d.Vec() + Int3.Down) || Stands(c + d.Vec() + Int3.Down)));
        static int M(int v, int m) => ((v % m) + m) % m;

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
        // A cell may take a shape: a plain cube, nothing against it, under building or rock (or a roof's or a wall's top
        // edge), and no top anyone stands on.
        var free = new Dictionary<Int3, bool>();
        bool Top(Int3 c) => (Solid(c + Int3.Up) || Outside(c + Int3.Up) && walked.Count > 0) && !walked.Contains(c + Int3.Up) && !roots.Contains(c + Int3.Up);
        bool Free(Int3 c) => free.TryGetValue(c, out bool f) ? f : free[c] = Plain(g.Get(c)) && Quiet(c) && Top(c);
        // (A panel's top is whole at its back: only what is stood on over it rules it out.)
        bool Panel(Int3 c, Dir? back = null) => Plain(g.Get(c)) && Behind(c, back) && !walked.Contains(c + Int3.Up) && !roots.Contains(c + Int3.Up);
        // An outside face's cell that can be cut back: no room under it or beside it, and not open behind (a thin wall).
        bool OutOk(Int3 c, Dir d) => !Room(c + Int3.Down) && !Outside(c - d.Vec()) && Dirs.Horizontal.All(h => h == d.Opposite() || !Room(c + h.Vec()));
        bool Frame(Int3 c, Dir d, bool thin = false) => Framed(c, d, g.Get, q => Air(q) && !Rock(q), thin);
        // Into a room: its air, or furniture standing in it.
        bool Inside(Int3 q) => Room(q) || occ[q] && Stands(q);

        var shaped = new Dictionary<Int3, Block>();
        void Put(Int3 c, BlockType t, Dir f) => shaped[c] = g.Get(c) with { Type = t, Facing = f };

        // The faces: each wall cube by the way it faces, outside or into a room (with more wall behind it), and each wall
        // between two rooms by its normal; and the ceilings over rooms.
        var outer = Dirs.Horizontal.ToDictionary(d => d, _ => new HashSet<Int3>());
        var inner = Dirs.Horizontal.ToDictionary(d => d, _ => new HashSet<Int3>());
        var between = new[] { Dir.North, Dir.East }.ToDictionary(d => d, _ => new HashSet<Int3>());
        var ceilings = new HashSet<Int3>();
        foreach (var (c, b) in g.Blocks())
        {
            if (!Wall(c)) continue;
            bool roomUp = Room(c + Int3.Up), roomDown = Room(c + Int3.Down), outs = Dirs.Horizontal.Any(h => Outside(c + h.Vec()));
            foreach (Dir d in Dirs.Horizontal)
                if (Outside(c + d.Vec())) { if (c.Y >= 0 || space) outer[d].Add(c); }
                else if (!outs && Inside(c + d.Vec()) && Solid(c - d.Vec()) && !roomUp && !roomDown) inner[d].Add(c);
            foreach (Dir d in between.Keys)
                if (!outs && !roomUp && !roomDown && b.Role == Role.Wall && Inside(c + d.Vec()) && Inside(c - d.Vec()) && !Frame(c, d, true)) between[d].Add(c);
            if (roomDown && !outs && !Dirs.Horizontal.Any(h => Room(c + h.Vec()))) ceilings.Add(c);
        }

        // Storey lines, per wall plane: a course a floor's slab meets along at least half the wall (high tech adds a line
        // every sixth course up a tall blank wall).
        static int Plane(Int3 c, Dir d) => d is Dir.North or Dir.South ? c.Z : c.X;
        var courses = new Dictionary<(Dir, int, int), (int All, int Slab)>();
        foreach (var (d, set) in outer)
            foreach (Int3 c in set)
            {
                var k = (d, Plane(c, d), c.Y);
                var (all, slab) = courses.GetValueOrDefault(k);
                courses[k] = (all + 1, slab + (Solid(c - d.Vec()) && Room(c - d.Vec() + Int3.Up) ? 1 : 0));
            }
        bool Slab(Dir d, int plane, int y) => courses.TryGetValue((d, plane, y), out var r) && r.Slab > 0 && r.Slab * 2 >= r.All;
        bool Storey(Dir d, Int3 c) => Slab(d, Plane(c, d), c.Y)
            || high && M(c.Y, 6) == 5 && !Enumerable.Range(c.Y - 2, 5).Any(y => Slab(d, Plane(c, d), y));

        // Runs: each line of a face at one height takes one profile, along its whole length or not at all.
        var runs = new List<(Dir D, List<Int3> Cells, BlockType P, bool On)>();
        var fields = Dirs.Horizontal.ToDictionary(d => d, _ => new HashSet<Int3>());
        foreach (var (d, set) in outer)
            foreach (List<Int3> line in Lines(set, d.RotateCw(1).Vec()))
            {
                void On(BlockType p) { if (line.All(c => Free(c) && OutOk(c, d))) runs.Add((d, line, p, true)); }
                if (line.All(c => Outside(c + Int3.Up))) { if (line.All(c => !Outside(c + Int3.Down))) On(BlockType.Ramp); } // a coping
                else if (line.All(c => set.Contains(c + Int3.Up) && Outside(c + Int3.Up * 2) && set.Contains(c + Int3.Down))) On(BlockType.Chamfer); // a cornice
                else if (Storey(d, line[0])) On(high ? BlockType.Chamfer : BlockType.Ramp); // a storey's band (low tech: a weathered string course)
                else
                    foreach (Int3 c in line)
                        if (!Outside(c + d.RotateCw(1).Vec()) && !Outside(c + d.RotateCw(3).Vec()) && OutOk(c, d) && !Frame(c, d)) fields[d].Add(c); // (quoins, piers and frames stand)
            }
        var panels = Dirs.Horizontal.ToDictionary(d => d, _ => new HashSet<Int3>());
        foreach (var (d, set) in inner)
            foreach (List<Int3> line in Lines(set, d.RotateCw(1).Vec()))
            {
                bool Cove(Int3 c) => Solid(c + Int3.Up) && !Inside(c + d.Vec() + Int3.Up);
                bool Kick(Int3 c) => high && Solid(c + Int3.Down) && !Inside(c + d.Vec() + Int3.Down);
                if (line.All(Cove) || line.All(Kick)) { if (line.All(c => Panel(c))) runs.Add((d, line, line.All(Cove) ? BlockType.Ramp : BlockType.Chamfer, true)); }
                else if (!line.Any(c => Cove(c) || Kick(c)))
                    foreach (Int3 c in line)
                        if (!Inside(c + d.RotateCw(1).Vec()) && !Inside(c + d.RotateCw(3).Vec()) && !Frame(c, d)) panels[d].Add(c); // (a room's outer corners and frames stand)
            }

        // (A run that is only another's corner cell is that one's, or nothing.)
        var claims = runs.SelectMany(r => r.Cells.Select(c => (c, r.P))).GroupBy(x => x.c).Where(k => k.Count() > 1).ToDictionary(k => k.Key, k => k.Select(x => x.P).Distinct().Count());
        runs.RemoveAll(r => r.Cells.Count == 1 && claims.ContainsKey(r.Cells[0]));
        // (Nor does a cell take two profiles: runs that would give it two stay plain.)
        runs.RemoveAll(r => r.Cells.Any(c => claims.GetValueOrDefault(c) > 1));
        // Corners: where two runs of a profile meet, out round a corner cell or in at the cell behind both, the corner piece
        // joins them; a run whose inner corner can't take its piece stays plain, and so does the run it meets.
        var at = new Dictionary<(Int3, Dir), int>();
        for (int i = 0; i < runs.Count; i++) foreach (Int3 c in runs[i].Cells) at[(c, runs[i].D)] = i;
        var turns = new List<(Int3 C, int A, int B, bool In, Dir F)>();
        for (int i = 0; i < runs.Count; i++)
        {
            var (d, cells, p, _) = runs[i];
            if (Turns(p) == null) continue;
            foreach (var (e, s) in new[] { (cells[0], d.RotateCw(3)), (cells[^1], d.RotateCw(1)) })
            {
                if (at.TryGetValue((e, s), out int k) && k > i && runs[k].P == p) turns.Add((e, i, k, false, CornerFacing(d.Opposite(), s.Opposite())));
                Int3 j = e + s.Vec();
                if (at.TryGetValue((j + d.Vec(), s.Opposite()), out k) && k > i && runs[k].P == p && !Dirs.Horizontal.Any(h => at.ContainsKey((j, h)))) turns.Add((j, i, k, true, CornerFacing(d.Opposite(), s)));
            }
        }
        for (bool changed = true; changed;)
        {
            changed = false;
            foreach (var t in turns.Where(t => t.In && runs[t.A].On && runs[t.B].On && !Free(t.C)))
            {
                runs[t.A] = runs[t.A] with { On = false };
                runs[t.B] = runs[t.B] with { On = false };
                changed = true;
            }
        }
        foreach (var (d, cells, p, on) in runs) if (on) foreach (Int3 c in cells) Put(c, p, d.Opposite());
        foreach (var t in turns.Where(t => runs[t.A].On && runs[t.B].On))
            Put(t.C, t.In ? Turns(runs[t.A].P)!.Value.Inner : Turns(runs[t.A].P)!.Value.Outer, t.F);

        // Panels: each storey of a facade set back whole between its storey lines, quoins and frames.
        // Each panel is cut in equal bays by piers, about every 9 m in masonry and every 3 m in timber (its posts).
        foreach (var (d, set) in fields)
            foreach (List<Int3> part in Parts(set, d.RotateCw(1).Vec(), Int3.Up))
            {
                List<Int3> panel = Level(part, d, q => Frame(q, d) || Outside(q + d.RotateCw(1).Vec()) || Outside(q + d.RotateCw(3).Vec()) || !OutOk(q, d));
                if (panel.Count == 0 || !panel.All(c => Panel(c, d.Opposite()))) continue;
                int U(Int3 c) => d is Dir.North or Dir.South ? c.X : c.Z;
                int bay = panel.Count(c => g.Get(c).Material == Material.Timber) * 2 > panel.Count ? 3 : 9;
                int lo = panel.Min(U), wide = panel.Max(U) - lo + 1, bays = (wide + bay - 1) / bay;
                // (A pier stands whole, the panel's full height with panel either side, as near its place as one can.)
                var cells = panel.ToHashSet();
                Int3 a = d.RotateCw(1).Vec();
                var rows = panel.Select(c => c.Y).Distinct().Count();
                bool Whole(int u) { var col = panel.Where(c => U(c) == u).ToList(); return col.Count == rows && col.All(c => cells.Contains(c - a) && cells.Contains(c + a)); }
                var piers = new HashSet<int>();
                for (int k = 1; k < bays; k++)
                    foreach (int u in new[] { 0, 1, -1, 2, -2 }.Select(o => lo + wide * k / bays + o))
                        if (Whole(u) && !piers.Contains(u - 1) && !piers.Contains(u) && !piers.Contains(u + 1)) { piers.Add(u); break; }
                foreach (Int3 c in panel) if (!piers.Contains(U(c))) Put(c, BlockType.Half, d.Opposite());
            }
        // A room's walls with more behind them are set back whole between the cove and the kick (or the floor), the
        // room's corners standing as piers and its doorways framed.
        foreach (var (d, set) in panels)
            foreach (List<Int3> panel in Parts(set, d.RotateCw(1).Vec(), Int3.Up).Select(p => Level(p, d, q => Frame(q, d) || Inside(q + d.RotateCw(1).Vec()) || Inside(q + d.RotateCw(3).Vec()))))
                if (panel.All(c => Panel(c, d.Opposite()))) foreach (Int3 c in panel) Put(c, BlockType.Half, d.Opposite());
        // A wall between two rooms is thin, the whole wall, its doorways framed.
        foreach (var (d, set) in between)
            foreach (List<Int3> wall in Parts(set, d.RotateCw(1).Vec(), Int3.Up).Select(p => Level(p, d, q => !set.Contains(q))))
                if (wall.All(c => Panel(c))) foreach (Int3 c in wall) Put(c, BlockType.Partition, d);

        // (No panel steps: each set-back panel, as laid, is cut level at its lowest head under plain wall.)
        foreach (Dir f in Dirs.Horizontal)
        {
            Dir open = f.Opposite();
            var set = shaped.Where(x => x.Value.Type == BlockType.Half && x.Value.Facing == f).Select(x => x.Key).ToHashSet();
            bool Head(Int3 q) => !shaped.ContainsKey(q) && Wall(q) && !Frame(q, open) && Solid(q - open.Vec())
                && Dirs.Horizontal.Where(h => h != open && h != f).All(h => !Air(q + h.Vec()) && !Stands(q + h.Vec()));
            foreach (List<Int3> panel in Parts(set, open.RotateCw(1).Vec(), Int3.Up))
            {
                var heads = panel.Where(q => !set.Contains(q + Int3.Up) && Head(q + Int3.Up)).ToList();
                if (heads.Count == 0) continue;
                int y = heads.Min(q => q.Y);
                foreach (Int3 q in panel) if (q.Y > y) shaped.Remove(q);
            }
        }

        // Ceilings under more floor, room by room across the whole span. Low tech: bays raised between joists that span the
        // short way every fourth metre, a beam under each joist from wall to wall over a room at least 3 m clear. High tech:
        // folded in V-bays the long way, two hung slopes to a fold, every fourth metre across. The bays are set out so as
        // many lamps as can hang from a joist (or a rib) do.
        var raised = new List<List<Int3>>(); // (beams, posts and timber sets, each never to cut a room off from the way in)
        bool Bay(Int3 c) => Free(c) && g.Get(c + Int3.Up).Role != Role.Wall;
        foreach (List<Int3> region in Parts(ceilings, Dir.East.Vec(), Dir.South.Vec()))
        {
            var cells = region.ToHashSet();
            int w = region.Max(c => c.X) - region.Min(c => c.X), l = region.Max(c => c.Z) - region.Min(c => c.Z);
            Dir along = high == (w >= l) ? Dir.East : Dir.South, across = along == Dir.East ? Dir.South : Dir.East;
            int At(Int3 c) => along == Dir.East ? c.Z : c.X;
            int from = region.Min(At);
            var lines = Lines(cells, along.Vec()).ToList();
            List<(List<Int3> Line, BlockType T, Dir F)> Lay(int off)
            {
                var laid = new List<(List<Int3>, BlockType, Dir)>();
                foreach (List<Int3> line in lines)
                {
                    int k = M(At(line[0]) - from - off, 4);
                    if (!high) { if (k != 0 && line.All(Bay)) laid.Add((line, BlockType.Half, Dir.Up)); continue; }
                    if (k != 1 || !line.All(Bay)) continue;
                    var pair = line.Select(c => c + across.Vec()).ToList();
                    if (pair.All(c => cells.Contains(c) && Bay(c)) && !cells.Contains(pair[0] - along.Vec()) && !cells.Contains(pair[^1] + along.Vec()))
                    {
                        laid.Add((line, BlockType.Chamfer, across.Opposite()));
                        laid.Add((pair, BlockType.Chamfer, across));
                    }
                }
                return laid;
            }
            int off = Enumerable.Range(0, 4).MaxBy(o => Lay(o).Sum(x => x.Line.Count));
            var bays = Lay(off);
            foreach (var (line, t, f) in bays) foreach (Int3 c in line) Put(c, t, f);
            if (high || bays.Count == 0) continue;
            foreach (List<Int3> joist in lines.Where(j => M(At(j[0]) - from - off, 4) == 0))
            {
                var beam = joist.Select(c => c + Int3.Down).ToList();
                if (beam.All(c => Enumerable.Range(0, 3).All(k => Air(c + Int3.Down * k)) && !lamps.Contains(c) && !NearDoor(c) && !walked.Contains(c) && !walked.Contains(c + Int3.Down) && !rootColumns.Contains((c.X, c.Z)))
                    && Solid(beam[0] - along.Vec()) && Solid(beam[^1] + along.Vec()))
                {
                    foreach (Int3 c in beam) shaped[c] = g.Get(c - Int3.Down) with { Type = BlockType.Beam, Facing = along, Role = Role.Decor };
                    raised.Add(beam);
                }
            }
        }

        // Posts (or round columns) on a 6 m grid in a big hall, floor to ceiling (or to a beam), each with 2 m of clear floor
        // all round it, and never in a way (a lift's shaft): one that would cut a room off from the way in isn't raised.
        foreach (PlacedModule m in layout.Modules)
        {
            if (m.Kind is not (ModuleKind.Room or ModuleKind.Hall)) continue;
            var floor = m.Cells.Where(c => c.Y == m.Min.Y && Room(c) && Solid(c + Int3.Down)).ToHashSet();
            if (floor.Count < 100) continue;
            foreach (Int3 f in floor)
            {
                if ((f.X - m.Min.X) % 6 != 3 || (f.Z - m.Min.Z) % 6 != 3) continue;
                int h = 0;
                while (h < 12 && Air(f + Int3.Up * h) && !shaped.ContainsKey(f + Int3.Up * h) && Enumerable.Range(-2, 5).All(x => Enumerable.Range(-2, 5).All(z => floor.Contains(f with { X = f.X + x, Z = f.Z + z }) && Air(f + new Int3(x, h, z))))) h++;
                Block over = shaped.TryGetValue(f + Int3.Up * h, out Block s) ? s : g.Get(f + Int3.Up * h);
                if (h < 2 || !(Plain(over) || over.Type == BlockType.Beam) || rootColumns.Contains((f.X, f.Z))) continue;
                var post = Enumerable.Range(0, h).Select(y => f + Int3.Up * y).ToList();
                foreach (Int3 q in post) shaped[q] = over with { Type = high ? BlockType.Column : BlockType.Pillar, Role = Role.Decor, Facing = Dir.Up };
                raised.Add(post);
            }
        }
        // A mine is timbered in whole sets: every fourth metre along a drift wide enough, a post against each wall, floor to
        // roof, and a cap beam across between them under the roof.
        if (layout.Family.Tags.Contains("mine") || layout.Family.Tags.Contains("ore"))
            foreach (PlacedModule m in layout.Modules.Where(m => m.Kind == ModuleKind.Corridor && m.Max.Y < 0))
                foreach (Int3 p in m.Cells.Where(c => c.Y == m.Min.Y))
                    foreach (Dir d in new[] { Dir.East, Dir.South })
                    {
                        // From the wall on one side across to the other, at a set's spacing along the drift.
                        if (M(d == Dir.East ? p.Z : p.X, 4) != 0 || !Solid(p - d.Vec()) || !Room(p) || !Solid(p + Int3.Down)) continue;
                        int w = 0;
                        while (w < 9 && Room(p + d.Vec() * w) && Solid(p + d.Vec() * w + Int3.Down)) w++;
                        if (w < 4 || w > 8 || !Solid(p + d.Vec() * w)) continue;
                        int h = 0;
                        while (h < 8 && Enumerable.Range(0, w).All(k => Air(p + d.Vec() * k + Int3.Up * h) && !shaped.ContainsKey(p + d.Vec() * k + Int3.Up * h))) h++;
                        if (h < 3 || Enumerable.Range(0, w).Any(k => !Solid(p + d.Vec() * k + Int3.Up * h) || rootColumns.Contains((p.X + d.Vec().X * k, p.Z + d.Vec().Z * k))
                            || k > 0 && k < w - 1 && (walked.Contains(p + d.Vec() * k + Int3.Up * (h - 1)) || walked.Contains(p + d.Vec() * k + Int3.Up * (h - 2))))) continue;
                        var timber = new Block(BlockType.Pillar, Material.Timber, new Rgb(0x6a, 0x4c, 0x30), Role.Decor, Dir.Up);
                        for (int y = 0; y < h; y++) { shaped[p + Int3.Up * y] = timber; shaped[p + d.Vec() * (w - 1) + Int3.Up * y] = timber; }
                        for (int k = 1; k < w - 1; k++) shaped[p + d.Vec() * k + Int3.Up * (h - 1)] = timber with { Type = BlockType.Beam, Facing = d };
                        raised.Add(Enumerable.Range(0, h).SelectMany(y => new[] { p + Int3.Up * y, p + d.Vec() * (w - 1) + Int3.Up * y })
                            .Concat(Enumerable.Range(1, w - 2).Select(k => p + d.Vec() * k + Int3.Up * (h - 1))).ToList());
                    }

        if (raised.Count > 0 && walked.Count > 0)
        {
            var rooms = layout.Modules.Where(m => m.Cells.Any(walked.Contains)).ToList();
            bool Cuts(IEnumerable<List<Int3>> pieces)
            {
                var was = pieces.SelectMany(p => p).Distinct().ToDictionary(q => q, g.Get);
                foreach (Int3 q in was.Keys) g.Set(q, shaped[q]);
                var reach = inst.Nav().Reachable(layout.EntranceCell);
                foreach (var (q, b) in was) g.Set(q, b);
                return rooms.Any(m => !m.Cells.Any(reach.Contains));
            }
            if (Cuts(raised))
            {
                var gone = raised.Where(p => Cuts(new[] { p })).SelectMany(p => p).ToHashSet();
                // (and a post that stood under a beam taken down goes with it)
                foreach (var post in raised.Where(p => gone.Contains(p[^1] + Int3.Up)))
                    gone.UnionWith(post);
                foreach (Int3 q in gone) shaped.Remove(q);
            }
        }


        foreach (var (c, b) in shaped) g.Set(c, b);
    }

    /// <summary>
    /// Whether <paramref name="c"/>, in a wall facing <paramref name="d"/>, frames something: beside a way through the
    /// wall in either wall it stands in (a window, a door, a doorway, a hole or the wall's free end), or round a fixture
    /// set against its face (or, a <paramref name="thin"/> wall, either face). A frame stands whole.
    /// </summary>
    public static bool Framed(Int3 c, Dir d, System.Func<Int3, Block> get, System.Func<Int3, bool> open, bool thin = false)
    {
        bool Way(Int3 q) => open(q) || Opening(get(q));
        static bool Mounted(Block b) => Fixture(b) || Opening(b);
        foreach (Dir n in new[] { d, d.RotateCw(1) })
        {
            Int3 a = n.RotateCw(1).Vec();
            for (int i = -1; i <= 1; i++) for (int y = -1; y <= 1; y++)
            {
                Int3 q = c + a * i + Int3.Up * y;
                if (q != c && (Opening(get(q)) || n == d && open(q) && Way(q + n.Vec()) && Way(q - n.Vec()))
                    || n == d && (Feature(get(q)) || Fixture(get(q)) || Mounted(get(q + d.Vec())) || get(q - d.Vec()).Type == BlockType.Rebar || thin && Mounted(get(q - d.Vec())))) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// A panel of a wall facing <paramref name="d"/> cut level: its rows from the bottom up while each spans the panel's
    /// whole width and stands on the whole row under it (where the wall above isn't <paramref name="framed"/>), so its
    /// head is one line, never a step (a gable's rake, a course of another element).
    /// </summary>
    static List<Int3> Level(List<Int3> panel, Dir d, System.Func<Int3, bool> framed)
    {
        int U(Int3 c) => d is Dir.North or Dir.South ? c.X : c.Z;
        var cells = panel.ToHashSet();
        int lo = panel.Min(U), hi = panel.Max(U), head = panel.Max(c => c.Y), top = int.MaxValue;
        foreach (var row in panel.GroupBy(c => c.Y).OrderBy(r => r.Key))
            if (row.Min(U) != lo || row.Max(U) != hi || row.Key < head && row.Any(c => !cells.Contains(c + Int3.Up) && !framed(c + Int3.Up)))
            {
                top = row.Min(U) != lo || row.Max(U) != hi ? row.Key : row.Key + 1;
                break;
            }
        return panel.Where(c => c.Y < top).ToList();
    }

    /// <summary>A set's straight lines along a step, each whole from end to end.</summary>
    public static IEnumerable<List<Int3>> Lines(HashSet<Int3> set, Int3 step)
    {
        foreach (Int3 c in set)
        {
            if (set.Contains(c - step)) continue;
            var line = new List<Int3>();
            for (Int3 q = c; set.Contains(q); q += step) line.Add(q);
            yield return line;
        }
    }

    /// <summary>A set's parts, joined along the given steps either way.</summary>
    static IEnumerable<List<Int3>> Parts(HashSet<Int3> set, params Int3[] steps)
    {
        var seen = new HashSet<Int3>();
        foreach (Int3 c in set)
        {
            if (!seen.Add(c)) continue;
            var part = new List<Int3> { c };
            for (int i = 0; i < part.Count; i++)
                foreach (Int3 s in steps)
                    foreach (Int3 n in new[] { part[i] + s, part[i] - s })
                        if (set.Contains(n) && seen.Add(n)) part.Add(n);
            yield return part;
        }
    }
}
