using System;
using Godot;

namespace Wildlife;

/// <summary>
/// Blows, hits and falls. A blow is a punch, an overhand swat, a Husk's lunge and two-handed seize
/// or its retch and spew, each a wind-up you can read, a strike and a recovery. A hit snaps the
/// struck part away from it, and a heavy one staggers the body a step. The dead ragdoll: the body
/// goes down as jointed limbs under gravity, onto the ground and whatever it lies across, and
/// settles there for good.
/// </summary>
public sealed partial class HumanRig
{
    // ---- Blows ----------------------------------------------------------------------------------

    /// <summary>How far into the wind-up and into the strike a blow is (each 0..1; both 0 at rest).</summary>
    static void StrikeCurve(float s, out float wind, out float hit)
    {
        wind = Mathf.SmoothStep(0f, 0.4f, s) * (1f - Mathf.SmoothStep(0.44f, 0.56f, s));
        hit = Mathf.SmoothStep(0.42f, 0.55f, s) * (1f - Mathf.SmoothStep(0.68f, 1f, s));
    }

    /// <summary>What a blow does to the trunk: its turn, its bend, how far the hips go forward and down.</summary>
    static void StrikeBody(in Gesture g, out float twist, out float pitch, out float forward, out float down)
    {
        twist = pitch = forward = down = 0f;
        if (g.Strike == StrikeKind.None) return;
        StrikeCurve(g.StrikeAt, out float w, out float h);
        switch (g.Strike)
        {
            case StrikeKind.Punch:
                twist = -0.4f * w + 0.5f * h;
                pitch = 0.04f * w + 0.14f * h;
                forward = -0.03f * w + 0.12f * h;
                down = 0.04f * w;
                break;
            case StrikeKind.Swat:
                twist = -0.35f * w + 0.5f * h;
                pitch = -0.14f * w + 0.38f * h;
                forward = -0.04f * w + 0.1f * h;
                down = 0.03f * w + 0.05f * h;
                break;
            case StrikeKind.Seize:
                // Coiled back and low, then thrown forward at the head.
                pitch = -0.1f * w + 0.32f * h;
                forward = -0.06f * w + 0.38f * h;
                down = 0.1f * w + 0.03f * h;
                break;
            case StrikeKind.Spew:
                // Doubled over, heaving, then the head and shoulders thrown forward.
                pitch = (0.3f + 0.22f * MathF.Abs(MathF.Sin(g.Clock * 13f))) * w + 0.6f * h;
                forward = 0.08f * h;
                down = 0.06f * w;
                break;
        }
    }

    /// <summary>Where an arm goes in a blow, and how far it is on it (its goal only counts while the blow does).</summary>
    ArmGoal StrikeArm(in Gesture g, int i, Vector3 shoulder, Basis torso, float upper, float fore, out float weight)
    {
        weight = 0f;
        if (g.Strike == StrikeKind.None) return default;
        StrikeCurve(g.StrikeAt, out float w, out float h);
        weight = MathF.Max(w, h);
        if (weight <= 0f) return default;
        float k = h / MathF.Max(1e-4f, w + h);
        var F = new Frame(torso, i == 0 ? -1f : 1f);
        bool lead = i == 1;
        switch (g.Strike)
        {
            case StrikeKind.Punch:
            {
                if (!lead) return Bones(shoulder, F.V(0.15f, -0.75f, 0.45f), F.V(-0.4f, 0.8f, 0.42f), upper, fore); // a guard up at the chin
                ArmGoal cocked = Bones(shoulder, F.V(0.3f, -0.72f, -0.38f), F.V(-0.12f, 0.85f, 0.5f), upper, fore);
                ArmGoal thrown = Bones(shoulder, F.V(-0.08f, 0.04f, 1f), F.V(-0.06f, 0.06f, 1f), upper, fore);
                return Blend(cocked, thrown, k);
            }
            case StrikeKind.Swat:
            {
                if (!lead) return Bones(shoulder, F.V(0.55f, -0.65f, 0.25f), F.V(0.3f, -0.4f, 0.55f), upper, fore); // out for balance
                ArmGoal raised = Bones(shoulder, F.V(0.3f, 0.88f, -0.22f), F.V(0.1f, 0.55f, -0.6f), upper, fore);
                ArmGoal through = Bones(shoulder, F.V(-0.1f, -0.4f, 0.95f), F.V(-0.45f, -0.6f, 0.65f), upper, fore);
                ArmGoal a = Blend(raised, through, k);
                a.Hand += F.Up * 0.22f * MathF.Sin(MathF.PI * k) + F.Ahead * 0.18f * MathF.Sin(MathF.PI * k); // over the top, not through the chest
                return a;
            }
            case StrikeKind.Seize:
            {
                // Arms up and open, fingers spread; then both hands closing on a head in front.
                ArmGoal open = Bones(shoulder, F.V(0.5f, 0.3f, 0.6f), F.V(0.3f, 0.6f, 0.72f), upper, fore);
                ArmGoal grab = Bones(shoulder, F.V(0.02f, 0.18f, 1f), F.V(-0.32f, 0.18f, 1f), upper, fore);
                return Blend(open, grab, k);
            }
            case StrikeKind.Spew:
            {
                ArmGoal clutch = Shell != null
                    ? new ArmGoal(Pose[_body] * Shell.FrontAt(_f.Torso * 0.42f, (i == 0 ? -1f : 1f) * Shell.HalfWidth(_f.Torso * 0.42f) * 0.35f) + F.Ahead * (HandRadius + 0.01f), F.V(0.75f, -0.65f, 0f))
                    : Bones(shoulder, F.V(0.2f, -0.9f, 0.3f), F.V(-0.6f, 0f, 0.8f), upper, fore);
                ArmGoal flung = Bones(shoulder, F.V(0.45f, -0.82f, -0.28f), F.V(0.25f, -0.9f, -0.2f), upper, fore);
                return Blend(clutch, flung, k);
            }
        }
        return default;
    }

    static ArmGoal Blend(ArmGoal a, ArmGoal b, float k) => new(a.Hand.Lerp(b.Hand, k), a.Pole.Lerp(b.Pole, k).Normalized());

    /// <summary>The blow a body throws, by what it is: a machine swats or punches by its make, a person punches bare-handed and swats with a blade or a club.</summary>
    public static StrikeKind BlowOf(Species s, int id)
    {
        if (s.Husk) return StrikeKind.Seize;
        if (s.Mechanical) return id % 2 == 0 ? StrikeKind.Swat : StrikeKind.Punch;
        return Weapons.HoldOf(s.Figure?.Prop ?? "") == HoldKind.Blade ? StrikeKind.Swat : StrikeKind.Punch;
    }

    // ---- Hits -----------------------------------------------------------------------------------

    /// <summary>The part a hit landed on: chest, head, left or right arm, left or right leg.</summary>
    enum Part { Chest, Head, ArmL, ArmR, LegL, LegR }

    Part _hitPart;
    Vector3 _hitDir;
    float _hitHeavy, _hitAge = 99f, _clock = float.NaN, _dt;

    /// <summary>How hard the last hit still shows (0..1), for the tests: the snap, and the stagger.</summary>
    public float Snap => _hitAge < 1f ? Envelope(_hitAge) : 0f;
    public float Stagger => _hitHeavy >= 0.5f ? StaggerOf(_hitAge) * _hitHeavy : 0f;

    /// <summary>The snap: up to full in 70 ms, gone in half a second.</summary>
    static float Envelope(float a)
    {
        const float tau = 0.07f;
        float x = a / tau;
        return x * MathF.Exp(1f - x);
    }

    /// <summary>The stagger: thrown a step in a quarter second, back on balance by a second.</summary>
    static float StaggerOf(float a)
    {
        if (a < 0.25f) return MathF.Sin(a / 0.25f * MathF.PI * 0.5f);
        float k = MathF.Max(0f, 1f - (a - 0.25f) / 0.8f);
        return k * k * (3f - 2f * k);
    }

    /// <summary>
    /// A hit lands: where (world), the way it travels, and how heavy it is (0..1; a heavy one
    /// staggers). The part nearest the point snaps away along the hit. On the dead it shoves the
    /// limb it struck.
    /// </summary>
    public void Struck(Vector3 point, Vector3 dir, float heavy)
    {
        if (dir.LengthSquared() < 1e-6f) dir = -Facing;
        dir = dir.Normalized();
        _hitDir = dir;
        _hitHeavy = Math.Clamp(heavy, 0f, 1f);
        _hitAge = 0f;
        // The nearest part: head, chest, the elbows and wrists, the knees.
        float best = point.DistanceSquaredTo(HeadPoint);
        _hitPart = Part.Head;
        void Try(Part p, Vector3 at)
        {
            float d = point.DistanceSquaredTo(at);
            if (d < best) { best = d; _hitPart = p; }
        }
        Try(Part.Chest, ChestPoint);
        Try(Part.Chest, Pose[_body].Origin + Pose[_body].Basis.Y * _f.Torso * 0.35f);
        Try(Part.ArmL, _wrist[0]);
        Try(Part.ArmR, _wrist[1]);
        Try(Part.ArmL, Pose[_armL[0]].Origin);
        Try(Part.ArmR, Pose[_armL[1]].Origin);
        Try(Part.LegL, _knee[0]);
        Try(Part.LegR, _knee[1]);
        _doll?.Shove(this, _hitPart, dir, _hitHeavy);
    }

    /// <summary>Time since the last pose, from the clock the game drives with (0 the first time or if it went back).</summary>
    void Tick(float clock)
    {
        _dt = float.IsNaN(_clock) ? 0f : Math.Clamp(clock - _clock, 0f, 0.1f);
        _clock = clock;
        _hitAge += _dt;
    }

    // ---- Dead -----------------------------------------------------------------------------------

    /// <summary>Optional ground for a body driven outside the sim (a town's floors and what stands on them): height under a point, given a height near it.</summary>
    public Func<Vector3, float> Ground;

    /// <summary>The ragdoll, from the moment of death.</summary>
    Doll _doll;

    /// <summary>Whether the dead body has come to rest (and stays as it lies).</summary>
    public bool Settled => _doll?.Settled == true;
    /// <summary>Whether this body is a ragdoll now.</summary>
    public bool Ragdolled => _doll != null;

    /// <summary>Where each joint is this frame, for the ragdoll to start from: see <see cref="Doll"/> for the order.</summary>
    readonly Vector3[] _joints = new Vector3[Doll.Count], _jointsBefore = new Vector3[Doll.Count];
    bool _jointsKnown;
    float _jointsDt;

    void KeepJoints(Vector3 hip, Vector3 neck, Basis torso, Basis head)
    {
        Array.Copy(_joints, _jointsBefore, Doll.Count);
        Figure f = _f;
        _joints[Doll.Pelvis] = hip;
        _joints[Doll.Chest] = hip + torso.Y * f.Torso * 0.7f;
        _joints[Doll.Neck] = neck;
        _joints[Doll.Crown] = neck + head.Y * (f.Neck + f.HeadRadius);
        for (int i = 0; i < 2; i++)
        {
            _joints[Doll.Shoulder + i] = Pose[_armU[i]].Origin;
            _joints[Doll.Elbow + i] = Pose[_armL[i]].Origin;
            _joints[Doll.Wrist + i] = _wrist[i];
            _joints[Doll.Hip + i] = Pose[_legU[i]].Origin;
            _joints[Doll.Knee + i] = _knee[i];
            _joints[Doll.Ankle + i] = _drawn[i];
            _joints[Doll.Toe + i] = _drawn[i] + Pose[_foot[i]].Basis.Z * -0.16f * HumanBody.Scale(f) * f.FootK + Vector3.Down * f.Ankle * 0.7f;
        }
        _jointsDt = _dt;
        if (!_jointsKnown)
        {
            Array.Copy(_joints, _jointsBefore, Doll.Count);
            _jointsKnown = true;
        }
    }

    float GroundUnder(Vector3 p, float fallback) => Ground != null ? Ground(p) : _world != null ? GroundY(_world, _creature, p.X, p.Z) : fallback;

    /// <summary>Poses the dead body from its ragdoll, stepping it on by the time gone.</summary>
    void DriveDead(in Gesture g)
    {
        if (_doll == null)
        {
            _doll = new Doll(this, _joints, _jointsBefore, _jointsKnown && _jointsDt > 0f ? _jointsDt : 1f / 60f);
            if (_hitAge < 0.4f) _doll.Shove(this, _hitPart, _hitDir, MathF.Max(0.35f, _hitHeavy));
            else _doll.Topple(this, -Facing);
        }
        float feet = g.Feet.Y;
        _doll.Step(this, _dt, p => GroundUnder(p, feet));
        _doll.Pose(this);
    }

    /// <summary>Back to life (a body reused for the living): the ragdoll goes.</summary>
    public void Revive() => _doll = null;
}
