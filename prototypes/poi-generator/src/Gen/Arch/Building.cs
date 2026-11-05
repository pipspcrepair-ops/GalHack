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
}

/// <summary>
/// Plans and builds one building from its purpose program, the way an architect would:
///   1. massing: a footprint, storeys above ground and levels below, the same outline on every
///      storey (a bunker shows only its stair house above ground);
///   2. vertical cores: a switchback stairwell (and sometimes a lift) at one x and z through every
///      storey, so stairs and shafts always line up;
///   3. circulation: a two-wide spine corridor down the middle of each storey from the stairwell,
///      double-loaded with rooms either side, entered from the lobby at the front;
///   4. rooms: each band of storeys asks for rooms by purpose; partitions are cut along the spine
///      and purposes assigned by a small constraint solver (size, preferred position, and the
///      near and avoid rules: labs by storage, quarters by the mess, never the reactor by the
///      quarters), improved by pairwise swaps;
///   5. doors on the spine, internal doors between rooms that belong together, keycard locks on
///      secure rooms with the keycard placed where it can be reached first, a vault at the deepest
///      point, hidden rooms behind breakable walls in the plan's unexplained gaps;
///   6. furniture along the walls by purpose, keeping every cell of a room reachable from its
///      door, then lights, slots for loot, terminals, spawns and story;
///   7. below ground: collapse breaches from the surface, a natural cave breaking into the deepest
///      level, and deliberate wrongness that grows with depth.
/// Everything is cell-exact on the 1 m grid and seeded.
/// </summary>
public sealed class Building
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

    // Massing.
    int W, D, Zs, Cx, Lx, CoreEnd, Floors, Levels, Top, Bottom, StairTop;
    bool Lift, Hut, RoofHut;

    // Plans.
    sealed class RoomPlan
    {
        public int Storey, X0, Z0, X1, Z1, Band;  // band 0 north, 1 south
        public RoomDef? Def;
        public bool Secret;
        public RoomPlan? SecretFrom;
        public int Module = -1, Door = -1;
        public int DoorX;
        public int Width => X1 - X0 + 1;
        public int Depth => Z1 - Z0 + 1;
        public string Purpose => Secret ? "hidden stash" : Def?.Id ?? "room";
    }

    readonly Dictionary<int, List<RoomPlan>> _rooms = new();
    readonly Dictionary<int, int> _spine = new(), _stair = new();
    int _liftModule = -1, _hutSpine = -1;
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

    /// <summary>Local plan cell (x along the spine, z across it) to world, turned by the site's rotation.</summary>
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

    // ---- the plan -----------------------------------------------------------------------

    void Run()
    {
        Massing();
        for (int s = Bottom; s <= Top; s++) Shell(s);
        if (RoofHut) RoofHutShell();
        for (int s = Bottom; s <= Top; s++)
        {
            if (Hut && s == 0) HutStorey();
            else FullStorey(s);
        }
        Stairwell();
        if (Lift) LiftShaft();
        Entrance();
        PlanRooms();
        BuildRooms(furnish: false);
        // The collapse comes before the furniture, so nothing is placed where the roof fell in.
        if ((Hut || Floors == 0) && Levels > 0 && Rng.Chance(P.Breach)) CollapseBreach();
        foreach (var (_, rooms) in _rooms.OrderBy(kv => kv.Key))
            foreach (var r in rooms.Where(r => !r.Secret)) Furnish(r);
        Locks();
        if (Rng.Chance(P.Cavern) && Levels > 0) Cavern();
        Roofscape();
        Levelinfo();
        Out.Floors = Floors;
        Out.Levels = Levels;
        Out.Min = Int3.Min(L(0, WalkY(Bottom) - 1, 0), L(W - 1, WalkY(Top) + 4, D - 1));
        Out.Max = Int3.Max(L(0, WalkY(Bottom) - 1, 0), L(W - 1, WalkY(Top) + 4, D - 1));
    }

    void Massing()
    {
        W = Spec.W ?? Rng.Between(P.WMin, P.WMax);
        D = Math.Max(16, Spec.D ?? Rng.Between(P.DMin, P.DMax));
        Floors = Spec.Floors ?? Rng.Between(P.FloorsMin, P.FloorsMax);
        Levels = Spec.Levels ?? Rng.Between(P.LevelsMin, P.LevelsMax);
        // Deeper and grander as rarity climbs: rare places go one level further down.
        if (Levels > 0 && Rarity >= Rarity.Rare && Spec.Levels == null) Levels++;
        Hut = P.Surface == "hut" || Floors == 0;
        if (Hut) Floors = 1;
        Top = Floors - 1;
        Bottom = -Levels;
        Zs = Math.Max(7, D / 2 - 1);
        int maxCx = W - 10;
        Lift = Rng.Chance(P.Lift) && Floors + Levels >= 3 && W - 13 >= 6;
        if (Lift) maxCx = W - 13;
        Cx = Math.Clamp((int)(W * Rng.Float(0.28f, 0.45f)), 6, Math.Max(6, maxCx));
        Lx = Cx + 5;
        CoreEnd = Lift ? Cx + 7 : Cx + 4;
        RoofHut = !Hut && Floors >= 2 && P.Roof is "access" or "pad";
        StairTop = RoofHut ? Top + 1 : Top;
    }

    /// <summary>The storey's outline: the whole footprint, or only the stair house on a hut's ground floor.</summary>
    (int x0, int z0, int x1, int z1) Foot(int s)
    {
        if (s > Top) return (Cx - 1, Math.Max(0, Zs - 7), Cx + 4, Zs - 1);                  // the roof hut
        if (Hut && s == 0) return (Cx - 2, Math.Max(0, Zs - 7), CoreEnd + 1, Zs + 2);         // the stair house
        return (0, 0, W - 1, D - 1);
    }

    static bool In((int x0, int z0, int x1, int z1) f, int x, int z) => x >= f.x0 && x <= f.x1 && z >= f.z0 && z <= f.z1;
    static bool OnRing((int x0, int z0, int x1, int z1) f, int x, int z) => In(f, x, z) && (x == f.x0 || x == f.x1 || z == f.z0 || z == f.z1);

    Block Wall(Int3 w, bool exterior, int s)
    {
        var e = exterior ? S.Wall : S.InnerWall;
        return e.Make(w, Dir.North);
    }

    /// <summary>Slabs, outer walls with windows above ground, a roof and parapet on top.</summary>
    void Shell(int s)
    {
        int y = WalkY(s);
        var f = Foot(s);
        var below = s > Bottom ? Foot(s - 1) : f;
        for (int z = Math.Min(f.z0, below.z0); z <= Math.Max(f.z1, below.z1); z++)
            for (int x = Math.Min(f.x0, below.x0); x <= Math.Max(f.x1, below.x1); x++)
            {
                if (!In(f, x, z) && !(s > Bottom && In(below, x, z))) continue;
                Int3 p = L(x, y - 1, z);
                C.Set(p, S.Floor.Make(p));
            }
        bool above = s >= 0;
        for (int z = f.z0; z <= f.z1; z++)
            for (int x = f.x0; x <= f.x1; x++)
            {
                if (!OnRing(f, x, z)) continue;
                for (int k = 0; k < 3; k++)
                {
                    Int3 p = L(x, y + k, z);
                    bool corner = (x == f.x0 || x == f.x1) && (z == f.z0 || z == f.z1);
                    if (above && k == 1 && !corner && P.Windows > 0f && (Hash.Cell(new Int3(x, s, z), 31) % 1000) / 1000f < P.Windows && (x + z) % 3 != 0)
                        C.Set(p, S.Window.Make(p));
                    else if (above && k == 2 && !corner && S.Accent != S.Wall && s % 2 == 0)
                        C.Set(p, S.Accent.Make(p));
                    else
                        C.Set(p, Wall(p, true, s));
                }
            }
        // The interior is the building's own air (carved out of the ground below ground).
        for (int z = f.z0 + 1; z < f.z1; z++)
            for (int x = f.x0 + 1; x < f.x1; x++)
                for (int k = 0; k < 3; k++) C.Clear(L(x, y + k, z));

        if (s == Top && !(RoofHut && false))
        {
            for (int z = f.z0; z <= f.z1; z++)
                for (int x = f.x0; x <= f.x1; x++)
                {
                    Int3 p = L(x, y + 3, z);
                    C.Set(p, S.Roof.Make(p));
                    if (OnRing(f, x, z) && s >= 0 && !(Hut && s == 0))
                    {
                        Int3 q = L(x, y + 4, z);
                        C.Set(q, S.Wall.Make(q));
                    }
                }
        }
    }

    void RoofHutShell()
    {
        int s = Top + 1, y = WalkY(s);
        var f = Foot(s);
        for (int z = f.z0; z <= f.z1; z++)
            for (int x = f.x0; x <= f.x1; x++)
            {
                for (int k = 0; k < 3; k++)
                {
                    Int3 p = L(x, y + k, z);
                    if (OnRing(f, x, z)) C.Set(p, S.Wall.Make(p));
                    else C.Clear(p);
                }
                Int3 r = L(x, y + 3, z);
                C.Set(r, S.Roof.Make(r));
            }
        // Its door opens onto the roof from the top of the stair.
        var cells = new List<Int3>();
        for (int x = Cx + 2; x <= Cx + 3; x++)
            for (int k = 0; k < 3; k++) cells.Add(L(x, y + k, Zs - 1));
        _roofDoorCells = cells;
    }

    List<Int3>? _roofDoorCells;

    // ---- storeys -----------------------------------------------------------------------

    void FullStorey(int s)
    {
        int y = WalkY(s);
        // The spine: two cells wide, walls either side.
        var spine = new List<Int3>();
        for (int x = 1; x <= W - 2; x++)
            for (int z = Zs; z <= Zs + 1; z++)
                for (int k = 0; k < 3; k++)
                {
                    Int3 p = L(x, y + k, z);
                    C.Clear(p);
                    spine.Add(p);
                    if (k == 0) C.X.CorridorCells.Add(p);
                }
        for (int x = 1; x <= W - 2; x++)
            for (int k = 0; k < 3; k++)
            {
                Int3 a = L(x, y + k, Zs - 1), b = L(x, y + k, Zs + 2);
                C.Set(a, Wall(a, false, s));
                C.Set(b, Wall(b, false, s));
            }
        _spine[s] = C.AddModule(ModuleKind.Corridor, "corridor", s, spine);
        Out.Modules.Add(_spine[s]);
        // Corridor lamps: a sconce in the wall every few metres, a light for the view between them.
        for (int x = 3; x <= W - 3; x += 6)
        {
            Int3 sc = L(x, y + 2, Zs + 2);
            C.Set(sc, S.Light.Make(sc));
            C.X.Lights.Add(new LightSpot { Cell = L(x, y + 2, Zs), Color = S.Kit.Light, Mode = LightMode.Steady, Energy = 1.0f, Range = 7f, Module = _spine[s], Storey = s });
        }
        // The core column's walls; behind the stairwell and the lift, solid.
        for (int k = 0; k < 3; k++)
        {
            for (int z = Math.Max(1, Zs - 7); z <= Zs - 2; z++)
            {
                C.Set(L(Cx - 1, y + k, z), Wall(L(Cx - 1, y + k, z), false, s));
                C.Set(L(Cx + 4, y + k, z), Wall(L(Cx + 4, y + k, z), false, s));
            }
            for (int x = Cx - 1; x <= Cx + 4; x++)
            {
                if (Zs - 7 >= 1) C.Set(L(x, y + k, Zs - 7), Wall(L(x, y + k, Zs - 7), false, s));
                for (int z = 1; z <= Zs - 8; z++) C.Set(L(x, y + k, z), Wall(L(x, y + k, z), false, s));
            }
            if (Lift)
                for (int z = 1; z <= Zs - 2; z++)
                    for (int x = Cx + 5; x <= Cx + 7; x++)
                    {
                        bool shaft = x <= Cx + 6 && z >= Zs - 3;
                        if (!shaft) C.Set(L(x, y + k, z), Wall(L(x, y + k, z), false, s));
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
        var f = Foot(0);
        for (int x = f.x0 + 1; x < f.x1; x++)
            for (int z = f.z0 + 1; z <= Zs - 1; z++)
                for (int k = 0; k < 3; k++)
                {
                    bool stair = x >= Cx && x <= Cx + 3 && z >= Zs - 6 && z <= Zs - 2;
                    bool shaft = Lift && x >= Lx && x <= Lx + 1 && z >= Zs - 3 && z <= Zs - 2;
                    if (!stair && !shaft) C.Set(L(x, y + k, z), Wall(L(x, y + k, z), false, 0));
                }
        _hutSpine = C.AddModule(ModuleKind.Entrance, "stair house", 0, spine);
        _spine[0] = _hutSpine;
        Out.Modules.Add(_hutSpine);
        C.X.Lights.Add(new LightSpot { Cell = L(Cx + 1, y + 2, Zs), Color = S.Kit.Light, Mode = LightMode.Steady, Energy = 1.1f, Range = 8f, Module = _hutSpine });
    }

    // ---- vertical cores -----------------------------------------------------------------

    /// <summary>
    /// The switchback stair: lane A climbs two steps away from the spine to a landing, lane B
    /// climbs two more back toward it and arrives on the next storey. The same pattern on every
    /// storey keeps every flight above the last, with headroom everywhere a body passes.
    /// </summary>
    void Stairwell()
    {
        if (P.Stair == "ladder")
        {
            Ladderway();
            return;
        }
        for (int s = Bottom; s <= StairTop; s++)
        {
            int y = WalkY(s);
            bool up = s < StairTop, fromBelow = s > Bottom;
            var air = new List<Int3>();
            void Cell(int lane, int r, int k, char what)
            {
                int x = Cx + lane, z = Zs - 2 - r;
                Int3 p = L(x, y + k, z);
                switch (what)
                {
                    case '.': C.Clear(p); if (k <= 2) air.Add(p); break;
                    case '#': C.Set(p, S.Floor.Make(p)); break;
                    case 'N': C.Set(p, S.Stair(p, R(Dir.North))); break;
                    case 'S': C.Set(p, S.Stair(p, R(Dir.South))); break;
                }
            }
            for (int lane = 0; lane < 4; lane++)
            {
                bool a = lane < 2;
                // r = 0: the landing on the spine.
                for (int k = 0; k < 3; k++) Cell(lane, 0, k, '.');
                if (up)
                {
                    if (a)
                    {
                        Cell(lane, 1, 0, 'N'); Cell(lane, 1, 1, '.'); Cell(lane, 1, 2, '.');
                        Cell(lane, 2, 0, '#'); Cell(lane, 2, 1, 'N'); Cell(lane, 2, 2, '.'); Cell(lane, 2, 3, '.');
                    }
                    else
                    {
                        Cell(lane, 1, 0, fromBelow ? '.' : '#'); Cell(lane, 1, 1, fromBelow ? '.' : '#'); Cell(lane, 1, 2, '#'); Cell(lane, 1, 3, 'S');
                        Cell(lane, 2, 0, fromBelow ? '.' : '#'); Cell(lane, 2, 1, '#'); Cell(lane, 2, 2, 'S'); Cell(lane, 2, 3, '.');
                    }
                    for (int r = 3; r <= 4; r++)
                    {
                        Cell(lane, r, 0, '#'); Cell(lane, r, 1, '#'); Cell(lane, r, 2, '.'); Cell(lane, r, 3, '.');
                    }
                }
                else
                {
                    // The top of the stair: only the arrival, and the headroom of the flight below.
                    for (int r = 1; r <= 4; r++)
                        for (int k = 0; k < 3; k++)
                        {
                            bool keep = !a && fromBelow && ((r == 1 && k <= 1) || (r == 2 && k == 0));
                            Cell(lane, r, k, keep ? '.' : '#');
                        }
                }
            }
            // The stairwell opens onto the spine.
            for (int x = Cx; x <= Cx + 3; x++)
                for (int k = 0; k < 3; k++)
                {
                    Int3 p = L(x, y + k, Zs - 1);
                    if (s <= Top) C.Clear(p);
                }
            var cells = air.Where(p => p.Y >= y && p.Y <= y + 2).Distinct().ToList();
            _stair[s] = C.AddModule(ModuleKind.Stair, "stairwell", s, cells);
            Out.Modules.Add(_stair[s]);
            if (_spine.TryGetValue(s, out int sp)) C.Link(sp, _stair[s]);
            if (s > Bottom) C.Link(_stair[s - 1], _stair[s]);
            C.X.Stairwells.Add((s, Int3.Min(L(Cx, y, Zs - 6), L(Cx + 3, y, Zs - 2)), Int3.Max(L(Cx, y, Zs - 6), L(Cx + 3, y, Zs - 2))));
            C.X.Lights.Add(new LightSpot { Cell = L(Cx + 2, y + 2, Zs - 4), Color = S.Kit.Light, Mode = LightMode.Steady, Energy = 0.9f, Range = 6f, Module = _stair[s], Storey = s });
            Int3 sconce = L(Cx - 1, y + 2, Zs - 3);
            if (C.Get(sconce).Type == BlockType.Cube) C.Set(sconce, S.Light.Make(sconce));
        }
        if (RoofHut && _roofDoorCells != null)
        {
            // The roof is open ground: the door leads out of the stair house onto it.
            var roof = new List<Int3>();
            int y = WalkY(Top + 1);
            for (int x = Cx - 1; x <= Cx + 4; x++)
                for (int k = 0; k < 2; k++) roof.Add(L(x, y + k, Zs));
            int roofModule = C.AddModule(ModuleKind.Outdoor, "roof", Top + 1, roof);
            Out.Modules.Add(roofModule);
            C.AddDoorway(_roofDoorCells, R(Dir.South), _stair[Top + 1], roofModule);
            _roofModule = roofModule;
        }
    }

    int _roofModule = -1;

    /// <summary>
    /// A ladderway instead of a stair (mines, service shafts): one shaft through every storey with
    /// a ladder up its wall, a landing on each storey you step on and off from.
    /// </summary>
    void Ladderway()
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
                        Int3 p = L(Cx + lane, y + k, Zs - 2 - r);
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
            for (int x = Cx; x <= Cx + 3; x++)
                for (int k = 0; k < 3; k++)
                    if (s <= Top) C.Clear(L(x, y + k, Zs - 1));
            _stair[s] = C.AddModule(ModuleKind.Stair, "ladderway", s, air.Distinct().ToList());
            Out.Modules.Add(_stair[s]);
            if (_spine.TryGetValue(s, out int sp)) C.Link(sp, _stair[s]);
            if (s > Bottom) C.Link(_stair[s - 1], _stair[s]);
            C.X.Stairwells.Add((s, Int3.Min(L(Cx, y, Zs - 6), L(Cx + 3, y, Zs - 2)), Int3.Max(L(Cx, y, Zs - 6), L(Cx + 3, y, Zs - 2))));
            C.X.Lights.Add(new LightSpot { Cell = L(Cx + 2, y + 2, Zs - 2), Color = S.Kit.Light, Mode = LightMode.Steady, Energy = 0.8f, Range = 5f, Module = _stair[s], Storey = s });
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
                    if (s > Bottom) C.Clear(L(x, y - 1, z));   // the shaft runs through the slabs
                    stopCells.Add(L(x, y, z));
                }
            // Shaft walls.
            for (int k = -1; k < 3; k++)
            {
                if (s == Bottom && k < 0) continue;
                for (int z = Zs - 4; z <= Zs - 2; z++)
                {
                    C.Set(L(Lx - 1, y + k, z), Wall(L(Lx - 1, y + k, z), false, s));
                    C.Set(L(Lx + 2, y + k, z), Wall(L(Lx + 2, y + k, z), false, s));
                }
                for (int x = Lx; x <= Lx + 1; x++) C.Set(L(x, y + k, Zs - 4), Wall(L(x, y + k, Zs - 4), false, s));
            }
        }
        _liftModule = C.AddModule(ModuleKind.Lift, "lift", Bottom, stopCells);
        Out.Modules.Add(_liftModule);
        for (int s = Bottom; s <= Top; s++)
        {
            int y = WalkY(s);
            var door = new List<Int3>();
            for (int x = Lx; x <= Lx + 1; x++)
                for (int k = 0; k < 3; k++) door.Add(L(x, y + k, Zs - 1));
            int d = C.AddDoorway(door, R(Dir.North), _spine[s], _liftModule);
            lift.StopDoors.Add(d);
        }
        C.X.Lifts.Add(lift);
    }

    void Entrance()
    {
        int y = 0;
        List<Int3> cells = new();
        Dir facing;
        if (Hut)
        {
            for (int z = Zs; z <= Zs + 1; z++)
                for (int k = 0; k < 3; k++) cells.Add(L(Cx - 2, y + k, z));
            facing = R(Dir.West);
        }
        else
        {
            for (int z = Zs; z <= Zs + 1; z++)
                for (int k = 0; k < 3; k++) cells.Add(L(0, y + k, z));
            facing = R(Dir.West);
        }
        foreach (Int3 c in cells) C.Clear(c);
        Out.ExitCells = cells;
        Out.EntranceFacing = facing;
        Int3 min = cells.Aggregate(Int3.Min);
        Out.EntranceCell = min + facing.Vec() * 2;
        Out.EntranceModule = _spine[0];
        // A sign over the door, and the porch light.
        Int3 over = cells.Aggregate(Int3.Max) + Int3.Up;
        C.X.Lights.Add(new LightSpot { Cell = min + facing.Vec() + Int3.Up * 2, Color = S.Kit.Light, Mode = LightMode.Steady, Energy = 0.8f, Range = 6f, Module = _spine[0] });
        _ = over;
    }

    // ---- rooms ----------------------------------------------------------------------------

    IEnumerable<string> BandKeys(int s)
    {
        yield return "any";
        if (s == 0) yield return "ground";
        if (s >= 0) yield return "surface";
        if (s > 0 && s < Top) yield return "upper";
        if (s > 0 && s == Top) yield return "top";
        if (s > 0) yield return "above";
        if (s < 0)
        {
            yield return "basement";
            if (s == -1) yield return "shallow";
            if (s <= Bottom / 2 && s < -1) yield return "deep";
            if (s == Bottom) yield return "deepest";
        }
        else if (Levels == 0 && s == Top) yield return "deepest";
    }

    /// <summary>Cuts a run of cells into rooms of 4 to 11 cells with one-cell walls between.</summary>
    List<(int a, int b)> Split(int a, int b)
    {
        var pieces = new List<(int, int)>();
        int len = b - a + 1;
        if (len < 4) return pieces;
        int target = Rng.Between(5, 9);
        int n = Math.Max(1, (int)MathF.Round((len + 1) / (float)(target + 1)));
        while (n > 1 && (len - (n - 1)) / n < 4) n--;
        int room = len - (n - 1);
        var widths = Enumerable.Repeat(room / n, n).ToArray();
        for (int i = 0; i < room % n; i++) widths[i]++;
        // Jitter pairs, never below four.
        for (int i = 0; i + 1 < n; i++)
        {
            int d = Rng.Between(-2, 2);
            if (widths[i] + d >= 4 && widths[i + 1] - d >= 4)
            {
                widths[i] += d;
                widths[i + 1] -= d;
            }
        }
        int x = a;
        foreach (int w in widths)
        {
            pieces.Add((x, x + w - 1));
            x += w + 1;
        }
        return pieces;
    }

    void PlanRooms()
    {
        int secretsWanted = Rng.Between(P.SecretsMin, P.SecretsMax);
        var secretStoreys = new List<int>();
        for (int i = 0; i < secretsWanted; i++)
            secretStoreys.Add(Levels > 0 && Rng.Chance(0.7f) ? Rng.Between(Bottom, -1) : Rng.Between(Bottom, Top));

        for (int s = Bottom; s <= Top; s++)
        {
            var rooms = new List<RoomPlan>();
            _rooms[s] = rooms;
            if (Hut && s == 0) continue;
            int northDepthZ0 = 1, northZ1 = Zs - 2, southZ0 = Zs + 3, southZ1 = D - 2;
            var segs = new List<(int band, int a, int b)>
            {
                (0, 1, Cx - 2), (0, CoreEnd + 1, W - 2), (1, 1, W - 2),
            };
            string? hall = P.Halls.Where(h => BandKeys(s).Contains(h.Band)).Select(h => h.Room).FirstOrDefault();
            foreach (var (band, a, b) in segs)
            {
                var pieces = band == 1 && hall != null ? new List<(int a, int b)> { (a, b) } : Split(a, b);
                if (band == 1 && hall != null)
                {
                    rooms.Add(new RoomPlan { Storey = s, X0 = a, X1 = b, Band = band, Z0 = southZ0, Z1 = southZ1, Def = Lib.Room(hall) });
                    continue;
                }
                if (pieces.Count == 0 && b - a + 1 >= 2)
                {
                    // A sliver too small for a room: walled up solid.
                    for (int x = a; x <= b; x++) SolidColumn(s, x, band == 0 ? northDepthZ0 : southZ0, band == 0 ? northZ1 : southZ1);
                    continue;
                }
                for (int i = 0; i < pieces.Count; i++)
                {
                    var (x0, x1) = pieces[i];
                    var r = new RoomPlan { Storey = s, X0 = x0, X1 = x1, Band = band, Z0 = band == 0 ? northDepthZ0 : southZ0, Z1 = band == 0 ? northZ1 : southZ1 };
                    // A hidden room: the plan's unexplained gap at the end of a run.
                    if (secretStoreys.Contains(s) && r.Width >= 9 && (i == 0 || i == pieces.Count - 1))
                    {
                        secretStoreys.Remove(s);
                        bool atStart = i == 0;
                        var hidden = new RoomPlan { Storey = s, Band = band, Z0 = r.Z0, Z1 = r.Z1, Secret = true };
                        if (atStart) { hidden.X0 = x0; hidden.X1 = x0 + 2; r.X0 = x0 + 4; }
                        else { hidden.X1 = x1; hidden.X0 = x1 - 2; r.X1 = x1 - 4; }
                        hidden.SecretFrom = r;
                        rooms.Add(hidden);
                    }
                    rooms.Add(r);
                }
            }
            Assign(s, rooms.Where(r => !r.Secret && r.Def == null).ToList());
        }
        // The prize: the deepest storey's room farthest from the stair becomes the vault.
        if (Spec.HoldsVault)
        {
            var deep = _rooms[Bottom].Where(r => !r.Secret).ToList();
            if (deep.Count == 0 && Hut) deep = _rooms.Where(kv => kv.Value.Count > 0).OrderBy(kv => kv.Key).First().Value.Where(r => !r.Secret).ToList();
            var far = deep.OrderByDescending(r => Math.Abs((r.X0 + r.X1) / 2 - (Cx + 2)) + (r.Width >= 5 ? 3 : 0)).First();
            var vault = deep.FirstOrDefault(r => r.Def?.Id == "vault");
            if (vault != null && vault != far) (vault.Def, far.Def) = (far.Def, vault.Def);
            else if (vault == null) far.Def = Lib.Room("vault");
        }
    }

    void SolidColumn(int s, int x, int z0, int z1)
    {
        int y = WalkY(s);
        for (int z = z0; z <= z1; z++)
            for (int k = 0; k < 3; k++) C.Set(L(x, y + k, z), Wall(L(x, y + k, z), false, s));
    }

    /// <summary>
    /// Gives each room a purpose: musts first to their best spot, then optional rooms, then
    /// fillers; then swaps any two rooms while that improves the plan's score.
    /// </summary>
    void Assign(int s, List<RoomPlan> rooms)
    {
        if (rooms.Count == 0) return;
        var musts = new List<RoomDef>();
        var optional = new List<RoomDef>();
        foreach (string key in BandKeys(s))
        {
            if (!P.Bands.TryGetValue(key, out var demands)) continue;
            foreach (var d in demands)
            {
                var def = Lib.Room(d.Room);
                if (d.Room == "vault" && !Spec.HoldsVault) continue;
                int n = Rng.Between(d.Min, d.Max);
                if (d.Must) { musts.Add(def); n--; }
                for (int i = 0; i < n; i++) optional.Add(def);
            }
        }
        Rng.Shuffle(optional);
        var wanted = musts.Concat(optional).ToList();
        var fill = P.Fill.Count > 0 ? P.Fill : new List<string> { "storage" };
        for (int i = 0; wanted.Count < rooms.Count; i++) wanted.Add(Lib.Room(fill[((i + s * 3 + 7) % fill.Count + fill.Count) % fill.Count]));
        if (wanted.Count > rooms.Count) wanted = wanted.Take(rooms.Count).ToList();

        foreach (var def in wanted)
        {
            RoomPlan? best = null;
            float bestScore = float.MinValue;
            foreach (var r in rooms.Where(r => r.Def == null))
            {
                float sc = Score(r, def, rooms);
                if (sc > bestScore) { bestScore = sc; best = r; }
            }
            if (best != null) best.Def = def;
        }
        for (int pass = 0; pass < 4; pass++)
        {
            bool improved = false;
            for (int i = 0; i < rooms.Count; i++)
                for (int j = i + 1; j < rooms.Count; j++)
                {
                    var a = rooms[i];
                    var b = rooms[j];
                    if (a.Def == b.Def) continue;
                    float before = Score(a, a.Def!, rooms) + Score(b, b.Def!, rooms);
                    (a.Def, b.Def) = (b.Def, a.Def);
                    float after = Score(a, a.Def!, rooms) + Score(b, b.Def!, rooms);
                    if (after > before + 0.01f) improved = true;
                    else (a.Def, b.Def) = (b.Def, a.Def);
                }
            if (!improved) break;
        }
    }

    IEnumerable<RoomPlan> Neighbours(RoomPlan r, List<RoomPlan> rooms) =>
        rooms.Where(o => o != r && !o.Secret && (
            (o.Band == r.Band && (o.X0 == r.X1 + 2 || o.X1 == r.X0 - 2)) ||                      // through a shared wall
            (o.Band != r.Band && Math.Min(o.X1, r.X1) - Math.Max(o.X0, r.X0) >= 1)));           // across the spine

    float Score(RoomPlan r, RoomDef def, List<RoomPlan> rooms)
    {
        float s = 0f;
        int w = r.Width;
        s += w >= def.WMin && w <= def.WMax ? 2f : -0.6f * (w < def.WMin ? def.WMin - w : w - def.WMax);
        float mid = (r.X0 + r.X1) / 2f;
        float toCore = Math.Abs(mid - (Cx + 2)) / W, toEntrance = mid / W;
        switch (def.Prefer)
        {
            case "entrance": s += Hut ? -toCore * 3f : -toEntrance * 4f; break;
            case "end": s += toCore * 3f + toEntrance * 1.5f; break;
            case "core": s -= toCore * 3f; break;
        }
        foreach (var n in Neighbours(r, rooms))
        {
            if (n.Def == null) continue;
            if (def.Near.Contains(n.Def.Id) || n.Def.Near.Contains(def.Id)) s += 1.5f;
            if (def.Avoid.Contains(n.Def.Id) || n.Def.Avoid.Contains(def.Id)) s -= 2.5f;
            if (n.Def == def && def.Has("cluster")) s += 1f;
        }
        return s;
    }

    // ---- building the rooms ----------------------------------------------------------------

    void BuildRooms(bool furnish = true)
    {
        foreach (var (s, rooms) in _rooms.OrderBy(kv => kv.Key))
        {
            int y = WalkY(s);
            foreach (var r in rooms)
            {
                // Walls between rooms in a band: the cell after each room.
                foreach (int wx in new[] { r.X0 - 1, r.X1 + 1 })
                    for (int z = r.Z0; z <= r.Z1; z++)
                        for (int k = 0; k < 3; k++)
                        {
                            Int3 p = L(wx, y + k, z);
                            if (C.Get(p).IsAir) C.Set(p, Wall(p, false, s));
                        }
                var cells = new List<Int3>();
                for (int z = r.Z0; z <= r.Z1; z++)
                    for (int x = r.X0; x <= r.X1; x++)
                        for (int k = 0; k < 3; k++) cells.Add(L(x, y + k, z));
                var def = r.Def ?? Lib.Room("storage");
                var kind = r.Secret ? ModuleKind.Room : def.Kind;
                r.Module = C.AddModule(kind, r.Purpose, s, cells);
                Out.Modules.Add(r.Module);
            }
            foreach (var r in rooms.Where(r => !r.Secret)) Door(r);
            foreach (var r in rooms.Where(r => !r.Secret)) InternalDoors(r, rooms);
            foreach (var r in rooms.Where(r => r.Secret)) Hidden(r);
            if (furnish) foreach (var r in rooms.Where(r => !r.Secret)) Furnish(r);
            Notes(s, rooms);
        }
        // The vault, and where the deepest point is.
        var vault = _rooms.SelectMany(kv => kv.Value).FirstOrDefault(r => r.Def?.Id == "vault");
        if (vault != null) Out.VaultModule = vault.Module;
        var deepest = _rooms[Bottom].Where(r => !r.Secret).Select(r => r.Module).DefaultIfEmpty(_spine[Bottom]).First();
        Out.DeepestModule = vault?.Module ?? deepest;
    }

    void Door(RoomPlan r)
    {
        int y = WalkY(r.Storey);
        int span = r.Width - 2;
        int dx = r.X0 + Math.Clamp(span / 2 + Rng.Between(-1, 1), 0, Math.Max(0, span));
        r.DoorX = dx;
        int wz = r.Band == 0 ? Zs - 1 : Zs + 2;
        var cells = new List<Int3>();
        for (int x = dx; x <= dx + 1; x++)
            for (int k = 0; k < 3; k++) cells.Add(L(x, y + k, wz));
        r.Door = C.AddDoorway(cells, R(r.Band == 0 ? Dir.North : Dir.South), _spine[r.Storey], r.Module);
    }

    void InternalDoors(RoomPlan r, List<RoomPlan> rooms)
    {
        var def = r.Def;
        if (def == null) return;
        foreach (var o in rooms.Where(o => !o.Secret && o.Band == r.Band && o.X0 == r.X1 + 2 && o.Def != null))
        {
            bool belong = def.Near.Contains(o.Def!.Id) || o.Def.Near.Contains(def.Id);
            if (!belong || o.Def.Lock != null || def.Lock != null) continue;
            int y = WalkY(r.Storey);
            int x = r.X1 + 1;
            int z = r.Band == 0 ? r.Z0 + 1 : r.Z1 - 2;
            var cells = new List<Int3>();
            for (int dz = 0; dz < 2; dz++)
                for (int k = 0; k < 3; k++) cells.Add(L(x, y + k, z + dz));
            C.AddDoorway(cells, R(Dir.East), r.Module, o.Module);
        }
    }

    /// <summary>
    /// A hidden room: its wall to the neighbouring room is a breakable section with tells on
    /// the near side (cracks, a draft, scuffs and scorch on the floor in front).
    /// </summary>
    void Hidden(RoomPlan r)
    {
        var from = r.SecretFrom!;
        int y = WalkY(r.Storey);
        int wx = from.X0 > r.X1 ? r.X1 + 1 : r.X0 - 1;
        int midZ = (r.Z0 + r.Z1) / 2;
        var secret = new SecretRoom { Module = r.Module, FromModule = from.Module, Facing = R(from.X0 > r.X1 ? Dir.East : Dir.West) };
        for (int dz = 0; dz < 2; dz++)
            for (int k = 0; k < 3; k++)
            {
                Int3 p = L(wx, y + k, midZ + dz);
                var b = S.InnerWall.Make(p);
                b.Type = BlockType.Breakable;
                b.Role = Role.Secret;
                b.Color = b.Color.Scale(0.9f).Lerp(new Rgb(0x8a, 0x7a, 0x60), 0.12f);
                b.Variant = (byte)(1 + dz + k * 2);
                C.Set(p, b);
                secret.WallCells.Add(p);
            }
        // Tells in front of it: scuffs where something heavy was dragged, scorch, a draft you can hear.
        int fx = from.X0 > r.X1 ? from.X0 : from.X1;
        Int3 front = L(fx, y, midZ);
        Int3 front2 = L(fx, y, midZ + 1);
        secret.TellCells.Add(front);
        secret.TellCells.Add(front2);
        secret.Tells.Add("cracked wall panel, a shade off the rest");
        secret.Tells.Add("a draft through the cracks");
        secret.Tells.Add("scuff marks on the floor in front");
        secret.Tells.Add("the corridor runs on with no door for this stretch");
        C.Set(front, new Block(BlockType.Scorch, Material.Ash, new Rgb(0x3a, 0x30, 0x28), Role.Story, Dir.North, 2));
        C.X.Sounds.Add(new SoundHook("draft", front2 + Int3.Up, 0.6f, r.Storey));
        C.X.Secrets.Add(secret);
        C.Link(from.Module, r.Module);
        // Inside: whoever hid here left their stash, and their camp.
        Int3 cache = L((r.X0 + r.X1) / 2, y, r.Band == 0 ? r.Z0 : r.Z1);
        C.Slots.Add(new Slot { Kind = SlotKind.Loot, Cell = cache, Module = r.Module, Sure = true, Secret = true });
        Int3 bedroll = L(r.X0, y, midZ + (r.Band == 0 ? 2 : -2));
        C.Set(bedroll, new Block(BlockType.Scatter, Material.Cloth, new Rgb(0x6a, 0x5a, 0x40), Role.Story));
        C.X.Story.Add(new StoryProp("hidden stash", cache, "Someone walled this space off and kept their best things here."));
    }

    // ---- furniture -----------------------------------------------------------------------

    static readonly Rgb Wood = new(0x6a, 0x4e, 0x36), CrateBrown = new(0x6e, 0x5a, 0x40), Linen = new(0xd6, 0xd8, 0xd2), Cryo = new(0x8f, 0xd8, 0xff);

    Block Prop(string kind, Int3 p, Dir facing)
    {
        Rgb accent = S.AccentColor, machine = S.Machine.Colors[0];
        return kind switch
        {
            "bench" => new Block(BlockType.Bench, Material.Alloy, machine.Lerp(new Rgb(0xb8, 0xbc, 0xc0), 0.4f), Role.Prop, facing),
            "machine" => new Block(BlockType.Machine, Material.Alloy, machine, Role.Prop, facing),
            "shelf" => new Block(BlockType.Shelf, Material.Steel, machine.Scale(0.8f), Role.Prop, facing),
            "locker" => new Block(BlockType.Locker, Material.Steel, accent.Lerp(new Rgb(0x50, 0x58, 0x60), 0.6f), Role.Prop, facing),
            "crate" => new Block(BlockType.Crate, Material.Scrap, CrateBrown.Lerp(accent, 0.2f), Role.Prop, facing),
            "table" => new Block(BlockType.Table, Material.Timber, Wood, Role.Prop, facing),
            "bunk" => new Block(BlockType.Bunk, Material.Cloth, (S.Kit.BySymbol('b')?.Colors[0]) ?? new Rgb(0x4f, 0x6d, 0x7a), Role.Prop, facing),
            "bed" => new Block(BlockType.Bed, Material.Cloth, Linen, Role.Prop, facing),
            "pod" => new Block(BlockType.Pod, Material.Lumen, Cryo, Role.Prop, facing),
            "tank" => new Block(BlockType.Tank, Material.Steel, machine.Lerp(accent, 0.25f), Role.Prop, facing),
            "statue" => new Block(BlockType.Statue, Material.Basalt, new Rgb(0x4a, 0x48, 0x50), Role.Prop, facing),
            "crystal" => new Block(BlockType.Crystal, Material.Crystal, new Rgb(0x80, 0xff, 0xe0), Role.Prop, facing),
            "cell" => new Block(BlockType.Lattice, Material.Steel, new Rgb(0x50, 0x54, 0x5a), Role.Prop, facing),
            "pillar" => new Block(BlockType.Pillar, Material.Concrete, S.Wall.Colors[0], Role.Prop, facing),
            "barricade" => new Block(BlockType.Barricade, Material.Timber, Wood.Scale(0.8f), Role.Story, facing),
            "plant" => new Block(BlockType.Plant, Material.Plant, new Rgb(0x4f, 0x8a, 0x3a), Role.Prop, facing),
            "planter" => new Block(BlockType.Crate, Material.Soil, new Rgb(0x4a, 0x3a, 0x2a), Role.Prop, facing),
            "screen" => new Block(BlockType.Pane, Material.Glass, new Rgb(0x9f, 0xd3, 0xe0), Role.Prop, facing),
            _ => new Block(BlockType.Crate, Material.Scrap, CrateBrown, Role.Prop, facing),
        };
    }

    string PickProp(RoomDef def)
    {
        if (def.Props.Count == 0) return "crate";
        int i = Rng.PickWeighted(def.Props.Select(p => p.Weight).ToList());
        return def.Props[Math.Max(0, i)].Prop;
    }

    void Furnish(RoomPlan r)
    {
        var def = r.Def ?? Lib.Room("storage");
        int y = WalkY(r.Storey);
        var interior = new List<(int x, int z)>();
        for (int z = r.Z0; z <= r.Z1; z++)
            for (int x = r.X0; x <= r.X1; x++) interior.Add((x, z));
        // Keep clear in front of every doorway into the room (two cells deep).
        var keep = new HashSet<(int, int)>();
        foreach (var d in C.Doorways.Where(d => d.ModuleA == r.Module || d.ModuleB == r.Module))
            foreach (Int3 dc in d.Cells.Where(c => c.Y == y))
                foreach (Dir h in Dirs.Horizontal)
                    for (int step = 1; step <= 2; step++)
                    {
                        Int3 q = dc + h.Vec() * step;
                        foreach (var (x, z) in interior)
                            if (L(x, y, z) == q) keep.Add((x, z));
                    }
        foreach (var s in C.X.Secrets.Where(s => s.FromModule == r.Module))
            foreach (Int3 t in s.TellCells)
                foreach (var (x, z) in interior)
                    if (L(x, y, z) == t) { keep.Add((x, z)); }
        var blocked = new HashSet<(int, int)>();
        var needsAccess = new List<(int x, int z)>();
        // Whatever already stands here (a rubble slope from a collapse) is in the way; the foot of a slope stays clear.
        foreach (var (x, z) in interior)
        {
            var b = C.Get(L(x, y, z));
            if (b.IsAir || !BlockInfo.Collides(b.Type)) continue;
            blocked.Add((x, z));
            if (BlockInfo.IsSlope(b.Type))
                foreach (var (fx, fz) in interior)
                    if (L(fx, y, fz) == L(x, y, z) - b.Facing.Vec()) keep.Add((fx, fz));
        }
        bool Perimeter((int x, int z) c) => c.x == r.X0 || c.x == r.X1 || c.z == r.Z0 || c.z == r.Z1;
        Dir InwardOf((int x, int z) c) => c.z == r.Z0 ? R(Dir.South) : c.z == r.Z1 ? R(Dir.North) : c.x == r.X0 ? R(Dir.East) : R(Dir.West);

        bool Connected()
        {
            var start = interior.FirstOrDefault(c => keep.Contains(c) && !blocked.Contains(c));
            if (start == default && !keep.Any()) start = interior.First(c => !blocked.Contains(c));
            var seen = new HashSet<(int, int)> { start };
            var q = new Queue<(int x, int z)>();
            q.Enqueue(start);
            while (q.Count > 0)
            {
                var (x, z) = q.Dequeue();
                foreach (var n in new[] { (x + 1, z), (x - 1, z), (x, z + 1), (x, z - 1) })
                    if (n.Item1 >= r.X0 && n.Item1 <= r.X1 && n.Item2 >= r.Z0 && n.Item2 <= r.Z1 && !blocked.Contains(n) && seen.Add(n)) q.Enqueue(n);
            }
            if (!interior.All(c => blocked.Contains(c) || seen.Contains(c))) return false;
            // Every cache, terminal and core keeps a free cell beside it to walk up to.
            foreach (var (ax, az) in needsAccess)
                if (!new[] { (ax + 1, az), (ax - 1, az), (ax, az + 1), (ax, az - 1) }.Any(seen.Contains)) return false;
            return true;
        }

        bool Take((int x, int z) c)
        {
            if (keep.Contains(c) || blocked.Contains(c)) return false;
            blocked.Add(c);
            if (Connected()) return true;
            blocked.Remove(c);
            return false;
        }

        var perimeter = interior.Where(Perimeter).ToList();
        Rng.Shuffle(perimeter);
        // Corners first for loot: caches tucked into corners and along the back wall.
        var corners = perimeter.OrderByDescending(c => ((c.x == r.X0 || c.x == r.X1) ? 1 : 0) + ((c.z == r.Z0 || c.z == r.Z1) ? 1 : 0)).ToList();

        int loot = Rng.Between(def.LootMin, def.LootMax) + (def.Id == "vault" ? Math.Min(3, (int)Rarity) : 0);
        foreach (var c in corners)
        {
            if (loot <= 0) break;
            needsAccess.Add(c);
            if (!Take(c)) { needsAccess.Remove(c); continue; }
            C.Slots.Add(new Slot { Kind = SlotKind.Loot, Cell = L(c.x, y, c.z), Module = r.Module, Sure = true });
            loot--;
        }
        int terms = Rng.Between(def.TerminalMin, def.TerminalMax);
        foreach (var c in perimeter)
        {
            if (terms <= 0) break;
            needsAccess.Add(c);
            if (!Take(c)) { needsAccess.Remove(c); continue; }
            C.Slots.Add(new Slot { Kind = SlotKind.Terminal, Cell = L(c.x, y, c.z), Module = r.Module, Sure = true });
            terms--;
        }
        if (def.Has("core") && Fam.Core == CoreRule.Owned && C.CoreModule < 0 && Spec.MayHoldCore)
        {
            var spot = interior.Where(c => !Perimeter(c) && !keep.Contains(c)).OrderBy(c => Math.Abs(c.x - (r.X0 + r.X1) / 2) + Math.Abs(c.z - (r.Z0 + r.Z1) / 2)).FirstOrDefault();
            if (spot != default) needsAccess.Add(spot);
            if (spot != default && Take(spot))
            {
                C.Slots.Add(new Slot { Kind = SlotKind.Core, Cell = L(spot.x, y, spot.z), Module = r.Module, Sure = true });
                C.CoreModule = r.Module;
            }
        }
        // Furniture along the walls.
        foreach (var c in perimeter)
        {
            if (blocked.Contains(c) || !Rng.Chance(def.Fill)) continue;
            if (!Take(c)) continue;
            string prop = PickProp(def);
            Int3 p = L(c.x, y, c.z);
            C.Set(p, Prop(prop, p, InwardOf(c)));
            if (prop is "shelf" or "tank" && Rng.Chance(0.5f))
            {
                Int3 up = p + Int3.Up;
                C.Set(up, Prop(prop == "shelf" ? "crate" : "tank", up, InwardOf(c)));
            }
            if (prop == "planter") C.Set(p + Int3.Up, Prop("plant", p + Int3.Up, Dir.North));
        }
        // Islands: rows of pods or beds with aisles, tables in the middle.
        if (def.Island.Count > 0 && r.Width >= 6 && r.Depth >= 5)
        {
            string island = def.Island[Rng.Range(0, def.Island.Count)];
            for (int z = r.Z0 + 2; z <= r.Z1 - 2; z++)
            {
                if ((z - r.Z0) % 3 == 0) continue; // an aisle every third row
                for (int x = r.X0 + 2; x <= r.X1 - 2; x++)
                {
                    if ((x - r.X0) % 4 == 0) continue; // and a gap to walk round the ends
                    var c = (x, z);
                    if (!Take(c)) continue;
                    Int3 p = L(x, y, z);
                    C.Set(p, Prop(island, p, R((z - r.Z0) % 3 == 1 ? Dir.North : Dir.South)));
                }
            }
        }
        // Who waits here, and where a story can be told.
        var free = interior.Where(c => !blocked.Contains(c) && !keep.Contains(c)).ToList();
        Rng.Shuffle(free);
        int spawns = Rng.Between(def.SpawnMin, def.SpawnMax);
        foreach (var c in free.Take(spawns)) C.Slots.Add(new Slot { Kind = SlotKind.Spawn, Cell = L(c.x, y, c.z), Module = r.Module, Sure = true });
        var storySpot = free.Skip(spawns).FirstOrDefault(Perimeter);
        if (storySpot != default) C.Slots.Add(new Slot { Kind = SlotKind.Prop, Cell = L(storySpot.x, y, storySpot.z), Module = r.Module, Sure = true });
        if (def.Kind is ModuleKind.Hall or ModuleKind.Vault && free.Count > spawns + 2)
            C.Slots.Add(new Slot { Kind = SlotKind.Boss, Cell = L(free[^1].x, y, free[^1].z), Module = r.Module, Sure = true });

        // Light: a sconce in the back wall, and a lamp for the view.
        if (def.Light != "none")
        {
            int bx = (r.X0 + r.X1) / 2, bz = r.Band == 0 ? r.Z0 - 1 : r.Z1 + 1;
            Int3 sconce = L(bx, y + 2, bz);
            var sb = C.Get(sconce);
            if (sb.Type == BlockType.Cube) C.Set(sconce, S.Light.Make(sconce));
            C.X.Lights.Add(new LightSpot
            {
                Cell = L(bx, y + 2, (r.Z0 + r.Z1) / 2), Color = LightColor(def.Light), Module = r.Module, Storey = r.Storey,
                Mode = def.Light == "red" ? LightMode.Emergency : def.Light == "glyph" ? LightMode.Pulse : LightMode.Steady,
                Energy = def.Light == "dim" ? 0.8f : 1.3f, Range = Math.Max(r.Width, r.Depth) * 0.9f + 2f,
            });
        }
        if (def.Has("hum")) C.X.Sounds.Add(new SoundHook("hum", L((r.X0 + r.X1) / 2, y + 1, (r.Z0 + r.Z1) / 2), 0.8f, r.Storey));
    }

    Rgb LightColor(string kind) => kind switch
    {
        "cold" => new Rgb(0xe6, 0xf2, 0xff),
        "dim" => new Rgb(0xc8, 0xb0, 0x90),
        "red" => new Rgb(0xff, 0x30, 0x20),
        "green" => new Rgb(0x9c, 0xff, 0x8a),
        "glyph" => new Rgb(0x80, 0xff, 0xe0),
        "cryo" => new Rgb(0x8f, 0xd8, 0xff),
        _ => S.Kit.Light,
    };

    void Notes(int s, List<RoomPlan> rooms)
    {
        var real = rooms.Where(r => !r.Secret && r.Def != null).ToList();
        foreach (var r in real)
        {
            var def = r.Def!;
            var neighbours = Neighbours(r, real).Where(n => n.Def != null).ToList();
            bool possible = def.Near.Any(n => real.Any(o => o != r && o.Def!.Id == n));
            C.X.Rooms.Add(new RoomNote
            {
                Module = r.Module, Purpose = def.Id, Storey = s,
                HasNearRule = def.Near.Count > 0 && possible,
                NearSatisfied = neighbours.Any(n => def.Near.Contains(n.Def!.Id) || n.Def!.Near.Contains(def.Id)),
                AvoidBroken = neighbours.Any(n => def.Avoid.Contains(n.Def!.Id)),
                Neighbours = neighbours.Select(n => n.Def!.Id).ToList(),
            });
        }
    }

    // ---- keycards ----------------------------------------------------------------------

    static readonly string[] Clearances = { "staff", "science", "security", "command" };

    /// <summary>
    /// Secure rooms get locked doors; each clearance's keycard lies in a room you can reach
    /// without it, on the same storey or above, preferably where someone would have carried it.
    /// </summary>
    void Locks()
    {
        var all = _rooms.SelectMany(kv => kv.Value).Where(r => !r.Secret && r.Def != null && r.Door >= 0).ToList();
        var locked = new List<(RoomPlan Room, string Clearance)>();
        foreach (var r in all)
        {
            string? c = r.Def!.Lock;
            if (r.Def.Id == "vault") c ??= "command";
            if (c == null) continue;
            locked.Add((r, c));
            C.X.Locks.Add(new DoorLock { Doorway = r.Door, Clearance = c, Blast = r.Def.Blast });
        }
        foreach (string clearance in Clearances)
        {
            var doors = locked.Where(l => l.Clearance == clearance).ToList();
            if (doors.Count == 0) continue;
            int shallowest = doors.Max(l => l.Room.Storey);
            int rank = Array.IndexOf(Clearances, clearance);
            var candidates = all.Where(r => r.Storey >= shallowest - 0 && !locked.Any(l => l.Room == r && Array.IndexOf(Clearances, l.Clearance) >= rank))
                .OrderByDescending(r => r.Def!.Has("keyspot") ? 2 : 0).ThenBy(r => Math.Abs(r.Storey - shallowest)).ThenBy(_ => Rng.Float()).ToList();
            if (candidates.Count == 0)
                candidates = all.Where(r => !locked.Any(l => l.Room == r)).OrderByDescending(r => r.Storey).ToList();
            if (candidates.Count == 0) continue;
            var room = candidates[0];
            int y = WalkY(room.Storey);
            // On whoever carried it: remains by the wall.
            Int3? spot = null;
            foreach (int z in new[] { room.Band == 0 ? room.Z0 : room.Z1, (room.Z0 + room.Z1) / 2 })
                for (int x = room.X0; x <= room.X1 && spot == null; x++)
                {
                    Int3 p = L(x, y, z);
                    if (C.Get(p).IsAir && !C.Slots.Any(s => s.Cell == p) && !C.Doorways.Any(d => d.Cells.Any(dc => dc.Manhattan(p) <= 1))) spot = p;
                }
            if (spot is not Int3 sp) continue;
            C.Set(sp, new Block(BlockType.Remains, Material.Cloth, new Rgb(0x7a, 0x74, 0x64), Role.Story, R(Dir.North), 2));
            C.X.Keys.Add(new KeyCard { Clearance = clearance, Cell = sp, Module = room.Module, On = "remains" });
            C.X.Story.Add(new StoryProp("keycard", sp, $"A {clearance} keycard, still clipped to what's left of its owner."));
        }
    }

    // ---- below ground ------------------------------------------------------------------

    /// <summary>
    /// The deepest storey breaks into a natural cave at the far end of its spine: the plan stops
    /// making sense here. Flat-floored so it walks, with a ragged roof and walls.
    /// </summary>
    void Cavern()
    {
        int s = Bottom, y = WalkY(s);
        var cells = new List<Int3>();
        int len = Rng.Between(12, 20);
        float phase = Rng.Float(0f, 6.28f);
        int cz = Zs;
        for (int dx = 0; dx < len; dx++)
        {
            int x = W - 1 + dx;
            float t = dx / (float)len;
            int half = 2 + (int)(MathF.Sin(t * 3.1f) * 4f + MathF.Sin(phase + dx * 0.7f) * 1.2f);
            half = Math.Max(1, half);
            int wobble = (int)MathF.Round(MathF.Sin(phase * 2 + dx * 0.35f) * 2f);
            int height = 3 + (int)(MathF.Sin(t * 3.1f) * 5f) + (dx % 3 == 0 ? 1 : 0);
            for (int z = cz - half + wobble; z <= cz + 1 + half + wobble; z++)
                for (int k = 0; k < Math.Max(3, height); k++)
                {
                    Int3 p = L(x, y + k, z);
                    if (C.Blocks.ContainsKey(p)) continue;
                    C.Clear(p);
                    cells.Add(p);
                    if (k == 0) C.X.CorridorCells.Remove(p);
                }
        }
        // Break through the spine's end wall.
        for (int z = Zs; z <= Zs + 1; z++)
            for (int k = 0; k < 3; k++)
            {
                Int3 p = L(W - 1, y + k, z);
                C.Clear(p);
                cells.Add(p);
            }
        var floorCells = cells.Where(p => p.Y == y).ToList();
        // Rubble at the breakthrough, a glow from something growing in the dark.
        Int3 edge = L(W - 1, y, Zs - 1);
        if (C.Get(edge).Type == BlockType.Cube) C.Set(edge + Int3.Up * 0, new Block(BlockType.Cube, Material.Rock, new Rgb(0x4a, 0x42, 0x3c), Role.Wall));
        int cave = C.AddModule(ModuleKind.Cave, "cavern", s, cells.Distinct().ToList());
        Out.Modules.Add(cave);
        C.Link(_spine[s], cave);
        var far = floorCells.OrderByDescending(p => (p - L(W - 1, y, Zs)).Manhattan(Int3.Zero)).ToList();
        C.Slots.Add(new Slot { Kind = SlotKind.Loot, Cell = far[0], Module = cave, Sure = true });
        C.Slots.Add(new Slot { Kind = SlotKind.Boss, Cell = far[Math.Min(far.Count - 1, 6)], Module = cave, Sure = true });
        foreach (Int3 sp in far.Skip(3).Where((_, i) => i % 9 == 0).Take(3))
            C.Slots.Add(new Slot { Kind = SlotKind.Spawn, Cell = sp, Module = cave, Sure = true });
        C.X.Lights.Add(new LightSpot { Cell = far[0] + Int3.Up * 2, Color = new Rgb(0xb8, 0xf0, 0x60), Mode = LightMode.Bloom, Energy = 1.4f, Range = 10f, Module = cave, Storey = s });
        C.X.Sounds.Add(new SoundHook("breathing", far[0] + Int3.Up, 0.7f, s));
        C.X.Story.Add(new StoryProp("breakthrough", L(W - 1, y, Zs), "The wall at the end of the corridor was dug out from the other side."));
        // The prize moves into the dark: the cave is the deepest point.
        Out.DeepestModule = cave;
        if (Spec.HoldsVault)
        {
            var vaultPlan = _rooms[Bottom].FirstOrDefault(r => r.Def?.Id == "vault");
            if (vaultPlan == null) Out.VaultModule = cave;
        }
    }

    /// <summary>
    /// The ground and the ceiling caved in over a room in the top basement: a hole in the bunker
    /// roof with a rubble slope down into the room, daylight and weather falling in.
    /// </summary>
    void CollapseBreach()
    {
        int s = -1, y = WalkY(s);
        var f0 = Foot(0);
        var candidates = _rooms[s].Where(r => !r.Secret && r.Def != null && r.Def.Id != "vault" && r.Width >= 7 && r.Depth >= 4)
            .Where(r => r.X1 + 2 < f0.x0 || r.X0 - 2 > f0.x1 || r.Band == 1).ToList();
        if (candidates.Count == 0) return;
        var room = candidates[Rng.Range(0, candidates.Count)];
        int rise = -y; // up to the surface
        int x0 = room.X0 + 1;
        int z0 = room.Band == 0 ? room.Z0 : room.Z1 - 1;
        if (x0 + rise + 1 > room.X1) return;
        var breach = new Breach { Module = room.Module };
        var rubble = new Rgb(0x6a, 0x60, 0x58);
        for (int i = 0; i < rise; i++)
            for (int dz = 0; dz < 2; dz++)
            {
                int x = x0 + i, z = z0 + dz;
                for (int k = 0; k < i; k++)
                {
                    Int3 fill = L(x, y + k, z);
                    C.Set(fill, new Block(BlockType.Cube, Material.Rock, rubble.Scale(0.85f + 0.1f * (k % 2)), Role.Debris));
                }
                Int3 ramp = L(x, y + i, z);
                var rb = new Block(BlockType.Ramp, Material.Rock, rubble, Role.Debris, R(Dir.East));
                C.Set(ramp, rb);
                // Headroom above the slope, through the ceiling and up into the open.
                for (int k = 1; k <= 3; k++)
                {
                    Int3 h = L(x, y + i + k, z);
                    if (h.Y < 0) C.Clear(h);
                    else C.Remove(h);
                    if (h.Y <= -1) breach.Cells.Add(h);
                }
            }
        // Widen the hole: the ceiling slab above the slope and beside it has fallen in.
        for (int x = x0 - 1; x < x0 + rise; x++)
            for (int dz = -1; dz <= 2; dz++)
            {
                int z = z0 + dz;
                if (z < room.Z0 || z > room.Z1 || x < room.X0 || x > room.X1) continue;
                Int3 ceil = L(x, -1, z);
                if (C.Get(ceil).Role is Role.Floor or Role.Roof)
                {
                    C.Clear(ceil);
                    breach.Cells.Add(ceil);
                }
            }
        // Cracked slab around the lip, rubble on the surface: a strange hole you can see from afar.
        for (int x = x0 - 3; x <= x0 + rise + 2; x++)
            for (int dz = -3; dz <= 4; dz++)
            {
                Int3 lip = L(x, -1, z0 + dz);
                var b = C.Get(lip);
                if (b.Role == Role.Floor && Hash.Cell(lip, 91) % 3 == 0)
                {
                    b.Color = b.Color.Lerp(new Rgb(0x2a, 0x24, 0x20), 0.5f);
                    C.Set(lip, b);
                }
                Int3 top = lip + Int3.Up;
                if (C.Get(top).IsAir && !C.Get(lip).IsAir && Hash.Cell(top, 92) % 5 == 0 && !breach.Cells.Contains(lip))
                    C.Set(top, new Block(BlockType.Rubble, Material.Rock, rubble, Role.Debris));
            }
        breach.SurfaceCell = L(x0 + rise, 0, z0);
        C.Remove(breach.SurfaceCell);
        C.Remove(breach.SurfaceCell + Int3.Up);
        C.X.Breaches.Add(breach);
        C.X.Sounds.Add(new SoundHook("wind", L(x0 + 1, y + 2, z0), 0.7f, s));
        C.X.Story.Add(new StoryProp("collapse", L(x0, y, z0), "The roof came down here. Daylight and weather have been getting in ever since."));
    }

    // ---- the roof and the levels ----------------------------------------------------------

    void Roofscape()
    {
        if (Hut || Floors == 0) return;
        int y = WalkY(Top) + 4; // standing on the roof
        if (P.Roof == "pad")
        {
            // A landing pad painted on the roof: a ring and a cross.
            int cxr = W * 3 / 4, czr = D / 2;
            for (int dz = -4; dz <= 4; dz++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    int x = cxr + dx, z = czr + dz;
                    if (x < 1 || x > W - 2 || z < 1 || z > D - 2) continue;
                    float r = MathF.Sqrt(dx * dx + dz * dz);
                    bool mark = (r > 3.2f && r < 4.3f) || (Math.Abs(dx) <= 0 && Math.Abs(dz) <= 2) || (Math.Abs(dz) <= 0 && Math.Abs(dx) <= 2);
                    if (!mark) continue;
                    Int3 p = L(x, y - 1, z);
                    if (C.Get(p).Role == Role.Roof) C.Set(p, new Block(BlockType.Cube, Material.Concrete, new Rgb(0xe8, 0xd0, 0x40), Role.Roof));
                }
            C.X.Markers.Add(new Marker("landing-pad", L(cxr, y, czr)));
        }
        // Plant on the roof: tanks, a mast with a beacon (a light you see from far off).
        Int3 mast = L(W - 3, y, 2);
        for (int k = 0; k < 6 + Floors; k++) C.Set(mast + Int3.Up * k, new Block(BlockType.Pillar, Material.Steel, new Rgb(0x60, 0x64, 0x68), Role.Decor));
        C.Set(mast + Int3.Up * (6 + Floors), new Block(BlockType.Cube, Material.Lumen, new Rgb(0xff, 0x40, 0x30), Role.Light));
        C.X.Lights.Add(new LightSpot { Cell = mast + Int3.Up * (6 + Floors), Color = new Rgb(0xff, 0x40, 0x30), Mode = LightMode.Beacon, Energy = 2f, Range = 14f, Storey = Top + 1 });
        for (int i = 0; i < 2; i++)
        {
            Int3 t = L(3 + i * 2, y, D - 3);
            C.Set(t, new Block(BlockType.Tank, Material.Steel, new Rgb(0x8a, 0x8e, 0x90), Role.Decor));
            C.Set(t + Int3.Up, new Block(BlockType.Tank, Material.Steel, new Rgb(0x8a, 0x8e, 0x90), Role.Decor));
        }
        Int3 ac = L(W / 2, y, D - 3);
        C.Set(ac, new Block(BlockType.Machine, Material.Alloy, new Rgb(0x7a, 0x80, 0x88), Role.Decor));
    }

    void Levelinfo()
    {
        for (int s = Bottom; s <= StairTop; s++)
        {
            if (C.X.Levels.Any(l => l.Storey == s)) continue;
            string name = s switch
            {
                0 => "ground floor",
                > 0 when s > Top => "roof",
                > 0 => $"floor {s}",
                _ => s == Bottom ? $"sublevel {-s} (deepest)" : $"sublevel {-s}",
            };
            C.X.Levels.Add(new LevelInfo { Storey = s, WalkY = WalkY(s), Name = name });
        }
        C.X.Levels.Sort((a, b) => a.Storey.CompareTo(b.Storey));
    }
}
