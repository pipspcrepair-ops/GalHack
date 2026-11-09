using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;
using Planets.Gen;
using PlanetSurface.Infra;
using PlanetSurface.Poi;
using PlanetSurface.Terrain;
using Slice.Game;
using Slice.Ui;

namespace Slice.Tests;

/// <summary>
/// The detail distance and flora's reach: the options slider runs from the default to the horizon and
/// the setting keeps; at the top the far rings, flora, places' merged blocks and the roadside tiles
/// all reach the horizon; flora's drawings together thin out in bands, never a step, at either end;
/// on this world's thickest woods trees are seen past a kilometre; and the bike route rides again
/// at the top setting without a pop.
/// </summary>
public partial class PopTest
{
    IEnumerable<int> ReachChecks()
    {
        GD.Print("-- detail distance and flora's reach --");
        float top = OptionsSection.DetailAt(1f), bottom = OptionsSection.DetailAt(0f);
        Check(bottom == DetailReach.Default && top >= TerrainStreamer.HorizonDistance,
            $"the detail slider runs from {bottom:F0} m (the default) to {top:F0} m (the horizon is {TerrainStreamer.HorizonDistance:F0} m)");

        const string path = "user://reach-test-settings.cfg";
        new GameSettings { DetailDistance = top }.Save(path);
        float back = GameSettings.Load(path).DetailDistance;
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));
        Check(back == top, $"the detail distance keeps in the settings file ({back:F0} m read back)");

        Bands("default");
        _game.Settings.DetailDistance = top;
        _game.Settings.Changed();
        yield return 0; // the game applies it
        yield return 0;
        float rings = TerrainStreamer.FarRadius * FarBuilder.Size;
        float flora = TerrainStreamer.FloraLayerReach(TerrainStreamer.DistantLayer).To;
        float places = DetailReach.Toward(_game.Zone.Pois.MidRadius), cities = DetailReach.Toward(_game.Zone.Pois.CityMidRadius);
        Check(DetailReach.Distance == top && rings >= top && flora >= top && InfraLayer.Radius >= top && places >= top && cities >= top,
            $"at the top the far rings reach {rings:F0} m, trees {flora:F0} m, places' and cities' merged blocks {places:F0} and {cities:F0} m, roadside tiles {InfraLayer.Radius:F0} m (detail {DetailReach.Distance:F0} m)");
        Bands("top");
        // The far rings come in at the top setting, live.
        var s = _game.Zone.Streamer;
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed.TotalSeconds < 30 && !s.FarComplete) yield return 0;
        GD.Print($"  (at the top: far rings complete {s.FarComplete} after {clock.Elapsed.TotalSeconds:F0} s)");
        foreach (int x in Route("bike at the top", null, 38f, 3.0f, 900f, 0f)) yield return x;

        _game.Settings.DetailDistance = DetailReach.Default;
        _game.Settings.Changed();
        yield return 0;
        yield return 0;
        Check(DetailReach.Distance == DetailReach.Default && TerrainStreamer.FarRadius * FarBuilder.Size >= DetailReach.Default,
            $"back to the default, live: the far rings reach {TerrainStreamer.FarRadius * FarBuilder.Size} m");
        foreach (int x in TreesFar()) yield return x;
    }

    /// <summary>All of flora's drawings together, at every distance, from many plants' own reaches: they fade in bands, never a step.</summary>
    void Bands(string setting)
    {
        // The mean over many plants of each (their own reaches differ), four in ten of them of the thinning share.
        var rng = new Random();
        var spots = Enumerable.Range(0, 300).Select(_ => (X: rng.NextSingle() * 10000f, Z: rng.NextSingle() * 10000f)).ToList();
        float Mean(Func<bool, float, float, float> of) => spots.Average(p => 0.6f * of(false, p.X, p.Z) + 0.4f * of(true, p.X, p.Z));
        float worstLarge = 0f, worstSmall = 0f, atLarge = 0f, atSmall = 0f, largeTo = 0f, smallTo = 0f;
        float last = 1f, lastSmall = 1f;
        for (float d = 5f; d <= DetailReach.Distance + 10f; d += 5f)
        {
            float c = Mean((thin, x, z) => TerrainStreamer.LargeCover(thin, d, x, z));
            float sm = Mean((thin, x, z) => TerrainStreamer.FloraShows(TerrainStreamer.SmallLayer, thin, d, x, z));
            if (last - c > worstLarge) { worstLarge = last - c; atLarge = d; }
            if (lastSmall - sm > worstSmall) { worstSmall = lastSmall - sm; atSmall = d; }
            if (c > 0.02f) largeTo = d;
            if (sm > 0.02f) smallTo = d;
            last = c;
            lastSmall = sm;
        }
        Check(worstLarge < 0.1f && worstSmall < 0.1f && largeTo >= DetailReach.Distance * 0.85f && smallTo >= DetailReach.Small * 0.85f,
            $"{setting}: large growth shows to {largeTo:F0} m and small to {smallTo:F0} m, fading in bands: at most {worstLarge:P0} of the large's cover goes in 5 m (at {atLarge:F0} m), {worstSmall:P0} of the small's (at {atSmall:F0} m)");
    }

    /// <summary>On this world's thickest woods, from their edge: trees are drawn and show past a kilometre.</summary>
    IEnumerable<int> TreesFar()
    {
        var planet = _game.Surface.Planet;
        float Wooded(double x, double z) => planet.Flora.RulesOf(planet.Palette.BiomeAt(x, z)).Where(r => Planets.Surface.FloraMeshes.IsLarge(r.Kind)).Sum(r => r.Density);
        // The most wooded 1.2 km square near the start, on a 128 m grid: its middle.
        Vector3 start = _game.Survivor.WorldFeet;
        Vector3 best = start;
        float most = 0f;
        for (int i = -20; i <= 20; i++)
        for (int j = -20; j <= 20; j++)
        {
            double x = Mathx.WrapX(start.X + i * 256.0, planet.Width), z = Mathx.Wrap(start.Z + j * 256.0, planet.Height);
            float w = 0f;
            for (int a = -4; a <= 4; a += 2)
            for (int b = -4; b <= 4; b += 2)
                w += Wooded(Mathx.WrapX(x + a * 128.0, planet.Width), Mathx.Wrap(z + b * 128.0, planet.Height));
            if (w > most)
            {
                most = w;
                best = new Vector3((float)x, 0f, (float)z);
            }
        }
        if (most <= 0f)
        {
            Check(true, "(no woods on this world: trees past a kilometre not checked)");
            yield break;
        }
        best = planet.NearestCopy(best, start);
        float g = MathF.Max(planet.GroundHeight(Mathx.WrapX(best.X, planet.Width), Mathx.Wrap(best.Z, planet.Height)), 0f);
        _game.Survivor.PlaceOutside(new Vector3(best.X, g + 0.1f, best.Z).ToNumerics());
        var s = _game.Zone.Streamer;
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed.TotalSeconds < 40 && (clock.Elapsed.TotalSeconds < 4 || !s.FarComplete || s.FarBehind > 0))
        {
            _game.FixedCamera = new Transform3D(Basis.Identity, new Vector3(best.X, g + 1.7f, best.Z));
            yield return 0;
        }
        Vector3 eye = new(best.X, g + 1.7f, best.Z);
        var shown = s.FloraShown(eye).Where(p => p.Shows > 0.05f).ToList();
        float trees = shown.Where(p => p.Layer is TerrainStreamer.FarLayer or TerrainStreamer.DistantLayer).Select(p => p.Away).DefaultIfEmpty(0f).Max();
        float small = shown.Where(p => p.Layer == TerrainStreamer.SmallLayer).Select(p => p.Away).DefaultIfEmpty(0f).Max();
        int past = shown.Count(p => p.Away > 1000f);
        Check(trees > 1000f && past > 20, $"in the woods ({most:F0} cover): trees show to {trees:F0} m, {past} of them past a kilometre; small growth to {small:F0} m");
        _game.FixedCamera = null;
    }
}
