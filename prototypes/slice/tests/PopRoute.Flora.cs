using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using PlanetSurface.Terrain;

namespace Slice.Tests;

/// <summary>
/// Plants that pop on a route: every plant drawn, from the eye now and from where it was a tenth of a second ago (all of a
/// plant's drawings together, as the shader has it), and those in view now that show half or more more or less. That is a
/// reach crossed or a drawing swapped within the tenth, not a fade: a fade over even 15 m of distance changes less than that
/// at a craft's top speed. A far chunk redrawn with a plant or without it is the pop-in log's (it dissolves in or out).
/// Too slow to sample beside the frame times, so each route runs again for it (<see cref="Route"/>'s pops pass).
/// </summary>
public partial class PopRoute
{
    readonly record struct FloraPop(string Kind, float Away, bool Appears, Vector3 Mid);

    readonly Dictionary<long, (float Change, float Away, float Size, bool Small, Vector3 At)> _changed = new();
    Vector3 _eyeBefore;
    double _sampledMs;
    int _samples;

    List<FloraPop> FloraPops()
    {
        long start = Stopwatch.GetTimestamp();
        Camera3D cam = _game.Camera;
        Vector3 eye = cam.GlobalPosition;
        var planes = cam.GetFrustum();
        var pops = new List<FloraPop>();
        _changed.Clear();
        // The same drawings in the same order from both eyes (nothing is streamed in between).
        using var was = _game.Zone.Streamer.FloraShown(_eyeBefore).GetEnumerator();
        foreach (var p in _game.Zone.Streamer.FloraShown(eye))
        {
            if (!was.MoveNext()) break;
            float change = p.Shows - was.Current.Shows;
            if (MathF.Abs(change) < 1e-3f) continue;
            _changed[p.Id] = _changed.TryGetValue(p.Id, out var c) ? c with { Change = c.Change + change } : (change, p.Away, p.Size, p.Layer == TerrainStreamer.SmallLayer, p.At);
        }
        foreach (var (_, c) in _changed)
        {
            Vector3 mid = c.At + Vector3.Up * (c.Size * 0.5f);
            if (MathF.Abs(c.Change) >= 0.5f && InView(planes, mid))
                pops.Add(new FloraPop(c.Small ? "small growth" : c.Size > 3f ? "tree" : "low large growth", c.Away, c.Change > 0f, mid));
        }
        _eyeBefore = eye;
        _sampledMs += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        _samples++;
        return pops;
    }

    static bool InView(Godot.Collections.Array<Plane> planes, Vector3 p)
    {
        foreach (Plane q in planes)
            if (q.DistanceTo(p) > 0f) return false;
        return true;
    }

    /// <summary>The camera's last drawn frame, scaled to this width.</summary>
    Image Frame(int width)
    {
        Image img = GetViewport().GetTexture().GetImage();
        img.Resize(width, width * img.GetHeight() / img.GetWidth(), Image.Interpolation.Bilinear);
        img.Convert(Image.Format.Rgb8);
        return img;
    }

    /// <summary>A copy of a frame (scaled as it is from the viewport) with rings round points.</summary>
    Image Ringed(Image frame, IEnumerable<Vector3> points, Color colour)
    {
        var img = (Image)frame.Duplicate();
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
        return img;
    }
}
