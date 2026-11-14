#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>A round thing as built (<see cref="Round"/>): its centre on the map, and each course's outer and inner radius as laid (inner 0: solid through).</summary>
public sealed record RoundBody(double X, double Z, int Y0, double[] Outer, double[] Inner)
{
    public int Y1 => Y0 + Outer.Length - 1;
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
                double ro = b.Outer[y - b.Y0], ri = b.Inner[y - b.Y0];
                for (int z = (int)Math.Floor(b.Z - ro) - 1; z <= (int)Math.Ceiling(b.Z + ro) + 1; z++)
                    for (int x = (int)Math.Floor(b.X - ro) - 1; x <= (int)Math.Ceiling(b.X + ro) + 1; x++)
                    {
                        double d = Math.Sqrt((x + 0.5 - b.X) * (x + 0.5 - b.X) + (z + 0.5 - b.Z) * (z + 0.5 - b.Z));
                        // (No further out than a cell the outline crosses, nor further in on a hollow one: a neighbour's are its own.)
                        if (d > ro - 2 && d <= ro + 0.75 || ri > 0 && d >= ri - 0.75 && d < ri + 2) rim.Add(new Int3(x, y, z));
                    }
            }
        return rim;
    }

    // ---- laying ---------------------------------------------------------------------------------------

    /// <summary>
    /// Lays a round from course y0 to y1: each course's outer and inner radius (inner 0: solid), each eased to its fit, and
    /// the block for it; each cell is set on the map, but those inside the inner round, or that <paramref name="hollow"/>
    /// keeps open (a drum's rooms, planned by the block), which go to <paramref name="inside"/>; the body is noted for the
    /// checks. Courses at or below the ground are laid whole, as the ground they meet.
    /// </summary>
    public static RoundBody Lay(Canvas c, double cx, double cz, int y0, int y1, Func<int, (double Outer, double Inner)> radius, Func<int, Block> block,
        Func<Int3, bool>? hollow = null, Action<Int3>? inside = null)
    {
        var outer = new double[y1 - y0 + 1];
        var inner = new double[outer.Length];
        var made = new Dictionary<(double, double), Course>();
        var fits = new Dictionary<double, double>();
        double Fitted(double r) => r <= 0 ? r : fits.TryGetValue(r, out double f) ? f : fits[r] = Fit(cx, cz, r);
        for (int y = y0; y <= y1; y++)
        {
            var (ro, ri) = radius(y);
            bool ground = y <= Assembler.GroundY && !c.X.InSpace;
            if (!ground) (ro, ri) = (Fitted(ro), Fitted(ri));
            outer[y - y0] = ro;
            inner[y - y0] = ri;
            if (!made.TryGetValue((ro, ri), out Course? course))
            {
                bool was = Cut;
                if (ground) Cut = false;
                try { made[(ro, ri)] = course = Ring(cx, cz, ro, ri); }
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
                bool both = p.Type != BlockType.Cube && BlockInfo.IsPlanCut(was.Type) && (was.Type != p.Type || was.Facing != p.Facing);
                c.Set(at, b with { Type = both ? BlockType.Cube : p.Type, Facing = p.Type == BlockType.Cube || both ? b.Facing : p.Facing });
            }
            if (inside != null) foreach (var (x, z) in course.Inside) inside(new Int3(x, y, z));
        }
        var body = new RoundBody(cx, cz, y0, outer, inner);
        c.X.Rounds.Add(body);
        return body;
    }
}
