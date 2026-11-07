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
        var reserved = Reserved(inst);
        string state = inst.State.ToString().ToLowerInvariant();
        bool alien = (inst.Family.Style ?? "").StartsWith("alien_", StringComparison.Ordinal);
        DecoTech? tech = TechOf(inst);
        foreach (PlacedModule m in layout.Modules)
        {
            if (m.Kind is not (ModuleKind.Room or ModuleKind.Hall or ModuleKind.Vault)) continue;
            if (inst.Extras.Secrets.Any(s => s.Module == m.Index)) continue; // a hidden room keeps what the generator left
            if (Interior(inst, m) is not { } room0) continue;
            var (y, x0, x1, z0, z1, height, open) = room0;
            int w = x1 - x0 + 1, d = z1 - z0 + 1;
            if (open < 9 || open < w * d * 0.85f || height < 2) continue; // not a rectangle: the generator's props stay
            var room = new DecoRoom
            {
                Width = w, Depth = d, Height = height, RoomId = m.Purpose, State = state,
                Age = inst.AgeOf(m.Index) switch { Age.New => DecoAge.Fresh, Age.Preserved => DecoAge.Preserved, _ => DecoAge.OldWorld },
                Exposed = inst.ExposureOf(m.Index) != Exposure.Indoors,
                Tech = tech,
                Set = alien ? "alien" : null,
                Seed = Hash.Combine(inst.SpawnSeed, (ulong)m.Index + 1),
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
            DecoPlan plan;
            try { plan = Poi.Dress(room, inst.Kit?.Id); }
            catch (Exception) { continue; }
            var placeRoom = new PlaceRoom { Module = m.Index, Corner = new Int3(x0, y, z0), Room = room, Plan = plan };
            // Pieces on a reserved cell (or standing on one that is) are left out.
            var dropped = new HashSet<int>();
            for (int i = 0; i < plan.Placements.Count; i++)
            {
                DecoPlacement pl = plan.Placements[i];
                if (pl.Support >= 0 && dropped.Contains(pl.Support)) { dropped.Add(i); continue; }
                foreach (Int3 c in CellsOf(DecoLayout.Footprint(Forge, pl), placeRoom.Corner, 0.05f))
                    if (reserved.Contains(c) || reserved.Contains(c + Int3.Up)) { dropped.Add(i); break; }
                if (!dropped.Contains(i) && pl.Support < 0 && !Held(inst, pl, DecoLayout.Footprint(Forge, pl), placeRoom.Corner)) dropped.Add(i);
            }
            var kept = memory.DecoTaken.GetValueOrDefault(m.Index) ?? new HashSet<int>();
            foreach (int i in dropped) kept.Add(i);
            if (kept.Count > 0) memory.DecoTaken[m.Index] = kept;
            // The pieces' meshes are worked out here, off the main thread (the forge keeps them).
            for (int i = 0; i < plan.Placements.Count; i++)
                if (!kept.Contains(i)) Forge.Mesh(plan.Placements[i].Look);
            for (int i = 0; i < plan.Placements.Count; i++)
            {
                if (kept.Contains(i) || !DecoLayout.Blocks(Forge, plan.Placements[i])) continue;
                placeRoom.Solid.Add(i);
                foreach (Int3 c in CentresOf(DecoLayout.Footprint(Forge, plan.Placements[i]), placeRoom.Corner)) placeRoom.Cells.Add(c);
            }
            // The generator's generic props in this room go: the library's pieces stand there instead.
            foreach (Int3 c in m.Cells)
            {
                if (c.Y != y && c.Y != y + 1) continue;
                if (c.X < x0 || c.X > x1 || c.Z < z0 || c.Z > z1) continue;
                Block b = inst.Blocks.Get(c);
                if (b.Role == Role.Prop || b.Role == Role.Story && b.Type == BlockType.Barricade) inst.Blocks.Set(c, Block.Air);
            }
            rooms[m.Index] = placeRoom;
        }
        KeepWaysOpen(memory, rooms);
        return rooms;
    }

    /// <summary>
    /// Whether a piece has something to hold it: one on the floor needs floor under all of it (decay leaves
    /// holes), one up a wall or under a ceiling needs that wall or ceiling still there.
    /// </summary>
    static bool Held(PoiInstance inst, DecoPlacement pl, DecoRect foot, Int3 corner)
    {
        var nav = inst.Nav();
        if (pl.Y < 0.2f)
        {
            foreach (Int3 c in CellsOf(foot, corner, 0.2f))
                if (!nav.Solid(c + Int3.Down)) return false;
            return true;
        }
        int up = (int)MathF.Floor(pl.Y);
        foreach (Int3 c in CellsOf(foot, corner, 0f))
        {
            Int3 at = c + Int3.Up * up;
            foreach (Dir d in Dirs.All)
                if (nav.Solid(at + d.Vec())) return true;
        }
        return false;
    }

    /// <summary>
    /// The dressing never closes a way the architect checked: every doorway, stair, cache, keycard
    /// and hidden wall reachable on the bare blocks stays reachable round the solid pieces. A piece
    /// standing on the bare route to one that was cut off goes (and what stood on it).
    /// </summary>
    void KeepWaysOpen(PlaceMemory memory, Dictionary<int, PlaceRoom> rooms)
    {
        PoiInstance inst = memory.Inst;
        var bare = inst.Nav();
        Int3 entry = inst.Layout.EntranceCell;
        var open = bare.Reachable(entry);
        var need = new HashSet<Int3>();
        void Need(Int3 c) { if (open.Contains(c)) need.Add(c); }
        foreach (Doorway d in inst.Layout.Doorways)
            foreach (Int3 c in d.Cells)
                foreach (Dir h in Dirs.Horizontal) Need(c + h.Vec());
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
        for (int round = 0; round < 200; round++)
        {
            var reach = Place.NavOf(memory, rooms).Reachable(entry);
            Int3? cut = null;
            foreach (Int3 c in need)
                if (!reach.Contains(c)) { cut = c; break; }
            if (cut is not Int3 lost) return;
            List<Int3> way = bare.FindPath(entry, lost);
            if (way == null) { need.Remove(lost); continue; }
            bool freed = false;
            foreach (PlaceRoom r in rooms.Values)
                foreach (int i in r.Solid.ToList())
                    if (CentresOf(DecoLayout.Footprint(Forge, r.Plan.Placements[i]), r.Corner).Any(way.Contains))
                    {
                        Drop(memory, r, i);
                        freed = true;
                    }
            if (!freed) need.Remove(lost); // nothing of ours stands in the way
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
        foreach (int i in r.Solid)
            foreach (Int3 c in CentresOf(DecoLayout.Footprint(Forge, r.Plan.Placements[i]), r.Corner)) r.Cells.Add(c);
    }

    /// <summary>
    /// A module's open interior: the level you walk on (the lowest with open floor: the architect's
    /// modules start at it, a kit's a layer of floor below), its box and clear height, its open cells.
    /// </summary>
    public static (int Y, int X0, int X1, int Z0, int Z1, int Height, int Cells)? Interior(PoiInstance inst, PlacedModule m)
    {
        foreach (int y in new[] { m.Min.Y, m.Min.Y + 1 })
        {
            var inside = m.Cells.Where(c => c.Y == y && Open(inst.Blocks.Get(c)) && !inst.Blocks.Get(c + Int3.Down).IsAir).ToList();
            if (inside.Count == 0) continue;
            int x0 = inside.Min(c => c.X), x1 = inside.Max(c => c.X), z0 = inside.Min(c => c.Z), z1 = inside.Max(c => c.Z);
            int height = 0;
            for (int h = 0; h < 12 && Open(inst.Blocks.Get(new Int3((x0 + x1) / 2, y + h, (z0 + z1) / 2))); h++) height++;
            return (y, x0, x1, z0, z1, height, inside.Count);
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
        _ => inst.Family.Tags.Contains("settlement") ? DecoTech.Low : null,
    };

    /// <summary>Cells the dressing must leave clear: fixtures, ways up and down, doorways and a step inside them.</summary>
    static HashSet<Int3> Reserved(PoiInstance inst)
    {
        var r = new HashSet<Int3>();
        foreach (LootCache c in inst.Caches) r.Add(c.Cell);
        foreach (TerminalLog t in inst.Terminals) r.Add(t.Cell);
        foreach (KeyCard k in inst.Extras.Keys) r.Add(k.Cell);
        if (inst.HasCore) r.Add(inst.CoreCell);
        foreach (var (p, b) in inst.Blocks.Blocks())
            if (b.Type is BlockType.Ladder or BlockType.Stair or BlockType.Ramp or BlockType.Door or BlockType.Breakable or BlockType.Remains)
                r.Add(p);
        foreach (Doorway d in inst.Layout.Doorways)
            foreach (Int3 c in d.Cells)
                foreach (Dir h in Dirs.Horizontal) r.Add(c + h.Vec());
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
    }

    readonly Dictionary<int, Dressed> _dressed = new();
    readonly Dictionary<ulong, (PlaceRoom Room, int Index)> _pieceOf = new();

    /// <summary>
    /// Dresses every room, a few pieces a step (the meshes were worked out on the worker). Pieces
    /// that block walking collide on the world layer; the rest (litter, things on tables, ceiling
    /// lamps) are scenery on an aim-only layer, there to look at and salvage. Salvaged pieces stay gone.
    /// </summary>
    internal IEnumerable<int> Dress()
    {
        Decor decor = Decor.Shared;
        if (decor == null || Memory.Rooms == null) yield break;
        foreach (PlaceRoom room in Memory.Rooms.Values)
        {
            if (_dressed.ContainsKey(room.Module)) continue;
            // A hair off the floor: nothing the dressing lays flat is ever coplanar with the blocks.
            var d = new Dressed { Node = new Node3D { Name = $"Deco {room.Module}", Position = new Vector3(room.Corner.X, room.Corner.Y + 0.008f, room.Corner.Z) } };
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
            DecoRooms++;
            yield return 0;
        }
    }

    /// <summary>Rooms dressed so far, and pieces standing in them (tests).</summary>
    public int DecoRooms { get; private set; }
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
            if (view == null || view.Pieces[i] is not DecoNode n) continue;
            if (n.Body != null) _pieceOf.Remove(n.Body.GetInstanceId());
            n.Visible = false;
            n.ProcessMode = ProcessModeEnum.Disabled;
        }
        Memory.Modified = true;
    }
}
