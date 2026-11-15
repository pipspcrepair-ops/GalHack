using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Arms;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;
using Slice.Game;
using Slice.Items;
using Slice.Player;
using Slice.Vessels;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;
using FieldRules = Slice.Game.SliceRules.FieldRules;
using FieldTier = Slice.Game.SliceRules.FieldTier;

namespace Slice.Tests;

/// <summary>
/// Force fields (design section 12), on a fresh random world, through the game's own keys where a
/// player's hands would do it. A room on a base with an opening leaks; a force field component in
/// hand is pressed on the opening's frame and dragged: red while it can't go (too small, its far
/// corner touching nothing), blue when it can; released, it spans the opening with an L-shaped
/// emitter in each corner and uses one component. It holds the air, draws tiles x the per-tile rate,
/// lets the sleeper walk through and stops shots. Plated emitters take less from a hit. Knocked to
/// zero it vanishes with its emitters whole and grows back after its tier's delay. An open outer
/// door holds the air while the room is powered; power off, neither field holds. Taking an emitter
/// down takes the field down, the component coming back. The sheet meets its emitters' outer edges
/// (and a door's sheet its frame) within a millimetre. The sleeper walks through an emitter; a shot
/// meets it, and one shot until it breaks destroys all four and the field for good.
///
/// Run: G:\GalHack\.tools\queue\test-q.ps1 -Project prototypes/slice -Scene res://tests/ForceFieldTest.tscn
/// </summary>
public partial class ForceFieldTest : Node
{
    Game.Game _game;
    Driver _drive;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();

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

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
    }

    IEnumerable<int> Scenario()
    {
        _game = new Game.Game { Interactive = false, SkipIntro = true, World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Tested) };
        AddChild(_game);
        _drive = new Driver(_game);
        var play = new Playthrough(_game);
        foreach (int x in _drive.UntilPlaying()) yield return x;
        _game.Zone.Weather.Bridge.Calm = true;
        _game.Zone.Wildlife.Frozen = true;
        foreach (int x in play.MakeTool()) yield return x;

        FieldRules rules = _game.Zone.Data.Rules.ForceFields;
        Tables(rules);

        FieldRig rig = FieldRig.Build(_game, play.FlatSpot(_game.Start, 30f, span: 12f));
        Vessel v = rig.Room;
        Structure s = v.Structure;
        var pack = _game.Pack;
        var builder = _game.Builder;
        Controls c = _game.Controls;
        foreach (int x in _drive.Wait(30)) yield return x;
        Check(v.Powered && !rig.Sealed(), $"the room, powered by its generator, leaks through its 4 x 3 opening ({v.Rooms.Sealed.Count()} sealed rooms)");
        Flush(s, rules);

        // ---- Placing by dragging --------------------------------------------------------------
        const string T1 = "force-field-t1";
        pack.Add(T1, 2);
        int slot = Enumerable.Range(0, PlayerInventory.ToolbarSlots).FirstOrDefault(i => pack.Grid[i].Item == T1, -1);
        if (slot < 0)
        {
            slot = 2;
            foreach (int x in play.ToToolbar(T1, slot)) yield return x;
            foreach (int x in play.CloseMenu()) yield return x;
        }
        foreach (int x in play.Select(slot)) yield return x;
        Check(pack.Hand == HandKind.Device && pack.HandItem == T1, $"a force field component in hand ({pack.Hand}, {pack.HandItem})");
        StandOutside(rig);
        foreach (int x in _drive.Wait(30)) yield return x;
        Vector3 frame = rig.World(1.5f, 1f, 0f); // the opening's frame, its inside face at the lower left corner
        foreach (int x in _drive.LookAt(frame, 8)) yield return x;
        Check(builder.HasTarget && builder.TargetVessel == v && builder.TargetCell == new Int3(1, 1, 0) && builder.PlaceCell == FieldRig.OpeningMin,
            $"aimed at the frame's inside face: block {builder.TargetCell}, the first corner {builder.PlaceCell} (\"{builder.Verdict}\")");
        c.PrimaryPressed = c.Primary = true;
        _drive.Face(frame);
        yield return 0;
        Check(builder.Drawing && !builder.DrawnCanGo && builder.Verdict.Contains("AT LEAST"), $"pressed: one cell, red: \"{builder.Verdict}\"");
        IEnumerable<int> DragTo(Vector3 to)
        {
            for (int i = 0; i < 12; i++)
            {
                c.Primary = true;
                _drive.Face(to);
                yield return 0;
            }
        }
        foreach (int x in DragTo(rig.World(4f, 2f, 0f))) yield return x;
        Check(builder.Drawing && !builder.DrawnCanGo && builder.Verdict.Contains("FAR CORNER"),
            $"dragged to the opening's middle, the far corner touches no block: red, \"{builder.Verdict}\"");
        foreach (int x in DragTo(rig.World(5f, 3f, 0f))) yield return x;
        Check(builder.Drawing && builder.DrawnCanGo && builder.Verdict.Contains("4 × 3") && builder.Verdict.Contains("0.12 MW"),
            $"dragged to the frame's far corner: blue, \"{builder.Verdict}\"");
        int had = pack[T1];
        c.Primary = false;
        foreach (int x in _drive.Wait(3)) yield return x;
        ForceField f = s.Fields.SingleOrDefault();
        Check(f != null && f.Min == FieldRig.OpeningMin && f.Max == FieldRig.OpeningMax && f.Axis == 2 && f.Tiles == 12 && pack[T1] == had - 1 && !builder.Drawing,
            $"released: one field across the opening, {f?.Width} x {f?.Height} = {f?.Tiles} tiles, one component used ({had} to {pack[T1]})");
        if (f == null) yield break;
        BlockType emitter = ForceFields.Emitter(s.Catalogue);
        bool Whole(int i) => s.TryGet(f.Corner(i), out Block b) && s.TypeOf(b).IsFieldEmitter && b.Rot == f.CornerRot(i) && b.Hp >= s.MaxHp(b) - 1e-3f;
        Check(Enumerable.Range(0, 4).All(Whole) && f.CornerRot(0) != f.CornerRot(2),
            $"an L-shaped {emitter.Name} in each corner, each turned to run along its two edges ({string.Join(", ", Enumerable.Range(0, 4).Select(i => $"{f.Corner(i)} {Orientations.Name(f.CornerRot(i))}"))})");
        Check(new ForceField(new Int3(3, 2, 0), new Int3(5, 3, 0), 2, T1).Check(s, new Int3(5, 3, 0), rules.MinSide, rules.MaxSide) == FieldError.Occupied,
            "another field over it can't go: occupied");

        // ---- Air and power --------------------------------------------------------------------
        foreach (int x in _drive.Wait(3)) yield return x;
        FieldTier tier = rules.TierOf(T1);
        double draw = _game.Fields.Draw(v);
        Check(f.Holding && f.Strength == tier.Strength && rig.Sealed(), $"it holds at its tier's full {f.Strength:F0} and the room is sealed ({v.Rooms.Sealed.Count()} sealed)");
        Check(Math.Abs(draw - f.Tiles * rules.DrawPerTile) < 1e-9, $"it draws its tiles x the per-tile rate: {f.Tiles} x {rules.DrawPerTile} = {draw:0.###} MW");

        // ---- Plating an emitter hardens it ---------------------------------------------------
        foreach (int x in play.Tool(ToolMode.Plate)) yield return x;
        pack.Add("iron-plate", 10);
        BlockType iron = s.Catalogue.Type("iron-plated");
        for (int i = 0; i < builder.Platings.Count && builder.Plating != iron; i++) { c.Wheel = 1; yield return 0; }
        Vector3 hub = rig.World(1.54f, 0.54f, -0.09f); // corner 0's hub, from out in front
        foreach (int x in _drive.LookAt(hub, 8)) yield return x;
        string said = builder.Verdict;
        int plates = pack["iron-plate"];
        foreach (int x in _drive.Click()) yield return x;
        foreach (int x in _drive.Wait(2)) yield return x;
        Block plated = s.Get(f.Corner(0)), bare = s.Get(f.Corner(1));
        Check(s.TypeOf(plated).IsFieldEmitter && s.TypeOf(plated).IsPlated && s.TypeOf(plated).Plate == "iron-plate" && pack["iron-plate"] == plates - s.TypeOf(plated).Plates && f.Holding,
            $"the plating gun on an emitter: {s.TypeOf(plated).Name}, for {plates - pack["iron-plate"]} iron plates; the line said \"{said}\"");
        float Lost(int corner)
        {
            Int3 at = f.Corner(corner);
            float before = s.Get(at).Hp;
            v.Hits.Shot(at, v.ToWorld(StructureBridge.CellCenter(s, at)).ToNumerics(), N.Vector3.UnitZ, 80f, DamageType.Kinetic);
            v.Hits.Flush();
            return before - s.Get(at).Hp;
        }
        float lostPlated = Lost(0), lostBare = Lost(1);
        Check(lostPlated < lostBare && s.StatsOf(plated).MaxHp > s.StatsOf(bare).MaxHp && Enumerable.Range(0, 4).All(i => s.TryGet(f.Corner(i), out Block e) && s.TypeOf(e).IsFieldEmitter) && f.Holding,
            $"plated, it takes less from the same hit ({lostPlated:F0} of {s.StatsOf(plated).MaxHp:F0} against {lostBare:F0} of {s.StatsOf(bare).MaxHp:F0}); a worn emitter leaves the field up");
        Building.Repair(s, f.Corner(0));
        Building.Repair(s, f.Corner(1));
        v.ApplyEdits();

        // ---- People and vessels pass through --------------------------------------------------
        Vector3 eye = _game.Survivor.WorldEye(), through = rig.World(3.5f, 2f, 0f), dir = (through - eye).Normalized();
        bool hitAny = WorldRay.Cast(_game.Zone, null, eye, dir, 30f, Layers.World | Layers.Hull | Layers.Props | Layers.Flora | Layers.Player, out LookHit look);
        Check(hitAny && look.Distance > eye.DistanceTo(through) + 2f && look.Cell.Z == 5,
            $"nothing in it collides: a ray on every body's layers goes through it to the back wall ({look.Distance:F1} m, block {look.Cell}, the field at {eye.DistanceTo(through):F1} m)");
        foreach (int x in play.Select(slot)) yield return x;
        foreach (int x in _drive.WalkTo(rig.World(3.5f, 0.6f, 2.5f), 0.6f, 40f)) yield return x;
        N.Vector3 feet = v.Body.ToLocal(_game.Survivor.WorldFeet.ToNumerics()) / s.Grid.CellSize;
        Check(feet.Z > 1f && f.Holding, $"the sleeper walks in through it (feet at cell {feet.X:F1} {feet.Y:F1} {feet.Z:F1}), the field still up");
        _game.Survivor.PlaceAboard(v, rig.Local(3.5f, 0.55f, 2.5f));
        foreach (int x in _drive.Wait(20)) yield return x;
        Suit suit = _game.Suit;
        Check(suit.InSealedRoom && suit.Room == RoomKind.Sealed, $"inside, the suit reads a sealed, powered room ({suit.Room})");

        // ---- Shots stop on it ------------------------------------------------------------------
        Vector3 outward = rig.World(3.5f, 2f, -4f);
        bool swept = _game.Arms.Sweep(_game.Survivor.WorldEye().ToNumerics(), outward.ToNumerics(), null, 0, 0f, out ShotHit shot);
        Check(swept && shot.Kind == HitKind.Field && shot.Thing is FieldHit fh && fh.Field == f, $"a shot's sweep out through the opening meets the field ({shot.Kind} at {shot.Distance:F1} m)");
        float strength = f.Strength;
        int hits = ForceFields.Hits;
        foreach (int x in play.ShootAt(() => rig.World(3.5f, 2f, 0f))) yield return x;
        float bolt = _game.Zone.Data.Rules.Tool.ShotDamage;
        Check(ForceFields.Hits == hits + 1 && Math.Abs(strength - f.Strength - bolt) < 0.01f && f.Holding,
            $"a survival tool bolt hits it: {strength:F0} to {f.Strength:F0} ({bolt:F0}), still up");

        // ---- Knocked down, it grows back --------------------------------------------------------
        _game.Fields.TimeScale = 10f; // STAGING: the waits at ten times play speed
        ForceFields.Hit(v, f, f.Strength + 100f);
        foreach (int x in _drive.Wait(2)) yield return x;
        Check(!f.Holding && f.Strength == 0f && !rig.Sealed() && Enumerable.Range(0, 4).All(Whole) && s.Fields.Contains(f),
            $"knocked to zero it fizzles: gone, the room leaking, its four emitters whole (wait {f.Wait:F1} s of {tier.RegrowDelay})");
        foreach (int x in _drive.Wait(2)) yield return x;
        Check(!suit.InSealedRoom && suit.Room == RoomKind.None, $"the suit inside feels it at once ({suit.Room})");
        IEnumerable<int> PlaySeconds(float seconds) => _drive.Wait((int)(seconds / _game.Fields.TimeScale * Engine.PhysicsTicksPerSecond));
        foreach (int x in PlaySeconds(tier.RegrowDelay * 0.8f)) yield return x;
        Check(!f.Holding && f.Strength == 0f, $"it stays down through its {tier.RegrowDelay:F0} s wait ({f.Wait:F1} s left)");
        foreach (int x in PlaySeconds(tier.RegrowDelay * 0.2f + tier.RegrowSeconds * 0.5f)) yield return x;
        float half = f.Strength / tier.Strength;
        Check(f.Holding && half > 0.3f && half < 0.75f && rig.Sealed(), $"then it quietly grows back, holding the air again: {half:P0} of its cap half way through its {tier.RegrowSeconds:F0} s");
        foreach (int x in PlaySeconds(tier.RegrowSeconds * 0.6f)) yield return x;
        Check(f.Strength == tier.Strength && f.Holding, $"and back to its cap, {f.Strength:F0}, no more");

        _game.Fields.TimeScale = 1f;

        // ---- The door's own field --------------------------------------------------------------
        Func<Vector3> was = _game.Doors.Feet;
        Vector3 inDoor = rig.World(FieldRig.Door.X, FieldRig.Door.Y, FieldRig.Door.Z) - Vector3.Up * 0.9f;
        _game.Doors.Feet = () => inDoor; // STAGING: held open as if someone stood in it
        _game.Doors.Use(v, FieldRig.Door);
        foreach (int x in _drive.Wait(40)) yield return x;
        double withDoor = _game.Fields.Draw(v);
        Check(BlockDoors.IsOpen(v, FieldRig.Door) && v.Rooms.FieldDoors.Contains(FieldRig.Door) && rig.Sealed() && suit.InSealedRoom && _game.Fields.DoorsShown == 1,
            $"the outer door open while powered: its own field holds the air (sealed {rig.Sealed()}, suit {suit.Room}, drawn {_game.Fields.DoorsShown})");
        Check(Math.Abs(withDoor - draw - rules.DoorDraw) < 1e-9 && rules.DoorDraw < rules.DrawPerTile, $"and costs very little: {rules.DoorDraw} MW ({withDoor:0.####} MW with the field)");
        v.PowerOn = false;
        foreach (int x in _drive.Wait(3)) yield return x;
        Check(!f.Holding && !s.DoorFields && v.Rooms.FieldDoors.Count == 0 && !rig.Sealed() && _game.Fields.Draw(v) == 0 && _game.Fields.DoorsShown == 0,
            "power off: neither field holds, the room leaks, nothing is drawn");
        v.PowerOn = true;
        foreach (int x in _drive.Wait(3)) yield return x;
        Check(f.Holding && f.Strength == tier.Strength && rig.Sealed(), "power back: both hold again at once");
        _game.Doors.Feet = was;
        for (int i = 0; i < 300 && BlockDoors.IsOpen(v, FieldRig.Door); i++) yield return 0;

        // ---- Taken down whole ---------------------------------------------------------------
        foreach (int x in play.Tool(ToolMode.Deconstruct)) yield return x;
        _game.Survivor.PlaceAboard(v, rig.Local(3.5f, 0.55f, 2.5f));
        foreach (int x in _drive.Wait(10)) yield return x;
        int components = pack[T1], ironBack = pack["iron-plate"];
        foreach (int x in _drive.LookAt(rig.World(1.54f, 0.54f, 0.09f), 8)) yield return x;
        said = builder.Verdict;
        foreach (int x in _drive.Click()) yield return x;
        foreach (int x in _drive.Wait(3)) yield return x;
        Check(s.Fields.Count == 0 && Enumerable.Range(0, 4).All(i => !s.Has(f.Corner(i))) && pack[T1] == components + 1 && pack["iron-plate"] > ironBack && !rig.Sealed(),
            $"deconstructing an emitter takes the field down whole: its emitters gone, the component back ({components} to {pack[T1]}), the plating's iron back; the line said \"{said}\"");

        // ---- Emitters: passed through, shot, destroyed --------------------------------------------
        f = new ForceField(FieldRig.OpeningMin, FieldRig.OpeningMax, 2, T1) { Strength = tier.Strength }; // STAGING: a second field, set down whole
        for (int i = 0; i < 4; i++) Building.Place(s, f.Corner(i), Building.Make(s, emitter, null, f.CornerRot(i)));
        s.Fields.Add(f);
        v.ApplyEdits();
        foreach (int x in _drive.Wait(3)) yield return x;
        // Along the opening's side, so the body runs through corner 0's upright arm.
        Vector3 start = rig.World(1.88f, 0f, -2.5f);
        _game.Survivor.PlaceOutside((_game.Zone.GroundAt(start.X, start.Z) + Vector3.Up * 0.1f).ToNumerics());
        foreach (int x in _drive.Wait(20)) yield return x;
        float widest = 0f;
        foreach (int x in _drive.WalkTo(rig.World(1.88f, 0.6f, 2.5f), 0.4f, 30f))
        {
            N.Vector3 at = v.Body.ToLocal(_game.Survivor.WorldFeet.ToNumerics()) / s.Grid.CellSize;
            if (MathF.Abs(at.Z) < 0.5f) widest = MathF.Max(widest, MathF.Abs(at.X - 1.88f));
            yield return x;
        }
        feet = v.Body.ToLocal(_game.Survivor.WorldFeet.ToNumerics()) / s.Grid.CellSize;
        Check(feet.Z > 1.5f && widest < 0.06f && f.Holding && Enumerable.Range(0, 4).All(Whole),
            $"the sleeper walks straight through an emitter's arm, nothing in the way (feet at cell {feet.X:F2} {feet.Z:F2}, pushed aside {widest * 100f:F0} cm at most)");
        _game.Survivor.PlaceAboard(v, rig.Local(3.5f, 0.55f, 2.5f));
        foreach (int x in _drive.Wait(10)) yield return x;
        Int3 target = f.Corner(2);
        Vector3 hub2 = rig.World(5.46f, 3.46f, 0.09f); // corner 2's hub, from inside
        swept = _game.Arms.Sweep(_game.Survivor.WorldEye().ToNumerics(), (hub2 + (hub2 - _game.Survivor.WorldEye()).Normalized()).ToNumerics(), null, 0, 0f, out shot);
        Check(swept && shot.Kind == HitKind.Block && new Int3(shot.CellX, shot.CellY, shot.CellZ) == target, $"a shot meets the emitter it is aimed at ({shot.Kind}, block {shot.CellX} {shot.CellY} {shot.CellZ})");
        int bolts = 0;
        for (; bolts < 20 && s.Has(target); bolts++)
            foreach (int x in play.ShootAt(() => hub2)) yield return x;
        foreach (int x in _drive.Wait(3)) yield return x;
        Check(!s.Has(target) && s.Fields.Count == 0 && Enumerable.Range(0, 4).All(i => !s.Has(f.Corner(i))) && !rig.Sealed(),
            $"shot until it breaks ({bolts} bolts): all four emitters and the field are gone");
        _game.Fields.TimeScale = 10f; // STAGING: the waits at ten times play speed
        foreach (int x in PlaySeconds(tier.RegrowDelay + tier.RegrowSeconds + 5f)) yield return x;
        _game.Fields.TimeScale = 1f;
        Check(s.Fields.Count == 0 && Enumerable.Range(0, 4).All(i => !s.Has(f.Corner(i))) && !rig.Sealed(), "nothing grows back after its timer: it takes a new component");
    }

    void StandOutside(FieldRig rig)
    {
        Vector3 at = rig.World(4f, 0f, -6f);
        _game.Survivor.PlaceOutside((_game.Zone.GroundAt(at.X, at.Z) + Vector3.Up * 0.1f).ToNumerics());
    }

    /// <summary>
    /// Sealed to the eye: a placed field's sheet ends exactly on its emitters' outer edges (any size,
    /// any facing) and lies inside their depth; an open door's sheet meets its frame's posts, lintel
    /// and sill (every door, every turn). Within a millimetre.
    /// </summary>
    void Flush(Structure s, FieldRules rules)
    {
        const float Mm = 1e-3f;
        float cell = s.Grid.CellSize;
        BlockType emitter = ForceFields.Emitter(s.Catalogue);
        N.Vector3 V(Vector3 g) => g.ToNumerics();
        // A sheet's four corners, structure frame, metres, as a box (flat along its normal).
        (N.Vector3 Lo, N.Vector3 Hi) Sheet(Transform3D t, Vector2 size)
        {
            var pts = new[] { -1f, 1f }.SelectMany(a => new[] { -1f, 1f }.Select(b => V(t.Origin + t.Basis.X * a * size.X * 0.5f + t.Basis.Y * b * size.Y * 0.5f))).ToList();
            return (pts.Aggregate(N.Vector3.Min), pts.Aggregate(N.Vector3.Max));
        }
        // A one-cell part's box in the structure frame, metres.
        (N.Vector3 Lo, N.Vector3 Hi) Box(Int3 anchor, in Block b, N.Vector3 min, N.Vector3 max)
        {
            BlockType t = s.TypeOf(b);
            Mat3 m = Orientations.Matrix(b.Rot);
            N.Vector3 c = s.CentreOf(anchor, b), n = t.Size.ToVector3();
            N.Vector3 p = c + m.Apply(min * n), q = c + m.Apply(max * n);
            return (N.Vector3.Min(p, q) * cell, N.Vector3.Max(p, q) * cell);
        }
        var worst = (gap: 0f, what: "");
        void Gap(float g, string what) { if (MathF.Abs(g) > MathF.Abs(worst.gap)) worst = (g, what); }

        int sizes = 0;
        foreach (int axis in new[] { 2, 0, 1 })
        foreach (var (w, h) in new[] { (rules.MinSide, rules.MinSide), (4, 3), (7, 2), (2, 9), (rules.MaxSide, rules.MaxSide) })
        {
            Int3 lo = new Int3(-3, 1, 2), hi = lo.With((axis + 1) % 3, lo[(axis + 1) % 3] + w - 1).With((axis + 2) % 3, lo[(axis + 2) % 3] + h - 1);
            var f = new ForceField(lo, hi, axis, "force-field-t1");
            Transform3D frame = ForceFields.Frame(cell, f.Min, f.Max, f.Axis, out Vector2 size);
            var sheet = Sheet(frame, size);
            N.Vector3 eLo = new(float.MaxValue), eHi = new(float.MinValue);
            for (int i = 0; i < 4; i++)
            {
                Block b = Building.Make(s, emitter, null, f.CornerRot(i));
                foreach (Part p in emitter.Parts)
                {
                    var (a, z) = Box(f.Corner(i), b, p.Min, p.Max);
                    (eLo, eHi) = (N.Vector3.Min(eLo, a), N.Vector3.Max(eHi, z));
                }
            }
            string at = $"{w} x {h} across {"xyz"[axis]}";
            foreach (int k in new[] { f.U, f.V })
            {
                Gap(Geometry.Get(sheet.Lo, k) - Geometry.Get(eLo, k), $"{at}, {"xyz"[k]} low edge");
                Gap(Geometry.Get(sheet.Hi, k) - Geometry.Get(eHi, k), $"{at}, {"xyz"[k]} high edge");
            }
            float plane = Geometry.Get(sheet.Lo, axis);
            if (plane < Geometry.Get(eLo, axis) || plane > Geometry.Get(eHi, axis)) Gap(1f, $"{at}: the sheet is outside its emitters' depth");
            sizes++;
        }

        int doors = 0;
        foreach (BlockType t in s.Catalogue.Types.Where(t => t is { IsDoor: true } && t.Parts.Any(p => p.ClosedOnly && p.Style == PartStyle.Body)))
        {
            // The opening: inside the frame's posts and under its lintel, down to the sill (the cell's floor).
            var frame = t.Parts.Where(p => p.Style == PartStyle.Dark && !p.ClosedOnly).ToList();
            float left = frame.Where(p => p.Min.X <= -0.5f + 1e-4f && p.Max.Y >= 0.5f - 1e-4f && p.Min.Y <= -0.5f + 1e-4f).Max(p => p.Max.X);
            float right = frame.Where(p => p.Max.X >= 0.5f - 1e-4f && p.Max.Y >= 0.5f - 1e-4f && p.Min.Y <= -0.5f + 1e-4f).Min(p => p.Min.X);
            float top = frame.Where(p => p.Max.Y >= 0.5f - 1e-4f && p.Min.X > -0.5f + 1e-4f && p.Max.X < 0.5f - 1e-4f).Min(p => p.Min.Y);
            for (int rot = 0; rot < 24; rot++)
            {
                Block b = Building.Make(s, t, null, rot);
                var sheet = Sheet(ForceFields.DoorSheet(s, Int3.Zero, b, out Vector2 size), size);
                var (oLo, oHi) = Box(Int3.Zero, b, new N.Vector3(left, -0.5f, 0f), new N.Vector3(right, top, 0f));
                string at = $"{t.Id} turned {Orientations.Name(rot)}";
                Gap(N.Vector3.Distance(sheet.Lo, oLo), $"{at}, low corner");
                Gap(N.Vector3.Distance(sheet.Hi, oHi), $"{at}, high corner");
            }
            doors++;
        }
        Check(sizes == 15 && doors >= 5 && MathF.Abs(worst.gap) <= Mm,
            $"flush: {sizes} field sizes and facings meet their emitters' outer edges, {doors} doors x 24 turns meet their frames, the worst {worst.gap * 1000f:F2} mm ({worst.what})");
        Check(rules.MinSide >= 2, $"a field is at least {rules.MinSide} x {rules.MinSide} (no 1 x 1: its four emitters would share one cell)");
    }

    /// <summary>The tiers and the draw, from data: linear in tiles, 4 tiles cheap against a first generator.</summary>
    void Tables(FieldRules rules)
    {
        var tiers = rules.Tiers.OrderBy(t => t.Key, StringComparer.Ordinal).ToList();
        foreach (var (id, t) in tiers) GD.Print($"  {id}: strength {t.Strength}, regrows after {t.RegrowDelay} s over {t.RegrowSeconds} s");
        Check(tiers.Count == 3 && tiers.Zip(tiers.Skip(1)).All(p => p.Second.Value.Strength > p.First.Value.Strength && p.Second.Value.RegrowDelay < p.First.Value.RegrowDelay),
            "three tiers: each higher tier takes more before it fizzles and comes back sooner");
        var t2 = new ForceField(Int3.Zero, new Int3(3, 3, 0), 2, "force-field-t2") { Strength = tiers[1].Value.Strength, Holding = true };
        Check(!t2.Take(tiers[0].Value.Strength, 1f) && t2.Holding, $"a T2 field takes a T1's whole strength and stays up ({t2.Strength:F0} left)");
        double generator = _game.Zone.Data.Catalogue.Type("generator-t1").Power;
        foreach (var (w, h) in new[] { (2, 2), (6, 4), (30, 15) })
        {
            var f = new ForceField(Int3.Zero, new Int3(w - 1, h - 1, 0), 2, "force-field-t1") { Holding = true };
            GD.Print($"  {w} x {h}: {f.Tiles} tiles, {f.Draw(rules.DrawPerTile):0.###} MW ({f.Draw(rules.DrawPerTile) / generator:P1} of a Generator T1's {generator:0.#} MW)");
            Check(Math.Abs(f.Draw(rules.DrawPerTile) - f.Tiles * rules.DrawPerTile) < 1e-12 && (f.Tiles != 4 || f.Draw(rules.DrawPerTile) < generator * 0.02),
                $"{w} x {h} draws tiles x rate{(f.Tiles == 4 ? ", under 2% of a first generator" : "")}");
        }
        var progress = _game.Progress;
        Check(progress.Node("force-field-t1")?.Unlock.Parent == "core" && progress.Node("force-field-t2")?.Unlock.Parent == "force-field-t1"
              && progress.Node("force-field-t3")?.Unlock.Parent == "force-field-t2" && tiers.All(t => progress.Node(t.Key)?.Tab == "structures"),
            "the tiers are their own tech line off the core, T1 to T2 to T3, not shields'");
    }
}
