using System;
using System.Diagnostics;
using GalHack.Terrain;
using Godot;
using Planets.Gen;
using PlanetSurface.World;

namespace PlanetSurface.Tests;

/// <summary>
/// The sea is drawn where it is: on fresh worlds (temperate and arid, rolled anew each run) the water
/// shader's mask, read as its filter reads it, says sea exactly where the terrain and the physics do;
/// and wherever the drawn sea stops, the ground stands clear of its surface, so no edge of it hangs
/// in the air over a dip. No engine needed beyond the world's build.
///
/// Run: .\tools\test.ps1 -Project prototypes/planet-surface -Scene res://tests/SeaTest.tscn
/// </summary>
public partial class SeaTest : Node
{
    int _failures, _passes;

    public override void _Ready()
    {
        new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(6));
            Console.Error.WriteLine("FAIL  the run took over 6 minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true }.Start();
        var clock = Stopwatch.StartNew();
        try { Run(); }
        catch (Exception e) { Check(false, $"threw: {e}"); }
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        GalHack.Testing.Backstop.Quit(this, _failures == 0 ? 0 : 1);
    }

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"{(ok ? "PASS" : "FAIL")}  {what}");
    }

    void Run()
    {
        var rand = new Random();
        foreach (Archetype kind in new[] { Archetype.Temperate, Archetype.Arid, Archetype.Arid })
        {
            ulong seed = (ulong)rand.NextInt64();
            PlanetWorld w = PlanetWorld.Build(PlanetSpec.Roll(seed, kind, 0), Main.Catalog);
            PlanetTerrainSource source = w.Source;
            Image mask = w.SeaTexture().GetImage();
            int coast = 0, disagree = 0, hanging = 0;
            float worst = 0f;
            // Points along every coast: a fine walk across map cells where the sea's margin runs.
            for (int k = 0; k < w.Map.Count && coast < 20000; k += 7)
            {
                if (MathF.Abs(w.Map.Elevation[k] + PlanetTerrainSource.SeaMaskDepth) > 6f) continue;
                int i = k % w.Map.Width, j = k / w.Map.Width;
                for (int s = 0; s < 6; s++)
                {
                    double x = (i + rand.NextDouble()) * w.Map.CellSize, z = (j + rand.NextDouble()) * w.Map.CellSize;
                    coast++;
                    bool drawn = PlanetWorld.SeaMaskAt(mask, x, z, w.Width, w.Height) >= PlanetTerrainSource.SeaMaskDepth;
                    float e = w.Map.Sample(w.Map.Elevation, x, z);
                    bool sea = e < -PlanetTerrainSource.SeaMaskDepth;
                    // Half floats round a little: only a hair either side of the margin may read either way.
                    if (drawn != sea && MathF.Abs(e + PlanetTerrainSource.SeaMaskDepth) > 0.01f) disagree++;
                    if (drawn || source.Sampler.Sample(x, z, 4).InRiver) continue;
                    // Where the sea isn't drawn, the ground stands at least at its surface: nothing it would hang over.
                    float g = source.SurfaceHeight(x, z);
                    if (g < source.WaterLevel - 0.01f) { hanging++; worst = MathF.Max(worst, source.WaterLevel - g); }
                }
            }
            Check(coast > 1000 && disagree == 0, $"{kind} world {seed}: the drawn sea and the terrain's agree along its coasts ({coast} points, {disagree} differ)");
            Check(hanging == 0, $"{kind} world {seed}: and where it isn't drawn the ground stands clear of its surface ({hanging} points under it, the worst {worst:F2} m)");
        }
    }
}
