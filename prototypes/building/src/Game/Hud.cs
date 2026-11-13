using System.Collections.Generic;
using System.Linq;
using GalHack.Structures;
using GalHack.Structures.Presentation;
using Godot;

namespace BlockBuilding;

/// <summary>
/// The build HUD, styled like a paperback cover's lettering: a stats panel for the structure in
/// view (mass, power, thrust-to-mass, storage, air), the hotbar, the tool line,
/// a hint under the crosshair and toasts for anything that just happened.
/// </summary>
public partial class Hud : CanvasLayer
{
    public static readonly Color Info = new("9ff3e8"), Good = new("8dff9a"), Warn = new("ffc145"),
        Bad = new("ff6650"), Muted = new("f1e6cc");
    static readonly Color Ink = new("f6ead0"), Panel = new(0.06f, 0.07f, 0.11f, 0.84f), Orange = new("e85d04"),
        Dim = new(0.96f, 0.92f, 0.82f, 0.55f);

    public BuildController Build;

    Font _font, _bold;
    Label _tool, _hint, _toast, _help, _stats, _statsTitle, _visorTag;
    ColorRect _visor;
    readonly List<(PanelContainer panel, Label name, ColorRect chip, Label number)> _slots = new();
    float _toastTime;
    bool _helpOn = true;
    RoomReport _rooms;
    StructureView _roomsFor;
    int _roomsVersion = -1;
    double _roomsAt;

    public override void _Ready()
    {
        _font = new SystemFont { FontNames = new[] { "Bahnschrift", "DIN Alternate", "Segoe UI", "Arial" } };
        _bold = new SystemFont { FontNames = new[] { "Bahnschrift", "DIN Alternate", "Segoe UI", "Arial" }, FontWeight = 700 };

        _visor = new ColorRect { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _visor.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _visor.Material = new ShaderMaterial { Shader = new Shader { Code = VisorCode } };
        AddChild(_visor);

        BuildStatsPanel();
        BuildHotbar();

        _tool = MakeLabel(18, Ink, bold: true);
        _tool.Position = new Vector2(22, 18);
        AddChild(_tool);

        var crosshair = new Crosshair { MouseFilter = Control.MouseFilterEnum.Ignore };
        crosshair.SetAnchorsPreset(Control.LayoutPreset.Center);
        AddChild(crosshair);

        _hint = MakeLabel(17, Muted);
        _hint.HorizontalAlignment = HorizontalAlignment.Center;
        _hint.SetAnchorsPreset(Control.LayoutPreset.Center);
        _hint.OffsetLeft = -400;
        _hint.OffsetRight = 400;
        _hint.OffsetTop = 26;
        _hint.OffsetBottom = 60;
        AddChild(_hint);

        _toast = MakeLabel(22, Good, bold: true);
        _toast.HorizontalAlignment = HorizontalAlignment.Center;
        _toast.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _toast.OffsetLeft = -500;
        _toast.OffsetRight = 500;
        _toast.OffsetTop = -178;
        _toast.OffsetBottom = -146;
        AddChild(_toast);

        _visorTag = MakeLabel(15, new Color("7ff7e6"), bold: true);
        _visorTag.Text = "VISOR  ·  AIR ANALYSIS";
        _visorTag.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _visorTag.OffsetLeft = -120;
        _visorTag.OffsetTop = 14;
        _visorTag.Visible = false;
        AddChild(_visorTag);

        // Controls sit under the tool line, clear of the hotbar and the stats panel.
        _help = MakeLabel(13, Dim);
        _help.Position = new Vector2(22, 72);
        AddChild(_help);
    }

    public void ToggleHelp() => _helpOn = !_helpOn;

    public void Toast(string text, Color color)
    {
        if (string.IsNullOrEmpty(text)) return;
        _toast.Text = text;
        _toast.Modulate = color;
        _toastTime = 2.6f;
    }

    public override void _Process(double delta)
    {
        _toastTime -= (float)delta;
        _toast.Visible = _toastTime > 0;
        if (_toastTime < 0.5f) _toast.SelfModulate = new Color(1, 1, 1, Mathf.Max(0, _toastTime / 0.5f));
        else _toast.SelfModulate = Colors.White;

        UpdateTool();
        UpdateHotbar();
        UpdateStats();
        _hint.Text = Build.Hint;
        _hint.Modulate = Build.HintColor;
        _visor.Visible = _visorTag.Visible = Build.OverlayOn;
        _help.Visible = _helpOn;
        _help.Text = Build.Mode == BuildController.Tool.Build ? BuildHelp : PaintHelp;
    }

    // ---- Tool line ------------------------------------------------------------------------

    void UpdateTool()
    {
        string mirror = string.Join(" ", new[] { "X", "Y", "Z" }.Where((_, a) => Build.SymmetryState[a] != 0)
            .Select((n, _) => n));
        string sym = mirror.Length > 0 ? $"   MIRROR {mirror}" : "";
        if (Build.Mode == BuildController.Tool.Build)
        {
            BlockType t = Build.SelectedType;
            string shape = "";
            _tool.Text = $"MULTI-TOOL   {BuildController.Describe(t, Build.SelectedShape).ToUpperInvariant()}{shape}   ·   facing {Orientations.Name(Build.Rot)}{sym}\n" +
                         $"salvage: {InventoryText()}";
        }
        else
        {
            Paint p = Build.CurrentPaint;
            string stencil = p.Stencil == 0 ? "none" : ((char)p.Stencil).ToString();
            _tool.Text = $"PAINT SPRAYER   {BuildController.PaintColorNames[Build.PaintBase].ToUpperInvariant()}" +
                         $"   ·   {p.Pattern.ToString().ToLowerInvariant()} in {BuildController.PaintColorNames[Build.PaintAccent].ToLowerInvariant()}" +
                         (p.IsLamp ? $"   ·   GLOWING {p.Shape.ToString().ToUpperInvariant()} {p.Lamp.Value.Anim.ToString().ToUpperInvariant()}  (L shape, Shift+L mode)" : "") +
                         $"   ·   stencil {stencil}   ·   grime {p.Grime}{sym}";
        }
    }

    string InventoryText()
    {
        var items = Build.Inventory.Items.Where(kv => kv.Value > 0).Select(kv => $"{kv.Value} {kv.Key}").ToList();
        return items.Count == 0 ? "nothing yet" : string.Join(", ", items);
    }

    // ---- Hotbar ---------------------------------------------------------------------------

    void BuildHotbar()
    {
        var bar = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        bar.AddThemeConstantOverride("separation", 6);
        bar.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        bar.OffsetLeft = -430;
        bar.OffsetRight = 430;
        bar.OffsetTop = -112;
        bar.OffsetBottom = -24;
        AddChild(bar);
        for (int i = 0; i < 9; i++)
        {
            var panel = new PanelContainer { CustomMinimumSize = new Vector2(88, 84) };
            var box = new VBoxContainer();
            box.AddThemeConstantOverride("separation", 2);
            var number = MakeLabel(14, Dim, bold: true);
            var chip = new ColorRect { CustomMinimumSize = new Vector2(0, 30) };
            var name = MakeLabel(13, Ink);
            name.HorizontalAlignment = HorizontalAlignment.Center;
            name.AutowrapMode = TextServer.AutowrapMode.Off;
            name.ClipText = true;
            box.AddChild(number);
            box.AddChild(chip);
            box.AddChild(name);
            panel.AddChild(box);
            bar.AddChild(panel);
            _slots.Add((panel, name, chip, number));
        }
    }

    void UpdateHotbar()
    {
        Catalogue cat = Build.Main.Catalogue;
        bool paint = Build.Mode == BuildController.Tool.Paint;
        for (int i = 0; i < 9; i++)
        {
            var (panel, name, chip, number) = _slots[i];
            bool selected;
            if (paint)
            {
                chip.Color = ChunkMesher.Rgb(BuildController.PaintColors[i]);
                name.Text = BuildController.PaintColorNames[i];
                number.Text = i == Build.PaintAccent ? $"{i + 1}   ACCENT" : $"{i + 1}";
                selected = i == Build.PaintBase;
            }
            else
            {
                string[] variants = BuildController.Slots[i];
                BlockType t = cat.Type(variants[Build.Variant[i] % variants.Length]);
                chip.Color = ChunkMesher.Rgb(t.IsLight ? t.GlowColor : t.Color);
                name.Text = t.Name.Replace(" (placeholder)", "").Replace("Hardened steel", "Hardened").Replace("Combat steel", "Combat");
                number.Text = variants.Length > 1 ? $"{i + 1}   {Build.Variant[i] % variants.Length + 1}/{variants.Length}" : $"{i + 1}";
                selected = i == Build.Slot;
            }
            panel.AddThemeStyleboxOverride("panel", SlotStyle(selected));
        }
    }

    StyleBoxFlat _slotOn, _slotOff;

    StyleBoxFlat SlotStyle(bool selected)
    {
        _slotOn ??= Box(new Color(0.1f, 0.1f, 0.15f, 0.92f), Orange, 3, 8);
        _slotOff ??= Box(new Color(0.05f, 0.06f, 0.09f, 0.72f), new Color(1, 1, 1, 0.12f), 1, 8);
        return selected ? _slotOn : _slotOff;
    }

    // ---- Stats panel ----------------------------------------------------------------------

    void BuildStatsPanel()
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", Box(Panel, Orange, 0, 16, topBorder: 5));
        panel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        panel.OffsetLeft = -392;
        panel.OffsetRight = -18;
        panel.OffsetTop = 18;
        AddChild(panel);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        panel.AddChild(box);
        _statsTitle = MakeLabel(22, Ink, bold: true);
        box.AddChild(_statsTitle);
        _stats = MakeLabel(15, Ink);
        box.AddChild(_stats);
    }

    void UpdateStats()
    {
        StructureView view = Build.Active;
        if (view == null || !IsInstanceValid(view) || view.Stats == null)
        {
            _statsTitle.Text = "NO STRUCTURE";
            _stats.Text = "Look at a structure, or place a core (9)\non the pad to start one.";
            return;
        }
        Structure s = view.Structure;
        StructureStats st = view.Stats;
        RoomReport rooms = RoomsFor(view);

        string mode = s.IsWreck ? "WRECK" : s.Mode.ToString().ToUpperInvariant();
        _statsTitle.Text = $"{s.Name.ToUpperInvariant()}   [{mode}]";
        string owner = s.Owner ?? "unowned";
        string power = st.Generators > 0 ? $"{st.PowerOut:0.#} MW made   ·   {st.PowerUse:0.#} MW peak use" : "no generator";
        string thrust = s.Mode == CoreMode.Base && !s.IsWreck
            ? "bases don't fly (B for capital)"
            : st.Thrusters == 0 ? "no thrusters"
            : $"{st.ThrustToWeightForward:0.00} g forward   ·   best axis {st.ThrustToWeightMax:0.00} g";
        string air = rooms == null ? "…"
            : $"{rooms.Sealed.Count()} sealed   ·   {rooms.Leaking.Count()} leaking{(rooms.LeakFaces.Any() ? $" ({rooms.LeakFaces.Count()} holes)" : "")}";
        string gravity = !st.HasCore ? "none" : !s.GravityOn ? "off (E on the core)" : st.GravityActive ? "on" : "on, but unpowered";

        _stats.Text =
            $"{st.Blocks:N0} blocks   ·   {owner}\n" +
            $"MASS      {st.Mass:N1} t{(st.CargoMass > 0 ? $"  (cargo {st.CargoMass:N1} t)" : "")}\n" +
            $"POWER     {power}\n" +
            $"THRUST    {thrust}\n" +
            $"STORAGE   {st.StorageCapacity:N0} SU shared   ·   {st.Containers} containers{(st.StorageRaw > st.StorageCapacity ? "  (at cap)" : "")}\n" +
            $"AIR       {air}\n" +
            $"GRAVITY   {gravity}   ·   LIFE SUPPORT {(st.LifeSupport ? "on" : "off")}";
    }

    RoomReport RoomsFor(StructureView view)
    {
        if (Build.OverlayOn && Build.Rooms != null && Build.Active == view) return Build.Rooms;
        double now = Time.GetTicksMsec() / 1000.0;
        if (view != _roomsFor || (view.Structure.Version != _roomsVersion && now - _roomsAt > 0.5))
        {
            _rooms = Rooms.Detect(view.Structure);
            _roomsFor = view;
            _roomsVersion = view.Structure.Version;
            _roomsAt = now;
        }
        return _rooms;
    }

    // ---- Styling helpers ------------------------------------------------------------------

    Label MakeLabel(int size, Color color, bool bold = false)
    {
        var label = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontOverride("font", bold ? _bold : _font);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.8f));
        label.AddThemeConstantOverride("outline_size", 5);
        return label;
    }

    static StyleBoxFlat Box(Color bg, Color border, int borderWidth, int margin, int topBorder = -1)
    {
        var box = new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = border,
            BorderWidthLeft = borderWidth,
            BorderWidthRight = borderWidth,
            BorderWidthBottom = borderWidth,
            BorderWidthTop = topBorder >= 0 ? topBorder : borderWidth,
            CornerRadiusTopLeft = 3,
            CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3,
            CornerRadiusBottomRight = 3,
        };
        box.SetContentMarginAll(margin);
        return box;
    }

    const string BuildHelp =
        "LMB place   ·   RMB remove   ·   MMB pick block   ·   1-9 block (again: variant)   ·   wheel / Tab shape\n" +
        "R T Y rotate (Shift back)   ·   X V Z mirror (again: between cells)   ·   U repair / upgrade armour\n" +
        "E door, light, core gravity   ·   B core mode   ·   O air visor\n" +
        "K blaster   ·   P paint sprayer   ·   F5 / F9 save / load blueprint   ·   N fly   ·   F1 hide help";

    const string PaintHelp =
        "LMB spray face (hold to sweep, Shift whole block)   ·   RMB strip to bare metal   ·   MMB pick paint\n" +
        "1-9 colour   ·   Shift 1-9 accent   ·   wheel / Tab pattern   ·   [ ] stencil digit   ·   - = grime\n" +
        "X V Z mirror   ·   O air visor   ·   P back to the multi-tool   ·   F1 hide help";

    const string VisorCode = @"
shader_type canvas_item;
void fragment() {
    vec2 uv = SCREEN_UV - 0.5;
    float vignette = smoothstep(0.35, 0.75, length(uv * vec2(1.6, 1.0)));
    float scan = 0.5 + 0.5 * sin(SCREEN_UV.y * 900.0 + TIME * 4.0);
    COLOR = vec4(0.2, 0.95, 0.85, vignette * 0.2 + scan * 0.012);
}
";
}

/// <summary>A small cross with a gap, drawn rather than textured.</summary>
public partial class Crosshair : Control
{
    public override void _Draw()
    {
        var c = new Color(1f, 0.96f, 0.86f, 0.9f);
        foreach (Vector2 d in new[] { Vector2.Right, Vector2.Left, Vector2.Up, Vector2.Down })
            DrawLine(d * 5f, d * 13f, c, 2f, true);
        DrawCircle(Vector2.Zero, 1.6f, c);
    }
}

