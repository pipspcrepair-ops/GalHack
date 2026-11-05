using Godot;

namespace Invasions.World;

/// <summary>
/// The in-game options (Esc): for now one setting, invasions on or off for this world. The game keeps
/// running behind it (co-op can't pause), the mouse is freed while it is open, and Esc or Resume
/// closes it and takes the mouse back.
/// </summary>
public partial class OptionsMenu : CanvasLayer
{
    public InvasionController Controller;

    public bool IsOpen => _root.Visible;
    /// <summary>The "Invasions" switch, for tests.</summary>
    public CheckButton InvasionsSwitch { get; private set; }

    Control _root;
    Label _note;

    public override void _Ready()
    {
        Layer = 2;
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Stop, Visible = false };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);
        // A dimmed backdrop that also keeps clicks off the game (a click would take the mouse back).
        var backdrop = new ColorRect { Color = new Color(0.02f, 0.03f, 0.04f, 0.55f), MouseFilter = Control.MouseFilterEnum.Stop };
        backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(backdrop);

        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.07f, 0.09f, 0.94f),
            BorderColor = Palette.Amber,
            BorderWidthLeft = 4,
            ContentMarginLeft = 26, ContentMarginRight = 26, ContentMarginTop = 18, ContentMarginBottom = 18,
        });
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        panel.GrowHorizontal = Control.GrowDirection.Both;
        panel.GrowVertical = Control.GrowDirection.Both;
        _root.AddChild(panel);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 14);
        panel.AddChild(column);
        column.AddChild(Text("OPTIONS", 30, Palette.Amber));

        InvasionsSwitch = new CheckButton { Text = "Invasions", FocusMode = Control.FocusModeEnum.None };
        InvasionsSwitch.AddThemeFontOverride("font", Palette.Font);
        InvasionsSwitch.AddThemeFontSizeOverride("font_size", 22);
        InvasionsSwitch.Toggled += on =>
        {
            Controller.SetInvasionsEnabled(on);
            Refresh();
        };
        column.AddChild(InvasionsSwitch);

        _note = Text("", 16, new Color(1f, 0.91f, 0.76f, 0.85f));
        _note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _note.CustomMinimumSize = new Vector2(460, 0);
        column.AddChild(_note);

        var resume = new Button { Text = "Resume (Esc)", FocusMode = Control.FocusModeEnum.None };
        resume.AddThemeFontOverride("font", Palette.Font);
        resume.AddThemeFontSizeOverride("font_size", 20);
        resume.Pressed += Close;
        column.AddChild(resume);
        Refresh();
    }

    static Label Text(string text, int size, Color color)
    {
        var label = new Label { Text = text };
        label.AddThemeFontOverride("font", Palette.Font);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    /// <summary>Shows the switch as the director has it.</summary>
    void Refresh()
    {
        bool on = Controller.Director.InvasionsEnabled;
        InvasionsSwitch.SetPressedNoSignal(on);
        InvasionsSwitch.Text = on ? "Invasions: On" : "Invasions: Off";
        _note.Text = on
            ? "Hostile factions attack this base while someone is home, with warnings at 10, 5 and 1 minutes. Saved with this world; in co-op it is the host's setting."
            : "No invasions. Any under way has been called off or is pulling out; nothing is undone. Turn them back on and the next one comes on the usual schedule, with full warning.";
    }

    public void Open()
    {
        Refresh();
        _root.Visible = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public void Close()
    {
        _root.Visible = false;
        if (DisplayServer.GetName() != "headless") Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!e.IsActionPressed(Controls.Options)) return;
        if (IsOpen) Close();
        else Open();
        GetViewport().SetInputAsHandled();
    }
}
