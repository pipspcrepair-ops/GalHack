#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>Where a building stands on a site and what a site asks of it.</summary>
public sealed class BuildingSpec
{
    public ProgramDef Program = null!;
    public int X, Z, Rot;
    public int? W, D, Floors, Levels;
    public string? Style;
    /// <summary>This building holds the site's prize (the vault).</summary>
    public bool HoldsVault = true;
    /// <summary>The building may hold the core (one per site, and only if the family's core rule allows).</summary>
    public bool MayHoldCore = true;
    public string Name = "";
}

/// <summary>What a built building hands back to the site composer.</summary>
public sealed class BuiltBuilding
{
    public int EntranceModule, VaultModule = -1, DeepestModule = -1;
    public Int3 EntranceCell;
    public Dir EntranceFacing;
    public List<Int3> ExitCells = new();
    public Int3 Min, Max;            // world footprint (x, z) and storeys (y)
    public int Floors, Levels;
    public List<int> Modules = new();
    /// <summary>Each storey's plan signature: its footprint, circulation pattern, corridor topology and rooms.</summary>
    public List<(int Storey, string Signature)> Plans = new();
}

/// <summary>
/// Plans and builds one building from its purpose program, the way an architect would. The type
/// is the main driver of the layout; the seed varies it within the type.
///   1. massing: the program's grammar picks a footprint shape (a bar, L, T, U, H, cross or a
///      courtyard block), its wings and their lengths; upper storeys may set back and basements may
///      be bigger, smaller or the bar alone (a bunker shows only its stair house above ground);
///   2. vertical cores: a switchback stairwell (and maybe a lift, and a second stair in a long
///      building) at the same x and z through every storey they serve, on the main bar;
///   3. circulation, re-rolled per storey from the type's patterns: a spine, a spine that jogs, a
///      rotunda plaza, a ring corridor, an atrium with galleries round a void through the storeys,
///      open halls, an enfilade of rooms opening into each other, or mine drifts; corridors run
///      down every wing;
///   4. rooms: the space left between corridors is cut into rooms of sensible sizes, purposes are
///      assigned by a small constraint solver (size, position, near and avoid rules), doors are set
///      where a room meets a corridor or (enfilade) the next room, and anything left without a way in
///      becomes a hidden room behind a breakable wall or is walled up solid;
///   5. no two storeys alike: a storey whose plan signature matches one below it is re-planned;
///   6. then the features: blast doors, the vault at the deepest point, collapse breaches, a cavern,
///      holes in floors, roofs, landmarks, light and furniture (see BuildingRooms.cs).
/// Everything is cell-exact on the 1 m grid and seeded.
/// </summary>
public sealed partial class Building
{
    public const int Pitch = 4; // a storey: floor slab, three cells of headroom; the next slab is the ceiling

    readonly Canvas C;
    readonly PoiLibrary Lib;
    readonly ProgramDef P;
    readonly FamilyDef Fam;
    readonly Rarity Rarity;
    readonly Rng Rng;
    readonly BuildingSpec Spec;
    readonly Style S;

    // Massing: the bounding box, the main bar and its corridor row, the cores.
    int W, D, Wm, Dm, Zm, Zs, Cx, Lx, CoreEnd, Cx2 = -1, Floors, Levels, Top, Bottom, StairTop;
    bool Lift, Hut, RoofHut;
    string Shape = "rect", Basement = "same";
    readonly List<Bar> _bars = new();
    readonly Dictionary<int, bool[,]> _foot = new();
    readonly Dictionary<int, Plan> _plans = new();

    /// <summary>A rectangle of the footprint, walls included; wings carry their corridor's axis.</summary>
    sealed record Bar(int X0, int Z0, int X1, int Z1, bool Wing, bool AlongZ)
    {
        public bool Has(int x, int z) => x >= X0 && x <= X1 && z >= Z0 && z <= Z1;
    }

    // Plan cell kinds.
    const byte OutK = 0, Ext = 1, Wall = 2, Corr = 3, Free = 4, Core = 5, Void = 6, Solid = 7, Rail = 8, DoorK = 9;

    /// <summary>One storey's plan: what every cell of the footprint is.</summary>
    sealed class Plan
    {
        public int S, Y;
        public byte[,] K = null!;
        public int[,] R = null!;            // room index for Free cells
        public string Pattern = "spine";
        /// <summary>The ring this storey ran (loops, atriums, tiers): a tiered atrium stacks the same ring storey on storey.</summary>
        public (int a, int b, int z1, int z2) Ring;
        public List<RoomPlan> Rooms = new();
        public int Corridor = -1;           // its corridor network's module
        public List<(int x, int z)> Holes = new();
        public string Signature = "";
    }

    sealed class RoomPlan
    {
        public int Storey;
        public List<(int x, int z)> Cells = new();
        public int X0, Z0, X1, Z1;
        public RoomDef? Def;
        public bool Secret, Hall, Walled;
        public RoomPlan? SecretFrom;
        public List<(int x, int z)> SecretWall = new();
        public int Module = -1, Door = -1;
        public bool OnCorridor;
        public readonly HashSet<RoomPlan> Adj = new(), Across = new();
        public int Width => X1 - X0 + 1;
        public int Depth => Z1 - Z0 + 1;
        public int Area => Cells.Count;
        public string Purpose => Secret ? "hidden stash" : Def?.Id ?? "room";
        public (float x, float z) Mid => ((X0 + X1) / 2f, (Z0 + Z1) / 2f);
        public bool Contains(int x, int z) => Cells.Contains((x, z));
    }

    readonly Dictionary<int, int> _stair = new(), _stair2 = new();
    int _liftModule = -1, _roofModule = -1;
    List<Int3>? _roofDoorCells;
    readonly BuiltBuilding Out = new();

    Building(Canvas c, PoiLibrary lib, FamilyDef fam, Rarity rarity, BuildingSpec spec, Rng rng)
    {
        C = c;
        Lib = lib;
        Fam = fam;
        Rarity = rarity;
        Spec = spec;
        P = spec.Program;
        Rng = rng;
        S = spec.Style != null && lib.Kits.TryGetValue(spec.Style, out var k) ? new Style(k) : c.Style;
    }

    public static BuiltBuilding Build(Canvas c, PoiLibrary lib, FamilyDef fam, Rarity rarity, BuildingSpec spec, Rng rng)
    {
        var b = new Building(c, lib, fam, rarity, spec, rng);
        b.Run();
        return b.Out;
    }

    // ---- coordinates --------------------------------------------------------------------

    /// <summary>Local plan cell (x along the main bar, z across it) to world, turned by the site's rotation.</summary>
    Int3 L(int x, int y, int z)
    {
        int wx, wz;
        switch (Spec.Rot & 3)
        {
            case 1: wx = D - 1 - z; wz = x; break;
            case 2: wx = W - 1 - x; wz = D - 1 - z; break;
            case 3: wx = z; wz = W - 1 - x; break;
            default: wx = x; wz = z; break;
        }
        return new Int3(Spec.X + wx, y, Spec.Z + wz);
    }

    Dir R(Dir d) => d.RotateCw(Spec.Rot & 3);
    static int WalkY(int s) => s * Pitch;
    static readonly (int dx, int dz)[] Four = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    // ---- the plan ---------------------------------------------------------------------------

    void Run()
    {
        Massing();
        for (int s = Bottom; s <= Top; s++) _foot[s] = Footprint(s);
        for (int s = Bottom; s <= Top; s++)
        {
            if (Hut && s == 0) continue;
            PlanStorey(s);
        }
        // Shells first (slabs, walls, roofs), then each storey's insides, then the cores over them.
        for (int s = Bottom; s <= Top; s++) Shell(s);
        if (RoofHut) RoofHutShell();
        for (int s = Bottom; s <= Top; s++)
        {
            if (Hut && s == 0) HutStorey();
            else RealizeStorey(s);
        }
        Stairwell(Cx, _stair);
        if (Cx2 >= 0) Stairwell(Cx2, _stair2, second: true);
        if (Lift) LiftShaft();
        Entrance();
        RoomModules();
        AssignAll();
        BuildDoors();
        foreach (var plan in _plans.Values) foreach (var r in plan.Rooms.Where(r => r.Secret)) Hidden(r, plan);
        SeatVault();
        if ((Hut || Floors == 0) && Levels > 0 && Rng.Chance(P.Breach)) CollapseBreach();
        if (Hut) HutLandmarks(); // after the collapse, so nothing stands where the slab fell in
        Holes();
        foreach (var plan in _plans.Values.OrderBy(p => p.S))
            foreach (var r in plan.Rooms.Where(r => !r.Secret && !r.Walled)) Furnish(r, plan);
        foreach (var plan in _plans.Values) Notes(plan);
        Blasts();
        if (Rng.Chance(P.Cavern) && Levels > 0) Cavern();
        Roofscape();
        Levelinfo();
        Out.Floors = Floors;
        Out.Levels = Levels;
        Out.Min = Int3.Min(L(0, WalkY(Bottom) - 1, 0), L(W - 1, WalkY(Top) + 4, D - 1));
        Out.Max = Int3.Max(L(0, WalkY(Bottom) - 1, 0), L(W - 1, WalkY(Top) + 4, D - 1));
        AlienForm();   // after the bounds: alien forms grow round the building's footprint
        int index = C.X.Plans.Select(p => p.Building).DefaultIfEmpty(-1).Max() + 1;
        foreach (var p in _plans.Values.OrderBy(p => p.S))
        {
            Out.Plans.Add((p.S, p.Signature));
            C.X.Plans.Add((index, p.S, p.Signature));
        }
    }

    // ---- massing ------------------------------------------------------------------------------

    string Pick(List<(string Key, float Weight)> options, string fallback)
    {
        if (options.Count == 0) return fallback;
        int i = Rng.PickWeighted(options.Select(o => o.Weight).ToList());
        return i < 0 ? fallback : options[i].Key;
    }

    void Massing()
    {
        Wm = Spec.W ?? Rng.Between(P.WMin, P.WMax);
        Dm = Math.Max(16, Spec.D ?? Rng.Between(P.DMin, P.DMax));
        Floors = Spec.Floors ?? Rng.Between(P.FloorsMin, P.FloorsMax);
        Levels = Spec.Levels ?? Rng.Between(P.LevelsMin, P.LevelsMax);
        // Deeper and grander as rarity climbs: rare places go one level further down.
        if (Levels > 0 && Rarity >= Rarity.Rare && Spec.Levels == null) Levels++;
        Hut = P.Surface == "hut" || Floors == 0;
        if (Hut) Floors = 1;
        Top = Floors - 1;
        Bottom = -Levels;
        Shape = Spec.W != null ? "rect" : Pick(P.Shapes, "rect");
        Basement = Pick(P.Basements, "same");

        // Wings: their reach and width by shape. North wings push the main bar down.
        int wingW = Rng.Between(14, 16), reachS = Rng.Between(10, 18), reachN = Rng.Between(10, 16);
        bool north = Shape is "H" or "cross";
        Zm = north ? reachN : 0;
        Zs = Zm + Math.Max(7, Dm / 2 - 1);
        int maxCx = Wm - 10;
        Lift = Rng.Chance(P.Lift) && Floors + Levels >= 3 && Wm - 13 >= 6;
        if (Lift) maxCx = Wm - 13;
        // The stair sits a third of the way along, away from any wing that would cut the north band.
        Cx = Math.Clamp((int)(Wm * Rng.Float(0.22f, 0.4f)), 6, Math.Max(6, maxCx));
        Lx = Cx + 5;
        CoreEnd = Lift ? Cx + 7 : Cx + 4;
        if ((Rng.Chance(P.SecondStair) || Wm >= 40) && Wm - (CoreEnd + 1) >= 18 && !Hut) Cx2 = Wm - 10;
        if (Shape == "H")
        {
            // An H keeps all four wings: narrower wings, and the core between them on the cross bar.
            int core = CoreEnd - Cx + 1;
            wingW = Math.Max(9, Math.Min(wingW - 4, (Wm - core - 4) / 2));
            Cx = Math.Clamp((Wm - core) / 2, wingW + 1, Math.Max(wingW + 1, Wm - wingW - 2 - core + 1));
            Lx = Cx + 5;
            CoreEnd = Cx + core - 1;
            Cx2 = -1;
        }
        _bars.Add(new Bar(0, Zm, Wm - 1, Zm + Dm - 1, false, false));
        int x0 = Wm / 2 - wingW / 2;
        int zS = Zm + Dm - 1, zN = Zm;
        void South(int wx) => _bars.Add(new Bar(wx, zS, wx + wingW - 1, zS + reachS, true, true));
        void North(int wx) => _bars.Add(new Bar(wx, zN - reachN, wx + wingW - 1, zN, true, true));
        switch (Shape)
        {
            case "L": South(Rng.Chance(0.5f) ? 0 : Wm - wingW); break;
            case "T": South(x0); break;
            case "U": South(0); South(Wm - wingW); break;
            case "H": South(0); South(Wm - wingW); North(Wm - wingW); North(0); break;
            case "cross": South(x0); North(Math.Max(CoreEnd + 3, x0)); break;
            case "court": break; // the courtyard is cut out of the main bar per storey
        }
        // North wings must keep clear of the core column.
        _bars.RemoveAll(b => b.Wing && b.Z1 == zN && b.X0 <= CoreEnd + 1 && b.X1 >= Cx - 1);
        if (Cx2 >= 0) _bars.RemoveAll(b => b.Wing && b.Z1 == zN && b.X0 <= Cx2 + 5 && b.X1 >= Cx2 - 1);
        W = _bars.Max(b => b.X1) + 1;
        D = _bars.Max(b => b.Z1) + 1;
        Notches();
        RoofHut = !Hut && Floors >= 2 && P.Roof is "access" or "pad";
        StairTop = RoofHut ? Top + 1 : Top;
    }

    /// <summary>Corners a tower sheds as it climbs (terraces): from the storey each starts at, upward.</summary>
    readonly List<(int From, int X0, int Z0, int X1, int Z1)> _notches = new();

    void Notches()
    {
        if (Hut || P.Setback <= 0f || _bars.Any(b => b.Wing)) return;
        var corners = new List<int> { 0, 1, 2, 3 };
        for (int s = 2; s <= Top && corners.Count > 0 && _notches.Count < 3; s++)
        {
            if (!Rng.Chance(P.Setback * 0.7f)) continue;
            int nx = Rng.Between(5, Math.Max(5, Math.Min(10, Wm / 3))), nz = Rng.Between(4, Math.Max(4, Math.Min(8, Dm / 3)));
            int pick = corners[Rng.Range(0, corners.Count)];
            corners.Remove(pick);
            bool east = (pick & 1) != 0, south = (pick & 2) != 0;
            int x0 = east ? Wm - nx : 0, x1 = east ? Wm - 1 : nx - 1;
            int z0 = south ? Zm + Dm - nz : Zm, z1 = south ? Zm + Dm - 1 : Zm + nz - 1;
            // Never into a core, its landing or the courtyard ring.
            bool Hits(int a0, int a1) => x0 <= a1 && x1 >= a0 && z0 <= Zs + 3;
            if (Hits(Cx - 3, CoreEnd + 3) || (Cx2 >= 0 && Hits(Cx2 - 3, Cx2 + 7))) continue;
            if (z0 <= Zs + 3 && z1 >= Zs - 1) continue;
            _notches.Add((s, x0, z0, x1, z1));
        }
    }

    bool Courtyard => Shape == "court" && Dm >= 30 && Wm >= 30;
    (int a, int b, int z1, int z2) Ring => (6, Wm - 8, Zs, Zm + Dm - 8);

    /// <summary>A storey's outline: the bars it keeps (upper storeys may set back, basements differ).</summary>
    bool[,] Footprint(int s)
    {
        var f = new bool[W, D];
        if (Hut && s == 0)
        {
            var h = HutRect();
            for (int z = h.z0; z <= h.z1; z++)
                for (int x = h.x0; x <= h.x1; x++) f[x, z] = true;
            return f;
        }
        var bars = _bars.ToList();
        bool setback = s >= 2 && P.Setback > 0f && Hash.Cell(new Int3(s, (int)Spec.X, (int)Spec.Z), (uint)(Wm * 7 + Dm)) % 1000 / 1000f < P.Setback;
        if (s < 0 && Basement == "bar") bars = bars.Where(b => !b.Wing).ToList();
        if (setback && s >= 2) bars = bars.Where(b => !b.Wing || b.Z1 - b.Z0 < 12).ToList();
        foreach (var b in bars)
            for (int z = b.Z0; z <= b.Z1; z++)
                for (int x = b.X0; x <= b.X1; x++) f[x, z] = true;
        // The top torn off: the top storeys lose a growing wedge from the corner away from the stair,
        // more the higher they are, so the skyline breaks in steps (never into the core's rows).
        if (P.Crown == "broken" && !Hut && Top >= 5 && s >= Top - 3 && !(Fam.Style ?? "").StartsWith("alien_", StringComparison.Ordinal))
        {
            int d = s - (Top - 3);
            bool eastCorner = Cx < Wm / 2;
            float k = (Wm + Dm) * (0.22f + 0.13f * d);
            for (int z = Zs + 3; z < D; z++)
                for (int x = 0; x < W; x++)
                {
                    int u = eastCorner ? W - 1 - x : x, v = Zm + Dm - 1 - z;
                    if (u + v * 1.2f < k + (Hash.Cell(new Int3(x, s, z), 613) % 3)) f[x, z] = false;
                }
        }
        foreach (var n in _notches.Where(n => s >= n.From))
            for (int z = n.Z0; z <= n.Z1; z++)
                for (int x = n.X0; x <= n.X1; x++) f[x, z] = false;
        if (s < 0 && Basement == "box")
            for (int z = Zm; z < Zm + Dm; z++)
                for (int x = 0; x < W; x++) f[x, z] = true;
        if (Courtyard && !(s < 0 && Basement != "same"))
        {
            // The courtyard: the middle of the ring is open to the sky.
            var (a, b2, z1, z2) = Ring;
            for (int z = z1 + 3; z <= z2 - 2; z++)
                for (int x = a + 3; x <= b2 - 2; x++) f[x, z] = false;
        }
        return f;
    }

    (int x0, int z0, int x1, int z1) HutRect() => (Cx - 2, Math.Max(Zm, Zs - 7), CoreEnd + 1, Zs + 2);

    bool InFoot(int s, int x, int z) => x >= 0 && z >= 0 && x < W && z < D && _foot.TryGetValue(s, out var f) && f[x, z];

    // ---- circulation --------------------------------------------------------------------------

    void PlanStorey(int s)
    {
        // Every storey is its own: re-plan (another pattern, other cuts) while it matches one already planned.
        Plan? plan = null;
        var used = new HashSet<string>(_plans.Values.Select(p => p.Signature));
        for (int tries = 0; tries < 8; tries++)
        {
            plan = TryPlan(s, tries);
            if (!used.Contains(plan.Signature)) break;
        }
        _plans[s] = plan!;
    }

    Plan TryPlan(int s, int attempt)
    {
        var f = _foot[s];
        var plan = new Plan { S = s, Y = WalkY(s), K = new byte[W, D], R = new int[W, D] };
        var K = plan.K;
        for (int z = 0; z < D; z++)
            for (int x = 0; x < W; x++)
            {
                plan.R[x, z] = -1;
                if (!f[x, z]) { K[x, z] = OutK; continue; }
                bool edge = false;
                for (int dz = -1; dz <= 1 && !edge; dz++)
                    for (int dx = -1; dx <= 1 && !edge; dx++)
                        if (!InFoot(s, x + dx, z + dz)) edge = true;
                K[x, z] = edge ? Ext : Free;
            }
        // The cores: a stair column (and a lift beside it, and maybe a second stair), solid behind.
        void CoreRect(int x0, int x1, int z0)
        {
            for (int z = Math.Max(Zm, z0); z <= Zs - 1; z++)
                for (int x = x0; x <= x1; x++)
                    if (K[x, z] != OutK) K[x, z] = Core;
            for (int z = Zm + 1; z < z0; z++)
                for (int x = x0; x <= x1; x++)
                    if (K[x, z] == Free) K[x, z] = Solid;
        }
        CoreRect(Cx - 1, Cx + 4, Zs - 7);
        if (Lift) CoreRect(Cx + 4, Cx + 7, Zs - 4);
        if (Cx2 >= 0) CoreRect(Cx2 - 1, Cx2 + 4, Zs - 7);

        // The type's circulation for this storey (the ground floor needs a way in from the front).
        var options = P.Plans.Where(o => Fits(o.Key, s)).ToList();
        if (options.Count == 0) options.Add(("spine", 1f));
        var rng = Rng.Fork($"plan{s}:{attempt}");
        // A building works through its repertoire: a circulation already used on another storey is less likely again.
        var usedPatterns = _plans.Values.Select(p => p.Pattern).ToList();
        // (Tiers are meant to repeat: a tiered atrium is galleries stacked round one void.)
        string pattern = options[Math.Max(0, rng.PickWeighted(options.Select(o => o.Weight * (o.Key == "tiers" ? 1f : MathF.Pow(0.3f, usedPatterns.Count(u => u == o.Key)))).ToList()))].Key;
        if (attempt > 0 && options.Count > 1 && _plans.TryGetValue(s + 1, out var above) && above.Pattern == pattern)
            pattern = options[(options.FindIndex(o => o.Key == pattern) + attempt) % options.Count].Key;
        // Round a courtyard the ring itself varies: a full cloister walk, a horseshoe open on one side, or spokes out to the street.
        if (pattern == "court")
        {
            string[] forms = { "court", "cloister", "horseshoe" };
            pattern = forms[Math.Max(0, rng.PickWeighted(forms.Select(fm => MathF.Pow(0.3f, usedPatterns.Count(u => u == fm))).ToList()))];
        }
        plan.Pattern = pattern;

        var corr = new HashSet<(int, int)>();
        void Run(int x0, int z0, int x1, int z1)
        {
            for (int z = Math.Min(z0, z1); z <= Math.Max(z0, z1); z++)
                for (int x = Math.Min(x0, x1); x <= Math.Max(x0, x1); x++)
                    if (x >= 0 && z >= 0 && x < W && z < D && K[x, z] == Free) corr.Add((x, z));
        }
        // The landing in front of every core, on every storey.
        Run(Cx, Zs, CoreEnd, Zs + 1);
        if (Cx2 >= 0) Run(Cx2, Zs, Cx2 + 3, Zs + 1);
        bool ground = s == 0;
        int from = 1, to = Wm - 2;
        // The ring this storey runs (round the courtyard it is fixed; otherwise its size and form vary storey to storey).
        var ring = Ring;
        int ringForm = 0;
        if (pattern is "loop" or "atrium" or "tiers")
        {
            ring = pattern == "tiers" && _plans.TryGetValue(s - 1, out var tierBelow) && tierBelow.Pattern == "tiers"
                ? tierBelow.Ring
                : (rng.Between(4, 9), Wm - rng.Between(7, 11), Zs, Zm + Dm - rng.Between(6, 9));
            if (pattern == "loop") ringForm = rng.Between(0, 2); // 0 a full ring, 1 a horseshoe, 2 a figure of eight
        }
        plan.Ring = ring;
        switch (pattern)
        {
            case "spine":
                Run(from, Zs, to, Zs + 1);
                break;
            case "jog":
            {
                // The spine steps sideways once, away from the core's landing.
                bool west = s != 0 && Cx > Wm - CoreEnd; // the ground floor keeps its straight run from the front door
                int at = west ? rng.Between(3, Math.Max(3, Cx - 3)) : rng.Between(Math.Min(to - 3, CoreEnd + 3), to - 3);
                int shift = rng.Chance(0.5f) ? 3 : 4;
                int z2 = Zs + shift;
                if (west) { Run(at, z2, from, z2 + 1); Run(at, Zs, at + 1, z2 + 1); Run(at, Zs, to, Zs + 1); }
                else { Run(from, Zs, at + 1, Zs + 1); Run(at, Zs, at + 1, z2 + 1); Run(at, z2, to, z2 + 1); }
                break;
            }
            case "plaza":
            {
                // A rotunda: the spine widens into a square where the ways meet.
                Run(from, Zs, to, Zs + 1);
                int px = Wings(s).FirstOrDefault()?.X0 + 4 ?? rng.Between(CoreEnd + 3, Math.Max(CoreEnd + 3, to - 8));
                px = Math.Clamp(px, CoreEnd + 2, Math.Max(CoreEnd + 2, to - 6));
                Run(px, Zs, px + 5, Zs + 5);
                break;
            }
            case "loop":
            case "atrium":
            case "tiers":
            case "court":
            case "cloister":
            case "horseshoe":
            {
                var (a, b, z1, z2) = ring;
                if (pattern == "horseshoe") ringForm = 1;
                if (pattern == "cloister")
                {
                    int mz = (z1 + z2) / 2;
                    Run(from, mz, a, mz + 1);
                    Run(b, mz, to, mz + 1);
                    Run((a + b) / 2, z2, (a + b) / 2 + 1, Zm + Dm - 2);
                }
                Run(from, z1, to, z1 + 1);
                Run(a, z2, b + 1, z2 + 1);
                bool westLeg = ringForm != 1 || rng.Chance(0.5f);
                if (westLeg) Run(a, z1, a + 1, z2 + 1);
                if (!westLeg || ringForm != 1) Run(b, z1, b + 1, z2 + 1);
                if (ringForm == 2) Run((a + b) / 2, z1, (a + b) / 2 + 1, z2 + 1);
                // Spokes from the ring out to the far façade.
                if (pattern == "loop" && rng.Chance(0.5f)) Run((a + b) / 2, z2, (a + b) / 2 + 1, Zm + Dm - 2);
                break;
            }
            case "hall":
                Run(ground ? from : Cx - 2, Zs, CoreEnd + 2, Zs + 1);
                break;
            case "enfilade":
                Run(Cx, Zs, CoreEnd, Zs + 1);
                break;
            case "radial":
            {
                // A hub in the middle with spokes out to every side: the blocks hang off it.
                int hx = Math.Clamp(Wm / 2, CoreEnd + 5, Math.Max(CoreEnd + 5, to - 6)), hz = Math.Clamp(Zm + Dm / 2, Zs + 3, Math.Max(Zs + 3, Zm + Dm - 6));
                Run(hx - 3, hz - 2, hx + 3, hz + 3);
                Run(from, hz, to, hz + 1);
                Run(hx, Zm + 1, hx + 1, Zm + Dm - 2);
                break;
            }
            case "racetrack":
            {
                // A ward loop: two parallel corridors joined at both ends, rooms inside and out.
                int z2 = Math.Min(Zs + 8, Zm + Dm - 4);
                Run(from, Zs, to, Zs + 1);
                Run(from + 2, z2, to - 2, z2 + 1);
                Run(from + 2, Zs, from + 3, z2 + 1);
                Run(to - 3, Zs, to - 2, z2 + 1);
                break;
            }
            case "hub":
                // A hall round the stair core; the wings branch straight off it.
                Run(Cx - 3, Zs, CoreEnd + 3, Zs + 4);
                break;
            case "branches":
            case "drifts":
            {
                Run(from, Zs, to, Zs + 1);
                // Drifts branch off the main gallery, each to a stope at its end.
                for (int i = 0, n = rng.Between(2, 4); i < n; i++)
                {
                    int x = rng.Between(2, to - 2);
                    if (x >= Cx - 2 && x <= CoreEnd + 2) continue;
                    bool northward = rng.Chance(0.4f);
                    if (northward) Run(x, Zm + 2, x + 1, Zs);
                    else Run(x, Zs, x + 1, Zm + Dm - 3);
                }
                break;
            }
        }
        // A corridor down every wing this storey has.
        foreach (var w in Wings(s))
        {
            int cx = (w.X0 + w.X1) / 2 - 1;
            if (w.Z0 > Zs) Run(cx, Zs, cx + 1, w.Z1 - 2);
            else Run(cx, w.Z0 + 2, cx + 1, Zs + 1);
        }
        // Every stretch of corridor joins the stair landing: a wing's corridor on a hall or enfilade
        // storey runs along the main bar to it.
        JoinCorridors(corr, K, Run);

        // The courtyard's inner block (open to the sky) or the atrium's void.
        if (pattern is "atrium" or "tiers" && s > Math.Max(0, Bottom) && s >= 1)
        {
            var (a, b, z1, z2) = ring;
            for (int z = z1 + 3; z <= z2 - 2; z++)
                for (int x = a + 3; x <= b - 2; x++)
                    if (K[x, z] == Free && (!_plans.TryGetValue(s - 1, out var under) || under.K[x, z] is Free or Void)) K[x, z] = Void; // open over rooms, never over a corridor
        }
        foreach (var (x, z) in corr) K[x, z] = Corr;
        // Walls round every corridor (and rails round a void, so the galleries overlook it).
        foreach (var (x, z) in corr)
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, nz = z + dz;
                    if (nx < 0 || nz < 0 || nx >= W || nz >= D) continue;
                    if (K[nx, nz] == Free) K[nx, nz] = Wall;
                }
        for (int z = 0; z < D; z++)
            for (int x = 0; x < W; x++)
            {
                if (K[x, z] != Wall) continue;
                bool overVoid = Four.Any(d => x + d.dx >= 0 && z + d.dz >= 0 && x + d.dx < W && z + d.dz < D && K[x + d.dx, z + d.dz] == Void);
                if (overVoid) K[x, z] = Rail;
            }
        Partition(plan, rng, pattern);
        plan.Signature = Signature(plan);
        return plan;
    }

    void JoinCorridors(HashSet<(int x, int z)> corr, byte[,] K, Action<int, int, int, int> run)
    {
        for (int guard = 0; guard < 6; guard++)
        {
            var seen = new HashSet<(int, int)> { (Cx, Zs) };
            var q = new Queue<(int x, int z)>();
            q.Enqueue((Cx, Zs));
            while (q.Count > 0)
            {
                var (x, z) = q.Dequeue();
                foreach (var (dx, dz) in Four)
                    if (corr.Contains((x + dx, z + dz)) && seen.Add((x + dx, z + dz))) q.Enqueue((x + dx, z + dz));
            }
            var lost = corr.Where(c => !seen.Contains(c)).OrderBy(c => Math.Abs(c.z - Zs) + Math.Abs(c.x - Cx)).ToList();
            if (lost.Count == 0) return;
            var (lx, lz) = lost[0];
            // Along the main bar's corridor row to the landing, then across to the stranded piece.
            run(Math.Min(lx, Cx), Zs, Math.Max(lx + 1, CoreEnd), Zs + 1);
            run(lx, Math.Min(lz, Zs), lx + 1, Math.Max(lz, Zs + 1));
        }
    }

    bool Fits(string pattern, int s) => pattern switch
    {
        "atrium" when Floors < 2 => false,
        "tiers" when Floors < 2 => false,
        "loop" or "atrium" or "tiers" => Dm >= 30 && Wm >= 30 && !Courtyard,
        "racetrack" => Zm + Dm - 4 - Zs >= 7 && !Courtyard,
        "radial" => Dm >= 18 && Wm >= 24 && !Courtyard,
        "court" or "cloister" or "horseshoe" => Courtyard,
        "enfilade" => s != 0,
        _ => !(Courtyard && pattern != "court"),
    };

    IEnumerable<Bar> Wings(int s) => _bars.Where(b => b.Wing && InFoot(s, (b.X0 + b.X1) / 2, (b.Z0 + b.Z1) / 2));

    // ---- rooms from the space left ------------------------------------------------------------

    /// <summary>
    /// Cuts the free space into rooms: each connected stretch is split by walls, the longer way,
    /// until its pieces are room-sized (halls stay big). Slivers become hidden rooms or solid.
    /// </summary>
    void Partition(Plan plan, Rng rng, string pattern)
    {
        var K = plan.K;
        // High tech plans its rooms three to four times larger (rule P2: a median near 90 m2, 6 m across and more).
        bool high = C.Tech == TechLevel.High;
        int maxW = pattern == "hall" ? 22 : high ? 15 : 11, maxD = pattern == "hall" ? 18 : high ? 12 : 9, maxA = pattern == "hall" ? 300 : high ? 160 : 84;
        var pieces = new List<List<(int x, int z)>>();
        foreach (var comp in Components(K, Free))
            SplitInto(comp, pieces, K, rng, maxW, maxD, maxA);
        // A hall the type asks for on this storey: the biggest stretch stays whole.
        string? hall = P.Halls.Where(h => BandKeys(plan.S).Contains(h.Band)).Select(h => h.Room).FirstOrDefault();
        foreach (var piece in pieces)
        {
            var r = MakeRoom(plan.S, piece);
            if (piece.Count < 12 || r.Width < 3 || r.Depth < 3)
            {
                if (piece.Count >= 6 && r.Width >= 2 && r.Depth >= 2) r.Secret = true;
                else { foreach (var (x, z) in piece) K[x, z] = Solid; continue; }
            }
            plan.Rooms.Add(r);
        }
        // (Only if the storey has a room big enough to be one: a small storey's biggest room is no hall.)
        if (hall != null && plan.Rooms.Count > 0 && plan.Rooms.Where(r => !r.Secret).MaxBy(r => r.Area) is { } biggest && biggest.Area >= Lib.Room(hall).AMin * 0.75f)
        {
            biggest.Def = Lib.Room(hall);
            biggest.Hall = true;
        }
        // Mines keep only the stopes at the drifts: the rest is rock.
        if (pattern == "drifts")
            foreach (var r in plan.Rooms.Where(r => !r.Secret && !Touches(K, r, Corr)).ToList())
            {
                foreach (var (x, z) in r.Cells) K[x, z] = Solid;
                plan.Rooms.Remove(r);
            }
        for (int i = 0; i < plan.Rooms.Count; i++)
            foreach (var (x, z) in plan.Rooms[i].Cells) plan.R[x, z] = i;
        // Requested hidden rooms: a sliver cut off the end of a big room, a gap the plan doesn't explain.
        int wanted = plan.S == Bottom || rng.Chance(0.4f) ? rng.Between(P.SecretsMin, P.SecretsMax) : 0;
        foreach (var big in plan.Rooms.Where(r => !r.Secret && !r.Hall && r.Width >= 9 && r.Depth >= 4).OrderBy(_ => rng.Float()).ToList())
        {
            if (plan.Rooms.Count(r => r.Secret) >= wanted) break;
            int cut = rng.Chance(0.5f) ? big.X0 + 3 : big.X1 - 3;
            var line = big.Cells.Where(c => c.x == cut).ToList();
            var side = big.Cells.Where(c => cut == big.X0 + 3 ? c.x < cut : c.x > cut).ToList();
            var rest = big.Cells.Where(c => cut == big.X0 + 3 ? c.x > cut : c.x < cut).ToList();
            if (side.Count < 6 || !Connected(side) || !Connected(rest)) continue;
            foreach (var (x, z) in line) K[x, z] = Wall;
            int idx = plan.Rooms.IndexOf(big);
            var keep = MakeRoom(plan.S, rest);
            keep.Def = big.Def;
            keep.Hall = big.Hall;
            plan.Rooms[idx] = keep;
            var secret = MakeRoom(plan.S, side);
            secret.Secret = true;
            plan.Rooms.Add(secret);
        }
        for (int z = 0; z < D; z++)
            for (int x = 0; x < W; x++) plan.R[x, z] = -1;
        for (int i = 0; i < plan.Rooms.Count; i++)
            foreach (var (x, z) in plan.Rooms[i].Cells) plan.R[x, z] = i;
        Adjacency(plan);
    }

    static RoomPlan MakeRoom(int s, List<(int x, int z)> cells) => new()
    {
        Storey = s, Cells = cells,
        X0 = cells.Min(c => c.x), X1 = cells.Max(c => c.x), Z0 = cells.Min(c => c.z), Z1 = cells.Max(c => c.z),
    };

    List<List<(int x, int z)>> Components(byte[,] K, byte kind)
    {
        var seen = new bool[W, D];
        var list = new List<List<(int, int)>>();
        for (int z = 0; z < D; z++)
            for (int x = 0; x < W; x++)
            {
                if (seen[x, z] || K[x, z] != kind) continue;
                var comp = new List<(int, int)>();
                var q = new Queue<(int x, int z)>();
                q.Enqueue((x, z));
                seen[x, z] = true;
                while (q.Count > 0)
                {
                    var (cx, cz) = q.Dequeue();
                    comp.Add((cx, cz));
                    foreach (var (dx, dz) in Four)
                    {
                        int nx = cx + dx, nz = cz + dz;
                        if (nx < 0 || nz < 0 || nx >= W || nz >= D || seen[nx, nz] || K[nx, nz] != kind) continue;
                        seen[nx, nz] = true;
                        q.Enqueue((nx, nz));
                    }
                }
                list.Add(comp);
            }
        return list;
    }

    static bool Connected(List<(int x, int z)> cells)
    {
        if (cells.Count == 0) return false;
        var set = new HashSet<(int, int)>(cells);
        var seen = new HashSet<(int, int)> { cells[0] };
        var q = new Queue<(int x, int z)>();
        q.Enqueue(cells[0]);
        while (q.Count > 0)
        {
            var (x, z) = q.Dequeue();
            foreach (var (dx, dz) in Four)
                if (set.Contains((x + dx, z + dz)) && seen.Add((x + dx, z + dz))) q.Enqueue((x + dx, z + dz));
        }
        return seen.Count == cells.Count;
    }

    void SplitInto(List<(int x, int z)> comp, List<List<(int x, int z)>> pieces, byte[,] K, Rng rng, int maxW, int maxD, int maxA)
    {
        int x0 = comp.Min(c => c.x), x1 = comp.Max(c => c.x), z0 = comp.Min(c => c.z), z1 = comp.Max(c => c.z);
        int w = x1 - x0 + 1, d = z1 - z0 + 1;
        if (w <= maxW && d <= maxD && comp.Count <= maxA) { pieces.Add(comp); return; }
        foreach (bool alongX in w >= d ? new[] { true, false } : new[] { false, true })
        {
            int lo = (alongX ? x0 : z0) + 4, hi = (alongX ? x1 : z1) - 4;
            if (hi < lo) continue;
            var positions = Enumerable.Range(lo, hi - lo + 1).ToList();
            rng.Shuffle(positions);
            // Prefer cuts near the middle, so rooms come out even.
            positions = positions.OrderBy(p => Math.Abs(p - (lo + hi) / 2) + rng.Float(0f, 3f)).ToList();
            foreach (int pos in positions)
            {
                var line = comp.Where(c => (alongX ? c.x : c.z) == pos).ToList();
                var a = comp.Where(c => (alongX ? c.x : c.z) < pos).ToList();
                var b = comp.Where(c => (alongX ? c.x : c.z) > pos).ToList();
                if (a.Count < 12 || b.Count < 12 || !Connected(a) || !Connected(b)) continue;
                foreach (var (x, z) in line) K[x, z] = Wall;
                SplitInto(a, pieces, K, rng, maxW, maxD, maxA);
                SplitInto(b, pieces, K, rng, maxW, maxD, maxA);
                return;
            }
        }
        pieces.Add(comp);
    }

    bool Touches(byte[,] K, RoomPlan r, byte kind)
    {
        foreach (var (x, z) in r.Cells)
            foreach (var (dx, dz) in Four)
            {
                int wx = x + dx, wz = z + dz, ox = x + 2 * dx, oz = z + 2 * dz;
                if (ox < 0 || oz < 0 || ox >= W || oz >= D) continue;
                if (K[wx, wz] == Wall && K[ox, oz] == kind) return true;
            }
        return false;
    }

    /// <summary>Which rooms share a wall, and which face each other across a corridor.</summary>
    void Adjacency(Plan plan)
    {
        var K = plan.K;
        foreach (var r in plan.Rooms)
            foreach (var (x, z) in r.Cells)
                foreach (var (dx, dz) in Four)
                {
                    int wx = x + dx, wz = z + dz;
                    if (wx < 0 || wz < 0 || wx >= W || wz >= D || K[wx, wz] != Wall) continue;
                    int ox = x + 2 * dx, oz = z + 2 * dz;
                    if (ox >= 0 && oz >= 0 && ox < W && oz < D && plan.R[ox, oz] >= 0 && plan.R[ox, oz] != plan.Rooms.IndexOf(r))
                        r.Adj.Add(plan.Rooms[plan.R[ox, oz]]);
                    if (ox >= 0 && oz >= 0 && ox < W && oz < D && K[ox, oz] == Corr)
                    {
                        r.OnCorridor = true;
                        // Across the corridor: walk on through it to the wall and the room beyond.
                        int k = 2;
                        while (k < 6 && x + k * dx >= 0 && z + k * dz >= 0 && x + k * dx < W && z + k * dz < D && K[x + k * dx, z + k * dz] == Corr) k++;
                        int bx = x + (k + 1) * dx, bz = z + (k + 1) * dz;
                        if (bx >= 0 && bz >= 0 && bx < W && bz < D && plan.R[bx, bz] >= 0) r.Across.Add(plan.Rooms[plan.R[bx, bz]]);
                    }
                }
    }

    /// <summary>A storey's plan in short: its outline, circulation, corridor topology and rooms.</summary>
    string Signature(Plan plan)
    {
        var K = plan.K;
        int corr = 0, junctions = 0, ends = 0, area = 0;
        for (int z = 0; z < D; z++)
            for (int x = 0; x < W; x++)
            {
                if (K[x, z] != OutK) area++;
                if (K[x, z] != Corr) continue;
                corr++;
                int n = Four.Count(d => x + d.dx >= 0 && z + d.dz >= 0 && x + d.dx < W && z + d.dz < D && K[x + d.dx, z + d.dz] == Corr);
                if (n >= 4) junctions++;
                if (n <= 1) ends++;
            }
        var rooms = string.Join(",", plan.Rooms.OrderBy(r => r.X0).ThenBy(r => r.Z0).Select(r => $"{r.X0}.{r.Z0}.{r.Width}x{r.Depth}{(r.Secret ? "s" : "")}"));
        return $"{Shape}/{plan.Pattern}/a{area}/c{corr}/j{junctions}/e{ends}/r{plan.Rooms.Count}/h{plan.Holes.Count}/{rooms}";
    }

    // ---- building it ----------------------------------------------------------------------------

    Block Armoured(Int3 p, Role role = Role.Wall)
    {
        var b = S.Wall.Make(p);
        b.Role = role;
        b.Material = Material.Armour;
        b.Color = Facade.Armoured(b.Color);
        return b;
    }

    Block InnerWall(Int3 p) => S.InnerWall.Make(p);

    /// <summary>Cells from one pilaster to the next along an outer wall.</summary>
    const int Bay = 5;

    /// <summary>Slabs (each storey's floor covers its outline and the one below), outer walls, roofs and parapets.</summary>
    void Shell(int s)
    {
        int y = WalkY(s);
        var plan = _plans.GetValueOrDefault(s);
        for (int z = 0; z < D; z++)
            for (int x = 0; x < W; x++)
            {
                bool here = InFoot(s, x, z), below = s > Bottom && InFoot(s - 1, x, z);
                if (!here && !below) continue;
                if (plan != null && plan.K[x, z] == Void) continue;   // an atrium's void runs down through the floor
                Int3 p = L(x, y - 1, z);
                // A faction armours its foundations, so tunnelling in from below is slow work.
                C.Set(p, s == Bottom && Fam.Core == CoreRule.Owned ? Armoured(p, Role.Floor) : S.Floor.Make(p));
            }
        bool above = s >= 0;
        for (int z = 0; z < D; z++)
            for (int x = 0; x < W; x++)
            {
                if (!InFoot(s, x, z)) continue;
                bool ring = !InFoot(s, x - 1, z) || !InFoot(s, x + 1, z) || !InFoot(s, x, z - 1) || !InFoot(s, x, z + 1)
                            || !InFoot(s, x - 1, z - 1) || !InFoot(s, x + 1, z + 1) || !InFoot(s, x - 1, z + 1) || !InFoot(s, x + 1, z - 1);
                for (int k = 0; k < 3; k++)
                {
                    Int3 p = L(x, y + k, z);
                    if (!ring) { C.Clear(p); continue; }
                    int open = (InFoot(s, x - 1, z) ? 0 : 1) + (InFoot(s, x + 1, z) ? 0 : 1) + (InFoot(s, x, z - 1) ? 0 : 1) + (InFoot(s, x, z + 1) ? 0 : 1);
                    bool corner = open >= 2 || open == 0;
                    // A wall is laid out in bays along its own length: a pilaster, then a window pair
                    // in the middle of the bay, on as many bays as the program wants windows.
                    bool runsX = InFoot(s, x - 1, z) && InFoot(s, x + 1, z);
                    int along = runsX ? x : z, bay = along % Bay;
                    bool pilaster = above && !corner && open == 1 && bay == 0 && !(Hut && s == 0);
                    bool glazed = above && !corner && open == 1 && bay is 2 or 3 && k >= 1
                                  && (Hash.Cell(new Int3(along / Bay, s, runsX ? z : x), 31) % 1000) / 1000f < 1f - (1f - P.Windows) * (1f - P.Windows);
                    if (P.Cab && s == Top && above && !corner && !pilaster) C.Set(p, S.Window.Make(p));
                    else if (pilaster)
                    {
                        var pb = S.Wall.Make(p);
                        pb.Color = Facade.Pilaster(pb.Color);
                        C.Set(p, pb);
                    }
                    else if (!above && Fam.Core == CoreRule.Owned) C.Set(p, Armoured(p));
                    else if (glazed) C.Set(p, S.Window.Make(p));
                    else C.Set(p, S.Wall.Make(p));
                }
                // The roof over whatever has no storey above it, with a parapet round its edge.
                bool roofed = s == Top || !InFoot(s + 1, x, z);
                if (!roofed || Hut && s == 0 && s != Top) continue;
                Int3 r = L(x, y + 3, z);
                if (s == Top || !InFoot(s + 1, x, z)) C.Set(r, S.Roof.Make(r));
                if (ring && above && !(Hut && s == 0)) C.Set(r + Int3.Up, S.Wall.Make(r + Int3.Up));
            }
    }

    void RoofHutShell()
    {
        int s = Top + 1, y = WalkY(s);
        var f = (x0: Cx - 1, z0: Math.Max(Zm, Zs - 7), x1: Cx + 4, z1: Zs - 1);
        for (int z = f.z0; z <= f.z1; z++)
            for (int x = f.x0; x <= f.x1; x++)
            {
                bool ring = x == f.x0 || x == f.x1 || z == f.z0 || z == f.z1;
                for (int k = 0; k < 3; k++)
                {
                    Int3 p = L(x, y + k, z);
                    if (ring) C.Set(p, S.Wall.Make(p));
                    else C.Clear(p);
                }
                Int3 r = L(x, y + 3, z);
                C.Set(r, S.Roof.Make(r));
            }
        var cells = new List<Int3>();
        for (int x = Cx + 2; x <= Cx + 3; x++)
            for (int k = 0; k < DoorSize.Tall; k++) cells.Add(L(x, y + k, Zs - 1));
        _roofDoorCells = cells;
    }

    /// <summary>A storey's insides from its plan: corridors and rooms open, walls, rails and solid fill.</summary>
    void RealizeStorey(int s)
    {
        var plan = _plans[s];
        int y = plan.Y;
        var corr = new List<Int3>();
        for (int z = 0; z < D; z++)
            for (int x = 0; x < W; x++)
            {
                byte k = plan.K[x, z];
                for (int h = 0; h < 3; h++)
                {
                    Int3 p = L(x, y + h, z);
                    switch (k)
                    {
                        case Corr:
                            C.Clear(p);
                            corr.Add(p);
                            if (h == 0) C.X.CorridorCells.Add(p);
                            break;
                        case Free:
                        case Void:
                            C.Clear(p);
                            break;
                        case Wall:
                        case Solid:
                            C.Set(p, InnerWall(p));
                            break;
                        case Rail:
                            if (h == 0) C.Set(p, new Block(BlockType.Rail, Material.Steel, S.AccentColor.Scale(0.8f), Role.Decor));
                            else C.Clear(p);
                            break;
                    }
                }
                if (k == Void) C.Clear(L(x, y - 1, z));
            }
        plan.Corridor = C.AddModule(ModuleKind.Corridor, plan.Pattern == "drifts" ? "drift" : "corridor", s, corr);
        Out.Modules.Add(plan.Corridor);
        // Corridor lamps: a sconce in a wall beside the corridor every few metres, a light between them.
        int n = 0;
        foreach (Int3 c in corr.Where(c => c.Y == y).OrderBy(c => c.X).ThenBy(c => c.Z))
        {
            if (n++ % 7 != 3) continue;
            C.X.Lights.Add(new LightSpot { Cell = c + Int3.Up * 2, Color = S.Kit.Light, Mode = LightMode.Steady, Energy = 1.0f, Range = 7f, Module = plan.Corridor, Storey = s });
            foreach (Dir d in Dirs.Horizontal)
            {
                Int3 sc = c + d.Vec() + Int3.Up * 2;
                // A lamp in the outer wall would show outside as one bright tile.
                if (C.Get(sc).Role == Role.Wall && C.Get(sc).Type == BlockType.Cube && C.Occ.Contains(sc + d.Vec())) { C.Set(sc, S.Light.Make(sc)); break; }
            }
        }
    }

    void HutStorey()
    {
        int y = WalkY(0);
        var spine = new List<Int3>();
        for (int x = Cx - 1; x <= CoreEnd; x++)
            for (int z = Zs; z <= Zs + 1; z++)
                for (int k = 0; k < 3; k++)
                {
                    Int3 p = L(x, y + k, z);
                    C.Clear(p);
                    spine.Add(p);
                    if (k == 0) C.X.CorridorCells.Add(p);
                }
        var f = HutRect();
        for (int x = f.x0 + 1; x < f.x1; x++)
            for (int z = f.z0 + 1; z <= Zs - 1; z++)
                for (int k = 0; k < 3; k++)
                {
                    bool stair = x >= Cx && x <= Cx + 3 && z >= Zs - 6 && z <= Zs - 2;
                    bool shaft = Lift && x >= Lx && x <= Lx + 1 && z >= Zs - 3 && z <= Zs - 2;
                    if (!stair && !shaft) C.Set(L(x, y + k, z), InnerWall(L(x, y + k, z)));
                }
        int hut = C.AddModule(ModuleKind.Entrance, "stair house", 0, spine);
        _plans[0] = new Plan { S = 0, Y = 0, Corridor = hut, Pattern = "hut", Signature = "hut" };
        Out.Modules.Add(hut);
        C.X.Lights.Add(new LightSpot { Cell = L(Cx + 1, y + 2, Zs), Color = S.Kit.Light, Mode = LightMode.Steady, Energy = 1.1f, Range = 8f, Module = hut });
    }

    // ---- vertical cores -----------------------------------------------------------------

    /// <summary>
    /// The switchback stair: lane A climbs two steps away from the corridor to a landing, lane B
    /// climbs two more back toward it and arrives on the next storey. The same pattern on every
    /// storey keeps every flight above the last. Every flight has three clear cells over it (a body
    /// on a slope rides higher than on the flat); flights hang from the core's side walls, so nothing
    /// needs to stand under them, and on the lowest storey the space under the stair is filled in.
    /// </summary>
    void Stairwell(int cx, Dictionary<int, int> modules, bool second = false)
    {
        if (P.Stair == "ladder" && !second)
        {
            Ladderway(cx, modules);
            return;
        }
        int stairTop = second ? Top : StairTop;
        for (int s = Bottom; s <= stairTop; s++)
        {
            if (Hut && s == 0 && second) continue;
            int y = WalkY(s);
            bool up = s < stairTop, fromBelow = s > Bottom;
            var air = new List<Int3>();
            void Cell(int lane, int r, int k, char what)
            {
                Int3 p = L(cx + lane, y + k, Zs - 2 - r);
                switch (what)
                {
                    case '.': C.Clear(p); if (k <= 2) air.Add(p); break;
                    case '#': C.Set(p, S.Floor.Make(p)); break;
                    case 'N': C.Set(p, S.Stair(p, R(Dir.North))); break;
                    case 'S': C.Set(p, S.Stair(p, R(Dir.South))); break;
                }
            }
            char under = fromBelow ? '.' : '#';
            for (int lane = 0; lane < 4; lane++)
            {
                bool a = lane < 2;
                for (int k = 0; k < 3; k++) Cell(lane, 0, k, '.');
                if (up)
                {
                    if (a)
                    {
                        Cell(lane, 1, 0, 'N'); Cell(lane, 1, 1, '.'); Cell(lane, 1, 2, '.'); Cell(lane, 1, 3, '.');
                        Cell(lane, 2, 0, under); Cell(lane, 2, 1, 'N'); Cell(lane, 2, 2, '.'); Cell(lane, 2, 3, '.');
                    }
                    else
                    {
                        Cell(lane, 1, 0, under); Cell(lane, 1, 1, under); Cell(lane, 1, 2, under); Cell(lane, 1, 3, 'S');
                        Cell(lane, 2, 0, under); Cell(lane, 2, 1, under); Cell(lane, 2, 2, 'S'); Cell(lane, 2, 3, '.');
                    }
                    for (int r = 3; r <= 4; r++)
                    {
                        Cell(lane, r, 0, under); Cell(lane, r, 1, '#'); Cell(lane, r, 2, '.'); Cell(lane, r, 3, '.');
                    }
                }
                else
                {
                    for (int r = 1; r <= 4; r++)
                        for (int k = 0; k < 3; k++)
                        {
                            bool keep = fromBelow && (a ? r == 2 && k == 0 : (r == 1 && k <= 2) || (r == 2 && k <= 1)) || fromBelow && r >= 3 && k == 0;
                            Cell(lane, r, k, keep ? '.' : '#');
                        }
                }
            }
            // The core's walls, and its open side onto the corridor between two jambs.
            for (int k = 0; k < 3; k++)
            {
                foreach (int jx in new[] { cx - 1, cx + 4 })
                {
                    Int3 jamb = L(jx, y + k, Zs - 1);
                    if (s <= Top && C.Get(jamb).IsAir) C.Set(jamb, InnerWall(jamb));
                }
                for (int z = Math.Max(Zm, Zs - 7); z <= Zs - 2; z++)
                {
                    Int3 w1 = L(cx - 1, y + k, z), w2 = L(cx + 4, y + k, z);
                    if (C.Get(w1).IsAir) C.Set(w1, InnerWall(w1));
                    if (C.Get(w2).IsAir) C.Set(w2, InnerWall(w2));
                }
                for (int x = cx - 1; x <= cx + 4; x++)
                {
                    Int3 back = L(x, y + k, Zs - 7);
                    if (Zs - 7 >= Zm && C.Get(back).IsAir) C.Set(back, InnerWall(back));
                }
                for (int x = cx; x <= cx + 3; x++)
                    if (s <= Top) C.Clear(L(x, y + k, Zs - 1));
            }
            var cells = air.Where(p => p.Y >= y && p.Y <= y + 2).Distinct().ToList();
            modules[s] = C.AddModule(ModuleKind.Stair, second ? "second stair" : "stairwell", s, cells);
            Out.Modules.Add(modules[s]);
            if (_plans.TryGetValue(s, out var pl) && pl.Corridor >= 0) C.Link(pl.Corridor, modules[s]);
            if (s > Bottom && modules.ContainsKey(s - 1)) C.Link(modules[s - 1], modules[s]);
            C.X.Stairwells.Add((s, Int3.Min(L(cx, y, Zs - 6), L(cx + 3, y, Zs - 2)), Int3.Max(L(cx, y, Zs - 6), L(cx + 3, y, Zs - 2))));
            C.X.Lights.Add(new LightSpot { Cell = L(cx + 2, y + 2, Zs - 4), Color = S.Kit.Light, Mode = LightMode.Steady, Energy = 0.9f, Range = 6f, Module = modules[s], Storey = s });
            Int3 sconce = L(cx - 1, y + 2, Zs - 3);
            if (C.Get(sconce).Type == BlockType.Cube) C.Set(sconce, S.Light.Make(sconce));
        }
        if (!second && RoofHut && _roofDoorCells != null)
        {
            var roof = new List<Int3>();
            int y = WalkY(Top + 1);
            for (int x = Cx - 1; x <= Cx + 4; x++)
                for (int k = 0; k < 2; k++) roof.Add(L(x, y + k, Zs));
            _roofModule = C.AddModule(ModuleKind.Outdoor, "roof", Top + 1, roof);
            Out.Modules.Add(_roofModule);
            C.AddDoorway(_roofDoorCells, R(Dir.South), modules[Top + 1], _roofModule);
        }
    }

    /// <summary>
    /// A ladderway instead of a stair (mines, service shafts): one shaft through every storey with
    /// a ladder up its wall, a landing on each storey you step on and off from.
    /// </summary>
    void Ladderway(int cx, Dictionary<int, int> modules)
    {
        var ladder = new Rgb(0x6a, 0x5a, 0x48);
        for (int s = Bottom; s <= StairTop; s++)
        {
            int y = WalkY(s);
            bool up = s < StairTop;
            var air = new List<Int3>();
            for (int lane = 0; lane < 4; lane++)
                for (int r = 0; r <= 4; r++)
                    for (int k = 0; k < (up ? 4 : 3); k++)
                    {
                        Int3 p = L(cx + lane, y + k, Zs - 2 - r);
                        bool shaft = lane == 1 && r == 1;
                        if (r == 0 && k < 3) { C.Clear(p); air.Add(p); continue; }
                        if (shaft)
                        {
                            if (up) C.Set(p, new Block(BlockType.Ladder, Material.Timber, ladder, Role.Traverse, R(Dir.South)));
                            else if (k < 2) C.Clear(p);
                            else C.Set(p, S.Floor.Make(p));
                            if (k < 3 && (up || k < 2)) air.Add(p);
                            continue;
                        }
                        if (k < 3) C.Set(p, S.Floor.Make(p));
                    }
            for (int x = cx; x <= cx + 3; x++)
                for (int k = 0; k < 3; k++)
                    if (s <= Top) C.Clear(L(x, y + k, Zs - 1));
            for (int k = 0; k < 3; k++)
                for (int z = Math.Max(Zm, Zs - 7); z <= Zs - 2; z++)
                {
                    Int3 w1 = L(cx - 1, y + k, z), w2 = L(cx + 4, y + k, z);
                    if (C.Get(w1).IsAir) C.Set(w1, InnerWall(w1));
                    if (C.Get(w2).IsAir) C.Set(w2, InnerWall(w2));
                }
            modules[s] = C.AddModule(ModuleKind.Stair, "ladderway", s, air.Distinct().ToList());
            Out.Modules.Add(modules[s]);
            if (_plans.TryGetValue(s, out var pl) && pl.Corridor >= 0) C.Link(pl.Corridor, modules[s]);
            if (s > Bottom) C.Link(modules[s - 1], modules[s]);
            C.X.Stairwells.Add((s, Int3.Min(L(cx, y, Zs - 6), L(cx + 3, y, Zs - 2)), Int3.Max(L(cx, y, Zs - 6), L(cx + 3, y, Zs - 2))));
            C.X.Lights.Add(new LightSpot { Cell = L(cx + 2, y + 2, Zs - 2), Color = S.Kit.Light, Mode = LightMode.Steady, Energy = 0.8f, Range = 5f, Module = modules[s], Storey = s });
        }
    }

    void LiftShaft()
    {
        var lift = new LiftShaft();
        for (int x = Lx; x <= Lx + 1; x++)
            for (int z = Zs - 3; z <= Zs - 2; z++) lift.Footprint.Add(L(x, 0, z) with { Y = 0 });
        var stopCells = new List<Int3>();
        for (int s = Bottom; s <= Top; s++)
        {
            int y = WalkY(s);
            lift.StopWalkY.Add(y);
            for (int x = Lx; x <= Lx + 1; x++)
                for (int z = Zs - 3; z <= Zs - 2; z++)
                {
                    for (int k = 0; k < 3; k++) C.Clear(L(x, y + k, z));
                    if (s > Bottom) C.Clear(L(x, y - 1, z));
                    stopCells.Add(L(x, y, z));
                }
            for (int k = -1; k < 3; k++)
            {
                if (s == Bottom && k < 0) continue;
                for (int z = Zs - 4; z <= Zs - 2; z++)
                {
                    C.Set(L(Lx - 1, y + k, z), InnerWall(L(Lx - 1, y + k, z)));
                    C.Set(L(Lx + 2, y + k, z), InnerWall(L(Lx + 2, y + k, z)));
                }
                for (int x = Lx; x <= Lx + 1; x++) C.Set(L(x, y + k, Zs - 4), InnerWall(L(x, y + k, Zs - 4)));
            }
            for (int k = 0; k < 3; k++) C.Set(L(Lx + 2, y + k, Zs - 1), InnerWall(L(Lx + 2, y + k, Zs - 1)));
        }
        _liftModule = C.AddModule(ModuleKind.Lift, "lift", Bottom, stopCells);
        Out.Modules.Add(_liftModule);
        for (int s = Bottom; s <= Top; s++)
        {
            int y = WalkY(s);
            var door = new List<Int3>();
            for (int x = Lx; x <= Lx + 1; x++)
            {
                for (int k = 0; k < DoorSize.Tall; k++) door.Add(L(x, y + k, Zs - 1));
                for (int k = DoorSize.Tall; k < Pitch - 1; k++) C.Set(L(x, y + k, Zs - 1), InnerWall(L(x, y + k, Zs - 1)));
            }
            int d = C.AddDoorway(door, R(Dir.North), _plans[s].Corridor, _liftModule);
            lift.StopDoors.Add(d);
        }
        C.X.Lifts.Add(lift);
    }

    void Entrance()
    {
        List<Int3> cells = new();
        for (int z = Zs; z <= Zs + 1; z++)
            for (int k = 0; k < DoorSize.Tall; k++) cells.Add(L(Hut ? Cx - 2 : 0, k, z));
        Dir facing = R(Dir.West);
        foreach (Int3 c in cells) C.Clear(c);
        Out.ExitCells = cells;
        Out.EntranceFacing = facing;
        Int3 min = cells.Aggregate(Int3.Min);
        Out.EntranceCell = min + facing.Vec() * 2;
        Out.EntranceModule = _plans[0].Corridor;
        // A canopy over the way in on two posts, so the door reads from across the yard: at the door's headroom, a metre over its head.
        Dir across = facing is Dir.North or Dir.South ? Dir.East : Dir.South;
        Int3 lo = min, hi = cells.Aggregate(Int3.Max);
        int span = (hi - lo).X + (hi - lo).Z;
        for (int d = 1; d <= 2; d++)
            for (int a = -1; a <= span + 1; a++)
            {
                Int3 c = (lo with { Y = DoorSize.Headroom }) + facing.Vec() * d + across.Vec() * a;
                if (!C.Blocks.ContainsKey(c)) C.Blocks[c] = S.Accent.Make(c);
            }
        foreach (int a in new[] { -1, span + 1 })
            for (int k = 0; k < DoorSize.Headroom; k++)
            {
                Int3 post = (lo with { Y = k }) + facing.Vec() * 2 + across.Vec() * a;
                if (!C.Blocks.ContainsKey(post)) C.Blocks[post] = new Block(BlockType.Pillar, Material.Steel, S.Machine.Colors[0], Role.Decor);
            }
        C.X.Lights.Add(new LightSpot { Cell = min + facing.Vec() + Int3.Up * 2, Color = S.Kit.Light, Mode = LightMode.Steady, Energy = 0.8f, Range = 6f, Module = _plans[0].Corridor });
    }

    /// <summary>
    /// What marks a buried place from afar: a bare concrete slab with a blockhouse on it, vent
    /// stacks breathing from below, and a mast whose red lamp still blinks for no one.
    /// </summary>
    bool NearBreach(Int3 p) => C.X.Breaches.Any(b => b.SurfaceCell.Manhattan(p with { Y = 0 }) <= 3 || b.Cells.Any(c => (c with { Y = 0 }).Manhattan(p with { Y = 0 }) <= 1));

    void HutLandmarks()
    {
        var f0 = HutRect();
        var vent = new Rgb(0x6a, 0x6c, 0x68);
        int placed = 0;
        for (int x = Wm - 3; x > CoreEnd + 3 && placed < 3; x -= 5)
        {
            int z = placed % 2 == 0 ? Zm + 2 : Zm + Dm - 3;
            if (!InFoot(-1, x, z) || NearBreach(L(x, 0, z)) || C.Get(L(x, -1, z)).Role != Role.Floor || C.Get(L(x, -1, z)).Type != BlockType.Cube || !C.Get(L(x, 0, z)).IsAir) continue;
            int h = 3 + (int)(Hash.Cell(new Int3(x, 0, z), 71) % 3);
            for (int k = 0; k < h; k++) C.Blocks[L(x, k, z)] = new Block(BlockType.Cube, Material.Steel, vent.Scale(0.85f + 0.05f * (k % 2)), Role.Decor);
            C.Blocks[L(x, h, z)] = new Block(BlockType.Tank, Material.Steel, vent.Scale(0.7f), Role.Decor);
            C.X.Markers.Add(new Marker("vent", L(x, h + 1, z), "a thin breath of air from below"));
            C.X.Sounds.Add(new SoundHook("vent-hum", L(x, 1, z), 0.4f, 0));
            placed++;
        }
        // The mast stands on solid slab clear of the stair house (never over a collapse).
        int mx = Math.Min(Wm - 2, f0.x1 + 3), mz = Math.Max(Zm + 2, f0.z0 + 1);
        while (mx < Wm - 2 && (NearBreach(L(mx, 0, mz)) || C.Get(L(mx, -1, mz)).Role != Role.Floor || C.Get(L(mx, -1, mz)).Type != BlockType.Cube || !C.Get(L(mx, 0, mz)).IsAir)) mx++;
        Int3 mast = L(mx, 0, mz);
        int mh = 12 + Levels * 2;
        for (int k = 0; k < mh; k++) C.Blocks[mast + Int3.Up * k] = new Block(k % 4 == 3 ? BlockType.Lattice : BlockType.Pillar, Material.Steel, new Rgb(0x5a, 0x5c, 0x60), Role.Decor);
        C.Blocks[mast + Int3.Up * mh] = new Block(BlockType.Cube, Material.Lumen, new Rgb(0xff, 0x30, 0x20), Role.Light);
        C.X.Lights.Add(new LightSpot { Cell = mast + Int3.Up * mh, Color = new Rgb(0xff, 0x30, 0x20), Mode = LightMode.Beacon, Energy = 2.2f, Range = 14f });
    }

    /// <summary>The rooms as modules (before purposes: the solver needs their sizes and neighbours).</summary>
    void RoomModules()
    {
        foreach (var plan in _plans.Values.OrderBy(p => p.S))
            foreach (var r in plan.Rooms)
            {
                var cells = new List<Int3>();
                foreach (var (x, z) in r.Cells)
                    for (int k = 0; k < 3; k++) cells.Add(L(x, plan.Y + k, z));
                r.Module = C.AddModule(ModuleKind.Room, "room", plan.S, cells);
                Out.Modules.Add(r.Module);
            }
    }
}
