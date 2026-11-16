#nullable enable
using System;
using NV3 = System.Numerics.Vector3;

namespace PoiGenerator.Gen;

/// <summary>
/// Kessari Clans, machine builders: see-through and symmetric. Whatever the plan, the shared traits are perfect 4-, 6- or
/// 8-fold symmetry round the thrust axis, open frames of bare struts, repeated identical modules each with its status
/// light, a bare core column with the same machinery bay after bay, a spire forward and a ring of drives round a central
/// one aft. Three body plans: the tube (bays of rings, longerons and braces); the vanes (flat lattice fins standing out
/// from the core, a star end on); the wheels (great wheels of modules on a long spindle).
/// </summary>
public sealed partial class ShipForm
{
    void Kessari()
    {
        int n = R.Pick(new[] { 4, 4, 6, 8 }), kind = R.Range(0, 3);
        float ms = M(2.5f, 4.5f), cr = M(2.5f, 3.5f), vp = L * R.Float(0.12f, 0.17f), vs = L - L * R.Float(0.08f, 0.12f);
        // The core column and the spire.
        Can("machine", 2, new NV3(0, 0, (vp * 0.5f + vs) / 2), cr, vs - vp * 0.5f);
        Taper("hull", 0, vp, (0, 0), (0.8f, 0.8f), (0, 0), (cr + M(2.5f), cr + M(2.5f)), round: true);
        int plan = R.PickWeighted(new[] { 1f, 1f, 0.8f });
        Plan = plan switch { 1 => "vanes", 2 => "wheels", _ => "tube" };
        float rd = plan switch
        {
            1 => Vanes(n, kind, ms, cr, vp, vs),
            2 => Wheels(n, kind, ms, cr, vp, vs),
            _ => Tube(n, kind, ms, cr, vp, vs),
        };
        // The drives: a central bell and a ring of n about it, on a round thrust plate.
        float a0 = MathF.PI / n, bb = M(4f, 6f), plate = M(5f), reach = M(8f, 14f);
        rd = MathF.Max(rd * 1.4f, cr + M(3f) + bb * 2f);
        Can("machine", 2, new NV3(0, 0, vs), rd + bb + M(1f), plate);
        Bell(new NV3(0, 0, vs + plate / 2), cr + M(3f), (cr + M(3f)) * 1.7f, reach: reach);
        for (int k = 0; k < n; k++) Bell(Round(a0 + k * MathF.Tau / n) * rd + new NV3(0, 0, vs + plate / 2), bb, bb * 1.8f, reach: reach);
        // Guns the same on all four faces fore and aft (a six-fold frame keeps one in its spire's tip).
        if (n == 6) Turret(new NV3(0, 0, vp), 5);
        else
            foreach (float v in new[] { vp + M(2f), vs - M(2f) })
                for (int face = 0; face < 4; face++) Turret(new NV3(0, 0, v), face);
    }

    /// <summary>One of the identical modules at p, standing out along o, its status light on its outer face; turned n ways round.</summary>
    void Unit(NV3 p, NV3 o, int kind, float ms, float reach, int n)
    {
        Prim m = kind switch
        {
            0 => Plate("hull", p + o * (ms - 0.5f), NV3.UnitZ, new NV3(-o.Y, o.X, 0f), new NV3(reach, ms, ms)),
            1 => Sphere("hull", p + o * (ms - 0.5f), new NV3(ms)),
            _ => Bar("hull", p + o * (ms - 0.5f) - new NV3(0, 0, reach * 0.6f), p + o * (ms - 0.5f) + new NV3(0, 0, reach * 0.6f), ms * 0.75f),
        };
        Around(m, n);
        Around(Sphere("glow", p + o * (ms - 0.5f + (kind == 2 ? ms * 0.75f : ms) - 0.2f), new NV3(1.1f)), n);
    }

    /// <summary>The tube: bays of rings of struts with spokes in to the core, longerons and braces between them, a module on every ring's corners (or every other ring's), its profile even, a lens or tapering forward. The drives' ring radius.</summary>
    float Tube(int n, int kind, float ms, float cr, float vp, float vs)
    {
        int bays = R.Between(4, 7), profile = R.Range(0, 3);
        float pitch = (vs - M(3f) - vp) / bays, rr = M(12f, 21f), a0 = MathF.PI / n, mr = cr + M(1f, 3f), ml = pitch * R.Float(0.3f, 0.5f);
        bool alternate = R.Chance(0.35f), hoops = R.Chance(0.3f);
        float Radius(int i) => profile switch { 1 => rr * (0.7f + 0.4f * MathF.Sin(MathF.PI * i / bays)), 2 => rr * (0.65f + 0.35f * i / bays), _ => rr };
        NV3 Vertex(int i, float a) => Round(a) * Radius(i) + new NV3(0, 0, vp + i * pitch);
        for (int i = 0; i <= bays; i++)
        {
            NV3 p = Vertex(i, a0), q = Vertex(i, a0 + MathF.Tau / n), o = Round(a0);
            // One sector of the ring (its side, its spoke, its module and light), turned n ways round.
            Around(Bar("machine", p, q, 1.3f * K), n);
            Around(Bar("machine", o * cr + new NV3(0, 0, p.Z), p, 1.1f * K), n);
            if (hoops) Add(new Hoop { C = new NV3(0, 0, p.Z), R = Radius(i), T = MathF.Max(Thin, 1.1f * K) }, "machine");
            if (!alternate || i % 2 == 0) Unit(p, o, kind, ms, MathF.Max(1f, MathF.Min(ms * 1.4f, pitch * 0.3f)), n);
            if (i == bays) break;
            // Longeron and brace to the next ring, and the bay's machinery on the core.
            Around(Bar("machine", p, Vertex(i + 1, a0), 1.3f * K), n);
            Around(Bar("machine", p, Vertex(i + 1, a0 + MathF.Tau / n), 0.75f * K), n);
            Can("machine", 2, new NV3(0, 0, p.Z + pitch / 2), mr, ml);
        }
        return Radius(bays) * 0.5f;
    }

    /// <summary>The vanes: flat lattice fins standing out from the core the ship's length (a star end on), struts across them at every station and a brace in every bay, a module on every fin's edge at each station, rings tying the fins' edges fore and aft. The drives' ring radius.</summary>
    float Vanes(int n, int kind, float ms, float cr, float vp, float vs)
    {
        int stations = R.Between(5, 9);
        float rv = M(11f, 19f), a0 = R.Chance(0.5f) ? 0f : MathF.PI / n, v0 = vp * 0.8f, v1 = vs - M(3f), pitch = (v1 - v0) / (stations - 1), swell = R.Float(-0.25f, 0.3f);
        NV3 o = Round(a0);
        float Reach(int i) => rv * (1f + swell * MathF.Sin(MathF.PI * i / (stations - 1)));
        NV3 Edge(int i) => o * Reach(i) + new NV3(0, 0, v0 + i * pitch);
        for (int i = 0; i < stations; i++)
        {
            NV3 p = Edge(i);
            Around(Bar("machine", o * cr + new NV3(0, 0, p.Z), p, 1.1f * K), n);
            Unit(p, o, kind, ms, MathF.Max(1f, MathF.Min(ms * 1.4f, pitch * 0.3f)), n);
            if (i + 1 == stations) break;
            Around(Bar("machine", p, Edge(i + 1), 1.3f * K), n);
            Around(Bar("machine", o * cr + new NV3(0, 0, p.Z), Edge(i + 1), 0.75f * K), n);
        }
        foreach (int i in new[] { 0, stations - 1 })
            Add(new Hoop { C = new NV3(0, 0, v0 + i * pitch), R = Reach(i), T = MathF.Max(Thin, 1.1f * K) }, "machine");
        return MathF.Min(Reach(stations - 1), rv) * 0.5f;
    }

    /// <summary>The wheels: two to four great wheels on a long spindle, each a ring of struts on spokes with modules all round its rim, an axle cage of longerons between them. The drives' ring radius.</summary>
    float Wheels(int n, int kind, float ms, float cr, float vp, float vs)
    {
        int wheels = R.Pick(new[] { 2, 3, 3, 4 }), per = R.Between(1, 3);
        float rw = M(18f, 30f), a0 = MathF.PI / n, v0 = vp + M(4f), v1 = vs - M(6f), shrink = R.Float(0.6f, 1f), cage = rw * R.Float(0.25f, 0.4f);
        float Rim(int w) => rw * (wheels == 1 ? 1f : 1f - (1f - shrink) * w / (wheels - 1f));
        float At(int w) => wheels == 1 ? (v0 + v1) / 2 : v0 + (v1 - v0) * w / (wheels - 1f);
        for (int w = 0; w < wheels; w++)
        {
            float v = At(w), r = Rim(w);
            NV3 p = Round(a0) * r + new NV3(0, 0, v), q = Round(a0 + MathF.Tau / n) * r + new NV3(0, 0, v);
            Around(Bar("machine", p, q, 1.3f * K), n);
            Around(Bar("machine", Round(a0) * cr + new NV3(0, 0, v), p, 1.2f * K), n);
            // Modules along each side of the rim, the same in every sector.
            for (int k = 0; k < per; k++)
            {
                NV3 at = NV3.Lerp(p, q, (k + 0.5f) / per), out_ = NV3.Normalize(at with { Z = 0 });
                Unit(at, out_, kind, ms, MathF.Max(1f, ms * 1.4f), n);
            }
            if (w + 1 == wheels) break;
            // The axle cage to the next wheel: longerons on the spokes, a brace in each bay.
            NV3 a = Round(a0) * cage + new NV3(0, 0, v), b = Round(a0) * cage + new NV3(0, 0, At(w + 1));
            Around(Bar("machine", a, b, 1.1f * K), n);
            Around(Bar("machine", a, Round(a0 + MathF.Tau / n) * cage + new NV3(0, 0, At(w + 1)), 0.75f * K), n);
            Can("machine", 2, new NV3(0, 0, (v + At(w + 1)) / 2), cr + M(1f, 3f), (At(w + 1) - v) * R.Float(0.25f, 0.45f));
        }
        return MathF.Max(cage, Rim(wheels - 1) * 0.4f);
    }
}
