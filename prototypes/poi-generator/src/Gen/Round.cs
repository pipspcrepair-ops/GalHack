#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// A round thing as built (<see cref="Round"/>): its centre on the map, and each course's outer and inner radius as laid at
/// its foot (inner 0: solid through). One that runs in or out as it rises is laid true to its profile: <see cref="Profile"/>
/// is its outer radius every half metre from the foot of its first course to the top of its last, <see cref="InnerProfile"/>
/// the same inside, and each course's rim is cut by them (<see cref="BlockType.RoundSlope"/>).
/// </summary>
public sealed record RoundBody(double X, double Z, int Y0, double[] Outer, double[] Inner, double[]? Profile = null, double[]? InnerProfile = null)
{
    public int Y1 => Y0 + Outer.Length - 1;
    public bool Sloped => Profile != null;
}

/// <summary>
/// One builder for every round thing: tanks, stacks, chimneys and cooling towers, silos, drums, domes course by course,
/// basins' rims. Each course's round is drawn on the cell grid as a many-sided outline whose corners lie on the grid's lines
/// at half metres (each the nearest to where the circle crosses that line, never two on one line in a row, so it never
/// steps), within a quarter metre of the circle; a radius is eased a little where that holds it nearer. Where the outline
/// crosses a cell the cell is the piece cut that way (<see cref="BlockType.Wedge"/> and the rest, a half where it runs
/// straight through, a quarter round where a small circle bows past the diagonal), so the rim is round from above at every
/// height, the top's edge too. Below ground nothing is cut: a disc laid in the ground meets the soil whole.
/// </summary>
public static class Round
{
    /// <summary>Off for a measurement against the old look (tests, shots): whole cubes wherever a cell's middle is inside.</summary>
    public static bool Cut = true;

    /// <summary>How far an outline may stray from its circle, m.</summary>
    public const double Tolerance = 0.25;

    /// <summary>One cell of a course: whole (Cube) or a piece cut by the outline, and its facing.</summary>
    public readonly record struct Piece(int X, int Z, BlockType Type, Dir Facing);

    /// <summary>A straight run of an outline inside one cell (or along its face), in map metres, with the cell holding its solid side.</summary>
    public readonly record struct Run(double X0, double Z0, double X1, double Z1, int CellX, int CellZ);

    /// <summary>A course as cut: its solid cells, the cells inside its inner round (to be cleared), and its outer outline's runs.</summary>
    public sealed class Course
    {
        public readonly List<Piece> Solid = new();
        public readonly List<(int X, int Z)> Inside = new();
        public readonly List<Run> Rim = new();
    }

    // ---- the outline ----------------------------------------------------------------------------------

    static double Snap(double t) => Math.Floor(t * 2 + 0.5) / 2;

    /// <summary>Whether a straight run from one outline corner to the next stays a piece's cut: a cell at most each way, crossing grid lines only at whole or half metres.</summary>
    static bool Fits((double X, double Z) a, (double X, double Z) b)
    {
        double dx = b.X - a.X, dz = b.Z - a.Z;
        if (Math.Abs(dx) > 1 + 1e-9 || Math.Abs(dz) > 1 + 1e-9) return false;
        if (dx == 0 || dz == 0) return true;
        bool Half(double v) => Math.Abs(v * 2 - Math.Round(v * 2)) < 1e-6;
        for (double x = Math.Floor(Math.Min(a.X, b.X)) + 1; x < Math.Max(a.X, b.X); x++) if (!Half(a.Z + dz * (x - a.X) / dx)) return false;
        for (double z = Math.Floor(Math.Min(a.Z, b.Z)) + 1; z < Math.Max(a.Z, b.Z); z++) if (!Half(a.X + dx * (z - a.Z) / dz)) return false;
        return true;
    }

    /// <summary>
    /// A circle's outline on the grid: where it crosses each whole line, to the nearest half metre, in order round it; where two
    /// corners in a row lie on one line (a step), the one further from the circle goes, while its neighbours still join as a cut.
    /// </summary>
    public static List<(double X, double Z)> Outline(double cx, double cz, double r)
    {
        var set = new HashSet<(double, double)>();
        for (int x = (int)Math.Floor(cx - r); x <= (int)Math.Ceiling(cx + r); x++)
        {
            double d = x - cx;
            if (Math.Abs(d) >= r) continue;
            double h = Math.Sqrt(r * r - d * d);
            set.Add((x, Snap(cz - h))); set.Add((x, Snap(cz + h)));
        }
        for (int z = (int)Math.Floor(cz - r); z <= (int)Math.Ceiling(cz + r); z++)
        {
            double d = z - cz;
            if (Math.Abs(d) >= r) continue;
            double h = Math.Sqrt(r * r - d * d);
            set.Add((Snap(cx - h), z)); set.Add((Snap(cx + h), z));
        }
        var pts = set.OrderBy(p => Math.Atan2(p.Item2 - cz, p.Item1 - cx)).ToList();
        double Off((double X, double Z) p) => Math.Abs(Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Z - cz) * (p.Z - cz)) - r);
        static bool Step((double X, double Z) a, (double X, double Z) b) => a.X == b.X && Math.Abs(a.Z - b.Z) <= 0.5 || a.Z == b.Z && Math.Abs(a.X - b.X) <= 0.5;
        for (bool again = true; again && pts.Count > 4;)
        {
            again = false;
            for (int i = 0; i < pts.Count && !again; i++)
            {
                int n = pts.Count, k = (i + 1) % n;
                if (!Step(pts[i], pts[k]) && pts[i] != pts[k]) continue;
                foreach (int drop in Off(pts[i]) >= Off(pts[k]) ? new[] { i, k } : new[] { k, i })
                {
                    var (p, q) = (pts[(drop - 1 + n) % n], pts[(drop + 1) % n]);
                    if (!Fits(p, q) || Step(p, q)) continue;
                    pts.RemoveAt(drop);
                    again = true;
                    break;
                }
            }
        }
        return pts;
    }

    /// <summary>How far an outline strays from its circle at worst (its corners and along its runs), and whether every run is a piece's cut.</summary>
    public static (double Worst, bool Whole) Stray(List<(double X, double Z)> pts, double cx, double cz, double r)
    {
        double worst = 0;
        bool whole = true;
        for (int i = 0; i < pts.Count; i++)
        {
            var (a, b) = (pts[i], pts[(i + 1) % pts.Count]);
            whole &= Fits(a, b);
            for (int k = 0; k <= 8; k++)
            {
                double t = k / 8.0, x = a.X + (b.X - a.X) * t - cx, z = a.Z + (b.Z - a.Z) * t - cz;
                worst = Math.Max(worst, Math.Abs(Math.Sqrt(x * x + z * z) - r));
            }
        }
        return (worst, whole);
    }

    /// <summary>
    /// The radius a round is laid at: its own where its outline keeps within <see cref="Tolerance"/>, else the nearest within
    /// 0.15 m that does (and that <paramref name="accept"/> takes, as a ladder's flat), else the truest of those.
    /// </summary>
    public static double Fit(double cx, double cz, double r, Func<List<(double X, double Z)>, bool>? accept = null)
    {
        double best = r, least = double.MaxValue;
        for (int k = 0; k <= 6; k++)
            foreach (int s in k == 0 ? new[] { 1 } : new[] { 1, -1 })
            {
                double rr = r + s * 0.025 * k;
                var pts = Outline(cx, cz, rr);
                var (worst, whole) = Stray(pts, cx, cz, rr);
                if (!whole || accept != null && !accept(pts)) continue;
                if (worst <= Tolerance) return rr;
                if (worst < least) (least, best) = (worst, rr);
            }
        return best;
    }

    /// <summary>Whether a round about the middle of cell (cx, cz) runs straight along a grid line across the cell column through its middle on each side (a ladder's, a fin's or a hatch's flat).</summary>
    public static bool Flat(List<(double X, double Z)> pts, int cx, int cz)
    {
        double Pole(Func<(double X, double Z), bool> on, Func<(double X, double Z), double> by, bool max) => max ? pts.Where(on).Select(by).DefaultIfEmpty(double.NaN).Max() : pts.Where(on).Select(by).DefaultIfEmpty(double.NaN).Min();
        bool Whole(double a, double b) => a == b && a == Math.Floor(a);
        return Whole(Pole(q => q.X == cx, q => q.Z, true), Pole(q => q.X == cx + 1, q => q.Z, true)) && Whole(Pole(q => q.X == cx, q => q.Z, false), Pole(q => q.X == cx + 1, q => q.Z, false))
            && Whole(Pole(q => q.Z == cz, q => q.X, true), Pole(q => q.Z == cz + 1, q => q.X, true)) && Whole(Pole(q => q.Z == cz, q => q.X, false), Pole(q => q.Z == cz + 1, q => q.X, false));
    }

    /// <summary>The first cell wholly outside a round of radius r about the middle of cell (cx, cz), out along its axis toward <paramref name="d"/> (against its flat, where it has one).</summary>
    public static (int X, int Z) Past(int cx, int cz, double r, Dir d)
    {
        var pts = Outline(cx + 0.5, cz + 0.5, r);
        IEnumerable<double> Across(bool alongX) => pts.Where(q => alongX ? q.Z == cz || q.Z == cz + 1 : q.X == cx || q.X == cx + 1).Select(q => alongX ? q.X : q.Z);
        return d switch
        {
            Dir.East => ((int)Math.Ceiling(Across(true).Max()), cz),
            Dir.West => ((int)Math.Floor(Across(true).Min()) - 1, cz),
            Dir.South => (cx, (int)Math.Ceiling(Across(false).Max())),
            _ => (cx, (int)Math.Floor(Across(false).Min()) - 1),
        };
    }

    /// <summary>
    /// A way cut through a round thing's wall by whole cells or blocks (a hatch, a mouth): past the cells cut, either way across
    /// the wall, what its rim still has in the way (the cut pieces and halves its outline makes, a cell or two deep) is cleared
    /// too, so the way opens onto the air each side and not onto a sliver of rim. Returns the cells cleared.
    /// </summary>
    public static List<Int3> Through(Canvas c, IEnumerable<Int3> cut, Dir across)
    {
        var set = cut.ToHashSet();
        var cleared = new List<Int3>();
        foreach (Int3 q in set)
            foreach (Int3 step in new[] { across.Vec(), across.Opposite().Vec() })
                for (int k = 1; k <= 2; k++)
                {
                    Int3 n = q + step * k;
                    if (set.Contains(n) || c.Get(n).Type is var t && t != BlockType.Half && !BlockInfo.IsRoundCut(t)) break;
                    c.Clear(n);
                    cleared.Add(n);
                }
        return cleared;
    }

    // ---- a course -------------------------------------------------------------------------------------

    /// <summary>What each piece's plan is, by its corners to the half metre: the key to the piece cut that way.</summary>
    static readonly Dictionary<string, (BlockType, Dir)> Keys = MakeKeys();

    static string Key(IEnumerable<(double X, double Z)> corners) =>
        string.Join(";", corners.Select(c => ((int)Math.Round(c.X * 2), (int)Math.Round(c.Z * 2))).Distinct().OrderBy(c => c).Select(c => $"{c.Item1},{c.Item2}"));

    static Dictionary<string, (BlockType, Dir)> MakeKeys()
    {
        var keys = new Dictionary<string, (BlockType, Dir)>();
        foreach (Dir f in Dirs.Horizontal)
        {
            for (BlockType t = BlockType.Wedge; t <= BlockType.SlantFillM; t++)
                keys[Key(Shapes.Plan(new Block(t, Material.Concrete, default, Role.Wall, f)).Select(v => ((double)v.X, (double)v.Z)))] = (t, f);
            var half = Shapes.Structural(new Block(BlockType.Half, Material.Concrete, default, Role.Wall, f), default, Surface.Matte).First();
            keys[Key(new[] { ((double)half.Lo.X, (double)half.Lo.Z), (half.Hi.X, half.Lo.Z), (half.Hi.X, half.Hi.Z), (half.Lo.X, half.Hi.Z) })] = (BlockType.Half, f);
        }
        return keys;
    }

    /// <summary>A cell's square cut by a straight line, keeping the side <paramref name="keep"/> is on (cell-local corners).</summary>
    static List<(double X, double Z)> Clip(List<(double X, double Z)> poly, (double X, double Z) a, (double X, double Z) b, (double X, double Z) keep)
    {
        double Side((double X, double Z) p) => (b.X - a.X) * (p.Z - a.Z) - (b.Z - a.Z) * (p.X - a.X);
        double want = Math.Sign(Side(keep));
        var outp = new List<(double, double)>();
        for (int i = 0; i < poly.Count; i++)
        {
            var (p, q) = (poly[i], poly[(i + 1) % poly.Count]);
            double sp = Side(p) * want, sq = Side(q) * want;
            if (sp >= -1e-9) outp.Add(p);
            if (sp > 1e-9 && sq < -1e-9 || sp < -1e-9 && sq > 1e-9)
            {
                double t = sp / (sp - sq);
                outp.Add((p.X + (q.X - p.X) * t, p.Z + (q.Z - p.Z) * t));
            }
        }
        return outp;
    }

    static double Area(List<(double X, double Z)> p)
    {
        double a = 0;
        for (int i = 0; i < p.Count; i++) a += p[i].X * p[(i + 1) % p.Count].Z - p[(i + 1) % p.Count].X * p[i].Z;
        return Math.Abs(a) / 2;
    }

    /// <summary>
    /// One course of a round: its outer circle, and, hollow, its inner one (0: solid). A cell the outline crosses is the piece
    /// cut that way; a cell cut by both rounds (a wall thinner than two cells) keeps the outer cut and stays solid inward.
    /// </summary>
    public static Course Ring(double cx, double cz, double outer, double inner = 0)
    {
        var course = new Course();
        if (outer <= 0) return course;
        int x0 = (int)Math.Floor(cx - outer) - 1, x1 = (int)Math.Ceiling(cx + outer) + 1, z0 = (int)Math.Floor(cz - outer) - 1, z1 = (int)Math.Ceiling(cz + outer) + 1;
        double D(double x, double z) => Math.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz));
        if (!Cut || outer < 0.75)
        {
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    double d = D(x + 0.5, z + 0.5);
                    if (d > outer) continue;
                    if (d > inner || inner <= 0) course.Solid.Add(new Piece(x, z, BlockType.Cube, Dir.North));
                    else course.Inside.Add((x, z));
                }
            return course;
        }
        // Each outline's runs, split where they cross a grid line, filed under the cell they cut (or, along a cell's face, under none).
        var cuts = new Dictionary<(int, int), List<((double X, double Z) A, (double X, double Z) B, bool Outer)>>();
        void Trace(double r, bool isOuter)
        {
            var pts = Outline(cx, cz, r);
            for (int i = 0; i < pts.Count; i++)
            {
                var (a, b) = (pts[i], pts[(i + 1) % pts.Count]);
                var ts = new List<double> { 0, 1 };
                double dx = b.X - a.X, dz = b.Z - a.Z;
                if (dx != 0) for (double x = Math.Floor(Math.Min(a.X, b.X)) + 1; x < Math.Max(a.X, b.X); x++) ts.Add((x - a.X) / dx);
                if (dz != 0) for (double z = Math.Floor(Math.Min(a.Z, b.Z)) + 1; z < Math.Max(a.Z, b.Z); z++) ts.Add((z - a.Z) / dz);
                ts.Sort();
                for (int k = 0; k + 1 < ts.Count; k++)
                {
                    if (ts[k + 1] - ts[k] < 1e-9) continue;
                    (double X, double Z) p = (a.X + dx * ts[k], a.Z + dz * ts[k]), q = (a.X + dx * ts[k + 1], a.Z + dz * ts[k + 1]);
                    double mx = (p.X + q.X) / 2, mz = (p.Z + q.Z) / 2;
                    // (The solid side: toward the middle on the outer round, away from it on the inner.)
                    double ix = cx - mx, iz = cz - mz, il = Math.Sqrt(ix * ix + iz * iz);
                    double sx = mx + (isOuter ? 1 : -1) * ix / il * 0.01, sz = mz + (isOuter ? 1 : -1) * iz / il * 0.01;
                    var cell = ((int)Math.Floor(sx), (int)Math.Floor(sz));
                    bool along = p.X == q.X && p.X == Math.Floor(p.X) || p.Z == q.Z && p.Z == Math.Floor(p.Z);
                    if (isOuter) course.Rim.Add(new Run(p.X, p.Z, q.X, q.Z, cell.Item1, cell.Item2));
                    if (along) continue;
                    (cuts.TryGetValue(cell, out var l) ? l : cuts[cell] = new()).Add((p, q, isOuter));
                }
            }
        }
        Trace(outer, true);
        if (inner > 0) Trace(inner, false);
        for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                if (!cuts.TryGetValue((x, z), out var list))
                {
                    double d = D(x + 0.5, z + 0.5);
                    if (d > outer) continue;
                    if (inner > 0 && d < inner) course.Inside.Add((x, z));
                    else course.Solid.Add(new Piece(x, z, BlockType.Cube, Dir.North));
                    continue;
                }
                var poly = new List<(double X, double Z)> { (0, 0), (1, 0), (1, 1), (0, 1) };
                // (Both rounds through one cell: the outer cut only.)
                foreach (var (a, b, isOuter) in list.Any(c => c.Outer) && list.Any(c => !c.Outer) ? list.Where(c => c.Outer) : list)
                {
                    // (The side kept: the middle's side of the cut on the outer round, the other on the inner.)
                    var mid = ((a.X + b.X) / 2 - x, (a.Z + b.Z) / 2 - z);
                    double ox = mid.Item1 + (cx - x - mid.Item1) * (isOuter ? 1 : -1), oz = mid.Item2 + (cz - z - mid.Item2) * (isOuter ? 1 : -1);
                    poly = Clip(poly, (a.X - x, a.Z - z), (b.X - x, b.Z - z), (ox, oz));
                }
                double area = Area(poly);
                if (area < 0.01) { if (inner > 0 && D(x + 0.5, z + 0.5) < inner) course.Inside.Add((x, z)); continue; }
                if (area > 0.99) { course.Solid.Add(new Piece(x, z, BlockType.Cube, Dir.North)); continue; }
                if (!Keys.TryGetValue(Key(poly), out var piece)) { if (area >= 0.5) course.Solid.Add(new Piece(x, z, BlockType.Cube, Dir.North)); continue; }
                var (type, facing) = piece;
                // A small round bows past the diagonal further than a flat cut stands off it: a quarter round there.
                if (type == BlockType.Wedge && list.Count == 1)
                {
                    var (a, b, isOuter) = list[0];
                    double r = isOuter ? outer : inner, bow = r - D((a.X + b.X) / 2, (a.Z + b.Z) / 2);
                    if (bow > 0.146) type = isOuter ? BlockType.RoundOut : BlockType.RoundIn;
                }
                course.Solid.Add(new Piece(x, z, type, facing));
            }
        return course;
    }

    /// <summary>Every round's rim and the cells just in from it, course by course: what no later pass reshapes or dresses.</summary>
    public static HashSet<Int3> Rims(IEnumerable<RoundBody> bodies)
    {
        var rim = new HashSet<Int3>();
        foreach (RoundBody b in bodies)
            for (int y = b.Y0; y <= b.Y1; y++)
            {
                var (oLo, oHi, iLo, iHi) = Span(b, y - b.Y0);
                for (int z = (int)Math.Floor(b.Z - oHi) - 1; z <= (int)Math.Ceiling(b.Z + oHi) + 1; z++)
                    for (int x = (int)Math.Floor(b.X - oHi) - 1; x <= (int)Math.Ceiling(b.X + oHi) + 1; x++)
                    {
                        double d = Math.Sqrt((x + 0.5 - b.X) * (x + 0.5 - b.X) + (z + 0.5 - b.Z) * (z + 0.5 - b.Z));
                        // (No further out than a cell the outline crosses, nor further in on a hollow one: a neighbour's are its own.)
                        if (d > oLo - 2 && d <= oHi + 0.75 || iHi > 0 && d >= iLo - 0.75 && d < iHi + 2) rim.Add(new Int3(x, y, z));
                    }
            }
        return rim;
    }

    /// <summary>The least and most a course's outside and inside radius come to over its height (a sloped round's run in or out within it).</summary>
    public static (double OuterLo, double OuterHi, double InnerLo, double InnerHi) Span(RoundBody b, int k)
    {
        if (b.Profile is not { } po || b.InnerProfile is not { } pi) return (b.Outer[k], b.Outer[k], b.Inner[k], b.Inner[k]);
        static double Lo(double[] a, int i) => Math.Min(a[i], Math.Min(a[i + 1], a[i + 2]));
        static double Hi(double[] a, int i) => Math.Max(a[i], Math.Max(a[i + 1], a[i + 2]));
        return (Lo(po, 2 * k), Hi(po, 2 * k), Lo(pi, 2 * k), Hi(pi, 2 * k));
    }

    // ---- laying ---------------------------------------------------------------------------------------

    /// <summary>
    /// Lays a round from course y0 to y1: its outer and inner radius at any height (inner 0: solid), and each course's block.
    /// Each course is cut at the radii at its foot, each eased to its fit; but a round that runs in or out as it rises is cut
    /// by its profile instead, every half metre up (<see cref="Slope"/>). Each cell is set on the map, but those inside the
    /// inner round, or that <paramref name="hollow"/> keeps open (a drum's rooms, planned by the block), which go to
    /// <paramref name="inside"/>; the body is noted for the checks and the mesher. Courses at or below the ground are laid
    /// whole, as the ground they meet.
    /// </summary>
    public static RoundBody Lay(Canvas c, double cx, double cz, int y0, int y1, Func<double, (double Outer, double Inner)> radius, Func<int, Block> block,
        Func<Int3, bool>? hollow = null, Action<Int3>? inside = null)
    {
        int n = y1 - y0 + 1;
        (double, double) At(double y) { var (o, i) = radius(y); return (Math.Max(0, o), Math.Max(0, i)); }
        bool Ground(int y) => y <= Assembler.GroundY && !c.X.InSpace;
        var outer = new double[n];
        var inner = new double[n];
        for (int k = 0; k < n; k++) (outer[k], inner[k]) = At(y0 + k);
        double[]? po = null, pi = null;
        if (Cut && Enumerable.Range(0, n).Any(k => !Ground(y0 + k) && Math.Abs(At(y0 + k + 1).Item1 - outer[k]) > 0.01))
        {
            (po, pi) = (new double[2 * n + 1], new double[2 * n + 1]);
            for (int h = 0; h <= 2 * n; h++) (po[h], pi[h]) = At(y0 + h / 2.0);
        }
        else
        {
            var fits = new Dictionary<double, double>();
            double Fitted(double r) => r <= 0 ? r : fits.TryGetValue(r, out double f) ? f : fits[r] = Fit(cx, cz, r);
            for (int k = 0; k < n; k++) if (!Ground(y0 + k)) (outer[k], inner[k]) = (Fitted(outer[k]), Fitted(inner[k]));
        }
        var body = new RoundBody(cx, cz, y0, outer, inner, po, pi);
        var made = new Dictionary<(double, double, bool), Course>();
        for (int y = y0; y <= y1; y++)
        {
            double ro = outer[y - y0], ri = inner[y - y0];
            bool ground = Ground(y);
            Course? course;
            if (body.Sloped && !ground) course = Slope(body, y);
            else if (!made.TryGetValue((ro, ri, ground), out course))
            {
                bool was = Cut;
                if (ground) Cut = false;
                try { made[(ro, ri, ground)] = course = Ring(cx, cz, ro, ri); }
                finally { Cut = was; }
            }
            Block b = block(y);
            foreach (Piece p in course.Solid)
            {
                Int3 at = new(p.X, y, p.Z);
                if (hollow != null && hollow(at)) { inside?.Invoke(at); continue; }
                // (A cut never carves what stands already: over a whole block it leaves it, over another cut it fills the cell.)
                Block was = c.Get(at);
                if (p.Type != BlockType.Cube && BlockInfo.IsFullCube(was.Type)) continue;
                bool both = p.Type != BlockType.Cube && BlockInfo.IsRoundCut(was.Type) && (was.Type != p.Type || was.Facing != p.Facing);
                c.Set(at, b with { Type = both ? BlockType.Cube : p.Type, Facing = p.Type == BlockType.Cube || both ? b.Facing : p.Facing });
            }
            if (inside != null) foreach (var (x, z) in course.Inside) inside(new Int3(x, y, z));
        }
        c.X.Rounds.Add(body);
        return body;
    }

    // ---- a sloped round's courses -----------------------------------------------------------------------

    /// <summary>
    /// A face of a sloped round's piece (<see cref="Pieces"/>), cell-local, wound anticlockwise seen from outside: on a side of
    /// its cell (its index in <see cref="Dirs.All"/>), on the round's outside (<see cref="OuterFace"/>) or inside
    /// (<see cref="InnerFace"/>), or a seam between two parts of one piece (<see cref="Seam"/>: within it, never seen).
    /// </summary>
    public readonly record struct SlopeFace(V3[] Pts, int Side);

    public const int OuterFace = 6, InnerFace = 7, Seam = 8;

    /// <summary>How many flats a sloped round has round its axis: about half a metre each at its widest, a multiple of four.</summary>
    public static int Sides(RoundBody b) => 4 * Math.Max(6, (int)Math.Ceiling(2 * Math.PI * b.Profile!.Max() / 0.5 / 4));

    /// <summary>
    /// A course of a sloped round: each cell its profile cuts is a <see cref="BlockType.RoundSlope"/> (a corner of a cube
    /// standing proud of a curve shows from far off, so only a cut of under a tenth of a litre counts as none), the cells of its
    /// wall whole, those of its inside inside.
    /// </summary>
    static Course Slope(RoundBody b, int y)
    {
        var course = new Course();
        var (oLo, oHi, iLo, iHi) = Span(b, y - b.Y0);
        double shrink = Math.Cos(Math.PI / Sides(b));
        for (int z = (int)Math.Floor(b.Z - oHi) - 1; z <= (int)Math.Ceiling(b.Z + oHi); z++)
            for (int x = (int)Math.Floor(b.X - oHi) - 1; x <= (int)Math.Ceiling(b.X + oHi); x++)
            {
                double dx = Math.Max(0, Math.Max(x - b.X, b.X - x - 1)), dz = Math.Max(0, Math.Max(z - b.Z, b.Z - z - 1));
                double near = Math.Sqrt(dx * dx + dz * dz), far = Math.Sqrt(Math.Pow(Math.Max(Math.Abs(x - b.X), Math.Abs(x + 1 - b.X)), 2) + Math.Pow(Math.Max(Math.Abs(z - b.Z), Math.Abs(z + 1 - b.Z)), 2));
                if (near >= oHi) continue;
                if (iLo > 0 && far <= iLo * shrink) { course.Inside.Add((x, z)); continue; }
                if (far <= oLo * shrink && near >= iHi) { course.Solid.Add(new Piece(x, z, BlockType.Cube, Dir.North)); continue; }
                double v = Volume(Pieces(b, new Int3(x, y, z)));
                if (v > 0.99995) course.Solid.Add(new Piece(x, z, BlockType.Cube, Dir.North));
                else if (v >= 0.00005) course.Solid.Add(new Piece(x, z, BlockType.RoundSlope, Dir.North));
                else if (iHi > 0 && Math.Sqrt((x + 0.5 - b.X) * (x + 0.5 - b.X) + (z + 0.5 - b.Z) * (z + 0.5 - b.Z)) < iHi) course.Inside.Add((x, z));
            }
        return course;
    }

    /// <summary>
    /// What a sloped round keeps of a cell: for each of its flats the cell crosses, each half of the cell's height cut by the
    /// flat's outside (and inside) as it runs from the radius at the half's foot to the one at its head, straight between.
    /// The flats are chords of the round, so each part is the cell cut by a few planes; the parts meet on seams.
    /// </summary>
    public static List<SlopeFace> Pieces(RoundBody b, Int3 cell)
    {
        var all = new List<SlopeFace>();
        int k = cell.Y - b.Y0;
        if (b.Profile is not { } po || b.InnerProfile is not { } pi || k < 0 || k >= b.Outer.Length) return all;
        int n = Sides(b);
        double step = 2 * Math.PI / n, chord = Math.Cos(step / 2);
        float ax = (float)(b.X - cell.X), az = (float)(b.Z - cell.Z);
        V3 axis = new(ax, 0, az);
        // The flats the cell's square spans, seen from the axis (all of them if the axis is in it).
        IEnumerable<int> flats;
        if (ax > -0.01f && ax < 1.01f && az > -0.01f && az < 1.01f) flats = Enumerable.Range(0, n);
        else
        {
            double mid = Math.Atan2(0.5 - az, 0.5 - ax), lo = 0, hi = 0;
            foreach (var (x, z) in new[] { (0, 0), (1, 0), (0, 1), (1, 1) })
            {
                double d = Math.IEEERemainder(Math.Atan2(z - az, x - ax) - mid, 2 * Math.PI);
                (lo, hi) = (Math.Min(lo, d), Math.Max(hi, d));
            }
            int first = (int)Math.Floor((mid + lo) / step) - 1, last = (int)Math.Floor((mid + hi) / step) + 1;
            flats = Enumerable.Range(first, last - first + 1).Select(i => ((i % n) + n) % n);
        }
        foreach (int s in flats)
        {
            double a0 = s * step, a1 = a0 + step, am = a0 + step / 2;
            V3 m = new((float)Math.Cos(am), 0, (float)Math.Sin(am));
            for (int half = 0; half < 2; half++)
            {
                float y0 = half * 0.5f;
                double ro0 = po[2 * k + half], ro1 = po[2 * k + half + 1], ri0 = pi[2 * k + half], ri1 = pi[2 * k + half + 1];
                if (ro0 <= 0 && ro1 <= 0) continue;
                var part = Cell(y0, y0 + 0.5f, half == 0 ? Dir.Up : Dir.Down);
                // Between the flat's two edges (planes through the axis), inside its outside, outside its inside.
                V3 e0 = new(-(float)Math.Sin(a0), 0, (float)Math.Cos(a0)), e1 = new(-(float)Math.Sin(a1), 0, (float)Math.Cos(a1));
                part = Plane(part, e0 * -1f, V3.Dot(e0 * -1f, axis), Seam);
                part = Plane(part, e1, V3.Dot(e1, axis), Seam);
                float h0 = (float)(ro0 * chord), slope = (float)((ro1 - ro0) * chord / 0.5);
                part = Plane(part, new V3(m.X, -slope, m.Z), h0 - slope * y0 + V3.Dot(m, axis), OuterFace);
                if (ri0 > 0 || ri1 > 0)
                {
                    float g0 = (float)(ri0 * chord), gs = (float)((ri1 - ri0) * chord / 0.5);
                    part = Plane(part, new V3(-m.X, gs, -m.Z), -g0 + gs * y0 - V3.Dot(m, axis), InnerFace);
                }
                if (part.Count >= 4) all.AddRange(part.Where(f => Area(f.Pts) > 1e-6f));
            }
        }
        return all;
    }

    /// <summary>A cell's box from one height to another, its faces tagged by side; the face at <paramref name="seam"/> is a seam (the cell's other half is over or under it).</summary>
    static List<SlopeFace> Cell(float y0, float y1, Dir seam)
    {
        V3 P(float x, float y, float z) => new(x, y, z);
        var faces = new List<SlopeFace>
        {
            new(new[] { P(0, y0, 0), P(0, y1, 0), P(1, y1, 0), P(1, y0, 0) }, (int)Dir.North),
            new(new[] { P(1, y0, 0), P(1, y1, 0), P(1, y1, 1), P(1, y0, 1) }, (int)Dir.East),
            new(new[] { P(0, y0, 1), P(1, y0, 1), P(1, y1, 1), P(0, y1, 1) }, (int)Dir.South),
            new(new[] { P(0, y0, 0), P(0, y0, 1), P(0, y1, 1), P(0, y1, 0) }, (int)Dir.West),
            new(new[] { P(0, y1, 0), P(0, y1, 1), P(1, y1, 1), P(1, y1, 0) }, seam == Dir.Up ? Seam : (int)Dir.Up),
            new(new[] { P(0, y0, 0), P(1, y0, 0), P(1, y0, 1), P(0, y0, 1) }, seam == Dir.Down ? Seam : (int)Dir.Down),
        };
        return faces;
    }

    /// <summary>A convex solid (its faces) cut by a plane, keeping where nÂ·q â‰¤ d; the cut's own face is tagged <paramref name="side"/>.</summary>
    static List<SlopeFace> Plane(List<SlopeFace> faces, V3 n, float d, int side)
    {
        const float e = 1e-5f;
        if (faces.All(f => f.Pts.All(q => V3.Dot(n, q) - d <= e))) return faces;
        var kept = new List<SlopeFace>(faces.Count + 1);
        var cap = new List<V3>();
        foreach (SlopeFace f in faces)
        {
            var pts = new List<V3>(f.Pts.Length + 2);
            for (int i = 0; i < f.Pts.Length; i++)
            {
                V3 a = f.Pts[i], c = f.Pts[(i + 1) % f.Pts.Length];
                float sa = V3.Dot(n, a) - d, sc = V3.Dot(n, c) - d;
                if (sa <= e) pts.Add(a);
                if (MathF.Abs(sa) <= e) cap.Add(a);
                if (sa < -e && sc > e || sa > e && sc < -e)
                {
                    V3 x = a + (c - a) * (sa / (sa - sc));
                    pts.Add(x);
                    cap.Add(x);
                }
            }
            if (pts.Count >= 3) kept.Add(new SlopeFace(pts.ToArray(), f.Side));
        }
        // The cut face: the points on the plane, in order round their middle, wound to look along n.
        var ring = new List<V3>();
        foreach (V3 q in cap) if (!ring.Any(r => MathF.Abs(r.X - q.X) + MathF.Abs(r.Y - q.Y) + MathF.Abs(r.Z - q.Z) < 1e-4f)) ring.Add(q);
        if (ring.Count >= 3)
        {
            V3 mid = ring.Aggregate(new V3(0, 0, 0), (s, q) => s + q) * (1f / ring.Count), u = (ring[0] - mid).Normalized(), w = V3.Cross(n, u);
            var face = ring.OrderBy(q => MathF.Atan2(V3.Dot(q - mid, w), V3.Dot(q - mid, u))).ToArray();
            if (V3.Dot(V3.Cross(face[1] - face[0], face[2] - face[0]), n) < 0) Array.Reverse(face);
            kept.Add(new SlopeFace(face, side));
        }
        return kept;
    }

    static float Area(V3[] pts)
    {
        V3 s = new(0, 0, 0);
        for (int i = 1; i + 1 < pts.Length; i++) s += V3.Cross(pts[i] - pts[0], pts[i + 1] - pts[0]);
        return MathF.Sqrt(V3.Dot(s, s)) / 2;
    }

    /// <summary>The volume a closed set of faces wound outward holds.</summary>
    public static double Volume(List<SlopeFace> faces)
    {
        double v = 0;
        foreach (SlopeFace f in faces)
            for (int i = 1; i + 1 < f.Pts.Length; i++) v += V3.Dot(f.Pts[0], V3.Cross(f.Pts[i], f.Pts[i + 1])) / 6.0;
        return v;
    }

    /// <summary>The sloped round a cell of a sloped course belongs to, among a place's rounds (null if none).</summary>
    public static RoundBody? BodyAt(IReadOnlyList<RoundBody>? rounds, Int3 cell)
    {
        if (rounds == null) return null;
        foreach (RoundBody b in rounds)
        {
            if (!b.Sloped || cell.Y < b.Y0 || cell.Y > b.Y1) continue;
            var (_, oHi, iLo, _) = Span(b, cell.Y - b.Y0);
            double dx = Math.Max(0, Math.Max(cell.X - b.X, b.X - cell.X - 1)), dz = Math.Max(0, Math.Max(cell.Z - b.Z, b.Z - cell.Z - 1));
            if (dx * dx + dz * dz < oHi * oHi) return b;
        }
        return null;
    }

    /// <summary>Whether a sloped round's piece in a cell takes in the whole face of its cell toward a side (a block beside it shows nothing there).</summary>
    public static bool Fills(RoundBody b, Int3 cell, Dir side) => Pieces(b, cell).Where(f => f.Side == (int)side).Sum(f => Area(f.Pts)) > 0.999f;

    /// <summary>
    /// The way a sloped round's outside (or inside) looks at a point on it, cell-local: out from its axis, and up or down as
    /// the profile runs there, eased from one half metre's slope to the next, so the round shades as one smooth surface (a
    /// flat-by-flat frame read as fluting: each flat took its finish from its own start).
    /// </summary>
    public static V3 Normal(RoundBody b, Int3 cell, V3 q, bool outside)
    {
        double[] p = outside ? b.Profile! : b.InnerProfile!;
        double h = Math.Clamp((cell.Y - b.Y0 + q.Y) * 2, 0, p.Length - 1);
        double Run(int i) => (p[Math.Min(i + 1, p.Length - 1)] - p[Math.Max(i - 1, 0)]) / ((Math.Min(i + 1, p.Length - 1) - Math.Max(i - 1, 0)) / 2.0);
        int i0 = Math.Min((int)h, p.Length - 2);
        double run = Run(i0) + (Run(i0 + 1) - Run(i0)) * (h - i0);
        V3 r = new V3(q.X - (float)(b.X - cell.X), 0, q.Z - (float)(b.Z - cell.Z)).Normalized();
        V3 n = new V3(r.X, -(float)run, r.Z).Normalized();
        return outside ? n : n * -1f;
    }
}
