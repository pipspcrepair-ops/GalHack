using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GalHack.SkyCheck;
using Godot;
using Planets.Gen;
using PlanetSurface.Skies;
using PlanetSurface.World;
using Weather.Model;
using Archetype = Planets.Gen.Archetype;

namespace PlanetSurface;

/// <summary>
/// The sky as a player sees it, rendered (headless runs draw nothing, so this starts itself again
/// in a window placed off-screen and passes on what it prints):
/// - <b>No seams.</b> The sky alone, from the planet's own <see cref="SkyRig"/>, looked at straight
///   along a cube's edges and corners, the zenith, the axes and the azimuth's wrap: by day under
///   broken cloud with the sun up and in the afternoon west (the slice's world and weather), at
///   dusk, under heavy cloud, and at night under a nebula. No line, no pinch, and stars as dense
///   along the axes as anywhere (<see cref="SkySeams"/>).
/// - <b>A low sun never drenches the picture.</b> On a temperate coast and in arid hills, at dawn
///   and at dusk with the sun a few degrees up, looking toward it, across it and away from it, in
///   clear air and in fog: no white wash (hardly any near-white pixels, never brighter than noon
///   in the same place and weather) and the ground never a bright, flat wall of lit haze.
/// Run alone: <c>.\tools\test.ps1 -Project prototypes/planet-surface -Scene res://tests/SkyTest.tscn</c>.
/// </summary>
public partial class SkyTest : Node
{
    int _failures;

    public override async void _Ready()
    {
        if (SkySeams.RelaunchWindowed(this, 600) is int code)
        {
            GetTree().Quit(code);
            return;
        }
        PlanetWorld.SaveDirectory = $"user://test-planets/{OS.GetProcessId()}";
        string only = System.Array.Find(OS.GetCmdlineUserArgs(), a => a.StartsWith("--only="))?["--only=".Length..];
        try
        {
            if (only is null or "seams") await Seams();
            if (only is null or "lowsun") await LowSun();
        }
        catch (Exception e)
        {
            Fail($"sky test crashed: {e}");
        }
        string saves = ProjectSettings.GlobalizePath(PlanetWorld.SaveDirectory);
        try { if (System.IO.Directory.Exists(saves)) System.IO.Directory.Delete(saves, recursive: true); }
        catch (Exception) { } // this run's own folder: nothing else reads it
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} CHECK(S) FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    void Check(bool ok, string what)
    {
        GD.Print($"{(ok ? "PASS" : "FAIL")}  {what}");
        if (!ok) _failures++;
    }

    void Fail(string what) => Check(false, what);

    Task Frames(int n) => FramesAsync(n);

    async Task FramesAsync(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    }

    // ---- Seams ------------------------------------------------------------------------------------

    /// <summary>The sky alone, in a world of its own: no ground in front of the low probes.</summary>
    async Task Seams()
    {
        var stage = new SubViewport { OwnWorld3D = true, Size = new Vector2I(4, 4), RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        AddChild(stage);
        var rig = new SkyRig { Name = "Sky" };
        stage.AddChild(rig);
        var eye = new Vector3(0f, 120f, 0f);

        PlanetWorld slice = PlanetWorld.Build(PlanetSpec.Roll(7, Archetype.Temperate, 0), Main.Catalog);
        rig.SetWorld(slice);
        foreach (var (what, time, cover) in new[]
                 {
                     ("by day under broken cloud, the sun well up", 0.40, 0.45f),
                     ("in the afternoon, the sun in the west", 0.62, 0.45f),
                     ("at dusk under broken cloud", 0.745, 0.45f),
                     ("by day under heavy cloud", 0.45, 0.85f),
                 })
        {
            Pose(rig, slice, time, cover);
            await Frames(8);
            int failures = SkySeams.Report($"the slice's sky {what}", await SkySeams.LookAround(stage, eye, SkySeams.Probes()));
            _failures += failures;
        }

        // Night under a nebula, the stars' own frame lined up with the world's (midnight): the
        // backdrop's pole and wrap, if it had them, would be straight up and behind.
        PlanetWorld alien = PlanetWorld.Build(PlanetSpec.Roll(1, Archetype.Alien, 0), Main.Catalog);
        rig.SetWorld(alien);
        Pose(rig, alien, 0.0, 0.1f);
        await Frames(8);
        _failures += SkySeams.Report("a nebula world's sky at midnight", await SkySeams.LookAround(stage, eye, SkySeams.Probes()));
        // The backdrop alone, six times as bright: a pinch or a wrap in how the nebula is looked
        // up shows plainly even where the night dims it out of sight (the pole is far from its band).
        rig.DebugView(3);
        await Frames(4);
        _failures += SkySeams.Report("a nebula world's night backdrop alone, brightened", await SkySeams.LookAround(stage, eye, SkySeams.Probes()));
        rig.DebugView(0);
        RemoveChild(stage);
        stage.QueueFree();
    }

    static void Pose(SkyRig rig, PlanetWorld world, double time, float cloudCover)
    {
        rig.TimeOfDay = time;
        rig.Paused = true;
        Conditions c = Atmosphere.Clear();
        c[Param.CloudCover] = cloudCover;
        rig.Advance(0.0, 0f, Atmosphere.Compute(world.Sky, c, rig.SunAt(time).Y), Vector2.Zero);
    }

    // ---- A low sun ----------------------------------------------------------------------------------

    /// <summary>What one frame looked like: its median brightness, the ground's contrast, and how much of it is near white.</summary>
    readonly record struct Frame(float Median, float Spread, float GroundMedian, float GroundSpread, float White)
    {
        /// <summary>Washed out: bright, flat ground (a white wall of lit haze), or near-white over the picture.</summary>
        public bool WashedOut => White >= 0.02f || (GroundMedian > 0.45f && GroundSpread < 0.08f);

        public override string ToString() => $"median {Median:F2}, contrast {Spread:F2} (ground {GroundMedian:F2}, contrast {GroundSpread:F2}), {White:P1} near white";
    }

    static Frame Measure(Image image)
    {
        image.Resize(400, 225, Image.Interpolation.Bilinear);
        var all = new List<float>(400 * 225);
        var ground = new List<float>(400 * 100);
        int white = 0;
        for (int y = 0; y < 225; y++)
            for (int x = 0; x < 400; x++)
            {
                Color c = image.GetPixel(x, y);
                float l = 0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B;
                all.Add(l);
                if (y >= 125) ground.Add(l);
                if (MathF.Min(c.R, MathF.Min(c.G, c.B)) > 0.85f) white++;
            }
        all.Sort();
        ground.Sort();
        return new Frame(all[all.Count / 2], all[all.Count * 19 / 20] - all[all.Count / 20],
            ground[ground.Count / 2], ground[ground.Count * 19 / 20] - ground[ground.Count / 20], white / (float)all.Count);
    }

    async Task LowSun()
    {
        var main = new Main { Interactive = false };
        AddChild(main);
        foreach (var (place, spec) in new[]
                 {
                     ("a temperate coast", PlanetSpec.Roll(21, Archetype.Temperate, 0)),
                     ("arid hills", PlanetSpec.Roll(7, Archetype.Arid, 1)),
                 })
        {
            main.Arrive(spec);
            if (!await Until(() => main.World != null, 120)) { Fail($"{place}: the planet never built"); continue; }
            PlanetWorld w = main.World;
            var at = new Vector3((float)w.Landing.X, 0f, (float)w.Landing.Y);
            at.Y = MathF.Max(w.GroundHeight(at.X, at.Z), 0f) + 8f;
            main.Player.ProcessMode = ProcessModeEnum.Disabled;
            main.Hud.HideAll();
            main.Sky.Paused = true;

            async Task<Frame> Shot(double time, float heading, float pitch, WeatherKind weather)
            {
                main.Sky.TimeOfDay = time;
                main.Weather.Snap(weather);
                main.Fly.Begin(at, -heading, Mathf.DegToRad(pitch));
                await Until(() => main.Streamer.Settled, 90);
                await Frames(40); // the sky's light, fog and ambient settle
                return Measure(GetViewport().GetTexture().GetImage());
            }

            float SunHeading(double time)
            {
                Vector3 sun = main.Sky.SunAt(time);
                return MathF.Atan2(sun.X, -sun.Z);
            }

            double dawn = 0.27, dusk = 0.735;
            var noon = new Dictionary<WeatherKind, Frame>();
            foreach (WeatherKind weather in new[] { WeatherKind.Clear, WeatherKind.Fog })
                noon[weather] = await Shot(0.5, SunHeading(dawn), -3f, weather);
            Frame clear = noon[WeatherKind.Clear];
            Check(clear.Median > 0.25f && clear.Median < 0.8f && !clear.WashedOut && clear.Spread > 0.15f,
                $"{place} at noon in clear air: bright and clear ({clear}); in fog, {noon[WeatherKind.Fog]}");
            foreach (var (when, time) in new[] { ("at dawn", dawn), ("at dusk", dusk) })
            {
                float sunUp = Mathf.RadToDeg(MathF.Asin(main.Sky.SunAt(time).Y));
                float toward = SunHeading(time);
                foreach (WeatherKind weather in new[] { WeatherKind.Clear, WeatherKind.Fog })
                    foreach (var (facing, turn) in new[] { ("toward", 0f), ("across", MathF.PI / 2f), ("away from", MathF.PI) })
                    {
                        Frame f = await Shot(time, toward + turn, facing == "toward" ? 2f : -3f, weather);
                        bool ok = !f.WashedOut && f.Median <= noon[weather].Median + 0.05f && f.Median > 0.05f;
                        Check(ok, $"{place} {when} (sun {sunUp:F0}° up) in {(weather == WeatherKind.Fog ? "fog" : "clear air")}, looking {facing} the sun: " +
                                  $"no white wash or bright flat haze, no brighter than noon in the same weather ({f})");
                    }
            }
        }
        RemoveChild(main);
        main.QueueFree();
    }

    async Task<bool> Until(Func<bool> condition, double seconds)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed.TotalSeconds > seconds) return false;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        return true;
    }
}
