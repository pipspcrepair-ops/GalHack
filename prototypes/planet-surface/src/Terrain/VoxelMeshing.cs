using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using Godot;
using Planets.Gen;
using TerrainDigging.Terrain;
using NVector3 = System.Numerics.Vector3;

namespace PlanetSurface.Terrain;

/// <summary>One voxel chunk's mesh, built off the main thread from an immutable snapshot.</summary>
public sealed class VoxelJob
{
    public Int3 Key;            // the chunk, in the frame it was captured in
    public int Version;         // bumped by every edit: a stale result is still shown, then replaced
    public ChunkNeighborhood Hood;
    public bool NeedFaces;      // near the player: build the collision triangles too

    public Vector3[] Positions, Normals, Faces;
    public Color[] Colors;
    public Vector2[] Flags;
    public int[] Indices;
    public ulong Hash;
    public double Milliseconds;
    public Exception Error;

    public bool IsEmpty => Positions == null;
}

/// <summary>
/// Builds voxel chunk meshes on worker threads: terrain-digging's Surface Nets (crack-free seams,
/// soft chamfered holes), coloured by the ground palette at canonical map coordinates, plus the
/// triangle soup for collision. Pure function of the snapshot, so a chunk always rebuilds the same.
/// </summary>
public static class VoxelMeshing
{
    static readonly ThreadLocal<SurfaceNetsMesher> Meshers = new(() => new SurfaceNetsMesher());

    public static void Build(VoxelJob job, GroundPalette palette, double width, double height)
    {
        try
        {
            long start = Stopwatch.GetTimestamp();
            ChunkMesh mesh = Meshers.Value.Build(job.Hood);
            job.Hash = mesh.ComputeHash();
            if (!mesh.IsEmpty)
            {
                // The mesher's arrays are handed over as they are: System.Numerics and Godot vectors
                // are both three packed floats, so no copy is needed (streaming allocates enough).
                job.Positions = Unsafe.As<Vector3[]>(mesh.Positions);
                job.Normals = Unsafe.As<Vector3[]>(mesh.Normals);
                job.Indices = mesh.Indices;
                job.Colors = new Color[job.Positions.Length];
                job.Flags = new Vector2[job.Positions.Length];
                List<Deposit> deposits = null;
                for (int i = 0; i < job.Positions.Length; i++)
                {
                    Vector3 p = job.Positions[i];
                    double x = Wrap(p.X, width), z = Wrap(p.Z, height);
                    CellMaterial m = mesh.Materials[i];
                    DepositKind ore = DepositKind.Iron;
                    if (m == CellMaterial.Ore) ore = NearestDeposit(palette.Map, x, z, ref deposits);
                    job.Colors[i] = palette.Vertex(m, x, z, p.Y, out job.Flags[i], ore);
                }
                if (job.NeedFaces) job.Faces = Faces(job.Positions, job.Indices);
            }
            job.Milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
        catch (Exception e)
        {
            job.Error = e;
        }
    }

    /// <summary>The triangle soup a collision shape takes.</summary>
    public static Vector3[] Faces(Vector3[] positions, int[] indices)
    {
        var faces = new Vector3[indices.Length];
        for (int t = 0; t < indices.Length; t++) faces[t] = positions[indices[t]];
        return faces;
    }

    /// <summary>The deposit an ore vertex belongs to: the nearest one on the map.</summary>
    static DepositKind NearestDeposit(WorldMap map, double x, double z, ref List<Deposit> cache)
    {
        if (cache == null)
        {
            cache = new List<Deposit>();
            foreach (Deposit d in map.Deposits.InRect(x - 24, z - 24, x + 24, z + 24)) cache.Add(d);
        }
        DepositKind kind = DepositKind.Iron;
        double best = double.MaxValue;
        foreach (Deposit d in cache)
        {
            double dx = Mathx.WrapDelta(d.X - x, map.WidthMeters), dz = Mathx.WrapDelta(d.Y - z, map.HeightMeters);
            double d2 = dx * dx + dz * dz;
            if (d2 < best) { best = d2; kind = d.Kind; }
        }
        return kind;
    }

    static double Wrap(double v, double span)
    {
        v %= span;
        return v < 0 ? v + span : v;
    }
}
