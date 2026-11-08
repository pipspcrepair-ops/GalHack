using System;
using System.Collections.Generic;
using System.Linq;
using DiegeticUi.Theme;
using Godot;
using Slice.Game;
using Palette = DiegeticUi.Theme.Palette;

namespace Slice.Ui;

/// <summary>The options' pages.</summary>
public enum OptionsTab { Graphics, Sound, Controls, Mouse }

/// <summary>
/// The escape menu's OPTIONS: GRAPHICS, SOUND, CONTROLS and MOUSE. Each page is rows: the setting's
/// name with a line under it saying what it does, then its control, then its value. Every change
/// applies at once and is remembered (<see cref="GameSettings"/>; the volumes keep their own file,
/// <see cref="AudioSettings"/>). Nothing here needs a restart. Each page resets on its own.
/// </summary>
public sealed class OptionsSection : IMenuSection
{
    static Palette C => UiTheme.Colors;
    readonly Game.Game _game;
    public OptionsTab Tab;

    /// <summary>The action waiting for its new key (CONTROLS), or null.</summary>
    public string Capturing { get; private set; }

    /// <summary>VIEW THE WORLD (GRAPHICS): the menu steps aside so a change can be seen. Set by whoever hosts the section.</summary>
    public Action View;

    public OptionsSection(Game.Game game) => _game = game;
    public Vector2 PreferredSize => new(1170, 784);
    GameSettings S => _game.Settings;

    /// <summary>What the pages show changes with these (the menu redraws).</summary>
    public string Signature => $"{Tab}|{S.Version}|{_game.Audio?.Settings.Version}|{Capturing}";

    static readonly string[] Names = { "GRAPHICS", "SOUND", "CONTROLS", "MOUSE" };
    const float RowH = 70, LabelW = 420;

    public void EndCapture() => Capturing = null;

    /// <summary>The key pressed at a key prompt: bound to the action (Esc cancels; Esc itself can't be bound).</summary>
    public void Captured(Key key)
    {
        string id = Capturing;
        Capturing = null;
        if (id != null && key != Key.Escape) S.Keys.Bind(id, key);
    }

    public void Build(Gui g, Rect2 area)
    {
        for (int i = 0; i < Names.Length; i++)
        {
            var r = new Rect2(area.Position + new Vector2(i * 210, 0), new Vector2(200, 46));
            if (g.Tab($"opt:{Names[i].ToLowerInvariant()}", r, Names[i], Tab == (OptionsTab)i, C.Accent2))
            {
                Tab = (OptionsTab)i;
                Capturing = null;
            }
        }
        string reset = Tab switch { OptionsTab.Graphics => "RESET GRAPHICS", OptionsTab.Sound => "RESET SOUND", OptionsTab.Controls => "RESET KEYS", _ => "RESET MOUSE" };
        if (g.Button("opt:reset", new Rect2(area.End.X - 250, area.Position.Y, 250, 46), reset, C.Accent)) Reset();
        var body = new Rect2(area.Position + new Vector2(0, 62), new Vector2(area.Size.X, area.Size.Y - 62));
        switch (Tab)
        {
            case OptionsTab.Graphics: Graphics(g, body); break;
            case OptionsTab.Sound: Sound(g, body); break;
            case OptionsTab.Controls: Keys(g, body); break;
            case OptionsTab.Mouse: Mouse(g, body); break;
        }
    }

    void Reset()
    {
        switch (Tab)
        {
            case OptionsTab.Graphics:
                S.ResetGraphics();
                _game.ApplySettings();
                break;
            case OptionsTab.Sound:
                if (_game.Audio is SliceAudio audio)
                    foreach (string key in AudioSettings.Volumes) audio.SetVolume(key, new AudioSettings().Get(key));
                break;
            case OptionsTab.Controls:
                S.Keys.Reset();
                Capturing = null;
                break;
            case OptionsTab.Mouse:
                S.ResetMouse();
                break;
        }
    }

    /// <summary>A setting changed: counted (the file is written soon) and applied now.</summary>
    void Changed()
    {
        S.Changed();
        _game.ApplySettings();
    }

    // ---- GRAPHICS -----------------------------------------------------------------------------

    void Graphics(Gui g, Rect2 body)
    {
        GameSettings s = S;
        int i = 0;
        Rect2 r = Row(g, body, i++, "WINDOW", "borderless: full screen, quick to leave");
        int w = Choice(g, "opt:window", r, new[] { "WINDOWED", "BORDERLESS", "FULL SCREEN" }, (int)s.Window, 210);
        if (w >= 0 && w != (int)s.Window)
        {
            s.Window = (WindowKind)w;
            Changed();
        }

        bool windowed = s.Window == WindowKind.Windowed;
        r = Row(g, body, i++, "RESOLUTION", windowed ? "the window's size" : "windowed only; full screen uses the screen's");
        List<Vector2I> sizes = Resolutions(s.Resolution);
        int at = sizes.IndexOf(s.Resolution);
        if (g.Button("opt:res-down", new Rect2(r.Position, new Vector2(64, r.Size.Y)), "<", C.Accent2, windowed && at > 0, size: 22))
        {
            s.Resolution = sizes[at - 1];
            Changed();
        }
        g.TextIn($"{s.Resolution.X} x {s.Resolution.Y}", new Rect2(r.Position.X + 72, r.Position.Y, 268, r.Size.Y), windowed ? C.Text : C.Dim, 21, HorizontalAlignment.Center, UiTheme.Mono);
        if (g.Button("opt:res-up", new Rect2(r.Position.X + 348, r.Position.Y, 64, r.Size.Y), ">", C.Accent2, windowed && at < sizes.Count - 1, size: 22))
        {
            s.Resolution = sizes[at + 1];
            Changed();
        }

        r = Row(g, body, i++, "VSYNC", "in step with the screen: no tearing");
        int v = Choice(g, "opt:vsync", r, new[] { "ON", "OFF" }, s.Vsync ? 0 : 1, 150);
        if (v >= 0 && (v == 0) != s.Vsync)
        {
            s.Vsync = v == 0;
            Changed();
        }

        r = Row(g, body, i++, "FRAME RATE CAP", "the most frames drawn a second");
        int[] caps = GameSettings.FpsCaps;
        int cap = Choice(g, "opt:fps", r, caps.Select(f => f == 0 ? "NONE" : $"{f}").ToArray(), Array.IndexOf(caps, s.FpsCap), 96);
        if (cap >= 0 && caps[cap] != s.FpsCap)
        {
            s.FpsCap = caps[cap];
            Changed();
        }

        r = Row(g, body, i++, "RENDER SCALE", "the 3D view drawn smaller: faster, softer");
        if (Range(g, "opt:render-scale", r, ref s.RenderScale, GameSettings.MinScale, 1f, 0.05f, x => $"{x * 100f:F0}%")) Changed();

        r = Row(g, body, i++, "FIELD OF VIEW", "top to bottom of the view");
        if (Range(g, "opt:fov", r, ref s.Fov, GameSettings.MinFov, GameSettings.MaxFov, 1f, x => $"{x:F0} DEG")) Changed();

        r = Row(g, body, i++, "SHADOWS", $"the sun's reach, {Game.Game.Shadows(s.Shadows).Reach:F0} m; higher is sharper");
        int sh = Choice(g, "opt:shadows", r, new[] { "LOW", "MEDIUM", "HIGH", "ULTRA" }, (int)s.Shadows, 150);
        if (sh >= 0 && sh != (int)s.Shadows)
        {
            s.Shadows = (ShadowLevel)sh;
            Changed();
        }

        r = Row(g, body, i++, "GROUND DETAIL", "diggable ground round you; far land past it");
        int[] radii = Enumerable.Range(GameSettings.MinGround, GameSettings.MaxGround - GameSettings.MinGround + 1).ToArray();
        int gd = Choice(g, "opt:ground", r, radii.Select(n => $"{n * 16} M").ToArray(), Array.IndexOf(radii, s.GroundDetail), 120);
        if (gd >= 0 && radii[gd] != s.GroundDetail)
        {
            s.GroundDetail = radii[gd];
            Changed();
        }

        r = Row(g, body, i++, "BRIGHTNESS", "gamma: above 1 lifts the dark parts");
        if (Range(g, "opt:gamma", r, ref s.Gamma, GameSettings.MinGamma, GameSettings.MaxGamma, 0.05f, x => $"{x:F2}")) Changed();

        float foot = body.Position.Y + i * RowH + 4;
        g.Text("Every change applies at once and is remembered; none needs a restart.", new Vector2(body.Position.X + 16, foot + 4), C.Dim, 16);
        g.Text("Ground detail streams in once the world runs again.", new Vector2(body.Position.X + 16, foot + 26), C.Dim, 16);
        if (View != null && g.Button("opt:view", new Rect2(body.End.X - 300, foot, 300, 44), "VIEW THE WORLD", C.Accent2)) View();
    }

    /// <summary>Window sizes that fit the screen, smallest first, with the one set now among them.</summary>
    static List<Vector2I> Resolutions(Vector2I current)
    {
        Vector2I screen = DisplayServer.ScreenGetSize();
        var sizes = GameSettings.Resolutions.Where(r => screen.X <= 0 || (r.X <= screen.X && r.Y <= screen.Y)).ToList();
        if (!sizes.Contains(current)) sizes.Add(current);
        return sizes.OrderBy(r => r.X * r.Y).ToList();
    }

    // ---- SOUND --------------------------------------------------------------------------------

    void Sound(Gui g, Rect2 body)
    {
        if (_game.Audio is not SliceAudio audio)
        {
            g.Text("The sound starts once the world is ready.", body.Position + new Vector2(16, 12), C.Dim, 18);
            return;
        }
        string[] about = { "everything; it starts at 50%", "the generated music", "tools, building, creatures, machines, menus", "wind, rain, water, the howl at openings" };
        for (int i = 0; i < AudioSettings.Volumes.Length; i++)
        {
            string key = AudioSettings.Volumes[i];
            Rect2 r = Row(g, body, i, key.ToUpperInvariant(), about[i], 90);
            float value = audio.Settings.Get(key);
            if (Range(g, $"opt:volume-{key}", r, ref value, 0f, 1f, 0.01f, x => $"{x * 100f:F0}%")) audio.SetVolume(key, value);
        }
        g.Text("The music plays on while the world is stopped, so it can be set by ear.", new Vector2(body.Position.X + 16, body.Position.Y + 4 * 90 + 8), C.Dim, 16);
    }

    // ---- CONTROLS -----------------------------------------------------------------------------

    void Keys(Gui g, Rect2 body)
    {
        // A click anywhere ends a key prompt (a click on a key below starts its own).
        if (!g.Drawing && g.Pressed && Capturing != null) Capturing = null;
        g.Text("Click a key, then press the one you want (Esc cancels). A key on two actions shows red: it does both.", body.Position + new Vector2(4, 0), C.Dim, 16);
        float colW = (body.Size.X - 24) / 2, top = body.Position.Y + 34;
        string[][] columns = { new[] { KeyBindings.Move, KeyBindings.Hands }, new[] { KeyBindings.Build, KeyBindings.Menus, KeyBindings.Toolbar } };
        for (int c = 0; c < columns.Length; c++)
        {
            float x = body.Position.X + c * (colW + 24), y = top;
            foreach (string group in columns[c])
            {
                g.Caption(group, new Vector2(x, y), colW);
                y += 34;
                KeyAction[] actions = KeyBindings.Actions.Where(a => a.Group == group).ToArray();
                if (group == KeyBindings.Toolbar)
                {
                    Slots(g, new Rect2(x, y, colW, 44), actions);
                    y += 52;
                    continue;
                }
                for (int j = 0; j < actions.Length; j++, y += 39) KeyRow(g, new Rect2(x, y, colW, 36), actions[j], j);
                y += 6;
            }
        }
        var fixedKeys = new Rect2(body.Position.X, body.End.Y - 62, body.Size.X, 58);
        g.Fill(fixedKeys, new Color(C.Panel, 0.6f));
        g.Text("FIXED     ESC  this menu     LEFT MOUSE  use, dig, build, shoot     RIGHT MOUSE  take a block away     WHEEL  slot or shape",
            fixedKeys.Position + new Vector2(14, 7), C.Dim, 15);
        g.Text("IN MENUS     /  search     CTRL+A  select all     SHIFT-CLICK  move a stack     + AND -  zoom the map",
            fixedKeys.Position + new Vector2(14, 31), C.Dim, 15);
    }

    /// <summary>One action: its name, what it does (or, red, what else is on its key), what it was, and its key.</summary>
    void KeyRow(Gui g, Rect2 r, KeyAction a, int j)
    {
        KeyBindings keys = S.Keys;
        List<KeyAction> clash = keys.Clashes(a.Id).ToList();
        g.Fill(r, new Color(j % 2 == 0 ? C.Panel : C.Glass, 0.6f));
        g.Text(a.Name.ToUpperInvariant(), r.Position + new Vector2(12, 1), C.Text, 17);
        string under = clash.Count > 0 ? $"SAME KEY AS {string.Join(", ", clash.Select(c => c.Name.ToUpperInvariant()))}" : a.Uses;
        g.Text(under, r.Position + new Vector2(12, 20), clash.Count > 0 ? C.Danger : C.Dim, 13);
        var key = new Rect2(r.End.X - 156, r.Position.Y + 2, 150, r.Size.Y - 4);
        if (keys[a.Id] != a.Default) g.TextIn($"WAS {KeyBindings.Name(a.Default)}", new Rect2(key.Position.X - 110, r.Position.Y, 100, r.Size.Y), C.Dim, 13, HorizontalAlignment.Right);
        bool asking = Capturing == a.Id;
        if (g.Button($"key:{a.Id}", key, asking ? "PRESS A KEY" : KeyBindings.Name(keys[a.Id]), clash.Count > 0 ? C.Danger : C.Accent2, lit: asking || clash.Count > 0)) Capturing = a.Id;
    }

    /// <summary>The toolbar's nine slots on one row of keys.</summary>
    void Slots(Gui g, Rect2 r, KeyAction[] slots)
    {
        KeyBindings keys = S.Keys;
        float w = (r.Size.X - (slots.Length - 1) * 6) / slots.Length;
        for (int i = 0; i < slots.Length; i++)
        {
            KeyAction a = slots[i];
            bool clash = keys.Clashes(a.Id).Any(), asking = Capturing == a.Id;
            var b = new Rect2(r.Position.X + i * (w + 6), r.Position.Y, w, r.Size.Y);
            if (g.Button($"key:{a.Id}", b, asking ? "?" : KeyBindings.Name(keys[a.Id]), clash ? C.Danger : C.Accent2, lit: asking || clash)) Capturing = a.Id;
        }
    }

    // ---- MOUSE --------------------------------------------------------------------------------

    void Mouse(Gui g, Rect2 body)
    {
        GameSettings s = S;
        Rect2 r = Row(g, body, 0, "LOOK SENSITIVITY", "on foot and on the hover bike", 90);
        if (Range(g, "opt:sensitivity", r, ref s.Sensitivity, GameSettings.MinSense, GameSettings.MaxSense, 0.05f, x => $"{x:F2}x")) Changed();

        r = Row(g, body, 1, "INVERT LOOK", "on: mouse forward looks down", 90);
        int inv = Choice(g, "opt:invert", r, new[] { "OFF", "ON" }, s.InvertY ? 1 : 0, 150);
        if (inv >= 0 && (inv == 1) != s.InvertY)
        {
            s.InvertY = inv == 1;
            Changed();
        }

        r = Row(g, body, 2, "FLIGHT STICK", "flying: how far the mouse pushes the stick", 90);
        if (Range(g, "opt:flight-sensitivity", r, ref s.FlightSensitivity, GameSettings.MinSense, GameSettings.MaxSense, 0.05f, x => $"{x:F2}x")) Changed();

        r = Row(g, body, 3, "INVERT FLIGHT PITCH", "flips the stick's up and down", 90);
        int finv = Choice(g, "opt:invert-flight", r, new[] { "OFF", "ON" }, s.InvertFlightY ? 1 : 0, 150);
        if (finv >= 0 && (finv == 1) != s.InvertFlightY)
        {
            s.InvertFlightY = finv == 1;
            Changed();
        }
        g.Text($"At 1.00x a pixel of mouse turns the view {Game.Game.BaseLook * 180f / Mathf.Pi:F3} degrees; now {_game.LookScale * 180f / Mathf.Pi:F3}.",
            new Vector2(body.Position.X + 16, body.Position.Y + 4 * 90 + 8), C.Dim, 16);
    }

    // ---- Rows and their controls --------------------------------------------------------------

    /// <summary>A row: its name and what it does on the left. Returns where its control goes.</summary>
    static Rect2 Row(Gui g, Rect2 body, int i, string name, string about, float h = RowH)
    {
        float y = body.Position.Y + i * h;
        var row = new Rect2(body.Position.X, y, body.Size.X, h - 6);
        g.Fill(row, new Color(i % 2 == 0 ? C.Panel : C.Glass, 0.6f));
        g.Text(name, new Vector2(row.Position.X + 16, y + h / 2 - 25), C.Text, 19);
        g.Text(about, new Vector2(row.Position.X + 16, y + h / 2 + 1), C.Dim, 14);
        return new Rect2(row.Position.X + LabelW, y + 11, row.Size.X - LabelW - 16, h - 28);
    }

    /// <summary>A row of buttons, the current one lit. The index clicked, or -1.</summary>
    static int Choice(Gui g, string id, Rect2 r, string[] labels, int current, float width)
    {
        int picked = -1;
        for (int i = 0; i < labels.Length; i++)
        {
            var b = new Rect2(r.Position.X + i * (width + 8), r.Position.Y, width, r.Size.Y);
            if (g.Button($"{id}-{labels[i].ToLowerInvariant().Replace(' ', '-')}", b, labels[i], C.Accent2, lit: i == current, size: 18)) picked = i;
        }
        return picked;
    }

    /// <summary>A slider over a range, snapped to a step, its value shown at its right end. True when it moved.</summary>
    static bool Range(Gui g, string id, Rect2 r, ref float value, float min, float max, float step, Func<float, string> shown)
    {
        var track = new Rect2(r.Position, new Vector2(r.Size.X - 160, r.Size.Y));
        float t = Mathf.Clamp((value - min) / (max - min), 0f, 1f);
        bool moved = false;
        if (g.Slider(id, track, ref t, C.Accent2))
        {
            float next = Mathf.Clamp(Mathf.Snapped(min + t * (max - min), step), min, max);
            moved = next != value;
            value = next;
        }
        g.TextIn(shown(value), new Rect2(track.End.X + 24, r.Position.Y, 136, r.Size.Y), C.Text, 21, HorizontalAlignment.Left, UiTheme.Mono);
        return moved;
    }
}
