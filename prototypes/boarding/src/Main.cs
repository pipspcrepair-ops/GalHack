using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Boarding;

/// <summary>
/// Builds the prototype in code and wires the rules together: a landed Vharn Dominion
/// picket frigate on a rust-red world, its crew, the player at the landing site, and the
/// alarm, hijack and noise systems that connect them.
///
/// Every system is a small class of its own; this is the only place that knows how they
/// talk to each other (for example: a finished hijack stage raises the alarm and fires the
/// core's next defensive response).
/// </summary>
public partial class Main : Node3D
{
    /// <summary>Tests can hand in their own tunables before the node enters the tree.</summary>
    public Tunables TunablesOverride;

    public Tunables Tunables { get; private set; }
    public ShipLayout Layout { get; private set; }
    public List<WeaponDef> WeaponDefs { get; private set; }

    public AlarmSystem Alarm { get; private set; }
    public HijackSystem Hijack { get; private set; }
    public NoiseSystem Noise { get; private set; }
    public Inventory Inventory { get; private set; }

    public Ship Ship { get; private set; }
    public Player Player { get; private set; }
    public Effects Effects { get; private set; }
    public Hud Hud { get; private set; }
    public Camera3D Camera { get; private set; }

    public readonly List<Guard> Guards = new();
    public readonly HashSet<Vector2I> CoverClaims = new();
    readonly List<Pickup> _pickups = new();

    public Vector3 SpawnPosition { get; private set; }
    public Vector3 SpawnFacing { get; private set; }
    public bool Headless { get; private set; }
    public bool Exploded { get; private set; }
    public int WavesSpawned { get; private set; }

    /// <summary>For tests: guards stop thinking and moving.</summary>
    public bool FreezeGuards;
    /// <summary>For tests: guards and the turret can't see the player.</summary>
    public bool PlayerHidden;

    /// <summary>The ship stands at an angle on purpose: nothing aboard may assume the ship's frame is the world's.</summary>
    const float ShipYawDegrees = 18f;

    public override void _Ready()
    {
        Headless = DisplayServer.GetName() == "headless";
        Tunables = TunablesOverride ?? Tunables.Load();
        Layout = ShipLayout.Load();
        WeaponDefs = WeaponDef.LoadAll();

        Alarm = new AlarmSystem(Tunables);
        Hijack = new HijackSystem(Tunables);
        Noise = new NoiseSystem();
        Inventory = new Inventory();
        Inventory.Add(Inventory.Charge, Tunables.StartingCharges);

        World.BuildSky(this);
        Effects = new Effects { Name = "Effects" };
        AddChild(Effects);

        Ship = new Ship { Name = "Ship", Game = this, Layout = Layout, RotationDegrees = new Vector3(0, ShipYawDegrees, 0), Position = new Vector3(6, 0, -4) };
        AddChild(Ship);
        World.BuildGround(this, Ship);
        SpawnCrew();

        SpawnPosition = Ship.ToGlobal(Layout.CellCenter(Layout.PlayerSpawn)) + Vector3.Up * 0.05f;
        SpawnFacing = Ship.ToGlobal(Layout.CellCenter(Layout.PlayerFace)) + Vector3.Up * 1.4f;

        Camera = new Camera3D { Name = "Camera", Fov = 78f, Near = 0.03f, Far = 4000f };
        AddChild(Camera);
        Camera.MakeCurrent();
        Player = new Player { Name = "Player", Game = this, Camera = Camera };
        AddChild(Player);
        Player.DebugPlace(SpawnPosition);
        Player.LookAt2(SpawnFacing);

        Hud = new Hud { Name = "Hud", Game = this };
        AddChild(Hud);

        WireRules();
        Hud.Message("Board the Vharn frigate. Hijack its core (hold E) or shoot it dead.");
        Hud.Message("Ways in: the stern airlock, the weak hull plate (G: charge), or crawl the port duct (Ctrl).");
    }

    void WireRules()
    {
        Noise.Emitted += noise =>
        {
            foreach (Guard guard in Guards.ToList()) guard.OnNoise(noise);
            // A charge going off on or near the hull is a loud breach whoever hears it.
            if (noise.Kind == NoiseKind.Explosion && Ship.ToLocal(noise.Position).Length() < 60f)
                Alarm.ReportLoudNoise(Ship.ToLocal(noise.Position));
        };
        Alarm.Converge += local =>
        {
            foreach (Guard guard in Guards) guard.Converge(local);
        };
        Alarm.LevelChanged += (_, level) =>
        {
            switch (level)
            {
                case AlarmLevel.Alert: Hud.Message("ALARM 1: the crew is alert and converging."); break;
                case AlarmLevel.Lockdown:
                    Ship.SealBulkheads();
                    Ship.SetLights(LightMode.Alarm);
                    Hud.Banner("LOCKDOWN - bulkheads sealed");
                    break;
                case AlarmLevel.LastStand:
                    Ship.SetLights(LightMode.Alarm);
                    Hud.Banner("LAST STAND - self-destruct armed. Finish the hijack or get clear!");
                    break;
            }
        };
        Alarm.SelfDestruct.Detonated += Explode;

        Hijack.Begun += () =>
        {
            Alarm.ReportHijackStarted();
            Hud.Message("Hijack Tool locked on. Hold E; taking fire stalls it.");
        };
        Hijack.StageCompleted += (stage, response) =>
        {
            Hud.Message($"Hijack stage {stage}/{Hijack.Stages} done.");
            Alarm.ReportHijackStage(stage);
            Respond(response);
        };
        Hijack.Finished += () =>
        {
            Ship.CaptureForPlayer();
            Alarm.StandDown();
            foreach (Door door in Ship.Doors) door.Unlock();   // the ship answers to you now
            Hud.Banner("THE SHIP IS YOURS - weapons and warp rebooting");
        };
        Ship.CoreDestroyed += () =>
        {
            Alarm.StandDown();
            Hud.Banner("CORE DESTROYED - the ship is derelict");
            Hud.Message("Its components are unlocked: remove the turret or generator whole (hold E).");
        };
    }

    /// <summary>The core's defensive response to a finished hijack stage (names come from data).</summary>
    void Respond(string response)
    {
        switch (response)
        {
            case "wave":
                SpawnWave();
                Hud.Banner("REINFORCEMENTS - the barracks lift is coming up");
                break;
            case "bulkhead":
                // Every bulkhead and security door slams shut behind you. You can always hack,
                // swipe or blow your way back out.
                Ship.SealBulkheads();
                foreach (Door door in Ship.Doors.Where(d => d.Kind == BlockKind.SecurityDoor).ToList()) door.Seal();
                Hud.Banner("BULKHEADS SEALED - you're locked in with the core");
                break;
            case "surge":
                Ship.SetLights(LightMode.Blackout);
                Player.Shake(0.8f);
                Effects.Sparks(Ship.Core.GlobalPosition + Vector3.Up * 1.5f, Vector3.Up, Palette.TealGlow, 40, 9f, 0.8f);
                Hud.Banner("POWER SURGE - lights out");
                break;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        Alarm.Tick(dt);
        Hijack.Tick(dt, Player.ChannellingCore && Ship.Hostile && Ship.CoreAlive);

        bool firefight = Player.IsInsideShip && Guards.Any(g => !g.Dead && g.State == GuardState.Combat);
        if (firefight) Alarm.ReportCombat(dt);

        foreach (Pickup pickup in _pickups.ToList())
        {
            if (pickup.GlobalPosition.DistanceTo(Player.GlobalPosition) > 1.4f) continue;
            Inventory.Add(pickup.Item);
            Hud.Message($"Picked up: {pickup.Item}. Security doors open silently with it.");
            pickup.Take();
            _pickups.Remove(pickup);
        }
    }

    // ---- Crew ----------------------------------------------------------------------------

    void SpawnCrew()
    {
        foreach (Vector2I cell in Layout.GuardSpawns) SpawnGuard(GuardRole.Trooper, cell);
        foreach (Vector2I cell in Layout.OfficerSpawns) SpawnGuard(GuardRole.Officer, cell);
        if (Layout.InBounds(Layout.GunnerStation))
        {
            Guard gunner = SpawnGuard(GuardRole.Gunner, Layout.GunnerStation);
            // He sits facing the hull and his turret controls.
            Vector3 toMount = Layout.CellCenter(Layout.TurretMount) - Layout.CellCenter(Layout.GunnerStation);
            gunner.Position = Layout.CellCenter(Layout.GunnerStation) - toMount.Normalized() * 0.2f;
            gunner.PostLocal = gunner.Position;
            gunner.Rotation = new Vector3(0, Mathf.Atan2(-toMount.X, -toMount.Z), 0);
            if (Ship.Turret != null) Ship.Turret.Gunner = gunner;
        }
    }

    public Guard SpawnGuard(GuardRole role, Vector2I cell)
    {
        var guard = new Guard
        {
            Name = $"{role}_{Guards.Count}",
            Game = this,
            Ship = Ship,
            Type = GuardType.For(role, Tunables),
            Position = Layout.CellCenter(cell) + Vector3.Up * 0.05f,
            Rotation = new Vector3(0, GD.Randf() * Mathf.Tau, 0),
        };
        Ship.AddChild(guard);
        Guards.Add(guard);
        return guard;
    }

    /// <summary>A reinforcement wave rides up the barracks lift and heads for the core.</summary>
    public void SpawnWave()
    {
        WavesSpawned++;
        var lifts = Layout.ReinforcementSpawns;
        if (lifts.Count == 0) return;
        Vector3 core = Layout.CellCenter(Layout.Core);
        for (int i = 0; i < Tunables.ReinforcementCount; i++)
        {
            GuardRole role = i == Tunables.ReinforcementCount - 1 ? GuardRole.Enforcer : GuardRole.Trooper;
            Guard guard = SpawnGuard(role, lifts[i % lifts.Count]);
            guard.Position += new Vector3((i / lifts.Count) * 0.8f, 0, 0);
            guard.Dispatch(core);
        }
    }

    public void OnGuardKilled(Guard guard)
    {
        if (guard.HasKeycard)
        {
            var pickup = new Pickup { Name = "Keycard" };
            Ship.AddChild(pickup);
            pickup.GlobalPosition = guard.GlobalPosition;
            _pickups.Add(pickup);
            Hud.Message("The officer dropped a keycard.");
        }
        if (guard.Type.Role == GuardRole.Gunner) Hud.Banner("GUNNER DOWN - the turret falls silent");
    }

    /// <summary>Ship-local feet of everyone who can open doors (the player and living guards).</summary>
    public void CollectActorFeet(List<Vector3> into)
    {
        if (Player != null) into.Add(Ship.ToLocal(Player.GlobalPosition));
        foreach (Guard guard in Guards)
            if (!guard.Dead) into.Add(guard.Position);
    }

    // ---- The end ---------------------------------------------------------------------------

    void Explode()
    {
        Exploded = true;
        Aabb b = Layout.Bounds;
        foreach (var corner in new[] { new Vector3(-0.3f, 0, -0.3f), new Vector3(0.3f, 0, 0.2f), new Vector3(0, 0, 0), new Vector3(-0.2f, 0, 0.35f), new Vector3(0.25f, 0, -0.35f) })
            Effects.Explosion(Ship.ToGlobal(b.GetCenter() + corner * b.Size + Vector3.Up * 4f), 9f, new Color("ff8c3a"));
        foreach (Guard guard in Guards.Where(g => !g.Dead).ToList()) guard.TakeHit(99999f, guard.GlobalPosition, Vector3.Up, false);
        Ship.Wreck();

        float distance = Ship.ToLocal(Player.GlobalPosition).Length();
        float radius = Tunables.SelfDestructBlastRadius;
        if (distance < radius) Player.TakeEnvironmentDamage(Player.Health.Max * 1.6f * (1f - distance / radius), canKill: true);
        Player.Shake(1.5f);
        Hud.Banner("THE FRIGATE SELF-DESTRUCTED");
    }
}
