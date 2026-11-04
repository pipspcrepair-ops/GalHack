using Encounters.Director;
using Godot;
using static Encounters.Palette;

namespace Encounters;

/// <summary>
/// How a zone looks: the painted sky, the sun and the ambient light. Presentation only, so it
/// lives here rather than in the zone data the director reads.
/// </summary>
public sealed class ZoneLook
{
    public Color Deep, Band, NebulaA, NebulaB;
    public float NebulaStrength = 0.7f, BandWidth = 0.4f;
    public Vector3 NebulaAxis = new(0.2f, 0.9f, 0.3f);
    public bool Planet, Rings;
    public Vector3 PlanetDir;
    public float PlanetRadius;
    public Color PlanetA, PlanetB, PlanetC, Air, RingColor;
    public Color SunColor;
    public float SunEnergy, SunSize, SunGlow = 0.35f, SunDisc = 40f;
    /// <summary>Direction towards the sun.</summary>
    public Vector3 SunTowards;
    public float AmbientEnergy;
    public Color DustTint;

    public static ZoneLook For(string zoneId) => zoneId == "drift-deep" ? Drift : Veyra;

    /// <summary>Veyra orbit: a banded ochre giant with rings, a warm K-class sun, teal and rust nebula.</summary>
    static readonly ZoneLook Veyra = new()
    {
        Deep = new Color("050a1a"), Band = new Color("1a1838"),
        NebulaA = new Color("c8552a"), NebulaB = new Color("1f7a80"), NebulaStrength = 0.22f,
        NebulaAxis = new Vector3(-0.35f, 0.85f, 0.4f), BandWidth = 0.3f,
        Planet = true, Rings = true,
        PlanetDir = new Vector3(0.6f, -0.36f, -0.72f), PlanetRadius = 0.3f,
        PlanetA = new Color("d07a3a"), PlanetB = new Color("e6c08a"), PlanetC = new Color("9c3b24"),
        Air = new Color("ffb070"), RingColor = new Color("e8d5b0"),
        SunColor = new Color("ffe2b8"), SunEnergy = 1.7f, SunSize = 0.018f,
        SunTowards = new Vector3(-0.72f, 0.45f, 0.52f),
        AmbientEnergy = 0.55f, DustTint = new Color("ffe2c0"),
    };

    /// <summary>The Ashen Drift: a swollen red dwarf, rose and steel-blue nebula, one dark world far off.</summary>
    static readonly ZoneLook Drift = new()
    {
        Deep = new Color("0b0512"), Band = new Color("2c0d2c"),
        NebulaA = new Color("a02a5e"), NebulaB = new Color("355f8f"), NebulaStrength = 0.45f,
        NebulaAxis = new Vector3(0.5f, 0.6f, -0.6f), BandWidth = 0.5f,
        Planet = true, Rings = false,
        PlanetDir = new Vector3(-0.75f, 0.28f, -0.6f), PlanetRadius = 0.09f,
        PlanetA = new Color("4a3b3f"), PlanetB = new Color("6b5552"), PlanetC = new Color("2b2228"),
        Air = new Color("ff6a4a"),
        SunColor = new Color("ff8a5c"), SunEnergy = 1.35f, SunSize = 0.07f, SunGlow = 0.45f, SunDisc = 1.6f,
        SunTowards = new Vector3(0.82f, 0.28f, -0.5f),
        AmbientEnergy = 0.7f, DustTint = new Color("ffb3c8"),
    };

    public void Apply(ShaderMaterial sky, DirectionalLight3D sun, Environment environment)
    {
        sky.SetShaderParameter("deep_color", Deep);
        sky.SetShaderParameter("band_color", Band);
        sky.SetShaderParameter("nebula_a", NebulaA);
        sky.SetShaderParameter("nebula_b", NebulaB);
        sky.SetShaderParameter("nebula_strength", NebulaStrength);
        sky.SetShaderParameter("nebula_axis", NebulaAxis.Normalized());
        sky.SetShaderParameter("band_width", BandWidth);
        sky.SetShaderParameter("planet_on", Planet);
        sky.SetShaderParameter("planet_dir", PlanetDir.Normalized());
        sky.SetShaderParameter("planet_radius", PlanetRadius);
        sky.SetShaderParameter("planet_a", PlanetA);
        sky.SetShaderParameter("planet_b", PlanetB);
        sky.SetShaderParameter("planet_c", PlanetC);
        sky.SetShaderParameter("planet_air", Air);
        sky.SetShaderParameter("rings_on", Rings);
        sky.SetShaderParameter("ring_color", RingColor);
        sky.SetShaderParameter("sun_size", SunSize);
        sky.SetShaderParameter("sun_glow", SunGlow);
        sky.SetShaderParameter("sun_disc_energy", SunDisc);

        sun.LightColor = SunColor;
        sun.LightEnergy = SunEnergy;
        Vector3 towards = SunTowards.Normalized();
        sun.GlobalBasis = Basis.LookingAt(-towards, Vector3.Up);
        environment.AmbientLightEnergy = AmbientEnergy;
    }
}

/// <summary>
/// Builds a zone's fixed scenery from the same zone data the director reads: a station for a
/// station POI, rocks for an asteroid POI, a relay beacon for a player base.
/// </summary>
public static class ZoneScenery
{
    public static Node3D Build(ZoneDef zone, ZoneLook look)
    {
        var root = new Node3D { Name = "Scenery" };
        int seed = 1;
        foreach (ZoneCircle circle in zone.Circles)
        {
            Node3D piece = circle.Kind switch
            {
                CircleKind.Poi when circle.Poi == "station" => new Station(),
                CircleKind.Poi when circle.Poi == "asteroids" => Asteroids(circle, seed++),
                CircleKind.Base => RelayBeacon(),
                _ => null,
            };
            if (piece == null) continue;
            piece.Position = new Vector3(circle.X, 0f, circle.Z);
            root.AddChild(piece);
        }
        root.AddChild(Dust(look.DustTint));
        return root;
    }

    /// <summary>The players' relay beacon: player-built, so it is permanent and nothing spawns near it.</summary>
    static Node3D RelayBeacon()
    {
        var b = new BlockBuilder("beacon");
        b.Box(0, 0, 0, 8, 60, 8, Cream);
        b.Stripes(new Vector3(0, -22, 0), new Vector3(8.3f, 12, 8.3f), 6, 1, Black, Yellow);
        b.Box(0, 32, 0, 26, 2, 26, Teal);
        b.Box(0, 40, 0, 2, 16, 2, Steel);
        b.Box(0, 49, 0, 3, 3, 3, Lasting, 2f);
        foreach (float sx in new[] { -1f, 1f })
            b.Box(sx * 12, -10, 0, 16, 1.5f, 4, Orange);
        var node = new Node3D { Name = "RelayBeacon" };
        node.AddChild(new MeshInstance3D { Mesh = b.Commit().Mesh, MaterialOverride = Visuals.Hull });
        MeshInstance3D light = Visuals.Flare(new Color(1.2f, 3f, 0.8f), 40f);
        light.Position = new Vector3(0, 49, 0);
        node.AddChild(light);
        return node;
    }

    /// <summary>A field of chunky, faceted rocks: one draw call per rock shape.</summary>
    static Node3D Asteroids(ZoneCircle circle, int seed)
    {
        var node = new Node3D { Name = "Asteroids" };
        var rng = new RandomNumberGenerator { Seed = (ulong)(seed * 7919) };
        var material = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 1f };
        Color[] tones = { new("8a6f5a"), new("a4836a"), new("765f52"), new("b8916a"), new("6a5a58") };
        for (int shape = 0; shape < 3; shape++)
        {
            const int count = 60;
            var multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,
                Mesh = Rock(rng, shape),
                InstanceCount = count,
            };
            for (int i = 0; i < count; i++)
            {
                float angle = rng.RandfRange(0, Mathf.Tau), radius = circle.R * Mathf.Sqrt(rng.Randf()) * 1.15f;
                var at = new Vector3(Mathf.Cos(angle) * radius, rng.RandfRange(-260, 260), Mathf.Sin(angle) * radius);
                float size = Mathf.Lerp(14f, 150f, Mathf.Pow(rng.Randf(), 2.2f));
                var scale = new Vector3(rng.RandfRange(0.7f, 1.3f), rng.RandfRange(0.6f, 1.1f), rng.RandfRange(0.8f, 1.5f)) * size;
                var basis = Basis.FromEuler(new Vector3(rng.RandfRange(0, Mathf.Tau), rng.RandfRange(0, Mathf.Tau), rng.RandfRange(0, Mathf.Tau))).Scaled(scale);
                multimesh.SetInstanceTransform(i, new Transform3D(basis, at));
                multimesh.SetInstanceColor(i, tones[rng.RandiRange(0, tones.Length - 1)].SrgbToLinear());
            }
            node.AddChild(new MultiMeshInstance3D { Multimesh = multimesh, MaterialOverride = material });
        }
        return node;
    }

    /// <summary>A lumpy, flat-shaded rock: a subdivided icosahedron pushed in and out at random.</summary>
    static ArrayMesh Rock(RandomNumberGenerator rng, int variant)
    {
        float t = (1f + Mathf.Sqrt(5f)) / 2f;
        Vector3[] v =
        {
            new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0), new(0, -1, t), new(0, 1, t),
            new(0, -1, -t), new(0, 1, -t), new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
        };
        int[] f =
        {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
        };
        var noise = new FastNoiseLite { Seed = 31 + variant * 17, Frequency = 1.3f };
        Vector3 Bump(Vector3 p)
        {
            p = p.Normalized();
            return p * (0.5f + 0.18f * noise.GetNoise3Dv(p * 2f) + rng.RandfRange(-0.04f, 0.04f));
        }
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetSmoothGroup(uint.MaxValue); // flat shading: chunky facets
        for (int i = 0; i < f.Length; i += 3)
        {
            Vector3 a = v[f[i]], b = v[f[i + 1]], c = v[f[i + 2]];
            Vector3 ab = Bump((a + b) / 2f), bc = Bump((b + c) / 2f), ca = Bump((c + a) / 2f);
            Vector3 pa = Bump(a), pb = Bump(b), pc = Bump(c);
            foreach (var (x, y, z) in new[] { (pa, ab, ca), (ab, pb, bc), (ca, bc, pc), (ab, bc, ca) })
            {
                // The icosahedron is wound counter-clockwise; Godot's front faces are clockwise.
                st.AddVertex(x);
                st.AddVertex(z);
                st.AddVertex(y);
            }
        }
        st.GenerateNormals();
        return st.Commit();
    }

    /// <summary>Specks of dust that wrap round the camera, so flying always feels like moving.</summary>
    static Node3D Dust(Color tint)
    {
        const int count = 700;
        const float box = 700f;
        var rng = new RandomNumberGenerator { Seed = 5 };
        var multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = new QuadMesh { Size = Vector2.One },
            InstanceCount = count,
        };
        for (int i = 0; i < count; i++)
            multimesh.SetInstanceTransform(i, new Transform3D(Basis.Identity,
                new Vector3(rng.RandfRange(0, box), rng.RandfRange(0, box), rng.RandfRange(0, box))));
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/dust.gdshader") };
        material.SetShaderParameter("box_size", box);
        material.SetShaderParameter("tint", tint);
        return new MultiMeshInstance3D
        {
            Name = "Dust",
            Multimesh = multimesh,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            // The shader moves the specks to wherever the camera is, so never cull them.
            CustomAabb = new Aabb(new Vector3(-1e6f, -1e6f, -1e6f), new Vector3(2e6f, 2e6f, 2e6f)),
        };
    }
}

/// <summary>
/// Halcyon Station, block-built and huge: a striped spindle, a slowly turning ring with spokes
/// and docking arms, and lit windows. Traders dock just outside the ring.
/// </summary>
public partial class Station : Node3D
{
    Node3D _ring;

    public override void _Ready()
    {
        Name = "Station";
        var core = new BlockBuilder("station-core");
        core.Box(0, 0, 0, 60, 300, 60, Cream);
        foreach (float y in new[] { -150f, 150f })
        {
            core.Box(0, y, 0, 96, 36, 96, Orange);
            core.Box(0, y, 0, 97, 6, 97, Cream);
            core.Stripes(new Vector3(0, y - 15, 0), new Vector3(96.5f, 6, 96.5f), 12, 0, Black, Yellow);
        }
        foreach (float y in new[] { -215f, 215f })
            core.Box(0, y, 0, 44, 70, 44, Cream);
        core.Box(0, 262, 0, 26, 24, 26, Dark);
        core.Box(0, -262, 0, 32, 24, 32, Dark);
        core.Box(0, 330, 0, 4, 120, 4, Steel);
        core.Box(0, 300, 0, 40, 3, 3, Steel);
        core.Box(0, 392, 0, 6, 6, 6, Red, 2f);
        core.Checker(new Vector3(0, 60, -30.3f), new Vector3(60, 80, 0.6f), 6, 8, Black, Cream);
        core.Box(0, 0, -30.4f, 62, 16, 0.6f, Red);
        for (int row = 0; row < 9; row++) // lit windows up the spindle
            foreach (float sx in new[] { -1f, 1f })
                core.Box(sx * 30.3f, -110 + row * 14, 0, 0.5f, 3, 46, Warm, 1f);
        AddChild(new MeshInstance3D { Mesh = core.Commit().Mesh, MaterialOverride = Visuals.Hull });

        var ring = new BlockBuilder("station-ring");
        const int segments = 24;
        const float radius = 250f;
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.Tau / segments;
            var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            var basis = new Basis(Vector3.Up, -a - Mathf.Pi / 2f); // segment's long axis along the ring
            Color panel = i % 6 == 0 ? Orange : i % 2 == 0 ? Cream : new Color("d8ceb2");
            ring.Box(dir * radius, new Vector3(68, 34, 44), panel, 0f, basis);
            ring.Box(dir * (radius + 22.3f), new Vector3(60, 5, 0.6f), Warm, 1f, basis);
            ring.Box(dir * radius + Vector3.Up * 17.4f, new Vector3(68, 1, 30), i % 2 == 0 ? Teal : Navy, 0f, basis);
        }
        for (int i = 0; i < 4; i++)
        {
            float a = i * Mathf.Tau / 4f + 0.13f;
            var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            var along = new Basis(Vector3.Up, -a);
            ring.Box(dir * 140f, new Vector3(190, 12, 12), Steel, 0f, along); // spoke
            float b = a + Mathf.Pi / 4f;
            var arm = new Vector3(Mathf.Cos(b), 0, Mathf.Sin(b));
            var armBasis = new Basis(Vector3.Up, -b);
            ring.Box(arm * 330f, new Vector3(120, 14, 14), Cream, 0f, armBasis); // docking arm
            ring.Box(arm * 386f, new Vector3(10, 16, 16), Yellow, 0f, armBasis);
            ring.Box(arm * 392f, new Vector3(3, 6, 6), Lasting, 2f, armBasis);
        }
        _ring = new MeshInstance3D { Mesh = ring.Commit().Mesh, MaterialOverride = Visuals.Hull };
        AddChild(_ring);
    }

    public override void _PhysicsProcess(double delta) => _ring.RotateY((float)delta * 0.02f);
}
