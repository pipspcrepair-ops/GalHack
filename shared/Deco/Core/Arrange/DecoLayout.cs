using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GalHack.Items;

namespace GalHack.Deco;

/// <summary>
/// Floor geometry for dressing: a placement's footprint, what blocks a walkway, and a raster of
/// the room for path checks (an agent of a given width must get from every doorway to every other).
/// </summary>
public static class DecoLayout
{
    /// <summary>The piece's footprint in its own frame (x and z bounds) for a state and fall.</summary>
    public static (Vector3 Min, Vector3 Max) LocalBounds(DecoForge forge, DecoPiece p, DecoState state, DecoFall fall, bool doors)
    {
        bool declared = fall == DecoFall.None && !doors && state is not (DecoState.Open or DecoState.Buried);
        if (declared) return (new Vector3(-p.Width / 2, 0, -p.Depth / 2), new Vector3(p.Width / 2, p.Height, p.Depth / 2));
        DecoForge.Shape s = forge.ShapeOf(p.Id, state, fall, doors);
        return (s.Min, s.Max);
    }

    public static DecoRect Footprint(DecoForge forge, DecoPlacement pl)
    {
        DecoPiece p = forge.Catalogue.Piece(pl.Piece);
        var (min, max) = LocalBounds(forge, p, pl.State, pl.Fall, pl.Doors);
        return RectOf(min, max, pl.X, pl.Z, pl.Yaw);
    }

    public static DecoRect RectOf(Vector3 min, Vector3 max, float x, float z, float yaw)
    {
        Vector2 c = new((min.X + max.X) / 2, (min.Z + max.Z) / 2);
        Vector2 h = new((max.X - min.X) / 2, (max.Z - min.Z) / 2);
        return new DecoRect(new Vector2(x, z) + DecoGeo.Yaw(c, yaw), DecoGeo.Yaw(new Vector2(h.X, 0), yaw), DecoGeo.Yaw(new Vector2(0, h.Y), yaw));
    }

    /// <summary>Its height range above the room floor.</summary>
    public static (float Lo, float Hi) Heights(DecoForge forge, DecoPlacement pl)
    {
        DecoPiece p = forge.Catalogue.Piece(pl.Piece);
        var (min, max) = LocalBounds(forge, p, pl.State, pl.Fall, pl.Doors);
        return (pl.Y + min.Y, pl.Y + max.Y);
    }

    /// <summary>
    /// True if it stops someone walking: anything on the floor that isn't flat litter, and wall
    /// pieces that hang lower than head height. Things on tables and ceiling lamps don't.
    /// </summary>
    public static bool Blocks(DecoForge forge, DecoPlacement pl)
    {
        if (pl.Support >= 0) return false;
        DecoPiece p = forge.Catalogue.Piece(pl.Piece);
        if (p.WalkOver && pl.Fall == DecoFall.None) return false;
        if (p.WalkOver && p.Height <= 0.3f) return false;
        if (p.Mount == DecoMount.Ceiling && pl.Fall == DecoFall.None) return false;
        return Heights(forge, pl).Lo < 2.0f;
    }

    /// <summary>Two placements that may not share floor: both block (or both stand on the floor), their heights overlap, and they aren't parts of one machine.</summary>
    public static bool Clash(DecoForge forge, DecoPlacement a, DecoPlacement b)
    {
        if (a.Assembly >= 0 && a.Assembly == b.Assembly) return false;
        if (a.Support >= 0 || b.Support >= 0) return false;
        DecoPiece pa = forge.Catalogue.Piece(a.Piece), pb = forge.Catalogue.Piece(b.Piece);
        if (pa.WalkOver != pb.WalkOver && (pa.WalkOver || pb.WalkOver)) return false;
        var (alo, ahi) = Heights(forge, a);
        var (blo, bhi) = Heights(forge, b);
        if (alo >= bhi - 0.01f || blo >= ahi - 0.01f) return false;
        return Footprint(forge, a).Shrink(0.01f).Overlaps(Footprint(forge, b).Shrink(0.01f));
    }

    /// <summary>Inside the room, within a millimetre: not through a wall.</summary>
    public static bool Inside(DecoRoom room, DecoRect r, float eps = 0.001f) =>
        r.Corners().All(c => c.X >= -eps && c.Y >= -eps && c.X <= room.Width + eps && c.Y <= room.Depth + eps);
}

/// <summary>
/// The room floor as square cells, each counting the blocking footprints on it, with a
/// breadth-first search for an agent of k by k cells.
/// </summary>
public sealed class DecoRaster
{
    public readonly float Cell;
    public readonly int Nx, Nz;
    readonly int[] _count;

    public DecoRaster(float width, float depth, float cell)
    {
        Cell = cell;
        Nx = Math.Max(1, (int)MathF.Floor(width / cell + 1e-4f));
        Nz = Math.Max(1, (int)MathF.Floor(depth / cell + 1e-4f));
        _count = new int[Nx * Nz];
    }

    public bool Blocked(int i, int j) => _count[j * Nx + i] > 0;

    /// <summary>Adds (or with -1 removes) a footprint: every cell it covers, by a separating-axis test against the cell.</summary>
    public void Add(DecoRect r, int delta)
    {
        DecoRect s = r.Shrink(0.005f);
        var (min, max) = s.Bounds();
        int i0 = Math.Max(0, (int)MathF.Floor(min.X / Cell)), i1 = Math.Min(Nx - 1, (int)MathF.Floor(max.X / Cell));
        int j0 = Math.Max(0, (int)MathF.Floor(min.Y / Cell)), j1 = Math.Min(Nz - 1, (int)MathF.Floor(max.Y / Cell));
        var half = new Vector2(Cell / 2, 0);
        var halfz = new Vector2(0, Cell / 2);
        for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
            {
                var cell = new DecoRect(new Vector2((i + 0.5f) * Cell, (j + 0.5f) * Cell), half, halfz);
                if (s.Overlaps(cell)) _count[j * Nx + i] += delta;
            }
    }

    /// <summary>Where a k-by-k agent fits (its low corner cell), from a prefix sum of blocked cells.</summary>
    public bool[] Fits(int k)
    {
        var sum = new int[(Nx + 1) * (Nz + 1)];
        for (int j = 0; j < Nz; j++)
            for (int i = 0; i < Nx; i++)
                sum[(j + 1) * (Nx + 1) + i + 1] = (Blocked(i, j) ? 1 : 0) + sum[j * (Nx + 1) + i + 1] + sum[(j + 1) * (Nx + 1) + i] - sum[j * (Nx + 1) + i];
        var fit = new bool[Nx * Nz];
        for (int j = 0; j + k <= Nz; j++)
            for (int i = 0; i + k <= Nx; i++)
            {
                int s = sum[(j + k) * (Nx + 1) + i + k] - sum[j * (Nx + 1) + i + k] - sum[(j + k) * (Nx + 1) + i] + sum[j * (Nx + 1) + i];
                fit[j * Nx + i] = s == 0;
            }
        return fit;
    }

    /// <summary>The agent positions (low corner cells) standing in a doorway: against its wall, wholly within the opening.</summary>
    public List<int> DoorStarts(DecoRoom room, DecoDoor d, int k)
    {
        var list = new List<int>();
        float a = d.Offset - d.Width / 2, b = d.Offset + d.Width / 2;
        int lo = (int)MathF.Ceiling(a / Cell - 1e-4f), hi = (int)MathF.Floor(b / Cell + 1e-4f) - k;
        for (int t = lo; t <= hi; t++)
        {
            switch (d.Wall)
            {
                case DecoWall.North: if (t >= 0 && t + k <= Nx) list.Add(t); break;
                case DecoWall.South: if (t >= 0 && t + k <= Nx && Nz - k >= 0) list.Add((Nz - k) * Nx + t); break;
                case DecoWall.West: if (t >= 0 && t + k <= Nz) list.Add(t * Nx); break;
                default: if (t >= 0 && t + k <= Nz && Nx - k >= 0) list.Add(t * Nx + Nx - k); break;
            }
        }
        return list;
    }

    /// <summary>Every position a k-by-k agent reaches from the starts.</summary>
    public bool[] Reach(IEnumerable<int> starts, int k, bool[] fit = null)
    {
        fit ??= Fits(k);
        var seen = new bool[Nx * Nz];
        var q = new Queue<int>();
        foreach (int s in starts)
            if (s >= 0 && s < fit.Length && fit[s] && !seen[s]) { seen[s] = true; q.Enqueue(s); }
        while (q.Count > 0)
        {
            int c = q.Dequeue(), i = c % Nx, j = c / Nx;
            void Try(int ni, int nj)
            {
                if (ni < 0 || nj < 0 || ni >= Nx || nj >= Nz) return;
                int n = nj * Nx + ni;
                if (!seen[n] && fit[n]) { seen[n] = true; q.Enqueue(n); }
            }
            Try(i + 1, j); Try(i - 1, j); Try(i, j + 1); Try(i, j - 1);
        }
        return seen;
    }

    /// <summary>
    /// True if a walker as wide as the room's agent gets from the first doorway to every other; with
    /// one doorway, that it can at least get a metre and a half into the room.
    /// </summary>
    public bool DoorsJoined(DecoRoom room)
    {
        if (room.Doors.Count == 0) return true;
        int k = Math.Max(1, (int)MathF.Ceiling(room.Agent / Cell - 1e-4f));
        bool[] fit = Fits(k);
        var starts = DoorStarts(room, room.Doors[0], k);
        bool[] reach = Reach(starts, k, fit);
        if (room.Doors.Count == 1)
        {
            var (at, inward) = room.DoorPoint(room.Doors[0]);
            for (int c = 0; c < reach.Length; c++)
            {
                if (!reach[c]) continue;
                var p = new Vector2((c % Nx + k / 2f) * Cell, (c / Nx + k / 2f) * Cell);
                if (Vector2.Dot(p - at, inward) >= MathF.Min(1.5f, MathF.Max(room.Width, room.Depth) * 0.4f)) return true;
            }
            return false;
        }
        for (int d = 1; d < room.Doors.Count; d++)
            if (!DoorStarts(room, room.Doors[d], k).Any(s => reach[s])) return false;
        return true;
    }
}
