#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// The 1 m standard read off a finished place (docs/one-metre-grid.md, sections 6 and 10): what a check counts and a
/// test prints. Plain C#, so the generator's tests and the game's both read places the same way.
/// <list type="bullet">
/// <item>Doors are 1x2x1 or 2x2x1 (width x height x depth, m), larger only for a vehicle's way (3 m or more each way);
/// never 1x1. A door's leaf runs along the wall it is set in (its doorway faces across), and it has floor under it.</item>
/// <item>Every directional piece faces as what it sits in says: a stair rises from open floor to open floor, a ladder's
/// back is on its wall, a console, locker or sign backs onto its wall and looks into the room, a bed's foot is never
/// against the wall its head should be.</item>
/// <item>Loose things rest on something (a floor, the ground, a crate) and nothing is set into the rock.</item>
/// <item>Every room has a way in a person can walk.</item>
/// <item>Rooms are 3 to 4 m clear under their ceilings (halls and shafts may go higher).</item>
/// </list>
/// </summary>
public sealed class PlaceAudit
{
    /// <summary>Faults by kind ("door: turned", "stair: blocked", ...), each with how many and the first cell.</summary>
    public readonly Dictionary<string, (int Count, Int3 First)> Faults = new();
    /// <summary>Doors by size, "WxHxD".</summary>
    public readonly Dictionary<string, int> DoorSizes = new();
    /// <summary>Rooms by their clear height, m.</summary>
    public readonly Dictionary<int, int> RoomHeights = new();
    /// <summary>Rooms with no way in, by what they are for.</summary>
    public readonly Dictionary<string, int> Shut = new();
    /// <summary>Rooms under 3 m clear, by what they are for.</summary>
    public readonly Dictionary<string, int> Low = new();
    public int Doors, Pieces, Rooms;

    public int Count(string kind) => Faults.TryGetValue(kind, out var f) ? f.Count : 0;
    public int FacingFaults => Faults.Where(f => f.Key.Contains("turned") || f.Key.Contains("backwards")).Sum(f => f.Value.Count);

    void Fault(string kind, Int3 at)
    {
        Faults[kind] = Faults.TryGetValue(kind, out var f) ? (f.Count + 1, f.First) : (1, at);
    }

    /// <summary>Things that stand on the floor of their cell: each needs something under it.</summary>
    public static bool Rests(BlockType t) => t is BlockType.Crate or BlockType.Rubble or BlockType.Scatter or BlockType.Ash
        or BlockType.Remains or BlockType.Scorch or BlockType.Puddle or BlockType.Table or BlockType.Meal or BlockType.Bunk
        or BlockType.Bed or BlockType.Bench or BlockType.Locker or BlockType.Shelf or BlockType.Tank or BlockType.Machine
        or BlockType.Terminal or BlockType.Cache or BlockType.Barricade or BlockType.Statue or BlockType.Pump or BlockType.Pod
        or BlockType.Core;

    /// <summary>Pieces that back onto a wall and look into the room (their front is Facing).</summary>
    static bool Backed(BlockType t) => t is BlockType.Terminal or BlockType.Locker;

    public static PlaceAudit Of(PoiInstance inst) => Of(inst.Layout, inst.Blocks, inst.Nav());

    public static PlaceAudit Of(Layout layout, BlockGrid g, NavGrid nav)
    {
        var a = new PlaceAudit();
        a.ReadDoors(layout, g, nav);
        a.ReadPieces(g, nav);
        a.ReadRooms(layout, g, nav);
        return a;
    }

    bool Wall(BlockGrid g, NavGrid nav, Int3 p)
    {
        Block b = g.Get(p);
        return b.IsAir ? nav.IsRock(p) : BlockInfo.IsFullCube(b.Type) || b.Type is BlockType.Pane or BlockType.Lattice or BlockType.SealedDoor or BlockType.Pillar or BlockType.Rib;
    }

    static bool Floor(BlockGrid g, NavGrid nav, Int3 below) => Settle.Holds(g, nav, below);

    void ReadDoors(Layout layout, BlockGrid g, NavGrid nav)
    {
        var groups = layout.Doorways.Select(d => (d.Cells, d.Facing)).ToList();
        groups.Add((layout.ExitCells, layout.EntranceFacing));
        var seen = new HashSet<Int3>();
        foreach (var (cells, facing) in groups)
        {
            var doors = cells.Where(c => g.Get(c).Type == BlockType.Door && seen.Add(c)).ToList();
            if (doors.Count == 0) continue;
            Doors++;
            Int3 lo = doors.Aggregate(Int3.Min), hi = doors.Aggregate(Int3.Max);
            int ex = hi.X - lo.X + 1, ez = hi.Z - lo.Z + 1, h = hi.Y - lo.Y + 1;
            bool alongX = facing is Dir.North or Dir.South;
            int w = alongX ? ex : ez, d = alongX ? ez : ex;
            string size = $"{w}x{h}x{d}";
            DoorSizes[size] = DoorSizes.GetValueOrDefault(size) + 1;
            // The leaf runs along the wall: the doorway's long side is across its facing, and the cells either end of the
            // leaf (at its foot) are wall, while the cells in front of and behind it are not.
            Int3 endA = alongX ? lo + new Int3(-1, 0, 0) : lo + new Int3(0, 0, -1);
            Int3 endB = alongX ? hi with { Y = lo.Y } + new Int3(1, 0, 0) : hi with { Y = lo.Y } + new Int3(0, 0, 1);
            Int3 frontA = lo - facing.Vec(), frontB = lo + facing.Vec();
            bool endsWall = Wall(g, nav, endA) && Wall(g, nav, endB);
            bool acrossWall = Wall(g, nav, frontA) && Wall(g, nav, frontB);
            if (d > w || d == w && !endsWall && acrossWall) Fault("door: turned 90 degrees", lo);
            foreach (Int3 c in doors)
                if (g.Get(c).Facing.IsHorizontal() && (g.Get(c).Facing is Dir.North or Dir.South) != alongX) { Fault("door: leaf block turned", c); break; }
            if (w == 1 && h == 1) Fault("door: 1x1", lo);
            else if (d > 1) Fault("door: deeper than 1 m", lo);
            else if (!(h == 2 && w is 1 or 2) && !(w >= 3 && h >= 3)) Fault("door: off the 1x2 and 2x2 standard", lo);
            foreach (Int3 c in doors)
                if (c.Y == lo.Y && !Floor(g, nav, c + Int3.Down)) { Fault("door: no floor under it", c); break; }
        }
    }

    void ReadPieces(BlockGrid g, NavGrid nav)
    {
        foreach (var (p, b) in g.Blocks())
        {
            Dir f = b.Facing;
            switch (b.Type)
            {
                case BlockType.Stair:
                {
                    if (!f.IsHorizontal()) break;
                    Pieces++;
                    // Climbed from the open cell at its foot, to the open cell over its head: neither end is walled.
                    Block foot = g.Get(p - f.Vec()), top = g.Get(p + f.Vec() + Int3.Up);
                    bool footShut = BlockInfo.IsFullCube(foot.Type) || foot.IsAir && nav.IsRock(p - f.Vec());
                    bool topShut = BlockInfo.IsFullCube(top.Type) || top.IsAir && nav.IsRock(p + f.Vec() + Int3.Up);
                    if (footShut && topShut) Fault("stair: turned (both ends blocked)", p);
                    else if (footShut && !(BlockInfo.IsSlope(g.Get(p - f.Vec() + Int3.Down).Type))) Fault("stair: foot blocked", p);
                    else if (topShut) Fault("stair: top blocked", p);
                    break;
                }
                case BlockType.Ladder:
                {
                    if (!f.IsHorizontal() || g.Get(p + Int3.Down).Type == BlockType.Ladder) break;
                    // A run at a time, from its foot: turned if another way has more of its wall at the run's back.
                    var run = new List<Int3>();
                    for (Int3 q = p; g.Get(q).Type == BlockType.Ladder; q += Int3.Up) run.Add(q);
                    Pieces += run.Count;
                    int Backs(Dir d) => run.Count(q => Wall(g, nav, q - d.Vec()) && !Wall(g, nav, q + d.Vec()));
                    int own = run.Count(q => Wall(g, nav, q - f.Vec()));
                    if (Dirs.Horizontal.Any(d => d != f && Backs(d) > own)) Fault("ladder: turned off its wall", p);
                    break;
                }
                case BlockType.Sign:
                {
                    if (!f.IsHorizontal()) break;
                    Pieces++;
                    // A plate across its facing: on a wall behind it or in front of it, never with its edge to the wall.
                    bool flat = Wall(g, nav, p - f.Vec()) || Wall(g, nav, p + f.Vec());
                    bool edge = Wall(g, nav, p + f.RotateCw(1).Vec()) || Wall(g, nav, p + f.RotateCw(3).Vec());
                    if (!flat && edge) Fault("sign: turned edge-on to its wall", p);
                    break;
                }
                case BlockType.Bed:
                {
                    if (!f.IsHorizontal()) break;
                    Pieces++;
                    // Head toward Facing: a bed with its foot on the wall and its head in the room is backwards.
                    if (Wall(g, nav, p - f.Vec()) && !Wall(g, nav, p + f.Vec()) && g.Get(p + f.Vec()).Type != BlockType.Bed) Fault("bed: backwards", p);
                    break;
                }
                default:
                    if (Backed(b.Type) && f.IsHorizontal())
                    {
                        Pieces++;
                        bool front = Wall(g, nav, p + f.Vec()), back = Wall(g, nav, p - f.Vec());
                        // Wrong only where its spot offers a right way: a wall to back onto with the room in front.
                        bool could = Dirs.Horizontal.Any(d => Wall(g, nav, p - d.Vec()) && !Wall(g, nav, p + d.Vec()));
                        if (front && !back && Dirs.Horizontal.Any(d => !Wall(g, nav, p + d.Vec()))) Fault($"{b.Type.ToString().ToLowerInvariant()}: backwards (faces its wall)", p);
                        else if (!(back && !front) && could) Fault($"{b.Type.ToString().ToLowerInvariant()}: turned (side to its wall)", p);
                    }
                    break;
            }
            // A floor's finish is paint on the top of the block under it (never a plate of its own): there must be one.
            if (BlockInfo.IsFinish(b.Type) && !Settle.Painted(g, nav, p + Int3.Down)) Fault("finish: painted on nothing", p);
            // Nothing set into the rock: below ground, things stand only in the place's own dug cells.
            if (nav.IsRock(p) && b.Role is Role.Debris or Role.Prop or Role.Loot or Role.Story or Role.Growth) Fault($"in the rock: {b.Type}", p);
            // Loose things rest on what is under them.
            else if (Rests(b.Type) && !Floor(g, nav, p + Int3.Down)) Fault($"rests on nothing: {b.Type}", p);
        }
    }

    void ReadRooms(Layout layout, BlockGrid g, NavGrid nav)
    {
        if (!nav.Standable(layout.EntranceCell)) return;
        var reach = nav.Reachable(layout.EntranceCell);
        // A hidden room's way in is its breakable wall: found from the room it faces, opened by a shot or a dig.
        var secret = layout.Extras.Secrets.Select(s => s.Module).ToHashSet();
        foreach (PlacedModule m in layout.Modules)
        {
            // (A room bricked up whole, its plan's cells filled in, is a wall now, not a room.)
            if (m.Kind is not (ModuleKind.Room or ModuleKind.Hall or ModuleKind.Vault) || m.Shape.Occupied.Count == 0) continue;
            Rooms++;
            if (!secret.Contains(m.Index) && Fixtures.RoomCell(m, reach) == null)
            {
                Fault("room: no way in", m.Min);
                Shut[m.Purpose] = Shut.GetValueOrDefault(m.Purpose) + 1;
            }
            // Clear height: from the floor up to the first solid, over the room's floor cells; the room's commonest.
            var heights = new Dictionary<int, int>();
            foreach (Int3 c in m.Cells)
            {
                // At the room's own floor (what the years heaped on it raises nothing).
                if (c.Y != m.Min.Y || !nav.StandsOn(c)) continue;
                int hgt = 0;
                // Up to the ceiling: the first full block (or the rock) overhead, not a lamp, a cable or a shelf's top.
                bool Ceiling(Int3 q) => g.Get(q) is var cb && (cb.IsAir ? nav.IsRock(q) : BlockInfo.IsFullCube(cb.Type) || BlockInfo.IsSlope(cb.Type) || BlockInfo.IsCornerSlope(cb.Type));
                while (hgt < 40 && !Ceiling(c + Int3.Up * hgt) && g.InBounds(c + Int3.Up * hgt)) hgt++;
                if (hgt >= 40 || !g.InBounds(c + Int3.Up * hgt)) continue; // open to the sky
                heights[hgt] = heights.GetValueOrDefault(hgt) + 1;
            }
            if (heights.Count == 0) continue;
            int common = heights.MaxBy(kv => kv.Value).Key;
            RoomHeights[common] = RoomHeights.GetValueOrDefault(common) + 1;
            if (m.Kind == ModuleKind.Room && common < 3)
            {
                Fault("room: ceiling under 3 m", m.Min);
                Low[m.Purpose] = Low.GetValueOrDefault(m.Purpose) + 1;
            }
        }
    }

    /// <summary>Another audit's counts added to this one's.</summary>
    public void Add(PlaceAudit o)
    {
        foreach (var kv in o.Faults) Faults[kv.Key] = Faults.TryGetValue(kv.Key, out var f) ? (f.Count + kv.Value.Count, f.First) : kv.Value;
        foreach (var kv in o.DoorSizes) DoorSizes[kv.Key] = DoorSizes.GetValueOrDefault(kv.Key) + kv.Value;
        foreach (var kv in o.RoomHeights) RoomHeights[kv.Key] = RoomHeights.GetValueOrDefault(kv.Key) + kv.Value;
        foreach (var kv in o.Shut) Shut[kv.Key] = Shut.GetValueOrDefault(kv.Key) + kv.Value;
        foreach (var kv in o.Low) Low[kv.Key] = Low.GetValueOrDefault(kv.Key) + kv.Value;
        Doors += o.Doors;
        Pieces += o.Pieces;
        Rooms += o.Rooms;
    }
}
