using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;
using PlanetSurface.Terrain;

namespace Slice.Tests;

/// <summary>
/// Pinholes: bright single pixels flickering in the grass, seen most by night (the owner's, 14 Nov). No ground
/// let the sky through: they were sparks, plants' vertex colours carried far past their range where a sliver of
/// a far or shrinking plant is shaded at a multisampled pixel's centre, outside itself (flora_cover.gdshader).
/// At night, standing in the grass looking the route's way and back, and from 12 m up, two numbers a frame: the
/// sparks ground cover adds (dots at least 0.4 bright, with cover drawn less without it), and the
/// ground's pixels showing what is behind it (the sky swapped for flat magenta). --check gates both.
/// --pinholes-only runs just this; --bisect leaves each layer out in turn and MSAA off, by day too and from 60 m
/// up; --frames saves each pass's first two pictures (--out-dir, user://pinholes by default).
/// </summary>
public partial class PopRoute
{
    const int PinFrames = 24;

    /// <summary>Render layers the probe sorts the streamer's drawings onto, so the camera can leave one out.</summary>
    static readonly (string Name, int Layer)[] PinLayers =
    {
        ("near ground", 12), ("far ground", 13), ("flora", 14), ("cover", 15), ("horizon", 16), ("water", 17),
    };

    /// <summary>At most, a frame, at night: the sparks ground cover adds, and ground pixels showing the sky.</summary>
    const double CoverSparks = 0.1, SkyThrough = 2;

    void Tag()
    {
        TerrainStreamer s = _game.Zone.Streamer;
        static void Set(VisualInstance3D v, int layer) => v.Layers = 1u << (layer - 1);
        foreach (Node n in s.GetChildren())
        {
            if (n is MeshInstance3D m) Set(m, m.MaterialOverride == s.VoxelMaterial ? 12 : m.Name == "Horizon" ? 16 : 17);
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
        Camera3D cam = _game.Camera;
        var env = zone.Sky.Environment;
        bool bisect = _args.ContainsKey("bisect");
        var layers = GetTree().Root.FindChildren("*", nameof(CanvasLayer), true, false).Cast<CanvasLayer>().Where(l => l.Visible).ToList();
        foreach (CanvasLayer l in layers) l.Visible = false;
        string dir = _args.GetValueOrDefault("out-dir", ProjectSettings.GlobalizePath("user://pinholes"));
        DirAccess.MakeDirRecursiveAbsolute(dir);
        zone.Sky.TimeScale = 0f;
        var cases = bisect ? PinLayers.Select(l => "-" + l.Name).Prepend("all").Append("-shadows").Append("flat sky").Append("no msaa").ToList() : new List<string> { "all", "-cover", "flat sky" };
        foreach (var (hour, label) in bisect ? new[] { (Staging.HighSun(_game, false), "night"), (Staging.HighSun(_game), "day") } : new[] { (Staging.HighSun(_game, false), "night") })
        {
            zone.Sky.TimeOfDay = hour;
            var views = new[] { ("standing", 1.7f, -4f, 0.02f, false), ("back", 1.7f, -4f, 0.02f, true), ("low", 12f, -8f, 0.4f, false), ("flying", 60f, -14f, 1.2f, false) };
            foreach (var (view, up, pitch, move, back) in bisect ? views : views[..3])
            {
                Vector3 look = back ? -way : way;
                Vector3 side = look.Cross(Vector3.Up).Normalized();
                Transform3D At(int i) => new(Basis.LookingAt(look, Vector3.Up).Rotated(side, Mathf.DegToRad(pitch)), Lift(start + side * (move * i), up));
                // The sleeper out of sight behind the camera (the streamer follows it).
                void Stand(int i) => _game.Survivor.PlaceOutside((At(i).Origin - look * 6f + Vector3.Down * 1.6f).ToNumerics());
                _game.FixedCamera = At(0);
                for (int i = 0; i < 2400 && (i < 120 || !zone.Streamer.Settled); i++)
                {
                    Stand(0);
                    yield return 0;
                }
                Viewport.Msaa msaa = GetViewport().Msaa3D;
                var sparked = new Dictionary<string, double>();
                var holes = new Dictionary<string, double>();
                foreach (string c in cases)
                {
                    int found = 0, flicker = 0, through = 0, sparks = 0;
                    HashSet<int> last = null;
                    for (int i = 0; i < PinFrames + 4; i++)
                    {
                        Tag();
                        foreach (var (name, layer) in PinLayers) cam.SetCullMaskValue(layer, c != "-" + name);
                        zone.Sky.Sun.ShadowCasterMask = zone.Sky.NightLight.ShadowCasterMask = c == "-shadows" ? 0u : uint.MaxValue;
                        env.BackgroundMode = c == "flat sky" ? Godot.Environment.BGMode.Color : Godot.Environment.BGMode.Sky;
                        env.BackgroundColor = new Color(1f, 0f, 1f);
                        GetViewport().Msaa3D = c == "no msaa" ? Viewport.Msaa.Disabled : msaa;
                        Stand(i);
                        _game.FixedCamera = At(i);
                        yield return 0;
                        if (i < 4) continue;
                        Image img = GetViewport().GetTexture().GetImage();
                        // Dots from level ground 200 m ahead in (cover's reach, where tufts shrink away, is within 112 m);
                        // the sky's way through only well under the land's skyline (40 m under the eye 400 m ahead).
                        var (now, bright) = Dots(img, Row(At(i), 200f, up));
                        found += now.Count;
                        sparks += bright;
                        through += Holes(img, Row(At(i), 400f, 40f) + 4);
                        if (last != null) flicker += now.Count(p => !last.Contains(p));
                        last = now;
                        if (i < 6 && _args.ContainsKey("frames")) img.SavePng($"{dir}/{label}-{view}-{c.Replace(' ', '-')}-{i - 4}.png");
                    }
                    sparked[c] = (double)sparks / PinFrames;
                    holes[c] = (double)through / PinFrames;
                    GD.Print(string.Create(CultureInfo.InvariantCulture, $"PINHOLES {label} {view} {c}: dots a frame {(double)found / PinFrames:F1} (sparks {sparked[c]:F2}), new a frame {(double)flicker / (PinFrames - 1):F1}, ground pixels showing the background a frame {holes[c]:F1}"));
                }
                foreach (var (_, layer) in PinLayers) cam.SetCullMaskValue(layer, true);
                GetViewport().Msaa3D = msaa;
                zone.Sky.Sun.ShadowCasterMask = zone.Sky.NightLight.ShadowCasterMask = uint.MaxValue;
                env.BackgroundMode = Godot.Environment.BGMode.Sky;
                if (label != "night" || view is "flying") continue;
                // (Both passes that draw cover count: sparks are few, and a second sample steadies the number.)
                Gate($"pinholes, night, {view}: sparks ground cover adds", (sparked["all"] + sparked["flat sky"]) / 2 - sparked["-cover"], CoverSparks, false, "a frame");
                Gate($"pinholes, night, {view}: ground showing the sky", holes["flat sky"], SkyThrough, false, "px a frame");
            }
        }
        _game.FixedCamera = null;
        zone.Sky.TimeScale = 1f;
        foreach (CanvasLayer l in layers) l.Visible = true;
    }

    /// <summary>The picture row a little under a point some way ahead and a drop under the eye, m.</summary>
    int Row(Transform3D eye, float ahead, float drop)
    {
        Vector3 way = (-eye.Basis.Z with { Y = 0f }).Normalized();
        Vector2 p = _game.Camera.UnprojectPosition(eye.Origin + way * ahead + Vector3.Down * drop);
        return Math.Clamp((int)p.Y + 4, 0, (int)GetViewport().GetVisibleRect().Size.Y - 1);
    }

    /// <summary>A picture's bytes, and how many to a pixel.</summary>
    static (byte[] Data, int Stride) Bytes(Image img) => (img.GetData(), img.GetFormat() == Image.Format.Rgba8 ? 4 : 3);

    /// <summary>
    /// Pixel indices of dots under a row: a pixel at least 0.2 brighter than, and three times, the mean of the ring
    /// of 16 two pixels out, with at most two of its eight neighbours anywhere near as bright (a dot, not an edge);
    /// and how many of them are sparks, at least 0.4 bright (the owner's were, brighter than anything lit by night).
    /// </summary>
    static (HashSet<int> Dots, int Sparks) Dots(Image img, int top)
    {
        int w = img.GetWidth(), h = img.GetHeight();
        var (data, stride) = Bytes(img);
        var l = new float[w * h];
        for (int i = 0; i < w * h; i++) l[i] = (0.299f * data[i * stride] + 0.587f * data[i * stride + 1] + 0.114f * data[i * stride + 2]) / 255f;
        var found = new HashSet<int>();
        int sparks = 0;
        for (int y = Math.Max(top, 2); y < h - 2; y++)
            for (int x = 2; x < w - 2; x++)
            {
                float v = l[y * w + x];
                if (v < 0.2f) continue;
                float ring = 0f;
                for (int k = -2; k <= 2; k++)
                    ring += l[(y - 2) * w + x + k] + l[(y + 2) * w + x + k] + (k is > -2 and < 2 ? l[(y + k) * w + x - 2] + l[(y + k) * w + x + 2] : 0f);
                ring /= 16f;
                if (v - ring < 0.2f || v < 3f * ring) continue;
                float half = ring + 0.5f * (v - ring);
                int bright = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        if ((dx != 0 || dy != 0) && l[(y + dy) * w + x + dx] > half) bright++;
                if (bright > 2) continue;
                found.Add(y * w + x);
                if (v >= 0.4f) sparks++;
            }
        return (found, sparks);
    }

    /// <summary>Pixels under a row showing the flat magenta background, wholly or in part: the ground let it through.</summary>
    static int Holes(Image img, int top)
    {
        int w = img.GetWidth(), h = img.GetHeight(), n = 0;
        var (data, stride) = Bytes(img);
        for (int i = top * w; i < w * h; i++)
        {
            int r = data[i * stride], g = data[i * stride + 1], b = data[i * stride + 2];
            if (r > 70 && b > 70 && g * 4 < Math.Min(r, b) && Math.Abs(r - b) * 5 < Math.Max(r, b)) n++;
        }
        return n;
    }
}
