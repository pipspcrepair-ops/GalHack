using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Planets.Gen;
using PlanetSurface.Terrain;
using PlanetSurface.World;
using Archetype = Planets.Gen.Archetype;

namespace PlanetSurface;

/// <summary>The far land's shade (past the sun's real-time shadows): ridges shade valleys away from the sun, a higher sun shades less, and the shade hands over to the real shadows with no gap or step.</summary>
public partial class SurfaceTest
{
    async Task ShadeChecks()
    {
        var rand = new Random();
        PlanetWorld world = null;
        // A world with relief to shade: the first of a few rolls whose land stands over 150 m between its high and low ground.
        for (int tries = 0; tries < 6 && world == null; tries++)
        {
            var spec = PlanetSpec.Roll((ulong)rand.NextInt64(), tries % 2 == 0 ? Archetype.Temperate : Archetype.Arid, 0, 16);
            PlanetWorld w = await Task.Run(() => PlanetWorld.Build(spec, Main.Catalog));
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i < 400; i++)
            {
                float g = w.GroundHeight(2000 + rand.NextDouble() * (w.Width - 4000), 2000 + rand.NextDouble() * (w.Height - 4000));
                if (g < 3f) continue;
                lo = MathF.Min(lo, g);
                hi = MathF.Max(hi, g);
            }
            if (hi - lo > 150f || tries == 5) world = w;
        }
        float cx = (float)(world.Width * 0.5), cz = (float)(world.Height * 0.5), half = 3200f;
        var toward = new Vector2(1f, 0.3f).Normalized();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        FarShade shade = await Task.Run(() => FarShade.Build(world, cx, cz, half, toward));
        double builtIn = clock.Elapsed.TotalSeconds;
        float cell = shade.Cell;

        // Ridges over valleys: a cell's rise is at least the steepest rise of the bare ground toward the sun (the trees and places only add to it).
        int cells = 0, short1 = 0, lit = 0, dark = 0;
        float worstShort = 0f;
        for (int n = 0; n < 1500; n++)
        {
            int i = rand.Next(8, FarShade.N - 8), j = rand.Next(8, FarShade.N - 8);
            float x = shade.X0 + (i + 0.5f) * cell, z = shade.Z0 + (j + 0.5f) * cell;
            float g0 = MathF.Max(world.GroundHeight(x, z), 0f);
            float t = -1f;
            for (int k = 1; k < 60; k++)
            {
                float px = x + toward.X * k * cell, pz = z + toward.Y * k * cell;
                if (px < shade.X0 || pz < shade.Z0 || px >= shade.X0 + shade.Side || pz >= shade.Z0 + shade.Side) break;
                t = MathF.Max(t, (MathF.Max(world.GroundHeight(px, pz), 0f) - g0 - 1f) / (k * cell));
            }
            float rise = shade.Rise[j * FarShade.N + i];
            cells++;
            // (The shade reads the ground a cell's width off the exact spot, so a hair short is allowed.)
            if (rise < t - 0.06f) { short1++; worstShort = MathF.Max(worstShort, t - rise); }
            if (shade.Lit(x, z, 0.5f) > 0.99f) lit++;
            if (shade.Lit(x, z, 0.02f) < 0.01f) dark++;
        }
        Check(short1 <= cells / 50, $"far shade: {cells - short1} of {cells} cells' rise covers the bare ground's steepest rise toward the sun ({short1} short, worst {worstShort:F3}; built in {builtIn:F1} s)");

        // A higher sun shades less: the share of the land in shade falls as the sun climbs, from some at a sun 1 degree up to little at 40.
        float Share(float tangent)
        {
            int s = 0, total = 0;
            for (int j = 4; j < FarShade.N - 4; j += 3)
            for (int i = 4; i < FarShade.N - 4; i += 3) { total++; if (shade.Lit(shade.X0 + (i + 0.5f) * cell, shade.Z0 + (j + 0.5f) * cell, tangent) < 0.5f) s++; }
            return s / (float)total;
        }
        float low = Share(MathF.Tan(1f * MathF.PI / 180f)), mid = Share(MathF.Tan(8f * MathF.PI / 180f)), high = Share(MathF.Tan(40f * MathF.PI / 180f));
        Check(low > mid && mid >= high && low > 0.02f, $"far shade: a sun 1, 8 and 40 degrees up shades {low * 100:F1}%, {mid * 100:F1}% and {high * 100:F1}% of the land (falling, some at a low sun)");
        Check(high < low / 4, $"far shade: a sun 40 degrees up leaves most of the land lit ({high * 100:F1}% shaded against {low * 100:F1}% at 1 degree)");

        // Shade lies away from the sun: a shaded cell has higher ground (or trees, or a building) toward the sun, never nothing there.
        int away = 0, checkedShaded = 0;
        float tan8 = MathF.Tan(8f * MathF.PI / 180f);
        for (int j = 8; j < FarShade.N - 8; j += 2)
        for (int i = 8; i < FarShade.N - 8; i += 2)
        {
            if (shade.Rise[j * FarShade.N + i] <= tan8 + 0.03f) continue;
            checkedShaded++;
            // Look along the sun's way for something that tall: bare ground to the tangent or less than 20 m of trees on top of it.
            float x = shade.X0 + (i + 0.5f) * cell, z = shade.Z0 + (j + 0.5f) * cell;
            float g0 = MathF.Max(world.GroundHeight(x, z), 0f) + 1f, best = -1f;
            for (int k = 1; k <= FarShade.Look / cell && k < 400; k++)
            {
                float px = x + toward.X * k * cell, pz = z + toward.Y * k * cell;
                if (px < shade.X0 || pz < shade.Z0 || px >= shade.X0 + shade.Side || pz >= shade.Z0 + shade.Side) break;
                best = MathF.Max(best, (MathF.Max(world.GroundHeight(px, pz), 0f) + 35f - g0) / (k * cell));
            }
            if (best >= tan8) away++;
        }
        Check(checkedShaded == 0 || away >= checkedShaded * 0.97f, $"far shade: {away} of {checkedShaded} shaded cells have ground, trees or a building toward the sun that stands over the sun");

        // The hand-over: the far shade comes in over 70% to 85% of the shadow distance and the real shadows fade from 85% to 100%, so at any
        // distance one of them holds in full, and neither steps: a strip of ground half in shade is as dark all the way along.
        foreach (float reach in new[] { 320f, 800f, 1500f })
        {
            float worstCover = 1f, worstStep = 0f, lastReal = 1f, lastFar = 0f;
            for (float d = 0f; d <= reach * 1.3f; d += reach / 2000f)
            {
                float f = FarShade.Weight(d, reach), r = FarShade.RealWeight(d, reach);
                // A shadow both draw is as dark as the darker of the two.
                worstCover = MathF.Min(worstCover, MathF.Max(f, r));
                worstStep = MathF.Max(worstStep, MathF.Max(MathF.Abs(f - lastFar), MathF.Abs(r - lastReal)));
                lastFar = f;
                lastReal = r;
            }
            Check(worstCover > 0.999f, $"shadow distance {reach:F0} m: real shadows and far shade cover every distance (the least held at any: {worstCover:F3})");
            Check(worstStep < 0.01f, $"shadow distance {reach:F0} m: neither the real shadows nor the far shade step (largest change per {reach / 2000f:F2} m: {worstStep:F4})");
        }
    }
}
