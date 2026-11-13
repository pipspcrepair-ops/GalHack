using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Wildlife;

namespace AssetViewer.Providers;

/// <summary>
/// One space creature in the game's own zero-g simulation: a swimmer pulsing, flapping or undulating
/// through open space, or a hull leech crawling on a derelict's plating. No ground and no up: the
/// space slides past it (drifting specks), and the creature's real rig and view do the rest. Like
/// <see cref="CreatureStage"/>, an animation is a short script over the game's own movement and attack code.
/// </summary>
sealed partial class SpaceStage : AssetInstance, IStageProbe
{
    readonly Species _s;
    readonly SpaceSim _sim;
    readonly Node3D _world = new() { Name = "World" };
    readonly DummyTarget _target = new() { Name = "Target", Health = new Health(100f) };
    readonly List<string> _anims = new();
    readonly bool _clinger;
    readonly Hull _hull;
    readonly Aabb _bounds;
    readonly Vector3[] _dust = new Vector3[140];
    readonly float _tile;
    readonly string[] _extra;
    Creature _c;
    CreatureView _view;
    MultiMeshInstance3D _specks;
    MeshInstance3D _marker;
    string _anim = "";
    float _t, _acc, _speed = 1f, _loop, _lastHurt;
    bool _manual = true, _markerOn;
    Action<float> _script = _ => { };

    public SpaceStage(Species species, string[] extraFacts = null)
    {
        _s = species;
        _extra = extraFacts ?? Array.Empty<string>();
        _clinger = species.Rig == RigKind.Clinger;
        float size = MathF.Max(0.5f, species.Body.Size);
        _sim = new SpaceSim(Zoo.Data);
        AddChild(_world);
        Stage = new StageSpec { Ground = false, Background = "space", MinDistanceShare = 0.03f };
        _tile = MathF.Max(24f, size * 3f);
        if (_clinger)
        {
            _hull = new Hull { Center = new Vector3(0f, -size * 0.3f, 0f), Half = new Vector3(size * 1.5f, size * 0.3f, size * 2f) };
            _sim.Hulls.Add(_hull);
            BuildHull(size);
            float w = _hull.Half.X, d = _hull.Half.Z;
            _bounds = new Aabb(new Vector3(-w, -size * 0.4f, -d), new Vector3(w * 2f, size * 1.6f, d * 2f));
        }
        else
        {
            float R = MathF.Max(size * 0.45f, 0.5f);
            _bounds = new Aabb(new Vector3(-R, -R * 0.5f, -R), new Vector3(R * 2f, R, R * 2f));
            BuildSpecks(size);
        }
        _marker = new MeshInstance3D { Name = "Target", Mesh = new SphereMesh { Radius = MathF.Max(0.15f, size * 0.12f), Height = MathF.Max(0.3f, size * 0.24f) }, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _marker.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.8f, 0.2f, 0.16f), EmissionEnabled = true, Emission = new Color(0.6f, 0.1f, 0.05f) };
        _world.AddChild(_marker);

        bool armed = species.Stats.Damage > 0f;
        _anims.Add(CreatureStage.Idle);
        _anims.Add(CreatureStage.Walk);
        if (species.Stats.RunSpeed > species.Stats.WalkSpeed * 1.2f) _anims.Add(CreatureStage.Run);
        _anims.Add(CreatureStage.Turn);
        _anims.Add(CreatureStage.Alert);
        _anims.Add(CreatureStage.Feed);
        if (armed) { _anims.Add(CreatureStage.First); _anims.Add(CreatureStage.Follow); }
        if (!_clinger && species.Stats.RunSpeed > 0f) _anims.Add(CreatureStage.Flee);
        _anims.Add(CreatureStage.Stranger);
        _anims.Add(CreatureStage.Hurt);
        _anims.Add(CreatureStage.Death);
        Begin(CreatureStage.Walk);
    }

    public override IReadOnlyList<string> Animations => _anims;
    public override Aabb? FrameBounds => _bounds;
    public override void SetSpeed(float scale) => _speed = scale;
    public override float CycleSeconds => _anim == CreatureStage.Stranger ? 40f : CreatureStage.ScriptedCycle(_anim, out _);
    public override bool CycleLoops { get { CreatureStage.ScriptedCycle(_anim, out bool loops); return loops; } }
    public override void PlayAnimation(string name) { base.PlayAnimation(name); Begin(name); if (FastForward.Seconds > 0f) Advance(FastForward.Seconds); }

    public override IEnumerable<(string Label, string Value)> Facts()
    {
        foreach (var f in Zoo.Facts(_s)) yield return f;
        foreach (string e in _extra)
        {
            int cut = e.IndexOf('|');
            if (cut > 0) yield return (e[..cut], e[(cut + 1)..]);
        }
        if (_s.Drops.Materials.Count > 0) yield return ("Drops", string.Join(", ", _s.Drops.Materials.Select(m => $"{m.Material} ({m.Rarity})")));
    }

    // ---- The place ---------------------------------------------------------------------------------

    void BuildHull(float size)
    {
        var plate = new StandardMaterial3D { AlbedoColor = new Color(0.34f, 0.35f, 0.37f), Roughness = 0.7f, Metallic = 0.5f };
        var seam = new StandardMaterial3D { AlbedoColor = new Color(0.16f, 0.16f, 0.17f), Roughness = 0.9f };
        Vector3 h = _hull.Half;
        _world.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = h * 2f }, MaterialOverride = plate, Position = _hull.Center });
        // Seams and a few ribs across the top, so a crawl reads against the plating.
        for (float z = -h.Z + size; z < h.Z; z += size * 1.3f)
            _world.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(h.X * 2f + 0.02f, 0.03f, size * 0.05f) }, MaterialOverride = seam, Position = _hull.Center + new Vector3(0f, h.Y + 0.002f, z), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        for (float x = -h.X + size; x < h.X; x += size * 1.6f)
            _world.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(size * 0.05f, 0.03f, h.Z * 2f + 0.02f) }, MaterialOverride = seam, Position = _hull.Center + new Vector3(x, h.Y + 0.002f, 0f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
    }

    void BuildSpecks(float size)
    {
        var rng = new Rng(5);
        float dot = MathF.Max(0.03f, size * 0.012f);
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = new SphereMesh { Radius = dot, Height = dot * 2f, RadialSegments = 6, Rings = 3 }, InstanceCount = _dust.Length };
        for (int i = 0; i < _dust.Length; i++) _dust[i] = new Vector3(rng.Range(-_tile, _tile), rng.Range(-_tile, _tile), rng.Range(-_tile, _tile));
        _specks = new MultiMeshInstance3D { Name = "Specks", Multimesh = mm, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.8f, 0.85f, 0.95f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _world.AddChild(_specks);
    }

    // ---- Running -------------------------------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (_c == null) return;
        _acc += (float)delta * _speed;
        int guard = 0;
        while (_acc >= 1f / 60f && guard++ < 6) { _acc -= 1f / 60f; Tick(1f / 60f); }
        if (_acc > 0.2f) _acc = 0f;
        Present();
    }

    void Tick(float dt)
    {
        _t += dt;
        _script(dt);
        if (_target.Health.Current < _target.Health.Max) _target.Health.Current = _target.Health.Max;
        if (_manual) StepManual(dt);
        else _sim.Step(dt);
        if (_loop > 0f && _t >= _loop) Begin(_anim);
    }

    /// <summary>The sim's own step without its brain: the script has set the intentions, the game's attack, motion and rig code does the rest.</summary>
    void StepManual(float dt)
    {
        _sim.Time += dt;
        _sim.Frame++;
        Creature c = _c;
        c.Age += dt;
        c.HurtFlash = MathF.Max(0f, c.HurtFlash - dt * 4f);
        if (c.Dead)
        {
            c.DeadTime += dt;
        }
        else SpaceAttacks.Update(_sim, c, dt);
        SpaceMotion.Move(_sim, c, dt);
        c.Rig.Update(null, c, dt);
    }

    void Present()
    {
        if (_c == null || _view == null) return;
        _view.Sync();
        if (!_clinger)
        {
            Vector3 p = _c.Position;
            _world.Position = -p;
            if (_specks != null)
            {
                MultiMesh mm = _specks.Multimesh;
                for (int i = 0; i < _dust.Length; i++)
                {
                    Vector3 d = _dust[i];
                    Vector3 off = new(Mathf.PosMod(d.X - p.X + _tile, _tile * 2f) - _tile, Mathf.PosMod(d.Y - p.Y + _tile, _tile * 2f) - _tile, Mathf.PosMod(d.Z - p.Z + _tile, _tile * 2f) - _tile);
                    mm.SetInstanceTransform(i, new Transform3D(Basis.Identity, p + off));
                }
            }
        }
        _marker.Visible = _markerOn;
        _marker.Position = _target.Position;
    }

    void Respawn()
    {
        if (_c != null) { _c.Removed = true; _view?.QueueFree(); }
        _sim.Creatures.Clear();
        _sim.Targets.Clear();
        _sim.Targets.Add(_target);
        _target.Health.Current = _target.Health.Max;
        _target.Position = new Vector3(0f, 0f, -40f);
        if (_clinger)
        {
            var rng = new Rng(3);
            _c = _sim.SpawnOnHull(_s, _hull, 1, rng, 2)[0];
            _c.Position = _hull.ClampToFace(2, _hull.FacePoint(2, 0f, 0f), _c.Radius);
            SpaceSim.Cling(_c, _hull, 2);
            _c.Anchor = _c.Position;
            _c.Rig.Reset(null, _c);
        }
        else
        {
            _c = _sim.Spawn(_s, Vector3.Zero, null, new Vector3(0f, 0f, -1f));
            _c.Yaw = 0f;
            _c.Rig.Reset(null, _c);
        }
        _view = new CreatureView();
        _world.AddChild(_view);
        _view.Build(_c);
        _view.Sync();
        _markerOn = false;
        _acc = 0f;
    }

    void Begin(string anim)
    {
        _anim = anim;
        CurrentAnimation = anim;
        _t = 0f;
        _loop = 0f;
        _manual = true;
        _lastHurt = 0f;
        Respawn();
        _script = _ => { };
        float walk = MathF.Max(0.3f, _s.Stats.WalkSpeed), run = MathF.Max(walk, _s.Stats.RunSpeed);
        switch (anim)
        {
            case CreatureStage.Idle: break;
            case CreatureStage.Walk: _script = _ => Cruise(walk); break;
            case CreatureStage.Run: _script = _ => Cruise(run); break;
            case CreatureStage.Turn:
                _script = _ =>
                {
                    float a = MathF.Sin(_t * 0.5f) * 2.4f;
                    _c.Desired = Vector3.Zero;
                    _c.FacePoint = _c.Position + new Vector3(-MathF.Sin(a), 0.3f * MathF.Sin(_t * 0.4f), -MathF.Cos(a)) * 10f;
                };
                break;
            case CreatureStage.Alert:
                _script = _ =>
                {
                    _c.Alert = MathF.Sin(_t * 0.5f) > -0.2f ? 1f : 0f;
                    _c.LookAt = _c.Position + new Vector3(MathF.Sin(_t * 0.8f) * 6f, 1f, -6f);
                    _c.Desired = Vector3.Zero;
                };
                break;
            case CreatureStage.Feed:
                _script = _ => { _c.WantGraze = Mathf.PosMod(_t, 5f) > 1f ? 1f : 0f; if (!_clinger) _c.Desired = _c.Forward3() * walk * 0.25f; else _c.Desired = Vector3.Zero; };
                break;
            case CreatureStage.First: _script = Strike(true); break;
            case CreatureStage.Follow: _script = Strike(false); break;
            case CreatureStage.Flee:
                _script = _ =>
                {
                    float a = _t * 0.6f;
                    _c.SetState(CState.Flee);
                    _c.Desired = new Vector3(-MathF.Sin(a), 0.1f * MathF.Sin(a * 0.7f), -MathF.Cos(a)) * run;
                };
                break;
            case CreatureStage.Hurt:
                _script = _ =>
                {
                    _c.Desired = Vector3.Zero;
                    if (_t - _lastHurt > 2.2f && !_c.Dead) { _lastHurt = _t; _sim.Damage(_c, _c.Stats.MaxHealth * 0.12f, null); if (_c.Health < _c.Stats.MaxHealth * 0.4f) _c.Health = _c.Stats.MaxHealth; }
                };
                break;
            case CreatureStage.Death:
                _loop = 9f;
                _script = _ =>
                {
                    _c.Desired = Vector3.Zero;
                    if (_t > 1.2f && !_c.Dead) _sim.Kill(_c, null);
                };
                break;
            case CreatureStage.Stranger: StartStranger(); break;
        }
    }

    void Cruise(float speed)
    {
        _c.SetState(CState.Wander);
        if (_clinger)
        {
            // Along the hull: a ring round the middle of its top face.
            Vector3 p = _c.Position - _hull.Center;
            Vector3 flat = new(p.X, 0f, p.Z);
            float radius = MathF.Max(1.2f, _s.Body.Size * 0.9f), r = flat.Length();
            Vector3 radial = r > 0.05f ? flat / r : Vector3.Right;
            Vector3 tangent = new(-radial.Z, 0f, radial.X);
            _c.Desired = (tangent - radial * ((r - radius) / radius) * 1.6f).Normalized() * speed;
            return;
        }
        float rad = MathF.Max(_s.Body.Size * 1.6f, 4f), a = _t * speed / rad;
        _c.Desired = new Vector3(MathF.Cos(a), 0.12f * MathF.Sin(a * 0.6f), MathF.Sin(a)) * speed;
    }

    Action<float> Strike(bool first)
    {
        float wait = 0f;
        return dt =>
        {
            if (_c.Attacking) { _c.Desired = Vector3.Zero; return; }
            // A cling lost to a strike is taken up again where it was.
            if (_clinger && !_c.Clinging)
            {
                _c.Position = _c.Anchor;
                SpaceSim.Cling(_c, _hull, 2);
                _c.Rig.Reset(null, _c);
            }
            wait -= dt;
            if (wait > 0f) return;
            Vector3 fwd = _c.Forward3();
            if (_clinger) fwd = new Vector3(fwd.X, 0.35f, fwd.Z).Normalized();
            float gap = MathF.Max(1f, SpaceBrain.StrikeRange(_c, _sim.Combat) * 0.85f);
            _target.Position = _c.Position + fwd * gap;
            _markerOn = true;
            _c.FacePoint = _target.Position;
            if (first) _c.RememberAttack(_target, -999f);
            else _c.RememberAttack(_target, _sim.Time);
            if (SpaceAttacks.Start(_sim, _c, _target)) wait = first ? 3f : 1.2f;
        };
    }

    void StartStranger()
    {
        _manual = false;
        _script = _ =>
        {
            float k = Mathf.PosMod(_t, 40f);
            float reach = MathF.Max(_s.Stats.Perception * 0.9f, 8f), near = MathF.Max(_s.Body.Size * 1.2f, 4f);
            float dist = k < 12f ? Mathf.Lerp(reach, near, k / 12f) : k < 22f ? near : k < 34f ? Mathf.Lerp(near, reach, (k - 22f) / 12f) : reach;
            Vector3 home = _clinger ? _hull.FacePoint(2, 0f, 0f) : Vector3.Zero;
            _target.Position = home + new Vector3(0f, _clinger ? 0.8f : 0f, -dist);
            _markerOn = true;
        };
    }

    // ---- Probes ------------------------------------------------------------------------------------------

    public Creature Creature => _c;
    public Node3D Subject => _view;
    public Vector3 Anchor => _c.Position + _world.Position;
    public Vector3? GroundShift => null;

    public void Feet(List<FootMark> into) => FootMarks.From(_c.Rig, _world.Position, into);

    public void Advance(float seconds)
    {
        int n = (int)MathF.Round(seconds * 60f);
        for (int i = 0; i < n; i++) Tick(1f / 60f);
        Present();
    }

    public string Problems()
    {
        if (_c == null) return "no creature";
        foreach (Transform3D t in _c.Rig.Pose)
            if (!float.IsFinite(t.Origin.X) || !float.IsFinite(t.Origin.Y) || !float.IsFinite(t.Origin.Z)) return "non-finite pose";
        if (!float.IsFinite(_c.Position.X) || !float.IsFinite(_c.Position.Y)) return "creature position is not finite";
        if (_clinger && _c.Clinging && !_c.Dead)
        {
            // Lying on the plating: the body's centre a little off the face, never inside the hull.
            float d = _hull.SignedDistance(Crawl_Center());
            if (d < -0.02f) return $"sunk into the hull by {-d:0.00} m";
        }
        return "";
    }

    Vector3 Crawl_Center() => _c.Position;

    public long PoseSignature()
    {
        long h = 1469598103934665603L;
        foreach (Transform3D t in _c.Rig.Pose)
        {
            h = (h ^ (long)MathF.Round((t.Origin.X - _c.Position.X) * 10f)) * 1099511628211L;
            h = (h ^ (long)MathF.Round((t.Origin.Y - _c.Position.Y) * 10f)) * 1099511628211L;
            h = (h ^ (long)MathF.Round((t.Origin.Z - _c.Position.Z) * 10f)) * 1099511628211L;
        }
        return h;
    }
}
