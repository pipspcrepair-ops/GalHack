using System;
using System.Collections.Generic;
using DiegeticUi.Theme;
using GalHack.Physics.Engine;
using Godot;
using Slice.Items;
using Slice.Game;
using Slice.Player;
using Slice.Vessels;
using SurvivalVitals.Model;
using Slice.Surface;
using Survivor = Slice.Player.Survivor;
using Weather.Model;
using Palette = DiegeticUi.Theme.Palette;

namespace Slice.Ui;

/// <summary>
/// The helmet visor, in the diegetic UI's style (its theme, fonts and glass shader): vitals
/// along the bottom of the glass that flash while they are only a warning and turn red once
/// they cost health, a compass with the pod, your vessels and the nearest deposits on it, the
/// suit's assistant's lines, what the tool is aimed at, and a flight strip when seated. Calm
/// vitals fade away after a few seconds. Drawn every frame onto the main canvas.
/// </summary>
public partial class Visor : CanvasLayer
{
    static Palette C => UiTheme.Colors;

    public Game.Game Game;

    Control _hud;
    ShaderMaterial _glass;
    double _time;
    float _vitalsAlpha = 1f, _calm, _harm, _frost;
    readonly List<(string text, Color colour, double until)> _lines = new();
    readonly List<(Vector3 from, Vector3 to, bool hit, double until)> _tracers = new();
    double _threatUntil = -1, _hurtUntil = -1;
    Vector3 _threatFrom;
    /// <summary>The ore scanner the compass and the map read.</summary>
    public readonly DepositFinder Deposits = new();
    readonly List<(string text, double until)> _xp = new();
    double _levelUntil = -1, _hitUntil = -1;
    string _level = "";

    /// <summary>Is this alert flashing right now? (tests read it)</summary>
    public bool Flashing(Alert a) => (Game.Suit?.Last.Warnings & a) != 0;

    public bool ThreatShowing => _time < _threatUntil;

    public override void _Ready()
    {
        Layer = 1;
        _glass = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/visor_glass.gdshader") };
        var glass = new ColorRect { Name = "Glass", MouseFilter = Control.MouseFilterEnum.Ignore, Material = _glass };
        glass.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(glass);
        _hud = new Control { Name = "Hud", MouseFilter = Control.MouseFilterEnum.Ignore };
        _hud.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _hud.Draw += Draw;
        AddChild(_hud);
    }

    // ---- What the game tells the visor -----------------------------------------------------------

    /// <summary>A line from VESTA, the suit's assistant, at the lower left.</summary>
    public void Say(string text, Color colour)
    {
        _lines.RemoveAll(l => l.text == text);
        _lines.Add((text, colour, _time + 5));
        if (_lines.Count > 5) _lines.RemoveAt(0);
    }

    /// <summary>Experience earned: a short line at the upper right ("+3 XP ore").</summary>
    public void Xp(int amount, string why)
    {
        _xp.Add(($"+{amount} XP  {why}", _time + 2.2));
        if (_xp.Count > 4) _xp.RemoveAt(0);
    }

    /// <summary>A new level: a banner across the top for a few seconds that never blocks play.</summary>
    public void LevelUp(int level, int points)
    {
        _level = $"LEVEL UP  {level}   +{points} POINT{(points == 1 ? "" : "S")}";
        _levelUntil = _time + 3.5;
    }

    /// <summary>A shot landed on a creature: a hit marker at the crosshair.</summary>
    public void Hit() => _hitUntil = _time + 0.25;

    public bool LevelUpShowing => _time < _levelUntil;
    public int XpLines => _xp.Count;

    /// <summary>Something is winding up to attack: the warning, on the side it is coming from.</summary>
    public void Threat(Vector3 from)
    {
        _threatFrom = from;
        _threatUntil = _time + 1.6;
    }

    public void Hurt(Vector3 from)
    {
        _threatFrom = from;
        _hurtUntil = _time + 0.6;
    }

    // ---- Every frame ---------------------------------------------------------------------------

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _time += delta;
        Suit suit = Game.Suit;
        if (suit != null)
        {
            bool calm = suit.Last.Warnings == Alert.None && !ThreatShowing;
            _calm = calm ? _calm + dt : 0f;
            _vitalsAlpha = Mathf.MoveToward(_vitalsAlpha, _calm > 3f ? 0.25f : 1f, dt * (calm ? 0.8f : 4f));
            _harm = Mathf.MoveToward(_harm, suit.Last.Harm != Alert.None || _time < _hurtUntil ? 1f : 0f, dt * 2f);
            _frost = Mathf.MoveToward(_frost, (suit.Last.Warnings & Alert.Cold) != 0 ? 0.7f : 0f, dt * 0.2f);
        }
        _glass.SetShaderParameter("harm", _harm);
        _glass.SetShaderParameter("frost", _frost);
        _glass.SetShaderParameter("aspect", _hud.Size.X / Mathf.Max(1, _hud.Size.Y));
        _lines.RemoveAll(l => l.until < _time);
        _tracers.RemoveAll(t => t.until < _time);
        _xp.RemoveAll(x => x.until < _time);
        _hud.QueueRedraw();
    }

    float S => _hud.Size.Y / 900f;

    void Draw()
    {
        if (_hud.Size.Y < 100f) return; // headless: no window to draw on
        float s = S;
        Vector2 size = _hud.Size;
        if (Game.Phase == Phase.Generating)
        {
            Banner("GENERATING PLANET", "the escape pod is on its way down", C.Accent);
            return;
        }
        Survivor survivor = Game.Survivor;
        if (survivor == null) return;
        if (Game.Menu.IsOpen) return; // the Tab menu covers the view: the visor waits behind it
        DrawTracers();
        DrawCompass(survivor);
        DrawLines();
        DrawWarnings();
        DrawProgress();
        if (Game.Suit.Dead)
        {
            Banner($"{Game.Suit.Sim.Player.DeathCause?.ToUpperInvariant() ?? "DEAD"}", "R: wake at the escape pod (your backpack stays where you fell)", C.Danger);
            return;
        }
        DrawVitals();
        if (survivor.Seated is Vessel v) DrawFlight(v);
        else
        {
            DrawCrosshair();
            DrawHand();
        }
        if (Game.Phase == Phase.Landing && Game.SkipIntro == false)
            _hud.TextIn(UiTheme.Display, (int)(26 * s), new Rect2(0, size.Y * 0.18f, size.X, 40 * s), "RE-ENTRY", C.Warn, HorizontalAlignment.Center);
    }

    void Banner(string title, string detail, Color colour)
    {
        float s = S;
        Vector2 size = _hud.Size;
        var band = new Rect2(0, size.Y * 0.4f, size.X, 110 * s);
        _hud.Block(band, new Color(C.Glass, 0.8f));
        _hud.Block(new Rect2(0, band.Position.Y, 18 * s, band.Size.Y), colour);
        _hud.TextIn(UiTheme.Display, (int)(40 * s), new Rect2(0, band.Position.Y + 8 * s, size.X, 56 * s), title, colour, HorizontalAlignment.Center);
        _hud.TextIn(UiTheme.Label, (int)(24 * s), new Rect2(0, band.Position.Y + 64 * s, size.X, 36 * s), detail, C.Text, HorizontalAlignment.Center);
    }

    // ---- Vitals along the bottom of the glass ----------------------------------------------------

    void DrawVitals()
    {
        Suit suit = Game.Suit;
        Vitals v = suit.Vitals;
        VitalsRules r = suit.Sim.Data.Vitals;
        Alert warn = suit.Last.Warnings, harm = suit.Last.Harm;
        var items = new (string label, string value, double share, Alert alert)[]
        {
            ("HEALTH", $"{v.Health:F0}", v.Health / r.Health.Max, Alert.Health),
            ("FOOD", $"{suit.FoodShare * 100:F0}%", suit.FoodShare, Alert.Food),
            ("O2", (warn & Alert.Breath) != 0 ? $"{v.Breath:F0}s" : $"{suit.OxygenShare * 100:F0}%", (warn & Alert.Breath) != 0 ? v.Breath / r.Oxygen.HoldBreathSeconds : suit.OxygenShare, Alert.Oxygen | Alert.Breath),
            ("TEMP", $"{v.BodyTemperature:F1}°", 1 - Math.Abs(v.BodyTemperature - r.Temperature.Normal) / 4.0, Alert.Cold | Alert.Heat),
            ("RAD", $"{v.Radiation:F1}", 1 - v.Radiation / (r.Radiation.DamageAbove * 1.5), Alert.Radiation),
            ("STAMINA", $"{v.Stamina:F0}", v.Stamina / r.Stamina.Max, Alert.Stamina),
        };
        float s = S;
        Vector2 size = _hud.Size;
        float w = 150 * s, h = 54 * s, gap = 14 * s;
        float total = items.Length * w + (items.Length - 1) * gap;
        for (int i = 0; i < items.Length; i++)
        {
            var (label, value, share, alert) = items[i];
            bool warned = (warn & alert) != 0, harmed = (harm & alert) != 0 || (alert == Alert.Health && harm != Alert.None);
            float off = (i - (items.Length - 1) / 2f) / items.Length;
            var at = new Vector2(size.X / 2 - total / 2 + i * (w + gap), size.Y - 96 * s + off * off * 120 * s);
            bool lit = harmed ? Mathf.PosMod((float)_time * 4.5f, 1f) < 0.55f : warned && Mathf.PosMod((float)_time * 2.2f, 1f) < 0.6f;
            Color tone = harmed ? C.Danger : warned ? C.Warn : C.Good;
            float a = warned || harmed ? 1f : _vitalsAlpha;
            var box = new Rect2(at, new Vector2(w, h));
            _hud.Block(box, new Color(lit ? tone : C.Glass, (lit ? 0.9f : 0.55f) * a));
            _hud.Block(new Rect2(at, new Vector2(6 * s, h)), new Color(tone, a));
            Color ink = new(lit ? C.Glass : warned || harmed ? tone : C.Text, a);
            _hud.Text(UiTheme.Label, (int)(16 * s), at + new Vector2(14 * s, 4 * s), label, ink);
            _hud.Text(UiTheme.Mono, (int)(20 * s), at + new Vector2(14 * s, 22 * s), value, ink);
            _hud.Segments(new Rect2(at + new Vector2(80 * s, 30 * s), new Vector2(w - 90 * s, 12 * s)), Math.Clamp(share, 0, 1), 1, new Color(tone, a), count: 8, gap: 2 * s, alpha: a);
        }
        if ((warn & Alert.Fall) != 0 && Mathf.PosMod((float)_time * 4f, 1f) < 0.6f)
            _hud.TextIn(UiTheme.Display, (int)(34 * s), new Rect2(0, size.Y * 0.6f, size.X, 50 * s), "IMPACT", C.Danger, HorizontalAlignment.Center);
        if (ThreatShowing && Mathf.PosMod((float)_time * 3f, 1f) < 0.6f)
        {
            Vector3 local = Game.Camera.GlobalBasis.Inverse() * (_threatFrom - Game.Camera.GlobalPosition);
            string side = MathF.Abs(local.X) < MathF.Abs(local.Z) * 0.6f ? (local.Z < 0 ? "AHEAD" : "BEHIND") : local.X < 0 ? "LEFT" : "RIGHT";
            _hud.TextIn(UiTheme.Display, (int)(28 * s), new Rect2(0, size.Y * 0.66f, size.X, 44 * s), $"THREAT {side}", C.Warn, HorizontalAlignment.Center);
        }
    }

    // ---- Compass ---------------------------------------------------------------------------------

    void DrawCompass(Survivor survivor)
    {
        float s = S;
        Vector2 size = _hud.Size;
        Vector3 look = survivor.WorldLook();
        float heading = Mathf.PosMod(Mathf.RadToDeg(Mathf.Atan2(look.X, -look.Z)), 360f);
        float width = 640 * s, y = 34 * s, cx = size.X / 2;
        _hud.Block(new Rect2(cx - width / 2, y + 22 * s, width, 2 * s), new Color(C.Text, 0.5f));
        for (int d = 0; d < 360; d += 15)
        {
            float off = Mathf.Wrap(d - heading, -180f, 180f);
            if (MathF.Abs(off) > 60f) continue;
            float x = cx + off / 60f * width / 2;
            bool major = d % 90 == 0;
            _hud.Block(new Rect2(x - s, y + (major ? 8 : 14) * s, 2 * s, (major ? 14 : 8) * s), new Color(C.Text, 0.7f));
            if (major)
                _hud.TextIn(UiTheme.Display, (int)(16 * s), new Rect2(x - 20 * s, y - 14 * s, 40 * s, 20 * s), d switch { 0 => "N", 90 => "E", 180 => "S", _ => "W" }, C.Text, HorizontalAlignment.Center);
        }
        _hud.Text(UiTheme.Mono, (int)(16 * s), new Vector2(cx - 18 * s, y + 28 * s), $"{heading:000}", C.Text);

        Vector3 feet = survivor.WorldFeet;
        var labels = new List<(float x, int row)>();
        void Mark(Vector3 at, string tag, Color colour)
        {
            Vector3 to = at - feet;
            float bearing = Mathf.PosMod(Mathf.RadToDeg(Mathf.Atan2(to.X, -to.Z)), 360f);
            float off = Mathf.Clamp(Mathf.Wrap(bearing - heading, -180f, 180f), -60f, 60f);
            float x = cx + off / 60f * width / 2;
            float d = new Vector2(to.X, to.Z).Length();
            _hud.Block(new Rect2(x - 5 * s, y + 4 * s, 10 * s, 10 * s), colour);
            // The first row whose neighbourhood is free, so labels at the same bearing stack.
            int row = 0;
            while (labels.Exists(l => l.row == row && MathF.Abs(l.x - x) < 110 * s)) row++;
            labels.Add((x, row));
            _hud.TextIn(UiTheme.Label, (int)(13 * s), new Rect2(x - 60 * s, y + (44 + row * 16) * s, 120 * s, 16 * s), $"{tag} {d:F0}m", colour, HorizontalAlignment.Center);
        }
        if (Game.Pod != null && survivor.Aboard != Game.Pod) Mark(Game.Pod.Body.Position.ToGodot(), "POD", C.Accent2);
        if (Game.Pod != null && survivor.Aboard != Game.Pod) Mark(Game.Pod.Body.Position.ToGodot(), "POD", C.Accent2);
        foreach (Vessel v in Game.Zone.Vessels)
            if (v != Game.Pod && !v.IsWreck && (v.Seat != null || v.IsBase) && survivor.Aboard != v) Mark(v.Body.Position.ToGodot(), v.Name.ToUpperInvariant(), C.Good);
        if (Game.Backpack != null) Mark(Game.Backpack.Position, "BACKPACK", C.Danger);
        if (survivor.Seated == null)
            foreach (var (kind, at) in Deposits.Near(Game.Zone, feet))
                Mark(at, kind.ToUpperInvariant(), C.Holo);
    }

    // ---- Lines, warnings, aim -------------------------------------------------------------------

    void DrawLines()
    {
        float s = S;
        Vector2 size = _hud.Size;
        float y = size.Y - 150 * s - _lines.Count * 26 * s;
        foreach (var (text, colour, until) in _lines)
        {
            float a = (float)Math.Clamp(until - _time, 0, 1);
            _hud.Text(UiTheme.Label, (int)(19 * s), new Vector2(40 * s, y), text, new Color(colour, a));
            y += 26 * s;
        }
    }

    void DrawWarnings()
    {
        float s = S;
        float y = 90 * s;
        foreach (WarningEntry w in Game.WeatherWarnings())
        {
            Color c = w.Level == WarningLevel.Danger ? C.Danger : w.Level == WarningLevel.Warning ? C.Warn : C.Dim;
            _hud.Text(UiTheme.Label, (int)(17 * s), new Vector2(40 * s, y), w.Text, c);
            y += 24 * s;
            if (y > 200 * s) break;
        }
    }

    /// <summary>Experience and levels: short lines at the upper right, a banner on a new level. Nothing blocks play.</summary>
    void DrawProgress()
    {
        float s = S;
        Vector2 size = _hud.Size;
        float y = 96 * s;
        foreach (var (text, until) in _xp)
        {
            float a = (float)Math.Clamp(until - _time, 0, 1);
            _hud.TextIn(UiTheme.Label, (int)(17 * s), new Rect2(size.X - 440 * s, y, 400 * s, 22 * s), text, new Color(C.Accent2, a), HorizontalAlignment.Right);
            y += 22 * s;
        }
        if (LevelUpShowing && Mathf.PosMod((float)_time * 2f, 1f) < 0.8f)
        {
            var band = new Rect2(size.X / 2 - 320 * s, 150 * s, 640 * s, 48 * s);
            _hud.Block(band, new Color(C.Accent, 0.85f));
            _hud.TextIn(UiTheme.Display, (int)(26 * s), band, _level, C.Glass, HorizontalAlignment.Center);
        }
    }

    void DrawCrosshair()
    {
        float s = S;
        Vector2 c = _hud.Size / 2;
        _hud.DrawCircle(c, 3 * s, new Color(C.Text, 0.85f));
        if (_time < _hitUntil)
            foreach (var d in new[] { new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, -1) })
                _hud.DrawLine(c + d * 7 * s, c + d * 15 * s, C.Danger, 3 * s);
        string label = Game.Pack.Hand switch
        {
            HandKind.Tool => Game.Tool.TargetLabel,
            HandKind.Block or HandKind.Core or HandKind.Device or HandKind.Blueprint => Game.Builder.Verdict,
            HandKind.Consumable => "click to use",
            _ => "",
        };
        if (!string.IsNullOrEmpty(label))
            _hud.TextIn(UiTheme.Label, (int)(18 * s), new Rect2(0, c.Y + 18 * s, _hud.Size.X, 24 * s), label, C.Text, HorizontalAlignment.Center);
    }

    /// <summary>The toolbar (1 to 9) at the lower right, what is in hand and its keys, and the head torch.</summary>
    void DrawHand()
    {
        float s = S;
        Vector2 size = _hud.Size;
        float cell = 52 * s, x = size.X - 9 * (cell + 4 * s) - 30 * s, y = size.Y - 150 * s;
        var items = Game.Zone.Data.Items;
        for (int i = 0; i < Slice.Items.PlayerInventory.ToolbarSlots; i++)
        {
            Slice.Items.Slot slot = Game.Pack.Grid[i];
            bool on = Game.Pack.Selected == i;
            var box = new Rect2(x + i * (cell + 4 * s), y, cell, cell);
            _hud.Block(box, new Color(on ? C.Accent : C.Glass, on ? 0.9f : 0.55f));
            string label = slot.Block == Slice.Items.PlayerInventory.BlueprintBlock ? "SKIFF" : slot.Block != null ? Slice.Ui.Slots.Short(Game.Zone.Data.Catalogue.Type(slot.Block).Name)
                : slot.Item != null ? items.Item(slot.Item).ShortName : "";
            _hud.TextIn(UiTheme.Label, (int)(11 * s), new Rect2(box.Position + new Vector2(2, 14 * s), new Vector2(cell - 4, 16 * s)), label, on ? C.Glass : C.Text, HorizontalAlignment.Center);
            if (slot.Count > 1) _hud.TextIn(UiTheme.Mono, (int)(12 * s), new Rect2(box.Position + new Vector2(2, cell - 18 * s), new Vector2(cell - 5, 16 * s)), $"{slot.Count}", on ? C.Glass : C.Accent2, HorizontalAlignment.Right);
            _hud.Text(UiTheme.Mono, (int)(11 * s), box.Position + new Vector2(3, 1), $"{i + 1}", on ? C.Glass : C.Dim);
        }
        HandKind hand = Game.Pack.Hand;
        string detail = hand switch
        {
            HandKind.Tool => Game.Tool.Mode == ToolMode.Shoot ? "SURVIVAL TOOL · SHOOT   [Q] GATHER" : "SURVIVAL TOOL · GATHER   [Q] SHOOT",
            HandKind.Block => $"{Game.Zone.Data.Catalogue.Type(Game.Pack.InHand.Block).Name} {(Game.Zone.Data.Catalogue.Type(Game.Pack.InHand.Block).IsArmor ? Game.Builder.Shape.Name : "")}  [WHEEL] SHAPE [R/T] TURN [X] MIRROR{(Game.Builder.MirrorX ? " ON" : "")} [RMB] TAKE DOWN",
            HandKind.Core => "CORE: set it down on open ground",
            HandKind.Device => "DEVICE: set it down on open ground",
            HandKind.Blueprint => Game.Builder.Underway != null ? $"{Game.Builder.Underway.Name}: {Game.Builder.Remaining.Count} blocks to go" : "BLUEPRINT PROJECTOR: set the Skiff down",
            HandKind.Consumable => "click to use",
            _ => "",
        };
        _hud.Text(UiTheme.Label, (int)(15 * s), new Vector2(x, y + cell + 8 * s), detail, C.Text);
        string status = $"[L] TORCH {(Game.Torch.Visible ? "ON" : "OFF")}   [TAB] MENU   LVL {Game.Progress.Level}";
        _hud.Text(UiTheme.Label, (int)(15 * s), new Vector2(x, y - 22 * s), status, Game.Torch.Visible ? C.Accent2 : C.Dim);
        if (Game.Mode == GalHack.Items.GameMode.Creative) _hud.Text(UiTheme.Label, (int)(16 * s), new Vector2(x, y - 44 * s), "CREATIVE: building is free", C.Accent2);
    }

    // ---- Flying --------------------------------------------------------------------------------

    void DrawFlight(Vessel v)
    {
        float s = S;
        Vector2 size = _hud.Size;
        if (v.Flight == null || v.Stats == null) return;
        var c = v.Controller;
        string gear = c?.Gear == null ? "NO GEAR" : c.Gear.Deployed ? "GEAR DOWN" : "GEAR UP";
        string line = $"{v.Body.Velocity.Length():F0} m/s   ALT {v.Body.Position.Y:F0} m   AGL {v.AboveGround:F0} m   {gear}{(v.Flight.State.Cruise ? "   CRUISE" : "")}";
        // Under the compass, clear of the cockpit console.
        var band = new Rect2(size.X / 2 - 360 * s, 128 * s, 720 * s, 34 * s);
        _hud.Block(band, new Color(C.Glass, 0.6f));
        _hud.TextIn(UiTheme.Mono, (int)(19 * s), band, line, C.Text, HorizontalAlignment.Center);
        string message = c?.ConsoleMessageAge < 4f ? c.ConsoleMessage : v.Stats.LiftWarning;
        if (!string.IsNullOrEmpty(message))
            _hud.TextIn(UiTheme.Label, (int)(20 * s), new Rect2(0, band.End.Y + 8 * s, size.X, 26 * s), message, C.Warn, HorizontalAlignment.Center);
    }

    void DrawTracers()
    {
        Camera3D cam = Game.Camera;
        foreach (var (from, to, hit, _) in _tracers)
        {
            if (cam.IsPositionBehind(from) || cam.IsPositionBehind(to)) continue;
            _hud.DrawLine(cam.UnprojectPosition(from), cam.UnprojectPosition(to), hit ? C.Accent2 : C.Holo, 3 * S);
        }
    }
}
