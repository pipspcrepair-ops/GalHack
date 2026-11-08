#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>The ground round a house: its plot, fence and gate on a street, on a farm or on a city lot.</summary>
public static partial class Homes
{
    static HomeHints Loose(Rng rng) => new((int)(rng.NextU64() >> 33), (Climate)rng.Range(0, 4), rng.Float());

    /// <summary>A house of its street's culture on a roadside lot: set back behind its garden, its drive and path out to the lot's front, fenced.</summary>
    static void HouseLot(Lot l, Rng rng, LayoutHints? hints, string? family)
    {
        HomeHints home = hints?.Home ?? Loose(rng);
        var culture = HomeCulture.Of(HomeBook.Current, home, l.Tech);
        HouseStyle s = HouseStyle.Roll(culture, rng, family ?? home.Family);
        const int Side = 6;
        int front = 10 + 2 * rng.Range(0, 2);
        Built b = Dwelling(l, l.F, s, Side, front);
        // A house on a street owns one side line, its neighbour the next; a house on its own owns both.
        Grounds(l, l.F, b, s, b.U1 + 1 + Side, b.V1 + 8 + 2 * rng.Range(0, 2), hints?.Home == null);
    }

    /// <summary>
    /// The plot round a house and its fence, of the kind its street builds (<see cref="FenceKind"/>), by rule. The
    /// fence follows the lot's own lines only: the front, the side the lot owns (a lot on a street owns its u = 0
    /// side and its neighbour the next, so a line between two lots is fenced once; a lot alone owns both) and the
    /// back where the kind runs right round. Runs meet at a corner on one shared post and never cross; every run
    /// starts and ends on a post (a pier, or the wall or hedge itself), or where it meets something solid; the
    /// only openings are where the path or the drive comes through, a gate post either side and the gate standing
    /// open inside. Panels run along their run from post to post; nothing hangs in the air. The lot's gate is
    /// where the drive (or the path) meets its front.
    /// </summary>
    static void Grounds(Lot l, Frame f, Built b, HouseStyle s, int lotW, int lotD, bool bothSides, bool gate = true)
    {
        Canvas c = l.C;
        Rng r = l.R;
        int gateU = b.DriveU0 is int d0 ? (d0 + b.DriveU1!.Value) / 2 : b.PathU;
        if (gate) c.X.Gate = f.At(gateU, 0, -1);
        int U = lotW - 1, B = lotD - 1;
        HomeNote? note = c.X.Homes.Count > 0 ? c.X.Homes[^1] : null;
        Int3 lo = Int3.Min(f.At(0, 0, 0), f.At(U, 0, B)), hi = Int3.Max(f.At(0, 0, 0), f.At(U, 0, B));
        void Record(FenceNote? fn)
        {
            if (note != null) c.X.Homes[^1] = note with { Fence = fn, FenceKind = s.Fence, LotMin = lo, LotMax = hi };
        }
        FenceKind? k = s.FenceKind;
        if (k == null) { Record(null); return; }
        int tall = k.Body == "edging" ? 1 : k.Tall;
        // The openings in the front line: each path and the drive, joined where they come together, never at a corner.
        var spans = b.Paths.Append(b.PathU).Distinct().Select(x => (A: x, B: x + 1)).ToList();
        if (b.DriveU0 is int a0 && b.DriveU1 is int a1) spans.Add((a0, a1));
        var open = new List<(int A, int B)>();
        foreach (var (sa, sb) in spans.OrderBy(x => x.A))
        {
            int a = Math.Max(3, sa), z = Math.Min(U - 3, sb);
            if (z < a) continue;
            if (open.Count > 0 && a - open[^1].B <= 1) open[^1] = (open[^1].A, Math.Max(open[^1].B, z));
            else open.Add((a, z));
        }
        // The runs: the front broken at the openings, the side or sides, and the back where it goes right round.
        bool enclose = k.Body != "edging" && r.Chance(k.Enclose);
        int sideTo = enclose ? B : Math.Clamp(b.V0 + Math.Max(2, (b.V1 - b.V0) / 2), 3, B - 1);
        static List<(int U, int V)> Line(int u0, int v0, int u1, int v1)
        {
            var cells = new List<(int, int)>();
            int len = Math.Max(Math.Abs(u1 - u0), Math.Abs(v1 - v0));
            for (int t = 0; t <= len; t++) cells.Add((u0 + Math.Sign(u1 - u0) * t, v0 + Math.Sign(v1 - v0) * t));
            return cells;
        }
        var runs = new List<List<(int U, int V)>>();
        var gates = new List<((int U, int V) A, (int U, int V) B)>();
        int from = 0;
        foreach (var (a, z) in open)
        {
            runs.Add(Line(from, 0, a - 1, 0));
            gates.Add(((a - 1, 0), (z + 1, 0)));
            from = z + 1;
        }
        runs.Add(Line(from, 0, U, 0));
        runs.Add(Line(0, 0, 0, sideTo));
        if (bothSides) runs.Add(Line(U, 0, U, sideTo));
        if (enclose) runs.Add(Line(0, B, U, B));
        // A run that meets something solid on or against the lot line (a shed, a wall, earth heaped against a house)
        // stops at it, never squeezing a slot between: split it there.
        bool Solid(int u, int v) => Enumerable.Range(0, tall).Any(y => BlockInfo.Collides(c.Get(f.At(u, y, v)).Type)) || c.Occ.Contains(f.At(u, 0, v)) && !c.Blocks.ContainsKey(f.At(u, 0, v));
        bool Blocked(int u, int v) => Solid(u, v) || Solid(u + 1, v) || Solid(u - 1, v) || Solid(u, v + 1) || Solid(u, v - 1);
        var split = new List<List<(int U, int V)>>();
        foreach (var run in runs)
        {
            var cur = new List<(int U, int V)>();
            foreach (var cell in run)
            {
                if (Blocked(cell.U, cell.V)) { if (cur.Count > 0) split.Add(cur); cur = new(); continue; }
                cur.Add(cell);
            }
            // A run of one cell is a post on its own: between two openings, or between two solid things.
            if (cur.Count > 0) split.Add(cur);
        }
        runs = split;
        // Posts: both ends of every run (a corner shared by two runs is one post), the gate posts, and along each run at the kind's spacing.
        var posts = new HashSet<(int U, int V)>();
        if (k.Body is "panel" or "railing" || k.Body == "wall" && k.Post == "pier")
            foreach (var run in runs)
            {
                posts.Add(run[0]);
                posts.Add(run[^1]);
                for (int i = 1; i < run.Count - 1; i++)
                    if (k.Every > 0 && i % k.Every == 0 && run.Count - 1 - i >= 1) posts.Add(run[i]);
            }
        if (k.Body is not "edging")
            foreach (var (ga, gb) in gates) { posts.Add(ga); posts.Add(gb); }
        // Paints and blocks.
        Rgb body = s.FenceC, post = s.FencePostC;
        var iron = new Rgb(0x2c, 0x2e, 0x30);
        Block Post(int y) => k.Body switch
        {
            "wall" or "railing" => new Block(BlockType.Cube, k.Stuff, body.Scale(0.86f), Role.Wall),
            "hedge" => new Block(BlockType.Pillar, Material.Timber, post, Role.Decor),
            _ => new Block(k.Post == "rib" ? BlockType.Rib : BlockType.Pillar, k.PostStuff, post, Role.Decor),
        };
        float Jit(int u, int y, int v, int spread) => 1f + ((int)(Hash.Cell(f.At(u, y, v), 0xFE7u) % (uint)(2 * spread + 1)) - spread) / 100f;
        Block Body(int u, int y, int v, bool alongU) => k.Body switch
        {
            "wall" => new Block(BlockType.Cube, k.Stuff, body.Scale(Jit(u, y, v, k.Stuff == Material.Rock ? 9 : 3) * (y == tall - 1 ? 0.9f : 1f)), Role.Wall),
            "hedge" => new Block(BlockType.Cube, Material.Plant, body.Scale(Jit(u, y, v, 10)), Role.Decor),
            "railing" => y == 0 ? new Block(BlockType.Cube, k.Stuff, body.Scale(Jit(u, y, v, 3)), Role.Wall)
                : new Block(BlockType.Ladder, Material.Steel, iron, Role.Decor, f.Turn(alongU ? Dir.North : Dir.East)),
            "edging" => new Block(BlockType.Plate, k.Stuff, body.Scale(Jit(u, y, v, 4)), Role.Floor),
            // Boards, sheet or screen: a corrugated sheet's ribs in alternate shades.
            _ => new Block(BlockType.Pane, k.Stuff, k.Stuff == Material.Rust || k.Stuff == Material.Steel && body.R < 0xa0 ? body.Scale((u + v) % 2 == 0 ? 1f : 0.82f) : body.Scale(Jit(u, y, v, 3)), Role.Decor),
        };
        foreach (var run in runs)
        {
            bool alongU = run[0].V == run[^1].V;
            foreach (var (u, v) in run)
            {
                bool isPost = posts.Contains((u, v));
                for (int y = 0; y < tall; y++)
                {
                    Int3 p = f.At(u, y, v);
                    if (k.Body == "edging") { if (!c.Blocks.ContainsKey(p)) c.Blocks[p] = Body(u, y, v, alongU); continue; }
                    c.Set(p, isPost ? Post(y) : Body(u, y, v, alongU));
                }
                // A pier stands a course over its wall, a hedge's gate post as tall as the hedge.
                if (isPost && k.Body == "wall") c.Set(f.At(u, tall, v), Post(tall));
            }
        }
        // A panel the mesher would turn across its run (something solid beside it) becomes a post, so every panel runs along its run.
        if (k.Body == "panel")
            foreach (var run in runs)
            {
                bool alongX = f.At(run[0].U, 0, run[0].V).X != f.At(run[^1].U, 0, run[^1].V).X;
                foreach (var (u, v) in run)
                    for (int y = 0; y < tall; y++)
                    {
                        Int3 p = f.At(u, y, v);
                        if (c.Get(p).Type != BlockType.Pane) continue;
                        bool meshX = !c.Get(p + Dir.East.Vec()).IsAir || !c.Get(p + Dir.West.Vec()).IsAir;
                        if (meshX == alongX) continue;
                        for (int yy = 0; yy < tall; yy++) c.Set(f.At(u, yy, v), Post(yy));
                        posts.Add((u, v));
                        break;
                    }
            }
        // The gates stand open inside the lot: a barred leaf hung on each gate post, as tall as the fence allows.
        var leaves = new List<List<(int U, int V)>>();
        if (k.Body is not "edging")
            foreach (var (ga, gb) in gates)
            {
                int width = gb.U - ga.U - 1;
                foreach (var (gp, half) in width <= 3 ? new[] { (ga, width) } : new[] { (ga, width / 2), (gb, width - width / 2) })
                {
                    int len = Math.Min(half, 3);
                    var leaf = Enumerable.Range(1, len).Select(d => (gp.U, d)).ToList();
                    if (leaf.Any(x => Enumerable.Range(0, Math.Min(tall, 2)).Any(y => c.Blocks.TryGetValue(f.At(x.Item1, y, x.d), out Block o) && o.Type != BlockType.Plate))) continue;
                    foreach (var (u, v) in leaf)
                        for (int y = 0; y < Math.Min(tall, 2); y++)
                        {
                            Int3 p = f.At(u, y, v);
                            c.Blocks[p] = new Block(BlockType.Ladder, k.Body is "panel" ? k.PostStuff : Material.Steel, k.Body is "panel" ? post : iron, Role.Decor, f.Turn(Dir.East));
                        }
                    leaves.Add(leaf);
                }
            }
        List<Int3> World(IEnumerable<(int U, int V)> cells) => cells.Select(x => f.At(x.U, 0, x.V)).ToList();
        Record(new FenceNote(k.Id, k.Body, tall, runs.Select(World).ToList(), World(posts).ToHashSet(), gates.Select(g => (f.At(g.A.U, 0, g.A.V), f.At(g.B.U, 0, g.B.V))).ToList(),
            open.Select(o => World(Line(o.A, 0, o.B, 0))).ToList(), bothSides)
        { Leaves = leaves.Select(World).ToList(), SideLines = runs.Where(x => x[0].U == x[^1].U && x.Any(y => y.V > 0)).Select(x => x[0].U).Distinct().Count() });
    }

    /// <summary>A farm's house: a house of the farm's own people, no garage (the farm has its sheds), its door on the yard.</summary>
    static (int W, int D, int DoorU) House(Lot l, Frame f, string plan)
    {
        var culture = HomeCulture.Of(HomeBook.Current, Loose(l.R), l.Tech);
        string family = plan switch { "bungalow" => "bungalow", "cottage" => "cottage", "prefab" => "modern", _ => "suburban" };
        if (HomeBook.Current.Find(family) == null) family = HomeBook.Current.Families.FirstOrDefault(x => !x.Odd)?.Id ?? family;
        HouseStyle s = HouseStyle.Roll(culture, l.R, family);
        s.Garage = "none";
        s.Fence = "none";
        s.FenceKind = null;
        if (s.Plan is "court" or "row" or "tower" or "round" or "oval") s.Plan = "rect";
        Built b = Dwelling(l, f, s, 0, 4);
        return (b.U1 - b.U0 + 1, b.V1 - b.V0 + 1, b.DoorU);
    }

    /// <summary>
    /// A city lot's house or row of terraced houses (<see cref="CityBlocks"/>): the city's culture and the lot's
    /// size choose it, set back from the lot line with its drive to the street. Returns where its drive and path
    /// cross the lot line, and its fence, of the kind its neighbourhood builds, to draw once the lot's retaining
    /// walls stand (on the lot's left line, and its right on its row's last lot, as every city lot).
    /// </summary>
    public static (List<int> Paths, int? DriveU0, int? DriveU1, Action Fence) CityHouse(Plot p, CityLot lot, bool terrace)
    {
        int q = (int)p.Turn(Dir.North);
        var frame = new Frame(q, q is 1 or 2 ? -1 : 0, q is 2 or 3 ? -1 : 0);
        var l = new Lot { C = p.C, R = p.R, Tech = p.Tech, S = Stuff.For(p.Tech, p.R), F = frame };
        p.C.X.PlainDoor = l.S.Door;
        HomeHints home = lot.Home ?? new HomeHints(lot.Style * 7 + lot.W, Climate.Temperate, 0.5f);
        var culture = HomeCulture.Of(HomeBook.Current, home, p.Tech);
        int lotW = 2 * lot.W, lotD = 2 * lot.D;
        HouseStyle s;
        int maxW = lotW - 6;
        if (terrace)
        {
            s = HouseStyle.Roll(culture, p.R, "terrace");
            if (s.Plan != "row") { s.Plan = "row"; s.Roof = "gable"; }
            // As many houses as the lot holds, a cell of fence each side.
            while (s.UnitW > 8 && (maxW - 1) / s.UnitW < 2) s.UnitW -= 2;
            s.Units = Math.Max(1, (maxW - 1) / s.UnitW);
            s.D = Math.Min(s.D, lotD - 12);
            s.Storeys = Math.Clamp(lot.Storeys, 2, 3);
        }
        else
        {
            s = HouseStyle.Roll(culture, p.R);
            for (int i = 0; i < 12 && (s.Family.Id == "terrace" || s.Plan == "court" && lotW < 30 || s.Plan == "tower"); i++) s = HouseStyle.Roll(culture, p.R);
            if (s.Family.Id == "terrace" || s.Plan is "court" or "tower" or "row") { s.Plan = "rect"; s.Units = 1; }
            s.D = Math.Min(s.D, lotD - 14);
            // A city lot has no room round a house for earth heaped against it, nor for eaves wider than its side yard.
            s.Berm = false;
            s.Over = Math.Min(s.Over, 2);
            if (s.Roof == "aframe") s.Roof = "gable";
            if (s.Roof == "turf" && s.Plan is "round" or "oval") s.Plan = "rect";
            if (s.Plan is "ell" or "tee") s.D = Math.Max(10, Math.Min(s.D, lotD - 26));
            // Fitted to the lot: a narrower house, a side wing folded back to an ell, the garage brought into the house or given up.
            if (s.Plan == "wing" && s.W + 8 > maxW) s.Plan = "ell";
            s.W = Math.Min(s.W, Ev(maxW));
            int beside = s.Garage switch { "attached" => s.Double ? 9 : 5, "carport" => s.Double ? 10 : 6, "detached" => (s.Double ? 10 : 6) + 2 * s.Over + 2, _ => 0 };
            if (beside > 0 && s.W + beside > maxW)
            {
                s.Double = false;
                beside = s.Garage switch { "attached" => 5, "carport" => 6, _ => 8 + 2 * s.Over };
                if (s.W + beside > maxW) s.Garage = s.W >= 12 && l.Tech != TechLevel.Low && s.Plan is "rect" or "ell" ? "front" : "none";
            }
        }
        int front = Math.Min(8 + 2 * p.R.Range(0, 2), lotD / 4);
        Built b = Dwelling(l, frame, s, 3, front, maxW);
        p.C.X.Gate = frame.At(b.DriveU0 is int d0 ? (d0 + b.DriveU1!.Value) / 2 : b.PathU, 0, 0);
        p.C.VaultModule = l.Loot;
        return (b.Paths.Append(b.PathU).Distinct().ToList(), b.DriveU0, b.DriveU1, () => Grounds(l, frame, b, s, lotW, lotD, lot.EndRight, gate: false));
    }

    /// <summary>
    /// A house on a water tower: a concrete shaft of storeys, a flight up each switching sides, and on top a
    /// wider house of the street's own cladding and roof, out over the shaft on every side.
    /// </summary>
    static Built Tower(Lot l, Frame lot, HouseStyle s, int side, int front)
    {
        Rng r = l.R;
        l.S = StuffOf(s, l.Tech, r);
        int storeys = Math.Clamp(s.Storeys, 3, 4), tw = 8, top = 12 + 2 * r.Range(0, 2), off = (top - tw) / 2, margin = off + 2;
        Frame f = lot.Shift(side + margin, front + margin);
        var shaft = new Hut(l, f) { Storeys = storeys };
        var concrete = new Rgb(0x9c, 0x98, 0x90);
        shaft.Skin = (u, y, v) => new Block(BlockType.Cube, Material.Concrete, concrete.Scale((y % 4 == 3 ? 0.88f : 1f) * (1f + (Hash.Cell(new Int3(u, y, v), 3) % 6) / 100f)), Role.Wall);
        shaft.Wings.Add(new Wing(0, 0, tw - 1, tw - 1));
        var halls = new List<Room>();
        for (int st = 0; st < storeys; st++) halls.Add(shaft.Add(st == 0 ? "hall" : "storage", st, 1, 1, tw - 2, tw - 2, ModuleKind.Hall));
        shaft.Raise();
        var flights = new List<int>();
        for (int st = 0; st + 1 < storeys; st++) flights.Add(shaft.Stair(st % 2 == 0 ? tw - 2 : 1, 2, st));
        // The house on top.
        var house = new Hut(l, f) { Storeys = 1, Lift = shaft.Walk(storeys) };
        house.Skin = Cladding(s, house, l.S, l.Tech);
        house.Wings.Add(new Wing(-off, -off, tw - 1 + off, tw - 1 + off, r.Chance(0.5f)));
        int last = storeys - 1, col = last % 2 == 0 ? tw - 2 : 1;
        // Two rooms side by side, the wall between them clear of where the stair comes up.
        int split = col == 1 ? 4 : 3;
        Room mess = col == 1 ? house.Add("mess", 0, -off + 1, -off + 1, split - 1, tw - 2 + off, ModuleKind.Hall) : house.Add("mess", 0, split + 1, -off + 1, tw - 2 + off, tw - 2 + off, ModuleKind.Hall);
        Room kitchen = col == 1 ? house.Add("farm-kitchen", 0, split + 1, -off + 1, tw - 2 + off, tw - 2 + off) : house.Add("farm-kitchen", 0, -off + 1, -off + 1, split - 1, tw - 2 + off);
        house.Raise();
        // The last flight comes up through the house's floor, the room keeping clear of it.
        flights.Add(shaft.Stair(col, 2, last));
        for (int v = 2; v <= 5; v++) house.Mark(col, v, false, 0, 1);
        house.Reserve(col, 2, 4);
        house.Gap(split, tw - 2 + r.Range(0, off), false, 0, DoorSize.Wide, DoorSize.Tall);
        shaft.Gap(tw / 2 - 1, 0, true, 0, DoorSize.Wide, DoorSize.Tall);
        shaft.Modules();
        house.Modules();
        for (int st = 0; st + 1 < storeys; st++)
        {
            l.C.Link(halls[st].Module, flights[st]);
            l.C.Link(flights[st], halls[st + 1].Module);
            l.C.X.Levels.Add(new LevelInfo { Storey = st + 1, WalkY = shaft.Walk(st + 1), Name = $"tower {st + 2}" });
        }
        l.C.Link(halls[last].Module, flights[^1]);
        l.C.Link(flights[^1], mess.Module);
        l.C.X.Levels.Add(new LevelInfo { Storey = storeys, WalkY = house.Lift, Name = "house" });
        shaft.Windows(0.6f);
        house.Windows(0.9f);
        if (s.Shutters) house.Shutters(s.ShutterC);
        switch (s.Roof)
        {
            case "flat": house.FlatRoof(1, 1); break;
            case "gable": house.Prisms(house.Wings.Select(w => (w, "gable")).ToList(), Math.Min(s.Over, 2), new List<Hut> { house }); break;
            default: Hut.Hipped(new List<Hut> { house }, Math.Min(s.Over, 2), false); break;
        }
        // A rail round the edge of the house's floor where it stands out over the shaft.
        house.Furnish();
        house.Loot(kitchen);
        shaft.Box();
        house.Box();
        l.Approach(f, tw / 2 - 1, -3);
        shaft.Headroom(tw / 2 - 1, 0, Dir.North, 0);
        shaft.FrontDoor(tw / 2 - 1, 0, halls[0], DoorSize.Tall);
        l.C.Link(l.C.EntranceModule, halls[0].Module);
        // The drop comes in on a bracket up the shaft, not to the house up top.
        shaft.Service(tw / 2 - 1, 0, null);
        for (int v = -(front + margin); v < 0; v++)
            for (int k = 0; k < 2; k++) l.Plate(f, tw / 2 - 1 + k, v, l.S.Base.Scale(1.15f), Role.Floor);
        l.C.X.Homes.Add(new HomeNote(s.Family.Id, $"tower/{storeys}/{top}/{s.Roof}/{s.Wall}", storeys + 1, s.Roof, "none", "", 0, 0, default, Dir.North, false) { Exits = shaft.ExitCells().ToList() });
        return new Built { U0 = side, U1 = side + 2 * margin + tw - 1, V0 = front, V1 = front + 2 * margin + tw - 1, DoorU = side + margin + tw / 2 - 1, DoorV = front + margin, PathU = side + margin + tw / 2 - 1 };
    }
}
