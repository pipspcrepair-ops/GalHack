using System;
using System.Collections.Generic;

namespace Planets.Gen;

/// <summary>
/// What a deposit yields. The first five are the common materials a small spacefaring vessel needs
/// (hull, wiring, electronics, composites and fuel or life support); every planet guarantees them
/// near any starting point. The rest are each archetype's signature resource.
/// </summary>
public enum DepositKind : byte
{
    Iron, Copper, Silicon, Carbon, Water,
    Titanium, Cobalt, Sulfur, Thorium, Crystal, Resin,
}

/// <summary>One deposit: a surface outcrop (Depth 0) or a shallow one a few metres down.</summary>
public readonly record struct Deposit(double X, double Y, DepositKind Kind, float Amount, float Depth);

/// <summary>
/// Resource deposits laid out so the early game stays short. The planet is cut into 200 m cells and
/// every cell with usable land gets one deposit of each common material, so from any walkable spot
/// the nearest of each is at most a cell diagonal (about 280 m) away, on every archetype. Each
/// archetype also scatters its signature resource, more sparsely and without that promise.
/// </summary>
public sealed class DepositField
{
    public const int CommonKinds = 5;
    public const double CellSize = 200;
    public const float MaxShallowDepth = 3f;

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
            var list = new List<Deposit>(CommonKinds + 1);
            for (int kind = 0; kind < CommonKinds; kind++)
                if (Place(ci, cj, (DepositKind)kind, out Deposit d)) list.Add(d);
            // Signature resources: roughly one cell in four.
            if (Hash.Unit(Hash.Of(map.Spec.Seed, ci, cj, 0x51C), 0) < 0.25f && Place(ci, cj, signature, out Deposit s)) list.Add(s);
            _cells[cj * _nx + ci] = list;
            Count += list.Count;
        }
    }

    /// <summary>Usable ground: dry land, out of river channels, gentle enough to stand on.</summary>
    public static bool Usable(WorldMap map, int cell) =>
        map.Flags[cell] is 0 or CellFlags.Riparian or CellFlags.Coast && map.Elevation[cell] >= 1.5f && map.Slope[cell] < 0.6f;

    bool Place(int ci, int cj, DepositKind kind, out Deposit deposit)
    {
        ulong h = Hash.Of(_map.Spec.Seed, ci, cj, 0xDE90 + (int)kind);
        float amount = Mathx.Lerp(0.6f, 1.6f, Hash.Unit(h, 40));
        float depth = Hash.Unit(h, 41) < 0.6f ? 0f : Mathx.Lerp(0.5f, MaxShallowDepth, Hash.Unit(h, 42));

        // Try jittered spots, near the middle of the cell first so neighbouring cells stay close.
        for (int attempt = 0; attempt < 16; attempt++)
        {
            double spread = attempt < 8 ? 0.5 : 0.9;
            double x = (ci + 0.5 + (Hash.Unit(h, attempt * 2) - 0.5) * spread) * _cw;
            double y = (cj + 0.5 + (Hash.Unit(h, attempt * 2 + 1) - 0.5) * spread) * _ch;
            if (!Usable(_map, _map.CellAt(x, y))) continue;
            deposit = new Deposit(x, y, kind, amount, depth);
            return true;
        }

        // Small islands and narrow valleys: take the usable map cell nearest the middle, if any.
        double best = double.MaxValue, bx = 0, by = 0;
        double cx = (ci + 0.5) * _cw, cy = (cj + 0.5) * _ch;
        for (double y = cj * _ch + _map.CellSize / 2; y < (cj + 1) * _ch; y += _map.CellSize)
        for (double x = ci * _cw + _map.CellSize / 2; x < (ci + 1) * _cw; x += _map.CellSize)
        {
            if (!Usable(_map, _map.CellAt(x, y))) continue;
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
