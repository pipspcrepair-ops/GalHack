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

    /// <summary>The places' and people's drawings by kind: a place's blocks (its chunks), its dressing, the people, and the places' far drawings.</summary>
    static readonly Dictionary<string, int> Parts = new() { ["blocks"] = 0, ["dressing"] = 1, ["people"] = 2, ["far places"] = 3 };

    Dictionary<string, List<(GeometryInstance3D G, GeometryInstance3D.ShadowCastingSetting Cast, bool Shown)>> Kinds()
    {
        var kinds = Parts.Keys.ToDictionary(k => k, _ => new List<(GeometryInstance3D, GeometryInstance3D.ShadowCastingSetting, bool)>());
        static bool InPerson(Node n)
        {
            for (; n != null; n = n.GetParent())
                if (n.GetType().Name == "PersonView") return true;
            return false;
        }
        if (_game.Places != null)
            foreach (Node n in _game.Places.FindChildren("*", "GeometryInstance3D", true, false))
            {
                var g = (GeometryInstance3D)n;
                string kind = InPerson(g) ? "people" : g.Name.ToString() is var nm && (nm.StartsWith("Chunk") || nm.StartsWith("Cube")) ? "blocks" : "dressing";
                kinds[kind].Add((g, g.CastShadow, g.Visible));
            }
        if (_game.Zone.Pois != null)
            foreach (Node n in _game.Zone.Pois.FindChildren("*", "GeometryInstance3D", true, false))
                kinds["far places"].Add(((GeometryInstance3D)n, ((GeometryInstance3D)n).CastShadow, ((GeometryInstance3D)n).Visible));
        return kinds;
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
        var cases = GpuLayers.Select(l => "-" + l.Name).Prepend("all").Concat(new[] { "-shadows", "flat sky", "-glow", "-msaa" })
            .Concat(Parts.Keys.SelectMany(k => new[] { $"-{k} shadows", $"-{k} drawn" })).Append("all again").ToList();
        foreach (var (view, at, up, pitch) in views)
        {
            Vector3 eye = Lift(at, up);
            _game.FixedCamera = new Transform3D(Basis.LookingAt(way, Vector3.Up).Rotated(way.Cross(Vector3.Up).Normalized(), Mathf.DegToRad(pitch)), eye);
            // Until the ground has settled and the places round have stood up (their cubes' full detail in), a minute at most.
            int idleFor = 0;
            for (int i = 0; i < 3600 && (i < 300 || !zone.Streamer.Settled || idleFor < 180); i++)
            {
                idleFor = _game.Places == null || _game.Places.Idle ? idleFor + 1 : 0;
                _game.Survivor.PlaceOutside((eye - way * 6f + Vector3.Down * 1.6f).ToNumerics());
                yield return 0;
            }
            TagAll();
            var parts = Kinds();
            Viewport.Msaa msaa = GetViewport().Msaa3D;
            bool glow = env.GlowEnabled;
            var cost = new Dictionary<string, double>();
            var renderCpu = new Dictionary<string, double>();
            var drawCalls = new Dictionary<string, double>();
            foreach (string c in cases)
            {
                foreach (var (name, layer) in GpuLayers) cam.SetCullMaskValue(layer, c != "-" + name);
                zone.Sky.Sun.ShadowCasterMask = zone.Sky.NightLight.ShadowCasterMask = c == "-shadows" ? 0u : uint.MaxValue;
                env.BackgroundMode = c == "flat sky" ? Godot.Environment.BGMode.Color : Godot.Environment.BGMode.Sky;
                env.GlowEnabled = c != "-glow" && glow;
                GetViewport().Msaa3D = c == "-msaa" ? Viewport.Msaa.Disabled : msaa;
                foreach (var (kind, list) in parts)
                    foreach (var (g, cast, shown) in list)
                    {
                        if (!IsInstanceValid(g)) continue;
                        g.CastShadow = c == $"-{kind} shadows" ? GeometryInstance3D.ShadowCastingSetting.Off : cast;
                        g.Visible = c != $"-{kind} drawn" && shown;
                    }
                var gpu = new List<double>();
                var cpu = new List<double>();
                var draws = new List<double>();
                for (int i = 0; i < 100; i++)
                {
                    _game.Survivor.PlaceOutside((eye - way * 6f + Vector3.Down * 1.6f).ToNumerics());
                    yield return 0;
                    if (i < 20) continue;
                    gpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeGpu(vp));
                    cpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeCpu(vp) + RenderingServer.GetFrameSetupTimeCpu());
                    draws.Add(RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame));
                }
                gpu.Sort();
                cpu.Sort();
                draws.Sort();
                cost[c] = gpu[gpu.Count / 2];
                renderCpu[c] = cpu[cpu.Count / 2];
                drawCalls[c] = draws[draws.Count / 2];
            }
            foreach (var (_, list) in parts)
                foreach (var (g, cast, shown) in list)
                    if (IsInstanceValid(g)) (g.CastShadow, g.Visible) = (cast, shown);
            foreach (var (_, layer) in GpuLayers) cam.SetCullMaskValue(layer, true);
            zone.Sky.Sun.ShadowCasterMask = zone.Sky.NightLight.ShadowCasterMask = uint.MaxValue;
            env.BackgroundMode = Godot.Environment.BGMode.Sky;
            env.GlowEnabled = glow;
            GetViewport().Msaa3D = msaa;
            double all = cost["all"];
            double allCpu = renderCpu["all"], allDraws = drawCalls["all"];
            string costs = string.Join(", ", cases.Skip(1).Select(c => string.Create(CultureInfo.InvariantCulture, $"{c.TrimStart('-')} {all - cost[c]:F2}/{allCpu - renderCpu[c]:F2}/{allDraws - drawCalls[c]:F0}")));
            // The whole picture measured again last: what it moved meanwhile (a place still coming in) is the measure's own error.
            GD.Print(string.Create(CultureInfo.InvariantCulture, $"ROUTE gpu {view}: drift over the cases gpu {cost["all again"] - all:F2} ms, render cpu {renderCpu["all again"] - allCpu:F2} ms, draw calls {drawCalls["all again"] - allDraws:F0}"));
            GD.Print(string.Create(CultureInfo.InvariantCulture, $"ROUTE gpu {view}: all gpu {all:F2} ms, render cpu {allCpu:F2} ms, {allDraws:F0} draw calls ({string.Join(", ", parts.Select(kv => $"{kv.Key} {kv.Value.Count}"))}); without each (what it costs, gpu/render cpu ms/draw calls): {costs}"));
            _game.FixedCamera = null;
        }
    }
}
