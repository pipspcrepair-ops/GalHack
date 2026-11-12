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
        bool Wall(Int3 p) => PieceFacing.Wall(g, layout.Occupied, p, layout.Extras.InSpace);
        a.ReadDoors(layout, g, nav, Wall);
        a.ReadPieces(g, nav, Wall);
        a.ReadRooms(layout, g, nav);
        return a;
    }

    /// <summary>A cell's clear height over the floor it stands on, to the first full block, slope or rock overhead (not a lamp, a cable or a shelf); null if open to the sky.</summary>
    public static int? ClearHeight(BlockGrid g, NavGrid nav, Int3 c)
    {
        for (int h = 0; h < 40; h++)
        {
            Int3 q = c + Int3.Up * h;
            if (!g.InBounds(q)) return null;
            Block b = g.Get(q);
            if (b.IsAir ? nav.IsRock(q) : BlockInfo.IsFullCube(b.Type) || BlockInfo.IsSlope(b.Type) || BlockInfo.IsCornerSlope(b.Type)) return h;
        }
        return null;
    }

    /// <summary>How wide a floor is at a cell: the shorter of its runs across x and z within the floor.</summary>
    public static int Span(HashSet<Int3> floor, Int3 c)
    {
        int Run(Int3 d) { int n = 1; for (Int3 q = c + d; floor.Contains(q); q += d) n++; for (Int3 q = c - d; floor.Contains(q); q -= d) n++; return n; }
        return Math.Min(Run(new Int3(1, 0, 0)), Run(new Int3(0, 0, 1)));
    }

    void ReadDoors(Layout layout, BlockGrid g, NavGrid nav, Func<Int3, bool> wall)
    {
        foreach (Settle.DoorShape d in Settle.DoorsOf(layout, g))
        {
            Doors++;
            string size = $"{d.W}x{d.H}x{d.D}";
            DoorSizes[size] = DoorSizes.GetValueOrDefault(size) + 1;
            // The leaf runs along the wall: the doorway's long side is across its facing, the cells at the leaf's ends are
            // wall, and those in front of and behind it are not.
            bool alongX = d.Facing is Dir.North or Dir.South;
            if (d.D > d.W || d.D == d.W && !(wall(d.EndA) && wall(d.EndB)) && wall(d.Lo - d.Facing.Vec()) && wall(d.Lo + d.Facing.Vec())) Fault("door: turned 90 degrees", d.Lo);
            if (d.Cells.Any(c => g.Get(c).Facing.IsHorizontal() && (g.Get(c).Facing is Dir.North or Dir.South) != alongX)) Fault("door: leaf block turned", d.Lo);
            if (d.W == 1 && d.H == 1) Fault("door: 1x1", d.Lo);
            else if (d.D > 1) Fault("door: deeper than 1 m", d.Lo);
            else if (!(d.H == 2 && d.W is 1 or 2) && !(d.W >= 3 && d.H >= 3)) Fault("door: off the 1x2 and 2x2 standard", d.Lo);
            if (d.Cells.Any(c => c.Y == d.Lo.Y && !Settle.Holds(g, nav, c + Int3.Down))) Fault("door: no floor under it", d.Lo);
        }
    }

    void ReadPieces(BlockGrid g, NavGrid nav, Func<Int3, bool> Wall)
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
                    int Backs(Dir d) => run.Count(q => Wall(q - d.Vec()) && !Wall(q + d.Vec()));
                    int own = run.Count(q => Wall(q - f.Vec()));
                    if (Dirs.Horizontal.Any(d => d != f && Backs(d) > own)) Fault("ladder: turned off its wall", p);
                    break;
                }
                case BlockType.Sign:
                {
                    if (!f.IsHorizontal()) break;
                    Pieces++;
                    // A plate across its facing: on a wall behind it or in front of it, never with its edge to the wall.
                    bool flat = Wall(p - f.Vec()) || Wall(p + f.Vec());
                    bool edge = Wall(p + f.RotateCw(1).Vec()) || Wall(p + f.RotateCw(3).Vec());
                    if (!flat && edge) Fault("sign: turned edge-on to its wall", p);
                    break;
                }
                case BlockType.Bed:
                {
                    if (!f.IsHorizontal()) break;
                    Pieces++;
                    // Head toward Facing: a bed with its foot on the wall and its head in the room is backwards.
                    if (Wall(p - f.Vec()) && !Wall(p + f.Vec()) && g.Get(p + f.Vec()).Type != BlockType.Bed) Fault("bed: backwards", p);
                    break;
                }
                default:
                    if (Backed(b.Type) && f.IsHorizontal())
                    {
                        Pieces++;
                        bool front = Wall(p + f.Vec()), back = Wall(p - f.Vec());
                        // Wrong only where its spot offers a right way: a wall to back onto with the room in front.
                        bool could = Dirs.Horizontal.Any(d => Wall(p - d.Vec()) && !Wall(p + d.Vec()));
                        if (front && !back && Dirs.Horizontal.Any(d => !Wall(p + d.Vec()))) Fault($"{b.Type.ToString().ToLowerInvariant()}: backwards (faces its wall)", p);
                        else if (!(back && !front) && could) Fault($"{b.Type.ToString().ToLowerInvariant()}: turned (side to its wall)", p);
                    }
                    break;
            }
            // A floor's finish is paint on the top of the block under it (never a plate of its own): there must be one.
            if (BlockInfo.IsFinish(b.Type) && !Settle.Painted(g, nav, p + Int3.Down)) Fault("finish: painted on nothing", p);
            // Nothing set into the rock: below ground, things stand only in the place's own dug cells.
            if (nav.IsRock(p) && b.Role is Role.Debris or Role.Prop or Role.Loot or Role.Story or Role.Growth) Fault($"in the rock: {b.Type}", p);
            // Loose things rest on what is under them.
            else if (Rests(b.Type) && !Settle.Holds(g, nav, p + Int3.Down))
                Fault(b.Type is BlockType.Cache or BlockType.Terminal or BlockType.Core ? $"kept, on nothing: {b.Type}" : $"rests on nothing: {b.Type}", p);
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
            if (!secret.Contains(m.Index) && Fixtures.RoomCell(m, reach) == null) Fault("room: no way in", m.Min);
            if (RoomClear(m, g, nav) is not int common) continue;
            RoomHeights[common] = RoomHeights.GetValueOrDefault(common) + 1;
            // (A wrecked aircraft's cabin is a vehicle's, not a building's room.)
            if (m.Kind == ModuleKind.Room && common < 3 && m.Purpose != "aircraft cabin") Fault("room: ceiling under 3 m", m.Min);
        }
    }

    /// <summary>A room's clear height: the commonest over its own floor (what the years heaped on it raises nothing).</summary>
    public static int? RoomClear(PlacedModule m, BlockGrid g, NavGrid nav)
    {
        var heights = m.Cells.Where(c => c.Y == m.Min.Y && nav.StandsOn(c)).Select(c => ClearHeight(g, nav, c)).OfType<int>().ToList();
        return heights.Count == 0 ? null : heights.GroupBy(h => h).MaxBy(x => x.Count())!.Key;
    }

    /// <summary>Another audit's counts added to this one's.</summary>
    public void Add(PlaceAudit o)
    {
        foreach (var kv in o.Faults) Faults[kv.Key] = Faults.TryGetValue(kv.Key, out var f) ? (f.Count + kv.Value.Count, f.First) : kv.Value;
        foreach (var kv in o.DoorSizes) DoorSizes[kv.Key] = DoorSizes.GetValueOrDefault(kv.Key) + kv.Value;
        foreach (var kv in o.RoomHeights) RoomHeights[kv.Key] = RoomHeights.GetValueOrDefault(kv.Key) + kv.Value;
        Doors += o.Doors;
        Pieces += o.Pieces;
        Rooms += o.Rooms;
    }
}
