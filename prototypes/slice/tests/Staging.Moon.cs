using System.Collections.Generic;
using GalHack.Physics.Engine;
using Godot;
using Slice.Orbit;
using Slice.Vessels;

namespace Slice.Tests;

/// <summary>To the moon: high orbit with the moon ahead, its ground close up, and the Skiff landed on it (the last frame).</summary>
public sealed partial class Staging
{
    IEnumerable<int> MoonView()
    {
        foreach (int x in ToOrbit()) yield return x;
        Vessel v = Skiff;
        OrbitZone o = _game.Orbit;
        Hurry(4);
        _game.Controls.Hold(Key.Space);
        for (int i = 0; i < 60 * 60 && _game.Spaceflight.Band < 1f; i++) yield return 0;
        _game.Controls.ReleaseAll();
        for (int i = 0; i < 60 * 300 && (o.Moon?.Ready != true || _game.Spaceflight.MoonSurface?.Made == null); i++) yield return 0;
        Celestial moon = o.Moon;
        if (moon == null) yield break;
        var (tx, tz) = moon.Map.MapPoint(moon.ToGlobe(v.Body.Position));
        Vector3 Over(float height) => moon.ToWorld(moon.Map.Direction(tx, tz) * moon.RadiusFor(height)).ToGodot();
        foreach (int x in _pilot.FlyTo(v, () => Over(3000f), 30f, 30f)) yield return x; // part of the way out
        foreach (int x in _pilot.Stop()) yield return x;
        Hurry(1);
        _game.Visor.Visible = true;
        LookFromBehind(v, moon.Centre);
        for (int i = 0; i < 12; i++) yield return 0;
        Snap("orbit");
        _game.FixedCamera = null;
        Hurry(4);
        foreach (int x in _pilot.FlyTo(v, () => Over(moon.Ceiling + o.Rules.Crossing.Band + 400f), 30f, 300f)) yield return x;
        Hurry(1);
        LookFromBehind(v, moon.Centre);
        for (int i = 0; i < 12; i++) yield return 0;
        Snap("over");
        _game.FixedCamera = null;
        Hurry(3);
        _game.Controls.Hold(Key.Ctrl);
        for (int i = 0; i < 60 * 90 && _game.InOrbit; i++) yield return 0;
        for (int i = 0; i < 60 * 90 && !v.Controller.Frozen; i++)
        {
            float agl = v.AboveGround, vy = v.Body.Velocity.Y;
            float wanted = agl > 260f ? -50f : agl > 70f ? -16f : agl > 25f ? -6f : agl > 6f ? -2.5f : -1f;
            bool onGear = agl < 3f && System.MathF.Abs(vy) < 0.4f;
            _game.Controls.Hold(Key.Ctrl, !onGear && vy > wanted + 0.5f);
            _game.Controls.Hold(Key.Space, !onGear && vy < wanted - 0.5f);
            yield return 0;
        }
        _game.Controls.ReleaseAll();
        Hurry(1);
        GD.Print($"(screenshot: on {(_game.Zone == _game.Spaceflight.MoonSurface ? "the moon" : _game.Zone.Name)}, landed {v.Controller.Frozen})");
        _game.FixedCamera = Game.Game.Chase(v, 2.2f, 0.6f);
        for (int i = 0; i < 30; i++) yield return 0;
    }

    /// <summary>A camera a little behind and over the Skiff, looking past it at a point.</summary>
    void LookFromBehind(Vessel v, Vector3 at)
    {
        Vector3 p = v.Body.Position.ToGodot(), to = (at - p).Normalized();
        Vector3 side = to.Cross(Vector3.Up).LengthSquared() > 0.01f ? to.Cross(Vector3.Up).Normalized() : Vector3.Right;
        Vector3 up = side.Cross(to).Normalized();
        Vector3 eye = p - to * 22f + up * 6f + side * 4f;
        _game.FixedCamera = new Transform3D(Basis.LookingAt(at - eye, up), eye);
    }
}
