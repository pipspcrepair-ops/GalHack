using System;
using System.Collections.Generic;
using Godot;
using Slice.Surface;
using Slice.Ui;

namespace Slice.Game;

/// <summary>
/// The player's settings at work: read at start, applied to the window, the view, the sun and the
/// streaming ground, and to how keys and the mouse reach <see cref="Controls"/>. Changes apply at
/// once (the escape menu changes them while the world is stopped).
/// </summary>
public partial class Game
{
    /// <summary>The settings file: user://settings.cfg in play. Tests and screenshots leave it null and play on the defaults (nothing read, nothing written) unless they set one.</summary>
    public string SettingsPath;

    public GameSettings Settings { get; private set; } = new();

    /// <summary>The escape menu: Resume, Save, Load, Options, Quit.</summary>
    public EscapeMenu Escape { get; private set; }

    /// <summary>Base look speed, radians a pixel, at a sensitivity of 1.</summary>
    public const float BaseLook = 0.0025f;

    /// <summary>Base flight stick push a pixel, at a flight sensitivity of 1.</summary>
    public const float BaseStick = 0.012f;

    /// <summary>Radians of turn a pixel of mouse movement gives, on foot.</summary>
    public float LookScale => BaseLook * Settings.Sensitivity;

    /// <summary>A tick's mouse movement as a turn of the view: x yaw, y pitch, radians (inverted if asked).</summary>
    Vector2 LookTurn(Controls c) => new(c.Look.X * LookScale, c.Look.Y * LookScale * (Settings.InvertY ? -1f : 1f));

    /// <summary>A tick's mouse movement as a push on the flight stick.</summary>
    Vector2 StickPush(Controls c) => new Vector2(c.Look.X, c.Look.Y * (Settings.InvertFlightY ? -1f : 1f)) * BaseStick * Settings.FlightSensitivity;

    Brightness _brightness;
    int _appliedVersion = -1;
    SurfaceZone _appliedZone;

    void LoadSettings()
    {
        SettingsPath ??= Interactive ? GameSettings.DefaultPath : null;
        Settings = GameSettings.Load(SettingsPath);
        _brightness = new Brightness { Name = "Brightness" };
        AddChild(_brightness);
        ApplyDisplay();
    }

    /// <summary>Writes the settings file (nothing for a game with no settings path).</summary>
    public void SaveSettings() => Settings.Save(SettingsPath);

    /// <summary>
    /// Everything at once, after a change: the window and the engine's frame pacing, the 3D view's
    /// resolution, the picture's gamma, and (next frame) the view, the sun's shadows and the ground.
    /// </summary>
    public void ApplySettings()
    {
        ApplyDisplay();
        ApplyScene();
    }

    /// <summary>The window as last set from the settings: it is set again only when they change, so a window dragged to another size stays so.</summary>
    (WindowKind, Vector2I, bool)? _windowSet;

    /// <summary>The window (in play only, never a test's), vsync, the frame cap, render scale and gamma.</summary>
    void ApplyDisplay()
    {
        GameSettings s = Settings;
        if (Interactive && DisplayServer.GetName() != "headless" && _windowSet != (s.Window, s.Resolution, s.Vsync))
        {
            _windowSet = (s.Window, s.Resolution, s.Vsync);
            var mode = s.Window switch
            {
                WindowKind.Borderless => DisplayServer.WindowMode.Fullscreen, // Godot's borderless full screen
                WindowKind.Fullscreen => DisplayServer.WindowMode.ExclusiveFullscreen,
                _ => DisplayServer.WindowMode.Windowed,
            };
            if (DisplayServer.WindowGetMode() != mode) DisplayServer.WindowSetMode(mode);
            if (mode == DisplayServer.WindowMode.Windowed && DisplayServer.WindowGetSize() != s.Resolution)
            {
                Vector2I screen = DisplayServer.ScreenGetSize();
                Vector2I size = new(Mathf.Min(s.Resolution.X, screen.X), Mathf.Min(s.Resolution.Y, screen.Y));
                DisplayServer.WindowSetSize(size);
                DisplayServer.WindowSetPosition(DisplayServer.ScreenGetPosition() + (screen - size) / 2);
            }
            DisplayServer.WindowSetVsyncMode(s.Vsync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
        }
        Engine.MaxFps = s.FpsCap;
        GetViewport().Scaling3DScale = s.RenderScale;
        _brightness?.SetGamma(s.Gamma);
    }

    /// <summary>The settings the world's own nodes carry: the field of view (every frame, under a Husk's pull), the sun's shadows and how far the diggable ground streams.</summary>
    void ApplyScene()
    {
        if (Zone == null) return;
        _appliedVersion = Settings.Version;
        _appliedZone = Zone;
        if (Camera != null) Camera.Fov = Settings.Fov;
        ShadowSetup s = Shadows(Settings.Shadows);
        RenderingServer.DirectionalShadowAtlasSetSize(s.SunAtlas, true);
        RenderingServer.DirectionalSoftShadowFilterSetQuality(s.Filter);
        RenderingServer.PositionalSoftShadowFilterSetQuality(s.Filter);
        GetViewport().PositionalShadowAtlasSize = s.LampAtlas;
        float reach = Settings.ShadowDistance > GameSettings.DefaultShadow ? Settings.ShadowDistance : s.Reach;
        foreach (DirectionalLight3D light in Suns())
        {
            // The level's own reach, or the shadow distance setting's if above AUTO: the splits grow by one ratio from the
            // first (so a texel grows with distance, a plant's shadow is as sharp at 300 m as at 80) and the last 15% fades as the far land's own shade comes in.
            light.DirectionalShadowMaxDistance = reach;
            light.ShadowNormalBias = NormalBias;
            light.DirectionalShadowBlendSplits = true; // one split hands over to the next across a band, not at a line
            if (reach >= GameSettings.DefaultShadow)
            {
                float ratio = MathF.Pow(reach / FirstSplit, 1f / 3f);
                light.DirectionalShadowSplit1 = FirstSplit / reach;
                light.DirectionalShadowSplit2 = FirstSplit * ratio / reach;
                light.DirectionalShadowSplit3 = FirstSplit * ratio * ratio / reach;
                light.DirectionalShadowFadeStart = PlanetSurface.Terrain.FarShade.JoinFull;
            }
            light.DirectionalShadowMode = s.Splits;
        }
        if (Surface?.Streamer != null) Surface.Streamer.VoxelRadius = Settings.GroundDetail;
        PlanetSurface.Terrain.DetailReach.Set(Settings.DetailDistance, Settings.TreeDistance);
        Slice.Surface.SpawnReach.Set(Settings.SpawnDistance);
        if (Surface?.Streamer != null) Surface.Streamer.ShadowReach = reach;
    }

    /// <summary>The sun's shadow normal bias (the engine's is 2, in texels: a lift off the ground the size of a texel or two, which at a low sun hid small shadows and set the rest off their bases) and the first split's distance, m.</summary>
    const float NormalBias = 0.6f, FirstSplit = 30f;

    /// <summary>A shadow level as the engine's numbers.</summary>
    /// <param name="Reach">How far from the eye the sun's shadows are drawn, m.</param>
    /// <param name="SunAtlas">The sun's shadow map, pixels a side; <paramref name="LampAtlas"/> the torch's and lamps'.</param>
    public readonly record struct ShadowSetup(float Reach, DirectionalLight3D.ShadowMode Splits, int SunAtlas, int LampAtlas, RenderingServer.ShadowQuality Filter);

    /// <summary>
    /// Ultra reaches the most on its bigger map; High reaches 320 m, as the game always did, on the engine's default maps and filter
    /// (read from the project, so it stays so), and the far land's own shade takes over past it. (High at 1500 m spread one
    /// map over five times the ground: past 400 m a texel was 2 m, so every far tree's shadow crawled at a low sun, sharpened
    /// in steps as you came in, and changed with every far chunk redrawn in view.)
    /// </summary>
    public static ShadowSetup Shadows(ShadowLevel level) => level switch
    {
        ShadowLevel.Low => new(120f, DirectionalLight3D.ShadowMode.Parallel2Splits, 2048, 1024, RenderingServer.ShadowQuality.Hard),
        ShadowLevel.Medium => new(220f, DirectionalLight3D.ShadowMode.Parallel4Splits, 2048, 2048, RenderingServer.ShadowQuality.SoftVeryLow),
        ShadowLevel.Ultra => new(GameSettings.MostShadow, DirectionalLight3D.ShadowMode.Parallel4Splits, 8192, 4096, RenderingServer.ShadowQuality.SoftMedium),
        _ => new(GameSettings.DefaultShadow, DirectionalLight3D.ShadowMode.Parallel4Splits,
            (int)ProjectSettings.GetSetting("rendering/lights_and_shadows/directional_shadow/size", 4096),
            (int)ProjectSettings.GetSetting("rendering/lights_and_shadows/positional_shadow/atlas_size", 4096),
            (RenderingServer.ShadowQuality)(int)ProjectSettings.GetSetting("rendering/lights_and_shadows/directional_shadow/soft_shadow_filter_quality", 2)),
    };

    /// <summary>Every sun and moon light that casts the world's shadows: the surface's sky, and the orbit's, if there is one.</summary>
    IEnumerable<DirectionalLight3D> Suns()
    {
        foreach (SurfaceZone z in new[] { Surface, Orbit })
        {
            if (z?.Sky == null) continue;
            if (z.Sky.Sun != null) yield return z.Sky.Sun;
            if (z.Sky.NightLight != null) yield return z.Sky.NightLight;
        }
        if (Orbit?.Space?.Sun != null) yield return Orbit.Space.Sun;
    }

    /// <summary>Re-applied after a change, and when you arrive in another zone (its sky and lights are its own).</summary>
    void KeepSettingsApplied()
    {
        if (_appliedVersion != Settings.Version || _appliedZone != Zone) ApplyScene();
    }

    /// <summary>
    /// A key pressed on the keyboard, as the game hears it: the key itself (menus' own shortcuts
    /// read it), and the actions bound to it, by their default keys (everything else reads those).
    /// </summary>
    public void Press(Key key)
    {
        Controls.Keys.Add(key);
        foreach (Key action in Settings.Keys.ActionsOn(key)) Controls.Pressed.Add(action);
    }
}
