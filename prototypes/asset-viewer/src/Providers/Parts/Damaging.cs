using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Structures;
using Godot;
using NV = System.Numerics.Vector3;
using Structure = GalHack.Structures.Structure;

namespace AssetViewer.Providers.Parts;

/// <summary>
/// The game's damage, played on a structure: hits that wear a block down through its scuffed,
/// cracked and holed looks until it is gone, a blast, and the repair that puts it back. It calls the
/// real <see cref="Damage"/> rules and the slice's own <see cref="DamageFx"/> shows the sparks, chips,
/// marks and smoke; nothing is drawn here.
/// </summary>
public static class Damaging
{
    static readonly NV[] Sides = { NV.UnitY, NV.UnitX, -NV.UnitZ, -NV.UnitX, NV.UnitZ, NV.UnitY };

    /// <summary>Adds "Hits until destroyed", "Blast" and "Repair" to a structure's animations.</summary>
    public static void Add(StructureAsset a)
    {
        Structure s = a.Structure;
        var saved = s.Blocks().ToDictionary(x => x.pos, x => x.block);
        var rng = new Random(1);
        float clock = 0f;
        int shot = 0;
        float wait = 0f;

        void Restore()
        {
            foreach (var (pos, block) in saved) s.Set(pos, block);
            s.CargoSu = 0;
            shot = 0;
            wait = 0f;
        }

        Int3 Target()
        {
            var cells = s.Blocks().Select(x => x.pos).ToList();
            if (cells.Count == 0) return Int3.Zero;
            return cells[rng.Next(cells.Count)];
        }

        a.AddAnimation("Hits until destroyed", () => { Restore(); clock = 0.4f; }, dt =>
        {
            clock -= dt;
            if (clock > 0f) return;
            if (s.Count == 0)
            {
                Restore();
                clock = 0.8f;
                return;
            }
            Int3 at = Target();
            if (!s.TryGet(at, out Block b)) return;
            BlockStats st = s.StatsOf(b);
            // Each hit takes about a quarter of the block's hit points after armour and resistance.
            float amount = st.MaxHp * 0.27f / Math.Max(0.05f, 1f - st.Resist.Kinetic) + st.Armor;
            NV side = Sides[shot++ % Sides.Length];
            var report = new DamageReport();
            Damage.Hit(s, at, DamageType.Kinetic, amount, report, at.ToVector3() + side * 0.5f, side);
            clock = 0.9f;
            if (s.Count == 0) clock = 1.8f;
        });

        a.AddAnimation("Blast", () => { Restore(); clock = 0.5f; }, dt =>
        {
            clock -= dt;
            if (clock > 0f) return;
            if (s.Count == 0 || s.Blocks().Count() < saved.Count / 3)
            {
                Restore();
                clock = 1.2f;
                return;
            }
            var (min, max) = s.Bounds();
            NV centre = new NV((min.X + max.X) * 0.5f, (min.Y + max.Y) * 0.5f, (min.Z + max.Z) * 0.5f) + new NV((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() * 0.5f, (float)rng.NextDouble() - 0.5f) * Math.Max(1, max.X - min.X);
            float radius = Math.Max(1.2f, (max.X - min.X + max.Z - min.Z) * 0.16f);
            float power = s.Blocks().Max(x => s.StatsOf(x.block).MaxHp) * 0.5f;
            Damage.BlastInto(s, centre, radius, DamageType.Explosive, power, new DamageReport());
            clock = 1.3f;
        });

        a.AddAnimation("Repair", () =>
        {
            // Wears everything down first, then mends it block by block.
            foreach (var (pos, block) in saved)
            {
                Block b = block;
                b.Hp = s.MaxHp(b) * 0.1f;
                s.Set(pos, b);
            }
            clock = 0.6f;
            wait = 0f;
        }, dt =>
        {
            clock -= dt;
            if (clock > 0f) return;
            foreach (var (pos, block) in saved)
            {
                if (!s.TryGet(pos, out Block b)) { s.Set(pos, block); continue; }
                float max = s.MaxHp(b);
                if (b.Hp >= max) continue;
                s.SetHp(pos, Math.Min(max, b.Hp + max * 0.2f));
            }
            clock = 0.6f;
            wait += 0.6f;
            if (wait > 6f)
                foreach (var (pos, block) in saved)
                {
                    Block b = block;
                    b.Hp = s.MaxHp(b) * 0.1f;
                    s.Set(pos, b);
                }
        });
    }
}
