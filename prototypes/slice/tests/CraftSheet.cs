using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Structures;
using GalHack.Structures.Presentation;
using Godot;
using Slice.Game;
using Structure = GalHack.Structures.Structure;

namespace Slice.Tests;

/// <summary>
/// The small craft's body plans on one contact sheet (docs/ship-forms.md's gate): a row per plan, traders then scouts,
/// the box first; three fresh rolls each, at a 3/4 view from a little above, every one at the same scale. Two sheets:
/// greyed (the form alone) and in Freeholder livery. Run windowed:
///   godot-q.ps1 -Console --path prototypes/slice res://tests/CraftSheet.tscn -After --dir=PATH
/// </summary>
public partial class CraftSheet : Node
{
    const int Cell = 600, CellH = 400, Rolls = 3;

    /// <summary>The camera's height across a cell, m: the same for every craft, so their sizes compare.</summary>
    const float Span = 26f;

    SubViewport _view;
    Camera3D _camera;
    Node3D _model;
    IEnumerator<int> _run;
    string _dir = ".";

    public override void _Ready()
    {
        new System.Threading.Thread(() => { System.Threading.Thread.Sleep(TimeSpan.FromMinutes(10)); Console.Error.WriteLine("CraftSheet: watchdog"); System.Environment.Exit(2); }) { IsBackground = true }.Start();
        foreach (string a in OS.GetCmdlineUserArgs())
            if (a.StartsWith("--dir=")) _dir = a[6..];
        System.IO.Directory.CreateDirectory(_dir);
        Boot.ResourceMount.Mount();
        Build();
        _run = Run().GetEnumerator();
    }

    public override void _Process(double delta)
    {
        if (_run.MoveNext()) return;
        SetProcess(false);
        GetTree().Quit();
    }

    IEnumerable<int> Run()
    {
        GameData data = GameData.Shared;
        var rng = new Random();
        var rows = new List<(CraftRole Role, CraftForm Form)>();
        foreach (CraftRole role in new[] { CraftRole.Trader, CraftRole.Scout })
            foreach (CraftForm form in SmallCraft.FormsOf[role]) rows.Add((role, form));
        var grey = Image.CreateEmpty(Cell * Rolls, CellH * rows.Count, false, Image.Format.Rgba8);
        var paint = Image.CreateEmpty(Cell * Rolls, CellH * rows.Count, false, Image.Format.Rgba8);
        grey.Fill(new Color(0.16f, 0.17f, 0.19f));
        paint.Fill(new Color(0.16f, 0.17f, 0.19f));
        for (int r = 0; r < rows.Count; r++)
            for (int c = 0; c < Rolls; c++)
            {
                Structure s = null;
                string why = null;
                for (int i = 0; i < 8 && s == null; i++)
                    s = SmallCraft.Roll(data.Catalogue, rows[r].Role, CraftStyle.Freeholder, rng.Next(), data.Rules.Planet.Gravity, out why, null, rows[r].Form);
                if (s == null) { GD.PrintErr($"{rows[r].Role} {rows[r].Form}: no roll ({why})"); continue; }
                var (lo, hi) = s.Bounds();
                GD.Print($"{rows[r].Role} {rows[r].Form} roll {c + 1}: {s.BlockCount} blocks, {hi.X - lo.X + 1} x {hi.Y - lo.Y + 1} x {hi.Z - lo.Z + 1} m{(SmallCraft.Dropped.Length > 0 ? "; would not go on: " + SmallCraft.Dropped : "")}");
                foreach (bool greyed in new[] { true, false })
                {
                    Show(s, greyed);
                    for (int f = 0; f < 4; f++) yield return 0;
                    Image shot = _view.GetTexture().GetImage();
                    shot.Convert(Image.Format.Rgba8);
                    (greyed ? grey : paint).BlitRect(shot, new Rect2I(0, 0, Cell, CellH), new Vector2I(c * Cell, r * CellH));
                }
            }
        foreach (var (image, name) in new[] { (grey, "craft-forms-grey.png"), (paint, "craft-forms-livery.png") })
        {
            string file = System.IO.Path.Combine(_dir, name);
            GD.Print(image.SavePng(file) == Error.Ok ? $"saved {file}" : $"could not save {file}");
        }
        GD.Print($"rows: {string.Join(", ", rows.Select(x => $"{x.Role} {x.Form}"))}");
    }

    void Build()
    {
        _view = new SubViewport { Size = new Vector2I(Cell, CellH), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always, Msaa3D = Viewport.Msaa.Msaa4X };
        AddChild(_view);
        _view.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.16f, 0.17f, 0.19f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.72f, 0.75f, 0.82f), AmbientLightEnergy = 0.7f,
            },
        });
        var sun = new DirectionalLight3D { LightEnergy = 1.25f, ShadowEnabled = true };
        _view.AddChild(sun);
        sun.LookAtFromPosition(new Vector3(-4, 6, -3), Vector3.Zero);
        var fill = new DirectionalLight3D { LightEnergy = 0.35f, LightColor = new Color(0.6f, 0.75f, 1f) };
        _view.AddChild(fill);
        fill.LookAtFromPosition(new Vector3(3, 1, 4), Vector3.Zero);
        _camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = Span, Current = true, Near = 1f, Far = 400f };
        _view.AddChild(_camera);
    }

    /// <summary>A craft in the cell: centred, its nose to the left and toward the viewer, seen a little from above.</summary>
    void Show(Structure s, bool greyed)
    {
        _model?.QueueFree();
        _model = new Node3D { Name = "Model" };
        var data = new ChunkMeshData();
        var flat = new StandardMaterial3D { AlbedoColor = new Color(0.62f, 0.63f, 0.65f), Roughness = 0.85f };
        foreach (Int3 chunk in s.Chunks.Keys)
        {
            ChunkMesher.Build(s, chunk, data);
            _model.AddChild(new MeshInstance3D { Mesh = ChunkMesher.ToMesh(data), MaterialOverride = greyed ? flat : null });
        }
        _view.AddChild(_model);
        var (min, max) = s.Bounds();
        float cell = s.Grid.CellSize;
        Vector3 centre = (new Vector3(min.X, min.Y, min.Z) + new Vector3(max.X, max.Y, max.Z)) * 0.5f * cell;
        Vector3 dir = new Vector3(-0.85f, 0.45f, -0.75f).Normalized();
        _camera.LookAtFromPosition(centre + dir * 150f, centre);
    }
}
