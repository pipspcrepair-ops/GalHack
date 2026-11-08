using System;
using System.Collections.Generic;
using System.Numerics;

namespace GalHack.Structures;

public sealed class DamageReport
{
    public int BlocksHit;
    public float Dealt;
    public readonly List<(Int3 pos, Block block)> Destroyed = new();
    public readonly List<Structure> Wrecks = new();
}

/// <summary>
/// One block taking a hit, for whoever draws it. <see cref="Point"/> is on the block in cell units
/// and <see cref="Outward"/> faces the way the hit came from (zero when nobody knows).
/// </summary>
public readonly record struct BlockHit(Int3 Cell, float Dealt, float MaxHp, bool Destroyed, Vector3 Point, Vector3 Outward);

/// <summary>
/// Per-block hit points. Each hit loses the block's armour (a flat amount) and then its
/// resistance to the damage type. A block at zero is gone, and so is whatever it did: a
/// destroyed thruster no longer pushes. Sections cut off from the core split into wrecks.
/// </summary>
public static class Damage
{
    /// <summary>The widest a crash spreads, in cells from where it struck.</summary>
    public const float MaxCrashRadius = 6f;

    public static float Effective(in BlockStats stats, DamageType type, float amount) =>
        MathF.Max(0f, amount - stats.Armor) * (1f - stats.Resist[type]);

    /// <summary>
    /// How far gone a block looks: 0 healthy, 1 scuffed, 2 cracked and dented, 3 holed. A live
    /// block with no hit points was placed by hand without any, not shot to nothing: it is whole.
    /// </summary>
    public static int Bucket(float hp, float maxHp)
    {
        if (hp <= 0f || maxHp <= 0f) return 0;
        float f = hp / maxHp;
        return f > 0.75f ? 0 : f > 0.5f ? 1 : f > 0.25f ? 2 : 3;
    }

    /// <summary>Damages one block. Does not split; call <see cref="Connectivity.SplitOff"/> after a volley.</summary>
    public static void Hit(Structure s, Int3 p, DamageType type, float amount, DamageReport report) =>
        Hit(s, p, type, amount, report, p.ToVector3(), Vector3.Zero);

    /// <summary>The same, knowing where it struck (cell units) and which way the hit came from, for sparks and marks.</summary>
    public static void Hit(Structure s, Int3 p, DamageType type, float amount, DamageReport report, Vector3 point, Vector3 outward)
    {
        if (!s.TryGet(p, out Block b)) return;
        Apply(s, p, b, Effective(s.StatsOf(b), type, amount), report, point, outward);
    }

    static void Apply(Structure s, Int3 p, Block b, float dealt, DamageReport report, Vector3 point, Vector3 outward)
    {
        report.BlocksHit++;
        if (dealt <= 0f) return;
        float max = s.MaxHp(b);
        float lost = MathF.Min(dealt, b.Hp);
        report.Dealt += lost;
        b.Hp -= dealt;
        bool gone = b.Hp <= 0f;
        s.NoteHit(new BlockHit(p, lost, max, gone, point, outward));
        if (!gone)
        {
            s.SetHp(p, b.Hp);
            return;
        }
        bool core = s.TypeOf(b).IsCore;
        // A taller block (a door and its top) is one piece: when one cell of it goes, the rest goes with it.
        foreach (Int3 q in Building.PieceCells(s, p))
            if (q != p && s.TryGet(q, out Block other)) { s.Clear(q); report.Destroyed.Add((q, other)); }
        s.Clear(p);
        report.Destroyed.Add((p, b));
        if (core) s.Owner = null; // ownership lives in the core
    }

    /// <summary>
    /// A crash: <paramref name="hp"/> hit points of impact, poured into the blocks around where
    /// it struck (cell units), nearest hardest. It spreads wider the harder it is, in proportion
    /// to how tough the blocks there are. A crash isn't one round on one plate, so it isn't
    /// stopped by each block's flat armour; its kinetic resistance still counts. Does not split.
    /// <paramref name="into"/> is the unit direction into the structure from the point.
    /// </summary>
    public static void Crash(Structure s, Vector3 point, Vector3 into, float hp, DamageReport report)
    {
        if (s.Count == 0 || hp <= 0f) return;
        Vector3 centre = point + into * 0.5f;
        Int3 nearest = NearestBlock(s, centre);
        float distance = Vector3.Distance(nearest.ToVector3(), centre);
        // A foot or a gear leg can strike well away from any block: the nearest takes it.
        if (distance > 1.5f) centre = nearest.ToVector3();

        float radius = Math.Clamp(1.5f + MathF.Cbrt(hp / MathF.Max(1f, s.MaxHp(s.Get(nearest)))), 1.5f, MaxCrashRadius);
        var struck = new List<(Int3 p, float weight)>();
        float total = 0f;
        int r = (int)MathF.Ceiling(radius);
        Int3 c = Int3.Round(centre);
        for (int x = -r; x <= r; x++)
            for (int y = -r; y <= r; y++)
                for (int z = -r; z <= r; z++)
                {
                    Int3 q = c + new Int3(x, y, z);
                    if (!s.Has(q)) continue;
                    float d = Vector3.Distance(q.ToVector3(), centre);
                    if (d > radius) continue;
                    float weight = 1f - d / radius * 0.75f;
                    weight *= weight; // concentrated where it struck, so the worst of it shows there
                    struck.Add((q, weight));
                    total += weight;
                }

        foreach (var (q, weight) in struck)
        {
            Block b = s.Get(q);
            float dealt = hp * weight / total * (1f - s.StatsOf(b).Resist[DamageType.Kinetic]);
            Apply(s, q, b, dealt, report, q.ToVector3() - into * 0.5f, -into);
        }
    }

    static Int3 NearestBlock(Structure s, Vector3 point)
    {
        Int3 c = Int3.Round(point);
        if (s.Has(c)) return c;
        Int3 best = default;
        float closest = float.MaxValue;
        foreach (var (p, _) in s.Blocks())
        {
            float d = Vector3.DistanceSquared(p.ToVector3(), point);
            if (d < closest)
            {
                closest = d;
                best = p;
            }
        }
        return best;
    }

    /// <summary>
    /// An explosion centred at a point in cell units, falling off linearly to nothing at
    /// <paramref name="radius"/> cells. Splits the structure afterwards.
    /// </summary>
    public static DamageReport Blast(Structure s, Vector3 centre, float radius, DamageType type, float amount)
    {
        var report = new DamageReport();
        BlastInto(s, centre, radius, type, amount, report);
        report.Wrecks.AddRange(Connectivity.SplitOff(s));
        return report;
    }

    /// <summary>The same blast into a running report, without splitting (a frame's hits are split once, together).</summary>
    public static void BlastInto(Structure s, Vector3 centre, float radius, DamageType type, float amount, DamageReport report)
    {
        int r = (int)MathF.Ceiling(radius);
        Int3 c = Int3.Round(centre);
        for (int x = -r; x <= r; x++)
            for (int y = -r; y <= r; y++)
                for (int z = -r; z <= r; z++)
                {
                    Int3 p = c + new Int3(x, y, z);
                    float distance = Vector3.Distance(p.ToVector3(), centre);
                    if (distance > radius) continue;
                    Vector3 away = p.ToVector3() - centre;
                    Hit(s, p, type, amount * (1f - distance / radius * 0.75f), report, p.ToVector3(),
                        away.LengthSquared() > 1e-4f ? Vector3.Normalize(away) : Vector3.Zero);
                }
    }
}
