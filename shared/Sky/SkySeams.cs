using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;

namespace GalHack.SkyCheck;

/// <summary>
/// Seams in a sky, judged the way a player sees them: the sky is rendered looking straight at the
/// directions where seams hide (the edges and corners of a cube's faces, the poles, the wrap of an
/// azimuth, the axes a lattice is aligned to) and each picture is searched for
/// - a <b>line</b> through the middle where neighbouring pixels jump far more than they change
///   anywhere round them (a cube face's edge, a texture's wrap, an <c>atan</c> meridian),
/// - a <b>pinch</b>: jumps crowding into the middle (a longitude-latitude map at its pole),
/// - <b>thinning stars</b>: fewer stars round an axis than elsewhere (a star lattice cut by the
///   sky's sphere, which leaves a hole and rings at each axis).
///
/// Headless runs draw nothing, so a gate scene that needs pictures starts itself again in a
/// window placed off-screen (<see cref="RelaunchWindowed"/>), and passes on what it prints and its
/// exit code. Every prototype with a sky of its own compiles this file in and has a
/// <c>tests/SkySeamTest.tscn</c>.
/// </summary>
public static class SkySeams
{
    /// <summary>A direction to look straight at, and what may hide there.</summary>
    public readonly record struct Probe(string Name, Vector3 Direction, bool Axis);

    /// <summary>
    /// What one picture showed: the worst line and pinch scores, and its stars (all of them, and
    /// those within <see cref="CentreRadius"/> of the middle, where a lattice's hole would be).
    /// </summary>
    public readonly record struct Look(string Probe, float Line, int LinePixels, float Pinch, int PinchPixels, float Swirl, int Stars, int CentreStars, bool Axis);

    /// <summary>The middle of a picture, as a share of its width: about 4.5° across a 30° view.</summary>
    public const float CentreRadius = 0.15f;

    /// <summary>A line or pinch scoring above this (times the typical jump density round it) is a seam.</summary>
    public const float SeamScore = 6f;

    /// <summary>...and it must be at least this many jumping pixels long or crowded, so a star or two never counts.</summary>
    public const int SeamPixels = 40;

    public const int Size = 192;
    public const float Fov = 30f;

    // ---- Where to look ------------------------------------------------------------------------

    /// <summary>
    /// The directions where seams hide, above the horizon (and the horizon itself): a cube's
    /// corners and edges, the zenith, the four axis directions, and the azimuth wrap behind (-Z),
    /// low and high.
    /// </summary>
    public static List<Probe> Probes(bool below = false)
    {
        var list = new List<Probe>();
        float[] ys = below ? new[] { 1f, -1f } : new[] { 1f };
        foreach (float y in ys)
        {
            foreach (int sx in new[] { -1, 1 })
                foreach (int sz in new[] { -1, 1 })
                    list.Add(new Probe($"corner {sx},{y},{sz}", new Vector3(sx, y, sz).Normalized(), false));
            foreach (var (x, z) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                list.Add(new Probe($"edge {x},{y},{z}", new Vector3(x, y, z).Normalized(), false));
            list.Add(new Probe($"pole {y}", new Vector3(0.001f, y, 0f).Normalized(), true));
        }
        foreach (var (x, z) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            list.Add(new Probe($"axis {x},0,{z}", new Vector3(x, 0.02f, z).Normalized(), true));
        foreach (var (x, z) in new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) })
            list.Add(new Probe($"edge {x},0,{z}", new Vector3(x, 0.02f, z).Normalized(), false));
        list.Add(new Probe("azimuth wrap, low", new Vector3(0f, 0.25f, -1f).Normalized(), false));
        list.Add(new Probe("azimuth wrap, high", new Vector3(0f, 1.5f, -1f).Normalized(), false));
        return list;
    }

    /// <summary>A camera basis looking along a direction, its up as near the world's as it can be.</summary>
    public static Basis LookAlong(Vector3 direction)
    {
        Vector3 up = MathF.Abs(direction.Y) > 0.99f ? Vector3.Forward : Vector3.Up;
        return Basis.LookingAt(direction, up);
    }

    // ---- Judging a picture --------------------------------------------------------------------

    /// <summary>Judges one picture taken looking straight at a probe's direction.</summary>
    public static Look Judge(Image image, Probe probe)
    {
        int w = image.GetWidth(), h = image.GetHeight();
        var l = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Color c = image.GetPixel(x, y);
                l[y * w + x] = 0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B;
            }

        // A jump: a step between neighbours far bigger than the steps round it (a smooth gradient
        // or a soft cloud edge changes steadily; a seam changes all at once).
        var jump = new bool[w * h];
        for (int y = 2; y < h - 2; y++)
            for (int x = 2; x < w - 3; x++)
            {
                int i = y * w + x;
                float dx = MathF.Abs(l[i + 1] - l[i]), dy = MathF.Abs(l[i + w] - l[i]);
                float around = (MathF.Abs(l[i - 1] - l[i - 2]) + MathF.Abs(l[i + 2] - l[i + 1]) + MathF.Abs(l[i - w] - l[i - 2 * w]) + MathF.Abs(l[i + 2 * w] - l[i + w])) * 0.25f;
                float d = MathF.Max(dx, dy);
                jump[i] = d > 0.008f && d > 4f * around + 0.003f;
            }

        // A line through the middle: jumping pixels along it, against the typical line.
        float cx = (w - 1) * 0.5f, cy = (h - 1) * 0.5f;
        var counts = new List<int>();
        for (int a = 0; a < 90; a++)
        {
            float angle = a * MathF.PI / 90f, ux = MathF.Cos(angle), uy = MathF.Sin(angle);
            int n = 0;
            for (float t = -cx + 3f; t <= cx - 3f; t += 1f)
            {
                bool hit = false;
                for (float o = -1f; o <= 1f && !hit; o += 1f)
                {
                    int x = (int)MathF.Round(cx + ux * t - uy * o), y = (int)MathF.Round(cy + uy * t + ux * o);
                    hit = x >= 0 && y >= 0 && x < w && y < h && jump[y * w + x];
                }
                if (hit) n++;
            }
            counts.Add(n);
        }
        int most = 0;
        foreach (int n in counts) most = Math.Max(most, n);
        var sorted = new List<int>(counts);
        sorted.Sort();
        float line = most / (sorted[sorted.Count / 2] + 3f);

        // A pinch: jumping pixels crowding into the middle, against their density elsewhere.
        int inside = 0, outside = 0, inArea = 0, outArea = 0;
        float r0 = w * 0.125f, r1 = w * 0.25f;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float r = MathF.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                if (r < r0) { inArea++; if (jump[y * w + x]) inside++; }
                else if (r > r1) { outArea++; if (jump[y * w + x]) outside++; }
            }
        float pinch = (inside / (float)inArea) / ((outside + 3f) / outArea);

        // A swirl: close round the middle, the colour changing almost only round it, not toward it.
        // A map wrapped round a pole changes with azimuth and hardly at all with latitude there, so
        // its streaks all point at the pole; any other pattern (a cloud's edge, a nebula's knot)
        // changes as much toward the middle as round it.
        float round = 0f, toward = 0f;
        for (int y = 1; y < h - 1; y++)
            for (int x = 1; x < w - 1; x++)
            {
                float dx = x - cx, dy = y - cy, r = MathF.Sqrt(dx * dx + dy * dy);
                if (r < 3f || r >= 14f) continue;
                float gx = (l[y * w + x + 1] - l[y * w + x - 1]) * 0.5f, gy = (l[(y + 1) * w + x] - l[(y - 1) * w + x]) * 0.5f;
                round += MathF.Abs((gy * dx - gx * dy) / r);
                toward += MathF.Abs((gx * dx + gy * dy) / r);
            }
        float swirl = round < 0.0015f * 560f ? 0f : round / (toward + 0.0005f * 560f);

        // Stars: small, sharp bright points over what is round them. (A nebula's glow is smooth
        // from one pixel to the next, so its brighter knots never count.)
        int stars = 0, centreStars = 0;
        for (int y = 3; y < h - 3; y++)
            for (int x = 3; x < w - 3; x++)
            {
                float v = l[y * w + x];
                if (v < 0.12f) continue;
                float ring = (l[y * w + x - 3] + l[y * w + x + 3] + l[(y - 3) * w + x] + l[(y + 3) * w + x]) * 0.25f;
                float near = (l[y * w + x - 1] + l[y * w + x + 1] + l[(y - 1) * w + x] + l[(y + 1) * w + x]) * 0.25f;
                if (v - ring < 0.08f || v - near < 0.025f) continue;
                bool peak = true;
                for (int dy = -1; dy <= 1 && peak; dy++)
                    for (int dx = -1; dx <= 1 && peak; dx++)
                        peak = (dx == 0 && dy == 0) || l[(y + dy) * w + x + dx] <= v;
                if (!peak) continue;
                stars++;
                if ((x - cx) * (x - cx) + (y - cy) * (y - cy) < CentreRadius * CentreRadius * w * w) centreStars++;
            }
        return new Look(probe.Name, line, most, pinch, inside, swirl, stars, centreStars, probe.Axis);
    }

    /// <summary>A swirl turning this many times faster near the middle than further out is a pole's pinch.</summary>
    public const float SwirlScore = 2.5f;

    /// <summary>Whether a look shows a seam: a line, a crowd of jumps or a swirl scoring high and big enough to see.</summary>
    public static bool Seam(Look look) =>
        (look.Line > SeamScore && look.LinePixels >= SeamPixels) || (look.Pinch > SeamScore && look.PinchPixels >= SeamPixels) || look.Swirl > SwirlScore;

    /// <summary>
    /// Whether stars thin out at the axes: the mean count in the middle of the pictures taken along
    /// an axis, against the middle of all the others. Null when there are too few stars to say (a
    /// day sky).
    /// </summary>
    public static float? AxisStarRatio(IEnumerable<Look> looks)
    {
        var axis = new List<float>();
        var other = new List<float>();
        foreach (Look k in looks) (k.Axis ? axis : other).Add(k.Stars);
        if (axis.Count == 0 || other.Count == 0) return null;
        // Against the typical picture elsewhere (the median, so one picture of a busy patch of
        // nebula or ringed giant doesn't decide it).
        other.Sort();
        float typical = other.Count % 2 == 1 ? other[other.Count / 2] : (other[other.Count / 2 - 1] + other[other.Count / 2]) * 0.5f;
        if (typical < 15f) return null;
        float mean = 0f;
        foreach (float a in axis) mean += a;
        return mean / axis.Count / typical;
    }

    public static string Describe(Look k) =>
        $"{k.Probe}: line {k.Line:F1} ({k.LinePixels} px), pinch {k.Pinch:F1} ({k.PinchPixels} px), swirl {k.Swirl:F1}, {k.Stars} stars ({k.CentreStars} in the middle)";

    // ---- Taking the pictures ------------------------------------------------------------------

    /// <summary>
    /// Renders each probe in a small viewport of its own that shares the host's world (its sky,
    /// fog and lights), from <paramref name="eye"/>, and judges it. <paramref name="keep"/> gets
    /// each picture, to save.
    /// </summary>
    public static async System.Threading.Tasks.Task<List<Look>> LookAround(Node host, Vector3 eye, IEnumerable<Probe> probes, Action<Probe, Image> keep = null)
    {
        var view = new SubViewport { Size = new Vector2I(Size, Size), RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        var camera = new Camera3D { Fov = Fov, Near = 0.1f, Far = 20000f };
        view.AddChild(camera);
        host.AddChild(view);
        camera.MakeCurrent();
        camera.GlobalPosition = eye;
        var looks = new List<Look>();
        foreach (Probe p in probes)
        {
            camera.GlobalBasis = LookAlong(p.Direction);
            for (int i = 0; i < 4; i++) await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Image image = view.GetTexture().GetImage();
            keep?.Invoke(p, image);
            looks.Add(Judge(image, p));
        }
        host.RemoveChild(view);
        view.QueueFree();
        return looks;
    }

    /// <summary>Prints every look's verdict and returns how many showed a seam, plus a star-thinning failure.</summary>
    public static int Report(string sky, List<Look> looks)
    {
        int failures = 0;
        foreach (Look k in looks)
        {
            bool seam = Seam(k);
            if (seam) failures++;
            GD.Print($"{(seam ? "FAIL" : "PASS")}  {sky}, looking at {Describe(k)}");
        }
        if (AxisStarRatio(looks) is float ratio)
        {
            bool even = ratio > 0.6f && ratio < 1.6f;
            if (!even) failures++;
            GD.Print($"{(even ? "PASS" : "FAIL")}  {sky}: stars as dense along the axes as anywhere else ({ratio:P0} of elsewhere)");
        }
        return failures;
    }

    // ---- Running windowed from a headless gate ------------------------------------------------

    /// <summary>
    /// In a headless run, starts this scene again in a window placed off-screen, with the same
    /// arguments, passes on what it prints and returns its exit code (1 if it ran too long).
    /// Returns null when already windowed: the caller does the work itself.
    /// </summary>
    public static int? RelaunchWindowed(Node scene, int timeoutSeconds = 400)
    {
        if (DisplayServer.GetName() != "headless") return null;
        var start = new ProcessStartInfo(OS.GetExecutablePath())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string a in new[] { "--position", "20000,20000", "--path", ProjectSettings.GlobalizePath("res://"), scene.SceneFilePath, "--" })
            start.ArgumentList.Add(a);
        foreach (string a in OS.GetCmdlineUserArgs()) start.ArgumentList.Add(a);
        var lines = new ConcurrentQueue<string>();
        using var process = Process.Start(start);
        if (process == null)
        {
            GD.Print("FAIL  could not start the windowed run");
            return 1;
        }
        process.OutputDataReceived += (_, e) => { if (e.Data != null) lines.Enqueue(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null && (e.Data.Contains("ERROR") || e.Data.Contains("FAIL"))) lines.Enqueue(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        bool done = process.WaitForExit(timeoutSeconds * 1000);
        if (!done) process.Kill(entireProcessTree: true);
        process.WaitForExit();
        while (lines.TryDequeue(out string line)) GD.Print(line);
        if (!done)
        {
            GD.Print($"FAIL  the windowed run took over {timeoutSeconds} s");
            return 1;
        }
        return process.ExitCode;
    }
}

/// <summary>
/// A prototype's gate scene for its sky (or skies): each rendered in a world of its own at every
/// probe, failing on any seam, pinch or thinning of the stars. Headless, it runs itself again in a
/// window off-screen. A prototype subclasses it in <c>tests/SkySeamTest.cs</c> and says how it
/// makes its skies.
/// </summary>
public abstract partial class SkySeamCheck : Node
{
    /// <summary>Each sky the prototype shows, as its own code sets it up.</summary>
    protected abstract IEnumerable<(string name, Material sky)> Skies();

    /// <summary>Where its sun is: away from every probe, so its disc is never mistaken for a pinch.</summary>
    protected virtual Vector3 ToSun => new Vector3(0.35f, 0.42f, 0.84f).Normalized();

    public override async void _Ready()
    {
        if (SkySeams.RelaunchWindowed(this) is int code)
        {
            GetTree().Quit(code);
            return;
        }
        var environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Sky, TonemapMode = Godot.Environment.ToneMapper.Filmic };
        AddChild(new WorldEnvironment { Environment = environment });
        var sun = new DirectionalLight3D();
        AddChild(sun);
        sun.LookAt(sun.GlobalPosition - ToSun, MathF.Abs(ToSun.Y) > 0.99f ? Vector3.Forward : Vector3.Up);
        int failures = 0;
        foreach (var (name, material) in Skies())
        {
            environment.Sky = new Sky { SkyMaterial = material };
            failures += SkySeams.Report(name, await SkySeams.LookAround(this, Vector3.Zero, SkySeams.Probes(below: true)));
        }
        GD.Print(failures == 0 ? "ALL PASSED" : $"{failures} CHECK(S) FAILED");
        GetTree().Quit(failures == 0 ? 0 : 1);
    }
}
