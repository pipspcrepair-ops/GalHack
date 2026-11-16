using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Structures;
using PoiGenerator.Gen;
using Block = GalHack.Structures.Block;
using BlockType = GalHack.Structures.BlockType;
using Building = GalHack.Structures.Building;
using Dir = GalHack.Structures.Dir;
using Int3 = GalHack.Structures.Int3;

namespace Slice.Places;

/// <summary>
/// The decks the hull's plan fitted inside its form (poi-generator's ShipDecks), built: this piece's share of every
/// space, the walls and slabs round them, the halls open two decks tall with their catwalks, the flights of stairs, the
/// doors, the ways out through the shell (and the crash's breaches), the windows.
/// </summary>
public sealed partial class HullVessel
{
    ShipDecks I => H.Inside;
    int Levels => I.Levels;
    int Walk(int d) => I.Walk(d);

    /// <summary>Each of the plan's spaces as this piece's (-1: none of it lies here).</summary>
    int[] _space = Array.Empty<int>();
    int _vault = -1;

    /// <summary>The piece's space a deck's cell belongs to, or <see cref="ShipDecks.Fill"/> (inside, no space's) or <see cref="ShipDecks.None"/>.</summary>
    int ZoneAt(int d, int u, int v) => I.Zone(d, u, v) is int z && z >= 0 ? (InPiece(v) && _space[z] >= 0 ? _space[z] : ShipDecks.Fill) : I.Zone(d, u, v);

    /// <summary>Is there a space (any piece's) at a deck's cell, or in the eight round it?</summary>
    bool Near(int d, int u, int v)
    {
        if (d < 0 || d >= Levels) return false;
        for (int dv = -1; dv <= 1; dv++)
            for (int du = -1; du <= 1; du++)
                if (I.Zone(d, u + du, v + dv) >= 0) return true;
        return false;
    }

    /// <summary>The plan's spaces, each cut to the piece.</summary>
    void Spaces()
    {
        _space = Enumerable.Repeat(-1, I.Rooms.Count).ToArray();
        for (int i = 0; i < I.Rooms.Count; i++)
        {
            ShipDecks.Room r = I.Rooms[i];
            var floor = r.Floor.Where(c => InPiece(c.V)).ToList();
            if (floor.Count == 0) continue;
            var s = new Space { Purpose = r.Purpose, Deck = r.Deck, Y = r.Y, Height = r.Height, Corridor = r.Corridor, Hall = r.Hall };
            s.Floor.UnionWith(floor);
            s.Doors.AddRange(r.Doors);
            s.Taken.UnionWith(r.Stair.Where(c => InPiece(c.V)));
            _space[i] = Out.Spaces.Count;
            Out.Spaces.Add(s);
        }
        _vault = I.CoreRoom >= 0 ? _space[I.CoreRoom] : -1;
    }

    /// <summary>
    /// The walls and slabs: every cell of a deck that is no space's but stands beside one is solid to its ceiling, and
    /// the slab between two decks runs under and over every space and its walls; the halls are then opened two decks
    /// tall, but for their catwalks' deck and a lip along them. What lies further from any space (the machinery's
    /// bulk, the bilges) is left as the form's own void.
    /// </summary>
    void Walls()
    {
        ushort wall = Paint["wall"], deck = Paint["deck"];
        for (int v = Pc.V0; v < VEnd; v++)
            for (int u = _u0; u < _u0 + _nu; u++)
            {
                for (int d = 0; d < Levels; d++)
                {
                    if (I.Zone(d, u, v) >= 0 || !Near(d, u, v)) continue;
                    for (int y = Walk(d); y < Walk(d) + 4; y++)
                        if (Interior(u, y, v)) Solid(u, y, v, wall);
                }
                for (int k = 0; k <= Levels; k++)
                {
                    int y = Walk(k) - 1;
                    if (Interior(u, y, v) && (Near(k, u, v) || Near(k - 1, u, v))) Solid(u, y, v, deck);
                }
            }
        ShapeDef half = Cat.Shape("half");
        for (int i = 0; i < I.Rooms.Count; i++)
        {
            ShipDecks.Room h = I.Rooms[i];
            if (!h.Hall) continue;
            int up = Walk(h.Deck + 1) - 1;
            foreach (var (u, v) in h.Floor)
            {
                if (!InPiece(v)) continue;
                bool catwalk = I.Zone(h.Deck + 1, u, v) != i;
                for (int y = Walk(h.Deck); y < Walk(h.Deck) + h.Height; y++)
                    if (!(catwalk && y >= up)) Clear(u, y, v);
                // A lip along the catwalk's edge.
                if (!catwalk && Four.Any(o => I.Zone(h.Deck + 1, u + o.U, v + o.V) is int z && z >= 0 && z != i && h.Floor.Contains((u + o.U, v + o.V))))
                    Put(u, up, v, Armour(Plating, half, Orientations.Identity, Paint["hazard"]));
            }
        }
    }

    static readonly (int U, int V)[] Four = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    static Dir DirOf(int du, int dv) => du > 0 ? Dir.PosX : du < 0 ? Dir.NegX : dv > 0 ? Dir.PosZ : Dir.NegZ;

    /// <summary>Each flight of stairs: five steps up from its landing, solid under them, the deck over it open.</summary>
    void Stairs()
    {
        foreach (ShipDecks.Flight f in I.Flights)
        {
            var (pu, pv) = f.DV != 0 ? (1, 0) : (0, 1);
            Dir up = DirOf(f.DU, f.DV);
            for (int lane = 0; lane < f.Width; lane++)
                for (int j = 1; j <= 5; j++)
                {
                    int u = f.U + f.DU * j + pu * lane, v = f.V + f.DV * j + pv * lane, y = Walk(f.Deck) + j - 1;
                    if (!InPiece(v)) continue;
                    if (j <= 4) Clear(u, Walk(f.Deck + 1) - 1, v);
                    for (int below = Walk(f.Deck); below < y; below++) Solid(u, below, v, Paint["wall"]);
                    D.Stairs(At(u, y, v), up, Paint["deck"]);
                }
        }
    }

    /// <summary>The plan's doors, every one opening by hand: 1, 2 or 3 m wide.</summary>
    void Doors()
    {
        foreach (ShipDecks.DoorPlan p in I.Doors)
        {
            if (!InPiece(p.V) || !InPiece(p.DV == 0 ? p.V + p.Width - 1 : p.V)) continue;
            BlockType t = Cat.Type(p.Width >= 3 ? "door-3x3x1" : p.Width == 2 ? "door-2x2x1" : "door-1x2x1");
            Device(t, p.U, Walk(p.Deck), p.V, Turn(Dir.PosY, DirOf(p.DU, p.DV)));
        }
    }

    /// <summary>
    /// The ways out through the shell: the hull door and the hatches (a door in the wall, the shell cut through behind
    /// it, a sill underfoot), the cargo opening open; then the crash's breaches torn through a flank where a space
    /// reaches (the place's ramps lead up to them).
    /// </summary>
    void Ways()
    {
        foreach (ShipDecks.Opening o in I.Openings)
        {
            var (pu, pv) = o.DV != 0 ? (1, 0) : (0, 1);
            int y0 = Walk(o.Deck);
            bool door = o.What != "cargo";
            for (int n = door ? 2 : 1; n <= o.Depth; n++)
                for (int k = 0; k < o.Width; k++)
                    Cut(o.U + o.DU * n + pu * k, y0, o.V + o.DV * n + pv * k, o.Height);
            int du = o.U + o.DU, dv = o.V + o.DV;
            if (!door || !InPiece(dv)) continue;
            Int3 anchor = Device(Cat.Type(o.Width >= 2 ? "door-2x2x1" : "door-1x2x1"), du, y0, dv, Turn(Dir.PosY, DirOf(o.DU, o.DV)));
            if (o.What == "door") Out.Door ??= anchor;
        }
        Bulkheads();
        foreach (CapitalHull.Way w in H.Ways.Where(w => w.Side != 0))
            for (int v = w.V0; v < w.V0 + w.Width; v++)
            {
                if (!InPiece(v)) continue;
                // From the lowest deck a space reaches at v, out from its outermost cell on that side.
                for (int d = 0; d < Levels; d++)
                {
                    var row = Enumerable.Range(_u0, _nu).Where(u => I.Zone(d, u, v) >= 0).ToList();
                    if (row.Count == 0) continue;
                    int from = w.Side > 0 ? row.Max() : row.Min();
                    for (int u = from + w.Side; Math.Abs(u) <= H.ExtentU + 1; u += w.Side) Cut(u, Walk(d), v, Math.Min(w.Height, 4));
                    break;
                }
            }
    }

    /// <summary>
    /// Where the hull is built in pieces (it broke apart), the frame it parted at is a bulkhead: every space that runs on
    /// past the piece's end has a door in it there (1 to 3 m wide, as many as its width takes), what the break tore open.
    /// </summary>
    void Bulkheads()
    {
        foreach (int v in new[] { Pc.V0 > 0 ? Pc.V0 : -1, !Pc.Last ? VEnd - 1 : -1 })
        {
            if (v < 0) continue;
            int beyond = v == Pc.V0 ? v - 1 : v + 1;
            for (int d = 0; d < Levels; d++)
                for (int u = _u0; u < _u0 + _nu;)
                {
                    int z = I.Zone(d, u, v);
                    if (z < 0 || I.Zone(d, u, beyond) != z || I.Upper(d, z)) { u++; continue; }
                    int w = 0;
                    while (w < 3 && I.Zone(d, u + w, v) == z && I.Zone(d, u + w, beyond) == z) w++;
                    Device(Cat.Type(w >= 3 ? "door-3x3x1" : w == 2 ? "door-2x2x1" : "door-1x2x1"), u, Walk(d), v, Turn(Dir.PosY, Dir.PosZ));
                    u += w;
                }
        }
    }

    /// <summary>A way's cell cut open to its height, a sill under it.</summary>
    void Cut(int u, int y0, int v, int height)
    {
        if (!InPiece(v)) return;
        for (int y = y0; y < y0 + height; y++) Clear(u, y, v);
        if (!Has(u, y0 - 1, v)) Solid(u, y0 - 1, v, Paint["hazard"]);
    }

    /// <summary>Windows: panes 2 m tall every few metres where a corridor or a lived-in room meets the outside through no more than its wall and the shell.</summary>
    void Windows()
    {
        foreach (Space s in Live)
        {
            if (s.Purpose is "reactor" or "generator-room" or "core-room" or "stores" or "magazine" or "engine-room" or "stair") continue;
            int y = s.Y + 1;
            foreach (var (u, v) in s.Floor)
                foreach (var (du, dv) in Four)
                {
                    if (s.Floor.Contains((u + du, v + dv)) || Mod(du != 0 ? v : u, 6) > 1) continue;
                    int n = 1;
                    while (n <= 3 && Has(u + du * n, y, v + dv * n) && Has(u + du * n, y + 1, v + dv * n)) n++;
                    if (n == 1 || n > 3 || Mask(u + du * n, y, v + dv * n) != 0 || Mask(u + du * n, y + 1, v + dv * n) != 0) continue;
                    for (int k = 1; k < n; k++)
                        for (int dy = 0; dy < 2; dy++)
                            if (S.TryGet(At(u + du * k, y + dy, v + dv * k), out Block b) && S.TypeOf(b) == Plating) Glaze(u + du * k, y + dy, v + dv * k, b);
                }
        }
    }

    void Glaze(int u, int y, int v, Block was) => Put(u, y, v, Building.Make(S, Glass, S.ShapeOf(was), was.Rot));
}
