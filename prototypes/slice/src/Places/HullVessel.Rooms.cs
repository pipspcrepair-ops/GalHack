using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Structures;
using PoiGenerator.Gen;
using Block = GalHack.Structures.Block;
using BlockType = GalHack.Structures.BlockType;
using Dirs = GalHack.Structures.Dirs;
using Dir = GalHack.Structures.Dir;
using Plan = PoiGenerator.Gen.CapitalHull.HullPlan;
using Bay = PoiGenerator.Gen.CapitalHull.Bay;

namespace Slice.Places;

/// <summary>
/// The floor plan, deck by deck, as a ship's is drawn: a spine corridor down the middle, a passage down each flank,
/// a cross passage at the head of every bay, the halls two decks tall, a stair core in the crew bay and the power
/// bay, and the rooms each bay holds on each deck between them, walled from one another and the corridors.
/// </summary>
public sealed partial class HullVessel
{
    /// <summary>Walking levels: the hull's decks.</summary>
    int Levels => H.Decks;

    int[] _zone;
    const int Fill = -1, None = -2;
    readonly List<(Bay Bay, int S, int Top)> _cores = new();

    int Z(int d, int u, int v) => d + Levels * (u - _u0 + _nu * (v - Pc.V0));

    /// <summary>The space a deck's cell belongs to (or <see cref="Fill"/>: solid, <see cref="None"/>: outside the deck).</summary>
    int ZoneAt(int d, int u, int v) => u < _u0 || u >= _u0 + _nu || !InPiece(v) || d < 0 || d >= Levels ? None : _zone[Z(d, u, v)];

    void SetZone(int d, int u, int v, int space)
    {
        if (ZoneAt(d, u, v) == None) return;
        _zone[Z(d, u, v)] = space;
        if (space >= 0) Out.Spaces[space].Floor.Add((u, v));
    }

    int NewSpace(string purpose, int deck, bool corridor = false, bool hall = false)
    {
        Out.Spaces.Add(new Space { Purpose = purpose, Deck = deck, Y = Plan.Walk(deck), Corridor = corridor, Hall = hall, Height = hall ? Plan.Pitch + 4 : 4 });
        return Out.Spaces.Count - 1;
    }

    /// <summary>The widest a deck runs at v: the u either side to which its every cell is inside the shell.</summary>
    int Reach(int d, int v)
    {
        int r = -1;
        while (ZoneAt(d, r + 1, v) != None && ZoneAt(d, -(r + 1), v) != None) r++;
        return r;
    }

    void Spaces()
    {
        _zone = new int[Levels * _nu * _nv];
        for (int v = Pc.V0; v < VEnd; v++)
            for (int u = _u0; u < _u0 + _nu; u++)
                for (int d = 0; d < Levels; d++)
                    _zone[Z(d, u, v)] = v < H.L && Open(u, d, v) && H.Body(u, Plan.Walk(d) + 2, v) ? Fill : None;
        // The stair cores first: they run the height of the hull.
        foreach (Bay b in H.Bays.Where(b => b.Kind is "crew" or "power"))
        {
            int s = b.V0 + 4, top = H.Decks - 1;
            // Its own cells wholly inside the hull on every deck it serves: under a sloping face its wall is the shaped skin
            // itself, so a narrow hull keeps its stairs to the top.
            bool Inside(int d, int u, int v)
            {
                if (!InPiece(v) || v >= H.L || d >= H.Decks) return false;
                for (int y = Plan.Walk(d); y < Plan.Walk(d) + 4; y++)
                    if (Mask(u, y, v) != 255) return false;
                return true;
            }
            if (!InPiece(s - 2) || !InPiece(s + 6) || Enumerable.Range(0, top + 1).Any(d => Enumerable.Range(s - 1, 7).Any(v => Enumerable.Range(3, 5).Any(u => !Inside(d, u, v))))) continue;
            _cores.Add((b, s, top));
            for (int d = 0; d <= top; d++)
            {
                int core = NewSpace("stair", d, corridor: true);
                for (int v = s - 1; v <= s + 5; v++)
                    for (int u = 3; u <= 7; u++)
                    {
                        if (u == 5 && v > s - 1 && v < s + 5) continue;
                        if (ZoneAt(d, u, v) == None)
                        {
                            _zone[Z(d, u, v)] = Fill;
                            for (int y = Plan.Walk(d); y < Plan.Walk(d) + 4; y++) Clear(u, y, v);
                        }
                        SetZone(d, u, v, core);
                    }
            }
        }
        for (int d = 0; d < H.Decks; d++)
        {
            int corridor = NewSpace("corridor", d, corridor: true);
            foreach (Bay b in H.Bays)
            {
                if (b.V1 <= Pc.V0 || b.V0 >= Pc.V1) continue;
                bool hall = b.Hall && d <= 1;
                // The cross passage at the bay's head, and the spine and flank passages down it (a hall's floor is its own).
                for (int v = Math.Max(b.V0, Pc.V0); v < Math.Min(b.V1, Pc.V1); v++)
                {
                    int r = Reach(d, v);
                    bool cross = v - b.V0 < 2 && b.Kind != "bow";
                    for (int u = -r; u <= r; u++)
                    {
                        if (ZoneAt(d, u, v) != Fill) continue;
                        bool spine = Math.Abs(u) <= 1 && !hall, side = b.Kind != "bow" && !hall && r >= 10 && Math.Abs(u) >= r - 2;
                        if (cross || spine || side) SetZone(d, u, v, corridor);
                    }
                }
                if (hall && d == 0) Hall(b);
                else if (hall) Catwalk(b, corridor);
                else Rooms(b, d, corridor);
            }
        }
    }

    /// <summary>Is (u, v) in a stair core, or within <paramref name="wall"/> cells of one (its walls)?</summary>
    bool InCore(int u, int v, int wall) => _cores.Any(c => u >= 3 - wall && u <= 7 + wall && v >= c.S - 1 - wall && v <= c.S + 5 + wall);

    /// <summary>A hall: the bay's two lower decks as one space, 9 m clear, bulkheads at its ends.</summary>
    void Hall(Bay b)
    {
        string purpose = b.Kind switch { "engine" => "engine-room", "promenade" => "promenade", "ore-hold" => "ore-hold", "refinery" => "refinery", "hangar" => "hangar", _ => "hold" };
        // (A stub of a hall at a piece's end, too short to have its door, stays solid.)
        if (Math.Min(b.V1 - 2, Pc.V1 - 3) - Math.Max(b.V0 + 3, Pc.V0) < 3) return;
        int hall = NewSpace(purpose, 0, hall: true);
        for (int v = Math.Max(b.V0 + 3, Pc.V0); v <= Math.Min(b.V1 - 2, Pc.V1 - 1); v++)
        {
            int r = Reach(0, v);
            for (int u = -r; u <= r; u++)
                if (ZoneAt(0, u, v) == Fill && ZoneAt(1, u, v) == Fill)
                {
                    SetZone(0, u, v, hall);
                    _zone[Z(1, u, v)] = hall;
                }
        }
    }

    /// <summary>The walkways through a hall's upper half on deck 1: the spine as a catwalk 3 m wide, galleries along its sides.</summary>
    void Catwalk(Bay b, int corridor)
    {
        for (int v = Math.Max(b.V0 + 3, Pc.V0); v <= Math.Min(b.V1 - 2, Pc.V1 - 1); v++)
        {
            if (ZoneAt(0, 0, v) is not (int h and >= 0) || !Out.Spaces[h].Hall) continue;
            int r = Reach(0, v);
            // The catwalk down the spine, and a gallery 2 m wide along each of the hall's sides.
            for (int u = -r; u <= r; u++)
                if ((Math.Abs(u) <= 1 || Math.Abs(u) >= r - 1) && ZoneAt(1, u, v) == h) SetZone(1, u, v, corridor);
        }
    }

    /// <summary>What a hall's cell has on deck 1: open (0), a walkway's deck (1), or a walkway's kerb on its deck beside it (2).</summary>
    int Walkway(int u, int v)
    {
        bool On(int x) => ZoneAt(1, x, v) is int z and >= 0 && Out.Spaces[z].Corridor;
        return On(u) ? 1 : On(u - 1) || On(u + 1) ? 2 : 0;
    }

    /// <summary>What a bay holds on a deck, port and starboard, and how many rooms a side.</summary>
    /// <remarks>
    /// A deck given over to machinery (null) is solid but for its corridors: the deck over a hall (its cargo gear and
    /// trunking), the decks over the engines, the power bay's middle decks, and up every bay every second deck between the
    /// lowest and the top (the services between the lived decks: trunking, life support, the ducts). Corridors dominate a
    /// ship's plan (Empyrion's derelicts 59% of the floor): most of a hull is the machinery and the ways round it.
    /// </remarks>
    (string Port, string Stbd, int N) Program(string bay, int d)
    {
        bool top = d == H.Decks - 1;
        if (d > 0 && d % 2 == 0 && !top) return (null, null, 0);
        return (bay, d) switch
        {
            ("hold" or "ore-hold" or "hangar" or "refinery" or "promenade", 2) or ("engine", >= 2) => (null, null, 0),
            ("power", >= 2) when !top => (null, null, 0),
            ("bow", 0) => ("hold", "hold", 1),
            ("bow", 1) => ("stores", "stores", 1),
            ("bow", _) when top => ("sensor-room", "stores", 1),
            ("bow", _) => ("crew-quarters", "crew-quarters", 1),
            ("crew", 0) => ("workshop", "stores", 1),
            ("crew", _) when top => ("galley", "medbay", 1),
            ("crew", _) => ("crew-quarters", "crew-quarters", 2),
            ("power", 0) => ("reactor", "generator-room", 1),
            ("power", 1) => ("generator-room", "workshop", 1),
            ("power", _) when top => ("crew-quarters", "stores", 1),
            ("power", _) => ("stores", "crew-quarters", 1),
            ("engine", _) => ("generator-room", "stores", 1),
            ("hangar", _) => ("workshop", "crew-quarters", 2),
            ("armoury", 0) => ("armoury", "armoury", 1),
            ("armoury", _) or ("barracks", _) => ("barracks", "barracks", 2),
            ("magazine", 0) => ("magazine", "magazine", 1),
            ("magazine", 1) => ("armoury", "stores", 1),
            ("magazine", _) => ("barracks", "barracks", 1),
            ("cabins", _) when top => ("galley", "cabin", 2),
            ("cabins", _) or ("promenade", _) => ("cabin", "cabin", 3),
            ("med", 0) => ("medbay", "medbay", 1),
            ("med", _) => ("medbay", "stores", 1),
            _ => ("stores", "crew-quarters", 2),
        };
    }

    /// <summary>
    /// A bay's rooms on a deck: each side's band between the spine and the flank passage, split along the bay into rooms
    /// of about 8 m (rooms of about 88 m², Empyrion's median) with a passage between each two (walled both sides), as a ship's transverse passages join its
    /// spine to its flanks.
    /// </summary>
    void Rooms(Bay b, int d, int corridor)
    {
        var (port, stbd, n0) = Program(b.Kind, d);
        int c0 = Math.Max(b.V0 + (b.Kind == "bow" ? 0 : 3), Pc.V0), c1 = Math.Min(b.V1 - 2, Pc.V1 - 1);
        bool passages = b.Kind != "bow";
        foreach (int side in new[] { -1, 1 })
        {
            int from = c0;
            var core = _cores.FirstOrDefault(c => c.Bay == b && d <= c.Top);
            if (side > 0 && core.Bay != null) from = core.S + 7;
            if (c1 - from < 4) continue;
            string purpose = side < 0 ? port : stbd;
            if (purpose == null)
            {
                Crawlway(d, side, from, c1);
                continue;
            }
            if (purpose == "reactor")
            {
                // The reactor, and behind it the core room, reached only through it.
                Band(purpose, d, side, from, c1 - 8, passages);
                if (Band("core-room", d, side, c1 - 6, c1, passages) is int vault) _vault = vault;
                continue;
            }
            int total = c1 - from + 1, n = Math.Max(n0, (int)MathF.Round((total + 4) / 12f));
            int len = (total - 4 * (n - 1)) / n;
            if (len < 6) { n = 1; len = total; }
            for (int i = 0; i < n; i++)
            {
                int a = from + i * (len + 4), z = i == n - 1 ? c1 : a + len - 1;
                // A deep band takes a passage down its middle, a room either side of it: from 12 m, or 10 m for the crew's
                // berths (cabins 3 m deep, a bunk along the wall).
                int outer = Reach(d, (a + z) / 2) - (passages ? 4 : 0), depth = outer - 2, m = 4 + (depth - 4) / 2;
                if (passages && depth >= (purpose is "crew-quarters" or "barracks" or "cabin" ? 10 : 12))
                {
                    Band(purpose, d, side, a, z, passages, 3, m - 2);
                    Band(purpose, d, side, a, z, passages, m + 3, outer);
                    for (int v = a; v <= z; v++)
                        for (int u = m; u <= m + 1; u++)
                            if (ZoneAt(d, side * u, v) == Fill && !InCore(side * u, v, 1)) SetZone(d, side * u, v, corridor);
                }
                else Band(purpose, d, side, a, z, passages);
                if (i == n - 1) continue;
                // The passage after it: two cells wide from the spine's wall out to the flank passage.
                for (int v = a + len + 1; v <= a + len + 2; v++)
                    for (int u = 2; u <= Reach(d, v); u++)
                        if (ZoneAt(d, side * u, v) == Fill && !InCore(side * u, v, 1)) SetZone(d, side * u, v, corridor);
            }
        }
    }

    int _vault = -1;

    /// <summary>
    /// A crawlway through a machinery deck's side: a duct 1 m wide and 2 m high down the middle of the machinery, through
    /// the bulkheads at its ends into the cross passages (place-patterns S1: 2 m only in crawlways, never on the main way).
    /// Unlit: where the ship's nesters go (with the holds and the engine room).
    /// </summary>
    void Crawlway(int d, int side, int v0, int v1)
    {
        int r = Reach(d, (v0 + v1) / 2), u = side * ((3 + (r >= 10 ? r - 4 : r)) / 2);
        bool Way(int v) => ZoneAt(d, u, v) is int z and >= 0 && Out.Spaces[z].Corridor;
        var run = Enumerable.Range(v0 - 1, v1 - v0 + 3).ToList();
        if (Math.Abs(u) < 3 || run.Any(v => ZoneAt(d, u, v) != Fill || InCore(u, v, 1)) || !Way(v0 - 2) && !Way(v1 + 2)) return;
        int duct = NewSpace("crawlway", d, corridor: true);
        Out.Spaces[duct].Height = 2;
        foreach (int v in run) SetZone(d, u, v, duct);
    }

    /// <summary>One room on a side of a deck over v0..v1: its cells from the spine's wall out to the flank passage's (or the shell).</summary>
    int? Band(string purpose, int d, int side, int v0, int v1, bool passages, int inner = 3, int limit = int.MaxValue)
    {
        var cells = new List<(int, int)>();
        for (int v = v0; v <= v1; v++)
        {
            int r = Reach(d, v), outer = Math.Min(limit, r >= 10 && passages ? r - 4 : r);
            for (int a = inner; a <= outer; a++)
                if (ZoneAt(d, side * a, v) == Fill && !InCore(side * a, v, 1)) cells.Add((side * a, v));
        }
        if (cells.Count < 12) return null;
        int room = NewSpace(purpose, d);
        foreach (var (u, v) in cells) SetZone(d, u, v, room);
        return room;
    }

    // ---- building what the plan says ------------------------------------------------------------------

    /// <summary>
    /// The walls: every cell of a deck that is no space's is solid. A hall is open from its floor to the deck over its
    /// upper half, but for its walkways' deck (the catwalk down the spine, the galleries along its sides) and a kerb along their edges.
    /// </summary>
    void Walls()
    {
        ShapeDef half = Cat.Shape("half");
        for (int v = Pc.V0; v < VEnd; v++)
            for (int u = _u0; u < _u0 + _nu; u++)
                for (int d = 0; d < Levels; d++)
                {
                    int z = _zone[Z(d, u, v)];
                    if (z >= 0 && Out.Spaces[z].Hall && d == 1) continue;
                    int y0 = Plan.Walk(d);
                    // No space's, or a deck's edge the shell pinches (the bilges, the bow, the nacelles): solid. Over a crawlway, its machinery.
                    int open = z < 0 ? 0 : Out.Spaces[z].Hall ? 4 : Out.Spaces[z].Height;
                    for (int y = y0 + open; y < y0 + 4; y++)
                        if (z != None || Interior(u, y, v)) Solid(u, y, v, Paint["wall"]);
                    if (z < 0 || !Out.Spaces[z].Hall) continue;
                    int walk = Walkway(u, v);
                    for (int y = y0; y < y0 + Out.Spaces[z].Height; y++)
                        if (!(walk > 0 && y == Plan.Pitch)) Clear(u, y, v);
                    if (walk == 2) Put(u, Plan.Walk(1), v, Armour(Plating, half, Orientations.Identity, Paint["hazard"]));
                }
    }
}
