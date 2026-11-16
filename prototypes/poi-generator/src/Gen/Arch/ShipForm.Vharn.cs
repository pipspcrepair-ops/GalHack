#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using NV3 = System.Numerics.Vector3;

namespace PoiGenerator.Gen;

/// <summary>
/// Vharn Dominion: alien ships, insect-like but never animals: no legs, no feelers, no limbs. Whatever the plan, the
/// shared traits are bulbous segments of dark carapace joined by narrow waists (a ridge or a shell on each), long blades
/// and fins swept hard back, unevenly port against starboard, a needle prow or heavy mandible forks, a cluster of
/// oversized drives in the tail. Three body plans: the wasp (a head, a thorax and a tapering abdomen); the beetle (a broad
/// shield and a great abdomen under two splayed shell halves); the centipede (a long undulating chain of small segments,
/// a pair of swept blades on each).
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
        if (hab) rad = new NV3(Roomy(rad.X, 8f), Roomy(rad.Y, 7.5f), rad.Z);
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

    /// <summary>The prow on the head: a needle (needle m long), or heavy mandible forks, short prongs one a little longer than the other.</summary>
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
            float k = R.Float(0.85f, 1.2f);
            NV3 a = head.C + new NV3(s * head.R.X * 0.5f, -head.R.Y * 0.25f, -head.R.Z * 0.5f);
            NV3 b = a + new NV3(s * head.R.X * 0.25f, 0, -L * 0.05f * k), c = b + new NV3(-s * head.R.X * 0.3f, 0, -L * 0.035f * k);
            Bar("trim", a, b, head.R.X * 0.32f, head.R.X * 0.24f);
            Bar("trim", b, c, head.R.X * 0.24f, 0.4f);
        }
    }

    /// <summary>Ventral fins under a segment, swept hard back, one each side a little off straight down, never alike.</summary>
    void Fins((NV3 C, NV3 R) seg)
    {
        foreach (int s in new[] { -1, 1 })
            Blade(seg.C, seg.R, -MathF.PI / 2 + s * R.Float(0.35f, 0.7f), L * R.Float(0.1f, 0.18f), M(7f, 11f));
    }

    /// <summary>The tail's drives: a cluster of two to four oversized bells in long housings, spread wider than the tail tip they grow from.</summary>
    void TailDrives((NV3 C, NV3 R) tail)
    {
        NV3 end = tail.C + new NV3(0, 0, tail.R.Z * 0.6f);
        int nb = R.Between(2, 4);
        float br = tail.R.X * R.Float(0.45f, 0.6f), a0 = R.Float(0f, MathF.Tau);
        for (int k = 0; k < nb; k++)
        {
            NV3 off = Round(a0 + k * MathF.Tau / nb) * br * R.Float(1f, 1.3f);
            float r = br * R.Float(0.8f, 1.1f);
            Bell(end + off + new NV3(0, 0, tail.R.Z * 0.4f + M(2f)), r, r * R.Float(1.5f, 2f), reach: tail.R.Z * 0.8f + M(4f));
        }
    }

    /// <summary>Blades swept hard back from the given segments: a different count port and starboard, a dorsal fin now and then.</summary>
    void Blades(List<(NV3 C, NV3 R)> from, float fewest, float most, float chord0, float chord1)
    {
        int port = R.Between(1, 3), star = port == 1 ? R.Between(2, 3) : port == 3 ? R.Between(1, 2) : R.Chance(0.5f) ? 1 : 3;
        var roots = new List<float>();
        for (int i = 0; i < port; i++) roots.Add(MathF.PI + R.Float(-0.3f, 0.3f));
        for (int i = 0; i < star; i++) roots.Add(R.Float(-0.3f, 0.3f));
        foreach (float a in roots)
        {
            var (c, rad) = R.Pick(from);
            Blade(c, rad, a, L * R.Float(fewest, most), M(chord0, chord1));
        }
        // A dorsal fin: short, broad and swept.
        if (R.Chance(0.5f))
        {
            var (c, rad) = R.Pick(from);
            Blade(c, rad, MathF.PI / 2 + R.Float(-0.2f, 0.2f), L * R.Float(0.08f, 0.14f), M(chord0, chord1) * 1.4f);
        }
    }

    /// <summary>The wasp: a head, a thorax and an abdomen of one to four segments tapering to its tail, the abdomen hanging off the line. A wasp at range.</summary>
    void Wasp()
    {
        int m = R.Between(3, 6);
        float needle = R.Chance(0.55f) ? L * R.Float(0.12f, 0.2f) : 0f, waist = M(3f, 6f), fy = R.Float(0.7f, 1f);
        var share = new List<float> { R.Float(0.7f, 1f), R.Float(1.2f, 1.6f) };
        for (int i = 2; i < m; i++) share.Add(i == 2 ? R.Float(2f, 2.6f) : share[^1] * R.Float(0.6f, 0.85f));
        float scale = (L * 0.88f - needle - waist * (m - 1)) / share.Sum(), sag = L * R.Float(-0.06f, 0.12f), lift = L * R.Float(-0.02f, 0.04f), v = needle;
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
        if (R.Chance(0.5f)) Fins(segs[1]);
        TailDrives(segs[^1]);
        Guns(R.Between(2, 4), segs[1].C.Z - segs[1].R.Z, segs[^1].C.Z);
    }

    /// <summary>The beetle: a small head, a broad shield of a thorax and a great wide abdomen under two shell halves splayed apart unevenly, mandibles, short broad blades and fins. A broad dark shell at range.</summary>
    void Beetle()
    {
        float fy = R.Float(0.55f, 0.75f), waist = M(2f, 4f), v = L * 0.08f, sag = L * R.Float(-0.02f, 0.05f);
        float hl = L * R.Float(0.11f, 0.15f), tl = L * R.Float(0.17f, 0.23f), al = L * 0.84f - v - hl - tl - 2 * waist;
        var segs = new List<(NV3 C, NV3 R)>
        {
            Segment(new NV3(0, sag * 0.5f, v + hl / 2), new NV3(hl * 0.55f, hl * 0.55f * fy, hl / 2), false),
            Segment(new NV3(0, 0, v + hl + waist + tl / 2), new NV3(tl * R.Float(0.7f, 0.9f), tl * 0.45f, tl / 2), true),
        };
        var ab = Segment(new NV3(0, -sag, v + hl + tl + 2 * waist + al / 2), new NV3(al * R.Float(0.34f, 0.42f), al * R.Float(0.34f, 0.42f) * fy, al / 2), true);
        segs.Add(ab);
        Waists(segs);
        // The shell halves over the abdomen, each splayed its own way.
        foreach (int s in new[] { -1, 1 })
        {
            float splay = R.Float(0f, 0.3f), lift = R.Float(0.25f, 0.45f);
            Sphere("trim", ab.C + new NV3(s * ab.R.X * (0.4f + splay), ab.R.Y * lift, -ab.R.Z * R.Float(0f, 0.12f)), new NV3(ab.R.X * 0.62f, ab.R.Y * 0.72f, ab.R.Z * R.Float(0.85f, 1f)));
        }
        Prow(segs[0], 0f);
        Blades(segs.GetRange(1, 1), 0.14f, 0.24f, 8f, 13f);
        Fins(segs[1]);
        TailDrives(ab);
        Guns(R.Between(2, 4), segs[1].C.Z - segs[1].R.Z, ab.C.Z);
    }

    /// <summary>The centipede: a long chain of small segments winding off the line, tapering to the tail, a pair of short swept blades of uneven length on each. A long segmented thing at range.</summary>
    void Centipede()
    {
        int m = R.Between(7, 10);
        float needle = R.Chance(0.5f) ? L * R.Float(0.08f, 0.14f) : 0f, waist = M(1.5f, 3f), fy = R.Float(0.65f, 0.9f), seg = (L * 0.86f - needle - waist * (m - 1)) / m;
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

    /// <summary>A blade from a segment's flank at angle a round the axis: pieces swept hard back from the root and further back each, its chord narrowing to the tip.</summary>
    void Blade(NV3 c, NV3 rad, float a, float len, float chord)
    {
        NV3 o = Round(a), root = c + new NV3(o.X * rad.X * 0.8f, o.Y * rad.Y * 0.8f, R.Float(-0.2f, 0.2f) * rad.Z);
        int pieces = R.Between(3, 5);
        float piece = len / pieces, sweep = R.Float(0.15f, 0.3f), droop = R.Float(-0.06f, 0.06f);
        NV3 dir = NV3.Normalize(o + new NV3(0, 0, R.Float(0.6f, 1f))), n = NV3.Normalize(NV3.Cross(o, NV3.UnitZ));
        for (int i = 0; i < pieces; i++)
        {
            NV3 end = root + dir * piece;
            Plate("trim", (root + end) / 2, dir, NV3.Cross(n, dir), new NV3(piece / 2 + 0.8f, chord / 2, 0.8f), 0.8f);
            root = end;
            chord *= 0.8f;
            dir = NV3.Normalize(dir + new NV3(0, 0, sweep) + n * droop);
        }
    }
}
