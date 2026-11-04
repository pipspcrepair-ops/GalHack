using Godot;
using TerrainDigging.Terrain;

namespace TerrainDigging;

/// <summary>
/// Everything around the terrain: an indigo-to-peach sky with a ringed giant hanging in it,
/// a low warm sun, glow so ore veins bloom, the static water plane, and the survey-beacon POI.
/// </summary>
public static class Scenery
{
    public static void BuildSky(Node3D root)
    {
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = Palette.SkyTop,
            SkyHorizonColor = Palette.SkyHorizon,
            SkyCurve = 0.09f,
            GroundHorizonColor = Palette.GroundHorizon,
            GroundBottomColor = new Color("5a2430"),
            SunAngleMax = 25f,
            SunCurve = 0.08f,
        };
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = sky },
            // Lavender shadows, as on a paperback cover, and light enough to read a pit's walls.
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color("8f7cc0"),
            AmbientLightEnergy = 0.85f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            TonemapExposure = 1.05f,
            GlowEnabled = true,
            GlowIntensity = 0.9f,
            GlowBloom = 0.05f,
            GlowHdrThreshold = 1.1f,
            FogEnabled = true,
            FogLightColor = new Color("f6cfa2"),
            FogDensity = 0.0032f,
            FogSkyAffect = 0.15f,
            FogAerialPerspective = 0.4f,
        };
        root.AddChild(new WorldEnvironment { Environment = environment });

        var sun = new DirectionalLight3D
        {
            LightColor = Palette.Sun,
            LightEnergy = 1.6f,
            ShadowEnabled = true,
            ShadowBias = 0.25f,
            ShadowNormalBias = 5f,
            DirectionalShadowMaxDistance = 110f,
            DirectionalShadowBlendSplits = true,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits,
        };
        root.AddChild(sun);
        sun.RotationDegrees = new Vector3(-42f, -38f, 0f);

        // A ringed gas giant low over the rim mountains: scale, colour, paperback drama.
        var planet = new Node3D { Position = new Vector3(-1500f, 650f, -3400f) };
        root.AddChild(planet);
        planet.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 620f, Height = 1240f, RadialSegments = 64, Rings = 32 },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color("8fd3c7"),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                DisableFog = true,
                AlbedoTexture = new NoiseTexture2D
                {
                    Width = 64,
                    Height = 512,
                    Noise = new FastNoiseLite { Frequency = 0.03f, Seed = 3 },
                    ColorRamp = Bands(),
                },
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        planet.AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 820f, OuterRadius = 1150f, Rings = 96, RingSegments = 4 },
            Scale = new Vector3(1f, 0.02f, 1f),
            RotationDegrees = new Vector3(14f, 0f, -18f),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.98f, 0.86f, 0.62f, 0.55f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                DisableFog = true,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    static Gradient Bands()
    {
        var g = new Gradient();
        g.SetColor(0, new Color("6fc2b8"));
        g.SetColor(1, new Color("f2e2b5"));
        g.AddPoint(0.45f, new Color("a8e0cf"));
        g.AddPoint(0.6f, new Color("e8a77c"));
        return g;
    }

    /// <summary>The static water plane, masked to the columns whose natural surface lies below it.</summary>
    public static void BuildWater(Node3D root, ChunkStore store)
    {
        CellBox w = ChunkStore.WorldCells;
        int width = w.Size.X, depth = w.Size.Z;
        var image = Image.CreateEmpty(width, depth, false, Image.Format.R8);
        bool any = false;
        for (int z = 0; z < depth; z++)
            for (int x = 0; x < width; x++)
            {
                bool water = store.IsWaterColumn(w.Min.X + x, w.Min.Z + z);
                any |= water;
                image.SetPixel(x, z, water ? Colors.White : Colors.Black);
            }
        if (!any) return;

        var material = Shaders.Make(Shaders.Water);
        material.SetShaderParameter("lake_mask", ImageTexture.CreateFromImage(image));
        material.SetShaderParameter("mask_origin", new Vector2(w.Min.X, w.Min.Z));
        material.SetShaderParameter("mask_size", new Vector2(width, depth));
        material.SetShaderParameter("deep_color", Palette.WaterDeep);
        material.SetShaderParameter("shallow_color", Palette.WaterShallow);
        root.AddChild(new MeshInstance3D
        {
            Name = "Water",
            Mesh = new PlaneMesh { Size = new Vector2(width, depth) },
            Position = new Vector3(w.Min.X + width / 2f, store.Source.WaterLevel, w.Min.Z + depth / 2f),
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    /// <summary>The survey beacon POI: a striped mast on a pad, with a slow blinking light.</summary>
    public static void BuildBeacon(Node3D root, PoiSite site)
    {
        var body = new StaticBody3D { Name = site.Name, CollisionLayer = Layers.Terrain, CollisionMask = 0 };
        root.AddChild(body);
        System.Numerics.Vector3 center = site.Footprint.CenterPosition;
        var c = new Vector3(center.X, center.Y, center.Z);
        float ground = site.Footprint.Min.Y + 2f;
        var white = new StandardMaterial3D { AlbedoColor = Palette.Beacon, Roughness = 0.6f };
        var red = new StandardMaterial3D { AlbedoColor = Palette.BeaconRed, Roughness = 0.5f };
        var dark = new StandardMaterial3D { AlbedoColor = Palette.BlockTrim, Roughness = 0.7f };

        void Part(Mesh mesh, Material material, Vector3 at, Shape3D shape = null)
        {
            body.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = material, Position = at });
            if (shape != null) body.AddChild(new CollisionShape3D { Shape = shape, Position = at });
        }

        // The pad reaches down into the ground so it never floats over a dip.
        Part(new CylinderMesh { TopRadius = 3f, BottomRadius = 3.2f, Height = 3f, RadialSegments = 8 }, dark,
            new Vector3(c.X, ground - 1.2f, c.Z), new CylinderShape3D { Radius = 3.1f, Height = 3f });
        Part(new CylinderMesh { TopRadius = 2.6f, BottomRadius = 2.6f, Height = 0.25f, RadialSegments = 8 }, white,
            new Vector3(c.X, ground + 0.4f, c.Z));
        for (int i = 0; i < 5; i++)
            Part(new CylinderMesh { TopRadius = 0.42f - i * 0.04f, BottomRadius = 0.46f - i * 0.04f, Height = 2f },
                i % 2 == 0 ? white : red, new Vector3(c.X, ground + 1.5f + i * 2f, c.Z));
        body.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 0.46f, Height = 10f }, Position = new Vector3(c.X, ground + 5.5f, c.Z) });
        Part(new TorusMesh { InnerRadius = 1.1f, OuterRadius = 1.35f }, red, new Vector3(c.X, ground + 7.5f, c.Z));

        var lamp = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.45f, Height = 0.9f },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color("ff5a3c"),
                EmissionEnabled = true,
                Emission = new Color("ff5a3c"),
                EmissionEnergyMultiplier = 4f,
            },
            Position = new Vector3(c.X, ground + 11f, c.Z),
        };
        body.AddChild(lamp);
        var light = new OmniLight3D { LightColor = new Color("ff7a50"), LightEnergy = 2f, OmniRange = 9f, Position = lamp.Position };
        body.AddChild(light);
        var tween = lamp.CreateTween().SetLoops();
        tween.TweenProperty(light, "light_energy", 0.2f, 0.9f);
        tween.TweenProperty(light, "light_energy", 2f, 0.9f);
    }
}
