using System;
using System.Collections.Generic;
using System.Linq;
using Planets.Gen;
using Planets.Look;
using PlanetSurface.Terrain;
using PlanetSurface.World;
using Rng = Planets.Gen.Rng;

namespace PlanetSurface.Tests;

/// <summary>Half the trees: the same fresh worlds and chunks grown at the old tree density and at the set one, trees counted per km².</summary>
public partial class FloraTest
{
    void TreeCount()
    {
        float keep = PlanetFlora.TreeDensity;
        foreach (var (a, v) in new[] { (Archetype.Temperate, 0), (Archetype.Temperate, 1), (Archetype.Alien, 1), (Archetype.Arid, 0) })
        {
            PlanetSpec spec = PlanetSpec.Roll(Fresh(), a, v, 8);
            ulong chunkSeed = Fresh();
            var per = new double[2];
            for (int pass = 0; pass < 2; pass++)
            {
                PlanetFlora.TreeDensity = pass == 0 ? 1f : keep;
                PlanetWorld w = PlanetWorld.Build(spec, null);
                var rng = new Rng(chunkSeed);
                int trees = 0, chunks = 60;
                for (int i = 0; i < chunks; i++)
                {
                    int fx = (int)(rng.NextFloat() * FarBuilder.Count(w.Terrain.Width)), fz = (int)(rng.NextFloat() * FarBuilder.Count(w.Terrain.Height));
                    var job = new FarJob { Fx = fx, Fz = fz, Lod = 0, SpanX = FarBuilder.Span(fx, w.Terrain.Width), SpanZ = FarBuilder.Span(fz, w.Terrain.Height) };
                    FarBuilder.Build(job, w.Source, w.Palette, new HashSet<long>());
                    if (job.Error != null) throw job.Error;
                    trees += job.Flora.Where(b => FloraMeshes_IsTree(b.Kind)).Sum(b => b.Count);
                }
                per[pass] = trees / (chunks * (double)FarBuilder.Size * FarBuilder.Size / 1e6);
            }
            PlanetFlora.TreeDensity = keep;
            double ratio = per[0] > 0 ? per[1] / per[0] : 1;
            Check(per[0] < 500 ? ratio is > 0.35 and < 0.65 : ratio is > 0.45 and < 0.55, $"{a} {v}: trees per km² {per[0]:F0} before, {per[1]:F0} after ({ratio:P0})");
        }
    }

    static bool FloraMeshes_IsTree(Flora k) => Planets.Surface.FloraMeshes.IsTree(k);
}
