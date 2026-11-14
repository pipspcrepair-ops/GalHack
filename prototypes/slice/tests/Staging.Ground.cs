using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;
using PlanetSurface.Terrain;

namespace Slice.Tests;

/// <summary>
/// The far ground (the GPU rings) in the views that show it: a mountain range from 3 and 5 km, the ground under a
/// fly-over, the near and far ground's meeting line, a road and a river far off, a night view; then a ridge approached
/// from 2 km at 70 m/s, frames at 1.5, 1 and 0.5 km. Frames go beside --out as NAME-TAG.png.
/// </summary>
public sealed partial class Staging
{
    IEnumerable<int> FarGroundShots()
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
            ("meeting", Lift(start + way * 300f, 1.8f), Lift(start + way * 410f, 0f), day),
            ("meeting-up", Lift(start + way * 300f, 25f), Lift(start + way * 400f, 0f), day),
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
            Snap(tag);
        }

        // A ridge approached from 2 km at 70 m/s, 60 m up: frames at 1.5, 1 and 0.5 km from it.
        zone.Sky.TimeOfDay = day;
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
            Hold(Lift(from + way * along, 60f), peak);
            yield return 0;
            if (along < marks.Peek()) continue;
            Snap($"approach{marks.Dequeue():0}");
        }
        _game.FixedCamera = null;
        zone.Sky.TimeScale = 1f;
        void Hold(Vector3 eye, Vector3 at) => HoldEye(eye, at);
    }

    /// <summary>
    /// What the far ground costs the graphics card, the same frames one after another: low over the ground looking along it
    /// (as on a bike) and 60 m up looking ahead and down (as flying), with the rings casting shadows as built, the finest
    /// alone casting, none casting, and the rings hidden. The GPU's frame time over two seconds of each.
    /// </summary>
    IEnumerable<int> FarGroundCost()
    {
        var zone = _game.Zone;
        TerrainStreamer s = zone.Streamer;
        _game.Visor.Visible = false;
        zone.Weather.Bridge.Calm = true;
        zone.Weather.Sim.Snap(Weather.Model.WeatherKind.Clear);
        zone.Wildlife.Frozen = true;
        zone.Sky.TimeScale = 0f;
        zone.Sky.TimeOfDay = Num("time", 0.36);
        Rid viewport = _game.GetViewport().GetViewportRid();
        RenderingServer.ViewportSetMeasureRenderTime(viewport, true);
        Vector3 start = _game.Survivor.WorldFeet, way = Vector3.Forward;
        Vector3 Lift(Vector3 p, float up) => p with { Y = zone.GroundAt(p.X, p.Z).Y + up };
        var blocks = s.Clip.GetChildren().OfType<MeshInstance3D>().ToList();
        int Level(MeshInstance3D m) => m.Name.ToString().Split(' ')[1][0] - '0';
        var built = blocks.ToDictionary(m => m, m => m.CastShadow);
        var configs = new (string Name, Action Apply)[]
        {
            ("as built", () => { foreach (var m in blocks) m.CastShadow = built[m]; s.Clip.Visible = true; }),
            ("finest casts", () => { foreach (var m in blocks) m.CastShadow = Level(m) == 0 ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off; }),
            ("none cast", () => { foreach (var m in blocks) m.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off; }),
            ("rings hidden", () => s.Clip.Visible = false),
        };
        foreach (var (tag, eye, at) in new[] { ("low", Lift(start, 1.6f), Lift(start + way * 400f, 0f)), ("high", Lift(start, 60f), Lift(start + way * 300f, 0f)) })
        {
            for (int i = 0; i < 600 && (i < 120 || !s.Settled); i++)
            {
                HoldEye(eye, at);
                yield return 0;
            }
            foreach (var (name, apply) in configs)
            {
                apply();
                var gpu = new List<double>();
                for (int i = 0; i < 150; i++)
                {
                    HoldEye(eye, at);
                    yield return 0;
                    if (i >= 30) gpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewport));
                }
                gpu.Sort();
                GD.Print($"(ground cost {tag}, {name}: GPU {gpu.Average():F2} ms mean, {gpu[gpu.Count / 2]:F2} median, {gpu[^1]:F2} worst; draw calls {Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame)}, primitives {Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame) / 1000:F0}k)");
            }
            configs[0].Apply();
        }
        _game.FixedCamera = null;
        zone.Sky.TimeScale = 1f;
    }

    /// <summary>
    /// The far shade as the graphics card marches it, read back: against the same march on the CPU at random cells, held
    /// still at a low sun; then flying level at 70 m/s, the same ground's cells marched again as the square moves on must
    /// hold their rise (no crawling or popping as the square moves).
    /// </summary>
    IEnumerable<int> FarShadeCheck()
    {
        var zone = _game.Zone;
        TerrainStreamer s = zone.Streamer;
        _game.Visor.Visible = false;
        zone.Weather.Bridge.Calm = true;
        zone.Weather.Sim.Snap(Weather.Model.WeatherKind.Clear);
        zone.Wildlife.Frozen = true;
        zone.Sky.TimeScale = 0f;
        zone.Sky.TimeOfDay = Num("time", 0.27);
        Vector3 start = _game.Survivor.WorldFeet, way = Vector3.Forward;
        Vector3 Lift(Vector3 p, float up) => p with { Y = zone.GroundAt(p.X, p.Z).Y + up };
        RenderingDevice rd = RenderingServer.GetRenderingDevice();
        float[] gpu = null;
        IEnumerable<int> Read()
        {
            gpu = null;
            Rid rid = s.Clip.ShadeMap.TextureRdRid;
            RenderingServer.CallOnRenderThread(Callable.From(() => gpu = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(rd.TextureGetData(rid, 0)).ToArray()));
            for (int i = 0; i < 10 && gpu == null; i++) yield return 0;
        }
        for (int i = 0; i < 900 && (i < 120 || !s.Settled); i++)
        {
            HoldEye(Lift(start, 60f), Lift(start + way * 500f, 0f));
            yield return 0;
        }
        foreach (int x in Read()) yield return x;
        var (corner, cell, toward) = s.ShadeSquare;
        var rng = new Random();
        double worst = 0, sum = 0;
        int n = 0, shaded = 0;
        for (int k = 0; k < 400; k++)
        {
            int i = rng.Next(FarShade.N), j = rng.Next(FarShade.N);
            float cpu = FarShade.Rise(s.Clip.Tops, corner.X, corner.Y, cell, toward, i, j), g = gpu[j * FarShade.N + i];
            worst = Math.Max(worst, Math.Abs(cpu - g));
            sum += Math.Abs(cpu - g);
            n++;
            if (g > 0.05f) shaded++;
        }
        GD.Print($"(far shade: graphics card against CPU over {n} cells: mean {sum / n:F4}, worst {worst:F4} in rise; {shaded} cells rising over 3 degrees; square {cell * FarShade.N:F0} m, cells {cell:F1} m)");
        // Flying: two marches a second apart, compared where their squares overlap, cell for cell.
        float along = 0f;
        float[] before = null;
        Vector2 cornerBefore = default;
        double crawl = 0;
        int compared = 0, moved = 0;
        for (int t = 0; t < 4; t++)
        {
            for (int i = 0; i < 60; i++)
            {
                along += 70f / Engine.PhysicsTicksPerSecond;
                HoldEye(Lift(start + way * along, 60f), Lift(start + way * (along + 500f), 0f));
                yield return 0;
            }
            foreach (int x in Read()) yield return x;
            var (c2, cell2, _) = s.ShadeSquare;
            if (before != null)
            {
                int di = (int)MathF.Round((c2.X - cornerBefore.X) / cell2), dj = (int)MathF.Round((c2.Y - cornerBefore.Y) / cell2);
                if (di != 0 || dj != 0) moved++;
                // Away from the square's edges (whose marches are cut short by them), the same cells must hold their rise.
                for (int j = 40; j < FarShade.N - 40; j++)
                for (int i = 40; i < FarShade.N - 40; i++)
                {
                    int bi = i + di, bj = j + dj;
                    if (bi < 40 || bj < 40 || bi >= FarShade.N - 40 || bj >= FarShade.N - 40) continue;
                    crawl = Math.Max(crawl, Math.Abs(gpu[j * FarShade.N + i] - before[bj * FarShade.N + bi]));
                    compared++;
                }
            }
            before = gpu;
            cornerBefore = c2;
        }
        GD.Print($"(far shade in flight: {moved} moves of the square compared, {compared} cells, the most a cell's rise changed {crawl:F4})");
        _game.FixedCamera = null;
        zone.Sky.TimeScale = 1f;
    }
    /// <summary>
    /// A near cliff from 100, 200 and 300 m (the 2 and 4 m rings): the steepest within 1.5 km of the start, the eye 10 m up on
    /// its low side looking at its face. Frames go beside --out as NAME-cliff100.png and on.
    /// </summary>
    IEnumerable<int> CliffShots()
    {
        var zone = _game.Zone;
        TerrainStreamer s = zone.Streamer;
        _game.Visor.Visible = false;
        zone.Weather.Bridge.Calm = true;
        zone.Weather.Sim.Snap(Weather.Model.WeatherKind.Clear);
        zone.Wildlife.Frozen = true;
        zone.Sky.TimeScale = 0f;
        zone.Sky.TimeOfDay = Num("time", 0.36);
        Vector3 start = _game.Survivor.WorldFeet;
        float Ground(float x, float z) => zone.GroundAt(x, z).Y;
        var rng = new Random();
        (Vector3 At, Vector3 Away, float Rise) best = (start, Vector3.Forward, 0f);
        for (int k = 0; k < 3000; k++)
        {
            var at = start + new Vector3((float)(rng.NextDouble() - 0.5) * 3000f, 0f, (float)(rng.NextDouble() - 0.5) * 3000f);
            float h = Ground(at.X, at.Z);
            foreach (var d in new[] { Vector3.Right, Vector3.Left, Vector3.Forward, Vector3.Back })
            {
                float rise = Ground(at.X + d.X * 16f, at.Z + d.Z * 16f) - h;
                if (rise > best.Rise) best = (at with { Y = h }, -d, rise);
            }
        }
        GD.Print($"(cliff: {best.Rise:F0} m over 16 m, {new Vector2(best.At.X - start.X, best.At.Z - start.Z).Length():F0} m from the start)");
        Vector3 face = best.At - best.Away * 8f;
        face.Y = Ground(face.X, face.Z) - best.Rise * 0.4f;
        foreach (float d in Floats("distances", "100,200,300"))
        {
            Vector3 foot = best.At + best.Away * d, eye = foot with { Y = Ground(foot.X, foot.Z) + 10f };
            for (int i = 0; i < 600 && (i < 90 || !s.Settled); i++)
            {
                HoldEye(eye, face);
                yield return 0;
            }
            Snap($"cliff{d:0}");
        }
        _game.FixedCamera = null;
        zone.Sky.TimeScale = 1f;
    }
    /// <summary>The sleeper stands in for the eye (the streaming follows it), the camera at the eye looking at a point.</summary>
    void HoldEye(Vector3 eye, Vector3 at)
    {
        _game.Survivor.PlaceOutside(eye.ToNumerics());
        _game.FixedCamera = new Transform3D(Basis.LookingAt((at - eye).Normalized(), Vector3.Up), eye);
    }
}
