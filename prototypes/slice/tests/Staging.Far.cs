using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using PhysicsSandbox.Bridges;
using Slice.Vessels;

namespace Slice.Tests;

/// <summary>
/// The far land from high up: the Skiff climbs through the game's own controls, stops at each
/// height and looks out at the horizon every way, with what the streamer holds printed beside each
/// frame. --heights=a,b,c, --yaws=0,90,180,270 (degrees), --pitch=2 (degrees down), --time=0.42;
/// --fly=SECONDS flies on ahead at the last height first. Frames go beside --out, as the seam's do.
/// </summary>
public sealed partial class Staging
{
    IEnumerable<int> FarLandView()
    {
        foreach (int x in Seated()) yield return x;
        Vessel v = Skiff;
        _game.Visor.Visible = false;
        _game.Zone.Sky.TimeOfDay = Num("time", 0.42);
        float pitch = (float)Num("pitch", 2) * Mathf.Pi / 180f;
        float[] yaws = Floats("yaws", "0,90,180,270");
        float[] heights = Floats("heights", "320,700,1000");
        void Look(float yaw)
        {
            Vector3 eye = v.Body.Position.ToGodot() + Vector3.Up * 5f;
            Vector3 ahead = new Vector3(MathF.Sin(yaw), 0f, -MathF.Cos(yaw)) * MathF.Cos(pitch) + Vector3.Down * MathF.Sin(pitch);
            _game.FixedCamera = new Transform3D(Basis.LookingAt(ahead, Vector3.Up), eye);
        }
        IEnumerable<int> Stop()
        {
            _game.Controls.ReleaseAll();
            Hurry(1);
            for (int i = 0; i < 240 && v.Body.Velocity.Length() > 2f; i++)
            {
                Look(yaws[0] * Mathf.Pi / 180f);
                yield return 0;
            }
        }
        IEnumerable<int> Frames(string tag)
        {
            foreach (float yaw in yaws)
            {
                for (int i = 0; i < 7; i++)
                {
                    Look(yaw * Mathf.Pi / 180f);
                    yield return 0;
                }
                Snap($"{tag}-y{yaw:0}");
            }
            var s = _game.Zone.Streamer;
            GD.Print($"(far {tag}: at {v.Body.Position.ToGodot()}, far complete {s.FarComplete}, voxels complete to {s.CompleteRadius}, pending {s.Pending}, settled {s.Settled})");
        }
        foreach (float height in heights)
        {
            Hurry(3);
            _game.Controls.Hold(Key.Space);
            for (int i = 0; i < 60 * 90 && v.Body.Position.Y < height; i++)
            {
                Look(yaws[0] * Mathf.Pi / 180f);
                yield return 0;
            }
            foreach (int x in Stop()) yield return x;
            foreach (int x in Frames($"up{height:0}")) yield return x;
        }
        if (_args.TryGetValue("fly", out string fly))
        {
            Hurry(3);
            _game.Controls.Hold(Key.W);
            int ticks = (int)(double.Parse(fly, System.Globalization.CultureInfo.InvariantCulture) * 60);
            for (int i = 0; i < ticks; i++)
            {
                Look(yaws[0] * Mathf.Pi / 180f);
                yield return 0;
            }
            foreach (int x in Stop()) yield return x;
            foreach (int x in Frames("flown")) yield return x;
        }
        _game.FixedCamera = null;
    }

    /// <summary>
    /// The horizon from set heights over a coast and over land: the eye is put at each of --heights
    /// (default 100,300,600,900 m) over a fresh spot of each kind, and looks toward the sea and the
    /// land, level and --down degrees down (default 20). Each frame is logged with how far the
    /// land's own mesh reaches that way, how far the horizon is, the haze at both, and what the band
    /// under the horizon holds (<see cref="HorizonBand"/>). --spots=coast,land; --hide=sheet,clouds,land
    /// leaves those out, to tell what is drawn where.
    /// </summary>
    IEnumerable<int> HorizonView()
    {
        // Mid-morning where the eye is (the sun's time runs round the map): --time=T sets the clock instead.
        double time = Num("time", -1);
        string[] hide = _args.GetValueOrDefault("hide", "").Split(',');
        // A gentle run: 30 frames a second at the most, the GPU's own time measured, and the run
        // stops if a frame or the memory runs away (HorizonCost).
        Engine.MaxFps = 30;
        Rid viewport = _game.GetViewport().GetViewportRid();
        RenderingServer.ViewportSetMeasureRenderTime(viewport, true);
        _game.Zone.Weather.Sim.Snap(Weather.Model.WeatherKind.Clear);
        _game.Visor.Visible = false;
        float[] heights = Floats("heights", "100,300,600,900");
        float down = (float)Num("down", 20);
        var world = _game.Surface.Planet;
        foreach (string kind in _args.GetValueOrDefault("spots", "coast,land").Split(','))
        {
            var (spot, toSea) = HorizonSpot(kind == "coast");
            GD.Print($"(horizon {kind}: map {spot.X:F0},{spot.Y:F0}, looking {toSea})");
            Vector3 at = world.NearestCopy(new Vector3(spot.X, 0f, spot.Y), _game.Survivor.WorldFeet);
            foreach (float height in heights)
            {
                // The sleeper is held well off to one side, out of the frame: the eye is the camera's alone.
                Vector3 eye = new(at.X, height, at.Z), feet = eye + new Vector3(-toSea.Y, 0f, toSea.X) * 40f;
                var streamer = _game.Zone.Streamer;
                var cost = new HorizonCost();
                for (int i = 0; i < 60 * 40 && (i < 180 || !streamer.Settled); i++)
                {
                    _game.Survivor.PlaceOutside(feet.ToNumerics());
                    _game.FixedCamera = new Transform3D(Basis.LookingAt(new Vector3(toSea.X, 0f, toSea.Y), Vector3.Up), eye);
                    yield return 0;
                    if (cost.Sample(viewport, i >= 120) is { } runaway)
                    {
                        Engine.MaxFps = 0;
                        throw new InvalidOperationException($"horizon: stopped at {height:0} m: {runaway}");
                    }
                }
                GD.Print($"(horizon cost at {height:0} m: {cost}, streamer worst {streamer.WorstFrameMs:F1} ms)");
                if (!_args.ContainsKey("time"))
                {
                    var sky = _game.Zone.Sky;
                    double noon = Enumerable.Range(0, 200).Select(k => k / 200.0).OrderByDescending(t => sky.SunAt(t).Y).First();
                    time = noon - 0.1 + (noon < 0.1 ? 1 : 0);
                }
                foreach (var (way, name) in new[] { (toSea, "sea"), (-toSea, "back") })
                foreach (float pitch in new[] { 0f, down })
                {
                    float p = pitch * Mathf.Pi / 180f;
                    Vector3 flat = new Vector3(way.X, 0f, way.Y).Normalized();
                    Vector3 ahead = flat * MathF.Cos(p) + Vector3.Down * MathF.Sin(p);
                    for (int i = 0; i < 8; i++)
                    {
                        _game.Zone.Sky.TimeOfDay = time;
                        if (_game.Orbit?.Globe is { } globe)
                        {
                            globe.GetNode<Node3D>("Ground").Visible = !hide.Contains("sheet");
                            globe.GetNode<Node3D>("Clouds").Visible = !hide.Contains("clouds");
                        }
                        _game.Zone.Streamer.Visible = _game.Surface.Sea.Visible = !hide.Contains("land");
                        _game.Survivor.PlaceOutside(feet.ToNumerics());
                        _game.FixedCamera = new Transform3D(Basis.LookingAt(ahead, Vector3.Up), eye);
                        yield return 0;
                    }
                    string tag = $"{kind}-h{height:0}-{name}-p{pitch:0}";
                    Snap(tag);
                    GD.Print($"(horizon {tag}: {HorizonBand(eye, flat, _game.GetViewport().GetTexture().GetImage())}; settled {streamer.Settled})");
                }
            }
        }
        _game.FixedCamera = null;
        RenderingServer.ViewportSetMeasureRenderTime(viewport, false);
        Engine.MaxFps = 0;
    }

    /// <summary>
    /// What the frames cost while the eye is held at a height: the GPU's and the CPU's worst frame
    /// (the first two seconds after the eye is moved there, while the place loads, left out of the frame check), the most draw calls
    /// and the most memory. A frame over 200 ms or over 6 GB of memory stops the run.
    /// </summary>
    sealed class HorizonCost
    {
        double _gpu, _cpu, _gpuSum;
        long _draws, _video, _ram;
        int _frames;

        public string Sample(Rid viewport, bool settled)
        {
            double gpu = RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewport), cpu = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0;
            long video = (long)Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed), ram = (long)Performance.GetMonitor(Performance.Monitor.MemoryStatic) + GC.GetTotalMemory(false);
            _draws = Math.Max(_draws, (long)Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame));
            _video = Math.Max(_video, video);
            _ram = Math.Max(_ram, ram);
            if (settled)
            {
                _gpu = Math.Max(_gpu, gpu);
                _cpu = Math.Max(_cpu, cpu);
                _gpuSum += gpu;
                _frames++;
                if (gpu > 200 || cpu > 200) return $"a frame took {gpu:F0} ms on the GPU, {cpu:F0} ms on the CPU";
            }
            const long Limit = 6L << 30;
            return video + ram > Limit ? $"memory reached {(video + ram) / (double)(1 << 30):F1} GB" : null;
        }

        public override string ToString() =>
            $"GPU {_gpuSum / Math.Max(_frames, 1):F1} ms mean, {_gpu:F1} worst; CPU process worst {_cpu:F1} ms; draw calls up to {_draws}; video memory {_video >> 20} MB, RAM {_ram >> 20} MB";
    }

    /// <summary>A fresh spot on land by the sea (and the way to the sea), or well inland (and a way along it): never the same two runs.</summary>
    (Vector2 Spot, Vector2 Way) HorizonSpot(bool coast)
    {
        var map = _game.Surface.Planet.Map;
        var rng = new Random();
        float Height(double x, double z) => map.Sample(map.Elevation, Planets.Gen.Mathx.WrapX(x, map.WidthMeters), Planets.Gen.Mathx.Wrap(z, map.HeightMeters));
        for (int tries = 0; tries < 20000; tries++)
        {
            double x = rng.NextDouble() * map.WidthMeters, z = rng.NextDouble() * map.HeightMeters;
            if (Height(x, z) is < 2f or > 40f) continue;
            for (int a = 0; a < 8; a++)
            {
                var way = new Vector2(MathF.Cos(a * MathF.Tau / 8f), MathF.Sin(a * MathF.Tau / 8f));
                bool Land(double d) => Height(x + way.X * d, z + way.Y * d) > 1f;
                bool sea = Height(x + way.X * 1200, z + way.Y * 1200) < -2f && Height(x + way.X * 2500, z + way.Y * 2500) < -2f;
                if (coast ? sea && Land(-1500) : Land(-1500) && Land(1500) && Land(3000))
                    return (new Vector2((float)x, (float)z), way);
            }
        }
        return (new Vector2((float)map.WidthMeters * 0.5f, (float)map.HeightMeters * 0.5f), Vector2.Right);
    }

    /// <summary>
    /// What the band under the horizon holds, looking one way from an eye: how far the land's own mesh
    /// reaches that way and how far the horizon is (where the round world's curve takes the ground out of sight), the haze at each, and in
    /// the frame, from the land's rim up to the horizon, how much the colour varies along each row
    /// (a flat smear of haze varies hardly at all) and how far it stands from the haze's own colour.
    /// </summary>
    string HorizonBand(Vector3 eye, Vector3 way, Image frame)
    {
        var s = _game.Zone.Streamer;
        Vector3 rim = s.HorizonRim;
        float h = eye.Y;
        float horizon = PlanetSurface.Terrain.TerrainStreamer.RoundFrom + MathF.Sqrt(2f * PlanetSurface.Terrain.TerrainStreamer.RoundRadius * MathF.Max(h, 1f));
        // Out along the way to the square's edge, less the 600 m it thins away over.
        float tx = MathF.Abs(way.X) > 1e-4f ? ((way.X > 0 ? rim.X + rim.Z : rim.X - rim.Z) - eye.X) / way.X : float.MaxValue;
        float tz = MathF.Abs(way.Z) > 1e-4f ? ((way.Z > 0 ? rim.Y + rim.Z : rim.Y - rim.Z) - eye.Z) / way.Z : float.MaxValue;
        float land = rim.Z > 0f ? MathF.Min(tx, tz) - 600f : float.NaN;
        var env = _game.Zone.Sky.Environment;
        float Haze(float d) => 1f - MathF.Exp(-env.FogDensity * d);
        Camera3D cam = _game.Camera;
        float Row(float d) => cam.UnprojectPosition(eye + way * d + Vector3.Down * (h + PlanetSurface.Terrain.TerrainStreamer.RoundSink(d))).Y;
        int top = (int)Row(horizon), bottom = (int)Row(MathF.Min(land, horizon * 0.9f));
        string band = "";
        if (frame != null && top > 0 && bottom > top + 2 && bottom < frame.GetHeight())
        {
            Color fog = env.FogLightColor;
            float spread = 0f, off = 0f;
            int rows = 0;
            for (int y = top + 1; y < bottom; y++, rows++)
            {
                float sum = 0f, sum2 = 0f, away = 0f;
                int n = 0;
                for (int x = 0; x < frame.GetWidth(); x += 4, n++)
                {
                    Color c = frame.GetPixel(x, y);
                    float l = 0.3f * c.R + 0.59f * c.G + 0.11f * c.B;
                    sum += l;
                    sum2 += l * l;
                    away += MathF.Abs(c.R - fog.R) + MathF.Abs(c.G - fog.G) + MathF.Abs(c.B - fog.B);
                }
                spread += MathF.Sqrt(MathF.Max(sum2 / n - sum * sum / ((float)n * n), 0f));
                off += away / n;
            }
            band = $", band rows {top}-{bottom}: spread {spread / rows * 255f:F1}/255, off the haze {off / rows * 255f:F1}/255";
        }
        return $"eye {h:F0} m, land's mesh to {land:F0} m, horizon {horizon:F0} m ({land / horizon:P0}), far field to {_game.Orbit?.Globe.Reach ?? 0f:F0} m; haze {Haze(MathF.Min(land, horizon)):P0} at the land's rim, {Haze(horizon):P0} at the horizon (density {env.FogDensity * 1e6f:F0}e-6){band}";
    }

    float[] Floats(string key, string fallback) =>
        _args.GetValueOrDefault(key, fallback).Split(',').Select(h => float.Parse(h, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
}
