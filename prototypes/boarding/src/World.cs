using Godot;

namespace Boarding;

/// <summary>
/// The landing site: a rust-red plain under a teal sky with a ringed giant on the horizon,
/// scattered mesas, and a handful of boulders on the approach to give cover from the turret.
/// </summary>
public static class World
{
    public static void BuildSky(Node3D root)
    {
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color("0b4f5c"),
            SkyHorizonColor = new Color("f2c879"),
            GroundHorizonColor = new Color("c4532b"),
            GroundBottomColor = new Color("5a1e12"),
            SunAngleMax = 25f,
            SkyCurve = 0.12f,
        };
        root.AddChild(new WorldEnvironment
        {
            Environment = new Environment
            {
                BackgroundMode = Environment.BGMode.Sky,
                Sky = new Sky { SkyMaterial = sky },
                AmbientLightSource = Environment.AmbientSource.Sky,
                AmbientLightEnergy = 0.65f,
                TonemapMode = Environment.ToneMapper.Filmic,
                TonemapWhite = 6f,
                GlowEnabled = true,
                GlowIntensity = 0.6f,
                GlowBloom = 0.05f,
                FogEnabled = true,
                FogLightColor = new Color("e8a76a"),
                FogDensity = 0.0012f,
                FogSkyAffect = 0.3f,
            },
        });

        var sun = new DirectionalLight3D
        {
            LightColor = new Color("ffd2a0"),
            LightEnergy = 1.35f,
            ShadowEnabled = true,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits,
            DirectionalShadowMaxDistance = 140f,
        };
        root.AddChild(sun);
        sun.RotationDegrees = new Vector3(-32f, -128f, 0f);

        // A ringed gas giant low over the horizon.
        var planet = new Node3D { Position = new Vector3(-1500f, 520f, -2600f) };
        root.AddChild(planet);
        planet.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 380f, Height = 760f },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("e85d04"), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, DisableFog = true },
        });
        planet.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 381f, Height = 140f, IsHemisphere = false },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("f4a100"), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, DisableFog = true },
            Scale = new Vector3(1f, 0.35f, 1f),
            Position = new Vector3(0, 120f, 0),
        });
        var ring = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 520f, OuterRadius = 700f, Rings = 64, RingSegments = 4 },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.95f, 0.85f, 0.65f, 0.7f),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                DisableFog = true,
            },
            Scale = new Vector3(1f, 0.02f, 1f),
            RotationDegrees = new Vector3(14f, 0f, -18f),
        };
        planet.AddChild(ring);
    }

    public static void BuildGround(Node3D root, Ship ship)
    {
        var ground = new StaticBody3D { Name = "Ground", CollisionLayer = Layers.World, CollisionMask = 0 };
        ground.SetMeta("surface", (int)Surface.Rock);
        ground.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(3000f, 1f, 3000f) }, Position = Vector3.Down * 0.5f });
        ground.AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(3000f, 3000f) },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color("c0603a"),
                AlbedoTexture = new NoiseTexture2D { Noise = new FastNoiseLite { Frequency = 0.03f }, Seamless = true, Width = 512, Height = 512 },
                Uv1Triplanar = true,
                Uv1Scale = Vector3.One * 0.05f,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
                Roughness = 1f,
            },
        });
        root.AddChild(ground);

        Color[] rock = { new("8b3a1e"), new("a0522d"), new("6e3b47"), new("b5651d") };
        var rng = new RandomNumberGenerator { Seed = 1979 };
        Aabb footprint = ship.Layout.Bounds.Grow(14f);
        for (int i = 0; i < 90; i++)
        {
            var spot = new Vector3(rng.RandfRange(-900f, 900f), 0f, rng.RandfRange(-900f, 900f));
            if (spot.Length() < 120f) continue;
            float w = rng.RandfRange(10f, 70f), h = rng.RandfRange(12f, 110f);
            bool spire = rng.Randf() < 0.35f;
            Vector3 size = spire ? new Vector3(w * 0.3f, h * 1.5f, w * 0.3f) : new Vector3(w, h * 0.45f, w * rng.RandfRange(0.6f, 1.4f));
            Rock(root, spot + Vector3.Up * size.Y * 0.5f, size, rng.RandfRange(0f, 90f), rock[i % rock.Length]);
        }

        // Boulders on the approach: cover from the turret on the walk in. Placed in ship space.
        Vector3[] approach =
        {
            new(-34f, 0, 26f), new(-29f, 0, 34f), new(-30f, 0, 14f), new(-18f, 0, 31f),
            new(-9f, 0, 38f), new(-36f, 0, 2f), new(4f, 0, 42f), new(-44f, 0, 18f),
        };
        for (int i = 0; i < approach.Length; i++)
        {
            Vector3 size = new(rng.RandfRange(2.6f, 4.2f), rng.RandfRange(1.6f, 2.8f), rng.RandfRange(2.4f, 4f));
            if (footprint.HasPoint(approach[i])) continue;
            Rock(root, ship.ToGlobal(approach[i]) + Vector3.Up * size.Y * 0.5f, size, rng.RandfRange(0f, 90f), rock[(i + 1) % rock.Length]);
        }
    }

    static void Rock(Node3D root, Vector3 at, Vector3 size, float yaw, Color color)
    {
        // Place it before it enters the tree: a static body moved afterwards, while the world is
        // being built from inside a physics callback, can keep its collision at the origin.
        var body = new StaticBody3D
        {
            CollisionLayer = Layers.World,
            CollisionMask = 0,
            Position = at,
            RotationDegrees = new Vector3(0, yaw, 0),
        };
        body.SetMeta("surface", (int)Surface.Rock);
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = Art.Flat(color, 1f) });
        root.AddChild(body);
    }
}
