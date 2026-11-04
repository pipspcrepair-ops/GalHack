using System;
using System.Collections.Generic;
using System.Text;
using Godot;

namespace Wildlife;

/// <summary>
/// A deliberately minimal debug HUD: status text, the hold's contents, a crosshair, a red
/// flash when something lands a hit, and a small codex card on each first discovery. The real
/// diegetic UI is another prototype's job.
/// </summary>
public partial class Hud : CanvasLayer
{
    Label _text, _hold, _hint;
    PanelContainer _card;
    Label _cardTitle, _cardBody;
    ColorRect _hurt;
    float _cardTime, _hurtTime;
    readonly Queue<CodexEntry> _pending = new();
    public bool ShowHelp = true;

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

        // The codex card: a cream slip with an orange band, top centre.
        _card = new PanelContainer { Visible = false };
        _card.AnchorLeft = _card.AnchorRight = 0.5f;
        _card.OffsetLeft = -230f;
        _card.OffsetRight = 230f;
        _card.OffsetTop = 18f;
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.95f, 0.89f, 0.75f, 0.94f),
            BorderColor = new Color("e8641e"),
            BorderWidthTop = 8,
            ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 10, ContentMarginBottom = 12,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
        };
        _card.AddThemeStyleboxOverride("panel", style);
        var box = new VBoxContainer();
        _cardTitle = new Label();
        _cardTitle.AddThemeFontSizeOverride("font_size", 24);
        _cardTitle.AddThemeColorOverride("font_color", new Color("2a1a10"));
        _cardBody = new Label();
        _cardBody.AddThemeFontSizeOverride("font_size", 15);
        _cardBody.AddThemeColorOverride("font_color", new Color("4a3020"));
        box.AddChild(_cardTitle);
        box.AddChild(_cardBody);
        _card.AddChild(box);
        AddChild(_card);
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

    /// <summary>Screenshots hide the debug text but keep the codex card.</summary>
    public bool StatusVisible
    {
        set
        {
            _text.Visible = value;
            _hold.Visible = value;
            _hint.Visible = value;
            foreach (Node n in GetChildren())
                if (n is ColorRect { Color.A: > 0.5f } crosshair) crosshair.Visible = value;
        }
    }

    public void SetStatus(string text) => _text.Text = text;
    public void SetHint(string text) => _hint.Text = text;

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

    public void Discovered(CodexEntry entry) => _pending.Enqueue(entry);

    /// <summary>Shows a codex card at once (screenshots).</summary>
    public void ShowCard(CodexEntry entry, string detail)
    {
        _cardTitle.Text = $"NEW SPECIES  -  {entry.Name}";
        _cardBody.Text = $"{Capitalise(entry.CommonName)}\n{detail}\nFirst found by {entry.Finder} on {entry.Planet}";
        _card.Visible = true;
        _card.Modulate = Colors.White;
        _cardTime = 5f;
    }

    public Func<CodexEntry, string> Describe = _ => "";

    public void ClearCards()
    {
        _pending.Clear();
        _cardTime = 0f;
        _card.Visible = false;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (_cardTime <= 0f && _pending.Count > 0)
        {
            CodexEntry e = _pending.Dequeue();
            ShowCard(e, Describe(e));
        }
        if (_cardTime > 0f)
        {
            _cardTime -= dt;
            _card.Modulate = new Color(1f, 1f, 1f, Math.Clamp(_cardTime / 0.6f, 0f, 1f));
            if (_cardTime <= 0f) _card.Visible = false;
        }
        _hurtTime = MathF.Max(0f, _hurtTime - dt * 1.8f);
        _hurt.Color = new Color(0.8f, 0.05f, 0.05f, _hurtTime * 0.35f);
    }

    static string Capitalise(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
