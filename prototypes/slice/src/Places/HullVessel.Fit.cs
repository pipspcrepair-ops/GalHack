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

/// <summary>Stairs, doors, the ways in, windows, the engines and every room's fittings and lights.</summary>
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

    // ---- stairs ---------------------------------------------------------------------------------------

    /// <summary>
    /// Each core's switchback: a flight of five up from every deck to the next, in the lane up the core's inboard side
    /// from an even deck and back down its outboard lane from an odd one, landings across both lanes at its ends,
    /// the deck over each flight open for headroom, and the landing open onto the spine.
    /// </summary>
    void Stairs()
    {
        BlockType stairs = Cat.Type("stairs");
        foreach (var (_, s, top) in _cores)
        {
            for (int k = 0; k < top; k++)
            {
                bool up = k % 2 == 0;
                int y0 = Plan.Walk(k);
                foreach (int u in up ? new[] { 3, 4 } : new[] { 6, 7 })
                    for (int j = 0; j < 5; j++)
                    {
                        int v = up ? s + j : s + 4 - j;
                        if (j < 4) Clear(u, Plan.Walk(k + 1) - 1, up ? s + j : s + 4 - j);
                        Device(stairs, u, y0 + j, v, Turn(Dir.PosY, up ? Dir.NegZ : Dir.PosZ), null, Paint["deck"]);
                    }
            }
            for (int d = 0; d <= top; d++)
            {
                int row = d % 2 == 0 ? s - 1 : s + 5;
                for (int y = Plan.Walk(d); y < Plan.Walk(d) + 3; y++) Clear(2, y, row);
                Space core = Out.Spaces.First(x => x.Purpose == "stair" && x.Deck == d && x.Floor.Contains((3, row)));
                core.Doors.Add(((2, row), (3, row), 1));
                for (int v = s; v <= s + 4; v++) core.Taken.UnionWith(new[] { (3, v), (4, v), (6, v), (7, v) });
            }
        }
    }

    // ---- doors ----------------------------------------------------------------------------------------

    /// <summary>
    /// A door from every room to the corridor beside it (two from a big room, one to the spine and one to the flank
    /// passage), wide doors at a hall's ends and the catwalk through it, the core room through the reactor. Every door
    /// opens by hand.
    /// </summary>
    void Doors()
    {
        for (int i = 0; i < Out.Spaces.Count; i++)
        {
            Space s = Out.Spaces[i];
            if (s.Corridor || s.Floor.Count == 0) continue;
            if (s.Hall) { HallDoors(s); continue; }
            if (s.Purpose == "core-room") continue;
            if (s.Purpose is "officers" or "bridge") continue; // the tower's decks open on the stair core
            // A door on its inboard side, else its outboard; a big room has both.
            bool made = RoomDoor(s, false);
            if (!made || s.Area >= 40) RoomDoor(s, true);
        }
        // The core room: only through the reactor.
        if (_vault >= 0)
        {
            Space core = Out.Spaces[_vault];
            Space reactor = Live.FirstOrDefault(x => x.Purpose == "reactor" && x.Deck == core.Deck && Math.Sign(x.U0) == Math.Sign(core.U0));
            int wall = core.V0 - 1;
            if (reactor != null)
                foreach (int u in core.Floor.Where(c => c.V == core.V0).Select(c => c.U).OrderBy(u => Math.Abs(u - (core.U0 + core.U1) / 2.0)))
                    if (reactor.Floor.Contains((u, wall - 1)) && ZoneAt(core.Deck, u, wall) == Fill)
                    {
                        PutDoor(core, u, wall, Dir.NegZ, 1, (u, core.V0));
                        reactor.Doors.Add(((u, wall), (u, wall - 1), 1));
                        break;
                    }
        }
    }

    /// <summary>A door in the room's inboard wall to the spine (or its outboard wall to the flank passage), near the middle of it.</summary>
    bool RoomDoor(Space s, bool outer)
    {
        double mid = (s.V0 + s.V1) / 2.0;
        int side = Math.Sign(s.U0 + s.U1);
        foreach (int v in Enumerable.Range(s.V0, s.V1 - s.V0 + 1).OrderBy(v => Math.Abs(v + 0.5 - mid)))
        {
            var row = s.Floor.Where(c => c.V == v).Select(c => Math.Abs(c.U)).ToList();
            if (row.Count == 0) continue;
            int room = outer ? row.Max() : row.Min(), step = outer ? 1 : -1;
            int wall = side * (room + step), into = side * (room + 2 * step);
            bool Ok(int w) => s.Floor.Contains((side * room, w)) && ZoneAt(s.Deck, wall, w) == Fill && ZoneAt(s.Deck, into, w) is int z && z >= 0 && Out.Spaces[z].Corridor
                              && !s.Doors.Any(d => Math.Abs(d.Wall.V - w) < 4);
            if (!Ok(v)) continue;
            int width = s.Area >= 30 && Ok(v + 1) ? 2 : 1;
            PutDoor(s, wall, v, side > 0 ? Dir.PosX : Dir.NegX, width, (side * room, v));
            return true;
        }
        return false;
    }

    void PutDoor(Space s, int u, int v, Dir through, int width, (int U, int V) inside)
    {
        BlockType t = Cat.Type(width >= 2 ? "door-2x2x1" : "door-1x2x1");
        int rot = Turn(Dir.PosY, through);
        Device(t, u, s.Y, v, rot);
        s.Doors.Add(((u, v), inside, width));
    }

    /// <summary>A hall's ends: a 3 m door on the spine into the cross passage on deck 0, the catwalk's way through on deck 1.</summary>
    void HallDoors(Space hall)
    {
        BlockType big = Cat.Type("door-3x3x1");
        foreach (int wall in new[] { hall.V0 - 1, hall.V1 + 1 })
        {
            int beyond = wall < hall.V0 ? wall - 1 : wall + 1;
            if (ZoneAt(0, 0, beyond) is not (int z and >= 0) || !Out.Spaces[z].Corridor) continue;
            Device(big, -1, Plan.Walk(0), wall, Turn(Dir.PosY, Dir.PosZ));
            hall.Doors.Add(((-1, wall), (0, wall < hall.V0 ? hall.V0 : hall.V1), 3));
            // On deck 1 the catwalk and the galleries run on through the bulkhead into the cross passage.
            int inside = wall < hall.V0 ? hall.V0 : hall.V1;
            for (int u = -Reach(1, inside); u <= Reach(1, inside); u++)
                if (ZoneAt(1, u, inside) is int a and >= 0 && Out.Spaces[a].Corridor && ZoneAt(1, u, beyond) is int z1 and >= 0 && Out.Spaces[z1].Corridor)
                    for (int y = Plan.Walk(1); y < Plan.Walk(1) + 3; y++) Clear(u, y, wall);
        }
    }

    // ---- the ways in ----------------------------------------------------------------------------------

    /// <summary>The hull door into the starboard passage, and the plan's openings (the cargo ramp, the breaches, the parted faces).</summary>
    void Ways()
    {
        int y0 = Plan.Walk(0);
        if (InPiece(H.DoorV) && H.DoorV < H.L)
        {
            int v = H.DoorV, r = Reach(0, v);
            // Cut through the shell to the outside; the door sits in its inner layer.
            for (int u = r + 1; u <= H.Half + 1; u++)
                for (int dv = 0; dv < 2; dv++)
                    for (int y = y0; y < y0 + 2; y++) Clear(u, y, v + dv);
            Out.Door = Device(Cat.Type("door-2x2x1"), r + 1, y0, v, Turn(Dir.PosY, Dir.PosX));
        }
        foreach (CapitalHull.Way w in H.Ways)
        {
            if (w.Side == 0)
            {
                int v = w.What == "fore-face" ? w.V0 : w.V0;
                if (!InPiece(v)) continue;
                for (int d = 0; d <= 1; d++)
                    for (int u = -Reach(d, v); u <= Reach(d, v); u++)
                        for (int y = Plan.Walk(d); y < Plan.Walk(d) + 4; y++) Clear(u, y, v);
                continue;
            }
            for (int v = w.V0; v < w.V0 + w.Width; v++)
            {
                if (!InPiece(v)) continue;
                int r = Reach(0, v);
                for (int u = r + 1; u <= H.Half + 1; u++)
                {
                    for (int y = y0; y < y0 + w.Height; y++) Clear(w.Side * u, y, v);
                    // A level sill out to the hull's side.
                    if (Mask(w.Side * u, y0 - 1, v) != 0) Solid(w.Side * u, y0 - 1, v, Paint["hazard"]);
                }
            }
        }
    }

    // ---- windows --------------------------------------------------------------------------------------

    /// <summary>The bridge's windows across its front and down its sides (a lower band for the officers' deck), portholes along the flank passages.</summary>
    void Windows()
    {
        foreach (var (d, rows) in new[] { (H.Decks + 1, 2), (H.Decks, 1) })
            for (int y = Plan.Walk(d) + 1; y <= Plan.Walk(d) + rows; y++)
                for (int v = H.TV0 - 1; v <= H.TV1 + 1; v++)
                    for (int u = H.TU0 - 1; u <= H.TU1 + 1; u++)
                    {
                        bool front = v <= H.TV0 + 1, side = (u <= H.TU0 + 1 || u >= H.TU1 - 1) && v < H.TV0 + (H.TV1 - H.TV0) * 2 / 3;
                        if ((front || side) && !Interior(u, y, v) && S.TryGet(At(u, y, v), out Block b) && S.TypeOf(b) == Plating) Glaze(u, y, v, b);
                    }
        for (int d = 0; d < H.Decks; d++)
            for (int v = Math.Max(Pc.V0, H.Bz); v < Math.Min(VEnd, H.L - 4); v++)
            {
                if (Mod(v, 8) > 1) continue;
                int r = Reach(d, v);
                foreach (int side in new[] { -1, 1 })
                {
                    if (ZoneAt(d, side * r, v) is not (int z and >= 0) || !Out.Spaces[z].Corridor) continue;
                    int y = Plan.Walk(d) + 1;
                    for (int dy = 0; dy < 2; dy++)
                        if (S.TryGet(At(side * (r + 1), y + dy, v), out Block b) && S.TypeOf(b) == Plating && !Interior(side * (r + 1), y + dy, v)) Glaze(side * (r + 1), y + dy, v, b);
                }
            }
    }

    void Glaze(int u, int y, int v, Block was)
    {
        Block g = Building.Make(S, Glass, S.ShapeOf(was), was.Rot);
        Put(u, y, v, g);
    }

    // ---- the engines ----------------------------------------------------------------------------------

    /// <summary>
    /// The nacelles' faces packed with the biggest main engines that fit them, their exhausts flush with the face; a
    /// retro and side jets in a pod on each flank; down jets on the roof. The lift engines go in last, once the hull's
    /// weight is known (<see cref="Lift"/>).
    /// </summary>
    void Engines()
    {
        if (!Pc.Last) return;
        int face = H.L + H.Nac - 1, rot = Turn(Dir.PosY, Dir.NegZ);
        BlockType main = Best("thruster", new Int3(3, 3, H.Nac - 1), rot, t => t.Thrust);
        if (main == null) return;
        Int3 size = Footprint.WorldSize(main, rot);
        foreach (var (u0, u1) in H.Nacelles)
            for (int y = H.NY0; y + size.Y - 1 <= H.NY1; y += size.Y)
                for (int u = u0; u + size.X - 1 <= u1; u += size.X)
                {
                    int v = face - size.Z + 1;
                    bool inside = true;
                    for (int dy = 0; dy < size.Y && inside; dy++)
                        for (int du = 0; du < size.X && inside; du++) inside = Mask(u + du, y + dy, face) == 255;
                    if (inside) Device(main, u, y, v, rot, null, Paint["dark"]);
                }
        // Manoeuvring pods on both flanks aft of the bow: a retro (pushing aft) and a side jet each.
        BlockType jet = Best("thruster", new Int3(2, 2, 4), Turn(Dir.PosY, Dir.PosZ), t => t.Thrust);
        if (jet == null) return;
        int pv = Math.Max(Pc.V0, H.Bz + 2), py = H.Top / 2;
        foreach (int side in new[] { -1, 1 })
        {
            int u = side > 0 ? H.Half + 1 : -H.Half - 2;
            if (Fits(jet, At(u, py, pv), Turn(Dir.PosY, Dir.PosZ))) Device(jet, u, py, pv, Turn(Dir.PosY, Dir.PosZ), null, Paint["trim"]);
            int sr = Turn(Dir.PosZ, side > 0 ? Dir.NegX : Dir.PosX), su = side > 0 ? H.Half + 1 : -H.Half - 4;
            if (Fits(jet, At(su, py + 3, pv), sr)) Device(jet, su, py + 3, pv, sr, null, Paint["trim"]);
        }
    }

    /// <summary>
    /// Lift: down-firing engines stood on the keel along the halls' flanks (their exhausts through the bottom), until
    /// their push is half again the hull's weight at a heavy world's gravity; then generators enough to run it all.
    /// </summary>
    void Lift()
    {
        const double g = 9.81 * 1.3;
        int rot = Turn(Dir.PosZ, Dir.PosY);
        BlockType lift = Best("thruster", new Int3(3, Plan.Pitch + 1, 3), rot, t => t.Thrust);
        if (lift == null) return;
        Int3 size = Footprint.WorldSize(lift, rot);
        double Up() => S.Blocks().Where(b => S.TypeOf(b.block).IsThruster && Orientations.ToWorld(b.block.Rot, Dir.NegZ) == Dir.PosY).Sum(b => (double)S.TypeOf(b.block).Thrust);
        double need = StructureStats.Compute(S).Mass * 1.3 * g * 1.5, up = Up();
        foreach (Space hall in Live.Where(s => s.Hall).OrderBy(s => s.Purpose == "engine-room" ? 0 : 1))
        {
            int r = hall.Floor.Max(c => Math.Abs(c.U));
            for (int v = hall.V0 + 3; v + size.Z - 1 <= hall.V1 - 2 && up < need; v += size.Z + 2)
                foreach (int side in new[] { -1, 1 })
                {
                    // In the line of the holds' first row of cargo, either side of the way down the middle.
                    int u = side > 0 ? 2 : -2 - size.X + 1;
                    if (up >= need || r < 2 + size.X + 1) continue;
                    bool flat = true;
                    for (int dv = 0; dv < size.Z && flat; dv++)
                        for (int du = 0; du < size.X && flat; du++) flat = Mask(u + du, 0, v + dv) == 255 && hall.Floor.Contains((u + du, v + dv)) && !hall.Taken.Contains((u + du, v + dv));
                    if (!flat) continue;
                    Device(lift, u, 0, v, rot, hall, Paint["dark"]);
                    up += lift.Thrust;
                }
        }
    }

    /// <summary>Generators enough for everything that draws power: in the reactor first (its largest), then the generator rooms.</summary>
    void Power()
    {
        var all = S.Blocks().Where(b => !b.block.IsFiller).Select(b => S.TypeOf(b.block).Power).ToList();
        double draw = all.Sum(p => -Math.Min(0f, p)), made = all.Sum(p => Math.Max(0f, p));
        foreach (Space room in Live.Where(s => s.Purpose is "reactor" or "generator-room").OrderBy(s => s.Purpose == "reactor" ? 0 : 1).ToList())
        {
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
    /// What each room is fitted with, as its purpose has it: cargo stacked in the holds with aisles between, the
    /// machines of the engine room (gyros, the warp drive), the workshop's fabricator and deconstructor, the bridge's
    /// cockpit and command chairs, cargo containers in the stores, the holds and wherever a crew keeps things, turrets
    /// on the roof, and the core room's empty seat. The machines and containers come to about 2.7 per 100 m² of floor,
    /// as Empyrion's wrecks have them.
    /// </summary>
    void Fittings()
    {
        // The lift engines first, where the holds' cargo would stand: what is still to come (the cargo, the generators, the
        // containers) is allowed for in their sum.
        Lift();
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
                        if (Fits(t, At(u, s.Y, v), 0, (x, y, vv) => !S.Has(new Int3(x, y, vv - Pc.V0)) && s.Floor.Contains((x, vv)) && !s.Taken.Contains((x, vv))))
                        {
                            Device(t, u, s.Y, v, 0, s, Paint["machine"]);
                            break;
                        }
        if (_vault >= 0)
        {
            Space core = Out.Spaces[_vault];
            var mid = core.Floor.OrderBy(c => Math.Abs(c.U - (core.U0 + core.U1) / 2.0) + Math.Abs(c.V - (core.V0 + core.V1) / 2.0)).First();
            Out.CoreSlot = At(mid.U, core.Y, mid.V);
            core.Taken.Add(mid);
            // An engineer's chair at its wall: the piece the core claims can be flown from it whichever piece the bridge came down in.
            BlockType chair = Cat.Type("command-chair");
            int rot = Turn(Dir.PosY, Dir.NegZ);
            foreach (var (u, v) in WallCells(core, 1))
                if (Fits(chair, At(u, core.Y, v), rot, (x, y, vv) => !S.Has(new Int3(x, y, vv - Pc.V0)) && core.Floor.Contains((x, vv)) && !core.Taken.Contains((x, vv))))
                {
                    Device(chair, u, core.Y, v, rot, core);
                    break;
                }
        }
        Turrets();
        Power();
        Containers();
    }

    /// <summary>
    /// What a room stores stood in rows along it, 2 m aisles between them, along the walls and at the doors: a hold's
    /// cargo three wide, 6 m long and two or three high in a hall (one under a 4 m deck), a store's or an armoury's
    /// shelving one wide. A row too narrow for the space left by the wall is made narrower, never left a wide gap.
    /// </summary>
    void Rows(Space s, int width, int length, bool cargo, BlockType machine = null)
    {
        string[] colours = { "orange", "teal", "hazard", "blue", "red", "hull" };
        // A row is as long as the room leaves with a 2 m aisle at either end.
        length = Math.Min(length, s.V1 - s.V0 - 3);
        if (length < 3) return;
        int along = Turn(Dir.PosY, Dir.PosX);
        var doorways = new HashSet<(int, int)>(s.Doors.SelectMany(d => Around(d.Inside, 2)));
        foreach (int side in new[] { -1, 1 })
        {
            var mine = s.Floor.Where(c => Math.Sign(c.U) == side || s.Hall && c.U == 0).ToList();
            if (mine.Count == 0) continue;
            int inner = s.Hall ? 2 : mine.Min(c => Math.Abs(c.U)) + 2, outer = mine.Max(c => Math.Abs(c.U)) - 2;
            for (int row = inner; row <= outer; row += width + 2)
            {
                int w = Math.Min(width, outer - row + 1), u0 = side > 0 ? row : -row - w + 1;
                for (int v = s.V0 + 2; v + length - 1 <= s.V1 - 2; v += length + 2)
                {
                    bool clear = true;
                    for (int dv = 0; dv < length && clear; dv++)
                        for (int du = 0; du < w && clear; du++)
                            clear = s.Floor.Contains((u0 + du, v + dv)) && !s.Taken.Contains((u0 + du, v + dv)) && !doorways.Contains((u0 + du, v + dv));
                    if (!clear) continue;
                    if (machine != null)
                    {
                        // Machines in the row, end to end along it, two high.
                        Int3 size = Footprint.WorldSize(machine, along);
                        for (int dv = 0; dv + size.Z <= length; dv += size.Z)
                            for (int y = s.Y; y + size.Y <= s.Y + 2; y += size.Y)
                                if (size.X <= w) Device(machine, u0, y, v + dv, along, s, Paint["machine"]);
                        for (int dv = 0; dv < length; dv++)
                            for (int du = 0; du < w; du++) s.Taken.Add((u0 + du, v + dv));
                        continue;
                    }
                    int high = s.Hall ? Rng.Next(2, 4) * 2 + (s.Purpose == "ore-hold" ? -2 : 0) : 3;
                    ushort paint = cargo ? Paint[colours[Rng.Next(colours.Length)]] : Paint["machine"];
                    for (int dv = 0; dv < length; dv++)
                        for (int du = 0; du < w; du++)
                        {
                            s.Taken.Add((u0 + du, v + dv));
                            for (int y = s.Y; y < s.Y + high; y++) Solid(u0 + du, y, v + dv, (dv == 0 || dv == length - 1 || y == s.Y + high - 1) ? Paint["trim"] : paint);
                        }
                }
            }
        }
    }


    /// <summary>The engine room: the warp drive on its centre line aft, gyros in a row down each side.</summary>
    void EngineRoom(Space s)
    {
        if (Cat.TryGetType("warp-drive", out BlockType warp))
        {
            Int3 size = Footprint.WorldSize(warp, 0);
            int u = -size.X / 2, v = s.V1 - size.Z - 1;
            if (Fits(warp, At(u, s.Y, v), 0, (x, y, vv) => !S.Has(new Int3(x, y, vv - Pc.V0)) && s.Floor.Contains((x, vv)))) Device(warp, u, s.Y, v, 0, s, Paint["machine"]);
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

    /// <summary>The bridge: the helm's cockpit at the forward windows on the centre line, command chairs either side of it, facing forward.</summary>
    void Bridge(Space s)
    {
        int rot = Turn(Dir.PosY, Dir.NegZ);
        int front = s.V0 + 1;
        if (Cat.TryGetType("cockpit-t1", out BlockType cockpit))
        {
            Int3 size = Footprint.WorldSize(cockpit, rot);
            int u = -size.X / 2;
            if (Fits(cockpit, At(u, s.Y, front), rot, (x, y, vv) => !S.Has(new Int3(x, y, vv - Pc.V0)) && s.Floor.Contains((x, vv)))) Device(cockpit, u, s.Y, front, rot, s);
        }
        BlockType chair = Cat.Type("command-chair");
        foreach (int u in new[] { -7, -5, 4, 6 })
            if (Fits(chair, At(u, s.Y, front), rot, (x, y, vv) => !S.Has(new Int3(x, y, vv - Pc.V0)) && s.Floor.Contains((x, vv)) && !s.Taken.Contains((x, vv)))) Device(chair, u, s.Y, front, rot, s);
    }

    /// <summary>Turrets on the roof and the tower's crown: a warship bristles, a hauler carries a few.</summary>
    void Turrets()
    {
        BlockType turret = Cat.Type("turret");
        int want = H.Role == "warship" ? 12 : H.Role == "carrier" ? 8 : 4, placed = 0;
        int every = Math.Max(6, (Math.Min(VEnd, H.L) - Math.Max(Pc.V0, H.Bz)) / Math.Max(1, want / 2));
        for (int v = Math.Max(Pc.V0, H.Bz) + 2; v < Math.Min(VEnd, H.L - 3) && placed < want; v += every)
            foreach (int side in new[] { -1, 1 })
            {
                int u = side * (H.Half / 2);
                int y = H.Top + 1;
                if (InTower(u, v) || Mask(u, H.Top, v) != 255 || Has(u, y, v)) continue;
                Device(turret, u, y, v, 0);
                placed++;
            }
    }

    /// <summary>
    /// Cargo containers where a crew keeps things, by the room's purpose (the stores and holds richest, a locker by
    /// the bunks, the galley's and medbay's cupboards), until the machines and containers together come to about 2.7
    /// per 100 m² of the vessel's floor.
    /// </summary>
    void Containers()
    {
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
            if (s.Floor.Count == 0 || s.Purpose == "stair" && s.Floor.Count < 8 || s.Purpose == "crawlway") continue;
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
            bool Middle((int U, int V) c) => s.Floor.Contains((c.U - 1, c.V)) && s.Floor.Contains((c.U + 1, c.V)) && !s.Floor.Contains((c.U - 2, c.V)) && !s.Floor.Contains((c.U + 2, c.V));
            var spots = s.Floor.Where(c => Mod(c.V, step) == step / 2 && (s.Corridor && s.Purpose != "stair" ? Middle(c) : Mod(c.U, step) == step / 2)).ToList();
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
        }
    }
}
