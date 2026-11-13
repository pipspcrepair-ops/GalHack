using System;
using System.Collections.Generic;
using System.Text;

namespace GalHack.Structures;

/// <summary>
/// Writes blueprints from short descriptions instead of hand-typed cells: boxes of blocks, rooms
/// carved out of them, devices set by the corner of their footprint, doors across their doorways,
/// thrusters by the way they push. It builds a real <see cref="Structure"/> on the catalogue it is
/// given, so a device's size and turn come from the data, and writes it in the canonical text
/// (<see cref="Blueprint.Save"/>) under a header. The stock set (slice/tests/BlueprintSet.cs) is
/// written with it; the blueprint test checks what comes out.
/// </summary>
public sealed class Designer
{
    public readonly Structure S;
    readonly Catalogue _cat;

    public Designer(Catalogue catalogue, string name, CoreMode mode)
    {
        _cat = catalogue;
        S = new Structure(catalogue) { Name = name, Mode = mode, Owner = Building.DefaultOwner };
    }

    /// <summary>A named paint, its palette slot.</summary>
    public ushort Paint(string name, Paint paint) => S.Palette.Intern(paint, name);

    /// <summary>The turn whose up and forward (-z) point these ways, as a blueprint writes it ("+y-z").</summary>
    public static string Rot(Dir up, Dir forward) => up.Name() + forward.Name();

    static (Int3, Int3) Order(Int3 a, Int3 b) => (Int3.Min(a, b), Int3.Max(a, b));

    /// <summary>Every cell of a box, corners inclusive, in either order.</summary>
    public static IEnumerable<Int3> Cells(Int3 a, Int3 b)
    {
        var (lo, hi) = Order(a, b);
        for (int y = lo.Y; y <= hi.Y; y++)
            for (int z = lo.Z; z <= hi.Z; z++)
                for (int x = lo.X; x <= hi.X; x++)
                    yield return new Int3(x, y, z);
    }

    /// <summary>Fills a box with one block (armour in a shape, or one-cell parts), all faces in one paint.</summary>
    public Designer Fill(Int3 a, Int3 b, string type, ushort paint = 0, string shape = null, string rot = null)
    {
        BlockType t = _cat.Type(type);
        ShapeDef sh = shape != null ? _cat.Shape(shape) : null;
        int r = Turn(rot);
        foreach (Int3 p in Cells(a, b))
        {
            Block block = Building.Make(S, t, sh, r);
            block.Paint = FacePaint.All(paint);
            S.Set(p, block);
        }
        return this;
    }

    /// <summary>Clears a box (every block with a cell in it goes whole).</summary>
    public Designer Carve(Int3 a, Int3 b)
    {
        foreach (Int3 p in Cells(a, b)) S.Clear(p);
        return this;
    }

    /// <summary>
    /// A room: a box of one block with its inside carved out, so its floor, walls and roof are one
    /// cell thick round the clear space between <paramref name="a"/> and <paramref name="b"/>.
    /// The floor's top face takes <paramref name="floor"/>, the inner wall faces <paramref name="inner"/>.
    /// </summary>
    public Designer Room(Int3 a, Int3 b, string type, ushort outer, ushort inner, ushort floor)
    {
        var (lo, hi) = Order(a, b);
        Int3 one = new(1, 1, 1);
        Fill(lo - one, hi + one, type, outer).Carve(lo, hi);
        Face(new Int3(lo.X, lo.Y - 1, lo.Z), new Int3(hi.X, lo.Y - 1, hi.Z), Dir.PosY, floor);
        Face(new Int3(lo.X, hi.Y + 1, lo.Z), new Int3(hi.X, hi.Y + 1, hi.Z), Dir.NegY, inner);
        Face(new Int3(lo.X - 1, lo.Y, lo.Z), new Int3(lo.X - 1, hi.Y, hi.Z), Dir.PosX, inner);
        Face(new Int3(hi.X + 1, lo.Y, lo.Z), new Int3(hi.X + 1, hi.Y, hi.Z), Dir.NegX, inner);
        Face(new Int3(lo.X, lo.Y, lo.Z - 1), new Int3(hi.X, hi.Y, lo.Z - 1), Dir.PosZ, inner);
        return Face(new Int3(lo.X, lo.Y, hi.Z + 1), new Int3(hi.X, hi.Y, hi.Z + 1), Dir.NegZ, inner);
    }

    /// <summary>Paints one face of every armour block in a box (a floor's finish, a stripe).</summary>
    public Designer Face(Int3 a, Int3 b, Dir face, ushort paint)
    {
        foreach (Int3 p in Cells(a, b))
            if (S.TryGet(p, out Block block) && !block.IsFiller && S.TypeOf(block).IsArmor)
            {
                block.Paint[face] = paint;
                S.Set(p, block);
            }
        return this;
    }

    /// <summary>
    /// A device with its turned footprint's low corner at <paramref name="corner"/>: whatever was in
    /// its cells goes. Returns its anchor (where the blueprint writes it).
    /// </summary>
    public Int3 Put(string type, Int3 corner, string rot = null, ushort paint = 0)
    {
        BlockType t = _cat.Type(type);
        int r = Turn(rot);
        Int3 anchor = Footprint.AnchorFor(t, r, corner);
        foreach (Int3 c in Footprint.Cells(anchor, t, r)) S.Clear(c);
        Block block = Building.Make(S, t, null, r);
        block.Paint = FacePaint.All(paint);
        S.Set(anchor, block);
        return anchor;
    }

    /// <summary>A thruster pushing the structure toward <paramref name="push"/>; its exhaust (and any length it has) points the other way.</summary>
    public Int3 Thruster(string type, Int3 corner, Dir push) =>
        Put(type, corner, Rot(push.Axis() == 1 ? Dir.PosZ : Dir.PosY, push));

    /// <summary>A door across its doorway: the leaf spans the wall, <paramref name="through"/> is the way you walk through it.</summary>
    public Int3 Door(string type, Int3 corner, Dir through) => Put(type, corner, Rot(Dir.PosY, through));

    /// <summary>A flight of stairs rising toward <paramref name="up"/> (two 0.5 m steps a cell).</summary>
    public Int3 Stairs(Int3 at, Dir up, ushort paint = 0) => Put("stairs", at, Rot(Dir.PosY, up.Opposite()), paint);

    /// <summary>A part that hangs on a face by its back (+z): a light on a ceiling or wall, a ladder.</summary>
    public Int3 Mount(string type, Int3 at, Dir onto) => Put(type, at, Rot(onto.Axis() == 1 ? Dir.PosZ : Dir.PosY, onto.Opposite()));

    int Turn(string rot)
    {
        if (rot == null) return Orientations.Identity;
        if (!Orientations.TryParse(rot, out int r)) throw new ArgumentException($"bad turn '{rot}'");
        return r;
    }

    /// <summary>The blueprint's text: the description as comments, then the canonical blocks.</summary>
    public string Text(string description)
    {
        var sb = new StringBuilder();
        foreach (string line in description.Trim().Split('\n')) sb.Append("# ").Append(line.Trim()).Append('\n');
        sb.Append("# Written by slice/tests/BlueprintSet.cs (Designer); regenerate rather than edit.\n");
        return sb.Append(Blueprint.Save(S)).ToString();
    }
}
