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
        public bool On;
        public ArmGoal(Vector3 hand, Vector3 pole)
        {
            Hand = hand;
            Pole = pole;
            On = true;
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
    }

    void BuildShell()
    {
        int body = Layout.Pieces.FindIndex(p => p.Name == "body");
        if (body < 0) return;
        Shell = new TorsoShell(Layout.Pieces[body], -0.12f * _f.Torso, 1.04f * _f.Torso);
    }

    /// <summary>The thickness of each part of an arm: the upper arm, the forearm, the hand (radii, m).</summary>
    float UpperRadius => _f.Husk || Species.Mechanical ? 0.045f : 0.05f * HumanBody.Scale(_f) * (0.85f + 0.5f * _f.Build) * _f.Padding;
    float ForeRadius => Species.Mechanical ? 0.04f : 0.04f * HumanBody.Scale(_f) * (0.85f + 0.5f * _f.Build) * _f.Padding;
    float HandRadius => 0.035f * HumanBody.Scale(_f) * _f.HandK;

    /// <summary>A pose by its bones: the upper arm along u, the forearm along f, from the shoulder.</summary>
    static ArmGoal Bones(Vector3 shoulder, Vector3 u, Vector3 f, float upper, float fore)
    {
        Vector3 elbow = shoulder + u * upper, hand = elbow + f * fore;
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
    /// shoulder in), and only past its first third; the forearm and the hand count everywhere.
    /// Measured on the arm's centre line: grazing the skin is not through it.
    /// </summary>
    public float Sunk(Transform3D body, Vector3 shoulder, Vector3 elbow, Vector3 wrist, float margin = 0f)
    {
        if (Shell == null) return 0f;
        Transform3D inv = body.AffineInverse();
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
            float r = k == 5 ? HandRadius : ForeRadius;
            float c = Clearance(inv * p) - margin * r;
            worst = MathF.Max(worst, -c);
        }
        return worst;
    }

    /// <summary>
    /// Shoulder to hand by IK, the elbow toward the pole, kept out of the torso: a hand asked inside
    /// it is put on its skin, and an arm that would pass through swings its elbow out to the side
    /// (as a real one does) and, failing that, takes the hand a little further out.
    /// </summary>
    (Vector3 elbow, Vector3 wrist) Arm(Transform3D body, Vector3 shoulder, Vector3 hand, Vector3 pole, float upper, float fore, Vector3 outward, Vector3 ahead)
    {
        if (Shell != null)
        {
            Transform3D inv = body.AffineInverse();
            Vector3 l = Shell.PushOut(inv * hand, HandRadius + 0.012f);
            hand = body * l;
        }
        (Vector3 e, Vector3 w) = LeggedRig.SolveIK(shoulder, hand, upper, fore, pole);
        if (Shell == null) return (e, w);
        for (int k = 0; k < 10; k++)
        {
            if (Sunk(body, shoulder, e, w, 0.55f) <= 0f) break;
            // The elbow out to the side and down, then the hand further out and forward.
            pole = (pole.Normalized() + outward * 0.9f + Vector3.Down * 0.15f).Normalized();
            if (k >= 2)
            {
                Transform3D inv = body.AffineInverse();
                Vector3 l = inv * hand;
                hand = body * Shell.PushOut(l, HandRadius + 0.03f * (k - 1)) + (outward * 0.6f + ahead) * 0.025f;
            }
            (e, w) = LeggedRig.SolveIK(shoulder, hand, upper, fore, pole);
        }
        return (e, w);
    }

    /// <summary>A held weapon moved out of the torso as a whole (a puffy coat, a big chest piece): both grips and the weapon with them.</summary>
    void ClearOfTorso(ref Hold h)
    {
        if (Shell == null) return;
        Transform3D body = Pose[_body], inv = body.AffineInverse();
        Vector3 shift = Vector3.Zero;
        // (Only as far as the hands' middles on the skin: a weapon held hard against a coat presses into it.)
        if (h.RW > 0f) shift = body * Shell.PushOut(inv * h.R, HandRadius * 0.4f) - h.R;
        if (h.LW > 0f) shift += body * Shell.PushOut(inv * (h.L + shift), HandRadius * 0.4f) - (h.L + shift);
        h.R += shift;
        h.L += shift;
        h.Prop = h.Prop.Translated(shift);
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
                return Bones(shoulder, F.V(0.75f, 0.5f, 0.3f), F.V(0.1f + shake, 1f, 0.12f), upper, fore);
            }
            case BodyAct.Cower:
            {
                // Forearms up before the face, elbows down and in front: shielding, not waving.
                float flinch = 0.04f * MathF.Sin(t * 9f + i * 2f);
                return Bones(shoulder, F.V(0.16f, -0.55f, 0.8f), F.V(-0.5f, 0.85f + flinch, 0.18f), upper, fore);
            }
            case BodyAct.WaveOff:
            {
                // Elbow low by the side; the forearm flicks out and away from the body, again and again.
                float s = 0.5f + 0.5f * MathF.Sin(t * 5.5f);
                float a = -0.25f + 1.45f * s;
                return Bones(shoulder, F.V(0.15f, -0.7f, 0.7f), F.V(MathF.Sin(a), 0.3f, MathF.Cos(a)), upper, fore);
            }
            case BodyAct.Beckon:
            {
                // Elbow down, the forearm out to them, then curling up and back toward the chest: come here.
                float c = 0.5f + 0.5f * MathF.Sin(t * 4.5f);
                return Bones(shoulder, F.V(0.12f, -0.7f, 0.7f), F.V(0.06f, 0.15f + 0.9f * c, 1f - 0.9f * c), upper, fore);
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
                // A touch of bend, the elbow down, so it reads as an arm and not a pole.
                return Bones(shoulder, (dir + F.Up * -0.08f).Normalized(), (dir + F.Up * 0.03f).Normalized(), upper, fore);
            }
            case BodyAct.Shrug:
            {
                // Elbows at the sides, forearms out and up, palms up.
                float lift = 0.1f * MathF.Max(0f, MathF.Sin(t * 3f));
                return Bones(shoulder, F.V(0.35f, -0.85f, 0.25f), F.V(0.55f, 0.62f + lift, 0.6f), upper, fore);
            }
            case BodyAct.Grieve:
            {
                // Face in the hands, elbows down before the chest; the shoulders shake.
                float sob = 0.05f * MathF.Sin(t * 7f) * MathF.Max(0f, MathF.Sin(t * 0.9f));
                return Bones(shoulder, F.V(0.1f, -0.68f, 0.72f), F.V(-0.55f, 0.8f + sob, 0.12f), upper, fore);
            }
            case BodyAct.Fidget: return Fidget(g, i, F, shoulder, neck, torso, upper, fore);
            case BodyAct.Taunt:
            {
                // A fist smacked into the other palm, again and again: come on, then.
                var Fl = new Frame(torso, -1f);
                Vector3 shoulderL = shoulder + torso.X * (-1f - side) * _shoulderOut; // the left shoulder, whichever arm this is
                ArmGoal palm = Bones(shoulderL, Fl.V(0.1f, -0.78f, 0.62f), Fl.V(-0.78f, 0.22f, 0.6f), f.UpperArmOf(0), f.ForearmOf(0));
                if (i == 0) return palm;
                float k = MathF.Pow(MathF.Max(0f, MathF.Sin(t * 6.5f)), 1.5f);
                ArmGoal cocked = Bones(shoulder, F.V(0.4f, -0.55f, -0.2f), F.V(-0.1f, 0.75f, 0.65f), upper, fore);
                Vector3 hit = palm.Hand + torso.X * (HandRadius * 2.2f) + F.Ahead * 0.01f;
                return new ArmGoal(cocked.Hand.Lerp(hit, k), cocked.Pole.Lerp(F.V(0.5f, -0.85f, -0.1f), k).Normalized());
            }
            case BodyAct.Greet when g.Style == 1:
                // An open palm held up to them, still.
                return Bones(shoulder, F.V(0.3f, -0.2f, 0.92f), F.V(0.05f, 1f, 0.1f), upper, fore);
            case BodyAct.Greet when g.Style == 2:
                return OnChest(F, torso, side, 0.78f, 0f);
            case BodyAct.Greet:
            {
                // Arm up, the forearm swinging from the elbow: a wave from far off.
                float w = 0.35f * MathF.Sin(t * 6f);
                return Bones(shoulder, F.V(0.65f, 0.55f, 0.35f), F.V(0.12f + w, 1f, 0.1f), upper, fore);
            }
            case BodyAct.Thanks when g.Style == 1:
            {
                // Both palms offered, low and forward, with a bow.
                float dip = 0.06f * MathF.Sin(t * 2f);
                return Bones(shoulder, F.V(0.2f, -0.85f, 0.48f), F.V(0.22f, 0.18f + dip, 0.95f), upper, fore);
            }
            case BodyAct.Thanks when g.Style == 2:
                // Both hands flat on the heart, one over the other, and a deep bow.
            {
                if (i == 1) return OnChest(F, torso, side, 0.74f, 0f);
                ArmGoal over = OnChest(new Frame(torso, 1f), torso, 1f, 0.68f, 0.035f);
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
                return new ArmGoal(brow.Lerp(outHand, k), F.V(0.85f, -0.25f, -0.15f));
            }
            case BodyAct.Play:
            {
                // Clapping, hands meeting before the chest, bouncing on the toes.
                float c = 0.5f + 0.5f * MathF.Cos(t * 9f);
                Vector3 mid = Shell != null ? Pose[_body] * Shell.FrontAt(f.Torso * 0.66f, 0f) : neck - torso.Y * f.Torso * 0.3f;
                Vector3 hand = mid + F.Ahead * (0.2f + 0.04f * MathF.Sin(t * 2f)) + F.Out * (HandRadius * 1.2f + 0.16f * c) + F.Up * 0.04f * MathF.Sin(t * 4.5f);
                return new ArmGoal(hand, F.V(0.6f, -0.8f, -0.1f));
            }
            case BodyAct.Warn:
                // Stop: the arm out, the palm up and out at them.
                return Bones(shoulder, F.V(0.18f, -0.05f, 0.98f), F.V(0.02f, 0.85f, 0.5f), upper, fore);
            case BodyAct.Celebrate:
            {
                float e = MathF.Max(0f, MathF.Sin(t * 8f + i * 1.3f));
                return Bones(shoulder, F.V(0.45f, 0.85f, 0.15f), F.V(-0.2f, 0.3f, 0.4f).Lerp(F.V(0.05f, 1f, 0.05f), e).Normalized(), upper, fore);
            }
            case BodyAct.Panic:
            {
                // Hands to the head, then flung about, then to the head again.
                float k = Mathf.SmoothStep(-0.3f, 0.3f, MathF.Sin(t * 1.6f + i));
                Vector3 c = neck + head.Y * (f.Neck + f.HeadRadius) + F.Out * (f.HeadRadius + HandRadius + 0.01f);
                ArmGoal clutch = new(c, F.V(0.85f, 0.25f, -0.2f));
                ArmGoal flail = Bones(shoulder, F.V(0.6f, 0.15f + 0.35f * MathF.Sin(t * 11f + i * 2f), 0.4f), F.V(0.3f, 0.6f, 0.5f + 0.4f * MathF.Sin(t * 9f + i)), upper, fore);
                return new ArmGoal(clutch.Hand.Lerp(flail.Hand, k), clutch.Pole.Lerp(flail.Pole, k).Normalized());
            }
            case BodyAct.Present:
                // A hand swept out to the wares, palm up.
                return Bones(shoulder, F.V(0.35f, -0.8f, 0.45f), F.V(0.65f, 0.18f, 0.75f), upper, fore);
            case BodyAct.Startle:
                return Bones(shoulder, F.V(0.6f, -0.45f, 0.42f), F.V(0.15f, 0.95f, 0.3f), upper, fore);
        }
        return default;
    }

    /// <summary>A hand laid flat on the chest (over the heart, across from its own shoulder), the elbow out and down.</summary>
    ArmGoal OnChest(Frame F, Basis torso, float side, float height, float outward)
    {
        if (Shell == null) return default;
        float y = _f.Torso * height;
        float x = -side * Shell.HalfWidth(y) * 0.32f;
        Vector3 local = Shell.FrontAt(y, x);
        Vector3 hand = Pose[_body] * local + F.Ahead * (HandRadius + 0.012f + outward);
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
        return new ArmGoal(now.Hand.Lerp(next.Hand, blend), now.Pole.Lerp(next.Pole, blend).Normalized());
    }

    ArmGoal FidgetPose(int phase, float t, int i, Frame F, Vector3 shoulder, Vector3 neck, float upper, float fore)
    {
        switch (phase)
        {
            case 0:
            {
                // Hands together before the belt, wringing.
                float w = 0.18f * MathF.Sin(t * 5f + i * MathF.PI);
                return Bones(shoulder, F.V(0.14f, -0.9f, 0.35f), F.V(-0.72f, 0.2f + w, 0.66f), upper, fore);
            }
            case 1:
            {
                // The right hand rubbing the back of the neck; the left on the hip.
                if (i == 1)
                {
                    float s = 0.04f * MathF.Sin(t * 8f);
                    Vector3 nape = neck + F.Up * (_f.Neck * 0.8f) - F.Ahead * (_f.HeadRadius * 0.6f + HandRadius) + F.Out * (0.03f + s);
                    return new ArmGoal(nape, F.V(0.8f, 0.3f, 0.35f));
                }
                return Bones(shoulder, F.V(0.72f, -0.62f, -0.15f), F.V(-0.6f, -0.45f, 0.35f), upper, fore);
            }
            default:
            {
                // Hands to the hips, weight on one foot.
                return Bones(shoulder, F.V(0.72f, -0.62f, -0.15f), F.V(-0.6f, -0.45f, 0.35f), upper, fore);
            }
        }
    }
}
