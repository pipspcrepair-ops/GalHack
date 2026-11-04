using System;
using Godot;

namespace Wildlife;

/// <summary>
/// A hover vessel with harvester blades on the front: a chunky 1970s workhorse in cream,
/// orange and hazard checks. It hovers over the height function, slides off rocks, and its
/// blades chew through whatever is in front of them. Wood, plant matter and meat go straight
/// into its storage (one shared inventory with a capacity), and the chips fly up over the
/// cab into the hopper so you can see it filling.
/// </summary>
public partial class Vessel : Node3D
{
    public Storage Storage;
    public HarvesterBlades Blades;
    public bool Driven;
    /// <summary>Screenshots and tests: drive forward on its own at this throttle.</summary>
    public float AutoThrottle;
    public Vector3 Velocity;
    public float Yaw;
    public BladeTick LastTick;
    public float CuttingFor;

    const float HoverHeight = 0.85f, TopSpeed = 11f, BoostSpeed = 17f, CutSpeed = 2.6f, Accel = 7f, TurnRate = 1.5f;
    const float HullRadius = 1.7f;

    Terrain _terrain;
    NavGrid _nav;
    HarvestField _field;
    WildSim _sim;
    ProceduralAudio _audio;
    Node3D _drum;
    GpuParticles3D _chips, _gore;
    ParticleProcessMaterial _chipMaterial;
    AudioStreamPlayer3D _hum, _grind;
    float _pitch, _roll, _bob, _drumAngle;
    Vector2 _input;
    bool _boost;
    float _chopTimer;

    public void Setup(Terrain terrain, NavGrid nav, HarvestField field, WildSim sim, HarvestRules rules, ProceduralAudio audio)
    {
        _terrain = terrain;
        _nav = nav;
        _field = field;
        _sim = sim;
        _audio = audio;
        Storage = new Storage(rules.StorageCapacity);
        Blades = new HarvesterBlades(rules);
    }

    public void Rebind(WildSim sim) => _sim = sim;

    public override void _Ready()
    {
        BuildBody();
        BuildParticles();
        _hum = new AudioStreamPlayer3D { Stream = _audio?.HoverLoop, UnitSize = 6f, VolumeDb = -10f, MaxDistance = 60f };
        _grind = new AudioStreamPlayer3D { Stream = _audio?.GrindLoop, UnitSize = 6f, VolumeDb = -4f, MaxDistance = 70f, Position = Blades.Center };
        AddChild(_hum);
        AddChild(_grind);
        if (_audio != null) _hum.Play();
    }

    public void SetInput(Vector2 move, bool boost)
    {
        _input = move;
        _boost = boost;
    }

    public Vector3 Forward => new(-MathF.Sin(Yaw), 0f, -MathF.Cos(Yaw));

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        bool active = Driven || AutoThrottle != 0f;
        Vector2 input = Driven ? _input : new Vector2(0f, -AutoThrottle);
        if (!active) input = Vector2.Zero;

        // Steering: turn on A/D, thrust on W/S, the hull's sideways slide bleeds off quickly.
        Yaw -= input.X * TurnRate * dt * (input.Y > 0.1f ? -1f : 1f);
        Vector3 fwd = Forward, right = new(-fwd.Z, 0f, fwd.X);
        float along = Velocity.Dot(fwd), side = Velocity.Dot(right);
        float cap = CuttingFor > 0f ? CutSpeed : _boost ? BoostSpeed : TopSpeed;
        along = Mathf.MoveToward(along, -input.Y * cap, Accel * dt);
        if (MathF.Abs(along) > cap) along = Mathf.MoveToward(along, MathF.Sign(along) * cap, Accel * 2f * dt);
        side = Mathf.MoveToward(side, 0f, 10f * dt);
        Velocity = fwd * along + right * side;

        Vector3 p = GlobalPosition + Velocity * dt;
        PushOut(ref p);
        p = _terrain.Clamp(p, 8f);

        // Hover: ride the ground under the four corners, tilting with it.
        float fH = Ground(p + fwd * 2f), bH = Ground(p - fwd * 2f), lH = Ground(p - right * 1.4f), rH = Ground(p + right * 1.4f);
        _bob += dt;
        float y = MathF.Max((fH + bH) * 0.5f, (lH + rH) * 0.5f) + HoverHeight + MathF.Sin(_bob * 2.1f) * 0.04f;
        p.Y = Mathf.Lerp(GlobalPosition.Y, y, 1f - MathF.Exp(-10f * dt));
        _pitch = Geo.Smooth(_pitch, MathF.Atan2(fH - bH, 4f) - along * 0.004f, 6f, dt);
        _roll = Geo.Smooth(_roll, MathF.Atan2(lH - rH, 2.8f) + side * 0.02f - input.X * along * 0.004f, 6f, dt);
        GlobalTransform = new Transform3D(new Basis(Vector3.Up, Yaw) * new Basis(Vector3.Right, _pitch) * new Basis(Vector3.Back, _roll), p);

        // Blades: always spinning while someone drives.
        Blades.Spinning = active;
        _drumAngle += dt * (active ? 18f : 0f);
        _drum.Rotation = new Vector3(_drumAngle, 0f, 0f);
        if (active && _sim != null)
        {
            LastTick = Blades.Tick(GlobalTransform, dt, _field, _sim, Storage, Driver);
            bool wood = LastTick.PlantsHit > 0, flesh = LastTick.CreaturesHit > 0;
            CuttingFor = wood || flesh ? 0.25f : MathF.Max(0f, CuttingFor - dt);
            _chips.Emitting = wood;
            _gore.Emitting = flesh;
            if ((wood || flesh) && (_chopTimer -= dt) <= 0f)
            {
                _chopTimer = 0.12f;
                _audio?.Play("chop", GlobalTransform * Blades.Center, wood ? 1f : 0.7f, -2f);
            }
            if ((wood || flesh) && !_grind.Playing && _audio != null) _grind.Play();
            if (!wood && !flesh && _grind.Playing) _grind.Stop();
        }
        else
        {
            _chips.Emitting = false;
            _gore.Emitting = false;
            if (_grind.Playing) _grind.Stop();
        }
        _hum.PitchScale = 0.8f + MathF.Abs(along) / BoostSpeed * 0.6f;
    }

    public ITarget Driver;

    float Ground(Vector3 p) => _terrain.Height(p.X, p.Z);

    /// <summary>Slides off rocks and standing trunks, and shoves creatures aside.</summary>
    void PushOut(ref Vector3 p)
    {
        Vector3 fwd = Forward;
        foreach (Vector3 center in new[] { p + fwd * 1.2f, p - fwd * 1.4f })
        {
            var near = _nav.Near(center);
            if (near == null) continue;
            foreach (Obstacle o in near)
            {
                if (!o.Active) continue;
                Vector3 away = WildSim.Flat(center - o.Position);
                float d = away.Length(), min = o.Radius + HullRadius;
                if (d >= min || d < 1e-3f) continue;
                Vector3 n = away / d;
                p += n * (min - d);
                float into = Velocity.Dot(n);
                if (into < 0f) Velocity -= n * into;
            }
        }
        if (_sim == null) return;
        foreach (Creature c in _sim.Creatures)
        {
            if (c.Removed || c.Species.Flies && !c.Landed) continue;
            Vector3 away = WildSim.Flat(c.Position - p);
            float d = away.Length(), min = c.Radius + HullRadius;
            if (d >= min || d < 1e-3f) continue;
            if (c.Species.Plan == BodyPlan.Giant)
            {
                // A giant doesn't budge: the vessel does.
                Vector3 n = -away / d;
                p += n * (min - d);
                float into = Velocity.Dot(n);
                if (into < 0f) Velocity -= n * into;
            }
            else c.Position += away / d * (min - d);
        }
    }

    // ---- Looks ------------------------------------------------------------------------

    void BuildBody()
    {
        var cream = Mat("f1e2c0");
        var orange = Mat("e8641e");
        var red = Mat("b8321e");
        var dark = Mat("2a2d36", 0.35f, 0.4f);
        var steel = Mat("8a8f98", 0.3f, 0.7f);
        var glass = new StandardMaterial3D { AlbedoColor = new Color("1d2b3a"), Roughness = 0.05f, Metallic = 0.6f };
        var glowCyan = Glow("7cf9ff", 3f);
        var glowAmber = Glow("ffb03b", 2.5f);
        var checker = new StandardMaterial3D
        {
            AlbedoTexture = CheckerTexture(),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            Uv1Scale = new Vector3(3f, 1f, 1f),
        };

        Box(new Vector3(3.2f, 0.9f, 5.0f), new Vector3(0, 0.2f, 0.2f), cream);           // hull
        Box(new Vector3(3.25f, 0.22f, 5.05f), new Vector3(0, 0.05f, 0.2f), orange);      // stripe
        Box(new Vector3(3.26f, 0.08f, 5.06f), new Vector3(0, -0.12f, 0.2f), red);
        Box(new Vector3(3.5f, 0.35f, 5.3f), new Vector3(0, -0.4f, 0.2f), dark);          // skirt
        Box(new Vector3(1.9f, 0.75f, 1.6f), new Vector3(0, 0.95f, -0.9f), cream);        // cab
        Box(new Vector3(1.95f, 0.5f, 0.08f), new Vector3(0, 1.0f, -1.72f), glass);       // windscreen
        Box(new Vector3(0.08f, 0.45f, 1.4f), new Vector3(-0.98f, 1.0f, -0.9f), glass);
        Box(new Vector3(0.08f, 0.45f, 1.4f), new Vector3(0.98f, 1.0f, -0.9f), glass);
        Box(new Vector3(2.6f, 1.0f, 2.0f), new Vector3(0, 1.1f, 1.5f), orange);          // hopper
        Box(new Vector3(2.4f, 0.1f, 1.8f), new Vector3(0, 1.62f, 1.5f), dark);
        for (int s = -1; s <= 1; s += 2)
        {
            Box(new Vector3(0.7f, 0.7f, 1.6f), new Vector3(s * 1.85f, 0.35f, 2.0f), cream);  // engine pods
            Box(new Vector3(0.5f, 0.5f, 0.1f), new Vector3(s * 1.85f, 0.35f, 2.82f), glowAmber);
            Box(new Vector3(0.12f, 1.4f, 0.12f), new Vector3(s * 1.5f, 0.25f, -3.2f), steel);  // blade arms
            Box(new Vector3(0.12f, 0.12f, 1.4f), new Vector3(s * 1.5f, 0.85f, -2.6f), steel);
        }
        for (int i = 0; i < 4; i++)
            Box(new Vector3(0.6f, 0.06f, 0.6f), new Vector3((i % 2 == 0 ? -1f : 1f) * 1.0f, -0.6f, i < 2 ? -1.2f : 1.6f), glowCyan); // hover pads
        Box(new Vector3(3.3f, 0.3f, 0.12f), new Vector3(0, 0.95f, -3.0f), checker);    // hazard guard

        // The blade drum: a spinning roller bristling with teeth, across the whole front.
        _drum = new Node3D { Position = Blades.Center };
        AddChild(_drum);
        _drum.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.42f, BottomRadius = 0.42f, Height = 3.0f, RadialSegments = 12 },
            MaterialOverride = dark,
            RotationDegrees = new Vector3(0, 0, 90),
        });
        for (int i = 0; i < 18; i++)
        {
            float a = i * MathF.Tau / 6f, x = -1.3f + (i % 9) * 0.32f + (i / 9) * 0.16f;
            var tooth = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.1f, 0.42f, 0.16f) },
                MaterialOverride = i % 3 == 0 ? orange : steel,
                Position = new Vector3(x, MathF.Cos(a) * 0.5f, MathF.Sin(a) * 0.5f),
                Rotation = new Vector3(a, 0f, 0f),
            };
            _drum.AddChild(tooth);
        }

        // Riding surfaces: you can climb on it, and it blocks you like a vehicle should.
        var hull = new AnimatableBody3D { CollisionLayer = Layers.Vessel, CollisionMask = 0, SyncToPhysics = false };
        hull.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(3.4f, 1.5f, 5.4f) }, Position = new Vector3(0, 0.3f, 0.2f) });
        AddChild(hull);
    }

    void BuildParticles()
    {
        var chunk = new BoxMesh { Size = new Vector3(0.18f, 0.08f, 0.3f) };
        var vertexColour = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.9f };
        chunk.Material = vertexColour;
        _chipMaterial = Spray(new Color("c79a5a"), new Color("8a5a2e"));
        _chips = new GpuParticles3D
        {
            Amount = 120, Lifetime = 1.1f, Emitting = false, ProcessMaterial = _chipMaterial, DrawPass1 = chunk,
            Position = Blades.Center + new Vector3(0, 0.6f, 0.2f), LocalCoords = false,
            VisibilityAabb = new Aabb(new Vector3(-8, -4, -8), new Vector3(16, 12, 16)),
        };
        AddChild(_chips);
        var meat = new SphereMesh { Radius = 0.09f, Height = 0.16f, RadialSegments = 6, Rings = 3, Material = vertexColour };
        _gore = new GpuParticles3D
        {
            Amount = 50, Lifetime = 0.9f, Emitting = false, ProcessMaterial = Spray(new Color("d23a3a"), new Color("7a1020")), DrawPass1 = meat,
            Position = Blades.Center + new Vector3(0, 0.6f, 0.2f), LocalCoords = false,
            VisibilityAabb = new Aabb(new Vector3(-8, -4, -8), new Vector3(16, 12, 16)),
        };
        AddChild(_gore);
    }

    public void SetChipColours(Color a, Color b) => _chipMaterial.ColorRamp = Ramp(a, b);

    /// <summary>Chips burst up and back over the cab, into the hopper.</summary>
    static ParticleProcessMaterial Spray(Color a, Color b) => new()
    {
        Direction = new Vector3(0, 1f, 0.55f),
        Spread = 38f,
        InitialVelocityMin = 5f,
        InitialVelocityMax = 9f,
        Gravity = new Vector3(0, -14f, 0),
        AngularVelocityMin = -720f,
        AngularVelocityMax = 720f,
        ScaleMin = 0.6f,
        ScaleMax = 1.5f,
        EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
        EmissionBoxExtents = new Vector3(1.3f, 0.2f, 0.3f),
        ColorRamp = Ramp(a, b),
    };

    static GradientTexture1D Ramp(Color a, Color b) => new() { Gradient = new Gradient { Colors = new[] { a, b }, Offsets = new[] { 0f, 1f } } };

    void Box(Vector3 size, Vector3 pos, Material m) => AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = m, Position = pos });

    static StandardMaterial3D Mat(string hex, float roughness = 0.55f, float metallic = 0f) => new() { AlbedoColor = new Color(hex), Roughness = roughness, Metallic = metallic };

    static StandardMaterial3D Glow(string hex, float energy) => new()
    {
        AlbedoColor = new Color(hex), EmissionEnabled = true, Emission = new Color(hex), EmissionEnergyMultiplier = energy,
    };

    static ImageTexture CheckerTexture()
    {
        var img = Image.CreateEmpty(8, 2, false, Image.Format.Rgb8);
        for (int x = 0; x < 8; x++)
        for (int y = 0; y < 2; y++)
            img.SetPixel(x, y, (x + y) % 2 == 0 ? new Color("1a1a1a") : new Color("f2c230"));
        return ImageTexture.CreateFromImage(img);
    }
}
