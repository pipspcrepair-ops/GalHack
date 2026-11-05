using Godot;

namespace CapitalBase;

/// <summary>
/// The debug overlay: the core console of the structure you have selected, the vessel's
/// auto-transmit link, the console's last messages and the keys, plus an optional caption (the
/// screenshots say what they show). Text only; the diegetic console on the core block is the
/// diegetic-ui prototype's job.
/// </summary>
public partial class Hud : CanvasLayer
{
    Label _console, _vessel, _log, _help, _caption;
    PanelContainer _consolePanel, _vesselPanel, _captionPanel;
    public bool ShowHelp = true;

    /// <summary>False to show only the caption (a screenshot of the world, not the console).</summary>
    public bool ShowPanels = true;

    /// <summary>A line or two across the bottom saying what is on screen. Empty: none.</summary>
    public string Caption = "";

    public static readonly Color Text = new("f1ead8");

    public override void _Ready()
    {
        var font = new SystemFont { FontNames = new[] { "Bahnschrift", "Segoe UI", "Arial" } };
        var theme = new Theme { DefaultFont = font, DefaultFontSize = 15 };
        var root = new Control { Theme = theme, MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        _consolePanel = Panel(root, new Color(0.06f, 0.07f, 0.09f, 0.78f));
        _consolePanel.Position = new Vector2(16, 16);
        _console = Body(_consolePanel);
        _vesselPanel = Panel(root, new Color(0.04f, 0.12f, 0.14f, 0.78f));
        _vessel = Body(_vesselPanel);

        // Pinned to the bottom-left corner, growing upward as lines arrive.
        _log = new Label
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AnchorTop = 1f, AnchorBottom = 1f, OffsetLeft = 16f, OffsetBottom = -16f, OffsetTop = -16f,
            GrowVertical = Control.GrowDirection.Begin,
        };
        _log.AddThemeColorOverride("font_color", Text);
        _log.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
        root.AddChild(_log);

        _help = new Label { MouseFilter = Control.MouseFilterEnum.Ignore, HorizontalAlignment = HorizontalAlignment.Right };
        _help.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _help.GrowHorizontal = Control.GrowDirection.Begin;
        _help.Position = new Vector2(-16, 16);
        _help.AddThemeColorOverride("font_color", Text);
        _help.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.9f));
        _help.AddThemeFontSizeOverride("font_size", 13);
        root.AddChild(_help);

        // The caption: centred along the bottom, growing upward.
        _captionPanel = Panel(root, new Color(0.06f, 0.07f, 0.09f, 0.82f));
        _captionPanel.AnchorLeft = _captionPanel.AnchorRight = 0.5f;
        _captionPanel.AnchorTop = _captionPanel.AnchorBottom = 1f;
        _captionPanel.OffsetBottom = _captionPanel.OffsetTop = -24f;
        _captionPanel.GrowHorizontal = Control.GrowDirection.Both;
        _captionPanel.GrowVertical = Control.GrowDirection.Begin;
        _caption = Body(_captionPanel);
        _caption.HorizontalAlignment = HorizontalAlignment.Center;
        _caption.AddThemeFontSizeOverride("font_size", 18);
    }

    static PanelContainer Panel(Control root, Color colour)
    {
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        var style = new StyleBoxFlat
        {
            BgColor = colour,
            BorderColor = new Color("f2a516"),
            BorderWidthLeft = 4,
            ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 10, ContentMarginBottom = 10,
        };
        panel.AddThemeStyleboxOverride("panel", style);
        root.AddChild(panel);
        return panel;
    }

    static Label Body(PanelContainer panel)
    {
        var label = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeColorOverride("font_color", Text);
        panel.AddChild(label);
        return label;
    }

    public void Set(string console, string vessel, string log, string help)
    {
        _console.Text = console;
        _consolePanel.Visible = ShowPanels && console.Length > 0;
        _vessel.Text = vessel;
        _vesselPanel.Visible = ShowPanels && vessel.Length > 0;
        _vesselPanel.Position = new Vector2(16, 16 + _consolePanel.Size.Y + 12);
        _log.Text = log;
        _log.Visible = ShowPanels && Caption.Length == 0;
        _help.Text = ShowHelp ? help : "F1  help";
        _help.Visible = ShowPanels;
        _caption.Text = Caption;
        _captionPanel.Visible = Caption.Length > 0;
    }
}
