using System.Collections.Generic;
using System.Linq;
using Godot;
using Invasions.Combat;
using Invasions.Director;
using Invasions.Model;

namespace Invasions.World;

/// <summary>
/// Builds the prototype in code: a dusk world of ochre dust and mesas, the player's block-built base
/// at the origin, the bases of several factions around it, the invasion director and the glue that
/// lets them meet. The nearest base of a faction hostile to the players sends the invasions: at the
/// start that is the Vharn garrison 600 m north-east. Debug keys: I invade now, T fast time, P place
/// a new core, K knock out the current source, J pick a faction, minus and equals wrong it or favour it.
/// </summary>
public partial class Main : Node3D
{
    public InvasionDirector Director { get; private set; }
    public BaseModel Model { get; private set; }
    public BaseView Base { get; private set; }
    public CombatWorld Combat { get; private set; }
    /// <summary>The Vharn garrison, the nearest hostile base at the start.</summary>
    public Garrison Garrison { get; private set; }
    public IReadOnlyList<EnemyBaseNode> EnemyBases => _enemyBases;
    public Player Player { get; private set; }
    public InvasionController Controller { get; private set; }
    public Hud Hud { get; private set; }
    public OptionsMenu Options { get; private set; }
    public InvasionData Data { get; private set; }

    /// <summary>
    /// Where this world's own settings (the options) are saved. Tests and screenshots set it to null so
    /// they always start from the data files, untouched by anything a player chose.
    /// </summary>
    public static string WorldFile = "user://world.json";
    WorldSettings _world;

    public static readonly Vector3 GarrisonPosition = new(430f, 0f, -420f);

    /// <summary>
    /// The neighbourhood: one base per faction. Only hostile factions invade, so at the start the Vharn
    /// (601 m), the Reavers (888 m) and the Bloom (1343 m) could, and the Vharn, being nearest, do. The
    /// Kessari and the Meridian Combine would if wronged; the Freeholders and the Exchange never will.
    /// </summary>
    public static readonly (string Id, string Name, string Faction, EnemyBaseKind Kind, Vector3 Position)[] Neighbours =
    {
        ("okkar", "Garrison Okkar-9", "vharn", EnemyBaseKind.Garrison, GarrisonPosition),
        ("larkspur", "Larkspur Steading", "freeholders", EnemyBaseKind.Outpost, new Vector3(-300f, 0f, -280f)),
        ("tannic", "Hold Tannic-3", "kessari", EnemyBaseKind.Garrison, new Vector3(520f, 0f, 520f)),
        ("scrapjaw", "Scrapjaw Den", "reavers", EnemyBaseKind.Garrison, new Vector3(-880f, 0f, 120f)),
        ("vantage", "Vantage Security Post", "meridian", EnemyBaseKind.Garrison, new Vector3(-150f, 0f, 1050f)),
        ("quay", "Ledger Quay 9", "exchange", EnemyBaseKind.Outpost, new Vector3(1250f, 0f, -100f)),
        ("sere", "Bloom Infestation Sere", "hollow", EnemyBaseKind.Garrison, new Vector3(-950f, 0f, 950f)),
    };

    readonly List<EnemyBaseNode> _enemyBases = new();

    public override void _Ready()
    {
        Controls.Register();
        Data = GameData.Invasions();
        // The server's defaults, with this world's own choices (saved from the options) on top.
        _world = GameData.LoadWorld(WorldFile);
        ServerSettings server = _world.Apply(GameData.Server());
        BaseData baseData = GameData.Base();

        BuildSky();
        BuildGround();
        var effects = new Node3D { Name = "Effects", PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off };
        AddChild(effects);
        Fx.Root = effects;

        Model = new BaseModel(baseData, new GroundPos(0, 0));
        Combat = new CombatWorld
        {
            Name = "Combat", Model = Model, Data = Data, FireData = GameData.FireControl(),
            Planner = new BreachPlanner(Data.Planner.HpPerCellOfDetour),
        };
        AddChild(Combat);

        var camera = new Camera3D { Name = "Camera", Fov = 78f, Near = 0.05f, Far = 6000f };
        AddChild(camera);
        camera.MakeCurrent();
        Player = new Player { Name = "Player", Camera = camera, World = Combat, Health = new PlayerHealth() };

        Base = new BaseView { Name = "Base", Model = Model, World = Combat, Player = Player };
        AddChild(Base);
        Player.Base = Base;
        Player.SpawnPoint = Base.SpawnPoint;
        Player.SpawnYaw = 0f; // facing north, toward the core room and the garrison beyond
        Player.Position = Player.SpawnPoint;
        AddChild(Player);

        // Enemy bases first: a player base's first countdown is timed for whoever will invade it.
        Director = new InvasionDirector(Data, server, server.Seed);
        foreach (var (id, name, faction, kind, position) in Neighbours)
        {
            EnemyBaseNode node = faction == "vharn"
                ? Garrison = new Garrison { Id = id, DisplayName = name }
                : new FactionOutpost { Id = id, DisplayName = name, Faction = faction };
            node.Name = id;
            node.Kind = kind;
            node.Position = position;
            node.Facing = -position.Normalized();
            node.Livery = Combat.LiveryFor(faction);
            AddChild(node);
            _enemyBases.Add(node);
            Director.AddEnemyBase(id, name, faction, kind, new GroundPos(position.X, position.Z));
        }
        Director.AddPlayerBase(InvasionController.HomeId, baseData.Name, Model.Centre);

        Controller = new InvasionController
        {
            Name = "Invasions",
            Director = Director,
            Model = Model,
            View = Base,
            World = Combat,
            Factory = new PlaceholderCombatFactory(Combat),
            Player = Player,
            EnemyBases = _enemyBases,
        };
        AddChild(Controller);

        Hud = new Hud { Name = "Hud", Controller = Controller, Player = Player };
        Controller.Hud = Hud;
        AddChild(Hud);

        Options = new OptionsMenu { Name = "Options", Controller = Controller };
        AddChild(Options);
        Controller.SettingsChanged += () =>
        {
            _world.InvasionsEnabled = Director.InvasionsEnabled;
            GameData.SaveWorld(WorldFile, _world);
        };
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed(Controls.DebugInvade))
        {
            string why = Controller.InvadeNow();
            if (why != "") Hud.Announce(why, 0, 5f);
        }
        else if (e.IsActionPressed(Controls.DebugFastTime))
        {
            Director.FastTime = !Director.FastTime;
            Hud.Announce(Director.FastTime ? $"Fast time on: the clock runs x{Director.Server.FastTimeScale:0}." : "Fast time off.", 0, 4f);
        }
        else if (e.IsActionPressed(Controls.DebugPlaceCore))
        {
            if (Model.Online) Hud.Announce("The core is still standing.", 0, 4f);
            else if (Model.PlaceNewCore() == null) Hud.Announce("Can't place a core here.", 0, 4f);
        }
        else if (e.IsActionPressed(Controls.DebugKnockOutSource))
        {
            string why = Controller.KnockOutSource();
            if (why != "") Hud.Announce(why, 0, 4f);
        }
        else if (e.IsActionPressed(Controls.DebugNextFaction))
        {
            Controller.SelectNextFaction();
        }
        else if (e.IsActionPressed(Controls.DebugWrong) || e.IsActionPressed(Controls.DebugFavour))
        {
            Controller.ShiftSelectedStanding(e.IsActionPressed(Controls.DebugWrong) ? -35 : 35);
        }
    }

    // ---- World ------------------------------------------------------------------------

    void BuildSky()
    {
        // High-key Foss palette: a pale cream horizon rising into teal over ochre dust. Warm sun,
        // cool sky fill. (Blue over orange blends to mud; cream into teal stays clean.)
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color("3f8f9c"),
            SkyHorizonColor = new Color("f5e2bb"),
            SkyCurve = 0.2f,
            GroundHorizonColor = new Color("d99a5e"),
            GroundBottomColor = new Color("6b3a22"),
            SunAngleMax = 20f,
            SunCurve = 0.1f,
        };
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = sky },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = 0.65f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            TonemapExposure = 1.0f,
            FogEnabled = true,
            FogLightColor = new Color("f1dcb4"),
            FogDensity = 0.00028f,
            FogSkyAffect = 0f,
            GlowEnabled = true,
            GlowIntensity = 0.45f,
            GlowBloom = 0.03f,
        };
        AddChild(new WorldEnvironment { Environment = environment });

        // Low afternoon sun from the south-west: the north-east approach is lit head-on from the base.
        var sun = new DirectionalLight3D
        {
            LightColor = new Color("ffdcae"),
            LightEnergy = 1.5f,
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 220f,
        };
        AddChild(sun);
        sun.RotationDegrees = new Vector3(-30f, -135f, 0f);

        // A ringed giant hangs over the garrison, Foss-style.
        var planet = new Node3D { Name = "Planet", Position = new Vector3(2700f, 1300f, -3300f) };
        AddChild(planet);
        planet.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 520f, Height = 1040f, RadialSegments = 48, Rings = 24 },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color("f0a27c"),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                DisableFog = true,
            },
        });
        var ring = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 950f, BottomRadius = 950f, Height = 2f, RadialSegments = 64, Rings = 1 },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.98f, 0.93f, 0.8f, 0.7f),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                DisableFog = true,
            },
            Rotation = new Vector3(0.35f, 0f, 0.42f),
        };
        planet.AddChild(ring);
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
                AlbedoColor = new Color("c7834d"),
                AlbedoTexture = new NoiseTexture2D
                {
                    Noise = new FastNoiseLite { Frequency = 0.025f, FractalOctaves = 4 },
                    Seamless = true,
                    Width = 512,
                    Height = 512,
                },
                Uv1Triplanar = true,
                Uv1Scale = Vector3.One * 0.05f,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
                Roughness = 1f,
            },
        });
        AddChild(ground);

        // Mesas and spires, kept clear of the base, the drop zone, the road to the garrison and every faction's base.
        var rng = new RandomNumberGenerator { Seed = 1975 };
        Color[] rock = { new("9c4a26"), new("b5651d"), new("7f3518"), new("cf8a52") };
        Vector2 road = new Vector2(GarrisonPosition.X, GarrisonPosition.Z);
        for (int i = 0; i < 140; i++)
        {
            var spot = new Vector2(rng.RandfRange(-2600f, 2600f), rng.RandfRange(-2600f, 2600f));
            float fromBase = spot.Length();
            float fromRoad = DistanceToSegment(spot, Vector2.Zero, road * 1.1f);
            if (fromBase < 760f || fromRoad < 120f || Neighbours.Any(n => spot.DistanceTo(new Vector2(n.Position.X, n.Position.Z)) < 150f)) continue;
            float width = rng.RandfRange(14f, 70f), height = rng.RandfRange(14f, 110f);
            bool spire = rng.Randf() < 0.3f;
            Vector3 size = spire
                ? new Vector3(width * 0.3f, height * 1.5f, width * 0.3f)
                : new Vector3(width, height * 0.45f, width * rng.RandfRange(0.6f, 1.4f));
            var mesa = new StaticBody3D { CollisionLayer = Layers.World, CollisionMask = 0 };
            mesa.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
            mesa.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = Palette.Mat(rock[i % rock.Length], 1f) });
            // A pale strata band near the top of the bigger mesas.
            if (!spire && size.Y > 20f)
                mesa.AddChild(new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(size.X + 0.4f, size.Y * 0.08f, size.Z + 0.4f) },
                    MaterialOverride = Palette.Mat(new Color("e0b07a"), 1f),
                    Position = new Vector3(0, size.Y * 0.3f, 0),
                });
            AddChild(mesa);
            mesa.Position = new Vector3(spot.X, size.Y * 0.5f, spot.Y);
            mesa.RotationDegrees = new Vector3(0f, rng.RandfRange(0f, 90f), 0f);
        }

        // Low boulders for texture near the base, outside the walls and off the approach lanes.
        for (int i = 0; i < 40; i++)
        {
            var spot = new Vector2(rng.RandfRange(-450f, 450f), rng.RandfRange(-450f, 450f));
            if (spot.Length() < 60f || spot.Length() > 450f) continue;
            float s = rng.RandfRange(1.2f, 3.5f);
            var boulder = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(s * 1.4f, s * 0.7f, s) },
                MaterialOverride = Palette.Mat(rock[i % rock.Length].Darkened(0.1f), 1f),
                Position = new Vector3(spot.X, s * 0.3f, spot.Y),
                RotationDegrees = new Vector3(rng.RandfRange(-8f, 8f), rng.RandfRange(0f, 90f), rng.RandfRange(-8f, 8f)),
            };
            AddChild(boulder);
        }
    }

    static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp((p - a).Dot(ab) / ab.LengthSquared(), 0f, 1f);
        return p.DistanceTo(a + ab * t);
    }
}
