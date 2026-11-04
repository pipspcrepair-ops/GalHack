using System.Collections.Generic;
using System.Text;
using Godot;
using Weather.Model;

namespace Weather;

/// <summary>
/// A minimal visor: forecast top right, warnings top centre, conditions bottom right, your
/// vitals bottom left, all thin amber lines on a faint dark wash so they read in a whiteout as
/// well as a night storm. Text is rebuilt a few times a second, not every frame.
/// </summary>
public partial class Hud : Control
{
    public WeatherDirector Director;
    public Player Player;
    public bool ShowHelp = true;

    static readonly Color Amber = new("ffb347");
    static readonly Color AmberDim = new(1f, 0.7f, 0.28f, 0.55f);
    static readonly Color Danger = new("ff4d2e");
    static readonly Color Cyan = new("5ce1e6");
    static readonly Color Wash = new(0.02f, 0.03f, 0.05f, 0.42f);

    Font _font;
    Label _forecast, _now, _env, _warnings, _messages, _help;
    float _refresh, _time;
    readonly List<ForecastEntry> _forecastList = new();
    readonly List<WarningEntry> _warningList = new();
    bool _danger;
    readonly StringBuilder _sb = new();

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        _font = new SystemFont { FontNames = new[] { "Consolas", "DejaVu Sans Mono", "Menlo", "Courier New", "monospace" }, Antialiasing = TextServer.FontAntialiasing.Gray };
        _now = MakeLabel(new Vector2(36, 30), 17, Amber);
        _forecast = MakeLabel(new Vector2(0, 30), 16, Amber, HorizontalAlignment.Right);
        _warnings = MakeLabel(new Vector2(0, 30), 17, Danger, HorizontalAlignment.Center);
        _env = MakeLabel(new Vector2(0, 0), 16, Amber);
        _messages = MakeLabel(new Vector2(0, 0), 20, Amber, HorizontalAlignment.Center);
        _help = MakeLabel(new Vector2(0, 0), 13, AmberDim, HorizontalAlignment.Center);
    }

    Label MakeLabel(Vector2 at, int size, Color color, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var label = new Label { Position = at, HorizontalAlignment = align, MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeFontOverride("font", _font);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.8f));
        label.AddThemeConstantOverride("outline_size", 4);
        label.AddThemeConstantOverride("line_spacing", 2);
        AddChild(label);
        return label;
    }

    public override void _Process(double delta)
    {
        if (Director?.Sim == null) return;
        _time += (float)delta;
        _refresh -= (float)delta;
        if (_refresh <= 0f)
        {
            _refresh = 0.2f;
            RebuildText();
        }
        QueueRedraw();
    }

    void RebuildText()
    {
        WeatherSim sim = Director.Sim;
        Conditions c = sim.Current;
        Vector2 size = GetViewportRect().Size;
        WeatherState now = sim.Data.State(sim.Dominant);
        var (incoming, blend) = sim.Incoming;

        _sb.Clear();
        _sb.Append(sim.Archetype.Name.ToUpperInvariant()).Append('\n');
        _sb.Append("NOW   ").Append(now.Name);
        if (blend < 0.98f && incoming != sim.Dominant) _sb.Append("  >  ").Append(sim.Data.State(incoming).Name.ToLowerInvariant()).Append(" rolling in");
        _sb.Append("\nFAUNA ").Append(now.CreatureHint.ToLowerInvariant()).Append($"  ({c.CreatureActivity * 100:F0}% active)");
        _now.Text = _sb.ToString();

        sim.GetForecast(_forecastList);
        _sb.Clear();
        _sb.Append("FORECAST\n");
        if (_forecastList.Count == 0) _sb.Append("no change ahead\n");
        foreach (ForecastEntry f in _forecastList)
            _sb.Append(f.Severe ? "! " : "  ").Append(f.Name).Append(" in ").Append(WeatherSim.Clock(f.Eta)).Append('\n');
        _forecast.Text = _sb.ToString();
        _forecast.Size = new Vector2(420, 120);
        _forecast.Position = new Vector2(size.X - 456, 30);

        sim.GetWarnings(_warningList, Player.GlobalPosition);
        _sb.Clear();
        _danger = false;
        int shown = 0;
        foreach (WarningEntry w in _warningList)
        {
            if (shown++ == 3) break;
            _danger |= w.Level == WarningLevel.Danger;
            _sb.Append(w.Level == WarningLevel.Danger ? "DANGER  " : "WARNING  ").Append(w.Text).Append('\n');
        }
        _warnings.Text = _sb.ToString();
        _warnings.Size = new Vector2(size.X * 0.5f, 100);
        _warnings.Position = new Vector2(size.X * 0.25f, 112);
        _warnings.AddThemeColorOverride("font_color", _danger ? Danger : Amber);

        Vector3 air = Director.Forces.AirAt(Player.GlobalPosition + Vector3.Up);
        float wind = new Vector2(air.X, air.Z).Length();
        _sb.Clear();
        _sb.Append($"TEMP     {c.Temperature,5:F0} °C\n");
        _sb.Append($"VIS      {FormatDistance(c.Visibility),8}\n");
        _sb.Append($"SOLAR    {c.Solar * 100,5:F0} %\n");
        _sb.Append($"WIND     {wind,5:F0} m/s {Compass(air)}\n");
        GroundCover g = sim.Ground;
        if (g.Wetness > 0.05f || g.Snow > 0.05f || g.Dust > 0.05f)
            _sb.Append($"GROUND   {(g.Snow > 0.05f ? $"snow {g.Snow * 100:F0}%" : g.Dust > g.Wetness ? $"dust {g.Dust * 100:F0}%" : $"wet {g.Wetness * 100:F0}%")}\n");
        _env.Text = _sb.ToString();
        _env.Size = new Vector2(320, 140);
        _env.Position = new Vector2(size.X - 420, size.Y - 160);

        _sb.Clear();
        foreach (var (text, _, _) in Director.Messages) _sb.Append(text).Append('\n');
        if (Player.Health.Dead) _sb.Append($"SUIT FAILURE. Respawning at the pad in {Mathf.Max(Player.RespawnIn, 0):F0} s\n");
        _messages.Text = _sb.ToString();
        _messages.Size = new Vector2(size.X, 120);
        _messages.Position = new Vector2(0, size.Y * 0.72f);

        _help.Visible = ShowHelp;
        _help.Text = "WASD move  Shift sprint  Space jump/jetpack  Ctrl crouch (brace)  V third person  Esc mouse  F1 hide help\n" +
                     "1 clouds  2 rain/monsoon  3 fog  4 snow/blizzard  5 dust  6 lightning  7 fire  8 hurricane  9 tornado storm  0 clear\n" +
                     $"T tornado heads your way  B next planet  R re-moor vehicles   {Engine.GetFramesPerSecond()} fps";
        _help.Size = new Vector2(size.X, 60);
        _help.Position = new Vector2(0, size.Y - 62);
    }

    static string FormatDistance(float metres) => metres >= 1000f ? $"{metres / 1000f:F1} km" : $"{metres:F0} m";

    static string Compass(Vector3 air)
    {
        if (new Vector2(air.X, air.Z).Length() < 0.5f) return "calm";
        // Wind is named for where it comes from.
        return "from " + WeatherSim.Bearing(Vector3.Zero, -air);
    }

    public override void _Draw()
    {
        Vector2 size = GetViewportRect().Size;
        float pulse = 0.55f + 0.45f * Mathf.Sin(_time * 9f);

        // Visor frame: corner brackets.
        float m = 18f, l = 46f;
        foreach (var (corner, dx, dy) in new[] { (new Vector2(m, m), 1, 1), (new Vector2(size.X - m, m), -1, 1), (new Vector2(m, size.Y - m), 1, -1), (new Vector2(size.X - m, size.Y - m), -1, -1) })
        {
            DrawLine(corner, corner + new Vector2(l * dx, 0), AmberDim, 2f);
            DrawLine(corner, corner + new Vector2(0, l * dy), AmberDim, 2f);
        }

        // Washes behind the text blocks, so they read in a whiteout.
        DrawRect(new Rect2(26, 24, 470, 76), Wash);
        DrawRect(new Rect2(size.X - 466, 24, 440, 28 + 21 * Mathf.Max(1, _forecastList.Count) + 4), Wash);
        DrawRect(new Rect2(size.X - 436, size.Y - 166, 410, 110), Wash);
        DrawRect(new Rect2(26, size.Y - 166, 470, 110), Wash);
        if (_warningList.Count > 0)
        {
            var box = new Rect2(size.X * 0.25f - 10, 104, size.X * 0.5f + 20, 14 + 23 * Mathf.Min(_warningList.Count, 3));
            DrawRect(box, new Color(0.15f, 0.02f, 0.0f, 0.45f));
            DrawRect(box, _danger ? new Color(Danger, pulse) : AmberDim, false, 2f);
        }

        // Vitals: health, suit, jetpack.
        var at = new Vector2(40, size.Y - 152);
        float health = Player.Health.Fraction;
        Color healthColor = Player.Health.InLastStand ? new Color(Danger, pulse) : health < 0.25f ? Danger : Amber;
        Bar(at, "HEALTH", health, healthColor, Player.Health.InLastStand ? $"LAST STAND {Player.Health.LastStand:F0}s" : $"{Player.Health.Current:F0}");
        float exposure = Player.Exposure;
        string suitNote = exposure > 0.05f ? $"{Director.Sim.Data.State(Director.Sim.Dominant).ExposureLabel} drain" : "";
        Bar(at + new Vector2(0, 32), "SUIT", Player.SuitPower, Cyan, suitNote);
        Bar(at + new Vector2(0, 64), "JET", Player.JetpackFuel, AmberDim, "");

        // Wind arrow beside the readout: points where the air is going.
        Vector3 air = Director.Forces.AirAt(Player.GlobalPosition + Vector3.Up);
        var flat = new Vector2(air.X, air.Z);
        var c = new Vector2(size.X - 70, size.Y - 112);
        DrawArc(c, 26f, 0, Mathf.Tau, 32, AmberDim, 1.5f);
        if (flat.Length() > 0.5f)
        {
            // Rotate into view space so the arrow is relative to where you're looking.
            Vector3 look = Player.LookDirection;
            float yaw = Mathf.Atan2(look.X, -look.Z);
            Vector2 dir = flat.Normalized().Rotated(-yaw);
            float len = Mathf.Clamp(flat.Length() / 40f, 0.25f, 1f) * 24f;
            Vector2 tip = c + new Vector2(dir.X, dir.Y) * len;
            DrawLine(c - new Vector2(dir.X, dir.Y) * len, tip, Amber, 3f);
            DrawLine(tip, tip - new Vector2(dir.X, dir.Y).Rotated(0.5f) * 9f, Amber, 3f);
            DrawLine(tip, tip - new Vector2(dir.X, dir.Y).Rotated(-0.5f) * 9f, Amber, 3f);
        }

        // Crosshair.
        Vector2 mid = size / 2f;
        DrawLine(mid - new Vector2(6, 0), mid + new Vector2(6, 0), new Color(1, 1, 1, 0.6f), 1.5f);
        DrawLine(mid - new Vector2(0, 6), mid + new Vector2(0, 6), new Color(1, 1, 1, 0.6f), 1.5f);

        // Damage and caught: a red edge to the visor.
        if (Player.Caught || Player.Health.InLastStand)
        {
            var edge = new Color(Danger, 0.35f * pulse);
            DrawRect(new Rect2(0, 0, size.X, 8), edge);
            DrawRect(new Rect2(0, size.Y - 8, size.X, 8), edge);
            DrawRect(new Rect2(0, 0, 8, size.Y), edge);
            DrawRect(new Rect2(size.X - 8, 0, 8, size.Y), edge);
        }
    }

    void Bar(Vector2 at, string name, float value, Color color, string note)
    {
        DrawString(_font, at + new Vector2(0, 14), name, HorizontalAlignment.Left, -1, 14, color);
        var frame = new Rect2(at + new Vector2(70, 2), new Vector2(200, 14));
        DrawRect(frame, AmberDim, false, 1.5f);
        DrawRect(new Rect2(frame.Position + new Vector2(2, 2), new Vector2(196 * Mathf.Clamp(value, 0f, 1f), 10)), color);
        if (note.Length > 0) DrawString(_font, at + new Vector2(280, 14), note, HorizontalAlignment.Left, -1, 13, color);
    }
}
