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
    readonly List<double> _frames = new();
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
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = 60; // as the player sees it (vsync): per-frame limits show as they do in play
        // (Set by reflection: the flag is only there from places built after load on.)
        if (_args.ContainsKey("places-at-load"))
            typeof(PlanetSurface.World.PlanetWorld).GetField("BuildPlacesAtLoad")?.SetValue(null, true);
        ProcessPhysicsPriority = 100;
        _game = new Game.Game { Interactive = false, SkipIntro = true, World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Tested) };
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
        Wildlife.Shutdown.Quit(this, _failed ? 1 : 0);
    }

    public override void _Process(double delta)
    {
        long now = Stopwatch.GetTimestamp();
        if (_last != 0) _frames.Add((now - _last) * 1000.0 / Stopwatch.Frequency);
        _last = now;
    }

    IEnumerable<int> Routes()
    {
        var load = Stopwatch.StartNew();
        foreach (int x in _drive.UntilPlaying(300)) yield return x;
        var zone = _game.Zone;
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
        PopLog.On = true;
        // First straight off, as a player does the moment the world shows (whatever still loads then loads under it); then each from a settled start.
        foreach (int x in Route("fly at once", start, -way, 70f, 4000f, 60f)) yield return x;
        foreach (var (name, speed, length, up) in new[] { ("walk", 6f, 300f, 0.3f), ("bike", 26f, 1500f, 1f), ("fly", 70f, 4000f, 60f) })
        {
            _game.Survivor.PlaceOutside(Lift(start, up).ToNumerics());
            for (int i = 0; i < 60; i++) yield return 0;
            foreach (int x in _drive.UntilSettled(40)) yield return x;
            for (int i = 0; i < 120; i++) yield return 0;
            foreach (int x in Route(name, start, way, speed, length, up)) yield return x;
        }
        PopLog.On = false;
        GD.Print($"ROUTE ruin trees planted: {_planted0} when the world showed, {(zone.Planet.Palette.Planted as System.Collections.ICollection)?.Count} at the end");
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
        var farIn = new HashSet<(int, int)>();
        int size = FarBuilder.Size;
        PopLog.Take();
        _frames.Clear();
        float along = 0f;
        int tick = 0;
        while (along < length)
        {
            along += speed / Engine.PhysicsTicksPerSecond;
            Vector3 p = Lift(start + way * along, up);
            _game.Survivor.PlaceOutside(p.ToNumerics());
            _drive.Face(Lift(start + way * (along + 200f), up * 0.5f + 1.6f));
            yield return 0;
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
            foreach (Node n in s.GetChildren())
            {
                string nm = n.Name.ToString();
                if (!nm.StartsWith("Far ") || !nm.EndsWith(" lod0")) continue;
                string[] parts = nm.Split(' ');
                var key = (int.Parse(parts[1]), int.Parse(parts[2]));
                if (farIn.Add(key) && farWanted.TryGetValue(key, out double t)) farWait.Add(clock.Elapsed.TotalMilliseconds - t);
            }
        }
        s.ChunkApplied -= onChunk;
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
        _frames.Sort();
        GD.Print($"ROUTE {name} {speed:F0} m/s {length:F0} m: near ground complete to m {Q(complete.Select(c => (double)c).ToList(), true)}; " +
                 $"nearest far chunk short of its level (chunks) {Q(farShort.Where(f => f < int.MaxValue).Select(f => (double)f).ToList(), true)}, at 0 in {farShort.Count(f => f == 0) * 100 / Math.Max(1, farShort.Count)}% of samples; " +
                 $"queue {Q(pending.Select(c => (double)c).ToList())}; far chunks short of their level {Q(farBehind.Select(c => (double)c).ToList())}; column wait ms {Q(columnWait)}; level-0 far chunk wait ms {Q(farWait)}; " +
                 $"trees appearing at m {Q(trees.Select(t => (double)t).ToList(), true)}, within 150 m {trees.Count(t => t < 150f)}; flora or cover jumps over 5% within 120 m {nearJumps}; " +
                 $"pops (count, and their sum of jump times size over distance) {byKind}; frames {_frames.Count}, p50 {(_frames.Count > 0 ? _frames[_frames.Count / 2] : 0):F1}, p99 {(_frames.Count > 0 ? _frames[(int)(_frames.Count * 0.99)] : 0):F1}, worst {(_frames.Count > 0 ? _frames[^1] : 0):F1} ms");
    }

    static double Mod(double a, double m) => a - Math.Floor(a / m) * m;
    static int Mod(int a, int m) => ((a % m) + m) % m;
}
