using Godot;
using Weather.Model;

namespace Weather;

/// <summary>
/// Builds the prototype in code: rolling plains with a lone spire on a hill, groves of
/// umbrella trees, a landing pad with a light hover vessel and a heavy rover side by side, and
/// a player standing in front of them. The weather model, the force module and the visuals are
/// separate pieces wired together here.
/// </summary>
public partial class Main : Node3D
{
    public ulong Seed = 1977;
    public string Archetype = "temperate";

    public WeatherDirector Weather { get; private set; }
    public WeatherFx Fx { get; private set; }
    public WeatherAudio Audio { get; private set; }
    public Player Player { get; private set; }
    public HoverVessel Hover { get; private set; }
    public Rover Rover { get; private set; }
    public Spire Spire { get; private set; }
    public Hud Hud { get; private set; }
    public Camera3D Camera { get; private set; }

    public static readonly Vector2 HoverSpot = new(-6f, -10f);
    public static readonly Vector2 RoverSpot = new(6f, -10f);
    public static readonly Vector2 SpawnSpot = new(0f, 9f);

    ShaderMaterial _groundMaterial;

    /// <summary>The terrain's material in another colour: it gets wet, dusty and snowed on like the ground.</summary>
    ShaderMaterial Ground(string color)
    {
        var m = (ShaderMaterial)_groundMaterial.Duplicate();
        m.SetShaderParameter("tint", new Color(color));
        return m;
    }

    public override void _Ready()
    {
        var data = WeatherData.Parse(FileAccess.GetFileAsString("res://data/weather.json"));
        Fx = new WeatherFx { Name = "WeatherFx" };

        var terrainMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/terrain.gdshader") };
        terrainMaterial.SetShaderParameter("cloud_noise", Fx.CloudNoise);
        terrainMaterial.SetShaderParameter("to_sun", WeatherFx.ToSun);
        terrainMaterial.SetShaderParameter("detail_noise", new NoiseTexture2D
        {
            Width = 256, Height = 256, Seamless = true, GenerateMipmaps = true,
            Noise = new FastNoiseLite { Seed = 9, Frequency = 0.03f, FractalOctaves = 4 },
        });
        AddChild(Terrain.Build(terrainMaterial));
        _groundMaterial = terrainMaterial;
        AddChild(Foliage.Build(GD.Load<Shader>("res://shaders/foliage.gdshader"), Fx.CloudNoise, WeatherFx.ToSun));
        BuildPad();
        BuildRocks();

        Spire = new Spire { Name = "Spire", Position = Terrain.OnGround(Terrain.SpireSpot.X, Terrain.SpireSpot.Y, -1f) };
        AddChild(Spire);

        Hover = new HoverVessel { Name = "HoverVessel" };
        Rover = new Rover { Name = "Rover" };
        AddChild(Hover);
        AddChild(Rover);
        ResetVehicles();

        Camera = new Camera3D { Name = "Camera", Fov = 78f, Near = 0.05f, Far = 9000f };
        AddChild(Camera);
        Camera.MakeCurrent();

        Weather = new WeatherDirector { Name = "Weather" };
        Player = new Player
        {
            Name = "Player", Camera = Camera, Forces = Weather.Forces,
            Spawn = Terrain.OnGround(SpawnSpot.X, SpawnSpot.Y, 0.1f),
        };
        Player.Position = Player.Spawn;
        AddChild(Player);

        Weather.Player = Player;
        Weather.Fx = Fx;
        AddChild(Weather);
        Weather.Setup(data, Seed, Archetype, Spire, Hover, Rover);

        Fx.Director = Weather;
        Fx.Camera = Camera;
        AddChild(Fx);
        Audio = new WeatherAudio { Name = "Audio", Director = Weather, Listener = Camera };
        AddChild(Audio);
        Weather.Audio = Audio;

        var layer = new CanvasLayer { Name = "Visor" };
        AddChild(layer);
        Hud = new Hud { Director = Weather, Player = Player };
        layer.AddChild(Hud);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        Player.HandleInput(e);
        if (e is not InputEventKey { Pressed: true, Echo: false } key) return;
        switch (key.PhysicalKeycode)
        {
            case Key.R: ResetVehicles(); break;
            case Key.F1: Hud.ShowHelp = !Hud.ShowHelp; break;
            default: Weather.HandleKey(key.PhysicalKeycode); break;
        }
    }

    /// <summary>Puts both vehicles back on the pad, the skiff moored again.</summary>
    public void ResetVehicles()
    {
        Hover.Anchor = Terrain.OnGround(HoverSpot.X, HoverSpot.Y - 3f);
        Hover.Moored = true;
        Place(Hover, Terrain.OnGround(HoverSpot.X, HoverSpot.Y, 1.3f), 0f);
        Place(Rover, Terrain.OnGround(RoverSpot.X, RoverSpot.Y, 0.05f), 0.1f);
    }

    static void Place(RigidBody3D body, Vector3 at, float yaw)
    {
        body.GlobalTransform = new Transform3D(new Basis(Vector3.Up, yaw), at);
        body.LinearVelocity = Vector3.Zero;
        body.AngularVelocity = Vector3.Zero;
        body.ResetPhysicsInterpolation();
    }

    /// <summary>A painted landing pad with bays, a mooring post and a couple of tanks: an outpost, so the plain has a place in it.</summary>
    void BuildPad()
    {
        float y = Terrain.Height(Terrain.PadCenter.X, Terrain.PadCenter.Y);
        var pad = new Node3D { Name = "Pad", Position = new Vector3(Terrain.PadCenter.X, y, Terrain.PadCenter.Y) };
        AddChild(pad);
        var slab = Ground("4a4c58");
        var paint = Ground("f4a100");
        var white = new StandardMaterial3D { AlbedoColor = new Color("e9dfc4"), Roughness = 0.6f };
        var orange = new StandardMaterial3D { AlbedoColor = new Color("e85d04"), Roughness = 0.6f };
        var dark = new StandardMaterial3D { AlbedoColor = new Color("2b2d42"), Roughness = 0.7f };

        var stripeDark = Ground("2b2d42");
        void Box(Node3D parent, Vector3 size, Vector3 at, Material m) =>
            parent.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = m, Position = at });

        Box(pad, new Vector3(30f, 0.3f, 22f), new Vector3(0, -0.1f, -3f), slab);
        foreach (Vector2 spot in new[] { HoverSpot, RoverSpot })
        {
            Vector3 c = new(spot.X - Terrain.PadCenter.X, 0.07f, spot.Y - Terrain.PadCenter.Y);
            Box(pad, new Vector3(6.4f, 0.04f, 0.3f), c + new Vector3(0, 0, -4.2f), paint);
            Box(pad, new Vector3(6.4f, 0.04f, 0.3f), c + new Vector3(0, 0, 4.2f), paint);
            Box(pad, new Vector3(0.3f, 0.04f, 8.4f), c + new Vector3(-3.2f, 0, 0), paint);
            Box(pad, new Vector3(0.3f, 0.04f, 8.4f), c + new Vector3(3.2f, 0, 0), paint);
        }
        for (int i = 0; i < 9; i++) Box(pad, new Vector3(1.2f, 0.04f, 0.5f), new Vector3(-12f + i * 3f, 0.07f, 7.2f), i % 2 == 0 ? paint : stripeDark);

        // Mooring post for the skiff.
        var post = new Vector3(HoverSpot.X - Terrain.PadCenter.X, 0, HoverSpot.Y - 3f - Terrain.PadCenter.Y);
        Box(pad, new Vector3(0.4f, 1.8f, 0.4f), post + new Vector3(0, 0.9f, 0), orange);
        Box(pad, new Vector3(0.6f, 0.2f, 0.6f), post + new Vector3(0, 1.8f, 0), dark);

        // Tanks and a container at the pad's edge.
        var tanks = new StaticBody3D { CollisionLayer = Layers.World, CollisionMask = 0 };
        pad.AddChild(tanks);
        foreach (var (x, z, m) in new[] { (17.5f, -9f, white), (17.5f, -4.5f, orange) })
        {
            tanks.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 1.8f, BottomRadius = 1.8f, Height = 5f }, MaterialOverride = m, Position = new Vector3(x, 2.5f, z) });
            tanks.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 1.85f, BottomRadius = 1.85f, Height = 0.5f }, MaterialOverride = dark, Position = new Vector3(x, 3.6f, z) });
            tanks.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 1.8f, Height = 5f }, Position = new Vector3(x, 2.5f, z) });
        }
        tanks.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(6f, 2.6f, 2.4f) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("0a9396"), Roughness = 0.7f }, Position = new Vector3(-17f, 1.3f, -8f) });
        tanks.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(6f, 2.6f, 2.4f) }, Position = new Vector3(-17f, 1.3f, -8f) });
    }

    /// <summary>Boulders for scale on the open plain.</summary>
    void BuildRocks()
    {
        var rng = new RandomNumberGenerator { Seed = 1975 };
        var material = new StandardMaterial3D { AlbedoColor = new Color("5e5260"), Roughness = 0.95f };
        var mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 7, Rings = 4 };
        var rocks = new StaticBody3D { Name = "Rocks", CollisionLayer = Layers.World, CollisionMask = 0 };
        AddChild(rocks);
        for (int i = 0; i < 40; i++)
        {
            float x = rng.RandfRange(-300f, 300f), z = rng.RandfRange(-300f, 300f);
            if (new Vector2(x - Terrain.PadCenter.X, z - Terrain.PadCenter.Y).Length() < 30f) continue;
            var scale = new Vector3(rng.RandfRange(1f, 3f), rng.RandfRange(0.6f, 1.8f), rng.RandfRange(1f, 3f));
            var at = Terrain.OnGround(x, z, scale.Y * 0.3f);
            var basis = new Basis(Vector3.Up, rng.RandfRange(0f, Mathf.Tau)).Scaled(scale);
            rocks.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = material, Transform = new Transform3D(basis, at) });
            // Jolt dislikes scaled shapes, so the collider is an unscaled sphere that fits inside.
            rocks.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = Mathf.Min(scale.X, Mathf.Min(scale.Y, scale.Z)) }, Position = at });
        }
    }
}
