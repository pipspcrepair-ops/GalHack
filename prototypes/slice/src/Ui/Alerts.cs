using System;
using System.Collections.Generic;
using DiegeticUi.Theme;
using Godot;
using SurvivalVitals.Model;
using Weather.Model;
using Palette = DiegeticUi.Theme.Palette;

namespace Slice.Ui;

/// <summary>
/// Hazards and attacks as symbols, never sentences, in a row along the toolbar's line from the far
/// left of the view. A hazard is its sign from the game's own sign set with a timer above it (until it
/// arrives, or once here until it ends) and an arrow for where it is or comes from. An attack flashes
/// an animal's or a person's silhouette with an arrow to the attacker, and makes no sound of its own:
/// the blow's own sounds and the damage vignette carry it. This layer sits over the Tab menu and the
/// escape menu, so a warning is never hidden behind a menu, and the row stays where it is while one is
/// open: no menu page uses that corner. A warning with no sign of its own in the sign set is not
/// shown: there is no generic one.
/// </summary>
public partial class Alerts : CanvasLayer
{
    static Palette C => UiTheme.Colors;

    public Game.Game Game;

    /// <summary>One symbol on the glass: the sign's name, the seconds its timer shows (0: none), and where it is.</summary>
    public readonly record struct Shown(string Sign, double Seconds, Vector3? From, bool Attack);

    readonly List<Shown> _hazards = new();
    readonly List<(string sign, Vector3 from, double until)> _attacks = new();
    readonly List<WarningEntry> _warnings = new();
    Control _view;
    double _time;

    /// <summary>The hazards shown now, most urgent first (tests read it).</summary>
    public IReadOnlyList<Shown> Hazards => _hazards;

    /// <summary>The attacks flashing now (tests read it).</summary>
    public IEnumerable<Shown> Attacks
    {
        get
        {
            foreach (var (sign, from, until) in _attacks)
                if (until > _time) yield return new Shown(sign, 0, from, true);
        }
    }

    /// <summary>How long an attack's silhouette flashes, seconds.</summary>
    public const double AttackSeconds = 1.4;

    /// <summary>The most hazards shown at once.</summary>
    public const int MostHazards = 4;

    public override void _Ready()
    {
        Layer = 9; // over the visor (1), the Tab menu (3) and the escape menu (8); under the start screen (10)
        ProcessMode = ProcessModeEnum.Always; // shown while the escape menu is up
        _view = new Control { Name = "Alerts", MouseFilter = Control.MouseFilterEnum.Ignore };
        _view.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _view.Draw += Draw;
        AddChild(_view);
    }

    /// <summary>An attack: the attacker's silhouette flashes at the left with an arrow to where it struck from.</summary>
    public void Attack(string sign, Vector3 from)
    {
        if (!Specific(sign)) return; // no generic "!" in its place
        _attacks.RemoveAll(a => a.sign == sign);
        _attacks.Add((sign, from, _time + AttackSeconds));
        if (_attacks.Count > 2) _attacks.RemoveAt(0);
    }

    /// <summary>The sign for whatever attacked: a person's mark for people, a Husk's sign for a Husk, the Hollow sign for a beast the fungus has, the wildlife sign for any other.</summary>
    public static string SignFor(Wildlife.Species s) =>
        s.Husk ? "husk_hazard" : s.Figure != null ? "hostile" : s.Corrupted ? "hollow_hazard" : "wildlife";

    public override void _Process(double delta)
    {
        _time += delta;
        _attacks.RemoveAll(a => a.until < _time);
        _hazards.Clear();
        if (Game?.Survivor == null || Game.Zone?.Weather == null || Game.Phase != Slice.Game.Phase.Playing) { _view.QueueRedraw(); return; }
        var seen = new HashSet<string>();
        _warnings.Clear();
        Game.Zone.Weather.Sim?.GetWarnings(_warnings, Game.Survivor.WorldFeet);
        foreach (WarningEntry w in _warnings)
        {
            // A warning with no sign of its own is skipped, never shown as a generic "!".
            if (w.Level == WarningLevel.Advisory || !Specific(w.Sign)) continue;
            // One sign per hazard kind: the most urgent of them (the list comes most urgent first).
            if (!seen.Add(w.Sign)) continue;
            _hazards.Add(new Shown(w.Sign, w.Eta > 0 ? w.Eta : w.Left, w.From, false));
        }
        if (Game.Suit != null && (Game.Suit.Last.Warnings & Alert.Lava) != 0 && Specific("lava") && seen.Add("lava")) _hazards.Add(new Shown("lava", 0, null, false));
        if (_hazards.Count > MostHazards) _hazards.RemoveRange(MostHazards, _hazards.Count - MostHazards);
        _view.QueueRedraw();
    }

    /// <summary>A timer as m:ss.</summary>
    public static string Clock(double seconds)
    {
        int s = (int)Math.Ceiling(seconds);
        return $"{s / 60}:{s % 60:00}";
    }

    // ---- Drawing ----------------------------------------------------------------------------------

    static readonly Dictionary<string, Texture2D> Signs = new();

    /// <summary>A sign's picture from the slice's copy of the sign set (data/signs), loaded once.</summary>
    public static Texture2D Sign(string name)
    {
        if (Signs.TryGetValue(name, out Texture2D t)) return t;
        string path = ProjectSettings.GlobalizePath($"res://data/signs/{name}.png");
        Image image = System.IO.File.Exists(path) ? Image.LoadFromFile(path) : null;
        if (image != null) image.GenerateMipmaps();
        return Signs[name] = image == null ? null : ImageTexture.CreateFromImage(image);
    }

    /// <summary>Whether the sign set has this sign.</summary>
    public static bool HasSign(string name) => System.IO.File.Exists(ProjectSettings.GlobalizePath($"res://data/signs/{name}.png"));

    /// <summary>
    /// Whether a sign is one of its own for what it warns of: named, in the sign set, and not the generic "!" hazard
    /// sign, which tells the player nothing. An alert without one is not shown at all; a hazard that has none gets one drawn.
    /// </summary>
    public static bool Specific(string sign) => !string.IsNullOrEmpty(sign) && sign != "hazard" && HasSign(sign);

    /// <summary>
    /// Where each of up to <paramref name="count"/> alerts goes in the row (pixels, <paramref name="scale"/> pixels to a
    /// design unit): the sign on the toolbar's line with its timer above it, left to right, most urgent first, as many as fit.
    /// </summary>
    public static List<(Rect2 Sign, Rect2 Timer)> RowCells(Rect2 row, float scale, int count)
    {
        var cells = new List<(Rect2, Rect2)>();
        float icon = HudLayout.AlertIcon * scale, step = HudLayout.AlertCell * scale, timerH = HudLayout.AlertTimerH * scale;
        int fit = Math.Max(0, (int)((row.Size.X - icon) / step) + 1);
        for (int i = 0; i < Math.Min(count, fit); i++)
        {
            float x = row.Position.X + i * step;
            cells.Add((new Rect2(x, row.End.Y - icon, icon, icon), new Rect2(x - 4f * scale, row.Position.Y, icon + 8f * scale, timerH - 2f * scale)));
        }
        return cells;
    }

    void Draw()
    {
        Vector2 size = _view.Size;
        if (size.Y < 100f || Game?.Camera == null) return; // headless: no window to draw on
        HudLayout layout = HudLayout.Compute(size, Game.Visor?.UiScale ?? 1f);
        float s = layout.Scale;
        // Attacks first, flashing, then the hazards, most urgent first.
        var shown = new List<Shown>(Attacks);
        shown.AddRange(_hazards);
        var cells = RowCells(layout.Alerts, s, shown.Count);
        for (int i = 0; i < cells.Count; i++)
        {
            Shown a = shown[i];
            var (signBox, timerBox) = cells[i];
            float alpha = a.Attack && Mathf.PosMod((float)_time * 5f, 1f) >= 0.6f ? 0.45f : 1f;
            DrawSign(a.Sign, signBox, alpha);
            if (a.Seconds > 0) _view.TextIn(UiTheme.Mono, Math.Max(8, (int)(17 * s)), timerBox, Clock(a.Seconds), C.Text, HorizontalAlignment.Center);
            if (a.From is Vector3 from)
            {
                Vector2 at = signBox.End - new Vector2(6f, 6f) * s;
                _view.DrawCircle(at, 10f * s, new Color(C.Glass, 0.85f));
                DrawArrow(at, from, a.Attack ? C.Danger : C.Warn, 0.55f * s);
            }
        }
        DrawStruggle(size, s);
    }

    void DrawSign(string name, Rect2 r, float alpha)
    {
        Texture2D t = Sign(name);
        if (t != null) _view.DrawTextureRect(t, r, false, new Color(1, 1, 1, alpha));
        else _view.Block(r, new Color(C.Danger, alpha));
    }

    /// <summary>An arrow in a ring, pointing where the thing is relative to the view (up: ahead).</summary>
    void DrawArrow(Vector2 at, Vector3 from, Color colour, float s)
    {
        Camera3D cam = Game.Camera;
        Vector3 local = cam.GlobalBasis.Inverse() * (from - cam.GlobalPosition);
        var dir = new Vector2(local.X, local.Z);
        dir = dir.LengthSquared() > 1e-4f ? dir.Normalized() : new Vector2(0f, -1f);
        float r = 16 * s;
        _view.DrawArc(at, r, 0, Mathf.Tau, 32, new Color(colour, 0.55f), 2f * s);
        Vector2 tip = at + dir * r * 0.85f, side = new Vector2(-dir.Y, dir.X);
        _view.DrawColoredPolygon(new[] { tip, at - dir * r * 0.35f + side * r * 0.5f, at - dir * r * 0.35f - side * r * 0.5f }, colour);
    }

    /// <summary>Held by a Husk with the helmet shut: the key to struggle free, and how far the struggle has got.</summary>
    void DrawStruggle(Vector2 size, float s)
    {
        if (Game.HeldBy == null || !Game.Suit.Sim.Player.Gear.HelmetOn) return;
        string key = $"[{Slice.Game.KeyBindings.Name(Godot.Key.Space)}]";
        var cap = new Rect2(size.X / 2f - 70 * s, size.Y * 0.62f, 140 * s, 40 * s);
        bool lit = Mathf.PosMod((float)_time * 6f, 1f) < 0.5f;
        _view.Block(cap, new Color(lit ? C.Danger : C.Glass, 0.85f));
        _view.Frame(cap, C.Danger, 2f * s);
        _view.TextIn(UiTheme.Display, (int)(20 * s), cap, key, lit ? C.Glass : C.Danger, HorizontalAlignment.Center);
        _view.Segments(new Rect2(cap.Position.X, cap.End.Y + 6 * s, cap.Size.X, 10 * s), Math.Clamp(Game.Struggle, 0, 1), 1, C.Good, count: 10, gap: 2 * s);
    }
}
