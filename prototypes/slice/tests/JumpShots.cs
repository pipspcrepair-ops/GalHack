using System;
using System.Collections.Generic;
using Godot;
using Slice.Orbit;

namespace Slice.Tests;

/// <summary>
/// The warp tunnel's streaks, frame by frame (windowed): the camera looks ahead along the way the jump goes, then 30° off
/// it (the hull not pointed at the target). Four frames a few hundredths of a second apart for each, saved side by side,
/// and a measure of which way the streaks run: the radial profiles round the point ahead, frame to frame, matched by
/// cross-correlation (positive: outward from the point ahead, past the camera). Quits itself.
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

    const float Step = 0.03f, Speed = 2.2f; // seconds between frames, and the tunnel's phase rate

    public override void _Ready()
    {
        foreach (string a in OS.GetCmdlineUserArgs())
            if (a.StartsWith("--out=")) _out = a["--out=".Length..];
        _out ??= System.IO.Path.Combine(OS.GetUserDataDir(), "jump-shots");
        System.IO.Directory.CreateDirectory(_out);
        DisplayServer.WindowSetSize(new Vector2I(960, 540));
        AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = Colors.Black } });
        _camera = new Camera3D { Fov = 75f, Current = true };
        AddChild(_camera);
        _material = JumpView.TunnelMaterial();
        _tunnel = new MeshInstance3D { Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 48, Rings = 24 }, MaterialOverride = _material, Scale = Vector3.One * 60f };
        AddChild(_tunnel);
        _material.SetShaderParameter("amount", 1f);
        _material.SetShaderParameter("cover", 1f);
        _material.SetShaderParameter("flash", 0f);
        _run = Run().GetEnumerator();
    }

    public override void _Process(double delta)
    {
        if (_run.MoveNext()) return;
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} CHECK(S) FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
        SetProcess(false);
    }

    IEnumerable<int> Run()
    {
        foreach (int x in Case("ahead", 0f)) yield return x;
        foreach (int x in Case("30-off", 30f)) yield return x;
    }

    /// <summary>The jump going -z; the camera turned <paramref name="off"/> degrees from it (the hull not pointed at what it goes to).</summary>
    IEnumerable<int> Case(string name, float off)
    {
        _tunnel.GlobalTransform = new Transform3D(Basis.LookingAt(Vector3.Forward, Vector3.Up).Scaled(Vector3.One * 60f), Vector3.Zero);
        _camera.GlobalTransform = new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad(off)), Vector3.Zero);
        Vector2 size = GetViewport().GetVisibleRect().Size;
        // Where the point ahead is on screen: the camera's height-kept field of view, the point turned off degrees from its middle.
        float focal = size.Y * 0.5f / MathF.Tan(Mathf.DegToRad(_camera.Fov * 0.5f));
        var centre = new Vector2(size.X * 0.5f + focal * MathF.Tan(Mathf.DegToRad(off)), size.Y * 0.5f);
        var frames = new List<Image>();
        for (int k = 0; k < 4; k++)
        {
            _material.SetShaderParameter("phase", 10f + k * Step * Speed);
            for (int i = 0; i < 3; i++) yield return 0; // drawn
            Image shot = GetViewport().GetTexture().GetImage();
            frames.Add(shot);
            shot.SavePng(System.IO.Path.Combine(_out, $"{name}-{k}.png"));
        }
        // Side by side, the point ahead marked, for looking at.
        int w = (int)size.X, h = (int)size.Y;
        var strip = Image.CreateEmpty(w * 2, h * 2, false, Image.Format.Rgba8);
        for (int k = 0; k < 4; k++)
        {
            Image f = (Image)frames[k].Duplicate();
            f.Convert(Image.Format.Rgba8);
            for (int d = -6; d <= 6; d++)
            {
                Mark(f, (int)centre.X + d, (int)centre.Y);
                Mark(f, (int)centre.X, (int)centre.Y + d);
            }
            strip.BlitRect(f, new Rect2I(0, 0, w, h), new Vector2I(k % 2 * w, k / 2 * h));
        }
        strip.SavePng(System.IO.Path.Combine(_out, $"{name}-strip.png"));
        // Which way the streaks run: each frame's brightness by distance from the point ahead (a polar profile per bearing), matched to the next frame's.
        var lags = new List<float>();
        for (int k = 0; k + 1 < 4; k++) lags.Add(Lag(frames[k], frames[k + 1], centre));
        bool outward = lags.TrueForAll(l => l > 0.5f);
        GD.Print($"  {(outward ? "ok   " : "FAIL ")} {name}: the point ahead at ({centre.X:F0}, {centre.Y:F0}); streaks move {string.Join(", ", lags.ConvertAll(l => $"{l:+0.0;-0.0} px"))} a frame from it ({Step * 1000:F0} ms apart): {(outward ? "outward, past the camera" : "not outward")} ({_out})");
        if (!outward) _failures++;
    }

    static void Mark(Image f, int x, int y)
    {
        if (x >= 0 && y >= 0 && x < f.GetWidth() && y < f.GetHeight()) f.SetPixel(x, y, Colors.Red);
    }

    /// <summary>The radial shift (px, outward positive) that best matches frame b to frame a, over every bearing round <paramref name="c"/>.</summary>
    static float Lag(Image a, Image b, Vector2 c)
    {
        const int Bearings = 180, From = 40, To = 300, MaxLag = 40;
        float[,] pa = Polar(a, c, Bearings, From, To), pb = Polar(b, c, Bearings, From, To);
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

    static float[,] Polar(Image img, Vector2 c, int bearings, int from, int to)
    {
        var p = new float[bearings, to - from];
        var mean = new float[to - from];
        for (int t = 0; t < bearings; t++)
        {
            float a = t * Mathf.Tau / bearings;
            var dir = new Vector2(MathF.Cos(a), MathF.Sin(a));
            for (int r = from; r < to; r++)
            {
                Vector2 q = c + dir * r;
                int x = (int)q.X, y = (int)q.Y;
                p[t, r - from] = x >= 0 && y >= 0 && x < img.GetWidth() && y < img.GetHeight() ? img.GetPixel(x, y).Luminance : 0f;
                mean[r - from] += p[t, r - from] / bearings;
            }
        }
        // The tunnel's own glow deepens with distance from the point ahead, the same every frame: only the streaks are matched.
        for (int t = 0; t < bearings; t++)
            for (int r = 0; r < to - from; r++) p[t, r] -= mean[r];
        return p;
    }
}
