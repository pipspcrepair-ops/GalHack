using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using PlanetSurface.Skies;
using Slice.Orbit;
using Slice.Vessels;
using N = System.Numerics;

namespace Slice.Tests;

/// <summary>
/// To the moon: high orbit with the moon ahead, the hand-over from its far look to its own globe
/// (a frame on each side, on one line from it, measured), its ground close up, and the sleeper
/// standing on it by the landed Skiff (the last frame). And the moon's light from the ground: a
/// crescent, a full moon and an eclipsed moon at moments found on the system's own clock.
/// </summary>
public sealed partial class Staging
{
    IEnumerable<int> MoonView()
    {
        AirCells.Give(_game);
        foreach (int x in ToOrbit()) yield return x;
        Vessel v = Skiff;
        OrbitZone o = _game.Orbit;
        Hurry(4);
        _game.Controls.Hold(Key.Space);
        for (int i = 0; i < 60 * 60 && _game.Spaceflight.Band < 1f; i++) yield return 0;
        _game.Controls.ReleaseAll();
        foreach (int x in _pilot.Stop()) yield return x;
        for (int i = 0; i < 60 * 300 && (o.Moon?.Ready != true || _game.Spaceflight.MoonSurface?.Made == null); i++) yield return 0;
        Celestial moon = o.Moon;
        if (moon == null || !_game.InOrbit) yield break;
        foreach (int x in MoonTest.TowardTheMoon(_pilot, o, v)) yield return x; // over the limb, the moon ahead
        foreach (int x in _pilot.Stop()) yield return x;
        Where("high orbit");
        Hurry(1);
        _game.Visor.Visible = true;
        LookFromBehind(v, moon.Centre);
        for (int i = 0; i < 12; i++) yield return 0;
        Snap("orbit");

        // The hand-over, from one line out of the moon: its far look alone, then its own globe alone.
        _game.Visor.Visible = false;
        Vector3 outward = (v.Body.Position.ToGodot() - moon.Centre).Normalized();
        var seen = new List<(float Lit, float Bright)>();
        foreach (var (tag, ground) in new[] { ("approach-far", 9400f), ("approach-globe", 6700f) })
        {
            Vector3 eye = moon.Centre + outward * (moon.GlobeRadius + ground);
            _game.FixedCamera = new Transform3D(Basis.LookingAt(moon.Centre - eye, MathF.Abs(outward.Y) > 0.9f ? Vector3.Forward : Vector3.Up), eye);
            for (int i = 0; i < 12; i++) yield return 0;
            Snap(tag);
            var disc = Disc(moon.Centre - eye, MathF.Asin(moon.GlobeRadius / eye.DistanceTo(moon.Centre)));
            seen.Add((disc.Lit, disc.Bright));
            GD.Print($"(moon light {tag}: {ground:F0} m off its ground, its far look at {o.Space.FadeOf(moon.Body):P0}, its globe {(moon.Globe.Visible ? "drawn" : "hidden")}: lit {disc.Lit:P1} of the disc, the lit part's brightness {disc.Bright:F3})");
        }
        GD.Print($"(moon light at the hand-over: lit share {seen[0].Lit:P1} far, {seen[1].Lit:P1} up close; brightness {seen[1].Bright / Math.Max(seen[0].Bright, 1e-4f):P0} of the far look's)");
        _game.FixedCamera = null;

        Hurry(4);
        var (tx, tz) = moon.Map.MapPoint(moon.ToGlobe(v.Body.Position));
        Vector3 Over() => moon.ToWorld(moon.Map.Direction(tx, tz) * moon.RadiusFor(moon.Ceiling + o.Rules.Crossing.Band + 400f)).ToGodot();
        foreach (int x in _pilot.FlyTo(v, Over, 30f, 400f)) yield return x;
        Where("over the moon");
        Hurry(1);
        _game.Visor.Visible = true;
        LookFromBehind(v, moon.Centre);
        for (int i = 0; i < 12; i++) yield return 0;
        Snap("over");
        _game.FixedCamera = null;
        Hurry(3);
        foreach (int x in MoonTest.Upright(_pilot, moon, v)) yield return x;
        // Down at a pilot's pace, a sink rate it can stop from.
        for (int i = 0; i < 60 * 90 && _game.InOrbit; i++)
        {
            _game.Controls.Hold(Key.Ctrl, -v.Body.Velocity.ToGodot().Dot((v.Body.Position.ToGodot() - moon.Centre).Normalized()) < 40f);
            yield return 0;
        }
        _game.Controls.ReleaseAll();
        foreach (int x in _pilot.Land(v)) yield return x;
        Hurry(1);
        Where("landed");
        // Out of the seat and onto the moon's ground, the world it came from in its sky.
        foreach (int x in _drive.Tap(Key.F)) yield return x;
        Vector3 there = v.Body.Position.ToGodot() + MathX.Rotate(v.Body.Rotation, N.Vector3.UnitX).ToGodot() * 7f;
        there.Y = _game.Zone.GroundAt(there.X, there.Z).Y;
        foreach (int x in _drive.WalkTo(there, 0.8f, 40f, sprint: false, dig: false)) yield return x;
        Where($"standing on the moon, {_game.Survivor.WorldFeet.DistanceTo(there):F1} m from the spot");
        _game.Visor.Visible = true;
        _game.ThirdPerson = true;
        foreach (int x in _drive.LookAt(v.Body.Position.ToGodot() + Vector3.Up * 6f, 30)) yield return x;
    }

    /// <summary>
    /// The moon's light from the ground at three moments on the system's clock, each looked at straight on:
    /// a crescent, a full moon and the moon in a shadow. Each frame's lit share is measured off the picture
    /// against its true phase.
    /// </summary>
    IEnumerable<int> MoonLightView()
    {
        var zone = _game.Zone;
        zone.Weather.Sim.Snap(Weather.Model.WeatherKind.Clear);
        _game.Visor.Visible = false;
        SkyRig sky = zone.Sky;
        StarSystem m = sky.Model;
        Orbiter moon = m.FuelMoon;
        var mat = (ShaderMaterial)sky.Environment.Sky.SkyMaterial;
        sky.Paused = true;
        double now = sky.Days;
        // Where the moon stands at a moment, its true lit share, and the share of the sun hidden from it.
        (Vector3 Dir, float Angular, float Phase, double Dark) At(double t)
        {
            sky.Days = t;
            sky.Track();
            var d = sky.Drawn.First(x => x.Body == moon);
            Vector3[] dirs = mat.GetShaderParameter("moon_dir").AsVector3Array(), lights = mat.GetShaderParameter("moon_light").AsVector3Array();
            int i = Array.FindIndex(dirs, x => x.AngleTo(d.Direction) < 1e-4f);
            return (d.Direction, d.Angular, (1f + lights[i].Dot(-d.Direction)) * 0.5f, m.EclipseAt(t, m.Position(moon, t), moon).Covered);
        }
        double Find(Func<(Vector3 Dir, float Angular, float Phase, double Dark), bool> fits)
        {
            for (int pass = 0; pass < 2; pass++)
                for (double t = now; t < now + 40.0; t += 0.004)
                {
                    var a = At(t);
                    if (a.Dir.Y > 0.3f && (pass == 1 || sky.ToSun.Y < -0.08f) && fits(a)) return t;
                }
            return -1;
        }
        var moments = new (string Tag, double When)[]
        {
            ("crescent", Find(a => a.Phase is > 0.12f and < 0.3f && a.Dark < 0.01)),
            ("full", Find(a => a.Phase > 0.96f && a.Dark < 0.01)),
            ("eclipse", Find(a => a.Dark > 0.98)),
        };
        float fullMean = 0f;
        foreach (var (tag, when) in moments)
        {
            if (when < 0)
            {
                GD.Print($"(moon light: no {tag} moon over the sky in 40 days)");
                continue;
            }
            var a = At(when);
            Vector3 eye = _game.Survivor.WorldEye();
            _game.FixedCamera = new Transform3D(Basis.LookingAt(a.Dir, MathF.Abs(a.Dir.Y) > 0.95f ? Vector3.Forward : Vector3.Up), eye);
            for (int i = 0; i < 12; i++)
            {
                sky.Days = when;
                sky.Track();
                yield return 0;
            }
            Snap(tag);
            var disc = Disc(a.Dir, a.Angular);
            if (tag == "full") fullMean = disc.Mean;
            GD.Print($"(moon light {tag}: day {when - now:F2} on, {Mathf.RadToDeg(MathF.Asin(a.Dir.Y)):F0} degrees up, sun {Mathf.RadToDeg(MathF.Asin(sky.ToSun.Y)):F0} degrees; true lit share {a.Phase:P1}, measured {disc.Lit:P1}; the sun hidden from it {a.Dark:P0}; mean brightness {disc.Mean:F3}{(tag == "eclipse" && fullMean > 0f ? $", {disc.Mean / fullMean:P0} of the full moon's" : "")})");
        }
        _game.FixedCamera = null;
        sky.Days = now;
        sky.Paused = false;
    }

    /// <summary>
    /// A body's disc in the last frame drawn, looked at along <paramref name="toward"/>: its lit share (the pixels well
    /// over the sky behind it), its lit part's brightness and its mean brightness.
    /// </summary>
    (float Lit, float Bright, float Mean) Disc(Vector3 toward, float angular)
    {
        Image image = _game.GetViewport().GetTexture().GetImage();
        Camera3D camera = _game.GetViewport().GetCamera3D();
        Vector2 c = camera.UnprojectPosition(camera.GlobalPosition + toward.Normalized() * 50f) * image.GetHeight() / _game.GetViewport().GetVisibleRect().Size.Y;
        float r = MathF.Tan(angular) / MathF.Tan(Mathf.DegToRad(camera.Fov) * 0.5f) * image.GetHeight() * 0.5f;
        var inside = new List<float>();
        var behind = new List<float>();
        int step = Math.Max(1, (int)(r / 60f));
        for (int y = (int)(c.Y - r * 1.8f); y <= c.Y + r * 1.8f; y += step)
            for (int x = (int)(c.X - r * 1.8f); x <= c.X + r * 1.8f; x += step)
            {
                if (x < 0 || y < 0 || x >= image.GetWidth() || y >= image.GetHeight()) continue;
                float d = new Vector2(x - c.X, y - c.Y).Length() / r;
                Color p = image.GetPixel(x, y);
                float l = 0.2126f * p.R + 0.7152f * p.G + 0.0722f * p.B;
                if (d < 0.9f) inside.Add(l);
                else if (d > 1.4f) behind.Add(l);
            }
        if (inside.Count == 0 || behind.Count == 0) return (0f, 0f, 0f);
        behind.Sort();
        var sorted = inside.OrderBy(l => l).ToList();
        float back = behind[behind.Count / 2], top = sorted[(int)(sorted.Count * 0.97f)], threshold = back + 0.25f * (top - back);
        var lit = inside.Where(l => l > threshold).ToList();
        return ((float)lit.Count / inside.Count, lit.Count > 0 ? lit.Average() : 0f, inside.Average());
    }

    void Where(string what) => GD.Print($"(screenshot: {what}: in {_game.Zone.Name}, frame {_game.Orbit?.Anchor?.Name}, band {_game.Spaceflight.Band:F2}, landed {Skiff.Controller?.Frozen})");

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
