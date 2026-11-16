#nullable enable
using System;
using System.Linq;
using NV3 = System.Numerics.Vector3;

namespace PoiGenerator.Gen;

/// <summary>
/// Meridian Combine: clean, massive slabs. Whatever the plan, the shared traits are square or hex sections with big flat
/// faces, an armoured hammer-faced prow layered with plates and a bridge blister low in it or on its flank, armour
/// collars, engine blocks heavier than the prow with four to eight big bells, hangars with open mouths, few greebles.
/// Three body plans: the hammerhead (a heavy prow, a long spine and a heavier engine block); the monolith (one slab
/// swelling from the prow into the engine block, terraces stacked on its back); twin hulls joined by a hammer cross-block.
/// </summary>
public sealed partial class ShipForm
{
    void Meridian()
    {
        switch (R.PickWeighted(Role switch { "carrier" => new[] { 1f, 0.7f, 1.5f }, "freighter" or "miner" => new[] { 1f, 1.4f, 0.8f }, _ => new[] { 1.2f, 1f, 1f } }))
        {
            case 1: Plan = "monolith"; Monolith(); break;
            case 2: Plan = "twin"; Twin(); break;
            default: Plan = "hammerhead"; Hammerhead(); break;
        }
    }

    /// <summary>
    /// An engine block about (cu, cy), half w across and h high, el long, its bells rows by cols filling its rear face and
    /// flaring past the stern, a little machinery on it, radiator fins off it now and then. Its front.
    /// </summary>
    float Engines(float cu, float cy, float w, float h, int rows, int cols, float el, bool fins)
    {
        float br = MathF.Min(w * 2f / cols, h * 2f / rows) * 0.44f, bl = br * R.Float(1.1f, 1.5f), ve1 = L - bl - R.Float(0f, 3f) * K, ve0 = ve1 - el;
        Taper("hull", ve0, ve1, (cu, cy), (w * 0.8f, h * 0.84f), (cu, cy), (w, h), chamfer: 0.12f);
        for (int i = 0; i < cols; i++)
            for (int j = 0; j < rows; j++)
                Bell(new NV3(cu - w + 2f * w * (i + 0.5f) / cols, cy - h + 2f * h * (j + 0.5f) / rows, ve1), br, bl, housing: false);
        Clutter(new NV3(cu - w, cy - h, ve0), new NV3(cu + w, cy + h, ve1), R.Chance(0.5f) ? 2 : 3, R.Between(0, 2));
        NV3 o = Out(R.Range(0, 4));
        if (fins && Surface(new NV3(cu, cy, MathF.Round((ve0 + ve1) / 2)), o) is { } root)
            Radiators(new NV3(MathF.Round(root.X), MathF.Round(root.Y), MathF.Round(root.Z)), o, R.Between(2, 4), MathF.Round(M(6f, 10f)), el * 0.7f);
        return ve0;
    }

    /// <summary>Armour plates layered on a prow's brow and chin (about (pu, py), pw wide, pl long), jutting forward over its face and resting on the surface at their backs.</summary>
    void BrowPlates(float pl, float pw, float pu, float py)
    {
        for (int k = R.Between(1, 3); k > 0; k--)
        {
            float up = R.Chance(0.6f) ? 1 : -1, t = M(1.5f, 2.5f), w = pw * R.Float(0.55f, 1f), du = R.Float(-0.25f, 0.25f) * pw;
            if (Surface(new NV3(pu + du, py, pl - 1f), up * NV3.UnitY) is not { } rest) continue;
            float y = rest.Y + up * (t - 1f);
            Taper("trim", -R.Float(0f, 4f) * K, pl, (pu + du, y), (w * 0.75f, t * 0.6f), (pu + du, y), (w, t), chamfer: 0.4f);
        }
    }

    /// <summary>The combat bridge: a glazed blister low in a prow's head or on its flank, set on the surface it finds there.</summary>
    void Blister(float v, float pu, float py)
    {
        bool flank = R.Chance(0.35f);
        NV3 o = flank ? (R.Chance(0.5f) ? NV3.UnitX : -NV3.UnitX) : -NV3.UnitY;
        if (Surface(new NV3(pu, py, v), o) is not { } p) return;
        NV3 h = flank ? new NV3(M(3.5f), M(3.5f), M(4.5f, 6.5f)) : new NV3(M(4.5f, 6.5f), M(3.5f), M(4.5f, 6.5f));
        Box("trim", p + o * (MathF.Abs(o.X) > 0.5f ? h.X : h.Y) * 0.5f, h, 0.6f, hab: true);
    }

    /// <summary>Armour collars round a body from v0 to v1 about (cu, cy), half w by h, every so often (or none).</summary>
    void Collars(float v0, float v1, float cu, float cy, float w, float h, float chamfer)
    {
        if (!R.Chance(0.65f)) return;
        float every = M(8f, 16f), thick = R.Chance(0.5f) ? M(1.5f) : M(2.5f);
        for (float v = v0 + R.Float(M(3f), every); v < v1 - M(3f); v += every) Box("trim", new NV3(cu, cy, v), new NV3(w + thick, h + thick, M(1f)), chamfer);
    }

    /// <summary>Sponsons between v0 and v1 off a body w across (about u 0) carrying hangars, their mouths open forward: one side or both, never alike.</summary>
    void Sponsons(int count, float v0, float v1, float w, float s)
    {
        int side = R.Chance(0.5f) ? 1 : -1;
        for (int k = 0; k < count; k++, side = -side)
        {
            float l = L * R.Float(0.14f, 0.26f), sw = M(4.5f, 8.5f), sh = M(3.5f, 6.5f), gap = M(3f, 8f);
            float sv = R.Float(v0 + l / 2 + M(4f), v1 - l / 2 - M(4f)), y = R.Float(-0.7f, 0.7f) * s, su = side * (w + gap + sw);
            Box("hull", new NV3(su, y, sv), new NV3(sw, sh, l / 2), 0.25f, hab: true);
            Box("trim", new NV3(side * (w + gap / 2f), y, sv + l * 0.1f), new NV3(gap / 2f + M(1.5f), MathF.Min(sh, s) * 0.6f, l * 0.3f));
            Mouth(su, y, sv - l / 2, sw, sh);
        }
    }

    /// <summary>A hangar's open mouth in the front face (at v) of a block about (u, y), half w by h.</summary>
    void Mouth(float u, float y, float v, float w, float h) =>
        Cut(new Loft { S0 = v - 1, S1 = v + M(7f), A0 = u, B0 = y, A1 = u, B1 = y, HA0 = w - 1.5f, HB0 = h - 1.5f, HA1 = w - 1.5f, HB1 = h - 1.5f });

    /// <summary>Guns by what the ship is for.</summary>
    int Battery => Role == "warship" ? R.Between(10, 14) : Role == "carrier" ? R.Between(6, 9) : R.Between(4, 6);

    /// <summary>
    /// The hammerhead, a hammer or a dumbbell at range: an armoured prow block wider than the spine (stood on end,
    /// stepped with a brow over its face, or drawn to an armoured ram's edge), a long thick spine (square or hex, or a pair
    /// of them side by side or one over the other) ribbed or bare, a keel now and then, a heavier engine block (or two
    /// side by side) at the stern, sponsons or a slung hangar amidships.
    /// </summary>
    void Hammerhead()
    {
        float s = M(5.5f, 8.5f), ch = R.Chance(0.5f) ? 0.55f : 0.15f;
        bool twin = R.Chance(0.3f);
        int rows = R.Chance(0.3f) ? 1 : 2, cols = twin ? R.Between(1, 2) : rows == 1 ? R.Between(4, 5) : R.Between(2, 4);
        float ew = s * (twin ? R.Float(1.3f, 1.7f) : R.Float(2.6f, 3.5f)), eh = rows == 1 ? s * R.Float(1.5f, 1.9f) : s * R.Float(2.0f, 2.7f), el = L * R.Float(0.14f, 0.24f), ey = R.Float(-0.4f, 0.4f) * s;
        float[] blocks = twin ? new[] { -(ew + M(1f, 4f)), ew + M(1f, 4f) } : new[] { R.Float(-0.25f, 0.25f) * s };
        bool fins = R.Chance(0.5f);
        float ve0 = blocks.Select((eu, i) => Engines(eu, ey, ew, eh, rows, cols, el, fins && i == 0)).Max();
        if (twin) Box("trim", new NV3(0, ey, ve0 + el / 2), new NV3(MathF.Abs(blocks[0]), eh * 0.5f, el * 0.3f), 0.2f);
        // The hammer.
        float pl = L * R.Float(0.12f, 0.21f), pw = s * R.Float(2.2f, 3.0f), ph = s * R.Float(1.3f, 1.8f), pu = R.Float(-0.3f, 0.3f) * s, py = R.Float(-0.45f, 0.45f) * s;
        int hammer = R.Range(0, 4);
        if (hammer == 1) (pw, ph) = (ph * 1.1f, pw);
        Taper("hull", 0, pl, (pu, py), hammer == 3 ? (pw * 0.95f, ph * 0.22f) : (pw * 0.7f, ph * 0.6f), (pu, py), (pw, ph), chamfer: 0.25f, hab: true);
        if (hammer == 2)
        {
            // The brow: wider and stubbier, standing over the block's face.
            float bw = pw * R.Float(1.1f, 1.35f), bh = ph * R.Float(0.35f, 0.5f), up = R.Chance(0.7f) ? 1 : -1;
            Box("hull", new NV3(pu, py + up * (ph + bh * 0.6f), pl * 0.4f - M(2f)), new NV3(bw, bh, pl * 0.4f + M(2f)), 0.3f);
        }
        BrowPlates(pl, pw, pu, py);
        Blister(pl * R.Float(0.4f, 0.7f), pu, py);
        // The spine.
        float sm = (pl + ve0) / 2, sl = (ve0 - pl) / 2 + 2;
        bool pair = R.Chance(0.25f), stacked = pair && R.Chance(0.4f);
        float ps = pair ? s * 0.6f : s, off = pair ? ps + M(1f, 3f) : 0f;
        foreach (float o in pair ? new[] { -off, off } : new[] { 0f })
            Box("hull", stacked ? new NV3(0, o, sm) : new NV3(o, 0, sm), new NV3(ps, ps, sl), ch, hab: true);
        float ss = !pair ? s : stacked ? ps : off + ps, sy = !pair ? s : stacked ? off + ps : ps;
        if (R.Chance(0.4f))
        {
            float kh = M(2f, 5f), up = R.Chance(0.6f) ? 1 : -1;
            Taper("trim", sm - sl * 0.8f, sm + sl * 0.8f, (0, up * (sy + kh * 0.5f)), (ss * 0.35f, kh * 0.3f), (0, up * (sy + kh * 0.5f)), (ss * 0.45f, kh), chamfer: 0.4f);
        }
        Collars(pl, ve0, 0, 0, ss, sy, ch);
        // Sponsons, or a hangar slung under the spine.
        int sponsons = Role is "carrier" or "warship" ? R.Between(1, 2) : R.Chance(0.6f) ? 1 : 0;
        if (sponsons > 0 && R.Chance(0.25f))
        {
            float hl = L * R.Float(0.2f, 0.3f), hw = ss * R.Float(0.9f, 1.3f), hh = M(4.5f, 6.5f), hv = R.Float(pl + hl / 2 + M(3f), ve0 - hl / 2 - M(3f));
            Box("hull", new NV3(0, -sy - hh + 1, hv), new NV3(hw, hh, hl / 2), 0.3f, hab: true);
            Mouth(0, -sy - hh + 1, hv - hl / 2, hw, hh);
            sponsons--;
        }
        Sponsons(sponsons, pl, ve0, ss, s);
        if (R.Chance(0.45f))
            Mast(new NV3(MathF.Round(pu + R.Float(-0.5f, 0.5f) * pw), py, MathF.Round(pl * R.Float(0.6f, 0.9f))), NV3.UnitY, M(5f, 10f), R.Chance(0.6f) ? new NV3(R.Float(-1f, 1f), 0.6f, -1f) : null);
        Guns(Battery, 2, L - 2);
    }

    /// <summary>
    /// The monolith, a great lozenge at range: one massive slab from a hammer-faced prow swelling back into the engine
    /// block, terraces of shorter slabs stacked on its back and its belly alike (never a deck with a tower on it), each to
    /// one side, armour collars round it, sponsons or none.
    /// </summary>
    void Monolith()
    {
        float s = M(5.5f, 8.5f), ch = R.Chance(0.5f) ? 0.45f : 0.12f;
        int rows = R.Between(1, 2), cols = rows == 1 ? R.Between(4, 5) : R.Between(3, 4);
        float ew = s * R.Float(2.4f, 3.2f), eh = s * (rows == 1 ? R.Float(1.4f, 1.8f) : R.Float(1.9f, 2.5f)), el = L * R.Float(0.12f, 0.18f), ey = R.Float(-0.3f, 0.3f) * s;
        float ve0 = Engines(0, ey, ew, eh, rows, cols, el, R.Chance(0.4f));
        // The slab: its section from the prow's face to most of the engine block's.
        float pw = s * R.Float(1.5f, 2.1f), ph = s * R.Float(1.4f, 1.9f), py = R.Float(-0.3f, 0.3f) * s, bw = ew * R.Float(0.7f, 0.9f), bh = eh * R.Float(0.8f, 1f);
        Taper("hull", 0, ve0 + M(2f), (0, py), (pw * 0.8f, ph * 0.65f), (0, ey), (bw, bh), chamfer: ch, hab: true);
        float pl = L * R.Float(0.12f, 0.2f);
        BrowPlates(pl, pw, 0, py);
        Blister(pl * R.Float(0.4f, 0.8f), 0, py);
        // Terraces: shorter slabs stacked one on another, on its back and its belly alike, each stack to its own side.
        foreach (int up in new[] { 1, -1 })
        {
            float side = R.Chance(0.5f) ? 1 : -1, len = (ve0 - pl) * R.Float(0.5f, 0.85f), mid = R.Float(pl + len / 2, ve0 - len / 2 + M(4f)), u = side * R.Float(0.1f, 0.35f) * bw;
            for (int k = 0, n = R.Between(1, 3); k < n && len > M(10f); k++)
            {
                if (Surface(new NV3(u, 0, mid), up * NV3.UnitY) is not { } top) break;
                float tw = pw * R.Float(0.35f, 0.7f) * (1f - 0.15f * k), tt = M(2.5f, 5f);
                Box(k == 0 ? "hull" : "trim", new NV3(u, top.Y + up * (tt - 1f), mid), new NV3(tw, tt, len / 2), 0.3f, hab: true);
                len *= R.Float(0.5f, 0.8f);
                mid += R.Float(-0.15f, 0.15f) * len;
                u += side * R.Float(0f, 0.2f) * tw;
            }
        }
        // Collars round the slab where it is wide enough to wear them.
        if (R.Chance(0.6f))
        {
            float every = M(10f, 18f);
            for (float v = pl + every * R.Float(0.3f, 1f); v < ve0 - M(4f); v += every)
            {
                float t = v / (ve0 + M(2f));
                Box("trim", new NV3(0, py + (ey - py) * t, v), new NV3(pw + (bw - pw) * t + M(1.5f), ph + (bh - ph) * t + M(1.5f), M(1f)), ch);
            }
        }
        Sponsons(Role is "carrier" or "warship" ? R.Between(0, 2) : R.Chance(0.3f) ? 1 : 0, pl, ve0, (pw + bw) / 2, s);
        Guns(Battery, 2, L - 2);
    }

    /// <summary>
    /// Twin hulls, an H or a tuning fork at range: two long slab hulls side by side, never the same length, a hammer
    /// cross-block joining their bows (or one hull's alone, the other set back), a cross-beam amidships, each its own
    /// engine block; the gap between them a hangar bay open forward.
    /// </summary>
    void Twin()
    {
        float s = M(4.5f, 6.5f), gap = M(10f, 18f), hu = MathF.Round(s + gap / 2), ch = R.Chance(0.5f) ? 0.5f : 0.15f;
        float ey = R.Float(-0.3f, 0.3f) * s, pl = L * R.Float(0.12f, 0.18f), ph = s * R.Float(1.3f, 1.8f), py = R.Float(-0.3f, 0.3f) * s;
        int rows = R.Between(1, 2), cols = R.Between(1, 2);
        float ve0 = new[] { -hu, hu }.Select(u => Engines(u, ey, s * R.Float(1.2f, 1.6f), s * R.Float(1.3f, 1.9f), rows, cols, L * R.Float(0.12f, 0.18f), R.Chance(0.25f))).Max();
        // The hulls: one set back behind the cross-block's line now and then.
        int back = R.Chance(0.4f) ? (R.Chance(0.5f) ? 1 : -1) : 0;
        foreach (int side in new[] { -1, 1 })
        {
            float v0 = side == back ? pl + M(10f, 25f) : pl * 0.5f;
            Box("hull", new NV3(side * hu, 0, (v0 + ve0 + M(2f)) / 2), new NV3(s, s * R.Float(0.9f, 1.2f), (ve0 + M(2f) - v0) / 2), ch, hab: true);
            Collars(v0, ve0, side * hu, 0, s, s, ch);
        }
        // The hammer cross-block on the bows, as wide as both hulls and more.
        float pw = hu + s + M(2f, 6f), pu = -back * hu;
        if (back != 0) pw = s * R.Float(1.8f, 2.4f);
        Taper("hull", 0, pl, (pu, py), (pw * 0.75f, ph * 0.6f), (pu, py), (pw, ph), chamfer: 0.25f, hab: true);
        BrowPlates(pl, pw, pu, py);
        Blister(pl * R.Float(0.4f, 0.7f), pu, py);
        // The cross-beam amidships, and the bay between the hulls open forward under it.
        float cv = R.Float(0.55f, 0.75f) * ve0;
        Box("trim", new NV3(0, R.Float(-0.5f, 0.5f) * s, cv), new NV3(hu, s * 0.6f, M(4f, 8f)), 0.3f);
        Guns(Battery, 2, L - 2);
    }
}
