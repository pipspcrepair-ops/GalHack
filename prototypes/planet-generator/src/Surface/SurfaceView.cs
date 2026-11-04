using System;
using Godot;
using Planets.Gen;
using Planets.Look;

namespace Planets.Surface;

/// <summary>
/// Standing on the planet: streamed terrain with biome colours and scatter, sea and rivers, a sky
/// and fog per archetype, the polar walls at the edges, and a free camera. World space is map space
/// (x east, z south, metres), with the camera kept within one map width so floats stay precise.
/// </summary>
public partial class SurfaceView : Node3D
{
    internal WorldMap Map;
    internal ArchetypeLook Look;
    internal LandingSite Landing;

    public FreeCamera Camera { get; private set; }
    public ChunkStreamer Streamer { get; private set; }

    SurfacePalette _palette;
    MeshInstance3D _sea;
    Label _hud;
    double _width;

    public override void _Ready()
    {
        _width = Map.WidthMeters;
        _palette = new SurfacePalette(Map, Look);
        var water = WaterMaterial();

        BuildSky();
        Streamer = new ChunkStreamer(_palette, water) { Name = "Terrain" };
        AddChild(Streamer);

        _sea = new MeshInstance3D
        {
            Name = "Sea",
            Mesh = new PlaneMesh { Size = new Vector2(16000f, 16000f) },
            MaterialOverride = water,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_sea);
        AddChild(new CapWalls { Name = "Caps", Map = Map, Look = Look });

        Camera = new FreeCamera { Name = "Camera", Fov = 72f, Near = 0.2f, Far = 9000f };
        AddChild(Camera);
        Camera.MakeCurrent();
        // Step back from the water's edge so the first view has the river or coast in it.
        double back = 70;
        double x = Landing.X - Math.Sin(Landing.Heading) * back, z = Landing.Y + Math.Cos(Landing.Heading) * back;
        float ground = _palette.Sampler.HeightAt(x, z);
        Camera.Position = new Vector3((float)Mathx.WrapX(x, _width), Math.Max(ground, 0f) + 16f, (float)z);
        Camera.SetLook(Landing.Heading, -0.16f);

        BuildHud();
        Streamer.Stream(Camera.Position.X, Camera.Position.Z);
    }

    public override void _Process(double delta)
    {
        Camera.Fly((float)delta);
        KeepInBounds();
        Vector3 p = Camera.Position;
        Streamer.Stream(p.X, p.Z);
        _sea.Position = new Vector3(p.X, 0f, p.Z);
        UpdateHud();
    }

    /// <summary>Wrap east-west, stop at the polar walls, never dip underground.</summary>
    void KeepInBounds()
    {
        Vector3 p = Camera.Position;
        if (p.X < 0f) p.X += (float)_width;
        else if (p.X >= _width) p.X -= (float)_width;
        float band = (float)Map.Model.CapBand;
        p.Z = Mathf.Clamp(p.Z, band * 0.3f, (float)Map.HeightMeters - band * 0.3f);
        float ground = Math.Max(_palette.Sampler.HeightAt(p.X, p.Z, 2), 0f);
        p.Y = Mathf.Clamp(p.Y, ground + 1.5f, 2500f);
        Camera.Position = p;
    }

    /// <summary>Puts the camera at a map position (x, z) at a height above the ground, facing a heading.</summary>
    public void PlaceCamera(double x, double z, float aboveGround, float heading, float pitch)
    {
        float ground = Math.Max(_palette.Sampler.HeightAt(x, z), 0f);
        Camera.Position = new Vector3((float)Mathx.WrapX(x, _width), ground + aboveGround, (float)z);
        Camera.SetLook(heading, pitch);
    }

    // ---- Sky, light and liquids ---------------------------------------------------------

    void BuildSky()
    {
        Rgb horizon = Look.SkyHorizon;
        var sky = new ShaderMaterial { Shader = Shaders.Sky };
        sky.SetShaderParameter("top_color", C(Look.SkyTop));
        sky.SetShaderParameter("horizon_color", C(horizon));
        sky.SetShaderParameter("ground_color", C(horizon * 0.55f));
        sky.SetShaderParameter("sun_color", C(Look.SunColor));
        sky.SetShaderParameter("moon_color", C(Look.Moon));
        sky.SetShaderParameter("moon_size", Look.MoonSize);
        sky.SetShaderParameter("moon_dir", Direction(Landing.Heading + 0.45f, 0.2f));
        sky.SetShaderParameter("ring", Look.Ring ? 1f : 0f);
        sky.SetShaderParameter("ring_color", C(Look.RingColor));
        sky.SetShaderParameter("stars", Look.Stars);
        sky.SetShaderParameter("cloud_color", C(Look.CloudColor));
        sky.SetShaderParameter("cloud_cover", Look.CloudCover);

        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = sky, RadianceSize = Sky.RadianceSizeEnum.Size128 },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = Look.Ambient,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            TonemapWhite = 6f,
            // Paperback-cover grading: fog and filmic tonemapping both wash colour out, so push it back.
            AdjustmentEnabled = true,
            AdjustmentSaturation = 1.15f,
            AdjustmentContrast = 1.05f,
            FogEnabled = true,
            FogLightColor = C(horizon),
            FogDensity = Look.FogDensity,
            FogSunScatter = 0.25f,
            FogAerialPerspective = 0.35f,
            FogSkyAffect = 0.0f,
            GlowEnabled = true,
            GlowIntensity = 0.7f,
            GlowBloom = 0.04f,
            GlowHdrThreshold = 1.1f,
        };
        AddChild(new WorldEnvironment { Environment = environment });

        var sun = new DirectionalLight3D
        {
            Name = "Sun",
            LightColor = C(Look.SunColor),
            LightEnergy = Look.SunEnergy,
            ShadowEnabled = true,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits,
            DirectionalShadowMaxDistance = 260f,
        };
        AddChild(sun);
        // Light from the side of the landing view, for long shadows and readable relief.
        float azimuth = Landing.Heading + 2.1f;
        sun.Rotation = new Vector3(-Mathf.DegToRad(Look.SunElevation), -azimuth + Mathf.Pi, 0f);
    }

    ShaderMaterial WaterMaterial()
    {
        var material = new ShaderMaterial { Shader = Shaders.Water };
        material.SetShaderParameter("sea_map", SeaTexture());
        material.SetShaderParameter("map_size", new Vector2((float)Map.WidthMeters, (float)Map.HeightMeters));
        material.SetShaderParameter("shallow", C(Look.ShallowSea));
        material.SetShaderParameter("deep", C(Look.DeepSea));
        material.SetShaderParameter("ice", C(Look.FrozenSea));
        material.SetShaderParameter("sky", C(Look.SkyHorizon));
        material.SetShaderParameter("lava", Map.Def.Liquid == Liquid.Lava ? 1f : 0f);
        material.SetShaderParameter("glow", Map.Def.Liquid == Liquid.Lava ? 2.2f : Map.Def.Liquid == Liquid.Exotic ? 0.25f : 0f);
        return material;
    }

    /// <summary>Sea depth (r) and pack ice (g) per world-map cell, so the water shader matches the map.</summary>
    ImageTexture SeaTexture()
    {
        var bytes = new byte[Map.Count * 2];
        for (int k = 0; k < Map.Count; k++)
        {
            bytes[k * 2] = Rgb.Byte(Math.Clamp(-Map.Elevation[k] / 120f, 0f, 1f));
            bytes[k * 2 + 1] = Map.Biome[k] == (byte)Biome.FrozenSea ? (byte)255 : (byte)0;
        }
        return ImageTexture.CreateFromImage(Image.CreateFromData(Map.Width, Map.Height, false, Image.Format.Rg8, bytes));
    }

    // ---- HUD ----------------------------------------------------------------------------

    void BuildHud()
    {
        var layer = new CanvasLayer();
        AddChild(layer);
        _hud = new Label { Position = new Vector2(16f, 12f) };
        _hud.AddThemeFontSizeOverride("font_size", 16);
        _hud.AddThemeColorOverride("font_color", new Color("ffe8c2"));
        _hud.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
        _hud.AddThemeConstantOverride("outline_size", 6);
        layer.AddChild(_hud);
    }

    void UpdateHud()
    {
        Vector3 p = Camera.Position;
        Biome biome = _palette.BiomeAt(p.X, p.Z);
        float ground = _palette.Sampler.HeightAt(p.X, p.Z, 2);
        // The early-game promise, made visible: every common vessel material is a short walk away.
        var nearest = new System.Text.StringBuilder("nearest ");
        for (int kind = 0; kind < DepositField.CommonKinds; kind++)
            nearest.Append($"{(DepositKind)kind} {Map.Deposits.Nearest((DepositKind)kind, p.X, p.Z):F0} m   ");
        _hud.Text =
            $"{Map.Spec}\n" +
            $"{p.X / 1000f:F2} km E, {p.Z / 1000f:F2} km S   altitude {p.Y:F0} m ({p.Y - Math.Max(ground, 0f):F0} m up)   over {biome}\n" +
            $"hazard: {Map.Def.Hazard}   resources: {Map.Def.Resources}\n" +
            $"{nearest}\n" +
            $"chunks {Streamer.ChunkCount} ({Streamer.Building} building, last {Streamer.LastBuildMs:F0} ms)   {Engine.GetFramesPerSecond()} fps\n\n" +
            "WASD fly  ·  mouse look  ·  Shift fast  ·  Space / Ctrl up / down  ·  Esc back to orbit";
    }

    static Color C(Rgb c) => new(c.R, c.G, c.B);

    /// <summary>A sky direction from a heading (0 = north) and an elevation, both radians.</summary>
    static Vector3 Direction(float heading, float elevation) =>
        new(Mathf.Sin(heading) * Mathf.Cos(elevation), Mathf.Sin(elevation), -Mathf.Cos(heading) * Mathf.Cos(elevation));
}
