using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using Slice.Places;
using Slice.Vessels;
using N = System.Numerics;

namespace Slice.Tests;

/// <summary>
/// The start world's lone trader or scout, from the ground: landed by its home place, passing overhead in daylight,
/// passing over at night with its lights on, and at night from about 800 m. Frames into --dir (skyship-landed, skyship-overhead,
/// skyship-night, skyship-night-far);
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
            under = Open(under);
            for (int i = 0; i < 60 * 20 && MathX.Flat(v.Body.Position - under.ToNumerics()).Length() > 90f; i++)
            {
                _game.FixedCamera = SkyLook(under, v.Body.Position.ToGodot());
                yield return 0;
            }
            GD.Print($"skyship: {name} {ship.Phase}, {under.DistanceTo(v.Body.Position.ToGodot()):F0} m off, {v.AboveGround:F0} m up, lights {(v.LightsOn ? "on" : "off")}, daylight {sky.Daylight:F2}");
            _game.FixedCamera = SkyLook(under, v.Body.Position.ToGodot());
            foreach (int x in Shot($"{dir}/skyship-{name}.png")) yield return x;
        }
        // At night from about 800 m off: its lamps carry as far points, as a town's do.
        Vector3 off = SkyGround(v.Body.Position.ToGodot() + side * 800f, 1.7f);
        foreach (int x in SkyHold(off, () => v.Body.Position.ToGodot(), 150)) yield return x;
        off = Open(off);
        _game.FixedCamera = SkyLook(off, v.Body.Position.ToGodot());
        GD.Print($"skyship: night-far {ship.Phase}, {off.DistanceTo(v.Body.Position.ToGodot()):F0} m off, {v.AboveGround:F0} m up, lights {(v.LightsOn ? "on" : "off")}");
        foreach (int x in Shot($"{dir}/skyship-night-far.png")) yield return x;
        _game.FixedCamera = null;
    }

    /// <summary>The nearest point to this one with no tree or crown within a few metres, for a camera to stand on.</summary>
    Vector3 Open(Vector3 p)
    {
        for (float r = 0f; r < 60f; r += 6f)
            for (int k = 0; k < (r == 0f ? 1 : 12); k++)
            {
                Vector3 q = SkyGround(p + new Vector3(MathF.Cos(k * 0.52f), 0f, MathF.Sin(k * 0.52f)) * r, 1.7f);
                if (!_game.Zone.Streamer.SolidsNear(q, 8f, canopies: true).Any()) return q;
            }
        return p;
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
