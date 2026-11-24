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
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>
/// Glass over the sky: from the Skiff's seat, a body (the world rolled for a ringed giant: --ringed=1) seen through its windscreen.
/// "glass": in orbit, a ringed giant centred, then its limb (air glow) in the pane, then a planet. "glass-ground": from the ground,
/// the Skiff set down nose to a body standing in the sky. The seat's own camera throughout; the eye is put out by the system's sky
/// (as "bodies" does), the Skiff turned to face the body by its pilot.
/// </summary>
public sealed partial class Staging
{
    IEnumerable<int> GlassOrbit()
    {
        foreach (int x in ToOrbit()) yield return x;
        foreach (int x in UntilSurfaces()) yield return x;
        Vessel v = Skiff;
        OrbitZone o = _game.Orbit;
        Vector3 world = o.ToWorld(N.Vector3.Zero).ToGodot(), out0 = (v.Body.Position.ToGodot() - world).Normalized();
        Hurry(3);
        foreach (int x in _pilot.FlyTo(v, () => world + out0 * (o.ShellRadius + 2500f), 60f, 120f)) yield return x;
        Hurry(1);
        _game.Controls.ReleaseAll();
        _game.Visor.Visible = false;
        var sky = _game.Surface.Sky;
        sky.Paused = true;
        o.Globe.Hidden = true;
        StarSystem model = _game.Surface.Planet.StarSystem;
        double days = sky.Days;
        StarSystem.Orientation turned = model.Orient(days);
        Basis turn = o.Turn.ToBasis();
        Orbiter giant = model.Bodies.FirstOrDefault(b => b.Rings && b != model.Home) ?? model.Bodies.FirstOrDefault(b => b.Kind == OrbiterKind.GasGiant && b != model.Home);
        Orbiter planet = model.Bodies.Where(b => b.Kind == OrbiterKind.Planet && b.Parent == model.Star && b != model.Home).OrderBy(b => (model.Position(b, days) - model.Position(model.Home, days)).Length).FirstOrDefault();
        GD.Print($"(glass: giant {giant?.Name} ringed {giant?.Rings}, planet {planet?.Name})");
        foreach (var (tag, body, r, limb) in new[] { ("ring", giant, 4.0, false), ("air", planet, 2.2, true), ("planet", planet, 6.0, false) })
        {
            if (body == null) continue;
            D3 toStar = (model.Position(model.Star, days) - model.Position(body, days)).Normalized;
            D3 side = toStar.Cross(new D3(0, 1, 0)).Normalized;
            D3 from = model.Position(body, days) - model.Position(model.Home, days) + (toStar * 0.5 + side * 0.8 + new D3(0, 0.25, 0)).Normalized * (body.Radius * r);
            _game.Spaceflight.ViewFrom = turned.ToGlobe(from);
            Sighting s = model.See(body, days, turned.ToGlobe(from), turned);
            Vector3 look = (turn * new Vector3((float)s.Direction.X, (float)s.Direction.Y, (float)s.Direction.Z)).Normalized();
            Vector3 up = Mathf.Abs(look.Y) > 0.9f ? Vector3.Forward : Vector3.Up;
            if (limb) look = look.Rotated(look.Cross(up).Normalized(), -s.Angular * 0.9f); // its upper limb at the middle, the air glow round it
            foreach (int x in _pilot.TurnTo(v, look, up)) yield return x;
            _game.Survivor.Pitch = (float)Num("pitch", 0);
            for (int i = 0; i < 20; i++) yield return 0;
            Snap(tag);
            GD.Print($"(glass {tag}: {body.Name} {Mathf.RadToDeg(s.Angular):F1} degrees radius, air {o.Space.AirOf(body):P0}, the Skiff's nose {v.Body.Rotation.ToGodot() * Vector3.Forward} wanted {look})");
        }
        _game.Spaceflight.ViewFrom = null;
        o.Globe.Hidden = false;
    }

    IEnumerable<int> GlassGround()
    {
        var sky = _game.Zone.Sky;
        sky.Paused = true;
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        // The time of day with the biggest body low in the sky, where the windscreen is (9 to 20 degrees up, where the windscreen takes it).
        Vector3 dir = default;
        float size = 0f;
        double when = 0.0;
        string name = "";
        for (double t = 0.0; t < 1.0; t += 0.01)
        {
            sky.TimeOfDay = t;
            sky.Track();
            foreach (var (b, d, a) in sky.Drawn)
                if (d.Y is > 0.15f and < 0.35f && a > size) { size = a; dir = d; name = b.Name; when = t; }
        }
        sky.TimeOfDay = when;
        sky.Track();
        GD.Print($"(glass ground: {name} {Mathf.RadToDeg(size):F1} degrees radius, up {dir})");
        for (int i = 0; i < 20; i++) yield return 0;
        Vector3 site = Play.FlatSpot(_game.Start, 40f, 0.6f, 0.4f, span: 14f);
        Playthrough.Clearing(_game.Zone, site, 30f);
        _game.Survivor.PlaceOutside((site + new Vector3(0f, 1f, 7f)).ToNumerics());
        foreach (int x in _drive.Wait(60)) yield return x;
        Vessel v = Vessel.Spawn(_game.Zone, CoreCraft.Skiff(_game.Zone.Data), (site + Vector3.Up * 3f).ToNumerics(), MathF.Atan2(-dir.X, -dir.Z), landed: true);
        v.Settle();
        foreach (int x in _drive.Wait(10)) yield return x;
        foreach (int x in _drive.Board(v)) yield return x;
        _game.Visor.Visible = false;
        _game.Survivor.Pitch = MathF.Asin(dir.Y) + (float)Num("tilt", 0.22);
        for (int i = 0; i < 20; i++) yield return 0;
        Snap("ground");
        GD.Print($"(glass ground: the Skiff's nose {v.Body.Rotation.ToGodot() * Vector3.Forward}, the body {dir})");
    }
}
