using System;
using System.Collections.Generic;
using Godot;
using Slice.Player;
using Wildlife;

namespace AssetViewer.Providers;

/// <summary>
/// The castaway as the slice draws and animates him: the suited figure on the people's own rig, driven
/// by the slice's <see cref="CharacterModel.Drive"/> with the walker's motion, so walking, running,
/// crouching, the jetpack, a fall, a swim, a ladder, the bike's seat and the gesture wheel are all its real poses.
/// </summary>
sealed partial class CastawayStage : AssetInstance, IStageProbe
{
    readonly Node3D _figure;
    readonly List<string> _anims = new()
    {
        "Idle", "Walk", "Run", "Crouch", "Walk, crouched", "Jump", "Fall", "Jetpack", "Swim", "Float", "Climb a ladder", "Ride the hover bike", "Sit in a chair",
        "Gesture: wave", "Gesture: beckon", "Gesture: hands up", "Gesture: stop", "Gesture: point", "Gesture: threaten", "Gesture: thanks",
    };
    readonly MultiMeshInstance3D _marks;
    readonly Vector3[] _at = new Vector3[70];
    string _anim = "";
    float _t, _acc, _speed = 1f, _walked;
    CastawayMotion _m;
    PlayerGesture? _gesture;
    float _since;

    public CastawayStage()
    {
        _figure = CharacterModel.Build();
        AddChild(_figure);
        Stage = new StageSpec { GroundRadius = 7.5f };
        var rng = new Rng(4);
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = new SphereMesh { Radius = 0.5f, Height = 0.7f, RadialSegments = 8, Rings = 4 }, InstanceCount = _at.Length };
        for (int i = 0; i < _at.Length; i++) _at[i] = new Vector3(rng.Range(-5f, 5f), 0f, rng.Range(-5f, 5f));
        _marks = new MultiMeshInstance3D { Name = "Marks", Multimesh = mm, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.3f, 0.28f, 0.25f), Roughness = 1f } };
        AddChild(_marks);
        Begin("Walk");
    }

    public override IReadOnlyList<string> Animations => _anims;
    public override Aabb? FrameBounds => new Aabb(new Vector3(-1.1f, 0f, -1.2f), new Vector3(2.2f, 2.0f, 2.4f));
    public override void SetSpeed(float scale) => _speed = scale;
    public override void PlayAnimation(string name) { base.PlayAnimation(name); Begin(name); if (FastForward.Seconds > 0f) Advance(FastForward.Seconds); }

    public override IEnumerable<(string Label, string Value)> Facts()
    {
        yield return ("Kit", "orange suit, cream plates, dark visor, jetpack");
        yield return ("Height", "1.8 m, 100 kg with suit and pack");
        yield return ("Rig", "the people's own (HumanRig)");
    }

    void Begin(string anim)
    {
        CurrentAnimation = anim;
        _t = 0f;
        _since = 0f;
        _m = new CastawayMotion { Grounded = true };
        _gesture = null;
        switch (anim)
        {
            case "Walk": _m.Speed = 1.6f; break;
            case "Run": _m.Speed = 5.5f; break;
            case "Crouch": _m.Crouched = true; break;
            case "Walk, crouched": _m.Crouched = true; _m.Speed = 1.0f; break;
            case "Jump": _m.Grounded = false; _m.Climb = 4f; break;
            case "Fall": _m.Grounded = false; _m.Climb = -7f; break;
            case "Jetpack": _m.Grounded = false; _m.Jetting = true; _m.Climb = 2f; break;
            case "Swim": _m.Swimming = true; _m.Grounded = false; _m.Speed = 1.4f; break;
            case "Float": _m.Floating = true; _m.Grounded = false; break;
            case "Climb a ladder": _m.Ladder = true; _m.Grounded = false; _m.Climb = 1.2f; break;
            case "Ride the hover bike": _m.Riding = true; break;
            case "Sit in a chair": _m.Seated = true; break;
            case "Gesture: wave": _gesture = PlayerGesture.Wave; break;
            case "Gesture: beckon": _gesture = PlayerGesture.Beckon; break;
            case "Gesture: hands up": _gesture = PlayerGesture.HandsUp; break;
            case "Gesture: stop": _gesture = PlayerGesture.Stop; break;
            case "Gesture: point": _gesture = PlayerGesture.Point; break;
            case "Gesture: threaten": _gesture = PlayerGesture.Threaten; break;
            case "Gesture: thanks": _gesture = PlayerGesture.Thanks; break;
        }
        CharacterModel.Drive(_figure, _m, _gesture, 0f, 0f);
    }

    public override void _Process(double delta)
    {
        _acc += (float)delta * _speed;
        int guard = 0;
        while (_acc >= 1f / 60f && guard++ < 6) { _acc -= 1f / 60f; Tick(1f / 60f); }
        if (_acc > 0.2f) _acc = 0f;
        Present();
    }

    void Tick(float dt)
    {
        _t += dt;
        _walked += _m.Speed * dt;
        if (_gesture != null)
        {
            _since += dt;
            if (_since > CharacterModel.GestureSeconds + 1.2f) _since = 0f;
        }
        CharacterModel.Drive(_figure, _m, _gesture, _since, dt);
    }

    void Present()
    {
        MultiMesh mm = _marks.Multimesh;
        for (int i = 0; i < _at.Length; i++)
        {
            Vector3 s = _at[i];
            float z = Mathf.PosMod(s.Z + _walked + 5f, 10f) - 5f;
            float k = 0.2f + 0.5f * ((i * 37) % 17) / 17f;
            if (MathF.Abs(s.X) < 0.8f && MathF.Abs(z) < 1f) k = 0.001f;
            mm.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(new Vector3(k * 0.5f, k * 0.2f, k * 0.6f)), new Vector3(s.X, 0.01f, z)));
        }
    }

    public void Advance(float seconds)
    {
        int n = (int)MathF.Round(seconds * 60f);
        for (int i = 0; i < n; i++) Tick(1f / 60f);
        Present();
    }

    public string Problems()
    {
        (Vector3 l, Vector3 r) = CharacterModel.Feet(_figure);
        foreach (Vector3 p in new[] { l, r })
            if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z)) return "feet are not finite";
        if (_m.Grounded && !_m.Riding && !_m.Seated && !_m.Ladder && (l.Y < -0.05f || r.Y < -0.05f)) return $"a foot is under the ground ({l.Y:0.00}, {r.Y:0.00})";
        return "";
    }

    public long PoseSignature()
    {
        (Vector3 l, Vector3 r) = CharacterModel.Feet(_figure);
        Vector3 h = CharacterModel.RightHand(_figure);
        return (long)(MathF.Round(l.Z * 30f) * 1000003 + MathF.Round(r.Y * 30f) * 7919 + MathF.Round(h.Y * 30f) * 31 + MathF.Round(h.Z * 30f) * 5 + MathF.Round(l.Y * 30f) * 101);
    }
}
