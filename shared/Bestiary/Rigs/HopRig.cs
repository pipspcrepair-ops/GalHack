using System;
using Godot;

namespace Wildlife;

/// <summary>
/// Leapers and living boulders. Locomotion does the ballistic hop; the rig sells it with
/// squash and stretch: the body sinks and the spring legs fold deeper before take-off, snap
/// straight in the air, and absorb the landing. (No throat sacs: they made leapers read as
/// frogs.) Living boulders tumble a quarter turn per hop on stubby legs that only show on
/// the ground.
/// </summary>
public sealed class HopRig : Rig
{
    readonly int _body, _head;
    readonly int[,] _hind = new int[2, 3];
    readonly int[] _front = new int[2];
    readonly int[] _tail;
    readonly float _thigh, _shin, _foot, _restHeight;
    float _squash, _ext, _tumble, _tumbleBase;
    HopPhase _lastPhase;

    static readonly Vector3 ThighFolded = Geo.Forward(0f, -0.75f), ShinFolded = Geo.Backward(0f, -0.55f), FootFolded = Geo.Forward(0f, -0.05f);
    static readonly Vector3 ThighDeep = Geo.Forward(0f, -0.35f), ShinDeep = Geo.Backward(0f, -0.2f);
    static readonly Vector3 ThighOut = Geo.Backward(0f, -1.0f), ShinOut = Geo.Backward(0f, -0.55f), FootOut = Geo.Backward(0f, -0.25f);

    public HopRig(Species s) : base(s, Build)
    {
        var pieces = Layout.Pieces;
        _body = pieces.FindIndex(p => p.Name == "body");
        _head = pieces.FindIndex(p => p.Name == "head");
        for (int side = 0; side < 2; side++)
        {
            for (int j = 0; j < 3; j++) _hind[side, j] = pieces.FindIndex(p => p.Name == $"hind{side}_{j}");
            _front[side] = pieces.FindIndex(p => p.Name == $"front{side}");
        }
        _tail = pieces.FindAll(p => p.Name.StartsWith("tail")).ConvertAll(p => pieces.IndexOf(p)).ToArray();
        (_thigh, _shin, _foot) = LegLengths(s);
        // Sitting height: whatever puts the folded hind foot exactly on the ground.
        Vector3 hip = HipLocal(s, 1);
        _restHeight = -(hip.Y + ThighFolded.Y * _thigh + ShinFolded.Y * _shin + FootFolded.Y * _foot);
    }

    static (float, float, float) LegLengths(Species s)
    {
        float l = s.Body.LegLength;
        return (l * 0.45f, l * 0.45f, l * 0.3f);
    }

    static Vector3 HipLocal(Species s, int side) =>
        new((side == 0 ? -1f : 1f) * s.Body.Width * 0.42f, -s.Body.Height * 0.12f, s.Body.Size * 0.22f);

    public override void Reset(WildSim w, Creature c)
    {
        ResetIntents(c);
        _lastPhase = c.Hop;
        Update(w, c, 0.016f);
    }

    public override void Update(WildSim w, Creature c, float dt)
    {
        if (dt <= 0f) return;
        SmoothIntents(c, dt);
        Proportions b = Species.Body;
        float L = b.Size, W = b.Width, H = b.Height;
        float ground = w.Terrain.Height(c.Position.X, c.Position.Z);
        bool boulder = Species.Tumbles;

        float squash, ext;
        switch (c.Hop)
        {
            case HopPhase.Crouch: squash = 0.15f + 0.6f * c.HopTime; ext = -0.3f * c.HopTime; break;
            case HopPhase.Air: squash = c.Velocity.Y > 0f ? -0.35f : -0.1f; ext = c.Velocity.Y > 0f ? 1f : 0.35f; break;
            case HopPhase.Land: squash = 0.7f * (1f - c.HopTime); ext = 0f; break;
            default: squash = 0.12f + MathF.Sin(c.Age * 2f + c.Id) * 0.03f; ext = -0.3f * Crouch; break; // (settled down to rest or to hide, the legs fold deep under it)
        }
        squash = Mathf.Lerp(squash, 0.5f, Math.Clamp(Hidden + Crouch * 0.5f + Bracing * 0.5f + Graze * 0.4f, 0f, 1f)) * (1f - Dead);
        _squash = Geo.Smooth(_squash, squash, 18f, dt);
        _ext = Geo.Smooth(_ext, ext, 16f, dt);

        if (boulder)
        {
            // A quarter turn per hop, finished on landing.
            if (c.Hop == HopPhase.Air)
            {
                float v0 = MathF.Max(0.1f, MathF.Sqrt(2f * 18f * b.Size * 0.3f));
                float progress = Math.Clamp(0.5f - c.Velocity.Y / (2f * v0), 0f, 1f);
                _tumble = _tumbleBase + progress * MathF.PI * 0.5f;
            }
            else if (_lastPhase == HopPhase.Air)
            {
                _tumbleBase += MathF.PI * 0.5f;
                _tumble = _tumbleBase;
            }
        }
        _lastPhase = c.Hop;

        bool airborne = c.Hop == HopPhase.Air;
        // (Resting, it settles down onto its folded legs.)
        float height = _restHeight * (1f - 0.35f * Math.Max(0f, _squash)) * (1f - 0.15f * Crouch * (c.Hop == HopPhase.Ground ? 1f : 0f));
        float bodyY = airborne ? c.Position.Y + _restHeight : ground + height;
        bodyY = Mathf.Lerp(bodyY, ground + H * 0.45f, Dead);
        float pitch = airborne ? Math.Clamp(c.Velocity.Y * 0.06f, -0.5f, 0.5f) : 0.18f + 0.15f * Math.Max(0f, _squash);
        // (Feeding, the nose goes down to the ground; resting, the body lies level.)
        pitch += Rearing * 0.35f - Lunging * 0.2f - (airborne ? 0f : Graze * 0.5f + Crouch * 0.12f);
        if (boulder) pitch = 0f;
        Basis yaw = new(Vector3.Up, c.Yaw);
        Basis frame = yaw * new Basis(Vector3.Right, pitch) * new Basis(Vector3.Back, Dead * 1.4f);
        var frameT = new Transform3D(frame, new Vector3(c.Position.X, bodyY, c.Position.Z));
        Vector3 scale = new(1f + 0.1f * _squash, 1f - 0.22f * _squash, 1f + 0.06f * _squash);
        Basis bodyB = boulder ? yaw * new Basis(Vector3.Right, -_tumble) : frame;
        Pose[_body] = new Transform3D(bodyB.Scaled(scale), frameT.Origin);
        BodyClearance = bodyY - ground;

        if (_head >= 0)
        {
            Vector3 at = frameT * new Vector3(0, H * 0.3f * (1f - 0.22f * _squash), -L * 0.4f);
            Basis hb = yaw * new Basis(Vector3.Up, LookYaw) * new Basis(Vector3.Right, -0.1f - Graze * 0.5f + Rearing * 0.2f);
            Pose[_head] = new Transform3D(hb, at);
        }

        // Spring legs: folded on the ground, deeper before a jump, straight out behind in the air.
        bool showLegs = !boulder || !airborne;
        for (int side = 0; side < 2; side++)
        {
            float sx = side == 0 ? -1f : 1f;
            Vector3 hip = frameT * HipLocal(Species, side);
            Vector3 t, s, f;
            if (_ext < 0f)
            {
                float k = Math.Clamp(-_ext / 0.3f, 0f, 1f);
                t = ThighFolded.Lerp(ThighDeep, k); s = ShinFolded.Lerp(ShinDeep, k); f = FootFolded;
            }
            else
            {
                t = ThighFolded.Lerp(ThighOut, _ext); s = ShinFolded.Lerp(ShinOut, _ext); f = FootFolded.Lerp(FootOut, _ext);
            }
            Vector3 splay = new(sx * 0.22f, 0f, 0f);
            Vector3 dt1 = (frame * (t + splay)).Normalized(), dt2 = (frame * (s + splay)).Normalized(), dt3 = (frame * f).Normalized();
            Vector3 knee = hip + dt1 * _thigh, ankle = knee + dt2 * _shin, toe = ankle + dt3 * _foot;
            Vector3 side3 = frame.X;
            Pose[_hind[side, 0]] = showLegs ? Geo.Segment(hip, knee, side3) : Collapsed(hip);
            Pose[_hind[side, 1]] = showLegs ? Geo.Segment(knee, ankle, side3) : Collapsed(knee);
            Pose[_hind[side, 2]] = showLegs ? Geo.Segment(ankle, toe, side3) : Collapsed(ankle);

            if (_front[side] >= 0)
            {
                Vector3 shoulder = frameT * new Vector3(sx * W * 0.32f, -H * 0.15f, -L * 0.28f);
                Vector3 reachTo = airborne
                    ? shoulder + frame * new Vector3(sx * 0.1f, -0.4f, 0.6f) * H
                    : new Vector3(shoulder.X, ground, shoulder.Z) + frame * new Vector3(sx * 0.1f, 0f, -0.12f) * H;
                Pose[_front[side]] = showLegs ? Geo.Segment(shoulder, reachTo, side3, H * 0.9f) : Collapsed(shoulder);
            }
        }

        if (_tail.Length > 0)
        {
            Vector3 p = frameT * new Vector3(0, 0, L * 0.42f);
            float seg = b.TailLength / _tail.Length, pitchAcc = -0.2f + 0.4f * _ext;
            for (int i = 0; i < _tail.Length; i++)
            {
                pitchAcc -= 0.15f;
                Vector3 d = OnGround(w, c, p, frame * Geo.Backward(MathF.Sin(c.Age * 2f + i) * 0.15f, pitchAcc), seg, W * 0.06f);
                Pose[_tail[i]] = Geo.Segment(p, p + d * seg, frame.X);
                p += d * seg;
            }
        }

        SetHit(0, frameT.Origin, MathF.Max(W, H) * 0.6f);
        HitCount = 1;
        if (_head >= 0) SetHit(1, Pose[_head] * new Vector3(0, 0, -b.HeadSize * 0.4f), b.HeadSize * 0.6f);
        SetBounds(frameT.Origin, L + b.LegLength);
    }

    // ---- Layout -----------------------------------------------------------------------

    static RigLayout Build(Species s)
    {
        var layout = new RigLayout();
        var k = new Paint(s);
        var rng = Rng.For(s.Seed, "layout");
        Proportions b = s.Body;
        float L = b.Size, W = b.Width, H = b.Height, hs = b.HeadSize;
        (float thigh, float shin, float foot) = LegLengths(s);

        var body = new Piece("body");
        LoftProfile shape = null;
        if (s.Tumbles)
        {
            // A living boulder: lumps of rock, moss in the cracks, a slit of light where it sees.
            body.Add(Shape.Sphere, new Vector3(W, H, L * 0.9f), Vector3.Zero, k.Base.Lerp(new Color(0.5f, 0.48f, 0.45f), 0.5f), 1f, 0f, null, 12, Mat.Carapace);
            int lumps = rng.Int(5, 8);
            for (int i = 0; i < lumps; i++)
            {
                Vector3 dir = new Vector3(rng.Range(-1f, 1f), rng.Range(-0.4f, 1f), rng.Range(-1f, 1f)).Normalized();
                var at = new Vector3(dir.X * W, dir.Y * H, dir.Z * L) * 0.35f;
                Shape shapeKind = i % 2 == 0 ? Shape.Box : Shape.Sphere;
                body.Add(shapeKind, Vector3.One * MathF.Min(W, H) * rng.Range(0.35f, 0.6f), at, k.Base.Darkened(rng.Range(0f, 0.3f)), 0.6f, 0f,
                    new Basis(new Vector3(rng.Range(-1f, 1f), 1f, rng.Range(-1f, 1f)).Normalized(), rng.Range(0f, 3f)), 0, Mat.Carapace);
            }
            for (int i = 0; i < 4; i++)
            {
                var at = new Vector3(rng.Range(-0.3f, 0.3f) * W, H * 0.42f, rng.Range(-0.3f, 0.3f) * L);
                body.Add(Shape.Sphere, new Vector3(W * 0.3f, H * 0.1f, W * 0.3f), at, k.Accent, 0f, k.GlowStrength * 0.5f);
            }
            body.Add(Shape.Sphere, new Vector3(W * 0.5f, H * 0.05f, W * 0.08f), new Vector3(0, H * 0.1f, -L * 0.44f), k.EyeGlow > 0 ? k.Glow : k.Mouth, 0f, k.EyeGlow, null, 0, Mat.Eye);
        }
        else
        {
            // A hunched body: heavy shoulders over the forelegs, the weight over the haunches.
            shape = LoftProfile.Smooth(-L * 0.46f, L * 0.44f, new[]
            {
                (0f, W * 0.28f, H * 0.32f, H * 0.04f), (0.35f, W * 0.44f, H * 0.48f, H * 0.12f), (0.72f, W * 0.5f, H * 0.48f, 0f), (1f, W * 0.34f, H * 0.34f, -H * 0.06f),
            }, 14, 0.2f);
            body.Loft(shape, k.Base, k.Belly, 1f, 0f, Mat.Skin, null, null, 16);
            Parts.Back(body, s.Part("back"), W, H, L * 0.86f, k, rng, shape);
        }
        if (s.Corrupted) Parts.Corruption(body, W, H, L * 0.85f, k, rng, shape);
        layout.Add(body);

        if (s.Part("head") != "none")
        {
            var head = new Piece("head");
            HeadShape info = Parts.Head(head, s.Part("head"), hs, k, rng);
            Parts.Horns(head, s.Part("horns"), hs, k, rng, info);
            if (s.Part("tendrils") is "chin" or "whiskers" or "back")
                for (int i = 0; i < 4; i++)
                    head.Taper(new Vector3((i - 1.5f) * hs * 0.15f, -hs * 0.22f, -hs * 0.7f), new Vector3((i - 1.5f) * hs * 0.3f, -hs * 0.9f, -hs * 0.8f),
                        hs * 0.035f, hs * 0.01f, s.Corrupted ? k.Veins : k.Base.Lerp(k.Accent, 0.4f), 0f, s.Corrupted ? 0.8f : 0f);
            layout.Add(head);
        }

        // Spring legs: a heavy thigh, a long shin and a long foot, every joint the same radius
        // on both sides so the leg folds without a seam.
        float legT = s.Tumbles ? W * 0.16f : W * 0.2f;
        Color leg = k.Base.Darkened(0.1f);
        float rT = legT * 0.72f, rK = legT * 0.42f, rA = legT * 0.3f, rToe = legT * 0.14f;
        for (int side = 0; side < 2; side++)
        {
            var t = new Piece($"hind{side}_0");
            t.Taper(Vector3.Zero, new Vector3(0, thigh, 0), rT, rK, leg, 0.8f);
            if (!s.Tumbles) t.Add(Shape.Sphere, new Vector3(legT * 1.7f, legT * 2.2f, legT * 2f), new Vector3(0, thigh * 0.22f, 0), k.Base, 1f, 0f, null, 10);
            layout.Add(t);
            var sh = new Piece($"hind{side}_1");
            sh.Taper(Vector3.Zero, new Vector3(0, shin, 0), rK, rA, leg, 0.5f);
            layout.Add(sh);
            var f = new Piece($"hind{side}_2") { Shadow = false };
            f.Taper(Vector3.Zero, new Vector3(0, foot, 0), rA, rToe, k.Dark, 0.2f);
            for (int c = -1; c <= 1; c += 2)
                f.Horn(k.Horn, rToe * 0.9f, Mat.Horn, new Vector3(c * rToe, foot, 0), new Vector3(c * rToe * 1.6f, foot + rToe * 3f, -rToe * 0.5f));
            layout.Add(f);
            var front = new Piece($"front{side}") { Shadow = false };
            float fl = H * 0.9f;
            front.Taper(Vector3.Zero, new Vector3(0, fl, 0), legT * 0.42f, legT * 0.2f, leg, 0.4f);
            layout.Add(front);
        }

        if (b.TailLength > L * 0.1f && s.Part("tail") != "none")
        {
            for (int i = 0; i < 2; i++)
            {
                var t = new Piece($"tail{i}");
                float seg = b.TailLength / 2f, r0 = W * 0.12f * (1f - 0.5f * i), r1 = W * 0.12f * (1f - 0.5f * (i + 1)) + W * 0.01f;
                t.Taper(Vector3.Zero, new Vector3(0, seg, 0), r0, r1, k.Base, 0.8f);
                if (i == 1) Parts.TailTip(t, s.Part("tail"), seg, W * 0.6f, k);
                layout.Add(t);
            }
        }
        return layout;
    }
}
