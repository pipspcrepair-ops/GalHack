using Godot;
using Slice.Player;

namespace Slice.Ui;

/// <summary>
/// The paperdoll: your own character model, lit and turning slowly in a small world of its own
/// (a SubViewport), drawn into the YOU tab between the equipment slots. It renders only while
/// the menu is open.
/// </summary>
public partial class Paperdoll : SubViewport
{
    public Game.Game Game;
    Node3D _model;

    public Texture2D Texture => GetTexture();

    /// <summary>The paperdoll for a game, made on first use.</summary>
    public static Paperdoll Attach(Game.Game game)
    {
        if (game.GetNodeOrNull<Paperdoll>("Paperdoll") is Paperdoll existing) return existing;
        var doll = new Paperdoll { Name = "Paperdoll", Game = game };
        game.AddChild(doll);
        return doll;
    }

    public override void _Ready()
    {
        OwnWorld3D = true;
        TransparentBg = true;
        Size = new Vector2I(360, 570);
        RenderTargetUpdateMode = UpdateMode.Always;
        _model = CharacterModel.Build();
        AddChild(_model);
        var camera = new Camera3D { Fov = 30f, Current = true, CullMask = 0xfffff };
        AddChild(camera);
        camera.LookAtFromPosition(new Vector3(0f, 1.05f, -4.1f), new Vector3(0f, 0.95f, 0f), Vector3.Up);
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-35, 150, 0), LightEnergy = 1.4f });
        AddChild(new OmniLight3D { Position = new Vector3(1.5f, 1.8f, 1.5f), LightColor = new Color("2ec4b6"), LightEnergy = 1.5f, OmniRange = 6f });
        var env = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.ClearColor, AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.35f, 0.35f, 0.4f) };
        AddChild(new WorldEnvironment { Environment = env });
    }

    public override void _Process(double delta)
    {
        bool open = Game?.Menu?.IsOpen == true && Game.Menu.Tab == MenuTab.You;
        RenderTargetUpdateMode = open ? UpdateMode.Always : UpdateMode.Disabled;
        if (open) _model.RotateY((float)delta * 0.4f);
    }
}
