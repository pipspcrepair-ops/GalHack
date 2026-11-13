using System;
using System.Collections.Generic;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using ShipHandling.Handling;
using Slice.Game;
using Slice.Vessels;
using N = System.Numerics;

namespace Slice.Tests;

/// <summary>
/// Flies and floats through the game's own controls, the way hands on a mouse and keys would: the
/// mouse moves the cockpit's stick (it recentres by itself, as in ship-handling), WASD and
/// Space/Ctrl thrust, Q/E roll; on foot in zero-g the suit's thrusters push along the look. No
/// teleports: every routine only presses keys and moves the mouse, one physics tick per step.
/// </summary>
public sealed class Pilot
{
    readonly Game.Game _game;
    readonly Driver _drive;
    int _tick;
    Vector2 _stick; // the game's own stick, mirrored: it eases toward the mouse and recentres (Game.Fly)

    public Pilot(Game.Game game, Driver drive)
    {
        _game = game;
        _drive = drive;
    }

    Controls C => _game.Controls;

    /// <summary>The largest turn rate the stick asks for, degrees a second (its mass band caps it).</summary>
    static float MaxTurn(Vessel v, Game.Game game) => MathF.Max(10f, v.Stats?.TurnRate ?? 60f);

    /// <summary>Moves the mouse so the stick sits at a place next tick (x yaws right, y pitches down).</summary>
    void Stick(Vector2 wanted)
    {
        wanted = wanted.Clamp(new Vector2(-1, -1), new Vector2(1, 1));
        C.Look = (wanted - _stick * 0.85f) / 0.012f;
        _stick = (_stick * 0.85f + C.Look * 0.012f).Clamp(new Vector2(-1, -1), new Vector2(1, 1));
    }

    /// <summary>A new seat: the stick starts centred.</summary>
    public void Seated() => _stick = Vector2.Zero;

    /// <summary>
    /// SETUP SHORTCUT (printed): the Skiff's parts handed over and the tech tree opened; then its
    /// blueprint set down on flat ground near the start and built block by block, by hand. Null if
    /// the projector set nothing down.
    /// </summary>
    public IEnumerable<int> BuildSkiff(Action<Vessel> built)
    {
        foreach (int x in new Playthrough(_game).MakeTool()) yield return x; // you wake empty-handed: the tool is made first
        foreach (var (item, n) in _game.Builder.TotalCost(_game.StarterPlan)) _game.Pack.Add(item, n);
        _game.Progress.Creative = true;
        GD.Print("  (setup: handed over the Skiff's parts and opened the tech tree)");
        _game.Pack.Grid.SetBlock(3, Slice.Items.PlayerInventory.BlueprintBlock);
        Vector3 pad = default;
        Vessel skiff = null;
        // A walk the ground blocks (a wall, a gully) leaves you too far off, so a few pads farther out are tried.
        for (int attempt = 0; attempt < 3 && skiff == null; attempt++)
        {
            pad = new Playthrough(_game).FlatSpot(_game.Start, 18f + attempt * 14f, span: 10f); // room for you to stand off it and see it
            Vector3 stand = pad + (pad - _game.Start).Normalized() * 7f;
            foreach (int x in _drive.WalkTo(new Vector3(stand.X, 0, stand.Z), 1.5f)) yield return x;
            if (_game.Pack.Selected != 3) foreach (int x in _drive.Tap(Key.Key4)) yield return x;
            foreach (int x in _drive.LookAt(pad, 6)) yield return x;
            // The ground's collision may still be coming in where you look: the ghost shows once it can go down.
            for (int i = 0; i < 300 && !_game.Builder.PlanShown; i++)
                foreach (int x in _drive.LookAt(pad, 1)) yield return x;
            foreach (int x in _drive.Click()) yield return x;
            skiff = _game.Builder.Underway;
            if (skiff == null) GD.Print($"  (the blueprint set nothing down at {pad}, feet {_game.Survivor.WorldFeet}: {_game.Builder.Verdict})");
        }
        if (skiff == null)
        {
            GD.Print($"  (the projector: {_game.Builder.Verdict}; shown {_game.Builder.PlanShown}, plan {_game.Builder.Plan != null}; pad {pad}, slot {_game.Pack.Selected}, in hand {_game.Pack.InHand})");
            built(null);
            yield break;
        }
        // Set down, it builds from anywhere: you step back clear of all of it (it went down facing the way you looked, its tail
        // toward you, and a block you stand in the way of won't go on).
        var (lo, hi) = _game.StarterPlan.Bounds();
        float clear = 0.5f * new Vector2(hi.X - lo.X + 1, hi.Z - lo.Z + 1).Length() * _game.StarterPlan.Grid.CellSize + 3f;
        Vector3 middle = skiff.Body.Position.ToGodot(), away = (_game.Survivor.WorldFeet - middle) with { Y = 0f };
        foreach (int x in _drive.WalkTo((middle + away.Normalized() * clear) with { Y = 0f }, 1f)) yield return x;
        int stalls = 0;
        while (_game.Builder.Underway == skiff && stalls < 240)
        {
            int before = skiff.Structure.Count;
            _drive.Face(skiff.Body.Position.ToGodot());
            C.Primary = true;
            yield return 0;
            stalls = skiff.Structure.Count > before ? 0 : stalls + 1;
        }
        C.Primary = false;
        foreach (int x in _drive.Wait(5)) yield return x;
        built(skiff);
    }

    /// <summary>Walks up to a vessel's seat and takes it (F).</summary>
    public IEnumerable<int> Board(Vessel v)
    {
        foreach (int x in _drive.Board(v)) yield return x;
        Seated();
    }

    /// <summary>Points the nose along a world direction, rolling the top toward <paramref name="up"/> if given. Returns the angle still off, degrees.</summary>
    public float Aim(Vessel v, Vector3 direction, Vector3? up = null)
    {
        _tick++;
        N.Quaternion q = v.Body.Rotation;
        Vector3 d = MathX.Unrotate(q, direction.Normalized().ToNumerics()).ToGodot();
        float yaw = Mathf.RadToDeg(MathF.Atan2(d.X, -d.Z));
        float pitch = Mathf.RadToDeg(MathF.Atan2(d.Y, new Vector2(d.X, d.Z).Length()));
        float rate = MaxTurn(v, _game), lead = 0.45f;
        Stick(new Vector2(yaw / (rate * lead), -pitch / (rate * lead)));
        float roll = 0f;
        if (up is Vector3 u)
        {
            Vector3 lu = MathX.Unrotate(q, u.Normalized().ToNumerics()).ToGodot();
            roll = Mathf.RadToDeg(MathF.Atan2(-lu.X, lu.Y)); // positive: the wanted up is to the left, roll left (Q)
            // Roll is on keys (full rate or none): pulsed when nearly there, so it settles instead of rocking.
            bool on = MathF.Abs(roll) > 1.5f && (MathF.Abs(roll) > 25f || _tick % 3 == 0);
            C.Hold(Key.Q, on && roll > 0f);
            C.Hold(Key.E, on && roll < 0f);
        }
        float off = Mathf.RadToDeg(MathF.Acos(Math.Clamp(-d.Z, -1f, 1f)));
        return MathF.Max(off, MathF.Abs(roll));
    }

    /// <summary>
    /// Flies to within <paramref name="within"/> of a point and stops there: nose on it, full
    /// thrust while far, let go in time for the assist to brake. Returns when stopped there or
    /// after the time limit.
    /// </summary>
    /// <param name="through">A waypoint: fly on through it at speed rather than stopping there.</param>
    public IEnumerable<int> FlyTo(Vessel v, Func<Vector3> target, float within, float seconds = 120f, bool through = false)
    {
        int ticks = (int)(seconds * 60);
        float brake = MathF.Max(8f, v.Stats?.Accel(Direction.Back) ?? 8f);
        for (int i = 0; i < ticks; i++)
        {
            Vector3 p = v.Body.Position.ToGodot(), to = target() - p;
            float d = to.Length(), speed = v.Body.Velocity.Length();
            float closing = v.Body.Velocity.ToGodot().Dot(to.Normalized());
            if (d < within && (through || speed < 0.6f)) break;
            float off = Aim(v, to);
            float stopping = through ? 0f : closing * closing / (2f * brake) + within * 0.5f + 4f;
            bool thrust = off < 12f && d > stopping;
            C.Hold(Key.W, thrust);
            yield return 0;
        }
        C.ReleaseAll();
        Stick(Vector2.Zero);
        yield return 0;
    }

    /// <summary>Turns to an attitude (the nose along <paramref name="forward"/>, the top toward <paramref name="up"/>), holding position.</summary>
    public IEnumerable<int> TurnTo(Vessel v, Vector3 forward, Vector3 up, float within = 1.5f, float seconds = 30f)
    {
        int ticks = (int)(seconds * 60), steady = 0;
        for (int i = 0; i < ticks && steady < 20; i++)
        {
            float off = Aim(v, forward, up);
            steady = off < within && v.Body.AngularVelocity.Length() < 0.02f ? steady + 1 : 0;
            yield return 0;
        }
        C.ReleaseAll();
        Stick(Vector2.Zero);
        yield return 0;
    }

    /// <summary>Down on the gear, as a pilot would: the height under it sets the sink rate, sideslipping toward a point if given. Returns once it has settled or after the time limit.</summary>
    public IEnumerable<int> Land(Vessel v, Vector3? over = null, float seconds = 150f)
    {
        for (int i = 0; i < seconds * 60 && !v.Controller.Frozen; i++)
        {
            float agl = v.AboveGround, vy = v.Body.Velocity.Y;
            float wanted = agl > 260f ? -50f : agl > 70f ? -16f : agl > 25f ? -6f : agl > 6f ? -2.5f : -1f;
            bool onGear = agl < 3f && MathF.Abs(vy) < 0.4f; // hands off, and it settles
            C.Hold(Key.Ctrl, !onGear && vy > wanted + 0.5f);
            C.Hold(Key.Space, !onGear && vy < wanted - 0.5f);
            if (over is Vector3 p) Creep(v, p, agl > 3f);
            yield return 0;
        }
        C.ReleaseAll();
    }

    /// <summary>Sideslips gently toward a point on the way down, on the thrusters, keeping the heading.</summary>
    public void Creep(Vessel v, Vector3 over, bool on)
    {
        Vector3 to = MathX.Unrotate(v.Body.Rotation, (over - v.Body.Position.ToGodot()).ToNumerics()).ToGodot();
        Vector3 speed = MathX.Unrotate(v.Body.Rotation, v.Body.Velocity).ToGodot();
        C.Hold(Key.W, on && to.Z < -1.5f && speed.Z > -MathF.Min(8f, -to.Z * 0.3f));
        C.Hold(Key.S, on && to.Z > 1.5f && speed.Z < MathF.Min(8f, to.Z * 0.3f));
        C.Hold(Key.D, on && to.X > 1.5f && speed.X < MathF.Min(8f, to.X * 0.3f));
        C.Hold(Key.A, on && to.X < -1.5f && speed.X > -MathF.Min(8f, -to.X * 0.3f));
    }

    // ---- Floating ------------------------------------------------------------------------------

    /// <summary>
    /// On the suit's thrusters in zero-g: looks at a point and pushes toward it (W), coasting, then
    /// pushes back (S) to stop within reach. Returns when stopped near it or after the time limit.
    /// </summary>
    public IEnumerable<int> FloatTo(Func<Vector3> target, float within, float seconds = 60f)
    {
        int ticks = (int)(seconds * 60);
        var s = _game.Survivor;
        for (int i = 0; i < ticks; i++)
        {
            Vector3 eye = s.WorldEye(), to = target() - eye;
            float d = to.Length();
            Vector3 v = s.Body.WorldVelocity.ToGodot();
            float closing = v.Dot(to.Normalized());
            _drive.Face(target());
            bool there = d < within;
            if (there && v.Length() < 0.25f) break;
            // 7 m/s² of thruster: stop in v²/14 metres.
            bool push = !there && d > closing * closing / 14f + within * 0.6f;
            bool back = (there && closing > 0.05f) || (!push && closing > 0.4f);
            C.Hold(Key.W, push);
            C.Hold(Key.S, back && !push);
            yield return 0;
        }
        C.ReleaseAll();
        yield return 0;
    }

    /// <summary>Stops drifting (pushing against the way you move until still).</summary>
    public IEnumerable<int> Stop(float seconds = 6f)
    {
        var s = _game.Survivor;
        for (int i = 0; i < seconds * 60; i++)
        {
            Vector3 v = s.Body.WorldVelocity.ToGodot();
            if (v.Length() < 0.1f) break;
            _drive.Face(s.WorldEye() + v.Normalized() * 5f);
            C.Hold(Key.S);
            yield return 0;
        }
        C.ReleaseAll();
        yield return 0;
    }
}
