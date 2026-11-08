using System;
using System.Collections.Generic;
using PoiGenerator;
using PoiGenerator.Gen;

namespace PlanetSurface.Poi;

/// <summary>
/// How far a place's far mesh stands off its merged blocks, as the eye would judge it at the switch:
/// the outside's extent (each way, as a share of the blocks') and its colour (the mean of the faces
/// seen from outside, by area, each channel 0..1). Tests and measurements read it.
/// </summary>
public partial class PoiLayer
{
    public static (float Size, float Tone) StandInGap(PoiSite site)
    {
        BlockGrid g = site.Instance.Blocks;
        CellMask occupied = site.Instance.Layout.Occupied;
        var far = Outside(new List<ChunkMesh> { Mesher.Coarse(g, occupied) }, g);
        var near = Outside(Mesher.Build(g, occupied, MidOptions(g)), g);
        if (near.Area <= 0f || far.Area <= 0f) return (1f, 1f);
        float size = 0f;
        for (int k = 0; k < 3; k++)
        {
            float a = near.Hi[k] - near.Lo[k], b = far.Hi[k] - far.Lo[k];
            size = MathF.Max(size, MathF.Abs(b - a) / MathF.Max(a, 1f));
        }
        float tone = 0f;
        for (int k = 0; k < 3; k++) tone += MathF.Abs(far.Colour[k] - near.Colour[k]) / 3f;
        return (size, tone);
    }

    /// <summary>The faces of a drawing that the outside sees (nothing solid of the place stands in front of them): their extent above ground, and their mean colour by area.</summary>
    static (float[] Lo, float[] Hi, float[] Colour, float Area) Outside(List<ChunkMesh> meshes, BlockGrid g)
    {
        float[] lo = { float.MaxValue, float.MaxValue, float.MaxValue }, hi = { float.MinValue, float.MinValue, float.MinValue }, colour = new float[3];
        float area = 0f;
        Int3 min = g.Min, max = g.Min + g.Size;
        foreach (ChunkMesh m in meshes)
            for (int s = 0; s < m.Surfaces.Length; s++)
            {
                if (s == 3) continue; // glass: seen through
                MeshBuffer b = m.Surfaces[s];
                for (int t = 0; t + 2 < b.Indices.Count; t += 3)
                {
                    int i0 = b.Indices[t], i1 = b.Indices[t + 1], i2 = b.Indices[t + 2];
                    float ax = b.Positions[i0 * 3], ay = b.Positions[i0 * 3 + 1], az = b.Positions[i0 * 3 + 2];
                    float bx = b.Positions[i1 * 3] - ax, by = b.Positions[i1 * 3 + 1] - ay, bz = b.Positions[i1 * 3 + 2] - az;
                    float cx = b.Positions[i2 * 3] - ax, cy = b.Positions[i2 * 3 + 1] - ay, cz = b.Positions[i2 * 3 + 2] - az;
                    float nx = by * cz - bz * cy, ny = bz * cx - bx * cz, nz = bx * cy - by * cx;
                    float a = MathF.Sqrt(nx * nx + ny * ny + nz * nz) * 0.5f;
                    if (a <= 1e-4f) continue;
                    nx /= 2f * a; ny /= 2f * a; nz /= 2f * a;
                    if (ny < -0.5f) continue; // undersides: never seen from the ground or the air
                    float px = ax + (bx + cx) / 3f, py = ay + (by + cy) / 3f, pz = az + (bz + cz) / 3f;
                    if (py < 0f) continue; // buried
                    if (!Open(g, px + nx * 0.5f, py + ny * 0.5f, pz + nz * 0.5f, nx, ny, nz, min, max)) continue;
                    float r = 0f, gr = 0f, bl = 0f;
                    foreach (int i in new[] { i0, i1, i2 })
                    {
                        r += b.Colors[i * 4] / 3f;
                        gr += b.Colors[i * 4 + 1] / 3f;
                        bl += b.Colors[i * 4 + 2] / 3f;
                    }
                    colour[0] += r * a; colour[1] += gr * a; colour[2] += bl * a;
                    area += a;
                    float[] p = { px, py, pz };
                    for (int k = 0; k < 3; k++)
                    {
                        lo[k] = MathF.Min(lo[k], p[k]);
                        hi[k] = MathF.Max(hi[k], p[k]);
                    }
                }
            }
        if (area > 0f) for (int k = 0; k < 3; k++) colour[k] /= area;
        return (lo, hi, colour, area);
    }

    /// <summary>Whether a line out from a point along a face's normal leaves the grid without meeting a full cube.</summary>
    static bool Open(BlockGrid g, float x, float y, float z, float nx, float ny, float nz, Int3 min, Int3 max)
    {
        for (int step = 0; step < 512; step++)
        {
            var c = new Int3((int)MathF.Floor(x), (int)MathF.Floor(y), (int)MathF.Floor(z));
            if (c.X < min.X || c.Y < min.Y || c.Z < min.Z || c.X >= max.X || c.Y >= max.Y || c.Z >= max.Z) return true;
            Block b = g.Get(c);
            if (!b.IsAir && BlockInfo.IsFullCube(b.Type) && !BlockInfo.IsTransparent(b.Material)) return false;
            x += nx; y += ny; z += nz;
        }
        return true;
    }
}
