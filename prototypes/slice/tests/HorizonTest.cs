using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using Planets.Gen;
using Planets.Look;
using PlanetSurface.Terrain;
using Slice.Game;

namespace Slice.Tests;

/// <summary>
/// The flat land reaches its horizon at every height: on a fresh random world, the eye is held at
/// 100, 300, 600 and 900 m over a fresh coast on a clear day, and at each height
/// - the land's own mesh is whole out to its reach round the eye, and that reach is never past half
///   the map either way, so no place is drawn twice however small the world (the sky's far ground
///   goes on past it);
/// - the haze leaves the horizon band at least 15 % of the land's own colour, so it is never erased;
/// - the band under the horizon, as it stands hazed, is not one flat colour: land and sea round
///   it differ from each other and from the haze.
/// The band is worked out from the map's own colours and the fog as set, so it runs headless.
///
/// Run: .\tools\test.ps1 -Project prototypes/slice -Scene res://tests/HorizonTest.tscn
/// </summary>
public partial class HorizonTest : Node
{
    const int WatchdogMinutes = 8;
    static readonly float[] Heights = { 100f, 300f, 600f, 900f };

    /// <summary>The least of the land's own colour the haze leaves at the horizon.</summary>
    const float LeastLeft = 0.15f;

    /// <summary>The least the hazed band varies round the horizon, and stands off the haze (linear RGB, summed).</summary>
    const float LeastSpread = 0.02f, LeastOff = 0.03f;

    Game.Game _game;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();

    public override void _Ready()
    {
        var thread = new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(WatchdogMinutes));
            Console.Error.WriteLine($"FAIL  the run took over {WatchdogMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true };
        thread.Start();
        ProcessPhysicsPriority = 100;
        _run = Scenario().GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        bool more;
        try { more = _run.MoveNext(); }
        catch (Exception e)
        {
            Fail($"scenario threw: {e}");
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

    void Fail(string what) => Check(false, what);

    IEnumerable<int> Scenario()
    {
        _game = new Game.Game { Interactive = false, SkipIntro = true, World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Fresh("random")) };
        AddChild(_game);
        var drive = new Driver(_game);
        foreach (int x in drive.UntilPlaying()) yield return x;
        GD.Print($"-- the horizon from high up, on {_game.World} --");
        var zone = _game.Zone;
        zone.Weather.Bridge.Calm = true;
        zone.Weather.Sim.Snap(Weather.Model.WeatherKind.Clear);
        zone.Wildlife.Frozen = true;
        var world = _game.Surface.Planet;
        var (spot, way) = CoastSpot(world.Map);
        Vector3 at = world.NearestCopy(new Vector3(spot.X, 0f, spot.Y), _game.Survivor.WorldFeet);
        GD.Print($"  (a fresh coast at map {spot.X:F0}, {spot.Y:F0})");
        foreach (float height in Heights)
        {
            Vector3 eye = new(at.X, height, at.Z), feet = eye + new Vector3(-way.Y, 0f, way.X) * 40f;
            var streamer = zone.Streamer;
            for (int i = 0; i < 60 * 40 && (i < 120 || !streamer.Settled); i++)
            {
                zone.Weather.Sim.Snap(Weather.Model.WeatherKind.Clear);
                _game.Survivor.PlaceOutside(feet.ToNumerics());
                _game.FixedCamera = new Transform3D(Basis.LookingAt(new Vector3(way.X, 0f, way.Y), Vector3.Up), eye);
                yield return 0;
            }
            GD.Print($"  (held at {height:F0} m, the ground {(streamer.Settled ? "all in" : "still coming in")}, {_clock.Elapsed.TotalSeconds:F0} s in)");
            Measure(eye);
        }
        _game.FixedCamera = null;
    }

    void Measure(Vector3 eye)
    {
        var world = _game.Surface.Planet;
        var streamer = _game.Zone.Streamer;
        float h = eye.Y;
        // The land drawn round the eye: its mesh whole out to the reach each way, the reach at most half the map.
        Vector2 reach = streamer.LandReach;
        var (mesh, at) = streamer.DrawnHorizon();
        float spareX = TerrainStreamer.HorizonReach - MathF.Abs(at.X - eye.X) - reach.X, spareZ = TerrainStreamer.HorizonReach - MathF.Abs(at.Z - eye.Z) - reach.Y;
        Check(mesh != null && spareX >= 0f && spareZ >= 0f && reach.X <= world.Width / 2 + 0.01 && reach.Y <= world.Height / 2 + 0.01 && MathF.Min(reach.X, reach.Y) >= MathF.Min(TerrainStreamer.HorizonDistance, (float)world.Height / 2f),
            $"at {h:F0} m the land is whole to its reach round the eye, {reach.X:F0} m east and west and {reach.Y:F0} m north and south, none of the {world.Width / 1000:F0} x {world.Height / 1000:F0} km map drawn twice (the mesh {spareX:F0} and {spareZ:F0} m past it)");
        float horizon = MathF.Min(reach.X, reach.Y), land = horizon;

        // The haze, as set for this eye.
        var env = _game.Zone.Sky.Environment;
        float Haze(float d) => 1f - MathF.Exp(-env.FogDensity * d);
        Check(1f - Haze(horizon) >= LeastLeft,
            $"at {h:F0} m the haze softens the horizon and leaves the land: {Haze(horizon):P0} at the horizon, {Haze(MathF.Min(land, horizon)):P0} where the land's own mesh ends (fog {env.FogDensity * 1e6f:F0}e-6)");

        // The band under the horizon, from the map's own colours, hazed as the fog has it.
        Color fog = env.FogLightColor;
        var look = world.Look;
        var samples = new List<Color>();
        for (int a = 0; a < 96; a++)
        for (int r = 0; r < 3; r++)
        {
            float d = horizon * (0.7f + 0.12f * r), angle = a * MathF.Tau / 96f;
            double x = Mathx.WrapX(eye.X + MathF.Cos(angle) * d, world.Width), z = Mathx.Wrap(eye.Z + MathF.Sin(angle) * d, world.Height);
            float e = world.Map.Sample(world.Map.Elevation, x, z);
            Color c;
            if (e < 0f) c = look.Shallow.Lerp(look.Deep, Mathf.Clamp(-e / 40f, 0f, 1f));
            else
            {
                Rgb g = world.Palette.FarGround(x, z, e, 1f, out _);
                c = new Color(g.R, g.G, g.B);
            }
            samples.Add(c.Lerp(fog, Haze(d)));
        }
        float Lum(Color c) => 0.3f * c.R + 0.59f * c.G + 0.11f * c.B;
        float mean = samples.Average(Lum);
        float spread = MathF.Sqrt(samples.Average(c => (Lum(c) - mean) * (Lum(c) - mean)));
        float off = samples.Average(c => MathF.Abs(c.R - fog.R) + MathF.Abs(c.G - fog.G) + MathF.Abs(c.B - fog.B));
        Check(spread >= LeastSpread && off >= LeastOff,
            $"at {h:F0} m the band under the horizon is land and sea, not a flat smear: it varies by {spread:F3} round the horizon and stands {off:F3} off the haze");
    }

    /// <summary>A fresh spot on land with open sea one way: never the same two runs.</summary>
    static (Vector2 Spot, Vector2 Way) CoastSpot(WorldMap map)
    {
        var rng = new Random();
        float Height(double x, double z) => map.Sample(map.Elevation, Mathx.WrapX(x, map.WidthMeters), Mathx.Wrap(z, map.HeightMeters));
        for (int tries = 0; tries < 20000; tries++)
        {
            double x = rng.NextDouble() * map.WidthMeters, z = rng.NextDouble() * map.HeightMeters;
            if (Height(x, z) is < 2f or > 40f) continue;
            for (int a = 0; a < 8; a++)
            {
                var way = new Vector2(MathF.Cos(a * MathF.Tau / 8f), MathF.Sin(a * MathF.Tau / 8f));
                if (Height(x + way.X * 1200, z + way.Y * 1200) < -2f && Height(x - way.X * 1500, z - way.Y * 1500) > 1f)
                    return (new Vector2((float)x, (float)z), way);
            }
        }
        return (new Vector2((float)map.WidthMeters * 0.5f, (float)map.HeightMeters * 0.5f), Vector2.Right);
    }
}
