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

    /// <summary>
    /// The way each arm's elbow points off the line from its shoulder to its hand, in the body's own frame, as last posed (zero: not
    /// yet). It turns from there toward the way asked no faster than an elbow swings, and a way round the body is looked for from it:
    /// an elbow never jumps round its circle between frames, whatever the way asked does (a pose blended through the line, a hand
    /// passing the shoulder, the body in the way one frame and not the next).
    /// </summary>
    readonly Vector3[] _way = new Vector3[2];

    /// <summary>How fast an elbow swings round the line from its shoulder to its hand, radians a second.</summary>
    const float ElbowSwing = 14f;

    /// <summary>How far each arm's shoulder and hand were moved off where they were asked last frame to keep the arm out of the body (body frame; shoulder, hand, by arm).</summary>
    readonly Vector3[] _shift = new Vector3[4];

    /// <summary>How fast those moves change, m a second.</summary>
    const float Shift = 2f;

    /// <summary>The part of <paramref name="v"/> square to <paramref name="axis"/> (a unit vector), unit length; zero if it has none.</summary>
    static Vector3 Square(Vector3 v, Vector3 axis)
    {
        v -= axis * v.Dot(axis);
        return v.LengthSquared() > 1e-8f ? v.Normalized() : Vector3.Zero;
    }

    /// <summary>The way an arm's elbow goes this frame: from where it went last, toward <paramref name="pole"/> as fast as an elbow swings.</summary>
    Vector3 ElbowWay(int arm, Vector3 shoulder, Vector3 hand, Vector3 pole, Vector3 outward)
    {
        Vector3 axis = (hand - shoulder).Normalized(), want = Square(pole, axis);
        if (want == Vector3.Zero) want = Square(outward, axis);
        Vector3 was = Square(Pose[_body].Basis * _way[arm], axis);
        if (was == Vector3.Zero || _dt <= 0f) return want;
        float a = MathF.Atan2(axis.Dot(was.Cross(want)), was.Dot(want)), step = ElbowSwing * _dt;
        return MathF.Abs(a) <= step ? want : was.Rotated(axis, MathF.Sign(a) * step);
    }

    /// <summary>
    /// Shoulder to hand by IK, the elbow toward the pole (turned to it as an elbow swings, <see cref="ElbowWay"/>), kept out of the
    /// torso: a hand asked inside it (or the head) is moved off its skin, and an arm that would pass through swings its elbow round
    /// (as a real one does) to the nearest way that clears from where it was and, failing that, takes the hand further out to the
    /// side. A hand on a weapon stays on its grip (the hold keeps the weapon itself clear of the body). Also the elbow's hinge (square
    /// to the arm's bend, toward the body's right with the elbow back): the arm's bones roll with it.
    /// </summary>
    (Vector3 elbow, Vector3 wrist) Arm(Transform3D body, ref Vector3 shoulder, Vector3 hand, Vector3 pole, float upper, float fore, in Frame F, int arm, in HandSpec spec, Vector3 grips, float gripW, Vector3 gripFore, bool held, out Vector3 hinge)
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
        pole = ElbowWay(arm, shoulder, hand, pole, outward);
        Vector3 askedShoulder = shoulder, askedHand = hand, wasWay = _way[arm];
        // The arm as found, its shoulder and hand moved off where they were asked by no more than they were last frame give or take
        // what a limb moves in a frame (Shift), and its elbow swung round from the way it went as far as an elbow swings:
        // what clears the body changes in a step between frames (a coat grazed, or no longer), the arm never does.
        (Vector3 e, Vector3 w) Done(Vector3 sh, (Vector3 e, Vector3 w) r, Vector3 way, out Vector3 h, out Vector3 shoulderAt)
        {
            Basis b = body.Basis, bi = b.Inverse();
            Vector3 cs = bi * (sh - askedShoulder), ch = bi * (r.w - askedHand);
            if (_dt > 0f) (cs, ch) = (_shift[arm * 2].MoveToward(cs, Shift * _dt), _shift[arm * 2 + 1].MoveToward(ch, Shift * _dt));
            (_shift[arm * 2], _shift[arm * 2 + 1]) = (cs, ch);
            (shoulderAt, sh) = (askedShoulder + b * cs, askedShoulder + b * cs);
            Vector3 hd = askedHand + b * ch, along = (hd - sh).Normalized();
            way = Square(way, along);
            Vector3 was = Square(b * wasWay, along);
            if (way == Vector3.Zero) way = was != Vector3.Zero ? was : Square(outward, along);
            if (was != Vector3.Zero && _dt > 0f)
            {
                float a = MathF.Atan2(along.Dot(was.Cross(way)), was.Dot(way)), step = ElbowSwing * _dt;
                if (MathF.Abs(a) > step) way = was.Rotated(along, MathF.Sign(a) * step);
            }
            if (way != Vector3.Zero) _way[arm] = bi * way;
            h = way != Vector3.Zero ? way.Cross(along) : outward;
            return LeggedRig.SolveIK(sh, hd, upper, fore, way);
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
            return Done(shoulder, (e, w), pole, out hinge, out shoulder);
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
        if (Shell == null) return Done(shoulder, (e, w), pole, out hinge, out shoulder);
        float least = Through(shoulder, e, w, !clear);
        if (least <= 0f) return Done(shoulder, (e, w), pole, out hinge, out shoulder);
        // Through the body: the elbow round its circle to the nearest way that clears it (from the way it went last, found to a degree
        // or two); failing all of them, the shoulder brought forward a little (as one reaching across the chest does), then the hand a
        // step out to the side, forward or up, by turns, and round again: only as far into the last step as clears it. Nothing is
        // taken in whole steps, so nothing jumps a step between frames.
        (Vector3, Vector3) fallback = (e, w);
        Vector3 fallbackShoulder = shoulder, fallbackWay = pole;
        const float Notch = MathF.Tau / 16f;
        // The way nearest the way it went that clears, within this far round (radians), with the arm; false if none.
        bool Round(Vector3 sh, Vector3 hd, float most, out (Vector3 e, Vector3 w) arm, out Vector3 way)
        {
            Vector3 axis = (hd - sh).Normalized();
            Vector3 b1 = Square(pole, axis);
            if (b1 == Vector3.Zero) b1 = Square(outward, axis);
            Vector3 b2 = axis.Cross(b1);
            bool Clears(float a, out (Vector3, Vector3) r, out Vector3 p)
            {
                p = b1 * MathF.Cos(a) + b2 * MathF.Sin(a);
                r = LeggedRig.SolveIK(sh, hd, upper, fore, p);
                float sunk = Through(sh, r.Item1, r.Item2, true);
                if (sunk > 0f && sunk < least) (least, fallback, fallbackShoulder, fallbackWay) = (sunk, r, sh, p);
                return sunk <= 0f;
            }
            for (int j = 0; j <= 8 && j * Notch <= most + 1e-3f; j++)
                for (int side = 1; side >= (j == 0 || j == 8 ? 1 : -1); side -= 2)
                {
                    if (!Clears(side * j * Notch, out arm, out way)) continue;
                    // (Between the last notch that was in the way and this one: as near the way it went as clears, to 1.4 degrees.)
                    float lo = side * (j - 1) * Notch, hi = side * j * Notch;
                    for (int n = 0; j > 0 && n < 4; n++)
                    {
                        float mid = (lo + hi) * 0.5f;
                        if (Clears(mid, out var r, out Vector3 p)) (hi, arm, way) = (mid, r, p);
                        else lo = mid;
                    }
                    return true;
                }
            (arm, way) = (default, Vector3.Zero);
            return false;
        }
        for (int k = 0; k < 17; k++)
        {
            float most = k >= 6 ? MathF.PI : 0.79f;
            if (Round(shoulder, hand, most, out var found, out Vector3 way))
            {
                if (k > 0)
                {
                    // Only as far into the last step as clears.
                    Vector3 sh0 = shoulder - (k <= 2 ? ahead * 0.025f : Vector3.Zero), hd0 = hand - (k > 2 ? Step(k - 1) : Vector3.Zero);
                    float lo = 0f, hi = 1f;
                    Vector3 sh = shoulder, hd = hand;
                    for (int n = 0; n < 4; n++)
                    {
                        float t = (lo + hi) * 0.5f;
                        Vector3 s1 = sh0.Lerp(shoulder, t), h1 = hd0.Lerp(hand, t);
                        if (Round(s1, h1, most, out var r, out Vector3 p)) (hi, found, way, sh) = (t, r, p, s1);
                        else lo = t;
                    }
                    shoulder = sh;
                }
                return Done(shoulder, found, way, out hinge, out shoulder);
            }
            if (k < 2) shoulder += ahead * 0.025f;
            else hand += Step(k);
        }
        Vector3 Step(int k) => (k % 3 == 0 ? outward : k % 3 == 1 ? ahead : body.Basis.Y) * 0.05f;
        shoulder = fallbackShoulder;
        return Done(shoulder, fallback, fallbackWay, out hinge, out shoulder);
    }

    /// <summary>
    /// A mark the arms are turned to (a weapon's, a pointing hand's), as far as they have swung to it: round from the body's heading and
    /// up from level, radians (yaw NaN: none yet). A new mark, or one passing behind the body, is swung onto across the front, never
    /// snapped to; once on it, it is the mark itself.
    /// </summary>
    struct Swing
    {
        float _yaw, _pitch;
        public Swing() => _yaw = float.NaN;

        /// <summary>How fast arms swing onto a new mark, radians a second; how far round from the body's heading they point at most (the waist's turn and the arms').</summary>
        const float Rate = 8f, Round = 1.5f;

        public Vector3? Toward(Vector3? mark, Vector3 from, float heading, float dt)
        {
            Vector3 to = (mark ?? from) - from;
            float flat = new Vector2(to.X, to.Z).Length();
            if (mark == null) _yaw = float.NaN;
            if (mark == null || flat < 0.3f) return mark;
            float yaw = Math.Clamp(Geo.WrapAngle(MathF.Atan2(-to.X, -to.Z) - heading), -Round, Round), pitch = MathF.Atan2(to.Y, flat);
            if (float.IsNaN(_yaw) || dt <= 0f) (_yaw, _pitch) = (yaw, pitch);
            else (_yaw, _pitch) = (Mathf.MoveToward(_yaw, yaw, Rate * dt), Mathf.MoveToward(_pitch, pitch, Rate * dt));
            return from + new Basis(Vector3.Up, heading + _yaw) * new Vector3(0f, MathF.Sin(_pitch), -MathF.Cos(_pitch)) * to.Length();
        }
    }

    Swing _aimed = new(), _shown = new();

    /// <summary>The gesture with its marks (<see cref="Gesture.AimAt"/>, <see cref="Gesture.ActAt"/>) where the arms have swung to so far.</summary>
    Gesture Aimed(in Gesture g)
    {
        Vector3 from = g.Feet + Vector3.Up * _f.Height * 0.75f;
        Gesture aimed = g;
        aimed.AimAt = _aimed.Toward(g.AimAt, from, g.Yaw, _dt);
        aimed.ActAt = _shown.Toward(g.Act == BodyAct.None ? null : g.ActAt, from, g.Yaw, _dt) ?? g.ActAt;
        return aimed;
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
    ArmGoal ActArm(in Gesture g, int i, Vector3 shoulder, Vector3 neck, Basis torso, Basis head, float upper, float fore)
    {
        float side = i == 0 ? -1f : 1f;
        var F = new Frame(torso, side);
        float t = g.Clock, arm = upper + fore;
        Figure f = _f;
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
