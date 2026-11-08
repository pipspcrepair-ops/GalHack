using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PoiGenerator.Gen;

namespace PoiGenerator;

public partial class PoiTest
{
    /// <summary>
    /// The one door, in every family (houses have their own stricter check): on fresh seeds, turned every way, at
    /// every tech level, each doorway a person walks through and each way in is <see cref="DoorSize"/>'s door, 2 m
    /// wide and 3 m tall exactly under its head; for 2 m either side of it nothing comes lower than its head, and
    /// out of doors nothing lower than its head and a metre more. Every opening a person can reach between two
    /// walls (a gap, a mouth with no door) is at least the door's height clear. Hatches you climb through are not doors.
    /// </summary>
    void Doors()
    {
        var rand = new Random();
        int places = 0, doors = 0, offSize = 0, noHead = 0, inWay = 0, low = 0, failed = 0, outlined = 0;
        string first = "";
        var byFamily = new Dictionary<string, int>();
        void Note(string fam, string s)
        {
            byFamily[fam] = byFamily.GetValueOrDefault(fam) + 1;
            if (byFamily[fam] <= 2) GD.Print($"      {fam}: {s}");
            if (first.Length == 0) first = $" (first: {fam}: {s})";
        }
        // "-- --doors=family,family" looks at just those, more often (a plan being fixed).
        string[]? only = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--doors="))?["--doors=".Length..].Split(',');
        Layout? layout0 = null;
        string Room(Int3 c) => layout0?.Modules.FirstOrDefault(m => m.Cells.Contains(c)) is { } m ? $"{m.Purpose}/{m.Kind}" : "no room";
        foreach (FamilyDef f in _lib.Families.Where(f => f.Id != "flavour_house" && (only == null || only.Contains(f.Id))))
            for (int k = 0; k < (only == null ? 4 : 12); k++)
            {
                Layout layout;
                int variant = rand.Next(f.Variants.Count);
                try { layout = PoiFactory.BuildLayout(_lib, f, variant, f.Rarity, (ulong)rand.NextInt64(), new LayoutHints(Dirs.Horizontal[rand.Next(4)], (TechLevel)rand.Next(3))); }
                catch (Exception e) { failed++; Note(f.Id, $"no layout: {e.Message}"); continue; }
                places++;
                layout0 = layout;
                var g = layout.Blocks;
                string name = $"{f.Id} '{f.Variants[variant].Name}'";
                // Which cells are a room's: the far side of a door that is no room's is out of doors.
                var indoors = new HashSet<Int3>(layout.Modules.Where(m => m.Kind != ModuleKind.Outdoor).SelectMany(m => m.Cells));
                var rooms = new HashSet<Int3>(layout.Modules.SelectMany(m => m.Cells));
                bool Hard(Int3 c) => g.Get(c) is var b && BlockInfo.Collides(b.Type) && b.Role != Role.Traverse && !BlockInfo.IsSlope(b.Type);
                int Clear(Int3 c, int max) { int n = 0; while (n < max && !BlockInfo.Collides(g.Get(c + Int3.Up * n).Type)) n++; return n; }
                var blast = layout.Extras.Locks.Where(l => l.Blast).Select(l => l.Doorway).ToHashSet();
                var ways = layout.Doorways.Select((d, i) => (d, i)).Where(x => x.d.Facing.IsHorizontal() && x.d.Cells.Count > 0)
                    .Select(x => (x.d.Cells, x.d.Facing, Exit: false, Danger: x.d.Hazard || blast.Contains(x.i))).ToList();
                if (layout.ExitCells.Count > 0) ways.Add((layout.ExitCells, layout.EntranceFacing, Exit: true, Danger: false));
                foreach (var (cells, facing, exit, danger) in ways)
                {
                    doors++;
                    Int3 lo = cells.Aggregate(Int3.Min), hi = cells.Aggregate(Int3.Max);
                    int wide = Math.Max(hi.X - lo.X, hi.Z - lo.Z) + 1, tall = hi.Y - lo.Y + 1;
                    var feet = cells.Where(c => c.Y == lo.Y).ToList();
                    // The opening as built: clear from the sill up to the head, the head solid.
                    int open = feet.Select(c => { int n = 0; while (n < 8 && !BlockInfo.Collides(g.Get(c + Int3.Up * n).Type)) n++; return n; }).Min();
                    if (wide != DoorSize.Wide || tall != DoorSize.Tall) { offSize++; Note(f.Id, $"{name}: a {(exit ? "way in" : "door")} {wide} m wide and {tall} m tall at {lo}"); }
                    else if (open != DoorSize.Tall) { noHead++; Note(f.Id, $"{name}: a {(exit ? "way in" : "door")} {open} m clear under its head at {lo}"); }
                    // No person's door outlined in hazard yellow: not its jambs, not its head (a dangerous one wears stripes on its own door).
                    Int3 alongWall = facing.RotateCw(1).Vec();
                    // An outline is a colour the wall doesn't wear further off (a sandstone wall is no frame).
                    Int3? yellow = cells.SelectMany(c => new[] { (c + alongWall, c + alongWall * 4), (c - alongWall, c - alongWall * 4), (c + Int3.Up, c + Int3.Up * 3) })
                        .Where(x => !cells.Contains(x.Item1)).Cast<(Int3, Int3)?>()
                        .FirstOrDefault(x => g.Get(x!.Value.Item1) is { Role: Role.Wall } w && Facade.IsHazard(w.Color) && g.Get(x.Value.Item2).Color != w.Color)?.Item1;
                    if (!danger && yellow is Int3 yc) { outlined++; Note(f.Id, $"{name}: a {(exit ? "way in" : "door")} outlined in {g.Get(yc).Color} at {yc}"); }
                    // Either side, past the wall's own thickness (the cells still under the door's head): nothing hangs lower than
                    // the head over the way, out of doors nothing lower than the head and a metre more. A wall or a machine standing
                    // on the floor doesn't hang: only what has clear floor under it counts.
                    foreach (Dir side in new[] { facing, facing.Opposite() })
                    {
                        Int3 o = side.Vec();
                        bool InWall(Int3 c) => !rooms.Contains(c) && Clear(c, 8) == DoorSize.Tall && g.Get(c + Int3.Up * DoorSize.Tall).Role == Role.Wall;
                        int through = 0;
                        while (through < 2 && feet.All(c => InWall(c + o * (through + 1)))) through++;
                        bool outside = exit ? side == facing : feet.All(c => !indoors.Contains(c + o * (through + 1)) && !indoors.Contains(c + o * (through + DoorSize.Approach)));
                        int over = outside ? DoorSize.Headroom : DoorSize.Tall;
                        Int3? hit = null;
                        int at = 0;
                        foreach (Int3 c in feet)
                            for (int d = through + 1; d <= through + DoorSize.Approach && hit == null; d++)
                            {
                                Int3 col = c + o * d;
                                if (Hard(col)) continue;
                                for (int y = 1; y < over && hit == null; y++)
                                    if (Hard(col + Int3.Up * y)) { hit = col + Int3.Up * y; at = d - through; }
                            }
                        if (hit is Int3 h) { inWay++; Note(f.Id, $"{name}: {g.Get(h)} {h.Y - lo.Y} m up and {at} m {(outside ? "out of doors" : "inside")} in front of the {(exit ? "way in" : "door")} at {lo} facing {side}, from {Room(feet[0] - o)} to {Room(feet[0] + o * (through + DoorSize.Approach))}"); }
                    }
                }
                // Openings with no door: a reachable cell in a gap one or two wide through a wall, its jambs wall up to the head, the
                // head a lintel with higher ground either side of it (a way through the wall, not a low room), clear to the door's height.
                var nav = new NavGrid(g, layout.Occupied, layout.Extras.Lifts, layout.Extras.InSpace);
                bool Jamb(Int3 c, int up) => Enumerable.Range(0, up + 1).All(k => g.Get(c + Int3.Up * k) is { Role: Role.Wall } w && BlockInfo.IsFullCube(w.Type));
                foreach (Int3 c in nav.Reachable(layout.EntranceCell))
                {
                    if (!nav.Standable(c) || nav.RampAt(c, out _) || nav.LadderAt(c)) continue;
                    int clear = Clear(c, DoorSize.Tall);
                    if (clear >= DoorSize.Tall || g.Get(c + Int3.Up * clear).Role is not (Role.Wall or Role.Roof)) continue;
                    bool gap = false;
                    string why = "";
                    foreach (Dir a in new[] { Dir.East, Dir.South })
                    {
                        Int3 v = a.Vec(), across = a.RotateCw(1).Vec();
                        bool jambs = Jamb(c + v, clear) && Jamb(c - v, clear) || Jamb(c + v, clear) && !Jamb(c - v, 0) && Jamb(c - v * 2, clear) || Jamb(c - v, clear) && !Jamb(c + v, 0) && Jamb(c + v * 2, clear);
                        bool Higher(int s) => Enumerable.Range(1, 3).Any(k => nav.Standable(c + across * (s * k)) && Clear(c + across * (s * k), DoorSize.Tall) > clear);
                        if (jambs && Higher(1) && Higher(-1)) { gap = true; why = $"jambs {a}: {g.Get(c + v)} / {g.Get(c - v)} / {g.Get(c + v * 2)} / {g.Get(c - v * 2)}; houses {string.Join(",", layout.Extras.Homes.Select(h => h.Silhouette))}"; }
                    }
                    if (gap) { low++; Note(f.Id, $"{name}: an opening {clear} m clear at {c} under {g.Get(c + Int3.Up * clear)}, in {Room(c)}; {why}"); break; }
                }
            }
        foreach (var (fam, n) in byFamily.OrderByDescending(x => x.Value).Take(16)) GD.Print($"      {n} x {fam}");
        Check(failed == 0 && offSize == 0 && noHead == 0, $"every door and way in is the standard {DoorSize.Wide} by {DoorSize.Tall} m door under a solid head: {doors} in {places} places, {offSize} another size, {noHead} not {DoorSize.Tall} m clear, {failed} places not laid out{first}");
        Check(outlined == 0, $"no person's door is outlined in hazard yellow or gold: {outlined} outlined{first}");
        Check(inWay == 0, $"nothing comes lower than a door's head for {DoorSize.Approach} m either side of it, nor lower than {DoorSize.Headroom} m out of doors: {inWay} in the way{first}");
        Check(low == 0, $"every opening a person can reach between two walls is {DoorSize.Tall} m clear: {low} places with a lower one{first}");
    }
}
