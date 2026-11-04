using System;
using Godot;

namespace Wildlife;

/// <summary>
/// Gas-bag jellies drifting on the wind: one to three breathing bags over a small gondola
/// with a face, trailing tendrils that stream behind with the motion and sway on their own.
/// Everything is forward kinematics from a bobbing body, so nothing can tangle or blow up.
/// </summary>
public sealed class GasBagRig : Rig
{
    readonly int _body, _head;
    readonly int[] _bags;
    readonly Vector3[] _bagAt;
    readonly int[][] _tendrils;
    readonly float _tendrilLen;

    public GasBagRig(Species s) : base(s, Build)
    {
        var pieces = Layout.Pieces;
        _body = pieces.FindIndex(p => p.Name == "body");
        _head = pieces.FindIndex(p => p.Name == "head");
        _bagAt = BagSpots(s);
        _bags = new int[_bagAt.Length];
        for (int i = 0; i < _bags.Length; i++) _bags[i] = pieces.FindIndex(p => p.Name == $"bag{i}");
        int count = TendrilCount(s);
        _tendrils = new int[count][];
        for (int t = 0; t < count; t++)
            _tendrils[t] = pieces.FindAll(p => p.Name.StartsWith($"tendril{t}_")).ConvertAll(p => pieces.IndexOf(p)).ToArray();
        _tendrilLen = MathF.Max(s.Body.TailLength, s.Body.Size * 0.6f);
    }

    static int TendrilCount(Species s) => s.Part("tendrils") switch { "curtain" => 10, "glowing" => 7, "dangling" => 6, _ => 4 };

    static Vector3[] BagSpots(Species s)
    {
        float W = s.Body.Width, H = s.Body.Height;
        return Math.Max(1, s.Body.Bags) switch
        {
            1 => new[] { new Vector3(0, H * 0.55f, 0) },
            2 => new[] { new Vector3(-W * 0.3f, H * 0.5f, 0), new Vector3(W * 0.32f, H * 0.62f, W * 0.1f) },
            _ => new[] { new Vector3(0, H * 0.75f, 0), new Vector3(-W * 0.38f, H * 0.4f, W * 0.15f), new Vector3(W * 0.36f, H * 0.42f, -W * 0.12f) },
        };
    }

    static float BagScale(Species s) => Math.Max(1, s.Body.Bags) switch { 1 => 1f, 2 => 0.72f, _ => 0.6f };

    public override void Reset(WildSim w, Creature c)
    {
        ResetIntents(c);
        Update(w, c, 0.016f);
    }

    public override void Update(WildSim w, Creature c, float dt)
    {
        if (dt <= 0f) return;
        SmoothIntents(c, dt);
        Proportions b = Species.Body;
        float W = b.Width, H = b.Height;
        Basis yaw = new(Vector3.Up, c.Yaw);
        Vector3 local = yaw.Inverse() * WildSim.Flat(c.Velocity);
        float tilt = Math.Clamp(local.Z * 0.06f, -0.25f, 0.25f), lean = Math.Clamp(-local.X * 0.06f, -0.25f, 0.25f);
        Basis body = yaw * new Basis(Vector3.Right, tilt) * new Basis(Vector3.Back, lean + Dead * 0.8f);
        var bodyT = new Transform3D(body, c.Position);
        float ground = w.Terrain.Height(c.Position.X, c.Position.Z);
        BodyClearance = c.Position.Y - ground;

        // Gondola hangs under the bags and swings a little.
        float swing = MathF.Sin(c.Age * 0.9f + c.Id) * 0.08f;
        Pose[_body] = new Transform3D(body * new Basis(Vector3.Back, swing), c.Position);
        if (_head >= 0)
        {
            Vector3 at = bodyT * new Vector3(0, -H * 0.05f, -W * 0.18f);
            Pose[_head] = new Transform3D(yaw * new Basis(Vector3.Up, LookYaw) * new Basis(Vector3.Right, -0.25f + Rearing * 0.3f), at);
        }

        float deflate = 1f - 0.45f * Dead;
        for (int i = 0; i < _bags.Length; i++)
        {
            float breathe = (1f + 0.07f * MathF.Sin(c.Age * 1.1f + i * 2.1f + c.Id)) * deflate * (1f + 0.15f * Rearing);
            Pose[_bags[i]] = new Transform3D(body.Scaled(Vector3.One * breathe), bodyT * _bagAt[i]);
        }

        Vector3 trail = -c.Velocity * 0.12f + w.Wind * 0.05f;
        for (int t = 0; t < _tendrils.Length; t++)
        {
            int[] chain = _tendrils[t];
            if (chain.Length == 0) continue;
            float a = t * MathF.Tau / _tendrils.Length;
            Vector3 p = bodyT * new Vector3(MathF.Cos(a) * W * 0.28f, -H * 0.12f, MathF.Sin(a) * W * 0.28f);
            Vector3 dir = Vector3.Down;
            float seg = _tendrilLen / chain.Length * (1f - 0.5f * Dead);
            for (int i = 0; i < chain.Length; i++)
            {
                var sway = new Vector3(MathF.Sin(c.Age * 0.9f + t * 1.3f + i * 0.5f), 0f, MathF.Cos(c.Age * 0.7f + t * 1.9f + i * 0.6f)) * 0.14f;
                dir = (dir + Vector3.Down * 0.6f + trail * (i + 1) * 0.15f + sway + Vector3.Up * Rearing * 0.3f).Normalized();
                Vector3 next = p + dir * seg;
                float g = w.Terrain.Height(next.X, next.Z) + 0.05f;
                if (next.Y < g) next.Y = g; // trail along the ground rather than through it
                Pose[chain[i]] = Geo.Segment(p, next, body.X, seg);
                p = next;
            }
        }

        int h = 0;
        float bagR = MathF.Max(W, H) * 0.5f * BagScale(Species);
        foreach (int i in _bags) SetHit(h++, Pose[i].Origin, bagR);
        SetHit(h++, c.Position, W * 0.25f);
        HitCount = h;
        SetBounds(bodyT * new Vector3(0, H * 0.4f, 0), MathF.Max(W, H) + _tendrilLen * 0.5f);
    }

    static RigLayout Build(Species s)
    {
        var layout = new RigLayout();
        var k = new Paint(s);
        var rng = Rng.For(s.Seed, "layout");
        Proportions b = s.Body;
        float W = b.Width, H = b.Height, hs = b.HeadSize;

        var body = new Piece("body");
        body.Add(Shape.Sphere, new Vector3(W * 0.36f, H * 0.26f, W * 0.42f), Vector3.Zero, k.Base, 1f);
        body.Add(Shape.Cylinder, new Vector3(W * 0.42f, H * 0.06f, W * 0.42f), new Vector3(0, H * 0.08f, 0), k.Accent, 0f, k.GlowStrength * 0.5f, null, 12);
        if (s.Corrupted) Parts.Corruption(body, W * 0.36f, H * 0.26f, W * 0.42f, k, rng);
        layout.Add(body);

        var head = new Piece("head");
        Parts.Head(head, s.Part("head") is "maw" or "lantern" or "dome" or "eyestalks" ? s.Part("head") : "dome", hs, k, rng);
        layout.Add(head);

        Vector3[] spots = BagSpots(s);
        float scale = BagScale(s);
        Color bag = k.Belly.Lerp(k.Accent, 0.3f).Lightened(0.1f);
        for (int i = 0; i < spots.Length; i++)
        {
            var piece = new Piece($"bag{i}");
            float bw = W * scale, bh = H * scale * rng.Range(0.85f, 1.1f);
            piece.Add(Shape.Sphere, new Vector3(bw, bh, bw * 1.08f), Vector3.Zero, i == 0 ? bag : bag.Lerp(k.Base, 0.25f), 1f, k.GlowStrength * 0.3f);
            piece.Add(Shape.Sphere, new Vector3(bw * 0.5f, bh * 0.12f, bw * 0.5f), new Vector3(0, -bh * 0.46f, 0), k.Base.Darkened(0.2f));
            if (i == 0) Parts.Back(piece, s.Part("back"), bw, bh, bw * 1.08f, k, rng);
            layout.Add(piece);
        }

        int count = TendrilCount(s);
        bool glowing = s.Part("tendrils") == "glowing" || s.Corrupted;
        for (int t = 0; t < count; t++)
        {
            int segs = 5;
            float len = MathF.Max(b.TailLength, b.Size * 0.6f) / segs;
            for (int i = 0; i < segs; i++)
            {
                var piece = new Piece($"tendril{t}_{i}") { Shadow = false };
                float d = W * 0.05f * (1f - 0.6f * i / segs);
                bool tip = i == segs - 1;
                piece.Add(Shape.Capsule, new Vector3(d, len + d, d), new Vector3(0, len * 0.5f, 0),
                    glowing && tip ? k.Veins : k.Base.Lerp(k.Accent, 0.5f * i / segs), 0.3f, glowing ? (tip ? 1f : 0.25f) : 0f);
                if (tip && glowing) piece.Add(Shape.Sphere, Vector3.One * d * 2.2f, new Vector3(0, len, 0), k.Veins, 0f, 1f);
                layout.Add(piece);
            }
        }
        return layout;
    }
}
