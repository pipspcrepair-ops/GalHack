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
        LoftProfile shape = BodyProfile(s);
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
            float spreadZ = z;
            // Many-legged skitterers carry every leg on the front body, like a spider; the
            // abdomen behind hangs free.
            if (s.Form == "skitterer" && pairs > 1) z = Mathf.Lerp(-0.3f, -0.05f, pair / (float)(pairs - 1));
            float x = side * (shape != null ? Parts.HalfWidth(shape, z * b.Size) * 0.85f : b.Width * (b.LegStyle == LegStyle.Splayed ? 0.48f : 0.4f));
            leg.Anchor = new Vector3(x, -b.Height * 0.25f, z * b.Size);
            float outward = b.LegStyle switch
            {
                LegStyle.Splayed => 0.72f, LegStyle.Roots => 0.7f, LegStyle.Stilt => 0.16f, _ => 0.08f,
            } * legLen;
            float spread = b.LegStyle is LegStyle.Splayed or LegStyle.Roots ? spreadZ * 0.6f * legLen : 0f;
            leg.Rest = side == 0 ? new Vector3(0, 0, outward) : new Vector3(side * outward, 0, spread);
            // Knees: forward on front legs and back on hind ones for upright walkers, backwards
            // like a bird's on stilts, and up-and-out like a spider's for splayed legs.
            leg.Pole = b.LegStyle switch
            {
                LegStyle.Splayed or LegStyle.Roots => new Vector3(side == 0 ? 0 : side * 0.6f, 1f, side == 0 ? 0.6f : 0f),
                LegStyle.Stilt => new Vector3(0, 0.2f, 1f),
                // Two-legged walkers get a bird's backward knee: an upright biped reads as a person.
                _ => n == 2 ? new Vector3(0, 0.2f, 1f) : z < 0f ? new Vector3(0, 0, -1f) : new Vector3(0, 0, 1f),
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
        _c = c;
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
        _c = c;
        float gF = Ground(w, pos + fwd * L * 0.35f).Y, gB = Ground(w, pos - fwd * L * 0.35f).Y;
        float groundMid = (gF + gB) * 0.5f;

        // Body: lowered by crouching, hiding and death, bobbing twice per stride.
        _gait += speed * dt / MathF.Max(0.15f, legLen * 0.9f);
        float hip = _hipRest * (1f - 0.38f * Crouch - 0.3f * Hidden - 0.08f * Bracing);
        float bob = MathF.Sin(_gait * MathF.Tau * 2f) * 0.035f * legLen * MathF.Min(1f, speed / walk);
        float breathe = MathF.Sin(c.Age * 1.7f + c.Id) * 0.01f * H;
        float bodyY = groundMid + hip + H * 0.25f + bob + breathe;
        float deadY = groundMid + H * 0.5f;
        bodyY = Mathf.Lerp(bodyY, deadY, Dead);
        float pitch = MathF.Atan2(gF - gB, 0.7f * L) + Rearing * 0.35f - Lunging * 0.15f + Graze * 0.04f - Bracing * 0.1f;
        float roll = Math.Clamp(-YawRate * speed * 0.03f, -0.15f, 0.15f) + Dead * 1.35f + Twitch(c) * 0.08f;
        // A charge's wind-up rocks the body back and forth, like a bull pawing the ground.
        float paw = Bracing * MathF.Sin(c.Age * 9f) * 0.035f * L;
        Vector3 lunge = fwd * (Lunging * 0.22f * L - Rearing * 0.08f * L + paw);
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
                float groundY = Ground(w, baseW).Y + hs * 0.35f;
                float reachDown = MathF.Asin(Math.Clamp((groundY - baseW.Y) / _neckLen, -0.97f, 0.97f));
                target = Mathf.Lerp(target, MathF.Max(reachDown, -1.25f), Graze);
            }
            // A charge drops the head to lead with the horns; a long neck has further to drop.
            float brace = _neckRest > 0.5f ? 1.25f : 0.65f;
            target = Mathf.Lerp(target, -0.15f, Crouch) + 0.55f * Rearing - 0.7f * Lunging - 0.9f * Dead - brace * Bracing;
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
            float headPitch = Math.Clamp(_neckPitch * 0.35f, -0.9f, 0.4f) - Graze * 0.5f - Lunging * 0.2f - Bracing * 0.35f + MathF.Sin(c.Age * 2.1f + h) * 0.03f;
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

    /// <summary>The creature being posed: in a cave or a place its ground is the floor under it.</summary>
    Creature _c;
    Vector3 Ground(WildSim w, Vector3 p) => new(p.X, _c?.Space != null ? GroundY(w, _c, p.X, p.Z) : w.Terrain.Height(p.X, p.Z), p.Z);

    // ---- Layout -----------------------------------------------------------------------

    static RigLayout Build(Species s)
    {
        var layout = new RigLayout();
        var k = new Paint(s);
        var rng = Rng.For(s.Seed, "layout");
        Proportions b = s.Body;
        float L = b.Size, W = b.Width, H = b.Height, hs = b.HeadSize;

        var body = new Piece("body");
        LoftProfile shape = BodyProfile(s);
        if (shape != null) body.Loft(shape, k.Base, k.Belly, 1f, 0f, Mat.Skin, null, null, 16);
        BodyExtras(body, s, k);
        Parts.Back(body, s.Part("back"), W, H, L, k, rng, shape);
        if (s.Corrupted) Parts.Corruption(body, W, H, L, k, rng, shape);
        if (s.Part("head") == "none")
        {
            // Headless forms still watch you: a ring of small eyes round the top.
            for (int i = 0; i < 6; i++)
            {
                float a = i * MathF.Tau / 6f;
                Parts.Eye(body, k, new Vector3(MathF.Cos(a) * W * 0.4f, H * 0.2f, MathF.Sin(a) * W * 0.4f), W * 0.06f);
            }
        }
        layout.Add(body);

        string headKind = s.Part("head");
        int heads = headKind == "twin" ? 2 : headKind == "none" ? 0 : 1;
        bool hunts = s.Behaviour is Behaviour.Predator or Behaviour.PackHunter or Behaviour.Ambusher;
        string drawnHead = headKind == "twin" ? (hunts ? "jaws" : "snout") : headKind;
        bool hasNeck = b.NeckLength > L * 0.12f;
        int neckSegs = hasNeck ? Math.Clamp((int)MathF.Round(b.NeckLength / (hs * 0.9f)), 2, 4) : 0;
        // The neck swells into the shoulders and narrows to the skull, so there is no visible joint.
        float rBase = MathF.Min(W * 0.3f, H * 0.34f) * (heads > 1 ? 0.75f : 1f), rTop = MathF.Min(hs * 0.27f, rBase);
        for (int h = 0; h < heads; h++)
        {
            var head = new Piece("head");
            HeadShape shapeInfo = Parts.Head(head, drawnHead, hs, k, rng);
            Parts.Horns(head, s.Part("horns"), hs, k, rng, shapeInfo);
            layout.Add(head);
            for (int i = 0; i < neckSegs; i++)
            {
                float seg = b.NeckLength / neckSegs;
                float r0 = Mathf.Lerp(rBase, rTop, i / (float)neckSegs), r1 = Mathf.Lerp(rBase, rTop, (i + 1) / (float)neckSegs);
                var neck = new Piece($"neck{h}_{i}");
                neck.Taper(Vector3.Zero, new Vector3(0, seg, 0), r0, r1, k.Base, 0.8f);
                layout.Add(neck);
            }
        }

        string tailKind = s.Part("tail");
        int tails = tailKind == "twin" ? 2 : 1;
        if (b.TailLength > L * 0.08f)
        {
            int segs = Math.Clamp((int)MathF.Round(b.TailLength / (W * 0.45f)), 3, 6);
            float seg = b.TailLength / segs;
            float r0 = W * (tailKind == "club" ? 0.24f : 0.2f) * (tails > 1 ? 0.75f : 1f);
            float rTip = W * (tailKind is "whip" or "twin" or "none" ? 0.02f : tailKind == "club" ? 0.1f : 0.06f);
            for (int t = 0; t < tails; t++)
            for (int i = 0; i < segs; i++)
            {
                // Each segment ends where the next begins, at the same radius: one smooth tail.
                float ra = Mathf.Lerp(r0, rTip, MathF.Pow(i / (float)segs, 0.8f)), rb = Mathf.Lerp(r0, rTip, MathF.Pow((i + 1) / (float)segs, 0.8f));
                var piece = new Piece($"tail{t}_{i}") { Shadow = ra > 0.04f };
                piece.Taper(Vector3.Zero, new Vector3(0, seg, 0), ra, rb, k.Base, 0.8f);
                if (i == segs - 1) Parts.TailTip(piece, tailKind, seg, W, k);
                layout.Add(piece);
            }
        }

        bool clawed = hunts || s.Behaviour == Behaviour.Scavenger;
        for (int i = 0; i < b.Legs; i++) AddLeg(layout, s, k, i, clawed);

        string limbs = s.Part("limbs");
        if (limbs != "none")
        {
            float len = (limbs == "feelers" ? L * 0.45f : L * 0.3f) * 0.5f;
            for (int l = 0; l < 2; l++)
            for (int i = 0; i < 2; i++)
            {
                var piece = new Piece($"limb{l}_{i}") { Shadow = false };
                float r0 = limbs == "feelers" ? W * 0.03f : W * (i == 0 ? 0.075f : 0.055f), r1 = limbs == "feelers" ? W * 0.02f : W * (i == 0 ? 0.055f : 0.04f);
                piece.Taper(Vector3.Zero, new Vector3(0, len, 0), r0, r1, i == 0 ? k.Base : k.Base.Darkened(0.25f), 0.5f, 0f, limbs == "feelers" ? Mat.Carapace : Mat.Skin);
                if (i == 1)
                {
                    if (limbs == "scythes")
                        piece.Horn(k.Bone, W * 0.06f, Mat.Horn, new Vector3(0, len, 0), new Vector3(0, len * 1.5f, -len * 0.15f), new Vector3(0, len * 2f, -len * 0.55f), new Vector3(0, len * 2.1f, -len * 0.95f));
                    else if (limbs == "feelers")
                        piece.Taper(new Vector3(0, len, 0), new Vector3(0, len * 1.4f, -len * 0.1f), r1, r1 * 0.2f, k.Dark, 0f, k.EyeGlow, Mat.Carapace);
                    else
                        for (int c = -1; c <= 1; c++)
                            piece.Horn(k.Horn, W * 0.025f, Mat.Horn, new Vector3(c * r1 * 0.7f, len, 0), new Vector3(c * r1 * 1.2f, len * 1.25f, -len * 0.08f), new Vector3(c * r1, len * 1.4f, -len * 0.25f));
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
                float seg = len / segs, r = chin ? hs * 0.03f : W * 0.022f;
                float r0 = r * (1f - 0.55f * i / segs), r1 = r * (1f - 0.55f * (i + 1) / segs);
                bool glow = tendrils == "glowing" || s.Corrupted;
                piece.Taper(Vector3.Zero, new Vector3(0, seg, 0), r0, r1,
                    glow && i == segs - 1 ? k.Veins : k.Base.Lerp(k.Accent, 0.3f), 0f, glow ? (i == segs - 1 ? 1f : 0.3f) : 0f);
                layout.Add(piece);
            }
        }
        return layout;
    }

    /// <summary>
    /// The body's loft, from front to back: shoulders, waist and haunches shaped for what the
    /// creature does. Grazers are deep-bellied barrels, hunters deep-chested with a tucked
    /// waist, giants carry a hump of shoulder muscle, skitterers are a front body and a
    /// swollen abdomen joined at a waist. Section sizes are halves of the body's width and
    /// height. Null for forms drawn without a loft.
    /// </summary>
    public static LoftProfile BodyProfile(Species s)
    {
        Proportions b = s.Body;
        float L = b.Size, W = b.Width, H = b.Height;
        bool hunter = s.Behaviour is Behaviour.Predator or Behaviour.PackHunter or Behaviour.Ambusher;
        (float t, float w, float h, float y)[] keys = s.Form switch
        {
            "walkingplant" => null,
            "skitterer" => new[] { (0f, 0.36f, 0.3f, 0f), (0.15f, 0.46f, 0.36f, 0.02f), (0.45f, 0.4f, 0.34f, 0.04f), (0.56f, 0.2f, 0.2f, 0.06f), (0.66f, 0.44f, 0.42f, 0.12f), (0.84f, 0.56f, 0.52f, 0.16f), (1f, 0.36f, 0.34f, 0.12f) },
            "titanstilt" => new[] { (0f, 0.4f, 0.36f, 0f), (0.3f, 0.56f, 0.5f, 0.06f), (0.65f, 0.6f, 0.52f, 0.04f), (1f, 0.46f, 0.4f, 0f) },
            "tripod" => new[] { (0f, 0.42f, 0.42f, 0f), (0.5f, 0.52f, 0.5f, 0.04f), (1f, 0.42f, 0.4f, 0f) },
            "colossus" => new[] { (0f, 0.4f, 0.42f, 0.04f), (0.28f, 0.54f, 0.58f, 0.12f), (0.6f, 0.52f, 0.5f, 0.02f), (0.85f, 0.46f, 0.46f, 0.04f), (1f, 0.36f, 0.36f, 0.02f) },
            _ when hunter || s.Form == "stalker" => new[] { (0f, 0.36f, 0.42f, 0.04f), (0.25f, 0.48f, 0.55f, 0.06f), (0.58f, 0.3f, 0.34f, 0.12f), (0.82f, 0.4f, 0.4f, 0.08f), (1f, 0.3f, 0.3f, 0.06f) },
            _ => new[] { (0f, 0.36f, 0.4f, 0.02f), (0.28f, 0.5f, 0.52f, 0.06f), (0.55f, 0.52f, 0.5f, 0f), (0.8f, 0.47f, 0.47f, 0.04f), (1f, 0.36f, 0.36f, 0.04f) },
        };
        if (keys == null) return null;
        var scaled = new (float t, float w, float h, float y)[keys.Length];
        for (int i = 0; i < keys.Length; i++) scaled[i] = (keys[i].t, keys[i].w * W, keys[i].h * H, keys[i].y * H);
        return LoftProfile.Smooth(-L * 0.5f, L * 0.5f, scaled, 16, 0.2f);
    }

    /// <summary>The odd forms' extra body parts: a tripod's skirt, a titan's hanging feelers, a walking plant's bulb.</summary>
    static void BodyExtras(Piece body, Species s, Paint k)
    {
        Proportions b = s.Body;
        float L = b.Size, W = b.Width, H = b.Height;
        switch (s.Form)
        {
            case "tripod":
                body.Add(Shape.Cylinder, new Vector3(W * 1.06f, H * 0.12f, W * 0.9f), new Vector3(0, -H * 0.12f, 0), k.Accent, 0f, 0f, null, 16, Mat.Carapace);
                for (int i = 0; i < 6; i++)
                {
                    float a = i * MathF.Tau / 6f;
                    body.Add(Shape.Sphere, Vector3.One * W * 0.07f, new Vector3(MathF.Cos(a) * W * 0.4f, -H * 0.36f, MathF.Sin(a) * W * 0.4f), k.EyeColor, 0f, k.EyeGlow, null, 8, Mat.Eye);
                }
                break;
            case "titanstilt":
                for (int i = 0; i < 10; i++)
                {
                    float a = i * MathF.Tau / 10f;
                    var at = new Vector3(MathF.Cos(a) * W * 0.5f, -H * 0.12f, MathF.Sin(a) * L * 0.4f);
                    body.Taper(at, at + Vector3.Down * H * 0.6f, W * 0.025f, W * 0.008f, k.Accent, 0f, k.GlowStrength * 0.6f);
                }
                break;
            case "walkingplant":
                body.Add(Shape.Sphere, new Vector3(W * 0.95f, H * 1.0f, W * 0.95f), Vector3.Zero, k.Base, 1f, 0f, null, 14);
                body.Add(Shape.Cylinder, new Vector3(W * 0.6f, H * 0.3f, W * 0.9f), new Vector3(0, -H * 0.4f, 0), k.Base.Darkened(0.3f), 0.5f, 0f, null, 12);
                break;
        }
    }

    static void AddLeg(RigLayout layout, Species s, Paint k, int i, bool clawed)
    {
        Proportions b = s.Body;
        float upper = b.LegLength * 0.52f, lower = b.LegLength * 0.56f, t = b.LegThickness;
        bool plant = s.Form == "walkingplant";
        Color legColor = plant ? k.Base.Darkened(0.35f) : k.Base.Darkened(0.08f);
        // Thigh, knee and ankle radii. Shared radii at the knee keep the joint seamless.
        (float rHip, float rKnee, float rAnkle, float mat) = b.LegStyle switch
        {
            LegStyle.Splayed => (t * 0.6f, t * 0.42f, t * 0.06f, Mat.Carapace),
            LegStyle.Stilt => (t * 0.66f, t * 0.46f, t * 0.3f, Mat.Skin),
            LegStyle.Roots => (t * 0.7f, t * 0.45f, t * 0.2f, Mat.Skin),
            _ => (t * 0.62f, t * 0.36f, t * 0.24f, Mat.Skin),
        };
        var up = new Piece($"leg{i}u");
        up.Taper(Vector3.Zero, new Vector3(0, upper, 0), rHip, rKnee, legColor, 0.7f, 0f, mat);
        // A haunch of muscle where an upright leg meets the body.
        if (b.LegStyle == LegStyle.Upright) up.Add(Shape.Sphere, new Vector3(t * 1.5f, t * 1.9f, t * 1.7f), new Vector3(0, upper * 0.12f, 0), k.Base, 1f, 0f, null, 10);
        if (b.LegStyle == LegStyle.Splayed) up.Add(Shape.Sphere, Vector3.One * rKnee * 2.3f, new Vector3(0, upper, 0), k.Base.Darkened(0.2f), 0.3f, 0f, null, 8, Mat.Carapace);
        // Stilts get a thick muscled thigh at the hip and an armoured knee cap.
        if (b.LegStyle == LegStyle.Stilt)
        {
            up.Add(Shape.Sphere, new Vector3(t * 1.6f, t * 2.6f, t * 1.6f), new Vector3(0, upper * 0.18f, 0), k.Base, 1f, 0f, null, 10);
            up.Add(Shape.Sphere, Vector3.One * rKnee * 2.4f, new Vector3(0, upper, 0), k.Accent.Lerp(k.Base, 0.5f), 0.2f, 0f, null, 10, Mat.Carapace);
        }
        layout.Add(up);
        var lo = new Piece($"leg{i}l") { Shadow = b.Size > 1.2f };
        lo.Taper(Vector3.Zero, new Vector3(0, lower, 0), rKnee, rAnkle, legColor.Darkened(0.12f), 0.4f, 0f, mat);
        layout.Add(lo);

        // Feet sit on the ground in the creature's heading (-Z forward).
        var foot = new Piece($"leg{i}f") { Shadow = false };
        float r = rAnkle;
        switch (b.LegStyle)
        {
            case LegStyle.Upright when clawed:
                // Three hooked claws gripping the ground.
                for (int c = -1; c <= 1; c++)
                    foot.Horn(k.Horn, r * 0.5f, Mat.Horn, new Vector3(c * r * 0.6f, r * 0.7f, -r * 0.3f), new Vector3(c * r * 0.9f, r * 0.5f, -r * 1.4f), new Vector3(c * r, 0.01f, -r * 2.1f));
                foot.Add(Shape.Sphere, new Vector3(r * 2.2f, r * 1.4f, r * 2.6f), new Vector3(0, r * 0.6f, -r * 0.4f), legColor.Darkened(0.15f));
                break;
            case LegStyle.Upright:
                // A split hoof.
                for (int c = -1; c <= 1; c += 2)
                    foot.Add(Shape.Sphere, new Vector3(r * 1.3f, r * 1.5f, r * 2.2f), new Vector3(c * r * 0.5f, r * 0.62f, -r * 0.3f), k.Horn.Darkened(0.3f), 0f, 0f, null, 8, Mat.Horn);
                break;
            case LegStyle.Stilt:
                for (int toe = -1; toe <= 1; toe++)
                    foot.Horn(k.Horn, r * 0.45f, Mat.Horn, new Vector3(0, r * 0.4f, 0), new Vector3(toe * r * 1.4f, 0.02f, -r * 2.4f), new Vector3(toe * r * 1.9f, 0.005f, -r * 3.6f));
                foot.Horn(k.Horn, r * 0.4f, Mat.Horn, new Vector3(0, r * 0.4f, 0), new Vector3(0, 0.02f, r * 1.6f));
                break;
            case LegStyle.Roots:
                for (int toe = 0; toe < 4; toe++)
                {
                    float a = toe * MathF.Tau / 4f + 0.4f;
                    foot.Horn(k.Base.Darkened(0.45f), r * 1.2f, Mat.Skin, Vector3.Zero, new Vector3(MathF.Cos(a) * t * 1.5f, -t * 0.1f, MathF.Sin(a) * t * 1.5f),
                        new Vector3(MathF.Cos(a) * t * 2.6f, -t * 0.5f, MathF.Sin(a) * t * 2.6f));
                }
                break;
            default:
                // Splayed legs end in a point: the lower segment is the claw.
                foot.Add(Shape.Sphere, Vector3.One * MathF.Max(r, t * 0.1f) * 1.4f, new Vector3(0, r * 0.5f, 0), k.Dark, 0f, 0f, null, 6, Mat.Carapace);
                break;
        }
        layout.Add(foot);
    }
}
