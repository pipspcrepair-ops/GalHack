using System;
using System.Collections.Generic;
using System.Globalization;
using GalHack.Physics.Engine;
using Godot;

namespace Slice.Tests;

/// <summary>
/// Culling as the view turns: from an eye held still, the view turned a whole way round a degree a frame. With the eye
/// still, a pure turn carries every pixel of one frame exactly to the next whatever its depth, so a pixel unlike the one it
/// came from (and its neighbours) is something drawn, culled or shadowed differently because the view turned. Standing
/// among the densest trees near the start, then 60 m and 400 m over them. At each, the plants that show by their own reach
/// in batches the renderer leaves out by distance are counted too (a chunk's worth vanishing at once).
/// </summary>
public partial class PopRoute
{
    const int TurnWidth = 480;
    Image _drawn;
    Basis _drawnBasis;
    long _drawnFrame;

    void OnPostDraw()
    {
        _drawn = Frame(TurnWidth);
        _drawnBasis = _game.Camera.GlobalBasis;
        _drawnFrame = (long)Engine.GetFramesDrawn();
    }

    IEnumerable<int> Turn(Vector3 over)
    {
        var zone = _game.Zone;
        zone.Sky.TimeScale = 0f;
        zone.Sky.TimeOfDay = _day;
        foreach (var (name, up) in new[] { ("standing", 0f), ("60 m up", 60f), ("400 m up", 400f) })
        {
            Vector3 eye = Lift(over, up);
            _hold = eye;
            foreach (int x in _drive.Wait(30)) yield return x;
            foreach (int x in _drive.UntilSettled(40)) yield return x;
            foreach (int x in _drive.Wait(60)) yield return x;
            int rangeCulled = zone.Streamer.FloraCulledByRange(_game.Camera.GlobalPosition);
            RenderingServer.FramePostDraw += OnPostDraw;
            var shares = new List<double>();
            double worst = -1;
            Image worstNow = null, worstBefore = null;
            (Image Img, Basis B) last = (null, default);
            long seen = -1;
            for (int i = 0; i < 420; i++)
            {
                float a = i * MathF.PI / 180f;
                _drive.Face(_game.Survivor.WorldEye() + new Vector3(MathF.Sin(a), -0.18f, -MathF.Cos(a)) * 100f);
                yield return 0;
                if (_drawn == null || _drawnFrame == seen) continue;
                seen = _drawnFrame;
                var now = (_drawn, _drawnBasis);
                if (i > 40 && last.Img != null)
                {
                    Image marked = null;
                    double share = Unlike(last.Img, last.B, now._drawn, now._drawnBasis, ref marked);
                    shares.Add(share);
                    if (share > worst) (worst, worstNow, worstBefore) = (share, marked, last.Img);
                }
                last = now;
            }
            RenderingServer.FramePostDraw -= OnPostDraw;
            _drive.Controls.Look = Vector2.Zero;
            shares.Sort();
            double P(double q) => shares.Count == 0 ? double.NaN : shares[Math.Min(shares.Count - 1, (int)(shares.Count * q))];
            GD.Print(string.Create(CultureInfo.InvariantCulture, $"ROUTE turn {name}: pixels unlike where they turned from, median {P(0.5):F3}%, 99th pct {P(0.99):F3}%, worst {worst:F3}% (n {shares.Count}); plants showing in batches culled by distance {rangeCulled}"));
            Gate($"turn {name}: plants showing in batches culled by distance", rangeCulled, 0, false, "");
            if (_args.TryGetValue("shots", out string dir) && worstNow != null)
            {
                DirAccess.MakeDirRecursiveAbsolute(dir);
                string tag = name.Replace(" ", "-");
                worstBefore.SavePng($"{dir}/turn-{tag}-before.png");
                worstNow.SavePng($"{dir}/turn-{tag}-after-marked.png");
            }
        }
        zone.Sky.TimeScale = 1f;
    }

    /// <summary>
    /// The share of a frame's pixels (%) unlike every pixel round where they were the frame before, the view having only
    /// turned: luminance more than 0.1 apart from all nine. Marks them red on a copy of the frame.
    /// </summary>
    double Unlike(Image before, Basis was, Image now, Basis basis, ref Image marked)
    {
        int w = before.GetWidth(), h = before.GetHeight();
        byte[] a = before.GetData(), b = now.GetData();
        float f = h * 0.5f / MathF.Tan(Mathf.DegToRad(_game.Camera.Fov) * 0.5f);
        Basis back = was.Transposed() * basis;
        static float Lum(byte[] d, int i) => (0.2126f * d[i] + 0.7152f * d[i + 1] + 0.0722f * d[i + 2]) / 255f;
        var unlike = new List<int>();
        int counted = 0;
        for (int v = 0; v < h; v++)
        for (int u = 0; u < w; u++)
        {
            Vector3 d = back * new Vector3((u + 0.5f - w * 0.5f) / f, (h * 0.5f - v - 0.5f) / f, -1f);
            if (d.Z >= -1e-3f) continue;
            int pu = (int)MathF.Round(w * 0.5f + f * d.X / -d.Z - 0.5f), pv = (int)MathF.Round(h * 0.5f - f * d.Y / -d.Z - 0.5f);
            if (pu < 1 || pv < 1 || pu > w - 2 || pv > h - 2) continue;
            counted++;
            float l = Lum(b, (v * w + u) * 3), least = float.MaxValue;
            for (int dv = -1; dv <= 1; dv++)
            for (int du = -1; du <= 1; du++)
                least = MathF.Min(least, MathF.Abs(l - Lum(a, ((pv + dv) * w + pu + du) * 3)));
            if (least > 0.1f) unlike.Add(v * w + u);
        }
        marked = (Image)now.Duplicate();
        foreach (int k in unlike) marked.SetPixel(k % w, k / w, new Color(1f, 0f, 0f));
        return counted == 0 ? 0 : unlike.Count * 100.0 / counted;
    }
}
