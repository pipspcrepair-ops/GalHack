#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// What a building leaves on the floor when part of it fails: not cubes, but the pieces of what came down.
/// A rubble block's <see cref="Block.Variant"/> says which kind of pile it is and its material and colour say
/// what it broke off: a slab of floor or roof cracked across and fallen at an angle with its bars showing, chunks
/// of wall, a heap where a lot came down at once, the ceiling's tiles and plaster dust, timbers, glass. Variant 0
/// is the old loose pile: lumps of grain, resin or cloth; for anything hard it is read as chunks or a slab.
/// Every piece is drawn resting on the cell's floor or on a piece under it, and inside the cell.
/// </summary>
public static class Debris
{
    public const byte Lumps = 0, Slab = 1, Chunks = 2, Heap = 3, Tiles = 4, Timber = 5, Glass = 6, Count = 7;

    public static readonly Rgb Rust = new(0x6e, 0x3c, 0x22);
    static readonly Rgb Core = new(0x8c, 0x88, 0x80), Dust = new(0xb8, 0xb2, 0xa6), Tile = new(0xcc, 0xc8, 0xbc), Wood = new(0x6a, 0x54, 0x3c);

    /// <summary>A rubble block of what fell.</summary>
    public static Block Of(Block fallen, byte kind) => new(BlockType.Rubble, MaterialOf(fallen), ColourOf(fallen, kind), Role.Debris, Dirs.Horizontal[(fallen.Color.R + fallen.Color.B) & 3], kind);

    /// <summary>A rubble block of a material and colour.</summary>
    public static Block Of(Material m, Rgb color, byte kind, Dir facing = Dir.North) => new(BlockType.Rubble, m, color, Role.Debris, facing, kind);

    /// <summary>The kind of pile a block makes when it comes down: slabs from floors and roofs, timber, glass, chunks of the rest.</summary>
    public static byte KindOf(Block fallen) => fallen.Material switch
    {
        Material.Glass => Glass,
        Material.Timber => Timber,
        _ => fallen.Role is Role.Roof or Role.Floor ? Slab : Chunks,
    };

    static Material MaterialOf(Block b) => b.Material switch
    {
        Material.Lumen or Material.DeadLumen or Material.Glyph => Material.Glass,
        Material.Plant or Material.Cloth or Material.Ash or Material.Water => Material.Rock,
        var m => m,
    };

    static Rgb ColourOf(Block b, byte kind)
    {
        if (kind == Glass) return new Rgb(0x6a, 0x82, 0x8a);
        // A pane of a lamp or a sign is glass under its paint.
        if (b.Material is Material.Lumen or Material.DeadLumen) return new Rgb(0x5a, 0x66, 0x6a);
        return b.Color;
    }

    /// <summary>Whether a block is drawn as plain lumps (the loose piles of soft stuff); anything hard never is.</summary>
    public static bool IsLumps(Block b) => b.Type == BlockType.Rubble && b.Variant == Lumps && Soft(b.Material);

    static bool Soft(Material m) => m is Material.Plant or Material.Resin or Material.Cloth or Material.Bloom or Material.Flesh or Material.Chitin or Material.Bone or Material.Soil;

    /// <summary>The kind a rubble block is drawn as: its own, or for an old hard pile, chunks or a slab by its cell.</summary>
    public static byte Look(Block b, Int3 p)
    {
        if (b.Variant is > Lumps and < Count) return b.Variant;
        if (Soft(b.Material)) return Lumps;
        if (b.Material == Material.Glass) return Glass;
        if (b.Material == Material.Timber) return Timber;
        return Hash.Cell(p, 131) % 3 == 0 ? Slab : Chunks;
    }

    /// <summary>How high it stands: what a body bumps into.</summary>
    public static float Top(byte kind) => kind switch
    {
        Heap => 0.62f,
        Slab => 0.42f,
        Tiles => 0.06f,
        Glass => 0.05f,
        Timber => 0.3f,
        Chunks => 0.3f,
        _ => 0.45f,
    };

    // ---- the pieces, for the mesher ----------------------------------------------------------

    /// <summary>The boxes a rubble block is drawn with, in cell units, each resting on the floor or a piece under it.</summary>
    public static void Shape(List<ShapeBox> l, Block b, Int3 p)
    {
        var r = new Pick(Hash.Cell(p, 151) ^ (ulong)b.Variant << 40);
        Rgb c = b.Color;
        Surface s = b.Material == Material.Glass ? Surface.Glass : Shapes.SurfaceOf(b.Material) == Surface.Metal ? Surface.Metal : Surface.Matte;
        switch (Look(b, p))
        {
            case Slab: SlabPiece(l, r, c, s, b.Material); break;
            case Chunks: ChunkPile(l, r, c, s, 0.18f, 3 + r.Int(3)); break;
            case Heap: HeapPile(l, r, c, s, b.Material); break;
            case Tiles: TileFall(l, r, c); break;
            case Timber: Timbers(l, r, c); break;
            case Glass: Shards(l, r, c); break;
            default:
                for (int i = 0; i < 4; i++)
                {
                    float sz = 0.25f + r.F() * 0.3f, x = r.F() * (1 - sz), z = r.F() * (1 - sz);
                    l.Add(new ShapeBox(new V3(x, 0, z), new V3(x + sz, sz * 0.8f, z + sz), c.Scale(0.8f + r.F() * 0.3f), Surface.Matte));
                }
                break;
        }
    }

    /// <summary>Hard stuff breaks to a pale core: concrete shows its grey aggregate, brick its paler body.</summary>
    static Rgb Broken(Rgb c, Pick r, Material m) => m is Material.Concrete or Material.Rock or Material.Basalt or Material.Ceramic ? c.Lerp(Core, 0.25f + r.F() * 0.3f) : c.Scale(0.85f + r.F() * 0.2f);

    static bool Reinforced(Material m) => m is Material.Concrete or Material.Rock or Material.Basalt or Material.Ceramic or Material.Armour or Material.Fused;

    /// <summary>A slab of floor or roof cracked off, one end on the floor and the other up on a chunk, bars bent out of its broken edge.</summary>
    static void SlabPiece(List<ShapeBox> l, Pick r, Rgb c, Surface s, Material m)
    {
        float len = 0.62f + r.F() * 0.22f, wide = 0.42f + r.F() * 0.2f, thick = 0.09f + r.F() * 0.05f;
        float yaw = r.F() * MathF.Tau, pitch = 0.2f + r.F() * 0.42f;
        // The chunk it fell on, then the slab leaning on it.
        Piece(l, new V3(0.5f + MathF.Sin(yaw) * 0.2f, 0, 0.5f + MathF.Cos(yaw) * 0.2f), new V3(0.13f, 0.08f + r.F() * 0.06f, 0.12f), new V3(r.S(0.3f), r.F() * 6f, r.S(0.3f)), Broken(c, r, m), s, 0f);
        Piece(l, new V3(0.5f, 0, 0.5f), new V3(wide / 2, thick / 2, len / 2), new V3(pitch, yaw, r.S(0.12f)), c.Scale(0.92f + r.F() * 0.12f), s, 0f);
        if (Reinforced(m))
        {
            // The raised end is the broken one: its bars come out of it, bent down where they snapped.
            var along = new V3(MathF.Sin(yaw), MathF.Sin(pitch), MathF.Cos(yaw)).Normalized();
            V3 end = new V3(0.5f, 0.02f, 0.5f) + along * (len * 0.48f);
            end = Inside(end, 0.08f);
            V3 side = new V3(MathF.Cos(yaw), 0, -MathF.Sin(yaw));
            int bars = 2 + r.Int(2);
            for (int i = 0; i < bars; i++)
            {
                V3 from = Inside(end + side * ((i - (bars - 1) / 2f) * wide / bars), 0.06f);
                Bar(l, r, from, along, 0.1f + r.F() * 0.16f);
            }
        }
        ChunkPile(l, r, c, s, 0.07f, 3); // the bits that broke off it
    }

    /// <summary>A few chunks of what fell, each lying as it landed, in a little of its own dust.</summary>
    static void ChunkPile(List<ShapeBox> l, Pick r, Rgb c, Surface s, float size, int count)
    {
        for (int i = 0; i < count; i++)
        {
            float a = size * (0.5f + r.F() * 0.9f);
            var half = new V3(a * (0.7f + r.F() * 0.6f), a * (0.45f + r.F() * 0.35f), a * (0.7f + r.F() * 0.6f));
            // A chunk lies on one of its faces as it landed (a little off level), never balanced on a corner.
            Piece(l, new V3(0.15f + r.F() * 0.7f, 0, 0.15f + r.F() * 0.7f), half, new V3(r.S(0.1f), r.F() * 6f, r.S(0.1f)), Broken(c, r, Material.Concrete).Lerp(c, 0.5f), s, 0f);
        }
        DustPatch(l, r, c);
    }

    /// <summary>A little of its own dust round it: two small turned drifts in the pieces' own colour, not a pale square pad.</summary>
    static void DustPatch(List<ShapeBox> l, Pick r, Rgb c)
    {
        for (int i = 0; i < 2; i++)
            Piece(l, new V3(0.25f + r.F() * 0.5f, 0, 0.25f + r.F() * 0.5f), new V3(0.1f + r.F() * 0.12f, 0.004f, 0.08f + r.F() * 0.1f), new V3(0, r.F() * 6f, 0), c.Lerp(Dust, 0.25f).Scale(0.85f + r.F() * 0.1f), Surface.Matte, 0.014f);
    }

    /// <summary>Where a lot came down at once: a mound of chunks over its dust, a slab across it, bars sticking out at angles.</summary>
    static void HeapPile(List<ShapeBox> l, Pick r, Rgb c, Surface s, Material m)
    {
        l.Add(new ShapeBox(new V3(0.04f, 0, 0.04f), new V3(0.96f, 0.05f, 0.96f), c.Lerp(Dust, 0.45f), Surface.Matte, true));
        // Layers, each a little smaller and resting on the one under it.
        float y = 0.04f;
        for (int layer = 0; layer < 3; layer++)
        {
            float reach = 0.36f - layer * 0.1f, a = 0.17f - layer * 0.03f, top = y;
            int n = 5 - layer;
            for (int i = 0; i < n; i++)
            {
                float ang = (i + r.F() * 0.6f) / n * MathF.Tau, d = reach * (0.45f + r.F() * 0.55f);
                var half = new V3(a * (0.7f + r.F() * 0.5f), a * (0.5f + r.F() * 0.3f), a * (0.7f + r.F() * 0.5f));
                var (_, t) = Piece(l, new V3(0.5f + MathF.Cos(ang) * d, 0, 0.5f + MathF.Sin(ang) * d), half, new V3(r.S(0.5f), r.F() * 6f, r.S(0.5f)), Broken(c, r, m), s, y);
                top = MathF.Max(top, t);
            }
            y = y + (top - y) * 0.6f;
        }
        // A slab caught across the top of it.
        Piece(l, new V3(0.5f + r.S(0.1f), 0, 0.5f + r.S(0.1f)), new V3(0.26f, 0.05f, 0.34f), new V3(0.25f + r.F() * 0.3f, r.F() * 6f, r.S(0.2f)), c.Scale(0.95f), s, y - 0.06f);
        if (Reinforced(m))
            for (int i = 0; i < 2 + r.Int(2); i++)
            {
                float ang = r.F() * MathF.Tau;
                var dir = new V3(MathF.Cos(ang), 0.35f + r.F() * 0.6f, MathF.Sin(ang)).Normalized();
                Bar(l, r, Inside(new V3(0.5f + MathF.Cos(ang) * 0.12f, y * 0.7f, 0.5f + MathF.Sin(ang) * 0.12f), 0.15f), dir, 0.18f + r.F() * 0.2f);
            }
    }

    /// <summary>The ceiling come down: its tiles lying where they dropped, one propped on another, in a drift of plaster dust.</summary>
    static void TileFall(List<ShapeBox> l, Pick r, Rgb c)
    {
        // The dust lies in drifts, not a sheet: a few small turned patches, greyed by the years.
        Rgb plaster = c.Lerp(Dust, 0.45f).Scale(0.78f);
        for (int i = 0; i < 2 + r.Int(2); i++)
            Piece(l, new V3(0.2f + r.F() * 0.6f, 0, 0.2f + r.F() * 0.6f), new V3(0.08f + r.F() * 0.14f, 0.004f, 0.06f + r.F() * 0.12f), new V3(0, r.F() * 6f, 0), plaster.Scale(0.9f + r.F() * 0.15f), Surface.Matte, 0.014f);
        int n = 2 + r.Int(3);
        V3 last = default;
        for (int i = 0; i < n; i++)
        {
            // The last of three or more is propped on the one before it, its low edge on the floor beside it: laid across
            // that tile, not leaning on nothing.
            bool propped = i == n - 1 && n > 2;
            float tilt = propped ? 0.2f : r.S(0.03f);
            Rgb t = Tile.Lerp(new Rgb(0x8a, 0x7a, 0x5a), r.F() * 0.35f); // stained by the water that brought them down
            V3 at = propped ? new V3(last.X + 0.06f, 0, last.Z) : new V3(0.2f + r.F() * 0.6f, 0, 0.2f + r.F() * 0.6f);
            var (centre, _) = Piece(l, at, new V3(0.1f + r.F() * 0.05f, 0.008f, 0.1f + r.F() * 0.05f), new V3(tilt, propped ? 0f : r.F() * 6f, r.S(0.03f)), t.Scale(0.8f), Surface.Matte, 0f);
            last = centre;
        }
    }

    /// <summary>Joists and boards down at angles across each other.</summary>
    static void Timbers(List<ShapeBox> l, Pick r, Rgb c)
    {
        Rgb w = c.Lerp(Wood, 0.3f);
        float y = 0f;
        for (int i = 0; i < 2 + r.Int(2); i++)
        {
            var (_, top) = Piece(l, new V3(0.5f + r.S(0.12f), 0, 0.5f + r.S(0.12f)), new V3(0.05f + r.F() * 0.03f, 0.04f + r.F() * 0.03f, 0.4f + r.F() * 0.05f), new V3(r.S(0.25f), r.F() * 6f, r.S(0.1f)), w.Scale(0.75f + r.F() * 0.3f), Surface.Matte, y);
            y = MathF.Min(top - 0.04f, 0.2f);
        }
        DustPatch(l, r, c.Scale(0.7f));
    }

    static void Shards(List<ShapeBox> l, Pick r, Rgb c)
    {
        for (int i = 0; i < 5; i++)
            Piece(l, new V3(0.15f + r.F() * 0.7f, 0, 0.15f + r.F() * 0.7f), new V3(0.03f + r.F() * 0.08f, 0.004f, 0.02f + r.F() * 0.06f), new V3(r.S(0.15f), r.F() * 6f, r.S(0.15f)), c.Scale(0.9f + r.F() * 0.3f), Surface.Glass, 0f);
    }

    /// <summary>
    /// A rebar block: the bars of a torn slab or wall standing out of the face toward its facing, two rows of them
    /// a hand apart as they were cast, each run out a little way and bent down where it snapped; out of a wall's
    /// broken top, a tangle standing up.
    /// </summary>
    public static void Bars(List<ShapeBox> l, Block b, Int3 p)
    {
        var r = new Pick(Hash.Cell(p, 157));
        Int3 f = b.Facing.Vec();
        var into = new V3(-f.X, -f.Y, -f.Z); // away from the face, into this cell
        // Two axes across the face.
        V3 u = f.Y != 0 ? new V3(1, 0, 0) : new V3(f.Z, 0, -f.X), v = f.Y != 0 ? new V3(0, 0, 1) : new V3(0, 1, 0);
        V3 face = new V3(0.5f, 0.5f, 0.5f) + new V3(f.X, f.Y, f.Z) * 0.5f;
        int n = 0;
        foreach (float a in new[] { 0.22f, 0.5f, 0.78f })
            foreach (float c in f.Y != 0 ? new[] { 0.3f, 0.7f } : new[] { 0.28f, 0.72f })
            {
                if (r.F() < 0.3f) continue;
                V3 from = face + u * (a - 0.5f) + v * (c - 0.5f) + into * 0.004f;
                V3 dir = (into + u * r.S(0.25f) + v * r.S(0.25f)).Normalized();
                Bar(l, r, from, dir, 0.18f + r.F() * 0.42f);
                if (++n >= 5) return;
            }
    }

    /// <summary>A bar of reinforcing out of a broken edge: a straight run, then bent where it snapped.</summary>
    public static void Bar(List<ShapeBox> l, Pick r, V3 from, V3 dir, float length)
    {
        const float T = 0.022f;
        float first = Fit(from, dir, length * (0.45f + r.F() * 0.3f));
        Rgb rust = Rust.Lerp(new Rgb(0x8a, 0x52, 0x30), r.F());
        V3 mid = Rod(l, from, dir, first, T, rust);
        var bent = (dir + new V3(r.S(0.6f), -0.5f - r.F() * 0.6f, r.S(0.6f))).Normalized();
        // A bent end never goes through the floor: it turns along it instead.
        if (mid.Y + bent.Y * (length - first) < 0.03f) bent = new V3(bent.X, 0, bent.Z).Normalized();
        float rest = Fit(mid, bent, length - first);
        if (rest > 0.03f) Rod(l, mid, bent, rest, T, rust.Scale(0.85f));
    }

    /// <summary>How far a bar can run from a point along a direction and stay in its cell (and off its floor).</summary>
    static float Fit(V3 from, V3 dir, float length)
    {
        float t = length;
        void Axis(float p, float d, float lo, float hi)
        {
            if (d > 1e-4f) t = MathF.Min(t, (hi - p) / d);
            else if (d < -1e-4f) t = MathF.Min(t, (lo - p) / d);
        }
        Axis(from.X, dir.X, 0.03f, 0.97f);
        Axis(from.Y, dir.Y, 0.03f, 0.97f);
        Axis(from.Z, dir.Z, 0.03f, 0.97f);
        return MathF.Max(0f, t);
    }

    /// <summary>A thin square rod from a point along a direction; where it ends.</summary>
    public static V3 Rod(List<ShapeBox> l, V3 from, V3 dir, float length, float thick, Rgb color)
    {
        V3 centre = from + dir * (length / 2);
        float yaw = MathF.Atan2(dir.X, dir.Z), pitch = -MathF.Asin(Math.Clamp(dir.Y, -1f, 1f));
        var h = new V3(thick / 2, thick / 2, length / 2);
        l.Add(new ShapeBox(centre - h, centre + h, color, Surface.Metal, false, new V3(pitch, yaw, 0)));
        return from + dir * length;
    }

    static V3 Inside(V3 p, float m) => new(Math.Clamp(p.X, m, 1 - m), MathF.Max(p.Y, 0.02f), Math.Clamp(p.Z, m, 1 - m));

    /// <summary>
    /// A box of half-size <paramref name="half"/> turned by <paramref name="tilt"/> (pitch, yaw, roll), its lowest
    /// corner sunk a hair into whatever is at <paramref name="floor"/>, and moved in from the cell's sides so no
    /// corner leaves the cell. Returns its centre and its top.
    /// </summary>
    static (V3 Centre, float Top) Piece(List<ShapeBox> l, V3 at, V3 half, V3 tilt, Rgb c, Surface s, float floor)
    {
        float x0 = 0, x1 = 0, y0 = 0, y1 = 0, z0 = 0, z1 = 0;
        for (int pass = 0; pass < 2; pass++)
        {
            x0 = x1 = y0 = y1 = z0 = z1 = 0;
            for (int i = 0; i < 8; i++)
            {
                V3 v = Shapes.Rotate(new V3((i & 1) == 0 ? -half.X : half.X, (i & 2) == 0 ? -half.Y : half.Y, (i & 4) == 0 ? -half.Z : half.Z), tilt);
                x0 = MathF.Min(x0, v.X); x1 = MathF.Max(x1, v.X);
                y0 = MathF.Min(y0, v.Y); y1 = MathF.Max(y1, v.Y);
                z0 = MathF.Min(z0, v.Z); z1 = MathF.Max(z1, v.Z);
            }
            // Too big to lie in the cell as it is turned: a smaller piece of the same.
            float over = MathF.Max(x1 - x0, z1 - z0) / 0.95f;
            if (over <= 1f) break;
            half = half * (1f / over);
        }
        const float M = 0.02f;
        float cx = Math.Clamp(at.X, M - x0, MathF.Max(M - x0, 1 - M - x1)), cz = Math.Clamp(at.Z, M - z0, MathF.Max(M - z0, 1 - M - z1));
        float cy = floor - y0 - 0.015f;
        var centre = new V3(cx, cy, cz);
        l.Add(new ShapeBox(centre - half, centre + half, c, s, false, tilt));
        return (centre, cy + y1);
    }

    /// <summary>A cell's own small stream of numbers, so a pile looks the same every time it is drawn.</summary>
    public struct Pick
    {
        ulong _s;
        public Pick(ulong seed) => _s = seed | 1;
        ulong Next()
        {
            _s ^= _s << 13; _s ^= _s >> 7; _s ^= _s << 17;
            return _s;
        }
        public float F() => (Next() >> 40) / (float)(1 << 24);
        public float S(float k) => (F() * 2f - 1f) * k;
        public int Int(int n) => (int)(Next() % (ulong)n);
    }

    // ---- where it lies ------------------------------------------------------------------------

    /// <summary>
    /// Lays what came down where it came down (at most <paramref name="most"/> piles): each fallen column drops to the first floor under it, and where a
    /// lot fell in one spot it heaps, spilling chunks round it; what fell from an outer wall spills outward. A
    /// pile only goes on a cell that is clear, off the protected way, and on a floor that holds it.
    /// <paramref name="share"/> of the fallen columns leave a pile.
    /// </summary>
    public static int Lay(PoiInstance inst, IEnumerable<(Int3 Cell, Block Block)> fallen, Rng rng, float share, int most = int.MaxValue)
    {
        var g = inst.Blocks;
        var nav = inst.Nav();
        var occ = inst.Layout.Occupied;
        var columns = new Dictionary<(int X, int Z), List<Block>>();
        var lowest = new Dictionary<(int X, int Z), int>();
        var outward = new Dictionary<(int X, int Z), Dir>();
        foreach (var (c, b) in fallen)
        {
            if (b.Role is Role.Debris or Role.Loot or Role.Core or Role.Terminal or Role.Growth || b.IsAir) continue;
            var key = (c.X, c.Z);
            if (!columns.TryGetValue(key, out var list)) { columns[key] = list = new List<Block>(); lowest[key] = c.Y; }
            list.Add(b);
            lowest[key] = Math.Min(lowest[key], c.Y);
            if (b.Role is Role.Wall or Role.Window && !outward.ContainsKey(key))
                foreach (Dir d in Dirs.Horizontal)
                    if (!occ[c + d.Vec()]) { outward[key] = d; break; }
        }
        bool Holds(Int3 c) => g.InBounds(c) && (g.Get(c).IsAir || g.Get(c).Type == BlockType.Plate || g.Get(c).Type is BlockType.Ash or BlockType.Scatter or BlockType.Plant)
                              && !inst.Protected[c] && (nav.IsRock(c + Int3.Down) || BlockInfo.Supports(g.Get(c + Int3.Down).Type));
        Int3? Land(Int3 from)
        {
            Int3 c = from;
            if (!g.InBounds(c)) return null;
            while (c.Y > g.Min.Y && g.Get(c).IsAir && g.Get(c + Int3.Down).IsAir && !nav.IsRock(c + Int3.Down)) c += Int3.Down;
            return Holds(c) ? c : null;
        }
        int laid = 0;
        var keys = columns.Keys.ToList();
        rng.Shuffle(keys);
        foreach (var key in keys.Take((int)MathF.Ceiling(keys.Count * share)))
        {
            if (laid >= most) break;
            List<Block> blocks = columns[key];
            Int3 top = new(key.X, lowest[key], key.Z);
            // An outer wall's pieces spill out from its foot.
            if (outward.TryGetValue(key, out Dir o) && rng.Chance(0.6f)) top += o.Vec() * rng.Between(1, 2);
            if (Land(top) is not Int3 at) continue;
            Block main = blocks.GroupBy(b => (b.Material, b.Role)).OrderByDescending(x => x.Count()).First().First();
            byte kind = blocks.Count >= 4 && main.Material is not (Material.Glass or Material.Timber) ? Heap : KindOf(main);
            g.Set(at, Of(main, kind));
            laid++;
            if (kind != Heap) continue;
            foreach (Dir d in Dirs.Horizontal)
                if (rng.Chance(0.45f) && Land(at + d.Vec()) is Int3 n && n.Y == at.Y)
                {
                    g.Set(n, Of(main, rng.Chance(0.3f) && main.Role is Role.Roof or Role.Floor ? Slab : Chunks));
                    laid++;
                }
        }
        return laid;
    }
}
