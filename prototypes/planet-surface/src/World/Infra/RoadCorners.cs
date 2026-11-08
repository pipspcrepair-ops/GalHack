using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Terrain;
using Planets.Gen;

namespace PlanetSurface.Infra;

/// <summary>
/// A junction's rounded corner (design doc "Infrastructure": intersections have curved inner corners): where two paved
/// decks meet at an angle, or a way meets a roundabout's ring or the bulb a street turns in, the land between them is
/// cut back by an arc of a radius for the class, tangent to both kerb lines. The kerb, and everything laid to it (the
/// gutter, the verge, the footpath), turns in one smooth arc from one road's kerb line to the other's: no notch, no
/// step, nothing laid over a lane. The terrain shader draws it from the same numbers (RoadField: a corner entry), and
/// the tests trace it.
/// </summary>
public sealed class RoadCorner
{
    /// <summary>The arc's centre (canonical world metres) and the deck's height there.</summary>
    public double X, Z;
    public float Y;
    /// <summary>The kerb's radius round the corner, m.</summary>
    public float R;
    /// <summary>Unit vectors from the centre to where the arc meets each kerb line (its tangent points).</summary>
    public float AX, AZ, BX, BZ;
    /// <summary>How far along each of those the corner may reach: to the far side of that deck, less a margin.</summary>
    public float LimA, LimB;
    /// <summary>How far from the centre the corner's own deck reaches at most (past the corner point of the two kerb lines).</summary>
    public float Cut;
    /// <summary>The corner of a kerbed street: kerbstones, a verge and a footpath round it.</summary>
    public bool Kerbed;
    /// <summary>The two ways (route ids; a ring's or a bulb's own route for the second) and what it is (tests).</summary>
    public int RouteA, RouteB;
    public string Why = "";
    /// <summary>How far out along each way its arc meets the kerb, and how far each way runs on from its meeting point, m (tests).</summary>
    public double TanA, TanB, LenA = 1e9, LenB = 1e9;
    /// <summary>Whether its first or second side meets a circle of deck (a ring's outer edge, a turning bulb) rather than a way's kerb line.</summary>
    public bool RoundA, RoundB;
    /// <summary>A turning bulb's disc of deck round a street's end, not a corner: <see cref="R"/> its radius round (X, Z).</summary>
    public bool Disc;
    /// <summary>The tilt of the plane the corner lies on (rise per metre in x and z from its centre): a turning bulb's and its fillets', following the land; 0 elsewhere.</summary>
    public float TiltX, TiltZ;
    /// <summary>Its deck's height at a point (on its tilted plane).</summary>
    public float DeckAt(double x, double z, double width, double height) => Y + TiltX * (float)Mathx.WrapDelta(x - X, width) + TiltZ * (float)Mathx.WrapDelta(z - Z, height);
    /// <summary>The same corner laid again at another radius (null where it won't go): for making room beside another.</summary>
    internal Func<float, RoadCorner> Remake;

    /// <summary>
    /// How far past the kerb a point is by this corner alone (positive in the land it rounds off, negative on the deck
    /// it adds), or infinity where the corner says nothing; the deck's edge is the least of this and the roads' own.
    /// </summary>
    public float Edge(double x, double z, double width, double height)
    {
        double dx = Mathx.WrapDelta(x - X, width), dz = Mathx.WrapDelta(z - Z, height);
        double dist = Math.Sqrt(dx * dx + dz * dz);
        if (dist > Cut) return float.PositiveInfinity;
        if (Disc) return (float)(dist - R);
        double cab = AX * BZ - AZ * BX, c1 = AX * dz - AZ * dx, c2 = dx * BZ - dz * BX;
        bool cone = cab >= 0 ? c1 >= 0 && c2 >= 0 : c1 <= 0 && c2 <= 0;
        if (!cone || dx * AX + dz * AZ > LimA || dx * BX + dz * BZ > LimB) return float.PositiveInfinity;
        return (float)(R - dist);
    }
}

/// <summary>Plans every rounded corner of a network: at its junctions, where streets cross, round its rings and its turning bulbs.</summary>
public static class RoadCorners
{
    /// <summary>A kerb's radius round a corner, m: wide enough for a vehicle to turn from its near lane into the other road's.</summary>
    public static float Radius(InfraRoute a, InfraRoute b)
    {
        bool town = a.Laid.Length == 0 || b.Laid.Length == 0;
        if (town) return Math.Min(a.HalfWidth, b.HalfWidth) < 3.5f ? 4f : 7f;
        return Math.Max(a.HalfWidth, b.HalfWidth) > 5f ? 15f : 12f;
    }

    /// <summary>The bulb a street that just stops turns in: a disc of this radius round its end (the shader's).</summary>
    public static float BulbRadius(InfraRoute r) => r.HalfWidth + 4f;

    /// <summary>Whether a way gets kerb corners: a paved road or highway (not a track, a dirt road or a railway).</summary>
    public static bool Paved(InfraRoute r) => r.Kind is RouteKind.Road or RouteKind.Highway && !r.Soil && r.Count >= 2;

    /// <summary>The least and greatest angle between two ways that are rounded: sharper is a slip's gore, straighter needs no corner.</summary>
    public const double LeastWedge = 30, MostWedge = 165;

    /// <summary>One way out from a meeting point: its centre line from there outward, in metres from the meeting point (with a run back behind it).</summary>
    sealed class Leg
    {
        public InfraRoute Route;
        public List<(double X, double Z)> P = new();
        /// <summary>The index in <see cref="P"/> of the meeting point itself (the points before it run back behind it, straight).</summary>
        public int Start;
        public double Angle;
        /// <summary>Its half width at the meeting point: its own, or where it narrows to meet the way it comes to, that.</summary>
        public float Hw;
    }

    public static List<RoadCorner> Plan(InfraNetwork net, Dictionary<string, int> misses = null, Func<double, double, bool> onPlace = null, List<(double X, double Z, string Why)> missedAt = null)
    {
        var list = new List<RoadCorner>();
        double w = net.Width, h = net.Height;
        void Miss(string why, double x = double.NaN, double z = double.NaN)
        {
            if (misses != null) misses[why] = misses.GetValueOrDefault(why) + 1;
            if (missedAt != null && !double.IsNaN(x)) missedAt.Add((Mathx.WrapX(x, w), Mathx.Wrap(z, h), why));
        }

        // A leg from route point `i` of r, stepping `step` (+1 or -1), as far as `reach` metres, in metres from (ox, oz).
        Leg MakeLeg(InfraRoute r, int i, int step, double ox, double oz, double reach = 70)
        {
            var leg = new Leg { Route = r, Hw = i == 0 && r.StartMeetHw > 0f ? (r.HalfWidth + r.StartMeetHw) * 0.5f : i == r.Count - 1 && r.EndMeetHw > 0f ? (r.HalfWidth + r.EndMeetHw) * 0.5f : r.HalfWidth };
            var pts = new List<(double X, double Z)>();
            double run = 0;
            for (int k = i; k >= 0 && k < r.Count; k += step)
            {
                var p = (Mathx.WrapDelta(r.X[k] - ox, w), Mathx.WrapDelta(r.Z[k] - oz, h));
                if (pts.Count > 0) run += Math.Sqrt(Math.Pow(p.Item1 - pts[^1].X, 2) + Math.Pow(p.Item2 - pts[^1].Z, 2));
                pts.Add(p);
                if (run > reach) break;
            }
            if (pts.Count < 2) return null;
            // Straight on back behind the meeting point (a branch may stop short of the middle of the way it meets).
            double bx = pts[0].X - pts[1].X, bz = pts[0].Z - pts[1].Z, bl = Math.Max(1e-6, Math.Sqrt(bx * bx + bz * bz));
            leg.P.Add((pts[0].X + bx / bl * 24, pts[0].Z + bz / bl * 24));
            leg.Start = 1;
            leg.P.AddRange(pts);
            // Its heading: toward where it is about 10 m out.
            var (ax, az) = At(leg, 10);
            leg.Angle = Math.Atan2(az, ax);
            return leg;
        }

        // ---- the junctions: every pair of neighbouring paved ways round the meeting point
        foreach (InfraJunction j in net.Joins)
        {
            if (j.Form is JunctionForm.Roundabout or JunctionForm.Slip or JunctionForm.Fork or JunctionForm.DeadEnd) continue;
            var legs = new List<Leg>();
            foreach (JunctionArm a in j.Arms)
            {
                InfraRoute r = a.Route;
                int i = r == j.Through ? NearestIndex(r, j.X, j.Z, w, h) : a.Forward ? 0 : r.Count - 1;
                if (MakeLeg(r, i, a.Forward ? 1 : -1, j.X, j.Z) is { } leg) legs.Add(leg);
            }
            Round(legs, j.X, j.Z, j.Y, j.Form.ToString());
        }

        // ---- where two streets cross on through, with no junction planned (a city's grid)
        foreach (var (a, ia, b, ib, x, z, y) in Crossings(net))
        {
            var legs = new List<Leg>();
            foreach (var (r, i) in new[] { (a, ia), (b, ib) })
            {
                if (MakeLeg(r, i, 1, x, z) is { } f) legs.Add(f);
                if (MakeLeg(r, i + 1, -1, x, z) is { } back) legs.Add(back);
            }
            Round(legs, x, z, y, "crossing");
        }

        // ---- a roundabout's ways into its ring
        foreach (InfraJunction j in net.Joins.Where(j => j.Form == JunctionForm.Roundabout && j.Ring != null))
        {
            double ro = j.RingR + j.RingHw;
            foreach (JunctionArm a in j.Arms)
            {
                InfraRoute r = a.Route;
                if (!Paved(r) || r == j.Ring) continue;
                int i = r == j.Through ? NearestIndex(r, j.X, j.Z, w, h) : a.Forward ? 0 : r.Count - 1;
                Leg leg = MakeLeg(r, i, a.Forward ? 1 : -1, j.X, j.Z, 90);
                if (leg == null) continue;
                foreach (int side in new[] { -1, 1 })
                    Circle(leg, side, j.X, j.Z, ro, j.RingHw * 2, Radius(r, j.Ring), j.Y, j.Ring, "ring");
            }
        }

        // ---- a street's turning bulb
        foreach (InfraRoute r in net.Routes.Where(Paved))
            foreach (bool start in new[] { true, false })
            {
                if ((start ? r.StartJoin : r.EndJoin) != JunctionForm.DeadEnd) continue;
                int e = start ? 0 : r.Count - 1;
                Leg leg = MakeLeg(r, e, start ? 1 : -1, r.X[e], r.Z[e], 50);
                if (leg == null) continue;
                float rb = BulbRadius(r);
                int before = list.Count;
                foreach (int side in new[] { -1, 1 })
                    Circle(leg, side, r.X[e], r.Z[e], rb, rb * 2, Radius(r, r), r.Y[e], r, "bulb");
                // The bulb itself: a true circle round the street's end (as its corners assume), whatever the street does before it,
                // on the plane its bed is laid to (the land's grade, as gently as a street's grades allow); its fillets on it too.
                var (tx, tz) = start ? r.StartTilt : r.EndTilt;
                list.Add(new RoadCorner { X = r.X[e], Z = r.Z[e], Y = r.Y[e], R = rb, Cut = rb + 9f, Disc = true, Kerbed = InfraNetwork.Kerbed(r), RouteA = r.Id, RouteB = r.Id, Why = "disc", AX = 1, BX = 1, TiltX = tx, TiltZ = tz });
                for (int k = before; k + 1 < list.Count; k++)
                {
                    RoadCorner c = list[k];
                    c.Y = r.Y[e] + tx * (float)Mathx.WrapDelta(c.X - r.X[e], w) + tz * (float)Mathx.WrapDelta(c.Z - r.Z[e], h);
                    (c.TiltX, c.TiltZ) = (tx, tz);
                }
            }
        // Corners near together on one way (two junctions close along it) each tighten until neither's deck runs over
        // the other's kerb: two corners, never one stretched pad.
        for (int pass = 0; pass < 10; pass++)
        {
            var bad = new HashSet<RoadCorner>();
            for (int i = 0; i < list.Count; i++)
                for (int k = i + 1; k < list.Count; k++)
                {
                    RoadCorner p = list[i], q = list[k];
                    if (p.Disc || q.Disc || Near(p.X, p.Z, q.X, q.Z, w, h) > p.Cut + q.Cut) continue;
                    if (Covers(p, q, w, h) || Covers(q, p, w, h)) { bad.Add(p); bad.Add(q); }
                }
            if (bad.Count == 0) break;
            foreach (RoadCorner c in bad)
            {
                int at = list.IndexOf(c);
                RoadCorner smaller = c.R * 0.8f >= 2.5f ? c.Remake?.Invoke(c.R * 0.8f) : null;
                if (smaller != null) list[at] = smaller;
                else { list.RemoveAt(at); Miss($"{c.Why}: no room beside another", c.X, c.Z); }
            }
        }
        // None on a place's footprint: the road isn't drawn there (the place's ground is its own).
        if (onPlace != null)
            list.RemoveAll(c =>
            {
                double mx = c.AX + c.BX, mz = c.AZ + c.BZ, ml = Math.Max(1e-6, Math.Sqrt(mx * mx + mz * mz));
                return onPlace(c.X, c.Z) || onPlace(c.X + c.AX * c.R, c.Z + c.AZ * c.R) || onPlace(c.X + c.BX * c.R, c.Z + c.BZ * c.R)
                    || onPlace(c.X + mx / ml * c.R, c.Z + mz / ml * c.R);
            });
        return list;

        // The corners between each pair of neighbouring legs round a meeting point (in order of heading).
        void Round(List<Leg> legs, double ox, double oz, float y, string why)
        {
            legs = legs.Where(l => Paved(l.Route)).OrderBy(l => l.Angle).ToList();
            if (legs.Count < 2) return;
            for (int k = 0; k < legs.Count; k++)
            {
                Leg a = legs[k], b = legs[(k + 1) % legs.Count];
                double wedge = (b.Angle - a.Angle) * 180 / Math.PI;
                wedge = (wedge % 360 + 360) % 360;
                if (wedge < LeastWedge || wedge > MostWedge) { if (wedge < LeastWedge) Miss($"{why}: too sharp", ox, oz); continue; }
                // The land between them lies to a's left (its heading turning toward b's) and b's right.
                RoadCorner At(float r)
                {
                    RoadCorner c = Fillet(a, 1, b, -1, r, ox, oz, why);
                    if (c == null) return null;
                    c.X += ox;
                    c.Z += oz;
                    c.Remake = At;
                    return c;
                }
                float r0 = Radius(a.Route, b.Route);
                RoadCorner made = null;
                for (int tries = 0; tries < 5 && made == null; tries++, r0 *= 0.75f) made = At(r0);
                if (made == null) { Miss($"{why}: no room", ox, oz); continue; }
                list.Add(made);
            }
        }

        // The deck's height on a way at a point near it (its nearest point's).
        float DeckAt(InfraRoute r, double x, double z) => r.Y[NearestIndex(r, x, z, w, h)];

        // A corner between leg a's kerb on side sa and leg b's on side sb (+1 left of the way out, -1 right).
        RoadCorner Fillet(Leg a, int sa, Leg b, int sb, float r, double ox, double oz, string why)
        {
            var oa = Offset(a, sa, a.Hw + r);
            var ob = Offset(b, sb, b.Hw + r);
            if (!Intersect(oa, ob, out double cx, out double cz)) return null;
            var (fa, ta) = Foot(a, cx, cz);
            var (fb, tb) = Foot(b, cx, cz);
            // The arc meets each kerb on the leg itself, short of its far end.
            if (ta < 0 || tb < 0 || ta > Length(a) - 1 || tb > Length(b) - 1) return null;
            // Its deck at the height of the two ways where it meets them.
            float y = (DeckAt(a.Route, fa.X + ox, fa.Z + oz) + DeckAt(b.Route, fb.X + ox, fb.Z + oz)) * 0.5f;
            var c = Make(a.Route, b.Route, cx, cz, fa, fb, r, a.Hw * 2, b.Hw * 2, y, why);
            if (c == null) return null;
            c.TanA = ta;
            c.TanB = tb;
            c.LenA = Length(a);
            c.LenB = Length(b);
            return c;
        }

        // The corners either side of a leg where it meets a circle of deck (a ring's outer edge, a bulb): the arc
        // outside it, tangent to the leg's kerb and to the circle.
        void Circle(Leg a, int side, double ox, double oz, double ro, float circleDeck, float r0, float y, InfraRoute circle, string why)
        {
            for (int tries = 0; tries < 5; tries++, r0 *= 0.75f)
                if (OnCircle(r0) is { } made) { list.Add(made); return; }
            Miss($"{why}: no room", ox, oz);

            RoadCorner OnCircle(float r)
            {
                var oa = Offset(a, side, a.Hw + r);
                // The offset line's first crossing of the circle round the middle at ro + r, going out.
                double want = ro + r;
                (double X, double Z)? hit = null;
                for (int k = a.Start; k + 1 < oa.Count && hit == null; k++)
                {
                    var (px, pz) = oa[k];
                    var (qx, qz) = oa[k + 1];
                    // (Both relative to the leg's own meeting point, which is the circle's middle.)
                    double dp = Math.Sqrt(px * px + pz * pz), dq = Math.Sqrt(qx * qx + qz * qz);
                    if (dp <= want && dq > want)
                    {
                        // On the segment, where its distance from the middle passes the radius (not linear along it).
                        double lo = 0, hi = 1, t;
                        for (int it = 0; it < 30; it++)
                        {
                            double m = (lo + hi) * 0.5, mx = px + (qx - px) * m, mz = pz + (qz - pz) * m;
                            if (Math.Sqrt(mx * mx + mz * mz) < want) lo = m; else hi = m;
                        }
                        t = (lo + hi) * 0.5;
                        hit = (px + (qx - px) * t, pz + (qz - pz) * t);
                    }
                }
                if (hit is not { } cc) return null;
                var (fa, ta) = Foot(a, cc.X, cc.Z);
                if (ta < 0 || ta > Length(a) - 1) return null;
                double l = Math.Sqrt(cc.X * cc.X + cc.Z * cc.Z);
                var fb = (cc.X - cc.X / l * r, cc.Z - cc.Z / l * r);   // where the arc meets the circle
                float yc = (DeckAt(a.Route, fa.X + ox, fa.Z + oz) + y) * 0.5f;
                RoadCorner c = side > 0
                    ? Make(a.Route, circle, cc.X + ox, cc.Z + oz, (fa.X + ox, fa.Z + oz), (fb.Item1 + ox, fb.Item2 + oz), r, a.Hw * 2, circleDeck, yc, why, local: false)
                    : Make(circle, a.Route, cc.X + ox, cc.Z + oz, (fb.Item1 + ox, fb.Item2 + oz), (fa.X + ox, fa.Z + oz), r, circleDeck, a.Hw * 2, yc, why, local: false);
                if (c == null) return null;
                c.TanA = side > 0 ? ta : 0;
                c.TanB = side > 0 ? 0 : ta;
                if (side > 0) c.LenA = Length(a); else c.LenB = Length(a);
                c.RoundA = side < 0;
                c.RoundB = side > 0;
                c.Remake = OnCircle;
                return c;
            }
        }

        // The corner from its centre and the two tangent points (kerb points: the foot on each way's centre line, or on
        // the circle): local to the meeting point unless told otherwise, which is added back by the caller.
        RoadCorner Make(InfraRoute ra, InfraRoute rb, double cx, double cz, (double X, double Z) fa, (double X, double Z) fb, float r, float deckA, float deckB, float y, string why, bool local = true)
        {
            double ax = fa.X - cx, az = fa.Z - cz, bx = fb.X - cx, bz = fb.Z - cz;
            double la = Math.Sqrt(ax * ax + az * az), lb = Math.Sqrt(bx * bx + bz * bz);
            if (la < 1e-6 || lb < 1e-6) return null;
            ax /= la; az /= la; bx /= lb; bz /= lb;
            double phi = Math.Acos(Math.Clamp(ax * bx + az * bz, -1, 1));
            if (phi > Math.PI * (180 - LeastWedge) / 180) return null;
            return new RoadCorner
            {
                X = cx, Z = cz, Y = y, R = r, AX = (float)ax, AZ = (float)az, BX = (float)bx, BZ = (float)bz,
                LimA = r + deckA - 0.8f, LimB = r + deckB - 0.8f, Cut = (float)(r / Math.Cos(phi / 2) + 3),
                Kerbed = InfraNetwork.Kerbed(ra) || InfraNetwork.Kerbed(rb), RouteA = ra.Id, RouteB = rb.Id, Why = why,
            };
        }
    }

    /// <summary>Whether one corner's deck runs over the other's kerb (any point of its arc).</summary>
    static bool Covers(RoadCorner deck, RoadCorner kerb, double w, double h)
    {
        double a0 = Math.Atan2(kerb.AZ, kerb.AX), cab = kerb.AX * kerb.BZ - kerb.AZ * kerb.BX;
        double sweep = Math.Acos(Math.Clamp(kerb.AX * kerb.BX + kerb.AZ * kerb.BZ, -1, 1)) * Math.Sign(cab);
        for (int i = 0; i <= 8; i++)
        {
            double ang = a0 + sweep * i / 8;
            if (deck.Edge(kerb.X + Math.Cos(ang) * kerb.R, kerb.Z + Math.Sin(ang) * kerb.R, w, h) < -0.05f) return true;
        }
        return false;
    }

    /// <summary>Puts every corner's centre on the map (planned relative to its meeting point).</summary>
    public static void Settle(List<RoadCorner> corners, double w, double h)
    {
        foreach (RoadCorner c in corners)
        {
            c.X = Mathx.WrapX(c.X, w);
            c.Z = Mathx.Wrap(c.Z, h);
        }
    }

    static int NearestIndex(InfraRoute r, double x, double z, double w, double h)
    {
        int best = 0;
        double bd = double.MaxValue;
        for (int i = 0; i < r.Count; i++)
        {
            double dx = Mathx.WrapDelta(r.X[i] - x, w), dz = Mathx.WrapDelta(r.Z[i] - z, h), d = dx * dx + dz * dz;
            if (d < bd) { bd = d; best = i; }
        }
        return best;
    }

    /// <summary>The point <paramref name="along"/> metres out along a leg from its meeting point.</summary>
    static (double X, double Z) At(Leg leg, double along)
    {
        double run = 0;
        for (int k = leg.Start; k + 1 < leg.P.Count; k++)
        {
            double sx = leg.P[k + 1].X - leg.P[k].X, sz = leg.P[k + 1].Z - leg.P[k].Z, sl = Math.Sqrt(sx * sx + sz * sz);
            if (run + sl >= along) { double t = (along - run) / Math.Max(1e-9, sl); return (leg.P[k].X + sx * t, leg.P[k].Z + sz * t); }
            run += sl;
        }
        return leg.P[^1];
    }

    static double Length(Leg leg)
    {
        double run = 0;
        for (int k = leg.Start; k + 1 < leg.P.Count; k++) run += Math.Sqrt(Math.Pow(leg.P[k + 1].X - leg.P[k].X, 2) + Math.Pow(leg.P[k + 1].Z - leg.P[k].Z, 2));
        return run;
    }

    /// <summary>The leg's centre line offset to one side (+1 left of the way out) by a distance, point by point.</summary>
    static List<(double X, double Z)> Offset(Leg leg, int side, double by)
    {
        var o = new List<(double X, double Z)>(leg.P.Count);
        for (int k = 0; k < leg.P.Count; k++)
        {
            var p = leg.P[Math.Max(0, k - 1)];
            var q = leg.P[Math.Min(leg.P.Count - 1, k + 1)];
            double tx = q.X - p.X, tz = q.Z - p.Z, tl = Math.Max(1e-9, Math.Sqrt(tx * tx + tz * tz));
            // Left of the way out: the heading turned a quarter toward +z from +x (atan2's sense).
            o.Add((leg.P[k].X - tz / tl * side * by, leg.P[k].Z + tx / tl * side * by));
        }
        return o;
    }

    /// <summary>The first place (nearest the meeting point along both) two polylines cross.</summary>
    static bool Intersect(List<(double X, double Z)> a, List<(double X, double Z)> b, out double x, out double z)
    {
        x = z = 0;
        double best = double.MaxValue;
        for (int i = 0; i + 1 < a.Count; i++)
            for (int k = 0; k + 1 < b.Count; k++)
            {
                var (p, p2) = (a[i], a[i + 1]);
                var (q, q2) = (b[k], b[k + 1]);
                double rx = p2.X - p.X, rz = p2.Z - p.Z, sx = q2.X - q.X, sz = q2.Z - q.Z, den = rx * sz - rz * sx;
                if (Math.Abs(den) < 1e-9) continue;
                double t = ((q.X - p.X) * sz - (q.Z - p.Z) * sx) / den, u = ((q.X - p.X) * rz - (q.Z - p.Z) * rx) / den;
                if (t < 0 || t > 1 || u < 0 || u > 1) continue;
                double score = i + t + k + u;
                if (score >= best) continue;
                best = score;
                x = p.X + rx * t;
                z = p.Z + rz * t;
            }
        return best < double.MaxValue;
    }

    /// <summary>The nearest point of a leg's centre line (its meeting point on, not the run back) to a point, and how far out along it that is.</summary>
    static ((double X, double Z) P, double Along) Foot(Leg leg, double x, double z)
    {
        double bd = double.MaxValue, run = 0, along = -1;
        (double X, double Z) best = leg.P[leg.Start];
        for (int k = leg.Start; k + 1 < leg.P.Count; k++)
        {
            var (p, q) = (leg.P[k], leg.P[k + 1]);
            double sx = q.X - p.X, sz = q.Z - p.Z, l2 = sx * sx + sz * sz, sl = Math.Sqrt(l2);
            double t = l2 < 1e-12 ? 0 : Math.Clamp(((x - p.X) * sx + (z - p.Z) * sz) / l2, 0, 1);
            double fx = p.X + sx * t, fz = p.Z + sz * t, d = (fx - x) * (fx - x) + (fz - z) * (fz - z);
            if (d < bd) { bd = d; best = (fx, fz); along = run + sl * t; }
            run += sl;
        }
        // At the meeting point itself the nearest might lie behind it: then the arc would meet the kerb before the way begins.
        if (along <= 1e-6)
        {
            var (p, q) = (leg.P[leg.Start - 1], leg.P[leg.Start]);
            double sx = q.X - p.X, sz = q.Z - p.Z, l2 = sx * sx + sz * sz;
            double t = l2 < 1e-12 ? 1 : ((x - p.X) * sx + (z - p.Z) * sz) / l2;
            if (t < 1 - 1e-9) return (best, -1);
        }
        return (best, along);
    }

    /// <summary>
    /// Where two paved ways cross on through, both on the ground at one deck height, with no junction planned near
    /// (a city's grid runs its streets straight on through every crossing): each route, its stretch's index there, and the point.
    /// </summary>
    static IEnumerable<(InfraRoute A, int IA, InfraRoute B, int IB, double X, double Z, float Y)> Crossings(InfraNetwork net)
    {
        double w = net.Width, h = net.Height;
        const double Cell = 32;
        var cells = new Dictionary<(int, int), List<(InfraRoute R, int I)>>();
        var paved = net.Routes.Where(r => Paved(r) && !r.Ring).ToList();
        foreach (InfraRoute r in paved)
            for (int i = 0; i + 1 < r.Count; i++)
            {
                if (r.Span[i] != RoadSpan.Ground) continue;
                var key = ((int)Math.Floor(Mathx.WrapX(r.X[i], w) / Cell), (int)Math.Floor(Mathx.Wrap(r.Z[i], h) / Cell));
                if (!cells.TryGetValue(key, out var l)) cells[key] = l = new();
                l.Add((r, i));
            }
        var seen = new HashSet<(int, int, int, int)>();
        var found = new List<(double X, double Z)>();
        foreach (var (key, l) in cells)
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!cells.TryGetValue((key.Item1 + dx, key.Item2 + dz), out var m)) continue;
                    foreach (var (ra, ia) in l)
                        foreach (var (rb, ib) in m)
                        {
                            if (ra.Id >= rb.Id) continue;
                            double px = Mathx.WrapX(ra.X[ia], w), pz = Mathx.Wrap(ra.Z[ia], h);
                            double ax = 0, az = 0, a2x = ra.X[ia + 1] - ra.X[ia], a2z = ra.Z[ia + 1] - ra.Z[ia];
                            double bx = Mathx.WrapDelta(rb.X[ib] - px, w), bz = Mathx.WrapDelta(rb.Z[ib] - pz, h);
                            double b2x = bx + rb.X[ib + 1] - rb.X[ib], b2z = bz + rb.Z[ib + 1] - rb.Z[ib];
                            double rx = a2x - ax, rz = a2z - az, sx = b2x - bx, sz = b2z - bz, den = rx * sz - rz * sx;
                            if (Math.Abs(den) < 1e-9) continue;
                            double t = ((bx - ax) * sz - (bz - az) * sx) / den, u = ((bx - ax) * rz - (bz - az) * rx) / den;
                            if (t < 0 || t >= 1 || u < 0 || u >= 1) continue;
                            if (!seen.Add((ra.Id, ia, rb.Id, ib))) continue;
                            double x = px + rx * t, z = pz + rz * t;
                            float ya = ra.Y[ia] + (ra.Y[ia + 1] - ra.Y[ia]) * (float)t, yb = rb.Y[ib] + (rb.Y[ib + 1] - rb.Y[ib]) * (float)u;
                            // An overpass, not a crossing; and none where a junction already stands (its corners are its own).
                            if (Math.Abs(ya - yb) > 1f) continue;
                            if (net.Joins.Any(j => Near(j.X, j.Z, x, z, w, h) < Math.Max(24, j.RingR + j.RingHw + 6))) continue;
                            // Ways that only touch near an end (a street running on into another) aren't a crossing.
                            if (ia < 2 || ia + 3 > ra.Count || ib < 2 || ib + 3 > rb.Count) continue;
                            if (found.Any(f => Near(f.X, f.Z, x, z, w, h) < 6)) continue;
                            found.Add((x, z));
                            yield return (ra, ia, rb, ib, x, z, (ya + yb) * 0.5f);
                        }
                }
    }

    static double Near(double ax, double az, double bx, double bz, double w, double h)
    {
        double dx = Mathx.WrapDelta(ax - bx, w), dz = Mathx.WrapDelta(az - bz, h);
        return Math.Sqrt(dx * dx + dz * dz);
    }
}
