using System.Collections.Generic;
using Godot;
using Manufacturing.Screens;

namespace Manufacturing.Game;

/// <summary>
/// The prototype's thin overlay: a crosshair, one prompt line for whatever you look at, the game
/// mode and the selected block's cost, and short toasts. Everything else lives on the machines'
/// own screens.
/// </summary>
public partial class Hud : CanvasLayer
{
    readonly List<(string text, double until, Color colour)> _toasts = new();
    Control _canvas;
    double _now;

    public string Prompt { get; set; } = "";
    public string Mode { get; set; } = "";
    public string Tool { get; set; } = "";
    public Color ToolColour { get; set; } = Kit.Cream;
    public bool ShowHelp { get; set; } = true;

    public override void _Ready()
    {
        _canvas = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _canvas.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _canvas.Draw += Paint;
        AddChild(_canvas);
    }

    public void Toast(string text, bool bad = false)
    {
        _toasts.Add((text, _now + 3.5, bad ? Kit.Danger : Kit.Accent2));
        if (_toasts.Count > 4) _toasts.RemoveAt(0);
    }

    public override void _Process(double delta)
    {
        _now += delta;
        _toasts.RemoveAll(t => t.until < _now);
        _canvas.QueueRedraw();
    }

    void Paint()
    {
        Vector2 size = _canvas.GetViewportRect().Size, c = size / 2;
        _canvas.DrawRect(new Rect2(c.X - 1, c.Y - 9, 2, 18), new Color(1, 1, 1, 0.8f));
        _canvas.DrawRect(new Rect2(c.X - 9, c.Y - 1, 18, 2), new Color(1, 1, 1, 0.8f));

        var glass = new Color(Kit.Glass, 0.78f);
        if (Prompt.Length > 0)
        {
            float w = Kit.Width(Kit.Label, 20, Prompt) + 40;
            var r = new Rect2(c.X - w / 2, size.Y - 112, w, 38);
            _canvas.DrawRect(r, glass);
            _canvas.TextIn(Kit.Label, 20, r, Prompt, Kit.Cream, HorizontalAlignment.Center);
        }
        if (Tool.Length > 0)
        {
            float w = Kit.Width(Kit.Label, 20, Tool) + 40;
            var r = new Rect2(c.X - w / 2, size.Y - 66, w, 38);
            _canvas.DrawRect(r, glass);
            _canvas.TextIn(Kit.Label, 20, r, Tool, ToolColour, HorizontalAlignment.Center);
        }
        var mode = new Rect2(20, 20, Kit.Width(Kit.Display, 18, Mode) + 56, 40);
        _canvas.DrawRect(mode, glass);
        _canvas.DrawRect(new Rect2(mode.Position, new Vector2(8, mode.Size.Y)), Mode.StartsWith("CREATIVE") ? Kit.Good : Kit.Accent);
        _canvas.TextIn(Kit.Display, 18, mode, Mode, Kit.Cream, padX: 20);

        float y = 76;
        foreach (var (text, _, colour) in _toasts)
        {
            var r = new Rect2(20, y, Kit.Width(Kit.Label, 20, text) + 32, 34);
            _canvas.DrawRect(r, glass);
            _canvas.TextIn(Kit.Label, 20, r, text, colour, padX: 16);
            y += 40;
        }

        if (ShowHelp)
        {
            string[] help =
            {
                "WASD MOVE   SHIFT SPRINT   SPACE JUMP   N FLY   ESC MOUSE",
                "1-9 BLOCK   WHEEL / Q SHAPE   LMB PLACE   RMB REMOVE   U UPGRADE",
                "LOOK AT A SCREEN: WHEEL / CLICK / TAB / R / [ ]",
                "G GATHER ORE   F FIRE GUNS   E OPEN CRATE   F2 MODE   F1 HIDE",
            };
            float hy = size.Y - 20 - help.Length * 26;
            foreach (string line in help)
            {
                _canvas.Text(Kit.Label, 16, new Vector2(size.X - Kit.Width(Kit.Label, 16, line) - 24, hy), line, new Color(Kit.Cream, 0.75f));
                hy += 26;
            }
        }
    }
}
