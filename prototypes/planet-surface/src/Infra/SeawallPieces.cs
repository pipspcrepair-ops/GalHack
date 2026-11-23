using System;
using System.Collections.Generic;
using GalHack.Deco;
using Planets.Gen;
using PlanetSurface.World;
using PoiGenerator.Gen;
using NV = System.Numerics.Vector3;
using Hash = Planets.Gen.Hash;
using NQ = System.Numerics.Quaternion;
using SInt3 = GalHack.Structures.Int3;

namespace PlanetSurface.Infra;

/// <summary>
/// A city's waterfronts (<see cref="CityPlan.Waterfront"/>): the wall under a waterfront road's water side, from
/// the top of the road down past the water or the beach, along the road's own line, a block structure a 4 m
/// stretch turned to it. By tech and use: a stone or concrete seawall with its coping and a parapet; a quay with
/// bollards where boats tied up; a revetment of sloped boulder blocks stepping out where the drop is tall; timber
/// planking and king piles on a low-tech world. Stairs go down the face every 60 m or so. On a fallen world
/// sections are slumped and leaning or breached, and weed darkens the waterline.
/// </summary>
public partial class InfraLayer
{
    static readonly DecoRgb Weed = DecoRgb.Hex("3e4a32"), Bollard = DecoRgb.Hex("3a3634"), Boulder = DecoRgb.Hex("6e675e");

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

    /// <summary>Where a waterfront's wall stands at point k (a world point): its top on the wall's line, the way out over the water, the foot in front of it, and the water there.</summary>
    static (NV Top, NV Out, float Foot, float? Water) WallAt(PlanetWorld w, InfraRoute r, int k)
    {
        int k0 = Math.Max(0, k - 1), k1 = Math.Min(r.Count - 1, k + 1);
        double dx = r.X[k1] - r.X[k0], dz = r.Z[k1] - r.Z[k0], len = Math.Max(1e-6, Math.Sqrt(dx * dx + dz * dz));
        // Left of the way is (-dz, dx); the wall's side says which way is out.
        double ox = -dz / len * r.WallSide, oz = dx / len * r.WallSide;
        float reach = r.HalfWidth + GalHack.Terrain.TerrainStamp.WallStep;
        double wx = r.X[k] + ox * reach, wz = r.Z[k] + oz * reach;
        // The ground in front, a little way out (the land's own there), and the water over it if any.
        double fx = wx + ox * 3, fz = wz + oz * 3;
        float foot = Math.Min(w.Source.SurfaceHeight(Mathx.WrapX(fx, w.Width), Mathx.Wrap(fz, w.Height)), r.Y[k] - 1f);
        float? water = w.Source.WaterSurfaceAt(Mathx.WrapX(wx + ox * 6, w.Width), Mathx.Wrap(wz + oz * 6, w.Height), out float level) ? level : null;
        return (new NV((float)wx, r.Y[k], (float)wz), new NV((float)ox, 0, (float)oz), foot, water);
    }

    /// <summary>One 4 m stretch of a waterfront's wall, its +X out over the water.</summary>
    sealed class SeawallStretch : BlockPiece
    {
        public CityPlan.Seawall Wall;
        public int K;

        protected override void Lay(Laying l)
        {
            InfraRoute r = Wall.Road;
            var (ta, outA, footA, waterA) = WallAt(l.World, r, K);
            var (tc, outC, footC, _) = WallAt(l.World, r, K + 1);
            float len = new NV(tc.X - ta.X, 0, tc.Z - ta.Z).Length();
            if (len < 1e-3f) return;
            ulong hash = Hash.Of(Wall.Seed + (ulong)K * 7919UL, 0x5EA);
            float roll = (hash % 1000) / 1000f, roll2 = (hash >> 12) % 1000 / 1000f;
            // A breach: the sea got in and took the section. Its neighbours stand.
            if (roll < Wall.Fall * 0.08f) return;
            NV outward = NV.Normalize(outA + outC), way = NV.Cross(NV.UnitY, outward);
            float top = (ta.Y + tc.Y) * 0.5f, foot = Math.Min(footA, footC) - 1.5f;
            if (waterA is float wa) foot = Math.Min(foot, wa - 1.5f);
            if (top - foot < 0.4f) return;
            // A slumped section has dropped and leant out a little.
            float slump = roll2 < Wall.Fall * 0.18f ? 0.5f + roll2 * 4f : 0f;
            Turn = slump > 0f ? NQ.Concatenate(Frame(way), NQ.CreateFromAxisAngle(way, slump * 0.05f)) : Frame(way);
            // Row 0 is the wall's top metre, column 0 the metre out from its line, the cells running on from the stretch's end behind its way.
            NV start = NV.Dot(way, tc - ta) >= 0f ? ta : tc;
            Origin = new NV(start.X, top - slump - 0.5f, start.Z) + way * 0.5f + outward * 0.5f;
            int n = Math.Max(1, (int)MathF.Round(len)), low = (int)MathF.Floor(foot - (top - slump) + 0.5f), g = Wall.Fall > 0.4f ? 3 : 2;
            if (Wall.Tech != TechLevel.Low && top - foot > 7f)
            {
                // Tall over a rough shore, a revetment of boulders stepping out and down from the wall's top, a slope atop each step.
                Laying.Stuff boulder = l.Of("concrete-plated", Boulder, g);
                int slope = Laying.Rot(new SInt3(0, 1, 0), new SInt3(1, 0, 0));
                for (int x = 0; -x > low; x++)
                    for (int z = 0; z < n; z++)
                    {
                        if (-x - 1 >= low) l.Fill(x, x, low, -x - 1, -z, -z, boulder);
                        l.Put(x, -x, -z, boulder, "slope", slope);
                    }
                return;
            }
            if (Wall.Tech == TechLevel.Low)
            {
                // A timber revetment holding the bank: heavy planking from the foot to the top, each course its own shade (the
                // waterline's rotting darker and greener), king piles driven in front every two metres and cut off at the top,
                // a capping beam over all. Nothing stands above the bank.
                for (int y = low; y <= 0; y++)
                {
                    DecoRgb tone = y == 0 ? TimberDark : Timber.Scale(0.82f + Hash.Of(hash, y + 64) % 100 / 400f);
                    if (y < 0 && waterA is float lv && top - slump - 0.5f + y < lv + 0.4f) tone = tone.Lerp(Weed, 0.45f);
                    l.Fill(0, 0, y, y, 0, -(n - 1), l.Of("base-block", tone, g));
                }
                Laying.Stuff pile = l.Of("base-block", TimberDark, g);
                for (int z = 1; z < n; z += 2)
                    for (int y = low; y <= -1 - (int)((hash >> z) & 1); y++) l.Put(1, y, -z, pile, "cylinder");
                return;
            }
            DecoRgb face = Wall.Tech == TechLevel.High ? Composite : (hash & 1) == 0 ? Concrete : Stone;
            Laying.Stuff wall = l.Of("concrete-plated", face, g), coping = l.Of("concrete-plated", ConcreteDark, g), weed = l.Of("concrete-plated", face.Lerp(Weed, 0.7f), 3);
            // The wall from below the water or the beach up to the walk along the top, a coping on it, weed where the water laps it.
            for (int y = low; y <= 0; y++)
            {
                float at = top - slump - 0.5f + y;
                bool wet = waterA is float level && at > level - 0.9f && at < level + 0.5f;
                l.Fill(0, 0, y, y, 0, -(n - 1), y == 0 ? coping : wet ? weed : wall);
            }
            if (Wall.Quay)
            {
                // A quay: a bollard on the edge to tie up to, every third stretch.
                if (K % 3 == 0) l.Put(0, 1, 0, l.Of("iron-plated", Bollard, g), "cylinder");
                return;
            }
            // A parapet along the top, broken where the wall slumped, here and there on a fallen world, and at a stair's head.
            if (slump > 0f || roll2 < Wall.Fall * 0.25f || K % 15 == 7) return;
            l.Fill(0, 0, 1, 1, 0, -(n - 1), wall);
        }
    }

    /// <summary>Stairs down a waterfront's wall: from the top of the wall at point k, down along its face toward the next point to the water or the beach, a stair block a metre on the fill under it.</summary>
    sealed class SeawallStair : BlockPiece
    {
        public CityPlan.Seawall Wall;
        public int K;

        protected override void Lay(Laying l)
        {
            InfraRoute r = Wall.Road;
            var (ta, outward, foot, water) = WallAt(l.World, r, K);
            var (tc, _, _, _) = WallAt(l.World, r, K + 1);
            NV along = new(tc.X - ta.X, 0, tc.Z - ta.Z);
            if (along.Length() < 1e-3f) return;
            float bottom = Math.Max(foot + 1.5f, (water ?? float.MinValue) - 0.3f), drop = ta.Y - bottom;
            if (drop < 1f || drop > 7f) return;
            NV dir = NV.Normalize(along);
            Turn = Frame(dir);
            Origin = new NV(ta.X, ta.Y - 0.5f, ta.Z) + outward * 1.5f + dir * 0.5f;
            int g = Wall.Fall > 0.4f ? 3 : 2;
            Laying.Stuff step = l.Of("stairs", ConcreteDark, g), fill = Wall.Tech == TechLevel.Low ? l.Of("base-block", Timber, g) : l.Of("concrete-plated", ConcreteDark, g);
            int steps = (int)MathF.Ceiling(drop), low = (int)MathF.Floor(foot - ta.Y + 0.5f);
            for (int i = 0; i < steps; i++)
            {
                l.Put(0, -i, -i, step);
                if (-i - 1 >= low) l.Fill(0, 0, low, -i - 1, -i, -i, fill);
            }
        }
    }
}
