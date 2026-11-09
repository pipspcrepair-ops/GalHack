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
/// How an ore is laid down on a world: one deposit to every <see cref="Every"/> base cells of ground
/// (200 m squares), each <see cref="Size"/> times the base amount (its ore body grows with it), and,
/// for the first ores, one promised within <see cref="Start"/> metres of the start (0: no promise).
/// </summary>
public readonly record struct OreRule(float Every, float Size, float Start = 0f);

/// <summary>
/// Resource deposits laid out so the early game stays short, but each a find: few, and big. Each ore
/// has its own grid of cells, sized by its line in <see cref="Ores"/> (the tuning table) and the world
/// type's <see cref="ArchetypeDef.DepositCount"/>; every cell with usable land gets one deposit of it,
/// <see cref="OreRule.Size"/> times the base amount times the world type's
/// <see cref="ArchetypeDef.DepositSize"/>. So from any walkable spot the nearest of each common ore is
/// at most its cell's diagonal away (about 490 m), the first ores are promised near the start, and the
/// uncommon ores are fewer still but never absent (design 626). Each world type's signature resource
/// is scattered on top, without the promise.
/// </summary>
public sealed class DepositField
{
    public const int CommonKinds = 6;
    public const float MaxShallowDepth = 3f;

    /// <summary>The ground the table's counts are measured in, and the cells deposits are found by (m).</summary>
    public const double BaseCell = 200;

    /// <summary>How near the start the first ores are promised (m).</summary>
    public const float StartReach = 200f;

    /// <summary>
    /// The tuning table: each ore's count (one deposit to every so many base cells) and size (times the
    /// base amount: a body's radius is 1.4 m + 1.3 m an amount, its ore about the cube of that). The first
    /// worlds had one common deposit to every cell at size 1, and the uncommon ores one to every 9, 9, 9,
    /// 16 and 25 cells at 0.6, 0.6, 0.6, 0.6 and 0.5: too many and too small. Now a third as many, each
    /// about three times the ore, so a world holds about as much and the scanner a third of the marks.
    /// </summary>
    public static readonly IReadOnlyDictionary<DepositKind, OreRule> Ores = new Dictionary<DepositKind, OreRule>
    {
        [DepositKind.Iron] = new(3f, 1.9f, StartReach),
        [DepositKind.Copper] = new(3f, 1.9f, StartReach),
        [DepositKind.Silicon] = new(3f, 1.9f, StartReach),
        [DepositKind.Carbon] = new(3f, 1.9f, StartReach),
        [DepositKind.Water] = new(3f, 1.9f),
        [DepositKind.Burnstone] = new(3f, 1.9f),
        [DepositKind.Magnesium] = new(27f, 1.3f),
        [DepositKind.Cobalt] = new(27f, 1.3f),
        [DepositKind.Neodymium] = new(27f, 1.3f),
        [DepositKind.Gold] = new(48f, 1.3f),
        [DepositKind.Gravium] = new(75f, 1.1f),
        // Only ever a world's signature: where it is one, this often and this big.
        [DepositKind.Crystal] = new(12f, 1.9f),
        [DepositKind.Resin] = new(12f, 1.9f),
    };

    /// <summary>A world's signature resource when it's an uncommon or signature-only ore: one to every so many base cells, on top of its own line.</summary>
    const float SignatureEvery = 12f;

    /// <summary>A signature resource that is a common material: a quarter of its deposits hold more of it.</summary>
    const float RichCommon = 1.4f;

    /// <summary>The kinds every planet holds, in the design's guarantee: the commons and the uncommon ores.</summary>
    public static IEnumerable<DepositKind> EveryPlanet()
    {
        for (int k = 0; k <= (int)DepositKind.Gravium; k++) yield return (DepositKind)k;
    }

    /// <summary>The ores that are smaller and fewer than the commons.</summary>
    public static bool IsScarce(DepositKind kind) => kind >= DepositKind.Magnesium && kind <= DepositKind.Gravium;

    /// <summary>The largest ore body any deposit can have (m): how far round a chunk to look for one that reaches into it.</summary>
    public static float MaxBodyRadius => 1.4f + 1.3f * 1.6f * Ores.Values.Max(r => r.Size) * RichCommon * 1.5f;

    readonly WorldMap _map;
    readonly int _nx, _ny;
    readonly double _cw, _ch;
    readonly List<Deposit>[] _cells;

    public int Count { get; private set; }

    public DepositField(WorldMap map)
    {
        _map = map;
        // The index deposits are found by: base cells, a whole number of them round the planet so it wraps.
        _nx = Math.Max(1, (int)Math.Round(map.WidthMeters / BaseCell));
        _ny = Math.Max(1, (int)Math.Round(map.HeightMeters / BaseCell));
        _cw = map.WidthMeters / _nx;
        _ch = map.HeightMeters / _ny;
        _cells = new List<Deposit>[_nx * _ny];
        for (int k = 0; k < _cells.Length; k++) _cells[k] = new List<Deposit>(4);
        ArchetypeDef def = map.Def;
        DepositKind signature = def.Signature;
        foreach (DepositKind kind in EveryPlanet()) Lay(kind, Ores[kind].Every / def.DepositCount, Ores[kind].Size * def.DepositSize, kind == signature);
        if ((int)signature > (int)DepositKind.Burnstone) Lay(signature, SignatureEvery / def.DepositCount, Ores[signature].Size * def.DepositSize, false, 0x516);
        foreach (DepositKind kind in EveryPlanet())
            if (!_cells.Any(c => c.Exists(d => d.Kind == kind))) AddMissing(kind);
        Promise(Landing.Find(map, map.WidthMeters * 0.5, map.HeightMeters * 0.5));
        Count = _cells.Sum(c => c.Count);
    }

    /// <summary>One ore over the whole planet: its own grid of cells, a deposit in each with land for it.</summary>
    void Lay(DepositKind kind, float every, float size, bool rich, int salt = 0)
    {
        double side = BaseCell * Math.Sqrt(Math.Max(0.25f, every));
        int nx = Math.Max(1, (int)Math.Round(_map.WidthMeters / side)), ny = Math.Max(1, (int)Math.Round(_map.HeightMeters / side));
        double cw = _map.WidthMeters / nx, ch = _map.HeightMeters / ny;
        for (int cj = 0; cj < ny; cj++)
        for (int ci = 0; ci < nx; ci++)
        {
            bool more = rich && (int)kind < CommonKinds && Hash.Unit(Hash.Of(_map.Spec.Seed, ci, cj, 0x51C + salt), 0) < 0.25f;
            if (Place(ci * cw, cj * ch, cw, ch, Hash.Of(_map.Spec.Seed, ci, cj, 0xDE90 + (int)kind + salt), kind, out Deposit d, more ? size * RichCommon : size)) Add(d);
        }
    }

    void Add(Deposit d)
    {
        int i = Math.Clamp((int)Math.Floor(d.X / _cw), 0, _nx - 1), j = Math.Clamp((int)Math.Floor(d.Y / _ch), 0, _ny - 1);
        _cells[j * _nx + i].Add(d);
    }

    /// <summary>The generator's guarantee (design 621): a kind the planet came up short of is added, wherever there is land, however little.</summary>
    void AddMissing(DepositKind kind)
    {
        for (int cj = 0; cj < _ny; cj++)
        for (int ci = 0; ci < _nx; ci++)
            if (Place(ci * _cw, cj * _ch, _cw, _ch, Hash.Of(_map.Spec.Seed, ci, cj, 0xADD + (int)kind), kind, out Deposit d, Ores[kind].Size * _map.Def.DepositSize))
            {
                Add(d);
                return;
            }
    }

    /// <summary>
    /// The first ores near the start (the landing the start is found from): any not already within its
    /// promise gets a deposit of its own, on usable land as near the site as a ring round it allows.
    /// </summary>
    void Promise(LandingSite site)
    {
        foreach (var (kind, rule) in Ores)
        {
            if (rule.Start <= 0f || Nearest(kind, site.X, site.Y, rule.Start * 2) <= rule.Start) continue;
            ulong h = Hash.Of(_map.Spec.Seed, (int)kind, 0, 0x57A27);
            for (int k = 0; k < 24; k++)
            {
                double a = (Hash.Unit(h, k) + k / 24.0) * Math.Tau, r = rule.Start * (0.4 + 0.5 * k / 24.0);
                double x = Mathx.WrapX(site.X + Math.Cos(a) * r, _map.WidthMeters), y = Mathx.Wrap(site.Y + Math.Sin(a) * r, _map.HeightMeters);
                if (Place(x - 20, y - 20, 40, 40, Hash.Of(h, k), kind, out Deposit d, rule.Size * _map.Def.DepositSize)) { Add(d); break; }
            }
        }
    }

    /// <summary>Deposits go on ground at least this high where the cell has any (metres above sea level).</summary>
    const float SafeElevation = 3f;

    /// <summary>Usable ground: dry land, out of river channels, gentle enough to stand on.</summary>
    public static bool Usable(WorldMap map, int cell) =>
        map.Flags[cell] is 0 or CellFlags.Riparian or CellFlags.Coast && map.Elevation[cell] >= 1.5f && map.Slope[cell] < 0.6f;

    /// <summary>
    /// Where ice may lie (the owner's rule): anywhere on an ice world (and, when they exist, on moons and
    /// asteroids, which have no biome map: they pass <see cref="IceAnywhere"/>); on other worlds only in the ice
    /// caps, the cold, snow and glacier land; never on arid or lava worlds.
    /// </summary>
    public static bool IceAllowed(WorldMap map, int cell)
    {
        Archetype a = map.Spec.Archetype;
        if (a == Archetype.Ice) return true;
        if (a is Archetype.Arid or Archetype.Lava) return false;
        return IceCap((Biome)map.Biome[cell]);
    }

    /// <summary>The biomes that make an ice cap: snow and glacier land and the cold ground round it.</summary>
    public static bool IceCap(Biome b) => b is Biome.Snow or Biome.Glacier or Biome.Snowfield or Biome.BlueIce or Biome.IceTundra or Biome.Tundra;

    /// <summary>Moons and asteroids: no biomes, ice anywhere (no such world type holds deposits yet; this is the rule for when one does).</summary>
    public const bool IceAnywhere = true;

    bool Allowed(DepositKind kind, int cell) => kind != DepositKind.Water || IceAllowed(_map, cell);

    /// <summary>A deposit of a kind somewhere in a cell (its corner, width and height in metres), on usable land, or false if the cell has none.</summary>
    bool Place(double x0, double y0, double w, double hgt, ulong h, DepositKind kind, out Deposit deposit, float size = 1f)
    {
        float amount = Mathx.Lerp(0.6f, 1.6f, Hash.Unit(h, 40)) * size;
        float depth = Hash.Unit(h, 41) < 0.6f ? 0f : Mathx.Lerp(0.5f, MaxShallowDepth, Hash.Unit(h, 42));

        // Try jittered spots, near the middle of the cell first so neighbouring cells stay close,
        // on ground a little above the waterline first (the fine terrain's last metre or so of
        // detail can dip a spot right at the shore under water).
        for (int pass = 0; pass < 2; pass++)
        for (int attempt = 0; attempt < 16; attempt++)
        {
            double spread = attempt < 8 ? 0.5 : 0.9;
            double x = Mathx.WrapX(x0 + (0.5 + (Hash.Unit(h, attempt * 2) - 0.5) * spread) * w, _map.WidthMeters);
            double y = Mathx.Wrap(y0 + (0.5 + (Hash.Unit(h, attempt * 2 + 1) - 0.5) * spread) * hgt, _map.HeightMeters);
            int cell = _map.CellAt(x, y);
            if (!Usable(_map, cell) || (pass == 0 && _map.Elevation[cell] < SafeElevation) || !Allowed(kind, cell)) continue;
            deposit = new Deposit(x, y, kind, amount, depth);
            return true;
        }

        // Small islands and narrow valleys: take the usable map cell nearest the middle, if any.
        // Every map cell that overlaps this deposit cell counts, at the nearest point of it inside.
        double best = double.MaxValue, bx = 0, by = 0;
        double cx = x0 + w * 0.5, cy = y0 + hgt * 0.5, x1 = x0 + w, y1 = y0 + hgt;
        int mi0 = (int)Math.Floor(x0 / _map.CellSize), mi1 = (int)Math.Floor((x1 - 1e-6) / _map.CellSize);
        int mj0 = (int)Math.Floor(y0 / _map.CellSize), mj1 = (int)Math.Floor((y1 - 1e-6) / _map.CellSize);
        for (int pass = 0; pass < 2 && best == double.MaxValue; pass++)
        for (int mj = mj0; mj <= mj1; mj++)
        for (int mi = mi0; mi <= mi1; mi++)
        {
            int cell = _map.Index(mi, mj);
            if (!Usable(_map, cell) || (pass == 0 && _map.Elevation[cell] < SafeElevation) || !Allowed(kind, cell)) continue;
            double x = Math.Clamp(_map.CellX(mi), x0 + 0.5, x1 - 0.5), y = Math.Clamp(_map.CellY(mj), y0 + 0.5, y1 - 0.5);
            double d = (x - cx) * (x - cx) + (y - cy) * (y - cy);
            if (d < best)
            {
                best = d;
                bx = Mathx.WrapX(x, _map.WidthMeters);
                by = Mathx.Wrap(y, _map.HeightMeters);
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
