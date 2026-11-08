using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;
using Wildlife;
using Rng = Wildlife.Rng;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>
/// --view=farfolk: a town square of people (--count, 48) on open ground at midday, seen from 50 to
/// 300 m: what they cost (frame rate uncapped, GPU and CPU time, draw calls, triangles, posing; and
/// the same view with nobody there), the crowd at 60, 120 and 250 m, and six frames of one of them
/// walking at 80 m. Each person is moved and posed as a town does it (<see cref="Places.Town"/>).
/// --dir=DIR is the folder (--out the last frame).
/// </summary>
public sealed partial class Staging
{
    sealed class Walker
    {
        public HumanRig Rig;
        public PersonView View;
        public Vector3 Pos;
        public float Yaw, Speed, Now, Gait, Saved, Work;
        public Vector3[] Loop;
        public int Leg;
        public BodyAct Act;
    }

    IEnumerable<int> FarFolkView()
    {
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        string dir = _args.GetValueOrDefault("dir", "farfolk");
        Directory.CreateDirectory(dir);
        foreach (int x in _drive.UntilSettled()) yield return x;
        WorldLife life = _game.Zone.Wildlife.Life;
        float eyeHeight = (float)Num("eye", 1.8);
        // Local noon, a little before: the sun high and to one side.
        double noon = 0.5, brightest = -1;
        for (int k = 0; k < 24; k++)
        {
            _game.Zone.Sky.TimeOfDay = k / 24.0;
            yield return 0;
            yield return 0;
            if (_game.Zone.Sky.ToSun.Y > brightest) { brightest = _game.Zone.Sky.ToSun.Y; noon = k / 24.0; }
        }
        _game.Zone.Sky.TimeOfDay = noon - 0.06;
        yield return 0;
        Vector3 sun = _game.Zone.Sky.ToSun;

        // The square: flat dry ground with no tree on it, looked at from where nothing of the land or
        // its trees is in the way out to 300 m, on dry ground, the sun behind. In woods, it looks further
        // afield for a clearing (the survivor goes to each, so the trees round it are known).
        float[] reach = { 50f, 60f, 80f, 100f, 120f, 200f, 250f, 300f };
        Vector3 spot = Ground(Play.FlatSpot(_game.Survivor.WorldFeet, 40f, span: 20f)), away = Vector3.Back;
        Vector3 start = _game.Survivor.WorldFeet;
        Vector3 Eye(Vector3 d, float r)
        {
            Vector3 e = spot + d * r;
            e.Y = _game.Zone.GroundAt(e.X, e.Z).Y + eyeHeight;
            return e;
        }
        bool Dry(Vector3 e) => !_game.Zone.Water.LiquidAt(e.X, e.Z, out _);
        int clearest = -1;
        Vector3 bestSpot = spot, bestAway = away;
        for (int attempt = 0; attempt < 8 && clearest < reach.Length; attempt++)
        {
            Vector3 c = Ground(Play.FlatSpot(start, 40f + 220f * attempt, startAngle: attempt * 2.1f, span: 20f));
            _game.Survivor.PlaceOutside(Ground(c + Vector3.Back * 3f).ToNumerics());
            foreach (int x in _drive.Wait(20)) yield return x;
            foreach (int x in _drive.UntilSettled()) yield return x;
            if (_game.Zone.Water.LiquidAt(c.X, c.Z, out _) || _game.Places?.PlaceAt(c) != null || _game.Zone.Streamer.SolidsNear(c, 15f).Any()) continue;
            spot = c;
            float best = float.MinValue;
            for (int a = 0; a < 48; a++)
            {
                var d = new Vector3(MathF.Cos(a * MathF.Tau / 48f), 0f, MathF.Sin(a * MathF.Tau / 48f));
                int clear = reach.Count(r => Dry(Eye(d, r)) && Clearance(Eye(d, r), spot + Vector3.Up * 1.2f) > 0.3f);
                // Over the people's heads rather than just past them: the further ones not sunk behind a rise.
                float margin = reach.Min(r => MathF.Min(2.5f, Clearance(Eye(d, r), spot + Vector3.Up * 0.3f)));
                float score = clear * 10f + margin * 3f + new Vector3(sun.X, 0f, sun.Z).Normalized().Dot(d) * 2f;
                if (score > best) { best = score; away = d; }
            }
            int open = reach.Count(r => Dry(Eye(away, r)) && Clearance(Eye(away, r), spot + Vector3.Up * 1.2f) > 0.3f);
            GD.Print($"(a square at {spot}: clear from {open} of {reach.Length} distances)");
            if (open > clearest) { clearest = open; bestSpot = spot; bestAway = away; }
        }
        spot = bestSpot;
        away = bestAway;
        _game.Survivor.PlaceOutside(Ground(spot + Vector3.Back * 3f).ToNumerics());
        foreach (int x in _drive.Wait(20)) yield return x;
        foreach (int x in _drive.UntilSettled()) yield return x;
        GD.Print($"(square at {spot}, looked at from {MathF.Atan2(away.X, away.Z) * 180f / MathF.PI:F0} degrees, sight clear at {clearest} of {reach.Length} distances; folk {life.People.Folk.Id})");

        // The square: people walking its round at their own pace, some standing, a few waving, two at work.
        var stage = new Node3D { Name = "Square" };
        _game.Zone.AddChild(stage);
        int count = (int)Num("count", 48);
        var rng = new Random();
        ulong salt = (ulong)rng.NextInt64();
        var roles = new[] { PersonRole.Trader, PersonRole.Guard, PersonRole.Farmer, PersonRole.Medic, PersonRole.Scavenger, PersonRole.Elder, PersonRole.Child, PersonRole.Drunk, PersonRole.Worker };
        var folk = new List<Walker>();
        Vector3 side = new Vector3(-away.Z, 0f, away.X);
        for (int i = 0; i < count; i++)
        {
            Species s = life.People.Person(Rng.Mix(salt, (ulong)i), roles[i % roles.Length]);
            var f = new Walker { Rig = new HumanRig(s), Yaw = (float)rng.NextDouble() * MathF.Tau };
            Figure fig = s.Figure;
            f.Speed = fig.Stage == LifeStage.Child ? 2.6f : fig.Stage == LifeStage.Elder || fig.Role == PersonRole.Drunk ? 0.9f : i % 7 == 0 ? 3.0f : 1.1f + (float)rng.NextDouble() * 0.5f;
            if (i == 0)
            {
                // The one the walking frames follow: across the view and back, off to one side of the rest.
                f.Loop = new[] { spot + side * 15f + away * 6f, spot + side * 27f + away * 6f };
                f.Speed = 1.35f;
            }
            else if (i % 4 != 3)
            {
                float hx = 3f + (float)rng.NextDouble() * 8f, hz = 3f + (float)rng.NextDouble() * 8f;
                Vector3 c = spot + new Vector3((float)rng.NextDouble() * 4f - 2f, 0f, (float)rng.NextDouble() * 4f - 2f);
                var corners = new[] { c + new Vector3(-hx, 0f, -hz), c + new Vector3(hx, 0f, -hz), c + new Vector3(hx, 0f, hz), c + new Vector3(-hx, 0f, hz) };
                if (i % 2 == 0) Array.Reverse(corners);
                f.Loop = corners;
                f.Leg = i % 4;
            }
            else
            {
                f.Act = i % 8 == 3 ? BodyAct.Greet : BodyAct.None;
                f.Work = i % 12 == 7 ? 1f : 0f;
            }
            float t0 = (float)rng.NextDouble();
            f.Pos = f.Loop != null ? f.Loop[f.Leg].Lerp(f.Loop[(f.Leg + 1) % f.Loop.Length], t0) : spot + new Vector3((float)rng.NextDouble() * 20f - 10f, 0f, (float)rng.NextDouble() * 20f - 10f);
            f.Pos = Ground(f.Pos);
            f.View = new PersonView { Name = $"Folk{i}" };
            stage.AddChild(f.View);
            f.View.Build(f.Rig, PersonView.Prepare(f.Rig));
            folk.Add(f);
        }

        _game.Visor.Visible = false;
        Vector3 eye = default;
        int tick = 0;
        float clock = 0f;
        double posing = 0;
        Vector3 EyeAt(float r) => Eye(away, r);
        // The survivor stands just behind the camera, as in play.
        IEnumerable<int> StandAt(float r, int settle)
        {
            eye = EyeAt(r);
            _game.Survivor.PlaceOutside(Ground(eye + away * 2.5f).ToNumerics());
            Look(eye, spot + Vector3.Up * 1f);
            for (int k = 0; k < settle; k++) { Step(); yield return 0; }
        }
        void Step()
        {
            long from = System.Diagnostics.Stopwatch.GetTimestamp();
            const float dt = 1f / 60f;
            clock += dt;
            tick++;
            for (int i = 0; i < folk.Count; i++)
            {
                Walker f = folk[i];
                f.Saved += dt;
                float d = new Vector2(f.Pos.X - eye.X, f.Pos.Z - eye.Z).Length();
                if (!Due(f, i, d, tick)) continue;
                float step = f.Saved;
                f.Saved = 0f;
                Walk(f, step);
                Show(f, i, d, step, clock);
            }
            posing += System.Diagnostics.Stopwatch.GetElapsedTime(from).TotalMilliseconds;
        }

        // The crowd at 60, 120 and 250 m, each with an enlarged crop of the square.
        Camera3D cam = _game.Camera;
        foreach (float r in new[] { 60f, 120f, 250f })
        {
            foreach (int x in StandAt(r, 120)) yield return x;
            int seen = folk.Count(f => cam.IsPositionInFrustum(f.Pos + Vector3.Up) && Clearance(eye, f.Pos + Vector3.Up * 1.5f) > 0f);
            string file = Path.Combine(dir, $"crowd-{r:F0}m.png");
            foreach (int x in Shot(file)) yield return x;
            Vector2 c = cam.UnprojectPosition(spot + Vector3.Up * 1f);
            int w = r < 100f ? 420 : r < 200f ? 220 : 110, h = r < 100f ? 160 : r < 200f ? 80 : 44;
            Crop(file, Path.Combine(dir, $"crowd-{r:F0}m-crop.png"), c, w, h, r < 100f ? 3 : r < 200f ? 5 : 9);
            GD.Print($"(crowd at {r:F0} m: {seen} of {folk.Count} in view)");
        }

        // One of them walking, at 80 m: six frames an eighth of a second apart, enlarged.
        Walker walker = folk[0];
        foreach (int x in StandAt(84f, 90)) yield return x;
        var strip = new List<string>();
        for (int k = 0; k < 6; k++)
        {
            for (int j = 0; j < 8; j++) { Step(); yield return 0; }
            string file = Path.Combine(dir, $"walk-{k}.png");
            foreach (int x in Shot(file)) yield return x;
            strip.Add(file);
            Crop(file, file.Replace(".png", "-crop.png"), cam.UnprojectPosition(walker.Pos + Vector3.Up * 0.9f), 36, 28, 7);
        }
        Strip(strip.Select(s => s.Replace(".png", "-crop.png")).ToList(), Path.Combine(dir, "walk-80m-strip.png"));
        GD.Print($"(walker {walker.Rig.Species.Figure.Role} at {walker.Pos.DistanceTo(eye):F0} m, {walker.Now:F2} m/s)");

        // What it costs, from 50 to 300 m: the crowd, then the same view with nobody there.
        var lines = new List<string>();
        Rid vp = _game.GetViewport().GetViewportRid();
        RenderingServer.ViewportSetMeasureRenderTime(vp, true);
        int cap = Engine.MaxFps;
        DisplayServer.VSyncMode vsync = DisplayServer.WindowGetVsyncMode();
        Engine.MaxFps = 0;
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        foreach (float r in new[] { 50f, 100f, 200f, 300f })
        foreach (bool shown in new[] { true, false })
        {
            stage.Visible = shown;
            foreach (int x in StandAt(r, 90)) yield return x;
            int seen = folk.Count(f => cam.IsPositionInFrustum(f.Pos + Vector3.Up) && Clearance(eye, f.Pos + Vector3.Up * 1.5f) > 0f);
            double cpu = 0, gpu = 0, worstFrame = 0;
            long draws = 0, prims = 0;
            posing = 0;
            const int ticks = 300;
            ulong drawn0 = (ulong)Engine.GetFramesDrawn(), us0 = Time.GetTicksUsec(), last = us0;
            ulong lastFrames = drawn0;
            for (int k = 0; k < ticks; k++)
            {
                if (shown) Step();
                yield return 0;
                cpu += RenderingServer.ViewportGetMeasuredRenderTimeCpu(vp) + RenderingServer.GetFrameSetupTimeCpu();
                gpu += RenderingServer.ViewportGetMeasuredRenderTimeGpu(vp);
                draws += (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame);
                prims += (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame);
                ulong now = Time.GetTicksUsec(), frames = (ulong)Engine.GetFramesDrawn();
                if (frames > lastFrames) { worstFrame = Math.Max(worstFrame, (now - last) / 1000.0 / (frames - lastFrames)); last = now; lastFrames = frames; }
            }
            double seconds = (Time.GetTicksUsec() - us0) / 1e6;
            double fps = ((ulong)Engine.GetFramesDrawn() - drawn0) / seconds;
            string line = $"COST {(shown ? "crowd" : "empty")} at {r:F0} m ({seen} of {folk.Count} in view): {fps:F0} fps, worst frame {worstFrame:F1} ms, gpu {gpu / ticks:F2} ms, render cpu {cpu / ticks:F2} ms, draw calls {draws / ticks}, triangles {prims / ticks}, posing {posing / ticks:F3} ms a tick";
            GD.Print(line);
            lines.Add(line);
        }
        stage.Visible = true;
        Engine.MaxFps = cap;
        DisplayServer.WindowSetVsyncMode(vsync);
        lines.Add($"(window {DisplayServer.WindowGetSize()}, {RenderingServer.GetVideoAdapterName()}, {OS.GetProcessorName()} x{OS.GetProcessorCount()})");
        File.WriteAllLines(Path.Combine(dir, "farfolk-cost.txt"), lines);
    }

    /// <summary>How far the line between two points clears the land under it and the trees standing near it (a crown round each trunk), m: below zero, something is in the way.</summary>
    float Clearance(Vector3 eye, Vector3 at)
    {
        float worst = float.MaxValue;
        int n = Math.Max(4, (int)(eye.DistanceTo(at) / 3f));
        for (int k = 1; k < n; k++)
        {
            Vector3 p = eye.Lerp(at, k / (float)n);
            worst = MathF.Min(worst, p.Y - _game.Zone.GroundAt(p.X, p.Z).Y);
            if (k % 2 == 1) continue;
            foreach (var (solid, offset) in _game.Zone.Streamer.SolidsNear(p, 6f))
                worst = MathF.Min(worst, p.Y - (solid.Center.Y + offset.Y + solid.Extents.Y * 1.4f + 1f));
        }
        return worst;
    }

    /// <summary>Round the square's corners at their pace, turning as a town's people do.</summary>
    void Walk(Walker f, float dt)
    {
        if (f.Loop == null) { f.Now = 0f; return; }
        Vector3 to = f.Loop[f.Leg];
        Vector3 d = to - f.Pos;
        d.Y = 0f;
        float flat = d.Length();
        if (flat < 0.4f) { f.Leg = (f.Leg + 1) % f.Loop.Length; return; }
        f.Now = Mathf.MoveToward(f.Now, f.Speed, 1.6f * dt);
        float step = MathF.Min(flat, f.Now * dt);
        f.Gait += step;
        f.Pos += d / flat * step;
        f.Pos.Y = _game.Zone.GroundAt(f.Pos.X, f.Pos.Z).Y;
        f.Yaw = Mathf.RotateToward(f.Yaw, MathF.Atan2(-d.X, -d.Z), 5f * dt);
    }

    /// <summary>Whether a person moves and is posed this tick, as a town decides it: the further off, the fewer times a second.</summary>
    static bool Due(Walker f, int i, float distance, int tick) => (tick + i) % PersonView.PoseEvery(distance) == 0;

    /// <summary>Draws them where they are now, as a town does: their own body at any distance, faded at the far edge.</summary>
    static void Show(Walker f, int i, float distance, float dt, float clock)
    {
        bool shows = f.View.Fade(distance, dt);
        if (f.View.Visible != shows) f.View.Visible = shows;
        if (!shows) return;
        f.View.SyncFar(f.Pos, f.Yaw);
        f.Rig.Drive(new Gesture
        {
            Feet = f.Pos, Yaw = f.Yaw, Speed = f.Now, Walked = f.Gait, Work = f.Work,
            Act = f.Act, ActAmount = f.Act == BodyAct.None ? 0f : 1f, Clock = clock + i * 1.7f,
        });
        f.View.Sync();
    }

    /// <summary>A piece of a saved frame round a point on the screen, enlarged so its pixels show.</summary>
    static void Crop(string file, string to, Vector2 centre, int w, int h, int scale)
    {
        Image img = Image.LoadFromFile(file);
        if (img == null) return;
        img.Convert(Image.Format.Rgb8);
        var r = new Rect2I((int)centre.X - w / 2, (int)centre.Y - h / 2, w, h);
        r.Position = new Vector2I(Math.Clamp(r.Position.X, 0, img.GetWidth() - w), Math.Clamp(r.Position.Y, 0, img.GetHeight() - h));
        Image part = img.GetRegion(r);
        part.Resize(w * scale, h * scale, Image.Interpolation.Nearest);
        part.SavePng(to);
    }

    /// <summary>Crops side by side, a pixel of grey between them.</summary>
    static void Strip(List<string> files, string to)
    {
        var parts = files.Select(Image.LoadFromFile).Where(i => i != null).ToList();
        if (parts.Count == 0) return;
        foreach (Image p in parts) p.Convert(Image.Format.Rgb8);
        int w = parts[0].GetWidth(), h = parts[0].GetHeight();
        Image strip = Image.CreateEmpty(w * parts.Count + 4 * (parts.Count - 1), h, false, Image.Format.Rgb8);
        strip.Fill(new Color(0.5f, 0.5f, 0.5f));
        for (int k = 0; k < parts.Count; k++) strip.BlitRect(parts[k], new Rect2I(0, 0, w, h), new Vector2I(k * (w + 4), 0));
        strip.SavePng(to);
    }
}
