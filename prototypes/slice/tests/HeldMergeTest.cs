using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Slice.Arms;

namespace Slice.Tests;

/// <summary>
/// A held weapon's parts merged into one mesh (HeldFinish.Merge, as townsfolk carry them) draw as the parts did: every weapon
/// in the table drawn both ways, side on and from above, in the same light, and the pictures compared pixel by pixel.
/// Run windowed (it draws): godot --path prototypes/slice res://tests/HeldMergeTest.tscn [-- --out=dir]
/// </summary>
public partial class HeldMergeTest : Node
{
    SubViewport _view;
    Camera3D _camera;
    int _failures, _passes;

    public override void _Ready()
    {
        new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(5));
            Console.Error.WriteLine("FAIL  the run took over 5 minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true }.Start();
        _view = new SubViewport { Size = new Vector2I(512, 256), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always, Msaa3D = Viewport.Msaa.Disabled };
        AddChild(_view);
        var world = new Node3D();
        _view.AddChild(world);
        world.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50f, 30f, 0f), LightEnergy = 1.2f });
        world.AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.2f, 0.2f, 0.22f), AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.5f, 0.5f, 0.55f) } });
        _camera = new Camera3D { Fov = 30f };
        world.AddChild(_camera);
        Run(world);
    }

    async void Run(Node3D world)
    {
        string outDir = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--out="))?[6..];
        foreach (var def in GunModel.Rows.All.Where(d => d.Look != null).Take(40))
        {
            GunModel.Built parts = GunModel.Build(def), merged = GunModel.Build(def);
            Player.HeldFinish.Merge(merged.Root);
            int before = parts.Root.GetChildren().OfType<MeshInstance3D>().Count(), after = merged.Root.GetChildren().OfType<MeshInstance3D>().Count();
            double worst = 0, mean = 0;
            foreach (var (eye, look) in new[] { (new Vector3(0f, 0.05f, 1.1f), Vector3.Zero), (new Vector3(0.2f, 1.0f, 0.1f), Vector3.Zero) })
            {
                Image a = await Shot(world, parts.Root, eye, look), b = await Shot(world, merged.Root, eye, look);
                var (w, m) = Compare(a, b);
                worst = Math.Max(worst, w);
                mean = Math.Max(mean, m);
                if (outDir != null && w > 0.02)
                {
                    a.SavePng($"{outDir}/{def.Id}-parts.png");
                    b.SavePng($"{outDir}/{def.Id}-merged.png");
                }
            }
            Check(worst < 0.02 && after <= Math.Max(1, before), string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"{def.Id}: {before} meshes drawn as {after}, pixels differ by {worst:F4} at most (mean {mean:F5})"));
            parts.Root.Free();
            merged.Root.Free();
        }
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    async System.Threading.Tasks.Task<Image> Shot(Node3D world, Node3D model, Vector3 eye, Vector3 look)
    {
        world.AddChild(model);
        _camera.Position = eye;
        _camera.LookAt(look, Mathf.Abs(eye.Normalized().Y) > 0.9f ? Vector3.Forward : Vector3.Up);
        // A few frames: its shaders compiled and drawn.
        for (int i = 0; i < 4; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Image img = _view.GetTexture().GetImage();
        world.RemoveChild(model);
        return img;
    }

    static (double Worst, double Mean) Compare(Image a, Image b)
    {
        double worst = 0, sum = 0;
        int n = 0;
        for (int y = 0; y < a.GetHeight(); y++)
            for (int x = 0; x < a.GetWidth(); x++)
            {
                Color p = a.GetPixel(x, y), q = b.GetPixel(x, y);
                double d = Math.Max(Math.Abs(p.R - q.R), Math.Max(Math.Abs(p.G - q.G), Math.Abs(p.B - q.B)));
                worst = Math.Max(worst, d);
                sum += d;
                n++;
            }
        return (worst, sum / n);
    }

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"  {(ok ? "ok  " : "FAIL")}  {what}");
    }
}
