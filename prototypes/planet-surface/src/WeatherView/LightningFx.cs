using System;
using System.Collections.Generic;
using Godot;
using Weather.Model;
using PlanetSurface.World;

namespace PlanetSurface.WeatherView;

/// <summary>
/// Ported from the weather prototype (bolts rise from this planet's cloud base). Lightning you can see coming: while a strike charges, its target glows and crackles (St
/// Elmo's fire) and the cloud above flickers; then a branching bolt, a flash that lights the
/// cloud round it, and a few re-strokes. Near and far differ: a near strike has its bolt, a wide
/// flash and a shadow-casting light; a far one is a glow inside its own cell, now and then a
/// thin bolt, no light on the ground; between them it fades by distance. Bolts are camera-facing
/// strips, one unbroken strip per channel (a quad per segment overlapped its neighbours' ends and
/// drew rungs across the bolt), rebuilt only while visible.
/// </summary>
public partial class LightningFx : Node3D
{
    public IWeatherScene Fx;

    /// <summary>0..~1.5: how bright the last strike's glow in the cloud is right now (the sky lights the cloud round <see cref="FlashAt"/> with it).</summary>
    public float Flash { get; private set; }
    /// <summary>Where in the world the cloud is lit from: over the last strike, or the cell flashing within.</summary>
    public Vector3 FlashAt { get; private set; } = new(0f, -1e6f, 0f);

    /// <summary>How near a strike is, 1 (bolt, wide flash, shadows) to 0 (a glow in its cloud), by metres from the eye.</summary>
    public static float Nearness(float metres) => 1f - Mathf.SmoothStep(NearUpTo, FarFrom, metres);
    public const float NearUpTo = 700f, FarFrom = 2500f;

    /// <summary>Tests and screenshots: holds the last strike as it is now; and lets no other strike or cloud flash in.</summary>
    public bool Held, Closed;
    /// <summary>How often a far strike's bolt shows below its cloud (the rest stay hidden in it).</summary>
    public float FarBoltChance = 0.35f;
    /// <summary>The last strike's nearness, and whether its bolt is showing (a far one's only now and then).</summary>
    public float LastNearness { get; private set; }
    public bool BoltShown => _bolt.Visible;

    /// <summary>How much of a strike's real light reaches the eye's surroundings, 0 to 1: a game with rooms says none in a sealed one.</summary>
    public Func<float> LightShare = () => 1f;

    /// <summary>How bright the strike's real light is now.</summary>
    public float StrikeEnergy => _strikeLight.Visible ? _strikeLight.LightEnergy : 0f;

    const float BoltLife = 0.55f;
    static readonly Color Core = new(1f, 1f, 1f, 1f);

    ShaderMaterial _boltMaterial;
    StandardMaterial3D _glowMaterial;
    ImmediateMesh _boltMesh;
    MeshInstance3D _bolt;
    OmniLight3D _strikeLight;
    readonly List<Channel> _segments = new();
    readonly List<Channel> _crackles = new();
    float _boltAge = 99f;
    bool _drawBolt;
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
        _boltMaterial.SetShaderParameter(PlanetSurface.Fx.Names.Of("tint"), new Color("e6dcff"));
        _boltMaterial.SetShaderParameter(PlanetSurface.Fx.Names.Of("energy"), 6f);
        _boltMaterial.SetShaderParameter(PlanetSurface.Fx.Names.Of("softness"), 0.5f);
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
        _strikeLight = new OmniLight3D { LightColor = new Color("d8ccff"), OmniRange = 120f, LightEnergy = 0f, ShadowEnabled = true, Visible = false, TopLevel = true }; // walls stop it
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
        var light = new OmniLight3D { LightColor = new Color("b8a6ff"), OmniRange = 26f, ShadowEnabled = true };
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

        if (!Held) _boltAge += dt;
        float stroke = Stroke(_boltAge);
        float near = Nearness(camera.DistanceTo(_boltTarget));
        // The glow in the cloud: a far strike's stays in its own cell (the sky keeps it round FlashAt), a near one's is wider and brighter.
        Flash = Mathf.Max(stroke * (0.45f + 0.65f * near), Flash - dt * 4f);
        // Light on the ground, and its shadows, only from a near strike.
        _strikeLight.LightEnergy = stroke * 14f * near * near * LightShare();
        _strikeLight.Visible = _strikeLight.LightEnergy > 0.01f;
        _strikeLight.ShadowEnabled = near > 0.3f;
        _bolt.Visible = _drawBolt && _boltAge < BoltLife;
        if (_bolt.Visible)
        {
            // Far off, a thin dim channel with no halo to bloom into a flash.
            _boltMaterial.SetShaderParameter(PlanetSurface.Fx.Names.Of("energy"), (2f + 7f * stroke) * (0.35f + 0.65f * near));
            DrawRibbons(_boltMesh, _segments, camera, 0.4f + 0.6f * stroke, 0.35f + 0.65f * near, near);
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
        Vector3 flickerAt = default;
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
            c.Light.LightEnergy = (0.5f + 5f * p * p) * pulse * LightShare();
            c.Sparks.AmountRatio = Mathf.Clamp(p * 1.4f - 0.2f, 0f, 1f);
            c.NextCrackle -= dt;
            if (c.NextCrackle <= 0f)
            {
                c.NextCrackle = Mathf.Lerp(0.2f, 0.05f, p);
                BuildCrackles(h.Target, p);
                DrawRibbons(c.Crackle, _crackles, camera, 0.3f + 0.7f * p, 1f, 1f);
            }
            float flicker = p * p * (_rng.Randf() < 0.15f ? 0.35f : 0.05f);
            if (flicker > skyFlicker) { skyFlicker = flicker; flickerAt = h.Target; }
        }
        for (int i = used; i < _charges.Count; i++) _charges[i].Root.Visible = false;
        if (!Closed && skyFlicker > Flash) // the cloud above it flickers as the charge builds
        {
            Flash = skyFlicker;
            FlashAt = new Vector3(flickerAt.X, Mathf.Max(Fx.CloudBaseY, flickerAt.Y + 150f) + 120f, flickerAt.Z);
        }
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
        if (Closed) return;
        _boltAge = 0f;
        _boltTarget = target;
        _strikeLight.GlobalPosition = target + Vector3.Up * 6f;
        Vector3 cloud = target + new Vector3(_rng.RandfRange(-90f, 90f), 0f, _rng.RandfRange(-90f, 90f));
        cloud.Y = Mathf.Max(Fx.CloudBaseY, target.Y + 150f);
        FlashAt = cloud + Vector3.Up * 120f;
        LastNearness = Nearness(Fx.Camera.GlobalPosition.DistanceTo(target));
        _drawBolt = LastNearness > 0.15f || _rng.Randf() < FarBoltChance; // far off, mostly hidden in the cloud
        _segments.Clear();
        Jag(_segments, cloud, target, 7, 1.1f, 1f, 0);
    }

    /// <summary>Lightning inside a cloud: no bolt, just the tower lighting up from within.</summary>
    public void CloudFlash(Vector3 at, float brightness)
    {
        if (Closed || brightness < Flash) return;
        FlashAt = at;
        Flash = brightness;
    }

    /// <summary>Midpoint displacement: a jagged path, with branches forking off and fading.</summary>
    void Jag(List<Channel> into, Vector3 from, Vector3 to, int levels, float width, float alpha, int depth)
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

        into.Add(new Channel(points, width, alpha));
        for (int i = 0; i < points.Count - 1; i++)
        {
            if (depth < 2 && levels > 3 && _rng.Randf() < (depth == 0 ? 0.09f : 0.05f))
            {
                Vector3 down = (to - from).Normalized();
                float length = from.DistanceTo(to) * _rng.RandfRange(0.1f, 0.25f);
                Vector3 side = new Vector3(_rng.RandfRange(-1f, 1f), 0f, _rng.RandfRange(-1f, 1f)).Normalized();
                Vector3 end = points[i] + (down * 0.6f + side * 0.8f).Normalized() * length;
                end.Y = Mathf.Max(end.Y, to.Y + (from.Y - to.Y) * 0.25f); // forks spread out under the cloud and die in the air, never into the ground
                Jag(into, points[i], end, levels - 2, width * 0.45f, alpha * 0.55f, depth + 1);
            }
        }
    }

    /// <summary>One channel of a bolt: its jagged path, its width and how bright it is.</summary>
    public sealed record Channel(List<Vector3> Points, float Width, float Alpha);

    /// <summary>A bolt from <paramref name="from"/> to <paramref name="to"/>, with its forks, for tests.</summary>
    public static List<Channel> TestBolt(Vector3 from, Vector3 to, ulong seed)
    {
        var fx = new LightningFx();
        fx._rng.Seed = seed;
        var into = new List<Channel>();
        fx.Jag(into, from, to, 7, 1.1f, 1f, 0);
        fx.Free();
        return into;
    }

    /// <summary>
    /// A channel's strip: at each point of its path a left and a right edge, shared by the pieces
    /// either side of it, so the strip runs unbroken; it narrows to a point at both ends, so nothing
    /// lies across the channel anywhere.
    /// </summary>
    public static (Vector3 left, Vector3 right)[] Strip(Channel c, Vector3 camera, float width)
    {
        List<Vector3> p = c.Points;
        var edges = new (Vector3, Vector3)[p.Count];
        Vector3 last = Vector3.Zero;
        for (int i = 0; i < p.Count; i++)
        {
            Vector3 along = p[Math.Min(i + 1, p.Count - 1)] - p[Math.Max(i - 1, 0)];
            Vector3 side = along.Cross(camera - p[i]);
            side = side.LengthSquared() > 1e-12f ? side.Normalized() : last;
            if (i > 0 && side.Dot(last) < 0f) side = -side; // never twisted over between two points
            last = side;
            float f = i / (float)(p.Count - 1);
            float taper = Mathf.Min(1f, Mathf.Min(f, 1f - f) * 12f) * Mathf.Lerp(1f, 0.6f, f);
            float scale = Mathf.Max(1f, camera.DistanceTo(p[i]) * 0.004f); // stays a few pixels wide far away
            Vector3 half = side * c.Width * width * scale * taper * 0.5f;
            edges[i] = (p[i] - half, p[i] + half);
        }
        return edges;
    }

    /// <summary>Each channel as strips turned to face the camera: a hot core plus a soft halo (<paramref name="halo"/> 0 drops it).</summary>
    static void DrawRibbons(ImmediateMesh mesh, List<Channel> channels, Vector3 camera, float brightness, float width, float halo)
    {
        mesh.ClearSurfaces();
        if (channels.Count == 0) return;
        mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);
        foreach (Channel c in channels)
        {
            StripMesh(mesh, Strip(c, camera, width), new Color(1, 1, 1, c.Alpha * brightness));
            if (halo > 0.01f) StripMesh(mesh, Strip(c, camera, width * 5f), new Color(0.7f, 0.6f, 1f, c.Alpha * brightness * 0.08f * halo));
        }
        mesh.SurfaceEnd();
    }

    static void StripMesh(ImmediateMesh mesh, (Vector3 left, Vector3 right)[] e, Color color)
    {
        void V(Vector3 p, float u)
        {
            mesh.SurfaceSetColor(color);
            mesh.SurfaceSetUV(new Vector2(u, 0));
            mesh.SurfaceAddVertex(p);
        }
        for (int i = 0; i < e.Length - 1; i++)
        {
            V(e[i].left, 0); V(e[i].right, 1); V(e[i + 1].right, 1);
            V(e[i].left, 0); V(e[i + 1].right, 1); V(e[i + 1].left, 0);
        }
    }
}
