using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Godot;

namespace Wildlife;

/// <summary>Writes where a reference person's shoulders, elbows, wrists, feet and carried thing are in a set of poses (body frame, chest origin), for the pose editor.</summary>
public partial class BestiaryTest
{
    void DumpPoses(string path)
    {
        PeopleGenerator pg = Folks(World(15f));
        ulong seed0 = Seed(), seed1 = Seed();
        Species Fresh()
        {
            Species sp = pg.Person(seed0, PersonRole.Worker, Occupancy.Friendly, founders: pg.FoundersOf(seed1));
            Figure ff = sp.Figure;
            ff.Sex = Sex.Male; ff.Stage = LifeStage.Adult; ff.Folk = new Folk(); ff.Height = 1.72f; ff.Build = 0.5f; ff.Stoop = 0f; ff.Stagger = 0f; ff.Padding = 1f;
            ff.LegK = ff.ArmK = ff.NeckK = ff.HeadK = ff.HandK = ff.FootK = ff.HipK = ff.ShoulderK = 1f;
            ff.LongArm = -1; ff.ArmStretch = 1f; ff.Husk = false; ff.Dress = "shirt"; ff.Wrongs = Wrong.None; ff.HeadTilt = ff.HeadDroop = ff.JawDrop = 0f;
            ff.Measure();
            return sp;
        }
        Species s = Fresh();
        Figure f = s.Figure;
        float off = (s.Seed % 97) / 97f, strideWalk = f.LegLength * 1.36f, strideRun = f.LegLength * 2.5f, strideStalk = f.LegLength * 1.36f * 0.65f;
        var sb = new StringBuilder();
        sb.Append("{\"figure\":{\"height\":").Append(F(f.Height)).Append(",\"torso\":").Append(F(f.Torso)).Append(",\"neck\":").Append(F(f.Neck)).Append(",\"head\":").Append(F(f.HeadRadius))
          .Append(",\"upper\":").Append(F(f.UpperArm)).Append(",\"fore\":").Append(F(f.Forearm)).Append(",\"shoulderW\":").Append(F(f.ShoulderW)).Append(",\"chestD\":").Append(F(f.ChestD))
          .Append(",\"legLen\":").Append(F(f.LegLength)).Append(",\"upperLeg\":").Append(F(f.UpperLeg)).Append(",\"hipW\":").Append(F(f.HipW)).Append("},\"poses\":[");
        bool first = true;
        void Dump(string name, string prop, Func<Gesture, Gesture> shape, int ramp = 0)
        {
            Species sp = Fresh();
            sp.Figure.Prop = prop;
            var rig = new HumanRig(sp);
            Gesture g = new() { Feet = Vector3.Zero, Yaw = 0f };
            g = shape(g);
            if (ramp > 0)
            {
                // A blow is read off a rising number: raise it to the value in steps.
                float target = g.Strike;
                for (int k = 1; k <= ramp; k++) { Gesture r = g; r.Strike = target * k / ramp; rig.Drive(r); }
            }
            for (int k = 0; k < 6; k++) rig.Drive(g);
            int body = rig.Layout.Pieces.FindIndex(p => p.Name == "body"), pr = rig.Layout.Pieces.FindIndex(p => p.Name == "prop");
            Transform3D b = rig.Pose[body], inv = b.AffineInverse();
            float chestY = f.Torso * 0.9f;
            float[] C(Vector3 p, int side)
            {
                Vector3 l = inv * p;
                return new[] { l.X * (side == 0 ? -1f : 1f), l.Y - chestY, -l.Z };
            }
            Vector3 by = b.Basis.Y;
            float lean = MathF.Atan2(by.Dot(Vector3.Forward), by.Y);
            if (!first) sb.Append(',');
            first = false;
            sb.Append("{\"name\":\"").Append(name).Append("\",\"prop\":\"").Append(prop).Append("\",\"lean\":").Append(F(lean)).Append(",\"hipY\":").Append(F(b.Origin.Y)).Append(",\"arms\":[");
            for (int i = 0; i < 2; i++)
            {
                var (sh, el, wr) = rig.ArmOf(i);
                if (i > 0) sb.Append(',');
                sb.Append("{\"s\":").Append(A(C(sh, i))).Append(",\"e\":").Append(A(C(el, i))).Append(",\"w\":").Append(A(C(wr, i))).Append('}');
            }
            sb.Append("],\"grip\":[").Append(F(rig.GripLeftOn)).Append(',').Append(F(rig.GripRightOn)).Append("],\"feet\":[");
            for (int i = 0; i < 2; i++) { if (i > 0) sb.Append(','); sb.Append(A(C(rig.FootTarget(i), i))); }
            sb.Append(']');
            if (pr >= 0 && prop != "")
            {
                Transform3D p = rig.Pose[pr];
                Vector3 d = inv.Basis * p.Basis.Y;
                sb.Append(",\"propAt\":").Append(A(C(p.Origin, 1))).Append(",\"propDir\":").Append(A(new[] { d.X, d.Y, -d.Z }));
            }
            sb.Append('}');
        }
        float FidgetClock(int phase, float into) => (3f * 2f + phase + into - off * 3f) / 0.32f;
        Dump("stand", "", g => g);
        Dump("walk-a", "", g => { g.Speed = 1.3f; g.Walked = (1.25f - off) * strideWalk; return g; });
        Dump("run-a", "", g => { g.Speed = 4f; g.Walked = (1.25f - off) * strideRun; return g; });
        Dump("stalk", "", g => { g.Stalk = 1f; g.Speed = 0.6f; g.Walked = (1.25f - off) * strideStalk; return g; });
        Dump("work-low", "", g => { g.Work = 1f; g.Clock = 3f * MathF.PI / 2f / 1.7f; return g; });
        Dump("work-high", "", g => { g.Work = 1f; g.Clock = MathF.PI / 2f / 1.7f; return g; });
        Dump("reach", "", g => { g.Reach = 1f; return g; });
        Dump("sit", "", g => { g.Sit = 1f; return g; });
        Dump("crouch", "", g => { g.Crouch = 1f; return g; });
        Dump("sip", "bottle", g => { g.Sip = 1f; return g; });
        foreach (string p in new[] { "cane", "umbrella", "wrench", "hoe", "bottle" }) Dump("carry-" + p, p, g => g);
        Dump("hoe-work", "hoe", g => { g.Work = 1f; g.Clock = 3f * MathF.PI / 2f / 1.7f; return g; });
        Dump("rifle-low", "rifle", g => g);
        Dump("rifle-aim", "rifle", g => { g.Aim = 1f; return g; });
        Dump("pistol-holster", "pistol", g => g);
        Dump("pistol-low", "pistol", g => { g.Aim = 0.5f; return g; });
        Dump("pistol-raised", "pistol", g => { g.Aim = 1f; return g; });
        Dump("spear-upright", "spear", g => g);
        Dump("spear-level", "spear", g => { g.Aim = 1f; return g; });
        Dump("bow-hang", "bow", g => g);
        Dump("bow-drawn", "bow", g => { g.Aim = 1f; return g; });
        Dump("machete-hang", "machete", g => g);
        Dump("machete-raised", "machete", g => { g.Aim = 1f; return g; });
        Dump("club-hang", "club", g => g);
        Dump("club-raised", "club", g => { g.Aim = 1f; return g; });
        Dump("greet-chest", "", g => { g.Act = BodyAct.Greet; g.ActAmount = 1f; g.Style = 2; return g; });
        Dump("thanks-chest", "", g => { g.Act = BodyAct.Thanks; g.ActAmount = 1f; g.Style = 2; return g; });
        Dump("salute-brow", "", g => { g.Act = BodyAct.Thanks; g.ActAmount = 1f; g.Style = 0; g.Clock = 0f; return g; });
        Dump("salute-out", "", g => { g.Act = BodyAct.Thanks; g.ActAmount = 1f; g.Style = 0; g.Clock = 1f; return g; });
        Dump("clap-open", "", g => { g.Act = BodyAct.Play; g.ActAmount = 1f; g.Clock = 0f; return g; });
        Dump("clap-closed", "", g => { g.Act = BodyAct.Play; g.ActAmount = 1f; g.Clock = MathF.PI / 9f; return g; });
        Dump("panic-clutch", "", g => { g.Act = BodyAct.Panic; g.ActAmount = 1f; g.Clock = 2.625f; return g; });
        Dump("panic-flail", "", g => { g.Act = BodyAct.Panic; g.ActAmount = 1f; g.Clock = 0.625f; return g; });
        Dump("fidget-wring", "", g => { g.Act = BodyAct.Fidget; g.ActAmount = 1f; g.Clock = FidgetClock(0, 0.3f); return g; });
        Dump("fidget-nape", "", g => { g.Act = BodyAct.Fidget; g.ActAmount = 1f; g.Clock = FidgetClock(1, 0.3f); return g; });
        Dump("fidget-hips", "", g => { g.Act = BodyAct.Fidget; g.ActAmount = 1f; g.Clock = FidgetClock(2, 0.3f); return g; });
        Dump("spew-clutch", "", g => { g.Blow = StrikeKind.Spew; g.Strike = 0.6f; return g; }, 6);
        Dump("spew-flung", "", g => { g.Blow = StrikeKind.Spew; g.Strike = 1f; return g; }, 10);
        Dump("punch-cocked", "", g => { g.Blow = StrikeKind.Punch; g.Strike = 0.6f; return g; }, 6);
        Dump("punch-thrown", "", g => { g.Blow = StrikeKind.Punch; g.Strike = 1f; return g; }, 10);
        sb.Append("]}");
        File.WriteAllText(path, sb.ToString());
        GD.Print("pose dump written: " + path);
    }

    static string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);
    static string A(float[] v) => "[" + F(v[0]) + "," + F(v[1]) + "," + F(v[2]) + "]";
}
