using System;
using System.Collections.Generic;
using GalHack.Terrain;
using Godot;
using Planets.Gen;

namespace PlanetSurface.Terrain;

/// <summary>
/// The made ground round the places as the terrain shader draws it: every <see cref="GroundPatch"/> packed in a
/// texture, with a grid of 16 m cells listing the patches that can touch each. The shader finds the patches over
/// each pixel and paints the top one from its exact outline (slab joints, bay lines, cracks, weeds in the seams,
/// the land creeping in from the edge), so a yard reads the same crisp way at any distance and from the air,
/// where vertex colours at the mesh's 1 to 32 m spacing only stained the ground in blotches.
///
/// Patches are four texels each in <see cref="Patches"/> (1,024 to a row):
///   0: centre x, z (canonical), U axis x, z
///   1: half length (U), half width (V), corner radius, ground height
///   2: cover, pattern, wear, layer
///   3: kerbed, slab size, a seed, how far off its height it still paints
/// <see cref="Cells"/> holds each cell's first entry in <see cref="Index"/> and its count; the index lists patches.
/// </summary>
public sealed class GroundField
{
    public const float Cell = 16f;
    public const int PatchesPerRow = 1024, IndexRow = 4096;

    /// <summary>How far past a patch's outline the shader can paint (the crumbling edge and kerb rubble), m.</summary>
    public const float Reach = 3f;

    /// <summary>The most patches a cell lists (the shader reads no more).</summary>
    public const int MostPerCell = 32;

    public ImageTexture Patches, Cells, Index;
    /// <summary>Cell size, cells across, cells down, patch count.</summary>
    public Vector4 Grid;
    public int PatchCount, Entries, Crowded;

    public static GroundField Build(IReadOnlyList<GroundPatch> patches, double width, double height)
    {
        int n = patches.Count;
        int rows = Math.Max(1, (n + PatchesPerRow - 1) / PatchesPerRow);
        var data = new float[rows * PatchesPerRow * 16];
        for (int i = 0; i < n; i++)
        {
            GroundPatch p = patches[i];
            int o = i * 16;
            data[o] = (float)Mathx.WrapX(p.X, width); data[o + 1] = (float)Mathx.Wrap(p.Z, height); data[o + 2] = p.UX; data[o + 3] = p.UZ;
            data[o + 4] = p.HalfU; data[o + 5] = p.HalfV; data[o + 6] = Math.Min(p.Round, Math.Min(p.HalfU, p.HalfV)); data[o + 7] = p.Y;
            data[o + 8] = (float)p.Cover; data[o + 9] = (float)p.Pattern; data[o + 10] = p.Wear; data[o + 11] = p.Layer;
            data[o + 12] = p.Kerb ? 1f : 0f; data[o + 13] = Math.Max(0.3f, p.Slab); data[o + 14] = (float)((uint)(p.X * 73.0 + p.Z * 31.0) % 997u); data[o + 15] = p.Give;
        }

        int cellsX = (int)Math.Ceiling(width / Cell), cellsZ = (int)Math.Ceiling(height / Cell);
        var lists = new Dictionary<int, List<int>>();
        for (int i = 0; i < n; i++)
        {
            GroundPatch p = patches[i];
            // The patch's box on the map, padded by the reach, every cell it touches (sampled every half cell).
            float ex = MathF.Abs(p.UX) * p.HalfU + MathF.Abs(p.UZ) * p.HalfV + Reach, ez = MathF.Abs(p.UZ) * p.HalfU + MathF.Abs(p.UX) * p.HalfV + Reach;
            double x0 = p.X - ex, x1 = p.X + ex, z0 = p.Z - ez, z1 = p.Z + ez;
            var seen = new HashSet<int>();
            for (double z = z0; ; z = Math.Min(z + Cell * 0.5, z1))
            {
                for (double x = x0; ; x = Math.Min(x + Cell * 0.5, x1))
                {
                    double wx = Mathx.WrapX(x, width), wz = Mathx.Wrap(z, height);
                    int cx = Math.Min(cellsX - 1, (int)(wx / Cell)), cz = Math.Min(cellsZ - 1, (int)(wz / Cell));
                    int key = cz * cellsX + cx;
                    if (seen.Add(key))
                    {
                        if (!lists.TryGetValue(key, out List<int> list)) lists[key] = list = new List<int>(4);
                        list.Add(i);
                    }
                    if (x >= x1) break;
                }
                if (z >= z1) break;
            }
        }

        int entries = 0, crowded = 0;
        foreach (List<int> l in lists.Values)
        {
            // The shader reads only so many: where a cell has more, the smallest go (a path's end, not the yard under it).
            if (l.Count > MostPerCell)
            {
                crowded++;
                l.Sort((a, b) => (patches[b].HalfU * patches[b].HalfV).CompareTo(patches[a].HalfU * patches[a].HalfV));
                l.RemoveRange(MostPerCell, l.Count - MostPerCell);
                l.Sort();
            }
            entries += l.Count;
        }
        var cells = new float[cellsX * cellsZ * 2];
        var index = new float[Math.Max(1, (entries + IndexRow - 1) / IndexRow) * IndexRow];
        int next = 0;
        foreach (var (key, list) in lists)
        {
            cells[key * 2] = next;
            cells[key * 2 + 1] = list.Count;
            foreach (int s in list) index[next++] = s;
        }
        return new GroundField
        {
            Patches = Texture(data, PatchesPerRow * 4, rows, Image.Format.Rgbaf, 16),
            Cells = Texture(cells, cellsX, cellsZ, Image.Format.Rgf, 8),
            Index = Texture(index, IndexRow, index.Length / IndexRow, Image.Format.Rf, 4),
            Grid = new Vector4(Cell, cellsX, cellsZ, n),
            PatchCount = n,
            Entries = entries,
            Crowded = crowded,
        };
    }

    static ImageTexture Texture(float[] values, int w, int h, Image.Format format, int bytesPerTexel)
    {
        var bytes = new byte[w * h * bytesPerTexel];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return ImageTexture.CreateFromImage(Image.CreateFromData(w, h, false, format, bytes));
    }
}
