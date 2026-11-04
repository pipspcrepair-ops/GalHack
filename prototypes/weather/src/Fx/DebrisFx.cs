using System.Collections.Generic;
using Godot;
using Weather.Model;

namespace Weather;

/// <summary>
/// Burning debris in a fire storm. Each fall is a model hazard with a build-up: a glowing ring
/// marks where it will land while a flaming chunk streaks down toward it, then a fireball.
/// A small fixed pool, so a long fire storm never allocates.
/// </summary>
public partial class DebrisFx : Node3D
{
    public WeatherFx Fx;

    sealed class Chunk
    {
        public int HazardId = -1;
        public Node3D Rock;
        public GpuParticles3D Trail;
        public MeshInstance3D Ring, Beam;
        public ShaderMaterial RingMaterial;
        public Vector3 From, To;
        public float Burst = 9f;
        public MeshInstance3D Fireball;
        public OmniLight3D Light;
    }

    readonly List<Chunk> _pool = new();

    public override void _Ready()
    {
        var ringShader = GD.Load<Shader>("res://shaders/ring.gdshader");
        var rockMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color("2a1a14"), EmissionEnabled = true, Emission = new Color("ff6a1a"), EmissionEnergyMultiplier = 3f, Roughness = 0.9f,
        };
        var fireMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled, BillboardKeepScale = true,
            AlbedoTexture = Fx.SoftDot, AlbedoColor = new Color(4f, 1.6f, 0.4f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        // A column of light over the landing spot: the warning reads from any angle, even edge-on to the ring.
        var beamMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.FixedY, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            AlbedoColor = new Color(3f, 0.7f, 0.25f),
            AlbedoTexture = new GradientTexture2D
            {
                Width = 32, Height = 128, FillFrom = new Vector2(0.5f, 1f), FillTo = new Vector2(0.5f, 0f),
                Gradient = new Gradient { Colors = new[] { new Color(1, 1, 1, 0.9f), new Color(1, 1, 1, 0.25f), new Color(1, 1, 1, 0f) }, Offsets = new[] { 0f, 0.3f, 1f } },
            },
        };
        var trailRamp = new Gradient
        {
            Colors = new[] { new Color(4f, 2.2f, 0.8f, 1f), new Color(2f, 0.6f, 0.15f, 0.8f), new Color(0.15f, 0.12f, 0.1f, 0.5f), new Color(0.1f, 0.1f, 0.1f, 0f) },
            Offsets = new[] { 0f, 0.2f, 0.55f, 1f },
        };

        for (int i = 0; i < 4; i++)
        {
            var chunk = new Chunk();
            chunk.Rock = new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.7f, Height = 1.2f, RadialSegments = 8, Rings = 4 }, MaterialOverride = rockMaterial, TopLevel = true, Visible = false };
            AddChild(chunk.Rock);
            chunk.Trail = new GpuParticles3D
            {
                Amount = 60, Lifetime = 1.2f, LocalCoords = false, Emitting = false, TopLevel = true,
                ProcessMaterial = new ParticleProcessMaterial
                {
                    EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere, EmissionSphereRadius = 0.6f,
                    Gravity = new Vector3(0, 2f, 0), InitialVelocityMin = 0f, InitialVelocityMax = 1.5f, Spread = 180f,
                    ScaleMin = 1.2f, ScaleMax = 3f, ColorRamp = new GradientTexture1D { Gradient = trailRamp },
                },
                DrawPass1 = new QuadMesh { Size = Vector2.One },
                MaterialOverride = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, VertexColorUseAsAlbedo = true, AlbedoTexture = Fx.SoftDot,
                    DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
                },
                VisibilityAabb = new Aabb(new Vector3(-200, -200, -200), new Vector3(400, 400, 400)),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(chunk.Trail);
            chunk.RingMaterial = new ShaderMaterial { Shader = ringShader };
            chunk.Ring = new MeshInstance3D
            {
                Mesh = new PlaneMesh { Size = new Vector2(9f, 9f) }, MaterialOverride = chunk.RingMaterial, TopLevel = true, Visible = false,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(chunk.Ring);
            chunk.Beam = new MeshInstance3D
            {
                Mesh = new QuadMesh { Size = new Vector2(1.4f, 36f), CenterOffset = new Vector3(0, 18f, 0) },
                MaterialOverride = beamMaterial, TopLevel = true, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(chunk.Beam);
            chunk.Fireball = new MeshInstance3D { Mesh = new QuadMesh { Size = new Vector2(10f, 10f) }, MaterialOverride = fireMaterial, TopLevel = true, Visible = false };
            AddChild(chunk.Fireball);
            chunk.Light = new OmniLight3D { LightColor = new Color("ff7a2a"), OmniRange = 30f, LightEnergy = 0f, TopLevel = true };
            AddChild(chunk.Light);
            _pool.Add(chunk);
        }
    }

    public int ActiveParticles()
    {
        int n = 0;
        foreach (Chunk c in _pool) if (c.Trail.Emitting) n += c.Trail.Amount;
        return n;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        foreach (Chunk c in _pool)
        {
            c.Burst += dt;
            float burst = Mathf.Clamp(1f - c.Burst / 0.8f, 0f, 1f);
            c.Fireball.Visible = burst > 0f;
            c.Fireball.Scale = Vector3.One * (0.5f + c.Burst * 2.5f);
            c.Light.LightEnergy = burst * 12f;
            if (c.HazardId >= 0 && Find(c.HazardId) == null) Release(c);
        }

        foreach (Hazard h in Fx.Hazards)
        {
            if (h.Kind != HazardKind.Debris) continue;
            Chunk c = Claim(h);
            if (c == null) continue;
            float p = h.Progress;
            // A shallow arc in from upwind, so you see it coming across the sky.
            Vector3 at = c.From.Lerp(c.To, p) + Vector3.Up * Mathf.Sin(p * Mathf.Pi) * 8f;
            c.Rock.Visible = true;
            c.Rock.GlobalPosition = at;
            c.Trail.GlobalPosition = at;
            c.Trail.Emitting = true;
            c.Ring.Visible = true;
            c.Ring.GlobalPosition = c.To + Vector3.Up * 0.15f;
            c.RingMaterial.SetShaderParameter("progress", p);
            c.Beam.Visible = true;
            c.Beam.GlobalPosition = c.To;
            float flicker = 0.7f + 0.3f * Mathf.Sin(h.Age * Mathf.Lerp(8f, 30f, p));
            c.Beam.Scale = new Vector3((0.6f + p) * flicker, 1f, 1f);
        }
    }

    Chunk Claim(Hazard h)
    {
        foreach (Chunk c in _pool) if (c.HazardId == h.Id) return c;
        foreach (Chunk c in _pool)
        {
            if (c.HazardId >= 0) continue;
            c.HazardId = h.Id;
            c.To = h.Target;
            var wind = Fx.Director.Sim.Current.Wind;
            var upwind = wind.LengthSquared() > 0.01f ? -wind.Normalized() : Vector2.Right;
            c.From = h.Target + new Vector3(upwind.X * 45f, 130f, upwind.Y * 45f);
            c.Trail.GlobalPosition = c.From;
            c.Trail.Restart();
            return c;
        }
        return null;
    }

    Hazard Find(int id)
    {
        foreach (Hazard h in Fx.Hazards) if (h.Id == id) return h;
        return null;
    }

    void Release(Chunk c)
    {
        c.HazardId = -1;
        c.Rock.Visible = false;
        c.Ring.Visible = false;
        c.Beam.Visible = false;
        c.Trail.Emitting = false;
    }

    /// <summary>The model says it landed.</summary>
    public void Impact(int hazardId, Vector3 at)
    {
        foreach (Chunk c in _pool)
        {
            if (c.HazardId != hazardId) continue;
            Release(c);
            c.Burst = 0f;
            c.Fireball.GlobalPosition = at + Vector3.Up * 2f;
            c.Light.GlobalPosition = at + Vector3.Up * 3f;
            return;
        }
    }
}
