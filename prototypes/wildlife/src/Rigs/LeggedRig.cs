using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>
/// Walkers, giants, walking plants and crystal grazers: a body on 2 to 8 legs with IK foot
/// stepping. Each foot stays planted until its home spot (under its hip, led by the body's
/// velocity) gets too far away, then steps there in an arc, but only when its neighbours are
/// down and its half of the gait is up, which gives a steady diagonal walk for any leg count.
/// Two-bone IK bends each knee towards a pole that depends on the leg style. Necks, tails,
/// tendrils and extra limbs are forward kinematics with procedural sway, so they never
/// explode.
/// </summary>
public sealed class LeggedRig : Rig
{
    sealed class Leg
    {
        public Vector3 Anchor, Rest, Pole;
        public float Upper, Lower;
        public int Group, UpPiece, LoPiece, FootPiece;
        public int[] Neighbours = Array.Empty<int>();
        public Vector3 Foot, From, To, Drawn;
        public float T;
        public bool Stepping;
    }

    readonly Leg[] _legs;
    readonly int _body;
    readonly int[] _heads;
    readonly int[][] _necks;
    readonly int[][] _tails;
    readonly int[][] _limbs;
    readonly int[][] _tendrils;
    readonly bool _chinTendrils;
    readonly float _hipRest, _neckLen, _tailLen, _neckRest;
    float _gait, _neckPitch, _sag;

    public LeggedRig(Species s) : base(s, Build)
    {
        Proportions b = s.Body;
        var pieces = Layout.Pieces;
        _body = pieces.FindIndex(p => p.Name == "body");
        _heads = FindAll("head");
        _necks = Chains("neck", _heads.Length);
        _tails = Chains("tail", s.Part("tail") == "twin" ? 2 : 1);
        _limbs = Chains("limb", 2);
        _tendrils = Chains("tendril", 12);
        _chinTendrils = s.Part("tendrils") is "chin" or "whiskers";
        _neckLen = b.NeckLength;
        _tailLen = b.TailLength;
        _neckRest = b.NeckLength > b.Size * 0.5f ? 0.95f : 0.35f;

        float legLen = b.LegLength;
        _hipRest = b.LegStyle switch
        {
            LegStyle.Splayed => 0.5f, LegStyle.Roots => 0.42f, LegStyle.Stilt => 0.9f, _ => 0.86f,
        } * legLen;

        int n = b.Legs;
        _legs = new Leg[n];
        int pairs = n / 2;
        bool odd = n % 2 == 1;
        for (int i = 0; i < n; i++)
        {
            var leg = new Leg { Upper = legLen * 0.52f, Lower = legLen * 0.56f };
            int pair = i / 2;
            int side = odd && i == n - 1 ? 0 : (i % 2 == 0 ? -1 : 1);
            float z = pairs <= 1 ? (n <= 3 ? -0.18f : 0.05f) : Mathf.Lerp(-0.32f, 0.32f, pair / (float)(pairs - 1));
            if (side == 0) z = 0.4f;
            float x = side * b.Width * (b.LegStyle == LegStyle.Splayed ? 0.48f : 0.4f);
            leg.Anchor = new Vector3(x, -b.Height * 0.25f, z * b.Size);
            float outward = b.LegStyle switch
            {
                LegStyle.Splayed => 0.72f, LegStyle.Roots => 0.7f, LegStyle.Stilt => 0.16f, _ => 0.08f,
            } * legLen;
            float spread = b.LegStyle is LegStyle.Splayed or LegStyle.Roots ? z * 0.6f * legLen : 0f;
            leg.Rest = side == 0 ? new Vector3(0, 0, outward) : new Vector3(side * outward, 0, spread);
            // Knees: forward on front legs and back on hind ones for upright walkers, backwards
            // like a bird's on stilts, and up-and-out like a spider's for splayed legs.
            leg.Pole = b.LegStyle switch
            {
                LegStyle.Splayed or LegStyle.Roots => new Vector3(side == 0 ? 0 : side * 0.6f, 1f, side == 0 ? 0.6f : 0f),
                LegStyle.Stilt => new Vector3(0, 0.2f, 1f),
                _ => z < 0f || n == 2 ? new Vector3(0, 0, -1f) : new Vector3(0, 0, 1f),
            };
            leg.Group = side == 0 ? pairs % 2 : (pair + (side > 0 ? 1 : 0)) % 2;
            leg.UpPiece = pieces.FindIndex(p => p.Name == $"leg{i}u");
            leg.LoPiece = pieces.FindIndex(p => p.Name == $"leg{i}l");
            leg.FootPiece = pieces.FindIndex(p => p.Name == $"leg{i}f");
            _legs[i] = leg;
        }
        // Neighbours: the other leg of the pair and the legs before and after on the same side.
        for (int i = 0; i < n; i++)
        {
            var list = new List<int>();
            for (int j = 0; j < n; j++)
            {
                if (j == i) continue;
                bool samePair = j / 2 == i / 2 && !(odd && (i == n - 1 || j == n - 1));
                bool adjacent = Math.Abs(j / 2 - i / 2) == 1 && (j % 2 == i % 2 || (odd && (i == n - 1 || j == n - 1)));
                if (samePair || adjacent || n <= 3) list.Add(j);
            }
            _legs[i].Neighbours = list.ToArray();
        }
    }

    int[] FindAll(string prefix)
    {
        var list = new List<int>();
        for (int i = 0; i < Layout.Pieces.Count; i++) if (Layout.Pieces[i].Name == prefix) list.Add(i);
        return list.ToArray();
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

    public override int FootCount => _legs.Length;
    public override bool FootPlanted(int i) => !_legs[i].Stepping;
    public override Vector3 FootTarget(int i) => _legs[i].Foot;
    public override Vector3 FootDrawn(int i) => _legs[i].Drawn;

    // ---- Animation --------------------------------------------------------------------

    public override void Reset(WildSim w, Creature c)
    {
        ResetIntents(c);
        Basis yaw = new(Vector3.Up, c.Yaw);
        foreach (Leg leg in _legs)
        {
            leg.Foot = Ground(w, WildSim.Flat(c.Position) + yaw * WildSim.Flat(leg.Anchor + leg.Rest));
            leg.Stepping = false;
            leg.Drawn = leg.Foot;
        }
        _neckPitch = _neckRest;
        Update(w, c, 0.016f);
    }

    public override void Update(WildSim w, Creature c, float dt)
    {
        if (dt <= 0f) return;
        SmoothIntents(c, dt);
        Proportions b = Species.Body;
        float L = b.Size, H = b.Height, W = b.Width, legLen = b.LegLength;
        Vector3 vel = WildSim.Flat(c.Velocity);
        float speed = vel.Length();
        float walk = MathF.Max(0.3f, Species.Stats.WalkSpeed);

        Basis yaw = new(Vector3.Up, c.Yaw);
        Vector3 fwd = yaw * Vector3.Forward;
        Vector3 pos = WildSim.Flat(c.Position);
        float gF = w.Terrain.Height(pos + fwd * L * 0.35f), gB = w.Terrain.Height(pos - fwd * L * 0.35f);
        float groundMid = (gF + gB) * 0.5f;

        // Body: lowered by crouching, hiding and death, bobbing twice per stride.
        _gait += speed * dt / MathF.Max(0.15f, legLen * 0.9f);
        float hip = _hipRest * (1f - 0.38f * Crouch - 0.3f * Hidden);
        float bob = MathF.Sin(_gait * MathF.Tau * 2f) * 0.035f * legLen * MathF.Min(1f, speed / walk);
        float breathe = MathF.Sin(c.Age * 1.7f + c.Id) * 0.01f * H;
        float bodyY = groundMid + hip + H * 0.25f + bob + breathe;
        float deadY = groundMid + H * 0.5f;
        bodyY = Mathf.Lerp(bodyY, deadY, Dead);
        float pitch = MathF.Atan2(gF - gB, 0.7f * L) + Rearing * 0.35f - Lunging * 0.15f + Graze * 0.04f;
        float roll = Math.Clamp(-YawRate * speed * 0.03f, -0.15f, 0.15f) + Dead * 1.35f + Twitch(c) * 0.08f;
        Vector3 lunge = fwd * (Lunging * 0.22f * L - Rearing * 0.08f * L);
        Basis body = yaw * new Basis(Vector3.Right, pitch) * new Basis(Vector3.Back, roll);
        var bodyT = new Transform3D(body, new Vector3(pos.X, bodyY, pos.Z) + lunge);

        // The body sags onto its legs rather than lifting a planted foot: rearing, lunging or a
        // dip in the ground lowers it just enough for every planted foot to stay in reach.
        float drop = 0f;
        foreach (Leg leg in _legs)
        {
            if (leg.Stepping) continue;
            Vector3 hipW = bodyT * leg.Anchor;
            Vector3 d = leg.Foot - hipW;
            float flat = new Vector2(d.X, d.Z).Length(), reach = (leg.Upper + leg.Lower) * 0.97f;
            if (flat >= reach) continue; // only a step can fix that
            float maxHip = leg.Foot.Y + MathF.Sqrt(reach * reach - flat * flat);
            drop = MathF.Max(drop, hipW.Y - maxHip);
        }
        drop = MathF.Min(drop, legLen * 0.5f);
        _sag = drop > _sag ? drop : Geo.Smooth(_sag, drop, 6f, dt);
        bodyT.Origin += Vector3.Down * _sag;
        Pose[_body] = bodyT;
        BodyClearance = bodyT.Origin.Y - groundMid;

        StepLegs(w, c, dt, yaw, pos, vel, speed, walk, bodyT);
        PoseNecks(w, c, dt, bodyT, yaw, groundMid);
        PoseTails(c, dt, bodyT);
        PoseLimbs(c, bodyT);
        PoseTendrils(c, bodyT);

        // Hit spheres: body (front, middle, back on long bodies) and each head.
        int h = 0;
        float r = MathF.Max(W, H) * 0.6f;
        SetHit(h++, bodyT.Origin, r);
        if (L > 1.5f * W)
        {
            SetHit(h++, bodyT * new Vector3(0, 0, -L * 0.3f), W * 0.55f);
            SetHit(h++, bodyT * new Vector3(0, 0, L * 0.3f), W * 0.55f);
        }
        foreach (int head in _heads) SetHit(h++, Pose[head] * new Vector3(0, 0, -b.HeadSize * 0.5f), b.HeadSize * 0.6f);
        HitCount = h;
        SetBounds(bodyT.Origin, L * 0.6f + legLen + _neckLen + b.HeadSize);
    }

    void StepLegs(WildSim w, Creature c, float dt, Basis yaw, Vector3 pos, Vector3 vel, float speed, float walk, Transform3D bodyT)
    {
        float legLen = Species.Body.LegLength;
        float duration = Math.Clamp(0.42f * MathF.Sqrt(legLen) / (1f + speed * 0.25f), 0.12f, 0.7f);
        float threshold = MathF.Max(0.1f, legLen * (speed < 0.1f ? 0.12f : 0.32f));
        int activeGroup = (int)MathF.Floor(_gait * 2f) & 1;
        bool dead = Dead > 0f;

        foreach (Leg leg in _legs)
        {
            Vector3 home = Ground(w, pos + yaw * WildSim.Flat(leg.Anchor + leg.Rest) + vel * duration * 0.5f);
            Vector3 hipW = bodyT * leg.Anchor;
            float reach = leg.Upper + leg.Lower;

            if (leg.Stepping)
            {
                leg.T += dt / duration;
                leg.To = home; // keep re-aiming while in the air, so turning feet land true
                if (leg.T >= 1f)
                {
                    leg.Stepping = false;
                    leg.Foot = leg.To;
                }
            }
            else if (!dead)
            {
                float err = WildSim.Flat(home - leg.Foot).Length();
                float stretch = (leg.Foot - hipW).Length();
                bool overstretched = stretch > reach * 0.97f;
                bool myTurn = speed < 0.1f || leg.Group == activeGroup || err > threshold * 1.8f;
                if (err > legLen * 3f)
                {
                    leg.Foot = home; // teleported or knocked far: snap rather than skate
                }
                else if ((err > threshold && myTurn || overstretched) && NeighboursDown(leg) || stretch > reach * 0.995f)
                {
                    leg.Stepping = true;
                    leg.T = 0f;
                    leg.From = leg.Foot;
                    leg.To = home;
                }
            }

            Vector3 foot = leg.Foot;
            if (leg.Stepping)
            {
                float t = leg.T * leg.T * (3f - 2f * leg.T);
                float lift = MathF.Min(0.3f * legLen, 0.06f + (leg.To - leg.From).Length() * 0.35f);
                foot = leg.From.Lerp(leg.To, t) + Vector3.Up * lift * MathF.Sin(leg.T * MathF.PI);
            }

            Vector3 pole = bodyT.Basis * leg.Pole;
            (Vector3 knee, Vector3 drawn) = SolveIK(hipW, foot, leg.Upper, leg.Lower, pole);
            leg.Drawn = drawn;
            Vector3 side = bodyT.Basis.X;
            Pose[leg.UpPiece] = Geo.Segment(hipW, knee, side);
            Pose[leg.LoPiece] = Geo.Segment(knee, drawn, side);
            Pose[leg.FootPiece] = new Transform3D(yaw, drawn);
        }
    }

    bool NeighboursDown(Leg leg)
    {
        foreach (int n in leg.Neighbours) if (_legs[n].Stepping) return false;
        return true;
    }

    /// <summary>Two-bone IK in the plane of hip, foot and pole. The foot is drawn at full reach if it is too far.</summary>
    static (Vector3 knee, Vector3 foot) SolveIK(Vector3 hip, Vector3 target, float a, float b, Vector3 pole)
    {
        Vector3 d = target - hip;
        float len = d.Length();
        Vector3 u = len > 1e-4f ? d / len : Vector3.Down;
        float dist = Math.Clamp(len, MathF.Abs(a - b) + 0.01f, a + b - 0.001f);
        float cosA = Math.Clamp((a * a + dist * dist - b * b) / (2f * a * dist), -1f, 1f);
        float sinA = MathF.Sqrt(1f - cosA * cosA);
        Vector3 n = pole - u * pole.Dot(u);
        if (n.LengthSquared() < 1e-6f) n = (MathF.Abs(u.Y) < 0.9f ? Vector3.Up : Vector3.Forward) - u * u.Y;
        n = n.Normalized();
        return (hip + u * (a * cosA) + n * (a * sinA), hip + u * dist);
    }

    void PoseNecks(WildSim w, Creature c, float dt, Transform3D bodyT, Basis yaw, float groundMid)
    {
        Proportions b = Species.Body;
        float hs = b.HeadSize;
        bool hasNeck = _neckLen > b.Size * 0.12f;
        for (int h = 0; h < _heads.Length; h++)
        {
            float x = _heads.Length > 1 ? (h == 0 ? -1f : 1f) * b.Width * 0.22f : 0f;
            float splay = _heads.Length > 1 ? (h == 0 ? 0.35f : -0.35f) : 0f;
            Vector3 baseW = bodyT * new Vector3(x, b.Height * (hasNeck ? 0.28f : 0.12f), -b.Size * (hasNeck ? 0.42f : 0.47f));

            // Neck pitch: reach the grass when grazing, low when stalking, up when alert,
            // reared back in a wind-up, thrown forward in a lunge.
            float target = _neckRest + 0.15f * c.Alert;
            if (hasNeck && Graze > 0.05f)
            {
                float groundY = w.Terrain.Height(baseW.X, baseW.Z) + hs * 0.35f;
                float reachDown = MathF.Asin(Math.Clamp((groundY - baseW.Y) / _neckLen, -0.97f, 0.97f));
                target = Mathf.Lerp(target, MathF.Max(reachDown, -1.25f), Graze);
            }
            target = Mathf.Lerp(target, -0.15f, Crouch) + 0.55f * Rearing - 0.7f * Lunging - 0.9f * Dead;
            if (h == 0) _neckPitch = Geo.Smooth(_neckPitch, target, 4f, dt);

            Vector3 p = baseW;
            if (hasNeck && h < _necks.Length)
            {
                int[] chain = _necks[h];
                float seg = _neckLen / chain.Length;
                for (int i = 0; i < chain.Length; i++)
                {
                    float t = (i + 0.5f) / chain.Length;
                    float ang = _neckPitch + (_neckPitch > 0.3f ? 0.45f : 0.15f) * (0.5f - t);
                    float yawI = (LookYaw + splay) * (i + 1) / (chain.Length + 1);
                    Vector3 dir = bodyT.Basis * Geo.Forward(yawI, ang);
                    Pose[chain[i]] = Geo.Segment(p, p + dir * seg, bodyT.Basis.X);
                    p += dir * seg;
                }
            }
            float headPitch = Math.Clamp(_neckPitch * 0.35f, -0.9f, 0.4f) - Graze * 0.5f - Lunging * 0.2f + MathF.Sin(c.Age * 2.1f + h) * 0.03f;
            Basis headB = yaw * new Basis(Vector3.Up, LookYaw + splay) * new Basis(Vector3.Right, headPitch) * new Basis(Vector3.Back, Twitch(c) * 0.3f);
            Pose[_heads[h]] = new Transform3D(headB, p);
        }
    }

    void PoseTails(Creature c, float dt, Transform3D bodyT)
    {
        Proportions b = Species.Body;
        for (int t = 0; t < _tails.Length; t++)
        {
            int[] chain = _tails[t];
            float seg = _tailLen / chain.Length;
            Vector3 p = bodyT * new Vector3(_tails.Length > 1 ? (t == 0 ? -0.15f : 0.15f) * b.Width : 0f, b.Height * 0.1f, b.Size * 0.47f);
            float yawAcc = _tails.Length > 1 ? (t == 0 ? -0.3f : 0.3f) : 0f;
            float pitchAcc = -0.3f + 0.35f * c.Alert * (Species.Stats.Hunts ? 1f : 0.4f) + 0.3f * Rearing - 0.6f * Dead;
            float speed = WildSim.Flat(c.Velocity).Length();
            float amp = speed > 0.3f ? 0.12f : 0.28f;
            float lag = Math.Clamp(-YawRate * 0.3f, -0.7f, 0.7f);
            for (int i = 0; i < chain.Length; i++)
            {
                yawAcc += MathF.Sin(c.Age * 2.2f - i * 0.8f + c.Id) * amp / chain.Length * 2f + lag / chain.Length;
                pitchAcc -= 0.06f;
                Vector3 dir = bodyT.Basis * Geo.Backward(yawAcc, pitchAcc);
                Pose[chain[i]] = Geo.Segment(p, p + dir * seg, bodyT.Basis.X);
                p += dir * seg;
            }
        }
    }

    void PoseLimbs(Creature c, Transform3D bodyT)
    {
        if (_limbs.Length == 0) return;
        Proportions b = Species.Body;
        string kind = Species.Part("limbs");
        float len = kind == "feelers" ? b.Size * 0.45f : b.Size * 0.3f;
        for (int l = 0; l < _limbs.Length; l++)
        {
            int[] chain = _limbs[l];
            float s = l == 0 ? -1f : 1f;
            Vector3 p = bodyT * new Vector3(s * b.Width * 0.36f, -b.Height * 0.02f, -b.Size * 0.36f);
            float sway = MathF.Sin(c.Age * (kind == "feelers" ? 2.6f : 1.3f) + l * 1.7f) * 0.2f;
            Vector3 upper = kind == "feelers" ? new Vector3(s * 0.4f, 0.55f + sway, -0.8f) : new Vector3(s * 0.3f, -0.55f + sway * 0.4f, -0.75f);
            upper = upper.Lerp(new Vector3(s * 0.2f, 0.9f, -0.35f), Rearing).Lerp(new Vector3(s * 0.1f, -0.2f, -1f), Lunging);
            Vector3 lower = kind == "scythes" ? new Vector3(0, -0.9f, 0.3f) : new Vector3(0, -0.6f + sway, -0.8f);
            lower = lower.Lerp(new Vector3(0, 0.2f, -1f), Lunging);
            Vector3 d1 = (bodyT.Basis * upper).Normalized(), d2 = (bodyT.Basis * lower).Normalized();
            for (int i = 0; i < chain.Length; i++)
            {
                Vector3 d = i == 0 ? d1 : d2;
                Pose[chain[i]] = Geo.Segment(p, p + d * len * 0.5f, bodyT.Basis.X);
                p += d * len * 0.5f;
            }
        }
    }

    void PoseTendrils(Creature c, Transform3D bodyT)
    {
        if (_tendrils.Length == 0) return;
        Proportions b = Species.Body;
        Transform3D anchorFrame = _chinTendrils && _heads.Length > 0 ? Pose[_heads[0]] : bodyT;
        float hs = b.HeadSize;
        float len = _chinTendrils ? hs * 1.4f : b.Size * 0.5f;
        Vector3 drag = -c.Velocity * 0.05f;
        for (int t = 0; t < _tendrils.Length; t++)
        {
            int[] chain = _tendrils[t];
            float spread = (t - (_tendrils.Length - 1) * 0.5f) / Math.Max(1, _tendrils.Length - 1);
            Vector3 p, dir;
            if (_chinTendrils)
            {
                p = anchorFrame * new Vector3(spread * hs * 0.5f, -hs * 0.25f, -hs * 0.75f);
                dir = anchorFrame.Basis * new Vector3(spread * 0.4f, -1f, -0.2f);
            }
            else
            {
                float z = Mathf.Lerp(-0.3f, 0.3f, (t % 6) / 5f) * b.Size;
                float side = t % 2 == 0 ? -1f : 1f;
                p = anchorFrame * new Vector3(side * b.Width * 0.2f, Parts.Top(z, b.Height, b.Size) * 0.9f, z);
                dir = anchorFrame.Basis * new Vector3(side * 0.6f, 1f, 0.3f);
            }
            dir = dir.Normalized();
            float seg = len / chain.Length;
            for (int i = 0; i < chain.Length; i++)
            {
                var sway = new Vector3(MathF.Sin(c.Age * 1.3f + t * 1.7f + i * 0.6f), 0f, MathF.Cos(c.Age * 1.1f + t * 2.1f + i * 0.5f)) * 0.3f;
                dir = (dir + Vector3.Down * 0.4f + drag + sway).Normalized();
                Pose[chain[i]] = Geo.Segment(p, p + dir * seg, bodyT.Basis.X);
                p += dir * seg;
            }
        }
    }

    /// <summary>The Bloom makes its hosts jerk: a twitch that fires every second or so.</summary>
    float Twitch(Creature c)
    {
        if (!Species.Corrupted || c.Dead) return 0f;
        float t = c.Age * 1.3f + c.Id * 0.37f;
        float spike = MathF.Max(0f, MathF.Sin(t * 5.1f) * MathF.Sin(t * 1.7f) - 0.6f) * 2.5f;
        return spike * MathF.Sin(c.Age * 40f);
    }

    static Vector3 Ground(WildSim w, Vector3 p) => new(p.X, w.Terrain.Height(p.X, p.Z), p.Z);

    // ---- Layout -----------------------------------------------------------------------

    static RigLayout Build(Species s)
    {
        var layout = new RigLayout();
        var k = new Paint(s);
        var rng = Rng.For(s.Seed, "layout");
        Proportions b = s.Body;
        float L = b.Size, W = b.Width, H = b.Height, hs = b.HeadSize;

        var body = new Piece("body");
        BodyShape(body, s, k, rng);
        Parts.Back(body, s.Part("back"), W, H, L, k, rng);
        if (s.Corrupted) Parts.Corruption(body, W, H, L, k, rng);
        if (s.Part("head") == "none")
        {
            // Headless forms still look at you: a ring of eyes round the top.
            for (int i = 0; i < 5; i++)
            {
                float a = i * MathF.Tau / 5f;
                Parts.Eye(body, k, new Vector3(MathF.Cos(a) * W * 0.42f, H * 0.18f, MathF.Sin(a) * W * 0.42f), W * 0.09f);
            }
        }
        layout.Add(body);

        string headKind = s.Part("head");
        int heads = headKind == "twin" ? 2 : headKind == "none" ? 0 : 1;
        bool hasNeck = b.NeckLength > L * 0.12f;
        int neckSegs = hasNeck ? Math.Clamp((int)MathF.Round(b.NeckLength / (hs * 0.9f)), 2, 4) : 0;
        for (int h = 0; h < heads; h++)
        {
            var head = new Piece("head");
            Parts.Head(head, headKind == "twin" ? "snout" : headKind, hs, k, rng);
            Parts.Horns(head, s.Part("horns"), hs, k, rng);
            layout.Add(head);
            float d0 = MathF.Min(W * 0.32f, hs * 0.7f);
            for (int i = 0; i < neckSegs; i++)
            {
                float seg = b.NeckLength / neckSegs;
                float d = d0 * (1f - 0.3f * i / neckSegs);
                var neck = new Piece($"neck{h}_{i}");
                neck.Add(Shape.Capsule, new Vector3(d, seg + d, d), new Vector3(0, seg * 0.5f, 0), k.Base, 0.8f);
                layout.Add(neck);
            }
        }

        string tailKind = s.Part("tail");
        int tails = tailKind == "twin" ? 2 : 1;
        if (b.TailLength > L * 0.08f)
        {
            int segs = Math.Clamp((int)MathF.Round(b.TailLength / (W * 0.5f)), 2, 5);
            float seg = b.TailLength / segs;
            for (int t = 0; t < tails; t++)
            for (int i = 0; i < segs; i++)
            {
                float d = W * (tailKind == "whip" || tailKind == "twin" ? 0.28f : 0.36f) * (1f - 0.7f * i / segs);
                var piece = new Piece($"tail{t}_{i}");
                piece.Add(Shape.Capsule, new Vector3(d, seg + d, d), new Vector3(0, seg * 0.5f, 0), k.Base, 0.8f);
                if (i == segs - 1) Parts.TailTip(piece, tailKind, seg, W, k);
                layout.Add(piece);
            }
        }

        for (int i = 0; i < b.Legs; i++) AddLeg(layout, s, k, i);

        string limbs = s.Part("limbs");
        if (limbs != "none")
        {
            float len = (limbs == "feelers" ? L * 0.45f : L * 0.3f) * 0.5f;
            for (int l = 0; l < 2; l++)
            for (int i = 0; i < 2; i++)
            {
                var piece = new Piece($"limb{l}_{i}") { Shadow = false };
                float d = limbs == "feelers" ? W * 0.05f : W * (i == 0 ? 0.12f : 0.09f);
                piece.Add(Shape.Capsule, new Vector3(d, len + d, d), new Vector3(0, len * 0.5f, 0), i == 0 ? k.Base : k.Dark, 0.5f);
                if (i == 1)
                {
                    if (limbs == "scythes")
                        piece.Add(Shape.Cone, new Vector3(W * 0.12f, len * 1.3f, 0), new Vector3(0, len * 1.3f, -len * 0.25f), k.Bone, 0f, 0f, new Basis(Vector3.Right, -0.35f));
                    else if (limbs == "feelers")
                        piece.Add(Shape.Sphere, Vector3.One * W * 0.1f, new Vector3(0, len, 0), k.EyeGlow > 0 ? k.Glow : k.Accent, 0f, k.EyeGlow);
                    else
                        piece.Rod(new Vector3(0, len, 0), new Vector3(0, len * 1.35f, -len * 0.15f), W * 0.07f, k.Dark, 0, 0, cone: true);
                }
                layout.Add(piece);
            }
        }

        string tendrils = s.Part("tendrils");
        if (tendrils != "none")
        {
            bool chin = tendrils is "chin" or "whiskers";
            // Three segments each are plenty to read as tendrils, and they are the most numerous pieces.
            int count = chin ? 4 : 5;
            float len = chin ? hs * 1.4f : L * 0.5f;
            int segs = 3;
            for (int t = 0; t < count; t++)
            for (int i = 0; i < segs; i++)
            {
                var piece = new Piece($"tendril{t}_{i}") { Shadow = false };
                float seg = len / segs, d = (chin ? hs * 0.06f : W * 0.06f) * (1f - 0.5f * i / segs);
                bool glow = tendrils == "glowing" || s.Corrupted;
                piece.Add(Shape.Capsule, new Vector3(d, seg + d, d), new Vector3(0, seg * 0.5f, 0),
                    glow && i == segs - 1 ? k.Veins : k.Base.Lerp(k.Accent, 0.3f), 0f, glow ? (i == segs - 1 ? 1f : 0.3f) : 0f);
                layout.Add(piece);
            }
        }
        return layout;
    }

    static void BodyShape(Piece body, Species s, Paint k, Rng rng)
    {
        Proportions b = s.Body;
        float L = b.Size, W = b.Width, H = b.Height;
        switch (s.Form)
        {
            case "skitterer":
                body.Add(Shape.Sphere, new Vector3(W * 0.7f, H * 0.75f, L * 0.45f), new Vector3(0, 0, -L * 0.22f), k.Base, 0.7f);
                body.Add(Shape.Sphere, new Vector3(W * 1.05f, H * 1.15f, L * 0.62f), new Vector3(0, H * 0.12f, L * 0.22f), k.Base, 1f);
                body.Add(Shape.Sphere, new Vector3(W * 0.8f, H * 0.5f, L * 0.5f), new Vector3(0, -H * 0.15f, L * 0.2f), k.Belly);
                break;
            case "tripod":
                body.Add(Shape.Sphere, new Vector3(W, H, L * 0.9f), Vector3.Zero, k.Base, 1f);
                body.Add(Shape.Cylinder, new Vector3(W * 1.08f, H * 0.12f, W * 1.08f), new Vector3(0, -H * 0.08f, 0), k.Accent, 0f, 0f, null, 14);
                for (int i = 0; i < 6; i++)
                {
                    float a = i * MathF.Tau / 6f;
                    body.Add(Shape.Sphere, Vector3.One * W * 0.08f, new Vector3(MathF.Cos(a) * W * 0.4f, -H * 0.42f, MathF.Sin(a) * W * 0.4f), k.Glow, 0f, k.GlowStrength > 0 ? 1f : 0.3f);
                }
                break;
            case "titanstilt":
                body.Add(Shape.Sphere, new Vector3(W * 1.25f, H * 0.6f, L), Vector3.Zero, k.Base, 1f);
                body.Add(Shape.Sphere, new Vector3(W * 1.1f, H * 0.35f, L * 0.85f), new Vector3(0, -H * 0.2f, 0), k.Belly);
                for (int i = 0; i < 10; i++)
                {
                    float a = i * MathF.Tau / 10f;
                    var at = new Vector3(MathF.Cos(a) * W * 0.6f, -H * 0.15f, MathF.Sin(a) * L * 0.48f);
                    body.Rod(at, at + Vector3.Down * H * 0.5f, W * 0.04f, k.Accent, 0f, k.GlowStrength * 0.6f);
                }
                break;
            case "walkingplant":
                body.Add(Shape.Sphere, new Vector3(W * 0.95f, H * 1.0f, W * 0.95f), Vector3.Zero, k.Base, 1f);
                body.Add(Shape.Cylinder, new Vector3(W * 0.6f, H * 0.3f, W * 0.9f), new Vector3(0, -H * 0.4f, 0), k.Base.Darkened(0.3f), 0.5f, 0f, null, 10);
                break;
            default:
                body.Add(Shape.Sphere, new Vector3(W, H, L), Vector3.Zero, k.Base, 1f);
                body.Add(Shape.Sphere, new Vector3(W * 0.88f, H * 0.6f, L * 0.88f), new Vector3(0, -H * 0.18f, 0), k.Belly);
                if (s.Form is "longneck" or "colossus")
                    body.Add(Shape.Sphere, new Vector3(W * 0.85f, H * 0.7f, L * 0.45f), new Vector3(0, H * 0.25f, -L * 0.18f), k.Base, 1f);
                if (s.Form == "stalker")
                    body.Add(Shape.Sphere, new Vector3(W * 0.95f, H * 0.85f, L * 0.4f), new Vector3(0, H * 0.12f, -L * 0.3f), k.Base, 1f);
                break;
        }
    }

    static void AddLeg(RigLayout layout, Species s, Paint k, int i)
    {
        Proportions b = s.Body;
        float upper = b.LegLength * 0.52f, lower = b.LegLength * 0.56f, t = b.LegThickness;
        Color legColor = s.Form == "walkingplant" ? k.Base.Darkened(0.35f) : k.Base.Darkened(0.12f);
        var up = new Piece($"leg{i}u");
        up.Add(Shape.Capsule, new Vector3(t, upper + t, t), new Vector3(0, upper * 0.5f, 0), legColor, 0.6f);
        layout.Add(up);
        var lo = new Piece($"leg{i}l") { Shadow = b.Size > 1.2f };
        float tl = t * 0.72f;
        lo.Add(Shape.Capsule, new Vector3(tl, lower + tl, tl), new Vector3(0, lower * 0.5f, 0), legColor.Darkened(0.1f), 0.4f);
        layout.Add(lo);
        var foot = new Piece($"leg{i}f") { Shadow = false };
        switch (b.LegStyle)
        {
            case LegStyle.Upright:
                foot.Add(Shape.Sphere, new Vector3(t * 1.3f, t * 0.6f, t * 1.6f), new Vector3(0, t * 0.2f, -t * 0.25f), k.Dark);
                break;
            case LegStyle.Stilt:
                for (int toe = -1; toe <= 1; toe++)
                    foot.Rod(new Vector3(0, t * 0.2f, 0), new Vector3(toe * t * 1.2f, 0.01f, -t * 2f), t * 0.4f, k.Dark);
                break;
            case LegStyle.Roots:
                for (int toe = 0; toe < 3; toe++)
                {
                    float a = toe * MathF.Tau / 3f;
                    foot.Rod(Vector3.Zero, new Vector3(MathF.Cos(a) * t * 2.5f, -t * 0.5f, MathF.Sin(a) * t * 2.5f), t * 0.5f, k.Base.Darkened(0.45f), 0, 0, cone: true);
                }
                break;
            default:
                foot.Add(Shape.Sphere, Vector3.One * t * 0.9f, new Vector3(0, t * 0.3f, 0), k.Dark);
                break;
        }
        layout.Add(foot);
    }
}
