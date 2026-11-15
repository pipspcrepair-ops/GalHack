using System;
using System.Collections.Generic;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using Slice.Places;
using Slice.Vessels;
using N = System.Numerics;

namespace Slice.Tests;

/// <summary>
/// The start world's lone trader or scout, from the ground: landed by its home place, passing overhead in daylight,
/// and passing over at night with its lights on. Frames into --dir (skyship-landed, skyship-overhead, skyship-night);
/// --role=trader|scout picks the role.
/// </summary>
public sealed partial class Staging
{
    IEnumerable<int> SkyshipView()
    {
        string dir = _args.GetValueOrDefault("dir", ".");
        System.IO.Directory.CreateDirectory(dir);
        _game.Zone.Wildlife.Frozen = true;
        _game.Visor.Visible = false;
        if (_args.TryGetValue("role", out string r) && Enum.TryParse(r, true, out GalHack.Structures.CraftRole role)) SkyTraffic.ForceRole = role;
        for (int i = 0; i < 600 && _game.Sky.Ship == null && _game.Sky.Missing == null; i++) yield return 0;
        Skyship ship = _game.Sky.Ship;
        if (ship == null) { GD.PrintErr($"skyship: none ({_game.Sky.Missing})"); yield break; }
        Vessel v = ship.Vessel;
        var sky = _game.Zone.Sky;
        sky.TimeOfDay = HighSun(_game);
        sky.Paused = true;

        // Landed by its home place: from the ground off its flank, the place beyond it.
        Vector3 at = v.Body.Position.ToGodot(), home = _game.Sky.CentreOf(ship.At, v.Body.Position).ToGodot();
        Vector3 away = (at - home) with { Y = 0f };
        away = away.LengthSquared() > 1f ? away.Normalized() : Vector3.Back;
        Vector3 side = away.Cross(Vector3.Up).Normalized();
        Vector3 eye = SkyGround(at + away * 26f + side * 14f, 1.7f);
        foreach (int x in SkyHold(eye, () => v.Body.Position.ToGodot() + Vector3.Up * 1.5f, 300)) yield return x;
        GD.Print($"skyship: landed {ship.Phase}, {eye.DistanceTo(v.Body.Position.ToGodot()):F0} m off, by {ship.At?.Family.Name}");
        foreach (int x in Shot($"{dir}/skyship-landed.png")) yield return x;

        // Overhead in daylight, then at night with its lights.
        Skyship.WaitScale = 0.05f;
        foreach (var (name, time) in new[] { ("overhead", HighSun(_game)), ("night", HighSun(_game, highest: false)) })
        {
            sky.TimeOfDay = time;
            for (int i = 0; i < 60 * 240 && ship.Phase is not (SkyPhase.Cruising or SkyPhase.Approaching); i++)
            {
                _game.FixedCamera = null;
                yield return 0;
            }
            // On the ground where it will be in a while, looking up at it as it comes over.
            N.Vector3 p = v.Body.Position, vel = MathX.Flat(v.Body.Velocity);
            Vector3 under = SkyGround((p + vel * 9f).ToGodot() + side * 25f, 1.7f);
            foreach (int x in SkyHold(under, () => v.Body.Position.ToGodot(), 60)) yield return x;
            for (int i = 0; i < 60 * 20 && MathX.Flat(v.Body.Position - under.ToNumerics()).Length() > 90f; i++)
            {
                _game.FixedCamera = SkyLook(under, v.Body.Position.ToGodot());
                yield return 0;
            }
            GD.Print($"skyship: {name} {ship.Phase}, {under.DistanceTo(v.Body.Position.ToGodot()):F0} m off, {v.AboveGround:F0} m up, lights {(v.LightsOn ? "on" : "off")}, daylight {sky.Daylight:F2}");
            _game.FixedCamera = SkyLook(under, v.Body.Position.ToGodot());
            foreach (int x in Shot($"{dir}/skyship-{name}.png")) yield return x;
        }
        _game.FixedCamera = null;
    }

    Vector3 SkyGround(Vector3 p, float up) => p with { Y = _game.Zone.GroundAt(p.X, p.Z).Y + up };

    static Transform3D SkyLook(Vector3 eye, Vector3 at) => new(Basis.LookingAt((at - eye).Normalized(), Vector3.Up), eye);

    /// <summary>The sleeper stood just behind an eye (so the ground and places stream in there), the camera at it looking at a moving point for a while.</summary>
    IEnumerable<int> SkyHold(Vector3 eye, Func<Vector3> at, int ticks)
    {
        Vector3 back = (eye - at()) with { Y = 0f };
        back = back.LengthSquared() > 0.01f ? back.Normalized() : Vector3.Back;
        _game.Survivor.PlaceOutside(SkyGround(eye + back * 3f, 0.2f).ToNumerics());
        for (int i = 0; i < ticks; i++)
        {
            _game.FixedCamera = SkyLook(eye, at());
            yield return 0;
        }
    }
}
