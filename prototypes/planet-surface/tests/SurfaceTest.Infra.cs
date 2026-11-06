using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using GalHack.Terrain;
using Godot;
using Planets.Gen;
using PlanetSurface.World;
using PoiGenerator.Gen;
using Archetype = Planets.Gen.Archetype;
using PoiSite = PlanetSurface.Poi.PoiSite;

namespace PlanetSurface;

/// <summary>The world around the places: the profile, and the old world's infrastructure.</summary>
public partial class SurfaceTest
{
    // ---- the world profile ------------------------------------------------------------------

    async Task ProfileChecks()
    {
        // The same seed rolls the same profile; seeds differ widely.
        int same = 0, inhabited = 0, start = 0, rolls = 0;
        var before = new Dictionary<TechLevel, int>();
        var now = new Dictionary<TechLevel, int>();
        int lower = 0, higher = 0;
        for (ulong seed = 1; seed <= 300; seed++)
        {
            var spec = PlanetSpec.Roll(seed, (Archetype)(seed % 5), (int)(seed % 2));
            WorldProfile p = PlanetWorld.ProfileFor(spec), q = PlanetWorld.ProfileFor(spec), s = PlanetWorld.ProfileFor(spec, startWorld: true);
            rolls++;
            if (p == q) same++;
            if (p.Inhabited) inhabited++;
            if (s.Inhabited && s.Now <= s.Before) start++;
            if (p.Inhabited) before[p.Before] = before.GetValueOrDefault(p.Before) + 1;
            now[p.Now] = now.GetValueOrDefault(p.Now) + 1;
            if (p.Inhabited && p.Now < p.Before) lower++;
            if (p.Inhabited && p.Now > p.Before) higher++;
        }
        Check(same == rolls, $"the same seed rolls the same world profile ({same} of {rolls})");
        Check(start == rolls, $"start worlds were always inhabited, and no faction brings newer tech to them ({start} of {rolls})");
        Check(inhabited > rolls * 0.6 && inhabited < rolls, $"most worlds were inhabited, some never were ({inhabited} of {rolls})");
        Check(before.Count == 3 && now.Count == 3 && before.Values.Min() > inhabited / 6 && lower > inhabited / 4 && higher > rolls / 20,
            $"tech levels spread (before the fall {string.Join(", ", before.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value}"))}; now {string.Join(", ", now.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value}"))}); survivors below their ruins on {lower} worlds, newer tech on older ruins on {higher}");
        await Frames(1);
    }

}
