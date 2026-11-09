using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;
using Planets.Gen;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>
/// How far flora carries, in the game itself: the world's thickest woods seen from the highest open
/// ground near them, at eye height, then from a hover bike's height at the valley's edge, each with
/// the GPU's and CPU's frame time there. --detail=METRES sets the detail distance first.
/// Frames go beside --out.
/// </summary>
public sealed partial class Staging
{
    IEnumerable<int> FloraReachView()
    {
        var planet = _game.Zone.Planet;
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        _game.Visor.Visible = false;
        _game.Zone.Wildlife.Frozen = true;
        if (_args.TryGetValue("detail", out string detail))
        {
            _game.Settings.DetailDistance = float.Parse(detail, System.Globalization.CultureInfo.InvariantCulture);
            _game.Settings.Changed();
        }
        Vector3 start = _game.Survivor.WorldFeet;
        Vector3? found = Best(start, Woodedness);
        if (found is not Vector3 wood)
        {
            GD.Print("REACH no woods on this world");
            yield break;
        }
        float woodY = planet.GroundHeight(wood.X, wood.Z);
        // The highest open ground 400 to 1,200 m off the woods, the more of them under it the better.
        Vector3 hill = wood;
        float best = float.MinValue;
        for (int a = 0; a < 48; a++)
        for (float d = 400f; d <= 1200f; d += 80f)
        {
            double x = Mathx.WrapX(wood.X + MathF.Cos(a * Mathf.Tau / 48f) * d, planet.Width), z = Mathx.Wrap(wood.Z + MathF.Sin(a * Mathf.Tau / 48f) * d, planet.Height);
            float rise = planet.GroundHeight(x, z) - woodY;
            if (rise > best && Clear(x, z) && Standable(x, z))
            {
                best = rise;
                hill = new Vector3((float)x, 0f, (float)z);
            }
        }
        // Open ground, and none of the woods' outliers in the first 150 m toward them.
        bool Clear(double x, double z)
        {
            for (float t = 0f; t <= 150f; t += 15f)
            {
                double dx = Mathx.WrapDelta(wood.X - x, planet.Width), dz = Mathx.WrapDelta(wood.Z - z, planet.Height);
                double len = Math.Sqrt(dx * dx + dz * dz);
                if (Woodedness(Mathx.WrapX(x + dx / len * t, planet.Width), Mathx.Wrap(z + dz / len * t, planet.Height)) >= 0.5f) return false;
            }
            return true;
        }
        GD.Print($"REACH world {_game.World}; woods at {wood.X:F0}, {wood.Z:F0} ({Woodedness(wood.X, wood.Z):F1}), hill {best:F0} m above them at {hill.X:F0}, {hill.Z:F0}");
        foreach (int x in Arrive(hill)) yield return x;
        var s = _game.Zone.Streamer;
        for (int i = 0; i < 60 * 40 && !(s.FarComplete && s.FarBehind == 0); i++) yield return 0;
        _game.Zone.Sky.TimeOfDay = Num("time", 0.43);

        Vector3 feet = _game.Survivor.WorldFeet;
        Vector3 to = planet.NearestCopy(wood, feet) with { Y = woodY } - feet;
        Vector3 eye = Feet(feet) + Vector3.Up * 1.7f;
        _game.FixedCamera = new Transform3D(Basis.LookingAt((to - Vector3.Up * 1.7f).Normalized() + Vector3.Up * 0.04f, Vector3.Up), eye);
        foreach (int x in Measure("hill")) yield return x;

        // The hover bike: 600 m short of the woods, 2.6 m up, looking at them.
        Vector3 flat = to with { Y = 0f };
        Vector3 edge = feet + flat.Normalized() * MathF.Max(0f, flat.Length() - 600f);
        Vector3 bikeEye = Feet(edge) + Vector3.Up * 2.6f;
        _game.Survivor.PlaceOutside(Feet(edge).ToNumerics());
        for (int i = 0; i < 60 * 20 && !(s.Settled && s.FarBehind == 0); i++)
        {
            _game.FixedCamera = new Transform3D(Basis.LookingAt(flat.Normalized() + Vector3.Up * 0.03f, Vector3.Up), bikeEye);
            yield return 0;
        }
        foreach (int x in Measure("bike")) yield return x;
        _game.FixedCamera = null;
    }

    /// <summary>A shot, with the GPU's and CPU's time a frame there (the mean of two seconds of frames).</summary>
    IEnumerable<int> Measure(string tag)
    {
        Rid vp = _game.GetViewport().GetViewportRid();
        RenderingServer.ViewportSetMeasureRenderTime(vp, true);
        foreach (int x in _drive.Wait(30)) yield return x;
        var gpu = new List<double>();
        var cpu = new List<double>();
        var frame = new List<double>();
        ulong last = Time.GetTicksUsec();
        for (int i = 0; i < 120; i++)
        {
            yield return 0;
            ulong now = Time.GetTicksUsec();
            frame.Add((now - last) / 1000.0);
            last = now;
            gpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeGpu(vp));
            cpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeCpu(vp));
        }
        var st = _game.Zone.Streamer;
        GD.Print($"REACH {tag}: GPU {gpu.Average():F2} ms, render CPU {cpu.Average():F2} ms, frame {frame.Average():F2} ms (99th {frame.OrderBy(f => f).ElementAt(118):F1}); detail {PlanetSurface.Terrain.DetailReach.Distance:F0} m, far rings {PlanetSurface.Terrain.TerrainStreamer.FarRadius}, far complete {st.FarComplete}");
        Snap(tag);
    }
}
