using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using PlanetSurface.Terrain;

namespace Slice.Tests;

/// <summary>
/// What drawing every tree out to the detail distance costs, in this run's own world: from 2 m and from 60 m over the densest
/// growth near the start, looking out four ways a little down, the GPU's and the render thread's time a frame (medians) with
/// the trees as drawn and with trees cut at 370 m (as far as every tree reached before). The two take turns, four times
/// round, so what else the machine does falls on each alike.
/// </summary>
public partial class PopRoute
{
    IEnumerable<int> TreeCost(Vector3 over)
    {
        TerrainStreamer s = _game.Zone.Streamer;
        Viewport view = GetViewport();
        void Reach(float large) => s.FloraMaterial.SetShaderParameter("flora_reach", new Vector2(large, DetailReach.Small));
        var states = new (string Name, Action On, Action Off)[]
        {
            ("as drawn", () => { }, () => { }),
            ("trees to 370 m", () => Reach(370f), () => Reach(DetailReach.Large)),
        };
        foreach (float up in new[] { 2f, 60f })
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
                Vector3 look = _game.Survivor.WorldEye() + new Vector3(MathF.Sin(a), -0.08f, -MathF.Cos(a)) * 100f;
                for (int k = 0; k < states.Length; k++)
                {
                    states[k].On();
                    for (int i = 0; i < 40; i++)
                    {
                        _drive.Face(look);
                        yield return 0;
                        if (i < 10) continue;
                        gpu[k].Add(RenderingServer.ViewportGetMeasuredRenderTimeGpu(view.GetViewportRid()));
                        cpu[k].Add(RenderingServer.ViewportGetMeasuredRenderTimeCpu(view.GetViewportRid()));
                    }
                    states[k].Off();
                }
            }
            _drive.Controls.Look = Vector2.Zero;
            static double Median(List<double> v) => v.OrderBy(t => t).ElementAt(v.Count / 2);
            // Trees (large growth over 3 m) drawn within a kilometre, per square kilometre.
            int trees = s.FloraShown(eye).Where(p => p.Layer != TerrainStreamer.SmallLayer && p.Size > 3f && p.Away < 1000f).Select(p => p.Id).Distinct().Count();
            GD.Print(string.Create(CultureInfo.InvariantCulture, $"ROUTE tree cost {up:F0} m up, {trees / MathF.PI:F0} trees a km2 within 1 km, a frame (median, GPU then render thread): ") +
                     string.Join(", ", states.Select((st, k) => string.Create(CultureInfo.InvariantCulture, $"{st.Name} {Median(gpu[k]):F2} / {Median(cpu[k]):F2} ms"))));
        }
    }
}
