using System;
using GalHack.Structures;
using GalHack.Structures.Presentation;
using Godot;
using Structure = GalHack.Structures.Structure;

namespace Slice.Ui;

/// <summary>
/// The BLUEPRINTS tab's turntable: the selected blueprint drawn with building's own mesher and
/// materials in a small world of its own, turning slowly until it is dragged round (or zoomed with
/// the wheel). It renders only while the tab shows it; a headless run has none.
/// </summary>
public partial class BlueprintPreview : Node
{
    SubViewport _view;
    Node3D _model;
    Camera3D _camera;
    MeshInstance3D _floor;
    Structure _shown;
    Vector3 _centre;
    float _radius = 4f;
    ulong _asked;

    /// <summary>The camera's turn round the model and its height above it, radians; how far out it stands (1 = fits the view).</summary>
    public float Yaw = 2.4f, Pitch = 0.42f, Zoom = 1f;

    /// <summary>Held still while it is being dragged.</summary>
    public bool Dragging;

    public static bool CanRender => ItemIcons.CanRender;

    /// <summary>The picture of a blueprint at a size in pixels (null when nothing can render). Asking keeps it rendering.</summary>
    public Texture2D Picture(Structure plan, Vector2I size)
    {
        if (!CanRender || plan == null) return null;
        if (_view == null) Build();
        if (plan != _shown) Show(plan);
        size = new Vector2I(Math.Clamp(size.X, 64, 2048), Math.Clamp(size.Y, 64, 2048));
        if (_view.Size != size) _view.Size = size;
        _asked = Time.GetTicksMsec();
        _view.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        return _view.GetTexture();
    }

    public override void _Process(double delta)
    {
        if (_view == null) return;
        if (Time.GetTicksMsec() - _asked > 300)
        {
            _view.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
            return;
        }
        if (!Dragging) Yaw += (float)delta * 0.25f;
        Pitch = Math.Clamp(Pitch, -0.2f, 1.45f);
        Zoom = Math.Clamp(Zoom, 0.45f, 2.5f);
        float distance = _radius / MathF.Tan(Mathf.DegToRad(_camera.Fov * 0.5f)) * 1.05f * Zoom;
        var dir = new Vector3(MathF.Sin(Yaw) * MathF.Cos(Pitch), MathF.Sin(Pitch), MathF.Cos(Yaw) * MathF.Cos(Pitch));
        _camera.LookAtFromPosition(_centre + dir * distance, _centre);
        _camera.Near = MathF.Max(0.05f, distance - _radius * 2f);
        _camera.Far = distance + _radius * 4f;
    }

    void Build()
    {
        _view = new SubViewport
        {
            Name = "BlueprintView", Size = new Vector2I(640, 480), TransparentBg = true, OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always, Msaa3D = Viewport.Msaa.Msaa4X,
        };
        AddChild(_view);
        _view.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0, 0, 0, 0),
                AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.72f, 0.75f, 0.82f), AmbientLightEnergy = 0.8f,
            },
        });
        var sun = new DirectionalLight3D { LightEnergy = 1.2f, ShadowEnabled = false };
        _view.AddChild(sun);
        sun.LookAtFromPosition(new Vector3(-3, 5, -2), Vector3.Zero);
        var fill = new DirectionalLight3D { LightEnergy = 0.35f, ShadowEnabled = false, LightColor = new Color(0.6f, 0.75f, 1f) };
        _view.AddChild(fill);
        fill.LookAtFromPosition(new Vector3(3, 1, 4), Vector3.Zero);
        _camera = new Camera3D { Fov = 30, Current = true };
        _view.AddChild(_camera);
        _floor = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 1f, BottomRadius = 1f, Height = 0.02f, RadialSegments = 48 },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(DiegeticUi.Theme.UiTheme.Colors.Holo, 0.16f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
        };
        _view.AddChild(_floor);
    }

    void Show(Structure plan)
    {
        _shown = plan;
        _model?.QueueFree();
        _model = new Node3D { Name = "Model" };
        var data = new ChunkMeshData();
        foreach (Int3 chunk in plan.Chunks.Keys)
        {
            ChunkMesher.Build(plan, chunk, data);
            _model.AddChild(new MeshInstance3D { Mesh = ChunkMesher.ToMesh(data) });
        }
        _view.AddChild(_model);
        var (min, max) = plan.Bounds();
        float cell = plan.Grid.CellSize;
        Vector3 lo = new Vector3(min.X, min.Y, min.Z) * cell - Vector3.One * cell * 0.5f;
        Vector3 hi = new Vector3(max.X, max.Y, max.Z) * cell + Vector3.One * cell * 0.5f;
        _centre = (lo + hi) * 0.5f;
        _radius = MathF.Max(0.5f, (hi - lo).Length() * 0.5f);
        _floor.Scale = new Vector3(_radius * 1.15f, 1f, _radius * 1.15f);
        _floor.Position = new Vector3(_centre.X, lo.Y - 0.02f, _centre.Z);
        Zoom = 1f;
    }
}
