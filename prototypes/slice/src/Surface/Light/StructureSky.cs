using System;
using System.Collections.Generic;
using GalHack.Structures;
using GalHack.Structures.Presentation;
using Godot;
using N = System.Numerics;
using SInt3 = GalHack.Structures.Int3;

namespace Slice.Surface;

/// <summary>
/// The same flood as <see cref="SkyField"/>, in a structure's own grid, baked into its vertices: a
/// vessel moves, and carries its dark inside with it, on the ground or in orbit. Light enters from
/// the box round the structure, from the top and the sides, never from under it: a room whose
/// floor is the ground (walls set straight on the dirt) mustn't fill from below.
/// </summary>
public sealed class StructureSky
{
    const int MaxCells = 1 << 23;

    SInt3 _min, _size;
    byte[] _open = Array.Empty<byte>();
    float _cell = 1f;

    int Index(int x, int y, int z) => (y * _size.Z + z) * _size.X + x;

    /// <summary>Whether a block stops light: whole, opaque blocks and shut doors.</summary>
    public static bool Stops(Structure s, in Block b) =>
        s.IsSolid(b) && (s.TypeOf(b).IsDoor || s.Catalogue.OpaqueAllRound(b));

    /// <summary>The middles of a structure's light-stopping blocks, in its own metres.</summary>
    public static N.Vector3[] Stops(Structure s)
    {
        var list = new List<N.Vector3>(s.Count);
        float cell = s.Grid.CellSize;
        foreach (var (p, b) in s.Cells())
            if (Stops(s, b)) list.Add(new N.Vector3(p.X, p.Y, p.Z) * cell);
        return list.ToArray();
    }

    /// <summary>Floods the structure. Returns the chunks whose light changed.</summary>
    public HashSet<SInt3> Flood(Structure s)
    {
        var changed = new HashSet<SInt3>();
        if (s.Count == 0) return changed;
        (SInt3 lo, SInt3 hi) = s.Bounds();
        var min = new SInt3(lo.X - 1, lo.Y, lo.Z - 1);
        var size = new SInt3(hi.X - lo.X + 3, hi.Y - lo.Y + 2, hi.Z - lo.Z + 3);
        if ((long)size.X * size.Y * size.Z > MaxCells) return changed;
        (SInt3 oldMin, SInt3 oldSize, byte[] old) = (_min, _size, _open);
        _min = min;
        _size = size;
        _cell = s.Grid.CellSize;
        var open = new byte[size.X * size.Y * size.Z];
        var solid = new bool[open.Length];
        foreach (var (p, b) in s.Cells())
            if (Stops(s, b)) solid[Index(p.X - min.X, p.Y - min.Y, p.Z - min.Z)] = true;
        var queue = new Queue<int>();
        for (int y = 0; y < size.Y; y++)
        for (int z = 0; z < size.Z; z++)
        for (int x = 0; x < size.X; x++)
        {
            if (y != size.Y - 1 && x != 0 && z != 0 && x != size.X - 1 && z != size.Z - 1) continue;
            int i = Index(x, y, z);
            open[i] = 255;
            queue.Enqueue(i);
        }
        int step = Math.Max(1, (int)(255f * _cell / SkyField.Reach)), layer = size.X * size.Z;
        while (queue.Count > 0)
        {
            int i = queue.Dequeue(), left = open[i] - step;
            if (left <= 0) continue;
            int x = i % size.X, z = i / size.X % size.Z, y = i / layer;
            if (x > 0) Spread(i - 1, left);
            if (x < size.X - 1) Spread(i + 1, left);
            if (z > 0) Spread(i - size.X, left);
            if (z < size.Z - 1) Spread(i + size.X, left);
            if (y > 0) Spread(i - layer, left);
            if (y < size.Y - 1) Spread(i + layer, left);
        }
        void Spread(int n, int level)
        {
            if (solid[n] || open[n] >= level) return;
            open[n] = (byte)level;
            queue.Enqueue(n);
        }
        _open = open;
        for (int y = 0; y < size.Y; y++)
        for (int z = 0; z < size.Z; z++)
        for (int x = 0; x < size.X; x++)
        {
            int wx = x + min.X, wy = y + min.Y, wz = z + min.Z;
            int ox = wx - oldMin.X, oy = wy - oldMin.Y, oz = wz - oldMin.Z;
            bool had = (uint)ox < (uint)oldSize.X && (uint)oy < (uint)oldSize.Y && (uint)oz < (uint)oldSize.Z;
            byte before = had ? old[(oy * oldSize.Z + oz) * oldSize.X + ox] : (byte)255;
            if (before != open[Index(x, y, z)]) changed.Add(Structure.ChunkOf(new SInt3(wx, wy, wz)));
        }
        return changed;
    }

    /// <summary>How dark each vertex is: the air cells around the spot half a cell out from it, solid ones left out.</summary>
    public void Bake(MeshBuffers buffers)
    {
        buffers.Dark.Clear();
        for (int v = 0; v < buffers.VertexCount; v++) buffers.Dark.Add(DarkAt(buffers.Vertices[v] + buffers.Normals[v] * (_cell * 0.5f)));
    }

    /// <summary>How dark a spot in the structure's own metres is, 0 open to 1 cut off.</summary>
    public float DarkAt(Vector3 p)
    {
        if (_open.Length == 0) return 0f;
        Vector3 f = p / _cell;
        int bx = Mathf.FloorToInt(f.X), by = Mathf.FloorToInt(f.Y), bz = Mathf.FloorToInt(f.Z);
        float total = 0f, weight = 0f;
        for (int i = 0; i < 8; i++)
        {
            int cx = bx + (i & 1), cy = by + ((i >> 1) & 1), cz = bz + ((i >> 2) & 1);
            int x = cx - _min.X, y = cy - _min.Y, z = cz - _min.Z;
            float w = (1f - Math.Abs(f.X - cx)) * (1f - Math.Abs(f.Y - cy)) * (1f - Math.Abs(f.Z - cz));
            bool inside = (uint)x < (uint)_size.X && (uint)y < (uint)_size.Y && (uint)z < (uint)_size.Z;
            byte level = inside ? _open[Index(x, y, z)] : (byte)255;
            if (level == 0) continue; // a solid neighbour says nothing about the air here
            total += w * level / 255f;
            weight += w;
        }
        if (weight <= 0f) return 1f;
        float open = total / weight;
        return 1f - open * open;
    }
}
