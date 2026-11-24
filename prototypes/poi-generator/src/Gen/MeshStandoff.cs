using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GalHack.Geometry;
using NV3 = System.Numerics.Vector3;
using NV4 = System.Numerics.Vector4;

namespace PoiGenerator.Gen;

/// <summary>
/// The place mesher's rule against z-fighting: in a finished chunk, two faces of different looks that lie in one
/// plane over each other (a hinge flush on a door's frame, a shelf's boards flush with its sides, flecks on a floor,
/// growth on a wall) are set apart, the smaller part moved <see cref="Coplanar.Standoff"/> out along its face. A part
/// is what shares vertices (a quad, a lump), so nothing tears. Only the drawing moves: collision is built on its own,
/// from the true surfaces.
/// </summary>
public static class MeshStandoff
{
    /// <summary>One surface's triangles as the rule sees them: its parts numbered from 0, in the order the triangles meet them.</summary>
    sealed class Buffer
    {
        public Coplanar.Face[] Faces = Array.Empty<Coplanar.Face>();
        public int[] Group = Array.Empty<int>();
        public bool[] Apart = Array.Empty<bool>();
        public int[] PartOf = Array.Empty<int>(); // each vertex's part, -1 if no triangle holds it
        public readonly List<float> Size = new();
    }

    public static void Apply(IReadOnlyList<ChunkMesh> chunks)
    {
        // All of a build at once: a lump that bulges over a chunk's edge fights faces in the next one.
        var buffers = new List<MeshBuffer>();
        foreach (ChunkMesh c in chunks) buffers.AddRange(c.Surfaces);
        if (buffers.Count == 0) return;
        int perChunk = chunks[0].Surfaces.Length;
        var parts = new Buffer[buffers.Count];
        // Each surface on its own (on several threads): its parts, and its triangles.
        Parallel.For(0, buffers.Count, s => parts[s] = Read(buffers[s], s % perChunk));
        int tris = 0, groups = 0;
        foreach (Buffer b in parts) { tris += b.Faces.Length; groups += b.Size.Count; }
        if (tris == 0) return;
        var faces = new Coplanar.Face[tris];
        var group = new int[tris];
        var apart = new bool[tris];
        var size = new float[groups];
        var firstPart = new int[buffers.Count];
        for (int s = 0, t = 0, g = 0; s < buffers.Count; s++)
        {
            Buffer b = parts[s];
            firstPart[s] = g;
            Array.Copy(b.Faces, 0, faces, t, b.Faces.Length);
            Array.Copy(b.Apart, 0, apart, t, b.Apart.Length);
            for (int i = 0; i < b.Group.Length; i++) group[t + i] = b.Group[i] + g;
            b.Size.CopyTo(size, g);
            t += b.Faces.Length;
            g += b.Size.Count;
        }
        NV3[] offset = Coplanar.Separate(faces, group, size, apart: apart);
        Parallel.For(0, buffers.Count, s =>
        {
            MeshBuffer m = buffers[s];
            int[] partOf = parts[s].PartOf;
            for (int v = 0; v < m.VertexCount; v++)
            {
                if (partOf[v] < 0) continue;
                NV3 o = offset[firstPart[s] + partOf[v]];
                if (o == NV3.Zero) continue;
                m.Positions[v * 3] += o.X;
                m.Positions[v * 3 + 1] += o.Y;
                m.Positions[v * 3 + 2] += o.Z;
            }
        });
    }

    /// <summary>
    /// A surface's parts (vertices joined by the triangles that share them) and its triangles. A grid's cell face (a
    /// cube's, the ground's, a merged run of them) is a quad of its own, and never lies over another: marked apart.
    /// </summary>
    static Buffer Read(MeshBuffer m, int surface)
    {
        var b = new Buffer();
        int verts = m.VertexCount, n = m.Indices.Count / 3;
        if (n == 0) return b;
        var parent = new int[verts];
        for (int v = 0; v < verts; v++) parent[v] = v;
        int Root(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
        for (int t = 0; t < n; t++)
        {
            int r0 = Root(m.Indices[t * 3]);
            int r1 = Root(m.Indices[t * 3 + 1]);
            if (r0 != r1) parent[r1] = r0;
            int r2 = Root(m.Indices[t * 3 + 2]);
            if (r0 != r2) parent[r2] = r0;
        }
        var grid = new bool[verts];
        foreach (int v in m.Grid)
            for (int k = 0; k < 4; k++) grid[v + k] = true;
        var partOfRoot = new int[verts];
        Array.Fill(partOfRoot, -1);
        b.Faces = new Coplanar.Face[n];
        b.Group = new int[n];
        b.Apart = new bool[n];
        for (int t = 0; t < n; t++)
        {
            int i0 = m.Indices[t * 3], i1 = m.Indices[t * 3 + 1], i2 = m.Indices[t * 3 + 2];
            NV3 a = P(m, i0), c1 = P(m, i1), c2 = P(m, i2);
            int root = Root(i0);
            if (partOfRoot[root] < 0) { partOfRoot[root] = b.Size.Count; b.Size.Add(0f); }
            int g = partOfRoot[root];
            b.Size[g] += NV3.Cross(c1 - a, c2 - a).Length() * 0.5f;
            var col = new NV4(m.Colors[i0 * 4], m.Colors[i0 * 4 + 1], m.Colors[i0 * 4 + 2], m.Colors[i0 * 4 + 3]);
            b.Faces[t] = new Coplanar.Face(a, c1, c2, N(m, i0) + N(m, i1) + N(m, i2), Coplanar.Look(surface * 1000 + (int)m.Codes[i0 * 2], col), surface);
            b.Group[t] = g;
            b.Apart[t] = grid[i0];
        }
        b.PartOf = new int[verts];
        for (int v = 0; v < verts; v++) b.PartOf[v] = partOfRoot[Root(v)];
        return b;
    }

    static NV3 P(MeshBuffer m, int i) => new(m.Positions[i * 3], m.Positions[i * 3 + 1], m.Positions[i * 3 + 2]);
    static NV3 N(MeshBuffer m, int i) => new(m.Normals[i * 3], m.Normals[i * 3 + 1], m.Normals[i * 3 + 2]);
}
