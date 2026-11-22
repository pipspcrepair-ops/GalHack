using System;
using System.Collections.Generic;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using Slice.Orbit;
using N = System.Numerics;

namespace Slice.Tests;

/// <summary>
/// The start world from fixed spots over one place, at noon there: down from just under the cap on
/// the surface, then up through the cap on foot (the sleeper set under it and lifted: no craft to
/// fly, so every world gets there) and held over the band, and the globe from just over the switch,
/// 3 km and 10 km, aimed at it with its limb at the top of the frame.
/// </summary>
public sealed partial class Staging
{
    IEnumerable<int> SizesView()
    {
        AirCells.Give(_game);
        _game.Visor.Visible = false;
        _game.Zone.Sky.TimeOfDay = 0.5;
        float cap = _game.Surface.Physics.Zone.Ceiling;
        Vector3 feet = _game.Survivor.WorldFeet;
        _game.Survivor.PlaceOutside(new N.Vector3(feet.X, cap - 30f, feet.Z));
        IEnumerable<int> Hold(int frames, Func<Transform3D> camera, float rise = 0f)
        {
            for (int i = 0; i < frames; i++)
            {
                _game.Survivor.Body.Velocity = new N.Vector3(0f, rise, 0f);
                _game.FixedCamera = camera();
                yield return 0;
            }
        }
        // The surface, down from just under the cap, the land streamed in round the spot first.
        var under = new Transform3D(Basis.LookingAt(Vector3.Down, Vector3.Forward), new Vector3(feet.X, cap - 10f, feet.Z));
        foreach (int x in Hold(300, () => under)) yield return x;
        Snap("surface-down");

        // Up through the cap and on over the band, where the zone stops being laid out over the surface.
        float top = cap + _game.Orbit.Rules.Crossing.Band + 300f;
        for (int i = 0; i < 60 * 120 && !_game.Spaceflight.PastBand; i++)
        {
            float y = _game.InOrbit ? _game.Orbit.Home.Height(_game.Survivor.WorldFeet) : _game.Survivor.WorldFeet.Y;
            _game.Survivor.Body.Velocity = new N.Vector3(0f, y < top ? 40f : 0f, 0f);
            _game.FixedCamera = null;
            yield return 0;
        }
        if (!_game.Spaceflight.PastBand)
        {
            GD.Print("(sizes: never got past the band)");
            yield break;
        }
        Celestial home = _game.Orbit.Home;
        Vector3 up = (_game.Survivor.WorldFeet - home.Centre).Normalized();
        Vector3 ahead = (up.Cross(Vector3.Up).LengthSquared() > 1e-4f ? up.Cross(Vector3.Up) : up.Cross(Vector3.Right)).Normalized();
        foreach (var (tag, h) in new[] { ("switch", cap + 5f), ("3km", 3000f), ("10km", 10000f) })
        {
            Vector3 eye = home.Centre + up * (home.GlobeRadius + h);
            // The limb 25 degrees over the middle of the frame, or straight down once the whole disc fits.
            float limb = MathF.Asin(home.GlobeRadius / (home.GlobeRadius + h)), tilt = MathF.Max(limb - Mathf.DegToRad(25f), 0f);
            Vector3 to = -up * MathF.Cos(tilt) + ahead * MathF.Sin(tilt);
            var view = new Transform3D(Basis.LookingAt(to, tilt > 0.3f ? up : ahead), eye);
            foreach (int x in Hold(120, () => view)) yield return x; // long enough for the near ground's paint to come in
            Snap(tag);
            GD.Print($"(sizes {tag}: {home.Height(eye):F0} m over the world, its radius {home.GlobeRadius:F0} m, the sun {home.Globe.LitFrom.Dot(up):F2} up)");
        }
        _game.FixedCamera = null;
    }
}
