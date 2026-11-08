using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;
using Slice.Game;
using Slice.Player;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>
/// Crouching, on a fresh random world, through the game's own keys: hold Ctrl and the eye eases
/// down while the capsule shortens and the walk slows; a 1.2 m passage can't be walked standing and
/// is walked crouched; under it, letting go of Ctrl doesn't stand you up (no room) until you are out
/// from under; a crouched hop is low, and the jetpack still lifts a crouched body; and in shallow
/// water a crouch goes down and stays there, steady, never setting you swimming.
///
/// Run: .\tools\test.ps1 -Project prototypes/slice -Scene res://tests/CrouchTest.tscn
/// </summary>
public partial class CrouchTest : Node
{
    const int WatchdogMinutes = 10;
    Game.Game _game;
    Driver _drive;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();

    Survivor S => _game.Survivor;
    Controls Keys => _game.Controls;
    float Z => S.WorldFeet.Z;
    float Speed => new Vector2(S.Body.Velocity.X, S.Body.Velocity.Z).Length();

    public override void _Ready()
    {
        var thread = new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(WatchdogMinutes));
            Console.Error.WriteLine($"FAIL  the run took over {WatchdogMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true };
        thread.Start();
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

    /// <summary>Waits until a condition holds, for at most this many ticks.</summary>
    static IEnumerable<int> Until(Func<bool> done, int ticks)
    {
        for (int i = 0; i < ticks && !done(); i++) yield return 0;
    }

    IEnumerable<int> Scenario()
    {
        _game = new Game.Game { Interactive = false, SkipIntro = false, World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Tested) };
        AddChild(_game);
        _drive = new Driver(_game);
        foreach (int x in _drive.UntilPlaying()) yield return x;
        _game.Zone.Weather.Bridge.Calm = true;
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        _game.Zone.Wildlife.Frozen = true;
        // Out of the cryopod and up the bunker's stairs to the surface, on foot.
        foreach (int x in _drive.Tap(Key.F)) yield return x;
        foreach (int x in _drive.Wait(40)) yield return x;
        foreach (int x in _drive.WalkTo(_game.Bunker.CryoFront, 0.3f, 10f)) yield return x;
        foreach (int x in _drive.ClimbOut(_game.Bunker)) yield return x;

        // A level pad with a 1.2 m passage in a bulkhead, on flat ground near the way out.
        Vector3 spot = new Playthrough(_game).FlatSpot(S.WorldFeet, 20f, span: 10f);
        foreach (int x in _drive.Wait(20)) yield return x;
        CrouchRig rig = CrouchRig.Build(_game, spot);
        S.PlaceOutside(rig.Start.ToNumerics());
        S.Yaw = 0f;
        S.Pitch = 0f;
        foreach (int x in Until(() => S.Body.OnFloor, 120)) yield return x;
        foreach (int x in _drive.Wait(30)) yield return x;
        GD.Print($"-- the passage is {CrouchRig.Gap:F1} m high, mouth at z {rig.Near:F1}, start z {Z:F1} --");
        Check(S.Body.OnFloor && !S.Crouched && Mathf.IsEqualApprox(S.CapsuleHeight, Survivor.Height) && Mathf.IsEqualApprox(S.EyeAbove, Survivor.EyeHeight, 0.01f),
            $"on the pad, standing: capsule {S.CapsuleHeight:F2} m, eye {S.EyeAbove:F2} m");

        // ---- Holding Ctrl: the capsule drops at once, the eye glides down ----------------------------
        var eyes = new List<float>();
        Keys.Hold(Key.Ctrl);
        for (int i = 0; i < 45; i++)
        {
            yield return 0;
            eyes.Add(S.EyeAbove);
        }
        bool falling = true;
        for (int i = 1; i < eyes.Count; i++) falling &= eyes[i] <= eyes[i - 1] + 1e-4f;
        Check(S.Crouched && Mathf.IsEqualApprox(S.CapsuleHeight, Survivor.CrouchHeight) && S.Body.Shape.Height == Survivor.CrouchHeight,
            $"Ctrl crouches: the capsule shortens to {S.CapsuleHeight:F2} m (the body's stuck-check shape {S.Body.Shape.Height:F2} m)");
        Check(falling && eyes.Any(e => e > Survivor.CrouchEyeHeight + 0.15f && e < Survivor.EyeHeight - 0.15f) && Mathf.Abs(eyes[^1] - Survivor.CrouchEyeHeight) < 0.02f,
            $"the eye eases down, not in one jump: {string.Join(" ", eyes.Take(8).Select(e => e.ToString("F2")))} ... {eyes[^1]:F2} m");
        Check(_game.Target.Crouched, "wildlife is told: the player's target is crouched");

        // ---- Crouch-walking is slow ------------------------------------------------------------------
        Keys.Hold(Key.W);
        foreach (int x in _drive.Wait(40)) yield return x;
        float crouchSpeed = Speed;
        Keys.Hold(Key.Shift);
        foreach (int x in _drive.Wait(20)) yield return x;
        float crouchSprint = Speed;
        Keys.ReleaseAll();
        Check(crouchSpeed > 1.8f && crouchSpeed < S.Legs.WalkSpeed * 0.6f, $"crouch-walking is slow: {crouchSpeed:F1} m/s against a walk of {S.Legs.WalkSpeed:F1}");
        Check(crouchSprint < S.Legs.WalkSpeed * 0.6f, $"and Shift doesn't sprint it ({crouchSprint:F1} m/s)");

        // ---- Standing can't get through the passage ----------------------------------------------------
        S.PlaceOutside(rig.Start.ToNumerics());
        S.Yaw = 0f;
        foreach (int x in _drive.Wait(30)) yield return x;
        Check(!S.Crouched, "Ctrl let go: standing again, there being room");
        Keys.Hold(Key.W);
        foreach (int x in _drive.Wait(300)) yield return x;
        float stoodAt = Z;
        Keys.ReleaseAll();
        Check(stoodAt > rig.Near && Speed < 0.5f, $"walking upright into the 1.2 m passage stops at its mouth (z {stoodAt:F2}, mouth {rig.Near:F2})");

        // ---- Crouched, it goes through ----------------------------------------------------------------
        S.PlaceOutside(rig.Start.ToNumerics());
        S.Yaw = 0f;
        foreach (int x in _drive.Wait(30)) yield return x;
        Keys.Hold(Key.Ctrl);
        Keys.Hold(Key.W);
        bool crouchedAllTheWay = true;
        float lowest = 9f;
        for (int i = 0; i < 900 && Z > rig.Far - 2f; i++)
        {
            yield return 0;
            if (Z < rig.Near && Z > rig.Far) crouchedAllTheWay &= S.Crouched;
            lowest = MathF.Min(lowest, S.Body.WorldPosition.Y - rig.Top);
        }
        Check(Z < rig.Far - 1.5f && crouchedAllTheWay, $"crouched, it walks the whole passage and out the far end (z {Z:F2}, end {rig.Far:F2}); lowest feet {lowest:F2} m over the pad");

        // ---- Back in, then let go of Ctrl: no room to stand ----------------------------------------------
        Keys.Hold(Key.W, false);
        Keys.Hold(Key.S);
        foreach (int x in Until(() => Z > (rig.Near + rig.Far) / 2f, 600)) yield return x;
        Keys.ReleaseAll();
        foreach (int x in _drive.Wait(90)) yield return x;
        bool under = Z < rig.Near - 0.5f && Z > rig.Far + 0.5f;
        Check(under && S.Crouched && !S.CanStand() && S.EyeAbove < Survivor.CrouchEyeHeight + 0.03f && S.Body.Shape.Height == Survivor.CrouchHeight,
            $"under the lintel, Ctrl let go: still crouched, no room to stand (eye {S.EyeAbove:F2} m, z {Z:F2})");

        // ---- Out from under: it stands up -------------------------------------------------------------------
        Keys.Hold(Key.W);
        bool stillLowAtTheLip = false;
        for (int i = 0; i < 600 && S.Crouched; i++)
        {
            yield return 0;
            if (S.Crouched && Z > rig.Far - 0.1f && Z < rig.Far + 0.4f) stillLowAtTheLip = true;
        }
        float stoodUpAt = Z;
        Check(!S.Crouched && stoodUpAt < rig.Far && stillLowAtTheLip, $"walking out it stands up once it is clear of the lintel (z {stoodUpAt:F2}, far end {rig.Far:F2})");
        foreach (int x in _drive.Wait(60)) yield return x;
        Check(Mathf.IsEqualApprox(S.CapsuleHeight, Survivor.Height) && Mathf.Abs(S.EyeAbove - Survivor.EyeHeight) < 0.02f && Speed > S.Legs.WalkSpeed * 0.8f,
            $"and the eye comes back up ({S.EyeAbove:F2} m) and the walk quickens ({Speed:F1} m/s)");
        Keys.ReleaseAll();

        // ---- Hops and the jetpack from a crouch ---------------------------------------------------------------
        S.PlaceOutside(rig.Start.ToNumerics());
        S.Yaw = 0f;
        foreach (int x in _drive.Wait(60)) yield return x;
        float standHop = 0f;
        foreach (int x in Hop(h => standHop = h)) yield return x;
        Keys.Hold(Key.Ctrl);
        foreach (int x in _drive.Wait(30)) yield return x;
        float crouchHop = 0f;
        foreach (int x in Hop(h => crouchHop = h)) yield return x;
        Check(crouchHop > 0.15f && crouchHop < standHop * 0.7f, $"a hop from a crouch is a low one: {crouchHop:F2} m against {standHop:F2} m standing");
        Check(S.Crouched, "and it stays crouched through the hop");
        float y0 = S.WorldFeet.Y, peak = y0;
        Keys.Hold(Key.Space);
        for (int i = 0; i < 90; i++)
        {
            yield return 0;
            peak = MathF.Max(peak, S.WorldFeet.Y);
        }
        Keys.ReleaseAll();
        Check(peak - y0 > 3f && S.Legs.JetpackFuel < S.Legs.JetpackFuelMax, $"holding Space with Ctrl down still lights the jetpack: up {peak - y0:F1} m");
        foreach (int x in Until(() => S.Body.OnFloor, 600)) yield return x;
        foreach (int x in _drive.Wait(60)) yield return x;
        Check(S.Body.OnFloor && !S.Crouched && Mathf.IsEqualApprox(S.CapsuleHeight, Survivor.Height), "landed with Ctrl let go: standing, nothing stuck");

        // ---- Crouching in shallow water: down and steady, never set swimming or floated off ----------------
        GalHack.Physics.IWaterField dry = _game.Zone.Physics.Water;
        foreach (float depth in new[] { 0.6f, 0.85f, 1.05f })
        {
            _game.Zone.Physics.Water = new StillWater(rig.Top + depth);
            S.PlaceOutside(rig.Start.ToNumerics());
            foreach (int x in _drive.Wait(60)) yield return x;
            Keys.Hold(Key.Ctrl);
            foreach (int x in _drive.Wait(90)) yield return x;
            float eyeLo = float.MaxValue, eyeHi = float.MinValue, feetLo = float.MaxValue, feetHi = float.MinValue;
            bool down = true, swam = false;
            for (int i = 0; i < 120; i++)
            {
                yield return 0;
                down &= S.Crouched;
                swam |= S.Legs.Swimming;
                eyeLo = MathF.Min(eyeLo, S.EyeAbove); eyeHi = MathF.Max(eyeHi, S.EyeAbove);
                feetLo = MathF.Min(feetLo, S.WorldFeet.Y); feetHi = MathF.Max(feetHi, S.WorldFeet.Y);
            }
            Keys.ReleaseAll();
            foreach (int x in _drive.Wait(30)) yield return x;
            Check(down && !swam && eyeHi - eyeLo < 0.01f && feetHi - feetLo < 0.02f && Mathf.Abs(eyeHi - Survivor.CrouchEyeHeight) < 0.03f,
                $"crouched in {depth:F2} m of still water it stays down and steady: never swimming, the eye at {eyeLo:F2} to {eyeHi:F2} m, the feet within {(feetHi - feetLo) * 100f:F1} cm");
        }
        _game.Zone.Physics.Water = dry;
    }

    /// <summary>A sheet of still water at one level everywhere (the shallows a crouch is tried in).</summary>
    sealed class StillWater : GalHack.Physics.IWaterField
    {
        readonly float _level;
        public StillWater(float level) => _level = level;
        public bool SurfaceAt(System.Numerics.Vector3 point, out float surface)
        {
            surface = _level;
            return true;
        }
    }

    /// <summary>Taps jump on the ground and reports how high the feet rose.</summary>
    IEnumerable<int> Hop(Action<float> height)
    {
        float y0 = S.WorldFeet.Y, peak = y0;
        Keys.Hold(Key.Space);
        yield return 0;
        Keys.Hold(Key.Space, false);
        for (int i = 0; i < 90; i++)
        {
            yield return 0;
            peak = MathF.Max(peak, S.WorldFeet.Y);
        }
        foreach (int x in Until(() => S.Body.OnFloor, 120)) yield return x;
        foreach (int x in _drive.Wait(20)) yield return x;
        height(peak - y0);
    }
}
