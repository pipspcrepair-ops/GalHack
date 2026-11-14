#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// Laid ground off the sketch's axes: a rounded box about a middle, its long way (a unit step), half its length and its
/// width, its corners rounded (a disc: both halves alike and rounded right round; a strip: its ends rounded). Sketch metres.
/// </summary>
public readonly record struct Shape(float X, float Z, float UX, float UZ, float HalfU, float HalfV, float Round)
{
    public static Shape Disc(float x, float z, float r) => new(x, z, 1f, 0f, r, r, r);

    /// <summary>A strip from one point to another, half its width either side.</summary>
    public static Shape Strip(float ax, float az, float bx, float bz, float half)
    {
        float dx = bx - ax, dz = bz - az, len = MathF.Max(0.01f, MathF.Sqrt(dx * dx + dz * dz));
        return new((ax + bx) / 2f, (az + bz) / 2f, dx / len, dz / len, len / 2f, half, 0f);
    }

    public static Shape Of(SketchBox b) => b.W >= b.D ? new(b.Cx, b.Cz, 1f, 0f, b.W / 2f, b.D / 2f, 0f) : new(b.Cx, b.Cz, 0f, 1f, b.D / 2f, b.W / 2f, 0f);

    bool IsDisc => HalfU == HalfV && Round >= HalfU;

    /// <summary>The axis-aligned box round it.</summary>
    public SketchBox Bounds
    {
        get
        {
            float ex = MathF.Abs(UX) * HalfU + MathF.Abs(UZ) * HalfV, ez = MathF.Abs(UZ) * HalfU + MathF.Abs(UX) * HalfV;
            return new SketchBox(X - ex, Z - ez, X + ex, Z + ez);
        }
    }

    public Shape Turn(int turn)
    {
        var (x, z) = PlaceTypes.Turn(X, Z, turn);
        var (ux, uz) = PlaceTypes.Turn(UX, UZ, turn);
        return this with { X = x, Z = z, UX = ux, UZ = uz };
    }

    /// <summary>Whether a point lies on it, <paramref name="inset"/> in from its edge (a disc as a disc).</summary>
    public bool Holds(float x, float z, float inset = 0f)
    {
        float dx = x - X, dz = z - Z;
        if (IsDisc) return dx * dx + dz * dz <= (HalfU - inset) * (HalfU - inset);
        return MathF.Abs(dx * UX + dz * UZ) <= HalfU - inset && MathF.Abs(-dx * UZ + dz * UX) <= HalfV - inset;
    }

    /// <summary>Whether it comes within <paramref name="clear"/> of another (separating axes; two discs as discs).</summary>
    public bool Hits(Shape o, float clear = 0f)
    {
        float dx = o.X - X, dz = o.Z - Z;
        if (IsDisc && o.IsDisc) return dx * dx + dz * dz < (HalfU + o.HalfU + clear) * (HalfU + o.HalfU + clear);
        Span<(float X, float Z)> axes = stackalloc (float, float)[] { (UX, UZ), (-UZ, UX), (o.UX, o.UZ), (-o.UZ, o.UX) };
        foreach (var (ax, az) in axes)
        {
            float ra = HalfU * MathF.Abs(UX * ax + UZ * az) + HalfV * MathF.Abs(-UZ * ax + UX * az);
            float rb = o.HalfU * MathF.Abs(o.UX * ax + o.UZ * az) + o.HalfV * MathF.Abs(-o.UZ * ax + o.UX * az);
            if (MathF.Abs(dx * ax + dz * az) >= ra + rb + clear) return false;
        }
        return true;
    }
}

/// <summary>
/// The spaceports' plans (a type's "plan" line, data/place-types.txt): rules that lay a high-tech port's parts round its
/// middle as the owner's references do (reference-photos/spaceports, Pearce Design Studio, CC BY-NC 4.0, followed as rules,
/// never as a template), every count, size, angle and spacing rolled by the spawn. Ground (pads, taxiways, roads) may be
/// laid at any angle (<see cref="Shape"/>); what stands is square to the axes, its front the way that faces what it serves.
/// </summary>
public static partial class PlaceTypes
{
    /// <summary>How far a recessed pad's ramp runs for each metre it falls (1 in 5).</summary>
    public const float RampRun = 5f;

    static Sketch RollPlan(PlaceType type, ulong seed, float fit)
    {
        var port = new PortPlan(type, seed, fit);
        switch (type.Plan)
        {
            case "hub": port.Hub(); break;
            case "rosettes": port.Rosettes(); break;
            case "grid": port.Grid(); break;
            default: throw new InvalidOperationException($"{type.Id}: no plan '{type.Plan}'");
        }
        return port.Finish();
    }

    sealed class PortPlan
    {
        readonly PlaceType _t;
        readonly Rng _r;
        readonly float _fit;
        public readonly Sketch S;
        /// <summary>The access road: where it leaves the middle, and the way it runs out.</summary>
        (float X, float Z, float DX, float DZ) _road;

        public PortPlan(PlaceType type, ulong seed, float fit)
        {
            _t = type;
            _fit = fit;
            _r = new Rng(Hash.Combine(seed, Hash.Of(type.Id)));
            S = new Sketch { Type = type, Seed = seed, Fitted = fit };
        }

        TypePart? Part(string id) => _t.Parts.FirstOrDefault(p => p.Id == id);

        /// <summary>A part's sizes rolled for one thing of it (a long part's length shortened toward its low end to fit the world).</summary>
        Dictionary<string, float> Sizes(TypePart p, Rng r)
        {
            var d = new Dictionary<string, float>();
            foreach (var (k, sp) in p.Size)
                d[k] = k == "len" ? sp.Lo + (MathF.Min(sp.Hi, sp.Lo * 4f) - sp.Lo) * _fit * r.Float() : sp.Roll(r);
            if (d.TryGetValue("sink", out float sink)) d["sink"] = MathF.Max(2f, MathF.Round(sink / 2f) * 2f);
            return d;
        }

        int Count(TypePart p, Rng r) => Math.Max(0, (int)MathF.Round(p.Get("count", 1f).Roll(r)));

        /// <summary>Whether a shape is clear of everything placed: laid ground may run into laid ground, nothing else may come within <paramref name="clear"/>.</summary>
        bool Free(Shape sh, bool ground, float clear) =>
            S.Items.All(i => ground && Laid(i) ? !i.Outline.Hits(sh, -0.5f) || i.Part.Kind is "surface" or "road" : !i.Outline.Hits(sh, clear));

        SketchItem Add(TypePart p, SketchBox box, float h, int fx, int fz, Dictionary<string, float> sizes, Shape? lay = null)
        {
            var item = new SketchItem(p, box, h, fx, fz, sizes) { Lay = lay };
            S.Items.Add(item);
            return item;
        }

        /// <summary>A strip of laid ground between two points; one that must keep clear of what stands and of the pads (not a link) is left out where it can't.</summary>
        SketchItem? Strip(TypePart? p, float ax, float az, float bx, float bz, float? width = null, bool clear = false)
        {
            if (p == null) return null;
            var sizes = Sizes(p, _r.Fork(p.Id + S.Items.Count));
            float w = width ?? sizes.GetValueOrDefault("w", 12f);
            sizes["w"] = w;
            Shape sh = Shape.Strip(ax, az, bx, bz, w / 2f);
            if (clear && !Free(sh, true, 1f)) return null;
            return Add(p, sh.Bounds, 0f, 0, 0, sizes, sh);
        }

        /// <summary>The road out: from where it leaves the middle, laid long for now so nothing is put across it; cut to length at the end.</summary>
        void Road(float x, float z, float dx, float dz)
        {
            _road = (x, z, dx, dz);
            Strip(Part("access"), x, z, x + dx * 900f, z + dz * 900f);
        }

        static float Even(float v) => MathF.Round(v / 2f) * 2f;

        /// <summary>A box of a size about a middle, on the 2 m lattice.</summary>
        static SketchBox Around(float x, float z, float w, float d)
        {
            float x0 = Even(x - w / 2f), z0 = Even(z - d / 2f);
            return new SketchBox(x0, z0, x0 + Even(w), z0 + Even(d));
        }

        /// <summary>A metre out from the middle of a box's side, the side one of the four ways faces.</summary>
        static (float X, float Z) Edge(SketchBox b, int fx, int fz) =>
            (fx > 0 ? b.X1 + 1f : fx < 0 ? b.X0 - 1f : b.Cx, fz > 0 ? b.Z1 + 1f : fz < 0 ? b.Z0 - 1f : b.Cz);

        /// <summary>The way a front faces, one of the four, nearest a direction.</summary>
        static (int X, int Z) Facing(float dx, float dz) => MathF.Abs(dx) >= MathF.Abs(dz) ? (Math.Sign(dx), 0) : (0, Math.Sign(dz));

        /// <summary>
        /// A pad (round): its middle and radius on the 2 m lattice, its front the way its link comes in; a recessed one
        /// (sink) has its ramp down that way. Null where it would not fit.
        /// </summary>
        SketchItem? Pad(TypePart p, float x, float z, float tx, float tz, Rng r, float? radius = null, float clear = 8f)
        {
            var sizes = Sizes(p, r);
            float rad = Even(radius ?? sizes.GetValueOrDefault("r", 26f));
            sizes["r"] = rad;
            (x, z) = (Even(x), Even(z));
            Shape disc = Shape.Disc(x, z, rad);
            if (!Free(disc, false, clear)) return null;
            float dx = tx - x, dz = tz - z, len = MathF.Max(0.01f, MathF.Sqrt(dx * dx + dz * dz));
            sizes["gx"] = dx / len;
            sizes["gz"] = dz / len;
            var (fx, fz) = Facing(dx, dz);
            return Add(p, new SketchBox(x - rad, z - rad, x + rad, z + rad), sizes.GetValueOrDefault("sink"), fx, fz, sizes, disc);
        }

        /// <summary>A pad's link: from a point on a taxiway to the pad's rim, through its ramp.</summary>
        void Link(SketchItem pad, float fromX, float fromZ)
        {
            float r = pad.Rolled["r"], gx = pad.Rolled["gx"], gz = pad.Rolled["gz"];
            Strip(Part("link"), fromX, fromZ, pad.Lay!.Value.X + gx * (r - 2f), pad.Lay.Value.Z + gz * (r - 2f));
        }

        /// <summary>A pad off a taxiway's centre line at a point, to one side (its link square to the taxiway).</summary>
        bool PadOff(TypePart p, float x, float z, float nx, float nz, float half, Rng r)
        {
            float rad = Even(p.Get("r", 26f).Lo), sink = p.Get("sink", 0f).Hi, reach = half + MathF.Max(sink * RampRun, 10f) + rad;
            if (Pad(p, x + nx * reach, z + nz * reach, x, z, r) is not { } pad) return false;
            Link(pad, x + nx * half * 0.5f, z + nz * half * 0.5f);
            return true;
        }

        /// <summary>A building square to the axes about a middle, facing a way; null where it would not fit.</summary>
        SketchItem? Building(TypePart p, float x, float z, int fx, int fz, Rng r, float clear = 6f)
        {
            var sizes = Sizes(p, r);
            float len = sizes.GetValueOrDefault("len", 2f * sizes.GetValueOrDefault("r", 10f)), depth = sizes.GetValueOrDefault("depth", len);
            if (p.What == "authority") len = depth = 2f * (sizes["r"] + sizes["wing"]);
            if (p.What == "hub") len = depth = 2f * (MathF.Max(sizes["r"], sizes.GetValueOrDefault("cab")) + 2f);
            if (p.What == "dome") len = depth = 2f * sizes["r"];
            // (Its length runs along its front.)
            SketchBox box = fz != 0 ? Around(x, z, len, depth) : Around(x, z, depth, len);
            if (!Free(Shape.Of(box), false, clear)) return null;
            return Add(p, box, sizes.GetValueOrDefault("h", 6f), fx, fz, sizes);
        }

        /// <summary>A thing of a part at the first clear spot out along a way from a point (a step at a time), facing back; null where none.</summary>
        SketchItem? Out(TypePart? p, float x, float z, float dx, float dz, float from, float to, Rng r, bool tanks = false)
        {
            if (p == null) return null;
            for (float d = from; d <= to; d += 8f)
            {
                float px = x + dx * d, pz = z + dz * d;
                var (fx, fz) = Facing(-dx, -dz);
                if ((tanks ? Tanks(p, px, pz, r) : Building(p, px, pz, fx, fz, r)) is { } done) return done;
            }
            return null;
        }

        /// <summary>A tank farm in its bunded yard, its tanks in rows a tank's width apart.</summary>
        SketchItem? Tanks(TypePart p, float x, float z, Rng r)
        {
            var sizes = Sizes(p, r);
            float cell = sizes.GetValueOrDefault("r", 5f) * 4f;
            int count = Math.Max(1, (int)MathF.Round(sizes.GetValueOrDefault("count", 2f))), cols = (int)MathF.Ceiling(MathF.Sqrt(count));
            SketchBox box = Around(x, z, cols * cell + 8f, MathF.Ceiling(count / (float)cols) * cell + 8f);
            if (!Free(Shape.Of(box), false, 6f)) return null;
            return Add(p, box, sizes.GetValueOrDefault("h", 8f), 0, 0, sizes);
        }

        // ---- class D: a hub and its spokes ------------------------------------------------------------------

        /// <summary>
        /// The tower on its roundabout at the middle; three or four taxiways out from it at rolled angles, never closer than
        /// 60 degrees; pads off them either side; the long pads for bigger craft at the end of one, the freight hall and its
        /// siding beside them; the fuel farm off the end of another; the road out on its own way between them.
        /// </summary>
        public void Hub()
        {
            Rng r = _r.Fork("hub");
            TypePart hub = Part("terminal")!, spoke = Part("spoke")!, pad = Part("pad")!;
            // The ways out: the road first, then the taxiways, spread round evenly and jittered; the tower faces the road.
            int n = Math.Max(2, Count(spoke, r.Fork("spokes")));
            double a = r.Float(0f, MathF.Tau), gap = Math.Tau / (n + 1);
            var ways = Enumerable.Range(0, n + 1).Select(k => a + k * gap + r.Float(-0.2f, 0.2f) * gap).ToList();
            var (tfx, tfz) = Facing((float)Math.Cos(ways[0]), (float)Math.Sin(ways[0]));
            SketchItem tower = Building(hub, 0f, 0f, tfx, tfz, r.Fork("tower"), 0f)!;
            // (Clear of the tower's box at its corners, wherever it sits on the lattice.)
            float reach = new[] { tower.Box.X0, tower.Box.Z0, tower.Box.X1, tower.Box.Z1 }.Max(MathF.Abs) * MathF.Sqrt(2f);
            // The roundabout: a ring of short strips round the tower.
            float ringR = reach + 16f;
            if (Part("ring") is { } ring)
            {
                var rs = Sizes(ring, r.Fork("ring"));
                ringR = MathF.Max(rs.GetValueOrDefault("r", ringR), (reach + rs.GetValueOrDefault("w", 12f) / 2f + 3f) / MathF.Cos(MathF.PI / 16f));
                for (int k = 0; k < 16; k++)
                {
                    double a0 = k * Math.PI / 8, a1 = (k + 1) * Math.PI / 8;
                    Strip(ring, ringR * (float)Math.Cos(a0), ringR * (float)Math.Sin(a0), ringR * (float)Math.Cos(a1), ringR * (float)Math.Sin(a1));
                }
            }
            Road((float)Math.Cos(ways[0]) * (ringR + 1f), (float)Math.Sin(ways[0]) * (ringR + 1f), (float)Math.Cos(ways[0]), (float)Math.Sin(ways[0]));
            int pads = Count(pad, r.Fork("pads"));
            var spokes = new List<(float X0, float Z0, float X1, float Z1, float DX, float DZ, float Half)>();
            for (int k = 1; k <= n; k++)
            {
                var sr = r.Fork("spoke" + k);
                float dx = (float)Math.Cos(ways[k]), dz = (float)Math.Sin(ways[k]);
                var sizes = Sizes(spoke, sr);
                float len = sizes.GetValueOrDefault("len", 180f), half = sizes.GetValueOrDefault("w", 15f) / 2f;
                Strip(spoke, dx * ringR, dz * ringR, dx * (ringR + len), dz * (ringR + len));
                spokes.Add((dx * ringR, dz * ringR, dx * (ringR + len), dz * (ringR + len), dx, dz, half));
            }
            // The long pads (and the freight hall with its siding) at the end of the longest taxiway.
            int big = spokes.Select((sp, i) => (sp, i)).MaxBy(q => MathF.Abs(q.sp.X1 - q.sp.X0) + MathF.Abs(q.sp.Z1 - q.sp.Z0)).i;
            if (Part("bigpad") is { } bigpad)
            {
                var (_, _, x1, z1, dx, dz, half) = spokes[big];
                int count = Math.Max(1, Count(bigpad, r.Fork("bigpads")));
                for (int k = 0; k < count; k++)
                {
                    var br = r.Fork("bigpad" + k);
                    var sizes = Sizes(bigpad, br);
                    float len = sizes.GetValueOrDefault("len", 120f), w = sizes.GetValueOrDefault("w", 60f);
                    // Across the taxiway's end, side by side: a stadium, its long way square to the taxiway.
                    float side = count == 1 ? 0f : (k - (count - 1) / 2f) * (len + 16f);
                    float cx = x1 + dx * (w / 2f + 12f) - dz * side, cz = z1 + dz * (w / 2f + 12f) + dx * side;
                    var sh = new Shape(Even(cx), Even(cz), -dz, dx, len / 2f, w / 2f, w / 2f);
                    if (!Free(sh, false, 10f)) continue;
                    Add(bigpad, sh.Bounds, 0f, 0, 0, sizes, sh);
                    Strip(Part("link"), x1, z1, sh.X, sh.Z);
                }
            }
            // Pads either side of every taxiway, shared out by length: one or both sides at each stop.
            float total = spokes.Sum(sp => MathF.Abs(sp.X1 - sp.X0) + MathF.Abs(sp.Z1 - sp.Z0));
            float pitch = 2f * Even(pad.Get("r", 26f).Lo) + r.Float(14f, 24f);
            int placed = 0;
            foreach (var (x0, z0, x1, z1, dx, dz, half) in spokes.OrderBy(_ => r.Float()))
            {
                float len = MathF.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0));
                for (float t = pitch / 2f + 10f; t < len - pitch / 3f && placed < pads; t += pitch)
                    foreach (int sideOf in r.Chance(0.6f) ? new[] { 1, -1 } : new[] { r.Chance(0.5f) ? 1 : -1 })
                        if (placed < pads && PadOff(pad, x0 + dx * t, z0 + dz * t, -dz * sideOf, dx * sideOf, half, r.Fork($"pad{placed}"))) placed++;
            }
            // The freight hall beside the long pads, its siding out the back; the fuel farm off another taxiway's end.
            {
                var (_, _, x1, z1, dx, dz, _) = spokes[big];
                if (Out(Part("freight"), x1, z1, -dz, dx, 90f, 260f, r.Fork("freight")) is { } freight && Part("siding") is { } siding)
                {
                    var sizes = Sizes(siding, r.Fork("siding"));
                    var (bx, bz) = (freight.FaceX, freight.FaceZ);
                    float len = sizes.GetValueOrDefault("len", 100f), ox = freight.Box.Cx - bx * (freight.Box.W / 2f), oz = freight.Box.Cz - bz * (freight.Box.D / 2f);
                    // (Along its back wall: off the back corner, running away from its front.)
                    float sx = bx != 0 ? ox : freight.Box.X1 - 6f, sz = bz != 0 ? oz : freight.Box.Z1 - 6f;
                    var sh = Shape.Strip(sx - bx * 3f, sz - bz * 3f, sx - bx * len, sz - bz * len, 2f);
                    if (Free(sh, true, 1f)) Add(siding, sh.Bounds, 1f, -bx, -bz, sizes, sh);
                }
            }
            int fuelOn = spokes.Count > 1 ? (big + 1) % spokes.Count : big;
            {
                var (_, _, x1, z1, dx, dz, _) = spokes[fuelOn];
                Out(Part("fuel"), x1, z1, dx, dz, 40f, 240f, r.Fork("fuel"), tanks: true);
            }
        }

        // ---- class C: rosettes round the authority building ---------------------------------------------

        /// <summary>
        /// The authority building at the middle, its front to the gate; three rosettes round it, a third of a turn apart and
        /// the gate between two of them, each a large pad ringed by recessed ones on links, joined to the building by a
        /// taxiway; the naval yard behind its own fence in one of the other gaps, a small airfield in the last; the fuel and
        /// the leased sheds by the building; the road out through the gate to the startown.
        /// </summary>
        public void Rosettes()
        {
            Rng r = _r.Fork("rosettes");
            var (gx, gz) = new[] { (0, -1), (1, 0), (0, 1), (-1, 0) }[r.Range(0, 4)];
            SketchItem hq = Building(Part("terminal")!, 0f, 0f, gx, gz, r.Fork("authority"), 0f)!;
            float reach = hq.Box.W / 2f;
            var (rx0, rz0) = Edge(hq.Box, gx, gz);
            Road(rx0, rz0, gx, gz);
            double gate = Math.Atan2(gz, gx);
            TypePart big = Part("bigpad")!, pad = Part("pad")!;
            float padR = Even(pad.Get("r", 26f).Lo), sink = pad.Get("sink", 0f).Hi;
            for (int k = 0; k < 3; k++)
            {
                var rr = r.Fork("rosette" + k);
                double a = gate + Math.PI / 3 + k * Math.Tau / 3 + rr.Float(-0.12f, 0.12f);
                float dx = (float)Math.Cos(a), dz = (float)Math.Sin(a);
                float bigR = Even(big.Get("r", 50f).Roll(rr)), ringR = bigR + MathF.Max(sink * RampRun, 12f) + rr.Float(12f, 24f) + padR;
                // Out far enough to clear the building and the other rosettes: the nearest that is clear of all.
                float d = reach * 1.42f + ringR + padR + rr.Float(16f, 40f);
                for (; d < 1500f; d += 10f)
                    if (Free(Shape.Disc(dx * d, dz * d, ringR + padR + 10f), false, 0f)) break;
                float cx = Even(dx * d), cz = Even(dz * d);
                Pad(big, cx, cz, -dx, -dz, rr, bigR, 0f);
                // Its ring: one stop of it is the taxiway in from the building; a recessed pad and its link at each of the rest.
                int slots = Count(pad, rr) + 1;
                double inward = Math.Atan2(-dz, -dx);
                for (int s = 1; s < slots; s++)
                {
                    double b = inward + s * Math.Tau / slots;
                    float px = cx + ringR * (float)Math.Cos(b), pz = cz + ringR * (float)Math.Sin(b);
                    if (Pad(pad, px, pz, cx, cz, rr.Fork("pad" + s), clear: 4f) is { } ringPad) Link(ringPad, cx + (bigR - 4f) * (float)Math.Cos(b), cz + (bigR - 4f) * (float)Math.Sin(b));
                }
                // The taxiway in: from the building's side that faces it to the large pad's rim.
                // (Its end clear of the building, however it comes away.)
                var (fx, fz) = Facing(dx, dz);
                var (ex, ez) = Edge(hq.Box, fx, fz);
                float half = Part("spoke")!.Get("w", 20f).Hi / 2f;
                Strip(Part("spoke"), ex + fx * half, ez + fz * half, cx - dx * (bigR - 4f), cz - dz * (bigR - 4f));
            }
            // The other two gaps: the naval yard and the airfield, a roll which is which.
            var gaps = new[] { gate + Math.Tau / 3, gate + 2 * Math.Tau / 3 };
            if (r.Chance(0.5f)) (gaps[0], gaps[1]) = (gaps[1], gaps[0]);
            Naval((float)Math.Cos(gaps[0]), (float)Math.Sin(gaps[0]), hq.Box, r.Fork("naval"));
            Airfield((float)Math.Cos(gaps[1]), (float)Math.Sin(gaps[1]), hq.Box, r.Fork("airfield"));
            // By the building: the fuel off its back, the leased sheds either side of the road in.
            // (The fuel out between the road and a rosette, well off the building.)
            double fa = gate + (r.Chance(0.5f) ? 1 : -1) * 0.5;
            Out(Part("fuel"), 0f, 0f, (float)Math.Cos(fa), (float)Math.Sin(fa), reach * 1.42f + 60f, reach + 700f, r.Fork("fuel"), tanks: true);
            if (Part("lease") is { } lease)
            {
                int count = Count(lease, r.Fork("leases"));
                for (int k = 0; k < count; k++)
                {
                    float side = k % 2 == 0 ? 1f : -1f, along = reach + 30f + k / 2 * 40f;
                    Out(lease, gx * along, gz * along, -gz * side, gx * side, 24f, 120f, r.Fork("lease" + k));
                }
            }
        }

        /// <summary>A group of things in a box out along a gap from the middle: the nearest spot where all of it is clear.</summary>
        (float X, float Z)? Spot(float dx, float dz, float from, float w, float d)
        {
            for (float t = from; t < 1500f; t += 10f)
            {
                float x = dx * t, z = dz * t;
                if (Free(Shape.Of(Around(x, z, w, d)), false, 12f)) return (x, z);
            }
            return null;
        }

        /// <summary>
        /// The naval yard: its hall facing its pad across a yard, its barracks beside, a fence round them with its gate
        /// where the road in from the authority building comes through.
        /// </summary>
        void Naval(float dx, float dz, SketchBox hq, Rng r)
        {
            float reach = hq.W / 2f;
            TypePart? hall = Part("naval"), npad = Part("navalpad"), barracks = Part("barracks");
            if (hall == null || npad == null) return;
            var hs = Sizes(hall, r.Fork("hall"));
            float padR = Even(npad.Get("r", 44f).Roll(r));
            // In a row along the way out: the hall nearest, then the pad (the barracks beside the hall).
            // (The box it looks for holds its fence too.)
            float span = hs["depth"] + 24f + 2f * padR, wide = MathF.Max(hs["len"] + 2f * (barracks?.Get("len", 40f).Hi ?? 0f) + 40f, 2f * padR) + 30f;
            bool alongX = MathF.Abs(dx) >= MathF.Abs(dz);
            var (ox, oz) = alongX ? (MathF.Sign(dx), 0) : (0, MathF.Sign(dz));
            if (Spot(dx, dz, reach + 60f, alongX ? span + 58f : wide + 28f, alongX ? wide + 28f : span + 58f) is not { } at) return;
            int before = S.Items.Count;
            float hx = at.X - ox * (span / 2f - hs["depth"] / 2f), hz = at.Z - oz * (span / 2f - hs["depth"] / 2f);
            var built = Building(hall, hx, hz, ox, oz, r.Fork("hall"));
            if (built == null) return;
            Pad(npad, at.X + ox * (span / 2f - padR), at.Z + oz * (span / 2f - padR), hx, hz, r.Fork("pad"), padR, 4f);
            if (barracks != null)
            {
                // (Its front in line with the hall's, on the yard.)
                float side = hs["len"] / 2f + barracks.Get("len", 40f).Hi / 2f + 12f, forward = (hs["depth"] - barracks.Get("depth", 20f).Hi) / 2f;
                Building(barracks, hx + oz * side + ox * forward, hz + ox * side + oz * forward, ox, oz, r.Fork("barracks"));
            }
            // The road in: from the building's side facing it to the hall's flank, then round in front of the hall and the
            // barracks across the yard between them and the pad.
            float px = -oz, pz = -ox, half = Part("spoke")!.Get("w", 20f).Hi / 2f, flank = hs["len"] / 2f + half + 4f, front = hs["depth"] / 2f + half + 2f;
            float ax = hx + px * flank, az = hz + pz * flank;
            var (sx, sz) = Edge(hq, (int)ox, (int)oz);
            Strip(Part("spoke"), sx + ox * half, sz + oz * half, ax - ox * half, az - oz * half, clear: true);
            Strip(Part("spoke"), ax - ox * half, az - oz * half, ax + ox * front, az + oz * front, clear: true);
            float across = flank + hs["len"] / 2f + (barracks?.Get("len", 40f).Hi ?? 0f) / 2f + 12f;
            Strip(Part("spoke"), ax + ox * front, az + oz * front, ax + ox * front - px * across, az + oz * front - pz * across, clear: true);
            // Its own fence round the yard, as far out as is clear of everything else (the road in comes through its gate).
            if (Part("navalfence") is { } fence)
            {
                var yard = S.Items.Skip(before).ToList();
                SketchBox inner = yard.Where(i => !Laid(i) || i.Part.Kind == "pads").Select(i => i.Box).Aggregate((a, b) => a.Union(b));
                foreach (float grow in new[] { 14f, 10f, 6f })
                {
                    SketchBox round = Around(inner.Cx, inner.Cz, inner.W + 2f * grow, inner.D + 2f * grow);
                    if (S.Items.Except(yard).Any(i => i.Part.Kind is not ("surface" or "road") && i.Outline.Hits(Shape.Of(round), 2f))) continue;
                    Add(fence, round, 3f, 0, 0, Sizes(fence, r.Fork("fence")));
                    break;
                }
            }
        }

        /// <summary>A small airfield: a short runway square across the way out, its hangars and tower on the inner side facing it.</summary>
        void Airfield(float dx, float dz, SketchBox hq, Rng r)
        {
            float reach = hq.W / 2f;
            TypePart? runway = Part("runway"), hangar = Part("airhangar"), tower = Part("airtower");
            if (runway == null) return;
            var rs = Sizes(runway, r.Fork("runway"));
            float len = rs["len"], w = rs["w"] + 2f * rs.GetValueOrDefault("shoulder");
            bool alongX = MathF.Abs(dz) >= MathF.Abs(dx);   // (square across the way out)
            float depth = w + 30f + (hangar?.Get("depth", 36f).Hi ?? 0f);
            if (Spot(dx, dz, reach + 80f, alongX ? len + 20f : depth + 20f, alongX ? depth + 20f : len + 20f) is not { } at) return;
            var (ox, oz) = alongX ? (0, MathF.Sign(dz)) : (MathF.Sign(dx), 0);
            // The runway on the outer side, the hangars inside it.
            float rx = at.X + ox * (depth / 2f - w / 2f), rz = at.Z + oz * (depth / 2f - w / 2f);
            Add(runway, alongX ? Around(rx, rz, len, w) : Around(rx, rz, w, len), 0f, 0, 0, rs);
            int count = hangar == null ? 0 : Count(hangar, r.Fork("hangars"));
            float hangarDepth = hangar?.Get("depth", 36f).Hi ?? 0f, step = (hangar?.Get("len", 44f).Hi ?? 0f) + 12f;
            for (int k = 0; k < count; k++)
            {
                float along = (k - (count - 1) / 2f) * step - len / 4f;
                float hx = rx - ox * (w / 2f + 24f + hangarDepth / 2f) + (alongX ? along : 0f), hz = rz - oz * (w / 2f + 24f + hangarDepth / 2f) + (alongX ? 0f : along);
                Building(hangar!, hx, hz, ox, oz, r.Fork("hangar" + k));
            }
            if (tower != null) Building(tower, rx - ox * (w / 2f + 24f) + (alongX ? len / 3f : 0f), rz - oz * (w / 2f + 24f) + (alongX ? 0f : len / 3f), ox, oz, r.Fork("tower"));
            // The way in from the building to the hangars' apron.
            var (sx, sz) = Edge(hq, Facing(dx, dz).X, Facing(dx, dz).Z);
            Strip(Part("spoke"), sx, sz, rx - ox * (w / 2f + 14f), rz - oz * (w / 2f + 14f), clear: true);
        }

        // ---- class A: a grid of pads on taxi spines -----------------------------------------------------

        /// <summary>
        /// Taxi spines side by side along z, recessed pads either side of each at a rolled pitch; a cross taxiway at each end;
        /// a column of big pads off the field's west side; a row of hangars along each long edge facing in; at the east end
        /// the terminal on its plaza with offices round it and the tower beside; the tank farm out in a far corner.
        /// </summary>
        public void Grid()
        {
            Rng r = _r.Fork("grid");
            TypePart spine = Part("spine")!, pad = Part("pad")!;
            float padR = Even(pad.Get("r", 30f).Lo), sink = pad.Get("sink", 0f).Hi, link = MathF.Max(sink * RampRun, 10f) + r.Float(4f, 10f);
            var ss = Sizes(spine, r.Fork("spine"));
            float half = ss["w"] / 2f, pitch = 2f * padR + r.Float(10f, 18f);
            int n = Math.Max(2, Count(spine, r.Fork("spines"))), per = Math.Max(2, Count(pad, r.Fork("per")));
            int stops = (per + 1) / 2;
            float len = MathF.Max(ss["len"], stops * pitch + 2f * padR), across = 2f * (half + link + 2f * padR) + r.Float(10f, 20f);
            float z0 = -len / 2f, z1 = len / 2f;
            float x0 = -(n - 1) * across / 2f;
            for (int k = 0; k < n; k++)
            {
                float x = Even(x0 + k * across);
                Strip(spine, x, z0, x, z1, ss["w"]);
                var pr = r.Fork("spine" + k);
                int placed = 0;
                for (int s = 0; s < stops; s++)
                {
                    float z = z0 + pitch / 2f + s * pitch + (len - stops * pitch) / 2f;
                    foreach (int side in new[] { -1, 1 })
                        if (placed < per && PadOff(pad, x, z, side, 0f, half, pr.Fork($"pad{s}{side}"))) placed++;
                }
            }
            // The cross taxiways, the hangars' aprons: along each end of the spines, from the big pads' column to the east end.
            float west = x0 - across / 2f, east = -x0 + across / 2f, lane = Part("lane")?.Get("w", 30f).Roll(r) ?? ss["w"] * 1.5f;
            if (Part("bigpad") is { } big)
            {
                // A column of big pads off the west side, each on a link to the westmost spine's line.
                int count = Count(big, r.Fork("bigpads"));
                float bigR = Even(big.Get("r", 56f).Hi), bx = west - lane - bigR;
                for (int k = 0; k < count; k++)
                {
                    float z = z0 + (k + 0.5f) * len / count;
                    if (Pad(big, bx, z, west + lane, z, r.Fork("bigpad" + k), null, 6f) is { } bp) Strip(Part("link"), west, z, bp.Lay!.Value.X + bp.Lay.Value.HalfU - 4f, z);
                }
                Strip(spine, west, z0, west, z1, ss["w"]);
                west = bx - bigR - 10f;
            }
            foreach (float z in new[] { z0 - lane / 2f, z1 + lane / 2f }) Strip(Part("lane") ?? spine, west, z, east, z, lane);
            // A row of hangars along each long edge, their fronts on the lane.
            if (Part("hangar") is { } hangar)
            {
                int count = Count(hangar, r.Fork("hangars"));
                foreach (int side in new[] { -1, 1 })
                {
                    float x = west + 10f, face = side < 0 ? z0 - lane : z1 + lane;
                    for (int k = 0; k < (count + (side < 0 ? 1 : 0)) / 2; k++)
                    {
                        var hr = r.Fork($"hangar{side}{k}");
                        var sizes = Sizes(hangar, hr);
                        if (x + sizes["len"] > east) break;
                        float hz = face + side * (sizes["depth"] / 2f + 8f);
                        if (Building(hangar, x + sizes["len"] / 2f, hz, 0, -side, hr) is { } h) x = h.Box.X1 + hr.Float(8f, 20f);
                        else x += 20f;
                    }
                }
            }
            // The east end: the plaza, the terminal facing the field across it, offices round the plaza, the tower beside.
            float plazaW = 0f;
            if (Part("plaza") is { } plaza)
            {
                var ps = Sizes(plaza, r.Fork("plaza"));
                plazaW = ps["depth"];
                Add(plaza, Around(east + 30f + plazaW / 2f, 0f, plazaW, ps["len"]), 0f, 0, 0, ps);
            }
            float tx = east + 30f + plazaW + 10f;
            if (Part("terminal") is { } terminal && Building(terminal, tx + terminal.Get("depth", 40f).Hi / 2f, 0f, -1, 0, r.Fork("terminal")) is { } t)
            {
                Road(t.Box.X1 + 1f, 0f, 1f, 0f);
                if (Part("tower") is { } tower) Out(tower, t.Box.Cx, 0f, 0f, r.Chance(0.5f) ? 1f : -1f, t.Box.D / 2f + 20f, t.Box.D / 2f + 200f, r.Fork("tower"));
                // (The offices either side of the road in, past the terminal.)
                if (Part("office") is { } office)
                {
                    int count = Count(office, r.Fork("offices"));
                    for (int k = 0; k < count; k++) Out(office, t.Box.X1 + 30f + k / 2 * 50f, 0f, 0f, k % 2 == 0 ? 1f : -1f, 24f, 140f, r.Fork("office" + k));
                }
            }
            else Road(east, 0f, 1f, 0f);
            Out(Part("fuel"), west, z0 - lane, -0.6f, -0.8f, 40f, 400f, r.Fork("fuel"), tanks: true);
        }

        // ---- the road, the fence, the startown ---------------------------------------------------------

        /// <summary>
        /// The road out from the middle to the fence round everything, the gate where it comes through, and on past it the
        /// startown huddled either side of it; the world's road meets it at its far end.
        /// </summary>
        public Sketch Finish()
        {
            // (Without the road, laid long till now.)
            SketchItem? road = S.Items.FirstOrDefault(i => i.Part.Kind == "road");
            if (road != null) S.Items.Remove(road);
            SketchBox all = S.Items.Select(i => i.Box).Aggregate((a, b) => a.Union(b));
            TypePart? fence = Part("fence");
            var fs = fence != null ? Sizes(fence, _r.Fork("fence")) : new Dictionary<string, float>();
            SketchBox round = all.Grow(fence?.Find("round")?.Gap?.Roll(_r.Fork("round")) ?? 20f);
            var (rx, rz, dx, dz) = _road;
            // To the fence's line: where the way out leaves its box.
            float tx = dx > 0 ? (round.X1 - rx) / dx : dx < 0 ? (round.X0 - rx) / dx : float.MaxValue;
            float tz = dz > 0 ? (round.Z1 - rz) / dz : dz < 0 ? (round.Z0 - rz) / dz : float.MaxValue;
            float out0 = MathF.Min(tx, tz);
            TypePart? town = Part("town"), access = Part("access");
            float townLen = 0f;
            if (town != null)
            {
                // The startown: huts either side of the road past the gate, close and uneven.
                var tr = _r.Fork("town");
                int count = Count(town, tr);
                float w = access?.Get("w", 8f).Hi ?? 8f;
                for (int k = 0; k < count; k++)
                {
                    var hr = tr.Fork("hut" + k);
                    float side = k % 2 == 0 ? 1f : -1f, along = out0 + 24f + k / 2 * hr.Float(18f, 30f), off = w / 2f + hr.Float(6f, 18f) + town.Get("r", 6f).Hi;
                    var (fx, fz) = Facing(dz * side, -dx * side);
                    if (Building(town, rx + dx * along + dz * side * off, rz + dz * along - dx * side * off, fx, fz, hr) is { } hut) townLen = MathF.Max(townLen, along + hut.Box.Long / 2f - out0);
                }
            }
            float end = out0 + townLen + 6f;
            if (access != null) Strip(access, rx, rz, rx + dx * end, rz + dz * end);
            if (fence != null) Add(fence, Around(round.Cx, round.Cz, round.W, round.D), fs.GetValueOrDefault("h", 3f), 0, 0, fs);
            S.Footprint = S.Items.Select(i => i.Box).Aggregate((a, b) => a.Union(b));
            var (ox, oz) = Facing(dx, dz);
            (S.GateX, S.GateZ, S.OutX, S.OutZ) = (Even(rx + dx * end), Even(rz + dz * end), ox, oz);
            return S;
        }
    }
}
