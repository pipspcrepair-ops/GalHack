using Godot;

namespace Coop;

/// <summary>
/// One zone's world: a SubViewport with its own World3D, holding the zone's scenery, ships and
/// anyone outside. It is only drawn while it is the zone you are in.
/// </summary>
public partial class ZoneWorld : SubViewport
{
    public int Zone { get; private set; }

    /// <summary>Everything in the zone hangs off this, so a player has a Node3D to stand in.</summary>
    public Node3D Root { get; private set; }

    public static ZoneWorld Create(int zone)
    {
        var world = new ZoneWorld
        {
            Name = $"Zone{zone}",
            Zone = zone,
            OwnWorld3D = true,
            RenderTargetUpdateMode = UpdateMode.Disabled,
            Size = new Vector2I(2, 2),
            Msaa3D = Msaa.Msaa2X,
            PhysicsObjectPicking = false,
        };
        world.Root = new Node3D { Name = "World" };
        world.AddChild(world.Root);
        if (zone == Zones.Moon) world.BuildMoon();
        else world.BuildDesert();
        return world;
    }

    /// <summary>Draws this zone to the screen (or stops), at the window's size.</summary>
    public void SetShown(bool shown, Vector2I size)
    {
        RenderTargetUpdateMode = shown ? UpdateMode.Always : UpdateMode.Disabled;
        Size = shown ? size : new Vector2I(2, 2);
    }

    // ---- The desert surface (from prototypes/walkable-ship) ---------------------------------

    void BuildDesert()
    {
        var skyMaterial = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color("1d3557"),
            SkyHorizonColor = new Color("f4a261"),
            GroundHorizonColor = new Color("e76f51"),
            GroundBottomColor = new Color("6d2e1f"),
            SunAngleMax = 30f,
        };
        AddEnvironment(skyMaterial, 0.7f, new Color("f4a261"), 0.00018f);
        AddSun(new Color("ffd6a0"), 1.3f, new Vector3(-35f, 40f, 0f));

        // A neighbouring world hangs in the sky: sense of place. It is the moon zone.
        AddSkyBody(420f, new Color("7fb7be"), new Vector3(2900f, 1500f, -3900f));
        AddGround(new Color("d08850"));

        // Mesas and spires give the eye something to judge speed by.
        var rng = new RandomNumberGenerator { Seed = 1977 };
        Color[] rock = { new("a0522d"), new("b5651d"), new("8b3a1e"), new("d2915a") };
        for (int i = 0; i < 110; i++)
        {
            var spot = new Vector3(rng.RandfRange(-2000f, 2000f), 0f, rng.RandfRange(-2000f, 2000f));
            if (spot.Length() < 80f) continue;
            float width = rng.RandfRange(8f, 60f), height = rng.RandfRange(10f, 120f);
            bool spire = rng.Randf() < 0.35f;
            Vector3 size = spire
                ? new Vector3(width * 0.3f, height * 1.6f, width * 0.3f)
                : new Vector3(width, height * 0.5f, width * rng.RandfRange(0.6f, 1.4f));
            AddRock(size, rock[i % rock.Length], spot + Vector3.Up * size.Y * 0.5f, rng.RandfRange(0f, 90f));
        }
    }

    // ---- The moon base ----------------------------------------------------------------------

    void BuildMoon()
    {
        var skyMaterial = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color("05060c"),
            SkyHorizonColor = new Color("1b2133"),
            GroundHorizonColor = new Color("2a2f3a"),
            GroundBottomColor = new Color("0e1016"),
            SunAngleMax = 8f,
        };
        AddEnvironment(skyMaterial, 0.45f, new Color("3a4a66"), 0.00005f, skyFog: false); // no air: a black sky
        AddSun(new Color("e8f0ff"), 1.6f, new Vector3(-22f, -60f, 0f));

        // The desert planet, the other zone, hangs huge in the black sky.
        AddSkyBody(900f, new Color("e07a3f"), new Vector3(1850f, 2400f, -6200f));
        AddGround(new Color("8d939c"));

        // Craters (low rims) and boulders, then a landing pad with beacons.
        var rng = new RandomNumberGenerator { Seed = 4242 };
        Color[] stone = { new("6c717a"), new("80868f"), new("5a5f68"), new("9aa0a8") };
        for (int i = 0; i < 90; i++)
        {
            var spot = new Vector3(rng.RandfRange(-1800f, 1800f), 0f, rng.RandfRange(-1800f, 1800f));
            if (spot.Length() < 70f) continue;
            float radius = rng.RandfRange(4f, 40f);
            if (rng.Randf() < 0.5f)
            {
                for (int k = 0; k < 10; k++)
                {
                    float a = k / 10f * Mathf.Tau;
                    var rim = new Vector3(radius * 0.5f, radius * 0.18f, radius * 0.25f);
                    AddRock(rim, stone[(i + k) % stone.Length],
                        spot + new Vector3(Mathf.Cos(a) * radius, rim.Y * 0.4f, Mathf.Sin(a) * radius), -Mathf.RadToDeg(a));
                }
            }
            else
            {
                var size = new Vector3(radius * 0.4f, radius * 0.35f, radius * 0.5f);
                AddRock(size, stone[i % stone.Length], spot + Vector3.Up * size.Y * 0.45f, rng.RandfRange(0f, 90f));
            }
        }

        var pad = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(30f, 0.1f, 40f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("2b2d42"), Roughness = 0.9f },
            Position = new Vector3(0, 0.05f, 0),
        };
        Root.AddChild(pad);
        foreach (float x in new[] { -15f, 15f })
            foreach (float z in new[] { -20f, 0f, 20f })
                Root.AddChild(new MeshInstance3D
                {
                    Mesh = new CylinderMesh { TopRadius = 0.25f, BottomRadius = 0.25f, Height = 1.2f },
                    MaterialOverride = new StandardMaterial3D
                    {
                        AlbedoColor = ShipLayout.Teal,
                        EmissionEnabled = true,
                        Emission = ShipLayout.Teal,
                        EmissionEnergyMultiplier = 3f,
                    },
                    Position = new Vector3(x, 0.6f, z),
                });
    }

    // ---- Pieces -----------------------------------------------------------------------------

    void AddEnvironment(ProceduralSkyMaterial sky, float ambient, Color fog, float fogDensity, bool skyFog = true)
    {
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = sky },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = ambient,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            FogEnabled = true,
            FogLightColor = fog,
            FogDensity = fogDensity,
            FogSkyAffect = skyFog ? 1f : 0f,
        };
        Root.AddChild(new WorldEnvironment { Environment = environment });
    }

    void AddSun(Color color, float energy, Vector3 rotationDegrees)
    {
        var sun = new DirectionalLight3D
        {
            LightColor = color,
            LightEnergy = energy,
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 250f,
        };
        Root.AddChild(sun);
        sun.RotationDegrees = rotationDegrees;
    }

    void AddSkyBody(float radius, Color color, Vector3 position)
    {
        var body = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = radius, Height = radius * 2f },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = color,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                DisableFog = true,
            },
        };
        Root.AddChild(body);
        body.Position = position;
    }

    void AddGround(Color color)
    {
        var ground = new StaticBody3D { Name = "Ground", CollisionLayer = Layers.World, CollisionMask = 0 };
        ground.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(8000f, 1f, 8000f) },
            Position = new Vector3(0, -0.5f, 0),
        });
        ground.AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(8000f, 8000f) },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = color,
                AlbedoTexture = new NoiseTexture2D
                {
                    Noise = new FastNoiseLite { Frequency = 0.02f },
                    Seamless = true,
                    Width = 512,
                    Height = 512,
                },
                Uv1Triplanar = true,
                Uv1Scale = Vector3.One * 0.06f,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
                Roughness = 1f,
            },
        });
        Root.AddChild(ground);
    }

    void AddRock(Vector3 size, Color color, Vector3 position, float yawDegrees)
    {
        var rock = new StaticBody3D { CollisionLayer = Layers.World, CollisionMask = 0 };
        rock.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        rock.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = size },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = 1f },
        });
        Root.AddChild(rock);
        rock.Position = position;
        rock.RotationDegrees = new Vector3(0f, yawDegrees, 0f);
    }
}
