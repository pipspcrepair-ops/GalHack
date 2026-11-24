#nullable enable
using System;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// Rockets and missiles, every one by the same rule: a round hull about a cell's middle (<see cref="Round"/>) run flat across
/// its axes, a nose over it cut to an ogive's profile, engine bells under it flaring down to the ground, and fins at its foot on
/// the flats, swept back up to the hull at 45 degrees. The caller lays the hull (a lander's decks, a missile's solid body) and
/// adds the parts it has.
/// </summary>
public static class Rockets
{
    /// <summary>A hull's radius about the middle of cell (cx, cz): the nearest to <paramref name="r"/> whose outline runs flat across each axis, for a fin's root or a hatch.</summary>
    public static double Fit(int cx, int cz, double r) => Round.Fit(cx + 0.5, cz + 0.5, r, pts => Round.Flat(pts, cx, cz));

    /// <summary>The nose on a hull of radius R whose top course is y0 - 1: a tangent ogive as long as the hull is wide, solid, cut to its profile. Returns its top course.</summary>
    public static int Nose(Canvas c, int cx, int cz, double R, int y0, Block b)
    {
        double len = 2 * R, rho = (R * R + len * len) / (2 * R);
        int y1 = y0 + (int)Math.Ceiling(len) - 1;
        Round.Lay(c, cx + 0.5, cz + 0.5, y0, y1, y => (Math.Sqrt(rho * rho - Math.Pow(Math.Clamp(y - y0, 0, len), 2)) + R - rho, 0), _ => b);
        return y1;
    }

    /// <summary>
    /// The engine bells under a hull of radius R, from course y0 up to its foot: four round the axis on the diagonals, each on a
    /// cell corner with a clear cell between it and the next and within the hull's round, or, where four would be too small, one
    /// under the middle; each a thin bell flaring from its throat at the hull to its mouth, twice as wide, at the bottom.
    /// </summary>
    public static void Bells(Canvas c, int cx, int cz, double R, int y0, int foot, Block b)
    {
        double Mouth(double o) => Math.Min(o - 0.5, R - o * Math.Sqrt(2));
        double o = new[] { 1.5, 2.5, 3.5 }.MaxBy(Mouth), mouth = Mouth(o);
        var at = mouth >= 0.9 ? new[] { (-o, -o), (o, -o), (-o, o), (o, o) } : new[] { (0.0, 0.0) };
        if (mouth < 0.9) mouth = 0.55 * R;
        foreach (var (dx, dz) in at)
            Round.Lay(c, cx + 0.5 + dx, cz + 0.5 + dz, y0, foot - 1, y =>
            {
                double r = mouth * (0.5 + 0.5 * Math.Pow(Math.Clamp((foot - y) / (foot - y0), 0, 1), 1.6));
                return (r, r - 0.25);
            }, _ => b);
    }

    /// <summary>
    /// A fin on a hull of radius R, on its flat toward <paramref name="d"/>: a plate a cell thick standing on course y0, out from
    /// the hull nine tenths of its radius and up it a little over twice that, its outer edge swept back up to the hull at 45
    /// degrees, each cell the edge cuts a ramp rising toward the hull. Returns its outermost cell at the foot.
    /// </summary>
    public static Int3 Fin(Canvas c, int cx, int cz, double R, Dir d, int y0, Block b)
    {
        var (x, z) = Round.Past(cx, cz, R, d);
        int span = Math.Max(1, (int)Math.Round(0.9 * R)), rise = (int)Math.Round(2.1 * R);
        for (int k = 0; k < span; k++)
            for (int y = 0; k + y < rise; y++)
                c.Set(new Int3(x, y0 + y, z) + d.Vec() * k, k + y + 1 < rise ? b : b with { Type = BlockType.Ramp, Facing = d.Opposite() });
        return new Int3(x, y0, z) + d.Vec() * (span - 1);
    }
}
