using System;
using Godot;

namespace Slice.Game;

/// <summary>How the game's window sits on the screen.</summary>
public enum WindowKind { Windowed, Borderless, Fullscreen }

/// <summary>The sun's shadows: how far they reach and how sharp they are. There is no "off": the sun's shadow is what keeps it out of rooms and under roofs.</summary>
public enum ShadowLevel { Low, Medium, High, Ultra }

/// <summary>
/// The player's settings: graphics, mouse and keys (the four volumes keep their own file,
/// <see cref="AudioSettings"/>). Kept in a small config file in user://, read at start; a missing
/// or broken file, or a value out of range, just means the default. Every field is plain data:
/// the game applies them (<see cref="Game.ApplySettings"/>) and the escape menu changes them.
/// </summary>
public sealed class GameSettings
{
    public const string DefaultPath = "user://settings.cfg";

    // ---- Graphics ----
    public WindowKind Window = WindowKind.Windowed;
    /// <summary>The window's size when windowed (full screen uses the screen's own).</summary>
    public Vector2I Resolution = new(1600, 900);
    public bool Vsync = true;
    /// <summary>Frames a second at most; 0 for no cap.</summary>
    public int FpsCap;
    /// <summary>The 3D view's resolution as a share of the window's, 0.5 to 1.</summary>
    public float RenderScale = 1f;
    /// <summary>Vertical field of view, degrees.</summary>
    public float Fov = 75f;
    public ShadowLevel Shadows = ShadowLevel.High;
    /// <summary>How far the diggable ground streams round you, in 16 m columns each way; past it, the far land.</summary>
    public int GroundDetail = 5;
    /// <summary>The picture's gamma: above 1 lifts the shadows, below darkens them.</summary>
    public float Gamma = 1f;

    // ---- Mouse ----
    /// <summary>On foot (and on a bike): a multiple of the base look speed.</summary>
    public float Sensitivity = 1f;
    public bool InvertY;
    /// <summary>Flying: how far a movement pushes the self-centring stick, a multiple of the base.</summary>
    public float FlightSensitivity = 1f;
    public bool InvertFlightY;

    // ---- Keys ----
    public readonly KeyBindings Keys = new();

    public const float MinScale = 0.5f, MinFov = 55f, MaxFov = 110f, MinGamma = 0.6f, MaxGamma = 1.6f, MinSense = 0.2f, MaxSense = 3f;
    public const int MinGround = 3, MaxGround = 7;
    public static readonly int[] FpsCaps = { 30, 60, 90, 120, 144, 240, 0 };
    public static readonly Vector2I[] Resolutions = { new(1280, 720), new(1366, 768), new(1600, 900), new(1920, 1080), new(2560, 1440), new(3840, 2160) };

    int _version;
    /// <summary>Goes up with every change, keys included: the game re-applies and the file is written again.</summary>
    public int Version => _version + Keys.Version;

    /// <summary>Call after changing a field.</summary>
    public void Changed() => _version++;

    /// <summary>Back to the defaults, one page at a time.</summary>
    public void ResetGraphics()
    {
        var d = new GameSettings();
        (Window, Resolution, Vsync, FpsCap, RenderScale, Fov, Shadows, GroundDetail, Gamma) = (d.Window, d.Resolution, d.Vsync, d.FpsCap, d.RenderScale, d.Fov, d.Shadows, d.GroundDetail, d.Gamma);
        Changed();
    }

    public void ResetMouse()
    {
        var d = new GameSettings();
        (Sensitivity, InvertY, FlightSensitivity, InvertFlightY) = (d.Sensitivity, d.InvertY, d.FlightSensitivity, d.InvertFlightY);
        Changed();
    }

    /// <summary>Reads the file; anything missing, unreadable or out of range keeps its default.</summary>
    public static GameSettings Load(string path)
    {
        var s = new GameSettings();
        var file = new ConfigFile();
        if (path == null || file.Load(path) != Error.Ok) return s;
        s.Window = Enum.TryParse(Str(file, "graphics", "window"), true, out WindowKind w) ? w : s.Window;
        if (file.GetValue("graphics", "resolution", s.Resolution).Obj is Vector2I r && r.X >= 640 && r.Y >= 360) s.Resolution = r;
        s.Vsync = Bool(file, "graphics", "vsync", s.Vsync);
        int cap = Int(file, "graphics", "fps_cap", s.FpsCap);
        if (Array.IndexOf(FpsCaps, cap) >= 0) s.FpsCap = cap;
        s.RenderScale = Math.Clamp(Float(file, "graphics", "render_scale", s.RenderScale), MinScale, 1f);
        s.Fov = Math.Clamp(Float(file, "graphics", "fov", s.Fov), MinFov, MaxFov);
        s.Shadows = Enum.TryParse(Str(file, "graphics", "shadows"), true, out ShadowLevel sh) ? sh : s.Shadows;
        s.GroundDetail = Math.Clamp(Int(file, "graphics", "ground_detail", s.GroundDetail), MinGround, MaxGround);
        s.Gamma = Math.Clamp(Float(file, "graphics", "gamma", s.Gamma), MinGamma, MaxGamma);
        s.Sensitivity = Math.Clamp(Float(file, "mouse", "sensitivity", s.Sensitivity), MinSense, MaxSense);
        s.InvertY = Bool(file, "mouse", "invert_y", s.InvertY);
        s.FlightSensitivity = Math.Clamp(Float(file, "mouse", "flight_sensitivity", s.FlightSensitivity), MinSense, MaxSense);
        s.InvertFlightY = Bool(file, "mouse", "invert_flight_y", s.InvertFlightY);
        if (file.HasSection("keys"))
            foreach (string id in file.GetSectionKeys("keys")) s.Keys.Restore(id, Str(file, "keys", id));
        return s;
    }

    public void Save(string path)
    {
        if (path == null) return;
        var file = new ConfigFile();
        file.SetValue("graphics", "window", Window.ToString().ToLowerInvariant());
        file.SetValue("graphics", "resolution", Resolution);
        file.SetValue("graphics", "vsync", Vsync);
        file.SetValue("graphics", "fps_cap", FpsCap);
        file.SetValue("graphics", "render_scale", RenderScale);
        file.SetValue("graphics", "fov", Fov);
        file.SetValue("graphics", "shadows", Shadows.ToString().ToLowerInvariant());
        file.SetValue("graphics", "ground_detail", GroundDetail);
        file.SetValue("graphics", "gamma", Gamma);
        file.SetValue("mouse", "sensitivity", Sensitivity);
        file.SetValue("mouse", "invert_y", InvertY);
        file.SetValue("mouse", "flight_sensitivity", FlightSensitivity);
        file.SetValue("mouse", "invert_flight_y", InvertFlightY);
        foreach (var (id, key) in Keys.Saved) file.SetValue("keys", id, key);
        Error err = file.Save(path);
        if (err != Error.Ok) GD.PushWarning($"Settings not saved to {path}: {err}");
    }

    static string Str(ConfigFile f, string section, string key) => f.GetValue(section, key, "").Obj as string ?? "";
    static bool Bool(ConfigFile f, string section, string key, bool fallback) => f.GetValue(section, key, fallback).Obj is bool b ? b : fallback;
    static int Int(ConfigFile f, string section, string key, int fallback) => f.GetValue(section, key, fallback).Obj switch { long l => (int)l, int i => i, double d => (int)d, _ => fallback };
    static float Float(ConfigFile f, string section, string key, float fallback)
    {
        float v = f.GetValue(section, key, fallback).Obj switch { double d => (float)d, float x => x, long l => l, int i => i, _ => fallback };
        return float.IsFinite(v) ? v : fallback;
    }
}
