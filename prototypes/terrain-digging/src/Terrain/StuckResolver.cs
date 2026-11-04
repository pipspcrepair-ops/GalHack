using System;
using System.Collections.Generic;
using System.Numerics;

namespace TerrainDigging.Terrain;

/// <summary>An upright body, approximated as a capsule standing on its feet.</summary>
public readonly record struct BodyShape(float Height, float Radius);

/// <summary>Where a stuck body should go, and how good a spot it is.</summary>
public readonly record struct OpenSpace(Vector3 Feet, bool Standable, bool Fallback);

/// <summary>
/// Finds bodies inside terrain and the nearest open space to move them to (rule 7).
///
/// Terrain never grows, so bodies only end up inside it through teleports, loads, network
/// corrections or a physics tunnel. "Inside" is judged at the body's core (its axis, a radius
/// in from each end) using the same density field the mesh is drawn from, so a body pressed
/// against a wall or crouched under an overhang is never mistaken for a buried one.
/// </summary>
public static class StuckResolver
{
    /// <summary>Density at the core above this means the body is genuinely inside the ground.</summary>
    public const float InsideThreshold = 0.15f;

    /// <summary>Open space must clear the body by this much sideways, so it isn't wedged into a shaft.</summary>
    const float SideClearance = 0.12f;

    const float OpenBelow = -0.05f;
    const int SearchRadius = 14;

    // Offsets ordered by cost: nearest first, and going down costs more than going up,
    // so a buried player is lifted to the surface rather than dropped into a cave below.
    static readonly Int3[] Offsets = BuildOffsets(SearchRadius);

    static Int3[] BuildOffsets(int r)
    {
        var list = new List<(int cost, Int3 offset)>();
        for (int y = -r; y <= r; y++)
            for (int z = -r; z <= r; z++)
                for (int x = -r; x <= r; x++)
                {
                    int cost = x * x + z * z + (y < 0 ? 3 * y * y : y * y);
                    if (cost <= r * r) list.Add((cost, new Int3(x, y, z)));
                }
        list.Sort((a, b) => a.cost != b.cost ? a.cost.CompareTo(b.cost) : a.offset.CompareTo(b.offset));
        var offsets = new Int3[list.Count];
        for (int i = 0; i < offsets.Length; i++) offsets[i] = list[i].offset;
        return offsets;
    }

    public static bool IsInsideTerrain(ChunkStore store, Vector3 feet, BodyShape shape)
    {
        float bottom = feet.Y + shape.Radius;
        float top = feet.Y + MathF.Max(shape.Height - shape.Radius, shape.Radius);
        int steps = Math.Max(1, (int)MathF.Ceiling((top - bottom) / 0.4f));
        for (int i = 0; i <= steps; i++)
        {
            float y = bottom + (top - bottom) * i / steps;
            if (store.SampleDensity(new Vector3(feet.X, y, feet.Z)) > InsideThreshold) return true;
        }
        return false;
    }

    /// <summary>Whether the body fits at these feet with a little room on every side.</summary>
    public static bool Fits(ChunkStore store, Vector3 feet, BodyShape shape)
    {
        if (store.SampleDensity(feet + new Vector3(0f, 0.12f, 0f)) > OpenBelow) return false;

        // Sideways checks start at the lower sphere's centre, so ordinary slopes still count as room.
        float ring = shape.Radius + SideClearance;
        float diagonal = ring * 0.7071f;
        float bottom = feet.Y + shape.Radius, top = feet.Y + shape.Height - 0.08f;
        int steps = Math.Max(1, (int)MathF.Ceiling((top - bottom) / 0.4f));
        for (int i = 0; i <= steps; i++)
        {
            float y = bottom + (top - bottom) * i / steps;
            var axis = new Vector3(feet.X, y, feet.Z);
            if (store.SampleDensity(axis) > OpenBelow) return false;
            if (store.SampleDensity(axis + new Vector3(ring, 0, 0)) > OpenBelow) return false;
            if (store.SampleDensity(axis + new Vector3(-ring, 0, 0)) > OpenBelow) return false;
            if (store.SampleDensity(axis + new Vector3(0, 0, ring)) > OpenBelow) return false;
            if (store.SampleDensity(axis + new Vector3(0, 0, -ring)) > OpenBelow) return false;
            if (store.SampleDensity(axis + new Vector3(diagonal, 0, diagonal)) > OpenBelow) return false;
            if (store.SampleDensity(axis + new Vector3(-diagonal, 0, diagonal)) > OpenBelow) return false;
            if (store.SampleDensity(axis + new Vector3(diagonal, 0, -diagonal)) > OpenBelow) return false;
            if (store.SampleDensity(axis + new Vector3(-diagonal, 0, -diagonal)) > OpenBelow) return false;
        }
        return true;
    }

    /// <summary>
    /// The nearest spot where the body fits: first one with ground to stand on, then any open
    /// one. If nothing fits within the search radius, it goes on top of its column.
    /// </summary>
    public static OpenSpace FindOpenSpace(ChunkStore store, Vector3 feet, BodyShape shape)
    {
        Int3 start = Int3.Floor(feet + new Vector3(0f, shape.Height * 0.5f, 0f));
        for (int pass = 0; pass < 2; pass++)
        {
            bool needGround = pass == 0;
            foreach (Int3 offset in Offsets)
            {
                Int3 cell = start + offset;
                if (!ChunkStore.InWorld(cell) || store.IsSolid(cell)) continue;
                bool ground = store.IsSolid(cell - Int3.Up);
                if (needGround && !ground) continue;

                Vector3 candidate = ground ? StandingFeet(store, cell) : new Vector3(cell.X + 0.5f, cell.Y + 0.1f, cell.Z + 0.5f);
                if (Fits(store, candidate, shape)) return new OpenSpace(candidate, ground, false);
            }
        }

        int top = store.TopSolid((int)MathF.Floor(feet.X), (int)MathF.Floor(feet.Z));
        return new OpenSpace(new Vector3(feet.X, top + 1.1f, feet.Z), true, true);
    }

    /// <summary>Feet just above the surface in a cell whose floor is solid.</summary>
    static Vector3 StandingFeet(ChunkStore store, Int3 cell)
    {
        // Along the column through two cell centres the density is linear: find its zero.
        float below = store.DensityAt(cell - Int3.Up), here = store.DensityAt(cell);
        float t = below / (below - here);
        return new Vector3(cell.X + 0.5f, cell.Y - 0.5f + t + 0.05f, cell.Z + 0.5f);
    }
}
