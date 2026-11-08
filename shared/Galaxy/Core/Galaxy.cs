using System;
using System.Collections.Generic;
using System.Numerics;

namespace GalHack.Stars;

/// <summary>What a star is (the design's star classes): it decides what its system can hold, and how it looks from far off.</summary>
public enum StarKind : byte { BrownDwarf, RedDwarf, OrangeDwarf, YellowDwarf, WhiteStar, BlueGiant, Protostar, RedGiant, WhiteDwarf, Pulsar, Magnetar, BlackHole }

/// <summary>The design's regions. The place a star sits predicts what kind of place it is.</summary>
public enum Region : byte { Core, InnerArm, OuterArm, Void, Periphery, Satellite }

public enum NebulaKind : byte { Emission, Dark, Reflection, Remnant }

/// <summary>
/// One catalogue entry: all an unvisited star costs. Its id is its place in the catalogue, the same
/// for a seed every time. Its system isn't here: it is generated from
/// <see cref="Galaxy.SystemSeed"/> the first time someone goes there.
/// </summary>
public readonly record struct Star(int Id, Vector3 Position, float Light, StarKind Kind, Region Region, byte Companions, StarKind Companion, short Nebula);

/// <summary>A small satellite galaxy: its stars are in the catalogue, marked with the region.</summary>
public readonly record struct Satellite(string Name, Vector3 Centre, float Radius, int First, int Count);

/// <summary>
/// The galaxy, one seed's worth: same seed, same galaxy. Positions are light years from the central
/// black hole, the disc in the XZ plane. No engine types: it is made on a worker thread.
/// </summary>
public sealed class Galaxy
{
    /// <summary>The disc's radius, ly; the hyperdrive's reach (design: T1 about 30 LY, T2 60); where the rim starts.</summary>
    public const float DiscRadius = 500f, JumpT1 = 30f, JumpT2 = 60f, RimFrom = 400f;

    public readonly ulong Seed;
    public readonly Star[] Stars;
    public readonly Nebula[] Nebulae;
    public readonly Satellite[] Satellites;

    const float GridCell = 32f;
    readonly Dictionary<long, List<int>> _grid = new();

    internal Galaxy(ulong seed, Star[] stars, Nebula[] nebulae, Satellite[] satellites)
    {
        Seed = seed;
        Stars = stars;
        Nebulae = nebulae;
        Satellites = satellites;
        foreach (ref readonly Star s in stars.AsSpan())
        {
            long key = Key(s.Position);
            if (!_grid.TryGetValue(key, out List<int> list)) _grid[key] = list = new List<int>(8);
            list.Add(s.Id);
        }
    }

    /// <summary>What picks among the rim stars for a home of this kind.</summary>
    public static ulong HomeKey(ulong seed, StarKind kind, bool inNebula) => Mix.Of(seed, (long)kind, inNebula ? 1 : 0);

    /// <summary>Makes the galaxy for a seed. Same seed, same galaxy.</summary>
    public static Galaxy Make(ulong seed) => GalaxyGen.Make(seed);

    /// <summary>The seed a star's system is generated from, the first time anyone visits it.</summary>
    public ulong SystemSeed(int id) => Mix.Of(Seed, 0x53595354, id);

    /// <summary>A star's name, worked out when asked for: names take no room in the catalogue.</summary>
    public string Name(int id) => GalaxyNames.Star(Seed, id);

    public Region RegionAt(Vector3 p) => GalaxyGen.RegionOf(this, p);

    static long Key(Vector3 p) => Key((int)MathF.Floor(p.X / GridCell), (int)MathF.Floor(p.Y / GridCell), (int)MathF.Floor(p.Z / GridCell));

    static long Key(int x, int y, int z) => ((long)(x + 512) << 42) | ((long)(y + 512) << 21) | (uint)(z + 512);

    /// <summary>Stars within a distance of a point, nearest first (<paramref name="except"/> left out).</summary>
    public List<int> Within(Vector3 p, float range, int except = -1)
    {
        var found = new List<(float d, int id)>();
        int reach = (int)MathF.Ceiling(range / GridCell);
        int cx = (int)MathF.Floor(p.X / GridCell), cy = (int)MathF.Floor(p.Y / GridCell), cz = (int)MathF.Floor(p.Z / GridCell);
        float r2 = range * range;
        for (int x = cx - reach; x <= cx + reach; x++)
        for (int y = cy - reach; y <= cy + reach; y++)
        for (int z = cz - reach; z <= cz + reach; z++)
        {
            if (!_grid.TryGetValue(Key(x, y, z), out List<int> list)) continue;
            foreach (int id in list)
            {
                float d = Vector3.DistanceSquared(Stars[id].Position, p);
                if (d <= r2 && id != except) found.Add((d, id));
            }
        }
        found.Sort();
        var ids = new List<int>(found.Count);
        foreach (var (_, id) in found) ids.Add(id);
        return ids;
    }

    /// <summary>The stars one jump from a star, nearest first.</summary>
    public List<int> Neighbours(int id, float range = JumpT1) => Within(Stars[id].Position, range, id);

    /// <summary>How much light gets through the dust between two points, 0 to 1 (a dark nebula blots out what is behind it).</summary>
    public float Clear(Vector3 from, Vector3 to)
    {
        Vector3 along = to - from;
        float length = along.Length();
        if (length < 1e-3f) return 1f;
        Vector3 dir = along / length;
        float depth = 0f;
        foreach (Nebula n in Nebulae)
        {
            if (n.Dust <= 0f || !n.Crosses(from, dir, length, out float t0, out float t1)) continue;
            const int Steps = 7;
            float dt = (t1 - t0) / Steps, sum = 0f;
            for (int i = 0; i < Steps; i++) sum += n.Density(from + dir * (t0 + dt * (i + 0.5f)));
            depth += sum * dt * n.Dust;
        }
        return MathF.Exp(-depth);
    }

    /// <summary>The nebula a point lies in, or -1: the one it lies deepest in.</summary>
    public int NebulaAt(Vector3 p)
    {
        int best = -1;
        float most = Nebula.Edge;
        foreach (Nebula n in Nebulae)
        {
            float d = n.Density(p);
            if (d > most) (most, best) = (d, n.Id);
        }
        return best;
    }

    /// <summary>How far a place lies across from the nearest spiral arm, as a share of the arm's width: 0 on its centre line, past 1 in the gap.</summary>
    public float ArmShare(Vector3 p) => GalaxyGen.ArmShare(this, p);

    /// <summary>A home's most share of an arm's width off its centre line: well inside the arm, never the gap beside it.</summary>
    public const float TipShare = 0.6f;

    /// <summary>Where the arms end, out on the rim (depth 0), and the core's edge (depth 1), ly from the centre.</summary>
    public const float TipRadius = 490f, CoreRadius = 85f;

    /// <summary>
    /// How deep in the galaxy a place lies, from 0 at the arms' outer tips on the rim to 1 at the
    /// core, by its distance from the centre. The satellites and anything past the tips are 0.
    /// </summary>
    public static float Depth(Vector3 p)
    {
        float r = new Vector2(p.X, p.Z).Length();
        return Math.Clamp((TipRadius - r) / (TipRadius - CoreRadius), 0f, 1f);
    }

    public float DepthOf(int id) => Depth(Stars[id].Position);

    /// <summary>The galaxy map's word for a depth: RIM, OUTER, INNER or CORE (the world roll's zones rim, arm, inner and core).</summary>
    public static string Band(float depth) => depth < 0.25f ? "RIM" : depth < 0.5f ? "OUTER" : depth < 0.8f ? "INNER" : "CORE";

    /// <summary>
    /// A home at a depth asked for (a new game's option): a real star of the world's class as near
    /// that depth as there is one, inside an arm with neighbours in jump reach, in a nebula you can
    /// see when the sky has one. The band widens until something fits, so there is always a home.
    /// </summary>
    public int Home(StarKind kind, ulong key, bool inNebula, float depth)
    {
        var mine = new List<int>();
        foreach (ref readonly Star s in Stars.AsSpan())
            if (s.Kind == kind && s.Region != Region.Satellite) mine.Add(s.Id);
        if (mine.Count == 0) return Home(kind, key, inNebula);
        int Pick(List<int> list) => list[(int)(Mix.Hash(key ^ 0x44455054) % (ulong)list.Count)];
        bool Seen(int id) => Stars[id].Nebula >= 0 && Nebulae[Stars[id].Nebula].Kind != NebulaKind.Dark;
        for (float within = 0.02f; ; within *= 2f)
        {
            var near = mine.FindAll(id => MathF.Abs(DepthOf(id) - depth) <= within);
            var fits = near.FindAll(id => ArmShare(Stars[id].Position) <= 1f && (inNebula ? Seen(id) : Stars[id].Nebula < 0) && Neighbours(id).Count >= 4);
            if (fits.Count == 0) fits = near.FindAll(id => ArmShare(Stars[id].Position) <= 1f && Neighbours(id).Count >= 4);
            if (fits.Count == 0) fits = near.FindAll(id => Neighbours(id).Count >= 1);
            if (fits.Count == 0 && within >= 1f) fits = near;
            if (fits.Count > 0) return Pick(fits);
        }
    }

    /// <summary>
    /// The player's home: a real star of the world's own class at the outer tip of a spiral arm, out on
    /// the rim (design: every game begins on a border world) and inside the arm, never in the gap between
    /// two or in toward the core, with neighbours in jump reach. <paramref name="key"/> picks among the
    /// candidates; a world whose sky holds a nebula gets a star inside one you can see, and the rest get
    /// one in the open. Each step down gives a little up, so there is always a home.
    /// </summary>
    public int Home(StarKind kind, ulong key, bool inNebula)
    {
        List<int> rim = new(), wider = new(), any = new();
        foreach (ref readonly Star s in Stars.AsSpan())
        {
            if (s.Kind != kind || s.Region == Region.Satellite) continue;
            any.Add(s.Id);
            float r = new Vector2(s.Position.X, s.Position.Z).Length();
            if (r >= RimFrom) rim.Add(s.Id);
            if (r >= 250f) wider.Add(s.Id);
        }
        int Pick(List<int> list) => list[(int)(Mix.Hash(key ^ 0x484F4D45) % (ulong)list.Count)];
        bool Reach(int id, int least) => Neighbours(id).Count >= least;
        bool Seen(int id) => Stars[id].Nebula >= 0 && Nebulae[Stars[id].Nebula].Kind != NebulaKind.Dark;
        // Out in the open the crowded spots are skipped: the rim is meant to be lonely. A nebula's stars crowd by nature.
        bool Lonely(int id) => Neighbours(id).Count <= 70;
        var tips = rim.FindAll(id => ArmShare(Stars[id].Position) <= TipShare);
        var fits = tips.FindAll(id => Reach(id, 4) && (inNebula ? Seen(id) : Stars[id].Nebula < 0 && Lonely(id)));
        if (fits.Count == 0) fits = tips.FindAll(id => Reach(id, 4) && (inNebula ? Seen(id) : Stars[id].Nebula < 0));
        // A nebula world would rather its nebula on the rim than an arm's tip without one.
        if (fits.Count == 0 && inNebula) fits = rim.FindAll(id => Reach(id, 4) && Seen(id));
        if (fits.Count == 0) fits = tips.FindAll(id => Reach(id, 1));
        if (fits.Count == 0) fits = rim.FindAll(id => Reach(id, 4) && (inNebula ? Seen(id) : Stars[id].Nebula < 0 && Lonely(id)));
        if (fits.Count == 0) fits = rim.FindAll(id => Reach(id, 4) && Stars[id].Nebula < 0);
        if (fits.Count == 0) fits = rim.FindAll(id => Reach(id, 1));
        if (fits.Count == 0) fits = wider.FindAll(id => Reach(id, 1));
        if (fits.Count == 0) fits = any;
        return Pick(fits);
    }
}

/// <summary>
/// A nebula: a real region of the galaxy, with systems in it. Its shape is a density you can ask of
/// any point in space: the star catalogue uses it to say who is inside, and the sky marches through
/// it from wherever you are, so what you see is what is there.
/// </summary>
public sealed class Nebula
{
    /// <summary>A point with more density than this is inside.</summary>
    public const float Edge = 0.12f;

    public int Id { get; init; }
    public string Name { get; init; }
    public NebulaKind Kind { get; init; }
    public Vector3 Centre { get; init; }

    /// <summary>The sphere it all lies in, ly.</summary>
    public float Radius { get; init; }

    /// <summary>How it is stretched (each up to 1), and turned.</summary>
    public Vector3 Axes { get; init; }
    public Quaternion Turn { get; init; }

    /// <summary>Its colour where it is thick, and where it thins out (linear, 0 to 1).</summary>
    public Vector3 Colour { get; init; }
    public Vector3 Rim { get; init; }

    /// <summary>Light given out, and dust blocking light, per light year of it.</summary>
    public float Glow { get; init; }
    public float Dust { get; init; }

    public Vector3 Offset { get; init; }
    public (Vector3 at, float radius)[] Lobes { get; init; }

    /// <summary>Whether a ray from a point crosses its sphere within a length, and over what stretch.</summary>
    public bool Crosses(Vector3 from, Vector3 dir, float length, out float t0, out float t1)
    {
        Vector3 v = Centre - from;
        float along = Vector3.Dot(v, dir), r2 = Radius * Radius;
        float off = v.LengthSquared() - along * along;
        t0 = t1 = 0f;
        if (off >= r2) return false;
        float half = MathF.Sqrt(r2 - off);
        t0 = MathF.Max(along - half, 0f);
        t1 = MathF.Min(along + half, length);
        return t1 > t0;
    }

    static Vector3 Warp(Vector3 p) => new Vector3(LatticeNoise.Value(p), LatticeNoise.Value(p + new Vector3(5.2f, 1.3f, 7.7f)), LatticeNoise.Value(p + new Vector3(2.8f, 9.1f, 3.4f))) - new Vector3(0.5f);

    /// <summary>
    /// How thick it is at a point, 0 to 1. The lobes only say roughly where it is: space is folded
    /// twice over before they are read, so they have no clean edge, and the gas itself is thin
    /// wisps and bright filaments that fade away to nothing at the rim.
    /// </summary>
    public float Density(Vector3 p)
    {
        Vector3 v = p - Centre;
        float d2 = v.LengthSquared(), r2 = Radius * Radius;
        if (d2 >= r2) return 0f;
        Vector3 q = Vector3.Transform(v, Quaternion.Conjugate(Turn)) / (Axes * Radius);
        Vector3 w = q + Warp(q * 1.5f + Offset) * 1.1f;
        w += Warp(w * 3.7f + Offset * 1.3f) * 0.34f;
        float body;
        if (Kind == NebulaKind.Remnant)
        {
            float shell = (w.Length() - 0.66f) / 0.13f;
            body = MathF.Exp(-shell * shell);
        }
        else
        {
            body = 0f;
            foreach (var (at, radius) in Lobes)
            {
                float k = 1f - Vector3.DistanceSquared(w, at) / (radius * radius * 1.6f);
                if (k > 0f) body += k * k;
            }
            body = 1f - MathF.Exp(-body * 1.5f);
        }
        if (body <= 0f) return 0f;
        float grain = LatticeNoise.Fractal(w * 2.4f + Offset * 1.7f, 4);
        float wisps = Math.Clamp((grain - 0.33f) / 0.34f, 0f, 1f);
        wisps = wisps * wisps * (3f - 2f * wisps);
        float fold = 1f - MathF.Abs(2f * LatticeNoise.Fractal(w * 3.1f + Offset, 3) - 1f);
        float filaments = fold * fold * fold * fold;
        float fade = 1f - d2 / r2;
        // It thins out long before its sphere ends, so the sphere never shows as an outline.
        return MathF.Min(body * body * (0.55f * wisps + 0.75f * filaments * wisps), 1f) * fade * fade;
    }

    /// <summary>Its colour at a point where it has a given thickness: thick gas and thin gas differ, and drift across it.</summary>
    public Vector3 ColourAt(Vector3 p, float density)
    {
        float drift = LatticeNoise.Value((p - Centre) / Radius * 1.4f + Offset);
        return Vector3.Lerp(Rim, Colour, Math.Clamp(density * 2.2f + (drift - 0.5f) * 1.1f, 0f, 1f));
    }

    /// <summary>Points of its own density, the same every time: how the map draws it.</summary>
    public (Vector3 at, float density, float size, Vector3 colour)[] Wisps(int count)
    {
        var rng = new GalaxyRng(Mix.Of(0x57495350, (long)(Offset.X * 1000f), Id));
        var list = new System.Collections.Generic.List<(Vector3, float, float, Vector3)>(count);
        for (int tries = 0; tries < count * 600 && list.Count < count; tries++)
        {
            Vector3 p = Centre + rng.InBall() * Radius;
            float d = Density(p);
            if (d > 0.04f && rng.Float() < d * 3f) list.Add((p, d, rng.Float(), ColourAt(p, d)));
        }
        return list.ToArray();
    }
}

/// <summary>The catalogue's hashing and random numbers (SplitMix64): its own, so nothing outside can shift a galaxy.</summary>
public static class Mix
{
    public static ulong Hash(ulong z)
    {
        z += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public static ulong Of(ulong seed, long a, long b = 0) => Hash(seed ^ Hash((ulong)a ^ Hash((ulong)b + 0x632BE59BD9B4E019UL)));

    public static float Unit(ulong hash) => (Hash(hash) >> 40) * (1f / (1 << 24));
}

public struct GalaxyRng
{
    ulong _state;

    public GalaxyRng(ulong seed) => _state = seed;

    public ulong Next() => Mix.Hash(_state += 0x9E3779B97F4A7C15UL);

    /// <summary>Uniform in [0, 1).</summary>
    public float Float() => (Next() >> 40) * (1f / (1 << 24));

    public float Range(float a, float b) => a + (b - a) * Float();

    public int Int(int count) => (int)((Next() >> 33) % (ulong)count);

    /// <summary>Uniform between two values on a log scale.</summary>
    public float Log(float a, float b) => a * MathF.Pow(b / a, Float());

    public Vector3 InBall()
    {
        while (true)
        {
            var v = new Vector3(Float() * 2f - 1f, Float() * 2f - 1f, Float() * 2f - 1f);
            if (v.LengthSquared() <= 1f) return v;
        }
    }

    public Vector3 Direction()
    {
        while (true)
        {
            Vector3 v = InBall();
            float l = v.Length();
            if (l > 1e-3f) return v / l;
        }
    }

    public Quaternion Turn()
    {
        var q = new Quaternion(Float() * 2f - 1f, Float() * 2f - 1f, Float() * 2f - 1f, Float() * 2f - 1f);
        return Quaternion.Normalize(q.LengthSquared() > 1e-4f ? q : Quaternion.Identity);
    }
}

/// <summary>Smooth value noise from a fixed lattice that tiles every 32 units: quick enough to march through.</summary>
public static class LatticeNoise
{
    const int N = 32;
    static readonly float[] Lattice = Make();

    static float[] Make()
    {
        var v = new float[N * N * N];
        for (int i = 0; i < v.Length; i++) v[i] = Mix.Unit(0x4E4F495345UL + (ulong)i);
        return v;
    }

    public static float Value(Vector3 p)
    {
        int ix = (int)MathF.Floor(p.X), iy = (int)MathF.Floor(p.Y), iz = (int)MathF.Floor(p.Z);
        float fx = p.X - ix, fy = p.Y - iy, fz = p.Z - iz;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        fz = fz * fz * (3f - 2f * fz);
        int x0 = ix & (N - 1), x1 = (ix + 1) & (N - 1), y0 = (iy & (N - 1)) * N, y1 = ((iy + 1) & (N - 1)) * N, z0 = (iz & (N - 1)) * N * N, z1 = ((iz + 1) & (N - 1)) * N * N;
        float a = Lerp(Lattice[x0 + y0 + z0], Lattice[x1 + y0 + z0], fx), b = Lerp(Lattice[x0 + y1 + z0], Lattice[x1 + y1 + z0], fx);
        float c = Lerp(Lattice[x0 + y0 + z1], Lattice[x1 + y0 + z1], fx), d = Lerp(Lattice[x0 + y1 + z1], Lattice[x1 + y1 + z1], fx);
        return Lerp(Lerp(a, b, fy), Lerp(c, d, fy), fz);
    }

    /// <summary>Several octaves of it, 0 to 1.</summary>
    public static float Fractal(Vector3 p, int octaves)
    {
        float sum = 0f, amp = 0.5f, total = 0f;
        for (int i = 0; i < octaves; i++)
        {
            sum += Value(p) * amp;
            total += amp;
            amp *= 0.5f;
            p = p * 2.03f + new Vector3(3.1f, 1.7f, 4.3f);
        }
        return sum / total;
    }

    static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
