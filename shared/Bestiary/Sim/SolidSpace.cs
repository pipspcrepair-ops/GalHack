using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>
/// Solid ground a creature can live inside: a cave or tunnel in the voxel terrain, or the
/// blocks of a place (a POI, a wreck). Both are 1 m cells, so the same creatures, paths and
/// tests work in either. Positions are in the creature simulation's own frame.
/// </summary>
public interface ISolidSpace
{
    /// <summary>
    /// Density at a point: positive inside solid, negative in open air, zero on the surface
    /// that is drawn. Roughly metres near the surface; it may be clamped further away (a block
    /// space saturates at a metre, the voxel terrain at two).
    /// </summary>
    float Density(Vector3 p);

    /// <summary>Whether the 1 m cell whose minimum corner is (x, y, z) is solid.</summary>
    bool SolidCell(int x, int y, int z);

    /// <summary>
    /// A solid cell that is a ramp or a stair of a flight (45 degrees, a metre up across the cell): the
    /// way it rises, as <see cref="Slopes"/> numbers it, or -1 for a whole block or open air.
    /// </summary>
    int SlopeCell(int x, int y, int z) => -1;
}

/// <summary>The ways a ramp cell can rise: 0 toward +x, 1 toward -x, 2 toward +z, 3 toward -z.</summary>
public static class Slopes
{
    public static readonly Vector3I[] Rise = { new(1, 0, 0), new(-1, 0, 0), new(0, 0, 1), new(0, 0, -1) };

    /// <summary>The slope number rising toward a level unit step, or -1.</summary>
    public static int Of(int dx, int dz) => dx > 0 ? 0 : dx < 0 ? 1 : dz > 0 ? 2 : dz < 0 ? 3 : -1;

    /// <summary>How high a ramp's surface stands over the bottom of its cell at a point (0 to 1 m).</summary>
    public static float Height(int slope, float lx, float lz) => Math.Clamp(slope switch { 0 => lx, 1 => 1f - lx, 2 => lz, _ => 1f - lz }, 0f, 1f);
}

/// <summary>
/// A place built of 1 m blocks: a POI's interior, a wreck's hold. The density is an exact
/// signed distance to the nearest solid block, so a creature's body never ends up inside a
/// block, except that inside corners (floor to wall, wall to ceiling) are filleted by a small
/// smooth minimum: a crawler rounds them instead of jamming. Outside corners stay sharp; a body
/// held a clearance off them goes round in an arc.
/// </summary>
public sealed class BlockSpace : ISolidSpace
{
    readonly Func<int, int, int, bool> _solid;
    readonly Vector3I _min, _max;
    /// <summary>How round inside corners are, metres.</summary>
    public float Fillet = 0.3f;
    /// <summary>
    /// Where the block grid sits in the sim's frame: the grid's cell (0,0,0) has its corner at this
    /// point (whole metres, so the cells stay the sim's cells). Zero when the sim works in the grid's frame.
    /// </summary>
    public Vector3I Offset;

    /// <summary>
    /// A space over any block lookup. Cells outside [min, max) count as solid when
    /// <paramref name="outsideSolid"/>: a sealed place, so nothing ever wanders out of it.
    /// </summary>
    public BlockSpace(Func<int, int, int, bool> solid, Vector3I min, Vector3I max, bool outsideSolid = true)
    {
        _min = min;
        _max = max;
        _solid = outsideSolid
            ? (x, y, z) => x < min.X || y < min.Y || z < min.Z || x >= max.X || y >= max.Y || z >= max.Z || solid(x, y, z)
            : solid;
    }

    public Vector3I Min => _min;
    public Vector3I Max => _max;

    public bool SolidCell(int x, int y, int z) => _solid(x - Offset.X, y - Offset.Y, z - Offset.Z);

    /// <summary>
    /// Ramps and stairs among the solid cells (in the grid's frame): the way each rises (see
    /// <see cref="Slopes"/>), or -1. Such a cell is solid only under its 45-degree surface, so a
    /// walker goes up a flight as people do, not into a metre-high step.
    /// </summary>
    public Func<int, int, int, int> Slope;

    public int SlopeCell(int x, int y, int z) => Slope == null ? -1 : Slope(x - Offset.X, y - Offset.Y, z - Offset.Z);

    int SlopeAt(int x, int y, int z) => Slope == null ? -1 : Slope(x, y, z);

    const float InvSqrt2 = 0.70710678f;

    /// <summary>Distance from a point to a solid cell: its box, or for a ramp its wedge.</summary>
    float CellDistance(Vector3 q, int x, int y, int z)
    {
        int slope = SlopeAt(x, y, z);
        if (slope < 0) return BoxDistance(q, x, y, z);
        // In the wedge's own terms: s along the way it rises, t up, w across; it fills 0 <= t <= s <= 1.
        float lx = q.X - x, lz = q.Z - z, t = q.Y - y;
        float sAlong = slope switch { 0 => lx, 1 => 1f - lx, 2 => lz, _ => 1f - lz };
        float w = slope < 2 ? lz : lx;
        float dw = MathF.Max(MathF.Max(-w, w - 1f), 0f);
        float d2 = TriangleDistance(sAlong, t);
        return MathF.Sqrt(d2 * d2 + dw * dw);
    }

    /// <summary>Distance in the plane from (s, t) to the triangle (0,0), (1,0), (1,1); zero inside it.</summary>
    static float TriangleDistance(float s, float t)
    {
        if (t >= 0f && s <= 1f && t <= s) return 0f;
        float best = Segment(s, t, 0f, 0f, 1f, 0f);
        best = MathF.Min(best, Segment(s, t, 1f, 0f, 1f, 1f));
        return MathF.Min(best, Segment(s, t, 0f, 0f, 1f, 1f));
    }

    static float Segment(float px, float py, float ax, float ay, float bx, float by)
    {
        float vx = bx - ax, vy = by - ay;
        float k = Math.Clamp(((px - ax) * vx + (py - ay) * vy) / (vx * vx + vy * vy), 0f, 1f);
        float dx = px - ax - vx * k, dy = py - ay - vy * k;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    public float Density(Vector3 p)
    {
        Vector3 q = p - new Vector3(Offset.X, Offset.Y, Offset.Z);
        int cx = (int)MathF.Floor(q.X), cy = (int)MathF.Floor(q.Y), cz = (int)MathF.Floor(q.Z);
        int here = _solid(cx, cy, cz) ? SlopeAt(cx, cy, cz) : -2;
        // In a ramp's cell: under its surface is inside, over it is in the open.
        float underRamp = here >= 0 ? (Slopes.Height(here, q.X - cx, q.Z - cz) - (q.Y - cy)) * InvSqrt2 : 1f;
        if (here == -1 || here >= 0 && underRamp > 0f)
        {
            // Inside: how deep, against the nearest open cell. Only the neighbouring cells can be
            // nearer than a metre, and nothing here cares about more than a metre.
            float best = here >= 0 ? underRamp : 1f;
            for (int dz = -1; dz <= 1; dz++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (_solid(cx + dx, cy + dy, cz + dz)) continue;
                float d = BoxDistance(q, cx + dx, cy + dy, cz + dz);
                if (d < best) best = d;
            }
            return best;
        }
        // Outside: distance to the nearest solid cell, with inside corners smoothed.
        float d1 = 1f, d2 = 1f;
        bool nearRamp = false;
        for (int dz = -1; dz <= 1; dz++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            if (!_solid(cx + dx, cy + dy, cz + dz)) continue;
            float d = CellDistance(q, cx + dx, cy + dy, cz + dz);
            if (d < d1) { d2 = d1; d1 = d; nearRamp = SlopeAt(cx + dx, cy + dy, cz + dz) >= 0; }
            else if (d < d2) d2 = d;
        }
        // Over a ramp the next step's edge lies on its own surface: no corner there to round.
        if (nearRamp) return -d1;
        // The two nearest blocks: if both are close (a corner), pull the surface in a little.
        float k = Fillet;
        float h = Math.Clamp(0.5f + 0.5f * (d2 - d1) / k, 0f, 1f);
        float smin = d2 + (d1 - d2) * h - k * h * (1f - h);
        return -MathF.Min(d1, smin);
    }

    /// <summary>Distance from a point to the unit cell at (x, y, z), zero inside it.</summary>
    static float BoxDistance(Vector3 q, int x, int y, int z)
    {
        float dx = MathF.Max(MathF.Max(x - q.X, q.X - (x + 1)), 0f);
        float dy = MathF.Max(MathF.Max(y - q.Y, q.Y - (y + 1)), 0f);
        float dz = MathF.Max(MathF.Max(z - q.Z, q.Z - (z + 1)), 0f);
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}

/// <summary>
/// Several spaces as one: solid wherever any of them is (a POI dug into the voxel terrain).
/// </summary>
public sealed class CombinedSpace : ISolidSpace
{
    readonly ISolidSpace[] _parts;
    public CombinedSpace(params ISolidSpace[] parts) => _parts = parts;

    public float Density(Vector3 p)
    {
        float d = float.MinValue;
        foreach (ISolidSpace s in _parts) d = MathF.Max(d, s.Density(p));
        return d;
    }

    public bool SolidCell(int x, int y, int z)
    {
        foreach (ISolidSpace s in _parts) if (s.SolidCell(x, y, z)) return true;
        return false;
    }

    public int SlopeCell(int x, int y, int z)
    {
        foreach (ISolidSpace s in _parts) if (s.SlopeCell(x, y, z) is int k && k >= 0) return k;
        return -1;
    }
}

/// <summary>Geometry over a solid space: normals, distances, projecting onto surfaces, lines of sight.</summary>
public static class SpaceMath
{
    const float E = 0.12f;

    /// <summary>The surface normal (out of the solid, into the air) at a point; zero far from any surface.</summary>
    public static Vector3 Normal(ISolidSpace s, Vector3 p) => Gradient(s, p, out _);

    /// <summary>The density gradient, normalised to point out of the solid, and its length.</summary>
    public static Vector3 Gradient(ISolidSpace s, Vector3 p, out float length)
    {
        var g = new Vector3(
            s.Density(p - new Vector3(E, 0, 0)) - s.Density(p + new Vector3(E, 0, 0)),
            s.Density(p - new Vector3(0, E, 0)) - s.Density(p + new Vector3(0, E, 0)),
            s.Density(p - new Vector3(0, 0, E)) - s.Density(p + new Vector3(0, 0, E))) / (2f * E);
        length = g.Length();
        return length > 1e-5f ? g / length : Vector3.Zero;
    }

    /// <summary>
    /// A signed distance estimate in metres (positive inside the solid): the density over its
    /// gradient. Exact for a block space; close to the surface for the voxel terrain, whose
    /// density is steeper than a distance where cells were dug out.
    /// </summary>
    public static float Distance(ISolidSpace s, Vector3 p, out Vector3 normal)
    {
        normal = Gradient(s, p, out float g);
        float d = s.Density(p);
        return d / MathF.Max(g, 0.25f);
    }

    /// <summary>
    /// Moves a point onto the surface held <paramref name="clearance"/> off the solid (so a body
    /// of that radius rests against it), by Newton steps along the normal. Returns false if
    /// there is no surface within reach.
    /// </summary>
    public static bool Project(ISolidSpace s, ref Vector3 p, float clearance, out Vector3 normal, int iterations = 4)
    {
        normal = Vector3.Zero;
        for (int i = 0; i < iterations; i++)
        {
            float d = Distance(s, p, out Vector3 n);
            if (n == Vector3.Zero) return false;
            normal = n;
            float err = d + clearance; // want d = -clearance; too close (err > 0) moves out along n
            if (MathF.Abs(err) < 0.004f) return true;
            p += n * Math.Clamp(err, -0.6f, 0.6f);
        }
        float last = Distance(s, p, out Vector3 n2);
        if (n2 != Vector3.Zero) normal = n2;
        return n2 != Vector3.Zero && MathF.Abs(last + clearance) < 0.12f;
    }

    /// <summary>The height of the first floor at or below (x, fromY, z), if one lies within <paramref name="reach"/> metres.</summary>
    public static float? FloorBelow(ISolidSpace s, float x, float fromY, float z, float reach = 4f)
    {
        var p = new Vector3(x, fromY, z);
        float prev = s.Density(p);
        if (prev > 0f)
        {
            // Started inside: climb out first (a step up onto a ledge).
            for (float up = 0.25f; up <= 1.5f; up += 0.25f)
            {
                float d = s.Density(p + Vector3.Up * up);
                if (d <= 0f) return Bisect(s, p + Vector3.Up * (up - 0.25f), p + Vector3.Up * up);
            }
            return null;
        }
        const float step = 0.25f;
        for (float t = step; t <= reach; t += step)
        {
            Vector3 q = p + Vector3.Down * t;
            float d = s.Density(q);
            if (d > 0f) return Bisect(s, q, q + Vector3.Up * step);
            prev = d;
        }
        return null;
    }

    /// <summary>Between a solid point and an open one above it: the height of the surface.</summary>
    static float Bisect(ISolidSpace s, Vector3 solid, Vector3 open)
    {
        for (int i = 0; i < 8; i++)
        {
            Vector3 mid = (solid + open) * 0.5f;
            if (s.Density(mid) > 0f) solid = mid; else open = mid;
        }
        return (solid.Y + open.Y) * 0.5f;
    }

    /// <summary>Whether a straight line stays in the open (held <paramref name="clearance"/> off solid), sampled every 0.2 m.</summary>
    public static bool Clear(ISolidSpace s, Vector3 a, Vector3 b, float clearance = 0f)
    {
        float len = a.DistanceTo(b);
        int n = Math.Max(1, (int)MathF.Ceiling(len / 0.2f));
        for (int i = 0; i <= n; i++)
        {
            Vector3 p = a.Lerp(b, i / (float)n);
            if (s.Density(p) > -clearance * 0.5f) return false;
        }
        return true;
    }

    /// <summary>How far along a ray it is to solid, up to <paramref name="max"/>.</summary>
    public static float Ray(ISolidSpace s, Vector3 from, Vector3 dir, float max)
    {
        const float step = 0.2f;
        for (float t = 0f; t <= max; t += step)
            if (s.Density(from + dir * t) > 0f) return MathF.Max(0f, t - step * 0.5f);
        return max;
    }

    /// <summary>Is the cell at a point open with solid next to it (a surface a crawler can hold)?</summary>
    public static bool SurfaceCell(ISolidSpace s, int x, int y, int z)
    {
        if (s.SolidCell(x, y, z)) return false;
        return s.SolidCell(x + 1, y, z) || s.SolidCell(x - 1, y, z) || s.SolidCell(x, y + 1, z)
            || s.SolidCell(x, y - 1, z) || s.SolidCell(x, y, z + 1) || s.SolidCell(x, y, z - 1);
    }

    public static Vector3I Cell(Vector3 p) => new((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y), (int)MathF.Floor(p.Z));
    public static Vector3 Center(Vector3I c) => new(c.X + 0.5f, c.Y + 0.5f, c.Z + 0.5f);

    /// <summary>The minimal rotation taking unit vector a onto unit vector b, applied to v.</summary>
    public static Vector3 Transport(Vector3 v, Vector3 a, Vector3 b)
    {
        Vector3 axis = a.Cross(b);
        float sin = axis.Length(), cos = a.Dot(b);
        if (sin < 1e-6f) return cos > 0f ? v : -v;
        axis /= sin;
        float angle = MathF.Atan2(sin, cos);
        return v.Rotated(axis.Normalized(), angle);
    }

    /// <summary>
    /// Turns unit vector a a share t of the way towards unit vector b, safely even when they
    /// point opposite ways (a body flipping from a ceiling to a floor).
    /// </summary>
    public static Vector3 TurnToward(Vector3 a, Vector3 b, float t)
    {
        a = a.LengthSquared() > 1e-8f ? a.Normalized() : b;
        Vector3 axis = a.Cross(b);
        float sin = axis.Length(), cos = Math.Clamp(a.Dot(b), -1f, 1f);
        if (sin < 1e-5f)
        {
            if (cos > 0f) return b;
            // Opposite: turn over about any axis square to them.
            axis = MathF.Abs(a.Y) < 0.9f ? a.Cross(Vector3.Up) : a.Cross(Vector3.Right);
        }
        float angle = MathF.Atan2(sin, cos) * Math.Clamp(t, 0f, 1f);
        return a.Rotated(axis.Normalized(), angle).Normalized();
    }

    /// <summary>An orthonormal frame with Y along up and -Z as close to forward as possible.</summary>
    public static Basis Frame(Vector3 forward, Vector3 up)
    {
        Vector3 y = up.Normalized();
        Vector3 f = forward - y * forward.Dot(y);
        if (f.LengthSquared() < 1e-6f) f = MathF.Abs(y.Y) < 0.9f ? Vector3.Up - y * y.Y : Vector3.Forward;
        f = f.Normalized();
        Vector3 z = -f;
        Vector3 x = y.Cross(z).Normalized();
        return new Basis(x, y, z);
    }
}
