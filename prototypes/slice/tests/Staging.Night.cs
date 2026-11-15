using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;
using Planets.Gen;
using PlanetSurface.Poi;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>
/// Night lights (--view=nightlights), on a fresh world at local midnight:
/// - checks, each an A/B of the same frame with the far lights drawn and hidden:
///   - a lived-in place's points seen from 3 km;
///   - none from a dead ruin;
///   - the glow over a town, and none over a lone farm (no glow is rolled for one);
///   - the frame's cost, with and without them.
/// - shots: from a hill 2 km off a town, from low flight, and from orbit (--orbit=0 skips it).
/// --minspec=1: the minimum spec's stand-in (4 cores: CPUs 0, 2, 4, 6; the lowest graphics). Frames go in --out.
/// Prints NIGHT CHECK lines and a NIGHT RESULT pass and fail count.
/// </summary>
public sealed partial class Staging
{
    int _nightPass, _nightFail;

    void NightCheck(bool ok, string what)
    {
        if (ok) _nightPass++; else _nightFail++;
        GD.Print($"NIGHT CHECK {(ok ? "PASS" : "FAIL")}: {what}");
    }

    IEnumerable<int> NightLightsView()
    {
        // Frames go in --dir (--out is the screenshot scene's own last frame).
        string dir = _args.GetValueOrDefault("dir", "nightlights");
        Directory.CreateDirectory(dir);
        bool minspec = _args.ContainsKey("minspec");
        if (minspec)
        {
            Process.GetCurrentProcess().ProcessorAffinity = 0x55;
            var s = _game.Settings;
            (s.Shadows, s.GroundDetail, s.DetailDistance, s.TreeDistance, s.SpawnDistance, s.ShadowDistance) = (Slice.Game.ShadowLevel.Low, 3, 0f, 0f, 0f, 0f);
            s.Changed();
            GD.Print("(night: the minimum spec's stand-in, 4 cores and the lowest graphics)");
        }
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        _game.Visor.Visible = false;
        var world = _game.Zone.Planet;
        FarLights far = _game.Zone.Pois.Far;
        Vector3 from = _game.Survivor.WorldFeet;
        Vector3 Near(double x, double z) => world.NearestCopy(new Vector3((float)x, 0f, (float)z), from) with { Y = world.GroundHeight(Mathx.WrapX(x, world.Width), Mathx.Wrap(z, world.Height)) };
        Vector3 Middle(PoiSite s) => Near((s.Footprint.Min.X + s.Footprint.Max.X + 1) * 0.5, (s.Footprint.Min.Z + s.Footprint.Max.Z + 1) * 0.5);
        float Off(Vector3 p) => new Vector2(p.X - from.X, p.Z - from.Z).Length();
        GD.Print($"(night: {world.Pois.Count} places, {world.Pois.Count(FarLights.Lived)} lived in, {far.Glows.Count} glows, {far.Beaming.Count} beams)");

        // The town: the brightest glow within reach of the start; the lone farm: a lived-in place no glow covers.
        FarLights.Glow? town = far.Glows.Where(g => Off(Near(g.X, g.Z)) < 9000f).OrderByDescending(g => g.Strength / (1f + Off(Near(g.X, g.Z)) / 3000f)).Cast<FarLights.Glow?>().FirstOrDefault();
        bool Covered(PoiSite s, float margin = 700f) => far.Glows.Any(g => Middle(s).DistanceTo(Near(g.X, g.Z) with { Y = Middle(s).Y }) < g.Radius + margin);
        // (Far enough from every glow that none of them stands behind it in the view.)
        PoiSite farm = world.Pois.Where(s => FarLights.Lived(s) && s.Siting != "city" && !Covered(s, 3000f)).OrderBy(s => Off(Middle(s))).FirstOrDefault();
        // The lit place and the dead ruin from what has been built (places build nearest first, out to the camera's reach), each with lamps to show.
        for (int i = 0; i < 1200 && !(far.Of.Any(o => o.Value.Lived && o.Value.Count > 0) && far.Of.Any(o => !o.Value.Lived && o.Value.Count > 0)); i++) yield return 0;
        PoiSite ruin = far.Of.Where(o => !o.Value.Lived && o.Value.Count > 0 && o.Key.Siting != "city").Select(o => o.Key).OrderBy(s => Off(Middle(s))).FirstOrDefault()
            ?? far.Of.Where(o => !o.Value.Lived && o.Value.Count > 0).Select(o => o.Key).OrderBy(s => Off(Middle(s))).FirstOrDefault();
        PoiSite lived = far.Of.Where(o => o.Value.Lived && o.Value.Count > 0).Select(o => o.Key)
            .OrderBy(s => town is { } t0 ? Middle(s).DistanceTo(Near(t0.X, t0.Z)) : Off(Middle(s))).FirstOrDefault();
        if (lived == null)
        {
            GD.Print("(night: nobody lives on this world, or no lived-in place shows a lamp)");
            NightCheck(far.Glows.Count == 0 && far.PointCount == 0 || far.Of.Values.All(o => !o.Lived), "a world nobody lives on has no glow and no lit place far off");
            foreach (int x in NightResult()) yield return x;
            yield break;
        }

        // A place's points from 3 km, a clear line to it, at midnight: lit pixels with the far lights drawn, against hidden.
        foreach ((PoiSite site, bool want) in new[] { (lived, true), (ruin, false) })
        {
            if (site == null) { GD.Print("(night: no dead ruin on this world)"); continue; }
            Vector3 mid = Middle(site);
            Vector3 eye = Clear(mid, 3000f, 40f);
            foreach (int x in Midnight(eye, mid, () => far.Of.ContainsKey(site))) yield return x;
            if (!far.Of.TryGetValue(site, out var of)) { NightCheck(!want, $"{site.Name}: never built, so no far points (wanted lit: {want})"); continue; }
            var spots = new List<Vector3>();
            for (int i = of.First; i < of.First + of.Count; i++) spots.Add(PointAt(far, i, eye));
            float seen = 0f;
            foreach (int x in AB(eye, mid, far, "Far points", spots, v => seen = v)) yield return x;
            GD.Print($"(night: {site.Name} ({site.State}, {site.Occupancy?.Main.ToString() ?? "nobody"}) from {eye.DistanceTo(mid):F0} m: {of.Count} points, brightest lift {seen:F3})");
            if (want) NightCheck(of.Count > 0 && seen > 0.04f, $"a lived-in place's lights seen from 3 km ({site.Name}: {of.Count} points, lift {seen:F3})");
            else NightCheck(seen < 0.01f, $"no lights from a dead ruin at 3 km ({site.Name}, {site.State}: lift {seen:F3})");
        }

        // The glow over a town, from 2 km on a hill; none over a lone farm.
        if (town is { } g)
        {
            Vector3 mid = Near(g.X, g.Z);
            Vector3 eye = Clear(mid, 2000f + g.Radius * 0.5f, 25f, highest: true);
            foreach (int x in Midnight(eye, mid + Vector3.Up * 40f, () => true)) yield return x;
            float lift = 0f;
            foreach (int x in AB(eye, mid + Vector3.Up * 40f, far, "Light glow", new List<Vector3> { mid + Vector3.Up * g.Radius * 0.25f }, v => lift = v, box: 40)) yield return x;
            NightCheck(lift > 0.02f, $"the glow over a town ({g.Places} places, strength {g.Strength:F2}, radius {g.Radius:F0} m: lift {lift:F3})");
            Look(eye, mid + Vector3.Up * 40f);
            foreach (int x in _drive.Wait(4)) yield return x;
            foreach (int x in Shot(Path.Combine(dir, $"hill-2km{(minspec ? "-minspec" : "")}.png"))) yield return x;
            foreach (int x in Cost(eye, mid + Vector3.Up * 40f, far, minspec)) yield return x;
            // Low flight: 1.5 km off and 150 m up, looking across it.
            Vector3 low = Clear(mid, 1500f + g.Radius * 0.5f, 150f);
            foreach (int x in Midnight(low, mid, () => true, 240)) yield return x;
            foreach (int x in Shot(Path.Combine(dir, "low-flight.png"))) yield return x;
        }
        else GD.Print("(night: no town glows on this world)");
        NightCheck(farm == null || !Covered(farm), $"no glow over a lone farm ({farm?.Name ?? "none on this world"})");
        if (farm != null)
        {
            Vector3 mid = Middle(farm);
            Vector3 eye = Clear(mid, 500f, 250f);
            foreach (int x in Midnight(eye, mid, () => true, 240)) yield return x;
            float lift = 0f;
            foreach (int x in AB(eye, mid, far, "Light glow", new List<Vector3> { mid + Vector3.Up * 60f }, v => lift = v, box: 40)) yield return x;
            NightCheck(lift < 0.005f, $"nothing glows over the farm's air (lift {lift:F3})");
        }
        foreach (int x in NightResult()) yield return x;
        if (_args.GetValueOrDefault("orbit", "1") == "0" || minspec) yield break;
        // From orbit: the night side, the towns as smudges.
        // Back to the start, where the Skiff's blueprint goes down.
        _game.FixedCamera = null;
        _game.Survivor.PlaceOutside(from.ToNumerics());
        foreach (int x in _drive.UntilSettled()) yield return x;
        _game.Zone.Sky.TimeOfDay = MidnightAt();
        foreach (int x in ToOrbit()) yield return x;
        if (!_game.InOrbit) yield break;
        // On up a while, for the world below to fit the view.
        _game.Controls.Hold(Key.Space);
        for (int i = 0; i < 60 * (int)Num("climb", 20); i++) yield return 0;
        _game.Controls.ReleaseAll();
        // The world under the Skiff, its night side: the clock set so the sun is well down over the start, the view narrowed onto it.
        Vector3 centre0 = _game.Orbit.ToWorld(System.Numerics.Vector3.Zero).ToGodot();
        SunOver((Skiff.Body.Position.ToGodot() - centre0).Normalized(), -0.6f);
        float fov = _game.Camera.Fov;
        _game.Camera.Fov = (float)Num("orbitfov", 40);
        for (int i = 0; i < 120; i++)
        {
            Vector3 at = Skiff.Body.Position.ToGodot(), centre = _game.Orbit.ToWorld(System.Numerics.Vector3.Zero).ToGodot();
            Vector3 down = (centre - at).Normalized(), side = down.Cross(Vector3.Right).Normalized();
            Vector3 eye = at + side * 80f - down * 30f;
            _game.FixedCamera = new Transform3D(Basis.LookingAt((centre - eye).Normalized(), side), eye);
            yield return 0;
        }
        GD.Print($"(night: the globe's lights map {(_game.Spaceflight.Orbit?.Paint?.Lights?.SavePng(Path.Combine(dir, "globe-lights.png")).ToString() ?? "none")})");
        foreach (int x in Shot(Path.Combine(dir, "orbit.png"))) yield return x;
        _game.Camera.Fov = fov;
    }

    IEnumerable<int> NightResult()
    {
        GD.Print($"NIGHT RESULT: {_nightPass} passed, {_nightFail} failed");
        yield break;
    }

    /// <summary>A canonical far point wrapped to the copy nearest the eye.</summary>
    Vector3 PointAt(FarLights far, int i, Vector3 eye)
    {
        var mm = ((MultiMeshInstance3D)far.GetNode("Far points")).Multimesh;
        Vector3 p = mm.InstanceCount > i ? mm.GetInstanceTransform(i).Origin : Vector3.Zero;
        return _game.Zone.Planet.NearestCopy(p, eye);
    }

    /// <summary>An eye --off metres from a point, --up over the ground, on the first bearing with a clear line to it (or the highest ground round it).</summary>
    Vector3 Clear(Vector3 mid, float off, float up, bool highest = false)
    {
        var world = _game.Zone.Planet;
        float Ground(Vector3 p) => world.GroundHeight(Mathx.WrapX(p.X, world.Width), Mathx.Wrap(p.Z, world.Height));
        Vector3 best = mid + Vector3.Back * off;
        float bestScore = float.MinValue;
        for (int k = 0; k < 36; k++)
        {
            Vector3 e = mid + new Vector3(MathF.Sin(k * MathF.Tau / 36f), 0f, MathF.Cos(k * MathF.Tau / 36f)) * off;
            e.Y = Ground(e) + up;
            Vector3 target = mid + Vector3.Up * 20f;
            int blocked = 0;
            for (float t = 0.04f; t < 0.92f; t += 0.02f)
            {
                Vector3 p = e.Lerp(target, t);
                if (Ground(p) > p.Y - 3f) blocked++;
            }
            float score = -blocked * 1000f + (highest ? e.Y : 0f);
            if (score > bestScore) (bestScore, best) = (score, e);
        }
        return best;
    }

    /// <summary>The hour it is midnight at a point (the sky is the real one).</summary>
    double MidnightAt()
    {
        var sky = _game.Zone.Sky;
        double low = 0;
        float bottom = 2f;
        for (int k = 0; k < 240; k++)
        {
            float y = sky.SunAt(k / 240.0).Y;
            if (y < bottom) { bottom = y; low = k / 240.0; }
        }
        return low;
    }

    /// <summary>Stands there at midnight and waits for the ground, the places in reach and what <paramref name="ready"/> asks.</summary>
    IEnumerable<int> Midnight(Vector3 eye, Vector3 look, Func<bool> ready, int most = 1800)
    {
        _game.Survivor.PlaceOutside((eye with { Y = _game.Zone.GroundAt(eye.X, eye.Z).Y + 0.3f }).ToNumerics());
        double night = MidnightAt();
        for (int i = 0; i < most; i++)
        {
            _game.FixedCamera = new Transform3D(Basis.LookingAt((look - eye).Normalized(), Vector3.Up), eye);
            if (i % 60 == 2) night = MidnightAt(); // the sky is the real one: midnight where the eye stands now
            _game.Zone.Sky.TimeOfDay = night;
            yield return 0;
            if (i > 120 && _game.Zone.Streamer.Settled && ready()) break;
        }
    }

    /// <summary>How much a far-lights layer lifts a box of pixels round each point, drawn against hidden (luminance, 0 to 1): the brightest pixel, or a wide box's mean.</summary>
    IEnumerable<int> AB(Vector3 eye, Vector3 look, FarLights far, string layer, List<Vector3> at, Action<float> lift, int box = 4)
    {
        var node = (Node3D)far.GetNode(layer);
        Camera3D cam = _game.Camera;
        node.Visible = true;
        for (int i = 0; i < 4; i++) { Look(eye, look); yield return 0; }
        Image on = _game.GetViewport().GetTexture().GetImage();
        node.Visible = false;
        for (int i = 0; i < 4; i++) { Look(eye, look); yield return 0; }
        Image off = _game.GetViewport().GetTexture().GetImage();
        node.Visible = true;
        float most = 0f, sum = 0f;
        int shown = 0, n = 0;
        int w = on.GetWidth(), h = on.GetHeight();
        float scale = w / cam.GetViewport().GetVisibleRect().Size.X;
        foreach (Vector3 p in at)
        {
            if (cam.IsPositionBehind(p)) continue;
            Vector2 s = cam.UnprojectPosition(p) * scale;
            if (s.X >= 0 && s.Y >= 0 && s.X < w && s.Y < h) shown++;
            for (int y = (int)s.Y - box; y <= (int)s.Y + box; y++)
            for (int x = (int)s.X - box; x <= (int)s.X + box; x++)
            {
                if (x < 0 || y < 0 || x >= w || y >= h) continue;
                float d = on.GetPixel(x, y).Luminance - off.GetPixel(x, y).Luminance;
                most = MathF.Max(most, d);
                sum += d;
                n++;
            }
        }
        GD.Print($"(night A/B {layer}: {shown} of {at.Count} on screen, fog {_game.Zone.Sky.Environment.FogDensity:F5}, daylight {_game.Zone.Sky.Daylight:F2})");
        // A point: the brightest pixel it lifts. A glow is smooth and wide: the mean over the box, so a swaying tree is not a glow.
        lift(box >= 20 ? sum / Math.Max(n, 1) : most);
    }

    /// <summary>What the far lights cost over a town at night: the frame with them and without, GPU and main thread, 120 frames each.</summary>
    IEnumerable<int> Cost(Vector3 eye, Vector3 look, FarLights far, bool minspec)
    {
        Rid vp = _game.GetViewport().GetViewportRid();
        RenderingServer.ViewportSetMeasureRenderTime(vp, true);
        var got = new (double Gpu, double Cpu, double Main)[2];
        double own = 0, ownWorst = 0;
        // Alternated, three times each, so the world settling in counts against neither.
        for (int pass = -1; pass < 6; pass++)
        {
            far.Visible = pass % 2 == 0; // pass -1 settles the view in, uncounted
            for (int i = 0; i < 20; i++) { Look(eye, look); yield return 0; }
            double gpu = 0, cpu = 0, main = 0;
            for (int i = 0; i < 60; i++)
            {
                Look(eye, look);
                yield return 0;
                gpu += RenderingServer.ViewportGetMeasuredRenderTimeGpu(vp);
                cpu += RenderingServer.ViewportGetMeasuredRenderTimeCpu(vp);
                main += Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0;
                if (pass >= 0) own += far.LastMs / 360.0;
                if (pass >= 0) ownWorst = Math.Max(ownWorst, far.LastMs);
            }
            GD.Print($"(night cost pass {pass}: far lights {(pass % 2 == 0 ? "on" : "off")}, GPU {gpu / 60:F2}, main {main / 60:F2} ms)");
            if (pass < 0) continue;
            var was = got[pass % 2];
            got[pass % 2] = (was.Gpu + gpu / 180, was.Cpu + cpu / 180, was.Main + main / 180);
        }
        far.Visible = true;
        string tag = minspec ? "minimum spec" : "this machine";
        GD.Print($"(night cost, {tag}: with far lights GPU {got[0].Gpu:F2} ms, render CPU {got[0].Cpu:F2} ms, main {got[0].Main:F2} ms; without GPU {got[1].Gpu:F2}, render CPU {got[1].Cpu:F2}, main {got[1].Main:F2})");
        // The machine's other runs make whole frames noisy: the far lights' own cost is what is judged, their GPU time (A/B) and main-thread time (timed).
        double gpuCost = got[0].Gpu - got[1].Gpu;
        GD.Print($"(night cost, {tag}: far lights' main thread {own:F3} ms a frame, worst {ownWorst:F3})");
        NightCheck(gpuCost < 0.5 && own < 0.2, $"the far lights' cost on {tag}: {gpuCost:+0.00;-0.00} ms GPU, {own:F3} ms main thread (frame with them {Math.Max(got[0].Gpu, got[0].Cpu + got[0].Main):F1} ms, without {Math.Max(got[1].Gpu, got[1].Cpu + got[1].Main):F1})");
    }
}
