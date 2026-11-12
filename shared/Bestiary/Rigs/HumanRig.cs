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
public sealed class HumanRig : Rig
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
        Drive(new Gesture
        {
            Feet = c.Position,
            Yaw = c.Yaw,
            Speed = speed,
            Walked = _walked,
            Crouch = MathF.Max(Crouch, Hidden),
            Reach = MathF.Max(Lunging, Rearing * (c.Species.Husk && c.Phase == AttackPhase.Windup && Spitting.Wants(c) ? 0f : 0.6f)),
            Retch = c.Attacking && c.Species.Spit != SpitKind.None && (c.Species.Attack.Style != AttackStyle.Seize || c.Phase == AttackPhase.Windup && Spitting.Wants(c)) ? Rearing : 0f,
            // (A Husk winding up to seize reaches; one bringing its maggots up retches.)
            Look = LookYaw,
            Down = Dead,
            Clock = c.Age,
        });
        Vector3 chest = Pose[_body].Origin + Vector3.Up * _f.Torso * 0.6f;
        Vector3 head = Pose[_head].Origin + Vector3.Up * (_f.Neck + _f.HeadRadius);
        SetHit(0, chest, 0.32f);
        SetHit(1, head, Species.Body.HeadSize * 0.6f);
        SetHit(2, (Pose[_legU[0]].Origin + _feet[0]) * 0.5f, 0.2f);
        SetHit(3, (Pose[_legU[1]].Origin + _feet[1]) * 0.5f, 0.2f);
        SetBounds(chest, _f.Height * 0.65f);
        BodyClearance = Pose[_body].Origin.Y - c.Position.Y;
    }

    /// <summary>Where the chest and the head are now, which way the body faces and its right side (stagings, tests).</summary>
    public Vector3 ChestPoint => Pose[_body].Origin + Pose[_body].Basis.Y * _f.Torso * 0.65f;
    public Vector3 HeadPoint => Pose[_head].Origin + Pose[_head].Basis.Y * (_f.Neck + _f.HeadRadius);
    public Vector3 Facing => -Pose[_body].Basis.Z.Normalized();
    public Vector3 Side => Pose[_body].Basis.X.Normalized();

    /// <summary>A hit lands: where (world), the way it travels, and how heavy it is (0..1, 1 a stagger).</summary>
    public void Struck(Vector3 point, Vector3 dir, float heavy) { }

    float GroundAt(float x, float z, float fallback) => _world != null ? GroundY(_world, _creature, x, z) : fallback;

    /// <summary>Poses the body for what someone is doing now. No nodes, nothing allocated.</summary>
    public void Drive(in Gesture g)
    {
        Figure f = _f;
        float run = Math.Clamp((g.Speed - 2f) / 1.4f, 0f, 1f);
        float moving = Math.Clamp(g.Speed / 0.45f, 0f, 1f);
        float strideLength = f.LegLength * Mathf.Lerp(1.36f, 2.5f, run);
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

        // Hips: over the feet, dropping as a step lands; low in a crouch, on the seat when sitting.
        float bob = moving * (0.012f + 0.024f * run) * f.Height * MathF.Cos(cycle * 2f);
        float slump = g.Act switch { BodyAct.Cower => 0.5f, BodyAct.Grieve => 0.18f, BodyAct.Startle => 0.15f, _ => 0f } * Math.Clamp(g.ActAmount, 0f, 1f);
        float hipY = f.LegLength * (0.99f - 0.022f * moving - 0.07f * run - 0.42f * MathF.Max(g.Crouch, slump)) + bob;
        hipY = Mathf.Lerp(hipY, f.LowerLeg + 0.05f, g.Sit);
        float sway = moving * 0.018f * f.Height * MathF.Sin(cycle);
        float staggerSide = f.Stagger * (0.09f * MathF.Sin(phase * 2.3f + g.Clock * 0.7f) + 0.03f * MathF.Sin(g.Clock * 2.1f));
        float idle = (1f - moving) * 0.012f * MathF.Sin(g.Clock * 0.7f + _offset * 6f);
        float keyed = g.Act == BodyAct.None ? 0f : Mathf.SmoothStep(0f, 1f, Math.Clamp(g.ActAmount, 0f, 1f)); // eased in and out, held at the top
        Vector3 recoil = g.Act == BodyAct.Startle ? -fwd * 0.16f * keyed + Vector3.Up * 0.03f * keyed : g.Act is BodyAct.HandsUp or BodyAct.Cower ? -fwd * 0.06f * keyed : Vector3.Zero;
        Vector3 hip = g.Feet + Vector3.Up * hipY + right * (sway + staggerSide + idle) + recoil;

        // The torso leans into a run and over work, bows with age and drink, rolls with the stride.
        float pitch = 0.05f + 0.26f * run + f.Stoop * 0.55f + g.Work * (0.5f + 0.2f * MathF.Sin(g.Clock * 1.7f)) + g.Crouch * 0.5f + g.Retch * 0.8f - g.Sit * 0.1f - g.Aim * 0.03f + g.Reach * 0.3f;
        float act = g.Act == BodyAct.None ? 0f : Mathf.SmoothStep(0f, 1f, Math.Clamp(g.ActAmount, 0f, 1f));
        pitch += act * g.Act switch { BodyAct.Cower => 0.55f, BodyAct.Grieve => 0.45f, BodyAct.Taunt => -0.22f, BodyAct.Startle => -0.28f, BodyAct.Warn => -0.08f, BodyAct.HandsUp => -0.06f, BodyAct.Celebrate => -0.1f, BodyAct.Present => 0.1f, BodyAct.Thanks => 0.3f, BodyAct.Greet when g.Style == 2 => 0.22f, _ => 0f };
        float roll = -sway * 1.2f + staggerSide * 2.2f;
        float twist = moving * (0.1f + 0.1f * run) * MathF.Sin(cycle);
        // A weapon up: the left shoulder comes round toward the target (an archer's side-on stance).
        HoldKind kind = Weapons.HoldOf(f.Prop);
        float squareOff = 0f; // the upper body turned to a weapon (the hips and legs stay square)
        float busy = Math.Clamp(MathF.Max(act, g.Reach), 0f, 1f);
        float aimW = kind == HoldKind.None ? 0f : Math.Clamp(g.Aim, 0f, 1f) * (1f - busy);
        squareOff -= aimW * kind switch { HoldKind.Bow => 0.75f, HoldKind.Spear => 0.3f, _ => 0f };
        if (kind is HoldKind.LongGun or HoldKind.Spear or HoldKind.Pistol) twist *= 1f - 0.8f * aimW; // steady on the target, even walking
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
        Pose[_body] = new Transform3D(torso, hip);
        PoseTail(g, hip, torso, cycle, moving, jerk);

        // Legs: a foot on the ground moves back under the body at walking speed, one in the air comes through high.
        float duty = Mathf.Lerp(0.62f, 0.4f, run);
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
            float forward = moving * reach * half + g.Sit * f.UpperLeg * 0.8f + g.Crouch * 0.05f * side;
            Vector3 hipJoint = hip + hipsX * side * f.HipW * 0.55f + Vector3.Down * f.HipW * 0.1f;
            float wide = g.Act is BodyAct.Taunt or BodyAct.Warn or BodyAct.Startle or BodyAct.Panic ? 0.12f * keyed : 0f; // a wide stance for a threat or a fright
            Vector3 foot = g.Feet + recoil * 0.6f + fwd * forward + right * side * (f.HipW * 0.7f + staggerSide * 0.6f + g.Sit * 0.04f + g.Crouch * 0.06f + wide);
            float ground = GroundAt(foot.X, foot.Z, g.Feet.Y);
            // The heel comes up as a foot trails behind; the swing clears the ground.
            float heel = stance ? MathF.Max(0f, -reach - 0.25f) * 0.22f * f.LegLength : 0f;
            // A Husk drags one foot and throws the other up too high.
            float step = husk ? (i == (_offset > 0.5f ? 1 : 0) ? 0.3f : 1.7f) : 1f;
            foot.Y = ground + f.Ankle + moving * (heel + lift * step * f.LegLength * Mathf.Lerp(0.13f, 0.34f, run));
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

        // Arms: each hand has a place to be, and the elbow follows it.
        Vector3 neck = hip + torso.Y * f.Torso;
        Vector3 chest = hip + torso.Y * f.Torso * 0.9f;
        Vector3 down = -torso.Y, ahead = -torso.Z;
        bool longGun = kind == HoldKind.LongGun;
        Hold hold = Grips(g, kind, aimW, busy, turn, torso, hip, chest, neck);
        GripRight = hold.R;
        GripLeft = hold.L;
        GripRightOn = hold.RW;
        GripLeftOn = hold.LW;
        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? -1f : 1f;
            float upperArm = f.UpperArmOf(i), forearm = f.ForearmOf(i), arm = upperArm + forearm;
            Vector3 shoulder = chest + torso.X * side * f.ShoulderW * 0.94f;
            // A Husk's arms don't keep time with its legs: one swings late and loose, the other leads, reaching.
            float lag = husk ? (i == 0 ? 1.4f : -0.5f) : 0f;
            float swing = (i == 0 ? 1f : -1f) * MathF.Sin(cycle - lag) * moving * (0.38f + 0.5f * run) * (husk ? 1.5f : 1f) + 0.04f + 0.3f * run;
            if (husk) swing += 0.3f * jerk * side + (i == f.LongArm ? 0.25f * moving : 0f);
            Vector3 hang = (down * MathF.Cos(swing) + ahead * MathF.Sin(swing)).Normalized();
            float bend = 0.95f - 0.28f * run;
            Vector3 hand = shoulder + hang * arm * bend + torso.X * side * (0.03f + 0.04f * (f.Padding - 1f) * 2f);
            if (g.Work > 0f)
            {
                float beat = MathF.Sin(g.Clock * 1.7f);
                Vector3 work = chest + ahead * (0.34f + 0.1f * beat) * f.Height / 1.72f + down * (0.34f - 0.22f * beat) + torso.X * side * 0.1f;
                hand = hand.Lerp(work, g.Work);
            }
            if (f.Prop == "umbrella" && i == 1) hand = chest + ahead * 0.24f + torso.X * 0.12f + Vector3.Up * 0.02f;
            if (f.Prop == "cane" && i == 1)
                hand = chest + down * 0.5f + ahead * (0.26f + moving * 0.08f * MathF.Sin(cycle)) + torso.X * 0.18f;
            if (f.Prop == "bottle" && i == 1)
            {
                float drink = Math.Clamp(MathF.Sin(g.Clock * 0.33f + _offset * 9f) * 5f - 3.6f, 0f, 1f) * (1f - moving);
                hand = hand.Lerp(neck + Vector3.Up * f.HeadRadius * 1.1f + ahead * 0.16f, drink);
            }
            // Reaching: a Husk's hands go up and in, for a head.
            if (g.Reach > 0f) hand = hand.Lerp(husk ? shoulder + ahead * arm * 0.8f + Vector3.Up * 0.24f + torso.X * side * -0.16f : shoulder + ahead * arm * 0.98f + Vector3.Up * 0.05f + torso.X * side * -0.06f, g.Reach);
            if (g.Sit > 0f && !longGun) hand = hand.Lerp(hip + ahead * 0.32f + torso.X * side * 0.14f + Vector3.Up * 0.03f, g.Sit);
            // Crouched, the forearms rest over the knees, the hands hanging just past them.
            if (g.Crouch > 0f) hand = hand.Lerp(_knee[i] + fwd * 0.1f * f.Height / 1.72f + torso.X * side * -0.02f - Vector3.Up * 0.04f, g.Crouch);
            Vector3 pole = torso.Z + down * 0.2f + torso.X * side * 0.3f;
            // A hand on a weapon stays on it; the other does the gesture (a one-handed one for the weapon hand, mirrored).
            bool keeps = i == 1 ? hold.RW > 0f : kind == HoldKind.Bow;
            if (act > 0f && !keeps)
            {
                Vector3 to = i == 0 && hold.RW > 0f && OneHanded(g.Act) ? ActHand(g, 1, -1f, shoulder, chest, neck, hip, torso, arm) : ActHand(g, i, side, shoulder, chest, neck, hip, torso, arm);
                hand = hand.Lerp(to, act);
                pole = (torso.Z * 0.6f + torso.X * side * 0.7f - Vector3.Up * 0.2f).Normalized();
            }
            float on = i == 1 ? hold.RW : hold.LW;
            if (on > 0f)
            {
                hand = hand.Lerp(i == 1 ? hold.R : hold.L, on);
                // Holding something out in front, the elbow hangs down and a little out.
                pole = pole.Lerp((Vector3.Down + torso.X * side * 0.5f + torso.Z * 0.2f).Normalized(), on).Normalized();
            }
            if (husk && g.Reach < 0.5f) pole = (pole + torso.X * side * 0.6f + Vector3.Up * 0.3f * jerk).Normalized(); // elbows out, slack
            // Reaching out to a weapon, the shoulder comes forward as far as it must (a little).
            float need = MathF.Max(0f, shoulder.DistanceTo(hand) - arm * 0.98f);
            if (on > 0f && need > 0f) shoulder += (hand - shoulder).Normalized() * MathF.Min(need, Reach * HumanBody.ArmScale(f)) * on;
            (Vector3 elbow, Vector3 wrist) = LeggedRig.SolveIK(shoulder, hand, upperArm, forearm, pole);
            Pose[_armU[i]] = Geo.Segment(shoulder, elbow, torso.X);
            Pose[_armL[i]] = Geo.Segment(elbow, wrist, torso.X);
            _wrist[i] = wrist;
        }
        if (_prop >= 0) Pose[_prop] = PropPose(g, kind, hold, aimW, _wrist[1], torso, fwd, right);
        if (_string0 >= 0 && _string1 >= 0)
        {
            // The string from each limb tip to the nock: straight at rest, drawn to the right hand.
            Transform3D bow = Pose[_prop];
            Vector3 nock = (bow * new Vector3(0f, 0f, HumanBody.BowTip(f, 1).Z)).Lerp(_wrist[1], hold.RW);
            Pose[_string0] = Geo.Segment(bow * HumanBody.BowTip(f, 1), nock, right, 1f);
            Pose[_string1] = Geo.Segment(bow * HumanBody.BowTip(f, -1), nock, right, 1f);
        }

        // The head turns on its own, and stays nearer level than the back it sits on.
        float headPitch = pitch * 0.35f + g.Retch * 0.5f + (longGun ? aimW * 0.3f : 0f) + act * (g.Act == BodyAct.Grieve ? 0.5f : g.Act == BodyAct.Taunt ? -0.25f : 0f);
        float headRoll = longGun ? -0.14f * aimW : 0f; // down to the sights, the cheek toward the stock
        if (husk)
        {
            // The head lolls: held at its wrong angle, rolling slowly round it, snapping with each jerk.
            headRoll = f.HeadTilt + 0.22f * MathF.Sin(g.Clock * 0.8f + _offset * 13f) + 0.4f * jerk * MathF.Sin(_offset * 57f);
            // The head is held up off the hunched back (it watches you), dropping now and then; reaching, it lifts to the face it wants.
            headPitch = pitch * 0.15f + f.HeadDroop + 0.1f * MathF.Sin(g.Clock * 0.55f + _offset * 3f) + g.Retch * 0.5f;
            headPitch = Mathf.Lerp(headPitch, -0.2f, g.Reach);
        }
        Basis headBasis = turn * new Basis(Vector3.Up, g.Look + staggerSide) * new Basis(Vector3.Right, -headPitch) * new Basis(Vector3.Back, headRoll);
        Pose[_head] = new Transform3D(headBasis, neck);

        if (g.Down > 0f)
        {
            // Dead: the whole body goes over about the feet, backwards.
            var fall = new Transform3D(new Basis(right, g.Down * MathF.PI * 0.49f), g.Feet);
            Transform3D lay = fall * new Transform3D(Basis.Identity, -g.Feet);
            for (int i = 0; i < Pose.Length; i++) Pose[i] = lay * Pose[i];
        }
    }

    /// <summary>
    /// Where a hand goes for a gesture: big, clear shapes that read at 30 m. The right hand (i = 1)
    /// does the one-handed ones. Everything moves on the clock, so none of it is a held pose.
    /// </summary>
    static Vector3 ActHand(in Gesture g, int i, float side, Vector3 shoulder, Vector3 chest, Vector3 neck, Vector3 hip, Basis torso, float arm)
    {
        Vector3 up = Vector3.Up, ahead = -torso.Z, out_ = torso.X * side;
        float t = g.Clock;
        bool right = i == 1;
        Vector3 toward = g.ActAt - shoulder;
        toward = toward.LengthSquared() > 0.01f ? toward.Normalized() : ahead;
        Vector3 rest = shoulder + Vector3.Down * arm * 0.9f + out_ * 0.05f;
        switch (g.Act)
        {
            case BodyAct.HandsUp: return shoulder + (up * 0.9f + out_ * 0.42f).Normalized() * arm * 0.97f + ahead * 0.06f + up * 0.02f * MathF.Sin(t * 13f + i);
            case BodyAct.Cower: return neck + ahead * 0.16f + out_ * 0.07f + up * 0.06f;
            case BodyAct.WaveOff: return right ? chest + ahead * 0.42f + up * 0.05f + torso.X * (0.1f + 0.32f * MathF.Sin(t * 7f)) : rest;
            case BodyAct.Beckon: return right ? shoulder + (ahead * (0.35f + 0.65f * (0.5f + 0.5f * MathF.Sin(t * 4f))) + out_ * 0.35f + Vector3.Down * 0.1f).Normalized() * arm * 0.95f : rest;
            case BodyAct.Point: return right ? shoulder + toward * arm * 0.98f : rest;
            case BodyAct.Shrug: return hip + out_ * 0.38f + ahead * 0.28f + up * (0.12f + 0.08f * MathF.Max(0f, MathF.Sin(t * 3f)));
            case BodyAct.Warn: return right ? shoulder + (ahead * 0.8f + up * 0.6f).Normalized() * arm * 0.98f : rest;
            case BodyAct.Celebrate: return shoulder + up * arm * (0.7f + 0.25f * MathF.Max(0f, MathF.Sin(t * 8f + i * 1.3f))) + out_ * 0.28f;
            case BodyAct.Grieve: return neck + ahead * 0.14f + out_ * 0.04f + up * 0.04f;
            case BodyAct.Panic: return shoulder + up * (0.25f + 0.2f * MathF.Sin(t * 11f + i * 2f)) + out_ * (0.3f + 0.15f * MathF.Sin(t * 9f)) + ahead * 0.15f;
            case BodyAct.Fidget: return right && MathF.Sin(t * 0.4f) > 0.2f ? neck + torso.Z * 0.06f + out_ * 0.12f : hip + out_ * 0.2f + ahead * 0.02f;
            case BodyAct.Taunt: return shoulder + (out_ * 0.85f + up * (0.2f + 0.6f * MathF.Max(0f, MathF.Sin(t * 5f + i * 3f))) + ahead * 0.2f).Normalized() * arm * 0.97f;
            case BodyAct.Present: return right ? shoulder + (toward * 0.6f + out_ * 0.4f).Normalized() * arm * 0.9f + Vector3.Down * 0.15f : rest;
            case BodyAct.Thanks: return right ? chest + ahead * 0.16f + torso.X * -0.04f + up * 0.02f : rest;
            case BodyAct.Greet when g.Style == 1: return right ? shoulder + (up * 0.75f + ahead * 0.45f + out_ * 0.2f).Normalized() * arm * 0.95f : rest;
            case BodyAct.Greet when g.Style == 2: return right ? chest + ahead * 0.16f + torso.X * -0.04f : rest;
            case BodyAct.Greet: return right ? shoulder + (up * 0.85f + out_ * (0.15f + 0.55f * (0.5f + 0.5f * MathF.Sin(t * 6f)))).Normalized() * arm * 0.98f : rest;
            case BodyAct.Startle: return shoulder + (up * 0.55f + out_ * 0.7f + ahead * 0.25f).Normalized() * arm * 0.9f;
            case BodyAct.Play: return shoulder + out_ * arm * (0.45f + 0.35f * MathF.Sin(t * 4f + i * 3.1f)) + up * (0.05f + 0.3f * MathF.Max(0f, MathF.Sin(t * 4f + i * 3.1f))) + ahead * 0.2f;
            default: return rest;
        }
    }

    /// <summary>The gestures done with the one (right) hand: with a weapon in it, the left does them.</summary>
    static bool OneHanded(BodyAct a) => a is BodyAct.WaveOff or BodyAct.Beckon or BodyAct.Point or BodyAct.Warn or BodyAct.Present or BodyAct.Thanks or BodyAct.Greet;

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
    Hold Grips(in Gesture g, HoldKind kind, float aim, float busy, Basis turn, Basis torso, Vector3 hip, Vector3 chest, Vector3 neck)
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
                // The string side (-Z) toward the archer: the bow faces the way it would shoot.
                Vector3 hang = shoulderL + Vector3.Down * armL * 0.84f + ahead * 0.14f * u - torso.X * 0.09f * u;
                Vector3 hangDir = (Vector3.Up * 0.95f + fwd * 0.2f - right * 0.12f).Normalized();
                Vector3 held = shoulderL + look * armL * 0.95f + Vector3.Up * 0.03f * u;
                Vector3 heldDir = (Vector3.Up + right * 0.12f).Normalized();
                grip = hang.Lerp(held, aim);
                dir = hangDir.Lerp(heldDir, aim).Normalized();
                Vector3 facing = fwd.Lerp(look, aim).Normalized();
                h.L = grip;
                h.LW = 1f;
                h.R = neck + Vector3.Up * f.Neck * 0.5f + right * 0.07f * u + look * 0.04f * u;
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

    /// <summary>Where the carried thing goes: a weapon where its hold puts it; everything else stands in the right hand.</summary>
    Transform3D PropPose(in Gesture g, HoldKind kind, in Hold h, float aim, Vector3 hand, Basis torso, Vector3 fwd, Vector3 right)
    {
        Figure f = _f;
        Vector3 along;
        if (kind is HoldKind.LongGun or HoldKind.Pistol or HoldKind.Spear or HoldKind.Bow) return h.Prop;
        if (kind == HoldKind.Blade)
        {
            // Hanging head-down and forward; up and ready when wary.
            along = (Vector3.Down * 0.7f + fwd * 0.7f - right * 0.08f).Normalized().Lerp((Vector3.Up + fwd * 0.25f).Normalized(), aim).Normalized();
            return new Transform3D(Geo.AlongY(along, right), hand);
        }
        if (f.Prop == "cane")
        {
            along = (Vector3.Down + fwd * 0.05f).Normalized();
            return new Transform3D(Geo.AlongY(along, right), hand);
        }
        if (f.Prop == "hoe")
        {
            // Over the shoulder on the way; head down and working when bent to it.
            along = ((Vector3.Up * 1.2f - torso.Z * 0.3f + right * 0.1f).Normalized()).Lerp((fwd + Vector3.Down * (0.8f + 0.5f * MathF.Sin(g.Clock * 1.7f))).Normalized(), g.Work).Normalized();
            return new Transform3D(Geo.AlongY(along, right), hand);
        }
        // A wrench points ahead out of the hand; everything else (umbrella, bottle) stands up in it.
        along = f.Prop == "wrench" ? (fwd + Vector3.Down * 0.5f).Normalized() : Vector3.Up;
        return new Transform3D(Geo.AlongY(along, right), hand);
    }
}
