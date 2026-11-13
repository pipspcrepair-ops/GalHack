using System.Collections.Generic;
using System.Linq;
using GalHack.Items;
using GalHack.Structures;
using Godot;
using Manufacturing.Game;

namespace Manufacturing.Panels;

/// <summary>
/// A structure's settings, as one panel of the Tab menu would show them: what the base is, its
/// ammo restock switch and how many boxes of each gun's ammo to keep, survival or creative, and a
/// button to empty spill crates back into storage.
///
/// Written to the panel contract (diegetic-ui's README), so the same class fills a tab or a
/// console's screen with no change: stock Controls in containers that fit any rect, no fonts or
/// colours of its own (the host's theme and its type variations Title, Heading, Caption, Readout
/// dress it), input only through the controls' own signals, and it touches nothing but the model.
/// It reads the model each frame and changes a control only when its value really changed, so a
/// screen showing it redraws only then.
/// </summary>
public partial class StructurePanel : PanelContainer
{
    readonly Workshop _w;
    readonly Dictionary<string, (HSlider slider, Label value)> _thresholds = new();
    Label _name, _summary, _storage;
    int _statsSeen = -1;

    public Button RestockToggle { get; private set; }
    public Button CreativeToggle { get; private set; }
    public Button EmptyCrates { get; private set; }
    public IReadOnlyDictionary<string, HSlider> Thresholds => _thresholds.ToDictionary(kv => kv.Key, kv => kv.Value.slider);

    public StructurePanel(Workshop w)
    {
        _w = w;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;

        var margin = new MarginContainer();
        foreach (string side in new[] { "left", "right" }) margin.AddThemeConstantOverride("margin_" + side, 40);
        foreach (string side in new[] { "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 28);
        AddChild(margin);
        var rows = new VBoxContainer();
        margin.AddChild(rows);

        var title = new HBoxContainer();
        title.AddChild(new Label { Text = "STRUCTURE", ThemeTypeVariation = "Title" });
        title.AddChild(_name = new Label { ThemeTypeVariation = "Heading", SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalAlignment = HorizontalAlignment.Right });
        rows.AddChild(title);
        rows.AddChild(_summary = new Label { ThemeTypeVariation = "Caption" });

        var switches = new GridContainer { Columns = 2 };
        switches.AddThemeConstantOverride("h_separation", 24);
        switches.AddThemeConstantOverride("v_separation", 10);
        RestockToggle = Switch(switches, "AMMO AUTO-RESTOCK", on => _w.Fab.Restock.Enabled = on);
        CreativeToggle = Switch(switches, "CREATIVE MODE (FREE BUILDING)", on =>
        {
            if (on != _w.Rules.Creative) _w.ToggleMode();
        });
        rows.AddChild(switches);

        rows.AddChild(new Label { Text = "KEEP IN STOCK, IN BOXES", ThemeTypeVariation = "Heading" });
        foreach (string ammo in _w.Fab.Restock.InUse)
        {
            var row = new HBoxContainer();
            row.AddChild(new Label { Text = _w.Items.Item(ammo).Name.ToUpperInvariant(), CustomMinimumSize = new Vector2(300, 0) });
            var slider = new HSlider { MinValue = 0, MaxValue = 40, Step = 1, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
            slider.ValueChanged += v => _w.Fab.Restock.SetThreshold(ammo, (int)v);
            row.AddChild(slider);
            var value = new Label { ThemeTypeVariation = "Readout", CustomMinimumSize = new Vector2(80, 0), HorizontalAlignment = HorizontalAlignment.Right };
            row.AddChild(value);
            rows.AddChild(row);
            _thresholds[ammo] = (slider, value);
        }

        var foot = new HBoxContainer { SizeFlagsVertical = SizeFlags.ShrinkEnd | SizeFlags.Expand };
        foot.AddChild(EmptyCrates = new Button { CustomMinimumSize = new Vector2(420, 56) });
        EmptyCrates.Pressed += EmptyAllCrates;
        foot.AddChild(_storage = new Label { ThemeTypeVariation = "Readout", SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalAlignment = HorizontalAlignment.Right });
        rows.AddChild(foot);
        Sync();
    }

    static Button Switch(GridContainer grid, string caption, System.Action<bool> toggled)
    {
        grid.AddChild(new Label { Text = caption, SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var button = new Button { ToggleMode = true, CustomMinimumSize = new Vector2(160, 52) };
        button.Toggled += on =>
        {
            button.Text = on ? "ON" : "OFF";
            toggled(on);
        };
        grid.AddChild(button);
        return button;
    }

    void EmptyAllCrates()
    {
        foreach (SpillCrate crate in _w.Crates.Crates.ToList()) _w.Crates.Collect(crate, _w.Storage);
    }

    public override void _Process(double delta) => Sync();

    /// <summary>Shows the model. Every setter here leaves a control alone when the value is unchanged.</summary>
    void Sync()
    {
        Structure s = _w.Base;
        _name.Text = s.Name.ToUpperInvariant();
        if (s.Version != _statsSeen)
        {
            _statsSeen = s.Version;
            StructureStats st = StructureStats.Compute(s);
            _summary.Text = $"{(st.HasCore ? "BASE, CORE ONLINE" : "NO CORE")}  -  {st.Blocks} BLOCKS  -  {st.Mass:0.0} T  -  {st.Lights} LIGHTS";
        }
        SetToggle(RestockToggle, _w.Fab.Restock.Enabled);
        SetToggle(CreativeToggle, _w.Rules.Creative);
        foreach (var (ammo, (slider, value)) in _thresholds)
        {
            int keep = _w.Fab.Restock.Threshold(ammo, _w.Items);
            slider.SetValueNoSignal(keep);
            value.Text = $"{keep}";
        }
        int crates = _w.Crates.Crates.Count;
        EmptyCrates.Text = crates == 0 ? "NO SPILL CRATES" : $"EMPTY {crates} SPILL CRATE{(crates > 1 ? "S" : "")}";
        EmptyCrates.Disabled = crates == 0;
        _storage.Text = $"{_w.Storage.UsedSu:0} / {_w.Storage.CapacitySu:0} SU";
    }

    static void SetToggle(Button button, bool on)
    {
        button.SetPressedNoSignal(on);
        button.Text = on ? "ON" : "OFF";
    }
}
