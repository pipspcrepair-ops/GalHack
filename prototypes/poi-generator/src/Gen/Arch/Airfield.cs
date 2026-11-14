#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// The buildings and yard works of an airfield or a spaceport, each a place of its own drawn on the player's 2 m lattice,
/// at the size its type's sketch rolled (data/place-types.txt; PlaceTypes.Pieces hands each its box and front). Each fills its box
/// exactly, frame blocks i 0..L/2-1 along its front and k 0..D/2-1 back from it, its way in on open ground outside:
///   a terminal: a concourse along the airside with gate doors onto the apron, a band of rooms (security, baggage,
///     shops, offices) between it and the check-in hall, the landside doors into the hall, offices upstairs;
///   a control tower: a shaft of equipment rooms climbed by a ladder through every floor, a glazed cab over it all;
///   a hangar (a port's ship hall): one clear-span hall with a door a craft goes through across its front, offices at the back;
///   a fuel farm (or a port's oxidiser store): tanks in a bunded yard; a fence run with its gates; a line of approach lights.
/// Sizes and the plan inside come from the part's arguments and the spawn's seed, never fixed coordinates.
/// </summary>
public static class Airfield
{
    /// <summary>Frame size in blocks from a part's frame length and depth (metres).</summary>
    static (int I1, int K1) Frame(SitePart part) => (part.Int("fl") / 2 - 1, part.Int("fd") / 2 - 1);

    static Block Solid(Stuff2 s, Role r) => Plot.Cube(s, r);

    public static void Compose(Canvas c, Rng rng, LayoutHints? hints, TechLevel tech, SitePart part)
    {
        var p = new Plot(c, rng, tech, hints?.Front ?? Dir.North);
        Civic.Wear(p);
        c.Begin(); // (the piece is one object: a building with all its rooms, or one yard work)
        switch (part.Arg("what"))
        {
            case "terminal": Terminal(p, part); break;
            case "tower": Tower(p, part); break;
            case "hangar": Hangar(p, part); break;
            case "fuel": Fuel(p, part); break;
            case "fence": Fence(p, part); break;
            case "lights": Lights(p, part); break;
            default: throw new ArgumentException($"no airfield work '{part.Arg("what")}'");
        }
        p.Finish();
    }

    // ---- the terminal ---------------------------------------------------------------------------

    /// <summary>
    /// Its front (k 0) on the apron, its back on the landside road. Ground floor from the front: the concourse
    /// (its width the type's), a wall, a band of rooms 10 m deep cut into bays, a wall, the check-in hall to the
    /// back wall. Storeys of 6 m; the rest upstairs is offices off a gallery along the landside, up a stair from the hall.
    /// </summary>
    static void Terminal(Plot p, SitePart part)
    {
        Canvas c = p.C;
        Rng r = p.R.Fork("terminal");
        Fab f = p.F;
        var (I1, K1) = Frame(part);
        int storeys = Math.Clamp((int)MathF.Round(part.Num("h", 14) / 6f), 1, 3);
        int Kc = Math.Max(3, part.Int("concourse", 6) / 2), band0 = Kc + 2, band1 = Kc + 6, hall0 = Kc + 8;
        const int P = 3, H = 2;
        int mid = I1 / 2;
        if (K1 < hall0 + 3) { Shed(p, I1, K1); return; }
        storeys = Math.Max(2, storeys);
        for (int s = 0; s < storeys; s++) p.Shell(0, 0, I1, K1, s * P, H, f.Wall, f.Roof);
        p.Plinth(0, 0, I1, K1, 0);
        int top = storeys * P - 1;
        foreach (var (i, k) in Edge(0, 0, I1, K1)) p.Fill(i, top + 1, k, i, top + 1, k, Solid(f.Wall, Role.Wall));
        p.Weathers(-1, 0, -1, I1 + 1, top + 2, K1 + 1, top + 1);

        // The landside way in: the approach out past the back wall, a wide mouth into the hall and a door either side.
        p.Approach(mid, K1 + 2, Dir.South);
        int hall = p.Room(ModuleKind.Hall, "check-in hall", 0, 1, 0, hall0, I1 - 1, H - 1, K1 - 1);
        int concourse = p.Room(ModuleKind.Hall, "concourse", 0, 1, 0, 1, I1 - 1, H - 1, Kc);
        p.Mouth(mid - 1, 0, K1, mid + 1, 1, K1, hall, p.Yard);
        foreach (int i in new[] { mid - 4, mid + 4 }) p.Door(i, 0, K1, true, hall, p.Yard);
        foreach (int k in new[] { band0 - 1, band1 + 1 }) p.Fill(1, 0, k, I1 - 1, H - 1, k, Solid(f.Panel, Role.Wall));

        // The band: bays of rolled widths, two or more of them security passages from the hall through to the concourse.
        var bays = new List<(int I0, int I1)>();
        for (int i = 1; i <= I1 - 1;)
        {
            int w = Math.Min(r.Between(5, 8), I1 - i);
            if (I1 - 1 - (i + w) < 4) w = I1 - i;
            bays.Add((i, i + w - 1));
            i += w + 1;
            if (i <= I1 - 1) p.Fill(i - 1, 0, band0, i - 1, H - 1, band1, Solid(f.Panel, Role.Wall));
        }
        var passages = new HashSet<int> { bays.Count / 3, bays.Count * 2 / 3 };
        var doors = new HashSet<int>();
        if (bays.Count > 8) passages.Add(r.Range(0, bays.Count));
        string[] kinds = { "baggage", "office", "shop", "cafe", "staff", "toilets", "shop", "office" };
        // One bay walled off behind its neighbour, its only way in a breakable panel in the wall between (found by
        // exploring, never by a lock).
        var walled = Enumerable.Range(1, bays.Count - 1).Where(b => !passages.Contains(b) && !passages.Contains(b - 1)).ToList();
        int hidden = walled.Count > 0 ? walled[r.Range(0, walled.Count)] : -1, beside = -1;
        for (int b = 0; b < bays.Count; b++)
        {
            var (a0, a1) = bays[b];
            if (b == hidden)
            {
                int store = p.Room(ModuleKind.Room, "hidden store", 0, a0, 0, band0, a1, H - 1, band1);
                p.Secret(a0 - 1, 0, band1 - 1, false, -1, beside, store, 0, 2 * a1, 2 * band0);
                continue;
            }
            if (passages.Contains(b))
            {
                int pass = p.Room(ModuleKind.Corridor, "security", 0, a0, 0, band0, a1, H - 1, band1);
                int at = (a0 + a1) / 2;
                p.Mouth(at, 0, band1 + 1, Math.Min(at + 1, a1), 1, band1 + 1, hall, pass);
                p.Mouth(at, 0, band0 - 1, Math.Min(at + 1, a1), 1, band0 - 1, pass, concourse);
                for (int u = 2 * a0; u <= 2 * a1 + 1; u += 3)
                    if (u < 2 * at - 1 || u > 2 * at + 4) p.Prop(u, 0, 2 * band0 + 4, BlockType.Machine, Material.Steel, f.Steel.C);
                p.Lamp(2 * at, 2 * H - 1, 2 * band0 + 5, pass, 1f, 10f);
                continue;
            }
            string what = kinds[r.Range(0, kinds.Length)];
            bool toHall = what is "baggage" or "office" or "staff" ? true : r.Chance(0.4f);
            int room = beside = p.Room(ModuleKind.Room, what, 0, a0, 0, band0, a1, H - 1, band1);
            p.Door((a0 + a1) / 2, 0, toHall ? band1 + 1 : band0 - 1, true, room, toHall ? hall : concourse);
            doors.Add((a0 + a1) / 2);
            Furnish(p, what, a0, band0, a1, band1, 0, room, toHall);
        }

        // Airside: gate doors onto the apron, a stand of open ground before each, the wall glazed between them.
        var gates = new List<int>();
        for (int i = 4; i <= I1 - 4; i += r.Between(12, 18)) gates.Add(i);
        for (int g = 0; g < gates.Count; g++)
        {
            int i = gates[g];
            var cells = new List<(int, int, int)>();
            for (int v = -3; v <= -1; v++) for (int u = 2 * i - 1; u <= 2 * i + 2; u++) for (int y = 0; y <= 2; y++) cells.Add((u, y, v));
            int stand = p.RoomOf(ModuleKind.Outdoor, "gate stand", 0, cells);
            p.Door(i, 0, 0, true, concourse, stand);
            c.Link(stand, p.Yard);
        }
        int last = 1;
        foreach (int i in gates.Append(I1))
        {
            if (i - 2 >= last + 1) p.Glazed(last + 1, 1, 0, i - 2, 0);
            last = i + 1;
        }
        // Landside glazing either side of the doors.
        p.Glazed(2, 1, K1, mid - 6, K1);
        p.Glazed(mid + 6, 1, K1, I1 - 2, K1);

        // Seats down the concourse against the band's wall; across the hall check-in desks in runs, a bag belt behind
        // them along the band's wall, the staff aisle between, seats further back; lamps.
        for (int u = 4; u < 2 * I1 - 2; u += 2)
        {
            int i = u / 2;
            if (bays.Where((_, b) => passages.Contains(b)).Any(bay => i >= bay.I0 - 1 && i <= bay.I1 + 1) || doors.Any(d => Math.Abs(d - i) <= 1)) continue;
            if (u % 12 < 8) p.Prop(u, 0, 2 * Kc + 1, BlockType.Bench, Material.Steel, f.Steel.C, Dir.North);
            if (u % 12 < 8 && hall0 + 2 < K1)
            {
                p.Prop(u, 0, 2 * hall0 + 1, BlockType.Machine, Material.Steel, f.Steel.C);
                p.Prop(u, 0, 2 * hall0 + 3, BlockType.Table, Material.Steel, f.Panel.C, Dir.South);
            }
            if (u % 16 < 10 && hall0 + 4 < K1) p.Prop(u, 0, 2 * hall0 + 7, BlockType.Bench, Material.Steel, f.Panel.C, Dir.South);
        }
        for (int i = 4; i < I1 - 2; i += 8)
        {
            p.Lamp(2 * i, 2 * H - 1, 2 * (Kc / 2) + 1, concourse, 1.4f, 16f);
            p.Lamp(2 * i, 2 * H - 1, 2 * ((hall0 + K1) / 2), hall, 1.4f, 16f);
        }
        p.Spawn(2 * (mid + 2), 0, 2 * (hall0 + 1), hall);
        p.Spawn(2 * (mid - 6), 0, 2, concourse);

        // Upstairs: a stair from the hall at each end, a gallery along the landside wide enough to pass the stair on up, offices to the airside glazing.
        int below = hall;
        for (int s = 1; s < storeys; s++)
        {
            int j = s * P;
            bool west = s % 2 == 1;
            int fi = west ? 2 : I1 - 5;
            int up = p.Flight(fi, K1 - 2, Dir.East, j - P, P, 2, "stair", s - 1);
            c.Link(below, up);
            int gallery = p.Room(ModuleKind.Corridor, "gallery", s, 1, j, K1 - 3, I1 - 1, j + H - 1, K1 - 1);
            c.Link(up, gallery);
            p.Level(s, j, s == storeys - 1 ? "top floor" : "first floor");
            p.Fill(1, j, K1 - 4, I1 - 1, j + H - 1, K1 - 4, Solid(f.Panel, Role.Wall));
            p.Glazed(2, j + 1, K1, I1 - 2, K1);
            p.Glazed(2, j + 1, 0, I1 - 2, 0);
            int vault = s == storeys - 1 ? r.Range(0, 2) : -1;
            for (int i = 1; i <= I1 - 1;)
            {
                int w = Math.Min(r.Between(5, 9), I1 - i);
                if (I1 - 1 - (i + w) < 4) w = I1 - i;
                int i1 = i + w - 1;
                bool strong = vault == 0 && i == 1 || vault == 1 && i1 >= I1 - 1;
                string what = strong ? "strongroom" : r.Pick(new[] { "office", "office", "archive", "lounge", "operations" });
                int room = p.Room(strong ? ModuleKind.Vault : ModuleKind.Room, what, s, i, j, 1, i1, j + H - 1, K1 - 5);
                // Its door clear of the stairs in the gallery (the one up to here, and the one on up).
                int door = (i + i1) / 2;
                bool Stairs(int x) => Math.Abs(x - (west ? 3 : I1 - 4)) <= 3 || s + 1 < storeys && Math.Abs(x - (west ? I1 - 4 : 3)) <= 3;
                for (int x = i; x <= i1 && Stairs(door); x++) door = x;
                p.Door(door, j, K1 - 4, true, room, gallery);
                Furnish(p, what, i, 1, i1, K1 - 5, j, room, true);
                if (strong)
                {
                    c.VaultModule = c.CoreModule = room;
                    c.AddSlot(SlotKind.Core, p.Cell(2 * i + 2, 2 * j, 4), room);
                }
                i += w + 1;
                if (i <= I1 - 1) p.Fill(i - 1, j, 1, i - 1, j + H - 1, K1 - 5, Solid(f.Panel, Role.Wall));
            }
            for (int i = 4; i < I1 - 2; i += 8) p.Lamp(2 * i, 2 * j + 2 * H - 1, 2 * K1 - 3, gallery, 1f, 10f);
            below = gallery;
        }
    }

    /// <summary>
    /// A small terminal (an airstrip's): one hall through from the landside door to the airside one, benches down it, an
    /// office at one end with the strip's papers and its cache.
    /// </summary>
    static void Shed(Plot p, int I1, int K1)
    {
        Canvas c = p.C;
        Fab f = p.F;
        const int H = 2;
        int mid = I1 / 2;
        p.Shell(0, 0, I1, K1, 0, H, f.Wall, f.Roof);
        p.Plinth(0, 0, I1, K1, 0);
        p.Weathers(-1, 0, -1, I1 + 1, H + 1, K1 + 1, H);
        p.Approach(mid, K1 + 2, Dir.South);
        int ow = Math.Clamp(I1 / 4 + p.R.Range(-1, 2), 2, Math.Max(2, I1 - 6));
        int office = p.Room(ModuleKind.Vault, "office", 0, 1, 0, 1, ow, H - 1, K1 - 1);
        p.Fill(ow + 1, 0, 1, ow + 1, H - 1, K1 - 1, Solid(f.Panel, Role.Wall));
        int hall = p.Room(ModuleKind.Hall, "hall", 0, ow + 2, 0, 1, I1 - 1, H - 1, K1 - 1);
        p.Door(ow + 1, 0, K1 / 2, false, office, hall);
        p.Door(Math.Max(ow + 3, mid), 0, K1, true, hall, p.Yard);
        var cells = new List<(int, int, int)>();
        for (int v = -3; v <= -1; v++) for (int u = 2 * mid - 1; u <= 2 * mid + 2; u++) for (int y = 0; y <= 2; y++) cells.Add((u, y, v));
        int stand = p.RoomOf(ModuleKind.Outdoor, "stand", 0, cells);
        p.Door(Math.Max(ow + 3, mid), 0, 0, true, hall, stand);
        c.Link(stand, p.Yard);
        // Glazed either side of the airside door, a pier of wall left round it (so a wall's panels head level).
        int dI = Math.Max(ow + 3, mid);
        if (dI - 2 >= ow + 3) p.Glazed(ow + 3, 1, 0, dI - 2, 0);
        if (I1 - 2 >= dI + 2) p.Glazed(dI + 2, 1, 0, I1 - 2, 0);
        Furnish(p, "office", 1, 1, ow, K1 - 1, 0, office, false);
        int du = 2 * Math.Max(ow + 3, mid);
        for (int u = 2 * ow + 6; u < 2 * I1 - 1; u += 3)
            if (Math.Abs(u - du) > 3) p.Prop(u, 0, 3, BlockType.Bench, Material.Steel, f.Steel.C);
        p.Lamp(2 * mid, 3, K1, hall, 1f, 10f);
        p.Spawn(2 * mid + 2, 0, K1, hall);
        c.VaultModule = office;
    }

    /// <summary>What a room of the airfield has in it, by what it is for: along its back wall, clear of its door.</summary>
    internal static void Furnish(Plot p, string what, int i0, int k0, int i1, int k1, int j, int room, bool doorBack)
    {
        Fab f = p.F;
        int y = 2 * j, along = doorBack ? 2 * k0 : 2 * k1 + 1, front = doorBack ? 2 * k1 + 1 : 2 * k0;
        int u0 = 2 * i0, u1 = 2 * i1 + 1;
        void Row(BlockType t, Material m, Rgb col, int every = 2) { for (int u = u0 + 1; u < u1; u += every) p.Prop(u, y, along, t, m, col); }
        switch (what)
        {
            case "baggage":
                // (The belt stops short of the side walls: the room is one floor round it.)
                p.Machine(i0 + 1, j, (k0 + k1) / 2, i1 - 1, (k0 + k1) / 2, 1, f.Steel);
                Row(BlockType.Crate, Material.Scrap, f.Rust.C, 3);
                break;
            case "office" or "operations":
                // Desks with their screens along the wall, a shelf by the door; operations has its consoles too.
                Row(BlockType.Table, Material.Steel, f.Panel.C, 3);
                p.Prop(u0 + 1, y, front, BlockType.Shelf, Material.Steel, f.Panel.C);
                p.Terminal(u1 - 1, y, along, room);
                if (what == "operations") p.Prop(u0 + 1, y, along + (front > along ? 1 : -1), BlockType.Machine, Material.Steel, f.Steel.C);
                break;
            case "staff":
                // Lockers along the wall, a table and bench in the middle.
                Row(BlockType.Locker, Material.Steel, f.Steel.C);
                p.Prop((u0 + u1) / 2, y, (along + front) / 2, BlockType.Table, Material.Steel, f.Panel.C);
                p.Prop((u0 + u1) / 2 - 1, y, (along + front) / 2, BlockType.Bench, Material.Steel, f.Steel.C);
                break;
            case "shop":
                // Shelves along the wall, a counter by the way in.
                Row(BlockType.Shelf, Material.Steel, f.Panel.C);
                p.Prop(u0 + 1, y, (along + front) / 2, BlockType.Table, Material.Steel, f.Steel.C);
                break;
            case "archive":
                // Stacks: along the wall and a free-standing run down the middle.
                Row(BlockType.Shelf, Material.Steel, f.Panel.C);
                for (int u = u0 + 2; u < u1 - 3; u += 2) p.Prop(u, y, (along + front) / 2, BlockType.Shelf, Material.Steel, f.Panel.C);
                break;
            case "cafe" or "lounge":
                // (A cafe's tables still laid.)
                for (int u = u0 + 1; u < u1 - 2; u += 3) p.Prop(u, y, (along + front) / 2, what == "cafe" ? BlockType.Meal : BlockType.Table, Material.Steel, f.Panel.C);
                Row(BlockType.Bench, Material.Steel, f.Steel.C, 3);
                break;
            case "toilets":
                // Stalls along the wall, the floor wet in front of them.
                Row(BlockType.Partition, Material.Steel, f.Panel.C);
                p.Wet((u0 + u1) / 2, y, (along + front) / 2);
                break;
            case "strongroom":
                Row(BlockType.Shelf, Material.Steel, f.Steel.C, 3);
                p.Prop(u0 + 1, y, front, BlockType.Crate, Material.Steel, f.Steel.C);
                break;
        }
        p.Cache(u1 - 2, y, (along + front) / 2, room);
        p.Lamp((u0 + u1) / 2, y + 3, (along + front) / 2, room, 0.9f, 9f);
    }

    static IEnumerable<(int I, int K)> Edge(int i0, int k0, int i1, int k1)
    {
        for (int k = k0; k <= k1; k++)
            for (int i = i0; i <= i1; i++)
                if (i == i0 || i == i1 || k == k0 || k == k1) yield return (i, k);
    }

    // ---- the control tower ----------------------------------------------------------------------

    /// <summary>
    /// A square shaft of 6 m storeys, an equipment room on each, one ladder up through them all against the back
    /// wall; the cab on top a block wider all round, glazed on every side, its consoles along the front.
    /// </summary>
    static void Tower(Plot p, SitePart part)
    {
        Canvas c = p.C;
        Fab f = p.F;
        var (S1, T1) = Frame(part);
        int n = Math.Max(2, (int)MathF.Round((part.Num("h", 30) - 8f) / 6f));
        const int P = 3, H = 2;
        int mid = S1 / 2, midK = T1 / 2;
        p.Approach(mid, -2, Dir.North);
        int below = -1;
        for (int s = 0; s < n; s++)
        {
            int j = s * P;
            p.Shell(0, 0, S1, T1, j, H, f.Wall, f.Roof);
            if (s == 0) p.Plinth(0, 0, S1, T1, 0);
            string what = s == 0 ? p.R.Pick(new[] { "lobby", "store", "radio room" }) : p.R.Pick(new[] { "equipment", "office", "rest room", "radio room" });
            int room = p.Room(ModuleKind.Room, what, s, 1, j, 1, S1 - 1, j + H - 1, T1 - 1);
            if (s > 0) { c.Link(below, room); p.Level(s, j, "floor " + s); }
            else p.Door(mid, 0, 0, true, room, p.Yard);
            if (s > 0) p.Glazed(mid, j + 1, 0, mid, 0);
            if (S1 >= 4 && T1 >= 3 && s > 0) p.Machine(S1 - 1, j, 1, S1 - 1, 1, 1, f.Steel);
            p.Lamp(2 * mid + 1, 2 * j + 3, 2 * midK + 1, room, 0.8f, 8f);
            below = room;
        }
        // The cab: a block over the shaft on every side, glazed between corner posts.
        int jc = n * P, o = Math.Max(1, (part.Int("cab", 10) / 2 - (S1 + 1)) / 2 + 1) + p.R.Range(0, 2);
        int c0 = -o, c1 = S1 + o, e0 = -o, e1 = T1 + o;
        p.Shell(c0, e0, c1, e1, jc, H, f.Wall, f.Roof);
        for (int y = jc; y <= jc + 1; y++)
        {
            foreach (int k in new[] { e0, e1 }) p.Glazed(c0 + 1, y, k, c1 - 1, k);
            foreach (int i in new[] { c0, c1 }) p.Glazed(i, y, e0 + 1, i, e1 - 1);
        }
        int cab = p.Room(ModuleKind.Vault, "control cab", n, c0 + 1, jc, e0 + 1, c1 - 1, jc + H - 1, e1 - 1);
        c.Link(below, cab);
        p.Level(n, jc, "cab");
        p.Machine(c0 + 2, jc, e0 + 1, c1 - 2, e0 + 1, 1, f.Steel);
        p.Terminal(2 * mid, 2 * jc, 2 * (e0 + 2), cab);
        p.Prop(2 * mid + 2, 2 * jc, 2 * (e0 + 2) + 1, BlockType.Bench, Material.Steel, f.Panel.C);
        p.Cache(2 * (c1 - 1), 2 * jc, 2 * (e1 - 1), cab);
        p.Lamp(2 * mid, 2 * jc + 3, 2 * midK, cab, 1f, 12f);
        c.VaultModule = c.CoreModule = cab;
        c.AddSlot(SlotKind.Core, p.Cell(2 * mid + 1, 2 * jc, 2 * midK + 1), cab);
        // The ladder: from the ground to the cab's floor, against the back wall, through a hatch in every slab.
        p.Ladder(2, 0, 2 * jc - 1, 2 * T1 - 1, Dir.South);
        // A mast on the roof with a lamp on its tip.
        p.Fill(mid, jc + H + 1, midK, mid, jc + H + 1, midK, Solid(f.Steel, Role.Decor));
        p.Put(2 * mid, 2 * (jc + H + 1) + 1, 2 * midK, new Block(BlockType.Cube, Material.Lumen, f.Emergency, Role.Light));
        p.Weathers(c0 - 1, 0, e0 - 1, c1 + 1, jc + H + 2, e1 + 1, jc + H);
    }

    // ---- a hangar -------------------------------------------------------------------------------

    /// <summary>One clear span across its front, the door nearly all of it, folded leaves stacked inside; offices at the back corners.</summary>
    static void Hangar(Plot p, SitePart part)
    {
        Canvas c = p.C;
        Fab f = p.F;
        var (I1, K1) = Frame(part);
        int H = Math.Max(4, (int)MathF.Ceiling((part.Num("h", 16) - 2f) / 2f));   // (its top, a block over the hall, at least as high as rolled)
        p.Approach(I1 / 2, -2, Dir.North);
        int hall = p.Hall("hangar floor", ModuleKind.Hall, 0, 0, 0, I1, K1, 0, H, f.Wall, f.Roof);
        int d0 = I1 > 8 ? 2 : 1, d1 = I1 - d0, dh = H - 2;
        p.Mouth(d0, 0, 0, d1, dh, 0, hall, p.Yard);
        // The door's leaves, rolled back and stacked at each side of the opening.
        foreach (int i in new[] { d0 - 1, d1 + 1 }) p.Fill(i, 0, 1, i, dh, 1, Solid(f.Steel, Role.Wall));
        p.FloorStripes(2 * d0, 4, 2 * d1 + 1, 4);
        // Offices at the back corners, a door each onto the floor.
        bool deep = K1 >= 12 && I1 >= 14;
        if (deep)
            foreach (var (i0, i1, what) in new[] { (1, 6, "office"), (I1 - 6, I1 - 1, "stores") })
            {
                int room = p.Hall(what, ModuleKind.Room, 0, i0, K1 - 6, i1, K1 - 1, 0, 2, f.Panel, f.Roof, weather: false);
                p.Door((i0 + i1) / 2, 0, K1 - 6, true, room, hall);
                Furnish(p, what == "stores" ? "archive" : "office", i0 + 1, K1 - 5, i1 - 1, K1 - 2, 0, room, false);
                p.Glazed(i0 + 1, 1, K1 - 6, (i0 + i1) / 2 - 1, K1 - 6);
            }
        // Along the side walls: benches, machines and crates, a clear floor down the middle.
        var rng = p.R.Fork("hangar");
        for (int k = 3; k < K1 - (deep ? 7 : 1); k += 2)
            foreach (int u in new[] { 2, 2 * I1 - 1 })
            {
                int pick = rng.Range(0, 4);
                if (pick == 0) continue;
                p.Prop(u, 0, 2 * k, pick == 1 ? BlockType.Bench : pick == 2 ? BlockType.Crate : BlockType.Machine, Material.Steel, pick == 2 ? f.Rust.C : f.Steel.C);
            }
        for (int i = 3; i < I1 - 1; i += 6)
            for (int k = 4; k < K1 - 1; k += 8) p.Lamp(2 * i, 2 * H - 1, 2 * k, hall, 1.8f, 20f);
        p.Spawn(2 * I1 / 2, 0, 2 * K1 / 2, hall);
        p.Cache(4, 0, 2 * K1 - 2 - (deep ? 12 : 0), hall);
    }

    // ---- the fuel farm --------------------------------------------------------------------------

    /// <summary>Tanks in rows inside a low bund wall, a ladder up each, a gap in the bund at the front for the bowser.</summary>
    static void Fuel(Plot p, SitePart part)
    {
        Canvas c = p.C;
        Fab f = p.F;
        var (I1, K1) = Frame(part);
        p.Approach(I1 / 2, -2, Dir.North);
        int yard = p.Room(ModuleKind.Outdoor, "fuel farm", 0, 1, 0, 1, I1 - 1, 0, K1 - 1);
        c.Link(p.Yard, yard);
        p.Pave(1, 1, I1 - 1, K1 - 1, f.Floor.Scale(0.8f));
        foreach (var (i, k) in Edge(0, 0, I1, K1))
            if (k != 0 || Math.Abs(i - I1 / 2) > 1) p.Fill(i, 0, k, i, 0, k, Solid(f.Plinth, Role.Wall));
        int count = Math.Max(1, part.Int("count", 3));
        double rb = Math.Max(1.5, part.Num("r", 6) / 2.0);
        int levels = Math.Max(2, part.Int("h", 10) / 2);
        int cols = (int)Math.Ceiling(Math.Sqrt(count)), rows = (int)Math.Ceiling(count / (double)cols);
        double ci0 = 2 + rb, ck0 = 2 + rb, step = Math.Max(2 * rb + 2, Math.Min((I1 - 2 - 2 * rb) / Math.Max(1, cols - 1), (K1 - 2 - 2 * rb) / Math.Max(1, rows - 1)));
        for (int t = 0; t < count; t++)
        {
            double ci = ci0 + t % cols * step, ck = ck0 + t / cols * step;
            if (ci + rb > I1 - 1 || ck + rb > K1 - 1) continue;
            c.Begin(); // (each tank its own object)
            p.Drum(ci, ck, rb, 0, levels - 1, f.Steel, j => j == levels / 2 ? new Stuff2(f.Steel.M, f.Hazard) : f.Steel);
            p.Disc(ci, ck, rb, levels, Solid(f.Steel, Role.Roof));
            p.Ladder((int)Math.Round(2 * ci), 0, 2 * levels + 1, (int)Math.Floor(2 * (ck - rb)) - 1, Dir.South);
        }
    }

    // ---- the perimeter fence --------------------------------------------------------------------

    /// <summary>A run of mesh on posts along its front, open at its gates.</summary>
    static void Fence(Plot p, SitePart part)
    {
        Fab f = p.F;
        int len = part.Int("fl"), h = Math.Max(1, part.Int("h", 2));
        var gates = part.Arg("gates").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
        int at = gates.Count > 0 ? gates[0] : len / 2;
        p.Approach(Math.Clamp(at / 2, 1, len / 2 - 2), -2, Dir.North);
        foreach (int g in gates) p.C.X.Markers.Add(new Marker("gate", p.Cell(g, 0, 0), "fence"));
        for (int u = 0; u < len; u++)
        {
            if (gates.Any(g => Math.Abs(u - g) <= 6)) continue;
            if (Hash.Cell(p.Cell(u, 0, 0), 61) % 1000 < 30) continue; // a panel down here and there
            for (int y = 0; y < h; y++)
                p.Lay(u, y, 0, u % 4 == 0 ? new Block(BlockType.Pillar, Material.Steel, f.Steel.C, Role.Wall) : new Block(BlockType.Lattice, Material.Steel, f.Steel.C.Scale(1.1f), Role.Wall));
        }
    }

    // ---- approach lights ------------------------------------------------------------------------

    /// <summary>A line of light poles running out from the runway's end along the frame's depth, a crossbar every fifth.</summary>
    static void Lights(Plot p, SitePart part)
    {
        Fab f = p.F;
        int w = part.Int("fl"), len = part.Int("fd"), u = w / 2;
        p.Approach(Math.Max(1, u / 2), -2, Dir.North);
        int n = 0;
        for (int v = 15; v < len; v += 30, n++)
        {
            int h = 3 + n / 6;
            for (int y = 0; y < h; y++) p.Lay(u, y, v, new Block(BlockType.Pillar, Material.Steel, f.Steel.C, Role.Wall));
            int bar = n % 5 == 4 ? Math.Min(u - 1, 8) : 1;
            for (int a = -bar; a <= bar; a++) p.Lay(u + a, h, v, new Block(BlockType.Cube, Material.Lumen, f.Lamp, Role.Light));
        }
    }
}
