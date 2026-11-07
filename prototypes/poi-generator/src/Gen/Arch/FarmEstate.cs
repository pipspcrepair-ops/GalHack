#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// A working farm laid out round its yard, the way farms grow: the house at the road, the great barn
/// across the back of the yard, a cattle shed and a machine shed down its sides, the silos and their
/// elevator leg where the grain trucks turned, and the fields running out on every side. Nothing here
/// is a corridor: you cross open ground from one thing to the next, and most of the place is sky and
/// crop rows. High-tech farms swap the cattle shed for long glass grow-houses and add a drone hangar.
/// About 300 by 240 m on the player's 2 m blocks, with no block trees: what grows round it is the
/// land's own flora.
/// </summary>
public static class FarmEstate
{
    const int W = 150, D = 120;

    public static void Compose(Canvas c, Rng rng, LayoutHints? hints, TechLevel tech)
    {
        var p = new Plot(c, rng, tech, hints?.Front ?? Dir.North);
        Fab f = p.F;
        // Farm paint: plain boards or sheet, whatever the age had.
        Stuff2 board = tech switch
        {
            TechLevel.Low => new(Material.Timber, rng.Pick(new[] { new Rgb(0x7a, 0x4a, 0x34), new Rgb(0x6c, 0x5a, 0x48), new Rgb(0x84, 0x50, 0x38) })),
            TechLevel.High => new(Material.Alloy, new Rgb(0xa0, 0xaa, 0xb0)),
            _ => new(Material.Rust, rng.Pick(new[] { new Rgb(0x8a, 0x3c, 0x2c), new Rgb(0x74, 0x6a, 0x58), new Rgb(0x56, 0x68, 0x58) })),
        };
        Stuff2 roof = tech switch
        {
            TechLevel.Low => new(Material.Timber, new Rgb(0x50, 0x42, 0x38)),
            TechLevel.High => new(Material.Alloy, new Rgb(0x56, 0x62, 0x6c)),
            _ => new(Material.Rust, new Rgb(0x66, 0x58, 0x4e)),
        };
        c.X.PlainDoor = board.C.Scale(0.75f);

        const int GateI = 66;
        p.Approach(GateI + 1, 1, Dir.North);
        Stuff2 track = new(Material.Rock, new Rgb(0x6a, 0x5e, 0x4c));
        int yardI0 = 52, yardI1 = 104, yardK0 = 28, yardK1 = 60;

        // ---- the house at the road: real rooms, a real stair ---------------------------------------
        int yardRoom = p.Room(ModuleKind.Outdoor, "yard", 0, yardI0, 0, yardK0, yardI1, 1, yardK1);
        c.Link(p.Yard, yardRoom);
        Homes.Farmhouse(c, rng, tech, p.Cell(2 * 34, 0, 2 * 8), p.Turn(Dir.North), yardRoom);

        // ---- the great barn, across the back of the yard -------------------------------------------
        int barn = GreatBarn(p, board, roof, yardRoom, 58, yardK1 + 1, 96, yardK1 + 18);
        c.VaultModule = barn;

        // ---- the long shed down one side -----------------------------------------------------------
        if (tech == TechLevel.High) GrowHouses(p, yardRoom, yardI1 + 2, yardK0);
        else CattleShed(p, board, roof, yardRoom, yardI1 + 2, yardK0, yardI1 + 12, yardK1);

        // ---- the machine shed, open along its yard side, tractors and a harvester in it -------------
        MachineShed(p, board, roof, yardRoom, yardI0 - 16, yardK0 + 8, yardI0 - 2, yardK1);

        // ---- the silos and their leg ---------------------------------------------------------------
        Silos(p, yardRoom, tech == TechLevel.High);

        // ---- fields out on every side --------------------------------------------------------------
        Field(p, 4, 66, 52, 116, 0);
        Field(p, 56, 86, 116, 116, 1);
        Field(p, 120, 66, 146, 116, 2);
        Field(p, 4, 4, 30, 62, 3);
        Field(p, 118, 4, 146, tech == TechLevel.High ? 24 : 60, 2);
        Harvester(p, tech, 26, 92);

        // A water tower by the track, a stock trough in the yard.
        WaterTower(p, 50, 4);
        foreach (int k in new[] { 34, 36 }) p.Machine(yardI1 - 5, 0, k, yardI1 - 3, k, 1, f.Rust);

        // ---- the fences, the track, the paving last so it runs under what stands ---------------------
        Fences(p, GateI);
        p.Pave(GateI - 1, 0, GateI + 2, yardK0, track);
        p.Pave(yardI0, yardK0, yardI1, yardK1, new Stuff2(tech == TechLevel.Low ? Material.Rock : Material.Concrete, tech == TechLevel.Low ? track.C : f.Floor.C));
        foreach (var (u, v) in new[] { (140, 80), (160, 100), (190, 110), (110, 90) }) p.Spawn(u, 0, v, yardRoom);
        p.Finish();
    }

    /// <summary>A hall 76 by 36 m under a great gable: mows to either side of the threshing floor, a hayloft, the big doors standing open.</summary>
    static int GreatBarn(Plot p, Stuff2 board, Stuff2 roof, int yard, int i0, int k0, int i1, int k1)
    {
        const int H = 5;
        int room = p.Hall("barn", ModuleKind.Hall, 0, i0, k0, i1, k1, 0, H, board, roof, weather: false);
        // No ceiling slab: open to the rafters.
        p.Dig(i0 + 1, H, k0 + 1, i1 - 1, H, k1 - 1);
        p.Plinth(i0, k0, i1, k1, 0);
        p.Gable(i0, k0, i1, k1, H, true, roof, board);
        p.Weathers(i0 - 2, 0, k0 - 2, i1 + 2, H + 12, k1 + 2, H);
        // Doors 12 m wide in the yard side, the floor running through.
        int mid = (i0 + i1) / 2;
        p.Mouth(mid - 3, 0, k0, mid + 2, 3, k0, room, yard);
        p.Fill(mid - 4, 4, k0 - 1, mid + 3, 4, k0, Plot.Cube(board.Scale(0.8f), Role.Wall));
        // Pairs of posts down the hall.
        for (int i = i0 + 6; i < i1 - 2; i += 6)
            foreach (int k in new[] { k0 + 6, k1 - 6 })
                for (int y = 0; y < 2 * H; y++)
                    foreach (int du in new[] { 0, 1 })
                        p.Put(2 * i + du, y, 2 * k, new Block(BlockType.Rib, Material.Timber, new Rgb(0x54, 0x42, 0x34), Role.Decor));
        // Straw and bales in the mows, stacked in big rectangles.
        Rgb hay = new(0xa8, 0x94, 0x58), old = new(0x7e, 0x70, 0x4c);
        for (int i = i0 + 2; i < mid - 5; i += 3)
            foreach (int k in new[] { k0 + 2, k0 + 4, k1 - 4, k1 - 2 })
            {
                int stack = k >= k1 - 6 ? 1 : 1 + (int)(Hash.Cell(new Int3(i, 0, k), 301) % 3);
                for (int h = 0; h < stack; h++)
                    p.Fill(i, h, k, i + 1, h, k + 1, new Block(BlockType.Crate, Material.Plant, (i + h) % 2 == 0 ? hay : old, Role.Prop));
            }
        // Stalls down the far end: rails and a feed passage.
        for (int i = mid + 6; i < i1 - 1; i += 4)
            for (int v = 2 * (k1 - 6); v < 2 * k1; v++) p.Put(2 * i, 0, v, new Block(BlockType.Rail, Material.Timber, new Rgb(0x60, 0x4c, 0x3c), Role.Decor));
        // The loft along the back wall, a flight up to it; the best of what's left is up there.
        int up = p.Flight(i0 + 2, k1 - 2, Dir.East, 0, 2, 1, "stair");
        p.C.Link(room, up);
        int loft = Loft(p, i0 + 4, k1 - 6, i1 - 2, k1 - 1, 2);
        p.C.Link(up, loft);
        p.Level(1, 2, "loft");
        p.Cache(2 * (i1 - 3), 4, 2 * (k1 - 2), loft);
        p.Cache(2 * (i1 - 8), 4, 2 * (k1 - 2), loft);
        p.Spawn(2 * mid, 0, 2 * (k0 + 8), room);
        p.Spawn(2 * (mid + 8), 0, 2 * (k0 + 4), room);
        return loft;
    }

    /// <summary>A boarded loft floor on the posts, half a block thick, open along its edge.</summary>
    static int Loft(Plot p, int i0, int k0, int i1, int k1, int j)
    {
        Rgb boards = new(0x6a, 0x56, 0x42);
        for (int v = 2 * k0; v <= 2 * k1 + 1; v++)
            for (int u = 2 * i0; u <= 2 * i1 + 1; u++)
                p.Put(u, 2 * j - 1, v, new Block(BlockType.Cube, Material.Timber, boards, Role.Floor));
        for (int u = 2 * i0; u <= 2 * i1 + 1; u++) p.Put(u, 2 * j, 2 * k0, new Block(BlockType.Rail, Material.Timber, boards.Scale(0.8f), Role.Decor));
        for (int i = i0 + 1; i < i1; i += 4)
            for (int y = 0; y < 2 * j - 1; y++) p.Put(2 * i, y, 2 * k0, new Block(BlockType.Rib, Material.Timber, new Rgb(0x54, 0x42, 0x34), Role.Decor));
        return p.Room(ModuleKind.Room, "hay-loft", 1, i0, j, k0, i1, j + 1, k1, c => !p.C.Blocks.TryGetValue(c, out var b) || b.Type != BlockType.Rail);
    }

    /// <summary>A long low shed: a feed passage down the middle, stalls and rails either side.</summary>
    static void CattleShed(Plot p, Stuff2 board, Stuff2 roof, int yard, int i0, int k0, int i1, int k1)
    {
        const int H = 2;
        int room = p.Hall("cattle-shed", ModuleKind.Hall, 0, i0, k0, i1, k1, 0, H, board, roof, weather: false);
        p.Dig(i0 + 1, H, k0 + 1, i1 - 1, H, k1 - 1);
        p.Plinth(i0, k0, i1, k1, 0);
        p.Gable(i0, k0, i1, k1, H, false, roof, board);
        p.Weathers(i0 - 2, 0, k0 - 2, i1 + 2, H + 8, k1 + 2, H);
        p.Mouth(i0, 0, (k0 + k1) / 2 - 2, i0, 2, (k0 + k1) / 2 + 1, room, yard);
        int mid = (i0 + i1) / 2;
        // Stall rails square off the passage; a trough along each side of it.
        for (int k = k0 + 2; k < k1 - 1; k += 3)
            foreach (int u0 in new[] { 2 * i0 + 2, 2 * mid + 4 })
                for (int u = u0; u < u0 + 2 * (mid - i0) - 4; u++) p.Put(u, 0, 2 * k, new Block(BlockType.Rail, Material.Timber, new Rgb(0x60, 0x4c, 0x3c), Role.Decor));
        for (int k = k0 + 1; k < k1; k += 1)
            foreach (int u in new[] { 2 * mid - 1, 2 * mid + 2 })
                if ((k - k0) % 3 != 1) p.Lay(u, 0, 2 * k, new Block(BlockType.Bench, Material.Concrete, new Rgb(0x70, 0x6c, 0x64), Role.Prop));
        p.Cache(2 * mid, 0, 2 * (k1 - 2), room);
        p.Spawn(2 * mid, 0, 2 * (k0 + 4), room);
    }

    /// <summary>The high-tech farm's long glass houses: frames on a plinth, racks of trays, a feed pipe down each.</summary>
    static void GrowHouses(Plot p, int yard, int i0, int k0)
    {
        Fab f = p.F;
        for (int n = 0; n < 3; n++)
        {
            int ki = k0 + n * 11, i1 = i0 + 34, k1 = ki + 9;
            int room = p.Hall("grow-house", ModuleKind.Hall, 0, i0, ki, i1, k1, 0, 2, f.Panel, new Stuff2(Material.Glass, f.Glass), weather: false);
            p.Glazed(i0 + 1, 1, ki, i1 - 1, ki);
            p.Glazed(i0 + 1, 1, k1, i1 - 1, k1);
            p.Weathers(i0 - 1, 0, ki - 1, i1 + 1, 4, k1 + 1, 2);
            p.Mouth(i0, 0, ki + 4, i0, 1, ki + 5, room, yard);
            // Racks: two double rows of benches, a green stain of what grew there.
            foreach (int k in new[] { ki + 2, ki + 7 })
                for (int u = 2 * i0 + 4; u < 2 * i1 - 2; u++)
                {
                    p.Lay(u, 0, 2 * k, new Block(BlockType.Bench, Material.Alloy, f.Steel.C, Role.Prop));
                    p.Lay(u, 0, 2 * k + 2, new Block(BlockType.Bench, Material.Alloy, f.Steel.C, Role.Prop));
                    if (Hash.Cell(new Int3(u, n, k), 307) % 100 < 60)
                        p.Lay(u, 1, 2 * k, new Block(BlockType.Plant, Material.Plant, new Rgb(0x4e, 0x6a, 0x3a), Role.Debris));
                }
            p.Pipe(2 * i0 + 2, 3, 2 * ki + 9, Dir.East, 2 * (i1 - i0) - 3, f.Pipe);
            p.Cache(2 * i1 - 3, 0, 2 * ki + 9, room);
        }
    }

    static void MachineShed(Plot p, Stuff2 board, Stuff2 roof, int yard, int i0, int k0, int i1, int k1)
    {
        Fab f = p.F;
        const int H = 3;
        int room = p.Hall("machine-shed", ModuleKind.Hall, 0, i0, k0, i1, k1, 0, H, board, roof, weather: false);
        p.Dig(i0 + 1, H, k0 + 1, i1 - 1, H, k1 - 1);
        p.Plinth(i0, k0, i1, k1, 0);
        p.Gable(i0, k0, i1, k1, H, false, roof, board);
        p.Weathers(i0 - 2, 0, k0 - 2, i1 + 2, H + 10, k1 + 2, H);
        // Open all along the yard side, on posts.
        for (int k = k0 + 1; k < k1; k++)
            if ((k - k0) % 6 != 0) p.Mouth(i1, 0, k, i1, H - 1, k, room, yard);
        // Tractors and implements parked in a row, big plain shapes.
        for (int k = k0 + 2; k < k1 - 4; k += 7)
        {
            p.Machine(i0 + 5, 0, k, i0 + 9, k + 3, 1, f.Rust);
            p.Machine(i0 + 7, 1, k + 1, i0 + 8, k + 2, 1, f.Rust.Scale(0.8f));
            foreach (int w in new[] { k, k + 3 })
                p.Fill(i0 + 4, 0, w, i0 + 5, 0, w, new Block(BlockType.Cube, Material.Rust, new Rgb(0x26, 0x24, 0x22), Role.Prop));
        }
        p.Cache(2 * i0 + 2, 0, 2 * (k0 + 2), room);
    }

    /// <summary>A row of round silos 12 m across and 30 m tall, an elevator leg taller than all of them, a conveyor over their tops, a truck dump at its foot.</summary>
    static void Silos(Plot p, int yard, bool hangar)
    {
        Fab f = p.F;
        Stuff2 shell = p.Tech == TechLevel.Low ? new Stuff2(Material.Timber, new Rgb(0x7a, 0x6c, 0x58)) : new Stuff2(f.Wall.M, f.Wall.C.Lerp(new Rgb(0xb0, 0xa8, 0x98), 0.5f));
        const int H = 15, K = 16, R = 6;
        var tops = new List<int>();
        for (int n = 0; n < 3; n++)
        {
            int ci = 80 + n * 14;
            p.Drum(ci, K, R, 0, H - 1, shell, j => j % 4 == 3 ? shell.Scale(0.86f) : shell);
            p.Disc(ci, K, R - 1, H - 1, Plot.Cube(shell.Scale(0.8f), Role.Roof));
            p.Cap(ci, K, R - 1, H, new Stuff2(Material.Steel, new Rgb(0x6a, 0x6c, 0x6c)), 1.4f);
            tops.Add(ci);
            p.Stencil((n + 1).ToString(), 2 * ci - 3, 12, 2 * (K + R) - 1, true, new Rgb(0x2c, 0x2a, 0x28), 1);
        }
        // The first silo is walked into at its foot: a round well 26 m high, old grain heaped in the bottom.
        int first = tops[0];
        var cells = new List<(int, int, int)>();
        for (int v = 2 * (K - R - 1); v < 2 * (K + R + 1); v++)
            for (int u = 2 * (first - R - 1); u < 2 * (first + R + 1); u++)
                if (Plot.In(first, K, R - 1.0, u >> 1, v >> 1))
                    for (int y = 0; y < 2 * (H - 1); y++) cells.Add((u, y, v));
        int well = p.RoomOf(ModuleKind.Hall, "silo", 0, cells);
        p.Mouth(first - 1, 0, K + R - 1, first, 1, K + R - 1, well, yard);
        Rgb grain = new(0x8c, 0x78, 0x48);
        for (int v = 2 * (K - R); v < 2 * (K + R); v++)
            for (int u = 2 * (first - R); u < 2 * (first + R); u++)
            {
                double d = Math.Sqrt(Math.Pow(u / 2.0 - first, 2) + Math.Pow(v / 2.0 - K, 2));
                if (d < 4 && d > 1) p.Lay(u, 0, v, new Block(BlockType.Rubble, Material.Plant, grain, Role.Prop));
            }
        p.Cache(2 * first + 8, 0, 2 * K, well);
        if (hangar)
        {
            int room = p.Hall("drone-hangar", ModuleKind.Hall, 0, 46, 14, 62, 26, 0, 5, f.Panel);
            p.Mouth(52, 0, 26, 55, 3, 26, room, yard);
            p.Stripes(2 * 51, 0, 2 * 26, 2 * 51 + 1, 7, 2 * 26 + 1);
            p.Stripes(2 * 56, 0, 2 * 26, 2 * 56 + 1, 7, 2 * 26 + 1);
            for (int k = 16; k < 24; k += 4) { p.Machine(48, 0, k, 50, k + 2, 1, f.Steel); p.Machine(57, 0, k, 59, k + 2, 1, f.Steel); }
        }
        // The leg: a square steel tower beside the silos, a head house on it.
        int legI = 70, legK = K - 2;
        Block frame = new(BlockType.Lattice, f.Steel.M, f.Steel.C, Role.Decor);
        for (int j = 0; j < H + 5; j++)
            foreach (var (di, dk) in new[] { (0, 0), (2, 0), (0, 2), (2, 2) })
                p.Fill(legI + di, j, legK + dk, legI + di, j, legK + dk, frame);
        for (int j = 3; j < H + 5; j += 3)
            for (int di = 0; di < 3; di++)
                for (int dk = 0; dk < 3; dk++)
                    if (di == 1 || dk == 1) p.Fill(legI + di, j, legK + dk, legI + di, j, legK + dk, frame);
        p.Fill(legI - 1, H + 5, legK - 1, legI + 3, H + 7, legK + 3, Plot.Cube(f.Rust, Role.Wall));
        p.Weathers(legI - 2, 0, legK - 2, legI + 4, H + 8, legK + 4, H + 5);
        // The covered conveyor along the silo tops, from the head house.
        int y0 = 2 * (H + 5);
        for (int u = 2 * (legI + 3); u < 2 * (tops[^1] + 2); u++)
            foreach (int v in new[] { 2 * K - 1, 2 * K, 2 * K + 1, 2 * K + 2 })
            {
                p.Put(u, y0, v, Plot.Cube(f.Steel, Role.Decor));
                if (v is 2 * K - 1 or 2 * K + 2) { p.Put(u, y0 + 1, v, Plot.Cube(f.Steel, Role.Decor)); p.Put(u, y0 + 2, v, Plot.Cube(f.Steel, Role.Decor)); }
                p.Put(u, y0 + 3, v, Plot.Cube(f.Rust, Role.Roof));
            }
        // Trestles from the conveyor down to each silo cap.
        foreach (int ci in tops)
            for (int t = 2 * H + 2; t < y0; t++)
                foreach (int v in new[] { 2 * K - 1, 2 * K + 2 })
                    p.Put(2 * ci, t, v, frame);
        // The truck dump: a grate in a low steel box by the leg.
        p.Machine(legI - 1, 0, legK + 4, legI + 2, legK + 8, 1, f.Rust);
    }

    /// <summary>A field: furrows laid in strips on the farm's bare earth, crop rows between them, gone to weed or still standing in lines.</summary>
    static void Field(Plot p, int i0, int k0, int i1, int k1, int kind)
    {
        Rgb soil = new(0x4c, 0x3e, 0x30);
        Rgb crop = kind switch { 0 => new Rgb(0x96, 0x90, 0x44), 1 => new Rgb(0x6c, 0x7a, 0x3a), 2 => new Rgb(0x8a, 0x78, 0x3c), _ => new Rgb(0x58, 0x6e, 0x3c) };
        bool alongI = (i1 - i0) >= (k1 - k0);
        for (int v = 2 * k0; v <= 2 * k1 + 1; v++)
            for (int u = 2 * i0; u <= 2 * i1 + 1; u++)
            {
                if (!p.Get(u, 0, v).IsAir) continue;
                int row = (alongI ? v : u) % 6;
                uint h = Hash.Cell(new Int3(u, kind, v), 311);
                if (row < 2) p.Lay(u, 0, v, new Block(BlockType.Plate, Material.Soil, soil, Role.Floor));
                else if (row is 3 or 4 && (h % 100 < 34)) p.Lay(u, 0, v, new Block(BlockType.Plant, Material.Plant, h % 5 == 0 ? new Rgb(0x70, 0x78, 0x44) : crop, Role.Debris));
            }
    }

    /// <summary>A harvester stopped mid-row, a great plain machine out in the crop.</summary>
    static void Harvester(Plot p, TechLevel tech, int i, int k)
    {
        Fab f = p.F;
        Stuff2 paint = tech == TechLevel.High ? new Stuff2(Material.Alloy, new Rgb(0xc8, 0xc4, 0xb8)) : new Stuff2(Material.Rust, new Rgb(0x9a, 0x7a, 0x28));
        p.Machine(i, 0, k, i + 6, k + 3, 2, paint);
        p.Fill(i + 4, 2, k + 1, i + 6, 2, k + 2, Plot.Cube(new Stuff2(Material.Glass, f.Glass), Role.Decor));
        p.Fill(i + 7, 0, k - 3, i + 8, 0, k + 6, Plot.Cube(paint.Scale(0.8f), Role.Prop));
        foreach (int w in new[] { k - 1, k + 4 })
            p.Fill(i + 1, 0, w, i + 3, 1, w, new Block(BlockType.Cube, Material.Rust, new Rgb(0x24, 0x22, 0x20), Role.Prop));
    }

    static void WaterTower(Plot p, int i, int k)
    {
        Fab f = p.F;
        foreach (var (di, dk) in new[] { (0, 0), (5, 0), (0, 5), (5, 5) })
            for (int y = 0; y < 18; y++)
                p.Put(2 * (i + di), y, 2 * (k + dk), new Block(BlockType.Rib, f.Steel.M, f.Steel.C, Role.Decor));
        for (int u = 2 * i; u <= 2 * (i + 5); u++)
            foreach (int v in new[] { 2 * k, 2 * (k + 5) }) p.Put(u, 17, v, Plot.Cube(f.Steel, Role.Decor));
        for (int v = 2 * k; v <= 2 * (k + 5); v++)
            foreach (int u in new[] { 2 * i, 2 * (i + 5) }) p.Put(u, 17, v, Plot.Cube(f.Steel, Role.Decor));
        p.Drum(i + 3, k + 3, 3.5, 9, 11, f.Rust);
        p.Disc(i + 3, k + 3, 3.5, 9, Plot.Cube(f.Rust, Role.Roof));
        p.Disc(i + 3, k + 3, 2.5, 11, Plot.Cube(f.Rust.Scale(0.8f), Role.Roof));
    }

    /// <summary>Post-and-wire round the holding, with its gate.</summary>
    static void Fences(Plot p, int gateI)
    {
        Rgb post = new(0x5a, 0x48, 0x38);
        var wire = new Block(BlockType.Rail, Material.Timber, new Rgb(0x6a, 0x60, 0x52), Role.Decor);
        void Line(int u0, int v0, int u1, int v1)
        {
            int len = Math.Max(Math.Abs(u1 - u0), Math.Abs(v1 - v0));
            for (int t = 0; t <= len; t++)
            {
                int u = u0 + Math.Sign(u1 - u0) * t, v = v0 + Math.Sign(v1 - v0) * t;
                if (!p.Get(u, 0, v).IsAir && p.Get(u, 0, v).Type != BlockType.Plate) continue;
                if (Hash.Cell(new Int3(u / 8, 0, v / 8), 313) % 100 < 16) continue;
                if (t % 4 == 0) p.Put(u, 0, v, new Block(BlockType.Pillar, Material.Timber, post, Role.Decor));
                else p.Put(u, 0, v, wire);
            }
        }
        Line(0, 0, 2 * gateI - 2, 0);
        Line(2 * gateI + 8, 0, 2 * W, 0);
        Line(0, 0, 0, 2 * D);
        Line(0, 2 * D, 2 * W, 2 * D);
        Line(2 * W, 0, 2 * W, 2 * D);
    }
}
