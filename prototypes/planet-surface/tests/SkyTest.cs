using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GalHack.SkyCheck;
using GalHack.Stars;
using Godot;
using Planets.Gen;
using PlanetSurface.Skies;
using PlanetSurface.World;
using Weather.Model;
using Archetype = Planets.Gen.Archetype;
using N = System.Numerics;

namespace PlanetSurface;

/// <summary>
/// The sky as a player sees it, rendered (headless runs draw nothing, so this starts itself again
/// in a window placed off-screen and passes on what it prints):
/// - <b>The galaxy's core grows on the way in.</b> From a fresh galaxy's homes at depths 0 to 1
///   and on toward the central hole, the bulge's light and the sky it covers grow every step, with
///   no drop at the core, and the hole is drawn: a point from the rim, a disc with a dark centre
///   from the core (<c>--only=core</c>; <c>--keep=DIR</c> saves the two pictures of it).
/// - <b>The sun is one size all the way up.</b> Looked at straight on, at one zoom, on the ground,
///   at the top of the climb (the same sky with the air gone) and in orbit (space's own sky, set up
///   as the orbit sets it up), its disc is as wide in each within a few percent, and as wide as the
///   star's real radius over the world's distance from it (<c>--only=sun</c>; <c>--keep=DIR</c>
///   saves the three, with the game's own tone and bloom, at one zoom).
/// - <b>Eclipses are real.</b> At an eclipse found in a fresh world's own system, the sun's light
///   falls by the share of its disc hidden (<c>--only=eclipse</c>).
/// - <b>Bodies are whole.</b> A ringed giant, big and small, lit and on its night side, is drawn
///   over the whole of its disc, its ring in front of its near half and behind its far half
///   (<c>--only=bodies</c>); and the sun from each kind of world is a sensible size (<c>--only=sun</c>).
/// - <b>No seams.</b> The sky alone, from the planet's own <see cref="SkyRig"/>, looked at straight
///   along a cube's edges and corners, the zenith, the axes and the azimuth's wrap: by day under
///   broken cloud with the sun up and in the afternoon west (the slice's world and weather), at
///   dusk, under heavy cloud, and at night under a nebula. No line, no pinch, and stars as dense
///   along the axes as anywhere (<see cref="SkySeams"/>).
/// - <b>Clouds hide what is behind them.</b> At midnight on a fresh world, looking up all round, the
///   galaxy's light in the picture (drawn with it and without it) under a storm's deck is next to none
///   of what a clear sky shows, and a giant overhead is hidden there too, whether the storm is only
///   coming (fair air round its deck) or is here; broken cloud lets part of it through its gaps
///   (<c>--only=nightclouds</c>).
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
            GalHack.Testing.Backstop.Quit(this, code);
            return;
        }
        PlanetWorld.SaveDirectory = $"user://test-planets/{OS.GetProcessId()}";
        string only = System.Array.Find(OS.GetCmdlineUserArgs(), a => a.StartsWith("--only="))?["--only=".Length..];
        try
        {
            if (only is null or "core") await Core();
            if (only is null or "sun") await Sun();
            if (only is null or "sun") SunBands();
            if (only is null or "bodies") await WholeBodies();
            if (only is null or "eclipse") await Eclipse();
            if (only is null or "seams") await Seams();
            if (only is null or "nightclouds") await NightClouds();
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
        GalHack.Testing.Backstop.Quit(this, _failures == 0 ? 0 : 1);
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
            // The bodies stand where the system has them this moment, and a disc in the middle of a
            // picture is a real thing, not a seam: a direction with one in view is left out, by name.
            bool Clear(SkySeams.Probe p)
            {
                const float View = 0.4f; // half a probe picture's diagonal, radians
                foreach (var (body, direction, angular) in rig.Drawn)
                    if (direction.AngleTo(p.Direction) < View + angular * (body.Rings ? 2.5f : 1f))
                    {
                        GD.Print($"({what}: {p.Name} left out, {body.Name} is in view there)");
                        return false;
                    }
                if (rig.ToSun.AngleTo(p.Direction) >= View) return true;
                GD.Print($"({what}: {p.Name} left out, the sun is in view there)");
                return false;
            }
            int failures = SkySeams.Report($"the slice's sky {what}", await SkySeams.LookAround(stage, eye, SkySeams.Probes().Where(Clear).ToList()));
            _failures += failures;
        }

        // Night under the galaxy's own stars and nebulae: a cube's faces and the like, if they showed,
        // would be straight up and behind. Fresh every run, as every sky is.
        SkySeams.RealStars = true;
        PlanetWorld alien = PlanetWorld.Build(PlanetSpec.Roll((ulong)System.Random.Shared.NextInt64(), Archetype.Alien, 0), Main.Catalog);
        rig.SetWorld(alien);
        rig.Stars.Finish();
        Pose(rig, alien, 0.0, 0.1f);
        await Frames(8);
        // The galaxy's central black hole is a real thing in the sky as a moon is: a direction with it in view is left out, as theirs are.
        GalaxySky galaxy = rig.Stars;
        N.Vector3 inField = galaxy.ToHole;
        Vector3 hole = ((galaxy.Field * rig.SkyToStars).Inverse() * new Vector3(inField.X, inField.Y, inField.Z)).Normalized();
        float holeWide = MathF.Atan(galaxy.Galaxy.Hole.Outer * 2.5f / N.Vector3.Distance(galaxy.Galaxy.Hole.Position, galaxy.View.Eye)) + 0.02f;
        bool NoHole(SkySeams.Probe p)
        {
            if (hole.AngleTo(p.Direction) >= 0.4f + holeWide) return true;
            GD.Print($"(a nebula world at midnight: {p.Name} left out, the galaxy's central black hole is in view there)");
            return false;
        }
        _failures += SkySeams.Report("a nebula world's sky at midnight", await SkySeams.LookAround(stage, eye, SkySeams.Probes().Where(NoHole).ToList()));
        // The backdrop alone, six times as bright: a pinch or a wrap in how the nebula is looked
        // up shows plainly even where the night dims it out of sight (the pole is far from its band).
        rig.DebugView(3);
        await Frames(4);
        _failures += SkySeams.Report("a nebula world's night backdrop alone, brightened", await SkySeams.LookAround(stage, eye, SkySeams.Probes().Where(NoHole).ToList()));
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

    // ---- Clouds in front of the night ---------------------------------------------------------------

    async Task NightClouds()
    {
        var stage = new SubViewport { OwnWorld3D = true, Size = new Vector2I(4, 4), RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        AddChild(stage);
        var rig = new SkyRig { Name = "Sky" };
        stage.AddChild(rig);
        PlanetWorld world = PlanetWorld.Build(PlanetSpec.Roll((ulong)System.Random.Shared.NextInt64(), Archetype.Temperate, 0), Main.Catalog);
        rig.SetWorld(world);
        rig.Stars.Finish();
        double midnight = 0.0;
        for (double t = 0.0; t < 1.0; t += 0.004) if (rig.SunAt(t).Y < rig.SunAt(midnight).Y) midnight = t;
        var sky = (ShaderMaterial)rig.Environment.Sky.SkyMaterial;
        const int Size = 96;
        // The sky's own energy, as the game draws it: a multiplier would scale its reduced passes too, the clouds' cover with them.
        var camera = new Camera3D
        {
            Fov = 60f, Near = 0.1f, Far = 20000f,
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Sky, Sky = new Sky { SkyMaterial = sky, RadianceSize = Sky.RadianceSizeEnum.Size32 },
                TonemapMode = Godot.Environment.ToneMapper.Linear,
                AmbientLightSource = Godot.Environment.AmbientSource.Disabled, ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
            },
        };
        stage.Size = new Vector2I(Size, Size);
        stage.AddChild(camera);
        camera.Position = new Vector3(0f, 120f, 0f);
        camera.MakeCurrent();
        // Up all round: the zenith, six ways at 40 degrees and four at 20.
        var looks = new List<Vector3> { Vector3.Up };
        for (int i = 0; i < 6; i++) looks.Add(new Vector3(MathF.Cos(i * MathF.Tau / 6f) * MathF.Cos(0.7f), MathF.Sin(0.7f), MathF.Sin(i * MathF.Tau / 6f) * MathF.Cos(0.7f)));
        for (int i = 0; i < 4; i++) looks.Add(new Vector3(MathF.Cos(i * MathF.Tau / 4f + 0.4f) * MathF.Cos(0.35f), MathF.Sin(0.35f), MathF.Sin(i * MathF.Tau / 4f + 0.4f) * MathF.Cos(0.35f)));
        string keep = System.Array.Find(OS.GetCmdlineUserArgs(), a => a.StartsWith("--keep="))?["--keep=".Length..];
        async Task<float[]> Light(Vector3 at, string save)
        {
            camera.GlobalBasis = SkySeams.LookAlong(at);
            await Frames(5);
            Image image = stage.GetTexture().GetImage();
            if (keep != null && save != null) image.SavePng(System.IO.Path.Combine(keep, save + ".png"));
            var light = new float[Size * Size];
            for (int i = 0; i < light.Length; i++)
            {
                Color c = image.GetPixel(i % Size, i / Size).SrgbToLinear();
                light[i] = c.R + c.G + c.B;
            }
            return light;
        }
        // What a uniform changes in the pictures, drawn with it on and off: per pixel, summed over the looks.
        async Task<float> Adds(string uniform, IEnumerable<Vector3> at, string save)
        {
            float total = 0f;
            int n = 0;
            foreach (Vector3 d in at)
            {
                sky.SetShaderParameter(uniform, true);
                float[] with = await Light(d, n == 0 ? save : null);
                sky.SetShaderParameter(uniform, false);
                float[] without = await Light(d, null);
                for (int i = 0; i < with.Length; i++) total += MathF.Abs(with[i] - without[i]);
                if (keep != null && save != null)
                {
                    // Where it changed the picture, brightened.
                    var change = Image.CreateEmpty(Size, Size, false, Image.Format.Rgb8);
                    for (int i = 0; i < with.Length; i++) change.SetPixel(i % Size, i / Size, new Color(1f, 1f, 1f) * Math.Clamp(MathF.Abs(with[i] - without[i]) * 8f, 0f, 1f));
                    change.SavePng(System.IO.Path.Combine(keep, $"{save}-change-{n}.png"));
                }
                n++;
            }
            sky.SetShaderParameter(uniform, true);
            return total / (Size * Size);
        }
        // A storm overhead: four strong, wide cells round the eye, their deck solid over all of this sky.
        var cells = new Vector4[4];
        for (int i = 0; i < 4; i++) cells[i] = new Vector4(MathF.Cos(i * MathF.Tau / 4f) * 1000f, MathF.Sin(i * MathF.Tau / 4f) * 1000f, 4000f, 1f);
        // The sky's passes are made for this camera on its first frames: nothing is measured until they are drawing.
        await Light(Vector3.Up, null);
        await Frames(60);
        var results = new Dictionary<string, (float Galaxy, float Giant)>();
        foreach (var (what, cover, dark, cellular, storm) in new[]
                 {
                     ("a clear sky", 0f, 0f, 0f, false),
                     ("broken cloud", 0.45f, 0.1f, 0f, false),
                     ("a storm's deck coming in over fair air", 0.5f, 0.4f, 0f, true),
                     ("a thunderstorm", 1f, 0.85f, 0.85f, true),
                 })
        {
            rig.SetCells(cells, storm ? 4 : 0);
            rig.TimeOfDay = midnight;
            rig.Paused = true;
            Conditions c = Atmosphere.Clear();
            c[Param.CloudCover] = cover;
            c[Param.CloudDarkness] = dark;
            c[Param.Cellular] = cellular;
            c[Param.Rain] = storm && cellular > 0f ? 0.8f : 0f;
            rig.Advance(0.0, 0f, Atmosphere.Compute(world.Sky, c, rig.SunAt(midnight).Y), Vector2.Zero);
            await Frames(4);
            // A lit giant straight overhead against a plain night, then the galaxy alone (the system's own
            // bodies are the same in both pictures of each pair).
            string name = what.Split(' ')[^1].Replace("'", "");
            sky.SetShaderParameter("giant_dir", Vector3.Up);
            sky.SetShaderParameter("giant_radius", 0.12f);
            sky.SetShaderParameter("giant_light", new Vector3(0.3f, -0.85f, 0.4f).Normalized());
            sky.SetShaderParameter("giant_at", 0);
            sky.SetShaderParameter("giant_layer", -1);
            sky.SetShaderParameter("rings", false);
            sky.SetShaderParameter("galaxy_on", false);
            float giant = await Adds("giant", new[] { Vector3.Up }, null);
            sky.SetShaderParameter("giant", false);
            float galaxy = await Adds("galaxy_on", looks, $"night-{name}");
            if (keep != null)
            {
                rig.DebugView(1);
                await Light(Vector3.Up, $"cover-{name}");
                await Light(looks[1], $"cover-{name}-40");
                await Light(looks[7], $"cover-{name}-20");
                rig.DebugView(0);
            }
            results[what] = (galaxy, giant);
            GD.Print($"({what} at midnight: the galaxy adds {galaxy:F4} to the pictures, the giant {giant:F4}; cover {rig.Air.CloudCover:F2}, overcast {rig.Air.Overcast:F2})");
        }
        var (clearGalaxy, clearGiant) = results["a clear sky"];
        Check(clearGalaxy > 0.01f && clearGiant > 0.01f, $"at midnight a clear sky shows the galaxy ({clearGalaxy:F4}) and a giant overhead ({clearGiant:F4})");
        foreach (string storm in new[] { "a storm's deck coming in over fair air", "a thunderstorm" })
        {
            var (galaxy, giant) = results[storm];
            Check(galaxy <= clearGalaxy * 0.01f && giant <= clearGiant * 0.01f,
                $"under {storm} the galaxy is hidden ({galaxy / clearGalaxy:P2} of a clear sky's) and so is the giant ({giant / clearGiant:P2})");
        }
        float broken = results["broken cloud"].Galaxy;
        Check(broken > clearGalaxy * 0.05f && broken < clearGalaxy * 0.95f, $"broken cloud lets part of the galaxy through its gaps ({broken / clearGalaxy:P0} of a clear sky's)");
        rig.SetCells(cells, 0);
        RemoveChild(stage);
        stage.QueueFree();
        await Frames(1);
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
            Frame? clearNoon = null;
            static bool Bright(Frame f) => f.Median > 0.25f && f.Median < 0.8f && !f.WashedOut && f.Spread > 0.15f;
            foreach (WeatherKind weather in new[] { WeatherKind.Clear, WeatherKind.Fog })
            {
                // Noon's sky, not whatever stands in the way: four headings, as a canyon wall can fill any one of them.
                Frame best = default;
                for (int k = 0; k < 4; k++)
                {
                    Frame f = await Shot(0.5, SunHeading(dawn) + k * MathF.PI / 2f, -3f, weather);
                    if (f.Median > best.Median) best = f;
                    if (weather == WeatherKind.Clear && Bright(f)) clearNoon = f;
                }
                noon[weather] = best;
            }
            Frame clear = clearNoon ?? noon[WeatherKind.Clear];
            Check(Bright(clear),
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

    // ---- The galaxy's core ----------------------------------------------------------------------------

    /// <summary>
    /// One place's look at the core: how far it is from the central hole; the bulge's light as its
    /// stars spread (r + g + b, before its star clouds and dust lanes, which are its weather: a
    /// place can sit in a thick cloud or a thin reach) over the whole sky, the share of the sky it
    /// lights and toward the hole; the light drawn, clouds, lanes and all, over the whole sky; and
    /// the glow the sky really draws round the hole (everything the sky's cube holds there) against
    /// the bulge's own there.
    /// </summary>
    readonly record struct CoreLook(string Where, float Far, float Light, float Cover, float Toward, float Textured, float Drawn, float DrawnBulge);

    /// <summary>Where the bulge counts as lighting the sky, r + g + b.</summary>
    const float CoreLit = 0.15f;

    /// <summary>Directions spread evenly over a cap round <paramref name="axis"/> of <paramref name="radius"/> radians (all the sky for pi).</summary>
    static List<N.Vector3> Around(N.Vector3 axis, float radius, int count)
    {
        N.Vector3 a = N.Vector3.Normalize(N.Vector3.Cross(axis, MathF.Abs(axis.Y) > 0.9f ? N.Vector3.UnitX : N.Vector3.UnitY)), b = N.Vector3.Cross(axis, a);
        float lowest = MathF.Cos(radius);
        var list = new List<N.Vector3>(count);
        for (int i = 0; i < count; i++)
        {
            float z = 1f - (1f - lowest) * (i + 0.5f) / count, r = MathF.Sqrt(MathF.Max(0f, 1f - z * z)), turn = i * 2.39996323f;
            list.Add(axis * z + (a * MathF.Cos(turn) + b * MathF.Sin(turn)) * r);
        }
        return list;
    }

    static float Median(List<float> values)
    {
        if (values.Count == 0) return 0f;
        values.Sort();
        return values[values.Count / 2];
    }

    static CoreLook LookAtCore(Galaxy g, SkyView v, string where)
    {
        N.Vector3 to = g.Hole.Position - v.Eye;
        float far = to.Length();
        N.Vector3 h = to / far;
        static float Sum(N.Vector3 c) => c.X + c.Y + c.Z;
        float Bulge(N.Vector3 d) => Sum(SkyView.BulgeLight(SkyView.Column(g, v.Eye, d)));
        float Spread(N.Vector3 d) => SkyView.BulgeTone(SkyView.Column(g, v.Eye, d).Spread);
        float Cube(N.Vector3 d)
        {
            var (f, s, t) = SkyView.Face(d);
            int x = Math.Clamp((int)((s * 0.5f + 0.5f) * SkyView.GlowSize), 0, SkyView.GlowSize - 1), y = Math.Clamp((int)((t * 0.5f + 0.5f) * SkyView.GlowSize), 0, SkyView.GlowSize - 1), k = (x + y * SkyView.GlowSize) * 3;
            return v.Glow[f][k] + v.Glow[f][k + 1] + v.Glow[f][k + 2];
        }
        double light = 0, textured = 0;
        int lit = 0;
        List<N.Vector3> all = Around(N.Vector3.UnitY, MathF.PI, 3000);
        foreach (N.Vector3 d in all)
        {
            float l = Spread(d);
            light += l;
            textured += Bulge(d);
            if (l >= CoreLit) lit++;
        }
        float toward = Around(h, Mathf.DegToRad(5f), 48).Average(Spread);
        List<N.Vector3> round = Around(h, Mathf.DegToRad(45f), 160);
        return new CoreLook(where, far, (float)(light / all.Count), lit / (float)all.Count, toward, (float)(textured / all.Count), Median(round.Select(Cube).ToList()), Median(round.Select(Bulge).ToList()));
    }

    /// <summary>
    /// The galaxy's core, from a fresh galaxy: from homes at depths 0, 0.5, 0.85, 0.95 and 1, and
    /// on in toward the central hole as deep as a home can be, the bulge's light and the share of
    /// the sky it lights grow all the way in, with no drop on reaching the core (where the sky
    /// once lost it), and the sky's cube really holds it. Then the hole itself, rendered through
    /// the one sky include every sky uses: a point from the rim, and from the core a disc with a
    /// dark centre, as big as it really looks.
    /// </summary>
    async Task Core()
    {
        ulong seed = (ulong)System.Random.Shared.NextInt64();
        var (g, homes, looks) = await Task.Run(() =>
        {
            Galaxy galaxy = Galaxy.Make(seed);
            ulong key = Galaxy.HomeKey(seed, StarKind.YellowDwarf, false);
            var at = new List<(string Where, int Here, N.Vector3 Eye)>();
            foreach (float depth in new[] { 0f, 0.5f, 0.85f, 0.95f, 1f })
            {
                int home = galaxy.Home(StarKind.YellowDwarf, key, false, depth);
                at.Add(($"the home at depth {depth:0.00} ({galaxy.DepthOf(home):0.00})", home, galaxy.Stars[home].Position));
            }
            N.Vector3 hole = galaxy.Hole.Position, core = at[^1].Eye;
            float from = N.Vector3.Distance(core, hole);
            foreach (float share in new[] { 0.6f, 0.3f })
            {
                float d = MathF.Max(Heart.Reach, from * share);
                if (d < N.Vector3.Distance(at[^1].Eye, hole) - 4f) at.Add(($"{d:F0} LY from the hole", -1, hole + N.Vector3.Normalize(core - hole) * d));
            }
            var seen = new List<CoreLook>();
            foreach (var (where, here, eye) in at)
                seen.Add(LookAtCore(galaxy, here >= 0 ? SkyView.From(galaxy, here) : SkyView.Bake(galaxy, eye), where));
            return (galaxy, at, seen);
        });
        GD.Print($"(core: galaxy seed {seed}, {g.BulgeStars} catalogue stars in its bulge; its hole's disc {g.Hole.Outer:F1} LY across the middle, shadow {g.Hole.Shadow:F2} LY)");
        foreach (CoreLook k in looks)
            GD.Print($"(core: {k.Where}, {k.Far:F0} LY from the hole: light {k.Light:F3} ({k.Textured:F3} with its clouds and lanes), {k.Cover:P0} of the sky lit, toward the hole {k.Toward:F2}; drawn within 45 degrees {k.Drawn:F2}, its bulge {k.DrawnBulge:F2})");
        bool grows = true, covers = true, holds = true;
        float most = 0f;
        for (int i = 1; i < looks.Count; i++)
        {
            grows &= looks[i].Light > looks[i - 1].Light;
            covers &= looks[i].Cover >= looks[i - 1].Cover - 0.01f;
        }
        foreach (CoreLook k in looks)
        {
            holds &= k.Toward >= most * 0.75f;
            most = MathF.Max(most, k.Toward);
        }
        int core = homes.FindIndex(h => h.Where.StartsWith("the home at depth 1"));
        Check(grows, "the core's light over the sky grows at every step in, from the rim to as deep as a home can be: " + string.Join(", ", looks.Select(k => $"{k.Light:F3}")));
        Check(looks.All(k => k.Textured >= k.Light * 0.12f) && looks[core].Textured > looks[core - 2].Textured * 2f,
            "its star clouds and dust lanes shape it but never hide it, and it is still far brighter at depth 1 than at 0.85: " + string.Join(", ", looks.Select(k => $"{k.Textured:F3}")));
        Check(covers && looks[^1].Cover >= 0.8f, "and so does the share of the sky it lights, nearly all of it deep in the core: " + string.Join(", ", looks.Select(k => $"{k.Cover:P0}")));
        Check(holds && looks[core].Toward > 0.3f, "toward the hole it never fades on the way in (no drop at the core): " + string.Join(", ", looks.Select(k => $"{k.Toward:F2}")));
        // The sky's own cube (all it draws there, nebulae and their dust too) holds the bulge's light everywhere on the way in.
        Check(looks.All(k => k.Drawn >= k.DrawnBulge * 0.4f) && looks[core].DrawnBulge > 0.02f,
            "the sky's own glow round the hole holds the bulge's light at every step in: " + string.Join(", ", looks.Select(k => $"{k.Drawn:F2} of {k.DrawnBulge:F2}")));
        await HeartDrawn(g, seed, homes[core].Here, true);
        await HeartDrawn(g, seed, homes[0].Here, false);
    }

    /// <summary>The galaxy's light alone, through the include every sky uses, scaled to fit an ordinary picture.</summary>
    const string HeartSky = "shader_type sky;\n#include \"" + GalaxyShader.Path + "\"\nvoid sky() { COLOR = galaxy_sky(EYEDIR, !AT_CUBEMAP_PASS) * 0.2; }\n";

    /// <summary>
    /// The hole as a sky draws it from a home, looked at straight on: from the core (<paramref name="disc"/>) a
    /// bright disc round a dark centre, each the size it really looks; from the rim a point brighter than the bulge round it.
    /// </summary>
    async Task HeartDrawn(Galaxy g, ulong seed, int home, bool disc)
    {
        GalaxyShader.Register();
        var stars = new GalaxySky(g, home, seed);
        stars.Finish();
        var material = new ShaderMaterial { Shader = new Shader { Code = HeartSky } };
        stars.Bind(material);
        Heart hole = g.Hole;
        float far = N.Vector3.Distance(hole.Position, stars.View.Eye);
        float outer = MathF.Atan(hole.Outer / far), shadow = MathF.Asin(hole.Shadow / far);
        const int Size = 256;
        // From the core the disc fills the middle of the picture; from the rim the view is the game's own.
        float fov = disc ? 2f * MathF.Atan(MathF.Tan(outer) * 2.6f) : Mathf.DegToRad(75f);
        material.SetShaderParameter("galaxy_pixel", 2f * MathF.Tan(fov * 0.5f) / Size);
        var view = new SubViewport { OwnWorld3D = true, Size = new Vector2I(Size, Size), RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        var camera = new Camera3D
        {
            Fov = Mathf.RadToDeg(fov), Near = 0.1f, Far = 100f,
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Sky, Sky = new Sky { SkyMaterial = material, RadianceSize = Sky.RadianceSizeEnum.Size32 },
                TonemapMode = Godot.Environment.ToneMapper.Linear, AmbientLightSource = Godot.Environment.AmbientSource.Disabled, ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
            },
        };
        view.AddChild(camera);
        AddChild(view);
        camera.MakeCurrent();
        N.Vector3 h = stars.ToHole;
        camera.GlobalBasis = SkySeams.LookAlong(new Vector3(h.X, h.Y, h.Z));
        await Frames(6);
        Image image = view.GetTexture().GetImage();
        RemoveChild(view);
        view.QueueFree();
        string keep = System.Array.Find(OS.GetCmdlineUserArgs(), a => a.StartsWith("--keep="))?["--keep=".Length..];
        if (keep != null) image.SavePng(System.IO.Path.Combine(keep, disc ? "heart-core.png" : "heart-rim.png"));
        float Pixels(float angle) => MathF.Tan(angle) / MathF.Tan(fov * 0.5f) * Size * 0.5f;
        float discPx = Pixels(outer), shadowPx = Pixels(shadow);
        var inShadow = new List<float>();
        var round = new List<float>();
        float brightest = 0f;
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                Color c = image.GetPixel(x, y).SrgbToLinear();
                float l = c.R + c.G + c.B, r = new Vector2(x + 0.5f - Size * 0.5f, y + 0.5f - Size * 0.5f).Length();
                if (disc && r < shadowPx * 0.9f) inShadow.Add(l);
                if (r <= MathF.Max(discPx, 3f)) brightest = MathF.Max(brightest, l);
                if (r >= (disc ? discPx * 1.8f : 12f) && r <= (disc ? discPx * 2.4f : 24f)) round.Add(l);
            }
        float sky = Median(round);
        string at = $"{g.Name(home)}, {far:F0} LY from the hole (depth {g.DepthOf(home):0.00})";
        if (disc)
        {
            // The disc's near side can cross in front of part of the shadow (it is nearer): the shadow's darker part.
            inShadow.Sort();
            float dark = inShadow.Count == 0 ? 1f : inShadow[inShadow.Count / 4];
            // What the sky there would be with no hole: the glow straight toward it, at the picture's scale.
            var (face, fs, ft) = SkyView.Face(h);
            int gx = Math.Clamp((int)((fs * 0.5f + 0.5f) * SkyView.GlowSize), 0, SkyView.GlowSize - 1), gy = Math.Clamp((int)((ft * 0.5f + 0.5f) * SkyView.GlowSize), 0, SkyView.GlowSize - 1), gk = (gx + gy * SkyView.GlowSize) * 3;
            float open = (stars.View.Glow[face][gk] + stars.View.Glow[face][gk + 1] + stars.View.Glow[face][gk + 2]) * 0.2f;
            // The core's light in front of the hole still shows in its shadow (about half of it): the
            // centre is dark against the disc, and darker than the sky would be there without it.
            Check(brightest >= sky * 3f && brightest > 0.3f && dark <= brightest * 0.25f && dark <= open * 0.75f && shadowPx >= 3f,
                $"from {at} the hole is a disc {2f * Mathf.RadToDeg(outer):F1} degrees across: at its brightest {brightest:F2} against the sky round it {sky:F2}, its dark centre {dark:F2} ({2f * shadowPx:F0} pixels across) where the sky would be {open:F2} without it");
        }
        else Check(brightest >= sky * 1.5f && brightest > 0.05f && discPx < 3f,
            $"from {at} it is a point in the bulge ({2f * Mathf.RadToDeg(outer) * 60f:F0} arcminutes): {brightest:F2} against the bulge round it {sky:F2}");
    }

    // ---- Eclipses -------------------------------------------------------------------------------------

    /// <summary>
    /// A real eclipse, found in a fresh world's own system from where its sky stands, the sun up:
    /// at its deepest the sun's light is down by the share of its disc hidden, the sky's own
    /// reckoning of that share is the system's, and the day darkens with it.
    /// </summary>
    async Task Eclipse()
    {
        var stage = new SubViewport { OwnWorld3D = true, Size = new Vector2I(4, 4), RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        AddChild(stage);
        var rig = new SkyRig { Name = "Sky" };
        stage.AddChild(rig);
        bool found = false;
        for (int attempt = 0; attempt < 6 && !found; attempt++)
        {
            PlanetWorld world = PlanetWorld.Build(PlanetSpec.Roll((ulong)System.Random.Shared.NextInt64(), Archetype.Temperate, 0), Main.Catalog);
            rig.SetWorld(world);
            Pose(rig, world, 0.5, 0f);
            var (when, covered) = rig.NextEclipse(240.0);
            GD.Print($"(eclipse: {world.Spec}: the deepest in 240 days hides {covered:P0} of the sun, day {when:F3})");
            if (covered < 0.2) continue;
            found = true;
            // At its deepest, and on its way in with about half the disc hidden.
            double half = when;
            while (half > when - 0.5 && world.StarSystem.Eclipse(half, rig.Eye).Covered > covered * 0.5) half -= 0.0002;
            foreach (double at in new[] { when, half })
            {
                double hidden = world.StarSystem.Eclipse(at, rig.Eye).Covered;
                rig.Days = at;
                SkyRig.Eclipses = false;
                rig.Apply();
                float open = rig.Sun.LightEnergy, daylight = rig.Daylight;
                SkyRig.Eclipses = true;
                rig.Apply();
                float dimmed = rig.Sun.LightEnergy;
                double expected = 1.0 - hidden;
                Check(open > 0.01f && MathF.Abs(dimmed / open - (float)expected) < 0.02f && MathF.Abs(rig.SunLit - (float)expected) < 0.01f,
                    $"at an eclipse hiding {hidden:P1} of the sun ({world.Spec.Archetype} world, day {at:F4}, its daylight {daylight:F2}), its light falls from {open:F3} to {dimmed:F3}: {dimmed / Math.Max(open, 1e-6f):P1} of it, as {expected:P1} of the disc is in sight");
            }
        }
        if (!found) Fail("no eclipse of a fifth of the sun or more, with the sun up, in 240 days of six fresh worlds");
        RemoveChild(stage);
        stage.QueueFree();
        await Frames(1);
    }

    // ---- Bodies drawn whole -----------------------------------------------------------------------

    /// <summary>
    /// A ringed giant as the sky draws it is whole at any size, lit or on its night side: drawn
    /// with no giant and with it, the whole of its disc differs (nothing of the sphere is missing,
    /// even dark against the night), nothing past its ring does, and its ring crosses in front of
    /// the near half of the disc and is hidden behind the far half.
    /// </summary>
    async Task WholeBodies()
    {
        var stage = new SubViewport { OwnWorld3D = true, Size = new Vector2I(4, 4), RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        AddChild(stage);
        var rig = new SkyRig { Name = "Sky" };
        stage.AddChild(rig);
        PlanetWorld world = PlanetWorld.Build(PlanetSpec.Roll(7, Archetype.Temperate, 0), Main.Catalog);
        rig.SetWorld(world);
        rig.Stars.Finish();
        Pose(rig, world, 0.0, 0f);
        await Frames(4);
        var sky = (ShaderMaterial)rig.Environment.Sky.SkyMaterial;
        // A plain night behind it: the star field's own colours would match a band of the giant's here and there.
        sky.SetShaderParameter("galaxy_on", false);
        Vector3 at = new Vector3(0.25f, 0.85f, -0.45f).Normalized();
        Basis view = SkySeams.LookAlong(at);
        Vector3 up = view.Y.Normalized();
        const float Tilt = 0.44f; // the ring plane seen 25 degrees open: its near side below the middle
        Vector3 ringAxis = (up * MathF.Cos(Tilt) - at * MathF.Sin(Tilt)).Normalized();
        Vector3 side = view.X.Normalized();
        sky.SetShaderParameter("moon_count", 0);
        sky.SetShaderParameter("giant_at", 0);
        sky.SetShaderParameter("giant_layer", -1);
        sky.SetShaderParameter("giant_color", new Color(0.78f, 0.6f, 0.42f));
        sky.SetShaderParameter("giant_band", new Color(0.6f, 0.42f, 0.3f));
        sky.SetShaderParameter("ring_color", new Color(0.82f, 0.76f, 0.66f));
        sky.SetShaderParameter("ring_normal", ringAxis);
        sky.SetShaderParameter("giant_dir", at);
        const int Size = 192;
        async Task<float[]> Picture(float fov)
        {
            var camera = new Camera3D
            {
                Fov = Mathf.RadToDeg(fov), Near = 0.1f, Far = 1000f,
                Environment = new Godot.Environment
                {
                    BackgroundMode = Godot.Environment.BGMode.Sky, Sky = new Sky { SkyMaterial = sky, RadianceSize = Sky.RadianceSizeEnum.Size32 },
                    BackgroundEnergyMultiplier = 0.5f, TonemapMode = Godot.Environment.ToneMapper.Linear,
                    AmbientLightSource = Godot.Environment.AmbientSource.Disabled, ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
                },
            };
            stage.Size = new Vector2I(Size, Size);
            stage.AddChild(camera);
            camera.Position = new Vector3(0f, 120f, 0f);
            camera.MakeCurrent();
            camera.GlobalBasis = view;
            await Frames(6);
            Image image = stage.GetTexture().GetImage();
            stage.RemoveChild(camera);
            camera.QueueFree();
            var light = new float[Size * Size];
            for (int i = 0; i < light.Length; i++)
            {
                Color c = image.GetPixel(i % Size, i / Size).SrgbToLinear();
                light[i] = c.R + c.G + c.B;
            }
            return light;
        }
        foreach (var (what, radius, night) in new[] { ("near, lit", 0.08f, false), ("near, on its night side", 0.08f, true), ("far, lit", 0.012f, false), ("far, on its night side", 0.012f, true) })
        {
            // Its light from beside and in front, or from behind it (a thin lit crescent at most).
            Vector3 light = night ? (at * 0.9f + side * 0.3f).Normalized() : (-at * 0.6f + side * 0.8f).Normalized();
            sky.SetShaderParameter("giant_radius", radius);
            sky.SetShaderParameter("giant_light", light);
            float fov = 2f * MathF.Atan(MathF.Tan(radius * 2.42f) / 0.7f);
            float discPx = MathF.Tan(radius) / MathF.Tan(fov * 0.5f) * Size * 0.5f;
            sky.SetShaderParameter("giant", false);
            float[] none = await Picture(fov);
            sky.SetShaderParameter("giant", true);
            sky.SetShaderParameter("rings", false);
            float[] bare = await Picture(fov);
            sky.SetShaderParameter("rings", true);
            float[] ringed = await Picture(fov);
            int inDisc = 0, drawn = 0, outside = 0, stray = 0, nearRing = 0, farRing = 0;
            for (int i = 0; i < none.Length; i++)
            {
                float x = i % Size + 0.5f - Size * 0.5f, y = i / Size + 0.5f - Size * 0.5f, r = MathF.Sqrt(x * x + y * y);
                bool differs = MathF.Abs(ringed[i] - none[i]) > 0.002f;
                if (r < discPx * 0.95f)
                {
                    inDisc++;
                    if (differs) drawn++;
                    // The ring where it crosses the disc: only on its near side, below the middle.
                    if (r < discPx * 0.9f && MathF.Abs(ringed[i] - bare[i]) > 0.004f)
                    {
                        if (y > 0f) nearRing++;
                        else farRing++;
                    }
                }
                else if (r > discPx * 2.6f && r < Size * 0.5f)
                {
                    outside++;
                    if (differs) stray++;
                }
            }
            Check(drawn >= inDisc * 0.985f && stray <= outside / 200 && nearRing > inDisc / 20 && farRing <= nearRing / 20,
                $"a ringed giant {what}, {2f * Mathf.RadToDeg(radius):F1} degrees across, is whole: {drawn} of {inDisc} pixels of its disc drawn, {stray} stray past its ring; its ring crosses its near half ({nearRing} pixels) and hides behind its far half ({farRing})");
        }
        RemoveChild(stage);
        stage.QueueFree();
    }

    /// <summary>The sun's size from each kind of world, with the star its kind has: a scorched world's sun big and fierce, an ice world's small, neither a wall nor a dot.</summary>
    void SunBands()
    {
        foreach (var (type, least, most) in new[] { (Archetype.Lava, 6f, 15f), (Archetype.Arid, 2.8f, 7f), (Archetype.Temperate, 2.4f, 4.6f), (Archetype.Ice, 0.9f, 3f) })
        {
            var sizes = new List<float>();
            for (int i = 0; i < 24; i++)
            {
                PlanetSpec spec = PlanetSpec.Roll((ulong)System.Random.Shared.NextInt64(), type, System.Random.Shared.Next(PlanetSpec.VariantCount));
                SkySystem system = SkySystem.For(spec);
                sizes.Add(2f * Mathf.RadToDeg(StarSystem.For(spec, system, 16000.0).SunAngular));
            }
            Check(sizes.All(w => w >= least && w <= most), $"a {type} world's sun is {sizes.Min():F1} to {sizes.Max():F1} degrees across over 24 worlds (between {least:F1} and {most:F1})");
        }
    }

    // ---- The sun's size ------------------------------------------------------------------------------

    /// <summary>
    /// The sun's disc where its light falls to half, radians, rendered looking straight at it
    /// through <paramref name="sky"/> (light 0 is the sun): linear light, no bloom, the sky itself
    /// turned down far enough that nothing clips (the 3D buffer tops out near 2, before exposure),
    /// so the disc's own edge is what is measured. The air's glow round it is the background its
    /// half is taken over.
    /// </summary>
    async Task<float> SunWidth(SubViewport stage, Material sky, Vector3 toSun, float fov)
    {
        const int Size = 256;
        var camera = new Camera3D
        {
            Fov = Mathf.RadToDeg(fov), Near = 0.1f, Far = 1000f,
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Sky, Sky = new Sky { SkyMaterial = sky, RadianceSize = Sky.RadianceSizeEnum.Size32 },
                BackgroundEnergyMultiplier = 0.02f, TonemapMode = Godot.Environment.ToneMapper.Linear,
                AmbientLightSource = Godot.Environment.AmbientSource.Disabled, ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
            },
        };
        stage.Size = new Vector2I(Size, Size);
        stage.AddChild(camera);
        camera.Position = new Vector3(0f, 120f, 0f);
        camera.MakeCurrent();
        camera.GlobalBasis = SkySeams.LookAlong(toSun);
        await Frames(8);
        Image image = stage.GetTexture().GetImage();
        stage.RemoveChild(camera);
        camera.QueueFree();
        var light = new float[Size * Size];
        var edge = new List<float>();
        float peak = 0f;
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                Color c = image.GetPixel(x, y).SrgbToLinear();
                float l = c.R + c.G + c.B;
                light[x + y * Size] = l;
                float r = new Vector2(x + 0.5f - Size * 0.5f, y + 0.5f - Size * 0.5f).Length();
                if (r < Size * 0.1f) peak = MathF.Max(peak, l);
                if (r > Size * 0.45f && r < Size * 0.5f) edge.Add(l);
            }
        float floor = Median(edge), half = floor + (peak - floor) * 0.5f;
        int inside = 0;
        for (int i = 0; i < light.Length; i++)
            if (light[i] >= half && new Vector2(i % Size + 0.5f - Size * 0.5f, i / Size + 0.5f - Size * 0.5f).Length() < Size * 0.4f) inside++;
        float pixels = MathF.Sqrt(inside / MathF.PI);
        if (peak >= 2.9f) GD.Print("(sun: the disc clipped in the measuring picture)");
        GD.Print($"(sun: measured over a background of {floor:F3} to a peak of {peak:F3})");
        return MathF.Atan(pixels * MathF.Tan(fov * 0.5f) / (Size * 0.5f));
    }

    /// <summary>A hard-edged disc of a known radius round light 0: what the sun's measure is checked against.</summary>
    const string KnownDisc = """
        shader_type sky;
        uniform float radius;
        void sky() { COLOR = vec3(dot(EYEDIR, LIGHT0_DIRECTION) >= cos(radius) ? 20.0 : 0.05); }
        """;

    /// <summary>A picture of the sun at the game's own tone and bloom, for the eye: <paramref name="env"/> with <paramref name="sky"/> in it.</summary>
    async Task KeepSun(SubViewport stage, Material sky, Godot.Environment env, Vector3 toSun, float fov, string file)
    {
        string keep = System.Array.Find(OS.GetCmdlineUserArgs(), a => a.StartsWith("--keep="))?["--keep=".Length..];
        if (keep == null) return;
        var look = (Godot.Environment)env.Duplicate();
        look.Sky = new Sky { SkyMaterial = sky, RadianceSize = Sky.RadianceSizeEnum.Size32 };
        var camera = new Camera3D { Fov = Mathf.RadToDeg(fov), Near = 0.1f, Far = 1000f, Environment = look };
        stage.Size = new Vector2I(512, 512);
        stage.AddChild(camera);
        camera.Position = new Vector3(0f, 120f, 0f);
        camera.MakeCurrent();
        camera.GlobalBasis = SkySeams.LookAlong(toSun);
        await Frames(8);
        stage.GetTexture().GetImage().SavePng(System.IO.Path.Combine(keep, file));
        stage.RemoveChild(camera);
        camera.QueueFree();
    }

    /// <summary>
    /// The sun on the ground in clear air with the sun well up, at the top of the climb (the same
    /// sky with its air gone and space's backdrop drawn, as the climb leaves it), and in orbit
    /// (space's own sky shader, its sun set up as the orbit's is): one zoom, one disc.
    /// </summary>
    async Task Sun()
    {
        var stage = new SubViewport { OwnWorld3D = true, Size = new Vector2I(256, 256), RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        AddChild(stage);
        var rig = new SkyRig { Name = "Sky" };
        stage.AddChild(rig);
        PlanetWorld world = PlanetWorld.Build(PlanetSpec.Roll((ulong)System.Random.Shared.NextInt64(), Archetype.Temperate, 0), Main.Catalog);
        rig.SetWorld(world);
        Pose(rig, world, 0.5, 0f);
        double time = 0.5;
        float highest = rig.SunAt(time).Y;
        for (double t = 0.30; t < 0.70; t += 0.02)
            if (rig.SunAt(t).Y > highest) (time, highest) = (t, rig.SunAt(t).Y);
        Pose(rig, world, time, 0f);
        await Frames(4);
        float truth = world.StarSystem.SunAngular;
        // Where its light falls to half: the shader's edge runs from its radius out to 1.15 of it.
        float expected = MathF.Acos((MathF.Cos(truth) + MathF.Cos(truth * 1.15f)) * 0.5f);
        float zoom = 2f * MathF.Atan(MathF.Tan(expected) * 3.2f), look = 2f * MathF.Atan(MathF.Tan(truth) * 14f);
        var ground = (ShaderMaterial)rig.Environment.Sky.SkyMaterial;
        Vector3 toSun = rig.ToSun;
        ground.SetShaderParameter("sun_measure", true);
        float onGround = await SunWidth(stage, ground, toSun, zoom);
        ground.SetShaderParameter("sun_measure", false);
        await KeepSun(stage, ground, rig.Environment, toSun, look, "sun-ground.png");
        // The top of the climb: the slice's seam sets just these as the air goes.
        ground.SetShaderParameter("space_turn", rig.SkyToStars);
        ground.SetShaderParameter("space_sun", rig.StarInField);
        ground.SetShaderParameter("space_fade", 1f);
        ground.SetShaderParameter("sun_measure", true);
        float climbing = await SunWidth(stage, ground, toSun, zoom);
        ground.SetShaderParameter("sun_measure", false);
        await KeepSun(stage, ground, rig.Environment, toSun, look, "sun-climb.png");
        ground.SetShaderParameter("space_fade", 0f);
        // Orbit: space's own sky, its sun set up as SpaceSky sets it up, under the same light.
        var space = new ShaderMaterial { Shader = Planets.Shaders.Space };
        SkyRig.ShowSun(space, world.StarSystem, world.Sky);
        space.SetShaderParameter("sun_measure", true);
        float inOrbit = await SunWidth(stage, space, toSun, zoom);
        space.SetShaderParameter("sun_measure", false);
        await KeepSun(stage, space, rig.Environment, toSun, look, "sun-orbit.png");
        // A hard-edged disc exactly as wide, measured the same way: the picture's own bias.
        var known = new ShaderMaterial { Shader = new Shader { Code = KnownDisc } };
        known.SetShaderParameter("radius", expected);
        float reference = await SunWidth(stage, known, toSun, zoom);
        RemoveChild(stage);
        stage.QueueFree();
        float[] all = { onGround, climbing, inOrbit };
        float most = all.Max(), least = all.Min();
        string widths = $"on the ground {2f * Mathf.RadToDeg(onGround):F3} degrees, at the top of the climb {2f * Mathf.RadToDeg(climbing):F3}, in orbit {2f * Mathf.RadToDeg(inOrbit):F3}";
        Check(most / least < 1.04f, $"the sun's disc is one size all the way up, the sun {Mathf.RadToDeg(MathF.Asin(highest)):F0} degrees up: {widths} (within {most / least - 1f:P1})");
        Check(all.All(w => MathF.Abs(w / reference - 1f) < 0.04f),
            $"and that size is the star's real radius over the world's distance from it: a disc drawn exactly {2f * Mathf.RadToDeg(expected):F3} degrees across where its light halves measures {2f * Mathf.RadToDeg(reference):F3} (its radius {Mathf.RadToDeg(truth):F3} degrees)");
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
