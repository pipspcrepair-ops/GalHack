using System.Collections.Generic;
using System.Linq;
using System.Text;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using ShipHandling.Handling;

namespace PhysicsSandbox.World;

/// <summary>
/// A debug overlay: the controls, the four numbers of whatever you fly or stand in, your health,
/// the wind, and a log of what the physics module just did (impacts, craters, rescues,
/// severing, landings). Not the diegetic UI.
/// </summary>
public partial class Hud : CanvasLayer
{
    public Main Main;
    Label _help, _status, _log;
    readonly List<string> _lines = new();
    public bool ShowHelp = true;

    public const string Controls =
        "WASD, mouse   walk and look (fly and steer when seated)\n" +
        "Space         jump; hold in the air for the jetpack; up when flying\n" +
        "Shift         sprint; boost when flying\n" +
        "Ctrl          down when flying\n" +
        "F             sit in / leave the pilot seat\n" +
        "Q / E         roll        G gear        C cruise\n" +
        "1 / 2 / 3     into the Corsair's cabin / the rover's seat / onto the Corsair's roof\n" +
        "Y             ship core gravity on / off\n" +
        "K             shoot an engine pod off the Corsair\n" +
        "B / N         blast a crater / dig at the crosshair\n" +
        "T             a tornado forms ahead and comes for you\n" +
        "V             first person / chase camera\n" +
        "R             start again      F1 hide this      Esc free the mouse";

    public override void _Ready()
    {
        Layer = 10;
        _help = Panel(new Vector2(16, 16), Controls);
        _status = Panel(new Vector2(16, 16), "");
        _status.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _status.GrowHorizontal = Control.GrowDirection.Begin;
        _status.Position = new Vector2(-16, 16);
        _status.HorizontalAlignment = HorizontalAlignment.Right;
        _log = Panel(new Vector2(16, 0), "");
        _log.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        _log.GrowVertical = Control.GrowDirection.Begin;
        _log.Position = new Vector2(16, -16);
    }

    Label Panel(Vector2 at, string text)
    {
        var label = new Label { Text = text, Position = at };
        label.AddThemeFontSizeOverride("font_size", 15);
        label.AddThemeColorOverride("font_color", Palette.Text);
        label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.75f));
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 1);
        AddChild(label);
        return label;
    }

    public void Log(string line)
    {
        _lines.Add(line);
        if (_lines.Count > 7) _lines.RemoveAt(0);
    }

    public override void _Process(double delta)
    {
        if (Main?.World?.Player == null) return;
        _help.Visible = ShowHelp;
        _log.Text = string.Join("\n", _lines);

        SandboxWorld w = Main.World;
        Player p = w.Player;
        var s = new StringBuilder();
        string where = p.Seated != null ? $"flying {p.Seated.Structure.Name}"
            : p.Aboard != null ? $"aboard {p.Aboard.Structure.Name}"
            : p.OnHull != null ? $"on {p.OnHull.Structure.Name}'s hull"
            : "outside";
        s.AppendLine($"{where}   health {p.HealthShare:P0}{(p.Health.Dead ? "  (down: R to start again)" : "")}");
        Ship ship = p.Seated ?? p.Aboard ?? p.OnHull ?? w.Corsair;
        if (ship?.Flight?.Stats is HandlingStats st && !ship.IsWreck)
        {
            foreach (string line in st.ConsoleLines()) s.AppendLine(line);
            VesselController v = ship.Vessel;
            string gear = v?.Gear == null ? "" : v.Gear.Down ? "  gear down" : "  gear up";
            s.AppendLine($"{ship.Body.Velocity.Length():F0} m/s   alt {v?.GroundDistance:F0} m   hull {1f - ship.Body.Damage / ship.Body.Integrity:P0}{gear}" +
                         (v?.Frozen == true ? "   landed" : "") + (ship.Flight.State.Cruise ? "   cruise" : "") + (ship.Flight.State.Boosting ? "   BOOST" : ""));
            if (v?.ConsoleMessageAge < 3f) s.AppendLine(v.ConsoleMessage);
            s.AppendLine($"core gravity {(ship.Body.Core?.On == true ? "on" : "off")}");
        }
        else if (ship != null) s.AppendLine($"{ship.Structure.Name}: a wreck, {ship.Body.Mass / 1000f:F1} t");
        Vector3 wind = w.Weather.Calm ? Vector3.Zero : w.Weather.Sim.WindAt(p.Body.WorldPosition.ToGodot());
        s.AppendLine($"weather {w.Weather.Sim.Dominant}   wind {wind.Length():F0} m/s");
        foreach (var t in w.Weather.Sim.Tornadoes)
            s.AppendLine($"TORNADO {(t.Position - p.Body.WorldPosition.ToGodot()).Length():F0} m{(t.Touchdown ? "" : " (forming)")}");
        s.AppendLine($"{Engine.GetFramesPerSecond():F0} fps   physics {w.Host.LastStepMs:F2} ms   bodies {w.Physics.Bodies.Count}   rescues {w.Physics.Stuck.Rescues}");
        _status.Text = s.ToString();
    }
}
