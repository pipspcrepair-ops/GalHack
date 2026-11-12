using System;
using Godot;

namespace Wildlife;

/// <summary>What a body says without words. Each is only where the hands, the back and the head go.</summary>
public enum BodyAct { None, HandsUp, Cower, WaveOff, Beckon, Point, Shrug, Warn, Celebrate, Grieve, Panic, Fidget, Taunt, Present, Greet, Startle, Play, Thanks }

/// <summary>A blow with the body: a straight punch, an overhand swat, a Husk's two-handed seize, or its retch and spew.</summary>
public enum StrikeKind { None, Punch, Swat, Seize, Spew }

/// <summary>What someone is doing, for the rig to show. Everything a game has to say about a person's body.</summary>
public struct Gesture
{
    /// <summary>The ground under them, and which way they face.</summary>
    public Vector3 Feet;
    public float Yaw;
    /// <summary>How fast they're going, m/s, and how far they have walked (keeps the feet in step with the ground).</summary>
    public float Speed, Walked;
    /// <summary>0..1: sitting; bent to work; rifle up; crouched; arms thrown out at something; bent over retching; down dead.</summary>
    public float Sit, Work, Aim, Crouch, Reach, Retch, Down;
    /// <summary>Which way the head is turned from the body, radians.</summary>
    public float Look;
    /// <summary>A gesture, how far into it they are (0..1), and where it's aimed (pointing, presenting).</summary>
    public BodyAct Act;
    public float ActAmount;
    public Vector3 ActAt;
    /// <summary>Seconds, for the slow things: weight shifting, a drink, a tool coming down.</summary>
    public float Clock;
    /// <summary>The local form of the friendly gestures (a greeting, thanks): 0 a hand waved high, 1 an open palm held up, 2 a hand to the chest.</summary>
    public int Style;
    /// <summary>0..1: a drink at the lips (the held bottle tipped to the mouth); a low, hunched creep (a stalk, not a crouch).</summary>
    public float Sip, Stalk;
    /// <summary>Dead: the body goes limp and falls as a ragdoll (Down alone lays a sleeper down whole).</summary>
    public bool Dead;
    /// <summary>A blow under way and how far through it (0..1: wind-up to about 0.45, the strike to 0.65, then back).</summary>
    public StrikeKind Strike;
    public float StrikeAt;
}

/// <summary>
/// A person on two legs. Each foot's walk is worked out from how far they've gone, so feet stay
/// planted through a stride and a run is a run; two-bone IK does the knees and the elbows. Sitting,
/// bending to work, stooping and staggering are only where the hips, the torso and the hands go:
/// the legs and arms follow by the same IK. Works two ways: a game's own people call
/// <see cref="Drive"/> with a <see cref="Gesture"/>; in the sim it runs off a creature like any rig.
/// </summary>
public sealed partial class HumanRig : Rig
{
    readonly Figure _f;
    readonly int _body, _head, _prop, _string0, _string1;
    readonly Vector3[] _wrist = new Vector3[2], _knee = new Vector3[2];
    /// <summary>Where each hand is to hold the carried thing this frame, and how far it's on it (0..1): the right (1) and the left (0).</summary>
    public Vector3 GripRight, GripLeft;
    public float GripRightOn, GripLeftOn;
    /// <summary>The wrist each arm reached this frame (0 left, 1 right).</summary>
    public Vector3 WristOf(int i) => _wrist[i];
    readonly int[] _armU = new int[2], _armL = new int[2], _legU = new int[2], _legL = new int[2], _foot = new int[2];
    readonly Vector3[] _feet = new Vector3[2], _drawn = new Vector3[2];
    readonly bool[] _planted = { true, true };
    readonly float _offset;
    float _walked;
    /// <summary>The sim's view of the ground, when in the sim.</summary>
    WildSim _world;
    Creature _creature;

    public HumanRig(Species s) : base(s, s.Mechanical ? Robots.BuildAutomaton : HumanBody.Build)
    {
        _f = s.Figure;
        var pieces = Layout.Pieces;
        _body = pieces.FindIndex(p => p.Name == "body");
        _head = pieces.FindIndex(p => p.Name == "head");
        _prop = pieces.FindIndex(p => p.Name == "prop");
        _string0 = pieces.FindIndex(p => p.Name == "string0");
        _string1 = pieces.FindIndex(p => p.Name == "string1");
        for (int i = 0; i < 2; i++)
        {
            int side = i;
            _armU[i] = pieces.FindIndex(p => p.Name == $"arm{side}u");
            _armL[i] = pieces.FindIndex(p => p.Name == $"arm{side}l");
            _legU[i] = pieces.FindIndex(p => p.Name == $"leg{side}u");
            _legL[i] = pieces.FindIndex(p => p.Name == $"leg{side}l");
            _foot[i] = pieces.FindIndex(p => p.Name == $"leg{side}f");
        }
        int bones = s.Mechanical ? 0 : FolkBody.TailBones(_f);
        _tail = new int[bones];
        for (int i = 0; i < bones; i++)
        {
            int bone = i;
            _tail[i] = pieces.FindIndex(p => p.Name == $"tail{bone}");
        }
        _offset = (s.Seed % 97) / 97f;
        BuildShell();
    }

    readonly int[] _tail;

    /// <summary>
    /// A tail on bones from the back of the hips: carried out behind and down, swinging with the
    /// stride, curling a little at rest, lying along the seat when sitting; a Husk's hangs slack and
    /// jerks with it. Never below the ground.
    /// </summary>
    void PoseTail(in Gesture g, Vector3 hip, Basis torso, float cycle, float moving, float jerk)
    {
        if (_tail.Length == 0 || _tail[0] < 0) return;
        Figure f = _f;
        Folk k = f.Folk;
        float len = FolkBody.TailLength(f), seg = len / _tail.Length, r = FolkBody.TailRoot(f);
        Vector3 back = torso.Z, side = torso.X;
        Vector3 at = hip + back * f.ChestD * f.Padding * 0.85f + torso.Y * f.Torso * 0.04f;
        // How far it droops from level behind: a thick tail is carried, a thin one hangs; slack on a Husk, lower sitting.
        float droop = (k?.Tail switch { "thick" => 0.25f, "fan" => 0.05f, "bushy" => 0.3f, "stub" => 0.1f, _ => 0.5f }) + (f.Husk ? 0.45f : 0f) + g.Sit * 0.5f + g.Crouch * 0.2f;
        float swing = moving * 0.32f * MathF.Sin(cycle) + (1f - moving) * 0.14f * MathF.Sin(g.Clock * 0.9f + _offset * 11f) + 0.5f * jerk * MathF.Sin(_offset * 41f);
        for (int i = 0; i < _tail.Length; i++)
        {
            float bend = droop + i * (f.Husk ? 0.28f : 0.16f) - (1f - moving) * i * 0.06f;
            Vector3 dir = (back * MathF.Cos(bend) - Vector3.Up * MathF.Sin(bend)).Rotated(Vector3.Up, swing * (0.6f + 0.5f * i)).Normalized();
            Vector3 to = at + dir * seg;
            float ground = GroundAt(to.X, to.Z, g.Feet.Y) + r * 0.8f;
            if (to.Y < ground) to.Y = ground;
            Pose[_tail[i]] = Geo.Segment(at, to, side, seg);
            at = to;
        }
    }

    public override int FootCount => 2;
    public override bool FootPlanted(int i) => _planted[i];
    public override Vector3 FootTarget(int i) => _feet[i];
    public override Vector3 FootDrawn(int i) => _drawn[i];

    public override void Reset(WildSim w, Creature c) => Update(w, c, 0f);

    public override void Update(WildSim w, Creature c, float dt)
    {
        SmoothIntents(c, dt);
        _world = w;
        _creature = c;
        float speed = c.Dead ? 0f : WildSim.Flat(c.Velocity).Length();
        _walked += speed * dt;
        // A new hit: the struck part snaps away from it.
        if (c.HitSerial != _hitSeen)
        {
            _hitSeen = c.HitSerial;
            if (Pose[_body].Basis.Y.LengthSquared() > 0.5f) Struck(c.HitPoint, c.HitDir, c.HitHeavy);
        }
        // A blow: wind-up, strike and recovery, read off the attack's own phases.
        StrikeKind blow = StrikeKind.None;
        float at = 0f;
        if (!c.Dead && c.Phase is AttackPhase.Windup or AttackPhase.Lunge or AttackPhase.Recover)
        {
            bool spew = c.Species.Husk && c.Species.Spit != SpitKind.None && Spitting.Wants(c) && !c.Seizing;
            blow = spew ? StrikeKind.Spew : BlowOf(c.Species, c.Id);
            float k = c.PhaseLength > 0f ? Math.Clamp(c.PhaseTime / c.PhaseLength, 0f, 1f) : 1f;
            at = c.Phase switch
            {
                AttackPhase.Windup => 0.44f * k,
                AttackPhase.Lunge => 0.45f + 0.15f * k,
                _ => 0.6f + 0.4f * Math.Clamp(c.PhaseTime / 0.5f, 0f, 1f),
            };
        }
        // Creeping up on something is a low, hunched stalk; only standing still (hiding, waiting) is a full crouch.
        float low = MathF.Max(Crouch, Hidden), stalk = 0f;
        if (speed > 0.25f && Hidden < 0.3f)
        {
            stalk = low;
            low = Hidden;
        }
        Drive(new Gesture
        {
            Feet = c.Position,
            Yaw = c.Yaw,
            Speed = speed,
            Walked = _walked,
            Crouch = low,
            Stalk = stalk,
            // A Husk holding a head keeps its hands on it; anyone held by one struggles.
            Reach = c.Phase == AttackPhase.Hold ? 1f : c.HeldBy != null ? 0.6f * Rearing : 0f,
            Retch = c.Attacking && blow != StrikeKind.Spew && c.Species.Spit != SpitKind.None && c.Species.Attack.Style != AttackStyle.Seize ? Rearing : 0f,
            Strike = blow,
            StrikeAt = at,
            Look = LookYaw,
            Down = Dead,
            Dead = c.Dead,
            Clock = c.Age,
        });
        Vector3 chest = ChestPoint;
        Vector3 head = HeadPoint;
        SetHit(0, chest, 0.32f);
        SetHit(1, head, Species.Body.HeadSize * 0.6f);
        SetHit(2, (Pose[_legU[0]].Origin + _drawn[0]) * 0.5f, 0.2f);
        SetHit(3, (Pose[_legU[1]].Origin + _drawn[1]) * 0.5f, 0.2f);
        SetBounds(chest, _f.Height * 0.65f);
        BodyClearance = Pose[_body].Origin.Y - c.Position.Y;
    }

    int _hitSeen;

    /// <summary>Where the chest and the head are now, which way the body faces and its right side (stagings, tests).</summary>
    public Vector3 ChestPoint => Pose[_body].Origin + Pose[_body].Basis.Y * _f.Torso * 0.65f;
    public Vector3 HeadPoint => Pose[_head].Origin + Pose[_head].Basis.Y * (_f.Neck + _f.HeadRadius);
    public Vector3 Facing => -Pose[_body].Basis.Z.Normalized();
    public Vector3 Side => Pose[_body].Basis.X.Normalized();

    float GroundAt(float x, float z, float fallback) => _world != null ? GroundY(_world, _creature, x, z) : fallback;

    /// <summary>Poses the body for what someone is doing now. No nodes, nothing allocated.</summary>
    public void Drive(in Gesture g)
    {
        Tick(g.Clock);
        if (g.Dead)
        {
            // Dead from the first: stood up once, so the fall starts from a body.
            if (!_jointsKnown)
            {
                Gesture up = g;
                up.Dead = false;
                up.Down = 0f;
                DriveAlive(up);
            }
            DriveDead(g);
            return;
        }
        _doll = null;
        DriveAlive(g);
    }

    void DriveAlive(in Gesture g)
    {
        Figure f = _f;
        float stalk = Math.Clamp(g.Stalk, 0f, 1f);
        float run = Math.Clamp((g.Speed - 2f) / 1.4f, 0f, 1f);
        float moving = Math.Clamp(g.Speed / 0.45f, 0f, 1f);
        float strideLength = f.LegLength * Mathf.Lerp(1.36f, 2.5f, run) * (1f - 0.35f * stalk);
        float phase = g.Walked / strideLength + _offset;
        // A Husk is worked like a puppet on a bad string: its stride hitches (one step quick, the
        // next dragged), and a sudden jerk runs through it now and then, each its own.
        bool husk = f.Husk;
        float jerk = 0f;
        if (husk)
        {
            phase += 0.09f * MathF.Sin(MathF.Tau * phase * 0.5f) * moving;
            jerk = MathF.Pow(MathF.Max(0f, MathF.Sin(g.Clock * 1.3f + _offset * 40f)), 24f) + 0.6f * MathF.Pow(MathF.Max(0f, MathF.Sin(g.Clock * 2.9f + _offset * 17f)), 30f);
        }
        float cycle = MathF.Tau * phase;

        Basis turn = new(Vector3.Up, g.Yaw);
        Vector3 fwd = turn * Vector3.Forward, right = turn * Vector3.Right;

        // A hit: the snap of the struck part, and on a heavy one a stagger a step along it.
        float snap = _hitAge < 1f ? Envelope(_hitAge) : 0f, stagger = Stagger;
        Vector3 hitFlat = new Vector3(_hitDir.X, 0f, _hitDir.Z);
        hitFlat = hitFlat.LengthSquared() > 1e-4f ? hitFlat.Normalized() : -fwd;
        StrikeBody(g, out float blowTwist, out float blowPitch, out float blowForward, out float blowDown);
        float act = g.Act == BodyAct.None ? 0f : Mathf.SmoothStep(0f, 1f, Math.Clamp(g.ActAmount, 0f, 1f)); // eased in and out, held at the top
        float keyed = act;

        // Hips: over the feet, dropping as a step lands; low in a crouch, on the seat when sitting.
        float bob = moving * (0.012f + 0.024f * run) * f.Height * MathF.Cos(cycle * 2f);
        float slump = g.Act switch { BodyAct.Cower => 0.5f, BodyAct.Grieve => 0.18f, BodyAct.Startle => 0.15f, _ => 0f } * Math.Clamp(g.ActAmount, 0f, 1f);
        float hop = g.Act == BodyAct.Play ? act * 0.035f * f.Height * MathF.Abs(MathF.Sin(g.Clock * 4.5f)) : 0f;
        float hipY = f.LegLength * (0.99f - 0.022f * moving - 0.07f * run - 0.42f * MathF.Max(g.Crouch, slump) - 0.24f * stalk) + bob + hop - blowDown * f.Height / 1.72f;
        if (_hitPart is Part.LegL or Part.LegR) hipY -= 0.07f * snap * f.LegLength;
        hipY = Mathf.Lerp(hipY, f.LowerLeg + 0.05f, g.Sit);
        float sway = moving * 0.018f * f.Height * MathF.Sin(cycle);
        float staggerSide = f.Stagger * (0.09f * MathF.Sin(phase * 2.3f + g.Clock * 0.7f) + 0.03f * MathF.Sin(g.Clock * 2.1f));
        float idle = (1f - moving) * 0.012f * MathF.Sin(g.Clock * 0.7f + _offset * 6f);
        if (g.Act == BodyAct.Fidget) idle += act * 0.035f * MathF.Sin(g.Clock * 0.32f * MathF.PI + _offset * 3f); // weight from foot to foot
        Vector3 recoil = g.Act == BodyAct.Startle ? -fwd * 0.16f * keyed + Vector3.Up * 0.03f * keyed : g.Act is BodyAct.HandsUp or BodyAct.Cower ? -fwd * 0.06f * keyed : Vector3.Zero;
        recoil += fwd * blowForward * f.Height / 1.72f + hitFlat * (0.05f * snap + 0.24f * stagger);
        Vector3 hip = g.Feet + Vector3.Up * hipY + right * (sway + staggerSide + idle) + recoil;

        // The torso leans into a run and over work, bows with age and drink, rolls with the stride.
        float pitch = 0.05f + 0.26f * run + f.Stoop * 0.55f + g.Work * (0.5f + 0.2f * MathF.Sin(g.Clock * 1.7f)) + g.Crouch * 0.5f + g.Retch * 0.8f - g.Sit * 0.1f - g.Aim * 0.03f + g.Reach * 0.3f + stalk * 0.62f + blowPitch;
        pitch += act * g.Act switch
        {
            BodyAct.Cower => 0.55f, BodyAct.Grieve => 0.45f, BodyAct.Taunt => 0.12f, BodyAct.Startle => -0.28f, BodyAct.Warn => -0.08f, BodyAct.HandsUp => -0.06f,
            BodyAct.Celebrate => -0.1f, BodyAct.Present => 0.1f, BodyAct.Beckon => 0.05f,
            BodyAct.Thanks => g.Style switch { 1 => 0.32f, 2 => 0.5f, _ => 0.18f } * (0.75f + 0.25f * MathF.Sin(g.Clock * 1.6f)),
            BodyAct.Greet when g.Style == 2 => 0.22f,
            _ => 0f,
        };
        float roll = -sway * 1.2f + staggerSide * 2.2f;
        float twist = moving * (0.1f + 0.1f * run) * MathF.Sin(cycle) + blowTwist;
        if (g.Act == BodyAct.WaveOff) twist -= act * 0.25f; // turned half away from them
        // A weapon up: the left shoulder comes round toward the target (an archer's side-on stance).
        HoldKind kind = Weapons.HoldOf(f.Prop);
        float squareOff = 0f; // the upper body turned to a weapon (the hips and legs stay square)
        float strikeOn = g.Strike == StrikeKind.None ? 0f : 1f;
        float busy = Math.Clamp(MathF.Max(MathF.Max(act, g.Reach), strikeOn), 0f, 1f);
        float aimW = kind == HoldKind.None ? 0f : Math.Clamp(g.Aim, 0f, 1f) * (1f - busy);
        squareOff -= aimW * kind switch { HoldKind.Bow => 1.15f, HoldKind.Spear => 0.3f, _ => 0f };
        if (kind is HoldKind.LongGun or HoldKind.Spear or HoldKind.Pistol or HoldKind.Bow) twist *= 1f - 0.8f * aimW; // steady on the target, even walking
        if (kind == HoldKind.LongGun)
        {
            // A long gun in both hands, carried or up; a short reach past a thick chest turns further, the left shoulder to the fore-end.
            float ua = HumanBody.ArmScale(f), armL = f.UpperArmOf(0) + f.ForearmOf(0);
            float lack = f.ChestD * f.Padding * 1.05f + 0.5f * ua - (armL * 0.97f + Reach * ua);
            squareOff -= (0.3f + Math.Clamp(lack / (f.ShoulderW * 0.94f), 0f, 0.5f)) * (1f - busy);
        }
        if (husk)
        {
            // The jerk throws the shoulders one way and the back the other; the torso hangs off-true.
            roll += 0.28f * jerk * MathF.Sin(_offset * 31f) + 0.06f * MathF.Sin(g.Clock * 0.7f + _offset * 9f);
            pitch += 0.18f * jerk + 0.05f * MathF.Sin(g.Clock * 0.45f + _offset * 5f);
            twist += 0.2f * jerk * MathF.Cos(_offset * 23f);
            // Reaching for a head, it straightens to bring its face to yours.
            pitch = Mathf.Lerp(pitch, 0.08f, 0.8f * g.Reach);
        }
        Basis torso = turn * new Basis(Vector3.Up, twist + squareOff) * new Basis(Vector3.Right, -pitch) * new Basis(Vector3.Back, roll);
        Vector3 hipsX = (turn * new Basis(Vector3.Up, twist) * new Basis(Vector3.Right, -pitch) * new Basis(Vector3.Back, roll)).X;
        // Struck: the upper body thrown away from the hit, the more the closer to the chest it landed.
        Vector3 hitAxis = Vector3.Up.Cross(hitFlat);
        float throwBack = snap * (_hitPart switch { Part.Chest => 0.32f, Part.Head => 0.12f, Part.LegL or Part.LegR => 0.1f, _ => 0.08f }) * (0.6f + _hitHeavy) + 0.3f * stagger;
        if (throwBack > 0f && hitAxis.LengthSquared() > 1e-4f) torso = new Basis(hitAxis.Normalized(), throwBack) * torso;
        Pose[_body] = new Transform3D(torso, hip);
        PoseTail(g, hip, torso, cycle, moving, jerk);

        // Legs: a foot on the ground moves back under the body at walking speed, one in the air comes through high.
        float duty = Mathf.Lerp(0.62f, 0.4f, run);
        int stepper = hitFlat.Dot(right) > 0f ? 1 : 0;
        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? -1f : 1f;
            float t = phase + i * 0.5f;
            t -= MathF.Floor(t);
            float reach, lift;
            bool stance = t < duty;
            if (stance)
            {
                reach = 1f - 2f * t / duty;
                lift = 0f;
            }
            else
            {
                float s = (t - duty) / (1f - duty);
                reach = s * s * (3f - 2f * s) * 2f - 1f;
                lift = MathF.Sin(s * MathF.PI);
            }
            float half = strideLength * duty * 0.5f;
            float forward = moving * reach * half + g.Sit * f.UpperLeg * 0.8f + g.Crouch * 0.05f * side + stalk * 0.08f * side;
            Vector3 hipJoint = hip + hipsX * side * f.HipW * 0.55f + Vector3.Down * f.HipW * 0.1f;
            float wide = g.Act is BodyAct.Taunt or BodyAct.Warn or BodyAct.Startle or BodyAct.Panic ? 0.12f * keyed : 0f; // a wide stance for a threat or a fright
            wide += stalk * 0.06f;
            Vector3 foot = g.Feet + recoil * 0.6f + fwd * forward + right * side * (f.HipW * 0.7f + staggerSide * 0.6f + g.Sit * 0.04f + g.Crouch * 0.06f + wide);
            // Staggered: one foot steps out along the hit to catch the weight, the other follows a little.
            float stepLift = 0f;
            if (stagger > 0f)
            {
                foot += hitFlat * stagger * (i == stepper ? 0.34f : 0.1f);
                if (i == stepper && _hitAge < 0.32f) stepLift = 0.08f * MathF.Sin(_hitAge / 0.32f * MathF.PI) * f.LegLength;
            }
            float ground = GroundAt(foot.X, foot.Z, g.Feet.Y);
            // The heel comes up as a foot trails behind; the swing clears the ground.
            float heel = stance ? MathF.Max(0f, -reach - 0.25f) * 0.22f * f.LegLength : 0f;
            // A Husk drags one foot and throws the other up too high.
            float step = husk ? (i == (_offset > 0.5f ? 1 : 0) ? 0.3f : 1.7f) : 1f;
            foot.Y = ground + f.Ankle + moving * (heel + lift * step * f.LegLength * Mathf.Lerp(0.13f, 0.34f, run) * (1f - 0.5f * stalk)) + stepLift;
            _feet[i] = foot;
            bool grounded = stance || moving <= 0f;
            if (grounded && !_planted[i]) Steps++;
            _planted[i] = grounded;
            (Vector3 knee, Vector3 drawn) = LeggedRig.SolveIK(hipJoint, foot, f.UpperLeg, f.LowerLeg, fwd + right * side * 0.12f);
            _knee[i] = knee;
            _drawn[i] = drawn;
            Pose[_legU[i]] = Geo.Segment(hipJoint, knee, right);
            Pose[_legL[i]] = Geo.Segment(knee, drawn, right);
            float toe = stance ? (reach > 0f ? 0.25f * reach : 0.3f * reach) * moving : -0.35f * moving * lift;
            Pose[_foot[i]] = new Transform3D(turn * new Basis(Vector3.Right, toe), drawn);
        }

        Vector3 neck = hip + torso.Y * f.Torso;
        Vector3 chest = hip + torso.Y * f.Torso * 0.9f;
        Vector3 down = -torso.Y, ahead = -torso.Z;
        bool longGun = kind == HoldKind.LongGun;

        // A drink: now and then from the bottle, or when asked (a sip): the head tips back to it.
        float sip = 0f;
        if (f.Prop == "bottle")
            sip = MathF.Max(Math.Clamp(MathF.Sin(g.Clock * 0.33f + _offset * 9f) * 5f - 3.6f, 0f, 1f) * (1f - moving), Math.Clamp(g.Sip, 0f, 1f)) * (1f - busy);

        // The head turns on its own, and stays nearer level than the back it sits on.
        StrikeCurve(g.StrikeAt, out float blowWind, out float blowHit);
        float headPitch = pitch * 0.35f + g.Retch * 0.5f + (longGun ? aimW * 0.3f : 0f) + act * (g.Act switch { BodyAct.Grieve => 0.5f, BodyAct.Taunt => 0.12f, BodyAct.Thanks => 0.15f, _ => 0f }) - 0.4f * sip - 0.5f * stalk;
        if (g.Strike == StrikeKind.Spew) headPitch += 0.35f * blowWind * MathF.Abs(MathF.Sin(g.Clock * 13f)) - 0.35f * blowHit;
        float headRoll = longGun ? -0.14f * aimW : 0f; // down to the sights, the cheek toward the stock
        if (g.Act == BodyAct.Shrug) headRoll += act * 0.18f;
        if (husk)
        {
            // The head lolls: held at its wrong angle, rolling slowly round it, snapping with each jerk.
            headRoll = f.HeadTilt + 0.22f * MathF.Sin(g.Clock * 0.8f + _offset * 13f) + 0.4f * jerk * MathF.Sin(_offset * 57f);
            // The head is held up off the hunched back (it watches you), dropping now and then; reaching, it lifts to the face it wants.
            headPitch = pitch * 0.15f + f.HeadDroop + 0.1f * MathF.Sin(g.Clock * 0.55f + _offset * 3f) + g.Retch * 0.5f - 0.35f * stalk;
            headPitch = Mathf.Lerp(headPitch, -0.2f, Math.Clamp(MathF.Max(g.Reach, g.Strike == StrikeKind.Seize ? blowWind + blowHit : 0f), 0f, 1f));
        }
        float headYaw = g.Look + staggerSide + (g.Act == BodyAct.WaveOff ? act * 0.45f : 0f);
        Basis headBasis = turn * new Basis(Vector3.Up, headYaw) * new Basis(Vector3.Right, -headPitch) * new Basis(Vector3.Back, headRoll);
        if (_hitPart == Part.Head && snap > 0f && hitAxis.LengthSquared() > 1e-4f) headBasis = new Basis(hitAxis.Normalized(), snap * 0.65f * (0.6f + _hitHeavy)) * headBasis;
        Pose[_head] = new Transform3D(headBasis, neck);

        // Arms: each hand has a place to be, and the elbow follows it.
        Hold hold = Grips(g, kind, aimW, busy, turn, torso, hip, chest, neck, headBasis);
        GripRight = hold.R;
        GripLeft = hold.L;
        GripRightOn = hold.RW;
        GripLeftOn = hold.LW;
        int doer = Doer(g, hold.RW > 0f, chest, torso);
        float shrug = g.Act == BodyAct.Shrug ? act * (0.03f + 0.03f * MathF.Max(0f, MathF.Sin(g.Clock * 3f))) * f.Height / 1.72f : 0f;
        Vector3 mouth = neck + headBasis.Y * (f.Neck + f.HeadRadius * 0.55f) - headBasis.Z * f.HeadRadius * 0.95f;
        float rightOff = 0f; // how far the right hand is off its usual hold (a gesture, a blow), for what it carries
        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? -1f : 1f;
            var F = new Frame(torso, side);
            float upperArm = f.UpperArmOf(i), forearm = f.ForearmOf(i), arm = upperArm + forearm;
            Vector3 shoulder = chest + torso.X * side * f.ShoulderW * 0.94f + torso.Y * shrug;
            // A Husk's arms don't keep time with its legs: one swings late and loose, the other leads, reaching.
            float lag = husk ? (i == 0 ? 1.4f : -0.5f) : 0f;
            float swing = (i == 0 ? 1f : -1f) * MathF.Sin(cycle - lag) * moving * (0.38f + 0.5f * run) * (husk ? 1.5f : 1f) * (1f - 0.5f * stalk) + 0.04f + 0.3f * run + 0.35f * stalk;
            if (husk) swing += 0.3f * jerk * side + (i == f.LongArm ? 0.25f * moving : 0f);
            Vector3 hang = (down * MathF.Cos(swing) + ahead * MathF.Sin(swing)).Normalized();
            float bend = 0.95f - 0.28f * run - 0.12f * stalk;
            Vector3 hand = shoulder + hang * arm * bend + torso.X * side * (0.03f + 0.04f * (f.Padding - 1f) * 2f);
            Vector3 pole = torso.Z + down * 0.2f + torso.X * side * 0.3f;
            if (g.Work > 0f)
            {
                float beat = MathF.Sin(g.Clock * 1.7f);
                Vector3 work = chest + ahead * (0.34f + 0.1f * beat) * f.Height / 1.72f + down * (0.34f - 0.22f * beat) + torso.X * side * 0.1f;
                hand = hand.Lerp(work, g.Work);
            }
            if (f.Prop == "umbrella" && i == 1)
            {
                hand = shoulder + F.V(0.05f, -0.75f, 0.65f) * upperArm + F.V(-0.35f, 0.25f, 0.9f) * forearm;
                pole = F.V(0.3f, -0.9f, -0.1f);
            }
            if (f.Prop == "cane" && i == 1)
                hand = chest + down * 0.5f + ahead * (0.26f + moving * 0.08f * MathF.Sin(cycle)) + torso.X * 0.18f;
            if (sip > 0f && i == 1)
            {
                // The bottle's neck to the lips, its foot tipped up: the hand before the face, the elbow down and out.
                Vector3 axis = (headBasis.Z * 0.72f - headBasis.Y * 0.69f).Normalized();
                Vector3 grip = mouth - axis * 0.19f * HumanBody.Scale(f);
                hand = hand.Lerp(grip, sip);
                pole = pole.Lerp(F.V(0.6f, -0.78f, 0.15f), sip).Normalized();
            }
            // Reaching: a Husk's hands go up and in, for a head; anyone else's out in front.
            if (g.Reach > 0f) hand = hand.Lerp(husk ? shoulder + ahead * arm * 0.8f + Vector3.Up * 0.24f + torso.X * side * -0.16f : shoulder + ahead * arm * 0.9f + Vector3.Up * 0.05f + torso.X * side * -0.04f, g.Reach);
            if (g.Sit > 0f && !longGun) hand = hand.Lerp(hip + ahead * 0.32f + torso.X * side * 0.14f + Vector3.Up * 0.03f, g.Sit);
            // Crouched, the forearms rest over the knees, the hands hanging just past them.
            if (g.Crouch > 0f) hand = hand.Lerp(_knee[i] + fwd * 0.1f * f.Height / 1.72f + torso.X * side * -0.02f - Vector3.Up * 0.04f, g.Crouch);
            // Stalking, the arms hang loose and forward, ready.
            if (stalk > 0f) pole = pole.Lerp(F.V(0.5f, -0.4f, -0.8f), stalk).Normalized();
            // A hand on a weapon stays on it; the other does the gesture (a one-handed one done by the free hand).
            bool keeps = i == 1 ? hold.RW > 0f : kind == HoldKind.Bow;
            if (act > 0f && !keeps)
            {
                ArmGoal a = ActArm(g, i, i == doer, shoulder, neck, torso, headBasis, upperArm, forearm);
                if (a.On)
                {
                    hand = hand.Lerp(a.Hand, act);
                    pole = pole.Lerp(a.Pole, act).Normalized();
                    if (i == 1) rightOff = MathF.Max(rightOff, act);
                }
            }
            float on = i == 1 ? hold.RW : hold.LW;
            if (on > 0f)
            {
                hand = hand.Lerp(i == 1 ? hold.R : hold.L, on);
                // Holding something out in front, the elbow hangs down and a little out; drawing a bow, the elbow up and back along the arrow.
                Vector3 holdPole = kind == HoldKind.Bow && i == 1 ? (hold.R - hold.L).Normalized() + Vector3.Up * 0.12f + torso.X * 0.1f : Vector3.Down + torso.X * side * 0.5f + torso.Z * 0.2f;
                pole = pole.Lerp(holdPole.Normalized(), on).Normalized();
            }
            ArmGoal blow = StrikeArm(g, i, shoulder, torso, upperArm, forearm, out float blowOn);
            if (blowOn > 0f && blow.On)
            {
                hand = hand.Lerp(blow.Hand, blowOn);
                pole = pole.Lerp(blow.Pole, blowOn).Normalized();
                if (i == 1) rightOff = MathF.Max(rightOff, blowOn);
            }
            // Struck on this arm, it is knocked along the hit; a heavy hit throws both arms out for balance.
            if (_hitPart == (i == 0 ? Part.ArmL : Part.ArmR)) hand += _hitDir * 0.26f * snap * (0.6f + _hitHeavy);
            hand += (F.Out * 0.14f + F.Up * 0.1f) * stagger;
            if (husk && g.Reach < 0.5f && act <= 0f && blowOn <= 0f) pole = (pole + torso.X * side * 0.6f + Vector3.Up * 0.3f * jerk).Normalized(); // elbows out, slack
            // Reaching out to a weapon, the shoulder comes forward as far as it must (a little).
            float need = MathF.Max(0f, shoulder.DistanceTo(hand) - arm * 0.98f);
            if (on > 0f && need > 0f) shoulder += (hand - shoulder).Normalized() * MathF.Min(need, Reach * HumanBody.ArmScale(f)) * on;
            (Vector3 elbow, Vector3 wrist) = Arm(Pose[_body], shoulder, hand, pole, upperArm, forearm, F.Out, ahead);
            Pose[_armU[i]] = Geo.Segment(shoulder, elbow, torso.X);
            Pose[_armL[i]] = Geo.Segment(elbow, wrist, torso.X);
            _wrist[i] = wrist;
            _elbow[i] = elbow;
            _shoulder[i] = shoulder;
        }
        if (_prop >= 0) Pose[_prop] = PropPose(g, kind, hold, aimW, torso, fwd, right, sip, mouth, headBasis, rightOff);
        if (_string0 >= 0 && _string1 >= 0)
        {
            // The string from each limb tip to the nock: straight at rest, drawn to the right hand.
            Transform3D bow = Pose[_prop];
            Vector3 nock = (bow * new Vector3(0f, 0f, HumanBody.BowTip(f, 1).Z)).Lerp(_wrist[1], hold.RW);
            Pose[_string0] = Geo.Segment(bow * HumanBody.BowTip(f, 1), nock, right, 1f);
            Pose[_string1] = Geo.Segment(bow * HumanBody.BowTip(f, -1), nock, right, 1f);
        }

        KeepJoints(hip, neck, torso, headBasis);
        if (g.Down > 0f)
        {
            // Laid down (asleep): the whole body goes over about the feet, backwards.
            var fall = new Transform3D(new Basis(right, g.Down * MathF.PI * 0.49f), g.Feet);
            Transform3D lay = fall * new Transform3D(Basis.Identity, -g.Feet);
            for (int i = 0; i < Pose.Length; i++) Pose[i] = lay * Pose[i];
        }
    }

    readonly Vector3[] _elbow = new Vector3[2], _shoulder = new Vector3[2];
    /// <summary>Each arm's shoulder, elbow and wrist as last posed (0 left, 1 right), for the tests.</summary>
    public (Vector3 shoulder, Vector3 elbow, Vector3 wrist) ArmOf(int i) => (_shoulder[i], _elbow[i], _wrist[i]);

    /// <summary>Where each hand holds a weapon (R right, L left), how far each is on it, and where the weapon is.</summary>
    struct Hold
    {
        public Vector3 R, L;
        public float RW, LW;
        public Transform3D Prop;
    }

    /// <summary>
    /// How a weapon is held, worked out from the body each frame so every build's hands land on
    /// it. A long gun: two hands, the stock in the shoulder pocket, carried low-ready (muzzle down,
    /// ahead and across, clear of the body) or shouldered on the look; one-handed beside the leg
    /// while the other hand is busy. A pistol: holstered at the hip, drawn low, then up in both
    /// hands. A spear: upright in the right hand, both hands and level when wary. A bow: in the
    /// left hand, drawn to the jaw. A club or machete: hanging head-down, raised when wary.
    /// </summary>
    Hold Grips(in Gesture g, HoldKind kind, float aim, float busy, Basis turn, Basis torso, Vector3 hip, Vector3 chest, Vector3 neck, Basis head)
    {
        Figure f = _f;
        var h = new Hold();
        if (kind == HoldKind.None) return h;
        float u = HumanBody.ArmScale(f), T = f.Torso, d = f.ChestD * f.Padding;
        Vector3 fwd = turn * Vector3.Forward, right = turn * Vector3.Right, ahead = -torso.Z, up = torso.Y;
        Vector3 look = turn * new Basis(Vector3.Up, Math.Clamp(g.Look, -0.6f, 0.6f)) * Vector3.Forward;
        Vector3 shoulderR = chest + torso.X * f.ShoulderW * 0.94f, shoulderL = chest - torso.X * f.ShoulderW * 0.94f;
        float armR = f.UpperArmOf(1) + f.ForearmOf(1), armL = f.UpperArmOf(0) + f.ForearmOf(0);
        Vector3 grip, dir;
        switch (kind)
        {
            case HoldKind.LongGun:
            {
                Vector3 lowButt = hip + up * T * 0.74f + torso.X * f.ShoulderW * f.Padding * 0.45f + ahead * (d * 1.1f + 0.035f * u);
                Vector3 lowDir = (fwd * 0.62f + Vector3.Down * (0.72f - 0.45f * g.Sit) - right * 0.32f).Normalized();
                Vector3 aimButt = hip + up * T * 0.86f + torso.X * f.ShoulderW * f.Padding * 0.45f + ahead * (d * 1.05f + 0.05f * u);
                Vector3 aimDir = (look - right * 0.04f).Normalized();
                dir = lowDir.Lerp(aimDir, aim).Normalized();
                grip = lowButt.Lerp(aimButt, aim) + dir * 0.3f * u;
                // A short reach (thick clothes, short arms): the gun comes across and up toward the
                // left shoulder, never back into the body, until the fore-end is in hand.
                grip = Across(grip, dir, 0.15f * u, shoulderL, armL * 0.97f + Reach * u, ahead);
                // Still short: the stock presses back into a thick coat (it gives), never into the chest under it.
                float give = MathF.Max(0f, (grip - dir * 0.3f * u - hip).Dot(ahead) - f.ChestD * (1f + 0.4f * (f.Padding - 1f)) * 1.03f);
                float shortR = grip.DistanceTo(shoulderR) - (armR * 0.97f + Reach * u), shortL = (grip + dir * 0.15f * u).DistanceTo(shoulderL) - (armL * 0.97f + Reach * u);
                if (MathF.Max(shortR, shortL) > 0f) grip -= ahead * MathF.Min(give, MathF.Max(shortR, shortL) * 1.2f);
                // The other hand busy: carried in the right, muzzle down beside the leg.
                Vector3 carry = hip + up * T * 0.3f + torso.X * (f.ShoulderW * f.Padding + 0.07f * u) + ahead * 0.04f * u;
                grip = grip.Lerp(carry, busy);
                dir = dir.Lerp((Vector3.Down * 0.8f + fwd * 0.6f).Normalized(), busy).Normalized();
                h.R = grip;
                h.RW = 1f;
                h.L = Along(shoulderL, armL + Reach * u, grip, dir, 0.15f * u, 0.3f * u);
                h.LW = 1f - busy;
                h.Prop = new Transform3D(Geo.AlongY(dir, right), grip);
                break;
            }
            case HoldKind.Pistol:
            {
                // Holstered on the right hip, muzzle down, the grip back and up.
                Vector3 holster = hip + up * T * 0.12f + torso.X * (MathF.Max(f.HipW, f.WaistW) * f.Padding + 0.06f * u) + ahead * 0.02f * u;
                Vector3 holsterDir = (Vector3.Down + fwd * 0.12f).Normalized();
                float drawn = Math.Clamp((aim - 0.15f) / 0.85f, 0f, 1f);
                Vector3 low = hip + up * T * 0.45f + torso.X * 0.12f * u + ahead * (d + 0.2f * u);
                Vector3 lowDir = (fwd * 0.6f + Vector3.Down * 0.8f).Normalized();
                Vector3 raised = chest + Vector3.Up * 0.02f * u + right * 0.03f * u + look * armR * 0.85f;
                Vector3 off = raised - shoulderR;
                if (off.Length() > armR * 0.95f) raised = shoulderR + off.Normalized() * armR * 0.95f;
                float clear = Math.Min(1f, drawn * 2.5f);
                grip = holster.Lerp(low, clear).Lerp(raised, drawn);
                dir = holsterDir.Lerp(lowDir, clear).Lerp(look, drawn).Normalized();
                h.R = grip;
                h.RW = aim > 0.15f ? 1f : aim / 0.15f;
                h.L = grip - right * 0.045f * u + Vector3.Down * 0.03f * u;
                h.LW = Mathf.SmoothStep(0.5f, 1f, drawn);
                h.Prop = new Transform3D(Geo.AlongY(dir, aim > 0.15f ? right : torso.X), grip);
                break;
            }
            case HoldKind.Spear:
            {
                Vector3 staff = shoulderR + Vector3.Down * armR * 0.82f + ahead * 0.12f * u + torso.X * 0.07f * u;
                Vector3 staffDir = (Vector3.Up + fwd * 0.08f).Normalized();
                Vector3 level = hip + up * T * 0.4f + torso.X * 0.15f * u + ahead * (d + 0.08f * u);
                Vector3 levelDir = (look - right * 0.24f + Vector3.Up * 0.08f).Normalized();
                // Swung out and round from upright to level, the butt clear of the hip on the way.
                grip = staff.Lerp(level, aim) + (torso.X * 0.1f + ahead * 0.08f) * u * MathF.Sin(MathF.PI * aim);
                dir = staffDir.Lerp(levelDir, aim).Normalized();
                if (aim > 0f) grip = grip.Lerp(Across(grip, dir, 0.16f * u, shoulderL, armL * 0.97f + Reach * u, ahead), aim);
                h.R = grip;
                h.RW = 1f;
                h.L = Along(shoulderL, armL + Reach * u, grip, dir, 0.16f * u, 0.45f * u);
                h.LW = aim;
                h.Prop = new Transform3D(Geo.AlongY(dir, right), grip);
                break;
            }
            case HoldKind.Bow:
            {
                // Hanging in the left hand, string side in; drawn: the bow arm straight at the target,
                // the bow upright across the arrow's line, the string hand back to the cheek along it.
                Vector3 hang = shoulderL + Vector3.Down * armL * 0.84f + ahead * 0.14f * u - torso.X * 0.09f * u;
                Vector3 hangDir = (Vector3.Up * 0.95f + fwd * 0.2f - right * 0.12f).Normalized();
                Vector3 held = shoulderL + look * armL * 0.96f + Vector3.Up * 0.02f * u;
                Vector3 cheek = neck + head.Y * (f.Neck + f.HeadRadius * 0.55f) + head.X * f.HeadRadius * 0.85f - head.Z * f.HeadRadius * 0.35f;
                Vector3 arrow = (held - cheek).Normalized();
                Vector3 heldDir = (Vector3.Up - arrow * Vector3.Up.Dot(arrow)).Normalized() + right * 0.08f;
                grip = hang.Lerp(held, aim);
                dir = hangDir.Lerp(heldDir.Normalized(), aim).Normalized();
                Vector3 facing = fwd.Lerp(arrow, aim).Normalized();
                h.L = grip;
                h.LW = 1f;
                h.R = cheek;
                h.RW = aim;
                h.Prop = new Transform3D(Geo.AlongY(dir, dir.Cross(facing)), grip);
                break;
            }
            case HoldKind.Blade:
            {
                // Hanging clear of the hip, head down; raised before the shoulder, swinging wide of the body between.
                Vector3 hangs = shoulderR + Vector3.Down * armR * 0.84f + torso.X * MathF.Max(0.17f * u, MathF.Max(f.HipW, f.WaistW) * f.Padding - f.ShoulderW * 0.94f + 0.12f * u) + ahead * 0.06f * u;
                Vector3 raised = shoulderR + Vector3.Up * 0.02f * u + ahead * 0.28f * u + torso.X * 0.05f * u;
                float wide = MathF.Sin(MathF.PI * aim);
                h.R = hangs.Lerp(raised, aim) + (torso.X * 0.1f + ahead * 0.12f) * u * wide;
                h.RW = 1f;
                break;
            }
        }
        return h;
    }

    /// <summary>How far a shoulder comes forward to reach out to a held weapon, m for a 1.72 m human's arms.</summary>
    const float Reach = 0.06f;

    /// <summary>A weapon held out of the left hand's reach comes across and up toward the left shoulder (never back into the body) until its near grip is in reach.</summary>
    static Vector3 Across(Vector3 grip, Vector3 dir, float near, Vector3 shoulder, float reach, Vector3 ahead)
    {
        for (int k = 0; k < 8; k++)
        {
            Vector3 at = grip + dir * near;
            float shortBy = at.DistanceTo(shoulder) - reach;
            if (shortBy <= 0f) break;
            Vector3 to = shoulder - at;
            to -= ahead * to.Dot(ahead);
            if (to.LengthSquared() < 1e-6f) break;
            grip += to.Normalized() * shortBy * 1.3f;
        }
        return grip;
    }

    /// <summary>The farthest point of a grip span (near..far along the weapon from the grip) the arm reaches: a short arm takes the near end.</summary>
    static Vector3 Along(Vector3 shoulder, float arm, Vector3 grip, Vector3 dir, float near, float far)
    {
        for (int k = 0; k <= 8; k++)
        {
            Vector3 p = grip + dir * Mathf.Lerp(far, near, k / 8f);
            if (p.DistanceTo(shoulder) <= arm * 0.97f) return p;
        }
        return grip + dir * near;
    }

    /// <summary>
    /// Where the carried thing goes: a weapon where its hold puts it; anything else turns with the
    /// hand that grips it (a bottle stands in a fist held level and tips to the lips to drink, an
    /// umbrella stands up out of the fist), and a cane keeps its tip on the ground unless the hand is busy.
    /// </summary>
    Transform3D PropPose(in Gesture g, HoldKind kind, in Hold h, float aim, Basis torso, Vector3 fwd, Vector3 right, float sip, Vector3 mouth, Basis head, float busy)
    {
        Figure f = _f;
        Vector3 hand = _wrist[1];
        if (kind is HoldKind.LongGun or HoldKind.Pistol or HoldKind.Spear or HoldKind.Bow) return h.Prop;
        // The fist: the forearm's line, and the thumb up off it (forward when the arm hangs).
        Vector3 along = (_wrist[1] - _elbow[1]).Normalized();
        Vector3 thumb = Vector3.Up - along * along.Dot(Vector3.Up);
        if (thumb.LengthSquared() < 0.04f) thumb = -torso.Z - along * along.Dot(-torso.Z);
        thumb = thumb.Normalized();
        // The hand piece's fist is a little short of the wrist.
        Vector3 fist = hand - along * _f.ForearmOf(1) * 0.05f;
        if (kind == HoldKind.Blade)
        {
            // Hanging head-down and forward; up and ready when wary; in a blow it follows the arm.
            Vector3 blade = (Vector3.Down * 0.7f + fwd * 0.7f - right * 0.08f).Normalized().Lerp((Vector3.Up + fwd * 0.25f).Normalized(), aim).Normalized();
            blade = blade.Lerp(thumb, busy).Normalized();
            return new Transform3D(Geo.AlongY(blade, right), fist);
        }
        switch (f.Prop)
        {
            case "cane":
            {
                Vector3 shaft = (Vector3.Down + fwd * 0.05f).Normalized().Lerp(-thumb, busy).Normalized();
                return new Transform3D(Geo.AlongY(shaft, right), fist);
            }
            case "hoe":
            {
                // Over the shoulder on the way; head down and working when bent to it.
                Vector3 shaft = ((Vector3.Up * 1.2f - torso.Z * 0.3f + right * 0.1f).Normalized()).Lerp((fwd + Vector3.Down * (0.8f + 0.5f * MathF.Sin(g.Clock * 1.7f))).Normalized(), g.Work).Normalized();
                return new Transform3D(Geo.AlongY(shaft, right), fist);
            }
            case "wrench":
                return new Transform3D(Geo.AlongY((along + thumb * 0.35f).Normalized(), right), fist);
            case "bottle":
            {
                // Up out of the fist; drinking, its neck to the lips and its foot tipped up.
                Vector3 axis = (head.Z * 0.72f - head.Y * 0.69f).Normalized();
                Vector3 up = thumb.Lerp(axis, sip).Normalized();
                Vector3 at = fist.Lerp(mouth - axis * 0.19f * HumanBody.Scale(f), sip * sip);
                return new Transform3D(Geo.AlongY(up, along.Cross(up).LengthSquared() > 1e-4f ? along.Cross(up) : right), at);
            }
            default:
                return new Transform3D(Geo.AlongY(thumb, right), fist);
        }
    }
}
