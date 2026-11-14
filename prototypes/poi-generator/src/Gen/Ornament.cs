#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// Ornament (docs/place-patterns.md section 1.5): after the shaping, the building's own structure made visible, never a
/// thing stuck on a face. Every element starts from something and ends in something, is cut from what it grows from (its
/// material and finish, its colour or a shade of it), is at least a beam's thickness where it joins, joins flush, and is
/// set out on the building's own lines.
/// <list type="bullet">
/// <item>Beams across a hall at the bay spacing, under the ceiling from wall to wall: every bay of the hall or none.</item>
/// <item>Corbels under an overhang (an eave, a slab or a storey that juts past the wall under it), from the wall up into
/// it, at the bay spacing along it: the whole run or none.</item>
/// <item>A chimney up the middle of a wall that is whole from the ground to the roof, on through the roof and the
/// builder's rise past it, capped in trim.</item>
/// <item>A spire from the middle of a tower's top: a stepped base wider than its shaft, a point at its head.</item>
/// <item>Decay: bars out of every torn concrete edge, every other cell along it, the way the slab or wall ran.</item>
/// </list>
/// Each builder (its world, tech level and faction or culture) rolls its own small set, spacing and proportions, so one
/// world's places look related and two worlds' differ. An element that can't be laid whole is left out; nothing goes
/// where a body walks or stands its head, by or over a door, window, stair or ladder, or on a lamp.
/// </summary>
public static class Ornament
{
    /// <summary>Off for a measurement against the place without it (tests, shots).</summary>
    public static bool Enabled = true;

    /// <summary>One element as laid: its kind, its cells, and where along its line each repeat stands (for spacing).</summary>
    public sealed record Piece(string Kind, List<Int3> Cells, List<int> At);

    /// <summary>A builder's vocabulary: which elements, how far apart, how far a chimney or spire rises, and the shade its trim takes of its walls.</summary>
    public sealed record Vocab(int Bay, bool Beams, bool Corbels, bool Chimneys, bool Spires, int Rise, float Tone)
    {
        public string Sig => $"bay {Bay} rise {Rise} tone {Tone:0.00}{(Beams ? " beams" : "")}{(Corbels ? " corbels" : "")}{(Chimneys ? " chimneys" : "")}{(Spires ? " spires" : "")}";
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
        // (Low tech leans to chimneys and bracketed eaves, high tech to framed halls and spires; each world picks among them.)
        bool beams = r.Chance(high ? 0.8f : 0.6f), corbels = r.Chance(high ? 0.35f : 0.75f), chimneys = r.Chance(tech == TechLevel.Low ? 0.85f : high ? 0.15f : 0.5f), spires = r.Chance(high ? 0.75f : 0.3f);
        if (!beams && !corbels && !chimneys && !spires) beams = corbels = true;
        return new Vocab(high ? r.Between(4, 8) : r.Between(3, 6), beams, corbels, chimneys, spires, r.Between(2, 4), 0.8f + 0.04f * r.Between(0, 4));
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
        // (Cut from what it grows from: its material and its finish, its colour a shade of the builder's trim.)
        Block Made(Int3 from, BlockType t, Dir f, float tone = 1f) { Block b = g.Get(from); return b with { Type = t, Facing = f, Role = Role.Decor, Color = b.Color.Scale(v.Tone * tone) }; }
        var lay = new List<(Int3 Cell, Block B)>();
        void Lay(string kind, List<(Int3, Block)> cells, List<int> at)
        {
            foreach (var (c, _) in cells) mine.Add(c);
            lay.AddRange(cells);
            inst.Ornaments.Add(new Piece(kind, cells.Select(x => x.Item1).ToList(), at));
        }
        // Where along a run of n cells the bay's repeats stand: at the bay spacing, set out evenly about its middle.
        List<int> Bays(int n) { int o = (n - 1) % v.Bay / 2; return Enumerable.Range(0, (n - 1 - o) / v.Bay + 1).Select(k => o + k * v.Bay).ToList(); }
        var rims = Round.Rims(layout.Extras.Rounds); // (a round thing is dressed by its own builder)
        // A wall's face toward a side, flat and whole, and no way through it there or in the two cells under it.
        bool Wall(Int3 w, Dir d) => Shapes.FlatToward(g.Get(w), d) && !rims.Contains(w) && !Way(g.Get(w)) && !Way(g.Get(w + Int3.Down)) && !Way(g.Get(w + Int3.Down * 2));

        // Beams: across a hall at the bay spacing, under the ceiling from wall to wall; every bay of the hall or none.
        if (v.Beams)
            foreach (PlacedModule m in layout.Modules.Where(m => m.Kind is ModuleKind.Hall or ModuleKind.Room && m.Max.Y - m.Min.Y >= 3 && (m.Max.X - m.Min.X + 1) * (m.Max.Z - m.Min.Z + 1) >= 60))
            {
                int y = m.Max.Y;
                var cells = m.Cells.Where(c => c.Y == y).ToList();
                if (m.Cells.Count(c => c.Y == m.Min.Y) < 60) continue;
                bool alongX = m.Max.X - m.Min.X >= m.Max.Z - m.Min.Z;
                Dir across = alongX ? Dir.South : Dir.East;
                int lo = alongX ? m.Min.X : m.Min.Z, hi = alongX ? m.Max.X : m.Max.Z;
                var rows = cells.GroupBy(c => alongX ? c.X : c.Z).ToDictionary(r => r.Key, r => r.OrderBy(c => alongX ? c.Z : c.X).ToList());
                List<(Int3, Block)>? Beam(int p)
                {
                    if (!rows.TryGetValue(p, out var rib)) return null;
                    if (rib.Count < 3 || (alongX ? rib[^1].Z - rib[0].Z : rib[^1].X - rib[0].X) != rib.Count - 1) return null;
                    Int3 w0 = rib[0] - across.Vec(), w1 = rib[^1] + across.Vec();
                    if (!Shapes.FlatToward(g.Get(w0), across) || !Shapes.FlatToward(g.Get(w1), across.Opposite()) || !rib.All(c => Shapes.FlatToward(g.Get(c + Int3.Up), Dir.Down))) return null;
                    // (Clear of anything beside it, a post or a pier it would pass a hand off.)
                    bool Clear(Int3 c) => Open(c, BlockType.Beam) && !BlockInfo.Collides(g.Get(c + across.RotateCw(1).Vec()).Type) && !BlockInfo.Collides(g.Get(c + across.RotateCw(3).Vec()).Type);
                    return rib.All(Clear) ? rib.Select(c => (c, Made(c + Int3.Up, BlockType.Beam, across))).ToList() : null;
                }
                // (Set out from the middle of the hall first, then shifted a metre at a time to clear its lamps and posts.)
                int s = v.Bay;
                foreach (int o in Enumerable.Range(0, s).OrderBy(o => System.Math.Abs(o - (hi - lo) % s / 2)))
                {
                    var at = Enumerable.Range(1, System.Math.Max(0, (hi - lo - o - 1) / s)).Select(k => lo + o + k * s).Where(p => p < hi).ToList();
                    var beams = at.Select(Beam).ToList();
                    if (at.Count < 2 || beams.Any(f => f == null)) continue;
                    Lay("beams", beams.SelectMany(f => f!).ToList(), at);
                    break;
                }
            }

        // Corbels: under an overhang (an eave, a slab or a storey jutting past the wall under it), its full back against the
        // wall and its top under the overhang, at the bay spacing along each straight run of it; the whole run or none.
        if (v.Corbels)
        {
            var under = new Dictionary<(Dir, int, int), HashSet<Int3>>();
            foreach (var (d, set) in survey.Outer)
                foreach (Int3 w in set)
                {
                    Int3 q = w + d.Vec();
                    if (!Outside(q) || !Wall(w, d) || !Shapes.FlatToward(g.Get(q + Int3.Up), Dir.Down) || inst.ObjectOf(q + Int3.Up) != inst.ObjectOf(w)) continue;
                    var k = (d, d is Dir.North or Dir.South ? w.Z : w.X, q.Y);
                    (under.TryGetValue(k, out var s) ? s : under[k] = new HashSet<Int3>()).Add(q);
                }
            foreach (var ((d, _, _), set) in under)
                foreach (List<Int3> run in Shaping.Lines(set, d.RotateCw(1).Vec()))
                {
                    var at = Bays(run.Count);
                    if (at.Count < 2 || !at.All(k => Open(run[k], BlockType.Chamfer))) continue;
                    Lay("corbels", at.Select(k => (run[k], Made(run[k] - d.Vec(), BlockType.Chamfer, d.Opposite()))).ToList(), at);
                }
        }

        // Chimneys: up the middle column of a building's end wall (its narrowest face), whole from its foot to the roof, on
        // through the roof (a slope there gives way to it) and the builder's rise past the highest roof round it, its top
        // course in a darker trim; one to a building.
        if (v.Chimneys)
        {
            var faces = new Dictionary<(Dir, int, int), List<Int3>>();
            foreach (var (d, set) in survey.Outer)
                foreach (Int3 w in set)
                {
                    var k = (d, d is Dir.North or Dir.South ? w.Z : w.X, inst.ObjectOf(w));
                    (faces.TryGetValue(k, out var l) ? l : faces[k] = new List<Int3>()).Add(w);
                }
            int Width(Dir d, List<Int3> face) => d is Dir.North or Dir.South ? face.Max(c => c.X) - face.Min(c => c.X) : face.Max(c => c.Z) - face.Min(c => c.Z);
            var chimneyed = new HashSet<int>();
            foreach (var ((d, _, obj), face) in faces.OrderBy(f => Width(f.Key.Item1, f.Value)).ThenBy(f => f.Key.Item1).ThenBy(f => f.Key.Item2))
            {
                if (chimneyed.Contains(obj)) continue;
                Dir a = d.RotateCw(1);
                int U(Int3 c) => a is Dir.East or Dir.West ? c.X : c.Z;
                int lo = face.Min(U), hi = face.Max(U), u = (lo + hi) / 2, foot = face.Min(c => c.Y);
                if (hi - lo < 4) continue;
                // (The column behind the face: a wall from the face's foot to its top, nothing let into it.)
                var column = face.Where(c => U(c) == u).OrderBy(c => c.Y).ToList();
                if (column.Count < 3 || column[0].Y != foot || column[^1].Y - column[0].Y != column.Count - 1 || !column.All(c => Wall(c, d) && Shapes.FlatToward(g.Get(c), d))) continue;
                Int3 top = column[^1];
                while (BlockInfo.IsFullCube(g.Get(top + Int3.Up).Type)) top += Int3.Up;
                int roof = top.Y;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                        for (int y = top.Y + 1; y <= top.Y + 12; y++)
                            if (BlockInfo.Collides(g.Get(new Int3(top.X + dx, y, top.Z + dz)).Type)) roof = System.Math.Max(roof, y);
                var stack = new List<(Int3, Block)>();
                bool ok = true;
                for (int y = top.Y + 1; y <= roof + v.Rise && ok; y++)
                {
                    Int3 q = new(top.X, y, top.Z);
                    Block at = g.Get(q);
                    ok = at.IsAir ? Open(q, BlockType.Cube) : at.Role == Role.Roof && !BlockInfo.IsFullCube(at.Type) && !walked.Contains(q + Int3.Up);
                    stack.Add((q, Made(column[^1], BlockType.Cube, Dir.North, y == roof + v.Rise ? 0.8f : 1f)));
                }
                if (!ok || stack.Count <= v.Rise || !g.Get(new Int3(top.X, roof + v.Rise + 1, top.Z)).IsAir) continue;
                Lay("chimney", stack, new List<int>());
                chimneyed.Add(obj);
            }
        }

        // Spires: from the middle of a tower's top (an object's highest roof, small and square, over a wider body or tall on its
        // own), a stepped base three cells across, a shaft of the builder's rise, a point at its head.
        if (v.Spires)
        {
            var tops = new Dictionary<(int, int), int>();
            var of = new Dictionary<(int, int), int>();
            foreach (Int3 c in survey.Tops)
                if (c.Y > Assembler.GroundY && (!tops.TryGetValue((c.X, c.Z), out int t) || c.Y > t)) { tops[(c.X, c.Z)] = c.Y; of[(c.X, c.Z)] = inst.ObjectOf(c); }
            foreach (var obj in of.GroupBy(kv => kv.Value))
            {
                var cols = obj.Select(kv => kv.Key).ToList();
                int peak = cols.Max(k => tops[k]);
                var crown = cols.Where(k => tops[k] == peak).ToList();
                int x0 = crown.Min(k => k.Item1), x1 = crown.Max(k => k.Item1), z0 = crown.Min(k => k.Item2), z1 = crown.Max(k => k.Item2);
                if (x1 - x0 < 2 || z1 - z0 < 2 || x1 - x0 > 8 || z1 - z0 > 8 || crown.Count < 0.7 * (x1 - x0 + 1) * (z1 - z0 + 1)) continue;
                // (A tower: its top no more than half the object's plan, or standing ten metres or more on its own.)
                if (crown.Count * 2 > cols.Count && peak < 10) continue;
                int cx = (x0 + x1) / 2, cz = (z0 + z1) / 2;
                var spire = new List<(Int3, Block)>();
                Int3 head = new(cx, peak, cz);
                bool ok = true;
                for (int dz = -1; dz <= 1 && ok; dz++)
                    for (int dx = -1; dx <= 1 && ok; dx++)
                    {
                        Int3 under = new(cx + dx, peak, cz + dz);
                        ok = tops.GetValueOrDefault((under.X, under.Z)) == peak && Shapes.FlatToward(g.Get(under), Dir.Up) && Open(under + Int3.Up, BlockType.Cube);
                        spire.Add((under + Int3.Up, Made(head, BlockType.Cube, Dir.North, 0.9f)));
                    }
                for (int k = 2; k <= v.Rise + 1 && ok; k++) { ok = Open(head + Int3.Up * k, BlockType.Cube); spire.Add((head + Int3.Up * k, Made(head, BlockType.Cube, Dir.North))); }
                Int3 tip = head + Int3.Up * (v.Rise + 2);
                if (ok && Open(tip, BlockType.Spike) && g.Get(tip + Int3.Up).IsAir)
                {
                    spire.Add((tip, Made(head, BlockType.Spike, Dir.North, 0.85f)));
                    Lay("spire", spire, new List<int>());
                }
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
