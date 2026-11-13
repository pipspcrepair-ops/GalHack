using System;
using Godot;

namespace Wildlife;

/// <summary>
/// Arms that know which way they bend. Every pose is set by where the upper arm and the forearm
/// point, in the body's own frame (out to the side, up, ahead), so an elbow is always where an
/// elbow goes; the hand and the elbow then go through the same IK as everything else, and nothing
/// of an arm is ever left inside the torso (<see cref="TorsoShell"/>, measured off the body as built).
/// </summary>
public sealed partial class HumanRig
{
    /// <summary>The torso's outside, measured once from this body's own piece.</summary>
    public TorsoShell Shell { get; private set; }

    /// <summary>An arm's goal: where the hand goes, and the way the elbow points.</summary>
    struct ArmGoal
    {
        public Vector3 Hand, Pole;
        public HandSpec Spec;
        public bool On;
        public ArmGoal(Vector3 hand, Vector3 pole)
        {
            Hand = hand;
            Pole = pole;
            Spec = default;
            On = true;
        }
        /// <summary>The same goal with this hand on it.</summary>
        public ArmGoal With(in HandSpec spec)
        {
            Spec = spec;
            return this;
        }
    }

    /// <summary>The body's frame for one arm: out is toward that arm's side.</summary>
    readonly struct Frame
    {
        public readonly Vector3 Out, Up, Ahead;
        public Frame(Basis torso, float side)
        {
            Out = torso.X * side;
            Up = torso.Y;
            Ahead = -torso.Z;
        }
        /// <summary>A direction out, up and ahead in the body's frame.</summary>
        public Vector3 V(float o, float u, float a) => (Out * o + Up * u + Ahead * a).Normalized();
        /// <summary>A vector given as (out, up, ahead) in this frame, in the world.</summary>
        public Vector3 Mix(Vector3 v) => Out * v.X + Up * v.Y + Ahead * v.Z;
    }

    void BuildShell()
    {
        int body = Layout.Pieces.FindIndex(p => p.Name == "body");
        if (body < 0) return;
        Shell = new TorsoShell(Layout.Pieces[body], -0.12f * _f.Torso, 1.04f * _f.Torso);
    }

    /// <summary>The thickness of each part of an arm: the upper arm and the forearm (radii, m).</summary>
    float UpperRadius => _f.Husk || Species.Mechanical ? 0.045f : 0.05f * HumanBody.Scale(_f) * (0.85f + 0.5f * _f.Build) * _f.Padding;
    float ForeRadius => Species.Mechanical ? 0.04f : 0.04f * HumanBody.Scale(_f) * (0.85f + 0.5f * _f.Build) * _f.Padding;

    /// <summary>A pose by its bones: the upper arm along u, the forearm along f, from the shoulder.</summary>
    ArmGoal Bones(Vector3 shoulder, Vector3 u, Vector3 f, float upper, float fore)
    {
        Vector3 elbow = shoulder + u * upper;
        if (Shell != null)
        {
            // An elbow asked inside a thick coat sits on its outside instead, the forearm going on from there as asked.
            Transform3D body = Pose[_body];
            Vector3 out_ = body * Shell.PushOut(body.AffineInverse() * elbow, UpperRadius + 0.01f), d = out_ - shoulder, up = body.Basis.Y;
            // Out round the coat, but no higher than asked: a dropped elbow stays dropped.
            Vector3 flat = d - up * d.Dot(up);
            elbow = u.Dot(up) < 0f && flat.Length() < upper * 0.98f ? shoulder + flat - up * MathF.Sqrt(upper * upper - flat.LengthSquared()) : shoulder + d.Normalized() * upper;
        }
        Vector3 hand = elbow + f * fore;
        Vector3 pole = elbow - (shoulder + hand) * 0.5f;
        if (pole.LengthSquared() < 1e-5f) pole = u.Cross(f).LengthSquared() > 1e-5f ? u.Cross(f) : Vector3.Down;
        return new ArmGoal(hand, pole.Normalized());
    }

    /// <summary>
    /// Clearance of a point from the torso's skin, m: below 0 inside it. In the body piece's space;
    /// near enough for an ellipse, and exact on its axes.
    /// </summary>
    public float Clearance(Vector3 local)
    {
        if (Shell == null) return 1f;
        float o = Shell.Out(local);
        if (o > 5f) return 1f;
        float r = new Vector2(local.X, local.Z).Length();
        return (o - 1f) * r / MathF.Max(o, 1e-3f);
    }

    /// <summary>
    /// How far the worst part of an arm is sunk into the torso, m (0 if none of it is). The upper
    /// arm counts only where it goes deeper than the shoulder it hangs from (a padded coat takes the
    /// shoulder in), and only past its first third; the forearm counts everywhere (the hand: <see cref="HandSunk"/>).
    /// Measured on the arm's centre line: grazing the skin is not through it.
    /// </summary>
    public float Sunk(Transform3D body, Vector3 shoulder, Vector3 elbow, Vector3 wrist, float margin = 0f) => SunkIn(body.AffineInverse(), shoulder, elbow, wrist, margin);

    /// <summary><see cref="Sunk"/> with the body's inverse already at hand.</summary>
    float SunkIn(Transform3D inv, Vector3 shoulder, Vector3 elbow, Vector3 wrist, float margin)
    {
        if (Shell == null) return 0f;
        float worst = 0f;
        float atShoulder = MathF.Min(0f, Clearance(inv * shoulder));
        for (int k = 1; k <= 4; k++)
        {
            Vector3 p = shoulder.Lerp(elbow, 0.3f + 0.7f * k / 4f);
            float c = Clearance(inv * p) - margin * UpperRadius;
            worst = MathF.Max(worst, atShoulder - c);
        }
        for (int k = 0; k <= 5; k++)
        {
            Vector3 p = elbow.Lerp(wrist, k / 5f);
            float c = Clearance(inv * p) - margin * ForeRadius;
            worst = MathF.Max(worst, -c);
        }
        return worst;
    }

    /// <summary>The way each arm's elbow went last time round the body (zero when it hung as asked): tried first, so an arm kept clear stays clear without a search.</summary>
    readonly Vector3[] _clearPole = new Vector3[2];

    /// <summary>
    /// Shoulder to hand by IK, the elbow toward the pole, kept out of the torso: a hand asked inside
    /// it (or the head) is moved off its skin, and an arm that would pass through swings its elbow out
    /// to the side (as a real one does) and, failing that, takes the hand further out to the side. A hand
    /// on a weapon stays on its grip (the hold keeps the weapon itself clear of the body).
    /// </summary>
    (Vector3 elbow, Vector3 wrist) Arm(Transform3D body, ref Vector3 shoulder, Vector3 hand, Vector3 pole, float upper, float fore, in Frame F, int arm, in HandSpec spec, Vector3 grips, float gripW, Vector3 gripFore, bool held)
    {
        Vector3 outward = F.Out, ahead = F.Ahead;
        Frame frame = F;
        HandSpec hs = spec;
        Vector3 axis0 = grips;
        Transform3D inv = _bodyInv;
        // The whole hand, not its wrist: the wrist goes where none of the hand is in the torso or the head.
        Vector3 Free(Vector3 elbow, Vector3 wrist) => HandClear(new Transform3D(HandFrame(arm, hs, frame, axis0, gripW, (wrist - elbow).Normalized(), gripFore), wrist), hs.Shape, arm, 0.006f);
        // How far any of the arm is in the body (the arm first, the hand when the arm is clear).
        float Through(Vector3 sh, Vector3 elbow, Vector3 wrist, bool handToo)
        {
            float sunk = SunkIn(inv, sh, elbow, wrist, 0.15f);
            return sunk > 0f || !handToo ? sunk : Free(elbow, wrist).Length();
        }
        (Vector3 e, Vector3 w) = LeggedRig.SolveIK(shoulder, hand, upper, fore, pole);
        if (held)
        {
            // On a grip it stays on it, give or take a hand's clearance of the body (never more than 3 cm).
            for (int k = 0; k < 2 && Shell != null; k++)
            {
                Vector3 nudge = Free(e, w);
                if (nudge.LengthSquared() < 1e-6f) break;
                hand += nudge.Length() > 0.03f ? nudge.Normalized() * 0.03f : nudge;
                (e, w) = LeggedRig.SolveIK(shoulder, hand, upper, fore, pole);
            }
            return (e, w);
        }
        bool clear = false;
        for (int k = 0; k < 6 && !clear; k++)
        {
            Vector3 push = Free(e, w);
            clear = push.LengthSquared() < 1e-6f;
            if (clear) break;
            hand += push;
            (e, w) = LeggedRig.SolveIK(shoulder, hand, upper, fore, pole);
        }
        if (Shell == null) return (e, w);
        float least = Through(shoulder, e, w, !clear);
        if (least <= 0f)
        {
            _clearPole[arm] = Vector3.Zero;
            return (e, w);
        }
        // Where it went last time, if that still clears and is near what was asked.
        if (_clearPole[arm].Dot(pole.Normalized()) > 0.3f)
        {
            (Vector3 e1, Vector3 w1) = LeggedRig.SolveIK(shoulder, hand, upper, fore, _clearPole[arm]);
            if (Through(shoulder, e1, w1, true) <= 0f) return (e1, w1);
        }
        // Through the body: the elbow round its circle to the nearest way that clears it; failing all of
        // them, the shoulder brought forward a little (as one reaching across the chest does), then the
        // hand a step out to the side, forward or up, by turns, and round again.
        (Vector3, Vector3) fallback = (e, w);
        Vector3 fallbackShoulder = shoulder;
        for (int k = 0; k < 17; k++)
        {
            Vector3 axis = (hand - shoulder).Normalized();
            Vector3 b1 = pole - axis * pole.Dot(axis);
            if (b1.LengthSquared() < 1e-6f) b1 = outward - axis * outward.Dot(axis);
            b1 = b1.Normalized();
            Vector3 b2 = axis.Cross(b1);
            float bestScore = -9f;
            Vector3 best = Vector3.Zero;
            for (int j = 0; j < 16; j++)
            {
                float a = MathF.Tau * j / 16f;
                Vector3 p = b1 * MathF.Cos(a) + b2 * MathF.Sin(a);
                (Vector3 e2, Vector3 w2) = LeggedRig.SolveIK(shoulder, hand, upper, fore, p);
                float sunk = Through(shoulder, e2, w2, true);
                // (Near the elbow it was asked for first: a hand moved a little out reads better than an elbow thrown up.)
                if (sunk <= 0f && MathF.Cos(a) > bestScore && (k >= 6 || MathF.Cos(a) > 0.7f)) { bestScore = MathF.Cos(a); (e, w) = (e2, w2); best = p; }
                else if (sunk < least) { least = sunk; fallback = (e2, w2); fallbackShoulder = shoulder; }
            }
            if (bestScore > -9f)
            {
                _clearPole[arm] = k == 0 ? best : Vector3.Zero;
                return (e, w);
            }
            if (k < 2) shoulder += ahead * 0.025f;
            else hand += (k % 3 == 0 ? outward : k % 3 == 1 ? ahead : body.Basis.Y) * 0.05f;
        }
        shoulder = fallbackShoulder;
        _clearPole[arm] = Vector3.Zero;
        return fallback;
    }

    /// <summary>The arm that does a one-handed act: the right, unless it holds a weapon or the thing pointed at is off to the left.</summary>
    static int Doer(in Gesture g, bool rightHeld, Vector3 chest, Basis torso)
    {
        if (rightHeld) return 0;
        if (g.Act == BodyAct.Point)
        {
            Vector3 to = g.ActAt - chest;
            to.Y = 0f;
            if (to.LengthSquared() > 0.01f && to.Normalized().Dot(torso.X) < -0.2f) return 0;
        }
        return 1;
    }

    /// <summary>Whether this act is done with one hand (the other rests, or keeps its weapon).</summary>
    static bool OneHanded(in Gesture g) => g.Act switch
    {
        BodyAct.WaveOff or BodyAct.Beckon or BodyAct.Point or BodyAct.Warn or BodyAct.Present => true,
        BodyAct.Greet => true,
        BodyAct.Thanks => g.Style == 0,
        _ => false,
    };

    /// <summary>
    /// Where an arm goes for a gesture. Big, clear shapes that read at 30 m, each its own; the
    /// elbows stay down unless the act lifts them (hands up, a wave, a salute), and everything
    /// moves on the clock, so nothing is a held pose.
    /// </summary>
    ArmGoal ActArm(in Gesture g, int i, bool doer, Vector3 shoulder, Vector3 neck, Basis torso, Basis head, float upper, float fore)
    {
        float side = i == 0 ? -1f : 1f;
        var F = new Frame(torso, side);
        float t = g.Clock, arm = upper + fore;
        Figure f = _f;
        if (OneHanded(g) && !doer) return default;
        switch (g.Act)
        {
            case BodyAct.HandsUp:
            {
                float shake = 0.04f * MathF.Sin(t * 13f + i);
                return Bones(shoulder, F.V(0.75f, 0.5f, 0.3f), F.V(0.1f + shake, 1f, 0.12f), upper, fore).With(HandTable.At("handsup", i));
            }
            case BodyAct.Cower:
            {
                // Forearms up before the face, elbows down and in front: shielding, not waving.
                float flinch = 0.04f * MathF.Sin(t * 9f + i * 2f);
                return Bones(shoulder, F.V(0.16f, -0.55f, 0.8f), F.V(-0.5f, 0.85f + flinch, 0.18f), upper, fore).With(HandTable.At("cower", i));
            }
            case BodyAct.WaveOff:
            {
                // Elbow low by the side; the forearm flicks out and away from the body, again and again.
                float s = 0.5f + 0.5f * MathF.Sin(t * 5.5f);
                float a = -0.25f + 1.45f * s;
                return Bones(shoulder, F.V(0.15f, -0.7f, 0.7f), F.V(MathF.Sin(a), 0.3f, MathF.Cos(a)), upper, fore).With(HandTable.Mix("waveoff-in", "waveoff-out", s, i));
            }
            case BodyAct.Beckon:
            {
                // Elbow down, the forearm out to them, then curling up and back toward the chest: come here.
                float c = 0.5f + 0.5f * MathF.Sin(t * 4.5f);
                return Bones(shoulder, F.V(0.12f, -0.7f, 0.7f), F.V(0.06f, 0.15f + 0.9f * c, 1f - 0.9f * c), upper, fore).With(HandTable.Mix("beckon-out", "beckon-in", c, i));
            }
            case BodyAct.Point:
            {
                // The whole arm straight out to it, never across the body.
                Vector3 to = g.ActAt - shoulder;
                to = to.LengthSquared() > 0.01f ? to.Normalized() : F.Ahead;
                float o = to.Dot(F.Out), u = to.Dot(F.Up), a = to.Dot(F.Ahead);
                float yaw = Math.Clamp(MathF.Atan2(o, MathF.Max(a, 0.05f)), -0.12f, 0.9f);
                float pitch = Math.Clamp(MathF.Asin(Math.Clamp(u, -1f, 1f)), -0.45f, 0.55f) + 0.08f;
                Vector3 dir = F.V(MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), MathF.Cos(yaw) * MathF.Cos(pitch));
                // A touch of bend, the elbow down, so it reads as an arm and not a pole; the hand as posed for a target high, low or to the side.
                HandSpec hand = pitch > 0.08f ? HandTable.Mix("point", "point-up", (pitch - 0.08f) / 0.47f, i) : HandTable.Mix("point", "point-down", (0.08f - pitch) / 0.53f, i);
                return Bones(shoulder, (dir + F.Up * -0.08f).Normalized(), (dir + F.Up * 0.03f).Normalized(), upper, fore).With(HandSpec.Lerp(hand, HandTable.At("point-side", i), yaw / 0.9f));
            }
            case BodyAct.Shrug:
            {
                // Elbows at the sides, forearms out and up, palms up.
                float lift = 0.1f * MathF.Max(0f, MathF.Sin(t * 3f));
                return Bones(shoulder, F.V(0.35f, -0.85f, 0.25f), F.V(0.55f, 0.62f + lift, 0.6f), upper, fore).With(HandTable.At("shrug", i));
            }
            case BodyAct.Grieve:
            {
                // Face in the hands, elbows down before the chest; the shoulders shake.
                float sob = 0.05f * MathF.Sin(t * 7f) * MathF.Max(0f, MathF.Sin(t * 0.9f));
                return Bones(shoulder, F.V(0.1f, -0.68f, 0.72f), F.V(-0.55f, 0.8f + sob, 0.12f), upper, fore).With(HandTable.At("grieve", i));
            }
            case BodyAct.Fidget: return Fidget(g, i, F, shoulder, neck, torso, upper, fore);
            case BodyAct.Taunt:
            {
                // A fist shaken at them, high and forward; the other balled low at the side.
                if (i == 0) return Bones(shoulder, F.V(0.3f, -0.9f, 0.2f), F.V(0.15f, 0.35f, 0.9f), upper, fore).With(HandTable.At("taunt", i));
                float shake = 0.12f * MathF.Sin(t * 14f);
                return Bones(shoulder, F.V(0.35f, 0.45f, 0.8f), F.V(0.05f + shake, 0.9f, 0.45f - shake), upper, fore).With(HandTable.At("taunt", i));
            }
            case BodyAct.Greet when g.Style == 1:
                // An open palm held up to them, still.
                return Bones(shoulder, F.V(0.3f, -0.2f, 0.92f), F.V(0.05f, 1f, 0.1f), upper, fore).With(HandTable.At("greet-palm", i));
            case BodyAct.Greet when g.Style == 2:
                return OnChest(F, torso, side, 0.78f, 0f).With(HandTable.At("greet-chest", i));
            case BodyAct.Greet:
            {
                // Arm up, the forearm swinging from the elbow: a wave from far off.
                float w = 0.35f * MathF.Sin(t * 6f);
                return Bones(shoulder, F.V(0.65f, 0.55f, 0.35f), F.V(0.12f + w, 1f, 0.1f), upper, fore).With(HandTable.At("greet-wave", i));
            }
            case BodyAct.Thanks when g.Style == 1:
            {
                // Both palms offered, low and forward, with a bow.
                float dip = 0.06f * MathF.Sin(t * 2f);
                return Bones(shoulder, F.V(0.2f, -0.85f, 0.48f), F.V(0.22f, 0.18f + dip, 0.95f), upper, fore).With(HandTable.At("thanks-offer", i));
            }
            case BodyAct.Thanks when g.Style == 2:
                // Both hands flat on the heart, one over the other, and a deep bow.
            {
                if (i == 1) return OnChest(F, torso, side, 0.74f, 0f).With(HandTable.At("thanks-chest", i));
                // (The left over the right: a palm's thickness above it.)
                ArmGoal over = OnChest(new Frame(torso, 1f), torso, 1f, 0.68f, 2f * HandHalf(0) + 0.004f).With(HandTable.At("thanks-chest", i));
                over.Pole = F.V(0.7f, -0.7f, 0.1f);
                return over;
            }
            case BodyAct.Thanks:
            {
                // A salute: the hand to the brow, then swept out and forward to them.
                float q = t * 0.8f - MathF.Floor(t * 0.8f);
                float R = f.HeadRadius;
                Vector3 brow = neck + head.Y * (f.Neck + R * 1.25f) - head.Z * R * 1.05f + F.Out * R * 0.45f;
                Vector3 outHand = shoulder + F.V(0.55f, 0.45f, 0.72f) * arm * 0.93f;
                float k = Mathf.SmoothStep(0.35f, 0.75f, q) * (1f - Mathf.SmoothStep(0.88f, 1f, q));
                return new ArmGoal(brow.Lerp(outHand, k), F.V(0.85f, -0.25f, -0.15f)).With(HandTable.Mix("salute-brow", "salute-out", k, i));
            }
            case BodyAct.Play:
            {
                // Clapping over the head, bouncing on the toes.
                float c = 0.5f + 0.5f * MathF.Cos(t * 9f);
                Vector3 mid = neck + F.Up * (f.Neck + f.HeadRadius * 2.2f) + F.Ahead * 0.22f;
                Vector3 hand = mid + F.Out * (HandHalf(i) + 0.004f + 0.16f * c) + F.Up * 0.04f * MathF.Sin(t * 4.5f);
                return new ArmGoal(hand, F.V(0.6f, -0.8f, -0.1f)).With(HandTable.Mix("clap-closed", "clap-open", c, i));
            }
            case BodyAct.Warn:
                // Stop: the arm out, the palm up and out at them.
                return Bones(shoulder, F.V(0.18f, -0.05f, 0.98f), F.V(0.02f, 0.85f, 0.5f), upper, fore).With(HandTable.At("warn", i));
            case BodyAct.Celebrate:
            {
                float e = MathF.Max(0f, MathF.Sin(t * 8f + i * 1.3f));
                return Bones(shoulder, F.V(0.45f, 0.85f, 0.15f), F.V(-0.2f, 0.3f, 0.4f).Lerp(F.V(0.05f, 1f, 0.05f), e).Normalized(), upper, fore).With(HandTable.Mix("celebrate-low", "celebrate-high", e, i));
            }
            case BodyAct.Panic:
            {
                // Hands to the head, then flung about, then to the head again.
                float k = Mathf.SmoothStep(-0.3f, 0.3f, MathF.Sin(t * 1.6f + i));
                Vector3 c = neck + head.Y * (f.Neck + f.HeadRadius) + F.Out * (f.HeadRadius + HandHalf(i) + 0.01f);
                ArmGoal clutch = new(c, F.V(0.85f, 0.25f, -0.2f));
                ArmGoal flail = Bones(shoulder, F.V(0.6f, 0.15f + 0.35f * MathF.Sin(t * 11f + i * 2f), 0.4f), F.V(0.3f, 0.6f, 0.5f + 0.4f * MathF.Sin(t * 9f + i)), upper, fore);
                return new ArmGoal(clutch.Hand.Lerp(flail.Hand, k), clutch.Pole.Lerp(flail.Pole, k).Normalized()).With(HandTable.Mix("panic-clutch", "panic-flail", k, i));
            }
            case BodyAct.Present:
                // A hand swept out to the wares, palm up.
                return Bones(shoulder, F.V(0.35f, -0.8f, 0.45f), F.V(0.65f, 0.18f, 0.75f), upper, fore).With(HandTable.At("present", i));
            case BodyAct.Startle:
                return Bones(shoulder, F.V(0.6f, -0.45f, 0.42f), F.V(0.15f, 0.95f, 0.3f), upper, fore).With(HandTable.At("startle", i));
        }
        return default;
    }

    /// <summary>A hand laid flat on the chest (over the heart, across from its own shoulder), the elbow out and down.</summary>
    ArmGoal OnChest(Frame F, Basis torso, float side, float height, float outward)
    {
        if (Shell == null) return default;
        int i = side < 0f ? 0 : 1;
        float y = _f.Torso * height;
        float x = -side * Shell.HalfWidth(y) * 0.32f;
        Vector3 local = Shell.FrontAt(y, x);
        Vector3 hand = Pose[_body] * local + F.Ahead * (HandHalf(i) + 0.012f + outward);
        return new ArmGoal(hand, F.V(0.7f, -0.7f, 0.1f));
    }

    /// <summary>Restless hands: wringing, a scratch at the back of the head, hands to the hips, round and round.</summary>
    ArmGoal Fidget(in Gesture g, int i, Frame F, Vector3 shoulder, Vector3 neck, Basis torso, float upper, float fore)
    {
        float t = g.Clock * 0.32f + _offset * 3f;
        int phase = (int)MathF.Floor(t) % 3;
        float into = t - MathF.Floor(t);
        float blend = Mathf.SmoothStep(0.75f, 1f, into);
        ArmGoal now = FidgetPose(phase, g.Clock, i, F, shoulder, neck, upper, fore);
        if (blend <= 0f) return now;
        ArmGoal next = FidgetPose((phase + 1) % 3, g.Clock, i, F, shoulder, neck, upper, fore);
        return new ArmGoal(now.Hand.Lerp(next.Hand, blend) + F.Out * 0.2f * MathF.Sin(MathF.PI * blend), now.Pole.Lerp(next.Pole, blend).Normalized()).With(HandSpec.Lerp(now.Spec, next.Spec, blend));
    }

    ArmGoal FidgetPose(int phase, float t, int i, Frame F, Vector3 shoulder, Vector3 neck, float upper, float fore)
    {
        switch (phase)
        {
            case 0:
            {
                // Hands together before the belt, wringing.
                float w = 0.18f * MathF.Sin(t * 5f + i * MathF.PI);
                return Bones(shoulder, F.V(0.14f, -0.9f, 0.35f), F.V(-0.72f, 0.2f + w, 0.66f), upper, fore).With(HandTable.At("fidget-wring", i));
            }
            case 1:
            {
                // The right hand rubbing the back of the neck; the left on the hip.
                if (i == 1)
                {
                    float s = 0.04f * MathF.Sin(t * 8f);
                    Vector3 nape = neck + F.Up * (_f.Neck * 0.8f) - F.Ahead * (_f.HeadRadius * 0.6f + HandHalf(i)) + F.Out * (0.03f + s);
                    return new ArmGoal(nape, F.V(0.8f, 0.3f, 0.35f)).With(HandTable.At("fidget-nape", i));
                }
                return Bones(shoulder, F.V(0.72f, -0.62f, -0.15f), F.V(-0.6f, -0.45f, 0.35f), upper, fore).With(HandTable.At("fidget-hips", i));
            }
            default:
            {
                // Hands to the hips, weight on one foot.
                return Bones(shoulder, F.V(0.72f, -0.62f, -0.15f), F.V(-0.6f, -0.45f, 0.35f), upper, fore).With(HandTable.At("fidget-hips", i));
            }
        }
    }
}
