using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;
using PlanetSurface.Terrain;

namespace Slice.Tests;

/// <summary>
/// Pinholes: bright single-pixel dots on the ground (the owner's night fly-overs). --pinholes-only runs just this.
/// Each view is drawn with every layer, then with one layer at a time left out (near ground, far ground,
/// flora, cover, horizon, water, the sun's shadows) and with the sky swapped for a flat colour, and the dots
/// are counted in the ground below the horizon: a pixel much brighter than the ring round it, alone.
/// </summary>
public partial class PopRoute
{
    const int PinFrames = 24;

    /// <summary>Render layers the probe sorts the streamer's drawings onto, so a camera can leave one out.</summary>
    static readonly (string Name, int Layer)[] PinLayers =
    {
        ("near ground", 12), ("far ground", 13), ("flora", 14), ("cover", 15), ("horizon", 16), ("water", 17),
    };

    void Tag()
    {
        TerrainStreamer s = _game.Zone.Streamer;
        static void Set(VisualInstance3D v, int layer) => v.Layers = 1u << (layer - 1);
        foreach (Node n in s.GetChildren())
        {
            if (n is MeshInstance3D m)
            {
                if (m.MaterialOverride == s.VoxelMaterial) Set(m, 12);
                else if (m.Name == "Horizon") Set(m, 16);
                else Set(m, 17);
            }
            else if (n.Name == "Cover")
                foreach (Node c in n.GetChildren())
                    if (c is VisualInstance3D v) Set(v, 15);
            if (n is not Node3D far || !far.Name.ToString().StartsWith("Far ")) continue;
            foreach (Node c in far.GetChildren())
                if (c is MultiMeshInstance3D f) Set(f, 14);
                else if (c is MeshInstance3D g) Set(g, g.MaterialOverride == s.FarMaterial ? 13 : 17);
        }
    }

    IEnumerable<int> Pinholes(Vector3 start, Vector3 way)
    {
        var zone = _game.Zone;
        foreach (Node n in GetTree().Root.FindChildren("*", nameof(CanvasLayer), true, false)) ((CanvasLayer)n).Visible = false;
        string dir = _args.GetValueOrDefault("out-dir", ProjectSettings.GlobalizePath("user://pinholes"));
        DirAccess.MakeDirRecursiveAbsolute(dir);
        zone.Sky.TimeScale = 0f;
        foreach (var (hour, label) in new[] { (Staging.HighSun(_game, false), "night"), (Staging.HighSun(_game), "day") })
        {
            zone.Sky.TimeOfDay = hour;
            foreach (var (view, up, pitch, move, back) in new[] { ("standing", 1.7f, -4f, 0.02f, false), ("back", 1.7f, -4f, 0.02f, true), ("low", 12f, -8f, 0.4f, false), ("flying", 60f, -14f, 1.2f, false) })
            {
                Vector3 look = back ? -way : way;
                Vector3 side = look.Cross(Vector3.Up).Normalized();
                Transform3D At(int i)
                {
                    Vector3 eye = Lift(start + side * (move * i), up);
                    Basis b = Basis.LookingAt(look, Vector3.Up).Rotated(side, Mathf.DegToRad(pitch));
                    return new Transform3D(b, eye);
                }
                // The sleeper out of sight behind the camera (the streamer follows it).
                void Stand(int i) => _game.Survivor.PlaceOutside((At(i).Origin - look * 6f + Vector3.Down * 1.6f).ToNumerics());
                _game.FixedCamera = At(0);
                for (int i = 0; i < 2400 && (i < 120 || !zone.Streamer.Settled); i++)
                {
                    Stand(0);
                    yield return 0;
                }
                var cases = new List<string> { "all" };
                cases.AddRange(PinLayers.Select(l => "-" + l.Name));
                cases.Add("-shadows");
                cases.Add("flat sky");
                foreach (string c in cases)
                {
                    Camera3D cam = _game.Camera;
                    var env = zone.Sky.Environment;
                    int dots = 0, frames = 0, flicker = 0, holes = 0;
                    HashSet<int> last = null;
                    for (int i = 0; i < PinFrames + 4; i++)
                    {
                        Tag();
                        foreach (var (name, layer) in PinLayers) cam.SetCullMaskValue(layer, c != "-" + name);
                        zone.Sky.Sun.ShadowCasterMask = zone.Sky.NightLight.ShadowCasterMask = c == "-shadows" ? 0u : uint.MaxValue;
                        env.BackgroundMode = c == "flat sky" ? Godot.Environment.BGMode.Color : Godot.Environment.BGMode.Sky;
                        env.BackgroundColor = new Color(1f, 0f, 1f);
                        Stand(i);
                        _game.FixedCamera = At(i);
                        yield return 0;
                        if (i < 4) continue;
                        Image img = GetViewport().GetTexture().GetImage();
                        int top = GroundTop(cam, At(i), img.GetHeight());
                        var found = Dots(img, top);
                        dots += found.Count;
                        holes += Holes(img, top);
                        frames++;
                        if (last != null) flicker += found.Count(p => !last.Contains(p));
                        last = found;
                        if (i < 6 && _args.ContainsKey("frames")) img.SavePng($"{dir}/{label}-{view}-{c.Replace(' ', '-')}-{i - 4}.png");
                    }
                    foreach (var (_, layer) in PinLayers) cam.SetCullMaskValue(layer, true);
                    zone.Sky.Sun.ShadowCasterMask = zone.Sky.NightLight.ShadowCasterMask = uint.MaxValue;
                    env.BackgroundMode = Godot.Environment.BGMode.Sky;
                    GD.Print(string.Create(CultureInfo.InvariantCulture, $"PINHOLES {label} {view} {c}: dots a frame {(double)dots / Math.Max(1, frames):F1}, new a frame {(double)flicker / Math.Max(1, frames - 1):F1}, background pixels in the ground a frame {(double)holes / Math.Max(1, frames):F1} (ground from row {GroundTop(cam, At(0), GetViewport().GetVisibleRect().Size.Y is var hh ? (int)hh : 0)})"));
                }
            }
        }        _game.FixedCamera = null;
        zone.Sky.TimeScale = 1f;
        foreach (Node n in GetTree().Root.FindChildren("*", nameof(CanvasLayer), true, false)) ((CanvasLayer)n).Visible = true;
    }

    /// <summary>The first picture row of ground for sure: under where the ground 400 m ahead meets the view, and a margin.</summary>
    static int GroundTop(Camera3D cam, Transform3D eye, int height)
    {
        Vector3 ahead = -eye.Basis.Z with { Y = 0f };
        Vector2 p = cam.UnprojectPosition(eye.Origin + ahead.Normalized() * 400f + Vector3.Down * 40f);
        return Math.Clamp((int)p.Y + 8, 0, height - 1);
    }

    /// <summary>
    /// Pixel indices of dots under a row: a pixel at least 0.15 brighter than, and twice, the mean of the ring
    /// of 16 two pixels out, with at most two of its eight neighbours anywhere near as bright (a dot, not an edge).
    /// </summary>
    static HashSet<int> Dots(Image img, int top)
    {
        int w = img.GetWidth(), h = img.GetHeight();
        byte[] data = img.GetData();
        int stride = img.GetFormat() == Image.Format.Rgba8 ? 4 : 3;
        var l = new float[w * h];
        for (int i = 0; i < w * h; i++)
            l[i] = (0.299f * data[i * stride] + 0.587f * data[i * stride + 1] + 0.114f * data[i * stride + 2]) / 255f;
        var found = new HashSet<int>();
        for (int y = Math.Max(top, 2); y < h - 2; y++)
            for (int x = 2; x < w - 2; x++)
            {
                float v = l[y * w + x];
                if (v < 0.12f) continue;
                float ring = 0f;
                for (int k = -2; k <= 2; k++)
                    ring += l[(y - 2) * w + x + k] + l[(y + 2) * w + x + k] + (k is > -2 and < 2 ? l[(y + k) * w + x - 2] + l[(y + k) * w + x + 2] : 0f);
                ring /= 16f;
                if (v - ring < 0.15f || v < 2f * ring) continue;
                float half = ring + 0.5f * (v - ring);
                int bright = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        if ((dx != 0 || dy != 0) && l[(y + dy) * w + x + dx] > half) bright++;
                if (bright <= 2) found.Add(y * w + x);
            }
        return found;
    }

    /// <summary>Pixels under a row showing the flat magenta background, wholly or in part: the ground let it through.</summary>
    static int Holes(Image img, int top)
    {
        int w = img.GetWidth(), h = img.GetHeight(), n = 0;
        byte[] data = img.GetData();
        int stride = img.GetFormat() == Image.Format.Rgba8 ? 4 : 3;
        for (int y = top; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * stride;
                int r = data[o], g = data[o + 1], b = data[o + 2];
                if (r > 70 && b > 70 && g * 2 < Math.Min(r, b) && Math.Abs(r - b) * 5 < Math.Max(r, b)) n++;
            }
        return n;
    }
}