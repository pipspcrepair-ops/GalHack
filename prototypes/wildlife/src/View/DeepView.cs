using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>
/// Shows (and sounds) the tells of a sim's ambushes and hunts, the things that make them fair:
/// grit trickling from a ceiling with a shadow gathering on the floor under it, a puff of dust
/// from a crack as something scrabbles inside the wall, a grille rattling, a door banging. The
/// place's own props (a grille, a door) can listen to <see cref="Shown"/> to shake. Effects are
/// placed in this node's own frame, the sim's: put it under the node the creatures are drawn in.
/// </summary>
public partial class TellView : Node3D
{
    WildSim _sim;
    public ProceduralAudio Audio;
    /// <summary>A tell was shown: places shake the grille or the door it came from.</summary>
    public event Action<TellEvent> Shown;

    sealed class Effect
    {
        public Node3D Node;
        public CpuParticles3D Particles;
        public MeshInstance3D Shadow;
        public Creature Follow;
        public float Age, Life, Fade;
    }

    readonly List<Effect> _effects = new();
    static StandardMaterial3D _dust, _shadow;

    public int Live => _effects.Count;

    public void Bind(WildSim sim)
    {
        if (_sim != null)
        {
            _sim.Told -= OnTell;
        }
        _sim = sim;
        if (sim != null) sim.Told += OnTell;
    }

    public override void _ExitTree() => Bind(null);

    void OnTell(TellEvent e)
    {
        Audio?.Tell(e);
        switch (e.Kind)
        {
            case TellKind.Dust:
                Dust(e.Position, e.Duration + 0.8f, 46, 0.45f);
                Shadow(e, e.Duration + 0.6f);
                break;
            case TellKind.Shadow:
                Shadow(e, e.Duration + 0.6f);
                break;
            case TellKind.Scratch:
                // Scrabbling inside the wall: a little grit shaken out where it passes.
                if (_effects.Count < 40) Dust(e.Position, 0.5f, 8, 0.25f);
                break;
            case TellKind.Rattle:
            case TellKind.Thump:
                Dust(e.Position, e.Duration + 0.4f, 24, 0.5f);
                break;
        }
        Shown?.Invoke(e);
    }

    void Dust(Vector3 at, float seconds, int amount, float spread)
    {
        _dust ??= new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(0.62f, 0.56f, 0.47f, 0.85f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            VertexColorUseAsAlbedo = true,
        };
        var p = new CpuParticles3D
        {
            Amount = amount,
            Lifetime = 1.3f,
            Mesh = new QuadMesh { Size = new Vector2(0.025f, 0.025f), Material = _dust },
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(spread, 0.04f, spread),
            Direction = Vector3.Down,
            Spread = 12f,
            InitialVelocityMin = 0.1f,
            InitialVelocityMax = 0.5f,
            Gravity = new Vector3(0f, -4.5f, 0f),
            ScaleAmountMin = 0.6f,
            ScaleAmountMax = 1.6f,
            Color = new Color(0.62f, 0.56f, 0.47f),
            Emitting = true,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(p);
        p.Position = at;
        _effects.Add(new Effect { Node = p, Particles = p, Life = seconds, Fade = 1.4f });
    }

    /// <summary>A soft dark patch on the floor under something on the ceiling, darkening as it gathers itself to drop.</summary>
    void Shadow(TellEvent e, float seconds)
    {
        _shadow ??= new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(0f, 0f, 0f, 0.5f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoTexture = SoftDisc(),
        };
        float r = MathF.Max(0.5f, e.Creature.Species.Body.Size * 0.6f);
        var m = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(r * 2f, r * 2f) },
            MaterialOverride = (Material)_shadow.Duplicate(),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(m);
        m.Position = e.Below + Vector3.Up * 0.03f;
        _effects.Add(new Effect { Node = m, Shadow = m, Follow = e.Creature, Life = seconds, Fade = 0.5f });
    }

    static ImageTexture _disc;

    static ImageTexture SoftDisc()
    {
        if (_disc != null) return _disc;
        const int n = 64;
        var img = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
            float a = Math.Clamp(1f - MathF.Sqrt(dx * dx + dy * dy), 0f, 1f);
            img.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
        }
        _disc = ImageTexture.CreateFromImage(img);
        return _disc;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        for (int i = _effects.Count - 1; i >= 0; i--)
        {
            Effect e = _effects[i];
            e.Age += dt;
            if (e.Particles != null && e.Age > e.Life) e.Particles.Emitting = false;
            if (e.Shadow != null)
            {
                // Grows darker as the tell runs, and stays under the thing that casts it.
                float k = Math.Clamp(e.Age / MathF.Max(0.1f, e.Life - e.Fade), 0f, 1f);
                float fade = e.Age > e.Life - e.Fade ? Math.Clamp((e.Life - e.Age) / e.Fade, 0f, 1f) : 1f;
                ((StandardMaterial3D)e.Shadow.MaterialOverride).AlbedoColor = new Color(0f, 0f, 0f, (0.25f + 0.45f * k) * fade);
                if (e.Follow != null && !e.Follow.Removed && e.Follow.Space != null)
                {
                    Vector3 c = Crawl.Center(e.Follow);
                    if (SpaceMath.FloorBelow(e.Follow.Space, c.X, c.Y - 0.2f, c.Z, 8f) is float y) e.Shadow.Position = new Vector3(c.X, y + 0.03f, c.Z);
                }
            }
            if (e.Age > e.Life + e.Fade)
            {
                e.Node.QueueFree();
                _effects.RemoveAt(i);
            }
        }
    }
}

/// <summary>
/// The marks a place's creatures leave on it: cracks too small to follow (dark slits low in the
/// walls), moss, fungus and scraps where subsisters feed, and nests: a resin-bound mound with a
/// clutch of pale eggs, or in a wreck a heap of scrap worked together, that grows as it is built.
/// </summary>
public partial class LairView : Node3D
{
    WildSim _sim;
    readonly List<(Nest nest, Node3D node)> _nests = new();
    readonly List<(FoodPatch food, Node3D node)> _food = new();

    static StandardMaterial3D Look(Color c, float rough = 0.85f, float glow = 0f, bool shiny = false) => new()
    {
        AlbedoColor = c,
        Roughness = shiny ? 0.25f : rough,
        Metallic = 0f,
        EmissionEnabled = glow > 0f,
        Emission = c,
        EmissionEnergyMultiplier = glow,
        ClearcoatEnabled = shiny,
        Clearcoat = shiny ? 0.6f : 0f,
    };

    public void Build(WildSim sim)
    {
        foreach (Node n in GetChildren()) n.QueueFree();
        _nests.Clear();
        _food.Clear();
        _sim = sim;
        var rng = new Rng(17);
        var crackMat = Look(new Color(0.02f, 0.015f, 0.01f), 1f);
        var lipMat = Look(new Color(0.16f, 0.13f, 0.1f), 1f);
        foreach (Crack k in sim.Cracks)
        {
            if (k.Kind != CrackKind.Crack) continue;
            // A jagged slit: a few dark slivers in a crooked line along the wall.
            var node = new Node3D();
            AddChild(node);
            Basis b = SpaceMath.Frame(Mathf.Abs(k.Normal.Y) < 0.8f ? Vector3.Up : Vector3.Forward, k.Normal);
            node.Transform = new Transform3D(b, k.Position + k.Normal * 0.005f);
            float len = k.Size * 2.2f;
            for (int i = 0; i < 4; i++)
            {
                float t = (i - 1.5f) / 4f;
                node.AddChild(new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(k.Size * (0.35f + 0.25f * (i % 2)), 0.02f, len / 3.2f) },
                    MaterialOverride = crackMat,
                    Position = new Vector3(rng.Range(-0.04f, 0.04f), 0f, t * len),
                    Rotation = new Vector3(0f, rng.Range(-0.5f, 0.5f), 0f),
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                });
            }
            node.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(k.Size * 1.3f, 0.012f, len * 1.1f) },
                MaterialOverride = lipMat,
                Position = new Vector3(0f, -0.004f, 0f),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }
        foreach (FoodPatch f in sim.Food) _food.Add((f, Food(f, rng)));
        foreach (Nest n in sim.Nests) _nests.Add((n, NestMound(n, rng)));
    }

    Node3D Food(FoodPatch f, Rng rng)
    {
        var node = new Node3D();
        AddChild(node);
        node.Position = f.Position;
        switch (f.Kind)
        {
            case Diet.Fungus:
            {
                var stalk = Look(new Color(0.78f, 0.76f, 0.7f), 0.9f);
                var cap = Look(new Color(0.72f, 0.86f, 0.9f), 0.6f, 0.35f);
                for (int i = 0; i < 9; i++)
                {
                    var at = new Vector3(rng.Range(-1f, 1f), 0f, rng.Range(-1f, 1f)) * f.Radius;
                    float h = rng.Range(0.08f, 0.3f);
                    node.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.012f, BottomRadius = 0.02f, Height = h, RadialSegments = 6 }, MaterialOverride = stalk, Position = at + Vector3.Up * h * 0.5f });
                    float r = rng.Range(0.04f, 0.1f);
                    node.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = r, Height = r * 0.8f, RadialSegments = 10, Rings = 5 }, MaterialOverride = cap, Position = at + Vector3.Up * h });
                }
                break;
            }
            case Diet.Scraps:
            {
                var rust = Look(new Color(0.42f, 0.26f, 0.15f), 0.8f);
                var steel = Look(new Color(0.36f, 0.37f, 0.38f), 0.5f);
                for (int i = 0; i < 8; i++)
                {
                    var at = new Vector3(rng.Range(-1f, 1f), 0.03f, rng.Range(-1f, 1f)) * f.Radius;
                    node.AddChild(new MeshInstance3D
                    {
                        Mesh = new BoxMesh { Size = new Vector3(rng.Range(0.05f, 0.25f), rng.Range(0.02f, 0.06f), rng.Range(0.05f, 0.2f)) },
                        MaterialOverride = i % 3 == 0 ? steel : rust,
                        Position = at,
                        Rotation = new Vector3(rng.Range(-0.3f, 0.3f), rng.Range(0f, 3f), rng.Range(-0.3f, 0.3f)),
                    });
                }
                break;
            }
            default:
            {
                // Moss: low, dull cushions in greys and greens, damp-looking.
                var moss = Look(new Color(0.24f, 0.3f, 0.17f), 1f);
                var moss2 = Look(new Color(0.33f, 0.36f, 0.22f), 1f);
                for (int i = 0; i < 12; i++)
                {
                    var at = new Vector3(rng.Range(-1f, 1f), 0f, rng.Range(-1f, 1f)) * f.Radius;
                    float r = rng.Range(0.08f, 0.22f);
                    node.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = r, Height = r * 0.55f, RadialSegments = 10, Rings = 5 }, MaterialOverride = i % 2 == 0 ? moss : moss2, Position = at });
                }
                break;
            }
        }
        return node;
    }

    Node3D NestMound(Nest n, Rng rng)
    {
        var node = new Node3D();
        AddChild(node);
        node.Position = n.Position;
        if (n.Derelict)
        {
            // Scrap worked into a heap and glued with dark resin: plates, pipe, cable.
            var resin = Look(new Color(0.16f, 0.09f, 0.06f), 0.3f, 0f, true);
            var rust = Look(new Color(0.4f, 0.24f, 0.13f), 0.8f);
            var steel = Look(new Color(0.33f, 0.34f, 0.36f), 0.45f);
            for (int i = 0; i < 18; i++)
            {
                float a = rng.Range(0f, MathF.Tau), d = rng.Range(0f, n.Radius * 0.9f);
                var at = new Vector3(MathF.Cos(a) * d, rng.Range(0f, 0.5f) * (1f - d / n.Radius), MathF.Sin(a) * d);
                node.AddChild(new MeshInstance3D
                {
                    Mesh = i % 4 == 0 ? new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.05f, Height = rng.Range(0.4f, 0.9f), RadialSegments = 8 } : new BoxMesh { Size = new Vector3(rng.Range(0.2f, 0.6f), 0.04f, rng.Range(0.15f, 0.45f)) },
                    MaterialOverride = i % 3 == 0 ? steel : rust,
                    Position = at,
                    Rotation = new Vector3(rng.Range(-0.7f, 0.7f), rng.Range(0f, 3f), rng.Range(-0.7f, 0.7f)),
                });
            }
            node.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = n.Radius * 0.8f, Height = n.Radius * 0.5f }, MaterialOverride = resin, Position = Vector3.Up * 0.02f });
        }
        else
        {
            // A low mound of resin-bound debris, ridged, with a clutch of pale eggs in its hollow.
            var resin = Look(new Color(0.22f, 0.13f, 0.1f), 0.35f, 0f, true);
            var resin2 = Look(new Color(0.3f, 0.2f, 0.14f), 0.5f);
            for (int i = 0; i < 10; i++)
            {
                float a = i * MathF.Tau / 10f + rng.Range(-0.2f, 0.2f);
                var at = new Vector3(MathF.Cos(a), 0f, MathF.Sin(a)) * n.Radius * 0.75f;
                float r = n.Radius * rng.Range(0.3f, 0.45f);
                node.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = r, Height = r * 0.9f, RadialSegments = 12, Rings = 6 }, MaterialOverride = i % 2 == 0 ? resin : resin2, Position = at });
            }
        }
        // The eggs: pale, faintly lit from within. More of them in a deeper nest.
        var egg = Look(new Color(0.86f, 0.82f, 0.7f), 0.4f, 0.25f, true);
        int eggs = 5 + n.Depth * 2;
        for (int i = 0; i < eggs; i++)
        {
            float a = rng.Range(0f, MathF.Tau), d = rng.Range(0f, n.Radius * 0.45f);
            node.AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 0.11f, Height = 0.3f, RadialSegments = 10, Rings = 6 },
                MaterialOverride = egg,
                Position = new Vector3(MathF.Cos(a) * d, 0.12f, MathF.Sin(a) * d),
                Rotation = new Vector3(rng.Range(-0.5f, 0.5f), 0f, rng.Range(-0.5f, 0.5f)),
            });
        }
        return node;
    }

    public override void _Process(double delta)
    {
        // Nests grow as they are built; eaten patches thin out.
        foreach (var (nest, node) in _nests)
        {
            float k = 0.45f + 0.55f * Math.Clamp(nest.Size, 0f, 2f);
            node.Scale = Vector3.One * k;
        }
        foreach (var (food, node) in _food) node.Scale = new Vector3(1f, 0.3f + 0.7f * Math.Clamp(food.Amount, 0f, 1f), 1f);
    }
}
