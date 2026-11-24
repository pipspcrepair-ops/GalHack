using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using PlanetSurface.Skies;
using Slice.Game;
using Weather.Model;

namespace Slice.Tests;

/// <summary>
/// Fog weather lies on the low ground as a layer you can climb out of; only the air's haze and what rain puts in the air
/// fill the whole sky. On a fresh random world with air (--type picks its kind), in clear, fog and rain weather, the fog
/// as the game sets it is read back:
/// - nowhere within 3 km does fog weather fill the air above its layer (a region only part fit for fog included), and
///   the eye is held where the fog is most mixed with its fallback, 2 m over the layer's floor and 400 m up;
/// - in fog weather low in the layer the visibility is the weather's own (the layer is there);
/// - 400 m up in fog weather the air fogs the land and the sky no more than clear air does there: the layer stays below;
/// - 400 m up in clear air land 3 km off keeps at least 15 % of its own colour, hazed but never erased.
///
/// Run: G:\GalHack\.tools\queue\test-q.ps1 -Project prototypes/slice -Scene res://tests/FogLayerTest.tscn
/// </summary>
public partial class FogLayerTest : Node
{
    const int WatchdogMinutes = 8;
    const float Up = 400f, Far = 3000f;

    Game.Game _game;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();

    public override void _Ready()
    {
        new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(WatchdogMinutes));
            Console.Error.WriteLine($"FAIL  the run took over {WatchdogMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true }.Start();
        ProcessPhysicsPriority = 100;
        _run = Scenario().GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        bool more;
        try { more = _run.MoveNext(); }
        catch (Exception e)
        {
            Check(false, $"scenario threw: {e}");
            more = false;
        }
        if (more) return;
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {_clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        SetPhysicsProcess(false);
        Wildlife.Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
    }

    /// <summary>What the fog takes of a thing, 0 to 1, as Godot's fog has it: by its distance, and by how deep it lies under the height fog's top.</summary>
    static float Fogged(Godot.Environment env, float distance, float y)
    {
        float amount = 1f - MathF.Exp(-env.FogDensity * distance);
        if (MathF.Abs(env.FogHeightDensity) >= 0.0001f) amount = MathF.Max(amount, 1f - MathF.Exp(MathF.Min(0f, (y - env.FogHeight) * env.FogHeightDensity)));
        return amount;
    }

    record struct Seen(float Density, float Land, float Sky, float Visibility);

    IEnumerable<int> Scenario()
    {
        WorldChoice world = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Fresh("random"));
        for (int i = 0; i < 4096 && SkySystem.For(world.Spec()).Thickness <= 0f; i++) world = WorldChoice.Fresh("random"); // fog needs air
        _game = new Game.Game { Interactive = false, SkipIntro = true, World = world };
        AddChild(_game);
        var drive = new Driver(_game);
        foreach (int x in drive.UntilPlaying()) yield return x;
        GD.Print($"-- fog lies on the low ground, on {_game.World} --");
        var zone = _game.Zone;
        zone.Weather.Bridge.Calm = true;
        zone.Wildlife.Frozen = true;
        zone.Sky.Paused = true;
        zone.Sky.TimeOfDay = 0.45;
        var env = zone.Sky.Environment;
        var sim = zone.Weather.Sim;
        var sky = _game.Surface.Planet.Sky;
        Vector3 feet = _game.Survivor.WorldFeet, worst = feet;

        // Fog weather over the land round the start, as the air over each place has it (a region only part fit for fog
        // thins it toward its fallback): nowhere does it fill the air above its layer, and the eye goes where it is most mixed.
        sim.Snap(WeatherKind.Fog);
        float most = 0f, mixed = 1f;
        for (float dx = -3000f; dx <= 3000f; dx += 150f)
        for (float dz = -3000f; dz <= 3000f; dz += 150f)
        {
            Vector3 p = feet + new Vector3(dx, 0f, dz);
            p.Y = MathF.Max(zone.GroundAt(p.X, p.Z).Y, 0f) + Up;
            AtmosphereState a = Atmosphere.Compute(sky, Surface.SurfaceWeather.AirAt(sim, p), 0.5f);
            most = MathF.Max(most, a.AirFog);
            if (a.GroundFog > 0.05f && MathF.Abs(a.GroundFog - 0.5f) < mixed) (mixed, worst) = (MathF.Abs(a.GroundFog - 0.5f), p);
        }
        Check(most <= Atmosphere.HazeMost * 1.001f, $"fog weather fills the air above its layer nowhere within 3 km: at most {most * 1e6f:F0}e-6 against the haze's {Atmosphere.HazeMost * 1e6f:F0}e-6");

        IEnumerable<int> Hold(WeatherKind kind, Vector3 eye)
        {
            for (int i = 0; i < 150; i++) // the layer's floor is found once a second
            {
                sim.Snap(kind);
                _game.FixedCamera = new Transform3D(Basis.LookingAt(Vector3.Right, Vector3.Up), eye);
                yield return 0;
            }
        }
        if (mixed >= 1f)
        {
            GD.Print("  (no ground within 3 km takes fog: nothing more to see)");
            yield break;
        }
        float ground = MathF.Max(zone.GroundAt(worst.X, worst.Z).Y, 0f);
        foreach (int x in Hold(WeatherKind.Fog, new Vector3(worst.X, ground + Up, worst.Z))) yield return x;
        float top = env.FogHeight, floor = top - zone.Sky.Air.ValleyFogDepth; // the layer as it lies round the focus
        GD.Print($"  (the eye over a place with ground fog {zone.Sky.Air.GroundFog:F2}, its ground {ground:F0} m, the layer {floor:F0} to {top:F0} m)");
        var seen = new Dictionary<(WeatherKind, bool), Seen>();
        foreach (WeatherKind kind in new[] { WeatherKind.Clear, WeatherKind.Fog, WeatherKind.Rain })
        foreach (bool high in new[] { false, true })
        {
            Vector3 eye = new(worst.X, high ? ground + Up : MathF.Max(ground, floor) + 2f, worst.Z);
            foreach (int x in Hold(kind, eye)) yield return x;
            var s = new Seen(env.FogDensity, Fogged(env, MathF.Sqrt(Far * Far + (eye.Y - ground) * (eye.Y - ground)), ground), env.FogSkyAffect, zone.Sky.Air.Visibility);
            seen[(kind, high)] = s;
            GD.Print($"  ({kind} at {eye.Y:F0} m: density {s.Density * 1e6f:F0}e-6, height fog {env.FogHeightDensity:F4} under {env.FogHeight:F0} m, sky {s.Sky:F2}, land {Far / 1000f:F0} km off {s.Land:P0} fogged)");
        }
        _game.FixedCamera = null;

        Seen fogLow = seen[(WeatherKind.Fog, false)], fogHigh = seen[(WeatherKind.Fog, true)], clearHigh = seen[(WeatherKind.Clear, true)];
        float own = 2.3f * 0.85f / fogLow.Visibility;
        if (MathF.Max(ground, floor) + 2f < top) Check(fogLow.Density >= own * 0.8f, $"in fog weather low in the layer the fog is the weather's own: density {fogLow.Density * 1e6f:F0}e-6 against {own * 1e6f:F0}e-6");
        else GD.Print("  (this ground stands above the layer)");
        Check(fogHigh.Density <= Math.Max(clearHigh.Density, Atmosphere.HazeMost) * 1.05f,
            $"{Up:F0} m up in fog weather the air is no thicker than clear air's: {fogHigh.Density * 1e6f:F0}e-6 against {clearHigh.Density * 1e6f:F0}e-6");
        Check(fogHigh.Sky <= clearHigh.Sky + 0.05f, $"{Up:F0} m up in fog weather the sky is no more fogged than a clear one's: {fogHigh.Sky:F2} against {clearHigh.Sky:F2}");
        Check(clearHigh.Land <= 0.85f, $"{Up:F0} m up in clear air land {Far / 1000f:F0} km off is hazed, never erased: {clearHigh.Land:P0} fogged");
    }
}
