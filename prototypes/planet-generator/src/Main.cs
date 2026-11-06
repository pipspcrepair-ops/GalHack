using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Godot;
using Planets.Gen;
using Planets.Look;
using Planets.Orbit;
using Planets.Surface;

namespace Planets;

/// <summary>
/// Builds the prototype in code and switches between its two views: the planet from orbit (the
/// main view) and its surface. Planets are generated on a worker thread, so changing planets never
/// freezes the globe.
/// </summary>
public partial class Main : Node3D
{
    sealed record Built(PlanetSpec Spec, WorldMap Map, ArchetypeLook Look, ColorMap Colors, byte[] Clouds, byte[] Landforms, GlobeRelief.Data Globe, double Ms);

    const int CloudWidth = 1024;

    public PlanetSpec Spec { get; private set; }
    public WorldMap Map { get; private set; }
    public ArchetypeLook Look { get; private set; }
    public OrbitView Orbit { get; private set; }
    public SurfaceView Surface { get; private set; }

    /// <summary>True while the planet on show is not yet the current spec.</summary>
    public bool Busy => _job != null || Map == null || Map.Spec != Spec;

    Task<Built> _job;
    Built _built;
    bool _showLandforms;
    VBoxContainer _legend;
    int _reroll;
    double _buildMs, _uploadMs;
    CanvasLayer _ui;
    Label _label;
    Control _flat;
    TextureRect _flatMap;

    public override void _Ready()
    {
        Orbit = new OrbitView { Name = "Orbit" };
        AddChild(Orbit);
        BuildUi();
        SetSpec(PlanetSpec.Roll(1977, Archetype.Temperate, 0));
    }

    public override void _ExitTree()
    {
        // While landed the orbit view is detached rather than freed; free it with everything else.
        if (Orbit != null && !Orbit.IsInsideTree()) Orbit.Free();
    }

    public void SetSpec(PlanetSpec spec)
    {
        Spec = spec;
        if (_job == null) StartBuild(); // otherwise the running job notices it is stale and restarts
    }

    void StartBuild()
    {
        PlanetSpec spec = Spec;
        _job = Task.Run(() =>
        {
            var watch = Stopwatch.StartNew();
            WorldMap map = PlanetGenerator.Generate(spec);
            ArchetypeLook look = ArchetypeLook.For(spec);
            var colors = ColorMap.Build(map, look, Math.Clamp(map.Width * 4, 2048, 4096));
            byte[] clouds = CloudMap.Build(spec, look, CloudWidth, out _);
            byte[] landforms = LandformLook.Albedo(map, colors.Width);
            GlobeRelief.Data globe = GlobeRelief.Build(map);
            return new Built(spec, map, look, colors, clouds, landforms, globe, watch.Elapsed.TotalMilliseconds);
        });
    }

    public override void _Process(double delta)
    {
        if (_job is { IsCompleted: true })
        {
            Task<Built> job = _job;
            _job = null;
            if (job.IsFaulted) GD.PushError($"planet generation failed: {job.Exception}");
            else if (job.Result.Spec != Spec) StartBuild();
            else Show(job.Result);
        }
        UpdateLabel();
    }

    void Show(Built built)
    {
        var watch = Stopwatch.StartNew();
        Map = built.Map;
        Look = built.Look;
        _buildMs = built.Ms;
        _built = built;
        Orbit.ShowRelief(GlobeRelief.Mesh(built.Globe));
        ShowMaps();
        _uploadMs = watch.Elapsed.TotalMilliseconds;
    }

    /// <summary>The globe and the flat map, in the planet's own colours or tinted by landform (L).</summary>
    void ShowMaps()
    {
        if (_built == null) return;
        ColorMap c = _built.Colors;
        byte[] albedo = ShowLandforms ? _built.Landforms : c.Albedo;
        Orbit.Show(_built.Spec, _built.Look,
            Texture(c.Width, c.Height, Image.Format.Rgba8, albedo),
            Texture(c.Width, c.Height, Image.Format.Rgb8, ShowLandforms ? new byte[c.Width * c.Height * 3] : c.Glow),
            Texture(CloudWidth, CloudWidth / 2, Image.Format.Rgba8, ShowLandforms ? new byte[CloudWidth * CloudWidth / 2 * 4] : _built.Clouds));
        byte[] flat = c.Composite();
        if (ShowLandforms)
        {
            flat = new byte[c.Width * c.Height * 3];
            for (int p = 0; p < c.Width * c.Height; p++)
                for (int k = 0; k < 3; k++) flat[p * 3 + k] = _built.Landforms[p * 4 + k];
        }
        _flatMap.Texture = ImageTexture.CreateFromImage(Image.CreateFromData(c.Width, c.Height, false, Image.Format.Rgb8, flat));
        BuildLegend();
    }

    /// <summary>True while the globe and flat map show landforms (each its key colour) instead of the planet's colours.</summary>
    public bool ShowLandforms
    {
        get => _showLandforms;
        set
        {
            _showLandforms = value;
            ShowMaps();
        }
    }

    /// <summary>The landform key: one swatch per landform this world really has, largest first.</summary>
    void BuildLegend()
    {
        foreach (Node child in _legend.GetChildren()) child.QueueFree();
        _legend.Visible = _showLandforms && Map != null;
        if (!_legend.Visible) return;
        var title = new Label { Text = Map.Model.Mix.Single ? "One landform throughout" : "Landforms" };
        Style(title, 18);
        _legend.AddChild(title);
        foreach (var (landform, share) in Map.Landforms.Present(0.004f))
        {
            var row = new HBoxContainer();
            Rgb c = LandformLook.Color(landform);
            row.AddChild(new ColorRect { Color = new Color(c.R, c.G, c.B), CustomMinimumSize = new Vector2(22, 22) });
            var name = new Label { Text = $" {LandformInfo.Name(landform)}  {share:P0}" };
            Style(name, 16);
            row.AddChild(name);
            _legend.AddChild(row);
        }
    }

    static void Style(Label label, int size)
    {
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", new Color("ffe8c2"));
        label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
        label.AddThemeConstantOverride("outline_size", 6);
    }

    static ImageTexture Texture(int width, int height, Image.Format format, byte[] data)
    {
        Image image = Image.CreateFromData(width, height, false, format, data);
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    // ---- Input --------------------------------------------------------------------------

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (Surface != null)
        {
            if (key.PhysicalKeycode == Key.Escape) ReturnToOrbit();
            return;
        }
        switch (key.PhysicalKeycode)
        {
            case >= Key.Key1 and <= Key.Key5:
                SetSpec(Spec with { Archetype = (Archetype)(key.PhysicalKeycode - Key.Key1), Variant = 0 });
                break;
            case Key.V:
                SetSpec(Spec with { Variant = (Spec.Variant + 1) % PlanetSpec.VariantCount });
                break;
            case Key.N:
                ulong seed = (ulong)Random.Shared.NextInt64(1, 1_000_000);
                _reroll = 0;
                SetSpec(PlanetSpec.Roll(seed, Spec.Archetype, Spec.Variant, Spec.WidthKm));
                break;
            case Key.M:
                SetSpec(Spec with { Modifiers = PlanetSpec.RollModifiers(Spec.Seed, ++_reroll) });
                break;
            case Key.S:
                int next = (Array.IndexOf(PlanetSpec.Widths, Spec.WidthKm) + 1) % PlanetSpec.Widths.Length;
                SetSpec(Spec with { WidthKm = PlanetSpec.Widths[next] });
                break;
            case Key.Tab:
                _flat.Visible = !_flat.Visible;
                break;
            case Key.L:
                ShowLandforms = !ShowLandforms;
                break;
            case Key.Enter or Key.KpEnter:
                Land();
                break;
        }
    }

    /// <summary>Lands on a riverside or coastal spot near the part of the globe facing the camera.</summary>
    public void Land()
    {
        if (Busy || Surface != null) return;
        var (x, y) = Orbit.FacingPoint(Map);
        LandingSite site = Landing.Find(Map, x, y);
        RemoveChild(Orbit);
        _ui.Visible = false;
        Surface = new SurfaceView { Name = "Surface", Map = Map, Look = Look, Landing = site };
        AddChild(Surface);
    }

    public void ReturnToOrbit()
    {
        if (Surface == null) return;
        Surface.QueueFree();
        Surface = null;
        AddChild(Orbit);
        Orbit.Camera.MakeCurrent();
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _ui.Visible = true;
    }

    // ---- UI -----------------------------------------------------------------------------

    /// <summary>Hides the orbit text (for clean screenshots); the flat map still toggles with Tab.</summary>
    public void HideLabel() => _label.Visible = false;

    void BuildUi()
    {
        _ui = new CanvasLayer();
        AddChild(_ui);

        _flat = new ColorRect { Color = new Color(0.03f, 0.03f, 0.06f, 0.92f), Visible = false };
        _flat.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _ui.AddChild(_flat);
        _flatMap = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = CanvasItem.TextureFilterEnum.Linear,
        };
        _flatMap.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _flatMap.OffsetLeft = 40;
        _flatMap.OffsetRight = -40;
        _flatMap.OffsetTop = 150;
        _flatMap.OffsetBottom = -40;
        _flat.AddChild(_flatMap);

        _legend = new VBoxContainer { Visible = false };
        _legend.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _legend.Position = new Vector2(-250f, 16f);
        _legend.CustomMinimumSize = new Vector2(230f, 0f);
        _ui.AddChild(_legend);

        _label = new Label { Position = new Vector2(16f, 12f) };
        _label.AddThemeFontSizeOverride("font_size", 16);
        _label.AddThemeColorOverride("font_color", new Color("ffe8c2"));
        _label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
        _label.AddThemeConstantOverride("outline_size", 6);
        _ui.AddChild(_label);
    }

    void UpdateLabel()
    {
        if (Map == null)
        {
            _label.Text = "Generating...";
            return;
        }
        string status = Busy ? "   (generating the next planet...)" : "";
        _label.Text =
            $"{Map.Spec}{status}\n" +
            $"generated in {Map.GenerationMs:F0} ms (map) + {_buildMs - Map.GenerationMs:F0} ms (colour and cloud maps) on a worker, " +
            $"{_uploadMs:F0} ms texture upload   ·   {Map.Width}x{Map.Height} cells of {Map.CellSize:F0} m\n" +
            $"land {Map.LandFraction:P0}   rivers {Map.Rivers.Count}   hazard: {Map.Def.Hazard}   resources: {Map.Def.Resources}\n" +
            $"landforms: {Map.Model.Mix}\n\n" +
            "1-5 archetype  ·  V variant  ·  N new seed  ·  M reroll modifiers  ·  S size  ·  Tab flat map  ·  L landforms  ·  Enter land\n" +
            $"{Engine.GetFramesPerSecond()} fps";
    }
}
