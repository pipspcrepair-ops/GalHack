#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using NV3 = System.Numerics.Vector3;

namespace PoiGenerator.Gen;

/// <summary>
/// The decks fitted inside a ship's form, after it (docs/ship-forms.md rule 1: the form first, its rooms after, never the
/// other way round). Every 5 m up the form a deck wherever 4 m stands clear inside a habitable part of it; on each, every
/// open area of its own: its corridors run down it as the hull's roll has them (a spine, kept to one side, or twin
/// corridors joined at their ends into a ring), cross passages through it, rooms cut from what is left between them in the
/// hull's grain (Meridian's and the Kessari's all one length, a Reaver's anything), the halls two decks tall where the form
/// is deep enough (the engine room aft, the holds and hangars), flights of stairs between rooms one over another,
/// a door from every room, hatches out through the shell from every part nothing else joins (a Freeholder's modules on
/// their truss each have their own), and what each room is for: the bridge in the nose, a blister, a sponson or buried
/// deep as the faction keeps it, the engine room and reactor aft, the core room only through the reactor, the crew
/// amidships. Planned once a hull (every piece of a broken one builds its own stretch of the same plan).
/// </summary>
public sealed class ShipDecks
{
    public const int Pitch = 5;
    /// <summary>A deck's walking level: the first cell over its slab (the decks set as high as fits the most floor in the form).</summary>
    public int Walk(int d) => Base + Pitch * d + 1;
    /// <summary>How far the decks are raised off the form's bottom (0 to 4).</summary>
    public readonly int Base;
    public const int Fill = -1, None = -2;
    const int Way = -3;

    /// <summary>A space: a room, a hall two decks tall, a deck's corridors or a stair room, its floor cells (u, v) at its walking level.</summary>
    public sealed class Room
    {
        public string Purpose = "";
        public int Deck, Y, Height = 4;
        public bool Corridor, Hall;
        public readonly HashSet<(int U, int V)> Floor = new();
        /// <summary>Its doorways: the wall cell (the door's low corner), the cell inside it, its width.</summary>
        public readonly List<((int U, int V) Wall, (int U, int V) Inside, int Width)> Doors = new();
        /// <summary>Its floor a flight of stairs takes (the steps, the opening over them).</summary>
        public readonly HashSet<(int U, int V)> Stair = new();
        public float Uc => (float)Floor.Average(c => c.U);
        public float Vc => (float)Floor.Average(c => c.V);
        public int Area => Floor.Count;
    }

    /// <summary>A door: its low corner (a wall cell), the way through it (du, dv), its width along the wall, the rooms either side.</summary>
    public sealed record DoorPlan(int Deck, int U, int V, int DU, int DV, int Width, int A, int B);

    /// <summary>A flight of stairs up from a deck to the next: its landing's first cell, rising along (du, dv), two lanes wide (+u or +v across), or one where no more will go.</summary>
    public sealed record Flight(int Deck, int U, int V, int DU, int DV, int Width = 2);

    /// <summary>A way out through the shell: from a room's edge cell out along (du, dv), Width wide (+u or +v across) and Height high, its door in the first wall cell (Door), the shell cut through to the outside.</summary>
    public sealed record Opening(int Deck, int U, int V, int DU, int DV, int Width, int Height, int Depth, string What, int Room);

    readonly ShipForm F;
    readonly Faction _faction;
    readonly string _role;
    /// <summary>The stretch of the hull's program (its bays, stem to stern) at a v: bow, crew, hold, power, engine...</summary>
    readonly Func<float, string> _program;
    readonly Rng R;
    public readonly int U0, NU, NY, NV, Levels;
    readonly byte[] _mask;
    readonly bool[] _inner;
    int[] _zone = Array.Empty<int>();
    public readonly List<Room> Rooms = new();
    public readonly List<DoorPlan> Doors = new();
    public readonly List<Flight> Flights = new();
    public readonly List<Opening> Openings = new();
    /// <summary>The core room (its seat for a core), or -1.</summary>
    public int CoreRoom = -1;
    /// <summary>The hull's roll of its corridors and grain, for the sheets.</summary>
    public string Style = "";
    /// <summary>How many spaces and how much floor each step left (what a check prints when a hull comes out bare).</summary>
    public string Log = "";

    void Note(string step) => Log += $"{step}: {Rooms.Count(r => r.Area > 0)} spaces, {Rooms.Sum(r => r.Area)} m²; ";

    // ---- the hull's roll -------------------------------------------------------------------------------

    const int Spine = 1, Offset = 2, Twin = 3, Single = 4;
    readonly int _style, _cw, _xw, _every, _deep, _g0, _g1;
    readonly float _ends, _reroll;
    readonly bool _regular;
    readonly float[] _weights;

    ShipDecks(ShipForm form, Faction faction, string role, ulong seed, Func<float, string> program)
    {
        F = form;
        _faction = faction;
        _role = role;
        _program = program;
        R = new Rng(Hash.Combine(seed, Hash.Of("decks")));
        U0 = -F.ExtentU - 1;
        NU = 2 * F.ExtentU + 3;
        NY = F.Height + 1;
        NV = F.Length;
        _mask = new byte[NU * NY * NV];
        _inner = new bool[_mask.Length];
        Corners();
        // The decks' height off the bottom that gives the most floor (every other row along, for speed).
        int best = -1;
        for (int b = 0; b < Pitch; b++)
        {
            int n = 0;
            for (int y0 = b + 1; y0 + 4 < NY; y0 += Pitch)
                for (int v = 0; v < NV; v += 2)
                    for (int u = U0; u < U0 + NU; u++)
                        if (Interior(u, y0, v) && Interior(u, y0 + 3, v) && F.Hab(new NV3(u, y0 + 2, v))) n++;
            if (n > best) (best, Base) = (n, b);
        }
        Levels = Math.Max(1, (NY - 2 - Base) / Pitch);
        Faction f = faction;
        _weights = f switch
        {
            Faction.Meridian => new[] { 0.5f, 0.1f, 0.4f },
            Faction.Freeholders => new[] { 0.4f, 0.4f, 0.2f },
            Faction.Exchange => new[] { 0.3f, 0.3f, 0.4f },
            Faction.Reavers => new[] { 0.3f, 0.5f, 0.2f },
            Faction.Kessari => new[] { 0.8f, 0f, 0.2f },
            _ => new[] { 0.3f, 0.3f, 0.4f },
        };
        _style = 1 + R.PickWeighted(_weights);
        _regular = f is Faction.Meridian or Faction.Kessari;
        _cw = f == Faction.Meridian && R.Chance(0.35f) ? 4 : 3;
        _xw = R.Chance(0.3f) ? 2 : 3;
        _every = R.Between(5, 9);
        _deep = R.Between(9, 13);
        (_g0, _g1) = f switch
        {
            Faction.Meridian or Faction.Kessari => R.Between(6, 9) is int g ? (g, g) : (6, 6),
            Faction.Reavers => (4, 12),
            Faction.Freeholders => (4, 11),
            Faction.Vharn => (5, 12),
            _ => (5, 10),
        };
        _ends = R.Float(0.2f, 0.8f);
        _reroll = f == Faction.Kessari ? 0f : 0.3f;
        Style = $"{new[] { "", "spine", "offset", "twin" }[_style]} corridors {_cw} m, passages {_xw} m every {_every} m, rooms {_g0}-{_g1} m";
    }

    /// <summary>The decks of a hull's form, its faction's and role's, the program's stretch at each v (bow, crew, hold, power, engine...).</summary>
    public static ShipDecks Plan(ShipForm form, Faction faction, string role, ulong seed, Func<float, string> program)
    {
        var s = new ShipDecks(form, faction, role, seed, program);
        s.Open();
        s.Halls();
        for (int d = 0; d < s.Levels; d++) s.Deck(d);
        s.Corridors();
        s.Note("laid out");
        s.MakeDoors();
        s.Note("doors");
        s.Stairs();
        s.Exits();
        s.Note("ways out");
        s.Purposes();
        return s;
    }

    // ---- the envelope ---------------------------------------------------------------------------------

    int Index(int u, int y, int v)
    {
        int i = u - U0;
        return i < 0 || y < 0 || v < 0 || i >= NU || y >= NY || v >= NV ? -1 : i + NU * (y + NY * v);
    }

    /// <summary>A cell's corner mask: which of its corners lie inside the form (bit 0 +u, bit 1 +y, bit 2 +v).</summary>
    public int Mask(int u, int y, int v) => Index(u, y, v) is int i && i >= 0 ? _mask[i] : 0;

    /// <summary>Inside the shell: the cell and every one round it wholly the form's.</summary>
    public bool Interior(int u, int y, int v) => Index(u, y, v) is int i && i >= 0 && _inner[i];

    /// <summary>
    /// Every cell's corner mask over a form's box (u from -ExtentU - 1, y and v from 0; index u - u0 + nu * (y + ny * v)):
    /// which of its corners lie inside the form. Also what its outline is drawn from (any corner inside: the shell's cells).
    /// </summary>
    public static byte[] Masks(ShipForm form, out int u0, out int nu, out int ny, out int nv)
    {
        (u0, nu, ny, nv) = (-form.ExtentU - 1, 2 * form.ExtentU + 3, form.Height + 1, form.Length);
        int cu = nu + 1, cy = ny + 1, x0 = u0;
        var corner = new bool[cu * cy * (nv + 1)];
        for (int k = 0; k <= nv; k++)
            for (int j = 0; j < cy; j++)
                for (int i = 0; i < cu; i++)
                    corner[i + cu * (j + cy * k)] = form.Solid(new NV3(x0 + i - 0.5f, j - 0.5f, k - 0.5f));
        var mask = new byte[nu * ny * nv];
        for (int k = 0; k < nv; k++)
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nu; i++)
                {
                    int m = 0;
                    for (int c = 0; c < 8; c++)
                        if (corner[i + (c & 1) + cu * (j + (c >> 1 & 1) + cy * (k + (c >> 2 & 1)))]) m |= 1 << c;
                    mask[i + nu * (j + ny * k)] = (byte)m;
                }
        return mask;
    }

    void Corners()
    {
        Array.Copy(Masks(F, out _, out _, out _, out _), _mask, _mask.Length);
        var full = new bool[_mask.Length];
        for (int i = 0; i < full.Length; i++) full[i] = _mask[i] == 255;
        // Wholly inside with every neighbour: the full cells eroded by one along each axis in turn.
        bool[] Erode(bool[] a, int step, Func<int, bool> edge)
        {
            var b = new bool[a.Length];
            for (int i = 0; i < a.Length; i++) b[i] = a[i] && !edge(i) && a[i - step] && a[i + step];
            return b;
        }
        bool[] e = Erode(full, 1, i => i % NU == 0 || i % NU == NU - 1);
        e = Erode(e, NU, i => (i / NU) % NY == 0 || (i / NU) % NY == NY - 1);
        e = Erode(e, NU * NY, i => i / (NU * NY) == 0 || i / (NU * NY) == NV - 1);
        Array.Copy(e, _inner, e.Length);
    }

    // ---- zones ----------------------------------------------------------------------------------------

    int Z(int d, int u, int v) => d + Levels * (u - U0 + NU * v);

    /// <summary>The room a deck's cell belongs to, or <see cref="Fill"/> (open inside the form, no room's: a wall or the machinery) or <see cref="None"/> (no deck).</summary>
    public int Zone(int d, int u, int v) => u < U0 || u >= U0 + NU || v < 0 || v >= NV || d < 0 || d >= Levels ? None : _zone[Z(d, u, v)];

    void Set(int d, int u, int v, int z) => _zone[Z(d, u, v)] = z;

    /// <summary>Is this a hall's upper half (on the deck over its floor)?</summary>
    public bool Upper(int d, int z) => z >= 0 && Rooms[z].Hall && Rooms[z].Deck == d - 1;

    /// <summary>A deck's column inside a habitable part of the form, 4 m clear over its slab.</summary>
    bool Open(int d, int u, int v)
    {
        int y0 = Walk(d);
        if (y0 + 4 >= NY) return false;
        for (int y = y0; y < y0 + 4; y++)
            if (!Interior(u, y, v)) return false;
        return F.Hab(new NV3(u, y0 + 2, v));
    }

    void Open()
    {
        _zone = new int[Levels * NU * NV];
        for (int v = 0; v < NV; v++)
            for (int u = U0; u < U0 + NU; u++)
                for (int d = 0; d < Levels; d++)
                    Set(d, u, v, Open(d, u, v) ? Fill : None);
    }

    int AddRoom(string purpose, int d, IEnumerable<(int U, int V)> cells, bool hall = false, bool corridor = false)
    {
        var r = new Room { Purpose = purpose, Deck = d, Y = Walk(d), Hall = hall, Corridor = corridor, Height = hall ? Pitch + 4 : 4 };
        Rooms.Add(r);
        int id = Rooms.Count - 1;
        foreach (var c in cells)
        {
            r.Floor.Add(c);
            Set(d, c.U, c.V, id);
            if (hall) Set(d + 1, c.U, c.V, id);
        }
        return id;
    }

    static readonly (int U, int V)[] Four = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    /// <summary>The 4-connected parts of a set of cells.</summary>
    static List<List<(int U, int V)>> Parts(IEnumerable<(int U, int V)> cells)
    {
        var left = new HashSet<(int, int)>(cells);
        var parts = new List<List<(int U, int V)>>();
        var stack = new Stack<(int U, int V)>();
        while (left.Count > 0)
        {
            var start = left.First();
            left.Remove(start);
            var part = new List<(int U, int V)> { start };
            stack.Push(start);
            while (stack.Count > 0)
            {
                var (u, v) = stack.Pop();
                foreach (var (du, dv) in Four)
                    if (left.Remove((u + du, v + dv)))
                    {
                        part.Add((u + du, v + dv));
                        stack.Push((u + du, v + dv));
                    }
            }
            parts.Add(part);
        }
        return parts;
    }

    IEnumerable<(int U, int V)> Plane(Func<int, int, bool> ok)
    {
        for (int v = 0; v < NV; v++)
            for (int u = U0; u < U0 + NU; u++)
                if (ok(u, v)) yield return (u, v);
    }

    /// <summary>How wide a set of cells runs across its long axis, the median of its rows.</summary>
    static int Width(List<(int U, int V)> cells, bool alongV)
    {
        var rows = cells.GroupBy(c => alongV ? c.V : c.U).Select(g => g.Count()).OrderBy(n => n).ToList();
        return rows[rows.Count / 2];
    }

    static bool AlongV(List<(int U, int V)> cells) => cells.Max(c => c.V) - cells.Min(c => c.V) >= (cells.Max(c => c.U) - cells.Min(c => c.U)) * 0.8f;

    // ---- the halls ------------------------------------------------------------------------------------

    /// <summary>
    /// The halls, two decks tall wherever the form is deep and wide enough: the engine room aft-most, then the role's holds,
    /// hangars, promenade or refinery, each a stretch of 12 to 24 m, walls and a margin between them.
    /// </summary>
    void Halls()
    {
        string[] kinds = _role switch
        {
            "warship" => new[] { "hangar", "hold" },
            "liner" => new[] { "promenade", "hold" },
            "miner" => new[] { "ore-hold", "refinery", "ore-hold" },
            "carrier" => new[] { "hangar", "hangar", "hold" },
            _ => new[] { "hold", "hold", "hangar" },
        };
        int want = Math.Clamp(1 + NV / 60, 1, 4);
        // (A hull with little room inside keeps it for its rooms: a hall takes no more than a fifth of its floor.)
        int open = 0;
        for (int d = 0; d < Levels; d++) open += Plane((u, v) => Zone(d, u, v) == Fill).Count();
        var cands = new List<(int D, List<(int U, int V)> Cells)>();
        for (int d = 0; d + 1 < Levels; d++)
            foreach (var comp in Parts(Plane((u, v) => Zone(d, u, v) == Fill && Zone(d + 1, u, v) == Fill)))
            {
                if (comp.Count < 60) continue;
                bool along = AlongV(comp);
                int a0 = comp.Min(c => along ? c.V : c.U), a1 = comp.Max(c => along ? c.V : c.U);
                int hl = Math.Min(a1 - a0 + 1, R.Between(12, 24));
                for (int s = a0; s + hl - 1 <= a1; s += Math.Max(4, hl / 2))
                {
                    var cells = comp.Where(c => (along ? c.V : c.U) is int a && a >= s && a < s + hl).ToList();
                    if (cells.Count < 60) continue;
                    cells = Parts(cells).MaxBy(p => p.Count)!;
                    if (cells.Count >= 60 && cells.Count * 2 <= open / 5 && Width(cells, along) >= 7) cands.Add((d, cells));
                }
            }
        var taken = new HashSet<(int, int, int)>();
        bool Free((int D, List<(int U, int V)> Cells) c) => c.Cells.All(x => !taken.Contains((c.D, x.U, x.V)) && !taken.Contains((c.D + 1, x.U, x.V)));
        void Take((int D, List<(int U, int V)> Cells) c, string kind)
        {
            AddRoom(kind, c.D, c.Cells, hall: true);
            foreach (var (u, v) in c.Cells)
                for (int dv = -3; dv <= 3; dv++)
                    for (int du = -3; du <= 3; du++)
                        for (int dd = -1; dd <= 2; dd++) taken.Add((c.D + dd, u + du, v + dv));
        }
        if (cands.Count == 0) return;
        Take(cands.MaxBy(c => c.Cells.Average(x => x.V) + c.Cells.Count * 0.02f), "engine-room");
        for (int i = 0; i < want - 1; i++)
        {
            var free = cands.Where(Free).ToList();
            if (free.Count == 0) break;
            // Lower decks and bigger stretches first, but a roll of it: the hold under the crew or over them.
            var pick = free[R.PickWeighted(free.Select(c => c.Cells.Count / (1f + c.D * 0.6f)).ToList())];
            Take(pick, kinds[i % kinds.Length]);
        }
    }

    // ---- a deck's areas --------------------------------------------------------------------------------

    /// <summary>Each open area of a deck laid out on its own (the hall's upper halves part of it: a corridor crosses them on a catwalk).</summary>
    void Deck(int d)
    {
        var avail = Plane((u, v) => Zone(d, u, v) == Fill || Upper(d, Zone(d, u, v)));
        foreach (var area in Parts(avail))
            if (area.Count(c => Zone(d, c.U, c.V) == Fill) >= 12) Area(d, area);
    }

    int Grain() => _g0 == _g1 ? _g0 : R.Between(_g0, _g1);

    /// <summary>
    /// One area of a deck: its corridors down its long axis as the hull's roll (or now and then this area's own) has them,
    /// end rooms across its ends, cross passages through it, a wall between everything and the rooms cut from the rest.
    /// </summary>
    void Area(int d, List<(int U, int V)> cells)
    {
        bool along = AlongV(cells);
        (int U, int V) Cell(int a, int b) => along ? (b, a) : (a, b);
        int a0 = cells.Min(c => along ? c.V : c.U), a1 = cells.Max(c => along ? c.V : c.U);
        var rows = new Dictionary<int, List<(int Lo, int Hi)>>();
        foreach (var g in cells.GroupBy(c => along ? c.V : c.U))
        {
            var bs = g.Select(c => along ? c.U : c.V).OrderBy(b => b).ToList();
            var runs = new List<(int Lo, int Hi)>();
            int lo = bs[0];
            for (int i = 1; i <= bs.Count; i++)
                if (i == bs.Count || bs[i] != bs[i - 1] + 1)
                {
                    runs.Add((lo, bs[i - 1]));
                    if (i < bs.Count) lo = bs[i];
                }
            rows[g.Key] = runs;
        }
        int W = Width(cells, along);
        if (W < 3) return;
        // A narrow area (a waist, a pod, a thin hull) has no corridor: its rooms open one into the next.
        if (W <= 6)
        {
            Split(d, cells.Where(c => Zone(d, c.U, c.V) == Fill && !Four.Any(o => Zone(d, c.U + o.U, c.V + o.V) >= 0)).ToList());
            return;
        }
        int style = R.Chance(_reroll) ? 1 + R.PickWeighted(_weights) : _style;
        if (W <= 10) style = Single;
        else if (style == Twin && W < 15) style = Spine;
        float f = style == Offset ? R.Float(0.2f, 0.34f) : style == Twin ? R.Float(0.22f, 0.3f) : 0.5f;
        bool flip = R.Chance(0.5f);
        int len = a1 - a0 + 1;
        int e0 = len >= 24 && R.Chance(_ends) ? R.Between(5, 8) : 0, e1 = len >= 24 && R.Chance(_ends) ? R.Between(5, 8) : 0;
        int aS = a0 + e0, aE = a1 - e1;
        var C = new HashSet<(int U, int V)>();
        for (int line = 0; line < (style == Twin ? 2 : 1); line++)
        {
            int? prev = null;
            for (int a = aS; a <= aE; a++)
            {
                if (!rows.TryGetValue(a, out var runs)) { prev = null; continue; }
                var run = runs.OrderByDescending(r => prev is int p && p >= r.Lo && p <= r.Hi ? 1 : 0).ThenByDescending(r => r.Hi - r.Lo).First();
                int w = run.Hi - run.Lo + 1, k = Math.Min(_cw, w);
                if (k < 3) { prev = null; continue; }
                float frac = style switch { Single => flip ? 1f : 0f, Offset => flip ? 1f - f : f, Twin => line == 0 ? f : 1f - f, _ => 0.5f };
                int target = run.Lo + (int)MathF.Round((w - k) * frac);
                bool Fits(int lo) => lo >= run.Lo && lo + k - 1 <= run.Hi;
                // (Over a hall it runs straight on, a catwalk.)
                bool Over(int lo) => Enumerable.Range(lo, k).All(b => Upper(d, Zone(d, Cell(a, b).U, Cell(a, b).V)));
                int at = prev is int pc && Fits(pc) && Over(pc) ? pc
                       : prev is int p0 && Math.Abs(p0 - target) <= 1 && Fits(p0) ? p0
                       : prev is int p1 && Fits(p1 + Math.Sign(target - p1)) ? p1 + Math.Sign(target - p1) : target;
                for (int b = at; b < at + k; b++) C.Add(Cell(a, b));
                prev = at;
            }
        }
        // Cross passages through the whole width (and a twin's joined at its ends into a ring).
        var xs = new List<int>();
        if (style == Twin) { xs.Add(aS); xs.Add(aE - _xw + 1); }
        // (A narrow area's single row of rooms is cut by them half as often.)
        int every = style == Single ? _every * 2 : _every;
        for (int a = aS + R.Between(every / 2, every); a + _xw - 1 <= aE - 4; a += _regular ? every : R.Between(every - 3, every + 3)) xs.Add(a);
        foreach (int a in xs)
            for (int r = a; r < a + _xw; r++)
                if (rows.TryGetValue(r, out var runs))
                    foreach (var run in runs.Where(x => Enumerable.Range(x.Lo, x.Hi - x.Lo + 1).Any(b => C.Contains(Cell(r, b)))))
                        for (int b = run.Lo; b <= run.Hi; b++)
                            if (!Upper(d, Zone(d, Cell(r, b).U, Cell(r, b).V))) C.Add(Cell(r, b));
        foreach (var (u, v) in C) Set(d, u, v, Way);
        // A wall round everything already there; the rooms from what is left.
        var rest = cells.Where(c => Zone(d, c.U, c.V) == Fill).ToList();
        var walls = rest.Where(c => Four.Any(o => Zone(d, c.U + o.U, c.V + o.V) is int z && (z >= 0 || z == Way))).ToHashSet();
        foreach (var band in Parts(rest.Where(c => !walls.Contains(c)))) Split(d, band);
    }

    /// <summary>A band of rooms between the corridors: cut along its length in the hull's grain, a wall between each two, and across too where it runs deep.</summary>
    void Split(int d, List<(int U, int V)> band)
    {
        if (band.Count < 9) return;
        bool along = AlongV(band);
        int A((int U, int V) c) => along ? c.V : c.U;
        int B((int U, int V) c) => along ? c.U : c.V;
        int a0 = band.Min(A), a1 = band.Max(A);
        var pieces = new List<List<(int U, int V)>>();
        for (int a = a0; a <= a1;)
        {
            int g = Grain(), end = a1 - (a + g) < 4 ? a1 : a + g - 1;
            pieces.Add(band.Where(c => A(c) >= a && A(c) <= end).ToList());
            a = end + 2;
        }
        foreach (var piece in pieces)
        {
            if (piece.Count == 0) continue;
            int b0 = piece.Min(B), b1 = piece.Max(B);
            var parts = new List<List<(int U, int V)>>();
            if (b1 - b0 + 1 > _deep)
            {
                int mid = (b0 + b1) / 2;
                parts.Add(piece.Where(c => B(c) < mid).ToList());
                parts.Add(piece.Where(c => B(c) > mid).ToList());
            }
            else parts.Add(piece);
            foreach (var part in parts.SelectMany(Parts))
            {
                int du = part.Max(c => c.U) - part.Min(c => c.U), dv = part.Max(c => c.V) - part.Min(c => c.V);
                if (part.Count >= 9 && du >= 1 && dv >= 1) AddRoom("", d, part);
            }
        }
    }

    /// <summary>Each deck's corridor cells, a space for every joined run of them.</summary>
    void Corridors()
    {
        for (int d = 0; d < Levels; d++)
            foreach (var part in Parts(Plane((u, v) => Zone(d, u, v) == Way)))
                AddRoom("corridor", d, part, corridor: true);
    }

    // ---- doors ----------------------------------------------------------------------------------------

    readonly HashSet<(int, int, int)> _doorCells = new();

    /// <summary>Every wall cell between a room and another space it could open into: the wall, the way through, the space beyond.</summary>
    List<(int U, int V, int DU, int DV, int Other)> Walls(int id)
    {
        Room r = Rooms[id];
        int d = r.Deck;
        var list = new List<(int, int, int, int, int)>();
        foreach (var (u, v) in r.Floor)
            foreach (var (du, dv) in Four)
            {
                int wu = u + du, wv = v + dv;
                if (Zone(d, wu, wv) != Fill || _doorCells.Contains((d, wu, wv))) continue;
                int z = Zone(d, wu + du, wv + dv);
                if (z < 0 || z == id || Upper(d, z) || Rooms[z].Stair.Contains((wu + du, wv + dv)) || r.Stair.Contains((u, v))) continue;
                list.Add((wu, wv, du, dv, z));
            }
        return list;
    }

    /// <summary>A door between a room and the space beyond, at the candidate nearest the middle of their shared wall, as wide as asked where the wall allows.</summary>
    bool Door(int id, List<(int U, int V, int DU, int DV, int Other)> walls, int other, int widest, (int U, int V)? away = null)
    {
        var mine = walls.Where(w => w.Other == other && !_doorCells.Contains((Rooms[id].Deck, w.U, w.V))
                                    && (away is not { } a || Math.Abs(w.U - a.U) + Math.Abs(w.V - a.V) >= 6)).ToList();
        if (mine.Count == 0) return false;
        float mu = (float)mine.Average(w => w.U), mv = (float)mine.Average(w => w.V);
        int d = Rooms[id].Deck;
        foreach (var w in mine.OrderBy(w => MathF.Abs(w.U - mu) + MathF.Abs(w.V - mv)))
        {
            var (pu, pv) = w.DV != 0 ? (1, 0) : (0, 1);
            int width = 1;
            while (width < widest && mine.Any(x => x.U == w.U + pu * width && x.V == w.V + pv * width && x.DU == w.DU && x.DV == w.DV)) width++;
            Doors.Add(new DoorPlan(d, w.U, w.V, w.DU, w.DV, width, id, other));
            for (int k = -1; k <= width; k++) _doorCells.Add((d, w.U + pu * k, w.V + pv * k));
            Rooms[id].Doors.Add(((w.U, w.V), (w.U - w.DU, w.V - w.DV), width));
            Rooms[other].Doors.Add(((w.U, w.V), (w.U + w.DU, w.V + w.DV), width));
            return true;
        }
        return false;
    }

    /// <summary>
    /// A door from every room to the corridor beside it (a big room two, one at either end), a hall's wide doors into the
    /// corridors at its ends, then the rooms nothing reaches through a door into a room something does (what is still shut
    /// away has a hatch out of its own, or is left as solid: <see cref="Exits"/>).
    /// </summary>
    void MakeDoors()
    {
        bool Open(int z) => Rooms[z].Corridor || Rooms[z].Hall;
        for (int id = 0; id < Rooms.Count; id++)
        {
            Room r = Rooms[id];
            if (r.Corridor) continue;
            var walls = Walls(id);
            var corridors = walls.Where(w => Rooms[w.Other].Corridor).GroupBy(w => w.Other).OrderByDescending(g => g.Count()).ToList();
            if (r.Hall)
            {
                int n = 0;
                foreach (var g in corridors)
                    if (n < 2 && Door(id, walls, g.Key, 3)) n++;
                continue;
            }
            if (corridors.Count == 0) continue;
            Door(id, walls, corridors[0].Key, r.Area >= 30 ? 2 : 1);
            if (r.Area < 40) continue;
            var first = r.Doors[0].Wall;
            if (corridors.Count > 1) Door(id, Walls(id), corridors[1].Key, 1);
            else Door(id, Walls(id), corridors[0].Key, 1, first);
        }
        // Rooms reached through their neighbours.
        for (int pass = 0; pass < 6; pass++)
        {
            bool more = false;
            for (int id = 0; id < Rooms.Count; id++)
            {
                Room r = Rooms[id];
                if (r.Corridor || r.Doors.Count > 0 || r.Floor.Count == 0) continue;
                var walls = Walls(id);
                var reached = walls.Where(w => Open(w.Other) || Rooms[w.Other].Doors.Count > 0).GroupBy(w => w.Other).OrderByDescending(g => Open(g.Key) ? 1 : 0).ThenByDescending(g => g.Count()).FirstOrDefault();
                if (reached != null && Door(id, walls, reached.Key, 1)) more = true;
            }
            if (!more) break;
        }
        // Rooms with no way to anything joined to a neighbour all the same: a chain of rooms opening one into the next, its way out its own.
        for (int id = 0; id < Rooms.Count; id++)
        {
            Room r = Rooms[id];
            if (r.Corridor || r.Doors.Count > 0 || r.Floor.Count == 0) continue;
            var walls = Walls(id);
            var next = walls.GroupBy(w => w.Other).OrderByDescending(g => g.Count()).FirstOrDefault();
            if (next != null) Door(id, walls, next.Key, 1);
        }
    }

    /// <summary>A space left out: its cells back to solid.</summary>
    void Drop(int id)
    {
        Room r = Rooms[id];
        foreach (var (u, v) in r.Floor)
        {
            Set(r.Deck, u, v, Fill);
            if (r.Hall && Zone(r.Deck + 1, u, v) == id) Set(r.Deck + 1, u, v, Fill);
        }
        r.Floor.Clear();
        r.Doors.Clear();
        // Its doors gone from the rooms beyond them, its flights with it.
        foreach (DoorPlan dp in Doors.Where(x => x.A == id || x.B == id).ToList())
        {
            Doors.Remove(dp);
            Rooms[dp.A == id ? dp.B : dp.A].Doors.RemoveAll(x => x.Wall == (dp.U, dp.V));
        }
        Flights.RemoveAll(f => Zone(f.Deck, f.U, f.V) < 0);
    }

    // ---- stairs ---------------------------------------------------------------------------------------

    int[] _group = Array.Empty<int>();

    int Find(int i) => _group[i] == i ? i : _group[i] = Find(_group[i]);

    void Union(int a, int b) => _group[Find(a)] = Find(b);

    /// <summary>
    /// Flights of stairs joining what no door joins: a straight flight of five up from a room on one deck into a room on
    /// the deck over it (or down one side of a corridor), 2 m wide or 1 m where no more will go, landings at both ends,
    /// the deck over the flight open; the smallest rooms that take one first. A long run of decks gets a second flight
    /// far from the first.
    /// </summary>
    void Stairs()
    {
        _group = Enumerable.Range(0, Rooms.Count).ToArray();
        foreach (DoorPlan dp in Doors) Union(dp.A, dp.B);
        // Rooms first; a corridor takes a flight down one side of it where no room will (the way past it a lane wide).
        bool Usable(Room r) => (r.Corridor ? r.Purpose == "corridor" : r.Purpose == "" && r.Doors.Count > 0) && !r.Hall && r.Area >= 12;
        for (int d = 0; d + 1 < Levels; d++)
        {
            var lower = Enumerable.Range(0, Rooms.Count).Where(i => Rooms[i].Deck == d && Usable(Rooms[i])).ToList();
            var upper = Enumerable.Range(0, Rooms.Count).Where(i => Rooms[i].Deck == d + 1 && Usable(Rooms[i])).ToList();
            var pairs = (from a in lower from b in upper
                         where Rooms[a].Floor.Overlaps(Rooms[b].Floor)
                         orderby (Rooms[a].Corridor ? 1000 : 0) + (Rooms[b].Corridor ? 1000 : 0) + Rooms[a].Area + Rooms[b].Area
                         select (a, b)).ToList();
            foreach (var (a, b) in pairs)
            {
                if (!Usable(Rooms[a]) || !Usable(Rooms[b])) continue;
                bool far = Flights.Where(x => x.Deck == d).All(x => MathF.Abs(x.V - Rooms[a].Vc) + MathF.Abs(x.U - Rooms[a].Uc) > 45f);
                if (Find(a) != Find(b) || far && R.Chance(0.5f)) Climb(d, a, b);
            }
        }
    }

    bool Climb(int d, int a, int b)
    {
        Room A = Rooms[a], B = Rooms[b];
        var dirs = Four.ToList();
        R.Shuffle(dirs);
        foreach (int width in new[] { 2, 1 })
        foreach (var (du, dv) in dirs)
        {
            var (pu, pv) = dv != 0 ? (1, 0) : (0, 1);
            (int, int) At((int U, int V) c, int j, int lane) => (c.U + du * j + pu * lane, c.V + dv * j + pv * lane);
            foreach (var c in A.Floor.OrderBy(x => x.U).ThenBy(x => x.V))
            {
                var steps = new HashSet<(int, int)>();
                var holes = new HashSet<(int, int)>();
                bool ok = true;
                for (int lane = 0; lane < width && ok; lane++)
                {
                    ok &= A.Floor.Contains(At(c, 0, lane)) && B.Floor.Contains(At(c, 6, lane));
                    for (int j = 1; j <= 5 && ok; j++)
                    {
                        ok &= A.Floor.Contains(At(c, j, lane)) && B.Floor.Contains(At(c, j, lane));
                        steps.Add(At(c, j, lane));
                        holes.Add(At(c, j, lane));
                    }
                }
                if (!ok) continue;
                if (!Clear(A, steps, new[] { At(c, 0, 0), At(c, 0, width - 1) }) || !Clear(B, holes, new[] { At(c, 6, 0), At(c, 6, width - 1) })) continue;
                Flights.Add(new Flight(d, c.U, c.V, du, dv, width));
                A.Stair.UnionWith(steps);
                B.Stair.UnionWith(holes);
                foreach (Room r in new[] { A, B })
                    if (!r.Corridor)
                    {
                        r.Purpose = "stair";
                        r.Corridor = true;
                    }
                Union(a, b);
                return true;
            }
        }
        return false;
    }

    /// <summary>Does the rest of a room's floor stay one, its doorways and the landing on it, with the flight's cells taken out?</summary>
    static bool Clear(Room r, HashSet<(int, int)> taken, (int, int)[] landing)
    {
        var inside = r.Doors.Select(x => x.Inside).ToList();
        if (inside.Any(taken.Contains)) return false;
        var free = r.Floor.Where(c => !taken.Contains(c) && !r.Stair.Contains(c)).ToList();
        if (free.Count == 0) return false;
        var part = Parts(free).Find(p => p.Contains(landing[0]));
        return part != null && landing.All(part.Contains) && inside.All(part.Contains);
    }

    // ---- the ways out ---------------------------------------------------------------------------------

    /// <summary>
    /// Every joined set of spaces its way out through the shell: the biggest the hull door (2 m, starboard and low where it
    /// can), a cargo opening from a hold or hangar out through its flank, a hatch from each of the rest (a module on a truss,
    /// a pod); whatever no way out can be cut from is left as solid.
    /// </summary>
    void Exits()
    {
        var groups = Enumerable.Range(0, Rooms.Count).Where(i => Rooms[i].Floor.Count > 0).GroupBy(Find).OrderByDescending(g => g.Sum(i => Rooms[i].Area)).ToList();
        bool first = true;
        foreach (var g in groups)
        {
            var ids = g.ToList();
            bool made = first ? Exit(ids, 2, 2, "door", sideways: true) || Exit(ids, 1, 2, "door") : Exit(ids, 1, 2, "hatch");
            if (first && made)
                foreach (int h in ids.Where(i => Rooms[i].Hall && Rooms[i].Purpose is "hold" or "hangar" or "ore-hold").Take(1))
                    Exit(new List<int> { h }, R.Between(4, 6), 4, "cargo", sideways: true);
            if (!made)
                foreach (int i in ids) Drop(i);
            first &= !made;
        }
    }

    bool Exit(List<int> ids, int width, int height, string what, bool sideways = false)
    {
        var best = (Score: float.MaxValue, O: (Opening?)null);
        foreach (int id in ids)
        {
            Room r = Rooms[id];
            int d = r.Deck, y0 = Walk(d);
            var near = new HashSet<(int, int)>(r.Doors.Select(x => x.Inside));
            foreach (var (u, v) in r.Floor)
                foreach (var (du, dv) in Four)
                {
                    if (sideways && du == 0) continue;
                    var (pu, pv) = dv != 0 ? (1, 0) : (0, 1);
                    int depth = Depth(d, u, v, du, dv, pu, pv, width, height, r);
                    if (depth < 0) continue;
                    // Corridors first, low, toward the middle of the hull's length, starboard for the door.
                    float score = (r.Corridor ? 0 : 20) + d * 8 + MathF.Abs(v - NV * 0.45f) * 0.3f + (du < 0 ? 6 : 0) + (dv != 0 ? 10 : 0) + (near.Contains((u, v)) ? 30 : 0);
                    if (score < best.Score) best = (score, new Opening(d, u, v, du, dv, width, height, depth, what, id));
                }
        }
        if (best.O == null) return false;
        Openings.Add(best.O);
        Room room = Rooms[best.O.Room];
        // (The way out's first wall cell is the door; its doorway kept clear like any other.)
        room.Doors.Add(((best.O.U + best.O.DU, best.O.V + best.O.DV), (best.O.U, best.O.V), width));
        return true;
    }

    /// <summary>
    /// How many cells of wall and shell lie between a room's edge cell and the outside along (du, dv), every one of a way
    /// <paramref name="width"/> cells across and <paramref name="height"/> high (the first a wall, the rest the shell, the
    /// outside clear for two beyond), or -1.
    /// </summary>
    int Depth(int d, int u, int v, int du, int dv, int pu, int pv, int width, int height, Room r)
    {
        int y0 = Walk(d);
        for (int k = 0; k < width; k++)
            if (!r.Floor.Contains((u + pu * k, v + pv * k)) || r.Stair.Contains((u + pu * k, v + pv * k))) return -1;
        for (int n = 1; n <= 6; n++)
        {
            bool outside = true;
            for (int k = 0; k < width && outside; k++)
                for (int y = y0; y < y0 + height && outside; y++)
                    outside = Mask(u + du * n + pu * k, y, v + dv * n + pv * k) == 0;
            if (outside) return n >= 2 ? n - 1 : -1;
            for (int k = 0; k < width; k++)
            {
                int cu = u + du * n + pu * k, cv = v + dv * n + pv * k;
                if (n == 1 ? Zone(d, cu, cv) >= 0 || _doorCells.Contains((d, cu, cv)) : Zone(d, cu, cv) >= 0 || Interior(cu, y0, cv)) return -1;
            }
        }
        return -1;
    }

    // ---- what each room is for ------------------------------------------------------------------------

    /// <summary>
    /// What every room is for. The ones a ship can't do without first: the bridge where its faction keeps it, the engine
    /// room aft (if no hall took it), the reactor near it with the core room cut from its far end, a hold, and for a crew of
    /// people their quarters, a galley and a medbay; the rest as the stretch of the hull's program each lies in has it.
    /// </summary>
    void Purposes()
    {
        var free = Rooms.Where(r => !r.Corridor && !r.Hall && r.Purpose == "" && r.Area >= 9).ToList();
        bool crewed = _faction != Faction.Kessari;
        string crew = _role switch { "liner" => "cabin", "warship" or "carrier" => "barracks", _ => "crew-quarters" };
        Room? Take(string purpose, Func<Room, float> score, Func<Room, bool>? ok = null)
        {
            Room? r = free.Where(x => ok == null || ok(x)).OrderByDescending(score).FirstOrDefault();
            if (r == null) return null;
            r.Purpose = purpose;
            free.Remove(r);
            return r;
        }
        if (free.Count == 0) return;
        float vMid = (float)free.Average(r => r.Vc), yTop = free.Max(r => r.Y);
        // The bridge: in the nose, a blister above or below, a sponson, or buried deep (ship-forms rule 3).
        float[] where = _faction switch
        {
            Faction.Meridian => new[] { 0.4f, 0.1f, 0.2f, 0.3f },
            Faction.Reavers => new[] { 0.1f, 0.5f, 0f, 0.4f },
            Faction.Vharn or Faction.Kessari => new[] { 0.2f, 0f, 0f, 0.8f },
            Faction.Exchange or Faction.Freeholders => new[] { 0.8f, 0.2f, 0f, 0f },
            _ => new[] { 0.5f, 0.2f, 0.1f, 0.2f },
        };
        float uMid = (float)free.Average(r => r.Uc);
        Func<Room, float> bridge = R.PickWeighted(where) switch
        {
            1 => r => r.Y * 2f - r.Vc * 0.4f,
            2 => r => MathF.Abs(r.Uc) - r.Vc * 0.1f,
            3 => r => -MathF.Abs(r.Vc - vMid) - MathF.Abs(r.Uc - uMid) * 2f - MathF.Abs(r.Y - yTop / 2f),
            _ => r => -r.Vc,
        };
        Take("bridge", bridge, r => r.Area >= 12);
        Room? engine = Rooms.FirstOrDefault(r => r.Purpose == "engine-room" && r.Area > 0) ?? Take("engine-room", r => r.Vc + r.Area * 0.05f, r => r.Area >= 16);
        float aft = engine?.Vc ?? NV;
        Room? reactor = Take("reactor", r => -MathF.Abs(r.Vc - aft) - MathF.Abs(r.Deck - (engine?.Deck ?? 0)) * 4f, r => r.Area >= 24 && CoreCut(r, true))
                        ?? Take("reactor", r => -MathF.Abs(r.Vc - aft), r => r.Area >= 12);
        if (reactor != null && !CoreCut(reactor, false))
        {
            Room? core = Take("core-room", r => -MathF.Abs(r.Vc - reactor.Vc) - MathF.Abs(r.Deck - reactor.Deck) * 5f);
            if (core != null) CoreRoom = Rooms.IndexOf(core);
        }
        if (!Rooms.Any(r => r.Purpose is "hold" or "ore-hold" && r.Area > 0)) Take(_role == "miner" ? "ore-hold" : "hold", r => r.Area);
        if (crewed)
        {
            Take(crew, r => -MathF.Abs(r.Vc - vMid), r => r.Area >= 12);
            Take("galley", r => -MathF.Abs(r.Vc - vMid) * 0.5f + r.Area * 0.1f, r => r.Area >= 12);
            Take("medbay", r => -MathF.Abs(r.Vc - vMid), r => r.Area >= 12);
        }
        foreach (Room r in free) r.Purpose = Program(Bay(r.Vc), r.Area, crewed, crew);
        // A hull too small for a core room of its own keeps the seat for a core in its reactor, its engine room or its bridge.
        if (CoreRoom < 0)
            foreach (string host in new[] { "reactor", "engine-room", "bridge" })
                if (Rooms.FindIndex(r => r.Area > 0 && r.Purpose == host) is int at && at >= 0)
                {
                    CoreRoom = at;
                    break;
                }
    }

    string Bay(float v) => _program(v);

    /// <summary>What a room is for, by the stretch of the program it lies in: a weighted roll of that stretch's kinds of room.</summary>
    string Program(string bay, int area, bool crewed, string crew)
    {
        (string, float)[] kinds = bay switch
        {
            "bow" => new[] { ("sensor-room", 1f), ("stores", 1f), ("officers", 1f), (crew, 1f) },
            "crew" => new[] { (crew, 3f), ("officers", 0.6f), ("galley", 0.4f), ("stores", 1f), ("workshop", 0.5f) },
            "power" or "engine" => new[] { ("generator-room", 2f), ("workshop", 1f), ("stores", 1f) },
            "hold" or "ore-hold" or "refinery" => new[] { ("stores", 2f), ("workshop", 1f), (_role == "miner" ? "refinery" : "stores", 0.5f) },
            "hangar" => new[] { ("workshop", 2f), ("stores", 1f), (crew, 1f) },
            "armoury" or "magazine" or "barracks" => new[] { ("armoury", 1f), ("magazine", 1f), ("barracks", 2f) },
            "cabins" or "promenade" or "med" => new[] { ("cabin", 3f), ("galley", 0.5f), ("medbay", 0.5f), ("stores", 0.5f) },
            _ => new[] { ("stores", 1f), (crew, 1f) },
        };
        var list = kinds.Where(k => crewed || k.Item1 is not ("officers" or "galley" or "medbay" or "cabin" or "barracks" or "crew-quarters")).ToList();
        if (list.Count == 0) list = new() { ("stores", 1f), ("workshop", 1f), ("generator-room", 1f) };
        if (area < 16) list = list.Select(k => (k.Item1, k.Item1 is "stores" or "cabin" or "officers" ? k.Item2 * 3f : k.Item2)).ToList();
        return list[R.PickWeighted(list.Select(k => k.Item2).ToList())].Item1;
    }

    /// <summary>
    /// The core room cut from the reactor's far end (away from its doors) behind a wall, reached only through the reactor
    /// (<paramref name="test"/>: only whether it can be).
    /// </summary>
    bool CoreCut(Room r, bool test)
    {
        bool along = AlongV(r.Floor.ToList());
        int A((int U, int V) c) => along ? c.V : c.U;
        if (r.Doors.Count == 0) return false;
        float mean = (float)r.Doors.Average(x => A(x.Inside));
        int a0 = r.Floor.Min(A), a1 = r.Floor.Max(A);
        bool fromHigh = mean < (a0 + a1) / 2f;
        for (int k = 4; k <= 6; k++)
        {
            int cut = fromHigh ? a1 - k : a0 + k;
            var core = r.Floor.Where(c => fromHigh ? A(c) > cut : A(c) < cut).ToList();
            var wall = r.Floor.Where(c => A(c) == cut).ToList();
            var kept = r.Floor.Where(c => fromHigh ? A(c) < cut : A(c) > cut).ToList();
            if (core.Count < 12 || kept.Count < 12 || Parts(core).Count != 1 || Parts(kept).Count != 1) continue;
            if (r.Doors.Any(x => !kept.Contains(x.Inside)) || r.Stair.Count > 0) continue;
            int s = fromHigh ? 1 : -1;
            (int U, int V) Step((int U, int V) w, int k) => along ? (w.U, w.V + k) : (w.U + k, w.V);
            var doors = wall.Select(w => (W: w, In: Step(w, -s), Out: Step(w, s))).Where(x => kept.Contains(x.In) && core.Contains(x.Out))
                            .OrderBy(x => MathF.Abs((along ? x.W.U : x.W.V) - (along ? r.Uc : r.Vc))).ToList();
            if (doors.Count == 0) continue;
            if (test) return true;
            var door = doors[0];
            int id = Rooms.IndexOf(r);
            foreach (var c in wall.Concat(core))
            {
                r.Floor.Remove(c);
                Set(r.Deck, c.U, c.V, Fill);
            }
            int coreId = AddRoom("core-room", r.Deck, core);
            CoreRoom = coreId;
            int du = along ? 0 : s, dv = along ? s : 0;
            Doors.Add(new DoorPlan(r.Deck, door.W.U, door.W.V, du, dv, 1, id, coreId));
            r.Doors.Add((door.W, door.In, 1));
            Rooms[coreId].Doors.Add((door.W, door.Out, 1));
            return true;
        }
        return false;
    }
}
