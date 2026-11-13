using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>
/// Crawlers, slitherers and burrowers: a follow-the-leader chain. The head is driven along the
/// creature's path with a sideways wave (big for legless slitherers, slight for centipedes),
/// and every segment simply keeps its spacing behind the one in front, so the wave travels
/// down the body by itself. Many-legged crawlers row their legs in a ripple. Burrowers sink
/// into the ground when hiding and leave a mound; any chain rears up like a cobra to strike.
/// </summary>
public sealed class ChainRig : Rig
{
    readonly Vector3[] _p;
    readonly Basis[] _bases;
    readonly float[] _r;
    readonly int[] _segs;
    readonly int _head, _tailTip, _mound;
    readonly List<(int seg, int side, int piece)> _legs = new();
    readonly int[][] _feelers, _whiskers;
    readonly float _segLen, _legLen;
    readonly bool _slither;
    float _wave, _legPhase;

    public ChainRig(Species s) : base(s, Build)
    {
        Proportions b = s.Body;
        int n = Math.Max(3, b.Segments);
        _p = new Vector3[n];
        _bases = new Basis[n];
        _r = Radii(s);
        _segLen = b.Size / n;
        _legLen = MathF.Max(b.LegLength, b.Width * 0.6f);
        _slither = b.Legs == 0;
        var pieces = Layout.Pieces;
        _segs = new int[n];
        for (int i = 0; i < n; i++) _segs[i] = pieces.FindIndex(p => p.Name == $"seg{i}");
        _head = pieces.FindIndex(p => p.Name == "head");
        _tailTip = pieces.FindIndex(p => p.Name == "tailtip");
        _mound = pieces.FindIndex(p => p.Name == "mound");
        for (int i = 0; i < n; i++)
        for (int side = -1; side <= 1; side += 2)
        {
            int idx = pieces.FindIndex(p => p.Name == $"leg{i}_{side}");
            if (idx >= 0) _legs.Add((i, side, idx));
        }
        _feelers = Chains("feeler", 2);
        _whiskers = Chains("whisker", 4);
    }

    int[][] Chains(string prefix, int max)
    {
        var chains = new List<int[]>();
        for (int c = 0; c < max; c++)
        {
            var list = new List<int>();
            for (int i = 0; i < Layout.Pieces.Count; i++)
                if (Layout.Pieces[i].Name.StartsWith($"{prefix}{c}_")) list.Add(i);
            if (list.Count > 0) chains.Add(list.ToArray());
        }
        return chains.ToArray();
    }

    static float[] Radii(Species s)
    {
        Proportions b = s.Body;
        int n = Math.Max(3, b.Segments);
        var r = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)(n - 1);
            r[i] = b.Width * 0.5f * (1f - 0.55f * MathF.Pow(t, 1.6f));
            if (s.Form == "burrower") r[i] *= 1f + 0.12f * MathF.Sin(i * 1.9f); // lumpy, grub-like
        }
        r[0] = MathF.Max(r[0], b.HeadSize * 0.4f);
        return r;
    }

    public override void Reset(WildSim w, Creature c)
    {
        ResetIntents(c);
        Vector3 back = -c.Forward;
        for (int i = 0; i < _p.Length; i++)
        {
            Vector3 q = WildSim.Flat(c.Position) + back * _segLen * i;
            _p[i] = new Vector3(q.X, w.Terrain.Height(q.X, q.Z) + _r[i], q.Z);
        }
        Update(w, c, 0.016f);
    }

    public override void Update(WildSim w, Creature c, float dt)
    {
        if (dt <= 0f) return;
        SmoothIntents(c, dt);
        Proportions b = Species.Body;
        Vector3 vel = WildSim.Flat(c.Velocity);
        float speed = vel.Length();
        float moving = Math.Clamp(speed / MathF.Max(0.3f, Species.Stats.WalkSpeed), 0f, 1.5f);
        Basis yaw = new(Vector3.Up, c.Yaw);
        Vector3 fwd = yaw * Vector3.Forward, right = yaw * Vector3.Right;

        _wave += speed * dt / MathF.Max(0.3f, b.Size * 0.7f) * MathF.Tau;
        _legPhase += speed * dt / MathF.Max(0.1f, _legLen * 1.2f) * MathF.Tau;
        float amp = (_slither ? b.Width * 1.1f : b.Width * 0.15f) * MathF.Min(1f, moving);
        float rmax = 0f;
        foreach (float r in _r) rmax = MathF.Max(rmax, r);
        float sink = (Species.Burrows ? Hidden * (2f * rmax + 0.15f) : Hidden * rmax * 0.9f) * (1f - Dead);

        // The head, driven along the path with the wave; then everyone follows.
        Vector3 drive = WildSim.Flat(c.Position) + right * MathF.Sin(_wave) * amp;
        _p[0] = Lift(w, drive, 0, sink);
        // At rest it draws itself into a coil round a point beside its head (and pays out again as the head moves off).
        float coil = c.State == CState.Rest && !c.Dead && speed < 0.05f ? MathF.Min(1f, 2.5f * dt) : 0f;
        float turn0 = MathF.Max(1.3f * rmax, _segLen * 0.9f), turn = turn0, round = 0f;
        Vector3 centre = WildSim.Flat(c.Position) + right * turn0;
        for (int i = 1; i < _p.Length; i++)
        {
            Vector3 d = WildSim.Flat(_p[i] - _p[i - 1]);
            d = d.LengthSquared() > 1e-6f ? d.Normalized() : -fwd;
            float spacing = _segLen * (i < 4 ? 1f - Rearing * 0.35f : 1f);
            Vector3 q = WildSim.Flat(_p[i - 1]) + d * spacing;
            if (coil > 0f)
            {
                round += _segLen / turn;
                turn = turn0 + 2.1f * rmax * round / MathF.Tau;
                q = q.Lerp(centre - (right * MathF.Cos(round) + fwd * MathF.Sin(round)) * turn, coil);
            }
            _p[i] = Lift(w, q, i, sink);
        }

        float twitch = Species.Corrupted && !c.Dead ? MathF.Max(0f, MathF.Sin(c.Age * 6.3f + c.Id) - 0.85f) * 3f : 0f;
        Basis[] bases = _bases;
        for (int i = 0; i < _p.Length; i++)
        {
            Vector3 f = i == 0 ? _p[0] - _p[1] : _p[i - 1] - _p[i];
            bases[i] = Facing(f) * new Basis(Vector3.Back, Dead * 1.5f + twitch * MathF.Sin(c.Age * 30f + i));
            Pose[_segs[i]] = new Transform3D(bases[i], _p[i]);
        }

        Vector3 headFwd = -bases[0].Z;
        // (Feeding, the head goes down and noses from side to side.)
        Basis headB = Facing(headFwd) * new Basis(Vector3.Up, LookYaw * 0.4f + Graze * 0.3f * MathF.Sin(c.Age * 1.3f)) * new Basis(Vector3.Right, -Rearing * 0.5f + Lunging * 0.1f - Graze * 0.45f);
        var headT = new Transform3D(headB, _p[0] + headFwd * _r[0] * 0.55f + headFwd * Lunging * b.Size * 0.12f);
        if (_head >= 0) Pose[_head] = headT;

        // Rowing legs: a ripple runs from head to tail, each foot lifting on its forward swing.
        foreach (var (seg, side, piece) in _legs)
        {
            Basis sb = bases[seg];
            Vector3 f = -sb.Z, rr = sb.X;
            float phase = _legPhase - seg * 0.9f + (side > 0 ? MathF.PI : 0f);
            float swing = MathF.Sin(phase) * MathF.Min(1f, moving), lift = MathF.Max(0f, MathF.Cos(phase)) * MathF.Min(1f, moving);
            Vector3 hip = _p[seg] + rr * side * _r[seg] * 0.7f;
            Vector3 tip = _p[seg] + rr * side * (_r[seg] + _legLen * 0.62f) + f * swing * _legLen * 0.35f;
            float ground = w.Terrain.Height(tip.X, tip.Z);
            tip.Y = ground + lift * _legLen * 0.3f - sink + Dead * _r[seg];
            Pose[piece] = Geo.Segment(hip, tip, f, _legLen);
        }

        int last = _p.Length - 1;
        if (_tailTip >= 0) Pose[_tailTip] = new Transform3D(Geo.AlongY(_p[last] - _p[last - 1], bases[last].X), _p[last]);
        if (_mound >= 0)
        {
            float g = w.Terrain.Height(_p[0].X, _p[0].Z);
            float m = Species.Burrows ? Math.Clamp(Hidden * 1.4f - 0.2f, 0f, 1f) : 0f;
            Pose[_mound] = m < 0.02f ? Collapsed(_p[0]) : new Transform3D(new Basis(Vector3.Up, c.Yaw).Scaled(new Vector3(m, m, m)), new Vector3(_p[0].X, g, _p[0].Z));
        }

        PoseFeelers(c, headT);
        PoseWhiskers(c, headT);

        int h = 0;
        if (_head >= 0) SetHit(h++, headT * new Vector3(0, 0, -b.HeadSize * 0.5f), b.HeadSize * 0.6f);
        for (int i = 0; i < _p.Length && h < Hits.Length; i += 2) SetHit(h++, _p[i], _r[i] * 1.2f);
        HitCount = h;
        SetBounds(_p[_p.Length / 2], b.Size * 0.6f + rmax);
        BodyClearance = _p[0].Y - w.Terrain.Height(_p[0].X, _p[0].Z);
    }

    Vector3 Lift(WildSim w, Vector3 q, int i, float sink)
    {
        float lift = Rearing * MathF.Max(0f, 1f - i / 3.5f) * Species.Body.Size * 0.28f;
        float ground = w.Terrain.Height(q.X, q.Z);
        return new Vector3(q.X, ground + _r[i] * 0.85f - sink + lift, q.Z);
    }

    void PoseFeelers(Creature c, Transform3D head)
    {
        float hs = Species.Body.HeadSize;
        for (int f = 0; f < _feelers.Length; f++)
        {
            float s = f == 0 ? -1f : 1f;
            Vector3 p = head * new Vector3(s * hs * 0.3f, hs * 0.1f, -hs * 0.6f);
            bool scythes = Species.Part("limbs") == "scythes";
            float seg = hs * (scythes ? 0.55f : 0.8f);
            for (int i = 0; i < _feelers[f].Length; i++)
            {
                float wave = MathF.Sin(c.Age * 3f + f * 2f + i) * 0.35f;
                // Scythes are a mantis's folded arms: out and down, then the blade back up.
                Vector3 local = scythes
                    ? (i == 0 ? new Vector3(s * 0.6f, -0.5f + Rearing * 0.8f, -0.6f) : new Vector3(s * 0.1f, 0.9f, -0.4f - Lunging))
                    : new Vector3(s * 0.5f, 0.4f + wave, -1f);
                Vector3 dir = (head.Basis * local).Normalized();
                Pose[_feelers[f][i]] = Geo.Segment(p, p + dir * seg, head.Basis.X);
                p += dir * seg;
            }
        }
    }

    void PoseWhiskers(Creature c, Transform3D head)
    {
        float hs = Species.Body.HeadSize;
        for (int f = 0; f < _whiskers.Length; f++)
        {
            float spread = (f - (_whiskers.Length - 1) * 0.5f) * 0.5f;
            Vector3 p = head * new Vector3(spread * hs * 0.4f, -hs * 0.2f, -hs * 0.7f);
            Vector3 dir = head.Basis * new Vector3(spread, -1f, -0.3f);
            for (int i = 0; i < _whiskers[f].Length; i++)
            {
                var sway = new Vector3(MathF.Sin(c.Age * 1.7f + f + i), 0f, MathF.Cos(c.Age * 1.3f + f * 2f)) * 0.3f;
                dir = (dir.Normalized() + Vector3.Down * 0.3f + sway).Normalized();
                Pose[_whiskers[f][i]] = Geo.Segment(p, p + dir * hs * 0.45f, head.Basis.X);
                p += dir * hs * 0.45f;
            }
        }
    }

    /// <summary>A basis facing along f (forward is -Z), kept upright.</summary>
    static Basis Facing(Vector3 f)
    {
        if (f.LengthSquared() < 1e-8f) return Basis.Identity;
        Vector3 z = -f.Normalized();
        Vector3 x = Vector3.Up.Cross(z);
        if (x.LengthSquared() < 1e-6f) x = Vector3.Right;
        x = x.Normalized();
        return new Basis(x, z.Cross(x), z);
    }

    // ---- Layout -----------------------------------------------------------------------

    static RigLayout Build(Species s)
    {
        var layout = new RigLayout();
        var k = new Paint(s);
        var rng = Rng.For(s.Seed, "layout");
        Proportions b = s.Body;
        int n = Math.Max(3, b.Segments);
        float[] r = Radii(s);
        float seg = b.Size / n, hs = b.HeadSize;
        string back = s.Part("back");
        bool legged = b.Legs > 0;

        var head = new Piece("head");
        HeadShape shape = Parts.Head(head, s.Part("head"), hs, k, rng);
        Parts.Horns(head, s.Part("horns"), hs, k, rng, shape);
        layout.Add(head);

        for (int i = 0; i < n; i++)
        {
            var piece = new Piece($"seg{i}") { Shadow = r[i] > 0.12f };
            float d = r[i] * 2f;
            Color c = i % 2 == 0 ? k.Base : k.Base.Lerp(k.Accent, 0.15f);
            // Each segment overlaps its neighbours, so the chain reads as one body that bends.
            var profile = LoftProfile.Smooth(-seg * 0.78f, seg * 0.78f, new[]
            {
                (0f, r[i] * 0.92f, r[i] * 0.8f, 0f), (0.45f, r[i], r[i] * 0.86f, r[i] * 0.02f), (1f, r[i] * 0.88f, r[i] * 0.76f, 0f),
            }, 8, 0.32f);
            piece.Loft(profile, c, k.Belly, 1f, 0f, Mat.Skin, null, null, 12);
            // Armoured crawlers wear a hard plate over each segment; slitherers are bare scales.
            if (legged || back is "plates" or "shell")
                piece.Add(Shape.Sphere, new Vector3(d * 1.08f, d * 0.5f, seg * 1.25f), new Vector3(0, r[i] * 0.42f, seg * 0.12f), back == "shell" ? k.Accent.Lerp(k.Base, 0.3f) : k.Accent,
                    back == "shell" ? 0.6f : 0.15f, 0f, Parts.Pitch(-8f), 12, Mat.Carapace);
            switch (back)
            {
                case "spines":
                    piece.Taper(new Vector3(0, r[i] * 0.6f, 0), new Vector3(0, r[i] * 1.9f, seg * 0.45f), r[i] * 0.18f, r[i] * 0.02f, k.Bone, 0f, 0f, Mat.Horn, 6);
                    break;
                case "ridge" when i % 2 == 0:
                    piece.Taper(new Vector3(0, r[i] * 0.7f, 0), new Vector3(0, r[i] * 1.3f, seg * 0.25f), r[i] * 0.2f, r[i] * 0.04f, k.Accent.Lerp(k.Bone, 0.3f), 0f, 0f, Mat.Horn, 6);
                    break;
                case "sail":
                    piece.Add(Shape.Sphere, new Vector3(r[i] * 0.1f, r[i] * 1.8f, seg * 1.25f), new Vector3(0, r[i] * 1.2f, 0), k.Accent, 1f, 0f, null, 0, Mat.Membrane);
                    break;
                case "crystals" when i % 2 == 1:
                    piece.Add(Shape.Cone, new Vector3(r[i] * 0.7f, r[i] * 2.2f, 0), new Vector3(0, r[i] * 1.5f, 0), (k.GlowStrength > 0 ? k.Glow : k.Accent).Lightened(0.25f), 0f, 0.9f,
                        new Basis(Vector3.Back, rng.Range(-0.4f, 0.4f)), 6, Mat.Horn);
                    break;
                case "vents" when i % 3 == 1:
                    piece.Add(Shape.Cylinder, new Vector3(r[i] * 0.8f, r[i] * 1.4f, r[i] * 0.6f), new Vector3(0, r[i] * 1.2f, 0), k.Dark, 0f, 0f, null, 8, Mat.Carapace);
                    piece.Add(Shape.Cylinder, new Vector3(r[i] * 0.5f, r[i] * 0.1f, r[i] * 0.5f), new Vector3(0, r[i] * 1.9f, 0), k.Accent.Lightened(0.3f), 0f, 0.9f, null, 8);
                    break;
            }
            if (s.Corrupted && i % 3 == 1)
                piece.Add(Shape.Sphere, Vector3.One * r[i] * 0.55f, new Vector3(r[i] * 0.5f * (i % 2 == 0 ? 1 : -1), r[i] * 0.6f, 0), k.Veins, 0f, 1f, null, 0, Mat.Membrane);
            layout.Add(piece);
        }

        if (legged)
        {
            // Jointed legs that end in a point, posed from hip to tip by the rig.
            float legLen = MathF.Max(b.LegLength, b.Width * 0.6f);
            for (int i = 1; i < n - 1; i++)
            for (int side = -1; side <= 1; side += 2)
            {
                var leg = new Piece($"leg{i}_{side}") { Shadow = false };
                float rr = MathF.Max(0.015f, r[i] * 0.16f);
                leg.Taper(Vector3.Zero, new Vector3(0, legLen * 0.5f, 0), rr, rr * 0.8f, k.Dark, 0.3f, 0f, Mat.Carapace, 6);
                leg.Taper(new Vector3(0, legLen * 0.5f, 0), new Vector3(0, legLen, 0), rr * 0.8f, rr * 0.12f, k.Dark.Lerp(k.Horn, 0.3f), 0f, 0f, Mat.Carapace, 6);
                layout.Add(leg);
            }
        }

        string tail = s.Part("tail");
        if (tail != "none")
        {
            var tip = new Piece("tailtip");
            Parts.TailTip(tip, tail == "whip" ? "stinger" : tail, r[n - 1], r[n - 1] * 2.5f, k);
            layout.Add(tip);
        }

        if (s.Burrows)
        {
            var mound = new Piece("mound") { Shadow = false };
            mound.Add(Shape.Sphere, new Vector3(b.Width * 2.4f, b.Width * 0.6f, b.Width * 2.8f), Vector3.Zero, new Color(0.3f, 0.24f, 0.18f).Lerp(k.Base, 0.25f), 0f, 0f, null, 12);
            for (int i = 0; i < 5; i++)
                mound.Add(Shape.Sphere, Vector3.One * b.Width * rng.Range(0.3f, 0.6f), new Vector3(rng.Range(-1f, 1f) * b.Width, b.Width * 0.15f, rng.Range(-1.2f, 1.2f) * b.Width), new Color(0.26f, 0.2f, 0.15f));
            layout.Add(mound);
        }

        if (s.Part("limbs") is "feelers" or "scythes" || s.Part("horns") == "antennae")
        {
            bool scythes = s.Part("limbs") == "scythes";
            for (int f = 0; f < 2; f++)
            for (int i = 0; i < 2; i++)
            {
                var piece = new Piece($"feeler{f}_{i}") { Shadow = false };
                float len = hs * (scythes ? 0.55f : 0.8f), rr = hs * (scythes ? 0.06f : 0.03f);
                piece.Taper(Vector3.Zero, new Vector3(0, len, 0), rr, rr * 0.7f, scythes ? k.Base.Darkened(0.2f) : k.Dark, 0f, 0f, Mat.Carapace, 6);
                if (i == 1 && scythes)
                    piece.Horn(k.Bone, hs * 0.06f, Mat.Horn, new Vector3(0, len, 0), new Vector3(0, len * 1.3f, len * 0.25f), new Vector3(0, len * 1.4f, len * 0.6f));
                layout.Add(piece);
            }
        }

        if (s.Part("tendrils") != "none")
        {
            for (int f = 0; f < 4; f++)
            for (int i = 0; i < 3; i++)
            {
                var piece = new Piece($"whisker{f}_{i}") { Shadow = false };
                float len = hs * 0.45f, rr = hs * 0.025f;
                bool glow = s.Corrupted || s.Part("tendrils") == "glowing";
                piece.Taper(Vector3.Zero, new Vector3(0, len, 0), rr * (1f - 0.3f * i), rr * (0.7f - 0.3f * i) + 0.002f,
                    glow && i == 2 ? k.Veins : k.Base.Lerp(k.Accent, 0.4f), 0f, glow && i == 2 ? 1f : 0f);
                layout.Add(piece);
            }
        }
        return layout;
    }
}
