#nullable enable
using System;
using NV3 = System.Numerics.Vector3;

namespace PoiGenerator.Gen;

/// <summary>
/// Carrion Reavers: wedge-nosed brutes with jagged outlines. Whatever the plan, the shared traits are a great ram of
/// overlapping armour wedges, hulls of different ships welded together (mismatched in size, section and paint, off each
/// other's axis, plates across the seams), trophy pieces of other factions' hulls hung on them, spikes, and oversized
/// drives bolted on unevenly. Three body plans: two halves welded end to end; a smaller hull welded piggyback on a bigger
/// one; a ram sled, half its length the ram, its drives on outriggers.
/// </summary>
public sealed partial class ShipForm
{
    void Reavers()
    {
        switch (R.PickWeighted(Role switch { "warship" => new[] { 1f, 1f, 1.4f }, "freighter" or "miner" => new[] { 1.2f, 1.3f, 0.6f }, _ => new[] { 1.2f, 1f, 1f } }))
        {
            case 1: Plan = "piggyback"; Piggyback(); break;
            case 2: Plan = "ramsled"; RamSled(); break;
            default: Plan = "welded"; Welded(); break;
        }
    }

    /// <summary>A hull of scrap about (u, y, v), half w by h, len long: round or square, with machinery on it.</summary>
    void Hulk(float u, float y, float v, float w, float h, float len, bool round)
    {
        if (round) Taper(Scrap(), v - len / 2, v + len / 2, (u, y), (w, h), (u, y), (w * 0.9f, h * 0.9f), round: true, hab: true);
        else Box(Scrap(), new NV3(u, y, v), new NV3(w, h, len / 2), R.Float(0f, 0.5f), hab: true);
        Clutter(new NV3(u - w, y - h, v - len / 2), new NV3(u + w, y + h, v + len / 2), R.Range(0, 4), R.Between(2, 5));
    }

    /// <summary>A weld between two hulls: a collar at c (its half sizes), the hulls meeting across it along <paramref name="along"/>, plates across the seam round it.</summary>
    void Weld(NV3 c, NV3 half, NV3 along)
    {
        Box("machine", c, half);
        for (int i = R.Between(3, 6); i > 0; i--)
        {
            var r = new NV3(R.Float(-1f, 1f), R.Float(-1f, 1f), R.Float(-1f, 1f));
            NV3 o = r - along * NV3.Dot(r, along);
            if (o.LengthSquared() < 0.01f) continue;
            o = NV3.Normalize(o);
            if (Surface(c, o) is { } p) Plate(Scrap(), p, along, NV3.Cross(along, o), new NV3(M(3f, 5f), M(2f, 4f), 0.9f));
        }
    }

    /// <summary>
    /// The ram on a bow face (half w by h about y): one great wedge from v 0 to v ram flaring back wider and taller than the
    /// face to a raked edge (across or upright), overlapping armour plates layered over it, their edges ragged forward.
    /// </summary>
    void Ram(float ram, float w, float h, float y)
    {
        bool flat = R.Chance(0.6f);
        float rw = w * R.Float(1.1f, 1.4f), rh = h * R.Float(1.1f, 1.4f), ry = y + R.Float(-0.3f, 0.2f) * h, rake = R.Float(0f, 0.5f) * h;
        Taper("trim", 0, ram, (0, ry - rake), flat ? (rw * R.Float(0.5f, 0.8f), 1f) : (1f, rh * R.Float(0.5f, 0.8f)), (0, ry), (rw, rh), chamfer: 0.2f);
        for (int i = R.Between(3, 6); i > 0; i--)
        {
            float tip = R.Float(-3f * K, ram * 0.5f), py = ry + R.Float(-0.7f, 0.7f) * rh, u = R.Float(-0.3f, 0.3f) * rw, bw = rw * R.Float(0.5f, 1.05f), bh = rh * R.Float(0.25f, 0.5f);
            Taper(R.Chance(0.5f) ? "trim" : Scrap(), tip, ram + R.Float(2f, 10f) * K, (u * 0.5f, py * 0.8f), (bw * R.Float(0.3f, 0.7f), 0.7f), (u, py), (bw, bh), chamfer: 0.3f);
        }
    }

    /// <summary>Trophies and spikes over the hull from v0 to v1: spikes outward and raked forward.</summary>
    void Spoils(float v0, float v1, int fewest, int most)
    {
        for (int i = R.Between(1, 4); i > 0; i--) Trophy(R.Float(v0 + M(4f), v1 - M(4f)));
        for (int i = R.Between(fewest, most); i > 0; i--)
        {
            NV3 o = Round(R.Float(0f, MathF.Tau));
            if (Surface(new NV3(0, 0, R.Float(M(2f), v1)), o) is not { } p) continue;
            NV3 d = NV3.Normalize(o + new NV3(0, 0, R.Float(-0.9f, 0.3f)));
            Bar("dark", p - d, p + d * M(5f, 14f), R.Float(1.5f, 2.2f) * K, 0.3f);
        }
    }

    /// <summary>Drives bolted on unevenly behind a stern face at v (half w by h about (u, y)): one oversized, others smaller and off to a side on outriggers.</summary>
    void BoltedDrives(float v, float u, float y, float w, float h, int outriggers)
    {
        float mr = MathF.Min(w, h) * R.Float(0.6f, 0.85f);
        Bell(new NV3(u + R.Float(-0.2f, 0.2f) * w, y + R.Float(-0.2f, 0.2f) * h, v + 1), mr, mr * R.Float(1.1f, 1.5f));
        for (int i = outriggers; i > 0; i--)
        {
            float r = mr * R.Float(0.35f, 0.6f), z = v - M(2f, 8f);
            NV3 o = Round(R.Float(0f, MathF.Tau)), c = new NV3(u, y, 0f) + o * (MathF.Max(w, h) + r + R.Float(0f, 3f) * K + M(0f, 8f) * (outriggers > 2 ? 1 : 0));
            Bar("machine", new NV3(u, y, z - M(3f)) + o * MathF.Min(w, h) * 0.5f, c + new NV3(0, 0, z - M(3f)), 1.6f * K);
            Bell(c + new NV3(0, 0, z), r, r * R.Float(1.2f, 1.7f));
        }
    }

    /// <summary>Welded: two halves of different ships end to end behind the ram, the aft one round or square, bigger or smaller, off the fore's axis. A stubby wedge-nosed brute.</summary>
    void Welded()
    {
        float ram = L * R.Float(0.2f, 0.28f), fl = L * R.Float(0.3f, 0.38f), al = L * R.Float(0.24f, 0.32f);
        float fw = M(10.5f, 14.5f), fh = M(8.5f, 12.5f), v0 = ram - M(4f), vj = v0 + fl;
        Hulk(0, 0, v0 + fl / 2, fw, fh, fl, false);
        float k = R.Chance(0.5f) ? R.Float(0.6f, 0.8f) : R.Float(1.15f, 1.45f), au = R.Float(-0.3f, 0.3f) * fw, ay = R.Float(-0.35f, 0.35f) * fh, aw = fw * k, ah = fh * k * R.Float(0.8f, 1.2f);
        float va = vj + M(2f) + al / 2;
        Hulk(au, ay, va, aw, ah, al, R.Chance(0.45f));
        Weld(new NV3(au / 2, ay / 2, vj + 1), new NV3(MathF.Min(fw, aw) * 0.7f, MathF.Min(fh, ah) * 0.7f, M(2.5f)), NV3.UnitZ);
        Ram(ram, fw, fh, 0);
        Spoils(v0, va + al / 2, 14, 28);
        BoltedDrives(va + al / 2, au, ay, aw, ah, R.Between(1, 2));
        Guns(R.Between(4, 8), v0, va + al / 2);
    }

    /// <summary>Piggyback: a smaller hull of another ship welded on a bigger one's back (or flank), set forward or aft of it, each its own drives; the ram on the big one. Two brutes in one.</summary>
    void Piggyback()
    {
        float ram = L * R.Float(0.18f, 0.25f), bl = L - ram - L * 0.1f, bw = M(10.5f, 14.5f), bh = M(7.5f, 10.5f), v0 = ram - M(4f), bv = v0 + bl / 2;
        Hulk(0, 0, bv, bw, bh, bl, R.Chance(0.3f));
        Ram(ram, bw, bh, 0);
        // The rider: its own section and size, on the back or a flank, forward or aft.
        bool flank = R.Chance(0.3f);
        float side = R.Chance(0.5f) ? 1 : -1, rl = bl * R.Float(0.4f, 0.65f), rw = bw * R.Float(0.45f, 0.7f), rh = bh * R.Float(0.5f, 0.8f), gap = M(2f, 4f);
        float rv = R.Chance(0.5f) ? v0 + rl / 2 + R.Float(0f, 0.15f) * bl : v0 + bl - rl / 2 - R.Float(0f, 0.1f) * bl;
        NV3 rc = flank ? new NV3(side * (bw + gap + rw), R.Float(-0.3f, 0.3f) * bh, rv) : new NV3(R.Float(-0.35f, 0.35f) * bw, bh + gap + rh, rv);
        Hulk(rc.X, rc.Y, rc.Z, rw, rh, rl, R.Chance(0.5f));
        if (flank) Weld(new NV3(side * (bw + gap / 2), rc.Y, rv), new NV3(gap / 2 + M(1.5f), rh * 0.6f, rl * 0.3f), NV3.UnitX);
        else Weld(new NV3(rc.X, bh + gap / 2, rv), new NV3(rw * 0.7f, gap / 2 + M(1.5f), rl * 0.3f), NV3.UnitY);
        Spoils(v0, v0 + bl, 12, 24);
        BoltedDrives(v0 + bl, 0, 0, bw, bh, R.Between(0, 2));
        // The rider's own drive, bolted on its stern.
        float rr = MathF.Min(rw, rh) * R.Float(0.5f, 0.75f);
        Bell(new NV3(rc.X, rc.Y, rv + rl / 2 + 1), rr, rr * R.Float(1.1f, 1.5f));
        Guns(R.Between(4, 8), v0, v0 + bl);
    }

    /// <summary>The ram sled: a ram half its length long, flaring back into a short stubby body, its drives on outriggers of uneven reach. A battering ram with engines.</summary>
    void RamSled()
    {
        float ram = L * R.Float(0.42f, 0.55f), bw = M(9.5f, 13.5f), bh = M(7.5f, 10.5f), bl = L * R.Float(0.22f, 0.3f), v0 = ram - M(4f), by = R.Float(-0.2f, 0.2f) * bh;
        Hulk(0, by, v0 + bl / 2, bw, bh, bl, false);
        Ram(ram, bw, bh, by);
        // Ribs of plate along the ram's spine.
        for (int i = R.Between(2, 5); i > 0; i--)
        {
            float v = R.Float(ram * 0.3f, ram);
            if (Surface(new NV3(0, by, v), NV3.UnitY) is { } p) Plate(Scrap(), p, NV3.UnitZ, NV3.UnitY, new NV3(M(4f, 8f), M(1.5f, 3f), 0.9f));
        }
        Spoils(M(4f), v0 + bl, 18, 32);
        BoltedDrives(v0 + bl, 0, by, bw, bh, R.Between(2, 3));
        Guns(R.Between(4, 8), ram * 0.5f, v0 + bl);
    }

    /// <summary>A trophy: a piece of another faction's hull hung on the brute's flank or back at v.</summary>
    void Trophy(float v)
    {
        NV3 o = Round(R.Range(0, 8) * MathF.PI / 4);
        if (Surface(new NV3(0, 0, v), o) is not { } p) return;
        string paint = Scrap();
        switch (R.Range(0, 5))
        {
            case 0:
                // A Combine slab, ribbed.
                var h = new NV3(M(3.5f, 5.5f), M(3.5f, 5.5f), M(5.5f, 8.5f));
                Box(paint, p + o * (MathF.Abs(o.X) > 0.5f ? h.X : h.Y) * 0.8f, h, 0.2f);
                Box(paint, p + o * (MathF.Abs(o.X) > 0.5f ? h.X : h.Y) * 0.8f, h + new NV3(M(1f), M(1f), M(1f) - h.Z), 0.2f);
                break;
            case 1:
                // An Exchange container stack.
                float ch = M(1.5f);
                for (int i = 0, n = R.Between(2, 4); i < n; i++) Plate(Cargo(), p + o * (ch - 0.3f + i * (2 * ch - 0.2f)), NV3.UnitZ, new NV3(-o.Y, o.X, 0f), new NV3(M(4f, 6f), ch, ch));
                break;
            case 2:
                // A Kessari ring and its spokes.
                float rr = M(4f, 6f);
                NV3 c = p + o * (rr * 0.6f);
                Add(new Hoop { C = c, R = rr, T = MathF.Max(Thin, 0.9f * K) }, paint);
                for (int i = 0; i < 4; i++) Bar("machine", c, c + Round(i * MathF.PI / 2) * rr, 0.8f * K);
                break;
            case 3:
                // A Vharn blade, swept back.
                Plate(paint, p + o * M(6f) + new NV3(0, 0, M(3f)), o + new NV3(0, 0, 0.5f), NV3.UnitZ, new NV3(M(8f), M(3f, 4.5f), 0.9f), 0.3f);
                break;
            default:
                // A Freeholder hab can.
                Can(paint, 2, p + o * M(3.5f), M(3.5f), M(8f, 12f), domes: true);
                break;
        }
    }
}
