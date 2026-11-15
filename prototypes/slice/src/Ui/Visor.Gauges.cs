using System;
using System.Collections.Generic;
using System.Linq;
using DiegeticUi.Theme;
using Godot;
using Slice.Vessels;
using SurvivalVitals.Model;
using Suit = Slice.Player.Suit;

namespace Slice.Ui;

/// <summary>One gauge row: its label, its value, how full its bar is (none for a reading without a scale), its alert's state and its tone.</summary>
public readonly record struct Gauge(string Label, string Value, double? Share, Color Tone, bool Warned, bool Harmed);

/// <summary>The icons of the visor's icon rows and the structure's toolbar, each drawn by code.</summary>
public enum HudIcon { HelmetShut, HelmetOpen, Vacuum, Core, Power, Gravity, Atmosphere, ThrustersPassive, ThrustersActive, Spotlight, Gun, Turret, Scanner, Shield, Fabricator }

/// <summary>An icon in a row: lit (on) or dim, in its tone.</summary>
public readonly record struct IconState(HudIcon Icon, bool On, Color Tone);

/// <summary>
/// A structure's readouts in a command chair: the share of capacitor supply in use (over 1, it draws more each tick than
/// the capacitors supply), generator fuel's time left (s), the warp fuel's reach (AU) and the fold fuel's (LY). Null where
/// it has none to show.
/// </summary>
public readonly record struct StructureReadout(double? CapUse, double? FuelSeconds, double? WarpAu, double? FoldLy)
{
    /// <summary>A structure's readouts. Live power (power phase 1) fills these; until then there are none to read.</summary>
    public static StructureReadout Of(Vessel v) => default;
}

/// <summary>
/// The visor's gauges (design: Visor HUD layout): the sleeper's at the bottom left with its icon row above them, the
/// surroundings at the upper left, and, in a command chair, the structure's at the bottom right with its own icon row.
/// </summary>
public partial class Visor
{
    // ---- What they show ------------------------------------------------------------------------

    Gauge Rated(string label, string value, double? share, Alert alert)
    {
        Alert warn = Game.Suit.Last.Warnings, harm = Game.Suit.Last.Harm;
        bool warned = (warn & alert) != 0, harmed = (harm & alert) != 0 || (alert == Alert.Health && harm != Alert.None);
        return new Gauge(label, value, share, harmed ? C.Danger : warned ? C.Warn : C.Good, warned, harmed);
    }

    /// <summary>The sleeper's gauges, top to bottom: Health, Stamina, Food, Air (the suit's oxygen, or held breath with none to breathe) and Rads (the body's dose).</summary>
    public Gauge[] SleeperGauges()
    {
        Suit suit = Game.Suit;
        Vitals v = suit.Vitals;
        VitalsRules r = suit.Sim.Data.Vitals;
        bool holding = (suit.Last.Warnings & Alert.Breath) != 0;
        return new[]
        {
            Rated("HEALTH", $"{v.Health:F0}", v.Health / r.Health.Max, Alert.Health),
            Rated("STAMINA", $"{v.Stamina:F0}", v.Stamina / r.Stamina.Max, Alert.Stamina),
            Rated("FOOD", $"{suit.FoodShare * 100:F0}%", suit.FoodShare, Alert.Food),
            Rated("AIR", holding ? $"{v.Breath:F0}s" : $"{suit.OxygenShare * 100:F0}%", holding ? v.Breath / r.Oxygen.HoldBreathSeconds : suit.OxygenShare, Alert.Oxygen | Alert.Breath),
            Rated("RADS", $"{v.Radiation:F1}", v.Radiation / (r.Radiation.DamageAbove * 1.5), Alert.Radiation),
        };
    }

    /// <summary>The surroundings: the radiation level and the temperature here (in a room, the room's air).</summary>
    public Gauge[] Surroundings()
    {
        Exposure e = Game.Suit.Last.Exposure;
        VitalsRules r = Game.Suit.Sim.Data.Vitals;
        Gauge rad = Rated("RAD", $"{e.Radiation:F1}", null, Alert.Radiation);
        Gauge temp = Rated("TEMP", $"{e.Temperature:F0}°C", null, Alert.Cold | Alert.Heat);
        bool comfortable = e.Temperature >= r.Temperature.AmbientComfort.Low && e.Temperature <= r.Temperature.AmbientComfort.High;
        return new[]
        {
            rad with { Tone = e.Radiation > r.Radiation.DamageAbove ? C.Danger : e.Radiation > r.Radiation.WarnAbove ? C.Warn : rad.Tone },
            temp with { Tone = temp.Harmed ? C.Danger : comfortable && !temp.Warned ? C.Good : C.Warn },
        };
    }

    /// <summary>Is the sleeper inside the area of one of their own core structures?</summary>
    public bool NearCore => Game.CoresInReach.Any(v => v.IsPlayers);

    /// <summary>The sleeper's icon row, left to right: the helmet (shut or open), in vacuum, near one of the player's cores.</summary>
    public IconState[] SleeperIcons()
    {
        bool shut = Game.Suit.Sim.Player.Gear.HelmetOn, vacuum = Game.Suit.InVacuum;
        return new[]
        {
            new IconState(shut ? HudIcon.HelmetShut : HudIcon.HelmetOpen, true, shut ? C.Holo : vacuum ? C.Danger : C.Text),
            new IconState(HudIcon.Vacuum, vacuum, C.Warn),
            new IconState(HudIcon.Core, NearCore, C.Accent2),
        };
    }

    /// <summary>The structure's gauges, top to bottom: capacitor supply in use, fuel's operating time in minutes, warp AU and fold LY left.</summary>
    public Gauge[] StructureGauges(Vessel v)
    {
        StructureReadout r = StructureReadout.Of(v);
        Gauge Row(string label, double? value, Func<double, string> shown, Color tone, double? share = null) =>
            new(label, value is double x ? shown(x) : "--", share, value == null ? C.Dim : tone, false, false);
        double cap = r.CapUse ?? 0, fuel = r.FuelSeconds ?? 0;
        return new[]
        {
            Row("CAP", r.CapUse, x => $"{x * 100:F0}%", cap > 1 ? C.Danger : cap > 0.9 ? C.Warn : C.Good, r.CapUse),
            Row("FUEL", r.FuelSeconds, x => double.IsInfinity(x) ? "IDLE" : $"{x / 60:F0} MIN", fuel <= 0 ? C.Danger : fuel < 300 ? C.Warn : C.Good),
            Row("WARP", r.WarpAu, x => $"{x:F1} AU", r.WarpAu < 1 ? C.Danger : C.Good),
            Row("FOLD", r.FoldLy, x => $"{x:F1} LY", r.FoldLy < 1 ? C.Danger : C.Good),
        };
    }

    /// <summary>
    /// The structure's icon row, right to left (mirroring the sleeper's): power, gravity, active air, and its thrusters, passive
    /// (flight assist on: they hold it fixed) or active (assist off: they fire only when told). The flight model has no assist
    /// off yet, so a structure that flies always holds. Dim where it is off or there is none (a base has no thrusters).
    /// </summary>
    public IconState[] StructureIcons(Vessel v) => new[]
    {
        new IconState(HudIcon.Power, v.PowerOn, v.Powered ? C.Good : C.Danger), // switched on with no power to give: lit in danger
        new IconState(HudIcon.Gravity, v.Structure.GravityOn, C.Holo),
        new IconState(HudIcon.Atmosphere, Game.Suit.HoldsAir(v), C.Holo),
        new IconState(HudIcon.ThrustersPassive, v.Flight != null, C.Accent),
    };

    // ---- Drawing them --------------------------------------------------------------------------

    /// <summary>The sleeper's gauges and icon row; calm, they fade like the rest of the vitals.</summary>
    void DrawSleeper(HudLayout layout)
    {
        DrawGauges(layout.Sleeper, layout.Scale, SleeperGauges(), _vitalsAlpha, C.Holo);
        DrawIcons(layout.SleeperIcons, layout.Scale, SleeperIcons(), _vitalsAlpha, false);
    }

    /// <summary>The structure's gauges and icon row, at the bottom right, while seated.</summary>
    void DrawStructure(HudLayout layout, Vessel v)
    {
        DrawGauges(layout.Structure, layout.Scale, StructureGauges(v), 1f, C.Accent);
        DrawIcons(layout.StructureIcons, layout.Scale, StructureIcons(v), 1f, true);
    }

    /// <summary>The radiation and temperature here at the upper left: labels and values, no bars.</summary>
    void DrawSurroundings(HudLayout layout)
    {
        float s = layout.Scale, h = HudLayout.SurroundRowH * s;
        Gauge[] rows = Surroundings();
        _hud.Block(layout.Surroundings, new Color(C.Glass, 0.55f));
        _hud.Block(new Rect2(layout.Surroundings.Position, new Vector2(5 * s, layout.Surroundings.Size.Y)), C.Holo);
        for (int i = 0; i < rows.Length; i++)
        {
            Gauge g = rows[i];
            var row = new Rect2(layout.Surroundings.Position + new Vector2(14 * s, i * h), new Vector2(layout.Surroundings.Size.X - 24 * s, h));
            bool lit = Blink(g);
            if (lit) _hud.Block(new Rect2(row.Position - new Vector2(4 * s, 0), row.Size + new Vector2(8 * s, 0)), new Color(g.Tone, 0.85f));
            _hud.TextIn(UiTheme.Label, (int)(15 * s), row, g.Label, lit ? C.Glass : C.Text);
            _hud.TextIn(UiTheme.Mono, (int)(18 * s), row, g.Value, lit ? C.Glass : g.Tone, HorizontalAlignment.Right);
        }
    }

    /// <summary>Is a row lit this instant? Harmed it blinks fast, warned slowly, calm never.</summary>
    bool Blink(Gauge g) => g.Harmed ? Mathf.PosMod((float)_time * 4.5f, 1f) < 0.55f : g.Warned && Mathf.PosMod((float)_time * 2.2f, 1f) < 0.6f;

    /// <summary>A gauge panel: a row each, the label at its left, the bar in the middle and the value at its right.</summary>
    void DrawGauges(Rect2 panel, float s, Gauge[] gauges, float calmAlpha, Color stripe)
    {
        _hud.Block(panel, new Color(C.Glass, 0.55f * calmAlpha));
        _hud.Block(new Rect2(panel.Position, new Vector2(5 * s, panel.Size.Y)), new Color(stripe, calmAlpha));
        Rect2[] rows = HudLayout.GaugeRows(panel, s, gauges.Length);
        for (int i = 0; i < gauges.Length; i++)
        {
            Gauge g = gauges[i];
            float a = g.Warned || g.Harmed ? 1f : calmAlpha;
            Rect2 row = rows[i];
            bool lit = Blink(g);
            if (lit) _hud.Block(row, new Color(g.Tone, 0.85f));
            Color ink = new(lit ? C.Glass : g.Warned || g.Harmed ? g.Tone : C.Text, a);
            var label = new Rect2(row.Position + new Vector2(8 * s, 0), new Vector2(76 * s, row.Size.Y));
            var value = new Rect2(row.End.X - 74 * s, row.Position.Y, 70 * s, row.Size.Y);
            _hud.TextIn(UiTheme.Label, (int)(15 * s), label, g.Label, ink);
            _hud.TextIn(UiTheme.Mono, (int)(17 * s), value, g.Value, lit ? C.Glass : new Color(g.Warned || g.Harmed ? g.Tone : C.Text, a), HorizontalAlignment.Right);
            if (g.Share is double share)
                _hud.Segments(new Rect2(label.End.X + 4 * s, row.Position.Y + row.Size.Y / 2 - 5 * s, value.Position.X - label.End.X - 12 * s, 10 * s),
                    Math.Clamp(share, 0, 1), 1, new Color(lit ? C.Glass : g.Tone, a), count: 10, gap: 2 * s, alpha: a);
        }
    }

    /// <summary>An icon row: each icon in its cell, lit in its tone or dim; from the left, or from the right for the structure's.</summary>
    void DrawIcons(Rect2 row, float s, IconState[] icons, float calmAlpha, bool fromRight)
    {
        for (int i = 0; i < icons.Length; i++)
        {
            IconState icon = icons[i];
            Rect2 cell = HudLayout.IconAt(row, i, s, fromRight);
            float a = icon.On ? 1f : calmAlpha;
            _hud.Block(cell, new Color(C.Glass, 0.6f * a));
            if (icon.On) _hud.Frame(cell, new Color(icon.Tone, 0.9f), 1.5f * s);
            DrawGlyph(icon.Icon, cell, new Color(icon.On ? icon.Tone : C.Dim, icon.On ? 1f : 0.5f * a), s);
        }
    }

    /// <summary>An icon's picture, in simple vector lines on a 24-unit square at the cell's centre.</summary>
    void DrawGlyph(HudIcon icon, Rect2 cell, Color ink, float s)
    {
        Vector2 c = cell.GetCenter();
        float u = cell.Size.X / 34f, w = 2.2f * s;
        Vector2 P(float x, float y) => c + new Vector2(x, y) * u;
        void Line(float x0, float y0, float x1, float y1) => _hud.DrawLine(P(x0, y0), P(x1, y1), ink, w);
        void Ring(float x, float y, float r) => _hud.DrawArc(P(x, y), r * u, 0f, Mathf.Tau, 20, ink, w);
        void Dome() // a helmet's shell: the crown's arc and its sides down to the collar
        {
            _hud.DrawArc(P(0, 1), 10 * u, Mathf.Pi, Mathf.Tau, 16, ink, w);
            Line(-10, 1, -10, 9);
            Line(10, 1, 10, 9);
            Line(-11, 9, 11, 9);
        }
        switch (icon)
        {
            case HudIcon.HelmetShut:
                Dome();
                _hud.DrawRect(new Rect2(P(-7, -3), new Vector2(14, 8) * u), ink); // the visor down over the face
                break;
            case HudIcon.HelmetOpen:
                Dome();
                _hud.DrawRect(new Rect2(P(-7, -3), new Vector2(14, 8) * u), ink, false, w); // the face opening, bare
                Line(-7, -13, 7, -13); // the visor lifted over the crown
                break;
            case HudIcon.Vacuum:
                Ring(0, 0, 10); // nothing inside: no air
                Line(-7, 7, 7, -7);
                break;
            case HudIcon.Core:
                Line(0, -11, 11, 0);
                Line(11, 0, 0, 11);
                Line(0, 11, -11, 0);
                Line(-11, 0, 0, -11);
                _hud.DrawRect(new Rect2(P(-3, -3), new Vector2(6, 6) * u), ink);
                break;
            case HudIcon.Power:
                _hud.DrawColoredPolygon(new[] { P(3, -12), P(-7, 2), P(-1, 2), P(-3, 12), P(7, -2), P(1, -2) }, ink);
                break;
            case HudIcon.Gravity:
                Line(0, -11, 0, 4);
                Line(-6, -2, 0, 4);
                Line(6, -2, 0, 4);
                Line(-11, 10, 11, 10);
                break;
            case HudIcon.Atmosphere:
                Ring(-5, 4, 5);
                Ring(6, 5, 4);
                Ring(1, -6, 4);
                break;
            case HudIcon.ThrustersPassive:
                // A nozzle, then the hull held between two opposed pushes.
                _hud.DrawColoredPolygon(new[] { P(-12, -5), P(-6, -3), P(-6, 3), P(-12, 5) }, ink);
                Line(-3, -5, 1, 0);
                Line(1, 0, -3, 5);
                Line(11, -5, 7, 0);
                Line(7, 0, 11, 5);
                _hud.DrawCircle(P(4, 0), 2 * u, ink);
                break;
            case HudIcon.ThrustersActive:
                // A nozzle firing: its exhaust streaks out behind it.
                _hud.DrawColoredPolygon(new[] { P(12, -5), P(6, -3), P(6, 3), P(12, 5) }, ink);
                Line(3, 0, -11, 0);
                Line(3, -3, -7, -6);
                Line(3, 3, -7, 6);
                break;
            case HudIcon.Spotlight:
                // A lamp's housing and its beam spreading out ahead.
                _hud.DrawColoredPolygon(new[] { P(-12, -4), P(-5, -6), P(-5, 6), P(-12, 4) }, ink);
                Line(-3, -5, 11, -11);
                Line(-3, 0, 12, 0);
                Line(-3, 5, 11, 11);
                break;
            case HudIcon.Gun:
                // A barrel with its muzzle flash ahead, a round flying.
                _hud.DrawRect(new Rect2(P(-12, -3), new Vector2(14, 6) * u), ink);
                Line(-6, 3, -6, 8);
                Line(5, -5, 9, 0);
                Line(9, 0, 5, 5);
                Line(10, 0, 13, 0);
                break;
            case HudIcon.Turret:
                // A dome on its ring, its twin barrels out to one side.
                _hud.DrawArc(P(0, 4), 8 * u, Mathf.Pi, Mathf.Tau, 14, ink, w);
                Line(-11, 4, 11, 4);
                Line(-9, 8, 9, 8);
                Line(3, -2, 12, -7);
                Line(4, 1, 13, -4);
                break;
            case HudIcon.Scanner:
                // A dish and the rings it sends out.
                _hud.DrawArc(P(-6, 6), 6 * u, -Mathf.Pi * 0.75f, Mathf.Pi * 0.25f, 10, ink, w);
                Line(-6, 6, -10, 11);
                _hud.DrawArc(P(-6, 6), 11 * u, -Mathf.Pi * 0.45f, -Mathf.Pi * 0.05f, 8, ink, w);
                _hud.DrawArc(P(-6, 6), 16 * u, -Mathf.Pi * 0.42f, -Mathf.Pi * 0.08f, 8, ink, w);
                break;
            case HudIcon.Shield:
                // A shield's outline over a hull.
                Line(-9, -9, 9, -9);
                Line(-9, -9, -9, 1);
                Line(9, -9, 9, 1);
                Line(-9, 1, 0, 11);
                Line(9, 1, 0, 11);
                Line(0, -6, 0, 7);
                break;
            case HudIcon.Fabricator:
                // A cog: the manufacturing devices.
                Ring(0, 0, 6);
                for (int k = 0; k < 8; k++)
                {
                    float a = k * Mathf.Tau / 8f;
                    Line(MathF.Cos(a) * 7, MathF.Sin(a) * 7, MathF.Cos(a) * 11, MathF.Sin(a) * 11);
                }
                _hud.DrawCircle(P(0, 0), 2 * u, ink);
                break;
        }
    }
}
