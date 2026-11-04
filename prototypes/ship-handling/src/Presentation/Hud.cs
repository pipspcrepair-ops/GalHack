using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipHandling.Handling;
using ShipHandling.Physics;

namespace ShipHandling;

/// <summary>
/// A plain cockpit HUD: the console with the four numbers, flight data, the lift-margin
/// warning, damage taken and a log of physics events. A debug overlay, not the diegetic UI.
/// </summary>
public partial class Hud : CanvasLayer
{
    Label _title, _numbers, _flight, _log, _warning, _help;
    StickView _stick;
    readonly List<string> _events = new();

    public override void _Ready()
    {
        var font = new SystemFont { FontNames = new[] { "Consolas", "DejaVu Sans Mono", "Menlo", "monospace" } };

        var panel = new PanelContainer { Position = new Vector2(14f, 12f) };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.06f, 0.09f, 0.72f),
            BorderColor = Palette.Orange,
            BorderWidthLeft = 6,
            BorderWidthTop = 2,
            ContentMarginLeft = 14f, ContentMarginRight = 14f, ContentMarginTop = 8f, ContentMarginBottom = 10f,
        });
        AddChild(panel);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        panel.AddChild(column);

        _title = MakeLabel(font, 19, Palette.Yellow);
        _numbers = MakeLabel(font, 15, new Color("ffe8c2"));
        _flight = MakeLabel(font, 15, new Color("9fe3e0"));
        _log = MakeLabel(font, 13, new Color("d9c6a5"));
        column.AddChild(_title);
        column.AddChild(_numbers);
        column.AddChild(_flight);
        column.AddChild(_log);

        _warning = MakeLabel(font, 20, new Color("ff5a4f"));
        _warning.HorizontalAlignment = HorizontalAlignment.Center;
        _warning.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _warning.Position = new Vector2(-460f, 22f);
        _warning.Size = new Vector2(920f, 60f);
        AddChild(_warning);

        _help = MakeLabel(font, 13, new Color(1f, 0.92f, 0.78f, 0.85f));
        _help.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        _help.Position = new Vector2(16f, -58f);
        _help.Text =
            "1/2/3 vessel · WASD thrust · mouse steer · Space/Ctrl up/down (rover: Space hops) · Q/E roll · Shift boost · C cruise · G gear\n" +
            "V cockpit/chase · F leave/take the seat · ] / [ add/remove main engine · K +50 t cargo · L lopsided · Backspace reset · Esc mouse";
        AddChild(_help);

        _stick = new StickView();
        _stick.SetAnchorsPreset(Control.LayoutPreset.Center);
        AddChild(_stick);
    }

    static Label MakeLabel(Font font, int size, Color color)
    {
        var label = new Label();
        label.AddThemeFontOverride("font", font);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
        label.AddThemeConstantOverride("outline_size", 4);
        return label;
    }

    public void Log(string line)
    {
        _events.Add(line);
        if (_events.Count > 5) _events.RemoveAt(0);
    }

    public void Show(Vessel v, VesselLoadout loadout, bool seated, Vector2 stick)
    {
        HandlingStats s = v.Stats;
        string build = (loadout.Lopsided ? "  LOPSIDED" : "") + (loadout.CargoMass > 0f ? $"  cargo {loadout.CargoMass:F0} t" : "");
        _title.Text = $"{v.Structure.Name.ToUpperInvariant()}  ·  {s.ClassLabel}  ·  {s.Mass:F1} t  ·  {v.Structure.Thrusters.Count} thrusters{build}";
        _numbers.Text = string.Join("\n", s.ConsoleLines());

        string gear = v.Gear == null ? "none (hover)"
            : (v.Gear.Deployed ? "DOWN" : v.Gear.Extension > 0f ? (v.Gear.Down ? "lowering" : "raising") : "up")
              + (v.Gear.Manual ? " (manual)" : " (auto)");
        string state = v.Frozen ? "LANDED (not simulating)" : v.Settling ? "settling" : !seated ? "autopilot holding course" : "flying";
        string boost = v.State.Boosting ? $"BOOST {v.State.BoostLeft:F1} s"
            : v.State.BoostReady ? "boost ready" : $"boost in {v.State.BoostCooldown:F1} s";
        string ground = v.Hover != null && float.IsFinite(v.Hover.Clearance)
            ? $"ride {v.Hover.Clearance:F2} m" + (v.Hover.LookAheadLift > 0.05f ? $" (+{v.Hover.LookAheadLift:F1} look-ahead)" : "")
            : float.IsFinite(v.GroundDistance) ? $"{v.GroundDistance:F0} m above ground" : "no ground below";
        string where = v.Conditions.InAtmosphere ? $"ATMOSPHERE  g {v.Conditions.PlanetGravity:F1}" : "SPACE  no weight";
        float hull = Mathf.Max(0f, 1f - v.Damage / v.HullPoints);
        _flight.Text =
            $"SPEED  {v.Speed,5:F1} m/s   {(v.State.Cruise ? $"CRUISE {v.State.CruiseSpeed:F0}" : "")}\n" +
            $"ALT    {v.GlobalPosition.Y,5:F0} m   {ground}\n" +
            $"GEAR   {gear}   ·   {boost}\n" +
            $"HULL   {hull * 100f:F0}%   damage taken {v.Damage:F1} of {v.HullPoints:F0}\n" +
            $"{where}   ·   {state}" + (v.Hover != null && v.Hover.SlopeRefused ? "   ·   SLOPE TOO STEEP" : "");
        _log.Text = string.Join("\n", _events.AsEnumerable().Reverse());

        var warnings = new List<string>();
        if (s.LiftWarning is string lift) warnings.Add(lift);
        if (v.ConsoleMessage != null && v.ConsoleMessageAge < 3f && v.ConsoleMessage != s.LiftWarning) warnings.Add(v.ConsoleMessage);
        if (hull <= 0f) warnings.Add("HULL BREACHED (Backspace to reset)");
        _warning.Text = string.Join("\n", warnings);

        _stick.Value = stick;
        _stick.QueueRedraw();
    }

    /// <summary>The mouse stick: where the virtual stick sits in its circle.</summary>
    sealed partial class StickView : Control
    {
        public Vector2 Value;

        public override void _Draw()
        {
            const float r = 46f;
            DrawArc(Vector2.Zero, r, 0f, Mathf.Tau, 48, new Color(1f, 0.9f, 0.7f, 0.35f), 1.5f);
            DrawLine(new Vector2(-6f, 0f), new Vector2(6f, 0f), new Color(1f, 1f, 1f, 0.7f), 1.5f);
            DrawLine(new Vector2(0f, -6f), new Vector2(0f, 6f), new Color(1f, 1f, 1f, 0.7f), 1.5f);
            DrawCircle(Value * r, 4f, Palette.Yellow);
        }
    }
}
