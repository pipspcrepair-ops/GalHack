using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Structures;
using NV = System.Numerics.Vector3;
using Structure = GalHack.Structures.Structure;

namespace AssetViewer.Providers.Parts;

/// <summary>
/// The states a vessel is shown in, all made with the game's own rules: paint with grime (the building
/// paint system), hit points (progressive damage looks), and <see cref="Damage"/> (hits, blasts and the
/// crash rule). The game has no rust or soot state for vessels; these are its paint and damage applied.
/// </summary>
public static class VesselStates
{
    const uint Rust = 0x8a4b27, Soot = 0x1d1c1b;

    public static void Apply(Structure s, string state, ulong seed)
    {
        var rng = new Random((int)(seed & 0x7fffffff) ^ 0x5bd1e995);
        switch (state)
        {
            case "Worn":
                Repaint(s, null, 0f, 2, FaceTexture.Panel);
                Scuff(s, rng, 0.3f, 0.45f, 0.8f);
                break;
            case "Rusted":
                Repaint(s, Rust, 0.65f, 3, FaceTexture.Plates);
                Scuff(s, rng, 0.5f, 0.3f, 0.75f);
                break;
            case "Burnt":
                Repaint(s, Soot, 0.8f, 3, FaceTexture.Panel);
                Scuff(s, rng, 0.8f, 0.15f, 0.5f);
                foreach (var (pos, b) in s.Blocks().ToList())
                    if (!s.TypeOf(b).IsArmor && rng.NextDouble() < 0.7) s.SetHp(pos, s.MaxHp(b) * (0.15f + 0.1f * (float)rng.NextDouble()));
                Remove(s, rng, 0.06);
                break;
            case "Damaged":
                Repaint(s, null, 0f, 1, FaceTexture.Panel);
                Shoot(s, rng, Math.Max(10, s.Count / 7));
                break;
            case "Wrecked":
                Repaint(s, Soot, 0.4f, 2, FaceTexture.Panel);
                Crash(s, rng);
                break;
        }
        s.DirtyChunks.Clear();
        foreach (Int3 c in s.Chunks.Keys) s.DirtyChunks.Add(c);
    }

    /// <summary>Every face's paint becomes the same paint grimed and tinted: bare faces take their material's colour first.</summary>
    static void Repaint(Structure s, uint? tint, float amount, int grime, FaceTexture texture)
    {
        var map = new Dictionary<ushort, ushort>();
        ushort Remap(ushort slot, BlockType t)
        {
            ushort key = slot == 0 ? (ushort)(0xf000 | t.Index) : slot;
            if (map.TryGetValue(key, out ushort done)) return done;
            Paint p = slot == 0 ? new Paint(t.Color, t.Accent, Pattern.None, t.Texture, 0, 0) : s.Palette[slot];
            if (p.IsLamp) return map[key] = slot;
            uint baseColour = tint is uint c ? Mix(p.Base, c, amount) : p.Base;
            Paint q = p with { Base = baseColour, Grime = (byte)Math.Max(p.Grime, grime), Texture = tint != null && p.Texture == FaceTexture.Panel ? texture : p.Texture };
            return map[key] = s.Palette.Intern(q, "state");
        }
        foreach (var (pos, b0) in s.Blocks().ToList())
        {
            BlockType t = s.TypeOf(b0);
            if (!t.IsArmor && t.Color == 0) continue;
            Block b = b0;
            foreach (Dir d in Dirs.All) b.Paint[d] = Remap(b.Paint[d], t);
            s.Set(pos, b);
        }
    }

    static uint Mix(uint a, uint b, float t)
    {
        uint Ch(int shift) => (uint)Math.Clamp((int)Math.Round(((a >> shift) & 255) * (1 - t) + ((b >> shift) & 255) * t), 0, 255);
        return (Ch(16) << 16) | (Ch(8) << 8) | Ch(0);
    }

    /// <summary>A share of blocks worn down to a scuffed, cracked or holed look.</summary>
    static void Scuff(Structure s, Random rng, double share, float low, float high)
    {
        foreach (var (pos, b) in s.Blocks().ToList())
            if (rng.NextDouble() < share) s.SetHp(pos, s.MaxHp(b) * (low + (high - low) * (float)rng.NextDouble()));
    }

    static void Remove(Structure s, Random rng, double share)
    {
        foreach (var (pos, b) in s.Blocks().ToList())
            if (!s.TypeOf(b).IsCore && rng.NextDouble() < share) s.Clear(pos);
    }

    /// <summary>Heavy kinetic hits on random blocks, as gunfire does.</summary>
    static void Shoot(Structure s, Random rng, int hits)
    {
        var cells = s.Blocks().Select(x => x.pos).ToList();
        var report = new DamageReport();
        for (int i = 0; i < hits && cells.Count > 0; i++)
        {
            Int3 at = cells[rng.Next(cells.Count)];
            if (!s.TryGet(at, out Block b)) continue;
            BlockStats st = s.StatsOf(b);
            float amount = st.MaxHp * (0.4f + 0.6f * (float)rng.NextDouble()) / Math.Max(0.05f, 1f - st.Resist.Kinetic) + st.Armor;
            NV side = new NV((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.2f, (float)rng.NextDouble() - 0.5f);
            side = side.LengthSquared() < 1e-4f ? NV.UnitY : NV.Normalize(side);
            Damage.Hit(s, at, DamageType.Kinetic, amount, report, at.ToVector3() + side * 0.5f, side);
        }
    }

    /// <summary>A crash into the bow (the game's crash rule), blasts through the middle, and whatever is cut off falls away.</summary>
    static void Crash(Structure s, Random rng)
    {
        if (s.Count == 0) return;
        var (min, max) = s.Bounds();
        var report = new DamageReport();
        NV bow = new NV((min.X + max.X) * 0.5f, (min.Y + max.Y) * 0.5f, min.Z - 0.5f);
        float hp = s.Blocks().Sum(x => s.StatsOf(x.block).MaxHp) * 0.1f;
        Damage.Crash(s, bow, NV.UnitZ, hp, report);
        for (int i = 0; i < 3; i++)
        {
            NV c = new NV(min.X + (max.X - min.X) * (float)rng.NextDouble(), min.Y + (max.Y - min.Y) * (float)rng.NextDouble(), min.Z + (max.Z - min.Z) * (float)rng.NextDouble());
            Damage.BlastInto(s, c, Math.Max(1.5f, (max.X - min.X) * 0.25f), DamageType.Explosive, 600f, report);
        }
        Shoot(s, rng, Math.Max(4, s.Count / 30));
        // Sections cut off from the rest are gone (a wreck's loose bits are not kept).
        var pieces = Connectivity.Pieces(s);
        for (int i = 1; i < pieces.Count; i++)
            foreach (Int3 p in pieces[i]) s.Clear(p);
    }
}
