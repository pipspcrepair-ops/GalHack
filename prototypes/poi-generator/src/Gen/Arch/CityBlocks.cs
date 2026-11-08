#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// What a city's planner asks of one lot, in the player's 2 m blocks: the kind of building, the lot's width
/// along its street and its depth back from it (lot line to lot line: the place builds all of it), its
/// storeys, a style roll and how far it has fallen (0 standing, 1 a stump). Lean: its upper storeys shear
/// over the free side of the lot. Toppled: its top came down onto it and round it. Back, Left and Right:
/// how many cells the ground past that line stands above this lot's (a lot on a hillside holds it with a
/// retaining wall). EndRight: the last lot of its row, fenced on its right as well as its left. The city
/// decides all of it from the district, the land and the world's fall; nothing is placed by hand.
/// </summary>
public sealed record CityLot(string Kind, int W, int D, int Storeys, int Style, float Fall,
    bool Lean = false, bool Toppled = false, int Back = 0, int Left = 0, int Right = 0, bool EndRight = false);

/// <summary>
/// A city's lots on the 2 m grid, built as a player builds: storeys of floor slab and walls, stairs, curtain
/// walls of panes or banded windows, and everything between the lot line and the building: front lawns and
/// setbacks, drives to a carport, paths to front doors, fences with a gap at the drive, car parks with painted
/// bays, loading aprons, back gardens. The front of the lot (v = 0, the lot line) faces its street; the
/// planner turns the plot to it, and the place's gate (<see cref="LayoutExtras.Gate"/>) is where its drive or
/// path crosses the lot line, for the street's crossover. Kinds:
///   - tower: 8 to 30 storeys; a glass slab, a banded office block, a stepped top or a slab on a podium, some
///     with a spire; a forecourt, a car park behind down a side drive on some;
///   - midrise: 4 to 8 storeys of banded windows, mostly with a car park;
///   - terrace: a row of narrow houses sharing their walls under one gable, a front garden each and a back
///     garden behind, a passage down the side where the lot is wider than the row;
///   - house: a detached house set back behind a lawn, a drive to a carport beside it, a back garden;
///   - works: a tall shed behind a loading apron, its yard fenced;
///   - civic: a domed hall behind a paved forecourt, lawns round it;
///   - station: a booking hall, a car park in front, platforms under canopies behind it with the track bed
///     between them left for the railway.
/// </summary>
public static class CityBlocks
{
    const int Pitch = 3, H = 2;

    public static void Compose(Canvas c, Rng rng, LayoutHints? hints, TechLevel tech, SitePart part)
    {
        CityLot lot = hints?.Lot ?? new CityLot(part.Arg("kind", "tower"), part.Int("w", 18), part.Int("d", 17), part.Int("storeys", 12), (int)(rng.NextU64() % 64), 0f);
        var p = new Plot(c, rng, tech, hints?.Front ?? Dir.North);
        Civic.Wear(p);
        switch (lot.Kind)
        {
            case "terrace": Terrace(p, lot); break;
            case "house": House(p, lot); break;
            case "works": Works(p, lot); break;
            case "civic": CivicHall(p, lot); break;
            case "station": Station(p, lot); break;
            default: Storeyed(p, lot); break;
        }
        p.Finish();
    }

    static Block Solid(Stuff2 s, Role r = Role.Wall) => Plot.Cube(s, r);

    static readonly Rgb[] Facades =
    {
        new(0xc8, 0x6a, 0x4a), new(0xd8, 0xb0, 0x5a), new(0x6a, 0x9a, 0xb4), new(0xe0, 0xd4, 0xc0),
        new(0x8a, 0xb0, 0x7a), new(0xc4, 0x8a, 0xa8), new(0x5a, 0x7a, 0x9a), new(0xb8, 0x5a, 0x50),
    };

    static readonly Rgb LinePaint = new(0xd8, 0xd8, 0xd0), Tarmac = new(0x3a, 0x3a, 0x3c);

    // ---- the lot round a building ---------------------------------------------------------------

    /// <summary>The way in, just inside the lot line at block i, and the gate the street's crossover meets (cell u on the lot line).</summary>
    static void Gate(Plot p, int i, int gateU)
    {
        p.Approach(i, 1, Dir.North);
        p.C.X.Gate = p.Cell(gateU, 0, 0);
    }

    static Block Rail(Plot p) => p.Tech == TechLevel.Low ? new Block(BlockType.Rail, Material.Timber, new Rgb(0x6a, 0x58, 0x46), Role.Decor)
        : new Block(BlockType.Rail, p.F.Steel.M, p.F.Steel.C, Role.Decor);

    /// <summary>
    /// The lot's edges: where the ground past a line stands higher (a lot on a hillside), a retaining wall up to
    /// it; then the fence, on the lot's own edge cells and on top of any wall: along the front with gaps where a
    /// drive or path comes through, down the left side (and the right on its row's last lot: every line between
    /// two lots is fenced once) and along the back. A low wall at a house's front, rails elsewhere. Sections
    /// have fallen, more the further the city fell.
    /// </summary>
    static void Edges(Plot p, CityLot lot, Func<int, bool> frontGap, bool lowFront, bool sides = true)
    {
        int U = 2 * lot.W - 1, V = 2 * lot.D - 1;
        Block rail = Rail(p), low = Solid(p.F.Plinth), hold = Solid(p.F.Plinth.Scale(0.9f));
        bool Open(int u, int y, int v) => p.Get(u, y, v) is var b && (b.IsAir || b.Type == BlockType.Plate);
        IEnumerable<(int U, int V)> Line(int u0, int v0, int u1, int v1)
        {
            int len = Math.Max(Math.Abs(u1 - u0), Math.Abs(v1 - v0));
            for (int t = 0; t <= len; t++) yield return (u0 + Math.Sign(u1 - u0) * t, v0 + Math.Sign(v1 - v0) * t);
        }
        void Hold(IEnumerable<(int U, int V)> cells, int rise)
        {
            foreach (var (u, v) in cells)
                for (int y = 0; y < rise; y++)
                    if (Open(u, y, v)) p.Put(u, y, v, hold);
        }
        void Fence(IEnumerable<(int U, int V)> cells, Block b, int rise)
        {
            // Rails fall in 2 m sections; a wall mostly stands.
            float down = b.Type == BlockType.Rail ? 6 + lot.Fall * 45 : 2 + lot.Fall * 12;
            foreach (var (u, v) in cells)
            {
                if (!Open(u, rise, v) || rise > 0 && !Open(u, rise - 1, v) && p.Get(u, rise - 1, v).Type != BlockType.Cube) continue;
                if (Hash.Cell(new Int3(u / 2, rise, v / 2), 0xFE7u + (uint)lot.Style) % 100 < down) continue;
                p.Put(u, rise, v, b);
            }
        }
        int left = Math.Clamp(lot.Left, 0, 8), right = Math.Clamp(lot.Right, 0, 8), back = Math.Clamp(lot.Back, 0, 8);
        Hold(Line(0, 1, 0, V), left);
        Hold(Line(U, 1, U, V), right);
        Hold(Line(0, V, U, V), back);
        Fence(Line(0, 0, U, 0).Where(c => !frontGap(c.U)), lowFront ? low : rail, 0);
        if (!sides) return;
        Fence(Line(0, 1, 0, V), rail, left);
        if (lot.EndRight) Fence(Line(U, 1, U, V), rail, right);
        Fence(Line(1, V, U - 1, V), rail, back);
    }

    /// <summary>A car park: tarmac over a box of blocks, a row of bays painted along its back (and its front where it is deep enough), 3 m wide and 5 m long.</summary>
    static void CarPark(Plot p, int i0, int k0, int i1, int k1, int shade)
    {
        int u0 = 2 * i0, u1 = 2 * i1 + 1, v0 = 2 * k0, v1 = 2 * k1 + 1;
        var line = new Block(BlockType.Plate, Material.Concrete, LinePaint.Scale(0.8f + 0.2f * (shade % 3) / 2f), Role.Floor);
        void Bays(int vFrom, int vTo)
        {
            for (int u = u0 + 1; u <= u1 - 1; u += 3)
                for (int v = vFrom; v <= vTo; v++) p.Lay(u, 0, v, line);
        }
        // Against the back fence; a second row facing it across a 6 m aisle where the park is deep enough.
        if (v1 - v0 + 1 >= 11) Bays(v1 - 5, v1 - 1);
        if (v1 - v0 + 1 >= 17) Bays(v0, v0 + 4);
        p.Pave(i0, k0, i1, k1, new Stuff2(Material.Concrete, Tarmac));
    }

    /// <summary>Wreckage on open ground: lengths of fallen wall and floor lying where they came down, some tipped on an edge.</summary>
    static void Debris(Plot p, Stuff2 wall, int i0, int k0, int i1, int k1, int count, Func<int, int, bool> keepClear)
    {
        for (int n = 0; n < count; n++)
        {
            bool alongI = p.R.Chance(0.5f);
            int len = p.R.Range(2, 5);
            int i = p.R.Range(i0, Math.Max(i0 + 1, i1 - (alongI ? len : 0) + 1)), k = p.R.Range(k0, Math.Max(k0 + 1, k1 - (alongI ? 0 : len) + 1));
            bool clear = true;
            for (int t = 0; t < len && clear; t++)
            {
                int a = alongI ? i + t : i, b = alongI ? k : k + t;
                clear = a <= i1 && b <= k1 && !keepClear(a, b) && p.Get(2 * a, 0, 2 * b) is var g && (g.IsAir || g.Type == BlockType.Plate);
            }
            if (!clear) continue;
            Dir tip = alongI ? (p.R.Chance(0.5f) ? Dir.North : Dir.South) : (p.R.Chance(0.5f) ? Dir.East : Dir.West);
            for (int t = 0; t < len; t++)
            {
                int a = alongI ? i + t : i, b = alongI ? k : k + t;
                // A slab lying flat a cell thick, or a panel tipped onto its edge as a slope.
                for (int du = 0; du < 2; du++)
                    for (int dv = 0; dv < 2; dv++)
                        p.Put(2 * a + du, 0, 2 * b + dv, n % 3 == 0 ? new Block(BlockType.Ramp, wall.M, wall.C.Scale(0.85f), Role.Wall, tip) : Solid(wall.Scale(0.85f)));
            }
        }
    }

    // ---- towers and blocks ----------------------------------------------------------------------

    /// <summary>
    /// A tower or a mid-rise: a storey of floor slab, walls and roof every three levels, one open floor each, a
    /// stair core in the middle that switches sides each floor. A paved forecourt to the lot line; on some, a car
    /// park behind down a side drive. Styles: a glass slab, a banded office block, a stepped top, a slab on a
    /// podium; plant boxes on the roof behind a parapet, a spire on the tallest. A leaning tower's storeys from a
    /// third of the way up stand further over the free side of its lot, a block at a time; a toppled one is the
    /// stump its top fell onto and round.
    /// </summary>
    static void Storeyed(Plot p, CityLot lot)
    {
        Fab f = p.F;
        bool tower = lot.Kind == "tower";
        int W = Math.Max(11, lot.W), D = Math.Max(12, lot.D), storeys = Math.Max(1, lot.Storeys);
        int style = tower ? lot.Style % 4 : 1; // 0 glass slab, 1 banded office, 2 stepped top, 3 podium and slab
        Stuff2 wall = style == 0 ? new Stuff2(Material.Steel, new Rgb(0x3e, 0x4a, 0x54))
            : !tower && lot.Style % 4 == 3 ? new Stuff2(f.Wall.M, Facades[lot.Style % Facades.Length])
            : f.Wall;
        const int K0 = 2;
        bool park = D >= 15 && W >= 14 && (tower ? lot.Style % 5 < 2 : lot.Style % 5 < 4);
        bool driveLeft = (lot.Style >> 3 & 1) == 0;
        const int Drive = 3;
        int i0 = park && driveLeft ? Drive : 0, i1 = park && !driveLeft ? W - 1 - Drive : W - 1;
        int depth = Math.Min(D - K0 - (park ? 6 : 1), 16), k1 = K0 + depth - 1;
        int w = i1 - i0 + 1, d = depth;
        int steps = style == 2 && w >= 14 && d >= 14 ? Math.Min(storeys / 3, 6) : 0;
        int podium = style == 3 && storeys >= 8 && Math.Min(w, d) >= 13 ? 2 + lot.Style % 2 : 0;
        int Inset(int s) => Math.Max(0, Math.Min(podium > 0 ? (s >= podium ? 2 : 0)
            : s >= storeys - steps ? 2 * (1 + (s - (storeys - steps)) / Math.Max(1, steps / 2 + 1)) : 0, Math.Min(w, d) / 2 - 5));

        // The lean: over the side of the lot with the most room (the drive or the car park), a block at a time.
        int leanDir = -1, room = 0;
        if (lot.Lean && !lot.Toppled)
            foreach (var (dir, r) in new[] { (0, W - 1 - i1), (1, D - 2 - k1), (2, i0) })
                if (r > room) (leanDir, room) = (dir, r);
        int hinge = Math.Max(1, storeys / 3);
        int Shift(int s) => leanDir < 0 || s < hinge ? 0 : Math.Min(s - hinge + 1, Math.Max(1, room * (s - hinge + 1) / Math.Max(1, storeys - hinge)));
        int Ox(int s) => leanDir == 0 ? Shift(s) : leanDir == 2 ? -Shift(s) : 0;
        int Oz(int s) => leanDir == 1 ? Shift(s) : 0;

        int cx = (i0 + i1 + 1) / 2, cz = K0 + d / 2;
        var rooms = new int[storeys];
        var stairs = new int[Math.Max(0, storeys - 1)];
        // The shells first, then the stairs cut through their slabs, then the floors round the stairs.
        for (int s = 0; s < storeys; s++)
            p.Shell(i0 + Inset(s) + Ox(s), K0 + Inset(s) + Oz(s), i1 - Inset(s) + Ox(s), k1 - Inset(s) + Oz(s), s * Pitch, H, wall, f.Roof);
        for (int s = 0; s + 1 < storeys; s++)
            stairs[s] = s % 2 == 0 ? p.Flight(cx - 2 + Ox(s), cz - 2 + Oz(s), Dir.East, s * Pitch, Pitch, 2, "stair", s)
                                   : p.Flight(cx + Ox(s), cz + 1 + Oz(s), Dir.West, s * Pitch, Pitch, 2, "stair", s);
        for (int s = 0; s < storeys; s++)
        {
            int j = s * Pitch, inset = Inset(s);
            int a0 = i0 + inset + Ox(s), a1 = i1 - inset + Ox(s), b0 = K0 + inset + Oz(s), b1 = k1 - inset + Oz(s);
            if (s > 0) p.Level(s, j, $"floor {s + 1}");
            rooms[s] = p.Room(ModuleKind.Hall, s == 0 ? "lobby" : tower ? "office floor" : "floor", s, a0 + 1, j, b0 + 1, a1 - 1, j + H - 1, b1 - 1);
            // The skin: panes floor to ceiling between piers (glass), or a band of windows over a band of wall.
            int every = style == 0 ? 4 : 3;
            for (int i = a0 + 1; i < a1; i++)
            {
                if ((i - a0) % every == 0) continue;
                foreach (int k in new[] { b0, b1 })
                {
                    if (s == 0 && k == K0 && i >= cx - 2 && i <= cx + 1) continue; // the lobby's mouth
                    if (style == 0) p.Glazed(i, j, k, i, k);
                    p.Glazed(i, j + 1, k, i, k);
                }
            }
            for (int k = b0 + 1; k < b1; k++)
            {
                if ((k - b0) % every == 0) continue;
                foreach (int i in new[] { a0, a1 })
                {
                    if (style == 0) p.Glazed(i, j, k, i, k);
                    p.Glazed(i, j + 1, k, i, k);
                }
            }
            if (s > 0) p.Repaint(2 * a0, 2 * j - 2, 2 * b0, 2 * a1 + 1, 2 * j - 1, 2 * b0 + 1, (_, _, _) => wall.C.Scale(0.78f));
            // Lamps in the corners, clear of the stair core whatever the floor's inset.
            p.Lamp(2 * (a0 + 1) + 1, 2 * j + 3, 2 * (b0 + 1) + 1, rooms[s], 1.2f, 12f);
            if (a1 - a0 > 10) p.Lamp(2 * (a1 - 1), 2 * j + 3, 2 * (b1 - 1), rooms[s], 1.2f, 12f);
        }
        for (int s = 0; s + 1 < storeys; s++)
        {
            p.C.Link(rooms[s], stairs[s]);
            p.C.Link(stairs[s], rooms[s + 1]);
        }

        // The forecourt, the way in and its gate; the drive and the car park.
        int mid = cx;
        Gate(p, mid, park ? 2 * (driveLeft ? 0 : W - Drive) + Drive : 2 * mid);
        p.Pave(0, 0, W - 1, K0 - 1, f.Floor.Scale(0.9f));
        p.Mouth(mid - 1, 0, K0, mid, 1, K0, p.Yard, rooms[0]);
        if (park)
        {
            int d0 = driveLeft ? 0 : W - Drive;
            CarPark(p, 0, k1 + 1, W - 1, D - 1, lot.Style);
            p.Pave(d0, K0, d0 + Drive - 1, k1, new Stuff2(Material.Concrete, Tarmac));
        }
        p.C.VaultModule = rooms[^1];

        // The top: a parapet, plant boxes, and on the tallest a spire.
        int top = storeys - 1, topInset = Inset(top);
        int t0 = i0 + topInset + Ox(top), t1 = i1 - topInset + Ox(top), u0 = K0 + topInset + Oz(top), u1 = k1 - topInset + Oz(top), roofJ = storeys * Pitch - 1;
        if (lot.Toppled)
        {
            // Its fallen storeys heaped on the stump in broken slabs, and lying in pieces round it.
            p.Gable(t0 + 1, u0 + 1, t1 - 1, u1 - 1, roofJ + 1, (lot.Style & 2) == 0, wall.Scale(0.8f), wall.Scale(0.7f), 0);
            int park0 = park ? k1 + 1 : D;
            Debris(p, wall, 0, park0, W - 1, D - 2, 4 + (int)(lot.Fall * 6), (a, b) => false);
            Debris(p, wall, 0, 0, W - 1, K0 - 1, 2, (a, b) => Math.Abs(a - mid) <= 2);
        }
        else
        {
            for (int i = t0; i <= t1; i++) { p.Fill(i, roofJ + 1, u0, i, roofJ + 1, u0, Solid(wall)); p.Fill(i, roofJ + 1, u1, i, roofJ + 1, u1, Solid(wall)); }
            for (int k = u0; k <= u1; k++) { p.Fill(t0, roofJ + 1, k, t0, roofJ + 1, k, Solid(wall)); p.Fill(t1, roofJ + 1, k, t1, roofJ + 1, k, Solid(wall)); }
            if (t1 - t0 >= 8) p.Machine(t0 + 2, roofJ + 1, u0 + 2, t0 + 4, u0 + 3, 1, f.Steel);
            if (t1 - t0 >= 10) p.Machine(t1 - 4, roofJ + 1, u1 - 4, t1 - 2, u1 - 2, 1, f.Panel);
            if (tower && storeys >= 18 && lot.Style % 2 == 0 && lot.Fall < 0.5f)
                p.Post(2 * ((t0 + t1) / 2) + 1, 2 * (roofJ + 1) + Math.Min(40, storeys * 2), 2 * ((u0 + u1) / 2) + 1, f.Steel);
            Ragged(p, lot, wall, t0, u0, t1, u1, roofJ);
        }
        Edges(p, lot, u => true, false, sides: park);
        p.Weathers(-1, 0, K0 - 1, W, roofJ + 2, k1 + 1, roofJ);
        Broken(p, lot, t0, u0, t1, u1, roofJ);
    }

    /// <summary>A fallen top: holes through the last roof where it gave way, more the further the building fell.</summary>
    static void Broken(Plot p, CityLot lot, int i0, int k0, int i1, int k1, int roofJ)
    {
        if (lot.Fall < 0.35f || lot.Toppled) return;
        int holes = (int)(lot.Fall * 6);
        for (int n = 0; n < holes; n++)
        {
            int i = i0 + 2 + p.R.Range(0, Math.Max(1, i1 - i0 - 4)), k = k0 + 2 + p.R.Range(0, Math.Max(1, k1 - k0 - 4));
            p.Dig(i, roofJ, k, Math.Min(i1 - 2, i + 1), roofJ, Math.Min(k1 - 2, k + 1));
        }
    }

    /// <summary>A torn top: notches bitten down through the parapet and the last storey's walls from above, so nothing is left hanging.</summary>
    static void Ragged(Plot p, CityLot lot, Stuff2 wall, int i0, int k0, int i1, int k1, int roofJ)
    {
        if (lot.Fall < 0.5f) return;
        int notches = 1 + (int)(lot.Fall * 5);
        for (int n = 0; n < notches; n++)
        {
            int side = p.R.Range(0, 4), wide = p.R.Range(1, 3), deep = p.R.Range(1, 3);
            int a = side < 2 ? p.R.Range(i0 + 1, Math.Max(i0 + 2, i1 - wide)) : side == 2 ? i0 : i1;
            int b = side >= 2 ? p.R.Range(k0 + 1, Math.Max(k0 + 2, k1 - wide)) : side == 0 ? k0 : k1;
            int a1 = side < 2 ? a + wide - 1 : a, b1 = side >= 2 ? b + wide - 1 : b;
            p.Dig(a, roofJ + 1 - deep, b, a1, roofJ + 1, b1);
        }
    }

    // ---- houses -----------------------------------------------------------------------------------

    /// <summary>
    /// A terrace: narrow houses four blocks apart sharing their side walls, two or three storeys under one gable
    /// along the street, each front its own colour, a chimney on each party wall. A front garden each with a path
    /// to its door through a low wall; a back garden each, fenced from its neighbours, behind a back door. Where
    /// the lot is wider than the row, a passage down the side to the gardens.
    /// </summary>
    static void Terrace(Plot p, CityLot lot)
    {
        Fab f = p.F;
        int W = Math.Max(5, lot.W), D = Math.Max(14, lot.D), storeys = Math.Clamp(lot.Storeys, 1, 3);
        int n = Math.Max(1, (W - 1) / 4), right = 4 * n;
        int s = D >= 16 ? 3 : 2, k0 = s, k1 = s + 6;
        Rgb paint = Facades[(lot.Style * 3 + W) % Facades.Length];
        var face = new Stuff2(f.Wall.M, paint);
        Rgb Front(int m) => Facades[(lot.Style * 3 + W + m * 5) % Facades.Length];
        for (int st = 0; st < storeys; st++)
        {
            int j = st * Pitch;
            p.Shell(0, k0, right, k1, j, H, face, f.Roof);
            for (int m = 1; m < n; m++) p.Fill(4 * m, j, k0 + 1, 4 * m, j + H - 1, k1 - 1, Solid(face));
        }
        var rooms = new int[n, storeys];
        var flights = new int[n, Math.Max(1, storeys - 1)];
        for (int m = 0; m < n; m++)
        {
            // Up the house along one party wall, the next flight back down the depth along the other: 1 block wide.
            if (storeys > 1) flights[m, 0] = p.Flight(4 * m + 1, k0 + 2, Dir.South, 0, Pitch, 1, "stair", 0);
            if (storeys > 2) flights[m, 1] = p.Flight(4 * m + 3, k0 + 4, Dir.North, Pitch, Pitch, 1, "stair", 1);
        }
        for (int st = 1; st < storeys; st++) p.Level(st, st * Pitch, $"floor {st + 1}");
        for (int m = 0; m < n; m++)
            for (int st = 0; st < storeys; st++)
            {
                int j = st * Pitch;
                rooms[m, st] = p.Room(ModuleKind.Room, st == 0 ? "hall" : "quarters", st, 4 * m + 1, j, k0 + 1, 4 * m + 3, j + H - 1, k1 - 1);
                foreach (int i in new[] { 4 * m + 1, 4 * m + 3 })
                {
                    p.Glazed(i, j + 1, k0, i, k0);
                    p.Glazed(i, j + 1, k1, i, k1);
                }
                if (st > 0) p.Glazed(4 * m + 2, j + 1, k0, 4 * m + 2, k0);
                p.Lamp(2 * (4 * m + 3), 2 * j + 3, 2 * (k0 + 1), rooms[m, st], 1f, 9f);
            }
        for (int m = 0; m < n; m++)
        {
            for (int st = 0; st + 1 < storeys; st++)
            {
                p.C.Link(rooms[m, st], flights[m, st]);
                p.C.Link(flights[m, st], rooms[m, st + 1]);
            }
            // Each front its own paint, its windows left alone.
            Rgb c = Front(m);
            for (int u = 8 * m + 1; u <= 8 * m + 8; u++)
                for (int y = 0; y < 2 * storeys * Pitch; y++)
                {
                    Int3 at = p.Cell(u, y, 2 * k0);
                    if (p.C.Blocks.TryGetValue(at, out Block b) && b.Role == Role.Wall) { b.Color = c; p.C.Blocks[at] = b; }
                }
        }
        Gate(p, 2, 4);
        int garden = -1;
        for (int m = 0; m < n; m++)
        {
            p.Door(4 * m + 2, 0, k0, true, p.Yard, rooms[m, 0]);
            // The path to the door through the front garden.
            p.Pave(4 * m + 2, 1, 4 * m + 2, k0 - 1, f.Floor.Scale(1.1f));
            int yard = p.Room(ModuleKind.Outdoor, "garden", 0, 4 * m + 1, 0, k1 + 1, 4 * m + 3, 1, D - 2);
            p.Door(4 * m + 2, 0, k1, true, rooms[m, 0], yard);
            garden = yard;
        }
        int roofJ = storeys * Pitch - 1;
        // The gable runs along the street, so the fronts are eaves, as a terrace's are; a chimney on each party wall.
        p.Gable(0, k0, right, k1, roofJ + 1, true, f.Roof, face);
        int ridge = 2 * (roofJ + 1) + 8;
        for (int m = 1; m < n; m++)
            for (int y = ridge; y <= ridge + 2; y++) p.Put(8 * m, y, 2 * k0 + 6, Solid(f.Plinth.Scale(1.1f)));
        // A passage down the side, where the row leaves room, to the back gardens.
        bool passage = W - 1 - right >= 1;
        if (passage) p.Pave(right + 1, 1, W - 1, D - 2, f.Floor.Scale(1.05f));
        // Fences between the back gardens, and between the last garden and the passage.
        Block rail = Rail(p);
        for (int m = 1; m <= (passage ? n : n - 1); m++)
            for (int v = 2 * (k1 + 1); v < 2 * D - 1; v++)
                if (p.Get(8 * m, 0, v).IsAir && Hash.Cell(new Int3(m, 0, v / 2), 0xBA7u) % 100 >= 10 + lot.Fall * 40) p.Put(8 * m, 0, v, rail);
        Edges(p, lot, u => u % 8 is 4 or 5 && u < 8 * n || passage && u > 2 * right + 1, true);
        p.Weathers(0, 0, k0, right, roofJ + 2 + 4, k1, roofJ);
        Broken(p, lot, 0, k0, right, k1, roofJ);
        p.C.VaultModule = garden >= 0 ? rooms[n - 1, storeys - 1] : rooms[0, 0];
    }

    /// <summary>
    /// A detached house: set back behind its lawn, a drive along one side to a carport against it, a path from
    /// the drive to the front door, the hall and the kitchen downstairs with a stair up to the rooms over them,
    /// a back door to the garden. Fenced, a low wall at the front with the drive's gap in it.
    /// </summary>
    static void House(Plot p, CityLot lot)
    {
        Fab f = p.F;
        int W = Math.Max(10, lot.W), D = Math.Max(14, lot.D), storeys = Math.Clamp(lot.Storeys, 1, 2);
        int s = D >= 17 ? 4 : 3, k0 = s, k1 = s + 6;
        bool driveLeft = (lot.Style & 1) == 0;
        int dr = driveLeft ? 1 : W - 3;
        int h0 = driveLeft ? 4 : 1, h1 = driveLeft ? W - 2 : W - 5;
        Rgb paint = Facades[(lot.Style * 5 + W) % Facades.Length];
        var face = new Stuff2(f.Wall.M, lot.Style % 3 == 0 ? f.Wall.C : paint);
        for (int st = 0; st < storeys; st++) p.Shell(h0, k0, h1, k1, st * Pitch, H, face, f.Roof);
        // The stair along the side away from the drive, up from the hall.
        int lane = driveLeft ? h1 - 1 : h0 + 1;
        int flight = storeys > 1 ? p.Flight(lane, k0 + 2, Dir.South, 0, Pitch, 1, "stair", 0) : -1;
        // The kitchen behind the hall, a wall between them all but where the stair goes up.
        for (int i = h0 + 1; i < h1; i++)
            if (i != lane) p.Fill(i, 0, k0 + 3, i, H - 1, k0 + 3, Solid(f.Panel));
        int doorI = driveLeft ? h0 + 1 : h1 - 1, inner = (h0 + h1) / 2 == lane ? (h0 + h1) / 2 + (driveLeft ? -1 : 1) : (h0 + h1) / 2;
        int hall = p.Room(ModuleKind.Room, "hall", 0, h0 + 1, 0, k0 + 1, h1 - 1, H - 1, k0 + 2);
        int mess = p.Room(ModuleKind.Room, "mess", 0, h0 + 1, 0, k0 + 4, h1 - 1, H - 1, k1 - 1);
        p.Door(inner, 0, k0 + 3, true, hall, mess);
        int upper = -1;
        if (storeys > 1)
        {
            p.Level(1, Pitch, "floor 2");
            upper = p.Room(ModuleKind.Room, "quarters", 1, h0 + 1, Pitch, k0 + 1, h1 - 1, Pitch + H - 1, k1 - 1);
            p.C.Link(hall, flight);
            p.C.Link(flight, upper);
            p.Lamp(2 * (h0 + 1) + 1, 2 * Pitch + 3, 2 * (k0 + 1) + 1, upper, 1f, 9f);
        }
        for (int st = 0; st < storeys; st++)
        {
            int j = st * Pitch;
            for (int i = h0 + 1; i < h1; i += 2)
            {
                if (st == 0 && i == doorI) continue;
                p.Glazed(i, j + 1, k0, i, k0);
                p.Glazed(i, j + 1, k1, i, k1);
            }
        }
        p.Lamp(2 * (h1 - 1), 3, 2 * (k0 + 1), hall, 1f, 9f);

        // The drive from the lot line to the carport, the path to the door, the door and the back door.
        Gate(p, dr + 1, 2 * dr + 2);
        p.Pave(dr, 0, dr + 1, k1 - 1, new Stuff2(f.Floor.M, f.Floor.C.Scale(0.85f)));
        p.Pave(driveLeft ? dr + 2 : doorI, k0 - 1, driveLeft ? doorI : dr - 1, k0 - 1, f.Floor.Scale(1.1f));
        p.Door(doorI, 0, k0, true, p.Yard, hall);
        int yard = p.Room(ModuleKind.Outdoor, "garden", 0, 1, 0, k1 + 1, W - 2, 1, D - 2);
        p.Door(inner, 0, k1, true, mess, yard);
        // The carport: a slab on two posts over the drive's end, against the house's side.
        int c0 = Math.Min(dr, driveLeft ? h0 - 1 : dr), c1 = driveLeft ? h0 - 1 : dr + 1;
        if (!driveLeft) c0 = h1 + 1;
        p.Fill(c0, 1, k0 + 2, c1, 1, k1 - 1, Solid(f.Roof, Role.Roof));
        int postU = driveLeft ? 2 * dr : 2 * (dr + 1) + 1;
        p.Post(postU, 1, 2 * (k0 + 2), f.Steel);
        p.Post(postU, 1, 2 * (k1 - 1) + 1, f.Steel);
        int roofJ = storeys * Pitch - 1;
        p.Gable(h0, k0, h1, k1, roofJ + 1, (lot.Style & 2) == 0, f.Roof, face);
        Edges(p, lot, u => u >= 2 * dr && u <= 2 * dr + 3, lot.Style % 3 == 1);
        p.Weathers(h0 - 1, 0, k0 - 1, h1 + 1, roofJ + 2 + 5, k1 + 1, roofJ);
        Broken(p, lot, h0, k0, h1, k1, roofJ);
        p.C.VaultModule = upper >= 0 ? upper : mess;
    }

    // ---- works, hall and station ------------------------------------------------------------------

    /// <summary>A tall works shed behind a loading apron striped at its door, its plant in rows along the back, its yard fenced with a wide gate at the apron.</summary>
    static void Works(Plot p, CityLot lot)
    {
        Fab f = p.F;
        int W = Math.Max(14, lot.W), D = Math.Max(14, lot.D), h = 4;
        int i0 = 1, i1 = W - 2, k0 = 5, k1 = D - 2;
        int hall = p.Hall("works floor", ModuleKind.Hall, 0, i0, k0, i1, k1, 0, h, f.Wall, f.Roof);
        // A store walled off in the back corner, its door onto the floor: what's worth taking was kept there.
        p.Fill(i0 + 4, 0, k1 - 4, i0 + 4, h - 1, k1 - 1, Solid(f.Panel));
        p.Fill(i0 + 1, 0, k1 - 4, i0 + 3, h - 1, k1 - 4, Solid(f.Panel));
        int store = p.Room(ModuleKind.Room, "storage", 0, i0 + 1, 0, k1 - 3, i0 + 3, h - 1, k1 - 1);
        p.Door(i0 + 2, 0, k1 - 4, true, hall, store);
        p.Cache(2 * (i0 + 2) + 1, 0, 2 * (k1 - 2), store);
        for (int i = i0 + 7; i + 2 < i1 - 1; i += 5)
            p.Machine(i, 0, k1 - 4, i + 2, k1 - 2, 2, i % 2 == 0 ? f.Steel : f.Panel); // along the back wall, the floor open
        for (int i = i0 + 2; i < i1 - 1; i += 4)
        {
            p.Glazed(i, h - 1, k0, i + 1, k0);
            p.Glazed(i, h - 1, k1, i + 1, k1);
        }
        p.Lamp(2 * (i0 + 4), 2 * h - 1, 2 * (k0 + 4), hall, 1.4f, 16f);
        int mid = (i0 + i1) / 2;
        // The apron: concrete from the lot line to the doors, a hazard stripe across the doorway.
        Gate(p, mid, 2 * mid + 1);
        p.Mouth(mid - 2, 0, k0, mid + 1, 2, k0, p.Yard, hall);
        p.FloorStripes(2 * (mid - 2), 2 * k0 - 2, 2 * (mid + 1) + 1, 2 * k0 - 1);
        p.Pave(0, 0, W - 1, k0 - 1, new Stuff2(Material.Concrete, f.Floor.C.Scale(0.95f)));
        p.C.VaultModule = store;
        p.Gable(i0, k0, i1, k1, h, D - k0 > W, f.Roof, f.Wall);
        Edges(p, lot, u => u >= 2 * (mid - 4) && u <= 2 * (mid + 3) + 1, false);
        Broken(p, lot, i0, k0, i1, k1, h);
    }

    /// <summary>A civic hall: a square of tall walls behind a colonnade, an eight-sided dome of slopes on a drum, a paved forecourt to the lot line and lawn round it.</summary>
    static void CivicHall(Plot p, CityLot lot)
    {
        Fab f = p.F;
        int W = Math.Max(20, lot.W), D = Math.Max(20, lot.D), h = 5;
        int w = Math.Min(W - 8, 24), d = Math.Min(D - 10, 22);
        int i0 = (W - w) / 2, i1 = i0 + w - 1, K0 = Math.Min(8, D - d - 2), k1 = K0 + d - 1;
        var stone = new Stuff2(f.Wall.M, new Rgb(0xd8, 0xd2, 0xc4));
        int hall = p.Hall("rotunda", ModuleKind.Hall, 0, i0, K0 + 3, i1, k1, 0, h, stone, f.Roof);
        // The colonnade: a portico roof on columns over the front steps.
        for (int i = i0 + 1; i <= i1 - 1; i += 3) p.Fill(i, 0, K0, i, h - 1, K0, Solid(stone));
        p.Fill(i0, h, K0, i1, h, K0 + 2, Solid(stone, Role.Roof));
        int porch = p.Room(ModuleKind.Hall, "portico", 0, i0 + 1, 0, K0 + 1, i1 - 1, h - 1, K0 + 2);
        for (int i = i0 + 2; i < i1 - 1; i += 3) p.Glazed(i, h - 2, k1, i, k1);
        double ci = (i0 + i1 + 1) / 2.0, ck = (K0 + 3 + k1 + 1) / 2.0, r = Math.Min(w, d - 3) / 2.0 - 2;
        p.Drum(ci, ck, r, h + 1, h + 1, stone);
        p.Cap(ci, ck, r, h + 2, new Stuff2(Material.Bronze, new Rgb(0x6a, 0x8a, 0x7a)));
        p.Lamp(2 * (i0 + 4), 2 * h - 1, 2 * (K0 + 6), hall, 1.6f, 18f);
        int mid = (i0 + i1) / 2;
        Gate(p, mid, 2 * mid + 1);
        // The forecourt: paving across the front, lawn either side and behind.
        p.Pave(i0 - 2, 0, i1 + 2, K0 - 1, stone.Scale(0.85f));
        p.Mouth(mid - 1, 0, K0, mid, 1, K0, p.Yard, porch);
        p.Mouth(mid - 1, 0, K0 + 3, mid, 2, K0 + 3, porch, hall);
        p.Cache(2 * (i0 + 3), 0, 2 * (k1 - 3), hall);
        p.C.VaultModule = hall;
        p.Weathers(i0 - 1, 0, K0 - 1, i1 + 1, h + 4 + (int)r, k1 + 1, h);
    }

    /// <summary>
    /// A station at the edge of town: a booking hall under a gable with a car park in front of it, and behind it
    /// two platforms under canopies with the track bed between them for the railway (laid by the network, not
    /// here). Ramps up onto each platform's end.
    /// </summary>
    static void Station(Plot p, CityLot lot)
    {
        Fab f = p.F;
        int W = Math.Max(24, lot.W), D = Math.Max(30, lot.D), h = 5;
        int i0 = 4, i1 = W - 5, k0 = 8, k1 = 17;
        int hall = p.Hall("concourse", ModuleKind.Hall, 0, i0, k0, i1, k1, 0, h, f.Wall, f.Roof);
        for (int i = i0 + 2; i < i1 - 1; i += 3)
        {
            p.Glazed(i, h - 2, k0, i, k0);
            p.Glazed(i, h - 2, k1, i, k1);
        }
        p.Lamp(2 * (i0 + 4), 2 * h - 1, 2 * (k0 + 4), hall, 1.4f, 16f);
        int mid = (i0 + i1) / 2;
        Gate(p, mid, 2 * mid + 1);
        p.Mouth(mid - 2, 0, k0, mid + 1, 2, k0, p.Yard, hall);
        CarPark(p, 1, 0, W - 2, k0 - 1, lot.Style);
        p.Gable(i0, k0, i1, k1, h, true, f.Roof, f.Wall);
        // The platforms: half-block decks the length of the hall and more, a ramp up at each end, a canopy on posts.
        foreach (var (pk0, pk1) in new[] { (k1 + 1, k1 + 3), (D - 6, D - 4) })
        {
            int a0 = 2, a1 = W - 3;
            for (int v = 2 * pk0; v <= 2 * pk1 + 1; v++)
                for (int u = 2 * a0; u <= 2 * a1 + 1; u++)
                {
                    bool end = u == 2 * a0 || u == 2 * a1 + 1;
                    p.Put(u, 0, v, end ? new Block(BlockType.Ramp, f.Plinth.M, f.Plinth.C, Role.Floor, u == 2 * a0 ? Dir.East : Dir.West) : Solid(f.Plinth, Role.Floor));
                }
            int deck = p.Room(ModuleKind.Outdoor, "platform", 0, a0 + 1, 0, pk0, a1 - 1, 1, pk1);
            p.C.Link(p.Yard, deck);
            for (int i = a0 + 2; i <= a1 - 2; i += 5) p.Post(2 * i + 1, 4, 2 * ((pk0 + pk1) / 2) + 1, f.Steel);
            for (int i = a0 + 1; i <= a1 - 1; i++)
                for (int k = pk0; k <= pk1; k++) p.Fill(i, 2, k, i, 2, k, Solid(f.Roof, Role.Roof));
            if (pk0 == k1 + 1)
            {
                // Out of the hall's back and up a ramp onto the first platform.
                p.Mouth(mid - 1, 0, k1, mid, 1, k1, hall, deck);
                for (int u = 2 * (mid - 1); u <= 2 * mid + 1; u++) p.Put(u, 0, 2 * pk0, new Block(BlockType.Ramp, f.Plinth.M, f.Plinth.C, Role.Floor, Dir.South));
            }
        }
        p.Cache(2 * (i0 + 3), 0, 2 * (k0 + 3), hall);
        p.C.VaultModule = hall;
        Edges(p, lot, u => u >= 2 && u <= 2 * W - 3, false);
        p.Weathers(i0 - 1, 0, k0 - 1, i1 + 1, h + 12, k1 + 1, h);
        Broken(p, lot, i0, k0, i1, k1, h);
    }
}
