using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;
using Planets.Gen;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>
/// Shadows at a low sun: from the highest open ground near the start, across the land at right angles
/// to the sun (so ridges' shadows lie across valleys), with the GPU's and CPU's frame time.
/// --elev=DEGREES the sun's height (8), --shadow=METRES the shadow distance. Frames go beside --out.
/// </summary>
public sealed partial class Staging
{
    IEnumerable<int> ShadowView()
    {
        var planet = _game.Zone.Planet;
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        _game.Visor.Visible = false;
        _game.Zone.Wildlife.Frozen = true;
        if (_args.TryGetValue("shadow", out string reach))
        {
            _game.Settings.ShadowDistance = float.Parse(reach, System.Globalization.CultureInfo.InvariantCulture);
            _game.Settings.Changed();
        }
        Vector3 start = _game.Survivor.WorldFeet;
        // Open high ground within 5 km over the deepest land round it: a ridge over valleys.
        Vector3 hill = start;
        float top = float.MinValue, score = float.MinValue;
        float H(double x, double z) => planet.GroundHeight(Mathx.WrapX(x, planet.Width), Mathx.Wrap(z, planet.Height));
        for (int a = 0; a < 36; a++)
        for (float d = 0f; d <= 5000f; d += 250f)
        {
            double x = Mathx.WrapX(start.X + MathF.Cos(a * Mathf.Tau / 36f) * d, planet.Width), z = Mathx.Wrap(start.Z + MathF.Sin(a * Mathf.Tau / 36f) * d, planet.Height);
            float h = H(x, z);
            if (h < 5f || Woodedness(x, z) >= 1f || !Standable(x, z)) continue;
            // (The land round it, not the sea: a valley, not a coast.)
            float low = float.MaxValue;
            int land = 0;
            for (int k = 0; k < 12; k++)
            {
                float g = H(x + MathF.Cos(k * Mathf.Tau / 12f) * 1500f, z + MathF.Sin(k * Mathf.Tau / 12f) * 1500f);
                if (g < 3f) continue;
                land++;
                low = MathF.Min(low, g);
            }
            if (land < 10) continue;
            if (h - low > score)
            {
                score = h - low;
                top = h;
                hill = new Vector3((float)x, 0f, (float)z);
            }
        }
        foreach (int x in Arrive(hill)) yield return x;
        // The morning hour the sun stands at the height asked.
        float want = (float)Num("elev", 8) * Mathf.Pi / 180f;
        var sky = _game.Zone.Sky;
        double best = 0.3, err = double.MaxValue;
        for (double t = 0.0; t < 1.0; t += 0.005)
        {
            sky.TimeOfDay = t;
            yield return 0;
            yield return 0;
            float e = MathF.Asin(Math.Clamp(sky.Sun.GlobalBasis.Z.Normalized().Y, -1f, 1f));
            if (MathF.Abs(e - want) < err) { err = MathF.Abs(e - want); best = t; }
        }
        sky.TimeOfDay = best;
        var s = _game.Zone.Streamer;
        for (int i = 0; i < 60 * 40 && !(s.FarComplete && s.FarBehind == 0); i++) yield return 0;
        sky.TimeOfDay = best;
        Vector3 sun = sky.Sun.GlobalBasis.Z.Normalized();
        Vector3 across = new Vector3(-sun.Z, 0f, sun.X).Normalized();
        Vector3 feet = _game.Survivor.WorldFeet;
        // The side with the lower land: the valleys.
        float Low(Vector3 way) => Enumerable.Range(1, 6).Sum(k => { float g = planet.GroundHeight(Mathx.WrapX(feet.X + way.X * k * 400f, planet.Width), Mathx.Wrap(feet.Z + way.Z * k * 400f, planet.Height)); return g < 3f ? 1000f : g; });
        if (Low(-across) < Low(across)) across = -across;
        Vector3 eye = Feet(feet) + Vector3.Up * (float)Num("up", 40); // over the hilltop's own crest
        GD.Print($"SHADOW world {_game.World}; hill {top:F0} m ({score:F0} m over the land round it) at {hill.X:F0}, {hill.Z:F0}; sun {Num("elev", 8):F0} degrees up at time {best:F3}; looking across it");
        for (int i = 0; i < 180; i++)
        {
            sky.TimeOfDay = best;
            _game.FixedCamera = new Transform3D(Basis.LookingAt(across + Vector3.Down * 0.12f, Vector3.Up), eye);
            yield return 0;
        }
        GD.Print($"SHADOW the sun stands {MathF.Asin(Math.Clamp(sky.Sun.GlobalBasis.Z.Normalized().Y, -1f, 1f)) * 180f / MathF.PI:F1} degrees up, its light {sky.Sun.LightEnergy:F2}");
        foreach (int x in Measure("valley")) yield return x;
        _game.FixedCamera = null;
    }
}
