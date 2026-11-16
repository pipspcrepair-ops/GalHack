#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using NV3 = System.Numerics.Vector3;

namespace PoiGenerator.Gen;

/// <summary>
/// A big ship's outer form, made before any room exists (docs/ship-forms.md). Each faction's body plan
/// (ShipForm.Plans.cs) builds it round the thrust axis out of shared parts: slabs and spines, open trusses, cans, tanks,
/// containers, drive bells, radiator panels, masts and dishes, rings, segments, blades and spikes. Mass lies above,
/// below and beside the axis, the drives at the stern; the bridge sits in the nose, in a blister or deep inside, never
/// on a tower. Rolls of one plan differ in length, module count, layout and which traits appear.
///
/// The vessel builder fits the form to the 1 m grid in the player's own block shapes (slopes and corners where a face
/// leans, cylinder blocks for the pipes) and puts the player's own components on its mounts: thrusters packed in the
/// bells' throats, turrets on its faces.
///
/// Frame: u across (+u starboard), y up, v along the thrust axis from the prow aft; the axis at u = y = 0 while it is
/// made, then settled so that nothing lies below y 0 or ahead of v 0. A part made later wins over what is under it: a
/// cut takes out what was made before it, never what comes after (the flesh bulging out of a Husk ship's breaches).
/// </summary>
public sealed partial class ShipForm
{
    // ---- the solids -----------------------------------------------------------------------------------

    /// <summary>One solid of the form: its paint, whether a crew can live in it (rooms are fitted there), and whether it cuts away.</summary>
    public abstract class Prim
    {
        public string Paint = "hull";
        public bool Hab, Cut;
        /// <summary>The box it lies in.</summary>
        public NV3 Lo, Hi;

        public abstract bool Contains(NV3 p);
        public abstract void Bound();
        public abstract void Move(NV3 d);
        /// <summary>A copy turned about the thrust axis by <paramref name="a"/> radians.</summary>
        public abstract Prim Turned(float a);

        protected Prim Like(Prim p)
        {
            p.Paint = Paint;
            p.Hab = Hab;
            p.Cut = Cut;
            p.Bound();
            return p;
        }
    }

    static NV3 Turn(NV3 p, float a)
    {
        float c = MathF.Cos(a), s = MathF.Sin(a);
        return new(p.X * c - p.Y * s, p.X * s + p.Y * c, p.Z);
    }

    /// <summary>An odd number of quarter turns: what lay across now stands up.</summary>
    static bool Quarter(float a) => MathF.Abs(MathF.Sin(a)) > 0.99f;

    /// <summary>
    /// A section lofted along an axis (0 u, 1 y, 2 v) from S0 to S1, its middle (A, B) and half sizes (HA, HB) going from
    /// the first end's to the second's: a rectangle with its corners chamfered (a share of its smaller half size) or an
    /// ellipse. With a wall it is hollow beyond Plug from S0 and open at S1: a drive's bell.
    /// </summary>
    public sealed class Loft : Prim
    {
        public int Axis = 2;
        public float S0, S1, A0, B0, HA0, HB0, A1, B1, HA1, HB1, Chamfer, Wall, Plug;
        public bool Round;

        (float S, float A, float B) Local(NV3 p) => Axis switch { 0 => (p.X, p.Y, p.Z), 1 => (p.Y, p.X, p.Z), _ => (p.Z, p.X, p.Y) };

        NV3 World(float s, float a, float b) => Axis switch { 0 => new(s, a, b), 1 => new(a, s, b), _ => new(a, b, s) };

        public override bool Contains(NV3 p)
        {
            var (s, a, b) = Local(p);
            float t = (s - S0) / (S1 - S0);
            if (t < 0f || t > 1f) return false;
            float da = MathF.Abs(a - (A0 + (A1 - A0) * t)), db = MathF.Abs(b - (B0 + (B1 - B0) * t));
            float ha = HA0 + (HA1 - HA0) * t, hb = HB0 + (HB1 - HB0) * t;
            if (!In(da, db, ha, hb)) return false;
            return Wall <= 0f || MathF.Abs(s - S0) <= Plug || !In(da, db, ha - Wall, hb - Wall);
        }

        bool In(float da, float db, float ha, float hb)
        {
            if (ha <= 0f || hb <= 0f) return false;
            if (Round) return da * da / (ha * ha) + db * db / (hb * hb) <= 1f;
            return da <= ha && db <= hb && da + db <= ha + hb - Chamfer * MathF.Min(ha, hb);
        }

        public override void Bound()
        {
            NV3 a = World(MathF.Min(S0, S1), MathF.Min(A0 - HA0, A1 - HA1), MathF.Min(B0 - HB0, B1 - HB1));
            NV3 b = World(MathF.Max(S0, S1), MathF.Max(A0 + HA0, A1 + HA1), MathF.Max(B0 + HB0, B1 + HB1));
            Lo = NV3.Min(a, b);
            Hi = NV3.Max(a, b);
        }

        public override void Move(NV3 d)
        {
            var (s, a, b) = Local(d);
            S0 += s;
            S1 += s;
            A0 += a;
            A1 += a;
            B0 += b;
            B1 += b;
        }

        public override Prim Turned(float an)
        {
            if (Axis != 2) throw new NotSupportedException("only a loft along the thrust axis turns about it");
            NV3 c0 = Turn(new NV3(A0, B0, 0), an), c1 = Turn(new NV3(A1, B1, 0), an);
            bool q = Quarter(an);
            return Like(new Loft
            {
                S0 = S0, S1 = S1, A0 = c0.X, B0 = c0.Y, A1 = c1.X, B1 = c1.Y, HA0 = q ? HB0 : HA0, HB0 = q ? HA0 : HB0, HA1 = q ? HB1 : HA1, HB1 = q ? HA1 : HB1,
                Round = Round, Chamfer = Chamfer, Wall = Wall, Plug = Plug,
            });
        }
    }

    /// <summary>An ellipsoid: tanks, spheres, pods, segments, flesh.</summary>
    public sealed class Ball : Prim
    {
        public NV3 C, R;

        public override bool Contains(NV3 p) => ((p - C) / R).LengthSquared() <= 1f;

        public override void Bound()
        {
            Lo = C - R;
            Hi = C + R;
        }

        public override void Move(NV3 d) => C += d;

        public override Prim Turned(float a) => Like(new Ball { C = Turn(C, a), R = Quarter(a) ? new NV3(R.Y, R.X, R.Z) : R });
    }

    /// <summary>A round bar from A to B, its radius going from RA to RB: struts, braces, masts, spikes, needles, tendrils.</summary>
    public sealed class Rod : Prim
    {
        public NV3 A, B;
        public float RA, RB;

        public override bool Contains(NV3 p)
        {
            NV3 ab = B - A;
            float t = Math.Clamp(NV3.Dot(p - A, ab) / MathF.Max(1e-4f, ab.LengthSquared()), 0f, 1f), r = RA + (RB - RA) * t;
            return NV3.DistanceSquared(p, A + ab * t) <= r * r;
        }

        public override void Bound()
        {
            float r = MathF.Max(RA, RB);
            Lo = NV3.Min(A, B) - new NV3(r);
            Hi = NV3.Max(A, B) + new NV3(r);
        }

        public override void Move(NV3 d)
        {
            A += d;
            B += d;
        }

        public override Prim Turned(float a) => Like(new Rod { A = Turn(A, a), B = Turn(B, a), RA = RA, RB = RB });
    }

    /// <summary>A box at any turn: half sizes H along its axes E1, E2, E3, its E2 and E3 halves shrinking toward its +E1 end to Tip of their size (a blade's taper).</summary>
    public sealed class Slab : Prim
    {
        public NV3 C, E1, E2, E3, H;
        public float Tip = 1f;

        public override bool Contains(NV3 p)
        {
            NV3 d = p - C;
            float x = NV3.Dot(d, E1);
            if (MathF.Abs(x) > H.X) return false;
            float k = 1f + (Tip - 1f) * (x / H.X + 1f) * 0.5f;
            return MathF.Abs(NV3.Dot(d, E2)) <= H.Y * k && MathF.Abs(NV3.Dot(d, E3)) <= H.Z * k;
        }

        public override void Bound()
        {
            NV3 r = NV3.Abs(E1) * H.X + NV3.Abs(E2) * H.Y + NV3.Abs(E3) * H.Z;
            Lo = C - r;
            Hi = C + r;
        }

        public override void Move(NV3 d) => C += d;

        public override Prim Turned(float a) => Like(new Slab { C = Turn(C, a), E1 = Turn(E1, a), E2 = Turn(E2, a), E3 = Turn(E3, a), H = H, Tip = Tip });
    }

    /// <summary>A ring round the thrust axis through C: its radius R, its tube's T.</summary>
    public sealed class Hoop : Prim
    {
        public NV3 C;
        public float R, T;

        public override bool Contains(NV3 p)
        {
            float du = p.X - C.X, dy = p.Y - C.Y, dv = p.Z - C.Z, rho = MathF.Sqrt(du * du + dy * dy) - R;
            return rho * rho + dv * dv <= T * T;
        }

        public override void Bound()
        {
            Lo = C - new NV3(R + T, R + T, T);
            Hi = C + new NV3(R + T, R + T, T);
        }

        public override void Move(NV3 d) => C += d;

        public override Prim Turned(float a) => Like(new Hoop { C = Turn(C, a), R = R, T = T });
    }

    /// <summary>A dish: a bowl Depth deep and R across its rim at C, its hollow facing N.</summary>
    public sealed class Dish : Prim
    {
        public NV3 C, N;
        public float R, Depth;

        public override bool Contains(NV3 p)
        {
            NV3 d = p - C;
            float s = NV3.Dot(d, N), rho2 = (d - s * N).LengthSquared();
            return rho2 <= R * R && MathF.Abs(s + Depth * (1f - rho2 / (R * R))) <= 0.8f;
        }

        public override void Bound()
        {
            Lo = C - new NV3(R + Depth + 1f);
            Hi = C + new NV3(R + Depth + 1f);
        }

        public override void Move(NV3 d) => C += d;

        public override Prim Turned(float a) => Like(new Dish { C = Turn(C, a), N = Turn(N, a), R = R, Depth = Depth });
    }

    // ---- the form ---------------------------------------------------------------------------------------

    public readonly List<Prim> Prims = new();

    /// <summary>The drives: each bell's throat face (thrusters packed into it, exhausts aft) and its open radius.</summary>
    public readonly List<(NV3 Face, float Radius)> Drives = new();

    /// <summary>Turret mounts: a point on the solid and the face a turret looks out of (0 +u, 1 -u, 2 +y, 3 -y, 4 +v, 5 -v).</summary>
    public readonly List<(NV3 At, int Face)> Turrets = new();

    /// <summary>Pipes and struts along an axis (0 u, 1 y, 2 v): a run of cylinder blocks from a cell.</summary>
    public readonly List<(Int3 From, int Axis, int Length, string Paint)> Pipes = new();

    /// <summary>Once settled: how far it reaches across (|u|), how high and how long it is (cells).</summary>
    public int ExtentU, Height, Length;

    List<int>[] _byV = Array.Empty<List<int>>();
    int _v0, _indexed = -1;

    void Index()
    {
        if (_indexed == Prims.Count) return;
        _v0 = (int)MathF.Floor(Prims.Min(p => p.Lo.Z));
        int n = (int)MathF.Floor(Prims.Max(p => p.Hi.Z)) - _v0 + 1;
        var by = new List<int>[n];
        for (int i = 0; i < n; i++) by[i] = new List<int>();
        for (int i = 0; i < Prims.Count; i++)
            for (int k = (int)MathF.Floor(Prims[i].Lo.Z) - _v0; k <= (int)MathF.Floor(Prims[i].Hi.Z) - _v0; k++) by[k].Add(i);
        _byV = by;
        _indexed = Prims.Count;
    }

    /// <summary>The part that decides a point: the last made that holds it (a cut there leaves nothing).</summary>
    public Prim? At(NV3 p)
    {
        Index();
        int k = (int)MathF.Floor(p.Z) - _v0;
        if (k < 0 || k >= _byV.Length) return null;
        List<int> list = _byV[k];
        for (int i = list.Count - 1; i >= 0; i--)
        {
            Prim q = Prims[list[i]];
            if (p.X < q.Lo.X || p.X > q.Hi.X || p.Y < q.Lo.Y || p.Y > q.Hi.Y || p.Z < q.Lo.Z || p.Z > q.Hi.Z) continue;
            if (q.Contains(p)) return q;
        }
        return null;
    }

    public bool Solid(NV3 p) => At(p) is { Cut: false };

    /// <summary>Can a crew live here (a part rooms are fitted into)?</summary>
    public bool Hab(NV3 p) => At(p) is { Cut: false, Hab: true };

    /// <summary>The paint of a cell: its middle's part, or the first of its corners' (a cell the surface only passes through).</summary>
    public string? PaintOf(int u, int y, int v)
    {
        if (At(new NV3(u, y, v)) is { Cut: false } mid) return mid.Paint;
        for (int c = 0; c < 8; c++)
            if (At(new NV3(u - 0.5f + (c & 1), y - 0.5f + (c >> 1 & 1), v - 0.5f + (c >> 2 & 1))) is { Cut: false } q) return q.Paint;
        return null;
    }

    /// <summary>How far the form reaches across at v (m), by the boxes of the parts there.</summary>
    public float Reach(float v)
    {
        float r = 0f;
        foreach (Prim p in Prims)
            if (!p.Cut && v >= p.Lo.Z && v <= p.Hi.Z) r = MathF.Max(r, MathF.Max(MathF.Abs(p.Lo.X), MathF.Abs(p.Hi.X)));
        return r;
    }

    /// <summary>Sets it in the vessel's frame: its lowest point a metre over y 0, its prow a metre behind v 0.</summary>
    void Settle()
    {
        var solid = Prims.Where(p => !p.Cut).ToList();
        float lowY = solid.Min(p => p.Lo.Y), lowV = solid.Min(p => p.Lo.Z);
        var d = new NV3(0f, MathF.Ceiling(-lowY) + 1f, MathF.Ceiling(-lowV) + 1f);
        foreach (Prim p in Prims)
        {
            p.Move(d);
            p.Bound();
        }
        for (int i = 0; i < Drives.Count; i++) Drives[i] = (Drives[i].Face + d, Drives[i].Radius);
        for (int i = 0; i < Turrets.Count; i++) Turrets[i] = (Turrets[i].At + d, Turrets[i].Face);
        var cell = new Int3(0, (int)d.Y, (int)d.Z);
        for (int i = 0; i < Pipes.Count; i++) Pipes[i] = (Pipes[i].From + cell, Pipes[i].Axis, Pipes[i].Length, Pipes[i].Paint);
        float wide = solid.Max(p => MathF.Max(MathF.Abs(p.Lo.X), MathF.Abs(p.Hi.X)));
        foreach (var (from, axis, n, _) in Pipes) wide = MathF.Max(wide, MathF.Abs(from.X) + (axis == 0 ? n : 0));
        ExtentU = (int)MathF.Ceiling(wide) + 1;
        Height = (int)MathF.Ceiling(MathF.Max(solid.Max(p => p.Hi.Y), Pipes.Count == 0 ? 0 : Pipes.Max(q => q.From.Y + (q.Axis == 1 ? q.Length : 0)))) + 2;
        Length = (int)MathF.Ceiling(MathF.Max(solid.Max(p => p.Hi.Z), Pipes.Count == 0 ? 0 : Pipes.Max(q => q.From.Z + (q.Axis == 2 ? q.Length : 0)))) + 2;
        _indexed = -1;
        Index();
    }

    // ---- the parts every plan is built from --------------------------------------------------------------

    readonly Rng R;
    readonly string Role;
    /// <summary>Its length, prow to the drives' mouths (m).</summary>
    readonly float L;
    /// <summary>
    /// Its scale against the 120 m class the plans' measures are set at: every measure in metres is taken at it, never under
    /// the grid's metre, so the same plans make a faction's fighters and gunships as well as its capital ships.
    /// </summary>
    readonly float K;

    ShipForm(Rng r, string role, float length, float scale)
    {
        R = r;
        Role = role;
        L = length;
        K = scale;
    }

    /// <summary>A measure of a metres (or between a and b) at the ship's scale, never under a metre.</summary>
    float M(float a, float b = float.NaN) => MathF.Max(1f, (float.IsNaN(b) ? a : R.Float(a, b)) * K);

    /// <summary>A whole number of metres between a and b at the ship's scale, never under <paramref name="least"/> (struts sit on cells).</summary>
    int Mi(int a, int b, int least = 1) => Math.Max(least, (int)MathF.Round(R.Between(a, b) * K));

    /// <summary>A drive's housing ahead of its throat: never shorter than the largest thruster that fits it.</summary>
    float Housing(float r) => MathF.Max(MathF.Min(7f, M(7f)), r * 1.4f);

    T Add<T>(T p, string paint, bool hab = false) where T : Prim
    {
        p.Paint = paint;
        p.Hab = hab;
        p.Bound();
        Prims.Add(p);
        return p;
    }

    /// <summary>Takes away everything made before it inside the part.</summary>
    void Cut(Prim p)
    {
        p.Cut = true;
        p.Bound();
        Prims.Add(p);
    }

    /// <summary>A part and its copies turned about the thrust axis n ways round (n 1: the part alone).</summary>
    void Around(Prim p, int n)
    {
        for (int k = 1; k < n; k++) Prims.Add(p.Turned(k * MathF.Tau / n));
    }

    /// <summary>A section lofted along the thrust axis from v0 to v1: its middle (u, y) and half sizes (u, y) at each end.</summary>
    Loft Taper(string paint, float v0, float v1, (float U, float Y) c0, (float U, float Y) h0, (float U, float Y) c1, (float U, float Y) h1, bool round = false, float chamfer = 0f, bool hab = false) =>
        Add(new Loft { S0 = v0, S1 = v1, A0 = c0.U, B0 = c0.Y, HA0 = h0.U, HB0 = h0.Y, A1 = c1.U, B1 = c1.Y, HA1 = h1.U, HB1 = h1.Y, Round = round, Chamfer = chamfer }, paint, hab);

    /// <summary>A box along the thrust axis: its middle and half sizes.</summary>
    Loft Box(string paint, NV3 c, NV3 h, float chamfer = 0f, bool hab = false) =>
        Taper(paint, c.Z - h.Z, c.Z + h.Z, (c.X, c.Y), (h.X, h.Y), (c.X, c.Y), (h.X, h.Y), false, chamfer, hab);

    /// <summary>A can along an axis (0 u, 1 y, 2 v): its middle, radius (r1 at its far end) and length, with domed ends if asked.</summary>
    Loft Can(string paint, int axis, NV3 c, float r, float len, float r1 = -1f, bool domes = false, bool hab = false)
    {
        if (r1 < 0f) r1 = r;
        var (s, a, b) = axis switch { 0 => (c.X, c.Y, c.Z), 1 => (c.Y, c.X, c.Z), _ => (c.Z, c.X, c.Y) };
        var can = Add(new Loft { Axis = axis, S0 = s - len / 2, S1 = s + len / 2, A0 = a, B0 = b, A1 = a, B1 = b, HA0 = r, HB0 = r, HA1 = r1, HB1 = r1, Round = true }, paint, hab);
        if (domes)
        {
            NV3 e = axis switch { 0 => NV3.UnitX, 1 => NV3.UnitY, _ => NV3.UnitZ };
            NV3 R0 = new NV3(r) - e * (r - r * 0.5f), R1 = new NV3(r1) - e * (r1 - r1 * 0.5f);
            Add(new Ball { C = c - e * (len / 2), R = R0 }, paint, hab);
            Add(new Ball { C = c + e * (len / 2), R = R1 }, paint, hab);
        }
        return can;
    }

    Ball Sphere(string paint, NV3 c, NV3 r, bool hab = false) => Add(new Ball { C = c, R = r }, paint, hab);

    /// <summary>A bar from a to b, its radius from ra to rb: never under a metre and a little, or a slanting one breaks into floating bits on the 1 m grid.</summary>
    Rod Bar(string paint, NV3 a, NV3 b, float ra, float rb = -1f) => Add(new Rod { A = a, B = b, RA = MathF.Max(ra, Thin), RB = MathF.Max(rb < 0f ? ra : rb, Thin) }, paint);

    /// <summary>The least radius of a bar and half thickness of a slanting plate the 1 m grid holds whole.</summary>
    const float Thin = 1.05f;

    /// <summary>A box at any turn: along e1 (its taper's way), e2 across it, the third square to both.</summary>
    Slab Plate(string paint, NV3 c, NV3 e1, NV3 e2, NV3 h, float tip = 1f)
    {
        e1 = NV3.Normalize(e1);
        e2 -= e1 * NV3.Dot(e2, e1);
        // (Across it any way square to it, where the way asked lies along it.)
        if (e2.LengthSquared() < 1e-4f) e2 = NV3.Cross(e1, MathF.Abs(e1.Y) < 0.9f ? NV3.UnitY : NV3.UnitX);
        e2 = NV3.Normalize(e2);
        NV3 e3 = NV3.Cross(e1, e2);
        // (Slanting, never thinner than the grid holds whole.)
        static bool Square(NV3 e) => MathF.Max(MathF.Abs(e.X), MathF.Max(MathF.Abs(e.Y), MathF.Abs(e.Z))) > 0.99f;
        if (!Square(e1) || !Square(e2)) h = NV3.Max(h, new NV3(Thin));
        return Add(new Slab { C = c, E1 = e1, E2 = e2, E3 = e3, H = h, Tip = tip }, paint);
    }

    /// <summary>A pipe or a strut from a to b: a run of cylinder blocks where it lies along an axis, else a bar a block thick.</summary>
    void Pipe(NV3 a, NV3 b, string paint = "machine")
    {
        NV3 d = b - a;
        int axis = MathF.Abs(d.X) > 0.5f ? 0 : MathF.Abs(d.Y) > 0.5f ? 1 : 2;
        float off = (axis == 0 ? 0 : MathF.Abs(d.X)) + (axis == 1 ? 0 : MathF.Abs(d.Y)) + (axis == 2 ? 0 : MathF.Abs(d.Z));
        if (off > 0.5f)
        {
            Bar(paint, a, b, 0.8f);
            return;
        }
        NV3 lo = NV3.Min(a, b);
        int n = (int)MathF.Round(axis == 0 ? MathF.Abs(d.X) : axis == 1 ? MathF.Abs(d.Y) : MathF.Abs(d.Z)) + 1;
        Pipes.Add((new Int3((int)MathF.Round(lo.X), (int)MathF.Round(lo.Y), (int)MathF.Round(lo.Z)), axis, n, paint));
    }

    /// <summary>
    /// A drive: its housing ahead of the throat (never shorter than the largest thruster), the bell flaring aft of it,
    /// hollow and open at its mouth, and the throat's face where the thrusters are packed, exhausts aft.
    /// </summary>
    void Bell(NV3 throat, float r, float len, bool housing = true)
    {
        if (housing) Can("machine", 2, throat - new NV3(0, 0, Housing(r) / 2 - 1f), r + 1f, Housing(r));
        Add(new Loft { S0 = throat.Z, S1 = throat.Z + len, A0 = throat.X, B0 = throat.Y, A1 = throat.X, B1 = throat.Y, HA0 = r, HB0 = r, HA1 = r * 1.4f, HB1 = r * 1.4f, Round = true, Wall = 1.3f, Plug = 1.5f }, "dark");
        Drives.Add((throat + new NV3(0, 0, 1f), r - 1.3f));
    }

    /// <summary>An open square truss along the thrust axis from v0 to v1: four chords at ±half about (cu, cy), a square frame every bay, a brace across each face of each bay, crossing the other way bay by bay.</summary>
    void Truss(float v0, float v1, int half, int bay, float cu = 0f, float cy = 0f)
    {
        v0 = MathF.Round(v0);
        v1 = MathF.Round(v1);
        foreach (int su in new[] { -1, 1 })
            foreach (int sy in new[] { -1, 1 })
                Pipe(new NV3(cu + su * half, cy + sy * half, v0), new NV3(cu + su * half, cy + sy * half, v1));
        int k = 0;
        for (float v = v0; v <= v1; v += bay, k++)
        {
            foreach (int s in new[] { -1, 1 })
            {
                Pipe(new NV3(cu - half + 1, cy + s * half, v), new NV3(cu + half - 1, cy + s * half, v));
                Pipe(new NV3(cu + s * half, cy - half + 1, v), new NV3(cu + s * half, cy + half - 1, v));
            }
            if (v + bay > v1) break;
            float flip = k % 2 == 0 ? 1 : -1;
            foreach (int s in new[] { -1, 1 })
            {
                Bar("machine", new NV3(cu - flip * half, cy + s * half, v), new NV3(cu + flip * half, cy + s * half, v + bay), 0.75f);
                Bar("machine", new NV3(cu + s * half, cy - flip * half, v), new NV3(cu + s * half, cy + flip * half, v + bay), 0.75f);
            }
        }
    }

    /// <summary>Radiator panels on a boom out of the form: parallel fins a metre and a half thick, 3 m apart, stacked across the boom.</summary>
    void Radiators(NV3 root, NV3 outward, int panels, float span, float chord)
    {
        NV3 stack = MathF.Abs(outward.Y) > 0.5f ? NV3.UnitX : NV3.UnitY;
        float reach = MathF.Round((panels - 1) / 2f * 3f) + 1f;
        Pipe(root, root + outward * (span + 2f));
        foreach (float at in new[] { 2f, MathF.Round(span / 2f) + 2f, span + 2f })
            Pipe(root + outward * at - stack * reach, root + outward * at + stack * reach);
        for (int i = 0; i < panels; i++)
        {
            NV3 c = root + outward * (2f + span / 2f) + stack * MathF.Round((i - (panels - 1) / 2f) * 3f);
            Plate("radiator", c, outward, NV3.UnitZ, new NV3(span / 2f, chord / 2f, 0.7f));
        }
    }

    /// <summary>A mast standing out of the form's surface along an axis from a point inside it, a dish on its tip facing the way it is told.</summary>
    void Mast(NV3 inside, NV3 up, float len, NV3? dish = null)
    {
        if (Surface(inside, up) is not { } foot) return;
        NV3 root = new(MathF.Round(foot.X), MathF.Round(foot.Y), MathF.Round(foot.Z));
        Pipe(root, root + up * len);
        if (dish is { } n) Add(new Dish { C = root + up * (len + 1f), N = NV3.Normalize(n), R = M(3f, 6f), Depth = M(2f) }, "machine");
    }

    /// <summary>Machinery on a face of a box (0 +u, 1 -u, 2 +y, 3 -y): small housings half sunk into it.</summary>
    void Clutter(NV3 lo, NV3 hi, int face, int n, string paint = "machine")
    {
        for (int i = 0; i < n; i++)
        {
            var h = new NV3(M(1.5f, 3.5f), M(1.5f, 2.5f), M(1.5f, 4.5f));
            float u = R.Float(lo.X + 1, hi.X - 1), y = R.Float(lo.Y + 1, hi.Y - 1), v = R.Float(lo.Z + 2, hi.Z - 2);
            NV3 c = face switch
            {
                0 => new(hi.X + h.Y - 1f, y, v),
                1 => new(lo.X - h.Y + 1f, y, v),
                2 => new(u, hi.Y + h.Y - 1f, v),
                _ => new(u, lo.Y - h.Y + 1f, v),
            };
            if (face < 2) h = new NV3(h.Y, h.X, h.Z);
            Box(paint, c, h);
        }
    }

    /// <summary>The way a face looks out (0 +u, 1 -u, 2 +y, 3 -y, 4 +v, 5 -v).</summary>
    static NV3 Out(int face) => face switch { 0 => NV3.UnitX, 1 => -NV3.UnitX, 2 => NV3.UnitY, 3 => -NV3.UnitY, 4 => NV3.UnitZ, _ => -NV3.UnitZ };

    /// <summary>The outermost point of the solid along a ray (null: it meets nothing).</summary>
    NV3? Surface(NV3 from, NV3 dir, float reach = 90f)
    {
        float last = -1f;
        for (float t = 0f; t <= reach; t += 0.5f)
            if (Solid(from + dir * t)) last = t;
        return last < 0f ? null : from + dir * last;
    }

    /// <summary>A turret where a ray from inside going out of a face leaves the solid last.</summary>
    void Turret(NV3 from, int face)
    {
        if (Surface(from, Out(face)) is { } at) Turrets.Add((at, face));
    }

    /// <summary>Turrets on the four sides round the axis at points down a stretch of it.</summary>
    void Guns(int n, float v0, float v1)
    {
        for (int i = 0; i < n; i++) Turret(new NV3(0, 0, R.Float(v0, v1)), R.Range(0, 4));
    }

    /// <summary>A way out from the axis round it (radians from +u toward +y).</summary>
    static NV3 Round(float a) => new(MathF.Cos(a), MathF.Sin(a), 0f);
}
