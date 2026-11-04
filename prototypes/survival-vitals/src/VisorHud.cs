using System;
using System.Collections.Generic;
using Godot;
using SurvivalVitals.Model;

namespace SurvivalVitals;

/// <summary>
/// A simple visor-style HUD drawn in code: six vital bars bottom left, the surroundings top
/// left, what you carry top right, prompts under the crosshair and a beacon for each backpack.
///
/// A vital that is about to cost health flashes amber; one costing health right now flashes red
/// and says so. The flash always comes first, because the model's warning lines sit before its
/// harm lines. The full diegetic UI belongs to another prototype.
/// </summary>
public partial class VisorHud : Control
{
    public Main Main;

    /// <summary>Holds the flash in its lit phase, so a screenshot always catches it.</summary>
    public bool FreezeFlash;

    float _clock;
    Font _font;

    static readonly Color Amber = new(1f, 0.66f, 0.1f);
    static readonly Color Danger = new(0.95f, 0.12f, 0.12f);
    static readonly Color Text = Palette.Cream;
    static readonly Color Panel = new(0.08f, 0.09f, 0.14f, 0.55f);

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        _font = ThemeDB.FallbackFont;
    }

    public override void _Process(double delta)
    {
        _clock += (float)delta;
        QueueRedraw();
    }

    bool Lit(float hz) => FreezeFlash || (_clock * hz) % 1f < 0.62f;

    public override void _Draw()
    {
        if (Main?.Sim == null) return;
        SurvivalSim sim = Main.Sim;
        Vector2 size = Size;
        TickReport report = sim.Last;
        bool dead = sim.Player.Dead;

        if (sim.Player.Gear.HelmetOn && !dead) DrawVisorFrame(size);
        if (!dead)
        {
            DrawVitals(sim, report, new Vector2(28, size.Y - 28));
            DrawSurroundings(sim, report, new Vector2(24, 22));
            DrawCarried(sim, new Vector2(size.X - 24, 22));
            DrawCentreWarnings(report, size);
            DrawRect(new Rect2(size / 2 - new Vector2(2, 2), new Vector2(4, 4)), new Color(1, 1, 1, 0.85f));
            if (!string.IsNullOrEmpty(Main.Prompt)) Centered(Main.Prompt, new Vector2(size.X / 2, size.Y / 2 + 70), 19, Palette.Yellow);
            if (Main.Mining) Centered($"MINING  ·  ore {sim.Player.Inventory.Ore}  ·  food drain x{sim.Data.Vitals.Food.MiningMultiplier}", new Vector2(size.X / 2, size.Y / 2 + 44), 17, Amber);
        }
        if (!dead) DrawBackpackBeacons(sim, size);
        DrawMessages(size);
    }

    // ---- Vitals ------------------------------------------------------------------------------

    void DrawVitals(SurvivalSim sim, TickReport report, Vector2 bottomLeft)
    {
        VitalsRules r = sim.Data.Vitals;
        Vitals v = sim.Player.Vitals;
        double cap = sim.OxygenCapacity;
        const float rowHeight = 34f, labelWidth = 118f, barWidth = 250f, barHeight = 14f;
        var rows = new List<(string label, float fill, Color color, string value, Alert flags)>
        {
            ("HEALTH", (float)(v.Health / r.Health.Max), Palette.Orange, $"{v.Health:F0} / {r.Health.Max:F0}", Alert.Health),
            ("FOOD", (float)(v.Food / r.Food.Max), Palette.Yellow, $"{v.Food:F0} / {r.Food.Max:F0}", Alert.Food),
            ("STAMINA", (float)(v.Stamina / r.Stamina.Max), new Color("2a9d8f"), v.Winded ? "WINDED" : $"{v.Stamina:F0}", Alert.Stamina),
            ("OXYGEN", cap > 0 ? (float)(v.Oxygen / cap) : 0, new Color("7fb7be"),
                (report.Warnings & Alert.Breath) != 0 ? $"HOLD BREATH {v.Breath:F0} s" : cap > 0 ? $"{v.Oxygen:F0} / {cap:F0}" : "no suit",
                Alert.Oxygen | Alert.Breath),
            ("BODY TEMP", 0, Colors.White, $"{v.BodyTemperature:F1} °C", Alert.Cold | Alert.Heat),
            ("RADIATION", (float)Math.Min(1, v.Radiation / 10), Palette.RadGlow, $"{v.Radiation:F1} RAD", Alert.Radiation),
        };

        float top = bottomLeft.Y - rows.Count * rowHeight;
        DrawRect(new Rect2(bottomLeft.X - 12, top - 14, labelWidth + barWidth + 170, rows.Count * rowHeight + 18), Panel);
        for (int i = 0; i < rows.Count; i++)
        {
            var (label, fill, color, value, flags) = rows[i];
            float y = top + i * rowHeight;
            bool harm = (report.Harm & flags) != 0, warn = (report.Warnings & flags) != 0;
            if (harm && Lit(4f)) DrawRect(new Rect2(bottomLeft.X - 8, y - 4, labelWidth + barWidth + 162, rowHeight - 4), new Color(Danger, 0.55f));
            else if (warn && Lit(2.2f)) DrawRect(new Rect2(bottomLeft.X - 8, y - 4, labelWidth + barWidth + 162, rowHeight - 4), new Color(Amber, 0.4f));

            Color ink = harm ? new Color("ffd0c8") : Text;
            Write(label, new Vector2(bottomLeft.X, y + 17), 16, ink);
            var bar = new Rect2(bottomLeft.X + labelWidth, y + 5, barWidth, barHeight);
            DrawRect(bar, new Color(0, 0, 0, 0.5f));
            if (label == "BODY TEMP") DrawTemperatureGauge(r.Temperature, v.BodyTemperature, bar);
            else
            {
                DrawRect(new Rect2(bar.Position, new Vector2(barWidth * Mathf.Clamp(fill, 0, 1), barHeight)), color);
                if (label == "RADIATION")
                    foreach (double line in new[] { r.Radiation.WarnAbove, r.Radiation.DamageAbove })
                        DrawRect(new Rect2(bar.Position.X + barWidth * (float)(line / 10) - 1, bar.Position.Y - 3, 2, barHeight + 6), line == r.Radiation.DamageAbove ? Danger : Amber);
            }
            DrawRect(bar, new Color(1, 1, 1, 0.35f), false, 1f);
            Write(value, new Vector2(bar.End.X + 12, y + 17), 16, ink);
            if (harm) Write("-HP", new Vector2(bar.End.X + 128, y + 17), 16, Lit(4f) ? Danger : ink);
        }
    }

    /// <summary>Body temperature on a scale: the comfort band in the middle, harm beyond it.</summary>
    void DrawTemperatureGauge(TemperatureRules t, double body, Rect2 bar)
    {
        const float lo = 31f, hi = 43f;
        float X(double c) => bar.Position.X + bar.Size.X * (float)((c - lo) / (hi - lo));
        void Span(double a, double b, Color c) => DrawRect(new Rect2(X(a), bar.Position.Y, X(b) - X(a), bar.Size.Y), c);
        Span(lo, t.ComfortBand.Low, new Color("2f5fd0"));
        Span(t.ComfortBand.Low, t.WarnBand.Low, new Color("7fb0e8"));
        Span(t.WarnBand.Low, t.WarnBand.High, new Color("4a7a5a"));
        Span(t.WarnBand.High, t.ComfortBand.High, new Color("e8a07f"));
        Span(t.ComfortBand.High, hi, new Color("d03a2f"));
        float x = X(Math.Clamp(body, lo, hi));
        DrawRect(new Rect2(x - 2, bar.Position.Y - 4, 4, bar.Size.Y + 8), Colors.White);
    }

    // ---- Readouts --------------------------------------------------------------------------

    void DrawSurroundings(SurvivalSim sim, TickReport report, Vector2 at)
    {
        Exposure env = report.Exposure;
        Planet planet = sim.Planet;
        double hour = planet.HourOfDay(sim.Time);
        string clock = $"{(int)hour:00}:{(int)(hour % 1 * 60):00}  {(planet.IsNight(sim.Time) ? "NIGHT" : "DAY")}  ·  {planet.WeatherAt(sim.Time).Name}";
        bool breathable = env.Volume != null || env.OxygenPercent >= sim.Data.Vitals.Oxygen.BreathablePercent;
        string air = env.Volume != null ? "SEALED ROOM, AIR OK" : breathable ? $"AIR {env.OxygenPercent:F0}% O2" : $"NO AIR ({env.OxygenPercent:F0}% O2)";
        var lines = new List<(string, Color)>
        {
            (clock, Text),
            ($"{env.Place}  ·  {env.Temperature:F0} °C  ·  {air}  ·  {env.Radiation:F1} RAD", Text),
            ($"HELMET {(sim.Player.Gear.HelmetOn ? "SEALED" : "OPEN")}  ·  {PaceName(report.Pace, Main.Player)}", new Color("9fd8e8")),
        };
        if (!breathable) lines[1] = (lines[1].Item1, Amber);
        if (planet.TerritoryAt(sim.Player.Position) is Territory land) lines.Add(($"{land.Name}: no survival devices", new Color("ff8a7a")));
        DrawRect(new Rect2(at.X - 10, at.Y - 6, 640, lines.Count * 24 + 10), Panel);
        for (int i = 0; i < lines.Count; i++) Write(lines[i].Item1, at + new Vector2(0, 18 + i * 24), 17, lines[i].Item2);
    }

    static string PaceName(Pace pace, Player player) =>
        (player.Crouching ? "CROUCH" : pace switch { Pace.Sprint => "SPRINT", Pace.Run => "RUN", Pace.Walk => "WALK", _ => "STILL" })
        + (player.RunToggled ? "  (run on)" : "");

    void DrawCarried(SurvivalSim sim, Vector2 topRight)
    {
        Inventory inv = sim.Player.Inventory;
        Loadout gear = sim.Player.Gear;
        string suit = gear.Suit == null ? "no suit" : $"{sim.Data.Armor.Suit(gear.Suit.Kind).Name} {gear.Suit.Durability:P0}";
        int rations = inv.CountFood("ration-pack") + inv.CountFood("ration-paste") + inv.CountFood("meal") + inv.CountFood("long-haul-ration");
        var lines = new[]
        {
            $"{suit}  ·  boosters {gear.Boosters.Count}/{gear.Slots(sim.Data.Armor)}",
            $"organics {inv.Organics:F1} kg  ·  ore {inv.Ore}",
            $"rations {rations}  ·  fruit {inv.CountFood("fruit")}  ·  medkits {inv.Medkits}",
            $"O2 bottles {inv.SmallBottles} small, {inv.LargeBottles} large",
            "I inventory  ·  Q medkit  ·  H helmet  ·  B replicator  ·  K die",
        };
        DrawRect(new Rect2(topRight.X - 470, topRight.Y - 6, 480, lines.Length * 24 + 10), Panel);
        for (int i = 0; i < lines.Length; i++)
            Write(lines[i], new Vector2(topRight.X - 460, topRight.Y + 18 + i * 24), 16, i == lines.Length - 1 ? new Color(Text, 0.7f) : Text);
    }

    /// <summary>Falls and lava get a big centre warning: they come at you fast.</summary>
    void DrawCentreWarnings(TickReport report, Vector2 size)
    {
        string text = (report.Warnings & Alert.Lava) != 0 ? ((report.Harm & Alert.Lava) != 0 ? "IN LAVA: GET OUT" : "LAVA AHEAD")
            : (report.Warnings & Alert.Fall) != 0 ? "IMPACT WARNING: HOLD SPACE FOR JETPACK"
            : null;
        if (text != null && Lit(4f)) Centered(text, new Vector2(size.X / 2, size.Y * 0.32f), 30, Danger);
    }

    void DrawVisorFrame(Vector2 size)
    {
        DrawRect(new Rect2(Vector2.Zero, size), new Color(0.4f, 0.8f, 0.9f, 0.035f));
        var edge = new Color(0.04f, 0.06f, 0.09f, 0.75f);
        const float m = 14f;
        DrawRect(new Rect2(0, 0, size.X, m), edge);
        DrawRect(new Rect2(0, size.Y - m, size.X, m), edge);
        DrawRect(new Rect2(0, 0, m, size.Y), edge);
        DrawRect(new Rect2(size.X - m, 0, m, size.Y), edge);
        var bracket = new Color(Palette.Orange, 0.8f);
        foreach (Vector2 corner in new[] { new Vector2(m, m), new Vector2(size.X - m, m), new Vector2(m, size.Y - m), new Vector2(size.X - m, size.Y - m) })
        {
            float sx = corner.X < size.X / 2 ? 1 : -1, sy = corner.Y < size.Y / 2 ? 1 : -1;
            DrawLine(corner, corner + new Vector2(70 * sx, 0), bracket, 3f);
            DrawLine(corner, corner + new Vector2(0, 70 * sy), bracket, 3f);
        }
    }

    // ---- Beacons and messages --------------------------------------------------------------

    void DrawBackpackBeacons(SurvivalSim sim, Vector2 size)
    {
        Camera3D camera = GetViewport().GetCamera3D();
        if (camera == null || size.X < 200 || size.Y < 200) return; // headless runs have a tiny viewport
        foreach (Backpack pack in sim.World.Backpacks)
        {
            var world = new Vector3((float)pack.Position.X, Main.GroundHeight(pack.Position) + 1.2f, (float)pack.Position.Z);
            float distance = camera.GlobalPosition.DistanceTo(world);
            bool behind = camera.IsPositionBehind(world);
            Vector2 p = camera.UnprojectPosition(world);
            if (behind) p = size - p;
            var clamped = new Vector2(Mathf.Clamp(p.X, 40, size.X - 40), Mathf.Clamp(p.Y, 60, size.Y - 60));
            bool onScreen = !behind && clamped == p;
            var color = new Color(1f, 0.78f, 0.2f);
            DrawColoredPolygon(new[] { clamped + new Vector2(0, -10), clamped + new Vector2(8, 0), clamped + new Vector2(0, 10), clamped + new Vector2(-8, 0) }, color);
            Centered($"BACKPACK {distance:F0} m{(onScreen ? "" : "  >")}", clamped + new Vector2(0, -16), 15, color);
        }
    }

    void DrawMessages(Vector2 size)
    {
        float y = 178;
        foreach (var (text, age) in Main.RecentMessages())
        {
            float alpha = Mathf.Clamp(1.5f - age / 4f, 0, 1);
            Centered(text, new Vector2(size.X / 2, y), 19, new Color(Text, alpha));
            y += 28;
        }
    }

    // ---- Text --------------------------------------------------------------------------------

    void Write(string text, Vector2 at, int size, Color color)
    {
        DrawStringOutline(_font, at, text, HorizontalAlignment.Left, -1, size, 5, new Color(0, 0, 0, 0.8f * color.A));
        DrawString(_font, at, text, HorizontalAlignment.Left, -1, size, color);
    }

    void Centered(string text, Vector2 at, int size, Color color)
    {
        float width = _font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
        Write(text, at - new Vector2(width / 2, 0), size, color);
    }
}
