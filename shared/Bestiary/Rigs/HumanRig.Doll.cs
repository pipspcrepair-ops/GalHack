using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

public sealed partial class HumanRig
{
    /// <summary>
    /// A body gone limp: a point for each joint, each with the thickness of the body there, held
    /// together by its bones (the trunk held rigid, the neck a little give, the elbows and knees
    /// never folding flat), falling under gravity onto the ground under each point and sliding to
    /// rest. Once still it is still for good. What was in the hands drops with it as a rod of its own.
    /// </summary>
    sealed class Doll
    {
        public const int Pelvis = 0, Chest = 1, Neck = 2, Crown = 3, Shoulder = 4, Elbow = 6, Wrist = 8, Hip = 10, Knee = 12, Ankle = 14, Toe = 16, Count = 18;
        const float H = 1f / 120f, Gravity = 9.8f;
        readonly Vector3[] _p = new Vector3[Count + 2], _old = new Vector3[Count + 2];
        readonly float[] _r = new float[Count + 2], _ground = new float[Count + 2];
        readonly List<(int a, int b, float min, float max)> _links = new();
        readonly bool _prop;
        readonly float _propFrom, _propTo;
        readonly Transform3D _bodyAt;
        readonly Transform3D[] _tailAt;
        float _calm, _age, _left;
        public bool Settled;

        public Doll(HumanRig rig, Vector3[] joints, Vector3[] before, float dt)
        {
            Figure f = rig._f;
            float pad = f.Padding, d = f.ChestD * pad;
            for (int i = 0; i < Count; i++)
            {
                _p[i] = joints[i];
                Vector3 v = (joints[i] - before[i]) / MathF.Max(dt, 1e-3f);
                v = v.LimitLength(3f);
                _old[i] = joints[i] - v * H;
            }
            _r[Pelvis] = d;
            _r[Chest] = d;
            _r[Neck] = 0.05f;
            _r[Crown] = f.HeadRadius;
            for (int i = 0; i < 2; i++)
            {
                _r[Shoulder + i] = MathF.Max(0.05f, f.ShoulderW * (pad - 0.94f) + 0.05f);
                _r[Elbow + i] = 0.045f;
                _r[Wrist + i] = 0.04f;
                _r[Hip + i] = MathF.Max(0.06f, f.HipW * (pad - 0.55f) + 0.02f);
                _r[Knee + i] = 0.055f;
                _r[Ankle + i] = MathF.Max(0.04f, f.Ankle);
                _r[Toe + i] = 0.03f;
            }
            // The trunk: every pair of its points held, so it moves as one.
            int[] trunk = { Pelvis, Chest, Neck, Shoulder, Shoulder + 1, Hip, Hip + 1 };
            for (int a = 0; a < trunk.Length; a++)
                for (int b = a + 1; b < trunk.Length; b++) Rigid(trunk[a], trunk[b]);
            Rigid(Neck, Crown);
            float nod = _p[Chest].DistanceTo(_p[Crown]);
            _links.Add((Chest, Crown, nod * 0.86f, nod * 1.03f));
            for (int i = 0; i < 2; i++)
            {
                Rigid(Shoulder + i, Elbow + i);
                Rigid(Elbow + i, Wrist + i);
                float arm = f.UpperArmOf(i) + f.ForearmOf(i);
                _links.Add((Shoulder + i, Wrist + i, arm * 0.42f, 9f));
                Rigid(Hip + i, Knee + i);
                Rigid(Knee + i, Ankle + i);
                _links.Add((Hip + i, Ankle + i, f.LegLength * 0.5f, 9f));
                Rigid(Ankle + i, Toe + i);
                Rigid(Knee + i, Toe + i);
                // Hands and knees stay outside the trunk.
                foreach (int j in new[] { Elbow + i, Wrist + i })
                {
                    _links.Add((Chest, j, _r[Chest] + _r[j], 9f));
                    _links.Add((Pelvis, j, _r[Pelvis] + _r[j], 9f));
                }
                _links.Add((Knee + i, Knee + 1 - i, 0.1f, 9f));
            }
            // What was in the hands falls free, as a rod.
            if (rig._prop >= 0)
            {
                Transform3D t = rig.Pose[rig._prop];
                Vector3 y = t.Basis.Y.Normalized();
                (_propFrom, _propTo) = Extent(rig.Layout.Pieces[rig._prop]);
                _prop = _propTo - _propFrom > 0.05f;
                _p[Count] = t.Origin + y * _propFrom;
                _p[Count + 1] = t.Origin + y * _propTo;
                for (int k = Count; k < Count + 2; k++)
                {
                    _old[k] = _p[k] - (_p[Wrist + 1] - _old[Wrist + 1]);
                    _r[k] = 0.025f;
                }
                if (_prop) Rigid(Count, Count + 1);
            }
            _bodyAt = rig.Pose[rig._body];
            _tailAt = new Transform3D[rig._tail.Length];
            for (int i = 0; i < _tailAt.Length; i++) _tailAt[i] = rig._tail[i] >= 0 ? rig.Pose[rig._tail[i]] : Transform3D.Identity;
        }

        /// <summary>A held thing's length along its +Y, from its own shapes.</summary>
        static (float, float) Extent(Piece p)
        {
            float lo = 0f, hi = 0f;
            foreach (Prim q in p.Prims)
            {
                Vector3 c = q.Local.Origin;
                float reach = q.Shape == Shape.Taper ? q.Size.Y : q.Size.Y * 0.5f;
                Vector3 along = q.Local.Basis.Y.Normalized();
                float a = c.Y + along.Y * (q.Shape == Shape.Taper ? 0f : -reach), b = c.Y + along.Y * reach;
                lo = MathF.Min(lo, MathF.Min(a, b));
                hi = MathF.Max(hi, MathF.Max(a, b));
            }
            return (lo, hi);
        }

        void Rigid(int a, int b)
        {
            float l = _p[a].DistanceTo(_p[b]);
            _links.Add((a, b, l, l));
        }

        int N => _prop ? Count + 2 : Count;

        /// <summary>A blow to one part (and a share of it to the trunk), as velocity.</summary>
        public void Shove(Part part, Vector3 dir, float heavy)
        {
            Vector3 hard = dir * (1.6f + 3.4f * heavy) * H, soft = dir * (0.7f + 1.6f * heavy) * H;
            int[] struck = part switch
            {
                Part.Head => new[] { Crown, Neck },
                Part.ArmL => new[] { Elbow, Wrist },
                Part.ArmR => new[] { Elbow + 1, Wrist + 1 },
                Part.LegL => new[] { Knee, Ankle },
                Part.LegR => new[] { Knee + 1, Ankle + 1 },
                _ => new[] { Chest, Neck },
            };
            for (int i = 0; i < Count; i++) _old[i] -= soft;
            foreach (int i in struck) _old[i] -= hard;
            Settled = false;
            _calm = 0f;
        }

        /// <summary>No hit to go by: the top of the body tips over one way and the knees go.</summary>
        public void Topple(Vector3 dir)
        {
            foreach (int i in new[] { Chest, Neck, Crown, Shoulder, Shoulder + 1 }) _old[i] -= dir * 1.1f * H;
            for (int i = 0; i < 2; i++) _old[Knee + i] -= -dir * 0.6f * H;
        }

        public void Step(HumanRig rig, float dt, Func<Vector3, float> ground)
        {
            if (Settled) return;
            _left += dt;
            int steps = 0;
            while (_left >= H && steps < 8)
            {
                _left -= H;
                steps++;
                _age += H;
                for (int i = 0; i < N; i++) _ground[i] = ground(_p[i]);
                float most = 0f;
                for (int i = 0; i < N; i++)
                {
                    Vector3 v = (_p[i] - _old[i]) * 0.998f;
                    _old[i] = _p[i];
                    _p[i] += v + Vector3.Down * Gravity * H * H;
                }
                for (int it = 0; it < 8; it++)
                {
                    foreach (var (a, b, min, max) in _links)
                    {
                        Vector3 d = _p[b] - _p[a];
                        float len = d.Length();
                        if (len < 1e-6f) continue;
                        float want = Math.Clamp(len, min, max);
                        if (want == len) continue;
                        Vector3 fix = d * ((len - want) / len * 0.5f);
                        _p[a] += fix;
                        _p[b] -= fix;
                    }
                    for (int i = 0; i < N; i++)
                    {
                        float floor = _ground[i] + _r[i];
                        if (_p[i].Y >= floor) continue;
                        _p[i].Y = floor;
                        // On the ground it drags: most of its slide goes, and it doesn't bounce.
                        _old[i].X = Mathf.Lerp(_old[i].X, _p[i].X, 0.45f);
                        _old[i].Z = Mathf.Lerp(_old[i].Z, _p[i].Z, 0.45f);
                        if (_old[i].Y < _p[i].Y) _old[i].Y = _p[i].Y;
                    }
                }
                for (int i = 0; i < N; i++) most = MathF.Max(most, (_p[i] - _old[i]).Length());
                _calm = most < 0.0009f ? _calm + H : 0f;
                if (_calm > 0.4f && _age > 0.6f || _age > 8f)
                {
                    Settled = true;
                    // At rest: nothing is left a hair under the ground.
                    for (int i = 0; i < N; i++)
                    {
                        float floor = ground(_p[i]) + _r[i];
                        if (_p[i].Y < floor) _p[i].Y = floor;
                    }
                    break;
                }
            }
            if (steps == 8) _left = 0f;
        }

        public void Pose(HumanRig rig)
        {
            Transform3D[] pose = rig.Pose;
            Vector3 up = (_p[Neck] - _p[Pelvis]).Normalized();
            Vector3 x = _p[Shoulder + 1] - _p[Shoulder];
            x = (x - up * x.Dot(up)).Normalized();
            var torso = new Basis(x, up, x.Cross(up));
            pose[rig._body] = new Transform3D(torso, _p[Pelvis]);
            Vector3 hy = (_p[Crown] - _p[Neck]).Normalized();
            Vector3 hx = (x - hy * x.Dot(hy)).Normalized();
            pose[rig._head] = new Transform3D(new Basis(hx, hy, hx.Cross(hy)), _p[Neck]);
            for (int i = 0; i < 2; i++)
            {
                pose[rig._armU[i]] = Geo.Segment(_p[Shoulder + i], _p[Elbow + i], x);
                pose[rig._armL[i]] = Geo.Segment(_p[Elbow + i], _p[Wrist + i], x);
                rig._wrist[i] = _p[Wrist + i];
                rig.LayHand(i, _p[Elbow + i], _p[Wrist + i]);
                pose[rig._legU[i]] = Geo.Segment(_p[Hip + i], _p[Knee + i], x);
                pose[rig._legL[i]] = Geo.Segment(_p[Knee + i], _p[Ankle + i], x);
                Vector3 toe = _p[Toe + i] - _p[Ankle + i];
                Vector3 z = -toe.Normalized();
                Vector3 y = _p[Knee + i] - _p[Ankle + i];
                y = (y - z * y.Dot(z)).Normalized();
                pose[rig._foot[i]] = new Transform3D(new Basis(y.Cross(z), y, z), _p[Ankle + i]);
                rig._knee[i] = _p[Knee + i];
                rig._drawn[i] = _p[Ankle + i];
            }
            Transform3D moved = pose[rig._body] * _bodyAt.AffineInverse();
            for (int i = 0; i < _tailAt.Length; i++)
                if (rig._tail[i] >= 0) pose[rig._tail[i]] = moved * _tailAt[i];
            if (rig._prop >= 0)
            {
                if (_prop)
                {
                    Vector3 y = (_p[Count + 1] - _p[Count]).Normalized();
                    Vector3 side = pose[rig._prop].Basis.X;
                    pose[rig._prop] = new Transform3D(Geo.AlongY(y, side), _p[Count] - y * _propFrom);
                }
                else pose[rig._prop] = new Transform3D(pose[rig._prop].Basis, _p[Wrist + 1]);
                if (rig._string0 >= 0 && rig._string1 >= 0)
                {
                    Transform3D bow = pose[rig._prop];
                    Vector3 nock = bow * new Vector3(0f, 0f, HumanBody.BowTip(rig._f, 1).Z);
                    pose[rig._string0] = Geo.Segment(bow * HumanBody.BowTip(rig._f, 1), nock, x, 1f);
                    pose[rig._string1] = Geo.Segment(bow * HumanBody.BowTip(rig._f, -1), nock, x, 1f);
                }
            }
        }

        /// <summary>The lowest any point of the body sits against the ground under it, m (below 0: sunk in), for the tests.</summary>
        public float WorstSink(Func<Vector3, float> ground)
        {
            float worst = float.MaxValue;
            for (int i = 0; i < N; i++) worst = MathF.Min(worst, _p[i].Y - _r[i] - ground(_p[i]));
            return worst;
        }
    }

    /// <summary>How the dead body lies against the ground: the least gap under any of its points (below 0 sunk in), m; 0 if it isn't a ragdoll.</summary>
    public float DeadGap(Func<Vector3, float> ground) => _doll?.WorstSink(ground) ?? 0f;
}
