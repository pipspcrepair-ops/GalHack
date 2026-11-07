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

    /// <summary>The largest turn rate the stick asks for, degrees a second (small vessels are capped).</summary>
    static float MaxTurn(Vessel v, Game.Game game)
    {
        float rate = v.Stats?.TurnRate ?? 60f;
        float cap = v.Structure.Grid.Size == GalHack.Structures.GridSize.Small && rate > 0f ? MathF.Min(1f, game.Zone.Data.Rules.Build.SmallVesselMaxTurn / rate) : 1f;
        return MathF.Max(10f, rate * cap);
    }

    /// <summary>Moves the mouse so the stick sits at a place next tick (x yaws right, y pitches down).</summary>
    void Stick(Vector2 wanted)
    {
        wanted = wanted.Clamp(new Vector2(-1, -1), new Vector2(1, 1));
        C.Look = (wanted - _stick * 0.85f) / 0.012f;
        _stick = (_stick * 0.85f + C.Look * 0.012f).Clamp(new Vector2(-1, -1), new Vector2(1, 1));
    }

    /// <summary>A new seat: the stick starts centred.</summary>
    public void Seated() => _stick = Vector2.Zero;

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
    public IEnumerable<int> FlyTo(Vessel v, Func<Vector3> target, float within, float seconds = 120f)
    {
        int ticks = (int)(seconds * 60);
        float brake = MathF.Max(8f, v.Stats?.Accel(Direction.Back) ?? 8f);
        for (int i = 0; i < ticks; i++)
        {
            Vector3 p = v.Body.Position.ToGodot(), to = target() - p;
            float d = to.Length(), speed = v.Body.Velocity.Length();
            float closing = v.Body.Velocity.ToGodot().Dot(to.Normalized());
            if (d < within && speed < 0.6f) break;
            float off = Aim(v, to);
            float stopping = closing * closing / (2f * brake) + within * 0.5f + 4f;
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
