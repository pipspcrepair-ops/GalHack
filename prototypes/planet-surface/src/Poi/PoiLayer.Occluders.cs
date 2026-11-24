using System;
using System.Collections.Generic;
using Godot;
using PoiGenerator.Gen;

namespace PlanetSurface.Poi;

/// <summary>
/// Places hide what is behind them: each 32 m cube of a place's merged blocks brings occluders made of its
/// own opaque faces, so the renderer leaves out whatever they cover (the next street's towers, the rooms and
/// furniture and people inside, the far land behind a wall). Only faces that are certainly solid: flat,
/// big enough to be worth testing, and with a whole opaque block behind every cell of them (no glass, no
/// door, no shaped block), so nothing seen through a window or a doorway is ever left out. They stand while
/// the merged blocks do (the place's own full detail is the same blocks), and a cube whose blocks change
/// loses its occluders at once (<see cref="Unocclude"/>).
/// </summary>
public partial class PoiLayer
{
    /// <summary>Occluders from places' merged blocks (measurements switch them off to compare).</summary>
    public static bool Occlude = true;

    /// <summary>The least face worth testing against, m² (a quad of a cube is 1).</summary>
    const float OccluderArea = 6f;

    /// <summary>A merged cube's occluder: its opaque faces of <see cref="OccluderArea"/> or more with solid blocks behind every cell, or null where it has none.</summary>
    public static (Vector3[] Vertices, int[] Indices)? OccluderOf(ChunkMesh chunk, BlockGrid g)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();
        bool Solid(Int3 c)
        {
            Block b = g.Get(c);
            return BlockInfo.IsFullCube(b.Type) && !BlockInfo.IsTransparent(b.Material);
        }
        for (int s = 0; s <= (int)Surface.Metal; s++)
        {
            MeshBuffer m = chunk.Surfaces[s];
            for (int t = 0; t + 2 < m.Indices.Count; t += 3)
            {
                Vector3 a = At(m, m.Indices[t]), b = At(m, m.Indices[t + 1]), c = At(m, m.Indices[t + 2]);
                float area = (b - a).Cross(c - a).Length() * 0.5f;
                if (area < OccluderArea * 0.5f) continue; // a triangle is half its quad
                // The face's own normal (outward; the winding is the engine's, clockwise from outside).
                Vector3 n = new(m.Normals[m.Indices[t] * 3], m.Normals[m.Indices[t] * 3 + 1], m.Normals[m.Indices[t] * 3 + 2]);
                int axis = MathF.Abs(n.X) > 0.999f ? 0 : MathF.Abs(n.Y) > 0.999f ? 1 : MathF.Abs(n.Z) > 0.999f ? 2 : -1;
                if (axis < 0) continue;
                // Every cell the triangle covers has a solid block behind it (the face's inward side).
                Vector3 lo = a.Min(b).Min(c), hi = a.Max(b).Max(c);
                Vector3 inward = -n * 0.5f;
                bool ok = true;
                for (float u = lo[(axis + 1) % 3] + 0.5f; ok && u < hi[(axis + 1) % 3]; u += 1f)
                    for (float v = lo[(axis + 2) % 3] + 0.5f; ok && v < hi[(axis + 2) % 3]; v += 1f)
                    {
                        var p = new Vector3();
                        p[axis] = a[axis];
                        p[(axis + 1) % 3] = u;
                        p[(axis + 2) % 3] = v;
                        if (!Inside(p, a, b, c, axis)) continue;
                        Vector3 q = p + inward;
                        ok = Solid(new Int3(Mathf.FloorToInt(q.X), Mathf.FloorToInt(q.Y), Mathf.FloorToInt(q.Z)));
                    }
                if (!ok) continue;
                tris.Add(verts.Count); verts.Add(a);
                tris.Add(verts.Count); verts.Add(b);
                tris.Add(verts.Count); verts.Add(c);
            }
        }
        return tris.Count == 0 ? null : (verts.ToArray(), tris.ToArray());
    }

    static Vector3 At(MeshBuffer m, int i) => new(m.Positions[i * 3], m.Positions[i * 3 + 1], m.Positions[i * 3 + 2]);

    /// <summary>Whether a point of the triangle's plane lies within it (seen down its axis).</summary>
    static bool Inside(Vector3 p, Vector3 a, Vector3 b, Vector3 c, int axis)
    {
        int i = (axis + 1) % 3, j = (axis + 2) % 3;
        float Side(Vector3 u, Vector3 v) => (v[i] - u[i]) * (p[j] - u[j]) - (v[j] - u[j]) * (p[i] - u[i]);
        float d1 = Side(a, b), d2 = Side(b, c), d3 = Side(c, a);
        return !((d1 < 0 || d2 < 0 || d3 < 0) && (d1 > 0 || d2 > 0 || d3 > 0));
    }

    /// <summary>Puts a merged cube's occluder in beside its meshes (under the place's merged blocks, so it stands while they do, shown or hidden by the near detail).</summary>
    void AddOccluder(Place p, Int3 cube, (Vector3[] Vertices, int[] Indices)? arrays)
    {
        if (arrays is not { } a || p.Unoccluded.Contains(cube)) return;
        var shape = new ArrayOccluder3D();
        shape.SetArrays(a.Vertices, a.Indices);
        var o = new OccluderInstance3D { Name = $"Occluder {cube}", Occluder = shape };
        p.Mid.AddChild(o);
        p.Occluders[cube] = o;
    }

    /// <summary>A cube of a place whose blocks changed: its occluders go (a wall broken through is seen through).</summary>
    public void Unocclude(PoiSite site, Int3 cube)
    {
        Place p = _places.Find(x => x.Site == site);
        if (p == null) return;
        p.Unoccluded.Add(cube);
        if (p.Occluders.Remove(cube, out OccluderInstance3D o)) o.QueueFree();
    }
}
