using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Planets.Gen;

namespace GalHack.Terrain;

/// <summary>
/// A way into the caves, chosen rather than come upon: a passage from a spot in the land where a cave
/// mouth makes sense, into the hill and down to the cave band. Where: <c>cliff</c> (the foot of a rock
/// face, level ground before it), <c>gully</c> (the head of a cleft, higher ground to both sides) or
/// <c>flank</c> (a bench on a hillside, the hill rising on behind it). Never out on an open slope.
/// </summary>
public sealed class CaveMouth
{
    public double X, Z;           // the mouth, on the ground, in canonical metres
    public float Y;
    public float DirX, DirZ;      // into the hill
    public string Kind = "";
    /// <summary>The passage's centre line in metres from the mouth (x, y, z), and its radius at each point.</summary>
    public (float X, float Y, float Z, float R)[] Path;
}

public sealed partial class PlanetTerrainSource
{
    /// <summary>The lattice mouths are looked for on, metres, and the share of its cells that try for one.</summary>
    const int MouthCell = 128;
    const float MouthChance = 0.45f;
    /// <summary>How far a mouth's passage can reach from it on the map: what a chunk looks round itself for.</summary>
    const float MouthReach = 64f;

    readonly ConcurrentDictionary<long, Lazy<CaveMouth>> _mouths = new();

    /// <summary>Every cave mouth in a box of the map (x, z canonical or not: they wrap).</summary>
    public IEnumerable<CaveMouth> CaveMouths(double x0, double z0, double x1, double z1)
    {
        int cx0 = (int)Math.Floor(x0 / MouthCell), cx1 = (int)Math.Floor(x1 / MouthCell), cz0 = (int)Math.Floor(z0 / MouthCell), cz1 = (int)Math.Floor(z1 / MouthCell);
        for (int z = cz0; z <= cz1; z++)
        for (int x = cx0; x <= cx1; x++)
            if (MouthIn(x, z) is { } m) yield return m;
    }

    CaveMouth MouthIn(int cx, int cz)
    {
        if (Stamps.Count == 0) return null; // the bare land a world is planned on: its caves come later
        int nx = Width / MouthCell, nz = Height / MouthCell;
        int wx = Mod(cx, nx), wz = Mod(cz, nz);
        return _mouths.GetOrAdd((long)wx << 32 | (uint)wz, static (k, self) => new Lazy<CaveMouth>(() => self.FindMouth((int)(k >> 32), (int)(k & 0xFFFFFFFF))), this).Value;
    }

    /// <summary>The mouths whose passages can touch a chunk column, or null.</summary>
    List<CaveMouth> MouthsNear(int ox, int oz)
    {
        List<CaveMouth> list = null;
        foreach (CaveMouth m in CaveMouths(ox - MouthReach, oz - MouthReach, ox + S + MouthReach, oz + S + MouthReach))
            (list ??= new List<CaveMouth>()).Add(m);
        return list;
    }

    /// <summary>A cell's spot of land, a cell at a time: the land there tells whether a mouth belongs in it, and which way it goes in.</summary>
    CaveMouth FindMouth(int cx, int cz)
    {
        ulong seed = Hash.Of(Map.Spec.Seed, cx, cz, 0xCA4E);
        var rng = new Rng(seed);
        if (rng.NextFloat() >= MouthChance) return null;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            double x = (cx + rng.NextFloat()) * MouthCell, z = (cz + rng.NextFloat()) * MouthCell;
            if (TryMouth(x, z) is { } m) return m;
        }
        return null;
    }

    CaveMouth TryMouth(double x, double z)
    {
        float h = SurfaceHeight(x, z);
        if (h < 4f || CaveStrength(x, z, h) <= 0.02f || StampWeight(x, z) > 0f || WaterSurfaceAt(x, z, out _)) return null;

        // Which way the land rises, and how it lies either side.
        const int Dirs = 12;
        Span<float> ring6 = stackalloc float[Dirs], ring12 = stackalloc float[Dirs], ring20 = stackalloc float[Dirs];
        int up = 0;
        for (int k = 0; k < Dirs; k++)
        {
            double a = k * Math.Tau / Dirs, c = Math.Cos(a), s = Math.Sin(a);
            ring6[k] = SurfaceHeight(x + c * 6, z + s * 6) - h;
            ring12[k] = SurfaceHeight(x + c * 12, z + s * 12) - h;
            ring20[k] = SurfaceHeight(x + c * 20, z + s * 20) - h;
            if (ring12[k] > ring12[up]) up = k;
        }
        int back = (up + Dirs / 2) % Dirs, left = (up + Dirs / 4) % Dirs, right = (up + 3 * Dirs / 4) % Dirs;
        float rise = ring12[up], fall = ring12[back];
        // Level ground before it: a mouth in a slope that runs on down past it is a hole in a hillside.
        bool level = MathF.Abs(ring6[back]) < 1.2f && fall > -3f;
        string kind = null;
        if (rise / 12f > 0.85f && level) kind = "cliff";
        else if (rise > 3.5f && ring12[left] > 2.5f && ring12[right] > 2.5f && fall > -2.5f) kind = "gully";
        else if (ring20[up] / 20f > 0.45f && ring6[up] / 6f > 0.35f && level) kind = "flank";
        if (kind == null) return null;

        // The passage: in along the rise, down at a walkable grade, always under a good roof of rock.
        double ang = up * Math.Tau / Dirs;
        float dx = (float)Math.Cos(ang), dz = (float)Math.Sin(ang);
        var path = new List<(float X, float Y, float Z, float R)> { (-dx * 2f, 1.5f, -dz * 2f, 2.6f) };
        float along = 0f, y = 1.4f;
        const float Step = 6f, Grade = 0.32f;
        bool reached = false;
        for (int k = 0; k < 8 && !reached; k++)
        {
            along += Step;
            if (k >= 2) y -= Step * Grade; // the opening stands tall in the rock face; only then does it go down
            float px = x2(dx, along), pz = x2(dz, along);
            double wx = x + px, wz = z + pz;
            float surface = SurfaceHeight(wx, wz);
            float roof = surface - (h + y) - 2.2f;
            // The first stretch comes out of the slope; past that it stays well under it.
            if (along >= 12f && roof < 2.5f) return null;
            if (WaterSurfaceAt(wx, wz, out _)) return null;
            // Nothing stamped anywhere over the passage, its width and the stretch back included: a stamped column stays shut.
            for (float behind = 0f; behind < Step; behind += 2f)
                for (float across = -3f; across <= 3f; across += 3f)
                    if (StampWeight(wx - dx * behind - dz * across, wz - dz * behind + dx * across) > 0f) return null;
            path.Add((px, y, pz, MathF.Max(2f, 2.6f - k * 0.1f)));
            // Far enough in, and it meets a cave: it stops there, open into it.
            float depth = surface - (h + y);
            float caves = CaveStrength(wx, wz, surface);
            if (along >= 18f && caves > 0f && depth > CaveTop + 2f && CaveDensity(wx, h + y + 0.5, wz, depth, caves) < 1f) reached = true;
        }
        if (path.Count < 4) return null;
        // A passage that never met a cave ends in a chamber of its own, with room for what lives there.
        var last = path[^1];
        path.Add((last.X + dx * 3f, last.Y, last.Z + dz * 3f, reached ? last.R : 4.2f));
        return new CaveMouth { X = Mathx.WrapX(x, Width), Z = Mathx.Wrap(z, Height), Y = h, DirX = dx, DirZ = dz, Kind = kind, Path = path.ToArray() };

        static float x2(float d, float t) => d * t;
    }

    /// <summary>Density (positive in rock) of a mouth's passage at a point: a flattened tube round its path.</summary>
    float MouthDensity(CaveMouth m, double x, double y, double z)
    {
        float px = (float)Wrap(x - m.X, Width), pz = (float)Wrap(z - m.Z, Height), py = (float)(y - m.Y);
        if (px * px + pz * pz > MouthReach * MouthReach) return Limit;
        float best = Limit;
        for (int i = 0; i + 1 < m.Path.Length; i++)
        {
            var a = m.Path[i];
            var b = m.Path[i + 1];
            float ex = b.X - a.X, ey = b.Y - a.Y, ez = b.Z - a.Z, len2 = ex * ex + ey * ey + ez * ez;
            float t = len2 > 1e-5f ? Math.Clamp(((px - a.X) * ex + (py - a.Y) * ey + (pz - a.Z) * ez) / len2, 0f, 1f) : 0f;
            float qx = a.X + ex * t - px, qy = (a.Y + ey * t - py) * 1.35f, qz = a.Z + ez * t - pz;
            best = MathF.Min(best, MathF.Sqrt(qx * qx + qy * qy + qz * qz) - (a.R + (b.R - a.R) * t));
        }
        return best;
    }

    /// <summary>The least density of the mouths' passages at a point, or the limit where there's none.</summary>
    float MouthsAt(List<CaveMouth> mouths, double x, double y, double z)
    {
        float d = Limit;
        foreach (CaveMouth m in mouths) d = MathF.Min(d, MouthDensity(m, x, y, z));
        return d;
    }

    /// <summary>A small fixed sequence of numbers from a seed: a mouth's spot is a pure function of its cell.</summary>
    struct Rng
    {
        ulong _s;
        public Rng(ulong seed) => _s = seed;
        public float NextFloat()
        {
            _s += 0x9E3779B97F4A7C15UL;
            ulong z = _s;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return (z >> 40) / (float)(1UL << 24);
        }
    }
}
