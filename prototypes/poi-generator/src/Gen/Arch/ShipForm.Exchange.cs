#nullable enable
using System;
using NV3 = System.Numerics.Vector3;

namespace PoiGenerator.Gen;

/// <summary>
/// Ledger Exchange: fat and round, bristling with blocks. Whatever the plan, the shared traits are fat round pods in
/// cradles, rings of colour-coded container pods all round them (a slot or two empty), a bulbous hab sphere forward on a
/// neck with the bridge in it, a thrust frame with its drives set in an A and round tanks on it, now and then a cargo crane.
/// Three body plans: the seed pod (one fat core); the tandem (two or three pods in a row on necks); the wheel (a short hub
/// inside a great wheel of containers on spokes).
/// </summary>
public sealed partial class ShipForm
{
    void Exchange()
    {
        switch (R.PickWeighted(Role switch { "freighter" or "miner" => new[] { 1f, 1.2f, 1.2f }, "liner" => new[] { 1f, 1.3f, 0.8f }, _ => new[] { 1.2f, 1f, 1f } }))
        {
            case 1: Plan = "tandem"; Tandem(); break;
            case 2: Plan = "wheel"; Wheel(); break;
            default: Plan = "seedpod"; SeedPod(); break;
        }
    }

    /// <summary>A fat pod about v cv, cl long, its radii cr across and cyr up, in a cradle of girders at its corners.</summary>
    void Pod(float cv, float cl, float cr, float cyr, float cy = 0f)
    {
        Sphere("hull", new NV3(0, cy, cv), new NV3(cr, cyr, cl / 2), hab: true);
        float half = cl / 2 * 0.82f, gr = 1f / MathF.Sqrt(0.5f / (cr * cr) + 0.5f / (cyr * cyr)) - 0.5f;
        foreach (int a in new[] { -1, 1 })
            foreach (int b in new[] { -1, 1 })
                Box("machine", new NV3(a * gr * 0.7071f, cy + b * gr * 0.7071f, cv), new NV3(M(1f), M(1f), half));
    }

    /// <summary>Rings of containers on hoops round a pod (about v cv, cl long, radii cr and cyr), all round it, a slot or two empty, stacked one to three deep.</summary>
    void ContainerRings(float cv, float cl, float cr, float cyr, int rings, float cy = 0f)
    {
        int slots = R.Pick(new[] { 8, 8, 12 });
        float half = cl / 2 * 0.82f, chh = M(1.5f, 2.5f), clen = MathF.Min(M(4.5f, 6.5f), half / rings);
        for (int k = 0; k < rings; k++)
        {
            float v = cv - half + 2 * half * (k + 0.5f) / rings, f = MathF.Sqrt(MathF.Max(0.1f, 1f - MathF.Pow((v - cv) / (cl / 2), 2)));
            Add(new Hoop { C = new NV3(0, cy, v), R = MathF.Max(cr, cyr) * f + 0.5f, T = MathF.Max(Thin, 1.2f * K) }, "machine");
            int deep = R.Between(1, 3);
            float a0 = R.Chance(0.5f) ? 0f : MathF.PI / slots;
            for (int j = 0; j < slots; j++)
            {
                if (R.Chance(0.12f)) continue;
                NV3 o = Round(a0 + j * MathF.Tau / slots), t = new(-o.Y, o.X, 0f);
                float re = 1f / MathF.Sqrt(o.X * o.X / (cr * f * cr * f) + o.Y * o.Y / (cyr * f * cyr * f));
                for (int i = 0, n = Math.Max(1, deep + R.Range(-1, 2)); i < n; i++)
                    Plate(Cargo(), o * (re + chh - 0.3f + i * (2 * chh - 0.2f)) + new NV3(0, cy, v), NV3.UnitZ, t, new NV3(clen, chh, chh));
            }
        }
    }

    /// <summary>The hab sphere forward (its middle at v hv, hr across, hy off the line) on a neck back to v neck, the bridge in it, a dish on it now and then.</summary>
    void HabSphere(float hv, float hr, float hy, float neck)
    {
        Sphere("hull", new NV3(0, hy, hv), new NV3(hr), hab: true);
        Can("machine", 2, new NV3(0, hy * 0.5f, (hv + neck) / 2 + 1), hr * 0.45f, neck - hv + M(4f));
        if (R.Chance(0.6f)) Mast(new NV3(0, hy, MathF.Round(hv)), NV3.UnitY, M(4f, 7f), new NV3(0, 0.6f, -1f));
    }

    /// <summary>The thrust frame from v fv, fr across, its drives in an A behind it, round tanks clustered on it.</summary>
    void ADrives(float fv, float fr)
    {
        Can("machine", 2, new NV3(0, 0, fv + M(4f)), fr, M(10f));
        int nb = R.Between(3, 5);
        float bb = fr * R.Float(0.4f, 0.48f), bv = fv + M(9f);
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
            float tr = M(3.5f, 5.5f);
            Sphere("tank", Round(ta + k * MathF.Tau / n + R.Float(-0.3f, 0.3f)) * (fr + tr - 1f) + new NV3(0, 0, fv + R.Float(-2f, 3f) * K), new NV3(tr));
        }
    }

    /// <summary>A cargo crane off a pod (about v cv, cl long, radii cr and cyr) now and then, a container hanging from it.</summary>
    void Crane(float cv, float cl, float cr, float cyr)
    {
        if (!R.Chance(0.5f)) return;
        float v = cv + R.Float(-0.3f, 0.3f) * cl, f = MathF.Sqrt(1f - MathF.Pow((v - cv) / (cl / 2), 2)), chh = M(1.5f, 2.5f);
        NV3 o = Round(R.Float(0f, MathF.Tau)), foot = o * (MathF.Min(cr, cyr) * f * 0.9f) + new NV3(0, 0, v), tip = foot + o * M(10f, 16f) + new NV3(0, 0, -M(6f, 12f));
        Bar("machine", foot, tip, 1.2f * K, 0.8f * K);
        Plate(Cargo(), tip + o * (chh - 0.5f), NV3.UnitZ, new NV3(-o.Y, o.X, 0f), new NV3(M(4.5f, 6.5f), chh, chh));
    }

    /// <summary>The seed pod: one short, fat core in its cradle ringed with containers, the hab sphere forward, the drives aft. A fat seed pod bristling with blocks.</summary>
    void SeedPod()
    {
        float cr = L * R.Float(0.15f, 0.2f), cyr = cr * R.Float(0.8f, 1.1f), hr = cr * R.Float(0.5f, 0.72f);
        float hv = hr, cv0 = hr * 1.6f + M(2f), cl = L * R.Float(0.46f, 0.56f), cv = cv0 + cl / 2;
        Pod(cv, cl, cr, cyr);
        HabSphere(hv, hr, R.Float(-0.35f, 0.35f) * cr, cv0);
        ContainerRings(cv, cl, cr, cyr, R.Between(2, 4));
        Crane(cv, cl, cr, cyr);
        ADrives(cv + cl / 2 - M(3f), cr * R.Float(0.55f, 0.7f));
        Guns(R.Between(3, 6), cv - cl / 2, cv + cl / 2);
    }

    /// <summary>The tandem: two or three pods of their own sizes in a row on necks, each ringed with containers, sagging off the line, the hab sphere forward, the drives aft. A string of fat pods.</summary>
    void Tandem()
    {
        int n = R.Between(2, 3);
        float hr = L * R.Float(0.07f, 0.1f), hv = hr, v = hr * 1.6f + M(2f), neck = M(4f, 8f), room = L * 0.82f - v - neck * (n - 1), sag = R.Float(-1f, 1f) * L * 0.03f;
        HabSphere(hv, hr, R.Float(-0.3f, 0.3f) * hr, v);
        float last = v, lastR = hr;
        for (int i = 0; i < n; i++)
        {
            float cl = room / n * R.Float(0.8f, 1.2f), cr = MathF.Min(L * R.Float(0.1f, 0.15f), cl * 0.55f), cyr = cr * R.Float(0.8f, 1.1f), cv = v + cl / 2, cy = sag * i;
            if (i > 0) Can("machine", 2, new NV3(0, cy * 0.5f, v - neck / 2), MathF.Min(lastR, cr) * 0.4f, neck + M(6f));
            Pod(cv, cl, cr, cyr, cy);
            ContainerRings(cv, cl, cr, cyr, R.Between(1, 2), cy);
            if (i == n - 1) Crane(cv, cl, cr, cyr);
            (last, lastR) = (cv + cl / 2, cr);
            v = cv + cl / 2 + neck;
        }
        ADrives(last - M(3f), lastR * R.Float(0.6f, 0.75f));
        Guns(R.Between(3, 6), hv, last);
    }

    /// <summary>The wheel: a short fat hub inside one or two great wheels of containers on spokes, the hab sphere forward, the drives aft. A broad drum of blocks round a hub.</summary>
    void Wheel()
    {
        float cr = L * R.Float(0.09f, 0.12f), cyr = cr * R.Float(0.85f, 1.05f), hr = cr * R.Float(0.8f, 1.1f);
        float hv = hr, cv0 = hr * 1.6f + M(2f), cl = L * R.Float(0.4f, 0.5f), cv = cv0 + cl / 2;
        Pod(cv, cl, cr, cyr);
        HabSphere(hv, hr, R.Float(-0.3f, 0.3f) * cr, cv0);
        int wheels = R.Chance(0.35f) ? 2 : 1, spokes = R.Pick(new[] { 4, 6, 8 }), slots = R.Between(16, 26);
        float rw = cr * R.Float(2.5f, 3.2f), chh = M(1.5f, 2.5f), clen = M(4.5f, 7.5f), a0 = R.Float(0f, MathF.Tau);
        for (int w = 0; w < wheels; w++)
        {
            float v = wheels == 1 ? cv + R.Float(-0.1f, 0.1f) * cl : cv + (w - 0.5f) * cl * 0.5f, r = rw * (w == 0 ? 1f : R.Float(0.7f, 0.9f));
            float f = MathF.Sqrt(MathF.Max(0.1f, 1f - MathF.Pow((v - cv) / (cl / 2), 2)));
            Add(new Hoop { C = new NV3(0, 0, v), R = r, T = M(1.5f) }, "machine");
            for (int k = 0; k < spokes; k++)
            {
                NV3 o = Round(a0 + k * MathF.Tau / spokes);
                Bar("machine", o * MathF.Min(cr, cyr) * f * 0.8f + new NV3(0, 0, v), o * r + new NV3(0, 0, v), 1.3f * K);
            }
            // Containers all round the rim, along the axis, one or two deep, a gap here and there.
            for (int j = 0; j < slots; j++)
            {
                if (R.Chance(0.15f)) continue;
                NV3 o = Round(a0 + (j + 0.5f) * MathF.Tau / slots), t = new(-o.Y, o.X, 0f);
                for (int i = 0, deep = R.Between(1, 2); i < deep; i++)
                    Plate(Cargo(), o * (r + chh - 0.3f + i * (2 * chh - 0.2f)) + new NV3(0, 0, v + R.Float(-0.2f, 0.2f) * clen), NV3.UnitZ, t, new NV3(clen, chh, chh));
            }
        }
        ADrives(cv + cl / 2 - M(3f), cr * R.Float(0.6f, 0.75f));
        Guns(R.Between(3, 6), cv - cl / 2, cv + cl / 2);
    }
}
