#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using NV3 = System.Numerics.Vector3;

namespace PoiGenerator.Gen;

/// <summary>
/// Freeholders: open trusses carrying irregular lumps. Whatever the plan, the shared traits are bare trusses, mismatched
/// modules clamped on at odd angles and in odd sizes (habitat cans, containers, tanks, cargo cages, radiators), salvaged
/// drives of different sizes on a thrust frame, a small command pod on the nose, scrap colours. Three body plans: a long
/// spine; a raft of trusses side by side; a pusher tug shoving a train of great modules.
/// </summary>
public sealed partial class ShipForm
{
    void Freeholders()
    {
        switch (R.PickWeighted(Role switch { "freighter" or "miner" => new[] { 1f, 1f, 1.5f }, "liner" => new[] { 1f, 1.5f, 0.5f }, _ => new[] { 1.5f, 1f, 0.7f } }))
        {
            case 1: Plan = "raft"; Raft(); break;
            case 2: Plan = "pusher"; Pusher(); break;
            default: Plan = "spine"; Spine(); break;
        }
    }

    /// <summary>The command pod on a nose at (cu, cy) from v0: a small box or can drawn in at its front, some machinery on it. Its length and radius.</summary>
    (float Len, float R) CommandPod(float v0, float cu, float cy)
    {
        float pl = Roomy(M(11f, 16f), 15f), pr = Roomy(M(5.5f, 7.5f), 7.5f);
        Taper("hull", v0, v0 + pl, (cu, cy), (pr * 0.55f, pr * 0.5f), (cu, cy), (pr, pr * 0.85f), round: R.Chance(0.5f), chamfer: 0.5f, hab: true);
        Clutter(new NV3(cu - pr, cy - pr, v0 + 2), new NV3(cu + pr, cy + pr * 0.85f, v0 + pl), R.Range(2, 4), R.Between(1, 3));
        if (R.Chance(0.6f)) Mast(new NV3(cu, cy, MathF.Round(v0 + pl * 0.6f)), NV3.UnitY, M(4f, 8f), new NV3(R.Float(-1f, 1f), 0.5f, -1f));
        return (pl, pr);
    }

    /// <summary>
    /// Salvaged drives of different sizes at the stern about (cu, cy), spread over half widths su and sy, each its own
    /// housing, on a thrust frame deep enough to reach into every housing. The frame's front.
    /// </summary>
    float SalvagedDrives(float cu, float cy, float su, float sy, int fewest, int most)
    {
        var bells = new List<(float U, float Y, float R)>();
        int want = R.Between(fewest, most);
        for (int tries = 0; tries < 120 && bells.Count < want; tries++)
        {
            float r = bells.Count == 0 ? M(6f, 9f) : M(3.5f, 6f), u = cu + R.Float(-1f, 1f) * su, y = cy + R.Float(-1f, 1f) * sy;
            if (bells.Any(b => MathF.Sqrt((b.U - u) * (b.U - u) + (b.Y - y) * (b.Y - y)) < b.R + r + M(3f))) continue;
            bells.Add((u, y, r));
        }
        float vt = L - bells.Max(b => b.R) * 1.9f, first = float.MaxValue, last = float.MinValue;
        foreach (var (u, y, r) in bells)
        {
            float throat = vt - R.Float(0f, 4f) * K, front = throat - Bell(new NV3(u, y, throat), r, r * R.Float(1.4f, 1.9f), reach: M(10f, 18f)) + 1f;
            (first, last) = (MathF.Min(first, front), MathF.Max(last, front));
        }
        float fu0 = bells.Min(b => b.U - b.R), fu1 = bells.Max(b => b.U + b.R), fy0 = bells.Min(b => b.Y - b.R), fy1 = bells.Max(b => b.Y + b.R), fv = first - M(3f);
        Box("machine", new NV3((fu0 + fu1) / 2, (fy0 + fy1) / 2, (fv + last + 1.5f) / 2), new NV3((fu1 - fu0) / 2 + 1, (fy1 - fy0) / 2 + 1, (last + 1.5f - fv) / 2), 0.3f, hab: true);
        return fv;
    }

    /// <summary>What a Freeholder hangs on its trusses, by what the ship is for.</summary>
    string[] Kinds => Role switch
    {
        "liner" => new[] { "can", "can", "can", "tank", "containers", "radiator" },
        "miner" => new[] { "cage", "cage", "tank", "can", "can", "containers", "radiator" },
        "warship" => new[] { "can", "can", "tank", "containers", "radiator", "cage" },
        _ => new[] { "containers", "containers", "cage", "can", "can", "tank", "radiator" },
    };

    /// <summary>
    /// The spine: a long open truss from the pod to the thrust frame, now and then jogged off its line where two were
    /// joined (a junction box at each joint), its modules clamped round it leaning to one side, one of them now and then a
    /// great hab can or tank. A thin spine carrying irregular lumps.
    /// </summary>
    void Spine()
    {
        int th = Mi(2, 4);
        var (pl, _) = CommandPod(0, 0, MathF.Round(R.Float(-2f, 2f) * K));
        float fv = SalvagedDrives(0, 0, th + M(9f), th + M(8f), 2, 5), t0 = pl - M(2f), t1 = fv + M(2f);
        int bay = Mi(5, 7, 3);
        var legs = new List<(float V0, float V1, NV3 Off)>();
        if (R.Chance(0.4f) && t1 - t0 > M(60f))
        {
            float j0 = MathF.Round(t0 + (t1 - t0) * R.Float(0.25f, 0.4f)), j1 = MathF.Round(t0 + (t1 - t0) * R.Float(0.6f, 0.75f));
            NV3 jog = Round(R.Range(0, 8) * MathF.PI / 4) * M(4f, 7f);
            jog = new NV3(MathF.Round(jog.X), MathF.Round(jog.Y), 0f);
            legs.Add((t0, j0, NV3.Zero));
            legs.Add((j0, j1, jog));
            legs.Add((j1, t1, NV3.Zero));
            foreach (float j in new[] { j0, j1 })
                Box("machine", new NV3(jog.X / 2, jog.Y / 2, j), new NV3(MathF.Abs(jog.X) / 2 + th + M(1.5f), MathF.Abs(jog.Y) / 2 + th + M(1.5f), M(2.5f)), 0.3f);
        }
        else legs.Add((t0, t1, NV3.Zero));
        foreach (var (a0, a1, off) in legs) Truss(a0, a1, th, bay, off.X, off.Y);
        float lean = R.Range(0, 8) * MathF.PI / 4, v = t0 + M(1f, 5f);
        bool great = R.Chance(0.4f);
        while (v < t1 - M(8f))
        {
            float a = R.Chance(0.7f) ? lean + R.Range(-1, 2) * MathF.PI / 4 : R.Range(0, 8) * MathF.PI / 4;
            var leg = legs.First(l => v < l.V1 || l == legs[^1]);
            // (Never across a joint.)
            if (v + M(6f) > leg.V1 && leg != legs[^1])
            {
                v = leg.V1 + M(3f);
                continue;
            }
            bool huge = great && v > t0 + (t1 - t0) * 0.3f;
            float len = Module(huge ? R.Pick(new[] { "can", "tank" }) : R.Pick(Kinds), Round(a), th, v, leg.Off, huge);
            great &= !huge;
            if (!R.Chance(0.3f)) v += len + M(1f, 6f);
        }
        // Fuel lines down the truss, a few guns.
        Pipe(new NV3(th + 1, -th, t0 + 2), new NV3(th + 1, -th, t1 - 2));
        Guns(R.Between(2, 4), 2, t1);
    }

    /// <summary>
    /// The raft: two or three trusses side by side tied by cross beams, the pod on one truss's nose, the others' noses
    /// short of it, modules clamped above, below and outboard, a broad bank of salvaged drives. Wide, flat and lumpy.
    /// </summary>
    void Raft()
    {
        int n = R.Between(2, 3), th = Mi(2, 3), lead = R.Range(0, n), bay = Mi(5, 7, 3);
        float gap = MathF.Round(M(11f, 17f));
        float[] us = Enumerable.Range(0, n).Select(i => MathF.Round((i - (n - 1) / 2f) * gap + R.Float(-1.5f, 1.5f) * K)).ToArray();
        var (pl, _) = CommandPod(0, us[lead], 0);
        float lo = us.Min(), hi = us.Max();
        float fv = SalvagedDrives((lo + hi) / 2, 0, (hi - lo) / 2 + th + M(5f), th + M(8f), 3, 6), t1 = fv + M(2f);
        float[] t0 = us.Select((u, i) => i == lead ? pl - M(2f) : MathF.Round(M(8f, 30f))).ToArray();
        for (int i = 0; i < n; i++)
        {
            Truss(t0[i], t1, th, bay, us[i], 0);
            if (i != lead) Box("machine", new NV3(us[i], 0, t0[i]), new NV3(th + M(1.5f), th + M(1.5f), M(2f)), 0.3f);
        }
        // Cross beams tying them, never two the same distance apart.
        float from = t0.Max() + M(2f);
        for (int k = 0, ties = R.Between(2, 4); k < ties; k++)
        {
            float v = MathF.Round(from + (t1 - from) * (k + R.Float(0.2f, 0.8f)) / ties);
            Box("machine", new NV3((lo + hi) / 2, R.Float(-1f, 1f) * th, v), new NV3((hi - lo) / 2 + th + M(1f), M(1.5f), M(2f)), 0.3f);
        }
        // Modules up, down and outboard of each truss.
        for (int i = 0; i < n; i++)
        {
            var ways = new List<float> { MathF.PI / 2, -MathF.PI / 2 };
            if (i == 0) ways.Add(MathF.PI);
            if (i == n - 1) ways.Add(0f);
            for (float v = t0[i] + M(3f, 6f); v < t1 - M(8f);)
            {
                float len = Module(R.Pick(Kinds), Round(R.Pick(ways)), th, v, new NV3(us[i], 0, 0), R.Chance(0.08f));
                if (!R.Chance(0.25f)) v += len + M(2f, 9f);
            }
        }
        Guns(R.Between(2, 4), 2, t1);
    }

    /// <summary>
    /// The pusher: a tug (a hull of scrap with a cab on it and its drives behind) shoving a train of great modules in a
    /// line ahead of it, container blocks, tanks and cages, each coupled to the next by a short truss, a small pod on the
    /// train's nose. A heavy tug and a string of lumps.
    /// </summary>
    void Pusher()
    {
        int th = Mi(2, 3), bay = Mi(4, 6, 3);
        var (pl, _) = CommandPod(0, 0, 0);
        float fv = SalvagedDrives(0, 0, M(10f), M(9f), 2, 4);
        // The tug and its cab.
        float tl = M(14f, 22f), tw = Roomy(M(5.5f, 8.5f), 7f), tt = Roomy(M(4.5f, 7.5f), 6.5f), tv1 = fv + M(2f), tv0 = tv1 - tl, tu = R.Float(-1f, 1f) * K, ty = R.Float(-1f, 1f) * K;
        Box(Scrap(), new NV3(tu, ty, (tv0 + tv1) / 2), new NV3(tw, tt, tl / 2), 0.35f, hab: true);
        Clutter(new NV3(tu - tw, ty - tt, tv0), new NV3(tu + tw, ty + tt, tv1), R.Range(0, 4), R.Between(2, 4));
        Box("trim", new NV3(tu + R.Float(-0.5f, 0.5f) * tw, ty + tt + M(2f), tv0 + M(4f)), new NV3(M(3f), M(2.5f), M(4f)), 0.5f, hab: true);
        // The train from the pod back to the tug, a coupling ahead of every load.
        float v = pl - 1f;
        while (true)
        {
            float coupling = MathF.Round(M(4f, 8f)), room = tv0 - v - coupling - M(4f);
            if (room < M(10f)) break;
            float len = MathF.Min(room, M(12f, 24f)), mid = v + coupling + len / 2, ou = R.Float(-2f, 2f) * K, oy = R.Float(-2f, 2f) * K;
            Truss(MathF.Round(v), MathF.Round(v + coupling + 1f), th, bay);
            switch (R.Range(0, 3))
            {
                case 0:
                    // A block of containers lashed together, each its own colour.
                    float cw = M(6f, 10f), ch = M(5f, 9f);
                    Box(Scrap(), new NV3(ou, oy, mid), new NV3(cw, ch, len / 2), 0.1f);
                    for (int i = 0, k = R.Between(2, 4); i < k; i++)
                    {
                        NV3 o = Round(R.Range(0, 4) * MathF.PI / 2);
                        float h = M(1.5f, 2.5f), out_ = MathF.Abs(o.X) > 0.5f ? cw : ch;
                        Plate(Cargo(), new NV3(ou, oy, mid) + o * (out_ + h - 0.3f) + new NV3(0, 0, R.Float(-0.3f, 0.3f) * len), NV3.UnitZ, new NV3(-o.Y, o.X, 0f), new NV3(M(3.5f, 6.5f), M(1.5f, 2.5f), h));
                    }
                    break;
                case 1:
                    Can("tank", 2, new NV3(ou, oy, mid), M(6f, 9.5f), len, domes: true);
                    break;
                default:
                    // An open cage round its load.
                    int half = Mi(5, 8, 3);
                    Truss(MathF.Round(mid - len / 2), MathF.Round(mid + len / 2), half, bay);
                    Box(Scrap(), new NV3(0, -half + M(2f), mid), new NV3(half, M(2f), len / 2 - 1f));
                    foreach (float end in new[] { mid - len / 2, mid + len / 2 }) Box("machine", new NV3(0, 0, MathF.Round(end)), new NV3(half, half, 1f), 0.4f);
                    break;
            }
            v = mid + len / 2;
        }
        Truss(MathF.Round(v - 1f), MathF.Round(tv0 + 1f), th, bay);
        Guns(R.Between(2, 4), 2, fv);
    }

    /// <summary>One module clamped to a truss (its axis off the ship's by <paramref name="off"/>) out along <paramref name="dir"/> from v, a great one if asked: its length down the truss.</summary>
    float Module(string kind, NV3 dir, int th, float v, NV3 off, bool great = false)
    {
        bool square = MathF.Abs(dir.X) > 0.9f || MathF.Abs(dir.Y) > 0.9f;
        int axis = MathF.Abs(dir.X) > 0.9f ? 0 : 1;
        float root = th + M(1.5f), len;
        NV3 side = new(-dir.Y, dir.X, 0f);
        if (kind == "cage" && !square) kind = "containers";
        if (kind == "radiator" && !square) kind = "tank";
        switch (kind)
        {
            case "can":
            {
                float r = Roomy(great ? M(8.5f, 11.5f) : M(5f, 9f), 7f);
                string paint = R.Chance(0.5f) ? "hull" : Scrap();
                if (square && R.Chance(0.35f))
                {
                    // Across the truss, sticking out.
                    float reach = Roomy(M(10f, 18f), 14f);
                    len = 2 * r;
                    Can(paint, axis, dir * (root + reach / 2) + new NV3(off.X, off.Y, v + r), r, reach, domes: true, hab: true);
                }
                else
                {
                    len = Roomy(great ? M(16f, 26f) : M(8f, 18f), 14f);
                    Can(paint, 2, dir * (root + r) + new NV3(off.X, off.Y, v + len / 2), r, len, domes: true, hab: true);
                }
                break;
            }
            case "tank":
            {
                float r = great ? M(7.5f, 10.5f) : M(3.5f, 6.5f);
                if (R.Chance(0.5f))
                {
                    len = 2 * r;
                    Sphere("tank", dir * (root + r) + new NV3(off.X, off.Y, v + r), new NV3(r));
                }
                else
                {
                    len = M(10f, 16f);
                    Can("tank", 2, dir * (root + r) + new NV3(off.X, off.Y, v + len / 2), r, len - r, domes: true);
                }
                break;
            }
            case "cage":
            {
                // An open frame of struts round a load.
                int hu = Mi(3, 4, 2), hy = Mi(3, 4, 2), hv = Mi(4, 7, 3);
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
                // Its load on a pallet, from the truss's face out through the cage's floor.
                float near = th - 0.5f, far = (axis == 0 ? MathF.Abs(c.X - off.X) - hu : MathF.Abs(c.Y - off.Y) - hy) + 3f, depth = (far - near) / 2;
                Box(Scrap(), dir * (near + depth) + new NV3(off.X, off.Y, c.Z), axis == 0 ? new NV3(depth, hy, hv - 1f) : new NV3(hu, depth, hv - 1f));
                break;
            }
            case "radiator":
            {
                len = M(8f, 12f);
                NV3 at = dir * th + new NV3(off.X, off.Y, v + len / 2);
                Radiators(new NV3(MathF.Round(at.X), MathF.Round(at.Y), MathF.Round(at.Z)), dir, R.Between(2, 3), MathF.Round(M(6f, 9f)), len);
                break;
            }
            default:
            {
                // Containers stacked out from the truss, each its own colour.
                var h = new NV3(M(1.5f, 2.5f), M(1.5f, 2.5f), M(3.5f, 6.5f));
                len = 2 * h.Z;
                for (int i = 0, n = R.Between(1, 3); i < n; i++)
                    Plate(Scrap(), dir * (root + h.Y + i * (2 * h.Y - 0.2f)) + side * R.Float(-1f, 1f) * K + new NV3(off.X, off.Y, v + h.Z), NV3.UnitZ, side, new NV3(h.Z, h.X, h.Y));
                break;
            }
        }
        // The clamp: a collar on the truss's face, chord to chord.
        Box("machine", dir * (th + 0.5f) + new NV3(off.X, off.Y, v + len / 2), !square ? new NV3(M(1.5f)) : axis == 0 ? new NV3(M(1.5f), th + 1f, M(1.5f)) : new NV3(th + 1f, M(1.5f), M(1.5f)));
        return len;
    }
}
