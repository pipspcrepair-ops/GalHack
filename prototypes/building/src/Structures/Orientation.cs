using System;
using System.Collections.Generic;
using System.Numerics;

namespace GalHack.Structures;

/// <summary>
/// A signed permutation matrix, stored as the images of the local X, Y and Z axes. Every block
/// orientation is one of these, so rotating and mirroring blocks is exact integer maths.
/// </summary>
public readonly struct Mat3 : IEquatable<Mat3>
{
    public readonly Int3 X, Y, Z;

    public Mat3(Int3 x, Int3 y, Int3 z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static readonly Mat3 Identity = new(Int3.UnitX, Int3.UnitY, Int3.UnitZ);

    public Int3 Apply(Int3 v) => X * v.X + Y * v.Y + Z * v.Z;
    public Vector3 Apply(Vector3 v) => X.ToVector3() * v.X + Y.ToVector3() * v.Y + Z.ToVector3() * v.Z;

    /// <summary>this × other: apply <paramref name="other"/> first.</summary>
    public Mat3 Mul(Mat3 other) => new(Apply(other.X), Apply(other.Y), Apply(other.Z));

    public int Det => Int3.Dot(X, Int3.Cross(Y, Z));

    public Mat3 Transposed() => new(
        new Int3(X.X, Y.X, Z.X),
        new Int3(X.Y, Y.Y, Z.Y),
        new Int3(X.Z, Y.Z, Z.Z));

    /// <summary>Reflection across the plane normal to one axis.</summary>
    public static Mat3 Mirror(int axis) => new(
        axis == 0 ? -Int3.UnitX : Int3.UnitX,
        axis == 1 ? -Int3.UnitY : Int3.UnitY,
        axis == 2 ? -Int3.UnitZ : Int3.UnitZ);

    public bool Equals(Mat3 o) => X == o.X && Y == o.Y && Z == o.Z;
    public override bool Equals(object obj) => obj is Mat3 m && Equals(m);
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);

    /// <summary>All 48 signed permutations: the 24 rotations and the 24 reflections of a cube.</summary>
    public static IEnumerable<Mat3> AllSignedPermutations()
    {
        int[][] perms = { new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } };
        Int3[] axes = { Int3.UnitX, Int3.UnitY, Int3.UnitZ };
        foreach (int[] p in perms)
            for (int signs = 0; signs < 8; signs++)
                yield return new Mat3(
                    axes[p[0]] * ((signs & 1) != 0 ? -1 : 1),
                    axes[p[1]] * ((signs & 2) != 0 ? -1 : 1),
                    axes[p[2]] * ((signs & 4) != 0 ? -1 : 1));
    }
}

/// <summary>
/// The 24 ways a block can face. Index 0 is the identity. In text an orientation is written as
/// where the block's up (+Y) and forward (-Z) point, so the identity is "+y-z" and a slope
/// turned to face right is "+y+x".
/// </summary>
public static class Orientations
{
    public const int Count = 24;
    public const int Identity = 0;

    static readonly Mat3[] Matrices = new Mat3[Count];
    static readonly string[] NameTable = new string[Count];
    static readonly Dictionary<Mat3, int> Lookup = new();
    static readonly Dir[,] ToWorldTable = new Dir[Count, 6];
    static readonly Dir[,] ToLocalTable = new Dir[Count, 6];

    static Orientations()
    {
        Dir[] ups = { Dir.PosY, Dir.NegY, Dir.PosX, Dir.NegX, Dir.PosZ, Dir.NegZ };
        Dir[] forwards = { Dir.NegZ, Dir.PosZ, Dir.PosX, Dir.NegX, Dir.PosY, Dir.NegY };
        int i = 0;
        foreach (Dir up in ups)
            foreach (Dir forward in forwards)
            {
                if (up.Axis() == forward.Axis()) continue;
                Int3 y = up.Vec(), z = -forward.Vec(); // local +Z is "back"
                var m = new Mat3(Int3.Cross(y, z), y, z);
                Matrices[i] = m;
                NameTable[i] = up.Name() + forward.Name();
                Lookup[m] = i;
                i++;
            }

        for (int o = 0; o < Count; o++)
            foreach (Dir d in Dirs.All)
            {
                Dir world = Dirs.FromVector(Matrices[o].Apply(d.Vec()));
                ToWorldTable[o, (int)d] = world;
                ToLocalTable[o, (int)world] = d;
            }
    }

    public static Mat3 Matrix(int o) => Matrices[o];
    public static string Name(int o) => NameTable[o];

    /// <summary>The orientation with this matrix, or -1 if it is a reflection or not a cube symmetry.</summary>
    public static int Find(Mat3 m) => Lookup.TryGetValue(m, out int o) ? o : -1;

    public static bool TryParse(string s, out int o)
    {
        o = Array.IndexOf(NameTable, s.ToLowerInvariant());
        return o >= 0;
    }

    public static Dir ToWorld(int o, Dir local) => ToWorldTable[o, (int)local];
    public static Dir ToLocal(int o, Dir world) => ToLocalTable[o, (int)world];

    /// <summary>Turns an orientation a quarter turn (or more) about a structure axis.</summary>
    public static int Rotate(int o, int axis, int quarterTurns)
    {
        Mat3 turn = QuarterTurn(axis);
        Mat3 m = Matrices[o];
        int n = ((quarterTurns % 4) + 4) % 4;
        for (int k = 0; k < n; k++) m = turn.Mul(m);
        return Lookup[m];
    }

    static Mat3 QuarterTurn(int axis) => axis switch
    {
        0 => new Mat3(Int3.UnitX, Int3.UnitZ, -Int3.UnitY),
        1 => new Mat3(-Int3.UnitZ, Int3.UnitY, Int3.UnitX),
        _ => new Mat3(Int3.UnitY, -Int3.UnitX, Int3.UnitZ),
    };

    /// <summary>
    /// The orientation of a block's mirror image across a structure plane. A reflection is not a
    /// rotation, but every shape here has a mirror symmetry S of its own, so M·R·S is a proper
    /// rotation that draws exactly the mirrored block.
    /// </summary>
    public static int Mirror(int o, int axis, Mat3 shapeSymmetry)
    {
        Mat3 m = Mat3.Mirror(axis).Mul(Matrices[o]).Mul(shapeSymmetry);
        int found = Find(m);
        return found >= 0 ? found : o;
    }
}
