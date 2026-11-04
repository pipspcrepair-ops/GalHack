using System.Collections.Generic;
using Godot;
using ShipHandling.Handling;
using ShipHandling.Physics;

namespace ShipHandling;

/// <summary>
/// Draws a vessel from its structure: Foss-style blocks, a nozzle and a flame for every
/// thruster (longer and white-hot while boosting), glowing pads and a wash under hover engines,
/// landing legs that really extend, and scorch marks where it has been hit. Presentation only:
/// it reads the vessel, never moves it.
/// </summary>
public partial class VesselArt : Node3D
{
    readonly Vessel _vessel;
    readonly List<(ThrusterSpec spec, MeshInstance3D flame, float radius)> _flames = new();
    readonly List<(HoverEngineSpec spec, MeshInstance3D wash)> _washes = new();
    readonly List<(GearSpec spec, MeshInstance3D leg, MeshInstance3D foot)> _legs = new();
    readonly List<Decal> _scorches = new();
    CpuParticles3D _spray;
    Node3D _parts;
    int _revision = -1;
    float _time;

    static readonly Dictionary<(Color, BlockKind), StandardMaterial3D> Materials = new();
    static StandardMaterial3D _flameMat, _boostMat, _washMat, _nozzleMat, _rimMat;
    static Texture2D _scorch;

    public VesselArt(Vessel vessel)
    {
        _vessel = vessel;
        Name = "Art";
    }

    public override void _Ready()
    {
        _vessel.World.Impact += OnImpact;
        Rebuild();
    }

    public override void _ExitTree() => _vessel.World.Impact -= OnImpact;

    public override void _Process(double delta)
    {
        _time += (float)delta;
        if (_vessel.Structure.Revision != _revision) Rebuild();
        UpdateFlames();
        UpdateHover();
        UpdateGear();
    }

    void Rebuild()
    {
        _revision = _vessel.Structure.Revision;
        _parts?.QueueFree();
        _flames.Clear();
        _washes.Clear();
        _legs.Clear();
        _parts = new Node3D { Name = "Parts" };
        AddChild(_parts);

        IVesselStructure s = _vessel.Structure;
        foreach (var block in s.Blocks) _parts.AddChild(BlockMesh(block));
        foreach (var thruster in s.Thrusters) AddThruster(thruster);
        foreach (var engine in s.HoverEngines) AddHoverEngine(engine);
        if (s.HoverEngines.Count > 0) AddSpray();
        foreach (var leg in s.LandingGear) AddLeg(leg);
    }

    static MeshInstance3D BlockMesh(HullBlock block)
    {
        Color color = Palette.Of(block.Color);
        Mesh mesh = block.Kind == BlockKind.Prism
            ? new PrismMesh { Size = block.Size, LeftToRight = 0f }
            : new BoxMesh { Size = block.Size };
        return new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = Material(color, block.Kind),
            Position = block.Center,
            RotationDegrees = block.RotationDegrees,
            CastShadow = block.Kind == BlockKind.Glass ? GeometryInstance3D.ShadowCastingSetting.Off : GeometryInstance3D.ShadowCastingSetting.On,
        };
    }

    static StandardMaterial3D Material(Color color, BlockKind kind)
    {
        var key = (color, kind == BlockKind.Prism ? BlockKind.Box : kind);
        if (Materials.TryGetValue(key, out var m)) return m;
        m = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.7f };
        if (kind == BlockKind.Glass)
        {
            m.AlbedoColor = Palette.Glass;
            m.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            m.Roughness = 0.05f;
            m.Metallic = 0.5f;
        }
        else if (kind == BlockKind.Glow)
        {
            m.EmissionEnabled = true;
            m.Emission = color;
            m.EmissionEnergyMultiplier = 3f;
        }
        return Materials[key] = m;
    }

    // ---- Thrusters --------------------------------------------------------------------------

    void AddThruster(ThrusterSpec t)
    {
        float r = t.Size switch { "capital" => 2.2f, "large" => 0.55f, _ => 0.25f };
        Vector3 exhaust = -t.Facing.Normalized();
        _nozzleMat ??= new StandardMaterial3D { AlbedoColor = new Color("3d3d44"), Metallic = 0.6f, Roughness = 0.4f };
        _rimMat ??= new StandardMaterial3D
        {
            AlbedoColor = Palette.Orange, EmissionEnabled = true, Emission = Palette.Orange, EmissionEnergyMultiplier = 2f,
        };

        // The nozzle's bell opens toward the exhaust.
        _parts.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = r * 0.7f, BottomRadius = r, Height = r * 1.4f, RadialSegments = 12 },
            MaterialOverride = _nozzleMat,
            Transform = new Transform3D(AlignY(t.Facing), t.Position),
        });
        _parts.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = r * 0.85f, BottomRadius = r * 0.85f, Height = 0.02f + r * 0.05f, RadialSegments = 12 },
            MaterialOverride = _rimMat,
            Transform = new Transform3D(AlignY(t.Facing), t.Position + exhaust * r * 0.7f),
        });

        _flameMat ??= FlameMaterial(Palette.Flame, 4f);
        _boostMat ??= FlameMaterial(Palette.BoostFlame, 8f);
        var flame = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0f, BottomRadius = r * 0.8f, Height = 1f, RadialSegments = 10, Rings = 1 },
            MaterialOverride = _flameMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        _parts.AddChild(flame);
        _flames.Add((t, flame, r));
    }

    static StandardMaterial3D FlameMaterial(Color color, float energy) => new()
    {
        AlbedoColor = new Color(color, 0.85f),
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        EmissionEnabled = true,
        Emission = color,
        EmissionEnergyMultiplier = energy,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    /// <summary>
    /// How hard each thruster is working, from what the flight model actually did this tick.
    /// Lift thrusters also burn steadily while they hold a ship up against gravity.
    /// </summary>
    void UpdateFlames()
    {
        HandlingStats stats = _vessel.Stats;
        bool landed = _vessel.Frozen || _vessel.Settling || _vessel.Wrecked;
        bool boosting = _vessel.State.Boosting;
        float boost = boosting ? _vessel.World.Tunables.Boost.Multiplier : 1f;
        Vector3 accel = _vessel.State.ThrustLocal;
        float hold = !stats.IsHover && _vessel.Conditions.Gravity > 0f && stats.Thrust[(int)Direction.Up] > 0f
            ? Mathf.Min(1f, stats.Weight / stats.Thrust[(int)Direction.Up]) : 0f;

        foreach (var (spec, flame, r) in _flames)
        {
            Vector3 facing = spec.Facing.Normalized();
            float max = 0f;
            for (int d = 0; d < 6; d++)
                if (facing.Dot(HandlingModel.Directions[d]) > 0.7f) max = stats.Accel((Direction)d) * boost;
            float throttle = max > 0f ? Mathf.Clamp(accel.Dot(facing) / max, 0f, 1f) : 0f;
            if (facing.Y > 0.7f) throttle = Mathf.Max(throttle, hold * 0.6f);
            if (landed) throttle = 0f;

            float flicker = 1f + 0.08f * Mathf.Sin(_time * 53f + spec.Position.X * 7f) + 0.05f * Mathf.Sin(_time * 31f + spec.Position.Z);
            float length = r * (0.6f + 7f * throttle) * (boosting && throttle > 0.05f ? 1.7f : 1f) * flicker;
            flame.Visible = throttle > 0.02f;
            flame.MaterialOverride = boosting ? _boostMat : _flameMat;
            Vector3 exhaust = -facing;
            flame.Transform = new Transform3D(AlignY(exhaust) * Basis.FromScale(new Vector3(1f, length, 1f)),
                spec.Position + exhaust * (r * 0.7f + length / 2f));
        }
    }

    // ---- Hover engines ----------------------------------------------------------------------

    void AddHoverEngine(HoverEngineSpec e)
    {
        float r = e.Size == "large" ? 1.6f : 0.55f;
        _parts.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = r, BottomRadius = r * 1.1f, Height = 0.3f, RadialSegments = 16 },
            MaterialOverride = Material(Palette.Dark, BlockKind.Box),
            Position = e.Position + Vector3.Up * 0.15f,
        });
        _parts.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = r * 0.8f, BottomRadius = r * 0.8f, Height = 0.04f, RadialSegments = 16 },
            MaterialOverride = Material(Palette.HoverGlow, BlockKind.Glow),
            Position = e.Position - Vector3.Up * 0.01f,
        });
        _washMat ??= new StandardMaterial3D
        {
            AlbedoColor = new Color(Palette.HoverGlow, 0.18f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        var wash = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = r * 0.8f, BottomRadius = r * 1.8f, Height = 1f, RadialSegments = 16, Rings = 1 },
            MaterialOverride = _washMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _parts.AddChild(wash);
        _washes.Add((e, wash));
    }

    /// <summary>White spray kicked up behind a hover vessel skimming water.</summary>
    void AddSpray()
    {
        float back = 0f;
        foreach (var e in _vessel.Structure.HoverEngines) back = Mathf.Max(back, e.Position.Z);
        _spray = new CpuParticles3D
        {
            Amount = 160,
            Lifetime = 0.9f,
            Emitting = false,
            LocalCoords = false,
            Mesh = new QuadMesh { Size = new Vector2(0.7f, 0.7f) },
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(1.4f, 0.1f, 0.4f),
            Direction = new Vector3(0f, 1f, 0.6f),
            Spread = 25f,
            InitialVelocityMin = 3f,
            InitialVelocityMax = 7f,
            Gravity = new Vector3(0f, -9.8f, 0f),
            ScaleAmountMin = 0.35f,
            ScaleAmountMax = 1.1f,
            ColorRamp = new Gradient
            {
                Colors = new[] { new Color(1f, 1f, 1f, 0.8f), new Color(1f, 1f, 1f, 0f) },
            },
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
                VertexColorUseAsAlbedo = true,
                AlbedoColor = new Color(0.92f, 0.98f, 1f),
                AlbedoTexture = new GradientTexture2D
                {
                    Fill = GradientTexture2D.FillEnum.Radial,
                    FillFrom = new Vector2(0.5f, 0.5f),
                    FillTo = new Vector2(1f, 0.5f),
                    Gradient = new Gradient { Colors = new[] { Colors.White, new Color(1f, 1f, 1f, 0f) } },
                },
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = new Vector3(0f, -_vessel.Stats.RideHeight + 0.2f, back + 0.5f),
        };
        _parts.AddChild(_spray);
    }

    void UpdateHover()
    {
        HoverController hover = _vessel.Hover;
        if (hover == null) return;
        Vector3 at = _vessel.GlobalPosition;
        _spray.Emitting = !_vessel.Frozen && _vessel.Speed > 8f && hover.Grounded
                          && PlanetSurface.InLake(at.X, at.Z) && hover.GroundRef <= PlanetSurface.WaterLevel + 0.05f;
        float reach = Mathf.Clamp(float.IsFinite(hover.Clearance) ? hover.Clearance : 0f, 0f, _vessel.Stats.RideHeight * 1.5f);
        foreach (var (spec, wash) in _washes)
        {
            wash.Visible = reach > 0.1f && !_vessel.Frozen;
            float pulse = 1f + 0.06f * Mathf.Sin(_time * 40f + spec.Position.Z * 3f);
            wash.Transform = new Transform3D(Basis.FromScale(new Vector3(pulse, reach, pulse)), spec.Position + Vector3.Down * reach / 2f);
        }
    }

    // ---- Landing gear -----------------------------------------------------------------------

    void AddLeg(GearSpec g)
    {
        float w = g.Size == "large" ? 1.1f : 0.22f;
        var leg = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(w, 1f, w) }, MaterialOverride = Material(Palette.Dark, BlockKind.Box) };
        var foot = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(w * 3f, w * 0.5f, w * 3f) },
            MaterialOverride = Material(Palette.Yellow, BlockKind.Box),
        };
        _parts.AddChild(leg);
        _parts.AddChild(foot);
        _legs.Add((g, leg, foot));
    }

    void UpdateGear()
    {
        float ext = _vessel.Gear?.Extension ?? 0f;
        foreach (var (spec, leg, foot) in _legs)
        {
            float length = spec.Length * ext;
            leg.Visible = foot.Visible = ext > 0.02f;
            leg.Transform = new Transform3D(Basis.FromScale(new Vector3(1f, Mathf.Max(length, 0.01f), 1f)), spec.Position + Vector3.Down * length / 2f);
            foot.Position = spec.Position + Vector3.Down * length;
        }
    }

    // ---- Scorch marks -----------------------------------------------------------------------

    /// <summary>Damage lands near the contact point, so the scorch does too.</summary>
    void OnImpact(ImpactInfo hit)
    {
        if (hit.Damage <= 0f || (hit.Vessel != _vessel && hit.Other != _vessel)) return;
        Vector3 normal = hit.Vessel == _vessel ? hit.Normal : -hit.Normal; // pointing from what was hit toward us
        float size = Mathf.Clamp(Mathf.Sqrt(hit.Damage) * 0.8f, 1.2f, _vessel.Length * 0.35f);
        var decal = new Decal
        {
            TextureAlbedo = ScorchTexture(),
            Size = new Vector3(size, size, size),
            AlbedoMix = 0.9f,
        };
        AddChild(decal);
        // A decal projects along its -Y; aim it into the hull, from just outside.
        decal.GlobalTransform = new Transform3D(AlignY(-normal), hit.Point - normal * 0.1f);
        _scorches.Add(decal);
        if (_scorches.Count > 16)
        {
            _scorches[0].QueueFree();
            _scorches.RemoveAt(0);
        }
    }

    /// <summary>A soft black-brown blot, generated once.</summary>
    public static Texture2D ScorchTexture()
    {
        if (_scorch != null) return _scorch;
        const int n = 128;
        var image = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        var noise = new FastNoiseLite { Seed = 3, Frequency = 0.08f };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x - n / 2f) / (n / 2f), dy = (y - n / 2f) / (n / 2f);
                float r = Mathf.Sqrt(dx * dx + dy * dy) + 0.15f * noise.GetNoise2D(x, y);
                float a = Mathf.Clamp(1f - Mathf.SmoothStep(0.45f, 1f, r), 0f, 1f);
                image.SetPixel(x, y, new Color(0.09f, 0.06f, 0.04f, a * 0.92f));
            }
        return _scorch = ImageTexture.CreateFromImage(image);
    }

    /// <summary>A basis whose Y axis points along <paramref name="y"/>.</summary>
    public static Basis AlignY(Vector3 y)
    {
        y = y.Normalized();
        Vector3 helper = Mathf.Abs(y.Dot(Vector3.Forward)) < 0.9f ? Vector3.Forward : Vector3.Right;
        Vector3 x = y.Cross(helper).Normalized();
        Vector3 z = x.Cross(y);
        return new Basis(x, y, z);
    }
}
