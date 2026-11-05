using System;
using DiegeticUi.Theme;
using Godot;
using Slice.Game;
using GalHack.Items;

namespace Slice.Ui;

/// <summary>
/// The start screen of a new game: Survival or Creative, the planet type (temperate is the easy
/// one; or random) and a seed. Leave the seed empty for a fresh world, or type one to replay it
/// (the STATS tab shows every world's seed). Shown only when the command line pins nothing.
/// </summary>
public partial class StartScreen : CanvasLayer
{
    /// <summary>Chosen: the world and the mode.</summary>
    public event Action<WorldChoice, GameMode> Chosen;

    GameMode _mode = GameMode.Survival;
    string _type = "temperate";
    LineEdit _seed;
    Label _summary;
    readonly System.Collections.Generic.List<(Button button, Func<bool> lit)> _toggles = new();

    public override void _Ready()
    {
        Layer = 10;
        var palette = UiTheme.Colors;
        var back = new ColorRect { Color = palette.Glass };
        back.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(back);
        var centre = new CenterContainer { Theme = UiTheme.GodotTheme };
        centre.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(centre);
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(620, 0) };
        column.AddThemeConstantOverride("separation", 14);
        centre.AddChild(column);

        column.AddChild(Heading("GALHACK", 46, palette.Accent));
        column.AddChild(Heading("You wake in a buried cryopod. Everyone else is gone. Climb out.", 18, palette.Text));

        column.AddChild(Heading("MODE", 16, palette.Dim));
        var modes = new HBoxContainer();
        modes.AddChild(Toggle("Survival", () => _mode == GameMode.Survival, () => _mode = GameMode.Survival));
        modes.AddChild(Toggle("Creative", () => _mode == GameMode.Creative, () => _mode = GameMode.Creative));
        column.AddChild(modes);

        column.AddChild(Heading("PLANET", 16, palette.Dim));
        var types = new HFlowContainer();
        foreach (var t in WorldChoice.Types)
        {
            string key = t.Key;
            types.AddChild(Toggle(t.Name, () => _type == key, () => _type = key));
        }
        types.AddChild(Toggle("Random", () => _type == "random", () => _type = "random"));
        column.AddChild(types);

        column.AddChild(Heading("SEED", 16, palette.Dim));
        _seed = new LineEdit { PlaceholderText = "empty: a fresh world   ·   type a seed to replay one", CustomMinimumSize = new Vector2(0, 44) };
        _seed.TextChanged += _ => Refresh();
        column.AddChild(_seed);

        _summary = Heading("", 15, palette.Dim);
        column.AddChild(_summary);
        var start = new Button { Text = "WAKE UP", CustomMinimumSize = new Vector2(0, 56) };
        start.Pressed += Start;
        column.AddChild(start);
        _seed.TextSubmitted += _ => Start();
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Refresh();
    }

    void Start()
    {
        ulong? seed = string.IsNullOrWhiteSpace(_seed.Text) ? null : WorldChoice.ParseSeed(_seed.Text);
        WorldChoice world = WorldChoice.Fresh(_type, seed);
        Chosen?.Invoke(world, _mode);
        QueueFree();
    }

    Label Heading(string text, int size, Color colour)
    {
        var l = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", colour);
        return l;
    }

    Button Toggle(string text, Func<bool> lit, Action pick)
    {
        var b = new Button { Text = text, ToggleMode = true, CustomMinimumSize = new Vector2(150, 44) };
        b.Pressed += () =>
        {
            pick();
            Refresh();
        };
        _toggles.Add((b, lit));
        return b;
    }

    void Refresh()
    {
        foreach (var (button, lit) in _toggles) button.SetPressedNoSignal(lit());
        if (_summary == null) return;
        string seed = string.IsNullOrWhiteSpace(_seed?.Text) ? "a fresh seed" : $"seed {WorldChoice.ParseSeed(_seed.Text)}";
        _summary.Text = $"{(_mode == GameMode.Creative ? "Creative" : "Survival")} · {(WorldChoice.Lookup(_type)?.Name ?? "a random planet type")} · {seed}";
    }
}
