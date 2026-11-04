using System;
using System.Collections.Generic;
using Godot;

namespace Saves;

/// <summary>
/// What is on the screen: the active zone's picture, the tunnel over it, the debug text, a big
/// centre message, and the host console (backtick or F7) where backups are listed and restored.
/// </summary>
public partial class Hud : CanvasLayer
{
    public TextureRect Screen { get; private set; }
    public Tunnel Tunnel { get; private set; }
    public Label Status { get; private set; }
    public Label Center { get; private set; }
    public Label Keys { get; private set; }

    public bool ConsoleOpen => _console.Visible;

    /// <summary>A line typed into the console.</summary>
    public event Action<string> Command;

    Control _console;
    Label _log;
    LineEdit _input;
    readonly List<string> _lines = new();

    public static readonly Color Ink = new("ffe8c2");
    public static readonly Color Good = new("9be564");
    public static readonly Color Bad = new("ff6b5b");

    public override void _Ready()
    {
        Screen = new TextureRect
        {
            Name = "Screen",
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        Screen.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(Screen);

        Tunnel = new Tunnel { Name = "Tunnel" };
        AddChild(Tunnel);

        Status = MakeLabel(17);
        Status.Position = new Vector2(16f, 12f);
        AddChild(Status);

        Keys = MakeLabel(15);
        Keys.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        Keys.GrowVertical = Control.GrowDirection.Begin;
        Keys.Position = new Vector2(16f, -12f);
        Keys.OffsetLeft = 16f;
        Keys.OffsetBottom = -12f;
        AddChild(Keys);

        // Below the crosshair, wrapped to the middle of the screen.
        Center = MakeLabel(24);
        Center.AnchorLeft = 0.14f;
        Center.AnchorRight = 0.86f;
        Center.AnchorTop = 0.58f;
        Center.AnchorBottom = 0.58f;
        Center.HorizontalAlignment = HorizontalAlignment.Center;
        Center.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        Center.GrowVertical = Control.GrowDirection.End;
        AddChild(Center);

        var crosshair = new ColorRect
        {
            Color = new Color(1f, 1f, 1f, 0.8f),
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = -2f, OffsetRight = 2f, OffsetTop = -2f, OffsetBottom = 2f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(crosshair);

        BuildConsole();
    }

    static Label MakeLabel(int size)
    {
        var label = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", Ink);
        label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
        label.AddThemeConstantOverride("outline_size", 6);
        return label;
    }

    void BuildConsole()
    {
        _console = new PanelContainer { Name = "Console", Visible = false };
        _console.SetAnchorsPreset(Control.LayoutPreset.CenterRight);
        _console.AnchorLeft = 0.52f;
        _console.AnchorRight = 0.985f;
        _console.AnchorTop = 0.03f;
        _console.AnchorBottom = 0.72f;
        _console.OffsetLeft = _console.OffsetRight = _console.OffsetTop = _console.OffsetBottom = 0;
        _console.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.07f, 0.05f, 0.08f, 0.88f),
            BorderColor = new Color("e85d04"),
            BorderWidthLeft = 3, BorderWidthTop = 3, BorderWidthRight = 3, BorderWidthBottom = 3,
            ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 10, ContentMarginBottom = 10,
        });
        AddChild(_console);

        var column = new VBoxContainer();
        _console.AddChild(column);
        var title = MakeLabel(18);
        title.Text = "HOST CONSOLE   (help for commands, Esc or ` to close)";
        title.AddThemeColorOverride("font_color", new Color("f4a100"));
        column.AddChild(title);
        _log = MakeLabel(15);
        _log.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _log.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _log.VerticalAlignment = VerticalAlignment.Bottom;
        _log.ClipText = true;
        column.AddChild(_log);
        _input = new LineEdit { PlaceholderText = "restore 1" };
        _input.AddThemeFontSizeOverride("font_size", 17);
        _input.TextSubmitted += text =>
        {
            _input.Clear();
            if (string.IsNullOrWhiteSpace(text)) return;
            Print("> " + text);
            Command?.Invoke(text.Trim());
        };
        column.AddChild(_input);
    }

    public void OpenConsole()
    {
        _console.Visible = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _input.GrabFocus();
    }

    public void CloseConsole()
    {
        _console.Visible = false;
        _input.ReleaseFocus();
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public void Print(string text)
    {
        foreach (string line in text.Split('\n')) _lines.Add(line);
        while (_lines.Count > 40) _lines.RemoveAt(0);
        _log.Text = string.Join("\n", _lines);
    }

    /// <summary>Shows the centre message in a colour: ink, good news or bad news.</summary>
    public void Say(string text, Color color)
    {
        Center.Text = text;
        Center.AddThemeColorOverride("font_color", color);
    }
}
