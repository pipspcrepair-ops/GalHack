#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using NV3 = System.Numerics.Vector3;

namespace PoiGenerator.Gen;

/// <summary>
/// Which faction's body plans make a ship (each faction's own file: ShipForm.Freeholders.cs and the rest, several plans
/// a faction sharing its traits; docs/ship-forms.md, "Body plans by faction"), and the Husk's wounds.
/// </summary>
public sealed partial class ShipForm
{
    /// <summary>The factions that build big ships of their own; the first four are human.</summary>
    static readonly Faction[] Builders = { Faction.Freeholders, Faction.Meridian, Faction.Exchange, Faction.Reavers, Faction.Kessari, Faction.Vharn };

    /// <summary>
    /// The form of a faction's ship of a role, its plan's share of <paramref name="length"/> m long, from its own seed; a
    /// Husk ship's wounded and overgrown. A faction with no ships of its own flies a human one's. Any length: a fighter's
    /// or a capital ship's (never past the 280 m Empyrion's longest derelicts reach).
    /// </summary>
    public static ShipForm Make(Faction faction, string role, int length, ulong seed, bool husk)
    {
        if (Array.IndexOf(Builders, faction) < 0) faction = Builders[(int)(seed % 4)];
        float share = faction switch { Faction.Meridian => 1.15f, Faction.Exchange => 0.75f, Faction.Reavers => 0.72f, Faction.Kessari => 0.95f, Faction.Vharn => 1.05f, _ => 1f };
        var f = new ShipForm(new Rng(seed), role, Math.Clamp(length * share, 8f, 280f), Math.Clamp(length * share, 8f, 280f) / share / 120f);
        switch (faction)
        {
            case Faction.Meridian: f.Meridian(); break;
            case Faction.Exchange: f.Exchange(); break;
            case Faction.Reavers: f.Reavers(); break;
            case Faction.Kessari: f.Kessari(); break;
            case Faction.Vharn: f.Vharn(); break;
            default: f.Freeholders(); break;
        }
        if (husk) f.Wound();
        f.Settle();
        return f;
    }

    string Scrap() => "scrap" + R.Range(0, 6);
    string Cargo() => "cargo" + R.Range(0, 6);

    // ---- Husks ---------------------------------------------------------------------------------------

    /// <summary>
    /// A Husk ship (DESIGN: a vessel of any faction hijacked by the worm): its prow crumpled from ramming, its drive section
    /// torn open, breaches down its hull with flesh bulging out of them and tendrils crawling over the plating.
    /// </summary>
    void Wound()
    {
        var solid = Prims.Where(p => !p.Cut).ToList();
        float front = solid.Min(p => p.Lo.Z), back = solid.Max(p => p.Hi.Z);
        // The prow crumpled, the drive section torn open, breaches down the hull: every wound bitten from the surface and
        // never deeper than part of the way in, so it tears the plating and never cuts the ship in two.
        var wounds = new List<(NV3 P, NV3 O, float R)>();
        void Bite(float v, float r)
        {
            NV3 o = Round(R.Float(0f, MathF.Tau));
            if (Surface(new NV3(0, 0, v), o) is not { } p) return;
            r = MathF.Min(r, MathF.Max(1.5f, new NV3(p.X, p.Y, 0f).Length() * 0.45f));
            Cut(new Ball { C = p, R = new NV3(r) });
            wounds.Add((p, o, r));
        }
        for (int i = R.Between(2, 4); i > 0; i--) Bite(front + M(1f, 6f), M(5f, 10f));
        int bow = wounds.Count;
        for (int i = R.Between(2, 3); i > 0; i--) Bite(back - M(2f, 10f), M(6f, 12f));
        for (int i = Math.Clamp((int)((back - front) / 18f), 1, 9) + R.Between(-1, 1); i > 0; i--) Bite(R.Float(front + M(8f), back - M(8f)), M(3f, 6f));
        // What grows over it all, once the cutting is done (nothing cut after it, nothing left floating): buckled plates
        // folded over the crumpled prow, flesh in the tears and bulging out of the breaches, tendrils crawling over the
        // plating from them.
        for (int i = R.Between(3, 6); i > 0; i--)
            if (Surface(new NV3(0, 0, front + M(3f, 14f)), Round(R.Float(0f, MathF.Tau))) is { } p)
                Plate(Scrap(), p, new NV3(R.Float(-1f, 1f), R.Float(-1f, 1f), R.Float(-1f, 1f)), new NV3(R.Float(-1f, 1f), R.Float(-1f, 1f), 1f), new NV3(M(2f, 4f), M(2f, 4f), 0.8f));
        foreach (var (p, o, r) in wounds.Skip(bow))
        {
            Flesh(p, o, r);
            for (int k = R.Between(1, 3); k > 0; k--)
            {
                NV3 a = p, dir = NV3.Normalize(new NV3(-o.Y, o.X, 0f) * (R.Chance(0.5f) ? 1 : -1) + new NV3(0, 0, R.Float(-1f, 1f)));
                for (int s = 0; s < 4; s++)
                {
                    NV3 b = a + dir * M(3f, 6f), bo = new(b.X, b.Y, 0f);
                    if (bo.LengthSquared() < 0.01f || Surface(new NV3(0, 0, b.Z), NV3.Normalize(bo)) is not { } on) break;
                    Bar("flesh", a, on, (1.3f - s * 0.15f) * K, (1.1f - s * 0.15f) * K);
                    a = on;
                    dir = NV3.Normalize(dir + new NV3(R.Float(-0.5f, 0.5f), R.Float(-0.5f, 0.5f), R.Float(-0.5f, 0.5f)));
                }
            }
        }
    }

    /// <summary>A mass of flesh filling a hole about p and bulging out of it along o, every lump overlapping the hole's rim, worms rooted in each lump.</summary>
    void Flesh(NV3 p, NV3 o, float r)
    {
        for (int k = R.Between(3, 6); k > 0; k--)
        {
            float fr = R.Float(MathF.Max(M(2.5f), r * 0.7f), r * 1.1f + M(1f));
            NV3 jitter = new NV3(R.Float(-1f, 1f), R.Float(-1f, 1f), R.Float(-1f, 1f)) * r * 0.35f;
            NV3 c = p + o * fr * R.Float(0.1f, 0.45f) + jitter, rad = new NV3(fr * R.Float(0.8f, 1.2f), fr * R.Float(0.8f, 1.2f), fr * R.Float(0.8f, 1.4f));
            Sphere("flesh", c, rad);
            // (Its worms from a stream of their own, so the form's own rolls run on as they were.)
            Rng w = R.Fork("worms" + Prims.Count);
            for (int n = w.Between(2, 6); n > 0; n--)
            {
                NV3 dir = NV3.Normalize(o + new NV3(w.Float(-1f, 1f), w.Float(-1f, 1f), w.Float(-1f, 1f)) * 0.9f);
                float least = MathF.Min(rad.X, MathF.Min(rad.Y, rad.Z));
                Worms.Add((c + dir * least * 0.8f, dir, MathF.Max(2f, w.Float(4f, 18f) * K), MathF.Max(0.25f, w.Float(0.3f, 1.1f) * K)));
            }
        }
    }
}
