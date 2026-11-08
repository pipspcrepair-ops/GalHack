using System;
using System.Linq;
using DiegeticUi.Theme;
using Godot;
using Slice.Game;
using Palette = DiegeticUi.Theme.Palette;

namespace Slice.Ui;

/// <summary>What the escape menu's right side shows: this world, or the options.</summary>
public enum EscapePage { World, Options }

/// <summary>The question the escape menu is asking, if any.</summary>
public enum EscapeQuestion { None, Quit, NewGame }

/// <summary>
/// The escape menu, on Esc with no other menu open: RESUME, SAVE and LOAD (marked NOT YET: the
/// slice has no saves), NEW GAME (it asks first, then tears this world down for the start screen,
/// as at launch: <see cref="Game.Game.NewGame"/>), OPTIONS (graphics, sound, controls and mouse:
/// <see cref="OptionsSection"/>) and QUIT TO DESKTOP, which asks first. The game never pauses (it is built for co-op): the world
/// runs on behind the menu, which is see-through like every menu, so you keep an eye on what is
/// coming. Each tick's key presses and clicks are the menu's (it reads them before the game does),
/// while held movement keys still reach the castaway, as with the Tab menu. The mouse is freed
/// while it is open and taken back on resume. A second Esc closes it, after backing out of a key
/// prompt or the quit question.
///
/// It is the Tab menu's kind of panel (<see cref="MenuPanel"/>) on the same 1600 x 900 canvas:
/// the real mouse clicks it in play, the game's own <see cref="Controls"/> in a test.
/// </summary>
public partial class EscapeMenu : CanvasLayer
{
    public Game.Game Game;
    public EscapeRoot Root { get; private set; }
    public bool IsOpen => Root?.Visible == true;

    /// <summary>Opened, closed, clicks: the menu's sounds play on these.</summary>
    public event Action<string> Sounded;

    /// <summary>Settings are written this long after the last change (a slider drag is many changes), and on closing.</summary>
    const double SaveDelay = 0.5;
    int _savedVersion;
    double _saveIn = -1;

    public override void _Ready()
    {
        Layer = 8; // over the visor (1), the debug readout (2) and the Tab menu (3); under the start screen (10)
        ProcessMode = ProcessModeEnum.Always;
        ProcessPhysicsPriority = -30; // the tick's presses and clicks reach the menu before the game (-20) reads them
        Root = new EscapeRoot { Name = "Root", Game = Game, Menu = this, Visible = false };
        AddChild(Root);
        Root.Sounded += s => Sounded?.Invoke(s);
        _savedVersion = Game.Settings.Version;
    }

    /// <summary>Whether the visor was showing when the menu opened: it is hidden under the menu, so nothing of it reads through.</summary>
    bool _visorWas;

    public void Open()
    {
        if (IsOpen) return;
        Root.Reset();
        Root.Visible = true;
        Game.Controls.EndTick(); // the Esc that opened it is spent
        if (Game.Interactive) Input.MouseMode = Input.MouseModeEnum.Visible;
        if (Game.Visor != null)
        {
            _visorWas = Game.Visor.Visible;
            Game.Visor.Visible = false;
        }
        Sounded?.Invoke("open");
    }

    public void Close()
    {
        if (!IsOpen) return;
        Root.Visible = false;
        Root.Gui.DragId = null;
        Root.Options.EndCapture();
        Game.Controls.EndTick();
        if (Game.Visor != null) Game.Visor.Visible = _visorWas;
        if (Game.Interactive && !Game.Menu.IsOpen && DisplayServer.GetName() != "headless") Input.MouseMode = Input.MouseModeEnum.Captured;
        SaveNow();
        Sounded?.Invoke("close");
    }

    /// <summary>QUIT TO DESKTOP, confirmed: the settings written, and out through wildlife's shutdown (sound drained, caches released). A test's game stays.</summary>
    public void Quit()
    {
        SaveNow();
        if (Game.Interactive) Wildlife.Shutdown.Quit(Game);
    }

    /// <summary>NEW GAME, confirmed: the settings written, this world torn down and the start screen up, as at launch.</summary>
    public void NewGame()
    {
        SaveNow();
        Game.NewGame();
    }

    void SaveNow()
    {
        _saveIn = -1;
        if (_savedVersion == Game.Settings.Version) return;
        _savedVersion = Game.Settings.Version;
        Game.SaveSettings();
    }

    /// <summary>While it is open, the tick's key presses and (from a script) mouse are the menu's: the game, which reads after it, sees none of them.</summary>
    public override void _PhysicsProcess(double delta)
    {
        if (!IsOpen) return;
        Controls c = Game.Controls;
        foreach (Key key in c.Keys.ToList())
        {
            Root.Press(key);
            if (!IsOpen) return; // Esc closed it (and spent the tick)
        }
        if (!Game.Interactive)
            Root.Feed(c.Mouse, c.PrimaryPressed, c.PrimaryReleased, c.Primary, c.SecondaryPressed, c.ModifierHeld(Key.Shift), c.ModifierHeld(Key.Ctrl), c.SecondaryReleased, c.Secondary, c.Wheel);
        c.EndTick();
    }

    public override void _Process(double delta)
    {
        if (Game?.Settings == null) return;
        if (Game.Settings.Version == _savedVersion) return;
        if (_saveIn < 0) _saveIn = SaveDelay;
        else if ((_saveIn -= delta) <= 0) SaveNow();
    }

    /// <summary>In play, a key pressed while the menu is open is the menu's (a key prompt takes any key; Esc backs out).</summary>
    public override void _UnhandledInput(InputEvent e)
    {
        if (!IsOpen || !Game.Interactive || e is not InputEventKey { Pressed: true, Echo: false } key) return;
        Game.Controls.Keys.Add(key.PhysicalKeycode);
        GetViewport().SetInputAsHandled();
    }

    /// <summary>A widget's rectangle by id (design units), from the last input pass.</summary>
    public Rect2? RectOf(string id) => Root.RectOf(id);
}

/// <summary>The escape menu's panel: the header, the column of what you can do, and this world or the options beside it.</summary>
public partial class EscapeRoot : MenuPanel
{
    static Palette C => UiTheme.Colors;
    public Game.Game Game;
    public EscapeMenu Menu;
    public EscapePage Page = EscapePage.World;

    /// <summary>The question up (QUIT TO DESKTOP? or NEW GAME?), or none.</summary>
    public EscapeQuestion Asking;

    /// <summary>QUIT TO DESKTOP was clicked: the question is up.</summary>
    public bool Confirming
    {
        get => Asking == EscapeQuestion.Quit;
        set => Asking = value ? EscapeQuestion.Quit : EscapeQuestion.None;
    }

    /// <summary>VIEW THE WORLD: the menu stands aside (a strip along the top) so a graphics change can be seen; a click or Esc brings it back.</summary>
    public bool Viewing;

    public OptionsSection Options { get; private set; }
    public override Vector2 DesignSize => new(Gui.Width, Gui.Height);

    protected override string Signature() => $"{Page}|{Asking}|{Viewing}|{Options?.Signature}";

    public override void _Ready()
    {
        base._Ready();
        Options = new OptionsSection(Game) { View = () => Viewing = true };
    }

    /// <summary>As it opens: this world showing, no question, no key prompt.</summary>
    public void Reset()
    {
        Page = EscapePage.World;
        Asking = EscapeQuestion.None;
        Viewing = false;
        Options?.EndCapture();
    }

    /// <summary>A key while open: a key prompt takes it (Esc cancels the prompt); otherwise Esc comes back from viewing the world, backs out of the question, or closes the menu.</summary>
    public void Press(Key key)
    {
        if (Options.Capturing != null)
        {
            Options.Captured(key);
            return;
        }
        if (key != Key.Escape) return;
        if (Viewing) Viewing = false;
        else if (Asking != EscapeQuestion.None) Asking = EscapeQuestion.None;
        else Menu.Close();
    }

    protected override void Build(Gui g, Rect2 area)
    {
        if (Game == null) return;
        if (Viewing)
        {
            var strip = new Rect2(area.Position, new Vector2(area.Size.X, 52));
            g.Fill(strip, new Color(C.Glass, 0.85f));
            g.Fill(new Rect2(strip.Position, new Vector2(8, strip.Size.Y)), C.Accent2);
            g.TextIn("VIEWING THE WORLD", strip.Grow(-20), C.Text, 18);
            g.TextIn("CLICK OR ESC TO GO BACK", strip.Grow(-20), C.Dim, 16, HorizontalAlignment.Right);
            if (!g.Drawing && g.Pressed)
            {
                Viewing = false;
                g.Sound = "click";
            }
            return;
        }
        // Under a question nothing is lit or clickable: the pointer is kept off it.
        Vector2 mouse = g.Mouse;
        if (Asking != EscapeQuestion.None) g.Mouse = new Vector2(-1e5f, -1e5f);
        Main(g, area);
        g.Mouse = mouse;
        if (Asking == EscapeQuestion.Quit) Question(g, area);
        else if (Asking == EscapeQuestion.NewGame) NewGameQuestion(g, area);
    }

    void Main(Gui g, Rect2 area)
    {
        // See-through: the world behind it stays in view.
        g.Fill(area, new Color(C.Glass, 0.6f));
        var header = new Rect2(area.Position, new Vector2(area.Size.X, 74));
        g.Fill(header, C.Accent);
        if (g.Drawing) g.Canvas.Stripes(new Rect2(header.Position + new Vector2(header.Size.X - 300, 0), new Vector2(300, header.Size.Y)), C.Accent, C.Accent2, 16);
        g.Text(Page == EscapePage.Options ? "OPTIONS" : "MENU", header.Position + new Vector2(24, 14), C.Glass, 30, UiTheme.Display);
        if (Game.World is WorldChoice w)
            g.TextIn($"{w.Name.ToUpperInvariant()}   SEED {w.Seed}", new Rect2(header.Position.X + 620, header.Position.Y + 20, 640, 34), C.Glass, 18, HorizontalAlignment.Right, UiTheme.Mono);

        // What you can do, down the left.
        float x = area.Position.X + 20, y = area.Position.Y + 96;
        const float W = 360, H = 66, Gap = 12;
        Rect2 Next()
        {
            var r = new Rect2(x, y, W, H);
            y += H + Gap;
            return r;
        }
        if (g.Button("esc:resume", Next(), "RESUME", C.Accent2, size: 24)) Menu.Close();
        NotYet(g, "esc:save", Next(), "SAVE");
        NotYet(g, "esc:load", Next(), "LOAD");
        if (g.Button("esc:new", Next(), "NEW GAME", C.Warn, size: 24)) Asking = EscapeQuestion.NewGame;
        if (g.Button("esc:options", Next(), "OPTIONS", C.Accent2, lit: Page == EscapePage.Options, size: 24))
        {
            Page = Page == EscapePage.Options ? EscapePage.World : EscapePage.Options;
            Options.EndCapture();
        }
        if (g.Button("esc:quit", Next(), "QUIT TO DESKTOP", C.Danger, size: 24)) Confirming = true;

        var right = new Rect2(area.Position.X + 410, area.Position.Y + 96, area.Size.X - 430, area.Size.Y - 116);
        if (Page == EscapePage.Options) Options.Build(g, right);
        else WorldCard(g, right);
    }

    /// <summary>A button for something not in the game yet: greyed, with the marker on it.</summary>
    static void NotYet(Gui g, string id, Rect2 r, string label)
    {
        g.Button(id, r, label, C.Dim, enabled: false, size: 24);
        g.TextIn("NOT YET", new Rect2(r.End.X - 110, r.Position.Y + 6, 100, 22), C.Warn, 14, HorizontalAlignment.Right);
    }

    /// <summary>This world: what it is, how long you have been here, and the keys that matter most.</summary>
    void WorldCard(Gui g, Rect2 area)
    {
        g.Caption("THIS WORLD", area.Position, area.Size.X);
        WorldChoice w = Game.World;
        var rows = new (string, string)[]
        {
            ("WORLD", w?.Name ?? "-"),
            ("SEED", w != null ? $"{w.Seed}" : "-"),
            ("DEPTH", Game.Zone?.Planet is { Depth: >= 0f } p ? $"{p.Depth:0.00}  {GalHack.Stars.Galaxy.Band(p.Depth)}" : "-"),
            ("MODE", Game.Mode == GalHack.Items.GameMode.Creative ? "Creative" : "Survival"),
            ("TIME HERE", TimeSpan.FromSeconds(Game.Stats.Seconds).ToString(@"h\:mm\:ss")),
            ("LEVEL", Game.Progress != null ? $"{Game.Progress.Level}  ({Game.Progress.Xp} XP)" : "-"),
        };
        float y = area.Position.Y + 40;
        for (int i = 0; i < rows.Length; i++, y += 46)
        {
            g.Fill(new Rect2(area.Position.X, y, area.Size.X, 40), new Color(i % 2 == 0 ? C.Panel : C.Glass, 0.6f));
            g.Text(rows[i].Item1, new Vector2(area.Position.X + 14, y + 9), C.Dim, 18);
            g.Text(rows[i].Item2, new Vector2(area.Position.X + 340, y + 7), C.Text, 20, UiTheme.Mono);
        }

        y += 24;
        g.Caption("KEYS", new Vector2(area.Position.X, y), area.Size.X);
        KeyBindings keys = Game.Settings.Keys;
        string[] shown = { "terminal", "map", "use", "seat", "tool-mode", "torch", "helmet", "view", "debug" };
        float col = (area.Size.X - 20) / 3;
        for (int i = 0; i < shown.Length; i++)
        {
            KeyAction a = KeyBindings.Action(shown[i]);
            var at = new Vector2(area.Position.X + 14 + (i % 3) * col, y + 40 + (i / 3) * 40);
            var key = new Rect2(at, new Vector2(110, 32));
            g.Fill(key, new Color(C.Panel, 0.9f));
            g.TextIn(KeyBindings.Name(keys[a.Id]), key, C.Accent2, 17, HorizontalAlignment.Center, UiTheme.Mono);
            g.Text(a.Name, at + new Vector2(124, 5), C.Text, 18);
        }
    }

    /// <summary>QUIT TO DESKTOP? With no saves, the answer loses the world: the question marks that, with its seed.</summary>
    void Question(Gui g, Rect2 area)
    {
        g.Fill(area, new Color(C.Glass, 0.55f));
        var box = new Rect2(area.GetCenter() - new Vector2(420, 160), new Vector2(840, 320));
        g.Fill(box, C.Glass);
        g.Stripes(box, 12, C.Danger);
        var inner = box.Grow(-36);
        g.Text("QUIT TO DESKTOP?", inner.Position + new Vector2(4, 6), C.Text, 30, UiTheme.Display);
        string seed = Game.World != null ? $"{Game.World.Seed}" : "-";
        g.Text("NO SAVES YET", inner.Position + new Vector2(4, 72), C.Warn, 20);
        g.Text($"SEED {seed}", inner.Position + new Vector2(4, 104), C.Text, 20, UiTheme.Mono);
        var buttons = new Rect2(inner.Position.X, inner.End.Y - 60, inner.Size.X, 60);
        if (g.Button("esc:quit-yes", new Rect2(buttons.Position, new Vector2(360, 60)), "QUIT", C.Danger, size: 24)) Menu.Quit();
        if (g.Button("esc:quit-no", new Rect2(buttons.End.X - 360, buttons.Position.Y, 360, 60), "STAY", C.Accent2, size: 24)) Confirming = false;
    }

    /// <summary>NEW GAME? YES tears this world down for the start screen; NO keeps it.</summary>
    void NewGameQuestion(Gui g, Rect2 area)
    {
        g.Fill(area, new Color(C.Glass, 0.55f));
        var box = new Rect2(area.GetCenter() - new Vector2(420, 110), new Vector2(840, 220));
        g.Fill(box, C.Glass);
        g.Stripes(box, 12, C.Warn);
        var inner = box.Grow(-36);
        g.Text("NEW GAME?", inner.Position + new Vector2(4, 6), C.Text, 30, UiTheme.Display);
        var buttons = new Rect2(inner.Position.X, inner.End.Y - 60, inner.Size.X, 60);
        if (g.Button("esc:new-yes", new Rect2(buttons.Position, new Vector2(360, 60)), "YES", C.Warn, size: 24)) Menu.NewGame();
        if (g.Button("esc:new-no", new Rect2(buttons.End.X - 360, buttons.Position.Y, 360, 60), "NO", C.Accent2, size: 24)) Asking = EscapeQuestion.None;
    }
}
