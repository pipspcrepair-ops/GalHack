using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;

namespace Slice.Tests;

/// <summary>
/// --only=gpu: what each part of the picture costs the GPU, ms a frame. Held still at a view (standing at the start,
/// in the largest city or town, low over it, flying high), each part is left out in turn by the camera's cull
/// mask (near ground, far ground, flora, cover, horizon, water, places and ruins, creatures and people), then the
/// sun's shadows, the sky, glow and MSAA: what the frame loses without it is that part's cost.
/// </summary>
public partial class PopRoute
{
    static (string Name, int Layer)[] GpuLayers => PinLayers.Concat(new[] { ("places", 18), ("life", 19) }).ToArray();

    /// <summary>Places, ruins and roads on layer 18, creatures and people on 19 (the streamer's own by <see cref="Tag"/>).</summary>
    void TagAll()
    {
        Tag();
        void Under(Node root, int layer)
        {
            if (root == null) return;
            foreach (Node n in root.FindChildren("*", "VisualInstance3D", true, false))
                ((VisualInstance3D)n).Layers = 1u << (layer - 1);
        }
        Under(_game.Zone.Pois, 18);
        Under(_game.Zone.Infra, 18);
        Under(_game.Places, 18);
        Under(_game.Zone.Wildlife, 19);
    }

    IEnumerable<int> GpuCost(Vector3 start, Vector3 way)
    {
        var zone = _game.Zone;
        Camera3D cam = _game.Camera;
        Godot.Environment env = zone.Sky.Environment;
        Rid vp = GetViewport().GetViewportRid();
        var views = new List<(string Name, Vector3 At, float Up, float Pitch)> { ("standing", start, 1.7f, -4f), ("flying high", start, 300f, -20f) };
        if (Biggest(_game) is var (mid, radius, _))
        {
            views.Add(("in the city", mid - way * 60f, 1.7f, -2f));
            views.Add(("low over the city", mid - way * (radius + 150f), 40f, -10f));
        }
        var cases = GpuLayers.Select(l => "-" + l.Name).Prepend("all").Concat(new[] { "-shadows", "flat sky", "-glow", "-msaa" }).ToList();
        foreach (var (view, at, up, pitch) in views)
        {
            Vector3 eye = Lift(at, up);
            _game.FixedCamera = new Transform3D(Basis.LookingAt(way, Vector3.Up).Rotated(way.Cross(Vector3.Up).Normalized(), Mathf.DegToRad(pitch)), eye);
            for (int i = 0; i < 2400 && (i < 300 || !zone.Streamer.Settled); i++)
            {
                _game.Survivor.PlaceOutside((eye - way * 6f + Vector3.Down * 1.6f).ToNumerics());
                yield return 0;
            }
            TagAll();
            Viewport.Msaa msaa = GetViewport().Msaa3D;
            bool glow = env.GlowEnabled;
            var cost = new Dictionary<string, double>();
            foreach (string c in cases)
            {
                foreach (var (name, layer) in GpuLayers) cam.SetCullMaskValue(layer, c != "-" + name);
                zone.Sky.Sun.ShadowCasterMask = zone.Sky.NightLight.ShadowCasterMask = c == "-shadows" ? 0u : uint.MaxValue;
                env.BackgroundMode = c == "flat sky" ? Godot.Environment.BGMode.Color : Godot.Environment.BGMode.Sky;
                env.GlowEnabled = c != "-glow" && glow;
                GetViewport().Msaa3D = c == "-msaa" ? Viewport.Msaa.Disabled : msaa;
                var gpu = new List<double>();
                for (int i = 0; i < 100; i++)
                {
                    _game.Survivor.PlaceOutside((eye - way * 6f + Vector3.Down * 1.6f).ToNumerics());
                    yield return 0;
                    if (i >= 20) gpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeGpu(vp));
                }
                gpu.Sort();
                cost[c] = gpu[gpu.Count / 2];
            }
            foreach (var (_, layer) in GpuLayers) cam.SetCullMaskValue(layer, true);
            zone.Sky.Sun.ShadowCasterMask = zone.Sky.NightLight.ShadowCasterMask = uint.MaxValue;
            env.BackgroundMode = Godot.Environment.BGMode.Sky;
            env.GlowEnabled = glow;
            GetViewport().Msaa3D = msaa;
            double all = cost["all"];
            string parts = string.Join(", ", cases.Skip(1).Select(c => string.Create(CultureInfo.InvariantCulture, $"{c.TrimStart('-')} {all - cost[c]:F2}")));
            GD.Print(string.Create(CultureInfo.InvariantCulture, $"ROUTE gpu {view}: all {all:F2} ms; without each (what it costs): {parts}"));
            _game.FixedCamera = null;
        }
    }
}
