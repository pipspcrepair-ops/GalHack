using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using Slice.Game;
using Slice.Vessels;
using N = System.Numerics;

namespace Slice.Tests;

/// <summary>
/// Plays the slice through its <see cref="Controls"/>, one physics tick per step, the way a
/// player's hands would: look, walk, press and hold. Shared by the playthrough test and the
/// screenshots. Each routine is an iterator that yields once per tick.
/// </summary>
public sealed class Driver
{
    public readonly Game.Game Game;
    public Controls Controls => Game.Controls;

    public Driver(Game.Game game) => Game = game;

    const float Sensitivity = 0.0025f;

    /// <summary>Waits for the planet, the pod's landing and the ground under the survivor.</summary>
    public IEnumerable<int> UntilPlaying(double timeout = 120)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (Game.Phase != Phase.Playing)
        {
            if (watch.Elapsed.TotalSeconds > timeout) throw new TimeoutException($"still {Game.Phase} after {timeout} s");
            yield return 0;
        }
        while (!Game.Zone.Streamer.ReadyAround(Game.Survivor.WorldFeet, 1, needCollision: true))
        {
            if (watch.Elapsed.TotalSeconds > timeout) throw new TimeoutException("the ground never streamed in");
            yield return 0;
        }
        for (int i = 0; i < 10; i++) yield return 0;
    }

    /// <summary>Waits a number of ticks with the hands off.</summary>
    public IEnumerable<int> Wait(int ticks)
    {
        for (int i = 0; i < ticks; i++) yield return 0;
    }

    /// <summary>Waits until the streamer has everything around the focus (screenshots: no half-loaded ground).</summary>
    public IEnumerable<int> UntilSettled(double timeout = 60)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!Game.Zone.Streamer.Settled && watch.Elapsed.TotalSeconds < timeout) yield return 0;
    }

    // ---- Looking ----------------------------------------------------------------------------

    /// <summary>The mouse movement that turns the view to look at a point this tick.</summary>
    public void Face(Vector3 point)
    {
        var s = Game.Survivor;
        Vector3 d = (point - s.WorldEye()).Normalized();
        float frameHeading = MathX.Heading(s.WorldViewFrame);
        float yaw = MathF.Atan2(-d.X, -d.Z) - frameHeading;
        float pitch = MathF.Asin(Math.Clamp(d.Y, -1f, 1f));
        if (s.Legs.Floating)
        {
            // Afloat in space your up can be any way at all: the look is in the view's own frame.
            Vector3 local = MathX.Unrotate(s.WorldViewFrame, d.ToNumerics()).ToGodot();
            yaw = MathF.Atan2(-local.X, -local.Z);
            pitch = MathF.Asin(Math.Clamp(local.Y, -1f, 1f));
        }
        float dy = Mathf.Wrap(yaw - s.Yaw, -MathF.PI, MathF.PI);
        Controls.Look = new Vector2(-dy / Sensitivity, -(pitch - s.Pitch) / Sensitivity);
    }

    /// <summary>Turns to look at a point and holds the look for a few ticks.</summary>
    public IEnumerable<int> LookAt(Vector3 point, int ticks = 3)
    {
        for (int i = 0; i < ticks; i++)
        {
            Face(point);
            yield return 0;
        }
    }

    // ---- Walking ----------------------------------------------------------------------------

    /// <summary>
    /// Walks (sprinting when there is stamina) to within a distance of a point, jumping and using
    /// the jetpack over anything that stops it. Returns when there, or after the time limit.
    /// </summary>
    public IEnumerable<int> WalkTo(Vector3 target, float within, float seconds = 90f, bool sprint = true, bool dig = true, bool hop = true)
    {
        // The menu takes the mouse: close it to look where you walk.
        if (Game.Menu.IsOpen)
        {
            Controls.Tap(Key.Tab);
            yield return 0;
        }
        int ticks = (int)(seconds * Engine.PhysicsTicksPerSecond);
        float stuck = 0f, jet = 0f, best = float.MaxValue, noProgress = 0f, side = 0f, sideFor = 0f;
        bool sideLeft = false;
        bool digging = false, climbed = false;
        Vector3 last = Game.Survivor.WorldFeet;
        for (int i = 0; i < ticks; i++)
        {
            Vector3 feet = Game.Survivor.WorldFeet;
            // Down in the bunker (fallen into its stairwell from the side, say) and going
            // somewhere above: up its stairs first, as a person would.
            // Once: if its way out doesn't take you (a bank at the head of the stairs), the walk's own hops and digging must.
            if (!_following && !climbed && Game.Bunker is Slice.Start.Bunker b && b.Inside(feet, Game.Zone) && !b.Inside(target, Game.Zone))
            {
                climbed = true;
                Controls.Hold(Key.W, false);
                Controls.Hold(Key.Space, false);
                foreach (int x in ClimbOut(b)) yield return x;
                continue;
            }
            Vector3 to = target - feet;
            float flat = new Vector2(to.X, to.Z).Length();
            if (flat <= within) break;
            Face(new Vector3(target.X, Game.Survivor.WorldEye().Y, target.Z));
            Controls.Hold(Key.W);
            Controls.Hold(Key.Shift, sprint && Game.Suit.Vitals.Stamina > 30);
            float moved = (feet - last).Length();
            last = feet;
            stuck = moved < 0.02f ? stuck + 1f / 60f : 0f;
            // Stuck against something: a hop on the jetpack, or on the stairs only a little jump.
            if (stuck > 0.4f && jet <= 0f)
            {
                jet = hop ? 0.7f : 0.12f;
                if (_following)
                {
                    StairJumps++;
                    GD.Print($"  (stuck on the bunker's way at {feet} toward {target}: {Probe(feet)})");
                }
            }
            // Getting nowhere for a while (a pit, a bank too steep): dig a way out ahead with the
            // survival tool, as a player in a hole would.
            if (flat < best - 0.5f) { best = flat; noProgress = 0f; }
            else noProgress += 1f / 60f;
            // Under the ground with the way on above it (a cave fallen into, a shaft dug): a ramp up out of
            // it, at once; on through the dark the walk only finds deeper caves, and ore nobody can reach.
            bool buried = target.Y - feet.Y > 2.5f && Game.Zone.GroundAt(feet.X, feet.Z).Y - feet.Y > 2.5f;
            bool canDig = dig && Game.Pack.Hand == Slice.Items.HandKind.Tool && Game.Tool.Mode == Slice.Player.ToolMode.Gather;
            digging = canDig && (noProgress > 2f || buried);
            // Nose to a tree or wedged under a parked hull, with nothing to dig with: back off and sidestep, further each time.
            if (!canDig && !_following && noProgress > 1.5f && side <= 0f)
            {
                sideFor += 0.8f;
                side = sideFor;
                sideLeft = !sideLeft;
                noProgress = 0f;
            }
            Controls.Hold(Key.W, side <= 0f);
            Controls.Hold(Key.S, side > sideFor - 0.5f && side > 0f);
            Controls.Hold(Key.A, side > 0f && sideLeft);
            Controls.Hold(Key.D, side > 0f && !sideLeft);
            side -= 1f / 60f;
            if (digging)
            {
                var ahead = new Vector3(to.X, 0f, to.Z).Normalized();
                Face(feet + ahead * 1.6f + Vector3.Up * (buried ? 3.2f : 0.7f));
            }
            Controls.Primary = digging;
            // Over an edge: the jetpack brakes the fall, as a careful player would.
            bool falling = !Game.Survivor.Body.OnFloor && Game.Survivor.Body.Velocity.Y < -5f;
            // Down in a hole, the way on well above: up on the jetpack, as a player would.
            bool climb = hop && target.Y - feet.Y > 2.5f && flat < 20f;
            Controls.Hold(Key.Space, jet > 0f || falling || climb);
            jet -= 1f / 60f;
            yield return 0;
        }
        if (digging) Controls.Primary = false;
        Controls.Hold(Key.W, false);
        Controls.Hold(Key.S, false);
        Controls.Hold(Key.A, false);
        Controls.Hold(Key.D, false);
        Controls.Hold(Key.Shift, false);
        Controls.Hold(Key.Space, false);
        for (int i = 0; i < 20; i++) yield return 0;
    }

    /// <summary>
    /// Up to a point on ground too steep to stand on, where the walker slides off below it: over it
    /// on the jetpack, looking at it, pressing whenever within reach, again when the tanks are full.
    /// </summary>
    public IEnumerable<int> HopToward(Vector3 aim, float reach, float over, Func<bool> done, Action press)
    {
        for (int hop = 0; hop < 6 && !done(); hop++)
        {
            for (int i = 0; i < 240 && !done(); i++)
            {
                Face(aim);
                Vector3 eye = Game.Survivor.WorldEye();
                Controls.Primary = false;
                Controls.Hold(Key.W, new Vector2(aim.X - eye.X, aim.Z - eye.Z).Length() > 1.2f);
                Controls.Hold(Key.Space, eye.Y < aim.Y + over);
                if (i % 4 == 3 && eye.DistanceTo(aim) < reach) press();
                yield return 0;
            }
            Controls.ReleaseAll();
            if (done()) break;
            for (int i = 0; i < 600 && !Game.Survivor.Body.OnFloor; i++) yield return 0;
            for (int i = 0; i < 200; i++) yield return 0; // the jetpack fills again underfoot
        }
    }

    /// <summary>Presses a key for one tick.</summary>
    public IEnumerable<int> Tap(Key key)
    {
        Controls.Tap(key);
        yield return 0;
    }

    /// <summary>One click of the primary button on what is under the crosshair.</summary>
    public IEnumerable<int> Click()
    {
        Controls.PrimaryPressed = true;
        Controls.Primary = true;
        yield return 0;
        Controls.Primary = false;
        yield return 0;
    }

    /// <summary>Holds the primary button, looking at a point, for a while or until a condition holds.</summary>
    public IEnumerable<int> HoldPrimary(Vector3? at, int maxTicks, Func<bool> until = null)
    {
        for (int i = 0; i < maxTicks && !(until?.Invoke() ?? false); i++)
        {
            if (at is Vector3 p) Face(p);
            Controls.Primary = true;
            yield return 0;
        }
        Controls.Primary = false;
        yield return 0;
    }

    // ---- The Tab menu, by its own mouse --------------------------------------------------------

    /// <summary>Where a menu widget is (the menu lays itself out on its first input pass after opening).</summary>
    public IEnumerable<int> UntilWidget(string id, int ticks = 4)
    {
        for (int i = 0; i < ticks && Game.Menu.RectOf(id) == null; i++) yield return 0;
        if (Game.Menu.RectOf(id) == null)
            throw new InvalidOperationException($"no widget '{id}' on the menu (tab {Game.Menu.Tab}; {string.Join(", ", Game.Menu.Root.WidgetIds.Where(w => !w.StartsWith("bag:") && !w.StartsWith("bar:")).Take(40))})");
    }

    /// <summary>Moves the mouse onto a menu widget and clicks it (Shift held for a quick move).</summary>
    public IEnumerable<int> ClickWidget(string id, bool shift = false)
    {
        foreach (int x in UntilWidget(id)) yield return x;
        Controls.Mouse = Game.Menu.RectOf(id).Value.GetCenter();
        Controls.Hold(Key.Shift, shift);
        yield return 0;
        Controls.PrimaryPressed = true;
        Controls.Primary = true;
        yield return 0;
        Controls.Primary = false;
        Controls.PrimaryReleased = true;
        yield return 0;
        Controls.Hold(Key.Shift, false);
        yield return 0;
    }

    /// <summary>Clicks a menu widget with Ctrl and/or Shift held (selection).</summary>
    public IEnumerable<int> ClickWidgetWith(string id, bool ctrl = false, bool shift = false)
    {
        foreach (int x in UntilWidget(id)) yield return x;
        Controls.Mouse = Game.Menu.RectOf(id).Value.GetCenter();
        Controls.Hold(Key.Ctrl, ctrl);
        Controls.Hold(Key.Shift, shift);
        yield return 0;
        Controls.PrimaryPressed = true;
        Controls.Primary = true;
        yield return 0;
        Controls.Primary = false;
        Controls.PrimaryReleased = true;
        yield return 0;
        Controls.Hold(Key.Ctrl, false);
        Controls.Hold(Key.Shift, false);
        yield return 0;
    }

    /// <summary>Two quick clicks on a menu widget (a double-click).</summary>
    public IEnumerable<int> DoubleClickWidget(string id)
    {
        foreach (int x in ClickWidget(id)) yield return x;
        foreach (int x in ClickWidget(id)) yield return x;
    }

    /// <summary>Drags from one widget to another with the right button held (half a stack; Ctrl: one); the wheel ticks change the amount on the way.</summary>
    public IEnumerable<int> RightDragWidget(string from, string to, bool ctrl = false, int wheel = 0)
    {
        foreach (int x in UntilWidget(from)) yield return x;
        foreach (int x in UntilWidget(to)) yield return x;
        Vector2 a = Game.Menu.RectOf(from).Value.GetCenter(), b = Game.Menu.RectOf(to).Value.GetCenter();
        Controls.Mouse = a;
        Controls.Hold(Key.Ctrl, ctrl);
        yield return 0;
        Controls.SecondaryPressed = true;
        Controls.Secondary = true;
        yield return 0;
        for (int i = 1; i <= 4; i++)
        {
            Controls.Secondary = true;
            Controls.Mouse = a.Lerp(b, i / 4f);
            if (i == 2) Controls.Wheel = wheel;
            yield return 0;
        }
        Controls.Secondary = false;
        Controls.SecondaryReleased = true;
        yield return 0;
        Controls.Hold(Key.Ctrl, false);
        yield return 0;
    }

    /// <summary>Holds the mouse over a widget with the left button held for a while (a drag in progress), then lets go over another.</summary>
    public IEnumerable<int> DragWidgetVia(string from, string via, int hoverTicks, string to)
    {
        foreach (int x in UntilWidget(from)) yield return x;
        foreach (int x in UntilWidget(via)) yield return x;
        Vector2 a = Game.Menu.RectOf(from).Value.GetCenter(), v = Game.Menu.RectOf(via).Value.GetCenter();
        Controls.Mouse = a;
        yield return 0;
        Controls.PrimaryPressed = true;
        Controls.Primary = true;
        yield return 0;
        for (int i = 1; i <= 4; i++)
        {
            Controls.Primary = true;
            Controls.Mouse = a.Lerp(v, i / 4f);
            yield return 0;
        }
        for (int i = 0; i < hoverTicks; i++)
        {
            Controls.Primary = true;
            yield return 0;
        }
        foreach (int x in UntilWidget(to)) yield return x;
        Controls.Mouse = Game.Menu.RectOf(to).Value.GetCenter();
        Controls.Primary = true;
        yield return 0;
        Controls.Primary = false;
        Controls.PrimaryReleased = true;
        yield return 0;
        yield return 0;
    }

    /// <summary>Right-clicks a menu widget (splits a stack).</summary>
    public IEnumerable<int> RightClickWidget(string id)
    {
        foreach (int x in UntilWidget(id)) yield return x;
        Controls.Mouse = Game.Menu.RectOf(id).Value.GetCenter();
        yield return 0;
        Controls.SecondaryPressed = true;
        Controls.Secondary = true;
        yield return 0;
        Controls.Secondary = false;
        Controls.SecondaryReleased = true;
        yield return 0;
    }

    /// <summary>Drags from one menu widget to another with the left button held, a few ticks on the way.</summary>
    public IEnumerable<int> DragWidget(string from, string to)
    {
        foreach (int x in UntilWidget(from)) yield return x;
        foreach (int x in UntilWidget(to)) yield return x;
        Vector2 a = Game.Menu.RectOf(from).Value.GetCenter(), b = Game.Menu.RectOf(to).Value.GetCenter();
        Controls.Mouse = a;
        yield return 0;
        Controls.PrimaryPressed = true;
        Controls.Primary = true;
        yield return 0;
        for (int i = 1; i <= 4; i++)
        {
            Controls.Primary = true;
            Controls.Mouse = a.Lerp(b, i / 4f);
            yield return 0;
        }
        Controls.Primary = false;
        Controls.PrimaryReleased = true;
        yield return 0;
        yield return 0;
    }

    // ---- Vessels ----------------------------------------------------------------------------

    /// <summary>Walks to the front of a structure's hatch (its -z side) and in through it, the way a player would.</summary>
    public IEnumerable<int> EnterThroughHatch(Vessel v, float depth = 1.2f)
    {
        Vector3 front = v.Body.ToParent(new N.Vector3(0f, 0f, -4.6f)).ToGodot();
        Vector3 inside = v.Body.ToParent(new N.Vector3(0f, 0.3f, -depth)).ToGodot();
        if (Game.Survivor.Aboard != v) foreach (int x in WalkTo(front, 0.6f, 30f)) yield return x;
        foreach (int x in WalkTo(inside, 0.4f, 10f)) yield return x;
    }

    /// <summary>Walks out of a structure through its hatch onto the ground in front of it.</summary>
    public IEnumerable<int> WalkOutOfHatch(Vessel v, float distance = 5f)
    {
        Vector3 inside = v.Body.ToParent(new N.Vector3(0f, 0.3f, -1.2f)).ToGodot();
        Vector3 outside = v.Body.ToParent(new N.Vector3(0f, 0f, -1.75f - distance)).ToGodot();
        foreach (int x in WalkTo(inside, 0.4f, 10f)) yield return x;
        foreach (int x in WalkTo(outside, 0.6f, 15f)) yield return x;
    }

    /// <summary>Walks the bunker's way out, from its chamber up the stairwell to the ground beyond.</summary>
    public IEnumerable<int> ClimbOut(Slice.Start.Bunker b) => Follow(b.WayOut, "out");

    /// <summary>Walks back down into the bunker, to just outside the cryopod.</summary>
    public IEnumerable<int> ClimbIn(Slice.Start.Bunker b) => Follow(b.WayOut.Reverse().ToList(), "in");

    bool _following;

    /// <summary>Jumps the walker needed on the bunker's stairs and at its way out (there should be none).</summary>
    public int StairJumps;

    /// <summary>What is under and in front of the feet (a diagnostic for a snag).</summary>
    string Probe(Vector3 feet)
    {
        var space = Game.Zone.GetWorld3D().DirectSpaceState;
        Vector3 look = Game.Survivor.WorldLook();
        look.Y = 0f;
        look = look.Normalized();
        string Hit(Vector3 a, Vector3 b)
        {
            var h = space.IntersectRay(PhysicsRayQueryParameters3D.Create(a, b, GalHack.Physics.Layers.World | GalHack.Physics.Layers.Hull | GalHack.Physics.Layers.Props));
            return h.Count == 0 ? "nothing" : $"{((Node)h["collider"].AsGodotObject()).GetPath()} at {((Vector3)h["position"]).Y - feet.Y:+0.00;-0.00}/{((Vector3)h["position"]).DistanceTo(a):F2} m";
        }
        Vector3 q = feet + look * 0.6f;
        var store = Game.Zone.Planet.Terrain.Store;
        string column = string.Join("", Enumerable.Range(-2, 5).Select(dy => store.MaterialAt(new TerrainDigging.Terrain.Int3((int)MathF.Floor(q.X), (int)MathF.Floor(feet.Y) + dy, (int)MathF.Floor(q.Z))) == TerrainDigging.Terrain.CellMaterial.Air ? "." : "#"));
        return $"natural ground 0.6 m ahead {Game.Zone.GroundAt(q.X, q.Z).Y - feet.Y:+0.00;-0.00}, cells there from {(int)MathF.Floor(feet.Y) - 2} up: {column}; down {Hit(feet + Vector3.Up * 0.5f, feet + Vector3.Down * 1f)}; ahead at 0.1 m {Hit(feet + Vector3.Up * 0.1f, feet + Vector3.Up * 0.1f + look * 0.8f)}, at 0.4 m {Hit(feet + Vector3.Up * 0.4f, feet + Vector3.Up * 0.4f + look * 0.8f)}, at 0.7 m {Hit(feet + Vector3.Up * 0.7f, feet + Vector3.Up * 0.7f + look * 0.8f)}; ground 0.6 m ahead {Hit(feet + look * 0.6f + Vector3.Up * 1.5f, feet + look * 0.6f + Vector3.Down * 1f)}";
    }

    IEnumerable<int> Follow(IReadOnlyList<Vector3> way, string what)
    {
        _following = true;
        // From the waypoint nearest where you are (part way up the stairs, say).
        int first = 0;
        Vector3 here = Game.Survivor.WorldFeet;
        for (int i = 1; i < way.Count; i++)
            if (way[i].DistanceTo(here) < way[first].DistanceTo(here)) first = i;
        for (int i = first; i < way.Count; i++)
        {
            // At a walk, as a player takes stairs; again if it slid past.
            for (int tries = 0; tries < 3; tries++)
            {
                // No hops on the stairs: a jump there lands on the kerb or the roof beside them.
                foreach (int x in WalkTo(way[i], 0.45f, 30f, sprint: false, dig: false, hop: false)) yield return x;
                Vector3 at = Game.Survivor.WorldFeet;
                if (new Vector2(at.X - way[i].X, at.Z - way[i].Z).Length() <= 0.8f) break;
            }
            Vector3 f = Game.Survivor.WorldFeet;
            if (new Vector2(f.X - way[i].X, f.Z - way[i].Z).Length() > 0.8f || MathF.Abs(f.Y - way[i].Y) > 1.3f)
                GD.Print($"  (the way {what}: short of waypoint {i} of {way.Count} at {way[i]}: feet {f}, on floor {Game.Survivor.Body.OnFloor})");
        }
        _following = false;
    }

    /// <summary>On the open ground (not down in the bunker).</summary>
    public bool OnSurface => Game.Survivor.WorldFeet.Y > Game.Zone.GroundAt(Game.Survivor.WorldFeet.X, Game.Survivor.WorldFeet.Z).Y - 1.0f;

    /// <summary>Walks up to a vessel's seat and takes it.</summary>
    /// <summary>What stands in the way of the feet, ankle and chest high, along the look.</summary>
    IEnumerable<string> Blockers()
    {
        Vector3 feet = Game.Survivor.WorldFeet, look = Game.Survivor.WorldLook() with { Y = 0f };
        foreach (float up in new[] { 0.3f, 1.2f })
        {
            var r = Game.Zone.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(feet + Vector3.Up * up, feet + Vector3.Up * up + look.Normalized() * 2f));
            yield return r.Count == 0 ? "clear" : $"{((Node)r["collider"].AsGodotObject()).GetPath()} at {((Vector3)r["position"]).DistanceTo(feet + Vector3.Up * up):F1} m";
        }
    }

    public IEnumerable<int> Board(Vessel v)
    {
        Vector3 seat = v.Body.ToParent(v.Seat.Value).ToGodot();
        // Never digging: the hull in the way isn't a bank to dig through, and a hole beside a landed vessel wakes it.
        foreach (int x in WalkTo(seat, 1.6f, 30f, dig: false)) yield return x;
        // The hull or a slope can keep the seat out of reach from the ground: up on the jetpack, as a player would.
        float Reach() => v.NearestChair(Game.Survivor.WorldEye(), out _);
        for (int i = 0; i < 240 && Reach() > 2.8f; i++)
        {
            Face(seat);
            Controls.Hold(Key.W);
            Controls.Hold(Key.Space, Game.Survivor.WorldEye().Y < seat.Y + 0.8f);
            yield return 0;
        }
        Controls.Hold(Key.W, false);
        Controls.Hold(Key.Space, false);
        foreach (int x in LookAt(seat, 4)) yield return x;
        if (Reach() > 3.2f) GD.Print($"  (boarding the {v.Name}: the eye at {Game.Survivor.WorldEye()} is {Reach():F1} m from its chair at {seat}; feet {Game.Survivor.WorldFeet}, ground {Game.Zone.GroundAt(Game.Survivor.WorldFeet.X, Game.Survivor.WorldFeet.Z).Y:F2}, on the floor {Game.Survivor.Body.OnFloor}, bike {Game.Survivor.OnBike != null}, seated {Game.Survivor.Seated?.Name}, menu {Game.Menu.IsOpen}, dead {Game.Suit.Dead}, hit {string.Join(" / ", Blockers())})");
        foreach (int x in Tap(Key.F)) yield return x;
        for (int i = 0; i < 5; i++) yield return 0;
        // The walk ended out of reach (on a hull or a ruin, or walled off from the seat): back down beside
        // the seat on one side or the other, over what's in the way on the jetpack, and again.
        for (int attempt = 0; attempt < 3 && Game.Survivor.Seated != v; attempt++)
        {
            seat = v.ToWorld(v.Seat.Value);
            Vector3 side = MathX.Rotate(v.Body.Rotation, N.Vector3.UnitX).ToGodot() * (attempt % 2 == 0 ? 1f : -1f);
            Vector3 beside = seat + side * 2.2f;
            beside.Y = Game.Zone.GroundAt(beside.X, beside.Z).Y;
            foreach (int x in WalkTo(beside, 0.5f, 20f, sprint: false, dig: false)) yield return x;
            foreach (int x in LookAt(seat, 4)) yield return x;
            foreach (int x in Tap(Key.F)) yield return x;
            for (int i = 0; i < 5; i++) yield return 0;
        }
    }

    public void ReleaseAll() => Controls.ReleaseAll();
}
