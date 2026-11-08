#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// Capital ships come down as hulks, composed from rules, never drawn by hand: one generator for every
/// crashed or derelict capital vessel. A role (freighter, warship, liner, miner, carrier) gives the bays
/// stem to stern; a size class gives the beam and how many cargo bays; the world's tech era gives the
/// plating, and the faction's kit its livery (stripes, a hull number, chevrons and checks: design, ships and
/// structures). How it came down (broken back, nose down, scattered, long hauler) gives its pose.
///
/// It is built as a player builds a capital vessel on the large grid, 2 m blocks and 45 degree slopes: a
/// tapered, raked bow, chamfered shoulders, a bridge tower with its windows forward, three decks 6 m apart
/// (a floor block and 4 m clear, Empyrion's usual deck) joined by switchback stairs, a spine corridor on
/// every deck, holds and the hangar two decks tall with a catwalk through them, the reactor, the fuel and
/// the core room amidships, the engine room against the stern and banks of real thruster blocks behind it,
/// command chairs and consoles on the bridge. Sizes follow Empyrion's capital vessels (community builds run
/// about 90 to 135 m, its crashed Titan lies in three pieces): 160 to 210 m long and 36 to 52 m in the beam.
///
/// The route is Empyrion's for a crashed vessel: you walk round it to find the ways in (a cargo ramp, the
/// breaches the crash tore), climb its decks, and the loot is deepest where the dead core sits behind the
/// reactor and on the bridge at the top. A tilt is a shear of whole cells, with a ramp wherever a floor
/// steps, so every deck can still be walked.
/// </summary>
public static class CapitalHull
{
    public sealed record Spec(string Role, string Size, string Crash);

    /// <summary>A size class: the beam and how many cargo bays (holds, cabins, ore holds) the role carries.</summary>
    public static (int Beam, int Bays) SizeOf(string size) => size switch
    {
        "medium" => (36, 2),
        "huge" => (52, 4),
        _ => (44, 3),
    };

    public static void Compose(Canvas c, Rng rng, LayoutHints? hints, TechLevel tech, SitePart part)
    {
        var p = new Plot(c, rng, tech, hints?.Front ?? Dir.North);
        Civic.Wear(p);
        new Hulk(p, new Spec(part.Arg("role", "freighter"), part.Arg("size", "large"), part.Arg("crash", "broken")), rng.Fork("hull")).Build();
        p.Finish();
    }
}

/// <summary>One hulk, built in its own frame (u across, centred; v from the bow aft; y up from the keel) and set down in its pose.</summary>
sealed class Hulk
{
    const int Pitch = 6, D = 3, TDecks = 2, Chamfer = 3, Rake = 12;
    static int Walk(int d) => Pitch * d + 2;
    static readonly int HullTop = Walk(D), TowerTop = Walk(D + TDecks);

    enum Bay { Bow, Crew, Hold, Hangar, Power, Engine, Armoury, Barracks, Magazine, Cabins, Promenade, Med, OreHold, Refinery }
    static bool IsHall(Bay b) => b is Bay.Hold or Bay.Hangar or Bay.Engine or Bay.Promenade or Bay.OreHold or Bay.Refinery;

    sealed record BayRec(Bay Kind, int V0, int V1);

    sealed class Room
    {
        public ModuleKind Kind;
        public string Purpose = "";
        public int Deck, WalkY, Top;
        public readonly HashSet<Int3> Cells = new();
    }

    sealed record DoorRec(List<Int3> Cells, List<Int3> Back, Dir Facing, int A, int B);

    /// <summary>A stretch of the hull (the whole of it, or a piece a crash tore off) and where it lies.</summary>
    sealed class Piece
    {
        public int V0, V1, OffU, OffV;
        public Func<int, int> Dy = _ => 0;
    }

    /// <summary>A way in from outside: an opening in a flank (side +1 starboard, -1 port) or an open face, onto a room's floor.</summary>
    sealed record Way(int Side, int V0, int Width, int Room, string What);

    readonly Plot P;
    readonly Rng R;
    readonly CapitalHull.Spec S;
    readonly int W, Half;
    int L, Bz, Nac = 8;
    readonly List<BayRec> Bays = new();
    int TU0, TU1, TV0, TV1, TowerBay = -1;
    readonly Dictionary<int, int> StairAt = new(); // bay index -> stair start v
    int[,] Hmin = new int[0, 0], Hmax = new int[0, 0], Inner = new int[0, 0];
    int UMin;
    readonly Dictionary<Int3, Block> B = new();
    readonly HashSet<Int3> Open = new();
    readonly Dictionary<Int3, int> Owner = new();
    readonly List<Room> Rooms = new();
    readonly List<DoorRec> Doors = new();
    readonly List<(int A, int B)> Links = new();
    readonly List<(SlotKind K, Int3 C, int Room)> Slots = new();
    readonly List<(Int3 C, int Room)> Lamps = new();
    readonly Dictionary<Int3, Block> Props = new();
    readonly HashSet<Int3> Reserved = new();
    readonly List<Piece> Pieces = new();
    readonly List<Way> Ways = new();
    readonly List<(int U0, int U1)> Nacelles = new();
    int[] Spine = new int[D];
    int Vault = -1, BridgeRoom = -1, Cut = -1, Break = -1;
    bool Round, Spiked, Pods;

    Stuff2 Plate, Trim, InnerS, FloorS, Orange, Teal, Dark, MachineS, Grate;
    Rgb Hazard, Black;

    public Hulk(Plot p, CapitalHull.Spec s, Rng r)
    {
        P = p;
        S = s;
        R = r;
        (W, _) = CapitalHull.SizeOf(s.Size);
        Half = W / 2;
    }

    static Stuff2 K(Plot p, char symbol, Stuff2 fallback) =>
        p.C.Kit.BySymbol(symbol) is { } e ? new Stuff2(e.Material, e.Colors[0]) : fallback;

    public void Build()
    {
        Paints();
        Plan();
        Pose();
        PlaceStairs();
        Columns();
        Fill();
        Decks();
        Openings();
        Tear();
        Furnish();
        Exterior();
        Emit();
    }

    // ---- what it is made of ------------------------------------------------------------------------

    void Paints()
    {
        Fab f = P.F;
        Faction faction = P.C.Kit.Faction;
        Plate = K(P, 'a', f.Wall);
        Plate = P.Tech switch
        {
            TechLevel.Low => new Stuff2(Material.Steel, Plate.C.Lerp(new Rgb(0x7a, 0x6a, 0x5a), 0.35f)),
            TechLevel.High => new Stuff2(Material.Alloy, Plate.C.Scale(1.04f)),
            _ => new Stuff2(Plate.M == Material.Concrete ? Material.Steel : Plate.M, Plate.C),
        };
        Trim = new Stuff2(Plate.M, Plate.C.Scale(0.72f));
        InnerS = K(P, '%', f.Panel);
        if (InnerS.M is Material.Concrete or Material.Rock or Material.Timber) InnerS = new Stuff2(Material.Steel, InnerS.C);
        FloorS = new Stuff2(Material.Steel, K(P, '_', f.Floor).C);
        Orange = K(P, 'k', f.Accent);
        Teal = K(P, 't', f.Accent);
        MachineS = K(P, 'm', f.Steel);
        Dark = new Stuff2(Material.Steel, new Rgb(0x2a, 0x2e, 0x34));
        Grate = new Stuff2(Material.Steel, f.Grating.C);
        Hazard = f.Hazard;
        Black = f.Black;
        // The faction's hand on the shape: a bulbous bow and stacked pods for the traders, rams for raiders.
        Round = faction == Faction.Exchange || S.Role == "liner";
        Pods = S.Role == "freighter" || faction == Faction.Exchange;
        Spiked = faction == Faction.Reavers || S.Role == "miner";
    }

    // ---- the plan: bays stem to stern ---------------------------------------------------------------

    void Plan()
    {
        var (_, n) = CapitalHull.SizeOf(S.Size);
        var list = new List<(Bay, int)> { (Bay.Bow, 24), (Bay.Crew, 22) };
        switch (S.Role)
        {
            case "warship":
                list.AddRange(new[] { (Bay.Armoury, 20), (Bay.Hangar, 26) });
                for (int i = 1; i < n; i++) list.Add((Bay.Barracks, 20));
                list.Add((Bay.Magazine, 18));
                break;
            case "liner":
                for (int i = 0; i < n; i++) list.Add((Bay.Cabins, 20));
                list.AddRange(new[] { (Bay.Promenade, 26), (Bay.Med, 18) });
                break;
            case "miner":
                for (int i = 0; i < n; i++) list.Add((Bay.OreHold, 22));
                list.Add((Bay.Refinery, 24));
                break;
            case "carrier":
                for (int i = 0; i < Math.Max(2, n - 1); i++) list.Add((Bay.Hangar, 30));
                break;
            default:
                for (int i = 0; i < n; i++) list.Add((Bay.Hold, 22));
                list.Add((Bay.Hangar, 26));
                break;
        }
        list.Add((Bay.Power, 24));
        list.Add((Bay.Engine, 20));
        int v = 0;
        foreach (var (kind, len0) in list)
        {
            // Each hull its own proportions: the cargo bays a block longer or shorter.
            int len = len0 + (kind is Bay.Hold or Bay.Cabins or Bay.OreHold or Bay.Hangar ? 2 * R.Range(-1, 2) : 0);
            Bays.Add(new BayRec(kind, v, v + len));
            v += len;
        }
        L = v;
        Bz = Bays[0].V1;
        // A hauler's bridge stands aft over its power, as a cargo ship's does; a warship's, a liner's and a carrier's forward.
        bool aft = S.Role is "freighter" or "miner";
        TowerBay = Bays.FindIndex(b => b.Kind == (aft ? Bay.Power : Bay.Crew));
    }

    // ---- how it came down -----------------------------------------------------------------------------

    void Pose()
    {
        int u0 = 0, v0 = 24;
        switch (S.Crash)
        {
            case "nose":
            {
                // Nose down: the bow ploughed in nine metres deep, the stern up on it at about four degrees.
                int bow = -9 - R.Range(0, 2);
                Pieces.Add(new Piece { V0 = 0, V1 = L, OffU = u0, OffV = v0, Dy = v => bow + (int)Math.Floor(0.075 * v) });
                break;
            }
            case "scattered":
            {
                // Broken apart: the fore hull lies nearly level where it stopped, the stern section with the
                // hangar, the power and the engines tumbled further on down the furrow, the cargo strewn between.
                int bay = Bays.FindIndex(b => b.Kind == Bay.Hangar);
                if (bay < 0) bay = Bays.FindLastIndex(b => IsHall(b.Kind) && b.Kind != Bay.Engine);
                Cut = Bays[bay].V0;
                int a = -2 - (int)Math.Floor(0.03 * (Cut - 1));
                int bv = Cut + 2, gap = 34 + R.Range(0, 10), side = R.Chance(0.5f) ? 1 : -1;
                Pieces.Add(new Piece { V0 = 0, V1 = Cut, OffU = u0, OffV = v0, Dy = v => a + (int)Math.Floor(0.03 * v) });
                Pieces.Add(new Piece { V0 = bv, V1 = L, OffU = u0 + side * (6 + R.Range(0, 6)), OffV = v0 + gap, Dy = v => -2 - (int)Math.Floor(0.05 * (v - bv) + 0.5) });
                break;
            }
            case "long":
            {
                // A long hauler down on its belly: buried six metres or so along its length, its bow a little up.
                int stern = -7;
                Pieces.Add(new Piece { V0 = 0, V1 = L, OffU = u0, OffV = v0, Dy = v => stern + (int)Math.Floor(0.02 * (L - v)) });
                break;
            }
            default:
            {
                // Broken back: it came down on its belly and its keel gave amidships, the middle sunk into the
                // ground with the hull torn open over it, bow and stern up either side.
                int mid = Bays.Where(b => IsHall(b.Kind) && b.Kind != Bay.Engine).Select(b => (b.V0 + b.V1) / 2).OrderBy(m => Math.Abs(m - L / 2)).FirstOrDefault(L / 2);
                Break = mid;
                Pieces.Add(new Piece { V0 = 0, V1 = L, OffU = u0, OffV = v0, Dy = v => -6 + (int)Math.Floor(0.07 * Math.Abs(v - mid - 0.5)) });
                break;
            }
        }
    }

    Piece? PieceOf(int v)
    {
        if (v >= L) v = L - 1;
        if (v < 0) v = 0;
        foreach (Piece p in Pieces) if (v >= p.V0 && v < p.V1) return p;
        return null;
    }

    int Dy(int v) => PieceOf(v)?.Dy(v) ?? 0;

    /// <summary>The floor runs level over [v0, v1): one piece, no step.</summary>
    bool Flat(int v0, int v1)
    {
        Piece? p = PieceOf(v0);
        if (p == null) return false;
        for (int v = v0; v < v1; v++)
            if (PieceOf(v) != p || p.Dy(v) != p.Dy(v0)) return false;
        return true;
    }

    bool StepAt(Int3 c) => PieceOf(c.Z) is { } p && (PieceOf(c.Z + 1) is { } a && a == p && p.Dy(c.Z + 1) != p.Dy(c.Z) || PieceOf(c.Z - 1) is { } b && b == p && p.Dy(c.Z - 1) != p.Dy(c.Z));

    bool NearTear(int v, int margin) => Break >= 0 && Math.Abs(v - Break) < 6 + margin || Cut >= 0 && v >= Cut - 4 - margin && v < Cut + 6 + margin;

    void PlaceStairs()
    {
        foreach (int i in Enumerable.Range(0, Bays.Count).Where(i => Bays[i].Kind is Bay.Crew or Bay.Power))
        {
            BayRec bay = Bays[i];
            int sv = bay.V0 + 2;
            for (int s = 0; s < 8; s++)
                if (Flat(bay.V0 + 2 + s - 2, bay.V0 + 2 + s + 8) && !NearTear(bay.V0 + 2 + s, 2)) { sv = bay.V0 + 2 + s; break; }
            StairAt[i] = sv;
        }
        int ts = StairAt[TowerBay];
        TU0 = W >= 44 ? -12 : -10;
        TU1 = Math.Min(18, Half);
        TV0 = ts - 8;
        TV1 = ts + 14;
    }

    // ---- the hull's shape -------------------------------------------------------------------------------

    float HalfW(float v)
    {
        if (v >= Bz) return Half;
        float t = Math.Clamp(v / Bz, 0f, 1f);
        return Half * (Round ? 0.3f + 0.7f * MathF.Sqrt(1f - (1f - t) * (1f - t)) : 0.26f + 0.74f * t);
    }

    bool InHull(int u, int v) => v >= 0 && v < L && MathF.Abs(u + 0.5f) < HalfW(v + 0.5f);
    bool InTower(int u, int v) => u >= TU0 && u < TU1 && v >= TV0 && v < TV1;

    int HullCorner(int cu, int cv)
    {
        float d = HalfW(cv) - Math.Abs(cu);
        int h = (int)MathF.Floor(HullTop - Chamfer + d);
        h = Math.Min(h, HullTop);
        h = Math.Min(h, HullTop - Rake + cv);
        h = Math.Min(h, HullTop - 2 + (L - cv));
        return Math.Max(h, 2);
    }

    int TowerCorner(int cu, int cv) => Math.Min(TowerTop, TowerTop - 2 + Math.Min(Math.Min(cu - TU0, TU1 - cu), TV1 - cv));

    int CornerAt(int u, int v, int cu, int cv)
    {
        int h = -1;
        if (InHull(u, v)) h = HullCorner(cu, cv);
        if (InTower(u, v)) h = Math.Max(h, TowerCorner(cu, cv));
        return h;
    }

    int Col(int[,] a, int u, int v) => u - UMin < 0 || u - UMin >= a.GetLength(0) || v < 0 || v >= a.GetLength(1) ? -1 : a[u - UMin, v];

    void Columns()
    {
        UMin = -Half - 2;
        int nu = W + 4;
        Hmin = new int[nu, L];
        Hmax = new int[nu, L];
        Inner = new int[nu, L];
        for (int v = 0; v < L; v++)
            for (int u = UMin; u < UMin + nu; u++)
            {
                if (!InHull(u, v) && !InTower(u, v)) { Hmin[u - UMin, v] = Hmax[u - UMin, v] = -1; continue; }
                int a = CornerAt(u, v, u, v), b = CornerAt(u, v, u + 1, v), c = CornerAt(u, v, u, v + 1), d = CornerAt(u, v, u + 1, v + 1);
                Hmin[u - UMin, v] = Math.Min(Math.Min(a, b), Math.Min(c, d));
                Hmax[u - UMin, v] = Math.Max(Math.Max(a, b), Math.Max(c, d));
            }
        // The inside: a block in from every face, a block of keel under it.
        for (int v = 0; v < L; v++)
            for (int u = UMin; u < UMin + nu; u++)
            {
                int m = int.MaxValue;
                for (int dv = -2; dv <= 2; dv++)
                    for (int du = -2; du <= 2; du++) m = Math.Min(m, Col(Hmin, u + du, v + dv));
                Inner[u - UMin, v] = m < 4 ? 0 : m - 2;
            }
    }

    int InnerTop(int u, int v) => Col(Inner, u, v);
    bool Usable(int d, int u, int v) => InnerTop(u, v) >= Walk(d) + 4;

    void Fill()
    {
        for (int v = 0; v < L; v++)
            for (int u = UMin; u < UMin + W + 4; u++)
            {
                int lo = Col(Hmin, u, v), hi = Col(Hmax, u, v), top = InnerTop(u, v);
                if (lo < 0) continue;
                for (int y = 0; y < lo; y++)
                {
                    bool inner = y >= 2 && y < top;
                    Block b = !inner ? new Block(BlockType.Cube, Plate.M, Plate.C, Role.Wall)
                        : y % Pitch < 2 ? new Block(BlockType.Cube, FloorS.M, FloorS.C, Role.Floor)
                        : new Block(BlockType.Cube, InnerS.M, InnerS.C, Role.Wall);
                    B[new Int3(u, y, v)] = b;
                }
                int nw = CornerAt(u, v, u, v), ne = CornerAt(u, v, u + 1, v), sw = CornerAt(u, v, u, v + 1), se = CornerAt(u, v, u + 1, v + 1);
                for (int y = lo; y < hi; y++)
                    if (Plot.Piece(nw > y, ne > y, sw > y, se > y, Plate) is { } piece) B[new Int3(u, y, v)] = piece;
            }
    }

    // ---- decks and rooms ----------------------------------------------------------------------------------

    int NewRoom(ModuleKind kind, string purpose, int deck, int walk, int top)
    {
        Rooms.Add(new Room { Kind = kind, Purpose = purpose, Deck = deck, WalkY = walk, Top = top });
        return Rooms.Count - 1;
    }

    void Take(int room, Int3 c)
    {
        if (Owner.ContainsKey(c)) return;
        Owner[c] = room;
        Open.Add(c);
        B.Remove(c);
        Rooms[room].Cells.Add(c);
    }

    void Drop(Int3 c)
    {
        if (Owner.Remove(c, out int r)) Rooms[r].Cells.Remove(c);
        Open.Remove(c);
    }

    /// <summary>Carves a room's box from deck d0 through d1 (4 m clear each, the floors between gone), where every deck of it fits.</summary>
    int Carve(int room, int u0, int u1, int v0, int v1, int d0, int d1, Func<int, int, bool>? keep = null)
    {
        int n = 0;
        for (int v = v0; v < v1; v++)
            for (int u = u0; u < u1; u++)
            {
                if (!Usable(d1, u, v) || PieceOf(v) == null || keep != null && !keep(u, v)) continue;
                for (int y = Walk(d0); y < Walk(d1) + 4; y++) Take(room, new Int3(u, y, v));
                n++;
            }
        return n;
    }

    int Area(int u0, int u1, int v0, int v1, int d)
    {
        int n = 0;
        for (int v = v0; v < v1; v++)
            for (int u = u0; u < u1; u++)
                if (Usable(d, u, v) && PieceOf(v) != null && !Owner.ContainsKey(new Int3(u, Walk(d), v))) n++;
        return n;
    }

    void Decks()
    {
        for (int d = 0; d < D; d++) Spine[d] = NewRoom(ModuleKind.Corridor, "corridor", d, Walk(d), Walk(d) + 4);
        // The stairs first: each a switchback tower beside the spine.
        foreach (var (bay, sv) in StairAt) Stair(sv, 0, bay == TowerBay ? D + TDecks - 1 : D - 1);
        for (int i = 0; i < Bays.Count; i++)
        {
            BayRec bay = Bays[i];
            bool hall = IsHall(bay.Kind);
            int stairV = StairAt.TryGetValue(i, out int sv) ? sv : -1;
            if (hall)
            {
                Carve(Spine[0], -2, 2, bay.V0, bay.V0 + 2, 0, 0);
                int walkway = NewRoom(ModuleKind.Corridor, "catwalk", 1, Walk(1), Walk(1) + 4);
                Carve(walkway, -1, 1, bay.V0, bay.V1, 1, 1);
                Links.Add((walkway, Spine[1]));
                int h = NewRoom(ModuleKind.Hall, HallPurpose(bay.Kind), 0, Walk(0), Walk(1) + 4);
                Carve(h, -Half, Half, bay.V0 + 2, bay.V1, 0, 1);
                Links.Add((h, Spine[0]));
                // The catwalk through it on deck 1: grating on beams, a rail each side.
                for (int v = bay.V0 + 2; v < bay.V1; v++)
                    for (int u = -2; u < 2; u++)
                    {
                        var deck = new Int3(u, Walk(1) - 1, v);
                        if (Owner.GetValueOrDefault(deck, -1) != h) continue;
                        Drop(deck);
                        B[deck] = new Block(BlockType.Lattice, Grate.M, Grate.C, Role.Floor);
                        if (u is -2 or 1)
                        {
                            var rail = deck + Int3.Up;
                            Drop(rail);
                            B[rail] = new Block(BlockType.Rail, Material.Steel, Hazard, Role.Decor);
                        }
                    }
            }
            for (int d = hall ? 2 : 0; d < D; d++)
            {
                Carve(Spine[d], -2, 2, bay.V0, bay.V1, d, d);
                var (port, stbd, n) = Sides(bay.Kind, d);
                int sFrom = stairV >= 0 ? stairV + 6 : bay.V0 + 2;
                if (bay.Kind == Bay.Power && d == 0)
                {
                    // The reactor, and behind it the core room: only through the reactor.
                    int reactor = SideRoom("reactor", d, -Half, -4, bay.V0 + 2, bay.V1 - 10, -1);
                    int core = SideRoom("core-room", d, -Half, -4, bay.V1 - 8, bay.V1, reactor);
                    if (core >= 0) { Rooms[core].Kind = ModuleKind.Vault; Vault = core; }
                }
                else Split(port, d, -Half, -4, bay.V0 + 2, bay.V1, n);
                Split(stbd, d, 4, Half, sFrom, bay.V1, n);
            }
        }
        // The tower: the officers' deck, the bridge over it.
        {
            int sv = StairAt[TowerBay];
            bool Keep(int u, int v) => !(u >= 2 && u < 16 && v >= sv - 2 && v < sv + 6);
            int officers = NewRoom(ModuleKind.Room, "officers", D, Walk(D), Walk(D) + 4);
            Carve(officers, TU0 + 2, TU1 - 2, TV0 + 2, TV1 - 2, D, D, Keep);
            BridgeRoom = NewRoom(ModuleKind.Room, "bridge", D + 1, Walk(D + 1), Walk(D + 1) + 4);
            Carve(BridgeRoom, TU0 + 2, TU1 - 2, TV0 + 2, TV1 - 2, D + 1, D + 1, Keep);
        }
        foreach (var (bay, sv) in StairAt) StairDoors(sv, 0, bay == TowerBay ? D + TDecks - 1 : D - 1);
        if (Vault < 0) Vault = BridgeRoom;
    }

    static string HallPurpose(Bay b) => b switch
    {
        Bay.Hangar => "hangar", Bay.Engine => "engine-room", Bay.Promenade => "promenade", Bay.OreHold => "ore-hold", Bay.Refinery => "refinery", _ => "hold",
    };

    static (string Port, string Stbd, int N) Sides(Bay b, int d) => (b, d) switch
    {
        (Bay.Bow, 0) => ("stores", "stores", 1),
        (Bay.Bow, 1) => ("crew-quarters", "crew-quarters", 1),
        (Bay.Bow, _) => ("sensor-room", "stores", 1),
        (Bay.Crew, 0) => ("workshop", "stores", 1),
        (Bay.Crew, 1) => ("crew-quarters", "crew-quarters", 2),
        (Bay.Crew, _) => ("mess", "med-ward", 1),
        (Bay.Power, 0) => ("reactor", "fuel-tanks", 1),
        (Bay.Power, 1) => ("generator-room", "workshop", 1),
        (Bay.Power, _) => ("crew-quarters", "stores", 1),
        (Bay.Engine, _) => ("engineering", "stores", 1),
        (Bay.Hangar, _) => ("workshop", "crew-quarters", 2),
        (Bay.Armoury, 0) => ("armoury", "armoury", 1),
        (Bay.Armoury, _) => ("barracks", "barracks", 2),
        (Bay.Barracks, _) => ("barracks", "barracks", 2),
        (Bay.Magazine, 0) => ("magazine", "magazine", 1),
        (Bay.Magazine, 1) => ("armoury", "stores", 1),
        (Bay.Magazine, _) => ("barracks", "barracks", 1),
        (Bay.Cabins, _) => ("cabin", "cabin", 3),
        (Bay.Med, 0) => ("med-ward", "med-ward", 1),
        (Bay.Med, _) => ("med-ward", "stores", 1),
        _ => ("stores", "crew-quarters", 2),
    };

    void Split(string purpose, int d, int u0, int u1, int v0, int v1, int n)
    {
        int total = v1 - v0, len = (total - 2 * (n - 1)) / n;
        if (len < 6) { n = 1; len = total; }
        for (int i = 0; i < n; i++)
        {
            int a = v0 + i * (len + 2), b = i == n - 1 ? v1 : a + len;
            SideRoom(purpose, d, u0, u1, a, b, -1);
        }
    }

    /// <summary>A room off the spine (or, from = another room, off that one through a bulkhead door), or none where too little of it fits.</summary>
    int SideRoom(string purpose, int d, int u0, int u1, int v0, int v1, int from)
    {
        if (v1 - v0 < 4 || Area(u0, u1, v0, v1, d) < 20) return -1;
        int r = NewRoom(ModuleKind.Room, purpose, d, Walk(d), Walk(d) + 4);
        Carve(r, u0, u1, v0, v1, d, d);
        bool ok = from >= 0 ? BulkheadDoor(from, r, u0, u1, v0 - 2, d) : SpineDoor(r, d, u0 > 0);
        if (!ok)
        {
            foreach (Int3 c in Rooms[r].Cells.ToList()) { Drop(c); B[c] = new Block(BlockType.Cube, InnerS.M, InnerS.C, Role.Wall); }
            return -1;
        }
        return r;
    }

    readonly List<(int Deck, int U, int V)> _doorAt = new();

    bool DoorFree(int d, int u, int v) => !_doorAt.Any(x => x.Deck == d && Math.Abs(x.U - u) < 4 && Math.Abs(x.V - v) < 4);

    /// <summary>A door through the spine's wall (the front layer at u = 2 or -4), facing east, where the floor is level about it.</summary>
    bool SpineDoor(int r, int d, bool stbd)
    {
        int front = stbd ? 2 : -4, roomU = stbd ? 4 : -5, spineU = stbd ? 1 : -2, y = Walk(d);
        var vs = Rooms[r].Cells.Where(c => c.Y == y && c.X == roomU).Select(c => c.Z).Distinct().ToList();
        if (vs.Count == 0) return false;
        double mid = vs.Average();
        foreach (int v in vs.OrderBy(v => Math.Abs(v + 0.5 - mid)))
        {
            if (!vs.Contains(v + 1) || !Flat(v - 2, v + 4) || NearTear(v, 1) || !DoorFree(d, front, v)) continue;
            if (Owner.GetValueOrDefault(new Int3(spineU, y, v), -1) != Spine[d] || Owner.GetValueOrDefault(new Int3(spineU, y, v + 1), -1) != Spine[d]) continue;
            MakeDoor(r, Spine[d], d, front, v, alongV: true);
            return true;
        }
        return false;
    }

    /// <summary>A door through a bulkhead between two rooms (the wall's front layer at v = wall), facing south.</summary>
    bool BulkheadDoor(int a, int b, int u0, int u1, int wall, int d)
    {
        int y = Walk(d);
        double mid = (u0 + u1) / 2.0;
        foreach (int u in Enumerable.Range(u0, u1 - u0).OrderBy(u => Math.Abs(u + 0.5 - mid)))
        {
            if (!Flat(wall - 2, wall + 4)) return false;
            bool fits = Owner.GetValueOrDefault(new Int3(u, y, wall - 1), -1) == a && Owner.GetValueOrDefault(new Int3(u + 1, y, wall - 1), -1) == a
                && Owner.GetValueOrDefault(new Int3(u, y, wall + 2), -1) == b && Owner.GetValueOrDefault(new Int3(u + 1, y, wall + 2), -1) == b;
            if (!fits || !DoorFree(d, u, wall)) continue;
            MakeDoor(a, b, d, wall, u, alongV: false);
            return true;
        }
        return false;
    }

    /// <summary>
    /// The standard door: alongV, in a wall across u (front layer u = at, cells v, v + 1), facing east; else in a
    /// wall across v (front layer v = at, cells u = along, along + 1), facing south. The back layer is left open.
    /// </summary>
    void MakeDoor(int a, int b, int d, int at, int along, bool alongV)
    {
        int y0 = Walk(d);
        var cells = new List<Int3>();
        var back = new List<Int3>();
        for (int s = 0; s < 2; s++)
            for (int y = y0; y < y0 + DoorSize.Tall; y++)
            {
                cells.Add(alongV ? new Int3(at, y, along + s) : new Int3(along + s, y, at));
                back.Add(alongV ? new Int3(at + 1, y, along + s) : new Int3(along + s, y, at + 1));
            }
        foreach (Int3 c in cells.Concat(back)) { B.Remove(c); Open.Add(c); }
        Doors.Add(new DoorRec(cells, back, alongV ? Dir.East : Dir.South, a, b));
        _doorAt.Add((d, alongV ? at : along, alongV ? along : at));
        for (int s = -3; s <= 4; s++)
            for (int t = -1; t <= 2; t++)
                Reserved.Add(alongV ? new Int3(at + s, y0, along + t) : new Int3(along + t, y0, at + s));
    }

    /// <summary>
    /// A switchback stair tower beside the spine from deck d0 to deck d1: landings across both lanes at its
    /// near end (u 4, 5) on every other deck and its far end (u 12, 13) between, flights of six steps between
    /// them, the lanes at v = sv and sv + 2. Its walls are the hull's own fill.
    /// </summary>
    void Stair(int sv, int d0, int d1)
    {
        int r = NewRoom(ModuleKind.Stair, "stair", d0, Walk(d0), Walk(d1) + 4);
        for (int k = 0; k <= d1 - d0; k++)
        {
            int d = d0 + k, ua = k % 2 == 0 ? 4 : 12;
            for (int v = sv; v < sv + 4; v++)
                for (int u = ua; u < ua + 2; u++)
                    for (int y = Walk(d); y < Walk(d) + 4; y++) Take(r, new Int3(u, y, v));
            if (k == d1 - d0) break;
            for (int t = 0; t < 6; t++)
            {
                int u = k % 2 == 0 ? 6 + t : 11 - t, y = Walk(d) + t;
                for (int v = k % 2 == 0 ? sv : sv + 2, e = v + 2; v < e; v++)
                {
                    var c = new Int3(u, y, v);
                    Take(r, c);
                    Open.Remove(c);
                    B[c] = new Block(BlockType.Stair, FloorS.M, FloorS.C.Scale(1.08f), Role.Traverse, k % 2 == 0 ? Dir.East : Dir.West);
                    for (int up = 1; up <= 4; up++) { var a = c + Int3.Up * up; if (Owner.TryGetValue(a, out int o) && o != r) Drop(a); Take(r, a); }
                }
            }
        }
        for (int v = sv - 2; v < sv + 6; v++)
            for (int u = 2; u < 16; u++) Reserved.Add(new Int3(u, 0, v));
    }

    void StairDoors(int sv, int d0, int d1)
    {
        int r = Owner[new Int3(4, Walk(d0), sv)];
        for (int k = 0; k <= d1 - d0; k++)
        {
            int d = d0 + k, y = Walk(d);
            if (k % 2 == 0)
            {
                int other = Owner.GetValueOrDefault(new Int3(1, y, sv), -1);
                if (other >= 0 && Owner.GetValueOrDefault(new Int3(1, y, sv + 1), -1) == other) MakeDoor(other, r, d, 2, sv, alongV: true);
            }
            else
            {
                int other = Owner.GetValueOrDefault(new Int3(12, y, sv + 6), -1);
                if (other >= 0 && Owner.GetValueOrDefault(new Int3(13, y, sv + 6), -1) == other) MakeDoor(r, other, d, sv + 4, 12, alongV: false);
            }
        }
    }

    // ---- the ways in --------------------------------------------------------------------------------------

    void Openings()
    {
        // The cargo ramp: down from a hangar or a hold onto the ground, its bay door open.
        int bay = Bays.FindIndex(b => b.Kind == Bay.Hangar && (Cut < 0 || b.V0 >= Cut));
        if (bay < 0) bay = Bays.FindLastIndex(b => IsHall(b.Kind) && b.Kind != Bay.Engine);
        int side = R.Chance(0.5f) ? 1 : -1;
        BayRec cargo = Bays[bay];
        int hall = Owner.GetValueOrDefault(new Int3(0, Walk(0), cargo.V0 + 4), -1);
        int ov = Enumerable.Range(cargo.V0 + 4, Math.Max(1, cargo.V1 - cargo.V0 - 12)).FirstOrDefault(v => Flat(v - 3, v + 9) && !NearTear(v, 2), cargo.V0 + 4);
        Flank(side, ov, 6, 6, hall, "cargo");
        switch (S.Crash)
        {
            case "nose":
            {
                // Where the hull meets the ground, the crash tore its flank open.
                var holds = Bays.Where(b => IsHall(b.Kind) && b.Kind != Bay.Engine).ToList();
                int at = -1;
                foreach (BayRec b in holds)
                    for (int v = b.V0 + 4; v < b.V1 - 8 && at < 0; v++)
                        if (Walk(0) + Dy(v) is >= -3 and <= 1 && Flat(v - 3, v + 7)) at = v;
                if (at < 0) at = holds[0].V0 + 4;
                Flank(-side, at, 4, 5, Owner.GetValueOrDefault(new Int3(0, Walk(0), at + 1), hall), "breach");
                break;
            }
            case "long":
            {
                BayRec b = Bays.First(x => IsHall(x.Kind) && x.Kind != Bay.Engine);
                int at = Enumerable.Range(b.V0 + 4, b.V1 - b.V0 - 10).FirstOrDefault(v => Flat(v - 3, v + 9), b.V0 + 4);
                Flank(-side, at, 6, 4, Owner.GetValueOrDefault(new Int3(0, Walk(0), at + 1), hall), "breach");
                break;
            }
            case "broken":
                foreach (int s in new[] { 1, -1 })
                    Ways.Add(new Way(s, Break - 2, 4, Owner.GetValueOrDefault(new Int3(0, Walk(0), Break), hall), "break"));
                break;
            case "scattered":
                Ways.Add(new Way(0, Cut - 1, 0, Owner.GetValueOrDefault(new Int3(0, Walk(0), Cut - 1), -1), "fore-face"));
                Ways.Add(new Way(0, Cut + 2, 0, Owner.GetValueOrDefault(new Int3(0, Walk(0), Cut + 2), hall), "aft-face"));
                break;
        }
    }

    /// <summary>An opening through a flank onto deck 0, width by height, its edge cells taken into the room.</summary>
    void Flank(int side, int v0, int width, int height, int room, string what)
    {
        if (room < 0) return;
        for (int v = v0; v < v0 + width; v++)
            for (int k = 0; k < 4; k++)
            {
                int u = side > 0 ? Half - 1 - k : -Half + k;
                for (int y = Walk(0); y < Walk(0) + height + (what == "breach" ? (int)(Hash.Cell(new Int3(v, 0, 0), 91) % 3) : 0); y++)
                {
                    var c = new Int3(u, y, v);
                    if (Owner.ContainsKey(c)) continue;
                    B.Remove(c);
                    Take(room, c);
                }
                for (int y = Walk(0) - 1; y >= 0; y--) B[new Int3(u, y, v)] = new Block(BlockType.Cube, Plate.M, Plate.C, Role.Floor);
            }
        for (int v = v0 - 3; v < v0 + width + 3; v++)
            for (int u = -Half; u < Half; u++)
                if (side > 0 ? u >= Half - 8 : u < -Half + 8) Reserved.Add(new Int3(u, 0, v));
        Ways.Add(new Way(side, v0, width, room, what));
    }

    /// <summary>The crash's own damage: the broken back torn open to the sky, ragged faces where the hull parted.</summary>
    void Tear()
    {
        if (Break >= 0)
            foreach (Int3 c in B.Keys.Concat(Open).ToList())
            {
                if (c.Y < 2) continue;
                int reach = 3 + (int)(Hash.Cell(new Int3(c.X / 2, c.Y / 2, 0), 37) % 3) + (c.Y > HullTop - 4 ? 1 : 0);
                if (Math.Abs(c.Z - Break + 0.5) < reach) { B.Remove(c); Open.Add(c); }
            }
        if (Cut >= 0)
            foreach (Int3 c in B.Keys.ToList())
            {
                if (c.Y < Walk(1) || c.Z < Cut - 4 || c.Z >= Cut + 6) continue;
                int edge = c.Z < Cut ? Cut - 1 - c.Z : c.Z - Cut - 2;
                if (edge < (int)(Hash.Cell(new Int3(c.X / 2, c.Y / 2, 1), 53) % 4)) { B.Remove(c); Open.Add(c); }
            }
    }

    bool Torn(Int3 c) => !B.ContainsKey(c) && !Owner.ContainsKey(c) && Open.Contains(c);

    // ---- what's in the rooms ------------------------------------------------------------------------------------

    bool Supports(Int3 c) => B.TryGetValue(c, out Block b) && BlockInfo.Supports(b.Type);

    bool Floor(Room r, Int3 c) => c.Y == r.WalkY && Open.Contains(c) && Open.Contains(c + Int3.Up) && Supports(c + Int3.Down) && !Props.ContainsKey(c);

    bool Free(Room r, Int3 c) => Floor(r, c) && !Reserved.Contains(c) && !Reserved.Contains(c with { Y = 0 }) && !StepAt(c) && !Slots.Any(s => s.C == c);

    bool ByWall(Int3 c) => Dirs.Horizontal.Any(h => B.TryGetValue(c + h.Vec(), out Block b) && BlockInfo.IsFullCube(b.Type));

    Dir AwayFromWall(Int3 c) => Dirs.Horizontal.FirstOrDefault(h => B.TryGetValue(c - h.Vec(), out Block b) && BlockInfo.IsFullCube(b.Type), Dir.North);

    Block Dev(BlockType t, Stuff2 s, Dir f = Dir.North) => new(t, s.M, s.C, Role.Decor, f);
    Block Fur(BlockType t, Stuff2 s, Dir f = Dir.North) => new(t, s.M, s.C, Role.Prop, f);

    void Furnish()
    {
        for (int i = 0; i < Rooms.Count; i++)
        {
            Room r = Rooms[i];
            if (r.Cells.Count == 0) continue;
            var floor = r.Cells.Where(c => Floor(r, c)).OrderBy(c => c.Z).ThenBy(c => c.X).ToList();
            if (floor.Count == 0) continue;
            var free = floor.Where(c => Free(r, c)).ToList();
            var wall = free.Where(ByWall).ToList();
            var placed = new HashSet<Int3>(Props.Keys);
            if (free.Count == 0) continue;
            switch (r.Purpose)
            {
                case "hold": Containers(r, free, "cargo"); break;
                case "ore-hold": Containers(r, free, "ore"); break;
                case "hangar": Hangar(r, free); break;
                case "engine-room": Engines(r, free); break;
                case "reactor": Reactor(i, r, free); break;
                case "core-room": CoreRoom(i, r, free); break;
                case "fuel-tanks": Grid(free, 4, 2, c => Dev(BlockType.Tank, MachineS.Scale(0.9f))); break;
                case "generator-room": Grid(free, 4, 2, c => Dev(BlockType.Machine, MachineS)); break;
                case "refinery": Grid(free, 6, 3, c => Dev(BlockType.Machine, MachineS)); break;
                case "magazine": Grid(free, 3, 2, c => Fur(BlockType.Crate, Dark.Scale(1.4f))); break;
                case "bridge": Bridge(i, r, free); break;
                case "crew-quarters" or "barracks" or "cabin":
                    Along(wall, 2, c => Fur(BlockType.Bunk, new Stuff2(Material.Cloth, K(P, 'b', Teal).C), AwayFromWall(c)));
                    break;
                case "mess" or "promenade": Islands(free, c => Fur(BlockType.Table, InnerS.Scale(1.2f))); break;
                case "med-ward":
                    Along(wall, 3, c => Fur(BlockType.Bed, new Stuff2(Material.Cloth, new Rgb(0xc8, 0xcc, 0xc4)), AwayFromWall(c)));
                    break;
                case "stores" or "armoury":
                    Along(wall, 2, c => Fur(r.Purpose == "armoury" ? BlockType.Locker : BlockType.Shelf, Dark.Scale(1.5f), AwayFromWall(c)));
                    break;
                case "workshop": Along(wall, 3, c => Fur(BlockType.Bench, MachineS, AwayFromWall(c))); break;
                case "engineering" or "sensor-room" or "officers":
                    Along(wall, 3, c => Dev(BlockType.Terminal, MachineS, AwayFromWall(c)));
                    break;
            }
            // Loot where the plan says it goes: the holds, the stores, the bridge and the core room most.
            int caches = r.Purpose switch { "hold" or "ore-hold" or "hangar" => 2, "stores" or "armoury" or "magazine" or "officers" or "workshop" => 1, "crew-quarters" or "cabin" or "barracks" or "med-ward" => R.Chance(0.4f) ? 1 : 0, _ => 0 };
            foreach (Int3 c in Shuffle(wall.Where(c => Free(r, c) && Dirs.Horizontal.Any(h => Free(r, c + h.Vec())))).Take(caches)) Slots.Add((SlotKind.Loot, c, i));
            int spawns = r.Kind == ModuleKind.Hall ? 3 : r.Purpose is "corridor" ? 2 : r.Purpose is "catwalk" or "stair" ? 0 : R.Chance(0.5f) ? 1 : 0;
            foreach (Int3 c in Shuffle(floor.Where(c => Free(r, c) && !ByWall(c))).Take(spawns)) Slots.Add((SlotKind.Spawn, c, i));
            Keep(r, i, placed);
            // Lamps in the ceiling.
            foreach (Int3 c in floor.Where(c => (c.X & 7) == 3 && (c.Z & 7) == 3))
            {
                var lamp = c with { Y = r.Top };
                if (B.TryGetValue(lamp, out Block b) && BlockInfo.IsFullCube(b.Type)) Lamps.Add((lamp, i));
            }
        }
    }

    IEnumerable<Int3> Shuffle(IEnumerable<Int3> cells) => cells.OrderBy(c => Hash.Cell(c, (uint)R.Range(0, 1 << 20)));

    void Along(List<Int3> wall, int every, Func<Int3, Block> make)
    {
        int n = 0;
        foreach (Int3 c in wall)
            if (n++ % every == 0 && !Props.ContainsKey(c)) Props[c] = make(c);
    }

    void Grid(List<Int3> free, int every, int tall, Func<Int3, Block> make)
    {
        var set = new HashSet<Int3>(free);
        foreach (Int3 c in free)
        {
            if (Mod(c.X, every) != 1 || Mod(c.Z, every) != 1) continue;
            var foot = new[] { c, c + new Int3(1, 0, 0), c + new Int3(0, 0, 1), c + new Int3(1, 0, 1) };
            if (!foot.All(set.Contains)) continue;
            foreach (Int3 f in foot)
                for (int y = 0; y < tall; y++) if (Open.Contains(f + Int3.Up * y)) Props[f + Int3.Up * y] = make(f);
        }
    }

    void Islands(List<Int3> free, Func<Int3, Block> make)
    {
        foreach (Int3 c in free)
            if (!ByWall(c) && Mod(c.X, 3) == 0 && Mod(c.Z, 2) == 0 && !Dirs.Horizontal.Any(h => ByWall(c + h.Vec()))) Props[c] = make(c);
    }

    static int Mod(int a, int m) => ((a % m) + m) % m;

    /// <summary>Cargo containers in rows down both sides of a hold, two high, aisles between, the middle kept clear.</summary>
    void Containers(Room r, List<Int3> free, string what)
    {
        var set = new HashSet<Int3>(free);
        Rgb[] cargo = { Orange.C, Teal.C, Hazard, new Rgb(0x3a, 0x5a, 0x9a), new Rgb(0x8a, 0x2a, 0x22), Plate.C.Scale(0.85f) };
        foreach (int s in new[] { -1, 1 })
            for (int row = 5; row + 2 <= Half - 3; row += 4)
            {
                int u0 = s > 0 ? row : -row - 2;
                var vs = free.Where(c => c.X == u0).Select(c => c.Z).OrderBy(v => v).ToList();
                if (vs.Count == 0) continue;
                for (int v = vs[0] + 1; v + 6 <= vs[^1]; v += 7)
                {
                    var foot = new List<Int3>();
                    for (int dv = 0; dv < 6; dv++) for (int du = 0; du < 2; du++) foot.Add(new Int3(u0 + du, r.WalkY, v + dv));
                    if (!foot.All(set.Contains)) continue;
                    int high = what == "ore" ? 1 : 2 + (int)(Hash.Cell(new Int3(u0, 0, v), 7) % 2) * 2;
                    Rgb col = cargo[Hash.Cell(new Int3(u0, 1, v), 11) % (uint)cargo.Length];
                    foreach (Int3 f in foot)
                        for (int y = 0; y < high; y++)
                        {
                            Int3 at = f + Int3.Up * y;
                            if (!Open.Contains(at)) continue;
                            Props[at] = what == "ore"
                                ? new Block(BlockType.Rubble, Material.Rock, new Rgb(0x6a, 0x5e, 0x50), Role.Decor, Dir.North, (byte)(Hash.Cell(at, 3) % 4))
                                : new Block(BlockType.Cube, Material.Steel, ((at.Z - v) % 6 == 0 || y % 2 == 1 && (at.Z - v) % 2 == 0) ? col.Scale(0.7f) : col, Role.Decor);
                        }
                }
            }
    }

    void Hangar(Room r, List<Int3> free)
    {
        // A landing square in hazard paint on the deck, fuel tanks and crates along the far wall.
        foreach (Int3 c in free)
            if ((Math.Abs(c.X) == 8 || Math.Abs(c.X) == 9) && c.Z % 2 == 0) Props[c] = new Block(BlockType.Plate, Material.Concrete, ((c.Z >> 1) & 1) == 0 ? Hazard : Black, Role.Floor);
        Along(free.Where(c => ByWall(c) && Math.Abs(c.X) > 10).ToList(), 3, c => Dev(BlockType.Tank, MachineS.Scale(0.9f)));
    }

    void Engines(Room r, List<Int3> free)
    {
        // A drive unit against the stern bulkhead behind each nacelle, a block clear of the catwalk overhead.
        var set = new HashSet<Int3>(free);
        int back = free.Max(c => c.Z);
        foreach (var (u0, u1) in Nacelles.Count > 0 ? Nacelles : DefaultNacelles())
            for (int u = Math.Max(u0 + 1, -Half + 3); u < Math.Min(u1 - 1, Half - 3); u++)
            {
                if (u >= -3 && u < 3) continue;
                for (int v = back - 4; v <= back; v++)
                {
                    var c = new Int3(u, r.WalkY, v);
                    if (!set.Contains(c)) continue;
                    for (int y = 0; y < 4; y++)
                        if (Open.Contains(c + Int3.Up * y)) Props[c + Int3.Up * y] = Dev(y == 3 || (u & 3) == 0 ? BlockType.Machine : BlockType.Tank, y == 1 ? Dark.Scale(1.3f) : MachineS);
                }
            }
        // Coolant tanks in a row along each side.
        Along(free.Where(c => ByWall(c) && c.Z < back - 6 && Math.Abs(c.X) > 6).ToList(), 3, c => Dev(BlockType.Tank, Teal.Scale(0.6f)));
    }

    void Reactor(int i, Room r, List<Int3> free)
    {
        // The reactor: a column of tanks banded with light, a walkway round it.
        var mid = free.OrderBy(c => Math.Abs(c.X - free.Average(f => f.X)) + Math.Abs(c.Z - free.Average(f => f.Z))).First();
        for (int du = -1; du <= 1; du++)
            for (int dv = -1; dv <= 1; dv++)
                for (int y = 0; y < 4; y++)
                {
                    Int3 c = mid + new Int3(du, y, dv);
                    if (!Open.Contains(c) || Reserved.Contains(c with { Y = r.WalkY })) continue;
                    Props[c] = y == 2 ? new Block(BlockType.Cube, Material.Lumen, new Rgb(0x8f, 0xd8, 0xff), Role.Light) : Dev(du == 0 && dv == 0 ? BlockType.Machine : BlockType.Tank, MachineS);
                }
        foreach (Int3 c in free.Where(c => ByWall(c) && Hash.Cell(c, 5) % 5 == 0).Take(1)) Slots.Add((SlotKind.Terminal, c, i));
    }

    void CoreRoom(int i, Room r, List<Int3> free)
    {
        // The dead core's housing in the middle, its consoles round it, the ship's most valuable things.
        var mid = free.OrderBy(c => Math.Abs(c.X - free.Average(f => f.X)) + Math.Abs(c.Z - free.Average(f => f.Z))).First();
        for (int du = 0; du <= 1; du++)
            for (int dv = 0; dv <= 1; dv++)
                for (int y = 0; y < 2; y++)
                {
                    Int3 c = mid + new Int3(du, y, dv);
                    if (Open.Contains(c)) Props[c] = Dev(BlockType.Machine, y == 1 ? Dark : MachineS);
                }
        var walls = free.Where(c => ByWall(c) && !Props.ContainsKey(c)).ToList();
        if (walls.Count > 0) Slots.Add((SlotKind.Terminal, walls[walls.Count / 2], i));
        foreach (Int3 c in walls.Where((_, k) => k % 5 == 1).Take(2)) Slots.Add((SlotKind.Loot, c, i));
    }

    void Bridge(int i, Room r, List<Int3> free)
    {
        // Consoles along the forward windows, a chair at each, the command chair behind them, the plot table aft.
        int front = r.Cells.Where(c => c.Y == r.WalkY).Min(c => c.Z);
        var set = new HashSet<Int3>(free);
        foreach (Int3 c in free.Where(c => c.Z == front && Mod(c.X, 3) != 2))
        {
            Props[c] = Dev(BlockType.Terminal, MachineS, Dir.South);
            Int3 seat = c + new Int3(0, 0, 2);
            if (set.Contains(seat)) Props[seat] = Dev(BlockType.Seat, new Stuff2(Material.Cloth, Dark.C.Scale(1.4f)));
        }
        Int3 command = free.Where(c => c.Z == front + 6).OrderBy(c => Math.Abs(c.X - 2)).FirstOrDefault(new Int3(int.MinValue, 0, 0));
        if (command.X != int.MinValue) Props[command] = Dev(BlockType.Seat, new Stuff2(Material.Cloth, Orange.C.Scale(0.6f)));
        foreach (Int3 c in free.Where(c => c.Z == front + 9 && c.X >= -2 && c.X <= 0)) Props[c] = Fur(BlockType.Table, MachineS);
        var walls = free.Where(c => ByWall(c) && !Props.ContainsKey(c) && c.Z > front + 4).ToList();
        if (walls.Count > 0) Slots.Add((SlotKind.Terminal, walls[0], i));
        if (walls.Count > 3) Slots.Add((SlotKind.Loot, walls[^1], i));
    }

    /// <summary>
    /// Every way through a room stays open: from the doorways, every floor cell, cache, console and spawn is
    /// still reached on foot, or the props put down last come out again until it is.
    /// </summary>
    void Keep(Room r, int i, HashSet<Int3> before)
    {
        var added = Props.Keys.Where(c => !before.Contains(c) && r.Cells.Contains(c) && c.Y == r.WalkY && BlockInfo.Collides(Props[c].Type)).ToList();
        bool Clear(Int3 c) => !Props.TryGetValue(c, out Block pb) || !BlockInfo.Collides(pb.Type);
        for (int guard = 0; guard < 200; guard++)
        {
            var walk = new HashSet<Int3>(r.Cells.Where(c => c.Y == r.WalkY && Open.Contains(c) && Open.Contains(c + Int3.Up) && Clear(c) && Supports(c + Int3.Down)));
            // From where the doorways and openings let in.
            var starts = walk.Where(c => Reserved.Contains(c)).ToList();
            if (starts.Count == 0) starts = walk.Take(1).ToList();
            var seen = new HashSet<Int3>();
            var q = new Queue<Int3>();
            foreach (Int3 s in starts) if (seen.Add(s)) q.Enqueue(s);
            while (q.Count > 0)
            {
                Int3 c = q.Dequeue();
                foreach (Dir h in Dirs.Horizontal)
                {
                    Int3 n = c + h.Vec();
                    if (walk.Contains(n) && seen.Add(n)) q.Enqueue(n);
                }
            }
            bool Reached(Int3 c) => seen.Contains(c) || Dirs.Horizontal.Any(h => seen.Contains(c + h.Vec()));
            bool ok = walk.All(seen.Contains) && Slots.Where(s => s.Room == i).All(s => Reached(s.C))
                && starts.All(seen.Contains);
            if (ok || added.Count == 0) return;
            // Take out a piece standing next to a cell nobody reaches first, else the last put down.
            int at = added.FindLastIndex(c => Dirs.Horizontal.Any(h => walk.Contains(c + h.Vec()) && !seen.Contains(c + h.Vec())));
            if (at < 0) at = added.Count - 1;
            Int3 last = added[at];
            added.RemoveAt(at);
            for (int y = 0; y < 8; y++) Props.Remove(last + Int3.Up * y);
        }
    }

    IEnumerable<(int, int)> DefaultNacelles() => NacelleSpans();

    List<(int, int)> NacelleSpans()
    {
        int n = W >= 44 ? 3 : 2, span = (W - 8) / n, w = (span - 2) & ~1;
        var list = new List<(int, int)>();
        for (int i = 0; i < n; i++)
        {
            int c = -Half + 4 + span * i + span / 2;
            list.Add((c - w / 2, c - w / 2 + w));
        }
        return list;
    }

    // ---- the outside --------------------------------------------------------------------------------------------

    bool Shell(Int3 c) => B.TryGetValue(c, out Block b) && b.Role is Role.Wall or Role.Roof && b.Material == Plate.M && b.Color.Equals(Plate.C);

    void Paint(Int3 c, Rgb col)
    {
        if (B.TryGetValue(c, out Block b) && BlockInfo.Collides(b.Type)) { b.Color = col; B[c] = b; }
    }

    void Exterior()
    {
        Windows();
        Livery();
        Engines();
        Topside();
    }

    void Windows()
    {
        // The bridge's windows across its front and down its sides, a lower band for the officers' deck.
        foreach (var (room, d, rows) in new[] { (BridgeRoom, D + 1, 3), (Owner.GetValueOrDefault(new Int3(0, Walk(D), TV0 + 4), -1), D, 2) })
        {
            if (room < 0) continue;
            for (int y = Walk(d) + 1; y < Walk(d) + 1 + rows; y++)
            {
                for (int u = TU0 + 2; u < TU1 - 2; u++) Glaze(new Int3(u, y, TV0), new Int3(u, y, TV0 + 1), room);
                for (int v = TV0 + 2; v < TV0 + 10; v++)
                {
                    Glaze(new Int3(TU0, y, v), new Int3(TU0 + 1, y, v), room);
                    Glaze(new Int3(TU1 - 1, y, v), new Int3(TU1 - 2, y, v), room);
                }
            }
        }
        // Portholes down the flanks for the crew's rooms.
        foreach (var (r, i) in Rooms.Select((r, i) => (r, i)).Where(x => x.r.Purpose is "crew-quarters" or "mess" or "med-ward" or "cabin" or "barracks" or "officers"))
            foreach (Int3 c in r.Cells.Where(c => c.Y == r.WalkY + 1 && Mod(c.Z, 6) == 2).ToList())
                foreach (int s in new[] { 1, -1 })
                {
                    Int3 inner = c + new Int3(s, 0, 0), outer = c + new Int3(2 * s, 0, 0);
                    if (!InHull(outer.X, outer.Z) || InHull(outer.X + s, outer.Z) || !Shell(inner) || !Shell(outer)) continue;
                    for (int dv = 0; dv < 2; dv++) Glaze(outer + new Int3(0, 0, dv), inner + new Int3(0, 0, dv), i);
                }
    }

    void Glaze(Int3 outer, Int3 inner, int room)
    {
        if (!B.ContainsKey(outer) || !B.ContainsKey(inner) && !Owner.ContainsKey(inner)) return;
        if (!Owner.ContainsKey(inner) && !Shell(inner)) return;
        B[outer] = new Block(BlockType.Pane, Material.Glass, P.F.Glass, Role.Window);
        if (!Owner.ContainsKey(inner)) { B.Remove(inner); Take(room, inner); }
    }

    bool OuterFlank(int u, int v) => InHull(u, v) && (!InHull(u + 1, v) || !InHull(u - 1, v));

    void Livery()
    {
        foreach (Int3 c in B.Keys.ToList())
        {
            if (!Shell(c)) continue;
            bool flank = OuterFlank(c.X, c.Z) && c.Y < HullTop - Chamfer;
            bool tower = InTower(c.X, c.Z) && c.Y >= HullTop && (c.X == TU0 || c.X == TU1 - 1 || c.Z == TV0 || c.Z == TV1 - 1);
            if (flank)
            {
                // The corporate bands down both flanks, chevrons on the bow, hazard checks on the stern.
                if (c.Y is 12 or 13) Paint(c, Orange.C);
                else if (c.Y == 14) Paint(c, Teal.C);
                else if (c.Z < Bz && c.Y >= 3 && c.Y < 11 && Mod(c.Z - c.Y, 6) < 3) Paint(c, Mod(c.Z - c.Y, 12) < 6 ? Orange.C : Black);
                else if (c.Z >= L - 4 && c.Y < 11 && ((c.Z / 2 + c.Y / 2) & 1) == 0) Paint(c, Hazard);
            }
            else if (tower)
            {
                if (c.Y >= TowerTop - 4) Paint(c, ((c.X / 2 + c.Z / 2 + c.Y / 2) & 1) == 0 ? Hazard : Black);
                else if (c.Y < Walk(D) + 1) Paint(c, Orange.C);
            }
            else if (c.Z >= L - 2 && c.Y < HullTop - 2) Paint(c, ((c.X / 2 + c.Y / 2) & 1) == 0 ? Trim.C : Plate.C.Scale(0.9f));
        }
        // The hull number, two metres to a stroke, by the bow on both flanks.
        string number = (11 + R.Range(0, 88)).ToString();
        int wide = number.Length * 8 - 2;
        Stencil(number, Half - 1, 2, Bz + 4 + wide - 1, -1, Black);
        Stencil(number, -Half, 2, Bz + 4, 1, Black);
        // Bay doors: the hangar's shut one on the far side in a hazard frame.
        foreach (BayRec b in Bays.Where(b => b.Kind == Bay.Hangar))
        {
            int s = Ways.FirstOrDefault(w => w.What == "cargo")?.Side ?? 1;
            int u = s > 0 ? -Half : Half - 1;
            for (int v = b.V0 + 4; v < b.V1 - 3; v++)
                for (int y = 2; y < 11; y++)
                {
                    bool frame = v == b.V0 + 4 || v == b.V1 - 4 || y == 10;
                    Paint(new Int3(u, y, v), frame ? (((v + y) >> 1 & 1) == 0 ? Hazard : Black) : Dark.C.Scale(1.2f));
                }
        }
        // A hazard frame round each way in.
        foreach (Way w in Ways.Where(w => w.Side != 0))
        {
            int u = w.Side > 0 ? Half - 1 : -Half;
            for (int v = w.V0 - 1; v <= w.V0 + w.Width; v++)
                for (int y = Walk(0); y <= Walk(0) + 7; y++)
                    if (v == w.V0 - 1 || v == w.V0 + w.Width || y == Walk(0) + 7) Paint(new Int3(u, y, v), ((v + y) >> 1 & 1) == 0 ? Hazard : Black);
        }
        // Scorched round the tears.
        foreach (Int3 c in B.Keys.Where(c => NearTear(c.Z, 0) && Shell(c) && Hash.Cell(c, 23) % 3 != 0).ToList()) Paint(c, Plate.C.Lerp(new Rgb(0x20, 0x1c, 0x1a), 0.7f));
        if (P.Tech == TechLevel.Low)
            foreach (Int3 c in B.Keys.Where(c => Shell(c) && Hash.Cell(new Int3(c.X / 3, c.Y / 3, c.Z / 3), 29) % 7 == 0).ToList()) Paint(c, new Rgb(0x7a, 0x4a, 0x30));
    }

    void Stencil(string text, int u, int y0, int v0, int dir, Rgb ink)
    {
        const int scale = 2;
        int at = 0;
        foreach (char ch in text)
        {
            string g = Plot.Font[ch - '0'];
            for (int row = 0; row < 5; row++)
                for (int col = 0; col < 3; col++)
                {
                    if (g[row * 3 + col] != '1') continue;
                    for (int sy = 0; sy < scale; sy++)
                        for (int sa = 0; sa < scale; sa++)
                        {
                            int a = at + col * scale + sa, y = y0 + (4 - row) * scale + sy;
                            Paint(new Int3(u, y, v0 + dir * a), ink);
                        }
                }
            at += 4 * scale;
        }
    }

    void Engines()
    {
        // Nacelles off the stern, each faced with a bank of thrusters; the nozzles' face kept on one level.
        while (Nac < 12 && !(PieceOf(L - 1) is { } last && last.Dy(L + Nac - 1) == last.Dy(L + Nac) && last.Dy(L + Nac) == last.Dy(L + Nac + 1))) Nac++;
        Nacelles.AddRange(NacelleSpans());
        const int yb = 2, yt = 16;
        foreach (var (u0, u1) in Nacelles)
        {
            for (int v = L; v < L + Nac; v++)
                for (int u = u0; u < u1; u++)
                    for (int y = yb; y < yt; y++)
                    {
                        bool edge = u == u0 || u == u1 - 1 || y == yt - 1;
                        B[new Int3(u, y, v)] = new Block(BlockType.Cube, Trim.M, edge && (v & 3) < 2 ? Trim.C.Scale(0.8f) : Trim.C, Role.Wall);
                    }
            for (int u = u0; u < u1; u++) B[new Int3(u, yt, L + Nac / 2)] = new Block(BlockType.Pipe, Material.Steel, MachineS.C, Role.Decor, Dir.East);
            for (int u = u0 + 1; u + 1 < u1; u += 2)
                for (int y = yb + 1; y + 1 < yt; y += 2)
                    B[new Int3(u, y, L + Nac)] = new Block(BlockType.Thruster, Material.Steel, Dark.C.Scale(1.5f), Role.Decor, Dir.South);
        }
        // Manoeuvring thrusters in pods on both flanks just aft of the bow.
        foreach (int s in new[] { 1, -1 })
        {
            int v0 = Bz + 18;
            for (int v = v0; v < v0 + 4; v++)
                for (int y = 8; y < 12; y++)
                    for (int k = 0; k < 2; k++) B[new Int3(s > 0 ? Half + k : -Half - 1 - k, y, v)] = new Block(BlockType.Cube, Trim.M, Trim.C, Role.Wall);
            for (int y = 8; y < 12; y += 2)
            {
                B[new Int3(s > 0 ? Half + 2 : -Half - 4, y, v0 + 1)] = new Block(BlockType.Thruster, Material.Steel, Dark.C.Scale(1.5f), Role.Decor, s > 0 ? Dir.East : Dir.West);
                // A thruster is drawn from its lowest corner: facing west its housing is the cell beyond, so a mount inside the housing holds it to the pod.
                if (s < 0) B[new Int3(-Half - 3, y, v0 + 1)] = new Block(BlockType.Pipe, Material.Steel, Dark.C, Role.Decor, Dir.East);
            }
        }
    }

    /// <summary>On the hull's back: the spine, cargo pods, radiators, vents, masts on the tower, rams and turrets as the role and faction have them.</summary>
    void Topside()
    {
        int top = HullTop;
        bool Roof(int u, int v) => Col(Hmin, u, v) == top && Col(Hmax, u, v) == top && !InTower(u, v) && !NearTear(v, 2);
        void Box(int u0, int u1, int v0, int v1, int h, Func<Int3, Block> make)
        {
            for (int v = v0; v < v1; v++)
                for (int u = u0; u < u1; u++)
                {
                    if (!Roof(u, v)) return;
                }
            for (int v = v0; v < v1; v++)
                for (int u = u0; u < u1; u++)
                    for (int y = top; y < top + h; y++) B[new Int3(u, y, v)] = make(new Int3(u, y, v));
        }
        // The dorsal spine, with a main along it.
        for (int v = Bz; v < L - 4; v++)
            if (Roof(-1, v) && Roof(0, v))
            {
                for (int u = -2; u < 2; u++) if (Roof(u, v)) B[new Int3(u, top, v)] = new Block(BlockType.Cube, Trim.M, Trim.C, Role.Wall);
                B[new Int3(-1, top + 1, v)] = new Block(BlockType.Pipe, Material.Steel, MachineS.C, Role.Decor, Dir.South);
                B[new Int3(0, top + 1, v)] = new Block(BlockType.Pipe, Material.Steel, Teal.C.Scale(0.8f), Role.Decor, Dir.South);
            }
        Rgb[] pods = { Orange.C, Teal.C, Hazard, new Rgb(0x3a, 0x5a, 0x9a), new Rgb(0x8a, 0x2a, 0x22) };
        foreach (BayRec b in Bays)
        {
            if (Pods && b.Kind is Bay.Hold or Bay.Cabins)
                foreach (int s in new[] { -1, 1 })
                    for (int v = b.V0 + 2; v + 8 <= b.V1; v += 10)
                    {
                        Rgb col = pods[Hash.Cell(new Int3(s, 0, v), 17) % (uint)pods.Length];
                        int u0 = s > 0 ? 5 : -13;
                        Box(u0, u0 + 8, v, v + 8, 4, c => new Block(BlockType.Cube, Material.Steel, c.Y == top + 3 || (c.Z - v) % 4 == 0 ? col.Scale(0.75f) : col, Role.Decor));
                    }
            if (b.Kind is Bay.Power or Bay.Engine)
                for (int v = b.V0 + 2; v < b.V1 - 2; v += 4)
                    foreach (int s in new[] { -1, 1 })
                        for (int u = 4; u < Math.Min(14, Half - 6); u++)
                            if (Roof(s * u, v))
                                for (int y = top; y < top + 4; y++) B[new Int3(s * u, y, v)] = new Block(BlockType.Lattice, Material.Steel, Grate.C.Scale(1.2f), Role.Decor);
            if (S.Role == "warship" && b.Kind is Bay.Armoury or Bay.Barracks or Bay.Magazine or Bay.Crew)
            {
                int v = (b.V0 + b.V1) / 2 - 2;
                Box(-2, 2, v, v + 4, 2, c => new Block(BlockType.Machine, Material.Steel, Dark.C.Scale(1.3f), Role.Decor));
                if (Col(Hmin, 0, v) == top)
                    for (int dv = -6; dv < 0; dv++) foreach (int u in new[] { -1, 0 }) if (B.ContainsKey(new Int3(u, top + 1, v))) B[new Int3(u, top + 1, v + dv)] = new Block(BlockType.Pipe, Material.Steel, Dark.C, Role.Decor, Dir.North);
            }
        }
        // Vents and boxes scattered over the back.
        for (int v = Bz + 2; v < L - 6; v += 5)
        {
            int u = (int)(Hash.Cell(new Int3(v, 0, 3), 19) % (uint)Math.Max(1, Half - 6)) * (v % 2 == 0 ? 1 : -1);
            if (Math.Abs(u) < 4) continue;
            Box(u, u + 2, v, v + 2, 1, c => new Block(BlockType.Machine, Material.Steel, MachineS.C, Role.Decor));
        }
        // Masts on the tower, a warning light on each.
        foreach (var (u, h) in new[] { (TU0 + 3, 12), (TU1 - 4, 8), ((TU0 + TU1) / 2, 6) })
        {
            int v = TV1 - 5;
            for (int y = TowerTop; y < TowerTop + h; y++) B[new Int3(u, y, v)] = new Block(BlockType.Rib, Material.Steel, MachineS.C, Role.Decor);
            B[new Int3(u, TowerTop + h, v)] = new Block(BlockType.Cube, Material.Lumen, new Rgb(0xff, 0x30, 0x20), Role.Light);
            if (h >= 8)
                for (int k = -2; k <= 2; k++) if (k != 0) B[new Int3(u + k, TowerTop + h - 3, v)] = new Block(BlockType.Lattice, Material.Steel, MachineS.C, Role.Decor);
        }
        // A ram or a drill on the stem.
        if (Spiked)
            for (int i = 0; i < 5; i++)
                for (int y = 6 + i % 2 * 2; y < 8 + i % 2 * 2; y++)
                {
                    int u = -2 + i;
                    if (InHull(u, 1) || InHull(u, 2)) for (int v = -3; v < 1; v++) B[new Int3(u, y, v)] = new Block(v == -3 ? BlockType.Spike : BlockType.Rib, Material.Steel, Dark.C.Scale(1.4f), Role.Decor);
                }
    }

    // ---- setting it down --------------------------------------------------------------------------------------------

    Int3 Map(Int3 c)
    {
        Piece p = PieceOf(c.Z)!;
        return new Int3(c.X + p.OffU, c.Y + p.Dy(c.Z), c.Z + p.OffV);
    }

    void Emit()
    {
        // Ramps where a floor steps with the tilt, so every deck is walked.
        var ramps = new Dictionary<Int3, Dir>();
        foreach (Int3 c in Open)
        {
            if (!Owner.TryGetValue(c, out int own) || Rooms[own].WalkY != c.Y || !Open.Contains(c + Int3.Up) || !Open.Contains(c + Int3.Up * 2) || !Open.Contains(c + Int3.Up * 3) || !Supports(c + Int3.Down) || Props.ContainsKey(c) || B.ContainsKey(c)) continue;
            Piece? p = PieceOf(c.Z);
            if (p == null) continue;
            foreach (Dir h in new[] { Dir.North, Dir.South })
            {
                Int3 n = c + h.Vec();
                if (PieceOf(n.Z) != p || p.Dy(n.Z) != p.Dy(c.Z) + 1) continue;
                if (Open.Contains(n) && Open.Contains(n + Int3.Up) && Open.Contains(n + Int3.Up * 2) && Supports(n + Int3.Down) && !Props.ContainsKey(n)) ramps[c] = h;
            }
        }
        foreach (var (c, h) in ramps) B[c] = new Block(BlockType.Ramp, FloorS.M, FloorS.C.Scale(1.05f), Role.Floor, h);
        foreach (Int3 c in ramps.Keys) Open.Remove(c);
        Slots.RemoveAll(s => ramps.ContainsKey(s.C));
        // The blocks, each in its piece's place; the lost strip between pieces never was.
        foreach (var (c, b) in B)
        {
            if (PieceOf(c.Z) == null || c.Z < -6) continue;
            Int3 m = Map(c);
            // A slope of the hull's back buried in the ground is a plain block: nothing walks it, the ground lies on it.
            Block put = b;
            if (BlockInfo.IsSlope(b.Type) && b.Role == Role.Roof && m.Y <= -2 && !B.ContainsKey(c + Int3.Up) && !Open.Contains(c + Int3.Up)) put = new Block(BlockType.Cube, b.Material, b.Color, Role.Wall);
            P.Put(m.X, m.Y, m.Z, put);
        }
        // Rooms: one module a piece they lie in.
        var modules = new Dictionary<(int Room, Piece Piece), int>();
        var primary = new int[Rooms.Count];
        for (int i = 0; i < Rooms.Count; i++)
        {
            primary[i] = -1;
            Room r = Rooms[i];
            foreach (var g in r.Cells.Where(c => PieceOf(c.Z) != null).GroupBy(c => PieceOf(c.Z)!).OrderByDescending(g => g.Count()))
            {
                var cells = g.Select(Map).Select(m => (m.X, m.Y, m.Z)).ToList();
                int m = P.RoomOf(r.Kind, r.Purpose, r.Deck, cells);
                modules[(i, g.Key)] = m;
                if (primary[i] < 0) primary[i] = m;
            }
        }
        int ModuleAt(int room, Int3 c) => PieceOf(c.Z) is { } p && modules.TryGetValue((room, p), out int m) ? m : room >= 0 ? primary[room] : -1;
        foreach (Int3 c in Open)
            if (!Owner.ContainsKey(c) && PieceOf(c.Z) != null) { Int3 m = Map(c); P.Air(m.X, m.Y, m.Z); }
        foreach (DoorRec d in Doors)
        {
            if (d.Cells.Any(c => PieceOf(c.Z) == null)) continue;
            var cells = d.Cells.Select(Map).Select(m => P.Cell(m.X, m.Y, m.Z)).ToList();
            foreach (Int3 c in d.Back) { Int3 m = Map(c); P.Air(m.X, m.Y, m.Z); }
            P.C.AddDoorway(cells, P.Turn(d.Facing), ModuleAt(d.A, d.Cells[0]), ModuleAt(d.B, d.Cells[0]));
        }
        foreach (var (c, b) in Props)
        {
            if (PieceOf(c.Z) == null || ramps.ContainsKey(c)) continue;
            Int3 m = Map(c);
            P.Lay(m.X, m.Y, m.Z, b);
        }
        foreach (var (a, b) in Links)
            if (primary[a] >= 0 && primary[b] >= 0) P.C.Link(primary[a], primary[b]);
        // Pieces of one room torn apart still belong together in the plan.
        foreach (var group in modules.GroupBy(kv => kv.Key.Room))
            foreach (var kv in group) if (kv.Value != primary[group.Key]) P.C.Link(kv.Value, primary[group.Key]);
        foreach (var (k, c, room) in Slots)
        {
            if (PieceOf(c.Z) == null) continue;
            Int3 m = Map(c);
            int mod = ModuleAt(room, c);
            if (mod < 0) continue;
            switch (k)
            {
                case SlotKind.Loot: P.Cache(m.X, m.Y, m.Z, mod); break;
                case SlotKind.Spawn: P.Spawn(m.X, m.Y, m.Z, mod); break;
                case SlotKind.Terminal: P.Terminal(m.X, m.Y, m.Z, mod); break;
            }
        }
        foreach (var (c, room) in Lamps)
        {
            if (PieceOf(c.Z) == null || !B.ContainsKey(c)) continue;
            Int3 m = Map(c);
            if (ModuleAt(room, c) is int lm and >= 0) P.Lamp(m.X, m.Y, m.Z, lm, 1.3f, 12f);
        }
        // The levels, at each deck's height by its stair.
        int sv = StairAt[TowerBay];
        for (int d = 1; d < D + TDecks; d++)
        {
            Int3 at = Map(new Int3(4, Walk(d), sv));
            if (P.C.X.Levels.All(l => l.Storey != d)) P.C.X.Levels.Add(new LevelInfo { Storey = d, WalkY = at.Y, Name = d < D ? $"deck {d}" : d == D ? "officers' deck" : "bridge" });
        }
        // The ways in from the ground: ramps up, cuts down.
        var clear = new HashSet<(int, int)>();
        int yardU = int.MinValue, yardV = 0;
        foreach (Way w in Ways)
        {
            if (w.Room < 0 || PieceOf(w.V0) is not { } p) continue;
            int room = ModuleAt(w.Room, new Int3(0, 0, w.V0));
            if (w.Side == 0)
            {
                // An open face: the ground runs straight in.
                ways.Add((room, Map(new Int3(0, Walk(0), w.V0)), w.What == "fore-face" ? Dir.South : Dir.North));
                continue;
            }
            int walk = Walk(0) + p.Dy(w.V0);
            int uo = (w.Side > 0 ? Half : -Half - 1) + p.OffU, s = w.Side;
            var cells = new List<(int, int, int)>();
            int n = Math.Abs(walk);
            for (int i = 0; i < n; i++)
            {
                int u = uo + s * i;
                for (int v = w.V0 + p.OffV; v < w.V0 + w.Width + p.OffV; v++)
                {
                    if (walk > 0)
                    {
                        int y = walk - 1 - i;
                        P.Put(u, y, v, new Block(BlockType.Ramp, Material.Steel, Trim.C.Scale(1.1f), Role.Floor, s > 0 ? Dir.West : Dir.East));
                        for (int f = 0; f < y; f++) P.Put(u, f, v, new Block(BlockType.Cube, Trim.M, Trim.C.Scale(0.8f), Role.Wall));
                        cells.Add((u, y, v));
                        for (int up = 1; up <= 3; up++) cells.Add((u, y + up, v));
                    }
                    else
                    {
                        int y = walk + i;
                        P.Put(u, y, v, new Block(BlockType.Ramp, Material.Rock, new Rgb(0x5a, 0x52, 0x48), Role.Floor, s > 0 ? Dir.East : Dir.West));
                        cells.Add((u, y, v));
                        for (int up = y + 1; up < Math.Max(0, y + 4); up++) { if (up < 0) P.Air(u, up, v); cells.Add((u, up, v)); }
                    }
                }
            }
            int foot = uo + s * n;
            for (int k = -2; k < n + 6; k++)
                for (int v = w.V0 - 3; v < w.V0 + w.Width + 3; v++) clear.Add((uo + s * k, v + p.OffV));
            if (cells.Count > 0)
            {
                int m = P.RoomOf(ModuleKind.Outdoor, walk > 0 ? "cargo-ramp" : "cutting", 0, cells.Distinct());
                outside.Add(m);
                P.C.Link(m, room);
            }
            else outside.Add(room);
            if (w.What == "cargo" || yardU == int.MinValue) { yardU = foot + s * 6; yardV = w.V0 + p.OffV + w.Width / 2; }
        }
        foreach (var (room, at, face) in ways)
        {
            outside.Add(room);
            for (int k = -2; k < 10; k++)
                for (int u = at.X - Half - 2; u < at.X + Half + 2; u++) clear.Add((u, at.Z + (face == Dir.North ? -k : k)));
            if (yardU == int.MinValue) { yardU = at.X + Half + 6; yardV = at.Z + (face == Dir.North ? -6 : 6); }
        }
        P.Approach(Div2(yardU), Div2(yardV), Dir.North);
        P.Yards(outside.Distinct().ToArray());
        for (int du = -4; du <= 4; du++) for (int dv = -4; dv <= 4; dv++) clear.Add((yardU + du, yardV + dv));
        P.C.VaultModule = primary[Vault];
        // No weather box: a steel hull is no house whose roof the years bring in; the states' own decay takes it.
        Debris(clear);
    }

    readonly List<(int Room, Int3 At, Dir Face)> ways = new();
    readonly List<int> outside = new();

    static int Div2(int a) => a >= 0 ? a / 2 : (a - 1) / 2;

    /// <summary>What the crash threw about: torn plates, a spill of cargo, scrap down the furrow behind it.</summary>
    void Debris(HashSet<(int, int)> clear)
    {
        var rng = R.Fork("debris");
        Piece last = Pieces[^1], first = Pieces[0];
        int stern = L + Nac + last.OffV, bow = first.OffV;
        var spots = new List<(int U, int V, int R)>();
        // Down the furrow behind it.
        for (int i = 0; i < 26; i++) spots.Add((last.OffU + rng.Range(-Half, Half), stern + 6 + rng.Range(0, 60), 2));
        // Along both flanks.
        for (int i = 0; i < 24; i++)
        {
            int v = rng.Range(0, L);
            Piece? p = PieceOf(v);
            if (p == null) continue;
            int s = rng.Chance(0.5f) ? 1 : -1;
            spots.Add((p.OffU + s * (Half + 3 + rng.Range(0, 18)), v + p.OffV, 1));
        }
        // Where it broke: the spill between the pieces, or out of the broken back.
        if (Cut >= 0)
            for (int i = 0; i < 30; i++) spots.Add((first.OffU + rng.Range(-Half, Half), Cut + first.OffV + rng.Range(2, last.OffV - first.OffV), 3));
        if (Break >= 0)
            for (int i = 0; i < 20; i++) spots.Add((first.OffU + (rng.Chance(0.5f) ? 1 : -1) * (Half + 2 + rng.Range(0, 14)), Break + first.OffV + rng.Range(-12, 12), 3));
        foreach (var (u, v, kind) in spots)
        {
            int n = 1 + rng.Range(0, kind + 1);
            for (int k = 0; k < n; k++)
            {
                int cu = u + rng.Range(-2, 3), cv = v + rng.Range(-2, 3);
                if (clear.Contains((cu, cv)) || P.C.Occ.Contains(P.Cell(cu, 0, cv)) || P.C.Blocks.ContainsKey(P.Cell(cu, 0, cv))) continue;
                if (P.C.Occ.Contains(P.Cell(cu, -1, cv))) continue;
                int pick = rng.Range(0, 10);
                Block b = pick switch
                {
                    < 3 => new Block(BlockType.Ramp, Plate.M, Plate.C.Scale(rng.Float(0.6f, 0.9f)), Role.Debris, Dirs.Horizontal[rng.Range(0, 4)]),
                    < 5 => new Block(BlockType.Corner, Plate.M, Plate.C.Scale(rng.Float(0.5f, 0.85f)), Role.Debris, Dirs.Horizontal[rng.Range(0, 4)]),
                    < 7 => new Block(BlockType.Rubble, Material.Scrap, new Rgb(0x5a, 0x50, 0x48), Role.Debris, Dir.North, (byte)rng.Range(0, 4)),
                    < 9 when kind == 3 => new Block(BlockType.Cube, Material.Steel, new[] { Orange.C, Teal.C, Hazard }[rng.Range(0, 3)].Scale(0.8f), Role.Debris),
                    _ => new Block(BlockType.Crate, Material.Scrap, new Rgb(0x6a, 0x5a, 0x48), Role.Debris, Dirs.Horizontal[rng.Range(0, 4)], 1),
                };
                P.Lay(cu, 0, cv, b);
            }
        }
    }
}
