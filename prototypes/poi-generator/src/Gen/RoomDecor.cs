#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Deco;
using GalHack.Items;
using NV2 = System.Numerics.Vector2;

namespace PoiGenerator.Gen;

/// <summary>
/// What the decorations library is asked to furnish in a place: each room's open interior, its doors, the
/// patches of its floor nothing may stand on, the place's state, age, exposure and tech. One source for the
/// game (the slice seats the plan as pieces), the viewer (shows the same rooms) and the tests (audit them),
/// so all three dress a room the same way from the same seed.
/// </summary>
public static class RoomDecor
{
    /// <summary>A room the library dresses: its <see cref="DecoRoom"/>, its floor corner (walk level), the floor's height in that cell and its own floor cells.</summary>
    public sealed record Dressable(PlacedModule Module, DecoRoom Room, Int3 Corner, float Floor, HashSet<Int3> Own);

    /// <summary>The top of a floor plate: a room laid with them stands its pieces on them.</summary>
    public const float PlateTop = Shapes.PlateTop;

    /// <summary>Every room of a place the library dresses, in module order (a hidden room, an odd shape or a cupboard keeps the generator's own props).</summary>
    public static List<Dressable> Rooms(PoiInstance inst)
    {
        var reserved = Reserved(inst, Openings(inst).ToList());
        NavGrid nav = inst.Nav();
        var rooms = new List<Dressable>();
        foreach (PlacedModule m in inst.Layout.Modules)
            if (Room(inst, m, nav, reserved) is { } r) rooms.Add(r);
        return rooms;
    }

    /// <summary>The room the library dresses for a module, or null where the generator's props stay.</summary>
    public static Dressable? Room(PoiInstance inst, PlacedModule m, NavGrid nav, HashSet<Int3> reserved)
    {
        Layout layout = inst.Layout;
        if (m.Kind is not (ModuleKind.Room or ModuleKind.Hall or ModuleKind.Vault)) return null;
        if (inst.Extras.Secrets.Any(s => s.Module == m.Index)) return null; // a hidden room keeps what the generator left
        if (Interior(inst, m) is not { } room0) return null;
        var (y, x0, x1, z0, z1, height, open, floor) = room0;
        int w = x1 - x0 + 1, d = z1 - z0 + 1;
        // Not a rectangle, or no more than a cupboard: the generator's props stay.
        if (open < MinCells || open < w * d * 0.85f || height < 2) return null;
        bool alien = (inst.Family.Style ?? "").StartsWith("alien_", StringComparison.Ordinal);
        var room = new DecoRoom
        {
            Width = w, Depth = d, Height = height, RoomId = m.Purpose, Tags = inst.Family.Tags.ToList(), State = inst.State.ToString().ToLowerInvariant(),
            Age = inst.AgeOf(m.Index) switch { Age.New => DecoAge.Fresh, Age.Preserved => DecoAge.Preserved, _ => DecoAge.OldWorld },
            Exposed = inst.ExposureOf(m.Index) != Exposure.Indoors,
            Tech = TechOf(inst),
            Set = alien ? "alien" : null,
            Seed = Hash.Combine(inst.SpawnSeed, (ulong)m.Index + 1),
            // Its pieces' trim in the world's own paint, as worn as the place.
            KitTrim = inst.Paint is { } paint && !alien ? DecoRgb.Hex(paint.Weather(paint.Accent).ToString()) : null,
        };
        foreach (var cells in layout.Doorways.Where(dw => dw.ModuleA == m.Index || dw.ModuleB == m.Index).Select(dw => dw.Cells)
                     .Concat(m.Index == layout.EntranceModule ? new[] { layout.ExitCells } : Array.Empty<List<Int3>>()))
        {
            var at = cells.Where(c => c.Y == y).ToList();
            if (at.Count == 0) continue;
            Int3 c = at.Aggregate(Int3.Min);
            float width = at.Count;
            if (c.Z < z0) room.Doors.Add(new DecoDoor(DecoWall.North, c.X - x0 + width / 2, width));
            else if (c.Z > z1) room.Doors.Add(new DecoDoor(DecoWall.South, c.X - x0 + width / 2, width));
            else if (c.X < x0) room.Doors.Add(new DecoDoor(DecoWall.West, c.Z - z0 + width / 2, width));
            else if (c.X > x1) room.Doors.Add(new DecoDoor(DecoWall.East, c.Z - z0 + width / 2, width));
        }
        // What the room's floor can't take, the library keeps clear from the start (the game's checks after are the last word):
        // another room's floor inside its box among it.
        var own = new HashSet<Int3>(m.Cells.Where(c => c.Y == y));
        for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                var c = new Int3(x, y, z);
                bool free = own.Contains(c) && FloorAt(inst, nav, c) is float f && MathF.Abs(f - floor) < 0.01f && !reserved.Contains(c) && !reserved.Contains(c + Int3.Up)
                    && (Clearable(inst.Blocks.Get(c + Int3.Up)) || inst.Blocks.Get(c + Int3.Up).Type is BlockType.Sign or BlockType.Cable or BlockType.Vine);
                if (!free) room.Blocked.Add(new DecoRect(new NV2(x - x0 + 0.5f, z - z0 + 0.5f), new NV2(0.5f, 0), new NV2(0, 0.5f)));
            }
        return new Dressable(m, room, new Int3(x0, y, z0), floor, own);
    }

    /// <summary>The generator's generic props in a dressed room go (and a barricade it stacked): the library's pieces stand there instead, so nothing is drawn one inside another.</summary>
    public static void ClearProps(PoiInstance inst, Dressable room)
    {
        int y = room.Corner.Y, x0 = room.Corner.X, z0 = room.Corner.Z, x1 = x0 + (int)room.Room.Width - 1, z1 = z0 + (int)room.Room.Depth - 1;
        foreach (Int3 c in room.Module.Cells)
        {
            if (c.Y != y && c.Y != y + 1) continue;
            if (c.X < x0 || c.X > x1 || c.Z < z0 || c.Z > z1) continue;
            Block b = inst.Blocks.Get(c);
            if (b.Role == Role.Prop || b.Role == Role.Story && b.Type == BlockType.Barricade) inst.Blocks.Set(c, Block.Air);
        }
    }

    /// <summary>The fewest open floor cells a room needs to be furnished by the library: a washroom of 2 by 2 m is a room, a cupboard is not.</summary>
    public const int MinCells = 4;

    /// <summary>
    /// A module's open interior: the level you walk on (the lowest with floor: the architect's modules
    /// start at it, a kit's a layer of floor below), the floor's height in it (bare, or laid with plates:
    /// as most of its cells are), its box and clear height, and its cells with that floor.
    /// </summary>
    public static (int Y, int X0, int X1, int Z0, int Z1, int Height, int Cells, float Floor)? Interior(PoiInstance inst, PlacedModule m)
    {
        NavGrid nav = inst.Nav();
        foreach (int y in new[] { m.Min.Y, m.Min.Y + 1 })
        {
            var floors = m.Cells.Where(c => c.Y == y).Select(c => (Cell: c, Floor: FloorAt(inst, nav, c))).Where(t => t.Floor != null).ToList();
            if (floors.Count == 0) continue;
            float floor = floors.GroupBy(t => t.Floor!.Value).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
            var inside = floors.Where(t => t.Floor == floor).Select(t => t.Cell).ToList();
            int x0 = inside.Min(c => c.X), x1 = inside.Max(c => c.X), z0 = inside.Min(c => c.Z), z1 = inside.Max(c => c.Z);
            int height = 0;
            for (int h = 0; h < 12; h++, height++)
            {
                Block b = inst.Blocks.Get(new Int3((x0 + x1) / 2, y + h, (z0 + z1) / 2));
                if (!Open(b) && !(h == 0 && b.Type == BlockType.Plate)) break;
            }
            return (y, x0, x1, z0, z1, height, inside.Count, floor);
        }
        return null;
    }

    /// <summary>A cell a room's interior may have: air, or something the generator set down in it.</summary>
    static bool Open(Block b) => b.IsAir || b.Role is Role.Prop or Role.Loot or Role.Terminal or Role.Story or Role.Debris or Role.Growth or Role.Core or Role.Traverse or Role.Hazard;

    /// <summary>
    /// The floor in a walk-level cell, as a height above the cell's bottom, or null where there's nothing a
    /// piece could stand on: it's bare over a full block or the ground (0), or laid with a floor plate on
    /// one (<see cref="PlateTop"/>). A stair, a hole, a ledge or a prop under it is no floor.
    /// </summary>
    public static float? FloorAt(PoiInstance inst, NavGrid nav, Int3 c)
    {
        Block b = inst.Blocks.Get(c), under = inst.Blocks.Get(c + Int3.Down);
        bool firm = under.IsAir ? nav.IsRock(c + Int3.Down) : BlockInfo.IsFullCube(under.Type) || under.Type == BlockType.Lattice;
        if (!firm) return null;
        if (b.Type == BlockType.Plate && b.Role == Role.Floor) return PlateTop;
        return Clearable(b) ? 0f : null;
    }

    /// <summary>What a piece may be drawn in: air, the generator's props (they go from a dressed room) and its loose litter (cleared where a piece lies).</summary>
    public static bool Clearable(Block b) => b.IsAir || b.Role == Role.Prop || b.Role == Role.Story && b.Type == BlockType.Barricade
        || b.Role == Role.Debris && b.Type is BlockType.Ash or BlockType.Scatter or BlockType.Rubble or BlockType.Plant or BlockType.Puddle or BlockType.Crate or BlockType.Scorch;

    /// <summary>The place's tech level for the library (the settlement's or layout's own until the world's profile gives one), or any.</summary>
    public static DecoTech? TechOf(PoiInstance inst) => (inst.Layout.Variant?.Tech ?? "") switch
    {
        "low" => DecoTech.Low,
        "mid" => DecoTech.Mid,
        "high" => DecoTech.High,
        // Otherwise the world's own era for it (the old world's for a ruin, the living's for a lived-in place).
        _ => inst.Tech switch
        {
            TechLevel.Low => DecoTech.Low,
            TechLevel.Mid => DecoTech.Mid,
            TechLevel.High => DecoTech.High,
            _ => inst.Family.Tags.Contains("settlement") ? DecoTech.Low : null,
        },
    };

    /// <summary>Cells the dressing must leave clear: fixtures, ways up and down, doorways and a step inside them.</summary>
    public static HashSet<Int3> Reserved(PoiInstance inst, List<(Int3 A, Int3 B)> openings)
    {
        var r = new HashSet<Int3>();
        foreach (LootCache c in inst.Caches) r.Add(c.Cell);
        foreach (TerminalLog t in inst.Terminals) r.Add(t.Cell);
        if (inst.HasCore) r.Add(inst.CoreCell);
        foreach (var (p, b) in inst.Blocks.Blocks())
        {
            if (b.Type is BlockType.Ladder or BlockType.Stair or BlockType.Ramp or BlockType.Door or BlockType.Breakable or BlockType.Remains)
                r.Add(p);
            // A ladder's ends: the floor beside its foot you step on from, and the landing a level over its top you step off onto, with headroom.
            if (b.Type != BlockType.Ladder) continue;
            bool foot = inst.Blocks.Get(p + Int3.Down).Type != BlockType.Ladder, top = inst.Blocks.Get(p + Int3.Up).Type != BlockType.Ladder;
            foreach (Dir h in Dirs.Horizontal)
            {
                if (foot) { r.Add(p + h.Vec()); r.Add(p + h.Vec() + Int3.Up); }
                if (top) { r.Add(p + Int3.Up + h.Vec()); r.Add(p + Int3.Up * 2 + h.Vec()); }
            }
        }
        foreach (Doorway d in inst.Layout.Doorways)
            foreach (Int3 c in d.Cells)
                foreach (Dir h in Dirs.Horizontal) r.Add(c + h.Vec());
        // Where two modules' floors meet with no doorway (a mine's drifts run into each other, a hall opens
        // on a passage), the way through stays clear as a doorway's does: nothing is stood across it.
        foreach (var (a, b) in openings)
        {
            r.Add(a);
            r.Add(b);
        }
        foreach (Int3 c in inst.Layout.ExitCells)
            foreach (Dir h in Dirs.Horizontal) r.Add(c + h.Vec());
        foreach (LiftShaft s in inst.Extras.Lifts)
            foreach (Int3 f in s.Footprint)
                foreach (int y in s.StopWalkY)
                    foreach (Dir h in Dirs.Horizontal) { r.Add(f with { Y = y }); r.Add((f + h.Vec()) with { Y = y }); }
        foreach (SecretRoom s in inst.Extras.Secrets)
        {
            Int3 front = s.WallCells.Count > 0 ? FoundFrom(inst, s) : default;
            foreach (Int3 c in s.WallCells)
            {
                r.Add(c);
                r.Add(c + front); // the panel stays in view: nothing stands in front of it
                r.Add(c + front * 2);
            }
            foreach (Int3 c in s.TellCells) r.Add(c);
        }
        return r;
    }

    /// <summary>The way out of a hidden room's breakable wall toward the room it is found from (a unit step).</summary>
    public static Int3 FoundFrom(PoiInstance inst, SecretRoom s)
    {
        Int3 f = s.Facing.Vec(), wall = s.WallCells[0];
        PlacedModule hidden = inst.Layout.Modules[s.Module];
        Int3 a = wall + f;
        bool intoHidden = a.X >= hidden.Min.X && a.X <= hidden.Max.X && a.Z >= hidden.Min.Z && a.Z <= hidden.Max.Z;
        return intoHidden ? f * -1 : f;
    }

    /// <summary>
    /// The places two modules open into each other with no doorway: a walkable cell of one beside a walkable
    /// cell of the other (a mine's drifts and stopes, a hangar floor and its passage). Each pair once.
    /// </summary>
    public static IEnumerable<(Int3 A, Int3 B)> Openings(PoiInstance inst)
    {
        var nav = inst.Nav();
        var of = new Dictionary<Int3, int>();
        foreach (PlacedModule m in inst.Layout.Modules)
            foreach (Int3 c in m.Cells) of.TryAdd(c, m.Index);
        // Walkable as the place will stand: the generator's props and litter go from a dressed room.
        bool Walk(Int3 c) => nav.Standable(c) || FloorAt(inst, nav, c) != null && Clearable(inst.Blocks.Get(c + Int3.Up));
        foreach (var (c, mi) in of)
            foreach (Dir h in new[] { Dir.East, Dir.South })
            {
                Int3 n = c + h.Vec();
                if (of.TryGetValue(n, out int ni) && ni != mi && Walk(c) && Walk(n)) yield return (c, n);
            }
    }

    /// <summary>The floor cells a footprint covers (room metres from a corner cell), shrunk by a margin.</summary>
    public static IEnumerable<Int3> CellsOf(DecoRect rect, Int3 corner, float margin)
    {
        DecoRect r = rect.Shrink(margin);
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (NV2 c in r.Corners())
        {
            minX = MathF.Min(minX, c.X); maxX = MathF.Max(maxX, c.X);
            minZ = MathF.Min(minZ, c.Y); maxZ = MathF.Max(maxZ, c.Y);
        }
        for (int z = (int)MathF.Floor(minZ); z <= (int)MathF.Floor(maxZ); z++)
            for (int x = (int)MathF.Floor(minX); x <= (int)MathF.Floor(maxX); x++)
                yield return new Int3(corner.X + x, corner.Y, corner.Z + z);
    }
}
