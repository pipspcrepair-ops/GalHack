#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// A sewage works laid out the way one works, along the flow, not as a building with rooms: what came
/// down the town's sewer enters the screen house, the pump hall lifts it out of a deep pit, round
/// settling tanks and long aeration lanes clean it, and the sludge goes by pipe tunnel to the presses
/// and the tall digesters while the clean water runs off down the outfall. Gantries and a pipe rack join
/// it all up overhead, a pipe gallery 12 m under the tanks joins it below. About 300 by 230 m on the
/// player's 2 m blocks.
/// </summary>
public static class SewageWorks
{
    const int W = 152, D = 112;

    public static void Compose(Canvas c, Rng rng, LayoutHints? hints, TechLevel tech)
    {
        var p = new Plot(c, rng, tech, hints?.Front ?? Dir.North);
        Fab f = p.F;
        bool high = tech == TechLevel.High;
        int gateI = 46;

        p.Approach(gateI + 2, 1, Dir.North);
        c.X.PlainDoor = f.Accent.C;
        c.X.PlainDoorSteel = true;
        p.Level(-3, -6, "pipe gallery");

        // ---- the buildings along the head of the flow --------------------------------------------
        int office = p.Hall("office", ModuleKind.Hall, 0, 8, 9, 24, 18, 0, 2, f.Wall);
        p.Door(16, 0, 9, true, office, p.Yard);
        foreach (int i in new[] { 10, 13, 19, 22 }) p.Glazed(i, 1, 9, i + 1, 9);
        Lab(p, office);

        int screen = p.Hall("screen-house", ModuleKind.Hall, 0, 28, 9, 44, 18, 0, 4, f.Wall);
        p.Mouth(34, 0, 9, 37, 2, 9, screen, p.Yard);
        ScreenHouse(p, screen);

        // ---- the pump hall: motors in a row over a pit 12 m deep ---------------------------------
        const int HallI0 = 8, HallI1 = 44, HallK0 = 20, HallK1 = 41;
        int hall = p.Hall("pump-hall", ModuleKind.Hall, 0, HallI0, HallK0, HallI1, HallK1, 0, 7, f.Wall);
        p.Mouth(24, 0, HallK0, 27, 3, HallK0, hall, p.Yard);
        p.Stripes(48, 0, 2 * HallK0, 49, 7, 2 * HallK0 + 1);
        p.Stripes(56, 0, 2 * HallK0, 57, 7, 2 * HallK0 + 1);
        p.Repaint(48, 8, 2 * HallK0, 57, 9, 2 * HallK0 + 1, (u, y, v) => ((u + v + y) >> 1 & 1) == 0 ? f.Hazard : f.Black);
        p.Door(12, 0, HallK0, true, hall, p.Yard);
        foreach (int i in new[] { 13, 18, 30, 35, 40 }) p.Glazed(i, 5, HallK0, i + 2, HallK0);
        p.Stencil("4", 2 * HallI0 + 6, 6, 2 * HallK0, true, f.Hazard, 2);
        const int PitI0 = 14, PitI1 = 38, PitK0 = 26, PitK1 = 35, PitJ = -6;
        Pit(p, hall, PitI0, PitK0, PitI1, PitK1, PitJ);

        // The pit's way down, the gallery round the hall and the switch room on it.
        int pitStair = p.Flight(33, 33, Dir.East, PitJ, 6, 3, "stair", -3);
        c.Link(hall, pitStair);
        int pitFloor = p.Room(ModuleKind.Hall, "pump-pit", -3, PitI0, PitJ, PitK0, PitI1, -1, PitK1);
        c.Link(pitStair, pitFloor);

        int up = p.Flight(10, 39, Dir.East, 0, 3, 2, "stair");
        c.Link(hall, up);
        int gallery = p.Gantry("gallery", 1, 13, 39, 43, 40, 3, 6);
        int galleryEnd = p.Gantry("gallery", 1, 42, 30, 43, 38, 3, 4);
        c.Link(up, gallery);
        c.Link(gallery, galleryEnd);
        p.Unrail(84, 6, 78, 87, 78);
        p.Level(1, 3, "gallery");

        int switches = p.Hall("switch-room", ModuleKind.Hall, 0, 24, 41, 36, 50, 0, 2, f.Panel);
        p.Door(30, 0, 50, true, switches, p.Yard);
        int control = p.Hall("control", ModuleKind.Vault, 1, 24, 41, 36, 50, 3, 2, f.Wall);
        p.Mouth(30, 3, 41, 31, 3, 41, control, gallery);
        p.Unrail(60, 6, 81, 63, 81);
        p.Glazed(25, 4, 41, 29, 41);
        p.Glazed(32, 4, 41, 35, 41);
        p.Glazed(26, 4, 50, 34, 50);
        Control(p, control);
        c.VaultModule = control;

        // ---- the clarifiers ----------------------------------------------------------------------
        int cols = rng.Between(2, 3);
        var tanks = new List<(int I, int K)>();
        for (int n = 0; n < cols; n++)
            foreach (int ck in new[] { 18, 46 })
                tanks.Add((60 + n * 22, ck));
        int number = 1;
        foreach (var (ci, ck) in tanks) Clarifier(p, ci, ck, 9, number++, high, ck == 18 ? -1 : 1);
        p.Main(45, 0, 16, Dir.East, 6, f.Pipe);

        // ---- the pipe rack over the road between the tanks, and the gantry on top of it ------------
        int rackEnd = 128;
        for (int i = 46; i <= 121; i += 5) Bent(p, i, 28, 33, 2);
        int rack = p.Gantry("pipe-gantry", 1, 45, 30, rackEnd, 31, 3, 0);
        p.Mouth(44, 3, 30, 44, 3, 31, galleryEnd, rack);
        foreach (int k in new[] { 29, 32 }) p.Main(46, 2, k, Dir.East, rackEnd - 46, k == 29 ? f.Pipe : f.PipeHot);

        // ---- the pipe gallery under it all ------------------------------------------------------
        int main = p.Tunnel("pipe-tunnel", -3, 39, 29, 126, 32, PitJ, 3, f.Plinth.Scale(1.2f));
        c.Link(pitFloor, main);
        foreach (int k in new[] { 29, 32 }) p.Main(40, PitJ + 2, k, Dir.East, 84, k == 29 ? f.PipeHot : f.Rust);
        for (int i = 44; i < 124; i += 6)
            foreach (int v in new[] { 58, 59, 64, 65 }) p.Put(2 * i, 2 * PitJ + 5, v, Plot.Cube(f.Steel, Role.Decor));
        p.Pipe(80, 2 * PitJ + 5, 60, Dir.East, 160, f.Pipe);
        p.Pipe(80, 2 * PitJ + 5, 62, Dir.East, 160, f.PipeHot);
        for (int i = 46; i < 126; i += 8) p.Lamp(2 * i, 2 * PitJ + 6, 61, main, 1.4f, 13f);
        foreach (var (ci, ck) in tanks) ValveChamber(p, main, ci, ck, PitJ, ck == 18 ? -1 : 1, tanks.IndexOf((ci, ck)));

        // ---- aeration lanes ---------------------------------------------------------------------
        int lanes = rng.Between(3, 4), laneI0 = 52, laneI1 = 112;
        Lanes(p, laneI0, 60, laneI1, lanes);
        int blower = p.Hall("blower-house", ModuleKind.Hall, 0, 34, 60, 48, 60 + lanes * 5 + 1, 0, 3, f.Wall);
        p.Mouth(40, 0, 60, 42, 1, 60, blower, p.Yard);
        for (int n = 0; n < lanes; n++) { p.Machine(37, 0, 63 + n * 5, 44, 64 + n * 5, 2, f.Rust); p.Main(45, 0, 63 + n * 5, Dir.East, 7, f.Pipe); }

        // ---- the sludge end: presses, the stair tower, the digesters ------------------------------
        int press = p.Hall("press-house", ModuleKind.Hall, 0, 118, 62, 146, 86, 0, 4, f.Wall);
        p.Mouth(130, 0, 62, 133, 2, 62, press, p.Yard);
        Presses(p, press);
        int branch = p.Tunnel("pipe-tunnel", -3, 124, 33, 127, 68, PitJ, 3, f.Plinth.Scale(1.2f));
        c.Link(main, branch);
        p.Main(124, PitJ + 2, 33, Dir.South, 34, f.PipeHot);
        for (int k = 34; k < 66; k += 6) foreach (int u in new[] { 248, 249 }) p.Put(u, 2 * PitJ + 5, 2 * k, Plot.Cube(f.Steel, Role.Decor));
        for (int k = 40; k < 66; k += 8) p.Lamp(251, 2 * PitJ + 6, 2 * k, branch, 1.4f, 13f);
        int rise = p.Flight(125, 69, Dir.South, PitJ, 6, 2, "stair", -3);
        c.Link(branch, rise);
        p.Dig(125, 0, 69, 126, 1, 76);
        c.Link(rise, press);

        var digesters = new List<(int I, int K)> { (133, 16), (133, 48) };
        foreach (var (ci, ck) in digesters) Digester(p, ci, ck, 7, 9, digesters.IndexOf((ci, ck)) + 1, high);
        int gas = Gasholder(p);
        p.Yards(gas);
        int inner = DigesterInside(p, 133, 16, 7, 9);
        p.Yards(inner);

        // Stair tower: a steel tower of flights up to the tops of the digesters.
        int tower = StairTower(p, rack, 124, 27);

        // ---- the outfall: a channel off the end of the lanes to the works' edge --------------------
        p.Dig(114, -2, 60, 117, -1, D - 2);
        p.Pave(114, 60, 117, D - 2, new Stuff2(Material.Concrete, f.Floor.C.Scale(0.7f)), Role.Floor, -4);
        for (int k = 60; k < D - 2; k += 1)
            foreach (int i in new[] { 113, 118 })
                p.Fill(i, -3, k, i, -1, k, Plot.Cube(f.Plinth, Role.Wall));

        // ---- the yard ----------------------------------------------------------------------------
        p.Pave(4, 3, W - 6, 6, new Stuff2(f.Floor.M, f.Floor.C.Scale(0.85f)));
        p.Pave(46, 7, 49, 28, new Stuff2(f.Floor.M, f.Floor.C.Scale(0.85f)));
        p.Pave(46, 26, 122, 35, new Stuff2(f.Floor.M, f.Floor.C.Scale(0.85f)));
        p.Pave(118, 36, 122, 61, new Stuff2(f.Floor.M, f.Floor.C.Scale(0.85f)));
        Fence(p, gateI);
        foreach (var (u, v) in new[] { (140, 60), (180, 60), (220, 60), (140, 12), (300, 66) }) p.Spawn(u, 0, v, p.Yard);
        p.Spawn(2 * 90, 2 * PitJ, 62, main);
        p.Spawn(2 * 120, 2 * PitJ, 62, main);
        p.Spawn(2 * 20, 0, 2 * 24, hall);
        p.Spawn(2 * 30, 2 * PitJ, 2 * 30, pitFloor);
        p.Cache(2 * 16, 2 * PitJ, 2 * 28, pitFloor);
        p.Cache(2 * 140, 0, 2 * 84, press);
        p.Cache(2 * 118 + 4, 2 * PitJ, 2 * 30, main);
        p.Finish();
    }

    /// <summary>Lab benches and sample bottles behind a partition: what the works tested.</summary>
    static void Lab(Plot p, int room)
    {
        Fab f = p.F;
        for (int u = 2 * 10; u < 2 * 22; u += 2)
            foreach (int v in new[] { 2 * 11 + 1, 2 * 16 })
                p.Prop(u, 0, v, BlockType.Bench, Material.Steel, f.Steel.C);
        for (int u = 2 * 10; u < 2 * 22; u += 3) p.Prop(u, 0, 2 * 17 + 1, BlockType.Shelf, Material.Steel, f.Steel.C);
        p.Terminal(2 * 9 + 1, 0, 2 * 14, room);
        p.Lamp(32, 5, 28, room, 1.2f, 12f);
    }

    /// <summary>An open channel through a long room, a rack of bars standing in it, a chute beyond: where the sewer arrives and the rubbish comes out.</summary>
    static void ScreenHouse(Plot p, int room)
    {
        Fab f = p.F;
        p.Dig(29, -2, 12, 43, -1, 15);
        for (int i = 29; i <= 43; i++)
            foreach (int k in new[] { 11, 16 })
                p.Fill(i, -3, k, i, -1, k, Plot.Cube(f.Plinth, Role.Wall));
        p.Fill(29, -3, 12, 43, -3, 15, Plot.Cube(f.Plinth, Role.Floor));
        for (int u = 2 * 29; u < 2 * 44; u++)
            for (int v = 2 * 12; v < 2 * 16; v++)
                p.Wet(u, -4, v);
        // Bar screens across the channel, a machine walking them.
        foreach (int i in new[] { 33, 38 })
        {
            for (int v = 2 * 12; v < 2 * 16; v += 1)
                for (int y = -4; y <= 3; y++) p.Put(2 * i, y, v, new Block(BlockType.Pillar, Material.Rust, f.Rust.C, Role.Decor));
            p.Fill(i, 2, 12, i, 2, 15, Plot.Cube(f.Rust, Role.Decor));
            p.Machine(i, 3, 13, i, 14, 1, f.Pipe);
        }
        // A skip for what the screens took out.
        p.Machine(30, 0, 17, 32, 17, 1, f.Rust);
        // A concrete footbridge over the channel, flush with the floor, railed.
        p.Fill(35, -1, 12, 36, -1, 15, Plot.Cube(f.Plinth, Role.Floor));
        for (int v = 2 * 12; v < 2 * 16; v++)
            foreach (int u in new[] { 2 * 35, 2 * 36 + 1 }) p.Put(u, 0, v, new Block(BlockType.Rail, Material.Steel, f.Hazard, Role.Decor));
        p.Lamp(2 * 36, 9, 2 * 13, room, 1.6f, 14f);
    }

    /// <summary>A pit in the hall floor, lined, railed and striped, with motors on beams across it and long-shaft pumps standing in it.</summary>
    static void Pit(Plot p, int hall, int i0, int k0, int i1, int k1, int j)
    {
        Fab f = p.F;
        // Lining, then open it.
        for (int k = k0 - 1; k <= k1 + 1; k++)
            for (int i = i0 - 1; i <= i1 + 1; i++)
            {
                bool edge = i < i0 || i > i1 || k < k0 || k > k1;
                p.Fill(i, j - 1, k, i, j - 1, k, Plot.Cube(f.Plinth, Role.Floor));
                if (edge) p.Fill(i, j, k, i, -1, k, Plot.Cube(f.Plinth, Role.Wall));
                else p.Dig(i, j, k, i, -1, k);
            }
        // Remove the plates over the hole.
        for (int k = 2 * k0; k <= 2 * k1 + 1; k++)
            for (int i = 2 * i0; i <= 2 * i1 + 1; i++) p.Air(i, 0, k);
        p.Pave(i0, k0, i1, k1, f.Floor, Role.Floor, 2 * j);
        // Rail and paint round the edge.
        for (int i = 2 * i0 - 1; i <= 2 * i1 + 2; i++)
            foreach (int v in new[] { 2 * k0 - 1, 2 * k1 + 2 })
            {
                p.FloorStripes(i, v + (v < 2 * k0 ? -1 : 1), i, v + (v < 2 * k0 ? -1 : 1));
                p.Put(i, 0, v, new Block(BlockType.Rail, Material.Steel, f.Hazard, Role.Decor));
            }
        for (int v = 2 * k0 - 1; v <= 2 * k1 + 2; v++)
        {
            p.Put(2 * i0 - 1, 0, v, new Block(BlockType.Rail, Material.Steel, f.Hazard, Role.Decor));
            if (v < 2 * 33) p.Put(2 * i1 + 2, 0, v, new Block(BlockType.Rail, Material.Steel, f.Hazard, Role.Decor));
        }
        // Five pump sets: a bridge of concrete, a motor drum 6 m tall on it, a shaft down to the pump casing below.
        for (int n = 0; n < 4; n++)
        {
            int i = i0 + 2 + n * 4;
            p.Fill(i, -1, k0, i + 2, -1, k1, Plot.Cube(f.Plinth, Role.Floor));
            double ci = i + 1.5, ck = (k0 + k1 + 1) / 2.0;
            for (int jj = 0; jj <= 2; jj++) p.Disc(ci, ck, 2.0, jj, new Block(BlockType.Cube, f.Rust.M, jj == 2 ? f.Rust.C.Scale(0.7f) : f.Rust.C, Role.Prop));
            p.Machine(i, j, (k0 + k1) / 2 - 1, i + 2, (k0 + k1) / 2 + 1, 2, f.Rust);
            p.Main(i + 1, j + 2, (k0 + k1) / 2, Dir.Up, 2, f.Pipe);
            p.Main(i + 1, j + 4, (k0 + k1) / 2, Dir.Up, 1, f.Pipe);
        }
        // The overhead crane: its rails along the walls, its bridge left halfway, a hook on a chain.
        foreach (int k in new[] { 21, 40 })
        {
            for (int i = 9; i <= 43; i++) p.Fill(i, 5, k, i, 5, k, Plot.Cube(f.Rust.Scale(0.8f), Role.Decor));
        }
        int craneI = 18 + p.R.Range(0, 3) * 4;
        p.Fill(craneI, 6, 21, craneI + 1, 6, 40, Plot.Cube(new Stuff2(f.Steel.M, f.Hazard), Role.Decor));
        for (int y = 11; y >= 5; y--) p.Put(2 * craneI + 1, y, 62, new Block(BlockType.Cable, Material.Steel, f.Black, Role.Decor));
        for (int i = 11; i <= 41; i += 6) p.Lamp(2 * i, 13, 60, hall, 2.2f, 18f);
    }

    /// <summary>A room of desks round a mimic board: the plant's flow, the switches left as they fell.</summary>
    static void Control(Plot p, int room)
    {
        Fab f = p.F;
        for (int u = 2 * 26; u < 2 * 35; u += 2)
            p.Prop(u, 6, 2 * 43, BlockType.Bench, Material.Steel, f.Steel.C);
        p.Terminal(2 * 30, 6, 2 * 45 + 1, room);
        p.Terminal(2 * 33, 6, 2 * 45 + 1, room);
        p.Cache(2 * 26, 6, 2 * 48, room);
        p.Cache(2 * 34, 6, 2 * 48, room);
        p.Lamp(2 * 30, 11, 2 * 46, room, 1.4f, 12f);
    }

    /// <summary>A sunken round basin: its ring wall, its mud floor, a column at its middle and a bridge from the rim to it. Stepped circles at this size read as round.</summary>
    static void Clarifier(Plot p, int ci, int ck, double r, int number, bool covered, int side)
    {
        Fab f = p.F;
        p.Drum(ci, ck, r, -3, 0, f.Wall, j => j == 0 ? f.Wall : f.Plinth);
        p.Disc(ci, ck, r - 1, -3, Plot.Cube(f.Plinth, Role.Floor));
        p.Weathers(ci - (int)r - 1, 0, ck - (int)r - 1, ci + (int)r, 2, ck + (int)r, 0);
        var basin = new List<(int, int, int)>();
        int rr = (int)r + 1;
        for (int v = 2 * (ck - rr); v < 2 * (ck + rr); v++)
            for (int u = 2 * (ci - rr); u < 2 * (ci + rr); u++)
            {
                if (!Plot.In(ci, ck, r - 1.0, u >> 1, v >> 1)) continue;
                for (int y = -4; y <= -2; y++) basin.Add((u, y, v));
                uint h = Hash.Cell(new Int3(u, ci, v), 211);
                p.Wet(u, -4, v);
                if (h % 100 is >= 55 and < 62) p.Put(u, -4, v, new Block(BlockType.Plant, Material.Plant, new Rgb(0x5a, 0x66, 0x38), Role.Debris));
            }
        // The middle column, and the scraper bridge from the rim to it.
        p.Fill(ci - 1, -2, ck - 1, ci, 0, ck, Plot.Cube(f.Plinth, Role.Wall));
        int rimK = side < 0 ? ck + (int)r - 1 : ck - (int)r;
        int k0 = side < 0 ? ck - 1 : rimK + 1, k1 = side < 0 ? rimK - 1 : ck;
        int bridge = p.Gantry("scraper-bridge", 0, ci - 1, k0, ci, k1, 1, 0);
        int steps = p.Flight(ci - 1, side < 0 ? rimK + 1 : rimK - 1, side < 0 ? Dir.North : Dir.South, 0, 1, 2);
        p.C.Link(p.Yard, steps);
        p.C.Link(steps, bridge);
        // The scraper arm on the far side of the middle, stopped where the power went.
        for (int t = 1; t < (int)r - 1; t++)
            for (int v = 0; v < 2; v++) p.Put(2 * ci, 1, 2 * (side < 0 ? ck - 1 - t : ck + t) + v, new Block(BlockType.Cube, f.Rust.M, f.Rust.C, Role.Decor));
        p.Stencil(number.ToString(), 2 * ci - 2, 0, side < 0 ? 2 * (ck - (int)r) : 2 * (ck + (int)r) - 1, true, f.Hazard, 1);
        int mud2 = p.RoomOf(ModuleKind.Hall, "settling-tank", 0, basin);
        // A ladder down the inside of the rim.
        int lv = side < 0 ? 2 * (ck + (int)r) - 3 : 2 * (ck - (int)r) + 2;
        int lu = 2 * (ci + 2);
        Dir back = side < 0 ? Dir.South : Dir.North;
        p.Ladder(lu, -4, 1, lv, back);
        p.Air(lu, 2, lv);
        p.Air(lu, 3, lv);
        p.C.Link(bridge, mud2);
        if (covered)
        {
            // A high-tech works keeps its tanks under ribbed covers, the ribs running out from the column.
            for (int a = 0; a < 8; a += 2)
            {
                double ang = a * Math.PI / 4;
                for (double t = 0.5; t < r - 0.5; t += 0.5)
                {
                    int i = ci + (int)Math.Round(Math.Cos(ang) * t), k = ck + (int)Math.Round(Math.Sin(ang) * t);
                    if (i >= ci - 2 && i <= ci + 1 && (k >= k0 - 1 && k <= k1 + 1)) continue;
                    if (!p.Get(2 * i, 0, 2 * k).IsAir) continue;
                    p.Fill(i, 0, k, i, 0, k, new Block(BlockType.Lattice, f.Steel.M, f.Steel.C, Role.Decor));
                }
            }
        }
    }

    /// <summary>A trestle across the rack: two posts and a beam, the mains on the beam, the deck over them.</summary>
    static void Bent(Plot p, int i, int k0, int k1, int j)
    {
        Fab f = p.F;
        int y = 2 * j - 1;
        for (int v = 2 * k0; v <= 2 * k1 + 1; v++) p.Put(2 * i, y, v, Plot.Cube(f.Steel, Role.Decor));
        foreach (int v in new[] { 2 * k0, 2 * k1 + 1 })
        {
            p.Post(2 * i, y - 1, v, f.Steel);
            for (int h = y + 1; h <= y + 2; h++) p.Put(2 * i, h, v, new Block(BlockType.Rib, f.Steel.M, f.Steel.C, Role.Decor));
        }
    }

    /// <summary>A narrow branch off the main tunnel to the foot of a tank: valves in a chamber, and a ladder up through the tank's middle column to its bridge.</summary>
    static void ValveChamber(Plot p, int main, int ci, int ck, int j, int side, int n)
    {
        Fab f = p.F;
        int k0 = side < 0 ? ck + 2 : 33, k1 = side < 0 ? 28 : ck - 2;
        int branch = p.Tunnel("valve-passage", -3, ci - 1, k0, ci, k1, j, 2, f.Plinth.Scale(1.1f));
        p.C.Link(main, branch);
        p.Dig(ci - 1, j, side < 0 ? 28 : 33, ci, j + 1, side < 0 ? 28 : 33);
        int ck0 = side < 0 ? ck - 2 : ck - 1, ck1 = side < 0 ? ck + 1 : ck + 2;
        int chamber = p.Tunnel("valve-chamber", -3, ci - 3, ck0, ci + 2, ck1, j, 3, f.Plinth.Scale(1.1f));
        p.C.Link(branch, chamber);
        p.Machine(ci - 3, j, ck0, ci - 2, ck0, 2, f.Rust);
        p.Machine(ci + 1, j, ck1, ci + 2, ck1, 2, f.Rust);
        p.Lamp(2 * ci, 2 * j + 6, 2 * ck, chamber, 1.0f, 10f, n % 2 == 0 ? LightMode.Emergency : LightMode.Steady);
        if (n % 2 == 0) p.Cache(2 * (ci + 2), 2 * j, 2 * ck0, chamber);
    }

    /// <summary>Long rectangular tanks side by side: concrete walls you can walk along, air mains down them, bridges across.</summary>
    static void Lanes(Plot p, int i0, int k0, int i1, int lanes)
    {
        Fab f = p.F;
        const int Pitch = 5;
        int k1 = k0 + lanes * Pitch;
        p.Weathers(i0, 0, k0, i1, 2, k1, 0);
        for (int k = k0; k <= k1; k++)
            for (int i = i0; i <= i1; i++)
            {
                bool wall = i == i0 || i == i1 || (k - k0) % Pitch == 0;
                p.Fill(i, -3, k, i, -3, k, Plot.Cube(f.Plinth, Role.Floor));
                if (wall) p.Fill(i, -2, k, i, 0, k, Plot.Cube(f.Wall, Role.Wall));
                else p.Dig(i, -2, k, i, 0, k);
            }
        var cells = new List<(int, int, int)>();
        for (int n = 0; n < lanes; n++)
            for (int v = 2 * (k0 + n * Pitch + 1); v < 2 * (k0 + (n + 1) * Pitch); v++)
                for (int u = 2 * (i0 + 1); u < 2 * i1; u++)
                {
                    if (n == 0) for (int y = -4; y <= -2; y++) cells.Add((u, y, v));
                    p.Wet(u, -4, v);
                }
        // Air mains along the wall tops, drop pipes into the water.
        for (int n = 0; n <= lanes; n++)
        {
            int v = 2 * (k0 + n * Pitch);
            p.Pipe(2 * i0 + 6, 2, v, Dir.East, 2 * (i1 - i0) - 8, f.Pipe);
            for (int u = 2 * i0 + 8; u < 2 * i1 - 4; u += 14)
                if (n < lanes) p.Pipe(u, 1, v + 2, Dir.Down, 5, f.Pipe);
            for (int u = 2 * i0 + 8; u < 2 * i1 - 4; u += 14)
                if (n < lanes) { p.Put(u, 2, v + 1, new Block(BlockType.Pipe, f.Pipe.M, f.Pipe.C, Role.Decor, Dir.South)); p.Put(u, 2, v + 2, new Block(BlockType.Pipe, f.Pipe.M, f.Pipe.C, Role.Decor, Dir.South)); }
        }
        int bridge = -1;
        for (int t = 0; t < 3; t++)
        {
            int i = i0 + 8 + t * (i1 - i0 - 14) / 2;
            bridge = p.Gantry("lane-bridge", 0, i, k0 - 1, i + 1, k1 + 1, 1, 0);
            int s = p.Flight(i, k0 - 2, Dir.South, 0, 1, 2);
            p.C.Link(p.Yard, s);
            p.C.Link(s, bridge);
        }
        // One lane can be walked: a ladder down from the first bridge's end of the wall.
        int lanesRoom = p.RoomOf(ModuleKind.Hall, "aeration-lane", 0, cells);
        p.C.Link(p.Yard, p.Flight(i0 + 1, k0 - 1, Dir.South, 0, 1, 1));
        p.Ladder(2 * i0 + 2, -4, 1, 2 * k0 + 2, Dir.North);
        p.Air(2 * i0 + 2, 2, 2 * k0 + 2);
        p.Air(2 * i0 + 2, 3, 2 * k0 + 2);
        p.C.Link(bridge, lanesRoom);
    }

    /// <summary>The press house: sludge belts in rows, hoppers over them, a cake conveyor to a skip bay.</summary>
    static void Presses(Plot p, int room)
    {
        Fab f = p.F;
        for (int n = 0; n < 3; n++)
        {
            int k = 66 + n * 6;
            // A belt press: long, low, with rollers along it.
            p.Machine(120, 0, k, 124, k + 2, 1, f.Rust);
            p.Machine(129, 0, k, 143, k + 2, 1, f.Steel);
            for (int i = 129; i <= 143; i += 2) p.Fill(i, 1, k + 1, i, 1, k + 1, new Block(BlockType.Cube, f.Rust.M, f.Rust.C.Scale(0.8f), Role.Prop));
            p.Fill(136, 3, k, 138, 3, k + 2, Plot.Cube(f.Pipe, Role.Prop));
            p.Post(2 * 136, 5, 2 * k, f.Steel);
            p.Post(2 * 138 + 1, 5, 2 * k + 5, f.Steel);
        }
        for (int i = 120; i < 146; i += 7) p.Lamp(2 * i, 9, 2 * 74, room, 1.6f, 16f);
    }

    /// <summary>A tall sludge digester: a drum 18 m high with a low cone on it, banded, numbered.</summary>
    static void Digester(Plot p, int ci, int ck, double r, int h, int number, bool high)
    {
        Fab f = p.F;
        p.Drum(ci, ck, r, 0, h - 1, f.Wall, j => j % 3 == 2 ? f.Panel : f.Wall);
        p.Cap(ci, ck, r - 1, h, f.Roof, high ? 0.8f : 1.2f);
        p.Disc(ci, ck, r - 1, h - 1, Plot.Cube(f.Roof, Role.Roof));
        p.Weathers(ci - (int)r - 1, 0, ck - (int)r - 1, ci + (int)r, h + 7, ck + (int)r, h - 1);
        int top = ck + (int)r - 1;
        p.Stencil(number.ToString(), 2 * ci - 3, 6, number == 1 ? 2 * top + 1 : 2 * (ck - (int)r), true, f.Hazard, 2);
    }

    /// <summary>The inside of the first digester, through a hole cut in its foot: a dark round hall, the mixer shaft still standing in it, dried sludge in terraces.</summary>
    static int DigesterInside(Plot p, int ci, int ck, double r, int h)
    {
        Fab f = p.F;
        var cells = new List<(int, int, int)>();
        int rr = (int)r + 1;
        for (int v = 2 * (ck - rr); v < 2 * (ck + rr); v++)
            for (int u = 2 * (ci - rr); u < 2 * (ci + rr); u++)
                if (Plot.In(ci, ck, r - 1.0, u >> 1, v >> 1))
                    for (int y = 0; y < 2 * (h - 1); y++) cells.Add((u, y, v));
        int m = p.RoomOf(ModuleKind.Hall, "digester", 0, cells);
        // The way in, on the side that faces the yard's road.
        int wall = ck + (int)r - 1;
        p.Mouth(ci - 1, 0, wall, ci, 1, wall, m, p.Yard);
        p.Fill(ci - 1, 0, ck - 1, ci, h - 2, ck, new Block(BlockType.Rib, f.Rust.M, f.Rust.C, Role.Prop));
        for (int v = 2 * (ck - rr); v < 2 * (ck + rr); v++)
            for (int u = 2 * (ci - rr); u < 2 * (ci + rr); u++)
                if (Plot.In(ci, ck, r - 2.0, u >> 1, v >> 1) && !Plot.In(ci, ck, 2, u >> 1, v >> 1) && Hash.Cell(new Int3(u, 0, v), 223) % 100 < 80)
                    p.Lay(u, 0, v, new Block(BlockType.Plate, Material.Rock, new Rgb(0x36, 0x30, 0x28), Role.Floor));
        p.Lamp(2 * ci, 2 * (h - 1) - 1, 2 * (ck + 4), m, 0.8f, 10f, LightMode.Emergency);
        return m;
    }

    /// <summary>A gas holder beside the digesters: a broad low steel drum in its frame, and a flare stack.</summary>
    static int Gasholder(Plot p)
    {
        Fab f = p.F;
        const double ci = 112, ck = 18;
        p.Drum(ci, ck, 8, 0, 3, f.Rust, j => j == 3 ? f.Rust.Scale(0.8f) : f.Rust);
        p.Disc(ci, ck, 7, 3, Plot.Cube(f.Rust.Scale(0.7f), Role.Roof));
        foreach (var (di, dk) in new[] { (-8, -8), (7, -8), (-8, 7), (7, 7) })
            for (int y = 0; y < 14; y++) p.Put(2 * ((int)ci + di), y, 2 * ((int)ck + dk), new Block(BlockType.Rib, f.Steel.M, f.Steel.C, Role.Decor));
        int ri0 = 2 * ((int)ci - 8), ri1 = 2 * ((int)ci + 7), rk0 = 2 * ((int)ck - 8), rk1 = 2 * ((int)ck + 7);
        for (int u = ri0; u <= ri1; u++)
            foreach (int v in new[] { rk0, rk1 }) p.Put(u, 13, v, new Block(BlockType.Cube, f.Steel.M, f.Steel.C, Role.Decor));
        for (int v = rk0; v <= rk1; v++)
            foreach (int u in new[] { ri0, ri1 }) p.Put(u, 13, v, new Block(BlockType.Cube, f.Steel.M, f.Steel.C, Role.Decor));
        // The flare stack.
        for (int y = 0; y < 44; y++)
            p.Put(2 * 124, y, 2 * 8, new Block(BlockType.Pipe, f.Rust.M, y > 40 ? f.Black : f.Rust.C, Role.Decor, Dir.Up));
        return p.Room(ModuleKind.Outdoor, "gas-yard", 0, (int)ci - 11, 0, (int)ck + 9, (int)ci - 6, 0, (int)ck + 10);
    }

    /// <summary>A steel tower of flights from the ground to the digesters' tops, off the end of the pipe gantry.</summary>
    static int StairTower(Plot p, int rack, int i, int k)
    {
        Fab f = p.F;
        var c = p.C;
        // Landings left and right; flights rising east on one side, west on the other.
        int foot = p.Flight(i + 2, k, Dir.East, 0, 3, 2, "stair");
        c.Link(p.Yard, foot);
        int land1 = p.Gantry("landing", 1, i + 5, k, i + 7, k + 7, 3, 3, rails: false);
        c.Link(foot, land1);
        c.Link(rack, land1);
        int mid = p.Flight(i + 4, k + 6, Dir.West, 3, 3, 2, "stair", 1);
        c.Link(land1, mid);
        int land2 = p.Gantry("landing", 2, i, k, i + 1, k + 7, 6, 3, rails: false);
        c.Link(mid, land2);
        int top = p.Flight(i + 2, k, Dir.East, 6, 3, 2, "stair", 2);
        c.Link(land2, top);
        int land3 = p.Gantry("landing", 3, i + 5, k, i + 7, k + 7, 9, 3, rails: false);
        c.Link(top, land3);
        p.Level(3, 9, "digester top");
        int north = p.Gantry("gantry", 3, i + 6, k - 5, i + 7, k - 1, 9, 0);
        int south = p.Gantry("gantry", 3, i + 6, k + 8, i + 7, k + 13, 9, 0);
        c.Link(land3, north);
        c.Link(land3, south);
        return land3;
    }

    /// <summary>A chain-link line round the works on concrete posts, with its gate, broken here and there.</summary>
    static void Fence(Plot p, int gateI)
    {
        Fab f = p.F;
        var link = new Block(BlockType.Rail, Material.Steel, new Rgb(0x6c, 0x6e, 0x70), Role.Decor);
        void Line(int u0, int v0, int u1, int v1)
        {
            int len = Math.Max(Math.Abs(u1 - u0), Math.Abs(v1 - v0));
            for (int t = 0; t <= len; t++)
            {
                int u = u0 + Math.Sign(u1 - u0) * t, v = v0 + Math.Sign(v1 - v0) * t;
                if (v == 0 && u >= 2 * gateI && u < 2 * gateI + 12) continue;
                if (Hash.Cell(new Int3(u / 8, 0, v / 8), 229) % 100 < 14) continue;
                if (t % 6 == 0)
                    for (int y = 0; y < 3; y++) p.Put(u, y, v, new Block(BlockType.Pillar, Material.Concrete, f.Plinth.C, Role.Decor));
                else p.Put(u, 0, v, link);
            }
        }
        Line(0, 0, 2 * W, 0);
        Line(0, 0, 0, 2 * D);
        Line(0, 2 * D, 2 * W, 2 * D);
        Line(2 * W, 0, 2 * W, 2 * D);
        // The gate: two concrete piers and a board over the way in.
        foreach (int i in new[] { gateI - 1, gateI + 6 })
            p.Fill(i, 0, 0, i, 3, 0, Plot.Cube(f.Plinth, Role.Wall));
        p.Fill(gateI - 1, 4, 0, gateI + 6, 4, 0, Plot.Cube(f.Accent, Role.Wall));
    }
}
