using System;
using GalHack.Structures;
using Godot;
using Slice.Vessels;
using Structure = GalHack.Structures.Structure;

namespace Slice.Tests;

/// <summary>
/// Frames of lights in each mode in a dark room, for the proof GIF. Run windowed:
///   godot --path prototypes/slice res://tests/LightShots.tscn --fixed-fps 60 -- --out=DIR
/// Writes DIR/f000.png ... (every 4th tick, so 15 per second). Not a *Test scene, so the gate doesn't run it.
/// </summary>
public partial class LightShots : Node
{
    string _out = ".";
    int _tick;
    const int Frames = 90;

    public override void _Ready()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--out=")) _out = arg[6..];
        System.IO.Directory.CreateDirectory(_out);
        Catalogue cat = Slice.Game.GameData.Shared.Catalogue;
        var s = new Structure(cat, cat.Grid(GridSize.Large));
        BlockType steel = cat.Type("steel"), lamp = System.Linq.Enumerable.First(cat.Types, t => t != null && t.IsLight);
        Block Make(BlockType t) => Building.Make(s, t, null, 0);
        for (int x = -6; x <= 6; x++)
        {
            for (int y = 0; y <= 3; y++) s.Set(new Int3(x, y, 0), Make(steel));          // the back wall
            for (int z = -8; z <= 0; z++) { s.Set(new Int3(x, -1, z), Make(steel)); s.Set(new Int3(x, 4, z), Make(steel)); }
        }
        // Five lights, one per mode.
        var styles = new[]
        {
            (-4, LightSettings.Make(0xffddaa, LightAnim.Steady)),
            (-2, LightSettings.Make(0xff3322, LightAnim.Pulse, 0.5f, 0)),
            (0, LightSettings.Make(0xffaa44, LightAnim.Flicker, 0.5f, 5)),
            (2, LightSettings.Make(0xffffff, LightAnim.Strobe, 0.5f, 9)),
            (4, LightSettings.Make(0x33ddff, LightAnim.Fade, 0.25f, 0, 0xff66aa)),
        };
        foreach (var (x, st) in styles)
        {
            s.Set(new Int3(x, 2, -1), Make(lamp));
            s.SetLight(new Int3(x, 2, -1), st);
            // And glowing paint under each, in the same mode: a panel, a strip, a border.
            LampShape shape = x switch { -4 => LampShape.Full, -2 => LampShape.Strip, 0 => LampShape.Panel, 2 => LampShape.Border, _ => LampShape.Strip };
            Building.PaintFace(s, new Int3(x, 0, 0), Dir.NegZ, Paint.Lit(shape, st));
        }
        var mesh = new StructureMesh { Name = "Room" };
        AddChild(mesh);
        mesh.Setup(s);
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = Colors.Black,
            AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.Black,
            GlowEnabled = true, GlowIntensity = 0.6f,
        };
        AddChild(new WorldEnvironment { Environment = env });
        var cam = new Camera3D { Fov = 62, Current = true, Position = new Vector3(0, 3.2f, -15) };
        AddChild(cam);
        cam.LookAt(new Vector3(0, 2.4f, 0));
    }

    public override void _Process(double delta)
    {
        _tick++;
        if (_tick % 4 != 0) return;
        int n = _tick / 4;
        if (n < 8) return; // let the meshes build
        int f = n - 8;
        GetViewport().GetTexture().GetImage().SavePng($"{_out}/f{f:D3}.png");
        if (f >= Frames) GetTree().Quit();
    }
}
