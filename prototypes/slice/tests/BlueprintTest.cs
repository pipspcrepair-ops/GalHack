using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using GalHack.Items;
using GalHack.Items.Bridge;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;
using ShipHandling.Handling;
using Slice.Construction;
using Slice.Game;
using Slice.Player;
using Slice.Vessels;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;

namespace Slice.Tests;

/// <summary>
/// The stock blueprints (<see cref="BlueprintSet"/>) against the 1 m grid's standards (owner, 12 Nov). Pure first, for
/// every file: it is what the set writes now (--write rewrites them); it loads, everything joined to the core; rooms and
/// corridors a person walks are 3 m clear and 2 m wide; doors stand upright across their doorways, set in a wall, with a
/// floor on both sides, people doors 1x2 or 2x2; a chair or cockpit is reachable on foot from the ground outside (on the
/// structure's walk grid: half-metre steps and stairs, 1.8 m of headroom); power covers full load with fuel for an hour;
/// a craft lifts 1.3 times its weight on its thrusters or plates, pushes and brakes, turns, every exhaust is clear and
/// its lift is balanced round its centre of mass. Then on a fresh random world, each one set down whole by Creative's
/// PLACE: the sleeper sits in its chair (drawn, the camera at the chair's eye), and a craft takes off, hovers, flies,
/// brakes and lands under the game's own keys (a hover craft runs and stops).
///
/// Run: G:\GalHack\.tools\queue\test-q.ps1 -Project prototypes/slice -Scene res://tests/BlueprintTest.tscn [-Extra --write]
/// </summary>
public partial class BlueprintTest : Node
{
    Game.Game _game;
    Driver _drive;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();

    /// <summary>The cheapest flyer's ceiling, ore (owner: every world is guaranteed the ores for it).</summary>
    const int StarterOre = 600;

    public override void _Ready()
    {
        ProcessPhysicsPriority = 100;
        _run = Scenario().GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        bool more;
        try { more = _run.MoveNext(); }
        catch (Exception e)
        {
            Check(false, $"scenario threw: {e}");
            more = false;
        }
        if (more) return;
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {_clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        SetPhysicsProcess(false);
        Wildlife.Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    bool Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
        return ok;
    }

    static string Repo => Path.GetFullPath(Path.Combine(GameData.PrototypesRoot, ".."));

    IEnumerable<int> Scenario()
    {
        GameData data = GameData.Shared;
        bool write = OS.GetCmdlineUserArgs().Contains("--write");
        var plans = new List<(string File, Structure Plan)>();
        foreach (BlueprintSet.Design d in BlueprintSet.All)
        {
            string path = Path.Combine(Repo, d.File), text = d.Make(data.Catalogue);
            if (write) File.WriteAllText(path, text, new System.Text.UTF8Encoding(false));
            else Check(File.Exists(path) && File.ReadAllText(path).Replace("\r\n", "\n") == text, $"{d.File} is what the set writes (else run with --write)");
            Structure s = Blueprint.Load(text, data.Catalogue, out List<string> errors);
            if (!Check(s != null, $"{d.File} loads, every block joined to the core{(errors.Count > 0 ? ": " + string.Join("; ", errors.Take(4)) : "")}")) continue;
            s.DirtyChunks.Clear();
            if (!d.Fixture) plans.Add((d.File, s));
        }
        foreach (var (file, plan) in plans) Standards(data, file, plan);

        _game = new Game.Game { Interactive = false, SkipIntro = true, World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Tested) };
        AddChild(_game);
        _drive = new Driver(_game);
        foreach (int x in _drive.UntilPlaying()) yield return x;
        _game.Zone.Weather.Bridge.Calm = true;
        _game.Zone.Wildlife.Frozen = true;
        _game.Mode = GameMode.Creative;
        foreach (var (_, plan) in plans)
            foreach (int x in InWorld(plan)) yield return x;
    }

    // ---- The standards, on the plan ------------------------------------------------------------------

    void Standards(GameData data, string file, Structure s)
    {
        string name = s.Name;
        StructureStats st = StructureStats.Compute(s);
        var (min, max) = s.Bounds();
        Int3 size = max - min + Footprint.One;
        double ore = data.Items.Graph.Expand(BlueprintLibrary.Cost(data.Items, s)).Leaves.Total;
        bool craft = s.Mode != CoreMode.Base;
        var walk = new WalkGrid(s);
        GD.Print($"-- {name} ({Path.GetFileName(file)}): {size.X} x {size.Y} x {size.Z} m, {s.BlockCount} blocks, {st.Mass:F1} t, {ore:0} ore, {s.Mode} --");

        // Rooms and corridors: 3 m clear, 2 m wide, wherever a person walks under a roof.
        // Rooms are walked under a roof on the structure itself (the ground under an overhang is not a room).
        var inside = walk.Reached.Where(n => walk.Roofed(n) && !walk.OnGround(n)).ToList();
        var low = inside.Where(n => !walk.OnStairsOrDoor(n) && walk.Clear(n) < 6).ToList();
        var narrow = inside.Where(n => !walk.Wide(n)).ToList();
        int heads = inside.Where(n => !walk.OnStairsOrDoor(n)).Select(walk.Clear).DefaultIfEmpty(0).Min();
        Check(low.Count == 0, $"{name}: every room and corridor walked is at least 3 m clear (lowest {heads * 0.5f:F1} m{(low.Count > 0 ? $", too low over {low.Count} cells such as {low[0]}" : "")})");
        Check(narrow.Count == 0, $"{name}: every corridor walked is at least 2 m wide{(narrow.Count > 0 ? $" ({narrow.Count} cells narrower, such as {narrow[0]})" : "")}");

        // Doors: upright, across the doorway in a wall, a floor either side; people doors 1x2 or 2x2.
        foreach (var (p, b) in s.Blocks().Where(x => s.TypeOf(x.block).IsDoor))
        {
            string problem = walk.DoorProblem(p, b);
            BlockType t = s.TypeOf(b);
            Check(problem == null, $"{name}: {t.Id} at {p} stands across its doorway{(problem != null ? ": " + problem : "")}");
        }

        // A seat, reachable on foot from outside.
        var chairs = s.Blocks().Where(x => s.TypeOf(x.block).IsCommandChair).ToList();
        Check(chairs.Count > 0 && chairs.All(c => walk.SeatReached(c.pos, c.block)),
            $"{name}: {chairs.Count} chair(s) ({string.Join(", ", chairs.Select(c => s.TypeOf(c.block).Id).Distinct())}), each reached on foot from the ground outside");

        // Power and fuel.
        double hours = st.PowerUse > 0 ? st.Fuel / (1000 * st.PowerUse) : double.PositiveInfinity;
        Check(st.Powered && st.PowerOut >= 1.2 * st.PowerUse && hours >= 1,
            $"{name}: {st.PowerOut:F2} MW out against {st.PowerUse:F2} MW at full load, fuel for {hours:F1} h of it");
        if (!craft)
        {
            var machines = BuildingBridge.MachinesOn(data.Items, s).Select(m => m.Machine.Name).ToList();
            Check(st.Containers > 0, $"{name}: storage ({st.StorageCapacity:0} SU); machines: {(machines.Count > 0 ? string.Join(", ", machines) : "none")}");
            return;
        }

        // A craft: lift, push, brake, turn on the start world's gravity.
        float g = data.Rules.Planet.Gravity;
        bool hover = s.Mode == CoreMode.Hover;
        HandlingStats h = HandlingModel.Compute(new StructureVessel(s, data.Handling), new FlightConditions(g, true), data.Handling);
        float lift = h.UpwardThrust / MathF.Max(1e-3f, h.Weight);
        Check(lift >= 1.3f, $"{name}: {(hover ? "hover plates" : "lift thrusters")} hold {lift:F2} times its {h.Mass:F1} t on {g} m/s² ({h.LiftMargin:+0;-0} kN spare)");
        float side = MathF.Min(h.Accel(Direction.Left), h.Accel(Direction.Right));
        Check(h.Accel(Direction.Forward) >= 5f && h.Accel(Direction.Back) >= 5f && (hover || side >= 2f) && h.TurnRate >= data.Handling.Turning.Floor,
            $"{name}: pushes {h.Accel(Direction.Forward):F1}, brakes {h.Accel(Direction.Back):F1}, slides {side:F1} m/s², turns {h.TurnRate:F0}°/s");
        if (file.EndsWith("starter.gbp")) Check(ore <= StarterOre, $"{name}: the cheapest flyer costs {ore:0} ore (at most {StarterOre})");

        // Every exhaust, plate and pad face clear (a pad's foot reaches down to attach); the lift balanced round the centre of mass.
        var blocked = new List<string>();
        N.Vector3 liftAt = N.Vector3.Zero;
        float liftSum = 0f;
        foreach (var (p, b) in s.Blocks())
        {
            BlockType t = s.TypeOf(b);
            if (!t.IsThruster && t.Function != "hover" && t.Function != "gear") continue;
            Dir away = Orientations.ToWorld(b.Rot, t.IsThruster ? Dir.PosZ : Dir.NegY);
            var cells = s.CellsOf(p, b).ToList();
            int face = cells.Max(c => Int3.Dot(c, away.Vec()));
            if (cells.Any(c => Int3.Dot(c, away.Vec()) == face && s.Has(c + away.Vec()))) blocked.Add($"{t.Id} at {p}");
            bool lifts = hover ? t.Function == "hover" : t.IsThruster && away == Dir.NegY;
            if (!lifts) continue;
            float f = hover ? 1f : t.Thrust;
            liftAt += s.CentreOf(p, b) * f;
            liftSum += f;
        }
        Check(blocked.Count == 0, $"{name}: every exhaust{(hover ? " and plate" : " and pad")} faces open air{(blocked.Count > 0 ? ": blocked " + string.Join(", ", blocked.Take(4)) : "")}");
        N.Vector3 com = StructureBridge.Mass(s).CenterOfMass / s.Grid.CellSize;
        liftAt /= MathF.Max(liftSum, 1e-3f);
        float off = new N.Vector2(com.X - liftAt.X, com.Z - liftAt.Z).Length() * s.Grid.CellSize;
        var pads = s.Blocks().Where(x => s.TypeOf(x.block).Function == "gear").SelectMany(x => s.CellsOf(x.pos, x.block)).ToList();
        // It rests on its pads, or on its keel where it has none (building's example has no pads).
        if (pads.Count == 0) pads = s.Cells().Select(c => c.Item1).Where(c => c.Y == min.Y).ToList();
        bool stands = hover || com.X >= pads.Min(c => c.X) && com.X <= pads.Max(c => c.X) && com.Z >= pads.Min(c => c.Z) && com.Z <= pads.Max(c => c.Z);
        Check(off <= 0.75f && stands, $"{name}: its lift acts {off:F2} m from the centre of mass{(hover ? "" : ", which stands inside its pads")} (mass at x {com.X:F1} z {com.Z:F1}, lift at x {liftAt.X:F1} z {liftAt.Z:F1})");
    }

    // ---- In the world ------------------------------------------------------------------------------------

    IEnumerable<int> InWorld(Structure plan)
    {
        string name = plan.Name;
        Vessel v = null;
        string why = null;
        var (lo, hi) = plan.Bounds();
        float radius = 0.5f * new Vector2(hi.X - lo.X + 1, hi.Z - lo.Z + 1).Length();
        var play = new Playthrough(_game);
        for (int attempt = 0; attempt < 5 && v == null; attempt++)
        {
            Vector3 spot = play.FlatSpot(_game.Survivor.WorldFeet, 60f + 40f * attempt, attempt * 1.3f, 0.5f + attempt * 0.5f, span: MathF.Max(14f, radius));
            Vector3 stand = spot - Vector3.Forward * (radius + 4f);
            _game.Survivor.PlaceOutside((_game.Zone.GroundAt(stand.X, stand.Z) + Vector3.Up * 0.2f).ToNumerics());
            foreach (int x in _drive.UntilSettled(20)) yield return x;
            _game.Survivor.Yaw = 0f;
            _game.Survivor.Pitch = 0f;
            foreach (int x in _drive.Wait(30)) yield return x;
            WholePlacement job = _game.Builder.PlaceWhole(plan);
            for (int t = 0; t < 600 && !job.Done; t++) yield return 0;
            v = job.Vessel;
            why = job.Why ?? (job.Done ? why : "still placing after 10 s");
            for (int t = 0; t < 300 && v != null && (v.PendingShapes > 0 || v.View.Waiting > 0); t++) yield return 0;
        }
        if (!Check(v != null, $"{name}: set down whole by Creative's PLACE{(v == null ? $" (five spots refused, the last: {why})" : "")}")) yield break;
        // PLACE fells what stands in its box and its head room; the rest stands. A crown met higher up or on the flight leg
        // breaks under a craft its trunk can't take, and holds or turns aside a light one (vessels collide with flora).
        GD.Print($"    ({name}: {CrownsOver(v)} crown(s) over it above PLACE's head room)");
        // Woken where it stands, as anything near it changing wakes it: hanging over the ground, it would fall now.
        float placedAt = v.Body.Position.Y;
        v.Controller?.Wake(v.Body);
        foreach (int x in _drive.Wait(30)) yield return x;
        for (int t = 0, still = 0; t < 300 && still < 10; t++)
        {
            still = v.Body.Velocity.Length() < 0.1f ? still + 1 : 0;
            yield return 0;
        }
        // PLACE fells the plants under it before setting it down on the ground's collision: it drops nowhere, loses nothing.
        float drop = placedAt - v.Body.Position.Y;
        Check(drop < 0.3f && v.Structure.BlockCount == plan.BlockCount && plan.Blocks().All(b => v.Structure.Has(b.pos)) && v.Chairs.Any(),
            $"{name}: stands where PLACE set it down ({drop:F2} m lower), every block and its chair kept ({v.Structure.BlockCount} of {plan.BlockCount})");

        // Seated: drawn, the camera at the chair's eye.
        _game.ThirdPerson = false;
        bool sat = _game.Survivor.Sit(v);
        foreach (int x in _drive.Wait(30)) yield return x;
        Camera3D cam = _game.Camera;
        float eyeOff = sat ? cam.GlobalPosition.DistanceTo(v.DrawTransform * v.Eye.ToGodot()) : float.NaN;
        Check(sat && _game.Figure.Visible && cam.GetCullMaskValue(CharacterModel.BodyLayer) && eyeOff < 0.03f,
            $"{name}: seated in its chair, the figure drawn, the camera at the chair's eye ({eyeOff * 100f:F1} cm off)");

        if (plan.Mode == CoreMode.Flight) foreach (int x in Fly(v)) yield return x;
        else if (plan.Mode == CoreMode.Hover) foreach (int x in Drive(v)) yield return x;

        _game.Survivor.StandUp();
        foreach (int x in _drive.Wait(5)) yield return x;
        _game.Survivor.PlaceOutside((v.Body.Position + N.Vector3.UnitY * 30f).ToGodot().ToNumerics());
        Playthrough.TakeAway(_game.Zone, v);
        foreach (int x in _drive.Wait(10)) yield return x;
    }

    /// <summary>The hull's height over the terrain under it, trees and all else left out (the probe under a vessel meets a canopy).</summary>
    float Height(Vessel v) => v.Body.Position.Y - _game.Zone.GroundAt(v.Body.Position.X, v.Body.Position.Z).Y;

    /// <summary>Tree crowns still standing over a structure's footprint, higher than its top.</summary>
    int CrownsOver(Vessel v)
    {
        var (lo, hi) = v.Structure.Bounds();
        float reach = 0.5f * v.Structure.Grid.CellSize * MathF.Max(hi.X - lo.X + 1, hi.Z - lo.Z + 1);
        float top = v.Body.Position.Y + v.Body.Shape.Height - v.Body.Shape.FeetBelowOrigin;
        return _game.Zone.Streamer.SolidsNear(v.Body.Position.ToGodot(), reach, canopies: true).Count(s => s.solid.IsCanopy && s.solid.Center.Y + s.offset.Y > top);
    }

    /// <summary>Take off, hover hands off, fly forward, let go and stop, land: the game's own keys.</summary>
    IEnumerable<int> Fly(Vessel v)
    {
        string name = v.Structure.Name;
        Controls c = _game.Controls;
        float ground = Height(v);
        c.Hold(Key.Space);
        int t = 0;
        for (; t < 600 && Height(v) < ground + 8f; t++) yield return 0;
        c.Hold(Key.Space, false);
        Check(Height(v) >= ground + 8f, $"{name}: takes off and climbs 8 m in {t / 60f:F1} s{(Height(v) < ground + 8f ? $" (from {ground:F1} to {Height(v):F1} m over the ground; console: {v.Controller?.ConsoleMessage}; pilot {v.Roles.Pilot}, lift margin {v.Stats?.LiftMargin:F0} kN)" : "")}");

        Vector3 start = v.Body.Position.ToGodot();
        for (t = 0; t < 600 && MathF.Abs(v.Body.Velocity.Y) > 0.3f; t++) yield return 0;
        float held = Height(v);
        foreach (int x in _drive.Wait(120)) yield return x;
        Check(MathF.Abs(Height(v) - held) < 1f && MathF.Abs(v.Body.Velocity.Y) < 0.5f,
            $"{name}: hands off, the climb stops in {t / 60f:F1} s and it hovers ({Height(v) - held:+0.0;-0.0} m in 2 s)");

        c.Hold(Key.W);
        foreach (int x in _drive.Wait(60)) yield return x;
        c.Hold(Key.W, false);
        float fast = v.Body.Velocity.Length();
        for (t = 0; t < 900 && v.Body.Velocity.Length() > 1f; t++) yield return 0;
        Check(fast > 5f && t < 60 * (fast / 6f + 3f), $"{name}: flies forward to {fast:F0} m/s in 1 s and stops within {t / 60f:F1} s of letting go");

        // Back over the ground it rose from (cleared when it was set down: elsewhere it may meet trees), then down:
        // Ctrl to sink, slower near the ground, hands off once it stops sinking.
        var pilot = new Pilot(_game, _drive);
        for (t = 0; t < 60 * 60; t++)
        {
            Vector3 p = v.Body.Position.ToGodot();
            if (new Vector2(p.X - start.X, p.Z - start.Z).Length() < 1.5f && v.Body.Velocity.Length() < 0.5f) break;
            pilot.Creep(v, start, true);
            yield return 0;
        }
        c.ReleaseAll();
        var heights = new Queue<float>();
        bool keel = false; // a hull with no pads: down on its keel at its rest height, not sinking
        for (t = 0; t < 120 * 60 && !v.Controller.Frozen; t++)
        {
            // The sink rate it really makes over half a second (pressed onto the ground, the flight model still asks for some).
            float agl = Height(v) - ground;
            heights.Enqueue(agl);
            float wanted = agl > 25f ? -6f : agl > 6f ? -2.5f : -1f;
            float vy = heights.Count > 30 ? (agl - heights.Dequeue()) * 2f : wanted;
            // Down on whatever it came to (the ground, or a tree that takes it): hands off, and it lands there.
            bool down = heights.Count >= 30 && MathF.Abs(vy) < 0.2f;
            if (down && agl < 0.6f && !v.Structure.Blocks().Any(b => v.Structure.TypeOf(b.block).Function == "gear")) { keel = true; break; }
            float now = v.Body.Velocity.Y; // steer on the hull's own velocity, judge touchdown on the height
            c.Hold(Key.Ctrl, !down && now > wanted + 0.5f);
            c.Hold(Key.Space, !down && now < 2f * wanted - 1f);
            if (t % 600 == 599) GD.Print($"    ({name} landing: {agl:F1} m over its rest height, sinking {-vy:F1} m/s, drifting {new Vector2(v.Body.Velocity.X, v.Body.Velocity.Z).Length():F1} m/s)");
            yield return 0;
        }
        c.ReleaseAll();
        float up = MathX.Up(v.Body.Rotation).Y;
        // A hull with no pads rests on its keel: it lands when it is back at its rest height and still.
        bool padless = !v.Structure.Blocks().Any(b => v.Structure.TypeOf(b.block).Function == "gear");
        bool landed = v.Controller.Frozen || padless && keel;
        Check(landed && up > 0.97f, $"{name}: lands and settles upright (up {up:F3}, {Height(v):F1} m over the ground, {new Vector2(v.Body.Position.X - start.X, v.Body.Position.Z - start.Z).Length():F0} m from the spot)");
    }

    /// <summary>A hover craft runs forward on its plates and stops when let go.</summary>
    IEnumerable<int> Drive(Vessel v)
    {
        string name = v.Structure.Name;
        Controls c = _game.Controls;
        foreach (int x in _drive.Wait(60)) yield return x;
        N.Vector3 from = v.Body.Position;
        c.Hold(Key.W);
        foreach (int x in _drive.Wait(120)) yield return x;
        c.Hold(Key.W, false);
        float Flat() => new Vector2(v.Body.Velocity.X, v.Body.Velocity.Z).Length();
        float fast = Flat(), ran = new Vector2(v.Body.Position.X - from.X, v.Body.Position.Z - from.Z).Length();
        int t = 0;
        for (; t < 900 && Flat() > 1f; t++) yield return 0;
        Check(ran > 5f && t < 60 * (fast / 5f + 3f), $"{name}: rides its plates {ran:F0} m in 2 s, up to {fast:F0} m/s, and stops within {t / 60f:F1} s (velocity {v.Body.Velocity}, {v.AboveGround:F1} m over the ground, up {MathX.Up(v.Body.Rotation).Y:F2})");
    }
}

/// <summary>
/// Where a person can stand and walk on a structure, in half metres: on the top of a full block, a half block or a step of
/// a stair, with 1.8 m clear overhead, stepping up or down at most 0.5 m to the next cell, and on the ground under its
/// lowest cells all round it (a structure stands on its lowest face). Open doors, lights and ladders are walked through.
/// </summary>
public sealed class WalkGrid
{
    readonly Structure _s;
    readonly int _ground;
    /// <summary>Standing places: column and height in half metres (a cell spans 2y-1 to 2y+1).</summary>
    public readonly HashSet<(int X, int Z, int H)> Nodes = new(), Reached = new();
    const int Headroom = 4; // 1.8 m, in half metres, rounded up

    public WalkGrid(Structure s)
    {
        _s = s;
        var (min, max) = s.Bounds();
        _ground = 2 * min.Y - 1;
        for (int x = min.X - 2; x <= max.X + 2; x++)
            for (int z = min.Z - 2; z <= max.Z + 2; z++)
            {
                if (Fits(x, z, _ground, null)) Nodes.Add((x, z, _ground));
                for (int y = min.Y; y <= max.Y; y++)
                {
                    var c = new Int3(x, y, z);
                    if (!s.TryGetBlock(c, out Int3 anchor, out Block b)) continue;
                    BlockType t = s.TypeOf(b);
                    if (t.Function == "stairs")
                    {
                        if (Fits(x, z, 2 * y, c)) Nodes.Add((x, z, 2 * y));
                        if (Fits(x, z, 2 * y + 1, c)) Nodes.Add((x, z, 2 * y + 1));
                    }
                    else if (Floor(c, anchor, b) is int top && Fits(x, z, top, null)) Nodes.Add((x, z, top));
                }
            }
        // Walk from the ground outside, at its corner.
        var open = new Queue<(int, int, int)>();
        var start = (min.X - 2, min.Z - 2, _ground);
        if (Nodes.Contains(start)) { Reached.Add(start); open.Enqueue(start); }
        while (open.Count > 0)
        {
            var (x, z, h) = open.Dequeue();
            foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1), (0, 0) })
                for (int dh = -1; dh <= 1; dh++)
                {
                    var n = (x + dx, z + dz, h + dh);
                    if ((dx, dz, dh) == (0, 0, 0) || !Nodes.Contains(n) || !Reached.Add(n)) continue;
                    open.Enqueue(n);
                }
        }
    }

    /// <summary>What a person can't share a cell with: anything but an open way (a door, a light, a ladder).</summary>
    bool Blocks(Int3 c) => _s.TryGetBlock(c, out _, out Block b) && !(_s.TypeOf(b).IsDoor || _s.TypeOf(b).IsLight || _s.TypeOf(b).IsLadder);

    /// <summary>The height a block's cell can be stood on, in half metres, or null (a slope, a part's insides, a door).</summary>
    int? Floor(Int3 c, Int3 anchor, Block b)
    {
        BlockType t = _s.TypeOf(b);
        if (t.IsArmor)
        {
            if (_s.AttachCoverage(b, Dir.PosY) == Coverage.Full) return 2 * c.Y + 1;
            return _s.ShapeOf(b).Id == "half" && Orientations.ToWorld(b.Rot, Dir.PosY) == Dir.PosY ? 2 * c.Y : null;
        }
        // A device's top, where its cell is the top of the block.
        if (t.Collide != Collide.Cell || t.IsDoor) return null;
        return _s.TryGetBlock(c + Int3.UnitY, out Int3 above, out _) && above == anchor ? null : 2 * c.Y + 1;
    }

    /// <summary>A person stands at this height in the column with 1.8 m clear (a stair's own cell excepted).</summary>
    bool Fits(int x, int z, int h, Int3? own)
    {
        for (int y = (int)MathF.Floor((h + 1) / 2f); 2 * y - 1 < h + Headroom; y++)
        {
            var c = new Int3(x, y, z);
            if (c != own && 2 * y + 1 > h && Blocks(c)) return false;
        }
        return true;
    }

    /// <summary>On the ground round the structure, not on the structure.</summary>
    public bool OnGround((int X, int Z, int H) n) => n.H == _ground;

    /// <summary>Under a roof: something above it in its column.</summary>
    public bool Roofed((int X, int Z, int H) n)
    {
        var (_, max) = _s.Bounds();
        for (int y = (n.H + 1) / 2; y <= max.Y; y++)
            if (Blocks(new Int3(n.X, y, n.Z))) return true;
        return false;
    }

    /// <summary>Clear height over a place to the first thing above it, half metres.</summary>
    public int Clear((int X, int Z, int H) n)
    {
        var (_, max) = _s.Bounds();
        for (int y = (int)MathF.Ceiling(n.H / 2f); y <= max.Y; y++)
            if (2 * y - 1 >= n.H && Blocks(new Int3(n.X, y, n.Z))) return 2 * y - 1 - n.H;
        return int.MaxValue;
    }

    /// <summary>In a doorway or on a stair, where the room rules are let off.</summary>
    public bool OnStairsOrDoor((int X, int Z, int H) n)
    {
        int y = (n.H + 1) / 2;
        bool Is(Int3 c, Func<BlockType, bool> f) => _s.TryGetBlock(c, out _, out Block b) && f(_s.TypeOf(b));
        return Is(new Int3(n.X, y, n.Z), t => t.IsDoor) || Is(new Int3(n.X, (int)MathF.Floor(n.H / 2f), n.Z), t => t.Function == "stairs")
            || Is(new Int3(n.X, y - 1, n.Z), t => t.Function == "stairs");
    }

    /// <summary>In a 2 x 2 square of places at its height (doorways and stairs are let off: a door is as wide as it is).</summary>
    public bool Wide((int X, int Z, int H) n)
    {
        if (OnStairsOrDoor(n)) return true;
        foreach (var (dx, dz) in new[] { (0, 0), (-1, 0), (0, -1), (-1, -1) })
        {
            bool all = true;
            for (int i = 0; i < 2 && all; i++)
                for (int k = 0; k < 2 && all; k++) all = Reached.Contains((n.X + dx + i, n.Z + dz + k, n.H));
            if (all) return true;
        }
        return false;
    }

    /// <summary>Why a door doesn't stand across its doorway, or null.</summary>
    public string DoorProblem(Int3 anchor, Block b)
    {
        BlockType t = _s.TypeOf(b);
        if (Orientations.ToWorld(b.Rot, Dir.PosY) != Dir.PosY) return "not upright";
        if (t.Size.Z != 1) return "thicker than a cell";
        Int3 through = Orientations.ToWorld(b.Rot, Dir.NegZ).Vec(), across = Orientations.ToWorld(b.Rot, Dir.PosX).Vec();
        var cells = _s.CellsOf(anchor, b).ToList();
        int bottom = cells.Min(c => c.Y);
        foreach (Int3 c in cells.Where(c => c.Y == bottom))
            foreach (Int3 side in new[] { c + through, c - through })
                if (!Reached.Contains((side.X, side.Z, 2 * bottom - 1))) return $"no floor walked to at {side}";
        foreach (Int3 c in cells)
            foreach (Int3 flank in new[] { c + across, c - across })
                if (!cells.Contains(flank) && !(_s.TryGetBlock(flank, out _, out Block w) && _s.TypeOf(w).IsArmor)) return $"no wall beside it at {flank}";
        bool people = t.Size.X <= 2 && t.Size.Y == 2;
        if (!people && t.Size.Y <= 2) return $"a {t.Size.X}x{t.Size.Y} door is neither a people door (1x2, 2x2) nor a vehicle opening";
        return null;
    }

    /// <summary>A chair is reached when someone can stand beside one of its cells, on its floor.</summary>
    public bool SeatReached(Int3 anchor, Block b)
    {
        var cells = _s.CellsOf(anchor, b).ToList();
        int floor = 2 * cells.Min(c => c.Y) - 1;
        foreach (Int3 c in cells)
            foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                for (int dh = -1; dh <= 1; dh++)
                    if (Reached.Contains((c.X + dx, c.Z + dz, floor + dh))) return true;
        return false;
    }
}
