using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace GalHack.Deco;

/// <summary>
/// Colour slots a model paints with. A palette (the place's style kit) gives each slot its colour,
/// so one desk model is an office desk in beige or a Vharn one in plum. Each slot also picks a
/// surface: matte, metal, glow, glass, flesh or leaf.
/// </summary>
public enum DecoSlot
{
    Body, Trim, Dark, Metal, Chrome, Wood, Fabric, Linen, Paper, Plastic, Glass, Screen, Lamp, Signal,
    Plant, Soil, Stone, Bronze, Chitin, Resin, Bone, Flesh, Lattice, Node, Node2, Hazard, Alert, Liquid,
    Frost, Canvas, Rust, Rubber, Gold,
    // Not in palettes: colours the variants paint with.
    Dead, Crack, Char, Ash, Moss, Vine, Leaf, Bloom, BloomVein, Debris, Sand, Snow, Grime,
}

/// <summary>The six materials a deco mesh is drawn with; colour lives in the vertices.</summary>
public enum DecoSurface { Matte, Metal, Glow, Glass, Flesh, Leaf }

/// <summary>
/// What a part is made of: the surface the deco shader draws on it (grain, weave, seams, glaze,
/// brushing, rust...). A part says so with <see cref="DecoPart.As"/>; otherwise it follows from its
/// slot and flags (<see cref="DecoFinishes.Of"/>). Colour still comes from the slot, so a place's
/// pieces share their handful of materials.
/// </summary>
public enum DecoFinish : byte
{
    /// <summary>From the part's slot and flags.</summary>
    Auto,
    /// <summary>Painted metal or board: a fine orange peel; chips at the edges and scratches as it wears, peeling over rust when weathered.</summary>
    Paint,
    /// <summary>Bare or brushed metal: brushed along its length; blooms of rust as it ages.</summary>
    Steel,
    /// <summary>Chrome, brass, bronze, gold: smooth and bright; tarnish and patina with age.</summary>
    Polished,
    /// <summary>Timber: grain along its length, rings on its ends, boards across a wide face; greys and splits in the weather.</summary>
    Wood,
    /// <summary>Moulded plastic: a fine stipple and a soft sheen; scuffs, chalky when old.</summary>
    Plastic,
    /// <summary>Cloth and upholstery: a weave, stitched seams and soft edges; fades and stains.</summary>
    Fabric,
    /// <summary>Leather and vinyl: pebbled, creased, stitched; scuffed and cracked with age.</summary>
    Leather,
    /// <summary>Paper and card: fibres; foxed and yellowed at the edges with age.</summary>
    Paper,
    /// <summary>Glazed ceramic: tiles and grout on a wide face, plain glaze on a small one; crazing with age.</summary>
    Ceramic,
    /// <summary>Rubber: a dull stipple; a tyre's tread.</summary>
    Rubber,
    /// <summary>Concrete and stone: mottle, aggregate and pits; chipped edges with age.</summary>
    Stone,
    /// <summary>A screen: a pixel grid and a darker border when lit, dark glass when dead.</summary>
    Screen,
    /// <summary>Glass: smudges and a gasket at the edges; dirt with age.</summary>
    Glass,
    /// <summary>Living or grown stuff (plants, chitin, bone, resin): cells and mottle.</summary>
    Organic,
    /// <summary>No surface detail (marks, cracks and stains drawn over a piece).</summary>
    Plain,
}

public enum DecoPrim { Hex, Cylinder, Sphere, Dome }

[Flags]
public enum PartFlags
{
    None = 0,
    /// <summary>A screen: goes dark when unpowered and cracks when broken. Its face is the part's -Z side.</summary>
    Screen = 1,
    /// <summary>Comes off when the piece is broken (a chair back, a drawer, a lamp shade).</summary>
    Loose = 2,
    /// <summary>Cloth, cushions, paper or foliage: burns away.</summary>
    Soft = 4,
    /// <summary>Too small or thin to collide with.</summary>
    NoCollide = 8,
    /// <summary>Glass that shatters when the piece is broken.</summary>
    Pane = 16,
    /// <summary>A lamp or glowing panel: goes dark when unpowered.</summary>
    Lit = 32,
    /// <summary>A door or hatch: swings open on its hinge when the piece is found open.</summary>
    Door = 64,
    /// <summary>A tyre: goes flat when the piece is rusted or burnt, and the body settles onto it.</summary>
    Tyre = 128,
    /// <summary>Rots fast (hay, food, produce, crops): long gone or a remnant in an old-world place.</summary>
    Perishable = 256,
}

/// <summary>
/// One primitive of a model: a hexahedron (box, wedge or tapered box from eight corners), a
/// cylinder or cone, or a low-poly sphere, placed by a matrix in the piece's frame (metres,
/// origin at the middle of its footprint on the floor, front facing -Z).
/// </summary>
public sealed class DecoPart
{
    public DecoPrim Kind;
    public Matrix4x4 M = Matrix4x4.Identity;
    /// <summary>Doors: the hinge point and axis (piece frame) and how far it swings open, in degrees.</summary>
    public Vector3 Hinge, HingeAxis = Vector3.UnitY;
    public float OpenAngle;
    /// <summary>Hex corners in unit space before <see cref="M"/>: index bit 0 is +x, bit 1 +y, bit 2 +z.</summary>
    public Vector3[] Corners;
    public int Segments = 8;
    /// <summary>Cylinders: top radius as a share of the bottom one (0 makes a cone).</summary>
    public float Taper = 1f;
    public DecoSlot Slot;
    public PartFlags Flags;
    /// <summary>What it is made of, when its slot doesn't say (a ceramic pot in the trim colour, a leather seat).</summary>
    public DecoFinish Finish;

    public bool Is(PartFlags f) => (Flags & f) != 0;
    /// <summary>Says what the part is made of.</summary>
    public DecoPart As(DecoFinish f) { Finish = f; return this; }
    public DecoPart Loose() { Flags |= PartFlags.Loose; return this; }
    public DecoPart Soft() { Flags |= PartFlags.Soft; return this; }
    public DecoPart Screen() { Flags |= PartFlags.Screen | PartFlags.Lit; return this; }
    public DecoPart Lit() { Flags |= PartFlags.Lit; return this; }
    public DecoPart Pane() { Flags |= PartFlags.Pane; return this; }
    public DecoPart Ghost() { Flags |= PartFlags.NoCollide; return this; }
    public DecoPart Tyre() { Flags |= PartFlags.Tyre; return this; }
    public DecoPart Rots() { Flags |= PartFlags.Perishable | PartFlags.Soft; return this; }

    /// <summary>Makes this a door swinging <paramref name="angle"/> degrees about a vertical hinge at (x, z) (positive opens anticlockwise seen from above).</summary>
    public DecoPart Door(float hx, float hz, float angle) { Flags |= PartFlags.Door; Hinge = new Vector3(hx, 0, hz); HingeAxis = Vector3.UnitY; OpenAngle = angle; return this; }

    /// <summary>A hatch or door on any hinge line through <paramref name="hinge"/> along <paramref name="axis"/>.</summary>
    public DecoPart Door(Vector3 hinge, Vector3 axis, float angle) { Flags |= PartFlags.Door; Hinge = hinge; HingeAxis = Vector3.Normalize(axis); OpenAngle = angle; return this; }

    public DecoPart Clone() => new() { Kind = Kind, M = M, Corners = Corners, Segments = Segments, Taper = Taper, Slot = Slot, Flags = Flags, Finish = Finish, Hinge = Hinge, HingeAxis = HingeAxis, OpenAngle = OpenAngle };

    public Vector3 Centre => Vector3.Transform(Vector3.Zero, M);

    /// <summary>Volume in cubic metres (for the centre of mass).</summary>
    public float Volume
    {
        get
        {
            float det = MathF.Abs(M.GetDeterminant());
            return Kind switch
            {
                DecoPrim.Cylinder => det * MathF.PI * 0.25f * (1 + Taper + Taper * Taper) / 3f,
                DecoPrim.Sphere => det * MathF.PI / 6f,
                DecoPrim.Dome => det * MathF.PI / 12f,
                _ => det * HexVolumeShare(),
            };
        }
    }

    float HexVolumeShare()
    {
        if (Corners == null) return 1f;
        // Average of top and bottom face areas (exact for prisms and near enough for wedges).
        static float Area(Vector3 a, Vector3 b, Vector3 c, Vector3 d) =>
            MathF.Abs((b.X - a.X) * (c.Z - a.Z) - (c.X - a.X) * (b.Z - a.Z)) * 0.5f + MathF.Abs((c.X - a.X) * (d.Z - a.Z) - (d.X - a.X) * (c.Z - a.Z)) * 0.5f;
        float bottom = Area(Corners[0], Corners[1], Corners[5], Corners[4]);
        float top = Area(Corners[2], Corners[3], Corners[7], Corners[6]);
        float h = ((Corners[2].Y + Corners[3].Y + Corners[6].Y + Corners[7].Y) - (Corners[0].Y + Corners[1].Y + Corners[4].Y + Corners[5].Y)) * 0.25f;
        return MathF.Max(0.01f, (bottom + top) * 0.5f * MathF.Abs(h));
    }

    public static readonly Vector3[] UnitCube = MakeCube();

    static Vector3[] MakeCube()
    {
        var c = new Vector3[8];
        for (int i = 0; i < 8; i++) c[i] = new Vector3((i & 1) != 0 ? 0.5f : -0.5f, (i & 2) != 0 ? 0.5f : -0.5f, (i & 4) != 0 ? 0.5f : -0.5f);
        return c;
    }
}

/// <summary>A point a piece shines from, and the slot whose colour it shines.</summary>
public readonly record struct DecoLightSpot(Vector3 At, DecoSlot Slot);

/// <summary>A piece as parts: what its builder made, before any variant is applied.</summary>
public sealed class DecoModel
{
    public readonly List<DecoPart> Parts = new();
    public readonly List<DecoLightSpot> Lights = new();
    /// <summary>Its wires, cables, ropes, chains and hoses, by their anchors (drawn by the forge, never floating).</summary>
    public readonly List<DecoCable> Cables = new();
    /// <summary>Named attachment points other things' cables snap to (a power pole's insulators).</summary>
    public readonly Dictionary<string, Vector3> Anchors = new(StringComparer.Ordinal);
    /// <summary>Where a sparking panel sparks from, if anywhere.</summary>
    public Vector3? Spark;

    public DecoModel Clone()
    {
        var m = new DecoModel { Spark = Spark };
        foreach (DecoPart p in Parts) m.Parts.Add(p.Clone());
        m.Lights.AddRange(Lights);
        foreach (DecoCable c in Cables) m.Cables.Add(c.Clone());
        foreach (var (k, v) in Anchors) m.Anchors[k] = v;
        return m;
    }
}

/// <summary>
/// What a model builder draws with. Units are metres; the piece stands on y = 0 centred on x and
/// z, its front facing -Z, inside its declared footprint (<see cref="W"/> by <see cref="H"/> by
/// <see cref="D"/>). Every call returns the part so flags can be chained: <c>Box(...).Loose()</c>.
/// </summary>
public sealed class DecoBuilder
{
    public readonly DecoModel Model = new();
    /// <summary>The footprint: width (x), height (y) and depth (z) in metres.</summary>
    public readonly float W, H, D;
    /// <summary>Per-piece random stream for small variations (book colours, can positions).</summary>
    public readonly DecoRng Rng;
    readonly Stack<Matrix4x4> _stack = new();
    Matrix4x4 _xf = Matrix4x4.Identity;

    public DecoBuilder(float w, float h, float d, ulong seed)
    {
        W = w; H = h; D = d;
        Rng = new DecoRng(seed);
    }

    public float X0 => -W / 2; public float X1 => W / 2;
    public float Z0 => -D / 2; public float Z1 => D / 2;

    internal DecoPart Add(DecoPart p)
    {
        p.M *= _xf;
        Model.Parts.Add(p);
        return p;
    }

    /// <summary>Draws everything inside the using block through <paramref name="m"/> (applied before the outer transforms).</summary>
    public IDisposable Push(Matrix4x4 m)
    {
        _stack.Push(_xf);
        _xf = m * _xf;
        return new Pop(this);
    }

    sealed class Pop : IDisposable
    {
        readonly DecoBuilder _b;
        public Pop(DecoBuilder b) => _b = b;
        public void Dispose() => _b._xf = _b._stack.Pop();
    }

    /// <summary>Rotation about an axis through a pivot, for a using block.</summary>
    public IDisposable Turn(Vector3 pivot, float xDeg, float yDeg = 0, float zDeg = 0) =>
        Push(DecoGeo.About(pivot, DecoGeo.RotX(xDeg) * DecoGeo.RotY(yDeg) * DecoGeo.RotZ(zDeg)));

    public IDisposable Shift(float x, float y, float z) => Push(DecoGeo.Move(x, y, z));

    /// <summary>An axis-aligned box from two corners.</summary>
    public DecoPart Box(float x0, float y0, float z0, float x1, float y1, float z1, DecoSlot s)
    {
        if (x1 < x0) (x0, x1) = (x1, x0);
        if (y1 < y0) (y0, y1) = (y1, y0);
        if (z1 < z0) (z0, z1) = (z1, z0);
        return Add(new DecoPart
        {
            Kind = DecoPrim.Hex, Corners = DecoPart.UnitCube, Slot = s,
            M = Matrix4x4.CreateScale(MathF.Max(x1 - x0, 1e-4f), MathF.Max(y1 - y0, 1e-4f), MathF.Max(z1 - z0, 1e-4f)) * DecoGeo.Move((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2),
        });
    }

    /// <summary>A box by its centre and size.</summary>
    public DecoPart Cube(float cx, float cy, float cz, float sx, float sy, float sz, DecoSlot s) =>
        Box(cx - sx / 2, cy - sy / 2, cz - sz / 2, cx + sx / 2, cy + sy / 2, cz + sz / 2, s);

    /// <summary>
    /// A box whose top slopes from <paramref name="backTop"/> at the back (+z) to
    /// <paramref name="frontTop"/> at the front (-z): consoles, lecterns, ramps.
    /// </summary>
    public DecoPart Wedge(float x0, float y0, float z0, float x1, float z1, float frontTop, float backTop, DecoSlot s)
    {
        float h = MathF.Max(frontTop, backTop) - y0;
        var c = new Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            float x = (i & 1) != 0 ? 0.5f : -0.5f, z = (i & 4) != 0 ? 0.5f : -0.5f;
            float top = (i & 4) != 0 ? backTop : frontTop;
            float y = (i & 2) != 0 ? (top - y0) / h - 0.5f : -0.5f;
            c[i] = new Vector3(x, y, z);
        }
        return Add(new DecoPart
        {
            Kind = DecoPrim.Hex, Corners = c, Slot = s,
            M = Matrix4x4.CreateScale(x1 - x0, h, z1 - z0) * DecoGeo.Move((x0 + x1) / 2, y0 + h / 2, (z0 + z1) / 2),
        });
    }

    /// <summary>A box whose top face is shrunk by <paramref name="topScale"/> (a plinth, a hopper, a shade).</summary>
    public DecoPart Taper(float x0, float y0, float z0, float x1, float y1, float z1, float topScaleX, float topScaleZ, DecoSlot s)
    {
        var c = new Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            bool top = (i & 2) != 0;
            float x = ((i & 1) != 0 ? 0.5f : -0.5f) * (top ? topScaleX : 1f), z = ((i & 4) != 0 ? 0.5f : -0.5f) * (top ? topScaleZ : 1f);
            c[i] = new Vector3(x, top ? 0.5f : -0.5f, z);
        }
        return Add(new DecoPart
        {
            Kind = DecoPrim.Hex, Corners = c, Slot = s,
            M = Matrix4x4.CreateScale(x1 - x0, y1 - y0, z1 - z0) * DecoGeo.Move((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2),
        });
    }

    /// <summary>An upright cylinder (or cone, with a taper) standing on y0.</summary>
    public DecoPart Cyl(float cx, float y0, float cz, float r, float h, DecoSlot s, int seg = 8, float taper = 1f) =>
        Add(new DecoPart
        {
            Kind = DecoPrim.Cylinder, Segments = seg, Taper = taper, Slot = s,
            M = Matrix4x4.CreateScale(r * 2, h, r * 2) * DecoGeo.Move(cx, y0 + h / 2, cz),
        });

    /// <summary>A round rod or pipe from a to b.</summary>
    public DecoPart Tube(Vector3 a, Vector3 b, float r, DecoSlot s, int seg = 6, float taper = 1f) =>
        Add(new DecoPart { Kind = DecoPrim.Cylinder, Segments = seg, Taper = taper, Slot = s, M = DecoGeo.Along(a, b, r * 2, r * 2) });

    public DecoPart Tube(float ax, float ay, float az, float bx, float by, float bz, float r, DecoSlot s, int seg = 6, float taper = 1f) =>
        Tube(new Vector3(ax, ay, az), new Vector3(bx, by, bz), r, s, seg, taper);

    /// <summary>A square beam from a to b (frames, bent bars, braces).</summary>
    public DecoPart Beam(Vector3 a, Vector3 b, float w, float t, DecoSlot s) =>
        Add(new DecoPart { Kind = DecoPrim.Hex, Corners = DecoPart.UnitCube, Slot = s, M = DecoGeo.Along(a, b, w, t) });

    public DecoPart Beam(float ax, float ay, float az, float bx, float by, float bz, float w, DecoSlot s) =>
        Beam(new Vector3(ax, ay, az), new Vector3(bx, by, bz), w, w, s);

    /// <summary>A low-poly ball (an ellipsoid with different radii).</summary>
    public DecoPart Ball(float cx, float cy, float cz, float rx, float ry, float rz, DecoSlot s, int seg = 8) =>
        Add(new DecoPart { Kind = DecoPrim.Sphere, Segments = seg, Slot = s, M = Matrix4x4.CreateScale(rx * 2, ry * 2, rz * 2) * DecoGeo.Move(cx, cy, cz) });

    public DecoPart Ball(float cx, float cy, float cz, float r, DecoSlot s, int seg = 8) => Ball(cx, cy, cz, r, r, r, s, seg);

    /// <summary>A low-poly half-ellipsoid standing on y0 (a shell, a mound, a carapace).</summary>
    public DecoPart Dome(float cx, float y0, float cz, float rx, float ry, float rz, DecoSlot s, int seg = 10) =>
        Add(new DecoPart { Kind = DecoPrim.Dome, Segments = seg, Slot = s, M = Matrix4x4.CreateScale(rx * 2, ry * 2, rz * 2) * DecoGeo.Move(cx, y0, cz) });

    /// <summary>Four legs inset from a rectangle's corners, from the floor to <paramref name="top"/>.</summary>
    public void Legs(float x0, float z0, float x1, float z1, float top, float t, DecoSlot s, bool round = false)
    {
        foreach (float x in new[] { x0 + t / 2, x1 - t / 2 })
            foreach (float z in new[] { z0 + t / 2, z1 - t / 2 })
            {
                if (round) Cyl(x, 0, z, t / 2, top, s, 6);
                else Box(x - t / 2, 0, z - t / 2, x + t / 2, top, z + t / 2, s);
            }
    }

    /// <summary>Marks where the piece shines from (transformed like the parts around it).</summary>
    public void Light(float x, float y, float z, DecoSlot s = DecoSlot.Lamp) =>
        Model.Lights.Add(new DecoLightSpot(Vector3.Transform(new Vector3(x, y, z), _xf), s));

    public void Spark(float x, float y, float z) => Model.Spark = Vector3.Transform(new Vector3(x, y, z), _xf);

    Vector3 At(Vector3 p) => Vector3.Transform(p, _xf);

    /// <summary>A cable strung between two anchors with a natural sag. Both ends must touch the piece.</summary>
    public DecoCable Cable(Vector3 a, Vector3 b, float radius, DecoSlot s, float slack = 0.04f, bool breakable = true)
    {
        var c = new DecoCable { A = At(a), B = At(b), Spanned = true, Slack = slack, Radius = radius, Slot = s, Breakable = breakable };
        Model.Cables.Add(c);
        return c;
    }

    /// <summary>A cable hanging from one anchor straight down to the floor, its tail lying towards <paramref name="lie"/>.</summary>
    public DecoCable Hang(Vector3 a, Vector3 lie, float radius, DecoSlot s, float tail = 0.3f)
    {
        var c = new DecoCable { A = At(a), B = At(a + lie), Spanned = false, Tail = tail, Radius = radius, Slot = s, Breakable = false };
        Model.Cables.Add(c);
        return c;
    }

    /// <summary>Names an attachment point other cables (power lines, bunting) snap to.</summary>
    public void Anchor(string name, float x, float y, float z) => Model.Anchors[name] = At(new Vector3(x, y, z));
}

public static class DecoBuilderShapes
{
    /// <summary>
    /// A hexahedron from eight corners in the piece frame (bit 0 is +x, bit 1 +y, bit 2 +z, as for
    /// boxes): a car's glass greenhouse, a swept fin, a tapered nose. Stored in unit space around
    /// its bounding box, so collision and volume work as for any box.
    /// </summary>
    public static DecoPart Hull(this DecoBuilder b, Vector3[] corners, DecoSlot s)
    {
        Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
        foreach (Vector3 c in corners) { lo = Vector3.Min(lo, c); hi = Vector3.Max(hi, c); }
        Vector3 size = Vector3.Max(hi - lo, new Vector3(1e-4f)), mid = (lo + hi) / 2;
        var unit = new Vector3[8];
        for (int i = 0; i < 8; i++) unit[i] = (corners[i] - mid) / size;
        return b.Add(new DecoPart { Kind = DecoPrim.Hex, Corners = unit, Slot = s, M = Matrix4x4.CreateScale(size) * DecoGeo.Move(mid) });
    }

    /// <summary>
    /// A box-section hull from a bottom rectangle and a top rectangle (each x0, x1, z0, z1): the
    /// usual way to draw a sloped cab, a greenhouse or a tapering block.
    /// </summary>
    public static DecoPart Frustum(this DecoBuilder b, float y0, float bx0, float bx1, float bz0, float bz1, float y1, float tx0, float tx1, float tz0, float tz1, DecoSlot s) =>
        b.Hull(new[]
        {
            new Vector3(bx0, y0, bz0), new Vector3(bx1, y0, bz0), new Vector3(tx0, y1, tz0), new Vector3(tx1, y1, tz0),
            new Vector3(bx0, y0, bz1), new Vector3(bx1, y0, bz1), new Vector3(tx0, y1, tz1), new Vector3(tx1, y1, tz1),
        }, s);

    /// <summary>A round (or oval, with <paramref name="ry"/>) hull along Z whose lowest corner sits exactly at <paramref name="bottom"/>.</summary>
    public static DecoPart Barrel(this DecoBuilder b, float z0, float z1, float cx, float bottom, float rx, float ry, DecoSlot s, int seg = 16, float taper = 1f, float rise = 0f)
    {
        DecoPart p = b.Add(new DecoPart
        {
            Kind = DecoPrim.Cylinder, Segments = seg, Taper = taper, Slot = s,
            M = DecoGeo.Along(new Vector3(cx, bottom + ry, z0), new Vector3(cx, bottom + ry + rise, z1), rx * 2, ry * 2),
        });
        float lo = DecoEmit.Vertices(p).Min(v => v.Y);
        p.M *= DecoGeo.Move(0, bottom - lo, 0);
        return p;
    }
}
