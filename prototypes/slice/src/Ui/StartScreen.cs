using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DiegeticUi.Theme;
using GalHack.Items;
using Godot;
using PoiGenerator.Gen;
using Slice.Game;
using Palette = DiegeticUi.Theme.Palette;

namespace Slice.Ui;

/// <summary>
/// The new-game screen: a preset (DEFAULT, on the rim at an arm's tip; MID-GALAXY; CORE-WARD;
/// RANDOM) or the options one by one: depth, world type, theme, fall, mid-collapse, the old and
/// current tech eras, world size, mode and seed. Every option's values are the world-layer data's,
/// and any can stay ROLLED, so the roll fills it. Shown only when the command line pins nothing.
/// It is the escape menu's kind of panel (<see cref="MenuPanel"/>) on the same 1600 x 900 canvas.
/// </summary>
public partial class StartScreen : CanvasLayer
{
    /// <summary>Chosen: the world and the mode.</summary>
    public event Action<WorldChoice, GameMode> Chosen;

    public StartRoot Root { get; private set; }

    public override void _Ready()
    {
        Layer = 10;
        var back = new ColorRect { Color = UiTheme.Colors.Glass, MouseFilter = Control.MouseFilterEnum.Ignore };
        back.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(back);
        Root = new StartRoot { Name = "Root", Screen = this };
        AddChild(Root);
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    /// <summary>The seed box takes typed keys while it has the keyboard; Enter starts.</summary>
    public override void _Input(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true } key || !Root.Typing) return;
        Root.Typed(key.Keycode, (char)key.Unicode);
        GetViewport().SetInputAsHandled();
    }

    public void Start()
    {
        (WorldChoice world, GameMode mode) = Root.Choice();
        Chosen?.Invoke(world, mode);
        QueueFree();
    }
}

/// <summary>The new-game panel: presets down the left, the options beside them, WAKE UP below.</summary>
public partial class StartRoot : MenuPanel
{
    static Palette C => UiTheme.Colors;
    public StartScreen Screen;
    public override Vector2 DesignSize => new(Gui.Width, Gui.Height);

    /// <summary>One option: its label and values (shown, and what they stand for); the first is ROLLED wherever the roll can fill it.</summary>
    public sealed class Picker
    {
        public string Id, Label;
        public readonly List<(string Shown, object Value)> Values = new();
        public int At;
        public object Value => Values[At].Value;
        public string Shown => Values[At].Shown;
        public void Step(int by) => At = ((At + by) % Values.Count + Values.Count) % Values.Count;
        public void Set(object value) => At = Math.Max(0, Values.FindIndex(v => Equals(v.Value, value)));
    }

    /// <summary>What ROLLED and RANDOM stand for among a picker's values.</summary>
    public static readonly object Rolled = "rolled", Shuffled = "random";

    public readonly List<Picker> Pickers = new();
    public string Seed = "";
    public bool Typing;

    public Picker Depth, Type, Theme, Fall, Mid, Old, Now, PopOld, PopNow, Size, Mode;

    /// <summary>The presets, as the depth and type they set (everything else rolled).</summary>
    public static readonly (string Id, string Label, object Depth, object Type)[] Presets =
    {
        ("default", "DEFAULT", Rolled, "meadow"),
        ("mid", "MID-GALAXY", 0.5f, "meadow"),
        ("core", "CORE-WARD", 0.9f, "meadow"),
        ("random", "RANDOM", Shuffled, Rolled),
    };

    protected override string Signature() => $"{string.Join(",", Pickers.Select(p => p.At))}|{Seed}|{Typing}|{(Typing && ((int)(Time.GetTicksMsec() / 500) & 1) == 0)}";

    public override void _Ready()
    {
        base._Ready();
        PoiLibrary lib = WorldChoice.Library;
        static string Words(string id) => id.Replace('_', ' ').ToUpperInvariant();
        Picker Add(string id, string label)
        {
            var p = new Picker { Id = id, Label = label };
            Pickers.Add(p);
            return p;
        }
        Depth = Add("depth", "DEPTH");
        Depth.Values.Add(("ROLLED", Rolled));
        Depth.Values.Add(("RANDOM", Shuffled));
        for (int i = 0; i <= 10; i++)
        {
            float d = i / 10f;
            Depth.Values.Add(($"{d.ToString("0.0", CultureInfo.InvariantCulture)}  {GalHack.Stars.Galaxy.Band(d)}", d));
        }
        Type = Add("type", "WORLD TYPE");
        Type.Values.Add(("ROLLED", Rolled));
        foreach (var t in WorldChoice.LayerTypes) Type.Values.Add((Words(t.Id), t.Id));
        Theme = Add("theme", "THEME");
        Theme.Values.Add(("ROLLED", Rolled));
        foreach (string id in WorldForce.Values(lib, LayerKind.Theme)) Theme.Values.Add((Words(id), id));
        Fall = Add("fall", "FALL");
        Fall.Values.Add(("ROLLED", Rolled));
        foreach (string id in WorldForce.Values(lib, LayerKind.Fall)) Fall.Values.Add((Words(id), id));
        Mid = Add("mid", "MID-COLLAPSE");
        Mid.Values.AddRange(new (string, object)[] { ("ROLLED", Rolled), ("YES", true), ("NO", false) });
        Old = Era("old", "OLD TECH ERA");
        Now = Era("now", "CURRENT TECH ERA");
        PopOld = Add("pop-old", "OLD POPULATION");
        PopOld.Values.Add(("ROLLED", Rolled));
        for (int i = 0; i <= 10; i++) PopOld.Values.Add((WorldProfile.PopulationWord(i).ToUpperInvariant(), i));
        PopNow = Add("pop-now", "CURRENT POPULATION");
        PopNow.Values.Add(("ROLLED", Rolled));
        foreach (PopulationNow c in Enum.GetValues<PopulationNow>()) PopNow.Values.Add((c.ToString().ToUpperInvariant(), c));
        Size = Add("size", "WORLD SIZE");
        Size.Values.Add(("ROLLED", Rolled));
        foreach (int w in Planets.Gen.PlanetSpec.Widths) Size.Values.Add(($"{w} x {w / 2} KM", w));
        Mode = Add("mode", "MODE");
        Mode.Values.AddRange(new (string, object)[] { ("SURVIVAL", GameMode.Survival), ("CREATIVE", GameMode.Creative) });
        Preset("default");

        Picker Era(string id, string label)
        {
            Picker era = Add(id, label);
            era.Values.Add(("ROLLED", Rolled));
            foreach (TechLevel t in Enum.GetValues<TechLevel>()) era.Values.Add((WorldProfile.Tag(t).ToUpperInvariant(), t));
            return era;
        }
    }

    /// <summary>Sets the pickers to a preset: its depth and type, everything else rolled (the mode and seed stay).</summary>
    public void Preset(string id)
    {
        var p = Presets.First(q => q.Id == id);
        foreach (Picker k in Pickers) if (k != Mode) k.At = 0;
        Depth.Set(p.Depth);
        Type.Set(p.Type);
    }

    /// <summary>The preset the pickers stand at, or null (custom).</summary>
    public string CurrentPreset
    {
        get
        {
            foreach (var p in Presets)
                if (Equals(Depth.Value, p.Depth) && Equals(Type.Value, p.Type) && Pickers.All(k => k == Depth || k == Type || k == Mode || k.At == 0)) return p.Id;
            return null;
        }
    }

    /// <summary>The world and mode the pickers add up to: a fresh seed unless one is typed, and every ROLLED option left to the roll.</summary>
    public (WorldChoice, GameMode) Choice()
    {
        ulong? seed = string.IsNullOrWhiteSpace(Seed) ? null : WorldChoice.ParseSeed(Seed);
        WorldChoice world = WorldChoice.Fresh(Type.Value is string t && Type.Value != Rolled ? t : "random", seed);
        if (Size.Value is int w) world = world with { WidthKm = w };
        var force = new WorldForce
        {
            Depth = Depth.Value is float d ? d : Depth.Value == Shuffled ? Random.Shared.NextSingle() : null,
            Theme = Theme.Value == Rolled ? null : (string)Theme.Value,
            Fall = Fall.Value == Rolled ? null : (string)Fall.Value,
            MidCollapse = Mid.Value is bool m ? m : null,
            Before = Old.Value is TechLevel o ? o : null,
            Now = Now.Value is TechLevel n ? n : null,
            Population = PopOld.Value is int pop ? pop : null,
            PopulationNow = PopNow.Value is PopulationNow c ? c : null,
        };
        if (!force.Empty) world = world with { Force = force };
        return (world, (GameMode)Mode.Value);
    }

    /// <summary>A key while the seed box has the keyboard: what is typed goes in, Backspace takes one back, Enter starts, Esc lets go.</summary>
    public void Typed(Key key, char c)
    {
        if (key is Key.Enter or Key.KpEnter) Screen?.Start();
        else if (key == Key.Escape) Typing = false;
        else if (key == Key.Backspace) Seed = Seed.Length > 0 ? Seed[..^1] : Seed;
        else if (!char.IsControl(c) && c != '\0' && Seed.Length < 24) Seed += c;
    }

    /// <summary>Tests: one click on a widget by id, through the panel's own input.</summary>
    public void Click(string id)
    {
        if (RectOf(id) == null) Feed(new Vector2(-1e5f, -1e5f), false, false, false, false, false);
        if (RectOf(id) is not Rect2 at) return;
        Feed(at.GetCenter(), pressed: true, released: false, held: true, right: false, shift: false);
        Feed(at.GetCenter(), pressed: false, released: true, held: false, right: false, shift: false);
    }

    protected override void Build(Gui g, Rect2 area)
    {
        g.Fill(area, new Color(C.Glass, 0.6f));
        var header = new Rect2(area.Position, new Vector2(area.Size.X, 74));
        g.Fill(header, C.Accent);
        if (g.Drawing) g.Canvas.Stripes(new Rect2(header.Position + new Vector2(header.Size.X - 300, 0), new Vector2(300, header.Size.Y)), C.Accent, C.Accent2, 16);
        g.Text("NEW GAME", header.Position + new Vector2(24, 14), C.Glass, 30, UiTheme.Display);

        // The presets, down the left, and WAKE UP under them.
        float x = area.Position.X + 20, y = area.Position.Y + 96;
        const float W = 360, H = 66, Gap = 12;
        g.Caption("PRESETS", new Vector2(x, y), W);
        y += 40;
        string current = CurrentPreset;
        foreach (var p in Presets)
        {
            if (g.Button($"start:preset-{p.Id}", new Rect2(x, y, W, H), p.Label, C.Accent2, lit: current == p.Id, size: 24)) Preset(p.Id);
            y += H + Gap;
        }
        g.TextIn("CUSTOM", new Rect2(x, y, W, 30), current == null ? C.Holo : C.Dim, 18, HorizontalAlignment.Center, UiTheme.Display);
        if (g.Button("start:wake", new Rect2(x, area.End.Y - 20 - 80, W, 80), "WAKE UP", C.Good, size: 28) && Screen != null) Screen.Start();

        // The options beside them: a label, and its value between arrows (the wheel steps it too).
        var right = new Rect2(area.Position.X + 410, area.Position.Y + 96, area.Size.X - 430, area.Size.Y - 116);
        g.Caption("OPTIONS", right.Position, right.Size.X);
        float row = right.Position.Y + 40;
        const float Arrow = 56;
        // Every option and the seed fit the panel: rows close up as options are added.
        float RowH = MathF.Min(64, MathF.Floor((right.Size.Y - 40) / (Pickers.Count + 1)));
        float valueX = right.Position.X + 340, valueW = right.Size.X - 340 - 2 * Arrow - 16;
        int i = 0;
        foreach (Picker p in Pickers)
        {
            var band = new Rect2(right.Position.X, row, right.Size.X, RowH - 8);
            g.Fill(band, new Color(i % 2 == 0 ? C.Panel : C.Glass, 0.6f));
            g.Text(p.Label, new Vector2(band.Position.X + 14, row + 17), C.Dim, 18);
            if (g.Button($"start:{p.Id}-prev", new Rect2(valueX, row + 4, Arrow, RowH - 16), "<", C.Accent2, size: 22)) p.Step(-1);
            var value = new Rect2(valueX + Arrow + 8, row + 4, valueW, RowH - 16);
            g.Fill(value, new Color(C.Panel, 0.9f));
            g.TextIn(p.Shown, value.Grow(-8), p.At == 0 && p != Mode ? C.Dim : C.Text, 20, HorizontalAlignment.Center, UiTheme.Mono);
            if (g.Button($"start:{p.Id}-next", new Rect2(value.End.X + 8, row + 4, Arrow, RowH - 16), ">", C.Accent2, size: 22)) p.Step(1);
            if (!g.Drawing && g.Wheel != 0 && g.Over(band)) p.Step(-g.Wheel);
            row += RowH;
            i++;
        }
        var seedBand = new Rect2(right.Position.X, row, right.Size.X, RowH - 8);
        g.Fill(seedBand, new Color(i % 2 == 0 ? C.Panel : C.Glass, 0.6f));
        g.Text("SEED", new Vector2(seedBand.Position.X + 14, row + 17), C.Dim, 18);
        var box = new Rect2(valueX, row + 4, valueW + 2 * Arrow + 16, RowH - 16);
        if (g.TextBox("start:seed", box, Seed, "RANDOM", Typing)) Typing = true;
        else if (!g.Drawing && g.Pressed && !g.Over(box)) Typing = false;
    }
}
