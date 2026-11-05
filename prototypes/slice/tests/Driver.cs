using System;
using System.Collections.Generic;
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
        float frameHeading = s.Body.Frame == null ? 0f : MathX.Heading(s.Body.Frame.Rotation);
        float yaw = MathF.Atan2(-d.X, -d.Z) - frameHeading;
        float pitch = MathF.Asin(Math.Clamp(d.Y, -1f, 1f));
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
    public IEnumerable<int> WalkTo(Vector3 target, float within, float seconds = 90f)
    {
        int ticks = (int)(seconds * Engine.PhysicsTicksPerSecond);
        float stuck = 0f, jet = 0f;
        Vector3 last = Game.Survivor.WorldFeet;
        for (int i = 0; i < ticks; i++)
        {
            Vector3 feet = Game.Survivor.WorldFeet;
            Vector3 to = target - feet;
            if (new Vector2(to.X, to.Z).Length() <= within) break;
            Face(new Vector3(target.X, Game.Survivor.WorldEye().Y, target.Z));
            Controls.Hold(Key.W);
            Controls.Hold(Key.Shift, Game.Suit.Vitals.Stamina > 30);
            float moved = (feet - last).Length();
            last = feet;
            stuck = moved < 0.02f ? stuck + 1f / 60f : 0f;
            if (stuck > 0.4f) jet = 0.7f;
            // Over an edge: the jetpack brakes the fall, as a careful player would.
            bool falling = !Game.Survivor.Body.OnFloor && Game.Survivor.Body.Velocity.Y < -5f;
            Controls.Hold(Key.Space, jet > 0f || falling);
            jet -= 1f / 60f;
            yield return 0;
        }
        Controls.Hold(Key.W, false);
        Controls.Hold(Key.Shift, false);
        Controls.Hold(Key.Space, false);
        for (int i = 0; i < 20; i++) yield return 0;
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

    // ---- Vessels ----------------------------------------------------------------------------

    /// <summary>Walks up to a vessel's seat and takes it.</summary>
    public IEnumerable<int> Board(Vessel v)
    {
        Vector3 seat = v.Body.ToParent(v.Seat.Value).ToGodot();
        foreach (int x in WalkTo(seat, 1.6f, 30f)) yield return x;
        foreach (int x in LookAt(seat, 4)) yield return x;
        foreach (int x in Tap(Key.F)) yield return x;
        for (int i = 0; i < 5; i++) yield return 0;
    }

    public void ReleaseAll() => Controls.ReleaseAll();
}
