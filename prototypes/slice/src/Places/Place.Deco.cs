using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Deco;
using GalHack.Deco.Engine;
using GalHack.Items;
using Godot;
using PoiGenerator.Gen;
using Slice.Game;
using Layers = GalHack.Physics.Layers;
using NV2 = System.Numerics.Vector2;

namespace Slice.Places;

/// <summary>The decorations library, loaded once for the game: catalogue, forge, arrangements and the mesh cache.</summary>
public sealed class Decor
{
    public readonly DecoCatalogue Catalogue;
    public readonly DecoForge Forge;
    public readonly DecoPoi Poi;
    public readonly DecoMeshCache Meshes;

    Decor(string dir)
    {
        Catalogue = DecoCatalogue.LoadDirectory(dir);
        Forge = new DecoForge(Catalogue);
        Poi = new DecoPoi(Forge, DecoArrangements.LoadDirectory(dir, Catalogue));
        Meshes = new DecoMeshCache(Forge);
    }

    static Decor _shared;
    static bool _tried;

    /// <summary>The library (null if it can't be read: places then keep the generator's own props).</summary>
    public static Decor Shared
    {
        get
        {
            if (_tried) return _shared;
            _tried = true;
            try { _shared = new Decor(GameData.SharedPath("Items", "data")); }
            catch (Exception e) { GD.PushWarning($"decorations library unavailable: {e.Message}"); }
            return _shared;
        }
    }

    /// <summary>
    /// Lets the library go with its caches (a NEW GAME: the last world's looks, in its own paint, are no use to the
    /// next); the next use loads it afresh. Anything of the last world still working keeps the old one to itself.
    /// </summary>
    public static void Forget()
    {
        _shared = null;
        _tried = false;
    }

    /// <summary>
    /// Works out how each room of a place is dressed (worker-safe): the room's interior, its doors,
    /// state, age (new, old-world or preserved), exposure and tech go to the library, which arranges
    /// its pieces as the room was used and then left. Pieces that would stand on a cache, a
    /// terminal, a keycard, a ladder, a stair or in a doorway are left out, and the generator's
    /// generic props are cleared from a dressed room so nothing overlaps.
    /// </summary>
    public Dictionary<int, PlaceRoom> Plan(PlaceMemory memory)
    {
        PoiInstance inst = memory.Inst;
        Layout layout = inst.Layout;
        var rooms = new Dictionary<int, PlaceRoom>();
        var openings = Openings(inst).ToList();
        var reserved = Reserved(inst, openings);
        NavGrid nav = inst.Nav();
        string state = inst.State.ToString().ToLowerInvariant();
        bool alien = (inst.Family.Style ?? "").StartsWith("alien_", StringComparison.Ordinal);
        DecoTech? tech = TechOf(inst);
        foreach (PlacedModule m in layout.Modules)
        {
            if (m.Kind is not (ModuleKind.Room or ModuleKind.Hall or ModuleKind.Vault)) continue;
            if (inst.Extras.Secrets.Any(s => s.Module == m.Index)) continue; // a hidden room keeps what the generator left
            if (Interior(inst, m) is not { } room0) continue;
            var (y, x0, x1, z0, z1, height, open, floor) = room0;
            int w = x1 - x0 + 1, d = z1 - z0 + 1;
            if (open < 9 || open < w * d * 0.85f || height < 2) continue; // not a rectangle: the generator's props stay
            var room = new DecoRoom
            {
                Width = w, Depth = d, Height = height, RoomId = m.Purpose, Tags = inst.Family.Tags.ToList(), State = state,
                Age = inst.AgeOf(m.Index) switch { Age.New => DecoAge.Fresh, Age.Preserved => DecoAge.Preserved, _ => DecoAge.OldWorld },
                Exposed = inst.ExposureOf(m.Index) != Exposure.Indoors,
                Tech = tech,
                Set = alien ? "alien" : null,
                Seed = Hash.Combine(inst.SpawnSeed, (ulong)m.Index + 1),
                // Its pieces' trim in the world's own paint, as worn as the place.
                KitTrim = inst.Paint is { } paint && !alien ? GalHack.Deco.DecoRgb.Hex(paint.Weather(paint.Accent).ToString()) : null,
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
            // What the room's floor can't take, the library keeps clear from the start (the checks below are the last word):
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
            DecoPlan plan;
            try { plan = Poi.Dress(room, inst.Kit?.Id); }
            catch (Exception) { continue; }
            var placeRoom = new PlaceRoom { Module = m.Index, Corner = new Int3(x0, y, z0), Floor = floor, Room = room, Plan = plan };
            // Pieces on a reserved cell (or standing on one that is), or not standing as they're drawn, are left out.
            var dropped = new HashSet<int>();
            var touched = new Dictionary<int, HashSet<Int3>>();
            for (int i = 0; i < plan.Placements.Count; i++)
            {
                DecoPlacement pl = plan.Placements[i];
                if (pl.Support >= 0 && dropped.Contains(pl.Support)) { dropped.Add(i); continue; }
                foreach (Int3 c in CellsOf(DecoLayout.Footprint(Forge, pl), placeRoom.Corner, 0.05f))
                    if (reserved.Contains(c) || reserved.Contains(c + Int3.Up)) { dropped.Add(i); break; }
                if (dropped.Contains(i)) continue;
                var cells = new HashSet<Int3>();
                if (!Rests(inst, nav, pl, placeRoom, own, cells)) dropped.Add(i);
                else touched[i] = cells;
            }
            var kept = memory.DecoTaken.GetValueOrDefault(m.Index) ?? new HashSet<int>();
            foreach (int i in dropped) kept.Add(i);
            if (kept.Count > 0) memory.DecoTaken[m.Index] = kept;
            for (int i = 0; i < plan.Placements.Count; i++)
            {
                if (kept.Contains(i) || !DecoLayout.Blocks(Forge, plan.Placements[i])) continue;
                var blocking = Blocking(plan.Placements[i], placeRoom);
                // A solid piece whose collision a person would meet in a doorway, an opening or by a cache goes, with what's on it.
                if (blocking.Any(reserved.Contains))
                {
                    foreach (int j in Carried(plan, i)) kept.Add(j);
                    continue;
                }
                placeRoom.Solid.Add(i);
                placeRoom.Blocking[i] = blocking;
            }
            placeRoom.Solid.RemoveWhere(kept.Contains);
            foreach (int i in placeRoom.Solid) placeRoom.Cells.UnionWith(placeRoom.Blocking[i]);
            if (kept.Count > 0) memory.DecoTaken[m.Index] = kept;
            // The generator's generic props in this room go, and its loose litter wherever a piece lies:
            // the library's pieces stand there instead, so nothing is drawn one inside another.
            foreach (Int3 c in m.Cells)
            {
                if (c.Y != y && c.Y != y + 1) continue;
                if (c.X < x0 || c.X > x1 || c.Z < z0 || c.Z > z1) continue;
                Block b = inst.Blocks.Get(c);
                if (b.Role == Role.Prop || b.Role == Role.Story && b.Type == BlockType.Barricade) inst.Blocks.Set(c, Block.Air);
            }
            foreach (var (i, cells) in touched)
                if (!kept.Contains(i))
                    foreach (Int3 c in cells)
                        if (!inst.Blocks.Get(c).IsAir && Clearable(inst.Blocks.Get(c))) inst.Blocks.Set(c, Block.Air);
            rooms[m.Index] = placeRoom;
        }
        KeepWaysOpen(memory, rooms, openings);
        return rooms;
    }

    /// <summary>
    /// Whether a piece stands as it is drawn: whatever it lays on the floor (its feet, a broken one's
    /// fragments, a burnt one's ash) lies on floor at the room's own height, never over a hole, a step,
    /// a stair or a ledge; none of it is drawn inside a block; and one up a wall or under a ceiling has
    /// that wall or ceiling still there. It stands on its own room's floor (<paramref name="own"/>), not on
    /// another's that its box takes in. <paramref name="cells"/> gets the cells it is drawn in.
    /// </summary>
    bool Rests(PoiInstance inst, NavGrid nav, DecoPlacement pl, PlaceRoom room, HashSet<Int3> own, HashSet<Int3> cells)
    {
        DecoPiece piece = Forge.Catalogue.Piece(pl.Piece);
        // The mesh is worked out here, off the main thread (the forge keeps it).
        DecoMeshData mesh = Forge.Mesh(pl.Look);
        System.Numerics.Matrix4x4 at = pl.Transform;
        bool onFloor = pl.Support < 0 && piece.Mount is DecoMount.Floor or DecoMount.Top;
        var centre = new NV2(pl.X, pl.Z);
        var floors = new HashSet<Int3>();
        // Most pieces stand clear of everything: if every cell of the box round all of it is clear (and floor at
        // the room's height under it), that is enough, and the vertices needn't be walked one by one.
        if (pl.Support < 0 && onFloor && ClearBox(inst, nav, mesh, at, room, own, cells)) return true;
        cells.Clear();
        foreach (DecoSurfaceData s in mesh.Surfaces)
            foreach (System.Numerics.Vector3 v0 in s.Positions)
            {
                System.Numerics.Vector3 v = System.Numerics.Vector3.Transform(v0, at);
                // A hair in toward the piece's middle: a face flush with a wall isn't in it.
                var flat = new NV2(v.X, v.Z);
                NV2 inward = centre - flat;
                flat += new NV2(MathF.Sign(inward.X) * MathF.Min(0.01f, MathF.Abs(inward.X)), MathF.Sign(inward.Y) * MathF.Min(0.01f, MathF.Abs(inward.Y)));
                float up = MathF.Max(v.Y, 0f);
                var c = new Int3(room.Corner.X + (int)MathF.Floor(flat.X), room.Corner.Y + (int)MathF.Floor(room.Floor + up + 0.005f), room.Corner.Z + (int)MathF.Floor(flat.Y));
                cells.Add(c);
                if (onFloor && up < 0.03f) floors.Add(c with { Y = room.Corner.Y });
            }
        foreach (Int3 c in cells)
        {
            Block b = inst.Blocks.Get(c);
            if (Clearable(b) || b.Type is BlockType.Sign or BlockType.Cable or BlockType.Vine) continue;
            if (c.Y == room.Corner.Y && b.Type == BlockType.Plate && b.Role == Role.Floor) continue;
            return false;
        }
        foreach (Int3 c in floors)
            if (!own.Contains(c) || FloorAt(inst, nav, c) is not float f || MathF.Abs(f - room.Floor) > 0.01f) return false;
        if (pl.Support >= 0 || onFloor) return true;
        if (piece.Mount == DecoMount.Ceiling)
        {
            // Under a ceiling: the ceiling over all of it, a slab of full blocks (a pipe, a rail or a lamp overhead is
            // no ceiling: a courtyard's ceiling piece hung in the air under one).
            foreach (Int3 c in CellsOf(DecoLayout.Footprint(Forge, pl), room.Corner, 0.05f))
            {
                Int3 over = c with { Y = room.Corner.Y + (int)MathF.Floor(room.Floor + pl.Y + piece.Height + 0.05f) };
                if (!(inst.Blocks.Get(over) is var b && (b.IsAir ? nav.IsRock(over) : BlockInfo.IsFullCube(b.Type)))) return false;
            }
            return true;
        }
        // Up a wall: the wall behind it, all along its back and from its foot to its top (an opening into
        // the next room is no wall to hang a rack on).
        NV2 back = DecoGeo.Yaw(new NV2(0, 1), pl.Yaw), side = new(-back.Y, back.X);
        foreach (float a in new[] { -0.4f, 0f, 0.4f })
            foreach (float h in new[] { pl.Y + 0.1f, pl.Y + piece.Height - 0.1f })
            {
                NV2 behind = new NV2(pl.X, pl.Z) + back * (piece.Depth / 2f + 0.1f) + side * (a * piece.Width);
                var c = new Int3(room.Corner.X + (int)MathF.Floor(behind.X), room.Corner.Y + (int)MathF.Floor(room.Floor + h), room.Corner.Z + (int)MathF.Floor(behind.Y));
                if (!nav.Solid(c)) return false;
            }
        return true;
    }

    /// <summary>Whether every cell of the box round a floor piece's mesh is clear and floored at the room's height (the cells go in <paramref name="cells"/>).</summary>
    static bool ClearBox(PoiInstance inst, NavGrid nav, DecoMeshData mesh, System.Numerics.Matrix4x4 at, PlaceRoom room, HashSet<Int3> own, HashSet<Int3> cells)
    {
        // The box of the drawn vertices themselves (a fallen piece's stored bounds can be its standing ones).
        float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue, top = 0f;
        foreach (DecoSurfaceData s in mesh.Surfaces)
            foreach (System.Numerics.Vector3 p in s.Positions)
            {
                var v = System.Numerics.Vector3.Transform(p, at);
                x0 = MathF.Min(x0, v.X); x1 = MathF.Max(x1, v.X); z0 = MathF.Min(z0, v.Z); z1 = MathF.Max(z1, v.Z); top = MathF.Max(top, v.Y);
            }
        if (x0 > x1) return false;
        int cx0 = room.Corner.X + (int)MathF.Floor(x0 + 0.01f), cx1 = room.Corner.X + (int)MathF.Floor(x1 - 0.01f);
        int cz0 = room.Corner.Z + (int)MathF.Floor(z0 + 0.01f), cz1 = room.Corner.Z + (int)MathF.Floor(z1 - 0.01f);
        int cy1 = room.Corner.Y + (int)MathF.Floor(room.Floor + top + 0.005f);
        for (int z = cz0; z <= cz1; z++)
            for (int x = cx0; x <= cx1; x++)
            {
                var floor = new Int3(x, room.Corner.Y, z);
                if (!own.Contains(floor) || FloorAt(inst, nav, floor) is not float f || MathF.Abs(f - room.Floor) > 0.01f) return false;
                for (int y = room.Corner.Y; y <= cy1; y++)
                {
                    var c = new Int3(x, y, z);
                    Block b = inst.Blocks.Get(c);
                    if (!Clearable(b) && !(y == room.Corner.Y && b.Type == BlockType.Plate && b.Role == Role.Floor) && b.Type is not (BlockType.Sign or BlockType.Cable or BlockType.Vine)) return false;
                    cells.Add(c);
                }
            }
        return true;
    }

    /// <summary>The top of a floor plate: a room laid with them stands its pieces on them.</summary>
    public const float PlateTop = Shapes.PlateTop;

    /// <summary>
    /// The floor in a walk-level cell, as a height above the cell's bottom, or null where there's nothing a
    /// piece could stand on: it's bare over a full block or the ground (0), or laid with a floor plate on
    /// one (<see cref="PlateTop"/>). A stair, a hole, a ledge or a prop under it is no floor.
    /// </summary>
    static float? FloorAt(PoiInstance inst, NavGrid nav, Int3 c)
    {
        Block b = inst.Blocks.Get(c), under = inst.Blocks.Get(c + Int3.Down);
        bool firm = under.IsAir ? nav.IsRock(c + Int3.Down) : BlockInfo.IsFullCube(under.Type) || under.Type == BlockType.Lattice;
        if (!firm) return null;
        if (b.Type == BlockType.Plate && b.Role == Role.Floor) return PlateTop;
        return Clearable(b) ? 0f : null;
    }

    /// <summary>What a piece may be drawn in: air, the generator's props (they go from a dressed room) and its loose litter (cleared where a piece lies).</summary>
    static bool Clearable(Block b) => b.IsAir || b.Role == Role.Prop || b.Role == Role.Story && b.Type == BlockType.Barricade
        || b.Role == Role.Debris && b.Type is BlockType.Ash or BlockType.Scatter or BlockType.Rubble or BlockType.Plant or BlockType.Puddle or BlockType.Crate or BlockType.Scorch;

    /// <summary>
    /// The dressing never closes a way the architect checked: every doorway, stair, cache, keycard
    /// and hidden wall reachable on the bare blocks stays reachable round the solid pieces. A piece
    /// standing on the bare route to one that was cut off goes (and what stood on it).
    /// </summary>
    void KeepWaysOpen(PlaceMemory memory, Dictionary<int, PlaceRoom> rooms, List<(Int3 A, Int3 B)> openings)
    {
        PoiInstance inst = memory.Inst;
        var bare = inst.Nav();
        Int3 entry = inst.Layout.EntranceCell;
        var tree = bare.Tree(entry);
        var open = new HashSet<Int3>(tree.Keys);
        var need = new HashSet<Int3>();
        void Need(Int3 c) { if (open.Contains(c)) need.Add(c); }
        foreach (Doorway d in inst.Layout.Doorways)
            foreach (Int3 c in d.Cells)
                foreach (Dir h in Dirs.Horizontal) Need(c + h.Vec());
        foreach (var (a, b) in openings)
        {
            Need(a);
            Need(b);
        }
        foreach (var (p, b) in inst.Blocks.Blocks())
            if (b.Type is BlockType.Stair or BlockType.Ladder) Need(p);
        foreach (LootCache c in inst.Caches)
            if (bare.AccessCell(c.Cell, open) is Int3 a) Need(a);
        foreach (KeyCard k in inst.Extras.Keys) Need(k.Cell);
        foreach (SecretRoom s in inst.Extras.Secrets)
            foreach (Int3 c in s.WallCells)
            {
                Need(c + s.Facing.Vec());
                Need(c - s.Facing.Vec());
            }
        for (int round = 0; round < 40 && need.Count > 0; round++)
        {
            var reach = Place.NavOf(memory, rooms).Reachable(entry);
            var lost = need.Where(c => !reach.Contains(c)).ToList();
            if (lost.Count == 0) return;
            bool freed = false;
            foreach (Int3 cut in lost)
            {
                List<Int3> way = NavGrid.PathInTree(tree, cut);
                if (way == null) { need.Remove(cut); continue; }
                var on = new HashSet<Int3>(way);
                foreach (PlaceRoom r in rooms.Values)
                    foreach (int i in r.Solid.ToList())
                        if (r.Blocking[i].Overlaps(on))
                        {
                            Drop(memory, r, i);
                            freed = true;
                        }
            }
            if (!freed) return; // nothing of ours stands in the way of what's left
        }
    }

    /// <summary>A piece left out of a room, with whatever stood on it.</summary>
    void Drop(PlaceMemory memory, PlaceRoom r, int index)
    {
        if (!memory.DecoTaken.TryGetValue(r.Module, out HashSet<int> taken)) memory.DecoTaken[r.Module] = taken = new HashSet<int>();
        var gone = new List<int> { index };
        for (int k = 0; k < gone.Count; k++)
            for (int i = 0; i < r.Plan.Placements.Count; i++)
                if (r.Plan.Placements[i].Support == gone[k] && !gone.Contains(i)) gone.Add(i);
        foreach (int i in gone)
        {
            taken.Add(i);
            r.Solid.Remove(i);
        }
        r.Cells.Clear();
        foreach (int i in r.Solid) r.Cells.UnionWith(r.Blocking[i]);
    }

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
            float floor = floors.GroupBy(t => t.Floor.Value).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
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
    static HashSet<Int3> Reserved(PoiInstance inst, List<(Int3 A, Int3 B)> openings)
    {
        var r = new HashSet<Int3>();
        foreach (LootCache c in inst.Caches) r.Add(c.Cell);
        foreach (TerminalLog t in inst.Terminals) r.Add(t.Cell);
        foreach (KeyCard k in inst.Extras.Keys) r.Add(k.Cell);
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
            Int3 front = s.WallCells.Count > 0 ? Place.FoundFrom(inst, s) : default;
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

    /// <summary>A piece and everything standing on it (and on that).</summary>
    static List<int> Carried(DecoPlan plan, int index)
    {
        var all = new List<int> { index };
        for (int k = 0; k < all.Count; k++)
            for (int i = 0; i < plan.Placements.Count; i++)
                if (plan.Placements[i].Support == all[k] && !all.Contains(i)) all.Add(i);
        return all;
    }

    /// <summary>
    /// The walk-level cells a solid piece takes from a walker: those it stands in, and those whose middle
    /// lies within a person's radius of its collision (its boxes between a step's height and a head's,
    /// seen from above). A route through cell middles never brushes it.
    /// </summary>
    HashSet<Int3> Blocking(DecoPlacement pl, PlaceRoom room)
    {
        var cells = new HashSet<Int3>(Standing(DecoLayout.Footprint(Forge, pl), room.Corner));
        float reach = Slice.Player.Survivor.Radius;
        foreach (DecoBox box in Forge.Mesh(pl.Look).Boxes)
        {
            var corners = box.Corners().Select(c => System.Numerics.Vector3.Transform(c, pl.Transform)).ToList();
            if (corners.Max(c => c.Y) < 0.35f || corners.Min(c => c.Y) > Slice.Player.Survivor.Height) continue;
            NV2[] hull = Hull(corners.Select(c => new NV2(c.X, c.Z)).ToList());
            int x0 = (int)MathF.Floor(hull.Min(h => h.X) - reach), x1 = (int)MathF.Floor(hull.Max(h => h.X) + reach);
            int z0 = (int)MathF.Floor(hull.Min(h => h.Y) - reach), z1 = (int)MathF.Floor(hull.Max(h => h.Y) + reach);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    if (Outside(hull, new NV2(x + 0.5f, z + 0.5f)) < reach) cells.Add(new Int3(room.Corner.X + x, room.Corner.Y, room.Corner.Z + z));
        }
        return cells;
    }

    /// <summary>The convex hull of a few points, counter-clockwise (a box's corners seen from above).</summary>
    static NV2[] Hull(List<NV2> pts)
    {
        var hull = new List<NV2>();
        NV2 start = pts.OrderBy(p => p.X).ThenBy(p => p.Y).First(), at = start;
        do
        {
            hull.Add(at);
            NV2 next = pts[0] == at ? pts[1] : pts[0];
            foreach (NV2 q in pts)
            {
                float cross = (next.X - at.X) * (q.Y - at.Y) - (next.Y - at.Y) * (q.X - at.X);
                if (cross < 0 || cross == 0 && NV2.DistanceSquared(at, q) > NV2.DistanceSquared(at, next)) next = q;
            }
            at = next;
        } while (at != start && hull.Count <= pts.Count);
        return hull.ToArray();
    }

    /// <summary>How far a point lies outside a convex polygon (0 inside).</summary>
    static float Outside(NV2[] poly, NV2 p)
    {
        if (poly.Length < 3) return poly.Min(q => NV2.Distance(q, p));
        bool inside = true;
        float best = float.MaxValue;
        for (int i = 0; i < poly.Length; i++)
        {
            NV2 a = poly[i], e = poly[(i + 1) % poly.Length] - a;
            if (e.X * (p.Y - a.Y) - e.Y * (p.X - a.X) < 0) inside = false;
            float t = Math.Clamp(NV2.Dot(p - a, e) / MathF.Max(e.LengthSquared(), 1e-9f), 0f, 1f);
            best = MathF.Min(best, NV2.Distance(a + e * t, p));
        }
        return inside ? 0f : best;
    }

    /// <summary>The floor cells a solid piece stands in: a body as wide as a person can't use them, even where the piece misses the cell's middle.</summary>
    static IEnumerable<Int3> Standing(DecoRect rect, Int3 corner) => CellsOf(rect, corner, 0.05f);

    /// <summary>
    /// The floor cells a body can't stand in the middle of for a footprint: what a walking route (which runs
    /// through cell middles) must go round. The footprint is grown by the body's radius, so a piece reaching
    /// nearly to a cell's middle takes that cell too, and one narrowing a corridor below a body's width closes it.
    /// </summary>
    public static IEnumerable<Int3> CentresOf(DecoRect rect, Int3 corner)
    {
        DecoRect grown = rect.Shrink(-(Slice.Player.Survivor.Radius + 0.02f));
        foreach (Int3 c in CellsOf(grown, corner, 0f))
        {
            var p = new NV2(c.X - corner.X + 0.5f, c.Z - corner.Z + 0.5f) - grown.Centre;
            float u = grown.U.LengthSquared(), v = grown.V.LengthSquared();
            if (u > 0f && v > 0f && MathF.Abs(NV2.Dot(p, grown.U)) <= u && MathF.Abs(NV2.Dot(p, grown.V)) <= v) yield return c;
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

/// <summary>A deco piece a ray met: its room, its placement and the piece.</summary>
public sealed record DecoHit(PlaceRoom Room, int Index, DecoPiece Piece, DecoNode Node);

public partial class Place
{
    /// <summary>The aim-only layer: pieces you can look at, salvage and walk through (litter, things on tables, ceiling lamps).</summary>
    public const uint SceneryLayer = 1u << 10;

    sealed class Dressed
    {
        public Node3D Node;
        public readonly List<DecoNode> Pieces = new();
        /// <summary>How far the room's pieces have dissolved in (0..1), and which way they are going (+1 in, -1 out, 0 still).</summary>
        public float Blend;
        public int Way;
    }

    readonly Dictionary<int, Dressed> _dressed = new();

    /// <summary>Rooms whose pieces are dissolving in or out (out: already let go of, freed once gone).</summary>
    readonly List<Dressed> _dressFading = new();

    /// <summary>How long a room's pieces take to dissolve in or out, s.</summary>
    const float DressBlendSeconds = 0.5f;

    void StepDressFades(float dt)
    {
        if (_dressFading.Count == 0) return;
        foreach (Dressed d in _dressFading)
        {
            d.Blend = Mathf.Clamp(d.Blend + d.Way * dt / DressBlendSeconds, 0f, 1f);
            if (d.Way > 0 && d.Blend >= 1f)
            {
                PlanetSurface.Fx.Fade.Clear(d.Node);
                d.Way = 0;
            }
            else if (d.Way < 0 && d.Blend <= 0f)
            {
                PlanetSurface.Fx.Fade.Forget(d.Node);
                d.Node.QueueFree();
                d.Way = 0;
            }
            else PlanetSurface.Fx.Fade.Set(d.Node, 0f, d.Blend);
        }
        _dressFading.RemoveAll(d => d.Way == 0);
    }
    readonly Dictionary<ulong, (PlaceRoom Room, int Index)> _pieceOf = new();

    /// <summary>
    /// Dresses the place, a few pieces a step (the meshes were worked out on the worker). Pieces that
    /// block walking collide on the world layer; the rest (litter, things on tables, ceiling lamps)
    /// are scenery on an aim-only layer, there to look at and salvage. Salvaged pieces stay gone.
    /// A small place is dressed whole; a large one room by room as you near each room (<see cref="TickDress"/>).
    /// </summary>
    internal IEnumerable<int> Dress()
    {
        if (Decor.Shared == null || Memory.Rooms == null) yield break;
        PlaceRules rules = Places.Rules;
        _dressByRoom = Memory.Rooms.Values.Sum(r => r.Plan.Placements.Count) > rules.DressAllUnder;
        Vector3 at = FromWorld(Places.Focus());
        foreach (PlaceRoom room in Memory.Rooms.Values)
        {
            if (_dressByRoom && Away(room, at) > rules.DressRadius) continue;
            foreach (int x in DressRoom(room)) yield return x;
        }
    }

    bool _dressByRoom;
    IEnumerator<int> _dressing;
    int _dressTimer;

    /// <summary>How far a point is from a room (a storey counts for its height).</summary>
    static float Away(PlaceRoom r, Vector3 local)
    {
        float dx = MathF.Max(0f, MathF.Max(r.Corner.X - local.X, local.X - (r.Corner.X + r.Room.Width)));
        float dz = MathF.Max(0f, MathF.Max(r.Corner.Z - local.Z, local.Z - (r.Corner.Z + r.Room.Depth)));
        float dy = MathF.Max(0f, MathF.Abs(local.Y - r.Corner.Y) - r.Room.Height);
        return MathF.Sqrt(dx * dx + dz * dz + dy * dy);
    }

    /// <summary>A large place's rooms furnished as you come near them and cleared behind you, within a frame's budget.</summary>
    void TickDress()
    {
        if (!_dressByRoom) return;
        if (_dressing != null)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            double budget = PlanetSurface.Fx.LoadBudget.Left(1.5);
            while (clock.Elapsed.TotalMilliseconds < budget)
                if (!_dressing.MoveNext()) { _dressing = null; break; }
            PlanetSurface.Fx.LoadBudget.Spend(clock.Elapsed.TotalMilliseconds);
            return;
        }
        if (--_dressTimer > 0) return;
        _dressTimer = 10;
        PlaceRules rules = Places.Rules;
        Vector3 at = FromWorld(Places.Focus());
        foreach (int module in _dressed.Keys.ToList())
            if (Away(Memory.Rooms[module], at) > rules.UndressRadius) Undress(module);
        PlaceRoom next = null;
        float best = rules.DressRadius;
        foreach (PlaceRoom room in Memory.Rooms.Values)
        {
            if (_dressed.ContainsKey(room.Module)) continue;
            float d = Away(room, at);
            if (d < best) { best = d; next = room; }
        }
        if (next != null) _dressing = DressRoom(next).GetEnumerator();
    }

    void Undress(int module)
    {
        Dressed d = _dressed[module];
        PlanetSurface.Fx.PopLog.Note("props", "room undressed", Away(Memory.Rooms[module], FromWorld(Places.EyeAt())), 1.2f, 1f / (60f * DressBlendSeconds));
        foreach (DecoNode n in d.Pieces)
            if (n?.Body != null)
            {
                _pieceOf.Remove(n.Body.GetInstanceId());
                n.Body.ProcessMode = ProcessModeEnum.Disabled; // nothing to aim at or bump while it goes
            }
        d.Way = -1;
        if (!_dressFading.Contains(d)) _dressFading.Add(d);
        _dressed.Remove(module);
    }

    IEnumerable<int> DressRoom(PlaceRoom room)
    {
        Decor decor = Decor.Shared;
        if (_dressed.ContainsKey(room.Module)) yield break;
        // A hair off the floor: nothing the dressing lays flat is ever coplanar with the blocks.
        var d = new Dressed { Node = new Node3D { Name = $"Deco {room.Module}", Position = new Vector3(room.Corner.X, room.Corner.Y + room.Floor + 0.008f, room.Corner.Z) } };
        AddChild(d.Node);
        _dressed[room.Module] = d;
        HashSet<int> taken = Memory.DecoTaken.GetValueOrDefault(room.Module);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < room.Plan.Placements.Count; i++)
        {
            if (taken != null && taken.Contains(i)) { d.Pieces.Add(null); continue; }
            DecoPlacement pl = room.Plan.Placements[i];
            var at = new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad(pl.Yaw)), new Vector3(pl.X, pl.Y, pl.Z));
            DecoNode.CollisionLayer = room.Solid.Contains(i) ? Layers.World : SceneryLayer;
            DecoNode n;
            try { n = DecoNode.Create(decor.Meshes, pl.Look, at, collide: true, lights: false); }
            catch (Exception e)
            {
                GD.PushWarning($"could not dress {room.Room.RoomId} with {pl.Piece}: {e.Message}");
                d.Pieces.Add(null);
                continue;
            }
            d.Node.AddChild(n);
            PlanetSurface.Fx.Fade.Set(n, 0f, 0f); // the room's pieces dissolve in together once all are in
            d.Pieces.Add(n);
            if (n.Body != null) _pieceOf[n.Body.GetInstanceId()] = (room, i);
            if (clock.Elapsed.TotalMilliseconds > 1.2)
            {
                yield return 0;
                clock.Restart();
            }
        }
        DecoNode.CollisionLayer = Layers.World;
        if (room.Plan.Cables.Count > 0)
        {
            // The wires strung between pieces, hung by the library's cable helper (anchored at both ends).
            var mesh = new DecoMeshData();
            foreach (DecoPlanCable c in room.Plan.Cables)
            {
                if (taken != null && (taken.Contains(c.From) || taken.Contains(c.To))) continue;
                DecoCable cable = c.AsCable();
                foreach (DecoPart part in DecoCables.Tubes(DecoCables.Path(cable), cable.Radius, cable.Slot))
                    DecoEmit.Part(mesh, part, (_, _, _) => DecoRgb.Hex("1e2024"), _ => DecoSurface.Matte);
            }
            d.Node.AddChild(new MeshInstance3D { Name = "Wires", Mesh = DecoGodot.ToMesh(mesh, decor.Meshes.Materials) });
        }
        PlanetSurface.Fx.PopLog.Note("props", "room dressed", Away(room, FromWorld(Places.EyeAt())), 1.2f, 1f / (60f * DressBlendSeconds));
        d.Blend = 0f;
        d.Way = 1;
        PlanetSurface.Fx.Fade.Set(d.Node, 0f, 0f);
        _dressFading.Add(d);
        yield return 0;
    }

    /// <summary>Rooms dressed now, and pieces standing in them (tests).</summary>
    public int DecoRooms => _dressed.Count;
    public int DecoPieces => _dressed.Values.Sum(d => d.Pieces.Count(n => n != null && n.Visible));

    /// <summary>Every dressed piece standing, with its room and placement (tests, screenshots).</summary>
    public IEnumerable<DecoHit> Pieces()
    {
        foreach (PlaceRoom room in Memory.Rooms?.Values ?? Enumerable.Empty<PlaceRoom>())
        {
            if (!_dressed.TryGetValue(room.Module, out Dressed d)) continue;
            for (int i = 0; i < d.Pieces.Count; i++)
                if (d.Pieces[i] is DecoNode n && n.Visible) yield return new DecoHit(room, i, Decor.Shared.Catalogue.Piece(n.Look.Piece), n);
        }
    }

    public DecoHit PieceOf(GodotObject collider)
    {
        if (collider == null || !_pieceOf.TryGetValue(collider.GetInstanceId(), out var at)) return null;
        if (!_dressed.TryGetValue(at.Room.Module, out Dressed d) || d.Pieces[at.Index] is not DecoNode node) return null;
        DecoPiece piece = Decor.Shared.Catalogue.Piece(node.Look.Piece);
        return new DecoHit(at.Room, at.Index, piece, node);
    }
    /// <summary>A piece salvaged: gone from the room for good, and whatever stood on it with it.</summary>
    public void TakePiece(DecoHit hit)
    {
        if (!Memory.DecoTaken.TryGetValue(hit.Room.Module, out HashSet<int> taken)) Memory.DecoTaken[hit.Room.Module] = taken = new HashSet<int>();
        Dressed view = _dressed.GetValueOrDefault(hit.Room.Module);
        var gone = new List<int> { hit.Index };
        for (int k = 0; k < gone.Count; k++)
            for (int i = 0; i < hit.Room.Plan.Placements.Count; i++)
                if (hit.Room.Plan.Placements[i].Support == gone[k] && !gone.Contains(i)) gone.Add(i);
        foreach (int i in gone)
        {
            taken.Add(i);
            hit.Room.Solid.Remove(i);
            PlaceParts.Taken(Memory, hit.Room.Module, i);
            if (view == null || view.Pieces[i] is not DecoNode n) continue;
            if (n.Body != null) _pieceOf.Remove(n.Body.GetInstanceId());
            n.Visible = false;
            n.ProcessMode = ProcessModeEnum.Disabled;
        }
        Memory.Modified = true;
    }
}
