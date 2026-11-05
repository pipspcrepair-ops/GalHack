using System;
using Godot;

namespace Wildlife;

/// <summary>
/// Hull leeches: a lamprey-mouthed head on a chain of ringed segments that lies flat on the
/// hull it holds. Crawling runs a peristaltic wave down the body: segments bunch, hump and
/// stretch. The telegraph is the rear: the front half lifts off the hull in an arch and turns
/// to its target. In the lunge and while drifting back, the body trails behind the head,
/// swimming with a side-to-side wave. Forward kinematics from the creature's frame; the hull
/// face it clings to is its ground.
/// </summary>
public sealed class ClingRig : ZeroGRig
{
    readonly int _head, _tip;
    readonly int[] _segs;
    readonly int[][] _feelers;
    float _phase;

    public ClingRig(Species s) : base(s, Build)
    {
        var pieces = Layout.Pieces;
        _head = pieces.FindIndex(p => p.Name == "head");
        _tip = pieces.FindIndex(p => p.Name == "tip");
        int n = Segments(s);
        _segs = new int[n];
        for (int i = 0; i < n; i++) _segs[i] = pieces.FindIndex(p => p.Name == $"seg{i}");
        int feelers = s.Part("tendrils") == "feelers" ? 4 : 0;
        _feelers = new int[feelers][];
        for (int t = 0; t < feelers; t++) _feelers[t] = Chain(pieces, $"feel{t}_");
        _phase = s.Seed % 50 * 0.21f;
    }

    static int Segments(Species s) => Math.Max(4, s.Body.Segments);
    static float SegLength(Species s) => (s.Body.Size - s.Body.HeadSize) / Segments(s);
    static float Radius(Species s, int i) => s.Body.Width * 0.5f * (0.72f + 0.28f * MathF.Sin(MathF.PI * (i + 0.5f) / Segments(s)));

    protected override void Animate(Creature c, float dt)
    {
        Species s = Species;
        int n = _segs.Length;
        float seg = SegLength(s), r = s.Body.Width * 0.5f;
        Basis B = c.Facing;
        Vector3 F = -B.Z, U = B.Y, S = B.X, P = c.Position;
        float speed = c.Velocity.Length();
        _phase += dt * (0.9f + speed / MathF.Max(0.05f, seg) * 0.9f + Lunging * 4f) * (1f - 0.9f * Dead);

        Span<Vector3> joints = stackalloc Vector3[n + 1];
        if (c.Clinging && Lunging < 0.5f)
        {
            // Flat on the hull: a travelling hump runs down the body as it crawls.
            float moving = Math.Clamp(speed / MathF.Max(0.1f, s.Stats.WalkSpeed), 0.15f, 1f);
            float along = 0f;
            for (int i = 0; i <= n; i++)
            {
                if (i > 0) along += seg * (1f + 0.14f * moving * MathF.Sin(_phase - i * 0.9f));
                float hump = r * 0.6f * moving * Sq(MathF.Max(0f, MathF.Sin(_phase - i * 0.9f))) + Graze * r * 0.15f * MathF.Sin(c.Age * 6f + i);
                float sway = r * 0.35f * MathF.Sin(_phase * 0.5f - i * 0.7f);
                joints[i] = P - F * along + S * sway + U * (Radius(s, Math.Min(i, n - 1)) * 0.85f + hump);
            }
            // The rear: the front half arches up off the hull, head turning to its target.
            if (Rearing > 0.01f)
            {
                int pivot = Math.Max(2, n / 2);
                Vector3 d = (joints[pivot - 1] - joints[pivot]).Normalized();
                Vector3 axis = S.Rotated(U, LookYaw * 0.6f);
                float bend = Rearing * 1.35f / pivot;
                for (int j = pivot - 1; j >= 0; j--)
                {
                    d = d.Rotated(axis, bend).Normalized();
                    joints[j] = joints[j + 1] + d * seg;
                }
            }
        }
        else
        {
            // Adrift or lunging: the body trails straight behind the head, swimming.
            Vector3 back = c.Velocity.Length() > 0.5f ? -c.Velocity.Normalized() : -F;
            Vector3 side = back.Cross(U);
            side = side.LengthSquared() > 1e-4f ? side.Normalized() : S;
            joints[0] = P;
            for (int i = 1; i <= n; i++)
                joints[i] = joints[i - 1] + back * seg + side * MathF.Sin(_phase * 1.6f - i * 1.1f) * r * 0.9f * (1f - Lunging) * (1f - Dead);
        }

        for (int i = 0; i < n; i++) Pose[_segs[i]] = Along(joints[i], joints[i + 1], U);
        Vector3 ahead = (joints[0] - joints[1]).Normalized();
        Basis headBasis = SpaceSim.Look(ahead, U);
        if (_head >= 0) Pose[_head] = new Transform3D(headBasis, joints[0]);
        if (_tip >= 0) Pose[_tip] = Geo.Segment(joints[n - 1], joints[n], S);

        // Barbels round the mouth, groping forward while it hunts, trailing when it swims.
        for (int t = 0; t < _feelers.Length; t++)
        {
            float a = t * MathF.Tau / _feelers.Length + 0.4f;
            Vector3 from = joints[0] + headBasis * new Vector3(MathF.Cos(a) * s.Body.HeadSize * 0.35f, MathF.Sin(a) * s.Body.HeadSize * 0.3f, -s.Body.HeadSize * 0.5f);
            Vector3 reach = (ahead * (0.6f + Rearing) + headBasis * new Vector3(MathF.Cos(a), MathF.Sin(a), 0f) * 0.8f).Normalized();
            Trail(_feelers[t], from, reach, -c.Velocity * 0.05f, s.Body.HeadSize * 1.1f, c, headBasis, t, 0.35f);
        }

        SetHit(0, joints[0] + ahead * s.Body.HeadSize * 0.4f, s.Body.HeadSize * 0.6f);
        SetHit(1, Pose[_segs[n / 2]].Origin, r * 1.2f);
        SetHit(2, Pose[_segs[n - 1]].Origin, r);
        HitCount = 3;
        SetBounds(Pose[_segs[n / 2]].Origin, s.Body.Size * 0.6f);
        BodyClearance = c.ClingHull != null ? c.ClingHull.SignedDistance(Pose[_segs[n / 2]].Origin) : 0f;
    }

    static float Sq(float x) => x * x;

    static RigLayout Build(Species s)
    {
        var layout = new RigLayout();
        var k = new Paint(s);
        var rng = Rng.For(s.Seed, "layout");
        int n = Segments(s);
        float seg = SegLength(s);
        for (int i = 0; i < n; i++)
        {
            float r = Radius(s, i);
            var piece = new Piece($"seg{i}");
            var ring = LoftProfile.Smooth(-seg * 0.62f, seg * 0.62f, new[]
            {
                (0f, r * 0.86f, r * 0.72f, 0f), (0.5f, r, r * 0.84f, r * 0.04f), (1f, r * 0.86f, r * 0.72f, 0f),
            }, 8, 0.25f);
            piece.Loft(ring, k.Base, k.Belly, 1f, 0f, Mat.Carapace, null, null, 12);
            // A glowing band round every other ring: it pulses with the hump as it crawls.
            if (i % 2 == 1 && k.GlowStrength > 0.3f)
                piece.Add(Shape.Cylinder, new Vector3(r * 2.04f, seg * 0.1f, r * 1.72f), new Vector3(0, r * 0.04f, 0), k.Glow, 0f, k.GlowStrength * 0.9f, new Basis(Vector3.Right, MathF.PI * 0.5f), 12);
            // Hooked grips along the belly hold it to the hull.
            for (int side = -1; side <= 1; side += 2)
                piece.Horn(k.Horn, r * 0.12f, Mat.Horn, new Vector3(side * r * 0.7f, -r * 0.5f, 0f), new Vector3(side * r * 0.95f, -r * 0.8f, -seg * 0.1f), new Vector3(side * r * 1.0f, -r * 0.9f, -seg * 0.3f));
            if (i > 0 && i < n - 1) Parts.Back(piece, s.Part("back"), r * 2f, r * 1.7f, seg * 1.2f, k, rng, ring);
            layout.Add(piece);
        }

        var head = new Piece("head");
        Parts.Head(head, s.Part("head"), s.Body.HeadSize, k, rng);
        layout.Add(head);

        var tip = new Piece("tip") { Shadow = false };
        string tail = s.Part("tail");
        if (tail == "none") tip.Taper(new Vector3(0, seg * 0.5f, 0), new Vector3(0, seg * 1.3f, 0), Radius(s, n - 1) * 0.8f, s.Body.Width * 0.04f, k.Base, 0.6f);
        else Parts.TailTip(tip, tail, seg, s.Body.Width * 0.8f, k);
        layout.Add(tip);

        if (s.Part("tendrils") == "feelers")
            for (int t = 0; t < 4; t++)
                BuildTrail(layout, $"feel{t}_", 3, s.Body.HeadSize * 1.1f, s.Body.Width * 0.035f, k.Base.Lerp(k.Accent, 0.5f), k.Glow, k.GlowStrength * 0.6f);
        return layout;
    }
}
