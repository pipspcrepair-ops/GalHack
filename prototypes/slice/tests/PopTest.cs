using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using Planets.Gen;
using PlanetSurface.Fx;
using PlanetSurface.Poi;
using Slice.Game;

namespace Slice.Tests;

/// <summary>
/// Pop-in: on a fresh random world, the castaway walks, rides a hover bike and flies low toward
/// places, on the real clock, and everything the drawing changes on the way is logged (<see cref="PopLog"/>):
/// flora, ground cover, places' far and merged blocks, city tiles, places' full detail, props and
/// creatures, with how far off each was and how much of the screen changed in one frame. A route
/// fails if anything changes hard (half its look or more in one frame) inside <see cref="Inside"/>
/// metres, or by <see cref="MostPixels"/> pixels or more anywhere, at 1080p and the game's field of
/// view. It also measures how far the places' far meshes stand off their merged blocks (size and
/// colour), and the main thread's worst frames on the way.
///
/// Run: .\tools\test.ps1 -Project prototypes/slice -Scene res://tests/PopTest.tscn
///   (-- --report=1 lists the pops and passes whatever: a measurement)
/// </summary>
public partial class PopTest : Node
{
    const int WatchdogMinutes = 9;

    /// <summary>Nothing changes hard inside this, m.</summary>
    const float Inside = 100f;

    /// <summary>The most a change may move on screen in one frame, px (1080 lines).</summary>
    const float MostPixels = 4f;

    /// <summary>A change of less than this share of a thing in one frame is a fade (it takes ten frames or more), not a pop, however big the thing.</summary>
    const float Sudden = 0.1f;

    Game.Game _game;
    IEnumerator<int> _run;
    int _failures, _passes;
    bool _report;
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
        _report = OS.GetCmdlineUserArgs().Contains("--report=1");
        PlanetSurface.Infra.CityPlan.Force = true; // a city on any lived-in world, for the flight over it
        ProcessPriority = 100;
        _run = Scenario().GetEnumerator();
    }

    public override void _Process(double delta)
    {
        _delta = delta;
        bool more;
        try { more = _run.MoveNext(); }
        catch (Exception e)
        {
            Fail($"scenario threw: {e}");
            more = false;
        }
        if (more) return;
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {_clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        SetProcess(false);
        PopLog.On = false;
        Wildlife.Shutdown.Quit(this, _failures == 0 || _report ? 0 : 1);
    }

    double _delta;
    int _farBuilt;

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
        foreach (int x in drive.UntilPlaying(240)) yield return x;
        var zone = _game.Zone;
        zone.Weather.Bridge.Calm = true;
        zone.Weather.Sim.Snap(Weather.Model.WeatherKind.Clear);
        var world = _game.Surface.Planet;
        GD.Print($"-- pop-in on {_game.World}: {world.Pois.Count(s => s.Instance != null)} places, {world.Pois.Count(s => s.Siting == "city")} of them a city's --");

        var rng = new Random();
        var places = world.Pois.Where(s => s.Instance != null).ToList();
        if (places.Count == 0)
        {
            Check(true, "(no places on this world: the routes cross open land)");
            places = null;
        }
        PoiSite Pick(Func<PoiSite, bool> want) => places?.Where(want).OrderBy(_ => rng.Next()).FirstOrDefault() ?? places?.OrderBy(_ => rng.Next()).FirstOrDefault();
        PoiSite city = Pick(s => s.Siting == "city");
        PoiSite town = Pick(s => s.Siting != "city" && places.Count(o => o != s && (o.Origin.X - s.Origin.X) * (o.Origin.X - s.Origin.X) + (o.Origin.Z - s.Origin.Z) * (o.Origin.Z - s.Origin.Z) < 400 * 400) >= 2);
        PoiSite any = Pick(_ => true);

        foreach (int x in Route("walk", any, 7f, 1.6f, 380f, -40f)) yield return x;
        foreach (int x in Route("bike", town ?? any, 38f, 3.0f, 1500f, -60f)) yield return x;
        foreach (int x in Route("fly", city ?? town ?? any, 90f, 70f, 2600f, -700f)) yield return x;

        GD.Print("-- far meshes against merged blocks --");
        var sample = places?.OrderBy(_ => rng.Next()).Take(40).ToList() ?? new();
        var gaps = sample.Select(s => (Site: s, Gap: PoiLayer.StandInGap(s))).ToList();
        if (gaps.Count > 0)
        {
            var was = sample.Select(s => PoiLayer.StandInGap(s, AsItWas(s.Instance.Blocks, s.Instance.Layout.Occupied))).ToList();
            GD.Print($"  (the far mesh as it was: {was.Average(g => g.Size):P0} off in size on average, at worst {was.Max(g => g.Size):P0}; {was.Average(g => g.Tone):F3} off in colour, at worst {was.Max(g => g.Tone):F3})");
            var worst = gaps.OrderByDescending(g => g.Gap.Size).First();
            var worstTone = gaps.OrderByDescending(g => g.Gap.Tone).First();
            // A measurement since a3e86f8: the far mesh went back to its old outline and colour rule (the
            // stricter one measured no closer), and the far mesh and the merged blocks dissolve into each
            // other instead; the routes above judge that switch.
            GD.Print($"  (far meshes against their merged blocks: {gaps.Average(g => g.Gap.Size):P0} off in size on average over {gaps.Count} places, at worst {worst.Gap.Size:P0} ({worst.Site.Name}); {gaps.Average(g => g.Gap.Tone):F3} off in colour a channel, at worst {worstTone.Gap.Tone:F3} ({worstTone.Site.Name}))");
        }
        _game.FixedCamera = null;
    }

    /// <summary>
    /// One route: from <paramref name="from"/> m out toward a place, at a speed and a height over the
    /// ground, on to <paramref name="to"/> m (negative: past it), looking the way it goes.
    /// </summary>
    IEnumerable<int> Route(string name, PoiSite site, float speed, float height, float from, float to)
    {
        var world = _game.Surface.Planet;
        var places = _game.Places;
        Vector3 at = _game.Survivor.WorldFeet;
        Vector3 goal;
        if (site != null)
        {
            goal = world.NearestCopy(new Vector3(site.Origin.X, site.Origin.Y, site.Origin.Z), at);
        }
        else goal = at + new Vector3(from, 0f, 0f);
        float angle = (float)(new Random().NextDouble() * Math.Tau);
        Vector3 way = new(MathF.Cos(angle), 0f, MathF.Sin(angle));
        Vector3 start = goal - way * from, end = goal - way * to;
        float Ground(Vector3 p) => world.GroundHeight(Mathx.WrapX(p.X, world.Width), Mathx.Wrap(p.Z, world.Height));
        float pitch = height > 20f ? -0.22f : -0.06f;
        void Put(Vector3 p)
        {
            float g = MathF.Max(Ground(p), 0f);
            var feet = new Vector3(p.X, g + (height > 20f ? height : 0.1f), p.Z);
            _game.Survivor.PlaceOutside(feet.ToNumerics());
            Vector3 eye = new(p.X, g + height, p.Z);
            _game.FixedCamera = new Transform3D(Basis.LookingAt(way + Vector3.Up * pitch, Vector3.Up), eye);
        }

        // Stand at the start until the world round it has come in, as it would have for someone who'd been there.
        var settle = Stopwatch.StartNew();
        while (settle.Elapsed.TotalSeconds < 40 && (settle.Elapsed.TotalSeconds < 4 || !_game.Zone.Streamer.Settled || _game.Places.Building != null || _game.Zone.Pois.MidPending))
        {
            Put(start);
            yield return 0;
        }
        PopLog.Take();
        PopLog.On = true;
        _game.Zone.Streamer.ResetStats();
        places.ResetWorst();
        var frames = new List<double>();
        _farBuilt = _game.Zone.Streamer.FarBuilds.Sum();
        float length = from - to, gone = 0f;
        var run = Stopwatch.StartNew();
        while (gone < length && run.Elapsed.TotalSeconds < 120)
        {
            gone = MathF.Min(length, gone + speed * (float)Math.Min(_delta, 0.1));
            Put(start + way * gone);
            frames.Add(_delta * 1000.0);
            yield return 0;
        }
        // A moment stood at the end, for what was still coming in.
        for (var hold = Stopwatch.StartNew(); hold.Elapsed.TotalSeconds < 2;)
        {
            Put(end);
            yield return 0;
        }
        PopLog.On = false;
        List<PopLog.Pop> pops = PopLog.Take();
        Judge(name, site, speed, length, pops, frames);
    }

    /// <summary>
    /// The far mesh as it was before it kept to its merged blocks (4 m cells solid from 7 blocks in
    /// 64, every block's colour averaged in, inner walls and floors too): what the measure compares with.
    /// </summary>
    static PoiGenerator.Gen.ChunkMesh AsItWas(PoiGenerator.Gen.BlockGrid g, PoiGenerator.Gen.CellMask occupied, int step = 4, int minimum = 7)
    {
        var size = new PoiGenerator.Gen.Int3((g.Size.X + step - 1) / step, (g.Size.Y + step - 1) / step, (g.Size.Z + step - 1) / step);
        var count = new int[size.X * size.Y * size.Z];
        var sum = new long[count.Length * 3];
        var window = new PoiGenerator.Gen.Rgb(0x8e, 0x9a, 0xa6);
        foreach (var (c, b) in g.Blocks())
        {
            int weight = PoiGenerator.Gen.BlockInfo.IsFullCube(b.Type) ? 2 : b.Type is PoiGenerator.Gen.BlockType.Ramp or PoiGenerator.Gen.BlockType.Corner or PoiGenerator.Gen.BlockType.InvCorner
                or PoiGenerator.Gen.BlockType.Stair or PoiGenerator.Gen.BlockType.Rib or PoiGenerator.Gen.BlockType.Pillar or PoiGenerator.Gen.BlockType.Pipe or PoiGenerator.Gen.BlockType.Tank or PoiGenerator.Gen.BlockType.Lattice ? 1 : 0;
            if (weight == 0 || PoiGenerator.Gen.BlockInfo.IsTransparent(b.Material)) continue;
            var l = c - g.Min;
            int i = ((l.Y / step) * size.Z + l.Z / step) * size.X + l.X / step;
            count[i] += weight;
            var col = b.Role == PoiGenerator.Gen.Role.Window ? window : b.Color;
            sum[i * 3] += col.R * weight;
            sum[i * 3 + 1] += col.G * weight;
            sum[i * 3 + 2] += col.B * weight;
        }
        var coarse = new PoiGenerator.Gen.BlockGrid(new PoiGenerator.Gen.Int3(0, 0, 0), size);
        for (int y = 0, i = 0; y < size.Y; y++)
            for (int z = 0; z < size.Z; z++)
                for (int x = 0; x < size.X; x++, i++)
                {
                    int n = count[i];
                    if (n < minimum * 2) continue;
                    var color = new PoiGenerator.Gen.Rgb((byte)(sum[i * 3] / n), (byte)(sum[i * 3 + 1] / n), (byte)(sum[i * 3 + 2] / n));
                    coarse.Set(new PoiGenerator.Gen.Int3(x, y, z), new PoiGenerator.Gen.Block(PoiGenerator.Gen.BlockType.Cube, PoiGenerator.Gen.Material.Concrete, color, PoiGenerator.Gen.Role.Wall));
                }
        var built = PoiGenerator.Gen.Mesher.Build(coarse, occupied, new PoiGenerator.Gen.MeshOptions { ChunkSize = Math.Max(size.X, Math.Max(size.Y, size.Z)) + 1, Rock = false, Collision = false, Detail = false, Doors = false, Merged = true });
        var all = new PoiGenerator.Gen.ChunkMesh();
        foreach (var m in built)
            for (int k = 0; k < m.Surfaces.Length; k++)
            {
                var from = m.Surfaces[k];
                var to = all.Surfaces[k];
                int first = to.VertexCount;
                for (int v = 0; v < from.VertexCount; v++)
                {
                    to.Positions.Add(from.Positions[v * 3] * step + g.Min.X);
                    to.Positions.Add(from.Positions[v * 3 + 1] * step + g.Min.Y);
                    to.Positions.Add(from.Positions[v * 3 + 2] * step + g.Min.Z);
                }
                to.Normals.AddRange(from.Normals);
                to.Colors.AddRange(from.Colors);
                foreach (int ix in from.Indices) to.Indices.Add(first + ix);
            }
        return all;
    }

    void Judge(string name, PoiSite site, float speed, float length, List<PopLog.Pop> pops, List<double> frames)
    {
        float focal = 540f / MathF.Tan(Mathf.DegToRad(_game.Settings.Fov) * 0.5f);
        float Pixels(PopLog.Pop p) => p.Jump * p.Size / MathF.Max(p.Distance, 1f) * focal;
        bool Hard(PopLog.Pop p) => p.Jump >= 0.5f && p.Distance < Inside;
        bool Seen(PopLog.Pop p) => p.Jump >= Sudden && Pixels(p) >= MostPixels;
        var bad = pops.Where(p => Hard(p) || Seen(p)).ToList();
        frames.Sort();
        double p99 = frames.Count > 0 ? frames[(int)(frames.Count * 0.99)] : 0;
        GD.Print($"  {name}: {length:F0} m at {speed:F0} m/s toward {site?.Name ?? "open land"}{(site != null ? $" ({site.Siting})" : "")}; {pops.Count} changes logged, {bad.Count} seen as pops; frame 99th pct {p99:F1} ms, streaming worst {_game.Zone.Streamer.WorstFrameMs:F1} ms, places worst {_game.Places.WorstFrameMs:F1} ms; far chunks built on the way {_game.Zone.Streamer.FarBuilds.Sum() - _farBuilt}, nearest ring behind at the end {_game.Zone.Streamer.FarBehindNearest}; worst streaming frame: {_game.Zone.Streamer.WorstBreakdown}; worst far node put in {_game.Zone.Streamer.WorstFarNodeMs:F1} ms");
        foreach (var g in pops.GroupBy(p => (p.Kind, p.What)).OrderBy(g => g.Key.Kind))
        {
            var seen = g.Where(p => Hard(p) || Seen(p)).ToList();
            GD.Print($"      {g.Key.Kind,-8} {g.Key.What,-34} {g.Count(),6} logged, {seen.Count,5} pops" +
                (seen.Count > 0 ? $", nearest {seen.Min(p => p.Distance),4:F0} m, worst {seen.Max(Pixels),5:F1} px" : ""));
        }
        Check(_report || bad.Count == 0, $"{name}: nothing pops (hard inside {Inside:F0} m, or {MostPixels:F0} px or more in a frame): {bad.Count} pops" +
            (bad.Count > 0 ? $", worst {bad.Max(Pixels):F0} px ({bad.OrderByDescending(Pixels).First().Kind} {bad.OrderByDescending(Pixels).First().What} at {bad.OrderByDescending(Pixels).First().Distance:F0} m)" : ""));
    }
}
