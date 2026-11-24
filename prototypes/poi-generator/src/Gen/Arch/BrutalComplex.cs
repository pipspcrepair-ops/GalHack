#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// A vast sealed industrial complex in concrete and steel, seen from kilometres off: a walled precinct
/// with a gate you could drive a rig through, a stepped process block 220 m long ribbed with buttresses,
/// a stack, a cooling tower, a tall tower with a crown, and pipework and gantries running between them
/// over hazard-striped yards. Inside it reads as machinery with people as an afterthought: a concourse
/// 8 m wide and 6 m high down its whole length, a cargo bay, a turbine hall, a reactor hall you look into
/// from a control room, coolant tunnels under it all, and only here and there a passage made for one
/// person. About 380 by 340 m on the player's 2 m blocks.
/// </summary>
public static class BrutalComplex
{
    const int W = 190, D = 168;
    const int ShellI0 = 40, ShellI1 = 150, ShellK0 = 24, ShellK1 = 70, ShellH = 13;

    public static void Compose(Canvas c, Rng rng, LayoutHints? hints, TechLevel tech)
    {
        var p = new Plot(c, rng, tech, hints?.Front ?? Dir.North);
        Fab f = p.F;
        c.X.PlainDoor = f.Accent.C;
        c.X.PlainDoorSteel = true;
        p.Approach(94, 3, Dir.North);
        p.Level(-3, -6, "coolant level");
        p.Level(1, 4, "upper deck");

        Precinct(p);
        Massing(p);

        // ---- the rooms inside the shell ---------------------------------------------------------
        int cargo = CargoBay(p);
        int conc = p.Hall("concourse", ModuleKind.Hall, 0, 41, 44, 149, 49, 0, 3, f.Panel, weather: false);
        p.Mouth(90, 0, 44, 97, 2, 44, cargo, conc);
        Concourse(p, conc);
        int turbine = TurbineHall(p, conc);
        int reactor = ReactorHall(p, conc);
        int mess = Mess(p, conc);
        int vault = Stores(p, conc);
        c.VaultModule = vault;
        int upper = UpperDeck(p, cargo, reactor);
        Coolant(p, reactor, turbine);
        c.Link(p.Yard, cargo);

        // ---- the towers, the stack, the cooling tower, and the pipes between them --------------------
        // Its skyline its own (its own seed): the office tower, the stack and the crane each their own height.
        var sky = p.R.Fork("skyline");
        int lobby = Tower(p, 58, 86, 14, 38 + 2 * sky.Range(0, 9));
        c.Link(p.Yard, lobby);
        Stack(p, 122, 92, 6, 34 + 2 * sky.Range(0, 9));
        int cooling = CoolingTower(p, 166, 118);
        c.Link(p.Yard, cooling);
        TankFarm(p, 16, 112);
        LandingPad(p, 104, 136);
        PipeYard(p);

        foreach (var (u, v) in new[] { (190, 40), (200, 70), (150, 170), (240, 170), (90, 200), (300, 200) }) p.Spawn(u, 0, v, p.Yard);
        p.Finish();
    }

    // ---- the outside --------------------------------------------------------------------------

    /// <summary>A wall 10 m high round the whole precinct, buttressed, and a gate portal 20 m high with its number over it.</summary>
    static void Precinct(Plot p)
    {
        Fab f = p.F;
        const int GateI0 = 88, GateI1 = 99;
        foreach (var (i, k) in Ring(0, 0, W, D))
        {
            if (k == 0 && i >= GateI0 && i <= GateI1) continue;
            bool foot = (i + k) % 10 == 0;
            p.Fill(i, 0, k, i, 4, k, Plot.Cube((i + k) % 10 == 0 ? f.Plinth : f.Wall, Role.Wall));
            if (foot)
            {
                int oi = i == 0 ? -1 : i == W ? 1 : 0, ok = k == 0 ? -1 : k == D ? 1 : 0;
                p.Fill(i + oi, 0, k + ok, i + oi, 3, k + ok, Plot.Cube(f.Plinth, Role.Wall));
            }
        }
        // Each run of wall weathers on its own: a box round the whole precinct would take the block with it.
        p.Weathers(-2, 0, -2, W + 2, 6, 2, 4);
        p.Weathers(-2, 0, D - 2, W + 2, 6, D + 2, 4);
        p.Weathers(-2, 0, 3, 2, 6, D - 3, 4);
        p.Weathers(W - 2, 0, 3, W + 2, 6, D - 3, 4);
        // The portal: two piers, a lintel 8 m deep.
        foreach (int i in new[] { GateI0 - 4, GateI1 + 1 }) p.Fill(i, 0, -2, i + 3, 9, 2, Plot.Cube(f.Wall, Role.Wall));
        p.Fill(GateI0, 6, -2, GateI1, 9, 2, Plot.Cube(f.Wall, Role.Wall));
        p.Repaint(2 * GateI0, 13, -4, 2 * GateI1 + 1, 19, -4, (_, _, _) => f.Plinth.C);
        foreach (int u in new[] { 2 * GateI0 - 1, 2 * GateI1 + 2 })
            p.Stripes(u, 0, -4, u, 11, 5);
        p.Stripes(2 * GateI0, 12, -4, 2 * GateI1 + 1, 12, 5);
        p.Stencil("07", 2 * GateI0 + 2, 14, -4, true, f.Hazard, 1, true);
        p.Stencil("07", 2 * (GateI0 + 7), 14, -4, true, f.Hazard, 1, true);
        // A guard's box each side, for a person, inside the wall.
        foreach (int i in new[] { GateI0 - 8, GateI1 + 4 })
        {
            int box = p.Hall("guard-post", ModuleKind.Room, 0, i, 3, i + 4, 8, 0, 2, f.Plinth, weather: false);
            p.Door(i + 2, 0, 8, true, box, p.Yard);
            p.Glazed(i + 1, 0, 3, i + 3, 3);
            p.Spawn(2 * i + 4, 0, 2 * 5, box);
        }
        // The road in, and round the block.
        Stuff2 road = new(f.Floor.M, f.Floor.C.Scale(0.8f));
        p.Pave(90, -2, 97, ShellK0, road);
        p.Pave(6, 12, 184, 19, road);
        p.Pave(32, 12, 39, 160, road);
        p.Pave(152, 12, 159, 160, road);
        p.Pave(6, 72, 184, 75, road);
    }

    static IEnumerable<(int I, int K)> Ring(int i0, int k0, int i1, int k1)
    {
        for (int k = k0; k <= k1; k++)
            for (int i = i0; i <= i1; i++)
                if (i == i0 || i == i1 || k == k0 || k == k1) yield return (i, k);
    }

    /// <summary>The block's mass: three stepped tiers, buttress fins down the long faces, slit windows in rows, a roofscape of vents.</summary>
    static void Massing(Plot p)
    {
        Fab f = p.F;
        // The base tier's walls and roof; everything inside is rooms hung on them.
        for (int k = ShellK0; k <= ShellK1; k++)
            for (int i = ShellI0; i <= ShellI1; i++)
            {
                bool edge = i == ShellI0 || i == ShellI1 || k == ShellK0 || k == ShellK1;
                if (edge) p.Fill(i, 0, k, i, ShellH - 1, k, Plot.Cube(f.Wall, Role.Wall));
                p.Fill(i, ShellH, k, i, ShellH, k, Plot.Cube(f.Roof, Role.Roof));
            }
        p.Plinth(ShellI0, ShellK0, ShellI1, ShellK1, 0);
        p.Plinth(ShellI0, ShellK0, ShellI1, ShellK1, 1);
        p.Weathers(ShellI0 - 5, 0, ShellK0 - 5, ShellI1 + 5, ShellH + 1, ShellK1 + 5, ShellH);
        // Fins: a block thick, standing 8 m proud, full height, every 16 m down the long sides.
        for (int i = ShellI0 + 4; i < ShellI1 - 2; i += 8)
        {
            if (i >= 86 && i <= 100) continue;
            foreach (var (k0, k1) in new[] { (ShellK0 - 4, ShellK0 - 1), (ShellK1 + 1, ShellK1 + 4) })
            {
                p.Fill(i, 0, k0, i, ShellH + 1, k1, Plot.Cube(f.Wall.Scale(0.92f), Role.Wall));
                p.Fill(i, 0, k0, i, 1, k1, Plot.Cube(f.Plinth, Role.Wall));
            }
            // Slit windows between the fins, in the dark.
            foreach (int j in new[] { 9, 11 })
            {
                p.Repaint(2 * i + 2, 2 * j, 2 * ShellK0, 2 * i + 13, 2 * j, 2 * ShellK0, (_, _, _) => f.Black);
                p.Repaint(2 * i + 2, 2 * j, 2 * ShellK1 + 1, 2 * i + 13, 2 * j, 2 * ShellK1 + 1, (_, _, _) => f.Black);
            }
        }
        Tier(p, 52, 30, 138, 64, ShellH + 1, 9);
        Tier(p, 72, 36, 118, 58, ShellH + 10, 8);
        // The high-tech build carries a band of colour at each setback; the older one a hazard course.
        foreach (int j in new[] { ShellH })
            p.Repaint(2 * ShellI0, 2 * j, 2 * ShellK0, 2 * ShellI1 + 1, 2 * j + 1, 2 * ShellK0 + 1, (u, y, v) => p.Tech == TechLevel.High ? f.Accent.C : ((u + y) >> 1 & 1) == 0 ? f.Hazard : f.Black);
        p.Stencil("4", 2 * 46, 10, 2 * ShellK0, true, f.Hazard, 2);
        p.Stencil("4", 2 * 138, 10, 2 * ShellK0, true, f.Hazard, 2);
        // Vent hoods on the roof.
        for (int i = ShellI0 + 4; i < 50; i += 3)
            for (int k = ShellK0 + 4; k < ShellK1 - 4; k += 8) p.Machine(i, ShellH + 1, k, i + 1, k + 3, 1, f.Rust);
    }

    /// <summary>One setback of the mass, hollow: walls, a roof, a course of dark slits.</summary>
    static void Tier(Plot p, int i0, int k0, int i1, int k1, int j, int h)
    {
        Fab f = p.F;
        for (int k = k0; k <= k1; k++)
            for (int i = i0; i <= i1; i++)
            {
                bool edge = i == i0 || i == i1 || k == k0 || k == k1;
                if (edge) p.Fill(i, j, k, i, j + h - 1, k, Plot.Cube((j + i) % 4 == 0 ? f.Panel : f.Wall, Role.Wall));
                p.Fill(i, j + h - 1, k, i, j + h - 1, k, Plot.Cube(f.Roof, Role.Roof));
            }
        p.Weathers(i0 - 1, j, k0 - 1, i1 + 1, j + h + 1, k1 + 1, j + h - 1);
        for (int y = 2 * j + 4; y < 2 * (j + h) - 4; y += 4)
        {
            p.Repaint(2 * i0, y, 2 * k0, 2 * i1 + 1, y, 2 * k0, (u, _, _) => u % 8 < 6 ? f.Black : f.Wall.C);
            p.Repaint(2 * i0, y, 2 * k1 + 1, 2 * i1 + 1, y, 2 * k1 + 1, (u, _, _) => u % 8 < 6 ? f.Black : f.Wall.C);
        }
    }

    // ---- the rooms ----------------------------------------------------------------------------

    /// <summary>A bay 44 m wide and 14 m high behind a mouth a rig could drive through: stacked containers, an overhead crane, a loading pit.</summary>
    static int CargoBay(Plot p)
    {
        Fab f = p.F;
        const int I0 = 84, I1 = 105, K0 = 24, K1 = 44;
        int room = p.Hall("cargo-bay", ModuleKind.Hall, 0, I0, K0, I1, K1, 0, 7, f.Wall, weather: false);
        // The mouth, framed in hazard stripes and a heavy lintel.
        p.Mouth(90, 0, K0, 97, 4, K0, p.Yard, room);
        p.Fill(88, 5, K0 - 2, 99, 6, K0 - 1, Plot.Cube(f.Wall.Scale(0.85f), Role.Wall));
        foreach (int i in new[] { 88, 98 }) p.Fill(i, 0, K0 - 2, i + 1, 4, K0 - 1, Plot.Cube(f.Wall.Scale(0.85f), Role.Wall));
        p.Stripes(2 * 88 + 3, 0, 2 * K0 - 4, 2 * 88 + 3, 9, 2 * K0 + 1);
        p.Stripes(2 * 98, 0, 2 * K0 - 4, 2 * 98, 9, 2 * K0 + 1);
        p.Stripes(2 * 90, 10, 2 * K0 - 4, 2 * 97 + 1, 10, 2 * K0 + 1);
        // Containers in stacks down both sides.
        Rgb[] paint = { new(0x7a, 0x3a, 0x2a), new(0x2e, 0x4c, 0x5e), new(0x74, 0x6a, 0x3a), new(0x5a, 0x5e, 0x60) };
        for (int k = K0 + 2; k < K1 - 4; k += 4)
            foreach (int i in new[] { I0 + 1, I1 - 4 })
            {
                int stack = 1 + (int)(Hash.Cell(new Int3(i, 0, k), 401) % 3);
                for (int h = 0; h < stack; h++)
                {
                    Rgb c = paint[Hash.Cell(new Int3(i, h, k), 403) % 4];
                    p.Fill(i, h, k, i + 3, h, k + 2, new Block(BlockType.Crate, Material.Steel, c, Role.Prop));
                }
            }
        // The crane bridge across the bay, its rails along the walls.
        foreach (int i in new[] { I0 + 1, I1 - 1 })
            p.Fill(i, 6, K0 + 1, i, 6, K1 - 1, Plot.Cube(f.Rust.Scale(0.8f), Role.Decor));
        int craneK = K0 + 8 + p.R.Range(0, 4) * 2;
        p.Fill(I0 + 1, 6, craneK, I1 - 1, 6, craneK + 1, Plot.Cube(new Stuff2(f.Steel.M, f.Hazard), Role.Decor));
        for (int y = 11; y >= 4; y--) p.Put(2 * 94, y, 2 * craneK, new Block(BlockType.Cable, Material.Steel, f.Black, Role.Decor));
        for (int k = K0 + 3; k < K1 - 2; k += 6) p.Lamp(2 * 94, 2 * 7 - 1, 2 * k, room, 2.2f, 20f);
        p.Spawn(2 * 94, 0, 2 * 30, room);
        p.Spawn(2 * 96, 0, 2 * 40, room);
        return room;
    }

    /// <summary>The way down the middle of everything: ribbed, numbered, a hazard line along each wall, a light strip overhead.</summary>
    static void Concourse(Plot p, int room)
    {
        Fab f = p.F;
        const int I0 = 42, I1 = 148, K0 = 45, K1 = 48;
        // Ribs: a pair of columns every 8 blocks, standing a metre out of each wall, with a beam dropped across.
        for (int i = I0 + 2; i < I1; i += 6)
        {
            foreach (int v in new[] { 2 * K0, 2 * K1 + 1 })
                for (int y = 0; y < 5; y++)
                    foreach (int du in new[] { 0, 1 })
                        p.Put(2 * i + du, y, v, Plot.Cube(f.Plinth, Role.Wall));
            for (int v = 2 * K0; v <= 2 * K1 + 1; v++)
                foreach (int du in new[] { 0, 1 })
                    p.Put(2 * i + du, 5, v, Plot.Cube(f.Plinth, Role.Roof));
            // Every other bay its number.
            if ((i - I0) % 12 == 2)
            {
                string n = ((i - I0) / 6 + 1).ToString("00");
                p.Stencil(n, 2 * i + 4, 1, 2 * K0 - 1, true, f.Hazard, 1);
            }
        }
        for (int u = 2 * I0; u <= 2 * I1 + 1; u++)
        {
            bool stripe = (u >> 2 & 1) == 0;
            foreach (int v in new[] { 2 * K0 + 1, 2 * K1 })
                p.Lay(u, 0, v, new Block(BlockType.Plate, Material.Concrete, stripe ? f.Hazard : f.Black, Role.Floor));
        }
        for (int i = I0 + 5; i < I1; i += 6)
        {
            p.Lamp(2 * i, 6, 2 * K0 + 3, room, 1.6f, 14f);
            p.Lamp(2 * i, 6, 2 * K0 + 4, room, 1.6f, 14f);
        }
        p.Spawn(2 * 60, 0, 2 * 46, room);
        p.Spawn(2 * 120, 0, 2 * 47, room);
    }

    /// <summary>Three turbine sets lying down the hall, each 40 m long, their steam mains rising through the roof.</summary>
    static int TurbineHall(Plot p, int conc)
    {
        Fab f = p.F;
        const int I0 = 42, I1 = 84, K0 = 49, K1 = 68;
        int room = p.Hall("turbine-hall", ModuleKind.Hall, 0, I0, K0, I1, K1, 0, 9, f.Panel, weather: false);
        p.Mouth(60, 0, K0, 63, 2, K0, room, conc);
        p.Stripes(2 * 60 - 1, 0, 2 * K0, 2 * 60 - 1, 5, 2 * K0 + 1);
        p.Stripes(2 * 64, 0, 2 * K0, 2 * 64, 5, 2 * K0 + 1);
        foreach (int k in new[] { 52, 58, 64 })
        {
            // Plinth, casing stepped up to its middle, a generator at the end.
            p.Fill(I0 + 4, 0, k, I1 - 4, 0, k + 3, Plot.Cube(f.Plinth, Role.Prop));
            for (int n = 0; n < 3; n++)
                p.Fill(I0 + 6 + n * 3, 1, k, I1 - 16 - n * 3, 2 + n, k + 3, new Block(BlockType.Cube, f.Rust.M, f.Rust.C.Scale(1f - n * 0.08f), Role.Prop));
            p.Fill(I1 - 13, 1, k - 1, I1 - 6, 3, k + 3, new Block(BlockType.Cube, f.Accent.M, f.Accent.C, Role.Prop));
            p.Main(I0 + 16, 5, k + 1, Dir.Up, 4, f.PipeHot);
        }
        for (int i = I0 + 3; i < I1 - 1; i += 8)
            foreach (int k in new[] { 50, 56, 62 })
                p.Lamp(2 * i, 2 * 9 - 1, 2 * k + 1, room, 2.0f, 18f);
        p.Cache(2 * (I1 - 2), 0, 2 * (K1 - 2), room);
        p.Spawn(2 * 50, 0, 2 * 50 + 1, room);
        return room;
    }

    /// <summary>A hall 26 m high round a great vessel: a catwalk ring at the control room's level, a flight down to the floor, hazard paint on everything near it.</summary>
    static int ReactorHall(Plot p, int conc)
    {
        Fab f = p.F;
        const int I0 = 104, I1 = 148, K0 = 49, K1 = 69;
        int room = p.Hall("reactor-hall", ModuleKind.Hall, 0, I0, K0, I1, K1, 0, ShellH, f.Wall, weather: false);
        p.Mouth(114, 0, K0, 117, 2, K0, room, conc);
        p.Stripes(2 * 113 + 1, 0, 2 * K0, 2 * 113 + 1, 7, 2 * K0 + 1);
        p.Stripes(2 * 118, 0, 2 * K0, 2 * 118, 7, 2 * K0 + 1);
        p.Stripes(2 * 114, 6, 2 * K0, 2 * 117 + 1, 7, 2 * K0 + 1);
        const int Ci = 126, Ck = 59;
        const double R = 7;
        Stuff2 skin = p.Tech == TechLevel.High ? f.Panel : f.Rust;
        p.Drum(Ci, Ck, R, 0, 10, skin, j => j % 3 == 1 ? new Stuff2(skin.M, skin.C.Scale(0.75f)) : skin);
        p.Disc(Ci, Ck, R - 1, 10, Plot.Cube(skin.Scale(0.8f), Role.Roof));
        p.Disc(Ci, Ck, R - 2, 11, Plot.Cube(skin.Scale(0.7f), Role.Roof));
        // A floor line round it you'd not cross.
        for (int v = 2 * (Ck - 11); v < 2 * (Ck + 11); v++)
            for (int u = 2 * (Ci - 11); u < 2 * (Ci + 11); u++)
            {
                double d = Math.Sqrt(Math.Pow(u / 2.0 - Ci, 2) + Math.Pow(v / 2.0 - Ck, 2));
                if (d > 9.2 && d < 10.2) p.Lay(u, 0, v, new Block(BlockType.Plate, Material.Concrete, ((u + v) >> 1 & 1) == 0 ? f.Hazard : f.Black, Role.Floor));
            }
        // Coolant mains off the vessel, through the walls.
        foreach (int k in new[] { 56, 61 })
        {
            p.Main(Ci + 7, 1, k, Dir.East, 15, f.PipeHot);
            p.Main(I0 + 1, 1, k, Dir.East, Ci - 7 - I0 - 1, f.PipeHot);
        }
        // The catwalk ring, at the control room's floor, and the walk out to it.
        int ring = Catwalk(p, Ci, Ck, 9, 4);
        int walk = p.Gantry("catwalk", 1, 118, 50, 119, 54, 4, 0);
        p.C.Link(ring, walk);
        int down = p.Flight(114, 58, Dir.East, 0, 4, 2, "stair");
        p.C.Link(room, down);
        p.C.Link(down, ring);
        for (int a = 0; a < 8; a++)
        {
            double t = a * Math.PI / 4 + 0.39;
            p.Lamp((int)(2 * Ci + Math.Cos(t) * 30), 2 * ShellH - 1, (int)(2 * Ck + Math.Sin(t) * 16), room, 2.4f, 24f, a % 3 == 0 ? LightMode.Emergency : LightMode.Steady);
        }
        p.Spawn(2 * 108, 0, 2 * 52, room);
        p.Spawn(2 * 142, 0, 2 * 64, room);
        p.Spawn(2 * 108, 0, 2 * 66, room);
        return room;
    }

    /// <summary>A round walkway of steel grating at level j, a rail on its outer edge, on posts.</summary>
    static int Catwalk(Plot p, int ci, int ck, double r, int j)
    {
        Fab f = p.F;
        int y = 2 * j, rr = (int)r + 2;
        var cells = new List<(int, int, int)>();
        for (int k = ck - rr; k <= ck + rr; k++)
            for (int i = ci - rr; i <= ci + rr; i++)
                if (Plot.In(ci, ck, r, i, k) && !Plot.In(ci, ck, r - 2, i, k))
                {
                    for (int v = 2 * k; v < 2 * k + 2; v++)
                        for (int u = 2 * i; u < 2 * i + 2; u++)
                        {
                            p.Put(u, y - 1, v, new Block(BlockType.Lattice, f.Grating.M, f.Grating.C, Role.Floor));
                            for (int up = 0; up < 3; up++) cells.Add((u, y + up, v));
                        }
                }
        for (int a = 0; a < 12; a++)
        {
            double t = a * Math.PI / 6 + 0.13;
            p.Post((int)(2 * ci + Math.Cos(t) * (2 * r - 2)), y - 2, (int)(2 * ck + Math.Sin(t) * (2 * r - 2)), f.Steel);
        }
        return p.RoomOf(ModuleKind.Corridor, "catwalk", 1, cells);
    }

    /// <summary>The crew's end of it: a mess and lockers, down a passage made for people. The only narrow place in the block, and it feels it.</summary>
    static int Mess(Plot p, int conc)
    {
        Fab f = p.F;
        int mess = p.Hall("mess", ModuleKind.Hall, 0, 42, 28, 62, 44, 0, 3, f.Panel, weather: false);
        p.Door(52, 0, 44, true, mess, conc);
        for (int i = 45; i < 60; i += 3)
            for (int k = 31; k < 42; k += 4)
            {
                p.Lay(2 * i, 0, 2 * k, new Block(BlockType.Table, Material.Steel, f.Steel.C, Role.Prop));
                p.Lay(2 * i + 1, 0, 2 * k, new Block(BlockType.Table, Material.Steel, f.Steel.C, Role.Prop));
            }
        for (int i = 44; i < 60; i += 5) p.Lamp(2 * i, 5, 2 * 36, mess, 1.2f, 12f);
        int lockers = p.Hall("lockers", ModuleKind.Hall, 0, 62, 28, 82, 44, 0, 3, f.Panel, weather: false);
        p.Door(62, 0, 36, false, mess, lockers);
        for (int k = 30; k < 43; k += 3)
            for (int u = 2 * 65; u < 2 * 80; u++)
                p.Lay(u, 0, 2 * k, new Block(BlockType.Locker, Material.Steel, f.Accent.C, Role.Prop));
        p.Cache(2 * 80, 0, 2 * 42, lockers);
        return mess;
    }

    /// <summary>The parts store, racks to the ceiling, and behind a steel door the cage where what mattered was kept.</summary>
    static int Stores(Plot p, int conc)
    {
        Fab f = p.F;
        int store = p.Hall("parts-store", ModuleKind.Hall, 0, 106, 28, 134, 44, 0, 3, f.Panel, weather: false);
        p.Door(120, 0, 44, true, store, conc);
        for (int k = 31; k < 42; k += 3)
            for (int u = 2 * 109; u < 2 * 132; u++)
                for (int y = 0; y < 3; y++)
                    if (u % 12 != 0) p.Lay(u, y, 2 * k, new Block(BlockType.Shelf, Material.Steel, f.Steel.C, Role.Prop));
        int vault = p.Hall("vault", ModuleKind.Vault, 0, 134, 28, 146, 44, 0, 3, f.Plinth, weather: false);
        p.Door(134, 0, 36, false, store, vault);
        p.Cache(2 * 144, 0, 2 * 30, vault);
        p.Cache(2 * 144, 0, 2 * 42, vault);
        p.Terminal(2 * 136, 0, 2 * 30, vault);
        p.Lamp(2 * 140, 5, 2 * 36, vault, 1.2f, 12f, LightMode.Emergency);
        return vault;
    }

    /// <summary>The upper deck: a gallery round the cargo bay, a corridor over the concourse, the control room looking into the reactor hall.</summary>
    static int UpperDeck(Plot p, int cargo, int reactor)
    {
        Fab f = p.F;
        int up = p.Flight(86, 42, Dir.East, 0, 4, 2, "stair");
        p.C.Link(cargo, up);
        int gallery = p.Gantry("gallery", 1, 90, 42, 103, 43, 4, 4);
        p.C.Link(up, gallery);
        int upper = p.Hall("corridor", ModuleKind.Hall, 1, 62, 44, 106, 49, 4, 3, f.Panel, weather: false);
        p.Mouth(94, 4, 44, 97, 5, 44, gallery, upper);
        p.Unrail(2 * 94, 8, 87, 2 * 97 + 1, 87);
        for (int i = 66; i < 104; i += 6) p.Lamp(2 * i, 13, 2 * 46 + 1, upper, 1.3f, 12f);
        int control = p.Hall("control", ModuleKind.Hall, 1, 106, 44, 128, 49, 4, 3, f.Panel, weather: false);
        p.Mouth(106, 4, 46, 106, 5, 47, upper, control);
        // Its long window over the reactor hall, desks along it, the door out to the catwalk.
        p.Glazed(108, 5, 49, 116, 49);
        p.Glazed(121, 5, 49, 127, 49);
        p.Mouth(118, 4, 49, 119, 5, 49, control, reactor);
        for (int u = 2 * 108; u < 2 * 127; u += 2)
            if (u < 2 * 117 || u > 2 * 120) p.Lay(u, 8, 2 * 48, new Block(BlockType.Bench, Material.Steel, f.Steel.C, Role.Prop));
        p.Terminal(2 * 112, 8, 2 * 46, control);
        p.Terminal(2 * 124, 8, 2 * 46, control);
        p.Cache(2 * 126, 8, 2 * 45, control);
        p.Lamp(2 * 118, 13, 2 * 46, control, 1.2f, 12f, LightMode.Emergency);
        return upper;
    }

    /// <summary>Under it all: a tunnel 8 by 6 m full of coolant mains from the reactor to the turbines, and a pump chamber half way.</summary>
    static void Coolant(Plot p, int reactor, int turbine)
    {
        Fab f = p.F;
        const int J = -6;
        int tunnel = p.Tunnel("coolant-tunnel", -3, 60, 50, 146, 53, J, 3, f.Plinth.Scale(1.15f));
        int down = p.Flight(142, 54, Dir.South, J, 6, 2, "stair", -3);
        p.C.Link(tunnel, down);
        p.C.Link(down, reactor);
        p.Dig(142, 0, 60, 143, 1, 61);
        foreach (int v in new[] { 100, 106 })
            p.Main(62, J + 2, v >> 1, Dir.East, 82, v == 100 ? f.PipeHot : f.Rust);
        for (int i = 64; i < 144; i += 6)
            foreach (int v in new[] { 100, 101, 106, 107 }) p.Put(2 * i, 2 * J + 5, v, Plot.Cube(f.Steel, Role.Decor));
        for (int i = 66; i < 144; i += 8) p.Lamp(2 * i, 2 * J + 6, 2 * 51 + 1, tunnel, 1.4f, 13f, i % 24 == 18 ? LightMode.Emergency : LightMode.Steady);
        int chamber = p.Tunnel("pump-chamber", -3, 84, 54, 104, 66, J, 4, f.Plinth.Scale(1.15f));
        p.C.Link(tunnel, chamber);
        for (int n = 0; n < 4; n++) p.Machine(86 + n * 5, J, 58, 88 + n * 5, 62, 2, f.Rust);
        p.Lamp(2 * 94, 2 * J + 8 - 1, 2 * 60, chamber, 1.2f, 14f, LightMode.Emergency);
        p.Cache(2 * 102, 2 * J, 2 * 65, chamber);
        p.Spawn(2 * 94, 2 * J, 2 * 55, chamber);
        p.Spawn(2 * 130, 2 * J, 2 * 52, tunnel);
    }

    // ---- what stands in the yard ---------------------------------------------------------------

    /// <summary>A square tower 92 m tall, stepped in at a third and two thirds, crowned with a wider head of dark glass, a stair inside it to the bridge over the block.</summary>
    static int Tower(Plot p, int i0, int k0, int side, int h)
    {
        Fab f = p.F;
        int i1 = i0 + side, k1 = k0 + side;
        int lobby = p.Hall("tower-lobby", ModuleKind.Hall, 0, i0, k0, i1, k1, 0, 6, f.Wall);
        p.Mouth((i0 + i1) / 2 - 1, 0, k0, (i0 + i1) / 2 + 1, 2, k0, lobby, p.Yard); // a way in for people: no hazard stripes round it
        // Solid-looking shafts stepped in; hollow, walls only.
        int j = 7;
        foreach (var (inset, until) in new[] { (0, h / 3), (1, 2 * h / 3), (2, h - 6) })
        {
            int a0 = i0 + inset, a1 = i1 - inset, b0 = k0 + inset, b1 = k1 - inset;
            // The step in: a ledge from the wall below to the wall above.
            if (inset > 0)
                foreach (var (i, k) in Ring(a0 - 1, b0 - 1, a1 + 1, b1 + 1)) p.Fill(i, j, k, i, j, k, Plot.Cube(f.Plinth, Role.Wall));
            for (; j < until; j++)
                foreach (var (i, k) in Ring(a0, b0, a1, b1))
                    p.Fill(i, j, k, i, j, k, Plot.Cube((i - a0) % 3 == 0 || (k - b0) % 3 == 0 ? f.Wall : f.Panel, Role.Wall));
        }
        for (int k = k0 + 2; k <= k1 - 2; k++)
            for (int i = i0 + 2; i <= i1 - 2; i++) p.Fill(i, j, k, i, j, k, Plot.Cube(f.Roof, Role.Roof));
        // The head: wider than the shaft, ringed in dark glass, on a bracket course.
        for (int t = 0; t < 2; t++)
            foreach (var (i, k) in Ring(i0 + 1 - t, k0 + 1 - t, i1 - 1 + t, k1 - 1 + t))
                p.Fill(i, j + t, k, i, j + t, k, Plot.Cube(f.Plinth, Role.Wall));
        foreach (var (i, k) in Ring(i0 - 2, k0 - 2, i1 + 2, k1 + 2))
            p.Fill(i, j + 2, k, i, j + 4, k, new Block(BlockType.Cube, Material.Glass, f.Black, Role.Window));
        for (int k = k0 - 2; k <= k1 + 2; k++)
            for (int i = i0 - 2; i <= i1 + 2; i++)
            {
                p.Fill(i, j + 1, k, i, j + 1, k, Plot.Cube(f.Plinth, Role.Floor));
                p.Fill(i, j + 5, k, i, j + 5, k, Plot.Cube(f.Roof, Role.Roof));
            }
        p.Weathers(i0 - 3, 0, k0 - 3, i1 + 3, j + 7, k1 + 3, j + 5);
        // A mast and its beacon.
        int mi = (i0 + i1) / 2, mk = (k0 + k1) / 2;
        for (int y = 2 * (j + 6); y < 2 * (j + 6) + 24; y++) p.Put(2 * mi, y, 2 * mk, new Block(BlockType.Pillar, f.Steel.M, f.Steel.C, Role.Decor));
        Beacon(p, 2 * mi, 2 * (j + 6) + 24, 2 * mk);
        // The number, as tall as a house, facing the block.
        p.Stencil("07", 2 * i0 + 6, 2 * 20, 2 * k0, true, f.Hazard, 3, true);
        // Inside: a flight up the wall from the lobby to the bridge level, the bridge over to the block's roof.
        int f1 = p.Flight(i0 + 3, k0 + 3, Dir.East, 0, 6, 2, "stair");
        p.C.Link(lobby, f1);
        int landing = p.Gantry("landing", 2, i0 + 9, k0 + 3, i0 + 11, k0 + 8, 6, 2, rails: false);
        p.C.Link(f1, landing);
        p.Fill(i0 + 1, 6, k0 + 1, i1 - 1, 6, k1 - 1, Plot.Cube(f.Roof, Role.Roof));
        p.Dig(i0 + 3, 6, k0 + 3, i0 + 8, 6, k0 + 4);
        p.Dig(i0 + 9, 6, k0 + 3, i0 + 11, 6, k0 + 8);
        for (int u = 2 * (i0 + 9); u <= 2 * (i0 + 11) + 1; u++)
            for (int v = 2 * (k0 + 3); v <= 2 * (k0 + 8) + 1; v++)
                p.Put(u, 11, v, new Block(BlockType.Lattice, f.Grating.M, f.Grating.C, Role.Floor));
        int f2 = p.Flight(i0 + 8, k0 + 7, Dir.West, 6, 7, 2, "stair", 2);
        p.C.Link(landing, f2);
        int top = p.Gantry("landing", 3, i0 + 1, k0 + 1, i0 + 1, k0 + 8, 13, 0, rails: false);
        p.C.Link(f2, top);
        return lobby;
    }

    /// <summary>A round chimney 84 m tall, banded near the top, a beacon on it.</summary>
    static void Stack(Plot p, int ci, int ck, double r, int h)
    {
        Fab f = p.F;
        p.Drum(ci, ck, r, 0, h - 1, f.Wall, j => j > h - 8 ? (j % 2 == 0 ? new Stuff2(f.Wall.M, f.Hazard) : new Stuff2(f.Wall.M, f.Black)) : j % 8 == 7 ? f.Panel : f.Wall);
        p.Weathers(ci - (int)r - 2, 0, ck - (int)r - 2, ci + (int)r + 2, h + 1, ck + (int)r + 2, h);
        Beacon(p, 2 * ci, 2 * h, 2 * (ck - (int)r) + 1);
        p.Pipe(2 * (ci - (int)r), 2 * h - 8, 2 * ck, Dir.Up, 10, f.Pipe);
    }

    static void Beacon(Plot p, int u, int y, int v)
    {
        p.Put(u, y, v, new Block(BlockType.Cube, Material.Lumen, new Rgb(0xff, 0x38, 0x20), Role.Light));
        p.C.X.Lights.Add(new LightSpot { Cell = p.Cell(u, y + 1, v), Color = new Rgb(0xff, 0x38, 0x20), Mode = LightMode.Beacon, Energy = 4f, Range = 40f });
    }

    /// <summary>A cooling tower 60 m tall: a waisted concrete shell, open to the sky, a gap at its foot to walk into.</summary>
    static int CoolingTower(Plot p, int ci, int ck)
    {
        Fab f = p.F;
        const int H = 30;
        double RadiusAt(double j)
        {
            double t = j / H, waist = 0.7;
            return 11 + 7 * Math.Pow(Math.Abs(t - waist) / waist, 2) + (t > waist ? 8 * (t - waist) : 0);
        }
        // The shell round outside, true to its waist and flare; inside, the blocks open as planned.
        var (x, z) = p.At(2 * ci, 2 * ck);
        Round.Lay(p.C, x, z, 0, 2 * H - 1, y => (2 * RadiusAt(y / 2) + 0.5, 0), y => Plot.Cube((y >> 1) % 6 == 5 ? f.Wall.Scale(0.9f) : f.Wall, Role.Wall),
            c => p.BlockAt(c) is var (i, k) && Plot.In(ci, ck, RadiusAt(c.Y >> 1) - 1.4, i, k));
        var cells = new List<(int, int, int)>();
        int r0 = (int)RadiusAt(0) + 2;
        for (int j = 0; j < 3; j++)
            for (int k = ck - r0; k <= ck + r0; k++)
                for (int i = ci - r0; i <= ci + r0; i++)
                    if (Plot.In(ci, ck, RadiusAt(j) - 1.4, i, k))
                        for (int v = 2 * k; v < 2 * k + 2; v++)
                            for (int u = 2 * i; u < 2 * i + 2; u++)
                                for (int y = 2 * j; y < 2 * j + 2; y++) cells.Add((u, y, v));
        int room = p.RoomOf(ModuleKind.Hall, "cooling-tower", 0, cells);
        // Support legs round the foot, the way in between them.
        int gapK = ck - (int)RadiusAt(0) - 1;
        p.Mouth(ci - 3, 0, gapK, ci + 2, 3, gapK + 3, room, p.Yard);
        p.Weathers(ci - 20, 0, ck - 20, ci + 20, H + 1, ck + 20, H);
        // Packing rotted down to a heap of grey in the bottom.
        for (int v = 2 * (ck - 12); v < 2 * (ck + 12); v++)
            for (int u = 2 * (ci - 12); u < 2 * (ci + 12); u++)
                if (Hash.Cell(new Int3(u, 0, v), 407) % 100 < 16 && Plot.In(ci, ck, 11, u >> 1, v >> 1)) p.Lay(u, 0, v, new Block(BlockType.Ash, Material.Ash, new Rgb(0x5c, 0x60, 0x5c), Role.Decor));
        return room;
    }

    /// <summary>Rows of tall storage tanks in a bund, each with a hazard band and a number.</summary>
    static void TankFarm(Plot p, int i0, int k0)
    {
        Fab f = p.F;
        Stuff2 skin = p.Tech == TechLevel.High ? f.Panel : f.Pipe;
        int n = 1;
        for (int a = 0; a < 2; a++)
            for (int b = 0; b < 2; b++, n++)
            {
                int ci = i0 + 8 + a * 15, ck = k0 + 8 + b * 15;
                p.Drum(ci, ck, 6, 0, 8, skin, j => j == 6 ? new Stuff2(skin.M, f.Hazard) : skin);
                p.Disc(ci, ck, 5, 8, Plot.Cube(skin.Scale(0.85f), Role.Roof));
                p.Stencil(n.ToString(), 2 * ci - 2, 4, 2 * (ck - 6), true, f.Black, 1, true);
            }
        // The bund wall.
        foreach (var (i, k) in Ring(i0, k0, i0 + 30, k0 + 30))
            if (!(k == k0 && i >= i0 + 13 && i <= i0 + 16)) p.Fill(i, 0, k, i, 0, k, Plot.Cube(f.Plinth, Role.Wall));
    }

    /// <summary>A round pad with a hazard ring, for what used to land.</summary>
    static void LandingPad(Plot p, int ci, int ck)
    {
        Fab f = p.F;
        for (int v = 2 * (ck - 16); v < 2 * (ck + 16); v++)
            for (int u = 2 * (ci - 16); u < 2 * (ci + 16); u++)
            {
                double d = Math.Sqrt(Math.Pow(u / 2.0 - ci, 2) + Math.Pow(v / 2.0 - ck, 2));
                if (d > 15) continue;
                bool ring = d > 13.6 || d < 6 && d > 5;
                p.Lay(u, 0, v, new Block(BlockType.Plate, Material.Concrete, ring ? ((u + v) >> 2 & 1) == 0 ? f.Hazard : f.Black : f.Floor.C, Role.Floor));
            }
    }

    /// <summary>A rack along the block's south side and out to the stack and cooling tower: 2 m mains on trestles, a 1 m bundle over them.</summary>
    static void PipeYard(Plot p)
    {
        Fab f = p.F;
        const int K0 = 76, K1 = 81, J = 3;
        void Bent(int i)
        {
            int y = 2 * J - 1;
            for (int v = 2 * K0; v <= 2 * K1 + 1; v++) p.Put(2 * i, y, v, Plot.Cube(f.Steel, Role.Decor));
            foreach (int v in new[] { 2 * K0, 2 * K1 + 1 }) p.Post(2 * i, y - 1, v, f.Steel);
        }
        for (int i = 42; i <= 170; i += 6) Bent(i);
        foreach (int k in new[] { 77, 80 }) p.Main(42, J, k, Dir.East, 128, k == 77 ? f.PipeHot : f.Pipe);
        foreach (int v in new[] { 157, 158, 159 })
            for (int i = 42; i < 170; i += 6)
                p.Pipe(2 * i, 2 * J, v, Dir.East, 12, f.Pipe);
        // Risers: up the block's wall to its roof, up to the stack's flank.
        foreach (int i in new[] { 70, 118 }) { p.Main(i, 0, ShellK1 + 5, Dir.Up, 3, f.Pipe); }
    }
}
