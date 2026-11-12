using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Wildlife;

/// <summary>
/// The owner (9 to 13 Nov 2026): arms through the body, gestures that don't read, a push for a
/// blow, a flinch too slight to see, the dead falling rigid. Fresh bodies every run.
/// </summary>
public partial class BestiaryTest
{
    /// <summary>Fresh people of many folk, builds and coats (the big chest pieces among them), Husks and walking machines.</summary>
    List<Species> ManyBodies(int people)
    {
        var list = new List<Species>();
        var roles = Enum.GetValues<PersonRole>();
        for (int w = 0; list.Count < people; w++)
        {
            WorldConditions world = World(_fresh.Range(-30f, 40f), alien: _fresh.Range(0f, 1f));
            PeopleGenerator g = Folks(world);
            for (int n = 0; n < 8 && list.Count < people; n++)
            {
                Species s = g.Person(Seed(), roles[_fresh.Int(0, roles.Length - 1)], _fresh.Chance(0.3f) ? Occupancy.Raider : Occupancy.Friendly, founders: g.FoundersOf(Seed()));
                if (n == 7 && Husks.CanTurn(s)) s = Husks.Turn(s, Seed(), _data.Husks);
                list.Add(s);
            }
            for (int n = 0; n < 6; n++)
            {
                Species r = Robots.Roll(world, _fresh.Int(0, 30));
                if (r.Rig == RigKind.Humanoid) { list.Add(r); break; }
            }
        }
        return list;
    }

    /// <summary>Every pose a body takes: each act in each of its forms, held and mid-motion; blows; holds; sitting, crouching, stalking, walking, running, drinking.</summary>
    IEnumerable<(string what, Gesture g)> EveryPose()
    {
        foreach (BodyAct act in Enum.GetValues<BodyAct>())
        {
            if (act == BodyAct.None) continue;
            int styles = act is BodyAct.Greet or BodyAct.Thanks ? 3 : 1;
            for (int style = 0; style < styles; style++)
                for (int k = 0; k < 10; k++)
                {
                    float amount = k == 0 ? 0.5f : 1f;
                    yield return ($"{act}{(styles > 1 ? style.ToString() : "")}", new Gesture { Act = act, ActAmount = amount, Style = style, Clock = 0.37f + k * 0.83f, ActAt = new Vector3(_fresh.Range(-3f, 3f), _fresh.Range(0.5f, 2f), -3f) });
                }
        }
        foreach (StrikeKind blow in new[] { StrikeKind.Punch, StrikeKind.Swat, StrikeKind.Seize, StrikeKind.Spew })
            for (int k = 0; k <= 24; k++) yield return ($"{blow}", new Gesture { Blow = blow, Strike = 1f - MathF.Abs(k - 12) / 12f, Clock = k * 0.1f });
        for (int k = 0; k < 6; k++)
        {
            float c = k * 0.7f;
            yield return ("stand", new Gesture { Clock = c });
            yield return ("walk", new Gesture { Speed = 1.3f, Walked = c * 1.3f, Clock = c });
            yield return ("run", new Gesture { Speed = 4f, Walked = c * 4f, Clock = c });
            yield return ("sit", new Gesture { Sit = 1f, Clock = c });
            yield return ("crouch", new Gesture { Crouch = 1f, Clock = c });
            yield return ("stalk", new Gesture { Stalk = 1f, Speed = 0.6f, Walked = c * 0.6f, Clock = c });
            yield return ("work", new Gesture { Work = 1f, Clock = c });
            yield return ("aim", new Gesture { Aim = 1f, Clock = c, Look = k * 0.15f - 0.4f });
            yield return ("aim-walk", new Gesture { Aim = 1f, Speed = 1.3f, Walked = c * 1.3f, Clock = c });
            yield return ("sip", new Gesture { Sip = 1f, Clock = c });
            yield return ("reach", new Gesture { Reach = 1f, Clock = c });
        }
    }

    static readonly string[] Carried = { "", "", "bottle", "cane", "umbrella", "wrench", "hoe", "rifle", "pistol", "spear", "bow", "machete", "club" };

    /// <summary>
    /// The owner (9 Nov): "the game doesn't know which way arms should go". Many fresh bodies, each
    /// carrying something different, through every act, blow, hold and stance: no arm is ever through
    /// the torso (measured on the torso as it is built, coat and chest pieces and all), and no pose
    /// stretches an arm.
    /// </summary>
    void NoArmThroughTheBody()
    {
        int poses = 0, through = 0, stretched = 0, bodies = 0;
        float worst = 0f;
        string worstWhat = "";
        var byWhat = new Dictionary<string, int>();
        foreach (Species s0 in ManyBodies(60))
        {
            Species s = s0;
            if (!s.Mechanical && !s.Husk)
            {
                s = s0.Copy();
                s.Figure = s0.Figure.Clone();
                s.Figure.Prop = Carried[bodies % Carried.Length];
            }
            bodies++;
            var rig = new HumanRig(s);
            int body = rig.Layout.Pieces.FindIndex(p => p.Name == "body");
            foreach (var (what, g0) in EveryPose())
            {
                Gesture g = g0;
                g.Feet = Vector3.Zero;
                g.Yaw = 0.7f;
                rig.Drive(g);
                poses++;
                for (int i = 0; i < 2; i++)
                {
                    if ((i == 1 ? rig.GripRightOn : rig.GripLeftOn) >= 0.5f) continue; // (a hand on a weapon: WeaponsAreHeldRight keeps the weapon out of the body)
                    var (sh, el, wr) = rig.ArmOf(i);
                    float sunk = rig.Sunk(rig.Pose[body], sh, el, wr);
                    float arm = s.Figure.UpperArmOf(i) + s.Figure.ForearmOf(i);
                    if (sh.DistanceTo(wr) > arm * 1.01f) stretched++;
                    if (sunk <= 0.002f) continue;
                    through++;
                    byWhat[what] = byWhat.GetValueOrDefault(what) + 1;
                    if (sunk > worst) { worst = sunk; worstWhat = $"{what} ({(i == 0 ? "left" : "right")} arm, {s.CommonName}, {s.Figure.Dress}, padding {s.Figure.Padding:F2}, carrying '{s.Figure.Prop}')"; }
                }
            }
        }
        if (through > 0) GD.Print($"  (arms through the body by pose: {string.Join(", ", byWhat.OrderByDescending(kv => kv.Value).Take(12).Select(kv => $"{kv.Key} {kv.Value}"))}; worst {worst * 100f:F1} cm, {worstWhat})");
        Check(through == 0, $"no arm passes through the torso: {through} limb-through-torso overlaps in {poses} poses of {bodies} fresh bodies (every act, blow, hold and stance)");
        Check(stretched == 0, $"and no pose stretches an arm past its length ({stretched})");
    }

    /// <summary>The owner's shots, pose by pose: each act reads as itself.</summary>
    void ActsReadAsThemselves()
    {
        int bodies = 0;
        int cowerUp = 0, grieveUp = 0, beckonOut = 0, pointAcross = 0, shrugDown = 0, fidgetStill = 0, thanksSame = 0, sipOff = 0, sipUpright = 0, bowOff = 0, waveOffAcross = 0;
        foreach (Species s0 in ManyBodies(24).Where(s => !s.Mechanical && !s.Husk))
        {
            bodies++;
            Species s = s0.Copy();
            s.Figure = s0.Figure.Clone();
            s.Figure.Prop = "bottle";
            var rig = new HumanRig(s);
            Figure f = s.Figure;
            float coat = 0.4f * MathF.Max(0f, f.Padding - 1f); // a thick coat takes the elbows out with it
            int body = rig.Layout.Pieces.FindIndex(p => p.Name == "body");
            Vector3 Local(Vector3 p) => rig.Pose[body].AffineInverse() * p; // the body's own frame: +Y up its back, -Z its front
            Gesture At(BodyAct a, float clock, int style = 0) => new() { Feet = Vector3.Zero, Act = a, ActAmount = 1f, Clock = clock, Style = style, ActAt = new Vector3(0f, 1.2f, -4f) };
            for (int k = 0; k < 6; k++)
            {
                float t = 0.3f + k * 0.77f;
                foreach (BodyAct a in new[] { BodyAct.Cower, BodyAct.Grieve })
                {
                    rig.Drive(At(a, t));
                    for (int i = 0; i < 2; i++)
                    {
                        var (sh, el, wr) = rig.ArmOf(i);
                        // Elbows down: below the shoulder and below the hand.
                        if (Local(el).Y > Local(sh).Y - 0.05f || Local(el).Y > Local(wr).Y) { if (a == BodyAct.Cower) cowerUp++; else grieveUp++; }
                    }
                }
                rig.Drive(At(BodyAct.Beckon, t));
                {
                    var (sh, el, wr) = rig.ArmOf(1);
                    if (Local(el).Y > Local(sh).Y - 0.1f || Local(el).X - Local(sh).X > 0.18f + coat) beckonOut++;
                }
                rig.Drive(At(BodyAct.WaveOff, t));
                {
                    var (sh, el, wr) = rig.ArmOf(1);
                    if (Local(el).X - Local(sh).X > 0.2f + coat || Local(el).Y > Local(sh).Y - 0.1f || Local(wr).X < -0.05f) waveOffAcross++;
                }
                rig.Drive(At(BodyAct.Point, t));
                {
                    var (sh, el, wr) = rig.ArmOf(1);
                    Vector3 d = (wr - sh).Normalized();
                    if (d.Dot(Vector3.Forward) < 0.85f) pointAcross++;
                }
                rig.Drive(At(BodyAct.Shrug, t));
                for (int i = 0; i < 2; i++)
                {
                    var (sh, el, wr) = rig.ArmOf(i);
                    if (Local(wr).Y < Local(el).Y + 0.05f || Local(el).Y > Local(sh).Y - 0.12f + coat) shrugDown++;
                }
            }
            // Fidgeting moves: over ten seconds each hand goes somewhere else.
            var seen = new List<Vector3>();
            for (int k = 0; k < 40; k++)
            {
                rig.Drive(At(BodyAct.Fidget, k * 0.25f));
                seen.Add(Local(rig.ArmOf(1).wrist));
            }
            float travel = seen.Max(p => seen.Max(q => p.DistanceTo(q)));
            if (travel < 0.3f) fidgetStill++;
            // Thanks in its three forms, and the greeting in its three, are six different things.
            var hands = new List<(Vector3, Vector3)>();
            foreach (BodyAct a in new[] { BodyAct.Thanks, BodyAct.Greet })
                for (int style = 0; style < 3; style++)
                {
                    rig.Drive(At(a, 1.1f, style));
                    hands.Add((Local(rig.ArmOf(0).wrist), Local(rig.ArmOf(1).wrist)));
                }
            for (int a = 0; a < hands.Count; a++)
                for (int b = a + 1; b < hands.Count; b++)
                    if (hands[a].Item1.DistanceTo(hands[b].Item1) + hands[a].Item2.DistanceTo(hands[b].Item2) < 0.12f) thanksSame++;
            // The sip: the bottle's neck at the lips, tipped up; not a pole before the face.
            rig.Drive(new Gesture { Feet = Vector3.Zero, Sip = 1f, Clock = 1f });
            int prop = rig.Layout.Pieces.FindIndex(p => p.Name == "prop");
            int head = rig.Layout.Pieces.FindIndex(p => p.Name == "head");
            Transform3D bottle = rig.Pose[prop], h = rig.Pose[head];
            Vector3 neckTip = bottle * new Vector3(0f, 0.19f * HumanBody.Scale(f), 0f);
            Vector3 mouth = h.Origin + h.Basis.Y * (f.Neck + f.HeadRadius * 0.55f) - h.Basis.Z * f.HeadRadius * 0.95f;
            if (neckTip.DistanceTo(mouth) > 0.05f) sipOff++;
            if (bottle.Basis.Y.Normalized().Y > 0.2f) sipUpright++;
            // The bow: the bow arm straight at the target, the string hand back at the cheek, the elbow along the arrow.
            Species a2 = s0.Copy();
            a2.Figure = s0.Figure.Clone();
            a2.Figure.Prop = "bow";
            var archer = new HumanRig(a2);
            archer.Drive(new Gesture { Feet = Vector3.Zero, Aim = 1f, Clock = 1f });
            var (lsh, _, lwr) = archer.ArmOf(0);
            var (_, rel, rwr) = archer.ArmOf(1);
            float armL = a2.Figure.UpperArmOf(0) + a2.Figure.ForearmOf(0);
            Vector3 line = (lwr - rwr).Normalized();
            Vector3 off = (rel - rwr) - line * (rel - rwr).Dot(line);
            if (lsh.DistanceTo(lwr) < armL * 0.93f || (lwr - lsh).Normalized().Dot(Vector3.Forward) < 0.8f || off.Length() > 0.18f) bowOff++; // (a draw's elbow rides a little above the arrow)
        }
        Check(cowerUp == 0 && grieveUp == 0, $"cowering and grieving keep the elbows down ({cowerUp}, {grieveUp} elbows up, {bodies} people)");
        Check(beckonOut == 0, $"a beckon keeps the elbow down by the side ({beckonOut} out)");
        Check(waveOffAcross == 0, $"a wave-off flicks out from a low elbow, never across the body ({waveOffAcross})");
        Check(pointAcross == 0, $"pointing at something ahead points straight ahead ({pointAcross} across the body)");
        Check(shrugDown == 0, $"a shrug's forearms come up, the elbows by the sides ({shrugDown} hanging)");
        Check(fidgetStill == 0, $"fidgeting moves, never one held pose ({fidgetStill} still)");
        Check(thanksSame == 0, $"thanks in each form and the greetings are all different ({thanksSame} pairs alike)");
        Check(sipOff == 0 && sipUpright == 0, $"a sip puts the bottle's neck to the lips, tipped up ({sipOff} off the lips, {sipUpright} upright)");
        Check(bowOff == 0, $"a drawn bow: the bow arm straight at the target, the string hand at the cheek, its elbow along the arrow ({bowOff} wrong)");
    }

    /// <summary>A blow is a punch or a swat (or a Husk's seize, its spew), with motion you can see: never a push with both arms.</summary>
    void BlowsAreBlows()
    {
        int still = 0, pushes = 0, blows = 0;
        foreach (Species s in ManyBodies(30))
        {
            var rig = new HumanRig(s);
            StrikeKind kind = HumanRig.BlowOf(s);
            if (s.Husk && s.Spit != SpitKind.None && _fresh.Chance(0.5f)) kind = StrikeKind.Spew;
            var at = new Dictionary<float, (Vector3 l, Vector3 r, Vector3 head)>();
            int head = rig.Layout.Pieces.FindIndex(p => p.Name == "head");
            foreach (float k in new[] { 0f, 0.4f, 0.56f })
            {
                rig.Drive(new Gesture { Feet = Vector3.Zero, Blow = kind, Strike = k == 0f ? 0f : k < 0.5f ? 0.75f : 1f, Clock = 1f });
                at[k] = (rig.ArmOf(0).wrist, rig.ArmOf(1).wrist, rig.Pose[head].Origin);
            }
            blows++;
            float moved = MathF.Max(at[0.4f].r.DistanceTo(at[0.56f].r), at[0.4f].l.DistanceTo(at[0.56f].l));
            float headMoved = at[0f].head.DistanceTo(at[0.56f].head);
            if (kind == StrikeKind.Spew ? headMoved < 0.12f : moved < 0.3f) still++;
            // A push: both hands thrown straight out together in a blow that should be one-handed.
            if (kind is StrikeKind.Punch or StrikeKind.Swat)
            {
                float ahead = -at[0.56f].l.Z + at[0f].l.Z;
                if (ahead > 0.25f && at[0.56f].l.DistanceTo(at[0.56f].r) < 0.25f) pushes++;
            }
        }
        Check(still == 0, $"every blow moves: the striking hand travels 30 cm or more from wind-up to strike, a spew throws the head ({still} of {blows} still)");
        Check(pushes == 0, $"no blow is a two-handed push, people's and machines' alike ({pushes})");
    }

    /// <summary>A hit shows: the part struck snaps away from it at once, and a heavy one staggers the body a step.</summary>
    void HitsShow()
    {
        int faint = 0, noStagger = 0, n = 0;
        foreach (Species s in ManyBodies(16))
        {
            var rig = new HumanRig(s);
            int head = rig.Layout.Pieces.FindIndex(p => p.Name == "head");
            for (int k = 0; k < 3; k++) rig.Drive(new Gesture { Feet = Vector3.Zero, Clock = k / 60f });
            Vector3 chest0 = rig.ChestPoint, head0 = rig.HeadPoint, hip0 = rig.Pose[0].Origin;
            Vector3 dir = new Vector3(_fresh.Range(-1f, 1f), 0f, _fresh.Range(-1f, 1f)).Normalized();
            bool atHead = _fresh.Chance(0.5f);
            rig.Struck(atHead ? head0 : chest0, dir, 0.3f);
            for (int k = 3; k < 8; k++) rig.Drive(new Gesture { Feet = Vector3.Zero, Clock = k / 60f });
            Vector3 moved = atHead ? rig.HeadPoint - head0 : rig.ChestPoint - chest0;
            n++;
            if (moved.Dot(dir) < 0.05f) faint++;
            rig.Struck(chest0, dir, 1f);
            float most = 0f;
            for (int k = 8; k < 40; k++)
            {
                rig.Drive(new Gesture { Feet = Vector3.Zero, Clock = k / 60f });
                most = MathF.Max(most, (rig.Pose[0].Origin - hip0).Dot(dir));
            }
            if (most < 0.12f) noStagger++;
        }
        Check(faint == 0, $"a hit snaps the struck part 5 cm or more away from it within a tenth of a second ({faint} of {n} faint)");
        Check(noStagger == 0, $"a heavy hit staggers the body 12 cm or more along it ({noStagger} of {n} didn't)");
    }

    /// <summary>The dead go limp and fall, then settle on the ground and stay: never rigid, never floating, never sunk.</summary>
    void TheDeadFallLimp()
    {
        int unsettled = 0, floating = 0, sunk = 0, rigid = 0, bad = 0, n = 0;
        float worstGap = 0f;
        foreach (Species s in ManyBodies(20))
        {
            var rig = new HumanRig(s);
            // On a slope, some of them: the ground is wherever it is.
            float slope = _fresh.Chance(0.5f) ? _fresh.Range(0f, 0.35f) : 0f;
            rig.Ground = p => p.X * slope;
            float clock = 0f;
            for (int k = 0; k < 3; k++) rig.Drive(new Gesture { Feet = Vector3.Zero, Clock = clock += 1f / 60f, Act = _fresh.Chance(0.3f) ? BodyAct.HandsUp : BodyAct.None, ActAmount = 1f });
            int body = rig.Layout.Pieces.FindIndex(p => p.Name == "body");
            int arm = rig.Layout.Pieces.FindIndex(p => p.Name == "arm1u");
            Basis armToBody0 = rig.Pose[body].Basis.Inverse() * rig.Pose[arm].Basis;
            rig.Struck(rig.ChestPoint, new Vector3(_fresh.Range(-1f, 1f), 0f, _fresh.Range(-1f, 1f)), _fresh.Range(0.3f, 1f));
            for (int k = 0; k < 600 && !rig.Settled; k++)
            {
                rig.Drive(new Gesture { Feet = Vector3.Zero, Clock = clock += 1f / 60f, Dead = true, Down = 1f });
                foreach (Transform3D t in rig.Pose) if (!Geo.Finite(t)) bad++;
            }
            n++;
            if (!rig.Settled) unsettled++;
            float gap = rig.DeadGap(p => p.X * slope);
            if (gap > 0.02f) floating++;
            if (gap < -0.01f) sunk++;
            worstGap = MathF.Max(worstGap, MathF.Abs(gap));
            // Limp: the arm's set to the body has changed (it isn't the last pose turned over).
            Basis armToBody = rig.Pose[body].Basis.Inverse() * rig.Pose[arm].Basis;
            if ((armToBody.Y - armToBody0.Y).Length() < 0.05f) rigid++;
            // And it lies down: the chest near the ground.
            if (rig.ChestPoint.Y - rig.ChestPoint.X * slope > s.Figure.Height * 0.4f) floating++;
        }
        Check(bad == 0, "a falling body stays finite");
        Check(unsettled == 0, $"every dead body settles within ten seconds ({unsettled} of {n} still moving)");
        Check(floating == 0 && sunk == 0, $"and lies on the ground: nothing floating, nothing sunk ({floating} floating, {sunk} sunk; worst gap {worstGap * 100f:F1} cm)");
        Check(rigid == 0, $"a body falls limp, not in its last pose ({rigid} of {n} rigid)");
    }
}
