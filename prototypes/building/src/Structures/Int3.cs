using System;
using System.Numerics;

namespace GalHack.Structures;

/// <summary>
/// A grid cell. The structure core is plain C# (no engine types) so any prototype, a server or
/// a command-line tool can load, edit and validate structures without a scene.
/// </summary>
public readonly struct Int3 : IEquatable<Int3>
{
    public readonly int X, Y, Z;

    public Int3(int x, int y, int z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static readonly Int3 Zero = new(0, 0, 0);
    public static readonly Int3 UnitX = new(1, 0, 0);
    public static readonly Int3 UnitY = new(0, 1, 0);
    public static readonly Int3 UnitZ = new(0, 0, 1);

    public int this[int axis] => axis switch { 0 => X, 1 => Y, _ => Z };

    public Int3 With(int axis, int value) => axis switch
    {
        0 => new Int3(value, Y, Z),
        1 => new Int3(X, value, Z),
        _ => new Int3(X, Y, value),
    };

    public static Int3 operator +(Int3 a, Int3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Int3 operator -(Int3 a, Int3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Int3 operator -(Int3 a) => new(-a.X, -a.Y, -a.Z);
    public static Int3 operator *(Int3 a, int k) => new(a.X * k, a.Y * k, a.Z * k);
    public static bool operator ==(Int3 a, Int3 b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;
    public static bool operator !=(Int3 a, Int3 b) => !(a == b);

    public static Int3 Min(Int3 a, Int3 b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
    public static Int3 Max(Int3 a, Int3 b) => new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));
    public static Int3 Cross(Int3 a, Int3 b) =>
        new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    public static int Dot(Int3 a, Int3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    /// <summary>The cell containing a point given in cell units (cell centres sit on integers).</summary>
    public static Int3 Round(Vector3 v) =>
        new((int)MathF.Floor(v.X + 0.5f), (int)MathF.Floor(v.Y + 0.5f), (int)MathF.Floor(v.Z + 0.5f));

    public Vector3 ToVector3() => new(X, Y, Z);

    public bool Equals(Int3 other) => this == other;
    public override bool Equals(object obj) => obj is Int3 other && this == other;
    public override int GetHashCode() => unchecked((X * 73856093) ^ (Y * 19349663) ^ (Z * 83492791));
    public override string ToString() => $"{X} {Y} {Z}";
}

/// <summary>The six face directions of a cell. Opposite directions differ only in the lowest bit.</summary>
public enum Dir : byte { PosX, NegX, PosY, NegY, PosZ, NegZ }

public static class Dirs
{
    public static readonly Dir[] All = { Dir.PosX, Dir.NegX, Dir.PosY, Dir.NegY, Dir.PosZ, Dir.NegZ };
    public static readonly string[] Names = { "+x", "-x", "+y", "-y", "+z", "-z" };

    static readonly Int3[] Vectors =
    {
        new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1),
    };

    public static Int3 Vec(this Dir d) => Vectors[(int)d];
    public static Vector3 Normal(this Dir d) => Vectors[(int)d].ToVector3();
    public static Dir Opposite(this Dir d) => (Dir)((int)d ^ 1);
    public static int Axis(this Dir d) => (int)d >> 1;
    public static int Sign(this Dir d) => ((int)d & 1) == 0 ? 1 : -1;
    public static Dir FromAxis(int axis, int sign) => (Dir)(axis * 2 + (sign > 0 ? 0 : 1));
    public static string Name(this Dir d) => Names[(int)d];

    /// <summary>The direction mirrored across the plane normal to an axis.</summary>
    public static Dir Mirror(this Dir d, int axis) => d.Axis() == axis ? d.Opposite() : d;

    public static bool TryParse(string s, out Dir d)
    {
        int i = Array.IndexOf(Names, s.ToLowerInvariant());
        d = i < 0 ? Dir.PosX : (Dir)i;
        return i >= 0;
    }

    public static Dir FromVector(Int3 v)
    {
        for (int i = 0; i < 6; i++)
            if (Vectors[i] == v) return (Dir)i;
        throw new ArgumentException($"not a unit axis vector: {v}");
    }

    /// <summary>
    /// The face slot a surface normal paints into: its largest component. A 45° slope is a tie,
    /// so ties go to Y, then Z, then X, with a tolerance so the mesh normal and a physics hit
    /// normal of the same face always agree.
    /// </summary>
    public static Dir Closest(Vector3 n)
    {
        const float tie = 1e-3f;
        float ax = MathF.Abs(n.X), ay = MathF.Abs(n.Y), az = MathF.Abs(n.Z);
        if (ay >= ax - tie && ay >= az - tie) return n.Y >= 0 ? Dir.PosY : Dir.NegY;
        if (az >= ax - tie) return n.Z >= 0 ? Dir.PosZ : Dir.NegZ;
        return n.X >= 0 ? Dir.PosX : Dir.NegX;
    }
}
