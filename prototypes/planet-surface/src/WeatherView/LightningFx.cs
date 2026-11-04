using System;
using System.Collections.Generic;
using Godot;
using Weather.Model;
using PlanetSurface.World;

namespace PlanetSurface.WeatherView;

/// <summary>
/// Ported from the weather prototype (bolts rise from this planet's cloud base). Lightning you can see coming: while a strike charges, its target glows and crackles (St
/// Elmo's fire) and the cloud above flickers; then a branching bolt, a flash that lights the
/// whole sky, and a few re-strokes. Bolts are camera-facing ribbons rebuilt only while visible.
/// </summary>
public partial class LightningFx : Node3D
{
    public WeatherRig Fx;

    /// <summary>0..~1.5: how bright the last strike is right now, for the sky, fog and ambient light.</summary>
    public float Flash { get; private set; }
    public Vector3 FlashDirection { get; private set; } = Vector3.Up;

    const float BoltLife = 0.55f;
    static readonly Color Core = new(1f, 1f, 1f, 1f);

    ShaderMaterial _boltMaterial;
    StandardMaterial3D _glowMaterial;
    ImmediateMesh _boltMesh;
    MeshInstance3D _bolt;
    OmniLight3D _strikeLight;
    readonly List<(Vector3 a, Vector3 b, float width, float alpha)> _segments = new();
    readonly List<(Vector3 a, Vector3 b, float width, float alpha)> _crackles = new();
    float _boltAge = 99f;
    Vector3 _boltTarget;
    readonly RandomNumberGenerator _rng = new() { Seed = 4242 };

    sealed class Charge
    {
        public Node3D Root;
        public MeshInstance3D Ball;
        public OmniLight3D Light;
        public ImmediateMesh Crackle;
        public GpuParticles3D Sparks;
        public float NextCrackle;
    }
    readonly List<Charge> _charges = new();

    public override void _Ready()
    {
        ProcessPriority = -10; // compute the flash before WeatherFx lights the scene with it
        _boltMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/glow.gdshader") };
        _boltMaterial.SetShaderParameter("tint", new Color("e6dcff"));
        _boltMaterial.SetShaderParameter("energy", 6f);
        _boltMaterial.SetShaderParameter("softness", 0.5f);
        // The charge glow: a soft, over-bright sprite that blooms, readable from far away.
        _glowMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled, BillboardKeepScale = true, AlbedoTexture = Fx.SoftDot,
            AlbedoColor = new Color(2.4f, 2.1f, 4.2f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            NoDepthTest = false, DisableFog = true,
        };

        _boltMesh = new ImmediateMesh();
        _bolt = new MeshInstance3D { Mesh = _boltMesh, MaterialOverride = _boltMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, TopLevel = true };
        AddChild(_bolt);
        _strikeLight = new OmniLight3D { LightColor = new Color("d8ccff"), OmniRange = 120f, LightEnergy = 0f, ShadowEnabled = false, TopLevel = true };
        AddChild(_strikeLight);

        for (int i = 0; i < 3; i++) _charges.Add(MakeCharge());
    }

    Charge MakeCharge()
    {
        var root = new Node3D { Visible = false, TopLevel = true };
        AddChild(root);
        var ball = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(5f, 5f) },
            MaterialOverride = _glowMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        root.AddChild(ball);
        var light = new OmniLight3D { LightColor = new Color("b8a6ff"), OmniRange = 26f, ShadowEnabled = false };
        root.AddChild(light);
        var crackle = new ImmediateMesh();
        var crackleNode = new MeshInstance3D { Mesh = crackle, MaterialOverride = _boltMaterial, TopLevel = true, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        root.AddChild(crackleNode);
        var sparks = new GpuParticles3D
        {
            Amount = 40, Lifetime = 0.5f, LocalCoords = false, AmountRatio = 0f,
            ProcessMaterial = new ParticleProcessMaterial
            {
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere, EmissionSphereRadius = 1.2f,
                Direction = Vector3.Up, Spread = 70f, InitialVelocityMin = 2f, InitialVelocityMax = 7f, Gravity = new Vector3(0, -3, 0),
                ScaleMin = 0.5f, ScaleMax = 1.2f,
            },
            DrawPass1 = new QuadMesh { Size = new Vector2(0.15f, 0.15f) },
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
                AlbedoColor = new Color(2.5f, 2.2f, 4f), AlbedoTexture = Fx.SoftDot, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        root.AddChild(sparks);
        return new Charge { Root = root, Ball = ball, Light = light, Crackle = crackle, Sparks = sparks };
    }

    public int ActiveParticles()
    {
        int n = 0;
        foreach (Charge c in _charges) if (c.Root.Visible) n += (int)(c.Sparks.Amount * c.Sparks.AmountRatio);
        return n;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        Vector3 camera = Fx.Camera.GlobalPosition;
        UpdateCharges(dt, camera);

        _boltAge += dt;
        float stroke = Stroke(_boltAge);
        float distance = camera.DistanceTo(_boltTarget);
        float near = 1f / (1f + distance / 350f);
        Flash = Mathf.Max(stroke * (0.25f + 0.6f * near), Flash - dt * 4f);
        _strikeLight.LightEnergy = stroke * 14f;
        _bolt.Visible = _boltAge < BoltLife;
        if (_bolt.Visible)
        {
            _boltMaterial.SetShaderParameter("energy", 2f + 7f * stroke);
            DrawRibbons(_boltMesh, _segments, camera, 0.4f + 0.6f * stroke);
        }
    }

    /// <summary>The flicker of a real strike: the return stroke, then a couple of re-strokes down the same channel.</summary>
    static float Stroke(float t)
    {
        if (t < 0.05f) return 1f;
        if (t < 0.09f) return 0.15f;
        if (t < 0.15f) return 0.9f;
        if (t < 0.21f) return 0.25f;
        if (t < 0.3f) return 0.75f * (1f - (t - 0.21f) / 0.09f);
        if (t < BoltLife) return 0.08f * (1f - (t - 0.3f) / (BoltLife - 0.3f));
        return 0f;
    }

    void UpdateCharges(float dt, Vector3 camera)
    {
        int used = 0;
        float skyFlicker = 0f;
        foreach (Hazard h in Fx.Hazards)
        {
            if (h.Kind != HazardKind.Lightning || used >= _charges.Count) continue;
            Charge c = _charges[used++];
            float p = h.Progress;
            c.Root.Visible = true;
            c.Root.GlobalPosition = h.Target;
            float pulse = 0.75f + 0.25f * Mathf.Sin(h.Age * (8f + 30f * p));
            float size = Mathf.Lerp(0.3f, 1.6f, p * p) * pulse;
            c.Ball.Scale = Vector3.One * size;
            c.Light.LightEnergy = (0.5f + 5f * p * p) * pulse;
            c.Sparks.AmountRatio = Mathf.Clamp(p * 1.4f - 0.2f, 0f, 1f);
            c.NextCrackle -= dt;
            if (c.NextCrackle <= 0f)
            {
                c.NextCrackle = Mathf.Lerp(0.2f, 0.05f, p);
                BuildCrackles(h.Target, p);
                DrawRibbons(c.Crackle, _crackles, camera, 0.3f + 0.7f * p);
            }
            skyFlicker = Mathf.Max(skyFlicker, p * p * (_rng.Randf() < 0.15f ? 0.35f : 0.05f));
        }
        for (int i = used; i < _charges.Count; i++) _charges[i].Root.Visible = false;
        if (skyFlicker > Flash) Flash = skyFlicker; // the cloud above flickers as the charge builds
    }

    /// <summary>Short streamers reaching up from the target, more and longer as the charge builds.</summary>
    void BuildCrackles(Vector3 at, float progress)
    {
        _crackles.Clear();
        int count = 1 + (int)(progress * 4f);
        for (int i = 0; i < count; i++)
        {
            var end = at + new Vector3(_rng.RandfRange(-3f, 3f), _rng.RandfRange(2f, 4f + progress * 6f), _rng.RandfRange(-3f, 3f));
            Jag(_crackles, at, end, 3, 0.18f, 0.7f, 0);
        }
    }

    /// <summary>The strike: a branching bolt from the cloud base down to the target.</summary>
    public void Strike(Vector3 target)
    {
        _boltAge = 0f;
        _boltTarget = target;
        _strikeLight.GlobalPosition = target + Vector3.Up * 6f;
        Vector3 cloud = target + new Vector3(_rng.RandfRange(-90f, 90f), 0f, _rng.RandfRange(-90f, 90f));
        cloud.Y = Mathf.Max(Fx.CloudBaseY, target.Y + 150f);
        FlashDirection = (cloud - Fx.Camera.GlobalPosition).Normalized();
        _segments.Clear();
        Jag(_segments, cloud, target, 7, 1.1f, 1f, 0);
    }

    /// <summary>Midpoint displacement: a jagged path, with branches forking off and fading.</summary>
    void Jag(List<(Vector3 a, Vector3 b, float width, float alpha)> into, Vector3 from, Vector3 to, int levels, float width, float alpha, int depth)
    {
        var points = new List<Vector3> { from, to };
        float rough = 0.22f;
        for (int level = 0; level < levels; level++)
        {
            var next = new List<Vector3>(points.Count * 2);
            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector3 a = points[i], b = points[i + 1];
                float length = a.DistanceTo(b);
                next.Add(a);
                next.Add((a + b) / 2f + new Vector3(_rng.RandfRange(-1f, 1f), _rng.RandfRange(-0.3f, 0.3f), _rng.RandfRange(-1f, 1f)) * length * rough);
            }
            next.Add(points[^1]);
            points = next;
        }

        for (int i = 0; i < points.Count - 1; i++)
        {
            float taper = Mathf.Lerp(0.6f, 1f, i / (float)points.Count);
            into.Add((points[i], points[i + 1], width * taper, alpha));
            if (depth < 2 && levels > 3 && _rng.Randf() < 0.05f)
            {
                Vector3 down = (to - from).Normalized();
                float length = from.DistanceTo(to) * _rng.RandfRange(0.15f, 0.35f);
                Vector3 side = new Vector3(_rng.RandfRange(-1f, 1f), 0f, _rng.RandfRange(-1f, 1f)).Normalized();
                Jag(into, points[i], points[i] + (down * 0.6f + side * 0.8f).Normalized() * length, levels - 2, width * 0.45f, alpha * 0.55f, depth + 1);
            }
        }
    }

    /// <summary>Each segment as a quad turned to face the camera: a hot core plus a wide soft halo.</summary>
    static void DrawRibbons(ImmediateMesh mesh, List<(Vector3 a, Vector3 b, float width, float alpha)> segments, Vector3 camera, float brightness)
    {
        mesh.ClearSurfaces();
        if (segments.Count == 0) return;
        mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);
        foreach (var (a, b, width, alpha) in segments)
        {
            Vector3 mid = (a + b) / 2f;
            float scale = Mathf.Max(1f, camera.DistanceTo(mid) * 0.004f); // stays a few pixels wide far away
            Vector3 side = (b - a).Cross(camera - mid).Normalized();
            Quad(mesh, a, b, side * width * scale * 0.5f, new Color(1, 1, 1, alpha * brightness));
            Quad(mesh, a, b, side * width * scale * 3f, new Color(0.7f, 0.6f, 1f, alpha * brightness * 0.09f));
        }
        mesh.SurfaceEnd();
    }

    static void Quad(ImmediateMesh mesh, Vector3 a, Vector3 b, Vector3 half, Color color)
    {
        void V(Vector3 p, float u)
        {
            mesh.SurfaceSetColor(color);
            mesh.SurfaceSetUV(new Vector2(u, 0));
            mesh.SurfaceAddVertex(p);
        }
        V(a - half, 0); V(a + half, 1); V(b + half, 1);
        V(a - half, 0); V(b + half, 1); V(b - half, 0);
    }
}
