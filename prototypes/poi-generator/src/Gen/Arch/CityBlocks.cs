#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// What a city's planner asks of one lot: the kind of building, its footprint in 2 m blocks (width along
/// the street, depth back from it), its storeys, a style roll and how far it has fallen (0 standing, 1 a
/// stump). The city decides all of it from the district, the land and the world's fall; nothing is placed by hand.
/// </summary>
public sealed record CityLot(string Kind, int W, int D, int Storeys, int Style, float Fall);

/// <summary>
/// A city's buildings on the 2 m grid, built as a player builds: storeys of floor slab and walls, a stair
/// core switching sides a floor at a time, curtain walls of panes or banded windows. Kinds:
///   - tower: 8 to 30 storeys; a glass slab, a banded office block or a stepped top, some with a spire;
///   - midrise: 4 to 8 storeys of banded windows, rooftop plant behind a parapet;
///   - terrace: narrow houses of two or three storeys under a gable, painted facades;
///   - works: a tall shed under a gable, its plant inside;
///   - civic: a hall under an eight-sided dome of slopes, a colonnade at its front.
/// The front (low k) faces the street: the planner turns the plot to it.
/// </summary>
public static class CityBlocks
{
    const int Pitch = 3, H = 2, K0 = 2;

    public static void Compose(Canvas c, Rng rng, LayoutHints? hints, TechLevel tech, SitePart part)
    {
        CityLot lot = hints?.Lot ?? new CityLot(part.Arg("kind", "tower"), part.Int("w", 14), part.Int("d", 14), part.Int("storeys", 12), (int)(rng.NextU64() % 6), 0f);
        var p = new Plot(c, rng, tech, hints?.Front ?? Dir.North);
        Civic.Wear(p);
        switch (lot.Kind)
        {
            case "terrace": Terrace(p, lot); break;
            case "works": Works(p, lot); break;
            case "civic": CivicHall(p, lot); break;
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

    /// <summary>The way in: an approach at the street, a lobby mouth in the front wall.</summary>
    static void Front(Plot p, int i0, int i1, int room)
    {
        int mid = (i0 + i1) / 2;
        p.Approach(mid, 0, Dir.North);
        p.Pave(i0, 1, i1, K0 - 1, p.F.Floor.Scale(0.9f));
        p.Mouth(mid - 1, 0, K0, mid, 1, K0, p.Yard, room);
    }

    /// <summary>
    /// A tower or a mid-rise: a storey of floor slab, walls and roof every three levels, one open floor each,
    /// a stair core in the middle that switches sides each floor. A stepped top steps in two blocks a side for
    /// its last floors; a spire stands on the tallest; plant boxes on the roof behind a parapet.
    /// </summary>
    static void Storeyed(Plot p, CityLot lot)
    {
        Fab f = p.F;
        int w = Math.Max(10, lot.W), d = Math.Max(10, lot.D), storeys = Math.Max(1, lot.Storeys);
        bool tower = lot.Kind == "tower";
        int style = lot.Style % 3; // 0 glass slab, 1 banded office, 2 stepped top
        if (!tower) style = 1;
        // The look: a glass slab in dark glass and steel, an office block in the world's walls, a few painted.
        Stuff2 wall = style == 0 ? new Stuff2(Material.Steel, new Rgb(0x3e, 0x4a, 0x54))
            : !tower && lot.Style % 4 == 3 ? new Stuff2(f.Wall.M, Facades[lot.Style % Facades.Length])
            : f.Wall;
        int i0 = 0, i1 = w - 1, k1 = K0 + d - 1;
        int steps = style == 2 && w >= 14 && d >= 14 ? Math.Min(storeys / 3, 6) : 0;
        int cx = w / 2, cz = K0 + d / 2;
        var rooms = new int[storeys];
        var stairs = new int[Math.Max(0, storeys - 1)];
        int Inset(int s) => Math.Min(s >= storeys - steps ? 2 * (1 + (s - (storeys - steps)) / Math.Max(1, steps / 2 + 1)) : 0, Math.Min(w, d) / 2 - 5);
        // The shells first, then the stairs cut through their slabs, then the floors round the stairs.
        for (int s = 0; s < storeys; s++)
            p.Shell(i0 + Inset(s), K0 + Inset(s), i1 - Inset(s), k1 - Inset(s), s * Pitch, H, wall, f.Roof);
        for (int s = 0; s + 1 < storeys; s++)
            stairs[s] = s % 2 == 0 ? p.Flight(cx - 2, cz - 2, Dir.East, s * Pitch, Pitch, 2, "stair", s)
                                   : p.Flight(cx, cz + 1, Dir.West, s * Pitch, Pitch, 2, "stair", s);
        for (int s = 0; s < storeys; s++)
        {
            int j = s * Pitch, inset = Inset(s);
            int a0 = i0 + inset, a1 = i1 - inset, b0 = K0 + inset, b1 = k1 - inset;
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
        Front(p, i0, i1, rooms[0]);
        p.C.VaultModule = rooms[^1];

        // The top: a parapet, plant boxes, and on the tallest a spire.
        int topInset = Math.Min(steps > 0 ? 2 * (1 + (steps - 1) / Math.Max(1, steps / 2 + 1)) : 0, Math.Min(w, d) / 2 - 5);
        int t0 = i0 + topInset, t1 = i1 - topInset, u0 = K0 + topInset, u1 = k1 - topInset, roofJ = storeys * Pitch - 1;
        for (int i = t0; i <= t1; i++) { p.Fill(i, roofJ + 1, u0, i, roofJ + 1, u0, Solid(wall)); p.Fill(i, roofJ + 1, u1, i, roofJ + 1, u1, Solid(wall)); }
        for (int k = u0; k <= u1; k++) { p.Fill(t0, roofJ + 1, k, t0, roofJ + 1, k, Solid(wall)); p.Fill(t1, roofJ + 1, k, t1, roofJ + 1, k, Solid(wall)); }
        if (t1 - t0 >= 8) p.Machine(t0 + 2, roofJ + 1, u0 + 2, t0 + 4, u0 + 3, 1, f.Steel);
        if (t1 - t0 >= 10) p.Machine(t1 - 4, roofJ + 1, u1 - 4, t1 - 2, u1 - 2, 1, f.Panel);
        if (tower && storeys >= 18 && lot.Style % 2 == 0)
            p.Post(2 * ((t0 + t1) / 2) + 1, 2 * (roofJ + 1) + Math.Min(40, storeys * 2), 2 * ((u0 + u1) / 2) + 1, f.Steel);
        p.Weathers(i0 - 1, 0, K0 - 1, i1 + 1, roofJ + 2, k1 + 1, roofJ);
        Broken(p, lot, t0, u0, t1, u1, roofJ);
    }

    /// <summary>A fallen top: holes through the last roof where it gave way, more the further the building fell.</summary>
    static void Broken(Plot p, CityLot lot, int i0, int k0, int i1, int k1, int roofJ)
    {
        if (lot.Fall < 0.35f) return;
        int holes = (int)(lot.Fall * 6);
        for (int n = 0; n < holes; n++)
        {
            int i = i0 + 2 + p.R.Range(0, Math.Max(1, i1 - i0 - 4)), k = k0 + 2 + p.R.Range(0, Math.Max(1, k1 - k0 - 4));
            p.Dig(i, roofJ, k, Math.Min(i1 - 2, i + 1), roofJ, Math.Min(k1 - 2, k + 1));
        }
    }

    /// <summary>Narrow houses side by side under one gable, each front painted its own colour, a stair inside each.</summary>
    static void Terrace(Plot p, CityLot lot)
    {
        Fab f = p.F;
        int w = Math.Max(6, lot.W), d = Math.Max(8, lot.D), storeys = Math.Clamp(lot.Storeys, 1, 3);
        int i0 = 0, i1 = w - 1, k1 = K0 + d - 1;
        var rooms = new int[storeys];
        var stairs = new int[storeys - 1];
        Rgb paint = Facades[(lot.Style * 3 + w) % Facades.Length];
        var face = new Stuff2(f.Wall.M, paint);
        for (int s = 0; s < storeys; s++) p.Shell(i0, K0, i1, k1, s * Pitch, H, face, f.Roof);
        for (int s = 0; s + 1 < storeys; s++)
            // Up the depth of the house, a floor along one side wall and the next along the other, floor at both ends of each.
            stairs[s] = s % 2 == 0 ? p.Flight(1, K0 + 2, Dir.South, s * Pitch, Pitch, 2, "stair", s) : p.Flight(i1 - 2, k1 - 2, Dir.North, s * Pitch, Pitch, 2, "stair", s);
        for (int s = 0; s < storeys; s++)
        {
            int j = s * Pitch;
            if (s > 0) p.Level(s, j, $"floor {s + 1}");
            rooms[s] = p.Room(ModuleKind.Room, s == 0 ? "front room" : "bedroom", s, i0 + 1, j, K0 + 1, i1 - 1, j + H - 1, k1 - 1);
            for (int i = i0 + 1; i < i1; i += 2)
            {
                if (s == 0 && i >= w / 2 - 1 && i <= w / 2) continue;
                p.Glazed(i, j + 1, K0, i, K0);
                p.Glazed(i, j + 1, k1, i, k1);
            }
            p.Lamp(2 * (i1 - 1), 2 * j + 3, 2 * (K0 + 1), rooms[s], 1f, 9f); // the front corner away from the stairs
        }
        for (int s = 0; s + 1 < storeys; s++)
        {
            p.C.Link(rooms[s], stairs[s]);
            p.C.Link(stairs[s], rooms[s + 1]);
        }
        Front(p, i0, i1, rooms[0]);
        p.C.VaultModule = rooms[^1];
        int roofJ = storeys * Pitch - 1;
        // The gable runs along the street, so the fronts are eaves, as a terrace's are.
        p.Gable(i0, K0, i1, k1, roofJ + 1, true, f.Roof, face);
        p.Weathers(i0 - 1, 0, K0 - 1, i1 + 1, roofJ + d / 2 + 2, k1 + 1, roofJ);
        Broken(p, lot, i0, K0, i1, k1, roofJ);
    }

    /// <summary>A tall works shed under a gable: its plant standing in rows inside, a wide door to the yard.</summary>
    static void Works(Plot p, CityLot lot)
    {
        Fab f = p.F;
        int w = Math.Max(12, lot.W), d = Math.Max(12, lot.D), h = 4;
        int i0 = 0, i1 = w - 1, k1 = K0 + d - 1;
        int hall = p.Hall("works floor", ModuleKind.Hall, 0, i0, K0, i1, k1, 0, h, f.Wall, f.Roof);
        for (int i = i0 + 3; i + 2 < i1 - 1; i += 5)
            p.Machine(i, 0, k1 - 4, i + 2, k1 - 2, 2, i % 2 == 0 ? f.Steel : f.Panel); // along the back wall, the floor open
        for (int i = i0 + 2; i < i1 - 1; i += 4)
        {
            p.Glazed(i, h - 1, K0, i + 1, K0);
            p.Glazed(i, h - 1, k1, i + 1, k1);
        }
        p.Lamp(2 * (i0 + 4), 2 * h - 1, 2 * (K0 + 4), hall, 1.4f, 16f);
        Front(p, i0, i1, hall);
        p.C.VaultModule = hall;
        p.Gable(i0, K0, i1, k1, h, d > w, f.Roof, f.Wall);
        Broken(p, lot, i0, K0, i1, k1, h);
    }

    /// <summary>A civic hall: a square of tall walls, a colonnade along its front, an eight-sided dome of slopes on a drum.</summary>
    static void CivicHall(Plot p, CityLot lot)
    {
        Fab f = p.F;
        int w = Math.Max(16, lot.W), d = Math.Max(16, lot.D), h = 5;
        int i0 = 0, i1 = w - 1, k1 = K0 + d - 1;
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
        p.Approach(mid, 0, Dir.North);
        p.Mouth(mid - 1, 0, K0, mid, 1, K0, p.Yard, porch);
        p.Mouth(mid - 1, 0, K0 + 3, mid, 2, K0 + 3, porch, hall);
        p.C.VaultModule = hall;
        p.Weathers(i0 - 1, 0, K0 - 1, i1 + 1, h + 4 + (int)r, k1 + 1, h);
    }
}
