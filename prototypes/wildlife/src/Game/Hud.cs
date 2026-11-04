using System;
using System.Collections.Generic;
using System.Text;
using Godot;

namespace Wildlife;

/// <summary>
/// A deliberately minimal debug HUD: status text, the hold's contents, a crosshair, a red
/// flash when something lands a hit, and a scan readout while you hold Q on a creature. The
/// real diegetic UI is another prototype's job.
///
/// Meeting a creature for the first time shows nothing at all: no card, no banner, no sound.
/// The codex records sightings silently; names only appear when you ask (the scan).
/// </summary>
public partial class Hud : CanvasLayer
{
    Label _text, _hold, _hint, _scan;
    ColorRect _hurt;
    float _hurtTime;

    public override void _Ready()
    {
        _text = MakeLabel(new Vector2(16f, 12f), 16, new Color("ffe8c2"));
        _hold = MakeLabel(new Vector2(16f, 0f), 18, new Color("ffd27a"));
        _hold.AnchorTop = _hold.AnchorBottom = 1f;
        _hold.OffsetTop = -120f;
        _hint = MakeLabel(Vector2.Zero, 18, new Color("ffffff"));
        _hint.AnchorLeft = _hint.AnchorRight = 0.5f;
        _hint.AnchorTop = _hint.AnchorBottom = 0.5f;
        _hint.OffsetLeft = -200f;
        _hint.OffsetRight = 200f;
        _hint.OffsetTop = 30f;
        _hint.HorizontalAlignment = HorizontalAlignment.Center;

        _hurt = new ColorRect { Color = new Color(0.8f, 0.05f, 0.05f, 0f), MouseFilter = Control.MouseFilterEnum.Ignore };
        _hurt.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_hurt);

        var crosshair = new ColorRect
        {
            Color = new Color(1f, 1f, 1f, 0.85f),
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = -2f, OffsetRight = 2f, OffsetTop = -2f, OffsetBottom = 2f,
        };
        AddChild(crosshair);

        // The scan readout sits under the crosshair and only shows while you hold Q on something.
        _scan = MakeLabel(Vector2.Zero, 17, new Color("d8f0e8"));
        _scan.AnchorLeft = _scan.AnchorRight = 0.5f;
        _scan.AnchorTop = _scan.AnchorBottom = 0.5f;
        _scan.OffsetLeft = -260f;
        _scan.OffsetRight = 260f;
        _scan.OffsetTop = 64f;
        _scan.HorizontalAlignment = HorizontalAlignment.Center;
    }

    Label MakeLabel(Vector2 at, int size, Color color)
    {
        var l = new Label { Position = at };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        l.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
        l.AddThemeConstantOverride("outline_size", 6);
        AddChild(l);
        return l;
    }

    /// <summary>Screenshots hide the debug text.</summary>
    public bool StatusVisible
    {
        set
        {
            _text.Visible = value;
            _hold.Visible = value;
            _hint.Visible = value;
            _scan.Visible = value;
            foreach (Node n in GetChildren())
                if (n is ColorRect { Color.A: > 0.5f } crosshair) crosshair.Visible = value;
        }
    }

    public void SetStatus(string text) => _text.Text = text;
    public void SetHint(string text) => _hint.Text = text;

    /// <summary>What the scan says about the creature under the crosshair; empty hides it.</summary>
    public void SetScan(string text) => _scan.Text = text;

    /// <summary>Every label the HUD can show, for the test that checks first sightings stay silent.</summary>
    public IEnumerable<Label> Labels => new[] { _text, _hold, _hint, _scan };

    public void SetHold(Storage hold, Storage pack, bool driving)
    {
        var sb = new StringBuilder();
        sb.Append($"HOLD {hold.Total}/{hold.Capacity}{(hold.Full ? "  FULL" : "")}   ");
        sb.Append($"wood {hold.Count(Items.Wood)}   organics {hold.Count(Items.Organics)}   meat {hold.Count(Items.Meat)}");
        foreach (var (item, n) in hold.Items)
            if (item is not (Items.Wood or Items.Organics or Items.Meat)) sb.Append($"   {item} x{n}");
        if (pack.Total > 0)
        {
            sb.Append($"\nPACK {pack.Total}/{pack.Capacity}   meat {pack.Count(Items.Meat)}");
            foreach (var (item, n) in pack.Items)
                if (item != Items.Meat) sb.Append($"   {item} x{n}");
        }
        _hold.Text = sb.ToString();
        _hold.Modulate = new Color(1f, 1f, 1f, driving || pack.Total > 0 ? 1f : 0.7f);
    }

    public void Hurt(float amount)
    {
        _hurtTime = Math.Clamp(amount / 25f, 0.3f, 1f);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _hurtTime = MathF.Max(0f, _hurtTime - dt * 1.8f);
        _hurt.Color = new Color(0.8f, 0.05f, 0.05f, _hurtTime * 0.35f);
    }
}
