#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using NV3 = System.Numerics.Vector3;

namespace PoiGenerator.Gen;

/// <summary>The body plans, one a faction (docs/ship-forms.md, "Body plans by faction"), and the Husk's wounds.</summary>
public sealed partial class ShipForm
{
    /// <summary>The factions that build big ships of their own; the first four are human.</summary>
    static readonly Faction[] Builders = { Faction.Freeholders, Faction.Meridian, Faction.Exchange, Faction.Reavers, Faction.Kessari, Faction.Vharn };

    /// <summary>
    /// The form of a faction's ship of a role, its plan's share of <paramref name="length"/> m long, from its own seed; a
    /// Husk ship's wounded and overgrown. A faction with no ships of its own flies a human one's.
    /// </summary>
    public static ShipForm Make(Faction faction, string role, int length, ulong seed, bool husk)
    {
        if (Array.IndexOf(Builders, faction) < 0) faction = Builders[(int)(seed % 4)];
        float share = faction switch { Faction.Meridian => 1.15f, Faction.Exchange => 0.75f, Faction.Reavers => 0.72f, Faction.Kessari => 0.95f, Faction.Vharn => 1.05f, _ => 1f };
        var f = new ShipForm(new Rng(seed), role, Math.Clamp(length * share, 50f, 280f));
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

    // ---- Freeholders ---------------------------------------------------------------------------------

    /// <summary>
    /// Freeholders: a thin spine carrying irregular lumps. A long open truss with mismatched modules clamped round it at
    /// odd angles and in odd sizes (habitat cans, containers, tanks, a cargo cage, radiators), leaning to one side; a cluster
    /// of salvaged drives of different sizes at the stern; a small command pod on the nose.
    /// </summary>
    void Freeholders()
    {
        int th = R.Between(2, 4);
        // The command pod on the nose.
        float pl = R.Between(10, 15), pr = R.Between(4, 6) + 0.5f, py = R.Between(-2, 2);
        Taper("hull", 0, pl, (0, py), (pr * 0.55f, pr * 0.5f), (0, py), (pr, pr * 0.85f), round: R.Chance(0.5f), chamfer: 0.5f, hab: true);
        // The salvaged drives: each its own size, its own housing and its own place on the thrust frame.
        var bells = new List<(float U, float Y, float R)>();
        int want = R.Between(2, 5);
        for (int tries = 0; tries < 80 && bells.Count < want; tries++)
        {
            float r = bells.Count == 0 ? R.Between(4, 7) + 0.5f : R.Between(2, 5), u = R.Float(-1f, 1f) * (th + 6), y = R.Float(-1f, 1f) * (th + 5);
            if (bells.Any(b => MathF.Sqrt((b.U - u) * (b.U - u) + (b.Y - y) * (b.Y - y)) < b.R + r + 3f)) continue;
            bells.Add((u, y, r));
        }
        float big = bells.Max(b => b.R), vt = L - big * 1.4f;
        foreach (var (u, y, r) in bells) Bell(new NV3(u, y, vt - R.Float(0f, 4f)), r, r * R.Float(1.1f, 1.5f));
        float fu0 = bells.Min(b => b.U - b.R), fu1 = bells.Max(b => b.U + b.R), fy0 = bells.Min(b => b.Y - b.R), fy1 = bells.Max(b => b.Y + b.R);
        float fv = vt - 5f - MathF.Max(7f, big * 1.4f);
        Box("machine", new NV3((fu0 + fu1) / 2, (fy0 + fy1) / 2, fv), new NV3((fu1 - fu0) / 2 + 1, (fy1 - fy0) / 2 + 1, 2.5f), 0.3f);
        Clutter(new NV3(-pr, py - pr, 2), new NV3(pr, py + pr * 0.85f, pl), R.Range(2, 4), R.Between(1, 3));
        // The truss from the pod to the frame, now and then jogged off its line amidships where two were joined (a
        // junction box at each joint), and the modules clamped round it, leaning to one side, one of them now and then a
        // great hab can or tank.
        float t0 = pl - 2, t1 = fv - 2;
        int bay = R.Between(5, 7);
        var legs = new List<(float V0, float V1, NV3 Off)>();
        if (R.Chance(0.4f) && t1 - t0 > 60)
        {
            float j0 = MathF.Round(t0 + (t1 - t0) * R.Float(0.25f, 0.4f)), j1 = MathF.Round(t0 + (t1 - t0) * R.Float(0.6f, 0.75f));
            NV3 jog = Round(R.Range(0, 8) * MathF.PI / 4) * R.Between(4, 7);
            jog = new NV3(MathF.Round(jog.X), MathF.Round(jog.Y), 0f);
            legs.Add((t0, j0, NV3.Zero));
            legs.Add((j0, j1, jog));
            legs.Add((j1, t1, NV3.Zero));
            foreach (float j in new[] { j0, j1 })
                Box("machine", new NV3(jog.X / 2, jog.Y / 2, j), new NV3(MathF.Abs(jog.X) / 2 + th + 1.5f, MathF.Abs(jog.Y) / 2 + th + 1.5f, 2.5f), 0.3f);
        }
        else legs.Add((t0, t1, NV3.Zero));
        foreach (var (a0, a1, off) in legs) Truss(a0, a1, th, bay, off.X, off.Y);
        string[] kinds = Role switch
        {
            "liner" => new[] { "can", "can", "can", "tank", "containers", "radiator" },
            "miner" => new[] { "cage", "cage", "tank", "can", "containers", "radiator" },
            "warship" => new[] { "can", "tank", "containers", "radiator", "cage" },
            _ => new[] { "containers", "containers", "cage", "can", "tank", "radiator" },
        };
        float lean = R.Range(0, 8) * MathF.PI / 4, v = t0 + R.Float(1f, 5f);
        bool great = R.Chance(0.4f);
        while (v < t1 - 8)
        {
            float a = R.Chance(0.7f) ? lean + R.Range(-1, 2) * MathF.PI / 4 : R.Range(0, 8) * MathF.PI / 4;
            var leg = legs.First(l => v < l.V1 || l == legs[^1]);
            // (Never across a joint.)
            if (v + 6 > leg.V1 && leg != legs[^1])
            {
                v = leg.V1 + 3;
                continue;
            }
            NV3 off = leg.Off;
            bool huge = great && v > t0 + (t1 - t0) * 0.3f;
            float len = Module(huge ? R.Pick(new[] { "can", "tank" }) : R.Pick(kinds), Round(a), th, v, off, huge);
            great &= !huge;
            if (!R.Chance(0.3f)) v += len + R.Float(1f, 6f);
        }
        // Fuel lines down the truss, a dish on the pod, a few guns.
        Pipe(new NV3(th + 1, -th, t0 + 2), new NV3(th + 1, -th, t1 - 2));
        if (R.Chance(0.6f)) Mast(new NV3(0, py + pr * 0.85f - 1, MathF.Round(pl * 0.6f)), NV3.UnitY, R.Between(4, 8), new NV3(R.Float(-1f, 1f), 0.5f, -1f));
        Guns(R.Between(2, 4), 2, t1);
    }

    /// <summary>One module clamped to the truss (its axis off the ship's by <paramref name="off"/>) out along <paramref name="dir"/> from v, a great one if asked: its length down the truss.</summary>
    float Module(string kind, NV3 dir, int th, float v, NV3 off, bool great = false)
    {
        bool square = MathF.Abs(dir.X) > 0.9f || MathF.Abs(dir.Y) > 0.9f;
        int axis = MathF.Abs(dir.X) > 0.9f ? 0 : 1;
        float root = th + 1.5f, len;
        NV3 side = new(-dir.Y, dir.X, 0f);
        if (kind == "cage" && !square) kind = "containers";
        if (kind == "radiator" && !square) kind = "tank";
        switch (kind)
        {
            case "can":
            {
                float r = great ? R.Between(8, 11) + 0.5f : R.Between(3, 8) + 0.5f;
                string paint = R.Chance(0.5f) ? "hull" : Scrap();
                if (square && R.Chance(0.35f))
                {
                    // Across the truss, sticking out.
                    float reach = R.Between(10, 18);
                    len = 2 * r;
                    Can(paint, axis, dir * (root + reach / 2) + new NV3(off.X, off.Y, v + r), r, reach, domes: true, hab: true);
                }
                else
                {
                    len = great ? R.Between(16, 26) : R.Between(8, 18);
                    Can(paint, 2, dir * (root + r) + new NV3(off.X, off.Y, v + len / 2), r, len, domes: true, hab: true);
                }
                break;
            }
            case "tank":
            {
                float r = great ? R.Between(7, 10) + 0.5f : R.Between(3, 6) + 0.5f;
                if (R.Chance(0.5f))
                {
                    len = 2 * r;
                    Sphere("tank", dir * (root + r) + new NV3(off.X, off.Y, v + r), new NV3(r));
                }
                else
                {
                    len = R.Between(10, 16);
                    Can("tank", 2, dir * (root + r) + new NV3(off.X, off.Y, v + len / 2), r, len - r, domes: true);
                }
                break;
            }
            case "cage":
            {
                // An open frame of struts round a load.
                int hu = R.Between(3, 4), hy = R.Between(3, 4), hv = R.Between(4, 7);
                len = 2 * hv;
                NV3 c = dir * (root + (axis == 0 ? hu : hy)) + new NV3(off.X, off.Y, MathF.Round(v + hv));
                c = new NV3(MathF.Round(c.X), MathF.Round(c.Y), c.Z);
                foreach (int a in new[] { -1, 1 })
                    foreach (int b in new[] { -1, 1 })
                    {
                        Pipe(c + new NV3(a * hu, b * hy, -hv), c + new NV3(a * hu, b * hy, hv));
                        Pipe(c + new NV3(-hu, a * hy, b * hv), c + new NV3(hu, a * hy, b * hv));
                        Pipe(c + new NV3(a * hu, -hy, b * hv), c + new NV3(a * hu, hy, b * hv));
                    }
                Box(Scrap(), c + new NV3(0, -hy + 2, 0), new NV3(hu - 1.5f, 1.5f, hv - 2f));
                break;
            }
            case "radiator":
            {
                len = R.Between(8, 12);
                NV3 at = dir * th + new NV3(off.X, off.Y, v + len / 2);
                Radiators(new NV3(MathF.Round(at.X), MathF.Round(at.Y), MathF.Round(at.Z)), dir, R.Between(2, 3), R.Between(6, 9), len);
                break;
            }
            default:
            {
                // Containers stacked out from the truss, each its own colour.
                var h = new NV3(R.Between(1, 2) + 0.5f, R.Between(1, 2) + 0.5f, R.Between(3, 6) + 0.5f);
                len = 2 * h.Z;
                for (int i = 0, n = R.Between(1, 3); i < n; i++)
                    Plate(Scrap(), dir * (root + h.Y + i * (2 * h.Y + 0.5f)) + side * R.Float(-1f, 1f) + new NV3(off.X, off.Y, v + h.Z), NV3.UnitZ, side, new NV3(h.Z, h.X, h.Y));
                break;
            }
        }
        // The clamp: a collar on the truss's face.
        Box("machine", dir * (th + 0.5f) + new NV3(off.X, off.Y, v + len / 2), new NV3(1.5f, 1.5f, 1.5f));
        return len;
    }

    // ---- Meridian ------------------------------------------------------------------------------------

    /// <summary>
    /// Meridian Combine: a hammer or a dumbbell at range, clean massive slabs. A long thick spine (square or hex) ribbed
    /// with armour collars; an armoured prow block like a hammerhead, layered plates on its brow and chin and its combat
    /// bridge in a blister low in its head or on its flank; a heavier engine block at the stern with four to eight big
    /// bells; sponsons amidships carrying hangars, their mouths open forward. Few greebles, big flat faces.
    /// </summary>
    void Meridian()
    {
        float s = R.Between(5, 8) + 0.5f, ch = R.Chance(0.5f) ? 0.55f : 0.15f;
        // The engine block, heavier than the hammer, or a pair of them side by side: its bells set its face.
        bool twin = R.Chance(0.3f);
        int rows = R.Chance(0.3f) ? 1 : 2, cols = twin ? R.Between(1, 2) : rows == 1 ? R.Between(4, 5) : R.Between(2, 4);
        float ew = s * (twin ? R.Float(1.3f, 1.7f) : R.Float(2.6f, 3.5f)), eh = rows == 1 ? s * R.Float(1.5f, 1.9f) : s * R.Float(2.0f, 2.7f);
        float br = MathF.Min(ew * 2f / cols, eh * 2f / rows) * 0.44f, bl = br * R.Float(1.1f, 1.5f), el = L * R.Float(0.14f, 0.24f);
        float ve1 = L - bl, ve0 = ve1 - el, ey = R.Float(-0.4f, 0.4f) * s;
        var blocks = twin ? new[] { -(ew + R.Between(1, 4)), ew + R.Between(1, 4) } : new[] { R.Float(-0.25f, 0.25f) * s };
        foreach (float eu in blocks)
        {
            Taper("hull", ve0, ve1, (eu, ey), (ew * 0.8f, eh * 0.84f), (eu, ey), (ew, eh), chamfer: 0.12f);
            for (int i = 0; i < cols; i++)
                for (int j = 0; j < rows; j++)
                    Bell(new NV3(eu - ew + 2f * ew * (i + 0.5f) / cols, ey - eh + 2f * eh * (j + 0.5f) / rows, ve1), br, bl, housing: false);
            Clutter(new NV3(eu - ew, ey - eh, ve0), new NV3(eu + ew, ey + eh, ve1), R.Chance(0.5f) ? 2 : 3, R.Between(0, 2));
        }
        if (twin) Box("trim", new NV3(0, ey, (ve0 + ve1) / 2), new NV3(MathF.Abs(blocks[0]), eh * 0.5f, el * 0.3f), 0.2f);
        float eu0 = blocks.Min() - ew, eu1 = blocks.Max() + ew;
        if (R.Chance(0.5f))
        {
            int face = R.Range(0, 4);
            NV3 o = Out(face), root = new NV3((eu0 + eu1) / 2, ey, (ve0 + ve1) / 2) + o * ((face < 2 ? (eu1 - eu0) / 2 : eh) - 1f);
            Radiators(new NV3(MathF.Round(root.X), MathF.Round(root.Y), MathF.Round(root.Z)), o, R.Between(2, 4), R.Between(6, 10), el * 0.7f);
        }
        // The hammer: an armoured block wider than the spine, its front raked in; stood on end, stepped with a brow over
        // its face, or drawn to an armoured ram's edge.
        float pl = L * R.Float(0.12f, 0.21f), pw = s * R.Float(2.2f, 3.0f), ph = s * R.Float(1.3f, 1.8f), pu = R.Float(-0.3f, 0.3f) * s, py = R.Float(-0.45f, 0.45f) * s;
        int hammer = R.Range(0, 4);
        if (hammer == 1) (pw, ph) = (ph * 1.1f, pw);
        var front = hammer == 3 ? (pw * 0.95f, ph * 0.22f) : (pw * 0.7f, ph * 0.6f);
        Taper("hull", 0, pl, (pu, py), front, (pu, py), (pw, ph), chamfer: 0.25f, hab: true);
        if (hammer == 2)
        {
            // The brow: wider and stubbier, standing over the block's face.
            float bw = pw * R.Float(1.1f, 1.35f), bh = ph * R.Float(0.35f, 0.5f), up = R.Chance(0.7f) ? 1 : -1;
            Box("hull", new NV3(pu, py + up * (ph + bh * 0.6f), pl * 0.4f - 2f), new NV3(bw, bh, pl * 0.4f + 2f), 0.3f);
        }
        // Plates layered on its brow and chin, jutting forward over its face.
        for (int k = R.Between(1, 3); k > 0; k--)
        {
            float up = R.Chance(0.6f) ? 1 : -1, t = R.Float(1.5f, 2.5f), w = pw * R.Float(0.55f, 1f), du = R.Float(-0.25f, 0.25f) * pw, y = py + up * (ph + t * 0.5f);
            Taper("trim", -R.Float(0f, 4f), pl * R.Float(0.5f, 1f), (pu + du, y), (w * 0.75f, t * 0.6f), (pu + du, y), (w, t), chamfer: 0.4f);
        }
        // The combat bridge: a glazed blister low in the head, or on its flank.
        {
            float bv = pl * R.Float(0.4f, 0.7f), wAt = pw * (front.Item1 / pw + (1f - front.Item1 / pw) * bv / pl), hAt = ph * (front.Item2 / ph + (1f - front.Item2 / ph) * bv / pl);
            bool flank = R.Chance(0.35f);
            NV3 c = flank ? new NV3(pu + (R.Chance(0.5f) ? 1 : -1) * (wAt + 1.5f), py + R.Float(-0.4f, 0.4f) * hAt, bv) : new NV3(pu + R.Float(-0.3f, 0.3f) * wAt, py - hAt - 1.5f, bv);
            Box("trim", c, new NV3(flank ? 3.5f : R.Between(4, 6) + 0.5f, 3.5f, R.Between(4, 6) + 0.5f), 0.6f, hab: true);
        }
        // The spine from the hammer into the engine block (or a pair of them, side by side or one over the other), a keel
        // slab down its back or belly now and then, ribbed with armour collars or bare.
        float sm = (pl + ve0) / 2, sl = (ve0 - pl) / 2 + 2;
        bool pair = R.Chance(0.25f), stacked = pair && R.Chance(0.4f);
        float ps = pair ? s * 0.6f : s, off = pair ? ps + R.Between(1, 3) : 0f;
        foreach (float o in pair ? new[] { -off, off } : new[] { 0f })
            Box("hull", stacked ? new NV3(0, o, sm) : new NV3(o, 0, sm), new NV3(ps, ps, sl), ch, hab: true);
        float ss = pair && !stacked ? off + ps : s, sy = stacked ? off + ps : s;
        if (R.Chance(0.4f))
        {
            float kh = R.Between(2, 5), up = R.Chance(0.6f) ? 1 : -1;
            Taper("trim", sm - sl * 0.8f, sm + sl * 0.8f, (0, up * (sy + kh * 0.5f)), (ss * 0.35f, kh * 0.3f), (0, up * (sy + kh * 0.5f)), (ss * 0.45f, kh), chamfer: 0.4f);
        }
        if (R.Chance(0.65f))
        {
            int every = R.Between(8, 16);
            float thick = R.Chance(0.5f) ? 1.5f : 2.5f;
            for (float v = pl + R.Between(3, every); v < ve0 - 3; v += every) Box("trim", new NV3(0, 0, v), new NV3(ss + thick, sy + thick, 1f), ch);
        }
        // Sponsons amidships carrying hangars, their mouths open forward: one side or both, never alike; or a hangar slung
        // under the spine.
        int sponsons = Role is "carrier" or "warship" ? R.Between(1, 2) : R.Chance(0.6f) ? 1 : 0, side = R.Chance(0.5f) ? 1 : -1;
        if (sponsons > 0 && R.Chance(0.25f))
        {
            float hl = L * R.Float(0.2f, 0.3f), hw = ss * R.Float(0.9f, 1.3f), hh = R.Between(4, 6) + 0.5f, hv = R.Float(pl + hl / 2 + 3, ve0 - hl / 2 - 3);
            Box("hull", new NV3(0, -sy - hh + 1, hv), new NV3(hw, hh, hl / 2), 0.3f, hab: true);
            Cut(new Loft { S0 = hv - hl / 2 - 1, S1 = hv - hl / 2 + 8, A0 = 0, B0 = -sy - hh + 1, A1 = 0, B1 = -sy - hh + 1, HA0 = hw - 1.5f, HB0 = hh - 1.5f, HA1 = hw - 1.5f, HB1 = hh - 1.5f });
            sponsons--;
        }
        for (int k = 0; k < sponsons; k++, side = -side)
        {
            float l = L * R.Float(0.14f, 0.26f), sw = R.Between(4, 8) + 0.5f, sh = R.Between(3, 6) + 0.5f, gap = R.Between(3, 8);
            float sv = R.Float(pl + l / 2 + 4, ve0 - l / 2 - 4), y = R.Float(-0.7f, 0.7f) * s, su = side * (ss + gap + sw);
            Box("hull", new NV3(su, y, sv), new NV3(sw, sh, l / 2), 0.25f, hab: true);
            Box("trim", new NV3(side * (ss + gap / 2f), y, sv + l * 0.1f), new NV3(gap / 2f + 1.5f, MathF.Min(sh, s) * 0.6f, l * 0.3f));
            Cut(new Loft { S0 = sv - l / 2 - 1, S1 = sv - l / 2 + 7, A0 = su, B0 = y, A1 = su, B1 = y, HA0 = sw - 1.5f, HB0 = sh - 1.5f, HA1 = sw - 1.5f, HB1 = sh - 1.5f });
        }
        if (R.Chance(0.45f))
            Mast(new NV3(MathF.Round(pu + R.Float(-0.5f, 0.5f) * pw), MathF.Round(py + ph * 0.8f), MathF.Round(pl * R.Float(0.6f, 0.9f))), NV3.UnitY, R.Between(5, 10), R.Chance(0.6f) ? new NV3(R.Float(-1f, 1f), 0.6f, -1f) : null);
        Guns(Role == "warship" ? R.Between(10, 14) : Role == "carrier" ? R.Between(6, 9) : R.Between(4, 6), 2, ve1);
    }

    // ---- Exchange ------------------------------------------------------------------------------------

    /// <summary>
    /// Ledger Exchange: a fat seed pod bristling with blocks. A short, fat round core in a cradle frame; rings of container
    /// pods all round it, above, below and on the sides, a slot or two empty; a bulbous hab sphere forward on a neck; a
    /// thrust frame aft with its drives set in an A and round tanks clustered on it; now and then a cargo crane.
    /// </summary>
    void Exchange()
    {
        float cr = L * R.Float(0.15f, 0.2f), cyr = cr * R.Float(0.8f, 1.1f), hr = cr * R.Float(0.5f, 0.72f);
        float hv = hr, cv0 = hr * 1.6f + 2f, cl = L * R.Float(0.46f, 0.56f), cv = cv0 + cl / 2, hy = R.Float(-0.35f, 0.35f) * cr;
        Sphere("hull", new NV3(0, 0, cv), new NV3(cr, cyr, cl / 2), hab: true);
        Sphere("hull", new NV3(0, hy, hv), new NV3(hr), hab: true);
        Can("machine", 2, new NV3(0, hy * 0.5f, (hv + cv0) / 2 + 1), hr * 0.45f, cv0 - hv + 4);
        // The cradle: girders the pod's length at its corners.
        float half = cl / 2 * 0.82f, gu = cr * 0.8f + 1f, gy = cyr * 0.8f + 1f;
        foreach (int a in new[] { -1, 1 })
            foreach (int b in new[] { -1, 1 })
                Box("machine", new NV3(a * gu, b * gy, cv), new NV3(1f, 1f, half));
        // Rings of containers on hoops round the pod.
        int rings = R.Between(2, 4), slots = R.Pick(new[] { 8, 8, 12 });
        float chh = R.Between(1, 2) + 0.5f, clen = R.Between(4, 6) + 0.5f;
        for (int k = 0; k < rings; k++)
        {
            float v = cv - half + 2 * half * (k + 0.5f) / rings, f = MathF.Sqrt(MathF.Max(0.1f, 1f - MathF.Pow((v - cv) / (cl / 2), 2)));
            Add(new Hoop { C = new NV3(0, 0, v), R = MathF.Max(cr, cyr) * f + 0.5f, T = 1.2f }, "machine");
            int deep = R.Between(1, 3);
            float a0 = R.Chance(0.5f) ? 0f : MathF.PI / slots;
            for (int j = 0; j < slots; j++)
            {
                if (R.Chance(0.12f)) continue;
                NV3 o = Round(a0 + j * MathF.Tau / slots), t = new(-o.Y, o.X, 0f);
                float re = 1f / MathF.Sqrt(o.X * o.X / (cr * f * cr * f) + o.Y * o.Y / (cyr * f * cyr * f));
                for (int i = 0, n = Math.Max(1, deep + R.Range(-1, 2)); i < n; i++)
                    Plate(Cargo(), o * (re + chh - 0.3f + i * (2 * chh + 0.6f)) + new NV3(0, 0, v), NV3.UnitZ, t, new NV3(clen, chh, chh));
            }
        }
        // A cargo crane off the pod now and then, a load hanging from it.
        if (R.Chance(0.5f))
        {
            float a = R.Float(0f, MathF.Tau), v = cv + R.Float(-0.3f, 0.3f) * cl, f = MathF.Sqrt(1f - MathF.Pow((v - cv) / (cl / 2), 2));
            NV3 o = Round(a), foot = o * (MathF.Min(cr, cyr) * f * 0.9f) + new NV3(0, 0, v), tip = foot + o * R.Float(10f, 16f) + new NV3(0, 0, -R.Float(6f, 12f));
            Bar("machine", foot, tip, 1.2f, 0.8f);
            Plate(Cargo(), tip + o * 3f, NV3.UnitZ, new NV3(-o.Y, o.X, 0f), new NV3(clen, chh, chh));
        }
        // The thrust frame, its drives in an A, round tanks clustered on it.
        float fv = cv + cl / 2 - 3, fr = cr * R.Float(0.55f, 0.7f);
        Can("machine", 2, new NV3(0, 0, fv + 4), fr, 10);
        int nb = R.Between(3, 5);
        float bb = fr * R.Float(0.4f, 0.48f), bv = fv + 9;
        (float U, float Y)[] at = nb switch
        {
            3 => new[] { (-1f, -0.7f), (1f, -0.7f), (0f, 0.9f) },
            4 => new[] { (-1.1f, -0.7f), (1.1f, -0.7f), (-0.5f, 0.85f), (0.5f, 0.85f) },
            _ => new[] { (-1.2f, -0.8f), (0f, -0.9f), (1.2f, -0.8f), (-0.6f, 0.8f), (0.6f, 0.8f) },
        };
        foreach (var (u, y) in at) Bell(new NV3(u * bb * 1.2f, y * bb * 1.2f, bv), bb, bb * R.Float(1.2f, 1.6f));
        float ta = R.Float(0f, MathF.Tau);
        for (int k = 0, n = R.Between(2, 4); k < n; k++)
        {
            float tr = R.Between(3, 5) + 0.5f;
            Sphere("tank", Round(ta + k * MathF.Tau / n + R.Float(-0.3f, 0.3f)) * (fr + tr - 1f) + new NV3(0, 0, fv + R.Float(-2f, 3f)), new NV3(tr));
        }
        if (R.Chance(0.6f)) Mast(new NV3(0, MathF.Round(hy + hr - 1), MathF.Round(hv)), NV3.UnitY, R.Between(4, 7), new NV3(0, 0.6f, -1f));
        Guns(R.Between(3, 6), cv - cl / 2, cv + cl / 2);
    }

    // ---- Reavers -------------------------------------------------------------------------------------

    /// <summary>
    /// Carrion Reavers: a wedge-nosed brute with a jagged outline. Short and heavy: two halves of different ships welded
    /// together, mismatched in size, section and paint and off each other's axis; a huge ram of overlapping armour wedges;
    /// trophy pieces of other factions' hulls hung on it; spikes; oversized drives bolted on unevenly.
    /// </summary>
    void Reavers()
    {
        float ram = L * R.Float(0.2f, 0.28f), fl = L * R.Float(0.3f, 0.38f), al = L * R.Float(0.24f, 0.32f);
        float fw = R.Between(10, 14) + 0.5f, fh = R.Between(8, 12) + 0.5f, v0 = ram - 4f, vj = v0 + fl;
        Box(Scrap(), new NV3(0, 0, v0 + fl / 2), new NV3(fw, fh, fl / 2), R.Float(0.15f, 0.5f), hab: true);
        Clutter(new NV3(-fw, -fh, v0), new NV3(fw, fh, vj), R.Range(0, 4), R.Between(2, 5));
        // The aft half: another ship's, round or square, bigger or smaller, off the fore's axis.
        float k = R.Chance(0.5f) ? R.Float(0.6f, 0.8f) : R.Float(1.15f, 1.45f), au = R.Float(-0.3f, 0.3f) * fw, ay = R.Float(-0.35f, 0.35f) * fh, aw = fw * k, ah = fh * k * R.Float(0.8f, 1.2f);
        float va = vj + 2 + al / 2;
        if (R.Chance(0.45f)) Taper(Scrap(), va - al / 2, va + al / 2, (au, ay), (aw, ah), (au, ay), (aw * 0.9f, ah * 0.9f), round: true, hab: true);
        else Box(Scrap(), new NV3(au, ay, va), new NV3(aw, ah, al / 2), R.Float(0f, 0.35f), hab: true);
        Clutter(new NV3(au - aw, ay - ah, va - al / 2), new NV3(au + aw, ay + ah, va + al / 2), R.Range(0, 4), R.Between(2, 5));
        // The weld: a narrow collar between them, plates across the seam.
        Box("machine", new NV3(au / 2, ay / 2, vj + 1), new NV3(MathF.Min(fw, aw) * 0.7f, MathF.Min(fh, ah) * 0.7f, 2.5f));
        for (int i = R.Between(3, 6); i > 0; i--)
        {
            NV3 o = Round(R.Float(0f, MathF.Tau));
            if (Surface(new NV3(au / 2, ay / 2, vj + 1), o) is { } p) Plate(Scrap(), p, NV3.UnitZ, new NV3(-o.Y, o.X, 0f), new NV3(R.Float(3f, 5f), R.Float(2f, 4f), 0.9f));
        }
        // The ram: one great wedge flaring back wider and taller than the fore half to a raked edge (across or upright),
        // overlapping armour plates layered over it, their edges ragged forward.
        bool flat = R.Chance(0.6f);
        float rw = fw * R.Float(1.1f, 1.4f), rh = fh * R.Float(1.1f, 1.4f), ry = R.Float(-0.3f, 0.2f) * fh, rake = R.Float(0f, 0.5f) * fh;
        Taper("trim", 0, ram, (0, ry - rake), flat ? (rw * R.Float(0.5f, 0.8f), 1f) : (1f, rh * R.Float(0.5f, 0.8f)), (0, ry), (rw, rh), chamfer: 0.2f);
        for (int i = R.Between(3, 6); i > 0; i--)
        {
            float tip = R.Float(-3f, ram * 0.5f), y = ry + R.Float(-0.7f, 0.7f) * rh, u = R.Float(-0.3f, 0.3f) * rw, bw = rw * R.Float(0.5f, 1.05f), bh = rh * R.Float(0.25f, 0.5f);
            Taper(R.Chance(0.5f) ? "trim" : Scrap(), tip, ram + R.Float(2f, 10f), (u * 0.5f, y * 0.8f), (bw * R.Float(0.3f, 0.7f), 0.7f), (u, y), (bw, bh), chamfer: 0.3f);
        }
        // Trophies: pieces of other factions' hulls hung on it.
        for (int i = R.Between(1, 4); i > 0; i--) Trophy(R.Float(v0 + 4, va + al / 2 - 4));
        // Spikes, outward and raked forward.
        for (int i = R.Between(14, 28); i > 0; i--)
        {
            NV3 o = Round(R.Float(0f, MathF.Tau));
            if (Surface(new NV3(0, 0, R.Float(2f, va + al / 2)), o) is not { } p) continue;
            NV3 d = NV3.Normalize(o + new NV3(0, 0, R.Float(-0.9f, 0.3f)));
            Bar("dark", p - d, p + d * R.Between(5, 14), R.Float(1.2f, 1.8f), 0.3f);
        }
        // Drives bolted on unevenly: one oversized, others smaller and off to a side on outriggers.
        float vs = va + al / 2, mr = MathF.Min(aw, ah) * R.Float(0.6f, 0.85f);
        Bell(new NV3(au + R.Float(-0.2f, 0.2f) * aw, ay + R.Float(-0.2f, 0.2f) * ah, vs + 1), mr, mr * R.Float(1.1f, 1.5f));
        for (int i = R.Between(1, 2); i > 0; i--)
        {
            float r = mr * R.Float(0.35f, 0.6f), z = vs - R.Float(2f, 8f);
            NV3 o = Round(R.Float(0f, MathF.Tau)), c = new NV3(au, ay, 0f) + o * (MathF.Max(aw, ah) + r + R.Float(0f, 3f));
            Bar("machine", new NV3(au, ay, z - 3) + o * MathF.Min(aw, ah) * 0.5f, c + new NV3(0, 0, z - 3), 1.6f);
            Bell(c + new NV3(0, 0, z), r, r * R.Float(1.2f, 1.7f));
        }
        Guns(R.Between(4, 8), v0, vs);
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
                var h = new NV3(R.Between(3, 5) + 0.5f, R.Between(3, 5) + 0.5f, R.Between(5, 8) + 0.5f);
                Box(paint, p + o * (MathF.Abs(o.X) > 0.5f ? h.X : h.Y) * 0.8f, h, 0.2f);
                Box(paint, p + o * (MathF.Abs(o.X) > 0.5f ? h.X : h.Y) * 0.8f, h + new NV3(1, 1, -h.Z + 1), 0.2f);
                break;
            case 1:
                // An Exchange container stack.
                for (int i = 0, n = R.Between(2, 4); i < n; i++) Plate(Cargo(), p + o * (2f + i * 3.4f), NV3.UnitZ, new NV3(-o.Y, o.X, 0f), new NV3(R.Between(4, 6), 1.5f, 1.5f));
                break;
            case 2:
                // A Kessari ring and its spokes.
                float rr = R.Between(4, 6);
                NV3 c = p + o * (rr * 0.6f);
                Add(new Hoop { C = c, R = rr, T = 0.9f }, paint);
                for (int i = 0; i < 4; i++) Bar("machine", c, c + Round(i * MathF.PI / 2) * rr, 0.8f);
                break;
            case 3:
                // A Vharn blade, swept back.
                Plate(paint, p + o * 6f + new NV3(0, 0, 3f), o + new NV3(0, 0, 0.5f), NV3.UnitZ, new NV3(8f, R.Float(3f, 4.5f), 0.9f), 0.3f);
                break;
            default:
                // A Freeholder hab can.
                Can(paint, 2, p + o * 3.5f, 3.5f, R.Between(8, 12), domes: true);
                break;
        }
    }

    // ---- Kessari -------------------------------------------------------------------------------------

    /// <summary>
    /// Kessari Clans: a see-through symmetric lattice. Machine builders: perfect 4-, 6- or 8-fold symmetry round the
    /// thrust axis; a bare core column; bays of identical modules on rings of struts with spokes in to the core, longerons
    /// and braces between the rings, the same machinery in every bay; a spire forward; a ring of drives about a central
    /// one aft; a status light on every module.
    /// </summary>
    void Kessari()
    {
        int n = R.Pick(new[] { 4, 4, 6, 8 }), bays = R.Between(4, 7), profile = R.Range(0, 3), kind = R.Range(0, 3);
        float vp = L * R.Float(0.12f, 0.17f), vd = L * R.Float(0.1f, 0.14f), pitch = (L - vp - vd) / bays, rr = R.Between(12, 21);
        float a0 = MathF.PI / n, ms = R.Between(2, 4) + 0.5f, cr = R.Between(2, 3) + 0.5f, mr = cr + R.Between(1, 3), ml = pitch * R.Float(0.3f, 0.5f);
        bool alternate = R.Chance(0.35f), hoops = R.Chance(0.3f);
        float Radius(int i) => profile switch { 1 => rr * (0.7f + 0.4f * MathF.Sin(MathF.PI * i / bays)), 2 => rr * (0.65f + 0.35f * i / bays), _ => rr };
        NV3 Vertex(int i, float a) => Round(a) * Radius(i) + new NV3(0, 0, vp + i * pitch);
        // The core column and the spire.
        float vs = vp + bays * pitch + 3;
        Can("machine", 2, new NV3(0, 0, (vp * 0.5f + vs) / 2), cr, vs - vp * 0.5f);
        Taper("hull", 0, vp, (0, 0), (0.8f, 0.8f), (0, 0), (cr + 2.5f, cr + 2.5f), round: true);
        for (int i = 0; i <= bays; i++)
        {
            NV3 p = Vertex(i, a0), q = Vertex(i, a0 + MathF.Tau / n), o = Round(a0);
            // One sector of the ring (its side, its spoke, its module and light), turned n ways round.
            Ways(Bar("machine", p, q, 1.0f), n);
            Ways(Bar("machine", o * cr + new NV3(0, 0, p.Z), p, 0.9f), n);
            if (hoops) Add(new Hoop { C = new NV3(0, 0, p.Z), R = Radius(i), T = 1.1f }, "machine");
            if (!alternate || i % 2 == 0)
            {
                float reach = MathF.Max(1f, MathF.Min(ms * 1.4f, pitch * 0.3f));
                Prim m = kind switch
                {
                    0 => Plate("hull", p + o * (ms - 0.5f), NV3.UnitZ, new NV3(-o.Y, o.X, 0f), new NV3(reach, ms, ms)),
                    1 => Sphere("hull", p + o * (ms - 0.5f), new NV3(ms)),
                    _ => Bar("hull", p + o * (ms - 0.5f) - new NV3(0, 0, reach * 0.6f), p + o * (ms - 0.5f) + new NV3(0, 0, reach * 0.6f), ms * 0.75f),
                };
                Ways(m, n);
                Ways(Sphere("glow", p + o * (2f * ms - 0.2f), new NV3(1f)), n);
            }
            if (i == bays) break;
            // Longeron and brace to the next ring, and the bay's machinery on the core.
            Ways(Bar("machine", p, Vertex(i + 1, a0), 1.0f), n);
            Ways(Bar("machine", p, Vertex(i + 1, a0 + MathF.Tau / n), 0.75f), n);
            Can("machine", 2, new NV3(0, 0, p.Z + pitch / 2), mr, ml);
        }
        // The drives: a central bell and a ring of n about it, on a round thrust plate.
        float rd = Radius(bays) * 0.5f, bb = R.Between(2, 3) + 0.5f;
        Can("machine", 2, new NV3(0, 0, vs), rd + bb + 1f, 5);
        Bell(new NV3(0, 0, vs + 2.5f), cr + 1.5f, (cr + 1.5f) * 1.4f, housing: false);
        for (int k = 0; k < n; k++) Bell(Round(a0 + k * MathF.Tau / n) * rd + new NV3(0, 0, vs + 2.5f), bb, bb * 1.4f, housing: false);
        // Guns the same on all four faces, at the first ring and the last (a six-fold lattice keeps one in its spire's tip).
        if (n == 6) Turret(new NV3(0, 0, vp), 5);
        else
            foreach (int i in new[] { 0, bays })
                for (int face = 0; face < 4; face++) Turret(new NV3(0, 0, vp + i * pitch), face);
    }

    // ---- Vharn ---------------------------------------------------------------------------------------

    /// <summary>
    /// Vharn Dominion: a wasp or an insect. A chain of three to six bulbous segments, a head, a thorax and an abdomen that
    /// tapers to its tail, joined by narrow waists, the abdomen hanging off the line; a carapace ridge or shell down each;
    /// long curved blades sweeping back unevenly, port against starboard; a needle prow or mandible forks; legs now and
    /// then; the drives in the abdomen's tail.
    /// </summary>
    void Vharn()
    {
        int m = R.Between(3, 6);
        float needle = R.Chance(0.55f) ? L * R.Float(0.12f, 0.2f) : 0f, waist = R.Between(3, 6), fy = R.Float(0.7f, 1f);
        var share = new List<float> { R.Float(0.7f, 1f), R.Float(1.2f, 1.6f) };
        for (int i = 2; i < m; i++) share.Add(i == 2 ? R.Float(2f, 2.6f) : share[^1] * R.Float(0.6f, 0.85f));
        float scale = (L * 0.94f - needle - waist * (m - 1)) / share.Sum(), sag = L * R.Float(-0.06f, 0.12f), lift = L * R.Float(-0.02f, 0.04f);
        var segs = new List<(NV3 C, NV3 R)>();
        float v = needle;
        for (int i = 0; i < m; i++)
        {
            float len = share[i] * scale, r = MathF.Min(len * (i == 0 ? 0.55f : i == 1 ? 0.45f : 0.38f), L * 0.1f);
            float y = i == 0 ? lift : i == 1 ? 0f : -sag * (i - 1) / Math.Max(1, m - 2);
            var c = new NV3(0, y, v + len / 2);
            var rad = new NV3(r, r * fy, len / 2);
            Sphere("hull", c, rad, hab: i > 0);
            // The carapace: a ridge down its back, or a shell over it.
            if (R.Chance(0.6f)) Plate("trim", c + new NV3(0, rad.Y * 0.85f, 0), NV3.UnitZ, NV3.UnitY, new NV3(len * 0.42f, rad.Y * 0.35f, 0.9f));
            else Sphere("trim", c + new NV3(0, rad.Y * 0.25f, -len * 0.08f), new NV3(rad.X * 1.06f, rad.Y * 0.85f, rad.Z * 0.75f));
            segs.Add((c, rad));
            v += len + waist;
        }
        // The waists: narrow joints between the segments.
        for (int i = 0; i + 1 < m; i++)
            Bar("machine", segs[i].C + new NV3(0, 0, segs[i].R.Z - 1), segs[i + 1].C - new NV3(0, 0, segs[i + 1].R.Z - 1), R.Float(1.5f, 2.5f));
        // The prow: a needle, or mandible forks of uneven length curving in.
        var head = segs[0];
        NV3 nose = head.C - new NV3(0, 0, head.R.Z);
        if (needle > 0f) Bar("trim", nose + new NV3(0, 0, 2), nose - new NV3(0, R.Float(-0.1f, 0.1f) * needle, needle), R.Float(1.6f, 2.4f), 0.4f);
        else
            foreach (int s in new[] { -1, 1 })
            {
                float k = R.Float(0.8f, 1.25f);
                NV3 a = head.C + new NV3(s * head.R.X * 0.6f, -head.R.Y * 0.3f, -head.R.Z * 0.6f);
                NV3 b = a + new NV3(s * head.R.X * 0.5f, 0, -L * 0.07f * k), c = b + new NV3(-s * head.R.X * 0.55f, 0, -L * 0.06f * k);
                Bar("trim", a, b, 1.8f, 1.4f);
                Bar("trim", b, c, 1.4f, 0.4f);
            }
        // Blades sweeping back unevenly: port against starboard, a dorsal or a ventral one now and then.
        int port = R.Between(1, 3), star = port == 1 ? R.Between(2, 3) : port == 3 ? R.Between(1, 2) : R.Chance(0.5f) ? 1 : 3;
        var roots = new List<float>();
        for (int i = 0; i < port; i++) roots.Add(MathF.PI + R.Float(-0.3f, 0.3f));
        for (int i = 0; i < star; i++) roots.Add(R.Float(-0.3f, 0.3f));
        if (R.Chance(0.4f)) roots.Add(MathF.PI / 2 * (R.Chance(0.6f) ? 1 : -1) + R.Float(-0.2f, 0.2f));
        foreach (float a in roots)
        {
            var (c, rad) = segs[Math.Min(m - 1, R.Between(1, 2))];
            Blade(c, rad, a, L * R.Float(0.24f, 0.4f), R.Between(6, 11));
        }
        // Legs folded under the thorax now and then.
        if (R.Chance(0.4f))
        {
            var (c, rad) = segs[1];
            for (int k = 0, pairs = R.Between(2, 3); k < pairs; k++)
                foreach (int s in new[] { -1, 1 })
                {
                    NV3 hip = c + new NV3(s * rad.X * 0.5f, -rad.Y * 0.7f, (k - (pairs - 1) / 2f) * rad.Z * 0.5f);
                    NV3 knee = hip + new NV3(s * rad.X * 0.7f, -rad.Y * 0.5f, R.Float(-2f, 2f)), foot = knee + new NV3(s * 1.5f, -rad.Y * 0.7f, R.Float(2f, 6f));
                    Bar("dark", hip, knee, 1.3f, 1.0f);
                    Bar("dark", knee, foot, 1.0f, 0.5f);
                }
        }
        // The drives in the tail.
        var tail = segs[^1];
        NV3 end = tail.C + new NV3(0, 0, tail.R.Z - 2);
        int nb = R.Between(1, 3);
        float br = tail.R.X * (nb == 1 ? 0.55f : 0.36f);
        for (int k = 0; k < nb; k++)
        {
            NV3 off = nb == 1 ? NV3.Zero : Round(MathF.PI / 2 + k * MathF.Tau / nb) * br * 1.25f;
            Bell(end + off, br, br * R.Float(1.3f, 1.8f));
        }
        Guns(R.Between(2, 4), segs[1].C.Z - segs[1].R.Z, tail.C.Z);
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

    // ---- Husks ---------------------------------------------------------------------------------------

    /// <summary>
    /// A Husk ship (DESIGN: a vessel of any faction hijacked by the worm): its prow crumpled from ramming, its drive section
    /// torn open, breaches down its hull with flesh bulging out of them and tendrils crawling over the plating.
    /// </summary>
    void Wound()
    {
        var solid = Prims.Where(p => !p.Cut).ToList();
        float front = solid.Min(p => p.Lo.Z), back = solid.Max(p => p.Hi.Z);
        float wide = solid.Max(p => MathF.Max(-p.Lo.X, p.Hi.X)), up = solid.Max(p => p.Hi.Y), down = solid.Min(p => p.Lo.Y);
        // The prow crumpled: bites out of its front.
        for (int i = R.Between(2, 4); i > 0; i--)
            Cut(new Ball { C = new NV3(R.Float(-0.4f, 0.4f) * wide, R.Float(down, up) * 0.5f, front + R.Float(-2f, 6f)), R = new NV3(R.Float(5f, 10f), R.Float(4f, 8f), R.Float(4f, 8f)) });
        // The drive section torn open.
        var tears = new List<NV3>();
        for (int i = R.Between(2, 3); i > 0; i--)
        {
            var c = new NV3(R.Float(-0.6f, 0.6f) * wide, R.Float(down, up) * 0.6f, back - R.Float(2f, 10f));
            Cut(new Ball { C = c, R = new NV3(R.Float(6f, 12f)) });
            tears.Add(c);
        }
        // Breaches down the hull.
        var breaches = new List<(NV3 P, NV3 O, float R)>();
        for (int i = Math.Clamp((int)((back - front) / 18f), 4, 9) + R.Between(-1, 1); i > 0; i--)
        {
            NV3 o = Round(R.Float(0f, MathF.Tau));
            if (Surface(new NV3(0, 0, R.Float(front + 8f, back - 8f)), o) is not { } p) continue;
            float r = R.Float(3f, 6f);
            Cut(new Ball { C = p, R = new NV3(r) });
            breaches.Add((p, o, r));
        }
        // What grows over it all, once the cutting is done (nothing cut after it, nothing left floating): buckled plates
        // folded over the crumpled prow, flesh in the tears and bulging out of the breaches, tendrils crawling over the
        // plating from them.
        for (int i = R.Between(3, 6); i > 0; i--)
            if (Surface(new NV3(0, 0, front + R.Float(3f, 14f)), Round(R.Float(0f, MathF.Tau))) is { } p)
                Plate(Scrap(), p, new NV3(R.Float(-1f, 1f), R.Float(-1f, 1f), R.Float(-1f, 1f)), new NV3(R.Float(-1f, 1f), R.Float(-1f, 1f), 1f), new NV3(R.Between(2, 4), R.Between(2, 4), 0.8f));
        foreach (NV3 c in tears)
        {
            NV3 o = NV3.Normalize(c with { Z = 0 } + new NV3(0, 0.01f, 0));
            if (Surface(new NV3(0, 0, c.Z), o) is { } p) Flesh(p, o, R.Float(4f, 7f));
        }
        foreach (var (p, o, r) in breaches)
        {
            Flesh(p, o, r);
            for (int k = R.Between(1, 3); k > 0; k--)
            {
                NV3 a = p, dir = NV3.Normalize(new NV3(-o.Y, o.X, 0f) * (R.Chance(0.5f) ? 1 : -1) + new NV3(0, 0, R.Float(-1f, 1f)));
                for (int s = 0; s < 4; s++)
                {
                    NV3 b = a + dir * R.Float(3f, 6f), bo = new(b.X, b.Y, 0f);
                    if (bo.LengthSquared() < 0.01f || Surface(new NV3(0, 0, b.Z), NV3.Normalize(bo)) is not { } on) break;
                    Bar("flesh", a, on, 1.3f - s * 0.15f, 1.1f - s * 0.15f);
                    a = on;
                    dir = NV3.Normalize(dir + new NV3(R.Float(-0.5f, 0.5f), R.Float(-0.5f, 0.5f), R.Float(-0.5f, 0.5f)));
                }
            }
        }
    }

    /// <summary>A mass of flesh filling a hole about p and bulging out of it along o, every lump overlapping the hole's rim.</summary>
    void Flesh(NV3 p, NV3 o, float r)
    {
        for (int k = R.Between(3, 6); k > 0; k--)
        {
            float fr = R.Float(MathF.Max(2.5f, r * 0.7f), r * 1.1f + 1f);
            NV3 jitter = new NV3(R.Float(-1f, 1f), R.Float(-1f, 1f), R.Float(-1f, 1f)) * r * 0.35f;
            Sphere("flesh", p + o * fr * R.Float(0.1f, 0.45f) + jitter, new NV3(fr * R.Float(0.8f, 1.2f), fr * R.Float(0.8f, 1.2f), fr * R.Float(0.8f, 1.4f)));
        }
    }
}
