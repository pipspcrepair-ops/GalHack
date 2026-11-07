using System;
using System.Linq;
using GalHack.Structures;
using Godot;
using PoiGenerator.Gen;

namespace PoiGenerator;

public partial class PoiTest
{
    /// <summary>A lamp set through PoiLights breathes in the view; a painted face becomes a glowing quad; a mood restyles a place.</summary>
    void LightsApi()
    {
        var rng = new Random();
        var fam = _lib.Families.First();
        var poi = PoiFactory.Generate(_lib, fam, 0, Rarity.Common, PoiState.Active, (ulong)rng.NextInt64());
        poi.Lights.Clear();
        Int3 at = poi.Layout.EntranceCell;
        var style = LightSettings.Make(0xff3322, LightAnim.Pulse, 1f, rng.Next(128));
        PoiLights.Add(poi, at, style);
        PoiLights.PaintFace(poi, at, Dir.North, LampShape.Border, LightSettings.Make(0x33ddff, LightAnim.Flicker, 0.5f, 3));
        var view = PoiView.Build(poi, Gen.Biome.Rock, new PoiView.Options(Collision: false, Lights: true, Figures: false));
        AddChild(view);
        var lamps = view.GetChildren().OfType<OmniLight3D>().Where(l => l.Visible).ToList();
        var lows = lamps.Select(_ => 99f).ToArray();
        var highs = lamps.Select(_ => -1f).ToArray();
        for (int i = 0; i < 10; i++)
        {
            view._Process(0.1);
            for (int k = 0; k < lamps.Count; k++)
            {
                lows[k] = Math.Min(lows[k], lamps[k].LightEnergy);
                highs[k] = Math.Max(highs[k], lamps[k].LightEnergy);
            }
        }
        bool pulses = lamps.Where((l, k) => highs[k] - lows[k] > 0.4f && l.LightColor.R > 0.9f && l.LightColor.G < 0.3f).Any();
        Check(pulses, "a lamp set by PoiLights.Set pulses red in the view");
        Check(view.LitQuads.Count == 1, "a face painted with PoiLights.PaintFace becomes a glowing quad");
        view.QueueFree();

        int n = PoiLights.SetMood(poi, LightMood.Dying);
        Check(n == poi.Lights.Count && poi.Lights.All(l => l.Style?.Anim == LightAnim.Flicker), "a dying mood makes every lamp flicker");
    }
}
