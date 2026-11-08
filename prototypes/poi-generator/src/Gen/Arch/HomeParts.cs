#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>The parts a house is put together from, each drawn by its own rule on the 2 m lattice in the player's shapes.</summary>
public static partial class Homes
{
    sealed partial class Hut
    {
        public bool StairAt(int u, int v, int storey) => _stair.Contains((u, v, storey));

        /// <summary>Whether a chimney stack can go up these two cells: outer wall both, no door or window through either on any storey.</summary>
        public bool ChimneyFits(int u, int v, int du, int dv)
        {
            foreach (var (cu, cv) in new[] { (u, v), (u + du, v + dv) })
            {
                if (!Foot.Contains((cu, cv)) || !Edge(cu, cv)) return false;
                bool outside = !Foot.Contains((cu - 1, cv)) || !Foot.Contains((cu + 1, cv)) || !Foot.Contains((cu, cv - 1)) || !Foot.Contains((cu, cv + 1));
                if (!outside) return false;
                for (int s = 0; s < Storeys; s++)
                    if (_door.Contains((cu, cv, s)) || _open.Contains((cu, cv, s))) return false;
            }
            return true;
        }

        /// <summary>Keeps a room's module off cells a flight from another building takes (a tower's stair coming up through its floor).</summary>
        public void Reserve(int u, int v0, int n, int storey = 0)
        {
            for (int k = 0; k < n; k++) _stair.Add((u, v0 + k, storey));
        }

        /// <summary>Notes a door cut through a wall this building shares, so its windows and furniture keep clear.</summary>
        public void Mark(int u, int v, bool alongU, int storey, int width = 2)
        {
            for (int k = 0; k < width; k++) _door.Add((alongU ? u + k : u, alongU ? v : v + k, storey));
        }

        int TopEave => Ring(Storeys - 1);

        /// <summary>Columns its roof keeps off: a lower garage against it with a roof of its own.</summary>
        public HashSet<(int U, int V)>? Avoid;

        void Put(int u, int y, int v, Block b)
        {
            b.Facing = b.Facing is Dir.Up or Dir.Down ? b.Facing : _f.Turn(b.Facing);
            C.Set(P(u, y, v), b);
        }

        bool Empty(int u, int y, int v) => !C.Blocks.ContainsKey(P(u, y, v));

        Block WallAt(int u, int y, int v) => Skin?.Invoke(u, y, v) ?? Cube(S.WallM, S.Wall, Role.Wall);

        // ---- the roof surface: a height at every cell corner, laid in slopes ----------------------

        /// <summary>
        /// Lays a surface over columns: h(x, z) is its height over <paramref name="low"/> at each cell corner (null
        /// where there is none), and each column gets the player's piece whose top runs through its four corners:
        /// a slope, a corner, an inner corner or a cube, stacked where a face is steeper than one in one. Under it
        /// a deck holds it together (or, solid, earth down to the ground); over a building's outer wall the wall
        /// rises to meet it, so a gable is closed. Every house sharing the roof keeps its heights for its chimneys.
        /// </summary>
        public static void Surface(List<Hut> huts, Func<int, int, int?> h, IEnumerable<(int U, int V)> columns, int low, Material m, Rgb c,
            Func<int, int, Rgb>? paint = null, bool solid = false, Material? fillM = null, Rgb? fillC = null, Func<int, int, int, bool>? skip = null, bool walls = true) =>
            Surface(huts, (u, v) => h(u, v) is int a && h(u + 1, v) is int b && h(u, v + 1) is int d && h(u + 1, v + 1) is int e ? (a, b, d, e) : null,
                columns, low, m, c, paint, solid, fillM, fillC, skip, walls);

        /// <summary>The same, with each column's four corner heights (north-west, north-east, south-west, south-east) its own.</summary>
        public static void Surface(List<Hut> huts, Func<int, int, (int, int, int, int)?> corners, IEnumerable<(int U, int V)> columns, int low, Material m, Rgb c,
            Func<int, int, Rgb>? paint = null, bool solid = false, Material? fillM = null, Rgb? fillC = null, Func<int, int, int, bool>? skip = null, bool walls = true)
        {
            Hut h0 = huts[0];
            foreach (var (u, v) in columns.Distinct())
            {
                if (h0.Avoid != null && h0.Avoid.Contains((u, v))) continue;
                var cs = corners(u, v);
                if (cs == null) continue;
                var (nw, ne, sw, se) = cs.Value;
                int lo = Math.Min(Math.Min(nw, ne), Math.Min(sw, se)), hi = Math.Max(Math.Max(nw, ne), Math.Max(sw, se));
                Rgb col = paint?.Invoke(u, v) ?? c.Scale(0.96f + (Hash.Cell(new Int3(u, 0, v), 0x7EEu) % 8) / 100f);
                var stuff = new Stuff2(m, col);
                int bottom = int.MaxValue, top = int.MinValue;
                void Lay(int y, Block b)
                {
                    if (skip != null && skip(u, y, v)) return;
                    h0.Put(u, y, v, b);
                    bottom = Math.Min(bottom, y);
                    top = Math.Max(top, y);
                }
                if (hi == lo) { if (lo > 0) Lay(low + lo - 1, new Block(BlockType.Cube, m, col, Role.Roof)); }
                else
                    for (int lv = lo; lv < hi; lv++)
                        // Where two roofs meet steeper than one in one, the courses under the top piece are solid: a slope is never stacked under another.
                        if (Plot.Piece(nw > lv, ne > lv, sw > lv, se > lv, stuff) is Block piece) Lay(low + lv, lv + 1 < hi ? new Block(BlockType.Cube, m, col, Role.Roof) : piece);
                if (bottom == int.MaxValue) continue;
                bool foot = huts.Any(x => x.Foot.Contains((u, v)));
                if (solid)
                {
                    for (int y = -h0.Lift; y < bottom; y++)
                        if (skip == null || !skip(u, y, v)) h0.Put(u, y, v, new Block(BlockType.Cube, fillM ?? m, (fillC ?? col).Scale(y < bottom - 1 ? 0.9f : 1f), Role.Wall));
                }
                else
                {
                    int eave = foot ? huts.Where(x => x.Foot.Contains((u, v))).Max(x => x.TopEave) : int.MinValue;
                    if ((foot ? bottom - 1 > eave : bottom - 1 >= low) && bottom - 1 >= -h0.Lift && (skip == null || !skip(u, bottom - 1, v)))
                        h0.Put(u, bottom - 1, v, new Block(BlockType.Cube, m, col.Scale(0.8f), Role.Roof));
                }
                foreach (Hut x in huts)
                {
                    if (!x.Foot.Contains((u, v))) continue;
                    if (walls && x.Edge(u, v))
                        for (int y = x.TopEave + 1; y < bottom; y++) x.Put(u, y, v, x.WallAt(u, y, v));
                    x._top[(u, v)] = Math.Max(x._top.GetValueOrDefault((u, v), int.MinValue), top);
                }
            }
        }

        /// <summary>The Chebyshev distance in from the edge of a set of cells, at each cell corner: a hipped roof over any plan, valleys and all.</summary>
        static Dictionary<(int, int), int> Inward(HashSet<(int U, int V)> cells)
        {
            var d = new Dictionary<(int, int), int>();
            var q = new Queue<(int, int)>();
            var corners = new HashSet<(int, int)>();
            foreach (var (u, v) in cells)
                for (int dz = 0; dz <= 1; dz++)
                    for (int dx = 0; dx <= 1; dx++) corners.Add((u + dx, v + dz));
            foreach (var (x, z) in corners)
            {
                bool all = cells.Contains((x - 1, z - 1)) && cells.Contains((x, z - 1)) && cells.Contains((x - 1, z)) && cells.Contains((x, z));
                if (!all) { d[(x, z)] = 0; q.Enqueue((x, z)); }
            }
            while (q.Count > 0)
            {
                var (x, z) = q.Dequeue();
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        var n = (x + dx, z + dz);
                        if (!corners.Contains(n) || d.ContainsKey(n)) continue;
                        d[n] = d[(x, z)] + 1;
                        q.Enqueue(n);
                    }
            }
            return d;
        }

        static HashSet<(int U, int V)> Grow(IEnumerable<(int U, int V)> cells, int by)
        {
            var set = new HashSet<(int, int)>(cells);
            if (by <= 0) return set;
            var grown = new HashSet<(int, int)>();
            foreach (var (u, v) in set)
                for (int dv = -by; dv <= by; dv++)
                    for (int du = -by; du <= by; du++) grown.Add((u + du, v + dv));
            return grown;
        }

        /// <summary>A hipped roof over every building given (one plan), its eaves o cells out: hips on the corners, valleys in the angles, a ridge along the narrowest span. Pagoda eaves: a flat lip turned up at the corners. Capped: it stops rising there, flat on top (a mansard's crown).</summary>
        public static void Hipped(List<Hut> huts, int o, bool flare, int cap = int.MaxValue)
        {
            Hut h = huts[0];
            var over = Grow(huts.SelectMany(x => x.Foot), o);
            var d = Inward(over);
            int? H(int x, int z)
            {
                if (!d.TryGetValue((x, z), out int k)) return null;
                k = Math.Min(k, cap);
                if (!flare) return k;
                int inside = (over.Contains((x - 1, z - 1)) ? 1 : 0) + (over.Contains((x, z - 1)) ? 1 : 0) + (over.Contains((x - 1, z)) ? 1 : 0) + (over.Contains((x, z)) ? 1 : 0);
                return inside == 1 ? 2 : Math.Max(k, 1);
            }
            Surface(huts, H, over, h.TopEave + 1 - o, h.S.RoofM, h.S.Roof);
        }

        /// <summary>A skirt roof round a storey: the ring of a hipped roof o cells out, at that storey's top, the walls going on up inside it (a pagoda's lower eaves, a mansard's kick).</summary>
        public void Skirt(int storey, int o, bool flare)
        {
            var over = Grow(Foot, o);
            var d = Inward(over);
            int? H(int x, int z)
            {
                if (!d.TryGetValue((x, z), out int k)) return null;
                k = Math.Min(k, o);
                if (!flare) return k;
                int inside = (over.Contains((x - 1, z - 1)) ? 1 : 0) + (over.Contains((x, z - 1)) ? 1 : 0) + (over.Contains((x - 1, z)) ? 1 : 0) + (over.Contains((x, z)) ? 1 : 0);
                return inside == 1 ? 2 : Math.Max(k, 1);
            }
            Surface(new List<Hut> { this }, H, over.Where(c => !Foot.Contains(c)), Ring(storey) + 1 - o, S.RoofM, S.Roof, walls: false);
        }

        /// <summary>
        /// Roofs of one form per wing, as prisms joined where they meet (the higher wins, so a cross gable meets
        /// its neighbour in a valley): a gable along the wing's ridge, eaves o out on its long sides and its ends
        /// flush with the gable walls; a shed rising from the front eave and flat behind; a wave along the wing.
        /// An A-frame's eaves reach the ground and its gable walls fill the whole triangle.
        /// </summary>
        public void Prisms(List<(Wing W, string Kind)> parts, int o, List<Hut> huts, bool aframe = false)
        {
            var rects = new List<(int X0, int Z0, int X1, int Z1, string Kind, bool AlongU)>();
            var foot = new HashSet<(int U, int V)>(huts.SelectMany(x => x.Foot));
            foreach (var (w, kind) in parts)
            {
                bool alongU = w.RidgeAlongU;
                int x0 = w.U0, x1 = w.U1 + 1, z0 = w.V0, z1 = w.V1 + 1;
                if (kind == "wave") { x0 -= o; x1 += o; z0 -= o; z1 += o; }
                else if (alongU) { z0 -= o; z1 += o; }
                else { x0 -= o; x1 += o; }
                rects.Add((x0, z0, x1, z1, kind, alongU));
            }
            // A gable that ends against a wing whose roof stands as high or higher runs on into it to that wing's
            // middle, meeting its roof in valleys; the higher one stops at its own gable wall.
            int Span(int i) => parts[i].W.RidgeAlongU ? (parts[i].W.V1 - parts[i].W.V0 + 1) : (parts[i].W.U1 - parts[i].W.U0 + 1);
            int Owner((int U, int V) cell, int not) => Enumerable.Range(0, parts.Count).FirstOrDefault(i => i != not && cell.U >= parts[i].W.U0 && cell.U <= parts[i].W.U1 && cell.V >= parts[i].W.V0 && cell.V <= parts[i].W.V1, -1);
            for (int i = 0; i < rects.Count; i++)
            {
                var (x0, z0, x1, z1, kind, alongU) = rects[i];
                if (kind != "gable") continue;
                Wing w = parts[i].W;
                int mid = alongU ? (w.V0 + w.V1) / 2 : (w.U0 + w.U1) / 2;
                int Run((int, int) start, int step)
                {
                    int n = 0;
                    var (u, v) = start;
                    while (n < 40 && foot.Contains((u, v))) { n++; if (alongU) u += step; else v += step; }
                    return n;
                }
                foreach (int step in new[] { -1, 1 })
                {
                    (int, int) beyond = alongU ? (step < 0 ? w.U0 - 1 : w.U1 + 1, mid) : (mid, step < 0 ? w.V0 - 1 : w.V1 + 1);
                    int other = Owner(beyond, i);
                    if (other < 0 || Span(other) < Span(i)) continue;
                    int ext = (Run(beyond, step) + 1) / 2;
                    if (alongU) { if (step < 0) x0 -= ext; else x1 += ext; }
                    else { if (step < 0) z0 -= ext; else z1 += ext; }
                }
                rects[i] = (x0, z0, x1, z1, kind, alongU);
            }
            int K((int X0, int Z0, int X1, int Z1, string Kind, bool AlongU) r, int x, int z) => r.Kind switch
            {
                "shed" => r.AlongU ? Math.Min(z - r.Z0, 5) : Math.Min(x - r.X0, 5),
                "wave" => 1 + (int)MathF.Round(1.5f * (1f - MathF.Cos(MathF.Tau * (x - r.X0) / 12f))),
                _ => r.AlongU ? Math.Min(z - r.Z0, r.Z1 - z) : Math.Min(x - r.X0, r.X1 - x),
            };
            // Each column from the prisms over it only, so where one roof stops its edge never pulls the next one up.
            (int, int, int, int)? Corners(int u, int v)
            {
                bool any = false;
                int a = int.MinValue, b = int.MinValue, c = int.MinValue, d = int.MinValue;
                foreach (var r in rects)
                {
                    if (u < r.X0 || u + 1 > r.X1 || v < r.Z0 || v + 1 > r.Z1) continue;
                    any = true;
                    a = Math.Max(a, K(r, u, v));
                    b = Math.Max(b, K(r, u + 1, v));
                    c = Math.Max(c, K(r, u, v + 1));
                    d = Math.Max(d, K(r, u + 1, v + 1));
                }
                return any ? (a, b, c, d) : null;
            }
            var cols = new List<(int, int)>();
            foreach (var r in rects)
                for (int z = r.Z0; z < r.Z1; z++)
                    for (int x = r.X0; x < r.X1; x++) cols.Add((x, z));
            int low = TopEave + 1 - o;
            Surface(huts, Corners, cols, low, S.RoofM, S.Roof);
            if (!aframe) return;
            // The A-frame's gable walls: the whole triangle, from the ground to the slope.
            foreach (var r in rects)
                foreach (int edge in r.AlongU ? new[] { r.X0, r.X1 - 1 } : new[] { r.Z0, r.Z1 - 1 })
                    for (int t = r.AlongU ? r.Z0 : r.X0; t < (r.AlongU ? r.Z1 : r.X1); t++)
                    {
                        var (u, v) = r.AlongU ? (edge, t) : (t, edge);
                        if (Foot.Contains((u, v))) continue;
                        int bottomY = -1;
                        for (int y = 0; y < 40 && bottomY < 0; y++)
                            if (C.Get(P(u, y, v)).Role == Role.Roof) bottomY = y;
                        for (int y = 0; y < bottomY; y++) Put(u, y, v, WallAt(u, y, v));
                    }
        }

        /// <summary>
        /// A dome over a round plan, its rim o out: eight faces, four square to the axes and four on the diagonals
        /// (an oval's from its plan's own distances). Glass on a frame; a mushroom's cap, spotted; or an earth
        /// dome from the ground up, turfed, with a way in cut through it to the door.
        /// </summary>
        public void Dome(int o, bool glass, bool spotted, bool earth, int doorU = 0, int doorV = 0)
        {
            int u0 = Foot.Min(c => c.U), u1 = Foot.Max(c => c.U), v0 = Foot.Min(c => c.V), v1 = Foot.Max(c => c.V);
            if (earth) o = TopEave + 1;
            int low = TopEave + 1 - o;
            bool round = u1 - u0 == v1 - v0;
            Func<int, int, int?> H;
            IEnumerable<(int, int)> cols;
            if (round)
            {
                double cu = (u0 + u1 + 1) / 2.0, cv = (v0 + v1 + 1) / 2.0, big = (u1 - u0 + 1) / 2.0 + o;
                double chamfer = Math.Round(big * (Math.Sqrt(2) - 1));
                H = (x, z) =>
                {
                    double ax = Math.Abs(x - cu), az = Math.Abs(z - cv);
                    if (ax * ax + az * az > (big + 1) * (big + 1)) return null;
                    return (int)Math.Max(0, Math.Min(big - Math.Max(ax, az), big + chamfer - ax - az));
                };
                var list = new List<(int, int)>();
                for (int v = (int)(cv - big) - 1; v <= (int)(cv + big) + 1; v++)
                    for (int u = (int)(cu - big) - 1; u <= (int)(cu + big) + 1; u++)
                        if ((u + 0.5 - cu) * (u + 0.5 - cu) + (v + 0.5 - cv) * (v + 0.5 - cv) <= big * big) list.Add((u, v));
                cols = list;
            }
            else
            {
                var over = Grow(Foot, o);
                var d = Inward(over);
                H = (x, z) => d.TryGetValue((x, z), out int k) ? k : null;
                cols = over;
            }
            Material m = earth ? Material.Plant : glass ? Material.Glass : S.RoofM;
            Rgb grass = new(0x5c, 0x74, 0x3c);
            Func<int, int, Rgb>? paint = glass ? (u, v) => (u % 4 == 0 || v % 4 == 0) ? new Rgb(0x8c, 0x98, 0xa0) : S.Roof
                : spotted ? (u, v) => Hash.Cell(new Int3(u, 1, v), 0x5B07u) % 9 == 0 ? new Rgb(0xec, 0xe8, 0xdc) : S.Roof
                : earth ? (u, v) => grass.Scale(0.9f + (Hash.Cell(new Int3(u, 2, v), 0xE47u) % 20) / 100f) : null;
            // Never down over the way to the door: an earth dome's passage, a low cap's notch over the path.
            Func<int, int, int, bool> skip = earth ? (u, y, v) => u >= doorU - 1 && u <= doorU + 2 && v < doorV && y <= Ring(0)
                : (u, y, v) => u >= doorU - 1 && u <= doorU + 2 && v < doorV && y < Ring(0);
            Surface(new List<Hut> { this }, H, cols, low, m, earth ? grass : S.Roof, paint, skip: skip);
            if (glass)
                foreach (var (u, v) in cols)
                    foreach (int y in Enumerable.Range(low, 30))
                        if (C.Blocks.TryGetValue(P(u, y, v), out Block b) && b.Material == Material.Glass && b.Role == Role.Roof && (u % 4 == 0 || v % 4 == 0))
                        { b.Material = Material.Alloy; C.Blocks[P(u, y, v)] = b; }
            if (!earth) return;
            // The way in: cheek walls and a lintel from the dome's foot to the door.
            int front = cols.Where(c => c.Item1 >= doorU && c.Item1 <= doorU + 1).Min(c => c.Item2);
            for (int v = front; v < doorV; v++)
            {
                foreach (int u in new[] { doorU - 1, doorU + 2 })
                    for (int y = 0; y <= Ring(0); y++) Put(u, y, v, new Block(BlockType.Cube, Material.Rock, S.Base, Role.Wall));
                for (int u = doorU; u <= doorU + 1; u++) Put(u, Ring(0), v, new Block(BlockType.Cube, Material.Rock, S.Base, Role.Wall));
            }
        }

        /// <summary>A turf roof: grass over the flat roof inside its parapet.</summary>
        public void Turf()
        {
            var grass = new Rgb(0x5c, 0x74, 0x3c);
            foreach (var (u, v) in Foot)
                if (!Edge(u, v)) Put(u, TopEave + 1, v, new Block(BlockType.Cube, Material.Plant, grass.Scale(0.9f + (Hash.Cell(new Int3(u, 3, v), 0x7E4u) % 20) / 100f), Role.Roof));
        }

        /// <summary>
        /// Earth heaped against the back and sides up to the eaves (or over them into the hill), a slope down to the
        /// ground all round, a stone cheek where it stops at the front. The front stays open to the light.
        /// </summary>
        public void Berm(int frontV, int extra, bool round)
        {
            if (round) return;
            int k = TopEave + 2 + extra;
            var over = Grow(Foot, k);
            var d = Inward(over);
            int? H(int x, int z) => d.TryGetValue((x, z), out int n) ? Math.Min(n, k) : null;
            var soil = new Rgb(0x6c, 0x5c, 0x44);
            var grass = new Rgb(0x5c, 0x74, 0x3c);
            int fv = frontV + 1;
            var cols = over.Where(c => !Foot.Contains(c) && c.V >= fv).ToList();
            Surface(new List<Hut> { this }, H, cols, 0, Material.Plant, grass, (u, v) => grass.Scale(0.88f + (Hash.Cell(new Int3(u, 4, v), 0xBE4u) % 22) / 100f),
                solid: true, fillM: Material.Soil, fillC: soil, walls: false);
            // The cheek where the earth is cut off at the front.
            foreach (var (u, v) in cols.Where(c => c.V == fv))
                for (int y = 0; y < 30; y++)
                {
                    Block b = C.Get(P(u, y, v));
                    if (b.IsAir) break;
                    Put(u, y, v, new Block(BlockType.Cube, Material.Rock, S.Base.Scale(1f + (Hash.Cell(new Int3(u, y, v), 9) % 10) / 100f), Role.Wall));
                }
            if (extra > 0) Turf();
        }

        // ---- windows ------------------------------------------------------------------------------

        /// <summary>Tall glass: every window a modern house has runs from the floor to the ceiling.</summary>
        public void TallWindows()
        {
            foreach (var (u, v, s) in _open.ToList())
            {
                int walk = Walk(s);
                foreach (int y in new[] { walk, walk + Height - 1 })
                    if (y < Ring(s) && C.Get(P(u, y, v)).Role == Role.Wall) C.Set(P(u, y, v), new Block(BlockType.Pane, Material.Glass, new Rgb(0x5a, 0x6c, 0x70), Role.Window));
            }
        }

        /// <summary>A shutter either side of every window: the wall beside it in the shutters' paint.</summary>
        public void Shutters(Rgb paint)
        {
            foreach (var (u, v, s) in _open.ToList())
                foreach (var (du, dv) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    var n = (u + du, v + dv);
                    if (_open.Contains((n.Item1, n.Item2, s)) || !Foot.Contains(n) || !Edge(n.Item1, n.Item2)) continue;
                    for (int y = Walk(s) + 1; y <= Walk(s) + 2; y++)
                        if (C.Get(P(n.Item1, y, n.Item2)) is { Type: BlockType.Cube, Role: Role.Wall }) C.Set(P(n.Item1, y, n.Item2), new Block(BlockType.Cube, Material.Timber, paint, Role.Wall));
                }
        }

        /// <summary>A band of glass round a round house, a metre off the floor, every few cells (a glass shell is glass already).</summary>
        public void Band(int every)
        {
            if (every <= 0) return;
            foreach (var (u, v) in Foot.Where(c => Edge(c.U, c.V)))
            {
                if ((u + v) % 3 == 0 || _door.Contains((u, v, 0))) continue;
                for (int s = 0; s < Storeys; s++)
                    for (int y = Walk(s) + 1; y <= Walk(s) + 2; y++)
                        if (!_door.Contains((u, v, s)) && C.Get(P(u, y, v)).Role == Role.Wall) C.Set(P(u, y, v), new Block(BlockType.Pane, Material.Glass, new Rgb(0x5a, 0x6c, 0x70), Role.Window));
            }
        }

        /// <summary>
        /// Dormers on the front slope: a box out of the roof at the wall's line, a window in its face and a little
        /// gable over it, wherever the roof stands high enough over the eaves to take one.
        /// </summary>
        public void Dormers(Wing w, Rng r)
        {
            if (!w.RidgeAlongU) return;
            int e = TopEave, rise = (w.V1 - w.V0 + 1) / 2;
            if (rise < 5) return;
            int span = w.U1 - w.U0 + 1, n = span >= 14 ? 2 : 1;
            var stuff = new Stuff2(S.RoofM, S.Roof);
            for (int i = 0; i < n; i++)
            {
                int u = w.U0 + (int)((i + 0.5f) * span / n) - 2;
                if (u < w.U0 + 1 || u + 3 > w.U1 - 1) continue;
                int v = w.V0;
                // Only on a plain run of the front slope: not on a hip's end, not into a cross gable.
                if (Enumerable.Range(0, 4).Any(du => _top.GetValueOrDefault((u + du, v + 2), int.MinValue) != e + 3 || _top.GetValueOrDefault((u + du, v + 4), int.MinValue) != e + 5)) continue;
                for (int dv = 0; dv <= 2; dv++)
                {
                    for (int du = 0; du <= 3; du++)
                        for (int y = e + 1; y <= e + 2; y++)
                        {
                            bool face = dv == 0 && du is 1 or 2;
                            if (face) Put(u + du, y, v, new Block(BlockType.Pane, Material.Glass, new Rgb(0x5a, 0x6c, 0x70), Role.Window));
                            else if (dv == 1 && du is 1 or 2) C.Clear(P(u + du, y, v + dv));
                            else Put(u + du, y, v + dv, WallAt(u + du, y, v + dv));
                        }
                    // Its little gable: two slopes and the ridge between.
                    Put(u, e + 3, v + dv, new Block(BlockType.Ramp, S.RoofM, S.Roof, Role.Roof, Dir.East));
                    Put(u + 3, e + 3, v + dv, new Block(BlockType.Ramp, S.RoofM, S.Roof, Role.Roof, Dir.West));
                    Put(u + 1, e + 3, v + dv, new Block(BlockType.Cube, S.RoofM, S.Roof.Scale(0.85f), Role.Roof));
                    Put(u + 2, e + 3, v + dv, new Block(BlockType.Cube, S.RoofM, S.Roof.Scale(0.85f), Role.Roof));
                    Put(u + 1, e + 4, v + dv, new Block(BlockType.Ramp, S.RoofM, S.Roof, Role.Roof, Dir.East));
                    Put(u + 2, e + 4, v + dv, new Block(BlockType.Ramp, S.RoofM, S.Roof, Role.Roof, Dir.West));
                    if (C.Blocks.ContainsKey(P(u + 1, e + 5, v + dv)) && dv < 2) C.Remove(P(u + 1, e + 5, v + dv));
                }
            }
        }

        // ---- garages --------------------------------------------------------------------------------

        /// <summary>
        /// A garage door in an opening wide as the drive and three metres tall, its header the wall's top course:
        /// a sectional door in panels, a ribbed roller between dark guides, glazed sections over a solid one, boarded
        /// timber, or a pair of side-hung leaves braced on the diagonal, one swung open now and then. Some stand
        /// half up.
        /// </summary>
        public void GarageDoor(int u0, int v, int wide, string kind, Rgb paint, Rng r)
        {
            Material metal = _l.Tech == TechLevel.High ? Material.Alloy : _l.Tech == TechLevel.Low ? Material.Timber : Material.Steel;
            bool halfUp = kind is "panel" or "ribbed" or "glazed" && r.Chance(0.2f);
            int half = wide / 2;
            bool swung = kind == "hinged" && r.Chance(0.35f);
            for (int k = 0; k < wide; k++)
                for (int y = 0; y < 3; y++)
                {
                    if (halfUp && y < 2) continue;
                    Block b = kind switch
                    {
                        "ribbed" => new Block(BlockType.Cube, metal, (y & 1) == 0 ? paint : paint.Scale(0.74f), Role.Wall),
                        "glazed" => y == 0 || k % 4 == 3 && k < wide - 1 ? new Block(BlockType.Cube, Material.Alloy, paint.Scale(0.7f), Role.Wall)
                            : new Block(BlockType.Pane, Material.Glass, new Rgb(0x58, 0x6a, 0x70), Role.Window),
                        "timber" => new Block(BlockType.Cube, Material.Timber, (k & 1) == 0 ? paint : paint.Scale(0.86f), Role.Wall),
                        "hinged" => new Block(BlockType.Cube, Material.Timber, k == half - 1 || k == half ? paint.Scale(0.7f) : (k % half) == (y + (k >= half ? 0 : 1)) % half ? paint.Scale(0.8f) : paint, Role.Wall),
                        _ => new Block(BlockType.Cube, metal, y == 1 ? paint : paint.Scale(0.9f), Role.Wall),
                    };
                    if (swung && k < half) continue;
                    Put(u0 + k, y, v, b);
                }
            if (kind == "ribbed")
                foreach (int u in new[] { u0 - 1, u0 + wide })
                    for (int y = 0; y < 3; y++) Put(u, y, v, new Block(BlockType.Cube, metal, new Rgb(0x3a, 0x3c, 0x40), Role.Wall));
            // A leaf swung open stands out from its hinge.
            if (swung)
                for (int j = 1; j <= half; j++)
                    for (int y = 0; y < 3; y++) Put(u0, y, v - j, new Block(BlockType.Cube, Material.Timber, paint.Scale(j == half ? 0.8f : 1f), Role.Wall));
        }

        // ---- what stands in front ---------------------------------------------------------------------

        /// <summary>Posts under a raised house: at its corners and every 4 m along its walls, down to the ground.</summary>
        public void Stilts()
        {
            if (Lift == 0) return;
            foreach (var (u, v) in Foot.Where(c => Edge(c.U, c.V)))
            {
                bool corner = !Foot.Contains((u - 1, v)) && !Foot.Contains((u, v - 1)) || !Foot.Contains((u + 1, v)) && !Foot.Contains((u, v + 1))
                    || !Foot.Contains((u - 1, v)) && !Foot.Contains((u, v + 1)) || !Foot.Contains((u + 1, v)) && !Foot.Contains((u, v - 1));
                if (!corner && u % 4 != 0 && v % 4 != 0) continue;
                for (int y = -Lift; y < -1; y++) Put(u, y, v, new Block(BlockType.Rib, Material.Timber, S.Door.Lerp(new Rgb(0x5a, 0x48, 0x38), 0.6f), Role.Wall));
            }
        }

        /// <summary>
        /// A verandah along the front: boards two metres deep, posts every four, a lean roof off the wall where the
        /// eaves don't already cover it. Raised with the house on stilts, its edge railed but for the steps.
        /// </summary>
        public void Verandah(int u0, int u1, int frontV, bool raised, int storeys)
        {
            var wood = S.Door.Lerp(new Rgb(0x6a, 0x58, 0x46), 0.5f);
            for (int u = u0; u <= u1; u++)
                for (int d = 1; d <= 2; d++)
                {
                    if (raised) Put(u, -1, frontV - d, new Block(BlockType.Cube, Material.Timber, wood, Role.Floor));
                    else _l.Plate(_f, u, frontV - d, wood);
                }
            for (int u = u0; u <= u1; u++)
            {
                bool post = u == u0 || u == u1 || (u - u0) % 4 == 0;
                int v = frontV - 2;
                if (post)
                    for (int y = raised ? -Lift : 0; y <= 1; y++)
                        if (y != -1) Put(u, y, v, new Block(BlockType.Pillar, Material.Timber, wood.Scale(0.85f), Role.Decor));
                if (raised && post) Put(u, -1, v, new Block(BlockType.Cube, Material.Timber, wood, Role.Floor));
                // The lean roof: a slope down from the wall, where nothing is already over it.
                if (Empty(u, 3, frontV - 1) && Empty(u, 4, frontV - 1)) Put(u, 3, frontV - 1, new Block(BlockType.Ramp, S.RoofM, S.Roof, Role.Roof, Dir.South));
                if (Empty(u, 2, frontV - 1)) Put(u, 2, frontV - 1, new Block(BlockType.Cube, S.RoofM, S.Roof.Scale(0.8f), Role.Roof));
                if (Empty(u, 2, frontV - 2) && Empty(u, 3, frontV - 2)) Put(u, 2, frontV - 2, new Block(BlockType.Ramp, S.RoofM, S.Roof, Role.Roof, Dir.South));
                else if (Empty(u, 2, frontV - 2)) Put(u, 2, frontV - 2, new Block(BlockType.Cube, S.RoofM, S.Roof.Scale(0.8f), Role.Roof));
            }
            _verandah ??= (u0, u1);
        }

        (int U0, int U1)? _verandah;

        /// <summary>Steps up to a raised door: a solid flight rising toward the house to its verandah's edge.</summary>
        public void Steps(int u, int frontV)
        {
            var wood = S.Door.Lerp(new Rgb(0x6a, 0x58, 0x46), 0.5f);
            for (int k = 0; k < Lift; k++)
            {
                int v = frontV - 3 - (Lift - 1) + k, y = k - Lift;
                for (int du = 0; du < 2; du++)
                {
                    Put(u + du, y, v, new Block(BlockType.Stair, Material.Timber, wood, Role.Traverse, Dir.South));
                    for (int f = -Lift; f < y; f++) Put(u + du, f, v, new Block(BlockType.Cube, Material.Timber, wood.Scale(0.8f), Role.Wall));
                }
            }
            // Rails along the deck's edge, the steps left open.
            if (_verandah is var (a0, a1))
                for (int x = a0; x <= a1; x++)
                    if ((x < u || x > u + 1) && Empty(x, 0, frontV - 2)) Put(x, 0, frontV - 2, new Block(BlockType.Rail, Material.Timber, wood, Role.Decor));
        }

        /// <summary>Bay windows out of the ground floor's front rooms: a box a metre out, glass on three sides, a slope over it.</summary>
        public void Bays(int frontV, int storeys, Rng r)
        {
            if (_verandah != null) return;
            foreach (Room rm in Rooms.Where(x => x.Storey == 0 && x.V0 == frontV + 1 && x.Kind != ModuleKind.Hall && x.Purpose != "workshop" && x.U1 - x.U0 + 1 >= 4 && x.Shape == null).ToList())
            {
                int u = rm.U0 + (rm.U1 - rm.U0 + 1 - 4) / 2, v = frontV - 1;
                bool clear = true;
                for (int du = -1; du <= 4 && clear; du++)
                    for (int y = 0; y <= 3 && clear; y++)
                        if (!Empty(u + du, y, v) && C.Get(P(u + du, y, v)).Role != Role.Roof || _door.Contains((u + du, frontV, 0)) || Foot.Contains((u + du, v)) || !Foot.Contains((u + du, frontV))) clear = false;
                if (!clear || !r.Chance(0.8f)) continue;
                for (int du = 0; du <= 3; du++)
                    for (int y = 0; y <= 2; y++)
                    {
                        bool glass = du is 1 or 2 && y >= 1;
                        Put(u + du, y, v, glass ? new Block(BlockType.Pane, Material.Glass, new Rgb(0x5a, 0x6c, 0x70), Role.Window) : WallAt(u + du, y, v));
                        if (glass) { C.Clear(P(u + du, y, frontV)); _open.Add((u + du, frontV, 0)); }
                    }
                for (int du = 0; du <= 3; du++)
                    if (Empty(u + du, 3, v) || C.Get(P(u + du, 3, v)).Role == Role.Roof) Put(u + du, 3, v, new Block(BlockType.Ramp, S.RoofM, S.Roof, Role.Roof, Dir.South));
            }
        }

        /// <summary>A balcony off the upper floor: a slab two metres out on the storey line, a rail round it, a door on to it.</summary>
        public void Balcony(int u0, int wide, int frontV)
        {
            var rail = new Block(BlockType.Rail, _l.Tech == TechLevel.Low ? Material.Timber : Material.Steel, _l.Tech == TechLevel.Low ? S.Door : new Rgb(0x4a, 0x4c, 0x50), Role.Decor);
            int y = Ring(0);
            for (int u = u0 - 1; u <= u0 + wide; u++)
                for (int d = 1; d <= 2; d++)
                {
                    Put(u, y, frontV - d, new Block(BlockType.Cube, S.BaseM, S.Floor.Scale(1.1f), Role.Floor));
                    // A porch roof under it becomes its soffit, not a slope under a slab.
                    if (C.Get(P(u, y - 1, frontV - d)).Type == BlockType.Ramp) Put(u, y - 1, frontV - d, new Block(BlockType.Cube, S.RoofM, S.Roof.Scale(0.8f), Role.Roof));
                    bool edge = d == 2 || u == u0 - 1 || u == u0 + wide;
                    if (edge) Put(u, y + 1, frontV - d, rail);
                    else if (!Empty(u, y + 1, frontV - d)) C.Clear(P(u, y + 1, frontV - d));
                }
        }

        /// <summary>Vigas: the roof beams' ends standing out of an adobe wall under its parapet, every two metres.</summary>
        public void Vigas()
        {
            var wood = new Rgb(0x5c, 0x48, 0x34);
            int y = TopEave;
            foreach (var (u, v) in Foot.Where(c => Edge(c.U, c.V)).ToList())
            {
                if ((u + v) % 2 != 0) continue;
                foreach (Dir d in new[] { Dir.North, Dir.East, Dir.West })
                {
                    Int3 o = d.Vec();
                    var n = (u + o.X, v + o.Z);
                    if (Foot.Contains(n) || !Empty(n.Item1, y, n.Item2)) continue;
                    if (d == Dir.North ? Foot.Contains((u, v + 1)) && (Foot.Contains((u - 1, v)) && Foot.Contains((u + 1, v))) : Foot.Contains((u, v - 1)) && Foot.Contains((u, v + 1)))
                        Put(n.Item1, y, n.Item2, new Block(BlockType.Pipe, Material.Timber, wood, Role.Decor, d));
                }
            }
        }

        /// <summary>A canal house's hoist beam out of the top of its gable, over the street.</summary>
        public void Hoist(int u, int frontV)
        {
            int top = Enumerable.Range(TopEave, 30).Where(y => C.Get(P(u, y, frontV)).Role is Role.Wall).DefaultIfEmpty(-1).Max();
            if (top < TopEave + 3) return;
            for (int d = 1; d <= 2; d++) Put(u, top - 1, frontV - d, new Block(BlockType.Pipe, Material.Timber, new Rgb(0x4a, 0x3c, 0x30), Role.Decor, Dir.North));
        }

        /// <summary>
        /// Where the street's service drop is fixed: a short steel mast on the roof over the front wall near a
        /// corner (on the parapet of a flat roof), its top the fixing point, facing the street; and the meter box
        /// at the foot of the wall below. A terrace has one to each house.
        /// </summary>
        public void Service(int doorU, int frontV, List<int>? units)
        {
            var steel = new Rgb(0x4c, 0x50, 0x54);
            var doors = new HashSet<Int3>(C.Doorways.SelectMany(d => d.Cells));
            bool NearDoor(int u, int v) => Enumerable.Range(-2, 5).Any(du => Enumerable.Range(-2, 5).Any(dv => doors.Contains(_f.At(u + du, Lift, v + dv)) || doors.Contains(_f.At(u + du, 0, v + dv))))
                || _door.Any(d => d.S == 0 && Math.Abs(d.U - u) <= 1 && Math.Abs(d.V - v) <= 1);
            int highest = Ring(Storeys - 1) - 1;
            foreach (int du in units ?? new List<int> { doorU })
            {
                // A bracket on a wall with clear air in front of it at least three metres up: the front wall
                // nearest this door first, then the sides near the front. On a terrace, its own house's front.
                var spots = new List<(int U, int V, Dir Out, int Score)>();
                foreach (var (u, v) in Foot.Where(c => Edge(c.U, c.V)))
                    foreach (Dir d in new[] { Dir.North, Dir.East, Dir.West })
                    {
                        Int3 o = d.Vec();
                        if (Foot.Contains((u + o.X, v + o.Z))) continue;
                        if (units != null && (d != Dir.North || Math.Abs(u - du) > 3)) continue;
                        int score = (d == Dir.North ? 0 : 40) + Math.Abs(u - du) + (v - frontV) * 2;
                        spots.Add((u, v, d, score));
                    }
                bool placed = false;
                foreach (var (u, v, d, _) in spots.OrderBy(x => x.Score))
                {
                    Int3 o = d.Vec();
                    for (int y = highest + 4; y >= Math.Max(3 - Lift, 1) && !placed; y--)
                    {
                        if (C.Get(P(u, y, v)).Role != Role.Wall || !BlockInfo.Collides(C.Get(P(u, y, v)).Type)) continue;
                        // Clear air in front, out past any eaves, and nothing overhead within two metres.
                        bool clear = true;
                        for (int k = 1; k <= 3 && clear; k++)
                            for (int dy = -1; dy <= 2 && clear; dy++)
                                if (!Empty(u + o.X * k, y + dy, v + o.Z * k)) clear = false;
                        if (!clear) continue;
                        Put(u + o.X, y, v + o.Z, new Block(BlockType.Pipe, Material.Steel, steel, Role.Decor, d));
                        C.X.Service.Add(new ServicePoint(P(u + o.X, y, v + o.Z), _f.Turn(d)));
                        // The meter box at the foot of the wall below it, clear of every door.
                        if (Lift == 0 && Empty(u + o.X, 0, v + o.Z) && !NearDoor(u + o.X, v + o.Z))
                            Put(u + o.X, 0, v + o.Z, new Block(BlockType.Locker, Material.Steel, new Rgb(0x8c, 0x90, 0x8c), Role.Decor, d));
                        placed = true;
                    }
                    if (placed) break;
                }
                if (placed) continue;
                // Every wall under eaves: a service pole in the front garden at the corner, its top the fixing.
                int fv = Foot.Min(x => x.V);
                var row = Foot.Where(c => c.V == fv).Select(c => c.U).ToList();
                int pv = fv - 3, top = highest + 2;
                int pu = int.MinValue;
                foreach (int cand in new[] { row.Min() - 3, row.Max() + 3, row.Min() - 5, row.Max() + 5 }.OrderBy(x => Math.Abs(x - du)))
                {
                    Int3 at = _f.At(cand, 0, pv);
                    bool offPath = Math.Abs(cand - du) > 2 && Math.Max(Math.Abs(at.X - C.EntranceCell.X), Math.Abs(at.Z - C.EntranceCell.Z)) > 2;
                    if (offPath && Enumerable.Range(-Lift, top + Lift + 3).All(y => Empty(cand, y, pv)) && !NearDoor(cand, pv)) { pu = cand; break; }
                }
                if (pu == int.MinValue) continue;
                for (int y = -Lift; y <= top; y++) Put(pu, y, pv, new Block(BlockType.Pillar, Material.Timber, new Rgb(0x5a, 0x4c, 0x3e), Role.Decor));
                C.X.Service.Add(new ServicePoint(P(pu, top, pv), _f.Turn(Dir.North)));
                if (Lift == 0 && !NearDoor(pu, pv - 1) && Empty(pu, 0, pv - 1)) Put(pu, 0, pv - 1, new Block(BlockType.Locker, Material.Steel, new Rgb(0x8c, 0x90, 0x8c), Role.Decor, Dir.North));
            }
        }
    }

    /// <summary>A carport: a flat roof over the end of the drive on posts, against the house's side or standing on its own.</summary>
    static void Carport(Lot l, Frame f, Hut h, int u0, int v0, int wide, int deep, bool right, HouseStyle s)
    {
        var post = new Block(BlockType.Rib, l.Tech == TechLevel.Low ? Material.Timber : Material.Steel, l.Tech == TechLevel.Low ? new Rgb(0x5a, 0x48, 0x38) : new Rgb(0x6c, 0x70, 0x74), Role.Wall);
        var roof = new Block(BlockType.Cube, l.Tech == TechLevel.Low ? Material.Timber : l.S.RoofM, l.Tech == TechLevel.Low ? s.RoofC : l.S.Roof.Scale(0.9f), Role.Roof);
        int u1 = u0 + wide - 1, v1 = v0 + deep - 1;
        int lift = 0;
        foreach (int u in new[] { u0, u1 })
            foreach (int v in new[] { v0, (v0 + v1) / 2, v1 })
            {
                bool againstHouse = h.Foot.Contains((right ? u0 - 1 : u1 + 1, v)) && u == (right ? u0 : u1);
                if (againstHouse) continue;
                for (int y = 0; y <= 2; y++)
                    if (!l.C.Blocks.ContainsKey(f.At(u, y + lift, v))) l.C.Set(f.At(u, y + lift, v), post);
            }
        // Its roof tucks under the house's eaves, never on them.
        for (int v = v0; v <= v1; v++)
            for (int u = u0; u <= u1; u++)
                if (!l.C.Blocks.ContainsKey(f.At(u, 3, v)) && l.C.Get(f.At(u, 2, v)).Role != Role.Roof) l.C.Set(f.At(u, 3, v), roof);
    }
}
