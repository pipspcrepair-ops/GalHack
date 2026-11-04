using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>
/// Builds the playable planet in code: terrain, groves, grass and rocks; the fauna of the
/// current world; the player with a blaster; and the harvester hovercraft. Keys 1-3 switch the
/// world between temperate, alien and Bloom-infested, which rolls new fauna and restyles the
/// forest. Everything runs in _PhysicsProcess (physics interpolation smooths it on screen).
/// </summary>
public partial class Main : Node3D
{
    public WildlifeData Data { get; private set; }
    public FaunaGenerator Generator { get; private set; }
    public Planet Planet { get; private set; }
    public Forest Forest { get; private set; }
    public Grass Grass { get; private set; }
    public WildSim Sim { get; private set; }
    public WildlifeView View { get; private set; }
    public PlanetFauna Fauna { get; private set; }
    public Player Player { get; private set; }
    public Vessel Vessel { get; private set; }
    public Camera3D Camera { get; private set; }
    public Hud Hud { get; private set; }
    public ProceduralAudio Audio { get; private set; }
    /// <summary>Sightings, recorded silently: nothing is ever announced when you first meet a creature.</summary>
    public readonly Codex Codex = new();
    public WorldKind World { get; private set; } = WorldKind.Alien;
    /// <summary>Screenshots: drive the camera by hand instead of from the player.</summary>
    public bool FreeCamera;

    static readonly Vector3 Start = new(0f, 0f, 22f), VesselSpot = new(7f, 0f, 12f);
    const float GunCooldown = 0.2f, GunDamage = 16f, GunRange = 140f;

    float _gunTimer, _sightTimer, _downedTimer;
    float _orbitYaw, _orbitPitch = 0.25f;
    readonly List<(MeshInstance3D mesh, float life)> _tracers = new();
    OmniLight3D _flash;
    float _flashTime;

    public override void _Ready()
    {
        Data = DataFiles.LoadWildlife();
        Generator = new FaunaGenerator(Data.Fauna);

        Planet = new Planet { Name = "Planet" };
        AddChild(Planet);
        Planet.Build();
        Forest = new Forest { Name = "Forest" };
        AddChild(Forest);
        Forest.Build(Planet.Terrain, Planet.Nav, Data.Harvest);
        Grass = new Grass { Name = "Grass" };
        AddChild(Grass);
        Grass.Build(Planet.Terrain, Planet.Nav);
        View = new WildlifeView { Name = "Wildlife" };
        AddChild(View);
        Audio = new ProceduralAudio { Name = "Audio" };
        AddChild(Audio);

        Camera = new Camera3D { Name = "Camera", Fov = 78f, Near = 0.05f, Far = 6000f };
        AddChild(Camera);
        Camera.MakeCurrent();
        Player = new Player { Name = "Player", Camera = Camera };
        AddChild(Player);
        Camera.AddChild(Player.Muzzle);
        Player.Teleport(Ground(Start) + Vector3.Up * 0.1f);
        _flash = new OmniLight3D { LightColor = new Color("9ff6ff"), OmniRange = 6f, LightEnergy = 0f, Position = new Vector3(0.3f, -0.2f, -1f) };
        Camera.AddChild(_flash);

        Vessel = new Vessel { Name = "Hovercraft" };
        Vessel.Setup(Planet.Terrain, Planet.Nav, Forest.Field, null, Data.Harvest, Audio);
        AddChild(Vessel);
        Vessel.GlobalPosition = Ground(VesselSpot) + Vector3.Up * 0.85f;
        Vessel.Yaw = 0.5f;
        Vessel.Driver = Player.Target;
        Player.Vehicle = Vessel;

        Hud = new Hud { Name = "Hud" };
        AddChild(Hud);

        SwitchWorld(World);
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    // ---- Worlds -----------------------------------------------------------------------

    public void SwitchWorld(WorldKind kind)
    {
        World = kind;
        WorldStyle style = WorldStyle.For(kind);
        float time = Sim?.Clock.Time01 ?? 0.36f;

        Fauna = Generator.Generate(style.FaunaSeed, style.Alienness, style.Bloom);
        Sim = new WildSim(Data, Planet.Terrain, Planet.Nav);
        Sim.Clock.Time01 = time;
        Sim.Targets.Add(Player.Target);
        Sim.Telegraph += e => Audio.Telegraph(e.Creature);
        Sim.Warned += c => Audio.Telegraph(c);
        Sim.Attacked += OnAttacked;
        View.Bind(Sim, null);
        Vessel.Rebind(Sim);

        Planet.ApplyStyle(style);
        Forest.ApplyStyle(style);
        Grass.ApplyStyle(style);
        Vessel.SetChipColours(style.Trees == TreeStyle.Temperate ? new Color("e3c48a") : style.Trees == TreeStyle.Alien ? new Color("ff9fd0") : new Color("ff3cac"),
            style.Trees == TreeStyle.Bloom ? new Color("2a1426") : new Color("6b4a2e"));
        Populate(style);
        GD.Print($"{Fauna.PlanetName} ({style.Label}): {Fauna.Species.Count} species, {Sim.Creatures.Count} creatures");
        foreach (Species s in Fauna.Species) GD.Print($"  {s}");
    }

    /// <summary>Puts each species where its behaviour belongs: herds on the plain ahead, giants far off, ambushers by the groves.</summary>
    void Populate(WorldStyle style)
    {
        var rng = Rng.For(style.FaunaSeed, "population", style.Bloom ? 1 : 0);
        foreach (Species s in Fauna.Species)
        {
            int groups = s.Plan == BodyPlan.Giant ? 2 : s.Behaviour == Behaviour.Herd || s.Rig == RigKind.Swarm ? 2 : 3;
            for (int g = 0; g < groups; g++)
            {
                int count = rng.Int(s.GroupMin, s.GroupMax);
                Vector3 at;
                if (s.Behaviour == Behaviour.Herd && g == 0) at = new Vector3(rng.Range(-15f, 15f), 0f, rng.Range(-30f, -15f));
                else if (s.Behaviour == Behaviour.Ambusher) at = Forest.Groves[rng.Int(0, Forest.Groves.Count - 1)] + Ring(rng, 14f, 24f);
                else if (s.Plan == BodyPlan.Giant) at = g == 0 ? new Vector3(rng.Range(30f, 60f), 0f, rng.Range(-110f, -80f)) : Ring(rng, 90f, 180f);
                else if (s.Behaviour == Behaviour.Predator) at = Ring(rng, 50f, 120f);
                else at = Ring(rng, g == 0 ? 25f : 40f, 170f);
                Sim.SpawnGroup(s, Sim.FreeSpot(at, rng), Math.Max(1, count));
            }
        }
    }

    static Vector3 Ring(Rng rng, float min, float max)
    {
        float a = rng.Range(0f, MathF.Tau), r = rng.Range(min, max);
        return new Vector3(MathF.Cos(a) * r, 0f, MathF.Sin(a) * r);
    }

    // ---- Input ------------------------------------------------------------------------

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            if (Player.Driving)
            {
                _orbitYaw -= motion.Relative.X * 0.003f;
                _orbitPitch = Math.Clamp(_orbitPitch + motion.Relative.Y * 0.002f, -0.1f, 0.9f);
            }
            else Player.HandleMouse(motion.Relative);
        }
        else if (e is InputEventMouseButton { Pressed: true } button)
        {
            if (Input.MouseMode != Input.MouseModeEnum.Captured) Input.MouseMode = Input.MouseModeEnum.Captured;
            else if (button.ButtonIndex == MouseButton.Left && !Player.Driving) Fire();
        }
        else if (e is InputEventKey { Pressed: true, Echo: false } key)
        {
            switch (key.PhysicalKeycode)
            {
                case Key.Key1: SwitchWorld(WorldKind.Temperate); break;
                case Key.Key2: SwitchWorld(WorldKind.Alien); break;
                case Key.Key3: SwitchWorld(WorldKind.Bloom); break;
                case Key.N: Sim.Clock.Time01 = (Sim.Clock.Time01 + 0.25f) % 1f; break;
                case Key.F: ToggleVessel(); break;
                case Key.E: Butcher(); break;
                case Key.Escape: Input.MouseMode = Input.MouseModeEnum.Visible; break;
            }
        }
    }

    void ToggleVessel()
    {
        if (Player.Driving)
        {
            Player.Driving = false;
            Vessel.Driven = false;
            Player.CollisionMask = Layers.World | Layers.Vessel;
            Vector3 left = new Basis(Vector3.Up, Vessel.Yaw) * new Vector3(-3.2f, 0f, 0f);
            Player.Teleport(Ground(Vessel.GlobalPosition + left) + Vector3.Up * 0.2f);
            Player.SetLook(Vessel.Yaw, -0.1f);
        }
        else if (WildSim.Flat(Vessel.GlobalPosition - Player.GlobalPosition).Length() < 5f && !Player.Health.Downed)
        {
            Player.Driving = true;
            Vessel.Driven = true;
            Player.CollisionMask = 0;
            _orbitYaw = 0f;
            // Whatever you were carrying goes into the hold.
            foreach (var (item, n) in new Dictionary<string, int>(Player.Pack.Items)) Vessel.Storage.Add(item, n);
            Player.Pack.Clear();
        }
    }

    // ---- Frame ------------------------------------------------------------------------

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (Player.Driving)
        {
            Vector2 move = new Vector2(Axis(Key.D, Key.A), Axis(Key.S, Key.W));
            Vessel.SetInput(move, Held(Key.Shift));
            Player.GlobalPosition = Vessel.GlobalPosition + Vector3.Up * 1.2f;
        }
        else if (!Player.Health.Downed) Player.Step(dt);

        Sim.Step(dt);
        var players = new List<Vector3>(1) { Player.Driving ? Vessel.GlobalPosition : Player.GlobalPosition };
        Forest.Field.Update(dt, players);
        Forest.Sync();
        Planet.UpdateSky(Sim.Clock);
        if (!FreeCamera)
        {
            if (Player.Driving) ChaseCamera(dt);
            else Player.PlaceCamera();
        }
        View.Sync(Camera.GlobalPosition);

        _gunTimer -= dt;
        UpdateEffects(dt);
        RecordSightings(dt);
        Respawn(dt);
        UpdateHud();
    }

    void ChaseCamera(float dt)
    {
        Basis orbit = new Basis(Vector3.Up, Vessel.Yaw + _orbitYaw) * new Basis(Vector3.Right, -_orbitPitch);
        Vector3 focus = Vessel.GlobalPosition + Vector3.Up * 1.4f;
        Vector3 want = focus + orbit * new Vector3(0f, 1.2f, 11f);
        want.Y = MathF.Max(want.Y, Planet.Terrain.Height(want.X, want.Z) + 1f);
        Camera.GlobalPosition = Camera.GlobalPosition.Lerp(want, 1f - MathF.Exp(-8f * dt));
        Camera.LookAt(focus + Vessel.Forward * 3f, Vector3.Up);
    }

    void Respawn(float dt)
    {
        if (!Player.Health.Downed) return;
        _downedTimer += dt;
        if (_downedTimer < 3f) return;
        _downedTimer = 0f;
        Player.Health.Restore();
        Vector3 left = new Basis(Vector3.Up, Vessel.Yaw) * new Vector3(-3.2f, 0f, 0f);
        Player.Teleport(Ground(Vessel.GlobalPosition + left) + Vector3.Up * 0.2f);
    }

    void OnAttacked(AttackEvent e)
    {
        if (e.Target != Player.Target) return;
        if (e.Hit)
        {
            Hud.Hurt(e.Damage);
            Audio.Play("thud", Player.Eye, 1f, 2f);
        }
        else Audio.Play("snap", e.Creature.Position + Vector3.Up, 1.2f, -4f);
    }

    // ---- The blaster --------------------------------------------------------------------

    void Fire()
    {
        if (_gunTimer > 0f || Player.Health.Downed) return;
        _gunTimer = GunCooldown;
        Vector3 origin = Camera.GlobalPosition, dir = -Camera.GlobalBasis.Z;
        float best = GunRange;
        Vector3 hitPoint = origin + dir * GunRange;

        var query = PhysicsRayQueryParameters3D.Create(origin, origin + dir * GunRange, Layers.World | Layers.Vessel, new Godot.Collections.Array<Rid> { Player.GetRid() });
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count > 0)
        {
            hitPoint = (Vector3)hit["position"];
            best = origin.DistanceTo(hitPoint);
        }
        Creature c = Sim.Raycast(origin, dir, best, out float dist, out Vector3 point);
        if (c != null)
        {
            hitPoint = point;
            Sim.Damage(c, GunDamage, Player.Target);
        }
        Player.Recoil = 1f;
        _flashTime = 0.06f;
        Audio.Play("shot", origin, 1f + (float)GD.RandRange(-0.05, 0.05), -4f);
        Tracer(Player.Muzzle.GlobalPosition + -Camera.GlobalBasis.Z * 0.4f, hitPoint, c != null);
    }

    void Tracer(Vector3 from, Vector3 to, bool flesh)
    {
        float len = from.DistanceTo(to);
        if (len < 0.1f) return;
        var m = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.035f, 0.035f, len) },
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = flesh ? new Color("ffb03b") : new Color("9ff6ff"),
                EmissionEnabled = true,
                Emission = flesh ? new Color("ffb03b") : new Color("9ff6ff"),
                EmissionEnergyMultiplier = 3f,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(m);
        m.GlobalPosition = (from + to) * 0.5f;
        m.LookAt(to, Mathf.Abs((to - from).Normalized().Y) > 0.98f ? Vector3.Right : Vector3.Up);
        _tracers.Add((m, 0.07f));
    }

    void UpdateEffects(float dt)
    {
        for (int i = _tracers.Count - 1; i >= 0; i--)
        {
            var (m, life) = _tracers[i];
            life -= dt;
            if (life <= 0f)
            {
                m.QueueFree();
                _tracers.RemoveAt(i);
            }
            else _tracers[i] = (m, life);
        }
        _flashTime -= dt;
        _flash.LightEnergy = _flashTime > 0f ? 3f : 0f;
    }

    /// <summary>E next to a kill: its meat (and maybe a rare part) goes into your pack.</summary>
    void Butcher()
    {
        Creature corpse = NearestCorpse(3.5f);
        if (corpse == null) return;
        int meat = (int)MathF.Ceiling(corpse.MeatLeft);
        int taken = Player.Pack.Add(Items.Meat, meat);
        var rng = Rng.For(corpse.Species.Seed, "drop", corpse.Id);
        if (corpse.Species.Drops.RarePart != "" && rng.Chance(corpse.Species.Drops.RareChance)) Player.Pack.Add(corpse.Species.Drops.RarePart, 1);
        corpse.MeatLeft -= taken;
        if (corpse.MeatLeft <= 0f) corpse.Removed = true;
        Audio.Play("chop", corpse.Position, 0.8f);
    }

    Creature NearestCorpse(float radius)
    {
        Creature best = null;
        float bestD = radius;
        foreach (Creature c in Sim.Creatures)
        {
            if (!c.Dead || c.Removed || c.MeatLeft <= 0f) continue;
            float d = WildSim.Flat(c.Position - Player.GlobalPosition).Length();
            if (d < bestD) { bestD = d; best = c; }
        }
        return best;
    }

    // ---- Codex, scan and HUD -----------------------------------------------------------

    /// <summary>
    /// A species goes in the codex when you get a good look at one: close by, or near the middle
    /// of the view. Silently: first meetings are never announced. Its name is there for the scan.
    /// </summary>
    void RecordSightings(float dt)
    {
        _sightTimer -= dt;
        if (_sightTimer > 0f) return;
        _sightTimer = 0.25f;
        Vector3 eye = Camera.GlobalPosition, look = -Camera.GlobalBasis.Z;
        foreach (Creature c in Sim.Creatures)
        {
            if (c.Removed || c.Dead || Codex.Knows(c.Species.Id)) continue;
            if (c.Species.Behaviour == Behaviour.Ambusher && c.WantHidden > 0.5f) continue; // you can't spot what's buried
            Vector3 to = c.Rig.BoundsCenter - eye;
            float d = to.Length();
            float reach = 30f + c.Species.Body.Size * 6f;
            if (d < 8f || (d < reach && look.Dot(to / d) > 0.93f))
                Codex.Record(c.Species, Player.Target.Name, Sim.Time, Fauna.PlanetName);
        }
    }

    /// <summary>Hold Q on a creature to scan it: its name and what it is, only when you ask.</summary>
    string Scan()
    {
        if (Player.Driving || !Held(Key.Q)) return "";
        Vector3 eye = Camera.GlobalPosition, dir = -Camera.GlobalBasis.Z;
        Creature c = Sim.Raycast(eye, dir, 80f, out float dist, out _);
        if (c == null) return "scanning...";
        Species s = c.Species;
        string what = s.Behaviour switch
        {
            Behaviour.Herd => "herd grazer", Behaviour.TerritorialGiant => "territorial", Behaviour.HarmlessOddity => "harmless",
            _ => s.Behaviour.ToString().ToLowerInvariant(),
        };
        return $"{s.Name}, {s.CommonName}\n{what}, {s.Temperament.ToString().ToLowerInvariant()}   {s.Body.Size:F1} m   {dist:F0} m away";
    }

    void UpdateHud()
    {
        WorldStyle style = Planet.Style;
        string where = Player.Driving
            ? $"Driving the harvester   {Vessel.Velocity.Length():F0} m/s{(Vessel.CuttingFor > 0f ? "   blades cutting" : "")}"
            : Player.Health.Downed ? "You were brought down. Back at the hovercraft in a moment." : $"On foot   health {Player.Health.Current:F0}/{Player.Health.Max:F0}";
        int alive = 0;
        foreach (Creature c in Sim.Creatures) if (!c.Removed && c.Alive) alive++;
        Hud.SetStatus(
            $"{Fauna.PlanetName}, a {style.Label}   {Sim.Clock.Label}\n" +
            $"{where}\n" +
            $"{alive} creatures about\n\n" +
            "1 temperate  2 alien  3 Bloom   N skip time\n" +
            "WASD move  Shift sprint  Space jump  Ctrl crouch  Mouse look  LMB shoot  Q scan\n" +
            "E butcher a kill  F hovercraft  Esc free the mouse\n" +
            $"{Engine.GetFramesPerSecond()} fps");
        Hud.SetHold(Vessel.Storage, Player.Pack, Player.Driving);
        string hint = "";
        if (Player.Driving && Vessel.LastTick.Refused > 0) hint = "Hold too full for that: the blades are idling";
        else if (!Player.Driving && !Player.Health.Downed)
        {
            if (WildSim.Flat(Vessel.GlobalPosition - Player.GlobalPosition).Length() < 5f) hint = "F  drive the harvester";
            else if (NearestCorpse(3.5f) is { } corpse) hint = $"E  butcher ({MathF.Ceiling(corpse.MeatLeft):F0} meat)";
        }
        Hud.SetHint(hint);
        Hud.SetScan(Scan());
    }

    Vector3 Ground(Vector3 p) => new(p.X, Planet.Terrain.Height(p.X, p.Z), p.Z);
    static bool Held(Key key) => Input.IsPhysicalKeyPressed(key);
    static float Axis(Key positive, Key negative) => (Held(positive) ? 1f : 0f) - (Held(negative) ? 1f : 0f);
}
