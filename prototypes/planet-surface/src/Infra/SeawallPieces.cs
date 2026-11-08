using System;
using System.Collections.Generic;
using GalHack.Deco;
using Planets.Gen;
using PlanetSurface.World;
using PoiGenerator.Gen;
using NV = System.Numerics.Vector3;

namespace PlanetSurface.Infra;

/// <summary>
/// A city's waterfronts (<see cref="CityPlan.Waterfront"/>): the wall under a waterfront road's water side, from
/// the top of the road down past the water or the beach, along the road's own smooth line (4 m stretches, no
/// steps). By tech and use: a stone or concrete seawall with a parapet; a quay with bollards where boats tied
/// up; a revetment of boulders where the drop is tall; timber piling and a rail on a low-tech world. Stairs go
/// down the face every 60 m or so. On a fallen world sections are cracked, slumped or breached, and weed and
/// barnacles darken the waterline.
/// </summary>
public partial class InfraLayer
{
    static readonly DecoRgb Weed = DecoRgb.Hex("3e4a32"), Crack = DecoRgb.Hex("4a4844"), Bollard = DecoRgb.Hex("3a3634"), Boulder = DecoRgb.Hex("6e675e");

    static IEnumerable<Piece> SeawallPieces(PlanetWorld world)
    {
        if (world.Infra?.City is not { } city) yield break;
        foreach (CityPlan.Seawall wall in city.Waterfront)
        {
            InfraRoute r = wall.Road;
            for (int k = 0; k + 1 < r.Count; k++)
            {
                if (r.Span[k] != GalHack.Terrain.RoadSpan.Ground) continue;
                yield return new SeawallStretch { Wall = wall, K = k, X = r.X[k], Z = r.Z[k] };
                // Stairs down to the water or the beach at intervals, clear of the ends.
                if (k % 15 == 7 && k + 4 < r.Count) yield return new SeawallStair { Wall = wall, K = k, X = r.X[k], Z = r.Z[k] };
            }
        }
    }

    /// <summary>Where a waterfront's wall stands at point k: its top on the wall's line, the way out over the water, and the foot in front of it.</summary>
    static (NV Top, NV Out, float Foot, float? Water) WallAt(TileBuilder b, InfraRoute r, int k)
    {
        int k0 = Math.Max(0, k - 1), k1 = Math.Min(r.Count - 1, k + 1);
        double dx = r.X[k1] - r.X[k0], dz = r.Z[k1] - r.Z[k0], len = Math.Max(1e-6, Math.Sqrt(dx * dx + dz * dz));
        // Left of the way is (-dz, dx); the wall's side says which way is out.
        double ox = -dz / len * r.WallSide, oz = dx / len * r.WallSide;
        float reach = r.HalfWidth + GalHack.Terrain.TerrainStamp.WallStep;
        double wx = r.X[k] + ox * reach, wz = r.Z[k] + oz * reach;
        // The ground in front, a little way out (the land's own there), and the water over it if any.
        double fx = wx + ox * 3, fz = wz + oz * 3;
        float foot = Math.Min(b.Ground(fx, fz), r.Y[k] - 1f);
        float? water = b.World.Source.WaterSurfaceAt(Mathx.WrapX(wx + ox * 6, b.World.Width), Mathx.Wrap(wz + oz * 6, b.World.Height), out float level) ? level : null;
        return (b.Local(wx, r.Y[k], wz), new NV((float)ox, 0, (float)oz), foot, water);
    }

    /// <summary>One 4 m stretch of a waterfront's wall.</summary>
    sealed class SeawallStretch : Piece
    {
        public CityPlan.Seawall Wall;
        public int K;

        public override void Emit(TileBuilder b)
        {
            InfraRoute r = Wall.Road;
            var (ta, outA, footA, waterA) = WallAt(b, r, K);
            var (tc, outC, footC, waterC) = WallAt(b, r, K + 1);
            NV along = tc - ta;
            float len = new NV(along.X, 0, along.Z).Length();
            if (len < 1e-3f) return;
            ulong hash = Planets.Gen.Hash.Of(Wall.Seed + (ulong)K * 7919UL, 0x5EA);
            float roll = (hash % 1000) / 1000f, roll2 = (hash >> 12) % 1000 / 1000f;
            // A breach: the sea got in and took the section. Its neighbours stand.
            if (roll < Wall.Fall * 0.08f) return;
            NV outward = NV.Normalize(outA + outC), dir = new NV(along.X, 0, along.Z) / len;
            float top = (ta.Y + tc.Y) * 0.5f, foot = Math.Min(footA, footC) - 1.5f;
            if (waterA is float wa) foot = Math.Min(foot, wa - 1.5f);
            // A slumped section has dropped and leant out a little.
            float slump = roll2 < Wall.Fall * 0.18f ? 0.5f + roll2 * 4f : 0f;
            NV mid = (ta + tc) * 0.5f;
            float height = top - foot;
            if (height < 0.4f) return;
            // Tall over a rough shore, a revetment of boulders instead of a face: heaped out from the wall's top.
            if (Wall.Tech != TechLevel.Low && height > 7f)
            {
                for (int i = 0; i < 5; i++)
                {
                    float t = (i + 0.5f) / 5f;
                    NV at = mid + outward * (t * height * 0.9f) + NV.UnitY * (top - mid.Y - t * height);
                    float size = 1.1f + ((hash >> (i * 5)) & 7) / 10f;
                    NV turn = NV.Normalize(dir + outward * ((((hash >> (i * 3)) & 3) - 1.5f) * 0.3f));
                    NV side = NV.Normalize(NV.Cross(NV.UnitY, turn));
                    b.Box(at, turn * size, NV.UnitY * (size * 0.8f), side * size, Boulder, DecoSurface.Matte, collide: true);
                }
                return;
            }
            const float Thick = 0.5f;
            NV centre = new NV(mid.X, (top - slump + foot) * 0.5f, mid.Z) + outward * (Thick + slump * 0.15f);
            if (Wall.Tech == TechLevel.Low)
            {
                // A timber revetment holding the bank: heavy planks laid edge on edge from the foot to the top, each
                // its own shade and the face leaning back a little into the bank, king piles driven in front of them
                // every two metres and cut off at the top, a capping beam over all. Nothing stands above the bank (a
                // rail along the top read as a fence).
                float h = top - slump - foot;
                const float Batter = 0.12f;
                // The backing the bank leans on (and what you bump into), behind the planks' faces.
                b.Box(new NV(mid.X, foot + h * 0.5f, mid.Z) - outward * 0.06f, dir * (len * 0.5f + 0.04f), NV.UnitY * (h * 0.5f), outward * 0.08f, TimberDark, DecoSurface.Matte, collide: true);
                int courses = Math.Clamp((int)MathF.Ceiling(h / 0.45f), 1, 10);
                float course = h / courses;
                for (int c = 0; c < courses; c++)
                {
                    float y = foot + (c + 0.5f) * course, back = Batter * (c + 0.5f) / courses;
                    ulong ch = Planets.Gen.Hash.Of(hash, c);
                    DecoRgb tone = Timber.Scale(0.82f + (ch % 100) / 400f);
                    // The waterline courses rot darker and greener.
                    if (waterA is float lv && y < lv + 0.4f) tone = tone.Lerp(Weed, 0.45f);
                    b.Box(new NV(mid.X, y, mid.Z) + outward * (0.24f - back), dir * (len * 0.5f + 0.02f), NV.UnitY * (course * 0.45f), outward * 0.06f, tone, DecoSurface.Matte, collide: false);
                }
                foreach (float t in new[] { 0.25f, 0.75f })
                {
                    NV p = ta + along * t + outward * 0.36f;
                    float cut = top - slump - 0.08f - ((hash >> (int)(t * 8)) & 3) * 0.06f;
                    b.Box(new NV(p.X, (cut + foot - 0.4f) * 0.5f, p.Z), dir * 0.13f, NV.UnitY * ((cut - foot + 0.4f) * 0.5f), outward * 0.13f, TimberDark, DecoSurface.Matte, collide: true);
                }
                if (roll2 > Wall.Fall * 0.3f)
                    b.Box(new NV(mid.X, top - slump - 0.06f, mid.Z) + outward * 0.2f, dir * (len * 0.5f + 0.03f), NV.UnitY * 0.09f, outward * 0.24f, TimberDark, DecoSurface.Matte, collide: true);
                return;
            }
            DecoRgb face = Wall.Tech == TechLevel.High ? Composite : (hash & 1) == 0 ? Concrete : Stone;
            // The wall: from below the water or the beach up to the walk along the top, a coping on it.
            b.Box(centre, dir * (len * 0.5f + 0.06f), NV.UnitY * ((top - slump - foot) * 0.5f), outward * Thick, face, DecoSurface.Matte, collide: true);
            b.Box(new NV(mid.X, top - slump + 0.08f, mid.Z) + outward * (Thick + slump * 0.15f), dir * (len * 0.5f + 0.06f), NV.UnitY * 0.1f, outward * (Thick + 0.12f), ConcreteDark, DecoSurface.Matte, collide: true);
            // Weed and barnacles where the water laps it, and a crack running down a fallen section.
            if (waterA is float level && level > foot && level < top)
            {
                NV w = new NV(mid.X, level, mid.Z) + outward * (Thick * 2f + slump * 0.15f + 0.03f);
                b.Quad(w - dir * (len * 0.5f) - NV.UnitY * 0.4f, w + dir * (len * 0.5f) - NV.UnitY * 0.4f, w + dir * (len * 0.5f) + NV.UnitY * 0.5f, w - dir * (len * 0.5f) + NV.UnitY * 0.5f,
                    Weed, DecoSurface.Matte, 1f, w - outward);
            }
            if (Wall.Fall > 0.4f && roll2 > 0.7f)
            {
                NV c0 = new NV(mid.X, top - slump - 0.2f, mid.Z) + outward * (Thick * 2f + slump * 0.15f + 0.02f) + dir * (len * (roll - 0.5f) * 0.6f);
                NV c1 = new NV(mid.X, foot + (top - foot) * 0.3f, mid.Z) + outward * (Thick * 2f + slump * 0.15f + 0.02f) + dir * (len * (roll2 - 0.5f) * 0.6f);
                b.Quad(c0 - dir * 0.06f, c0 + dir * 0.06f, c1 + dir * 0.06f, c1 - dir * 0.06f, Crack, DecoSurface.Matte, 1f, c0 - outward);
            }
            if (Wall.Quay)
            {
                // A quay: bollards along the edge to tie up to, every third stretch.
                if (K % 3 == 0) b.Box(ta + outward * 0.6f + NV.UnitY * (0.35f - slump), dir * 0.22f, NV.UnitY * 0.35f, outward * 0.22f, Bollard, DecoSurface.Matte, collide: true);
                return;
            }
            // A parapet along the top, broken where the wall slumped and here and there on a fallen world.
            if (slump > 0f || roll2 < Wall.Fall * 0.25f) return;
            if (K % 15 == 7) return; // the stair's head
            b.Box(new NV(mid.X, top + 0.45f, mid.Z) + outward * 0.25f, dir * (len * 0.5f + 0.02f), NV.UnitY * 0.45f, outward * 0.2f, face, DecoSurface.Matte, collide: true);
        }
    }

    /// <summary>Stairs down a waterfront's wall: from the top of the wall at point k, down along its face to the water or the beach.</summary>
    sealed class SeawallStair : Piece
    {
        public CityPlan.Seawall Wall;
        public int K;

        public override void Emit(TileBuilder b)
        {
            InfraRoute r = Wall.Road;
            var (ta, outward, foot, water) = WallAt(b, r, K);
            var (tc, _, _, _) = WallAt(b, r, K + 1);
            NV along = new NV(tc.X - ta.X, 0, tc.Z - ta.Z);
            if (along.Length() < 1e-3f) return;
            NV dir = NV.Normalize(along);
            float bottom = Math.Max(foot + 1.5f, (water ?? float.MinValue) - 0.3f), drop = ta.Y - bottom;
            if (drop < 1f || drop > 7f) return;
            int steps = (int)MathF.Ceiling(drop / 0.25f);
            DecoRgb stone = Wall.Tech == TechLevel.Low ? Timber : ConcreteDark;
            // Down along the face, a step a quarter metre down and 0.3 m along, each a block to the ground under it.
            for (int i = 0; i < steps; i++)
            {
                float y = ta.Y - (i + 1) * 0.25f;
                NV at = ta + outward * 1.75f + dir * (0.15f + i * 0.3f);
                float under = Math.Min(y, bottom) - 0.5f;
                b.Box(new NV(at.X, (y + under) * 0.5f, at.Z), dir * 0.15f, NV.UnitY * ((y - under) * 0.5f), outward * 0.7f, stone, DecoSurface.Matte, collide: true);
            }
        }
    }
}
