#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// Ornament (docs/place-patterns.md section 1.5): after the shaping, purely decorative blocks laid with the same
/// discipline, each a whole element set out from the building's own lines, never a cell scattered alone.
/// <list type="bullet">
/// <item>A hood over each way in; pipes along the walls of a place with machines, corner to corner.</item>
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
    public sealed record Vocab(BlockType Pilaster, int Bay, bool Hoods, bool Frames, bool Pipes, int PipeAt)
    {
        public string Sig => $"{Pilaster} bay {Bay}{(Hoods ? " hoods" : "")}{(Frames ? " frames" : "")}{(Pipes ? $" pipes {PipeAt}" : "")}";
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
        bool hoods = r.Chance(0.6f), frames = r.Chance(0.6f);
        if (!hoods && !frames) hoods = frames = true;
        BlockType pilaster = high || tech != TechLevel.Low && r.Chance(0.5f) ? BlockType.Rib : BlockType.Pillar;
        return new Vocab(pilaster, high ? r.Between(4, 8) : r.Between(3, 6), hoods, frames, r.Chance(0.8f), r.Between(3, 5));
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
        var walked = new WalkSet(survey.Walked);
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
        // object; and the doors.
        var planes = new Dictionary<(Dir D, int Plane, int Obj), HashSet<Int3>>();
        bool industry = layout.Family.Tags.Any(t => t is "industry" or "utility" or "power" or "machine" or "factory");
        var rims = v.Pipes && industry ? Round.Rims(layout.Extras.Rounds) : new HashSet<Int3>(); // (a round thing's rim is dressed by its own builder)
        if (v.Pipes && industry) // (only builders that dress their walls look at them)
        foreach (var (d, set) in survey.Outer)
            foreach (Int3 c in set)
                if (g.Get(c) is { Type: BlockType.Cube, Role: Role.Wall } b && !BlockInfo.IsTransparent(b.Material) && g.Get(c + d.Vec()).IsAir && !rims.Contains(c))
                {
                    var k = (d, d is Dir.North or Dir.South ? c.Z : c.X, inst.ObjectOf(c));
                    (planes.TryGetValue(k, out var s) ? s : planes[k] = new HashSet<Int3>()).Add(c);
                }
        var doors = new HashSet<Int3>();
        foreach (Int3 c in layout.Doorways.SelectMany(w => w.Cells).Concat(layout.ExitCells))
            for (Int3 q = c; g.Get(q).Type == BlockType.Door && doors.Add(q); q += Int3.Up) { }

        // Pipes: along the wall at one height, the face's whole length at that height, where the place has machines.
        foreach (var ((d, _, _), set) in planes)
        {
            Dir a = d.RotateCw(1);
            int U(Int3 c) => d is Dir.North or Dir.South ? c.X : c.Z;
            int y = set.Min(c => c.Y) + v.PipeAt;
            var row = set.Where(c => c.Y == y).OrderBy(U).ToList();
            if (row.Count >= 4 && U(row[^1]) - U(row[0]) == row.Count - 1 && row.All(c => Open(c + d.Vec(), BlockType.Pipe)))
                Lay("pipe", row.Select(c => (c + d.Vec(), new Block(BlockType.Pipe, Material.Steel, new Rgb(0x6a, 0x6e, 0x70), Role.Decor, a))).ToList(), new List<int>());
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
                        for (Int3 q = end + Int3.Down; Open(q, v.Pilaster) && BlockInfo.IsFullCube(g.Get(q + (end == rib[0] ? across.Opposite() : across).Vec()).Type); q += Int3.Down)
                            f.Add((q, Made(end + Int3.Up, v.Pilaster, Dir.Up)));
                    return rib.All(c => Open(c, BlockType.Beam)) ? f : null;
                }
                // (Set out from the middle of the hall first, then shifted a metre at a time to clear its lamps and beams.)
                int s = v.Bay;
                foreach (int o in Enumerable.Range(0, s).OrderBy(o => System.Math.Abs(o - (hi - lo) % s / 2)))
                {
                    var at = Enumerable.Range(1, System.Math.Max(0, (hi - lo - o - 1) / s)).Select(k => lo + o + k * s).Where(p => p < hi).ToList();
                    var frames = at.Select(Frame).ToList();
                    if (at.Count < 2 || frames.Any(f => f == null)) continue;
                    Lay("frames", frames.SelectMany(f => f!).ToList(), at);
                    break;
                }
            }

        // Bars: out of the torn concrete edges the decay left, every other cell along each edge, the way the bars run (out of
        // a slab's edge or from under it, out of a broken wall's side or its top), each torn edge whole; edges in the order
        // the decay tore them, until about as many bars as a ruin shows.
        if (inst.State != PoiState.Active && inst.Fallen.Count > 0)
        {
            var fallen = new HashSet<Int3>(inst.Fallen);
            var spots = new Dictionary<Int3, Dir?>();
            Dir? Spot(Int3 c)
            {
                if (spots.TryGetValue(c, out Dir? known)) return known;
                Dir? found = null;
                if (fallen.Contains(c) && g.Get(c).IsAir && !mine.Contains(c) && !inst.Protected[c] && !lamps.Contains(c))
                    foreach (Dir d in Dirs.All)
                    {
                        Block b = g.Get(c + d.Vec());
                        if (d == Dir.Up || !BlockInfo.IsFullCube(b.Type) || b.Material is not (Material.Concrete or Material.Fused or Material.Armour)) continue;
                        if (b.Role == Role.Wall || b.Role is Role.Floor or Role.Roof && d != Dir.Down) { found = d; break; }
                    }
                return spots[c] = found;
            }
            int most = System.Math.Min(600, 120 + inst.Fallen.Count / 12), laid = 0;
            var done = new HashSet<Int3>();
            foreach (Int3 c0 in inst.Fallen)
            {
                if (laid >= most) break;
                if (done.Contains(c0) || Spot(c0) is not Dir d) continue;
                done.Add(c0);
                var edge = new List<Int3> { c0 };
                for (int i = 0; i < edge.Count; i++)
                    foreach (Dir h in Dirs.All)
                        if (h != d && h != d.Opposite() && !done.Contains(edge[i] + h.Vec()) && Spot(edge[i] + h.Vec()) == d && done.Add(edge[i] + h.Vec())) edge.Add(edge[i] + h.Vec());
                int parity = (c0.X + c0.Y + c0.Z) & 1;
                var bars = new List<(Int3, Block)>();
                foreach (Int3 q in edge) if (((q.X + q.Y + q.Z) & 1) == parity) bars.Add((q, new Block(BlockType.Rebar, Material.Rust, Debris.Rust, Role.Debris, d)));
                if (laid + bars.Count > most) continue;
                Lay("bars", bars, new List<int>());
                laid += bars.Count;
            }
        }
        foreach (var (c, b) in lay) g.Set(c, b);
    }

    /// <summary>Where anyone walks: the shaping's set and what is reached past its slopes, without copying the first.</summary>
    sealed class WalkSet
    {
        readonly HashSet<Int3> _was, _more = new();
        public WalkSet(HashSet<Int3> was) => _was = was;
        public bool Contains(Int3 q) => _was.Contains(q) || _more.Contains(q);
        public bool Add(Int3 q) => !_was.Contains(q) && _more.Add(q);
    }
}
