using Godot;

namespace TerrainDigging;

/// <summary>
/// Debug-style overlay in the house colours: status top left, performance top right, a
/// toast for feedback, the controls, and a crosshair that changes colour with what it's on.
/// </summary>
public partial class Hud : CanvasLayer
{
    Label _status, _perf, _toast, _help, _ore, _loading;
    Control _crosshair;
    ColorRect[] _crossBars;
    float _toastTime, _orePop;
    bool _showHelp = true;

    public override void _Ready()
    {
        _status = MakeLabel(new Vector2(18f, 14f), 18);
        _ore = MakeLabel(new Vector2(18f, 14f), 30);
        _ore.AddThemeColorOverride("font_color", Palette.Ore);
        _ore.PivotOffset = new Vector2(0f, 20f);

        // Explicit anchors and offsets: predictable at any window size.
        _perf = MakeLabel(Vector2.Zero, 15);
        Place(_perf, 1f, 0f, 1f, 0f, -760f, 14f, -18f, 120f);
        _perf.HorizontalAlignment = HorizontalAlignment.Right;
        _perf.Modulate = new Color(1f, 1f, 1f, 0.85f);

        _help = MakeLabel(Vector2.Zero, 15);
        Place(_help, 0f, 1f, 0f, 1f, 18f, -90f, 900f, -14f);
        _help.VerticalAlignment = VerticalAlignment.Bottom;
        _help.Modulate = new Color(1f, 1f, 1f, 0.8f);
        _help.Text =
            "WASD move · Shift sprint · Space jump / jetpack · Ctrl crouch\n" +
            "LMB (hold) dig · RMB place 2 m block · C test crater · Q ore detector\n" +
            "F5 save · F9 load · F8 wipe & rebuild all meshes · B chunk borders · H hide help · Esc mouse";

        _toast = MakeLabel(Vector2.Zero, 22);
        Place(_toast, 0f, 0.7f, 1f, 0.7f, 0f, -40f, 0f, 40f);
        _toast.HorizontalAlignment = HorizontalAlignment.Center;
        _toast.VerticalAlignment = VerticalAlignment.Center;

        _loading = MakeLabel(Vector2.Zero, 30);
        Place(_loading, 0f, 0.5f, 1f, 0.5f, 0f, -30f, 0f, 30f);
        _loading.HorizontalAlignment = HorizontalAlignment.Center;
        _loading.Visible = false;

        _crosshair = new Control();
        _crosshair.SetAnchorsPreset(Control.LayoutPreset.Center);
        AddChild(_crosshair);
        _crossBars = new ColorRect[4];
        Vector2[] offsets = { new(-11f, -1f), new(5f, -1f), new(-1f, -11f), new(-1f, 5f) };
        Vector2[] sizes = { new(6f, 2f), new(6f, 2f), new(2f, 6f), new(2f, 6f) };
        for (int i = 0; i < 4; i++)
        {
            _crossBars[i] = new ColorRect { Position = offsets[i], Size = sizes[i], Color = new Color(1f, 1f, 1f, 0.85f) };
            _crosshair.AddChild(_crossBars[i]);
        }
    }

    static void Place(Control c, float anchorLeft, float anchorTop, float anchorRight, float anchorBottom,
        float left, float top, float right, float bottom)
    {
        c.AnchorLeft = anchorLeft;
        c.AnchorTop = anchorTop;
        c.AnchorRight = anchorRight;
        c.AnchorBottom = anchorBottom;
        c.OffsetLeft = left;
        c.OffsetTop = top;
        c.OffsetRight = right;
        c.OffsetBottom = bottom;
    }

    Label MakeLabel(Vector2 position, int size)
    {
        var label = new Label { Position = position };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", Palette.Text);
        label.AddThemeColorOverride("font_outline_color", new Color(0.05f, 0.04f, 0.12f, 0.9f));
        label.AddThemeConstantOverride("outline_size", 6);
        AddChild(label);
        return label;
    }

    public void SetStatus(string ore, string text)
    {
        _ore.Text = ore;
        _status.Text = "\n\n" + text;
    }

    public void SetPerf(string text) => _perf.Text = text;

    public void Loading(string text)
    {
        _loading.Visible = text != null;
        _loading.Text = text ?? "";
    }

    public void ToggleHelp()
    {
        _showHelp = !_showHelp;
        _help.Visible = _showHelp;
    }

    public void Toast(string text, Color color, float seconds = 2.2f)
    {
        _toast.Text = text;
        _toast.AddThemeColorOverride("font_color", color);
        _toastTime = seconds;
        _toast.Modulate = Colors.White;
    }

    public void PopOre() => _orePop = 1f;

    public void SetCrosshair(Color color)
    {
        foreach (ColorRect bar in _crossBars) bar.Color = color;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (_toastTime > 0f)
        {
            _toastTime -= dt;
            _toast.Modulate = new Color(1f, 1f, 1f, Mathf.Clamp(_toastTime / 0.4f, 0f, 1f));
        }
        _orePop = Mathf.MoveToward(_orePop, 0f, dt * 4f);
        _ore.Scale = Vector2.One * (1f + 0.25f * _orePop * _orePop);
    }
}
