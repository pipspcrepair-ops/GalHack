using System;
using Godot;

namespace Wildlife;

/// <summary>
/// What a body is drawn at, eased from what the sim says. The sim steps in ticks (and a far body less often);
/// the drawn root (position and heading) glides from one stepped value to the next, and a correction (a set-back, a
/// landing, a floor change) is eased in over about 0.1 to 0.25 s by its size. A real teleport cuts.
/// </summary>
public sealed class DrawSmoother
{
    /// <summary>Real seconds (tests step it themselves).</summary>
    public static Func<double> Clock = static () => Time.GetTicksMsec() / 1000.0;

    /// <summary>Draw what the sim has, with no easing (measuring what it fixes).</summary>
    public static bool Off;

    /// <summary>A move over this far, or after this long unseen, is a respawn or a level change: it cuts, m and s.</summary>
    public const float CutDistance = 8f, CutAfter = 0.5f;

    /// <summary>Something fading out linearly: x y z the offset (m), w the heading (rad).</summary>
    struct Ease
    {
        Vector4 _from;
        double _end;
        float _dur;

        public Vector4 Now(double t) => _dur <= 0f || t >= _end ? default : _from * (float)((_end - t) / _dur);

        public void Add(Vector4 v, float dur, double t)
        {
            _from = Now(t) + v;
            _end = t + dur;
            _dur = dur;
        }

        public bool Live(double t) => _dur > 0f && t < _end;
        public void Clear() => _dur = 0f;
    }

    Ease _step, _jump;
    Vector3 _at;
    float _yaw;
    /// <summary>When the sim's root last changed, and when it was last noted at all.</summary>
    double _seen, _called;
    bool _has;

    /// <summary>Where it stands in the sim now (last noted).</summary>
    public Vector3 Root => _at;

    /// <summary>Its heading in the sim now (last noted), rad.</summary>
    public float Yaw => _yaw;

    /// <summary>The drawn root differs from the sim's, or is still easing.</summary>
    public bool Easing(double now) => _step.Live(now) || _jump.Live(now);

    public void Cut()
    {
        _has = false;
        _step.Clear();
        _jump.Clear();
    }

    /// <summary>
    /// The sim's root now. A move a body's speed allows (a tick's, or a far body's few) is spread over the time since the last
    /// one; whatever it doesn't allow is a correction, eased in over longer the further it was.
    /// </summary>
    public void Note(Vector3 at, float yaw, float speed, double now)
    {
        double since = now - _called;
        _called = now;
        if (Off || !_has || since > CutAfter || at.DistanceSquaredTo(_at) > CutDistance * CutDistance)
        {
            Cut();
            (_has, _at, _yaw, _seen) = (true, at, yaw, now);
            return;
        }
        if (at == _at && yaw == _yaw) return;
        // (A body that stood still and moves again takes the last step's time, not the whole wait.)
        float dt = Math.Clamp((float)(now - _seen), 1f / 120f, 0.1f);
        Vector3 d = at - _at;
        float len = d.Length(), allow = 0.06f + 1.5f * speed * dt;
        float turn = Geo.WrapAngle(yaw - _yaw), turnAllow = 0.05f + 6f * dt;
        float over = MathF.Max(0f, len - allow), overTurn = MathF.Max(0f, MathF.Abs(turn) - turnAllow);
        Vector3 jump = len > 1e-5f ? d * (over / len) : Vector3.Zero;
        float jumpTurn = MathF.Sign(turn) * overTurn;
        _step.Add(-new Vector4(d.X - jump.X, d.Y - jump.Y, d.Z - jump.Z, turn - jumpTurn), dt, now);
        if (over > 0f || overTurn > 0f)
        {
            // Easing in over longer the bigger the whole of what is still to come.
            Vector4 all = _jump.Now(now) - new Vector4(jump.X, jump.Y, jump.Z, jumpTurn);
            _jump.Add(-new Vector4(jump.X, jump.Y, jump.Z, jumpTurn), Math.Clamp(0.1f + 0.02f * new Vector3(all.X, all.Y, all.Z).Length() + 0.05f * MathF.Abs(all.W), 0.1f, 0.25f), now);
        }
        (_at, _yaw, _seen) = (at, yaw, now);
    }

    /// <summary>Drawn minus sim, offset and heading.</summary>
    public Vector4 Offset(double now) => _step.Now(now) + _jump.Now(now);

    /// <summary>The move that takes a pose made at the sim's root to the drawn root (identity once settled).</summary>
    public Transform3D Placement(double now)
    {
        Vector4 o = Offset(now);
        if (o == default) return Transform3D.Identity;
        var turn = new Basis(Vector3.Up, o.W);
        return new Transform3D(turn, _at + new Vector3(o.X, o.Y, o.Z) - turn * _at);
    }
}

/// <summary>
/// A pose carried from where the last one was drawn to the rig's, over a few tenths of a second: at a change of action or gait,
/// and for a body the rig steps less often, over the span between its updates. Blended about the root, so the body's travel
/// is not part of it.
/// </summary>
public sealed class PoseBlend
{
    /// <summary>How long a switch from one action to another takes, s.</summary>
    public const float Switch = 0.2f, Leap = 0.12f;

    Transform3D[] _from, _out, _last, _prev;
    Transform3D _lastRoot, _prevRoot;
    int _key;
    double _end, _mixed;
    float _dur;
    /// <summary>
    /// Carried across the span between the rig's updates: straight, and every frame, as the root glides over it
    /// (<see cref="DrawSmoother"/>), so a planted foot stays where it is planted; a change of action eases in and out.
    /// </summary>
    bool _has, _span;

    public bool Active(double now) => _dur > 0f && now < _end;

    public void Reset() => _has = false;

    /// <summary>
    /// The pose to draw for the rig's, made at this root. A change of <paramref name="key"/> blends over <see cref="Switch"/>;
    /// <paramref name="span"/> above 0 (the seconds since the rig was last updated) carries it over that, straight. A piece that has leapt over
    /// four fifths of the body's <paramref name="size"/> since the rig's last pose (a rig's own jump from one stride or stance to another) blends too.
    /// </summary>
    public Transform3D[] Frame(Transform3D[] live, Transform3D root, int key, float span, float size, double now)
    {
        int n = live.Length;
        if (_last == null || _last.Length != n)
        {
            _last = new Transform3D[n];
            _from = new Transform3D[n];
            _out = new Transform3D[n];
            _prev = new Transform3D[n];
            _has = false;
        }
        if (DrawSmoother.Off) _has = false;
        float dur = key != _key ? Switch : span;
        _key = key;
        if (_has && Leapt(live, root, size * 0.8f)) dur = MathF.Max(dur, Leap);
        // (A change of action still easing in runs its course: the span after it is in it.)
        if (_has && dur > 0f && (dur != span || _span || !Active(now)))
        {
            Transform3D back = _lastRoot.AffineInverse();
            for (int i = 0; i < n; i++) _from[i] = back * _last[i];
            _span = dur == span;
            _dur = MathF.Min(dur, 0.4f);
            _end = now + _dur;
            _mixed = double.MinValue;
        }
        else if (!_has) _dur = 0f;
        _has = true;
        _prevRoot = root;
        Array.Copy(live, _prev, n);
        return Apply(live, root, now);
    }

    /// <summary>
    /// One piece's pose between two, by a straight blend of each axis kept at its blended length: a fifth of the cost of
    /// a rotation's slerp, and for the few tenths of a second it lasts, as good.
    /// </summary>
    static Transform3D Mix(in Transform3D a, in Transform3D b, float w)
    {
        float v = 1f - w;
        Vector3 a0 = a.Basis.Column0, a1 = a.Basis.Column1, a2 = a.Basis.Column2, b0 = b.Basis.Column0, b1 = b.Basis.Column1, b2 = b.Basis.Column2;
        var basis = new Basis(
            (a0 * v + b0 * w).Normalized() * (a0.Length() * v + b0.Length() * w),
            (a1 * v + b1 * w).Normalized() * (a1.Length() * v + b1.Length() * w),
            (a2 * v + b2 * w).Normalized() * (a2.Length() * v + b2.Length() * w));
        return new Transform3D(basis, a.Origin * v + b.Origin * w);
    }

    /// <summary>Whether a piece of the pose has gone further than this since the last, the root's own travel aside.</summary>
    bool Leapt(Transform3D[] live, Transform3D root, float far)
    {
        Vector3 travel = root.Origin - _prevRoot.Origin;
        float limit = far * far;
        for (int i = 0; i < live.Length; i++)
            if ((live[i].Origin - _prev[i].Origin - travel).LengthSquared() > limit) return true;
        return false;
    }

    /// <summary>The same pose a moment on (a frame between the rig's updates).</summary>
    public Transform3D[] Apply(Transform3D[] live, Transform3D root, double now)
    {
        Transform3D[] drawn = live;
        if (_dur > 0f && now < _end)
        {
            // (A change of action blended 60 times a second at most: a faster screen has nothing more to see in it.)
            if (_span || now - _mixed >= 1.0 / 60.0 - 0.001)
            {
                _mixed = now;
                float w = 1f - (float)((_end - now) / _dur);
                if (!_span) w = w * w * (3f - 2f * w);
                for (int i = 0; i < live.Length; i++) _out[i] = Mix(root * _from[i], live[i], w);
            }
            drawn = _out;
        }
        else _dur = 0f;
        Array.Copy(drawn, _last, drawn.Length);
        _lastRoot = root;
        return drawn;
    }
}
