using System;
using System.Collections.Generic;
using Godot;
using Environment = Godot.Environment;

namespace Wildlife;

/// <summary>
/// A patch of space for the space fauna: a nebula, an asteroid belt or a derelict's graveyard,
/// with its sky, its star, its rocks or wreck, and the creatures that live there, rolled by the
/// <see cref="SpaceFaunaGenerator"/> and run by a <see cref="SpaceSim"/>. Built in code for the
/// screenshot views; the encounter director is what brings these creatures to players in the game.
/// </summary>
public partial class SpaceScene : Node3D
{
    public WildlifeData Data { get; private set; }
    public SpaceFaunaGenerator Generator { get; private set; }
    public SpaceSim Sim { get; private set; }
    public SpaceView View { get; private set; }
    public SpaceFauna Fauna { get; private set; }
    public Camera3D Camera { get; private set; }
    public DirectionalLight3D Star { get; private set; }
    public Habitat Habitat { get; private set; }
    /// <summary>Screenshots hold the simulation still while they stage a shot.</summary>
    public bool Paused;

    ShaderMaterial _sky;
    Environment _env;
    StandardMaterial3D _rock, _steel, _rust, _dark;

    public override void _Ready()
    {
        Data = DataFiles.LoadWildlife();
        Generator = new SpaceFaunaGenerator(DataFiles.LoadSpace(), Data.Combat);
        Sim = new SpaceSim(Data);
        View = new SpaceView { Name = "Fauna" };
        AddChild(View);
        View.Bind(Sim);
        Camera = new Camera3D { Name = "Camera", Fov = 62f, Near = 0.1f, Far = 20000f };
        AddChild(Camera);
        Camera.MakeCurrent();
        _sky = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/space_sky.gdshader") };
        _env = new Environment
        {
            BackgroundMode = Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = _sky, RadianceSize = Sky.RadianceSizeEnum.Size128 },
            AmbientLightSource = Environment.AmbientSource.Sky,
            AmbientLightEnergy = 0.55f,
            TonemapMode = Environment.ToneMapper.Filmic,
            TonemapExposure = 1.05f,
            GlowEnabled = true,
            GlowIntensity = 0.85f,
            GlowBloom = 0.12f,
            GlowHdrThreshold = 0.9f,
            GlowBlendMode = Environment.GlowBlendModeEnum.Additive,
        };
        AddChild(new WorldEnvironment { Environment = _env });
        Star = new DirectionalLight3D { Name = "Star", ShadowEnabled = true, DirectionalShadowMaxDistance = 600f };
        AddChild(Star);
        _rock = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.95f };
        _steel = new StandardMaterial3D { AlbedoColor = new Color("3a4148"), Metallic = 0.6f, Roughness = 0.62f };
        _rust = new StandardMaterial3D { AlbedoColor = new Color("5a3426"), Metallic = 0.2f, Roughness = 0.9f };
        _dark = new StandardMaterial3D { AlbedoColor = new Color("0d0f12"), Roughness = 0.9f };
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Paused) Sim.Step((float)delta);
        View.Sync(Camera.GlobalPosition);
    }

    /// <summary>Sets the place up: its look, its props and its fauna for the region seed.</summary>
    public void Build(Habitat habitat, ulong region)
    {
        Habitat = habitat;
        Fauna = Generator.Generate(region, habitat);
        switch (habitat)
        {
            case Habitat.AsteroidField:
                Look(new Color("06070b"), new Color("8a4b2b"), new Color("2b4a66"), 0.45f, 0.55f, new Color("ffd8a8"), 1.5f, new Vector3(-0.5f, 0.35f, 0.75f));
                BuildBelt(Rng.For(region, "belt"));
                break;
            case Habitat.Derelict:
                Look(new Color("07050a"), new Color("6a1f2c"), new Color("27304f"), 0.4f, 0.7f, new Color("ffc8a8"), 1.6f, new Vector3(0.85f, 0.45f, -0.25f));
                _env.AmbientLightEnergy = 0.8f;
                BuildWreck();
                break;
            default:
                // A nebula: glowing gas all round and a haze that swallows the distance.
                Look(new Color("0a0414"), new Color("6a2f8f"), new Color("1f5e86"), 1.0f, 0.6f, new Color("d8e4ff"), 1.0f, new Vector3(0.3f, 0.6f, -0.7f));
                _env.AmbientLightEnergy = 0.35f;
                _env.FogEnabled = true;
                _env.FogLightColor = new Color("3a2a5a");
                _env.FogDensity = 0.0014f;
                _env.FogSkyAffect = 0f;
                break;
        }
    }

    void Look(Color deep, Color a, Color b, float strength, float dust, Color star, float energy, Vector3 towards)
    {
        _sky.SetShaderParameter("deep_color", deep);
        _sky.SetShaderParameter("cloud_a", a);
        _sky.SetShaderParameter("cloud_b", b);
        _sky.SetShaderParameter("cloud_strength", strength);
        _sky.SetShaderParameter("dust", dust);
        Star.LightColor = star;
        Star.LightEnergy = energy;
        Star.GlobalBasis = Basis.LookingAt(-towards.Normalized(), Vector3.Up);
    }

    /// <summary>Rocks of every size, lumpy and cratered, scattered through a few hundred metres.</summary>
    void BuildBelt(Rng rng)
    {
        for (int i = 0; i < 46; i++)
        {
            float radius = i < 6 ? rng.Range(28f, 60f) : rng.Range(3f, 22f);
            Vector3 at = new(rng.Range(-380f, 380f), rng.Range(-90f, 90f), rng.Range(-420f, 120f));
            if (at.Length() < radius + 30f) continue; // keep the middle open for the camera
            var mesh = new MeshInstance3D { Mesh = RockMesh(rng, radius), MaterialOverride = _rock };
            AddChild(mesh);
            mesh.Position = at;
            mesh.Rotation = new Vector3(rng.Range(0f, 6f), rng.Range(0f, 6f), rng.Range(0f, 6f));
            Sim.Asteroids.Add(new Asteroid { Center = at, Radius = radius * 0.92f });
        }
    }

    /// <summary>A lumpy rock: a subdivided icosphere pushed in and out by noise, coloured by its own hollows.</summary>
    static ArrayMesh RockMesh(Rng rng, float radius)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();
        Icosphere(3, verts, tris);
        float seed = rng.Range(0f, 100f), squash = rng.Range(0.65f, 1f);
        Color light = new Color("6f6359").Lerp(new Color("4d5560"), rng.Float()), dark = new Color("2a2522");
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var heights = new float[verts.Count];
        for (int i = 0; i < verts.Count; i++)
        {
            Vector3 v = verts[i];
            float n = Noise(v * 1.6f + Vector3.One * seed) * 0.35f + Noise(v * 4.2f + Vector3.One * seed * 2f) * 0.12f;
            heights[i] = n;
            verts[i] = v * radius * (0.85f + n) * new Vector3(1f, squash, 1f);
        }
        foreach (int i in tris)
        {
            st.SetColor(dark.Lerp(light, Math.Clamp(0.4f + heights[i] * 2.2f, 0f, 1f)));
            st.AddVertex(verts[i]);
        }
        st.Index();
        st.GenerateNormals();
        return st.Commit();
    }

    static float Noise(Vector3 p)
    {
        Vector3 i = p.Floor(), f = p - i;
        f = f * f * (Vector3.One * 3f - f * 2f);
        float H(Vector3 c) => Mathf.PosMod(MathF.Sin(c.Dot(new Vector3(127.1f, 311.7f, 74.7f))) * 43758.547f, 1f);
        float x00 = Mathf.Lerp(H(i), H(i + new Vector3(1, 0, 0)), f.X), x10 = Mathf.Lerp(H(i + new Vector3(0, 1, 0)), H(i + new Vector3(1, 1, 0)), f.X);
        float x01 = Mathf.Lerp(H(i + new Vector3(0, 0, 1)), H(i + new Vector3(1, 0, 1)), f.X), x11 = Mathf.Lerp(H(i + new Vector3(0, 1, 1)), H(i + new Vector3(1, 1, 1)), f.X);
        return Mathf.Lerp(Mathf.Lerp(x00, x10, f.Y), Mathf.Lerp(x01, x11, f.Y), f.Z) - 0.5f;
    }

    static void Icosphere(int subdivisions, List<Vector3> verts, List<int> tris)
    {
        float t = (1f + MathF.Sqrt(5f)) / 2f;
        foreach (var v in new[] { new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0), new Vector3(0, -1, t), new Vector3(0, 1, t),
                     new Vector3(0, -1, -t), new Vector3(0, 1, -t), new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1) })
            verts.Add(v.Normalized());
        tris.AddRange(new[] { 0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1 });
        for (int s = 0; s < subdivisions; s++)
        {
            var cache = new Dictionary<long, int>();
            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (cache.TryGetValue(key, out int m)) return m;
                verts.Add(((verts[a] + verts[b]) * 0.5f).Normalized());
                return cache[key] = verts.Count - 1;
            }
            var next = new List<int>(tris.Count * 4);
            for (int i = 0; i < tris.Count; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2], ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            tris.Clear();
            tris.AddRange(next);
        }
    }

    /// <summary>
    /// A dead freighter, 60 m of dark plating: its hull a box the leeches crawl over, with a
    /// bridge, an engine block, torn plates, exposed ribs round a breach and a red emergency light.
    /// </summary>
    void BuildWreck()
    {
        var hull = new Hull { Center = Vector3.Zero, Basis = new Basis(Vector3.Up, 0.5f) * new Basis(Vector3.Back, 0.12f), Half = new Vector3(8f, 4.5f, 30f) };
        Sim.Hulls.Add(hull);
        var wreck = new Node3D { Name = "Wreck", Transform = new Transform3D(hull.Basis, hull.Center) };
        AddChild(wreck);
        void Box(Vector3 size, Vector3 at, Material m, Vector3 rot = default)
        {
            wreck.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = m, Position = at, RotationDegrees = rot });
        }
        Box(hull.Half * 2f, Vector3.Zero, _steel);
        // Plating seams and a rust-streaked band down each flank.
        for (int i = -5; i <= 5; i++) Box(new Vector3(16.2f, 9.2f, 0.3f), new Vector3(0, 0, i * 5.2f), _dark);
        Box(new Vector3(16.3f, 1.6f, 44f), new Vector3(0, -2.2f, 3f), _rust);
        // The bridge at the bow, half torn away; the engine block at the stern.
        Box(new Vector3(9f, 5f, 8f), new Vector3(0, 6.5f, -22f), _steel);
        Box(new Vector3(7f, 1.2f, 3f), new Vector3(1.5f, 9.2f, -23f), _rust, new Vector3(0, 0, 14f));
        Box(new Vector3(14f, 10f, 9f), new Vector3(0, 0, 33f), _steel);
        for (int e = -1; e <= 1; e += 2) Box(new Vector3(4.5f, 4.5f, 5f), new Vector3(e * 4f, 0, 39f), _dark);
        // A breach amidships: blackened plates bent outwards, ribs showing.
        Box(new Vector3(0.4f, 5.5f, 10f), new Vector3(8.1f, 0.6f, 4f), _dark);
        for (int r = 0; r < 5; r++) Box(new Vector3(0.6f, 6.5f, 0.5f), new Vector3(8.4f, 0.5f, -0.5f + r * 2.3f), _steel, new Vector3(0, 0, 8f + r * 3f));
        Box(new Vector3(0.3f, 4f, 6f), new Vector3(10f, 2f, 1f), _steel, new Vector3(0, 30f, 25f));
        Box(new Vector3(0.3f, 3f, 5f), new Vector3(9.6f, -2.5f, 7f), _rust, new Vector3(0, -20f, -30f));
        wreck.AddChild(new OmniLight3D { LightColor = new Color("ff3020"), LightEnergy = 2.5f, OmniRange = 14f, Position = new Vector3(7.5f, 0.5f, 4f) });
    }

    /// <summary>Spawns this place's fauna where it belongs: shoals in open space or by rocks, leeches on the wreck.</summary>
    public List<Creature> Populate(Vector3 around)
    {
        var all = new List<Creature>();
        var rng = Rng.For(Fauna.Seed, "space-population", (long)Habitat);
        foreach (Species s in Fauna.Species)
        {
            int count = rng.Int(s.GroupMin, s.GroupMax);
            if (s.Rig == RigKind.Clinger && Sim.Hulls.Count > 0) all.AddRange(Sim.SpawnOnHull(s, Sim.Hulls[0], count, rng));
            else if (s.Behaviour == Behaviour.TerritorialGiant) all.Add(Sim.Spawn(s, around + new Vector3(rng.Range(-200f, 200f), rng.Range(-40f, 40f), -420f)));
            else all.AddRange(Sim.SpawnShoal(s, around + new Vector3(rng.Range(-20f, 20f), rng.Range(-8f, 8f), rng.Range(-60f, -30f)), count));
        }
        return all;
    }
}
