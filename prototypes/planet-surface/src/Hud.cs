using System.Collections.Generic;
using Godot;

namespace PlanetSurface;

/// <summary>A debug HUD: crosshair, status, toasts, a loading message and the key help. Not the diegetic UI.</summary>
public partial class Hud : CanvasLayer
{
    public static readonly Color Good = new("9be07a"), Caution = new("ffb84a"), Warning = new("ff5a4a"), Text = new("f2ead8");

    Label _status, _perf, _help, _loading, _weather;
    ColorRect _crosshair;
    VBoxContainer _toasts;
    readonly List<(Label label, double until)> _live = new();
    double _time;

    public bool ShowHelp { get; private set; } = true;

    public override void _Ready()
    {
        _status = MakeLabel(new Vector2(16, 12), 15);
        _perf = MakeLabel(new Vector2(16, 0), 13);
        _perf.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        _perf.GrowVertical = Control.GrowDirection.Begin;
        _perf.Position = new Vector2(16, -16);
        _weather = MakeLabel(new Vector2(0, 12), 15);
        _weather.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _weather.GrowHorizontal = Control.GrowDirection.Begin;
        _weather.HorizontalAlignment = HorizontalAlignment.Right;
        _weather.Position = new Vector2(-16, 12);
        _help = MakeLabel(new Vector2(0, 0), 14);
        _help.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        _help.GrowHorizontal = Control.GrowDirection.Begin;
        _help.GrowVertical = Control.GrowDirection.Begin;
        _help.HorizontalAlignment = HorizontalAlignment.Right;
        _help.Position = new Vector2(-16, -16);
        _help.Text =
            "WASD walk · Shift sprint · Space jump / jetpack · Ctrl crouch · in deep water Space/Ctrl swim up/down\n" +
            "LMB dig, or fell a tree · RMB place block · F free-fly (Shift fast, Space/Ctrl up/down)\n" +
            "1-5 planet: temperate, arid, ice, lava, alien · V variant · N new seed\n" +
            "K next weather · L lightning · O tornado · T time ×20 · Y dawn/noon/dusk/night\n" +
            "F5 save · F9 load · H hide help · Esc mouse";
        _loading = MakeLabel(Vector2.Zero, 26);
        _loading.SetAnchorsPreset(Control.LayoutPreset.Center);
        _loading.HorizontalAlignment = HorizontalAlignment.Center;

        _crosshair = new ColorRect { Size = new Vector2(6, 6), Color = new Color(1, 1, 1, 0.8f), MouseFilter = Control.MouseFilterEnum.Ignore };
        _crosshair.SetAnchorsPreset(Control.LayoutPreset.Center);
        _crosshair.Position = new Vector2(-3, -3);
        AddChild(_crosshair);
        _toasts = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _toasts.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _toasts.Position = new Vector2(-400, -190);
        _toasts.Size = new Vector2(800, 120);
        AddChild(_toasts);
    }

    Label MakeLabel(Vector2 at, int size)
    {
        var label = new Label { Position = at, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", Text);
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        label.AddThemeConstantOverride("outline_size", 6);
        AddChild(label);
        return label;
    }

    public override void _Process(double delta)
    {
        _time += delta;
        for (int i = _live.Count - 1; i >= 0; i--)
        {
            var (label, until) = _live[i];
            double left = until - _time;
            label.Modulate = new Color(1, 1, 1, (float)Mathf.Clamp(left / 0.4, 0, 1));
            if (left > 0) continue;
            label.QueueFree();
            _live.RemoveAt(i);
        }
    }

    public void Toast(string text, Color color, float seconds = 2.2f)
    {
        var label = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", 17);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        label.AddThemeConstantOverride("outline_size", 6);
        _toasts.AddChild(label);
        _live.Add((label, _time + seconds));
        while (_live.Count > 4)
        {
            _live[0].label.QueueFree();
            _live.RemoveAt(0);
        }
    }

    public void SetCrosshair(Color c) => _crosshair.Color = c;
    public void SetStatus(string text) => _status.Text = text;
    public void SetPerf(string text) => _perf.Text = text;
    public void SetWeather(string text) => _weather.Text = text;
    public void Loading(string text)
    {
        _loading.Text = text ?? "";
        _loading.Visible = text != null;
    }

    public void ToggleHelp()
    {
        ShowHelp = !ShowHelp;
        _help.Visible = ShowHelp;
    }

    /// <summary>Screenshots: nothing but the world.</summary>
    public void HideAll()
    {
        foreach (Node n in GetChildren())
            if (n is CanvasItem c) c.Visible = false;
    }
}
