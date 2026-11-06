using System;
using Godot;
using Planets.Gen;

namespace Planets.Orbit;

/// <summary>
/// The orbit globe with real relief: a sphere displaced by the generator's height map, so ranges,
/// plateaus and canyon country stand up or cut in, and break the silhouette a little at the limb.
///
/// - <b>Layout.</b> The same as Godot's sphere and the colour map: u = 0 on +Z running towards +X,
///   v = 0 the north pole, so the textures, <see cref="OrbitView.FacingPoint"/> and landing all line
///   up as before. The u = 0 and u = 1 columns are the same points with the same heights and
///   normals (the map wraps), so the seam has no crack.
/// - <b>Heights.</b> Oceans stay flat at sea level. Land rises by h / (h + 450 m) of a small
///   fraction of the radius: these planets are only a few kilometres round, so true relief would
///   be a spiky ball; this keeps plains low, lifts tablelands a little and ranges most, and stays
///   under the cloud layer.
/// - <b>Poles.</b> The torus map pinches to a point at each pole, so heights relax to the polar
///   average over the last few degrees: no spike, no crack.
/// - <b>Lighting.</b> Normals come from the same surface with its relief steepened, so the
///   terminator shows ranges and escarpments in light and shade without a taller globe. Heights
///   are smoothed over a few map cells first: the globe shows nothing that fine, and a cliff one
///   cell wide, steepened, would light as a stair of cells.
/// Pure arrays, built off the main thread with the colour map.
/// </summary>
public static class GlobeRelief
{
    public const int Segments = 720;          // vertices round the equator (the rings are half that)
    public const float MaxRise = 0.0125f;     // of the radius, approached by the highest peaks
    public const float HalfRise = 260f;       // metres at which land has half of it
    public const float ShadeSteepen = 3f;     // how much steeper the lit surface is than the drawn one
    const float PoleBlend = 0.11f;            // share of v at each end that relaxes to the pole
    const int SmoothPasses = 3;               // [1 2 1] passes over the map's cells before the heights are read

    public sealed class Data
    {
        public Vector3[] Vertices, Normals;
        public Vector2[] Uvs;
        public int[] Indices;
        public int Columns, Rows;
        /// <summary>Displacement (fraction of the radius) per vertex, for tests.</summary>
        public float[] Rise;
    }

    /// <summary>How far land of this height rises, as a fraction of the radius.</summary>
    public static float Rise(float height) => height <= 0f ? 0f : MaxRise * height / (height + HalfRise);

    public static Data Build(WorldMap map, int segments = Segments)
    {
        int cols = segments, rows = segments / 2;
        int stride = cols + 1;
        var rise = new float[stride * (rows + 1)];

        var cell = new float[map.Count];
        for (int k = 0; k < cell.Length; k++) cell[k] = Rise(map.Elevation[k]);
        cell = Look.ColorMap.Soften(map, cell, SmoothPasses);

        // Heights, relaxed to each pole's average near it. Oceans stay flat, whatever the coast next to them.
        float Height(double x, double y) => map.Sample(map.Elevation, x, y) <= 0f ? 0f : map.Sample(cell, x, y);
        float north = 0f, south = 0f;
        for (int i = 0; i < cols; i++)
        {
            north += Height((i + 0.5) * map.WidthMeters / cols, 0.5 * map.CellSize);
            south += Height((i + 0.5) * map.WidthMeters / cols, map.HeightMeters - 0.5 * map.CellSize);
        }
        north /= cols;
        south /= cols;
        System.Threading.Tasks.Parallel.For(0, rows + 1, j =>
        {
            float v = j / (float)rows;
            float keep = Mathx.Smooth(0f, PoleBlend, v) * Mathx.Smooth(1f, 1f - PoleBlend, v);
            float pole = v < 0.5f ? north : south;
            for (int i = 0; i < cols; i++)
            {
                double x = i * map.WidthMeters / cols, y = Math.Clamp(v * map.HeightMeters, 0.0, map.HeightMeters - 1e-3);
                rise[j * stride + i] = Mathx.Lerp(pole, Height(x, y), keep);
            }
            rise[j * stride + cols] = rise[j * stride]; // the seam column: the same points
        });

        var verts = new Vector3[rise.Length];
        var shade = new Vector3[rise.Length];
        var uvs = new Vector2[rise.Length];
        for (int j = 0; j <= rows; j++)
        {
            float v = j / (float)rows, theta = v * MathF.PI;
            for (int i = 0; i <= cols; i++)
            {
                float u = i / (float)cols, phi = u * MathF.Tau;
                // At the poles every column is the same point.
                Vector3 dir = j == 0 ? Vector3.Up : j == rows ? Vector3.Down
                    : new Vector3(MathF.Sin(theta) * MathF.Sin(phi), MathF.Cos(theta), MathF.Sin(theta) * MathF.Cos(phi));
                int k = j * stride + i;
                verts[k] = dir * (1f + rise[k]);
                shade[k] = dir * (1f + rise[k] * ShadeSteepen);
                uvs[k] = new Vector2(u, v);
            }
        }

        // Normals from the steepened surface: central differences, wrapping round in u.
        var normals = new Vector3[rise.Length];
        for (int j = 0; j <= rows; j++)
        for (int i = 0; i <= cols; i++)
        {
            int k = j * stride + i;
            if (j == 0 || j == rows)
            {
                normals[k] = verts[k].Normalized();
                continue;
            }
            int il = (i - 1 + cols) % cols, ir = (i + 1) % cols;
            Vector3 east = shade[j * stride + ir] - shade[j * stride + il];
            Vector3 down = shade[(j + 1) * stride + i % cols] - shade[(j - 1) * stride + i % cols];
            Vector3 n = down.Cross(east);
            if (n.Dot(verts[k]) < 0f) n = -n;
            normals[k] = n.Normalized();
        }
        for (int j = 0; j <= rows; j++) normals[j * stride + cols] = normals[j * stride];

        // Two triangles per quad, wound so their faces look outwards.
        var indices = new int[cols * rows * 6];
        int t = 0;
        for (int j = 0; j < rows; j++)
        for (int i = 0; i < cols; i++)
        {
            int a = j * stride + i, b = a + 1, c = a + stride, d = c + 1;
            indices[t++] = a; indices[t++] = b; indices[t++] = c;
            indices[t++] = b; indices[t++] = d; indices[t++] = c;
        }
        return new Data { Vertices = verts, Normals = normals, Uvs = uvs, Indices = indices, Columns = cols, Rows = rows, Rise = rise };
    }

    /// <summary>The mesh, made on the main thread from built arrays.</summary>
    public static ArrayMesh Mesh(Data data)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Godot.Mesh.ArrayType.Max);
        arrays[(int)Godot.Mesh.ArrayType.Vertex] = data.Vertices;
        arrays[(int)Godot.Mesh.ArrayType.Normal] = data.Normals;
        arrays[(int)Godot.Mesh.ArrayType.TexUV] = data.Uvs;
        arrays[(int)Godot.Mesh.ArrayType.Index] = data.Indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    /// <summary>FNV-1a over the vertices and normals, for determinism checks.</summary>
    public static ulong Hash(Data data)
    {
        ulong h = 14695981039346656037UL;
        void Add(float f) { h = (h ^ (uint)BitConverter.SingleToInt32Bits(f)) * 1099511628211UL; }
        for (int k = 0; k < data.Vertices.Length; k++)
        {
            Add(data.Vertices[k].X); Add(data.Vertices[k].Y); Add(data.Vertices[k].Z);
            Add(data.Normals[k].X); Add(data.Normals[k].Y); Add(data.Normals[k].Z);
        }
        return h;
    }
}
