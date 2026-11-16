#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using NV3 = System.Numerics.Vector3;

namespace PoiGenerator.Gen;

/// <summary>
/// Vharn Dominion: alien, insect-like. Whatever the plan, the shared traits are bulbous segments of dark carapace joined
/// by narrow waists (a ridge or a shell on each), long curved blades sweeping back unevenly port against starboard, a
/// needle prow or mandible forks, legs folded under now and then, the drives in the tail. Three body plans: the wasp (a
/// head, a thorax and a tapering abdomen); the beetle (a broad shield and a great abdomen under two splayed shell halves);
/// the centipede (a long undulating chain of small segments, a pair of blades on each).
/// </summary>
public sealed partial class ShipForm
{
    void Vharn()
    {
        switch (R.PickWeighted(Role switch { "warship" => new[] { 1.2f, 0.8f, 1.2f }, "freighter" or "miner" => new[] { 1f, 1.4f, 0.8f }, _ => new[] { 1.2f, 1f, 1f } }))
        {
            case 1: Plan = "beetle"; Beetle(); break;
            case 2: Plan = "centipede"; Centipede(); break;
            default: Plan = "wasp"; Wasp(); break;
        }
    }

    /// <summary>A segment of carapace at c (its radii rad): a ridge down its back, or a shell over it.</summary>
    (NV3 C, NV3 R) Segment(NV3 c, NV3 rad, bool hab)
    {
        Sphere("hull", c, rad, hab);
        if (R.Chance(0.6f)) Plate("trim", c + new NV3(0, rad.Y * 0.85f, 0), NV3.UnitZ, NV3.UnitY, new NV3(rad.Z * 0.84f, rad.Y * 0.35f, 0.9f));
        else Sphere("trim", c + new NV3(0, rad.Y * 0.25f, -rad.Z * 0.16f), new NV3(rad.X * 1.06f, rad.Y * 0.85f, rad.Z * 0.75f));
        return (c, rad);
    }

    /// <summary>The narrow waists joining each segment to the next.</summary>
    void Waists(List<(NV3 C, NV3 R)> segs)
    {
        for (int i = 0; i + 1 < segs.Count; i++)
            Bar("machine", segs[i].C + new NV3(0, 0, segs[i].R.Z - 1), segs[i + 1].C - new NV3(0, 0, segs[i + 1].R.Z - 1), R.Float(1.5f, 2.5f) * K);
    }

    /// <summary>The prow on the head: a needle (needle m long), or mandible forks of uneven length curving in.</summary>
    void Prow((NV3 C, NV3 R) head, float needle)
    {
        NV3 nose = head.C - new NV3(0, 0, head.R.Z);
        if (needle > 0f)
        {
            Bar("trim", nose + new NV3(0, 0, M(2f)), nose - new NV3(0, R.Float(-0.1f, 0.1f) * needle, needle), R.Float(1.6f, 2.4f) * K, 0.4f);
            return;
        }
        foreach (int s in new[] { -1, 1 })
        {
            float k = R.Float(0.8f, 1.25f);
            NV3 a = head.C + new NV3(s * head.R.X * 0.6f, -head.R.Y * 0.3f, -head.R.Z * 0.6f);
            NV3 b = a + new NV3(s * head.R.X * 0.5f, 0, -L * 0.07f * k), c = b + new NV3(-s * head.R.X * 0.55f, 0, -L * 0.06f * k);
            Bar("trim", a, b, 1.8f * K, 1.4f * K);
            Bar("trim", b, c, 1.4f * K, 0.4f);
        }
    }

    /// <summary>Pairs of legs folded under a segment.</summary>
    void Legs((NV3 C, NV3 R) seg, int pairs)
    {
        var (c, rad) = seg;
        for (int k = 0; k < pairs; k++)
            foreach (int s in new[] { -1, 1 })
            {
                NV3 hip = c + new NV3(s * rad.X * 0.5f, -rad.Y * 0.7f, (k - (pairs - 1) / 2f) * rad.Z * 0.5f);
                NV3 knee = hip + new NV3(s * rad.X * 0.7f, -rad.Y * 0.5f, R.Float(-2f, 2f) * K), foot = knee + new NV3(s * M(1.5f), -rad.Y * 0.7f, M(2f, 6f));
                Bar("dark", hip, knee, 1.3f * K, 1.0f * K);
                Bar("dark", knee, foot, 1.0f * K, 0.5f);
            }
    }

    /// <summary>The drives in the tail segment: one, two or three bells.</summary>
    void TailDrives((NV3 C, NV3 R) tail)
    {
        NV3 end = tail.C + new NV3(0, 0, tail.R.Z - M(2f));
        int nb = R.Between(1, 3);
        float br = tail.R.X * (nb == 1 ? 0.55f : 0.36f);
        for (int k = 0; k < nb; k++)
        {
            NV3 off = nb == 1 ? NV3.Zero : Round(MathF.PI / 2 + k * MathF.Tau / nb) * br * 1.25f;
            Bell(end + off, br, br * R.Float(1.3f, 1.8f));
        }
    }

    /// <summary>Blades sweeping back unevenly from the given segments: a different count port and starboard, a dorsal or ventral one now and then.</summary>
    void Blades(List<(NV3 C, NV3 R)> from, float fewest, float most, float chord0, float chord1)
    {
        int port = R.Between(1, 3), star = port == 1 ? R.Between(2, 3) : port == 3 ? R.Between(1, 2) : R.Chance(0.5f) ? 1 : 3;
        var roots = new List<float>();
        for (int i = 0; i < port; i++) roots.Add(MathF.PI + R.Float(-0.3f, 0.3f));
        for (int i = 0; i < star; i++) roots.Add(R.Float(-0.3f, 0.3f));
        if (R.Chance(0.4f)) roots.Add(MathF.PI / 2 * (R.Chance(0.6f) ? 1 : -1) + R.Float(-0.2f, 0.2f));
        foreach (float a in roots)
        {
            var (c, rad) = R.Pick(from);
            Blade(c, rad, a, L * R.Float(fewest, most), M(chord0, chord1));
        }
    }

    /// <summary>The wasp: a head, a thorax and an abdomen of one to four segments tapering to its tail, the abdomen hanging off the line. A wasp at range.</summary>
    void Wasp()
    {
        int m = R.Between(3, 6);
        float needle = R.Chance(0.55f) ? L * R.Float(0.12f, 0.2f) : 0f, waist = M(3f, 6f), fy = R.Float(0.7f, 1f);
        var share = new List<float> { R.Float(0.7f, 1f), R.Float(1.2f, 1.6f) };
        for (int i = 2; i < m; i++) share.Add(i == 2 ? R.Float(2f, 2.6f) : share[^1] * R.Float(0.6f, 0.85f));
        float scale = (L * 0.94f - needle - waist * (m - 1)) / share.Sum(), sag = L * R.Float(-0.06f, 0.12f), lift = L * R.Float(-0.02f, 0.04f), v = needle;
        var segs = new List<(NV3 C, NV3 R)>();
        for (int i = 0; i < m; i++)
        {
            float len = share[i] * scale, r = MathF.Min(len * (i == 0 ? 0.55f : i == 1 ? 0.45f : 0.38f), L * 0.1f);
            float y = i == 0 ? lift : i == 1 ? 0f : -sag * (i - 1) / Math.Max(1, m - 2);
            segs.Add(Segment(new NV3(0, y, v + len / 2), new NV3(r, r * fy, len / 2), i > 0));
            v += len + waist;
        }
        Waists(segs);
        Prow(segs[0], needle);
        Blades(segs.GetRange(1, Math.Min(2, m - 1)), 0.24f, 0.4f, 6f, 11f);
        if (R.Chance(0.4f)) Legs(segs[1], R.Between(2, 3));
        TailDrives(segs[^1]);
        Guns(R.Between(2, 4), segs[1].C.Z - segs[1].R.Z, segs[^1].C.Z);
    }

    /// <summary>The beetle: a small head, a broad shield of a thorax and a great wide abdomen under two shell halves splayed apart unevenly, mandibles, short thick blades, legs folded under. A broad dark shell at range.</summary>
    void Beetle()
    {
        float fy = R.Float(0.55f, 0.75f), waist = M(2f, 4f), v = L * 0.1f, sag = L * R.Float(-0.02f, 0.05f);
        float hl = L * R.Float(0.11f, 0.15f), tl = L * R.Float(0.17f, 0.23f), al = L * 0.9f - v - hl - tl - 2 * waist;
        var segs = new List<(NV3 C, NV3 R)>
        {
            Segment(new NV3(0, sag * 0.5f, v + hl / 2), new NV3(hl * 0.55f, hl * 0.55f * fy, hl / 2), false),
            Segment(new NV3(0, 0, v + hl + waist + tl / 2), new NV3(tl * R.Float(0.7f, 0.9f), tl * 0.45f, tl / 2), true),
        };
        var ab = Segment(new NV3(0, -sag, v + hl + tl + 2 * waist + al / 2), new NV3(al * R.Float(0.36f, 0.46f), al * R.Float(0.36f, 0.46f) * fy, al / 2), true);
        segs.Add(ab);
        Waists(segs);
        // The shell halves over the abdomen, each splayed its own way.
        foreach (int s in new[] { -1, 1 })
        {
            float splay = R.Float(0f, 0.3f), lift = R.Float(0.25f, 0.45f);
            Sphere("trim", ab.C + new NV3(s * ab.R.X * (0.4f + splay), ab.R.Y * lift, -ab.R.Z * R.Float(0f, 0.12f)), new NV3(ab.R.X * 0.62f, ab.R.Y * 0.72f, ab.R.Z * R.Float(0.85f, 1f)));
        }
        Prow(segs[0], 0f);
        Blades(segs.GetRange(1, 1), 0.12f, 0.22f, 7f, 12f);
        if (R.Chance(0.6f))
        {
            Legs(segs[1], R.Between(1, 2));
            Legs(ab, R.Between(1, 2));
        }
        TailDrives(ab);
        Guns(R.Between(2, 4), segs[1].C.Z - segs[1].R.Z, ab.C.Z);
    }

    /// <summary>The centipede: a long chain of small segments winding off the line, tapering to the tail, a pair of short blades of uneven length on each. A long segmented thing at range.</summary>
    void Centipede()
    {
        int m = R.Between(7, 10);
        float needle = R.Chance(0.5f) ? L * R.Float(0.08f, 0.14f) : 0f, waist = M(1.5f, 3f), fy = R.Float(0.65f, 0.9f), seg = (L * 0.92f - needle - waist * (m - 1)) / m;
        float wave = L * R.Float(0.01f, 0.04f), phase = R.Float(0f, MathF.Tau), v = needle;
        var segs = new List<(NV3 C, NV3 R)>();
        for (int i = 0; i < m; i++)
        {
            float t = i / (m - 1f), len = seg * (1.15f - 0.3f * t), r = MathF.Min(len * 0.55f, L * 0.075f) * (1.05f - 0.35f * t);
            var c = new NV3(wave * MathF.Sin(phase + i * 0.9f) * 0.6f, wave * MathF.Cos(phase + i * 0.9f) * 0.4f, v + len / 2);
            segs.Add(Segment(c, new NV3(r, r * fy, len / 2), i > 0));
            v += len + waist;
        }
        Waists(segs);
        Prow(segs[0], needle);
        float bl = L * R.Float(0.06f, 0.11f);
        for (int i = 1; i < m - 1; i++)
            foreach (int s in new[] { -1, 1 })
            {
                var (c, rad) = segs[i];
                Blade(c, rad, (s > 0 ? 0f : MathF.PI) + R.Float(-0.25f, 0.25f), bl * R.Float(0.6f, 1.4f) * (1f - 0.3f * i / m), M(3f, 5f));
            }
        TailDrives(segs[^1]);
        Guns(R.Between(2, 4), segs[1].C.Z, segs[^1].C.Z);
    }

    /// <summary>A blade from a segment's flank at angle a round the axis: pieces stepping out and sweeping further back each, its chord narrowing to the tip.</summary>
    void Blade(NV3 c, NV3 rad, float a, float len, float chord)
    {
        NV3 o = Round(a), root = c + new NV3(o.X * rad.X * 0.8f, o.Y * rad.Y * 0.8f, R.Float(-0.2f, 0.2f) * rad.Z);
        int pieces = R.Between(3, 5);
        float piece = len / pieces, sweep = R.Float(0.2f, 0.45f), droop = R.Float(-0.1f, 0.1f);
        NV3 dir = NV3.Normalize(o + new NV3(0, 0, sweep * 0.3f)), n = NV3.Normalize(NV3.Cross(o, NV3.UnitZ));
        for (int i = 0; i < pieces; i++)
        {
            NV3 end = root + dir * piece;
            Plate("trim", (root + end) / 2, dir, NV3.Cross(n, dir), new NV3(piece / 2 + 0.8f, chord / 2, 0.8f), 0.8f);
            root = end;
            chord *= 0.78f;
            dir = NV3.Normalize(dir + new NV3(0, 0, sweep) + n * droop);
        }
    }
}
