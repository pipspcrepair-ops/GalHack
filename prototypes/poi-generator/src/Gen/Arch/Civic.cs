#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// Buildings at real scale on the player's 2 m blocks, each with an inside of its own kind: a market is
/// bays of stalls under three ridges with shops and a gallery round them, a hospital is wards off one long
/// corridor over a mortuary, a water works is sunken filter beds and pumps with a gallery under them, a
/// bunker is a long stair to one tunnel with rooms cut off it. All of them wear their kit: one wall colour
/// per building, never cell by cell, so they read from a distance, and <see cref="Facade"/> trims them.
/// </summary>
public static class Civic
{
    /// <summary>A building's materials from its kit: the first colour of each, so a wall is one wall.</summary>
    internal static void Wear(Plot p)
    {
        Style s = p.C.Style;
        Fab f = p.F;
        f.Wall = new(s.Wall.Material, s.Wall.Colors[0]);
        f.Panel = new(s.InnerWall.Material, s.InnerWall.Colors[0]);
        f.Floor = new(s.Floor.Material, s.Floor.Colors[0]);
        f.Roof = new(s.Roof.Material, s.Roof.Colors[0]);
        f.Lamp = s.Light.Colors[0];
        p.C.X.PlainDoor = s.AccentColor;
        p.C.X.PlainDoorSteel = true;
    }

    static Block Solid(Stuff2 s, Role r) => Plot.Cube(s, r);

    /// <summary>A row of windows along a wall running along i, one block tall, with a pier between each.</summary>
    static void WindowsAlongI(Plot p, int i0, int i1, int j, int k, int wide, int every)
    {
        for (int i = i0; i + wide - 1 <= i1; i += every) p.Glazed(i, j, k, i + wide - 1, k);
    }

    /// <summary>A rail round the open side of a stair's well, on the floor above it.</summary>
    static void WellRail(Plot p, int i0, int k0, int i1, int k1, int j, Stuff2 s, bool openWest)
    {
        var rail = new Block(BlockType.Rail, s.M, s.C, Role.Decor);
        int y = 2 * j;
        for (int u = 2 * i0; u <= 2 * i1 + 1; u++)
        {
            p.Put(u, y, 2 * k0 - 1, rail);
            p.Put(u, y, 2 * k1 + 2, rail);
        }
        int end = openWest ? 2 * i1 + 2 : 2 * i0 - 1;
        for (int v = 2 * k0 - 1; v <= 2 * k1 + 2; v++) p.Put(end, y, v, rail);
    }

    // ---- a market ------------------------------------------------------------------------------

    /// <summary>
    /// A market hall 96 by 64 m under three ridges, its slopes the player's: an aisle down each bay, stalls
    /// along them with their awnings, shops along the back with a gallery on their roofs, and the counting
    /// room up there looking over it all.
    /// </summary>
    public static void Market(Canvas c, Rng rng, LayoutHints? hints, TechLevel tech)
    {
        const int I0 = 4, K0 = 6, K1 = 37, H = 7;
        // Each spawn its own length (its own seed).
        int I1 = 51 + 2 * rng.Fork("size").Range(0, 4); // never shorter than the old fixed 51: a large place stands 100 m on its long side
        var p = new Plot(c, rng, tech, hints?.Front ?? Dir.North);
        Wear(p);
        Fab f = p.F;
        p.Approach(27, 1, Dir.North);
        p.Pave(23, 2, 32, K0 - 1, f.Floor.Scale(0.85f));

        int hall = p.Hall("market-hall", ModuleKind.Hall, 0, I0, K0, I1, K1, 0, H, f.Wall, f.Roof, weather: false);
        // The roofs stand on the walls' tops: the hall's own slab goes, edge and all.
        p.Dig(I0, H, K0, I1, H, K1);
        p.Plinth(I0, K0, I1, K1, 0);
        // Three ridges side by side: one span this wide would stand taller than the hall.
        int[] bays = { K0, K0 + 11, K0 + 22, K1 };
        for (int b = 0; b < 3; b++)
            p.Gable(I0, bays[b], I1, b == 2 ? K1 : bays[b + 1] - 1, H, true, f.Roof, f.Wall, 0);
        p.Weathers(I0 - 2, 0, K0 - 2, I1 + 2, H + 10, K1 + 2, H);

        // The front: a way in you could drive through, a door each side of it, a long light above.
        p.Mouth(25, 0, K0, 30, 2, K0, hall, p.Yard);
        foreach (int i in new[] { 14, 41 }) p.Door(i, 0, K0, true, hall, p.Yard);
        WindowsAlongI(p, I0 + 3, I1 - 3, 5, K0, 4, 6);
        WindowsAlongI(p, I0 + 3, I1 - 3, 5, K1, 4, 6);

        // Columns under each valley, a lamp on each.
        Stuff2 column = f.Steel;
        foreach (int k in new[] { bays[1] - 1, bays[2] - 1 })
            for (int i = I0 + 6; i < I1 - 3; i += 8)
            {
                p.Fill(i, 0, k, i, H - 1, k, Solid(column, Role.Wall));
                p.Lamp(2 * i, 2 * H - 2, 2 * k - 1, hall, 1.6f, 18f);
            }

        // Stalls along each aisle, under one awning colour a row.
        Rgb[] awnings = { f.Accent.C, f.Accent.C.Scale(0.7f), f.Wall.C.Scale(0.6f) };
        int row = 0;
        foreach (int k in new[] { K0 + 4, K0 + 15 })
        {
            Stuff2 cloth = new(Material.Cloth, awnings[row++ % 3]);
            for (int i = I0 + 3; i < I1 - 5; i += 6)
            {
                if (i >= 23 && i <= 31 && k == K0 + 4) continue; // the way in stays clear
                Stall(p, i, k, cloth, hall);
            }
        }

        // Shops along the back, their roofs the gallery.
        const int ShopK0 = K1 - 6;
        var shops = new List<int>();
        for (int i = I0 + 1; i + 6 <= I1 - 1; i += 6)
        {
            int shop = p.Hall("shop", ModuleKind.Room, 0, i, ShopK0, i + 6, K1 - 1, 0, 2, f.Panel, f.Roof, weather: false);
            p.Door(i + 3, 0, ShopK0, true, shop, hall);
            shops.Add(shop);
            p.Prop(2 * i + 3, 0, 2 * ShopK0 + 3, BlockType.Bench, Material.Steel, f.Steel.C);
            p.Prop(2 * i + 7, 0, 2 * K1 - 5, BlockType.Shelf, Material.Steel, f.Steel.C);
            p.Prop(2 * i + 9, 0, 2 * K1 - 5, BlockType.Shelf, Material.Steel, f.Steel.C);
            p.Lamp(2 * i + 6, 3, 2 * ShopK0 + 6, shop, 1f, 9f);
        }
        p.Cache(2 * (I0 + 4), 0, 2 * K1 - 6, shops[0]);
        p.Cache(2 * (I1 - 4), 0, 2 * K1 - 6, shops[^1]);

        int up = p.Flight(25, ShopK0 - 3, Dir.South, 0, 3, 2, "stair");
        c.Link(hall, up);
        // The gallery: open along its north edge, a rail there but for the stair's head.
        int gallery = p.Room(ModuleKind.Hall, "gallery", 1, I0 + 1, 3, ShopK0, I1 - 13, 5, K1 - 1);
        c.Link(up, gallery);
        p.Level(1, 3, "gallery");
        for (int u = 2 * (I0 + 1); u <= 2 * (I1 - 13) + 1; u++)
            if (u < 50 || u > 53) p.Put(u, 6, 2 * ShopK0, new Block(BlockType.Rail, Material.Steel, f.Steel.C, Role.Decor));

        int office = p.Hall("counting-room", ModuleKind.Vault, 1, I1 - 12, ShopK0, I1 - 1, K1 - 1, 3, 2, f.Panel, f.Roof, weather: false);
        p.Door(I1 - 12, 3, K1 - 4, false, office, gallery);
        p.Glazed(I1 - 10, 4, ShopK0, I1 - 3, ShopK0);
        p.Terminal(2 * (I1 - 3), 6, 2 * K1 - 5, office);
        p.Prop(2 * (I1 - 6), 6, 2 * K1 - 5, BlockType.Bench, Material.Steel, f.Steel.C);
        p.Cache(2 * (I1 - 9), 6, 2 * K1 - 5, office);
        p.Lamp(2 * (I1 - 6), 9, 2 * K1 - 8, office, 1.2f, 12f);
        c.VaultModule = c.CoreModule = office;
        c.AddSlot(SlotKind.Core, p.Cell(2 * (I1 - 4), 6, 2 * K1 - 8), office);

        foreach (var (u, v) in new[] { (30, 40), (56, 40), (80, 58), (30, 80) }) p.Spawn(u, 0, v, hall);
        p.Finish();
    }

    /// <summary>A stall: a counter three blocks long, goods on it, crates behind, a pitched awning on four posts (slopes either side of a ridge, as a player would roof it).</summary>
    static void Stall(Plot p, int i, int k, Stuff2 cloth, int room)
    {
        Fab f = p.F;
        for (int u = 2 * i; u < 2 * i + 6; u++)
        {
            p.Prop(u, 0, 2 * k, BlockType.Bench, Material.Steel, f.Steel.C);
            for (int v = 2 * k - 1; v <= 2 * k + 3; v++)
            {
                int ridge = 2 * k + 1;
                p.Put(u, 5, v, v == ridge ? new Block(BlockType.Cube, cloth.M, cloth.C, Role.Decor)
                    : new Block(BlockType.Ramp, cloth.M, cloth.C, Role.Decor, v < ridge ? Dir.South : Dir.North));
            }
        }
        foreach (int u in new[] { 2 * i, 2 * i + 5 })
            foreach (int v in new[] { 2 * k - 1, 2 * k + 3 })
                for (int y = 0; y < 5; y++) p.Put(u, y, v, new Block(BlockType.Rib, f.Steel.M, f.Steel.C, Role.Decor));
        for (int u = 2 * i + 1; u < 2 * i + 5; u += 2) p.Prop(u, 0, 2 * k + 2, BlockType.Crate, Material.Scrap, f.Rust.C);
    }

    // ---- a hospital ----------------------------------------------------------------------------

    /// <summary>
    /// A hospital block 116 by 40 m of three storeys, 6 m a storey: wards off one long corridor, day rooms
    /// at each end with the stairs in them, an operating wing behind, and a mortuary under the ground floor.
    /// </summary>
    public static void Hospital(Canvas c, Rng rng, LayoutHints? hints, TechLevel tech)
    {
        const int I0 = 4, K0 = 8, Storeys = 3, Pitch = 3, H = 2, End = 8;
        // Each spawn its own length and depth of wings (its own seed), so no two hospitals are one building.
        var size = rng.Fork("size");
        // (Whole bays of wards: the length steps by a ward.)
        int I1 = 54 + 7 * size.Range(0, 3), K1 = 25 + 2 * size.Range(0, 3);
        const int CorridorK0 = 17, CorridorK1 = 18;
        var p = new Plot(c, rng, tech, hints?.Front ?? Dir.North);
        Wear(p);
        Fab f = p.F;
        p.Approach(30, 1, Dir.North);
        p.Pave(28, 2, 33, K0 - 1, f.Floor.Scale(0.85f));
        for (int s = 0; s < Storeys; s++)
        {
            p.Shell(I0, K0, I1, K1, s * Pitch, H, f.Wall, f.Roof);
            if (s > 0) p.Level(s, s * Pitch, s == 1 ? "first floor" : "top floor");
        }
        p.Plinth(I0, K0, I1, K1, 0);
        // A parapet, and the storeys told on the outside by a band at each floor.
        int top = Storeys * Pitch - 1;
        foreach (int i in Enumerable.Range(I0, I1 - I0 + 1))
            foreach (int k in new[] { K0, K1 }) p.Fill(i, top + 1, k, i, top + 1, k, Solid(f.Wall, Role.Wall));
        foreach (int k in Enumerable.Range(K0, K1 - K0 + 1))
            foreach (int i in new[] { I0, I1 }) p.Fill(i, top + 1, k, i, top + 1, k, Solid(f.Wall, Role.Wall));
        p.Weathers(I0 - 2, 0, K0 - 2, I1 + 2, top + 3, K1 + 2, top + 1);
        for (int s = 1; s < Storeys; s++)
            foreach (int k in new[] { K0, K1 })
                p.Repaint(2 * I0, 2 * s * Pitch - 2, 2 * k, 2 * I1 + 1, 2 * s * Pitch - 1, 2 * k + 1, (_, _, _) => f.Plinth.C);

        var corridors = new int[Storeys];
        var dayW = new int[Storeys];
        var dayE = new int[Storeys];
        // Stairs first, so the rooms leave them be: up the west end, then up the east, a long walk between.
        // Beside the corridor's line, not in it: the way in from each corridor never opens over a well.
        const int StairK = CorridorK0 - 3;
        int stairW = p.Flight(I0 + 6, StairK, Dir.West, 0, Pitch, 2, "stair", 0);
        int stairE = p.Flight(I1 - 6, StairK, Dir.East, Pitch, Pitch, 2, "stair", 1);
        for (int s = 0; s < Storeys; s++)
        {
            int j = s * Pitch;
            foreach (int k in new[] { CorridorK0 - 1, CorridorK1 + 1 })
                p.Fill(I0 + End + 1, j, k, I1 - End - 1, j + H - 1, k, Solid(f.Panel, Role.Wall));
            foreach (int i in new[] { I0 + End, I1 - End })
            {
                p.Fill(i, j, K0 + 1, i, j + H - 1, K1 - 1, Solid(f.Panel, Role.Wall));
                p.Dig(i, j, CorridorK0, i, j + H - 1, CorridorK1);
            }
            corridors[s] = p.Room(ModuleKind.Corridor, "corridor", s, I0 + End + 1, j, CorridorK0, I1 - End - 1, j + H - 1, CorridorK1);
            dayW[s] = p.Room(ModuleKind.Hall, "day-room", s, I0 + 1, j, K0 + 1, I0 + End, j + H - 1, K1 - 1);
            dayE[s] = p.Room(ModuleKind.Hall, "day-room", s, I1 - End, j, K0 + 1, I1 - 1, j + H - 1, K1 - 1);
            c.Link(corridors[s], dayW[s]);
            c.Link(corridors[s], dayE[s]);
            p.Lamp(2 * (I0 + 4), 2 * j + 3, 2 * (K0 + 4), dayW[s], 1.4f, 14f);
            p.Lamp(2 * (I1 - 4), 2 * j + 3, 2 * (K0 + 4), dayE[s], 1.4f, 14f);
            for (int i = I0 + End + 4; i < I1 - End; i += 9) p.Lamp(2 * i, 2 * j + 3, 2 * CorridorK0 + 2, corridors[s], 1f, 10f);
            Wards(p, I0 + End + 1, I1 - End - 1, K0 + 1, CorridorK0 - 2, j, s, corridors[s], true);
            Wards(p, I0 + End + 1, I1 - End - 1, CorridorK1 + 2, K1 - 1, j, s, corridors[s], false);
            WindowsAlongI(p, I0 + 2, I0 + End - 2, j + 1, K0, 2, 3);
            WindowsAlongI(p, I1 - End + 2, I1 - 2, j + 1, K0, 2, 3);
            WindowsAlongI(p, I0 + 2, I0 + End - 2, j + 1, K1, 2, 3);
            WindowsAlongI(p, I1 - End + 2, I1 - 2, j + 1, K1, 2, 3);
        }
        c.Link(dayW[0], stairW);
        c.Link(stairW, dayW[1]);
        c.Link(dayE[1], stairE);
        c.Link(stairE, dayE[2]);
        WellRail(p, I0 + 4, StairK, I0 + 6, StairK + 1, Pitch, f.Steel, true);
        WellRail(p, I1 - 6, StairK, I1 - 4, StairK + 1, 2 * Pitch, f.Steel, false);

        // The way in: a porch through the middle of the south wing's front, into the corridor by a lobby.
        int lobby = p.Room(ModuleKind.Hall, "lobby", 0, 27, 0, K0 + 1, 32, 1, CorridorK0 - 2);
        p.Mouth(29, 0, K0, 32, 1, K0, p.Yard, lobby);
        p.Mouth(29, 0, CorridorK0 - 1, 32, 1, CorridorK0 - 1, lobby, corridors[0]);
        p.Lamp(2 * 29, 3, 2 * (K0 + 4), lobby, 1.4f, 13f);

        // The operating wing behind, top-lit, off the ground floor corridor.
        const int WingI0 = 26, WingI1 = 39, WingK1 = 42;
        int theatre = p.Hall("theatre", ModuleKind.Vault, 0, WingI0, K1, WingI1, WingK1, 0, 3, f.Wall, f.Roof);
        p.Mouth(32, 0, CorridorK1 + 1, 33, 1, K1, corridors[0], theatre);
        foreach (int i in new[] { WingI0 + 4, WingI1 - 4 }) p.Machine(i - 1, 0, 34, i + 1, 36, 1, f.Steel);
        p.Prop(2 * 32, 0, 2 * 35, BlockType.Bunk, Material.Steel, f.Steel.C);
        p.Lamp(66, 5, 72, theatre, 2f, 14f);
        p.Terminal(2 * (WingI0 + 1), 0, 2 * (WingK1 - 2), theatre);
        p.Cache(2 * (WingI1 - 2), 0, 2 * (WingK1 - 2), theatre);
        c.VaultModule = theatre;

        // Under it, the mortuary, down from the east day room.
        int down = p.Flight(I1 - 6, K0 + 3, Dir.East, -Pitch - 1, Pitch + 1, 2, "stair", -1);
        c.Link(dayE[0], down);
        p.Line(I1 - 6, K0 + 3, Dir.East, -Pitch - 1, Pitch + 1, 2, f.Plinth);
        int morgue = p.Tunnel("mortuary", -1, I1 - 22, K0 + 2, I1 - 7, K0 + 6, -Pitch - 1, 2, f.Plinth);
        c.Link(down, morgue);
        p.Level(-1, -Pitch - 1, "basement");
        for (int i = I1 - 21; i < I1 - 8; i += 2)
        {
            p.Prop(2 * i, 2 * (-Pitch - 1), 2 * (K0 + 2), BlockType.Locker, Material.Steel, f.Steel.C);
            p.Prop(2 * i, 2 * (-Pitch - 1), 2 * (K0 + 6) + 1, BlockType.Locker, Material.Steel, f.Steel.C);
        }
        p.Lamp(2 * (I1 - 14), 2 * (-Pitch - 1) + 3, 2 * (K0 + 4), morgue, 0.9f, 11f);
        p.Cache(2 * (I1 - 21), 2 * (-Pitch - 1), 2 * (K0 + 4), morgue);

        foreach (var (u, v) in new[] { (60, 8), (70, 8) }) p.Spawn(u, 0, v, p.Yard);
        p.Spawn(2 * (I0 + 4), 0, 2 * (K0 + 4), dayW[0]);
        p.Finish();
    }

    /// <summary>A side of wards along a corridor: each a room with a door to it and a window out, beds along its walls.</summary>
    static void Wards(Plot p, int i0, int i1, int k0, int k1, int j, int storey, int corridor, bool north)
    {
        Fab f = p.F;
        const int Bay = 7;
        int outer = north ? k0 - 1 : k1 + 1, wall = north ? k1 + 1 : k0 - 1;
        for (int a = i0; a < i1; a += Bay)
        {
            int b = Math.Min(a + Bay - 1, i1);
            if (b < i1) p.Fill(b, j, k0, b, j + 1, k1, Plot.Cube(f.Panel, Role.Wall));
            if (j == 0 && north && a <= 32 && b >= 29) continue; // the lobby takes this bay
            int room = p.Room(ModuleKind.Room, "ward", storey, a, j, k0, Math.Min(b, i1) - (b < i1 ? 1 : 0), j + 1, k1);
            p.Door(a + 2, j, wall, true, room, corridor);
            p.Glazed(a + 1, j + 1, outer, b - 2, outer);
            for (int i = a + 1; i < b - 1; i += 2)
            {
                p.Prop(2 * i, 2 * j, north ? 2 * k0 : 2 * k1 + 1, BlockType.Bunk, Material.Steel, f.Panel.C);
                if (i % 4 == 1) p.Prop(2 * i + 1, 2 * j, north ? 2 * k0 : 2 * k1 + 1, BlockType.Locker, Material.Steel, f.Steel.C);
            }
            p.Lamp(2 * a + 6, 2 * j + 3, 2 * ((k0 + k1) / 2), room, 1f, 10f);
            if ((a + j) % 3 == 0) p.Cache(2 * a + 1, 2 * j, north ? 2 * k1 : 2 * k0 + 1, room);
        }
    }

    // ---- a water works --------------------------------------------------------------------------

    /// <summary>
    /// A water works about 150 by 110 m: a long filter hall of sunken sand beds with a walk down the middle,
    /// a pump house, two round clear-water tanks, a gallery of pipes under it all, and a tower that shows
    /// for kilometres.
    /// </summary>
    public static void WaterWorks(Canvas c, Rng rng, LayoutHints? hints, TechLevel tech)
    {
        const int I0 = 8, I1 = 47, K0 = 8, K1 = 25, H = 4;
        var p = new Plot(c, rng, tech, hints?.Front ?? Dir.North);
        Wear(p);
        Fab f = p.F;
        p.Approach(27, 2, Dir.North);
        p.Pave(25, 3, 28, K0 - 1, f.Floor.Scale(0.85f));

        int hall = p.Hall("filter-hall", ModuleKind.Hall, 0, I0, K0, I1, K1, 0, H, f.Wall, f.Roof);
        p.Mouth(25, 0, K0, 28, 2, K0, hall, p.Yard);
        WindowsAlongI(p, I0 + 3, I1 - 3, 2, K0, 4, 6);
        WindowsAlongI(p, I0 + 3, I1 - 3, 2, K1, 4, 6);
        // Sand beds each side of the walk, a rail along each.
        for (int i = I0 + 2; i + 6 <= I1 - 2; i += 8)
            foreach (var (k0, k1) in new[] { (K0 + 2, K0 + 6), (K1 - 6, K1 - 2) })
                FilterBed(p, i, k0, i + 6, k1);
        for (int i = I0 + 5; i < I1 - 3; i += 8) p.Lamp(2 * i, 2 * H - 1, 2 * ((K0 + K1) / 2), hall, 1.6f, 18f);

        // The pump house.
        const int PumpI0 = 8, PumpK0 = K1 + 6;
        // Each spawn its own pump house (its own seed).
        var pumpSize = p.R.Fork("pumps");
        int PumpI1 = 22 + 2 * pumpSize.Range(0, 5), PumpK1 = PumpK0 + 12 + 2 * pumpSize.Range(0, 3);
        int pumps = p.Hall("pump-house", ModuleKind.Hall, 0, PumpI0, PumpK0, PumpI1, PumpK1, 0, H, f.Wall, f.Roof);
        p.Mouth(14, 0, PumpK0, 17, 2, PumpK0, pumps, p.Yard);
        for (int i = PumpI0 + 3; i < PumpI1 - 3; i += 5)
        {
            p.Machine(i, 0, PumpK0 + 5, i + 2, PumpK0 + 8, 2, f.Steel);
            p.Main(i + 1, 0, PumpK0 + 9, Dir.South, 1, f.Pipe);
        }
        p.Main(PumpI0 + 2, 0, PumpK0 + 10, Dir.East, PumpI1 - PumpI0 - 3, f.Pipe);
        for (int i = PumpI0 + 4; i < PumpI1 - 2; i += 6) p.Lamp(2 * i, 2 * H - 1, 2 * (PumpK0 + 3), pumps, 1.6f, 16f);

        // The switch room, off the pump house, where the good stuff was kept.
        int switches = p.Hall("switch-room", ModuleKind.Vault, 0, PumpI1, PumpK0 + 3, PumpI1 + 10, PumpK1 - 3, 0, 2, f.Panel, f.Roof);
        p.Door(PumpI1, 0, PumpK0 + 6, false, switches, pumps);
        for (int v = 2 * (PumpK0 + 4); v < 2 * (PumpK1 - 3); v += 2) p.Prop(2 * (PumpI1 + 9) + 1, 0, v, BlockType.Machine, Material.Steel, f.Steel.C);
        p.Terminal(2 * (PumpI1 + 2), 0, 2 * (PumpK0 + 4), switches);
        p.Cache(2 * (PumpI1 + 5), 0, 2 * (PumpK1 - 4), switches);
        p.Lamp(2 * (PumpI1 + 5), 3, 2 * (PumpK0 + 6), switches, 1.1f, 11f);
        c.VaultModule = switches;

        // Two round clear-water tanks, half sunk, a walk round them.
        foreach (int ck in new[] { 16, 42 }) Tank(p, 62, ck, 10, 4);
        p.Main(48, 2, 16, Dir.East, 4, f.Pipe);
        p.Main(37, 1, 42, Dir.East, 15, f.Pipe);

        // The pipe gallery under the hall, from the pump house.
        const int GalleryJ = -5;
        p.Line(18, PumpK0 + 11, Dir.West, GalleryJ, 5, 2, f.Plinth);
        int down = p.Flight(18, PumpK0 + 11, Dir.West, GalleryJ, 5, 2, "stair", -1);
        c.Link(pumps, down);
        int gallery = p.Tunnel("pipe-gallery", -1, 19, PumpK0 + 10, 44, PumpK0 + 13, GalleryJ, 3, f.Plinth);
        c.Link(down, gallery);
        p.Level(-1, GalleryJ, "pipe gallery");
        // Laid against the lining cell by cell, in the place's own frame: a 2 m main's corner moves when the place is turned.
        p.Pipe(40, 2 * GalleryJ + 4, 2 * (PumpK0 + 10), Dir.East, 48, f.Pipe);
        p.Pipe(40, 2 * GalleryJ + 4, 2 * (PumpK0 + 13) + 1, Dir.East, 48, f.PipeHot);
        for (int i = 22; i < 44; i += 7) p.Lamp(2 * i, 2 * GalleryJ + 5, 2 * (PumpK0 + 11) + 1, gallery, 1.2f, 12f);
        p.Cache(2 * 43, 2 * GalleryJ, 2 * (PumpK0 + 11), gallery);

        WaterTower(p, 44, 44);
        foreach (var (u, v) in new[] { (54, 10), (40, 30) }) p.Spawn(u, 0, v, p.Yard);
        p.Spawn(2 * 22, 0, 2 * (K0 + 8), hall);
        p.Finish();
    }

    static void FilterBed(Plot p, int i0, int k0, int i1, int k1)
    {
        Fab f = p.F;
        p.Fill(i0 - 1, -3, k0 - 1, i1 + 1, -1, k1 + 1, Plot.Cube(f.Plinth, Role.Wall));
        p.Dig(i0, -2, k0, i1, 0, k1);
        for (int v = 2 * k0; v <= 2 * k1 + 1; v++)
            for (int u = 2 * i0; u <= 2 * i1 + 1; u++)
                p.Wet(u, -4, v);
        var rail = new Block(BlockType.Rail, f.Steel.M, f.Steel.C, Role.Decor);
        for (int u = 2 * i0 - 1; u <= 2 * i1 + 2; u++)
            foreach (int v in new[] { 2 * k0 - 1, 2 * k1 + 2 }) p.Put(u, 0, v, rail);
        for (int v = 2 * k0 - 1; v <= 2 * k1 + 2; v++)
            foreach (int u in new[] { 2 * i0 - 1, 2 * i1 + 2 }) p.Put(u, 0, v, rail);
    }

    /// <summary>A round tank: a drum of whole blocks with a low cone of slopes for a lid.</summary>
    static void Tank(Plot p, double ci, double ck, double r, int top)
    {
        Fab f = p.F;
        p.Drum(ci, ck, r, 0, top, f.Wall, j => j == 0 ? f.Plinth : f.Wall);
        p.Disc(ci, ck, r, top + 1, Plot.Cube(f.Roof, Role.Roof));
        p.Weathers((int)ci - (int)r - 2, 0, (int)ck - (int)r - 2, (int)ci + (int)r + 2, top + 3, (int)ck + (int)r + 2, top + 1);
    }

    /// <summary>A tank high on six legs, braced, that shows for kilometres.</summary>
    static void WaterTower(Plot p, int ci, int ck)
    {
        Fab f = p.F;
        const int Legs = 16;
        foreach (var (di, dk) in new[] { (-5, -3), (5, -3), (-5, 3), (5, 3), (0, -6), (0, 6) })
            p.Fill(ci + di, 0, ck + dk, ci + di, Legs - 1, ck + dk, Plot.Cube(f.Steel, Role.Wall));
        p.Disc(ci + 0.5, ck + 0.5, 8, Legs, Plot.Cube(f.Plinth, Role.Floor));
        p.Drum(ci + 0.5, ck + 0.5, 8, Legs + 1, Legs + 5, f.Wall);
        p.Disc(ci + 0.5, ck + 0.5, 8, Legs + 6, Plot.Cube(f.Roof, Role.Roof));
        p.Weathers(ci - 10, 0, ck - 10, ci + 10, Legs + 8, ck + 10, Legs + 6);
    }

    // ---- a bunker -------------------------------------------------------------------------------

    /// <summary>
    /// A concrete hood in the wild, a blast door, a stair 30 m down, and one long tunnel with what kept people
    /// alive cut off it: bunks, a mess, a generator hall, and the armoury at the far end.
    /// </summary>
    public static void Bunker(Canvas c, Rng rng, LayoutHints? hints, TechLevel tech)
    {
        const int Hood0 = 6, Hood1 = 15, StairI = 19, StairK = Hood1 + 10, MainI0 = 18, MainI1 = 21, MainK0 = StairK + 1;
        // Each spawn dug to its own depth, its main tunnel its own length (its own seed).
        var size = rng.Fork("size");
        int J = -13 - 2 * size.Range(0, 3), MainK1 = 60 + 2 * size.Range(0, 9), Bottom = 2 * J;
        var p = new Plot(c, rng, tech, hints?.Front ?? Dir.North);
        Wear(p);
        Fab f = p.F;
        p.Approach(20, 2, Dir.North);

        // The hood: thick concrete with one door in it.
        int hood = p.Hall("hood", ModuleKind.Hall, 0, 15, Hood0, 24, Hood1, 0, 2, f.Wall, f.Roof);
        p.Door(20, 0, Hood0, true, hood, p.Yard);
        p.Gable(15, Hood0, 24, Hood1, 3, false, f.Roof, f.Wall, 0);
        p.Lamp(40, 3, 2 * Hood0 + 6, hood, 0.9f, 9f);

        // The long stair, down and back toward the tunnel, lined all the way.
        p.Line(StairI, StairK, Dir.North, J, -J, 2, f.Plinth);
        int down = p.Flight(StairI, StairK, Dir.North, J, -J, 2, "stair", -1);
        c.Link(hood, down);
        p.Level(-1, J, "tunnel");

        int main = p.Tunnel("main-tunnel", -1, MainI0, MainK0, MainI1, MainK1, J, 3, f.Plinth);
        c.Link(down, main);
        for (int k = MainK0 + 3; k < MainK1; k += 7) p.Lamp(2 * MainI0 + 3, Bottom + 5, 2 * k, main, 1.1f, 13f);
        p.Pipe(2 * MainI0, 2 * J + 4, 2 * MainK0, Dir.South, 2 * (MainK1 - MainK0 + 1), f.Pipe);

        // West side: bunks, then the generator.
        int bunks = p.Tunnel("bunk-room", -1, 5, MainK0 + 2, MainI0 - 2, MainK0 + 12, J, 2, f.Plinth);
        p.Door(MainI0 - 1, J, MainK0 + 7, false, bunks, main);
        for (int k = MainK0 + 3; k < MainK0 + 12; k += 2)
            foreach (int i in new[] { 6, 9, 12, 14 })
                p.Prop(2 * i, Bottom, 2 * k, BlockType.Bunk, Material.Steel, f.Panel.C);
        p.Lamp(2 * 10, Bottom + 3, 2 * (MainK0 + 7), bunks, 0.9f, 12f);
        p.Cache(2 * 5, Bottom, 2 * (MainK0 + 2), bunks);

        int power = p.Tunnel("generator-hall", -1, 3, MainK0 + 16, MainI0 - 2, MainK0 + 30, J, 4, f.Plinth);
        p.Mouth(MainI0 - 1, J, MainK0 + 22, MainI0 - 1, J + 2, MainK0 + 25, power, main);
        foreach (int i in new[] { 5, 10 }) p.Machine(i, J, MainK0 + 19, i + 3, MainK0 + 27, 2, f.Steel);
        p.Lamp(2 * 9, Bottom + 7, 2 * (MainK0 + 23), power, 1.4f, 15f);

        // East side: the mess, the stores.
        int mess = p.Tunnel("mess", -1, MainI1 + 2, MainK0 + 2, 34, MainK0 + 13, J, 2, f.Plinth);
        p.Door(MainI1 + 1, J, MainK0 + 7, false, mess, main);
        for (int k = MainK0 + 4; k < MainK0 + 12; k += 3)
            for (int i = MainI1 + 4; i < 33; i += 2)
                p.Prop(2 * i, Bottom, 2 * k, BlockType.Table, Material.Steel, f.Steel.C);
        p.Lamp(2 * 28, Bottom + 3, 2 * (MainK0 + 7), mess, 0.9f, 12f);

        int stores = p.Tunnel("stores", -1, MainI1 + 2, MainK0 + 17, 32, MainK0 + 28, J, 2, f.Plinth);
        p.Door(MainI1 + 1, J, MainK0 + 22, false, stores, main);
        for (int k = MainK0 + 18; k < MainK0 + 28; k += 3)
            for (int i = MainI1 + 3; i < 31; i += 1)
                p.Prop(2 * i, Bottom, 2 * k, BlockType.Shelf, Material.Steel, f.Steel.C);
        p.Lamp(2 * 27, Bottom + 3, 2 * (MainK0 + 22), stores, 0.8f, 11f);
        p.Cache(2 * 31, Bottom, 2 * (MainK0 + 27), stores);

        // The far end: the armoury behind the last door.
        int armoury = p.Tunnel("armoury", -1, 16, MainK1 + 2, 23, MainK1 + 9, J, 2, f.Plinth, ModuleKind.Vault);
        p.Door(19, J, MainK1 + 1, true, armoury, main);
        for (int i = 17; i < 23; i += 1) p.Prop(2 * i, Bottom, 2 * (MainK1 + 9) + 1, BlockType.Locker, Material.Steel, f.Steel.C);
        p.Terminal(2 * 16, Bottom, 2 * (MainK1 + 5), armoury);
        p.Cache(2 * 22, Bottom, 2 * (MainK1 + 8), armoury);
        p.Cache(2 * 17, Bottom, 2 * (MainK1 + 8), armoury);
        p.Lamp(2 * 19, Bottom + 3, 2 * (MainK1 + 5), armoury, 0.8f, 10f);
        c.VaultModule = armoury;

        p.Spawn(2 * MainI0 + 2, Bottom, 2 * (MainK0 + 10), main);
        p.Spawn(2 * MainI0 + 2, Bottom, 2 * (MainK0 + 30), main);
        p.Spawn(2 * 10, Bottom, 2 * (MainK0 + 7), bunks);
        p.Finish();
    }
}
