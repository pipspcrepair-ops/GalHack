using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PlanetSurface.Poi;
using PoiGenerator.Gen;
using Slice.Game;
using Slice.Places;
using N = System.Numerics;
using PNav = PoiGenerator.Gen.NavGrid;

namespace Slice.Tests;

/// <summary>
/// A player's way round a place, with the game's own inputs only: routes are the place's own
/// walking routes (the generator's A* over its cells, round the dressing), walked a cell at a time
/// by looking and holding W; a shut door on the way is looked at and opened with E (a jammed one is
/// squeezed through at its gap); ladders are climbed by walking into them. Nothing is moved by hand.
/// </summary>
public sealed class PlaceWalk
{
    readonly Game.Game _game;
    readonly Driver _drive;
    Controls Controls => _game.Controls;

    /// <summary>Doors opened with E, stair and ladder cells walked, on the way so far.</summary>
    public int DoorsOpened, StairCells, LadderCells, Stuck;
    public readonly List<string> Notes = new();

    public PlaceWalk(Game.Game game, Driver drive)
    {
        _game = game;
        _drive = drive;
    }

    public static Vector3 Feet(Place p, Int3 c) => p.ToWorld(new Vector3(c.X + 0.5f, c.Y, c.Z + 0.5f));

    /// <summary>The standable cell the survivor is in (or the nearest one round their feet).</summary>
    public Int3 Here(Place p, PNav nav)
    {
        Vector3 l = p.FromWorld(_game.Survivor.WorldFeet);
        var c = new Int3(Mathf.FloorToInt(l.X), Mathf.FloorToInt(l.Y + 0.3f), Mathf.FloorToInt(l.Z));
        if (nav.Standable(c)) return c;
        Int3 best = c;
        float bestD = float.MaxValue;
        for (int dy = -1; dy <= 1; dy++)
        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
        {
            var q = c + new Int3(dx, dy, dz);
            if (!nav.Standable(q)) continue;
            float d = new Vector3(q.X + 0.5f - l.X, (q.Y - l.Y) * 2f, q.Z + 0.5f - l.Z).Length();
            if (d < bestD) { bestD = d; best = q; }
        }
        return best;
    }

    /// <summary>What stands between the feet and a point, at knee height (for a stuck note).</summary>
    string InTheWay(Vector3 to)
    {
        Vector3 from = _game.Survivor.WorldFeet + Vector3.Up * 0.5f;
        var q = PhysicsRayQueryParameters3D.Create(from, to + Vector3.Up * 0.5f, GalHack.Physics.Layers.World | GalHack.Physics.Layers.Props);
        var hit = _game.Zone.GetWorld3D().DirectSpaceState.IntersectRay(q);
        return hit.Count == 0 ? "nothing" : $"{(hit["collider"].AsGodotObject() as Node)?.GetPath()} at {p3(hit["position"].AsVector3())}";
        string p3(Vector3 v) => $"{v.X:F1},{v.Y:F1},{v.Z:F1}";
    }

    /// <summary>Walks a place's route to a cell. False (with a note) if there is none or it gets stuck.</summary>
    public IEnumerable<int> To(Place p, Int3 goal, Action<bool> done, Func<bool> stop = null)
    {
        PNav nav = p.Nav();
        Int3 from = Here(p, nav);
        List<Int3> path = nav.FindPath(from, goal, 400_000);
        if (path == null)
        {
            // Up on a piece of furniture (the walker's step takes a low one in its stride): the map has
            // no way down from there, but a person just steps off, to the nearest floor with a route.
            var floor = new List<Int3>();
            for (int dy = -2; dy <= 0; dy++)
            for (int dz = -3; dz <= 3; dz++)
            for (int dx = -3; dx <= 3; dx++)
                if (nav.Standable(from + new Int3(dx, dy, dz))) floor.Add(from + new Int3(dx, dy, dz));
            foreach (Int3 off in floor.OrderBy(c => c.Manhattan(from)))
            {
                if (nav.FindPath(off, goal, 400_000) == null) continue;
                foreach (int x in _drive.WalkTo(Feet(p, off), 0.5f, 6f, sprint: false, dig: false, hop: false)) yield return x;
                from = Here(p, nav);
                path = nav.FindPath(from, goal, 400_000);
                break;
            }
        }
        if (path == null)
        {
            Notes.Add($"no route from {from} to {goal}");
            done(false);
            yield break;
        }
        bool ok = true;
        foreach (int x in Follow(p, path, r => ok = r, stop)) yield return x;
        done(ok);
    }

    /// <summary>The place's blocks with the Hollow Bloom's growths taken away: the way there if they were cut.</summary>
    sealed class Cut : IBlockStructure
    {
        readonly IBlockStructure _g;
        public Cut(IBlockStructure g) => _g = g;
        public Int3 Min => _g.Min;
        public Int3 Size => _g.Size;
        public bool InBounds(Int3 p) => _g.InBounds(p);
        public Block Get(Int3 p) => _g.Get(p) is { Type: BlockType.Growth } ? Block.Air : _g.Get(p);
        public void Set(Int3 p, Block b) => _g.Set(p, b);
        public int CountBlocks() => _g.CountBlocks();
    }

    /// <summary>Growths cut out of the way so far.</summary>
    public int GrowthsCut;

    /// <summary>To a cell, cutting through whatever the Bloom has grown over the way with the survival tool.</summary>
    public IEnumerable<int> CutWay(Place p, Int3 goal, Action<bool> done)
    {
        PNav nav = p.Nav();
        if (nav.FindPath(Here(p, nav), goal, 400_000) is List<Int3>)
        {
            foreach (int x in To(p, goal, done)) yield return x;
            yield break;
        }
        var open = new PNav(new Cut(Place.Routed(p.Memory)), p.Inst.Layout.Occupied, p.Inst.Extras.Lifts, false);
        List<Int3> way = open.FindPath(Here(p, nav), goal, 400_000);
        if (way == null)
        {
            Notes.Add($"no way to {goal} even through the Bloom");
            done(false);
            yield break;
        }
        for (int i = 1; i < way.Count; i++)
        {
            var growths = new[] { way[i], way[i] + Int3.Up, way[i] + Int3.Up * 2 }.Where(c => p.Inst.Blocks.Get(c).Type == BlockType.Growth).ToList();
            if (growths.Count == 0) continue;
            bool ok = true;
            foreach (int x in To(p, way[i - 1], r => ok = r)) yield return x;
            if (!ok) { done(false); yield break; }
            foreach (Int3 g in growths)
            {
                foreach (int x in _drive.HoldPrimary(p.WorldOf(g), 300, () => p.Inst.Blocks.Get(g).IsAir)) yield return x;
                if (p.Inst.Blocks.Get(g).IsAir) GrowthsCut++;
                else Notes.Add($"couldn't cut the growth at {g}: the tool's on '{_game.Tool.TargetLabel}', on a place {_game.Tool.OnPlace}, cell {_game.Tool.PlaceHit?.Cell}");
            }
        }
        foreach (int x in To(p, goal, done)) yield return x;
    }

    /// <summary>Walks a route cell by cell (straight runs merged), opening shut doors on the way.</summary>
    public IEnumerable<int> Follow(Place p, List<Int3> path, Action<bool> done, Func<bool> stop = null)
    {
        if (_game.Menu.IsOpen) { Controls.Tap(Key.Tab); yield return 0; }
        for (int i = 1; i < path.Count; i++)
        {
            if (stop?.Invoke() == true) break;
            Int3 a = path[i - 1], b = path[i];
            Block bb = p.Inst.Blocks.Get(b);
            if (bb.Type == BlockType.Stair || p.Inst.Blocks.Get(a).Type == BlockType.Stair) StairCells++;
            // A door in the way: open it with E if it is shut; squeeze through a jammed one at its gap.
            Place.Door door = p.Doors.FirstOrDefault(d => d.Cells.Contains(b) || d.Cells.Contains(b + Int3.Up));
            if (door != null)
            {
                bool opened = true;
                foreach (int x in Through(p, door, a, r => opened = r)) yield return x;
                if (!opened) { done(false); Release(); yield break; }
                continue;
            }
            // Ladders: up or down the shaft, walking into the rungs.
            if (bb.Type == BlockType.Ladder || p.Inst.Blocks.Get(a).Type == BlockType.Ladder && b.Y != a.Y)
            {
                LadderCells++;
                bool climbed = true;
                foreach (int x in Climb(p, a, b, r => climbed = r)) yield return x;
                if (!climbed) { done(false); Release(); yield break; }
                continue;
            }
            // A lift's stops: straight up or down the shaft, Space or Ctrl.
            if (a.X == b.X && a.Z == b.Z && a.Y != b.Y)
            {
                bool rode = true;
                foreach (int x in Ride(p, b, r => rode = r)) yield return x;
                if (!rode) { Stuck++; Notes.Add($"the lift didn't bring us to {b} (feet {p.FromWorld(_game.Survivor.WorldFeet)}, shaft {p.ShaftAt(p.FromWorld(_game.Survivor.WorldFeet))}, climb {_game.Survivor.Legs.Climb}, floor {_game.Survivor.Body.OnFloor}, from {a})"); done(false); Release(); yield break; }
                continue;
            }
            // Merge a straight run on the level.
            int j = i;
            while (j + 1 < path.Count && path[j + 1].Y == b.Y && path[j].Y == a.Y && Same(path[j + 1] - path[j], b - a)
                   && p.Inst.Blocks.Get(path[j + 1]).Type is not (BlockType.Stair or BlockType.Ladder)
                   && !p.Doors.Any(d => d.Cells.Contains(path[j + 1]) || d.Cells.Contains(path[j + 1] + Int3.Up))) j++;
            int run = j - i; // a large place's halls are long: the time allowed goes by the length of the run
            i = j;
            bool reached = true;
            foreach (int x in Step(Feet(p, path[j]), 0.3f, 6f + run * 0.5f, r => reached = r)) yield return x;
            if (!reached)
            {
                Stuck++;
                Notes.Add($"stuck short of {path[j]} (feet {p.FromWorld(_game.Survivor.WorldFeet)}; in the way: {InTheWay(Feet(p, path[j]))})");
                // Once more, from where we are.
                foreach (int x in Step(Feet(p, path[j]), 0.35f, 6f, r => reached = r)) yield return x;
                if (!reached) { done(false); Release(); yield break; }
            }
        }
        Release();
        done(true);
    }

    static bool Same(Int3 u, Int3 v) => u.X == v.X && u.Z == v.Z;

    void Release()
    {
        Controls.Hold(Key.W, false);
        Controls.Hold(Key.Space, false);
        Controls.Hold(Key.Ctrl, false);
    }

    /// <summary>Walks toward a point (feet) until within a distance on the flat (and near its height), or gives up.</summary>
    public IEnumerable<int> Step(Vector3 to, float within, float seconds, Action<bool> done)
    {
        int ticks = (int)(seconds * 60);
        float best = float.MaxValue, still = 0f;
        for (int t = 0; t < ticks; t++)
        {
            Vector3 feet = _game.Survivor.WorldFeet;
            float flat = new Vector2(to.X - feet.X, to.Z - feet.Z).Length();
            if (flat <= within && MathF.Abs(to.Y - feet.Y) < 1.2f) { done(true); yield break; }
            _drive.Face(new Vector3(to.X, _game.Survivor.WorldEye().Y, to.Z));
            // Easing off short of the mark, as a person does at a corner, so a turn doesn't carry you past it.
            N.Vector3 v = _game.Survivor.Body.Velocity;
            float speed = new Vector2(v.X, v.Z).Length();
            Controls.Hold(Key.W, flat > within + speed * speed / 90f || speed < 1.5f);
            Controls.Hold(Key.Shift, false);
            if (flat < best - 0.05f) { best = flat; still = 0f; }
            else still += 1f / 60f;
            // Caught on a lip: a small hop, as a person would step up.
            Controls.Hold(Key.Space, still > 0.8f && still < 0.95f);
            yield return 0;
        }
        Controls.Hold(Key.W, false);
        Controls.Hold(Key.Space, false);
        done(false);
    }

    /// <summary>Through a doorway: up to it, E if it is shut (and allowed), then through at its middle.</summary>
    IEnumerable<int> Through(Place p, Place.Door d, Int3 before, Action<bool> done)
    {
        Vector3 centre = p.ToWorld(d.Centre);
        float floorY = p.ToWorld(new Vector3(0, d.Cells.Min(c => c.Y), 0)).Y;
        Vector3 here = Feet(p, before);
        Vector3 across = (centre - here) with { Y = 0f };
        Vector3 n = d.Along.Cross(Vector3.Up).Normalized();
        if (n.Dot(across) < 0f) n = -n;
        Vector3 near = new Vector3(centre.X, floorY, centre.Z) - n * 1.2f, far = new Vector3(centre.X, floorY, centre.Z) + n * 1.1f;
        bool ok = true;
        foreach (int x in Step(near, 0.3f, 8f, r => ok = r)) yield return x;
        if (!d.Jammed && d.IsShut)
        {
            foreach (int x in _drive.LookAt(centre, 6)) yield return x;
            foreach (int x in _drive.Tap(Key.E)) yield return x;
            for (int t = 0; t < 120 && d.Open < 0.9f; t++) yield return 0;
            // A shaft door: E called the car from wherever it stood; wait for it, and E again opens the door on it.
            for (int t = 0; d.Lift >= 0 && t < 1800 && d.Open < 0.9f; t++)
            {
                if (t % 60 == 59 && d.Target < 0.5f) foreach (int x in _drive.Tap(Key.E)) yield return x;
                yield return 0;
            }
            if (d.Open < 0.9f)
            {
                Notes.Add($"door {d.Index} would not open ({(d.Clearance != null ? $"locked: {d.Clearance}" : "shut")}, lift {d.Lift}, stop {d.Stop}, target {d.Target:F1}, feet {p.FromWorld(_game.Survivor.WorldFeet)}, in shaft {p.ShaftAt(p.FromWorld(_game.Survivor.WorldFeet))}, aim '{_game.UsePrompt()}')");
                done(false);
                yield break;
            }
            DoorsOpened++;
        }
        // A jammed door's gap has little to spare: line up on it, and back off and line up again if a leaf catches you.
        for (int tries = 0; tries < 3; tries++)
        {
            if (tries > 0 || d.Jammed) foreach (int x in Step(near, 0.1f, 3f, _ => { })) yield return x;
            foreach (int x in Step(far, 0.3f, tries == 0 ? 8f : 5f, r => ok = r)) yield return x;
            if (ok) break;
        }
        string Across() => string.Join(" ", new[] { -0.4f, -0.2f, 0f, 0.2f, 0.4f }.Select(o => { Vector3 s = near + d.Along.Normalized() * o + Vector3.Up * 1f; var r = _game.Zone.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(s, s + n * 2.3f)); return r.Count == 0 ? "clear" : $"{((Vector3)r["position"]).DistanceTo(s):F2}:{(r["collider"].AsGodotObject() as Node)?.Name}"; }));
        if (!ok) Notes.Add($"blocked across the gap: {Across()}; door cells {string.Join(" ", d.Cells.Select(c => $"{c}={p.Inst.Blocks.Get(c).Type}"))}");
        if (!ok) Notes.Add($"could not get through door {d.Index} (jammed {d.Jammed}, open {d.Open:F2}, {d.Width:F1} m wide, leaves at {d.Leaves[0].GlobalPosition} and {d.Leaves[1].GlobalPosition} round {centre}, feet {_game.Survivor.WorldFeet}, from {near} to {far})");
        done(ok);
    }

    /// <summary>Rides a lift's car to a stop; let go, it eases level with the stop.</summary>
    IEnumerable<int> Ride(Place p, Int3 b, Action<bool> done)
    {
        float target = Feet(p, b).Y;
        Key key = target > _game.Survivor.WorldFeet.Y ? Key.Space : Key.Ctrl;
        Controls.Hold(Key.W, false); // the last step leaves it held: on it would walk you out of the shaft on the way up
        for (int t = 0; t < 1800 && MathF.Abs(_game.Survivor.WorldFeet.Y - target) > 0.5f; t++)
        {
            Controls.Hold(key, true);
            yield return 0;
        }
        Controls.Hold(key, false);
        for (int t = 0; t < 120 && MathF.Abs(_game.Survivor.WorldFeet.Y - target) > 0.15f; t++) yield return 0;
        done(MathF.Abs(_game.Survivor.WorldFeet.Y - target) < 0.4f);
    }

    /// <summary>Up or down a ladder: walk into the rungs (W), or step off its top to the cell beside.</summary>
    IEnumerable<int> Climb(Place p, Int3 a, Int3 b, Action<bool> done)
    {
        Vector3 target = Feet(p, b);
        int ticks = 0;
        Block ladder = p.Inst.Blocks.Get(p.Inst.Blocks.Get(b).Type == BlockType.Ladder ? b : a);
        Vector3 wall = p.ToWorld(new Vector3(a.X + 0.5f, a.Y + 1.2f, a.Z + 0.5f)) - Look(ladder.Facing) * 1.0f;
        while (ticks++ < 600)
        {
            Vector3 feet = _game.Survivor.WorldFeet;
            if (MathF.Abs(feet.Y - target.Y) < 0.25f && new Vector2(feet.X - target.X, feet.Z - target.Z).Length() < 0.45f) break;
            bool up = target.Y > feet.Y + 0.1f, down = target.Y < feet.Y - 0.1f;
            if (up || down)
            {
                // Face the rungs and climb (W up, S down).
                _drive.Face(new Vector3(wall.X, _game.Survivor.WorldEye().Y, wall.Z));
                Controls.Hold(Key.W, up);
                Controls.Hold(Key.S, down);
            }
            else
            {
                _drive.Face(new Vector3(target.X, _game.Survivor.WorldEye().Y, target.Z));
                Controls.Hold(Key.W, true);
                Controls.Hold(Key.S, false);
            }
            yield return 0;
        }
        Controls.Hold(Key.W, false);
        Controls.Hold(Key.S, false);
        done(ticks < 600);
    }

    // ---- Over the land -------------------------------------------------------------------------

    /// <summary>
    /// A way over the land a person would walk, worked out as a player picks one by eye: on a 4 m
    /// grid, no step steeper than about 31 degrees, no sea, no water deeper than wading, round cave
    /// mouths (the ground's own density, read straight from the generator) and round other places.
    /// Null if there is none within the search box.
    /// </summary>
    public List<Vector3> Overland(Vector3 from, Vector3 to, PoiSite target) =>
        Overland(from, to, target, 160f, 0.6f, false) ?? Overland(from, to, target, 420f, 0.6f, false) ?? Overland(from, to, target, 420f, 0.85f, false)
        ?? Overland(from, to, target, 420f, 0.85f, true) // swimming a river
        ?? Overland(from, to, target, 420f, 1.4f, true); // and last, up the banks on the jetpack (the walk hops them)

    List<Vector3> Overland(Vector3 from, Vector3 to, PoiSite target, float margin, float slope, bool swim)
    {
        var zone = _game.Zone;
        var src = zone.Planet.Source;
        const float cell = 4f;
        Vector3 lo = new(MathF.Min(from.X, to.X) - margin, 0, MathF.Min(from.Z, to.Z) - margin);
        int nx = (int)((MathF.Max(from.X, to.X) + margin - lo.X) / cell) + 1, nz = (int)((MathF.Max(from.Z, to.Z) + margin - lo.Z) / cell) + 1;
        var height = new Dictionary<int, float>();
        var others = zone.Planet.Pois.Where(s => s != target && _game.Places.DistanceTo(s, (from + to) / 2f) < from.DistanceTo(to)).ToList();
        float H(int i, int j)
        {
            int k = j * nx + i;
            if (height.TryGetValue(k, out float h)) return h;
            float x = lo.X + i * cell, z = lo.Z + j * cell;
            h = zone.GroundAt(x, z).Y;
            bool ok = h >= 1.5f;
            double wx = Planets.Gen.Mathx.WrapX(x, zone.Planet.Width), wz = Planets.Gen.Mathx.Wrap(z, zone.Planet.Height);
            if (ok && !swim && src.WaterSurfaceAt(wx, wz, out float w) && w - h > 0.4f) ok = false;
            // A cave mouth: no ground under the surface the map says is there.
            bool near = new Vector2(x - to.X, z - to.Z).Length() <= 18f || new Vector2(x - from.X, z - from.Z).Length() <= 10f;
            if (ok && !near)
                foreach (float down in new[] { 0.7f, 1.7f, 2.7f })
                    if (src.CellDensity((int)MathF.Floor((float)wx), (int)MathF.Floor(h - down), (int)MathF.Floor((float)wz)) < 0f) { ok = false; break; }
            if (ok && new Vector2(x - to.X, z - to.Z).Length() > 18f && others.Any(s => _game.Places.DistanceTo(s, new Vector3(x, 0, z)) < 4f)) ok = false;
            // Round the place you are walking to, to its way in (a town is walked into from any side).
            if (ok && target?.Instance.Townsfolk == null && target != null && new Vector2(x - to.X, z - to.Z).Length() > 18f && _game.Places.DistanceTo(target, new Vector3(x, 0, z)) < 2f) ok = false;
            return height[k] = ok ? h : float.NaN;
        }
        (int, int) Cell(Vector3 p) => ((int)MathF.Round((p.X - lo.X) / cell), (int)MathF.Round((p.Z - lo.Z) / cell));
        var start = Cell(from);
        var goal = Cell(to);
        var open = new PriorityQueue<(int, int), float>();
        var cost = new Dictionary<(int, int), float> { [start] = 0f };
        var came = new Dictionary<(int, int), (int, int)>();
        open.Enqueue(start, 0f);
        int expanded = 0;
        while (open.TryDequeue(out var cur, out _))
        {
            if (Math.Abs(cur.Item1 - goal.Item1) <= 1 && Math.Abs(cur.Item2 - goal.Item2) <= 1)
            {
                var path = new List<Vector3> { to };
                for (var c = cur; came.ContainsKey(c); c = came[c]) path.Add(new Vector3(lo.X + c.Item1 * cell, H(c.Item1, c.Item2), lo.Z + c.Item2 * cell));
                path.Reverse();
                return path;
            }
            if (++expanded > 200_000) break;
            float hc = cur == start ? from.Y : H(cur.Item1, cur.Item2);
            for (int dj = -1; dj <= 1; dj++)
            for (int di = -1; di <= 1; di++)
            {
                if (di == 0 && dj == 0) continue;
                int i = cur.Item1 + di, j = cur.Item2 + dj;
                if (i < 0 || j < 0 || i >= nx || j >= nz) continue;
                float h = H(i, j);
                if (float.IsNaN(h)) continue;
                float run = cell * (di != 0 && dj != 0 ? 1.414f : 1f);
                if (MathF.Abs(h - hc) > run * slope) continue;
                float c = cost[cur] + run + MathF.Abs(h - hc) * 3f;
                var n = (i, j);
                if (cost.TryGetValue(n, out float old) && old <= c) continue;
                cost[n] = c;
                came[n] = cur;
                open.Enqueue(n, c + new Vector2(i - goal.Item1, j - goal.Item2).Length() * cell);
            }
        }
        Notes.Add($"no way over the land ({expanded} cells searched, {height.Count} probed, {height.Values.Count(float.IsNaN)} refused; start {start} {H(start.Item1, start.Item2):F1}, goal {goal} {H(goal.Item1, goal.Item2):F1})");
        return null;
    }

    static Vector3 Look(Dir d)
    {
        Int3 v = d.Vec();
        return new Vector3(v.X, v.Y, v.Z);
    }
}
