using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;
using Planets.Gen;
using Slice.Game;

namespace Slice.Tests;

/// <summary>
/// The barren proof (owner, 22 Nov: "Wouldn't the airless vacuum moons be grey cratered moonscapes?"), in the game itself,
/// on whatever barren ground the sleeper stands on: the field under a low sun, the biggest body in its black sky over the
/// land, a big fresh crater from its rim, a mare's edge from the highland over it, and the highland. "barren" takes them on
/// a barren start world (--type barren --variant 0, 1 or 2); "moon" with --barren=1 takes them on the thruster moon after
/// landing. --out=PATH names the files (PATH-field.png and so on); --star=yellow rolls a world under a sunlike star.
/// Each shot is taken at its own place's low sun: a world's sun stands where it does over each place on it.
/// </summary>
public sealed partial class Staging
{
    IEnumerable<int> BarrenView()
    {
        foreach (int x in BarrenShots()) yield return x;
    }

    IEnumerable<int> BarrenShots()
    {
        var planet = _game.Zone.Planet;
        _game.Visor.Visible = false;
        _game.ThirdPerson = false;
        _game.Zone.Wildlife.Frozen = true;
        _game.Zone.Sky.Paused = true;
        GD.Print($"BARREN world {_game.World}: {planet.Spec}, star {planet.Sky.Star}, air density {planet.Sky.Density:F2}");
        Vector3 start = _game.Survivor.WorldFeet;
        Biome At(Vector3 p) => planet.Palette.BiomeAt(Mathx.WrapX(p.X, planet.Width), Mathx.Wrap(p.Z, planet.Height));
        Vector3 Ring(Vector3 c, float r, int k, int n) => c + new Vector3(Mathf.Cos(k * Mathf.Tau / n), 0f, Mathf.Sin(k * Mathf.Tau / n)) * r;

        // The field: off the landing's made ground, looking across the sun's way so every rim throws its shadow sideways.
        Vector3 field = Ring(start, 220f, 1, 8);
        foreach (int x in BarrenShot("field", field, 1.7f, -6f, Across)) yield return x;

        // Up: the biggest body in the sky over the land, low enough that the ground is in the frame.
        foreach (int x in BarrenShot("up", field, 1.7f, float.NaN, s => s.Drawn.Count == 0 ? Across(s) : s.Drawn.MaxBy(d => d.Angular).Direction, body: true)) yield return x;

        // A big fresh crater, nearest first, from its rim on the sun's side: the far wall lit, the near one in its own shadow.
        var crater = planet.Map.Model.CratersOver(120f).Where(c => c.Fresh > 0.85f && c.Radius < 900f && At(new Vector3((float)c.X, 0f, (float)c.Y)) != Biome.Mare)
            .Select(c => (Centre: planet.NearestCopy(new Vector3((float)c.X, 0f, (float)c.Y), start), c.Radius))
            .OrderBy(c => c.Centre.DistanceTo(start with { Y = 0f })).FirstOrDefault();
        if (crater.Radius > 0f)
        {
            float off = crater.Centre.DistanceTo(start with { Y = 0f });
            GD.Print($"BARREN rim: a crater {crater.Radius * 2f:F0} m across, {off:F0} m from the start");
            // Its sun first (from its middle), to know which rim is the sun's side.
            foreach (int x in BarrenShot(null, crater.Centre, 2f, 0f, Across)) yield return x;
            Vector3 rim = crater.Centre + Sunward() * crater.Radius * 1.06f;
            foreach (int x in BarrenShot("rim", rim, 4f, -16f, s => crater.Centre - rim)) yield return x;
        }

        // A mare's edge: the nearest highland ground with mare beside it, looking out over the mare from a little above.
        Vector3? edge = null, onto = null;
        for (float r = 60f; r < 4000f && edge == null; r += 60f)
            for (int a = 0; a < 48 && edge == null; a++)
            {
                Vector3 p = Ring(start, r, a, 48);
                if (At(p) != Biome.Mare) continue;
                foreach (Vector3 d in new[] { Vector3.Right, Vector3.Left, Vector3.Forward, Vector3.Back })
                    if (At(p + d * 120f) == Biome.Highland) { edge = p + d * 120f; onto = -d; break; }
            }
        if (edge is Vector3 e && onto is Vector3 o)
            foreach (int x in BarrenShot("mare", e, 8f, -8f, s => o)) yield return x;
        else GD.Print("BARREN mare: no mare edge within 4 km (an icy or metal body has none)");

        // The highland: ground with only highland round it for 200 m.
        Vector3? high = null;
        for (float r = 0f; r < 4000f && high == null; r += 80f)
            for (int a = 0; a < 24 && high == null; a++)
            {
                Vector3 p = Ring(start, r, a, 24);
                if (Enumerable.Range(0, 8).All(k => At(Ring(p, 200f, k, 8)) == Biome.Highland) && At(p) == Biome.Highland) high = p;
            }
        if (high is Vector3 h)
            foreach (int x in BarrenShot("highland", h, 1.7f, -6f, Across)) yield return x;
        else GD.Print("BARREN highland: none within 4 km (an icy or metal body has none)");
    }

    /// <summary>Toward the sun over the ground, flat: the sun light's own way (its lit side is the world's).</summary>
    Vector3 Sunward()
    {
        Vector3 to = _game.Zone.Sky.Sun.GlobalTransform.Basis.Z;
        return new Vector3(to.X, 0f, to.Z).Normalized();
    }

    /// <summary>Across the sun's way, flat.</summary>
    Vector3 Across(PlanetSurface.Skies.SkyRig sky)
    {
        Vector3 s = Sunward();
        return new Vector3(-s.Z, 0f, s.X);
    }

    /// <summary>
    /// The sleeper set down at a spot and the ground round it streamed in; the sky turned to that place's low sun (about 20° up,
    /// as relief reads best; with <paramref name="body"/>, the biggest body in the sky about 25° up too); and a shot from an
    /// eye this high over it, the way <paramref name="way"/> gives, pitched (degrees; NaN: as the way itself is). No shot with no tag.
    /// </summary>
    IEnumerable<int> BarrenShot(string tag, Vector3 at, float high, float pitch, Func<PlanetSurface.Skies.SkyRig, Vector3> way, bool body = false)
    {
        _game.FixedCamera = null;
        _game.Survivor.PlaceOutside(Feet(at).ToNumerics());
        foreach (int x in _drive.UntilSettled()) yield return x;
        foreach (int x in _drive.Wait(tag == null ? 30 : 300)) yield return x;
        var sky = _game.Zone.Sky;
        double best = sky.Days, score = double.MinValue, from = Math.Floor(sky.Days);
        for (double t = from; t < from + (body ? 6.0 : 1.0); t += 0.002)
        {
            sky.Days = t;
            sky.Track();
            float sun = Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(sky.Sun.GlobalTransform.Basis.Z.Y, -1f, 1f)));
            if (sun < 12f || sun > 30f) continue;
            // With a body: it about 25° up, where the land still fits under it in the frame.
            double s = (body && sky.Drawn.Count > 0 ? -Math.Abs(Mathf.RadToDeg(Mathf.Asin(sky.Drawn.MaxBy(d => d.Angular).Direction.Y)) - 25f) * 0.05 : 0.0) - Math.Abs(sun - 20f) * 0.01;
            if (s > score) (score, best) = (s, t);
        }
        sky.Days = best;
        sky.Track();
        if (tag == null) yield break;
        foreach (int x in _drive.Wait(10)) yield return x;
        Vector3 look = way(sky).Normalized(), flat = new Vector3(look.X, 0f, look.Z).Normalized();
        if (float.IsNaN(pitch)) pitch = Mathf.RadToDeg(Mathf.Asin(look.Y)) - 12f; // the body over the middle, the land along the bottom
        look = flat * Mathf.Cos(Mathf.DegToRad(pitch)) + Vector3.Up * Mathf.Sin(Mathf.DegToRad(pitch));
        Vector3 front = at + flat * 1.5f; // the eye out in front of the sleeper's own head
        Vector3 eye = _game.Zone.GroundAt(front.X, front.Z) + Vector3.Up * high;
        _game.FixedCamera = new Transform3D(Basis.LookingAt(look, Vector3.Up), eye);
        foreach (int x in _drive.Wait(20)) yield return x;
        var planet = _game.Zone.Planet;
        GD.Print($"BARREN {tag}: biome {planet.Palette.BiomeAt(Mathx.WrapX(at.X, planet.Width), Mathx.Wrap(at.Z, planet.Height))}, sun {Mathf.RadToDeg(Mathf.Asin(sky.Sun.GlobalTransform.Basis.Z.Y)):F0}° up"
            + (body && sky.Drawn.Count > 0 ? $", {sky.Drawn.MaxBy(d => d.Angular).Body.Name} {Mathf.RadToDeg(Mathf.Asin(sky.Drawn.MaxBy(d => d.Angular).Direction.Y)):F0}° up" : ""));
        Snap(tag);
        _game.FixedCamera = null;
    }
}
