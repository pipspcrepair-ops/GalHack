using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using Slice.Construction;
using Slice.Ui;
using Slice.Vessels;
using N = System.Numerics;

namespace Slice.Tests;

/// <summary>The vessels' cases: each kind the player can set down whole, flown or driven by a seated pilot through the same knocks.</summary>
public partial class VehicleGroundTest
{
    /// <summary>The deepest a vessel's collision shapes may go into anything solid, m.</summary>
    const float VesselTolerance = 0.1f;

    /// <summary>The kinds the player can have: a small flyer, a small hover craft, a large flyer.</summary>
    List<LibraryEntry> Kinds()
    {
        var all = _game.Menu.Root.Blueprints.Library.Starters;
        var kinds = new List<LibraryEntry>();
        void Add(LibraryEntry e) { if (e != null) kinds.Add(e); }
        Add(all.Where(e => !e.Large && e.Plan.Mode == CoreMode.Vessel && !BlueprintsSection.Unwired(e)).OrderBy(e => e.Plan.Count).FirstOrDefault());
        Add(all.Where(e => !e.Large && e.Plan.Mode == CoreMode.Hover && !BlueprintsSection.Unwired(e)).OrderBy(e => e.Plan.Count).FirstOrDefault());
        Add(all.Where(e => e.Large && e.Plan.Mode == CoreMode.Capital && !BlueprintsSection.Unwired(e)).OrderBy(e => e.Plan.Count).FirstOrDefault());
        foreach (LibraryEntry e in all.Where(BlueprintsSection.Unwired))
            GD.Print($"  ({e.Name}: Large Hover isn't wired, so it is not a vehicle the player can have yet)");
        return kinds;
    }

    /// <summary>How far any of the vessel's collision shapes are into anything solid (the ground's own collision, a place's blocks, another hull), m, by the engine's own contact query.</summary>
    float Sunk(Vessel v)
    {
        var space = _game.Zone.GetWorld3D().DirectSpaceState;
        CollisionObject3D self = v.Binding.Collider;
        float worst = 0f;
        foreach (Node n in self.GetChildren())
        {
            if (n is not CollisionShape3D cs || cs.Shape == null || cs.Disabled) continue;
            var q = new PhysicsShapeQueryParameters3D
            {
                Shape = cs.Shape, Transform = cs.GlobalTransform, CollisionMask = Layers.World | Layers.Hull, Margin = 0f,
                Exclude = new Godot.Collections.Array<Rid> { self.GetRid() },
            };
            var points = space.CollideShape(q, 16);
            for (int i = 0; i + 1 < points.Count; i += 2) worst = MathF.Max(worst, points[i].DistanceTo(points[i + 1]));
        }
        return worst;
    }

    sealed class Hull
    {
        public float Sunk;
    }

    IEnumerable<int> ObserveHull(Vessel v, Hull w, int ticks, Func<bool> done = null)
    {
        for (int i = 0; i < ticks; i++)
        {
            yield return 0;
            if (i % 2 == 0) w.Sunk = MathF.Max(w.Sunk, Sunk(v));
            if (done != null && i > 5 && done()) yield break;
        }
    }

    /// <summary>At rest again: landed, or still in the air under its own power.</summary>
    static bool Resting(Vessel v) => v.Controller == null || v.Controller.Frozen || v.Body.Velocity.Length() < 0.3f && v.Body.AngularVelocity.Length() < 0.05f;

    /// <summary>The pilot still seated takes the controls: a flyer lifts off and climbs, a hover craft runs along the ground.</summary>
    IEnumerable<int> Operable(Vessel v, string mode, string what, Hull w)
    {
        for (int t = 0; t < 600 && !Resting(v); t++) yield return 0;
        bool seated = _game.Survivor.Seated == v;
        N.Vector3 from = v.Body.Position;
        float high = v.AboveGround;
        bool hover = v.Structure.Mode == CoreMode.Hover;
        Key key = hover ? Key.W : Key.Space;
        _game.Controls.Hold(key);
        foreach (int x in ObserveHull(v, w, 120)) yield return x;
        _game.Controls.Hold(key, false);
        N.Vector3 to = v.Body.Position;
        float ran = new Vector2(to.X - from.X, to.Z - from.Z).Length(), rose = to.Y - from.Y;
        bool moved = hover ? ran > 4f : rose > 3f;
        string way = "forward";
        if (hover && !moved)
        {
            // Nose against something it came to rest facing (a rock, a trunk): the driver backs off it, as anyone would.
            from = v.Body.Position;
            _game.Controls.Hold(Key.S);
            foreach (int x in ObserveHull(v, w, 120)) yield return x;
            _game.Controls.Hold(Key.S, false);
            to = v.Body.Position;
            ran = new Vector2(to.X - from.X, to.Z - from.Z).Length();
            moved = ran > 4f;
            way = "in reverse";
        }
        if (!moved)
            GD.Print($"  (stuck: frozen {v.Controller?.Frozen}, settling {v.Controller?.Settling}, asleep {v.Body.Asleep}, tossed {v.Body.Tossed:F1}, kind {v.Body.Kind}, motion {v.Body.Motion}, flight {(v.Flight != null)}, "
                + $"input {v.Flight?.Input.Move}, can lift {v.Stats?.CanLiftOff}, pilot {v.Roles.Pilot}, message '{v.Controller?.ConsoleMessage}', velocity {v.Body.Velocity}, up {MathX.Up(v.Body.Rotation)}, hover grounded {v.Controller?.Hover?.Grounded} clearance {v.Controller?.Hover?.Clearance:F2} slope refused {v.Controller?.Hover?.SlopeRefused} steepest {v.Controller?.Hover?.SteepestAhead:F0}, on floor {v.Body.OnFloor}, pushes {(v.Binding as HullBinding)?.Pushes} last {(v.Binding as HullBinding)?.LastPush}, seated {_game.Survivor.Seated == v}, sunk now {Sunk(v):F2})");
        Check(seated && _game.Survivor.Seated == v && moved && w.Sunk < VesselTolerance,
            $"{mode}, {v.Name}, {what}: deepest {w.Sunk * 100f:F1} cm into anything solid (tolerance {VesselTolerance * 100f:F0} cm); the pilot still seated (pilot role: {v.Roles.Pilot}), under its own power it {(hover ? $"runs {ran:F1} m {way}" : $"climbs {rose:F1} m")} in two seconds");
        // Down again for the next case.
        if (!hover)
        {
            _game.Controls.Hold(Key.Ctrl);
            for (int t = 0; t < 600 && !v.Controller.Frozen && v.AboveGround > high + 0.6f; t++) yield return 0;
            _game.Controls.Hold(Key.Ctrl, false);
        }
        else
        {
            _game.Controls.Hold(Key.S);
            foreach (int x in _drive.Wait(45)) yield return x;
            _game.Controls.Hold(Key.S, false);
        }
        foreach (int x in _drive.Wait(30)) yield return x;
    }

    /// <summary>
    /// Between cases the knocks' damage is put right (as Creative builds and repairs, free), so each case starts from a
    /// whole vessel: a chair or the lift thrusters broken by the last crash is fair damage, not what is checked here.
    /// </summary>
    Hull Mend(Vessel v, GalHack.Structures.Structure plan)
    {
        for (int pass = 0; pass < 8; pass++)
        {
            int placed = 0;
            foreach (var (p, b) in plan.Blocks())
                if (!v.Structure.Has(p) && Building.Place(v.Structure, p, b, null, Building.DefaultOwner) == PlaceError.None) placed++;
            if (placed == 0) break;
        }
        foreach (var (p, _) in v.Structure.Blocks().ToList())
            if (Building.IsDamaged(v.Structure, p)) Building.Repair(v.Structure, p, 1f);
        v.ApplyEdits();
        if (_game.Survivor.Seated != v) _game.Survivor.Sit(v);
        return new Hull();
    }

    /// <summary>Sets the hull somewhere, as a knock or a placement would, and lets it go.</summary>
    static void Put(Vessel v, N.Vector3 position, N.Quaternion rotation, N.Vector3 velocity)
    {
        v.Body.Position = position;
        v.Body.Rotation = rotation;
        v.Body.Velocity = velocity;
        v.Body.AngularVelocity = N.Vector3.Zero;
        v.Controller?.Wake(v.Body);
        v.Binding.Teleport();
    }

    IEnumerable<int> Vessels(string mode)
    {
        foreach (LibraryEntry e in Kinds())
        {
            GD.Print($"-- {mode}: {e.Name} ({(e.Large ? "large" : "small")}, {e.Plan.Mode}) --");
            Vessel v = null;
            foreach (int x in SetDown(e, r => v = r)) yield return x;
            if (!Check(v != null && v.Flight != null, $"{mode}: the {e.Name} set down whole{(v == null ? $" ({_game.Builder.Verdict})" : "")}")) continue;
            Check(_game.Survivor.Sit(v), $"{mode}, {v.Name}: in its seat");
            foreach (int x in _drive.Wait(10)) yield return x;
            Vector3 fwd = new Basis(Vector3.Up, MathX.Heading(v.Body.Rotation)) * Vector3.Forward;
            N.Vector3 f = fwd.ToNumerics();
            N.Quaternion level = MathX.Yaw(MathX.Heading(v.Body.Rotation));
            float feet = v.Body.Shape.FeetBelowOrigin;

            // Dropped from a height, engines idle.
            var w = Mend(v, e.Plan);
            Put(v, v.Body.Position + N.Vector3.UnitY * 20f, level, N.Vector3.Zero);
            foreach (int x in ObserveHull(v, w, 600, () => Resting(v))) yield return x;
            foreach (int x in Operable(v, mode, "dropped from 20 m", w)) yield return x;

            // Into the ground at speed.
            w = Mend(v, e.Plan);
            Put(v, v.Body.Position + N.Vector3.UnitY * 8f, level, f * 25f - N.Vector3.UnitY * 18f);
            foreach (int x in ObserveHull(v, w, 600, () => Resting(v))) yield return x;
            foreach (int x in Operable(v, mode, "a crash into the ground at 31 m/s", w)) yield return x;

            // A steep hit: nose down at 50°.
            w = Mend(v, e.Plan);
            N.Quaternion nose = N.Quaternion.Normalize(level * MathX.AxisAngle(N.Vector3.UnitX, -0.87f));
            Put(v, v.Body.Position + N.Vector3.UnitY * 10f, nose, f * 10f - N.Vector3.UnitY * 20f);
            foreach (int x in ObserveHull(v, w, 600, () => Resting(v))) yield return x;
            foreach (int x in Operable(v, mode, "a steep hit, nose down at 50°", w)) yield return x;

            // Placed partly in the ground: set down landed with its keel a metre under.
            w = Mend(v, e.Plan);
            N.Vector3 at = v.Body.Position;
            float ground = _game.Zone.GroundAt(at.X, at.Z).Y;
            v.Controller.PlaceLanded(v.Body, new N.Vector3(at.X, ground + feet - 1f, at.Z), MathX.Heading(v.Body.Rotation));
            v.Binding.Teleport();
            foreach (int x in ObserveHull(v, w, 30)) yield return x;
            foreach (int x in Operable(v, mode, "placed with its keel 1 m in the ground", w)) yield return x;

            // Against a place's blocks (the small ones: a large hull is bigger than any roof): dropped onto them, and set down partly in them.
            if (_placeTop is Vector3 top && !e.Large)
            {
                w = Mend(v, e.Plan);
                Put(v, top.ToNumerics() + N.Vector3.UnitY * (feet + 3f), level, N.Vector3.Zero);
                foreach (int x in ObserveHull(v, w, 600, () => Resting(v))) yield return x;
                foreach (int x in Operable(v, mode, "dropped 3 m onto a place's blocks", w)) yield return x;
                w = Mend(v, e.Plan);
                v.Controller.PlaceLanded(v.Body, top.ToNumerics() + N.Vector3.UnitY * (feet - 0.4f), MathX.Heading(v.Body.Rotation));
                v.Binding.Teleport();
                foreach (int x in ObserveHull(v, w, 30)) yield return x;
                foreach (int x in Operable(v, mode, "placed 0.4 m into a place's blocks", w)) yield return x;
            }

            _game.Survivor.StandUp();
            foreach (int x in _drive.Wait(5)) yield return x;
            _game.Survivor.PlaceOutside((v.Body.Position + N.Vector3.UnitY * 30f).ToGodot().ToNumerics());
            _game.Zone.Physics.Unregister(v.Body);
            _game.Zone.Physics.StuckFields.Remove(v.Field);
            _game.Zone.Untrack(v);
            v.Node.QueueFree();
            foreach (int x in _drive.Wait(10)) yield return x;
        }
    }

    /// <summary>Creative's PLACE: the whole starter set down on level ground just ahead of you.</summary>
    IEnumerable<int> SetDown(LibraryEntry e, Action<Vessel> result)
    {
        float radius = 0.5f * new Vector2(e.Size.X, e.Size.Z).Length();
        var play = new Playthrough(_game);
        for (int attempt = 0; attempt < 6; attempt++)
        {
            Vector3 spot = play.FlatSpot(_game.Survivor.WorldFeet, 60f + 40f * attempt, attempt * 1.3f, 0.5f + attempt * 0.5f, span: MathF.Max(14f, radius));
            float yaw = attempt * 0.9f;
            Vector3 ahead = new Basis(Vector3.Up, yaw) * Vector3.Forward;
            Vector3 stand = spot - ahead * (radius + 4f);
            _game.Survivor.PlaceOutside((_game.Zone.GroundAt(stand.X, stand.Z) + Vector3.Up * 0.2f).ToNumerics());
            foreach (int x in _drive.UntilSettled(20)) yield return x;
            _game.Survivor.Yaw = yaw;
            _game.Survivor.Pitch = 0f;
            foreach (int x in _drive.Wait(30)) yield return x;
            WholePlacement job = _game.Builder.PlaceWhole(e.Plan);
            for (int t = 0; t < 600 && !job.Done; t++) yield return 0;
            foreach (int x in _drive.Wait(20)) yield return x;
            if (job.Vessel != null)
            {
                for (int t = 0; t < 300 && (job.Vessel.PendingShapes > 0 || job.Vessel.View.Waiting > 0); t++) yield return 0;
                result(job.Vessel);
                yield break;
            }
            GD.Print($"  ({e.Name} would not go down near {spot}: {job.Why})");
        }
    }
}
