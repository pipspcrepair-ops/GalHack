using System;
using System.Collections.Generic;
using System.Numerics;

namespace GalHack.Plants;

/// <summary>
/// Collects flat-shaded triangles: the low-poly plants' whole vocabulary of tubes, turned shapes,
/// lumps, skirts and blades. Triangles are given with an outward hint and emitted clockwise, which
/// is the engine's front face, so faces light from the side they should.
/// </summary>
internal sealed class Sculptor
{
    readonly List<Vector3> _p = new(), _n = new();
    readonly List<Vector4> _c = new();
    readonly List<int> _owner = new();
    readonly List<byte> _kinds = new() { 0 };
    int _depth, _part;

    public int Triangles => _p.Count / 3;

    /// <summary>The solid parts laid down so far, as columns for colliders (twigs, wands and leaves aren't: they're walked through).</summary>
    public readonly List<PlantColumn> Columns = new();

    /// <summary>A part thinner than this is walked through: a twig, a cactus's wand, a frond's rib.</summary>
    public const float SolidRadius = 0.06f;

    /// <summary>How deep under the foot a column reaches: an instance stands sunk a little into its ground.</summary>
    const float ColumnFloor = -0.3f;

    /// <summary>How far a point lies from the segment a to b.</summary>
    static float OffLine(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 e = b - a;
        float t = Math.Clamp(Vector3.Dot(p - a, e) / MathF.Max(e.LengthSquared(), 1e-9f), 0f, 1f);
        return Vector3.Distance(p, a + e * t);
    }

    /// <summary>A stretch of a part as a column, cut off below the ground; nothing if it's too thin or wholly underground.</summary>
    void Column(Vector3 a, Vector3 b, float radius)
    {
        if (radius < SolidRadius || a.Y < ColumnFloor && b.Y < ColumnFloor) return;
        if (a.Y < ColumnFloor) a = Vector3.Lerp(a, b, (ColumnFloor - a.Y) / (b.Y - a.Y));
        if (b.Y < ColumnFloor) b = Vector3.Lerp(b, a, (ColumnFloor - b.Y) / (a.Y - b.Y));
        if (Vector3.Distance(a, b) < 0.02f) return;
        Columns.Add(new PlantColumn(a, b, radius, _part));
    }

    /// <summary>Each primitive is a part, numbered from 1; one made of others (a tube is quads, a quad is triangles) is still one part.</summary>
    void Begin(PartKind kind)
    {
        if (_depth++ > 0) return;
        _part = _kinds.Count;
        _kinds.Add((byte)kind);
    }

    void End() => _depth--;

    public PlantMesh Finish() => new() { Positions = _p.ToArray(), Normals = _n.ToArray(), Colors = _c.ToArray(), PartOf = _owner.ToArray(), PartKind = _kinds.ToArray() };

    /// <summary>
    /// Nothing floats: whatever the body plan left adrift gets a branch. A group of parts that no part
    /// in the ground reaches is joined to the nearest of those that are held (from a limb or trunk where
    /// one is about as near), and a lobe of leaves that no wood touches is joined to the nearest wood. The
    /// branch runs from the wood into the mass, <see cref="PlantAudit"/>'s own rule deciding what counts as joined.
    /// </summary>
    public void Weld(Dice d, Vector4 bark, float radius, Vector3? stemTop = null)
    {
        for (int pass = 0; pass < 40; pass++)
        {
            PlantMesh m = Finish();
            PlantAudit.Report r = PlantAudit.Analyse(m);
            var adrift = new List<int>(r.Adrift());
            bool lobe = false;
            if (adrift.Count == 0)
            {
                adrift.AddRange(r.Loose(m));
                lobe = true;
                if (adrift.Count == 0)
                {
                    if (stemTop is Vector3 top && JoinStem(m, top, d, bark, radius)) continue;
                    return;
                }
            }
            // The group to join: its parts' vertices, against the held (or wood) parts' vertices.
            int target = adrift[0];
            var mine = new List<int>();
            if (lobe) mine.Add(target);
            else
                for (int p = 1; p <= r.Parts; p++)
                    if (r.Count[p] > 0 && r.Group[p] == r.Group[target]) mine.Add(p);
            Vector3 best0 = default, best1 = default, centre = default;
            float best = float.MaxValue;
            int bestPart = -1;
            for (int p = 1; p <= r.Parts; p++)
            {
                if (r.Count[p] == 0 || mine.Contains(p)) continue;
                bool wood = m.PartKind[p] == (byte)PartKind.Wood;
                if (lobe ? !wood : !r.GroupRooted[r.Group[p]]) continue;
                float weight = wood ? 0.5f : 1f;
                for (int i = 0; i < r.Count[p]; i++)
                    for (int c = 0; c < 3; c++)
                    {
                        Vector3 from = m.Positions[r.Tris[r.First[p] + i] * 3 + c];
                        foreach (int q in mine)
                            for (int j = 0; j < r.Count[q]; j++)
                                for (int k = 0; k < 3; k++)
                                {
                                    Vector3 to = m.Positions[r.Tris[r.First[q] + j] * 3 + k];
                                    float dist = Vector3.Distance(from, to) * weight;
                                    if (dist >= best) continue;
                                    best = dist;
                                    best0 = from;
                                    best1 = to;
                                    bestPart = q;
                                }
                    }
            }
            if (bestPart < 0) return;
            centre = Vector3.Zero;
            for (int t = 0; t < r.Count[bestPart]; t++)
                for (int v = 0; v < 3; v++) centre += m.Positions[r.Tris[r.First[bestPart] + t] * 3 + v];
            centre /= r.Count[bestPart] * 3f;
            // Into the mass a little way, so the branch is buried in it and shows no end.
            Vector3 end = best1 + (centre - best1) * 0.4f;
            Vector3 way = end - best0;
            float length = way.Length();
            if (length < 0.03f)
            {
                end = best0 + Vector3.UnitY * 0.08f;
                way = end - best0;
                length = 0.08f;
            }
            Vector3 dir = way / length;
            Bridge(best0, end, dir, radius, bark);
        }
    }

    /// <summary>A thin straight branch, three sides, buried at both ends: thin enough that whatever it starts in is within touching distance of its skin.</summary>
    void Bridge(Vector3 from, Vector3 to, Vector3 dir, float radius, Vector4 bark)
    {
        float rad = Math.Clamp(radius, 0.03f, PlantAudit.Touch * 0.7f);
        Tube(new[] { from - dir * rad, to }, new[] { rad, rad * 0.6f }, 3, bark, bark, false);
    }

    /// <summary>A trunk's top that nothing meets: a branch from it into the nearest other part, and on into that part. False when it is met.</summary>
    bool JoinStem(PlantMesh m, Vector3 top, Dice d, Vector4 bark, float radius)
    {
        float best = float.MaxValue;
        Vector3 at = default;
        int part = -1;
        for (int t = 0; t < m.PartOf.Length; t++)
        {
            if (m.PartOf[t] == 1) continue;
            Vector3 q = PlantAudit.Closest(top, m.Positions[t * 3], m.Positions[t * 3 + 1], m.Positions[t * 3 + 2]);
            float dist = Vector3.Distance(top, q);
            if (dist >= best) continue;
            best = dist;
            at = q;
            part = m.PartOf[t];
        }
        if (PlantAudit.TrunkMeets(m, top) || part < 0) return false;
        Vector3 centre = Vector3.Zero;
        int n = 0;
        for (int t = 0; t < m.PartOf.Length; t++)
            if (m.PartOf[t] == part) { centre += m.Positions[t * 3] + m.Positions[t * 3 + 1] + m.Positions[t * 3 + 2]; n += 3; }
        centre /= n;
        Vector3 end = at + (centre - at) * 0.4f, way = end - top;
        float length = way.Length();
        Vector3 dir = way / length;
        Bridge(top, end, dir, radius, bark);
        return true;
    }

    public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 outward, Vector4 ca, Vector4 cb, Vector4 cc)
    {
        Vector3 n = Vector3.Cross(b - a, c - a);
        float length = n.Length();
        if (length < 1e-7f) return;
        Begin(PartKind.Body);
        _owner.Add(_part);
        End();
        n /= length;
        if (Vector3.Dot(n, outward) < 0f)
        {
            (b, c) = (c, b);
            (cb, cc) = (cc, cb);
            n = -n;
        }
        _p.Add(a); _p.Add(c); _p.Add(b);
        _n.Add(n); _n.Add(n); _n.Add(n);
        _c.Add(ca); _c.Add(cc); _c.Add(cb);
    }

    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, Vector4 ca, Vector4 cb, Vector4 cc, Vector4 cd)
    {
        Begin(PartKind.Body);
        Tri(a, b, c, outward, ca, cb, cc);
        Tri(a, c, d, outward, ca, cc, cd);
        End();
    }

    static Vector3 Perpendicular(Vector3 v) =>
        Vector3.Normalize(Vector3.Cross(v, MathF.Abs(v.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX));

    /// <summary>A bending tube along a path, its colour running along its length. Radius 0 at the end comes to a point.</summary>
    public void Tube(IReadOnlyList<Vector3> path, IReadOnlyList<float> radii, int sides, Vector4 from, Vector4 to, bool cap = true)
    {
        Begin(PartKind.Wood);
        TubeCore(path, radii, sides, from, to, cap);
        // Its columns: a straight run every two nodes, wide enough to hold the tube there however it bends
        // (each node's ring, wherever the node strays from the run's line).
        for (int i = 0; i + 1 < path.Count; i += 2)
        {
            int j = Math.Min(i + 2, path.Count - 1);
            float r = 0f;
            for (int k = i; k <= j; k++) r = MathF.Max(r, radii[k] + OffLine(path[k], path[i], path[j]));
            Column(path[i], path[j], r);
        }
        End();
    }

    void TubeCore(IReadOnlyList<Vector3> path, IReadOnlyList<float> radii, int sides, Vector4 from, Vector4 to, bool cap = true)
    {
        int n = path.Count;
        var rings = new Vector3[n][];
        Vector3 u = Perpendicular(Vector3.Normalize(path[1] - path[0]));
        Vector3 tangent = Vector3.UnitY;
        for (int i = 0; i < n; i++)
        {
            tangent = Vector3.Normalize(path[Math.Min(i + 1, n - 1)] - path[Math.Max(i - 1, 0)]);
            u = Vector3.Normalize(u - tangent * Vector3.Dot(u, tangent));
            Vector3 v = Vector3.Cross(tangent, u);
            rings[i] = new Vector3[sides];
            for (int k = 0; k < sides; k++)
            {
                float a = k * MathF.Tau / sides;
                rings[i][k] = path[i] + (u * MathF.Cos(a) + v * MathF.Sin(a)) * radii[i];
            }
        }
        for (int i = 0; i + 1 < n; i++)
        {
            Vector4 c0 = Vector4.Lerp(from, to, i / (n - 1f)), c1 = Vector4.Lerp(from, to, (i + 1) / (n - 1f));
            for (int k = 0; k < sides; k++)
            {
                int j = (k + 1) % sides;
                Vector3 outward = (rings[i][k] + rings[i][j]) * 0.5f - path[i];
                Quad(rings[i][k], rings[i][j], rings[i + 1][j], rings[i + 1][k], outward, c0, c0, c1, c1);
            }
        }
        if (!cap || radii[n - 1] <= 0f) return;
        Vector3 top = path[n - 1] + tangent * radii[n - 1] * 0.4f;
        for (int k = 0; k < sides; k++)
            Tri(rings[n - 1][k], rings[n - 1][(k + 1) % sides], top, tangent, to, to, to);
    }

    /// <summary>
    /// A shape turned round an axis. The profile (radius, height) runs from the bottom, out and up
    /// the outside, to the top: so a lens, a cap with a skirt or a bottle all read outward.
    /// </summary>
    public void Lathe(Vector3 at, Vector3 axis, (float R, float Y)[] profile, int sides, Func<int, Vector4> band, float ribs = 0f, float wobble = 0f, Dice dice = null, float squash = 1f, float phase = 0f)
    {
        Begin(PartKind.Body);
        LatheCore(at, axis, profile, sides, band, ribs, wobble, dice, squash, phase);
        // Its columns: each step of the profile up its axis, as wide as the step's widest (ribbed and wobbled outward).
        Vector3 up = Vector3.Normalize(axis);
        float grow = (1f + ribs) * (1f + wobble) * MathF.Max(1f, squash);
        for (int i = 0; i + 1 < profile.Length; i++)
        {
            float y0 = MathF.Min(profile[i].Y, profile[i + 1].Y), y1 = MathF.Max(profile[i].Y, profile[i + 1].Y);
            if (y1 - y0 < 0.02f) y1 = y0 + 0.02f; // a flat step (a cap's rim) is a thin disc
            Column(at + up * y0, at + up * y1, MathF.Max(profile[i].R, profile[i + 1].R) * grow);
        }
        End();
    }

    void LatheCore(Vector3 at, Vector3 axis, (float R, float Y)[] profile, int sides, Func<int, Vector4> band, float ribs = 0f, float wobble = 0f, Dice dice = null, float squash = 1f, float phase = 0f)
    {
        axis = Vector3.Normalize(axis);
        Vector3 u = Perpendicular(axis), v = Vector3.Cross(axis, u);
        var ring = new Vector3[profile.Length][];
        for (int i = 0; i < profile.Length; i++)
        {
            ring[i] = new Vector3[sides];
            for (int k = 0; k < sides; k++)
            {
                float a = (k + phase) * MathF.Tau / sides;
                float r = profile[i].R * (1f + (k % 2 == 0 ? ribs : -ribs));
                if (wobble > 0f && dice != null) r *= 1f + dice.Range(-wobble, wobble);
                ring[i][k] = at + (u * MathF.Cos(a) * squash + v * MathF.Sin(a)) * r + axis * profile[i].Y;
            }
        }
        for (int i = 0; i + 1 < profile.Length; i++)
        {
            (float r0, float y0) = profile[i];
            (float r1, float y1) = profile[i + 1];
            float nr = y1 - y0, ny = r0 - r1; // to the right of the way the profile walks: the outside
            Vector4 c0 = band(i), c1 = band(i + 1);
            for (int k = 0; k < sides; k++)
            {
                int j = (k + 1) % sides;
                Vector3 side = (ring[i][k] + ring[i][j] + ring[i + 1][k] + ring[i + 1][j]) * 0.25f - at;
                side -= axis * Vector3.Dot(side, axis);
                if (side.LengthSquared() > 1e-8f) side = Vector3.Normalize(side);
                Vector3 outward = side * nr + axis * ny;
                Quad(ring[i][k], ring[i][j], ring[i + 1][j], ring[i + 1][k], outward, c0, c0, c1, c1);
            }
        }
    }

    static readonly Vector3[] Ico = MakeIco();

    static readonly int[] IcoFaces =
    {
        0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
        3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
    };

    static Vector3[] MakeIco()
    {
        float t = (1f + MathF.Sqrt(5f)) / 2f;
        Vector3[] p =
        {
            new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0), new(0, -1, t), new(0, 1, t),
            new(0, -1, -t), new(0, 1, -t), new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
        };
        for (int i = 0; i < p.Length; i++) p[i] = Vector3.Normalize(p[i]);
        return p;
    }

    /// <summary>A faceted mass of leaves: nudged out of round, flatter underneath as a real crown is, light on top and dark below.</summary>
    public void Lump(Vector3 centre, Vector3 radii, Dice dice, Vector4 top, Vector4 under, float jitter = 0.18f, float flatBelow = 0.55f)
    {
        Begin(PartKind.Lobe);
        LumpCore(centre, radii, dice, top, under, jitter, flatBelow);
        End();
    }

    void LumpCore(Vector3 centre, Vector3 radii, Dice dice, Vector4 top, Vector4 under, float jitter = 0.18f, float flatBelow = 0.55f)
    {
        var v = new Vector3[Ico.Length];
        for (int i = 0; i < v.Length; i++)
        {
            Vector3 d = Ico[i] * (1f + dice.Range(-jitter, jitter));
            if (d.Y < 0f) d.Y *= flatBelow;
            v[i] = centre + d * radii;
        }
        float low = centre.Y - radii.Y * flatBelow, high = centre.Y + radii.Y;
        Vector4 C(Vector3 p) => Vector4.Lerp(under, top, Math.Clamp((p.Y - low) / (high - low), 0f, 1f));
        for (int i = 0; i < IcoFaces.Length; i += 3)
        {
            Vector3 a = v[IcoFaces[i]], b = v[IcoFaces[i + 1]], c = v[IcoFaces[i + 2]];
            Tri(a, b, c, (a + b + c) / 3f - centre, C(a), C(b), C(c));
        }
    }

    /// <summary>A hanging skirt of foliage round a trunk: a cone with a toothed, drooping hem.</summary>
    public void Skirt(Vector3 at, float radius, float height, int sides, float jag, float droop, Dice dice, Vector4 top, Vector4 hem, Vector4 under)
    {
        Begin(PartKind.Body);
        SkirtCore(at, radius, height, sides, jag, droop, dice, top, hem, under);
        End();
    }

    void SkirtCore(Vector3 at, float radius, float height, int sides, float jag, float droop, Dice dice, Vector4 top, Vector4 hem, Vector4 under)
    {
        Vector3 apex = at + Vector3.UnitY * height;
        var rim = new Vector3[sides];
        float phase = dice.Unit() * MathF.Tau;
        for (int k = 0; k < sides; k++)
        {
            float a = phase + k * MathF.Tau / sides;
            float r = radius * (k % 2 == 0 ? 1f : 1f - jag) * (1f + dice.Range(-0.07f, 0.07f));
            rim[k] = at + new Vector3(MathF.Cos(a) * r, -(k % 2 == 0 ? droop : 0f) * radius, MathF.Sin(a) * r);
        }
        Vector3 hub = at + Vector3.UnitY * height * 0.12f;
        for (int k = 0; k < sides; k++)
        {
            Vector3 p = rim[k], q = rim[(k + 1) % sides];
            Tri(p, q, apex, (p + q) * 0.5f - at + Vector3.UnitY * 0.4f * radius, hem, hem, top);
            Tri(p, q, hub, -Vector3.UnitY, under, under, under);
        }
    }

    /// <summary>
    /// A leaf, frond or blade: an arching strip from its root along dir, widest part way out, with
    /// an optional fold along its midrib. Ends in a point.
    /// </summary>
    public void Blade(Vector3 root, Vector3 dir, float length, float width, float arch, int segments, Vector4 from, Vector4 to, float fold = 0f, float widest = 0.4f)
    {
        Begin(PartKind.Body);
        BladeCore(root, dir, length, width, arch, segments, from, to, fold, widest);
        End();
    }

    void BladeCore(Vector3 root, Vector3 dir, float length, float width, float arch, int segments, Vector4 from, Vector4 to, float fold = 0f, float widest = 0.4f)
    {
        dir = Vector3.Normalize(dir);
        Vector3 side = Vector3.Cross(dir, Vector3.UnitY);
        side = side.LengthSquared() < 1e-4f ? Vector3.UnitX : Vector3.Normalize(side);
        Vector3 up = Vector3.Cross(side, dir);
        Vector3 Spine(float t) => root + dir * (length * t) - Vector3.UnitY * (arch * length * t * t);
        float Half(float t) => width * 0.5f * (t < widest ? 0.45f + 0.55f * t / widest : (1f - t) / (1f - widest));
        for (int i = 0; i < segments; i++)
        {
            float t0 = i / (float)segments, t1 = (i + 1) / (float)segments;
            Vector3 p0 = Spine(t0), p1 = Spine(t1);
            float w0 = Half(t0), w1 = i + 1 == segments ? 0f : Half(t1);
            Vector4 c0 = Vector4.Lerp(from, to, t0), c1 = Vector4.Lerp(from, to, t1);
            Vector3 l0 = p0 + side * w0 - up * fold * w0, r0 = p0 - side * w0 - up * fold * w0;
            Vector3 l1 = p1 + side * w1 - up * fold * w1, r1 = p1 - side * w1 - up * fold * w1;
            if (fold > 0f)
            {
                Tri(p0, l0, p1, up, c0, c0, c1);
                Tri(p0, r0, p1, up, c0, c0, c1);
                if (w1 > 0f)
                {
                    Tri(l0, l1, p1, up, c0, c1, c1);
                    Tri(r0, r1, p1, up, c0, c1, c1);
                }
            }
            else
            {
                Tri(l0, r0, r1, up, c0, c0, c1);
                if (w1 > 0f) Tri(l0, r1, l1, up, c0, c1, c1);
            }
        }
    }

    /// <summary>A small pointed leaf: two triangles, tilted off its stem.</summary>
    public void Leaf(Vector3 at, Vector3 dir, Vector3 face, float length, float width, Vector4 root, Vector4 tip)
    {
        Begin(PartKind.Body);
        LeafCore(at, dir, face, length, width, root, tip);
        End();
    }

    void LeafCore(Vector3 at, Vector3 dir, Vector3 face, float length, float width, Vector4 root, Vector4 tip)
    {
        dir = Vector3.Normalize(dir);
        Vector3 side = Vector3.Cross(dir, face);
        side = side.LengthSquared() < 1e-4f ? Vector3.UnitX : Vector3.Normalize(side);
        Vector3 mid = at + dir * (length * 0.45f), end = at + dir * length;
        Vector3 up = Vector3.Cross(side, dir);
        Tri(at, mid + side * width * 0.5f, end, up, root, root, tip);
        Tri(at, mid - side * width * 0.5f, end, up, root, root, tip);
    }

    /// <summary>A small flower: a shallow five-sided cup, its colour running from a pale heart to the rim. Five triangles.</summary>
    public void Blossom(Vector3 at, Vector3 face, float radius, Vector4 petal, Vector4 heart)
    {
        Begin(PartKind.Body);
        BlossomCore(at, face, radius, petal, heart);
        End();
    }

    void BlossomCore(Vector3 at, Vector3 face, float radius, Vector4 petal, Vector4 heart)
    {
        face = Vector3.Normalize(face);
        Vector3 u = Perpendicular(face), v = Vector3.Cross(face, u);
        var rim = new Vector3[5];
        for (int k = 0; k < 5; k++)
        {
            float a = k * MathF.Tau / 5f;
            rim[k] = at + (u * MathF.Cos(a) + v * MathF.Sin(a)) * radius + face * radius * 0.3f;
        }
        for (int k = 0; k < 5; k++) Tri(at, rim[k], rim[(k + 1) % 5], face, heart, petal, petal);
    }

    /// <summary>A crystal: a faceted prism with a pointed tip, leaning along an axis, its foot sunk.</summary>
    public void Shard(Vector3 at, Vector3 axis, float radius, float length, int sides, Vector4 foot, Vector4 tip)
    {
        Begin(PartKind.Body);
        ShardCore(at, axis, radius, length, sides, foot, tip);
        End();
    }

    void ShardCore(Vector3 at, Vector3 axis, float radius, float length, int sides, Vector4 foot, Vector4 tip)
    {
        Vector3 d = Vector3.Normalize(axis);
        Tube(new[] { at - d * 1.2f, at + d * length * 0.72f, at + d * length }, new[] { radius, radius * 0.9f, 0f }, sides, foot, tip);
    }
}
