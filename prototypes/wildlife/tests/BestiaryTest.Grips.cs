using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
namespace Wildlife;

/// <summary>The owner (9 Nov 2026): people don't hold weapons properly, and a low-tech world's people carried modern guns.</summary>
public partial class BestiaryTest
{
    static readonly string[] Arms = { "rifle", "shotgun", "musket", "crossbow", "laser_rifle", "pistol", "energy_pistol", "spear", "bow", "club", "machete" };

    /// <summary>
    /// Fresh people of many folk, sizes and dress, each given every kind of weapon in turn: in every
    /// pose (calm, half up, aiming; standing and walking; looking ahead and aside; a gesture with
    /// the free hand) each hand on the weapon is within 3 cm of its grip, a long gun has the other
    /// hand on its fore-end, nothing of it passes through the torso or the head, and it points
    /// where the pose says.
    /// </summary>
    void WeaponsAreHeldRight()
    {
        float worstGrip = 0f;
        string worstGripWhat = "", kneelWhat = "", clipWhat = "", muzzleWhat = "", foreWhat = "";
        int kneels = 0, clips = 0, muzzles = 0, fores = 0, people = 0, poses = 0;
        var folks = new HashSet<string>();
        var tally = new Dictionary<string, int>();
        void Tally(string k) => tally[k] = tally.GetValueOrDefault(k) + 1;
        var roles = Enum.GetValues<PersonRole>();
        for (int w = 0; w < 8; w++)
        {
            PeopleGenerator g = Folks(World(_fresh.Range(-25f, 40f), alien: _fresh.Range(0f, 1f)));
            for (int n = 0; n < 22; n++)
            {
                Species s = g.Person(Seed(), roles[_fresh.Int(0, roles.Length - 1)], founders: g.FoundersOf(Seed()));
                Figure f = s.Figure;
                if (s.Mechanical || f.Husk) continue;
                f.Prop = Arms[(w * 22 + n) % Arms.Length];
                folks.Add(f.Folk?.Id ?? "human");
                var rig = new HumanRig(s);
                people++;
                HoldKind kind = Weapons.HoldOf(f.Prop);
                int prop = rig.Layout.Pieces.FindIndex(p => p.Name == "prop");
                int body = rig.Layout.Pieces.FindIndex(p => p.Name == "body");
                int head = rig.Layout.Pieces.FindIndex(p => p.Name == "head");
                float yaw = _fresh.Range(-3f, 3f), u = HumanBody.ArmScale(f);
                foreach (float aim in new[] { 0f, 0.5f, 1f })
                foreach (float look in new[] { 0f, 0.45f })
                foreach (int mode in new[] { 0, 1, 2, 3 }) // standing, walking, a gesture, crouched
                {
                    if (mode == 2 && aim > 0f) continue;
                    var gs = new Gesture { Feet = Vector3.Zero, Yaw = yaw, Aim = aim, Look = look };
                    for (int k = 0; k < 4; k++)
                    {
                        gs.Speed = mode == 1 ? 1.3f : 0f;
                        gs.Walked = mode == 1 ? k * 0.37f : 0f;
                        gs.Clock = k * 0.41f;
                        gs.Crouch = mode == 3 ? 1f : 0f;
                        if (mode == 2) { gs.Act = k % 2 == 0 ? BodyAct.Warn : BodyAct.HandsUp; gs.ActAmount = 1f; }
                        rig.Drive(gs);
                        poses++;
                        string key = $"{f.Prop}/{aim}/{(mode == 0 ? "stand" : mode == 1 ? "walk" : mode == 3 ? "crouch" : gs.Act.ToString())}";
                        string what = $"{f.Prop} on a {f.Folk?.Id ?? "human"} {f.Height:F2} m ({f.Dress}), aim {aim}, {(mode == 0 ? "standing" : mode == 1 ? "walking" : mode == 3 ? "crouched" : gs.Act.ToString())}";
                        // Hands on their grips.
                        if (rig.GripRightOn >= 0.999f)
                        {
                            float miss = rig.WristOf(1).DistanceTo(rig.GripRight);
                            if (miss > 0.03f) Tally("gripR " + key + $" reach {rig.Pose[rig.Layout.Pieces.FindIndex(p => p.Name == "arm1u")].Origin.DistanceTo(rig.GripRight) / (f.UpperArmOf(1) + f.ForearmOf(1)):F2}");
                            if (miss > worstGrip) { worstGrip = miss; worstGripWhat = what + " (right)"; }
                        }
                        if (rig.GripLeftOn >= 0.999f)
                        {
                            float miss = rig.WristOf(0).DistanceTo(rig.GripLeft);
                            if (miss > 0.03f) Tally("gripL " + key + $" reach {rig.Pose[rig.Layout.Pieces.FindIndex(p => p.Name == "arm0u")].Origin.DistanceTo(rig.GripLeft) / (f.UpperArmOf(0) + f.ForearmOf(0)):F1} off {(rig.Pose[body].Basis.Inverse() * (rig.GripLeft - rig.Pose[rig.Layout.Pieces.FindIndex(p => p.Name == "arm0u")].Origin)).Snapped(Vector3.One * 0.05f)} arm {f.UpperArmOf(0) + f.ForearmOf(0):F2} d {f.ChestD * f.Padding:F2}");
                            if (miss > worstGrip) { worstGrip = miss; worstGripWhat = what + " (left)"; }
                        }
                        Transform3D pp = rig.Pose[prop];
                        // Crouched, a hand off the weapon rests by its knee.
                        if (mode == 3)
                            for (int side = 0; side < 2; side++)
                            {
                                if ((side == 1 ? rig.GripRightOn : rig.GripLeftOn) > 0f) continue;
                                Vector3 knee = rig.Pose[rig.Layout.Pieces.FindIndex(p => p.Name == $"leg{side}l")].Origin;
                                float off = rig.WristOf(side).DistanceTo(knee);
                                if (off > 0.3f * f.Height / 1.72f) { kneels++; kneelWhat = what + $" ({off:F2} m from the knee)"; Tally("knee " + key); }
                            }
                        // A long gun's second hand is on its fore-end whenever it isn't busy.
                        if (kind == HoldKind.LongGun && mode != 2)
                        {
                            float along = (pp.AffineInverse() * rig.GripLeft).Y / u;
                            if (rig.GripLeftOn < 0.999f || along < 0.14f || along > 0.36f) { fores++; foreWhat = what + $" (left hand {along:F2} along)"; }
                        }
                        // Nothing through the torso or the head.
                        Transform3D torso = rig.Pose[body];
                        Transform3D headT = rig.Pose[head];
                        Vector3 headAt = headT.Origin + headT.Basis.Y.Normalized() * (f.Neck + f.HeadRadius);
                        foreach (Vector3 at in Samples(rig.Layout.Pieces[prop]))
                        {
                            Vector3 p = pp * at;
                            if (InTorso(f, torso, p) || p.DistanceTo(headAt) < f.HeadRadius * 0.9f)
                            {
                                Tally((InTorso(f, torso, p) ? "torso " : "head ") + key + $" y {(torso.AffineInverse() * p).Y / f.Torso:F1}");
                                clips++;
                                clipWhat = what;
                                break;
                            }
                        }
                        // It points where the pose says.
                        Vector3 fwd = new Basis(Vector3.Up, yaw) * Vector3.Forward;
                        Vector3 aimAt = new Basis(Vector3.Up, yaw + look) * Vector3.Forward;
                        Vector3 dir = (kind == HoldKind.Bow ? pp.Basis.Z : pp.Basis.Y).Normalized();
                        bool ok = dir.Dot(-fwd) < 0.3f || kind == HoldKind.Bow && aim < 1f; // nothing points back
                        if (mode != 2 && aim == 0f)
                            ok &= kind switch
                            {
                                HoldKind.LongGun => dir.Dot(Vector3.Down) > 0.35f && dir.Dot(fwd) > 0.35f,
                                HoldKind.Pistol => dir.Dot(Vector3.Down) > 0.85f,
                                HoldKind.Spear => dir.Dot(Vector3.Up) > 0.9f,
                                HoldKind.Blade => dir.Dot(Vector3.Down) > 0.5f,
                                _ => true,
                            };
                        if (aim == 1f)
                            ok &= kind switch
                            {
                                HoldKind.LongGun or HoldKind.Pistol => dir.Dot(aimAt) > 0.95f,
                                HoldKind.Spear or HoldKind.Bow => dir.Dot(aimAt) > 0.9f,
                                HoldKind.Blade => dir.Dot(Vector3.Up) > 0.8f,
                                _ => true,
                            };
                        if (!ok) { muzzles++; muzzleWhat = what; Tally("dir " + key + $" {dir.Dot(Vector3.Down):F1}down {dir.Dot(fwd):F1}fwd"); }
                    }
                }
            }
        }
        foreach (var (k, v) in tally.GroupBy(t => t.Key.Split(' ')[0]).SelectMany(gr => gr.OrderByDescending(t => t.Value).Take(8))) GD.Print($"        {v,4}  {k}");
        Check(people > 120 && folks.Count >= 2, $"{people} fresh people of {folks.Count} folk, each with a weapon, in {poses} poses");
        Check(worstGrip < 0.03f, $"every hand on a weapon is within 3 cm of its grip ({worstGrip * 100f:F1} cm at worst{(worstGrip >= 0.03f ? ": " + worstGripWhat : "")})");
        Check(fores == 0, $"a long gun has the second hand on its fore-end ({fores} missed{(fores > 0 ? ": " + foreWhat : "")})");
        Check(kneels == 0, $"crouched, a free hand rests by its knee ({kneels} didn't{(kneels > 0 ? ": " + kneelWhat : "")})");
        Check(clips == 0, $"no weapon passes through a torso or a head ({clips} did{(clips > 0 ? ": " + clipWhat : "")})");
        Check(muzzles == 0, $"every weapon points where its pose says: low and ahead calm, along the look aimed, never back ({muzzles} wrong{(muzzles > 0 ? ": " + muzzleWhat : "")})");
    }

    /// <summary>A prop's parts as points in prop space: a grid through each part's box.</summary>
    static IEnumerable<Vector3> Samples(Piece piece)
    {
        foreach (Prim prim in piece.Prims)
        {
            Vector3 sz = prim.Size;
            float w = prim.Shape == Shape.Taper ? MathF.Max(sz.X, sz.Z) : sz.X, dz = prim.Shape is Shape.Taper or Shape.Cone ? MathF.Max(sz.X, sz.Z) : sz.Z;
            float y0 = prim.Shape == Shape.Taper ? 0f : -sz.Y * 0.5f, y1 = prim.Shape == Shape.Taper ? sz.Y : sz.Y * 0.5f;
            for (int a = 0; a < 3; a++)
            for (int b = 0; b < 6; b++)
            for (int c = 0; c < 3; c++)
                yield return prim.Local * new Vector3((a - 1) * w * 0.45f, Mathf.Lerp(y0, y1, b / 5f), (c - 1) * dz * 0.45f);
        }
    }

    /// <summary>Inside the torso: an elliptic column a little inside the drawn body, hips to shoulders.</summary>
    static bool InTorso(Figure f, Transform3D torso, Vector3 world)
    {
        Vector3 p = torso.AffineInverse() * world;
        float T = f.Torso;
        if (p.Y < 0.05f * T || p.Y > 0.95f * T) return false;
        float t = p.Y / T;
        float half = (t < 0.45f ? MathF.Max(f.HipW, f.WaistW) : Mathf.Lerp(f.WaistW, f.ShoulderW * 0.9f, Math.Clamp((t - 0.45f) / 0.35f, 0f, 1f))) * f.Padding * 0.9f;
        float depth = f.ChestD * (1f + 0.4f * (f.Padding - 1f)) * 0.95f; // a thick coat gives under a stock
        return p.X * p.X / (half * half) + p.Z * p.Z / (depth * depth) < 1f;
    }

    /// <summary>What people carry follows their world's era: no modern gun on a low world, energy weapons on a high one.</summary>
    void ArmsFollowTheEra()
    {
        var count = new Dictionary<string, (int armed, int modern, int energy, int old)>();
        var roles = Enum.GetValues<PersonRole>();
        foreach (string era in new[] { "low", "mid", "high" })
        {
            var c = (armed: 0, modern: 0, energy: 0, old: 0);
            for (int w = 0; w < 6; w++)
            {
                WorldConditions world = World(_fresh.Range(-20f, 35f), alien: _fresh.Range(0f, 0.8f));
                world.Now = era == "low" ? 0 : era == "mid" ? 1 : 2;
                world.Before = era == "low" ? 0 : _fresh.Int(0, 2);
                PeopleGenerator g = Bestiary.Roll(_data, world).People;
                for (int n = 0; n < 60; n++)
                {
                    string prop = g.Person(Seed(), roles[n % roles.Length], founders: g.FoundersOf(Seed())).Figure.Prop;
                    if (!Weapons.Armed(prop)) continue;
                    c.armed++;
                    if (Weapons.Modern(prop)) c.modern++;
                    if (Weapons.Energy(prop)) c.energy++;
                    if (prop is "spear" or "bow" or "crossbow" or "musket" or "machete") c.old++;
                }
            }
            count[era] = c;
        }
        Check(count["low"].armed > 20 && count["low"].modern == 0 && count["low"].old > 0, $"on low-tech worlds nobody carries a modern gun ({count["low"].armed} armed, {count["low"].modern} modern, {count["low"].old} spears, bows and the like)");
        Check(count["mid"].modern > 20 && count["mid"].energy * 10 < count["mid"].armed, $"mid-tech worlds carry rifles, shotguns and pistols ({count["mid"].modern} of {count["mid"].armed}, {count["mid"].energy} energy relics)");
        Check(count["high"].energy > 5, $"high-tech worlds have energy weapons ({count["high"].energy} of {count["high"].armed} armed)");
    }
}
