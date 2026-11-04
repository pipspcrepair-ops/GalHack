using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>Physics layers.</summary>
public static class Layers
{
    /// <summary>The ground, rocks and tree trunks.</summary>
    public const uint World = 1u << 0;
    /// <summary>The hovercraft's hull.</summary>
    public const uint Vessel = 1u << 1;
}

/// <summary>
/// The planet surface: ground mesh and collider built from the terrain's height function,
/// rocks (colliders plus nav-grid obstacles), the sky with a day/night cycle, and a big
/// neighbouring world hanging overhead. Restyled in place when you switch worlds; the shape
/// of the land stays the same.
/// </summary>
public partial class Planet : Node3D
{
    public const ulong LandSeed = 1977;
    public const float GridStep = 2f;

    public Terrain Terrain { get; private set; }
    public NavGrid Nav { get; private set; }
    public WorldStyle Style { get; private set; }

    MeshInstance3D _ground;
    readonly List<(MeshInstance3D mesh, int shade)> _rocks = new();
    StandardMaterial3D _rockA, _rockB, _farGround;
    ProceduralSkyMaterial _sky;
    Godot.Environment _env;
    DirectionalLight3D _sun, _moonLight;
    MeshInstance3D _moon;
    StandardMaterial3D _moonMaterial;
    float _shownDaylight = -1f;

    public void Build()
    {
        Terrain = new Terrain(LandSeed);
        Nav = new NavGrid(Terrain.HalfSize, 3f);
        BuildSky();
        BuildGround();
        BuildRocks();
    }

    /// <summary>Which way the sunlight travels (for staging shots with the light behind the camera).</summary>
    public Vector3 SunDirection => -_sun.GlobalBasis.Z;

    public void ApplyStyle(WorldStyle style)
    {
        Style = style;
        _ground.Mesh = GroundMesh(style);
        _farGround.AlbedoColor = style.GroundA.Darkened(0.1f);
        _rockA.AlbedoColor = style.RockA;
        _rockB.AlbedoColor = style.RockB;
        _env.FogLightColor = style.Fog;
        _env.FogDensity = style.FogDensity;
        _sun.LightColor = style.Sun;
        _moonLight.LightColor = style.Moon;
        _moonMaterial.AlbedoColor = style.Moon;
        _shownDaylight = -1f;
    }

    /// <summary>Moves the sun and blends the sky between day and night.</summary>
    public void UpdateSky(DayClock clock)
    {
        float t = clock.Time01;
        // The sun circles round a tilted axis: up in the east at 0.25, down in the west at 0.75.
        _sun.Rotation = new Vector3(-(t - 0.25f) * MathF.Tau, Mathf.DegToRad(35f), 0f);
        _moonLight.Rotation = new Vector3(-(t + 0.25f) * MathF.Tau, Mathf.DegToRad(-30f), 0f);
        float day = clock.Daylight;
        _sun.LightEnergy = 1.2f * day;
        _sun.Visible = day > 0.01f;
        _moonLight.LightEnergy = 0.3f * (1f - day);

        // Re-rendering the sky's radiance is not free: only when the light has visibly changed.
        if (MathF.Abs(day - _shownDaylight) < 0.02f) return;
        _shownDaylight = day;
        WorldStyle s = Style;
        _sky.SkyTopColor = s.NightTop.Lerp(s.SkyTop, day);
        _sky.SkyHorizonColor = s.NightHorizon.Lerp(s.SkyHorizon, day);
        _sky.GroundHorizonColor = s.NightHorizon.Lerp(s.GroundHorizon, day);
        _sky.GroundBottomColor = s.NightTop.Lerp(s.GroundHorizon.Darkened(0.5f), day);
        _env.AmbientLightEnergy = 0.3f + 0.35f * day;
        _env.FogLightColor = s.NightHorizon.Lerp(s.Fog, day);
        _env.FogLightEnergy = 0.4f + 0.6f * day;
    }

    void BuildSky()
    {
        _sky = new ProceduralSkyMaterial { SunAngleMax = 25f, SkyCurve = 0.12f, GroundCurve = 0.05f };
        _env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = _sky, RadianceSize = Sky.RadianceSizeEnum.Size64 },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = 0.6f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            TonemapExposure = 1.0f,
            FogEnabled = true,
            FogDensity = 0.003f,
            FogSkyAffect = 0.3f,
            // Glow only catches what is brighter than white: bioluminescence and the Bloom's
            // veins bleed light at night without milking out the daytime colours.
            GlowEnabled = true,
            GlowIntensity = 0.9f,
            GlowBloom = 0f,
            GlowHdrThreshold = 1.0f,
            GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Additive,
            AdjustmentEnabled = true,
            AdjustmentSaturation = 1.15f,
            AdjustmentContrast = 1.08f,
        };
        AddChild(new WorldEnvironment { Environment = _env });

        _sun = new DirectionalLight3D
        {
            Name = "Sun",
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 70f,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits,
            LightEnergy = 1.3f,
        };
        AddChild(_sun);
        _moonLight = new DirectionalLight3D { Name = "Moonlight", ShadowEnabled = false, LightEnergy = 0.2f };
        AddChild(_moonLight);

        // A neighbouring world hangs in the sky: sense of place, and a 1970s paperback staple.
        _moonMaterial = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, DisableFog = true };
        _moon = new MeshInstance3D
        {
            Name = "Moon",
            Mesh = new SphereMesh { Radius = 380f, Height = 760f, RadialSegments = 48, Rings = 24 },
            MaterialOverride = _moonMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = new Vector3(-2600f, 1300f, -3600f),
        };
        AddChild(_moon);
    }

    void BuildGround()
    {
        _ground = new MeshInstance3D
        {
            Name = "Ground",
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                VertexColorUseAsAlbedo = true,
                VertexColorIsSrgb = true, // the style colours are sRGB, like everything else in the palette
                // A gentle mottle (75-100% brightness), so the vertex colours stay in charge.
                AlbedoTexture = new NoiseTexture2D
                {
                    Noise = new FastNoiseLite { Frequency = 0.05f, FractalOctaves = 3 },
                    Seamless = true, Width = 256, Height = 256,
                    ColorRamp = new Gradient { Colors = new[] { new Color(0.72f, 0.72f, 0.72f), Colors.White }, Offsets = new[] { 0f, 1f } },
                },
                Uv1Triplanar = true,
                Uv1Scale = Vector3.One * 0.15f,
                Roughness = 1f,
            },
        };
        AddChild(_ground);

        // The collider is the same height samples as the mesh, so feet and wheels agree with what you see.
        int n = (int)(Terrain.HalfSize * 2f / GridStep) + 1;
        var heights = new float[n * n];
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
            heights[z * n + x] = Terrain.Height(-Terrain.HalfSize + x * GridStep, -Terrain.HalfSize + z * GridStep) / GridStep;
        var body = new StaticBody3D { Name = "GroundBody", CollisionLayer = Layers.World, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D
        {
            Shape = new HeightMapShape3D { MapWidth = n, MapDepth = n, MapData = heights },
            Scale = Vector3.One * GridStep,
        });
        AddChild(body);

        // Past the edge, a flat plain to the horizon (the fog does the rest), and an invisible fence.
        _farGround = new StandardMaterial3D { Roughness = 1f };
        AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(9000f, 9000f) },
            MaterialOverride = _farGround,
            Position = new Vector3(0f, -2.5f, 0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        float h = Terrain.HalfSize - 4f;
        foreach (var (pos, size) in new[]
                 {
                     (new Vector3(h, 20f, 0f), new Vector3(1f, 60f, h * 2f)), (new Vector3(-h, 20f, 0f), new Vector3(1f, 60f, h * 2f)),
                     (new Vector3(0f, 20f, h), new Vector3(h * 2f, 60f, 1f)), (new Vector3(0f, 20f, -h), new Vector3(h * 2f, 60f, 1f)),
                 })
        {
            var fence = new StaticBody3D { CollisionLayer = Layers.World, CollisionMask = 0, Position = pos };
            fence.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
            AddChild(fence);
        }
    }

    ArrayMesh GroundMesh(WorldStyle style)
    {
        int n = (int)(Terrain.HalfSize * 2f / GridStep) + 1;
        var verts = new Vector3[n * n];
        var normals = new Vector3[n * n];
        var colors = new Color[n * n];
        var noise = new FastNoiseLite { Seed = 7, Frequency = 0.02f };
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
        {
            float wx = -Terrain.HalfSize + x * GridStep, wz = -Terrain.HalfSize + z * GridStep;
            float y = Terrain.Height(wx, wz);
            int i = z * n + x;
            verts[i] = new Vector3(wx, y, wz);
            normals[i] = Terrain.Normal(wx, wz);
            float k = Math.Clamp(noise.GetNoise2D(wx, wz) * 0.8f + 0.5f, 0f, 1f);
            colors[i] = style.GroundA.Lerp(style.GroundB, k).Darkened(Math.Clamp(-y * 0.04f, 0f, 0.25f));
        }
        var indices = new int[(n - 1) * (n - 1) * 6];
        int t = 0;
        for (int z = 0; z < n - 1; z++)
        for (int x = 0; x < n - 1; x++)
        {
            int a = z * n + x, b = a + 1, c = a + n, d = c + 1;
            indices[t++] = a; indices[t++] = b; indices[t++] = c; // clockwise seen from above
            indices[t++] = b; indices[t++] = d; indices[t++] = c;
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    /// <summary>Mesas, monoliths and leaning spires: big readable shapes for the eye to judge distance by.</summary>
    void BuildRocks()
    {
        _rockA = new StandardMaterial3D { Roughness = 0.95f };
        _rockB = new StandardMaterial3D { Roughness = 0.95f };
        var rng = Rng.For(LandSeed, "rocks");
        for (int i = 0; i < 70; i++)
        {
            var p = new Vector3(rng.Range(-210f, 210f), 0f, rng.Range(-210f, 210f));
            if (new Vector2(p.X, p.Z - 15f).Length() < 30f) continue; // keep the start clear
            bool spire = rng.Chance(0.3f);
            float w = spire ? rng.Range(1.2f, 3f) : rng.Range(2f, 7f);
            var size = spire ? new Vector3(w, rng.Range(6f, 16f), w * rng.Range(0.8f, 1.3f)) : new Vector3(w, rng.Range(1.2f, 5f), w * rng.Range(0.6f, 1.4f));
            float ground = Terrain.Height(p.X, p.Z);
            var body = new StaticBody3D { CollisionLayer = Layers.World, CollisionMask = 0 };
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
            int shade = i % 2;
            // Stratified like a paperback mesa: a few stacked slabs in alternating colours.
            var slabRng = Rng.For(LandSeed, "slabs", i);
            int tiers = spire ? 3 : slabRng.Int(2, 3);
            MeshInstance3D mesh = null;
            for (int k = 0; k < tiers; k++)
            {
                float h = size.Y / tiers, shrink = 1f - 0.12f * k;
                var slab = new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(size.X * shrink, h, size.Z * shrink) },
                    MaterialOverride = (k + shade) % 2 == 0 ? _rockA : _rockB,
                    Position = new Vector3(slabRng.Range(-0.08f, 0.08f) * size.X, -size.Y * 0.5f + h * (k + 0.5f), slabRng.Range(-0.08f, 0.08f) * size.Z),
                    RotationDegrees = new Vector3(0f, slabRng.Range(-8f, 8f), 0f),
                };
                body.AddChild(slab);
                mesh ??= slab;
            }
            AddChild(body);
            body.Position = new Vector3(p.X, ground + size.Y * 0.5f - 0.3f, p.Z);
            body.Rotation = new Vector3(spire ? rng.Range(-0.12f, 0.12f) : 0f, rng.Range(0f, MathF.PI), spire ? rng.Range(-0.12f, 0.12f) : 0f);
            Nav.AddObstacle(p, MathF.Max(size.X, size.Z) * 0.62f);
            _rocks.Add((mesh, shade));
        }
    }
}
