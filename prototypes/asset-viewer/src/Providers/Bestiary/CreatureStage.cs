using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Wildlife;

namespace AssetViewer.Providers;

/// <summary>
/// One creature alive in the game's own simulation: a <see cref="WildSim"/> with a flat ground (or a
/// sealed room of blocks for what lives in caves and places), the creature's real rig and brain, and
/// its real view. An animation is a little script that sets the creature's intentions or drops a
/// target in front of it, then lets the sim run: walking, attacking, fleeing, dying and the rest all
/// come out of the game's own code. A creature walks on the spot: the sim world slides under it.
/// </summary>
sealed partial class CreatureStage : AssetInstance
{
    // The animations, by name. Which of them a species has is decided in AnimationsFor.
    public const string Idle = "Idle", Walk = "Walk", Run = "Run", Turn = "Turn on the spot", Alert = "Look about, alert", Feed = "Feed",
        Rest = "Rest", Hurt = "Hurt", Death = "Death", Thrown = "Thrown and downed", First = "Telegraph and strike", Follow = "Strike, quick follow-up",
        Stalk = "Stalk, crouched", Flee = "Flee", Stranger = "Meets a stranger (its own brain)", Hunt = "Hunts a target (its own brain)",
        Wander = "Wanders (its own brain)", Warn = "Warning display", Hide = "Hide, buried or flattened", Root = "Root", Hover = "Hover", Land = "Landed, folded",
        Drift = "Drift", Tell = "Tell, giving itself away", Cling = "Cling to a wall", Ceiling = "Climb to the ceiling", Drop = "Drop from the ceiling",
        Fly = "Fly", Descend = "Come down low";

    readonly Species _s;
    readonly bool _room;
    readonly WildSim _sim;
    readonly Node3D _world = new() { Name = "World" };
    readonly DummyTarget _target = new() { Name = "Target", Health = new Health(100f) };
    readonly List<string> _anims;
    readonly Aabb _bounds;
    BlockSpace _space;
    Creature _c;
    CreatureView _view;
    MeshInstance3D _ground, _marker;
    MultiMeshInstance3D _stones;
    readonly Vector3[] _stoneAt = new Vector3[180];
    const float StoneTile = 64f;
    static float DebugEvery => FastForward.DebugEvery;

    string _anim = "";
    float _t, _clock, _acc, _speed = 1f, _loop, _angle;
    Action<float> _script = _ => { };
    bool _manual;
    int _cycle;
    float _lastHurt;
    readonly string[] _extra;

    public override bool Spins => true;
    public override float CycleSeconds => _cycleSeconds;
    public override bool CycleLoops => _loops;
    float _cycleSeconds;
    bool _loops = true;

    /// <summary>
    /// The length of a scripted animation whose period is written in its script, and whether it repeats (true) or is
    /// played once to a held end (false). 0: it runs on the rig's own rhythm (a gait, a wingbeat, an attack), measured from the pose.
    /// </summary>
    public static float ScriptedCycle(string anim, out bool loops)
    {
        loops = anim is not (Death or Thrown or Hurt);
        return anim switch
        {
            Death => 9f, Thrown => 7f, Hurt => 2.2f, Warn => 5f, Tell => 6f, Hide => 9f, Root => 8f, Feed => 5f,
            Turn => MathF.Tau / 0.6f, Alert => MathF.Tau / 0.5f, Stranger => 36f,
            _ => 0f,
        };
    }

    public CreatureStage(Species species, bool room, string[] extraFacts = null)
    {
        _s = species;
        _room = room;
        _extra = extraFacts ?? Array.Empty<string>();
        Stage = new StageSpec { Ground = false, Background = room ? "studio" : "sky", MinDistanceShare = 0.05f };
        float L = MathF.Max(0.5f, MathF.Max(species.BodyLength, species.BodyHeight));
        _sim = new WildSim(Zoo.Data, new Wildlife.Terrain((x, z) => 0f, 220f), new NavGrid(220f, 3f));
        AddChild(_world);
        if (room) BuildRoom();
        else BuildGround();
        _anims = AnimationsFor(species, room);
        float R = MathF.Max(L * 0.62f, 0.35f);
        if (room)
        {
            int w = RoomHalf(species), h = RoomHeight(species);
            _bounds = new Aabb(new Vector3(-w * 0.8f, 0f, -w * 0.8f), new Vector3(w * 1.6f, h * 0.9f, w * 1.6f));
        }
        else if (species.Rig == RigKind.Swarm)
        {
            float cloud = MathF.Max(2f, species.Body.Size * 2.2f);
            _bounds = new Aabb(new Vector3(-cloud, species.Flies ? 0.2f : 0f, -cloud), new Vector3(cloud * 2f, cloud * 0.6f, cloud * 2f));
        }
        else if (species.Flies)
        {
            // A flyer is held at one height in view and the ground comes up to meet it when it lands.
            _flyer = true;
            _hoverBase = MathF.Max(0.6f, R * 0.8f);
            _bounds = new Aabb(new Vector3(-R, _hoverBase - R * 0.6f, -R), new Vector3(R * 2f, R * 1.3f, R * 2f));
        }
        else
            _bounds = new Aabb(new Vector3(-R, 0f, -R), new Vector3(R * 2f, MathF.Max(species.BodyHeight * 1.15f, R * 0.9f), R * 2f));
        string first = _anims.Contains(Walk) ? Walk : _anims[0];
        Begin(first);
        if (FastForward.Seconds > 0f) Advance(FastForward.Seconds);
    }

    // ---- The place ---------------------------------------------------------------------------------

    static int RoomHalf(Species s) => Math.Clamp((int)MathF.Ceiling(MathF.Max(s.BodyLength, s.BodyHeight) * 1.9f), 3, 9);
    static int RoomHeight(Species s) => Math.Clamp((int)MathF.Ceiling(MathF.Max(s.BodyLength, s.BodyHeight) * 1.5f) + 1, 3, 7);

    void BuildGround()
    {
        var mat = new StandardMaterial3D { AlbedoColor = new Color(0.34f, 0.31f, 0.26f), Roughness = 0.95f };
        _ground = new MeshInstance3D { Name = "Ground", Mesh = new CylinderMesh { TopRadius = 900f, BottomRadius = 900f, Height = 0.2f, RadialSegments = 64 }, MaterialOverride = mat };
        _world.AddChild(_ground);
        var rng = new Rng(7);
        var stone = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = new SphereMesh { Radius = 0.5f, Height = 0.7f, RadialSegments = 8, Rings = 4 }, InstanceCount = _stoneAt.Length };
        for (int i = 0; i < _stoneAt.Length; i++) _stoneAt[i] = new Vector3(rng.Range(-StoneTile, StoneTile), rng.Range(0.05f, 0.18f), rng.Range(-StoneTile, StoneTile));
        _stones = new MultiMeshInstance3D { Name = "Stones", Multimesh = stone, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.27f, 0.25f, 0.22f), Roughness = 1f } };
        _world.AddChild(_stones);
        _marker = Marker();
        _world.AddChild(_marker);
    }

    static MeshInstance3D Marker()
    {
        var m = new MeshInstance3D { Name = "Target", Mesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.12f, Height = 1.8f }, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        m.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.78f, 0.2f, 0.16f), Roughness = 0.6f, EmissionEnabled = true, Emission = new Color(0.5f, 0.08f, 0.05f) };
        return m;
    }

    void BuildRoom()
    {
        int w = RoomHalf(_s), h = RoomHeight(_s);
        _space = new BlockSpace((x, y, z) => y < 0, new Vector3I(-w, -1, -w), new Vector3I(w, h, w));
        var floor = new StandardMaterial3D { AlbedoColor = new Color(0.30f, 0.29f, 0.27f), Roughness = 0.95f };
        var wall = new StandardMaterial3D { AlbedoColor = new Color(0.46f, 0.45f, 0.42f), Roughness = 0.95f };
        void Slab(Vector3 centre, Vector3 size, Material m)
        {
            _world.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = m, Position = centre, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
        // Walls and ceiling are single faces turned inward: from outside the room is open to the eye.
        void Face(Vector3 centre, Vector3 rotationDeg, float width, float height)
        {
            _world.AddChild(new MeshInstance3D { Mesh = new QuadMesh { Size = new Vector2(width, height) }, MaterialOverride = wall, Position = centre, RotationDegrees = rotationDeg, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
        Slab(new Vector3(0f, -0.15f, 0f), new Vector3(w * 2f, 0.3f, w * 2f), floor);
        Face(new Vector3(0f, h, 0f), new Vector3(90f, 0f, 0f), w * 2f, w * 2f);
        Face(new Vector3(w, h * 0.5f, 0f), new Vector3(0f, -90f, 0f), w * 2f, h);
        Face(new Vector3(-w, h * 0.5f, 0f), new Vector3(0f, 90f, 0f), w * 2f, h);
        Face(new Vector3(0f, h * 0.5f, w), new Vector3(0f, 180f, 0f), w * 2f, h);
        Face(new Vector3(0f, h * 0.5f, -w), new Vector3(0f, 0f, 0f), w * 2f, h);
        // A few blocks to give the walls and floor something to read against.
        var rng = new Rng(11);
        var trim = new StandardMaterial3D { AlbedoColor = new Color(0.22f, 0.22f, 0.21f), Roughness = 1f };
        for (int i = 0; i < 8; i++)
        {
            float x = rng.Range(-w + 0.5f, w - 0.5f), z = rng.Range(-w + 0.5f, w - 0.5f);
            if (MathF.Abs(x) < 1.5f && MathF.Abs(z) < 1.5f) continue;
            _world.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.12f, 0.02f, 0.5f) }, MaterialOverride = trim, Position = new Vector3(x, 0.01f, z), RotationDegrees = new Vector3(0f, rng.Range(0f, 180f), 0f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
        _marker = Marker();
        _world.AddChild(_marker);
    }

    // ---- Which animations a species has ---------------------------------------------------------

    static List<string> AnimationsFor(Species s, bool room)
    {
        var a = new List<string> { Idle };
        bool mobile = !s.Rooted;
        bool armed = s.Stats.Damage > 0f;
        bool glider = s.Flight == FlightKind.Glide;
        bool hover = s.Flight == FlightKind.Hover;
        bool winged = s.Rig == RigKind.Winged;
        bool bag = s.Rig == RigKind.GasBag;
        bool swarm = s.Rig == RigKind.Swarm;
        bool humanoid = s.Rig == RigKind.Humanoid;
        bool grounded = !winged && !bag && !swarm;
        if (room)
        {
            a.Add(Walk);
            if (s.Stats.RunSpeed > s.Stats.WalkSpeed * 1.2f) a.Add(Run);
            a.Add(Turn);
            if (s.Climbs) { a.Add(Cling); a.Add(Ceiling); }
            if (s.Tells.Length > 0 || s.Role is DeepRole.Ambusher or DeepRole.Lurker or DeepRole.Crawler) a.Add(Tell);
            if (s.Role is DeepRole.Nester or DeepRole.DerelictNester) a.Add(Warn);
            if (s.Diet != Diet.None && s.Diet != Diet.Prey) a.Add(Feed);
            if (armed)
            {
                if (s.Climbs && s.Attack.Style == AttackStyle.Drop) a.Add(Drop);
                a.Add(First);
                a.Add(Follow);
            }
            a.Add(Hurt);
            a.Add(Death);
            return a;
        }
        if (mobile)
        {
            if (glider) { a.Add(Fly); }
            else
            {
                a.Add(Walk);
                if (s.Stats.RunSpeed > s.Stats.WalkSpeed * 1.2f || hover) a.Add(Run);
            }
            if (!bag && !swarm && !winged) a.Add(Turn);
            if (hover) { a.Add(Hover); }
            if (winged) a.Add(Land);
            if (bag) { a.Add(Drift); a.Add(Descend); }
        }
        else a.Add(Root);
        if (!glider) a.Add(Alert);
        if (!bag && !(swarm && s.Flies) && !winged && !humanoid) a.Add(Feed);
        if (winged && !glider) a.Add(Feed);
        if (s.RestKind != RestKind.None && !s.Rooted) a.Add(Rest);
        if (s.Burrows || s.Behaviour == Behaviour.Ambusher) a.Add(Hide);
        if (s.Behaviour is Behaviour.Predator or Behaviour.PackHunter or Behaviour.Ambusher && grounded && mobile) a.Add(Stalk);
        if (s.Behaviour == Behaviour.TerritorialGiant && armed) a.Add(Warn);
        if (armed && !glider)
        {
            a.Add(First);
            a.Add(Follow);
        }
        if (mobile && s.Stats.RunSpeed > 0f && !glider) a.Add(Flee);
        a.Add(Wander);
        a.Add(Stranger);
        if (armed || s.Stats.Retaliates) a.Add(Hunt);
        a.Add(Hurt);
        a.Add(Death);
        if (grounded && !humanoid) a.Add(Thrown);
        else if (humanoid) a.Add(Thrown);
        foreach (string lead in new[] { Walk, Fly, Root }) if (a.Remove(lead)) { a.Insert(0, lead); break; }
        return a;
    }

    public override IReadOnlyList<string> Animations => _anims;

    public override IEnumerable<(string Label, string Value)> Facts()
    {
        foreach (var f in Zoo.Facts(_s)) yield return f;
        foreach (string e in _extra)
        {
            int cut = e.IndexOf('|');
            if (cut > 0) yield return (e[..cut], e[(cut + 1)..]);
        }
    }

    public override Aabb? FrameBounds => _bounds;
    public override void SetSpeed(float scale) => _speed = scale;

    public override void PlayAnimation(string name)
    {
        base.PlayAnimation(name);
        Begin(name);
        if (FastForward.Seconds > 0f) Advance(FastForward.Seconds);
    }

    // ---- Running ---------------------------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (_c == null) return;
        _acc += (float)delta * _speed;
        int guard = 0;
        while (_acc >= 1f / 60f && guard++ < 6)
        {
            _acc -= 1f / 60f;
            Tick(1f / 60f);
        }
        if (_acc > 0.2f) _acc = 0f;
        Present();
    }

    void Tick(float dt)
    {
        _t += dt;
        _clock += dt;
        _script(dt);
        if (_manual && !_c.Dead) { _c.ThinkEvery = 1e9f; _c.ThinkClock = 0f; }
        if (_target.Health.Current < _target.Health.Max) _target.Health.Current = _target.Health.Max;
        _sim.Step(dt);
        if (DebugEvery > 0f && (int)(_t / DebugEvery) != (int)((_t - dt) / DebugEvery)) GD.Print($"[{_anim}] t={_t:0.0} pos={_c.Position} up={_c.Facing.Y} state={_c.State} phase={_c.Phase} climbs={_s.Climbs} role={_s.Role} speed={_c.CrawlSpeed:0.00} falling={_c.Falling}");
        if (_loop > 0f && _t >= _loop) Begin(_anim, true);
    }

    /// <summary>Copies the pose onto the meshes and slides the world under the creature, so it stays in front of the camera.</summary>
    void Present()
    {
        if (_c == null || _view == null) return;
        _view.Sync();
        if (_room)
        {
            _marker.Visible = _target.Attackable && _markerOn;
            _marker.Position = _target.Position + Vector3.Up * 0.9f;
            return;
        }
        Vector3 p = _c.Position;
        float lift = _flyer ? p.Y - _hoverBase : 0f;
        _world.Position = new Vector3(-p.X, -lift, -p.Z);
        // Slide the ground under it, and the stones past it.
        _ground.Position = new Vector3(p.X, -0.1f, p.Z);
        if (_stones != null && (p - _lastStone).LengthSquared() > 0.0004f || _stonesDirty)
        {
            _lastStone = p;
            _stonesDirty = false;
            MultiMesh mm = _stones.Multimesh;
            for (int i = 0; i < _stoneAt.Length; i++)
            {
                Vector3 s = _stoneAt[i];
                float x = Mathf.PosMod(s.X - p.X + StoneTile, StoneTile * 2f) - StoneTile, z = Mathf.PosMod(s.Z - p.Z + StoneTile, StoneTile * 2f) - StoneTile;
                float k = 0.3f + 0.9f * ((i * 37) % 17) / 17f;
                mm.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(new Vector3(k, k * 0.45f, k * 1.2f)), new Vector3(p.X + x, 0.04f, p.Z + z)));
            }
        }
        _marker.Visible = _markerOn;
        _marker.Position = _target.Position + Vector3.Up * 0.9f;
    }

    bool _flyer;
    float _hoverBase;
    Vector3 _lastStone = new(1e9f, 0f, 0f);
    bool _stonesDirty = true, _markerOn;

    // ---- Starting an animation ---------------------------------------------------------------

    void Respawn()
    {
        if (_c != null) { _sim.Remove(_c); _view?.QueueFree(); }
        _sim.Clear();
        _sim.Targets.Clear();
        _sim.Targets.Add(_target);
        _target.Health.Current = _target.Health.Max;
        _target.Attackable = true;
        _target.Position = new Vector3(0f, 0f, -30f);
        Herd herd = null;
        Pack pack = null;
        if (_s.Behaviour == Behaviour.Herd && !_room) { herd = new Herd { Home = Vector3.Zero, Goal = Vector3.Zero, Center = Vector3.Zero, Spread = 3f }; _sim.Herds.Add(herd); }
        if (_s.Behaviour == Behaviour.PackHunter && !_room) { pack = new Pack { Home = Vector3.Zero, Center = Vector3.Zero }; _sim.Packs.Add(pack); }
        if (_room)
        {
            _c = _sim.Spawn(_s, new Vector3(0f, 0f, 0f), herd, pack, true);
            _c.Space = _space;
            _c.Clearance = Crawl.ClearanceFor(_s);
            Crawl.Place(_sim, _c, new Vector3(0f, 0.4f, 0f), null);
            _c.Home = _c.Position;
        }
        else _c = _sim.Spawn(_s, Vector3.Zero, herd, pack, true);
        _c.Yaw = 0f;
        _c.Facing = SpaceMath.Frame(_c.Forward, Vector3.Up);
        if (_room) Crawl.Place(_sim, _c, new Vector3(0f, 0.4f, 0f), null);
        _c.Rig.Reset(_sim, _c);
        _c.Home = _c.Position;
        _view = new CreatureView();
        _world.AddChild(_view);
        _view.Build(_c);
        _view.Sync();
        _stonesDirty = true;
        _markerOn = false;
        _acc = 0f;
    }

    void Begin(string anim, bool again = false)
    {
        _anim = anim;
        CurrentAnimation = anim;
        _t = 0f;
        _loop = 0f;
        _manual = true;
        _markerOn = false;
        _cycle = again ? _cycle + 1 : 0;
        _lastHurt = 0f;
        _cycleSeconds = ScriptedCycle(anim, out _loops);
        Respawn();
        _script = _ => { };
        float walk = MathF.Max(0.3f, _s.Stats.WalkSpeed), run = MathF.Max(walk, _s.Stats.RunSpeed);
        switch (anim)
        {
            case Idle: break;
            case Walk: _script = dt => Circle(walk); break;
            case Run: _script = dt => Circle(run); break;
            case Fly: _script = dt => Circle(MathF.Max(walk, Heft.CruiseAirspeed(_c))); _c.Altitude = 3f; break;
            case Stalk: _script = dt => { _c.WantCrouch = 0.8f; Circle(walk * 0.7f); }; break;
            case Turn:
                _script = dt => { _angle = MathF.Sin(_t * 0.6f) * 2.2f; _c.Desired = Vector3.Zero; _c.FacePoint = _c.Position + new Vector3(-MathF.Sin(_angle), 0f, -MathF.Cos(_angle)) * 6f; };
                break;
            case Alert:
                _script = dt =>
                {
                    float k = MathF.Sin(_t * 0.5f);
                    _c.Alert = k > -0.2f ? 1f : 0f;
                    float a = MathF.Sin(_t * 0.9f) * 1.5f;
                    _c.LookAt = _c.Position + new Vector3(-MathF.Sin(a), 0f, -MathF.Cos(a)) * 8f + Vector3.Up * MathF.Max(0.5f, _s.BodyHeight);
                    _c.Desired = Vector3.Zero;
                };
                break;
            case Feed:
                _script = dt => { _c.Desired = Vector3.Zero; _c.WantGraze = Mathf.PosMod(_t, 5f) > 1f ? 1f : 0f; _c.SetState(CState.Graze); };
                break;
            case Rest: StartRest(); break;
            case Hide:
                _script = dt => { _c.Desired = Vector3.Zero; _c.WantCrouch = 1f; _c.WantHidden = Mathf.PosMod(_t, 9f) < 7f ? 1f : 0f; };
                break;
            case Root:
                _script = dt => { _c.Desired = Vector3.Zero; _c.SetState(CState.Root); _c.WantCrouch = Mathf.PosMod(_t, 8f) < 5f ? 0.7f : 0f; };
                break;
            case Hover:
                _script = dt => { _c.Desired = Vector3.Zero; _c.Altitude = 2.2f; };
                break;
            case Land: StartLand(); break;
            case Drift: _script = dt => { _c.Altitude = 4f; Circle(MathF.Max(0.6f, walk * 0.6f)); }; break;
            case Descend: _script = dt => { _c.Altitude = 1.6f; Circle(MathF.Max(0.6f, walk * 0.5f)); }; break;
            case Warn:
                _script = dt =>
                {
                    _c.Desired = Vector3.Zero;
                    _target.Position = _c.Position + _c.Forward * (4f + _s.Body.Size);
                    _markerOn = true;
                    _c.FacePoint = _target.Position;
                    _c.SetState(CState.Warn);
                    float k = Mathf.PosMod(_t, 5f);
                    _c.Rear = Math.Clamp(k / 1.5f, 0f, 1f) * (k < 3.4f ? 1f : 0f);
                };
                break;
            case Tell:
                _script = dt =>
                {
                    float k = Mathf.PosMod(_t, 6f);
                    _c.Desired = Vector3.Zero;
                    _c.SetState(CState.Tell);
                    _c.WantCrouch = k < 4f ? 0.8f : 0f;
                    _c.Alert = 1f;
                    _target.Position = _c.Position + _c.Forward * 5f;
                    _c.FacePoint = _target.Position;
                    _markerOn = true;
                };
                break;
            case First: _script = Strike(true); break;
            case Follow: _script = Strike(false); break;
            case Drop: _script = DropScript(); break;
            case Cling: _script = ClimbTo(false); break;
            case Ceiling: _script = ClimbTo(true); break;
            case Hurt:
                _script = dt =>
                {
                    _c.Desired = Vector3.Zero;
                    if (_t - _lastHurt > 2.2f && !_c.Dead) { _lastHurt = _t; _sim.Damage(_c, _c.Stats.MaxHealth * 0.12f, null); if (_c.Health < _c.Stats.MaxHealth * 0.4f) _c.Health = _c.Stats.MaxHealth; }
                };
                break;
            case Death:
                _loop = 9f;
                _script = dt =>
                {
                    _c.Desired = Vector3.Zero;
                    if (_t > 1.2f && !_c.Dead) _sim.Kill(_c, null);
                };
                break;
            case Thrown:
                _loop = 7f;
                _script = dt =>
                {
                    if (_t > 0.8f && _t - dt <= 0.8f) _sim.Throw(_c, new Vector3(0f, 9f, -3f));
                };
                break;
            case Flee: StartFlee(run); break;
            case Wander: _manual = false; _script = dt => { }; break;
            case Stranger: StartStranger(); break;
            case Hunt: StartHunt(); break;
        }
        if (_room && _s.Climbs == false) _c.Position = _c.Position; // (walkers in a room stay on the floor)
    }

    // ---- Scripts -----------------------------------------------------------------------------------

    /// <summary>Keeps the creature walking a ring round the middle: the sim's own steering does the turning.</summary>
    void Circle(float speed)
    {
        Vector3 p = WildSim.Flat(_c.Position);
        float radius = MathF.Max(_room ? 2.5f : 5f, _s.Body.Size * 2.2f + speed * 1.3f);
        if (_room) radius = MathF.Min(radius, RoomHalf(_s) * 0.55f);
        float r = p.Length();
        Vector3 radial = r > 0.05f ? p / r : Vector3.Right;
        Vector3 tangent = new(-radial.Z, 0f, radial.X);
        float err = (r - radius) / MathF.Max(radius, 1f);
        Vector3 dir = (tangent - radial * err * 1.6f).Normalized();
        _c.SetState(CState.Wander);
        if (_room && _s.Climbs)
        {
            // A climber on the floor: the same ring, in 3D along the surface it holds.
            Vector3 n = _c.Facing.Y;
            dir -= n * dir.Dot(n);
            if (dir.LengthSquared() < 1e-4f) dir = tangent;
            dir = dir.Normalized();
        }
        _c.Desired = dir * speed;
        if (_s.Flies && _s.Rig == RigKind.Winged) _c.Altitude = MathF.Max(_c.Altitude, 3f);
    }

    void StartRest()
    {
        if (_s.Flies && _s.Rig == RigKind.Winged) Land_();
        _script = dt =>
        {
            _c.Desired = Vector3.Zero;
            _c.SetState(CState.Rest);
            _c.Alert = 0f;
            _c.WantCrouch = 1f;
            _c.WantHidden = _s.RestKind switch { RestKind.Burrow => _s.Burrows ? 1f : 0.6f, RestKind.Den => 0.25f, _ => 0f };
            if (_s.Flies) _c.Altitude = _s.Rig == RigKind.Winged ? 0f : 1.4f;
        };
    }

    void Land_()
    {
        _c.Position = new Vector3(0f, 0f, 0f);
        _c.Altitude = 0f;
        _c.Landed = true;
        _c.Velocity = Vector3.Zero;
        _c.LandedAt = _sim.Time;
        _c.Rig.Reset(_sim, _c);
    }

    void StartLand()
    {
        Land_();
        _script = dt =>
        {
            _c.Desired = Vector3.Zero;
            _c.Altitude = 0f;
            _c.WantCrouch = 0.2f;
            // Now and then it hops up and looks round.
            _c.LookAt = _c.Position + new Vector3(MathF.Sin(_t * 0.7f) * 6f, 1f, -6f);
        };
    }

    /// <summary>A real attack on a post a body-length away: the first needs a stranger (full telegraph), the second the same one again (short wind-up).</summary>
    Action<float> Strike(bool first)
    {
        float wait = 0f;
        ITarget prey = null;
        return dt =>
        {
            Vector3 fwd = _c.Space != null ? WildSim.Flat(-_c.Facing.Z).Normalized() : _c.Forward;
            if (_c.Attacking)
            {
                _c.Desired = Vector3.Zero;
                return;
            }
            // Hold the post where it will be reached, in front.
            float gap = MathF.Max(1.2f, Attacks.Range(_c) * 0.92f);
            if (!first && prey != null && WildSim.Flat(_target.Position - _c.Position).Length() > gap * 2f) wait = 0f;
            wait -= dt;
            if (wait > 0f) { _c.Desired = Vector3.Zero; _c.FacePoint = _target.Position; return; }
            if (first || prey == null)
            {
                _target.Position = _c.Position + fwd * gap;
            }
            else _target.Position = _c.Position + fwd * gap;
            _markerOn = true;
            _c.FacePoint = _target.Position;
            _c.Desired = Vector3.Zero;
            Vector3 toward = WildSim.Flat(_target.Position - _c.Position).Normalized();
            if (_c.Forward.Dot(toward) < 0.9f && _c.Space == null) return;
            if (first)
            {
                // A new post each time: a stranger, so the long, telegraphed wind-up.
                prey = _target;
                _c.RememberAttack(_target, -999f);
                _c.Rear = 0f;
            }
            else if (prey == null)
            {
                // The quick follow-up: it has just attacked this one.
                prey = _target;
                _c.RememberAttack(_target, _sim.Time);
            }
            if (Attacks.Start(_sim, _c, _target)) wait = first ? 2.2f : 0.9f;
        };
    }

    Action<float> DropScript()
    {
        float wait = 0f;
        return dt =>
        {
            if (_c.Attacking || _c.Falling) { _markerOn = true; return; }
            wait -= dt;
            if (wait > 0f) return;
            // Up on the ceiling above a post, then let go.
            int h = RoomHeight(_s);
            Vector3 above = new(1.5f, h - 0.4f, 0f);
            Crawl.Place(_sim, _c, above, Vector3.Up);
            _c.Home = _c.Position;
            _target.Position = new Vector3(1.5f, 0f, 0f);
            _markerOn = true;
            _c.RememberAttack(_target, -999f);
            _c.Desired = Vector3.Zero;
            if (Attacks.Start(_sim, _c, _target)) wait = 6f;
        };
    }

    /// <summary>A climber walks from the floor to a point on a wall, or on to the ceiling, holds there a while, and comes back the same way.</summary>
    Action<float> ClimbTo(bool ceiling)
    {
        int w = RoomHalf(_s), h = RoomHeight(_s);
        Vector3[] pts = ceiling
            ? new[] { new Vector3(w * 0.55f, 0.02f, 0.3f), new Vector3(w - 0.02f, h * 0.5f, 0.3f), new Vector3(w * 0.35f, h - 0.02f, 0.3f) }
            : new[] { new Vector3(w * 0.55f, 0.02f, 0.3f), new Vector3(w - 0.02f, h * 0.4f, 0.3f), new Vector3(w - 0.02f, h * 0.7f, -w * 0.4f) };
        int i = 0, dir = 1;
        float dwell = 0f;
        return dt =>
        {
            Vector3 centre = Crawl.Center(_c);
            Vector3 goal = pts[i];
            _c.SetState(CState.Wander);
            if ((goal - centre).Length() < _c.Clearance + 0.4f)
            {
                _c.Desired = Vector3.Zero;
                dwell += dt;
                if (dwell > 2.5f)
                {
                    dwell = 0f;
                    if (i + dir < 0 || i + dir >= pts.Length) dir = -dir;
                    i += dir;
                }
                return;
            }
            _c.Desired = (goal - centre).Normalized() * MathF.Max(0.4f, _s.Stats.WalkSpeed);
        };
    }

    void StartFlee(float run)
    {
        _manual = false;
        _c.Home = Vector3.Zero;
        float turnAt = 0f;
        _script = dt =>
        {
            // Frightened by something that keeps circling it: the flight bends round, so it never leaves the screen.
            turnAt += dt * 0.55f;
            Vector3 heading = new(-MathF.Sin(turnAt), 0f, -MathF.Cos(turnAt));
            _sim.Scare(_c, _c.Position - heading * 10f, 3f);
            _c.ThinkEvery = 0.1f;
        };
    }

    void StartStranger()
    {
        _manual = false;
        _c.Home = Vector3.Zero;
        _loop = 0f;
        float tumbleLook = 0f;
        _script = dt =>
        {
            // A stranger walks up from far off, stands, and goes away again.
            float k = Mathf.PosMod(_t, 36f);
            float dist = k < 10f ? Mathf.Lerp(34f, 7f, k / 10f) : k < 20f ? 7f : k < 30f ? Mathf.Lerp(7f, 34f, (k - 20f) / 10f) : 34f;
            Vector3 dir = new(0f, 0f, -1f);
            _target.Position = WildSim.Flat(_c.Home) + dir * dist;
            _target.Forward = k < 20f ? -dir : dir;
            if (_s.Tumbles) { tumbleLook += dt; _target.Forward = Mathf.PosMod(_t, 10f) < 5f ? -dir : Vector3.Right; }
            _markerOn = true;
        };
        _target.Position = new Vector3(0f, 0f, -34f);
    }

    void StartHunt()
    {
        _manual = false;
        _c.Home = Vector3.Zero;
        _script = dt =>
        {
            // A post a few strides off, which it goes for: wound or not, it has reason to.
            float k = Mathf.PosMod(_t, 30f);
            _target.Position = _c.Position + (_c.Position.LengthSquared() < 1f ? new Vector3(0f, 0f, -12f) : (_target.Position - _c.Position).LengthSquared() > 1f ? _target.Position - _c.Position : new Vector3(0f, 0f, -12f));
            if (k < 0.1f || !_markerOn) _target.Position = new Vector3(_c.Position.X, 0f, _c.Position.Z - 14f);
            _markerOn = true;
            if (_c.Stats.Retaliates && _sim.Time - _c.ProvokedAt > 8f) { _c.ProvokedBy = _target; _c.ProvokedAt = _sim.Time; }
        };
    }
}

sealed partial class CreatureStage : IStageProbe
{
    public Creature Creature => _c;
    public Species Species => _s;
    public Node3D Subject => _view;
    public Vector3 Anchor => _c.Position + _world.Position;
    public Vector3? GroundShift => _world.Position;

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
        Transform3D[] pose = _c.Rig.Pose;
        float low = float.MaxValue;
        for (int i = 0; i < pose.Length; i++)
        {
            Transform3D t = pose[i];
            if (!float.IsFinite(t.Origin.X) || !float.IsFinite(t.Origin.Y) || !float.IsFinite(t.Origin.Z) || !float.IsFinite(t.Basis.X.X) || !float.IsFinite(t.Basis.Y.Y) || !float.IsFinite(t.Basis.Z.Z))
                return $"piece {_c.Rig.Layout.Pieces[i].Name} has a non-finite pose";
            if (CountsForGround(_c.Rig.Layout.Pieces[i].Name) && (t.Basis.Determinant() > 1e-9f || t.Basis.Determinant() < -1e-9f)) low = MathF.Min(low, t.Origin.Y);
        }
        if (!float.IsFinite(_c.Position.X) || !float.IsFinite(_c.Position.Y) || !float.IsFinite(_c.Position.Z)) return "creature position is not finite";
        if (_room || _c.Thrown || _c.Dead && _c.Downed > 0f || _c.Species.InSpace) return "";
        float ground = 0f;
        bool buried = _c.WantHidden > 0.3f || _c.Rig is ChainRig && _c.State == CState.Hidden;
        if (!buried && !_c.Dead && low < ground - 0.22f - 0.05f * _s.BodyLength) return $"sunk: lowest piece {low:0.00} m under the ground";
        bool air = _s.Flies && !_c.Landed;
        if (!air && !buried && _c.Hop == HopPhase.Ground && !_c.Thrown && _s.Rig != RigKind.Swarm && low > ground + 0.5f + 0.4f * _s.BodyHeight) return $"floating: lowest piece {low:0.00} m up";
        return "";
    }

    public long PoseSignature()
    {
        long h = 1469598103934665603L;
        foreach (Transform3D t in _c.Rig.Pose)
        {
            h = (h ^ (long)MathF.Round((t.Origin.X - _c.Position.X) * 20f)) * 1099511628211L;
            h = (h ^ (long)MathF.Round((t.Origin.Y - _c.Position.Y) * 20f)) * 1099511628211L;
            h = (h ^ (long)MathF.Round((t.Origin.Z - _c.Position.Z) * 20f)) * 1099511628211L;
        }
        return h;
    }
}

sealed partial class CreatureStage
{
    /// <summary>Heads (they go down to feed), jaws and tails, tendrils and wings reach where the body doesn't: only the rest says whether it stands on the ground.</summary>
    static bool CountsForGround(string piece)
    {
        string n = piece.ToLowerInvariant();
        foreach (string skip in new[] { "head", "jaw", "neck", "tail", "tend", "horn", "wing", "ant", "eye", "limb", "feeler", "tooth", "claw", "scythe", "ear", "crest", "tusk" })
            if (n.StartsWith(skip, StringComparison.Ordinal)) return false;
        return true;
    }
}
