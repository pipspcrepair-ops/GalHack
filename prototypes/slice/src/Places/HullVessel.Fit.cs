using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Structures;
using PoiGenerator.Gen;
using Block = GalHack.Structures.Block;
using BlockType = GalHack.Structures.BlockType;
using Dirs = GalHack.Structures.Dirs;
using Building = GalHack.Structures.Building;
using Int3 = GalHack.Structures.Int3;
using Plan = PoiGenerator.Gen.CapitalHull.HullPlan;
using Dir = GalHack.Structures.Dir;

namespace Slice.Places;

/// <summary>The engines, the thrusters on every axis, every room's fittings and lights.</summary>
public sealed partial class HullVessel
{
    /// <summary>The spaces this piece holds any of.</summary>
    IEnumerable<Space> Live => Out.Spaces.Where(s => s.Floor.Count > 0);

    /// <summary>The catalogue's part for a function that fits a box (turned as given), the most capable first.</summary>
    BlockType Best(string function, Int3 room, int rot, Func<BlockType, float> score)
    {
        BlockType best = null;
        foreach (BlockType t in Cat.Types)
        {
            if (t == null || t.Function != function) continue;
            Int3 size = Footprint.WorldSize(t, rot);
            if (size.X > room.X || size.Y > room.Y || size.Z > room.Z) continue;
            if (best == null || score(t) > score(best)) best = t;
        }
        return best;
    }

    static int Turn(Dir up, Dir forward) => Orientations.TryParse(Designer.Rot(up, forward), out int r) ? r : Orientations.Identity;

    /// <summary>Is every cell of a part's footprint free (and inside the hull's open space)?</summary>
    bool Fits(BlockType t, Int3 corner, int rot, Func<int, int, int, bool> ok = null)
    {
        Int3 anchor = Footprint.AnchorFor(t, rot, corner);
        foreach (Int3 c in Footprint.Cells(anchor, t, rot))
        {
            int v = c.Z + Pc.V0;
            if (!InPiece(v) || (ok == null ? S.Has(c) : !ok(c.X, c.Y, v))) return false;
        }
        return true;
    }

    /// <summary>Sets a part by the low corner of its footprint (hull frame) and notes the floor it stands on in the space.</summary>
    Int3 Device(BlockType t, int u, int y, int v, int rot, Space space = null, ushort paint = 0)
    {
        Int3 anchor = D.Put(t.Id, At(u, y, v), Orientations.Name(rot), paint);
        if (space != null)
            foreach (Int3 c in Footprint.Cells(anchor, t, rot)) space.Taken.Add((c.X, c.Z + Pc.V0));
        return anchor;
    }

    // ---- the engines ----------------------------------------------------------------------------------

    /// <summary>
    /// The drives: each bell's throat packed with the biggest main thrusters that fit wholly inside it, exhausts aft and
    /// flush with its face, the gaps between them filled with smaller ones. The lift engines go in last, once the hull's
    /// weight is known (<see cref="Lift"/>).
    /// </summary>
    void Engines()
    {
        int rot = Turn(Dir.PosY, Dir.NegZ);
        var mains = new[] { new Int3(3, 3, 6), new Int3(2, 2, 4), new Int3(1, 1, 2) }.Select(box => Best("thruster", box, rot, t => t.Thrust)).Where(t => t != null).Distinct().ToList();
        var packed = new HashSet<(int, int, int)>();
        foreach (var (face, radius) in H.Form.Drives)
        {
            // The throat's face: the last solid plane at the bell's middle before the hollow behind it.
            int cu = (int)MathF.Round(face.X), cy = (int)MathF.Round(face.Y), vf = (int)MathF.Round(face.Z);
            bool Face(int u, int y) => Mask(u, y, vf) == 255 && Mask(u, y, vf + 1) != 255;
            for (int k = 0; k < 3 && !Face(cu, cy); k++) vf += Mask(cu, cy, vf) == 255 ? 1 : -1;
            if (!Face(cu, cy)) continue;
            int r = (int)MathF.Ceiling(radius);
            foreach (BlockType t in mains)
            {
                Int3 size = Footprint.WorldSize(t, rot);
                int v = vf - size.Z + 1;
                if (!InPiece(v)) continue;
                for (int y = cy - r; y <= cy + r; y++)
                    for (int u = cu - r; u <= cu + r; u++)
                    {
                        bool fits = true;
                        for (int dy = 0; dy < size.Y && fits; dy++)
                            for (int du = 0; du < size.X && fits; du++)
                            {
                                float a = u + du - face.X, b = y + dy - face.Y;
                                fits = a * a + b * b <= radius * radius && Face(u + du, y + dy) && !packed.Contains((u + du, y + dy, vf));
                                // (Its length into the housing: never into a room behind it.)
                                for (int dz = 0; dz < size.Z && fits; dz++) fits = Bulk(At(u + du, y + dy, v + dz));
                            }
                        if (!fits) continue;
                        Device(t, u, y, v, rot, null, Paint["dark"]);
                        for (int dy = 0; dy < size.Y; dy++)
                            for (int du = 0; du < size.X; du++) packed.Add((u + du, y + dy, vf));
                    }
            }
        }
    }

    /// <summary>
    /// Thrusters of a type set flush in the hull's faces that look the other way from their push (an exhaust out of
    /// every one), in the shell and the bulk behind it, never in a room but a hold, hangar or engine room (where they
    /// stand in the floor); or, <paramref name="outside"/>, bolted on outside those faces. Spread along the hull, until
    /// <paramref name="most"/> are set. How many it set.
    /// </summary>
    int Embed(Dir push, BlockType t, int most, bool outside = false)
    {
        if (t == null) return 0;
        int rot = Turn(push.Axis() == 1 ? Dir.PosZ : Dir.PosY, push);
        Int3 size = Footprint.WorldSize(t, rot), e = push.Opposite().Vec();
        int axis = push.Axis(), depth = axis == 0 ? size.X : axis == 1 ? size.Y : size.Z;
        // (Outside: nothing built there, and no more than the form's skin passing through it.)
        bool Air(Int3 q) => !S.Has(q) && Mask(q.X, q.Y, q.Z + Pc.V0) != 255;
        var faces = S.Blocks().Where(b => !b.block.IsFiller && S.TypeOf(b.block) == Plating && Air(b.pos + e)).Select(b => b.pos).OrderBy(p => p.Z).ThenBy(p => Math.Abs(p.X)).ToList();
        var used = new HashSet<Int3>();
        int placed = 0;
        foreach (Int3 f in faces)
        {
            if (placed >= most) break;
            // Its exhaust layer on the face (or a thruster's depth out past it), the rest behind it (or back against the face).
            Int3 x = outside ? f + e * depth : f, corner = Int3.Dot(e, new Int3(1, 1, 1)) > 0 ? x - e * (depth - 1) : x;
            bool ok = true;
            var cells = new List<Int3>();
            for (int a = 0; a < size.X && ok; a++)
                for (int b = 0; b < size.Y && ok; b++)
                    for (int c = 0; c < size.Z && ok; c++)
                    {
                        Int3 q = corner + new Int3(a, b, c);
                        int k = Int3.Dot(x - q, e);
                        ok = InPiece(q.Z + Pc.V0) && !used.Contains(q) && (outside ? Air(q) : Bulk(q)) && (k != 0 || Air(q + e))
                             && (!outside || k != depth - 1 || S.TryGet(q - e, out Block back) && S.TypeOf(back) == Plating);
                        cells.Add(q);
                    }
            if (!ok) continue;
            foreach (Int3 q in cells)
                for (int du = -1; du <= 1; du++)
                    for (int dv = -1; dv <= 1; dv++) used.Add(q + new Int3(du, 0, dv));
            foreach (Int3 q in cells)
                if (!outside && AirOf(q) is { } s) s.Taken.Add((q.X, q.Z + Pc.V0));
            D.Thruster(t.Id, corner, push);
            placed++;
        }
        return placed;
    }

    /// <summary>The space whose air a cell (structure frame) is, or null.</summary>
    Space AirOf(Int3 q)
    {
        for (int d = 0; d < Levels; d++)
            if (ZoneAt(d, q.X, q.Z + Pc.V0) is int z && z >= 0 && q.Y >= Walk(d) && q.Y < Walk(d) + Out.Spaces[z].Height) return Out.Spaces[z];
        return null;
    }

    /// <summary>A cell a machine may take: plating, the form's own void, or the floor of a hold, hangar or engine room (never a room people use, a corridor or a doorway).</summary>
    bool Bulk(Int3 q)
    {
        if (S.TryGet(q, out Block b)) return !b.IsFiller && S.TypeOf(b) == Plating;
        if (Mask(q.X, q.Y, q.Z + Pc.V0) == 0) return false;
        return AirOf(q) is not { } s || s.Purpose is "hold" or "ore-hold" or "hangar" or "engine-room" or "refinery";
    }

    /// <summary>The biggest thrusters of each class that fit a box (its width across, its depth along the push) when turned to push that way, largest first.</summary>
    List<BlockType> Thrusters(Dir push, params (int Wide, int Deep)[] boxes)
    {
        int rot = Turn(push.Axis() == 1 ? Dir.PosZ : Dir.PosY, push), axis = push.Axis();
        return boxes.Select(b => Best("thruster", axis == 0 ? new Int3(b.Deep, b.Wide, b.Wide) : axis == 1 ? new Int3(b.Wide, b.Deep, b.Wide) : new Int3(b.Wide, b.Wide, b.Deep), rot, t => t.Thrust))
                    .Where(t => t != null).Distinct().ToList();
    }

    double ThrustTo(Dir push) => S.Blocks().Where(b => !b.block.IsFiller && S.TypeOf(b.block).IsThruster && Orientations.ToWorld(b.block.Rot, Dir.NegZ) == push).Sum(b => (double)S.TypeOf(b.block).Thrust);

    /// <summary>
    /// Lift: thrusters in the belly pushing up, until their push is half again the hull's weight at a heavy world's
    /// gravity (what is still to come, the cargo, the generators, the containers, allowed for); where its belly has no
    /// room for enough of them, more bolted on under it.
    /// </summary>
    void Lift()
    {
        const double g = 9.81 * 1.3;
        double need = StructureStats.Compute(S).Mass * 1.3 * g * 1.5, up = ThrustTo(Dir.PosY);
        var kinds = Thrusters(Dir.PosY, (3, 6), (2, 4), (1, 2));
        foreach (bool outside in new[] { false, true })
            foreach (BlockType t in kinds)
                if (up < need)
                {
                    int n = Embed(Dir.PosY, t, (int)Math.Ceiling((need - up) / t.Thrust), outside);
                    up += t.Thrust * n;
                    if (n > 0) Out.Thrust += $"{n} {t.Id} {(outside ? "under" : "in")} the belly, ";
                }
        Out.Thrust += $"lift {up / 1000:F0} of {need / 1000:F0} MN; ";
    }

    /// <summary>Thrusters on every other axis (DESIGN 5: thrusters on every axis): flush in the flanks, the roof and the faces looking forward, a few of each spread along the hull.</summary>
    void Manoeuvre()
    {
        int n = Math.Clamp(H.L / 30, 2, 6);
        // (Forward too, and lift, wherever this piece's drives or belly gave it none: a piece of a broken hull flies alone.)
        foreach (Dir push in Dirs.All.Where(d => d is Dir.PosX or Dir.NegX or Dir.NegY or Dir.PosZ || ThrustTo(d) <= 0))
        {
            int set = 0;
            foreach (bool outside in new[] { false, true })
                foreach (BlockType t in Thrusters(push, (2, 4), (1, 2)))
                    if (set < n) set += Embed(push, t, n - set, outside);
            Out.Thrust += $"{push} {set}; ";
        }
    }

    /// <summary>Generators enough for everything that draws power: in the reactor first (its largest), then the generator rooms.</summary>
    void Power()
    {
        var all = S.Blocks().Where(b => !b.block.IsFiller).Select(b => S.TypeOf(b.block).Power).ToList();
        double draw = all.Sum(p => -Math.Min(0f, p)), made = all.Sum(p => Math.Max(0f, p));
        // The reactor and the generator rooms; on a hull with neither in this piece, the engine room, the core room or any room.
        static int Rank(Space s) => s.Purpose switch { "reactor" => 0, "generator-room" => 1, "engine-room" => 2, "core-room" => 3, _ => 4 };
        foreach (Space room in Live.Where(s => !s.Corridor).OrderBy(Rank).ToList())
        {
            if (made >= draw * 1.2 + 4 && Rank(room) > 1) break;
            bool reactor = room.Purpose == "reactor";
            int rot = Turn(Dir.PosY, Dir.NegZ);
            BlockType gen = Best("generator", reactor ? new Int3(6, 2, 2) : new Int3(3, 1, 1), rot, t => t.Power);
            if (gen == null) continue;
            Int3 size = Footprint.WorldSize(gen, rot);
            if (reactor)
            {
                // The reactor: a block of generators stacked to the ceiling in the room's middle, a walk round it.
                int u0 = (room.U0 + room.U1) / 2 - size.X / 2, v0 = (room.V0 + room.V1) / 2 - size.Z;
                for (int stack = 0; stack < 2; stack++)
                    for (int y = room.Y; y + size.Y <= room.Y + 4; y += size.Y)
                    {
                        int v = v0 + stack * size.Z;
                        if (Fits(gen, At(u0, y, v), rot, (x, yy, vv) => !S.Has(new Int3(x, yy, vv - Pc.V0)) && room.Floor.Contains((x, vv)))) { Device(gen, u0, y, v, rot, room, Paint["machine"]); made += gen.Power; }
                    }
                continue;
            }
            foreach (var (u, v) in WallCells(room, 2))
            {
                if (made >= draw * 1.2 + 4) break;
                if (Fits(gen, At(u, room.Y, v), rot, (x, yy, vv) => !S.Has(new Int3(x, yy, vv - Pc.V0)) && room.Floor.Contains((x, vv)) && !room.Taken.Contains((x, vv))))
                { Device(gen, u, room.Y, v, rot, room, Paint["machine"]); made += gen.Power; }
            }
        }
        // (Whatever the rooms leave short, set into the bulk.)
        if (Best("generator", new Int3(2, 2, 2), 0, t => t.Power) is { } spare)
            for (int i = 0; i < 6 && made < draw * 1.2 + 4; i++)
                if (Stow(spare)) made += spare.Power;
                else break;
    }

    // ---- fittings -------------------------------------------------------------------------------------

    /// <summary>A room's floor cells against a wall (not in a doorway's way), every <paramref name="every"/>th along it.</summary>
    IEnumerable<(int U, int V)> WallCells(Space s, int every)
    {
        var near = new HashSet<(int, int)>(s.Doors.SelectMany(d => Around(d.Inside, 2)));
        int n = 0;
        foreach (var c in s.Floor.OrderBy(c => c.V).ThenBy(c => c.U))
        {
            if (near.Contains(c) || s.Taken.Contains(c)) continue;
            bool wall = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }.Any(o => !s.Floor.Contains((c.U + o.Item1, c.V + o.Item2)));
            if (wall && n++ % every == 0) yield return c;
        }
    }

    static IEnumerable<(int, int)> Around((int U, int V) c, int r)
    {
        for (int dv = -r; dv <= r; dv++)
            for (int du = -r; du <= r; du++) yield return (c.U + du, c.V + dv);
    }

    /// <summary>
    /// What each room is fitted with, as its purpose has it: the lift and manoeuvring thrusters first, cargo stacked in
    /// the holds with aisles between, the machines of the engine room (gyros, the warp drive), the workshop's fabricator
    /// and deconstructor, the bridge's command chairs, cargo containers in the stores, the holds and wherever a crew keeps
    /// things, turrets where the form mounts them, and the core room's empty seat. The machines and containers come to
    /// about 2.7 per 100 m² of floor, as Empyrion's wrecks have them.
    /// </summary>
    void Fittings()
    {
        // The lift engines first, where the holds' cargo would stand: what is still to come (the cargo, the generators, the
        // containers) is allowed for in their sum.
        Lift();
        Manoeuvre();
        // The seat for a core first (the room's own middle, away from its doors: one cell of the plan, so one piece has it).
        if (_vault >= 0)
        {
            Space core = Out.Spaces[_vault];
            ShipDecks.Room whole = I.Rooms[I.CoreRoom];
            float cu = whole.Uc, cv = whole.Vc;
            var seat = whole.Floor.Where(c => !whole.Stair.Contains(c) && !whole.Doors.Any(d => Math.Abs(d.Inside.U - c.U) + Math.Abs(d.Inside.V - c.V) < 2))
                                  .OrderBy(c => MathF.Abs(c.U - cu) + MathF.Abs(c.V - cv)).ThenBy(c => c.U).ThenBy(c => c.V).DefaultIfEmpty(whole.Floor.First()).First();
            if (InPiece(seat.V))
            {
                Out.CoreSlot = At(seat.U, core.Y, seat.V);
                core.Taken.Add(seat);
            }
            // An engineer's chair at its wall: the piece the core claims can be flown from it whichever piece the bridge came down in.
            BlockType chair = Cat.Type("command-chair");
            int rot = Turn(Dir.PosY, Dir.NegZ);
            // (Facing any way the room allows; failing that, in the nearest room that takes one: the piece must be flyable from somewhere.)
            bool Seat(Space room)
            {
                foreach (int turn in new[] { rot, Turn(Dir.PosY, Dir.PosX), Turn(Dir.PosY, Dir.NegX), Turn(Dir.PosY, Dir.PosZ) })
                    foreach (var (u, v) in WallCells(room, 1).Concat(room.Floor))
                        if (Fits(chair, At(u, room.Y, v), turn, Free(room)) && !room.Doors.Any(d => d.Inside == (u, v)))
                        {
                            Device(chair, u, room.Y, v, turn, room);
                            return true;
                        }
                return false;
            }
            if (!Seat(core) && Out.CoreSlot != null)
                foreach (Space room in Live.Where(x => !x.Corridor && x != core).OrderBy(x => Math.Abs(x.Deck - core.Deck) * 10 + Math.Abs(x.V0 - core.V0)))
                    if (Seat(room)) break;
        }
        foreach (Space s in Live.Where(s => s.Purpose is "hold" or "ore-hold")) Rows(s, 3, 6, cargo: true);
        foreach (Space s in Live.Where(s => s.Purpose is "stores" or "armoury" or "magazine")) Rows(s, 1, 6, cargo: false);
        if (Best("generator", new Int3(1, 1, 3), Turn(Dir.PosY, Dir.PosX), t => t.Power) is { } bank)
            foreach (Space s in Live.Where(s => s.Purpose == "generator-room")) Rows(s, 1, 9, cargo: false, bank);
        foreach (Space s in Live.Where(s => s.Purpose == "engine-room")) EngineRoom(s);
        foreach (Space s in Live.Where(s => s.Purpose == "bridge")) Bridge(s);
        foreach (Space s in Live.Where(s => s.Purpose == "workshop"))
            foreach (string id in new[] { "fabricator-t2", "decon" })
                if (Cat.TryGetType(id, out BlockType t))
                    foreach (var (u, v) in WallCells(s, 3))
                        if (Fits(t, At(u, s.Y, v), 0, Free(s)))
                        {
                            Device(t, u, s.Y, v, 0, s, Paint["machine"]);
                            break;
                        }
        Turrets();
        Power();
        Gyros();
        Containers();
    }

    /// <summary>A room's cells a part may stand in: free, its own, not taken.</summary>
    Func<int, int, int, bool> Free(Space s) => (x, y, vv) => !S.Has(new Int3(x, y, vv - Pc.V0)) && s.Floor.Contains((x, vv)) && !s.Taken.Contains((x, vv));

    /// <summary>
    /// Gyros wherever this piece has none yet (its engine room is in another): two by the core room's, the reactor's or any
    /// room's walls; and the piece with the seat for a core its warp drive, if its engine room had no room for one.
    /// </summary>
    void Gyros()
    {
        if (_vault >= 0 && !S.Blocks().Any(b => S.TypeOf(b.block).Function == "warp-drive") && Cat.TryGetType("warp-drive", out BlockType warp))
        {
            bool placed = false;
            foreach (Space s in Live.Where(s => !s.Corridor).OrderBy(s => s.Purpose switch { "engine-room" => 0, "reactor" => 1, "core-room" => 2, _ => 3 }))
                foreach (var (u, v) in s.Floor)
                    if (!placed && !s.Doors.Any(d => Math.Abs(d.Inside.U - u) + Math.Abs(d.Inside.V - v) < 2) && Fits(warp, At(u, s.Y, v), 0, Free(s)))
                    {
                        Device(warp, u, s.Y, v, 0, s, Paint["machine"]);
                        placed = true;
                    }
            if (!placed) Stow(warp);
        }
        if (S.Blocks().Any(b => S.TypeOf(b.block).Function == "gyro") || Best("gyro", new Int3(2, 2, 2), 0, t => t.Torque) is not { } gyro) return;
        int n = 0;
        foreach (Space s in Live.Where(s => !s.Corridor).OrderBy(s => s.Purpose switch { "core-room" => 0, "reactor" => 1, _ => 2 }))
            foreach (var (u, v) in WallCells(s, 3))
                if (n < 2 && Fits(gyro, At(u, s.Y, v), 0, Free(s)))
                {
                    Device(gyro, u, s.Y, v, 0, s, Paint["dark"]);
                    n++;
                }
    }

    /// <summary>
    /// A machine set into the hull's own bulk where no room has space for it: its footprint all the form's void or
    /// plating, never a room's air, as near the seat for a core as it goes. Whether it went in.
    /// </summary>
    bool Stow(BlockType t, ushort paint = 0)
    {
        Int3 near = Out.CoreSlot ?? new Int3(0, _ny / 2, (VEnd - Pc.V0) / 2);
        bool Bare(int x, int y, int v) => Bulk(new Int3(x, y, v - Pc.V0)) && AirOf(new Int3(x, y, v - Pc.V0)) == null && Interior(x, y, v);
        foreach (Int3 c in S.Blocks().Where(b => S.TypeOf(b.block) == Plating && Interior(b.pos.X, b.pos.Y, b.pos.Z + Pc.V0)).Select(b => b.pos)
                             .OrderBy(p => Math.Abs(p.X - near.X) + Math.Abs(p.Y - near.Y) + Math.Abs(p.Z - near.Z)).Take(4000))
            if (Fits(t, c, 0, Bare))
            {
                D.Put(t.Id, c, Orientations.Name(0), paint == 0 ? Paint["machine"] : paint);
                return true;
            }
        return false;
    }

    /// <summary>
    /// What a room stores stood in rows along it, 2 m aisles between them, along the walls and at the doors: a hold's
    /// cargo three wide, 6 m long and two or three high in a hall (one under a 4 m deck), a store's or an armoury's
    /// shelving one wide. A row too narrow for the space left by the wall is made narrower, never left a wide gap.
    /// </summary>
    void Rows(Space s, int width, int length, bool cargo, BlockType machine = null)
    {
        string[] colours = { "orange", "teal", "hazard", "blue", "red", "hull" };
        // Rows run down the room's long side.
        bool alongV = s.V1 - s.V0 >= s.U1 - s.U0;
        int a0 = alongV ? s.V0 : s.U0, a1 = alongV ? s.V1 : s.U1, b0 = alongV ? s.U0 : s.V0, b1 = alongV ? s.U1 : s.V1;
        (int U, int V) Cell(int a, int b) => alongV ? (b, a) : (a, b);
        // A row is as long as the room leaves with a 2 m aisle at either end.
        length = Math.Min(length, a1 - a0 - 3);
        if (length < 3) return;
        int along = alongV ? Turn(Dir.PosY, Dir.PosX) : Turn(Dir.PosY, Dir.PosZ);
        var doorways = new HashSet<(int, int)>(s.Doors.SelectMany(d => Around(d.Inside, 2)));
        for (int b = b0 + 2; b <= b1 - 2; b += width + 2)
        {
            int w = Math.Min(width, b1 - 2 - b + 1);
            for (int a = a0 + 2; a + length - 1 <= a1 - 2; a += length + 2)
            {
                var cells = new List<(int U, int V)>();
                for (int da = 0; da < length; da++)
                    for (int db = 0; db < w; db++) cells.Add(Cell(a + da, b + db));
                if (!cells.All(c => s.Floor.Contains(c) && !s.Taken.Contains(c) && !doorways.Contains(c))) continue;
                if (machine != null)
                {
                    // Machines in the row, end to end along it, two high.
                    Int3 size = Footprint.WorldSize(machine, along);
                    int step = alongV ? size.Z : size.X;
                    if ((alongV ? size.X : size.Z) > w) continue;
                    for (int da = 0; da + step <= length; da += step)
                        for (int y = s.Y; y + size.Y <= s.Y + 2; y += size.Y)
                        {
                            var (u, v) = Cell(a + da, b);
                            Device(machine, u, y, v, along, s, Paint["machine"]);
                        }
                    s.Taken.UnionWith(cells);
                    continue;
                }
                int high = s.Hall ? Rng.Next(2, 4) * 2 + (s.Purpose == "ore-hold" ? -2 : 0) : 3;
                ushort paint = cargo ? Paint[colours[Rng.Next(colours.Length)]] : Paint["machine"];
                foreach (var (u, v) in cells)
                {
                    s.Taken.Add((u, v));
                    bool end = (alongV ? v : u) == a || (alongV ? v : u) == a + length - 1;
                    for (int y = s.Y; y < s.Y + high; y++) Solid(u, y, v, end || y == s.Y + high - 1 ? Paint["trim"] : paint);
                }
            }
        }
    }

    /// <summary>The engine room: the warp drive on its centre line aft, gyros in a row down each side.</summary>
    void EngineRoom(Space s)
    {
        if (Cat.TryGetType("warp-drive", out BlockType warp))
        {
            // On the room's middle line, as far aft as it stands clear.
            Int3 size = Footprint.WorldSize(warp, 0);
            int u = (s.U0 + s.U1) / 2 - size.X / 2;
            for (int v = s.V1 - size.Z - 1; v >= s.V0 + 1; v--)
                if (Fits(warp, At(u, s.Y, v), 0, Free(s)))
                {
                    Device(warp, u, s.Y, v, 0, s, Paint["machine"]);
                    break;
                }
        }
        BlockType gyro = Best("gyro", new Int3(2, 2, 2), 0, t => t.Torque);
        if (gyro == null) return;
        int n = 0;
        foreach (var (u, v) in WallCells(s, 3))
        {
            if (n >= Math.Max(4, H.Decks * 2)) break;
            if (Fits(gyro, At(u, s.Y, v), 0, (x, y, vv) => !S.Has(new Int3(x, y, vv - Pc.V0)) && s.Floor.Contains((x, vv)) && !s.Taken.Contains((x, vv)))) { Device(gyro, u, s.Y, v, 0, s, Paint["dark"]); n++; }
        }
    }

    /// <summary>
    /// The bridge: command chairs facing forward (every seat aboard is a command chair, DESIGN 12: no cockpit component),
    /// the helm's on the room's middle line at its forward wall, the rest either side of it and back row by row, the
    /// ways in kept clear.
    /// </summary>
    void Bridge(Space s)
    {
        int rot = Turn(Dir.PosY, Dir.NegZ);
        var way = new HashSet<(int, int)>(s.Doors.SelectMany(d => Around(d.Inside, 1)));
        bool Clear(int x, int y, int vv) => Free(s)(x, y, vv) && !way.Contains((x, vv));
        BlockType chair = Cat.Type("command-chair");
        int centre = (s.U0 + s.U1) / 2, chairs = 0, most = Math.Clamp(s.Floor.Count / 12, 2, 5);
        var columns = Enumerable.Range(s.U0, s.U1 - s.U0 + 1).OrderBy(u => Math.Abs(u - centre)).ToList();
        // The helm's faces the prow (a vessel's forward is its pilot's seat's); the rest the prow or a flank where the room is narrow.
        foreach (int turn in new[] { rot, Turn(Dir.PosY, Dir.PosX), Turn(Dir.PosY, Dir.NegX) })
            for (int v = s.V0 + 1; v <= s.V1 && chairs < most && (turn == rot || chairs > 0); v++)
                foreach (int u in columns)
                    if (chairs < most && Fits(chair, At(u, s.Y, v), turn, Clear))
                    {
                        Device(chair, u, s.Y, v, turn, s);
                        chairs++;
                    }
        // (A bridge too cramped for that: one chair anywhere but in a doorway itself.)
        if (chairs == 0)
            foreach (int turn in new[] { rot, Turn(Dir.PosY, Dir.PosX), Turn(Dir.PosY, Dir.NegX), Turn(Dir.PosY, Dir.PosZ) })
                foreach (var (u, v) in s.Floor.Where(c => !s.Doors.Any(d => d.Inside == c)).OrderBy(c => c.V))
                    if (chairs == 0 && Fits(chair, At(u, s.Y, v), turn, Free(s)))
                    {
                        Device(chair, u, s.Y, v, turn, s);
                        chairs++;
                    }
    }

    /// <summary>Turrets where the form mounts them, each on the first free cell out from its plating, looking out of that face.</summary>
    void Turrets()
    {
        BlockType turret = Cat.Type("turret");
        foreach (var (at, face) in H.Form.Turrets)
        {
            Dir d = face switch { 0 => Dir.PosX, 1 => Dir.NegX, 2 => Dir.PosY, 3 => Dir.NegY, 4 => Dir.PosZ, _ => Dir.NegZ };
            Int3 step = d.Vec();
            int u = (int)MathF.Round(at.X), y = (int)MathF.Round(at.Y), v = (int)MathF.Round(at.Z);
            for (int k = 0; k < 6 && Has(u, y, v); k++) (u, y, v) = (u + step.X, y + step.Y, v + step.Z);
            if (!InPiece(v) || y < 0 || Has(u, y, v) || !Has(u - step.X, y - step.Y, v - step.Z)) continue;
            Device(turret, u, y, v, d == Dir.PosY ? Orientations.Identity : Turn(d, d.Axis() == 2 ? Dir.PosY : Dir.NegZ));
        }
    }

    /// <summary>
    /// Cargo containers where a crew keeps things, by the room's purpose (the stores and holds richest, a locker by
    /// the bunks, the galley's and medbay's cupboards), until the machines and containers together come to about 2.7
    /// per 100 m² of the vessel's floor.
    /// </summary>
    void Containers()
    {
        // (The core's piece at least one, for its fuel: in its bulk if no room takes it.)
        if (Out.CoreSlot != null && !S.Blocks().Any(b => S.TypeOf(b.block).IsContainer) && Cat.TryGetType("container-t1", out BlockType one))
        {
            var spot = Live.Where(s => !s.Corridor).SelectMany(s => WallCells(s, 1).Select(c => (Room: s, c.U, c.V))).FirstOrDefault(x => Fits(one, At(x.U, x.Room.Y, x.V), 0, Free(x.Room)));
            if (spot.Room != null) Device(one, spot.U, spot.Room.Y, spot.V, 0, spot.Room, Paint["orange"]);
            else Stow(one, Paint["orange"]);
        }
        float floor = Out.Spaces.Sum(s => s.Floor.Count - s.Taken.Count(t => s.Floor.Contains(t)));
        int count = S.Blocks().Count(b => !b.block.IsFiller && IsMachine(S.TypeOf(b.block)));
        int Count() => count;
        int target = (int)MathF.Round(floor * 0.027f);
        var rooms = Live.Where(s => !s.Corridor).OrderByDescending(s => Share(s.Purpose)).ToList();
        BlockType box = Cat.Type("container-t1"), big = Best("container", new Int3(2, 1, 1), 0, t => t.Storage);
        for (int pass = 0; pass < 6 && Count() < target; pass++)
            foreach (Space s in rooms)
            {
                if (Count() >= target) break;
                float share = Share(s.Purpose);
                int want = (int)MathF.Ceiling(s.Area * share / 100f * (pass + 1) / 3f) - s.Taken.Count(c => _containers.Contains(c));
                foreach (var (u, v) in WallCells(s, 2).Take(Math.Max(0, want)))
                {
                    BlockType t = s.Hall && big != null ? big : box;
                    if (!Fits(t, At(u, s.Y, v), 0, (x, y, vv) => !S.Has(new Int3(x, y, vv - Pc.V0)) && s.Floor.Contains((x, vv)) && !s.Taken.Contains((x, vv)))) continue;
                    Device(t, u, s.Y, v, 0, s, Paint["orange"]);
                    _containers.Add((u, v));
                    count++;
                }
            }
    }

    readonly HashSet<(int, int)> _containers = new();

    /// <summary>How rich in containers a kind of room is, per 100 m² (place-patterns P8 and E9).</summary>
    static float Share(string purpose) => purpose switch
    {
        "stores" or "armoury" or "magazine" => 8f,
        "hold" or "ore-hold" or "galley" or "medbay" => 5f,
        "crew-quarters" or "barracks" or "cabin" or "officers" or "workshop" => 3.5f,
        "bridge" or "core-room" or "hangar" => 2f,
        _ => 0.5f,
    };

    /// <summary>A machine or a container: what the 2.7 per 100 m² counts.</summary>
    public static bool IsMachine(BlockType t) => t.Function is "container" or "generator" or "gyro" or "warp-drive" or "hyperdrive" or "constructor" or "deconstructor" or "capacitor" or "forecast";

    // ---- lights ---------------------------------------------------------------------------------------

    /// <summary>
    /// Every space lit for what it is: ceiling lights a few metres apart in the crew's rooms, further apart in the
    /// holds, the machinery and the corridors (one per 11 to 15 m² lived in, one per 25 m² else: place-patterns P7),
    /// in the colour of the room's use (warm where people live, white in the medbay, cool on the bridge, amber by the
    /// machines, sodium in the holds).
    /// </summary>
    void Lights()
    {
        BlockType light = Cat.Type("light");
        foreach (Space s in Out.Spaces)
        {
            if (s.Floor.Count == 0 || s.Purpose == "stair" && s.Floor.Count < 8) continue;
            int step = s.Purpose switch { "crew-quarters" or "cabin" or "barracks" or "officers" or "galley" or "medbay" or "bridge" => 4, "corridor" => 6, _ => 5 };
            uint colour = s.Purpose switch
            {
                "crew-quarters" or "cabin" or "barracks" or "officers" or "galley" or "promenade" => 0xffddaa,
                "medbay" => 0xffffff,
                "bridge" or "sensor-room" => 0x33ddff,
                "engine-room" or "reactor" or "generator-room" or "core-room" or "workshop" or "refinery" => 0xffaa44,
                "hold" or "ore-hold" or "hangar" or "stores" => 0xffaa44,
                _ => 0xffddaa,
            };
            int top = s.Y + s.Height - 1, placed = 0;
            // A room's lamps on a grid; a corridor's down the middle of each run of it 3 m wide (the spine, the flank passages).
            bool F(int u, int v) => s.Floor.Contains((u, v));
            bool AcrossU((int U, int V) c) => F(c.U - 1, c.V) && F(c.U + 1, c.V) && !F(c.U - 2, c.V) && !F(c.U + 2, c.V);
            bool AcrossV((int U, int V) c) => F(c.U, c.V - 1) && F(c.U, c.V + 1) && !F(c.U, c.V - 2) && !F(c.U, c.V + 2);
            bool Spot((int U, int V) c) => s.Corridor && s.Purpose != "stair"
                ? AcrossU(c) && Mod(c.V, step) == step / 2 || AcrossV(c) && !AcrossU(c) && Mod(c.U, step) == step / 2
                : Mod(c.V, step) == step / 2 && Mod(c.U, step) == step / 2;
            var spots = s.Floor.Where(Spot).ToList();
            if (spots.Count == 0) spots = s.Floor.OrderBy(c => Math.Abs(c.U - (s.U0 + s.U1) / 2.0) + Math.Abs(c.V - (s.V0 + s.V1) / 2.0)).Take(1).ToList();
            // Its spots, or failing every one of those (a ceiling open over a stair, a machine under it), the nearest cell to its middle that takes one.
            foreach (var (u, v) in spots.Concat(s.Floor.OrderBy(c => Math.Abs(c.U - (s.U0 + s.U1) / 2.0) + Math.Abs(c.V - (s.V0 + s.V1) / 2.0))))
            {
                if (placed > 0 && !spots.Contains((u, v))) break;
                if (Has(u, top, v) || !Has(u, top + 1, v)) continue;
                Int3 at = D.Mount("light", At(u, top, v), Dir.PosY);
                S.SetLight(at, LightSettings.Make(colour, LightAnim.Steady, 0.5f, u * 7 + v));
                placed++;
            }
            // A room whose ceiling takes none (open over a flight, a machine under every cell of it): a lamp high on a wall,
            // or failing that one standing on the floor in a corner.
            if (placed == 0)
                foreach (int y in new[] { top, s.Y })
                {
                    foreach (var (u, v) in s.Floor.Where(c => !Has(c.U, y, c.V) && !s.Taken.Contains(c)))
                    {
                        var wall = Four.FirstOrDefault(o => Has(u + o.U, y, v + o.V));
                        if (wall == default) continue;
                        Int3 at = D.Mount("light", At(u, y, v), y == top ? DirOf(wall.U, wall.V) : Dir.NegY);
                        S.SetLight(at, LightSettings.Make(colour, LightAnim.Steady, 0.5f, u * 7 + v));
                        placed++;
                        break;
                    }
                    if (placed > 0) break;
                }
        }
    }
}
