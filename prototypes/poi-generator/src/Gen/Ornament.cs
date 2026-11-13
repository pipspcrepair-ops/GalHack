#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// Ornament (docs/place-patterns.md section 1.5): after the shaping, purely decorative blocks laid with the same
/// discipline, each a whole element set out from the building's own lines, never a cell scattered alone.
/// <list type="bullet">
/// <item>Fins up the outside walls on the piers and quoins, at the bay spacing, from above head height to the eaves, a
/// finial on each where the builders top them; masts at the corners of each object's highest roof; a hood over each
/// way in; pipes along the walls of a place with machines, corner to corner.</item>
/// <item>Inside: frames across a hall at the bay spacing, a rib under the ceiling from wall to wall, its pilasters down
/// the walls to head height.</item>
/// <item>Decay: bars out of every torn concrete edge, every other cell along it, the way the bars run.</item>
/// </list>
/// Each builder (its world, tech level and faction or culture) rolls its own small vocabulary, spacing and proportions,
/// so one world's places look related and two worlds' differ. An element that can't be laid whole is left out; nothing
/// goes where a body walks or stands its head, by a door, window, stair or ladder, or on a lamp; everything is held by
/// the wall, roof or edge it stands from.
/// </summary>
public static class Ornament
{
    /// <summary>Off for a measurement against the place without it (tests, shots).</summary>
    public static bool Enabled = true;

    /// <summary>One element as laid: its kind, its cells, and where along its line each repeat stands (for spacing).</summary>
    public sealed record Piece(string Kind, List<Int3> Cells, List<int> At);

    /// <summary>A builder's vocabulary: which elements, how far apart, how tall.</summary>
    public sealed record Vocab(bool Fins, BlockType Fin, int Bay, bool Masts, int Mast, bool Finial, bool Hoods, bool Frames, bool Pipes, int PipeAt)
    {
        public string Sig => $"{(Fins ? "fins " + Fin + " " : "")}bay {Bay}{(Masts ? $" masts {Mast}" : "")}{(Finial ? " finials" : "")}{(Hoods ? " hoods" : "")}{(Frames ? " frames" : "")}{(Pipes ? $" pipes {PipeAt}" : "")}";
    }

    /// <summary>The builders of a place: its world, its tech level, its faction and its culture (alien or not).</summary>
    public static ulong Builder(PoiInstance inst)
    {
        ulong world = 0;
        if (inst.World is { } w)
        {
            world = Hash.Combine((ulong)w.Before, (ulong)w.Now, (ulong)w.Trade);
            if (w.Paint is { } p)
                foreach (Rgb c in new[] { p.Main, p.Accent, p.Roof, p.Cloth }) world = Hash.Combine(world, (ulong)(c.R | c.G << 8 | c.B << 16));
        }
        TechLevel tech = inst.Tech ?? inst.Layout.Tech ?? TechLevel.Mid;
        bool alien = inst.Layout.Family.Style?.StartsWith("alien", System.StringComparison.Ordinal) ?? false;
        return Hash.Combine(world, (ulong)tech + 1, Hash.Combine((ulong)inst.Layout.Family.Faction + 7, alien ? 1UL : 2UL));
    }

    /// <summary>The vocabulary a builder rolls, the same for every place it builds.</summary>
    public static Vocab For(PoiInstance inst)
    {
        var r = new Rng(Hash.Combine(Builder(inst), 0x0A11A3UL));
        TechLevel tech = inst.Tech ?? inst.Layout.Tech ?? TechLevel.Mid;
        bool high = Shaping.High(inst);
        bool fins = r.Chance(0.75f), masts = r.Chance(high ? 0.7f : 0.5f), hoods = r.Chance(0.6f), frames = r.Chance(0.6f);
        if (new[] { fins, masts, hoods, frames }.Count(x => x) < 2) fins = masts = true;
        BlockType fin = high ? (r.Chance(0.5f) ? BlockType.Partition : BlockType.Rib) : tech == TechLevel.Low ? BlockType.Pillar : r.Chance(0.5f) ? BlockType.Rib : BlockType.Pillar;
        return new Vocab(fins, fin, high ? r.Between(4, 8) : r.Between(3, 6), masts, r.Between(1, high ? 4 : 2), r.Chance(0.7f), hoods, frames, r.Chance(0.8f), r.Between(3, 5));
    }

    public static void Apply(PoiInstance inst)
    {
        inst.Ornaments.Clear();
        if (!Enabled) return;
        Layout layout = inst.Layout;
        BlockGrid g = inst.Blocks;
        CellMask occ = layout.Occupied;
        bool space = layout.Extras.InSpace;
        Vocab v = For(inst);

        // (It follows the shaping, whose survey of the place it reads: no shaping, no ornament.)
        if (inst.Survey is not { } survey) return;
        // Where anyone walks now: as the shaping found it, and on from any slope it laid that a walker can step onto (a
        // coping's slope can be walked where its cube couldn't be; nothing else it lays opens a way).
        var walked = new HashSet<Int3>(survey.Walked);
        NavGrid nav = inst.Nav();
        var step = new List<Int3>();
        var on = new Queue<Int3>();
        foreach (Int3 s in survey.Slopes)
        {
            if (walked.Contains(s) || !nav.Standable(s)) continue;
            nav.NeighboursInto(s, step);
            if (step.Any(walked.Contains) && walked.Add(s)) on.Enqueue(s);
        }
        while (on.Count > 0)
        {
            nav.NeighboursInto(on.Dequeue(), step);
            foreach (Int3 n in step) if (walked.Add(n)) on.Enqueue(n);
        }
        var lamps = inst.Lights.Select(l => l.Cell).ToHashSet();
        var roofed = new Dictionary<Int3, bool>();
        bool Rock(Int3 q) => !space && q.Y <= Assembler.GroundY && !occ[q] && g.Get(q).IsAir;
        bool Roofed(Int3 q)
        {
            if (roofed.TryGetValue(q, out bool r)) return r;
            int k = 1;
            for (; k <= 16 && !r; k++) r = BlockInfo.Collides(g.Get(q + Int3.Up * k).Type);
            if (r) for (int j = 1; j < k - 1; j++) roofed[q + Int3.Up * j] = true; // (the air between is under the same roof)
            return roofed[q] = r;
        }
        // Out of doors: open air not in a room (below ground, the place's air is all rooms and the rest rock).
        bool Outside(Int3 q) => (space || q.Y > Assembler.GroundY) && g.Get(q).IsAir && !(occ[q] && Roofed(q));
        static bool Way(Block b) => b.Type is BlockType.Door or BlockType.SealedDoor or BlockType.Ladder or BlockType.Stair or BlockType.Pane
            || b.Role is Role.Window or Role.Door or Role.Traverse || BlockInfo.IsTransparent(b.Material);
        // A cell an ornament may take: open air, no one's feet, body or (but for a beam under its cell's top) head in it,
        // no lamp, and nothing anyone goes through or climbs beside it.
        var mine = new HashSet<Int3>();
        bool Open(Int3 q, BlockType t) => g.Get(q).IsAir && !Rock(q) && !mine.Contains(q) && !lamps.Contains(q)
            && !walked.Contains(q) && !walked.Contains(q + Int3.Down) && (t == BlockType.Beam || !walked.Contains(q + Int3.Down * 2))
            && !Dirs.All.Any(d => Way(g.Get(q + d.Vec())))
            && !BlockInfo.IsSlope(g.Get(q + Int3.Down).Type) && !BlockInfo.IsSlope(g.Get(q + Int3.Down * 2).Type); // (nor over a slope one might walk up since)
        Block Made(Int3 from, BlockType t, Dir f) { Block b = g.Get(from); return b with { Type = t, Facing = f, Role = Role.Decor, Color = b.Color.Scale(0.88f), Variant = 0 }; }
        var lay = new List<(Int3 Cell, Block B)>();
        void Lay(string kind, List<(Int3, Block)> cells, List<int> at)
        {
            foreach (var (c, _) in cells) mine.Add(c);
            lay.AddRange(cells);
            inst.Ornaments.Add(new Piece(kind, cells.Select(x => x.Item1).ToList(), at));
        }

        // What the shaping surveyed: the outside faces (those still plain wall) by the way they look, their plane and their
        // object; the tops open to the sky; and the doors.
        var planes = new Dictionary<(Dir D, int Plane, int Obj), HashSet<Int3>>();
        foreach (var (d, set) in survey.Outer)
            foreach (Int3 c in set)
                if (g.Get(c) is { Type: BlockType.Cube, Role: Role.Wall } b && !BlockInfo.IsTransparent(b.Material) && g.Get(c + d.Vec()).IsAir)
                {
                    var k = (d, d is Dir.North or Dir.South ? c.Z : c.X, inst.ObjectOf(c));
                    (planes.TryGetValue(k, out var s) ? s : planes[k] = new HashSet<Int3>()).Add(c);
                }
        var tops = survey.Tops.Where(c => g.Get(c) is { Type: BlockType.Cube, Role: Role.Roof or Role.Wall }).ToList();
        var doors = new HashSet<Int3>();
        foreach (Int3 c in layout.Doorways.SelectMany(w => w.Cells).Concat(layout.ExitCells))
            for (Int3 q = c; g.Get(q).Type == BlockType.Door && doors.Add(q); q += Int3.Up) { }
        bool industry = layout.Family.Tags.Any(t => t is "industry" or "utility" or "power" or "machine" or "factory");

        foreach (var ((d, _, _), set) in planes)
        {
            Dir a = d.RotateCw(1);
            int U(Int3 c) => d is Dir.North or Dir.South ? c.X : c.Z;
            // Fins: each a whole column on a pier or quoin, from its wall's head down to head height; whole sets at a regular spacing.
            if (v.Fins && set.Count >= 6)
            {
                var heads = new Dictionary<int, Int3>();
                foreach (Int3 c in set) if (!heads.TryGetValue(U(c), out Int3 h) || c.Y > h.Y) heads[U(c)] = c;
                // (Each column judged only when a set asks for it, once.)
                var columns = new Dictionary<int, List<(Int3, Block)>?>();
                List<(Int3, Block)>? Column(int u)
                {
                    if (columns.TryGetValue(u, out var known)) return known;
                    return columns[u] = Judge(u);
                }
                List<(Int3, Block)>? Judge(int u)
                {
                    if (!heads.TryGetValue(u, out Int3 top) || BlockInfo.IsFullCube(g.Get(top + Int3.Up).Type)) return null; // (under more wall: not this face's head)
                    var cells = new List<(Int3, Block)>();
                    Int3 c = top;
                    for (; set.Contains(c) && Open(c + d.Vec(), v.Fin); c += Int3.Down) cells.Add((c + d.Vec(), Made(c, v.Fin, v.Fin == BlockType.Partition ? a : Dir.Up)));
                    Int3 stop = c + d.Vec();
                    // (It stops at head height, the wall's foot or the ground: never short of a window, a door or a lamp.)
                    bool whole = !set.Contains(c) || walked.Contains(stop) || walked.Contains(stop + Int3.Down) || walked.Contains(stop + Int3.Down * 2);
                    if (!whole || cells.Count < 3) return null;
                    if (v.Finial && Open(top + d.Vec() + Int3.Up, BlockType.Spike)) cells.Insert(0, (top + d.Vec() + Int3.Up, Made(top, BlockType.Spike, Dir.Up)));
                    return cells;
                }
                {
                    int lo = heads.Keys.Min(), hi = heads.Keys.Max();
                    foreach (int s in new[] { v.Bay, v.Bay * 2, v.Bay * 3 })
                    {
                        var offs = Enumerable.Range(0, s).OrderBy(o => System.Math.Abs(o - (hi - lo) % s / 2)).ToList();
                        var pick = offs.Select(o => Enumerable.Range(0, (hi - lo - o) / s + 1).Select(k => lo + o + k * s).ToList())
                            .FirstOrDefault(us => us.Count >= 2 && us.All(u => Column(u) != null));
                        if (pick == null) continue;
                        Lay("fins", pick.SelectMany(u => columns[u]!).ToList(), pick);
                        break;
                    }
                }
            }
            // Pipes: along the wall at one height, the face's whole length at that height, where the place has machines.
            if (v.Pipes && industry)
            {
                int y = set.Min(c => c.Y) + v.PipeAt;
                var row = set.Where(c => c.Y == y).OrderBy(U).ToList();
                if (row.Count >= 4 && U(row[^1]) - U(row[0]) == row.Count - 1 && row.All(c => Open(c + d.Vec(), BlockType.Pipe)))
                    Lay("pipe", row.Select(c => (c + d.Vec(), new Block(BlockType.Pipe, Material.Steel, new Rgb(0x6a, 0x6e, 0x70), Role.Decor, a))).ToList(), new List<int>());
            }
        }

        // Masts: at every outer corner of each object's highest flat roof, a mast (with its finial), all or none.
        if (v.Masts)
            foreach (var obj in tops.GroupBy(inst.ObjectOf))
            {
                int top = obj.Max(c => c.Y);
                var flat = obj.Where(c => c.Y == top).ToHashSet();
                var corners = flat.Where(c => Dirs.Horizontal.Any(h => !flat.Contains(c + h.Vec()) && !flat.Contains(c + h.RotateCw(1).Vec())
                    && flat.Contains(c - h.Vec()) && flat.Contains(c - h.RotateCw(1).Vec()))).ToList();
                if (corners.Count < 2 || corners.Count > 12) continue;
                var cells = new List<(Int3, Block)>();
                foreach (Int3 c in corners)
                {
                    for (int k = 1; k <= v.Mast; k++) cells.Add((c + Int3.Up * k, Made(c, BlockType.Pillar, Dir.Up)));
                    if (v.Finial) cells.Add((c + Int3.Up * (v.Mast + 1), Made(c, BlockType.Spike, Dir.Up)));
                }
                if (cells.All(x => Open(x.Item1, x.Item2.Type))) Lay("masts", cells, new List<int>());
            }

        // Hoods: over each way in from outside, a beam along the wall a cell out, a cell past the door either side.
        if (v.Hoods)
        {
            var seen = new HashSet<Int3>();
            foreach (Int3 start in doors)
            {
                if (!seen.Add(start)) continue;
                var group = new List<Int3> { start };
                for (int i = 0; i < group.Count; i++)
                    foreach (Dir h in Dirs.All)
                        if (doors.Contains(group[i] + h.Vec()) && seen.Add(group[i] + h.Vec())) group.Add(group[i] + h.Vec());
                int ty = group.Max(c => c.Y);
                foreach (Dir d in Dirs.Horizontal)
                {
                    var head = group.Where(c => c.Y == ty).ToList();
                    if (!head.All(c => Outside(c + d.Vec()) && !Outside(c - d.Vec()))) continue;
                    Dir a = d.RotateCw(1);
                    var span = head.SelectMany(c => new[] { c - a.Vec(), c, c + a.Vec() }).Distinct().Select(c => c + Int3.Up).ToList();
                    if (span.All(c => BlockInfo.IsFullCube(g.Get(c).Type) && Open(c + d.Vec(), BlockType.Beam)))
                        Lay("hood", span.Select(c => (c + d.Vec(), Made(c, BlockType.Beam, a))).ToList(), new List<int>());
                    break;
                }
            }
        }

        // Frames: across a hall at the bay spacing, a rib under the ceiling from wall to wall and its pilasters down the
        // walls to head height; every frame of the hall or none.
        if (v.Frames)
            foreach (PlacedModule m in layout.Modules.Where(m => m.Kind is ModuleKind.Hall or ModuleKind.Room && m.Max.Y - m.Min.Y >= 3 && (m.Max.X - m.Min.X + 1) * (m.Max.Z - m.Min.Z + 1) >= 60))
            {
                int y = m.Max.Y;
                var cells = m.Cells.Where(c => c.Y == y).ToList();
                if (m.Cells.Count(c => c.Y == m.Min.Y) < 60) continue;
                bool alongX = m.Max.X - m.Min.X >= m.Max.Z - m.Min.Z;
                Dir across = alongX ? Dir.South : Dir.East;
                int lo = alongX ? m.Min.X : m.Min.Z, hi = alongX ? m.Max.X : m.Max.Z;
                var rows = cells.GroupBy(c => alongX ? c.X : c.Z).ToDictionary(r => r.Key, r => r.OrderBy(c => alongX ? c.Z : c.X).ToList());
                List<(Int3, Block)>? Frame(int p)
                {
                    if (!rows.TryGetValue(p, out var rib)) return null;
                    if (rib.Count < 3 || (alongX ? rib[^1].Z - rib[0].Z : rib[^1].X - rib[0].X) != rib.Count - 1) return null;
                    Int3 w0 = rib[0] - across.Vec(), w1 = rib[^1] + across.Vec();
                    if (!BlockInfo.IsFullCube(g.Get(w0).Type) || !BlockInfo.IsFullCube(g.Get(w1).Type) || !BlockInfo.IsFullCube(g.Get(rib[0] + Int3.Up).Type)) return null;
                    var f = rib.Select(c => (c, Made(c + Int3.Up, BlockType.Beam, across))).ToList();
                    foreach (Int3 end in new[] { rib[0], rib[^1] })
                        for (Int3 q = end + Int3.Down; Open(q, v.Fin) && BlockInfo.IsFullCube(g.Get(q + (end == rib[0] ? across.Opposite() : across).Vec()).Type); q += Int3.Down)
                            f.Add((q, Made(end + Int3.Up, v.Fin == BlockType.Partition ? BlockType.Rib : v.Fin, Dir.Up)));
                    return rib.All(c => Open(c, BlockType.Beam)) ? f : null;
                }
                int s = v.Bay, o = (hi - lo) % s / 2;
                var at = Enumerable.Range(1, System.Math.Max(0, (hi - lo - o - 1) / s)).Select(k => lo + o + k * s).Where(p => p < hi).ToList();
                var frames = at.Select(Frame).ToList();
                if (at.Count >= 2 && frames.All(f => f != null)) Lay("frames", frames.SelectMany(f => f!).ToList(), at);
            }

        // Bars: out of every torn concrete edge the decay left, every other cell along it, the way the bars run (out of a
        // slab's edge or from under it, out of a broken wall's side or its top), each torn edge whole.
        if (inst.State != PoiState.Active && inst.Fallen.Count > 0)
        {
            var spots = new Dictionary<Int3, Dir>();
            foreach (Int3 c in inst.Fallen)
            {
                if (spots.ContainsKey(c) || !g.Get(c).IsAir || mine.Contains(c) || inst.Protected[c] || lamps.Contains(c)) continue;
                foreach (Dir d in Dirs.All)
                {
                    Block b = g.Get(c + d.Vec());
                    if (d == Dir.Up || !BlockInfo.IsFullCube(b.Type) || b.Material is not (Material.Concrete or Material.Fused or Material.Armour)) continue;
                    if (b.Role == Role.Wall || b.Role is Role.Floor or Role.Roof && d != Dir.Down) { spots[c] = d; break; }
                }
            }
            // (As many torn edges as the years would show, the longest first, each whole: about as many bars as a ruin has.)
            int most = System.Math.Min(600, 120 + inst.Fallen.Count / 12), laid = 0;
            var edges = new List<(Dir D, List<(Int3, Block)> Bars)>();
            var done = new HashSet<Int3>();
            foreach (var (c0, d) in spots)
            {
                if (!done.Add(c0)) continue;
                var edge = new List<Int3> { c0 };
                for (int i = 0; i < edge.Count; i++)
                    foreach (Dir h in Dirs.All)
                        if (h != d && h != d.Opposite() && spots.TryGetValue(edge[i] + h.Vec(), out Dir e) && e == d && done.Add(edge[i] + h.Vec())) edge.Add(edge[i] + h.Vec());
                int parity = edge.Min(q => q.X + q.Y + q.Z) & 1;
                var bars = edge.Where(q => ((q.X + q.Y + q.Z) & 1) == parity).Select(q => (q, new Block(BlockType.Rebar, Material.Rust, Debris.Rust, Role.Debris, d))).ToList();
                if (bars.Count > 0) edges.Add((d, bars));
            }
            foreach (var (_, bars) in edges.OrderByDescending(e => e.Bars.Count).ThenBy(e => e.Bars[0].Item1.X).ThenBy(e => e.Bars[0].Item1.Z).ThenBy(e => e.Bars[0].Item1.Y))
            {
                if (laid + bars.Count > most) continue;
                Lay("bars", bars, new List<int>());
                laid += bars.Count;
            }
        }

        foreach (var (c, b) in lay) g.Set(c, b);
    }
}
