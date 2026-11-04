using System;
using System.Collections.Generic;
using System.Numerics;

namespace GalHack.Physics;

/// <summary>A chunk of a structure's grid (16³ cells), by chunk coordinates.</summary>
public readonly record struct ChunkKey(int X, int Y, int Z);

/// <summary>A collision box in a structure's own frame, metres.</summary>
public readonly record struct BoxCollider(Vector3 Center, Vector3 Size);

/// <summary>A convex collider (a sloped block) in a structure's own frame, metres.</summary>
public sealed record HullCollider(Vector3[] Points);

/// <summary>One chunk's colliders, with a fingerprint so an unchanged chunk is never rebuilt.</summary>
public sealed class ChunkColliders
{
    public ChunkKey Chunk;
    public readonly List<BoxCollider> Boxes = new();
    public readonly List<HullCollider> Hulls = new();
    public ulong Hash;

    public bool IsEmpty => Boxes.Count == 0 && Hulls.Count == 0;
}

/// <summary>Total mass and centre of mass, in a structure's own frame.</summary>
public readonly record struct MassProperties(float Mass, Vector3 CenterOfMass)
{
    public static MassProperties Of(IEnumerable<(Vector3 position, float mass)> points)
    {
        double m = 0, x = 0, y = 0, z = 0;
        foreach (var (p, mass) in points)
        {
            m += mass;
            x += p.X * (double)mass;
            y += p.Y * (double)mass;
            z += p.Z * (double)mass;
        }
        return m > 0 ? new MassProperties((float)m, new Vector3((float)(x / m), (float)(y / m), (float)(z / m))) : default;
    }
}

/// <summary>
/// Structures as bodies: the collider strategy.
///
/// <b>One body per structure, one compound shape, built per chunk.</b> A structure's solid
/// cells are merged chunk by chunk (16³) into as few boxes as a greedy sweep finds, plus one
/// convex hull per sloped block. Each chunk's colliders carry a hash. When the structure is
/// edited, only its dirty chunks are rebuilt, and only chunks whose hash changed swap their
/// shapes; every other chunk's shapes stay exactly as they were (the engine keeps their cooked
/// shapes). The body's compound is re-linked from those shapes once, not re-cooked.
///
/// One body per structure keeps a ship's motion one sweep and one transform, keeps hulls from
/// ever separating, and lets a wreck be one free rigid body. Per-chunk rebuilds keep a
/// one-block edit on a big ship from rebuilding thousands of boxes (building's prototype
/// rebuilds the whole body's collider on every edit).
/// </summary>
public static class StructureCollision
{
    public const int ChunkSize = 16;

    /// <summary>
    /// Greedy box merge of one chunk. <paramref name="solid"/> is indexed x + 16·(y + 16·z).
    /// Returns boxes as [min, max) in cell coordinates within the chunk. Deterministic: same
    /// cells, same boxes, in the same order.
    /// </summary>
    public static List<(int x0, int y0, int z0, int x1, int y1, int z1)> MergeBoxes(bool[] solid)
    {
        const int S = ChunkSize;
        var used = new bool[S * S * S];
        var boxes = new List<(int, int, int, int, int, int)>();
        static int I(int x, int y, int z) => x + S * (y + S * z);

        for (int z = 0; z < S; z++)
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    if (!solid[I(x, y, z)] || used[I(x, y, z)]) continue;

                    int x1 = x + 1;
                    while (x1 < S && solid[I(x1, y, z)] && !used[I(x1, y, z)]) x1++;

                    int y1 = y + 1;
                    while (y1 < S && RowFree(x, x1, y1, z)) y1++;

                    int z1 = z + 1;
                    while (z1 < S && SlabFree(x, x1, y, y1, z1)) z1++;

                    for (int zz = z; zz < z1; zz++)
                        for (int yy = y; yy < y1; yy++)
                            for (int xx = x; xx < x1; xx++)
                                used[I(xx, yy, zz)] = true;
                    boxes.Add((x, y, z, x1, y1, z1));
                }
        return boxes;

        bool RowFree(int xa, int xb, int yy, int zz)
        {
            for (int xx = xa; xx < xb; xx++)
                if (!solid[I(xx, yy, zz)] || used[I(xx, yy, zz)]) return false;
            return true;
        }

        bool SlabFree(int xa, int xb, int ya, int yb, int zz)
        {
            for (int yy = ya; yy < yb; yy++)
                if (!RowFree(xa, xb, yy, zz)) return false;
            return true;
        }
    }

    /// <summary>Box colliders in metres for a chunk's merged boxes. Cell (0,0,0) is centred on the structure's origin.</summary>
    public static void AddBoxes(ChunkColliders into, ChunkKey chunk, bool[] solid, float cellSize)
    {
        foreach (var (x0, y0, z0, x1, y1, z1) in MergeBoxes(solid))
        {
            var min = new Vector3(chunk.X * ChunkSize + x0 - 0.5f, chunk.Y * ChunkSize + y0 - 0.5f, chunk.Z * ChunkSize + z0 - 0.5f) * cellSize;
            var max = new Vector3(chunk.X * ChunkSize + x1 - 0.5f, chunk.Y * ChunkSize + y1 - 0.5f, chunk.Z * ChunkSize + z1 - 0.5f) * cellSize;
            into.Boxes.Add(new BoxCollider((min + max) * 0.5f, max - min));
        }
    }

    /// <summary>FNV-1a over a chunk's colliders: equal colliders give equal hashes.</summary>
    public static ulong Hash(ChunkColliders c)
    {
        ulong h = 14695981039346656037ul;
        void Add(float f)
        {
            uint bits = BitConverter.SingleToUInt32Bits(f);
            for (int i = 0; i < 4; i++)
            {
                h ^= (bits >> (8 * i)) & 0xff;
                h *= 1099511628211ul;
            }
        }
        foreach (BoxCollider b in c.Boxes)
        {
            Add(b.Center.X); Add(b.Center.Y); Add(b.Center.Z);
            Add(b.Size.X); Add(b.Size.Y); Add(b.Size.Z);
        }
        foreach (HullCollider hull in c.Hulls)
            foreach (Vector3 p in hull.Points) { Add(p.X); Add(p.Y); Add(p.Z); }
        return h | 1ul;
    }
}
