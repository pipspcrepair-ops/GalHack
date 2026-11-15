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
    /// <summary>Walking levels: the hull's decks, then the tower's two.</summary>
    int Levels => H.Decks + Plan.TowerDecks;

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
                    _zone[Z(d, u, v)] = v < H.L && (d < H.Decks || InTower(u, v)) && Open(u, d, v) ? Fill : None;
        // The stair cores first: they run the height of the hull, and up through the tower over their bay.
        foreach (Bay b in H.Bays.Where(b => b.Kind is "crew" or "power"))
        {
            int s = b.V0 + 4, top = InTower(4, s) ? Levels - 1 : H.Decks - 1;
            if (!InPiece(s - 2) || !InPiece(s + 6) || Enumerable.Range(0, top + 1).Any(d => Enumerable.Range(s - 2, 9).Any(v => ZoneAt(d, 8, v) == None))) continue;
            _cores.Add((b, s, top));
            for (int d = 0; d <= top; d++)
            {
                int core = NewSpace("stair", d, corridor: true);
                for (int v = s - 1; v <= s + 5; v++)
                    for (int u = 3; u <= 7; u++)
                        if (u != 5 || v == s - 1 || v == s + 5) SetZone(d, u, v, core);
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
                        bool spine = Math.Abs(u) <= 1 && !hall, side = b.Kind != "bow" && !hall && r >= 9 && Math.Abs(u) >= r - 1;
                        if (cross || spine || side) SetZone(d, u, v, corridor);
                    }
                }
                if (hall && d == 0) Hall(b);
                else if (hall) Catwalk(b, corridor);
                else Rooms(b, d, corridor);
            }
        }
        // The tower: the officers' deck, the bridge over it.
        foreach (var (d, purpose) in new[] { (H.Decks, "officers"), (H.Decks + 1, "bridge") })
        {
            int room = NewSpace(purpose, d);
            for (int v = H.TV0; v <= H.TV1; v++)
                for (int u = H.TU0; u <= H.TU1; u++)
                    if (ZoneAt(d, u, v) == Fill && !InCore(u, v, 1)) SetZone(d, u, v, room);
        }
    }

    /// <summary>Is (u, v) in a stair core, or within <paramref name="wall"/> cells of one (its walls)?</summary>
    bool InCore(int u, int v, int wall) => _cores.Any(c => u >= 3 - wall && u <= 7 + wall && v >= c.S - 1 - wall && v <= c.S + 5 + wall);

    /// <summary>A hall: the bay's two lower decks as one space, 9 m clear, bulkheads at its ends.</summary>
    void Hall(Bay b)
    {
        string purpose = b.Kind switch { "engine" => "engine-room", "promenade" => "promenade", "ore-hold" => "ore-hold", "refinery" => "refinery", "hangar" => "hangar", _ => "hold" };
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

    /// <summary>The spine carried through a hall's upper half on deck 1: a catwalk 3 m wide on its own deck, kerbs either side.</summary>
    void Catwalk(Bay b, int corridor)
    {
        for (int v = Math.Max(b.V0 + 3, Pc.V0); v <= Math.Min(b.V1 - 2, Pc.V1 - 1); v++)
            for (int u = -1; u <= 1; u++)
                if (ZoneAt(1, u, v) is int z and >= 0 && Out.Spaces[z].Hall) SetZone(1, u, v, corridor);
    }

    bool OnCatwalk(int u, int v) => ZoneAt(1, Math.Clamp(u, -1, 1), v) is int z and >= 0 && Out.Spaces[z].Corridor && ZoneAt(0, u, v) is int h and >= 0 && Out.Spaces[h].Hall;

    /// <summary>What a bay holds on a deck, port and starboard, and how many rooms a side.</summary>
    (string Port, string Stbd, int N) Program(string bay, int d)
    {
        bool top = d == H.Decks - 1;
        return (bay, d) switch
        {
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
    /// of up to about 12 m with a passage between each two (walled both sides), as a ship's transverse passages join its
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
            if (purpose == "reactor")
            {
                // The reactor, and behind it the core room, reached only through it.
                Band(purpose, d, side, from, c1 - 8, passages);
                if (Band("core-room", d, side, c1 - 6, c1, passages) is int vault) _vault = vault;
                continue;
            }
            int total = c1 - from + 1, n = Math.Max(n0, (int)MathF.Round((total + 4) / 16f));
            int len = (total - 4 * (n - 1)) / n;
            if (len < 6) { n = 1; len = total; }
            for (int i = 0; i < n; i++)
            {
                int a = from + i * (len + 4);
                Band(purpose, d, side, a, i == n - 1 ? c1 : a + len - 1, passages);
                if (i == n - 1) continue;
                // The passage after it: two cells wide from the spine's wall out to the flank passage.
                for (int v = a + len + 1; v <= a + len + 2; v++)
                    for (int u = 2; u <= Reach(d, v); u++)
                        if (ZoneAt(d, side * u, v) == Fill && !InCore(side * u, v, 1)) SetZone(d, side * u, v, corridor);
            }
        }
    }

    int _vault = -1;

    /// <summary>One room on a side of a deck over v0..v1: its cells from the spine's wall out to the flank passage's (or the shell).</summary>
    int? Band(string purpose, int d, int side, int v0, int v1, bool passages)
    {
        var cells = new List<(int, int)>();
        for (int v = v0; v <= v1; v++)
        {
            int r = Reach(d, v), outer = r >= 9 && passages ? r - 3 : r;
            for (int a = 3; a <= outer; a++)
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
    /// upper half, but for the catwalk's deck down the spine and a kerb along each of its edges.
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
                    // No space's, or a deck's edge the shell pinches (the bilges, the bow, the nacelles): solid.
                    if (z < 0)
                        for (int y = y0; y < y0 + 4; y++)
                            if (z == Fill || Interior(u, y, v)) Solid(u, y, v, Paint["wall"]);
                    if (z < 0 || !Out.Spaces[z].Hall) continue;
                    bool walk = Math.Abs(u) <= 2 && OnCatwalk(u, v);
                    for (int y = y0; y < y0 + Out.Spaces[z].Height; y++)
                        if (!(walk && y == Plan.Pitch)) Clear(u, y, v);
                    if (walk && Math.Abs(u) == 2) Put(u, Plan.Walk(1), v, Armour(Plating, half, Orientations.Identity, Paint["hazard"]));
                }
    }
}
