#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// What stands at a high-tech spaceport (data/place-types.txt, PlaceTypes.Ports), each a place of its own on the player's
/// 2 m lattice at the size its sketch rolled, its front (k 0) the way it serves, and every round thing round:
///   a hub: the domed control tower on its roundabout: a round base and a glazed tier stepped in over it, a slim shaft
///     climbed by a ladder, a glazed saucer of a cab under a dome;
///   an authority building: a broad podium and a tower stepping in storey by storey to its glazed crown, its rooms round a
///     ladder, and three wings, halls with a craft's door at their ends and their roofs terraced down, off its sides and back;
///   a dome: a startown hut, one round room under a dome, furnished by what it was;
///   a pad: a recessed pad's blast wall, a ring from the pit's floor to a block over the field, cut where its ramp comes down;
///   a siding: a rail track on its ballast out from a freight hall.
/// </summary>
public static class Ports
{
    public static void Compose(Canvas c, Rng rng, LayoutHints? hints, TechLevel tech, SitePart part)
    {
        var p = new Plot(c, rng, tech, hints?.Front ?? Dir.North);
        Civic.Wear(p);
        c.Begin();
        switch (part.Arg("what"))
        {
            case "hub": Hub(p, part); break;
            case "authority": Authority(p, part); break;
            case "dome": Dome(p, part); break;
            case "pad": Pad(p, part); break;
            case "siding": Siding(p, part); break;
            default: throw new ArgumentException($"no port work '{part.Arg("what")}'");
        }
        p.Finish();
    }

    const int P = 3, H = 2;

    static Block Solid(Stuff2 s, Role r) => Plot.Cube(s, r);

    /// <summary>The cells of a round floor (blocks within r of a middle), as rooms want them kept.</summary>
    static Func<Int3, bool> Within(Plot p, double c, double r) => cell => p.BlockAt(cell) is var (i, k) && Plot.In(c, c, r, i, k);

    /// <summary>Plates over a round floor at level j, inside r blocks.</summary>
    static void Floor(Plot p, double c, double r, int j)
    {
        int n = (int)Math.Ceiling(c + r);
        for (int k = (int)(c - r) - 1; k <= n; k++)
            for (int i = (int)(c - r) - 1; i <= n; i++)
                if (Plot.In(c, c, r, i, k)) p.Pave(i, k, i, k, p.F.Floor, y: 2 * j);
    }

    // ---- the hub ----------------------------------------------------------------------------------

    /// <summary>
    /// A round base a storey high with a mouth at its front, a glazed tier stepped in over it; a slim shaft of storeys over
    /// that, each a room, one ladder up them all; the cab a glazed saucer wider than the shaft, a faceted dome over it, its
    /// consoles round the glass.
    /// </summary>
    static void Hub(Plot p, SitePart part)
    {
        Canvas c = p.C;
        Fab f = p.F;
        double mid = part.Int("fl") / 4.0;
        double rb = Math.Max(4, part.Num("r", 12) / 2.0 - 0.5), rs = Math.Max(2.5, rb * 0.42), rc = Math.Max(rs + 2, part.Num("cab", 9) / 2.0 - 0.5), r1 = Math.Max(rs + 2, rb * 0.7);
        int storeys = Math.Max(2, (int)Math.Round((part.Num("h", 32) - 16) / 6.0));
        int m = (int)Math.Floor(mid);
        p.Approach(m, (int)Math.Floor(mid - rb) - 2, Dir.North);
        // The base: one tall storey, its roof a terrace round an upper tier stepped in, the waiting hall, glazed all round.
        p.Drum(mid, mid, rb, 0, H, f.Wall, j => j == 1 ? new Stuff2(Material.Glass, f.Glass) : f.Wall);
        p.Disc(mid, mid, rb, H + 1, Solid(f.Roof, Role.Roof));
        Floor(p, mid, rb - 1, 0);
        int lobby = p.Room(ModuleKind.Hall, "concourse", 0, (int)(mid - rb), 0, (int)(mid - rb), (int)(mid + rb), H, (int)(mid + rb), Within(p, mid, rb - 1));
        p.Drum(mid, mid, r1, H + 2, 2 * H + 1, f.Wall, j => j == 2 * H + 1 ? new Stuff2(Material.Glass, f.Glass) : f.Wall);
        p.Disc(mid, mid, r1, 2 * H + 2, Solid(f.Roof, Role.Roof));
        Floor(p, mid, r1 - 1, H + 2);
        int tier = p.Room(ModuleKind.Hall, "waiting hall", 1, (int)(mid - r1), H + 2, (int)(mid - r1), (int)(mid + r1), 2 * H + 1, (int)(mid + r1), Within(p, mid, r1 - 1));
        c.Link(lobby, tier);
        p.Level(1, H + 2, "waiting hall");
        p.Lamp(2 * m, 2 * (2 * H + 1) + 1, 2 * m + 3, tier, 1.2f, 14f);
        p.Mouth(m - 1, 0, (int)Math.Floor(mid - rb) - 1, m + 1, 1, (int)Math.Floor(mid - rb) + 1, lobby, p.Yard);
        p.Mouth(m - 1, 0, (int)Math.Floor(mid + rb) - 1, m + 1, 1, (int)Math.Floor(mid + rb) + 1, lobby, p.Yard);
        for (int a = 0; a < 6; a++)
        {
            double t = a * Math.Tau / 6;
            int u = (int)Math.Round(2 * (mid + (rb - 2.5) * Math.Cos(t))), v = (int)Math.Round(2 * (mid + (rb - 2.5) * Math.Sin(t)));
            p.Prop(u, 0, v, a % 2 == 0 ? BlockType.Bench : BlockType.Table, Material.Steel, f.Panel.C);
        }
        p.Lamp(2 * m, 2 * H + 1, 2 * m, lobby, 1.6f, 18f);
        p.Spawn(2 * m + 3, 0, 2 * m + 3, lobby);
        // The shaft: storeys of 6 m, a room each.
        int below = tier, j0 = 2 * H + 3;
        for (int s = 0; s < storeys; s++)
        {
            int j = j0 + s * P;
            p.Drum(mid, mid, rs, j, j + H - 1, f.Wall, jj => jj == j + 1 ? new Stuff2(Material.Glass, f.Glass) : f.Wall);
            p.Disc(mid, mid, rs, j + H, Solid(f.Floor, Role.Floor));
            int room = p.Room(ModuleKind.Room, p.R.Pick(new[] { "equipment", "radio room", "office", "rest room" }), s + 2, (int)(mid - rs), j, (int)(mid - rs), (int)(mid + rs), j + H - 1, (int)(mid + rs), Within(p, mid, rs - 1));
            c.Link(below, room);
            p.Level(s + 2, j, "floor " + (s + 2));
            p.Lamp(2 * m + 1, 2 * j + 3, 2 * m + 1, room, 0.8f, 8f);
            below = room;
        }
        // The cab: a saucer over the shaft, glazed all round, its dome over it.
        int jc = j0 + storeys * P;
        p.Disc(mid, mid, rc, jc - 1, Solid(f.Roof, Role.Floor));
        p.Drum(mid, mid, rc, jc, jc + H - 1, new Stuff2(Material.Glass, f.Glass), jj => jj == jc ? f.Wall : new Stuff2(Material.Glass, f.Glass));
        p.Cap(mid, mid, rc, jc + H, f.Roof);
        Floor(p, mid, rc - 1, jc);
        int cab = p.Room(ModuleKind.Vault, "control cab", storeys + 2, (int)(mid - rc), jc, (int)(mid - rc), (int)(mid + rc), jc + H - 1, (int)(mid + rc), Within(p, mid, rc - 1));
        c.Link(below, cab);
        p.Level(storeys + 2, jc, "cab");
        for (int a = 0; a < 8; a++)
        {
            double t = a * Math.Tau / 8;
            p.Prop((int)Math.Round(2 * (mid + (rc - 1.5) * Math.Cos(t))), 2 * jc, (int)Math.Round(2 * (mid + (rc - 1.5) * Math.Sin(t))), BlockType.Machine, Material.Steel, f.Steel.C);
        }
        p.Terminal(2 * m + 3, 2 * jc, 2 * m, cab);
        p.Cache(2 * m - 3, 2 * jc, 2 * m, cab);
        p.Lamp(2 * m, 2 * jc + 3, 2 * m + 2, cab, 1f, 12f);
        c.VaultModule = c.CoreModule = cab;
        c.AddSlot(SlotKind.Core, p.Cell(2 * m + 1, 2 * jc, 2 * m + 1), cab);
        // One ladder from the lobby to the cab, against the shaft's wall.
        p.Ladder(2 * m, 0, 2 * jc - 1, 2 * (int)Math.Floor(mid - rs) + 3, Dir.North);
    }

    // ---- the authority building -------------------------------------------------------------------

    /// <summary>
    /// The reference's three-winged complex, dominating its port. The core round the middle of its box: a broad podium
    /// whose lobby is a hall three storeys high, open to the front and to each wing; over it a tower of 6 m storeys
    /// stepping in toward its top, glazed in bands, a room each round a ladder up the middle; on top the crown, a block
    /// wider than the storey under it and glazed all round (flight operations), a mast over it. A wing off each side and
    /// the back: a hall the width the type rolled out to the box's edge, its roof stepping down in terraces from twice the
    /// podium's height at the core to two storeys at its end, a craft's door across its end.
    /// </summary>
    static void Authority(Plot p, SitePart part)
    {
        Canvas c = p.C;
        Fab f = p.F;
        int L = part.Int("fl") / 2;
        double mid = L / 2.0;
        int m = (int)Math.Floor(mid);
        double rb = Math.Max(8, part.Num("r", 20) / 2.0 - 0.5);
        const int Podium = 6, Steps = 3;
        // Storeys of the tower over the podium, under the crown and its mast.
        int hw = Math.Max(3, part.Int("span", 26) / 4), storeys = Math.Clamp((int)Math.Round((part.Num("h", 60) / 2.0 - Podium - 5) / P), 3, 14);
        int[] tops = { Podium + 4, (Podium + 4 + 5) / 2, 5 };
        var glass = new Stuff2(Material.Glass, f.Glass);
        p.Approach(m, (int)Math.Floor(mid - rb) - 2, Dir.North);
        // The wings first: their walls run up to the core's.
        var wings = new List<int>();
        string[] uses = { "freight terminal", "ship services", "repair hall" };
        p.R.Shuffle(uses);
        for (int w = 0; w < 3; w++)
        {
            // Left (-i), right (+i), back (+k); the hall from where its side walls meet the core to the box's edge.
            int meet = (int)Math.Floor(mid + Math.Sqrt(Math.Max(0, rb * rb - (hw + 0.5) * (hw + 0.5))));
            (int i0, int k0, int i1, int k1) = w switch
            {
                0 => (0, m - hw, L - 1 - meet, m + hw),
                1 => (meet, m - hw, L - 1, m + hw),
                _ => (m - hw, meet, m + hw, L - 1),
            };
            bool alongI = w < 2;
            int length = alongI ? i1 - i0 : k1 - k0;
            // Its roof by how far out from the core: tall at the core, a terrace down at each third.
            int Top(int i, int k) => tops[Math.Min(Steps - 1, (w == 0 ? i1 - i : w == 1 ? i - i0 : k - k0) * Steps / (length + 1))];
            for (int k = k0; k <= k1; k++)
                for (int i = i0; i <= i1; i++)
                {
                    if (Plot.In(mid, mid, rb, i, k))
                    {
                        // Over the podium's rim, the wing's end wall up to its roof.
                        if (!Plot.In(mid, mid, rb - 1, i, k)) p.Fill(i, Podium + 1, k, i, tops[0], k, Solid(f.Wall, Role.Wall));
                        continue;
                    }
                    int top = Top(i, k), inward = Top(w == 0 ? i + 1 : w == 1 ? i - 1 : i, w == 2 ? k - 1 : k);
                    bool side = alongI ? k == k0 || k == k1 : i == i0 || i == i1, end = alongI ? (w == 0 ? i == i0 : i == i1) : k == k1;
                    if (side || end) p.Fill(i, 0, k, i, top - 1, k, Solid(f.Wall, Role.Wall));
                    p.Fill(i, top, k, i, top, k, Solid(f.Roof, Role.Roof));
                    // The riser up to the next terrace in, standing on this one's roof.
                    if (inward > top) p.Fill(i, top + 1, k, i, inward, k, Solid(f.Wall, Role.Wall));
                }
            p.Pave(i0 + 1, k0 + 1, i1 - 1, k1 - 1, f.Floor);
            int hall = p.Room(ModuleKind.Hall, uses[w], 0, i0 + 1, 0, k0 + 1, i1 - 1, tops[0] - 1, k1 - 1,
                cell => p.BlockAt(cell) is var (bi, bk) && !Plot.In(mid, mid, rb, bi, bk) && cell.Y < 2 * Top(bi, bk));
            wings.Add(hall);
            // A craft's door across its end, two thirds its width; benches, crates and machines down its sides.
            int d = Math.Max(2, hw * 2 / 3);
            if (alongI) p.Mouth(w == 0 ? i0 : i1, 0, m - d, w == 0 ? i0 : i1, tops[^1] - 2, m + d, hall, p.Yard);
            else p.Mouth(m - d, 0, k1, m + d, tops[^1] - 2, k1, hall, p.Yard);
            for (int a = 2; a < length - 2; a += 2)
                foreach (int s in new[] { -1, 1 })
                {
                    int pick = p.R.Range(0, 4);
                    if (pick == 0) continue;
                    int u = alongI ? 2 * (i0 + a) : 2 * (m + s * (hw - 1)) + (s > 0 ? 1 : 0), v = alongI ? 2 * (m + s * (hw - 1)) + (s > 0 ? 1 : 0) : 2 * (k0 + a);
                    p.Prop(u, 0, v, pick == 1 ? BlockType.Bench : pick == 2 ? BlockType.Crate : BlockType.Machine, Material.Steel, pick == 2 ? f.Rust.C : f.Steel.C);
                }
            int li = alongI ? (i0 + i1) / 2 : m, lk = alongI ? m : (k0 + k1) / 2;
            p.Lamp(2 * li, 2 * Top(li, lk) - 1, 2 * lk, hall, 1.8f, 20f);
            p.Weathers(i0 - 1, 0, k0 - 1, i1 + 1, tops[0] + 1, k1 + 1, tops[^1]);
        }
        // The core: the lobby in the podium, the tower's storeys over it stepping in to a little over half its width, the crown.
        double rTop = Math.Max(5, rb * 0.55), step = (rb - 2 - rTop) / Math.Max(1, storeys - 1);
        int below = -1, jTop = 0;
        for (int s = 0; s <= storeys + 1; s++)
        {
            bool crown = s == storeys + 1;
            double r = s == 0 ? rb : crown ? rTop + 1 : rb - 2 - (s - 1) * step;
            int j = s == 0 ? 0 : Podium + 1 + (s - 1) * P, h = s == 0 ? Podium : H;
            jTop = j;
            // Glazed in a band at the top of each storey; the crown all glass.
            p.Drum(mid, mid, r, j, j + h - 1, f.Wall, jj => crown || jj == j + h - 1 ? glass : f.Wall);
            p.Disc(mid, mid, r, j + h, Solid(crown ? f.Roof : f.Floor, crown ? Role.Roof : Role.Floor));
            Floor(p, mid, r - 1, j);
            string what = s == 0 ? "lobby" : crown ? "flight operations" : p.R.Pick(new[] { "offices", "port housing", "customs", "medical", "beacon room" });
            int room = p.Room(crown ? ModuleKind.Vault : s == 0 ? ModuleKind.Hall : ModuleKind.Room, what, s, (int)(mid - r), j, (int)(mid - r), (int)(mid + r), j + h - 1, (int)(mid + r), Within(p, mid, r - 1));
            if (s == 0)
            {
                p.Mouth(m - 2, 0, (int)Math.Floor(mid - rb) - 1, m + 2, 2, (int)Math.Floor(mid - rb) + 1, room, p.Yard);
                int edge = (int)Math.Floor(mid + rb - 1);
                p.Mouth(L - 1 - edge - 1, 0, m - 2, L - 1 - edge + 1, 2, m + 2, room, wings[0]);
                p.Mouth(edge - 1, 0, m - 2, edge + 1, 2, m + 2, room, wings[1]);
                p.Mouth(m - 2, 0, edge - 1, m + 2, 2, edge + 1, room, wings[2]);
                p.Spawn(2 * m + 4, 0, 2 * m + 4, room);
            }
            else
            {
                c.Link(below, room);
                p.Level(s, j, crown ? "operations" : "floor " + s);
                for (int a = 0; a < 6; a++)
                {
                    double t = a * Math.Tau / 6 + 0.5;
                    p.Prop((int)Math.Round(2 * (mid + (r - 2) * Math.Cos(t))), 2 * j, (int)Math.Round(2 * (mid + (r - 2) * Math.Sin(t))), crown ? BlockType.Machine : a % 2 == 0 ? BlockType.Table : BlockType.Shelf, Material.Steel, f.Panel.C);
                }
            }
            p.Lamp(2 * m + 1, 2 * (j + h) - 1, 2 * m + 5, room, 1.2f, 14f);
            if (crown)
            {
                c.VaultModule = c.CoreModule = room;
                c.AddSlot(SlotKind.Core, p.Cell(2 * m + 4, 2 * j, 2 * m), room);
                p.Terminal(2 * m - 4, 2 * j, 2 * m, room);
                p.Cache(2 * m, 2 * j, 2 * m - 4, room);
                // The mast over it, a beacon on its tip.
                p.Fill(m, j + h + 1, m, m, j + h + 4, m, Solid(f.Steel, Role.Decor));
                p.Put(2 * m, 2 * (j + h + 5), 2 * m, new Block(BlockType.Cube, Material.Lumen, f.Emergency, Role.Light));
            }
            below = room;
        }
        // One ladder from the lobby to the top, up the core's middle.
        p.Ladder(2 * m, 0, 2 * jTop - 1, 2 * m - 2, Dir.North);
        p.Weathers((int)(mid - rb) - 1, 0, (int)(mid - rb) - 1, (int)(mid + rb) + 1, jTop + H + 1, (int)(mid + rb) + 1, H);
    }

    // ---- a startown hut ---------------------------------------------------------------------------

    /// <summary>One round room under a faceted dome, a door at its front, furnished by what it was.</summary>
    static void Dome(Plot p, SitePart part)
    {
        Fab f = p.F;
        double mid = part.Int("fl") / 4.0, r = mid - 0.5;
        int m = (int)Math.Floor(mid), h = 1;
        p.Approach(m, (int)Math.Floor(mid - r) - 2, Dir.North);
        p.Drum(mid, mid, r, 0, h, f.Wall);
        p.Cap(mid, mid, r, h + 1, f.Roof);
        Floor(p, mid, r - 1, 0);
        string what = p.R.Pick(new[] { "shop", "cafe", "office", "staff", "lounge", "archive" });
        int room = p.Room(ModuleKind.Room, what, 0, (int)(mid - r), 0, (int)(mid - r), (int)(mid + r), h, (int)(mid + r), Within(p, mid, r - 1));
        p.Mouth(m, 0, (int)Math.Floor(mid - r), m, 0, (int)Math.Floor(mid - r) + 1, room, p.Yard);
        int q = Math.Max(1, (int)Math.Floor(r * 0.6));
        Airfield.Furnish(p, what, m - q + 1, m - q + 2, m + q - 1, m + q - 1, 0, room, false);
        p.Weathers((int)(mid - r) - 1, 0, (int)(mid - r) - 1, (int)(mid + r) + 1, h + (int)r + 2, (int)(mid + r) + 1, h + 1);
    }

    // ---- a recessed pad's wall ---------------------------------------------------------------------

    /// <summary>
    /// The ring round a recessed pad: a block thick, from the pit's floor (the piece's level) to a block over the field,
    /// round the pit's edge, cut where the ramp comes down (its way, gu gv, a hundredth of a step in the piece's frame);
    /// lights round its top; the way in down the ramp.
    /// </summary>
    static void Pad(Plot p, SitePart part)
    {
        Fab f = p.F;
        double mid = part.Int("fl") / 2.0, rim = part.Num("r", 26);
        int top = part.Int("sink", 4) + 1;
        double gu = part.Num("gu", 0) / 100.0, gv = part.Num("gv", -100) / 100.0, half = Math.Atan2(part.Num("ramp", 12) / 2.0 + 1.0, rim);
        // The cut: cells whose bearing from the middle is within the ramp's half width of its way.
        bool Cut(Int3 cell)
        {
            var (i, k) = p.BlockAt(cell);
            double du = 2 * i + 1 - mid, dv = 2 * k + 1 - mid;
            return Math.Abs(Math.Atan2(du * gv - dv * gu, du * gu + dv * gv)) < half;
        }
        var (x, z) = p.At(mid, mid);
        Round.Lay(p.C, x, z, 0, top, _ => (rim, rim - 2), y => y == top ? new Block(BlockType.Cube, f.Plinth.M, f.Hazard, Role.Wall) : Solid(f.Plinth, Role.Wall), Cut);
        // The way in: at the ramp's foot, inside the cut.
        int fu = (int)Math.Round(mid + gu * (rim - 4)), fv = (int)Math.Round(mid + gv * (rim - 4));
        p.Approach(fu / 2, fv / 2, Math.Abs(gu) >= Math.Abs(gv) ? (gu > 0 ? Dir.East : Dir.West) : (gv > 0 ? Dir.South : Dir.North));
        // Lights on its top every 8 m or so, clear of the cut.
        int lights = Math.Max(6, (int)(Math.Tau * rim / 8));
        for (int a = 0; a < lights; a++)
        {
            double t = a * Math.Tau / lights;
            int u = (int)Math.Floor(mid + (rim - 1) * Math.Cos(t)), v = (int)Math.Floor(mid + (rim - 1) * Math.Sin(t));
            Int3 cell = p.Cell(u, top + 1, v);
            if (!Cut(cell) && p.Get(u, top, v).Type == BlockType.Cube) p.Put(u, top + 1, v, new Block(BlockType.Pillar, Material.Lumen, f.Lamp, Role.Light));
        }
    }

    // ---- a rail siding ------------------------------------------------------------------------------

    /// <summary>Two rails on its length (its depth, back from its front), on ballast the ground lays under them.</summary>
    static void Siding(Plot p, SitePart part)
    {
        Fab f = p.F;
        int w = part.Int("fl"), len = part.Int("fd"), a = Math.Max(0, w / 2 - 2);
        p.Approach(w / 4, -1, Dir.North);
        for (int v = 0; v < len; v++)
        {
            p.Lay(a + 1, 0, v, new Block(BlockType.Quarter, Material.Steel, f.Steel.C, Role.Decor, Dir.West));
            p.Lay(a + 2, 0, v, new Block(BlockType.Quarter, Material.Steel, f.Steel.C, Role.Decor, Dir.East));
        }
    }
}
