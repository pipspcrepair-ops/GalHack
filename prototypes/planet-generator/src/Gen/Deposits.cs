using System;
using System.Collections.Generic;
using System.Linq;

namespace Planets.Gen;

/// <summary>
/// What a deposit yields. The first six are the common materials a small spacefaring vessel needs
/// (hull, wiring, electronics, composites, life support and fuel); every planet guarantees them
/// near any starting point. Magnesium to gravium are the uncommon ores, on every planet in small,
/// scattered deposits. The last are only ever an archetype's signature resource.
/// </summary>
public enum DepositKind : byte
{
    Iron, Copper, Silicon, Carbon, Water, Burnstone,
    Magnesium, Cobalt, Neodymium, Gold, Gravium,
    Crystal, Resin,
}

/// <summary>One deposit: a surface outcrop (Depth 0) or a shallow one a few metres down.</summary>
public readonly record struct Deposit(double X, double Y, DepositKind Kind, float Amount, float Depth);

/// <summary>
/// Resource deposits laid out so the early game stays short. The planet is cut into 283 m cells (two
/// 200 m cells' ground: half as many deposits as the first worlds had, which crowded the scanner) and
/// every cell with usable land gets one deposit of each common material, so from any walkable spot
/// the nearest of each is at most a cell diagonal (about 400 m) away, on every archetype. The
/// uncommon ores are smaller and fewer but never absent (design 626): one deposit to each block of
/// a few cells, so they are spread evenly and a walk finds them. Each archetype also scatters its
/// signature resource, in full-sized deposits, without that promise.
/// </summary>
public sealed class DepositField
{
    public const int CommonKinds = 6;
    /// <summary>How much ground each cell's deposits serve: the deposit frequency (halved from 200 m cells).</summary>
    public const double CellSize = 283;
    public const float MaxShallowDepth = 3f;

    /// <summary>A signature resource that is a common material: these cells hold more of it.</summary>
    const float RichCommon = 1.4f;

    /// <summary>
    /// The uncommon ores: one deposit in each block of this many cells a side, at this share of a
    /// common deposit's size. Gravium belongs on high-gravity worlds, but no world has its gravity
    /// yet and a system is one world, so the Capital guarantee (design 627, "it adds the missing
    /// deposit") puts a few on every planet until those worlds exist.
    /// </summary>
    static readonly (DepositKind Kind, int Block, float Size)[] Scarce =
    {
        (DepositKind.Magnesium, 3, 0.6f),
        (DepositKind.Cobalt, 3, 0.6f),
        (DepositKind.Neodymium, 3, 0.6f),
        (DepositKind.Gold, 4, 0.6f),
        (DepositKind.Gravium, 5, 0.5f),
    };

    /// <summary>The kinds every planet holds, in the design's guarantee: the commons and the uncommon ores.</summary>
    public static IEnumerable<DepositKind> EveryPlanet()
    {
        for (int k = 0; k < CommonKinds; k++) yield return (DepositKind)k;
        foreach (var s in Scarce) yield return s.Kind;
    }

    /// <summary>The ores that are smaller and fewer than the commons.</summary>
    public static bool IsScarce(DepositKind kind) => kind >= DepositKind.Magnesium && kind <= DepositKind.Gravium;

    readonly WorldMap _map;
    readonly int _nx, _ny;
    readonly double _cw, _ch;
    readonly List<Deposit>[] _cells;

    public int Count { get; private set; }

    public DepositField(WorldMap map)
    {
        _map = map;
        _nx = Math.Max(1, (int)Math.Round(map.WidthMeters / CellSize));
        _ny = Math.Max(1, (int)Math.Round(map.HeightMeters / CellSize));
        _cw = map.WidthMeters / _nx; // a whole number of cells around the planet, so the grid wraps
        _ch = map.HeightMeters / _ny;
        _cells = new List<Deposit>[_nx * _ny];
        DepositKind signature = map.Def.Signature;
        for (int cj = 0; cj < _ny; cj++)
        for (int ci = 0; ci < _nx; ci++)
        {
            var list = new List<Deposit>(CommonKinds + 2);
            for (int kind = 0; kind < CommonKinds; kind++)
                if (Place(ci, cj, (DepositKind)kind, out Deposit d, (DepositKind)kind == signature && Rich(ci, cj) ? RichCommon : 1f)) list.Add(d);
            // Signature resources: roughly one cell in four.
            if ((int)signature >= CommonKinds && Rich(ci, cj) && Place(ci, cj, signature, out Deposit s)) list.Add(s);
            _cells[cj * _nx + ci] = list;
        }
        foreach (var (kind, block, size) in Scarce) Scatter(kind, block, size);
        foreach (DepositKind kind in EveryPlanet())
            if (!_cells.Any(c => c.Exists(d => d.Kind == kind))) AddMissing(kind);
        Count = _cells.Sum(c => c.Count);
    }

    bool Rich(int ci, int cj) => Hash.Unit(Hash.Of(_map.Spec.Seed, ci, cj, 0x51C), 0) < 0.25f;

    /// <summary>One deposit in each block of cells (none if a signature already put one there): the first cell of the block, in a rolled order, with land for it.</summary>
    void Scatter(DepositKind kind, int block, float size)
    {
        for (int bj = 0; bj < _ny; bj += block)
        for (int bi = 0; bi < _nx; bi += block)
        {
            int w = Math.Min(block, _nx - bi), h = Math.Min(block, _ny - bj), n = w * h;
            bool has = false;
            for (int k = 0; k < n && !has; k++) has = _cells[(bj + k / w) * _nx + bi + k % w].Exists(d => d.Kind == kind);
            if (has) continue;
            int start = (int)(Hash.Of(_map.Spec.Seed, bi, bj, 0x5CA7 + (int)kind) % (ulong)n);
            for (int k = 0; k < n; k++)
            {
                int at = (start + k) % n, ci = bi + at % w, cj = bj + at / w;
                if (!Place(ci, cj, kind, out Deposit d, size)) continue;
                _cells[cj * _nx + ci].Add(d);
                break;
            }
        }
    }

    /// <summary>The generator's guarantee (design 621): a kind the planet came up short of is added, wherever there is land, however little.</summary>
    void AddMissing(DepositKind kind)
    {
        for (int cj = 0; cj < _ny; cj++)
        for (int ci = 0; ci < _nx; ci++)
            if (Place(ci, cj, kind, out Deposit d))
            {
                _cells[cj * _nx + ci].Add(d);
                return;
            }
    }

    /// <summary>Deposits go on ground at least this high where the cell has any (metres above sea level).</summary>
    const float SafeElevation = 3f;

    /// <summary>Usable ground: dry land, out of river channels, gentle enough to stand on.</summary>
    public static bool Usable(WorldMap map, int cell) =>
        map.Flags[cell] is 0 or CellFlags.Riparian or CellFlags.Coast && map.Elevation[cell] >= 1.5f && map.Slope[cell] < 0.6f;

    bool Place(int ci, int cj, DepositKind kind, out Deposit deposit, float size = 1f)
    {
        ulong h = Hash.Of(_map.Spec.Seed, ci, cj, 0xDE90 + (int)kind);
        float amount = Mathx.Lerp(0.6f, 1.6f, Hash.Unit(h, 40)) * size;
        float depth = Hash.Unit(h, 41) < 0.6f ? 0f : Mathx.Lerp(0.5f, MaxShallowDepth, Hash.Unit(h, 42));

        // Try jittered spots, near the middle of the cell first so neighbouring cells stay close,
        // on ground a little above the waterline first (the fine terrain's last metre or so of
        // detail can dip a spot right at the shore under water).
        for (int pass = 0; pass < 2; pass++)
        for (int attempt = 0; attempt < 16; attempt++)
        {
            double spread = attempt < 8 ? 0.5 : 0.9;
            double x = (ci + 0.5 + (Hash.Unit(h, attempt * 2) - 0.5) * spread) * _cw;
            double y = (cj + 0.5 + (Hash.Unit(h, attempt * 2 + 1) - 0.5) * spread) * _ch;
            int cell = _map.CellAt(x, y);
            if (!Usable(_map, cell) || (pass == 0 && _map.Elevation[cell] < SafeElevation)) continue;
            deposit = new Deposit(x, y, kind, amount, depth);
            return true;
        }

        // Small islands and narrow valleys: take the usable map cell nearest the middle, if any.
        // Every map cell that overlaps this deposit cell counts, at the nearest point of it inside.
        double best = double.MaxValue, bx = 0, by = 0;
        double cx = (ci + 0.5) * _cw, cy = (cj + 0.5) * _ch;
        double x0 = ci * _cw, x1 = (ci + 1) * _cw, y0 = cj * _ch, y1 = (cj + 1) * _ch;
        int mi0 = (int)Math.Floor(x0 / _map.CellSize), mi1 = (int)Math.Floor((x1 - 1e-6) / _map.CellSize);
        int mj0 = (int)Math.Floor(y0 / _map.CellSize), mj1 = (int)Math.Floor((y1 - 1e-6) / _map.CellSize);
        for (int pass = 0; pass < 2 && best == double.MaxValue; pass++)
        for (int mj = mj0; mj <= mj1; mj++)
        for (int mi = mi0; mi <= mi1; mi++)
        {
            int cell = _map.Index(mi, mj);
            if (!Usable(_map, cell) || (pass == 0 && _map.Elevation[cell] < SafeElevation)) continue;
            double x = Math.Clamp(_map.CellX(mi), x0 + 0.5, x1 - 0.5), y = Math.Clamp(_map.CellY(mj), y0 + 0.5, y1 - 0.5);
            double d = (x - cx) * (x - cx) + (y - cy) * (y - cy);
            if (d < best)
            {
                best = d;
                bx = x;
                by = y;
            }
        }
        deposit = new Deposit(bx, by, kind, amount, depth);
        return best < double.MaxValue;
    }

    /// <summary>All deposits whose cells overlap a rectangle in metres (either axis may run past a seam).</summary>
    public IEnumerable<Deposit> InRect(double x0, double y0, double x1, double y1)
    {
        int i0 = (int)Math.Floor(x0 / _cw) - 1, i1 = (int)Math.Floor(x1 / _cw) + 1;
        int j0 = (int)Math.Floor(y0 / _ch) - 1, j1 = (int)Math.Floor(y1 / _ch) + 1;
        for (int j = j0; j <= j1; j++)
        for (int i = i0; i <= i1; i++)
            foreach (Deposit d in _cells[WrapJ(j) * _nx + Wrap(i)])
                yield return d;
    }

    /// <summary>Distance in metres to the nearest deposit of a kind, or infinity if the planet has none.</summary>
    public double Nearest(DepositKind kind, double x, double y, double giveUp = 3000)
    {
        int ci = (int)Math.Floor(Mathx.WrapX(x, _map.WidthMeters) / _cw), cj = (int)Math.Floor(Mathx.Wrap(y, _map.HeightMeters) / _ch);
        double best = double.PositiveInfinity;
        for (int ring = 0; ring * Math.Min(_cw, _ch) < Math.Min(best, giveUp) + Math.Max(_cw, _ch); ring++)
        {
            for (int j = cj - ring; j <= cj + ring; j++)
            {
                for (int i = ci - ring; i <= ci + ring; i++)
                {
                    if (Math.Max(Math.Abs(i - ci), Math.Abs(j - cj)) != ring) continue;
                    foreach (Deposit d in _cells[WrapJ(j) * _nx + Wrap(i)])
                    {
                        if (d.Kind != kind) continue;
                        double dx = Mathx.WrapDelta(d.X - x, _map.WidthMeters), dy = Mathx.WrapDelta(d.Y - y, _map.HeightMeters);
                        best = Math.Min(best, Math.Sqrt(dx * dx + dy * dy));
                    }
                }
            }
            if (ring > _nx + _ny) break;
        }
        return best;
    }

    public IEnumerable<Deposit> All()
    {
        foreach (List<Deposit> list in _cells)
            foreach (Deposit d in list)
                yield return d;
    }

    int Wrap(int i) => (i % _nx + _nx) % _nx;
    int WrapJ(int j) => (j % _ny + _ny) % _ny;
}
