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
/// Per-block hit points. Each hit loses the block's armour (a flat amount) and then its
/// resistance to the damage type. A block at zero is gone, and so is whatever it did: a
/// destroyed thruster no longer pushes. Sections cut off from the core split into wrecks.
/// </summary>
public static class Damage
{
    public static float Effective(in BlockStats stats, DamageType type, float amount) =>
        MathF.Max(0f, amount - stats.Armor) * (1f - stats.Resist[type]);

    /// <summary>Damages one block. Does not split; call <see cref="Connectivity.SplitOff"/> after a volley.</summary>
    public static void Hit(Structure s, Int3 p, DamageType type, float amount, DamageReport report)
    {
        if (!s.TryGet(p, out Block b)) return;
        float dealt = Effective(s.StatsOf(b), type, amount);
        report.BlocksHit++;
        if (dealt <= 0f) return;
        report.Dealt += MathF.Min(dealt, b.Hp);
        b.Hp -= dealt;
        if (b.Hp > 0f)
        {
            s.Set(p, b);
            return;
        }
        bool core = s.TypeOf(b).IsCore;
        s.Clear(p);
        report.Destroyed.Add((p, b));
        if (core) s.Owner = null; // ownership lives in the core
    }

    /// <summary>
    /// An explosion centred at a point in cell units, falling off linearly to nothing at
    /// <paramref name="radius"/> cells. Splits the structure afterwards.
    /// </summary>
    public static DamageReport Blast(Structure s, Vector3 centre, float radius, DamageType type, float amount)
    {
        var report = new DamageReport();
        int r = (int)MathF.Ceiling(radius);
        Int3 c = Int3.Round(centre);
        for (int x = -r; x <= r; x++)
            for (int y = -r; y <= r; y++)
                for (int z = -r; z <= r; z++)
                {
                    Int3 p = c + new Int3(x, y, z);
                    float distance = Vector3.Distance(p.ToVector3(), centre);
                    if (distance > radius) continue;
                    Hit(s, p, type, amount * (1f - distance / radius * 0.75f), report);
                }
        report.Wrecks.AddRange(Connectivity.SplitOff(s));
        return report;
    }
}
