using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Slice.Orbit;

namespace Slice.Tests;

/// <summary>
/// The warp's streaks as a person sees them (windowed): two seconds of the tunnel and two of the spool at the window's own
/// frame rate, the camera looking along the pilot's forward and then turned 30° off it, as from the seat, the streaks run
/// by real time as the game runs them (<see cref="JumpView.PhaseRate"/>). For each, a contact sheet
/// of eight consecutive frames to follow a streak by eye, and a measure of which way they run: the brightness round the
/// point ahead, bearing by bearing, matched frame to frame (positive: outward from the point ahead, past the camera).
/// Quits itself.
///
/// Run: G:\GalHack\.tools\queue\godot-q.ps1 -Console --path prototypes/slice res://tests/JumpShots.tscn -After "--out=&lt;folder&gt;"
/// </summary>
public partial class JumpShots : Node3D
{
    Camera3D _camera;
    MeshInstance3D _tunnel;
    ShaderMaterial _material;
    IEnumerator<int> _run;
    string _out;
    int _failures;
    double _delta;

    const int W = StreakFlow.W, H = StreakFlow.H; // each kept frame, for the sheets and the measure

    public override void _Ready()
    {
        foreach (string a in OS.GetCmdlineUserArgs())
            if (a.StartsWith("--out=")) _out = a["--out=".Length..];
        _out ??= System.IO.Path.Combine(OS.GetUserDataDir(), "jump-shots");
        System.IO.Directory.CreateDirectory(_out);
        DisplayServer.WindowSetSize(new Vector2I(960, 540));
        Engine.MaxFps = 60; // the owner's 60 Hz screen: every frame kept is a frame he sees
        AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = Colors.Black } });
        _camera = new Camera3D { Fov = 75f, Current = true };
        AddChild(_camera);
        _material = JumpView.TunnelMaterial();
        _tunnel = new MeshInstance3D { Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 48, Rings = 24 }, MaterialOverride = _material };
        AddChild(_tunnel);
        _run = Run().GetEnumerator();
    }

    public override void _Process(double delta)
    {
        _delta = delta;
        if (_run.MoveNext()) return;
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} CHECK(S) FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
        SetProcess(false);
    }

    IEnumerable<int> Run()
    {
        foreach (bool tunnel in new[] { true, false })
            foreach (float off in new[] { 0f, 30f })
                foreach (int x in Case(tunnel, off)) yield return x;
    }

    /// <summary>Two seconds of the tunnel (or the spool, its streaks gathering), the camera turned <paramref name="off"/> degrees from the way it goes.</summary>
    IEnumerable<int> Case(bool tunnel, float off)
    {
        string name = $"{(tunnel ? "tunnel" : "spool")}-{(off == 0f ? "ahead" : $"{off:F0}-off")}";
        // The pilot's forward is -z (JumpView's axis); the camera turned from it as a pilot looking aside in the seat.
        _tunnel.GlobalTransform = new Transform3D(Basis.LookingAt(Vector3.Forward, Vector3.Up).Scaled(Vector3.One * 60f), Vector3.Zero);
        _camera.GlobalTransform = new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad(off)), Vector3.Zero);
        _material.SetShaderParameter("cover", tunnel ? 1f : 0f);
        _material.SetShaderParameter("flash", 0f);
        Vector2 size = GetViewport().GetVisibleRect().Size;
        float focal = size.Y * 0.5f / MathF.Tan(Mathf.DegToRad(_camera.Fov * 0.5f));
        var centre = new Vector2(size.X * 0.5f + focal * MathF.Tan(Mathf.DegToRad(off)), size.Y * 0.5f) * (W / size.X);
        var frames = new List<Image>();
        var deltas = new List<double>();
        float phase = 10f, seconds = 0f;
        for (int i = 0; i < 5; i++) yield return 0; // settled
        while (seconds < 2f)
        {
            float spool = tunnel ? 1f : Mathf.Lerp(0.5f, 0.95f, seconds / 2f);
            _material.SetShaderParameter("amount", tunnel ? 1f : spool * spool * 0.45f);
            _material.SetShaderParameter("phase", phase);
            yield return 0;
            Image shot = GetViewport().GetTexture().GetImage();
            shot.Resize(W, H, Image.Interpolation.Bilinear);
            frames.Add(shot);
            deltas.Add(_delta);
            seconds += (float)_delta;
            phase += (float)_delta * JumpView.PhaseRate(spool, tunnel);
        }
        // Eight consecutive frames, mid-run, 4 by 2, the point ahead marked: follow a streak by eye.
        int from = frames.Count / 2;
        var sheet = Image.CreateEmpty(W * 4, H * 2, false, Image.Format.Rgba8);
        for (int k = 0; k < 8 && from + k < frames.Count; k++)
        {
            Image f = (Image)frames[from + k].Duplicate();
            f.Convert(Image.Format.Rgba8);
            for (int d = -4; d <= 4; d++)
            {
                Mark(f, (int)centre.X + d, (int)centre.Y);
                Mark(f, (int)centre.X, (int)centre.Y + d);
            }
            sheet.BlitRect(f, new Rect2I(0, 0, W, H), new Vector2I(k % 4 * W, k / 4 * H));
        }
        sheet.SavePng(System.IO.Path.Combine(_out, $"{name}-sheet.png"));
        frames[from].SavePng(System.IO.Path.Combine(_out, $"{name}-frame.png"));
        // Which way they run, frame to frame over the whole two seconds.
        var (median, outward) = StreakFlow.Run(frames.Select(StreakFlow.Luma).ToList(), centre);
        double fps = deltas.Count / deltas.Sum();
        bool ok = median > 0 && outward > 0.8f;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {name}: {frames.Count} frames at {fps:F0} fps; streaks move {median:+0.0;-0.0} px a frame (median, at {W} px wide) from the point ahead at ({centre.X:F0}, {centre.Y:F0}), outward in {outward:P0} of frame pairs: {(ok ? "rushing outward, past the camera" : "NOT outward")} ({_out})");
        if (!ok) _failures++;
    }

    static void Mark(Image f, int x, int y)
    {
        if (x >= 0 && y >= 0 && x < f.GetWidth() && y < f.GetHeight()) f.SetPixel(x, y, Colors.Red);
    }
}

/// <summary>
/// Which way the warp's streaks run on screen, frame to frame: the brightness round the point ahead, bearing by bearing,
/// matched between consecutive frames at every radial shift (positive: outward from the point ahead, past the camera).
/// Whatever stands still over the frames (the tunnel's own glow, a cockpit in frame) is taken out first: only what moves
/// is matched. Frames are measured at <see cref="W"/> × <see cref="H"/>.
/// </summary>
static class StreakFlow
{
    public const int W = 480, H = 270;

    /// <summary>A frame's brightness at W × H.</summary>
    public static float[] Luma(Image shot)
    {
        var img = (Image)shot.Duplicate();
        if (img.GetWidth() != W || img.GetHeight() != H) img.Resize(W, H, Image.Interpolation.Bilinear);
        img.Convert(Image.Format.Rgba8);
        byte[] d = img.GetData();
        var l = new float[W * H];
        for (int i = 0; i < l.Length; i++) l[i] = (0.2126f * d[4 * i] + 0.7152f * d[4 * i + 1] + 0.0722f * d[4 * i + 2]) / 255f;
        return l;
    }

    /// <summary>The median shift a frame (px at W wide) round <paramref name="c"/> over every consecutive pair, and the share of pairs that ran outward.</summary>
    public static (float Median, float Outward) Run(IReadOnlyList<float[]> frames, Vector2 c)
    {
        var still = new float[W * H];
        foreach (float[] f in frames)
            for (int i = 0; i < still.Length; i++) still[i] += f[i] / frames.Count;
        var polar = frames.Select(f => Polar(f, still, c)).ToList();
        var lags = new List<float>();
        for (int k = 0; k + 1 < polar.Count; k++) lags.Add(Lag(polar[k], polar[k + 1]));
        if (lags.Count == 0) return (0f, 0f);
        lags.Sort();
        return (lags[lags.Count / 2], lags.Count(l => l > 0) / (float)lags.Count);
    }

    const int Bearings = 180, From = 50, To = 240, MaxLag = 30; // out where a streak crosses pixels a frame; the newborn dots by the point ahead barely move

    static int Lag(float[,] pa, float[,] pb)
    {
        int best = 0;
        double bestScore = double.NegativeInfinity;
        for (int lag = -MaxLag; lag <= MaxLag; lag++)
        {
            double score = 0;
            for (int t = 0; t < Bearings; t++)
                for (int r = MaxLag; r < To - From - MaxLag; r++)
                    score += pa[t, r] * pb[t, r + lag];
            if (score > bestScore) (bestScore, best) = (score, lag);
        }
        return best;
    }

    static float[,] Polar(float[] img, float[] still, Vector2 c)
    {
        var p = new float[Bearings, To - From];
        for (int t = 0; t < Bearings; t++)
        {
            float a = t * Mathf.Tau / Bearings;
            var dir = new Vector2(MathF.Cos(a), MathF.Sin(a));
            for (int r = From; r < To; r++)
            {
                Vector2 q = c + dir * r;
                int x = (int)q.X, y = (int)q.Y;
                p[t, r - From] = x >= 0 && y >= 0 && x < W && y < H ? img[y * W + x] - still[y * W + x] : 0f;
            }
        }
        return p;
    }
}
