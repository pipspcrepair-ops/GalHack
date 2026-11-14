using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;
using PlanetSurface.Fx;
using PlanetSurface.Terrain;
using Slice.Game;

namespace Slice.Tests;

/// <summary>
/// How far ahead the ground and the trees are in, on one world: walking, at a hover bike's speed and flying at a
/// craft's top speed, each from the start toward the highest ground within 4 km (mountains, through what grows on the way).
/// Run windowed: godot --path prototypes/slice res://tests/PopRoute.tscn [-- --seed=N --places-at-load=1]
/// Per route: the near ground's complete radius (median, worst), the far rings' nearest chunk still short of its level,
/// the streamer's queue, how long a column waited from coming into the ring to its first near chunk, how long a
/// level-0 far chunk (the trees' near drawing) waited from being wanted to standing, and how far off trees appeared.
/// </summary>
public partial class PopRoute : Node
{
    Game.Game _game;
    Driver _drive;
    IEnumerator<int> _run;
    readonly Dictionary<string, string> _args = new();
    readonly List<double> _frames = new(), _gpu = new();
    /// <summary>The streamer's own main-thread time on this route so far, ms.</summary>
    double _streaming;
    long _last;
    bool _failed;

    public override void _Ready()
    {
        new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(20));
            Console.Error.WriteLine("PopRoute took over 20 minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true }.Start();
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            int eq = arg.IndexOf('=');
            if (arg.StartsWith("--") && eq > 2) _args[arg[2..eq]] = arg[(eq + 1)..];
        }
        // As the owner plays: his settings file (--settings=path), vsync on, full screen at 1920 x 1080.
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Enabled);
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
        // (Set by reflection: the flag is only there from places built after load on.)
        if (_args.ContainsKey("places-at-load"))
            typeof(PlanetSurface.World.PlanetWorld).GetField("BuildPlacesAtLoad")?.SetValue(null, true);
        ProcessPhysicsPriority = 100;
        _game = new Game.Game { Interactive = false, SkipIntro = true, SettingsPath = _args.GetValueOrDefault("settings"), World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Tested) };
        AddChild(_game);
        _drive = new Driver(_game);
        _run = Routes().GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        bool more;
        try { more = _run.MoveNext(); }
        catch (Exception e)
        {
            GD.PrintErr($"FAIL route: {e}");
            _failed = true;
            more = false;
        }
        if (more) return;
        SetPhysicsProcess(false);
        if (_args.ContainsKey("check")) GD.Print(_gateFails == 0 && !_failed ? "ALL PASSED (the moving picture is within its limits)" : $"{_gateFails} CHECK(S) FAILED");
        Wildlife.Shutdown.Quit(this, _failed || _gateFails > 0 ? 1 : 0);
    }

    public override void _Process(double delta)
    {
        long now = Stopwatch.GetTimestamp();
        if (_last != 0) _frames.Add((now - _last) * 1000.0 / Stopwatch.Frequency);
        _gpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid()));
        if (_game?.Zone?.Streamer is { World: not null } streamer) _streaming += streamer.LastFrameMs;
        _last = now;
    }

    IEnumerable<int> Routes()
    {
        var load = Stopwatch.StartNew();
        foreach (int x in _drive.UntilPlaying(300)) yield return x;
        var zone = _game.Zone;
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
        DirectionalLight3D sun = zone.Sky.Sun;
        GD.Print($"ROUTE applied: window {DisplayServer.WindowGetSize()} {DisplayServer.WindowGetMode()}, vsync {DisplayServer.WindowGetVsyncMode()}, fps cap {Engine.MaxFps}, render scale {GetViewport().Scaling3DScale}, fov {_game.Camera.Fov}, " +
                 $"ground detail {zone.Streamer.VoxelRadius}, detail distance {DetailReach.Distance}, level-0 radius {TerrainStreamer.FarLod0Radius}, far radius {TerrainStreamer.FarRadius}, " +
                 $"sun shadows {sun.DirectionalShadowMaxDistance} m, splits {sun.DirectionalShadowSplit1 * sun.DirectionalShadowMaxDistance:F0}/{sun.DirectionalShadowSplit2 * sun.DirectionalShadowMaxDistance:F0}/{sun.DirectionalShadowSplit3 * sun.DirectionalShadowMaxDistance:F0}, normal bias {sun.ShadowNormalBias}, bias {sun.ShadowBias}, blend splits {sun.DirectionalShadowBlendSplits}, mode {sun.DirectionalShadowMode}");
        GD.Print($"ROUTE world {_game.World}; load {load.Elapsed.TotalSeconds:F1} s; places at load: {_args.ContainsKey("places-at-load")}");
        zone.Weather.Bridge.Calm = true;
        zone.Weather.Sim.Snap(Weather.Model.WeatherKind.Clear);
        zone.Wildlife.Frozen = true;
        Vector3 start = _game.Survivor.WorldFeet;
        // The way to the highest ground within 4 km.
        Vector3 way = Vector3.Forward;
        float best = float.MinValue;
        for (int k = 0; k < 24; k++)
        {
            var d = new Vector3(MathF.Cos(k * MathF.Tau / 24), 0f, MathF.Sin(k * MathF.Tau / 24));
            float top = Enumerable.Range(1, 8).Max(i => zone.GroundAt(start.X + d.X * 500f * i, start.Z + d.Z * 500f * i).Y);
            if (top > best) (best, way) = (top, d);
        }
        GD.Print($"ROUTE toward {way} (ground up to {best - zone.GroundAt(start.X, start.Z).Y:F0} m above the start)");
        _planted0 = (zone.Planet.Palette.Planted as System.Collections.ICollection)?.Count;
        if (_args.ContainsKey("flicker-only"))
        {
            foreach (int x in Flicker(start)) yield return x;
            yield break;
        }
        PopLog.On = true;
        // --only=a,b runs just those routes: once (flying straight off), walk, bike, fly, flicker.
        string[] only = _args.TryGetValue("only", out string o) ? o.Split(',') : null;
        // First straight off, as a player does the moment the world shows (whatever still loads then loads under it); then each from a settled start.
        if (only == null || only.Contains("once"))
            foreach (int x in Route("fly at once", start, -way, 70f, 4000f, 60f)) yield return x;
        foreach (var (name, speed, length, up) in new[] { ("walk", 6f, 300f, 0.3f), ("bike", 26f, 1500f, 1f), ("fly", 70f, 4000f, 60f) })
        {
            if (only != null && !only.Contains(name)) continue;
            _game.Survivor.PlaceOutside(Lift(start, up).ToNumerics());
            for (int i = 0; i < 60; i++) yield return 0;
            foreach (int x in _drive.UntilSettled(40)) yield return x;
            for (int i = 0; i < 120; i++) yield return 0;
            foreach (int x in Route(name, start, way, speed, length, up)) yield return x;
        }
        PopLog.On = false;
        if (only == null || only.Contains("flicker"))
            foreach (int x in Flicker(start)) yield return x;
        GD.Print($"ROUTE ruin trees planted: {_planted0} when the world showed, {(zone.Planet.Palette.Planted as System.Collections.ICollection)?.Count} at the end");
    }

    /// <summary>
    /// Shadow flicker at a low sun, 115 m over the densest trees near the start, the sun moving at the game's own pace:
    /// hovering still, and flying straight and level at 70 m/s. Counted as the share of the lower half's pixels that
    /// change from one frame to the next, with the sun's shadows on and then off on the same path: what the shadows add
    /// is their flicker. --bias=X sets the sun's normal bias.
    /// </summary>
    IEnumerable<int> Flicker(Vector3 start)
    {
        var zone = _game.Zone;
        TerrainStreamer s = zone.Streamer;
        DirectionalLight3D sun = zone.Sky.Sun;
        _game.Survivor.PlaceOutside(Lift(start, 115f).ToNumerics());
        foreach (int x in _drive.Wait(60)) yield return x;
        foreach (int x in _drive.UntilSettled(40)) yield return x;
        foreach (int x in _drive.Wait(120)) yield return x;
        Vector3 over = start;
        int most = -1;
        for (int i = -4; i <= 4; i++)
            for (int k = -4; k <= 4; k++)
            {
                var at = start + new Vector3(i * 60f, 0f, k * 60f);
                int n = s.FloraNear(at, 30f).Count();
                if (n > most) (most, over) = (n, at);
            }
        foreach (float want in new[] { 5f, 10f })
        {
            zone.Sky.TimeScale = 0f;
            for (double t = 0.15; t < 0.5; t += 0.0025)
            {
                zone.Sky.TimeOfDay = t;
                yield return 0;
                if (MathF.Asin(sun.GlobalTransform.Basis.Z.Y) * 180f / MathF.PI >= want) break;
            }
            double from = zone.Sky.TimeOfDay;
            if (_args.TryGetValue("bias", out string b)) sun.ShadowNormalBias = float.Parse(b, CultureInfo.InvariantCulture);
            Vector3 toSun = new Vector3(sun.GlobalTransform.Basis.Z.X, 0f, sun.GlobalTransform.Basis.Z.Z).Normalized();
            foreach (bool flying in new[] { false, true })
            {
                var share = new double[2];
                for (int pass = 0; pass < 2; pass++)
                {
                    sun.ShadowEnabled = pass == 0;
                    zone.Sky.TimeOfDay = from;
                    zone.Sky.TimeScale = 1f;
                    // Flying across the sun's way (shadows stretch sideways), looking ahead and down at the trees.
                    Vector3 across = toSun.Cross(Vector3.Up), a = over - across * 150f;
                    Image last = null;
                    double changed = 0;
                    int frames = 0;
                    for (int i = 0; i < 150; i++)
                    {
                        Vector3 eye = Lift(flying ? a + across * (70f / Engine.PhysicsTicksPerSecond * i) : over, 115f);
                        _game.Survivor.PlaceOutside(eye.ToNumerics());
                        _drive.Face(eye + across * 160f + Vector3.Down * 115f);
                        yield return 0;
                        if (i < 100) continue;
                        Image img = GetViewport().GetTexture().GetImage();
                        img.Resize(480, 270, Image.Interpolation.Nearest);
                        if (last != null)
                        {
                            int diff = 0, total = 0;
                            for (int y = 135; y < 270; y++)
                                for (int x = 0; x < 480; x++)
                                {
                                    total++;
                                    if (MathF.Abs(img.GetPixel(x, y).Luminance - last.GetPixel(x, y).Luminance) > 0.06f) diff++;
                                }
                            changed += (double)diff / total;
                            frames++;
                        }
                        last = img;
                    }
                    share[pass] = changed / Math.Max(1, frames) * 100.0;
                }
                _drive.Controls.Look = Vector2.Zero;
                sun.ShadowEnabled = true;
                Gate($"flicker, sun {want:F0} degrees, {(flying ? "flying" : "hovering")}: the shadows' own pixels changing a frame", share[0] - share[1], 0.1, false, "%");
                GD.Print(string.Create(CultureInfo.InvariantCulture, $"ROUTE flicker sun {want:F0} deg, {(flying ? "flying level 70 m/s" : "hovering")} 115 m over {most} plants, normal bias {sun.ShadowNormalBias}: pixels changing a frame {share[0]:F2}% with shadows, {share[1]:F2}% without, the shadows' own {share[0] - share[1]:F2}%"));
            }
        }
        zone.Sky.TimeScale = 1f;
    }
    int? _planted0;

    Vector3 Lift(Vector3 p, float up) => p with { Y = _game.Zone.GroundAt(p.X, p.Z).Y + up };

    IEnumerable<int> Route(string name, Vector3 start, Vector3 way, float speed, float length, float up)
    {
        TerrainStreamer s = _game.Zone.Streamer;
        const int S = 16;
        var complete = new List<float>();
        var farShort = new List<int>();
        var pending = new List<int>();
        var farBehind = new List<int>();
        var columnWait = new List<double>();
        var farWait = new List<double>();
        var clock = Stopwatch.StartNew();
        // A column's wait: from first in the near ring round the focus to its first chunk applied.
        var ringAt = new Dictionary<(int, int), double>();
        var applied = new HashSet<(int, int)>();
        Action<TerrainDigging.Terrain.Int3> onChunk = key =>
        {
            var col = (key.X, key.Z);
            if (applied.Add(col) && ringAt.TryGetValue(col, out double t)) columnWait.Add(clock.Elapsed.TotalMilliseconds - t);
        };
        s.ChunkApplied += onChunk;
        // A level-0 far chunk's wait: from first within the level-0 radius of the focus's far chunk to its node standing at level 0.
        var farWanted = new Dictionary<(int, int), double>();
        var seenNodes = new HashSet<ulong>();
        foreach (Node n in s.GetChildren()) seenNodes.Add(n.GetInstanceId());
        var farSwaps = new List<(int Lod, float Away)>();
        var groundSwaps = new List<double>();
        int ring = -1;
        var farIn = new HashSet<(int, int)>();
        int size = FarBuilder.Size;
        PopLog.Take();
        _frames.Clear();
        _gpu.Clear();
        _streaming = 0;
        double solids0 = s.SolidsTotalMs, physics = 0, physicsWorst = 0;
        // The workers' time on the far ground: far chunks (flora and water) and the GPU rings' tiles.
        Array.Clear(FarBuilder.ProfTicks); // PROFILE (temporary)
        double farMs0 = s.FarBuildMs.Sum();
        long tileTicks0 = ClipGround.FillTicks;
        int tiles0 = ClipGround.Filled;
        float along = 0f;
        int tick = 0;
        while (along < length)
        {
            along += speed / Engine.PhysicsTicksPerSecond;
            Vector3 p = Lift(start + way * along, up);
            _game.Survivor.PlaceOutside(p.ToNumerics());
            _drive.Face(Lift(start + way * (along + 200f), up * 0.5f + 1.6f));
            yield return 0;
            double ph = Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0;
            physics += ph;
            physicsWorst = Math.Max(physicsWorst, ph);
            if (++tick % 3 != 0) continue;
            complete.Add((s.CompleteRadius + 0.5f) * S);
            farShort.Add(s.FarBehindNearest);
            pending.Add(s.Pending);
            farBehind.Add(s.FarBehind);
            int fcx = (int)MathF.Floor(s.Focus.X / S), fcz = (int)MathF.Floor(s.Focus.Z / S);
            for (int dz = -s.VoxelRadius; dz <= s.VoxelRadius; dz++)
                for (int dx = -s.VoxelRadius; dx <= s.VoxelRadius; dx++)
                    ringAt.TryAdd((fcx + dx, fcz + dz), clock.Elapsed.TotalMilliseconds);
            int w = s.World.Terrain.Width, h = s.World.Terrain.Height;
            int nx = FarBuilder.Count(w), nz = FarBuilder.Count(h);
            int ffx = (int)Math.Floor(Mod(s.Focus.X, w) / size), ffz = (int)Math.Floor(Mod(s.Focus.Z, h) / size);
            for (int dz = -TerrainStreamer.FarLod0Radius; dz <= TerrainStreamer.FarLod0Radius; dz++)
                for (int dx = -TerrainStreamer.FarLod0Radius; dx <= TerrainStreamer.FarLod0Radius; dx++)
                    farWanted.TryAdd((Mod(ffx + dx, nx), Mod(ffz + dz, nz)), clock.Elapsed.TotalMilliseconds);
            Camera3D cam = _game.Camera;
            if (s.CompleteRadius != ring)
            {
                // The near ground taking over (or giving back) a whole ring of columns: in view in front at that distance.
                if (ring >= 0) groundSwaps.Add(((s.CompleteRadius > ring ? s.CompleteRadius : ring) + 0.5f) * S);
                ring = s.CompleteRadius;
            }
            foreach (Node n in s.GetChildren())
            {
                if (n is not Node3D n3 || !n3.Name.ToString().StartsWith("Far ") || !seenNodes.Add(n3.GetInstanceId())) continue;
                string[] parts = n3.Name.ToString().Split(' ');
                var key = (int.Parse(parts[1]), int.Parse(parts[2]));
                int lod = parts[3][^1] - '0';
                if (lod == 0 && farIn.Add(key) && farWanted.TryGetValue(key, out double t)) farWait.Add(clock.Elapsed.TotalMilliseconds - t);
                // A far chunk's new drawing (its trees and what they cast; the ground is the GPU rings', never redrawn): in view if any of its square is.
                Vector3 c0 = n3.GlobalPosition, mid = c0 + new Vector3(size * 0.5f, 0f, size * 0.5f);
                mid.Y = _game.Zone.GroundAt(mid.X, mid.Z).Y;
                bool inView = cam != null && Enumerable.Range(0, 9).Any(k => cam.IsPositionInFrustum(c0 + new Vector3(size * (k % 3) * 0.5f, mid.Y - c0.Y, size * (k / 3) * 0.5f)));
                if (inView && tick > 30) farSwaps.Add((lod, new Vector2(mid.X - p.X, mid.Z - p.Z).Length()));
            }
        }
        s.ChunkApplied -= onChunk;
        double seconds = clock.Elapsed.TotalSeconds;
        string workers = string.Create(CultureInfo.InvariantCulture, $"main thread {_streaming / seconds:F0} ms a second; workers: far chunks {(s.FarBuildMs.Sum() - farMs0) / seconds:F0} ms a second, ground tiles {(ClipGround.FillTicks - tileTicks0) * 1000.0 / Stopwatch.Frequency / seconds:F0} ms a second ({(ClipGround.Filled - tiles0) / seconds:F0} a second)");
        GD.Print("PROFILE " + name + ": " + string.Join(", ", new[] { "slides", "place", "unfold", "relief", "colours", "normals", "heights", "all" }.Select((n, k) => $"{n} {FarBuilder.ProfTicks[k] * 1000.0 / Stopwatch.Frequency / seconds:F0}"))); // PROFILE (temporary)
        Array.Clear(FarBuilder.ProfTicks);
        var pops = PopLog.Take();
        var trees = pops.Where(q => q.Kind == "flora" && q.What == "appears" && q.Size > 3f).Select(q => q.Distance).ToList();
        string byKind = string.Join(", ", pops.GroupBy(q => q.Kind + " " + q.What).OrderBy(g => g.Key).Select(g => string.Create(CultureInfo.InvariantCulture, $"{g.Key} {g.Count()} ({g.Sum(q => q.Jump * q.Size / MathF.Max(q.Distance, 10f)):F1})")));
        int nearJumps = pops.Count(q => q.Kind is "flora" or "cover" && q.Distance < 120f && q.Jump > 0.05f);
        static string Q(List<double> v, bool low = false)
        {
            if (v.Count == 0) return "none";
            v.Sort();
            double worst = low ? v[(int)(v.Count * 0.05)] : v[Math.Min(v.Count - 1, (int)(v.Count * 0.95))];
            return string.Create(CultureInfo.InvariantCulture, $"median {v[v.Count / 2]:F0}, {(low ? "5th pct" : "95th pct")} {worst:F0}, {(low ? "least" : "most")} {(low ? v[0] : v[^1]):F0} (n {v.Count})");
        }
        double minutes = clock.Elapsed.TotalMinutes;
        int over = _frames.Count(t => t > 16.7);
        _frames.Sort();
        _gpu.Sort();
        string swaps = string.Join(", ", Enumerable.Range(0, 3).Select(l => { var d = farSwaps.Where(q => q.Lod == l).Select(q => (double)q.Away).ToList(); return $"level {l} {d.Count / minutes:F0}/min at m {Q(d, true)}"; }));
        GD.Print($"ROUTE {name} {speed:F0} m/s {length:F0} m: near ground complete to m {Q(complete.Select(c => (double)c).ToList(), true)}; " +
                 $"nearest far chunk short of its level (chunks) {Q(farShort.Where(f => f < int.MaxValue).Select(f => (double)f).ToList(), true)}, at 0 in {farShort.Count(f => f == 0) * 100 / Math.Max(1, farShort.Count)}% of samples; " +
                 $"queue {Q(pending.Select(c => (double)c).ToList())}; far chunks short of their level {Q(farBehind.Select(c => (double)c).ToList())}; column wait ms {Q(columnWait)}; level-0 far chunk wait ms {Q(farWait)}; " +
                 $"trees appearing at m {Q(trees.Select(t => (double)t).ToList(), true)}, within 150 m {trees.Count(t => t < 150f)}; flora or cover jumps over 5% within 120 m {nearJumps}; " +
                 $"main thread: solid flora {(s.SolidsTotalMs - solids0) / Math.Max(1, tick):F2} ms a tick, physics {physics / Math.Max(1, tick):F2} ms a tick (worst {physicsWorst:F1}), {s.SolidColliders} solid colliders; streaming: {workers}; in view: far chunks' flora redrawn {swaps}; redrawn inside the sun's shadow reach (casters changing) {farSwaps.Count(q => q.Away < _game.Zone.Sky.Sun.DirectionalShadowMaxDistance) / minutes:F0}/min; near ground rings {groundSwaps.Count / minutes:F0}/min at m {Q(groundSwaps, true)}; pops (count, and their sum of jump times size over distance) {byKind}; frames {_frames.Count}, p50 {(_frames.Count > 0 ? _frames[_frames.Count / 2] : 0):F1}, p99 {(_frames.Count > 0 ? _frames[(int)(_frames.Count * 0.99)] : 0):F1}, worst {(_frames.Count > 0 ? _frames[^1] : 0):F1} ms, {over} over 16.7 ms; gpu p50 {(_gpu.Count > 0 ? _gpu[_gpu.Count / 2] : 0):F1}, p99 {(_gpu.Count > 0 ? _gpu[(int)(_gpu.Count * 0.99)] : 0):F1} ms");
        // The gate (--check=1): limits from the best build measured (ee8a3ace^ with the owner's settings; fad41bce, his last
        // good test build, about the same), with room for a machine other runs share.
        static double P(List<double> v, double q) => v.Count == 0 ? double.NaN : v[Math.Min(v.Count - 1, (int)(v.Count * q))];
        if (Limits.GetValueOrDefault(name.Replace(" at once", "")) is { } lim)
        {
            Gate($"{name}: near ground complete, median", P(complete.Select(x => (double)x).OrderBy(x => x).ToList(), 0.5), lim.Ground, true, "m");
            Gate($"{name}: trees appear, 5th percentile", P(trees.Select(t => (double)t).OrderBy(x => x).ToList(), 0.05), lim.Trees, true, "m");
            Gate($"{name}: column wait, 95th percentile", P(columnWait, 0.95), lim.ColumnWait, false, "ms");
            Gate($"{name}: shadow casters redrawn in view", farSwaps.Count(q => q.Away < _game.Zone.Sky.Sun.DirectionalShadowMaxDistance) / minutes, lim.ShadowPops, false, "/min");
            Gate($"{name}: frames over 16.7 ms", over / minutes, lim.Over, false, "/min");
            Gate($"{name}: frame p99", P(_frames, 0.99), lim.P99, false, "ms");
        }
    }

    sealed record Limit(double Ground, double Trees, double ColumnWait, double ShadowPops, double Over, double P99);

    /// <summary>Per route: near ground at least, trees appearing at least, column wait at most, shadow casters redrawn in view at most, frames over 16.7 ms a minute at most, frame p99 at most.</summary>
    static readonly Dictionary<string, Limit> Limits = new()
    {
        ["walk"] = new(88, double.NaN, double.NaN, 60, 60, 20), // (a walk sees too few trees and columns to judge)
        ["bike"] = new(88, 150, 220, 400, 100, 20),
        ["fly"] = new(72, 180, 360, 700, 180, 25),
    };

    int _gateFails;

    void Gate(string what, double value, double limit, bool atLeast, string unit)
    {
        if (!_args.ContainsKey("check") || double.IsNaN(value) || double.IsNaN(limit)) return;
        bool ok = atLeast ? value >= limit : value <= limit;
        if (!ok) _gateFails++;
        GD.Print(string.Create(CultureInfo.InvariantCulture, $"  {(ok ? "ok   " : "FAIL ")} {what} {value:F1} {unit} ({(atLeast ? "at least" : "at most")} {limit:F0})"));
    }

    static double Mod(double a, double m) => a - Math.Floor(a / m) * m;
    static int Mod(int a, int m) => ((a % m) + m) % m;
}
