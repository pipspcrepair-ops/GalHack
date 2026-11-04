using System;
using System.Numerics;

namespace TerrainDigging.Terrain;

/// <summary>
/// An integer cell or chunk coordinate. Cell (x, y, z) is the 1 m cube [x, x+1) × [y, y+1) × [z, z+1).
/// Ordering is X, then Y, then Z, and is used wherever output must be stable (edit logs, hashes).
/// </summary>
public readonly struct Int3 : IEquatable<Int3>, IComparable<Int3>
{
    public readonly int X, Y, Z;

    public Int3(int x, int y, int z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static readonly Int3 Zero = new(0, 0, 0);
    public static readonly Int3 One = new(1, 1, 1);
    public static readonly Int3 Up = new(0, 1, 0);

    public static Int3 operator +(Int3 a, Int3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Int3 operator -(Int3 a, Int3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Int3 operator *(Int3 a, int s) => new(a.X * s, a.Y * s, a.Z * s);
    public static bool operator ==(Int3 a, Int3 b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;
    public static bool operator !=(Int3 a, Int3 b) => !(a == b);

    /// <summary>The cell containing a world position.</summary>
    public static Int3 Floor(Vector3 p) => new((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y), (int)MathF.Floor(p.Z));

    public Vector3 Center => new(X + 0.5f, Y + 0.5f, Z + 0.5f);
    public Vector3 ToVector() => new(X, Y, Z);
    public int LengthSquared => X * X + Y * Y + Z * Z;

    public bool Equals(Int3 other) => this == other;
    public override bool Equals(object obj) => obj is Int3 other && this == other;
    public override int GetHashCode() => unchecked((X * 73856093) ^ (Y * 19349663) ^ (Z * 83492791));

    public int CompareTo(Int3 other)
    {
        if (X != other.X) return X.CompareTo(other.X);
        if (Y != other.Y) return Y.CompareTo(other.Y);
        return Z.CompareTo(other.Z);
    }

    public override string ToString() => $"({X}, {Y}, {Z})";
}

/// <summary>An axis-aligned box of cells, Min inclusive and Max exclusive.</summary>
public readonly struct CellBox : IEquatable<CellBox>
{
    public readonly Int3 Min, Max;

    public CellBox(Int3 min, Int3 max)
    {
        Min = min;
        Max = max;
    }

    public static CellBox FromSize(Int3 min, Int3 size) => new(min, min + size);

    public Int3 Size => Max - Min;
    public int Volume => Math.Max(0, Size.X) * Math.Max(0, Size.Y) * Math.Max(0, Size.Z);
    public bool IsEmpty => Max.X <= Min.X || Max.Y <= Min.Y || Max.Z <= Min.Z;

    public bool Contains(Int3 c) =>
        c.X >= Min.X && c.X < Max.X && c.Y >= Min.Y && c.Y < Max.Y && c.Z >= Min.Z && c.Z < Max.Z;

    public bool Intersects(CellBox o) =>
        Min.X < o.Max.X && o.Min.X < Max.X && Min.Y < o.Max.Y && o.Min.Y < Max.Y && Min.Z < o.Max.Z && o.Min.Z < Max.Z;

    public CellBox Grow(int side, int below, int above) =>
        new(new Int3(Min.X - side, Min.Y - below, Min.Z - side), new Int3(Max.X + side, Max.Y + above, Max.Z + side));

    public Vector3 CenterPosition => (Min.ToVector() + Max.ToVector()) * 0.5f;

    public bool Equals(CellBox other) => Min == other.Min && Max == other.Max;
    public override bool Equals(object obj) => obj is CellBox other && Equals(other);
    public override int GetHashCode() => Min.GetHashCode() * 31 + Max.GetHashCode();
    public override string ToString() => $"[{Min}..{Max})";
}
