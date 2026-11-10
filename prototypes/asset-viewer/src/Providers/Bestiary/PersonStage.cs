using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Wildlife;

namespace AssetViewer.Providers;

/// <summary>
/// One person, or a row of them, on the people's own rig: <see cref="HumanRig"/> drawn by
/// <see cref="PersonView"/> and driven by the same <see cref="Gesture"/> a town drives its people with.
/// They walk on the spot with the ground marks sliding past; sitting, working, aiming, crouching, falling
/// and every body act are the game's own poses, nothing is faked.
/// </summary>
sealed partial class PersonStage : AssetInstance, IStageProbe
{
    public const string Idle = "Idle", Walk = "Walk", Run = "Run", CrouchIdle = "Crouch", CrouchWalk = "Walk, crouched", Look = "Look about", Work = "Work, bent over a task",
        Sit = "Sit", Sleep = "Lie down to sleep", Downed = "Hurt and downed", Startled = "Hurt, startled", Aim = "Raise and aim", Reach = "Reach out", Retch = "Retch",
        Stagger = "Stagger";

    readonly Species[] _species;
    readonly HumanRig[] _rigs;
    readonly PersonView[] _views;
    readonly List<string> _anims = new();
    readonly Aabb _bounds;
    readonly string[] _extra;
    readonly float _spacing;
    MultiMeshInstance3D _stones;
    readonly Vector3[] _stoneAt = new Vector3[90];
    string _anim = "";
    float _t, _walked, _acc, _speed = 1f, _hurt;
    BodyAct _act;
    int _style;
    Action<float, int> _pose = (_, _) => { };
    Gesture[] _g;
    float _speedNow;
    float _tile = 4.6f;

    /// <summary>The gesture wheel's names for the body acts it uses.</summary>
    static string ActName(BodyAct a) => a switch
    {
        BodyAct.Greet => "Gesture: wave (greet)", BodyAct.Beckon => "Gesture: beckon", BodyAct.HandsUp => "Gesture: hands up", BodyAct.Warn => "Gesture: stop (warn)",
        BodyAct.Point => "Gesture: point", BodyAct.Taunt => "Gesture: threaten (taunt)", BodyAct.Thanks => "Gesture: thanks",
        _ => "Act: " + Zoo.Title(a.ToString()).ToLowerInvariant(),
    };

    public PersonStage(Species[] people, string[] extraFacts = null, float spacing = 1.5f)
    {
        _species = people;
        _spacing = spacing;
        _extra = extraFacts ?? Array.Empty<string>();
        _rigs = people.Select(s => new HumanRig(s)).ToArray();
        _views = new PersonView[people.Length];
        _g = new Gesture[people.Length];
        float tallest = 0f;
        for (int i = 0; i < people.Length; i++)
        {
            _views[i] = new PersonView { Name = $"Person{i}" };
            AddChild(_views[i]);
            _views[i].Build(_rigs[i]);
            tallest = MathF.Max(tallest, people[i].Figure.Top);
        }
        float half = (people.Length - 1) * spacing * 0.5f;
        _bounds = new Aabb(new Vector3(-half - 0.75f, 0f, -0.8f), new Vector3(half * 2f + 1.5f, tallest + 0.1f, 1.6f));
        _tile = MathF.Max(4.6f, half + 2.6f);
        Stage = new StageSpec { GroundRadius = _tile * 1.5f };
        BuildStones();
        BuildAnimations();
        Begin(_anims.Contains(Walk) ? Walk : Idle);
    }

    void BuildStones()
    {
        var rng = new Rng(9);
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = new SphereMesh { Radius = 0.5f, Height = 0.7f, RadialSegments = 8, Rings = 4 }, InstanceCount = _stoneAt.Length };
        for (int i = 0; i < _stoneAt.Length; i++) _stoneAt[i] = new Vector3(rng.Range(-_tile, _tile), 0f, rng.Range(-_tile, _tile));
        _stones = new MultiMeshInstance3D { Name = "Marks", Multimesh = mm, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.3f, 0.28f, 0.25f), Roughness = 1f } };
        AddChild(_stones);
    }

    void BuildAnimations()
    {
        Figure f = _species[0].Figure;
        bool armed = _species.Any(s => Weapons.Armed(s.Figure.Prop));
        bool ranged = _species.Any(s => Weapons.Ranged(s.Figure.Prop));
        bool husk = _species.Any(s => s.Husk);
        bool carries = _species.Any(s => s.Figure.Prop != "");
        _anims.AddRange(new[] { Idle, Walk, Run, CrouchIdle, CrouchWalk, Look });
        if (armed) _anims.Add(Aim);
        _anims.AddRange(new[] { Work, Sit, Sleep });
        if (husk) { _anims.Add(Reach); _anims.Add(Retch); }
        _anims.AddRange(new[] { Startled, Downed });
        if (_species.Any(s => s.Figure.Stagger > 0.05f)) _anims.Add(Stagger);
        foreach (BodyAct a in Enum.GetValues<BodyAct>())
        {
            if (a == BodyAct.None) continue;
            if (a is BodyAct.Greet or BodyAct.Thanks)
            {
                _anims.Add(ActName(a) + " (hand high)");
                _anims.Add(ActName(a) + " (open palm)");
                _anims.Add(ActName(a) + " (hand to chest)");
            }
            else _anims.Add(ActName(a));
        }
    }

    public override IReadOnlyList<string> Animations => _anims;
    public override Aabb? FrameBounds => _bounds;
    public override void SetSpeed(float scale) => _speed = scale;
    public override void PlayAnimation(string name) { base.PlayAnimation(name); Begin(name); if (FastForward.Seconds > 0f) Advance(FastForward.Seconds); }

    public override IEnumerable<(string Label, string Value)> Facts()
    {
        Species s = _species[0];
        Figure f = s.Figure;
        if (_species.Length > 1) yield return ("People shown", _species.Length.ToString());
        yield return ("Role", f.Role.ToString());
        yield return ("Folk", f.Folk == null || f.Folk.Human ? "human" : $"{f.Folk.Covering}, {FolkParts(f.Folk)}");
        yield return ("Build", $"{f.Sex.ToString().ToLowerInvariant()}, {f.Stage.ToString().ToLowerInvariant()}, {f.Height:0.00} m, build {f.Build:0.0}");
        yield return ("Dress", $"{f.Dress}{(f.Layered.Count > 0 ? ", layers: " + string.Join(" > ", f.Layered) : "")}");
        yield return ("Wearing", f.Wear.Count == 0 ? "nothing extra" : string.Join(", ", f.Wear.OrderBy(w => w, StringComparer.Ordinal)));
        yield return ("Carries", f.Prop == "" ? "nothing" : $"{f.Prop} ({Weapons.HoldOf(f.Prop)})");
        yield return ("Standing", $"{f.Occupancy.ToString().ToLowerInvariant()}{(f.Husk ? ", Husk" : "")}{(s.Mutation > 0f ? $", mutation {s.Mutation:0.0}" : "")}");
        yield return ("Body language", $"greets by {f.Approach}, aimed at: {f.Aimed}, startled: {f.Startled}");
        foreach (string e in _extra)
        {
            int cut = e.IndexOf('|');
            if (cut > 0) yield return (e[..cut], e[(cut + 1)..]);
        }
    }

    static string FolkParts(Folk k)
    {
        var parts = new List<string>();
        if (k.Crown != "") parts.Add(k.Crown);
        if (k.Mouth != "") parts.Add(k.Mouth);
        if (k.Tail != "") parts.Add(k.Tail + " tail");
        if (k.Back != "") parts.Add(k.Back);
        if (k.Ears is not ("human" or "none")) parts.Add(k.Ears + " ears");
        if (k.Eyes != "two") parts.Add(k.Eyes + " eyes");
        return parts.Count == 0 ? "human outline" : string.Join(", ", parts);
    }

    // ---- Running ----------------------------------------------------------------------------------------

    public override void _Process(double delta)
    {
        _acc += (float)delta * _speed;
        int guard = 0;
        while (_acc >= 1f / 60f && guard++ < 6) { _acc -= 1f / 60f; Tick(1f / 60f); }
        if (_acc > 0.2f) _acc = 0f;
        Present();
    }

    float _loop;

    void Tick(float dt)
    {
        _t += dt;
        _walked += _speedNow * dt;
        for (int i = 0; i < _species.Length; i++)
        {
            float half = (_species.Length - 1) * _spacing * 0.5f;
            var g = new Gesture
            {
                Feet = new Vector3(i * _spacing - half, 0f, 0f),
                Yaw = 0f,
                Clock = _t + i * 1.7f,
                Walked = _walked + i * 0.9f,
                Style = _style,
            };
            _pose(dt, i);
            _g[i] = Fill(g, i);
        }
        if (_loop > 0f && _t >= _loop) Begin(_anim);
    }

    // The scratch fields an animation sets, read when a gesture is filled in.
    float _speedV, _crouch, _sit, _work, _aim, _reachV, _retch, _down, _look, _actAmount;

    Gesture Fill(Gesture g, int i)
    {
        g.Speed = _speedV;
        g.Crouch = _crouch;
        g.Sit = _sit;
        g.Work = _work;
        g.Aim = _aim;
        g.Reach = _reachV;
        g.Retch = _retch;
        g.Down = _down;
        g.Look = _look;
        g.Act = _act;
        g.ActAmount = _actAmount;
        g.ActAt = g.Feet + new Vector3(-1.4f, 1.0f, -1.8f);
        return g;
    }

    void Present()
    {
        for (int i = 0; i < _species.Length; i++)
        {
            _rigs[i].Drive(_g[i]);
            _views[i].SyncFar(_g[i].Feet, 0f);
            _views[i].Sync();
            _views[i].Hurt(_hurt);
        }
        // Marks on the ground slide back at the pace they walk (the feet stay in one place).
        MultiMesh mm = _stones.Multimesh;
        for (int i = 0; i < _stoneAt.Length; i++)
        {
            Vector3 s = _stoneAt[i];
            float z = Mathf.PosMod(s.Z + _walked + _tile, _tile * 2f) - _tile;
            float k = 0.2f + 0.5f * ((i * 37) % 17) / 17f;
            if (MathF.Abs(s.X) < _spacing * _species.Length * 0.5f + 0.5f && MathF.Abs(z) < 1f) k = 0.001f; // not under their feet
            mm.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(new Vector3(k * 0.5f, k * 0.2f, k * 0.6f)), new Vector3(s.X, 0.01f, z)));
        }
    }

    // ---- The animations --------------------------------------------------------------------------------------

    void Begin(string anim)
    {
        _anim = anim;
        CurrentAnimation = anim;
        _t = 0f;
        _loop = 0f;
        _walked = 0f;
        _speedNow = 0f;
        _speedV = _crouch = _sit = _work = _aim = _reachV = _retch = _down = _look = _actAmount = 0f;
        _act = BodyAct.None;
        _hurt = 0f;
        _style = 0;
        _pose = (_, _) => { };
        float walk = 1.4f, run = 4.2f;
        switch (anim)
        {
            case Idle: break;
            case Walk: _pose = (_, _) => { _speedNow = walk; _speedV = walk; }; break;
            case Run: _pose = (_, _) => { _speedNow = run; _speedV = run; }; break;
            case CrouchIdle: _crouch = 1f; break;
            case CrouchWalk: _pose = (_, _) => { _speedNow = 0.8f; _speedV = 0.8f; _crouch = 1f; }; break;
            case Look: _pose = (_, _) => _look = MathF.Sin(_t * 0.9f) * 1.0f; break;
            case Work: _pose = (_, _) => _work = Math.Clamp(_t / 0.6f, 0f, 1f); break;
            case Sit: _pose = (_, _) => _sit = Math.Clamp(_t / 0.8f, 0f, 1f); break;
            case Sleep: _loop = 8f; _pose = (_, _) => _down = Math.Clamp((_t - 0.3f) / 1.6f, 0f, 1f); break;
            case Downed:
                _loop = 7f;
                _pose = (_, _) =>
                {
                    _hurt = _t > 0.4f && _t < 1.2f ? 1f - (_t - 0.4f) / 0.8f : 0f;
                    _act = _t < 1.4f ? BodyAct.Startle : BodyAct.None;
                    _actAmount = _t < 1.4f ? Math.Clamp(_t / 0.25f, 0f, 1f) : 0f;
                    _down = Math.Clamp((_t - 1.2f) / 1.0f, 0f, 1f);
                };
                break;
            case Startled:
                _pose = (_, _) =>
                {
                    float k = Mathf.PosMod(_t, 4f);
                    _hurt = k < 0.8f ? 1f - k / 0.8f : 0f;
                    _act = BodyAct.Startle;
                    _actAmount = k < 2.4f ? Math.Clamp(k / 0.2f, 0f, 1f) * Math.Clamp((2.8f - k) / 0.4f, 0f, 1f) : 0f;
                };
                break;
            case Aim: _pose = (_, _) => { float k = Mathf.PosMod(_t, 8f); _aim = k < 0.8f ? k / 0.8f : k < 6f ? 1f : k < 6.8f ? (6.8f - k) / 0.8f : 0f; }; break;
            case Reach: _pose = (_, _) => _reachV = MathF.Sin(_t * 1.1f) > -0.1f ? Math.Clamp(Mathf.PosMod(_t, 5f) / 0.6f, 0f, 1f) : 0f; break;
            case Retch: _pose = (_, _) => _retch = MathF.Pow(MathF.Max(0f, MathF.Sin(_t * 1.6f)), 0.6f); break;
            case Stagger: _pose = (_, _) => { _speedNow = 0.7f; _speedV = 0.7f; }; break;
            default:
                foreach (BodyAct a in Enum.GetValues<BodyAct>())
                {
                    if (a == BodyAct.None) continue;
                    int style = 0;
                    string baseName = ActName(a);
                    if (a is BodyAct.Greet or BodyAct.Thanks)
                    {
                        if (anim == baseName + " (open palm)") style = 1;
                        else if (anim == baseName + " (hand to chest)") style = 2;
                        else if (anim != baseName + " (hand high)") continue;
                    }
                    else if (anim != baseName) continue;
                    BodyAct act = a;
                    int st = style;
                    _loop = 0f;
                    _pose = (_, _) =>
                    {
                        float k = Mathf.PosMod(_t, 4f);
                        _act = act;
                        _style = st;
                        _actAmount = Math.Clamp(k / 0.35f, 0f, 1f) * Math.Clamp((3.6f - k) / 0.4f, 0f, 1f);
                    };
                    break;
                }
                break;
        }
        // Armed people keep the weapon ready while walking or standing; "Raise and aim" lifts it.
    }

    // ---- Probes -------------------------------------------------------------------------------------------

    public void Advance(float seconds)
    {
        int n = (int)MathF.Round(seconds * 60f);
        for (int i = 0; i < n; i++) Tick(1f / 60f);
        Present();
    }

    public string Problems()
    {
        for (int p = 0; p < _rigs.Length; p++)
        {
            HumanRig rig = _rigs[p];
            Figure f = _species[p].Figure;
            foreach (Transform3D t in rig.Pose)
                if (!float.IsFinite(t.Origin.X) || !float.IsFinite(t.Origin.Y) || !float.IsFinite(t.Origin.Z) || !float.IsFinite(t.Basis.X.X) || !float.IsFinite(t.Basis.Y.Y) || !float.IsFinite(t.Basis.Z.Z)) return $"person {p}: non-finite pose";
            if (_down > 0.01f || _sit > 0.2f) continue;
            for (int i = 0; i < rig.FootCount; i++)
            {
                float y = rig.FootDrawn(i).Y;
                if (y < f.Ankle - 0.06f) return $"person {p}: foot {i} sunk, ankle at {y:0.00} m (rests at {f.Ankle:0.00})";
                if (_crouch < 0.1f && _speedV < 0.1f && y > f.Ankle + 0.25f) return $"person {p}: foot {i} floating, ankle at {y:0.00} m";
            }
        }
        return "";
    }

    public long PoseSignature()
    {
        long h = 1469598103934665603L;
        foreach (Transform3D t in _rigs[0].Pose)
        {
            h = (h ^ (long)MathF.Round(t.Origin.X * 40f)) * 1099511628211L;
            h = (h ^ (long)MathF.Round(t.Origin.Y * 40f)) * 1099511628211L;
            h = (h ^ (long)MathF.Round(t.Origin.Z * 40f)) * 1099511628211L;
        }
        return h;
    }
}
