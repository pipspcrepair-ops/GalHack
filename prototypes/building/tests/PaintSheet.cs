using GalHack.Structures;
using GalHack.Structures.Presentation;
using Godot;

namespace BlockBuilding;

/// <summary>
/// The paint sheet: each plating in a row, bare and then under a light paint, a dark paint and a
/// patterned one, in daylight, every cell a 2x2 patch of wall seen square on. Run windowed:
///   godot --path prototypes/building res://tests/PaintSheet.tscn -- --out=sheet.png
/// </summary>
public partial class PaintSheet : Node
{
    const int Patch = 2, Px = 360;
    static readonly Paint[] Coats =
    {
        default,
        Paint.Solid(0xe9dfc4),
        Paint.Solid(0x1c1f2e),
        Paint.Solid(0x0a9396).WithPattern(Pattern.Stripes, 0xe9dfc4),
    };

    string _out = "sheet.png";
    int _tick;
    SubViewport _view;

    public override void _Ready()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--out=")) _out = arg["--out=".Length..];
        Catalogue cat = PlatingKit.WithPrecursor();
        var s = new Structure(cat) { Name = "Sheet" };
        int rows = PlatingKit.Platings.Length, cols = Coats.Length;
        s.Set(new Int3(0, 0, -1), Building.Make(s, cat.CoreType));
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                for (int dx = 0; dx < Patch; dx++)
                    for (int dy = 0; dy < Patch; dy++)
                    {
                        var at = new Int3(c * Patch + dx, (rows - 1 - r) * Patch + dy, 0);
                        s.Set(at, Building.Make(s, cat.Type(PlatingKit.Platings[r])));
                        if (Coats[c] != default) Building.PaintFace(s, at, Dir.PosZ, Coats[c]);
                    }

        _view = new SubViewport { Size = new Vector2I(cols * Px, rows * Px), RenderTargetUpdateMode = SubViewport.UpdateMode.Always, Msaa3D = Viewport.Msaa.Msaa4X };
        AddChild(_view);
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial() },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
        };
        _view.AddChild(new WorldEnvironment { Environment = environment });
        var sun = new DirectionalLight3D { LightEnergy = 1.5f, ShadowEnabled = true };
        _view.AddChild(sun);
        sun.Basis = Basis.LookingAt(new Vector3(-0.45f, -0.55f, -0.7f).Normalized(), Vector3.Up);
        StructureView ship = StructureView.Create(s);
        _view.AddChild(ship);
        float w = cols * Patch, h = rows * Patch;
        var cam = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = h, Current = true };
        _view.AddChild(cam);
        cam.Position = new Vector3(w * 0.5f - 0.5f, h * 0.5f - 0.5f, 20f);
    }

    public override async void _PhysicsProcess(double delta)
    {
        if (++_tick != 90) return;
        await ToSignal(RenderingServer.Singleton, "frame_post_draw");
        Image image = _view.GetTexture().GetImage();
        Error result = image.SavePng(_out);
        GD.Print(result == Error.Ok ? $"saved {_out}" : $"could not save {_out}: {result}");
        GetTree().Quit(result == Error.Ok ? 0 : 1);
    }
}
