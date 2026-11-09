using System;
using System.Collections.Generic;
using GalHack.Geometry;
using NV3 = System.Numerics.Vector3;
using NV4 = System.Numerics.Vector4;

namespace PoiGenerator.Gen;

/// <summary>
/// The place mesher's rule against z-fighting: in a finished chunk, two faces of different looks that lie in one
/// plane over each other (a hinge flush on a door's frame, a shelf's boards flush with its sides, flecks on a floor,
/// growth on a wall) are set apart, the smaller part moved <see cref="Coplanar.Standoff"/> out along its face. A part
/// is what shares vertices (a quad, a lump), so nothing tears.
/// </summary>
public static class MeshStandoff
{
    public static void Apply(IReadOnlyList<ChunkMesh> chunks)
    {
        // All of a build at once: a lump that bulges over a chunk's edge fights faces in the next one.
        var buffers = new List<MeshBuffer>();
        foreach (ChunkMesh c in chunks) buffers.AddRange(c.Surfaces);
        int perChunk = chunks.Count > 0 ? chunks[0].Surfaces.Length : 1;
        var faces = new List<Coplanar.Face>();
        var group = new List<int>();
        var size = new List<float>();
        var firstGroup = new int[buffers.Count];
        var parent = new List<int>();
        int Root(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
        // Parts: vertices joined by the triangles that share them, surface by surface.
        for (int s = 0; s < buffers.Count; s++)
        {
            MeshBuffer m = buffers[s];
            firstGroup[s] = parent.Count;
            for (int v = 0; v < m.VertexCount; v++) parent.Add(parent.Count);
            for (int t = 0; t + 2 < m.Indices.Count; t += 3)
            {
                int a = Root(firstGroup[s] + m.Indices[t]);
                foreach (int k in new[] { m.Indices[t + 1], m.Indices[t + 2] })
                {
                    int b = Root(firstGroup[s] + k);
                    if (a != b) parent[b] = a;
                }
            }
        }
        if (parent.Count == 0) return;
        var partOf = new int[parent.Count];
        Array.Fill(partOf, -1);
        for (int s = 0; s < buffers.Count; s++)
        {
            MeshBuffer m = buffers[s];
            for (int t = 0; t + 2 < m.Indices.Count; t += 3)
            {
                int i0 = m.Indices[t], i1 = m.Indices[t + 1], i2 = m.Indices[t + 2];
                NV3 a = P(m, i0), b = P(m, i1), c = P(m, i2);
                int root = Root(firstGroup[s] + i0);
                if (partOf[root] < 0) { partOf[root] = size.Count; size.Add(0f); }
                int g = partOf[root];
                size[g] += NV3.Cross(b - a, c - a).Length() * 0.5f;
                var col = new NV4(m.Colors[i0 * 4], m.Colors[i0 * 4 + 1], m.Colors[i0 * 4 + 2], m.Colors[i0 * 4 + 3]);
                faces.Add(new Coplanar.Face(a, b, c, N(m, i0) + N(m, i1) + N(m, i2), Coplanar.Look(s % perChunk * 1000 + (int)m.Codes[i0 * 2], col), s % perChunk));
                group.Add(g);
            }
        }
        NV3[] offset = Coplanar.Separate(faces, group, size);
        for (int s = 0; s < buffers.Count; s++)
        {
            MeshBuffer m = buffers[s];
            for (int v = 0; v < m.VertexCount; v++)
            {
                int g = partOf[Root(firstGroup[s] + v)];
                if (g < 0 || offset[g] == NV3.Zero) continue;
                m.Positions[v * 3] += offset[g].X;
                m.Positions[v * 3 + 1] += offset[g].Y;
                m.Positions[v * 3 + 2] += offset[g].Z;
            }
        }
    }

    static NV3 P(MeshBuffer m, int i) => new(m.Positions[i * 3], m.Positions[i * 3 + 1], m.Positions[i * 3 + 2]);
    static NV3 N(MeshBuffer m, int i) => new(m.Normals[i * 3], m.Normals[i * 3 + 1], m.Normals[i * 3 + 2]);
}
