using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using PlanetSurface.Terrain;

namespace Slice.Tests;

/// <summary>
/// Plants that pop on a route: in view at two samples a tenth of a second apart, and showing half or more more or less
/// (all of a plant's drawings together, as the shader has it). That is a reach crossed or a drawing swapped within the
/// tenth, not a fade: a fade over even 15 m of distance changes less than that at a craft's top speed. A plant only one
/// of the two samples holds (a far chunk redrawn with it or without it) is the pop-in log's: it dissolves in or out.
/// </summary>
public partial class PopRoute
{
    readonly record struct Seen(float Shows, float Away, float Size, bool Small, Vector3 Mid);

    readonly record struct FloraPop(string Kind, float Away, bool Appears, Vector3 Mid);

    Dictionary<long, Seen> _seen = new(), _seenBefore = new();

    /// <summary>Time the samples took, ms, left out of the frame they landed in; and all of it on a route, and how many.</summary>
    double _sampleMs, _sampledMs;
    int _samples;

    List<FloraPop> FloraPops()
    {
        long start = Stopwatch.GetTimestamp();
        Camera3D cam = _game.Camera;
        var planes = cam.GetFrustum();
        (_seenBefore, _seen) = (_seen, _seenBefore);
        _seen.Clear();
        foreach (var p in _game.Zone.Streamer.FloraShown(cam.GlobalPosition))
        {
            Vector3 mid = p.At + Vector3.Up * (p.Size * 0.5f);
            if (!InView(planes, mid)) continue;
            _seen[p.Id] = _seen.TryGetValue(p.Id, out Seen was) ? was with { Shows = was.Shows + p.Shows } : new Seen(p.Shows, p.Away, p.Size, p.Layer == TerrainStreamer.SmallLayer, mid);
        }
        var pops = new List<FloraPop>();
        foreach (var (id, now) in _seen)
            if (_seenBefore.TryGetValue(id, out Seen was) && MathF.Abs(now.Shows - was.Shows) >= 0.5f)
                pops.Add(new FloraPop(now.Small ? "small growth" : now.Size > 3f ? "tree" : "low large growth", now.Away, now.Shows > was.Shows, now.Mid));
        double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        _sampleMs += ms;
        _sampledMs += ms;
        _samples++;
        return pops;
    }

    static bool InView(Godot.Collections.Array<Plane> planes, Vector3 p)
    {
        foreach (Plane q in planes)
            if (q.DistanceTo(p) > 0f) return false;
        return true;
    }

    /// <summary>The camera's last drawn frame, scaled to this width (--shots).</summary>
    Image Frame(int width)
    {
        Image img = GetViewport().GetTexture().GetImage();
        img.Resize(width, width * img.GetHeight() / img.GetWidth(), Image.Interpolation.Bilinear);
        img.Convert(Image.Format.Rgb8);
        return img;
    }

    /// <summary>Rings round points on a frame (scaled as it is from the viewport).</summary>
    void Mark(Image img, IEnumerable<Vector3> points, Color colour)
    {
        float k = img.GetWidth() / (float)GetViewport().GetVisibleRect().Size.X;
        foreach (Vector3 p in points)
        {
            if (_game.Camera.IsPositionBehind(p)) continue;
            Vector2 s = _game.Camera.UnprojectPosition(p) * k;
            for (int a = 0; a < 48; a++)
            {
                int x = (int)(s.X + MathF.Cos(a * MathF.Tau / 48) * 9f), y = (int)(s.Y + MathF.Sin(a * MathF.Tau / 48) * 9f);
                if (x >= 0 && y >= 0 && x < img.GetWidth() && y < img.GetHeight()) img.SetPixel(x, y, colour);
            }
        }
    }
}
