#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// What stands at a high-tech spaceport (data/place-types.txt, PlaceTypes.Ports), each a place of its own on the player's
/// 2 m lattice at the size its sketch rolled, its front (k 0) the way it serves, and every round thing round:
///   a hub: the domed control tower on its roundabout: a round base, a slim shaft climbed by a ladder, a glazed saucer of a
///     cab under a dome;
///   an authority building: a round core stepping in storey by storey, its rooms round a ladder, and three wings, halls
///     with a craft's door at their ends, off its sides and back;
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
    /// A round base a storey high with a mouth at its front; a slim shaft of storeys over it, each a room, one ladder up
    /// them all; the cab a glazed saucer wider than the shaft, a faceted dome over it, its consoles round the glass.
    /// </summary>
    static void Hub(Plot p, SitePart part)
    {
        Canvas c = p.C;
        Fab f = p.F;
        double mid = part.Int("fl") / 4.0;
        double rb = Math.Max(4, part.Num("r", 12) / 2.0 - 0.5), rs = Math.Max(2.5, rb * 0.42), rc = Math.Max(rs + 2, part.Num("cab", 9) / 2.0 - 0.5);
        int storeys = Math.Max(2, (int)Math.Round((part.Num("h", 32) - 10) / 6.0));
        int m = (int)Math.Floor(mid);
        p.Approach(m, (int)Math.Floor(mid - rb) - 2, Dir.North);
        // The base: one tall storey, its roof a terrace round the shaft.
        p.Drum(mid, mid, rb, 0, H, f.Wall, j => j == 1 ? new Stuff2(Material.Glass, f.Glass) : f.Wall);
        p.Disc(mid, mid, rb, H + 1, Solid(f.Roof, Role.Roof));
        Floor(p, mid, rb - 1, 0);
        int lobby = p.Room(ModuleKind.Hall, "concourse", 0, (int)(mid - rb), 0, (int)(mid - rb), (int)(mid + rb), H, (int)(mid + rb), Within(p, mid, rb - 1));
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
        int below = lobby, j0 = H + 2;
        for (int s = 0; s < storeys; s++)
        {
            int j = j0 + s * P;
            p.Drum(mid, mid, rs, j, j + H - 1, f.Wall, jj => jj == j + 1 ? new Stuff2(Material.Glass, f.Glass) : f.Wall);
            p.Disc(mid, mid, rs, j + H, Solid(f.Floor, Role.Floor));
            int room = p.Room(ModuleKind.Room, p.R.Pick(new[] { "equipment", "radio room", "office", "rest room" }), s + 1, (int)(mid - rs), j, (int)(mid - rs), (int)(mid + rs), j + H - 1, (int)(mid + rs), Within(p, mid, rs - 1));
            c.Link(below, room);
            p.Level(s + 1, j, "floor " + (s + 1));
            p.Lamp(2 * m + 1, 2 * j + 3, 2 * m + 1, room, 0.8f, 8f);
            below = room;
        }
        // The cab: a saucer over the shaft, glazed all round, its dome over it.
        int jc = j0 + storeys * P;
        p.Disc(mid, mid, rc, jc - 1, Solid(f.Roof, Role.Floor));
        p.Drum(mid, mid, rc, jc, jc + H - 1, new Stuff2(Material.Glass, f.Glass), jj => jj == jc ? f.Wall : new Stuff2(Material.Glass, f.Glass));
        p.Cap(mid, mid, rc, jc + H, f.Roof);
        Floor(p, mid, rc - 1, jc);
        int cab = p.Room(ModuleKind.Vault, "control cab", storeys + 1, (int)(mid - rc), jc, (int)(mid - rc), (int)(mid + rc), jc + H - 1, (int)(mid + rc), Within(p, mid, rc - 1));
        c.Link(below, cab);
        p.Level(storeys + 1, jc, "cab");
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
    /// The core round the middle of its box: storeys of 6 m stepping in a block each, glazed in bands, a lobby with its
    /// mouth to the front, rooms above, flight operations at the top; a ladder through them all. A wing off each side and
    /// the back: a hall the width the type rolled, out to the box's edge, a craft's door across its end, a door into the core.
    /// </summary>
    static void Authority(Plot p, SitePart part)
    {
        Canvas c = p.C;
        Fab f = p.F;
        int L = part.Int("fl") / 2;
        double mid = L / 2.0;
        int m = (int)Math.Floor(mid);
        double rb = Math.Max(5, part.Num("r", 20) / 2.0 - 0.5);
        int hw = Math.Max(3, part.Int("span", 26) / 4), storeys = Math.Clamp((int)Math.Round((part.Num("h", 34) - 2) / 6.0), 3, 7), wh = 4;
        var stuff = new Stuff2(Material.Glass, f.Glass);
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
            for (int k = k0; k <= k1; k++)
                for (int i = i0; i <= i1; i++)
                {
                    bool inCore = Plot.In(mid, mid, rb, i, k);
                    if (inCore) continue;
                    bool side = alongI ? k == k0 || k == k1 : i == i0 || i == i1, end = alongI ? (w == 0 ? i == i0 : i == i1) : k == k1;
                    if (side || end) p.Fill(i, 0, k, i, wh - 1, k, Solid(f.Wall, Role.Wall));
                    p.Fill(i, wh, k, i, wh, k, Solid(f.Roof, Role.Roof));
                }
            p.Pave(i0 + 1, k0 + 1, i1 - 1, k1 - 1, f.Floor);
            int hall = p.Room(ModuleKind.Hall, uses[w], 0, i0 + 1, 0, k0 + 1, i1 - 1, wh - 1, k1 - 1, cell => p.BlockAt(cell) is var (bi, bk) && !Plot.In(mid, mid, rb, bi, bk));
            wings.Add(hall);
            // A craft's door across its end, two thirds its width; benches, crates and machines down its sides.
            int d = Math.Max(2, hw * 2 / 3);
            if (alongI) p.Mouth(w == 0 ? i0 : i1, 0, m - d, w == 0 ? i0 : i1, wh - 2, m + d, hall, p.Yard);
            else p.Mouth(m - d, 0, k1, m + d, wh - 2, k1, hall, p.Yard);
            for (int a = 2; a < (alongI ? i1 - i0 : k1 - k0) - 2; a += 2)
                foreach (int s in new[] { -1, 1 })
                {
                    int pick = p.R.Range(0, 4);
                    if (pick == 0) continue;
                    int u = alongI ? 2 * (i0 + a) : 2 * (m + s * (hw - 1)) + (s > 0 ? 1 : 0), v = alongI ? 2 * (m + s * (hw - 1)) + (s > 0 ? 1 : 0) : 2 * (k0 + a);
                    p.Prop(u, 0, v, pick == 1 ? BlockType.Bench : pick == 2 ? BlockType.Crate : BlockType.Machine, Material.Steel, pick == 2 ? f.Rust.C : f.Steel.C);
                }
            p.Lamp(alongI ? 2 * ((i0 + i1) / 2) : 2 * m, 2 * wh - 1, alongI ? 2 * m : 2 * ((k0 + k1) / 2), hall, 1.8f, 20f);
            p.Weathers(i0 - 1, 0, k0 - 1, i1 + 1, wh + 1, k1 + 1, wh);
        }
        // The core: its lobby as tall as the wings, the storeys over it stepping in, open to the front and to each wing.
        int below = -1, jTop = 0;
        for (int s = 0; s < storeys; s++)
        {
            double r = Math.Max(rb * 0.55, rb - s);
            int j = s == 0 ? 0 : wh + 1 + (s - 1) * P, h = s == 0 ? wh : H;
            jTop = j;
            p.Drum(mid, mid, r, j, j + h - 1, f.Wall, jj => jj == j + h - 1 ? stuff : f.Wall);
            p.Disc(mid, mid, r, j + h, Solid(s == storeys - 1 ? f.Roof : f.Floor, s == storeys - 1 ? Role.Roof : Role.Floor));
            Floor(p, mid, r - 1, j);
            string what = s == 0 ? "lobby" : s == storeys - 1 ? "flight operations" : p.R.Pick(new[] { "offices", "port housing", "customs", "medical", "beacon room" });
            int room = p.Room(s == storeys - 1 ? ModuleKind.Vault : s == 0 ? ModuleKind.Hall : ModuleKind.Room, what, s, (int)(mid - r), j, (int)(mid - r), (int)(mid + r), j + h - 1, (int)(mid + r), Within(p, mid, r - 1));
            if (s == 0)
            {
                p.Mouth(m - 1, 0, (int)Math.Floor(mid - rb) - 1, m + 1, 1, (int)Math.Floor(mid - rb) + 1, room, p.Yard);
                int edge = (int)Math.Floor(mid + rb - 1);
                p.Mouth(L - 1 - edge - 1, 0, m - 1, L - 1 - edge + 1, 1, m + 1, room, wings[0]);
                p.Mouth(edge - 1, 0, m - 1, edge + 1, 1, m + 1, room, wings[1]);
                p.Mouth(m - 1, 0, edge - 1, m + 1, 1, edge + 1, room, wings[2]);
                p.Spawn(2 * m + 4, 0, 2 * m + 4, room);
            }
            else
            {
                c.Link(below, room);
                p.Level(s, j, s == storeys - 1 ? "operations" : "floor " + s);
                for (int a = 0; a < 6; a++)
                {
                    double t = a * Math.Tau / 6 + 0.5;
                    p.Prop((int)Math.Round(2 * (mid + (r - 2) * Math.Cos(t))), 2 * j, (int)Math.Round(2 * (mid + (r - 2) * Math.Sin(t))), s == storeys - 1 ? BlockType.Machine : a % 2 == 0 ? BlockType.Table : BlockType.Shelf, Material.Steel, f.Panel.C);
                }
            }
            p.Lamp(2 * m + 1, 2 * (j + h) - 1, 2 * m + 5, room, 1.2f, 14f);
            if (s == storeys - 1)
            {
                c.VaultModule = c.CoreModule = room;
                c.AddSlot(SlotKind.Core, p.Cell(2 * m + 4, 2 * j, 2 * m), room);
                p.Terminal(2 * m - 4, 2 * j, 2 * m, room);
                p.Cache(2 * m, 2 * j, 2 * m - 4, room);
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
