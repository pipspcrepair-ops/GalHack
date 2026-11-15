using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using PlanetSurface.Terrain;

namespace Slice.Tests;

/// <summary>
/// What the trees cost, in this run's own world: from 2 m (on the ground), 150 m (low flight) and 800 m (high flight) over
/// the densest growth near the start, by day, looking out four ways and down, the GPU's and the render thread's time a frame
/// (medians) with the trees as drawn, cut at 370 m, casting no shadows, and not drawn at all. The states take turns, four
/// times round, so what else the machine does falls on each alike.
/// </summary>
public partial class PopRoute
{
    IEnumerable<int> TreeCost(Vector3 over)
    {
        TerrainStreamer s = _game.Zone.Streamer;
        Viewport view = GetViewport();
        void Reach(float large) => s.FloraMaterial.SetShaderParameter("flora_reach", new Vector2(large, DetailReach.Small));
        // Every far chunk's drawing of large growth now standing (small growth is its own layer).
        List<MultiMeshInstance3D> Trees() => s.FindChildren("*", nameof(MultiMeshInstance3D), true, false).OfType<MultiMeshInstance3D>()
            .Where(m => m.GetParent().Name.ToString().StartsWith("Far ") && (float)m.GetInstanceShaderParameter("flora_layer") != TerrainStreamer.SmallLayer).ToList();
        List<MultiMeshInstance3D> held = null;
        var cast = new Dictionary<MultiMeshInstance3D, GeometryInstance3D.ShadowCastingSetting>();
        var states = new (string Name, Action On, Action Off)[]
        {
            ("as drawn", () => { }, () => { }),
            ("trees to 370 m", () => Reach(370f), () => Reach(DetailReach.Large)),
            ("no tree shadows", () => { cast.Clear(); foreach (var m in Trees()) (cast[m], m.CastShadow) = (m.CastShadow, GeometryInstance3D.ShadowCastingSetting.Off); },
                () => { foreach (var (m, was) in cast) if (GodotObject.IsInstanceValid(m)) m.CastShadow = was; }),
            ("no trees", () => { held = Trees(); foreach (var m in held) m.Visible = false; }, () => { foreach (var m in held.Where(GodotObject.IsInstanceValid)) m.Visible = true; }),
        };
        var sky = _game.Zone.Sky;
        sky.TimeScale = 0f;
        sky.TimeOfDay = 0.42; // mid-morning: daylight, and the same light for every run
        foreach (var (up, down) in new[] { (2f, -0.08f), (150f, -0.4f), (800f, -0.7f) })
        {
            Vector3 eye = Lift(over, up);
            _hold = eye;
            foreach (int x in _drive.Wait(30)) yield return x;
            foreach (int x in _drive.UntilSettled(40)) yield return x;
            var gpu = new List<double>[states.Length];
            var cpu = new List<double>[states.Length];
            for (int k = 0; k < states.Length; k++) (gpu[k], cpu[k]) = (new List<double>(), new List<double>());
            for (int round = 0; round < 4; round++)
            for (int way = 0; way < 4; way++)
            {
                float a = way * MathF.PI / 2f;
                Vector3 look = _game.Survivor.WorldEye() + new Vector3(MathF.Sin(a), down, -MathF.Cos(a)) * 100f;
                for (int k = 0; k < states.Length; k++)
                {
                    states[k].On();
                    for (int i = 0; i < 40; i++)
                    {
                        _drive.Face(look);
                        yield return 0;
                        if (i < 10) continue;
                        // --shots=dir: the first view in each state, side by side in this world.
                        if (round == 0 && way == 0 && i == 39 && _args.TryGetValue("shots", out string dir))
                        {
                            DirAccess.MakeDirRecursiveAbsolute(dir);
                            Frame(1280).SavePng(string.Create(CultureInfo.InvariantCulture, $"{dir}/trees-{up:F0}m-{states[k].Name.Replace(' ', '-')}.png"));
                        }
                        gpu[k].Add(RenderingServer.ViewportGetMeasuredRenderTimeGpu(view.GetViewportRid()));
                        cpu[k].Add(RenderingServer.ViewportGetMeasuredRenderTimeCpu(view.GetViewportRid()));
                    }
                    states[k].Off();
                }
            }
            _drive.Controls.Look = Vector2.Zero;
            static double Median(List<double> v) => v.OrderBy(t => t).ElementAt(v.Count / 2);
            // Trees (large growth over 3 m) showing within a kilometre across the ground, per square kilometre.
            int trees = s.FloraShown(eye).Where(p => p.Layer != TerrainStreamer.SmallLayer && p.Size > 3f && p.Shows > 0.05f && new Vector2(p.At.X - eye.X, p.At.Z - eye.Z).Length() < 1000f)
                .Select(p => p.Id).Distinct().Count();
            GD.Print(string.Create(CultureInfo.InvariantCulture, $"ROUTE tree cost {up:F0} m up, {trees / MathF.PI:F0} trees a km2 within 1 km, a frame (median, GPU then render thread): ") +
                     string.Join(", ", states.Select((st, k) => string.Create(CultureInfo.InvariantCulture, $"{st.Name} {Median(gpu[k]):F2} / {Median(cpu[k]):F2} ms"))));
        }
        sky.TimeScale = 1f;
    }
}
