#nullable enable
using System;
using System.Linq;
using NV3 = System.Numerics.Vector3;

namespace PoiGenerator.Gen;

/// <summary>
/// Meridian Combine: clean, massive slabs, a hammer or a dumbbell at range whatever the plan. The shared traits are a
/// heavy armoured prow block (a hammerhead layered with plates, its bridge in a blister low in it or on its flank), a long
/// thick spine you can see (square or hex, ribbed with collars or bare), an engine block bigger than the prow with four
/// to eight big bells and drive pods on its flanks, sponsons carrying hangars with open mouths, few greebles. Three body
/// plans: the hammerhead (one spine); twin spines joined by a bridge between a cross-block prow and one great engine
/// block; the cross (one spine, sponsons standing out above, below and to the sides).
/// </summary>
public sealed partial class ShipForm
{
    void Meridian()
    {
        switch (R.PickWeighted(Role switch { "carrier" => new[] { 1f, 1.4f, 1.2f }, "warship" => new[] { 1.2f, 1f, 1.3f }, _ => new[] { 1.2f, 1f, 1f } }))
        {
            case 1: Plan = "twin"; Twin(); break;
            case 2: Plan = "cross"; Cross(); break;
            default: Plan = "hammerhead"; Hammerhead(); break;
        }
    }

    /// <summary>
    /// The engine block about (cu, cy), half w across and h high, el long, its bells (rows by cols) filling its rear face
    /// and flaring past the stern, drive pods on two to four of its faces, a little machinery on it, radiator fins off it
    /// now and then. Its front.
    /// </summary>
    float Engines(float cu, float cy, float w, float h, float el)
    {
        int rows = h > w * 0.8f ? 2 : 1, cols = Math.Clamp((int)MathF.Round(w * 2f / (h * 2f / rows)), 2, 4);
        float br = MathF.Min(w * 2f / cols, h * 2f / rows) * 0.46f, bl = br * R.Float(1.5f, 2f), ve1 = L - bl - R.Float(0f, 3f) * K, ve0 = ve1 - el;
        Taper("hull", ve0, ve1, (cu, cy), (w * 0.82f, h * 0.86f), (cu, cy), (w, h), chamfer: 0.12f, hab: true);
        for (int i = 0; i < cols; i++)
            for (int j = 0; j < rows; j++)
                Bell(new NV3(cu - w + 2f * w * (i + 0.5f) / cols, cy - h + 2f * h * (j + 0.5f) / rows, ve1), br, bl, housing: false);
        DrivePods(cu, cy, w, h, ve0, ve1, 2, 4);
        Clutter(new NV3(cu - w, cy - h, ve0), new NV3(cu + w, cy + h, ve1), R.Chance(0.5f) ? 2 : 3, R.Between(0, 2));
        NV3 o = Out(R.Range(0, 4));
        if (R.Chance(0.3f) && Surface(new NV3(cu, cy, MathF.Round((ve0 + ve1) / 2)), o) is { } root)
            Radiators(new NV3(MathF.Round(root.X), MathF.Round(root.Y), MathF.Round(root.Z)), o, R.Between(2, 4), MathF.Round(M(6f, 10f)), el * 0.6f);
        return ve0;
    }

    /// <summary>
    /// The hammer: an armoured prow block pl long about (pu, py), half pw by ph, its front raked in; stood on end, stepped
    /// with a brow over its face, or drawn to an armoured ram's edge; plates on its brow and chin, the bridge blister in it,
    /// a mast now and then.
    /// </summary>
    void Hammer(float pl, float pw, float ph, float pu, float py)
    {
        int kind = R.Range(0, 4);
        if (kind == 1) (pw, ph) = (ph * 1.1f, pw);
        Taper("hull", 0, pl, (pu, py), kind == 3 ? (pw * 0.95f, ph * 0.22f) : (pw * 0.7f, ph * 0.6f), (pu, py), (pw, ph), chamfer: 0.25f, hab: true);
        if (kind == 2)
        {
            // The brow: wider and stubbier, standing over the block's face.
            float bw = pw * R.Float(1.1f, 1.35f), bh = ph * R.Float(0.35f, 0.5f), up = R.Chance(0.7f) ? 1 : -1;
            Box("hull", new NV3(pu, py + up * (ph + bh * 0.6f), pl * 0.4f - M(2f)), new NV3(bw, bh, pl * 0.4f + M(2f)), 0.3f);
        }
        for (int k = R.Between(1, 3); k > 0; k--)
        {
            float up = R.Chance(0.6f) ? 1 : -1, t = M(1.5f, 2.5f), w = pw * R.Float(0.55f, 1f), du = R.Float(-0.25f, 0.25f) * pw;
            if (Surface(new NV3(pu + du, py, pl - 1f), up * NV3.UnitY) is not { } rest) continue;
            float y = rest.Y + up * (t - 1f);
            Taper("trim", -R.Float(0f, 4f) * K, pl, (pu + du, y), (w * 0.75f, t * 0.6f), (pu + du, y), (w, t), chamfer: 0.4f);
        }
        // The combat bridge: a glazed blister low in the head, or on its flank.
        bool flank = R.Chance(0.35f);
        NV3 bo = flank ? (R.Chance(0.5f) ? NV3.UnitX : -NV3.UnitX) : -NV3.UnitY;
        if (Surface(new NV3(pu, py, pl * R.Float(0.4f, 0.7f)), bo) is { } p)
        {
            NV3 h = flank ? new NV3(M(3.5f), M(3.5f), M(4.5f, 6.5f)) : new NV3(M(4.5f, 6.5f), M(3.5f), M(4.5f, 6.5f));
            Box("trim", p + bo * (MathF.Abs(bo.X) > 0.5f ? h.X : h.Y) * 0.5f, h, 0.6f, hab: true);
        }
        if (R.Chance(0.4f)) Mast(new NV3(MathF.Round(pu + R.Float(-0.5f, 0.5f) * pw), py, MathF.Round(pl * R.Float(0.6f, 0.9f))), NV3.UnitY, M(5f, 10f), R.Chance(0.6f) ? new NV3(R.Float(-1f, 1f), 0.6f, -1f) : null);
    }

    /// <summary>A spine from v0 to v1 about (cu, cy), half s across and up, its section square or hex, ribbed with armour collars or bare, a keel slab down its back or belly now and then.</summary>
    void Spine(float v0, float v1, float cu, float cy, float s, float ch)
    {
        Box("hull", new NV3(cu, cy, (v0 + v1) / 2), new NV3(s, s, (v1 - v0) / 2), ch, hab: true);
        if (R.Chance(0.6f))
        {
            float every = M(8f, 16f), thick = R.Chance(0.5f) ? M(1.5f) : M(2.5f);
            for (float v = v0 + R.Float(M(3f), every); v < v1 - M(3f); v += every) Box("trim", new NV3(cu, cy, v), new NV3(s + thick, s + thick, M(1f)), ch);
        }
        if (R.Chance(0.35f))
        {
            float kh = M(2f, 4f), up = R.Chance(0.6f) ? 1 : -1, mid = (v0 + v1) / 2, half = (v1 - v0) / 2;
            Taper("trim", mid - half * 0.75f, mid + half * 0.75f, (cu, cy + up * (s + kh * 0.5f)), (s * 0.35f, kh * 0.3f), (cu, cy + up * (s + kh * 0.5f)), (s * 0.45f, kh), chamfer: 0.4f);
        }
    }

    /// <summary>A sponson standing out from a spine (half s about (cu, cy)) along o, l long about v, on a pylon, its hangar's mouth open forward.</summary>
    void Sponson(NV3 o, float v, float l, float cu, float cy, float s)
    {
        float sw = Roomy(M(4.5f, 8.5f), 6.5f), sh = Roomy(M(3.5f, 6f), 6f), gap = M(3f, 8f), out_ = MathF.Abs(o.X) > 0.5f ? sw : sh;
        NV3 c = new NV3(cu, cy, v) + o * (s + gap + out_), h = MathF.Abs(o.X) > 0.5f ? new NV3(sw, sh, l / 2) : new NV3(sh * 1.4f, sh, l / 2);
        Box("hull", c, h, 0.25f, hab: true);
        Box("trim", new NV3(cu, cy, v + l * 0.1f) + o * (s + gap / 2f), MathF.Abs(o.X) > 0.5f ? new NV3(gap / 2f + M(1.5f), MathF.Min(sh, s) * 0.6f, l * 0.3f) : new NV3(MathF.Min(sh, s) * 0.6f, gap / 2f + M(1.5f), l * 0.3f));
        Cut(new Loft { S0 = v - l / 2 - 1, S1 = v - l / 2 + M(7f), A0 = c.X, B0 = c.Y, A1 = c.X, B1 = c.Y, HA0 = h.X - 1.5f, HB0 = h.Y - 1.5f, HA1 = h.X - 1.5f, HB1 = h.Y - 1.5f });
    }

    /// <summary>Guns by what the ship is for.</summary>
    int Battery => Role == "warship" ? R.Between(10, 14) : Role == "carrier" ? R.Between(6, 9) : R.Between(4, 6);

    /// <summary>The hammerhead: a hammer prow, one long spine, a bigger engine block and sponsons amidships, one side or both, never alike.</summary>
    void Hammerhead()
    {
        float s = Roomy(M(5.5f, 8f), 6.5f), ch = R.Chance(0.5f) ? 0.55f : 0.15f, ey = R.Float(-0.3f, 0.3f) * s, el = L * R.Float(0.14f, 0.2f);
        float pl = L * R.Float(0.12f, 0.18f), pw = s * R.Float(2.2f, 2.9f), ph = s * R.Float(1.4f, 1.9f);
        float ew = MathF.Max(pw * R.Float(1.05f, 1.3f), s * 2.6f), eh = MathF.Max(ph * R.Float(1.1f, 1.4f), s * 2f);
        float ve0 = Engines(R.Float(-0.2f, 0.2f) * s, ey, ew, eh, el);
        Hammer(pl, pw, ph, R.Float(-0.3f, 0.3f) * s, R.Float(-0.4f, 0.4f) * s);
        Spine(pl - M(2f), ve0 + M(2f), 0, 0, s, ch);
        int side = R.Chance(0.5f) ? 1 : -1;
        for (int k = 0, n = Role is "carrier" or "warship" ? R.Between(1, 2) : 1; k < n; k++, side = -side)
        {
            float l = L * R.Float(0.14f, 0.24f);
            Sponson(NV3.UnitX * side, R.Float(pl + l / 2 + M(4f), ve0 - l / 2 - M(4f)), l, 0, R.Float(-0.5f, 0.5f) * s, s);
        }
        Guns(Battery, 2, L - 2);
    }

    /// <summary>
    /// Twin spines, a tuning fork or an H at range: two long spines side by side with clear space between them, joined by
    /// a bridge block amidships, a hammer cross-block over both their bows and one great engine block over both their
    /// sterns, a sponson outboard of one or both.
    /// </summary>
    void Twin()
    {
        float s = Roomy(M(4f, 5.5f), 6f), gap = M(12f, 18f), hu = MathF.Round(s + gap / 2), ch = R.Chance(0.5f) ? 0.5f : 0.15f, el = L * R.Float(0.14f, 0.2f);
        float pl = L * R.Float(0.11f, 0.16f), pw = hu + s + M(3f, 7f), ph = s * R.Float(1.8f, 2.4f), py = R.Float(-0.3f, 0.3f) * s;
        float ew = pw * R.Float(1.05f, 1.25f), eh = ph * R.Float(1.15f, 1.4f);
        float ve0 = Engines(R.Float(-0.15f, 0.15f) * s, R.Float(-0.3f, 0.3f) * s, ew, eh, el);
        Hammer(pl, pw, ph, 0, py);
        foreach (int side in new[] { -1, 1 }) Spine(pl - M(2f), ve0 + M(2f), side * hu, R.Float(-0.3f, 0.3f) * s, s, ch);
        // The bridge between them, its own length, somewhere amidships.
        float bv = R.Float(0.35f, 0.65f) * (ve0 + pl);
        Box("trim", new NV3(0, R.Float(-0.4f, 0.4f) * s, bv), new NV3(hu, s * R.Float(0.6f, 0.9f), M(4f, 9f)), 0.3f, hab: true);
        int side0 = R.Chance(0.5f) ? 1 : -1;
        for (int k = 0, n = R.Between(1, 2); k < n; k++, side0 = -side0)
        {
            float l = L * R.Float(0.12f, 0.2f);
            Sponson(NV3.UnitX * side0, R.Float(pl + l / 2 + M(4f), ve0 - l / 2 - M(4f)), l, side0 * hu, 0, s);
        }
        Guns(Battery, 2, L - 2);
    }

    /// <summary>The cross: a hammer prow, one long spine, a bigger engine block, and sponsons standing out from the spine above, below and to the sides, each its own size and place: a cross end on.</summary>
    void Cross()
    {
        float s = Roomy(M(5f, 7.5f), 6.5f), ch = R.Chance(0.5f) ? 0.55f : 0.15f, el = L * R.Float(0.13f, 0.18f);
        float pl = L * R.Float(0.12f, 0.17f), pw = s * R.Float(2.1f, 2.7f), ph = s * R.Float(1.6f, 2.2f);
        float ew = MathF.Max(pw * R.Float(1.05f, 1.25f), s * 2.6f), eh = MathF.Max(ph * R.Float(1.05f, 1.25f), s * 2.2f);
        float ve0 = Engines(0, R.Float(-0.2f, 0.2f) * s, ew, eh, el);
        Hammer(pl, pw, ph, R.Float(-0.25f, 0.25f) * s, R.Float(-0.3f, 0.3f) * s);
        Spine(pl - M(2f), ve0 + M(2f), 0, 0, s, ch);
        int[] faces = { 0, 1, 2, 3 };
        R.Shuffle(faces);
        foreach (int face in faces.Take(R.Between(3, 4)))
        {
            float l = L * R.Float(0.12f, 0.22f);
            Sponson(Out(face), R.Float(pl + l / 2 + M(4f), ve0 - l / 2 - M(4f)), l, 0, 0, s);
        }
        Guns(Battery, 2, L - 2);
    }
}
