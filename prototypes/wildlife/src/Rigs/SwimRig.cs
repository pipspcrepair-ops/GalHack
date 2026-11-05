using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>
/// A rig for zero-g bodies: there is no ground to stand on, so the body is posed from the
/// creature's full orientation (<see cref="Creature.Facing"/>), not from a yaw over terrain.
/// The world argument is unused (it is null in space).
/// </summary>
public abstract class ZeroGRig : Rig
{
    protected ZeroGRig(Species s, Func<Species, RigLayout> build) : base(s, build) { }

    public sealed override void Reset(WildSim w, Creature c)
    {
        ResetIntents(c);
        Animate(c, 0.016f);
    }

    public sealed override void Update(WildSim w, Creature c, float dt)
    {
        if (dt <= 0f) return;
        SmoothIntents(c, dt);
        Animate(c, dt);
    }

    protected abstract void Animate(Creature c, float dt);

    /// <summary>A fin or wing panel: Y along the span, Z kept towards the trailing edge.</summary>
    protected static Basis PanelBasis(Vector3 span, Vector3 back)
    {
        Vector3 y = span.Normalized();
        Vector3 z = back - y * back.Dot(y);
        if (z.LengthSquared() < 1e-6f) z = y.Cross(Vector3.Right);
        if (z.LengthSquared() < 1e-6f) z = y.Cross(Vector3.Up);
        z = z.Normalized();
        return new Basis(y.Cross(z), y, z);
    }

    /// <summary>A segment's pose from joint a to joint b: Z runs along it (backwards), Y stays towards up.</summary>
    protected static Transform3D Along(Vector3 a, Vector3 b, Vector3 up)
    {
        Vector3 z = b - a;
        if (z.LengthSquared() < 1e-8f) return new Transform3D(Basis.Identity, a);
        z = z.Normalized();
        Vector3 y = up - z * up.Dot(z);
        if (y.LengthSquared() < 1e-6f) y = z.Cross(Vector3.Right);
        y = y.Normalized();
        return new Transform3D(new Basis(y.Cross(z), y, z), (a + b) * 0.5f);
    }

    /// <summary>A trailing chain (tendril, streamer, ribbon) that streams behind with the motion and sways.</summary>
    protected void Trail(int[] chain, Vector3 from, Vector3 back, Vector3 drag, float length, Creature c, Basis body, float seed, float sway = 0.18f)
    {
        if (chain.Length == 0) return;
        float seg = length / chain.Length;
        Vector3 dir = back;
        Vector3 p = from;
        for (int i = 0; i < chain.Length; i++)
        {
            Vector3 wave = body.X * MathF.Sin(c.Age * 1.1f + seed * 1.7f + i * 0.6f) + body.Y * MathF.Cos(c.Age * 0.8f + seed * 2.3f + i * 0.5f);
            dir = (dir * 1.6f + back + drag * (i + 1) * 0.12f + wave * sway).Normalized();
            Vector3 next = p + dir * seg;
            Pose[chain[i]] = Geo.Segment(p, next, body.X, seg);
            p = next;
        }
    }

    protected static int[] Chain(List<Piece> pieces, string prefix) =>
        pieces.FindAll(p => p.Name.StartsWith(prefix)).ConvertAll(p => pieces.IndexOf(p)).ToArray();

    /// <summary>Builds a trailing chain's pieces: tapering, glowing at the tip if asked.</summary>
    protected static void BuildTrail(RigLayout layout, string prefix, int segs, float length, float radius, Color color, Color tip, float glow, float material = Mat.Skin)
    {
        float seg = length / segs;
        for (int i = 0; i < segs; i++)
        {
            var piece = new Piece($"{prefix}{i}") { Shadow = false };
            float r0 = radius * (1f - 0.7f * i / segs), r1 = radius * (1f - 0.7f * (i + 1) / segs);
            bool last = i == segs - 1;
            piece.Taper(Vector3.Zero, new Vector3(0, seg, 0), r0, r1, color.Lerp(tip, (float)i / segs * 0.4f), 0.3f, glow * (last ? 0.5f : 0.02f), material);
            if (last && glow > 0f) piece.Add(Shape.Sphere, Vector3.One * r1 * 3.5f, new Vector3(0, seg, 0), tip, 0f, 1f);
            layout.Add(piece);
        }
    }
}

/// <summary>
/// Zero-g swimmers, in three styles:
/// - Pulse: a bell that contracts and relaxes like a jellyfish, a rim of veil lobes that sweep
///   back on each beat, and streamers and filaments trailing behind.
/// - Flap: a ray or manta, wide wings flapping slowly up and down with the outer panel lagging
///   the inner, gliding between beats, banking into turns, a tail streaming behind.
/// - Undulate: a long segmented serpent (the leviathan) swimming by a wave that runs down its
///   body, pectoral paddles beating, the head rearing in its warning display.
/// All of it is forward kinematics from the body's frame, so nothing can tangle or blow up.
/// </summary>
public sealed class SwimRig : ZeroGRig
{
    readonly SwimStyle _style;
    readonly int _body = -1, _head = -1, _fluke = -1;
    readonly int[] _rims, _segs = Array.Empty<int>();
    readonly int[][] _streamers, _filaments, _tail;
    readonly List<(int pair, int side, int inner, int outer)> _wings = new();
    readonly float _streamerLength, _filamentLength;
    float _phase, _flap;

    public SwimRig(Species s) : base(s, Build)
    {
        _style = s.Swim;
        var pieces = Layout.Pieces;
        _body = pieces.FindIndex(p => p.Name == "body");
        _head = pieces.FindIndex(p => p.Name == "head");
        _fluke = pieces.FindIndex(p => p.Name == "fluke");
        _rims = pieces.FindAll(p => p.Name.StartsWith("rim")).ConvertAll(p => pieces.IndexOf(p)).ToArray();
        if (_style == SwimStyle.Undulate) _segs = SegmentIndices(pieces);
        _streamers = Chains(pieces, "streamer", StreamerCount(s));
        _filaments = Chains(pieces, "fil", FilamentCount(s));
        _tail = Chains(pieces, "tail", 1 + (s.Part("tail") == "streamers" && _style == SwimStyle.Flap ? 2 : 0));
        foreach (Piece p in pieces)
            if (p.Name.StartsWith("wing") && p.Name.EndsWith("a"))
            {
                int pair = p.Name[4] - '0', side = p.Name[5] - '0';
                _wings.Add((pair, side, pieces.IndexOf(p), pieces.FindIndex(q => q.Name == $"wing{pair}{side}b")));
            }
        _streamerLength = s.Body.TailLength;
        _filamentLength = s.Body.TailLength * (s.Part("tendrils") switch { "curtain" => 0.45f, "glowing" => 0.85f, _ => 0.6f });
        _phase = s.Seed % 100 * 0.37f;
    }

    static int[] SegmentIndices(List<Piece> pieces)
    {
        var list = new List<int>();
        for (int i = 0; ; i++)
        {
            int k = pieces.FindIndex(p => p.Name == $"seg{i}");
            if (k < 0) break;
            list.Add(k);
        }
        return list.ToArray();
    }

    static int[][] Chains(List<Piece> pieces, string prefix, int count)
    {
        var chains = new int[count][];
        for (int t = 0; t < count; t++) chains[t] = Chain(pieces, $"{prefix}{t}_");
        return chains;
    }

    // Trailing chains are a piece per segment and so a draw call each: kept to a few dozen per body.
    static int StreamerCount(Species s) => s.Swim == SwimStyle.Pulse ? (s.Part("tail") == "streamers" ? 7 : 4) : 0;
    const int StreamerSegments = 4;

    static int FilamentCount(Species s) => s.Part("tendrils") switch
    {
        "filaments" => s.Swim == SwimStyle.Pulse ? 8 : 6,
        "curtain" => 11,
        "glowing" => s.Swim == SwimStyle.Undulate ? 6 : 6,
        "feelers" => 4,
        _ => 0,
    };

    static int FilamentSegments(Species s) => s.Part("tendrils") == "glowing" ? 3 : 2;

    protected override void Animate(Creature c, float dt)
    {
        switch (_style)
        {
            case SwimStyle.Pulse: Pulse(c, dt); break;
            case SwimStyle.Flap: Flap(c, dt); break;
            default: Undulate(c, dt); break;
        }
    }

    // ---- Pulse: the veil drifter ------------------------------------------------------

    void Pulse(Creature c, float dt)
    {
        Proportions b = Species.Body;
        float L = b.Size, W = b.Width, H = b.Height;
        Basis B = c.Facing;
        Vector3 P = c.Position;
        float speed = c.Velocity.Length();
        float freq = (0.32f + 0.07f * speed) / MathF.Sqrt(MathF.Max(0.5f, L / 5f));
        _phase += dt * freq * (c.Dead ? 0.15f : 1f);
        float s = _phase - MathF.Floor(_phase);
        float beat = s < 0.22f ? Smooth(s / 0.22f) : 1f - Smooth((s - 0.22f) / 0.78f);
        beat = MathF.Max(beat, Rearing) * (1f - Dead);
        float squeeze = 1f - 0.16f * beat;

        Pose[_body] = new Transform3D(B * Basis.FromScale(new Vector3(squeeze, squeeze, 1f + 0.1f * beat)), P);
        if (_head >= 0)
            Pose[_head] = new Transform3D(B * new Basis(Vector3.Up, LookYaw * 0.3f) * new Basis(Vector3.Right, -0.35f + Rearing * 0.5f),
                P + B * new Vector3(0, -H * 0.28f * squeeze, -L * 0.3f));

        // The veil: lobes round the rim, flared out when relaxed, swept back on the beat, thrown
        // forward and open when it rears to sting.
        float rx = 0.44f * W * squeeze, ry = 0.44f * H * squeeze, rz = L * 0.08f * (1f + 0.1f * beat);
        float lobe = W * 0.42f;
        for (int i = 0; i < _rims.Length; i++)
        {
            float a = i * MathF.Tau / _rims.Length;
            Vector3 radial = new(MathF.Cos(a), MathF.Sin(a), 0f);
            Vector3 at = P + B * new Vector3(rx * radial.X, ry * radial.Y, rz);
            float flare = Mathf.Lerp(1.05f, 0.4f, beat) + Rearing * 0.9f - Dead * 0.6f + 0.06f * MathF.Sin(c.Age * 2f + i);
            Vector3 dir = B * (radial * MathF.Sin(flare) + Vector3.Back * MathF.Cos(flare));
            Pose[_rims[i]] = Geo.Segment(at, at + dir * lobe, B * radial, lobe);
        }

        Vector3 drag = -c.Velocity * 0.06f;
        for (int t = 0; t < _streamers.Length; t++)
        {
            float a = t * MathF.Tau / _streamers.Length + 0.3f;
            Vector3 from = P + B * new Vector3(MathF.Cos(a) * rx * 0.8f, MathF.Sin(a) * ry * 0.8f, rz);
            Trail(_streamers[t], from, B.Z, drag, _streamerLength * (1f - 0.3f * Dead), c, B, t, 0.22f);
        }
        for (int t = 0; t < _filaments.Length; t++)
        {
            float a = t * MathF.Tau / MathF.Max(1, _filaments.Length) + 0.1f;
            Vector3 from = P + B * new Vector3(MathF.Cos(a) * rx * 0.5f, MathF.Sin(a) * ry * 0.5f, rz * 0.5f);
            Trail(_filaments[t], from, B.Z, drag, _filamentLength, c, B, t + 10, 0.3f);
        }

        SetHit(0, P, MathF.Max(W, H) * 0.5f * squeeze);
        SetHit(1, P + B * new Vector3(0, 0, _streamerLength * 0.4f), MathF.Max(W, H) * 0.35f);
        HitCount = 2;
        SetBounds(P + B * new Vector3(0, 0, _streamerLength * 0.3f), L * 0.6f + _streamerLength * 0.55f);
        BodyClearance = 0f;
    }

    // ---- Flap: rays and mantas --------------------------------------------------------

    void Flap(Creature c, float dt)
    {
        Proportions b = Species.Body;
        float L = b.Size, W = b.Width;
        Basis B = c.Facing;
        float speed = c.Velocity.Length();
        // Big wings beat slowly; mantas glide a lot between beats, rays rather less.
        float cycle = (c.Age + c.Id * 1.7f) % 9f;
        bool beating = !c.Dead && (speed > Species.Stats.WalkSpeed * 0.9f || cycle < 5.5f || Rearing > 0.1f || Lunging > 0.1f);
        _flap = Geo.Smooth(_flap, beating ? 1f : 0f, 1.5f, dt);
        float freq = Math.Clamp(1.4f / MathF.Sqrt(MathF.Max(0.5f, L)), 0.12f, 0.9f);
        _phase += dt * freq * MathF.Tau * MathF.Max(0.2f, _flap);

        float bob = -MathF.Sin(_phase) * 0.035f * L * _flap;
        Basis Bb = B * new Basis(Vector3.Right, Rearing * 0.45f - Lunging * 0.25f - Dead * 0.6f);
        Vector3 P = c.Position + B.Y * bob;
        Pose[_body] = new Transform3D(Bb, P);
        if (_head >= 0)
            Pose[_head] = new Transform3D(Bb * new Basis(Vector3.Up, LookYaw * 0.35f) * new Basis(Vector3.Right, -0.08f + Rearing * 0.25f), P + Bb * new Vector3(0, 0, -L * 0.36f));

        float span1 = W * 0.6f, span2 = W * 0.55f;
        foreach (var (_, side, inner, outer) in _wings)
        {
            float sgn = side == 0 ? -1f : 1f;
            float a1 = Mathf.Lerp(0.06f, 0.08f + 0.62f * MathF.Sin(_phase), _flap) + Rearing * 0.85f - Dead * 0.5f;
            float a2 = Mathf.Lerp(0.03f, 0.5f * MathF.Sin(_phase - 0.9f), _flap) + Rearing * 0.3f;
            float sweep = Lunging * 0.35f;
            Vector3 shoulder = P + Bb * new Vector3(sgn * W * 0.3f, 0f, -L * 0.04f);
            Vector3 d1 = Bb * new Vector3(sgn * MathF.Cos(a1), MathF.Sin(a1), sweep).Normalized();
            Vector3 d2 = Bb * new Vector3(sgn * MathF.Cos(a1 + a2), MathF.Sin(a1 + a2), 0.12f + sweep).Normalized();
            Pose[inner] = new Transform3D(PanelBasis(d1, Bb.Z), shoulder);
            Pose[outer] = new Transform3D(PanelBasis(d2, Bb.Z), shoulder + d1 * span1);
        }

        // The tail streams behind, rippling with the beat.
        Vector3 q = P + Bb * new Vector3(0, 0, L * 0.3f);
        for (int t = 0; t < _tail.Length; t++)
        {
            int[] chain = _tail[t];
            if (chain.Length == 0) continue;
            float seg = b.TailLength * (t == 0 ? 1f : 1.6f) / chain.Length, yaw = (t - 1) * 0.35f * (t == 0 ? 0f : 1f), pitch = 0f;
            Vector3 p = q + Bb.X * (t == 0 ? 0f : (t == 1 ? -1f : 1f) * W * 0.12f);
            for (int i = 0; i < chain.Length; i++)
            {
                yaw += MathF.Sin(c.Age * 1.5f - i * 0.8f + t) * 0.07f - YawRate * 0.05f;
                pitch += MathF.Sin(_phase - i * 0.7f) * 0.1f * _flap;
                Vector3 d = Bb * Geo.Backward(yaw, pitch);
                Pose[chain[i]] = Geo.Segment(p, p + d * seg, Bb.X);
                p += d * seg;
            }
            if (t == 0 && _fluke >= 0) Pose[_fluke] = Pose[chain[^1]]; // the tip is built at the end of the last bone
        }
        Vector3 drag = -c.Velocity * 0.05f;
        for (int t = 0; t < _filaments.Length; t++)
        {
            float x = (t / MathF.Max(1f, _filaments.Length - 1) - 0.5f) * W * 0.5f;
            Trail(_filaments[t], P + Bb * new Vector3(x, -b.Height * 0.4f, L * 0.05f), (Bb.Z * 0.8f - Bb.Y * 0.2f).Normalized(), drag, _filamentLength, c, Bb, t, 0.2f);
        }

        int h = 0;
        SetHit(h++, P, MathF.Max(W * 0.35f, b.Height));
        if (_head >= 0) SetHit(h++, Pose[_head].Origin, b.HeadSize * 0.6f);
        foreach (var (_, _, inner, outer) in _wings)
        {
            if (h < Hits.Length) SetHit(h++, Pose[inner] * new Vector3(0, span1 * 0.55f, L * 0.15f), span1 * 0.45f);
            if (h < Hits.Length) SetHit(h++, Pose[outer] * new Vector3(0, span2 * 0.45f, L * 0.1f), span2 * 0.4f);
        }
        HitCount = h;
        SetBounds(P, span1 + span2 + W * 0.3f + b.TailLength * 0.5f);
        BodyClearance = 0f;
    }

    // ---- Undulate: the void leviathan -------------------------------------------------

    void Undulate(Creature c, float dt)
    {
        Proportions b = Species.Body;
        float L = b.Size, W = b.Width, hs = b.HeadSize;
        int n = _segs.Length;
        float segLen = SegmentLength(Species);
        Basis B = c.Facing;
        Vector3 P = c.Position;
        float speed = c.Velocity.Length() / MathF.Max(1f, Species.Stats.WalkSpeed);
        _phase += dt * MathF.Tau * (0.08f + 0.09f * MathF.Min(speed, 3f) + 0.25f * Lunging) * (1f - 0.8f * Dead);

        Span<Vector3> joints = stackalloc Vector3[n + 1];
        for (int i = 0; i <= n; i++)
        {
            float u = (float)i / n;
            float amp = W * (0.2f + 1.1f * MathF.Pow(u, 1.3f)) * (1f + 0.6f * Lunging) * (1f - 0.7f * Bracing);
            // The wave runs in a tilted plane, so it reads from the side as well as from above.
            float wave = amp * MathF.Sin(_phase - i * 0.55f);
            float x = wave * 0.8f + Bracing * W * 1.6f * MathF.Sin(i * 0.75f) * (1f - u);
            float y = wave * 0.6f + amp * 0.15f * MathF.Sin(_phase * 0.5f - i * 0.4f) + Rearing * W * 2.4f * Sq(MathF.Max(0f, 1f - u / 0.45f));
            float z = -L * 0.5f + hs * 1.1f + i * segLen * (1f - 0.18f * Bracing * (1f - u));
            joints[i] = P + B * new Vector3(x, y - Dead * W * 0.3f * MathF.Sin(i * 0.9f), z);
        }
        for (int i = 0; i < n; i++) Pose[_segs[i]] = Along(joints[i], joints[i + 1], B.Y);

        Vector3 ahead = (joints[0] - joints[1]).Normalized();
        Basis headBasis = SpaceSim.Look(ahead, B.Y) * new Basis(Vector3.Up, LookYaw * 0.25f) * new Basis(Vector3.Right, Rearing * 0.45f - Bracing * 0.15f);
        if (_head >= 0) Pose[_head] = new Transform3D(headBasis, joints[0]);

        // Pectoral paddles on the first segment and half way down, beating as it swims.
        foreach (var (pair, side, inner, outer) in _wings)
        {
            int at = pair == 0 ? 1 : n / 2;
            Transform3D seg = Pose[_segs[Math.Min(at, n - 1)]];
            float sgn = side == 0 ? -1f : 1f, r = SegmentRadius(Species, at);
            float beat = 0.35f * MathF.Sin(_phase * 2f + pair * 1.3f + side * 0.4f) + Rearing * 0.5f;
            Vector3 root = seg.Origin + seg.Basis.X * sgn * r * 0.85f;
            Vector3 d1 = (seg.Basis.X * sgn * MathF.Cos(beat) + seg.Basis.Y * MathF.Sin(beat) + seg.Basis.Z * 0.35f).Normalized();
            Pose[inner] = new Transform3D(PanelBasis(d1, seg.Basis.Z), root);
            if (outer >= 0) Pose[outer] = new Transform3D(PanelBasis((d1 + seg.Basis.Z * 0.5f).Normalized(), seg.Basis.Z), root + d1 * W * (pair == 0 ? 0.9f : 0.6f));
        }
        if (_fluke >= 0) Pose[_fluke] = Geo.Segment(joints[n - 1], joints[n], B.X); // the tip is built at the end of the last bone

        Vector3 drag = -c.Velocity * 0.02f;
        for (int t = 0; t < _filaments.Length; t++)
        {
            float a = t * MathF.Tau / MathF.Max(1, _filaments.Length);
            Vector3 from = joints[0] + headBasis * new Vector3(MathF.Cos(a) * hs * 0.3f, -hs * 0.25f + MathF.Sin(a) * hs * 0.15f, -hs * 0.2f);
            Trail(_filaments[t], from, (-ahead - headBasis.Y * 0.2f).Normalized(), drag, _filamentLength, c, headBasis, t, 0.25f);
        }

        int h = 0;
        if (_head >= 0) SetHit(h++, joints[0] + ahead * hs * 0.4f, hs * 0.7f);
        int stride = Math.Max(1, n / (Hits.Length - 2));
        for (int i = 0; i < n && h < Hits.Length; i += stride) SetHit(h++, Pose[_segs[i]].Origin, SegmentRadius(Species, i) * 1.05f);
        HitCount = h;
        SetBounds(P, L * 0.62f);
        BodyClearance = 0f;
    }

    static float SegmentLength(Species s) => (s.Body.Size - s.Body.HeadSize * 1.1f) / Math.Max(1, s.Body.Segments);

    static float SegmentRadius(Species s, int i) => s.Body.Width * 0.5f * (1f - 0.7f * i / Math.Max(1f, s.Body.Segments));

    static float Smooth(float t) => t * t * (3f - 2f * t);

    static float Sq(float x) => x * x;

    // ---- Layout -----------------------------------------------------------------------

    static RigLayout Build(Species s)
    {
        var layout = new RigLayout();
        var k = new Paint(s);
        var rng = Rng.For(s.Seed, "layout");
        switch (s.Swim)
        {
            case SwimStyle.Pulse: BuildBell(layout, s, k, rng); break;
            case SwimStyle.Flap: BuildRay(layout, s, k, rng); break;
            default: BuildSerpent(layout, s, k, rng); break;
        }
        return layout;
    }

    /// <summary>A veil drifter: a domed bell lit from within, a ring of lamps at the rim, veil lobes, streamers and filaments.</summary>
    static void BuildBell(RigLayout layout, Species s, Paint k, Rng rng)
    {
        Proportions b = s.Body;
        float L = b.Size, W = b.Width, H = b.Height;
        var bell = new Piece("body");
        var mantle = LoftProfile.Smooth(-L * 0.45f, L * 0.1f, new[]
        {
            (0f, W * 0.2f, H * 0.2f, 0f), (0.4f, W * 0.46f, H * 0.46f, 0f), (0.8f, W * 0.5f, H * 0.5f, 0f), (1f, W * 0.43f, H * 0.43f, 0f),
        }, 14, 0.24f);
        bell.Loft(mantle, k.Base, k.Belly, 1f, 0f, Mat.Membrane, null, null, 16);
        // A glowing organ cluster inside the crown and lamps round the rim.
        bell.Add(Shape.Sphere, new Vector3(W * 0.36f, H * 0.36f, L * 0.22f), new Vector3(0, 0, -L * 0.2f), k.Glow, 0f, 0.9f * k.GlowStrength, null, 12);
        for (int i = 0; i < 12; i++)
        {
            float a = i * MathF.Tau / 12f;
            bell.Add(Shape.Sphere, Vector3.One * W * 0.045f, new Vector3(MathF.Cos(a) * W * 0.45f, MathF.Sin(a) * H * 0.45f, L * 0.06f), k.Glow, 0f, 1f, null, 8);
        }
        Parts.Back(bell, s.Part("back"), W, H, L * 0.55f, k, rng, mantle);
        layout.Add(bell);

        var head = new Piece("head");
        Parts.Head(head, s.Part("head"), b.HeadSize, k, rng);
        layout.Add(head);

        Color veil = k.Accent.Lerp(k.Glow, 0.3f);
        for (int i = 0; i < 8; i++)
        {
            var lobe = new Piece($"rim{i}") { Shadow = false };
            float len = W * 0.42f;
            lobe.Add(Shape.Sphere, new Vector3(W * 0.025f, len, W * 0.24f), new Vector3(0, len * 0.5f, 0), veil, 0.6f, 0.02f, null, 12, Mat.Membrane);
            lobe.Taper(Vector3.Zero, new Vector3(0, len * 0.95f, 0), W * 0.012f, W * 0.004f, k.Glow, 0f, 0.8f);
            layout.Add(lobe);
        }
        for (int t = 0; t < StreamerCount(s); t++)
            BuildTrail(layout, $"streamer{t}_", StreamerSegments, b.TailLength, W * 0.03f, k.Accent, k.Glow, 0.7f, Mat.Membrane);
        BuildFilaments(layout, s, k);
    }

    /// <summary>A ray or manta: a flat, deep-keeled disc of a body, wide two-panel wings, a head at the front and a long tail.</summary>
    static void BuildRay(RigLayout layout, Species s, Paint k, Rng rng)
    {
        Proportions b = s.Body;
        float L = b.Size, W = b.Width, H = b.Height;
        var body = new Piece("body");
        var disc = LoftProfile.Smooth(-L * 0.42f, L * 0.32f, new[]
        {
            (0f, W * 0.2f, H * 0.35f, 0f), (0.25f, W * 0.4f, H * 0.5f, H * 0.05f), (0.6f, W * 0.34f, H * 0.45f, 0f), (1f, W * 0.08f, H * 0.25f, 0f),
        }, 14, 0.2f);
        body.Loft(disc, k.Base, k.Belly, 1f, 0f, Mat.Skin, null, null, 14);
        // Gill slits along the belly, glowing faintly.
        for (int i = 0; i < 5; i++)
            for (int side = -1; side <= 1; side += 2)
                body.Add(Shape.Sphere, new Vector3(W * 0.05f, H * 0.04f, L * 0.012f), new Vector3(side * W * 0.12f, -H * 0.42f, -L * 0.12f + i * L * 0.035f), k.Glow, 0f, k.GlowStrength * 0.8f, null, 6);
        Parts.Back(body, s.Part("back"), W * 0.7f, H, L * 0.72f, k, rng, disc);
        layout.Add(body);

        var head = new Piece("head");
        Parts.Head(head, s.Part("head"), b.HeadSize, k, rng);
        layout.Add(head);

        // Wings are broad and triangular, rooted along most of the body like a manta's, the
        // leading edge swept back: each panel is a flat loft along its span, its chord shrinking
        // from root to tip, so inner and outer panels meet chord to chord at the joint.
        float span1 = W * 0.6f, span2 = W * 0.55f;
        float rootChord = L * 0.85f, jointChord = L * 0.4f, line = L * 0.36f, thick = W * 0.035f;
        bool vanes = s.Part("fins") == "vanes";
        Color membrane = k.Base.Lerp(k.Accent, 0.35f);
        var spanwise = new Basis(new Vector3(0, 0, 1), new Vector3(1, 0, 0), new Vector3(0, 1, 0)); // loft X to chord, Y to thickness, Z to span
        for (int side = 0; side < 2; side++)
        {
            float bone = W * 0.045f;
            var inner = new Piece($"wing0{side}a");
            inner.Taper(Vector3.Zero, new Vector3(0, span1, line - jointChord * 0.5f), bone * 1.3f, bone, k.Dark, 0.3f);
            var innerSkin = LoftProfile.Smooth(-span1 * 0.04f, span1 * 1.02f, new[]
            {
                (0f, rootChord * 0.5f, thick, 0f), (0.55f, (rootChord + jointChord) * 0.27f, thick * 0.8f, 0f), (1f, jointChord * 0.5f, thick * 0.6f, 0f),
            }, 8, 0.06f);
            inner.Loft(innerSkin, membrane, membrane.Darkened(0.2f), 1f, 0f, Mat.Membrane, new Vector3(0, 0, line), spanwise, 10);
            var outer = new Piece($"wing0{side}b");
            outer.Taper(Vector3.Zero, new Vector3(0, span2 * 0.95f, jointChord * 0.45f), bone, bone * 0.25f, k.Dark, 0.3f);
            var outerSkin = LoftProfile.Smooth(-span2 * 0.04f, span2, new[]
            {
                (0f, jointChord * 0.5f, thick * 0.6f, 0f), (0.6f, jointChord * 0.26f, thick * 0.45f, 0f), (1f, jointChord * 0.06f, thick * 0.3f, 0f),
            }, 8, 0.12f);
            outer.Loft(outerSkin, membrane, membrane.Darkened(0.2f), 1f, 0f, Mat.Membrane, new Vector3(0, 0, line), spanwise, 10);
            // Chord at a point along a panel: the leading and trailing edges there.
            float Lead(float u, float c0, float c1) => line - Mathf.Lerp(c0, c1, u) * 0.5f;
            float Trail(float u, float c0, float c1) => line + Mathf.Lerp(c0, c1, u) * 0.5f;
            if (vanes)
            {
                // Ribbed vanes: bony rays through the membrane, jutting past the trailing edge.
                for (int f = 0; f < 4; f++)
                {
                    float u = 0.12f + 0.24f * f;
                    inner.Taper(new Vector3(0, span1 * u, Lead(u, rootChord, jointChord) + bone), new Vector3(0, span1 * (u + 0.1f), Trail(u, rootChord, jointChord) * 1.12f), bone * 0.5f, bone * 0.08f, k.Horn, 0f, 0f, Mat.Horn, 6);
                    outer.Taper(new Vector3(0, span2 * u, Lead(u, jointChord, 0.05f * L) + bone), new Vector3(0, span2 * (u + 0.1f), Trail(u, jointChord, 0.05f * L) * 1.15f), bone * 0.4f, bone * 0.06f, k.Horn, 0f, 0f, Mat.Horn, 6);
                }
            }
            // A row of lamps along the leading edge.
            for (int l = 0; l < 4; l++)
            {
                float u = 0.2f + 0.2f * l;
                inner.Add(Shape.Sphere, Vector3.One * bone * 0.7f, new Vector3(0, span1 * u, Lead(u, rootChord, jointChord) + bone), k.Glow, 0f, k.GlowStrength, null, 6);
            }
            layout.Add(inner);
            layout.Add(outer);
        }

        int tails = s.Part("tail") == "streamers" ? 3 : 1;
        for (int t = 0; t < tails; t++)
        {
            int segs = 5;
            float len = b.TailLength * (t == 0 ? 1f : 1.6f), seg = len / segs;
            for (int i = 0; i < segs; i++)
            {
                var piece = new Piece($"tail{t}_{i}") { Shadow = false };
                float r = W * (t == 0 ? 0.05f : 0.018f);
                piece.Taper(Vector3.Zero, new Vector3(0, seg, 0), r * (1f - 0.75f * i / segs), r * (1f - 0.75f * (i + 1) / segs), t == 0 ? k.Base : k.Accent.Lerp(k.Glow, 0.4f), 0.5f, t == 0 ? 0f : 0.5f);
                layout.Add(piece);
            }
        }
        if (s.Part("tail") is "fin" or "twin" or "stinger")
        {
            var fluke = new Piece("fluke") { Shadow = false };
            Fluke(fluke, s.Part("tail"), b.TailLength / 5f, W * 0.2f, k);
            layout.Add(fluke);
        }
        BuildFilaments(layout, s, k);
    }

    /// <summary>A tail tip at the end of the last bone: the planet library's, plus a forked fluke for space swimmers.</summary>
    static void Fluke(Piece p, string kind, float segLen, float W, Paint k)
    {
        if (kind == "fin")
        {
            // A flat crescent fluke, spread sideways like a whale's, ribbed with bone.
            p.Add(Shape.Sphere, new Vector3(W * 1.7f, W * 0.55f, W * 0.05f), new Vector3(0, segLen + W * 0.15f, 0), k.Accent.Lerp(k.Base, 0.3f), 1f, 0f, null, 14, Mat.Membrane);
            for (int i = -2; i <= 2; i++)
                p.Taper(new Vector3(0, segLen, 0), new Vector3(i * W * 0.38f, segLen + W * (0.4f - 0.06f * MathF.Abs(i)), 0), W * 0.04f, W * 0.008f, k.Dark, 0f, 0f, Mat.Horn, 5);
            return;
        }
        if (kind != "twin")
        {
            Parts.TailTip(p, kind, segLen, W, k);
            return;
        }
        for (int side = -1; side <= 1; side += 2)
        {
            var lobe = new Basis(Vector3.Back, side * 0.55f);
            p.Add(Shape.Sphere, new Vector3(W * 0.06f, W * 1.5f, W * 0.55f), new Vector3(0, segLen, 0) + lobe * new Vector3(0, W * 0.7f, 0), k.Accent, 1f, k.GlowStrength * 0.4f, lobe, 12, Mat.Membrane);
            p.Taper(new Vector3(0, segLen, 0), new Vector3(0, segLen, 0) + lobe * new Vector3(0, W * 1.45f, 0), W * 0.05f, W * 0.008f, k.Dark, 0f, 0f, Mat.Horn, 6);
        }
    }

    /// <summary>The leviathan: armoured segments tapering to a fluke, glowing seams down each flank, a great head and paddles.</summary>
    static void BuildSerpent(RigLayout layout, Species s, Paint k, Rng rng)
    {
        Proportions b = s.Body;
        int n = Math.Max(4, b.Segments);
        float segLen = SegmentLength(s);
        for (int i = 0; i < n; i++)
        {
            float r0 = SegmentRadius(s, i), r1 = SegmentRadius(s, i + 1), mid = (r0 + r1) * 0.53f;
            var seg = new Piece($"seg{i}");
            var shell = LoftProfile.Smooth(-segLen * 0.62f, segLen * 0.62f, new[]
            {
                (0f, r0, r0 * 0.88f, 0f), (0.5f, mid, mid * 0.92f, mid * 0.04f), (1f, r1, r1 * 0.88f, 0f),
            }, 10, 0.16f);
            seg.Loft(shell, k.Base, k.Belly, 1f, 0f, Mat.Carapace, null, null, 14);
            if (k.GlowStrength > 0.3f)
                for (int side = -1; side <= 1; side += 2)
                    seg.Add(Shape.Sphere, new Vector3(mid * 0.14f, mid * 0.07f, segLen * 0.95f), new Vector3(side * mid * 0.86f, -mid * 0.12f, 0f), k.Glow, 0f, k.GlowStrength, null, 8);
            if (i % 2 == 0 && i < n - 2) Parts.Back(seg, s.Part("back"), mid * 2f, mid * 1.8f, segLen * 1.1f, k, rng, shell);
            layout.Add(seg);
        }

        var head = new Piece("head");
        Parts.Head(head, s.Part("head"), b.HeadSize, k, rng);
        layout.Add(head);

        string fins = s.Part("fins");
        if (fins is "paddles" or "vanes")
            for (int pair = 0; pair < 2; pair++)
                for (int side = 0; side < 2; side++)
                {
                    float span = b.Width * (pair == 0 ? 0.9f : 0.6f), chord = segLen * (pair == 0 ? 1.3f : 0.9f), bone = b.Width * 0.05f;
                    var fin = new Piece($"wing{pair}{side}a");
                    fin.Taper(Vector3.Zero, new Vector3(0, span, chord * 0.1f), bone * 1.4f, bone * 0.3f, k.Dark, 0.2f);
                    fin.Add(Shape.Sphere, new Vector3(bone * 0.6f, span * 1.05f, chord), new Vector3(0, span * 0.5f, chord * 0.35f), k.Accent.Lerp(k.Base, 0.3f), 1f, k.GlowStrength * 0.2f, null, 12, Mat.Membrane);
                    if (fins == "vanes")
                        for (int f = 0; f < 3; f++)
                            fin.Taper(new Vector3(0, span * (0.2f + 0.25f * f), 0), new Vector3(0, span * (0.35f + 0.25f * f), chord * 1.05f), bone * 0.6f, bone * 0.1f, k.Horn, 0f, 0f, Mat.Horn, 6);
                    layout.Add(fin);
                }

        var fluke = new Piece("fluke");
        string tail = s.Part("tail");
        if (tail == "none") fluke.Taper(new Vector3(0, segLen * 0.9f, 0), new Vector3(0, segLen * 1.8f, 0), SegmentRadius(s, n), b.Width * 0.02f, k.Base, 0.6f);
        else Fluke(fluke, tail, segLen, b.Width * 1.1f, k);
        layout.Add(fluke);
        BuildFilaments(layout, s, k);
    }

    static void BuildFilaments(RigLayout layout, Species s, Paint k)
    {
        int count = FilamentCount(s);
        if (count == 0) return;
        string kind = s.Part("tendrils");
        bool glowing = kind == "glowing";
        float length = s.Body.TailLength * (kind switch { "curtain" => 0.45f, "glowing" => 0.85f, _ => 0.6f });
        if (s.Swim == SwimStyle.Undulate) length = s.Body.HeadSize * (glowing ? 4f : 1.5f);
        float radius = s.Body.Width * (kind == "curtain" ? 0.012f : 0.009f) * (s.Swim == SwimStyle.Undulate ? 3f : 1f);
        for (int t = 0; t < count; t++)
            BuildTrail(layout, $"fil{t}_", FilamentSegments(s), length, radius, k.Base.Lerp(k.Accent, 0.5f), glowing ? k.Glow : k.Accent, glowing ? 0.9f : k.GlowStrength * 0.3f);
    }
}
