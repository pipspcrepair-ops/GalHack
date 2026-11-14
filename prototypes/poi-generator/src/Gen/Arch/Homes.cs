#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>What a placer asks of a layout it is about to set down: which way its front looks (at the road), and the world's tech level.</summary>
/// <param name="Ruin">How long ago and how hard its world fell: places below ground plan their collapses, caves and squatters by it.</param>
/// <param name="Culture">Its builders' habit, where the world knows it (a street's culture, a region's): otherwise rolled per place.</param>
public sealed record LayoutHints(Dir Front = Dir.North, TechLevel? Tech = null, CityLot? Lot = null, HomeHints? Home = null, RuinAge? Ruin = null, int? Culture = null);

/// <summary>One plain building of a lot: its box (roof and all), the height of its eaves and the way its front looks.</summary>
public sealed record PlainBox(Int3 Min, Int3 Max, int Eave, Dir Front);

/// <summary>
/// The small ruins of the old world, drawn by hand rather than planned from a program, at real size
/// against a 1.8 m person: houses you know for houses from the road (10 to 16 m across, 4 m a storey),
/// a farmstead with its barn and silo, sheds, shops, and the stop on the road each age kept: a coaching
/// inn, a petrol station, a charging and fuel-cell depot, or something nobody here built.
///
/// They are built the way a player builds. POI cells are 1 m and the player's large block is 2 m, so
/// everything keeps to a 2 m lattice in plan and in storeys, in shapes the player has: walls and
/// slabs are large half blocks (1 m thick on their 2 m module), roofs are 45 degree slopes (a pair of
/// 1 m ramps and the fill under them is one large slope), floors and paths are never blocks of their own but a finish painted on the top of the block or the ground under them (<see cref="BlockInfo.IsFinish"/>), doors are
/// the standard door (one large block, nothing low over the way to it) and windows are panes in a 2 m wide opening, and everything is plain paint on a
/// plain material. You can read a ruin and rebuild it block for block.
///
/// Everything stands on the ground, so no cell goes below it and the terrain runs flush up to
/// every wall. A lot is drawn in its own frame (u along the front, v back from it) and turned to face
/// the road; <see cref="StateTransforms.Weathered"/> breaks it believably afterwards.
/// </summary>
public static partial class Homes
{
    /// <summary>A quarter-turned frame: u along the lot's front, v back from it, y up; the lot's front looks along -v.</summary>
    readonly record struct Frame(int Q, int X, int Z)
    {
        public Int3 At(int u, int y, int v) => Q switch
        {
            0 => new Int3(X + u, y, Z + v),
            1 => new Int3(X - v, y, Z + u),
            2 => new Int3(X - u, y, Z - v),
            _ => new Int3(X + v, y, Z - u),
        };
        public Dir Turn(Dir d) => (Dir)(((int)d + Q) % 4);
        public Frame Shift(int u, int v) { Int3 o = At(u, 0, v); return this with { X = o.X, Z = o.Z }; }
    }

    /// <summary>A building's plain materials, by the tech level that built it.</summary>
    sealed record Stuff(Material WallM, Rgb Wall, Material RoofM, Rgb Roof, Material BaseM, Rgb Base, Rgb Floor, Material StackM, Rgb Stack, Rgb Door, Rgb Sign)
    {
        public static Stuff For(TechLevel tech, Rng r) => tech switch
        {
            TechLevel.Low => new Stuff(Material.Timber, r.Pick(new[] { new Rgb(0x7c, 0x6a, 0x56), new Rgb(0x6e, 0x5c, 0x4a), new Rgb(0x8a, 0x7c, 0x68) }),
                Material.Timber, new Rgb(0x46, 0x3a, 0x30), Material.Rock, new Rgb(0x6c, 0x68, 0x60), new Rgb(0x5a, 0x4a, 0x3a), Material.Rock, new Rgb(0x70, 0x6a, 0x60), new Rgb(0x4a, 0x3c, 0x30), new Rgb(0x7a, 0x4a, 0x34)),
            TechLevel.High => new Stuff(Material.Resin, r.Pick(new[] { new Rgb(0xbe, 0xc2, 0xbe), new Rgb(0xa8, 0xb0, 0xb4), new Rgb(0xc4, 0xbc, 0xac) }),
                Material.Resin, new Rgb(0x3e, 0x46, 0x50), Material.Concrete, new Rgb(0x7e, 0x80, 0x80), new Rgb(0x6c, 0x70, 0x74), Material.Concrete, new Rgb(0x7e, 0x80, 0x80), new Rgb(0x34, 0x3a, 0x40), new Rgb(0x2e, 0x5a, 0x7a)),
            _ => r.Chance(0.6f)
                ? new Stuff(Material.Ceramic, r.Pick(new[] { new Rgb(0x84, 0x4c, 0x3a), new Rgb(0x92, 0x60, 0x48), new Rgb(0x7a, 0x52, 0x44) }),
                    Material.Ceramic, r.Pick(new[] { new Rgb(0x44, 0x46, 0x4c), new Rgb(0x5e, 0x3a, 0x30) }), Material.Concrete, new Rgb(0x74, 0x72, 0x6c), new Rgb(0x62, 0x58, 0x4c), Material.Ceramic, new Rgb(0x6a, 0x3e, 0x32), new Rgb(0x3e, 0x3a, 0x36), new Rgb(0x8c, 0x32, 0x28))
                : new Stuff(Material.Concrete, r.Pick(new[] { new Rgb(0xa8, 0xa2, 0x94), new Rgb(0x9c, 0x9a, 0x8e) }),
                    Material.Ceramic, new Rgb(0x4c, 0x44, 0x40), Material.Concrete, new Rgb(0x6a, 0x68, 0x62), new Rgb(0x62, 0x58, 0x4c), Material.Ceramic, new Rgb(0x6a, 0x3e, 0x32), new Rgb(0x3e, 0x3a, 0x36), new Rgb(0x8c, 0x32, 0x28)),
        };
    }

    enum RoofKind { Gable, Lean, Flat }

    /// <summary>A rectangle of a building, outer faces inclusive, and the way its ridge runs.</summary>
    sealed record Wing(int U0, int V0, int U1, int V1, bool RidgeAlongU = true, RoofKind Kind = RoofKind.Gable);

    sealed class Room
    {
        public string Purpose = "quarters";
        public ModuleKind Kind = ModuleKind.Room;
        public int Storey, U0, V0, U1, V1, Module = -1;
        /// <summary>A room that isn't a rectangle (a round house's): its cells. U0 to V1 is then the square inside it its furniture keeps to.</summary>
        public HashSet<(int U, int V)>? Shape;
        public IEnumerable<(int U, int V)> Cells()
        {
            if (Shape != null) { foreach (var c in Shape) yield return c; yield break; }
            for (int v = V0; v <= V1; v++)
                for (int u = U0; u <= U1; u++) yield return (u, v);
        }
        public bool Holds(int u, int v) => Shape?.Contains((u, v)) ?? u >= U0 && u <= U1 && v >= V0 && v <= V1;
    }

    /// <summary>A lot being drawn: the canvas, its turn, what it's made of.</summary>
    sealed class Lot
    {
        public Canvas C = null!;
        public Rng R = null!;
        public Stuff S = null!;
        public Frame F;
        public TechLevel Tech;
        /// <summary>The culture of the region it stands in, when its world gave one.</summary>
        public HomeHints? Home;
        public int Loot = -1;

        public void Plate(Frame f, int u, int v, Rgb c, Role role = Role.Debris)
        {
            Int3 p = f.At(u, 0, v);
            C.Blocks.Pave(p, new Block(BlockType.Plate, Material.Concrete, c, role));
        }

        /// <summary>The patch of ground at the way in: every lot's walk begins here.</summary>
        public void Approach(Frame f, int u, int v)
        {
            var cells = new List<Int3>();
            for (int dv = -1; dv <= 1; dv++)
                for (int du = -1; du <= 1; du++)
                    for (int y = 0; y <= 1; y++) cells.Add(f.At(u + du, y, v + dv));
            C.EntranceModule = C.AddModule(ModuleKind.Outdoor, "approach", 0, cells);
            C.EntranceCell = f.At(u, 0, v);
            C.EntranceFacing = f.Turn(Dir.North);
        }
    }

    /// <summary>One building: wings and rooms, then its walls, openings and roof in that order.</summary>
    sealed partial class Hut
    {
        readonly Lot _l;
        readonly Frame _f;
        /// <summary>How far its ground floor stands off the ground: a house on stilts or legs, a room on a tower.</summary>
        public int Lift;
        /// <summary>The outer walls' cladding by cell (u, y over the ground floor, v): courses of board, a timber frame, a stone ground floor.</summary>
        public Func<int, int, int, Block>? Skin;
        public readonly List<Wing> Wings = new();
        public readonly List<Room> Rooms = new();
        public readonly HashSet<(int U, int V)> Foot = new();
        readonly HashSet<(int U, int V, int S)> _door = new(), _open = new(), _stair = new();
        readonly Dictionary<(int U, int V), int> _top = new();
        readonly HashSet<(int U, int V)> _verge = new();
        public int Storeys = 1, Height = 3;
        /// <summary>No ceiling under the roof: a barn's or a shed's open rafters.</summary>
        public bool Open;
        public Stuff S;

        public Hut(Lot lot, Frame frame, Stuff? stuff = null) { _l = lot; _f = frame; S = stuff ?? lot.S; }

        Canvas C => _l.C;
        Int3 P(int u, int y, int v) => _f.At(u, y + Lift, v);
        public int Walk(int storey) => storey * (Height + 1);
        public int Ring(int storey) => Walk(storey) + Height;
        int Eave => Ring(Storeys - 1);
        bool Edge(int u, int v) => !Foot.Contains((u - 1, v)) || !Foot.Contains((u + 1, v)) || !Foot.Contains((u, v - 1)) || !Foot.Contains((u, v + 1));

        static Block Cube(Material m, Rgb c, Role r) => new(BlockType.Cube, m, c, r);

        public Room Add(string purpose, int storey, int u0, int v0, int u1, int v1, ModuleKind kind = ModuleKind.Room)
        {
            var r = new Room { Purpose = purpose, Storey = storey, U0 = u0, V0 = v0, U1 = u1, V1 = v1, Kind = kind };
            Rooms.Add(r);
            return r;
        }

        /// <summary>Walls, floors and ceilings. Openings, modules and the roof come after.</summary>
        public void Raise()
        {
            foreach (Wing w in Wings)
                for (int v = w.V0; v <= w.V1; v++)
                    for (int u = w.U0; u <= w.U1; u++) Foot.Add((u, v));
            var inside = new HashSet<(int, int, int)>();
            foreach (Room r in Rooms)
                foreach (var (u, v) in r.Cells())
                    {
                        if (!Foot.Contains((u, v)) || Edge(u, v)) throw new InvalidOperationException($"room {r.Purpose} leaves the walls at {u},{v}");
                        inside.Add((u, v, r.Storey));
                        if (r.Storey == 0 && Lift == 0) _l.Plate(_f, u, v, S.Floor, Role.Floor);
                    }
            foreach (var (u, v) in Foot)
            {
                // Off the ground, the ground floor stands on a slab of its own.
                if (Lift > 0) C.Set(P(u, -1, v), Cube(S.BaseM, S.Floor, Role.Floor));
                for (int s = 0; s < Storeys; s++)
                {
                    bool room = inside.Contains((u, v, s));
                    for (int y = Walk(s); y < Ring(s); y++)
                        if (!room) C.Set(P(u, y, v), Skin != null && Edge(u, v) ? Skin(u, y, v) : y == 0 ? Cube(S.BaseM, S.Base, Role.Wall) : Cube(S.WallM, S.Wall, Role.Wall));
                    if (Edge(u, v)) C.Set(P(u, Ring(s), v), Skin != null ? Skin(u, Ring(s), v) : Cube(S.WallM, S.Wall, Role.Wall));
                    else if (!Open || s + 1 < Storeys || !room) C.Set(P(u, Ring(s), v), Cube(S.BaseM, S.Floor, Role.Floor));
                }
            }
        }

        /// <summary>The rooms as modules, once the stairs have taken their cells.</summary>
        public void Modules()
        {
            foreach (Room r in Rooms)
            {
                if (r.Module >= 0) continue;
                var cells = new List<Int3>();
                foreach (var (u, v) in r.Cells())
                        if (!_stair.Contains((u, v, r.Storey)))
                            for (int y = Walk(r.Storey); y < Ring(r.Storey); y++) cells.Add(P(u, y, v));
                r.Module = C.AddModule(r.Kind, r.Purpose, r.Storey, cells);
            }
            foreach (Room a in Rooms)
                foreach (Room b in Rooms)
                    if (a.Module < b.Module && a.Storey == b.Storey && a.U0 <= b.U1 + 2 && b.U0 <= a.U1 + 2 && a.V0 <= b.V1 + 2 && b.V0 <= a.V1 + 2) C.Link(a.Module, b.Module);
        }

        /// <summary>An opening through a wall, the standard door's by default (<see cref="DoorSize"/>): one large block, 2 m wide and tall.</summary>
        public void Gap(int u, int v, bool alongU, int storey = 0, int width = DoorSize.Wide, int tall = DoorSize.Tall)
        {
            for (int k = 0; k < width; k++)
            {
                int gu = alongU ? u + k : u, gv = alongU ? v : v + k;
                _door.Add((gu, gv, storey));
                for (int y = Walk(storey); y < Walk(storey) + tall; y++) C.Clear(P(gu, y, gv));
                // The floor's finish runs through the opening: a threshold is never a hole in the tiles.
                if (storey == 0 && Lift == 0) _l.Plate(_f, gu, gv, S.Floor, Role.Floor);
            }
        }

        /// <summary>The door in an outside wall: the standard door (<see cref="DoorSize"/>) in its opening, from the yard into a room.</summary>
        public void FrontDoor(int u, int v, Room room, int tall = DoorSize.Tall)
        {
            var cells = new List<Int3>();
            for (int k = 0; k < 2; k++)
                for (int y = 0; y < tall; y++) cells.Add(P(u + k, y, v));
            C.AddDoorway(cells, _f.Turn(Dir.North), room.Module, C.EntranceModule);
        }

        /// <summary>A flight up the inside of a wall, one metre up for one along, and the hole in the slab over it.</summary>
        public int Stair(int u, int v0, int fromStorey = 0)
        {
            int walk = Walk(fromStorey), rise = Height + 1;
            var cells = new List<Int3>();
            for (int k = 0; k < rise; k++)
            {
                int v = v0 + k;
                C.Clear(P(u, Ring(fromStorey), v));
                C.Set(P(u, walk + k, v), new Block(BlockType.Stair, S.BaseM, S.Base, Role.Traverse, _f.Turn(Dir.South)));
                // Solid under each step, so a flight stands on its own away from a wall.
                for (int y = walk; y < walk + k; y++) C.Set(P(u, y, v), new Block(BlockType.Cube, S.BaseM, S.Base.Scale(0.9f), Role.Traverse));
                for (int up = 0; up <= 3; up++)
                    if (walk + k + up < Ring(fromStorey + 1)) cells.Add(P(u, walk + k + up, v));
                _stair.Add((u, v, fromStorey));
                _stair.Add((u, v, fromStorey + 1));
            }
            return C.AddModule(ModuleKind.Stair, "stair", fromStorey, cells.Distinct().ToList());
        }

        /// <summary>Windows on the 2 m lattice, spaced along each room's outside walls, a metre off the floor.</summary>
        public void Windows(float backChance = 0.8f)
        {
            foreach (Room r in Rooms)
            {
                if (r.Shape != null) continue;
                void Side(int a0, int a1, Func<int, (int U, int V)> wall, Func<int, (int U, int V)> beyond, bool back)
                {
                    int len = a1 - a0 + 1, n = len >= 10 ? 2 : len >= 3 ? 1 : 0;
                    if (back && !_l.R.Chance(backChance)) return;
                    for (int i = 0; i < n; i++)
                    {
                        int c = (a0 + (int)((i + 0.5f) * len / n)) / 2 * 2; // on the lattice
                        var spots = new[] { c, c + 1 };
                        bool Free(int a) => !_door.Contains((wall(a).U, wall(a).V, r.Storey)) && !_open.Contains((wall(a).U, wall(a).V, r.Storey));
                        if (spots.Any(a => a < a0 || a > a1 || !Foot.Contains(wall(a)) || Foot.Contains(beyond(a)) || !Free(a) || !Free(a - 1) || !Free(a + 1))) continue;
                        foreach (int a in spots)
                        {
                            var (u, v) = wall(a);
                            _open.Add((u, v, r.Storey));
                            for (int y = Walk(r.Storey) + 1; y <= Walk(r.Storey) + 2 && y < Ring(r.Storey); y++)
                                C.Set(P(u, y, v), new Block(BlockType.Pane, Material.Glass, new Rgb(0x5a, 0x6c, 0x70), Role.Window));
                        }
                    }
                }
                Side(r.U0, r.U1, a => (a, r.V0 - 1), a => (a, r.V0 - 2), false);
                Side(r.U0, r.U1, a => (a, r.V1 + 1), a => (a, r.V1 + 2), true);
                Side(r.V0, r.V1, a => (r.U0 - 1, a), a => (r.U0 - 2, a), true);
                Side(r.V0, r.V1, a => (r.U1 + 1, a), a => (r.U1 + 2, a), true);
            }
        }

        /// <summary>
        /// The roof, a height for every column: a 45 degree slope of large slope blocks from eaves 2 m out
        /// from the walls up to the ridge (a pair of ramps and the fill under them each 2 m). Where wings
        /// meet the higher wins, so a cross gable joins its neighbour. A deck under the slope holds it
        /// together and the gable walls fill up to it; the attic stays hollow.
        /// </summary>
        public void Roof()
        {
            int low = Eave - 1;
            foreach (Wing w in Wings.Where(w => w.Kind != RoofKind.Flat))
            {
                int a0 = w.RidgeAlongU ? w.V0 : w.U0, a1 = w.RidgeAlongU ? w.V1 : w.U1, b0 = w.RidgeAlongU ? w.U0 : w.V0, b1 = w.RidgeAlongU ? w.U1 : w.V1;
                Dir up = w.RidgeAlongU ? Dir.South : Dir.East, down = up.Opposite();
                for (int a = a0 - 2; a <= a1 + 2; a++)
                    for (int b = b0; b <= b1; b++)
                    {
                        int left = a - (a0 - 2), right = a1 + 2 - a;
                        int k = w.Kind == RoofKind.Lean ? left : Math.Min(left, right);
                        (int U, int V) col = w.RidgeAlongU ? (b, a) : (a, b);
                        int y = low + k;
                        if (_top.TryGetValue(col, out int old))
                        {
                            if (old >= y) continue;
                            C.Remove(P(col.U, old, col.V));
                        }
                        _top[col] = y;
                        // The slope's end over a gable wall is the wall's own: its coping, in one clean line from eaves to ridge.
                        (int U, int V) past = w.RidgeAlongU ? (b == b0 ? b - 1 : b + 1, a) : (a, b == b0 ? b - 1 : b + 1);
                        bool verge = (b == b0 || b == b1) && !Foot.Contains(past);
                        if (verge) _verge.Add(col); else _verge.Remove(col);
                        Rgb colour = verge ? S.Wall : S.Roof;
                        Material stuff = verge ? S.WallM : S.RoofM;
                        C.Set(P(col.U, y, col.V), left == right ? Cube(stuff, colour, Role.Roof)
                            : new Block(BlockType.Ramp, stuff, colour, Role.Roof, _f.Turn(w.Kind == RoofKind.Lean || left < right ? up : down)));
                    }
            }
            foreach (var ((u, v), y) in _top)
            {
                bool inFoot = Foot.Contains((u, v));
                if (y - 1 > Eave || !inFoot && y > low) C.Set(P(u, y - 1, v), _verge.Contains((u, v)) ? Cube(S.WallM, S.Wall, Role.Roof) : Cube(S.RoofM, S.Roof.Scale(0.8f), Role.Roof));
                if (inFoot && Edge(u, v))
                    for (int h = Eave + 1; h < y; h++) C.Set(P(u, h, v), Cube(S.WallM, S.Wall, Role.Wall));
            }
        }

        /// <summary>A flat roof with a parapet round it, higher along the front.</summary>
        public void FlatRoof(int parapet = 1, int front = 2)
        {
            int v0 = Foot.Min(c => c.V);
            foreach (var (u, v) in Foot)
            {
                if (!Edge(u, v)) continue;
                // A house's parapet is its own cladding.
                for (int y = 1; y <= (v == v0 ? front : parapet); y++) C.Set(P(u, Eave + y, v), Skin?.Invoke(u, Eave + y, v) ?? Cube(S.WallM, S.Wall, Role.Wall));
            }
        }

        /// <summary>A small pane high in a gable.</summary>
        public void GableVent(int u, int v)
        {
            if (!_top.TryGetValue((u, v), out int y) || y < Eave + 4) return;
            C.Set(P(u, Eave + 2, v), new Block(BlockType.Pane, Material.Glass, new Rgb(0x5a, 0x6c, 0x70), Role.Window));
        }

        /// <summary>A masonry stack on a 2 m module, up through a wall and 2 m clear of the roof: the last thing to fall.</summary>
        public void Chimney(int u, int v, int du, int dv)
        {
            int top = Math.Max(_top.GetValueOrDefault((u, v)), _top.GetValueOrDefault((u + du, v + dv))) + 2;
            foreach (var (cu, cv) in new[] { (u, v), (u + du, v + dv) })
                for (int y = 0; y <= top; y++)
                {
                    Int3 p = P(cu, y, cv);
                    if (y == 0 && !C.Solid(p)) continue;
                    C.Set(p, Cube(S.StackM, y >= top ? S.Stack.Scale(0.6f) : S.Stack, Role.Decor));
                    C.X.Lasting.Add(p);
                }
        }

        /// <summary>A verandah under the eaves: a boarded strip along the front, posts holding the eave up.</summary>
        public void Verandah(int u0, int u1, int v)
        {
            for (int u = u0; u <= u1; u++)
                for (int d = 1; d <= 2; d++) _l.Plate(_f, u, v - d, S.Floor);
            foreach (int u in new[] { u0, u1 })
                for (int y = 0; y < 2; y++) C.Set(P(u, y, v - 2), new Block(BlockType.Pillar, Material.Timber, S.Door, Role.Decor));
        }

        /// <summary>A porch roof over a door in a gable end: a slope down from the wall on two posts.</summary>
        public void Porch(int u0, int u1, int v)
        {
            foreach (int u in Enumerable.Range(u0, u1 - u0 + 1))
            {
                C.Set(P(u, 3, v - 1), new Block(BlockType.Ramp, S.RoofM, S.Roof, Role.Roof, _f.Turn(Dir.South)));
                C.Set(P(u, 2, v - 1), Cube(S.RoofM, S.Roof.Scale(0.8f), Role.Roof));
                C.Set(P(u, 2, v - 2), new Block(BlockType.Ramp, S.RoofM, S.Roof, Role.Roof, _f.Turn(Dir.South)));
            }
            foreach (int u in new[] { u0, u1 })
                for (int y = 0; y < 2; y++) C.Set(P(u, y, v - 2), new Block(BlockType.Pillar, Material.Timber, S.Door, Role.Decor));
        }

        /// <summary>The building's box, for its decay.</summary>
        public void Box()
        {
            int u0 = Foot.Min(c => c.U), u1 = Foot.Max(c => c.U), v0 = Foot.Min(c => c.V), v1 = Foot.Max(c => c.V);
            int top = (_top.Count > 0 ? _top.Values.Max() : Eave) + 2;
            Int3 a = P(u0 - 2, -Lift, v0 - 2), b = P(u1 + 2, top, v1 + 2);
            C.X.Plain.Add(new PlainBox(Int3.Min(a, b), Int3.Max(a, b), Eave + Lift, _f.Turn(Dir.North)));
        }

        // ---- furniture: a few pieces against the walls, clear of the doors ------------------------

        bool Clear(Room r, int u, int v)
        {
            foreach (var (du, dv) in new[] { (0, 0), (1, 0), (-1, 0), (0, 1), (0, -1) })
                for (int k = 1; k <= 2; k++)
                    if (_door.Contains((u + du * k, v + dv * k, r.Storey)) || _stair.Contains((u + du * k, v + dv * k, r.Storey))) return false;
            return true;
        }

        void Piece(Room r, int u, int v, BlockType t, Material m, Rgb c, Dir d = Dir.North)
        {
            if (Clear(r, u, v) && r.Holds(u, v)) C.Blocks[P(u, Walk(r.Storey), v)] = new Block(t, m, c, Role.Prop, _f.Turn(d));
        }

        public void Furnish()
        {
            var wood = new Rgb(0x5c, 0x4c, 0x3c);
            var metal = new Rgb(0x5e, 0x5a, 0x54);
            foreach (Room r in Rooms)
                switch (r.Purpose)
                {
                    case "quarters":
                        Piece(r, r.U0, r.V1, BlockType.Bed, Material.Cloth, new Rgb(0x6e, 0x66, 0x5a));
                        Piece(r, r.U0 + 1, r.V1, BlockType.Bed, Material.Cloth, new Rgb(0x6e, 0x66, 0x5a));
                        Piece(r, r.U1, r.V1, BlockType.Locker, Material.Timber, wood);
                        break;
                    case "farm-kitchen":
                        for (int u = r.U0; u <= Math.Min(r.U1, r.U0 + 2); u++) Piece(r, u, r.V1, BlockType.Bench, Material.Steel, metal);
                        Piece(r, (r.U0 + r.U1) / 2, (r.V0 + r.V1) / 2, BlockType.Table, Material.Timber, wood);
                        break;
                    case "storage":
                        for (int v = r.V0 + 1; v <= r.V1; v += 2) Piece(r, r.U1, v, BlockType.Shelf, Material.Steel, metal);
                        break;
                    case "workshop":
                        for (int v = r.V0 + 1; v <= Math.Min(r.V1, r.V0 + 3); v++) Piece(r, r.U1, v, BlockType.Bench, Material.Steel, metal);
                        Piece(r, r.U0, r.V1, BlockType.Machine, Material.Rust, new Rgb(0x6a, 0x44, 0x34));
                        break;
                    case "mess":
                        for (int u = r.U0 + 2; u < r.U1 - 1; u += 3)
                            for (int v = r.V0 + 2; v < r.V1 - 1; v += 3) Piece(r, u, v, BlockType.Table, Material.Timber, wood);
                        break;
                    case "shop":
                        for (int u = r.U0 + 2; u < r.U1 - 1; u += 3)
                            for (int v = r.V0 + 2; v < r.V1; v++) Piece(r, u, v, BlockType.Shelf, Material.Steel, metal);
                        break;
                    case "washroom":
                        Piece(r, r.U1, r.V1, BlockType.Tank, Material.Steel, metal);
                        break;
                }
        }

        /// <summary>A cache on the floor of a room, in a corner clear of the doors: the lot's loot.</summary>
        public void Loot(Room r)
        {
            foreach (var (u, v) in new[] { (r.U0, r.V0), (r.U1, r.V0), (r.U0, r.V1), (r.U1, r.V1), (r.U1, (r.V0 + r.V1) / 2), ((r.U0 + r.U1) / 2, (r.V0 + r.V1) / 2) })
            {
                Int3 p = P(u, Walk(r.Storey), v);
                if (C.Solid(p) || !Clear(r, u, v) || !r.Holds(u, v) || _stair.Contains((u, v, r.Storey))) continue;
                C.Blocks.Remove(p);
                C.AddSlot(SlotKind.Loot, p, r.Module);
                _l.Loot = r.Module;
                return;
            }
            throw new InvalidOperationException($"no place for a cache in the {r.Purpose}");
        }
    }

    // ---- the front door of this class ---------------------------------------------------------

    public static void Compose(Canvas c, SiteDef site, FamilyDef fam, Rng rng, LayoutHints? hints)
    {
        var tech = hints?.Tech ?? (TechLevel)rng.Range(0, 3);
        var l = new Lot { C = c, R = rng, Tech = tech, S = Stuff.For(tech, rng), F = new Frame((int)(hints?.Front ?? Dir.North), 0, 0), Home = hints?.Home };
        c.ExitCells = new List<Int3>();
        c.X.PlainDoor = l.S.Door;
        c.X.Levels.Add(new LevelInfo { Storey = 0, WalkY = 0, Name = "ground" });
        string what = site.Parts[0].Arg("what");
        switch (what)
        {
            case "house": HouseLot(l, rng, hints, site.Parts[0].Arg("family", "") is { Length: > 0 } named ? named : null); break;
            case "terrace": HouseLot(l, rng, hints, "terrace"); break;
            case "homestead": Farm(l, 0); break;
            case "hillfarm": Farm(l, 1); break;
            case "bottomfarm": Farm(l, 2); break;
            case "toolshed": case "machineshed": case "leanto": ShedLot(l, what); break;
            case "coachinn": Inn(l, 2); break;
            case "cartstop": Inn(l, 1); break;
            case "fuelstop": Fuel(l, 16 + 2 * rng.Range(0, 3), 1, false); break;
            case "truckstop": Fuel(l, 22 + 2 * rng.Range(0, 2), 2, true); break;
            case "chargedepot": Fuel(l, 20 + 2 * rng.Range(0, 3), 2, true, depot: true); break;
            case "waystone": Waystone(l); break;
            case "cornershop": ShopLot(l, 12 + 2 * rng.Range(0, 4), false); break;
            case "shoprow": ShopLot(l, 20 + 2 * rng.Range(0, 4), false); break;
            case "shophall": ShopLot(l, 14 + 2 * rng.Range(0, 4), true); break;
            default: throw new ArgumentException($"site {site.Id}: no plain plan '{what}'");
        }
        if (l.Loot < 0) throw new InvalidOperationException($"site {site.Id}: no loot room");
        c.VaultModule = l.Loot;
    }

    /// <summary>A real house set down on a larger place's plan, its rooms and stair its own, walked to from <paramref name="yard"/>.</summary>
    public static void Farmhouse(Canvas c, Rng rng, TechLevel tech, Int3 at, Dir front, int yard)
    {
        var l = new Lot { C = c, R = rng, Tech = tech, S = Stuff.For(tech, rng), F = new Frame((int)front, at.X, at.Z) };
        int module = c.EntranceModule;
        Int3 cell = c.EntranceCell, gate = c.X.Gate;
        Dir facing = c.EntranceFacing;
        House(l, l.F, tech switch { TechLevel.Low => "cottage", TechLevel.High => "prefab", _ => "twostorey" });
        c.Link(yard, c.EntranceModule);
        c.EntranceModule = module;
        c.EntranceCell = cell;
        c.EntranceFacing = facing;
        c.X.Gate = gate;
    }

    static void Upper(Lot l, Hut h) => l.C.X.Levels.Add(new LevelInfo { Storey = 1, WalkY = h.Walk(1), Name = "upper" });

    // ---- farmsteads --------------------------------------------------------------------------

    /// <summary>A barn 22 m and more long and 13 m to the ridge, gable on to the yard, its big doors open.</summary>
    static Room Barn(Lot l, Frame f, int w, int d)
    {
        var s = l.S with
        {
            WallM = Material.Timber, Wall = l.Tech == TechLevel.High ? l.S.Wall : new Rgb(0x6c, 0x40, 0x32),
            RoofM = l.Tech == TechLevel.Low ? Material.Timber : Material.Rust, Roof = l.Tech == TechLevel.Low ? l.S.Roof : new Rgb(0x5a, 0x48, 0x40),
            BaseM = Material.Rock, Base = new Rgb(0x68, 0x64, 0x5c), Floor = new Rgb(0x5a, 0x4c, 0x3e),
        };
        var barn = new Hut(l, f, s) { Height = 5, Open = true };
        barn.Wings.Add(new Wing(0, 0, w - 1, d - 1, false));
        Room floor = barn.Add("storage", 0, 1, 1, w - 2, d - 2, ModuleKind.Hall);
        barn.Raise();
        barn.Gap(w / 2 - 2, 0, true, 0, 4, 4);
        barn.Modules();
        barn.Windows(0.6f);
        barn.Roof();
        barn.Headroom(w / 2 - 2, 0, Dir.North, 0, 4, exit: false);
        barn.GableVent(w / 2, 0);
        barn.Box();
        // Stalls down one side.
        var wood = new Rgb(0x54, 0x46, 0x38);
        for (int v = 4; v < d - 3; v += 4)
            for (int u = w - 5; u <= w - 2; u++) l.C.Blocks[f.At(u, 0, v)] = new Block(BlockType.Rail, Material.Timber, wood, Role.Decor);
        l.C.Link(l.C.EntranceModule, floor.Module);
        return floor;
    }

    static void Farm(Lot l, int layout)
    {
        Rng r = l.R;
        Frame f = l.F;
        // The house at the front of the yard, the barn across the back of it, the silo beside the barn.
        const int HouseU = 6, HouseV = 12;
        var (hw, _, door) = House(l, f.Shift(HouseU, HouseV), layout switch { 0 => "bungalow", 1 => "cottage", _ => "twostorey" });
        // How far back the house reaches, wings, eaves and all: the shed keeps clear of it.
        int back = l.C.Blocks.Keys.Where(p => p.Y >= 0).Select(p => f.Q switch { 0 => p.Z - f.Z, 1 => f.X - p.X, 2 => f.Z - p.Z, _ => p.X - f.X }).DefaultIfEmpty(HouseV).Max();
        for (int v = 0; v < HouseV - 4; v++)
            for (int k = 0; k < 2; k++) l.Plate(f, HouseU + door + k, v, l.S.Base.Scale(1.1f));
        l.C.X.Gate = f.At(HouseU + door, 0, -1);
        // A working farm's yard and fields at the size of one (a farm lot is about 60 m by 60 and more): the barn well across
        // the yard, the field out to the silo's far side and twenty-odd rows deep.
        int bu = HouseU + hw + 24 + 2 * r.Range(0, 3), bv = 6 + 2 * r.Range(0, 3), bw = 14 + 2 * r.Range(0, 2), bd = 20 + 2 * r.Range(0, 3);
        Barn(l, f.Shift(bu, bv), bw, bd);
        Silo(l, f.At(bu + bw + 7, 0, bv + 6), layout == 2 ? 16 : 12);
        // The field the place served, its rows gone to weeds, an old rail along it.
        int fieldV = bv + bd + 4, run = Math.Max(30, bu + bw + 12 - HouseU);
        var weed = new Rgb(0x6a, 0x72, 0x44);
        for (int k = 0; k < 24 + layout * 2; k += 2)
            for (int u = 0; u < run; u++)
            {
                Int3 p = f.At(HouseU + u, 0, fieldV + k);
                if ((Hash.Cell(p, 83) & 3) != 0) l.C.Blocks.TryAdd(p, new Block(BlockType.Plant, Material.Plant, weed, Role.Debris));
            }
        for (int u = 0; u < run; u++)
            if (Hash.Cell(new Int3(u / 2, 0, 0), 85) % 100 > 25) l.C.Blocks.TryAdd(f.At(HouseU + u, 0, fieldV - 2), new Block(BlockType.Rail, Material.Timber, l.S.Door, Role.Decor));
        if (layout == 0) return;
        // The shed far enough behind the house that its eaves never close the way between them overhead.
        var shed = new Hut(l, f.Shift(HouseU, Math.Max(HouseV + 22, back + 6)), l.S) { Open = true };
        shed.Wings.Add(new Wing(0, 0, 9, 7));
        Room s = shed.Add("workshop", 0, 1, 1, 8, 6);
        shed.Raise();
        shed.Gap(4, 0, true);
        shed.Modules();
        shed.Roof();
        shed.Headroom(4, 0, Dir.North, 0, exit: false);
        shed.Furnish();
        shed.Box();
        l.C.Link(l.C.EntranceModule, s.Module);
    }

    static void Silo(Lot l, Int3 at, int h)
    {
        const float R = 3.4f;
        var shell = new Rgb(0x8c, 0x88, 0x7c);
        Round.Lay(l.C, at.X + 0.5, at.Z + 0.5, at.Y, at.Y + h + 2, y => (y - at.Y < h ? R : R - (y - at.Y - h + 1) * 1.05, 0),
            y => new Block(BlockType.Cube, Material.Concrete, (y - at.Y) % 4 == 3 ? shell.Scale(0.85f) : shell, Role.Wall));
    }

    // ---- sheds -------------------------------------------------------------------------------

    static void ShedLot(Lot l, string kind)
    {
        Rng r = l.R;
        Frame f = l.F.Shift(6, 8);
        var s = l.S with
        {
            Wall = l.Tech == TechLevel.Low ? l.S.Wall : new Rgb(0x6a, 0x5e, 0x50), WallM = l.Tech == TechLevel.Low ? l.S.WallM : Material.Rust,
            Roof = new Rgb(0x56, 0x46, 0x3c), RoofM = l.Tech == TechLevel.Low ? Material.Timber : Material.Rust,
        };
        var h = new Hut(l, f, s) { Open = true };
        int w, d, mouth, wide;
        switch (kind)
        {
            case "toolshed":
                w = 8 + 2 * r.Range(0, 3); d = 6 + 2 * r.Range(0, 3); mouth = w / 2 / 2 * 2 - 2; wide = 4;
                h.Wings.Add(new Wing(0, 0, w - 1, d - 1));
                break;
            case "machineshed":
                w = 12 + 2 * r.Range(0, 3); d = 10 + 2 * r.Range(0, 3); mouth = w / 2 / 2 * 2 - 2; wide = 4; h.Height = 5;
                h.Wings.Add(new Wing(0, 0, w - 1, d - 1, false));
                break;
            default:
                w = 10 + 2 * r.Range(0, 3); d = 6 + 2 * r.Range(0, 2); mouth = 2; wide = 4;
                h.Wings.Add(new Wing(0, 0, w - 1, d - 1, true, RoofKind.Lean));
                break;
        }
        Room room = h.Add("workshop", 0, 1, 1, w - 2, d - 2, ModuleKind.Hall);
        h.Raise();
        // A shed's doors are a cart's or a machine's: full width, up to the wall's top course, never a low hatch.
        h.Gap(mouth, 0, true, 0, wide, h.Height);
        h.Modules();
        h.Windows(0.5f);
        h.Roof();
        h.Headroom(mouth, 0, Dir.North, 0, wide, exit: false);
        h.Furnish();
        h.Loot(room);
        h.Box();
        l.Approach(f, mouth + wide / 2, -3);
        l.C.Link(l.C.EntranceModule, room.Module);
        for (int v = -8; v < -1; v++)
            for (int k = 0; k < wide; k++) l.Plate(f, mouth + k, v, new Rgb(0x6a, 0x60, 0x54));
        l.C.X.Gate = f.At(mouth, 0, -9);
    }

    // ---- shops ------------------------------------------------------------------------------

    /// <summary>A shop: glass across the front, a door in it, shelves, a store room behind with the loot, a blank name board over it all.</summary>
    static void Shop(Lot l, Frame f, int w, int d, bool upper)
    {
        var h = new Hut(l, f) { Storeys = upper ? 2 : 1 };
        h.Wings.Add(new Wing(0, 0, w - 1, d - 1, true, RoofKind.Flat));
        int backV = d - 6;
        Room shop = h.Add("shop", 0, 1, 1, w - 2, backV - 1), store = h.Add("storage", 0, 1, backV + 1, w - 2, d - 2);
        Room? flat = upper ? h.Add("quarters", 1, 1, 1, w - 2, d - 2) : null;
        h.Raise();
        int door = (w / 2 - 1) / 2 * 2;
        for (int u = 2; u < w - 2; u += 2)
        {
            if (u == door) continue;
            for (int k = 0; k < 2; k++)
                for (int y = 1; y <= 2; y++) l.C.Set(f.At(u + k, y, 0), new Block(BlockType.Pane, Material.Glass, new Rgb(0x5a, 0x6c, 0x70), Role.Window));
        }
        h.Gap(door, 0, true);
        h.Gap(door, backV, true);
        if (upper)
        {
            int stair = h.Stair(w - 2, backV + 1 - 4 > 1 ? 2 : 1);
            h.Modules();
            l.C.Link(shop.Module, stair);
            l.C.Link(stair, flat!.Module);
            Upper(l, h);
        }
        else h.Modules();
        h.Windows(0.5f);
        h.FlatRoof();
        h.Headroom(door, 0, Dir.North, 0);
        for (int u = 2; u < w - 2; u++) l.C.Set(f.At(u, h.Ring(h.Storeys - 1) + 1, -1), new Block(BlockType.Cube, l.S.BaseM, l.S.Sign, Role.Wall));
        h.Furnish();
        h.Loot(store);
        h.Box();
        l.Approach(f, door, -3);
        h.FrontDoor(door, 0, shop);
        l.C.Link(l.C.EntranceModule, shop.Module);
    }

    static void ShopLot(Lot l, int width, bool upper)
    {
        Frame f = l.F.Shift(4, 8);
        Shop(l, f, width, 12 + 2 * l.R.Range(0, 3), upper);
        var pale = l.S.Base.Scale(1.1f);
        for (int u = 0; u < width + 8; u++)
            for (int v = 0; v < 6; v++) l.Plate(l.F, u, v, pale);
        l.C.X.Gate = l.F.At(width / 2 + 4, 0, -1);
    }

    // ---- stops on low-tech roads --------------------------------------------------------------

    /// <summary>
    /// A coaching inn, or a smaller horse and cart stop: a timber tap room on the road with rooms over
    /// it, an open stable, a well, a hitching rail and a cart that never left. A forecourt of packed dirt.
    /// </summary>
    static void Inn(Lot l, int storeys)
    {
        Rng r = l.R;
        Frame f = l.F.Shift(8, 14);
        int w = 20 + 2 * r.Range(0, 3), d = 12, tapW = 11, door = 6;
        var h = new Hut(l, f) { Storeys = storeys };
        h.Wings.Add(new Wing(0, 0, w - 1, d - 1));
        Room tap = h.Add("mess", 0, 1, 1, tapW, d - 2, ModuleKind.Hall);
        h.Add("farm-kitchen", 0, tapW + 2, 1, w - 2, 5);
        Room store = h.Add("storage", 0, tapW + 2, 7, w - 2, d - 2);
        Room? gallery = storeys > 1 ? h.Add("hall", 1, 1, 1, 4, d - 2, ModuleKind.Hall) : null;
        if (gallery != null)
        {
            h.Add("quarters", 1, 6, 1, w - 2, 5);
            h.Add("quarters", 1, 6, 7, w - 2, d - 2);
        }
        h.Raise();
        h.Gap(door, 0, true);
        h.Gap(tapW + 1, 2, false);
        h.Gap(tapW + 1, 8, false);
        if (gallery != null)
        {
            h.Gap(5, 2, false, 1);
            h.Gap(5, 8, false, 1);
            int stair = h.Stair(1, 2);
            h.Modules();
            l.C.Link(tap.Module, stair);
            l.C.Link(stair, gallery.Module);
            Upper(l, h);
        }
        else h.Modules();
        h.Windows();
        h.Roof();
        h.Headroom(door, 0, Dir.North, 0);
        h.Chimney(w - 1, d / 2 - 1, 0, 1);
        h.Furnish();
        h.Loot(store);
        h.Box();
        l.Approach(f, door, -3);
        h.FrontDoor(door, 0, tap);
        l.C.Link(l.C.EntranceModule, tap.Module);
        // The open stable beside it, a low roof over the stalls.
        var stable = new Hut(l, f.Shift(w + 8, 0)) { Open = true };
        int sw = 12, sd = 8;
        stable.Wings.Add(new Wing(0, 0, sw - 1, sd - 1, true, RoofKind.Lean));
        Room stalls = stable.Add("storage", 0, 1, 1, sw - 2, sd - 2, ModuleKind.Hall);
        stable.Raise();
        stable.Gap(2, 0, true, 0, 8, DoorSize.Tall);
        stable.Modules();
        stable.Roof();
        stable.Headroom(2, 0, Dir.North, 0, 8, exit: false);
        stable.Box();
        l.C.Link(l.C.EntranceModule, stalls.Module);
        var wood = new Rgb(0x54, 0x46, 0x38);
        for (int u = 3; u < sw - 2; u += 3)
            for (int v = 3; v <= sd - 2; v++) l.C.Blocks[f.At(w + 8 + u, 0, v)] = new Block(BlockType.Rail, Material.Timber, wood, Role.Decor);
        // The yard: a hitching rail along the road, a well with its little roof, a cart.
        for (int u = -4; u < w + 4; u++)
            if ((u < door - 3 || u > door + 4) && Hash.Cell(new Int3(u / 2, 0, 1), 71) % 100 > 20) l.C.Set(f.At(u, 0, -8), new Block(BlockType.Rail, Material.Timber, wood, Role.Decor));
        int wu = w + 2, wv = -6;
        for (int du = -1; du <= 1; du++)
            for (int dv = -1; dv <= 1; dv++)
                if (du != 0 || dv != 0) l.C.Set(f.At(wu + du, 0, wv + dv), new Block(BlockType.Cube, Material.Rock, l.S.Stack, Role.Wall));
        foreach (int du in new[] { -1, 1 })
            for (int y = 1; y <= 2; y++) l.C.Set(f.At(wu + du, y, wv), new Block(BlockType.Pillar, Material.Timber, wood, Role.Decor));
        for (int du = -1; du <= 1; du++) l.C.Set(f.At(wu + du, 3, wv), new Block(BlockType.Cube, Material.Timber, wood, Role.Roof));
        for (int k = 0; k < 3; k++) l.C.Blocks[f.At(w + 2 + k, 0, -10)] = new Block(BlockType.Bench, Material.Timber, wood, Role.Prop);
        l.C.Blocks[f.At(w + 1, 0, -10)] = new Block(BlockType.Barricade, Material.Timber, wood.Scale(0.8f), Role.Prop, f.Turn(Dir.East));
        // The inn's board on a post by the road.
        for (int y = 0; y < 6; y++) l.C.Set(f.At(2, y, -10), new Block(BlockType.Pillar, Material.Timber, wood, Role.Decor));
        for (int y = 6; y < 8; y++)
            for (int k = 0; k < 2; k++) l.C.Set(f.At(2 + k, y, -10), new Block(BlockType.Cube, Material.Timber, l.S.Sign, Role.Wall));
        for (int v = -13; v < -2; v++)
            for (int k = 0; k < 2; k++) l.Plate(f, door + k, v, new Rgb(0x6a, 0x5c, 0x4c));
        l.C.X.Gate = f.At(door, 0, -14);
    }

    // ---- a stop nobody here built ---------------------------------------------------------------

    /// <summary>
    /// The rare outlier: whatever the alien or the Precursor road kept instead of a fuel stop. A low dark
    /// chamber you can walk into, a ring of standing stones round it, a crystal on its roof.
    /// </summary>
    static void Waystone(Lot l)
    {
        const int Ring = 14;
        Frame f = l.F.Shift(16, 16);
        var dark = new Rgb(0x30, 0x2e, 0x38);
        var s = l.S with { WallM = Material.Basalt, Wall = dark, RoofM = Material.Basalt, Roof = dark.Scale(0.8f), BaseM = Material.Basalt, Base = dark.Scale(1.2f), Floor = dark.Scale(1.1f) };
        var h = new Hut(l, f, s);
        h.Wings.Add(new Wing(0, 0, 9, 9, true, RoofKind.Flat));
        Room room = h.Add("storage", 0, 1, 1, 8, 8, ModuleKind.Hall);
        h.Raise();
        h.Gap(4, 0, true);
        h.Modules();
        h.FlatRoof(1, 1);
        h.Headroom(4, 0, Dir.North, 0);
        h.Loot(room);
        h.Box();
        l.Approach(f, 4, -3);
        h.FrontDoor(4, 0, room);
        l.C.Set(f.At(4, 4, 4), new Block(BlockType.Crystal, Material.Crystal, new Rgb(0x8a, 0x60, 0xd8), Role.Decor));
        for (int k = 0; k < 8; k++)
        {
            float a = (k + 0.5f) * MathF.Tau / 8;
            int cu = 4 + (int)MathF.Round(MathF.Cos(a) * Ring), cv = 4 + (int)MathF.Round(MathF.Sin(a) * Ring), tall = 8 + (k * 5 % 5);
            for (int y = 0; y < tall; y++)
                for (int du = 0; du <= (y < tall - 3 ? 1 : 0); du++)
                    for (int dv = 0; dv <= (y < tall - 3 ? 1 : 0); dv++)
                    {
                        Int3 p = f.At(cu + du, y, cv + dv);
                        bool glyph = y % 4 == 3;
                        l.C.Set(p, new Block(BlockType.Cube, glyph ? Material.Glyph : Material.Basalt, glyph ? new Rgb(0x8a, 0x60, 0xd8) : dark, Role.Decor));
                        l.C.X.Lasting.Add(p);
                    }
        }
        l.C.X.Gate = f.At(4, 0, -17);
    }

    // ---- fuel stations ------------------------------------------------------------------------
    /// <summary>
    /// A fuel station at full size: a forecourt (the pad's own concrete) open to the road, a canopy 5 m
    /// clear on posts over the pumps, a shop behind it you can walk into, the slab over the tanks with
    /// its fillers and vent pipes, and a sign on a pole you see coming.
    /// </summary>
    static void Fuel(Lot l, int canopyW, int islands, bool diner, bool depot = false)
    {
        Frame f = l.F;
        var steel = depot ? new Rgb(0xc4, 0xcc, 0xd0) : new Rgb(0x8e, 0x8e, 0x88);
        var band = depot ? new Rgb(0x2e, 0x6a, 0x8a) : l.S.Sign;
        int canopyD = 8 + 4 * islands, cu = 12, cv = 10;
        int Clear = depot ? 7 : 5; // a grav hauler rides high

        int Lane(int k) => cv + 4 + k * 4; // an island every 4 m of the canopy's depth
        // Six posts, or nine: a grid under the deck.
        for (int i = 0; i <= 2; i++)
            foreach (int v in Enumerable.Range(0, islands).Select(Lane))
            {
                int u = cu + 2 + i * (canopyW - 6) / 2;
                for (int y = 0; y < Clear; y++) l.C.Set(f.At(u, y, v), new Block(BlockType.Rib, Material.Steel, steel, Role.Wall));
            }
        for (int v = cv; v < cv + canopyD; v++)
            for (int u = cu; u < cu + canopyW; u++)
            {
                bool edge = v == cv || v == cv + canopyD - 1 || u == cu || u == cu + canopyW - 1;
                l.C.Set(f.At(u, Clear, v), new Block(BlockType.Cube, Material.Alloy, steel.Scale(0.8f), Role.Roof));
                if (edge) l.C.Set(f.At(u, Clear + 1, v), new Block(BlockType.Cube, Material.Alloy, band, Role.Roof));
            }
        Int3 a = f.At(cu, 0, cv), b = f.At(cu + canopyW - 1, Clear + 2, cv + canopyD - 1);
        l.C.X.Plain.Add(new PlainBox(Int3.Min(a, b), Int3.Max(a, b), Clear, f.Turn(Dir.North)));
        // Pumps on their islands, between the posts.
        var pump = depot ? new Rgb(0x34, 0x50, 0x58) : new Rgb(0x9a, 0x3c, 0x30);
        foreach (int v in Enumerable.Range(0, islands).Select(Lane))
            for (int u = cu + 3; u < cu + canopyW - 3; u++)
            {
                if (!depot) l.Plate(f, u, v, new Rgb(0x96, 0x94, 0x8c));
                if ((u - cu - 5) % 5 == 0) l.C.Blocks[f.At(u, 0, v)] = new Block(BlockType.Pump, Material.Steel, pump, Role.Prop, f.Turn(Dir.North));
            }
        // A depot's hover pads: painted rings where a grav vehicle set down to take a charge.
        if (depot)
            foreach (int pu in new[] { cu + canopyW / 4, cu + canopyW * 3 / 4 })
                for (int dv = -5; dv <= 5; dv++)
                    for (int du = -5; du <= 5; du++)
                    {
                        float dist = MathF.Sqrt(du * du + dv * dv);
                        if (dist > 3.6f && dist <= 4.8f) l.Plate(f, pu + du, cv + canopyD / 2 + dv, new Rgb(0x8a, 0xb4, 0xc0));
                    }
        // The shop behind the forecourt.
        int shopW = diner ? 20 : 12 + 2 * l.R.Range(0, 2), shopD = diner ? 16 : 12 + 2 * l.R.Range(0, 2), su = cu + (canopyW - shopW) / 2 / 2 * 2, sv = cv + canopyD + 8;
        Shop(l, f.Shift(su, sv), shopW, shopD, false);
        // The slab over the underground tanks: fillers in a row, two vent pipes at its corner.
        int tu = su + shopW + 4, tv = cv + canopyD + 2, tw = 10, td = 6;
        var slab = new Rgb(0xa2, 0xa0, 0x98);
        for (int v = tv; v < tv + td; v++)
            for (int u = tu; u < tu + tw; u++)
                l.Plate(f, u, v, (v == tv + 2 || v == tv + 3) && (u - tu) % 3 == 1 ? new Rgb(0x3a, 0x3a, 0x3a) : slab);
        foreach (int k in new[] { 0, 2 })
            for (int y = 0; y < 4; y++) l.C.Set(f.At(tu + tw - 1 - k, y, tv + td), new Block(BlockType.Pillar, Material.Steel, steel.Scale(0.7f), Role.Wall));
        // A depot keeps its fuel cells above ground, in racks on the slab.
        if (depot)
            for (int u = tu + 1; u < tu + tw - 1; u += 2)
                for (int y = 0; y < 2; y++) l.C.Blocks[f.At(u, y, tv + 1)] = new Block(BlockType.Tank, Material.Steel, new Rgb(0x8c, 0xa4, 0xac), Role.Prop);
        // The sign on its pole, at the road end of the lot, where you see it coming.
        for (int y = 0; y < 10; y++) l.C.Set(f.At(4, y, 4), new Block(BlockType.Rib, Material.Steel, steel, Role.Wall));
        for (int y = 10; y < 14; y++)
            for (int k = -2; k <= 3; k++) l.C.Set(f.At(4 + k, y, 4), new Block(BlockType.Cube, Material.Alloy, y >= 12 ? band : steel.Scale(1.1f), Role.Wall));
        l.C.X.Gate = f.At(cu + canopyW / 2, 0, -1);
    }
}
