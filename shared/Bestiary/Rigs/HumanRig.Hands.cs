using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>
/// How a hand is held: where its palm faces (in its arm's own frame: out toward that arm's side, up,
/// ahead) and its shape. The fingers go on from the forearm unless the wrist is bent: then
/// <see cref="Fingers"/> says where they point (same frame) and <see cref="Bend"/> how far (0 to 1).
/// </summary>
public readonly record struct HandSpec(Vector3 Palm, HandShape Shape, Vector3 Fingers = default, float Bend = 0f)
{
    /// <summary>The wrist bent so the fingers point this way (out, up, ahead).</summary>
    public HandSpec Toward(float o, float u, float a) => this with { Fingers = new Vector3(o, u, a).Normalized(), Bend = 1f };

    public static HandSpec Lerp(in HandSpec a, in HandSpec b, float t)
    {
        if (t <= 0f) return a;
        if (t >= 1f) return b;
        Vector3 palm = a.Palm.Lerp(b.Palm, t);
        return new(palm.LengthSquared() > 1e-4f ? palm.Normalized() : b.Palm, t < 0.5f ? a.Shape : b.Shape, a.Fingers.Lerp(b.Fingers, t), Mathf.Lerp(a.Bend, b.Bend, t));
    }
}

/// <summary>
/// Every hand pose in one table, keyed as the Pose Bench keys its keyframes (docs/pose-bench): the way
/// the palm faces and the shape, for each arm (one row for both, or left then right). The code asks
/// for a row by name wherever an act, a style, a blow, a hold or a stance puts a hand there, and
/// blends two rows where the pose moves between them. A row that is missing is a relaxed open hand,
/// palm in.
/// </summary>
public static class HandTable
{
    static HandSpec Open(float o, float u, float a) => new(new Vector3(o, u, a).Normalized(), HandShape.Open);
    static HandSpec Fist(float o, float u, float a) => new(new Vector3(o, u, a).Normalized(), HandShape.Fist);
    static HandSpec Point(float o, float u, float a) => new(new Vector3(o, u, a).Normalized(), HandShape.Point);
    static HandSpec[] Both(HandSpec h) => new[] { h };
    static HandSpec[] Pair(HandSpec left, HandSpec right) => new[] { left, right };

    static readonly HandSpec Rest = Open(-1f, 0f, 0f);

    static readonly Dictionary<string, HandSpec[]> Rows = new()
    {
        // Gestures.
        ["handsup"] = Both(Open(0, 0, 1)), ["cower"] = Both(Open(-0.3f, 0, -1)),
        ["waveoff-in"] = Both(Open(0, -1, 0)), ["waveoff-out"] = Both(Open(0, -1, 0)),
        ["beckon-out"] = Both(Open(0, 1, 0)), ["beckon-in"] = Both(Open(0, 0.4f, -1).Toward(-0.1f, 0.5f, -0.85f)),
        ["point"] = Both(Point(-1, -0.3f, 0)), ["point-up"] = Both(Point(-1, 0, 0.2f)), ["point-down"] = Both(Point(-1, 0, -0.2f)), ["point-side"] = Both(Point(-0.3f, -1, 0)),
        ["shrug"] = Both(Open(0, 1, 0)), ["grieve"] = Both(Open(-0.3f, 0, -1).Toward(-0.2f, 1, 0.1f)), ["taunt"] = Both(Fist(-1, 0, 0)),
        ["greet-wave"] = Both(Open(0, 0, 1)), ["greet-palm"] = Both(Open(0, 0, 1)), ["thanks-offer"] = Both(Open(0, 1, 0)), ["warn"] = Both(Open(0, 0, 1).Toward(0, 1, 0.12f)),
        ["celebrate-low"] = Both(Fist(-1, 0, 0)), ["celebrate-high"] = Both(Fist(-1, 0, 0)), ["present"] = Both(Open(0, 1, 0)), ["startle"] = Both(Open(0, 0, 1)),
        ["panic-flail"] = Both(Open(0, 0, 1)), ["panic-clutch"] = Both(Open(-1, 0, 0).Toward(-0.2f, 1, 0)), ["fidget-wring"] = Both(Open(-1, 0.3f, 0)),
        ["fidget-nape"] = Pair(Open(-1, 0, 0), Open(0, 0, -1).Toward(0.7f, 0.7f, 0)),
        ["punch-cocked"] = Pair(Fist(-1, 0, 0), Fist(0, -1, 0)), ["punch-thrown"] = Pair(Fist(-1, 0, 0), Fist(0, -1, 0)),
        ["spew-clutch"] = Both(Open(0, 0, -1)), ["greet-chest"] = Both(Open(0, 0, -1)), ["thanks-chest"] = Both(Open(0, 0, -1)),
        ["salute-brow"] = Both(Open(0, -1, 0)), ["salute-out"] = Both(Open(0, 0, 1)),
        // Stances.
        ["sit"] = Both(Open(0, -1, 0)), ["crouch"] = Both(Open(0, -1, 0).Toward(0, -0.8f, 0.6f)), ["run-a"] = Both(Fist(-1, 0, 0)), ["reach"] = Both(Open(0, -1, 0)),
        ["work-low"] = Both(Fist(-1, 0, 0)), ["work-high"] = Both(Fist(-1, 0, 0)), ["sip"] = Both(Fist(-1, 0.3f, 0.5f)),
        // Carrying and holding: a fist (a grip decides how it wraps what it holds; the palm here is the side it prefers).
        ["carry-cane"] = Both(Fist(-1, 0, 0)), ["carry-umbrella"] = Both(Fist(-1, 0, 0)), ["carry-wrench"] = Both(Fist(-1, 0, 0)), ["carry-hoe"] = Both(Fist(-1, 0, 0)),
        ["carry-bottle"] = Both(Fist(-1, 0, 0)), ["hoe-work"] = Both(Fist(-1, 0, 0)),
        // (A rifle's left hand cradles the fore-end from under.)
        ["rifle-low"] = Pair(Fist(-0.3f, 1, 0), Fist(-1, 0, 0)), ["rifle-aim"] = Pair(Fist(-0.3f, 1, 0), Fist(-1, 0, 0)),
        ["pistol-holster"] = Both(Fist(-1, 0, 0)), ["pistol-low"] = Both(Fist(-1, 0, 0)),
        ["pistol-raised"] = Both(Fist(-1, 0, 0)), ["spear-upright"] = Both(Fist(-1, 0, 0)), ["spear-level"] = Both(Fist(-1, 0, 0)), ["bow-hang"] = Both(Fist(-1, 0, 0)),
        ["bow-drawn"] = Pair(Fist(-1, 0, 0), Fist(0, 0, -1)), ["blade-hang"] = Both(Fist(-1, 0, 0)), ["blade-raised"] = Both(Fist(-1, 0, 0)),
    };

    /// <summary>A row for an arm (0 left, 1 right).</summary>
    public static HandSpec At(string id, int arm) => Rows.TryGetValue(id, out HandSpec[] row) ? row[Math.Min(arm, row.Length - 1)] : Rest;

    /// <summary>Between two rows (t 0 the first, 1 the second).</summary>
    public static HandSpec Mix(string a, string b, float t, int arm) => HandSpec.Lerp(At(a, arm), At(b, arm), t);
}

public sealed partial class HumanRig
{
    readonly int[] _handP = new int[6];
    readonly float[] _handSize = new float[2];
    readonly Transform3D[] _handAt = new Transform3D[2];
    readonly HandShape[] _handShape = new HandShape[2];
    /// <summary>Each hand shape's corners and middle (nine points) and its box, in its own space: what the body is kept out of.</summary>
    readonly Vector3[][] _corners = new Vector3[6][];
    readonly Vector3[] _lo = new Vector3[6], _hi = new Vector3[6];
    Transform3D _bodyInv;
    /// <summary>The carried thing's row in the hand table (null when nothing is carried, or it is a weapon: a hold names its own).</summary>
    readonly string _carryRow;
    /// <summary>Whether the body holds a bow (its hands go where the arm points).</summary>
    readonly bool _bowman;

    /// <summary>Half the palm's thickness, m: where a hand lies flat on something its wrist is this far off it.</summary>
    float HandHalf(int i) => HandMake.Half * _handSize[i];

    /// <summary>One arm's hand as posed: where it is (origin at the wrist, Y along the fingers, Z out of the palm) and its shape.</summary>
    public (Transform3D at, HandShape shape) HandOf(int i) => (_handAt[i], _handShape[i]);

    /// <summary>How big an arm's hand is (<see cref="HandMake.Size"/>).</summary>
    public float HandSizeOf(int i) => _handSize[i];

    /// <summary>The middle of a hand's fist in the world (what a hand grips runs through it).</summary>
    public Vector3 FistOf(int i) => _handAt[i] * (HandMake.Fist * _handSize[i]);

    /// <summary>The hand's eight corners and its middle in the world (a left hand mirrored), into the caller's array.</summary>
    public void HandCorners(int i, Vector3[] into)
    {
        for (int k = 0; k < 9; k++) into[k] = _handAt[i] * _corners[i * 3 + (int)_handShape[i]][k];
    }

    /// <summary>How far the worst of a hand is sunk into the torso or the head, m (0 if none of it is).</summary>
    public float HandSunk(int i) => HandClear(_handAt[i], _handShape[i], i, 0f).Length();

    /// <summary>A hand's frame from where its palm faces: the fingers on from the forearm (or bent toward where the spec says), the palm squared to them.</summary>
    static Basis HandBasis(in HandSpec s, in Frame F, Vector3 fore)
    {
        Vector3 d = s.Bend > 0f ? fore.Lerp(F.Mix(s.Fingers).Normalized(), s.Bend).Normalized() : fore;
        Vector3 p = F.Mix(s.Palm), n = p - d * p.Dot(d);
        if (n.LengthSquared() < 1e-3f)
        {
            // The palm along the fingers: it faces up, or toward the body.
            n = F.Up - d * F.Up.Dot(d);
            if (n.LengthSquared() < 1e-3f) n = -F.Out - d * (-F.Out).Dot(d);
        }
        n = n.Normalized();
        return new Basis(d.Cross(n), d, n);
    }

    /// <summary>
    /// A hand round something it grips, a pole along <paramref name="axis"/>: the knuckles run along it and the
    /// palm faces as near the way <paramref name="palm"/> says as that allows, the fingers going the forearm's way
    /// (the wrist bends to make up the difference). A bow's hands go the forearm's way first, the palm second:
    /// the archer's arm points where the torso does not; the string hand has no such lead, only its palm to the face.
    /// </summary>
    Basis HandGrip(int arm, Vector3 axis, Vector3 fore, Vector3 across, Vector3 otherwise, Vector3 palm)
    {
        Vector3 facing = palm - axis * palm.Dot(axis);
        if (!(_bowman && arm == 0) && facing.LengthSquared() > 0.25f)
        {
            Vector3 face = facing.Normalized(), knuckles = axis, fingers = face.Cross(knuckles);
            if (fingers.Dot(fore) < 0f) (knuckles, fingers) = (-knuckles, -fingers);
            return new Basis(knuckles, fingers, face);
        }
        Vector3 hint = across - axis * across.Dot(axis);
        if (hint.LengthSquared() < 0.1f) hint = otherwise - axis * otherwise.Dot(axis); // the axis runs the way they would go
        Vector3 along = fore - axis * fore.Dot(axis);
        float w = MathF.Min(1f, along.Length() * 1.5f);
        Vector3 d = hint.Normalized() * (1f - w) + (w > 0f ? along.Normalized() * w : Vector3.Zero);
        d = d.LengthSquared() > 1e-4f ? d.Normalized() : hint.Normalized();
        Vector3 x = axis, n = x.Cross(d);
        if (n.Dot(palm) < 0f)
        {
            x = -x;
            n = -n;
        }
        return new Basis(x, d, n);
    }

    /// <summary>The frame a hand takes: from its spec on the forearm, closed round what it grips as far as it grips.</summary>
    Basis HandFrame(int arm, in HandSpec spec, in Frame F, Vector3 axis, float gripW, Vector3 fore, Vector3 gripFore)
    {
        Basis open = HandBasis(spec, F, fore);
        return gripW > 0f ? Between(open, HandGrip(arm, axis.Normalized(), gripFore, F.Ahead, -F.Out, F.Mix(spec.Palm)), Math.Min(1f, gripW)) : open;
    }

    /// <summary>Between two frames, turned the short way (t 0 the first).</summary>
    static Basis Between(Basis a, Basis b, float t) => new(a.GetRotationQuaternion().Slerp(b.GetRotationQuaternion(), t));

    /// <summary>
    /// A weapon held out of the way of the fists on it: where they would be in the torso or the head (a thick
    /// coat, a drawn string hand), the whole weapon and both hands go the way the worst of them is pushed.
    /// </summary>
    void KeepFistsClear(ref Hold h, HoldKind kind, Basis torso)
    {
        for (int pass = 0; pass < 3; pass++)
        {
            Vector3 shift = Vector3.Zero, pulled = Vector3.Zero;
            for (int i = 0; i < 2; i++)
            {
                if ((i == 1 ? h.RW : h.LW) < 0.05f) continue;
                var F = new Frame(torso, i == 0 ? -1f : 1f);
                Vector3 grip = i == 1 ? h.R : h.L, shoulder = Pose[_body].Origin + torso.Y * _f.Torso * 0.9f + F.Out * _f.ShoulderW * 0.94f;
                Vector3 push = Vector3.Zero;
                for (int flip = 1; flip >= (kind == HoldKind.Bow ? 1 : -1); flip -= 2) // (the fingers go on from the forearm, which the arm settles later: clear of the fist whichever way round it ends; the string hand's palm is the whole of its frame)
                {
                    Basis frame = HandGrip(i, (i == 1 ? h.RAxis : h.LAxis).Normalized(), (grip - shoulder).Normalized() * flip, F.Ahead, -F.Out, F.Mix((i == 1 ? h.RHand : h.LHand).Palm));
                    Vector3 p = HandClear(new Transform3D(frame, grip - frame * (HandMake.Fist * _handSize[i])), HandShape.Fist, i, 0.03f);
                    if (p.LengthSquared() > push.LengthSquared()) push = p;
                }
                if (kind == HoldKind.Bow && i == 1) pulled = push; // (the string hand is not on the bow: it alone goes)
                else if (push.LengthSquared() > shift.LengthSquared()) shift = push;
            }
            if (shift.LengthSquared() < 1e-6f && pulled.LengthSquared() < 1e-6f) return;
            h.R += shift + pulled;
            h.L += shift;
            h.Prop.Origin += shift;
        }
    }

    /// <summary>
    /// The push that frees a hand from the torso and the head: the way and the distance the worst sunk
    /// point of it must go to stand <paramref name="margin"/> clear. Zero when none of it is in them.
    /// </summary>
    Vector3 HandClear(in Transform3D hand, HandShape shape, int i, float margin)
    {
        int k = i * 3 + (int)shape;
        Vector3 worst = Vector3.Zero;
        float most = 0f;
        if (Shell != null)
        {
            Transform3D body = Pose[_body];
            // The box's corners and middle against the torso.
            for (int c = 0; c < 9; c++)
            {
                Vector3 p = hand * _corners[k][c], l = _bodyInv * p, d = body.Basis * (Shell.PushOut(l, margin) - l);
                if (d.LengthSquared() > most * most) (worst, most) = (d, d.Length());
            }
        }
        // The head, a ball: the nearest point of the box to its middle.
        float r = _f.HeadRadius + margin;
        Vector3 local = hand.Basis.Transposed() * (HeadPoint - hand.Origin), near = new(Math.Clamp(local.X, _lo[k].X, _hi[k].X), Math.Clamp(local.Y, _lo[k].Y, _hi[k].Y), Math.Clamp(local.Z, _lo[k].Z, _hi[k].Z));
        float len = (local - near).Length();
        if (len < r && r - len > most)
        {
            most = r - len;
            worst = hand.Basis * (len > 1e-4f ? (near - local) / len : (near - (_lo[k] + _hi[k]) * 0.5f).Normalized()) * most;
        }
        return worst;
    }

    /// <summary>Shows the hand in the shape asked for, framed at the wrist; the other two shapes are folded away.</summary>
    void PoseHand(int i, in Basis frame, Vector3 wrist, HandShape shape)
    {
        _handAt[i] = new Transform3D(frame, wrist);
        _handShape[i] = shape;
        for (int s = 0; s < 3; s++)
            if (_handP[i * 3 + s] is int p and >= 0) Pose[p] = s == (int)shape ? _handAt[i] : Collapsed(wrist);
    }

    /// <summary>
    /// A hand's shape and palm at rest in this stance: what it carries, how it walks, works, reaches, sits,
    /// crouches or drinks (each row of the table eased in by how far the body is into it).
    /// </summary>
    HandSpec StanceHand(int i, in Gesture g, HoldKind kind, in Hold hold, float moving, float run, float stalk, float sip)
    {
        bool weapon = kind != HoldKind.None && (i == 1) != (kind == HoldKind.Bow) && (i == 1 ? hold.RW : hold.LW) > 0f; // the hand that keeps the weapon when it hangs (a holstered one is a free hand)
        bool carries = i == 1 && _carryRow != null;
        HandSpec h = weapon ? (i == 1 ? hold.RHand : hold.LHand) : carries ? HandTable.At(_carryRow, i) : HandTable.At("stand", i);
        if (!weapon && !carries) h = HandSpec.Lerp(h, HandTable.At(stalk > 0.5f ? "stalk" : run > 0.5f ? "run-a" : "walk-a", i), moving);
        if (g.Work > 0f) h = HandSpec.Lerp(h, carries && _f.Prop == "hoe" ? HandTable.At("hoe-work", i) : HandTable.Mix("work-low", "work-high", 0.5f + 0.5f * MathF.Sin(g.Clock * 1.7f), i), g.Work);
        h = HandSpec.Lerp(h, HandTable.At("reach", i), g.Reach);
        if (kind != HoldKind.LongGun) h = HandSpec.Lerp(h, HandTable.At("sit", i), g.Sit);
        h = HandSpec.Lerp(h, HandTable.At("crouch", i), g.Crouch);
        return i == 1 ? HandSpec.Lerp(h, HandTable.At("sip", i), sip) : h;
    }

    /// <summary>A dead hand lies flat on whatever the arm lies on, palm up, the fingers level or rising.</summary>
    void LayHand(int i, Vector3 elbow, Vector3 wrist)
    {
        Vector3 d = (wrist - elbow).Normalized();
        d.Y = MathF.Max(d.Y, 0f);
        d = d.LengthSquared() > 1e-4f ? d.Normalized() : Vector3.Forward;
        Vector3 n = Vector3.Up - d * d.Y;
        n = n.LengthSquared() > 1e-3f ? n.Normalized() : Vector3.Right;
        PoseHand(i, new Basis(d.Cross(n), d, n), wrist, HandShape.Open);
    }
}
