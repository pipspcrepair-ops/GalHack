using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Planets.Gen;

namespace Planets;

/// <summary>
/// Orbit starts at 1,060 m, so land stops short of it: nothing on any world stands over the ceiling
/// in the archetype data. Most ranges top out about 500 m. Only the highest peaks of the biggest
/// ranges climb to 700-900 m, up into the cloud, so they stand out. High ground carries ice.
/// </summary>
public partial class GenTest
{
    const int CeilingSeeds = 14;

    void TerrainCeiling()
    {
        var byLandform = new float[LandformInfo.Count];
        var lines = new List<string>();
        float worst = 0f, ceiling = 0f;
        string worstWhere = "";
        int maps = 0;
        long mountainLand = 0, over600 = 0;
        var rangeTops = new List<float>();
        long highGround = 0, iced = 0;
        foreach (var (a, v) in Variants())
        {
            float top = 0f;
            var tops = new List<float>();
            for (int s = 0; s < CeilingSeeds; s++)
            {
                // Every few worlds the modifiers that lift land most, at a size the roll can give.
                Modifier mods = s % 3 == 0 ? Modifier.Highlands : s % 3 == 1 ? Modifier.Highlands | Modifier.Mesas : Modifier.Canyons;
                var spec = new PlanetSpec((ulong)(7300 + s * 29 + (int)a * 7 + v), a, v, mods, s % 5 == 4 ? 32 : 16);
                WorldMap map = PlanetGenerator.Generate(spec);
                maps++;
                ceiling = map.Def.TerrainCeiling;
                float high = 0f;
                int peak = 0;
                var range = new List<float>();
                for (int k = 0; k < map.Count; k++)
                {
                    float e = map.Elevation[k];
                    if (e > high) (high, peak) = (e, k);
                    int l = map.Landform[k];
                    if (e > byLandform[l]) byLandform[l] = e;
                    if (map.Landform[k] == (byte)Landform.Mountains && e > 0f)
                    {
                        range.Add(e);
                        mountainLand++;
                        if (e > 600f) over600++;
                    }
                    // High ground on the worlds that have ice: temperate and ice.
                    if (a is Archetype.Temperate or Archetype.Ice && v == 0 && e > 460f)
                    {
                        highGround++;
                        if ((Biome)map.Biome[k] is Biome.Snow or Biome.Glacier or Biome.Snowfield or Biome.BlueIce) iced++;
                    }
                }
                if (range.Count > 300)
                {
                    range.Sort();
                    rangeTops.Add(range[(int)(range.Count * 0.98f)]);
                }
                // The fine ground round the highest cell: finer noise must not poke through either.
                var sampler = new SurfaceSampler(map);
                double px = map.CellX(peak % map.Width), py = map.CellY(peak / map.Width);
                for (int j = -6; j <= 6; j++)
                for (int i = -6; i <= 6; i++)
                    high = Math.Max(high, sampler.HeightAt(px + i * 6.0, py + j * 6.0, 1));
                tops.Add(high);
                top = Math.Max(top, high);
                if (high > worst) (worst, worstWhere) = (high, $"{a}/{map.Def.VariantName} seed {spec.Seed} ({mods})");
            }
            tops.Sort();
            lines.Add($"{a}/{v}: tallest {top:F0} m, median world {tops[tops.Count / 2]:F0} m");
        }
        GD.Print("      " + string.Join("; ", lines));
        GD.Print("      by landform: " + string.Join(", ", LandformInfo.All.Select(l => $"{LandformInfo.Name(l)} {byLandform[(int)l]:F0}")));
        Check(worst <= ceiling, $"terrain ceiling: over {maps} worlds of every variant the highest ground is {worst:F1} m ({worstWhere}), at or below the {ceiling:F0} m ceiling");

        rangeTops.Sort();
        float typical = rangeTops[rangeTops.Count / 2], share = over600 / (float)Math.Max(1, mountainLand);
        Check(typical is >= 380f and <= 620f, $"most ranges top out about 500 m: over {rangeTops.Count} worlds with ranges, the 98th percentile of mountain land is {typical:F0} m at the median ({rangeTops[0]:F0}-{rangeTops[^1]:F0} m), 380-620 m");
        Check(share <= 0.04f && worst >= 700f, $"peaks above 600 m are rare: {share:P1} of mountain land (4%), and the greatest reach {worst:F0} m (700)");

        var others = LandformInfo.All.Where(l => l is not (Landform.Mountains)).OrderByDescending(l => byLandform[(int)l]).ToList();
        float other = byLandform[(int)others[0]];
        Check(other <= 640f, $"other landforms stay on the 500 m scale: the highest is {LandformInfo.Name(others[0])} at {other:F0} m (640)");
        float icedShare = iced / (float)Math.Max(1, highGround);
        Check(highGround > 0 && icedShare >= 0.6f, $"high ground carries ice: {icedShare:P0} of temperate and ice ground over 460 m ({highGround} cells) is snow or ice (60%)");
    }
}
