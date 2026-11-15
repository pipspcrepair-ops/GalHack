using System;
using System.Collections.Generic;
using GalHack.Physics.Engine;
using Godot;
using Slice.Game;
using Slice.Ui;
using Slice.Vessels;
using N = System.Numerics;

namespace Slice.Tests;

/// <summary>
/// Flight assist from the chair, through the game's own keys: off, a Skiff falls and a roll carries on with no
/// thrust between inputs, and the icon row shows active thrusters; on again, it eases to a hold; standing up, the
/// autopilot holds course; the hotkey can be moved and the settings file keeps it.
/// </summary>
public partial class CoreTest
{
    const string KeysFile = "user://core-test-keys.cfg";

    HudIcon ThrusterIcon(Vessel v) => _game.Visor.StructureIcons(v)[3].Icon;

    IEnumerable<int> Assist()
    {
        GD.Print("-- flight assist: off it falls and spins on, on again it holds --");
        _site = _play.FlatSpot(_game.Start, 150f, 4.2f, 0.4f, span: 14f);
        GD.Print($"  (a clearing: {Playthrough.Clearing(_game.Zone, _site, 50f)} trees and boulders felled)");
        _game.Survivor.PlaceOutside((_site + new Vector3(0f, 1f, 8f)).ToNumerics());
        foreach (int x in _drive.Wait(60)) yield return x;
        Vessel v = Vessel.Spawn(_game.Zone, CoreCraft.Skiff(Data), (_site + Vector3.Up * 3f).ToNumerics(), 0f, landed: true);
        v.Settle();
        foreach (int x in _drive.Wait(10)) yield return x;
        foreach (int x in Seat(v)) yield return x;
        if (!Check(_game.Survivor.Seated == v, "in the Skiff's chair")) yield break;

        foreach (int x in Hold(Key.Space, 300)) yield return x;
        foreach (int x in _drive.Wait(90)) yield return x;
        float high = v.AboveGround;
        Check(high > 30f && !v.Flight.State.AssistOff && ThrusterIcon(v) == HudIcon.ThrustersPassive,
            $"climbed {high:F0} m with assist on: the icon row shows passive thrusters");

        _game.Press(Key.I);
        foreach (int x in _drive.Wait(1)) yield return x;
        Check(v.Flight.State.AssistOff && ThrusterIcon(v) == HudIcon.ThrustersActive, "I switches assist off: the icon row shows active thrusters");
        float burnt = 0f;
        float vy0 = v.Body.Velocity.Y; // the climb may not have quite eased out yet
        for (int t = 0; t < 30; t++)
        {
            burnt = MathF.Max(burnt, v.Flight.State.ThrustLocal.Length());
            yield return 0;
        }
        float g = _game.Zone.Physics.Zone.Strength, fell = vy0 - v.Body.Velocity.Y;
        Check(MathF.Abs(fell - g * 0.5f) < 0.05f * g && burnt == 0f,
            $"hands off, it falls: {fell:F2} m/s faster down after half a second at {g:F1} m/s² ({vy0:+0.0;-0.0} to {v.Body.Velocity.Y:+0.0;-0.0}), no thrust fired (most {burnt:F3} m/s²)");

        foreach (int x in Hold(Key.Q, 8)) yield return x;
        N.Vector3 spin = v.Controller.LocalSpin;
        foreach (int x in _drive.Wait(30)) yield return x;
        N.Vector3 after = v.Controller.LocalSpin;
        Check(MathF.Abs(spin.Z) > 0.1f && N.Vector3.Distance(spin, after) < 1e-3f,
            $"a tap of roll left spinning: it rolls on at {Mathf.RadToDeg(after.Z):F1}°/s half a second later (was {Mathf.RadToDeg(spin.Z):F1})");

        _game.Press(Key.I);
        float last = v.Body.Velocity.Length(), worst = 0f;
        int ticks = 0;
        for (; ticks < 60 * 6 && (v.Body.Velocity.Length() > 0.05f || v.Controller.LocalSpin.Length() > 1e-4f); ticks++)
        {
            yield return 0;
            float now = v.Body.Velocity.Length();
            worst = MathF.Max(worst, now - last);
            last = now;
        }
        float held = v.Body.Position.Y;
        foreach (int x in _drive.Wait(60)) yield return x;
        Check(!v.Flight.State.AssistOff && ThrusterIcon(v) == HudIcon.ThrustersPassive && ticks < 60 * 6 && worst < 0.05f && MathF.Abs(v.Body.Position.Y - held) < 0.2f,
            $"I again: assist on, passive thrusters; the fall and the roll ease to a hold in {ticks / 60f:F1} s, never speeding up (worst {worst:+0.000} m/s a tick), then it holds ({v.AboveGround:F0} m up)");

        // Standing up with assist off: the autopilot holds course.
        _game.Press(Key.I);
        foreach (int x in _drive.Wait(1)) yield return x;
        _game.Survivor.StandUp();
        float stood = v.Body.Position.Y;
        foreach (int x in _drive.Wait(60)) yield return x;
        Check(v.Flight.State.AssistOff && MathF.Abs(v.Body.Position.Y - stood) < 0.5f,
            $"stood up with assist off: the autopilot holds it ({v.Body.Position.Y - stood:+0.00;-0.00} m in a second); the switch stays off");
        foreach (int x in Seat(v)) yield return x;
        Check(_game.Survivor.Seated == v && ThrusterIcon(v) == HudIcon.ThrustersActive, "seated again: still off, still active thrusters");

        // The hotkey moved to O: O works it and I no longer does; the settings file keeps it.
        KeyBindings keys = _game.Settings.Keys;
        keys.Bind("assist", Key.O);
        _game.Press(Key.I);
        foreach (int x in _drive.Wait(1)) yield return x;
        bool afterI = v.Flight.State.AssistOff;
        _game.Press(Key.O);
        foreach (int x in _drive.Wait(1)) yield return x;
        bool afterO = v.Flight.State.AssistOff;
        _game.Settings.Save(KeysFile);
        GameSettings read = GameSettings.Load(KeysFile);
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(KeysFile));
        keys.Bind("assist", Key.I);
        Check(afterI && !afterO && read.Keys["assist"] == Key.O && read.Keys["cruise"] == Key.C,
            $"the assist key moved to O: I does nothing, O switches it on; read back from the settings file it is on {KeyBindings.Name(read.Keys["assist"])}");

        _game.Survivor.StandUp();
        foreach (int x in _drive.Wait(5)) yield return x;
        _game.Survivor.PlaceOutside((_site + Vector3.Up).ToNumerics());
        Playthrough.TakeAway(_game.Zone, v);
    }
}
