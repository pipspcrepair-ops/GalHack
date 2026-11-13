using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Wildlife;

/// <summary>
/// The owner (13 Nov 2026): hands too small and facing no way, so a warning cannot be told from a wave at
/// a distance. Every hand is a real adult's size and faces the way its pose says, on fresh bodies.
/// </summary>
public partial class BestiaryTest
{
    /// <summary>Whether a world point is inside a hand's box (shrunk by <paramref name="slack"/>), or, for <paramref name="slabOnly"/>, what lies behind its palm alone (a fist closed on a grip holds what is in front of that).</summary>
    static bool InHand(HumanRig rig, int i, Vector3 world, bool slabOnly, float slack = 0.003f)
    {
        var (at, shape) = rig.HandOf(i);
        float s = rig.HandSizeOf(i);
        var (lo, hi) = HandMake.Box(shape, i == 0, s);
        Vector3 l = at.AffineInverse() * world;
        if (slabOnly) (lo, hi) = (new Vector3(lo.X * 0.8f, 0.005f * s, -HandMake.Half * s), new Vector3(hi.X * 0.8f, 0.09f * s, -0.003f * s));
        return l.X > lo.X + slack && l.X < hi.X - slack && l.Y > lo.Y + slack && l.Y < hi.Y - slack && l.Z > lo.Z + slack && l.Z < hi.Z - slack;
    }

    /// <summary>
    /// Real hands, facing the way the pose says: a stop with the palm ahead and the fingers up, hands up
    /// palms ahead, a shrug, an offering and a wave-off with the palm up or down, fists on blows, taunts,
    /// cheers and grips, a pointing hand on a point; the fingers go on from the forearm; and an adult's hand
    /// is the size of one (0.19 m, times what makes a gesture read at 30 m).
    /// </summary>
    void HandsFaceTheWay()
    {
        var wrong = new Dictionary<string, int>();
        int people = 0, small = 0, checks = 0;
        string who = "";
        void Want(bool ok, string what)
        {
            checks++;
            if (!ok) wrong[what + who] = wrong.GetValueOrDefault(what + who) + 1;
        }
        foreach (Species s0 in ManyBodies(24).Where(s => !s.Husk))
        {
            Species s = s0.Copy();
            s.Figure = s0.Figure.Clone();
            s.Figure.Prop = "";
            var rig = new HumanRig(s);
            people++;
            Figure f = s.Figure;
            who = $" [{f.Folk?.Id ?? "human"} {f.Stage} {f.Height:F2} m, {f.Dress}, padding {f.Padding:F2}]";
            var (lo, hi) = HandMake.Box(HandShape.Open, false, HandMake.Size(f, 1));
            float ratio = (hi.Y - lo.Y) / (0.2f * HumanBody.Scale(f) * f.HandK * HandMake.Readable); // (gloves run a little bigger)
            if (!s.Mechanical && f.Stage == LifeStage.Adult && (ratio < 0.99f || ratio > 1.15f)) small++;
            Gesture At(BodyAct a, float clock, int style = 0) => new() { Feet = Vector3.Zero, Act = a, ActAmount = 1f, Clock = clock, Style = style, ActAt = new Vector3(0f, 1.2f, -4f) };
            int torso = rig.Layout.Pieces.FindIndex(p => p.Name == "body");
            // (An elder stoops: up and ahead are the body's own.)
            Vector3 Ahead() => -rig.Pose[torso].Basis.Z.Normalized();
            Vector3 Up() => rig.Pose[torso].Basis.Y.Normalized();
            (Vector3 palm, Vector3 fingers, HandShape shape) Hand(int i)
            {
                var (at, shape) = rig.HandOf(i);
                return (at.Basis.Z, at.Basis.Y, shape);
            }
            for (int k = 0; k < 6; k++)
            {
                float t = 0.3f + k * 0.77f;
                rig.Drive(At(BodyAct.Warn, t));
                {
                    var (palm, fingers, shape) = Hand(1);
                    Vector3 fore = (rig.ArmOf(1).wrist - rig.ArmOf(1).elbow).Normalized();
                    Want(palm.Dot(Ahead()) > 0.7f, "a stop: palm ahead");
                    Want(fingers.Dot(Up()) > 0.7f, "a stop: fingers up");
                    Want(shape == HandShape.Open, "a stop: open");
                    Want(fingers.Dot(fore) > 0.99f, "fingers go on from the forearm");
                }
                rig.Drive(At(BodyAct.HandsUp, t));
                for (int i = 0; i < 2; i++)
                {
                    var (palm, fingers, shape) = Hand(i);
                    Want(palm.Dot(Ahead()) > 0.7f && fingers.Dot(Up()) > 0.6f && shape == HandShape.Open, "hands up: palms ahead, fingers up");
                }
                rig.Drive(At(BodyAct.Shrug, t));
                for (int i = 0; i < 2; i++) Want(Hand(i).palm.Dot(Up()) > 0.6f, "a shrug: palms up");
                rig.Drive(At(BodyAct.Present, t));
                Want(Hand(1).palm.Dot(Up()) > 0.6f, "presenting: palm up");
                rig.Drive(At(BodyAct.Thanks, t, 1));
                for (int i = 0; i < 2; i++) Want(Hand(i).palm.Dot(Up()) > 0.6f, "thanks with palms offered: palms up");
                rig.Drive(At(BodyAct.Greet, t, 1));
                Want(Hand(1).palm.Dot(Ahead()) > 0.7f, "a greeting palm: palm ahead");
                rig.Drive(At(BodyAct.WaveOff, t));
                Want(Hand(1).palm.Dot(Up()) < -0.5f, "a wave-off: palm down");
                foreach (BodyAct a in new[] { BodyAct.Taunt, BodyAct.Celebrate })
                {
                    rig.Drive(At(a, t));
                    for (int i = 0; i < 2; i++) Want(Hand(i).shape == HandShape.Fist, $"{a}: fists");
                }
                rig.Drive(At(BodyAct.Point, t));
                {
                    var (sh, _, wr) = rig.ArmOf(1);
                    var (palm, fingers, shape) = Hand(1);
                    Want(shape == HandShape.Point && fingers.Dot((wr - sh).Normalized()) > 0.9f, "pointing: the first finger out, along the arm");
                }
            }
            // A blow: fists, both hands, wound up and thrown.
            foreach (float strike in new[] { 0.6f, 1f })
            {
                for (int k = 0; k < 6; k++) rig.Drive(new Gesture { Feet = Vector3.Zero, Blow = StrikeKind.Punch, Strike = strike * (k + 1) / 6f, Clock = k * 0.1f });
                for (int i = 0; i < 2; i++) Want(Hand(i).shape == HandShape.Fist, "a punch: fists");
            }
        }
        // Weapons are gripped with fists, knuckles along the weapon (the grip itself is WeaponsAreHeldRight's).
        foreach (string arm in new[] { "rifle", "pistol", "spear", "bow", "club" })
            foreach (Species s0 in ManyBodies(6).Where(s => !s.Husk && !s.Mechanical))
            {
                Species s = s0.Copy();
                s.Figure = s0.Figure.Clone();
                s.Figure.Prop = arm;
                var rig = new HumanRig(s);
                foreach (float aim in new[] { 0f, 1f })
                {
                    for (int k = 0; k < 4; k++) rig.Drive(new Gesture { Feet = Vector3.Zero, Aim = aim, Clock = k * 0.3f });
                    for (int i = 0; i < 2; i++)
                    {
                        if ((i == 1 ? rig.GripRightOn : rig.GripLeftOn) < 0.999f) continue;
                        var (at, shape) = rig.HandOf(i);
                        Vector3 axis = i == 1 ? rig.GripAxisRight : rig.GripAxisLeft;
                        Want(shape == HandShape.Fist && MathF.Abs(at.Basis.X.Dot(axis)) > 0.97f, $"a {arm} gripped in a fist, the knuckles along it");
                    }
                }
            }
        foreach (var (what, n) in wrong.OrderByDescending(kv => kv.Value).Take(8)) GD.Print($"        {n,4}  {what}");
        Check(small == 0, $"an adult's hand is 0.19 m times their height over 1.72, their hand size and the readability factor, a glove a little more ({small} of {people} off)");
        Check(wrong.Count == 0, $"hands face the way their pose says, in {checks} looks at {people} fresh people ({wrong.Values.Sum()} wrong)");
    }
}
