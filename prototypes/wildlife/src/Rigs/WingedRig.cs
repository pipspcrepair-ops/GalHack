using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>
/// Fliers: a body with one or two pairs of leathery wings, each an inner and an outer panel.
/// Wings flap with the outer panel lagging the inner (which is what makes a flap look like a
/// flap), glide between bursts, and fold back along the body when it lands to eat. The body
/// pitches with climb and banks into turns.
/// </summary>
public sealed class WingedRig : Rig
{
    readonly int _body, _head;
    readonly int[] _neck, _tail;
    readonly List<(int pair, int side, int inner, int outer)> _wings = new();
    readonly int[] _feet = new int[2];
    readonly float _span1, _span2;
    readonly bool _moth;
    float _phase, _flapBlend, _fold;

    public WingedRig(Species s) : base(s, Build)
    {
        var pieces = Layout.Pieces;
        _body = pieces.FindIndex(p => p.Name == "body");
        _head = pieces.FindIndex(p => p.Name == "head");
        _neck = pieces.FindAll(p => p.Name.StartsWith("neck")).ConvertAll(p => pieces.IndexOf(p)).ToArray();
        _tail = pieces.FindAll(p => p.Name.StartsWith("tail")).ConvertAll(p => pieces.IndexOf(p)).ToArray();
        int pairs = Math.Max(1, s.Body.Wings / 2);
        for (int pair = 0; pair < pairs; pair++)
        for (int side = 0; side < 2; side++)
            _wings.Add((pair, side, pieces.FindIndex(p => p.Name == $"wing{pair}{side}a"), pieces.FindIndex(p => p.Name == $"wing{pair}{side}b")));
        for (int side = 0; side < 2; side++) _feet[side] = pieces.FindIndex(p => p.Name == $"foot{side}");
        (_span1, _span2) = Spans(s);
        _moth = s.Body.Wings >= 4;
    }

    static (float, float) Spans(Species s) => (s.Body.Size * 0.62f, s.Body.Size * 0.72f);

    public override void Reset(WildSim w, Creature c)
    {
        ResetIntents(c);
        _fold = c.Landed ? 1f : 0f;
        Update(w, c, 0.016f);
    }

    public override void Update(WildSim w, Creature c, float dt)
    {
        if (dt <= 0f) return;
        SmoothIntents(c, dt);
        Proportions b = Species.Body;
        float L = b.Size, W = b.Width, H = b.Height;
        float ground = w.Terrain.Height(c.Position.X, c.Position.Z);

        _fold = Geo.Smooth(_fold, c.Landed || c.Dead ? 1f : 0f, 5f, dt);
        // Gliders flap in bursts and glide between them; moths never stop; climbing always flaps.
        float cycle = (c.Age + c.Id * 1.3f) % 5f;
        bool flapping = !c.Landed && !c.Dead && (_moth || c.Velocity.Y > 0.3f || cycle < 2.4f || Rearing > 0.1f);
        _flapBlend = Geo.Smooth(_flapBlend, flapping ? 1f : 0f, 4f, dt);
        float freq = Math.Clamp(3.4f / MathF.Sqrt(MathF.Max(0.3f, L)), 1.4f, 6f) * (_moth ? 1.6f : 1f);
        _phase += dt * freq * MathF.Tau * MathF.Max(0.15f, _flapBlend);

        Basis yaw = new(Vector3.Up, c.Yaw);
        float pitch = Math.Clamp(c.Velocity.Y * 0.08f, -0.45f, 0.45f) * (1f - _fold) + Rearing * 0.4f - Lunging * 0.5f;
        float roll = Math.Clamp(-YawRate * 0.4f, -0.7f, 0.7f) * (1f - _fold) + Dead * 1.3f;
        float bob = -MathF.Sin(_phase) * 0.05f * L * _flapBlend;
        float bodyY = c.Landed || c.Dead ? ground + H * 0.5f + W * 0.25f * (1f - Dead) : c.Position.Y + bob;
        Basis body = yaw * new Basis(Vector3.Right, pitch) * new Basis(Vector3.Back, roll);
        var bodyT = new Transform3D(body, new Vector3(c.Position.X, bodyY, c.Position.Z));
        Pose[_body] = bodyT;
        BodyClearance = bodyY - ground;

        float glideIn = 0.12f + 0.04f * MathF.Sin(c.Age * 2f), glideOut = 0.05f;
        foreach (var (pair, side, inner, outer) in _wings)
        {
            float s = side == 0 ? -1f : 1f;
            float ph = _phase - pair * 1.2f;
            float a1 = Mathf.Lerp(glideIn, 0.15f + 0.8f * MathF.Sin(ph), _flapBlend);
            float a2 = Mathf.Lerp(glideOut, 0.55f * MathF.Sin(ph - 0.9f), _flapBlend);
            float scale = pair == 0 ? 1f : 0.7f;
            Vector3 shoulder = new(s * W * 0.42f, H * 0.2f, pair == 0 ? -L * 0.08f : L * 0.18f);
            Vector3 d1 = new Vector3(s * MathF.Cos(a1), MathF.Sin(a1), 0f).Lerp(new Vector3(s * 0.35f, -0.25f, 0.9f).Normalized(), _fold).Normalized();
            Vector3 d2 = new Vector3(s * MathF.Cos(a1 + a2), MathF.Sin(a1 + a2), 0.08f).Lerp(new Vector3(-s * 0.05f, -0.15f, 1f).Normalized(), _fold).Normalized();
            Vector3 p0 = bodyT * shoulder;
            Vector3 w1 = body * d1, w2 = body * d2;
            Pose[inner] = new Transform3D(WingBasis(w1, body.Z), p0);
            Vector3 p1 = p0 + w1 * _span1 * scale;
            Pose[outer] = new Transform3D(WingBasis(w2, body.Z), p1);
        }

        // Head on a short neck, looking about; the tail streams behind.
        Vector3 p = bodyT * new Vector3(0, H * 0.1f, -L * 0.33f);
        float neckPitch = -0.05f + Graze * -0.8f + Rearing * 0.3f - Lunging * 0.5f;
        if (_neck.Length > 0)
        {
            float seg = b.NeckLength / _neck.Length;
            for (int i = 0; i < _neck.Length; i++)
            {
                Vector3 d = body * Geo.Forward(LookYaw * 0.5f, neckPitch + 0.3f);
                Pose[_neck[i]] = Geo.Segment(p, p + d * seg, body.X);
                p += d * seg;
            }
        }
        if (_head >= 0)
            Pose[_head] = new Transform3D(yaw * new Basis(Vector3.Up, LookYaw) * new Basis(Vector3.Right, neckPitch + pitch * 0.5f), p);

        if (_tail.Length > 0)
        {
            Vector3 q = bodyT * new Vector3(0, 0, L * 0.33f);
            float seg = b.TailLength / _tail.Length, yawAcc = 0f, pitchAcc = -0.1f - 0.5f * _fold;
            for (int i = 0; i < _tail.Length; i++)
            {
                yawAcc += MathF.Sin(c.Age * 1.8f - i * 0.9f) * 0.12f - YawRate * 0.08f;
                pitchAcc += MathF.Sin(_phase - i * 0.8f) * 0.06f * _flapBlend - 0.05f;
                Vector3 d = body * Geo.Backward(yawAcc, pitchAcc);
                Pose[_tail[i]] = Geo.Segment(q, q + d * seg, body.X);
                q += d * seg;
            }
        }

        for (int side = 0; side < 2; side++)
        {
            if (_feet[side] < 0) continue;
            float s = side == 0 ? -1f : 1f;
            Vector3 hip = bodyT * new Vector3(s * W * 0.2f, -H * 0.35f, L * 0.08f);
            Vector3 toe = c.Landed && !c.Dead ? new Vector3(hip.X, ground, hip.Z) : hip + body * new Vector3(0, -0.3f, 1f) * H;
            Pose[_feet[side]] = Geo.Segment(hip, toe, body.X, H * 0.8f);
        }

        SetHit(0, bodyT.Origin, MathF.Max(W, H) * 0.7f);
        int h = 1;
        if (_head >= 0) SetHit(h++, Pose[_head] * new Vector3(0, 0, -b.HeadSize * 0.4f), b.HeadSize * 0.6f);
        foreach (var (_, _, inner, _) in _wings)
            if (h < Hits.Length) SetHit(h++, Pose[inner] * new Vector3(0, _span1 * 0.5f, L * 0.15f), L * 0.3f);
        HitCount = h;
        SetBounds(bodyT.Origin, _span1 + _span2 + L * 0.5f);
    }

    /// <summary>Wing panels: Y along the span, Z kept towards the trailing edge (the body's back).</summary>
    static Basis WingBasis(Vector3 span, Vector3 back)
    {
        Vector3 y = span.Normalized();
        Vector3 z = back - y * back.Dot(y);
        if (z.LengthSquared() < 1e-6f) z = Vector3.Down - y * y.Y;
        z = z.Normalized();
        return new Basis(y.Cross(z), y, z);
    }

    // ---- Layout -----------------------------------------------------------------------

    static RigLayout Build(Species s)
    {
        var layout = new RigLayout();
        var k = new Paint(s);
        var rng = Rng.For(s.Seed, "layout");
        Proportions b = s.Body;
        float L = b.Size, W = b.Width, H = b.Height, hs = b.HeadSize;
        (float span1, float span2) = Spans(s);
        bool moth = b.Wings >= 4;

        var body = new Piece("body");
        body.Add(Shape.Sphere, new Vector3(W, H, L * 0.72f), Vector3.Zero, k.Base, 1f);
        body.Add(Shape.Sphere, new Vector3(W * 0.85f, H * 0.6f, L * 0.62f), new Vector3(0, -H * 0.18f, 0), k.Belly);
        if (moth) body.Add(Shape.Sphere, new Vector3(W * 1.1f, H * 0.9f, L * 0.3f), new Vector3(0, H * 0.05f, -L * 0.18f), k.Belly.Lerp(k.Base, 0.3f), 0f);
        Parts.Back(body, s.Part("back"), W, H, L * 0.72f, k, rng);
        if (s.Corrupted) Parts.Corruption(body, W, H, L * 0.72f, k, rng);
        layout.Add(body);

        var head = new Piece("head");
        Parts.Head(head, s.Part("head"), hs, k, rng);
        Parts.Horns(head, s.Part("horns"), hs, k, rng);
        if (s.Part("tendrils") != "none")
            for (int i = 0; i < 4; i++)
                head.Rod(new Vector3((i - 1.5f) * hs * 0.15f, -hs * 0.2f, -hs * 0.8f), new Vector3((i - 1.5f) * hs * 0.4f, -hs * 0.8f, -hs * 0.6f),
                    hs * 0.05f, s.Corrupted ? k.Veins : k.Accent, 0f, s.Corrupted ? 0.8f : 0f);
        layout.Add(head);

        if (b.NeckLength > L * 0.15f)
        {
            var neck = new Piece("neck0");
            float d = W * 0.4f;
            neck.Add(Shape.Capsule, new Vector3(d, b.NeckLength + d, d), new Vector3(0, b.NeckLength * 0.5f, 0), k.Base, 0.8f);
            layout.Add(neck);
        }

        int pairs = Math.Max(1, b.Wings / 2);
        Color membrane = moth ? k.Accent : k.Accent.Lerp(k.Base, 0.45f);
        for (int pair = 0; pair < pairs; pair++)
        for (int side = 0; side < 2; side++)
        {
            float scale = pair == 0 ? 1f : 0.7f;
            float a = span1 * scale, o = span2 * scale, chord = L * (moth ? 0.75f : 0.55f) * scale;
            var inner = new Piece($"wing{pair}{side}a");
            inner.Add(Shape.Capsule, new Vector3(W * 0.12f, a + W * 0.12f, W * 0.12f), new Vector3(0, a * 0.5f, 0), k.Dark);
            inner.Add(Shape.Sphere, new Vector3(W * 0.04f, a * 1.05f, chord), new Vector3(0, a * 0.5f, chord * 0.38f), membrane, 1f, k.GlowStrength * 0.2f);
            layout.Add(inner);
            var outer = new Piece($"wing{pair}{side}b") { Shadow = true };
            outer.Add(Shape.Capsule, new Vector3(W * 0.08f, o * 0.8f, W * 0.08f), new Vector3(0, o * 0.4f, 0), k.Dark);
            if (moth)
                outer.Add(Shape.Sphere, new Vector3(W * 0.04f, o, chord * 1.1f), new Vector3(0, o * 0.45f, chord * 0.3f), membrane.Lerp(k.Base, 0.2f), 1f, k.GlowStrength * 0.3f);
            else
            {
                outer.Add(Shape.Sphere, new Vector3(W * 0.04f, o, chord * 0.8f), new Vector3(0, o * 0.45f, chord * 0.28f), membrane, 1f, k.GlowStrength * 0.2f);
                for (int f = 0; f < 2; f++)
                    outer.Rod(new Vector3(0, o * 0.15f, 0), new Vector3(0, o * (0.55f + 0.35f * f), chord * (0.7f - 0.3f * f)), W * 0.04f, k.Dark);
            }
            layout.Add(outer);
        }

        if (b.TailLength > 0.05f)
        {
            int segs = 3;
            for (int i = 0; i < segs; i++)
            {
                var t = new Piece($"tail{i}") { Shadow = false };
                float seg = b.TailLength / segs, d = W * 0.22f * (1f - 0.6f * i / segs);
                t.Add(Shape.Capsule, new Vector3(d, seg + d, d), new Vector3(0, seg * 0.5f, 0), k.Base, 0.6f);
                if (i == segs - 1) Parts.TailTip(t, s.Part("tail"), seg, W * 0.9f, k);
                layout.Add(t);
            }
        }

        for (int side = 0; side < 2; side++)
        {
            var f = new Piece($"foot{side}") { Shadow = false };
            float len = H * 0.8f, d = W * 0.08f;
            f.Add(Shape.Capsule, new Vector3(d, len + d, d), new Vector3(0, len * 0.5f, 0), k.Dark);
            layout.Add(f);
        }
        return layout;
    }
}
