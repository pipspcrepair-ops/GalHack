using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;
using PlanetSurface.Skies;

namespace Slice.Tests;

/// <summary>
/// Shadow edges by numbers (the owner, 22 Nov: "pixelation around edges of shadows; flickering around edges of shadows;
/// flickering of shadows"), at a dusk sun (6 degrees: long grazing shadows) and a day sun (25), standing on the ground and 25 m over it looking across the sun, and hovering 40 m over the trees. Judge it on bare land (--type=dune, or a barren world):
/// <list type="bullet">
/// <item>where the edges are: each view drawn with the sun's shadows and the far shade, then without either; a pixel's
/// shadow is the one over the other, and an edge pixel is one whose shadow changes by 0.12 or more across its 3 x 3
/// neighbours. Pixels that change by themselves with no shadows at all (grass dither, water, sky) are left out;</item>
/// <item>steps: the share of edge pixels whose whole change happens between two neighbours (a hard stair, where a
/// filtered edge ramps over several pixels);</item>
/// <item>still: everything held, frame after frame: edge pixels changing (more than 8 of 255) a frame. Each of these measures runs first with no shadows, and a pixel that changes then (grass, water, the ground's own shading as the sun moves, what a move streams in) is left out of it;</item>
/// <item>sun: the view held while the sun moves on a frame at a time at the game's own pace, 1.5 s: edge pixels
/// changing a frame, and the shadows' inside pixels likewise (a whole shadow wavering);</item>
/// <item>move: from the view, a step, a walk, a turn, a look down or a climb away and straight back: edge pixels that
/// differ from before. Nothing in the world moved, so it is the shadow map refitted to
/// where the eye has been.</item>
/// </list>
/// --variants=a|b|...: the whole measure once for each, each a list of shadow settings over the player's (filter=0-5,
/// atlas=N, bits=16|32, blur=X, nbias=X, bias=X, pancake=X); the GPU's time a frame is printed with each. Frames go to --out-dir (user://edges),
/// with a close-up of the same busiest patch in every variant.
/// </summary>
public partial class PopRoute
{
    /// <summary>A pixel's change that counts, as a share of full brightness.</summary>
    const float EdgeChange = 8f / 255f;

    /// <summary>Limits for --check, %: edge pixels changing a frame as the sun moves and after a move away and back; hard steps of edge pixels.</summary>
    const double SunFlickerMost = double.NaN, MoveFlickerMost = double.NaN, StepsMost = double.NaN;

    IEnumerable<int> Edges(Vector3 start)
    {
        var zone = _game.Zone;
        string dir = _args.GetValueOrDefault("out-dir", ProjectSettings.GlobalizePath("user://edges"));
        DirAccess.MakeDirRecursiveAbsolute(dir);
        var layers = GetTree().Root.FindChildren("*", nameof(CanvasLayer), true, false).Cast<CanvasLayer>().Where(l => l.Visible).ToList();
        foreach (CanvasLayer l in layers) l.Visible = false;
        // The roughest way from the start (the most rise and fall 50 to 400 m out): ridges and dunes casting across it.
        Vector3 rough = Vector3.Forward;
        float most = -1f;
        for (int k = 0; k < 24; k++)
        {
            var d = new Vector3(MathF.Cos(k * MathF.Tau / 24), 0f, MathF.Sin(k * MathF.Tau / 24));
            float[] h = Enumerable.Range(1, 36).Select(i => zone.GroundAt(start.X + d.X * (40f + i * 10f), start.Z + d.Z * (40f + i * 10f)).Y).ToArray();
            float rise = h.Zip(h.Skip(1), (p, q) => MathF.Abs(q - p)).Sum();
            if (rise > most) (most, rough) = (rise, d);
        }
        // The sleeper held out of view casts nothing: its shadow would stray into the frame, and move as it settles.
        var figure = _game.Figure.FindChildren("*", nameof(GeometryInstance3D), true, false).Cast<GeometryInstance3D>().ToDictionary(g => g, g => g.CastShadow);
        foreach (GeometryInstance3D g in figure.Keys) g.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        foreach (string variant in _args.GetValueOrDefault("variants", "as set").Split('|'))
        {
            _game.ApplySettings();
            Vary(variant);
            foreach (int x in EdgeViews(start, rough, Densest(start), variant, dir)) yield return x;
        }
        foreach (var (g, cast) in figure) if (IsInstanceValid(g)) g.CastShadow = cast;
        zone.Sky.TimeScale = 1f;
        _game.FixedCamera = null;
        foreach (CanvasLayer l in layers) l.Visible = true;
    }

    /// <summary>A variant's shadow settings over the player's.</summary>
    void Vary(string variant)
    {
        DirectionalLight3D sun = _game.Zone.Sky.Sun;
        foreach (string part in variant.Split(','))
        {
            string[] kv = part.Split('=');
            if (kv.Length != 2) continue;
            float v = float.Parse(kv[1], CultureInfo.InvariantCulture);
            switch (kv[0].Trim())
            {
                case "filter": RenderingServer.DirectionalSoftShadowFilterSetQuality((RenderingServer.ShadowQuality)(int)v); break;
                case "atlas": RenderingServer.DirectionalShadowAtlasSetSize((int)v, true); break;
                case "bits": RenderingServer.DirectionalShadowAtlasSetSize(SunShadows.For(_game.Settings.Shadows).SunAtlas, v < 24f); break;
                case "blur": sun.ShadowBlur = v; break;
                case "nbias": sun.ShadowNormalBias = v; break;
                case "bias": sun.ShadowBias = v; break;
                case "pancake": sun.DirectionalShadowPancakeSize = v; break;
            }
        }
    }

    IEnumerable<int> EdgeViews(Vector3 start, Vector3 rough, Vector3 trees, string variant, string dir)
    {
        var zone = _game.Zone;
        var sky = zone.Sky;
        DirectionalLight3D sun = sky.Sun;
        sky.TimeScale = 0f;
        double frame = 1.0 / (sky.System.DayMinutes * 60.0 * 60.0); // a frame of the day at 60 frames a second
        var sums = new Dictionary<string, List<double>> { ["still"] = new(), ["sun"] = new(), ["move"] = new(), ["steps"] = new() };
        foreach (float want in new[] { 6f, 25f })
        {
            double t0 = 0.15;
            for (double t = 0.15; t < 0.5; t += 0.001)
            {
                sky.TimeOfDay = t0 = t;
                sky.Apply();
                if (MathF.Asin(sun.GlobalBasis.Z.Y) * 180f / MathF.PI >= want) break;
            }
            Vector3 toSun = new Vector3(sun.GlobalBasis.Z.X, 0f, sun.GlobalBasis.Z.Z).Normalized(), across = toSun.Cross(Vector3.Up);
            // Across the sun (long shadows lie across the view), toward the rougher side; then over the land, and over the trees.
            Vector3 side0 = across.Dot(rough) < 0f ? -across : across;
            foreach (var (view, eye, look, pitch) in new[] { ("walk", Lift(start, 1.7f), side0, -6f), ("land", Lift(start, 25f), side0, -15f), ("hover", Lift(trees - across * 60f, 40f), across, -25f) })
            {
                Vector3 side = look.Cross(Vector3.Up).Normalized();
                var pose = new Transform3D(Basis.LookingAt(look, Vector3.Up).Rotated(side, Mathf.DegToRad(pitch)), eye);
                void Stand(Transform3D at) => _game.Survivor.PlaceOutside((at.Origin - look * 5f + Vector3.Down * 1.6f).ToNumerics());
                for (int i = 0; i < 2400 && (i < 180 || !zone.Streamer.Settled); i++)
                {
                    Stand(pose);
                    _game.FixedCamera = pose;
                    sky.TimeOfDay = t0;
                    yield return 0;
                }
                float[] shot = null;
                // One frame of a pose, with the shadows or without (the sun's casters off, the far shade out past reach).
                IEnumerable<int> Shoot(Transform3D at, bool shadows)
                {
                    sun.ShadowCasterMask = shadows ? uint.MaxValue : 0u;
                    float reach = zone.Streamer.ShadowReach;
                    if (!shadows) zone.Streamer.ShadowReach = 1e7f;
                    for (int i = 0; i < 3; i++)
                    {
                        Stand(at);
                        _game.FixedCamera = at;
                        yield return 0;
                    }
                    shot = Luma(GetViewport().GetTexture().GetImage(), out _w, out _h);
                    zone.Streamer.ShadowReach = reach;
                    sun.ShadowCasterMask = uint.MaxValue;
                }
                foreach (int x in Shoot(pose, true)) yield return x;
                float[] lit = shot;
                Image picture = GetViewport().GetTexture().GetImage();
                // Pixels that change by themselves: no shadows, nothing moving, six frames.
                float[] bare = null;
                var noisy = new bool[lit.Length];
                for (int k = 0; k < 6; k++)
                {
                    foreach (int x in Shoot(pose, false)) yield return x;
                    if (bare != null)
                        for (int i = 0; i < noisy.Length; i++) noisy[i] |= MathF.Abs(shot[i] - bare[i]) > EdgeChange;
                    bare = shot;
                }
                int w = _w, h = _h;
                var shade = new float[w * h];
                for (int i = 0; i < shade.Length; i++) shade[i] = bare[i] < 0.02f ? 1f : Math.Clamp(lit[i] / bare[i], 0f, 1f);
                var edge = new bool[w * h];
                var inside = new bool[w * h];
                int edges = 0, hard = 0, insides = 0;
                for (int y = 1; y < h - 1; y++)
                    for (int x = 1; x < w - 1; x++)
                    {
                        int i = y * w + x;
                        if (noisy[i]) continue;
                        float lo = 1f, hi = 0f, jump = 0f;
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                float s = shade[i + dy * w + dx];
                                lo = MathF.Min(lo, s);
                                hi = MathF.Max(hi, s);
                                if (dx * dy == 0) jump = MathF.Max(jump, MathF.Abs(s - shade[i]));
                            }
                        if (hi - lo >= 0.12f)
                        {
                            edge[i] = true;
                            edges++;
                            if (jump >= 0.8f * (hi - lo)) hard++;
                        }
                        else if (hi < 0.7f) (inside[i], insides) = (true, insides + 1);
                    }
                // Each measure runs first with no shadows, marking every pixel that changes by itself then (grass, water, the
                // ground's own shading as the sun moves, what a move streams in); then with them, counting only the rest.
                bool[] moving = null;
                double Changed(float[] a, float[] c, bool[] where)
                {
                    int n = 0, of = 0;
                    for (int i = 0; i < a.Length; i++)
                    {
                        if (!where[i] || moving[i]) continue;
                        of++;
                        if (MathF.Abs(a[i] - c[i]) > EdgeChange) n++;
                    }
                    return of == 0 ? 0 : 100.0 * n / of;
                }
                void Mark(float[] a, float[] c)
                {
                    for (int i = 0; i < a.Length; i++) moving[i] |= MathF.Abs(a[i] - c[i]) > EdgeChange;
                }
                // Everything still; then the sun moving on a frame at a time, the view still.
                double still = 0, sunEdge = 0, sunInside = 0, gpu = 0;
                foreach (bool sunMoves in new[] { false, true })
                {
                    moving = new bool[w * h];
                    for (int pass = 1; pass >= 0; pass--)
                    {
                        float[] last = null;
                        int n = sunMoves ? 90 : 20, from = _gpu.Count;
                        for (int k = 0; k <= n; k++)
                        {
                            sky.TimeOfDay = t0 + (sunMoves ? k * frame : 0.0);
                            foreach (int x in Shoot(pose, pass == 0)) yield return x;
                            if (last != null)
                            {
                                if (pass == 1) Mark(shot, last);
                                else if (!sunMoves) still += Changed(shot, last, edge) / n;
                                else
                                {
                                    sunEdge += Changed(shot, last, edge) / n;
                                    sunInside += Changed(shot, last, inside) / n;
                                }
                            }
                            last = shot;
                        }
                        if (pass == 0 && sunMoves) gpu = _gpu.Skip(from).Average(); // the frame's GPU time with the shadows, ms
                    }
                }
                sky.TimeOfDay = t0;
                // Away and straight back.
                Basis Turn(float yaw, float down) => pose.Basis.Rotated(Vector3.Up, Mathf.DegToRad(yaw)).Rotated(side, Mathf.DegToRad(down));
                var aways = new (string Name, Transform3D At)[]
                {
                    ("step 0.5 m", pose.Translated(look * 0.5f)),
                    ("walk 4 m", pose.Translated(look * 4f)),
                    ("walk 16 m", pose.Translated(look * 16f)),
                    ("turn 10", new Transform3D(Turn(10f, 0f), eye)),
                    ("turn 45", new Transform3D(Turn(45f, 0f), eye)),
                    ("turn 120", new Transform3D(Turn(120f, 0f), eye)),
                    ("look down 30", new Transform3D(Turn(0f, -30f), eye)),
                    ("climb 10 m", pose.Translated(Vector3.Up * 10f)),
                };
                moving = new bool[w * h];
                double moved = 0, worst = 0;
                string worstName = "";
                for (int pass = 1; pass >= 0; pass--)
                {
                    float[] before = pass == 0 ? lit : bare;
                    foreach (var (name, at) in aways)
                    {
                        foreach (int x in Shoot(at, pass == 0)) yield return x;
                        foreach (int x in Shoot(pose, pass == 0)) yield return x;
                        if (pass == 1)
                        {
                            Mark(shot, before);
                            continue;
                        }
                        double c = Changed(shot, before, edge);
                        moved += c / aways.Length;
                        if (c > worst) (worst, worstName) = (c, name);
                    }
                }
                double steps = edges == 0 ? 0 : 100.0 * hard / edges;
                if (edges > w * h / 1000) // a view with next to no shadow in it says nothing
                {
                    sums["still"].Add(still);
                    sums["sun"].Add(sunEdge);
                    sums["move"].Add(moved);
                    sums["steps"].Add(steps);
                }
                string file = $"{dir}/edges-{Tidy(variant)}-{want:F0}-{view}";
                picture.SavePng(file + ".png");
                // The same close-up in every variant: the first one's busiest.
                if (!_closeUps.TryGetValue((want, view), out Rect2I close)) _closeUps[(want, view)] = close = Busiest(edge, w, h);
                Image near = picture.GetRegion(close);
                near.Resize(close.Size.X * 3, close.Size.Y * 3, Image.Interpolation.Nearest);
                near.SavePng(file + "-close.png");
                GD.Print(string.Create(CultureInfo.InvariantCulture,
                    $"ROUTE edges [{variant}] sun {want:F0} deg, {view}: edge pixels {100.0 * edges / (w * h):F2}% of {w}x{h} ({100.0 * noisy.Count(n => n) / noisy.Length:F1}% noisy left out), hard steps {steps:F1}%; GPU {gpu:F2} ms a frame; " +
                    $"still {still:F2}% a frame; sun moving {sunEdge:F2}% a frame, insides {sunInside:F2}%; " +
                    $"away and back {moved:F2}% (worst {worst:F2}% after {worstName}); {file}.png"));
            }
        }
        string Mean(string k) => sums[k].Count == 0 ? "-" : sums[k].Average().ToString("F2", CultureInfo.InvariantCulture);
        GD.Print($"ROUTE edges [{variant}], mean of the views: still {Mean("still")}% a frame, sun moving {Mean("sun")}% a frame, away and back {Mean("move")}%, hard steps {Mean("steps")}%");
        if (sums["sun"].Count == 0) yield break;
        Gate($"shadow edges [{variant}]: changing a frame as the sun moves", sums["sun"].Max(), SunFlickerMost, false, "%");
        Gate($"shadow edges [{variant}]: changed after a move away and back", sums["move"].Max(), MoveFlickerMost, false, "%");
        Gate($"shadow edges [{variant}]: hard steps", sums["steps"].Max(), StepsMost, false, "%");
    }

    static string Tidy(string variant) => new(variant.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

    int _w, _h;

    /// <summary>A frame's brightness a pixel, 0 to 1.</summary>
    static float[] Luma(Image img, out int w, out int h)
    {
        img.Convert(Image.Format.Rgb8);
        (w, h) = (img.GetWidth(), img.GetHeight());
        byte[] d = img.GetData();
        var l = new float[w * h];
        for (int i = 0; i < l.Length; i++) l[i] = (0.2126f * d[i * 3] + 0.7152f * d[i * 3 + 1] + 0.0722f * d[i * 3 + 2]) / 255f;
        return l;
    }

    readonly Dictionary<(float, string), Rect2I> _closeUps = new();

    /// <summary>The frame's busiest 320 x 180 of edges (shown three times over, a pixel a block: the stairs as they are).</summary>
    static Rect2I Busiest(bool[] edge, int w, int h)
    {
        const int W = 320, H = 180, Step = 20;
        int bx = 0, by = 0, best = -1;
        for (int y0 = 0; y0 + H <= h; y0 += Step)
            for (int x0 = 0; x0 + W <= w; x0 += Step)
            {
                int n = 0;
                for (int y = y0; y < y0 + H; y += 2)
                    for (int x = x0; x < x0 + W; x += 2)
                        if (edge[y * w + x]) n++;
                if (n > best) (best, bx, by) = (n, x0, y0);
            }
        return new Rect2I(bx, by, W, H);
    }
}
