using Godot;

namespace WalkableShip;

/// <summary>
/// Builds the prototype world in code: a desert planet with mesas and a moon in the sky,
/// one ship, one player standing in its cabin, and a ring course to fly.
/// </summary>
public partial class Main : Node3D
{
    public Ship Ship { get; private set; }
    public Player Player { get; private set; }
    public RingCourse Course { get; private set; }

    Label _hud;

    public override void _Ready()
    {
        BuildSky();
        BuildGround();

        Ship = new Ship { Name = "Ship", Position = new Vector3(0, WalkableShip.Ship.GroundClearance, 0) };
        AddChild(Ship);

        var camera = new Camera3D { Name = "Camera", Fov = 80f, Near = 0.05f, Far = 8000f };
        AddChild(camera);
        camera.MakeCurrent();

        Player = new Player { Name = "Player", Ship = Ship, Camera = camera };
        AddChild(Player);
        Player.GlobalPosition = WalkableShip.Ship.GhostOrigin + ShipLayout.SpawnLocal;

        Course = new RingCourse { Name = "Course", Ship = Ship };
        AddChild(Course);

        BuildHud();
    }

    public override void _Process(double delta)
    {
        string where = Player.CurrentMode switch
        {
            WalkableShip.Player.Mode.Piloting => "Piloting",
            WalkableShip.Player.Mode.Aboard => Ship.GravityOn ? "Aboard, walking" : "Aboard, floating (core gravity off)",
            _ => $"Outside   jetpack {Player.JetpackFuel * 100f:F0}%",
        };
        string flight = Ship.StuntAutopilot ? "stunt autopilot" : Ship.Piloted ? "manual" : "autopilot holding course";
        string course = Course.Finished
            ? $"Course complete: {Course.Elapsed:F1} s   (Backspace to fly it again)"
            : $"Rings {Course.Passed}/{Course.Count}   {Course.Elapsed:F1} s";
        if (Course.BestTime is float best) course += $"   best {best:F1} s";

        _hud.Text =
            $"{where}\n" +
            $"Ship {Ship.Speed:F0} m/s   altitude {Ship.GlobalPosition.Y:F0} m   {flight}\n" +
            $"{course}\n\n" +
            "WASD move  ·  Space jump / up  ·  Ctrl down  ·  Shift sprint / boost\n" +
            "F sit in or leave the pilot seat  ·  mouse steer  ·  Q/E roll\n" +
            "G core gravity  ·  T stunt autopilot  ·  V third person  ·  R back aboard  ·  Esc free the mouse\n" +
            $"{Engine.GetFramesPerSecond()} fps";
    }

    void BuildSky()
    {
        var skyMaterial = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color("1d3557"),
            SkyHorizonColor = new Color("f4a261"),
            GroundHorizonColor = new Color("e76f51"),
            GroundBottomColor = new Color("6d2e1f"),
            SunAngleMax = 30f,
        };
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = skyMaterial },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = 0.7f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            FogEnabled = true,
            FogLightColor = new Color("f4a261"),
            FogDensity = 0.00018f,
        };
        AddChild(new WorldEnvironment { Environment = environment });

        var sun = new DirectionalLight3D
        {
            LightColor = new Color("ffd6a0"),
            LightEnergy = 1.3f,
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 250f,
        };
        AddChild(sun);
        sun.RotationDegrees = new Vector3(-35f, 40f, 0f);

        // A neighbouring world hangs in the sky: sense of place.
        var moon = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 420f, Height = 840f },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color("7fb7be"),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                DisableFog = true,
            },
        };
        AddChild(moon);
        moon.Position = new Vector3(2900f, 1500f, -3900f);
    }

    void BuildGround()
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
                AlbedoColor = new Color("d08850"),
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
        AddChild(ground);

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

            var mesa = new StaticBody3D { CollisionLayer = Layers.World, CollisionMask = 0 };
            mesa.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
            mesa.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = size },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = rock[i % rock.Length], Roughness = 1f },
            });
            AddChild(mesa);
            mesa.Position = spot + Vector3.Up * size.Y * 0.5f;
            mesa.RotationDegrees = new Vector3(0f, rng.RandfRange(0f, 90f), 0f);
        }
    }

    void BuildHud()
    {
        var layer = new CanvasLayer();
        AddChild(layer);

        _hud = new Label { Position = new Vector2(16f, 12f) };
        _hud.AddThemeFontSizeOverride("font_size", 17);
        _hud.AddThemeColorOverride("font_color", new Color("ffe8c2"));
        _hud.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
        _hud.AddThemeConstantOverride("outline_size", 6);
        layer.AddChild(_hud);

        var crosshair = new ColorRect
        {
            Color = new Color(1f, 1f, 1f, 0.8f),
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = -2f, OffsetRight = 2f, OffsetTop = -2f, OffsetBottom = 2f,
        };
        layer.AddChild(crosshair);
    }
}
