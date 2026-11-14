using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;
using PlanetSurface.Terrain;

namespace Slice.Tests;

/// <summary>
/// The far ground, CPU meshes against the GPU rings, in the same views of one world (run with --ground=ab): a
/// mountain range from 3 and 5 km, the ground under a fly-over, the near and far ground's meeting line, a road and a
/// river far off, a night view; then a ridge approached from 2 km at 70 m/s, once with each, frames at the same points.
/// Frames go beside --out as NAME-TAG-mesh.png and NAME-TAG-gpu.png.
/// </summary>
public sealed partial class Staging
{
    IEnumerable<int> GroundAb()
    {
        var zone = _game.Zone;
        TerrainStreamer s = zone.Streamer;
        _game.Visor.Visible = false;
        zone.Weather.Bridge.Calm = true;
        zone.Weather.Sim.Snap(Weather.Model.WeatherKind.Clear);
        zone.Wildlife.Frozen = true;
        double day = Num("time", 0.36);
        zone.Sky.TimeScale = 0f;
        Vector3 start = _game.Survivor.WorldFeet;
        float Ground(float x, float z) => zone.GroundAt(x, z).Y;
        Vector3 Lift(Vector3 p, float up) => p with { Y = Ground(p.X, p.Z) + up };

        // The highest ground within 5 km, and the way to it.
        Vector3 peak = start;
        for (int k = 0; k < 36; k++)
            for (int i = 4; i <= 20; i++)
            {
                var d = new Vector3(MathF.Cos(k * MathF.Tau / 36), 0f, MathF.Sin(k * MathF.Tau / 36));
                Vector3 p = Lift(start + d * 250f * i, 0f);
                if (p.Y > peak.Y) peak = p;
            }
        Vector3 way = (peak - start) with { Y = 0f };
        way = way.LengthSquared() > 1f ? way.Normalized() : Vector3.Forward;
        GD.Print($"(ground: peak {peak.Y - Ground(start.X, start.Z):F0} m above the start, {new Vector2(peak.X - start.X, peak.Z - start.Z).Length():F0} m off)");

        // A road and a river 500 to 1,500 m off, if the world has them.
        Vector3? road = null, river = null;
        var source = zone.Planet.Source;
        double W = zone.Planet.Width, H = zone.Planet.Height;
        for (float r = 500f; r < 1500f && (road == null || river == null); r += 16f)
            for (float a = 0f; a < MathF.Tau && (road == null || river == null); a += 12f / r)
            {
                Vector3 p = start + new Vector3(MathF.Cos(a), 0f, MathF.Sin(a)) * r;
                double cx = Planets.Gen.Mathx.WrapX(p.X, W), cz = Planets.Gen.Mathx.Wrap(p.Z, H);
                if (road == null && source.OnRoad(cx, cz, out _)) road = p;
                if (river == null && source.Ground(cx, cz, 4).InRiver) river = p;
            }

        var views = new List<(string Tag, Vector3 Eye, Vector3 At, double Time)>
        {
            ("range3k", Lift(peak - way * 3000f, 80f), peak, day),
            ("range5k", Lift(peak - way * 5000f, 150f), peak, day),
            ("flyover", Lift(start, 60f), Lift(start + way * 300f, 0f), day),
            ("meeting", Lift(start, 1.8f), Lift(start + way * 110f, 0f), day),
            ("night", Lift(start, 40f), peak, Num("night", 0.97)),
        };
        if (road is { } rd) views.Add(("road", Lift(start + (rd - start).Normalized() * 100f, 120f), Lift(rd, 0f), day));
        if (river is { } rv) views.Add(("river", Lift(start + (rv - start).Normalized() * 100f, 120f), Lift(rv, 0f), day));
        GD.Print($"(ground: road {(road != null ? "found" : "none")}, river {(river != null ? "found" : "none")} within 1.5 km)");

        foreach (var (tag, eye, at, time) in views)
        {
            zone.Sky.TimeOfDay = time;
            for (int i = 0; i < 400 && (i < 90 || !s.Settled); i++)
            {
                Hold(eye, at);
                yield return 0;
            }
            foreach (bool gpu in new[] { false, true })
            {
                s.ShowGpu = gpu;
                for (int i = 0; i < 8; i++)
                {
                    Hold(eye, at);
                    yield return 0;
                }
                Snap($"{tag}-{(gpu ? "gpu" : "mesh")}");
            }
        }

        // A ridge approached from 2 km at 70 m/s, 60 m up: frames at 1.5, 1 and 0.5 km from it, once with each.
        zone.Sky.TimeOfDay = day;
        foreach (bool gpu in new[] { false, true })
        {
            s.ShowGpu = gpu;
            Vector3 from = peak - way * 2000f;
            for (int i = 0; i < 400 && (i < 90 || !s.Settled); i++)
            {
                Hold(Lift(from, 60f), peak);
                yield return 0;
            }
            float along = 0f;
            var marks = new Queue<float>(new[] { 500f, 1000f, 1500f });
            while (marks.Count > 0)
            {
                along += 70f / Engine.PhysicsTicksPerSecond;
                Vector3 eye = Lift(from + way * along, 60f);
                Hold(eye, peak);
                yield return 0;
                if (along < marks.Peek()) continue;
                Snap($"approach{marks.Dequeue():0}-{(gpu ? "gpu" : "mesh")}");
            }
        }
        _game.FixedCamera = null;
        zone.Sky.TimeScale = 1f;

        void Hold(Vector3 eye, Vector3 at)
        {
            // The sleeper stands in for the eye (the streaming follows it), the camera at the eye.
            _game.Survivor.PlaceOutside(eye.ToNumerics());
            Vector3 look = at - eye;
            _game.FixedCamera = new Transform3D(Basis.LookingAt(look.Normalized(), Vector3.Up), eye);
        }
    }
}
